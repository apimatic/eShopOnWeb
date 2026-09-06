# Maxio Advanced Billing Integration Plan: eShopOnWeb Subscriptions

## Scope & Sequence

1. **Client initialization & configuration** — register `MaxioAdvancedBillingClient` with DI; load credentials from app configuration (Maxio:ApiKey, Maxio:Subdomain, Maxio:Environment, Maxio:ProductFamilyHandle); wire HTTP client and retry/resilience options.
2. **List subscription plans** — `ListProductsForProductFamily` to retrieve plans for the default product family; expose via `GET /api/subscription-plans`.
3. **Create or lookup customer** — `ReadCustomerByReference` to check if user exists (idempotent); fall back to `CreateCustomer` if not found; expose customer mapping to eShopOnWeb user.
4. **Create subscription** — `CreateSubscription` to subscribe customer to a plan; expose via `POST /api/subscriptions` (requires plan handle and customer reference).
5. **Retrieve active subscriptions** — `ListCustomerSubscriptions` to fetch user's active subscriptions; expose via `GET /api/my-subscriptions`.
6. **Error handling** — wrap all operations in try/catch; map Maxio errors to app-level exceptions; return deterministic HTTP status and error messages to the API caller.

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

| Operation | Signature | Request Model & Fields | Response Envelope & Inner Fields | Error Case & Accessors | Pagination | Source |
|---|---|---|---|---|---|---|
| **ListProductsForProductFamily** (client.ProductFamilies) | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | Optional query params for filtering/pagination; pass `null` to skip. For eShopOnWeb, pass only `productFamilyId` (the handle "eshop-subscribe" or numeric ID) and default pagination. | `IReadOnlyList<ProductResponse>` — each element wraps `Product (product): Product !req`. Read `product.Id`, `product.Handle`, `product.Name`, `product.PriceInCents`, `product.Interval`, `product.IntervalUnit` to populate the UI plan list. | Case A: `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404 — product family not found] · `TryGetRawError(out RawError)` [fallback]. | Manual `page`+`perPage` (defaults 1, 20). For v1, use defaults. | `operations/ProductFamilies.md` |
| **ReadCustomerByReference** (client.Customers) | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | `reference` — wire name `reference`, the eShopOnWeb user ID or email (must be unique per Maxio site). | `CustomerResponse` — wraps `Customer (customer): Customer !req`. Read `customer.Id` (Maxio customer ID), `customer.Reference`, `customer.Email`, `customer.FirstName`, `customer.LastName`. | Case B: `SdkException<RawError>` — `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` · `ReadAsBytes(): ReadOnlyMemory<byte>`. On 404, customer does not exist (fall back to CreateCustomer). | None. | `operations/Customers.md` |
| **CreateCustomer** (client.Customers) | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` | `CreateCustomerRequest` (namespace `MaxioAdvancedBilling.Models`) wraps `Subscription (subscription): CreateSubscription !req`. `CreateCustomer` record fields: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?`, `CcEmails (cc_emails): string?`, `Organization (organization): string?`, `Address (address): string?`, `Address2 (address_2): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `Locale (locale): string?`, `VatNumber (vat_number): string?`, `TaxExempt (tax_exempt): bool?`, `TaxExemptReason (tax_exempt_reason): string?`. For eShopOnWeb: map user email to `Email`, user ID to `Reference`, and use `FirstName`+`LastName` from profile. Wire format: `{ "customer": { "first_name": "…", "last_name": "…", "email": "…", "reference": "…" } }`. | `CustomerResponse` — wraps `Customer (customer): Customer !req`. Read `customer.Id` (Maxio customer ID). | Case A: `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422 — validation error, e.g. duplicate reference] · `TryGetRawError(out RawError)` [fallback]. | None. | `operations/Customers.md` · `records-2-Cr-Ne.md` |
| **CreateSubscription** (client.Subscriptions) | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | `CreateSubscriptionRequest` (namespace `MaxioAdvancedBilling.Models`) wraps `Subscription (subscription): CreateSubscription !req`. `CreateSubscription` record fields (key ones for eShopOnWeb): `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `CustomerReference (customer_reference): string?` *(preferred for eShopOnWeb user ID mapping)*, `CustomerId (customer_id): int?`, `PaymentProfileId (payment_profile_id): int?`, `CustomerAttributes (customer_attributes): CustomerAttributes?` *(use to create customer inline if not yet created)*, `CouponCode (coupon_code): string?`, `DeferSignup (defer_signup): bool? = false` *(if true, subscription created but not yet activated; defaults false, so subscription activates immediately)*, optional `Components (components): IReadOnlyList<CreateSubscriptionComponent>?`, etc. For eShopOnWeb: pass `product_handle` (e.g. "eshop-pro") and either `customer_reference` (user ID) or `customer_id` (if already fetched). Payment method NOT required per contract. Wire format: `{ "subscription": { "product_handle": "eshop-pro", "customer_reference": "user123" } }` or with `customer_id`. | `SubscriptionResponse` — wraps `Subscription (subscription): Subscription !req`. Read `subscription.Id`, `subscription.Reference`, `subscription.State` (e.g. "active"), `subscription.CurrentPeriodEndsAt`, `subscription.NextAssessmentAt`, `subscription.Product` (if included), etc. | Case A: `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422 — validation error, e.g. product not found, customer not found] · `TryGetRawError(out RawError)` [fallback]. | None. | `operations/Subscriptions.md` · `records-2-Cr-Ne.md` |
| **ListCustomerSubscriptions** (client.Customers) | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | `customerId` — Maxio customer ID (from CreateCustomer or ReadCustomerByReference response). | `IReadOnlyList<SubscriptionResponse>` — each wraps `Subscription (subscription): Subscription !req`. Read `subscription.Id`, `subscription.Reference`, `subscription.State`, `subscription.CurrentPeriodEndsAt`, `subscription.Product`, etc. | Case B: `SdkException<RawError>` — `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` · `ReadAsBytes(): ReadOnlyMemory<byte>`. | None. | `operations/Customers.md` |

### Enum values needed

| Enum | Namespace | Members used | Source |
|---|---|---|---|
| `SubscriptionState` | `MaxioAdvancedBilling.Models.Enums` | `Active (active)`, `Canceled (canceled)`, `PastDue (past_due)`, `Trialing (trialing)`, `Suspended (suspended)` — map these wire values to app subscription states for display. | `enums.md` |
| `IntervalUnit` | `MaxioAdvancedBilling.Models.Enums` | `Day (day)`, `Month (month)` — from Product.IntervalUnit; pair with Product.Interval (e.g. 1 month = monthly billing). | `enums.md` |

### Client construction & configuration

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Servers;
using MaxioAdvancedBilling.Core.Authentication.Basic;

// In dependency injection (startup):
services.AddMaxioAdvancedBillingClient(options =>
{
    // Read from config: Maxio:ApiKey, Maxio:Subdomain, Maxio:Environment
    var apiKey = configuration["Maxio:ApiKey"];      // environment variable or appsettings.json
    var subdomain = configuration["Maxio:Subdomain"];
    var env = configuration["Maxio:Environment"];    // "US" or "EU", defaults to "US"
    
    // Basic auth: username = API key, password = literal "x"
    options.BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" };
    
    // Environment: ServerEnvironment.Us (default) or ServerEnvironment.Eu
    options.Environment = env == "EU" ? ServerEnvironment.Eu : ServerEnvironment.Us;
    
    // Optional: override base URL if needed
    // options.Server.Production.Us.Site = subdomain;
});

// Then inject and use:
public class SubscriptionService
{
    public SubscriptionService(MaxioAdvancedBillingClient client) { _client = client; }
    private readonly MaxioAdvancedBillingClient _client;
    
    public async Task<List<Plan>> GetPlansAsync(string productFamilyHandle, CancellationToken ct = default)
    {
        var response = await _client.ProductFamilies.ListProductsForProductFamily(
            productFamilyId: productFamilyHandle, // e.g. "eshop-subscribe"
            dateField: null, filter: null, startDate: null, endDate: null, 
            startDatetime: null, endDatetime: null, includeArchived: null, 
            include: null, page: 1, perPage: 20, ct: ct);
        // response is IReadOnlyList<ProductResponse>
        return response.Select(pr => new Plan { Id = pr.Product.Id, Handle = pr.Product.Handle, ... }).ToList();
    }
}
```

