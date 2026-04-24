# MainView Sample

This sample demonstrates a WXSG-generated WPF app that starts from `MainView.xaml` instead of `MainWindow.xaml`.

Purpose
- Exercise WXSG behavior for projects that do not contain a `MainWindow` type.
- Reproducer / verification for issue: https://github.com/lextudio/wxsg/issues/8

Build & Run
```powershell
dotnet build ./MainViewSample.csproj
dotnet run --project ./MainViewSample.csproj
```

Notes
- The generator was updated to avoid assuming a `MainWindow` type exists; see the linked issue for details and discussion.
- If you see generated code referencing `MainWindow`, ensure the generator package in the solution is rebuilt from the local sources.

File: samples/mainview-sample
