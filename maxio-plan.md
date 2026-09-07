# Maxio Advanced Billing Integration Plan — eShopOnWeb Subscription Capability

## Scope & Sequence

1. **Client Setup** — initialize MaxioAdvancedBillingClient with Basic auth from configuration
2. **List Plans** — expose `GET /api/subscription-plans` returning available products and pricing
3. **Customer Idempotency** — lookup or create Maxio customer keyed by eShopOnWeb user ID (email as reference)
4. **Subscription Enrollment** — expose `POST /api/subscriptions` to link eShopOnWeb user to plan
5. **Subscription Retrieval** — expose `GET /api/my-subscriptions` to fetch user's active subscriptions with state and next-billing-date
6. **Response Mapping** — transform Maxio Subscription/Product/Customer models into eShopOnWeb API DTOs

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operations

| Operation | Signature | Request Model & Fields | Response Envelope & Inner Fields | Error Case | Pagination | Source |
|---|---|---|---|---|---|---|
| **ListProducts** | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | — (query only) | `IReadOnlyList<ProductResponse>` · each item wraps `Product (product): Product !req` · from `Product`: `Id`, `Name`, `Handle`, `PriceInCents (price_in_cents): long?`, `Interval`, `IntervalUnit` | Case B: `SdkException<RawError>` · `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | manual `page`+`perPage`; defaults page=1, perPage=20 | `operations/Products.md` |
| **ReadProductByHandle** | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | — (path param only) | `ProductResponse` · wraps `Product (product): Product !req` · from `Product`: `Id`, `Name`, `Handle`, `PriceInCents`, `Interval`, `IntervalUnit` | Case B: `SdkException<RawError>` · `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | none | `operations/Products.md` |
| **CreateCustomer** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` · `body` — nullable, no default → **must pass explicitly** | `CreateCustomerRequest` · wraps `Customer (customer): CreateCustomer !req` · fields: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?` | `CustomerResponse` · wraps `Customer (customer): Customer !req` · from `Customer`: `Id`, `Email`, `FirstName`, `LastName`, `Reference`, `CreatedAt`, `UpdatedAt` | Case A: `SdkException<CreateCustomerError>` · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422], `TryGetRawError(out RawError)` [fallback] | none | `operations/Customers.md` |
| **ReadCustomerByReference** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | — (query param only) | `CustomerResponse` · wraps `Customer (customer): Customer !req` · from `Customer`: `Id`, `Email`, `FirstName`, `LastName`, `Reference` | Case B: `SdkException<RawError>` · `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | none | `operations/Customers.md` |
| **ListCustomerSubscriptions** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | — (path param only) | `IReadOnlyList<SubscriptionResponse>` · each item wraps `Subscription (subscription): Subscription?` · from `Subscription`: `Id`, `State`, `ProductPriceInCents`, `ActivatedAt`, `CurrentPeriodEndsAt`, `NextAssessmentAt`, `CanceledAt`, `ExpiresAt`, `CreatedAt`, `UpdatedAt` | Case B: `SdkException<RawError>` · `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | none | `operations/Customers.md` |
| **CreateSubscription** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` · `body` — nullable, no default → **must pass explicitly** | `CreateSubscriptionRequest` · wraps `Subscription (subscription): CreateSubscription !req` · required fields: one of `ProductId (product_id): int?` or `ProductHandle (product_handle): string?` **[must select one]**; one of `CustomerId (customer_id): int?` or `CustomerReference (customer_reference): string?` **[must select one]**; optional: `ProductPricePointHandle (product_price_point_handle): string?`, `ProductPricePointId (product_price_point_id): int?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `Reference (reference): string?` | `SubscriptionResponse` · wraps `Subscription (subscription): Subscription?` · from `Subscription`: `Id`, `State`, `ProductPriceInCents (product_price_in_cents): long?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `ActivatedAt (activated_at): DateTimeOffset?`, `ExpiresAt (expires_at): DateTimeOffset?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, `CanceledAt (canceled_at): DateTimeOffset?` | Case A: `SdkException<CreateSubscriptionError>` · `TryGetErrorListResponse1(out ErrorListResponse1)` [422], `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |
| **ListSubscriptions** | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` · 14 nullable params before `page`/`perPage` — must pass explicitly (pass `null` to skip); defaults: page=1, perPage=20 | — (query only) | `IReadOnlyList<SubscriptionResponse>` · each item wraps `Subscription (subscription): Subscription?` · from `Subscription`: `Id`, `State`, `ProductPriceInCents`, `NextAssessmentAt`, `ActivatedAt`, `CustomerId (customer_id): int?`, `CanceledAt`, `ExpiresAt` | Case B: `SdkException<RawError>` · `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | manual `page`+`perPage` | `operations/Subscriptions.md` |

### Enums Used

| Enum | Namespace | Value Literals Needed | Notes | Source |
|---|---|---|---|---|
| `SubscriptionState` | `MaxioAdvancedBilling.Models.Enums` | `Active`, `Canceled`, `Suspended`, `PastDue`, `OnHold`, `Expired`, `Trialing`, `AwaitingSignup` | Subscription state filter and response state field; use static members `SubscriptionState.Active`, not wire strings | `models/enums.md` |
| `SubscriptionStateFilter` | `MaxioAdvancedBilling.Models.Enums` | `Active`, `Canceled`, `Expired` | Pass to `ListSubscriptions(state: ...)` to filter subscriptions by state | `models/enums.md` |
| `IntervalUnit` | `MaxioAdvancedBilling.Models.Enums` | `Month` (for sandbox plans) | Billing period unit; plans configured as month | `models/enums.md` |
| `CollectionMethod` | `MaxioAdvancedBilling.Models.Enums` | `Automatic`, `Invoice`, `Remittance`, `Prepaid` | Payment collection method; default is Automatic; sandbox has no payment required | `models/enums.md` |

### Client Construction & Configuration

| Step | Config Key | Type / Source | Binding / Default | Notes | Source |
|---|---|---|---|---|---|
| **Initialize HttpClient** | (app responsibility) | `System.Net.Http.HttpClient` | long-lived, reused via `IHttpClientFactory` | SDK wraps this; do NOT create new per request | companion skill |
| **API Key** | `Maxio:ApiKey` | string (from `MAXIO_API_KEY` env) | (required) | Basic auth username; no hardcode | `dotnet-authentication` |
| **Subdomain** | `Maxio:Subdomain` | string (from `MAXIO_SITE_SUBDOMAIN` env) | (required) | part of base URL; `https://{subdomain}.chargify.com` | `sdk-map.md` line 220 |
| **Product Family Handle** | `Maxio:ProductFamilyHandle` | string (from `MAXIO_DEFAULT_PRODUCT_FAMILY` env) | (required for filtering, if used) | Sandbox value: `eshop-subscribe` | (user-provided) |
| **Base URL Override** | `Maxio:BaseUrl` | string (optional, from `MAXIO_BASE_URL` env) | (optional; if set, use verbatim) | Override default derived from subdomain; e.g. for mock/dev host | `sdk-map.md` line 221 |
| **Environment** | (app responsibility) | `ServerEnvironment` enum | `ServerEnvironment.Us` (default) | US: `https://{site}.chargify.com`; EU: `https://{site}.ebilling.maxio.com` | `sdk-map.md` line 205 |

