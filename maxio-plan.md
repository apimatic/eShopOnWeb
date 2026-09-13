# Maxio Advanced Billing Integration — Recurring-Subscription Billing

## Scope & Sequence

| Step | Description | SDK Operations Used |
|------|-------------|---------------------|
| 1 | Add NuGet package + configuration/settings model | — |
| 2 | Register `MaxioAdvancedBillingClient` in DI | — |
| 3 | `MaxioService` — idempotent customer creation via `ReadCustomerByReference` → `CreateCustomer` | `client.Customers.ReadCustomerByReference`, `client.Customers.CreateCustomer` |
| 4 | `MaxioService` — list products via `ListProducts` (or `ReadProductByHandle`) | `client.Products.ReadProductByHandle` |
| 5 | `MaxioService` — create subscription via `CreateSubscription` | `client.Subscriptions.CreateSubscription` |
| 6 | `MaxioService` — list subscriptions for a customer via `ListCustomerSubscriptions` | `client.Customers.ListCustomerSubscriptions` |
| 7 | `MaxioService` — read single subscription via `ReadSubscription` | `client.Subscriptions.ReadSubscription` |
| 8 | Endpoint: `GET /api/subscription-plans` | ReadProductByHandle |
| 9 | Endpoint: `POST /api/subscriptions` | CreateCustomer (idempotent) + CreateSubscription |
| 10 | Endpoint: `GET /api/my-subscriptions` | ReadCustomerByReference + ListCustomerSubscriptions |

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

### Client Construction & Auth

| Fact | Value | Source |
|------|-------|--------|
| Package id | `AsadAli.AdvancedBilling.Sdk` | `sdk-map.md` |
| Root namespace | `MaxioAdvancedBilling` | `sdk-map.md` |
| Client class | `MaxioAdvancedBillingClient` | `sdk-map.md` |
| Options class | `MaxioAdvancedBillingClientOptions` | `sdk-map.md` |
| Auth scheme | HTTP Basic — `Username` = API key, `Password` = literal `"x"` | `sdk-map.md` |
| Auth type | `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials` | `sdk-map.md` |
| Environment type | `MaxioAdvancedBilling.Servers.ServerEnvironment` | `sdk-map.md` |
| Base URL override | `options.Server.Production.Us.BaseUrl` (set to the literal base-URL) | `sdk-map.md` |
| Subdomain | `options.Server.Production.Us.Site` (set to subdomain) | `sdk-map.md` |

### ReadProductByHandle

- **HTTP**: `GET /products/handle/{api_handle}.json` (Production)
- **Signature**: `ReadProductByHandle(string apiHandle, CancellationToken ct = default)`
- **Returns**: `MaxioAdvancedBilling.Models.ProductResponse`
- **Error**: `SdkException<MaxioAdvancedBilling.Errors.RawError>` — **Case B**
- **Error accessors**: `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?`
- **Response envelope**: `ProductResponse` has one field: `Product (product): Product`
  - `Product.Id (id): int?`
  - `Product.Name (name): string?`
  - `Product.Handle (handle): string?`
  - `Product.Description (description): string?`
  - `Product.PriceInCents (price_in_cents): long?`
  - `Product.Interval (interval): int?`
  - `Product.IntervalUnit (interval_unit): IntervalUnit?`
  - `Product.RequireCreditCard (require_credit_card): bool?`
- **Source**: `operations/Products.md`

### CreateCustomer

