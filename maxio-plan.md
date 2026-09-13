# Maxio Advanced Billing Integration — eShopOnWeb

**Scope:** Add recurring subscription billing as an additive, parallel capability to the eShopOnWeb one-time commerce app. Three new API endpoints backed by the Maxio SDK, with no changes to existing catalog/basket/order flows.

---

## 1. Scope & Sequence

| Step | What | Operations Used |
|------|------|-----------------|
| 1 | Add SDK NuGet package to Infrastructure project | — |
| 2 | Add `Maxio:` config section + environment variable binding | — |
| 3 | Register `MaxioAdvancedBillingClient` as singleton via DI | — |
| 4 | Create `IMaxioCustomerService` interface + implementation in ApplicationCore | `ReadCustomerByReference`, `CreateCustomer` |
| 5 | Create `IMaxioSubscriptionService` interface + implementation in ApplicationCore | `ListProductsForProductFamily`, `ReadProductByHandle`, `CreateSubscription`, `ListCustomerSubscriptions`, `ReadCustomerByReference` |
| 6 | Add endpoint: `GET /api/subscription-plans` | `ListProductsForProductFamily` |
| 7 | Add endpoint: `POST /api/subscriptions` | `ReadCustomerByReference`, `CreateCustomer`, `CreateSubscription` |
| 8 | Add endpoint: `GET /api/my-subscriptions` | `ReadCustomerByReference`, `ListCustomerSubscriptions` |
| 9 | Add DTOs and endpoint request/response types | — |

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### SDK Client Construction

| Fact | Value | Source |
|------|-------|--------|
| NuGet package | `AsadAli.AdvancedBilling.Sdk` | `sdk-map.md` |
| Root namespace | `MaxioAdvancedBilling` | `sdk-map.md` |
| Client class | `MaxioAdvancedBillingClient` | `sdk-map.md` |
| Options class | `MaxioAdvancedBillingClientOptions` | `sdk-map.md` |
| Constructor | `new MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` |
| DI registration | `services.AddMaxioAdvancedBillingClient(o => { ... })` | `sdk-map.md` |
| Auth | Basic: `Username = API_KEY`, `Password = "x"` | `sdk-map.md` |
| Environment enum | `ServerEnvironment.Us` or `ServerEnvironment.Eu` | `sdk-map.md` — `Servers/ServerEnvironment.cs` |
| Server override | `options.Server.Production.Us.BaseUrl = "..."` | `sdk-map.md` |
| Options properties | `Environment: ServerEnvironment`, `Retry: RetryOptions`, `Server: ServerOptions`, `BasicAuth: BasicAuthCredentials?` | `sdk-map.md` — `MaxioAdvancedBillingClientOptions.cs` |
| `BasicAuthCredentials` namespace | `MaxioAdvancedBilling.Core.Authentication.Basic` | `sdk-map.md` |
| `ServerEnvironment` namespace | `MaxioAdvancedBilling.Servers` | `sdk-map.md` |

### Operation: ListProductsForProductFamily

