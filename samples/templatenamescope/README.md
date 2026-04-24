# Template NameScope Sample

## Overview

This sample exercises the name-scope behavior inside `DataTemplate` and
`ControlTemplate` instances. It verifies that template-local `x:Name` targets
are resolvable from template triggers and setters after XAML is converted to
C# by the source generator.

## Files

- `MainWindow.xaml` — defines a `DataTemplate` and a styled `Button` with
  template-local `x:Name` and triggers
- `MainWindow.xaml.cs` — code-behind
- `TemplateNameScopeSample.csproj` — project file

## How to run

```powershell
cd external\wxsg\samples\templatenamescope
dotnet build
dotnet run
```

Expected result: application runs and the templated controls behave normally
(e.g., the template `Path` changes fill when selected or hovered as defined
by the template triggers).
