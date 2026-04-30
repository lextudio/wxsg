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

