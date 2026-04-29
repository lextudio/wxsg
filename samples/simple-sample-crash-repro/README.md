# SimpleSample Crash Repro

This sample reproduces the toolbox-selection shape from WpfDesigner SimpleSample.

- The `ListBox` appears before a later named `designSurface` element, matching the original layout shape.
- The fake design surface mirrors the SimpleSample dependency chain: `designSurface.DesignPanel.Context.Services.Tool.CurrentTool`.
- During startup the sample scans `ExtensionForAttribute` metadata before creating `DesignPanel`, matching WpfDesign extension registration.
- If metadata reflection fails, the sample logs `LoadDesigner FAILED`, leaves `DesignPanel` null, and the toolbox selection fails like the original SimpleSample click.
- On `ContentRendered` the sample sets `lstControls.SelectedIndex = 2` to trigger the TextBox handler path.
- The assembly also contains `ExtensionForAttribute` usages with named `Type` and `Type[]` arguments, matching the metadata shapes used by WpfDesigner extensions.
- `Themes/Generic.xaml` references a local type so WXSG exercises the deferred-BAML injection path that rewrites assembly metadata.
- On success the window closes, prints `OK:` status lines, and the process exits with code 0.

Run locally with `dotnet run --project samples/simple-sample-crash-repro`.
