# eShopOnWeb Subscribe — Maxio + eShopOnWeb Integration Plan

> **Revision note (this update):** Added a first-class requirement that the Maxio integration's **outbound target server (base URL) is configurable** — so the same build can hit **production, a dev/sandbox tenant, or a local mock server** purely through configuration, with no code change. This is stated as a convention in **§2.3**, given a concrete config knob in **§5**, wired in **§4.3** (and reflected in **§4.2**), and reinforced in **§7 (out of scope)** and **§8 (decisions)**. See the change map at the end of this document.

A subscription capability for eShopOnWeb: shoppers browse and subscribe to recurring plans from the storefront, Maxio Advanced Billing runs the billing, and eShopOnWeb's own layered architecture keeps the customer's account in sync.

A customer performs a billing action from the eShopOnWeb storefront (subscribe to a plan, change plan, report usage, pause/cancel). A new subscription module — built exactly like eShopOnWeb's existing domain features (an ApplicationCore service over a provider abstraction, an Infrastructure implementation, surfaced through the Web storefront and the PublicApi) — holds the request, drives the matching Maxio Advanced Billing operation, and reflects the result back to the customer's account. Subscription lifecycle actions raise in-process integration-event notifications through eShopOnWeb's existing MediatR pipeline, exactly the way the storefront's other features already use MediatR.

**Stack:** C# / .NET 8 (net8.0) · ASP.NET Core MVC + Razor Pages (storefront) · Blazor WebAssembly (admin) · MinimalApi.Endpoint endpoints (PublicApi) · EF Core (SQL Server / LocalDB / in-memory) · ASP.NET Core Identity · MediatR (in-process eventing).

---

## 0. Bottlenecks & Gotchas — eShopOnWeb environment

These are the eShopOnWeb-side constraints an executor will hit. They are about this local development environment and this codebase itself, not about how to call the billing provider. Several were observed first-hand bringing this clone up.

| # | Bottleneck | Impact | How to avoid getting stuck |
|---|------------|--------|----------------------------|
| 1 | SDK / runtime mismatch | `global.json` pins the SDK to 8.0.x; this machine has only the .NET 10 SDK, and the ASP.NET Core 8.0 runtime (x64) is not installed. A clean `dotnet run` fails to build (no 8.0 SDK) and then fails to launch (no 8.0 ASP.NET runtime). | Allow the SDK to roll forward (`global.json` → `rollForward: latestMajor`), and run the net8.0 app on the installed .NET 10 runtime with `DOTNET_ROLL_FORWARD=Major`. (Cleanest permanent fix: install the ASP.NET Core 8.0 Runtime, x64.) |
| 2 | No SQL Server LocalDB | The default connection strings point at `(localdb)\mssqllocaldb`, which is not installed here. EF seeding throws at startup. | Run with the built-in toggle `UseOnlyInMemoryDatabase=true` (read in `src/Infrastructure/Dependencies.cs`). Caveat: the in-memory provider loses all data on restart and ignores migrations — so any acceptance criterion that depends on persisted userId ↔ subscription mapping survives only within a single run. |
| 3 | Two runnable hosts, two auth models | Web (storefront) uses cookie auth and listens on `https://localhost:5001`. PublicApi uses JWT bearer and runs on its own ports. An endpoint placed in the wrong host gets the wrong auth and the wrong consumer. | Customer browser flows → Web (cookie). Programmatic / admin / external flows → PublicApi (JWT). For Postman/curl against PublicApi, first obtain a bearer token from its authenticate endpoint — the storefront cookie does not work there. |
| 4 | Secrets are not yet a discipline here | The repo currently hardcodes its JWT signing key in `ApplicationCore/Constants/AuthorizationConstants.cs` (marked "TODO: don't use in production"). It is tempting to follow that pattern for the Maxio key. Don't. | Both `Web.csproj` and `PublicApi.csproj` already declare a `UserSecretsId`. Put the Maxio API key in .NET user-secrets; `git grep` for the key must return nothing. |
| 5 | New migration required if you choose persistence | If subscriptions are persisted, they live in the existing `CatalogContext`; the relational provider needs a new migration, while the in-memory provider ignores it. | Add the entity + an EF Core migration; exercise both the SQL and in-memory paths so neither breaks. Or start stateless (see §8). |
| 6 | Subscriptions are a parallel flow | They do not go through eShopOnWeb's Basket → Order checkout (`OrderService.CreateOrderAsync`). | This is intentional/additive. If stakeholders expect "subscribe via the normal cart", that is extra scope — flag it, don't assume. |
| 7 | HTTPS redirect + dev cert | Both hosts call `UseHttpsRedirection()`; an untrusted dev cert breaks the browser flow. | Ensure the ASP.NET Core dev cert is present/trusted (`dotnet dev-certs https --check`). |

**Net:** there is no infrastructure dependency beyond the .NET SDK/runtime and (optionally) SQL Server LocalDB. There is no Docker, no Aspire, no message broker, and no PostgreSQL in this repo — and this plan deliberately does not introduce any.

---

## 1. Overview

### 1.1 What this is

eShopOnWeb Subscribe adds recurring subscription billing to the eShopOnWeb reference app. eShopOnWeb today is one-time-purchase commerce (Catalog → Basket → Order). This integration adds a parallel subscription capability — browse plans, subscribe, manage, and keep billing state in sync — with Maxio Advanced Billing as the system of record, implemented as a first-class eShopOnWeb domain feature that obeys the repo's existing Clean Architecture layering.

The design at a glance:

| Layer | eShopOnWeb Subscribe |
|-------|----------------------|
| Actor | Customer (logged-in shopper) + Admin (optional, for usage reporting and lifecycle actions across users) |
| Input source | eShopOnWeb storefront (Web, MVC + Razor Pages) — new Subscriptions pages; PublicApi for programmatic/admin access |
| Domain | ApplicationCore — Subscription aggregate, `ISubscriptionService`, a provider-agnostic billing-client interface, specifications, exceptions |
| Provider integration | Infrastructure — the single concrete billing client (Maxio) behind the ApplicationCore interface, via `IHttpClientFactory` |
| Billing engine | Maxio Advanced Billing — full subscription lifecycle |
| Eventing | In-process MediatR `INotification` on lifecycle changes (eShopOnWeb's existing in-process pipeline) |
| Identity | reuse eShopOnWeb ASP.NET Core Identity (`ApplicationUser`); storefront cookie, PublicApi JWT |

### 1.2 Why Maxio fits eShopOnWeb's architecture

Maxio is a B2B/B2C recurring-billing platform: Customers, Product Families/Products, Subscriptions (create/pause/resume/cancel/reactivate), Components (metered/quantity/prepaid/event-based usage), Usage records, Invoices, Coupons, Billing Portal, and reporting.

eShopOnWeb is a layered, repository-driven application: domain types and service interfaces live in ApplicationCore; concrete I/O (EF Core, email, logging) lives in Infrastructure behind those interfaces; the Web and PublicApi hosts compose everything through a small set of DI extension methods. Maxio slots in cleanly as a new domain service in ApplicationCore whose only outbound dependency is an interface, with the actual HTTP client living in Infrastructure — exactly how `IEmailSender`/`EmailSender` and `IRepository<T>`/`EfRepository<T>` are already shaped. Lifecycle changes are announced with MediatR notifications, mirroring how the storefront already dispatches work in-process.

We choose depth over breadth: one mandatory subscription use case plus a focused set of lifecycle/billing use cases, each specified end-to-end.

**Eventing reality check.** The original (dotnet/eShop) plan published lifecycle facts onto a RabbitMQ event bus with a durable outbox. eShopOnWeb has neither. The faithful eShopOnWeb equivalent is an in-process MediatR `INotification` published best-effort after the provider call succeeds. A durable broker + outbox is explicitly out of scope (§7); the acceptance bar for eventing is therefore re-scoped to best-effort in-process delivery (§2.5).

### 1.3 Demo pricing model (seeded in the Maxio sandbox)

A dedicated Product Family has been seeded on the Maxio site **apimatic-hackathon** for this integration. All UCs operate against the entities below. Use these exact handles and IDs in configuration and code — they are the verified, live identifiers in the sandbox.

**Product Family** — the container that holds the plans and the metered component:

| Attribute | Value |
|-----------|-------|
| Name | eShopSubscribe |
| Handle | eshop-subscribe |
| ID | 3008866 |

**Products** (the recurring plans customers subscribe to):

| Plan | Type | Handle | ID | Price | Used by |
|------|------|--------|----|-------|---------|
| Pro Plan | Product (recurring monthly) | eshop-pro | 7111477 | $299.00 / month | UC1 (hero subscribe target), UC3 (upgrade target from Basic) |
| Basic Plan | Product (recurring monthly) | basic-plan | 7111478 | $29.00 / month | UC3 (downgrade target from Pro); optional UC1 alternative |

Both plans: setup fee none, trial none, expires never, taxable no, requires payment method off (so the demo subscribes without card capture or 3-DS).

**Metered Component** (the pay-as-you-go add-on, lives on the Family and is therefore available to every subscription on either plan):

| Attribute | Value |
|-----------|-------|
| Name | API Calls |
| Handle | api-call |
| ID | 3033795 |
| Kind | Metered |
| Pricing scheme | Per Unit |
| Default price | $0.01 each |

Summary of which UC touches what:

- **UC1 (Subscribe):** enrolls the eShopOnWeb user in `eshop-pro` (or `basic-plan` if you parameterize the demo).
- **UC2 (Pay-as-you-go):** records usage against the `api-call` component on an active subscription; bills $0.01/unit on the next invoice.
- **UC3 (Plan change):** moves a subscription between `eshop-pro` and `basic-plan` with proration.
- **UC4 (Lifecycle):** pause/resume/cancel/reactivate any subscription on either plan.

🔐 **Secrets:** Maxio API key + site live in .NET user-secrets / env vars. Never committed; `git grep` for the key must return nothing.

---

## 2. eShopOnWeb integration conventions (the contract any new feature must follow)

These are the rules of the road inside the eShopOnWeb repo. Any new feature — including the subscription module — must follow all five. They are how eShopOnWeb expects features to be built; they are not optional, and they exist independently of any billing provider.

### 2.1 The DI composition roots are the single control room

eShopOnWeb has no AppHost. Its "single source of truth for what exists" is the set of composition roots:

- `src/Web/Configuration/ConfigureCoreServices.cs` (`AddCoreServices`) — domain services and repositories.
- `src/Web/Configuration/ConfigureWebServices.cs` (`AddWebServices`) — MediatR, view-model services, typed options.
- `src/Infrastructure/Dependencies.cs` (`ConfigureServices`) — DbContext selection (SQL Server vs in-memory).
- `src/PublicApi/Program.cs` — the equivalent wiring for the API host.
- `appsettings.json` / user-secrets / `launchSettings.json` — configuration and run profiles.

Nothing exists in the app unless one of these composition roots registers it. For this integration that means:

- `ISubscriptionService` and the billing-client interface are registered in `AddCoreServices` (and in `PublicApi/Program.cs` if the API host needs them), exactly like `IOrderService`/`OrderService` and `IRepository<>`/`EfRepository<>` already are.
- The concrete Maxio client is registered as a typed `HttpClient` (via `IHttpClientFactory`) in the appropriate composition root.
- If subscriptions are persisted, the new entity is mapped in `CatalogContext` and migrated — no separate database.
- The storefront pages/controllers and any PublicApi endpoints are discovered by the hosts they live in (MVC/Razor Pages auto-discovery in Web; `AddEndpoints()`/`MapEndpoints()` in PublicApi).

### 2.2 Obey Clean Architecture layering (the dependency rule)

eShopOnWeb is strict about direction: Web/PublicApi → Infrastructure → ApplicationCore, and ApplicationCore depends on nothing outward. The new feature must respect this:

- **ApplicationCore** owns the Subscription aggregate (`: BaseEntity, IAggregateRoot`), the `ISubscriptionService`, the provider-agnostic billing-client interface, the Ardalis.Specification query specs, and domain exceptions. It must not reference Maxio's SDK or `HttpClient` directly — only the interface.
- **Infrastructure** owns the one concrete billing client that talks to Maxio, the EF Core mapping/migration, and the typed-options binding — mirroring how `EmailSender` and `EfRepository<T>` live here. **This is also the single place that resolves the outbound base URL (§2.3), so retargeting prod/dev/mock never leaks beyond this one class.**
- **Web / PublicApi** own only orchestration and presentation (controllers, Razor Pages, endpoints, view models).

This is the eShopOnWeb-native expression of the original plan's "single integration point with the billing provider": the provider is touched in exactly one class in Infrastructure, behind one ApplicationCore interface.

### 2.3 Configuration through typed Options; secrets through user-secrets; target server is configurable

eShopOnWeb loads configuration into typed settings classes (e.g. `CatalogSettings`, bound via `services.Configure<CatalogSettings>(configuration)` / `configuration.Get<CatalogSettings>()`). Secrets currently sit in a constants file with a "don't use in production" TODO — that is the anti-pattern to replace, not to copy.

For this integration:

- A typed `MaxioSettings` (or equivalent) class holds the provider configuration shape (subdomain, environment, family/product/component handles + IDs).
- The Maxio API key enters through .NET user-secrets (both Web and PublicApi already declare a `UserSecretsId`) and is bound via the same Options mechanism.
- No credentials appear in source files, `appsettings.json`, or anywhere reachable by `git grep`.
- **The outbound target server (base URL) is configurable — this is a hard requirement, not a convenience.** `MaxioSettings` includes an explicit base-URL / server override (`Maxio:BaseUrl`) that the typed `HttpClient`'s `BaseAddress` is set from. **Resolution order the client MUST honor:** if an explicit `Maxio:BaseUrl` is configured, the client targets it **verbatim**; only if it is absent does the client derive the host from `Subdomain` (+ region `Environment`). The point is that the *identical build* can be pointed at:
  - **production** (the real Maxio host, or the subdomain-derived default),
  - a **dev / sandbox tenant** (e.g. `apimatic-hackathon`), or
  - a **local mock server** for testing (e.g. `http://localhost:8080`),

  purely through configuration — never a code change, never a recompile. Because the value is Options-bound, each host (Web / PublicApi) and each run profile (`appsettings.json` / user-secrets / environment variables / `launchSettings.json` / test config) can override it independently. **Implementers must not build a client that silently ignores `Maxio:BaseUrl` in favor of a hardcoded or subdomain-only host** — an explicit override always wins. Note that `Maxio:Environment` denotes the Maxio *data-center region* (US/EU), which is a separate axis from the *deployment target* (prod/dev/mock) controlled by `Maxio:BaseUrl`.

### 2.4 Identity / auth: reuse eShopOnWeb's existing login systems

eShopOnWeb already has identity; new features do not invent their own.

- **Storefront (Web):** ASP.NET Core Identity with cookie auth. The signed-in customer is obtained the way the existing code does it — `User.Identity.Name` (e.g. `OrderController`) or `UserManager.GetUserId(User)` (e.g. `ManageController`). Customer-facing subscription pages are `[Authorize]`.
- **PublicApi:** JWT bearer. Endpoints decorate with `[Authorize(... AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]`, and admin-only endpoints add `Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS` — exactly like `CreateCatalogItemEndpoint`.

For this integration:

- The customer subscribe/manage flows live in the Web storefront and run in-process — so there is no cross-service token forwarding to build (this is a simplification versus the original plan's `AddAuthToken()` plumbing).
- The stable customer reference used to identify the same eShopOnWeb user across repeated subscribe calls (idempotency on the billing-provider customer record) is the user's email/username (`User.Identity.Name`) — decided in §8.

### 2.5 Communicate state changes through in-process MediatR notifications (no broker, no outbox)

eShopOnWeb has no event bus. Its existing in-process mediator (`AddMediatR(...)` in `ConfigureWebServices`, used by the MyOrders / OrderDetails features) is the native mechanism for announcing that something happened.

For this integration: whenever a subscription's state meaningfully changes — activated, plan changed, paused/resumed/cancelled/reactivated — the subscription service publishes the corresponding MediatR `INotification` (`SubscriptionActivated`, `SubscriptionPlanChanged`, `SubscriptionStateChanged`) through `IPublisher`. Any in-process handler (e.g. send a confirmation email via the existing `IEmailSender`, write an audit log via `IAppLogger<>`) subscribes.

Because there is no durable outbox, publication is best-effort and in-process only. If a future requirement demands cross-process, durable delivery, that is a new infrastructure project (broker + outbox) and is out of scope here (§7). The acceptance bar for eventing is exactly the in-process guarantee: after a successful provider call, the corresponding notification is published and any registered in-process handlers run — nothing more is promised.

---

## 3. Use Cases

Each use case is specified in production-integration terms: actor, goal, trigger, preconditions, main success scenario in domain language, success state, failure scenarios, and the capabilities the system must possess. The plan does not prescribe which provider endpoints or models to use — that is left to the implementing agent.

UC0 (Seed) is a one-time setup prerequisite — every other UC assumes it has been completed. UC1 (Subscribe) is the mandatory hero flow. UC2 (pay-as-you-go usage) is a priority focus. UC3–UC4 add depth.

### UC0 — Seed the Billing Provider Sandbox (one-time setup, prerequisite for all other UCs)

The integration depends on a specific set of entities existing in the billing provider before any customer-facing flow can run. UC0 provisions those entities. It is performed once per sandbox (or on rebuild), by a developer or an automation script — never by a customer.

- **Priority:** Must — prerequisite for UC1–UC4
- **Actor:** Integration operator (developer setting up the sandbox, or an automated seeding script with admin credentials)
- **Goal:** Provision the product family, recurring plans, and metered component the integration expects, with the exact handles the integration is configured to use.
- **Preconditions:**
  - Operator has billing-provider credentials with permission to create product families, products, and components on the target site (`apimatic-hackathon`)
  - The configured handles (`eshop-subscribe`, `eshop-pro`, `basic-plan`, `api-call`) are either available, or any existing entities with these handles match the expected shape
- **Trigger:** First-time setup of the integration against a fresh (or wiped) sandbox; or re-running setup after the sandbox has been reset.

**Main success scenario:**

1. Operator creates a product family with name `eShopSubscribe` and handle `eshop-subscribe`.
2. Operator creates the primary recurring plan inside that family — name Pro Plan, handle `eshop-pro`, price $299.00 / month, no trial, no setup fee, expires never, taxable no, payment method NOT required.
3. Operator creates the alternate recurring plan inside the same family — name Basic Plan, handle `basic-plan`, price $29.00 / month, same other settings (no trial, no setup fee, expires never, taxable no, payment method not required).
4. Operator creates the metered component on the family — name API Calls, handle `api-call`, kind Metered, pricing scheme per-unit, default price $0.01 / unit.
5. Operator records the resulting IDs (family, products, component) and updates the integration's configuration to match.
6. Operator verifies the seed by reading back the family and confirming the products and component are listed under it.

**Success state:**

- Product family `eshop-subscribe` (id 3008866) exists and is reachable with the configured API key.
- Products `eshop-pro` (id 7111477, $299/mo) and `basic-plan` (id 7111478, $29/mo) exist inside that family, both with requires payment method = off.
- Metered component `api-call` (id 3033795) exists on that family with kind Metered and default price $0.01/unit.
- The integration's "list plans" capability (UC1, step 1) returns these entities, and the integration's startup validation (UC2 preconditions) confirms `api-call` is of metered kind.

**Failure scenarios:**

- Handle already taken by an unrelated entity → either rename the seed entity or archive the conflicting one; do not silently reuse a mismatched entity.
- Component created with the wrong kind (e.g. Quantity-Based instead of Metered) → component cannot be type-converted in place; archive it and recreate as Metered. Skipping this will cause UC2 to fail at runtime with a confusing error.
- Component created on the wrong family → it will not be available to `eshop-pro` / `basic-plan` subscriptions. Recreate on the correct family.
- Plan created with requires payment method = on → subscribe (UC1) will demand card capture or trigger 3-DS in the demo path. Either flip the toggle off, or accept that UC1 must be exercised with the card-capture path.
- Plan created with a trial period or setup fee → first-invoice expectations in UC1's success state will not match.
- Credentials lack admin scope → operator cannot create entities; obtain an API key with sufficient permissions before retrying.

**Required capabilities:**

- Create a product family with a name and stable handle.
- Create a recurring product inside a family with name, handle, price, billing interval, and the toggle for whether a payment method is required.
- Create a metered component on a product family with name, handle, kind (must be metered), pricing scheme, and unit price.
- Read back the family and its products / components to verify the seed.
- Archive existing entities cleanly when correcting a mis-created entity (rather than mutating them in place).

**Notes for the implementing agent:**

- UC0 is not exposed through the subscription module — it is operator tooling. It may be performed manually through the provider's admin UI or scripted as a one-shot console / setup task (a small console project or a script under `src/` that is not wired into the storefront).
- The seed values listed above (`eshop-subscribe`, `eshop-pro`, `basic-plan`, `api-call`, and their IDs) are the live state of the `apimatic-hackathon` sandbox at the time of writing. If those entities still exist and match this spec, UC0 is already satisfied and no action is required.
- If reseeding from scratch, the resulting IDs will be new (Maxio assigns them at creation). The handles are stable; IDs are not. Update the configuration with the new IDs after reseeding.

### UC1 — Subscribe to a Plan (mandatory hero use case)

A logged-in shopper enrolls in a recurring subscription plan.

- **Priority:** Must — hero flow
- **Actor:** Customer (authenticated)
- **Goal:** Enroll in a recurring subscription plan from the storefront and see it reflected in their account.
- **Preconditions:**
  - Customer is authenticated against eShopOnWeb Identity (cookie session in the storefront)
  - At least one plan is available from the billing provider — for the demo, Pro Plan, handle `eshop-pro`, id 7111477, in product family `eshop-subscribe` (id 3008866). Basic Plan (`basic-plan`, id 7111478) is also available as an alternative.
- **Trigger:** Customer clicks "Subscribe" on a plan from the Plans page.

**Main success scenario:**

1. Customer browses the catalog of available plans, each shown with name, price, and billing interval.
2. Customer selects a plan.
3. System ensures a customer record exists for this eShopOnWeb user in the billing system, creating one (linked to the eShopOnWeb user identity) if it does not already exist.
4. System enrolls the customer in the selected plan.
5. System persists the link between the eShopOnWeb user and the resulting subscription (or, if running stateless, relies on the idempotent provider-side customer reference).
6. System publishes a `SubscriptionActivated` in-process notification through MediatR.
7. Customer is shown a confirmation: plan, price, state, and next billing date.

**Success state:** Subscription is active in the billing provider and visible from the customer's account page in eShopOnWeb.

**Failure scenarios:**

- Plans cannot be listed (provider unreachable, bad credentials) → show a friendly error on the Plans page; no enrollment is attempted.
- Configured product handle does not resolve (sandbox reseeded, stale IDs) → fail with a configuration error pointing back at UC0; do not enroll against a guessed plan.
- Customer record is created but enrollment fails → the provider-side customer exists without a subscription; surface the error. Retrying is safe because customer creation is idempotent on the user reference (§4.4).
- Duplicate subscribe (double-click, repeated call) while a subscription is already active for this customer → detect the existing active subscription via the customer reference and return it; never create a second enrollment.
- Provider rejects enrollment (e.g. plan unexpectedly requires a payment method) → surface the provider's message; see UC0's failure scenarios for correcting the seed.
- Notification publish fails after successful enrollment → the subscription stands; log the handler failure and do not roll back (best-effort eventing, §2.5).

### UC2 — Pay-As-You-Go Usage Billing (priority focus)

A customer consumes a metered resource; the consumption is billed on top of their flat subscription fee.

- **Priority:** Should — priority focus
- **Actor:** Customer (own usage) or Admin (any subscription)
- **Goal:** Bill the customer for variable consumption of a metered resource, accruing to the next invoice.
- **Preconditions:**
  - Customer has an active subscription on a plan within product family `eshop-subscribe` (either `eshop-pro` or `basic-plan`)
  - The metered component `api-call` (id 3033795, kind Metered, $0.01/unit) exists on the family and is therefore automatically available to the subscription
  - At service startup (and again before the first usage call), the integration has verified that the configured component handle resolves to a component of metered kind on the family — otherwise it refuses to record usage
- **Trigger:** A unit (or batch of units) of the metered resource is consumed — reported either manually from an admin tool or automatically from an eShopOnWeb in-process event (e.g. each order placed records one unit).

**Main success scenario:**

1. Actor reports a quantity of usage against a specific metered item on the customer's subscription, with an optional memo.
2. System records the usage with the billing provider.
3. System reads back the running period-to-date total.
4. Customer (or admin) sees the recorded usage and is informed it will appear on the next renewal invoice.

**Success state:** Usage is recorded against the subscription; the billable unit balance increments; the charge appears on the next renewal invoice.

**Failure scenarios:**

- The configured component handle resolves to a non-metered component (or does not resolve at all) → the startup/first-call validation refuses to record usage; fix the seed (UC0) before retrying.
- The customer has no active subscription → reject the usage report; nothing is sent to the provider.
- Quantity is zero or negative → reject as invalid input before any provider call.
- Provider call fails after the report was sent (timeout, ambiguous response) → do not blindly resend; read back the period-to-date total first to avoid double-billing the same units.
- Read-back of the running total fails after a successful record → the usage stands; report success with the total marked unavailable rather than failing the whole operation.

**eShopOnWeb hook for "automatic" usage:** the storefront's checkout already runs through `OrderService.CreateOrderAsync`. To demo "one order placed → one billable unit", raise a MediatR notification at order creation and let a usage handler call the subscription service. Decided (§8): usage is wired automatically — one order placed raises a MediatR notification on `OrderService.CreateOrderAsync` that records one usage unit (admin-reported usage remains available alongside it).

### UC3 — Plan Change (Upgrade / Downgrade with Proration Preview)

A customer changes their plan and sees the prorated cost before committing.

- **Priority:** Should
- **Actor:** Customer (own subscription)
- **Goal:** Move an existing subscription to a different plan, with the prorated cost shown and confirmed before any charge.
- **Preconditions:** Customer has an active subscription on either `eshop-pro` (id 7111477) or `basic-plan` (id 7111478); the other plan is available as a target.
- **Trigger:** Customer selects the other plan (upgrade `basic-plan` → `eshop-pro`, or downgrade `eshop-pro` → `basic-plan`) from the management page.

**Main success scenario:**

1. Customer selects a target plan and timing — either apply now with proration or at next renewal without proration.
2. System computes a preview: the prorated charge or credit (for "now") or the new plan price effective from the next period (for "at renewal").
3. Customer confirms.
4. System commits the plan change with the chosen timing.
5. System publishes a `SubscriptionPlanChanged` in-process notification.
6. Customer is shown old plan → new plan, the proration amount, and the effective date.

**Success state:** Subscription is on the new plan at the chosen effective time; the charge or credit applied matches the previewed amount.

**Failure scenarios:**

- Target plan is the same as the current plan → reject as a no-op before any provider call.
- Target plan handle does not resolve or the plan is archived → configuration error; point back at UC0.
- Preview is stale at commit time (price or proration basis changed between preview and confirm) → reject the commit and require a fresh preview; never silently apply a different amount than the one shown (§6, Phase 4).
- Subscription is not in a state that allows a plan change (e.g. cancelled) → reject with the current state; direct the customer to reactivate first (UC4).
- Provider call fails during commit → re-read the subscription's state from the provider before any retry; do not risk applying the change twice.
- Notification publish fails after a successful change → the plan change stands; log only (best-effort eventing, §2.5).

### UC4 — Subscription Lifecycle (Pause / Resume / Cancel / Reactivate)

One management surface, four lifecycle actions.

- **Priority:** Must
- **Actor:** Customer (own subscription) or Admin (any)
- **Goal:** Manage the lifecycle state of an existing subscription.
- **Preconditions:** Subscription exists; the requested transition is legal from its current state.
- **Trigger:** Actor selects one of: pause, resume, cancel (immediate or end-of-period), reactivate.

**Main success scenario:**

1. Actor selects a lifecycle action (and, for cancel, a timing — immediate or end-of-period) with an optional reason.
2. System applies the requested transition against the subscription.
3. System publishes a `SubscriptionStateChanged` in-process notification carrying old → new state.
4. Actor sees the new state and the effective date of the transition.

**Success state:** Subscription reaches the requested state at the requested effective time.

**Failure scenarios:**

- Illegal transition from the current state (resume a subscription that is not paused, reactivate one that is active, pause one that is cancelled) → reject with the current state and the legal transitions; make no provider call.
- Provider rejects a transition the local check allowed (state drifted out-of-band — dunning or admin action in the Maxio UI; there are no webhooks, §7) → treat the provider's state as truth, refresh the local view, and surface the conflict.
- End-of-period cancel requested while the subscription is already pending cancellation or past-due → surface the provider's outcome rather than reporting the request as newly applied.
- Provider call fails mid-transition → re-read the subscription state before retrying; do not assume the transition failed.
- Notification publish fails after a successful transition → the state change stands; log only (best-effort eventing, §2.5).

---

## 4. Architecture (grounded in eShopOnWeb's existing patterns)

This section describes only what is grounded in eShopOnWeb's own repository conventions (mirroring `OrderService`, `EfRepository`, `EmailSender`, the `IEndpoint<>` Catalog endpoints, the Basket Razor Pages, and the MediatR Features). It does not prescribe how to talk to the billing provider — that is the implementing agent's decision, confined to a single Infrastructure class.

### 4.1 New + touched files (mirroring eShopOnWeb's existing layout)

```
src/ApplicationCore/                              # DOMAIN — depends on nothing outward
├── Entities/SubscriptionAggregate/
│   └── Subscription.cs                           # : BaseEntity, IAggregateRoot
│                                                 #   (eShop userId ↔ provider customer/subscription refs, plan, state)
├── Interfaces/
│   ├── ISubscriptionService.cs                   # mirror IOrderService — the use-case surface
│   └── IBillingClient.cs                         # PROVIDER-AGNOSTIC abstraction (the single seam)
├── Services/
│   └── SubscriptionService.cs                    # mirror OrderService — orchestrates billing client + repo + IPublisher
├── Specifications/
│   └── SubscriptionsByUserSpecification.cs       # mirror CustomerOrdersSpecification (Ardalis.Specification)
├── IntegrationEvents/                            # MediatR INotification types
│   ├── SubscriptionActivated.cs
│   ├── SubscriptionPlanChanged.cs
│   └── SubscriptionStateChanged.cs
└── Exceptions/
    └── BillingProviderException.cs               # mirror DuplicateException / BasketNotFoundException

src/Infrastructure/                               # I/O — the only place the provider is touched
├── Services/
│   └── MaxioBillingClient.cs                     # implements IBillingClient via a typed HttpClient (IHttpClientFactory);
│                                                 #   resolves BaseAddress from MaxioSettings (explicit BaseUrl wins) — §2.3
├── Configuration/
│   └── MaxioSettings.cs                          # typed options (mirror CatalogSettings usage); includes BaseUrl override
├── Data/Config/
│   └── SubscriptionConfiguration.cs              # EF mapping (optional persistence) — mirror OrderConfiguration
└── Data/Migrations/                              # + a new migration if persistence is chosen (CatalogContext)

src/Web/                                          # STOREFRONT — cookie auth, customer-facing
├── Configuration/ConfigureCoreServices.cs        # + register ISubscriptionService, IBillingClient, typed HttpClient
├── Pages/Subscriptions/
│   ├── Plans.cshtml(.cs)                         # mirror Pages/Basket/Index — list plans, Subscribe button
│   └── Mine.cshtml(.cs)                          # [Authorize], mirror OrderController.MyOrders view — view/manage
└── (nav links in the shared layout)

src/PublicApi/                                    # JWT API — programmatic / admin / external
└── SubscriptionEndpoints/                        # mirror CatalogItemEndpoints (IEndpoint<>)
    ├── ListPlansEndpoint.cs                      # anonymous GET (mirror CatalogItemListPagedEndpoint)
    ├── SubscribeEndpoint.cs                      # [Authorize] POST (mirror CreateCatalogItemEndpoint)
    ├── MySubscriptionsEndpoint.cs                # [Authorize] GET
    ├── RecordUsageEndpoint.cs                    # [Authorize] POST  (UC2; admin-guarded for "any subscription")
    ├── PlanChangeEndpoint.cs (+ preview)         # [Authorize] POST  (UC3)
    └── LifecycleEndpoint.cs                      # [Authorize] POST  (UC4)

(optional) src/BlazorAdmin/Pages/Subscriptions/   # admin usage/lifecycle UI — mirror CatalogItemPage/*
src/SubscriptionsSeed/ (or a script)              # UC0 operator tooling, NOT wired into the hosts
```

The split is deliberate: UC1 (hero, customer, browser) is simplest in the Web storefront (cookie auth, in-process, no token forwarding). UC2/UC4 admin and any programmatic/external access fit the PublicApi (JWT, `[Authorize(Roles = Administrators)]`). The two hosts share the same ApplicationCore service and Infrastructure client — the provider is still touched in exactly one place. Decided (§8): both hosts are used — UC1's hero flow lives in the Web storefront; admin and programmatic flows live in the PublicApi.

### 4.2 Component responsibilities

| Component | Responsibility |
|-----------|----------------|
| Web Razor Pages / controllers | Authenticated Subscriptions pages; one action per UC; resolve the user via `User.Identity.Name` / `UserManager.GetUserId`; render plans, state, previews. |
| PublicApi endpoints (`IEndpoint<>`) | JWT-secured programmatic surface; validate input; `[Authorize]`, admin routes add `Roles = Administrators`; delegate to `ISubscriptionService`. |
| `ISubscriptionService` (ApplicationCore) | Use-case orchestration (mirror `OrderService`): validate, call the billing client, persist the link (optional), publish the MediatR notification. |
| `IBillingClient` (ApplicationCore) → `MaxioBillingClient` (Infrastructure) | Single integration point with the billing provider. Nothing else talks to the provider directly. **Resolves its outbound base URL from `MaxioSettings` so the target (prod / dev / mock) is configuration-driven (§2.3).** Normalizes results and throws typed errors. |
| Subscription entity + `IRepository<Subscription>` (optional) | Persists userId ↔ billing identifiers in `CatalogContext` via `EfRepository<>`. |
| MediatR `INotification` handlers | In-process reactions (email via `IEmailSender`, audit via `IAppLogger<>`). |

### 4.3 Wiring (composition roots, not an AppHost)

```csharp
// src/Web/Configuration/ConfigureCoreServices.cs  (AddCoreServices) — mirror existing registrations
services.AddScoped<ISubscriptionService, SubscriptionService>();
services.Configure<MaxioSettings>(configuration.GetSection("Maxio"));

// Typed client via IHttpClientFactory. The BaseAddress is resolved from configuration so the
// SAME build can target prod / dev / a local mock — explicit Maxio:BaseUrl wins, else derive
// from Subdomain (+ region). See §2.3. Do NOT hardcode the host.
services.AddHttpClient<IBillingClient, MaxioBillingClient>((sp, http) =>
{
    var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
    http.BaseAddress = new Uri(settings.ResolveBaseUrl());   // BaseUrl override ?? derived-from-subdomain
});
// (IRepository<>/IReadRepository<> and AddMediatR are already registered)
```

- `MaxioSettings.ResolveBaseUrl()` (or equivalent composition-root logic) returns the explicit `Maxio:BaseUrl` when present, otherwise the subdomain-derived host. This is the one place retargeting happens; keeping it here means pointing at a mock server for tests is a config change, not a code change.
- Register the same trio in `src/PublicApi/Program.cs` if the API host exposes subscription endpoints.
- If persistence is chosen, map `Subscription` in `CatalogContext` and add an EF Core migration.
- MediatR is already added in `ConfigureWebServices` (`AddWebServices`); the new `INotification` types and handlers are discovered from the assembly scan — no extra registration.

### 4.4 Identity / customer mapping

- eShopOnWeb user identity in the storefront = the signed-in Identity user (`User.Identity.Name` for username/email, or `UserManager.GetUserId(User)` for the stable id) — existing patterns in `OrderController` and `ManageController`.
- That value is the stable reference used to identify the same user across repeated subscribe calls (idempotency on the billing-provider customer record). Decided (§8): the reference is the user's email/username (`User.Identity.Name`).

---

## 5. Configuration (user-secrets / env)

Stored in .NET user-secrets for the host that owns the subscription endpoints (Web and/or PublicApi — both already declare a `UserSecretsId`). Never committed to git. All identifiers below are the live values seeded on the `apimatic-hackathon` sandbox.

```
# --- Credentials ---
Maxio:ApiKey                    = <provided out-of-band; never commit>
Maxio:Subdomain                 = apimatic-hackathon
Maxio:Environment               = US            # Maxio DATA-CENTER REGION (US/EU) — NOT the deployment target

# --- Target server (CONFIGURABLE: prod / dev / mock) ---
# Optional explicit override for the outbound base URL. When set, it WINS over the
# Subdomain-derived host, so the same build can be pointed at production, a dev/sandbox
# tenant, or a local mock server — no code change. Leave empty to use the derived host.
Maxio:BaseUrl                   =               # e.g. https://apimatic-hackathon.chargify.com (sandbox)
                                                #      or  http://localhost:8080 (local mock, for tests)

# --- Demo entities (Product Family / Products / Metered Component) ---
Maxio:ProductFamilyHandle       = eshop-subscribe
Maxio:ProductFamilyId           = 3008866

Maxio:DefaultProductHandle      = eshop-pro
Maxio:DefaultProductId          = 7111477

Maxio:AlternateProductHandle    = basic-plan
Maxio:AlternateProductId        = 7111478

Maxio:MeteredComponentHandle    = api-call
Maxio:MeteredComponentId        = 3033795
```

**Target-server selection (the configurable endpoint).** `Maxio:BaseUrl` is the single knob that decides which server the integration hits. The client resolves its `BaseAddress` as: *explicit `Maxio:BaseUrl` if present, otherwise the host derived from `Maxio:Subdomain` (+ region).* Because it is Options-bound, any run profile can override it independently:

- **Production / sandbox:** leave `Maxio:BaseUrl` empty to use the `Subdomain`-derived host, **or** set it explicitly to the real host.
- **Dev tenant:** point `Maxio:Subdomain`/`Maxio:BaseUrl` at the dev site.
- **Local mock (testing):** set `Maxio:BaseUrl = http://localhost:8080` (or wherever a mock listens). All outbound Maxio traffic then targets the mock; no live calls escape. **Note:** supporting this target is a configuration capability the client must have — it is *not* a task. This plan does not ask the implementer to stand up, invoke, or investigate a mock server (§7, §8).

Set the secrets (example for the PublicApi host):

```bash
cd src/PublicApi          # or src/Web
dotnet user-secrets set "Maxio:ApiKey"                 "<key>"
dotnet user-secrets set "Maxio:Subdomain"              "apimatic-hackathon"
dotnet user-secrets set "Maxio:Environment"            "US"
dotnet user-secrets set "Maxio:BaseUrl"                ""            # empty = derived host; set to override (prod/dev/mock)
dotnet user-secrets set "Maxio:ProductFamilyHandle"    "eshop-subscribe"
dotnet user-secrets set "Maxio:ProductFamilyId"        "3008866"
dotnet user-secrets set "Maxio:DefaultProductHandle"   "eshop-pro"
dotnet user-secrets set "Maxio:DefaultProductId"       "7111477"
dotnet user-secrets set "Maxio:AlternateProductHandle" "basic-plan"
dotnet user-secrets set "Maxio:AlternateProductId"     "7111478"
dotnet user-secrets set "Maxio:MeteredComponentHandle" "api-call"
dotnet user-secrets set "Maxio:MeteredComponentId"     "3033795"

# Example: point tests at a local mock instead of the sandbox (no secret; env var also works)
# dotnet user-secrets set "Maxio:BaseUrl" "http://localhost:8080"
# or:  export Maxio__BaseUrl="http://localhost:8080"
```

Only `Maxio:ApiKey` is sensitive. The IDs, handles, and `Maxio:BaseUrl` are **not** secret — they're environment metadata, kept in user-secrets together purely for convenience (and `Maxio:BaseUrl` is just as legitimately supplied via `appsettings.{Environment}.json`, an environment variable `Maxio__BaseUrl`, or `launchSettings.json`). Bind them via a typed `MaxioSettings` Options class (mirror `CatalogSettings`); do not follow the hardcoded-constant pattern of `AuthorizationConstants.JWT_SECRET_KEY`.

---

## 6. Implementation plan (phased)

Each phase delivers a verifiable slice. The phases are described in capability terms — what the slice does — not how the implementing agent should call the provider.

**Phase 0 — Seed the billing-provider sandbox (UC0).** Ensure the product family, plans, and metered component exist on the sandbox with the configured handles. Current sandbox state: family `eshop-subscribe` (id 3008866), products `eshop-pro` (id 7111477) and `basic-plan` (id 7111478), metered component `api-call` (id 3033795) — all present, no action needed unless reseeding from scratch. *Verify:* Reading back the family shows the two products and the component; the integration's API key can list them.

**Phase 1 — Domain + provider seam.** In ApplicationCore: define the `Subscription` entity, `ISubscriptionService`, and the provider-agnostic `IBillingClient`. In Infrastructure: implement `MaxioBillingClient` (the single provider seam) behind that interface with a typed `HttpClient`, bind `MaxioSettings`, **resolve the base URL from configuration so the target server is switchable (prod/dev/mock) per §2.3/§4.3**, and register everything in the composition roots. Expose a first read capability: list available plans.

**Phase 2 — UC1 Subscribe (hero).** `SubscriptionService` ensures a provider-side customer exists for the eShopOnWeb user (idempotent on the user reference) and enrolls them in the chosen plan; add the "my subscriptions" read capability; publish `SubscriptionActivated` via MediatR. Surface it through the chosen host (storefront page and/or PublicApi endpoint).

**Phase 3 — UC2 Pay-as-you-go (priority).** The metered component (`api-call`, id 3033795, $0.01/unit) is already on family `eshop-subscribe` and therefore available on every `eshop-pro` / `basic-plan` subscription. Add the capability to record usage against a subscription's `api-call` component and to read the period-to-date total. Include the startup/first-call validation that the configured component handle resolves to a metered-kind component on the family. (Optionally wire the order-placed MediatR notification → one usage unit.)

**Phase 4 — UC3 / UC4 (plan change + lifecycle).** Preview + commit plan change with proration timing (verify between `eshop-pro` ↔ `basic-plan`); pause / resume / cancel (immediate or end-of-period) / reactivate; publish the corresponding state notifications on each transition. *Verify:* State transitions reflect in both Maxio and eShopOnWeb; preview matches commit (or is rejected as stale); end-of-period cancel defers to the period boundary; notifications fire in-process.

**Phase 5 — UI.** Storefront Plans and Mine Razor Pages (mirror the Basket pages and the Orders view), plus a minimal usage panel; navigation links. Optionally an admin surface in BlazorAdmin for cross-user usage/lifecycle.

---

## 7. Out of scope (mention, don't build)

- A durable message broker / event bus + outbox (RabbitMQ-style) — eShopOnWeb has none, and this integration does not add one. Eventing is in-process MediatR, best-effort, and the acceptance bar for eventing is scoped to that guarantee (§2.5).
- .NET Aspire / AppHost / service orchestration — not present in this repo; wiring is through the existing DI composition roots (§2.1).
- PostgreSQL — eShopOnWeb uses SQL Server / LocalDB / in-memory; no provider swap.
- Maxio → eShopOnWeb webhooks (live state sync) — all actions are initiated from eShopOnWeb and return results synchronously. The "my subscriptions" view reflects eShopOnWeb's view, which may lag the provider for out-of-band changes (dunning, admin actions in the Maxio UI).
- Replacing eShopOnWeb's existing one-time Basket/Order flow — this is additive (subscriptions only).
- Production PCI hardening — demo uses test mode and the sandbox.
- Tax, dunning, invoice templating configuration on the provider side.
- Advanced usage-metering UI (UC2 ships a minimal usage panel; rich dashboards are later).
- Real admin auth model beyond what exists — reuse the existing Administrators role; full role/claim expansion is later.
- Persistent everything — the userId ↔ customer mapping can start stateless (idempotent on the user reference); adding the Subscription entity + migration upgrades this to a durable cache.
- *Note:* making the target server configurable (§2.3) is **in** scope; standing up, invoking, investigating, or maintaining a mock server is **not** — the implementer's only obligation is that `Maxio:BaseUrl` is honored when set.

---


## 8. Decisions

- **Scope for the live demo:**  all five.
- **Host surface:** Hero UC1 is simplest in the storefront; admin/programmatic flows want the PublicApi.
- **Persistence:** stateless mapping (idempotent on the user reference).
- **Customer identity reference:** email/username (`User.Identity.Name`).
- **Eventing depth:** in-process MediatR notifications only. No durable or cross-process delivery is required for the demo; a broker + outbox would be new infrastructure and is out of scope (§7).
- **UC2 trigger:** an automatic *one order placed → one unit* MediatR hook on `OrderService.CreateOrderAsync`.
- **Target server per environment:** the outbound base URL is configurable via `Maxio:BaseUrl` (§2.3/§5). Defaults: both hosts leave `Maxio:BaseUrl` empty in Development, so the subdomain-derived sandbox host (`apimatic-hackathon`) is used. Each run profile supplies its value via `appsettings.{Environment}.json` or the `Maxio__BaseUrl` environment variable — never a code change. If a mock server is ever pointed at, its URL is supplied the same way (e.g. `http://localhost:8080`) — but invoking or investigating a mock server is not part of this plan's work; the deliverable is only that the knob exists and is honored.
- **Second plan for UC3:** `basic-plan` (id 7111478, $29/mo) seeded alongside `eshop-pro` in family `eshop-subscribe`.
- **Metered component for UC2:** `api-call` (id 3033795) seeded on family `eshop-subscribe`, type Metered, $0.01/unit. Component is on the family so it's automatically available to every subscription on either plan — no per-subscribe attach step needed.

**Domain:** B2C e-commerce + recurring billing — eShopOnWeb gains subscriptions, Maxio runs the billing, and eShopOnWeb's existing layered architecture + in-process mediator keep the customer's account in sync.

---

## Configurable target server requirement

| Location | What changed |
|----------|--------------|
| Revision note (top) | New banner summarizing the change and pointing to the affected sections. |
| §2.2 (layering) | Noted that base-URL resolution lives in the single Infrastructure client, so retargeting never leaks outward. |
| **§2.3 (config conventions)** | **Primary statement of the requirement:** renamed the heading to include "target server is configurable"; added the `Maxio:BaseUrl` override, the explicit resolution order (override wins, else subdomain-derived), the prod/dev/mock intent, the per-host/per-profile override note, and the caution that the client must not ignore the override. Clarified `Maxio:Environment` = region, not deployment target. |
| §4.1 (files) | Annotated `MaxioBillingClient.cs` and `MaxioSettings.cs` to note base-URL resolution / the `BaseUrl` override. |
| §4.2 (responsibilities) | `IBillingClient`/`MaxioBillingClient` row now states it resolves the outbound base URL from `MaxioSettings`. |
| **§4.3 (wiring)** | **Mechanism:** the `AddHttpClient` snippet now sets `BaseAddress` from `MaxioSettings.ResolveBaseUrl()` with the "override ?? derived" rule and a "do NOT hardcode the host" note. |
| §6 (implementation plan) | Implementing the use cases above is the main objective, plus the configurable target server. The mock server is a configuration target only — the implementer must not invoke or investigate a mock server as part of this plan. |
| §7 (out of scope) | Clarified configurability is in scope; standing up, invoking, investigating, or maintaining a mock server is not. |