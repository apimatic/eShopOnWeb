# Maxio Subscription Billing Integration — eShopOnWeb Reference App

**Scope & sequence:**
1. **Client & DI setup** — Register `MaxioAdvancedBillingClient` with transient or scoped lifetime via HTTP factory seam
2. **Load credentials from configuration** — `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:Environment`, `Maxio:BaseUrl` (optional)
3. **List subscription plans** — call `ListProductsForProductFamily()` with product family handle "eshop-subscribe" (ID 3023074)
4. **Idempotent customer creation** — call `CreateCustomer()` with userId as external reference; on 422 with duplicate reference, call `ReadCustomerByReference()` to reuse existing
5. **Create subscription** — call `CreateSubscription()` for the logged-in user to a selected plan product
6. **List user subscriptions** — call `ListCustomerSubscriptions()` to populate account dashboard
7. **Error boundary & resilience** — trap SDK exceptions at API layer; handle 422 (duplicate customer) by looking up existing; handle non-2xx or malformed JSON with proper HTTP error response

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### 1. List Subscription Plans (ListProductsForProductFamily)

| Aspect | Details | Source |
|--------|---------|--------|
| **Controller property** | `client.ProductFamilies` |  |
| **Signature** | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | `map/operations/ProductFamilies.md` |
| **Parameters (in order)** | `productFamilyId: "3023074"` (or handle "eshop-subscribe"), all date filters: pass `null`, `includeArchived: null`, `include: null`, `page: 1`, `perPage: 20`, `ct: default` | `map/operations/ProductFamilies.md` |
| **Returns** | `IReadOnlyList<ProductResponse>` — unwrap each to `ProductResponse.Product` (namespace `MaxioAdvancedBilling.Models`) to get `Product` record with fields: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `TrialPriceInCents (trial_price_in_cents): long?`, `TrialInterval (trial_interval): int?`, … (full model at `map/models/records-3-Of-Su.md`) | `map/models/records-3-Of-Su.md` |
| **Response envelope** | Each element is `ProductResponse { Product !req }` — read the nested `Product` field | `map/models/records-3-Of-Su.md` |
| **Error case** | Case A (typed): `SdkException<ListProductsForProductFamilyError>` with accessors: `TryGetString(out string)` [404 — product family not found], `TryGetRawError(out RawError)` [fallback] | `map/operations/ProductFamilies.md` |
| **Notes** | eShopOnWeb config supplies `MAXIO_DEFAULT_PRODUCT_FAMILY` (likely handle "eshop-subscribe" or ID 3023074); pass as `productFamilyId`. The API returns only non-archived, active products; no trial or setup fees in the sandbox seeding. `Interval` + `IntervalUnit` define billing cycle (e.g., `Interval: 1, IntervalUnit: Month` → monthly). | `map/operations/ProductFamilies.md` |

### 2. Create Customer (Idempotent via Reference)

