# _wpftmp Stub Issue — Regression Test

**Regression test conclusion (2026-04-26):**
WXSG does **not** support .NET Framework. The `_wpftmp` stub problem is a
.NET Framework-only failure. Modern .NET is unaffected.

| Target | Build result | Reason |
|--------|-------------|--------|
| `net10.0-windows` | **PASSES** | No `GenerateTemporaryTargetAssembly`; WXSG owns the full compilation |
| `net48` | **FAILS** | `GenerateTemporaryTargetAssembly` builds `_wpftmp.csproj`; WXSG stubs are absent |

This dual-target project is the regression test. If `net10.0-windows` ever regresses to
a failure, something broke in WXSG's core XAML handling. If `net48` ever starts passing,
it means stub support was added (intentionally or not) and should be verified.

---

Demonstrates why `WxsgGenerateCSharpWpfTmpStubs` is needed and what it must produce correctly.

## Background

WPF's XAML compilation uses two passes:

| Pass | What it does |
|------|-------------|
| **MarkupCompilePass1** | Compiles classless XAML (ResourceDictionaries) to BAML; **defers** classed XAML that references local types to Pass2 |
| **MarkupCompilePass2** | Resolves local type references, finishes BAML; needs a reference assembly |

To compile Pass2, MSBuild runs `GenerateTemporaryTargetAssembly`, which creates a temporary project (`_wpftmp.csproj`) that compiles **all project `.cs` sources** plus any `.g.cs` files from Pass1. Pass1 normally generates a `.g.cs` stub for every classed XAML file it processes — but WXSG **removes** classed XAML from `@(Page)` before Pass1 runs, so Pass1 never generates those stubs.

Without stubs, code-behind files fail to compile inside `_wpftmp` because the partial class has no base type, no `InitializeComponent`, and no x:Name fields.

**Why `net48`?** SDK-style .NET Framework projects still go through both passes. The `_wpftmp` mechanism does not apply to pure SDK-style .NET 6+ projects (which use a different XAML pipeline). This sample targets `net48` to reproduce the actual failure mode from production codebases like SharpDevelop 4.

---

## Scenario 1 — Missing base class → CS0115

**Files:** `Scenario1_MissingBase/MyControl.xaml` + `MyControl.xaml.cs`

```
MyControl.xaml  →  x:Class="...MyControl", root <UserControl>
MyControl.xaml.cs  →  protected override void OnPropertyChanged(...)
```

WXSG removes `MyControl.xaml` from `@(Page)`. Pass1 never generates `MyControl.g.cs`.
In `_wpftmp`, the partial class has no base type declared anywhere, so `override` has
nothing to override.

```
error CS0115: 'MyControl.OnPropertyChanged(DependencyPropertyChangedEventArgs)':
              no suitable method found to override
```

**Fix:** Stub declares `public partial class MyControl : global::System.Windows.Controls.UserControl`.
Base type is read from the code-behind regex (`public partial class MyControl`) — no match for
base → fall back to `wpfTypeMap["UserControl"]`.

**Stub produced:** [`generated-stubs/...MyControl.wpftmp.g.cs`](generated-stubs/WpfTmpStubIssue_Scenario1_MissingBase_MyControl.wpftmp.g.cs)

---

## Scenario 2 — Wrong base class inferred → CS1061 `.Children`

**Files:** `Scenario2_WrongBase/ItemsPanel.xaml` (no `.xaml.cs`)

```
ItemsPanel.xaml  →  x:Class="...ItemsPanel", root <Grid>
                     (no code-behind file)
```

`ItemsPanel` is a XAML-only class. The stub generator has no code-behind to parse,
so it must infer the base class from the XAML root element tag.

Before fix: `wpfTypeMap` did not include `"Grid"` → fell back to `FrameworkElement`.
A caller (`PanelHost.xaml.cs`) then does:

```csharp
panel.Children.Add(toolbar);   // panel is ItemsPanel
```

`FrameworkElement` (and `UserControl`, the previous default) have no `Children` property.
`Grid` inherits `Panel.Children`, so the correct stub base is `Grid`.

```
error CS1061: 'ItemsPanel' does not contain a definition for 'Children'
```

**Fix:** Added `"Grid"` (and all other Panel types) to `wpfTypeMap`.
Also note `x:FieldModifier="public"` on `<ListView Name="listView">` — the stub emits
`public ListView listView` so that callers from other classes can reach it.

**Stub produced:** [`generated-stubs/...ItemsPanel.wpftmp.g.cs`](generated-stubs/WpfTmpStubIssue_Scenario2_WrongBase_ItemsPanel.wpftmp.g.cs)

