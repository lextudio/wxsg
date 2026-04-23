# MergedDictionaries Bug Reproduction Sample

## Overview

This sample targets issue #1 in the wxsg repository:
https://github.com/lextudio/wxsg/issues/1

It focuses on `Application.Resources` with a nested
`ResourceDictionary.MergedDictionaries` entry.

## What this sample checks

WXSG should emit a one-argument `Add(...)` call for:

```csharp
__resourceDictionary.MergedDictionaries.Add(__mergedDictionary);
```

It must not emit the keyed dictionary form:

```csharp
__resourceDictionary.MergedDictionaries.Add("System.Windows.ResourceDictionary", __mergedDictionary);
```

## How to run

```bash
cd c:/Users/lextudio/source/repos/WpfDesigner/external/wxsg/samples/mergeddictionaries
dotnet build
```
