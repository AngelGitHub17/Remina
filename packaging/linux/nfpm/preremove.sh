#!/bin/sh
set -e
if command -v systemctl >/dev/null 2>&1; then
  systemctl stop remina-agent >/dev/null 2>&1 || true
  systemctl disable remina-agent >/dev/null 2>&1 || true
fi
