@echo off
setlocal

cd /d "%~dp0" || exit /b 1
set "VENV_PYTHON=.venv-gui\Scripts\python.exe"

if not defined GAMEMASTER_PYTHON set "GAMEMASTER_PYTHON=python"
if not exist "%VENV_PYTHON%" (
    "%GAMEMASTER_PYTHON%" -c "import tkinter" >nul 2>&1
    if errorlevel 1 goto missing_tk
    "%GAMEMASTER_PYTHON%" -m venv .venv-gui
    if errorlevel 1 exit /b 1
)

"%VENV_PYTHON%" -m pip --disable-pip-version-check install -r requirements.txt
if errorlevel 1 exit /b 1

"%VENV_PYTHON%" gamemaster_gui.py %*
exit /b %errorlevel%

:missing_tk
echo Python with Tkinter is required. Install Tkinter or set GAMEMASTER_PYTHON to a Python executable with Tkinter. 1>&2
exit /b 1
