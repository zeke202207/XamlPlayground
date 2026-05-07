using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace XamlPlayground.Controls;

public sealed class ExclusiveContentControl : ContentControl
{
    public static readonly StyledProperty<Control?> SourceProperty =
        AvaloniaProperty.Register<ExclusiveContentControl, Control?>(nameof(Source));

    private static readonly object s_sync = new();
    private static readonly Dictionary<Control, WeakReference<ExclusiveContentControl>> s_owners =
        new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<Control, List<WeakReference<ExclusiveContentControl>>> s_hosts =
        new(ReferenceEqualityComparer.Instance);

    private bool _isAttached;
    private bool _ownsSource;

    public Control? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;

        var source = Source;
        RegisterHost(source);
        TryClaim(source);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        var source = Source;
        _isAttached = false;

        var releasedOwnership = ReleaseOwnership(source);
        UnregisterHost(source);
        ClearContent(source);
        if (releasedOwnership)
        {
            PromoteHost(source);
        }

        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != SourceProperty)
        {
            return;
        }

        var oldSource = change.GetOldValue<Control?>();
        var newSource = change.GetNewValue<Control?>();

        if (ReferenceEquals(oldSource, newSource))
        {
            return;
        }

        var releasedOwnership = ReleaseOwnership(oldSource);
        UnregisterHost(oldSource);
        ClearContent(oldSource);
        if (releasedOwnership)
        {
            PromoteHost(oldSource);
        }

        RegisterHost(newSource);
        TryClaim(newSource);
    }

    private void TryClaim(Control? source)
    {
        if (!_isAttached || source is null || !ReferenceEquals(Source, source))
        {
            ClearContent(source);
            return;
        }

        ExclusiveContentControl? previousOwner;

        lock (s_sync)
        {
            previousOwner = GetOwner(source);

            if (ReferenceEquals(previousOwner, this))
            {
                _ownsSource = true;
            }
            else
            {
                if (previousOwner is not null)
                {
                    _ownsSource = false;
                }
                else
                {
                    s_owners[source] = new WeakReference<ExclusiveContentControl>(this);
                    _ownsSource = true;
                }
            }
        }

        if (previousOwner is not null && !ReferenceEquals(previousOwner, this))
        {
            ClearContent(source);
            return;
        }

        if (!ReferenceEquals(Content, source))
        {
            Content = source;
        }
    }

    private void ClearContent(Control? source)
    {
        if (source is not null && ReferenceEquals(Content, source))
        {
            Content = null;
        }
    }

    private void RegisterHost(Control? source)
    {
        if (!_isAttached || source is null)
        {
            return;
        }

        lock (s_sync)
        {
            if (!s_hosts.TryGetValue(source, out var hosts))
            {
                hosts = [];
                s_hosts[source] = hosts;
            }

            hosts.RemoveAll(IsThisHost);
            hosts.Add(new WeakReference<ExclusiveContentControl>(this));
        }
    }

    private void UnregisterHost(Control? source)
    {
        if (source is null)
        {
            return;
        }

        lock (s_sync)
        {
            if (!s_hosts.TryGetValue(source, out var hosts))
            {
                return;
            }

            hosts.RemoveAll(IsThisHost);

            if (hosts.Count == 0)
            {
                s_hosts.Remove(source);
            }
        }
    }

    private bool ReleaseOwnership(Control? source)
    {
        if (source is null)
        {
            _ownsSource = false;
            return false;
        }

        var releasedOwnership = false;

        lock (s_sync)
        {
            if (s_owners.TryGetValue(source, out var owner) &&
                owner.TryGetTarget(out var ownerTarget) &&
                ReferenceEquals(ownerTarget, this))
            {
                s_owners.Remove(source);
                releasedOwnership = true;
            }

            _ownsSource = false;
        }

        return releasedOwnership;
    }

    private static void PromoteHost(Control? source)
    {
        if (source is null)
        {
            return;
        }

        ExclusiveContentControl? nextOwner = null;

        lock (s_sync)
        {
            if (GetOwner(source) is not null)
            {
                return;
            }

            if (!s_hosts.TryGetValue(source, out var hosts))
            {
                return;
            }

            PruneHosts(source, hosts);
            nextOwner = hosts
                .Select(static reference => reference.TryGetTarget(out var host) ? host : null)
                .FirstOrDefault(host => host is { _isAttached: true } && ReferenceEquals(host.Source, source));
        }

        nextOwner?.TryClaim(source);
    }

    private static ExclusiveContentControl? GetOwner(Control source)
    {
        if (!s_owners.TryGetValue(source, out var owner))
        {
            return null;
        }

        if (owner.TryGetTarget(out var ownerTarget) &&
            ownerTarget._ownsSource &&
            ownerTarget._isAttached &&
            ReferenceEquals(ownerTarget.Source, source))
        {
            return ownerTarget;
        }

        s_owners.Remove(source);
        return null;
    }

    private static void PruneHosts(Control source, List<WeakReference<ExclusiveContentControl>> hosts)
    {
        hosts.RemoveAll(IsDeadHost);

        if (hosts.Count == 0)
        {
            s_hosts.Remove(source);
        }
    }

    private static bool IsDeadHost(WeakReference<ExclusiveContentControl> reference)
    {
        return !reference.TryGetTarget(out var host) || !host._isAttached;
    }

    private bool IsThisHost(WeakReference<ExclusiveContentControl> reference)
    {
        return reference.TryGetTarget(out var host) && ReferenceEquals(host, this);
    }
}
