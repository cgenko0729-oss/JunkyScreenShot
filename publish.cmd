@echo off
rem Builds a self-contained single-file JunkyScreenShot.exe.
rem The exe runs on any 64-bit Windows PC, no .NET installation needed.
rem
rem Note: IncludeAllContentForSelfExtract is required because WPF's native
rem DLLs do not support the normal single-file bundling (the app crashes with
rem DllNotFoundException otherwise). This mode extracts to %%TEMP%% on first run.

dotnet publish "%~dp0JunkyScreenShot.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none

echo.
echo Output: bin\Release\net8.0-windows\win-x64\publish\JunkyScreenShot.exe
pause
