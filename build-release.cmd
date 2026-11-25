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
    start "" /wait "%PUBLISH%\Notepad.exe" --verify-release "%CD%\%PUBLISH%\release-smoke.log"
    if errorlevel 1 (
        if exist "%PUBLISH%\release-smoke.log" type "%PUBLISH%\release-smoke.log"
        popd
        exit /b 1
    )
    type "%PUBLISH%\release-smoke.log"
)
popd
exit /b 0
