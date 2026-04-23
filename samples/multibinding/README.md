# MultiBinding ItemsSource Bug Reproduction Sample

## Overview

This sample reproduces issue #3 in the wxsg (XAML Source Generator for WPF) repository:
https://github.com/lextudio/wxsg/issues/3

## The Bug

When using `MultiBinding` on an `ItemsSource` property, the XAML-to-C# code generator produces invalid C# code that fails to compile with:

```
error CS1061: 'IEnumerable' does not contain a definition for 'Add'
```

### Root Cause

The generator treats the MultiBinding as a collection item and generates:
```csharp
__node8.ItemsSource.Add(__node9);  // WRONG - ItemsSource is IEnumerable, doesn't have Add()
```

Instead of using binding APIs to attach the binding:
```csharp
global::System.Windows.Data.BindingOperations.SetBinding(__node8, global::System.Windows.Controls.ListBox.ItemsSourceProperty, __node9);
```

## Sample Details

### Files

- **MainWindow.xaml** - The UI definition showing a ListBox with MultiBinding on ItemsSource
- **MainWindow.xaml.cs** - Code-behind with sample data
- **DataConverter.cs** - Custom IMultiValueConverter to filter the list
- **Program.cs** - Application entry point
- **MultiBindingSample.csproj** - Project file with WPF and WXSG configuration

### How to Reproduce

```bash
cd c:/Users/lextudio/source/repos/WpfDesigner/external/wxsg/samples/multibinding
dotnet build
```

### Expected Behavior

The build should succeed and the application should run, displaying a ListBox with items that can be filtered using the TextBox and CheckBox above it.

### Actual Behavior

The build fails with CS1061 error on the generated code at line 92 of the generated MainWindow file.

## Generated Code Issue

The problematic line in the generated code:
```csharp
// From: WPF.MultiBindingSample_MainWindow.wpf.g.cs, line 92
__node8.ItemsSource.Add(__node9);
```

Should be:
```csharp
global::System.Windows.Data.BindingOperations.SetBinding(
    __node8, 
    global::System.Windows.Controls.ListBox.ItemsSourceProperty, 
    __node9);
```
