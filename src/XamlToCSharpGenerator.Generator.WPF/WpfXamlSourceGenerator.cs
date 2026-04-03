using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Compiler;
using XamlToCSharpGenerator.WPF.Framework;

namespace XamlToCSharpGenerator.Generator.WPF;

/// <summary>
/// Roslyn incremental source generator for WPF XAML (WXSG).
///
/// Plugs <see cref="WpfFrameworkProfile"/> into the shared
/// <see cref="XamlSourceGeneratorCompilerHost"/> pipeline from the XSG engine.
/// This mirrors what <c>AvaloniaXamlSourceGenerator</c> does via
/// <c>FrameworkXamlSourceGenerator</c>, but calls the public compiler host API
/// directly so the generator can live outside the XSG submodule.
///
/// Phase 1 output for each <c>&lt;Page /&gt;</c> / <c>&lt;ApplicationDefinition /&gt;</c> file:
/// <list type="bullet">
///   <item>Typed field declarations for all <c>x:Name</c> elements</item>
///   <item><c>InitializeComponent()</c> backed by <c>Application.LoadComponent</c></item>
/// </list>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class WpfXamlSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        XamlSourceGeneratorCompilerHost.Initialize(context, WpfFrameworkProfile.Instance);
    }
}
