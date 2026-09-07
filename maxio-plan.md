# Maxio Integration Plan — eShopOnWeb Subscriptions

## Scope & Sequence

1. **Dependency injection & configuration** — Register MaxioAdvancedBillingClient, wire Maxio credentials from user-secrets, set base URL for sandbox (cp-exp-1).
2. **Customer idempotency layer** — Before creating subscriptions, ensure the logged-in eShopOnWeb user has a Maxio customer record; store the userId ↔ customerId mapping so subsequent calls reuse it without duplicates.
3. **GET /api/subscription-plans** — Fetch available plans from Maxio using product handles (`eshop-pro`, `basic-plan`) and return a JSON list of plan name + price + handle.
4. **POST /api/subscriptions** — Create a subscription for the logged-in user; resolve or create their Maxio customer, then create a subscription keyed by product handle, bound to their eShopOnWeb userId for future lookups.
5. **GET /api/my-subscriptions** — List the logged-in user's active Maxio subscriptions by querying the Maxio customer; filter for the expected product family.
6. **Error handling & logging** — Wrap all Maxio calls in a unified error boundary; catch SDK exceptions, log, and return HTTP 400 (invalid request), 409 (conflict/duplicate), or 5xx (Maxio outage or malformed response).

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operation: ReadProductByHandle

| Aspect | Details |
|---|---|
| **Controller property** | `client.Products` |
| **Signature** | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` |
| **Request body** | None — `apiHandle` is a URL path parameter (e.g., `"eshop-pro"` or `"basic-plan"`) |
| **Response envelope** | `ProductResponse` — unwrap to `ProductResponse.Product` (type: `Product` from `MaxioAdvancedBilling.Models`) |
| **Response shape** | `Product`: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, … (many optional fields) |
| **Error case** | **Case B** — `SdkException<RawError>` (no typed accessors; 404 on unknown handle) |
| **Pagination** | None |
| **Source** | `map/operations/Products.md`, `Models/ProductResponse.cs` |

### Operation: ListSubscriptions

| Aspect | Details |
|---|---|
| **Controller property** | `client.Subscriptions` |
| **Signature** | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| **Query params (pass explicitly; nullable params → pass `null` to skip)** | `state`, `product`, `productPricePointId`, `coupon`, `couponCode`, `dateField`, `startDate`, `endDate`, `startDatetime`, `endDatetime`, `metadata`, `direction`, `sort`, `include`; `page` and `perPage` have defaults (1, 20) |
| **Request body** | None |
| **Response envelope** | `IReadOnlyList<SubscriptionResponse>` — each wraps a `Subscription` (type: `Subscription` from `MaxioAdvancedBilling.Models`) |
| **Subscription key fields** | `Id (id): int?`, `CustomerId (customer_id): int?`, `ProductId (product_id): int?`, `ProductHandle (product_handle): string?`, `State (state): SubscriptionState?` (enum), `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `CancelledAt (cancelled_at): DateTimeOffset?`, … (many optional fields; for "active" filter by state) |
| **Error case** | **Case B** — `SdkException<RawError>` (no typed accessors) |
| **Pagination** | Manual `page`+`perPage` (defaults 1, 20) |
| **Source** | `map/operations/Subscriptions.md`, `Models/Subscription.cs` |

### Operation: CreateCustomer

