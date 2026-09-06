# Maxio Advanced Billing Integration Plan — eShopOnWeb Subscriptions

## Scope & Sequence

1. **Infrastructure setup** — DI registration, HTTP client, authentication configuration
2. **Create/lookup customer** — Idempotent Maxio customer per eShopOnWeb user
3. **Implement GET /api/subscription-plans** — List products in `eshop-subscribe` family
4. **Implement POST /api/subscriptions** — Create subscription for logged-in user
5. **Implement GET /api/my-subscriptions** — List subscriptions for logged-in customer
6. **Error handling boundary** — Serialize SDK exceptions to HTTP responses

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Products.ListProducts

| Aspect | Value |
|--------|-------|
| **Controller** | `client.Products` |
| **Method signature** | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| **Required params** (explicit pass required) | All optional/nullable params (8 before pagination): pass `null` if not filtering; pass `null` for `include`; can omit `page` and `perPage` to use defaults (1, 20) |
| **Request body** | None (GET operation) |
| **Response envelope** | `IReadOnlyList<ProductResponse>` (array, not wrapped object) |
| **Response inner type** | `ProductResponse` → `Product (product): Product !req`; extract `resp[i].Product` for each item |
| **Product fields (essential)** | `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `ProductFamily (product_family): ProductFamily?` |
| **Error type** | `SdkException<RawError>` — **Case B** (no typed accessors) |
| **Error accessors** | `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`, `ReadAsBytes(): ReadOnlyMemory<byte>` |
| **Pagination** | Manual via `page` (1-indexed) and `perPage` (default 20) query params; no metadata in response |
| **Notes** | Lists products belonging to a site. For the scope, filter by product family ID 3023074 via query after the call (or use product handles `eshop-pro`, `basic-plan`). |
| **Source** | `operations/Products.md` |

### Customers.CreateCustomer

| Aspect | Value |
|--------|-------|
| **Controller** | `client.Customers` |
| **Method signature** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` |
| **Required params** | `body` — nullable, no default → **must pass explicitly** (envelope wrapping `Customer` field) |
| **Request envelope** | `CreateCustomerRequest` → `Customer (customer): Customer !req` |
| **Request inner model** | `Customer`: `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?`, `Organization (organization): string?`, `Address (address): string?`, `Address2 (address_2): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `Verified (verified): bool?`, `TaxExempt (tax_exempt): bool?`, `VatNumber (vat_number): string?`, `Metafields (metafields): IReadOnlyDictionary<string, string>?`, `ParentId (parent_id): int?`, `SalesforceId (salesforce_id): string?`, `DefaultAutoRenewalProfileId (default_auto_renewal_profile_id): int?` |
| **Minimal request for idempotent key** | Set `Reference (reference): string?` to eShopOnWeb user ID or email (unique identifier). Omit payment info. Required: email is not marked required in the model, but the Notes say "reference value must be unique" and recommend using your app's ID. |
| **Response envelope** | `CustomerResponse` → `Customer (customer): Customer !req` |
| **Response inner fields (extract from Customer)** | `Id (id): int?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?` |
| **Error type** | `SdkException<CreateCustomerError>` — **Case A (typed)** |
| **Error accessors** | `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422], `TryGetRawError(out RawError)` [fallback] |
| **Error payload shapes** | `CustomerErrorResponse1` → `Errors (errors): Errors?`; `Errors` → `PerPage (per_page): IReadOnlyList<string>?`, `PricePoint (price_point): IReadOnlyList<string>?` |
| **Notes** | "The only validation restriction is that you may only create one customer for a given reference value." Use `Reference` field set to app user ID for idempotency: `ReadCustomerByReference(reference, ct)` first; if 404, create. ISO country codes (2 chars), ISO state codes (2-3 chars) required if address supplied. |
| **Source** | `operations/Customers.md` |

### Customers.ReadCustomerByReference

