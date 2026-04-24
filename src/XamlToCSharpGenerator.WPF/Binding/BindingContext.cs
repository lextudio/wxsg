using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.WPF.Binding;

internal sealed class BindingContext
{
    private const string WxsgUnknownTypeDiagnosticId = "WXSG0101";
    private const string WxsgUnknownPropertyDiagnosticId = "WXSG0102";
    private const string WxsgInvalidEventHandlerDiagnosticId = "WXSG0103";

    public BindingContext(
        XamlDocumentModel document,
        Compilation compilation,
        XmlnsDefinitionCacheEntry xmlnsMap,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        bool strictMode,
        bool csharpExpressionsEnabled,
        bool implicitCSharpExpressionsEnabled)
    {
        Document = document;
        Compilation = compilation;
        XmlnsMap = xmlnsMap;
        Diagnostics = diagnostics;
        StrictMode = strictMode;
        CSharpExpressionsEnabled = csharpExpressionsEnabled;
        ImplicitCSharpExpressionsEnabled = implicitCSharpExpressionsEnabled;
    }

    public XamlDocumentModel Document { get; }

    public Compilation Compilation { get; }

    public XmlnsDefinitionCacheEntry XmlnsMap { get; }

    public ImmutableArray<DiagnosticInfo>.Builder Diagnostics { get; }

    public bool StrictMode { get; }

    public bool CSharpExpressionsEnabled { get; }

    public bool ImplicitCSharpExpressionsEnabled { get; }

    public void AddUnknownTypeDiagnostic(string xmlTypeName, int line, int column)
    {
        Diagnostics.Add(new DiagnosticInfo(
            WxsgUnknownTypeDiagnosticId,
            "Unknown XAML type '" + xmlTypeName + "'.",
            Document.FilePath,
            line,
            column,
            StrictMode));
    }

    public void AddUnknownPropertyDiagnostic(string propertyName, string ownerTypeName, int line, int column)
    {
        Diagnostics.Add(new DiagnosticInfo(
            WxsgUnknownPropertyDiagnosticId,
            "Unknown property or event '" + propertyName + "' on '" + ownerTypeName + "'.",
            Document.FilePath,
            line,
            column,
            StrictMode));
    }

    public void AddInvalidEventHandlerDiagnostic(string eventName, int line, int column)
    {
        Diagnostics.Add(new DiagnosticInfo(
            WxsgInvalidEventHandlerDiagnosticId,
            "Event '" + eventName + "' requires a valid handler method name.",
            Document.FilePath,
            line,
            column,
            StrictMode));
    }
}
