# Maxio Advanced Billing — eShopOnWeb Integration Plan

## 1. Scope & Sequence

| Step | Description | SDK Operations |
|------|-------------|----------------|
| 1 | Add NuGet package + configuration | — |
| 2 | Register SDK client in DI (singleton HttpClient, scoped client) | — |
| 3 | Create `MaxioOptions` config POCO + appsettings.json section | — |
| 4 | Create `MaxioService` (wraps SDK calls, idempotent customer creation) | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer` |
| 5 | `GET /api/subscription-plans` endpoint | `ProductFamilies.ListProductsForProductFamily` |
| 6 | `POST /api/subscriptions` endpoint | `Subscriptions.CreateSubscription` |
| 7 | `GET /api/my-subscriptions` endpoint | `Customers.ListCustomerSubscriptions` |
| 8 | Unit tests for `MaxioService` | — |

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal
> C# identifier. The cancellation-token parameter really is named `ct`: in named
> arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take
> each one from that type's own map row, never from where a neighbouring type sits.

### NuGet Package

| | |
|---|---|
| Package ID | `AsadAli.AdvancedBilling.Sdk` |
| Root namespace (using) | `MaxioAdvancedBilling` |
| Target | `netstandard2.0` (.NET Core 2.0+ / .NET 5–10+) |

```bash
dotnet add package AsadAli.AdvancedBilling.Sdk
```

### Client Construction

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;

var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials { Username = "<api_key>", Password = "x" },
    Environment = ServerEnvironment.Us, // or ServerEnvironment.Eu
};
var client = new MaxioAdvancedBillingClient(httpClient, options);
```

DI alternative:
```csharp
services.AddMaxioAdvancedBillingClient(o =>
{
    o.BasicAuth = new BasicAuthCredentials { Username = "<api_key>", Password = "x" };
    o.Environment = ServerEnvironment.Us;
});
```

All API groups are properties on `MaxioAdvancedBillingClient` (e.g. `client.Customers`, `client.Subscriptions`, `client.Products`, `client.ProductFamilies`).

### Server / Base-URL

| Environment | US base URL | EU base URL |
|---|---|---|
| Production | `https://{site}.chargify.com` | `https://{site}.ebilling.maxio.com` |

The `{site}` defaults to `options.Server.Production.Us.Site`. To override the base URL (e.g. for sandbox), set `options.Server.Production.Us.BaseUrl`.

### Auth

HTTP Basic only. **Username = API key, Password = literal `"x"`**.

```csharp
o.BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" };
```

### Namespaces (using-directives)

```csharp
using MaxioAdvancedBilling;                     // client, options, auth
using MaxioAdvancedBilling.Core.Authentication.Basic; // BasicAuthCredentials
using MaxioAdvancedBilling.Servers;              // ServerEnvironment
using MaxioAdvancedBilling.Models;               // records (CreateCustomer, CreateSubscription, etc.)
using MaxioAdvancedBilling.Models.Enums;          // StringEnum types (SubscriptionState, CollectionMethod, etc.)
using MaxioAdvancedBilling.Models.AnyOf;          // union types (if needed)
using MaxioAdvancedBilling.Errors;               // typed error classes (CreateCustomerError, etc.)
using MaxioAdvancedBilling.Core.Configuration;   // RetryOptions (if tuning retries)
```

---

### Operation: List Products for Product Family

`client.ProductFamilies.ListProductsForProductFamily(...)`

