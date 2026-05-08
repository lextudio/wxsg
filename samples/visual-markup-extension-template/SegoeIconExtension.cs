using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace VisualMarkupExtensionTemplateSample;

// Returns a TextBlock (a Visual) — FrameworkElementFactory.SetValue() rejects
// Visual-derived values, so using this inside a ControlTemplate exposes the bug.
[MarkupExtensionReturnType(typeof(TextBlock))]
public sealed class SegoeIconExtension : MarkupExtension
{
    public SegoeIconExtension(string glyph)
    {
        Glyph = glyph;
    }

    public string Glyph { get; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return new TextBlock
        {
            Text = Glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 16,
        };
    }
}
