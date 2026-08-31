# Re-check reconciliation after PayPal's reporting lag: our run's transactions should
# eventually appear and match eShop orders via the stored invoice/authorization/capture ids.
param([string]$BaseUrl = "http://localhost:19664")
$ErrorActionPreference = 'Stop'

function Invoke-Api {
    param([string]$Method, [string]$Path, [object]$Body = $null, [string]$Token = $null)
    $args = @('-s', '-o', '-', '-w', "`n%{http_code}", '-X', $Method, "$BaseUrl$Path")
    $tmp = $null
    if ($null -ne $Body) {
        $tmp = [System.IO.Path]::GetTempFileName()
        [System.IO.File]::WriteAllText($tmp, ($Body | ConvertTo-Json -Depth 10 -Compress))
        $args += @('-H', 'Content-Type: application/json', '-d', "@$tmp")
    }
    if ($Token) { $args += @('-H', "Authorization: Bearer $Token") }
    try { $raw = & curl.exe @args } finally { if ($tmp) { Remove-Item $tmp -Force -ErrorAction SilentlyContinue } }
    $lines = $raw -split "`n"
    return @{ Status = [int]$lines[-1].Trim(); Body = ($lines[0..($lines.Count - 2)] -join "`n").Trim() }
}

$admin = ((Invoke-Api -Method POST -Path '/api/authenticate' -Body @{ username = 'admin@microsoft.com'; password = 'Pass@word1' }).Body | ConvertFrom-Json).token

for ($i = 1; $i -le 14; $i++) {
    Start-Sleep -Seconds 900   # 15 min between checks
    $from = (Get-Date).ToUniversalTime().AddHours(-6).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
    $to = (Get-Date).ToUniversalTime().AddHours(1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
    $r = Invoke-Api -Method GET -Path "/api/reconciliation?from=$from&to=$to" -Token $admin
    if ($r.Status -ne 200) { Write-Host "check ${i}: HTTP $($r.Status)"; continue }
    $report = $r.Body | ConvertFrom-Json
    $matched = @($report.transactions | Where-Object { $_.matchedOrderId })
    $missing = @($report.ordersMissingFromProviderReport)
    Write-Host "check ${i}: txns=$($report.transactions.Count) matched=$($matched.Count) missingFromProvider=$($missing.Count)"
    if ($matched.Count -gt 0) {
        $matched | Select-Object transactionId, eventCode, amount, invoiceId, matchedOrderId | Format-Table -AutoSize | Out-String | Write-Host
        Write-Host 'RECONCILIATION MATCHING VERIFIED' -ForegroundColor Green
        exit 0
    }
}
Write-Host 'RECONCILIATION MATCHING NOT OBSERVED (provider report still lagging)' -ForegroundColor Yellow
exit 2
