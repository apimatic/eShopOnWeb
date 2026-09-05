# Maxio Advanced Billing Integration Plan — eShopOnWeb Recurring Subscriptions

## Scope & Sequence

The integration adds three HTTP endpoints to expose subscription plans and management:

1. **List available subscription plans** — read product catalog filtered by the configured product family handle
2. **Create a subscription** — idempotent customer creation + subscription enrollment for authenticated user
3. **Retrieve user's subscriptions** — list all active/inactive subscriptions for a customer

The hero flow is: authenticate, browse plans, subscribe to plan, confirm enrollment in account.

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operation 1: Idempotent Customer Lookup/Creation

| Field | Value |
|-------|-------|
| **Controller** | `client.Customers` |
| **Method** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` |
| **Purpose** | Look up existing Maxio customer by eShopOnWeb user ID; used before `CreateCustomer` to ensure idempotency |
| **HTTP** | GET `/customers/lookup.json` |
| **Request** | Query param: `reference` (eShopOnWeb user ID) |
| **Response Envelope** | `CustomerResponse { Customer (customer): Customer !req }` |
| **Response Inner** | `Customer` fields: `Id (id): int?`, `Email (email): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Reference (reference): string?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?` — and 20+ other fields |
| **Error** | `SdkException<RawError>` — Case B (no typed accessors) |
| **Status on lookup miss** | 404 (customer not found) — handle with `ex.Error.StatusCode == HttpStatusCode.NotFound` |
| **Notes** | The `reference` value in Maxio must match eShopOnWeb's authenticated user ID for this to work; this is the "lookup" pattern, not a search |
| **Source** | `operations/Customers.md` |

### Operation 2: Create Customer (Fallback, Only If Lookup Misses)

| Field | Value |
|-------|-------|
| **Controller** | `client.Customers` |
| **Method** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` |
| **Purpose** | Create a new Maxio customer record for a new eShopOnWeb user |
| **HTTP** | POST `/customers.json` |
| **Request Model** | `CreateCustomerRequest` (in namespace `MaxioAdvancedBilling.Models`) |
| **Request Structure** | `{ subscription: { customer: CreateCustomer !req } }` where `CreateCustomer` is: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?`, `Organization (organization): string?`, `Address (address): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?` |
| **Required Fields in CreateCustomer** | `FirstName`, `LastName`, `Email` (all three must be set in the object initializer); `Reference` is optional but **MUST** be set to the eShopOnWeb user ID for idempotency on future lookups |
| **Response Envelope** | `CustomerResponse { Customer (customer): Customer !req }` |
| **Response Inner** | `Customer` with all fields as above; focus on `Id (id): int?` (the Maxio customer ID) and `Reference (reference): string?` (echo of the reference you sent) |
| **Error** | `SdkException<CreateCustomerError>` — Case A (typed) |
| **Error Accessors** | `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] (validation errors, e.g. duplicate reference) · `TryGetRawError(out RawError)` [fallback] |
| **Notes** | The `Reference` field is your hook for idempotency: set it to the authenticated user's ID from eShopOnWeb. If a customer with the same reference already exists (in a retry scenario), Maxio returns 422 with message "You may only create one customer for a given reference value." Handle this by catching the 422 and re-running the lookup. |
| **Source** | `operations/Customers.md` |

### Operation 3: List Subscription Plans (Filter by Product Family)

