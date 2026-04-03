using System.Collections.Immutable;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Abstractions;

namespace XamlToCSharpGenerator.WPF.Framework;

/// <summary>
/// WPF has no transform rule files (Avalonia uses .axamlx; WPF has no equivalent).
/// Returns empty configurations for all inputs.
/// </summary>
internal sealed class WpfFrameworkTransformProvider : IXamlFrameworkTransformProvider
{
    internal static WpfFrameworkTransformProvider Instance { get; } = new();
    private WpfFrameworkTransformProvider() { }

    public XamlFrameworkTransformRuleResult ParseTransformRule(XamlFrameworkTransformRuleInput input) =>
        new(input.FilePath, XamlTransformConfiguration.Empty, ImmutableArray<DiagnosticInfo>.Empty);

    public XamlFrameworkTransformRuleAggregateResult MergeTransformRules(
        ImmutableArray<XamlFrameworkTransformRuleResult> files) =>
        new(XamlTransformConfiguration.Empty, ImmutableArray<DiagnosticInfo>.Empty);
}
