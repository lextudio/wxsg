using System;

namespace SimpleSampleCrashRepro;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ExtensionForAttribute : Attribute
{
    public ExtensionForAttribute(Type designedItemType)
    {
        DesignedItemType = designedItemType;
    }

    public Type DesignedItemType { get; }

    public Type? OverrideExtension { get; set; }

    public Type[] OverrideExtensions { get; set; } = Array.Empty<Type>();
}
