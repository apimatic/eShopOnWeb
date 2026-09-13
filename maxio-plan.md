# Maxio Advanced Billing Integration Plan — eShopOnWeb

## 1. Scope & Sequence

| Step | Description | Operations Used |
|------|-------------|-----------------|
| 1 | Install NuGet package | `dotnet add package AsadAli.AdvancedBilling.Sdk` |
| 2 | Register `MaxioAdvancedBillingClient` in DI | Client construction + auth |
| 3 | Config binding (`Maxio:` section) | `MaxioAdvancedBillingClientOptions`, `BasicAuthCredentials`, `ServerOptions` |
| 4 | `GET /api/subscriptions-plans` — list available plans | `ListProductsForProductFamily` |
| 5 | `POST /api/subscriptions` — create subscription | `ReadCustomerByReference` → `CreateCustomer` (if needed) → `CreateSubscription` |
| 6 | `GET /api/my-subscriptions` — list user's subscriptions | `ListCustomerSubscriptions` |

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal
> C# identifier. The cancellation-token parameter really is named `ct`: in named
> arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take
> each one from that type's own map row, never from where a neighbouring type sits.

### Step 2+3: Client Construction & Auth

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration; // RetryOptions
using MaxioAdvancedBilling.Servers;

var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials { Username = config.ApiKey, Password = "x" },
    Environment = config.Environment == "EU"
        ? ServerEnvironment.Eu
        : ServerEnvironment.Us,
};
// Server.Site = config.Subdomain
// Optional override: options.Server.Production.Us.BaseUrl = config.BaseUrl;
var client = new MaxioAdvancedBillingClient(httpClient, options);
```

**All `MaxioAdvancedBillingClientOptions` properties** (source: `MaxioAdvancedBillingClientOptions.cs`):

| Property | Type |
|---|---|
| `Environment` | `ServerEnvironment` |
| `Retry` | `RetryOptions` |
| `Server` | `ServerOptions` |
| `BasicAuth` | `BasicAuthCredentials?` |

**Server configuration** — to set the site subdomain:
```csharp
options.Server.Production.Us.Site = subdomain;
```

**Base URL override** (for custom/mock hosts):
```csharp
options.Server.Production.Us.BaseUrl = "https://custom-host.example.com";
```

**DI registration alternative**:
```csharp
services.AddMaxioAdvancedBillingClient(o =>
{
    o.BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" };
    o.Server.Production.Us.Site = subdomain;
});
```

**Config binding key path:** `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:Environment`, `Maxio:BaseUrl`.

---

### Step 4: List Available Plans

**SDK Operation:** `client.ProductFamilies.ListProductsForProductFamily`

| | |
|---|---|
| **Accessor** | `client.ProductFamilies` |
| **Signature** | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| **Params** | `productFamilyId` — the product family handle or ID string. `dateField`…`include` — 8 nullable params, pass `null` to skip. Defaults: `page` = 1, `perPage` = 20. |
| **Returns** | `IReadOnlyList<ProductResponse>` |
| **Error** | `SdkException<ListProductsForProductFamilyError>` — **Case A (typed)** |
| **Error accessors** | `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] |
| **Pagination** | manual `page` + `perPage` |

**Response envelope:** `ProductResponse` → `Product` (1 required field):

| C# Field | Wire Name | Type | Required |
|---|---|---|---|
| `Product` | `product` | `Product` | **yes** |

**Key `Product` fields** (namespace `MaxioAdvancedBilling.Models`):

| C# Field | Wire Name | Type | Required |
|---|---|---|---|
| `Id` | `id` | `int?` | — |
| `Name` | `name` | `string?` | — |
| `Handle` | `handle` | `string?` | — |
| `Description` | `description` | `string?` | — |
| `PriceInCents` | `price_in_cents` | `long?` | — |
| `Interval` | `interval` | `int?` | — |
| `IntervalUnit` | `interval_unit` | `IntervalUnit?` | — |
| `TrialPriceInCents` | `trial_price_in_cents` | `long?` | — |
| `RequireCreditCard` | `require_credit_card` | `bool?` | — |
| `ArchivedAt` | `archived_at` | `DateTimeOffset?` | — |
| `ProductFamily` | `product_family` | `ProductFamily?` | — |

