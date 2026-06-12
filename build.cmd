@echo off
rem Builds the Release VSIX for marketplace publishing.
call "C:\Program Files\Microsoft Visual Studio\18\Community\Common7\Tools\VsDevCmd.bat" -arch=amd64 -no_logo
MSBuild.exe /restore /t:Rebuild /p:Configuration=Release "%~dp0src\WindowLayoutManager\WindowLayoutManager.csproj"
if errorlevel 1 exit /b 1
echo --- VSIX ---
dir /b "%~dp0src\WindowLayoutManager\bin\Release\net472\*.vsix" 2>nul
if errorlevel 1 (
    echo ERROR: build succeeded but no .vsix was found under bin\Release\net472
    exit /b 1
)
exit /b 0
