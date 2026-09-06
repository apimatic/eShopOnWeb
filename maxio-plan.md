# Maxio Integration Plan: eShopOnWeb Recurring Subscriptions

## Scope & Sequence

1. **Client & DI registration** — Register `MaxioAdvancedBillingClient` with HTTP Basic auth (API key + "x"), set environment/base URL from config
2. **Customer idempotence** — Call `ReadCustomerByReference` with user ID as reference; on 404, call `CreateCustomer` to enroll in Maxio
3. **Plan listing** — Call `ListProducts` filtered by product family handle; return handle, name, price to UI
4. **Subscription enrollment** — Call `CreateSubscription` with product handle, existing customer ID, no payment method
5. **Subscription status** — Call `ListSubscriptions` filtered by customer ID; return active subscription state/price/next billing date

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

| Operation | Signature | Request model + fields | Response envelope + inner fields | Error case + accessors + payload type | Pagination | Source |
|---|---|---|---|---|---|---|
| **Products.ReadProductByHandle** | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` · `apiHandle` — literal product handle string, required | N/A | `ProductResponse` · `Product (product): Product !req` — contains `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?` | **Case B** — `SdkException<RawError>` · `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | none | `map/operations/Products.md` |
| **Products.ListProducts** | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` · 8 params nullable, pass `null` to skip · `page` default 1, `perPage` default 20 | N/A | `IReadOnlyList<ProductResponse>` · each contains `Product (product): Product !req` | **Case B** — `SdkException<RawError>` · `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | manual `page`+`perPage` | `map/operations/Products.md` |
| **Customers.ReadCustomerByReference** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` · `reference` — query param, user ID, required | N/A | `CustomerResponse` · `Customer (customer): Customer !req` — contains `Id (id): int?`, `Reference (reference): string?`, `FirstName (first_name): string?`, `Email (email): string?`, `CreatedAt (created_at): DateTimeOffset?` | **Case B** — `SdkException<RawError>` · on 404 body is `null`; `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | none | `map/operations/Customers.md` |
| **Customers.CreateCustomer** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` · `body` required (non-nullable param, must pass explicitly) | `CreateCustomerRequest` · `Customer (customer): CreateCustomer !req` · inner `CreateCustomer` fields: `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?`, `Organization (organization): string?` | `CustomerResponse` · `Customer (customer): Customer !req` | **Case A** — `SdkException<CreateCustomerError>` · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `map/operations/Customers.md`, `map/models/records-2-Cr-Ne.md` |
| **Subscriptions.CreateSubscription** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` · `body` required (non-nullable param, must pass explicitly) | `CreateSubscriptionRequest` · `Subscription (subscription): CreateSubscription !req` · inner `CreateSubscription` fields (alphabetically): `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `CustomerId (customer_id): int?`, `CustomerAttributes (customer_attributes): CustomerAttributes?`, `Reference (reference): string?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` (enum, optional; wire values: `automatic`, `remittance`) | `SubscriptionResponse` · `Subscription (subscription): Subscription?` — contains `Id (id): int?`, `State (state): SubscriptionState?` (enum), `ProductPriceInCents (product_price_in_cents): long?`, `BalanceInCents (balance_in_cents): long?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `CreatedAt (created_at): DateTimeOffset?` | **Case A** — `SdkException<CreateSubscriptionError>` · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `map/operations/Subscriptions.md`, `map/models/records-2-Cr-Ne.md` |
| **Subscriptions.ListSubscriptions** | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` · 14 params nullable, pass `null` to skip · `page` default 1, `perPage` default 20 | N/A | `IReadOnlyList<SubscriptionResponse>` · each contains `Subscription (subscription): Subscription?` | **Case B** — `SdkException<RawError>` · `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | manual `page`+`perPage` | `map/operations/Subscriptions.md` |
| **Subscriptions.ReadSubscription** | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` · `subscriptionId` — required; `include` nullable, pass `null` to skip | N/A | `SubscriptionResponse` · `Subscription (subscription): Subscription?` | **Case B** — `SdkException<RawError>` · `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | none | `map/operations/Subscriptions.md` |

### Enum values (wire names)