| Aspect | Value |
|--------|-------|
| **Controller** | `client.Customers` |
| **Method signature** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` |
| **Required params** | `reference` (query string param, wire name `reference`) — non-nullable, no default → **must pass explicitly** |
| **Request body** | None (GET operation) |
| **Response envelope** | `CustomerResponse` → `Customer (customer): Customer !req` |
| **Response inner fields** | Same as CreateCustomer response: `Id`, `FirstName`, `LastName`, `Email`, `Reference`, `CreatedAt`, `UpdatedAt` |
| **Error type** | `SdkException<RawError>` — **Case B** |
| **Error accessors** | `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` |
| **Notes** | Returns a single match by reference ID. If no match, HTTP 404 is raised (error via `SdkException<RawError>`). Use to check idempotent customer creation. |
| **Source** | `operations/Customers.md` |

### Subscriptions.CreateSubscription

| Aspect | Value |
|--------|-------|
| **Controller** | `client.Subscriptions` |
| **Method signature** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` |
| **Required params** | `body` — nullable, no default → **must pass explicitly** (envelope wrapping `Subscription` field) |
| **Request envelope** | `CreateSubscriptionRequest` → `Subscription (subscription): CreateSubscription !req` |
| **Request inner model (CreateSubscription)** | A large model with many optional fields. **Minimal for the scope**: `ProductId (product_id): int?` OR `ProductHandle (product_handle): string?`, `CustomerId (customer_id): int?` OR `CustomerReference (customer_reference): string?` OR `CustomerAttributes (customer_attributes): CustomerAttributes?`. No payment method required per scope. Key optional fields: `Reference (reference): string?` (for subscription tracking), `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` |
| **Note on payment** | "Payment information may be required to create a subscription, depending on the options for the Product being subscribed." Scope says no payment method required; if product config requires it, that is a **Blocker** (§5). |
| **Response envelope** | `SubscriptionResponse` → `Subscription (subscription): Subscription?` |
| **Response inner fields (essential)** | `Id (id): int?`, `State (state): SubscriptionState?`, `BalanceInCents (balance_in_cents): long?`, `TotalRevenueInCents (total_revenue_in_cents): long?`, `ProductId (product_id): int?`, `ProductHandle (product_handle): string?`, `CustomerId (customer_id): int?`, `Reference (reference): string?`, `CurrentPeriodStartsAt (current_period_starts_at): DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `CanceledAt (canceled_at): DateTimeOffset?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?` |
| **Error type** | `SdkException<CreateSubscriptionError>` — **Case A (typed)** |
| **Error accessors** | `TryGetErrorListResponse1(out ErrorListResponse1)` [422], `TryGetRawError(out RawError)` [fallback] |
| **Error payload shapes** | `ErrorListResponse1` → `Errors (errors): IReadOnlyList<string> !req` |
| **Pagination** | None |
| **Notes** | "Specify the product with `product_id` or `product_handle`. Identify an existing customer with `customer_id` or `customer_reference`." On success, subscription enters state from product config (e.g., `active`, `trialing`). State from `response.Subscription.State` (type `SubscriptionState?`). |
| **Source** | `operations/Subscriptions.md` |

### Customers.ListCustomerSubscriptions

| Aspect | Value |
|--------|-------|
| **Controller** | `client.Customers` |
| **Method signature** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` |
| **Required params** | `customerId` (path param) — non-nullable int, no default → **must pass explicitly** |
| **Request body** | None (GET operation) |
| **Response envelope** | `IReadOnlyList<SubscriptionResponse>` (array, not wrapped object) |
| **Response inner type** | `SubscriptionResponse` → `Subscription (subscription): Subscription?`; extract `resp[i].Subscription` for each item |
| **Subscription fields (essential)** | `Id (id): int?`, `State (state): SubscriptionState?`, `ProductId (product_id): int?`, `ProductHandle (product_handle): string?`, `Reference (reference): string?`, `CurrentPeriodStartsAt (current_period_starts_at): DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `CreatedAt (created_at): DateTimeOffset?`, `CanceledAt (canceled_at): DateTimeOffset?` |
| **Error type** | `SdkException<RawError>` — **Case B** |
| **Error accessors** | `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` |
| **Pagination** | None (endpoint returns all, no page/per_page params) |
| **Notes** | Lists all subscriptions that belong to a customer. Returns an empty array if customer has no subscriptions (not an error). |
| **Source** | `operations/Customers.md` |

---

### Enum Values (wire names)

**CollectionMethod** (namespace `MaxioAdvancedBilling.Models.Enums`)
- `CollectionMethod.Automatic` (wire: `"automatic"`) — default, billed automatically
- `CollectionMethod.Invoice` (wire: `"invoice"`)
- `CollectionMethod.Remittance` (wire: `"remittance"`)
- `CollectionMethod.Prepaid` (wire: `"prepaid"`)

**SubscriptionState** (namespace `MaxioAdvancedBilling.Models.Enums`)
- `SubscriptionState.Pending` (wire: `"pending"`)
- `SubscriptionState.Active` (wire: `"active"`)
- `SubscriptionState.Trialing` (wire: `"trialing"`)
- `SubscriptionState.Assessing` (wire: `"assessing"`)
- `SubscriptionState.PastDue` (wire: `"past_due"`)
- `SubscriptionState.Suspended` (wire: `"suspended"`)
- `SubscriptionState.Canceled` (wire: `"canceled"`)
- `SubscriptionState.Expired` (wire: `"expired"`)
- (See `enums.md` for full list)

**IntervalUnit** (namespace `MaxioAdvancedBilling.Models.Enums`)
- `IntervalUnit.Day` (wire: `"day"`)
- `IntervalUnit.Month` (wire: `"month"`)

---

### Client Construction & Configuration

**SDK Identity**
- Package: `AsadAli.AdvancedBilling.Sdk`
- Root namespace: `MaxioAdvancedBilling`
- Client class: `MaxioAdvancedBillingClient`
- Options class: `MaxioAdvancedBillingClientOptions`
- Auth: HTTP Basic — username = API key, password = literal `"x"`

**Minimal DI / construction (from config)**
```csharp
// Bind at registration time:
var apiKey = configuration["Maxio:ApiKey"];
var subdomain = configuration["Maxio:Subdomain"];
var baseUrl = configuration["Maxio:BaseUrl"]; // optional override

