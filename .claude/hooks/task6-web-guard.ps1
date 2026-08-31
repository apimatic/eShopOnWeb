# PreToolUse hook: deny WebSearch/WebFetch calls whose query/URL touches the task-6
# provider blocklist (Visa/CyberSource only). All other lookups (Blazor, EF, ASP.NET, ...)
# pass through untouched.
#
# TASK-6 VARIANT (2026-08-31). Task 6 has ONE provider, so the blocklist is Visa-only: a
# task-6 run has no Maxio, PayPal or Twilio leg, and blocking terms the task never touches
# would only add noise to the transcript audit. It is a SEPARATE file (selected per arm via
# webGuardFile in the arm config) rather than an edit of the task-1/2/3/4/5 guards, so the
# guard those recorded runs received stays byte-identical. Within task 6 the same rule as the
# earlier tasks applies: identical for every guarded arm - tool policy must never differ
# between arms. Keyword filtering is best-effort; the transcript audit is the backstop.
#
# 'cybersource' covers the documentation and API hosts (developer.cybersource.com,
# apitest.cybersource.com, api.cybersource.com, ebc2test.cybersource.com).
#
# 'visa' covers the brand's own developer material (developer.visa.com) and the Visa-branded
# naming this task uses throughout. It is a short and fairly common token, which is accepted
# deliberately: over-blocking a few unrelated lookups is the safe direction for a guard whose
# job is to keep each arm on its own knowledge source, and the transcript audit shows what was
# actually denied.
#
# 'mintlify' is here for the same reason it is in the task-1, task-3 and task-4 guards: a
# docs-MCP arm stood up on a Mintlify-hosted site would otherwise have its pages reachable by
# direct WebFetch, and the MCP server would stop being that arm's sole documentation channel.
# It goes in the SHARED blocklist, not an arm-specific guard, because tool policy must be
# identical for every guarded arm. Task 6's docs-MCP arm is wired but not runnable yet; the
# term is present so the guard does not have to change when it is.
#
# NOT BLOCKED, and this is the one asymmetry worth stating plainly: the official-MCP arm's
# server is a LOCAL PROCESS that fetches its own documentation over the network from inside
# itself. A PreToolUse hook sees the agent's WebFetch/WebSearch calls, not another process's
# HTTP traffic, so this guard neither can nor does constrain what that server retrieves. That
# arm is single-source because its prompt and its tool surface make it so, not because this
# guard enforces it.

$raw = [Console]::In.ReadToEnd()
try { $evt = $raw | ConvertFrom-Json } catch { exit 0 }

if ($evt.tool_name -ne 'WebSearch' -and $evt.tool_name -ne 'WebFetch') { exit 0 }

$parts = @()
if ($evt.tool_input.query)  { $parts += [string]$evt.tool_input.query }
if ($evt.tool_input.url)    { $parts += [string]$evt.tool_input.url }
if ($evt.tool_input.prompt) { $parts += [string]$evt.tool_input.prompt }
$text = $parts -join ' '

# Deliberately NOT blocked: the live API endpoints reached via Bash/curl - self-verification
# against the live sandbox is part of the task for every arm; this guard only closes the
# WebFetch/WebSearch documentation channel.
$blocklist = 'cybersource|visa|mintlify'

if ($text -match $blocklist) {
    $decision = @{
        hookSpecificOutput = @{
            hookEventName            = 'PreToolUse'
            permissionDecision       = 'deny'
            permissionDecisionReason = 'Visa/CyberSource-related web lookups are not permitted in this workspace. Use only the knowledge sources provided inside the workspace.'
        }
    }
    Write-Output ($decision | ConvertTo-Json -Depth 5 -Compress)
}
exit 0
