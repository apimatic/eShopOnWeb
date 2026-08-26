# Maxio Advanced Billing integration plan — eShopOnWeb recurring subscriptions

Plan + contract sheet for adding Maxio-backed subscription billing to `src/PublicApi` (three
JWT-authenticated endpoints), parallel to the existing one-time checkout. Every contract fact
below is grounded in the bundled SDK map (`maxio-getting-started` skill); each row cites its map
page.

## 1. Scope & sequence

| # | Step | Operations used |
|---|------|-----------------|
| 1 | Add NuGet package `AsadAli.AdvancedBilling.Sdk` (pin the `1.0.2` line — the map's stamp) to `src/PublicApi`; bind a `MaxioOptions` class from the `Maxio:` section (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl`) | — |
| 2 | Register `MaxioAdvancedBillingClient` in DI (auth + site/base-URL from `MaxioOptions`) | — |
| 3 | `GET /api/subscription-plans` — list plans in the configured family | `ProductFamilies.ListProductsForProductFamily` |
| 4 | `POST /api/subscriptions` — find-or-create customer by reference (= eShopOnWeb user id from JWT), duplicate-subscription guard, create subscription by product handle | `Customers.ReadCustomerByReference` → `Customers.CreateCustomer` → `Customers.ListCustomerSubscriptions` → `Subscriptions.CreateSubscription` |
| 5 | `GET /api/my-subscriptions` — resolve customer by reference, list their subscriptions (404 from the lookup ⇒ return empty list, not an error) | `Customers.ReadCustomerByReference` → `Customers.ListCustomerSubscriptions` |
| 6 | Error boundary: translate SDK exceptions to HTTP responses (404 vs 422 vs 5xx) | all of the above |
| 7 | Tests for the integration layer (fake at the `HttpClient` seam) | — |

Out of scope: the seeded metered component `api-call` (per brief), webhooks, plan changes,
cancellation, payment profiles (seeded plans do not require a payment method).

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

### 2.1 SDK identity & client construction (map: `sdk-map.md`)

| Fact | Value |
|------|-------|
| NuGet package | `AsadAli.AdvancedBilling.Sdk` (version line `1.0.2`; package id ≠ root namespace) |
| Root namespace | `MaxioAdvancedBilling` |
| Client class | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` — only ctor: `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` |
| Options class | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` — props: `Environment: ServerEnvironment`, `Retry: RetryOptions`, `Server: ServerOptions`, `BasicAuth: BasicAuthCredentials?` |
| DI alternative | `services.AddMaxioAdvancedBillingClient(o => { … })` (`ServiceCollectionExtensions.cs`, repo root ⇒ root namespace `MaxioAdvancedBilling`) |
| Auth | `o.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }` — password is the literal string `"x"` |
| Environment | `o.Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default; `Eu` ⇒ `https://{site}.ebilling.maxio.com`) |
| Site (subdomain) | `o.Server.Production.Us.Site = <Maxio:Subdomain>` — fills `{site}` in `https://{site}.chargify.com` |
| Base-URL override | When `Maxio:BaseUrl` is set: `o.Server.Production.Us.BaseUrl = <Maxio:BaseUrl>` **verbatim**, instead of setting `.Site` (`ServerOptions` at repo root ⇒ root ns; `ProductionOptions` ⇒ `MaxioAdvancedBilling.Servers`) |
| Retry config | `o.Retry` is `MaxioAdvancedBilling.Core.Configuration.RetryOptions` — all members `required`; start from `RetryOptions.Default()` |

### 2.2 Operations

**a. List plans — `client.ProductFamilies.ListProductsForProductFamily`** (map: `operations/ProductFamilies.md`)

- Signature: `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)`
  - The 8 params `dateField`…`include` are nullable with **no C# default — must be passed explicitly** (pass `null` to skip). Call with **named arguments**.
  - `productFamilyId` is `string` — pass `"handle:" + productFamilyHandle` (i.e. `"handle:eshop-subscribe"`). Map evidence: the sibling row `ReadProductFamily` documents the `handle:my-family` format for the `/product_families/{…}` path segment, and this op's parameter is string-typed where `ReadProductFamily` takes `int`. On a 404 here, treat the configured family handle as broken config, not an empty catalog.
- Returns: `IReadOnlyList<ProductResponse>` — **no envelope/metadata wrapper**; each element wraps the payload one level down: `ProductResponse.Product` (`MaxioAdvancedBilling.Models.Product`, `required`, non-null).
- Error: **Case A** `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] (404 payload is a plain string) · `TryGetRawError(out RawError)` [fallback].
- Pagination: manual `page`/`perPage` only; no total-count in the response. Loop `page` until a short page if the catalog can exceed `perPage`.

**b. Find-or-create customer** (map: `operations/Customers.md`)

1. `client.Customers.ReadCustomerByReference(string reference, CancellationToken ct = default)` — `GET /customers/lookup.json`, single exact match. `reference` = the eShopOnWeb user id.
   - Returns `CustomerResponse` → `.Customer` (`required`).
   - Error: **Case B** `SdkException<RawError>` — "customer not found" = `ex.Error.StatusCode == HttpStatusCode.NotFound`. This 404 is the **normal find-or-create branch**, not a failure.
2. On 404: `client.Customers.CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly.
   - Request: `new CreateCustomerRequest { Customer = new CreateCustomer { FirstName = …, LastName = …, Email = …, Reference = <userId> } }` — `Customer` is `required`; on `CreateCustomer`, `FirstName`/`LastName`/`Email` are `required`; `Reference` optional-but-load-bearing (uniqueness is enforced server-side: one customer per `reference`).
   - Returns `CustomerResponse` → `.Customer.Id (id): int?`, `.Customer.Reference (reference): string?`.
   - Error: **Case A** `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. A 422 on duplicate `reference` means a race lost — re-run `ReadCustomerByReference` and use the winner.
   - ⚠ `UNVERIFIED`: `CustomerErrorResponse1.Errors` is typed `Errors?`, and the shared `Errors` record models only `PerPage (per_page)` / `PricePoint (price_point)` lists — a suspicious shape for a customer-creation 422 (map-visible: `records-2-Cr-Ne.md`). Defensive directive: extract 422 details best-effort via the accessor, but always fall back to `TryGetRawError` + `ReadAsString()` for the real message; never depend on `Errors` fields being populated.

**c. Create subscription — `client.Subscriptions.CreateSubscription`** (map: `operations/Subscriptions.md`, `records-2-Cr-Ne.md`)

- Signature: `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly.
- Request: `new CreateSubscriptionRequest { Subscription = new CreateSubscription { ProductHandle = <plan handle>, CustomerId = <customer.Id> } }`
  - `Subscription` is `required`. On `CreateSubscription` **every** field is optional — identify the product by `ProductHandle (product_handle): string?` (never `ProductId`; ids are unstable per brief) and the customer by `CustomerId (customer_id): int?` (or `CustomerReference (customer_reference): string?` — either works; using the id obtained in step b is one less lookup).
  - **No payment fields**: seeded plans have payment-method-not-required, so omit `PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes` entirely. (The op notes confirm payment info is required only "depending on the options for the Product".)
  - Optional idempotency aid: `CreateSubscription.Reference (reference): string?` set to a deterministic value (e.g. `"{userId}:{productHandle}"`) enables `client.Subscriptions.FindSubscription(string? reference, …)` — Case A `FindSubscriptionError` with `TryGetNoContent(out RawError)` [404] — as a pre-create lookup.
- Returns: `SubscriptionResponse` → `.Subscription` is **nullable** (`Subscription?`) — null-check before reading.
- Error: **Case A** `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] (`ErrorListResponse1.Errors (errors): IReadOnlyList<string>`, `required`) · `TryGetRawError(out RawError)` [fallback].
- Duplicate guard (double-click): before creating, run `ListCustomerSubscriptions` (below) and refuse/return-existing when the customer already has a subscription on the same product handle in a non-terminal state (see state table; non-terminal = anything but `Canceled`, `Expired`, `FailedToCreate`).

**d. List a customer's subscriptions — `client.Customers.ListCustomerSubscriptions`** (map: `operations/Customers.md`)

- Signature: `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)`.
- Returns: `IReadOnlyList<SubscriptionResponse>` — each element's `.Subscription` is nullable.
- Error: **Case B** `SdkException<RawError>` (`StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`).
- Pagination: **none** — no page params; the full list comes back.

### 2.3 Response model fields the integration reads (map: `records-3-Of-Su.md`, `records-4-Su-We.md`; all in `MaxioAdvancedBilling.Models`)

| Model | Fields (`CSharpName (wire_name): Type`) |
|-------|------------------------------------------|
| `Product` | `Name (name): string?` · `Handle (handle): string?` · `Description (description): string?` · `PriceInCents (price_in_cents): long?` — cents, $299.00 ⇒ `29900` · `Interval (interval): int?` · `IntervalUnit (interval_unit): IntervalUnit?` · `ArchivedAt (archived_at): DateTimeOffset?` |
| `Customer` | `Id (id): int?` · `Reference (reference): string?` · `Email (email): string?` · `FirstName (first_name): string?` · `LastName (last_name): string?` |
| `Subscription` | `Id (id): int?` · `State (state): SubscriptionState?` · `Product (product): Product?` (nested — plan name/handle/price) · `ProductPriceInCents (product_price_in_cents): long?` · `Customer (customer): Customer?` · `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` · `NextAssessmentAt (next_assessment_at): DateTimeOffset?` · `CreatedAt (created_at): DateTimeOffset?` · `CanceledAt (canceled_at): DateTimeOffset?` |
| Envelopes | `ProductResponse.Product` (`required`) · `CustomerResponse.Customer` (`required`) · `SubscriptionResponse.Subscription` (**nullable**) — reads go one level down |

**"Next billing date" — map-visible gap, resolved:** the generated `Subscription` model has **no**
`NextBillingAt`/`next_billing_at` field (verified against the full model row; `next_billing_at`
exists only as a *request* field on `CreateSubscription`/`UpdateSubscription`). Surface
**`CurrentPeriodEndsAt (current_period_ends_at)`** as the plan's next-billing/renewal date in all
three endpoints (the `UpdateSubscription` notes treat `current_period_ends_at` as the field that
reflects next-billing changes). `NextAssessmentAt` is the internal assessment timestamp — do not
show it to shoppers.

### 2.4 Enums needed (map: `models/enums.md`; namespace `MaxioAdvancedBilling.Models.Enums`)

`StringEnum<T>`, **not** C# enums — use static members (`SubscriptionState.Active`) or
`Type.FromValue("active")`; compare with `.ToString()`/wire value, not `Enum.Parse`.

| Enum | Members (`CSharpMember (wire)`) | Used for |
|------|----------------------------------|----------|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` | state in API responses; duplicate-guard terminal set = `Canceled`, `Expired`, `FailedToCreate` |
| `IntervalUnit` | `Day (day)`, `Month (month)` | plan interval display ("$299.00 / 1 month") |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` | only if ever setting `CreateSubscription.PaymentCollectionMethod`; default (omit) is fine for seeded plans |
| `BasicDateField`, `ListProductsInclude` | — | unused list-op params; pass `null` |

### 2.5 Error model summary (map: `sdk-map.md`, `operations/*`)

- All ops are **throw-only** (no `…Result` variants). `MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>` exposes `.Error` (namespace path-implied from `Core/Exceptions/SdkException.cs`).
- Case A (typed): `SdkException<{Operation}Error>` (`{Operation}Error` types live in `MaxioAdvancedBilling.Errors`) — status-specific `TryGet…(out …)` + inherited `TryGetRawError(out RawError)` fallback. In scope: `ListProductsForProductFamilyError` (404→`string`), `CreateCustomerError` (422→`CustomerErrorResponse1`), `CreateSubscriptionError` (422→`ErrorListResponse1`), `FindSubscriptionError` (404→`RawError`).
- Case B (raw): `SdkException<RawError>` — `MaxioAdvancedBilling.Core.ErrorResponse.RawError` (path-implied): `StatusCode: HttpStatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. In scope: `ReadCustomerByReference`, `ListCustomerSubscriptions`.
- 404 vs 422 for find-or-create: customer-missing = Case B `RawError.StatusCode == 404`; customer-duplicate-race = Case A 422 via `TryGetCustomerErrorResponse1`; subscription-validation failure = Case A 422 via `TryGetErrorListResponse1` (`Errors` string list).

## 3. Trap notes

- ⚠ Step 2 (client registration) — the `HttpClient`/handler pipeline behind the client has lifetime rules the ctor signature doesn't show; registering the wrong way sockets-exhausts under load. **MUST load `dotnet-client-initialization`** before wiring DI.
- ⚠ Step 2 (auth) — credentials must be set before the client is constructed and loaded from configuration, never hard-coded; the options shape hides when auth is captured. **MUST load `dotnet-authentication`**.
- ⚠ Steps 3–5 (every call) — most optional params have **no C# default** and mis-bind positionally; call list/search ops with named arguments only, and the cancellation token really is `ct:`. **MUST load `dotnet-calling-endpoints`**.
- ⚠ Steps 3–5 (models) — enums are `StringEnum<T>` (not C# enums), records are immutable with `required` init members, unions use factories/`TryGet…`, and **unmodeled JSON fields are silently dropped on deserialize** (why `next_billing_at` can never appear — §2.3). **MUST load `dotnet-models`**.
- ⚠ Step 6 (error boundary) — Case A vs Case B is **per operation** (confirm each in §2.2/§2.5), `TryGetRawError` is not a catch-all on typed errors, and every op is throw-only. **MUST load `dotnet-error-handling`**.
- ⚠ Step 4 (`POST /api/subscriptions`) — the SDK's retry policy interacts with non-idempotent writes: whether a failed `CreateSubscription` can be re-sent under the hood is exactly the double-billing scenario the endpoint's idempotency guard must assume can happen; what `Timeout` actually bounds is similarly non-obvious. **MUST load `dotnet-configuration-resilience`** before tuning or relying on retry/timeout behavior.
- ⚠ Step 7 (tests) — the test seam is the `HttpClient` ctor argument, not mocking SDK types. **MUST load `dotnet-testing`**.

## 4. REQUIRED READING

Load **before implementation starts** — this sheet deliberately does not carry their contents:

- `dotnet-client-initialization` — step 2 (client construction & DI registration)
- `dotnet-authentication` — step 2 (Basic auth credentials from config)
- `dotnet-calling-endpoints` — steps 3–5 (named arguments, must-pass-explicitly params, envelopes)
- `dotnet-models` — steps 3–5 (records, `StringEnum<T>`, required members, dropped fields)
- `dotnet-error-handling` — step 6 (Case A/B mechanics, the boundary)
- `dotnet-configuration-resilience` — steps 2 & 4 (retries on writes, timeout semantics, base-URL)
- `dotnet-testing` — step 7 (faking the `HttpClient` seam)

Mandatory hazard rows for the error boundary — `System.Text.Json.JsonException` reaches the
boundary from two directions and they need opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
  maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
  and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

## 5. Assumptions & Blockers

Assumptions (stated, not blocking):

1. Customer `reference` = the eShopOnWeb user id from the JWT (per brief); `CreateCustomer.FirstName`/`LastName` are SDK-`required`, so derive them from JWT claims (name/given_name) with a deterministic fallback (e.g. email local-part) when absent.
2. US hosting — `ServerEnvironment.Us`; if the site is EU-hosted, set `Environment` accordingly (same `Server.Production.Eu.*` override shape).
3. "Next billing date" in API responses = `Subscription.CurrentPeriodEndsAt` (no `next_billing_at` on the response model — §2.3).
4. Duplicate-subscription definition for the double-click guard: same customer + same product handle with state not in {`Canceled`, `Expired`, `FailedToCreate`}.
5. Plan catalog is small; one `ListProductsForProductFamily` page at a raised `perPage` suffices, with the manual page loop as the safety net.
6. `Maxio:BaseUrl`, when set, is a complete base address (e.g. `http://localhost:8080`) applied verbatim to `Server.Production.Us.BaseUrl`; when absent, `Server.Production.Us.Site = Maxio:Subdomain`.
7. The metered component `api-call` stays out of scope (brief permits).

Blockers: none.