// Server setup:
var environment = ServerEnvironment.Us; // or .Eu; maps to subdomain in US: https://{subdomain}.chargify.com
var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" },
    Environment = environment,
};
if (!string.IsNullOrEmpty(baseUrl))
{
    options.Server.Production.Us.BaseUrl = baseUrl; // override for sandbox
}
var client = new MaxioAdvancedBillingClient(httpClient, options); // httpClient from IHttpClientFactory
```

---

### Error Handling Patterns

**Case A (typed)** — Operations `CreateCustomer`, `CreateSubscription`  
```csharp
try
{
    var resp = await client.Subscriptions.CreateSubscription(body, ct);
}
catch (SdkException<CreateSubscriptionError> ex)
{
    if (ex.Error.TryGetErrorListResponse1(out var errorList))
    {
        var messages = string.Join("; ", errorList.Errors ?? []); // IReadOnlyList<string>
        // log / return 422
    }
    else if (ex.Error.TryGetRawError(out var raw))
    {
        // fallback: read HTTP status and body
        var status = raw.StatusCode;
        var body = raw.ReadAsString();
    }
}
```

**Case B (raw)** — Operations `ListProducts`, `ListCustomerSubscriptions`, `ReadCustomerByReference`  
```csharp
try
{
    var resp = await client.Customers.ListCustomerSubscriptions(customerId, ct);
}
catch (SdkException<RawError> ex)
{
    var status = ex.Error.StatusCode;
    var body = ex.Error.ReadAsString();
    // no typed accessors; read HTTP status + body directly
}
```

**JsonException caveat (MUST load `dotnet-error-handling` before writing boundary)**
- A drifted 2xx body (missing `required` member) surfaces as `JsonException` from deserialization, **not** as `SdkException` — SDK-exception-only catch lets it escape; boundary must handle it.
- A non-2xx body that does not match the operation's generated error shape throws `JsonException` *while constructing the error object*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed — map all `JsonException` to 5xx deterministically, never retry 5xx on malformed error bodies.

---

## Trap Notes

⚠ **Step 2 (customer create/lookup)** — `ReadCustomerByReference` raises HTTP 404 as `SdkException<RawError>` (Case B), not as a thrown exception with a specific error type. Catch the exception and check `ex.Error.StatusCode == HttpStatusCode.NotFound` to detect "customer not found"; do not rely on exception type to differentiate 404 from other errors. **MUST load `dotnet-error-handling`** to learn Case A/B wiring and the `TryGet…` pattern.

⚠ **Step 1 (client registration)** — The SDK's `RetryOptions` do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register. The `Timeout` setting is per-attempt; there is no built-in total-call timeout. `HttpMethodsToRetry` gates only the **status** trigger (e.g., 503), so a transport failure (`HttpRequestException`) on a `POST` is retried regardless and a non-idempotent write can execute more than once. **MUST load `dotnet-configuration-resilience`** before wiring retries or timeouts.

⚠ **Step 3 (subscription creation)** — The `CreateSubscription` request model carries dozens of optional fields. The scope requires `ProductId`/`ProductHandle` and `CustomerId`/`CustomerReference`. If the product config requires a payment method and one is not provided, the API returns 422. Test in sandbox (cp-exp-3) first. **MUST load `dotnet-calling-endpoints`** to understand named-vs-positional argument binding for operations with many optional params.

⚠ **Step 4 (list subscriptions response parsing)** — `ListCustomerSubscriptions` returns `IReadOnlyList<SubscriptionResponse>` directly (array, not a wrapper object). Iterate and extract `item.Subscription` (type `Subscription?`, nullable). A missing `Subscription` field should not occur for a successful 2xx, but defensive code should handle null. **MUST load `dotnet-models`** for union/enum/response-envelope shapes.

⚠ **Step 5 (HTTP boundary)** — Both `System.Text.Json.JsonException` sources must be handled:
  - Drifted 2xx body (missing `required` field) → `JsonException` from deserialization (not `SdkException`)
  - Non-2xx body mismatched to operation error type → `JsonException` *during* error construction (replaces `SdkException`, status lost)
  
  A boundary that maps only `SdkException` to responses lets the first escape; one that maps `JsonException` to a fixed 5xx then retries 5xx retries something that will never succeed. **MUST load `dotnet-error-handling`** before implementing the boundary.

---

## REQUIRED READING

Before implementation starts, load each of these skills in order — the sheet deliberately does not carry their contents, and the integration always encounters all of them:

| Skill | Step |
|-------|------|
| `dotnet-client-initialization` | Step 1 (DI, HTTP client reuse, constructor) |
| `dotnet-authentication` | Step 1 (API key, Basic auth setup) |
| `dotnet-calling-endpoints` | Step 3 (named-vs-positional args, response envelope unwrapping, async/await) |
| `dotnet-models` | Step 4 (response shape parsing, nullable fields, when to use `.Subscription` unwrap) |
| `dotnet-error-handling` | Step 5 (Case A/B, `TryGet…` accessors, `JsonException` boundary, non-retry scenarios) |
| `dotnet-configuration-resilience` | Step 1 (retry/timeout semantics, idempotency risk on transport errors) |
| `dotnet-testing` | (if unit tests are in scope) |

---

## Assumptions & Blockers

### Assumptions
1. **No trial period** — Products are configured with no trial in sandbox (cp-exp-3); subscriptions start in `active` state immediately.
2. **No setup fee** — Product pricing is recurring only; no initial charge.
3. **No payment method collection** — PublicApi endpoint does not require cardholder data; Maxio product config permits zero-payment subscriptions (automatic billing method on `active` state subscription).
4. **User identity mapping** — eShopOnWeb user ID (or email) is stable and unique; this becomes the Maxio `Reference (reference)` for idempotent customer lookup.
5. **Sandbox environment** — All three endpoints target sandbox (cp-exp-3); production deployment requires config swap only (no code change).
6. **JWT auth on PublicApi** — PublicApi endpoints are JWT-protected; caller identity is retrieved from token before any Maxio call.

### Blockers
1. **Product configuration unknown** — Maxio products (ID 7126957, 7126958) are assumed to exist and to permit zero-payment subscriptions. If products require a payment method and scope excludes cardholder data collection, subscription creation will fail with 422. **Verify product config in sandbox before implementation.**
2. **Customer reference collision** — If eShopOnWeb user ID is not globally unique (e.g., ID reused after user deletion), `ReadCustomerByReference` + `CreateCustomer` idempotency breaks. **Confirm user ID stability and lifetime.**
3. **Metered component integration** — Scope mentions component ID 3057195 (`api-call`, $0.01/unit) but no endpoint uses it. If POST /subscriptions must pre-allocate component units or POST /api/subscriptions must accept a usage param, that is out of scope and requires a separate step (create subscription, then POST usage/component allocation).

---

## Implementation Sequence

### Step 1: Infrastructure & Configuration

**Files to create/modify:**
- `eShopOnWeb.PublicApi/appsettings.json` — Add Maxio config keys (or `.secrets.json` for local dev)
- `eShopOnWeb.PublicApi/Services/MaxioSubscriptionService.cs` — New service (DI-registered)
- `eShopOnWeb.PublicApi/Startup.cs` (or `Program.cs`) — DI registration

**Tasks:**
1. Add NuGet package: `dotnet add package AsadAli.AdvancedBilling.Sdk`
2. Configure binding keys: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` (optional)
3. DI-register `MaxioAdvancedBillingClient` as scoped (or use provided `AddMaxioAdvancedBillingClient` extension if available); ensure `HttpClient` is registered via `IHttpClientFactory` and reused.
4. Read API key from `IConfiguration`, build `BasicAuthCredentials`, set environment to `ServerEnvironment.Us` (or from config).

