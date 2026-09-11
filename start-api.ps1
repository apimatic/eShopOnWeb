#!/usr/bin/env pwsh
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:UseOnlyInMemoryDatabase = "true"
$env:DOTNET_ROLL_FORWARD = "Major"
$env:MAXIO_API_KEY = [System.Environment]::GetEnvironmentVariable("MAXIO_API_KEY")
$env:MAXIO_SITE_SUBDOMAIN = [System.Environment]::GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN")
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = [System.Environment]::GetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY")
Set-Location "D:\claude-runs\t1ocnameer-maxio-sdk-oc-openrouterxiaomimimov25high-002\repo"
dotnet run --project src/PublicApi --no-build --urls "https://localhost:35403;http://localhost:35404"