| Aspect | Details | Source |
|--------|---------|--------|
| **Controller property** | `client.Customers` |  |
| **Signature** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` | `map/operations/Customers.md` |
| **Request model** | `CreateCustomerRequest { Customer: CreateCustomer !req }` → wrap a `CreateCustomer` record with required fields: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`; optional fields: `Reference (reference): string?`, `Organization (organization): string?`, `Address (address): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `CcEmails (cc_emails): string?`, `Address2 (address_2): string?`, `Locale (locale): string?`, `VatNumber (vat_number): string?`, `TaxExempt (tax_exempt): bool?` (namespace `MaxioAdvancedBilling.Models`) | `map/models/records-1-Ac-Cr.md` |
| **Wire name mapping** | All field names use snake_case on wire: `first_name`, `last_name`, `email`, `reference`, etc. (SDKgenerates `[JsonPropertyName]` automatically) | `map/models/records-1-Ac-Cr.md` |
| **Returns** | `CustomerResponse { Customer !req }` → unwrap to `Customer` record with fields: `Id (id): int?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, … (namespace `MaxioAdvancedBilling.Models`) | `map/models/records-2-Cr-Ne.md` |
| **Response envelope** | Unwrap `CustomerResponse.Customer` to read the customer ID and reference back | `map/models/records-2-Cr-Ne.md` |
| **Error case** | Case A (typed): `SdkException<CreateCustomerError>` with accessors: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422 — validation error, including duplicate reference], `TryGetRawError(out RawError)` [fallback] | `map/operations/Customers.md` |
| **Idempotent flow** | **Caller passes `Reference: userId` (from app identity).** On 422 with error message indicating duplicate reference, catch the exception and call `ReadCustomerByReference(reference: userId, ct: default)` to retrieve the existing customer instead of failing. Store the returned customer ID for subscription creation. If the 422 is not a duplicate-reference error, re-throw as a genuine validation failure. | `map/operations/Customers.md` |
| **Required fields** | `FirstName`, `LastName`, `Email` (per Notes: "only validation restriction is unique reference if provided"); pass `Reference` = userId; all other fields optional | `map/operations/Customers.md` |
| **Notes** | The reference field is the app's own unique identifier (userId); Maxio also generates an `id` (auto-increment). The integration uses reference for idempotency: if a call with the same reference is retried, Maxio rejects it with 422. The plan catches that, looks up the customer by reference, and proceeds. Email, first/last name are required; address/country format is ISO-2 for country (US, CA, etc.) per Notes. | `map/operations/Customers.md` |

### 3. Create Subscription

| Aspect | Details | Source |
|--------|---------|--------|
| **Controller property** | `client.Subscriptions` |  |
| **Signature** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | `map/operations/Subscriptions.md` |
| **Request model** | `CreateSubscriptionRequest { Subscription: CreateSubscription !req }` → wrap a `CreateSubscription` record; key fields: `CustomerId (customer_id): int?`, `ProductHandle (product_handle): string?` OR `ProductId (product_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?` OR `ProductPricePointId (product_price_point_id): int?`, `Reference (reference): string?`, `CouponCode (coupon_code): string?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` (enum), `Components (components): IReadOnlyList<CreateSubscriptionComponent>?`, … (full at `map/models/records-2-Cr-Ne.md`, namespace `MaxioAdvancedBilling.Models`) | `map/models/records-2-Cr-Ne.md` |
| **Wire name mapping** | All field names use snake_case: `customer_id`, `product_handle`, `product_id`, `product_price_point_id`, `payment_collection_method`, `reference`, etc. | `map/models/records-2-Cr-Ne.md` |
| **Required fields per Notes** | `CustomerId` (the Maxio customer ID from step 2) and `ProductHandle` or `ProductId` (the product to subscribe to); everything else optional. Pass `ProductHandle: "eshop-pro"` (or `ProductId: 7126957`) for Pro Plan, `ProductHandle: "basic-plan"` (or `ProductId: 7126958`) for Basic Plan. Payment method NOT required per brief. | `map/operations/Subscriptions.md` |
| **Returns** | `SubscriptionResponse { Subscription: Subscription? }` → unwrap to `Subscription` record with fields: `Id (id): int?`, `State (state): SubscriptionState?` (enum), `CustomerId (customer_id): int?`, `ProductId (product_id): int?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `ActivatedAt (activated_at): DateTimeOffset?`, `CreatedAt (created_at): DateTimeOffset?`, `Reference (reference): string?`, … (namespace `MaxioAdvancedBilling.Models`) | `map/models/records-3-Of-Su.md` |
| **Response envelope** | Unwrap `SubscriptionResponse.Subscription` to read subscription ID and state | `map/models/records-3-Of-Su.md` |
| **Error case** | Case A (typed): `SdkException<CreateSubscriptionError>` with accessors: `TryGetErrorListResponse1(out ErrorListResponse1)` [422 — validation error, e.g., missing required payment method, product not found], `TryGetRawError(out RawError)` [fallback]; Note: per brief, payment method NOT required, so 422 is unexpected unless customer or product is invalid | `map/operations/Subscriptions.md` |
| **Enum: CollectionMethod** | Values: `CollectionMethod.Automatic` (wire: "automatic"), `CollectionMethod.Remittance` (wire: "remittance"), `CollectionMethod.Prepaid` (wire: "prepaid"), `CollectionMethod.Invoice` (wire: "invoice"). Per brief, payment method is NOT required for subscription creation, so this is optional; pass `null` or omit. | `map/models/enums.md` |
| **Enum: SubscriptionState** | Values after creation: typically `SubscriptionState.Active` (wire: "active"), also possible: `SubscriptionState.PendingRenewal`, `SubscriptionState.Trialing`, `SubscriptionState.PastDue`, `SubscriptionState.Canceled`, `SubscriptionState.Expired`, etc. (full list: namespace `MaxioAdvancedBilling.Models.Enums`) | `map/models/enums.md` |
| **Notes** | Maxio will activate the subscription immediately (no trial per brief). The subscription is charged to the customer's payment method if one is on file; if not, it enters a pending/unpaid state. In test sandbox with no payment method, subscription activates but invoice is unpaid. The API returns the subscription ID (needed for step 4) and activation state. | `map/operations/Subscriptions.md` |

### 4. List User Subscriptions

| Aspect | Details | Source |
|--------|---------|--------|
| **Controller property** | `client.Customers` |  |
| **Signature** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | `map/operations/Customers.md` |
| **Parameters** | `customerId: <int from step 2>`, `ct: default` | `map/operations/Customers.md` |
| **Returns** | `IReadOnlyList<SubscriptionResponse>` — unwrap each to `SubscriptionResponse.Subscription` (or null if missing) to get `Subscription` record with: `Id (id): int?`, `State (state): SubscriptionState?`, `Product (product): Product?`, `CustomerId (customer_id): int?`, `ProductId (product_id): int?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, … | `map/models/records-3-Of-Su.md` |
| **Response envelope** | Each element is `SubscriptionResponse` with an optional nested `Subscription` field; unwrap and check for null | `map/models/records-3-Of-Su.md` |
| **Error case** | Case B (raw): `SdkException<RawError>` with accessors: `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?` (no typed accessors) | `map/operations/Customers.md` |
| **Notes** | Returns all active and inactive subscriptions for the customer (no filtering on state). Empty list if customer has no subscriptions. Does NOT include paginated results; all subscriptions are returned in one call per the signature (no `page`/`perPage` parameters). | `map/operations/Customers.md` |