**Notes:** The product family ID for this integration is `eshop-subscribe` (handle) / `3023074` (ID). Pass as `productFamilyId` string. Filter archived products client-side by checking `ArchivedAt` is null. The two seeded products are `eshop-pro` (ID 7126957, $299/mo) and `basic-plan` (ID 7126958, $29/mo).

---

### Step 5: Create Subscription

#### 5a. Ensure Maxio Customer Exists (idempotent lookup + create)

**SDK Operation 1:** `client.Customers.ReadCustomerByReference`

| | |
|---|---|
| **Accessor** | `client.Customers` |
| **Signature** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` |
| **Params** | `reference` — the eShopOnWeb user ID as a string. Must pass explicitly. |
| **Returns** | `CustomerResponse` |
| **Error** | `SdkException<RawError>` — **Case B** |
| **Error accessors** | `StatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()` |

**Logic:** Call `ReadCustomerByReference` with the eShopOnWeb user ID. If it throws with 404 (Case B, so catch `SdkException<RawError>` and check `ex.Error.StatusCode == HttpStatusCode.NotFound`), fall through to create.

**SDK Operation 2:** `client.Customers.CreateCustomer`

| | |
|---|---|
| **Signature** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` |
| **Params** | `body` — nullable, no default → **must pass explicitly** |
| **Returns** | `CustomerResponse` |
| **Error** | `SdkException<CreateCustomerError>` — **Case A (typed)** |
| **Error accessors** | `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |

**Request body:** `CreateCustomerRequest` → `CreateCustomer` (namespace `MaxioAdvancedBilling.Models`):

| C# Field | Wire Name | Type | Required |
|---|---|---|---|
| `Customer` | `customer` | `CreateCustomer` | **yes** |

**`CreateCustomer` fields:**

| C# Field | Wire Name | Type | Required |
|---|---|---|---|
| `FirstName` | `first_name` | `string` | **yes** |
| `LastName` | `last_name` | `string` | **yes** |
| `Email` | `email` | `string` | **yes** |
| `CcEmails` | `cc_emails` | `string?` | — |
| `Organization` | `organization` | `string?` | — |
| `Reference` | `reference` | `string?` | — |
| `Address` | `address` | `string?` | — |
| `Address2` | `address_2` | `string?` | — |
| `City` | `city` | `string?` | — |
| `State` | `state` | `string?` | — |
| `Zip` | `zip` | `string?` | — |
| `Country` | `country` | `string?` | — |
| `Phone` | `phone` | `string?` | — |
| `Locale` | `locale` | `string?` | — |
| `VatNumber` | `vat_number` | `string?` | — |
| `TaxExempt` | `tax_exempt` | `bool?` | — |
| `TaxExemptReason` | `tax_exempt_reason` | `string?` | — |
| `ParentId` | `parent_id` | `int?` | — |
| `SalesforceId` | `salesforce_id` | `string?` | — |

**Idempotency:** The `Reference` field on `CreateCustomer` must be unique. Use the eShopOnWeb user ID as the `reference` value. If `ReadCustomerByReference` succeeds, reuse the returned `CustomerResponse.Customer.Id` — do not create a duplicate.

**Note on 422:** If a 422 occurs (e.g. duplicate reference race), catch `SdkException<CreateCustomerError>`, call `TryGetCustomerErrorResponse1`, then retry `ReadCustomerByReference` to get the existing customer.

#### 5b. Create the Subscription

**SDK Operation:** `client.Subscriptions.CreateSubscription`

| | |
|---|---|
| **Accessor** | `client.Subscriptions` |
| **Signature** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` |
| **Params** | `body` — nullable, no default → **must pass explicitly** |
| **Returns** | `SubscriptionResponse` |
| **Error** | `SdkException<CreateSubscriptionError>` — **Case A (typed)** |
| **Error accessors** | `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |

**Request body:** `CreateSubscriptionRequest` → `CreateSubscription` (namespace `MaxioAdvancedBilling.Models`):

| C# Field | Wire Name | Type | Required |
|---|---|---|---|
| `Subscription` | `subscription` | `CreateSubscription` | **yes** |

**`CreateSubscription` key fields:**

| C# Field | Wire Name | Type | Required | Notes |
|---|---|---|---|---|
| `ProductHandle` | `product_handle` | `string?` | — | Use `"eshop-pro"` for the default plan |
| `ProductId` | `product_id` | `int?` | — | Or use ID `7126957` |
| `CustomerReference` | `customer_reference` | `string?` | — | eShopOnWeb user ID — preferred idempotent customer reference |
| `CustomerId` | `customer_id` | `int?` | — | Or use the resolved Maxio customer ID |
| `Reference` | `reference` | `string?` | — | Subscription-level reference for idempotency |
| `Components` | `components` | `IReadOnlyList<CreateSubscriptionComponent>?` | — | For metered component enrollment |
| `PaymentCollectionMethod` | `payment_collection_method` | `CollectionMethod?` | — | See note below |
| `DeferSignup` | `defer_signup` | `bool?` | — | Default `false` |
| `NextBillingAt` | `next_billing_at` | `DateTimeOffset?` | — | — |
| `InitialBillingAt` | `initial_billing_at` | `DateTimeOffset?` | — | — |
| `CalendarBilling` | `calendar_billing` | `CalendarBilling?` | — | — |
| `Metafields` | `metafields` | `IReadOnlyDictionary<string, string>?` | — | — |

**Payment method note:** The task states "payment method not required" for both plans. Maxio may still require a payment method depending on site settings. For sandbox `cp-exp-7` with test gateway (bogus), subscriptions can be created without a card. The `PaymentCollectionMethod` should be set to `CollectionMethod.Invoice` if you want invoice-based collection, or left null for the site default.

**Response envelope:** `SubscriptionResponse` → `Subscription`:

| C# Field | Wire Name | Type | Required |
|---|---|---|---|
| `Subscription` | `subscription` | `Subscription?` | — (nullable!) |

**Key `Subscription` fields for the API response:**

| C# Field | Wire Name | Type |
|---|---|---|
| `Id` | `id` | `int?` |
| `State` | `state` | `SubscriptionState?` |
| `CurrentPeriodEndsAt` | `current_period_ends_at` | `DateTimeOffset?` |
| `NextAssessmentAt` | `next_assessment_at` | `DateTimeOffset?` |
| `ActivatedAt` | `activated_at` | `DateTimeOffset?` |
| `CreatedAt` | `created_at` | `DateTimeOffset?` |
| `Product` | `product` | `Product?` |
| `Customer` | `customer` | `Customer?` |
| `ProductPriceInCents` | `product_price_in_cents` | `long?` |

**Metered component enrollment (optional):** To enroll the subscription with the `api-call` metered component (handle `api-call`, ID `3057195`), populate `Components` in the `CreateSubscription`:

```csharp
Components = new List<CreateSubscriptionComponent>
{
    new CreateSubscriptionComponent
    {
        ComponentId = ComponentId1.Int(3057195),  // api-call component ID
        Enabled = true,
    }
}
```

**`CreateSubscriptionComponent` fields:**

| C# Field | Wire Name | Type | Required |
|---|---|---|---|
| `ComponentId` | `component_id` | `ComponentId1?` | — (union: `int` or `string`) |
| `Enabled` | `enabled` | `bool?` | — |
| `UnitBalance` | `unit_balance` | `int?` | — |
| `AllocatedQuantity` | `allocated_quantity` | `AllocatedQuantity3?` | — (union: `int` or `string`) |
| `Quantity` | `quantity` | `int?` | — |
| `PricePointId` | `price_point_id` | `PricePointId2?` | — (union: `int` or `string`) |
| `CustomPrice` | `custom_price` | `ComponentCustomPrice?` | — |

---

### Step 6: List User's Subscriptions

**SDK Operation:** `client.Customers.ListCustomerSubscriptions`

| | |
|---|---|
| **Accessor** | `client.Customers` |
| **Signature** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` |
| **Params** | `customerId` — the Maxio customer ID (from step 5a). |
| **Returns** | `IReadOnlyList<SubscriptionResponse>` |
| **Error** | `SdkException<RawError>` — **Case B** |
| **Error accessors** | `StatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()` |
| **Pagination** | none |

