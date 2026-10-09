# Task — A sign-in tool for the shop's Square account

The eShopOnWeb shop also sells in person with **Square**. Before the shop's Square features are
built, the owner wants a small **command-line tool** that the shop's operator runs on their own
computer: it connects to the merchant's Square account the way any Square app does, with the
merchant signing in through their browser and approving the shop's access, and then shows what
it is connected to. It is an **additive** capability — it does not change the existing apps.

You own the design and every implementation decision — architecture, file layout, build
order, patterns. Just honor the mandates and the details below.

---

## What to build

A console project, **`src/SquareCheck`**, added to the solution. Running it
(`dotnet run --project src/SquareCheck`) does this:

1. **Sign in.** It opens the operator's browser at Square's sign-in page. The operator signs in
   and approves the shop's access, and Square sends the browser back to the registered redirect
   address (`SQUARE_REDIRECT_URI`, below), where the tool picks up the result.
   - Operators can be slow (two-factor codes, a forgotten password): the tool waits up to
     **five minutes** for the sign-in, and opens the sign-in page **only once** per run.
   - When the operator has approved, the page their browser lands on names the **Square business**
     the tool is now connected to, and tells them they can close the tab.
   - A visit to the redirect address that does not belong to this run's sign-in (a stale tab from
     an earlier run, a link someone else sent) must not complete the sign-in.
2. **Show the account.** It prints the merchant's business name and id, then each of the
   merchant's locations with its name, status and address, and exits with code `0`.

When things go wrong, the operator gets one plain line, never a stack trace:
- the operator declines access, or does not finish signing in within five minutes → say which,
  exit code `2`;
- Square refuses or fails a request → say so, exit code `1`;
- the operator presses Ctrl+C at any point → stop at once, exit code `130`.

The tool keeps nothing between runs: every run signs in again.

---

## Square tooling — non-negotiable

- Use the **square** plugin (from the **context-plugins** marketplace) for
  **every** Square interaction. It is your sole reference for how to talk to Square.
- **Do not** web-search or rely on general/external knowledge for Square API details.
- If the plugin does not expose a capability you need, **STOP and report the gap** — do not
  invent or work around it.

---

## Sandbox

The merchant is a Square **sandbox** test seller with at least one location.

A fact from Square's account team that the plugin does not spell out (take it as given):
- **The permission the sign-in asks for:** `MERCHANT_PROFILE_READ`.

---

## Credentials

- Sandbox credentials arrive as env vars: `SQUARE_ENVIRONMENT`, `SQUARE_APPLICATION_ID`,
  `SQUARE_APPLICATION_SECRET`, `SQUARE_REDIRECT_URI` (the redirect address registered with
  Square for this app — an address on the operator's own machine) and `SQUARE_ACCESS_TOKEN`.
- Target the Square **sandbox** for all development and testing.
- **Bind settings from the `Square:` configuration section using exactly these keys**, and
  hard-code none of their values — the same tool has to run against a different Square app:
  `Square:Environment` (from `SQUARE_ENVIRONMENT`), `Square:ApplicationId` (from
  `SQUARE_APPLICATION_ID`), `Square:ApplicationSecret` (from `SQUARE_APPLICATION_SECRET`) and
  `Square:RedirectUri` (from `SQUARE_REDIRECT_URI`).
- `SQUARE_ACCESS_TOKEN` is a sandbox seller's ready-made access token. It is **only** for your own
  live checks of the Square calls (from a test or a scratch program); the tool itself never uses
  it — it always signs in.

---

## Environment gotchas (this machine)

- **SDK/runtime mismatch:** `global.json` pins the SDK to 8.0.x, but only the .NET 10 SDK is
  installed. Let it roll forward (`rollForward: latestMajor`) and run with
  `DOTNET_ROLL_FORWARD=Major`.
- **The redirect address's port** may already be in use by another program on this machine. Your
  automated tests must not depend on it being free.
- **Nobody will sign in during your session**, and no browser step is possible: verify the sign-in
  with automated tests that play the browser's part.

There is otherwise no infra dependency beyond the .NET SDK — no Docker, no database. Don't
introduce any.

---

## Rules of engagement

- We want a **production-grade** tool — you decide what production-grade looks like.
- Ship automated tests that run without network access, and make them pass.
- When done, **self-verify** that it builds, that the sign-in behaves as described under your
  tests, and that the Square calls work against the sandbox (using `SQUARE_ACCESS_TOKEN` for that
  check only). Then give me a concise, step-by-step guide to try the tool myself.

---

## Constraints

- **Secrets never enter the repository.** Read the API credentials from the environment
  variables above and load them into **.NET user-secrets** yourself. Never write their
  **values** into any file inside this repository — not into `appsettings*.json`, not into
  a launch profile, a script, a test fixture, a comment, or a commit message. Referencing
  the variable/secret **names** is fine, the values are not.
- **Report a gap only when it is genuinely a gap.** Stop and report when the source you were
  given does not cover a capability this integration requires. A design decision being hard,
  open-ended, or left to your judgment is **not** a gap — decide it and proceed.
- **You are running headless — there is no one to answer you.** Work until the integration
  is fully complete. Never hand back, never end with a question, and never defer remaining
  work to the user: decide and proceed.

