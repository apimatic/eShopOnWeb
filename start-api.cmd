@echo off
set DOTNET_ROLL_FORWARD=Major
set UseOnlyInMemoryDatabase=true
set ASPNETCORE_ENVIRONMENT=Development
set ASPNETCORE_URLS=https://localhost:35923;http://localhost:35924
cd /d C:\claude-runs\t1ochassaan-maxio-docs-mcp-oc-openrouterxiaomimimov25high-002\repo
dotnet run --project src/PublicApi --no-build
