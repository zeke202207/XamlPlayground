using System;

namespace XamlPlayground.Extensions;

public static class ExtensionActivationEvents
{
    public const string Any = "*";
    public const string OnStartupFinished = "onStartupFinished";
    public const string OnWorkspaceOpened = "onWorkspaceOpened";
    public const string OnThemeEditor = "onThemeEditor";
    public const string OnAnimationEditor = "onAnimationEditor";
    public const string OnPreviewSession = "onPreviewSession";

    public static string OnCommand(string commandId)
    {
        return "onCommand:" + RequireSegment(commandId, nameof(commandId));
    }

    public static string OnView(string viewId)
    {
        return "onView:" + RequireSegment(viewId, nameof(viewId));
    }

    public static string OnPreview(string previewKind)
    {
        return "onPreview:" + RequireSegment(previewKind, nameof(previewKind));
    }

    public static string OnEditorFeature(string featureId)
    {
        return "onEditorFeature:" + RequireSegment(featureId, nameof(featureId));
    }

    public static string OnDiagnostic(string diagnosticKind)
    {
        return "onDiagnostic:" + RequireSegment(diagnosticKind, nameof(diagnosticKind));
    }

    public static string OnDebugTool(string toolId)
    {
        return "onDebugTool:" + RequireSegment(toolId, nameof(toolId));
    }

    public static string OnLanguage(string languageId)
    {
        return "onLanguage:" + RequireSegment(languageId, nameof(languageId));
    }

    public static string OnWorkspaceContains(string glob)
    {
        return "workspaceContains:" + RequireSegment(glob, nameof(glob));
    }

    public static bool Matches(string declaredActivationEvent, string requestedActivationEvent)
    {
        if (string.IsNullOrWhiteSpace(declaredActivationEvent))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(requestedActivationEvent))
        {
            return false;
        }

        if (string.Equals(declaredActivationEvent, Any, StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(declaredActivationEvent, requestedActivationEvent, StringComparison.Ordinal);
    }

    private static string RequireSegment(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The activation event segment cannot be empty.", parameterName);
        }

        return value.Trim();
    }
}
