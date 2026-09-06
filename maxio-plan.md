# eShopOnWeb Maxio Advanced Billing Integration Plan

## Scope & Sequence

1. **Client registration & DI setup** — Register the Maxio client as a singleton in the service container with auth credentials from configuration.
2. **Customer idempotency layer** — Ensure Maxio customer exists for each authenticated eShopOnWeb user (upsert by reference).
3. **List subscription plans** — Fetch available products by family handle (`eshop-subscribe`) and return via GET `/api/subscription-plans`.
4. **Create subscription** — POST `/api/subscriptions` to create a Maxio subscription tied to the logged-in user's Maxio customer.
5. **Get user subscriptions** — GET `/api/my-subscriptions` to list subscriptions for the authenticated user's Maxio customer.

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

| Step | Operation | Signature | Request Model | Response Envelope | Error Case | Notes | Source |
|---|---|---|---|---|---|---|---|
| 1 | Client setup | `new MaxioAdvancedBillingClient(httpClient: HttpClient, options: MaxioAdvancedBillingClientOptions)` | N/A | N/A | N/A | Register as singleton. Set `options.BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" }` (auth: `MaxioAdvancedBilling.Core.Authentication.Basic`). Set `options.Environment = ServerEnvironment.Us` or `.Eu` (from `MaxioAdvancedBilling.Servers`). Override base URL via `options.Server.Production.Us.BaseUrl` if needed for sandbox. | `sdk-map.md` lines 24–52 |
| 2a | Upsert customer | `client.Customers.ReadCustomerByReference(reference: string, ct: CancellationToken = default)` | N/A | `CustomerResponse` → `.Customer: Customer !req` | Case B: `SdkException<RawError>` (404 on not found; other status codes) | Query param wire name: `reference`. Pass the eShopOnWeb user ID as `reference` for idempotency. If 404, call `CreateCustomer`. If found, use the returned customer. | `map/operations/Customers.md` lines 61–70 |
| 2b | Create customer (if not found) | `client.Customers.CreateCustomer(body: CreateCustomerRequest?, ct: CancellationToken = default)` | `CreateCustomerRequest` wrapper → `.Customer: CreateCustomer !req` with required fields: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`; optional: `Reference (reference): string?` (use for idempotency) | `CustomerResponse` → `.Customer: Customer !req` | Case A: `SdkException<CreateCustomerError>` (namespace `MaxioAdvancedBilling.Errors`) — accessors: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | Body is required (must pass explicitly, not null). Pass eShopOnWeb user ID in optional `Reference` for idempotency; catch 422 with unique-reference violation and retry via `ReadCustomerByReference` to get existing customer. | SDK source: `Models/CreateCustomerRequest.cs`, `Models/CreateCustomer.cs` |
| 3 | List subscription plans | `client.Products.ListProducts(dateField: BasicDateField?, filter: ListProductsFilter?, endDate: DateTimeOffset?, endDatetime: DateTimeOffset?, startDate: DateTimeOffset?, startDatetime: DateTimeOffset?, includeArchived: bool?, include: ListProductsInclude?, page: int? = 1, perPage: int? = 20, ct: CancellationToken = default)` | N/A (all params nullable; pass `null` to skip) | `IReadOnlyList<ProductResponse>` array, each wraps `.Product: Product !req` — key fields: `Id`, `Handle`, `PriceInCents` (not `DefaultPricePointData.Price`), `ProductFamily` (contains family handle for filtering) | Case B: `SdkException<RawError>` (generic) | The task specifies family handle `eshop-subscribe` — SDK does not filter by family in ListProducts; extract from returned `Product.ProductFamily.Handle`. Pagination: manual `page`+`perPage`; defaults are `page=1, perPage=20`. | `map/operations/Products.md` lines 28–39; SDK source: `Models/Product.cs` |
| 3b | Read product by handle (alt) | `client.Products.ReadProductByHandle(apiHandle: string, ct: CancellationToken = default)` | N/A | `ProductResponse` → `.Product: Product !req` — key fields: `Id`, `Handle` (product API handle, e.g. `eshop-pro`), `PriceInCents` (price in cents, not `DefaultPricePointData.Price`), `ProductFamily` (contains the family handle for filtering) | Case B: `SdkException<RawError>` (404 if not found) | Query param wire name: `api_handle`. If you want to fetch a specific plan by its handle (e.g. `eshop-pro`), use this. Otherwise omit and filter ListProducts results client-side. | `map/operations/Products.md` lines 51–59; SDK source: `Models/Product.cs` |
| 4 | Create subscription | `client.Subscriptions.CreateSubscription(body: CreateSubscriptionRequest?, ct: CancellationToken = default)` | `CreateSubscriptionRequest` wrapper → `.Subscription: CreateSubscription !req` → field list below | `SubscriptionResponse` → `.Subscription: Subscription !req` | Case A: `SdkException<CreateSubscriptionError>` — accessors: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | Body is required (must pass explicitly, not null). **To create a subscription, pass one of:** `ProductHandle (product_handle): string?` OR `ProductId (product_id): int?`. **Identify customer via:** `CustomerId (customer_id): int?` OR `CustomerReference (customer_reference): string?` (use the reference value created in step 2). Pass the customer reference to make the call idempotent to eShopOnWeb's user ID. No payment method required per task (sandbox plan config). Other optional fields: `CouponCode (coupon_code): string?`, `Reference (reference): string?` (optional subscription reference). | `map/operations/Subscriptions.md` lines 31–40; `map/models/records-2-Cr-Ne.md` lines 17–18 (CreateSubscriptionRequest, CreateSubscription) |
| 5 | Get user subscriptions | `client.Customers.ListCustomerSubscriptions(customerId: int, ct: CancellationToken = default)` | N/A | `IReadOnlyList<SubscriptionResponse>` array, each wraps `.Subscription: Subscription !req` — key fields: `Id`, `State`, `ProductPriceInCents`, `CurrentPeriodEndsAt` (next regular billing date), `NextAssessmentAt` (next attempted charge date; may differ if payment failed), `ActivatedAt`, `CanceledAt` | Case B: `SdkException<RawError>` (generic) | Pass the Maxio customer ID (obtained in step 2a). Returns all subscriptions for that customer. Note: Subscription has no `NextBillingAt` field; use `CurrentPeriodEndsAt` for the next billing date or `NextAssessmentAt` if a payment retry is scheduled. | SDK source: `Models/Subscription.cs` |

### Models — Request/Response Structures

**CreateCustomerRequest** (SDK source: `Models/CreateCustomerRequest.cs`):
```csharp
record CreateCustomerRequest {
    required CreateCustomer Customer (wire: "customer") { get; init; }
}

