# PreToolUse hook: deny calls that reach the app repository's remote, its credentials, or gh.
#
# The reasons, the list of what it denies and how it is audited are kept outside this file, in
# test-suite/scripts/common.ps1 ("No access to the app repository") and knowledge/business-rule-tasks.md,
# because this file is copied into the run's own clone. Keep the comments here neutral.
#
# Keep this file ASCII: hooks run under Windows PowerShell 5.1, which reads a BOM-less .ps1 as cp1252.
# check-app-repo-access.py reads the four $*Pattern lines below, so keep each one a single-quoted string
# on one line.

$raw = [Console]::In.ReadToEnd()
try { $evt = $raw | ConvertFrom-Json } catch { exit 0 }

$tool = [string]$evt.tool_name
if ($tool -ne 'Bash' -and $tool -ne 'PowerShell' -and $tool -ne 'WebFetch' -and $tool -ne 'WebSearch') { exit 0 }

# Owner and name next to each other, joined by anything a URL, an SSH remote or a search query puts
# between them (slash, backslash, colon, space, plus, or their percent-encodings).
$appRepoPattern = '(?i)apimatic(?:\s|/|\\|:|\+|%2f|%5c|%3a|%20)+eshoponweb'
# git, by name or by path, then any global options, then a subcommand that talks to a remote.
$gitNetworkPattern = '(?i)(?:^|[\s;&|(''"=\\/])git(?:\.exe)?[''"]?(?:\s+(?:-[Cc]\s+(?:"[^"]*"|''[^'']*''|\S+)|--(?:git-dir|work-tree|namespace|exec-path|config-env|super-prefix)\s+(?:"[^"]*"|''[^'']*''|\S+)|-{1,2}[A-Za-z][\w-]*(?:=(?:"[^"]*"|''[^'']*''|\S+))?))*\s+(?:fetch|pull|clone|ls-remote|push|fetch-pack|send-pack|remote\s+(?:add|set-url|update)|submodule\s+(?:[^;&|\r\n]*\s)?(?:update|add|sync)|archive\s+(?:[^;&|\r\n]*\s)?--remote|lfs\s+(?:fetch|pull|push)|subtree\s+(?:add|pull|push))(?=$|[\s;&|)''"])'
# gh, by name or by path, as a command word.
$ghPattern = '(?i)(?:^|[\s;&|(''"=\\/])gh(?:\.exe)?[''"]?(?=\s|$|[;&|)])'
# A git credential helper or the Windows credential store.
$credentialPattern = '(?i)(?:\bgit(?:\.exe)?[''"]?\s+(?:-[Cc]\s+\S+\s+)*credential(?:-manager(?:-core)?)?\b|\bgit-credential-[\w-]+|\bcmdkey(?:\.exe)?\b)'

$deny = $false
if ($tool -eq 'Bash' -or $tool -eq 'PowerShell') {
    $cmd = [string]$evt.tool_input.command
    if ($cmd) {
        if ($cmd -match $appRepoPattern -or $cmd -match $gitNetworkPattern -or $cmd -match $ghPattern -or $cmd -match $credentialPattern) { $deny = $true }
    }
}
elseif ($tool -eq 'WebFetch') {
    $url = [string]$evt.tool_input.url
    if ($url -and $url -match $appRepoPattern) { $deny = $true }
}
elseif ($tool -eq 'WebSearch') {
    $query = [string]$evt.tool_input.query
    if ($query -and $query -match $appRepoPattern) { $deny = $true }
}

if ($deny) {
    $decision = @{
        hookSpecificOutput = @{
            hookEventName            = 'PreToolUse'
            permissionDecision       = 'deny'
            permissionDecisionReason = "This run has no access to the app repository's remote."
        }
    }
    Write-Output ($decision | ConvertTo-Json -Depth 5 -Compress)
}
exit 0
