# Maxio Advanced Billing integration plan — eShopOnWeb

Additive, parallel **recurring-subscription billing** for eShopOnWeb. Maxio Advanced Billing is
the system of record. Three JWT-authenticated endpoints on `src/PublicApi`. The caller's
identity is the JWT `ClaimTypes.Name` (username), which is the stable eShop user key we use as
the Maxio **customer reference** and the local claim key.

## 1. Scope & sequence

| # | Step | Maxio operations used |
| --- | --- | --- |
| 1 | Vendor SDK source under `src/MaxioAdvancedBilling.Sdk/` (not on NuGet — build from source), ProjectReference from Infrastructure. Disable central-package-management in the vendored csproj. | — |
| 2 | `MaxioSettings` (Infrastructure) bound from `Maxio:` section; fail-fast validation of every part. | — |
| 3 | DI: register `MaxioAdvancedBillingClient` singleton (US env, BasicAuth, subdomain/BaseUrl, retry+timeout). | — |
| 4 | Local claim/ledger entities in `CatalogContext`: `MaxioCustomerLink` (unique `BuyerId`), `MaxioSubscriptionRecord` (unique `BuyerId+PlanHandle`). | — |
| 5 | `ISubscriptionBillingService` (ApplicationCore) + `MaxioSubscriptionBillingService` (Infrastructure). | see below |
| 5a | List plans in the configured family. | `ProductFamilies.ListProductFamilies` (resolve handle→id) → `ProductFamilies.ListProductsForProductFamily` |
| 5b | Ensure customer (idempotent). | `Customers.ReadCustomerByReference` → `Customers.CreateCustomer` |
| 5c | Subscribe (idempotent). | `Subscriptions.FindSubscription` (reconcile) → `Subscriptions.CreateSubscription` |
| 5d | My subscriptions. | `Customers.ReadCustomerByReference` → `Customers.ListCustomerSubscriptions` |
| 6 | Three endpoints on PublicApi: `GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions`. | — |
| 7 | Placeholder `Maxio:` config in `tests/PublicApiIntegrationTests/appsettings.test.json`; integration tests (auth-required) + service unit tests with faked HttpClient seam. | — |

A metered `api-call` component is seeded but the hero flow (subscribe) does not touch it; out of scope.

## 2. CONTRACT SHEET

⚠ **Signatures below are generated code, verbatim.** Every parameter name is the literal C#
identifier; named arguments must use them exactly (the cancellation-token parameter is `ct`, so
write `ct:`). List/search ops have many nullable-no-default params that **must be passed
explicitly** (`null` to skip) and must be called with named arguments to avoid mis-binding.
⚠ **Every SDK type is written fully-qualified with the namespace its source path implies**
(`Models/` → `MaxioAdvancedBilling.Models`; `Models/Enums/` → `MaxioAdvancedBilling.Models.Enums`;
`Errors/` → `MaxioAdvancedBilling.Errors`; root client/options → `MaxioAdvancedBilling`;
`Core/…` per its file). Taken from each type's own map/source path.

### Operations

