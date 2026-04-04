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

## Notes

- `xmlns:local="clr-namespace:YourNamespace"` is still needed when referencing your own CLR types
  (for example view models in `Window.Resources`), unless you declare an equivalent global prefix.
- Simpler XAML here means you can omit repeated standard header declarations in WXSG input while
  still keeping normal WPF semantics.
- Well-known WPF apps like ILSpy have been tested for compatibility, but WXSG is still under development and testing. Please open new issues if it doesn't work for your apps.

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