| | |
|---|---|
| HTTP | `GET /product_families/{product_family_id}/products.json` |
| Returns | `IReadOnlyList<ProductResponse>` |
| Error | **Case A (typed)** `SdkException<ListProductsForProductFamilyError>` |
| Error accessors | `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] |
| Pagination | manual `page`+`perPage` |
| Source | `map/operations/ProductFamilies.md` |

**Signature:**
```csharp
ListProductsForProductFamily(
    string productFamilyId,        // ← handle like "eshop-subscribe"
    BasicDateField? dateField,     // pass null
    ListProductsFilter? filter,    // pass null
    DateTimeOffset? startDate,     // pass null
    DateTimeOffset? endDate,       // pass null
    DateTimeOffset? startDatetime, // pass null
    DateTimeOffset? endDatetime,   // pass null
    bool? includeArchived,         // pass false
    ListProductsInclude? include,  // pass null
    int? page = 1,
    int? perPage = 20,
    CancellationToken ct = default
)
```

**Response envelope:**
`ProductResponse` → `Product (product): Product !req`

**Key `Product` fields (namespace `MaxioAdvancedBilling.Models`):**
| C# field (wire) | Type | Notes |
|---|---|---|
| `Id (id)` | `int?` | Product ID |
| `Name (name)` | `string?` | Display name |
| `Handle (handle)` | `string?` | e.g. `eshop-pro`, `basic-plan` |
| `Description (description)` | `string?` | |
| `PriceInCents (price_in_cents)` | `long?` | e.g. 29900 for $299.00 |
| `Interval (interval)` | `int?` | Billing interval (1) |
| `IntervalUnit (interval_unit)` | `IntervalUnit?` | `IntervalUnit.Month` |
| `ProductFamilyId (product_family_id)` | `int?` | |
| `ProductFamily (product_family)` | `ProductFamily?` | Nested family object |
| `RequireCreditCard (require_credit_card)` | `bool?` | |
| `RequestCreditCard (request_credit_card)` | `bool?` | |
| `TrialPriceInCents (trial_price_in_cents)` | `long?` | |
| `DefaultProductPricePointId (default_product_price_point_id)` | `int?` | |

---

### Operation: Create Customer

`client.Customers.CreateCustomer(...)`

| | |
|---|---|
| HTTP | `POST /customers.json` |
| Returns | `CustomerResponse` |
| Error | **Case A (typed)** `SdkException<CreateCustomerError>` |
| Error accessors | `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| Source | `map/operations/Customers.md` |

**Signature:**
```csharp
CreateCustomer(
    CreateCustomerRequest? body,
    CancellationToken ct = default
)
```

**Request body:**
`CreateCustomerRequest` → `Customer (customer): CreateCustomer !req`

**`CreateCustomer` record fields (namespace `MaxioAdvancedBilling.Models`):**
| C# field (wire) | Type | Required | Notes |
|---|---|---|---|
| `FirstName (first_name)` | `string` | **!req** | |
| `LastName (last_name)` | `string` | **!req** | |
| `Email (email)` | `string` | **!req** | |
| `CcEmails (cc_emails)` | `string?` | optional | |
| `Organization (organization)` | `string?` | optional | |
| `Reference (reference)` | `string?` | optional | **Use for idempotency** — unique customer identifier from your app |
| `Address (address)` | `string?` | optional | |
| `Address2 (address_2)` | `string?` | optional | |
| `City (city)` | `string?` | optional | |
| `State (state)` | `string?` | optional | ISO 3166-2 |
| `Zip (zip)` | `string?` | optional | |
| `Country (country)` | `string?` | optional | ISO 3166-1 (2 chars) |
| `Phone (phone)` | `string?` | optional | |
| `Locale (locale)` | `string?` | optional | |
| `VatNumber (vat_number)` | `string?` | optional | |
| `TaxExempt (tax_exempt)` | `bool?` | optional | |
| `TaxExemptReason (tax_exempt_reason)` | `string?` | optional | |
| `ParentId (parent_id)` | `int?` | optional | |
| `SalesforceId (salesforce_id)` | `string?` | optional | |

**Response envelope:**
`CustomerResponse` → `Customer (customer): Customer !req`

**Key `Customer` fields:**
| C# field (wire) | Type | Notes |
|---|---|---|
| `Id (id)` | `int?` | Maxio customer ID |
| `FirstName (first_name)` | `string?` | |
| `LastName (last_name)` | `string?` | |
| `Email (email)` | `string?` | |
| `Reference (reference)` | `string?` | App's unique identifier |
| `Organization (organization)` | `string?` | |
| `CreatedAt (created_at)` | `DateTimeOffset?` | |

