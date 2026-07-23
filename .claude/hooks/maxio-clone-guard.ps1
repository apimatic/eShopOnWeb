# PreToolUse hook: enforce the maxio-sdk-clone variant's isolation boundary.
#
# The variant's design REQUIRES that the MAIN agent never reads/greps/finds the SDK
# clone, never inspects the NuGet package cache, and never decompiles/reflects the
# installed SDK DLL -- all SDK-contract work belongs to the maxio-plan / maxio-debug
# subagents (which legitimately read a cloned SDK). Instruction-only enforcement of
# this failed at N=2 (sel-003: main find-ed the hidden clone via its handshake and
# read SDK source 34x + tried DLL reflection). This hook backs the rule with a hard
# block AND redirects main to the right helper (the deny reason is shown to the model).
#
# Allowed for agent_type maxio-plan / maxio-debug ONLY (the SDK owners; agent_type is
# harness-set, not forgeable by the model). Everyone else -- main (no agent_type) AND
# any other subagent (Explore / general-purpose) -- is blocked from SDK internals, which
# also closes the "delegate the spelunk to a generic subagent" bypass.
#
# Main's normal work is untouched: app code reads/edits, dotnet build/restore/test/run/
# add-package, live curl to the Maxio API, reading maxio-plan.md, git, src/tests grep --
# none contain the blocklist tokens. Keyword filtering is best-effort; the stage-5
# transcript audit is the backstop. Experiment-only (wired for the maxio-sdk-clone arm
# via new-run.ps1 -EnforceClone); does NOT ship in the plugin.

$raw = [Console]::In.ReadToEnd()
try { $evt = $raw | ConvertFrom-Json } catch { exit 0 }

$tool = [string]$evt.tool_name
if ($tool -notin @('Bash','Read','Grep','Glob')) { exit 0 }

# Allow ONLY the SDK-owning subagents: the map-in-clone variant's two
# ('maxio-sdk-clone:maxio-plan'/'maxio-debug', or bare) AND the merged variant's
# single 'maxio-sdk-merged:maxio-sdk' (matched end-anchored so the plugin PREFIX
# 'maxio-sdk-*' never counts as a match). agent_type is harness-set, not forgeable.
$agent = [string]$evt.agent_type
if ($agent -match 'maxio-(plan|debug)' -or $agent -match '(^|:)maxio-sdk$') { exit 0 }

# Everyone else (main = empty agent_type, or any other subagent): inspect the target.
$parts = @()
if ($evt.tool_input.command)   { $parts += [string]$evt.tool_input.command }
if ($evt.tool_input.file_path) { $parts += [string]$evt.tool_input.file_path }
if ($evt.tool_input.pattern)   { $parts += [string]$evt.tool_input.pattern }
if ($evt.tool_input.path)      { $parts += [string]$evt.tool_input.path }
$text = ($parts -join ' ')
if (-not $text) { exit 0 }

# SDK-internal inspection blocklist. Deliberately narrow: the clone dir + its handshake,
# the SDK's NuGet package cache, and decompile/reflection tools. NOT dotnet build/run,
# NOT live curl, NOT app source (e.g. MaxioBillingClient.cs has no 'maxio-sdk-src' token).
$blocklist = 'maxio-sdk-src|\.maxio-session|\.nuget[\\/]packages[\\/]asadali|ilspycmd|ildasm|monodis|Assembly\]::LoadFrom|GetTypes\('

if ($text -match $blocklist) {
    $reason = 'Blocked: the main agent must not read/clone/grep the SDK source or clone, inspect the NuGet package cache, or decompile/reflect the SDK DLL. To get an SDK contract fact (signature, wire name, enum value, error type) -- INCLUDING during live testing -- send a narrow question to the warm maxio-plan agent. For an SDK compile or runtime error, spawn maxio-debug. Do not retry this lookup yourself.'
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