---

## Trap notes

⚠ **Step 1 (client initialization)** — the `HttpClient` passed to the SDK constructor must be long-lived and shared (via `IHttpClientFactory`), not recreated per request. The SDK wraps it; if you pass a new instance each time, connections are wasted and retry state is lost. **MUST load `dotnet-client-initialization`** before wiring DI.

⚠ **Step 1 (credentials)** — Basic auth username is the API key (NOT an email or username); password is the literal string `"x"`. Set credentials **before** constructing the client or in the DI callback. **MUST load `dotnet-authentication`** before setting credentials.

⚠ **Step 2–5 (calling endpoints)** — all operations are throw-based; there are no no-throw `Result` variants. Wrap every call in try/catch. Many optional params have no C# default and must be passed explicitly (even as `null`). Use named arguments to avoid positional mis-binding. **MUST load `dotnet-calling-endpoints`** before writing the first call.

⚠ **Step 3–4 (models)** — `CreateCustomer` and `CreateSubscription` wrap their payloads in outer request records (`CreateCustomerRequest` ⇒ field `Customer`, `CreateSubscriptionRequest` ⇒ field `Subscription`). Do **not** pass the inner record directly; wrap it. Response envelopes also wrap (e.g. `CustomerResponse.Customer`, `SubscriptionResponse.Subscription`). **MUST load `dotnet-models`** before building request bodies.

