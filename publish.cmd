@echo off
rem Publishes the Release VSIX to the Visual Studio Marketplace.
rem
rem Usage:  publish.cmd            (PAT read from the VSIX_PAT environment variable)
rem         publish.cmd <PAT>      (PAT passed as the first argument)
rem
rem The PAT is an Azure DevOps personal access token (https://dev.azure.com/<you>/_usersSettings/tokens)
rem with Organization = "All accessible organizations" and scope Marketplace -> Manage.
rem GitHub PATs do NOT work here.
rem
rem NOTE: the very first upload for a new extension is done manually (drag & drop at
rem https://marketplace.visualstudio.com/manage/publishers/michaeljordanlxman).
rem This script handles every update after that.

setlocal
set "PAT=%~1"
if "%PAT%"=="" set "PAT=%VSIX_PAT%"
if "%PAT%"=="" (
    echo ERROR: no PAT. Pass it as the first argument or set VSIX_PAT.
    exit /b 1
)

set "VSIX=%~dp0src\WindowLayoutManager\bin\Release\net472\WindowLayoutManager.vsix"
if not exist "%VSIX%" (
    echo ERROR: %VSIX% not found. Run build.cmd first.
    exit /b 1
)

set "PUBLISHER_EXE=C:\Program Files\Microsoft Visual Studio\18\Community\VSSDK\VisualStudioIntegration\Tools\Bin\VsixPublisher.exe"
"%PUBLISHER_EXE%" publish -payload "%VSIX%" -publishManifest "%~dp0publishManifest.json" -personalAccessToken "%PAT%"
exit /b %errorlevel%
