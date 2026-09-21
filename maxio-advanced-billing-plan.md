# Maxio Advanced Billing integration plan — eShopOnWeb subscription billing

Additive, parallel subscription capability on `src/PublicApi` (JWT). Maxio Advanced Billing is
the system of record; the eShop user ↔ Maxio customer mapping lives in Maxio (customer
`reference` = the eShop user id), so it survives the in-memory-DB restart caveat with no local
persistence. SDK is **not on any feed** → vendored from source and built as a project reference.

## 1. Scope & sequence

| # | Step | SDK operations |
| --- | --- | --- |
| 1 | Vendor SDK source into repo (`third-party/maxio-advanced-billing-sdk/`, CPM opted out), ProjectReference from `Infrastructure`. | — |
| 2 | `MaxioSettings` POCO (`Maxio:` section) + fail-fast validation + client DI (Basic auth, env, site/base-url). | client construction |
| 3 | `ISubscriptionBillingService` (ApplicationCore, pure domain types) + `MaxioBillingService` (Infrastructure) mapping SDK↔domain. | — |
| 4 | List plans = products in the configured family (resolve handle→numeric id first, then list by id). | `ProductFamilies.ListProductFamilies`, `ProductFamilies.ListProductsForProductFamily` |
| 5 | Ensure customer (idempotent by `reference`=userId). | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer` |
| 6 | Subscribe (idempotent: reuse existing live sub to same product, else create). | `Customers.ListCustomerSubscriptions`, `Subscriptions.CreateSubscription` |
| 7 | List my subscriptions. | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |
| 8 | Three `IEndpoint` HTTP endpoints on PublicApi (JWT). | — |
| 9 | Tests (HttpClient seam) + build/run self-verify. | — |

Idempotency serialized per-user with an in-process keyed `SemaphoreSlim` (single-process host).

## 2. CONTRACT SHEET

> ⚠ Signatures below are **generated code, verbatim** — every parameter name is the literal C#
> identifier; named arguments must use them exactly (the cancellation-token parameter is `ct`, so
> write `ct:`). List/filter operations have many nullable params with **no C# default** → pass
> `null` explicitly; call them with **named arguments**.
> ⚠ Every SDK type is written **fully-qualified** with the namespace its source path implies
> (`Models/`→`MaxioAdvancedBilling.Models`, `Models/Enums/`→`.Models.Enums`,
> `Api/`→`.Api`, `Errors/`→`.Errors`, root→`MaxioAdvancedBilling`,
> `Core/`→`MaxioAdvancedBilling.Core`), taken from THAT type's own path.

### Operations

| Op | Signature (verbatim) | Request → fields used | Response envelope → inner fields read | Error case | Source |
| --- | --- | --- | --- | --- | --- |
| `client.ProductFamilies.ListProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, RequestOptions? requestOptions = null, CancellationToken ct = default)` | all `null` | `IReadOnlyList<ProductFamilyResponse>` → each `.ProductFamily` (`Id`, `Handle`) — used to resolve the configured handle to its numeric id | **B** `SdkException<RawError>` | map/operations/ProductFamilies.md; Models/ProductFamilyResponse.cs; Models/ProductFamily.cs |
| `client.ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)` | **VERIFIED on the wire:** `productFamilyId` must be the **numeric family id** (the handle 404s on this route), resolved via `ListProductFamilies`; `includeArchived: false`; `perPage: 200`; rest `null` | `IReadOnlyList<ProductResponse>` → each `.Product` (`Id`, `Handle`, `Name`, `Description`, `PriceInCents`, `Interval`, `IntervalUnit`) | **A** typed `ListProductsForProductFamilyError`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | map/operations/ProductFamilies.md; Models/ProductResponse.cs; Models/Product.cs |
| `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `reference` = eShop user id | `CustomerResponse` → `.Customer` (`Id`, `Reference`, `Email`) | **B** `SdkException<RawError>` (404 = not found → `.StatusCode`) | map/operations/Customers.md; Models/CustomerResponse.cs; Models/Customer.cs |
| `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | body must pass explicitly | `CustomerResponse` → `.Customer.Id` | **A** `CreateCustomerError`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError` [fallback] | map/operations/Customers.md; Models/CreateCustomerRequest.cs |
| `client.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `customerId` (int) | `IReadOnlyList<SubscriptionResponse>` → each `.Subscription` | **B** `SdkException<RawError>` | map/operations/Customers.md; Models/SubscriptionResponse.cs |
| `client.Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | body must pass explicitly | `SubscriptionResponse` → `.Subscription` | **A** `CreateSubscriptionError`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | map/operations/Subscriptions.md; Models/CreateSubscriptionRequest.cs |

