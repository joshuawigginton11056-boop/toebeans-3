@echo off
setlocal enabledelayedexpansion

rem SETUP-LAPTOP.cmd - double-click this after cloning the project.
rem
rem Configures the per-clone git settings that do not travel with a clone,
rem then pulls down the model and texture files. No terminal, no typing.
rem Safe to run more than once.

cd /d "%~dp0"

echo.
echo  Toebeans-3 laptop setup
echo  =======================
echo.

set "BASH="

for %%P in (
  "%ProgramFiles%\Git\bin\bash.exe"
  "%ProgramFiles(x86)%\Git\bin\bash.exe"
  "%LocalAppData%\Programs\Git\bin\bash.exe"
  "%ProgramW6432%\Git\bin\bash.exe"
) do (
  if not defined BASH if exist "%%~P" set "BASH=%%~P"
)

rem Fall back to whatever bash is on PATH.
if not defined BASH (
  for /f "delims=" %%B in ('where bash.exe 2^>nul') do (
    if not defined BASH set "BASH=%%B"
  )
)

if not defined BASH (
  echo  Could not find Git for Windows on this machine.
  echo.
  echo  Install it from:  https://gitforwindows.org
  echo  Accept every default, then double-click this file again.
  echo.
  echo  GitHub Desktop on its own is not enough - it hides its copy of git.
  echo.
  pause
  exit /b 1
)

echo  Using: !BASH!
echo.

"!BASH!" "Tools/setup-machine.sh"
set "RESULT=%ERRORLEVEL%"

echo.
if "%RESULT%"=="0" (
  echo  ================================================================
  echo   Ready. Open the project in Unity Hub and start working.
  echo  ================================================================
) else (
  echo  ================================================================
  echo   Something above needs fixing - read the FAIL lines.
  echo  ================================================================
)

echo.
pause
exit /b %RESULT%
