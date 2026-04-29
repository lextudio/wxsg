using System;
using System.Windows;
using System.Windows.Controls;

namespace SimpleSampleCrashRepro
{
    public sealed class FakeDesignSurface : Border
    {
        public FakeDesignPanel? DesignPanel { get; private set; }

        public void LoadDesigner()
        {
            DesignPanel = new FakeDesignPanel();
        }
    }

    public sealed class FakePropertyGridView : Control
    {
    }

    public sealed class VersionedAssemblyResourceDictionary : ResourceDictionary
    {
    }

    public sealed class FakeDesignPanel
    {
        public FakeDesignContext Context { get; } = new();
    }

    public sealed class FakeDesignContext
    {
        public FakeDesignServices Services { get; } = new();
    }

    public sealed class FakeDesignServices
    {
        public FakeToolService Tool { get; } = new();
    }

    public sealed class FakeToolService
    {
        public CreateComponentTool? CurrentTool { get; set; }
    }

    public sealed class CreateComponentTool
    {
        public CreateComponentTool(Type componentType)
        {
            ComponentType = componentType;
        }

        public Type ComponentType { get; }
    }
}
