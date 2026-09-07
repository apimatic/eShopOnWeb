# Maxio SDK Integration Plan — eShopOnWeb Subscription Billing

## Scope & Sequence

This plan covers three HTTP endpoints to add to `src/PublicApi` for subscription management:

1. **Step 1: List available subscription plans** (`GET /api/subscription-plans`)
   - Retrieve products from the configured product family (`eshop-subscribe`)
   - Operation: `ProductFamilies.ListProductsForProductFamily`

2. **Step 2: Create a subscription** (`POST /api/subscriptions`)
   - Ensure customer exists or create one (idempotent by reference)
   - Create subscription on the specified plan
   - Operations: `Customers.ReadCustomerByReference` (or fallback to `CreateCustomer`), then `Subscriptions.CreateSubscription`

3. **Step 3: List user's subscriptions** (`GET /api/my-subscriptions`)
   - Retrieve the authenticated user's subscriptions from Maxio
   - Operation: `Customers.ListCustomerSubscriptions`

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

| Operation | Signature | Request model + fields | Response envelope + fields | Error case | Pagination | Source |
|---|---|---|---|---|---|---|
| **List products for product family** (Step 1) | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — all params before `page` are nullable with no default, must pass explicitly (or `null` to skip); `page` defaults to 1, `perPage` to 20 | N/A (params only) | `IReadOnlyList<ProductResponse>` — each carries `Product (product): Product !req`, which includes: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?` (enum: `Day`, `Month`), `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?` | **Case A (typed)** `SdkException<ListProductsForProductFamilyError>` with accessors: `TryGetString(out string)` [404], `TryGetRawError(out RawError)` [fallback] | Manual via `page`+`perPage` | `operations/ProductFamilies.md` |
| **Create customer (idempotent)** (Step 2a, fallback) | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, must pass explicitly | `CreateCustomerRequest` wrapper → field: `Customer (customer): Customer !req` — which contains: `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?`, `Address (address): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`. **Note:** `Reference` is the idempotency key; use user ID from eShopOnWeb. | `CustomerResponse` — field: `Customer (customer): Customer !req`, with same fields as request plus `Id (id): int?` (Maxio-assigned), `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?` | **Case A (typed)** `SdkException<CreateCustomerError>` with accessors: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422], `TryGetRawError(out RawError)` [fallback] | None | `operations/Customers.md`, `records-2-Cr-Ne.md` |
| **Read customer by reference** (Step 2a, check existence) | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — `reference` must be passed explicitly; wire param name `reference` | N/A (query param only) | `CustomerResponse` — field: `Customer (customer): Customer !req` (same structure as CreateCustomer response) | **Case B (raw)** `SdkException<RawError>` with members: `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`, `ReadAsBytes(): ReadOnlyMemory<byte>` | None | `operations/Customers.md` |
| **Create subscription** (Step 2b) | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, must pass explicitly | `CreateSubscriptionRequest` wrapper → field: `Subscription (subscription): CreateSubscription !req` — which contains: `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `CustomerId (customer_id): int?`, `CustomerReference (customer_reference): string?`, `Reference (reference): string?` (optional; for idempotency), `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` (enum: `Automatic`, `Remittance`, `Prepaid`, `Invoice` — use `Automatic` for auto-billing, but **no payment method required** per scope), **plus many optional fields listed in source** (see `Models/CreateSubscription.cs` for full list) | `SubscriptionResponse` — field: `Subscription (subscription): Subscription?`, with key fields: `Id (id): int?` (Maxio-assigned), `State (state): SubscriptionState?` (enum: `Active`, `Trialing`, `PastDue`, `Suspended`, `Canceled`, `Expired`, `Paused`… — see enums.md for full list), `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `ProductPriceInCents (product_price_in_cents): long?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?` | **Case A (typed)** `SdkException<CreateSubscriptionError>` with accessors: `TryGetErrorListResponse1(out ErrorListResponse1)` [422], `TryGetRawError(out RawError)` [fallback] | None | `operations/Subscriptions.md`, `records-2-Cr-Ne.md` |
| **List customer subscriptions** (Step 3) | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — `customerId` must be passed explicitly | N/A (URL param only) | `IReadOnlyList<SubscriptionResponse>` — each carries `Subscription (subscription): Subscription?` (same fields as CreateSubscription response) | **Case B (raw)** `SdkException<RawError>` (no typed accessors; use `StatusCode`, `ReadAsString()`, etc.) | None (returns full list, no pagination on this endpoint) | `operations/Customers.md` |

### Enum values actually needed

| Enum | Namespace | Used in | C# members (wire values) | Source |
|---|---|---|---|---|
| `CollectionMethod` | `MaxioAdvancedBilling.Models.Enums` | CreateSubscription | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` | `enums.md` |
| `IntervalUnit` | `MaxioAdvancedBilling.Models.Enums` | Product, ProductPricePoint | `Day (day)`, `Month (month)` | `enums.md` |
| `SubscriptionState` | `MaxioAdvancedBilling.Models.Enums` | Subscription response | `Pending (pending)`, `Trialing (trialing)`, `Active (active)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)`, etc. (15 total values) | `enums.md` |
| `BasicDateField` | `MaxioAdvancedBilling.Models.Enums` | ListProductsForProductFamily optional param | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` | `enums.md` |

### Client construction & configuration

| Fact | Details | Source |
|---|---|---|
| **Client class** | `MaxioAdvancedBillingClient` | `sdk-map.md` |
| **Root namespace** | `MaxioAdvancedBilling` (the `using` — differs from NuGet package id `AsadAli.AdvancedBilling.Sdk`) | `sdk-map.md` |
| **Constructor** | `MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` |
| **Options class** | `MaxioAdvancedBillingClientOptions` | `sdk-map.md` |
| **Auth** | HTTP **Basic**: `options.BasicAuth = new BasicAuthCredentials { Username = "<api_key>", Password = "x" }`. Username = Maxio API key; Password = literal string `"x"` | `sdk-map.md` |
| **Auth namespace** | `MaxioAdvancedBilling.Core.Authentication.Basic` (add `using` for `BasicAuthCredentials`) | `sdk-map.md` |
| **Environments** | `options.Environment = ServerEnvironment.Us` (default, US hosting) or `ServerEnvironment.Eu` (EU hosting). Namespace: `MaxioAdvancedBilling.Servers` | `sdk-map.md` |
| **Site subdomain override** | `options.Server.Production.Us.Site = "<subdomain>"` (from config `Maxio:Subdomain`) | `sdk-map.md` |
| **Base URL override** | `options.Server.Production.Us.BaseUrl = "<url>"` (for mock/dev hosts; from config `Maxio:BaseUrl` if provided) | `sdk-map.md` |

### Error payload types (by operation)

| Operation | Error type | Case | Accessors (wire status → C# type) | Source |
|---|---|---|---|---|
| ListProductsForProductFamily | `ListProductsForProductFamilyError` | A (typed) | `TryGetString(out string)` [404], `TryGetRawError(out RawError)` [fallback] | `operations/ProductFamilies.md` |
| CreateCustomer | `CreateCustomerError` | A (typed) | `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422], `TryGetRawError(out RawError)` [fallback] | `operations/Customers.md` |
| ReadCustomerByReference | `RawError` | B (raw) | N/A — use `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | `operations/Customers.md` |
| CreateSubscription | `CreateSubscriptionError` | A (typed) | `TryGetErrorListResponse1(out ErrorListResponse1)` [422], `TryGetRawError(out RawError)` [fallback] | `operations/Subscriptions.md` |
| ListCustomerSubscriptions | `RawError` | B (raw) | N/A — use `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | `operations/Customers.md` |

