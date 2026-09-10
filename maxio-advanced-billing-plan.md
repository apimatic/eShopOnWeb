# Maxio Advanced Billing — integration plan (eShopOnWeb subscription billing)

Additive, parallel capability: recurring-subscription billing with Maxio as system of record.
Exposed as JWT-authenticated HTTP endpoints on `src/PublicApi`. Caller identity comes from the
JWT (`ClaimTypes.Name` = eShop username/email; see `IdentityTokenClaimService`).

## 1. Scope & sequence

Layering (matches eShop: interface in ApplicationCore, impl in Infrastructure, endpoints in PublicApi):

1. **Vendor the SDK** (not on NuGet) into `src/Maxio.AdvancedBilling.Sdk/` and add a `ProjectReference`
   from `Infrastructure` (the only project that touches SDK types). ApplicationCore stays SDK-free.
2. **Config + fail-fast DI**: `MaxioOptions` bound from `Maxio:` section (keys below). A registration
   extension in Infrastructure validates every credential part is present, builds the SDK client via
   `services.AddMaxioAdvancedBillingClient(...)`, sets Basic auth + server site/base-url.
3. **Domain abstraction**: `ISubscriptionBillingService` (ApplicationCore) with plain DTOs
   (`SubscriptionPlan`, `CustomerSubscriptionInfo`, `SubscribeResult`) — no SDK types leak out.
