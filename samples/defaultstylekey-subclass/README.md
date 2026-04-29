DefaultStyleKey Subclass Sample
================================

Purpose
-------
Demonstrates a control subclass that relies on `DefaultStyleKey` and
template resolution. The sample includes small runtime checks used by the
regression harness.

Run (manual)
------------
- dotnet build -c Debug
- dotnet run --no-build --project defaultstylekey-subclass/DefaultStyleKeySubclassSample.csproj

Automation contract
-------------------
- This sample performs a non-interactive self-check after the main window
  renders. On success it writes `WXSG-SAMPLE-OK` to stdout and exits `0`.
- On failure it writes `WXSG-SAMPLE-ERROR: <ex>` to stderr and exits non-zero.
- Ensure the project `OutputType` is `Exe` so stdout/stderr are available.

Notes
-----
- Avoid blocking dialogs or interactive prompts in the sample. See
  `../../docs/regression-infra.md` for the full infra contract and examples.
