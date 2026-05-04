# Outline Inspector

This utility inspects runtime WPF object/resource state without changing app source code.

## Modes

- Default (`sharpdevelop-startpage`): probes `StartPage.dll` image/resource loading and control creation.
- `sharpdevelop-workbench`: probes the SharpDevelop main window type (`WpfWorkbench`) and dumps runtime resources/visual tree to diagnose black client-area issues.
- `xamldesigner`: probes `WpfDesigner/XamlDesigner` outline/runtime internals.

## Run

From repository root:

```powershell
# Default: SharpDevelop StartPage probe
dotnet run --project .\wxsg\diagnostics\OutlineInspector\outline-inspector.csproj -f net48 -c Debug

# SharpDevelop main workbench probe
dotnet run --project .\wxsg\diagnostics\OutlineInspector\outline-inspector.csproj -f net48 -c Debug -- sharpdevelop-workbench

# SharpDevelop workbench probe with custom add-in exclusions
dotnet run --project .\wxsg\diagnostics\OutlineInspector\outline-inspector.csproj -f net48 -c Debug -- sharpdevelop-workbench --exclude-addin=TypeScriptBinding

# XamlDesigner probe
dotnet run --project .\wxsg\diagnostics\OutlineInspector\outline-inspector.csproj -f net10.0-windows -c Debug -- xamldesigner
```

## What It Dumps

- Assembly resources (`.g.resources`) and pack URI probe results.
- `Application.Current.Resources` and merged dictionaries.
- Binding warnings/errors (`PresentationTraceSources`).
- Dependency property local values and value-source details.
- NameScope contents (when available).
- Visual tree snapshot (depth-limited).

For black-window diagnosis, compare `ResourceProbe` and background/foreground-related values between `WpfWorkbench`, `dockPanel`, and `mainContent`.

`sharpdevelop-workbench` excludes `TypeScriptBinding` by default to avoid known `BadImageFormatException` during host bootstrap. Add more filters by repeating `--exclude-addin=Pattern`.