### 5. Read Customer by Reference (Idempotency Recovery)

| Aspect | Details | Source |
|--------|---------|--------|
| **Controller property** | `client.Customers` |  |
| **Signature** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | `map/operations/Customers.md` |
| **Query param** | `reference` (wire: "reference") — pass the userId | `map/operations/Customers.md` |
| **Returns** | `CustomerResponse { Customer !req }` → unwrap to `Customer` record with `Id (id): int?`, `Reference (reference): string?`, … | `map/models/records-2-Cr-Ne.md` |
| **Response envelope** | Unwrap `CustomerResponse.Customer` | `map/models/records-2-Cr-Ne.md` |
| **Error case** | Case B (raw): `SdkException<RawError>` with accessors: `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` (no typed accessors). 404 if reference not found. | `map/operations/Customers.md` |
| **Notes** | Called only when `CreateCustomer()` fails with 422 indicating a duplicate reference. Looks up and returns the existing customer ID so subscription creation can proceed. | `map/operations/Customers.md` |

---

## Enum Values (Used in Contract)

### SubscriptionState
- `SubscriptionState.Active` (wire: "active") — subscription is active
- `SubscriptionState.Trialing` (wire: "trialing") — on trial (not applicable in eShopOnWeb; no trial per brief)
- `SubscriptionState.PastDue` (wire: "past_due") — payment past due
- `SubscriptionState.Canceled` (wire: "canceled") — subscription canceled
- `SubscriptionState.Expired` (wire: "expired") — subscription expired
- Full set at namespace `MaxioAdvancedBilling.Models.Enums`, source `Models/Enums/SubscriptionState.cs`

