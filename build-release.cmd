@echo off
setlocal
pushd "%~dp0"
dotnet publish Notepad.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true -p:UsedAvaloniaProducts=
if errorlevel 1 (
    popd
    exit /b 1
)
set "PUBLISH=bin\Release\net9.0\win-x64\publish"
echo Published %PUBLISH%\Notepad.exe
if /i "%~1"=="--verify" (
    if not exist "%CD%\obj\release-smoke" mkdir "%CD%\obj\release-smoke"
    call :verify
    if errorlevel 1 (
        popd
        exit /b 1
    )
)
popd
exit /b 0

:verify
set "SMOKE=%CD%\obj\release-smoke\smoke-%RANDOM%-%RANDOM%.log"
start "" /wait "%PUBLISH%\Notepad.exe" --verify-release "%SMOKE%"
set "RESULT=%ERRORLEVEL%"
if not exist "%SMOKE%" exit /b 1
type "%SMOKE%"
exit /b %RESULT%
