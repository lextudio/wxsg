@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "PACKAGE_DIR=%~1"

if "%PACKAGE_DIR%"=="" (
  set "PACKAGE_DIR=artifacts\nuget"
)

pushd "%SCRIPT_DIR%" >nul

if "%~2" neq "" (
  set "NUGET_API_KEY=%~2"
)

set "NUGET_EXE=nuget.exe"

if not exist "%NUGET_EXE%" (
  echo Downloading nuget.exe...
  powershell -Command "Invoke-WebRequest https://dist.nuget.org/win-x86-commandline/latest/nuget.exe -OutFile nuget.exe"
  if errorlevel 1 goto :fail
)

%NUGET_EXE% update /self
if errorlevel 1 goto :fail

for %%f in (%PACKAGE_DIR%\*.nupkg) do (
  %NUGET_EXE% push %%f -Source https://www.nuget.org/api/v2/package -ApiKey "%NUGET_API_KEY%"
  if errorlevel 1 goto :fail
)

echo Publish complete.
popd >nul
exit /b 0

:fail
set "EXIT_CODE=%errorlevel%"
echo Push failed with exit code %EXIT_CODE%.
popd >nul
exit /b %EXIT_CODE%