| Aspect | Details |
|---|---|
| **Controller property** | `client.Customers` |
| **Signature** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` |
| **Request body** | `CreateCustomerRequest` (wraps `CreateCustomer` record) |
| **CreateCustomer fields** | `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `CcEmails (cc_emails): string?`, `Organization (organization): string?`, `Reference (reference): string?` (use this for idempotent lookup: pass the eShopOnWeb userId), `Address (address): string?`, `Address2 (address_2): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `Locale (locale): string?`, `VatNumber (vat_number): string?`, `TaxExempt (tax_exempt): bool?`, `TaxExemptReason (tax_exempt_reason): string?`, `ParentId (parent_id): int?`, `SalesforceId (salesforce_id): string?` |
| **Response envelope** | `CustomerResponse` — unwrap to `CustomerResponse.Customer` (type: `Customer` from `MaxioAdvancedBilling.Models`) |
| **Customer key fields** | `Id (id): int?` (Maxio-generated customerId), `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, … |
| **Error case** | **Case A** — `SdkException<CreateCustomerError>` with typed accessors: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422 — validation error], `TryGetRawError(out RawError)` [fallback] |
| **Pagination** | None |
| **Notes** | Use the eShopOnWeb userId as the `Reference` field so subsequent lookups can retrieve the same customer without creating a duplicate. The Notes say "The only validation restriction is that you may only create one customer for a given reference value. If provided, the `reference` value must be unique." |
| **Source** | `map/operations/Customers.md`, `Models/CreateCustomer.cs`, `Models/CreateCustomerRequest.cs` |

### Operation: ReadCustomerByReference

| Aspect | Details |
|---|---|
| **Controller property** | `client.Customers` |
| **Signature** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` |
| **Query param** | `reference` (the eShopOnWeb userId; pass explicitly) |
| **Request body** | None |
| **Response envelope** | `CustomerResponse` — unwrap to `CustomerResponse.Customer` (type: `Customer` from `MaxioAdvancedBilling.Models`) |
| **Response shape** | Same as CreateCustomer's response |
| **Error case** | **Case B** — `SdkException<RawError>` (404 if not found) |
| **Pagination** | None |
| **Notes** | "Returns a customer by their unique reference ID. It will return a single match." Use this to check if a Maxio customer already exists for a logged-in user before calling CreateCustomer. |
| **Source** | `map/operations/Customers.md` |

### Operation: CreateSubscription

| Aspect | Details |
|---|---|
| **Controller property** | `client.Subscriptions` |
| **Signature** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` |
| **Request body** | `CreateSubscriptionRequest` (wraps `CreateSubscription` record) |
| **CreateSubscription required fields for scope** | `ProductHandle (product_handle): string?` (use `"eshop-pro"` or `"basic-plan"`; either this or `ProductId`), `CustomerId (customer_id): int?` (the Maxio customer ID resolved via CreateCustomer or ReadCustomerByReference) |
| **CreateSubscription optional fields to consider** | `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` (enum), `Reference (reference): string?` (for idempotent subscription lookup; use a derived key like `"{userId}-{productHandle}"`), `ReceivesInvoiceEmails (receives_invoice_emails): string?`, `CouponCodes (coupon_codes): IReadOnlyList<string>?`, `NextBillingAt (next_billing_at): DateTimeOffset?`, `MetaFields (metafields): IReadOnlyDictionary<string, string>?` |
| **Other optional fields (not in immediate scope)** | `ProductId (product_id): int?`, `ProductPricePointId (product_price_point_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?`, `CustomPrice (custom_price): SubscriptionCustomPrice?`, `CouponCode (coupon_code): string?`, `PaymentProfileId (payment_profile_id): int?`, `PaymentProfileAttributes (payment_profile_attributes): PaymentProfileAttributes?`, `CreditCardAttributes (credit_card_attributes): PaymentProfileAttributes?`, `Components (components): IReadOnlyList<CreateSubscriptionComponent>?`, … |
| **Response envelope** | `SubscriptionResponse` — unwrap to `SubscriptionResponse.Subscription` (type: `Subscription` from `MaxioAdvancedBilling.Models`) |
| **Subscription key fields** | `Id (id): int?` (Maxio subscription ID), `State (state): SubscriptionState?` (enum), `ProductId (product_id): int?`, `ProductHandle (product_handle): string?`, `CustomerId (customer_id): int?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextBillingAt (next_billing_at): DateTimeOffset?`, `Reference (reference): string?`, `CancelledAt (cancelled_at): DateTimeOffset?`, … |
| **Error case** | **Case A** — `SdkException<CreateSubscriptionError>` with typed accessors: `TryGetErrorListResponse1(out ErrorListResponse1)` [422 — validation error], `TryGetRawError(out RawError)` [fallback] |
| **Pagination** | None |
| **Notes** | "Creates a Subscription for a customer and product. Specify the product with `product_id` or `product_handle`. Identify an existing customer with `customer_id` or `customer_reference`. … Payment information may be required to create a subscription, depending on the options for the Product being subscribed." **For the eShopOnWeb scope (no payment method required per constraint)**, the plan must be configured in Maxio with `require_credit_card: false` and a zero-dollar price (if testing) or the appropriate monthly price. The constraint states "payment method not required", so ensure that the plan entity in Maxio reflects this. |
| **Source** | `map/operations/Subscriptions.md`, `Models/CreateSubscription.cs`, `Models/CreateSubscriptionRequest.cs` |