**`SubscriptionState`** (map: `map/models/enums.md`):
- `Active` ("active"), `TrialEnded` ("trial_ended"), `Expired` ("expired"), `Canceled` ("canceled"), `PastDue` ("past_due"), `Paused` ("paused")

**`CollectionMethod`** (map: `map/models/enums.md`):
- `Automatic` ("automatic"), `Remittance` ("remittance"), `Prepaid` ("prepaid")

**`SubscriptionStateFilter`** (for ListSubscriptions query filter):
- `Active`, `Canceled`, `Expired`, `PastDue`, `Paused`, `PendingCancellation`, `TrialEnded`, `Awaiting_signup`, `AwaitingSignup`

### Client construction & auth

**Namespaces for client & auth:**
```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers; // ServerEnvironment
using MaxioAdvancedBilling.Api; // controller accessors (client.Customers, client.Subscriptions, etc.)
using MaxioAdvancedBilling.Models; // request/response records
using MaxioAdvancedBilling.Models.Enums; // enums (SubscriptionState, CollectionMethod, etc.)
```

**Options construction (from config):**
```csharp
var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials 
    { 
        Username = config["Maxio:ApiKey"], // read from config (IConfiguration)
        Password = "x" // literal string
    },
    Environment = ServerEnvironment.Us, // default; set to ServerEnvironment.Eu if needed
    Server = new ServerOptions
    {
        Production = new ProductionOptions
        {
            Us = new ProductionServerConfig { Site = config["Maxio:Subdomain"] }
        }
    }
};
// Optional: override base URL if Maxio:BaseUrl is configured
if (!string.IsNullOrEmpty(config["Maxio:BaseUrl"]))
{
    options.Server.Production.Us.BaseUrl = config["Maxio:BaseUrl"];
}
var client = new MaxioAdvancedBillingClient(httpClient, options);
```

**DI registration:**
```csharp
services.AddMaxioAdvancedBillingClient(options =>
{
    options.BasicAuth = new BasicAuthCredentials { Username = "<api_key>", Password = "x" };
    // configure Environment, Server as above
});
```

---

## Trap notes

⚠ **Step 1 (client initialization)** — The `HttpClient` must be registered as a **long-lived singleton** via `IHttpClientFactory.CreateClient()` and reused across the application lifetime; the SDK client wrapper may be transient, but the underlying `HttpClient` pipeline must not be rebuilt per request. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ **Step 2 (customer lookup on 404)** — When `ReadCustomerByReference` returns HTTP 404, the SDK throws `SdkException<RawError>` with `StatusCode = NotFound`; the response body may be empty or null. The exception **is** thrown (not a nullable return), so catch the exception to detect "customer not found", not null-check. **MUST load `dotnet-calling-endpoints`** for exception-based lookup patterns.

⚠ **Step 3 (product filtering)** — `ListProducts` has no built-in filter for product family; instead, read all products and filter in memory by `product_family_handle`, or call `ListProducts` and check each `Product.ProductFamily.Handle` in the response. The API returns the full hierarchy. **MUST load `dotnet-calling-endpoints`** to understand optional parameter passing (use named arguments, pass `null` to skip).

⚠ **Step 4 (subscription creation without payment method)** — The specification states "no payment method required"; the sandbox plans (`eshop-pro`, `basic-plan`) do not require a payment profile for subscription creation. Do not pass `PaymentProfileId` or `CreditCardAttributes` in the `CreateSubscription` request body. If the live API requires a payment method despite the plan config, Maxio will return a 422 error; read the error response via `TryGetErrorListResponse1` to extract field-level validation messages. **MUST load `dotnet-error-handling`** before implementing the error boundary.

⚠ **Step 5 (subscription state tracking)** — `ListSubscriptions` returns the full `Subscription` object per result, including `State` (active/canceled/expired/etc.) and `NextAssessmentAt` (next billing date). For the hero flow, extract these fields from the response to confirm enrollment. The `State` field is a `SubscriptionState?` enum, so use the enum values (not wire strings) when comparing in C#. **MUST load `dotnet-models`** to understand union and enum construction.

