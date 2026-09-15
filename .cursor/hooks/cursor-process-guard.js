#!/usr/bin/env node
// beforeShellExecution hook: deny MACHINE-WIDE process kills, allow PID-scoped ones.
//
// Cursor equivalent of hooks/maxio-process-guard.ps1, ENFORCED rather than instructed (2026-08-20).
// Ported from the MORE refined check-isolation.py detection logic (statement-aware, twice
// bug-fixed: the word-boundary `-id` false-negative and the brace-truncation false-negative that
// let the t1parali kill through undetected), not the older, simpler PowerShell regex, so what
// this BLOCKS and what check-isolation.py DETECTS never drift apart.
//
// WHY THIS EXISTS AT ALL, AND WHY IT WAS THOUGHT IMPOSSIBLE UNTIL NOW: cursor's hooks were
// believed to never fire in headless `-p` mode (knowledge/agent-dimension.md, 2026-08-19). That
// was a false negative: the payload cursor pipes via stdin on Windows carries a DOUBLE UTF-8 BOM
// (`﻿﻿`, not the single one a cursor forum thread describes), so a hook that does
// `JSON.parse(stdin)` as its first action throws before writing any evidence of having run at
// all -- indistinguishable from "never invoked" unless you log BEFORE parsing, which the original
// probe did not. Verified live 2026-08-20: this exact double-BOM strip, then a real `beforeShell
// Execution` deny, blocked a real headless run's shell command
// ("Command execution was blocked by a hook").
//
// WHAT THIS DOES NOT COVER: WebSearch/WebFetch bypass cursor's entire hook system (tested against
// all 13 documented event names -- zero fired) and bypass `permissions.deny` under `--force`
// (tested directly: an explicit `WebFetch(domain)` deny is silently ignored with --force, honoured
// without it -- and without --force ordinary Shell commands ALSO fail closed with no human to
// approve them, which breaks every real build). --force is required for autonomous runs, so the
// web guard has no enforcement lever on this agent. See check-isolation.py's
// `webCallsToBlockedTerms` for the detect-not-prevent substitute.

const fs = require("fs");

function readStdinText() {
    const raw = fs.readFileSync(0); // raw Buffer -- do NOT decode-then-strip; see below
    return raw.toString("utf8").replace(/^﻿+/, ""); // strip ALL leading BOMs, not just one
}

// ---- Ported from check-isolation.py: KILL_VERB_RE, BY_PATTERN_RE, BY_IDENTITY_RE, ENUM_RE ----
const KILL_VERB_RE = /stop-process|kill-process|taskkill|pkill|killall|\.terminate\s*\(|invoke-cimmethod|xargs\s+kill|\bkill\s+-|\bkill\s+\d|\bkill\s+\$/i;
const BY_PATTERN_RE = /-name\b|\/im\b|-imagename\b|-processname\b|commandline\s*-(match|like|eq)|\$_\.name\s*-(match|like|eq)|\bpkill\b|\bkillall\b/i;
const BY_IDENTITY_RE = /-id\b|\/pid\b|processid\s*(=|-eq)|parentprocessid\s*-eq|owningprocess/i;
const ENUM_RE = /get-ciminstance|get-wmiobject|\bgcim\b|\bgwmi\b|get-process|\bgps\b|\bps\s+-?[aeu]|tasklist/i;

// Split into statements at brace-depth 0 only (a kill's victims are often chosen in an earlier
// statement of the SAME command, e.g. `Where-Object { ... } | Stop-Process`, and splitting inside
// the braces would separate the kill from the filter that picked its victims). Unlike
// check-isolation.py's statements(), the hook's `command` field arrives as the raw command text
// directly (not JSON-wrapped), so there is no outer-brace level to account for.
function statements(cmd) {
    const text = String(cmd || "");
    const out = [];
    let buf = [], depth = 0;
    for (const ch of text) {
        if (ch === "{" || ch === "(") depth++;
        else if (ch === "}" || ch === ")") depth = Math.max(0, depth - 1);
        if (depth === 0 && (ch === ";" || ch === "\n")) {
            out.push(buf.join(""));
            buf = [];
        } else {
            buf.push(ch);
        }
    }
    out.push(buf.join(""));
    return out.filter(s => s.trim());
}

function machineWideKillStatement(cmd) {
    for (const st of statements(cmd)) {
        if (!KILL_VERB_RE.test(st)) continue;
        if (BY_PATTERN_RE.test(st)) return st;                              // by image/process name or command-line match
        if (ENUM_RE.test(st) && !BY_IDENTITY_RE.test(st)) return st;        // every process on the machine, nothing narrowing it
    }
    return null;
}

const GUIDANCE = `BLOCKED: the processes to kill are selected by IMAGE NAME, COMMAND-LINE PATTERN, or an
unnarrowed machine-wide enumeration, which matches every concurrent run on this machine --
resolving a PID from that set does not narrow it.

Several runs are building on this machine at the same time, each in its own workspace and on
its own port block. An image name identifies no run, and the app host's command line holds a
relative path, so it cannot be filtered by run id either.

Kill by PID, resolved from the port block YOU own (APP_PORT_BLOCK_BASE ..
APP_PORT_BLOCK_BASE + APP_PORT_BLOCK_SIZE - 1):

  Get-NetTCPConnection -LocalPort <your-port> -State Listen |
    Select-Object -ExpandProperty OwningProcess -Unique |
    ForEach-Object { Stop-Process -Id $_ -Force }

Better still, keep the PID when you start the app and stop that PID. Never kill by image name
or by a machine-wide process enumeration here.`;

let payload;
try {
    payload = JSON.parse(readStdinText());
} catch (e) {
    // Fail OPEN on a parse error, matching cursor's own documented fail-open-on-hook-failure
    // behaviour (exit != 0/2 => action proceeds) -- a hook that cannot even read its own input
    // must not be the thing that hangs a build. The kill-attempt detection in
    // check-isolation.py is the backstop if this ever happens.
    process.exit(1);
}

const cmd = payload && payload.command;
const victimStatement = cmd ? machineWideKillStatement(cmd) : null;

if (victimStatement) {
    console.log(JSON.stringify({
        continue: true,
        permission: "deny",
        user_message: "Blocked: machine-wide process kill (see agent message for the safe form).",
        agent_message: GUIDANCE
    }));
} else {
    console.log(JSON.stringify({ continue: true, permission: "allow" }));
}
process.exit(0);
