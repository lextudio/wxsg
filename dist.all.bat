@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
pushd "%SCRIPT_DIR%" >nul

if /i "%~1"=="help" goto :usage
if /i "%~1"=="--help" goto :usage
if /i "%~1"=="/?" goto :usage

call build.all.bat
if %errorlevel% neq 0 exit /b %errorlevel%
call pack.all.bat
if %errorlevel% neq 0 exit /b %errorlevel%

if /i "%~1"=="publish" (
  set "OUTPUT_DIR=artifacts\nuget"
  call "%SCRIPT_DIR%dist.publish2nugetdotorg.bat" "%OUTPUT_DIR%" "%~2"
  if errorlevel 1 goto :fail
)

echo Done.
popd >nul
exit /b 0

:usage
echo Usage:
echo   dist.all.bat
echo   dist.all.bat publish [NUGET_API_KEY]
echo.
echo Set NUGET_API_KEY as an environment variable or pass it as argument 2 when publishing.
popd >nul
exit /b 0

:fail
set "EXIT_CODE=%errorlevel%"
echo Failed with exit code %EXIT_CODE%.
popd >nul
exit /b %EXIT_CODE%