**Client construction (DI or factory):**
```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;

// In Startup / Program.cs, or at request boundary:
var httpClient = httpClientFactory.CreateClient(); // or System.Net.Http.HttpClient from DI
var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials 
    { 
        Username = config["Maxio:ApiKey"],     // API key
        Password = "x"                           // literal "x"
    },
    Environment = ServerEnvironment.Us,
    Server = new MaxioAdvancedBilling.Servers.ServerOptions 
    { 
        Production = new MaxioAdvancedBilling.Servers.ProductionOptions 
        { 
            Us = new MaxioAdvancedBilling.Servers.ProductionOptions.UsOptions
            { 
                Site = config["Maxio:Subdomain"],
                // BaseUrl override, if needed:
                BaseUrl = config["Maxio:BaseUrl"] ?? "https://{site}.chargify.com"
            }
        }
    }
};
var client = new MaxioAdvancedBillingClient(httpClient, options);
```

**Namespaces (using directives):**
```csharp
using System;
using System.Linq;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Api;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Servers;
using MaxioAdvancedBilling.Core.Authentication.Basic;
```

**IMPORTANT: Method names do not have "Async" suffix** — even though all operations return `Task<T>`, the SDK method names are NOT suffixed with `Async`. Call them directly (not `ListProductsAsync`, just `ListProducts`), await the result, and use `ct:` for the cancellation token parameter:
```csharp
// CORRECT:
var products = await _client.Products.ListProducts(null, null, null, null, null, null, null, null, ct: cancellationToken);
var response = await _client.Customers.CreateCustomer(request, ct: cancellationToken);

// WRONG:
// var products = await _client.Products.ListProductsAsync(...);  // ❌ Method does not exist
// var response = await _client.Customers.CreateCustomerAsync(...); // ❌ Method does not exist
```