**Idempotency strategy:** Use `ReadCustomerByReference` (below) before `CreateCustomer`. If found, skip creation.

---

### Operation: Read Customer by Reference

`client.Customers.ReadCustomerByReference(...)`

| | |
|---|---|
| HTTP | `GET /customers/lookup.json` |
| Returns | `CustomerResponse` |
| Error | **Case B (raw)** `SdkException<RawError>` |
| Source | `map/operations/Customers.md` |

**Signature:**
```csharp
ReadCustomerByReference(
    string reference,
    CancellationToken ct = default
)
```

**Query params:** `reference` ← `reference`

---

### Operation: Create Subscription

`client.Subscriptions.CreateSubscription(...)`

| | |
|---|---|
| HTTP | `POST /subscriptions.json` |
| Returns | `SubscriptionResponse` |
| Error | **Case A (typed)** `SdkException<CreateSubscriptionError>` |
| Error accessors | `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| Source | `map/operations/Subscriptions.md` |

**Signature:**
```csharp
CreateSubscription(
    CreateSubscriptionRequest? body,
    CancellationToken ct = default
)
```

**Request body:**
`CreateSubscriptionRequest` → `Subscription (subscription): CreateSubscription !req`

**`CreateSubscription` record fields (namespace `MaxioAdvancedBilling.Models`):**
| C# field (wire) | Type | Required | Notes |
|---|---|---|---|
| `ProductHandle (product_handle)` | `string?` | optional | **Use this** — e.g. `"eshop-pro"` |
| `ProductId (product_id)` | `int?` | optional | Alternative to handle |
| `ProductPricePointHandle (product_price_point_handle)` | `string?` | optional | For specific price point |
| `ProductPricePointId (product_price_point_id)` | `int?` | optional | |
| `CustomerId (customer_id)` | `int?` | optional | **Use this** — from CreateCustomer response |
| `CustomerReference (customer_reference)` | `string?` | optional | Alternative — lookup by reference |
| `Reference (reference)` | `string?` | optional | Unique reference for this subscription |
| `PaymentProfileId (payment_profile_id)` | `int?` | optional | **NOT required if product allows** |
| `PaymentCollectionMethod (payment_collection_method)` | `CollectionMethod?` | optional | |
| `CouponCode (coupon_code)` | `string?` | optional | |
| `CouponCodes (coupon_codes)` | `IReadOnlyList<string>?` | optional | |
| `CustomerAttributes (customer_attributes)` | `CustomerAttributes?` | optional | Inline customer creation |
| `PaymentProfileAttributes (payment_profile_attributes)` | `PaymentProfileAttributes?` | optional | |
| `CreditCardAttributes (credit_card_attributes)` | `PaymentProfileAttributes?` | optional | Alias |
| `Components (components)` | `IReadOnlyList<CreateSubscriptionComponent>?` | optional | For metered components |
| `NextBillingAt (next_billing_at)` | `DateTimeOffset?` | optional | |
| `InitialBillingAt (initial_billing_at)` | `DateTimeOffset?` | optional | |
| `CalendarBilling (calendar_billing)` | `CalendarBilling?` | optional | |
| `Metafields (metafields)` | `IReadOnlyDictionary<string, string>?` | optional | |
| `ReceivesInvoiceEmails (receives_invoice_emails)` | `string?` | optional | |
| `NetTerms (net_terms)` | `string?` | optional | |
| `Currency (currency)` | `string?` | optional | |
| `ExpiresAt (expires_at)` | `DateTimeOffset?` | optional | |
| `AgreementTerms (agreement_terms)` | `string?` | optional | |
| `DeferSignup (defer_signup)` | `bool?` | optional | Default `false` |
| `SalesRepId (sales_rep_id)` | `int?` | optional | |
| `Ref (ref)` | `string?` | optional | |
| `Group (group)` | `GroupSettings?` | optional | |
| `OfferId (offer_id)` | `OfferId?` (union) | optional | |
| `AgreementAcceptance (agreement_acceptance)` | `AgreementAcceptance?` | optional | |
| `AchAgreement (ach_agreement)` | `AchAgreement?` | optional | |

**Response envelope:**
`SubscriptionResponse` → `Subscription (subscription): Subscription?`

**Key `Subscription` fields (namespace `MaxioAdvancedBilling.Models`):**
| C# field (wire) | Type | Notes |
|---|---|---|
| `Id (id)` | `int?` | Maxio subscription ID |
| `State (state)` | `SubscriptionState?` | See enum below |
| `ProductPriceInCents (product_price_in_cents)` | `long?` | Plan price |
| `CurrentPeriodEndsAt (current_period_ends_at)` | `DateTimeOffset?` | Next billing date |
| `NextAssessmentAt (next_assessment_at)` | `DateTimeOffset?` | Next charge date |
| `ActivatedAt (activated_at)` | `DateTimeOffset?` | |
| `CreatedAt (created_at)` | `DateTimeOffset?` | |
| `Product (product)` | `Product?` | Nested product details |
| `Customer (customer)` | `Customer?` | Nested customer details |
| `PaymentCollectionMethod (payment_collection_method)` | `CollectionMethod?` | |
| `Reference (reference)` | `string?` | Your app's reference |
| `CurrentBillingAmountInCents (current_billing_amount_in_cents)` | `long?` | |
| `Currency (currency)` | `string?` | |
| `CancelAtEndOfPeriod (cancel_at_end_of_period)` | `bool?` | |
| `CanceledAt (canceled_at)` | `DateTimeOffset?` | |

---

### Operation: List Customer Subscriptions

`client.Customers.ListCustomerSubscriptions(...)`

| | |
|---|---|
| HTTP | `GET /customers/{customer_id}/subscriptions.json` |
| Returns | `IReadOnlyList<SubscriptionResponse>` |
| Error | **Case B (raw)** `SdkException<RawError>` |
| Source | `map/operations/Customers.md` |

**Signature:**
```csharp
ListCustomerSubscriptions(
    int customerId,
    CancellationToken ct = default
)
```

---

### Operation: Find Component by Handle

`client.Components.FindComponent(...)`

| | |
|---|---|
| HTTP | `GET /components/lookup.json` |
| Returns | `ComponentResponse` |
| Error | **Case B (raw)** `SdkException<RawError>` |
| Source | `map/operations/Components.md` |

**Signature:**
```csharp
FindComponent(
    string handle,
    CancellationToken ct = default
)
```

**Query params:** `handle` ← `handle`

**Response envelope:**
`ComponentResponse` → `Component (component): Component !req`

**Key `Component` fields:**
| C# field (wire) | Type | Notes |
|---|---|---|
| `Id (id)` | `int?` | Component ID |
| `Name (name)` | `string?` | |
| `Handle (handle)` | `string?` | e.g. `api-call` |
| `Kind (kind)` | `ComponentKind?` | `ComponentKind.MeteredComponent` |
| `PricingScheme (pricing_scheme)` | `PricingScheme?` | |
| `UnitPrice (unit_price)` | `string?` | |
| `UnitName (unit_name)` | `string?` | |
| `ProductFamilyId (product_family_id)` | `int?` | |
| `ProductFamilyHandle (product_family_handle)` | `string?` | |
| `PricePerUnitInCents (price_per_unit_in_cents)` | `long?` | |

---

### Enums (namespace `MaxioAdvancedBilling.Models.Enums`)

These are `StringEnum<T>` records — **not C# enums**. Construct via static members.

**`SubscriptionState`** — `StringEnum<SubscriptionState>`:
| C# member | Wire value |
|---|---|
| `Pending` | `pending` |
| `FailedToCreate` | `failed_to_create` |
| `Trialing` | `trialing` |
| `Assessing` | `assessing` |
| `Active` | `active` |
| `SoftFailure` | `soft_failure` |
| `PastDue` | `past_due` |
| `Suspended` | `suspended` |
| `Canceled` | `canceled` |
| `Expired` | `expired` |
| `Paused` | `paused` |
| `Unpaid` | `unpaid` |
| `TrialEnded` | `trial_ended` |
| `OnHold` | `on_hold` |
| `AwaitingSignup` | `awaiting_signup` |

**`CollectionMethod`** — `StringEnum<CollectionMethod>`:
| C# member | Wire value |
|---|---|
| `Automatic` | `automatic` |
| `Remittance` | `remittance` |
| `Prepaid` | `prepaid` |
| `Invoice` | `invoice` |

**`ComponentKind`** — `StringEnum<ComponentKind>`:
| C# member | Wire value |
|---|---|
| `MeteredComponent` | `metered_component` |
| `QuantityBasedComponent` | `quantity_based_component` |
| `OnOffComponent` | `on_off_component` |
| `PrepaidUsageComponent` | `prepaid_usage_component` |
| `EventBasedComponent` | `event_based_component` |

**`IntervalUnit`** — `StringEnum<IntervalUnit>`:
| C# member | Wire value |
|---|---|
| `Day` | `day` |
| `Month` | `month` |

**`PricingScheme`** — `StringEnum<PricingScheme>`:
| C# member | Wire value |
|---|---|
| `Stairstep` | `stairstep` |
| `Volume` | `volume` |
| `PerUnit` | `per_unit` |
| `Tiered` | `tiered` |

---

## 3. Error Handling

### Error model

Operations are **throw-based**. On error, the SDK throws `SdkException<TError>`.

- **Case A (typed):** `TError` is a generated `{Operation}Error` class with `TryGet…(out …)` accessors per HTTP status, plus inherited `TryGetRawError(out RawError)`.
- **Case B (raw):** `TError` is `RawError` with `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`.

**No no-throw variants exist in this SDK.** Every operation is throw-only.

### Operation error cases

| Operation | Error type | Case | Accessors |
|---|---|---|---|
| `ListProductsForProductFamily` | `SdkException<ListProductsForProductFamilyError>` | A | `TryGetString(out string)` [404], `TryGetRawError(out RawError)` [fallback] |
| `CreateCustomer` | `SdkException<CreateCustomerError>` | A | `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422], `TryGetRawError(out RawError)` [fallback] |
| `ReadCustomerByReference` | `SdkException<RawError>` | B | `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` |
| `CreateSubscription` | `SdkException<CreateSubscriptionError>` | A | `TryGetErrorListResponse1(out ErrorListResponse1)` [422], `TryGetRawError(out RawError)` [fallback] |
| `ListCustomerSubscriptions` | `SdkException<RawError>` | B | `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` |
| `FindComponent` | `SdkException<RawError>` | B | `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` |

