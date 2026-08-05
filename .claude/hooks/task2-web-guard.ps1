# PreToolUse hook: deny WebSearch/WebFetch calls whose query/URL touches the task-2
# provider blocklist (Maxio + PayPal + Twilio). All other lookups (Blazor, EF,
# ASP.NET, ...) pass through untouched.
#
# TASK-2 VARIANT of maxio-web-guard.ps1 (2026-08-05). Task 2 has three providers and its
# prompts forbid web lookups for ALL of them, so the guard must cover paypal/twilio too.
# It is a SEPARATE file (selected per arm via webGuardFile in the arm config) rather than
# an extension of the shared task-1 blocklist, so the guard any future task-1 run receives
# stays byte-identical to the one the 31 recorded runs ran under. Within task 2 the same
# rule as task 1 applies: identical for every guarded arm - tool policy must never differ
# between arms. Keyword filtering is best-effort; the transcript audit is the backstop.

$raw = [Console]::In.ReadToEnd()
try { $evt = $raw | ConvertFrom-Json } catch { exit 0 }

if ($evt.tool_name -ne 'WebSearch' -and $evt.tool_name -ne 'WebFetch') { exit 0 }

$parts = @()
if ($evt.tool_input.query)  { $parts += [string]$evt.tool_input.query }
if ($evt.tool_input.url)    { $parts += [string]$evt.tool_input.url }
if ($evt.tool_input.prompt) { $parts += [string]$evt.tool_input.prompt }
$text = $parts -join ' '

# Task-1 Maxio terms (incl. 'mintlify', the docs-MCP arm's host - see maxio-web-guard.ps1)
# plus the task-2 providers. 'paypal' and 'twilio' also cover their docs/sandbox hosts
# (developer.paypal.com, api-m.sandbox.paypal.com, www.twilio.com, api.twilio.com).
# Deliberately NOT blocked: the live API endpoints reached via Bash/curl - self-verification
# against the sandboxes is part of the task for every arm; this guard only closes the
# WebFetch/WebSearch documentation channel.
$blocklist = 'maxio|chargify|advancedbilling|advanced-billing|advanced_billing|AsadAli|ebilling|mintlify|paypal|twilio'

if ($text -match $blocklist) {
    $decision = @{
        hookSpecificOutput = @{
            hookEventName            = 'PreToolUse'
            permissionDecision       = 'deny'
            permissionDecisionReason = 'Maxio-, PayPal- and Twilio-related web lookups are not permitted in this workspace. Use only the knowledge sources provided inside the workspace.'
        }
    }
    Write-Output ($decision | ConvertTo-Json -Depth 5 -Compress)
}
exit 0