**Trap to load:** `dotnet-client-initialization`, `dotnet-authentication`, `dotnet-configuration-resilience`

---

### Step 2: Idempotent Customer Creation

**Files to create/modify:**
- `eShopOnWeb.PublicApi/Services/MaxioSubscriptionService.cs` — Add method `GetOrCreateCustomerAsync(userId, userEmail, ct)`

**Tasks:**
1. Accept eShopOnWeb user ID and email.
2. Call `client.Customers.ReadCustomerByReference(userId, ct)` to check if customer exists.
3. On `SdkException<RawError>` with `StatusCode == HttpStatusCode.NotFound`, create customer via `CreateCustomer(new CreateCustomerRequest { Customer = new Customer { Reference = userId, Email = userEmail, … } }, ct)`.
4. On any other error (422, 500, etc.), propagate or retry per resilience policy.
5. Extract `SubscriptionResponse.Subscription.Id` (Maxio customer ID) from response; return or cache for step 3.

**Response handling:** 
- Success (404 on lookup, then 201 on create or 200 on lookup hit) → return `{ CustomerId: int, Reference: string }`
- Error (422 from create, 500, timeout) → log, return error to caller

**Trap to load:** `dotnet-error-handling` (Case B for lookup, Case A for create)

---

### Step 3: GET /api/subscription-plans

