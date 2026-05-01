# Outline Inspector

This utility loads the built `WpfDesigner/XamlDesigner` app and inspects the outline pane without modifying WpfDesigner sources.

Run it from the repository root after rebuilding the WXSG-instrumented WpfDesigner binaries:

```powershell
dotnet run --project .\wxsg\diagnostics\OutlineInspector\outline-inspector.csproj -c Debug
```

Useful success signals include:

- `WindowClone instance` is not an exception.
- `CurrentDocument.XamlErrors.Count: 0`
- `CurrentDocument.OutlineRoot` is not null.
- `Outline[0].OutlineTreeView.Items.Count` is greater than zero.

The inspector also captures runtime XAML internals to help diagnose WXSG/runtime mismatches:

- Application and window `ResourceDictionary` trees (including merged dictionaries and sample keys).
- Binding diagnostics (`PresentationTraceSources`) for warnings/errors.
- Per-element local dependency property value sources (`DependencyPropertyHelper.GetValueSource`) and active binding expressions.
- NameScope content (when the underlying implementation exposes its map).
- Visual tree snapshot for quick structure validation.

If theme/resource behavior is suspicious, compare the `ResourceProbe` section between controls that render correctly and controls that do not.

