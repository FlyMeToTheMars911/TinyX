@echo off
setlocal

pushd "%~dp0" || exit /b 1
set "TINYX_DOTNET=dotnet"
if exist "work\tools\dotnet\dotnet.exe" set "TINYX_DOTNET=%CD%\work\tools\dotnet\dotnet.exe"
if not defined NUGET_PACKAGES if exist "work\nuget-packages\" set "NUGET_PACKAGES=%CD%\work\nuget-packages"

rem Let Windows PowerShell load its own modules even when launched from PowerShell 7.
set "PSModulePath="
powershell.exe -NoLogo -NoProfile -File "scripts\publish.ps1" -Dotnet "%TINYX_DOTNET%" %*
set "TINYX_PACK_EXIT_CODE=%ERRORLEVEL%"
popd
exit /b %TINYX_PACK_EXIT_CODE%
