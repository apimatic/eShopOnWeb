# Maxio SDK Integration Plan — eShopOnWeb Recurring Subscriptions

## Scope & Sequence

1. **Client initialization & configuration** — register MaxioAdvancedBillingClient with DI, load API credentials (key, subdomain) from configuration, set Basic auth (username=key, password="x"), override base URL if provided
2. **Ensure Maxio customer exists (idempotent)** — call `ReadCustomerByReference(reference)` with the logged-in user ID; on 404, call `CreateCustomer(request)` with first/last name and email from user identity
3. **Fetch subscription plans** — call `ListProductsByHandle` or `ListProducts` to enumerate plans with handles `eshop-pro`, `basic-plan` from product family `eshop-subscribe`
4. **Subscribe user to plan** — call `CreateSubscription(request)` with customer reference, product handle, defer payment (no card required per scope)
5. **List user's subscriptions** — call `ListCustomerSubscriptions(customerId)` to fetch all active subscriptions for display; extract state, next billing date, plan handle/name

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

| Operation | Signature | Request Model + Fields | Response Envelope + Fields | Error Case | Pagination | Source |
|---|---|---|---|---|---|---|
| **Ensure customer exists** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | Reference: query param (wire: `reference`) — C# `reference: string` — required | `CustomerResponse` { `Customer (customer): Customer !req` } → read `.Customer` · fields: `Id (id): int?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?` | **Case B** — `SdkException<RawError>` · access via `.Error.StatusCode`, `.Error.ReadAsString()`, `.Error.ReadAsJson<T>()` | none | `operations/Customers.md` |
| **Create customer (fallback)** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — **body must pass explicitly** | `CreateCustomerRequest` { `Customer (customer): CreateCustomer !req` } containing: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?` (set to user ID for idempotent lookup), `Organization (organization): string?`, `Address (address): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?` | `CustomerResponse` { `Customer (customer): Customer !req` } → read `.Customer` (same fields as ReadCustomerByReference) | **Case A** — `SdkException<CreateCustomerError>` · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Customers.md` · `records-2-Cr-Ne.md` |
| **List subscription plans** | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 optional params before page/perPage; **pass `null` to skip** | Query params (wire ← C#): `date_field` ← `dateField`, `filter` ← `filter`, `end_date` ← `endDate`, `end_datetime` ← `endDatetime`, `start_date` ← `startDate`, `start_datetime` ← `startDatetime`, `include_archived` ← `includeArchived`, `include` ← `include`, `page` ← `page`, `per_page` ← `perPage` | `IReadOnlyList<ProductResponse>` — iterate and read `.Product` on each item · fields: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `ProductFamily (product_family): ProductFamily?` { `Handle (handle): string?`, `Id (id): int?`, `Name (name): string?` }, `ArchivedAt (archived_at): DateTimeOffset?` | **Case B** — `SdkException<RawError>` | manual `page` + `perPage` (defaults: `page`=1, `perPage`=20) | `operations/Products.md` · `records-3-Of-Su.md` |
| **Get plan by handle** | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | Handle: path param (wire: in URL as `/products/handle/{api_handle}.json`) — C# `apiHandle: string` — required | `ProductResponse` { `Product (product): Product !req` } → read `.Product` (same fields as ListProducts) | **Case B** — `SdkException<RawError>` | none | `operations/Products.md` |
| **Create subscription** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — **body must pass explicitly** | `CreateSubscriptionRequest` { `Subscription (subscription): CreateSubscription !req` } containing: `CustomerId (customer_id): int?` **OR** `CustomerReference (customer_reference): string?` (use reference for idempotent lookup), `ProductHandle (product_handle): string?` **OR** `ProductId (product_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?` (optional, uses default price point if omitted), `NextBillingAt (next_billing_at): DateTimeOffset?`, `Reference (reference): string?` (optional: unique ref from app), `CouponCode (coupon_code): string?` or `CouponCodes (coupon_codes): IReadOnlyList<string>?`, `Metafields (metafields): IReadOnlyDictionary<string, string>?` (optional custom data) | `SubscriptionResponse` { `Subscription (subscription): Subscription !req` } → read `.Subscription` · fields: `Id (id): int?`, `State (state): SubscriptionState?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `ActivatedAt (activated_at): DateTimeOffset?`, `TrialEndedAt (trial_ended_at): DateTimeOffset?`, `ExpiresAt (expires_at): DateTimeOffset?`, `Customer (customer): Customer?`, `Product (product): Product?`, `ProductPricePointId (product_price_point_id): int?`, `CouponCodes (coupon_codes): IReadOnlyList<string>?`, `Reference (reference): string?` | **Case A** — `SdkException<CreateSubscriptionError>` · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` · `records-2-Cr-Ne.md` |
| **List customer subscriptions** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | Customer ID: path param (wire: in URL) — C# `customerId: int` — required | `IReadOnlyList<SubscriptionResponse>` — iterate and read `.Subscription` on each item (same fields as CreateSubscription) | **Case B** — `SdkException<RawError>` | none | `operations/Customers.md` |

### Enum Values

**`SubscriptionState` (`MaxioAdvancedBilling.Models.Enums.SubscriptionState`)** — used in Subscription `.State` field:
- `Active (active)` — normal, active subscription, paid and up to date
- `Canceled (canceled)` — subscription has been canceled
- `Expired (expired)` — subscription expired (expiration date passed)
- `Trialing (trialing)` — in trial period
- `PastDue (past_due)` — payment overdue
- `Paused (paused)` — subscription paused
- `OnHold (on_hold)` — on hold
- `AwaitingSignup (awaiting_signup)` — awaiting initial signup completion
- `TrialEnded (trial_ended)` — trial period ended

(See `enums.md` for full list; these are the most common states for monitoring active subscriptions.)

**`IntervalUnit` (`MaxioAdvancedBilling.Models.Enums.IntervalUnit`)** — used in Product `.IntervalUnit` field:
- `Day (day)` — billing interval in days
- `Month (month)` — billing interval in months

**`CollectionMethod` (`MaxioAdvancedBilling.Models.Enums.CollectionMethod`)** — if needed for subscription creation:
- `Automatic (automatic)` — automatic recurring charge (default for most integrations)
- `Invoice (invoice)` — invoice-based (requires manual payment)
- `Remittance (remittance)` — remittance billing
- `Prepaid (prepaid)` — prepaid model

### Client Construction & Auth

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using MaxioAdvancedBilling.Core.Configuration;

// In DI setup (appsettings.json or user-secrets):
// Maxio:ApiKey = <your API key>
// Maxio:Subdomain = <site subdomain, e.g. "cp-exp-1">
// Maxio:BaseUrl = <optional override, e.g. http://localhost:8080>

services.AddMaxioAdvancedBillingClient(options =>
{
    var config = configuration.GetSection("Maxio");
    options.BasicAuth = new BasicAuthCredentials
    {
        Username = config["ApiKey"],
        Password = "x"  // literal string "x"
    };
    
    // Set environment (US or EU)
    options.Environment = ServerEnvironment.Us;
    
    // Optional: override base URL
    if (!string.IsNullOrEmpty(config["BaseUrl"]))
    {
        options.Server.Production.Us.BaseUrl = config["BaseUrl"];
    }
    
    // Optional: configure retries/timeout (see dotnet-configuration-resilience)
    options.Retry = RetryOptions.Default();
});
```

### Error Handling Boundaries

Two directions where `System.Text.Json.JsonException` reaches the error boundary — **both require dedicated handling in REQUIRED READING section below**:

1. **Drifted/malformed 2xx body** (missing `required` member in response) surfaces as `JsonException` from deserialization, **not** as `SdkException` — a SDK-exception-only catch ladder lets it escape
2. **Non-2xx body that doesn't match `{Operation}Error` shape** throws `JsonException` *while error object is constructed*, **replacing** the `SdkException` and destroying HTTP status — a boundary that maps every `JsonException` to 5xx then retries 5xx retries something that can never succeed

**MUST load `dotnet-error-handling`** before writing any catch blocks.

---

## Assumptions & Blockers

### Assumptions

1. **User identity is available** in the integration context (HttpContext, claims principal, or similar) with accessible `Id`, `FirstName`, `LastName`, `Email` fields or can be resolved via DI
2. **Maxio site is seeded** with product family `eshop-subscribe`, products `eshop-pro` and `basic-plan`, and no payment method is required for subscription creation (product `require_credit_card` = false)
3. **Subscription state is the source of truth** — the app does not maintain separate local subscription state; it reads `.State` on each call and reflects it in the UI
4. **Idempotent customer creation uses `reference`** — the app will pass the user's local ID as `reference` on CreateCustomer, and on next call will use ReadCustomerByReference(reference) to find or create. This assumes the reference value is unique per eShopOnWeb user and persists across sessions
5. **Configuration binding key is `Maxio:`** — the plan uses `configuration.GetSection("Maxio")` and keys `ApiKey`, `Subdomain`, `BaseUrl`. The implementer will match these to their configuration source (environment, appsettings, user-secrets)

### Blockers

None at this time. The SDK map contains all necessary operations, request/response shapes, and error types. No provider capability gaps detected.

---

## REQUIRED READING

Before implementation starts, load these companion skills in order. The sheet deliberately does not carry their contents — each covers patterns, defaults, gotchas, and worked examples that a signature cannot show.

| Skill | Governs |
|---|---|
| **`dotnet-client-initialization`** | Client construction, HttpClient/IHttpClientFactory lifecycle, DI registration via `AddMaxioAdvancedBillingClient`, transient vs long-lived client semantics |
| **`dotnet-authentication`** | HTTP Basic credentials (Username=API key, Password="x"), loading from configuration, rotation/refresh patterns |
| **`dotnet-calling-endpoints`** | Operation call syntax, required-but-nullable parameter handling, request body construction, response envelope unwrapping (`.Product`, `.Customer`, `.Subscription`), named vs positional arguments |
| **`dotnet-models`** | Record immutability, `required` properties, nullable vs optional fields, union types (if any are encountered in custom price/component fields), `StringEnum<T>` construction and membership |
| **`dotnet-error-handling`** | Throw-only (no Result variants), typed Case A errors with `TryGet…` accessors vs Case B `RawError`, when `SdkException<T>` replaces or wraps provider payloads, **`System.Text.Json.JsonException` handling in both directions** (deserialization failures on 2xx, error-object construction failures on non-2xx) |
| **`dotnet-configuration-resilience`** | Retry options (`HttpMethodsToRetry`, `MaxRetries` floor=1, non-idempotent writes can execute >1 time), `Timeout` is per-attempt not total, logging hooks, base-URL and server-node overrides |
| **`dotnet-testing`** | HttpClient test seam, mocking patterns for SDK operations, assertion style alignment with project conventions |

---

**Source reference:** `sdk-map.md` + `map/operations/{Customers,Products,Subscriptions}.md` + `map/models/{records-2-Cr-Ne.md, records-3-Of-Su.md, enums.md}` (Maxio Advanced Billing .NET SDK, v1.0.2, source commit `15db14b2e663ebe9e957e061bd67634630429035`)
