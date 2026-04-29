Theme x:Static Repro
=====================

Purpose
-------
Reproducer for theme resource dictionaries that use `{x:Static ...}` for
resource keys (e.g., `{x:Static themes:ResourceKeys.TextBackgroundBrush}`).
The sample includes a non-interactive self-check used by the regression harness.

Run (manual)
------------
- dotnet build -c Debug
- dotnet run --no-build --project theme-xstatic-repro/ThemeXStaticRepro.csproj

Automation contract
-------------------
- After the main window renders the sample writes `WXSG-SAMPLE-OK` to stdout
  and exits `0` when the minimal self-check succeeds.
- On failure it writes `WXSG-SAMPLE-ERROR: <ex>` to stderr and exits non-zero.
- Ensure the project `OutputType` is `Exe` so stdout/stderr are available to
  the test harness.

Notes
-----
- See `../../docs/regression-infra.md` for recommended csproj settings and the
  full sample contract.
