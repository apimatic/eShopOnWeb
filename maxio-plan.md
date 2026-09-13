# Maxio Advanced Billing Integration Plan — eShopOnWeb

## Scope & Sequence

| Step | Description | SDK Operations Used |
|------|-------------|---------------------|
| 1 | Client & DI setup | — |
| 2 | Auth configuration | — |
| 3 | List available plans (products) | `client.Products.ListProducts` |
| 4 | Create or find customer | `client.Customers.ReadCustomerByReference`, `client.Customers.CreateCustomer` |
| 5 | Subscribe user to a plan | `client.Subscriptions.CreateSubscription` |
| 6 | List user's subscriptions | `client.Customers.ListCustomerSubscriptions` |
| 7 | API endpoint wiring (controller endpoints) | Steps 3–6 composed |

---

## CONTRACT SHEET

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

### Operation 1 — ListProducts (list available plans)

| | |
|---|---|
| **Accessor** | `client.Products` |
| **Method** | `ListProducts` |
| **Signature** | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| **Params (must-pass)** | 8 nullable params (`dateField` … `include`) — pass `null` to skip; `page` and `perPage` have defaults |
| **Returns** | `IReadOnlyList<ProductResponse>` |
| **Error** | `SdkException<RawError>` — **Case B** |
| **Error accessors** | `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` |
| **Pagination** | Manual `page` + `perPage` |
| **Source** | `map/operations/Products.md` |

**Response envelope — `ProductResponse`** (`MaxioAdvancedBilling.Models`):

| Field | Wire name | Type | Required |
|-------|-----------|------|----------|
| `Product` | `product` | `Product` | **yes** |

**Inner `Product` record** (`MaxioAdvancedBilling.Models`) — key fields for the integration:

| C# Field | Wire name | Type | Required |
|----------|-----------|------|----------|
| `Id` | `id` | `int?` | — |
| `Name` | `name` | `string?` | — |
| `Handle` | `handle` | `string?` | — |
| `Description` | `description` | `string?` | — |
| `PriceInCents` | `price_in_cents` | `long?` | — |
| `Interval` | `interval` | `int?` | — |
| `IntervalUnit` | `interval_unit` | `IntervalUnit?` | — |
| `ProductFamily` | `product_family` | `ProductFamily?` | — |
| `DefaultProductPricePointId` | `default_product_price_point_id` | `int?` | — |

**Source**: `map/models/records-3-Of-Su.md` (ProductResponse), `map/models/records-3-Of-Su.md` (Product)

---

### Operation 2 — CreateCustomer (ensure customer exists)

| | |
|---|---|
| **Accessor** | `client.Customers` |
| **Method** | `CreateCustomer` |
| **Signature** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` |
| **Params (must-pass)** | `body` — nullable, no default → **must pass explicitly** |
| **Returns** | `CustomerResponse` |
| **Error** | `SdkException<CreateCustomerError>` — **Case A (typed)** |
| **Error accessors** | `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| **Source** | `map/operations/Customers.md` |

**Request model — `CreateCustomerRequest`** (`MaxioAdvancedBilling.Models`):

| Field | Wire name | Type | Required |
|-------|-----------|------|----------|
| `Customer` | `customer` | `CreateCustomer` | **yes** |

**Inner `CreateCustomer` record** (`MaxioAdvancedBilling.Models`):

| C# Field | Wire name | Type | Required |
|----------|-----------|------|----------|
| `FirstName` | `first_name` | `string` | **yes** |
| `LastName` | `last_name` | `string` | **yes** |
| `Email` | `email` | `string` | **yes** |
| `Reference` | `reference` | `string?` | — |
| `Organization` | `organization` | `string?` | — |
| `Phone` | `phone` | `string?` | — |
| `Address` | `address` | `string?` | — |
| `Address2` | `address_2` | `string?` | — |
| `City` | `city` | `string?` | — |
| `State` | `state` | `string?` | — |
| `Zip` | `zip` | `string?` | — |
| `Country` | `country` | `string?` | — |
| `Locale` | `locale` | `string?` | — |
| `TaxExempt` | `tax_exempt` | `bool?` | — |
| `VatNumber` | `vat_number` | `string?` | — |