**Response envelope access** — use capitalized property names:
```csharp
// ProductResponse wraps Product (capitalized, required, non-nullable)
var product = productResponse.Product;

// SubscriptionResponse wraps Subscription (capitalized, nullable)
var subscription = subscriptionResponse.Subscription;  // Could be null

// CustomerResponse wraps Customer (capitalized, required, non-nullable)
var customer = customerResponse.Customer;

// Handle nullable ID (int?):
var subscriptionId = subscription.Id ?? 0;  // null-coalesce if not required
// or: var subscriptionId = subscription.Id.GetValueOrDefault();
```

---

## Trap Notes

⚠ **All steps — SDK method naming: NO "Async" suffix** — The SDK methods return `Task<T>` but are NOT named with "Async" suffix, contrary to standard .NET naming convention. Call `ListProducts(...)`, `CreateCustomer(...)`, etc. directly (NOT `ListProductsAsync`, `CreateCustomerAsync`). This is a source-level fact, not inferable from documentation alone. **MUST verify method names** before first call.

⚠ **All steps — response envelope property access** — Response types wrap their payload in **capitalized property names** (not snake_case). Access `ProductResponse.Product` (not `.product`), `SubscriptionResponse.Subscription`, `CustomerResponse.Customer`. The `Subscription` property is nullable (`Subscription?`). **MUST use correct capitalization** or get `CS0117` (does not exist in type).

⚠ **Step 2 (List Plans) — query string pagination** — `ListProducts` uses manual `page` and `perPage` query params (not cursor-based); both default, but you may need to paginate manually if there are many products. **MUST load `dotnet-calling-endpoints`** before writing the first call to confirm required-but-nullable params and pagination.

⚠ **Step 3 (Customer Idempotency) — email vs. reference lookup** — the SDK provides `ReadCustomerByReference()` (query lookup) and CreateCustomer accepts an optional `Reference` field; idempotency strategy **must be chosen at the application boundary**, not in the SDK contract. Store the Maxio customer ID in the eShopOnWeb user record on first creation, or retrieve by reference every time; do not recreate on re-subscribe. **MUST load `dotnet-calling-endpoints`** to confirm the exact `reference` parameter name and how it binds.

⚠ **Step 4 (Subscription Enrollment) — customer ID vs. reference** — `CreateSubscription` accepts *either* `CustomerId` or `CustomerReference`, not both; if the customer does not yet exist, pass `CustomerReference` to allow auto-creation (but idempotency is then the caller's responsibility). **MUST load `dotnet-calling-endpoints`** to confirm parameter binding and whether `CustomerAttributes` can auto-create on the same call.

⚠ **Step 4 (Subscription Enrollment) — required field selection in CreateSubscription** — the operation signature lists many optional fields; which combination is actually required depends on the sandbox plan configuration. The map's Notes say "Payment information may be required…depending on the options for the Product"; the assumption is no payment method required on sandbox. Test the actual Maxio sandbox response to confirm. **MUST load `dotnet-models`** to understand union types and how to construct complex nested structures if payment is required later.

⚠ **Step 5 (Subscription Retrieval) — state filtering and response envelope depth** — `ListSubscriptions` has a `state` filter (`SubscriptionStateFilter`, not the full `SubscriptionState`); responses wrap each subscription in `SubscriptionResponse.Subscription?` (nullable inner). **MUST load `dotnet-calling-endpoints`** to confirm which state values filter correctly and whether missing/null subscriptions must be handled.

⚠ **All operations (error boundary)** — two `System.Text.Json.JsonException` cases reach this boundary from opposite directions and need opposite handling: (1) a drifted or malformed **2xx** body (missing `required` member) surfaces as `JsonException` from deserialization, **not** `SdkException` — an SDK-exception-only catch ladder lets it escape; (2) a **non-2xx** body that does not match the operation's generated error shape throws `JsonException` **while the error object is being constructed**, so the `JsonException` **replaces** the `SdkException` and HTTP status is destroyed — a boundary that maps every `JsonException` to 5xx then reports it as an outage, and a caller that retries 5xx, retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing the exception boundary.

⚠ **Step 2, 4, 5 (error handling) — Case A vs. Case B** — `ListProducts`, `ListCustomerSubscriptions`, `ListSubscriptions`, `ReadCustomerByReference` are **Case B** (generic `RawError` with no typed accessors); `CreateCustomer` and `CreateSubscription` are **Case A** (typed errors with `TryGet*` accessors). Do **not** assume all errors are the same shape. **MUST load `dotnet-error-handling`** to confirm per-operation error types and how to extract business-meaningful details from 422/400 responses.

⚠ **Step 1 (Client Setup) — HttpClient lifecycle and retry/timeout semantics** — the SDK's `RetryOptions` and `Timeout` do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register. A transport failure (`HttpRequestException`) is retried on every verb, including `POST`, so writes may execute more than once if retries are enabled. **MUST load `dotnet-configuration-resilience`** before wiring the client, to understand what Timeout actually bounds (per-attempt) and when retries are safe (i.e., is the plan-creation idempotent from Maxio's side?).