| Field | Value |
|-------|-------|
| **Controller** | `client.Products` |
| **Method** | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| **Purpose** | List all products in a site; filter programmatically to the product family (e.g. `eshop-subscribe`) and return active plans only |
| **HTTP** | GET `/products.json` |
| **Query Params** | All params nullable; you will pass: `dateField: null`, `filter: null`, `endDate: null`, ... (all null to skip), `page: 1`, `perPage: 20` to retrieve all plans (or paginate if many); `includeArchived: false` to exclude archived products |
| **Response** | `IReadOnlyList<ProductResponse>` — each wraps `ProductResponse { Product (product): Product !req }` |
| **Product Inner Fields** | `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `ProductFamily (product_family): ProductFamily?`, `TrialPriceInCents (trial_price_in_cents): long?`, `TrialInterval (trial_interval): int?`, `ExpirationInterval (expiration_interval): int?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?` — and 15+ other fields |
| **ProductFamily Inner** | `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?` |
| **Filtering** | **Do this in your app code**, not via API param: receive all products for the site, then filter to `product_family.handle == "eshop-subscribe"` (or the configured handle from settings). Note: the products endpoint does NOT take a family filter; you must post-filter. |
| **Error** | `SdkException<RawError>` — Case B |
| **Pagination** | Manual via `page` and `perPage`; default page=1, perPage=20. Since we expect only 2–3 plans, a single fetch should suffice. |
| **Notes** | Products returned include archived ones unless you pass `includeArchived: false`. Sandbox site `cp-exp-1` has product family `eshop-subscribe` (ID 3023074) with two products: Pro Plan `eshop-pro` (ID 7126957, $299.00/mo) and Basic Plan `basic-plan` (ID 7126958, $29.00/mo). Both have no trial, no setup fee. |
| **Source** | `operations/Products.md` |

### Operation 4: Create Subscription

| Field | Value |
|-------|-------|
| **Controller** | `client.Subscriptions` |
| **Method** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` |
| **Purpose** | Enroll a customer in a subscription (product + plan) |
| **HTTP** | POST `/subscriptions.json` |
| **Request Model** | `CreateSubscriptionRequest` (namespace `MaxioAdvancedBilling.Models`) |
| **Request Structure** | `{ subscription: CreateSubscription !req }` where `CreateSubscription` contains 50+ optional/required fields. **Key fields to set**: `CustomerId (customer_id): int?` (the Maxio customer ID from Operation 2) OR `CustomerReference (customer_reference): string?` (the eShopOnWeb user ID, only if customer already exists in Maxio); `ProductHandle (product_handle): string?` OR `ProductId (product_id): int?` (the plan handle/ID); `ProductPricePointHandle (product_price_point_handle): string?` (optional, use default price point if omitted); `Reference (reference): string?` (your internal subscription reference, e.g. `{userId}:{planHandle}` for idempotency on retries) |
| **Minimal Request (for plans with no payment method required)** | Set: `customer_id` (from Maxio), `product_handle` or `product_id`, `reference` (for idempotency). Payment info NOT required per task constraints (product family has `payment_method NOT required`). |
| **Full CreateSubscription Field List (excerpt)** | `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `CustomerId (customer_id): int?`, `CustomerReference (customer_reference): string?`, `Reference (reference): string?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` (enum: `Automatic`, `Remittance`, `Prepaid`, `Invoice`), `Components (components): IReadOnlyList<CreateSubscriptionComponent>?`, `CouponCode (coupon_code): string?`, `ReceivesInvoiceEmails (receives_invoice_emails): string?`, `DeferSignup (defer_signup): bool? = false`, `NextBillingAt (next_billing_at): DateTimeOffset?`, `InitialBillingAt (initial_billing_at): DateTimeOffset?`, ... and 40+ others |
| **Response Envelope** | `SubscriptionResponse { Subscription (subscription): Subscription !req }` |
| **Subscription Inner (Fields to Read)** | `Id (id): int?` (Maxio subscription ID), `State (state): SubscriptionState?` (enum: `Active`, `Trialing`, `PastDue`, `Suspended`, `Canceled`, etc.), `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?` (the next billing date), `ProductPriceInCents (product_price_in_cents): long?` (recurring price in cents), `Reference (reference): string?` (echo of your reference), `Customer (customer): Customer?`, `Product (product): Product?`, `CouponCodes (coupon_codes): IReadOnlyList<string>?`, `CreatedAt (created_at): DateTimeOffset?` — and 40+ other fields |
| **Error** | `SdkException<CreateSubscriptionError>` — Case A (typed) |
| **Error Accessors** | `TryGetErrorListResponse1(out ErrorListResponse1)` [422] (validation errors: bad product, bad customer, missing required fields) · `TryGetRawError(out RawError)` [fallback] — `ErrorListResponse1` has `Errors (errors): IReadOnlyList<string> !req`, so unpack as array of strings |
| **Idempotency Note** | Set the `Reference` field to something like `{userId}:{productHandle}:{timestamp}` or `{userId}:{productHandle}`. On retry (e.g. 422 due to network glitch), the server may reject with "subscription reference must be unique" or similar. To make it truly idempotent, you must either: (a) **query for existing subscriptions by reference before creating**, using `FindSubscription(string? reference, CancellationToken ct)` (Operation 5 below), or (b) **catch the 422 and re-query**. |
| **Source** | `operations/Subscriptions.md` |

### Operation 5: Find Subscription by Reference (Pre-Create Check for Idempotency)

| Field | Value |
|-------|-------|
| **Controller** | `client.Subscriptions` |
| **Method** | `FindSubscription(string? reference, CancellationToken ct = default)` |
| **Purpose** | Before creating a subscription, check if one with the same reference already exists (handles retry/duplicate scenarios) |
| **HTTP** | GET `/subscriptions/lookup.json?reference={ref}` |
| **Request** | Query param: `reference` (your internal reference, e.g. `userId:planHandle`) |
| **Response** | `SubscriptionResponse { Subscription (subscription): Subscription !req }` |
| **Error** | `SdkException<FindSubscriptionError>` — Case A (typed) with `TryGetNoContent(out RawError)` [404] (subscription not found) · `TryGetRawError(out RawError)` [fallback] |
| **Status on miss** | 404 — handle by catching and proceeding to `CreateSubscription` |
| **Notes** | If the subscription already exists, return it (idempotent); if not found (404), proceed to `CreateSubscription`. This is the recommended pattern for handling retries. |
| **Source** | `operations/Subscriptions.md` |

### Operation 6: List Customer Subscriptions

| Field | Value |
|-------|-------|
| **Controller** | `client.Customers` |
| **Method** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` |
| **Purpose** | Retrieve all subscriptions for a customer (used by `GET /api/my-subscriptions`) |
| **HTTP** | GET `/customers/{customer_id}/subscriptions.json` |
| **Request** | Path param: `customerId` (Maxio customer ID, from Operation 2) |
| **Response** | `IReadOnlyList<SubscriptionResponse>` — each wraps the same `SubscriptionResponse` as Operation 4 |
| **Subscription Inner** | Same as Operation 4 |
| **Error** | `SdkException<RawError>` — Case B |
| **Notes** | Includes all subscriptions (active, canceled, expired, etc.); filter in your app if you want only active ones |
| **Source** | `operations/Customers.md` |

