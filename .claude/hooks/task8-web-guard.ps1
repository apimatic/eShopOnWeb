# PreToolUse hook: deny WebSearch/WebFetch calls whose query/URL touches the task-8
# provider blocklist (Walmart Seller API v4). All other lookups (Blazor, EF, ASP.NET, ...) pass
# through untouched.
#
# TASK-8 VARIANT (2026-10-13). A SEPARATE file, selected per arm via webGuardFile, so no earlier
# task's guard changes. Identical for every task-8 arm - tool policy must never differ between
# arms. Keyword filtering is best-effort; the transcript audit is the backstop.
#
# Why each term:
#   walmart          - Walmart's public developer docs describe the v3 API, a DIFFERENT contract
#                      from the v4 one every arm builds against. Both arm prompts forbid the web
#                      for Walmart details; this enforces it for WebFetch/WebSearch.
#   apimatic-wm      - the public APIMatic POC portal for this v4 API
#   wm-portal        - (apimatic-wm-poc-v4.netlify.app, wm-portal-v2.pages.dev). Reading either
#                      would hand the spec arm the plugin arm's generated docs.
#   wmv4 / wm-v4     - the generated SDK's NuGet package (Apimaticwmv4SDK) and GitHub repo
#                      (sdks-io/apimatic-wm-v4-dotnet-sdk). The plugin arm reaches that repo with
#                      Bash `git clone`, which this guard does not see, exactly as its skill
#                      intends; the spec arm is forbidden a pre-built SDK by its prompt.
#   onrender         - the stateless Prism mock of this API (walmart-apis-mock-server.onrender.com).
#                      The task prompt says no live Walmart exists; the mock must not become one
#                      arm's private test target.
#
# Deliberately NOT blocked: Bash/curl. This guard only closes the WebFetch/WebSearch channel.

$raw = [Console]::In.ReadToEnd()
try { $evt = $raw | ConvertFrom-Json } catch { exit 0 }

if ($evt.tool_name -ne 'WebSearch' -and $evt.tool_name -ne 'WebFetch') { exit 0 }

$parts = @()
if ($evt.tool_input.query)  { $parts += [string]$evt.tool_input.query }
if ($evt.tool_input.url)    { $parts += [string]$evt.tool_input.url }
if ($evt.tool_input.prompt) { $parts += [string]$evt.tool_input.prompt }
$text = $parts -join ' '

$blocklist = 'walmart|apimatic-wm|wm-portal|wmv4|wm-v4|onrender'

if ($text -match $blocklist) {
    $decision = @{
        hookSpecificOutput = @{
            hookEventName            = 'PreToolUse'
            permissionDecision       = 'deny'
            permissionDecisionReason = 'Walmart-related web lookups are not permitted in this workspace. Use only the knowledge sources provided inside the workspace.'
        }
    }
    Write-Output ($decision | ConvertTo-Json -Depth 5 -Compress)
}
exit 0
