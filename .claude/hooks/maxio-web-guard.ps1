# PreToolUse hook: deny WebSearch/WebFetch calls whose query/URL touches the Maxio
# blocklist. All other lookups (Blazor, EF, ASP.NET, ...) pass through untouched.
# Identical for every arm - tool policy must never differ between arms.
# Keyword filtering is best-effort; the stage-5 transcript audit is the backstop.

$raw = [Console]::In.ReadToEnd()
try { $evt = $raw | ConvertFrom-Json } catch { exit 0 }

if ($evt.tool_name -ne 'WebSearch' -and $evt.tool_name -ne 'WebFetch') { exit 0 }

$parts = @()
if ($evt.tool_input.query)  { $parts += [string]$evt.tool_input.query }
if ($evt.tool_input.url)    { $parts += [string]$evt.tool_input.url }
if ($evt.tool_input.prompt) { $parts += [string]$evt.tool_input.prompt }
$text = $parts -join ' '

$blocklist = 'maxio|chargify|advancedbilling|advanced-billing|advanced_billing|AsadAli|ebilling'

if ($text -match $blocklist) {
    $decision = @{
        hookSpecificOutput = @{
            hookEventName            = 'PreToolUse'
            permissionDecision       = 'deny'
            permissionDecisionReason = 'Maxio-related web lookups are not permitted in this workspace. Use only the knowledge sources provided inside the workspace.'
        }
    }
    Write-Output ($decision | ConvertTo-Json -Depth 5 -Compress)
}
exit 0