**Files to create/modify:**
- `eShopOnWeb.PublicApi/Controllers/SubscriptionsController.cs` — New controller or endpoint
- `eShopOnWeb.PublicApi/Services/MaxioSubscriptionService.cs` — Add method `ListPlansAsync(ct)`

**Tasks:**
1. Call `client.Products.ListProducts(null, null, null, null, null, null, null, null, 1, 100, ct)` (pass `null` for all filters; list 100 per page).
2. Filter response by product family ID (3023074) or by product handle (`eshop-pro`, `basic-plan`). Response is `IReadOnlyList<ProductResponse>`; extract `item.Product` for each.
3. Map each `Product` to a DTO: `{ Id, Name, Handle, PriceInCents, Interval, IntervalUnit }`.
4. Return HTTP 200 with array of plans.

**Error handling:** 
- `SdkException<RawError>` on API call → return HTTP 503 (or 5xx, per boundary)
- Empty array on no matching products → return 200 with empty array

**Response DTO:**
```csharp
public record SubscriptionPlanDto
{
    public int Id { get; init; }
    public string? Name { get; init; }
    public string? Handle { get; init; }
    public long? PriceInCents { get; init; }
    public int? Interval { get; init; }
    public string? IntervalUnit { get; init; } // wire name from enum
}
```

