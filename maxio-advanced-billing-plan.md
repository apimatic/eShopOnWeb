# Maxio Advanced Billing integration plan — eShopOnWeb subscription billing

Adds recurring-subscription billing to eShopOnWeb as an **additive, parallel** capability,
with Maxio Advanced Billing as the system of record. Exposed as three JWT-authenticated
HTTP endpoints on `src/PublicApi`. Does not touch the existing Catalog→Basket→Order flow.

## 1. Scope & sequence

Layering follows eShopOnWeb: domain contract + DTOs in **ApplicationCore**, external-service
implementation in **Infrastructure**, HTTP surface in **PublicApi**. The Maxio .NET SDK is
vendored as a source project (it is not on NuGet) and referenced by Infrastructure only.

1. **Vendor the SDK** — copy the SDK source into `src/MaxioAdvancedBilling.Sdk/` (exclude
   `map/`, `api-reference.md`, `.git`), opt it out of central package management, add it to
   the solution, reference it from Infrastructure. (No SDK operation — build plumbing.)
2. **ApplicationCore contract** — `ISubscriptionBillingService` + plain domain DTOs
   (`SubscriptionPlan`, `SubscriptionDetails`, `SubscribeRequest`) + `BillingException`.
   No SDK types leak past Infrastructure.
3. **Infrastructure — `MaxioSettings`** bound from the `Maxio:` section (keys below) with
   startup fail-fast validation.
4. **Infrastructure — client registration** — `AddMaxioBilling(IServiceCollection, IConfiguration)`:
   binds+validates settings, registers the `MaxioAdvancedBillingClient` via `IHttpClientFactory`,
   registers `ISubscriptionBillingService → MaxioBillingService`, and a keyed-lock registry.
5. **Infrastructure — `MaxioBillingService`** implements the three flows:
   - **Plans**: `ListProductsForProductFamily(ProductFamilyHandle, …)` → map to `SubscriptionPlan`.
   - **Subscribe (hero)**: ensure customer (idempotent) → ensure subscription (idempotent) → map.
   - **My subscriptions**: `ReadCustomerByReference` → `ListCustomerSubscriptions(customerId)` → map.
6. **PublicApi endpoints** (MinimalApi.Endpoint `IEndpoint` style, all `[Authorize]` JWT):
   - `GET  /api/subscription-plans`
   - `POST /api/subscriptions`
   - `GET  /api/my-subscriptions`
   Identity = JWT `ClaimTypes.Name` (email) → used as the Maxio customer `reference`.
7. **ExceptionMiddleware** — map `BillingException` to a clean HTTP status.
8. **Tests** — unit-test `MaxioBillingService` against a faked `HttpClient` transport; add a
   PublicApi integration smoke test for the plans endpoint.

A capability the map lacks is a Blocker (§6) — none found; all flows map to real operations.

## 2. CONTRACT SHEET

> ⚠ **Signatures below are generated code, copied verbatim. Every parameter name is the literal
> C# identifier** — in named arguments use exactly these names; the cancellation-token parameter
> is named `ct`, so named calls write `ct:`.
> ⚠ **Every SDK type is written fully-qualified with the namespace its source path implies**,
> taken from the path the map gives for THAT type (`Models/` → `MaxioAdvancedBilling.Models`,
> `Models/Enums/` → `MaxioAdvancedBilling.Models.Enums`, `Errors/` → `MaxioAdvancedBilling.Errors`,
> root client/options → `MaxioAdvancedBilling`, `ServerEnvironment` → `MaxioAdvancedBilling.Servers`).

### Operations

