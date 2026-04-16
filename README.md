# XAML Source Generator for WPF (WXSG) Toolset

[![Become a Sponsor](https://img.shields.io/badge/Become%20a%20Sponsor-lextudio-orange.svg?style=for-readme)](https://github.com/sponsors/lextudio)

WXSG replaces WPF's classic XAML/BAML codegen path with Roslyn source generation and emits
typed `InitializeComponent()` code directly in C# and VB.NET.

**This toolset is independent and unaffiliated with Microsoft.**

This repo's reference usage can be found [in this sample](https://github.com/lextudio/vscode-wpf/tree/master/sample/net6.0-csharp-expressions).

## What You Get

- WXSG takes over `InitializeComponent()` generation for WPF XAML.
- Typed `x:Name` fields in generated partial classes.
- C# expression support in XAML via `{cs: ...}`. This feature mirrors the MAUI preview in .NET MAUI 11. Not available in VB.NET projects.
- Simpler XAML support (implicit standard namespaces for WXSG parsing). This feature mirrors the MAUI preview in .NET MAUI 10.

## Quick Start (Project Consumption)

WXSG is published as a NuGet package. [![NuGet Version](https://img.shields.io/nuget/v/XamlToCSharpGenerator.Generator.WPF.svg?style=flat-square&label=WXSG)](https://www.nuget.org/packages/XamlToCSharpGenerator.Generator.WPF/)

1. Add WXSG package reference:

```xml
<ItemGroup>
  <PackageReference Include="XamlToCSharpGenerator.Generator.WPF" Version="0.1.1" PrivateAssets="all" />
</ItemGroup>
```

2. Enable WPF along with WXSG in your `.csproj`:

```xml
<PropertyGroup>
  <UseWPF>true</UseWPF>
  <WpfXsgEnabled>true</WpfXsgEnabled>
</PropertyGroup>
```

> VB.NET projects must add `<WpfXsgTargetLanguage>VisualBasic</WpfXsgTargetLanguage>`.

That is enough for normal package usage because the package ships `buildTransitive` props/targets.

## Classic .NET Framework WPF Projects

WXSG also works with classic non-SDK `.csproj` WPF projects when they build through a
modern Roslyn-based MSBuild toolset (for example Visual Studio 2022 MSBuild).

1. Install the NuGet package into the project.
   Old `packages.config` projects are supported through the package's `build` assets.

2. Enable WXSG in the project file:

```xml
<PropertyGroup>
  <WpfXsgEnabled>true</WpfXsgEnabled>
</PropertyGroup>
```

3. Build with a modern MSBuild/Roslyn toolset.
   WXSG automatically raises legacy C# defaults (`LangVersion` blank, `default`, or `7.3`)
   to `9.0`, because classic WPF projects commonly compile as C# 7.3 while WXSG-generated
   code needs newer syntax (nullable reference types and `not`-patterns require C# 8+/9+).

   > **Note:** `Microsoft.CSharp.Core.targets` in VS 2022 caps `LangVersion` to `7.3` for all
   > `.NETFramework` targets. WXSG's `.targets` file is imported after that cap is applied, so it
   > can override the value. Projects that explicitly set `LangVersion` to a higher value (e.g.
   > `latest`) are not affected.

4. Target .NET Framework 4.8 (or later).
   Old projects targeting .NET 4.0 or 4.5 can be retargeted by changing
   `<TargetFrameworkVersion>v4.0</TargetFrameworkVersion>` to `v4.8` in each project file.
   The VS 2022 `.NETFramework,v4.8` targeting pack is installed automatically with Visual Studio.

   When a project library has custom MSBuild platform names (e.g. `Net35`/`Net40`/`WithNRefactory`)
   instead of the standard `AnyCPU`, it needs an `AnyCPU` platform condition so that downstream
   consumers that build through the standard `Debug|Any CPU` solution configuration still pick up
   the correct `DefineConstants` and `TargetFrameworkVersion`.

If your NuGet client does not automatically add analyzer/import entries for a classic
project, add them explicitly from the installed package path:

```xml
<ItemGroup>
  <Analyzer Include="..\packages\XamlToCSharpGenerator.Generator.WPF.<version>\analyzers\dotnet\cs\XamlToCSharpGenerator.Generator.WPF.dll" />
  <Analyzer Include="..\packages\XamlToCSharpGenerator.Generator.WPF.<version>\analyzers\dotnet\cs\XamlToCSharpGenerator.WPF.dll" />
  <Analyzer Include="..\packages\XamlToCSharpGenerator.Generator.WPF.<version>\analyzers\dotnet\cs\XamlToCSharpGenerator.Compiler.dll" />
  <Analyzer Include="..\packages\XamlToCSharpGenerator.Generator.WPF.<version>\analyzers\dotnet\cs\XamlToCSharpGenerator.Core.dll" />
  <Analyzer Include="..\packages\XamlToCSharpGenerator.Generator.WPF.<version>\analyzers\dotnet\cs\XamlToCSharpGenerator.ExpressionSemantics.dll" />
  <Analyzer Include="..\packages\XamlToCSharpGenerator.Generator.WPF.<version>\analyzers\dotnet\cs\XamlToCSharpGenerator.Framework.Abstractions.dll" />
  <Analyzer Include="..\packages\XamlToCSharpGenerator.Generator.WPF.<version>\analyzers\dotnet\cs\XamlToCSharpGenerator.MiniLanguageParsing.dll" />
</ItemGroup>

<Import Project="..\packages\XamlToCSharpGenerator.Generator.WPF.<version>\build\XamlToCSharpGenerator.Generator.WPF.props"
        Condition="Exists('..\packages\XamlToCSharpGenerator.Generator.WPF.<version>\build\XamlToCSharpGenerator.Generator.WPF.props')" />
<Import Project="$(MSBuildBinPath)\Microsoft.CSharp.targets" />
<Import Project="..\packages\XamlToCSharpGenerator.Generator.WPF.<version>\build\XamlToCSharpGenerator.Generator.WPF.targets"
        Condition="Exists('..\packages\XamlToCSharpGenerator.Generator.WPF.<version>\build\XamlToCSharpGenerator.Generator.WPF.targets')" />
```

That manual fallback was validated locally with a legacy WPF project in classic MSBuild format.

## XAML Features Example

From the sample:

```xml
<Window xmlns:local="clr-namespace:sample"
        x:Class="sample.MainWindow"
        Title="{cs: string.Concat(&quot;WXSG — &quot;, &quot;C# Expressions + Simpler XAML&quot;)}"
        Width="{cs: 520}"
        Height="{cs: 420}">

  <Window.Resources>
    <local:GreetingViewModel x:Key="Vm" />
  </Window.Resources>

  <Grid DataContext="{StaticResource Vm}" Margin="20">
    <TextBlock x:Name="GreetingLabel" Text="{Binding Greeting}" />
  </Grid>
</Window>
```

You can see that both C# expressions and simpler XAML features are showed.

## Custom Markup Extensions

WXSG handles the standard WPF markup extensions (`{Binding}`, `{StaticResource}`, `{DynamicResource}`, `{x:Static}`, `{x:Type}`, etc.) itself.

For **custom markup extensions** declared via a custom XAML namespace prefix (for example `{core:Localize Key}` or `{sd:Theme Value}`), WXSG follows the standard WPF extensibility model:

1. Your markup extension class must extend `System.Windows.Markup.MarkupExtension`.
2. Its assembly must carry `[assembly: XmlnsDefinition("http://your-xmlns-uri", "Your.CLR.Namespace")]`.
3. Your XAML file maps the prefix to the URI: `xmlns:core="http://your-xmlns-uri"`.

At **runtime**, the WXSG-generated `InitializeComponent()` code:
- Finds the extension type (`LocalizeExtension`, `ThemeExtension`, etc.) by scanning loaded assemblies for `XmlnsDefinitionAttribute` matching the URI.
- Creates an instance (passing any positional constructor argument).
- Calls `ProvideValue(null)` on it.
- If the result is a `BindingBase`, installs it as a binding on the target property (so language-change bindings work automatically).
- Otherwise, assigns the scalar result to the property directly.

This means your project-specific markup extensions **just work** — no extra configuration in WXSG, no handler registration, no MSBuild properties to set.

## Notes

- `xmlns:local="clr-namespace:YourNamespace"` is still needed when referencing your own CLR types
  (for example view models in `Window.Resources`), unless you declare an equivalent global prefix.
- Simpler XAML here means you can omit repeated standard header declarations in WXSG input while
  still keeping normal WPF semantics.
- Well-known WPF apps like ILSpy and SharpDevelop 4 have been tested for compatibility, but WXSG is still under development and testing. Please open new issues if it doesn't work for your apps.

## Local Package Development Flow (Optional, Advanced)

> You only follow the steps below if you hit some issues with WXSG and need to debug further.

When developing WXSG from source (as in `vscode-wpf`), use a local package feed:

```xml
<PropertyGroup>
  <RestoreSources>$(MSBuildProjectDirectory)\..\..\artifacts\local-packages;$(RestoreSources)</RestoreSources>
</PropertyGroup>
```

Pack locally:

```powershell
dotnet pack src\XamlToCSharpGenerator.Generator.WPF\XamlToCSharpGenerator.Generator.WPF.csproj -c Debug -o artifacts\local-packages
```

Then restore/build:

```powershell
dotnet restore sample\net6.0-csharp-expressions\sample.csproj --no-cache
dotnet build sample\net6.0-csharp-expressions\sample.csproj
```

## License

MIT

## Copyright

2026 (c) LeXtudio Inc. All rights reserved.