---

## Enum Values (Used in Requests/Responses)

### `IntervalUnit` (wire name → C# member)

| Wire Value | C# Member | Used In |
|---|---|---|
| `day` | `Day` | Product/plan billing interval (e.g. daily, monthly) |
| `month` | `Month` | |

### `SubscriptionState` (wire name → C# member)

| Wire Value | C# Member | Used In |
|---|---|---|
| `active` | `Active` | Subscription state (plan is active and paid) |
| `canceled` | `Canceled` | Subscription state (canceled by customer or dunning) |
| `trialing` | `Trialing` | Subscription state (in trial period) |
| `past_due` | `PastDue` | Subscription state (payment failed, awaiting retry) |
| `pending` | `Pending` | Subscription state (awaiting signup completion or payment) |
| `assessing` | `Assessing` | Subscription state (internal, transient) |
| `suspended` | `Suspended` | Subscription state (dunning in progress) |
| `expired` | `Expired` | Subscription state (product expired) |
| `awaiting_signup` | `AwaitingSignup` | Subscription state (awaiting customer signup action) |

### `CollectionMethod` (wire name → C# member)

| Wire Value | C# Member | Notes |
|---|---|---|
| `automatic` | `Automatic` | Payment collected via stored payment method on billing date |
| `remittance` | `Remittance` | Invoice sent; customer remits payment |
| `prepaid` | `Prepaid` | Payment collected upfront; balance used for subscriptions |
| `invoice` | `Invoice` | Legacy; invoice sent on billing date |