---

## Trap notes

⚠ **Step 1 (client DI registration)** — The SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; the `HttpClient` must be long-lived and reused via `IHttpClientFactory`, not rebuilt per request. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ **Step 1 (authentication)** — Set credentials (username = API key, password = `"x"`) in the options object **before** constructing the client or in the DI callback. Load the key from configuration (`Maxio:ApiKey`, defaulted or injected as binding key) rather than hardcoding. **MUST load `dotnet-authentication`** before writing credential setup.

⚠ **Step 2 (idempotent customer lookup)** — The integration must check if a customer with the user's reference (e.g. user ID) already exists via `ReadCustomerByReference`, then fall back to `CreateCustomer` only if not found. This requires catching `SdkException<RawError>` with `StatusCode == 404`. **Do not** retry or assume creation on failure — a customer might exist but with a different reference value. **MUST load `dotnet-error-handling`** to understand Case A vs B exceptions and how to distinguish 404 (not found) from other failures.

⚠ **Step 2–3 (calling endpoints)** — Many optional params have no C# default and mis-bind in positional calls; use **named arguments** (e.g., `ListProductsForProductFamily(productFamilyId: familyHandle, dateField: null, filter: null, …)`). The cancellation-token parameter is literally named `ct`, not `cancellationToken`. **MUST load `dotnet-calling-endpoints`** before the first call.