### CollectionMethod
- `CollectionMethod.Automatic` (wire: "automatic") — charge automatically
- `CollectionMethod.Remittance` (wire: "remittance") — customer invoiced, pays manually
- `CollectionMethod.Prepaid` (wire: "prepaid") — prepaid balance
- `CollectionMethod.Invoice` (wire: "invoice") — legacy invoice method
- Namespace `MaxioAdvancedBilling.Models.Enums`, source `Models/Enums/CollectionMethod.cs`

---

## Client Construction & Configuration

### DI Registration (Recommended)

```csharp
// In Startup/Program.cs
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;

services.AddMaxioAdvancedBillingClient(options =>
{
    var config = services.BuildServiceProvider().GetRequiredService<IConfiguration>();
    options.BasicAuth = new BasicAuthCredentials
    {
        Username = config["Maxio:ApiKey"], // API key from env var
        Password = "x"                      // Literal "x" per SDK spec
    };
    // Optionally override environment:
    // options.Environment = ServerEnvironment.Eu; // or Us (default)
    
    // Optionally override base URL:
    // options.Server.Production.Us.BaseUrl = config["Maxio:BaseUrl"];
});
```

### Configuration Keys

| Key | Example Value | Purpose | Source |
|-----|---|---------|--------|
| `Maxio:ApiKey` | (from env var `MAXIO_API_KEY`) | HTTP Basic auth username | App-supplied |
| `Maxio:Subdomain` | "cp-exp-1" (from env var `MAXIO_SITE_SUBDOMAIN`) | Site subdomain for base URL (`https://cp-exp-1.chargify.com`) | App-supplied |
| `Maxio:Environment` | "Us" or "Eu" (from env var `MAXIO_ENVIRONMENT`, default "Us") | Hosting region | App-supplied; maps to `ServerEnvironment.Us` or `ServerEnvironment.Eu` |
| `Maxio:BaseUrl` | (optional, from env var `MAXIO_BASE_URL`) | Override base URL (for testing/mocking) | App-supplied; optional |
| `Maxio:DefaultProductFamilyHandle` | "eshop-subscribe" or ID 3023074 (from env var `MAXIO_DEFAULT_PRODUCT_FAMILY`) | Product family for subscription plans | App-supplied |

### Resilience & Retry

The SDK wraps `Polly` for automatic retry on transient failures (5xx, timeouts, transport errors). **MUST load `dotnet-configuration-resilience`** before wiring retry/timeout options; default is 2 retries with exponential backoff. `Timeout` is **per-attempt**, not per-call.

### HTTP Client Lifetime

The `HttpClient` injected into the SDK constructor **must be long-lived and reused** — register via `IHttpClientFactory` and inject the factory into the client builder. **Do NOT create a new `HttpClient` per request.** The SDK expects the same underlying handler across calls to benefit from connection pooling and cookie jar.

---

## Error Handling & Response Envelopes

### Response Envelope Pattern
**All create/read operations wrap their response in a single envelope field:**
- `CreateCustomer()` returns `CustomerResponse.Customer` (not bare `Customer`)
- `CreateSubscription()` returns `SubscriptionResponse.Subscription` (not bare `Subscription`)
- `ListProductsForProductFamily()` returns `IReadOnlyList<ProductResponse>`, each with `.Product` field

**Always unwrap one level** — read the inner field (e.g., `.Customer`, `.Subscription`, `.Product`); do NOT use the outer response type in domain logic.

### Case A vs Case B Exceptions

| Case | Type | Throws | Accessors | When |
|------|------|--------|-----------|------|
| **A (typed)** | `SdkException<{Operation}Error>` | Yes, on error status | `TryGet…(out …)` per operation (e.g., `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` for 422), plus `TryGetRawError(out RawError)` fallback | `CreateCustomer`, `CreateSubscription`, `ListProductsForProductFamily` (per map) |
| **B (raw)** | `SdkException<RawError>` | Yes, on error status | `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?` (no typed accessors) | `ListCustomerSubscriptions`, `ReadCustomerByReference`, `ListProductsForProductFamily` (per map row) |

