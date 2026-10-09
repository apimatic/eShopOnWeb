# PreToolUse hook: deny WebSearch/WebFetch calls whose query/URL touches Wikimedia.
# t567 Wikimedia A/B (2026-10-07): a copy of task3-web-guard.ps1 with a Adyen-only blocklist, identical for
# both arms. Only the WebFetch/WebSearch documentation channel is closed; live API calls via the SDK or curl pass.

$raw = [Console]::In.ReadToEnd()
try { $evt = $raw | ConvertFrom-Json } catch { exit 0 }

if ($evt.tool_name -ne 'WebSearch' -and $evt.tool_name -ne 'WebFetch') { exit 0 }

$parts = @()
if ($evt.tool_input.query)  { $parts += [string]$evt.tool_input.query }
if ($evt.tool_input.url)    { $parts += [string]$evt.tool_input.url }
if ($evt.tool_input.prompt) { $parts += [string]$evt.tool_input.prompt }
$text = $parts -join ' '

# 'paypal' also covers its docs/sandbox hosts (developer.paypal.com,
# api-m.sandbox.paypal.com). Deliberately NOT blocked: the live API endpoints reached via
# Bash/curl - self-verification against the sandbox is part of the task for every arm; this
# guard only closes the WebFetch/WebSearch documentation channel.
$blocklist = 'wikimedia|mediawiki|eventstreams|wikipedia api|server-sent events'

if ($text -match $blocklist) {
    $decision = @{
        hookSpecificOutput = @{
            hookEventName            = 'PreToolUse'
            permissionDecision       = 'deny'
            permissionDecisionReason = 'Wikimedia-related web lookups are not permitted in this workspace. Use only the knowledge sources provided inside the workspace.'
        }
    }
    Write-Output ($decision | ConvertTo-Json -Depth 5 -Compress)
}
exit 0
