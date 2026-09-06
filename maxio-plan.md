# Maxio Advanced Billing SDK Integration Plan — eShopOnWeb Subscriptions

## Scope & Sequence

1. **Client initialization & DI** — `MaxioAdvancedBillingClient`, HTTP configuration, Basic auth wiring
2. **Customer lifecycle** — Ensure/create Maxio customer for each eShopOnWeb user (idempotent via `customer_reference`)
3. **Plan discovery** — List subscription products from the `eshop-subscribe` product family
4. **Subscription creation** — Create subscriptions (no payment method required per scope; payment in sandbox mode)
5. **Subscription retrieval** — Fetch user's subscriptions by customer ID

All operations use `client.Customers`, `client.Subscriptions`, and `client.Products`.

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operations

| Operation | Signature | Request Model + Fields | Response Envelope & Inner Fields | Error Case + Accessors + Type | Pagination | Source |
|---|---|---|---|---|---|---|
| **Step 2a: Idempotent customer lookup** `Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — `reference`: required; used as query param `reference` (wire ← C#). **Must pass explicitly**. | Query param only — no request body. Pass the eShopOnWeb user's unique ID as `reference`. | `CustomerResponse` (envelope) → field `Customer` (wire: `customer`) of type `Customer?`. Key fields: `Id` (int?), `Reference` (string?), `Email` (string?), `FirstName` (string?), `LastName` (string?). | **Case B** (`SdkException<RawError>`). No typed accessors; use `ex.Error.StatusCode`, `ex.Error.ReadAsString()`, `ex.Error.ReadAsJson<T>()`. 404 = customer not found (create new). | None | `operations/Customers.md` |
| **Step 2b: Create Maxio customer** `Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body`: nullable, no default → **must pass explicitly**. | `CreateCustomerRequest` (envelope) → field `Customer` (wire: `customer`) of type `CustomerAttributes !req` (required). Customer fields: `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?`, `Organization (organization): string?`, `Address (address): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`. **Only `Reference` is needed for idempotent matching; others optional unless validation requires them.** | `CustomerResponse` (envelope) → field `Customer` (wire: `customer`) of type `Customer !req` (required). Key fields: `Id` (int?), `Reference` (string?), `Email` (string?), `CreatedAt` (DateTimeOffset?), `UpdatedAt` (DateTimeOffset?). | **Case A** (`SdkException<CreateCustomerError>`). Accessors: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] — contains `Errors (errors): Errors?` (map of error lists), `TryGetRawError(out RawError)` [fallback]. | None | `operations/Customers.md` |
| **Step 3: List subscription plans** `Products.ListProducts` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 params (`dateField` through `include`) nullable, no default → **must pass explicitly** (pass `null` to skip). Defaults: `page` = 1, `perPage` = 20. **Filter by product family**: use `filter` param (type `ListProductsFilter?`); its field `Ids (ids): IReadOnlyList<int>?` filters by product ID, not family. **UNVERIFIED**: To filter by product-family handle `eshop-subscribe`, either: (A) set `filter.Ids` to the product IDs in the family (query the family first via `ProductFamilies.ReadProductFamilyByHandle`, then list products; 2 calls), OR (B) call `ListProducts` without filter and post-filter by `product.ProductFamily.Handle == "eshop-subscribe"` in-memory. The map does not explicitly state a product-family-handle filter on `ListProducts`. — Query params (wire ← C#): `page` ← `page`, `per_page` ← `perPage`, `date_field` ← `dateField`, `filter` ← `filter`, `end_date` ← `endDate`, `end_datetime` ← `endDatetime`, `start_date` ← `startDate`, `start_datetime` ← `startDatetime`, `include_archived` ← `includeArchived`, `include` ← `include`. | No request body (query-only). `filter` (query object) — `ListProductsFilter` (no envelope; inline query object): field `Ids (ids): IReadOnlyList<int>?`, `UseSiteExchangeRate (use_site_exchange_rate): bool?`, `PrepaidProductPricePoint (prepaid_product_price_point): PrepaidProductPricePointFilter?`. Use `Ids` to pass product IDs (if pre-filtering by product family). | `IReadOnlyList<ProductResponse>` (array, no envelope). Each `ProductResponse` (envelope) → field `Product` (wire: `product`) of type `Product !req` (required). Key fields: `Id` (int?), `Handle` (string?), `Name` (string?), `PriceInCents` (long?), `Interval` (int?), `IntervalUnit` (IntervalUnit?), `ProductFamily` (ProductFamily?) [nested], `ProductPricePointName` (string?). | **Case B** (`SdkException<RawError>`). No typed accessors; use `ex.Error.StatusCode`, `ex.Error.ReadAsString()`, `ex.Error.ReadAsJson<T>()`. | Manual `page`+`perPage`. | `operations/Products.md` |
| **Step 4: Create subscription** `Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body`: nullable, no default → **must pass explicitly**. | `CreateSubscriptionRequest` (envelope) → field `Subscription` (wire: `subscription`) of type `CreateSubscription !req` (required). Key fields for minimal creation: `ProductHandle (product_handle): string?` **OR** `ProductId (product_id): int?` (one of two required by Notes), `CustomerId (customer_id): int?` (or `CustomerAttributes` for inline customer create). Per Notes, payment method not required for this sandbox; omit payment-profile params. Other fields: `CouponCode (coupon_code): string?`, `Reference (reference): string?`, `NextBillingAt (next_billing_at): DateTimeOffset?`, `DeferSignup (defer_signup): bool? = false`. | `SubscriptionResponse` (envelope) → field `Subscription` (wire: `subscription`) of type `Subscription?`. Key fields: `Id` (int?), `CustomerId` (customer_id): int?`, `ProductId (product_id): int?`, `State (state): SubscriptionState?`, `CurrentPeriodStartsAt (current_period_starts_at): DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `ActivatedAt (activated_at): DateTimeOffset?`, `CanceledAt (canceled_at): DateTimeOffset?`, `TrialStartedAt (trial_started_at): DateTimeOffset?`, `TrialEndedAt (trial_ended_at): DateTimeOffset?`. | **Case A** (`SdkException<CreateSubscriptionError>`). Accessors: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] — contains `Errors (errors): IReadOnlyList<string> !req` (list of error messages), `TryGetRawError(out RawError)` [fallback]. | None | `operations/Subscriptions.md` |
| **Step 5: Get user's subscriptions** `Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — `customerId`: required; used as path param. | No request body (path param only). Pass the Maxio `Customer.Id`. | `IReadOnlyList<SubscriptionResponse>` (array, no envelope). Each `SubscriptionResponse` (envelope) → field `Subscription` (wire: `subscription`) of type `Subscription?`. Same fields as Step 4. | **Case B** (`SdkException<RawError>`). No typed accessors; use `ex.Error.StatusCode`, `ex.Error.ReadAsString()`, `ex.Error.ReadAsJson<T>()`. | None (no pagination params). | `operations/Customers.md` |

