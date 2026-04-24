# x:Static with custom XML namespace prefix (repro for issue #6)

## Overview

This sample reproduces wxsg issue #6:
https://github.com/lextudio/wxsg/issues/6

When using `{x:Static}` with a custom XML namespace prefix (for example
`xmlns:p="https://xxx.com/xaml"`), the generated runtime resolver throws
`InvalidOperationException: Unable to resolve x:Static member 'Converters.CollectionsToComposite'.`.

This sample shows the minimal XAML and supporting types to reproduce the
failure and to validate any fix to the generator.

## Files

- `MainWindow.xaml` — XAML that uses `xmlns:p="https://xxx.com/xaml"` and
  `{x:Static p:Converters.CollectionsToComposite}` in a `MultiBinding`.
- `Converters.cs` — `Converters` static class exposing `CollectionsToComposite`.
- `MainWindow.xaml.cs` — code-behind providing sample data.
- `Program.cs` — sample entry with an `inspect` mode to invoke the generated
  `__WXSG_ResolveXStatic` reflectively.
- `AssemblyInfo.cs` — `XmlnsDefinition` that maps the `https://xxx.com/xaml`
  XML namespace to the sample CLR namespace.

## How to reproduce

Build and run the sample in "inspect" mode so the generated resolver is
invoked without launching the GUI (useful for automated runs):

```powershell
dotnet build external\wxsg\samples\xstatic-customns\XStaticCustomNsSample.csproj
dotnet external\wxsg\samples\xstatic-customns\bin\Debug\net10.0-windows\XStaticCustomNsSample.dll inspect
```

Expected result: the inspect invocation should either print the resolved
object (converter instance) or indicate success. Current observed result is an
InvalidOperationException: the resolver fails to find the static member.

## Notes

- This README follows the style used by the other samples in the `samples/`
  folder (see `multibinding/README.md`).
- Use the `inspect` mode when running in CI or headless environments.