| Fact | Value | Source |
|------|-------|--------|
| Controller | `client.ProductFamilies` | `map/operations/ProductFamilies.md` |
| HTTP | `GET /product_families/{product_family_id}/products.json` | `map/operations/ProductFamilies.md` |
| Signature | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | `map/operations/ProductFamilies.md` |
| Must-pass-explicitly | `dateField`, `filter`, `startDate`, `endDate`, `startDatetime`, `endDatetime`, `includeArchived`, `include` — all nullable, no default → **pass `null` to skip** | `map/operations/ProductFamilies.md` |
| Returns | `IReadOnlyList<ProductResponse>` | `map/operations/ProductFamilies.md` |
| Error | `SdkException<ListProductsForProductFamilyError>` — **Case A (typed)** | `map/operations/ProductFamilies.md` |
| Error accessors | `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | `map/operations/ProductFamilies.md` |
| Pagination | manual `page` + `perPage` | `map/operations/ProductFamilies.md` |

### Operation: ReadProductByHandle

| Fact | Value | Source |
|------|-------|--------|
| Controller | `client.Products` | `map/operations/Products.md` |
| HTTP | `GET /products/handle/{api_handle}.json` | `map/operations/Products.md` |
| Signature | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | `map/operations/Products.md` |
| Returns | `ProductResponse` | `map/operations/Products.md` |
| Error | `SdkException<RawError>` — **Case B** | `map/operations/Products.md` |
| Error accessors | `StatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()` | `map/operations/Products.md` |

### Operation: CreateCustomer

| Fact | Value | Source |
|------|-------|--------|
| Controller | `client.Customers` | `map/operations/Customers.md` |
| HTTP | `POST /customers.json` | `map/operations/Customers.md` |
| Signature | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` | `map/operations/Customers.md` |
| `body` | nullable, no default → **must pass explicitly** | `map/operations/Customers.md` |
| Returns | `CustomerResponse` | `map/operations/Customers.md` |
| Error | `SdkException<CreateCustomerError>` — **Case A (typed)** | `map/operations/Customers.md` |
| Error accessors | `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | `map/operations/Customers.md` |

### Operation: ReadCustomerByReference

| Fact | Value | Source |
|------|-------|--------|
| Controller | `client.Customers` | `map/operations/Customers.md` |
| HTTP | `GET /customers/lookup.json` | `map/operations/Customers.md` |
| Signature | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | `map/operations/Customers.md` |
| Query param | `reference` ← `reference` | `map/operations/Customers.md` |
| Returns | `CustomerResponse` | `map/operations/Customers.md` |
| Error | `SdkException<RawError>` — **Case B** | `map/operations/Customers.md` |
| Error accessors | `StatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()` | `map/operations/Customers.md` |

### Operation: CreateSubscription

| Fact | Value | Source |
|------|-------|--------|
| Controller | `client.Subscriptions` | `map/operations/Subscriptions.md` |
| HTTP | `POST /subscriptions.json` | `map/operations/Subscriptions.md` |
| Signature | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | `map/operations/Subscriptions.md` |
| `body` | nullable, no default → **must pass explicitly** | `map/operations/Subscriptions.md` |
| Returns | `SubscriptionResponse` | `map/operations/Subscriptions.md` |
| Error | `SdkException<CreateSubscriptionError>` — **Case A (typed)** | `map/operations/Subscriptions.md` |
| Error accessors | `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | `map/operations/Subscriptions.md` |

### Operation: ListCustomerSubscriptions

| Fact | Value | Source |
|------|-------|--------|
| Controller | `client.Customers` | `map/operations/Customers.md` |
| HTTP | `GET /customers/{customer_id}/subscriptions.json` | `map/operations/Customers.md` |
| Signature | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | `map/operations/Customers.md` |
| Returns | `IReadOnlyList<SubscriptionResponse>` | `map/operations/Customers.md` |
| Error | `SdkException<RawError>` — **Case B** | `map/operations/Customers.md` |
| Error accessors | `StatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()` | `map/operations/Customers.md` |

### Request Models

#### CreateCustomerRequest → CreateCustomer (inner)

| Field (wire_name) | Type | Required? | Source |
|--------------------|------|-----------|--------|
| `Customer (customer)` | `CreateCustomer` | `!req` | `records-1-Ac-Cr.md` |

#### CreateCustomer inner record

| Field (wire_name) | Type | Required? | Source |
|--------------------|------|-----------|--------|
| `FirstName (first_name)` | `string` | `!req` | `records-1-Ac-Cr.md` |
| `LastName (last_name)` | `string` | `!req` | `records-1-Ac-Cr.md` |
| `Email (email)` | `string` | `!req` | `records-1-Ac-Cr.md` |
| `CcEmails (cc_emails)` | `string?` | optional | `records-1-Ac-Cr.md` |
| `Organization (organization)` | `string?` | optional | `records-1-Ac-Cr.md` |
| `Reference (reference)` | `string?` | optional | `records-1-Ac-Cr.md` |
| `Address (address)` | `string?` | optional | `records-1-Ac-Cr.md` |
| `Address2 (address_2)` | `string?` | optional | `records-1-Ac-Cr.md` |
| `City (city)` | `string?` | optional | `records-1-Ac-Cr.md` |
| `State (state)` | `string?` | optional | `records-1-Ac-Cr.md` |
| `Zip (zip)` | `string?` | optional | `records-1-Ac-Cr.md` |
| `Country (country)` | `string?` | optional | `records-1-Ac-Cr.md` |
| `Phone (phone)` | `string?` | optional | `records-1-Ac-Cr.md` |
| `Locale (locale)` | `string?` | optional | `records-1-Ac-Cr.md` |
| `VatNumber (vat_number)` | `string?` | optional | `records-1-Ac-Cr.md` |
| `TaxExempt (tax_exempt)` | `bool?` | optional | `records-1-Ac-Cr.md` |
| `TaxExemptReason (tax_exempt_reason)` | `string?` | optional | `records-1-Ac-Cr.md` |
| `ParentId (parent_id)` | `int?` | optional | `records-1-Ac-Cr.md` |
| `SalesforceId (salesforce_id)` | `string?` | optional | `records-1-Ac-Cr.md` |

