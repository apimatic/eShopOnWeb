# Maxio Advanced Billing Integration — Contract Sheet

> **Signatures are generated code, verbatim — every parameter name is the literal
> C# identifier. The cancellation-token parameter really is named `ct`: in named
> arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take
> each one from that type's own map row, never from where a neighbouring type sits. A members
> table names the namespace outright; otherwise the row's source path implies it
> (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root
> namespace). Enums, unions, auth, server and client-config types are spread across different
> child namespaces, and two types configured side by side in the same options object routinely
> live in different ones. Dropping a type to the root or to `.Models` makes the implementer
> guess the wrong `using`, and the build breaks.

---

## 1. Scope & Sequence

| Step | Operations | Description |
|------|-----------|-------------|
| 1 | — | Install NuGet package and configure DI/client |
| 2 | `ListProducts` | List available subscription plans from Maxio |
| 3 | `CreateCustomer` | Create a customer (idempotent by reference) |
| 4 | `CreateSubscription` | Subscribe a user to a plan |
| 5 | `ListCustomerSubscriptions` | List the current user's subscriptions |

---

## 2. NuGet Package & Namespaces

```bash
dotnet add package AsadAli.AdvancedBilling.Sdk
```

**Required using directives** (add to files that reference these types):

```csharp
using MaxioAdvancedBilling;                              // Client, options, auth
using MaxioAdvancedBilling.Core.Authentication.Basic;     // BasicAuthCredentials
using MaxioAdvancedBilling.Core.Configuration;            // RetryOptions (if tuning)
using MaxioAdvancedBilling.Servers;                       // ServerEnvironment
using MaxioAdvancedBilling.Models;                        // All request/response records
using MaxioAdvancedBilling.Models.Enums;                  // All StringEnum/IntEnum types
using MaxioAdvancedBilling.Errors;                        // Typed error classes (Case A)
```

---

## 3. Client Construction & Auth

### Manual construction

```csharp
var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials { Username = "<api_key>", Password = "x" },
    Environment = ServerEnvironment.Us,  // or ServerEnvironment.Eu
};
var client = new MaxioAdvancedBillingClient(httpClient, options);
```

### DI registration

```csharp
services.AddMaxioAdvancedBillingClient(o =>
{
    o.BasicAuth = new BasicAuthCredentials { Username = "<api_key>", Password = "x" };
    o.Environment = ServerEnvironment.Us;
});
```

### Environment values

| Value | Hosting |
|-------|---------|
| `ServerEnvironment.Us` (default) | US-hosted: `https://{site}.chargify.com` |
| `ServerEnvironment.Eu` | EU-hosted: `https://{site}.ebilling.maxio.com` |

### Server/site override (if needed)

```csharp
options.Server.Production.Us.Site = "your-subdomain";
// or override BaseUrl for sandbox:
options.Server.Production.Us.BaseUrl = "http://localhost:8080";
```

**Source**: `sdk-map.md` lines 24–52, 199–224

---

## 4. Contract Sheet — Operations

### Operation 1: ListProducts

| Property | Value |
|----------|-------|
| **Controller** | `client.Products` |
| **HTTP** | `GET /products.json` |
| **Signature** | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| **Must-pass params** | `dateField`, `filter`, `endDate`, `endDatetime`, `startDate`, `startDatetime`, `includeArchived`, `include` — all nullable, no default → **must pass explicitly** (pass `null` to skip) |
| **Defaults** | `page = 1`, `perPage = 20` |
| **Returns** | `IReadOnlyList<ProductResponse>` |
| **Error type** | `SdkException<RawError>` — **Case B** |
| **Error accessors** | `StatusCode: HttpStatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()` |
| **Pagination** | Manual `page`+`perPage` |

**Response envelope**: `ProductResponse` has one field:
- `Product (product): Product !req` — the `Product` record

**Key `Product` fields** (namespace `MaxioAdvancedBilling.Models`):
- `Id (id): int?`
- `Name (name): string?`
- `Handle (handle): string?`
- `Description (description): string?`
- `PriceInCents (price_in_cents): long?` — price in cents (divide by 100 for dollars)
- `Interval (interval): int?`
- `IntervalUnit (interval_unit): IntervalUnit?` — enum: `Day`, `Month`
- `ProductFamily (product_family): ProductFamily?` — nested object with `Id`, `Name`, `Handle`
- `DefaultProductPricePointId (default_product_price_point_id): int?`

**Source**: `map/operations/Products.md` line 28–39, `map/models/records-3-Of-Su.md` lines 62, 70

---

### Operation 2: CreateCustomer

