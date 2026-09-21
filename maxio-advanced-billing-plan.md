# Maxio Advanced Billing — integration plan (eShopOnWeb subscription billing)

Additive, parallel subscription capability on top of eShopOnWeb's one-time commerce. Three
JWT-authenticated endpoints on `src/PublicApi`, backed by Maxio Advanced Billing (system of
record). Caller identity = the JWT `ClaimTypes.Name` (username/email).

## 1. Scope & sequence

| # | Step | Maxio operations |
| --- | --- | --- |
| 1 | Vendor SDK source into `src/MaxioAdvancedBilling.Sdk`, add to solution, `ProjectReference` from Infrastructure. | — |
| 2 | `MaxioOptions` bound from `Maxio:` section (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl?`); fail-fast validation. | — |
| 3 | Register SDK client (BasicAuth = ApiKey/`x`, env Us, site=Subdomain or BaseUrl override). | — |
| 4 | ApplicationCore: `ISubscriptionBillingService` + DTOs (`SubscriptionPlanDto`, `CustomerSubscriptionDto`, results). | — |
| 5 | Infra `MaxioSubscriptionBillingService`: list plans / ensure-customer / subscribe / list-my-subs, with error mapping + reconciliation. | `ListProductsForProductFamily`, `ReadCustomerByReference`, `CreateCustomer`, `FindSubscription`, `CreateSubscription`, `ListCustomerSubscriptions` |
| 6 | PublicApi endpoints: `GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions`. | (via service) |
| 7 | Secrets → user-secrets; placeholder Maxio config in test host; build; e2e verify against sandbox; tests. | — |

**Hero flow (POST /api/subscriptions):** resolve plan handle → validate it is in the family's
product list → ensure Maxio customer (read-by-reference → create → on-conflict re-read) →
find-existing-subscription-by-reference (idempotent hit) → else create subscription with a
deterministic reference → surface plan/price/state/next-billing-date.

## 2. CONTRACT SHEET

> ⚠ Signatures below are generated code, verbatim. Every parameter name is the literal C#
> identifier; in named arguments use exactly those names (the cancellation-token parameter is
> named `ct`, so write `ct:`). Optional/nullable params with no C# default must be passed
> explicitly (pass `null` to skip) — call list/lookup ops with named arguments.
> ⚠ Every SDK type is written fully-qualified with the namespace its source path implies
> (`Models/` → `MaxioAdvancedBilling.Models`, `Models/Enums/` → `...Models.Enums`,
> `Errors/` → `...Errors`, `Core/Authentication/Basic/` → `...Core.Authentication.Basic`,
> client/options/servers at root/`...Servers`), taken from the path the map gives for THAT type.

### Operations

| Op | Controller · signature | Request model / key fields | Response envelope → fields read | Error case + accessors | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| List plans | `client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `productFamilyId` = `"handle:" + ProductFamilyHandle` (accepts id **or** `handle:` prefix). Pass `includeArchived: false`. | `IReadOnlyList<ProductResponse>` → each `.Product` → `Id`, `Name`, `Handle`, `Description`, `PriceInCents`, `Interval`, `IntervalUnit`, `ArchivedAt`, `RequireCreditCard`, `ProductPricePointHandle` | **Case A** `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | page/perPage (page-based). **Loop pages** until a short page. | ProductFamilies.md; `Models/ProductResponse.cs`, `Models/Product.cs` |
| Read customer by ref | `client.Customers.ReadCustomerByReference(string reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `reference` = eShop username | `CustomerResponse` → `.Customer` → `Id`, `Reference`, `Email`, `FirstName`, `LastName` | **Case B** `SdkException<RawError>` — 404 when absent (`.StatusCode`) | none | Customers.md; `Models/CustomerResponse.cs`, `Models/Customer.cs` |
| Create customer | `client.Customers.CreateCustomer(CreateCustomerRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateCustomerRequest { Customer = CreateCustomer{...} }` (**required** `Customer`). `CreateCustomer` **required**: `FirstName`, `LastName`, `Email`. Set `Reference` (username) — *purpose:* provider-unique idempotency claim (`omit → provider default` = no reference). Leave all other optionals unset (`omit → provider default`). No card fields. | `CustomerResponse` → `.Customer.Id`, `.Reference` | **Case A** `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | Customers.md; `Models/CreateCustomerRequest.cs`, `Models/CreateCustomer.cs`, `Models/CustomerErrorResponse1.cs` |
| Find subscription by ref | `client.Subscriptions.FindSubscription(string? reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `reference` = deterministic sub ref | `SubscriptionResponse` → `.Subscription` (nullable) | **Case A** `SdkException<FindSubscriptionError>` — `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback] | none | Subscriptions.md; `Models/SubscriptionResponse.cs`, `Models/Subscription.cs` |
| Create subscription | `client.Subscriptions.CreateSubscription(CreateSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateSubscriptionRequest { Subscription = CreateSubscription{...} }` (**required** `Subscription`). Set `ProductHandle` — *purpose:* the plan to subscribe (handle preferred over id per remarks). Set `CustomerReference` (username) — *purpose:* bind to existing customer without a second id round-trip. Set `Reference` (deterministic) — *purpose:* idempotency/lookup key. Set `PaymentCollectionMethod` = `CollectionMethod` from `Maxio:PaymentCollectionMethod` (default `remittance`) — *purpose:* invoice/remittance billing so a no-card subscription is accepted; the provider default `automatic` attempts an immediate card charge and returns 422 "No payment method was on file" (confirmed live). No card fields. | `SubscriptionResponse` → `.Subscription` → `Id`, `State`, `CurrentPeriodEndsAt`, `NextAssessmentAt`, `ProductPriceInCents`, `Reference`, `Product.Handle/Name`, `CreatedAt` | **Case A** `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | Subscriptions.md; `Models/CreateSubscriptionRequest.cs`, `Models/CreateSubscription.cs`, `Models/ErrorListResponse1.cs` |
| List customer subscriptions | `client.Customers.ListCustomerSubscriptions(int customerId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `customerId` from ensured customer | `IReadOnlyList<SubscriptionResponse>` → each `.Subscription` → `Id`, `State`, `CurrentPeriodEndsAt`, `NextAssessmentAt`, `ProductPriceInCents`, `Product.Handle/Name`, `Reference` | **Case B** `SdkException<RawError>` | none (single response) | Customers.md; `Models/Subscription.cs` |

### Enums / client

- `SubscriptionState` (`StringEnum<SubscriptionState>`, `Models/Enums/SubscriptionState.cs`) — wire values: `active`, `trialing`, `pending`, `awaiting_signup`, `assessing`, `past_due`, `soft_failure`, `unpaid`, `canceled`, `expired`, `failed_to_create`, `on_hold`, `suspended`, `paused`, `trial_ended`. Render via its wire value (StringEnum). Success states for subscribe = `active`, `trialing` (and `awaiting_signup`/`pending` = accepted-but-pending); failure = `failed_to_create`, `canceled`.
- `IntervalUnit` (`Models/Enums/IntervalUnit.cs`) — `month`, `day` (billing period unit, rendered as wire value).
- Client: `new MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)`; DI: `services.AddMaxioAdvancedBillingClient(opts => …)` (`ServiceCollectionExtensions.cs`). Options: `BasicAuth = new BasicAuthCredentials { Username = <ApiKey>, Password = "x" }` (`Core/Authentication/Basic/BasicAuthCredentials.cs`; per *Servers & auth*: username=API key, password=`x`); `Environment = ServerEnvironment.Us` (`MaxioAdvancedBilling.Servers`); site via `opts.Server.Production.Us.Site = <Subdomain>` **or** `opts.Server.Production.Us.BaseUrl = <BaseUrl>` verbatim when `Maxio:BaseUrl` is set (`Servers/ProductionOptions.cs` → `UsOptions { BaseUrl, Site }`).

### CROSS-OPERATION INVARIANTS

| invariant | operations | enforced where |
| --- | --- | --- |
| The plan `handle` a caller submits to subscribe must be one returned for the configured family | `CreateSubscription` ← `ListProductsForProductFamily` | implementation (service validates requested handle against the family's product handles before creating; unknown handle → 400 to caller) |
| The `customer_reference` used on subscribe must be an existing Maxio customer's reference | `CreateSubscription` ← `CreateCustomer`/`ReadCustomerByReference` | implementation (customer is ensured before subscription create) |

## 3. Trap notes

- List/lookup ops (`ListProductsForProductFamily`, `ListCustomers`, `ListSubscriptions`) have many nullable no-default params — a positional call mis-binds. Hazard: silent wrong query. **MUST load dotnet-calling-endpoints.**
- `SubscriptionState` is `StringEnum<T>`, not a C# enum; equality/rendering differ from a normal enum. Hazard: comparing with `==` to a string or `.ToString()` giving the wrong token. **MUST load dotnet-models.**
- `CreateSubscription`/`CreateCustomer` bodies are `required`-wrapped records with an `AdditionalProperties` bag; optional fields set unnecessarily override provider defaults. Hazard: an over-populated body changing billing behaviour. **MUST load dotnet-models.**
- Error boundary spans Case A (typed `{Operation}Error`) and Case B (`RawError`) **and** two `JsonException` directions (drifted 2xx body; non-2xx body not matching `{Operation}Error`). Hazard: exception escaping the catch ladder / HTTP status destroyed. **MUST load dotnet-error-handling.**
- `Retry.Timeout` is per-attempt, `HttpMethodsToRetry` gates all retries (POST never resent, PUT **is**), `LogRequestBody` logs JSON unredacted, and pagination is hand-rolled. Hazard: a call costing a multiple of the timeout / PII in logs / silent truncation. **MUST load dotnet-configuration-resilience.**
- Client/HttpClient lifetime: the SDK client wraps a long-lived `HttpClient` via `IHttpClientFactory`; rebuild-per-request leaks sockets. Hazard: handler exhaustion. **MUST load dotnet-client-initialization.**
- Auth: BasicAuth username/password roles are non-obvious (API key as username, literal `x` as password); a credential never set is silently skipped. Hazard: unauthenticated requests that look like bad credentials. **MUST load dotnet-authentication.**
- Test seam is the `HttpClient` constructor argument. Hazard: tests coupled to SDK internals. **MUST load dotnet-testing.**

## 4. REQUIRED READING (load all before implementing; this sheet does not carry their contents)

- **maxio-platforms-team:dotnet-client-initialization** — step 3 (client construction & DI registration in Infrastructure).
- **maxio-platforms-team:dotnet-authentication** — step 3 (BasicAuth credentials).
- **maxio-platforms-team:dotnet-calling-endpoints** — step 5 (named-argument calls to list/lookup/create ops).
- **maxio-platforms-team:dotnet-models** — step 5 (building request records; reading `StringEnum` states).
- **maxio-platforms-team:dotnet-error-handling** — step 5 (error boundary; always required). Mandatory hazard rows: (a) a drifted/malformed **2xx** body (missing `required` member) throws `System.Text.Json.JsonException` from deserialization, **not** `SdkException` — an SDK-exception-only ladder lets it escape; (b) a **non-2xx** body not matching its operation's `{Operation}Error` throws `JsonException` *while constructing the error object*, **replacing** the `SdkException` and destroying the HTTP status.
- **maxio-platforms-team:dotnet-configuration-resilience** — step 3/5 (retries, per-attempt timeout, base-URL, pagination, logging).
- **maxio-platforms-team:dotnet-testing** — step 7 (faking the `HttpClient` seam).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `MaxioOptions` bound from `Maxio:`; an `IValidateOptions<MaxioOptions>` + `.ValidateOnStart()` rejects blank/missing `ApiKey`, `Subdomain`, **and** `ProductFamilyHandle` (each part checked individually — a blank part fails). `BaseUrl` optional. Host refuses to start otherwise. |
| 2 | Secret sourcing & rotation | Secrets from .NET user-secrets (dev) / env-vars `Maxio:*` (prod); `AddMaxioAdvancedBillingClient` builds the options object **once at registration** and captures it in the singleton, so a rotated key needs a **process restart**. Documented; no hot-reload required for this scope. |
| 3 | Total timeout budget | SDK `Retry.Timeout` is **per attempt**; the caller-visible budget is bounded by a per-operation `CancellationTokenSource` (linked to `HttpContext.RequestAborted`) with a fixed wall-clock deadline (30 s), passed as `ct:` to every SDK call. Retries kept small (`MaxRetries` 2) so worst case ≈ budget, not a multiple. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = GET,HEAD,PUT,OPTIONS. Our writes are **POST** (`CreateCustomer`, `CreateSubscription`) → **never resent by the SDK**. We call no PUT. GET reads (lookups/lists) are idempotent and safe to retry. |
| 5 | Idempotency & ambiguous writes | `CreateCustomer`: key = customer `reference` (= eShop username), **provider-unique** (verified: "you may only create one customer for a given reference value"). Path: read-by-reference → create → on-422 re-read. `CreateSubscription`: key = deterministic subscription `reference` (`eshop:{username}:{productHandle}`); find-by-reference first, create, on-error re-read via `FindSubscription`. Injected `Idempotency-Key` header is **not** a key. Subscription-reference create-uniqueness is `UNVERIFIED` (see §6) → reconciliation covers it. |
| 6 | Observability | Log at Information: endpoint, username, productHandle, resulting `customerId`/`subscriptionId`/`state`. On provider error: log status + `ErrorListResponse1.Errors` / `CustomerErrorResponse1` messages at Warning/Error. `LogRequestBody` stays **off**. This SDK's `RawError` carries only `StatusCode` (no provider correlation id) → we log status plus our own request context. |
| 7 | Sensitive data | Request models carry **email/name PII** (`CreateCustomer`), no card/bank data (we send none). Posture: `LogRequestBody` off; `LoggerFactory` set explicitly by the DI extension (not left to the `MAXIOADVANCEDBILLINGCLIENT_LOG` env var); our own diagnostics never echo a request body. |
| 8 | Environment selection | `ServerEnvironment.Us` (the sandbox site is US-hosted). Base URL = `https://{Subdomain}.chargify.com` via `Server.Production.Us.Site`, or `Maxio:BaseUrl` verbatim override. This SDK exposes **no separate sandbox environment** — Maxio's sandbox is a distinct *site* (subdomain), so test traffic is isolated by pointing at the sandbox subdomain; production sets a different `Maxio:Subdomain`/`BaseUrl`. |
| 9 | Duplicate prevention under concurrency | Store = **Maxio customers**, column = **`reference`**; its **unique constraint** rejects the second concurrent `CreateCustomer` (HTTP 422), and the code **catches** `SdkException<CreateCustomerError>` and reconciles via `ReadCustomerByReference`. (Local EF store is the **in-memory** provider, which does **not** enforce unique indexes, so the durable claim lives in Maxio — the system of record — by design.) Subscription duplicate-claim = Maxio subscription `reference`; create-time uniqueness `UNVERIFIED` → §6 + reconcile via `FindSubscription`. |
| 10 | Partial results | `ListProductsForProductFamily` is page-based. We **loop pages** (`perPage` 200) until a short page, so the plans list is never silently truncated; if a hard page cap is ever hit we `log()` the truncation. `ListCustomerSubscriptions` returns a single (non-paged) response. |
| 11 | Startup validation vs test host | `tests/PublicApiIntegrationTests` boots `WebApplicationFactory<Program>`. Placeholder (non-secret) `Maxio` config is added to `tests/PublicApiIntegrationTests/appsettings.test.json` so `ValidateOnStart` passes; those tests never call Maxio. This project is run and must be green. |
| 12 | Ordering & no-op side effects | The integration is **stateless** (Maxio is the record of truth; no local mapping row — the in-memory DB could not durably hold one anyway). We do **not** persist the provider response as a first local write. Subscribe's create is **gated** on a prior `FindSubscription` (idempotent hit returns without a second create); **no** external notification is emitted after the transition. |
| 13 | Unknown outcomes | On transport failure after a write may have been received: `CreateCustomer` failure → re-read `ReadCustomerByReference(reference)`; `CreateSubscription` failure → re-read `FindSubscription(reference)`. The create-with-reconcile helper catches both `SdkException` and transport faults and re-reads by reference before surfacing failure. |
| 14 | Provider status & reconciliation clocks | `CreateSubscription`/`ListCustomerSubscriptions` return `Subscription.State`. Code branches on it: `active`/`trialing` → success; `awaiting_signup`/`pending` → surfaced as *pending*; `failed_to_create`/`canceled` → surfaced as *failure*. No `state ?? "active"` default. Single source (Maxio) → no two-source reconciliation clock. |

## 6. Assumptions & Blockers

- **Assumption (minor):** caller identity for the Maxio customer `reference` is the JWT `ClaimTypes.Name` (username/email) — stable per eShop user; namespaced as-is (username) for the customer reference and `eshop:{username}:{handle}` for the subscription reference. Proceeding.
- **Assumption (minor):** default subscribe target when the caller omits a plan handle = `Maxio:DefaultPlanHandle` if configured, else the configured product family's Pro plan is not assumed — the caller supplies the handle; POST validates it against the family list. Proceeding (endpoint requires an explicit `planHandle`; unknown → 400).
- **UNVERIFIED → CONFIRMED (live):** Maxio subscription `reference` create-time uniqueness. The SDK exposes a `FindSubscription(reference)` single-match lookup; the `CreateSubscription` remarks do not state duplicate rejection, so this was planned as `UNVERIFIED` with a defensive design (deterministic reference, `FindSubscription` before create, and reconcile-by-reference on any create error). **A live concurrency test — 6 simultaneous subscribe requests for one user/plan — produced exactly one `201` and five idempotent `200`s all returning the same subscription id, and exactly one persisted subscription (and one customer).** So the provider does enforce reference uniqueness in practice; the defensive design holds regardless.
- **Collection method (implementation finding):** the seeded plans require no payment method, but the provider default collection method (`automatic`) still attempts an immediate charge and returns 422 "No payment method was on file" on subscribe. The service therefore sets `PaymentCollectionMethod` (config `Maxio:PaymentCollectionMethod`, default `remittance`) so subscriptions are invoiced rather than charged — verified live (subscribe returns an `active` subscription with no card).
- **No blockers.** Every capability in scope (list family products, read/create customer, find/create subscription, list customer subscriptions) is a mapped operation.