#### CreateSubscriptionRequest → CreateSubscription (inner)

| Field (wire_name) | Type | Required? | Source |
|--------------------|------|-----------|--------|
| `Subscription (subscription)` | `CreateSubscription` | `!req` | `records-2-Cr-Ne.md` |

#### CreateSubscription inner record — fields used in this integration

| Field (wire_name) | Type | Required? | Source |
|--------------------|------|-----------|--------|
| `ProductHandle (product_handle)` | `string?` | optional | `records-2-Cr-Ne.md` |
| `ProductId (product_id)` | `int?` | optional | `records-2-Cr-Ne.md` |
| `CustomerReference (customer_reference)` | `string?` | optional | `records-2-Cr-Ne.md` |
| `CustomerId (customer_id)` | `int?` | optional | `records-2-Cr-Ne.md` |
| `PaymentCollectionMethod (payment_collection_method)` | `CollectionMethod?` | optional | `records-2-Cr-Ne.md` |
| `Reference (reference)` | `string?` | optional | `records-2-Cr-Ne.md` |

**Note:** The `CreateSubscription` record has many more optional fields. Only the ones used in this integration are listed above. Others (e.g., `coupon_code`, `components`, `payment_profile_id`) are omitted and will default to null.

### Response Models

#### ProductResponse (wrapping `Product`)

| Field (wire_name) | Type | Source |
|--------------------|------|--------|
| `Product (product)` | `Product !req` | `records-3-Of-Su.md` |

#### Product inner record — fields used

| Field (wire_name) | Type | Source |
|--------------------|------|--------|
| `Id (id)` | `int?` | `records-3-Of-Su.md` |
| `Name (name)` | `string?` | `records-3-Of-Su.md` |
| `Handle (handle)` | `string?` | `records-3-Of-Su.md` |
| `Description (description)` | `string?` | `records-3-Of-Su.md` |
| `PriceInCents (price_in_cents)` | `long?` | `records-3-Of-Su.md` |
| `Interval (interval)` | `int?` | `records-3-Of-Su.md` |
| `IntervalUnit (interval_unit)` | `IntervalUnit?` | `records-3-Of-Su.md` |
| `ProductFamily (product_family)` | `ProductFamily?` | `records-3-Of-Su.md` |

#### CustomerResponse (wrapping `Customer`)

| Field (wire_name) | Type | Source |
|--------------------|------|--------|
| `Customer (customer)` | `Customer !req` | `records-2-Cr-Ne.md` |

#### Customer inner record — fields used

| Field (wire_name) | Type | Source |
|--------------------|------|--------|
| `Id (id)` | `int?` | `records-2-Cr-Ne.md` |
| `FirstName (first_name)` | `string?` | `records-2-Cr-Ne.md` |
| `LastName (last_name)` | `string?` | `records-2-Cr-Ne.md` |
| `Email (email)` | `string?` | `records-2-Cr-Ne.md` |
| `Reference (reference)` | `string?` | `records-2-Cr-Ne.md` |

#### SubscriptionResponse (wrapping `Subscription`)

| Field (wire_name) | Type | Source |
|--------------------|------|--------|
| `Subscription (subscription)` | `Subscription?` | `records-4-Su-We.md` |

#### Subscription inner record — fields used

| Field (wire_name) | Type | Source |
|--------------------|------|--------|
| `Id (id)` | `int?` | `records-3-Of-Su.md` |
| `State (state)` | `SubscriptionState?` | `records-3-Of-Su.md` |
| `CurrentPeriodEndsAt (current_period_ends_at)` | `DateTimeOffset?` | `records-3-Of-Su.md` |
| `NextAssessmentAt (next_assessment_at)` | `DateTimeOffset?` | `records-3-Of-Su.md` |
| `ActivatedAt (activated_at)` | `DateTimeOffset?` | `records-3-Of-Su.md` |
| `CanceledAt (canceled_at)` | `DateTimeOffset?` | `records-3-Of-Su.md` |
| `CreatedAt (created_at)` | `DateTimeOffset?` | `records-3-Of-Su.md` |
| `Product (product)` | `Product?` | `records-3-Of-Su.md` |
| `Customer (customer)` | `Customer?` | `records-3-Of-Su.md` |
| `PaymentCollectionMethod (payment_collection_method)` | `CollectionMethod?` | `records-3-Of-Su.md` |

