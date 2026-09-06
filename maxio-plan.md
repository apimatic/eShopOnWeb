# Maxio Advanced Billing Integration — eShopOnWeb Subscription Endpoints

## Scope & Sequence

1. **Endpoint 1: GET /api/subscription-plans** — list available subscription plans from Maxio via `client.Products.ListProducts()`
2. **Endpoint 2: POST /api/subscriptions** — ensure customer exists (idempotent, via reference), then create subscription via `client.Subscriptions.CreateSubscription()`
3. **Endpoint 3: GET /api/my-subscriptions** — retrieve authenticated user's Maxio customer, list their subscriptions via `client.Customers.ListCustomerSubscriptions()`

Each endpoint wraps the SDK calls in error handling that converts Maxio errors to appropriate HTTP responses.

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

| Step | Operation | Signature & Request | Response Envelope + Inner Fields | Error Case + Accessors + Payload | Source |
|---|---|---|---|---|---|
| **Step 1a: Get plans** | `client.Products.ListProducts()` | `ListProducts(null, null, null, null, null, null, null, null, page: 1, perPage: 20, ct: default)` — all optional filter params passed as `null`; `page` defaults to 1, `perPage` to 20. | `IReadOnlyList<ProductResponse>` where each element is `ProductResponse { Product (product): Product !req }` with fields: `Product.Id`, `Product.Name`, `Product.Handle`, `Product.Description`, `Product.PriceInCents` (in cents, e.g., 29900 = $299.00), `Product.Interval` (e.g., 1), `Product.IntervalUnit` (e.g., `MonthlyIntervalUnit` enum value). | **Case B** — `SdkException<RawError>`. Accessors: `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`, `ReadAsBytes(): ReadOnlyMemory<byte>`. | [operations/Products.md](map/operations/Products.md) |
| **Step 2a: Check customer exists** | `client.Customers.ReadCustomerByReference()` | `ReadCustomerByReference(reference: "user-id", ct: default)` — reference is your (eShopOnWeb's) user identifier (e.g., AspNetUserId); passed as a query param. Returns customer if found by exact reference match. | `CustomerResponse { Customer (customer): Customer !req }` with fields: `Customer.Id`, `Customer.Reference`, `Customer.FirstName`, `Customer.LastName`, `Customer.Email`. **If reference is not found, the SDK throws 404.** | **Case B** — `SdkException<RawError>`. Accessors: `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`, `ReadAsBytes(): ReadOnlyMemory<byte>`. 404 indicates customer does not exist; proceed to Step 2b. | [operations/Customers.md](map/operations/Customers.md) |
| **Step 2b: Create customer (if not exists)** | `client.Customers.CreateCustomer()` | `CreateCustomer(body: new CreateCustomerRequest { Customer = new CreateCustomer { FirstName = "…", LastName = "…", Email = "…", Reference = "user-id" } !req }, ct: default)` — `CreateCustomerRequest` wraps `CreateCustomer`. Required fields in `CreateCustomer`: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`. Optional: `Reference (reference): string?`, `Organization (organization): string?`, `Address (address): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `Locale (locale): string?`. **Reference must be unique per Maxio site — use eShopOnWeb's user ID.** | `CustomerResponse { Customer (customer): Customer !req }` with fields: `Customer.Id` (Maxio ID; store for later), `Customer.Reference`, `Customer.Email`, `Customer.CreatedAt`. | **Case A** — `SdkException<CreateCustomerError>`. Accessors: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1) [422]`, `TryGetRawError(out RawError) [fallback]`. `CreateCustomerError` is in namespace `MaxioAdvancedBilling.Errors`. Payload: `CustomerErrorResponse1 { Errors (errors): Errors? }` where `Errors` has fields like `PerPage`, `PricePoint` (both `IReadOnlyList<string>?`). | [operations/Customers.md](map/operations/Customers.md) |
| **Step 2c: Create subscription** | `client.Subscriptions.CreateSubscription()` | `CreateSubscription(body: new CreateSubscriptionRequest { Subscription = new CreateSubscription { CustomerId = <maxio-customer-id>, ProductHandle = "eshop-pro", Reference = "unique-sub-ref", DeferSignup = false } !req }, ct: default)` — `CreateSubscriptionRequest` wraps `CreateSubscription`. **Minimally required in `CreateSubscription`:** `CustomerId (customer_id): int?` or `CustomerReference (customer_reference): string?` — use the Maxio ID from Step 2b. Product id: one of `ProductHandle (product_handle): string?`, `ProductId (product_id): int?` — use handle (e.g., "eshop-pro", "basic-plan"). Optional but recommended: `Reference (reference): string?` (your app's subscription ID), `DeferSignup (defer_signup): bool? = false` (false = activate immediately). All other fields (`PaymentProfileId`, `CouponCode`, `Components`, etc.) are optional and default to absent. **No payment method required per scope.** | `SubscriptionResponse { Subscription (subscription): Subscription? }` with fields: `Subscription.Id` (Maxio subscription ID; store), `Subscription.CustomerId`, `Subscription.Reference`, `Subscription.Product?.Id`, `Subscription.State` (e.g., `SubscriptionState.Active` enum), `Subscription.CurrentPeriodEndsAt`, `Subscription.NextAssessmentAt`, `Subscription.CreatedAt`. **Note:** Current product accessed via nested `Product` object; use `NextProductHandle` for pending product changes. | **Case A** — `SdkException<CreateSubscriptionError>`. Accessors: `TryGetErrorListResponse1(out ErrorListResponse1) [422]`, `TryGetRawError(out RawError) [fallback]`. `CreateSubscriptionError` is in `MaxioAdvancedBilling.Errors`. Payload: `ErrorListResponse1 { Errors (errors): IReadOnlyList<string> !req }`. | [operations/Subscriptions.md](map/operations/Subscriptions.md) |
| **Step 3a: Get customer ID for authenticated user** | `client.Customers.ReadCustomerByReference()` | `ReadCustomerByReference(reference: "authenticated-user-id", ct: default)` — same as Step 2a; return `Customer.Id`. | `CustomerResponse { Customer (customer): Customer !req }` with `Customer.Id`. | **Case B** — `SdkException<RawError>`. 404 = no Maxio customer for this user (user never subscribed). Return empty list or error to caller. | [operations/Customers.md](map/operations/Customers.md) |
| **Step 3b: List subscriptions for customer** | `client.Customers.ListCustomerSubscriptions()` | `ListCustomerSubscriptions(customerId: <maxio-id-from-3a>, ct: default)` — no pagination or filter params. | `IReadOnlyList<SubscriptionResponse>` where each element is `SubscriptionResponse { Subscription (subscription): Subscription? }`. Fields per item: `Subscription.Id`, `Subscription.State`, `Subscription.Product?.Id`, `Subscription.Product?.Handle`, `Subscription.CurrentPeriodEndsAt`, `Subscription.NextAssessmentAt`, `Subscription.CreatedAt`. **Note:** Current product accessed via nested `Product` object (nullable); use `NextProductHandle` for pending product changes. | **Case B** — `SdkException<RawError>`. Accessors: `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. | [operations/Customers.md](map/operations/Customers.md) |

### Enum Values (Needed)

From `map/models/enums.md`:

#### `IntervalUnit` — wire values for product billing intervals
- `MaxioAdvancedBilling.Models.Enums.IntervalUnit.Day` — wire: `"day"`
- `MaxioAdvancedBilling.Models.Enums.IntervalUnit.Week` — wire: `"week"`
- `MaxioAdvancedBilling.Models.Enums.IntervalUnit.Month` — wire: `"month"`
- `MaxioAdvancedBilling.Models.Enums.IntervalUnit.Year` — wire: `"year"`

#### `SubscriptionState` — subscription states (for reading, not sending)
- `MaxioAdvancedBilling.Models.Enums.SubscriptionState.Active` — wire: `"active"`
- `MaxioAdvancedBilling.Models.Enums.SubscriptionState.PastDue` — wire: `"past_due"`
- `MaxioAdvancedBilling.Models.Enums.SubscriptionState.Canceled` — wire: `"canceled"`
- `MaxioAdvancedBilling.Models.Enums.SubscriptionState.AwaitingSignup` — wire: `"awaiting_signup"`
- `MaxioAdvancedBilling.Models.Enums.SubscriptionState.Trialing` — wire: `"trialing"`

### Client Construction & Configuration

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;  // ServerEnvironment

var options = new MaxioAdvancedBillingClientOptions
{
    // HTTP Basic auth: Username = API key, Password = literal "x"
    BasicAuth = new BasicAuthCredentials
    {
        Username = "<your-api-key>",  // from Maxio:ApiKey config
        Password = "x"
    },
    Environment = ServerEnvironment.Us,  // or .Eu if account uses EU hosting
    // Optional: override base URL if needed
    // Server = new ServerOptions { Production = new ProductionOptions { Us = new Us { BaseUrl = "…" } } }
};

// Pass a long-lived HttpClient (via IHttpClientFactory in DI, NOT a new one per request)
var client = new MaxioAdvancedBillingClient(httpClient, options);
```

**Configuration binding:** Load `Maxio:ApiKey` and `Maxio:Subdomain` from appsettings.json or user-secrets.
- `Maxio:ApiKey` → BasicAuth.Username
- `Maxio:Subdomain` → server site (not currently exposed in the base options; if needed, set `options.Server.Production.Us.Site`)
- `Maxio:BaseUrl` (optional) → `options.Server.Production.Us.BaseUrl` (use verbatim if set)

---

## Trap Notes

⚠ **Step 2a–2c (customer/subscription creation)** — Creating a subscription *without* an explicit payment profile will succeed **if the product does not require payment method** (per your sandbox config, no payment method required). If payment *is* required and none is supplied, Maxio returns 422 with `ErrorListResponse1.Errors[]` containing field-level messages. Handle as validation error, not fatal. **MUST load `dotnet-calling-endpoints`** before writing the first subscription create call — required params must be passed explicitly (no C# defaults).

⚠ **Step 1, 3 (list operations returning `IReadOnlyList<T>`)** — The return type is a collection, not a wrapped response object. Do not try to unwrap `.Subscription` or `.Product`; iterate the list directly. **MUST load `dotnet-calling-endpoints`** to understand nullable optional params (e.g., `page: null` is valid, means "use default").

⚠ **All steps (error boundary)** — Case B operations return `SdkException<RawError>`. There is **no typed error decoder**; all HTTP statuses (404, 422, 500) land here with `StatusCode` and body accessible via `ReadAsString()` or `ReadAsJson<T>()`. Case A operations return `SdkException<CreateCustomerError>` or similar, which have typed `TryGet…()` accessors for specific statuses; always chain `TryGetRawError()` as fallback. **Two critical JsonException cases (read REQUIRED READING below).**

⚠ **Step 2a (customer lookup by reference, 404 is normal)** — If the authenticated user has never been enrolled in Maxio, this call throws `SdkException<RawError>` with `StatusCode = 404`. Catch and proceed to Step 2b (create customer). Do not treat 404 as a fatal error. **MUST load `dotnet-error-handling`** before writing the exception boundary.

⚠ **Step 2b–2c (customer/subscription create, 422 is validation error)** — Maxio returns 422 when a required field is missing or a constraint is violated (e.g., reference already exists). These are user-correctable errors, not bugs. `CreateCustomerError` has `TryGetCustomerErrorResponse1(out var err422)` → `err422.Errors.PerPage` or `.PricePoint` list the violations. Build an error response for the endpoint caller. **MUST load `dotnet-error-handling`**.

⚠ **Idempotency (double-click protection)** — Step 2 (customer create) uses `Reference` (eShopOnWeb user ID) as a unique key. Maxio rejects duplicate references with 422. To guard against double-click on subscribe:
  1. Check if customer exists (Step 2a) via reference first.
  2. If 404, create (Step 2b).
  3. If 422 on create, treat as "customer already exists" (another request won race), re-fetch and continue.
  
  Similarly, subscription `Reference` (your app's subscription ID) must be unique. Same pattern: generate a deterministic reference (e.g., `user-id + "-" + plan-handle`) and catch 422 as "already exists."

⚠ **Configuration resilience & retry** — The SDK client ships with Polly retry configured. **MUST load `dotnet-configuration-resilience`** before wiring the client to understand what `Timeout` bounds (per-attempt, not total), which HTTP statuses are retried, and that retries on `POST` are **not** gated by HTTP method (transport failures are retried on all verbs, including writes — **this can cause duplicate subscription creates on network timeout**; defend via reference uniqueness).

---

## REQUIRED READING

Load these companion skills **before implementation starts**. This sheet deliberately does not carry their contents; the sheet and the skills are complementary.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Steps 1–3: how to construct the `MaxioAdvancedBillingClient`, pass `HttpClient` and options, and register in DI. |
| `dotnet-authentication` | Step 1–3: Basic auth credentials (Username = API key, Password = `"x"`), how to load from config, when to set before vs. after client construction. |
| `dotnet-calling-endpoints` | Steps 1–3: signatures and required-explicit params (e.g., `reference: "…"` in lookup, `page: null` in lists), async/await, cancellation token. |
| `dotnet-models` | Steps 2b–2c: `CreateCustomer` and `CreateSubscription` record construction, immutability, required fields in object initializer, nullable optional fields. |
| `dotnet-error-handling` | Steps 1–3: `SdkException<T>` throw-only pattern (no `Result` variants), Case A vs. B, `TryGet…()` accessors on typed errors, `RawError` on Case B, handling 404 and 422 as expected conditions. |
| `dotnet-configuration-resilience` | All steps: retry/timeout configuration, what `Timeout` bounds, which statuses trigger retry, transport-failure retry on all verbs (not just GET). |

**Critical JsonException handling (both cases must be addressed BEFORE writing error boundary):**

1. **Drifted/malformed 2xx body** (missing required field in response deserialize) — surfaces as `System.Text.Json.JsonException` from deserialization, **not** as `SdkException`. An exception-only boundary that catches `SdkException` exclusively will let `JsonException` escape. Map this to 5xx in your boundary (indicates SDK/provider contract breakage, not user error). **MUST load `dotnet-error-handling`**.

2. **Non-2xx body that does not match operation's `{Operation}Error` shape** — throws `JsonException` *while the error object is being constructed*, replacing the `SdkException` and destroying the HTTP status. A boundary that maps `JsonException` to 5xx will correctly signal an outage; a boundary that retries 5xx will retry something that can never succeed. **MUST load `dotnet-error-handling`** for the correct pattern.

---

## Assumptions & Blockers

### Assumptions
1. **User identity available in endpoint handler** — authenticated user's ID (eShopOnWeb AspNetUserId or equivalent) is available as a claim or property, sufficient to use as Maxio `Reference`.
2. **Maxio sandbox credentials in configuration** — `Maxio:ApiKey` is populated from environment (loaded into user-secrets or appsettings).
3. **Sandbox entities stable** — Product Family `eshop-subscribe`, plans `eshop-pro` and `basic-plan`, and metered component `api-call` exist and are not being deleted. API will fail if a plan handle does not exist; no fallback needed.
4. **No payment collection required** — per scope, plans have no payment method requirement. If this changes in Maxio config, `CreateSubscription` calls will fail with 422; add a payment profile step if needed.
5. **In-memory database** — subscription metadata (Maxio customer ID, subscription ID, state) is stored locally in-memory EF. It survives only within a single process run; no persistence across restarts.

### Blockers
*None identified.* All required SDK operations exist; all enum values are documented; error types are known. Configuration and credential routing are standard .NET patterns.

---

## Appendix: Namespace Summary

When writing `using` directives, group by content:

```csharp
using MaxioAdvancedBilling;                    // Client, options
using MaxioAdvancedBilling.Api;                // Controllers (if directly importing)
using MaxioAdvancedBilling.Models;             // Records (Product, Customer, Subscription, etc.)
using MaxioAdvancedBilling.Models.Enums;       // Enums (IntervalUnit, SubscriptionState, etc.)
using MaxioAdvancedBilling.Errors;             // Error classes (CreateCustomerError, etc.)
using MaxioAdvancedBilling.Core.Authentication.Basic;  // BasicAuthCredentials
using MaxioAdvancedBilling.Servers;            // ServerEnvironment
using MaxioAdvancedBilling.Core.Configuration; // RetryOptions (if tuning)
```

**Do not** omit the `.Enums` or `.Errors` using directives; C# does not import child namespaces transitively.

---

**Generation date:** 2026-09-07  
**SDK version (map/source):** v1.0.2 (commit `15db14b2e663ebe9e957e061bd67634630429035`)  
**API:** Maxio Advanced Billing (formerly Chargify)
