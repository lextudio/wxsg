Regression testing infra notes
=============================

Purpose
-------
Provide a clear, minimal contract for WXSG samples so automated regression
jobs can detect: build-time failures, startup exceptions, and whether the UI
renders correctly. The contract favors deterministic, non-interactive checks
that are easy to run and parse from CI/test harnesses.

Quick checklist for any new sample
---------------------------------
- `OutputType` must be `Exe` (so stdout/stderr are available to the test harness).
- `UseWPF` must be `true` and the sample must enable the WXSG settings used
  by the generator in this repo (examples below).
- Add global exception wiring in `App.xaml.cs` or `Program.cs` so pre-UI
  exceptions are written to `Console.Error` and cause a non-zero exit.
- Add a short, non-interactive self-check that runs after `ContentRendered` on
  the main window. On success write `WXSG-SAMPLE-OK` and `Environment.Exit(0)`.
  On failure write `WXSG-SAMPLE-ERROR: <ex>` to `Console.Error` and exit `1`.
- No blocking UI or interactive prompts (no `MessageBox.Show`, `ShowDialog`,
  `Console.ReadLine`, or file dialogs) in the normal execution path used by
  automation. If manual interaction is needed, gate it behind a command-line
  flag such as `inspect` or `interactive`.
- Add a short `README.md` describing the sample purpose and the automation
  contract (how to run manually and what the harness expects).

Why `Exe` (not `WinExe`)
-----------------------
WPF `WinExe` suppresses the console window. Tests rely on reading `stdout` and
`stderr` from the process to detect the `WXSG-SAMPLE-OK` token and any
error output. Using `Exe` ensures a console is available and output is
captured reliably by the harness.

Recommended csproj snippet (minimal)
-----------------------------------
Use the following as a baseline for samples:

```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <TargetFramework>net10.0-windows</TargetFramework>
  <UseWPF>true</UseWPF>
  <WpfXsgEnabled>true</WpfXsgEnabled>
  <WpfXsgCSharpMode>true</WpfXsgCSharpMode>
  <WpfXsgEmitGeneratedFiles>true</WpfXsgEmitGeneratedFiles>
  <WpfXsgLanguageSupported>true</WpfXsgLanguageSupported>
  <XamlSourceGenEnabled>true</XamlSourceGenEnabled>
  <CompilerGeneratedFilesOutputPath>$(IntermediateOutputPath)generated\</CompilerGeneratedFilesOutputPath>
  <LangVersion>latest</LangVersion>
  <Nullable>enable</Nullable>
</PropertyGroup>
```

Global exception wiring (example)
--------------------------------
Place this in `App.xaml.cs` (override `OnStartup`) or the `Program.Main` entry
so exceptions that occur during startup are visible to the harness:

```csharp
protected override void OnStartup(StartupEventArgs e)
{
    AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
    {
        Console.Error.WriteLine(ev.ExceptionObject?.ToString());
        Environment.Exit(1);
    };

    this.DispatcherUnhandledException += (_, ev) =>
    {
        Console.Error.WriteLine(ev.Exception?.ToString());
        ev.Handled = true;
        Environment.Exit(1);
    };

    TaskScheduler.UnobservedTaskException += (_, ev) =>
    {
        Console.Error.WriteLine(ev.Exception?.ToString());
        ev.SetObserved();
        Environment.Exit(1);
    };

    base.OnStartup(e);
}
```

Main-window self-check (example)
--------------------------------
Keep the check tiny and deterministic. Subscribe to `ContentRendered` and
perform the minimal validation needed for the sample (e.g., resource lookup,
DataContext type, simple method call). On success write `WXSG-SAMPLE-OK`
and `Environment.Exit(0)`; on failure write a descriptive message to
`Console.Error` and exit non-zero.

```csharp
public MainWindow()
{
    InitializeComponent();

    this.ContentRendered += async (_, __) =>
    {
        try
        {
            await Task.Yield(); // allow layout to complete
            Console.WriteLine("WXSG-SAMPLE-OK");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("WXSG-SAMPLE-ERROR: " + ex);
            Environment.Exit(1);
        }
    };
}
```

Handling samples that require clicks or other UI interaction
-----------------------------------------------------------
If the sample's bug reproduction requires a button click, double-click, or
complex interaction, prefer one of these approaches (ordered by preference):

- Make the meaningful action programmatic and call it from the self-check.
  For example, expose a `SelfCheck()` or `RunScenario()` method on the
  `MainWindow` that performs the action the same way a user would; call it
  from `ContentRendered`.

