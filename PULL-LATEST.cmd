@echo off
setlocal enabledelayedexpansion

rem PULL-LATEST.cmd - double-click this BEFORE you start working.
rem
rem Brings down whatever the other machine pushed, including the model and
rem texture files that live in Git LFS.
rem
rem It refuses to run if you have unsaved changes here, on purpose. Pulling
rem on top of uncommitted work is how scenes get mangled. Run
rem SAVE-AND-PUSH.cmd first if it stops you.

cd /d "%~dp0"

echo.
echo  Getting the latest version
echo  ==========================
echo.

call :findgit
if not defined GIT goto :nogit

rem Unstaged, staged, or untracked - any of the three means unsaved work.
set "DIRTY="
for /f "delims=" %%S in ('"!GIT!" status --porcelain 2^>nul') do set "DIRTY=1"

if defined DIRTY (
  echo  STOP - you have unsaved changes on this machine:
  echo.
  "!GIT!" status --short
  echo.
  echo  Pulling on top of these can wreck a Unity scene.
  echo.
  echo  Double-click SAVE-AND-PUSH.cmd first, then run this again.
  echo.
  pause
  exit /b 1
)

echo  Downloading...
echo.
"!GIT!" pull
if errorlevel 1 goto :pullfailed

"!GIT!" lfs pull
if errorlevel 1 echo  (LFS reported a problem - models may be incomplete)

echo.
echo  ================================================================
echo   Up to date. Open the project in Unity and go.
echo  ================================================================
echo.
pause
exit /b 0

:pullfailed
echo.
echo  ================================================================
echo   The pull did not finish. Read the message above.
echo  ================================================================
echo.
pause
exit /b 1

:nogit
echo  Could not find git on this machine.
echo  Install Git for Windows from https://gitforwindows.org
echo.
pause
exit /b 1

:findgit
set "GIT="
for %%P in (
  "%ProgramFiles%\Git\cmd\git.exe"
  "%ProgramFiles(x86)%\Git\cmd\git.exe"
  "%LocalAppData%\Programs\Git\cmd\git.exe"
  "%ProgramW6432%\Git\cmd\git.exe"
) do (
  if not defined GIT if exist "%%~P" set "GIT=%%~P"
)
if not defined GIT (
  for /f "delims=" %%B in ('where git.exe 2^>nul') do (
    if not defined GIT set "GIT=%%B"
  )
)
goto :eof
