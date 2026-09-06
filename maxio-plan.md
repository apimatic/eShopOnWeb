# eShopOnWeb Subscription Billing with Maxio — Implementation Plan

## Scope

Add recurring-subscription billing capability to eShopOnWeb using Maxio Advanced Billing SDK. Expose three endpoints on PublicApi:
- `GET /api/subscription-plans` — list available plans
- `POST /api/subscriptions` — create a subscription for an authenticated user
- `GET /api/my-subscriptions` — list subscriptions for the authenticated user

---

## Architecture & Design

### Layering

- **ApplicationCore**: Domain models for subscription data (minimal — only what eShopOnWeb must persist locally)
- **Infrastructure**: Maxio client registration, configuration, and any repository interfaces
- **PublicApi**: HTTP endpoints (RESTful, JWT-authenticated, following existing endpoint conventions)

### Data Persistence Strategy

- **User ↔ Maxio Customer mapping**: Stored in-memory or in the in-memory database (survives per-run; no persistence across restarts per task constraints)
- **Subscriptions**: Retrieved on-demand from Maxio (not replicated locally — single source of truth is Maxio)
- **Plans**: Retrieved on-demand from Maxio (cached briefly in memory per request to avoid repeated calls)

### Idempotency & Customer Creation

- When a user attempts to subscribe, ensure a Maxio customer exists via `ListCustomers(q: email)` + `CreateCustomer` if absent
- Double-click protection: if customer already exists in Maxio, reuse their ID; if a subscription with the same handle already exists, return the existing subscription

---

## CONTRACT SHEET: Four Maxio Operations

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier.**

### Operation 1: List Subscription Plans

| Aspect | Details |
|--------|---------|
| **Controller** | `client.ProductFamilies` |
| **Method Signature** | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| **Input** | `productFamilyId` (string, required) — product family handle or numeric ID. Format: `"handle:eshop-subscribe"` or numeric ID. All other params nullable (pass `null` to skip). |
| **Returns** | `IReadOnlyList<ProductResponse>` — each wraps one `Product` with: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Price (price_in_cents): long?` (in cents), `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `CreatedAt (created_at): DateTimeOffset?`, and others. |
| **Error** | **Case B — RawError** (`SdkException<RawError>`). Accessors: `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. |
| **Pagination** | Manual; `page` (default 1) and `perPage` (default 20). |
| **Notes** | Response items are `ProductResponse` wrapping an inner `Product` — must unwrap to access fields. |

### Operation 2A: Create Customer

| Aspect | Details |
|--------|---------|
| **Controller** | `client.Customers` |
| **Method Signature** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` |
| **Request** | `CreateCustomerRequest` model. Required fields: `FirstName`, `LastName`, `Email`. Optional: `Organization`, `Reference`, etc. |
| **Wire Format** | Request envelope: `customer` ← body. Inner fields: `first_name`, `last_name`, `email`, `organization`, `reference`, etc. |
| **Returns** | `CustomerResponse` — wraps `Customer` with: `Id (id): int?` ← **customer ID to store**, `FirstName`, `LastName`, `Email`, `CreatedAt`, `UpdatedAt`. |
| **Response Envelope** | `CustomerResponse.Customer` — unwrap one level; access `.Id`. |
| **Error** | **Case A — typed** (`SdkException<CreateCustomerError>`). Accessors: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422], `TryGetRawError(out RawError)` [other]. |
| **Notes** | Always unwrap `CustomerResponse.Customer` to get the `Customer` record; then read `.Id`. |

### Operation 2B: List Customers (Search by Email)

| Aspect | Details |
|--------|---------|
| **Controller** | `client.Customers` |
| **Method Signature** | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` |
| **Input** | `q` (string?, nullable) — search query (e.g., email address). All other params optional; defaults: `page`=1, `perPage`=50. |
| **Wire Format** | Query param: `q` ← `q`, `page` ← `page`, `per_page` ← `perPage`. |
| **Returns** | `IReadOnlyList<CustomerResponse>` — each wraps a `Customer` (same shape as CreateCustomer response). |
| **Error** | **Case B — RawError** (`SdkException<RawError>`). |
| **Pagination** | Manual; `page` and `perPage` (defaults 1, 50). |
| **Notes** | Use this to check if a customer with a given email already exists before calling CreateCustomer. |

### Operation 3: Create Subscription

| Aspect | Details |
|--------|---------|
| **Controller** | `client.Subscriptions` |
| **Method Signature** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` |
| **Request** | `CreateSubscriptionRequest` model. Key fields: `ProductId (product_id)` or `ProductHandle (product_handle)` (string, e.g., `"eshop-pro"`), `CustomerId (customer_id): int?`, or `CustomerReference (customer_reference): string?`, and 40+ optional fields (`Reference`, `NextBillingAt`, etc.). |
| **Wire Format** | Request envelope: `subscription` ← body. Inner: `product_id`, `product_handle`, `customer_id`, `customer_reference`, `reference`, `next_billing_at`, etc. |
| **Returns** | `SubscriptionResponse` — wraps `Subscription` with: `Id (id): int?` ← **subscription ID**, `State (state): SubscriptionState?`, `BalanceInCents (balance_in_cents): long?`, `ProductPriceInCents (product_price_in_cents): long?`, **`NextAssessmentAt (next_assessment_at): DateTimeOffset?`** ← **next billing date**, `CreatedAt`, `UpdatedAt`, `Customer`, `Product`. |
| **Response Envelope** | `SubscriptionResponse.Subscription` — unwrap one level; access `.Id` and `.NextAssessmentAt`. |
| **Error** | **Case A — typed** (`SdkException<CreateSubscriptionError>`). Accessors: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] (payload: `Errors: IReadOnlyList<string>`), `TryGetRawError(out RawError)` [other]. |
| **Notes** | Always unwrap `SubscriptionResponse.Subscription` to get the `Subscription` record. Set `ProductHandle` to the plan's handle (e.g., `"eshop-pro"`). Use `CustomerId` if available; use `CustomerReference` as a fallback or secondary key. |