⚠ **Step 3 (Customer Idempotency) — Maxio customer creation concurrency** — if two concurrent requests for the same eShopOnWeb user hit `CreateCustomer` or `ReadCustomerByReference` simultaneously, both may attempt customer creation. The map notes say "you may only create one customer for a given reference value"; behavior when a duplicate reference arrives is not specified. Test concurrency and race conditions on the sandbox before production. **MUST load `dotnet-configuration-resilience`** for retry semantics and **load `dotnet-testing`** to stub/mock concurrent scenarios.

---

## Trap Note Summary — REQUIRED READING

The following companion skills **must be loaded before implementation starts**. The sheet deliberately does not carry their contents — each skill includes defaults, worked examples, gotchas, and configuration patterns specific to that layer. Load each **before writing code for the corresponding step**:

- **`dotnet-client-initialization`** — Step 1 · client construction, HttpClient lifecycle, DI registration
- **`dotnet-authentication`** — Step 1 · setting Basic credentials, API key sourcing, per-environment configuration
- **`dotnet-calling-endpoints`** — Steps 2–5 · operation signatures, required-but-nullable params, async/await, async-cancellation patterns
- **`dotnet-models`** — Steps 2–5 (as needed) · record field initialization, enum construction, union variants, JSON deserialization traps
- **`dotnet-error-handling`** — ALL steps · error envelope shape, Case A/B distinction, TryGet* accessors, `JsonException` handling (2xx body drift vs. non-2xx malformed body — these need opposite handling), `RawError` extraction
- **`dotnet-configuration-resilience`** — Step 1 · retry semantics (transport failure retries on all verbs, including POST), timeout bounds (per-attempt, not total), base-URL override, `HttpMethodsToRetry` gates only status-code triggers
- **`dotnet-testing`** — Steps 3–5 (for concurrent scenarios) · HttpClient mocking, response stubbing, concurrency testing

**Special caveat (both necessary in the FIRST sheet, not a later revision):**
- a drifted or malformed **2xx** body surfaces as `JsonException` from deserialization, **not** `SdkException` — an SDK-exception-only catch ladder lets it escape;
- a **non-2xx** body that does not match the operation's generated error shape throws `JsonException` *while constructing the error object*, **replacing** the `SdkException` and destroying the HTTP status — a boundary that maps all `JsonException` to 5xx then reports it as an outage, and a caller retrying 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing the exception boundary.

---

## Assumptions & Blockers

### Assumptions

- **Sandbox environment** — Maxio sandbox (not production) is the target; handles and IDs in the configuration are sandboxed.
- **No payment method required** — sandbox plans (`eshop-pro`, `basic-plan`) are configured without trial and no setup fee; the assumption is `CreateSubscription` succeeds without payment method data. **Verify on first test** — if 422 is returned saying payment is required, add payment method handling (out of scope for this plan).
- **JWT-authenticated caller identity** — the `eShopOnWeb` application supplies authenticated user ID from JWT token; caller identity is NOT the SDK's responsibility.
- **No customer hierarchy** — subscription will be at the customer level, not within a subscription group. (Subscription groups require different operations and are not in scope.)
- **Idempotency via application reference** — eShopOnWeb will store the Maxio customer ID; subsequent re-subscriptions will look it up by that ID or use `CustomerReference` to avoid duplicates. The SDK itself does not provide idempotency keys.
- **Synchronous call pattern** — integration will use async/await (per SDK design) but not webhook-driven or event-sourced subscription events.

### Blockers

**None identified.** The map covers all operations needed. Configuration bindings are from the environment. Error handling, concurrency, and edge cases are addressed via companion skills.

---

## Field Mapping Reference

### Plan Details → Product Response