---

### Enums Used

| Enum | Namespace | Members & Values | Used In |
|---|---|---|---|
| `SubscriptionState` | `MaxioAdvancedBilling.Models.Enums` | `Active (active)`, `Trialing (trialing)`, `Assessing (assessing)`, `PastDue (past_due)`, `Canceled (canceled)`, `Paused (paused)`, `Suspended (suspended)`, `Expired (expired)`, others. [Full list on `models/enums.md`](map/models/enums.md). | Response field `Subscription.State` — read only. |
| `IntervalUnit` | `MaxioAdvancedBilling.Models.Enums` | `Day (day)`, `Month (month)` | Response field `Product.IntervalUnit` — read only. |

---

### Client Construction & Auth

| Aspect | Details | Source |
|---|---|---|
| **Client class** | `MaxioAdvancedBillingClient` | `MaxioAdvancedBillingClient.cs` |
| **Options class** | `MaxioAdvancedBillingClientOptions` | `MaxioAdvancedBillingClientOptions.cs` |
| **Constructor** | `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` § "Getting a client" |
| **Namespaces** | `using MaxioAdvancedBilling;` (root), `using MaxioAdvancedBilling.Models;` (records), `using MaxioAdvancedBilling.Models.Enums;` (enums), `using MaxioAdvancedBilling.Errors;` (error types), `using MaxioAdvancedBilling.Core.Authentication.Basic;` (auth). | `sdk-map.md` § "Namespaces" |
| **Auth: Basic (required)** | `options.BasicAuth = new BasicAuthCredentials { Username = "<api_key>", Password = "x" }` — **`Username` = Maxio API key, `Password` = literal `"x"`**. Load from config key `Maxio:ApiKey` (from env var `MAXIO_API_KEY`). | `sdk-map.md` § "Servers & auth" |
| **Server environment & base URL** | `options.Environment = ServerEnvironment.Us` (default; for Maxio US hosting). Override base URL: `options.Server.Production.Us.BaseUrl = "https://{site}.chargify.com"` or custom host. For sandbox `cp-exp-1` under US: derive from config `Maxio:Subdomain` (env var `MAXIO_SITE_SUBDOMAIN`) — site = subdomain, so base URL = `https://{subdomain}.chargify.com`. | `sdk-map.md` § "Servers & auth"; operations/Subscriptions.md Notes |

---

### Error Accessors & Payload Types (for Error Boundaries)

