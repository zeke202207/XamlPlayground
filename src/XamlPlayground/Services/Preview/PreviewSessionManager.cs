namespace XamlPlayground.Services.Preview;

public sealed class PreviewSessionManager
{
    public PreviewSnapshot? CurrentSnapshot { get; private set; }

    public PreviewReloadPlan PlanNext(
        PreviewSnapshot snapshot,
        PreviewHostCapabilities capabilities)
    {
        var changes = PreviewChangeClassifier.Classify(CurrentSnapshot, snapshot);
        var strategy = SelectStrategy(snapshot, capabilities, changes);
        return new PreviewReloadPlan(
            snapshot.RequestId,
            capabilities.Mode,
            strategy,
            changes,
            capabilities.IsIsolated,
            Describe(capabilities, strategy, changes));
    }

    public void MarkLoaded(PreviewSnapshot snapshot)
    {
        CurrentSnapshot = snapshot;
    }

    public void Reset()
    {
        CurrentSnapshot = null;
    }

    private static PreviewReloadStrategy SelectStrategy(
        PreviewSnapshot snapshot,
        PreviewHostCapabilities capabilities,
        PreviewChangeSet changes)
    {
        if (changes.Kind == PreviewUpdateKind.None)
        {
            return PreviewReloadStrategy.NoOp;
        }

        if (PreviewChangeClassifier.CanApplyAsLiveXamlUpdate(changes, snapshot) &&
            capabilities.SupportsLiveXamlUpdates)
        {
            return capabilities.Mode == PreviewExecutionMode.InlineDesign
                ? PreviewReloadStrategy.InlineReload
                : PreviewReloadStrategy.RemoteLiveXamlUpdate;
        }

        if (changes.Kind is PreviewUpdateKind.ResourcesOnly or PreviewUpdateKind.XamlAndResources &&
            capabilities.SupportsResourceUpdates)
        {
            return PreviewReloadStrategy.HostResourceUpdate;
        }

        if (changes.RequiresCompilation && capabilities.SupportsHostCompilation)
        {
            return PreviewReloadStrategy.HostRecompile;
        }

        return capabilities.Mode == PreviewExecutionMode.InlineDesign
            ? PreviewReloadStrategy.InlineReload
            : PreviewReloadStrategy.RestartHost;
    }

    private static string Describe(
        PreviewHostCapabilities capabilities,
        PreviewReloadStrategy strategy,
        PreviewChangeSet changes)
    {
        var isolation = capabilities.IsIsolated ? "isolated" : "inline";
        return strategy switch
        {
            PreviewReloadStrategy.NoOp => $"{isolation} preview is current.",
            PreviewReloadStrategy.InlineReload => "Reloading inline design preview.",
            PreviewReloadStrategy.RemoteLiveXamlUpdate => "Sending live XAML update to isolated preview host.",
            PreviewReloadStrategy.HostResourceUpdate => "Updating isolated preview resources.",
            PreviewReloadStrategy.HostRecompile => "Compiling changed code inside isolated preview host.",
            PreviewReloadStrategy.RestartHost => changes.RequiresCompilation
                ? "Restarting isolated preview host for code or reference changes."
                : "Restarting isolated preview host for structural changes.",
            _ => "Updating preview."
        };
    }
}
