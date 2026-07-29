# PreToolUse hook: deny MACHINE-WIDE process kills, allow PID-scoped ones.
#
# WHY (2026-07-29 incident): runs execute in parallel, one per Maxio site, all on this one
# machine. The task prompt tells the agent to stop its previous app instance before starting
# another -- and an agent in run -006 implemented that as:
#
#     Get-Process -Name dotnet,PublicApi | Stop-Process -Force
#
# which is not scoped to its own workspace. Within 25 seconds it killed the app hosts and
# build/test processes of the three SIBLING runs building alongside it and froze a concurrent
# grading task mid-gate. Three runs (~$30 and ~75 minutes of agent work) were lost. The same
# hazard had already been recorded once before in this project as
# `taskkill /F /IM PublicApi.exe` killing a sibling run's server.
#
# An image name (dotnet.exe, PublicApi.exe, node.exe) carries NO run identity, and neither
# does the app host's command line -- agents launch it as a RELATIVE path
# (`dotnet bin/Debug/net8.0/PublicApi.dll`), so even a command-line exclusion of the run id
# cannot work. The only safe scope is a PID, and the reliable way to get one is from the
# port block this run owns (APP_PORT_BLOCK_BASE .. +APP_PORT_BLOCK_SIZE-1).
#
# Identical for every arm -- tool policy must never differ between arms.
# Keyword filtering is best-effort; the transcript audit remains the backstop.

$raw = [Console]::In.ReadToEnd()
try { $evt = $raw | ConvertFrom-Json } catch { exit 0 }

# Only shell-executing tools can kill a process.
if ($evt.tool_name -ne 'Bash' -and $evt.tool_name -ne 'PowerShell') { exit 0 }

$cmd = [string]$evt.tool_input.command
if (-not $cmd) { exit 0 }

$reason = $null

# 1) taskkill by IMAGE NAME (/IM, -IM). /PID is fine.
if ($cmd -match '(?i)\btaskkill\b' -and $cmd -match '(?i)[/-]IM\b') {
    $reason = 'taskkill by image name (/IM) kills that image in EVERY concurrent run, not just yours.'
}
# 2) Stop-Process by NAME (-Name / -ProcessName) in the SAME statement. -Id is fine.
#    Scoped to one statement so a block that merely INSPECTS with `Get-Process -Name x`
#    and then kills a resolved PID is not caught.
elseif ($cmd -match '(?i)\bStop-Process\b[^;|\r\n]*-(Name|ProcessName)\b') {
    $reason = 'Stop-Process -Name kills that image in EVERY concurrent run, not just yours.'
}
# 3) Get-Process ... | Stop-Process  (the piped form needs no -Name to be machine-wide).
#    Deny unless the pipeline is narrowed by a PID (-Id / ProcessId / OwningProcess).
elseif ($cmd -match '(?i)Get-Process[^|]*\|[^|]*Stop-Process' -and $cmd -notmatch '(?i)(-Id\b|ProcessId|OwningProcess)') {
    $reason = 'Get-Process | Stop-Process without a PID filter kills matching processes in EVERY concurrent run.'
}
# 4) Get-CimInstance Win32_Process ... | ... Stop-Process with no PID narrowing.
elseif ($cmd -match '(?i)Win32_Process' -and $cmd -match '(?i)Stop-Process' -and $cmd -notmatch '(?i)(ProcessId|-Id\b)') {
    $reason = 'A Win32_Process sweep into Stop-Process is not scoped to your run.'
}
# 5) POSIX name-based killers.
elseif ($cmd -match '(?i)\b(pkill|killall)\b') {
    $reason = 'pkill/killall match by NAME and will hit sibling runs.'
}

if ($reason) {
    $guidance = @"
BLOCKED: $reason

Several runs are building on this machine at the same time, each in its own workspace and on
its own port block. An image name identifies no run, and the app host's command line holds a
relative path, so it cannot be filtered by run id either.

Kill by PID, resolved from the port block YOU own (`APP_PORT_BLOCK_BASE` ..
`APP_PORT_BLOCK_BASE + APP_PORT_BLOCK_SIZE - 1`):

  # PowerShell -- stop only what is listening on YOUR port
  Get-NetTCPConnection -LocalPort <your-port> -State Listen |
    Select-Object -ExpandProperty OwningProcess -Unique |
    ForEach-Object { Stop-Process -Id `$_ -Force }

Better still, keep the PID when you start the app (`Start-Process -PassThru`, or `$!` in bash)
and stop that PID. Never kill by image name here.
"@
    $decision = @{
        hookSpecificOutput = @{
            hookEventName            = 'PreToolUse'
            permissionDecision       = 'deny'
            permissionDecisionReason = $guidance
        }
    }
    Write-Output ($decision | ConvertTo-Json -Depth 5 -Compress)
}
exit 0
