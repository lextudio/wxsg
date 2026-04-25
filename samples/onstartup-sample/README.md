# OnStartupSample

Small sample demonstrating an `Application` subclass that overrides `OnStartup`.

Purpose: reproduce and investigate generator behavior when an `Application` overrides
`OnStartup` instead of using `StartupUri`/XAML-based startup wiring.

Files:
- `App.xaml` / `App.xaml.cs` — application with `OnStartup` override.
- `MainWindow.xaml` / `MainWindow.xaml.cs` — simple window shown from `OnStartup`.
