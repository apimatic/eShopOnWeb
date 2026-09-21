# Maxio Advanced Billing — integration plan (eShopOnWeb subscription billing)

Additive, parallel capability: recurring-subscription billing with Maxio Advanced Billing as
system of record. Three JWT-authenticated endpoints on `src/PublicApi`, caller identity from
the token. Maxio is the source of truth (no local subscription persistence — the in-memory DB
caveat makes local mapping unreliable; the durable user↔customer link is the Maxio customer
`reference`, set to the caller's identity).

## 1. Scope & sequence

Layering follows the repo's Clean Architecture: abstraction + provider-agnostic DTOs in
`ApplicationCore`; Maxio client wiring + implementation in `Infrastructure`; endpoints in
`PublicApi`.

1. **Config + client wiring** (`Infrastructure/Maxio`): bind `MaxioSettings` from `Maxio:`
   section; fail-fast validation; register `MaxioAdvancedBillingClient` via
   `AddMaxioAdvancedBillingClient`. Uses no operations.
2. **`GET /api/subscription-plans`** → `ProductFamilies.ListProductsForProductFamily(handle,…)`
   — list plans in the configured family, map `Product` → `SubscriptionPlan`.
3. **`POST /api/subscriptions`** (hero flow) →
   a. `Customers.ReadCustomerByReference(reference=userId)` — find existing (Case B 404 = not found).
   b. if absent, `Customers.CreateCustomer` with `reference=userId` (idempotent; on 422 race, re-read).
   c. `Customers.ListCustomerSubscriptions(customerId)` — if a live subscription to the requested
      product handle already exists, return it (double-click idempotency, enforced against Maxio).
   d. else `Subscriptions.CreateSubscription` with `product_handle` + `customer_id` +
      deterministic `reference`. Map `Subscription` → `CustomerSubscription`.
4. **`GET /api/my-subscriptions`** → `ReadCustomerByReference` (404 ⇒ empty), then
   `Customers.ListCustomerSubscriptions(customerId)` → map to `CustomerSubscription[]`.

No capability is missing from the map; no Blockers.

## 2. CONTRACT SHEET

> ⚠ Signatures below are **generated code, verbatim** — every parameter name is the literal C#
> identifier; named arguments must use them exactly (the cancellation-token parameter is `ct`,
> so it is written `ct:`).
> ⚠ Every SDK type is written **fully-qualified with the namespace its source path implies**,
> taken from the path the map gives for THAT type (e.g. `Models/` ⇒ `MaxioAdvancedBilling.Models`,
> `Models/Enums/` ⇒ `MaxioAdvancedBilling.Models.Enums`, `Errors/` ⇒ `MaxioAdvancedBilling.Errors`,
> `Core/Authentication/Basic/` ⇒ `MaxioAdvancedBilling.Core.Authentication.Basic`,
> `Servers/` ⇒ `MaxioAdvancedBilling.Servers`).

### Operations

| # | op | signature (verbatim) | request model + fields used | response envelope → inner fields read | error case | source |
|---|----|----------------------|-----------------------------|----------------------------------------|-----------|--------|
| 1 | `client.ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `productFamilyId` = **`handle:` + `Maxio:ProductFamilyHandle`** (the path param takes a numeric id OR a handle prefixed with `handle:` — per Api/ProductFamilies.cs `<param>` doc; passing the bare handle returns 404); all filters `null`; `includeArchived: false`; page through with `page`/`perPage` | `IReadOnlyList<Models.ProductResponse>` → `.Product` (`Models.Product`): `Id`, `Name`, `Handle`, `PriceInCents`, `Interval`, `IntervalUnit`, `ArchivedAt`, `ProductPricePointHandle`, `ProductFamily` | **Case A** `SdkException<Errors.ListProductsForProductFamilyError>`; `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback]. ⚠ VERIFIED live: a 404 body is empty, so the Case-A error deserialization throws `JsonException` (not `SdkException`) — the boundary treats it as Unexpected. | map/operations/ProductFamilies.md; Models/ProductResponse.cs; Models/Product.cs; Api/ProductFamilies.cs (param doc) |
| 2 | `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `reference` = caller identity (userId) | `Models.CustomerResponse` → `.Customer` (`Models.Customer`): `Id`, `Reference`, `Email` | **Case B** `SdkException<RawError>` — **404 = not-found sentinel**, read `StatusCode` | map/operations/Customers.md; Models/CustomerResponse.cs; Models/Customer.cs |
| 3 | `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `body` must pass explicitly | `Models.CreateCustomerRequest{ Customer=Models.CreateCustomer }`; `CreateCustomer`: `FirstName`(req), `LastName`(req), `Email`(req), `Reference`(opt, **purpose: idempotency/user link — set to userId**) | `Models.CustomerResponse` → `.Customer.Id` | **Case A** `SdkException<Errors.CreateCustomerError>`; `TryGetCustomerErrorResponse1(out Models.CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | map/operations/Customers.md; Models/CreateCustomerRequest.cs; Models/CreateCustomer.cs |
| 4 | `client.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `customerId` from op 2/3 | `IReadOnlyList<Models.SubscriptionResponse>` → each `.Subscription` (`Models.Subscription`) | **Case B** `SdkException<RawError>` | map/operations/Customers.md; Models/SubscriptionResponse.cs; Models/Subscription.cs |
| 5 | `client.Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `body` must pass explicitly | `Models.CreateSubscriptionRequest{ Subscription=Models.CreateSubscription }`; `CreateSubscription`: `ProductHandle`(opt, **purpose: which plan — set to requested plan handle**), `CustomerId`(opt, **purpose: existing customer — set to op 2/3 id**), `Reference`(opt, **purpose: deterministic subscription key for traceability**). All other fields **omit → provider default** (no price-point handle ⇒ default price point; no payment profile ⇒ none, plans require no payment method). | `Models.SubscriptionResponse` → `.Subscription` (`Models.Subscription`): `Id`, `State`, `CurrentPeriodEndsAt`, `NextAssessmentAt`, `ProductPriceInCents`, `Currency`, `Reference`, `Product` | **Case A** `SdkException<Errors.CreateSubscriptionError>`; `TryGetErrorListResponse1(out Models.ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | map/operations/Subscriptions.md; Models/CreateSubscriptionRequest.cs; Models/CreateSubscription.cs; Models/Subscription.cs |

### Response/inner models read (shapes)

- `Models.Product`: `Id int?`, `Name string?`, `Handle string?`, `PriceInCents long?` (`price_in_cents`),
  `Interval int?`, `IntervalUnit MaxioAdvancedBilling.Models.Enums.IntervalUnit?`,
  `ArchivedAt DateTimeOffset?` (`archived_at`), `ProductPricePointHandle string?`, `ProductFamily Models.ProductFamily?`.
- `Models.Subscription`: `Id int?`, `State MaxioAdvancedBilling.Models.Enums.SubscriptionState?`,
  `CurrentPeriodEndsAt DateTimeOffset?` (`current_period_ends_at`),
  `NextAssessmentAt DateTimeOffset?` (`next_assessment_at`, the next-billing date),
  `ProductPriceInCents long?` (`product_price_in_cents`), `Currency string?`, `Reference string?`,
  `Product Models.Product?`.
- `Models.Customer`: `Id int?`, `Reference string?`, `Email string?`.
- `Models.ErrorListResponse1`: `Errors IReadOnlyList<string>` (required) — used to surface 422 messages.
- `Models.CustomerErrorResponse1`: `Errors MaxioAdvancedBilling.Models.AnyOf.Errors1?` (union
  `CustomerError | IReadOnlyList<string>`; read via `TryGetCustomerError` / `TryGetListOfString`).

### Enums (StringEnum — read wire value via `.Value`; build via static members / `FromValue`)

| enum | namespace | wire values used | source |
|------|-----------|------------------|--------|
| `SubscriptionState` | `MaxioAdvancedBilling.Models.Enums` | `active`, `trialing`, `assessing`, `pending`, `soft_failure`, `past_due`, `on_hold`, `paused`, `awaiting_signup` treated as **live**; `canceled`, `expired`, `unpaid`, `suspended`, `trial_ended`, `failed_to_create` treated as **terminal** | Models/Enums/SubscriptionState.cs |
| `IntervalUnit` | `MaxioAdvancedBilling.Models.Enums` | `day`, `month` | Models/Enums/IntervalUnit.cs |

### Client construction / auth / server node

- Construct via DI: `services.AddMaxioAdvancedBillingClient(options => { … })` (source: ServiceCollectionExtensions.cs).
- Auth: **Basic** — `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }`. (Basic works only with US/EU envs, which connect to chargify.com directly.)
- Environment: `options.Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (subdomain-hosted chargify.com; sandbox sites live here). — source: sdk-map.md Servers & auth.
- Base URL: if `Maxio:BaseUrl` set → `options.Server.Production.Us.BaseUrl = <BaseUrl>` **verbatim**;
  else → `options.Server.Production.Us.Site = <Maxio:Subdomain>` (template `https://{site}.chargify.com`).
  (`ProductionOptions.UsOptions { BaseUrl, Site }` — source: Servers/ProductionOptions.cs.)

### CROSS-OPERATION INVARIANTS

| invariant | operations | enforced where |
|-----------|-----------|----------------|
| `product_handle` supplied to CreateSubscription must be one returned by ListProductsForProductFamily (a plan in the configured family) | `Subscriptions.CreateSubscription` ← `ProductFamilies.ListProductsForProductFamily` | implementation — validate requested handle ∈ listed plan handles; else 400 before calling |
| `customer_id` supplied to CreateSubscription must be one returned by ReadCustomerByReference/CreateCustomer for this caller | `Subscriptions.CreateSubscription` ← `Customers.ReadCustomerByReference`/`CreateCustomer` | implementation — id comes only from the ensure-customer step |
| existing live subscription to the same product ⇒ do not create a second | `Subscriptions.CreateSubscription` ← `Customers.ListCustomerSubscriptions` | implementation — idempotency guard against Maxio state |

## 3. Trap notes

- Client/HttpClient lifetime + DI registration shape (long-lived handler pipeline, not per-request). `MUST load dotnet-client-initialization`.
- Setting credentials before/at construction and sourcing the secret from config not code. `MUST load dotnet-authentication`.
- List/filter ops have many nullable params with **no C# default** → positional mis-binding; call with named arguments. `MUST load dotnet-calling-endpoints`.
- `StringEnum<T>` is not a C# enum; unions use factory + `TryGet…`; response models keep unknown fields in an extension bag. `MUST load dotnet-models`.
- Which exceptions actually reach the boundary, Case A vs Case B accessors, and the two `JsonException` directions (see REQUIRED READING). `MUST load dotnet-error-handling`.
- `Timeout` is per-attempt not total; `HttpMethodsToRetry` default excludes POST; base-URL override point; unredacted body logging. `MUST load dotnet-configuration-resilience`.
- The `HttpClient` ctor arg is the test seam; match the project's test framework. `MUST load dotnet-testing`.

## 4. REQUIRED READING (load all before implementation)

- `maxio-platforms-team:dotnet-client-initialization` — step 1 client + DI.
- `maxio-platforms-team:dotnet-authentication` — step 1 credentials.
- `maxio-platforms-team:dotnet-calling-endpoints` — steps 2–4 operation calls.
- `maxio-platforms-team:dotnet-models` — request bodies + enum/union reads.
- `maxio-platforms-team:dotnet-error-handling` — error boundary (always required).
- `maxio-platforms-team:dotnet-configuration-resilience` — retries/timeout/base-URL/logging.
- `maxio-platforms-team:dotnet-testing` — integration-layer tests.

These carry the how-to; this sheet deliberately does not restate their contents.

**Mandatory `JsonException` hazard rows (both directions reach the boundary):**
- A drifted/malformed **2xx** body (a missing `required` member) surfaces as
  `System.Text.Json.JsonException` from deserialization — **not** an `SdkException`; an
  SDK-exception-only catch ladder lets it escape.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` **while the error object is being constructed**, **replacing** the `SdkException`
  and destroying the HTTP status.

## 5. PRODUCTION READINESS

| # | Concern | Decision |
|---|---------|----------|
| 1 | Credential fail-fast | `MaxioSettings` validated at startup (`ValidateOnStart`): `ApiKey`, `Subdomain`, `ProductFamilyHandle` each must be non-null/non-blank (each part checked separately); `BaseUrl` optional. Host refuses to start otherwise — no first-call 401 in prod. |
| 2 | Secret sourcing & rotation | Secret from configuration (`Maxio:ApiKey`), sourced from .NET user-secrets (loaded from env vars out-of-band; never in repo). `AddMaxioAdvancedBillingClient` builds options **once at registration** and captures them in the singleton client → a rotated key takes effect only on process restart. Rotation-without-restart not required for this sandbox integration. |
| 3 | Total timeout budget | SDK `Timeout` is **per attempt**; the only whole-call bound is a `CancellationToken` deadline. Each service call is bounded by a linked CTS deadline (default 30s) combined with the request's `ct`, enforced in the service. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS ⇒ SDK never resends our two writes (`CreateCustomer`, `CreateSubscription`, both POST). Reads (GET) may be retried by the SDK — safe (idempotent). We keep SDK retry defaults for GETs; writes are guarded by our own idempotency (row 5). |
| 5 | Idempotency & ambiguous writes | No real caller-supplied idempotency key exists on either write (the injected `Idempotency-Key: Guid.NewGuid()` header is **not** a key). Reconciliation path instead: (a) customer keyed by `reference=userId` — create guarded by read-by-reference + 422 re-read on race; (b) subscription guarded by `ListCustomerSubscriptions` live-state check before create + deterministic `reference`. A create that races is reconciled by re-reading Maxio (source of truth), never duplicated locally. |
| 6 | Observability | Structured logs via the repo's `IAppLogger<T>`: info on ensure-customer / subscribe outcomes (customerId, subscriptionId, state); warning on provider 4xx with the surfaced message; error on unexpected. `LogRequestBody` left **off** (SDK default). No provider correlation id is exposed on Case A/B error bodies in scope; we log HTTP status + returned `errors[]` messages. |
| 7 | Sensitive data | Scope carries **no** card/bank/PII beyond the caller's email + name (already in our identity store). No payment-profile fields are sent (plans need no payment method). `LogRequestBody` stays off and `LoggerFactory` is left to DI (never trace-forced in prod config); request bodies are never echoed by our own diagnostics. |
| 8 | Environment selection | Basic auth ⇒ `ServerEnvironment.Us` (`https://{site}.chargify.com`) or explicit `Maxio:BaseUrl`. The sandbox is a normal chargify.com site addressed by the configured `Maxio:Subdomain` (or `Maxio:BaseUrl`); all dev/test traffic targets that site. No live system involved; the SDK has no separate "sandbox" env — isolation is by subdomain/BaseUrl pointing only at the sandbox site. |

## 6. Assumptions & Blockers

- **Caller identity**: the PublicApi JWT carries `ClaimTypes.Name` (the eShop username, which is the
  user's email in the seeded store) and roles — no separate GUID. We use the name claim as the
  Maxio customer `reference` (stable per user) and as the email; first/last name derived from it.
  No Blocker.
- **Plan selection**: `POST /api/subscriptions` accepts an optional `planHandle`; when omitted it
  defaults to the configured default (`eshop-pro` per task, but not hard-coded — falls back to the
  first live plan returned by ListProductsForProductFamily). No Blocker.
- No capability gaps in the plugin; no Blockers.
