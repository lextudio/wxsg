# EventSetter Loaded repro (wxsg issue #5)

## Overview

This sample reproduces issue #5 in the wxsg repository:
https://github.com/lextudio/wxsg/issues/5

Using an `EventSetter` with `Event="Loaded"` and `Handler="OnLoaded"`
produces runtime code that tries to convert the string token to a
`RoutedEvent`/`Delegate` using `TypeDescriptor` converters, which throws
`NotSupportedException` (RoutedEventConverter cannot convert from System.String).

## Files

- `MainWindow.xaml` — declares a `Style` with `<EventSetter Event="Loaded" Handler="OnLoaded" />`.
- `MainWindow.xaml.cs` — provides the `OnLoaded` handler.
- `Program.cs` — runs the app and prints unhandled exceptions to console.
- `EventSetterLoadedSample.csproj` — project file wired to the generator.

## How to reproduce

```powershell
cd external\wxsg\samples\eventsetter-loaded
dotnet build
dotnet run
```

Expected behavior: the app should run and call the `OnLoaded` handler.

Actual behavior: the generated code throws `System.NotSupportedException: 'RoutedEventConverter cannot convert from System.String.'` during initialization.

## Notes

- This sample is intentionally minimal to highlight the incorrect conversion
  approach in the generated code for `EventSetter` values.