- Provide an `inspect` / `automation` command-line mode in `Program.Main`
  that builds the same scenario programmatically (or uses reflection to
  call into the window) so tests can exercise the behavior without
  platform UI automation.

- If UI automation is unavoidable, keep the number of steps small and
  document exactly what the automation must do in the sample `README.md`.

Minimal `Program` inspect-mode example
-------------------------------------
This pattern is useful for samples that also want a manual debug path. It
lets you run the sample in an automated 'inspect' mode that invokes internal
checks or reflection-based helpers.

```csharp
var args = Environment.GetCommandLineArgs();
if (args.Length > 1 && args[1] == "inspect")
{
    // run programmatic checks, write logs, then exit
    // (for example: call an internal SelfCheck method by reflection)
    return;
}

// otherwise run the normal app
var app = new Application();
app.Run(new MainWindow());
```

Test-harness expectations and recommended MSBuild invocation
-----------------------------------------------------------
The test harness builds and then runs the sample. Recommended build
invocation used by `Wxsg.Tests` is similar to:

```powershell
dotnet build "path\to\Sample.csproj" --no-restore -c Debug -t:Rebuild \
  -p:EmitCompilerGeneratedFiles=true \
  -p:CompilerGeneratedFilesOutputPath="<temp>" \
  -p:BaseOutputPath="<temp>\bin" \
  -p:BaseIntermediateOutputPath="<temp>\obj"
```

Notes:
- Redirecting `BaseOutputPath` / `BaseIntermediateOutputPath` into a
  per-test temporary folder avoids locking files in the repo `bin`/`obj`
  directories during parallel builds.
- The harness then runs the sample executable and asserts:
  - Exit code `0` and stdout contains `WXSG-SAMPLE-OK` => success
  - Any stderr output or `WXSG-SAMPLE-ERROR` on stdout => failure

README template for samples
---------------------------
Each sample should provide a small `README.md` that documents:

- Purpose: a one-sentence description of the sample and the scenario it
  exercises.
- How to run manually: `dotnet build`, `dotnet run --no-build --project ...`,
  or `dotnet <path-to-dll>`.
- The automation contract: mention `WXSG-SAMPLE-OK` / `WXSG-SAMPLE-ERROR`,
  and `OutputType` must be `Exe`.
- Any special modes (e.g., `inspect`) and how to invoke them.

Example README skeleton
-----------------------

```
# SampleName

Purpose
- Short description of what this sample demonstrates.

Run (manual)
- dotnet build -c Debug
- dotnet run --no-build --project SampleName.csproj

Automation contract
- This sample writes `WXSG-SAMPLE-OK` to stdout and exits 0 when the
  minimal self-check passes. On failure it writes `WXSG-SAMPLE-ERROR: ...`
  to stderr and exits non-zero.

Special modes
- `dotnet run --project SampleName.csproj -- inspect` — programmatic inspect mode.
```

Checklist when adding a new sample
---------------------------------
1. Add/update `SampleName.csproj` using the recommended csproj snippet.
2. Add global exception wiring in `App.xaml.cs` / `Program.cs`.
3. Add `ContentRendered` self-check in `MainWindow` or a `SelfCheck()` method
   invoked by `ContentRendered`.
4. Verify no blocking dialogs or prompts.
5. Add `README.md` containing the automation contract and manual run steps.
6. Add a regression test entry in `Wxsg.Tests` (if appropriate) that builds
   and runs the sample, asserting on `WXSG-SAMPLE-OK` and exit code.

Troubleshooting
---------------
- File locked during build: ensure the test harness uses unique `BaseOutputPath`
  and `BaseIntermediateOutputPath` for each run. If a process still holds a
  handle, identify and stop it (PowerShell/Terminal that ran the sample).
- Missing console output: confirm `OutputType` is `Exe` not `WinExe`.
- Interactive MessageBox observed: replace or gate behind a flag. Tests must
  be non-interactive.

Summary
-------
Following this contract keeps samples small, deterministic, and easy for the
regression harness to build and run. The key goals are: reliable stdout/stderr
capture (`OutputType=Exe`), global exception visibility, and a minimal,
non-interactive self-check after the UI renders.

If you'd like, I can add the README skeleton to the two samples we identified
(`defaultstylekey-subclass` and `theme-xstatic-repro`) and add example
`inspect` mode code to any sample that still requires interaction.
