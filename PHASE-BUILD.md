# Task — Add customer invoicing to eShopOnWeb

Make the eShopOnWeb reference app bill its shoppers for what they order, with **Visa** — through
its CyberSource payment platform — as the invoicing provider. eShopOnWeb today ends checkout by
writing an `Order` row and asks the shopper for nothing further: there is no bill, no way for a
shopper to pay one, and no record of what has been billed. This adds the bill raised against an
order, the moment it is put in front of the shopper, and the operator's view of what has actually
been billed. It is an **additive** capability — it does not replace the existing
catalog/basket/order flow.

You own the design and every implementation decision — architecture, file layout, build
order, patterns. Just honor the mandates and the details below.

---

## What to build

### Flow 1 — A bill for an order

A logged-in shopper's order becomes a bill held with the provider before anyone is asked to pay it.

- `POST /api/orders` — place an order from catalog items; the request carries catalog item
  ids and quantities, and reuses the app's existing order/order-item model rather than a
  parallel one (the caller's identity comes from the token).
- `POST /api/orders/{orderId}/invoice` — raise a bill with the provider for that order. What is
  billed comes from the order itself — its items and what they cost — not from anything the
  caller restates. The request carries the **calendar date the bill falls due**. A bill starts
  out not yet put to the shopper.
- `GET /api/invoices/{invoiceId}` — a bill's current state, whatever the provider reports about
  how it reached that state, and — once it has been put to the shopper — how they can pay it.
- `PATCH /api/invoices/{invoiceId}` — correct the due date, or the customer details the bill
  carries, on a bill that has not yet been put to the shopper. What is billed still comes from
  the order, so the amount is not correctable here. Once the bill has been put to the shopper,
  or once it has been withdrawn, correcting it is no longer possible and the caller must be told
  so rather than the change silently doing nothing.

A bill belongs to the shopper whose order it was raised against: one shopper must never see or
correct another's. The same goes for orders. Operator actions are the exception — an operator
acts on any shopper's bill, not only their own.

### Flow 2 — Putting the bill to the shopper, and taking it back

- `POST /api/invoices/{invoiceId}/issue` — put the bill to the shopper. Afterwards this
  application can hand out a way for them to pay it, and the bill reports itself as having been
  put to them.
- `POST /api/invoices/{invoiceId}/withdraw` — withdraw a bill that should not be paid. Afterwards
  it must no longer be payable, and the way to pay it must no longer be handed out.
- `GET /api/my-invoices` — the caller's bills, each showing where it has got to.

The bill has to carry enough of the state the provider owns — its identifier there, and where it
currently stands — that a later request can act on it and report on it, not only the one that
raised it.

### Flow 3 — What the operator can see

- `GET /api/invoices/reconciliation?from={from}&to={to}` — a report listing the provider's own
  record of bills raised in a date range and lining them up against what eShop believes it
  raised, so a bill the provider knows about and eShop doesn't — or the reverse — is visible.
  It covers the whole range. `from` and `to` are ISO-8601 date-times.
  **The provider account carries bills that are not this application's**, and the report must
  make plain which is which rather than presenting the provider's record as though it were all
  eShop's.

### Where it goes

Expose all capabilities as HTTP endpoints on the **`src/PublicApi`** project
(JWT-authenticated; the caller's identity comes from the token), following that project's
existing endpoint conventions, routed under `/api/` as named above. Every flow above has to
be drivable through that API alone, and each action a caller can take stays separately
invocable — not one do-everything call that raises, issues and withdraws behind a single
route. No storefront UI is required.

Issue, withdraw and reconciliation are **operator** actions: restrict them to the administrator
role this project already uses for its privileged endpoints. Every other endpoint is
shopper-scoped and acts only on the caller's own data.

### Response identifiers

So the flows can be driven end to end by a caller, a response that creates something returns
its identifier as a top-level field of the response body: `orderId` from `POST /api/orders`,
and `invoiceId` from `POST /api/orders/{orderId}/invoice`. Each entry returned by
`GET /api/my-invoices` and by the reconciliation report carries its own `invoiceId` too, since
that is what the operator endpoints act on. Where a bill that has been put to the shopper
reports how it can be paid, that is a top-level `paymentLink` on
`GET /api/invoices/{invoiceId}`. Everything else about the response shape is your call.

---

## Visa tooling — non-negotiable

- Use the **visa-sdk** plugin (from the **apimatic** marketplace) for **every** Visa
  interaction. It is your sole reference for how to talk to Visa.
- **Do not** web-search or rely on general/external knowledge for Visa API details.
- If the plugin does not expose a capability you need, **STOP and report the gap** — do not
  invent or work around it.
---

## Sandbox entities & test fixtures

Nothing is pre-seeded on the Visa side for this task — every bill is created dynamically. The
account is a shared sandbox: it already carries bills raised by other activity, and anything you
raise stays there afterwards. Keep the volume to what verifying the work needs.

- The account reaches the provider's **test** environment; no money moves and no real customer
  is ever contacted. Use only the customer details you invent for your own fixtures.
- eShopOnWeb prices its catalog without recording a currency. **This account bills in `USD`** —
  use it for every bill you raise, rather than picking one per call.
- **Some transitions are legitimately refused**, and that is expected rather than a missing
  capability: a bill that has been withdrawn, or already put to the shopper, will not accept
  every change that a fresh one would. Treat a refusal as an outcome of the state the bill is
  in — not as a defect in your integration and not as a gap.
- **There is no publicly reachable URL for this application.** The provider cannot call back
  into it, so anything you need to know about a bill has to be obtained by asking the provider,
  not by receiving a notification from it.

---

## Credentials

- Sandbox account credentials arrive as env vars: `VISA_MERCHANT_ID`, `VISA_KEY_ID`,
  `VISA_SECRET_KEY`.
- **Hard-code none of their values** — the same build has to run against a different Visa
  account than the one above. Referencing the variable names is fine; the values are not.
- **Bind `Visa:BaseUrl` from configuration**, and route **every** call this integration makes to
  Visa through it — without exception, whatever the capability and whatever the endpoint. When
  it is set, use it verbatim as the base address in place of any default your tooling would
  otherwise use; no provider call may bypass it or carry a hard-coded host. The same build has
  to be able to run against a different address than the one it is given here.
- `VISA_SECRET_KEY` is a secret: it is never logged, never returned by an endpoint, and never
  written into a source file.

---

## Environment gotchas (this machine)

- **SDK/runtime mismatch:** `global.json` pins the SDK to 8.0.x, but only the .NET 10 SDK is
  installed and the ASP.NET Core 8.0 runtime is missing. Let it roll forward
  (`rollForward: latestMajor`) and run with `DOTNET_ROLL_FORWARD=Major`, or install the
  ASP.NET Core 8.0 runtime (x64).
- **No SQL Server LocalDB:** default connection strings point at `(localdb)\mssqllocaldb`,
  which isn't here. Run with `UseOnlyInMemoryDatabase=true`. Caveat: the in-memory provider
  loses all data on restart and ignores migrations — so orders and bills only survive within a
  single run. Raise, correct, issue and withdraw the bills you created in that same run.
- **Per-host in-memory stores:** with the in-memory provider, Web and PublicApi each hold
  their **own isolated** store — an order placed through the Web storefront is invisible to
  PublicApi. Keep the invoicing flow verifiable end-to-end through PublicApi alone (that is
  why `POST /api/orders` is part of the surface).
- **Two hosts, two auth models:** Web = cookie, `https://localhost:5001`; PublicApi = JWT on
  its own ports. For curl/Postman against PublicApi, get a bearer token from its authenticate
  endpoint first — the storefront cookie won't work there.
- **HTTPS dev cert:** both hosts use `UseHttpsRedirection()`; ensure the dev cert is trusted
  (`dotnet dev-certs https --check`).
- **Ports:** when you run services, bind only to your assigned block
  (`APP_PORT_BLOCK_BASE` … `+APP_PORT_BLOCK_SIZE-1`; `launchSettings` already points there).
  Stop your previous instance before starting another — no stray processes on stale builds.

There is otherwise no infra dependency beyond the .NET SDK/runtime — no Docker, no broker,
no PostgreSQL. Don't introduce any.

---

## Rules of engagement

- We want a **production-grade** integration — you decide what production-grade looks like.
- When done, **self-verify** that it builds and the flows actually work — a bill really raised
  against an order, corrected while it still can be, put to the shopper and the way to pay it
  read back, a bill withdrawn and no longer payable, and a reconciliation report over a range
  that has data in it. Then give me a concise, step-by-step guide to verify the working
  integration myself.

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

