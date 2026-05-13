using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Avalonia.Remote.Protocol;
using Avalonia.Remote.Protocol.Designer;
using Avalonia.Remote.Protocol.Viewport;

namespace XamlPlayground.Services;

public sealed class WorkspaceRemotePreviewService : IDisposable
{
    private Process? _process;
    private RemotePreviewTcpSession? _session;
    private string? _targetAssemblyPath;

    public event Action<FrameMessage>? FrameReceived;

    public event Action<string>? ErrorReceived;

    public async Task<WorkspaceRemotePreviewResult> StartOrUpdateAsync(
        string xaml,
        string targetAssemblyPath,
        string xamlFileProjectPath,
        string? workingDirectory)
    {
        if (!File.Exists(targetAssemblyPath))
        {
            return WorkspaceRemotePreviewResult.Fail("Project output assembly not found. Build the project first.");
        }

        var hostPath = Path.Combine(AppContext.BaseDirectory, "XamlPlayground.PreviewerHost.dll");
        if (!File.Exists(hostPath))
        {
            return WorkspaceRemotePreviewResult.Fail("Workspace preview host not found. Build the desktop app first.");
        }

        if (_process is null ||
            _process.HasExited ||
            _session is null ||
            !string.Equals(_targetAssemblyPath, targetAssemblyPath, StringComparison.OrdinalIgnoreCase))
        {
            StopCurrentProcess();
            _targetAssemblyPath = targetAssemblyPath;
            _session = new RemotePreviewTcpSession();
            _session.FrameReceived += frame => FrameReceived?.Invoke(frame);
            _session.ErrorReceived += error => ErrorReceived?.Invoke(error);

            var startInfo = BuildStartInfo(
                hostPath,
                targetAssemblyPath,
                _session.Port,
                workingDirectory);
            try
            {
                _process = new Process
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };
                _process.Exited += (_, _) =>
                {
                    var exitCode = _process?.ExitCode;
                    if (exitCode is not 0 and not null)
                    {
                        ErrorReceived?.Invoke($"Workspace preview host exited with code {exitCode}.");
                    }
                };
                _process.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        ErrorReceived?.Invoke(e.Data);
                    }
                };
                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
            }
            catch (Exception exception)
            {
                StopCurrentProcess();
                return WorkspaceRemotePreviewResult.Fail($"Workspace preview host failed to start: {exception.Message}");
            }
        }

        await _session.SendUpdateXamlAsync(xaml, targetAssemblyPath, xamlFileProjectPath);
        return WorkspaceRemotePreviewResult.Success();
    }

    public void Dispose()
    {
        StopCurrentProcess();
    }

    private void StopCurrentProcess()
    {
        _session?.Dispose();
        _session = null;

        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }

        _process?.Dispose();
        _process = null;
    }

    private static ProcessStartInfo BuildStartInfo(
        string hostPath,
        string targetAssemblyPath,
        int port,
        string? workingDirectory)
    {
        var targetDirectory = Path.GetDirectoryName(targetAssemblyPath) ?? AppContext.BaseDirectory;
        var baseName = Path.GetFileNameWithoutExtension(targetAssemblyPath);
        var targetRuntimeConfig = Path.Combine(targetDirectory, baseName + ".runtimeconfig.json");
        var targetDeps = Path.Combine(targetDirectory, baseName + ".deps.json");
        var hostRuntimeConfig = Path.ChangeExtension(hostPath, ".runtimeconfig.json");
        var hostDeps = Path.ChangeExtension(hostPath, ".deps.json");
        var runtimeConfig = File.Exists(hostRuntimeConfig) ? hostRuntimeConfig : targetRuntimeConfig;
        var depsFile = File.Exists(hostDeps) ? hostDeps : targetDeps;
        var args = string.Join(
            " ",
            new[]
            {
                "exec",
                File.Exists(runtimeConfig) ? $"--runtimeconfig \"{runtimeConfig}\"" : null,
                File.Exists(depsFile) ? $"--depsfile \"{depsFile}\"" : null,
                $"\"{hostPath}\"",
                $"--transport tcp-bson://127.0.0.1:{port}/",
                $"\"{targetAssemblyPath}\""
            }.Where(static value => !string.IsNullOrWhiteSpace(value)));

        return new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = args,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? targetDirectory : workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }
}