### HTTP Status → Handling

| Status | Scenario | Handling |
|--------|----------|----------|
| **2xx** | Success | Deserialize response envelope, unwrap inner field, return domain model |
| **422** (Unprocessable Entity) | Validation error (Case A: `CreateCustomer` with duplicate reference) | Catch `SdkException<CreateCustomerError>`, call `TryGetCustomerErrorResponse1(out error)` to inspect field-level errors. If error message contains "reference", call `ReadCustomerByReference()` to reuse existing customer; else re-throw as validation failure. |
| **404** (Not Found) | Resource missing (e.g., product family, customer reference) | Case B: check `StatusCode` in `RawError`; return HTTP 404 to API caller or log and retry at higher level. |
| **5xx** | Server error | SDK retries automatically (per `RetryOptions.StatusCodesToRetry`); if all retries exhausted, throw `SdkException<RawError>`. Caller should return HTTP 5xx or retry with backoff. |
| **Timeout** | Network delay exceeds `Timeout` (per-attempt) | SDK retries up to `MaxRetries` times; if all exhausted, throw `HttpRequestException`. Caller should treat as transient and retry. |

### JSON Deserialization Exceptions

**IMPORTANT: Two distinct JsonException scenarios reach the error boundary:**

1. **Malformed 2xx body** (e.g., missing required field in response): `System.Text.Json.JsonException` is thrown **from deserialization**, NOT wrapped in `SdkException`. A catch-ladder that only catches `SdkException` will let this escape to the caller as a 5xx error. **MUST load `dotnet-error-handling`** to handle this case separately and return a deterministic HTTP error.

2. **Non-2xx body that doesn't match the typed error shape** (e.g., a 422 response body that isn't a valid `CreateCustomerError`): `JsonException` is thrown **while constructing the `{Operation}Error` object**, so it **replaces** the `SdkException` and the HTTP status is lost. A boundary that catches `JsonException` and blindly maps it to HTTP 500 then reports a transient outage can cause cascading retry loops on deterministic contract-violation errors.

**Solution:** Catch `JsonException` separately (before SDK exceptions) and log it as a contract/integration error. **MUST load `dotnet-error-handling`** before writing the boundary.

---

## Trap Notes

⚠ **Step 1 (Client & DI setup)** — The SDK client wraps an `HttpClient`; the `HttpClient` must be long-lived and reused via `IHttpClientFactory`, not created per request. A transient client with a new connection per call defeats connection pooling and incurs per-request handshake overhead. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ **Step 2 (Load credentials)** — The API key must be loaded from secure config (env vars, Key Vault, user-secrets), never hardcoded in source or config files. Maxio uses HTTP Basic auth: username = API key, password = literal string `"x"`. **MUST load `dotnet-authentication`** before constructing credentials.

⚠ **Step 2 (Customer creation idempotency)** — When `CreateCustomer()` fails with 422 and error reason is "duplicate reference", catch the exception and call `ReadCustomerByReference()` to retrieve the existing customer instead of propagating an error. This handles the case where the API call succeeded but the response was lost; retrying the request must not create a duplicate. **MUST load `dotnet-error-handling`** before writing exception logic.

⚠ **Step 3 & 4 (Calling operations)** — Many optional parameters in the SDK have no C# default value and must be passed explicitly (pass `null` to skip). Positional arguments can mis-bind. Use **named arguments** for clarity: `client.ProductFamilies.ListProductsForProductFamily(productFamilyId: "3023074", dateField: null, filter: null, …)`. **MUST load `dotnet-calling-endpoints`** before the first call.

⚠ **Step 2–4 (Models)** — Enums are **not** C# enums; they are `StringEnum<T>` records. Construct with static members: `CollectionMethod.Automatic`, not `CollectionMethod.Automatic.ToString()`. Read back via `TryGet…()` pattern on unions. **MUST load `dotnet-models`** when request/response fields are not plain string/number.