| op | signature (verbatim) | request model + fields used | response envelope + fields read | error case | source |
| --- | --- | --- | --- | --- | --- |
| `client.ProductFamilies.ListProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — (all null) | `IReadOnlyList<ProductFamilyResponse>`; each `.ProductFamily` → `Handle`, `Id` | **Case B** `SdkException<RawError>` | map/operations/ProductFamilies.md; Models/ProductFamilyResponse.cs; Models/ProductFamily.cs |
| `client.ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `productFamilyId` = resolved numeric family **id** (string); pass `includeArchived:false`, rest null | `IReadOnlyList<ProductResponse>`; each `.Product` → `Handle`, `Name`, `Description`, `PriceInCents`, `Interval`, `IntervalUnit`, `Id`, `ArchivedAt` | **Case A** `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | map/operations/ProductFamilies.md; Api/ProductFamilies.cs (path `/product_families/{product_family_id}/products.json`); Models/Product.cs |
| `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `reference` = eShop username | `CustomerResponse.Customer` → `Id`, `Reference` | **Case B** `SdkException<RawError>` (404 when absent → `RawError.StatusCode`) | map/operations/Customers.md; Models/CustomerResponse.cs; Models/Customer.cs |
| `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateCustomerRequest{ Customer = CreateCustomer{…} }` — see body below | `CustomerResponse.Customer` → `Id` | **Case A** `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError` [fallback] | map/operations/Customers.md; Models/CreateCustomerRequest.cs; Models/CreateCustomer.cs |
| `client.Subscriptions.FindSubscription` | `FindSubscription(string? reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` (**pass `reference:` explicitly**) | `reference` = deterministic subscription reference | `SubscriptionResponse.Subscription?` (nullable) → all fields below | **Case A** `SdkException<FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError` [fallback] | map/operations/Subscriptions.md; Models/SubscriptionResponse.cs; Models/Subscription.cs |
| `client.Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateSubscriptionRequest{ Subscription = CreateSubscription{…} }` — see body below | `SubscriptionResponse.Subscription?` → `Id`, `State`, `CurrentPeriodEndsAt`, `NextAssessmentAt`, `ProductPriceInCents`, `Reference`, `Product`, `Customer` | **Case A** `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | map/operations/Subscriptions.md; Models/CreateSubscriptionRequest.cs; Models/CreateSubscription.cs |
| `client.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `customerId` | `IReadOnlyList<SubscriptionResponse>`; each `.Subscription?` → fields as above | **Case B** `SdkException<RawError>` | map/operations/Customers.md; Models/SubscriptionResponse.cs |

### Request body — `CreateCustomer` (Models/CreateCustomer.cs)

| field (wire) | type | required? | purpose |
| --- | --- | --- | --- |
| `FirstName` (first_name) | string | **required** | eShop first name; fall back to username-derived value (email local part) — no eShop profile name exists |
| `LastName` (last_name) | string | **required** | eShop last name; fall back to `"eShop User"` |
| `Email` (email) | string | **required** | eShop username (which is the email) |
| `Reference` (reference) | string? | optional | **set** = eShop username → cross-run idempotency key for the customer. omit → provider default (null, no dedup). We must set it. |

All other `CreateCustomer` fields left unset → **omit → provider default**. No card/bank/tax fields
touched (payment method not required on these plans).

### Request body — `CreateSubscription` (Models/CreateSubscription.cs)

| field (wire) | type | required? | purpose |
| --- | --- | --- | --- |
| `ProductHandle` (product_handle) | string? | one-of | **set** = caller's plan handle (recommended over `product_id`; ids not published). Required unless `product_id` given. |
| `CustomerId` (customer_id) | int? | one-of | **set** = ensured Maxio customer id (deterministic, avoids re-creating customer). Required unless `customer_reference`/`customer_attributes` given. |
| `Reference` (reference) | string? | optional | **set** = deterministic `eshop-{username}-{planHandle}` → reconciliation key for `FindSubscription`. omit → provider default (null). We must set it. |

Everything else omitted → **omit → provider default**: no `PaymentCollectionMethod`
(plans are payment-method-not-required; provider default applies), no `CustomerAttributes`/
`CreditCardAttributes`/`PaymentProfile*` (no card capture), no `NextBillingAt`/`InitialBillingAt`
(so the subscription activates immediately, `active` state, `current_period_ends_at` = +1 interval),
no components. `DeferSignup`/`DunningCommunicationDelayEnabled` default `false` in the model.

### Enum tables needed