| Operation | Error Case | Accessor Method | Payload Type (wire name) | HTTP Status | Source |
|---|---|---|---|---|---|
| `ReadCustomerByReference` | B | N/A (use `RawError.StatusCode`, `.ReadAsString()`, `.ReadAsJson<T>()`) | `RawError` | any non-2xx | `operations/Customers.md` |
| `CreateCustomer` | A | `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` | `CustomerErrorResponse1` (contains `Errors (errors): Errors?`) | 422 | `operations/Customers.md`; `records-2-Cr-Ne.md` |
| `CreateCustomer` | A | `TryGetRawError(out RawError)` | `RawError` | fallback (other statuses) | `operations/Customers.md` |
| `ListProducts` | B | N/A (use `RawError.*`) | `RawError` | any non-2xx | `operations/Products.md` |
| `CreateSubscription` | A | `TryGetErrorListResponse1(out ErrorListResponse1)` | `ErrorListResponse1` (contains `Errors (errors): IReadOnlyList<string> !req`) | 422 | `operations/Subscriptions.md`; `records-2-Cr-Ne.md` |
| `CreateSubscription` | A | `TryGetRawError(out RawError)` | `RawError` | fallback (other statuses) | `operations/Subscriptions.md` |
| `ListCustomerSubscriptions` | B | N/A (use `RawError.*`) | `RawError` | any non-2xx | `operations/Customers.md` |

---

### Configuration & Secrets (Application Responsibility)

| Setting | Config Key | Env Var | Default | Notes |
|---|---|---|---|---|
| Maxio API Key | `Maxio:ApiKey` | `MAXIO_API_KEY` | (required, no default) | Load via User Secrets (non-repo); set `BasicAuth.Username` in client options. |
| Site Subdomain | `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | (required, no default) | The `{site}` placeholder in base URL `https://{site}.chargify.com`. |
| Product Family Handle | `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | `"eshop-subscribe"` | For plan discovery; used by app logic to filter products (see Step 3 UNVERIFIED note). |
| Base URL (optional override) | `Maxio:BaseUrl` | (none documented) | `https://{site}.chargify.com` | Override `ServerOptions.Production.Us.BaseUrl` if using non-standard host. |
| Environment (US/EU) | (not in scope) | (none) | US | Derived from Maxio account (sandbox `cp-exp-1` is US-based). |

---

## Trap Notes

**⚠ Step 1 (client & DI)** — The SDK's client must be registered as a transient or reuse a single long-lived `HttpClient` instance via `IHttpClientFactory`. The `HttpClient` constructor parameter is the injection point; never create a new `HttpClient` per call. **MUST load `dotnet-client-initialization`** before wiring `AddMaxioAdvancedBillingClient` or the `new MaxioAdvancedBillingClient(httpClient, options)` constructor.

**⚠ Step 1 (auth setup)** — `BasicAuth.Username` must be set **before** the client is constructed, and the password is always the literal string `"x"`, not a hashed or derived value. Load the API key from User Secrets (configuration, not hardcoded). **MUST load `dotnet-authentication`** before setting credentials.

**⚠ Step 2a, 2b, 3, 4, 5 (operation calls)** — Every call is throw-based; no `…Result` or no-throw variants exist. Wrap each call in a `try/catch` targeting the exact exception type (Case A typed `SdkException<…>`, Case B `SdkException<RawError>`). Many optional params have no C# default; use **named arguments** when passing `null` or specific values to avoid positional-bind ambiguity. **MUST load `dotnet-calling-endpoints`** before writing the first operation call.

**⚠ Step 2b, 4 (request model construction)** — `CreateCustomerRequest` and `CreateSubscriptionRequest` are **envelopes**; the actual data lives in their nested field (`Customer` and `Subscription`, respectively). Omit fields you don't need — they are optional unless marked `!req`. Union types (e.g., `ComponentId1?` in `CreateSubscriptionComponent`) are built via factory methods or implicit conversion, never `new`. **MUST load `dotnet-models`** before building requests.

**⚠ Step 2a (404 on customer lookup)** — A 404 does not throw a typed error; `ReadCustomerByReference` returns a `SdkException<RawError>` with `.StatusCode == 404` on miss. The boundary must distinguish between "not found" (→ create new) and genuine errors (→ rethrow or log). **MUST load `dotnet-error-handling`** before writing the error boundary.

**⚠ Step 3 (product-family filtering)** — The `ListProducts` operation has no direct product-family-handle filter. The plan requires filtering by handle `"eshop-subscribe"`. **UNVERIFIED:** either (A) pre-query the family via `ProductFamilies.ReadProductFamilyByHandle("eshop-subscribe")` to get its product IDs, then call `ListProducts` with `filter.Ids`, or (B) post-filter by `product.ProductFamily.Handle` in-memory. The contract sheet cites this as UNVERIFIED because no map row explicitly guarantees a family handle filter exists on `ListProducts`. The implementer must confirm live or inspect the API docs.