### Error payload types

- `CustomerErrorResponse1` → `Errors (errors): Errors?` where `Errors` has `PerPage (per_page): IReadOnlyList<string>?` and `PricePoint (price_point): IReadOnlyList<string>?`
- `ErrorListResponse1` → `Errors (errors): IReadOnlyList<string> !req`

---

## 4. Configuration

### Options POCO

```csharp
public class MaxioOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
}
```

### appsettings.json section

```json
{
  "Maxio": {
    "ApiKey": "",
    "Subdomain": "cp-exp-5",
    "ProductFamilyHandle": "eshop-subscribe",
    "BaseUrl": ""
  }
}
```

### Environment variables

| Env var | Config key | Notes |
|---|---|---|
| `MAXIO_API_KEY` | `Maxio:ApiKey` | Required |
| `MAXIO_SITE_SUBDOMAIN` | `Maxio:Subdomain` | e.g. `cp-exp-5` |
| `MAXIO_ENVIRONMENT` | (mapped to `ServerEnvironment`) | `US` or `EU` |
| `MAXIO_DEFAULT_PRODUCT_FAMILY` | `Maxio:ProductFamilyHandle` | e.g. `eshop-subscribe` |

---

## 5. Implementation Notes

### Idempotent Customer Creation

The `reference` field on `CreateCustomer` is unique per site. Strategy:
1. On subscribe, first call `ReadCustomerByReference(reference)` where `reference` = user's stable ID (e.g. ASP.NET Identity user ID).
2. If found → use existing `customer.Id`.
3. If 404 → call `CreateCustomer` with `Reference = userId`.
4. Race condition: two concurrent creates with same reference → one succeeds, other gets 422. Handle `TryGetCustomerErrorResponse1` → treat as "already exists" and retry `ReadCustomerByReference`.