**Source**: `map/models/records-1-Ac-Cr.md` (CreateCustomerRequest, CreateCustomer)

---

### Operation 3 — ReadCustomerByReference (find existing customer)

| | |
|---|---|
| **Accessor** | `client.Customers` |
| **Method** | `ReadCustomerByReference` |
| **Signature** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` |
| **Query params** | `reference` ← `reference` |
| **Returns** | `CustomerResponse` |
| **Error** | `SdkException<RawError>` — **Case B** |
| **Error accessors** | `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` |
| **Source** | `map/operations/Customers.md` |

**Response envelope — `CustomerResponse`** (`MaxioAdvancedBilling.Models`):

| Field | Wire name | Type | Required |
|-------|-----------|------|----------|
| `Customer` | `customer` | `Customer` | **yes** |

**Inner `Customer` record** (`MaxioAdvancedBilling.Models`) — key fields:

| C# Field | Wire name | Type | Required |
|----------|-----------|------|----------|
| `Id` | `id` | `int?` | — |
| `FirstName` | `first_name` | `string?` | — |
| `LastName` | `last_name` | `string?` | — |
| `Email` | `email` | `string?` | — |
| `Reference` | `reference` | `string?` | — |

**Source**: `map/models/records-1-Ac-Cr.md` (CustomerResponse), `map/models/records-2-Cr-Ne.md` (Customer)

---

### Operation 4 — CreateSubscription (subscribe a user to a plan)

| | |
|---|---|
| **Accessor** | `client.Subscriptions` |
| **Method** | `CreateSubscription` |
| **Signature** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` |
| **Params (must-pass)** | `body` — nullable, no default → **must pass explicitly** |
| **Returns** | `SubscriptionResponse` |
| **Error** | `SdkException<CreateSubscriptionError>` — **Case A (typed)** |
| **Error accessors** | `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| **Source** | `map/operations/Subscriptions.md` |

**Request model — `CreateSubscriptionRequest`** (`MaxioAdvancedBilling.Models`):

| Field | Wire name | Type | Required |
|-------|-----------|------|----------|
| `Subscription` | `subscription` | `CreateSubscription` | **yes** |

**Inner `CreateSubscription` record** (`MaxioAdvancedBilling.Models`) — key fields for the integration:

| C# Field | Wire name | Type | Required |
|----------|-----------|------|----------|
| `ProductHandle` | `product_handle` | `string?` | — |
| `ProductId` | `product_id` | `int?` | — |
| `ProductPricePointHandle` | `product_price_point_handle` | `string?` | — |
| `ProductPricePointId` | `product_price_point_id` | `int?` | — |
| `CustomerId` | `customer_id` | `int?` | — |
| `CustomerReference` | `customer_reference` | `string?` | — |
| `Reference` | `reference` | `string?` | — |
| `PaymentProfileId` | `payment_profile_id` | `int?` | — |
| `CustomerAttributes` | `customer_attributes` | `CustomerAttributes?` | — |
| `PaymentProfileAttributes` | `payment_profile_attributes` | `PaymentProfileAttributes?` | — |
| `CouponCode` | `coupon_code` | `string?` | — |
| `CouponCodes` | `coupon_codes` | `IReadOnlyList<string>?` | — |
| `Components` | `components` | `IReadOnlyList<CreateSubscriptionComponent>?` | — |
| `Metafields` | `metafields` | `IReadOnlyDictionary<string, string>?` | — |
| `CustomPrice` | `custom_price` | `SubscriptionCustomPrice?` | — |
| `PaymentCollectionMethod` | `payment_collection_method` | `CollectionMethod?` | — |
| `ReceivesInvoiceEmails` | `receives_invoice_emails` | `string?` | — |
| `NetTerms` | `net_terms` | `string?` | — |
| `NextBillingAt` | `next_billing_at` | `DateTimeOffset?` | — |
| `InitialBillingAt` | `initial_billing_at` | `DateTimeOffset?` | — |

**Notes** (from the operation row): "Creates a Subscription for a customer and product. Specify the product with `product_id` or `product_handle`. Identify an existing customer with `customer_id` or `customer_reference`. Optionally, include an existing payment profile using `payment_profile_id`. To create a new customer, pass `customer_attributes`."

**Source**: `map/models/records-2-Cr-Ne.md` (CreateSubscriptionRequest, CreateSubscription)

---

### Operation 5 — ListCustomerSubscriptions (list a user's subscriptions)

| | |
|---|---|
| **Accessor** | `client.Customers` |
| **Method** | `ListCustomerSubscriptions` |
| **Signature** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` |
| **Returns** | `IReadOnlyList<SubscriptionResponse>` |
| **Error** | `SdkException<RawError>` — **Case B** |
| **Error accessors** | `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` |
| **Pagination** | None |
| **Source** | `map/operations/Customers.md` |