**⚠ All error responses** — Two distinct deserialization-time `JsonException` paths exist and need opposite handling:
- A **drifted 2xx body** (missing a `required` field) surfaces as `JsonException` during deserialization, **not** as an `SdkException`. An SDK-exception-only catch ladder lets it escape.
- A **non-2xx body** that does not match the operation's typed error shape throws `JsonException` *while constructing* the error object, replacing the `SdkException` and destroying the HTTP status.
**MUST load `dotnet-error-handling`** before writing the error boundary — the skill covers both paths and how to map them to application-level responses.

---

## REQUIRED READING

Before implementation starts, load these skills **in order**. They are called out in the trap notes above and carry defaults, worked examples, and binding details the contract sheet deliberately omits.

| Skill | Step(s) | Notes |
|---|---|---|
| `dotnet-client-initialization` | 1 | HTTP client registration, DI, transient vs. long-lived, `IHttpClientFactory` integration. |
| `dotnet-authentication` | 1 | Basic auth credential wiring, loading from config, per-environment identity, refreshing. |
| `dotnet-calling-endpoints` | 2–5 | Operation signature navigation, required vs. optional params, named-argument patterns, async/await, `CancellationToken` usage. |
| `dotnet-models` | 2–5 | Record immutability, `required` fields, union construction and `TryGet…` accessors, enum creation via `FromValue()` or static members. |
| `dotnet-error-handling` | 2–5 | Typed vs. raw error cases, `TryGet…` accessor usage, `JsonException` on drifted 2xx / non-matching non-2xx, boundary mapping to HTTP/app response, no-throw patterns (absent in this SDK). |
| `dotnet-configuration-resilience` | 1, 4 | Retry/timeout options, per-attempt vs. total bounds, idempotence and POST retries, logging hooks, server-node override, base-URL wiring. |

---

## Assumptions & Blockers

### Assumptions

- **Maxio customer already exists for the user or will be created on-demand.** The plan uses `ReadCustomerByReference(reference: <eShopOnWeb user ID>)` to look up an existing customer; if 404, the app creates one via `CreateCustomer`. This is idempotent: subsequent calls with the same reference will find the customer and skip creation.
- **No payment method required at subscription creation.** Per scope ("payment method not required"), the plan omits payment-profile parameters in the `CreateSubscription` request. The sandbox allows subscriptions without a card on file. If production requires a payment method, the request model includes fields `PaymentProfileId`, `CreditCardAttributes`, `BankAccountAttributes` to supply one.
- **Product handles are stable and known to the app.** The plan assumes handles like `"eshop-pro"` and `"basic-plan"` are configured in Maxio and will not change mid-integration. The app uses them to build `CreateSubscription.ProductHandle`.
- **Customer reference can be the eShopOnWeb user ID (string).** The plan treats eShopOnWeb's internal user ID as the Maxio `customer_reference`, ensuring idempotent lookup and preventing duplicate customers. This assumes the ID is a string; if numeric, convert to string.
- **Metered usage (api-call component) is not created/recorded in this phase.** The plan covers core subscription CRUD; metered-usage ingestion (tracking API calls) is out of scope and noted in the schema but not implemented.

### Blockers

- **No product-family-handle filter confirmed on `ListProducts`.** The map does not name a `product_family_handle` query parameter for `ListProducts`. To filter by family `"eshop-subscribe"`, the app must either (A) pre-query the family via `ProductFamilies.ReadProductFamilyByHandle(…)` and then call `ListProducts(filter: { Ids: […] })`, or (B) post-filter in memory. **UNVERIFIED** — the implementer must confirm which approach works live or check the Maxio API docs directly. This is not a compile-time blocker (both approaches are expressible in the SDK), but a contract fact the map cannot settle.

---

## Summary

The contract sheet defines five operations across `Customers`, `Products`, and `Subscriptions` controllers needed to implement recurring-subscription billing as a parallel capability in eShopOnWeb:

1. **Idempotent customer lookup/creation** via `reference` (eShopOnWeb user ID).
2. **Plan discovery** from the `eshop-subscribe` product family (with UNVERIFIED family-filter caveat).
3. **Subscription creation** with product handle and customer ID (no payment profile).
4. **Subscription retrieval** for the logged-in user.

All operations are throw-based; error handling relies on typed accessors (Case A: `TryGet…`) and raw-error fallbacks (Case B). The SDK requires HTTP Basic auth (API key as username, `"x"` as password), long-lived `HttpClient` via `IHttpClientFactory`, and immutable record models with optional fields. Three dotnet-* skills are mandatory before code: authentication, calling-endpoints, models, and error-handling (the last two cover the traps that signatures cannot convey).
