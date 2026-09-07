# eShopOnWeb Maxio Advanced Billing Integration Plan

## Scope & Sequence

1. **Client initialization & DI setup** — register the Maxio SDK client and HttpClient factory
2. **Configuration layer** — load credentials and site configuration from appsettings
3. **List subscription plans** (GET /api/subscription-plans) — call `ListProductsForProductFamily` to fetch plans from the eshop-subscribe product family
4. **Get subscription plans details** — read plan details for display (handle, name, price)
5. **Create/link customer** (idempotent) — attempt `ReadCustomerByReference` first; if 404, call `CreateCustomer`
6. **Create subscription** (POST /api/subscriptions) — call `CreateSubscription` with customer ID and product handle
7. **List user subscriptions** (GET /api/my-subscriptions) — call `ListCustomerSubscriptions` with customer ID

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### 1. List subscription plans

| Aspect | Details |
|--------|---------|
| **Controller** | `client.ProductFamilies` |
| **Method signature** | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| **Parameters** | `productFamilyId` (required, pass product family ID or handle, e.g. `"3023074"` or `"handle:eshop-subscribe"`); remaining 8 params nullable — pass `null` to skip each. `page` defaults to 1, `perPage` defaults to 20. |
| **Request model** | None (parameters only) |
| **Response type** | `IReadOnlyList<ProductResponse>` |
| **Response envelope** | Each element is `ProductResponse { Product: Product }` — unwrap to `.Product` to read product details |
| **Response fields to read** | From each `Product`: `Id` (int?), `Name` (string?), `Handle` (string?), `Description` (string?), `PriceInCents` (long?), `Interval` (int?), `IntervalUnit` (IntervalUnit?), `TrialPriceInCents` (long?), `TrialInterval` (int?), `TrialIntervalUnit` (IntervalUnit?) |
| **Error case** | `SdkException<ListProductsForProductFamilyError>` — **Case A (typed)** |
| **Error accessors** | `TryGetString(out string)` [404 — product family not found] · `TryGetRawError(out RawError)` [fallback] |
| **Pagination** | Manual `page`+`perPage`; implement by looping `page` until response is empty or smaller than `perPage` |
| **Source** | `map/operations/ProductFamilies.md` |

### 2. Create or link customer (idempotent lookup)