record CreateCustomer {
    required string FirstName (first_name) { get; init; }
    required string LastName (last_name) { get; init; }
    required string Email (email) { get; init; }
    string? Reference (reference) { get; init; }
    string? Organization (organization) { get; init; }
    // … (many other optional fields)
}
```

**CreateSubscriptionRequest** (SDK source: `Models/CreateSubscriptionRequest.cs`, `Models/CreateSubscription.cs`):
```csharp
record CreateSubscriptionRequest {
    required CreateSubscription Subscription (subscription) { get; init; }
}

record CreateSubscription {
    string? ProductHandle (product_handle) { get; init; }
    int? ProductId (product_id) { get; init; }
    int? CustomerId (customer_id) { get; init; }
    string? CustomerReference (customer_reference) { get; init; }
    string? Reference (reference) { get; init; }
    // … (many other optional fields)
}
```

**Product** (SDK source: `Models/Product.cs`):
```csharp
record Product {
    int? Id { get; init; }
    string? Handle { get; init; }  // NOT ApiHandle
    long? PriceInCents { get; init; }  // NOT DefaultPricePointData.Price
    ProductFamily? ProductFamily { get; init; }
    // … (many other fields)
}
```

**Subscription** (SDK source: `Models/Subscription.cs`):
```csharp
record Subscription {
    int? Id { get; init; }
    SubscriptionState? State { get; init; }
    long? ProductPriceInCents { get; init; }
    DateTimeOffset? CurrentPeriodEndsAt { get; init; }  // Next billing date
    DateTimeOffset? NextAssessmentAt { get; init; }  // Next charge attempt
    // NOTE: No NextBillingAt field; use CurrentPeriodEndsAt instead
}
```

## Enums — Subscription Collection Method

From `map/models/enums.md` line 21:

```csharp
CollectionMethod (StringEnum) — members:
  Automatic (automatic)
  Remittance (remittance)
  Prepaid (prepaid)
  Invoice (invoice)