### Enum: SubscriptionState

| Value (wire) | C# Member |
|---|---|
| `pending` | `SubscriptionState.Pending` |
| `active` | `SubscriptionState.Active` |
| `past_due` | `SubscriptionState.PastDue` |
| `soft_failure` | `SubscriptionState.SoftFailure` |
| `expired` | `SubscriptionState.Expired` |
| `cancelled` | `SubscriptionState.Cancelled` |
| `assessing` | `SubscriptionState.Assessing` |
| `on_hold` | `SubscriptionState.OnHold` |
| `suspended` | `SubscriptionState.Suspended` |
| `trialing` | `SubscriptionState.Trialing` |
| `awaiting_signup` | `SubscriptionState.AwaitingSignup` |

Use `SubscriptionState.Active` when filtering for active subscriptions. Source: `map/models/enums.md`.

### Enum: CollectionMethod

| Value (wire) | C# Member |
|---|---|
| `automatic` | `CollectionMethod.Automatic` |
| `remittance` | `CollectionMethod.Remittance` |
| `prepaid` | `CollectionMethod.Prepaid` |
| `invoice` | `CollectionMethod.Invoice` |

For subscriptions without an explicit collection method, the plan default applies. Source: `map/models/enums.md`.

---

## Entity & Model Definitions

### DTO: SubscriptionPlanDto (for GET /api/subscription-plans)

```csharp
public record SubscriptionPlanDto(
    string Handle,           // e.g., "eshop-pro", "basic-plan"
    string Name,             // e.g., "Pro", "Basic"
    long PriceInCents,       // e.g., 29900 for $299.00/mo
    int IntervalInMonths,    // billing period in months (1 for monthly)
    string? Description      // optional plan description
);
```

### DTO: SubscriptionDto (for GET /api/my-subscriptions)

```csharp
public record SubscriptionDto(
    int SubscriptionId,           // Maxio subscription ID
    string ProductHandle,         // e.g., "eshop-pro"
    string ProductName,           // e.g., "Pro"
    DateTimeOffset? NextBillingAt, // when the next payment is due
    string State                  // e.g., "active"
);
```

### DTO: CreateSubscriptionRequest (for POST /api/subscriptions)

```csharp
public record CreateSubscriptionRequest(
    string PlanHandle  // e.g., "eshop-pro" or "basic-plan"
);
```

### DTO: CreateSubscriptionResponse (for POST /api/subscriptions)

```csharp
public record CreateSubscriptionResponse(
    int SubscriptionId,       // Maxio subscription ID
    string State,             // e.g., "pending" or "active"
    DateTimeOffset? NextBillingAt // when the next payment is due
);
```

### Application Entity: UserMaxioCustomerMapping (In-Memory Cache or Database)

**Purpose:** Store the mapping between eShopOnWeb userId and Maxio customerId so that:
- Duplicate customers are never created (idempotency).
- Subsequent API calls can resolve the Maxio customer without an extra lookup on every request.

**Fields:**
- `UserId` (string) — eShopOnWeb user identifier (from `User.Id`)
- `MaxioCustomerId` (int) — Maxio-generated customer ID
- `CreatedAt` (DateTimeOffset) — when the mapping was created
- `MaxioReference` (string, optional) — the `Reference` field sent to Maxio (echoes the eShopOnWeb userId for verification)

