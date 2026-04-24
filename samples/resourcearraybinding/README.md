# Resource Array Binding Sample

## Overview

This sample demonstrates using an `x:Array` declared in a `ResourceDictionary`
and binding a control's `ItemsSource` to that static resource (via
`{StaticResource toolBoxItems}`). It's a small verification that the XAML-to-C#
generator preserves array resources and static resource bindings correctly.

## Files

- `App.xaml` — application entry XAML
- `MainWindow.xaml` — shows an `x:Array` resource and a `ListBox` bound to it
- `MainWindow.xaml.cs` — code-behind (DataContext / initialization)
- `ToolBoxItem.cs` — simple model used in the array
- `ResourceArrayBindingSample.csproj` — project file

## How to run

```powershell
cd external\wxsg\samples\resourcearraybinding
dotnet build
dotnet run
```

Expected result: the application runs and displays a ListBox containing
"Button" and "TextBox" (values from the `x:Array` resource).