```

Use when specifying payment collection in subscription creation; the task does not require this (no payment method needed in sandbox).

## Client Construction & Configuration

**Dependencies:** The NuGet package `AsadAli.AdvancedBilling.Sdk` includes transitive dependencies: `Polly`, `Microsoft.Extensions.Http`, `System.Net.Http.Json`, `System.Net.ServerSentEvents`. No manual add of `Microsoft.Extensions.Http` is needed.

**Namespace:** `MaxioAdvancedBilling` · `MaxioAdvancedBilling.Core.Authentication.Basic` · `MaxioAdvancedBilling.Servers` (from `sdk-map.md`)

```csharp
// Example (do NOT copy to code; use companion skills):
var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials 
    { 
        Username = configuration["Maxio:ApiKey"], 
        Password = "x" 
    },
    Environment = ServerEnvironment.Us,
    // Optional: override base URL for sandbox
    // Server = new ServerOptions { Production = new ProductionOptions { Us = new { BaseUrl = "http://localhost:..." } } }
};
var client = new MaxioAdvancedBillingClient(httpClient, options);
```

**Auth note:** Username = API key (from `MAXIO_API_KEY` env var → `Maxio:ApiKey` config key). Password = literal string `"x"`.

**Environment note:** The task specifies sandbox site `cp-exp-1`; map line 205 notes that `{site}` defaults to `subdomain` — set via `options.Server.Production.Us.Site = "cp-exp-1"` OR override `options.Server.Production.Us.BaseUrl` directly.

## Trap Notes

⚠ **Step 1 (client registration & DI)** — The SDK's `RetryOptions` (including `Timeout`, `MaxRetries`, `HttpMethodsToRetry`) are per-attempt settings on Polly, not total-call timeouts. `HttpMethodsToRetry` gates only the HTTP status code trigger (not transport failures, which retry on all verbs including POST/PUT). **MUST load `dotnet-configuration-resilience`** before wiring client retry/timeout settings.

⚠ **Step 2a & 3 & 5 (read operations returning lists or optional single objects)** — `ReadCustomerByReference`, `ListProducts`, `ListCustomerSubscriptions` are Case B (throw `SdkException<RawError>`), not typed `{Operation}Error`. `TryGetRawError` is the only accessor; no other `TryGet…` methods. **MUST load `dotnet-error-handling`** before writing catch blocks to understand the Case A vs Case B difference and the two `JsonException` hazards.

⚠ **Step 2b & 4 (write operations)** — `CreateCustomer` and `CreateSubscription` are Case A (throw `SdkException<CreateCustomerError>` and `SdkException<CreateSubscriptionError>` respectively). Each has multiple `TryGet…` accessors for different HTTP statuses. **MUST load `dotnet-error-handling`** before implementing error boundaries.

⚠ **All steps (deserialization of response models)** — The SDK uses `System.Text.Json` with `[JsonPropertyName]` attributes. Mismatch between wire field names (JSON) and C# property names is handled by the generated models — do not parse wire payloads manually. However, **a drifted or malformed 2xx body (missing required member) surfaces as `JsonException` from deserialization, NOT `SdkException`** — so an SDK-exception-only catch ladder lets it escape. A **non-2xx body that does not match the operation's error shape throws `JsonException` while the error object is being constructed**, destroying the HTTP status in the process. **MUST load `dotnet-error-handling`** to understand these two `JsonException` paths and how to handle them in a boundary that distinguishes outages from contract violations.

⚠ **Step 3 (product listing by family)** — The SDK provides `ListProducts` with optional filtering, but the map does not list a family-handle query parameter. The returned `Product` model contains a `ProductFamily` field. Either (a) list all products and filter client-side by `product.ProductFamily.Handle == "eshop-subscribe"`, or (b) call `ReadProductByHandle("eshop-pro")` and `ReadProductByHandle("eshop-basic")` separately for the two known plans. The task provides product handles; method (b) is more direct. **MUST load `dotnet-calling-endpoints`** to confirm nullable/optional parameter handling on `ListProducts` (many params are nullable with no default, so named arguments are safer than positional).

⚠ **Step 4 (subscription idempotency)** — The task requires idempotency: creating a subscription for a user who already has one should not fail. The SDK does not expose an "upsert" operation; there is no built-in deduplication. Instead, pass the eShopOnWeb user ID as the subscription's `Reference (reference): string?` field, then query by reference or catch the 422 on duplicate and return the existing subscription. **MUST load `dotnet-calling-endpoints`** to see `FindSubscription` (lines 42–52 of Subscriptions.md) which finds by reference — this is the idempotency check tool.

⚠ **Configuration & environment** — The task provides env vars (`MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_ENVIRONMENT`, `MAXIO_DEFAULT_PRODUCT_FAMILY`) and config keys (`Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl`). Bind these early in the DI setup. The site subdomain (from env var `MAXIO_SITE_SUBDOMAIN` → config key `Maxio:Subdomain`) must be wired into the client's server options or base URL. **MUST load `dotnet-client-initialization`** for the DI registration pattern.

---

## REQUIRED READING

Before implementation starts, load the following companion skills in order. Each is named to match the step it governs, and each carries gotchas and defaults a signature cannot show:

| Skill | Step(s) | Purpose |
|---|---|---|
| `maxio-sdk:dotnet-client-initialization` | 1, configuration & environment | Client construction, DI registration, httpClient factory lifetime, options builder callback pattern |
| `maxio-sdk:dotnet-authentication` | 1 | Basic auth wiring: username = API key, password = `"x"`, credential lifecycle, per-environment secret rotation |
| `maxio-sdk:dotnet-calling-endpoints` | 2, 3, 4, 5 | Operation invocation, nullable required params, named argument safety, pagination (manual page+perPage), async/cancellation, response envelope shape (`.Customer`, `.Subscription`, `.Product`) |
| `maxio-sdk:dotnet-models` | 2, 4 | Record model immutability, union construction/reading, enum `StringEnum<T>` factories and static members, JSON wire name mapping |
| `maxio-sdk:dotnet-error-handling` | 2, 3, 4, 5 | Case A vs Case B exceptions, `TryGet…` accessors, `JsonException` from malformed 2xx and from error-shape mismatch on non-2xx, boundary design to distinguish contract violations from transient failures |
| `maxio-sdk:dotnet-configuration-resilience` | 1 | Retry policy semantics (per-attempt, HTTP-status vs transport-failure gates), timeout scope, Polly backing, logging hooks |

**These are to be loaded BEFORE implementation starts.** The sheet deliberately does not carry their contents — those skills carry the worked examples, defaults, and gotchas that no operation signature can encode. The implementer must read each skill for its step before writing the corresponding code, or the integration will fail at runtime or will be fragile.

**JsonException hazard — both paths, both behaviors:**

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing the error boundary. These rows belong in the FIRST sheet, not a later revision: the boundary is written early, and a caveat that arrives afterwards arrives too late to shape it.

---

## Clarifications (for Coordinator's SDK Questions)

1. **Product fields** — `Product.Handle` (not `ApiHandle`), `Product.PriceInCents` (not `DefaultPricePointData.Price`). Full field list in Models section above and in SDK source `Models/Product.cs`.
2. **Subscription fields** — No `NextBillingAt`; use `CurrentPeriodEndsAt` (next regular billing date) or `NextAssessmentAt` (if a retry is pending). Full field list in Models section and SDK source `Models/Subscription.cs`.
3. **Error types** — `CreateCustomerError` and `CreateSubscriptionError` DO exist in namespace `MaxioAdvancedBilling.Errors`. They are generated Case A error types with `TryGet…` accessors per the map.
4. **CreateCustomerRequest structure** — Wrapper property is `.Customer: CreateCustomer` (not `.Subscription`). Wire name is `"customer"`. The `CreateCustomer` record has required fields `FirstName`, `LastName`, `Email` and optional `Reference`.
5. **IHttpClientFactory / Microsoft.Extensions.Http** — Already included as a transitive dependency of the SDK package. No manual add required.

## Assumptions & Blockers

- **Assumption:** eShopOnWeb user ID (from JWT claim or identity) will be passed as the Maxio customer `Reference` field for idempotency. The task assumes JWT auth is already in place on the PublicApi endpoints.
- **Assumption:** The in-memory database on eShopOnWeb is not used to store Maxio customer IDs or subscription IDs; those are fetched on-demand via the SDK (or cached in application state if needed for performance).
- **Assumption:** The sandbox site `cp-exp-1` is already created in Maxio, and the two plans (`eshop-pro`, `eshop-basic`) and metered component (`api-call`) are already provisioned.
- **No blockers** — all operations and models are generated and available in the SDK. The Maxio customer-creation call may return 422 if the reference already exists; this is handled by the idempotency pattern (catch the 422 and retry via `ReadCustomerByReference`, or check first before creating).