| eShopOnWeb DTO Field | Maxio Response Path | Notes |
|---|---|---|
| `PlanId` | `ProductResponse.Product.Id` | Maxio product ID |
| `PlanName` | `ProductResponse.Product.Name` | Display name |
| `PlanHandle` | `ProductResponse.Product.Handle` | API handle for later reference |
| `PriceInCents` | `ProductResponse.Product.PriceInCents` | Price per billing period in cents |
| `BillingInterval` | `ProductResponse.Product.Interval` | Numeric interval (e.g., 1) |
| `BillingPeriod` | `ProductResponse.Product.IntervalUnit` | Enum `IntervalUnit` → "Month" / "Day" |
| `ExpiresNever` | (hardcoded) | Sandbox plans have no expiration configured; return constant true |

### Customer Creation → CreateCustomerRequest + CustomerResponse

| eShopOnWeb / Request | Maxio Request Path | Maxio Response Path | Notes |
|---|---|---|---|
| User Email | `CreateCustomerRequest.Customer.Email` | `CustomerResponse.Customer.Email` | Required field |
| User First Name | `CreateCustomerRequest.Customer.FirstName` | `CustomerResponse.Customer.FirstName` | Required field |
| User Last Name | `CreateCustomerRequest.Customer.LastName` | `CustomerResponse.Customer.LastName` | Required field |
| User ID (from JWT) | `CreateCustomerRequest.Customer.Reference` | `CustomerResponse.Customer.Reference` | Optional field; set to eShopOnWeb user ID for idempotency |
| — | — | `CustomerResponse.Customer.Id` | Maxio customer ID; store in eShopOnWeb user record |

### Subscription Enrollment → CreateSubscriptionRequest + SubscriptionResponse

| eShopOnWeb / Request | Maxio Request Path | Maxio Response Path | Notes |
|---|---|---|---|
| Plan Handle | `CreateSubscriptionRequest.Subscription.ProductHandle` | `SubscriptionResponse.Subscription.Product.Handle` | Use product handle if already known; else product ID |
| Maxio Customer ID | `CreateSubscriptionRequest.Subscription.CustomerId` | `SubscriptionResponse.Subscription.CustomerId` | OR use `CustomerReference` if customer is being created in-line |
| Subscription Reference (optional) | `CreateSubscriptionRequest.Subscription.Reference` | `SubscriptionResponse.Subscription.Reference` | Optional; can store eShopOnWeb subscription ID here |
| — | — | `SubscriptionResponse.Subscription.State` | Enum `SubscriptionState`; should be `Active` after successful creation |
| — | — | `SubscriptionResponse.Subscription.ActivatedAt` | Timestamp when subscription became active |
| — | — | `SubscriptionResponse.Subscription.NextAssessmentAt` | Next billing date (IMPORTANT for API response) |
| — | — | `SubscriptionResponse.Subscription.ExpiresAt` | Expiration date (null if no expiration) |

### Subscription Retrieval → ListSubscriptions + SubscriptionResponse List

| eShopOnWeb DTO Field | Maxio Response Path | Notes |
|---|---|---|
| `SubscriptionId` | `SubscriptionResponse.Subscription.Id` | Maxio subscription ID |
| `State` | `SubscriptionResponse.Subscription.State` | Enum `SubscriptionState` |
| `PriceInCents` | `SubscriptionResponse.Subscription.ProductPriceInCents` | Current price |
| `NextBillingDate` | `SubscriptionResponse.Subscription.NextAssessmentAt` | Key field for API response |
| `ActiveSince` | `SubscriptionResponse.Subscription.ActivatedAt` | Subscription start date |
| `CanceledAt` | `SubscriptionResponse.Subscription.CanceledAt` | If state is Canceled |
| `ExpiresAt` | `SubscriptionResponse.Subscription.ExpiresAt` | If applicable |

---

## Notes & Source

All facts above are grounded in the Maxio SDK map:
- **SDK identity, namespaces, client construction:** `sdk-map.md` (lines 10–52)
- **Operations signatures, error types, pagination:** `operations/Products.md`, `operations/Customers.md`, `operations/Subscriptions.md`
- **Model fields, required markers, wire names:** `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md`, `records-3-Of-Su.md`, `records-4-Su-We.md`
- **Enum values and wire format:** `models/enums.md`
- **Auth and server wiring:** `sdk-map.md` (lines 199–226)

**Test this plan against the sandbox before implementation:**
- Verify `CreateSubscription` succeeds without payment method (or capture required fields if it fails 422)
- Verify `ReadCustomerByReference` returns 404 for non-existent reference (or error handling needs adjustment)
- Verify concurrent subscription enrollments do not cause duplicate customer creation (concurrency testing)
- Verify `NextAssessmentAt` is set correctly on subscription creation and carries through retrieval