### Request model shapes (fields actually set)

- **`CreateCustomerRequest`** (`Models/CreateCustomerRequest.cs`): `Customer (customer): CreateCustomer, required`.
- **`CreateCustomer`** (`Models/CreateCustomer.cs`): `FirstName (first_name): string, required` · `LastName (last_name): string, required` · `Email (email): string, required` · `Reference (reference): string?` (← eShop user id). All other fields optional; left out.
- **`CreateSubscriptionRequest`** (`Models/CreateSubscriptionRequest.cs`): `Subscription (subscription): CreateSubscription, required`.
- **`CreateSubscription`** (`Models/CreateSubscription.cs`) — **nothing is `required`**; the endpoint's own acceptance rules (from `<remarks>`) drive what to set:
  - product: `ProductHandle (product_handle): string?` (doc: "Required, unless a product_id is given") → set to requested plan handle.
  - customer: `CustomerId (customer_id): int?` (doc: "Required, unless a customer_reference or customer_attributes is given") → set to the ensured customer's `Id`.
  - `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` → set to **`remittance`** (configurable via `Maxio:PaymentCollectionMethod`, default `remittance`). **VERIFIED on the wire:** with the default `automatic` collection, create is rejected 422 *"No payment method was on file for the $X balance"* (there is no stored card); `remittance` makes the balance an invoice and creates an **active** subscription with no card. Legacy sites may need `invoice`.
  - deliberately left out: all payment-profile/card fields (no card capture in scope), `Reference` (subscription-level — NOT used for idempotency; avoids unique-reference collisions on re-subscribe after cancel).

### Response fields read (domain mapping)

- `Product`: `Id:int?`, `Handle:string?`, `Name:string?`, `Description:string?`, `PriceInCents:long?`, `Interval:int?`, `IntervalUnit:IntervalUnit?`.
- `Subscription`: `Id:int?`, `State:SubscriptionState?`, `ProductPriceInCents:long?`, `CurrentPeriodEndsAt:DateTimeOffset?`, `NextAssessmentAt:DateTimeOffset?` (next-billing-date; tracks period end, diverges on failed renewal), `CreatedAt:DateTimeOffset?`, nested `.Product` (Handle/Name), nested `.Customer`.
- `Customer`: `Id:int?`, `Reference:string?`, `Email:string?`.
- Enum wire value for output via `.Value` (`StringEnum<T>.Value`, e.g. `SubscriptionState.Active.Value == "active"`).

### Enums

- `SubscriptionState` (`Models/Enums/SubscriptionState.cs`), `StringEnum` — members incl. `Active` `Trialing` `Assessing` `Pending` `PastDue` `SoftFailure` `Unpaid` `OnHold` `Paused` `Suspended` `AwaitingSignup` (live/problem) and `Canceled` `Expired` `FailedToCreate` `TrialEnded` (terminal / re-subscribable). Read `.Value`.
- `IntervalUnit` (`Models/Enums/IntervalUnit.cs`): `Day`("day"), `Month`("month"). Read `.Value`.
- `CollectionMethod` (`Models/Enums/CollectionMethod.cs`): not set (default automatic). Listed for reference only.

### Client construction / auth / server (source: sdk-map.md *Getting a client*/*Servers & auth*; ServerOptions.cs; ProductionOptions.cs; ServiceCollectionExtensions.cs)