**Lookup strategy:**
1. On API request, extract the logged-in `UserId` from the JWT principal.
2. Query the mapping store (in-memory cache or database row).
3. If found and Maxio customer exists, reuse; if not found, create Maxio customer and store the mapping.
4. Subsequent calls to Maxio use the cached customerId.

**For this plan, a simple in-memory concurrent dictionary is acceptable** (per session, one instance per application instance). For production, promote to a database table with a unique index on `UserId`.

---

## Dependency Injection & Configuration Wiring

### appsettings.json (or appsettings.Development.json)

```json
{
  "Maxio": {
    "Subdomain": "cp-exp-1",
    "BaseUrl": "https://cp-exp-1.chargify.com",
    "ProductFamilyHandle": "eshop-subscribe"
  }
}
```

### User Secrets (dotnet user-secrets)

```
{
  "Maxio:ApiKey": "<your-api-key>",
  "Maxio:BaseUrl": "https://cp-exp-1.chargify.com"  // optional override
}
```

Do **not** store `ApiKey` in appsettings.json or code; always use user-secrets or environment variables for sensitive credentials.

### Startup Registration (Program.cs or Startup.cs)

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;

// In your service registration (e.g., Program.cs):
var maxioConfig = configuration.GetSection("Maxio");
var apiKey = configuration["Maxio:ApiKey"]; // from user-secrets
var subdomain = maxioConfig["Subdomain"] ?? "cp-exp-1";
var baseUrl = configuration["Maxio:BaseUrl"] ?? $"https://{subdomain}.chargify.com";

services.AddMaxioAdvancedBillingClient(o =>
{
    o.BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" };
    o.Environment = ServerEnvironment.Us;
    o.Server = new MaxioAdvancedBilling.Servers.ServerOptions
    {
        Production = new MaxioAdvancedBilling.Servers.ProductionOptions
        {
            Us = new MaxioAdvancedBilling.Servers.ProductionServerOptions
            {
                BaseUrl = baseUrl
            }
        }
    };
});

// Register configuration as singleton
services.Configure<MaxioOptions>(maxioConfig);

// Register the UserMaxioCustomerMapping cache (in-memory for this scope)
services.AddSingleton<IUserMaxioCustomerMappingCache, InMemoryUserMaxioCustomerMappingCache>();