---

## Client Construction & Configuration

| Aspect | Value | Source |
|---|---|---|
| **Package** | `AsadAli.AdvancedBilling.Sdk` (NuGet) | `sdk-map.md` |
| **Root Namespace** | `MaxioAdvancedBilling` | `sdk-map.md` |
| **Client Class** | `MaxioAdvancedBillingClient` | `sdk-map.md` |
| **Options Class** | `MaxioAdvancedBillingClientOptions` | `sdk-map.md` |
| **Auth Scheme** | HTTP Basic — `BasicAuthCredentials { Username = apiKey, Password = "x" }` | `sdk-map.md` |
| **Environment** | `ServerEnvironment.Us` (default) for US-hosted sandbox `cp-exp-1` | `sdk-map.md` |
| **Base URL Template** | `https://{site}.chargify.com` → `https://cp-exp-1.chargify.com` | `sdk-map.md` |
| **Site Override** | `options.Server.Production.Us.Site = "cp-exp-1"` | `sdk-map.md` |
| **Namespaces to Use** | `MaxioAdvancedBilling`, `MaxioAdvancedBilling.Models`, `MaxioAdvancedBilling.Models.Enums`, `MaxioAdvancedBilling.Errors`, `MaxioAdvancedBilling.Core.Configuration` | `sdk-map.md` |

### Configuration Binding (from environment variables)

| Env Var | Binding Key | Type | Used For |
|---|---|---|---|
| `MAXIO_API_KEY` | `Maxio:ApiKey` | string | HTTP Basic username (the API key) |
| `MAXIO_SITE_SUBDOMAIN` | `Maxio:Subdomain` | string | Site subdomain (e.g. `cp-exp-1`); used in URL base: `https://{subdomain}.chargify.com` |
| `MAXIO_ENVIRONMENT` | `Maxio:Environment` | enum or string | `"Us"` or `"Eu"`; maps to `ServerEnvironment` |
| `MAXIO_DEFAULT_PRODUCT_FAMILY` | `Maxio:ProductFamilyHandle` | string | The handle to filter products by (e.g. `"eshop-subscribe"`); used in app code, not SDK config |
| (optional) `MAXIO_BASE_URL` | `Maxio:BaseUrl` | string (URL) | Override the full base URL (e.g. for local mock). If not set, defaults to template above. |

### Retry / Resilience Configuration

| Setting | Type | Default | Notes |
|---|---|---|---|
| `options.Retry.MaxRetries` | int | (SDK-defined) | Maximum number of retry attempts on transport failure or retryable status. **MUST load `dotnet-configuration-resilience`** to understand the semantics — `MaxRetries = 0` is rejected; the floor is 1. |
| `options.Retry.Delay` | TimeSpan | (SDK-defined) | Initial delay between retries; grows exponentially if `UseExponentialBackoff = true`. |
| `options.Retry.StatusCodesToRetry` | IReadOnlyList<HttpStatusCode> | (SDK-defined, includes 429, 503, etc.) | HTTP status codes that trigger a retry. **Note:** status gates **only the status**, so a 503 on a POST is retried if the status is in the list; however, a **transport failure** (e.g. connection timeout) is retried on **every** HTTP verb, including POST. |
| `options.Retry.HttpMethodsToRetry` | IReadOnlyList<HttpMethod> | (SDK-defined, includes GET, PUT, DELETE; may or may not include POST) | HTTP methods to retry on status match. |
| `options.Retry.Timeout` | TimeSpan? | (SDK-defined) | **Per-attempt** timeout, NOT total timeout for the whole call. **MUST load `dotnet-configuration-resilience`** before assuming the semantics. |
| `options.Retry.OnRetry` | Action<RetryAttempt>? | null | Optional callback fired on each retry attempt; useful for logging. |

