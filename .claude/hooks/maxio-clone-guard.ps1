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
# It also guards the MAP boundary: main may not read map pages / sdk-map.md nor load the
# maxio-getting-started skill (the map layer). That rule is why 'Skill' is a guarded tool
# here -- a Skill-tool load is the one way main could pull the map into its own context
# without ever touching a file path. The dotnet-* companion skills are deliberately NOT
# guarded: they are API-agnostic usage guidance that the merged variant now REQUIRES main
# to load, and they contain no map content.
#
# Main's normal work is untouched: app code reads/edits, dotnet build/restore/test/run/
# add-package, live curl to the Maxio API, reading maxio-plan.md, git, src/tests grep --
# none contain the blocklist tokens. Keyword filtering is best-effort; the stage-5
# transcript audit is the backstop. Experiment-only (wired for the maxio-sdk-clone,
# maxio-sdk-merged and maxio-sdk-lean arms via new-run.ps1 -EnforceClone); does NOT ship
# in the plugin.
#
# NOTE for maxio-sdk-lean (v0.4.0+): that variant moved the map OUT of the plugin and into
# the SDK source, so its map pages live inside the clone -- the 'maxio-sdk-src' token
# already covers them, and 'sdk-map.md' plus 'maxio-getting-started' remain as the
# belt-and-braces for a map path or Skill load reached some other way. No new token needed.

$raw = [Console]::In.ReadToEnd()
try { $evt = $raw | ConvertFrom-Json } catch { exit 0 }

$tool = [string]$evt.tool_name
if ($tool -notin @('Bash','Read','Grep','Glob','Skill')) { exit 0 }

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
if ($evt.tool_input.skill)     { $parts += [string]$evt.tool_input.skill }
$text = ($parts -join ' ')
if (-not $text) { exit 0 }

# SDK-internal inspection blocklist. Deliberately narrow: the clone dir + its handshake,
# the SDK's NuGet package cache, decompile/reflection tools, and the bundled MAP layer
# (map/ + sdk-map.md live under skills/maxio-getting-started/, so that one token covers
# them; it also denies a Skill-tool load of maxio-getting-started itself). NOT dotnet
# build/run, NOT live curl, NOT app source (MaxioBillingClient.cs has no blocked token),
# and deliberately NOT the dotnet-* companion skills -- those are API-agnostic usage
# guidance that the main agent is now REQUIRED to load, and they carry no map content.
$blocklist = 'maxio-sdk-src|\.maxio-session|\.nuget[\\/]packages[\\/]asadali|ilspycmd|ildasm|monodis|Assembly\]::LoadFrom|GetTypes\(|maxio-getting-started|sdk-map\.md'

if ($text -match $blocklist) {
    $reason = 'Blocked: the main agent must not read/clone/grep the SDK source or clone, inspect the NuGet package cache, decompile/reflect the SDK DLL, or open the SDK map (map pages / sdk-map.md / the maxio-getting-started skill -- whether the map is bundled in the plugin or shipped inside the SDK source) -- the map belongs to the SDK subagent. To get an SDK contract fact (signature, wire name, enum value, error type) -- INCLUDING during live testing -- send a narrow question to the warm SDK agent (maxio-sdk in the merged variant; maxio-plan in the clone variant). For an SDK compile or runtime error, hand it to that agent (or spawn maxio-debug in the clone variant). The dotnet-* companion skills are NOT blocked -- load those freely for usage guidance. Do not retry this lookup yourself.'
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
