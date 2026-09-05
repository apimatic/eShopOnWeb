# Maxio Advanced Billing Integration Plan — eShopOnWeb Subscriptions

## Scope & Sequence

1. **Client registration & DI** — Create `MaxioAdvancedBillingClient` with Basic auth (API key as username, `"x"` as password)
2. **Configuration from settings** — Bind `Maxio:` section (ApiKey, Subdomain, ProductFamilyHandle, optional BaseUrl)
3. **Ensure Maxio customer exists** — Idempotent create or fetch using `reference` = eShopOnWeb user ID
4. **List available products** — Fetch product/plan details via `ListProducts` for UI endpoint
5. **Create/read subscription** — Enroll user in plan via `CreateSubscription`; read state & next-billing date via `ReadSubscription`

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal
C# identifier. The cancellation-token parameter really is named `ct`: in named
arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take
each one from that type's own map row, never from where a neighbouring type sits. A members
table names the namespace outright; otherwise the row's source path implies it
(`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root
namespace). Enums, unions, auth, server and client-config types are spread across different
child namespaces, and two types configured side by side in the same options object routinely
live in different ones. Dropping a type to the root or to `.Models` makes the implementer
guess the wrong `using`, and the build breaks.

### Operations

| Controller | Method signature | Request model + fields | Response envelope + inner fields | Error (Case A/B) + accessors | Pagination | Source |
|---|---|---|---|---|---|---|
| `client.Customers` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` · **must pass explicitly** | `CreateCustomerRequest`: `Customer (customer): CreateCustomer !req` · **CreateCustomer** (wire: `customer` in JSON): `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?` (eShopOnWeb user ID for idempotency), `Organization (organization): string?`, `Address (address): string?`, `Address2 (address_2): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `Locale (locale): string?`, `CcEmails (cc_emails): string?`, `VatNumber (vat_number): string?`, `TaxExempt (tax_exempt): bool?` | `CustomerResponse`: `Customer (customer): Customer !req` · **Customer** (from response): read `Id (id): int?`, `Email (email): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Reference (reference): string?`, `CreatedAt (created_at): DateTimeOffset?` | **Case A** · `ex.Error.TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `ex.Error.TryGetRawError(out RawError)` [fallback] | none | `operations/Customers.md` · `records-1-Ac-Cr.md` (CreateCustomer, CreateCustomerRequest) · `records-2-Cr-Ne.md` (Customer, CustomerResponse) |
| `client.Customers` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` · **must pass explicitly** | Wire param: `reference` ← `reference` (query string) | `CustomerResponse`: same as above | **Case B** · `ex.Error.StatusCode`, `ex.Error.ReadAsString()`, `ex.Error.ReadAsJson<T>()` | none | `operations/Customers.md` |
| `client.Customers` | `ReadCustomer(int id, CancellationToken ct = default)` | (path param: `id`) | `CustomerResponse`: same as above | **Case B** | none | `operations/Customers.md` |
| `client.Subscriptions` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` · **must pass explicitly** | `CreateSubscriptionRequest`: `Subscription (subscription): CreateSubscription !req` · **CreateSubscription** (wire: `subscription`): `CustomerId (customer_id): int?`, `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?`, `ProductPricePointId (product_price_point_id): int?`, `Reference (reference): string?` (for tracking subscription by eShopOnWeb order), `CustomerReference (customer_reference): string?`, `CouponCode (coupon_code): string?`, `CouponCodes (coupon_codes): IReadOnlyList<string>?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, optional components for metered usage (omit unless tracking `api-call` component) | `SubscriptionResponse`: `Subscription (subscription): Subscription?` · **Subscription** (from response): read `Id (subscription_id): int?`, `State (state): SubscriptionState?`, `CreatedAt (created_at): DateTimeOffset?`, `NextBillingAt (next_billing_at): DateTimeOffset?`, `CurrentPeriodStartsAt (current_period_starts_at): DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `CustomerId (customer_id): int?`, `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `Reference (reference): string?`, `BalanceInCents (balance_in_cents): long?`, `TotalRevenueInCents (total_revenue_in_cents): long?`, `CancelAtEndOfPeriod (cancel_at_end_of_period): bool?` | **Case A** · `ex.Error.TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `ex.Error.TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` · `records-2-Cr-Ne.md` (CreateSubscription, CreateSubscriptionRequest) · `records-4-Su-We.md` (SubscriptionResponse) |
| `client.Subscriptions` | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | Wire params: `page` ← `page`, `per_page` ← `perPage`, others pass `null` for unused filters (e.g., `state: null` to skip) | `IReadOnlyList<SubscriptionResponse>` · each item: `Subscription (subscription): Subscription?` with same fields as CreateSubscription response | **Case B** | `page`, `perPage`; defaults: `page` = 1, `perPage` = 20 | `operations/Subscriptions.md` |
| `client.Subscriptions` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` · **`include` must pass explicitly** (pass `null` to omit) | Wire param: `include` ← `include` (query, enum values from `map/models/enums.md`) | `SubscriptionResponse`: same as CreateSubscription response | **Case B** | none | `operations/Subscriptions.md` |
| `client.Products` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | Wire params: `page` ← `page`, `per_page` ← `perPage`, others pass `null` to skip; typical call: `ListProducts(dateField: null, filter: null, endDate: null, endDatetime: null, startDate: null, startDatetime: null, includeArchived: false, include: null, page: 1, perPage: 20, ct: ct)` | `IReadOnlyList<ProductResponse>` · each item: `Product (product): Product !req` · **Product** (from response): read `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `CreatedAt (created_at): DateTimeOffset?`, `ProductFamily (product_family): ProductFamily?` (nested, contains `Id`, `Name`, `Handle`) | **Case B** | `page`, `perPage`; defaults: `page` = 1, `perPage` = 20 | `operations/Products.md` · `records-3-Of-Su.md` (Product, ProductResponse) |

### Enums & Wire Values

From `map/models/enums.md`:

**`SubscriptionState`** (returned by subscription read/list/create):
- `Active`, `PastDue`, `Unpaid`, `Paused`, `Canceled`, `Expired`, `AwaitingSignup`, `Trialing`, `PendingCancellation`

**`IntervalUnit`** (product & subscription billing period):
- `Day`, `Month`, `Year`

**`CollectionMethod`** (optional on subscription create):
- `Automatic`, `Invoice`, `Remittance`

**`SubscriptionInclude`** (optional on ReadSubscription):
- `SelfServicePageToken`, `Coupons`, `ProductPricePoint`, `OfferMetadata`, `Metadata`, `Components`
- Pass `null` to skip; if needed, build as `new[] { SubscriptionInclude.Components }`

**`ListProductsInclude`** (optional on ListProducts):
- — (empty or pass `null` on typical calls)

### Client Construction & DI

From `sdk-map.md`:

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;

var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials
    {
        Username = configuration["Maxio:ApiKey"],  // API key
        Password = "x"                              // literal "x"
    },
    Environment = ServerEnvironment.Us,             // or .Eu if configured
    Server = new MaxioAdvancedBillingClientOptions.ServerOptions
    {
        Production = new ServerOptions.ProductionOptions
        {
            Us = new ServerOptions.ProductionOptionsUs
            {
                Site = configuration["Maxio:Subdomain"]  // e.g., "eshop-sandbox"
                // BaseUrl override: if needed, set to "http://localhost:8080" or custom
            }
        }
    }
};