⚠ **JsonException on 2xx mismatches** — If a 2xx response body has a missing required field (e.g. `Subscription` is `null` when it should be required), deserialization throws `System.Text.Json.JsonException`, **not** `SdkException`. This exception **is not** caught by an `SdkException` catch block. The boundary must map every `JsonException` to a 5xx so that a missing/malformed 2xx does not silently pass as success. **MUST load `dotnet-error-handling`** — the section on `JsonException` handling is load-bearing.

⚠ **JsonException on non-2xx shape mismatch** — If a non-2xx response body does not match the operation's generated error type (e.g. `CreateSubscriptionError`), deserialization throws `JsonException` **while the error object is being constructed inside the exception handler**, which **replaces** the `SdkException` and **destroys the HTTP status code**. A boundary that maps every `JsonException` to a 5xx then retries 5xx will retry a 422 formatted incorrectly forever. **MUST load `dotnet-error-handling`** — understand the two `JsonException` cases and handle them separately from `SdkException`.

⚠ **Step 1 (HTTP Basic auth)** — The SDK requires Basic auth with **username = API key, password = the literal string `"x"`**. Do not pass the key as the password. Load credentials from `Maxio:ApiKey` config binding (never hardcoded). **MUST load `dotnet-authentication`** before configuring credentials.

⚠ **Step 1 (environment/base URL)** — The SDK defaults to US production (`https://{subdomain}.chargify.com`). The sandbox environment is the same host; only the subdomain (`cp-exp-1`) and the API key differ. If the `Maxio:BaseUrl` config is set, use it as an override; otherwise, derive the URL from `Maxio:Subdomain`. **MUST load `dotnet-configuration-resilience`** to understand server-node overrides and how `ServerOptions` routes requests.

---

## REQUIRED READING

Load these skills **before implementation starts**. The sheet deliberately does not carry their contents:

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1: Client registration, HttpClient lifecycle, DI patterns |
| `dotnet-authentication` | Step 1: Basic auth setup, credential loading, rotation |
| `dotnet-calling-endpoints` | Steps 2–5: Operation signatures, required vs optional params, named arguments, exception-based error detection |
| `dotnet-models` | Steps 2–5: Request/response envelope shapes, enum values, union construction/reading via `TryGet…` |
| `dotnet-error-handling` | All steps: Exception hierarchy (Case A vs Case B), `TryGet…` accessors, `JsonException` boundary hazards, retry semantics |
| `dotnet-configuration-resilience` | Step 1: Server-node overrides, base-URL routing, retry policies, timeout semantics |

**Critical: These two JsonException rows belong in the FIRST sheet, not a later revision:**

- **2xx body mismatch** — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.
- **Non-2xx body mismatch** — a **non-2xx** body that does not match its operation's generated error shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

---

## Assumptions & Blockers

**Assumptions:**

1. **Customer reference strategy** — User ID (from ASP.NET Identity `User.FindFirst(ClaimTypes.NameIdentifier)?.Value`) will be passed to Maxio as the customer `Reference` field, enabling idempotent lookup on repeat calls. The assumption is that the application has a stable user ID that does not change over the subscription lifetime.

2. **Plan handle/ID source** — Plan IDs/handles (`eshop-pro`, `basic-plan`, etc.) are hardcoded in the integration code or cached locally from Maxio; the application does not require plan names/pricing to be fetched live on every request (though a cached store + periodic refresh is valid). The specified plans exist in the `cp-exp-1` sandbox.

3. **No subscription cancellation in scope** — The hero flow covers enrollment only. Cancellation, plan changes, and payment updates are not in scope for this plan.

4. **JWT authentication is the boundary; Maxio auth is orthogonal** — The application's `/api/subscriptions` endpoints are protected by JWT; the Maxio API key is injected at the service layer and never exposed to the client. The application does not need to route Maxio credentials through the user session.

5. **No webhook event ingestion in scope** — Subscription state changes (billing, renewal, cancellation) are not tracked via webhooks; the application reads subscription state on demand via `ReadSubscription` or `ListSubscriptions`. Async billing events (proforma invoice generation, payment failures) are not modeled in this integration.

**Blockers:**

None identified. The sandbox environment, plan IDs, and API key are confirmed available. The SDK targets `netstandard2.0` and is compatible with .NET 8 (the target for eShopOnWeb; .NET 10 SDK can build .NET 8 targets via `DOTNET_ROLL_FORWARD=Major`).