⚠ **Step 2 (request models — no implicit nulls)** — `CreateSubscriptionRequest` wraps a required `CreateSubscription` field; only fields marked `!req` are C# `required` and must be set in the initializer. Fields without `!req` are optional, but **an optional field you leave out is not sent to the wire** — if the operation's Notes tie a field to acceptance (e.g. payment method required if `request_credit_card` is true), you must explicitly include it or risk the call being rejected. Review `CreateSubscription.cs` in the SDK source for the full field list and defaults. **MUST load `dotnet-models`** before building request initializers.

⚠ **Deserialization boundary (two JsonException paths)** — a drifted or malformed **2xx** body (missing `required` member) surfaces as `JsonException` from deserialization, **not** as `SdkException` — an SDK-exception-only catch ladder lets it escape; a **non-2xx** body that doesn't match the operation's `{Operation}Error` shape throws `JsonException` *while the error object is constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is lost — a boundary that maps every `JsonException` to 5xx then reports deterministic rejections as outages, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing the integration boundary.

⚠ **Configuration resilience** — Retry and timeout settings are per-attempt, not per-call; there is no built-in logging hook; `HttpMethodsToRetry` gates only status-based triggers (a `503` on `POST` is not retried), but transport failures on **any** verb are retried on every method including `POST` (non-idempotent creates can execute twice; set `MaxRetries = 0` is rejected, floor is 1). **MUST load `dotnet-configuration-resilience`** when tuning retries or timeouts.

---

## REQUIRED READING

Load these skills **before implementation starts**. The sheet deliberately does not carry their contents — each skill carries defaults, worked examples, configuration patterns, and code you must still wire yourself.

| Skill | Step(s) governed |
|---|---|
| `dotnet-client-initialization` | Step 1 — DI registration, `HttpClient` lifetime, `IHttpClientFactory` |
| `dotnet-authentication` | Step 1 — credential loading, auth-scheme setup, environment-based secrets |
| `dotnet-calling-endpoints` | Steps 1–3 — operation calls, named arguments, pagination, query/body param binding |
| `dotnet-models` | Step 2 — request/response model construction, immutable records, `init`-only setters, required fields |
| `dotnet-error-handling` | Steps 1–3 — `SdkException<T>` (typed vs raw), `TryGet…` accessors, catching by status, `JsonException` dual paths, boundary logic |
| `dotnet-configuration-resilience` | Steps 1–3 (setup) — retry/timeout wiring, per-attempt bounds, transport vs status triggers, `MaxRetries` floor |

**These are mandatory:** the boundary (error handling) is written early; a caveat that arrives afterwards is too late. Both `JsonException` hazards belong in the FIRST iteration of the integration boundary, not a later revision.

---

## Assumptions & Blockers

| Item | Status | Notes |
|---|---|---|
| **Product family handle** | Assumption | The scope names the product family `eshop-subscribe`; the plan assumes this handle exists in Maxio and is configured in `Maxio:ProductFamilyHandle` (or injected via IOptions). If the family doesn't exist, `ListProductsForProductFamily` will return 404. |
| **User identity from JWT** | Assumption | The endpoints will extract the authenticated user's identity from the JWT token; the user's ID (or a stable reference) will be passed to Maxio's `Reference` field for idempotency. The app's user ID scheme must be stable (e.g. numeric ID, not email, to avoid collisions). |
| **No payment method captured upfront** | Fact | The scope states "payment method not required"; subscription creation calls omit payment fields. Maxio allows this only if the product's `request_credit_card` flag is false (seeded data must allow this). If a product requires a card, creation will fail with 422. Verify seeded products do not require payment. |
| **Trial and setup fee not in scope** | Fact | Plan creation and subscription operations do not configure trial or setup fees. If seeded data includes these, subscriptions will inherit them. The plan does not override them. |
| **Configuration keys** | Assumption | `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:Environment`, `Maxio:ProductFamilyHandle` are injected via `IConfiguration` + `IOptions<MaxioSettings>` (or similar). `Maxio:BaseUrl` is optional (defaults to Maxio-hosted endpoint). No integration code directly reads environment variables. |
| **Maxio site already exists** | Blocker | The plan assumes the eShopOnWeb app has a Maxio account and site set up. A Maxio site must exist before any SDK calls; if not, operations will fail with auth or connection errors. **Verify the sandbox/live Maxio site is provisioned and accessible before implementation starts.** |

