# PreToolUse hook: deny WebSearch/WebFetch calls whose query/URL touches the task-5
# provider blocklist (Firecrawl only). All other lookups (ASP.NET, EF, .NET generally, ...)
# pass through untouched.
#
# TASK-5 VARIANT (2026-08-19). Task 5 has ONE provider, so the blocklist is Firecrawl-only:
# a task-5 run has no Maxio/PayPal/Twilio leg, and blocking terms the task never touches would
# only add noise to the transcript audit. It is a SEPARATE file (selected per arm via
# webGuardFile in the arm config), matching the task-3/task-4 convention: identical for every
# guarded arm (plugin, openapi) - tool policy must never differ between arms sharing a guard.
# The 'none' arm does not use this file at all (webGuard=false; its prompt states web/docs are
# available, so a guard would leave it with no source).
#
# 'firecrawl' covers its docs and API hosts (firecrawl.dev, docs.firecrawl.dev,
# api.firecrawl.dev). Keyword filtering is best-effort; the transcript audit is the backstop.

$raw = [Console]::In.ReadToEnd()
try { $evt = $raw | ConvertFrom-Json } catch { exit 0 }

if ($evt.tool_name -ne 'WebSearch' -and $evt.tool_name -ne 'WebFetch') { exit 0 }

$parts = @()
if ($evt.tool_input.query)  { $parts += [string]$evt.tool_input.query }
if ($evt.tool_input.url)    { $parts += [string]$evt.tool_input.url }
if ($evt.tool_input.prompt) { $parts += [string]$evt.tool_input.prompt }
$text = $parts -join ' '

# Deliberately NOT blocked: the live Firecrawl API endpoints reached via Bash/curl (the
# https://task5-supplier-listing.vercel.app/ fixture site, and Firecrawl's own api.firecrawl.dev
# for the real scrape/crawl calls) - self-verification against the live account is part of the
# task for every arm; this guard only closes the WebFetch/WebSearch DOCUMENTATION channel.
$blocklist = 'firecrawl'

if ($text -match $blocklist) {
    $decision = @{
        hookSpecificOutput = @{
            hookEventName            = 'PreToolUse'
            permissionDecision       = 'deny'
            permissionDecisionReason = 'Firecrawl-related web lookups are not permitted in this workspace. Use only the knowledge sources provided inside the workspace.'
        }
    }
    Write-Output ($decision | ConvertTo-Json -Depth 5 -Compress)
}
exit 0