⚠ **Step 5 (error handling)** — reads and lookups are Case B (`SdkException<RawError>`); creates are Case A (typed `{Operation}Error` with `TryGet…` accessors). A 404 on `ReadCustomerByReference` means "customer does not exist" (not an error in the app logic; fall back to create). On `CreateCustomerError` 422, extract `ex.Error.TryGetCustomerErrorResponse1(out var payload)` and inspect `payload.Errors` for field-level messages (e.g. duplicate reference). A `JsonException` can surface from **two** directions: a drifted **2xx** body (missing `required` field) surfaces as `JsonException` from deserialization (not an `SdkException`), and a **non-2xx** body that doesn't match the operation's error shape throws `JsonException` **while constructing the error object** (replacing the `SdkException` and destroying the HTTP status). **MUST load `dotnet-error-handling`** before writing the error boundary.

⚠ **Steps 1, 6 (resilience & retries)** — the SDK's `Retry` options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register. `MaxRetries` gates only the **status-code** trigger; a **transport failure** (`HttpRequestException`) on a `POST` is still retried, so non-idempotent writes can execute more than once. Subscription creates are idempotent when using `customer_reference` (Maxio deduplicates on reference), so re-sends are safe. **MUST load `dotnet-configuration-resilience`** before tuning retries, timeouts, or the base URL.

⚠ **Drifted or malformed 2xx body (missing required field)** — surfaces as `System.Text.Json.JsonException` from deserialization, **not** as `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** and map `JsonException` at the boundary.

⚠ **Non-2xx body that doesn't match the operation's error shape** — throws `JsonException` **while the error object is being constructed**, so the `JsonException` replaces the `SdkException` and the HTTP status is destroyed with it. A boundary that maps every `JsonException` to 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** and implement a defensive mapping that preserves the status code.

---

## REQUIRED READING

Load **before implementation starts**. The sheet deliberately does not carry their contents; these are the authoritative sources for gotchas and best practices:

| Skill | Governs | Why |
|---|---|---|
| `dotnet-client-initialization` | Step 1 — HttpClient, DI registration, client construction | The `HttpClient` must be long-lived and shared; the client wrapper is transient or singleton depending on your DI choice. |
| `dotnet-authentication` | Step 1 — Basic auth credentials, API key, password literal | Username = API key; password = `"x"`. Set before client construction. |
| `dotnet-calling-endpoints` | Steps 2–5 — operation signatures, named args, cancellation, async/await | Required vs optional params; many optionals have no default and must pass `null` explicitly. |
| `dotnet-models` | Steps 3–4 — request/response records, enums, unions, wrapper shapes | Records are immutable with `init`-only setters; `required` fields must be set; enums are `StringEnum<T>` not C# enums; responses wrap their payload in one field. |
| `dotnet-error-handling` | Step 6 — try/catch, Case A (typed error) vs Case B (raw error), JsonException mapping, boundary design | Typed errors have `TryGet…` accessors; raw errors expose `StatusCode` and `ReadAs…()` methods. `JsonException` from two sources needs opposite handling. |
| `dotnet-configuration-resilience` | Steps 1, 6 — retry options, timeout semantics, base URL override, Polly integration | `Timeout` is per-attempt not total; retries on transport failures happen on all verbs; no built-in logging hook. |

---

## Assumptions & Blockers

**Assumptions:**
- eShopOnWeb's user ID (or email) will be passed as the customer `reference` to Maxio, enabling idempotent lookups and re-sends.
- The Maxio sandbox has the product family "eshop-subscribe" (or its numeric ID) and plans "eshop-pro" (and "basic-plan") already created with the billing interval and price specified.
- Payment method is NOT required for subscription creation (per the contract); subscriptions are created in an active state or awaiting activation depending on the product/site configuration.
- HTTP Basic auth credentials (API key + "x") are available from configuration (environment variables or appsettings.json).
- The eShopOnWeb application already has a user identity context available at the endpoint (JWT or session); the integration reads the user ID from there.

**Blockers:**
None identified at planning time. All required SDK operations are present and documented.