- **HTTP**: `POST /customers.json` (Production)
- **Signature**: `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `MaxioAdvancedBilling.Models.CustomerResponse`
- **Error**: `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>` — **Case A (typed)**
- **Error accessors**: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]
- **Request model** (`CreateCustomerRequest`):
  - `Customer (customer): CreateCustomer` — `required`
  - `CreateCustomer` fields:
    - `FirstName (first_name): string` — `required`
    - `LastName (last_name): string` — `required`
    - `Email (email): string` — `required`
    - `Reference (reference): string?` — optional (used for idempotent lookup)
    - `Organization (organization): string?` — optional
    - `CcEmails (cc_emails): string?` — optional
    - `Address (address): string?`, `Address2: string?`, `City: string?`, `State: string?`, `Zip: string?`, `Country: string?` — optional
    - `Phone (phone): string?` — optional
    - `Locale (locale): string?` — optional
    - `VatNumber (vat_number): string?` — optional
    - `TaxExempt (tax_exempt): bool?` — optional
    - `ParentId (parent_id): int?` — optional
- **Response envelope**: `CustomerResponse` has one field: `Customer (customer): Customer`
  - `Customer.Id (id): int?`
  - `Customer.FirstName (first_name): string?`
  - `Customer.LastName (last_name): string?`
  - `Customer.Email (email): string?`
  - `Customer.Reference (reference): string?`
- **Source**: `operations/Customers.md`, `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md`

### ReadCustomerByReference

- **HTTP**: `GET /customers/lookup.json` (Production)
- **Signature**: `ReadCustomerByReference(string reference, CancellationToken ct = default)`
- **Query params**: `reference` ← `reference`
- **Returns**: `CustomerResponse`
- **Error**: `SdkException<RawError>` — **Case B**
- **Error accessors**: `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?`
- **Notes**: Returns a customer by their unique reference ID. It will return a single match. If not found, throws Case B (status 404 or similar).
- **Source**: `operations/Customers.md`

### CreateSubscription

- **HTTP**: `POST /subscriptions.json` (Production)
- **Signature**: `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `MaxioAdvancedBilling.Models.SubscriptionResponse`
- **Error**: `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]
- **Request model** (`CreateSubscriptionRequest`):
  - `Subscription (subscription): CreateSubscription` — `required`
  - `CreateSubscription` fields:
    - `ProductHandle (product_handle): string?` — use this (stable handle, not stale ID)
    - `ProductId (product_id): int?` — alternative, but handle is preferred per brief
    - `CustomerReference (customer_reference): string?` — idempotent link to customer by reference
    - `CustomerId (customer_id): int?` — alternative
    - `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` — optional
    - `Reference (reference): string?` — subscription reference for idempotent lookup
    - `CouponCode (coupon_code): string?` — not needed
    - `Components (components): IReadOnlyList<CreateSubscriptionComponent>?` — not needed
    - `CustomerAttributes (customer_attributes): CustomerAttributes?` — used when creating customer inline
    - `PaymentProfileId (payment_profile_id): int?` — not needed (product has `require_credit_card: false`)
    - `NextBillingAt (next_billing_at): DateTimeOffset?` — not needed
- **Response envelope**: `SubscriptionResponse` has one field: `Subscription (subscription): Subscription?`
  - `Subscription.Id (id): int?`
  - `Subscription.State (state): SubscriptionState?`
  - `Subscription.CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`
  - `Subscription.NextAssessmentAt (next_assessment_at): DateTimeOffset?`
  - `Subscription.ActivatedAt (activated_at): DateTimeOffset?`
  - `Subscription.CreatedAt (created_at): DateTimeOffset?`
  - `Subscription.Product (product): Product?` — nested, may be null
  - `Subscription.Customer (customer): Customer?` — nested, may be null
  - `Subscription.ProductPriceInCents (product_price_in_cents): long?`
  - `Subscription.ProductHandle (product_handle): string?`
  - `Subscription.CouponCode (coupon_code): string?`
- **Source**: `operations/Subscriptions.md`, `records-2-Cr-Ne.md`, `records-3-Of-Su.md`, `records-4-Su-We.md`

### ListCustomerSubscriptions

- **HTTP**: `GET /customers/{customer_id}/subscriptions.json` (Production)
- **Signature**: `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)`
- **Returns**: `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>`
- **Error**: `SdkException<RawError>` — **Case B**
- **Error accessors**: `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?`
- **Source**: `operations/Customers.md`

### ReadSubscription

- **HTTP**: `GET /subscriptions/{subscription_id}.json` (Production)
- **Signature**: `ReadSubscription(int subscriptionId, IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionInclude>? include, CancellationToken ct = default)`
  - `include` — nullable, no default → **must pass explicitly** (pass `null` to skip)
- **Returns**: `SubscriptionResponse`
- **Error**: `SdkException<RawError>` — **Case B**
- **Error accessors**: `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?`
- **Source**: `operations/Subscriptions.md`

### Error Handling Summary

| Operation | Error Type | TryGet Methods | HTTP Status |
|-----------|-----------|----------------|-------------|
| CreateCustomer | `SdkException<CreateCustomerError>` | `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)`, `TryGetRawError(out RawError)` | 422 |
| ReadCustomerByReference | `SdkException<RawError>` | `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | varies |
| CreateSubscription | `SdkException<CreateSubscriptionError>` | `TryGetErrorListResponse1(out ErrorListResponse1)`, `TryGetRawError(out RawError)` | 422 |
| ReadProductByHandle | `SdkException<RawError>` | `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | varies |
| ListCustomerSubscriptions | `SdkException<RawError>` | `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | varies |
| ReadSubscription | `SdkException<RawError>` | `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | varies |

### Key Enums

| Enum | Namespace | Values Needed |
|------|-----------|---------------|
| `SubscriptionState` | `MaxioAdvancedBilling.Models.Enums` | `Active`, `Canceled`, `Pending`, `Trialing`, `PastDue`, `Expired` |
| `CollectionMethod` | `MaxioAdvancedBilling.Models.Enums` | `Automatic`, `Invoice` |
| `IntervalUnit` | `MaxioAdvancedBilling.Models.Enums` | `Day`, `Month` |

### Configuration Bindings

| Setting Key | Env Var | SDK Options Path |
|-------------|---------|-----------------|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | `BasicAuth.Username` |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | `Server.Production.Us.Site` |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | (app-level setting, not SDK options) |
| `Maxio:BaseUrl` | `MAXIO_BASE_URL` | `Server.Production.Us.BaseUrl` (when set) |

---

## Implementation Details

### Step 1: NuGet Package + Settings

Add `AsadAli.AdvancedBilling.Sdk` to `PublicApi.csproj`. Create `MaxioSettings.cs`:

```csharp
namespace PublicApi.Configuration;

