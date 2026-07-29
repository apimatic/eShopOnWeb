# PreToolUse hook: enforce reuse of the single warm maxio-sdk agent (maxio-sdk-merged variant).
#
# The merged variant's efficiency thesis is "one warm SDK agent, reused." But sel-005 showed a
# weak model cold-re-spawns the SDK helper repeatedly despite the router's "reuse, don't re-spawn"
# rule (7 cold maxio-plan spawns = that run's churn / 11 sessions). This hook backs the rule: the
# FIRST spawn of the maxio-sdk agent is allowed; a SECOND cold spawn is DENIED with a redirect to
# resume the warm one instead. SAFETY VALVE: after 2 denials it YIELDS (allows the spawn), so a
# genuinely dead / un-resumable agent can never deadlock the run -- a firm nudge, not a wall
# (best-effort, like the clone-guard; the transcript audit is the backstop).
#
# Resuming the agent via a FOLLOW-UP message (SendMessage / resume) is NOT a spawn (different tool),
# so it always passes -- that is the path this hook steers toward. Experiment-only (wired via
# new-run.ps1 -ReuseGuard for the maxio-sdk-merged arm); does NOT ship in the plugin.

$raw = [Console]::In.ReadToEnd()
try { $evt = $raw | ConvertFrom-Json } catch { exit 0 }

# Only the subagent-spawn tool.
$tool = [string]$evt.tool_name
if ($tool -notin @('Task','Agent')) { exit 0 }

# Only spawns that TARGET the merged SDK agent. End-anchored so the plugin PREFIX 'maxio-sdk-*'
# is not a match, and so maxio-plan/maxio-debug (other variants' agents) are not affected.
$sub = [string]$evt.tool_input.subagent_type
if ($sub -notmatch '(^|:)maxio-sdk$') { exit 0 }

# Per-session marker so sequential runs sharing a TEMP never contaminate each other. Main is
# sequential within a run, so no race. (run-phase also sets a per-run TEMP for extra isolation.)
$sid = [string]$evt.session_id; if (-not $sid) { $sid = 'nosid' }
$sid = ($sid -replace '[^A-Za-z0-9._-]', '_')
$marker = Join-Path $env:TEMP (".maxio-sdk-reuse.$sid.marker")

if (-not (Test-Path $marker)) { Set-Content -Path $marker -Value '0' -Encoding ascii; exit 0 }  # first spawn: allow

$denies = 0; try { $denies = [int]((Get-Content $marker -Raw).Trim()) } catch { $denies = 0 }
if ($denies -ge 2) { exit 0 }   # safety valve: already nudged twice -- yield, allow the re-spawn

Set-Content -Path $marker -Value ([string]($denies + 1)) -Encoding ascii
$reason = 'Blocked: you already spawned the maxio-sdk agent this session -- it is warm. Send it a FOLLOW-UP message (resume it) instead of spawning a new one: route this need -- a plan revision, a contract fact, or an SDK error to fix -- to the EXISTING agent. A fresh spawn rebuilds the entire SDK map context from scratch, which is the dominant cost. (If the agent has genuinely ended and cannot be resumed, retry -- this block yields after a couple of attempts.)'
$decision = @{
    hookSpecificOutput = @{
        hookEventName            = 'PreToolUse'
        permissionDecision       = 'deny'
        permissionDecisionReason = $reason
    }
}
Write-Output ($decision | ConvertTo-Json -Depth 5 -Compress)
exit 0