public readonly record struct WorkspaceRemotePreviewResult(bool IsSuccess, string? ErrorMessage)
{
    public static WorkspaceRemotePreviewResult Success() => new(true, null);

    public static WorkspaceRemotePreviewResult Fail(string errorMessage) => new(false, errorMessage);
}

internal sealed class RemotePreviewTcpSession : IDisposable
{
    private readonly IDisposable _listener;
    private readonly object _gate = new();
    private IAvaloniaRemoteTransportConnection? _connection;
    private string? _pendingXaml;
    private string? _pendingAssemblyPath;
    private string? _pendingProjectPath;

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Avalonia designer transport uses BSON reflection by design.")]
    public RemotePreviewTcpSession()
    {
        Port = GetFreeTcpPort();
        _listener = new BsonTcpTransport().Listen(IPAddress.Loopback, Port, OnConnected);
    }

    public int Port { get; }

    public event Action<FrameMessage>? FrameReceived;

    public event Action<string>? ErrorReceived;

    public Task SendUpdateXamlAsync(string xaml, string assemblyPath, string xamlFileProjectPath)
    {
        IAvaloniaRemoteTransportConnection? connection;
        lock (_gate)
        {
            if (_connection is null)
            {
                _pendingXaml = xaml;
                _pendingAssemblyPath = assemblyPath;
                _pendingProjectPath = xamlFileProjectPath;
                return Task.CompletedTask;
            }

            connection = _connection;
        }

        SendViewportMessage(connection);
        return connection.Send(new UpdateXamlMessage
        {
            Xaml = xaml,
            AssemblyPath = assemblyPath,
            XamlFileProjectPath = xamlFileProjectPath
        });
    }

    public void Dispose()
    {
        _listener.Dispose();
        lock (_gate)
        {
            _connection?.Dispose();
            _connection = null;
        }
    }

    private void OnConnected(IAvaloniaRemoteTransportConnection connection)
    {
        lock (_gate)
        {
            _connection?.Dispose();
            _connection = connection;
        }

        connection.OnMessage += OnMessage;
        connection.OnException += (_, exception) => ErrorReceived?.Invoke($"Workspace preview transport error: {exception.Message}");
        connection.Start();
        _ = connection.Send(new ClientSupportedPixelFormatsMessage
        {
            Formats = new[] { PixelFormat.Bgra8888 }
        });
        _ = connection.Send(new ClientRenderInfoMessage
        {
            DpiX = 96,
            DpiY = 96
        });
        SendViewportMessage(connection);
        TrySendPendingUpdate(connection);
    }

    private void OnMessage(IAvaloniaRemoteTransportConnection connection, object message)
    {
        switch (message)
        {
            case FrameMessage frame:
                _ = connection.Send(new FrameReceivedMessage
                {
                    SequenceId = frame.SequenceId
                });
                FrameReceived?.Invoke(frame);
                break;
            case RequestViewportResizeMessage resize:
                SendViewportMessage(connection, resize.Width, resize.Height);
                break;
            case UpdateXamlResultMessage { Error: { Length: > 0 } error }:
                ErrorReceived?.Invoke(error);
                break;
        }
    }

    private void TrySendPendingUpdate(IAvaloniaRemoteTransportConnection connection)
    {
        string? xaml;
        string? assemblyPath;
        string? projectPath;
        lock (_gate)
        {
            xaml = _pendingXaml;
            assemblyPath = _pendingAssemblyPath;
            projectPath = _pendingProjectPath;
            _pendingXaml = null;
            _pendingAssemblyPath = null;
            _pendingProjectPath = null;
        }

        if (string.IsNullOrWhiteSpace(xaml) ||
            string.IsNullOrWhiteSpace(assemblyPath) ||
            string.IsNullOrWhiteSpace(projectPath))
        {
            return;
        }

        _ = connection.Send(new UpdateXamlMessage
        {
            Xaml = xaml,
            AssemblyPath = assemblyPath,
            XamlFileProjectPath = projectPath
        });
    }

    private static void SendViewportMessage(
        IAvaloniaRemoteTransportConnection connection,
        double width = 1000,
        double height = 700)
    {
        _ = connection.Send(new ClientViewportAllocatedMessage
        {
            Width = width,
            Height = height,
            DpiX = 96,
            DpiY = 96
        });
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