**Response envelope — `SubscriptionResponse`** (`MaxioAdvancedBilling.Models`):

| Field | Wire name | Type | Required |
|-------|-----------|------|----------|
| `Subscription` | `subscription` | `Subscription?` | — (nullable) |

**Inner `Subscription` record** (`MaxioAdvancedBilling.Models`) — key fields:

| C# Field | Wire name | Type | Required |
|----------|-----------|------|----------|
| `Id` | `id` | `int?` | — |
| `State` | `state` | `SubscriptionState?` | — |
| `CurrentPeriodEndsAt` | `current_period_ends_at` | `DateTimeOffset?` | — |
| `NextAssessmentAt` | `next_assessment_at` | `DateTimeOffset?` | — |
| `ActivatedAt` | `activated_at` | `DateTimeOffset?` | — |
| `CanceledAt` | `canceled_at` | `DateTimeOffset?` | — |
| `Product` | `product` | `Product?` | — |
| `Customer` | `customer` | `Customer?` | — |
| `BalanceInCents` | `balance_in_cents` | `long?` | — |
| `TotalRevenueInCents` | `total_revenue_in_cents` | `long?` | — |
| `ProductPriceInCents` | `product_price_in_cents` | `long?` | — |
| `CouponCodes` | `coupon_codes` | `IReadOnlyList<string>?` | — |

**Source**: `map/models/records-4-Su-We.md` (SubscriptionResponse), `map/models/records-3-Of-Su.md` (Subscription)

---

### Operation 6 — ListSubscriptions (list all subscriptions, optionally filtered)

| | |
|---|---|
| **Accessor** | `client.Subscriptions` |
| **Method** | `ListSubscriptions` |
| **Signature** | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| **Params (must-pass)** | 14 nullable params (`state` … `include`) — pass `null` to skip; `page` and `perPage` have defaults |
| **Returns** | `IReadOnlyList<SubscriptionResponse>` |
| **Error** | `SdkException<RawError>` — **Case B** |
| **Pagination** | Manual `page` + `perPage` |
| **Source** | `map/operations/Subscriptions.md` |

---

## Error Types Reference

| Operation | Error Type | Case | Accessors |
|-----------|------------|------|-----------|
| `ListProducts` | `SdkException<RawError>` | B | `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` |
| `CreateCustomer` | `SdkException<CreateCustomerError>` | A | `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] |
| `ReadCustomerByReference` | `SdkException<RawError>` | B | `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` |
| `CreateSubscription` | `SdkException<CreateSubscriptionError>` | A | `TryGetErrorListResponse1(out ErrorListResponse1)` [422] |
| `ListCustomerSubscriptions` | `SdkException<RawError>` | B | `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` |
| `ListSubscriptions` | `SdkException<RawError>` | B | `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` |

---

## Key Enums Needed

| Enum | Values (relevant subset) | Source |
|------|--------------------------|--------|
| `IntervalUnit` | `Day (day)`, `Month (month)` | `map/models/enums.md` |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Invoice (invoice)` | `map/models/enums.md` |
| `SubscriptionState` | `Active (active)`, `Canceled (canceled)`, `PastDue (past_due)`, `Trialing (trialing)`, `Pending (pending)`, `Expired (expired)` | `map/models/enums.md` |
| `SubscriptionStateFilter` | `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `PastDue (past_due)`, `Trialing (trialing)` (+ more) | `map/models/enums.md` |

**Enum construction**: These are `StringEnum<T>` — NOT C# enums. Construct with `Type.FromValue("wire")` or static members (e.g. `IntervalUnit.Month`).

---

## Client Construction & Auth

```csharp
// Required using directives:
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Errors;

