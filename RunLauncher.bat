@echo off
setlocal

set "PROJECT=%~dp0Windows\Launcher\KairosoftGameToolbox.csproj"

dotnet run --project "%PROJECT%" --configuration Debug
exit /b %errorlevel%