| Property | Value |
|----------|-------|
| **Controller** | `client.Customers` |
| **HTTP** | `POST /customers.json` |
| **Signature** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` |
| **Must-pass params** | `body` — nullable, no default → **must pass explicitly** |
| **Returns** | `CustomerResponse` |
| **Error type** | `SdkException<CreateCustomerError>` — **Case A (typed)** |
| **Error accessors** | `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| **Pagination** | none |

**Request model** — `CreateCustomerRequest` (namespace `MaxioAdvancedBilling.Models`):
- `Customer (customer): CreateCustomer !req`

**`CreateCustomer` fields** (all in `MaxioAdvancedBilling.Models`):
- `FirstName (first_name): string !req`
- `LastName (last_name): string !req`
- `Email (email): string !req`
- `CcEmails (cc_emails): string?`
- `Organization (organization): string?`
- `Reference (reference): string?` — **use this for idempotency** (unique per customer)
- `Address (address): string?`
- `Address2 (address_2): string?`
- `City (city): string?`
- `State (state): string?`
- `Zip (zip): string?`
- `Country (country): string?`
- `Phone (phone): string?`
- `Locale (locale): string?`
- `VatNumber (vat_number): string?`
- `TaxExempt (tax_exempt): bool?`
- `TaxExemptReason (tax_exempt_reason): string?`
- `ParentId (parent_id): int?`
- `SalesforceId (salesforce_id): string?`

**Response envelope**: `CustomerResponse` has one field:
- `Customer (customer): Customer !req`

**Key `Customer` fields**:
- `Id (id): int?` — Maxio's internal ID
- `FirstName`, `LastName`, `Email`, `Reference`, `Organization`

**Idempotency**: Use `Reference` field set to the application's user ID. If a customer with that reference already exists, the call will fail with 422. **Strategy**: Try `CreateCustomer` first; on 422 (use `TryGetCustomerErrorResponse1`), look up the existing customer via `ReadCustomerByReference(reference)`.

**Source**: `map/operations/Customers.md` lines 7–16, `map/models/records-1-Ac-Cr.md` lines 124–125, `map/models/records-2-Cr-Ne.md` line 43

---

### Operation 3: ReadCustomerByReference

| Property | Value |
|----------|-------|
| **Controller** | `client.Customers` |
| **HTTP** | `GET /customers/lookup.json` |
| **Signature** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` |
| **Returns** | `CustomerResponse` |
| **Error type** | `SdkException<RawError>` — **Case B** |
| **Pagination** | none |

**Query params**: `reference` ← `reference`

**Source**: `map/operations/Customers.md` lines 61–70

---

### Operation 4: CreateSubscription

| Property | Value |
|----------|-------|
| **Controller** | `client.Subscriptions` |
| **HTTP** | `POST /subscriptions.json` |
| **Signature** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` |
| **Must-pass params** | `body` — nullable, no default → **must pass explicitly** |
| **Returns** | `SubscriptionResponse` |
| **Error type** | `SdkException<CreateSubscriptionError>` — **Case A (typed)** |
| **Error accessors** | `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| **Pagination** | none |

**Request model** — `CreateSubscriptionRequest` (namespace `MaxioAdvancedBilling.Models`):
- `Subscription (subscription): CreateSubscription !req`

**`CreateSubscription` fields** (namespace `MaxioAdvancedBilling.Models`):
- `ProductHandle (product_handle): string?` — **use this** (pass `"eshop-pro"` or `"basic-plan"`)
- `ProductId (product_id): int?` — alternative to ProductHandle
- `ProductPricePointHandle (product_price_point_handle): string?`
- `ProductPricePointId (product_price_point_id): int?`
- `CustomerId (customer_id): int?` — Maxio's internal customer ID from CreateCustomer
- `CustomerReference (customer_reference): string?` — alternative: app's user ID
- `CouponCode (coupon_code): string?`
- `CouponCodes (coupon_codes): IReadOnlyList<string>?`
- `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` — enum: `Automatic`, `Remittance`, `Prepaid`, `Invoice`
- `Reference (reference): string?` — subscription reference for lookup
- `PaymentProfileId (payment_profile_id): int?`
- `NextBillingAt (next_billing_at): DateTimeOffset?`
- `InitialBillingAt (initial_billing_at): DateTimeOffset?`
- `CustomerAttributes (customer_attributes): CustomerAttributes?` — inline customer creation
- `Components (components): IReadOnlyList<CreateSubscriptionComponent>?`
- `Metafields (metafields): IReadOnlyDictionary<string, string>?`

**Response envelope**: `SubscriptionResponse` has one field:
- `Subscription (subscription): Subscription?` — **nullable** (may be null on 422)

**Key `Subscription` fields** (namespace `MaxioAdvancedBilling.Models`):
- `Id (id): int?`
- `State (state): SubscriptionState?` — enum: `Active`, `Pending`, `Trialing`, `Canceled`, `Expired`, etc.
- `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`
- `NextAssessmentAt (next_assessment_at): DateTimeOffset?`
- `ActivatedAt (activated_at): DateTimeOffset?`
- `CreatedAt (created_at): DateTimeOffset?`
- `Product (product): Product?` — nested product info
- `Customer (customer): Customer?` — nested customer info
- `BalanceInCents (balance_in_cents): long?`
- `TotalRevenueInCents (total_revenue_in_cents): long?`
- `ProductPriceInCents (product_price_in_cents): long?`
- `CancelAtEndOfPeriod (cancel_at_end_of_period): bool?`
- `CouponCode (coupon_code): string?`

**Source**: `map/operations/Subscriptions.md` lines 31–40, `map/models/records-2-Cr-Ne.md` lines 17, 21

---

### Operation 5: ListCustomerSubscriptions

| Property | Value |
|----------|-------|
| **Controller** | `client.Customers` |
| **HTTP** | `GET /customers/{customer_id}/subscriptions.json` |
| **Signature** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` |
| **Returns** | `IReadOnlyList<SubscriptionResponse>` |
| **Error type** | `SdkException<RawError>` — **Case B** |
| **Pagination** | none (returns all for that customer) |