// Client construction:
var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials { Username = "<api_key>", Password = "x" },
    Environment = ServerEnvironment.Us,
};
var client = new MaxioAdvancedBillingClient(httpClient, options);
```

**Properties available**: `client.Products`, `client.Customers`, `client.Subscriptions`, `client.SubscriptionStatus`.

**Source**: `sdk-map.md` (Getting a client, Servers & auth)

---

## Trap Notes

⚠ Step 1 (client registration) — the SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ Step 2 (auth) — credentials must be set before constructing the client or in the DI callback. The password is literally `"x"`, not a placeholder. **MUST load `dotnet-authentication`**.

⚠ Step 3 (list products) — `ListProducts` returns `IReadOnlyList<ProductResponse>`, not `IReadOnlyList<Product>`. You must unwrap `.Product` from each response item. **MUST load `dotnet-calling-endpoints`**.

⚠ Step 4 (create/find customer) — `ReadCustomerByReference` throws `SdkException<RawError>` (Case B) on 404 — there is no typed accessor; check `StatusCode` directly. **MUST load `dotnet-error-handling`**.

⚠ Step 5 (create subscription) — `CreateSubscriptionRequest` wraps `CreateSubscription` (which has **no required fields** — `product_handle` and `customer_id`/`customer_reference` are all nullable). The provider validates at runtime. If payment attributes are required by the product config, the call fails with 422. **MUST load `dotnet-models`** and **`dotnet-error-handling`**.

⚠ Step 6 (list customer subscriptions) — `ListCustomerSubscriptions` takes `customerId` (int), not a reference string. You must resolve the customer ID first from step 4. **MUST load `dotnet-calling-endpoints`**.

---

## REQUIRED READING

| Skill | Governs | Load before |
|-------|---------|-------------|
| `dotnet-client-initialization` | Step 1 — client construction & DI | Writing the client registration |
| `dotnet-authentication` | Step 2 — credential wiring | Setting auth credentials |
| `dotnet-calling-endpoints` | Steps 3–6 — calling SDK operations | First SDK call |
| `dotnet-models` | Steps 3–6 — building request payloads | Constructing `CreateSubscriptionRequest` |
| `dotnet-error-handling` | Steps 3–6 — error boundaries | Writing any try/catch |
| `dotnet-configuration-resilience` | Step 1 — retries, timeouts, base URL | Tuning the client |

**`System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:**
- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## Assumptions & Blockers

1. **YOUR CALL — not in the map**: The eShopOnWeb app uses ASP.NET Identity for user management. The mapping between the app's user ID and the Maxio customer `reference` is an application decision — use the app's user ID as the Maxio `reference` value.

2. **YOUR CALL — not in the map**: Payment profile handling (credit card tokenization) is out of scope for this plan — the sandbox uses `bogus` gateway for testing. For production, Maxio.js (Chargify.js) is required for PCI compliance.

3. **YOUR CALL — not in the map**: The metered component `api-call` ($0.01/unit) usage reporting is not part of the hero flow (browse → subscribe → view). It may be added in a later iteration.

4. **UNVERIFIED**: Whether `CreateSubscription` with `product_handle` + `customer_reference` (without a payment profile) will succeed in the sandbox depends on the product's `require_credit_card` setting. The plan assumes the product allows signup without payment info or that the sandbox gateway (`bogus`) accepts it. If it fails, the error response from `TryGetErrorListResponse1` will indicate what's missing.
