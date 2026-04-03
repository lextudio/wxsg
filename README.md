# XAML Source Generator for WPF (WXSG)

WXSG replaces WPF's classic XAML/BAML codegen path with Roslyn source generation and emits
typed `InitializeComponent()` code directly in C#.

This repo's reference usage is:
`vscode-wpf/sample/net6.0-csharp-expressions`.

## What You Get

- Source-generated `InitializeComponent()` for WPF XAML.
- Typed `x:Name` fields in generated partial classes.
- C# expression support in XAML via `{cs: ...}`. Preview in .NET MAUI 11.
- Simpler XAML support (implicit standard namespaces for WXSG parsing). Preview in .NET MAUI 10.

## Quick Start (Project Consumption)

Published package page:
`https://www.nuget.org/packages/XamlToCSharpGenerator.Generator.WPF`

1. Add WXSG package reference:

```xml
<ItemGroup>
  <PackageReference Include="XamlToCSharpGenerator.Generator.WPF" Version="0.1.0" PrivateAssets="all" />
</ItemGroup>
```

2. Enable WXSG in your `.csproj`:

```xml
<PropertyGroup>
  <UseWPF>true</UseWPF>
  <WpfXsgEnabled>true</WpfXsgEnabled>
</PropertyGroup>
```

That is enough for normal package usage because the package ships `buildTransitive` props/targets.

## Local Package Development Flow (Optional, Advanced)

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

## XAML Example

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

## Notes

- `xmlns:local="clr-namespace:YourNamespace"` is still needed when referencing your own CLR types
  (for example view models in `Window.Resources`), unless you declare an equivalent global prefix.
- Simpler XAML here means you can omit repeated standard header declarations in WXSG input while
  still keeping normal WPF semantics.
