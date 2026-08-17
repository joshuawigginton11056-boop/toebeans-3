@echo off
setlocal enabledelayedexpansion

rem SAVE-AND-PUSH.cmd - double-click this WHEN YOU STOP working.
rem
rem Commits everything you changed and sends it to GitHub, so the other
rem machine can pick it up. Run it even if the work is half finished - an
rem unfinished commit costs nothing, and two machines both holding unpushed
rem edits to the same scene is the one situation git cannot sort out.

cd /d "%~dp0"

echo.
echo  Saving your work to GitHub
echo  ==========================
echo.

call :findgit
if not defined GIT goto :nogit

set "DIRTY="
for /f "delims=" %%S in ('"!GIT!" status --porcelain 2^>nul') do set "DIRTY=1"

if not defined DIRTY (
  echo  Nothing new to save. Checking for unsent commits...
  echo.
  goto :push
)

echo  About to save these changes:
echo.
"!GIT!" status --short
echo.
echo  ----------------------------------------------------------------
echo   Press any key to save and send. Close this window to cancel.
echo  ----------------------------------------------------------------
pause >nul

"!GIT!" add -A
if errorlevel 1 goto :failed

for /f "tokens=*" %%D in ('powershell -NoProfile -Command "Get-Date -Format \"yyyy-MM-dd HH:mm\""') do set "STAMP=%%D"

"!GIT!" commit -m "wip: %COMPUTERNAME% %STAMP%"
if errorlevel 1 goto :failed

:push
echo.
echo  Sending to GitHub...
echo.
"!GIT!" push
if errorlevel 1 goto :pushfailed

echo.
echo  ================================================================
echo   Saved and sent. Safe to shut down.
echo  ================================================================
echo.
pause
exit /b 0

:pushfailed
echo.
echo  ================================================================
echo   Could not send - the other machine probably pushed first.
echo.
echo   Your work IS saved locally, nothing is lost.
echo   Double-click PULL-LATEST.cmd, then run this again.
echo  ================================================================
echo.
pause
exit /b 1

:failed
echo.
echo  ================================================================
echo   Could not save. Read the message above.
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
