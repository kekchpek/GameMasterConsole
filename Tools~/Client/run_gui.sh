#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

supports_gui() {
    "$1" -c 'from tkinter import TkVersion; raise SystemExit(TkVersion < 8.6)' >/dev/null 2>&1
}

if [ -n "${GAMEMASTER_PYTHON:-}" ]; then
    if supports_gui "$GAMEMASTER_PYTHON"; then
        exec "$GAMEMASTER_PYTHON" gamemaster_gui.py "$@"
    fi
    printf '%s\n' 'GAMEMASTER_PYTHON must point to a working Python 3 executable with Tkinter and Tk 8.6 or newer.' >&2
    exit 1
fi

python_candidates=(python3)
if [ "$(uname -s)" = "Darwin" ]; then
    # Finder and Unity Hub can omit these directories from PATH.
    python_candidates+=(/opt/homebrew/bin/python3 /usr/local/bin/python3)
fi
for python_executable in "${python_candidates[@]}"; do
    if supports_gui "$python_executable"; then
        exec "$python_executable" gamemaster_gui.py "$@"
    fi
done

printf '%s\n' 'GameMaster Console requires Python 3 with Tkinter and Tk 8.6 or newer. Install it or set GAMEMASTER_PYTHON to its executable.' >&2
exit 1