---

## Models & Namespaces Reference

| Model | Namespace | Used By | Source |
|---|---|---|---|
| `CreateCustomer` | `MaxioAdvancedBilling.Models` | Operation 2 request body inner | `records-1-Ac-Cr.md` |
| `CreateCustomerRequest` | `MaxioAdvancedBilling.Models` | Operation 2 request envelope | `records-1-Ac-Cr.md` |
| `CustomerResponse` | `MaxioAdvancedBilling.Models` | Operations 1, 2 response envelope | `records-2-Cr-Ne.md` |
| `Customer` | `MaxioAdvancedBilling.Models` | Nested in `CustomerResponse` | `records-2-Cr-Ne.md` |
| `ProductResponse` | `MaxioAdvancedBilling.Models` | Operation 3 response item | `records-3-Of-Su.md` |
| `Product` | `MaxioAdvancedBilling.Models` | Nested in `ProductResponse` | `records-3-Of-Su.md` |
| `ProductFamily` | `MaxioAdvancedBilling.Models` | Nested in `Product` | `records-3-Of-Su.md` |
| `CreateSubscription` | `MaxioAdvancedBilling.Models` | Operation 4 request body inner | `records-2-Cr-Ne.md` |
| `CreateSubscriptionRequest` | `MaxioAdvancedBilling.Models` | Operation 4 request envelope | `records-2-Cr-Ne.md` |
| `SubscriptionResponse` | `MaxioAdvancedBilling.Models` | Operations 4, 5, 6 response envelope | `records-3-Of-Su.md` |
| `Subscription` | `MaxioAdvancedBilling.Models` | Nested in `SubscriptionResponse` | `records-3-Of-Su.md` |
| `SubscriptionState` | `MaxioAdvancedBilling.Models.Enums` | Enum in `Subscription.State` | `enums.md` |
| `IntervalUnit` | `MaxioAdvancedBilling.Models.Enums` | Enum in `Product.IntervalUnit` | `enums.md` |
| `CollectionMethod` | `MaxioAdvancedBilling.Models.Enums` | Enum in `CreateSubscription.PaymentCollectionMethod` | `enums.md` |
| `CreateCustomerError` | `MaxioAdvancedBilling.Errors` | Operation 2 error type | (not in models pages; error-only) |
| `CustomerErrorResponse1` | `MaxioAdvancedBilling.Models` | Payload of `CreateCustomerError.TryGetCustomerErrorResponse1(…)` [422] | `records-2-Cr-Ne.md` |
| `CreateSubscriptionError` | `MaxioAdvancedBilling.Errors` | Operation 4 error type | (not in models pages; error-only) |
| `ErrorListResponse1` | `MaxioAdvancedBilling.Models` | Payload of `CreateSubscriptionError.TryGetErrorListResponse1(…)` [422] | `records-2-Cr-Ne.md` |
| `RawError` | `MaxioAdvancedBilling.Core.ErrorResponse` | Fallback for all Case B errors | `sdk-map.md` |
| `BasicAuthCredentials` | `MaxioAdvancedBilling.Core.Authentication.Basic` | Auth setup | `sdk-map.md` |
| `ServerEnvironment` | `MaxioAdvancedBilling.Servers` | Environment selection | `sdk-map.md` |
| `RetryOptions` | `MaxioAdvancedBilling.Core.Configuration` | Retry configuration | `sdk-map.md` |

---

## REQUIRED READING

The following companion skills MUST be loaded before implementation starts. The contract sheet deliberately does not carry their contents; load each skill and read in full before writing code for the step it governs.