**Trap to load:** `dotnet-calling-endpoints` (many optional params to `ListProducts`), `dotnet-error-handling` (Case B errors)

---

### Step 4: POST /api/subscriptions

**Files to create/modify:**
- `eShopOnWeb.PublicApi/Controllers/SubscriptionsController.cs` — New endpoint
- `eShopOnWeb.PublicApi/Services/MaxioSubscriptionService.cs` — Add method `CreateSubscriptionAsync(customerId, productId, ct)`

**Tasks:**
1. Extract logged-in user identity from JWT token (existing PublicApi pattern).
2. Call `GetOrCreateCustomerAsync(userId, userEmail, ct)` from Step 2 to obtain Maxio `CustomerId`.
3. Call `client.Subscriptions.CreateSubscription(new CreateSubscriptionRequest { Subscription = new CreateSubscription { CustomerId = customerId, ProductId = productId, Reference = "${userId}:${productId}:${timestamp}" } }, ct)`.
   - Set `Reference` to a composite key for deduplication (if needed; otherwise omit).
   - Do **not** set payment-method fields (scope excludes cardholder collection).
4. Extract response: `response.Subscription.Id` (Maxio subscription ID), `response.Subscription.State`, `response.Subscription.NextAssessmentAt`.
5. Store mapping (if needed): eShopOnWeb subscription ID ↔ Maxio subscription ID (in-app persistence).
6. Return HTTP 201 with subscription object.

**Request DTO:**
```csharp
public record CreateSubscriptionRequest
{
    [JsonPropertyName("product_id")]
    public int? ProductId { get; init; }
    
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; init; }
}
```

**Response DTO:**
```csharp
public record SubscriptionDto
{
    public int? Id { get; init; }
    public string? State { get; init; }
    public int? CustomerId { get; init; }
    public int? ProductId { get; init; }
    public string? Reference { get; init; }
    public DateTimeOffset? NextAssessmentAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
}
```

**Error handling:**
- `SdkException<CreateSubscriptionError>` with `TryGetErrorListResponse1` → extract error messages, return HTTP 422.
- `SdkException<CreateSubscriptionError>` with `TryGetRawError` → log status, return HTTP 5xx.
- User not authenticated → return HTTP 401 (JWT boundary, not SDK).

**Trap to load:** `dotnet-calling-endpoints` (named args, nullable params), `dotnet-error-handling` (Case A for CreateSubscription, Case B for GetOrCreateCustomer)

---

### Step 5: GET /api/my-subscriptions

**Files to create/modify:**
- `eShopOnWeb.PublicApi/Controllers/SubscriptionsController.cs` — New endpoint
- `eShopOnWeb.PublicApi/Services/MaxioSubscriptionService.cs` — Add method `ListUserSubscriptionsAsync(customerId, ct)`

**Tasks:**
1. Extract logged-in user identity from JWT token.
2. Call `GetOrCreateCustomerAsync(userId, userEmail, ct)` to obtain Maxio `CustomerId` (or retrieve from Step 4 cache/persistence).
3. Call `client.Customers.ListCustomerSubscriptions(customerId, ct)`.
4. Iterate response (`IReadOnlyList<SubscriptionResponse>`); for each item, extract `item.Subscription` (type `Subscription?`).
5. Filter by `State` (e.g., exclude `canceled`), or return all.
6. Map to DTO array: `{ Id, State, ProductId, ProductHandle, Reference, NextAssessmentAt, CurrentPeriodEndsAt, … }`.
7. Return HTTP 200 with array.

