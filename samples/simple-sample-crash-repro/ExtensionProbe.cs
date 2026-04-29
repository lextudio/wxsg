using System.Windows.Controls;

namespace SimpleSampleCrashRepro;

public sealed class ProbeControl : Control
{
}

public sealed class ResizeProbeExtension
{
}

public sealed class SelectionProbeExtension
{
}

[ExtensionFor(
    typeof(ProbeControl),
    OverrideExtensions = new[] { typeof(ResizeProbeExtension), typeof(SelectionProbeExtension) })]
public sealed class ProbeControlExtension
{
}

[ExtensionFor(typeof(TextBox), OverrideExtension = typeof(SelectionProbeExtension))]
public sealed class TextBoxPlacementExtension
{
}
