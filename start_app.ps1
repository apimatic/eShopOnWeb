
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = "dotnet"
$psi.Arguments = "run --project src/PublicApi --no-build"
$psi.UseShellExecute = $true
$psi.WindowStyle = "Hidden"
$env:DOTNET_ROLL_FORWARD = "Major"
$env:UseOnlyInMemoryDatabase = "true"
$proc = [System.Diagnostics.Process]::Start($psi)
$proc.Id | Set-Content app.pid
Write-Host "Started PID $($proc.Id)"
