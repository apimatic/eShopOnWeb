$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = 'dotnet'
$psi.Arguments = 'run --project src/PublicApi --urls "https://localhost:32043;http://localhost:32044"'
$psi.UseShellExecute = $true
$psi.WindowStyle = 'Hidden'
$proc = [System.Diagnostics.Process]::Start($psi)
$proc.Id | Set-Content app.pid
Write-Output "Started PID $($proc.Id)"
