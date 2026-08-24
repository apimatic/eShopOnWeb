# Maxio Advanced Billing integration plan — eShopOnWeb `src/PublicApi`

Recurring-subscription billing (parallel to the existing one-time flow) with Maxio Advanced Billing
as the billing system of record. Endpoints: `GET /api/subscription-plans`, `POST /api/subscriptions`,
`GET /api/my-subscriptions` (JWT-authenticated). Sandbox site `cp-exp-1`, product family
`eshop-subscribe`, plans `eshop-pro` ($299.00/mo) and `basic-plan` ($29.00/mo).

## 1. Scope & sequence

| # | Step | SDK operations used |
|---|------|---------------------|
| 1 | Add NuGet package `AsadAli.AdvancedBilling.Sdk` to `src/PublicApi` | — |
| 2 | Bind `Maxio:` config section (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl?`); register the SDK client in DI with Basic auth + server/site or BaseUrl override | — (client construction, §2.1) |
| 3 | `GET /api/subscription-plans`: resolve family `eshop-subscribe` → id, list its products, map to plan DTOs (handle, name, price, interval) | `ProductFamilies.ListProductFamilies`, `ProductFamilies.ListProductsForProductFamily` (optional: `Products.ReadProductByHandle`) |
| 4 | Customer-ensure service (idempotent): lookup by `reference` = eShopOnWeb user id; create on 404; on create-422 race re-read by reference | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer` |
| 5 | `POST /api/subscriptions` (hero): ensure customer (step 4) → idempotency pre-check by deterministic subscription `reference` → create subscription by `product_handle` + `customer_id` → return plan/price/state/next-billing | `Subscriptions.FindSubscription`, `Subscriptions.CreateSubscription` (+ step 4) |
| 6 | `GET /api/my-subscriptions`: resolve customer by reference (404 ⇒ empty list), list their subscriptions, map state/price/next-billing | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |
| 7 | Error boundary: translate SDK exceptions + `JsonException` to HTTP problem responses | (§2.5) |
| 8 | Tests for the integration layer via the SDK's HttpClient seam | — |

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal
> C# identifier. The cancellation-token parameter really is named `ct`: in named
> arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take
> each one from that type's own map row, never from where a neighbouring type sits. A members
> table names the namespace outright; otherwise the row's source path implies it
> (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root
> namespace). Enums, unions, auth, server and client-config types are spread across different
> child namespaces, and two types configured side by side in the same options object routinely
> live in different ones. Dropping a type to the root or to `.Models` makes the implementer
> guess the wrong `using`, and the build breaks.

### 2.1 SDK identity, client construction, auth, server (map: `sdk-map.md`)

| Fact | Value |
|---|---|
| NuGet package | `AsadAli.AdvancedBilling.Sdk` — `dotnet add package AsadAli.AdvancedBilling.Sdk` (map grounded at tag `v1.0.2`, commit `15db14b`; if NuGet serves newer, the compiler is the backstop) |
| Root namespace | `MaxioAdvancedBilling` (≠ package id) |
| Client class | `MaxioAdvancedBillingClient.MaxioAdvancedBillingClient` — only ctor: `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` |
| Options class | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` — properties: `Environment: ServerEnvironment`, `Retry: RetryOptions`, `Server: ServerOptions`, `BasicAuth: BasicAuthCredentials?` |
| DI alternative | `services.AddMaxioAdvancedBillingClient(o => { … })` (`ServiceCollectionExtensions.cs`, root namespace) |
| Auth (Basic) | `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials` — `Username` = **API key**, `Password` = **literal `"x"`** |
| Environment | `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default) → `https://{site}.chargify.com`; `.Eu` → `https://{site}.ebilling.maxio.com`. **No sandbox environment exists** — sandbox = a site in test mode; use `Us` + site `cp-exp-1` |
| Site (subdomain) | `options.Server.Production.Us.Site = "cp-exp-1"` (`ServerOptions`/`ProductionOptions` under `Servers/` ⇒ namespace `MaxioAdvancedBilling.Servers`) |
| Base-URL override | `options.Server.Production.Us.BaseUrl = <Maxio:BaseUrl>` — verbatim replacement of the whole base URL; when set it wins over `Site` (set only one) |
| Retry/timeout | `options.Retry` is `MaxioAdvancedBilling.Core.Configuration.RetryOptions`; **all 9 members are C# `required`** — start from `RetryOptions.Default()` and mutate, never `new` it bare. Members: `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout (TimeSpan?)`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry` |
| Config mapping | `Maxio:ApiKey`→`BasicAuth.Username` · `Maxio:Subdomain`→`Server.Production.Us.Site` · `Maxio:BaseUrl`→`Server.Production.Us.BaseUrl` (verbatim, optional) · `Maxio:ProductFamilyHandle`→app-level filter for step 3 |

