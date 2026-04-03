using XamlToCSharpGenerator.Framework.Abstractions;

namespace XamlToCSharpGenerator.WPF.Framework;

/// <summary>
/// WPF MSBuild item-group conventions.
/// WPF XAML files are declared as &lt;Page /&gt; or &lt;ApplicationDefinition /&gt; items.
/// The source generator discovers them via &lt;AdditionalFiles&gt; with
/// <c>SourceItemGroup=Page</c> metadata (set by the XamlToCSharpGenerator.Build.WPF targets).
/// There are no transform rule files (unlike Avalonia's .axamlx format).
/// </summary>
internal sealed class WpfFrameworkBuildContract : IXamlFrameworkBuildContract
{
    internal static WpfFrameworkBuildContract Instance { get; } = new();
    private WpfFrameworkBuildContract() { }

    private const string PageGroup = "Page";
    private const string AppDefGroup = "ApplicationDefinition";

    /// <summary>
    /// Roslyn incremental generator metadata key for AdditionalFiles item-group membership.
    /// Matches the <c>CompilerVisibleItemMetadata</c> format used by the XSG build props.
    /// </summary>
    public string SourceItemGroupMetadataName => "build_metadata.AdditionalFiles.SourceItemGroup";

    public string TargetPathMetadataName => "build_metadata.AdditionalFiles.TargetPath";

    public string XamlSourceItemGroup => PageGroup;

    // WPF has no transform-rule files.
    public string TransformRuleSourceItemGroup => string.Empty;

    public bool IsXamlPath(string path) =>
        path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase);

    public bool IsXamlSourceItemGroup(string? sourceItemGroup) =>
        string.Equals(sourceItemGroup, PageGroup, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(sourceItemGroup, AppDefGroup, StringComparison.OrdinalIgnoreCase);

    public bool IsTransformRuleSourceItemGroup(string? sourceItemGroup) => false;

    public string NormalizeSourceItemGroup(string? sourceItemGroup) =>
        IsXamlSourceItemGroup(sourceItemGroup) ? PageGroup : string.Empty;
}
