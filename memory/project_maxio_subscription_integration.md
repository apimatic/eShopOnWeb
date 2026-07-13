---
name: project-maxio-subscription-integration
description: eShopOnWeb now has a Maxio Advanced Billing subscription module (UC0-UC4) implemented per plan.md
metadata:
  type: project
---

A full Maxio Advanced Billing (Chargify) subscription integration was implemented in this eShopOnWeb clone per the repo-root `plan.md`, talking to Maxio over raw HTTP only (no SDK/client library), per the plan's hard requirement.

**What exists now:**
- ApplicationCore: `Subscription` entity (stateless, not EF-persisted — decision was idempotent-on-user-reference, no local DB mapping), `ISubscriptionService`, `IBillingClient` (provider-agnostic), `ISubscriptionCatalogOptions`, MediatR notifications (`SubscriptionActivated`, `SubscriptionPlanChanged`, `SubscriptionStateChanged`, `OrderPlaced`), exceptions (`BillingProviderException`, `SubscriptionNotFoundException`, `InvalidSubscriptionStateException`, `StalePlanPreviewException`).
- Infrastructure: `MaxioBillingClient` (the single HTTP seam, snake_case JSON via `JsonNamingPolicy.SnakeCaseLower`), `MaxioSettings` (implements `ISubscriptionCatalogOptions`), MediatR notification handlers, `Dependencies.AddSubscriptionServices()` shared by both hosts.
- Web: `Pages/Subscriptions/Plans.cshtml` + `Mine.cshtml` (subscribe, view, record usage, preview/confirm plan change, pause/resume/cancel/reactivate). `OrderService.CreateOrderAsync` now publishes `OrderPlaced`; `OrderPlacedUsageHandler` (Infrastructure) records 1 usage unit against the buyer's active subscription per order (best-effort, never fails checkout).
- PublicApi: `SubscriptionEndpoints/*` (JWT-secured, mirrors `CatalogItemEndpoints` `IEndpoint<>` pattern), admin role bypasses ownership checks for usage/lifecycle actions.
- Config: `Maxio:*` keys live in **user-secrets only** (Web + PublicApi, both hosts), never in appsettings.json. `Maxio:BaseUrl` overrides the subdomain-derived host verbatim when set — this was tested live by pointing it at a bogus port and confirming the client actually tried to hit it.

**Verified live against the `apimatic-hackathon` Maxio sandbox** (not mocked): UC1 subscribe + duplicate-idempotency, UC2 usage recording + running total + automatic order→usage hook, UC3 preview/confirm plan change + stale-preview rejection, UC4 pause/resume/cancel(now and end-of-period)/reactivate + illegal-transition rejection + admin-vs-owner authorization (404 on cross-user access) — all through both the PublicApi (curl+JWT) and the Web Razor Pages (cookie session via curl).

See [[feedback-eshoponweb-razor-pages-tempdata]] and [[feedback-eshoponweb-publicapi-di-gaps]] for gotchas hit while building this.