### Enums Used

| Enum | Members Used | Namespace | Source |
|------|-------------|-----------|--------|
| `SubscriptionState` | `Active`, `Trialing`, `Pending`, `Canceled`, `Expired`, `PastDue`, `Paused`, `Suspended` | `MaxioAdvancedBilling.Models.Enums` | `enums.md` |
| `CollectionMethod` | `Automatic`, `Invoice` | `MaxioAdvancedBilling.Models.Enums` | `enums.md` |
| `IntervalUnit` | `Day`, `Month` | `MaxioAdvancedBilling.Models.Enums` | `enums.md` |

### Namespaces Required

| Content | Namespace |
|---------|-----------|
| Client + options | `MaxioAdvancedBilling` |
| Basic auth credentials | `MaxioAdvancedBilling.Core.Authentication.Basic` |
| Server environment | `MaxioAdvancedBilling.Servers` |
| Models (records) | `MaxioAdvancedBilling.Models` |
| Enums | `MaxioAdvancedBilling.Models.Enums` |
| Error types | `MaxioAdvancedBilling.Errors` |
| Retry options | `MaxioAdvancedBilling.Core.Configuration` |

### Client Wiring (DI)

```csharp
// In Program.cs or Infrastructure/Dependencies.cs
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;

services.AddMaxioAdvancedBillingClient(o =>
{
    o.BasicAuth = new BasicAuthCredentials
    {
        Username = config["Maxio:ApiKey"]!,
        Password = "x"
    };
    o.Environment = config["Maxio:Environment"] == "EU"
        ? ServerEnvironment.Eu
        : ServerEnvironment.Us;
    // Optional base URL override
    var baseUrl = config["Maxio:BaseUrl"];
    if (!string.IsNullOrEmpty(baseUrl))
    {
        o.Server.Production.Us.BaseUrl = baseUrl;
    }
    // Set subdomain
    o.Server.Production.Us.Site = config["Maxio:Subdomain"]!;
});
```

**Note:** The exact `Server.Production.Us.Site` assignment is from `sdk-map.md`. The implementer MUST load `dotnet-client-initialization` and `dotnet-configuration-resilience` before wiring this — the SDK map documents the shape but the companion skills document the traps (HttpClient lifetime, retry semantics, etc.).

---

## 3. Trap Notes

⚠ **Step 3 (client registration)** — The SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register. Transport failures on POST are retried on every verb including non-idempotent ones, and `MaxRetries = 0` is rejected at construction (floor is 1). **MUST load `dotnet-configuration-resilience`** before wiring the client.

⚠ **Step 3 (client registration)** — The `AddMaxioAdvancedBillingClient` DI extension manages the `HttpClient`/handler pipeline. It must be long-lived and reused, not rebuilt per request. The SDK client wrapper may be transient. **MUST load `dotnet-client-initialization`** before registering.

⚠ **Step 5 (service implementation)** — Every SDK operation is throw-only — there are no `…Result` no-throw variants. The boundary must catch `SdkException<TError>` (Case A) and `SdkException<RawError>` (Case B). **MUST load `dotnet-error-handling`** before writing the service layer.

⚠ **Step 5 (service implementation)** — `ReadCustomerByReference` throws Case B (`SdkException<RawError>`) on 404. To detect "customer not found" you must check `ex.Error.StatusCode == HttpStatusCode.NotFound` inside the catch block, not use a typed accessor. **MUST load `dotnet-error-handling`** before implementing this pattern.

⚠ **Step 5 (service implementation)** — Request models are immutable records with `init`-only setters. Construct via object initializer syntax; required fields (`!req`) must be set or the compiler will error. Nullable fields can be omitted. **MUST load `dotnet-models`** before building request payloads.

⚠ **Step 6/7/8 (endpoints)** — `ListProductsForProductFamily` has 8 nullable no-default params that must be passed explicitly (pass `null` to skip). In a positional call these mis-bind. Use named arguments. **MUST load `dotnet-calling-endpoints`** before writing the call.

⚠ **Step 7 (subscribe endpoint)** — The subscription creation flow calls `ReadCustomerByReference` (Case B) then `CreateCustomer` (Case A) then `CreateSubscription` (Case A). Each has a different error case. A boundary that only catches `SdkException<CreateSubscriptionError>` will miss the raw errors from the customer lookup. **MUST load `dotnet-error-handling`**.

---

## 4. File Layout (New & Modified)

### New files:

