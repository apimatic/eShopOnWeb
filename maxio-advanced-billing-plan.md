# Maxio Advanced Billing integration plan — eShopOnWeb subscription billing

Additive, parallel recurring-subscription capability with **Maxio Advanced Billing** as the
system of record. Three JWT-authenticated endpoints on `src/PublicApi`:
`GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions`.
Maxio is the source of truth — no local subscription persistence (the in-memory DB is lost on
restart), so state is always re-read from Maxio by a stable, deterministic **reference**.

## 1. Scope & sequence

| # | Step | Maxio operations used |
| --- | --- | --- |
| 1 | Vendor the SDK source into the repo (`src/Integrations/MaxioAdvancedBilling.Sdk/`), opt out of Central Package Management, add to `eShopOnWeb.sln`, `ProjectReference` from Infrastructure. | — (not published to NuGet; build-from-source) |
| 2 | ApplicationCore: `ISubscriptionBillingService` + plain DTOs (no SDK types leak into core). | — |
| 3 | Infrastructure `Maxio/`: `MaxioSettings` (bind `Maxio:` section), fail-fast validation, singleton client via `IHttpClientFactory`, `MaxioBillingService` implementing the interface. | listed below |
| 4 | List plans for the configured product family. | `ProductFamilies.ListProductsForProductFamily` |
| 5 | Subscribe: ensure customer (idempotent), then create subscription (idempotent). | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Subscriptions.FindSubscription`, `Subscriptions.CreateSubscription` |
| 6 | List a user's subscriptions. | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |
| 7 | PublicApi endpoints + DTOs; resolve caller identity from JWT (`ClaimTypes.Name`) → `UserManager<ApplicationUser>` for email/names/stable reference. | — |
| 8 | user-secrets from env vars; test-host placeholders; build/test; live sandbox verify. | — |

**Identity → Maxio reference.** JWT carries `ClaimTypes.Name` (the username, e.g.
`demouser@microsoft.com`). The endpoint resolves the `ApplicationUser` via `UserManager` and
uses a **deterministic, stable customer reference** derived from the username
(`eshop-user-{normalized-username}`) so a Maxio customer is found again across process
restarts (the in-memory identity DB reseeds the user's GUID each run, but the username is
seeded deterministically). Subscription reference is deterministic per (user, plan):
`eshop-sub-{normalized-username}-{planHandle}` — this is the duplicate-prevention key (§5).

## 2. CONTRACT SHEET

> ⚠ Signatures below are **generated code, verbatim**. Every parameter name is the literal
> C# identifier; in named arguments use exactly these (the cancellation-token parameter is
> `ct`, so write `ct:`). Optional query params have no C# default → pass `null` explicitly.
> ⚠ Every SDK type is written **fully-qualified with the namespace its source path implies**
> (`Models/` → `MaxioAdvancedBilling.Models`, `Models/Enums/` → `…Models.Enums`,
> `Errors/` → `…Errors`, root/client → `MaxioAdvancedBilling`,
> `Servers/` → `MaxioAdvancedBilling.Servers`, `Api/` → `MaxioAdvancedBilling.Api`),
> taken from the path the map gives for THAT type.

### Client construction / auth / server (source: `sdk-map.md` §Getting a client, §Servers & auth)

- Client: `MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)` — only ctor.
- Options: `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` — `BasicAuth: BasicAuthCredentials?`, `BearerAuth: string?`, `Environment: ServerEnvironment`, `Server: ServerOptions`, `Retry`, `Logging`, `Hooks`.
- **Auth (all ops in scope): `options.BasicAuth` OR `options.BearerAuth`.** Basic works only with US/EU (direct `chargify.com`). `BasicAuthCredentials { Username = <Chargify API key>, Password = "x" }`. → US env + Basic (`MAXIO_ENVIRONMENT=US`).
- Environment: `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default).
- Base URL: `Production`/`Us` template `https://{site}.chargify.com`, `{site}` defaults to `"subdomain"` → override `options.Server.Production.Us.Site = <Maxio:Subdomain>`. When `Maxio:BaseUrl` is set, use it **verbatim** via `options.Server.Production.Us.BaseUrl` instead of deriving from subdomain.
- DI helper `services.AddMaxioAdvancedBillingClient(configure)` builds options **once at registration**, captures in the singleton, and sets `Logging.LoggerFactory ??= sp ILoggerFactory` — see §5 rows 2 & 7 for why we register the client ourselves rather than using it verbatim.

