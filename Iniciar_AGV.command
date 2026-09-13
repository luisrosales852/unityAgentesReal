#!/bin/zsh
set -eu
AGV_UNITY_DIR="${0:A:h}"
# Finder puede tener un PATH distinto al de Terminal.
export PATH="/Library/Frameworks/Python.framework/Versions/Current/bin:/opt/homebrew/bin:/usr/local/bin:$PATH"
exec python3 -B "$AGV_UNITY_DIR/PythonBridge/serve_unity.py" "$@"
