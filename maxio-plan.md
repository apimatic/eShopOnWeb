# Maxio Advanced Billing Integration Plan — eShopOnWeb Subscriptions

## Scope & Sequence

1. **Client initialization & DI** — Set up `MaxioAdvancedBillingClient` with HTTP Basic auth (API key + "x") from `Maxio` config section; register with `IHttpClientFactory` for reuse.
2. **Subscription plans listing** — List products in the product family handle (`eshop-subscribe`) via `ListProducts` → extract `ProductResponse.Product` records with name/handle/price for display.
3. **Customer sync (idempotent)** — For logged-in user, check if a Maxio customer exists by email via `ReadCustomerByReference` using email as reference; if not found (404), create one via `CreateCustomer` with first/last name and email.
4. **Subscription creation** — Create subscription via `CreateSubscription` with customer ID (or customer reference), product handle, and no payment profile (payment not required per sandbox config).
5. **User subscriptions retrieval** — List subscriptions by customer ID via `ListCustomerSubscriptions` → extract state, next billing date, plan info from `SubscriptionResponse.Subscription`.

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operations & Signatures

| Step | Controller.Method | Signature | Request Model Fields (Required?) | Response Envelope & Fields | Error Case | Pagination | Source |
|---|---|---|---|---|---|---|---|
| 1 (init) | `n/a` | Client construction: `new MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | Options: `Environment: ServerEnvironment`, `BasicAuth: BasicAuthCredentials { Username, Password = "x" }`, `Server: ServerOptions` (optional, for base-URL override) | `n/a` | `n/a` | `n/a` | `sdk-map.md`, `Api/MaxioAdvancedBillingClient.cs` |
| 2 | `Products.ListProducts` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — pass `null` for all optional params except `page` and `perPage` to get defaults | Filter by product family: use query loop with `page=1, perPage=20`; SDK does NOT filter by family handle in the operation itself — post-filter results or list all and match by `Product.ProductFamily.Handle == "eshop-subscribe"`. Fields not needed in request body. | Returns `IReadOnlyList<ProductResponse>` where each element is `{ Product: MaxioAdvancedBilling.Models.Product }`. Extract `Product.Name`, `Product.Handle`, `Product.ProductPriceInCents` (price in cents, divide by 100 for display). | Case B: `SdkException<RawError>` with `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | Manual `page`+`perPage`, default 20 items/page | `operations/Products.md` |
| 3a (sync customer — lookup) | `Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` where `reference` is the eShopOnWeb user's email (wire param: `reference`). | No request body; email passed as query param. | Returns `CustomerResponse { Customer: MaxioAdvancedBilling.Models.Customer }`. Extract `Customer.Id` (Maxio customer ID). | Case B: `SdkException<RawError>`. On 404: catch and treat as "customer not found" → proceed to create. | None | `operations/Customers.md` |
| 3b (sync customer — create) | `Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — pass `body` explicitly (not nullable in practice). | `CreateCustomerRequest { Customer: CreateCustomer !req }`. Inner `CreateCustomer` record (required fields marked `!req`): `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`. Optional: `Reference (reference): string?` (store eShopOnWeb user ID for future idempotent lookup), `Organization`, `Phone`, `Address`, `City`, `State`, `Zip`, `Country`. | Returns `CustomerResponse { Customer: MaxioAdvancedBilling.Models.Customer }` with `Customer.Id` set by Maxio. Store this ID in eShopOnWeb user record for future subscription operations. | Case A: `SdkException<CreateCustomerError>` with `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. `CustomerErrorResponse1.Errors` contains validation errors. | None | `operations/Customers.md`, `records-1-Ac-Cr.md` |
| 4 (subscribe) | `Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — pass `body` explicitly. | `CreateSubscriptionRequest { Subscription: CreateSubscription !req }`. Inner `CreateSubscription` record (vast): at minimum, **one of** `ProductHandle (product_handle): string?` OR `ProductId (product_id): int?` (use handle: `"eshop-pro"` or `"basic-plan"`); **one of** `CustomerId (customer_id): int?` OR `CustomerReference (customer_reference): string?` (use customer ID from step 3). No payment profile needed per sandbox rules (task states "payment method NOT required"). Optional but useful: `NextBillingAt (next_billing_at): DateTimeOffset?` to set explicit next bill date. All other billing/coupon/component fields are optional. | Returns `SubscriptionResponse { Subscription: MaxioAdvancedBilling.Models.Subscription }`. Extract `Subscription.Id`, `Subscription.State` (enum: `SubscriptionState`, e.g. `Active`), `Subscription.NextAssessmentAt`, `Subscription.ProductPriceInCents`. | Case A: `SdkException<CreateSubscriptionError>` with `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. `ErrorListResponse1.Errors` is `IReadOnlyList<string>`. | None | `operations/Subscriptions.md`, `records-2-Cr-Ne.md` |
| 5 (list subs) | `Subscriptions.ListSubscriptions` (alt: `Customers.ListCustomerSubscriptions`) | **Option A (global list + filter):** `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — pass 14 nullable params as `null` to skip filters; returns all subscriptions. **Option B (customer-scoped):** `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — simpler if you have the customer ID. | Option A: all params optional (pass `null`). Option B: only `customerId` required. | Both return `IReadOnlyList<SubscriptionResponse>` — iterate and extract `Subscription.State`, `Subscription.NextAssessmentAt`, `Subscription.ProductPriceInCents`, `Subscription.Id` for display. | Case B: `SdkException<RawError>` with `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | Option A: manual `page`+`perPage`. Option B: none. | `operations/Subscriptions.md`, `operations/Customers.md` |

### Key Enum Values

**SubscriptionState** (namespace `MaxioAdvancedBilling.Models.Enums`): Used in `Subscription.State` response field. Common values (from spec / wire names): `Active (active)`, `Trialing (trialing)`, `PastDue (past_due)`, `Canceled (canceled)`, `Expired (expired)`. Construct via static members: `SubscriptionState.Active`.

**SubscriptionStateFilter** (namespace `MaxioAdvancedBilling.Models.Enums`): For filtering in `ListSubscriptions`. Members: `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)`.

**CollectionMethod** (namespace `MaxioAdvancedBilling.Models.Enums`): Payment collection mode for subscriptions (optional on create). Members: `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)`.

### Client Construction & Auth

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;

// Read config
var apiKey = configuration["Maxio:ApiKey"];       // env var MAXIO_API_KEY
var subdomain = configuration["Maxio:Subdomain"]; // env var MAXIO_SUBDOMAIN (e.g., "cp-exp-1")
var environment = configuration["Maxio:Environment"]; // env var MAXIO_ENVIRONMENT (e.g., "US")

// Build options
var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials 
    { 
        Username = apiKey, 
        Password = "x"  // Literal "x", not a placeholder
    },
    Environment = environment == "EU" ? ServerEnvironment.Eu : ServerEnvironment.Us
    // Optional: Server.Production.Us.Site = subdomain; // if not auto-detected
};

// Register with DI (example)
services.AddHttpClient<MaxioAdvancedBillingClient>()
    .ConfigureHttpClient(client => 
    {
        // HttpClient configuration if needed (timeout, default headers, etc.)
    });

// Or inject HttpClient directly and construct:
var client = new MaxioAdvancedBillingClient(httpClient, options);
```

