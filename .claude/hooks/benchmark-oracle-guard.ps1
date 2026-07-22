# PreToolUse hook: keep the readiness gate HONEST during a benchmark run.
#
# In a benchmark run the building agent iterates against the PUBLIC readiness gate
# (benchmark-loop.ps1). The gate is only an honest signal if the agent optimizes against
# it WITHOUT being able to read or shape the parts that keep it honest:
#   1. the profile trio (profile.json / contract.json / optable.json) + the generated
#      _wired copy -- that trio IS the test spec (oracle bodies, field names, mustContain
#      values, drift/fault plan). An agent that reads it can tune to the fixtures.
#   2. the HOLDOUT (--mode holdout) + the recorder/judge surfaces (run.ps1/report.ps1,
#      Harness.Quality) -- the holdout is the held-out acceptance test; the moment the
#      agent can see holdout failures it is no longer held out.
#
# This backs those invariants with a hard PreToolUse deny (the deny reason is shown to the
# model and redirects it back to the loop). It blocks ALL agents (main + any subagent), so
# "delegate the read to a generic subagent" is closed too. It does NOT touch the agent's
# real work: app code reads/edits, dotnet build/restore/test/run, live curl, reading
# maxio-spec/ (the provider spec it SHOULD use), git, src/tests grep -- none carry the
# blocklist tokens -- and it ALLOWS the loop itself (powershell -File benchmark-loop.ps1,
# and any explicit --mode public). Keyword filtering is best-effort; the transcript audit
# is the backstop. Harness-side only (wired via new-run.ps1 -Benchmark); ships NO plugin content.

$raw = [Console]::In.ReadToEnd()
try { $evt = $raw | ConvertFrom-Json } catch { exit 0 }

$tool = [string]$evt.tool_name
if ($tool -notin @('Bash','Read','Grep','Glob')) { exit 0 }

$parts = @()
if ($evt.tool_input.command)   { $parts += [string]$evt.tool_input.command }
if ($evt.tool_input.file_path) { $parts += [string]$evt.tool_input.file_path }
if ($evt.tool_input.pattern)   { $parts += [string]$evt.tool_input.pattern }
if ($evt.tool_input.path)      { $parts += [string]$evt.tool_input.path }
$text = ($parts -join ' ')
if (-not $text) { exit 0 }

# ALLOW the loop wrapper explicitly first: its own command string mentions benchmark-loop.ps1
# (and never a blocked token), so this is belt-and-suspenders in case a path also matched.
if ($text -match 'benchmark-loop\.ps1') { exit 0 }

# Oracle blocklist. The profile trio + wired copy by path; the holdout + recorder/judge
# surfaces by command. 'profiles[\\/]maxio' is distinct from the agent's own 'maxio-spec/'.
$fixtures = 'profiles[\\/]maxio|[\\/]_wired[\\/]|[\\/]turnkey[\\/]profiles|\bcontract\.json\b|\boptable\.json\b|\bprofile\.json\b'
$surfaces = '--mode\s+holdout|\bagent-loop\.(ps1|sh)\b|\brun\.(ps1|sh)\b|\breport\.ps1\b|Harness\.Quality'

if ($text -match $fixtures -or $text -match $surfaces) {
    $reason = 'Blocked: this is a benchmark run. Do NOT read the readiness harness fixtures (profile.json / contract.json / optable.json / the _wired profile) and do NOT run the holdout or any recorder/judge surface (--mode holdout, run.ps1, report.ps1, Harness.Quality). Those are the held-out spec that keeps the gate honest. Use ONLY the loop: run `powershell -NoProfile -ExecutionPolicy Bypass -File .\benchmark\benchmark-loop.ps1 -App .\src\PublicApi\PublicApi.csproj`, read the [FAIL] lines and .\benchmark\status.json, fix your integration, and re-run.'
    $decision = @{
        hookSpecificOutput = @{
            hookEventName            = 'PreToolUse'
            permissionDecision       = 'deny'
            permissionDecisionReason = $reason
        }
    }
    Write-Output ($decision | ConvertTo-Json -Depth 5 -Compress)
}
exit 0