---

## Scenario 3 — Event subscription on `dynamic` field → CS0019 / CS1977

**Files:** `Scenario3_TypedField/PanelHost.xaml` + `PanelHost.xaml.cs`

```xml
<!-- PanelHost.xaml -->
xmlns:local="clr-namespace:WpfTmpStubIssue.Scenario2_WrongBase"
<local:ItemsPanel x:Name="panel" />
```

```csharp
// PanelHost.xaml.cs
panel.listView.MouseDoubleClick += delegate { ... };   // CS0019
panel.listView.SelectionChanged += (s, e) => { ... };  // CS1977
```

Before fix: `WxsgGenerateCSharpWpfTmpStubs` typed every x:Name field as `internal dynamic`.
`dynamic` allows member access at compile time, but C# **cannot** subscribe events using
`+=` with anonymous methods, delegates, or lambda expressions on a `dynamic` receiver — it
can't infer the delegate type.

```
error CS0019: Operator '+=' cannot be applied to operands of type 'dynamic' and 'anonymous method'
error CS1977: Cannot use a lambda expression as an argument to a dynamically dispatched
              operation without first casting it to a delegate or expression tree type
error CS1976: Cannot use a method group as an argument to a dynamically dispatched operation
```

**Fix:** For each x:Name element the stub now resolves the actual CLR type:
- WPF presentation namespace → look up `wpfTypeMap` by element local name
- `clr-namespace:Foo;assembly=Bar` namespace → use `global::Foo.ElementName` directly
- Registered URI namespace (e.g. `http://example.com/ns`) → **still a gap** (see below)
- Unknown → fall back to `global::System.Windows.Controls.Control`

`x:FieldModifier` on the element is also read and applied to the field's access modifier
(default `internal`; `public` when declared).

**Stub produced:** [`generated-stubs/...PanelHost.wpftmp.g.cs`](generated-stubs/WpfTmpStubIssue_Scenario3_TypedField_PanelHost.wpftmp.g.cs)

---

## Scenario 4 — Registered XML namespace URI → CS1061 / CS1503 (open issue)

Not yet reproduced as a buildable sample, but documented here for completeness.

```xml
xmlns:widgets="http://icsharpcode.net/sharpdevelop/widgets"
<widgets:NumericUpDown Name="parallelBuildCount" />
```

```csharp
parallelBuildCount.Value = 5;   // CS1061: 'Control' has no 'Value'
```

The stub falls back to `global::System.Windows.Controls.Control` because the namespace
URI `http://icsharpcode.net/sharpdevelop/widgets` is not a `clr-namespace:` form. To
resolve it, the stub generator must read `[XmlnsDefinitionAttribute]` from referenced
assemblies at build time (exactly as WPF's `XmlnsCache` does).

**Planned fix:** Add a `ReferencePaths` parameter to `WxsgGenerateCSharpWpfTmpStubs`.
At task startup, load each reference via `Assembly.ReflectionOnlyLoadFrom()` and scan
`CustomAttributeData` for `XmlnsDefinitionAttribute`. This builds a
`Dictionary<uri, List<clrNamespace>>` used as a fourth lookup tier before the `Control`
fallback. See [`docs/mock.md`](../../docs/mock.md) for the full implementation sketch.

---

## How to inspect the generated _wpftmp project

Set the MSBuild property `GenerateTemporaryTargetAssemblyDebuggingInformation=true`
(already set in `WpfTmpStubIssue.csproj`). After a build, the generated project will
remain on disk at a path like:

```
obj\Debug\WpfTmpStubIssue_<random>_wpftmp.csproj
```

Open it to see which `.cs` and `.g.cs` files are included in the temporary compilation.
The stub files injected by `_WxsgInjectStubsIntoGeneratedCodeFiles` will appear as
`<Compile>` items alongside the normal project sources.

---

## Build (regression test)

```powershell
# net10.0-windows — expected: PASS
dotnet build WpfTmpStubIssue.csproj -f net10.0-windows

# net48 — expected: FAIL (CS0115, CS1061, CS0019 in _wpftmp)
# Requires MSBuild from VS 2022; dotnet build cannot target net48.
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" `
    WpfTmpStubIssue.csproj `
    /p:TargetFramework=net48 `
    /p:WpfXsgEnabled=true /p:WpfXsgCSharpMode=true `
    /p:Configuration=Debug /t:Rebuild
```

The `net48` failure is **expected and intentional** — it documents the scope boundary of
WXSG. Do not add `_wpftmp` stub infrastructure to WXSG to make it pass; that work belongs
in the consuming project (e.g. SharpDevelop's own `Directory.Build.targets`).
