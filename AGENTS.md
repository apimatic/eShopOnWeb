<!-- maxio-advanced-billing:rules:start (managed block: replace it with the SDK's knowledge/AGENTS.block.md after an SDK upgrade) -->
## Maxio Advanced Billing: rules for this project

This project calls Maxio through the Maxio Advanced Billing C#/.NET SDK. Before writing or changing Maxio code, read
`AGENTS.md` in the SDK folder and follow it. Look facts up there; never answer a Maxio business rule from memory.

Where the SDK folder is: the package's folder in the NuGet cache, which the project's `obj/project.assets.json` names and `dotnet
build` prints on a line that starts "Maxio Advanced Billing SDK:". With a `<ProjectReference>` to
`MaxioAdvancedBilling.csproj` instead, the nearest folder above that `.csproj` that holds `AGENTS.md`.
If that copy does not exist or holds no `AGENTS.md`, and the maxio-advanced-billing plugin is installed, use the copy it
bundles in its `sdk/dotnet/` folder; the plugin's session-start message names that folder on this machine.
If you cannot find the folder, ask the user for it.

The rules agents most often get wrong:
1. Send `payment_collection_method` on every signup; omitted, the site's own default applies. Under
   `automatic`, a signup owing money with no payment source gets 422 "No payment method was on file…".
2. `remittance` (Relationship Invoicing) or `invoice` (legacy Statements): the merchant bills by invoice;
   nothing is charged automatically. Choose by intent, never to clear that 422; `ReadSite` shows which.
3. No Idempotency-Key header. Send one `uniqueness_token` per write at the body root (events: inside
   `chargify`), the same on every resend, a new one after a corrected 422. Serialize writes per customer.
4. Page every list that takes `page` and `per_page`, with `per_page` ≤ 200, until an empty page; larger
   values are clamped silently.
5. `*_in_cents` fields are integer cents; component `unit_price` and invoice amounts are decimal.
6. Access: `active`, `trialing`, `past_due`, `soft_failure`; `pending`/`assessing` keep the last decision.
   `on_hold`, `suspended`, `unpaid`, `awaiting_signup` come back: no access, no second one on that plan.
7. A plan change via `UpdateSubscription` is not prorated; `MigrateSubscriptionProduct` resets the
   period and charges full price unless `preserve_period: true`.
8. At most 4 requests in flight per subdomain; on 429 back off for minutes. A write that times out or gets
   5xx/409 is unknown: read back, wait out 120 s, resend only with the same token; never call it failed.
9. A 4xx never proves a call works. A 409: outcome unknown. A 422 may be a 3-D Secure `action_link` or an
   account block. A 404 by reference: "not there", but only "not yet" after an unknown outcome.
10. Call Maxio from your server only; cards arrive as Maxio.js tokens.

When Maxio rejects a call, find its status and message in `knowledge/failures.md` in the SDK folder before changing code.
<!-- maxio-advanced-billing:rules:end -->