**Auth pattern:** HTTP Basic with username = API key, password = literal string `"x"`. No OAuth, no bearer tokens, no API key in headers. The `BasicAuthCredentials` class wraps this; the SDK applies it to every request.

### Important: Response Envelopes

Responses wrap their payload in a single field:
- `ProductResponse.Product` (not `ProductResponse` itself)
- `CustomerResponse.Customer`
- `SubscriptionResponse.Subscription`

Reads must go one level down to access the data.

---

## Trap Notes

⚠ **Step 1 (client registration)** — The `HttpClient` must be registered with `IHttpClientFactory` and reused; do not instantiate a new `HttpClient` per request. The SDK wraps Polly for retries; a fresh `HttpClient` on each call negates that. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ **Step 1 (auth)** — API key comes from config (`Maxio:ApiKey` binding key), never hardcoded. The password is the literal string `"x"`, not a placeholder. **MUST load `dotnet-authentication`** before setting credentials.

⚠ **Step 2 (ListProducts)** — The operation does NOT take a product-family-handle filter parameter. The plan lists by product family handle `"eshop-subscribe"`, but the SDK's `ListProducts` endpoint cannot filter by family handle in a single call. Post-filter results by checking `Product.ProductFamily.Handle` or use pagination to load all products and match in-memory. Alternatively, retrieve products by family ID if you know the ID, but the task requires handle-based lookup. **No MUST-load here; this is a design constraint.**

⚠ **Step 3a (customer lookup by email)** — The operation is `ReadCustomerByReference(reference)` where `reference` is a string the system uses to identify the customer. The task suggests using email as the unique identifier (idempotent create-or-find). On 404, the customer does not exist; catch `SdkException<RawError>` and check `StatusCode == HttpStatusCode.NotFound` before creating. **MUST load `dotnet-error-handling`** to distinguish 404 from other errors.

⚠ **Step 4 (subscription create, no payment profile)** — The SDK's `CreateSubscription` operation may require or validate a payment profile depending on the product's configuration. The task states "payment method NOT required" for sandbox products. Pass no `PaymentProfileId`, no `PaymentProfileAttributes`, no `CreditCardAttributes`; rely on the product/site config to accept unpaid subscriptions. If the API rejects the request with a 422, check the error response for payment-related validation errors. **MUST load `dotnet-error-handling`** to decode the error payload.