**Response DTO:**
```csharp
public record UserSubscriptionDto
{
    public int? Id { get; init; }
    public string? State { get; init; }
    public int? ProductId { get; init; }
    public string? ProductHandle { get; init; }
    public string? Reference { get; init; }
    public DateTimeOffset? NextAssessmentAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
}
```

**Error handling:**
- `SdkException<RawError>` on API call → return HTTP 503.
- Empty array (no subscriptions) → return 200 with empty array.
- User not authenticated → return HTTP 401.

**Trap to load:** `dotnet-error-handling` (Case B errors), `dotnet-models` (nullable `Subscription?` unwrap)

---

### Step 6: HTTP Exception Boundary

**Files to create/modify:**
- `eShopOnWeb.PublicApi/Middleware/SdkExceptionHandlerMiddleware.cs` — New middleware
- `eShopOnWeb.PublicApi/Startup.cs` (or `Program.cs`) — Register middleware in pipeline

**Tasks:**
1. Wrap all Maxio SDK calls in try-catch at the boundary (or per-endpoint).
2. Catch `SdkException<T>` (both `Case A` typed and `Case B` raw):
   - Extract HTTP status from the exception or error object.
   - Build response: status + error message.
   - Log for observability.
3. Catch `JsonException` (malformed response body):
   - Treat as 5xx (internal error, not retryable by caller).
   - Log the exception and the HTTP status it came from (if available).
   - Return HTTP 500 with generic message (do not leak deserialization details).
4. Re-throw any other exception (let ASP.NET Core handle it).

**Pseudocode:**
```csharp
try
{
    // call client.Subscriptions.CreateSubscription(…)
}
catch (SdkException<CreateSubscriptionError> ex)
{
    if (ex.Error.TryGetErrorListResponse1(out var e422))
    {
        var msg = string.Join("; ", e422.Errors ?? []);
        return StatusCode(422, new { error = msg });
    }
    else if (ex.Error.TryGetRawError(out var raw))
    {
        var status = (int)raw.StatusCode;
        var body = raw.ReadAsString();
        return StatusCode(status >= 500 ? 503 : status, new { error = body });
    }
}
catch (SdkException<RawError> ex)
{
    var status = (int)ex.Error.StatusCode;
    var body = ex.Error.ReadAsString();
    return StatusCode(status >= 500 ? 503 : status, new { error = body });
}
catch (JsonException ex)
{
    // Malformed response body; treat as 5xx
    _logger.LogError(ex, "Malformed Maxio API response");
    return StatusCode(500, new { error = "API communication error" });
}
```

**Trap to load:** `dotnet-error-handling` (both `JsonException` sources, Case A/B wiring, retry semantics)

---

## Files to Create

- `eShopOnWeb.PublicApi/Services/MaxioSubscriptionService.cs`
- `eShopOnWeb.PublicApi/Controllers/SubscriptionsController.cs` (or extend existing)
- `eShopOnWeb.PublicApi/Middleware/SdkExceptionHandlerMiddleware.cs`
- `eShopOnWeb.PublicApi/DTOs/SubscriptionDtos.cs` (namespace for response models)

## Files to Modify

- `eShopOnWeb.PublicApi/appsettings.json` (config keys)
- `eShopOnWeb.PublicApi/Program.cs` (DI, middleware registration)
- `.csproj` (NuGet package reference, already added via `dotnet add`)

## Configuration Keys

| Key | Example Value | Required |
|-----|---------------|----------|
| `Maxio:ApiKey` | (from cp-exp-3 admin panel) | Yes |
| `Maxio:Subdomain` | `cp-exp-3` | Yes |
| `Maxio:ProductFamilyHandle` | `eshop-subscribe` | No (for reference) |
| `Maxio:BaseUrl` | `https://cp-exp-3.chargify.com` | No (overrides default) |

---

**This plan assumes no in-memory state persistence across restarts and no existing Maxio integration. Configuration is loaded at startup from `IConfiguration`; all operations are async/await with cancellation token support.**