var client = new MaxioAdvancedBillingClient(httpClient, options);
```

**DI registration** (alternative to manual construction):
```csharp
services.AddMaxioAdvancedBillingClient(o =>
{
    o.BasicAuth = new BasicAuthCredentials
    {
        Username = configuration["Maxio:ApiKey"],
        Password = "x"
    };
    o.Environment = ServerEnvironment.Us;
    // Configure o.Server as above if needed
});
// Then inject `MaxioAdvancedBillingClient` into your service
```

**Configuration binding** (appsettings.json or user secrets):
```json
{
  "Maxio": {
    "ApiKey": "your-api-key",
    "Subdomain": "eshop-sandbox",
    "Environment": "Sandbox",
    "ProductFamilyHandle": "eshop-subscribe",
    "BaseUrl": null
  }
}
```

### Idempotency Strategy

**Customer creation** — Use `reference` field (set to eShopOnWeb user ID):
1. Call `ReadCustomerByReference(userReference, ct)` to check if customer exists
2. If 404 (not found) or empty, call `CreateCustomer` with `Reference` set to `userReference`
3. If customer exists, reuse its `Id` for subscription operations

**Subscription creation** — No built-in idempotency key on the SDK; use these guards:
1. Before creating a subscription for a customer + product pair, call `ListSubscriptions` with `state: SubscriptionStateFilter.Active` to check for active subscriptions for that customer and product
2. If subscription exists and is `Active`, skip creation and return existing subscription details
3. On 422 errors (validation), extract error details from `ErrorListResponse1.Errors` (a list of error strings per field)

---

## Trap Notes

- **Step 1 (client registration)** — The SDK's `MaxioAdvancedBillingClient` wraps an injected `HttpClient`; that client is **not** created or managed by the SDK. Register `IHttpClientFactory` in DI and pass a long-lived client instance (reuse across multiple calls). **MUST load `dotnet-client-initialization`** before wiring the client.

- **Step 2 (configuration)** — Basic auth is **not** configurable after client construction; set `options.BasicAuth` **before** passing to the client constructor. Load the API key from configuration (e.g., `IConfiguration["Maxio:ApiKey"]`), **never** hardcode it. **MUST load `dotnet-authentication`** before setting credentials.

- **Step 3 (customer idempotency)** — The `CreateCustomer` operation does not accept an idempotency key; guard against double-creation by checking `ReadCustomerByReference` first. The `reference` field is unique per site, so store the eShopOnWeb user ID there. If a creation fails with a 422 (validation error), the error is a `CreateCustomerError` (Case A typed); extract details via `TryGetCustomerErrorResponse1`. **MUST load `dotnet-calling-endpoints`** for named-argument best practices when calling with optional fields.

- **Step 4 (listing products)** — The `ListProducts` call has many optional filter parameters (most `null` for a base query). The signature requires all 8 params + `page`/`perPage` to be passed explicitly; pass `null` for unused filters. Pagination is manual: loop over `page` until the response is empty or smaller than `perPage`. **MUST load `dotnet-models`** — the `IntervalUnit` enum, `ProductFamily` nested object, and the union types inside filters are all non-plain fields.

- **Step 5 (subscription creation)** — The `CreateSubscription` call accepts `customer_id` (Maxio ID, from step 3) **or** `customer_reference` (the eShopOnWeb user ID stored in step 3); use the ID path for safety. The request envelope is `CreateSubscriptionRequest { Subscription: CreateSubscription { … } }`; build it immutably with init-only properties. On 422, the error is `CreateSubscriptionError` (Case A); extract the list of error messages from `TryGetErrorListResponse1(out ErrorListResponse1)` and iterate the `ErrorListResponse1.Errors` list. **MUST load `dotnet-error-handling`** — Case A vs. Case B distinction is critical, and `JsonException` can mask 422 bodies on deserialization failure.

- **Both calls (ReadCustomerByReference & CreateSubscription)** — These operations may throw `SdkException<RawError>` (Case B, no typed accessors) on network or parsing failures; always wrap in a try-catch that handles both the typed Case A and the fallback Case B. Timeouts and retries are governed by the client's `Retry` options; a failed create is **not** automatically resent (see step 6 for retry semantics). **MUST load `dotnet-configuration-resilience`** — understand what `MaxRetries` really gates and whether a failed `POST` is retried.

- **Error boundary** — Map every `SdkException<CreateCustomerError>` and `SdkException<CreateSubscriptionError>` (and `SdkException<RawError>` for reads) with a dedicated handler. **BOTH** of these **must** be caught separately from generic exception handlers:
  - A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
  - A **non-2xx** body that does not match its operation's generated `CreateCustomerError` or `CreateSubscriptionError` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

  **MUST load `dotnet-error-handling`** before writing that boundary.

---

## REQUIRED READING

Before implementation starts, load **each** of these companion skills; the sheet does not carry their contents, only names them:

- **`dotnet-client-initialization`** — Step 1 (client & DI registration). Covers `HttpClient` reuse, `AddMaxioAdvancedBillingClient` callback, and transient vs. long-lived client wrapper lifetimes.
- **`dotnet-authentication`** — Step 2 (credentials & auth scheme). Covers HTTP Basic (username = API key, password = `"x"`), per-environment config, and when to set credentials.
- **`dotnet-calling-endpoints`** — Step 1–5 (operation calls). Covers named-argument idiom for optional params, async usage, and when parameters **must** be passed explicitly.
- **`dotnet-models`** — Step 4–5 (request/response models, enums, unions). Covers immutable records with init-only setters, `StringEnum<T>` construction, union `TryGet…` accessors, and `Optional<T>` wrapping.
- **`dotnet-error-handling`** — Step 1–5 (error boundary). Covers Case A (typed `CreateCustomerError`, etc.) vs. Case B (`RawError`) distinction, `TryGet…` accessors, and `JsonException` handling (both 2xx deserialization failure and non-2xx shape mismatch).
- **`dotnet-configuration-resilience`** — Step 1–3 (retry, timeout, server override). Covers `RetryOptions`, `Timeout` per-attempt (not total), `HttpMethodsToRetry` filtering, and why a failed `POST` write may be retried on transport error.

---

## Assumptions & Blockers

**Assumptions:**
- eShopOnWeb user ID is stable and unique (will be used as Maxio `reference` for idempotency).
- JWT authentication on PublicApi endpoints is implemented outside this plan.
- In-memory database persists across the run; state (customer Maxio ID, subscription ID) is stored in the application's own DB to avoid repeated Maxio lookups.
- Configuration section `Maxio:` (ApiKey, Subdomain, ProductFamilyHandle, optional BaseUrl and Environment) is supplied at runtime (never hardcoded).
- Maxio sandbox account and API key are available during dev/test.

**Blockers:**
- None. The Maxio API and SDK support all required operations. The idempotency strategy (check-before-create via `reference`) is fully documented in the operation Notes.

---

## Configuration Example

**appsettings.json:**
```json
{
  "Maxio": {
    "ApiKey": "YOUR_SANDBOX_API_KEY",
    "Subdomain": "your-sandbox-subdomain",
    "Environment": "Sandbox",
    "ProductFamilyHandle": "eshop-subscribe",
    "BaseUrl": null
  }
}
```

**User Secrets (for sensitive values in dev):**
```bash
dotnet user-secrets init
dotnet user-secrets set "Maxio:ApiKey" "YOUR_SANDBOX_API_KEY"
```

---

## Summary

This plan provides exact signatures, request/response shapes, error cases, and error accessors for four core operations (CreateCustomer, ReadCustomerByReference, CreateSubscription, ReadSubscription, ListProducts). Idempotency is achieved by storing eShopOnWeb user ID as the Maxio `reference` and checking `ReadCustomerByReference` before creates. All companion skills are named, and both `JsonException` hazards (2xx deserialization and non-2xx shape mismatch) are flagged for the error boundary. Configuration is entirely external, and the in-memory database design is the implementer's responsibility.