| Aspect | Details |
|--------|---------|
| **Lookup controller** | `client.Customers` |
| **Lookup method** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` |
| **Lookup response** | `CustomerResponse { Customer: Customer }` — unwrap to `.Customer` |
| **Lookup errors** | `SdkException<RawError>` — **Case B**; on 404, customer not found, proceed to create |
| **Create controller** | `client.Customers` |
| **Create method signature** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` |
| **Create request model** | `CreateCustomerRequest { Customer: CreateCustomer !req }` |
| **CreateCustomer fields** | `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `CcEmails (cc_emails): string?`, `Organization (organization): string?`, `Reference (reference): string?`, `Address (address): string?`, `Address2 (address_2): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `Locale (locale): string?`, `VatNumber (vat_number): string?`, `TaxExempt (tax_exempt): bool?`, `TaxExemptReason (tax_exempt_reason): string?`, `ParentId (parent_id): int?`, `SalesforceId (salesforce_id): string?` |
| **Required wire names** | `first_name`, `last_name`, `email` |
| **Recommended wire names** | `reference` (store eShopOnWeb user ID here for idempotent lookups) |
| **Create response** | `CustomerResponse { Customer: Customer }` — unwrap to `.Customer`, read `Id` (int?) to pass to subscription create |
| **Create error case** | `SdkException<CreateCustomerError>` — **Case A (typed)** |
| **Create error accessors** | `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| **Source** | `map/operations/Customers.md` |

### 3. Create subscription

| Aspect | Details |
|--------|---------|
| **Controller** | `client.Subscriptions` |
| **Method signature** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` |
| **Request model** | `CreateSubscriptionRequest { Subscription: CreateSubscription !req }` |
| **CreateSubscription fields to set** | `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `CustomerId (customer_id): int?`, `CustomerAttributes (customer_attributes): CustomerAttributes?`, `Reference (reference): string?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `PaymentProfileId (payment_profile_id): int?`, `InitialBillingAt (initial_billing_at): DateTimeOffset?`, `DeferSignup (defer_signup): bool? = false`, `CouponCode (coupon_code): string?`, `CouponCodes (coupon_codes): IReadOnlyList<string>?` |
| **Wire names** | `product_handle`, `product_id`, `customer_id`, `customer_attributes`, `reference`, `payment_collection_method`, `payment_profile_id`, `initial_billing_at`, `defer_signup`, `coupon_code`, `coupon_codes` |
| **Required logic** | Identify customer by either `CustomerId` (int) OR `CustomerAttributes` (nested); use `CustomerId` when customer already exists (from step 2); omit `PaymentProfileId` (payment method not required per spec) |
| **Response type** | `SubscriptionResponse` |
| **Response envelope** | `SubscriptionResponse { Subscription: Subscription? }` — unwrap to `.Subscription` to read subscription |
| **Response fields to read** | From `Subscription`: `Id` (int?), `CustomerId` (int?), `ProductId` (int?), `ProductHandle` (string?), `State` (SubscriptionState?), `CreatedAt` (DateTimeOffset?), `CurrentPeriodEndsAt` (DateTimeOffset?), `NextBillingAt` (DateTimeOffset?), `CanceledAt` (DateTimeOffset?), `ActivatedAt` (DateTimeOffset?), `Reference` (string?) |
| **Error case** | `SdkException<CreateSubscriptionError>` — **Case A (typed)** |
| **Error accessors** | `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| **Note from Maxio** | Payment information may be required depending on product options; per spec, payment method is not required for this feature. 3DS authentication may redirect on 422 with an action_link. For testing, use sandbox credentials, not real card data. |
| **Source** | `map/operations/Subscriptions.md` |

### 4. List user subscriptions

| Aspect | Details |
|--------|---------|
| **Controller** | `client.Customers` |
| **Method signature** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` |
| **Parameters** | `customerId` (required, from step 2) |
| **Request model** | None (parameter only) |
| **Response type** | `IReadOnlyList<SubscriptionResponse>` |
| **Response envelope** | Each element is `SubscriptionResponse { Subscription: Subscription? }` — unwrap each to `.Subscription` to read subscription |
| **Response fields to read** | From each `Subscription`: `Id`, `ProductId`, `ProductHandle`, `State`, `CreatedAt`, `CurrentPeriodEndsAt`, `NextBillingAt`, `CanceledAt`, `ActivatedAt`, `Reference` |
| **Error case** | `SdkException<RawError>` — **Case B** |
| **Error accessors** | `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` |
| **Pagination** | None (returns all subscriptions for customer) |
| **Source** | `map/operations/Customers.md` |

---

## Enum values

### CollectionMethod (required for subscription creation if specifying payment method)

Wire values used in `PaymentCollectionMethod` field of `CreateSubscription`. From `map/models/enums.md`:

| C# member name | Wire value |
|---|---|
| `Invoice` | `"invoice"` |
| `Automatic` | `"automatic"` |
| `Remittance` | `"remittance"` |

### SubscriptionState (read from subscription response)

From `map/models/enums.md`:

| C# member name | Wire value |
|---|---|
| `Pending` | `"pending"` |
| `Active` | `"active"` |
| `Trialing` | `"trialing"` |
| `PastDue` | `"past_due"` |
| `Soft` | `"soft"` |
| `Paused` | `"paused"` |
| `Canceled` | `"canceled"` |
| `Expired` | `"expired"` |
| `AwaitingSignup` | `"awaiting_signup"` |
| `OnHold` | `"on_hold"` |

### IntervalUnit (read from product response)

From `map/models/enums.md`:

| C# member name | Wire value |
|---|---|
| `Day` | `"day"` |
| `Month` | `"month"` |
| `Year` | `"year"` |

---

## Client initialization & authentication

From `map/operations/*` and `sdk-map.md`:

- **Root namespace**: `MaxioAdvancedBilling`
- **Client class**: `MaxioAdvancedBillingClient`
- **Constructor**: `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` — requires both an `HttpClient` (long-lived, reused) and options object
- **Options class**: `MaxioAdvancedBillingClientOptions` (namespace: `MaxioAdvancedBilling`)
- **Auth type**: HTTP Basic — `BasicAuth = new BasicAuthCredentials { Username = "<api_key>", Password = "x" }` (password is literal string "x")
- **Environment**: `ServerEnvironment.Us` (default, default-to-us hosting) or `ServerEnvironment.Eu` (if EU hosting is configured)
- **Server override**: `options.Server.Production.Us.Site = "<subdomain>"` (or `.Eu.*` for EU)
- **Dependencies** (namespace: `MaxioAdvancedBilling.Core.Configuration`, `MaxioAdvancedBilling.Servers`):
  - `BasicAuthCredentials` lives in `MaxioAdvancedBilling.Core.Authentication.Basic`
  - `ServerEnvironment` lives in `MaxioAdvancedBilling.Servers`
  - `RetryOptions` lives in `MaxioAdvancedBilling.Core.Configuration`

Minimal wiring (see `dotnet-client-initialization` and `dotnet-authentication` before implementing):

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;

var httpClient = new HttpClient(); // or injected IHttpClientFactory
var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" },
    Environment = ServerEnvironment.Us,
};
options.Server.Production.Us.Site = subdomain;