| File | Purpose |
|------|---------|
| `src/ApplicationCore/Interfaces/IMaxioCustomerService.cs` | Interface for customer ensure-or-create |
| `src/ApplicationCore/Interfaces/IMaxioSubscriptionService.cs` | Interface for subscription operations |
| `src/Infrastructure/Services/MaxioCustomerService.cs` | Customer service implementation |
| `src/Infrastructure/Services/MaxioSubscriptionService.cs` | Subscription service implementation |
| `src/PublicApi/SubscriptionEndpoints/SubscriptionPlanListEndpoint.cs` | GET /api/subscription-plans |
| `src/PublicApi/SubscriptionEndpoints/SubscriptionPlanListEndpoint.ListSubscriptionPlanResponse.cs` | Response DTO |
| `src/PublicApi/SubscriptionEndpoints/SubscriptionPlanDto.cs` | Plan DTO |
| `src/PublicApi/SubscriptionEndpoints/SubscriptionCreateEndpoint.cs` | POST /api/subscriptions |
| `src/PublicApi/SubscriptionEndpoints/SubscriptionCreateEndpoint.CreateSubscriptionRequest.cs` | Request DTO |
| `src/PublicApi/SubscriptionEndpoints/SubscriptionCreateEndpoint.CreateSubscriptionResponse.cs` | Response DTO |
| `src/PublicApi/SubscriptionEndpoints/SubscriptionListEndpoint.cs` | GET /api/my-subscriptions |
| `src/PublicApi/SubscriptionEndpoints/SubscriptionListEndpoint.ListMySubscriptionsResponse.cs` | Response DTO |
| `src/PublicApi/SubscriptionEndpoints/SubscriptionDto.cs` | Subscription DTO |

### Modified files:

| File | Change |
|------|--------|
| `src/Infrastructure/Infrastructure.csproj` | Add `AsadAli.AdvancedBilling.Sdk` package reference |
| `src/Infrastructure/Dependencies.cs` | Register `MaxioAdvancedBillingClient` + services |
| `src/PublicApi/appsettings.json` | Add `Maxio` config section (template) |
| `src/PublicApi/MappingProfile.cs` | Add Maxio DTO mappings (if using AutoMapper) |

---

## 5. Assumptions & Blockers

1. **Product family `eshop-subscribe` already exists** in the Maxio sandbox with the products (handles `eshop-pro`, `basic-plan`) and the metered component (`api-call`) pre-configured. If not, the `GET /api/subscription-plans` endpoint will return an empty list.

2. **No payment gateway is configured** in the sandbox for actual card processing. The `CreateSubscription` call may fail at runtime if the product requires a credit card and no payment profile is provided. For sandbox testing, use products configured with `require_credit_card: false` or use the `bogus` gateway. This is an environment concern, not a code blocker.

3. **The `ListProductsForProductFamily` operation takes `productFamilyId` as a `string`** but it is an ID or handle in the URL path. We will pass the configured product family handle (e.g., `"eshop-subscribe"`) as the `productFamilyId` parameter.

4. **Customer identity mapping**: We will use the authenticated user's email as the `reference` field when creating/looking up Maxio customers. This is the natural idempotency key — one Maxio customer per app user.

5. **The `Subscription` inner record's `Product` and `Customer` fields are nullable** (`Product?`, `Customer?`). The subscription list endpoint must handle null product/customer gracefully.

6. **The `CreateSubscription` record's `CustomerReference` field** is used to link to an existing Maxio customer without needing to know the Maxio customer ID upfront. We set this to the user's email reference.

---

## 6. Implementation Details Per Step

### Step 1: NuGet Package

Add to `src/Infrastructure/Infrastructure.csproj`:
```xml
<PackageReference Include="AsadAli.AdvancedBilling.Sdk" />
```

### Step 2: Configuration

Add to `src/PublicApi/appsettings.json`:
```json
{
  "Maxio": {
    "ApiKey": "",
    "Subdomain": "",
    "Environment": "US",
    "ProductFamilyHandle": "eshop-subscribe",
    "BaseUrl": ""
  }
}
```

Environment variables: `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_ENVIRONMENT`, `MAXIO_DEFAULT_PRODUCT_FAMILY`

### Step 3: DI Registration

In `src/Infrastructure/Dependencies.cs`, add after existing registrations:
- Register `MaxioAdvancedBillingClient` as singleton via `AddMaxioAdvancedBillingClient`
- Register `IMaxioCustomerService` as scoped
- Register `IMaxioSubscriptionService` as scoped

