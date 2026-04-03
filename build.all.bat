@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
pushd "%SCRIPT_DIR%" >nul

set "PROJECT=src\XamlToCSharpGenerator.Generator.WPF\XamlToCSharpGenerator.Generator.WPF.csproj"
set "CONFIGURATION=Release"

echo Restoring %PROJECT%...
dotnet restore "%PROJECT%"
if errorlevel 1 goto :fail

echo Building %PROJECT% (%CONFIGURATION%)...
dotnet build "%PROJECT%" -c "%CONFIGURATION%" --no-restore
if errorlevel 1 goto :fail

popd >nul
exit /b 0

:fail
set "EXIT_CODE=%errorlevel%"
echo Failed with exit code %EXIT_CODE%.
popd >nul
exit /b %EXIT_CODE%