// Register the subscription service
services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();
```

### Configuration Record

```csharp
public record MaxioOptions
{
    public string Subdomain { get; set; } = "cp-exp-1";
    public string ProductFamilyHandle { get; set; } = "eshop-subscribe";
    public string BaseUrl { get; set; } = "https://cp-exp-1.chargify.com";
}
```

### Error Handling Boundary (Middleware or Service Wrapper)

**Purpose:** Centralize SDK exception handling so all Maxio calls return consistent HTTP status codes.

```csharp
public class MaxioErrorHandler
{
    public static int GetHttpStatusCode(Exception ex)
    {
        if (ex is SdkException<RawError> rawError)
        {
            return (int)rawError.Error.StatusCode;
        }
        
        if (ex is SdkException<CreateCustomerError> customerError)
        {
            if (customerError.Error.TryGetCustomerErrorResponse1(out var e422))
                return 422; // Unprocessable Entity
            return 500; // fallback for unknown shape
        }
        
        if (ex is SdkException<CreateSubscriptionError> subError)
        {
            if (subError.Error.TryGetErrorListResponse1(out var e422))
                return 422;
            return 500;
        }
        
        // Catch-all for SDK exceptions
        if (ex is SdkException sde)
            return 500;
        
        return 500; // unknown error
    }
}
```

**CRITICAL CAVEAT:**
- `System.Text.Json.JsonException` from a **drifted 2xx body** (missing required member) surfaces from deserialization **before** the error type wrapper is constructed, so it will NOT be an `SdkException` — the error boundary must catch `JsonException` and map it to HTTP 422 or 5xx (see `dotnet-error-handling` for the exact rule).
- `System.Text.Json.JsonException` from a **non-2xx body** that doesn't match the generated `{Operation}Error` shape **replaces** the `SdkException` **during error object construction**, destroying the HTTP status code — the error boundary must NOT assume status recovery; log the exception and return a deterministic 5xx to avoid a retry loop (see `dotnet-error-handling` for the two-case rule).

**MUST load `dotnet-error-handling`** before writing the error boundary. These caveats belong in the first contract sheet, not a later revision: the boundary is written early, and a caveat that arrives afterwards arrives too late to shape it.

---

## REQUIRED READING

Load these companion skills **before implementation starts**. The sheet deliberately does not carry their contents.

| Skill | Applies at step |
|---|---|
| `dotnet-client-initialization` | Step 1 (DI & client construction) |
| `dotnet-authentication` | Step 1 (wiring BasicAuth credentials) |
| `dotnet-calling-endpoints` | Steps 2–5 (all SDK operation calls) |
| `dotnet-models` | Steps 3–5 (request/response model field names, required flags, nullable handling) |
| `dotnet-error-handling` | Step 6 (exception boundary; **CRITICAL — read before writing catch handlers**) |
| `dotnet-configuration-resilience` | Step 1 (retry/timeout semantics; **CRITICAL — HttpMethodsToRetry gates status trigger, not transport retry**) |

**Both of these hazard rows must be included in the FIRST integration boundary code, not a later revision:**
- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

---

## Assumptions & Blockers

### Assumptions

1. **The eShopOnWeb application has a working JWT-authenticated PublicApi.** Endpoints `/api/subscription-plans`, `/api/subscriptions` (POST/GET) will be added to this controller. The `User.Id` claim is available on the `HttpContext.User` principal.
2. **Maxio products (`eshop-pro`, `basic-plan`) exist on site cp-exp-1 in product family `eshop-subscribe`.** They are configured with:
   - No trial period
   - No setup fee
   - No expiration date
   - No tax
   - `require_credit_card: false` (payment method not required per scope)
   - Monthly billing interval
3. **The `.NET 10 SDK` is installed.** The project will use `dotnet-sdk` roll-forward behavior to build against pinned `8.0.x` Maxio SDK via `rollForward: latestMajor` in the project file or global.json.
4. **User-secrets are configured** for the development environment. The API key and base URL will be stored there and NOT in appsettings.json.
5. **In-memory database is acceptable for the user ↔ Maxio customer mapping** within a single session. For multi-server or persistent requirements, this must be promoted to a database table.
6. **The application does not require idempotent subscriptions** across multiple API calls (if the same POST is sent twice, the second call will create a second subscription). The plan implements idempotent customer creation via `Reference` field, but subscriptions themselves are created fresh on each call. To add subscription idempotency, a reference field can be passed (see `Reference` in `CreateSubscription` optional fields).

### Blockers

**None — the SDK map provides all required operation signatures, models, error types, and enum values. No capability gap identified.**

---

## Implementation Checklist

- [ ] Load `dotnet-client-initialization` and wire MaxioAdvancedBillingClient in DI.
- [ ] Load `dotnet-authentication` and set BasicAuth credentials from user-secrets.
- [ ] Load `dotnet-configuration-resilience` and understand retry/timeout bounds before setting them.
- [ ] Load `dotnet-error-handling` and implement the two-case `JsonException` boundary rule before touching exception handlers.
- [ ] Load `dotnet-calling-endpoints` and confirm parameter order and named argument names for each operation.
- [ ] Load `dotnet-models` and verify request/response model field names, wire names, and required flags against the map.
- [ ] Implement UserMaxioCustomerMapping cache (in-memory `ConcurrentDictionary<string, int>` for userId → customerId).
- [ ] Implement ReadCustomerByReference lookup before CreateCustomer to ensure no duplicate customers.
- [ ] Implement POST /api/subscriptions using CreateSubscription with ProductHandle and CustomerId from the mapping.
- [ ] Implement GET /api/subscription-plans using ReadProductByHandle for each plan.
- [ ] Implement GET /api/my-subscriptions using ListSubscriptions filtered by the customer.
- [ ] Write end-to-end tests (offline mock or live sandbox).
- [ ] Verify no secrets in code or appsettings.json; all credentials via user-secrets or environment.