| enum | members used (C# → wire) | source |
| --- | --- | --- |
| `MaxioAdvancedBilling.Models.Enums.SubscriptionState` | read-only, surfaced as `.Value` string (e.g. `active`, `trialing`, `canceled`) | Models/Enums/SubscriptionState.cs |
| `MaxioAdvancedBilling.Models.Enums.IntervalUnit` | read-only, surfaced as `.Value` (`month`/`day`) | Models/Enums/IntervalUnit.cs |

`StringEnum<T>` types are **not C# enums**: read the wire string via `.Value` (or `ToString()`), never cast. We build none of these on the request side. **MUST load dotnet-models.**

### Client construction / auth / server

- `new MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions)` — only ctor. Source: MaxioAdvancedBillingClient.cs, sdk-map.md.
- Auth: `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }`. Basic works only with US/EU (chargify.com). Source: sdk-map.md *Servers & auth*, MaxioAdvancedBillingClientOptions.cs.
- Environment: `options.Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us` — **YOUR CALL — not in the map** (see §6): the seeded site is on `chargify.com` (US) and Basic auth requires US/EU; US is the sandbox default.
- Server base URL: default template `https://{site}.chargify.com`. Set `options.Server.Production.Us.Site = <Maxio:Subdomain>`. If `Maxio:BaseUrl` is set, use it verbatim: `options.Server.Production.Us.BaseUrl = <Maxio:BaseUrl>` (its lack of `{site}` means the site var is inert). Source: ServerOptions.cs, Servers/ProductionOptions.cs, sdk-map.md.
- DI: prefer registering via `IHttpClientFactory` so the pipeline is long-lived (**MUST load dotnet-client-initialization**); build the SDK options object once at registration.

### CROSS-OPERATION INVARIANTS

| invariant | operations | enforced where |
| --- | --- | --- |
| a `product_handle` accepted by subscribe must be one of the plan handles the family exposes | `Subscriptions.CreateSubscription` ← `ProductFamilies.ListProductsForProductFamily` | implementation (validate handle ∈ family plan handles before subscribe; else 400) |
| the `customer_id` passed to subscribe must be a real Maxio customer for this user | `Subscriptions.CreateSubscription` ← `Customers.ReadCustomerByReference`/`CreateCustomer` | implementation (ensure-customer runs first, id captured) |
| the family id passed to list-products must be one `ListProductFamilies` returns for the configured handle | `ProductFamilies.ListProductsForProductFamily` ← `ProductFamilies.ListProductFamilies` | implementation (resolve handle→id; 404/empty → config error) |

## 3. Trap notes (hazard + skill pointer — not resolved here)

- **Client/HttpClient lifetime**: a per-request `HttpClient` or rebuilt pipeline leaks sockets; the SDK wrapper vs handler lifetime differ. **MUST load dotnet-client-initialization.**
- **Auth skip-vs-fail**: an unset credential is silently skipped and the call still goes out, so a 401 can mean "nothing sent" not "wrong key". **MUST load dotnet-authentication.**
- **List ops positional mis-bind**: `ListProductsForProductFamily`/`ListCustomerSubscriptions`/`FindSubscription` have nullable-no-default params; a positional call mis-binds. **MUST load dotnet-calling-endpoints.**
- **StringEnum, not C# enum; wire vs C# names**: reading `State`/`IntervalUnit` by cast fails; wire names differ from C# names. **MUST load dotnet-models.**
- **Two JsonException directions + Case A/B**: a drifted 2xx body throws `System.Text.Json.JsonException` (not `SdkException`) from deserialization; a non-2xx body not matching `{Operation}Error` throws `JsonException` while building the error, destroying the status. **MUST load dotnet-error-handling.**
- **Timeout is per-attempt; POST never auto-retried; unredacted body logging**: the caller's real budget needs a `CancellationToken` deadline; `LogRequestBody` prints JSON verbatim. **MUST load dotnet-configuration-resilience.**
- **Test seam is the HttpClient ctor arg**: faking anywhere else couples to SDK internals. **MUST load dotnet-testing.**

## 4. REQUIRED READING (load all before implementation; contents deliberately not copied here)

| skill | governs |
| --- | --- |
| maxio-platforms-team:dotnet-client-initialization | client construction + DI singleton via IHttpClientFactory (step 3) |
| maxio-platforms-team:dotnet-authentication | BasicAuth credentials wiring (step 3) |
| maxio-platforms-team:dotnet-calling-endpoints | every `client.*` call, named-argument discipline (step 5) |
| maxio-platforms-team:dotnet-models | reading StringEnum values, building request records, wire names (step 5) |
| maxio-platforms-team:dotnet-error-handling | try/catch boundary in the service (step 5) — **always required** |
| maxio-platforms-team:dotnet-configuration-resilience | retry/timeout/logging tuning at registration (step 3) |
| maxio-platforms-team:dotnet-testing | faking the HttpClient seam (step 7) |

**Two mandatory `JsonException` hazards** (from dotnet-integrate-maxio, verbatim):
a drifted/malformed **2xx** body (a missing `required` member) surfaces as `System.Text.Json.JsonException`
from deserialization, **not** as an `SdkException`, so an SDK-exception-only catch ladder lets it
escape; and a **non-2xx** body that does not match its operation's generated `{Operation}Error`
shape throws `JsonException` *while the error object is being constructed*, so it **replaces** the
`SdkException` and the HTTP status is destroyed with it. Boundary catches both `SdkException<T>`
and `JsonException`.

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `MaxioSettings` validated at DI registration; host throws on startup if `ApiKey`, `Subdomain`, or `ProductFamilyHandle` is missing/blank (each part checked individually). `BaseUrl` is optional. |
| 2 | Secret sourcing & rotation | `Maxio:ApiKey` from .NET user-secrets (loaded from `MAXIO_API_KEY` env; never written to repo). Options object built once at registration and captured in the singleton → a rotated key takes effect only on process restart. Documented; rotation-without-restart not required for this sandbox integration. |
| 3 | Total timeout budget | SDK `RetryOptions.Timeout` is **per attempt**. Each service call passes a `CancellationToken` derived from the request token + a bounded `CancellationTokenSource(TimeSpan)` (30s total) — the only thing bounding the whole (retried) call. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS. Our writes are **POST** (`CreateCustomer`, `CreateSubscription`) → **never auto-resent by the SDK**. Reads (GET) may retry safely. We keep POST out of the retry method set (default already excludes it). |
| 5 | Idempotency & ambiguous writes | Neither `CreateCustomer` nor `CreateSubscription` takes a real caller-supplied idempotency key (no such param on their map rows; the generator-injected `Idempotency-Key` header is per-call GUID and is **not** a key). Reconciliation path instead: customer keyed by `reference`=username (`ReadCustomerByReference` before create); subscription keyed by deterministic `reference`=`eshop-{username}-{planHandle}` (`FindSubscription` before create). |
| 6 | Observability | `IAppLogger<T>` (repo's adapter). Info: plan-list count, customer ensured (id), subscription created (id/state). Warning/Error: Maxio error status + first error message from the typed error body; the request `CorrelationId` (repo convention) is logged. `LogRequestBody` stays **off**. |
| 7 | Sensitive data | Request models in scope (`CreateCustomer`, `CreateSubscription`) carry **no card/bank/PII beyond name+email** (no card fields set). `LogRequestBody` stays off and `options.Logging.LoggerFactory` is set explicitly at registration so `MAXIOADVANCEDBILLINGCLIENT_LOG` cannot switch body logging on externally. We never echo request bodies in our own logs. |
| 8 | Environment selection | One group in scope: `Production` on `ServerEnvironment.Us` → `https://{site}.chargify.com` with `{site}`=subdomain (or `Maxio:BaseUrl` verbatim). Sandbox = a dedicated Maxio site (`cp-exp-1`); there is no separate "sandbox" server enum — test traffic is kept off any live system by pointing `Maxio:Subdomain`/`Maxio:BaseUrl` at the sandbox site only. Deployments set their own subdomain. |
| 9 | Duplicate prevention under concurrency | Store: `CatalogContext`. `MaxioCustomerLink` table, column `BuyerId` carries the customer claim (**unique index**); `MaxioSubscriptionRecord` table, columns `BuyerId`+`PlanHandle` carry the subscription claim (**composite unique index**). Second concurrent insert is rejected by the unique constraint → code catches `DbUpdateException` and reconciles/returns the existing record. (SQL Server enforces the constraint; see §6 note on the in-memory provider on this machine.) |
| 10 | Partial results | `ListProductsForProductFamily` is paged (default perPage 20). The plan-list read walks pages until a short page is returned; the response DTO carries an explicit `Truncated` bool set true only if a hard page cap (defensive max 20 pages / 400 plans) is hit, so the caller learns from the return type, not a log. |
| 11 | Startup validation vs test host | `tests/PublicApiIntegrationTests` boots the host via `WebApplicationFactory<Program>`. It reads `appsettings.test.json`; we add placeholder (non-secret) `Maxio:` values there so fail-fast passes and the host boots. Verified by running `dotnet test` on that project (must be green). |
| 12 | Ordering & no-op side effects | Local claim row is inserted (state `pending`) **before** the Maxio call and updated with the returned id/state **after**. The outbound Maxio `CreateSubscription`/`CreateCustomer` is **gated** on the claim row being newly created — a duplicate claim (already `pending`/`active`) short-circuits to reconcile-and-return, so no second create and no duplicate side effect. |
| 13 | Unknown outcomes | If `CreateSubscription` transport fails after send: re-read with `Subscriptions.FindSubscription`, searching by the subscription `reference` (`eshop-{username}-{planHandle}`). If found → treat as success. If `CreateCustomer` transport fails: re-read with `Customers.ReadCustomerByReference` searching by `reference`=username. |
| 14 | Provider status & reconciliation clocks | `SubscriptionResponse.Subscription.State` is the status field. Code surfaces it verbatim (`.Value`) and does not coerce; success = subscription returned with a non-null id. No two-source reconciliation clock in scope (Maxio is the single source of record; local ledger is a claim cache, not a reconciled mirror) → single-timestamp rule N/A. |

### DUPLICATE CLAIMS

| write | where the claim is stored | what rejects the second one | where that rejection is caught |
| --- | --- | --- | --- |
| ensure Maxio customer for a user | `CatalogContext.MaxioCustomerLink`, column `BuyerId` | unique index on `BuyerId` | `DbUpdateException` caught in `EnsureCustomerAsync` → reload existing link |
| create subscription for user+plan | `CatalogContext.MaxioSubscriptionRecord`, columns `BuyerId`+`PlanHandle` | composite unique index on (`BuyerId`,`PlanHandle`) | `DbUpdateException` caught in `SubscribeAsync` → reconcile via `FindSubscription`/return existing |

### PAGED READS

| read | what caps it | how the caller learns the answer was cut short |
| --- | --- | --- |
| list plans (`ListProductsForProductFamily`) | provider page size (20) + defensive 20-page cap | `SubscriptionPlansResponse.Truncated` bool on the return type |
| my subscriptions (`ListCustomerSubscriptions`) | single call, provider returns all for the customer (not paged in the SDK signature) | N/A — not paged (returns full `IReadOnlyList`) |

### REPEATED OPERATIONS

| operation | what tells you the state actually changed | the effects gated on that |
| --- | --- | --- |
| subscribe (user+plan) | the `MaxioSubscriptionRecord` claim row was newly inserted (no `DbUpdateException`) AND `FindSubscription(reference)` returned no existing active subscription | the Maxio `CreateSubscription` call + writing the returned subscription id/state to the ledger |
| ensure customer | the `MaxioCustomerLink` row was newly inserted AND `ReadCustomerByReference` returned 404 | the Maxio `CreateCustomer` call |

### UNKNOWN OUTCOMES

| write | the operation you re-read with | the reference you search by |
| --- | --- | --- |
| `CreateSubscription` | `Subscriptions.FindSubscription` | subscription `reference` = `eshop-{username}-{planHandle}` |
| `CreateCustomer` | `Customers.ReadCustomerByReference` | customer `reference` = eShop username |

## 6. Assumptions & Blockers

- **Assumption (minor):** eShop `ApplicationUser` has no first/last name fields exposed to
  PublicApi; the username is the email. `CreateCustomer` requires first+last name, so we derive
  `FirstName` from the email local-part and default `LastName` to `"eShop User"`. Harmless demo data.
- **YOUR CALL — not in the map:** `ServerEnvironment.Us` (Basic auth requires US/EU; seeded site
  is `chargify.com`). No `Maxio:Environment` binding key was mandated, so US is fixed in code as the
  sandbox target; EU/gateway would need a code change (documented, acceptable for this integration).
- **Environment caveat (this machine only), not a design limitation:** the task mandates
  `UseOnlyInMemoryDatabase=true`; the EF Core **in-memory provider does not enforce unique
  indexes**, so the §9 constraints are enforced only against SQL Server (the app's real provider).
  The design carries the correct unique constraints; on this machine the reconciliation reads
  (`ReadCustomerByReference`/`FindSubscription`) keep sequential double-clicks idempotent. No design
  substitution (lock/dictionary/single-host) is used.
- **No blockers.** Every capability the hero flow needs is exposed by the plugin's SDK map.