### Operations (source column cites the map page / declaring file)

| Operation | Signature (verbatim) · request→fields · response fields read · error case | Source |
| --- | --- | --- |
| `client.ProductFamilies.ListProductsForProductFamily` | `(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, RequestOptions? = null, CancellationToken ct = default)`. `productFamilyId` path accepts numeric id **or `handle:<handle>`** (route `/product_families/{product_family_id}/products.json`; sibling `ReadProductFamily` remarks confirm the `handle:` form). Returns `IReadOnlyList<CreateProductResponse? no →> ProductResponse>`; read `ProductResponse.Product` → `Product.{Handle,Name,Description,PriceInCents,Interval,IntervalUnit,ArchivedAt}`. **Case A** `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError` [fallback]. | `map/operations/ProductFamilies.md`; `Api/ProductFamilies.cs`; `Models/ProductResponse.cs`, `Models/Product.cs` |
| `client.Customers.ReadCustomerByReference` | `(string reference, RequestOptions? = null, CancellationToken ct = default)`. Route `/customers/lookup.json?reference=`. Returns `CustomerResponse`; read `.Customer.{Id,Email,FirstName,LastName,Reference}`. **Case B** `SdkException<RawError>` — **404 when no match** (`RawError.StatusCode`). | `map/operations/Customers.md`; `Api/Customers.cs`; `Models/CustomerResponse.cs`, `Models/Customer.cs` |
| `client.Customers.CreateCustomer` | `(CreateCustomerRequest? body, RequestOptions? = null, CancellationToken ct = default)` — `body` must pass explicitly. Route POST `/customers.json`. Returns `CustomerResponse`. **Case A** `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError` [fallback]. | `map/operations/Customers.md`; `Models/CreateCustomerRequest.cs`, `Models/CreateCustomer.cs`, `Models/CustomerErrorResponse1.cs` |
| `client.Customers.ListCustomerSubscriptions` | `(int customerId, RequestOptions? = null, CancellationToken ct = default)`. Returns `IReadOnlyList<SubscriptionResponse>`. **Case B** `SdkException<RawError>`. | `map/operations/Customers.md`; `Models/SubscriptionResponse.cs` |
| `client.Subscriptions.FindSubscription` | `(string? reference, RequestOptions? = null, CancellationToken ct = default)` — pass explicitly. Route `/subscriptions/lookup.json?reference=`. Returns `SubscriptionResponse`. **Case A** `SdkException<FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError` [fallback]. | `map/operations/Subscriptions.md`; `Api/Subscriptions.cs` |
| `client.Subscriptions.CreateSubscription` | `(CreateSubscriptionRequest? body, RequestOptions? = null, CancellationToken ct = default)` — pass explicitly. Route POST `/subscriptions.json`. Returns `SubscriptionResponse` (`.Subscription` is nullable). **Case A** `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback]. | `map/operations/Subscriptions.md`; `Models/CreateSubscriptionRequest.cs`, `Models/CreateSubscription.cs`, `Models/ErrorListResponse1.cs` |

### Request bodies — fields actually set (all optional unless noted); each with `purpose`

**`CreateCustomerRequest` → `Customer: CreateCustomer` (required wrapper).** `CreateCustomer`
required members: `FirstName (first_name)`, `LastName (last_name)`, `Email (email)` — all
`required string`. Optional set: `Reference (reference): string?` — **purpose:** our stable
per-user key; the lookup key for idempotency (§5). All other `CreateCustomer` optionals
(address, tax_exempt, locale, branding_theme_id, …) → **omit → provider default** (not in
scope; setting them would override provider-side defaults). Source: `Models/CreateCustomer.cs`.

**`CreateSubscriptionRequest` → `Subscription: CreateSubscription` (required wrapper).**
`CreateSubscription` marks **nothing** `required`, so `required?` selects nothing — the fields
below are chosen from the endpoint prose that decides call acceptance:
- `ProductHandle (product_handle): string?` — **purpose:** identifies the plan; remarks: "Required, unless a `product_id` is given." We use the handle (stable). Source: `Models/CreateSubscription.cs`.
- `CustomerReference (customer_reference): string?` — **purpose:** attach to the existing customer by our stable reference; remarks: "Required, unless a `customer_id` or `customer_attributes` is given." Source: same.
- `Reference (reference): string?` — **purpose:** deterministic subscription reference = the duplicate-prevention key (§5). Source: same.
- `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` = `CollectionMethod.Remittance` — **purpose:** the plans are *payment method not required*, but with the default (automatic) collection Maxio still attempts to charge the signup balance and rejects the create with 422 *"No payment method was on file"* (confirmed against the sandbox). Setting **remittance** (invoice billing) lets the subscription activate without card capture / 3-DS. This is a request-contract decision (`YOUR CALL — not in the map`), verified on live sandbox traffic. Enum source: `Models/Enums/CollectionMethod.cs`.
- Left out deliberately: `CustomerId`/`ProductId` (we key on handle/reference, ids are unstable per task); `payment_profile_attributes`/`credit_card_attributes`/`bank_account_attributes` (**omit → provider default**; no card capture); `next_billing_at`/`initial_billing_at`/`defer_signup` (**omit → provider default**); `coupon_codes`, `components`, `offer_id`, `currency`, `net_terms`, `metafields` (out of scope — **omit → provider default**). No `import/migration only` fields are set.

### Response fields read

- `Product`: `Handle`, `Name`, `Description`, `PriceInCents (long?)`, `Interval (int?)`, `IntervalUnit (IntervalUnit? enum)`, `ArchivedAt (DateTimeOffset?)` (filter archived out). Source `Models/Product.cs`.
- `Customer`: `Id (int?)`, `Reference`, `Email`, `FirstName`, `LastName`. Source `Models/Customer.cs`.
- `Subscription`: `Id (int?)`, `State (SubscriptionState? enum)`, `ProductPriceInCents (long?)`, `CurrentPeriodEndsAt (DateTimeOffset?)` = **next billing date**, `NextAssessmentAt (DateTimeOffset?)`, `Reference`, `Product (Product?)`, `Currency`, `CreatedAt`. Source `Models/Subscription.cs`.

### Enums needed

- `MaxioAdvancedBilling.Models.Enums.SubscriptionState` — `StringEnum<SubscriptionState>`; static members (`.Active`, `.Trialing`, `.Canceled`, …); read wire value via `.Value` (base `TypedEnum<string,_>`); build via `SubscriptionState.FromValue("active")`. Source `Models/Enums/SubscriptionState.cs`.
- `MaxioAdvancedBilling.Models.Enums.IntervalUnit` — for the plan billing period unit; read `.Value`. Source `Models/Enums/IntervalUnit.cs`.
- `SubscriptionState` and `IntervalUnit` are **not** C# enums (`StringEnum<T>`) — see `dotnet-models` before comparing/reading. States considered "active-ish" for display are read-only; no state is *constructed*, only rendered via `.Value`.

### CROSS-OPERATION INVARIANTS

| invariant | operations | enforced where |
| --- | --- | --- |
| `planHandle` accepted by subscribe must be a product handle the family actually offers | `Subscriptions.CreateSubscription` ← `ProductFamilies.ListProductsForProductFamily` | implementation — the POST handler validates the requested `planHandle` against the family's non-archived product handles before calling CreateSubscription; unknown handle → 400 (never forwarded to Maxio) |
| customer attached at subscribe must be one that exists (by reference) | `Subscriptions.CreateSubscription` ← `Customers.ReadCustomerByReference`/`CreateCustomer` | implementation — ensure-customer runs first and its reference is what CreateSubscription passes as `customer_reference` |

## 3. Trap notes (name the hazard + skill; do not resolve here)

- **Client lifetime / HttpClient pipeline** — a per-request `new HttpClient()` or a rebuilt handler pipeline leaks sockets / breaks DNS rotation; the SDK client wrapper's lifetime is a separate decision. **MUST load dotnet-client-initialization.** (step 3)
- **Credential wiring & when it is read** — setting `BasicAuth` at the wrong point, or a credential that is silently *not sent* (so a 401 means "nothing sent", not "wrong value"). **MUST load dotnet-authentication.** (step 3)
- **List/search calls with positional args** — the many no-C#-default optional params mis-bind positionally; named arguments matter. **MUST load dotnet-calling-endpoints.** (steps 4–6)
- **`StringEnum<T>` and response envelopes** — `SubscriptionState`/`IntervalUnit` are not C# enums; unknown JSON fields land on `AdditionalProperties`; the response wraps its payload one level down. **MUST load dotnet-models.** (steps 4–6)
- **Error boundary shape** — Case A vs Case B differ; a 2xx body missing a `required` member throws `System.Text.Json.JsonException` (not `SdkException`), and a non-2xx body that does not match the operation's `{Operation}Error` throws `JsonException` **while constructing the error**, destroying the HTTP status. **MUST load dotnet-error-handling.** (step 3, every call site)
- **Retry/timeout/logging semantics** — `Timeout` is per-attempt not total; `HttpMethodsToRetry` gates whether a POST can be resent; `LogRequestBody` logs JSON unredacted and the `MAXIOADVANCEDBILLINGCLIENT_LOG` env var can arm it if `LoggerFactory` is left implicit. **MUST load dotnet-configuration-resilience.** (steps 3 & 5-readiness)
- **Test seam** — the SDK's `HttpClient` ctor arg is the fake seam; match the repo's xUnit/NSubstitute style. **MUST load dotnet-testing.** (step 8)

## 4. REQUIRED READING (load all before implementation; contents deliberately not inlined)

- `maxio-platforms-team:dotnet-client-initialization` — client construction & DI singleton (step 3).
- `maxio-platforms-team:dotnet-authentication` — Basic-auth credential wiring (step 3).
- `maxio-platforms-team:dotnet-calling-endpoints` — named-argument calls, list ops (steps 4–6).
- `maxio-platforms-team:dotnet-models` — `StringEnum<T>`, envelopes, `AdditionalProperties` (steps 4–6).
- `maxio-platforms-team:dotnet-error-handling` — Case A/B, `TryGet…`, the two `JsonException` directions (always). **Both hazard rows apply:** a drifted/malformed **2xx** body surfaces as `System.Text.Json.JsonException` from deserialization, **not** `SdkException`, so an SDK-exception-only catch ladder lets it escape; a **non-2xx** body that does not match its `{Operation}Error` throws `JsonException` while the error object is constructed, **replacing** the `SdkException` and destroying the HTTP status.
- `maxio-platforms-team:dotnet-configuration-resilience` — retries/timeout/logging (steps 3 & readiness).
- `maxio-platforms-team:dotnet-testing` — test seam (step 8).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `MaxioSettings` bound from `Maxio:` section; `IValidateOptions` + `ValidateOnStart` requires `ApiKey`, `Subdomain`, `ProductFamilyHandle` **each non-null & non-whitespace** (blank ≠ missing — all three checked). `BaseUrl` optional. Host refuses to start otherwise. Artefact: `Infrastructure/Maxio/MaxioSettings.cs` + `MaxioSettingsValidator`. |
| 2 | Secret sourcing & rotation | Values from .NET user-secrets (loaded from env vars `MAXIO_*`; never in repo). We register the client ourselves as a singleton, building options **once at registration** → a rotated key needs a process restart. This is accepted (no hot-rotation requirement); documented in code comment. |
| 3 | Total timeout budget | SDK `Timeout` is **per attempt**. We enforce a whole-call bound with a `CancellationToken` linked to an overall deadline (from `HttpContext.RequestAborted` + a configured budget) passed as `ct:` to every SDK call. Retry timeout budget kept small (`MaxRetries` low). Artefact: `MaxioBillingService` call sites + `Retry` config in registration. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS, so `POST` (`CreateCustomer`, `CreateSubscription`) is **never resent by the SDK** — good, avoids duplicate writes. We keep POSTs out of the retry set (default). GET lookups may retry. Decided in registration `RetryOptions`. |
| 5 | Idempotency & ambiguous writes | **No caller-supplied idempotency key exists** on these ops (the injected `Idempotency-Key: Guid.NewGuid()` header is fresh per call → not a key). Reconciliation instead: **customer** keyed on `reference`; **subscription** keyed on `reference`. Both writes are preceded by a lookup and followed by catch-and-reconcile (§9, §13). |
| 6 | Observability | Structured logs via `IAppLogger<MaxioBillingService>`: Info on customer-ensured / subscription-created (ids + reference), Warning on reconciled races, Error on provider failures. `LogRequestBody` stays **off**. Provider error text (from `ErrorListResponse1.Errors` / `RawError.ReadAsString()`) is logged and mapped to a domain exception carrying the messages. |
| 7 | Sensitive data | Request bodies carry customer **PII** (email, first/last name) but **no card/bank data** (payment method not required; no `*_attributes` set). Posture: `LogRequestBody` = off **and** `options.Logging.LoggerFactory` assigned **explicitly** in registration so `MAXIOADVANCEDBILLINGCLIENT_LOG` cannot switch body logging on from outside code. Our own logs never echo request bodies. |
| 8 | Environment selection | Only `ServerEnvironment.Us` (Production/Us group, `https://{Subdomain}.chargify.com`) in scope (`MAXIO_ENVIRONMENT=US`). All dev/test traffic targets the **sandbox** site via `Maxio:Subdomain`/`Maxio:BaseUrl`. Test hosts get **placeholder** config and never make a live call (no test exercises the network path). No EU/Gateway group used. |
| 9 | Duplicate prevention under concurrency | **Store = Maxio; unique column = `reference`.** Customer `reference` and subscription `reference` are unique per site in Maxio; the second concurrent create is **rejected by the provider (422)** and the code **catches that rejection** (Case A `TryGetCustomerErrorResponse1` / `TryGetErrorListResponse1`) then re-reads by reference and returns the existing entity. No `SemaphoreSlim`/dict/single-host/pre-check-only is used as the guard. (Provider uniqueness of `reference` is not something the SDK map can assert → labelled **UNVERIFIED**; code is written to be correct whether or not the race occurs, by reconciling on any 422.) |
| 10 | Partial results | `ListProductsForProductFamily` and `ListCustomerSubscriptions` are the paged reads. The plan family holds a handful of plans and a user a handful of subscriptions — but to be safe against a `perPage` cap, plan listing **loops pages until a short page** and the response DTO carries no silent truncation; subscriptions per customer use `ListCustomerSubscriptions` (returns all for a customer, not caller-paged). If a cap were hit, the DTO exposes the full accumulated list; there is no place a caller is silently handed a truncated set. |
| 11 | Startup validation vs existing test host | Both `tests/PublicApiIntegrationTests` (`WebApplicationFactory<Program>`) and `tests/FunctionalTests` (`WebApplicationFactory<AuthenticateEndpoint>`, env `Testing`) boot the real host. Placeholder `Maxio:` config is supplied: `PublicApiIntegrationTests/appsettings.test.json` gets a placeholder `Maxio` section; `FunctionalTests` `TestApiApplication` adds placeholder `Maxio` values via `ConfigureAppConfiguration`. Both projects are run and must be **green**. |
| 12 | Ordering & no-op side effects | No local write precedes/follows the provider call (Maxio is the record of truth; nothing local to persist). Ensure-customer is **idempotent** (lookup → create-or-reuse) and emits its "created" log **only** when a create actually happened (gated on whether the create path ran, not on every ensure). Subscribe likewise logs "created" only on an actual create, "reused" on the idempotent hit. No unconditional notification. |
| 13 | Unknown outcomes | If the transport fails after a POST may have been received: **customer** → re-read via `Customers.ReadCustomerByReference` (by `reference`); **subscription** → re-read via `Subscriptions.FindSubscription` (by `reference`). Only if the re-read also finds nothing do we surface a failure. No definite failure is thrown without the re-read. |
| 14 | Provider status & reconciliation clocks | The status field is `Subscription.State` (`SubscriptionState`). Subscribe returns the live state to the caller and does not assume success: a non-`active`/`trialing` state (e.g. `failed_to_create`, `past_due`) is surfaced verbatim in the response DTO and logged at Warning; the code never coerces status (no `state ?? "active"`). No two-source reconciliation clock is needed (single source = Maxio); N/A for the shared-timestamp part. |

## 6. Assumptions & Blockers

- **Assumption:** "next billing date" = `Subscription.CurrentPeriodEndsAt` (the end of the current recurring period = when the next charge is attempted); `NextAssessmentAt` also surfaced. No blocker.
- **Assumption:** plan selection is by `planHandle` in the POST body (no config key for a default plan was provided; only the family handle). If omitted → 400. Validated against the family's product handles (§2 invariants).
- **Assumption (UNVERIFIED, see §5/9):** Maxio enforces per-site uniqueness of customer & subscription `reference`. Code reconciles on any 422 so it is correct regardless.
- **No blockers.** Every capability the flow needs is present in the map (list-products-for-family, read/create customer, find/create/list subscription).
