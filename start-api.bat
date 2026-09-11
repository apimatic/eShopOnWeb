@echo off
set ASPNETCORE_ENVIRONMENT=Development
set MAXIO_API_KEY=%MAXIO_API_KEY%
set MAXIO_SITE_SUBDOMAIN=%MAXIO_SITE_SUBDOMAIN%
set MAXIO_DEFAULT_PRODUCT_FAMILY=%MAXIO_DEFAULT_PRODUCT_FAMILY%
set UseOnlyInMemoryDatabase=true
cd /d D:\claude-runs\t1ocnameer-maxio-sdk-oc-openrouterxiaomimimov25high-002\repo
dotnet run --project src/PublicApi --no-build --urls "https://localhost:35403;http://localhost:35404"