**Source**: `map/operations/Customers.md` lines 28–36

---

### Operation 6 (backup): ListSubscriptions

| Property | Value |
|----------|-------|
| **Controller** | `client.Subscriptions` |
| **HTTP** | `GET /subscriptions.json` |
| **Signature** | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| **Must-pass params** | 14 params (`state` … `include`) — all nullable, no default → **must pass explicitly** (pass `null` to skip) |
| **Defaults** | `page = 1`, `perPage = 20` |
| **Returns** | `IReadOnlyList<SubscriptionResponse>` |
| **Error type** | `SdkException<RawError>` — **Case B** |
| **Pagination** | Manual `page`+`perPage` |

**Source**: `map/operations/Subscriptions.md` lines 54–65

---

### Operation 7 (backup): ReadProductByHandle

| Property | Value |
|----------|-------|
| **Controller** | `client.Products` |
| **HTTP** | `GET /products/handle/{api_handle}.json` |
| **Signature** | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` |
| **Returns** | `ProductResponse` |
| **Error type** | `SdkException<RawError>` — **Case B** |

Use this to look up a single product by handle (e.g. `"eshop-pro"` or `"basic-plan"`) if you don't want to list all products.

**Source**: `map/operations/Products.md` lines 51–59

---

## 5. Error Handling Reference

### Case A operations (typed errors)

| Operation | Error Type | Typed Accessor | HTTP Status |
|-----------|-----------|----------------|-------------|
| `CreateCustomer` | `SdkException<CreateCustomerError>` | `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` | 422 |
| `CreateSubscription` | `SdkException<CreateSubscriptionError>` | `TryGetErrorListResponse1(out ErrorListResponse1)` | 422 |

### Case B operations (raw errors)

| Operation | Error Type |
|-----------|-----------|
| `ListProducts` | `SdkException<RawError>` |
| `ReadCustomerByReference` | `SdkException<RawError>` |
| `ListCustomerSubscriptions` | `SdkException<RawError>` |
| `ListSubscriptions` | `SdkException<RawError>` |
| `ReadProductByHandle` | `SdkException<RawError>` |

### Case A catch pattern

```csharp
try { /* operation */ }
catch (SdkException<CreateCustomerError> ex)
{
    if (ex.Error.TryGetCustomerErrorResponse1(out var err422))
    {
        // err422.Errors contains validation details
    }
    else if (ex.Error.TryGetRawError(out var raw))
    {
        // fallback: raw.StatusCode, raw.ReadAsString()
    }
}
```

### Case B catch pattern

```csharp
try { /* operation */ }
catch (SdkException<RawError> ex)
{
    var status = ex.Error.StatusCode;
    var body = ex.Error.ReadAsString();
}
```

### `System.Text.Json.JsonException` caveat

A drifted or malformed **2xx** body surfaces as `JsonException` from deserialization, **not** as `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. A **non-2xx** body that does not match its generated error shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it.

**MUST load `dotnet-error-handling`** before writing that boundary.

**Source**: `sdk-map.md` lines 81–118

---

## 6. Key Enums Needed

All in namespace `MaxioAdvancedBilling.Models.Enums`. These are `StringEnum<T>` records — use static members like `IntervalUnit.Month`, not wire values.

