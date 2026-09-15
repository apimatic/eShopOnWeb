#!/usr/bin/env bash
# Rebuild PublicApi and restart it on the assigned port block, using a private log.
set -e
cd /c/claude-runs/t3v7ali-task3-plugin-opus48high-023/repo
export DOTNET_ROLL_FORWARD=Major
PID=$(netstat -ano 2>/dev/null | grep -E "127.0.0.1:13523 " | grep LISTEN | awk '{print $5}' | head -1)
[ -n "$PID" ] && taskkill //PID "$PID" //T //F >/dev/null 2>&1 || true
sleep 2
dotnet build src/PublicApi/PublicApi.csproj -c Debug 2>&1 | grep -E ": error|Build FAILED" && { echo "BUILD FAILED"; exit 1; } || true
export UseOnlyInMemoryDatabase=true ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="https://localhost:13523;http://localhost:13524"
nohup dotnet run --project src/PublicApi/PublicApi.csproj -c Debug --no-build --no-launch-profile > tmp/run-publicapi.log 2>&1 &
for i in $(seq 1 30); do grep -q "Application started" tmp/run-publicapi.log 2>/dev/null && break; sleep 1; done
grep -E "Now listening on: https|Application started" tmp/run-publicapi.log | head