4. **Implementation** `MaxioSubscriptionBillingService` (Infrastructure) — maps the operations below.
5. **Endpoints** (PublicApi, `IEndpoint` minimal-API convention like CatalogItemEndpoints):
   - `GET  /api/subscription-plans`  → list plans in the configured product family (op #2 after #1).
   - `POST /api/subscriptions`       → ensure customer (#3/#4) → idempotent subscribe (#5/#6).
   - `GET  /api/my-subscriptions`    → the caller's subscriptions (#3 read + #7).

### Operation sequence for the hero "Subscribe" flow (POST /api/subscriptions)
- Resolve family id (op1, cached) is NOT needed for subscribe (subscribe uses product **handle**).
- **Ensure customer (idempotent):** ReadCustomerByReference(ref) → on 404 CreateCustomer. ref is
  deterministic from the username so a re-run/double-click maps to the same customer.
- **Idempotent subscribe:** FindSubscription(subRef) → if a live subscription exists, return it;
  else CreateSubscription with `reference = subRef`, `customer_id`, `product_handle`.
- Serialize per-user with an in-process keyed lock (see §5 row 5) to close the check-then-create race
  within a run.

## 2. CONTRACT SHEET

⚠ Signatures below are **generated code, verbatim** — every parameter name is the literal C#
identifier; the cancellation-token parameter is named `ct`, so named arguments write `ct:`.
⚠ Every SDK type is written **fully-qualified with the namespace its source path implies**
(`Models/` → `MaxioAdvancedBilling.Models`, `Models/Enums/` → `…Models.Enums`,
`Errors/` → `…Errors`, root → `MaxioAdvancedBilling`, `Api/` → `…Api`).

| # | Controller · method (verbatim signature) | Request model + fields used | Response envelope + fields read | Error case + accessors | Pag. | Source |
|---|---|---|---|---|---|---|
| 1 | `client.ProductFamilies` · `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — (pass all `null`) | `IReadOnlyList<ProductFamilyResponse>`; each `.ProductFamily` → `Id (id): int?`, `Handle (handle): string?` | **Case B** `SdkException<RawError>` (StatusCode, ReadAsString) | none | map/operations/ProductFamilies.md · Models/ProductFamilyResponse.cs · Models/ProductFamily.cs |
| 2 | `client.ProductFamilies` · `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `productFamilyId` = **numeric family id as string** (handle 404s — verified live); pass `includeArchived: false`; `perPage: 200` | `IReadOnlyList<ProductResponse>`; each `.Product` → `Id`, `Name`, `Handle`, `Description`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `ProductFamily.Handle` | **Case A** `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | page/perPage | map/operations/ProductFamilies.md · Models/ProductResponse.cs · Models/Product.cs |
| 3 | `client.Customers` · `ReadCustomerByReference(string reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `reference` = deterministic per-user key | `CustomerResponse.Customer` (required) → `Id`, `FirstName`, `LastName`, `Email`, `Reference` | **Case B** `SdkException<RawError>`; **404 = not-found control flow** (StatusCode==NotFound) | none | map/operations/Customers.md · Models/CustomerResponse.cs · Models/Customer.cs |
| 4 | `client.Customers` · `CreateCustomer(CreateCustomerRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateCustomerRequest{ Customer = CreateCustomer{ required FirstName, required LastName, required Email, Reference } }` | `CustomerResponse.Customer` → `Id` | **Case A** `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError` [fallback] | none | map/operations/Customers.md · Models/CreateCustomerRequest.cs · Models/CreateCustomer.cs |
| 5 | **(idempotency anchor — implemented via op #7, not FindSubscription)** — a live subscription for (customer, plan) is found by scanning `ListCustomerSubscriptions`. FindSubscription(reference) was rejected as the anchor: **Maxio enforces reference uniqueness (verified live: 422 "Reference: must be unique")**, so a canceled subscription keeps its reference forever and re-subscribe cannot reuse it. | — | see #7 | — | none | `YOUR CALL — not in the map` (application idempotency design) |
| 6 | `client.Subscriptions` · `CreateSubscription(CreateSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateSubscriptionRequest{ Subscription = CreateSubscription{ ProductHandle (product_handle), CustomerId (customer_id), Reference (reference), PaymentCollectionMethod (payment_collection_method) = CollectionMethod.Remittance } }` — all optional; **Remittance is required so enrollment succeeds without a stored card** (verified live: automatic collection → 422 "No payment method was on file"); Reference is a **unique** value (uniqueness enforced, see #5) | `SubscriptionResponse.Subscription` → see #7 | **Case A** `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | none | map/operations/Subscriptions.md · Models/CreateSubscription.cs · Models/Enums/CollectionMethod.cs |
| 7 | `client.Customers` · `ListCustomerSubscriptions(int customerId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `customerId` from op #3 | `IReadOnlyList<SubscriptionResponse>`; each `.Subscription` → `Id`, `State (state): SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentPeriodEndsAt`, `NextAssessmentAt`, `ActivatedAt`, `CurrentPeriodStartedAt`, `Product` (Handle/Name/PriceInCents/Interval/IntervalUnit), `Reference`? (n/a — read from create) | **Case B** `SdkException<RawError>` | none | map/operations/Customers.md · Models/Subscription.cs |

**Enums:** reads (`SubscriptionState`, `IntervalUnit`) use `.Value` for the wire string — **not
`.ToString()`, which returns the record debug form** (per dotnet-models). One enum is *written*:
`CollectionMethod.Remittance` on CreateSubscription. All `StringEnum<T>`. Source:
Models/Enums/SubscriptionState.cs, IntervalUnit.cs, CollectionMethod.cs, Core/Enum/TypedEnum.cs.

**Client construction / auth / server (source: sdk-map.md "Getting a client" + "Servers & auth"):**
- Ctor: `new MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)`; DI helper
  `services.AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>)` (source:
  ServiceCollectionExtensions.cs — uses `IHttpClientFactory`, registers client **singleton**, builds
  options **once at registration**).
- Auth: **Basic** — `options.BasicAuth = new BasicAuthCredentials { Username = <API key>, Password = "x" }`
  (map: username = Chargify API key, password = `x`). `BasicAuthCredentials` ns
  `MaxioAdvancedBilling.Core.Http.Configuration`? → confirm at edit time from `AuthSchemes.cs` / map
  example (map shows `new BasicAuthCredentials { … }`).
- Environment: `options.Environment = ServerEnvironment.Us` (ns `MaxioAdvancedBilling.Servers`).
- Base address: default template `https://{site}.chargify.com`; set
  `options.Server.Production.Us.Site = <subdomain>`. If `Maxio:BaseUrl` set, use it verbatim via
  `options.Server.Production.Us.BaseUrl = <baseUrl>`. Source: ServerOptions.cs, Servers/ProductionOptions.cs.

**Configuration keys (bind from `Maxio:` — values NEVER in repo; from user-secrets):**
`Maxio:ApiKey` (←MAXIO_API_KEY), `Maxio:Subdomain` (←MAXIO_SITE_SUBDOMAIN),
`Maxio:ProductFamilyHandle` (←MAXIO_DEFAULT_PRODUCT_FAMILY), `Maxio:BaseUrl` (optional override).

## 3. Trap notes (name the hazard; do not resolve)

- **Basic-auth wiring / password="x"** — credential set on options before client build; a skipped
  credential is sent as no-auth, surfacing as 401 not an exception at set time. `MUST load dotnet-authentication`.
- **Client/HttpClient lifetime + DI singleton capture** — the client must be long-lived; how the DI
  helper owns the HttpClient vs a hand-rolled factory decides socket/rotation behaviour.
  `MUST load dotnet-client-initialization`.
- **List ops with many nullable-no-default params** — positional mis-binding; named args required.
  `MUST load dotnet-calling-endpoints`.
- **Response envelope + StringEnum + required members** — `.Subscription` is nullable, `.Customer`/
  `.Product` are `required`; enums are not C# enums. `MUST load dotnet-models`.
- **Two-directional `JsonException` + Case A/B ladder + 404-as-control-flow** — see REQUIRED READING.
  `MUST load dotnet-error-handling`.
- **Timeout is per-attempt; POST not retried by default; unredacted body logging** — bounding the whole
  subscribe call and keeping secrets out of logs. `MUST load dotnet-configuration-resilience`.
- **SDK seam for tests is the HttpClient** — fake the transport, not SDK internals. `MUST load dotnet-testing`.

## 4. REQUIRED READING (load every one BEFORE implementation; this sheet omits their contents)

| Skill (load plugin-qualified `maxio-platforms-team:<name>`) | Governs |
|---|---|
| `dotnet-client-initialization` | client construction + DI registration (step 2/impl) |
| `dotnet-authentication` | Basic-auth credentials (step 2) |
| `dotnet-calling-endpoints` | every `client.X.Op(...)` call (impl) |
| `dotnet-models` | building request bodies + reading envelopes/enums (impl) |
| `dotnet-error-handling` | the try/catch boundary (impl) — always required |
| `dotnet-configuration-resilience` | retries/timeouts/base-url/logging (step 2 + boundary) |
| `dotnet-testing` | integration tests |

**Two mandatory `JsonException` hazards (`System.Text.Json.JsonException` reaches the boundary from
two directions, needing opposite handling):**
1. A drifted/malformed **2xx** body (a missing `required` member — e.g. `CustomerResponse.Customer`
   absent) surfaces as `JsonException` from deserialization, **not** as `SdkException`; an
   SDK-exception-only catch ladder lets it escape. Catch `JsonException` at the boundary.
2. A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
   `JsonException` **while the error object is constructed**, replacing the `SdkException` and
   destroying the HTTP status. Catch it and treat as an upstream/bad-gateway failure.

## 5. PRODUCTION READINESS

| # | Concern | Decision |
|---|---|---|
| 1 | Credential fail-fast | `MaxioOptions` validated in the DI registration extension **at startup**: throws `InvalidOperationException` if `ApiKey` or `Subdomain` or `ProductFamilyHandle` is null/blank (each part checked independently — a blank part is not a missing one). Host refuses to start rather than 401 on first call. |
| 2 | Secret sourcing & rotation | Secrets live in **.NET user-secrets** (`Maxio:*`), loaded from env vars by me; never in repo files. DI helper builds options **once at registration** and captures them in the singleton client → rotation requires a process restart. Documented; acceptable for a reference app (`YOUR CALL — not in the map`). |
| 3 | Total timeout budget | SDK `Timeout` is **per-attempt**. The only whole-call bound is a `CancellationToken` deadline. Impl: derive a linked CTS with a total budget (e.g. 30s) in the service and pass `ct:` to every SDK call; the HTTP request's own `HttpContext.RequestAborted` also flows in. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS, so `CreateCustomer`/`CreateSubscription` (**POST**) are **never resent by the SDK** — no risk of SDK-driven duplicate writes. GET ops (lists, reads) are retried, which is safe. Keep default retry policy. |
| 5 | Idempotency & ambiguous writes | No caller-supplied idempotency-key param exists on these writes (map rows show none; the injected `Idempotency-Key` GUID header is not one). Reconciliation instead: **customer** deduped by deterministic `reference` (ReadCustomerByReference before CreateCustomer). **subscription** deduped by a **live-subscription-for-plan** check (ListCustomerSubscriptions, filter live state + product handle) before CreateSubscription — return the existing live one. (Reference-based dedup was rejected: Maxio enforces reference uniqueness — verified live — so a canceled reference cannot be reused; the subscription reference is therefore made unique per create.) In-process `SemaphoreSlim` keyed by user closes the check-then-create race within a run; the live-check also holds across restarts since it queries Maxio. `YOUR CALL — not in the map`. |
| 6 | Observability | Log at Information: subscribe start/outcome with correlationId + Maxio customer id / subscription id. Log at Warning/Error on SDK failures with the provider status + `RawError.ReadAsString()` body (customer/subscription payloads carry no card/PII here — see row 7). `LogRequestBody` left **off**. |
| 7 | Sensitive data | Request models in scope (`CreateCustomer`: name/email/address; `CreateSubscription`: handle/id/reference) carry **no card or bank data** — payment method not required, no `credit_card_attributes` set. Still: `LogRequestBody` stays off and `options.Logging.LoggerFactory` is assigned explicitly so the `MAXIOADVANCEDBILLINGCLIENT_LOG` env var cannot force body logging on. Email is personal data — not echoed into logs beyond customer id. |
| 8 | Environment selection | Sandbox only. `ServerEnvironment.Us` → `https://{site}.chargify.com` with `{site}` = configured subdomain (`cp-exp-6` sandbox). No live/prod URL is ever configured; `Maxio:BaseUrl` override lets a different site be targeted without code change. Gateway/EU groups untouched. |

## 6. Assumptions & Blockers

- **Blockers: none.** All operations needed exist in the map; credentials verified live (200 on
  product_families / customers / products).
- Assumptions (minor, proceed):
  - "Plans" = the products in the configured product family. Metered `api-call` component is out of
    scope for the hero flow (not needed to subscribe).
  - The `POST /api/subscriptions` body carries an optional `planHandle`. When present it must match a
    plan in the configured family (validated server-side against the live plan list — no hard-coded
    id/handle). When omitted, the **first plan in the configured family** is used (rather than a
    hard-coded `eshop-pro`, which would break on a different catalog).
  - Per-user identity key = JWT `ClaimTypes.Name` (username/email). Customer `reference` and
    subscription `reference` derived deterministically from it so idempotency survives the ephemeral
    in-memory user id.
  - Names for CreateCustomer (Identity has no first/last name): FirstName = email local-part,
    LastName = "eShopOnWeb". Email = username.

## 7. Source labels
Every row's Source cell cites its map page and declaring source file (all read this session).
Rows 2/5/7/8 of §5 that are application decisions are marked `YOUR CALL — not in the map`.
