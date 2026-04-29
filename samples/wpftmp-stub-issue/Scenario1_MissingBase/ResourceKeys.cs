using System.Windows;

namespace WpfTmpStubIssue.Scenario1_MissingBase;

public static class ResourceKeys
{
    public static readonly ResourceKey AccentBrush =
        new ComponentResourceKey(typeof(ResourceKeys), nameof(AccentBrush));
}