- `services.AddMaxioAdvancedBillingClient(options => …)` (namespace `MaxioAdvancedBilling`) — registers a **singleton** client over `IHttpClientFactory`.
- Auth = **Basic**: `options.BasicAuth = new BasicAuthCredentials { Username = <API key>, Password = "x" }` (map: username = Chargify API key, password = `x`). Basic works with `ServerEnvironment.Us`.
- `options.Environment = ServerEnvironment.Us` (namespace `MaxioAdvancedBilling.Servers`; `MAXIO_ENVIRONMENT=US`).
- Server address: if `Maxio:BaseUrl` set → `options.Server.Production.Us.BaseUrl = <BaseUrl>` verbatim (no `{site}` token → used as-is). Else `options.Server.Production.Us.Site = <Subdomain>` → resolves `https://{site}.chargify.com`.
- `BasicAuthCredentials` namespace: root `MaxioAdvancedBilling` (auth types); confirm at edit time.

## 3. Trap notes (hazard + skill; not resolved here)

- **Client/HttpClient lifetime & DI shape** — the client must reuse a factory-managed `HttpClient`, not one rebuilt per request; how `AddMaxioAdvancedBillingClient` captures options decides rotation behaviour. **MUST load dotnet-client-initialization.**
- **Basic-auth wiring** — a credential never set is silently skipped and the request still goes (a 401 can mean *no* credential sent, not a bad one); where to set it in the DI callback. **MUST load dotnet-authentication.**
- **List-op argument binding** — `ListProductsForProductFamily`/`ListCustomerSubscriptions` have many no-default nullable params that mis-bind positionally. **MUST load dotnet-calling-endpoints.**
- **Model building & enum reading** — `StringEnum<T>` is not a C# enum; records are `init`-only with `required`; how to read the wire value safely for output. **MUST load dotnet-models.**
- **Error boundary** — Case A vs Case B per operation; and the two `JsonException` directions below. **MUST load dotnet-error-handling.**
- **Retry / timeout / logging / base-url** — which writes the SDK may resend, what `Timeout` actually bounds, and that `LogRequestBody` logs JSON unredacted / the env-var log switch. **MUST load dotnet-configuration-resilience.**
- **Test seam** — the `HttpClient` ctor arg is the fake seam; match the repo's xUnit style. **MUST load dotnet-testing.**

## 4. REQUIRED READING (load before implementation starts; contents deliberately not copied here)

| Skill (plugin `maxio-platforms-team`) | Governs |
| --- | --- |
| `maxio-platforms-team:dotnet-client-initialization` | Step 2 — client + DI + HttpClient lifetime |
| `maxio-platforms-team:dotnet-authentication` | Step 2 — Basic-auth credentials |
| `maxio-platforms-team:dotnet-calling-endpoints` | Steps 4–6 — calling list/create ops, named args |
| `maxio-platforms-team:dotnet-models` | Steps 3–6 — building requests, reading enums/unions |
| `maxio-platforms-team:dotnet-error-handling` | All call sites — error boundary |
| `maxio-platforms-team:dotnet-configuration-resilience` | Step 2 — retries, timeout budget, logging, base URL |
| `maxio-platforms-team:dotnet-testing` | Step 9 — HttpClient test seam |