| # | Accessor · Signature | Request model & fields used | Response envelope → fields read | Error case + accessors | Pagination | Source |
|---|---|---|---|---|---|---|
| 1 | `client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, MaxioAdvancedBilling.Models.ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `productFamilyId` = `MaxioSettings.ProductFamilyHandle` (handle accepted in place of id). Pass all middle params `null`; `includeArchived: false`. | returns `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>`; each `.Product` (`MaxioAdvancedBilling.Models.Product`) → `Id`, `Name`, `Handle`, `Description`, `PriceInCents`, `Interval`, `IntervalUnit` (.Value), `ProductPricePointHandle` | **Case A** `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | page-based (`page`/`perPage`); one family's plans fit one page (perPage=200 to be safe) | map/operations/ProductFamilies.md · Models/ProductResponse.cs · Models/Product.cs |
| 2 | `client.Customers.ReadCustomerByReference(string reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `reference` = eShop user email (from JWT) | returns `MaxioAdvancedBilling.Models.CustomerResponse`; `.Customer` (`MaxioAdvancedBilling.Models.Customer`, **required**) → `Id`, `Reference`, `Email` | **Case B** `SdkException<RawError>`; a **404** here means "no such customer" → treat as not-found, not an error | none | map/operations/Customers.md · Models/CustomerResponse.cs · Models/Customer.cs |
| 3 | `client.Customers.CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `body` must be passed | `CreateCustomerRequest { Customer = CreateCustomer }` (both **required**). `CreateCustomer` **required**: `FirstName`, `LastName`, `Email`; set `Reference` = email. | returns `MaxioAdvancedBilling.Models.CustomerResponse`; `.Customer.Id` | **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`: `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | map/operations/Customers.md · Models/CreateCustomerRequest.cs · Models/CreateCustomer.cs |
| 4 | `client.Subscriptions.FindSubscription(string? reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `reference` must be passed | `reference` = deterministic per (user,plan): `eshop:{email}:{planHandle}` | returns `MaxioAdvancedBilling.Models.SubscriptionResponse`; `.Subscription` (nullable) | **Case A** `SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback] — 404 = no existing subscription | none | map/operations/Subscriptions.md · Models/SubscriptionResponse.cs |
| 5 | `client.Subscriptions.CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `body` must be passed | `CreateSubscriptionRequest { Subscription = CreateSubscription }` (**required**). `CreateSubscription` has **no required fields**; set `ProductHandle` (chosen plan), `CustomerId` (from #2/#3), `Reference` (as in #4). Leave all payment-profile fields null (plan requires no payment method). | returns `MaxioAdvancedBilling.Models.SubscriptionResponse`; `.Subscription` (`MaxioAdvancedBilling.Models.Subscription`) → `Id`, `State` (.Value), `ProductPriceInCents`, `CurrentPeriodEndsAt`, `NextAssessmentAt`, `Reference`, `.Product.Handle/.Name` | **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`: `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | map/operations/Subscriptions.md · Models/CreateSubscriptionRequest.cs · Models/CreateSubscription.cs · Models/Subscription.cs |
| 6 | `client.Customers.ListCustomerSubscriptions(int customerId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `customerId` from #2 | returns `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>`; each `.Subscription` → same fields as #5 | **Case B** `SdkException<RawError>` | none | map/operations/Customers.md |

⚠ #5: `CreateSubscription` marks **nothing** `required`, so `required?` selects nothing. The endpoint's
own prose ties acceptance to providing a product (`product_handle` OR `product_id`) **and** a customer
(`customer_id` OR `customer_reference` OR `customer_attributes`). Decision: send `ProductHandle` +
`CustomerId`. Deliberately omitted: all payment-profile / credit-card / bank fields (plan = payment
method not required), `next_billing_at`/`initial_billing_at` (want immediate activation), coupons, offers.

### Enums used (read `.Value` for the wire string; build via static members / `FromValue`)

| Enum | Members (C# → wire) | Source |
|---|---|---|
| `MaxioAdvancedBilling.Models.Enums.SubscriptionState` | `Active`→`active`, `Trialing`→`trialing`, `Pending`→`pending`, `Canceled`→`canceled`, `PastDue`→`past_due`, … (15 members) | Models/Enums/SubscriptionState.cs |
| `MaxioAdvancedBilling.Models.Enums.IntervalUnit` | `Day`→`day`, `Month`→`month` | Models/Enums/IntervalUnit.cs |

### Client construction / auth / server

- **Client**: `new MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)` — only constructor. DI helper `services.AddMaxioAdvancedBillingClient(Action<…Options>?)` wires `IHttpClientFactory`-created `HttpClient` + singleton client, and defaults `Logging.LoggerFactory` to the app's `ILoggerFactory`. Source: `MaxioAdvancedBillingClient.cs`, `ServiceCollectionExtensions.cs`.
- **Auth (US/EU use Basic)**: `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.BasicAuthCredentials { Username = <API key>, Password = "x" }` — Maxio's convention: username = Chargify API key, password = `x`. Source: sdk-map.md *Servers & auth*. (Exact namespace of `BasicAuthCredentials` to confirm from `AuthSchemes.cs` at implementation — see trap note.)
- **Environment**: `options.Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (MAXIO_ENVIRONMENT=`US`). `ServerEnvironment` source: `Servers/ServerEnvironment.cs`.
- **Subdomain**: `options.Server.Production.Us.Site = <subdomain>` → resolves `https://{site}.chargify.com`. Source: `Servers/ProductionOptions.cs`.
- **BaseUrl override**: when `Maxio:BaseUrl` is non-empty, set `options.Server.Production.Us.BaseUrl = <BaseUrl>` verbatim (used as-is; no `{site}` substitution needed). Source: `Servers/ProductionOptions.cs`.

### Configuration keys (bind from `Maxio:` section — values never in repo; loaded via user-secrets)

| Key | From env var | Required | Use |
|---|---|---|---|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | yes | Basic-auth username |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | yes | `Server.Production.Us.Site` |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | yes | `ListProductsForProductFamily` id + plan validation |
| `Maxio:BaseUrl` | — (optional override) | no | verbatim base URL when set |

## 3. Trap notes (one per hazard; resolved only by loading the named skill)

- **Client & DI (step 4)** — the `HttpClient`/handler pipeline must be long-lived and reused, not rebuilt per request, and getting the client lifetime vs. HttpClient lifetime wrong is invisible at compile time. **MUST load `maxio-platforms-team:dotnet-client-initialization`.**
- **Auth (step 4)** — a credential that is never set is *skipped*, not an error, so a misconfigured Basic credential can surface as an unauthenticated call rather than a 401; and the exact `BasicAuthCredentials` type/namespace is a shape fact. **MUST load `maxio-platforms-team:dotnet-authentication`.**
- **Calling endpoints (steps 5)** — `ListProductsForProductFamily` / `ListSubscriptions` have many nullable-no-default params that mis-bind in a positional call, and whether a write carries a real idempotency key (it does not — the injected `Idempotency-Key` header is not one) governs the whole idempotency design. **MUST load `maxio-platforms-team:dotnet-calling-endpoints`.**
- **Models (steps 5)** — enums are `StringEnum<T>`, not C# enums (read `.Value`, build via static members), and response models carry an extension-data bag; constructing/mapping these wrong compiles but misbehaves. **MUST load `maxio-platforms-team:dotnet-models`.**
- **Error handling (all SDK calls)** — operations are split Case A (typed `{Operation}Error`) vs Case B (`RawError`), and the exception families do not overlap the way a naive catch ladder assumes. **MUST load `maxio-platforms-team:dotnet-error-handling`.**
- **Config & resilience (step 4)** — `Timeout` is per-attempt not total, `POST` is never resent by the SDK while `GET` is, and `LogRequestBody` logs JSON bodies unredacted; each of these changes how the integration must bound and observe calls. **MUST load `maxio-platforms-team:dotnet-configuration-resilience`.**
- **Testing (step 8)** — the SDK's test seam is the `HttpClient` constructor argument, not any SDK internal. **MUST load `maxio-platforms-team:dotnet-testing`.**

## 4. REQUIRED READING (load all before implementation; this sheet does not carry their contents)

| Skill (load the copy shipped by **maxio-platforms-team**) | Governs |
|---|---|
| `maxio-platforms-team:dotnet-client-initialization` | client construction + DI registration (step 4) |
| `maxio-platforms-team:dotnet-authentication` | Basic-auth credential wiring (step 4) |
| `maxio-platforms-team:dotnet-calling-endpoints` | every SDK operation call (steps 5–6) |
| `maxio-platforms-team:dotnet-models` | building requests / mapping responses (steps 5–6) |
| `maxio-platforms-team:dotnet-error-handling` | error boundary around every SDK call (always) |
| `maxio-platforms-team:dotnet-configuration-resilience` | retries, timeouts, base-URL, logging (step 4) |
| `maxio-platforms-team:dotnet-testing` | faking the SDK in tests (step 8) |

**Mandatory `System.Text.Json.JsonException` hazard rows** (it reaches the error boundary from two
directions, needing opposite handling):
- A drifted/malformed **2xx** body (e.g. a missing `required` member) surfaces as a `JsonException`
  from deserialization — **not** an `SdkException` — so an SDK-exception-only catch ladder lets it escape.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so it **replaces** the `SdkException`
  and the HTTP status is destroyed with it.

## 5. PRODUCTION READINESS

| # | Concern | Decision |
|---|---|---|
| 1 | **Credential fail-fast** | `MaxioSettings` bound from `Maxio:` and validated with `ValidateOnStart()`: `ApiKey`, `Subdomain`, `ProductFamilyHandle` must each be present **and non-blank** (blank ≠ missing); `BaseUrl` optional. Host refuses to start otherwise — no first-call 401 in prod. |
| 2 | **Secret sourcing & rotation** | Secrets come from **.NET user-secrets** (`Maxio:ApiKey` …), loaded by `CreateBuilder` in Development; never written to any repo file. Options object is built **once at registration** and captured in the singleton client, so a rotated key takes effect only after process restart. Acceptable for this sandbox demo; documented as the restart requirement. |
| 3 | **Total timeout budget** | SDK `Timeout` is **per attempt**; the only thing that bounds a whole call is a `CancellationToken` deadline. `MaxioBillingService` wraps each public operation in a linked CTS with a total budget (30s) combined with the request's `ct`. Confirm retry/timeout interaction — MUST load `dotnet-configuration-resilience`. |
| 4 | **Write-retry ownership** | Default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS, so the two writes in scope (`CreateCustomer`, `CreateSubscription`, both POST) are **never resent by the SDK** — good, avoids silent duplicate charges. Reads (`List*`, `Read*`, `FindSubscription` are GET) may be retried safely. No retry override needed. |
| 5 | **Idempotency & ambiguous writes** | Neither write exposes a real caller-supplied idempotency key (the generator's `Idempotency-Key: Guid.NewGuid()` header is per-call and deduplicates nothing). **Reconciliation path instead:** (a) **customer** — deterministic `reference = email`; read-before-create via `ReadCustomerByReference`; on create 422 "reference taken" re-read; (b) **subscription** — deterministic `reference = eshop:{email}:{planHandle}`; `FindSubscription(reference)` before create, return existing if found. Both writes serialized per-identity by an in-process `SemaphoreSlim` registry so a double-click cannot race create→create (single-process app; in-memory DB). |
| 6 | **Observability** | Info log on each operation (operation name + masked user reference + plan handle + resulting subscription id/state). On SDK error, log the HTTP status and the provider error body text (via `RawError.ReadAsString()` / typed accessor) as the correlation signal. `LogRequestBody` stays **off**. |
| 7 | **Sensitive data** | In-scope request model `CreateCustomer` carries **email** (personal data) but **no card/bank data** (plans require no payment method; all payment-profile fields left null). Posture: `LogRequestBody` stays off **and** `Logging.LoggerFactory` is assigned explicitly (the DI helper defaults it to the app `ILoggerFactory`) so the `MAXIOADVANCEDBILLINGCLIENT_LOG` env var cannot switch body logging on from outside the code; our own logs never echo request bodies, and email is masked in logs. |
| 8 | **Environment selection** | One environment in play: `ServerEnvironment.Us` → `Production` group → `https://{subdomain}.chargify.com` (subdomain from config). `Ebb`/`Oauth` groups are not touched by any in-scope operation. The SDK has **no dedicated "sandbox" environment**; test/dev traffic is isolated from live purely by pointing `Subdomain` (and optionally `BaseUrl`) at the sandbox site — enforced by configuration, never hard-coded. Each deployment sets its own `Maxio:Subdomain`/`Maxio:ApiKey`. |

## 6. Assumptions & Blockers

- **Assumption (minor):** The three endpoints require an authenticated user (JWT); plan browsing is
  also gated, matching "a logged-in shopper browses available plans." Proceed.
- **Assumption (minor):** `POST /api/subscriptions` accepts an optional `planHandle` in the body; when
  omitted it resolves to `Maxio:DefaultPlanHandle` (optional config, not one of the four mandated keys)
  and, failing that, the first active plan in the family. The chosen handle is validated against the
  family before subscribing. No plan handle is hard-coded. Proceed.
- **Assumption (minor):** The eShop user's email (JWT `ClaimTypes.Name`) is a stable identity suitable
  as the Maxio customer `reference`. The seeded `demouser@microsoft.com` is stable across restarts even
  under the in-memory DB. Proceed.
- **To confirm at implementation (not a blocker):** exact namespace of `BasicAuthCredentials`
  (`Core/...` — read from `AuthSchemes.cs`), and whether `RequestOptions` sits in root or `Core`.
  These are shape facts resolved by a one-file open + `dotnet-*` companion skills, not gaps in the plan.
- **No Blockers:** every flow maps to a real SDK operation; no invented capability.
