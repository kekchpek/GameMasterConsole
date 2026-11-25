@echo off
setlocal

cd /d "%~dp0" || exit /b 1

set "PYTHON_ARGS="
if defined GAMEMASTER_PYTHON goto use_override

set "PYTHON_EXE=python"
call :check_python
if not errorlevel 1 goto launch_gui
set "PYTHON_EXE=py"
set "PYTHON_ARGS=-3"
call :check_python
if not errorlevel 1 goto launch_gui
echo GameMaster Console requires Python 3 with Tkinter and Tk 8.6 or newer. Install it or set GAMEMASTER_PYTHON to its executable. 1>&2
exit /b 1

:use_override
set "PYTHON_EXE=%GAMEMASTER_PYTHON%"
call :check_python
if not errorlevel 1 goto launch_gui
echo GAMEMASTER_PYTHON must point to a working Python 3 executable with Tkinter and Tk 8.6 or newer. 1>&2
exit /b 1

:launch_gui
"%PYTHON_EXE%" %PYTHON_ARGS% gamemaster_gui.py %*
exit /b %errorlevel%

:check_python
"%PYTHON_EXE%" %PYTHON_ARGS% -c "from tkinter import TkVersion; raise SystemExit(TkVersion < 8.6)" >nul 2>&1
exit /b %errorlevel%