**Prerequisite:** You need the Maxio `customer_id` (int). This comes from the `ReadCustomerByReference` or `CreateCustomer` response in step 5a (`CustomerResponse.Customer.Id`).

---

## 3. Enums Used

| Enum | Namespace | Values Needed |
|---|---|---|
| `SubscriptionState` | `MaxioAdvancedBilling.Models.Enums` | `Active`, `Canceled`, `PastDue`, `Trialing`, `Pending`, etc. |
| `IntervalUnit` | `MaxioAdvancedBilling.Models.Enums` | `Month`, `Day` |
| `CollectionMethod` | `MaxioAdvancedBilling.Models.Enums` | `Automatic`, `Invoice`, `Remittance`, `Prepaid` |
| `ComponentKind` | `MaxioAdvancedBilling.Models.Enums` | `MeteredComponent`, `QuantityBasedComponent`, `OnOffComponent` |
| `ServerEnvironment` | `MaxioAdvancedBilling.Servers` | `Us`, `Eu` |

All enums are `StringEnum<T>` / `IntEnum<T>` records — **not** C# enums. Construct via static members (e.g. `SubscriptionState.Active`) or `Type.FromValue("wire")`.

---

## 4. Unions Used

| Union | Variants | Used In | How to Construct |
|---|---|---|---|
| `ComponentId1` | `int`, `string` | `CreateSubscriptionComponent.ComponentId` | `ComponentId1.Int(3057195)` |
| `AllocatedQuantity3` | `int`, `string` | `CreateSubscriptionComponent.AllocatedQuantity` | `AllocatedQuantity3.Int(0)` |
| `PricePointId2` | `int`, `string` | `CreateSubscriptionComponent.PricePointId` | `PricePointId2.Int(pricePointId)` |

