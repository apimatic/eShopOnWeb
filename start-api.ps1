$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName    = 'dotnet'
$psi.Arguments   = 'run --project src/PublicApi --no-build --urls https://localhost:33323;http://localhost:33324'
$psi.UseShellExecute = $true
$psi.WindowStyle = 'Hidden'
$proc = [System.Diagnostics.Process]::Start($psi)
$proc.Id | Set-Content app.pid
Write-Host "Started PID $($proc.Id)"