**Two mandatory `JsonException` hazards (both bypass an SDK-exception-only catch ladder):**
1. A drifted/malformed **2xx** body (a missing `required` member) surfaces as `System.Text.Json.JsonException` from deserialization — **not** an `SdkException`; an SDK-only catch lets it escape.
2. A **non-2xx** body that doesn't match its operation's generated `{Operation}Error` shape throws `JsonException` **while the error object is being constructed**, so it **replaces** the `SdkException` and the HTTP status is lost with it.
→ The error boundary must also catch `JsonException` (and a final `Exception`) and map to `502 Bad Gateway`, never leak SDK/JSON internals to the API caller.

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `MaxioSettings` validated at DI registration in Infrastructure: throw `InvalidOperationException` if `ApiKey`, `Subdomain`, or `ProductFamilyHandle` is null/whitespace (each part checked; blank ≠ missing). `BaseUrl` optional. Host refuses to start rather than 401 on first call. |
| 2 | Secret sourcing & rotation | Values come from env vars → loaded into **.NET user-secrets** (never in repo). `appsettings.json` carries only empty `Maxio` keys as a schema hint. `AddMaxioAdvancedBillingClient` builds options **once at registration** and captures them in the singleton → a rotated key needs a process restart (acceptable for this demo; documented). |
| 3 | Total timeout budget | SDK `Timeout` is **per-attempt**; a whole call is bounded only by a `CancellationToken` deadline. Endpoints pass the request-aborted token through; the service wraps each SDK call chain with a linked `CancellationTokenSource` (≈100 s overall budget) so a hung retryable GET cannot stack per-attempt timeouts unbounded. Decided against SDK default reliance per dotnet-configuration-resilience. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS → `POST` (`CreateCustomer`, `CreateSubscription`) is **never** resent by the SDK. Reads (`ReadCustomerByReference`, `ListCustomerSubscriptions`, `ListProductsForProductFamily`) are GET → safely retried. No PUT in scope. |
| 5 | Idempotency & ambiguous writes | `CreateCustomer`/`CreateSubscription` take **no** caller-supplied idempotency key (map rows show none; the generator's per-call `Idempotency-Key: Guid.NewGuid()` header is not one). Reconciliation path instead: **customer** deduped by `reference`=userId (read-before-create; on create-race 422 re-read by reference); **subscription** deduped by listing the customer's subscriptions and reusing any live (non-terminal) sub to the same product before creating. Whole subscribe serialized per-user by in-process `SemaphoreSlim` (single-process host). |
| 6 | Observability | Structured `ILogger` in the service: Information on ensure-customer / create-subscription / idempotent-reuse (ids + plan handle, no secrets); Warning on 422 with the provider messages; Error on unexpected. Provider correlation: the `RawError`/typed-error messages are logged. `LogRequestBody` left **off**. |
| 7 | Sensitive data | Scope carries **no** card/bank/PII beyond the user's email + name (already in the app's own Identity store). No card capture (payment-method-not-required plans). `LogRequestBody` stays off and `options.Logging.LoggerFactory` is set explicitly via DI (the extension assigns `ILoggerFactory`) so `MAXIOADVANCEDBILLINGCLIENT_LOG` cannot force body logging from outside code. Our own logs never echo request bodies. |
| 8 | Environment selection | Groups: `Production` (billing calls in scope), `Ebb` (events — not used), `Oauth` (gateway — not used). Deployment sets `ServerEnvironment.Us` + `Maxio:Subdomain` (sandbox site `cp-exp-1`), or `Maxio:BaseUrl` override. **Sandbox isolation:** this SDK declares no dedicated "sandbox" environment; test traffic is kept off live systems by pointing `Subdomain`/`BaseUrl` at the sandbox site only — never a production site — via config, and by seeding no data (catalog pre-seeded). |

## 6. Assumptions & Blockers

- **No Blockers.** Every capability the hero flow needs is on the map.
- Assumption: the eShop user id (`ApplicationUser.Id`, GUID string) is the stable customer `reference`. JWT carries only `ClaimTypes.Name` (= username = email); the endpoint resolves the `ApplicationUser` via `UserManager` to get `Id`/`Email`. `YOUR CALL — not in the map` (application design).
- Assumption: `CreateCustomer` requires first/last name; `ApplicationUser` (bare `IdentityUser`) has none → derive FirstName = email local-part, LastName = `"(eShopOnWeb)"`. `YOUR CALL — not in the map`.
- Assumption: a plan = a Maxio product in the configured family; subscribe validates the requested handle is one of them (else 400). `YOUR CALL — not in the map`.
- Assumption: "next billing date" surfaced = `Subscription.NextAssessmentAt` (falls back to `CurrentPeriodEndsAt`). Source: Models/Subscription.cs remarks.
- **Verified during self-test (against sandbox `cp-exp-1`):** the `products.json` route requires the numeric family id, not the handle (the handle 404s), so the handle is resolved to an id via `ListProductFamilies` and cached; and subscribe requires `payment_collection_method: remittance` to create without a stored card. The seeded family id was `3026728` (the task table's `3023074` was stale, as warned — handles are the stable key, ids are resolved live).