### 2.2 Operations (one row per operation; all controller properties hang off the client)

**`client.ProductFamilies`** (map: `operations/ProductFamilies.md`)

| Operation | Signature (verbatim) | Returns | Error case + accessors | Pagination |
|---|---|---|---|---|
| ListProductFamilies | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 nullable params **must be passed explicitly** (pass `null`) | `IReadOnlyList<ProductFamilyResponse>` | **Case B** `SdkException<RawError>` | none |
| ListProductsForProductFamily | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 params `dateField…include` **must be passed explicitly** | `IReadOnlyList<ProductResponse>` | **Case A** `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage` |

**`client.Products`** (map: `operations/Products.md`)

| Operation | Signature | Returns | Error case | Pagination |
|---|---|---|---|---|
| ReadProductByHandle (optional — validate/display a single plan, e.g. default `eshop-pro`) | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | `ProductResponse` | **Case B** `SdkException<RawError>` | none |

**`client.Customers`** (map: `operations/Customers.md`)

| Operation | Signature | Returns | Error case | Pagination |
|---|---|---|---|---|
| ReadCustomerByReference | `ReadCustomerByReference(string reference, CancellationToken ct = default)` (query `reference`) | `CustomerResponse` | **Case B** `SdkException<RawError>` — 404 ⇒ `ex.Error.StatusCode == HttpStatusCode.NotFound` | none |
| CreateCustomer | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` **must be passed explicitly** | `CustomerResponse` | **Case A** `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none |
| ListCustomerSubscriptions | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | `IReadOnlyList<SubscriptionResponse>` | **Case B** `SdkException<RawError>` | none |

**`client.Subscriptions`** (map: `operations/Subscriptions.md`)

| Operation | Signature | Returns | Error case | Pagination |
|---|---|---|---|---|
| FindSubscription | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` **must be passed explicitly** (query `reference`) | `SubscriptionResponse` | **Case A** `SdkException<FindSubscriptionError>` — `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback] | none |
| CreateSubscription | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` **must be passed explicitly** | `SubscriptionResponse` | **Case A** `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none |

Contract notes:
- `Subscriptions.ListSubscriptions` exists but has **no customer filter** (filters: state/product/coupon/dates/metadata/sort only) — per-customer listing goes through `Customers.ListCustomerSubscriptions(customerId)`. (`operations/Subscriptions.md`)
- `ProductFamilies.ReadProductFamily(int id, …)` is **`int`-typed** — the API's `handle:my-family` format cannot be passed through it. Family-by-handle resolution = `ListProductFamilies` + client-side `Handle` match. (`operations/ProductFamilies.md`)
- All list endpoints here return **bare `IReadOnlyList<T>`** — no envelope, no total count; page until a short/empty page where pagination exists.

### 2.3 Models — fields with wire names (namespace `MaxioAdvancedBilling.Models` throughout)

| Model | Fields (sheet subset; full list on cited page) | Map page |
|---|---|---|
| `CreateCustomerRequest` | `Customer (customer): CreateCustomer` **`!req`** | `records-1-Ac-Cr.md` |
| `CreateCustomer` | `FirstName (first_name): string` **`!req`** · `LastName (last_name): string` **`!req`** · `Email (email): string` **`!req`** · `Reference (reference): string?` ← set to eShopOnWeb user id · optional: `Organization`, `Address`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `TaxExempt`… | `records-1-Ac-Cr.md` |
| `CustomerResponse` | `Customer (customer): Customer` **`!req`** | `records-2-Cr-Ne.md` |
| `Customer` (read) | `Id (id): int?` · `Reference (reference): string?` · `Email`, `FirstName`, `LastName: string?` | `records-2-Cr-Ne.md` |
| `ProductFamilyResponse` | `ProductFamily (product_family): ProductFamily?` — **nullable** | `records-3-Of-Su.md` |
| `ProductFamily` | `Id (id): int?` · `Handle (handle): string?` · `Name (name): string?` | `records-3-Of-Su.md` |
| `ProductResponse` | `Product (product): Product` **`!req`** | `records-3-Of-Su.md` |
| `Product` (plan fields) | `Id (id): int?` · `Name (name): string?` · `Handle (handle): string?` · `PriceInCents (price_in_cents): long?` · `Interval (interval): int?` · `IntervalUnit (interval_unit): IntervalUnit?` · `ArchivedAt (archived_at): DateTimeOffset?` · `ProductFamily (product_family): ProductFamily?` | `records-3-Of-Su.md` |
| `CreateSubscriptionRequest` | `Subscription (subscription): CreateSubscription` **`!req`** | `records-2-Cr-Ne.md` |
| `CreateSubscription` (hero subset) | `ProductHandle (product_handle): string?` ← `"eshop-pro"` / `"basic-plan"` · `ProductId (product_id): int?` (alternative; use handle) · `CustomerId (customer_id): int?` ← from ensured customer · `CustomerReference (customer_reference): string?` (alternative to id) · `Reference (reference): string?` ← **idempotency key** · `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` · `NextBillingAt (next_billing_at): DateTimeOffset?` | `records-2-Cr-Ne.md` |
| `SubscriptionResponse` | `Subscription (subscription): Subscription?` — **nullable** (unlike `ProductResponse.Product` / `CustomerResponse.Customer`, which are `!req`) | `records-4-Su-We.md` |
| `Subscription` (read subset) | `Id (id): int?` · `State (state): SubscriptionState?` · `ProductPriceInCents (product_price_in_cents): long?` · `NextAssessmentAt (next_assessment_at): DateTimeOffset?` · `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` · `Product (product): Product?` · `Customer (customer): Customer?` · `Reference (reference): string?` · `ActivatedAt (activated_at): DateTimeOffset?` · `CancelAtEndOfPeriod (cancel_at_end_of_period): bool?` | `records-3-Of-Su.md` |