| Enum | Values Used |
|------|-------------|
| `IntervalUnit` | `Day`, `Month` |
| `CollectionMethod` | `Automatic`, `Remittance`, `Prepaid`, `Invoice` |
| `SubscriptionState` | `Active`, `Pending`, `Trialing`, `Canceled`, `Expired`, `PastDue`, `Suspended`, `Paused` |
| `BasicDateField` | `UpdatedAt`, `CreatedAt` |
| `SortingDirection` | `Asc`, `Desc` |

**Source**: `map/models/enums.md` lines 15, 21, 47, 85, 96

---

## 7. Implementation Pseudocode Flow

```
Step 1: Install package, register DI with env-var credentials
Step 2: ListProducts → filter by ProductFamily handle "eshop-subscribe" → display plans
Step 3: CreateCustomer (Reference = app user ID) → on 422, ReadCustomerByReference to get existing
Step 4: CreateSubscription (CustomerReference = app user ID, ProductHandle = plan handle)
Step 5: ListCustomerSubscriptions (CustomerId from step 3/4) → display user's subscriptions
```

---

## 8. Trap Notes

⚠ **Step 1 (client registration)** — The SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register. `MaxRetries = 0` is rejected at construction; the floor is 1. Transport failures (`HttpRequestException`) are retried on **every** verb including `POST`, so a non-idempotent write can execute more than once and no setting disables that. **MUST load `dotnet-configuration-resilience`** before wiring the client.

⚠ **Step 1 (client registration)** — The `HttpClient`/handler pipeline must be long-lived and reused via `IHttpClientFactory`, not rebuilt per request; the SDK client wrapper over it may be transient. **MUST load `dotnet-client-initialization`** before writing `new MaxioAdvancedBillingClient(...)`.

⚠ **Step 2 (ListProducts)** — This is Case B (`SdkException<RawError>`). The 8 nullable params before `page` have no C# default — mis-binding in a positional call is likely. Use named arguments. **MUST load `dotnet-calling-endpoints`**.

⚠ **Step 3 (CreateCustomer)** — Idempotency requires checking for an existing customer. On 422, `TryGetCustomerErrorResponse1` tells you the customer already exists with that reference; then call `ReadCustomerByReference` to get the ID. **MUST load `dotnet-error-handling`** for the 422-vs-other-status distinction.

⚠ **Step 3 (CreateCustomer)** — The `Reference` field is the idempotency key. If omitted, you cannot detect duplicates. **MUST load `dotnet-models`** for required-member handling.

⚠ **Step 4 (CreateSubscription)** — `ProductHandle` is the simplest way to specify the plan (use `"eshop-pro"` or `"basic-plan"`). `CustomerReference` lets you reference the customer by app ID instead of Maxio's internal ID. **MUST load `dotnet-calling-endpoints`** for request-body construction.

⚠ **Step 4 (CreateSubscription)** — `CreateSubscription` is Case A. On 422, check `TryGetErrorListResponse1` for validation errors. The response may have a null `Subscription` even on success (e.g. deferred signup). **MUST load `dotnet-error-handling`**.

⚠ **Steps 2–5 (all calls)** — Enums are `StringEnum<T>` records, not C# enums. Build via `IntervalUnit.Month` (static member) or `IntervalUnit.FromValue("month")`. **MUST load `dotnet-models`**.

---

## 9. REQUIRED READING

The following `dotnet-*` companion skills **must be loaded before implementation starts**. The sheet deliberately does not carry their contents.

| Skill | Governs |
|-------|---------|
| `dotnet-client-initialization` | Step 1: Client construction, HttpClient lifetime, DI registration |
| `dotnet-configuration-resilience` | Step 1: Retry semantics, timeout bounds, server-node config |
| `dotnet-calling-endpoints` | Steps 2–5: Named arguments, optional param passing, async patterns |
| `dotnet-models` | Steps 2–5: Request construction, required members, StringEnum usage |
| `dotnet-error-handling` | Steps 2–5: Case A vs Case B catch ladders, JsonException caveat |
| `dotnet-authentication` | Step 1: Basic auth credential wiring |

### `System.Text.Json.JsonException` — two opposite directions

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary. These rows belong in the FIRST sheet, not a later revision.

---

## 10. Assumptions & Blockers

- **Assumption**: The Maxio sandbox is seeded with the product handles `eshop-pro` and `basic-plan` under the product family `eshop-subscribe`.
- **Assumption**: The application will use `Reference` (the app's user ID) as the idempotency key for customer creation.
- **Assumption**: Subscriptions are created with `Automatic` collection method (credit card on file).
- **Assumption**: The metered component `api-call` is not part of the initial subscription creation scope (usage reporting can be added later).
- **Blocker**: None identified. All operations needed are available in the SDK.