var client = new MaxioAdvancedBillingClient(httpClient, options);
```

---

## Model namespaces (using directives)

| Content | Namespace |
|---|---|
| Client, options | `MaxioAdvancedBilling` |
| Controllers (API groups) | `MaxioAdvancedBilling.Api` |
| Records (data models) | `MaxioAdvancedBilling.Models` |
| Enums | `MaxioAdvancedBilling.Models.Enums` |
| Error classes | `MaxioAdvancedBilling.Errors` |
| Auth (Basic credentials) | `MaxioAdvancedBilling.Core.Authentication.Basic` |
| Server config | `MaxioAdvancedBilling.Servers` |

---

## Trap notes

⚠ **Step 1 (client initialization)** — the `HttpClient` must be long-lived and reused across requests, never instantiated per-call. The SDK wraps it; retries and resilience are managed by Polly. **MUST load `dotnet-client-initialization`** before constructing the client.

⚠ **Step 2 (authentication)** — credentials (API key + literal `"x"`) must be set BEFORE client construction OR in the DI callback. Do not hardcode credentials; load from configuration. **MUST load `dotnet-authentication`** before wiring auth.

⚠ **Step 3 (calling endpoints)** — all operations are throw-only (no Result/ApiResult variants). `ListProductsForProductFamily` and `ListCustomerSubscriptions` return arrays directly (not wrapped in envelopes). `CreateCustomer` and `CreateSubscription` return response envelopes wrapping a single field — unwrap to `.Customer` or `.Subscription` before reading data. **MUST load `dotnet-calling-endpoints`** before the first operation call.

⚠ **Step 4 (models & fields)** — `CreateCustomer` requires `FirstName`, `LastName`, `Email` (all `string !req`); build with an object initializer. `CreateSubscription` requires either `ProductHandle` (string) or `ProductId` (int), and either `CustomerId` (int) or `CustomerAttributes` (nested); the choice depends on whether you created a new customer (use nested attributes) or looked up an existing one (use ID). Enums like `CollectionMethod` are `StringEnum<T>`, not C# enums; build with `CollectionMethod.FromValue("invoice")` or the static members (`CollectionMethod.Invoice`). **MUST load `dotnet-models`** before writing model initializers.

⚠ **Step 5 (error handling — MANDATORY FIRST)** — This SDK generates typed error classes (`CreateCustomerError`, `CreateSubscriptionError`) for most write operations (Case A) and raw errors (`RawError`) for reads/lists (Case B). Confirm each operation's error case in the map row. Two JSON-parsing exceptions can surface:
- A drifted/malformed **2xx** body (missing `required` field) surfaces as `JsonException` from deserialization, NOT as `SdkException` — an SDK-exception-only catch ladder lets it escape the integration boundary;
- A **non-2xx** body that doesn't match the operation's generated error shape throws `JsonException` while constructing the error object, so `JsonException` **replaces** the `SdkException` and HTTP status is lost — a boundary that maps every `JsonException` to a 5xx misreports a real rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

  Write the boundary early (before integration code), handle both cases, and never catch `JsonException` as a catch-all. **MUST load `dotnet-error-handling`** before writing any error boundary.

⚠ **Step 6 (configuration & resilience)** — The SDK client's `Timeout` (per-attempt, not per-call) and retry settings are configured via `MaxioAdvancedBillingClientOptions.Retry` (`RetryOptions`, backed by Polly). `HttpMethodsToRetry` gates status-code retries (POST with 503 is not re-sent), but **transport failures** (`HttpRequestException`) are retried on every verb including POST — writes can execute more than once. `MaxRetries = 0` is rejected; the floor is 1. No built-in logging hook. **MUST load `dotnet-configuration-resilience`** before tuning retries or timeouts.

---

## Assumptions & Blockers

### Assumptions

1. **Product family ID/handle** — The plan uses `"3023074"` (the numeric ID from spec) or `"handle:eshop-subscribe"` (handle string); the map shows that `ListProductsForProductFamily` accepts both. Implementation will choose one format and pass it directly.
2. **Idempotent customer linking** — The `Reference` field on `CreateCustomer` is used to store the eShopOnWeb user ID so that `ReadCustomerByReference` can look it up by user ID, making the create/link operation idempotent. A lookup that returns 404 triggers a create; any other error (401, 500) will propagate as an exception.
3. **No payment method required** — The plan omits `PaymentProfileId` and payment profile attributes from `CreateSubscription`, relying on the product configuration and `PaymentCollectionMethod` to control whether payment is collected at signup.
4. **Collection method** — If a collection method is needed, the plan assumes `Automatic` is suitable (subscription charges occur automatically). Implementer must confirm product configuration permits this.
5. **No custom billing dates** — The plan does not set `InitialBillingAt`; subscriptions begin on the default schedule. If custom billing dates are needed, pass `InitialBillingAt` as a `DateTimeOffset`.
6. **Timezone for site** — No timezone configuration is assumed; timestamps are interpreted as UTC. If the site requires a specific timezone context, this must be handled by the application (e.g., converting user-local dates to UTC before passing to the SDK).

### Blockers

None at this time. All required operations are available in the SDK map with no feature gaps.

---

## REQUIRED READING

Before implementation starts, load the following companion skills in order. The sheet intentionally does **not** carry their contents — each skill carries worked examples, defaults, and gotchas that are critical to correct integration:

1. **`dotnet-client-initialization`** — Step 1 (client & DI setup). Covers HttpClient factory registration, transient vs. singleton scoping, and DI wiring via `AddMaxioAdvancedBillingClient`.
2. **`dotnet-authentication`** — Step 2 (credentials & auth scheme). Covers HTTP Basic wiring, credential loading from configuration (not hardcoding), and when to set auth (before or during DI).
3. **`dotnet-calling-endpoints`** — Step 3+ (calling operations). Covers operation signatures, named vs. positional arguments, response envelope unwrapping, and async patterns.
4. **`dotnet-models`** — Step 4 (request/response models). Covers record construction, `required` field enforcement, optional field defaults, enums (`StringEnum<T>`, not C# enums), and unions.
5. **`dotnet-error-handling`** — Step 5 (error boundary). **MANDATORY FIRST** — covers typed (Case A) vs. raw (Case B) errors, `TryGet…` accessors, the two `JsonException` paths (2xx deserialization failure vs. non-2xx schema mismatch), and why a catch-all `JsonException` handler breaks retries.
6. **`dotnet-configuration-resilience`** — Step 6 (configuration & retries). Covers `RetryOptions`, `Timeout` (per-attempt semantics), `HttpMethodsToRetry` (status-code only, not transport failures), and idempotency implications of transport-level retries on POST.

All six skills are required. Do not skip Step 5 (`dotnet-error-handling`) — the error boundary must be designed before the first integration call, and its assumptions (2xx vs. non-2xx handling) shape how all error cases are caught and reported.

---

## Configuration bindings

The plan assumes configuration via a `Maxio` section (e.g., in `appsettings.json`):

```csharp
public class MaxioOptions
{
    public string ApiKey { get; set; } = "";
    public string Subdomain { get; set; } = "";
    public string? ProductFamilyHandle { get; set; } // or use ID "3023074" directly
    public string? BaseUrl { get; set; } // optional override for dev/mock
    public string Environment { get; set; } = "Us"; // "Us" or "Eu"
}
```

Bind via `configuration.GetSection("Maxio").Get<MaxioOptions>()` or use `IOptions<MaxioOptions>` in DI. Environment variables can override: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl`, `Maxio:Environment`.
