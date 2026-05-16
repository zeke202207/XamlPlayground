using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace XamlPlayground.Extensions;

public interface IExtensionContext
{
    ExtensionManifest Manifest { get; }

    IExtensionCommandRegistry Commands { get; }

    IDictionary<string, object?> WorkspaceState { get; }

    IDisposable Subscribe(IDisposable subscription);
}

public sealed class ExtensionContext : IExtensionContext, IDisposable, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly List<IDisposable> _subscriptions = new();
    private bool _disposed;

    public ExtensionContext(ExtensionManifest manifest, IExtensionCommandRegistry commands)
    {
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        Commands = commands ?? throw new ArgumentNullException(nameof(commands));
        WorkspaceState = new Dictionary<string, object?>(StringComparer.Ordinal);
    }

    public ExtensionManifest Manifest { get; }

    public IExtensionCommandRegistry Commands { get; }

    public IDictionary<string, object?> WorkspaceState { get; }

    public IDisposable Subscribe(IDisposable subscription)
    {
        if (subscription is null)
        {
            throw new ArgumentNullException(nameof(subscription));
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _subscriptions.Add(subscription);
        }

        return subscription;
    }

    public void Dispose()
    {
        List<IDisposable> subscriptions;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            subscriptions = new List<IDisposable>(_subscriptions);
            _subscriptions.Clear();
        }

        List<Exception>? exceptions = null;
        for (var i = subscriptions.Count - 1; i >= 0; i--)
        {
            try
            {
                subscriptions[i].Dispose();
            }
            catch (Exception exception)
            {
                exceptions ??= new List<Exception>();
                exceptions.Add(exception);
            }
        }

        if (exceptions is { Count: > 0 })
        {
            throw new AggregateException("One or more extension subscriptions failed during disposal.", exceptions);
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed class ExtensionDisposable : IDisposable
{
    private readonly Action _dispose;
    private bool _disposed;

    private ExtensionDisposable(Action dispose)
    {
        _dispose = dispose;
    }

    public static IDisposable Create(Action dispose)
    {
        if (dispose is null)
        {
            throw new ArgumentNullException(nameof(dispose));
        }

        return new ExtensionDisposable(dispose);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _dispose();
    }
}
