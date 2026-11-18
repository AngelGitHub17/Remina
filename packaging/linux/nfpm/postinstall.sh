#!/bin/sh
set -e
chmod +x /opt/remina/agent/Remina.Agent 2>/dev/null || true
if command -v systemctl >/dev/null 2>&1; then
  systemctl daemon-reload || true
  systemctl enable remina-agent >/dev/null 2>&1 || true
fi
echo ""
echo "  Agent Remina installé."
echo "  1) Édite /etc/remina/agent.env (URL du serveur + code d'inscription)"
echo "  2) sudo systemctl start remina-agent"
echo ""