**Next-billing-date fact:** the `Subscription` record carries **no `next_billing_at` field** — expose
`NextAssessmentAt ?? CurrentPeriodEndsAt` as the endpoint's `nextBillingDate`; both are nullable, so the
DTO must tolerate null. (`records-3-Of-Su.md`)

**Error-payload models** (namespace `MaxioAdvancedBilling.Models`; map `records-2-Cr-Ne.md`):
- `ErrorListResponse1` (CreateSubscription 422): `Errors (errors): IReadOnlyList<string>` **`!req`**.
- `CustomerErrorResponse1` (CreateCustomer 422): `Errors (errors): Errors?`, and the shared `Errors`
  record models only `PerPage (per_page)` / `PricePoint (price_point)` string lists — **a map-visible
  anomaly**: those keys don't correspond to customer fields. Directive: do **not** parse field-level
  customer errors from it; on any CreateCustomer 422 treat as "reference possibly taken" → re-read by
  reference; log the body via `TryGetRawError(out var raw)` / `raw.ReadAsString()`. Live 422 wire shape:
  `UNVERIFIED`.

### 2.4 Enums (namespace `MaxioAdvancedBilling.Models.Enums`; `StringEnum<T>`, not C# enums — map `enums.md`)

| Enum | Values (C# member ← wire) |
|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `IntervalUnit` | `Day (day)`, `Month (month)` |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` |
| `SubscriptionStateFilter` (optional, only if filtering `ListSubscriptions` later) | `Active`, `Canceled`, `Expired`, `ExpiredCards`, `OnHold`, `PastDue`, `PendingCancellation`, `PendingRenewal`, `Suspended`, `TrialEnded`, `Trialing`, `Unpaid` |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` |
| `ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)` — not needed for the plan list |
| `SortingDirection` | `Asc (asc)`, `Desc (desc)` |

### 2.5 Error boundary — what reaches a `catch` (map: `sdk-map.md` error-handling model)

- Every operation is **throw-only** (no `…Result` no-throw variants exist in this SDK).
- **Case A** (typed): `MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>` where `TError` is in
  `MaxioAdvancedBilling.Errors` — here: `CreateCustomerError`, `CreateSubscriptionError`,
  `FindSubscriptionError`, `ListProductsForProductFamilyError`. Read via `ex.Error.TryGet…(out …)`
  per §2.2; `TryGetRawError(out RawError)` is the fallback on every typed error.
- **Case B** (raw): `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` — here:
  `ReadCustomerByReference`, `ListCustomerSubscriptions`, `ListProductFamilies`, `ReadProductByHandle`.
  Read `ex.Error.StatusCode` (`HttpStatusCode`), `ex.Error.ReadAsString()` / `ReadAsJson<T>()`.
  404 detection on customer lookup / my-subscriptions: `StatusCode == HttpStatusCode.NotFound`.
- (`SdkException<>` / `RawError` namespaces are path-implied per the map's rule: `Core/Exceptions/…` ⇒
  `MaxioAdvancedBilling.Core.Exceptions`, `Core/ErrorResponse/…` ⇒ `MaxioAdvancedBilling.Core.ErrorResponse`.)
- Dates are `DateTimeOffset?` (ISO-8601 on the wire). Prices are integer **cents** (`long?`): $299.00 → `29900`.
- No union/`AnyOf` type is on the hero path (`CreateSubscription.OfferId` and `Components` involve unions —
  not used here).

### 2.6 Idempotency design (contract-grounded)

- **Customer**: `reference` = eShopOnWeb user id; the API enforces reference uniqueness
  (`operations/Customers.md` CreateCustomer notes). Flow: `ReadCustomerByReference` → 404 ⇒
  `CreateCustomer` → 422 race (double-click) ⇒ re-`ReadCustomerByReference`.
- **Subscription**: no idempotency-header field exists on `CreateSubscription` (full field list,
  `records-2-Cr-Ne.md`) — idempotency is app-level: set `CreateSubscription.Reference` to a deterministic
  key (`{userId}:{productHandle}`), `FindSubscription(reference)` first; 404 (`TryGetNoContent`) ⇒ create.
  Secondary check via `ListCustomerSubscriptions(customerId)` for an existing live subscription to the
  same product.

## 3. Trap notes

> ⚠ Step 2 (client registration) — the `HttpClient`/handler pipeline and the SDK client wrapper have
> different required lifetimes; getting this wrong exhausts sockets under load. **MUST load
> `dotnet-client-initialization`** before wiring DI.

> ⚠ Step 2 (auth) — when credentials must be set relative to client construction (and from configuration,
> not hardcoded) is not visible from the options shape. **MUST load `dotnet-authentication`**.

> ⚠ Steps 3–6 (every call) — most optional params are nullable-with-no-default and mis-bind in positional
> calls; call list/search ops with named arguments. **MUST load `dotnet-calling-endpoints`**.

> ⚠ Steps 3–6 (models) — enums are `StringEnum<T>` (build/compare via its members, not C# enum syntax),
> records are immutable with `init`-only setters and `required` members, and unmodeled JSON fields are
> silently dropped on deserialize. **MUST load `dotnet-models`**.

> ⚠ Step 7 (error boundary) — Case A vs Case B is per-operation (§2.2 marks each); `TryGetRawError` is not
> a catch-all on typed errors; this SDK has no no-throw variants. **MUST load `dotnet-error-handling`**.

> ⚠ Step 2 (resilience) — what `RetryOptions.Timeout` actually bounds, and whether a failed
> `POST /subscriptions.json` can be re-sent by the retry layer (which decides how hard the §2.6
> idempotency key matters), are not answerable from the option names. **MUST load
> `dotnet-configuration-resilience`** before tuning `Retry`.

> ⚠ Step 8 (tests) — the SDK's test seam is a specific constructor argument; stubbing elsewhere couples
> tests to SDK internals. **MUST load `dotnet-testing`**.

## 4. REQUIRED READING

Load **before implementation starts** (this sheet deliberately does not carry their contents):

- `dotnet-client-initialization` — step 2 (client construction & DI registration)
- `dotnet-authentication` — step 2 (Basic credentials wiring)
- `dotnet-calling-endpoints` — steps 3–6 (every operation call)
- `dotnet-models` — steps 3–6 (request/response model construction & reads)
- `dotnet-error-handling` — step 7 (the exception boundary)
- `dotnet-configuration-resilience` — step 2 (retry/timeout/base-URL tuning)
- `dotnet-testing` — step 8 (integration-layer tests)

Two `System.Text.Json.JsonException` hazards reach the boundary from opposite directions and need
opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from
  deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape
  the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the
  `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException`
  to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries
  something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

## 5. Assumptions & Blockers

- **Assumption** — the `cp-exp-1` sandbox account is US-hosted ⇒ `ServerEnvironment.Us`. The SDK has no
  sandbox environment; "sandbox" is a site in test mode. If the account is EU-hosted, switch to
  `ServerEnvironment.Eu` (or set `Maxio:BaseUrl`).
- **Assumption** — family resolution goes `ListProductFamilies` → match `Handle == "eshop-subscribe"` →
  numeric `Id` → `ListProductsForProductFamily(id.ToString(), …)`. Passing `"handle:eshop-subscribe"`
  directly as `productFamilyId` (a `string`) is plausible but **UNVERIFIED** for that endpoint — only
  live traffic could confirm it; the two-step path is fully contract-grounded.
- **Assumption** — subscription idempotency key `{userId}:{productHandle}` implies ≤ 1 subscription per
  user per plan. If multi-subscription-per-plan is ever required, the key must come from the client
  attempt instead.
- **Assumption** — shopper `FirstName`/`LastName`/`Email` are available from eShopOnWeb identity (JWT
  claims or the user store); `CreateCustomer` requires all three. **Blocker** if the identity layer
  cannot supply them.
- **Fact** — `Subscription` has no `next_billing_at`; endpoints expose `NextAssessmentAt ??
  CurrentPeriodEndsAt` (both nullable).
- **Fact** — the metered component `api-call` is noted but out of scope; `SubscriptionComponents`
  operations are deliberately not on this sheet.
- **Fact** — numeric IDs (family `3023074`) are treated as stale per the brief; everything resolves by
  handle.
- **UNVERIFIED** — the live 422 wire shape for `CreateCustomer` (the generated `Errors` record models
  only `per_page`/`price_point` keys); the sheet's directive (re-read by reference, log raw body) is
  written to be correct under any shape.
