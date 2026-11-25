#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

VENV_PYTHON=".venv-gui/bin/python"
if [ ! -x "$VENV_PYTHON" ]; then
    BASE_PYTHON="${GAMEMASTER_PYTHON:-python3}"
    if [ -z "${GAMEMASTER_PYTHON:-}" ] && [ "$(uname -s)" = "Darwin" ] &&
        ! "$BASE_PYTHON" -c 'import tkinter' >/dev/null 2>&1 &&
        [ -x /usr/local/bin/python3 ]; then
        BASE_PYTHON="/usr/local/bin/python3"
    fi
    if ! "$BASE_PYTHON" -c 'import tkinter' >/dev/null 2>&1; then
        printf '%s\n' 'Python with Tkinter is required. Install Tkinter or set GAMEMASTER_PYTHON to a Python executable with Tkinter.' >&2
        exit 1
    fi
    "$BASE_PYTHON" -m venv .venv-gui
fi

"$VENV_PYTHON" -m pip --disable-pip-version-check install -r requirements.txt
exec "$VENV_PYTHON" gamemaster_gui.py "$@"
