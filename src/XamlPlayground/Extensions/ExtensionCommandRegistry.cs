using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace XamlPlayground.Extensions;

public delegate ValueTask<object?> ExtensionCommandHandler(ExtensionCommandInvocation invocation, CancellationToken cancellationToken);

public interface IExtensionCommandRegistry
{
    IReadOnlyCollection<ExtensionCommandRegistration> Commands { get; }

    IDisposable RegisterCommand(string commandId, ExtensionCommandHandler handler, string? title = null, string? extensionId = null);

    bool Contains(string commandId);

    ValueTask<object?> ExecuteAsync(string commandId, IEnumerable<object?>? arguments = null, CancellationToken cancellationToken = default);
}

public sealed class ExtensionCommandRegistry : IExtensionCommandRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, RegisteredCommand> _commands = new(StringComparer.Ordinal);

    public IReadOnlyCollection<ExtensionCommandRegistration> Commands
    {
        get
        {
            lock (_gate)
            {
                var commands = new List<ExtensionCommandRegistration>(_commands.Count);
                foreach (var command in _commands.Values)
                {
                    commands.Add(command.Registration);
                }

                return Array.AsReadOnly(commands.ToArray());
            }
        }
    }

    public IDisposable RegisterCommand(string commandId, ExtensionCommandHandler handler, string? title = null, string? extensionId = null)
    {
        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        var id = ExtensionCollections.RequireIdentifier(commandId, nameof(commandId));
        var registration = new ExtensionCommandRegistration(id, title, extensionId);

        lock (_gate)
        {
            if (_commands.ContainsKey(id))
            {
                throw new InvalidOperationException("A command with id '" + id + "' is already registered.");
            }

            _commands.Add(id, new RegisteredCommand(registration, handler));
        }

        return ExtensionDisposable.Create(() => Unregister(id, registration));
    }

    public bool Contains(string commandId)
    {
        var id = ExtensionCollections.RequireIdentifier(commandId, nameof(commandId));

        lock (_gate)
        {
            return _commands.ContainsKey(id);
        }
    }

    public ValueTask<object?> ExecuteAsync(string commandId, IEnumerable<object?>? arguments = null, CancellationToken cancellationToken = default)
    {
        var id = ExtensionCollections.RequireIdentifier(commandId, nameof(commandId));
        cancellationToken.ThrowIfCancellationRequested();
        RegisteredCommand command;

        lock (_gate)
        {
            if (!_commands.TryGetValue(id, out command!))
            {
                throw new KeyNotFoundException("No command with id '" + id + "' is registered.");
            }
        }

        var invocation = new ExtensionCommandInvocation(id, arguments);
        return command.Handler(invocation, cancellationToken);
    }

    private void Unregister(string commandId, ExtensionCommandRegistration registration)
    {
        lock (_gate)
        {
            if (_commands.TryGetValue(commandId, out var command)
                && ReferenceEquals(command.Registration, registration))
            {
                _commands.Remove(commandId);
            }
        }
    }

    private sealed class RegisteredCommand
    {
        public RegisteredCommand(ExtensionCommandRegistration registration, ExtensionCommandHandler handler)
        {
            Registration = registration;
            Handler = handler;
        }

        public ExtensionCommandRegistration Registration { get; }

        public ExtensionCommandHandler Handler { get; }
    }
}

public sealed class ExtensionCommandRegistration
{
    public ExtensionCommandRegistration(string id, string? title = null, string? extensionId = null)
    {
        Id = ExtensionCollections.RequireIdentifier(id, nameof(id));
        Title = title;
        ExtensionId = extensionId;
    }

    public string Id { get; }

    public string? Title { get; }

    public string? ExtensionId { get; }
}

public sealed class ExtensionCommandInvocation
{
    public ExtensionCommandInvocation(string commandId, IEnumerable<object?>? arguments = null)
    {
        CommandId = ExtensionCollections.RequireIdentifier(commandId, nameof(commandId));
        Arguments = ExtensionCollections.CopyList(arguments);
    }

    public string CommandId { get; }

    public IReadOnlyList<object?> Arguments { get; }
}