### Operation 4: List Subscriptions

| Aspect | Details |
|--------|---------|
| **Controller** | `client.Subscriptions` |
| **Method Signature** | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| **Input** | `state` (SubscriptionStateFilter?) — filter by state (enum: `Active`, `Canceled`, `Expired`, `PastDue`, etc.). All others optional; defaults: `page`=1, `perPage`=20. |
| **Wire Format** | Query params: `state` ← `state`, `product` ← `product`, `page` ← `page`, `per_page` ← `perPage`, etc. |
| **Returns** | `IReadOnlyList<SubscriptionResponse>` — list of subscription responses (same `Subscription` shape as Operation 3). |
| **Error** | **Case B — RawError** (`SdkException<RawError>`). |
| **Pagination** | Manual; `page` and `perPage` (defaults 1, 20). |
| **Notes** | No built-in customer-ID filter; application must filter results client-side or use metadata. |

---

## Enums

| Enum | C# Members (literal) | Wire Value |
|------|-----|-----------|
| `SubscriptionStateFilter` | `Active`, `Canceled`, `Expired`, `ExpiredCards`, `OverdueBalance`, `PastDue`, `PendingCancellation`, `Suspended`, `TrialEnded`, `Trialing`, `Unpaid` | lowercase + underscore |
| `SubscriptionState` | (same as above, used in response) | lowercase + underscore |
| `IntervalUnit` | `Day`, `Month`, `Week`, `Year` | lowercase |
| `BasicDateField` | `CreatedAt`, `UpdatedAt` | lowercase + underscore |
| `SortingDirection` | `Asc`, `Desc` | lowercase |

---

## Namespaces (using-directives)

| Content | Namespace |
|---------|-----------|
| Client, responses, options | `MaxioAdvancedBilling` |
| Operations/controllers | `MaxioAdvancedBilling.Api` |
| Request/response models, records | `MaxioAdvancedBilling.Models` |
| Enums | `MaxioAdvancedBilling.Models.Enums` |
| Error classes | `MaxioAdvancedBilling.Errors` |

---

## Client Construction & Authentication

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;

var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials { Username = "<api_key>", Password = "x" },
    Environment = ServerEnvironment.Us, // or ServerEnvironment.Eu
    BaseUrl = "<optional_override>", // if Maxio:BaseUrl is set
};
var client = new MaxioAdvancedBillingClient(httpClient, options);
```

---

## Configuration Keys

- `Maxio:ApiKey` ← env var `MAXIO_API_KEY`
- `Maxio:Subdomain` ← env var `MAXIO_SITE_SUBDOMAIN`
- `Maxio:ProductFamilyHandle` ← env var `MAXIO_DEFAULT_PRODUCT_FAMILY`
- `Maxio:BaseUrl` (optional) ← env var or derived from subdomain if not set

---

## Assumptions & Blockers

1. **Database persistence**: Per task constraints, in-memory storage is acceptable for a demo. User ↔ Maxio Customer mapping will be stored in-memory (in-process) or via the in-memory EF provider.
2. **Payment method requirement**: Sandbox plans have `payment_method_required: false`, so subscriptions can be created without capturing a card. No 3-DS or payment UI needed.
3. **Subscription-to-customer correlation**: Maxio subscriptions reference customers by ID or reference string. Application will use customer ID (immutable within Maxio) as the primary key.
4. **No trial or setup fees**: Sandbox plans have no trial or setup fees; integration assumes this for simplicity.

---

## REQUIRED READING

Before implementation begins, load all the following companion skills:

1. **`dotnet-client-initialization`** — how to construct the client, HttpClient ownership, DI registration
2. **`dotnet-authentication`** — HTTP Basic auth (username = API key, password = `"x"`), credential loading
3. **`dotnet-calling-endpoints`** — operation signatures, named vs positional arguments, parameter passing, pagination
4. **`dotnet-models`** — request/response record construction, required fields, immutability, envelope unwrapping
5. **`dotnet-error-handling`** — exception types (Case A vs B), typed error accessors, JSON deserialization errors
6. **`dotnet-configuration-resilience`** — retries, timeouts, base-URL override, logging

All facts in the CONTRACT SHEET are authoritative. Do not re-derive or guess.

---

## Implementation Checklist

- [ ] Load all REQUIRED READING skills
- [ ] Add Maxio SDK NuGet package to PublicApi
- [ ] Configure Maxio settings (ApiKey, Subdomain, ProductFamilyHandle, BaseUrl)
- [ ] Register Maxio client in DI
- [ ] Create application-layer services (SubscriptionService)
- [ ] Implement subscription endpoints following existing PublicApi patterns
- [ ] Handle idempotency (customer creation, subscription creation)
- [ ] Error handling and validation
- [ ] Test endpoints (manual or via integration tests)
- [ ] Self-verify build and functionality

---

## Implementation Status

**Step 1c — Load Required Skills**: Pending
**Step 2 — Implement**: Pending

---