### Steps 4-5: Service Layer

**IMaxioCustomerService** — methods:
- `Task<CustomerResponse> EnsureCustomerExistsAsync(string email, string firstName, string lastName, CancellationToken ct)`
  - Calls `ReadCustomerByReference(email)` — if found, return existing
  - If 404, call `CreateCustomer(...)` with reference=email
  - If other error, propagate

**IMaxioSubscriptionService** — methods:
- `Task<IReadOnlyList<Product>> ListPlansAsync(CancellationToken ct)`
  - Calls `ListProductsForProductFamily(productFamilyHandle, null, null, null, null, null, null, null, null, ct:)`
  - Extracts `.Product` from each `ProductResponse`
  - Filters to product family handle match

- `Task<Subscription> SubscribeAsync(string email, string productHandle, string? reference, CancellationToken ct)`
  - Calls `EnsureCustomerExistsAsync(email, ...)` to get/create customer
  - Builds `CreateSubscriptionRequest` with `ProductHandle = productHandle`, `CustomerReference = email`
  - Calls `CreateSubscription(body, ct:)`
  - Returns `.Subscription` from response

- `Task<IReadOnlyList<Subscription>> ListMySubscriptionsAsync(string email, CancellationToken ct)`
  - Calls `ReadCustomerByReference(email)` to get customer ID
  - Calls `ListCustomerSubscriptions(customerId, ct:)`
  - Extracts `.Subscription` from each `SubscriptionResponse`

### Steps 6-8: Endpoints

Follow the MinimalApi.Endpoint pattern (existing in `CatalogItemEndpoints/`):
- Each endpoint implements `IEndpoint<IResult, ...>`
- Has `AddRoute(IEndpointRouteBuilder app)` and `HandleAsync(...)` methods
- Request/Response as nested partial classes in separate files
- DTOs as flat classes in the endpoint folder

### Step 9: DTOs

**SubscriptionPlanDto:**
- `int Id`, `string Name`, `string Handle`, `string Description`, `decimal PriceInCents`, `int Interval`, `string IntervalUnit`

**SubscriptionDto:**
- `int Id`, `string State`, `string ProductName`, `DateTimeOffset? CurrentPeriodEndsAt`, `DateTimeOffset? NextAssessmentAt`, `DateTimeOffset? ActivatedAt`, `DateTimeOffset? CanceledAt`

---

## 7. Error Handling Strategy

| SDK Operation | Error Case | Detection |
|---------------|-----------|-----------|
| `ReadCustomerByReference` | Case B (`SdkException<RawError>`) | Check `ex.Error.StatusCode == 404` for "not found" |
| `CreateCustomer` | Case A (`SdkException<CreateCustomerError>`) | `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` for validation errors |
| `ListProductsForProductFamily` | Case A (`SdkException<ListProductsForProductFamilyError>`) | `TryGetString(out string)` for 404 |
| `CreateSubscription` | Case A (`SdkException<CreateSubscriptionError>`) | `TryGetErrorListResponse1(out ErrorListResponse1)` for validation errors |
| `ListCustomerSubscriptions` | Case B (`SdkException<RawError>`) | Status code + body inspection |

All errors must be caught at the endpoint level and translated to appropriate HTTP responses (400, 404, 502) with descriptive messages. The `dotnet-error-handling` skill MUST be loaded to understand the full pattern.

---

## 8. REQUIRED READING

The following `dotnet-*` skills MUST be loaded before implementation starts. The sheet deliberately does not carry their contents.

| Skill | Governs |
|-------|---------|
| `dotnet-client-initialization` | Step 3: client construction, `AddMaxioAdvancedBillingClient`, HttpClient lifetime |
| `dotnet-configuration-resilience` | Step 3: retry semantics, timeout bounds, base URL override |
| `dotnet-authentication` | Step 3: Basic auth credential wiring |
| `dotnet-calling-endpoints` | Steps 4-5: named arguments for nullable-no-default params |
| `dotnet-models` | Steps 4-5: record construction, `!req` fields, init-only setters |
| `dotnet-error-handling` | Steps 4-8: `SdkException<T>` catch pattern, Case A vs B, `TryGet…` accessors |
| `dotnet-testing` | Step 9: test seam via `HttpClient` constructor argument |

**Hazard rows (mandatory — `System.Text.Json.JsonException` reaches the boundary from two directions):**

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;

- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary. These rows belong in the FIRST sheet, not a later revision: the boundary is written early, and a caveat that arrives afterwards arrives too late to shape it.