---

## 5. Error Handling Summary

| Operation | Error Type | Case | Key Accessors |
|---|---|---|---|
| `ListProductsForProductFamily` | `SdkException<ListProductsForProductFamilyError>` | A (typed) | `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` |
| `ReadCustomerByReference` | `SdkException<RawError>` | B (raw) | `StatusCode` · `ReadAsString()` |
| `CreateCustomer` | `SdkException<CreateCustomerError>` | A (typed) | `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` |
| `CreateSubscription` | `SdkException<CreateSubscriptionError>` | A (typed) | `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` |
| `ListCustomerSubscriptions` | `SdkException<RawError>` | B (raw) | `StatusCode` · `ReadAsString()` |

**⚠ CRITICAL: `System.Text.Json.JsonException` hazard.** A drifted or malformed 2xx body surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape. A non-2xx body that doesn't match its generated error shape throws `JsonException` *while the error object is being constructed*, destroying the HTTP status. **MUST load `dotnet-error-handling`** before writing the exception boundary.

---

## 6. Trap Notes

⚠ Step 2 (client registration) — the SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register. **MUST load `dotnet-configuration-resilience`** before wiring the client.

⚠ Step 3 (config binding) — `ServerOptions` has a nested structure: `options.Server.Production.Us.Site = subdomain`. The subdomain is set on the server group, not on a top-level property. **MUST load `dotnet-client-initialization`** before wiring server config.

⚠ Step 5b (create subscription) — the `CreateSubscription.CustomerReference` and `CreateSubscription.Reference` fields serve different purposes: `CustomerReference` is the customer's external ID, `Reference` is a subscription-level idempotency key. **MUST load `dotnet-models`** to understand the union types (`ComponentId1`, `AllocatedQuantity3`, `PricePointId2`) used in `CreateSubscriptionComponent`.

⚠ Step 6 (list subscriptions) — `ListCustomerSubscriptions` returns `IReadOnlyList<SubscriptionResponse>`, where `SubscriptionResponse.Subscription` is nullable. A null subscription inside a response item indicates a data inconsistency; guard against it. **MUST load `dotnet-calling-endpoints`** before building list responses.

⚠ All steps — all operations are throw-only (no `…Result` variants). Every call must be wrapped in try/catch. **MUST load `dotnet-error-handling`** before writing any error boundary.

---

## 7. REQUIRED READING