public class MaxioSettings
{
    public const string SectionName = "Maxio";
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
}
```

### Step 2: DI Registration

In `Program.cs` (or startup), register:

```csharp
builder.Services.Configure<MaxioSettings>(builder.Configuration.GetSection(MaxioSettings.SectionName));
builder.Services.AddHttpClient();
builder.Services.AddSingleton<MaxioService>();
```

Inside `MaxioService` constructor, build `MaxioAdvancedBillingClient`:

```csharp
var httpClient = httpClientFactory.CreateClient();
var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey, Password = "x" },
    Environment = ServerEnvironment.Us,
};
if (!string.IsNullOrEmpty(settings.Subdomain))
    options.Server.Production.Us.Site = settings.Subdomain;
if (!string.IsNullOrEmpty(settings.BaseUrl))
    options.Server.Production.Us.BaseUrl = settings.BaseUrl;
_client = new MaxioAdvancedBillingClient(httpClient, options);
```

### Step 3: Idempotent Customer Creation

```csharp
public async Task<int> EnsureCustomerAsync(string email, string firstName, string lastName, string reference, CancellationToken ct)
{
    try
    {
        var existing = await _client.Customers.ReadCustomerByReference(reference, ct);
        return existing.Customer!.Id!.Value;
    }
    catch (SdkException<RawError> ex) when (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference,
            }
        };
        var response = await _client.Customers.CreateCustomer(request, ct);
        return response.Customer!.Id!.Value;
    }
}
```

### Step 4: Create Subscription (Idempotent)

Use `Reference` field on `CreateSubscription` for idempotency:

```csharp
public async Task<SubscriptionResponse> CreateSubscriptionAsync(int customerId, string productHandle, string subscriptionReference, CancellationToken ct)
{
    var request = new CreateSubscriptionRequest
    {
        Subscription = new CreateSubscription
        {
            ProductHandle = productHandle,
            CustomerId = customerId,
            Reference = subscriptionReference,
        }
    };
    return await _client.Subscriptions.CreateSubscription(request, ct);
}
```

### Step 5: Endpoints

**GET /api/subscription-plans** — Returns products from the seeded product family.

```csharp
// Endpoint returns ProductResponse list
// Product fields: Name, Handle, Description, PriceInCents, Interval, IntervalUnit
```

**POST /api/subscriptions** — Accepts `{ productHandle: string }`, ensures customer, creates subscription.

```csharp
// 1. Get caller identity from JWT
// 2. EnsureCustomerAsync(email, firstName, lastName, reference)
// 3. CreateSubscriptionAsync(customerId, productHandle, subscriptionReference)
// 4. Return subscription details: plan, price, state, next-billing-date
```

**GET /api/my-subscriptions** — Returns caller's subscriptions.

```csharp
// 1. Get caller identity from JWT
// 2. ReadCustomerByReference → get customer ID
// 3. ListCustomerSubscriptions → return subscription list
```

### Step 6: Error Handling

- **Case A errors** (`CreateCustomerError`, `CreateSubscriptionError`): Use `TryGet…` accessors, extract error messages, return 4xx to caller.
- **Case B errors** (`RawError`): Check `StatusCode`; 404 on lookup → create; 422 → validation error; else return 500 with generic message.
- **System.Text.Json.JsonException** on 2xx: Malformed response body → log + return 500.
- **System.Text.Json.JsonException** on non-2xx: Replaces the `SdkException` → map to appropriate status.

---

## Trap Notes

⚠ **Step 2 (client registration)** — The SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register. **MUST load `dotnet-configuration-resilience`** before wiring the client.

⚠ **Step 3 (customer creation)** — Idempotency via `ReadCustomerByReference` throws `SdkException<RawError>` on 404 (not a typed error). You must catch `SdkException<RawError>` and check `StatusCode == 404` specifically. **MUST load `dotnet-error-handling`** before writing this boundary.

⚠ **Step 5 (create subscription)** — `CreateSubscription` is Case A but can also throw `SdkException<RawError>` for transport/auth errors. The error response is `ErrorListResponse1` (422), not the same shape as `CreateCustomerError`. **MUST load `dotnet-error-handling`**.

⚠ **Step 5 (list subscriptions)** — `ListCustomerSubscriptions` is Case B. If the customer lookup throws before you reach this call, the error shape is also Case B — handle both. **MUST load `dotnet-error-handling`**.

⚠ **Step 2 (client construction)** — `MaxioAdvancedBillingClientOptions` has four properties; you must set `BasicAuth` (non-nullable when used), `Environment`, and optionally `Server`. The `Server` property has nested `Production.Us.Site` and `Production.Us.BaseUrl`. **MUST load `dotnet-client-initialization`**.

⚠ **Models** — Response types wrap their payload: `CustomerResponse.Customer`, `SubscriptionResponse.Subscription`, `ProductResponse.Product`. Reads go one level down. `SubscriptionResponse.Subscription` is nullable — null-check before accessing fields. **MUST load `dotnet-models`**.

---

## REQUIRED READING

These are the `dotnet-*` companion skills. **Load ALL of them before implementation starts.** The sheet deliberately does not carry their contents — it names the hazard and hands you the skill that resolves it.

| Skill | Governs |
|-------|---------|
| `dotnet-error-handling` | Error/exception boundary — which exception types reach catch blocks, how to read status codes and error bodies safely, and the traps that make catch ladders silently wrong. |
| `dotnet-client-initialization` | Client construction and DI — the builder/options shape, HttpClient ownership, and dependency-injection registration. |
| `dotnet-authentication` | Credentials — the auth scheme shape (Basic: username=API key, password="x"), per-environment configuration. |
| `dotnet-calling-endpoints` | Calling SDK operations — required vs optional parameters, named arguments for optional params, request/response envelope shapes, async usage, and cancellation. |
| `dotnet-models` — Working with models — building request objects, required members and nullability, enum construction (`StringEnum<T>`, not C# enums), union/AnyOf accessors, and JSON wire names vs C# property names. |
| `dotnet-configuration-resilience` | Retries and backoff, timeouts and cancellation, base-URL/server selection — what `Timeout` actually bounds, what `MaxRetries` does on transport vs status errors, and what you must still wire yourself. |
| `dotnet-testing` | Testing code that calls the SDK — which seam to fake, covering error and edge paths, asserting real behaviour. |

> **`System.Text.Json.JsonException` hazard** — this exception reaches the boundary from two directions
> with opposite handling:
>
> - A **drifted or malformed 2xx body** (a missing `required` member) surfaces as a `JsonException`
>   from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it
>   escape the integration boundary;
> - A **non-2xx body** that does not match its operation's generated `{Operation}Error` shape throws
>   `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces**
>   the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every
>   `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that
>   retries 5xx retries something that can never succeed.
>
> **MUST load `dotnet-error-handling`** before writing that boundary.

---

## Assumptions & Blockers

1. **Seeded products use stable handles** — the brief states handles `eshop-pro` and `basic-plan` are stable; we rely on `product_handle` not numeric IDs. ✅
2. **Payment not required** — both plans have `require_credit_card: false`, so subscription creation works without a payment profile. ✅
3. **Customer reference uniqueness** — we use the JWT subject claim (e.g. email or user ID) as the `reference` for idempotent customer creation. The `reference` field is unique per site. ✅
4. **Subscription idempotency** — we use the `Reference` field on `CreateSubscription` (set to a deterministic value like `{userId}:{productHandle}`) so double-clicks don't create duplicate subscriptions. The SDK map's `CreateSubscription` Notes do **not** explicitly document that `Reference` is used for idempotent deduplication — it may or may not prevent duplicates. **UNVERIFIED** — cannot confirm without live testing. We defensively guard by calling `ReadCustomerByReference` + `ListCustomerSubscriptions` before creating, and by catching duplicate-create errors.
5. **Maxio subdomain** — sandbox environment (`cp-exp-8`) will be supplied via `Maxio:Subdomain` configuration. The `Maxio:BaseUrl` override can redirect to a mock server for testing.
6. **Product listing endpoint** — the brief says "browse available plans." We need to list products from the seeded product family. `ListProducts` is Case B with 7 required-but-nullable params (all pass `null` to skip). We can also use `ReadProductByHandle` for known handles. The implementation should list all products in the family using `ListProducts` with `productFamilyId` filter or iterate from `ListProducts`. However, `ListProducts` does not have a product-family filter — it lists site-wide products. We'll filter client-side by `ProductFamilyId` match, or use the family's ID. **UNVERIFIED** — need to check if filtering is possible via query params. Fallback: list all products and filter by `product_family_id` in response.
7. **No custom fields** — the brief does not mention custom fields; we won't use them.
8. **Endpoint convention** — the existing `MinimalApi.Endpoint` pattern with `IEndpoint<IResult, TRequest, TRepository>` is assumed to be the pattern. The implementer must follow the existing convention and adapt.