### Payment Method

The task states "Payment method NOT required on plans." This means the product `require_credit_card` is `false`. No `payment_profile_id`, `credit_card_attributes`, or `payment_profile_attributes` needed in `CreateSubscription`.

### Listing Plans

Use `ListProductsForProductFamily` with `productFamilyId = "eshop-subscribe"` (the handle). This returns all products in that family. The response `ProductResponse.Product` contains `Handle`, `Name`, `PriceInCents`, `IntervalUnit`, etc.

### Subscribing

Pass `ProductHandle = planHandle` (e.g. `"eshop-pro"`) and `CustomerId = customerId` in `CreateSubscription`. The response's `SubscriptionResponse.Subscription` contains `State`, `ProductPriceInCents`, `CurrentPeriodEndsAt` (next billing date), etc.

### Listing User's Subscriptions

Use `ListCustomerSubscriptions(customerId)` — returns `IReadOnlyList<SubscriptionResponse>`. Each `SubscriptionResponse.Subscription` has `Id`, `State`, `Product` (with `Name`, `Handle`, `PriceInCents`), `CurrentPeriodEndsAt`, `NextAssessmentAt`.

---

## 6. Assumptions & Blockers

| # | Type | Statement |
|---|---|---|
| 1 | Assumption | The eShopOnWeb user ID (from ASP.NET Identity) can serve as the Maxio customer `reference` for idempotent lookup. The mapping is `reference = userId.ToString()`. |
| 2 | Assumption | Products in the `eshop-subscribe` family have `require_credit_card = false` on the sandbox site `cp-exp-5`. If not, `CreateSubscription` will return 422 and the flow must add payment profile capture. |
| 3 | Assumption | The `ProductFamilies` endpoint accepts handles (not just numeric IDs) for `productFamilyId`. The map's `ListProductsForProductFamily` signature takes `string productFamilyId`, which supports the `handle:` prefix convention. |
| 4 | Assumption | Metered component (`api-call`) subscription allocation is out of scope for the initial three endpoints — it would require a separate usage-reporting endpoint. The current plan only covers plan listing, subscription creation, and subscription listing. |
| 5 | Assumption | No trial, no setup fee — the seeded products on `cp-exp-5` are configured with `TrialPriceInCents = null` and no initial charge. |
| 6 | Blocker (minor) | The `BaseUrl` configuration is optional; if not set, the SDK defaults to `https://{subdomain}.chargify.com` for US. Confirm the sandbox base URL matches before deployment. |

---

## 7. REQUIRED READING

The following `dotnet-*` skills **MUST** be loaded before implementation starts. The contract sheet deliberately does not carry their contents.

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 2 — registering the SDK client, HttpClient lifetime, DI registration |
| `dotnet-authentication` | Step 2 — wiring BasicAuth credentials, env var loading |
| `dotnet-configuration-resilience` | Step 2 — retry/timeout options, what `Timeout` actually bounds, what `HttpMethodsToRetry` does and does not cover |
| `dotnet-calling-endpoints` | Steps 5–7 — calling SDK operations, named vs positional args, async patterns |
| `dotnet-models` | Steps 5–7 — building request objects, `required` members, enum construction (`StringEnum`), union types |
| `dotnet-error-handling` | Steps 4–7 — the try/catch boundary, Case A vs Case B, `TryGet…` accessors, when `JsonException` can escape |
| `dotnet-testing` | Step 8 — stubbing the SDK in unit tests |

**Hazard: `System.Text.Json.JsonException` reaches the boundary from two directions:**
1. A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
2. A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage.

**MUST load `dotnet-error-handling`** before writing that boundary.