| Skill | Governs | Why |
|---|---|---|
| `dotnet-client-initialization` | Step 2-3: Client construction, DI, server config | `HttpClient` lifetime, `ServerOptions` nesting, DI registration |
| `dotnet-authentication` | Step 3: Basic auth credentials | `BasicAuthCredentials` property name, username/password convention |
| `dotnet-calling-endpoints` | Steps 4-6: Every SDK call | Named arguments for optional params, async usage, cancellation |
| `dotnet-models` | Step 5b: Request body construction | Union factory methods, enum construction, nullable vs required |
| `dotnet-error-handling` | All steps: Exception boundaries | Case A/B distinction, `TryGet…` accessors, `JsonException` hazard |
| `dotnet-configuration-resilience` | Step 3: Retry/timeout tuning | What `Timeout` actually bounds, retry behavior on POST, logging gaps |

**⚠ HAZARD: `System.Text.Json.JsonException` reaches the boundary from two directions:**

1. A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
2. A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage.

**MUST load `dotnet-error-handling`** before writing that boundary. These rows belong in the FIRST sheet, not a later revision.

---

## 8. Assumptions & Blockers

- **Assumption:** The eShopOnWeb user ID (from ASP.NET Identity) can be used as the Maxio customer `reference` string. If the user ID is a GUID, it will work as a reference; if it is null/empty, the integration must generate a stable synthetic reference.
- **Assumption:** The sandbox site `cp-exp-7` uses a test gateway (bogus) that does not require real card details for subscription creation. The plans are configured with `require_credit_card: false`.
- **Assumption:** The `PaymentCollectionMethod` can be left as the site default (likely `Automatic`). If the site enforces a specific collection method, the integration must set it explicitly.
- **Assumption:** Products are fetched by product family handle `eshop-subscribe` (or ID `3023074`). The `ListProductsForProductFamily` call returns both plans in the family.
- **No blockers identified.** All required SDK operations and models are present in the map.

---

## 9. Config Binding Model (YOUR CALL — not in the map)

The implementer must define a POCO to bind from the `Maxio:` configuration section:

```csharp
// YOUR CALL — not in the map — the implementer defines this
public class MaxioOptions
{
    public string ApiKey { get; set; } = "";
    public string Subdomain { get; set; } = "";
    public string Environment { get; set; } = "US"; // "US" or "EU"
    public string? BaseUrl { get; set; } // optional override
    public string ProductFamilyHandle { get; set; } = "eshop-subscribe";
}
```

Bind from: `IConfiguration.GetSection("Maxio")`. Env vars: `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_ENVIRONMENT`, `MAXIO_DEFAULT_PRODUCT_FAMILY`.

---

## 10. Source Citations

| Fact | Map Page |
|---|---|
| Client construction + auth + servers | `sdk-map.md` § Getting a client, § Servers & auth |
| `ListProductsForProductFamily` signature + errors | `map/operations/ProductFamilies.md` |
| `ReadCustomerByReference` signature + errors | `map/operations/Customers.md` |
| `CreateCustomer` signature + errors | `map/operations/Customers.md` |
| `CreateSubscription` signature + errors | `map/operations/Subscriptions.md` |
| `ListCustomerSubscriptions` signature + errors | `map/operations/Customers.md` |
| `CreateCustomerRequest` / `CreateCustomer` model | `map/models/records-1-Ac-Cr.md` |
| `CreateSubscriptionRequest` / `CreateSubscription` model | `map/models/records-2-Cr-Ne.md` |
| `CreateSubscriptionComponent` model | `map/models/records-2-Cr-Ne.md` |
| `CustomerResponse` / `Customer` model | `map/models/records-2-Cr-Ne.md` |
| `SubscriptionResponse` / `Subscription` model | `map/models/records-3-Of-Su.md` |
| `ProductResponse` / `Product` model | `map/models/records-3-Of-Su.md` |
| `ProductFamilyResponse` / `ProductFamily` model | `map/models/records-3-Of-Su.md` |
| `ComponentId1`, `AllocatedQuantity3`, `PricePointId2` unions | `map/models/unions.md` |
| `SubscriptionState`, `IntervalUnit`, `CollectionMethod` enums | `map/models/enums.md` |
| Error model (`SdkException<T>`, `RawError`, `ApiError`) | `sdk-map.md` § Error-handling model |
