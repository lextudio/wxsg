# MultiBinding Property Bug Reproduction Sample

## Overview

This sample targets issue #2 in the wxsg repository:
https://github.com/lextudio/wxsg/issues/2

It focuses on `MultiBinding` used through property-element syntax on dependency
properties such as `Text` and `Visibility`.

## What this sample checks

WXSG should emit binding attachment code such as:

```csharp
global::System.Windows.Data.BindingOperations.SetBinding(
    __node,
    global::System.Windows.Controls.TextBlock.TextProperty,
    __multiBinding);
```

and:

```csharp
global::System.Windows.Data.BindingOperations.SetBinding(
    __node,
    global::System.Windows.UIElement.VisibilityProperty,
    __multiBinding);
```

It must not emit direct CLR assignment like:

```csharp
__node.Text = __multiBinding;
__node.Visibility = __multiBinding;
```

## How to run

```bash
cd c:/Users/lextudio/source/repos/WpfDesigner/external/wxsg/samples/multibinding-properties
dotnet build
dotnet run
```