| Skill | Step It Governs | Purpose |
|---|---|---|
| `dotnet-client-initialization` | Client & DI setup | How to construct `MaxioAdvancedBillingClient`, register via DI, and ensure `HttpClient` is long-lived and reused |
| `dotnet-authentication` | Authentication | How to set `BasicAuthCredentials` (API key + password `"x"`), where to load credentials from env vars/config, and when to set them |
| `dotnet-calling-endpoints` | Calling SDK operations | How to call `client.{Controller}.{Operation}`, handle named arguments, cancellation tokens, and async/await |
| `dotnet-models` | Request/response models | How to construct records with `init` setters, set `required` fields, handle unions (factories + `TryGet…`), and read enums (not C# enums; use `StringEnum<T>` static members or `FromValue`) |
| `dotnet-error-handling` | Exception boundary | How to catch `SdkException<T>` (both Case A typed and Case B `RawError`), use error accessors like `TryGet…(out …)`, handle HTTP status codes, and distinguish between retry-able and terminal errors. **Both of these MUST be handled in your boundary:**  (1) a drifted or malformed 2xx body (missing `required` member) surfaces as `System.Text.Json.JsonException` from deserialization, not `SdkException` — SDK-exception-only catches let it escape; (2) a non-2xx body not matching the operation's `{Operation}Error` shape throws `JsonException` *during error object construction*, replacing the `SdkException` and losing the HTTP status — a boundary that maps all `JsonException` to 500 then reports a deterministic rejection as an outage causes a retry-loop on something that can never succeed. |
| `dotnet-configuration-resilience` | Retry, timeout, URL, logging | How to configure retries (max attempts, delay, exponential backoff, status trigger), timeout semantics (per-attempt, not total), base URL override, and logging. **Key gotcha:** `Timeout` bounds only per-attempt, and there is no built-in logging hook; `MaxRetries = 0` is rejected (floor is 1); a transport failure is retried on all verbs (POST included), so non-idempotent writes can execute more than once if you don't handle it. |
| `dotnet-testing` | Unit testing | How to stub the SDK's `HttpClient` seam, match the project's test framework, and assert on error cases |

Load these skills before writing any code. They carry defaults, semantics, worked examples, and gotchas that cannot fit in this sheet.

---

## Assumptions & Blockers

### Assumptions

1. **eShopOnWeb authenticated user ID is unique and stable.** The implementation uses this value as the `reference` on both customer and subscription records in Maxio for idempotency. If user IDs can change or are not unique across time, idempotency will break.

2. **No payment method is required to create a subscription.** The task states product family has `payment_method NOT required`. This is confirmed by the Maxio API: subscriptions can be created in `pending` or `trialing` state without a stored payment profile. If the requirement changes to collect payment at enrollment, the `CreateSubscription` call will need payment profile data (credit card, bank account, etc.), and the API may return 422 with payment-related errors.

3. **The configuration binding keys (`Maxio:ApiKey`, `Maxio:Subdomain`, etc.) are available via `IConfiguration` in the eShopOnWeb project.** The implementer must wire user-secrets or environment variables to these keys before the app starts.

4. **The authenticated user context is available in the HTTP request (e.g., from JWT claims or session).** The endpoints (`POST /api/subscriptions`, `GET /api/my-subscriptions`) assume the caller is authenticated and the user ID can be extracted from the request context.

### Blockers

**None.** The Maxio API surface and the eShopOnWeb architecture are aligned for this integration. All operations are mapped, error cases are understood, and configuration is clear.

---

## Notes on Idempotency & Retries

**Customer creation idempotency:** Use the `reference` field set to the eShopOnWeb user ID. Before calling `CreateCustomer`, always call `ReadCustomerByReference(userId)` first. If it succeeds, use that customer; if it 404s, proceed to `CreateCustomer`. This is the only way to guarantee idempotency (HTTP retries or replay can otherwise create duplicate customers).

**Subscription creation idempotency:** Use the `reference` field set to something like `{userId}:{productHandle}`. Before calling `CreateSubscription`, call `FindSubscription(reference)`. If it succeeds, use that subscription; if it 404s, proceed to `CreateSubscription`. The API does not prevent duplicate subscription creation if you retry the same payload — you must use the lookup pattern.

**Billing date accuracy:** The `NextAssessmentAt` field on `Subscription` is the next billing date. Always read and return this in the `GET /api/my-subscriptions` response so the user sees when the next charge will occur.
