@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
pushd "%SCRIPT_DIR%" >nul

set "PROJECT=src\XamlToCSharpGenerator.Generator.WPF\XamlToCSharpGenerator.Generator.WPF.csproj"
set "CONFIGURATION=Release"
set "OUTPUT_DIR=artifacts\nuget"

if exist "%OUTPUT_DIR%" (
  echo Cleaning old packages from %OUTPUT_DIR%...
  del /q "%OUTPUT_DIR%\*.nupkg" 2>nul
)

if not exist "%OUTPUT_DIR%" mkdir "%OUTPUT_DIR%"

echo Packing %PROJECT% to %OUTPUT_DIR%...
dotnet pack "%PROJECT%" -c "%CONFIGURATION%" --no-build -o "%OUTPUT_DIR%"
if errorlevel 1 goto :fail

echo Signing packages...
pwsh -ExecutionPolicy Bypass -file sign.ps1
if errorlevel 1 goto :fail

popd >nul
exit /b 0

:fail
set "EXIT_CODE=%errorlevel%"
echo Failed with exit code %EXIT_CODE%.
popd >nul
exit /b %EXIT_CODE%