⚠ **Step 5 (list subscriptions)** — Two patterns: global `ListSubscriptions` (requires manual filtering to find the user's subs) or `ListCustomerSubscriptions` by customer ID (simpler if you store customer ID in the user record from step 3). The task uses customer sync, so customer ID is available; prefer `ListCustomerSubscriptions`. **MUST load `dotnet-calling-endpoints`** for the named-parameter binding and to know which params must be passed explicitly vs. have defaults.

⚠ **Error boundary (all steps)** — **Two separate `JsonException` traps must be handled differently:**
1. A drifted or malformed **2xx** body (missing `required` member) surfaces as `JsonException` from deserialization, **NOT** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the boundary as an unhandled exception.
2. A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` **while the error object is constructed**, destroying the HTTP status — the `JsonException` replaces the `SdkException`, and the status code is lost. A boundary that maps every `JsonException` to a 5xx then reports the rejection as an outage to the caller, who retries 5xx expecting eventual success — but this payload mismatch can never succeed, so the retry loop is infinite waste.

**MUST load `dotnet-error-handling`** before writing the error boundary. Map `JsonException` from malformed **2xx** responses to a distinct internal error (not thrown to the caller), and catch typed `SdkException<…>` separately before the catch-all `JsonException` handler, so malformed error responses are logged but don't erase the HTTP status.

---

## REQUIRED READING

The following companion skills must be loaded **before implementation starts**. This sheet deliberately does not carry their contents; they are the integration layer on top of the SDK map.

| Skill | Governs | Why |
|---|---|---|
| `dotnet-client-initialization` | Step 1 (client construction & DI) | HttpClient reuse via IHttpClientFactory, SDK client lifetime, DI registration patterns |
| `dotnet-authentication` | Step 1 (auth credentials) | HTTP Basic setup, credential rotation, per-environment config binding |
| `dotnet-calling-endpoints` | Steps 2–5 (operation calls) | Named-argument binding, optional param defaults, pagination patterns, query vs. body params |
| `dotnet-models` | Steps 2–5 (request/response fields) | Field naming, immutable records, union factories, StringEnum construction, wire names |
| `dotnet-error-handling` | All steps (exception boundary) | Typed vs. raw error cases, TryGet accessors, JsonException vs. SdkException, HTTP status preservation |
| `dotnet-configuration-resilience` | Step 1 (client config) | Retry semantics, timeout scope (per-attempt, not total), base-URL override, logging |
| `dotnet-testing` | Test coverage (if applicable) | HttpClient mocking, SDK stub patterns, framework alignment |

---

## Assumptions & Blockers

### Assumptions

1. **eShopOnWeb user identity is email-based for idempotent customer sync.** The integration stores Maxio `CustomerId` in the eShopOnWeb user record after first sync, or uses email as the `reference` field in Maxio for lookup on subsequent calls.
2. **Sandbox products (`eshop-pro`, `basic-plan`) accept subscriptions without an upfront payment profile.** The task specifies "payment method NOT required"; if the Maxio site/product config contradicts this, the create call will fail with a 422 validation error.
3. **Product family handle `eshop-subscribe` exists and contains the two plans.** The integration assumes the Maxio site is pre-seeded with this family and the two products.
4. **`.NET 10` SDK is available in the build environment.** The task mentions SDK pinned to 8.0.x but only .NET 10 installed; `rollForward: latestMajor` must be set in the project file or global.json to allow the mismatch.
5. **JWT authentication is wired separately for the PublicApi endpoints.** This plan covers Maxio SDK calls only; JWT validation and user extraction from claims are outside scope.
6. **In-memory database is acceptable for test/dev.** The task specifies `UseOnlyInMemoryDatabase=true`; no LocalDB is required for Maxio integration itself.

### Blockers

None identified. All required operations are present in the SDK map, response/request shapes are documented, and the Maxio sandbox is pre-seeded with the needed entities.

---

## Configuration & Environment

**Binding keys** (from `Maxio` config section):
- `Maxio:ApiKey` — maps to `MAXIO_API_KEY` env var (required)
- `Maxio:Subdomain` — maps to `MAXIO_SUBDOMAIN` env var (e.g., `cp-exp-1`, required)
- `Maxio:ProductFamilyHandle` — maps to `MAXIO_DEFAULT_PRODUCT_FAMILY` env var (optional; used in product listing, default = none, must filter in code)
- `Maxio:Environment` — maps to `MAXIO_ENVIRONMENT` env var (optional; `US` or `EU`, default = `US`)
- `Maxio:BaseUrl` — (optional; override for testing/mocking, e.g., `http://localhost:8080`)

**Example appsettings.json (non-sensitive defaults):**
```json
{
  "Maxio": {
    "Subdomain": "cp-exp-1",
    "ProductFamilyHandle": "eshop-subscribe",
    "Environment": "US"
  }
}
```

Sensitive values (`ApiKey`) must come from user secrets (dev) or environment variables (deploy).

---

## Notes on the Hero Flow

1. **Browse plans:** GET `/api/subscription-plans` (not Maxio-authenticated; returns UI list)
   - Calls `ListProducts`, filters by family handle `eshop-subscribe`, returns name/handle/price.
2. **Subscribe:** POST `/api/subscriptions` with `{ planHandle, ... }`
   - Step 3a: Lookup/create customer (sync current user to Maxio).
   - Step 4: Create subscription (product handle, customer ID).
   - Response: subscription ID, state, next billing date, plan details.
3. **View subscriptions:** GET `/api/my-subscriptions`
   - Step 5: List subscriptions by customer ID, return active subs with state and next billing date.

All three endpoints are JWT-authenticated (caller must be logged in); Maxio auth is SDK-internal (API key from config).
