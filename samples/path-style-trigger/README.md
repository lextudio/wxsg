# PathStyleTriggerSample

Purpose
- Regression coverage for [lextudio/wxsg#12](https://github.com/lextudio/wxsg/issues/12):
  - **Issue 1**: `<Style TargetType="Path">` must resolve to `System.Windows.Shapes.Path`,
    not `System.IO.Path`. Misresolution throws
    `'Path' type must be derived from FrameworkElement` at `Style.set_TargetType`.
  - **Issue 2**: `<DataTrigger Binding="{Binding IsBool}" Value="False">` must compare
    correctly against the bound `bool` source.

Run (manual)
- `dotnet build -c Debug`
- `dotnet run --no-build`

Automation contract
- Writes `WXSG-SAMPLE-OK` to stdout and exits 0 on success.
- Writes `WXSG-SAMPLE-ERROR: ...` to stderr and exits non-zero on failure.

What the self-check verifies
- The Window loads (no exception during `InitializeComponent` — confirms Issue 1).
- `probePath.Width == 20` from the style (confirms style applied).
- `probePath.Stroke == Blue` from the DataTrigger override (confirms Issue 2).
