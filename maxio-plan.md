# Maxio Advanced Billing Integration — eShopOnWeb

## Summary

Integrate three endpoints into eShopOnWeb PublicApi to surface Maxio subscription management:

1. **`GET /api/subscription-plans`** — list subscription products (plans) available from the Maxio sandbox: Pro ($299/mo), Basic ($29/mo), plus any metered components (api-call usage).
2. **`POST /api/subscriptions`** — create a subscription for the logged-in user, idempotently ensuring a Maxio customer record exists first (keyed by user's email as reference); return plan name, price, state, next billing date.
3. **`GET /api/my-subscriptions`** — list subscriptions for the logged-in user (lookup customer by email reference, then list their subscriptions).

All endpoints require JWT authentication (identity from token claims: sub/email). Configuration via environment variables maps to `Maxio` section binding.

## Scope & Sequence

1. **Client initialization** — wire `MaxioAdvancedBillingClient` via DI with HTTP retry/resilience
2. **Credentials** — set HTTP Basic auth (username = API key, password = `"x"`)
3. **Sandbox entities** — seeded product family `eshop-subscribe`, plans `eshop-pro` and `basic-plan`, component `api-call`
4. **Lookup or create customer** — by email reference (idempotent), return customer ID
5. **List products** — filter by family handle `eshop-subscribe`, return plan details (name, price, interval)
6. **Create subscription** — POST to Maxio with customer ID + product handle, return state/next billing
7. **List subscriptions** — GET customer's subscriptions, map to response DTOs

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### 1. CreateCustomer — Idempotent Lookup or Create

| Aspect | Value |
|--------|-------|
| **Controller** | `client.Customers` |
| **Signature** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` → `CustomerResponse` |
| **HTTP** | `POST /customers.json` |
| **Request Model** | `CreateCustomerRequest` wraps `CreateCustomer` |
| **CreateCustomer fields** (required: `FirstName !req`, `LastName !req`, `Email !req`) | `Reference (reference): string?`, `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Organization (organization): string?`, `Address (address): string?`, `Address2 (address_2): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `Locale (locale): string?`, `VatNumber (vat_number): string?`, `TaxExempt (tax_exempt): bool?`, `TaxExemptReason (tax_exempt_reason): string?`, `ParentId (parent_id): int?`, `SalesforceId (salesforce_id): string?`, `CcEmails (cc_emails): string?` |
| **Response Envelope** | `CustomerResponse` → `.Customer: Customer !req` |
| **Customer key fields** | `.Id (id): int?`, `.FirstName (first_name): string?`, `.LastName (last_name): string?`, `.Email (email): string?`, `.Reference (reference): string?`, `.CreatedAt (created_at): DateTimeOffset?`, `.UpdatedAt (updated_at): DateTimeOffset?` |
| **Error** | `SdkException<CreateCustomerError>` — **Case A (typed)** |
| **Error accessors** | `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| **Pagination** | none |
| **Notes** | To create idempotently by user email: call `ReadCustomerByReference(reference: userEmail)` first (Case B error, 404 → not found); if not found, call `CreateCustomer` with `Reference = userEmail`. Set first/last/email from user claims. |
| **Source** | `operations/Customers.md` |

### 2. ReadCustomerByReference — Lookup by Email

| Aspect | Value |
|--------|-------|
| **Controller** | `client.Customers` |
| **Signature** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` → `CustomerResponse` |
| **HTTP** | `GET /customers/lookup.json` (query: `reference=<email>`) |
| **Query param** | `reference` ← `reference` |
| **Response Envelope** | `CustomerResponse` → `.Customer: Customer !req` |
| **Error** | `SdkException<RawError>` — **Case B** |
| **Error accessors** | `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?` |
| **Pagination** | none |
| **Notes** | Returns 404 (StatusCode) if customer reference not found; no typed error payload. Use to check customer existence before create. |
| **Source** | `operations/Customers.md` |

### 3. ListProducts — List Subscription Plans

| Aspect | Value |
|--------|-------|
| **Controller** | `client.Products` |
| **Signature** | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` → `IReadOnlyList<ProductResponse>` |
| **HTTP** | `GET /products.json` (manual pagination: `page`, `per_page`) |
| **Query params** | `page` ← `page`, `per_page` ← `perPage`, [others optional] |
| **Response Envelope** | Array of `ProductResponse` — each → `.Product: Product !req` |
| **Product key fields** | `.Id (id): int?`, `.Name (name): string?`, `.Handle (handle): string?`, `.PriceInCents (price_in_cents): long?`, `.Interval (interval): int?`, `.IntervalUnit (interval_unit): IntervalUnit?`, `.TrialPriceInCents (trial_price_in_cents): long?`, `.Description (description): string?` |
| **Error** | `SdkException<RawError>` — **Case B** |
| **Error accessors** | `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` |
| **Pagination** | manual `page` + `perPage` (defaults: 1, 20) |
| **Notes** | To list only plans in product family `eshop-subscribe`: pass `filter: new ListProductsFilter { }` (filter is optional; no field on it for family, so use server-side configuration or list all and client-filter by product family ID). Sandbox has stable product family ID; products belong by `ProductFamilyId`. Align on which products to surface: map product handles to user-facing plan names. |
| **Source** | `operations/Products.md` |

### 4. CreateSubscription — Enroll User in Plan

| Aspect | Value |
|--------|-------|
| **Controller** | `client.Subscriptions` |
| **Signature** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` → `SubscriptionResponse` |
| **HTTP** | `POST /subscriptions.json` |
| **Request Model** | `CreateSubscriptionRequest` wraps `CreateSubscription` |
| **CreateSubscription key fields** (to set) | `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?`, `ProductPricePointId (product_price_point_id): int?`, `CustomerId (customer_id): int?`, `CustomerReference (customer_reference): string?`, `Reference (reference): string?`, `PaymentProfileId (payment_profile_id): int?`, `CustomerAttributes (customer_attributes): CustomerAttributes?`, `DeferSignup (defer_signup): bool? = false`, [plus 30+ optional] |
| **Response Envelope** | `SubscriptionResponse` → `.Subscription: Subscription?` |
| **Subscription key fields** | `.Id (id): int?`, `.State (state): SubscriptionState?`, `.CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `.NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `.ActivatedAt (activated_at): DateTimeOffset?`, `.CanceledAt (canceled_at): DateTimeOffset?`, `.CouponCode (coupon_code): string?`, `.Customer (customer): Customer?`, `.Product (product): Product?` |
| **Error** | `SdkException<CreateSubscriptionError>` — **Case A (typed)** |
| **Error accessors** | `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| **Pagination** | none |
| **Notes** | Pass `CustomerId` (from CreateCustomer) + `ProductHandle` (from ListProducts). Omit payment profile (sandbox does not require payment method). Set `DeferSignup: false` (or omit) to activate immediately. Response wraps subscription in `.Subscription` field; unwrap to access `.State`, `.NextAssessmentAt` for response DTO. `State` enum is `SubscriptionState` (`Active`, `Trialing`, `Paused`, `Canceled`, `Expired`, …). |
| **Source** | `operations/Subscriptions.md` |

### 5. ListSubscriptions — List User's Active Subscriptions

| Aspect | Value |
|--------|-------|
| **Controller** | `client.Subscriptions` |
| **Signature** | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` → `IReadOnlyList<SubscriptionResponse>` |
| **HTTP** | `GET /subscriptions.json` (manual pagination: `page`, `per_page`) |
| **Query params** | `page` ← `page`, `per_page` ← `perPage`, `state` ← `state`, `product` ← `product`, [others optional] |
| **Response Envelope** | Array of `SubscriptionResponse` — each → `.Subscription: Subscription?` |
| **Subscription key fields** | (same as CreateSubscription response) `.Id`, `.State`, `.CurrentPeriodEndsAt`, `.NextAssessmentAt`, `.Customer`, `.Product` |
| **Error** | `SdkException<RawError>` — **Case B** |
| **Error accessors** | `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` |
| **Pagination** | manual `page` + `perPage` (defaults: 1, 20) |
| **Notes** | To list subscriptions for a specific customer: pass `product: <productId>` or use app-side filtering (fetch all, filter locally by customer reference or ID). **No built-in customer_id parameter**; Maxio API does not filter by customer directly on this endpoint. Instead, call `client.Customers.ListCustomerSubscriptions(customerId)` (below) to get subscriptions for a known customer. Optional: `state: SubscriptionStateFilter.Active` to filter active only. Omit `state` to get all states. |
| **Source** | `operations/Subscriptions.md` |

### 6. ListCustomerSubscriptions — List Subscriptions for a Customer

| Aspect | Value |
|--------|-------|
| **Controller** | `client.Customers` |
| **Signature** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` → `IReadOnlyList<SubscriptionResponse>` |
| **HTTP** | `GET /customers/{customer_id}/subscriptions.json` |
| **URL param** | `customer_id` ← `customerId` |
| **Response Envelope** | Array of `SubscriptionResponse` — each → `.Subscription: Subscription?` |
| **Error** | `SdkException<RawError>` — **Case B** |
| **Error accessors** | `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` |
| **Pagination** | none (no pagination on this endpoint) |
| **Notes** | Preferred way to fetch subscriptions for a user: call after lookup/create customer by email. Returns full subscription array without pagination. |
| **Source** | `operations/Customers.md` |

---

## Enum Values (In Scope)

### SubscriptionState

Values (wire names — wire names match member names in this enum):

| Value | Use |
|-------|-----|
| `Active` | subscription is active and billing |
| `Trialing` | trial period active |
| `Paused` | subscription paused |
| `Canceled` | subscription canceled |
| `Expired` | subscription expired |
| `Pending` | awaiting payment/activation |
| `Past Due` | overdue payment |

Read from response `.State` as `SubscriptionState` enum (namespace `MaxioAdvancedBilling.Models.Enums`).

### SubscriptionStateFilter

Enum for filtering on list. Values: `Active`, `Trialing`, `Canceled`, `Paused`, `Expired`, `Pending`, `Past Due`. Use when calling `ListSubscriptions(state: SubscriptionStateFilter.Active, ...)`.

### IntervalUnit

Enum for subscription interval unit. Values: `Day`, `Week`, `Month`, `Year`. Read from `Product.IntervalUnit` and `Subscription.Product.IntervalUnit`.

Namespace: `MaxioAdvancedBilling.Models.Enums`.

---

## Client Construction & Configuration

**DI Registration** (recommended):

```csharp
services.AddMaxioAdvancedBillingClient(options =>
{
    var config = configuration.GetSection("Maxio");
    var apiKey = config["ApiKey"] ?? throw new InvalidOperationException("MAXIO_API_KEY not set");
    var subdomain = config["Subdomain"] ?? throw new InvalidOperationException("MAXIO_SITE_SUBDOMAIN not set");
    var environment = (config["Environment"] ?? "sandbox").ToLower() == "production"
        ? ServerEnvironment.Us
        : ServerEnvironment.Us; // sandbox also uses US (subdomain routes to sandbox subdomain)
    
    options.BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" };
    options.Environment = environment;
    
    // Optional: override base URL if using mock/dev host
    if (!string.IsNullOrEmpty(config["BaseUrl"]))
        options.Server.Production.Us.BaseUrl = config["BaseUrl"];
    
    // Optional: override subdomain (default matches "Subdomain" config)
    if (!string.IsNullOrEmpty(subdomain))
        options.Server.Production.Us.Site = subdomain;
});
```

**Configuration Binding** (`appsettings.json`):

```json
{
  "Maxio": {
    "ApiKey": "your_api_key_here",
    "Subdomain": "your-site-subdomain",
    "Environment": "sandbox",
    "BaseUrl": null,
    "ProductFamilyHandle": "eshop-subscribe"
  }
}
```

**Environment Variables** (recommended for secrets):

- `MAXIO_API_KEY` → binds to `Maxio:ApiKey`
- `MAXIO_SITE_SUBDOMAIN` → binds to `Maxio:Subdomain`
- `MAXIO_ENVIRONMENT` → binds to `Maxio:Environment` (default: "sandbox")
- `MAXIO_DEFAULT_PRODUCT_FAMILY` → binds to `Maxio:ProductFamilyHandle`
- Optional: `MAXIO_BASE_URL` → binds to `Maxio:BaseUrl` (for mock/dev override)

**Using** in endpoints:

```csharp
public async Task<IResult> HandleAsync(
    HttpContext httpContext,
    MaxioAdvancedBillingClient maxioClient,
    IConfiguration configuration,
    CancellationToken ct)
{
    var productFamilyHandle = configuration["Maxio:ProductFamilyHandle"] ?? "eshop-subscribe";
    var subscriptions = await maxioClient.Subscriptions.ListSubscriptions(
        state: SubscriptionStateFilter.Active,
        page: 1,
        perPage: 20,
        ct: ct);
    // ...
}
```

---

## Response Envelopes & Unwrapping

All operations return wrapped envelopes; unwrap before use:

| Operation | Response Type | Unwrap |
|-----------|---------------|--------|
| CreateCustomer | `CustomerResponse` | `.Customer` → `Customer` |
| ReadCustomerByReference | `CustomerResponse` | `.Customer` → `Customer` |
| ListProducts | `IReadOnlyList<ProductResponse>` | iterate, unwrap each `.Product` |
| CreateSubscription | `SubscriptionResponse` | `.Subscription` → `Subscription` |
| ListSubscriptions | `IReadOnlyList<SubscriptionResponse>` | iterate, unwrap each `.Subscription` |
| ListCustomerSubscriptions | `IReadOnlyList<SubscriptionResponse>` | iterate, unwrap each `.Subscription` |

**Careful**: response fields are nullable (e.g. `Subscription?`). Guard before access:

```csharp
var resp = await client.Subscriptions.CreateSubscription(body, ct);
if (resp.Subscription == null) throw new Exception("Subscription not created");
var state = resp.Subscription.State; // now safe
```

---

## Data Model for Subscriptions

**App persistence schema** (recommended minimum):

| Field | Type | Purpose |
|-------|------|---------|
| `MaxioCustomerId` | `int` | Maxio customer ID (from CreateCustomer response `.Id`) |
| `MaxioSubscriptionId` | `int` | Maxio subscription ID (from CreateSubscription response `.Id`) |
| `UserEmail` | `string` | User email (reference key for idempotent lookup) |
| `ProductHandle` | `string` | Maxio product handle (e.g. "eshop-pro") |
| `SubscriptionState` | `string` | Last known subscription state (Active, Canceled, etc.) |
| `NextBillingDate` | `DateTimeOffset?` | Next assessment date (`.NextAssessmentAt`) |
| `CreatedAt` | `DateTimeOffset` | When subscription was created in Maxio |
| `UpdatedAt` | `DateTimeOffset` | When subscription was last updated locally |

**Sync strategy**: on `GET /api/my-subscriptions`, call `ListCustomerSubscriptions(customerId)`, map to DTOs; optionally sync state/billing date to local DB for audit/display (does not drive billing, which is server-authoritative on Maxio).

---

## Assumptions & Blockers

1. **No payment method required in sandbox.** Real production may require credit card; sandbox creation succeeds without one. Plan assumes sandbox testing only; production integration will need payment profile handling.

2. **Idempotent customer creation by reference.** Plan assumes app uses email as `Reference` to enable lookup before create. This is application design, not SDK; map it in endpoint logic.

3. **Product family filter.** API has no built-in filter for product family on `ListProducts`. Plan assumes app wires product family handle from config and filters locally or relies on configured site to surface only family products.

4. **No async refreshing of subscription state.** Maxio webhooks can notify on state changes; plan does not include webhook listener. State is fetched on-demand via `ListCustomerSubscriptions` or `ReadSubscription`.

5. **Trial, setup fee, metered components not in scope.** Sandbox has basic products and metered `api-call` component; plan focuses on core create/list flow. Usage tracking for metered components is separate from subscription creation.

6. **Identity source.** Plan assumes JWT token carries `sub` (or `email_verified` claim) for user identity. App must map token claims to email for customer reference lookup. This is out of scope for SDK but blocks endpoint design.

---

## REQUIRED READING

**Before implementation starts, load these companion skills (in order).** The sheet deliberately does not carry their contents; each one is required to resolve gotchas the SDK signature does not show:

| Skill | Governs | Why Now |
|-------|---------|---------|
| `dotnet-client-initialization` | Client & DI setup | HttpClient lifetime, transience, constructor shape, DI registration pattern |
| `dotnet-authentication` | Credentials & auth scheme | HTTP Basic: username/password encoding, when to set before client construction, credential rotation |
| `dotnet-calling-endpoints` | Calling operations, named args | Required vs optional params, which params have no C# default, positional vs named call safety, cancellation token |
| `dotnet-models` | Request/response models, unions, enums | Immutable records, `required` init-only fields, unions (factory methods + `TryGet…` accessors), `StringEnum<T>` (not C# enum) |
| `dotnet-error-handling` | Exception boundary, error types | Case A vs Case B, `TryGet…` accessors, `SdkException<T>` unwrapping, JsonException from 2xx deserialization vs 4xx error parsing |
| `dotnet-configuration-resilience` | Retries, timeouts, base URL, logging | HttpMethodsToRetry gates status only (not transport), non-idempotent writes can retry on transport failure, Timeout is per-attempt, no built-in logging hook |
| `dotnet-testing` | Stubbing the SDK, test seams | HttpClient constructor is seam point, match project's assertion framework |

**Two hazards that belong in EVERY exception boundary** — load `dotnet-error-handling` before writing the catch ladder:

- A drifted or malformed **2xx body** (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx body** that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.
