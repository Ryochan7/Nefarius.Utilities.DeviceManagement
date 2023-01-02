@echo off

REM Run from Developer Command Prompt for VS 2022

set assemblyVersion=3.5.209.0

set mypath=%~dp0
cd %mypath%

dotnet build .\Nefarius.Utilities.DeviceManagement.sln --configuration Release /p:Version=%assemblyVersion% /p:AssemblyVersion=%assemblyVersion% /p:FileVersion=%assemblyVersion%