⚠ **Step 7 (Resilience)** — `Timeout` bounds **per-attempt**, not the entire call. On a transport failure (e.g., `HttpRequestException`), the SDK **retries on all HTTP methods** (including POST), so a non-idempotent write can execute more than once. `MaxRetries = 0` is rejected; the floor is 1. Disable retries only in application logic (e.g., skip customer creation if a write-once flag is set). **MUST load `dotnet-configuration-resilience`** before tuning retry/timeout.

⚠ **Step 7 (Error boundary)** — `System.Text.Json.JsonException` reaches the boundary from two directions: (1) malformed **2xx** body (missing required member) surfaces from deserialization, **not** as `SdkException` — a SDK-exception-only catch ladder lets it escape; (2) **non-2xx** body that doesn't match the typed error shape throws `JsonException` while the error object is being constructed, so `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed. A boundary that maps every `JsonException` to 500 then reports a transient outage causes retry loops on deterministic failures. **MUST load `dotnet-error-handling`** before writing the boundary — both scenarios are documented there with resolution patterns.

---

## REQUIRED READING

Load the following companion skills **before implementation starts**. The contract sheet deliberately does not carry their contents; each skill provides the full usage patterns, worked examples, and gotchas a one-line contract entry cannot.

| Skill | Step(s) | Purpose |
|-------|---------|---------|
| `dotnet-client-initialization` | 1 | Client construction, HTTP factory seam, DI registration, transient vs scoped lifetime |
| `dotnet-authentication` | 2 | HTTP Basic credentials (username = API key, password = "x"), loading from secure config, rotating/refreshing credentials |
| `dotnet-calling-endpoints` | 2–6 | Operation signatures, required vs optional parameters, named arguments, request envelope wrapping, async/await patterns, cancellation token usage |
| `dotnet-models` | 2–6 | Request/response model construction, enum (`StringEnum<T>`) building, union (`OneOf`/`AnyOf`) factories and `TryGet…()` accessors, immutable record setters (`init`-only) |
| `dotnet-error-handling` | 7 | Exception types (typed `SdkException<{Operation}Error>` vs raw `SdkException<RawError>`), `TryGet…()` accessors, `JsonException` on malformed 2xx and non-2xx contract violations, error boundary patterns, retry logic |
| `dotnet-configuration-resilience` | 1, 7 | Retry policy (`RetryOptions`: `MaxRetries`, `StatusCodesToRetry`, `HttpMethodsToRetry`), timeout semantics (per-attempt, not per-call), base URL override, exponential backoff, logging hooks |
| `dotnet-testing` | — | Mocking/stubbing the SDK (via `HttpClient` seam), test frameworks, test doubles for operations |

---

## Assumptions & Blockers

**Assumptions:**
- The app supplies all credentials via configuration/env vars (`MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_ENVIRONMENT`) — never hardcoded.
- Product Family "eshop-subscribe" (ID 3023074) and products "eshop-pro" (ID 7126957) and "basic-plan" (ID 7126958) are pre-seeded on the Maxio site `cp-exp-1` and will not change during the integration run.
- Metered component "api-call" (ID 3057195) is defined but not used in the hero flow (MVP scope). If future work adds usage tracking, the plan can be extended with `SubscriptionComponents` operations.
- User identity (logged-in shopper) is available to the API layer (e.g., via JWT claims `sub` or custom claim); the app maps it to a userId string for the Maxio `reference` field.
- In-memory DB or session store persists the Maxio customer ID and subscription ID only within a single app session; no durable persistence to SQL Server (per brief "in-memory DB only").
- Payment method is NOT required for subscription creation (per brief); subscriptions activate without charging immediately. In production, this would typically require a payment method; the sandbox allows it for testing.
- API callers (front-end or other services) authenticate with JWT (or equivalent) before calling the subscription endpoints; the API layer extracts user identity and passes it to Maxio as the customer `reference`.

**Blockers:**
- None identified. All operations, models, error types, and enum values are present in the SDK map. The plan is ready for implementation.

