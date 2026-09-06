# Maxio Advanced Billing Integration — eShopOnWeb Subscription Plans

## Scope & Sequence

1. **JWT-auth endpoint: GET /api/subscription-plans**
   - Call `ListProducts()` to retrieve all products from the catalog
   - Filter products by `ProductFamily.Handle == "eshop-subscribe"` (from config)
   - Return: product id, name, handle, price (PriceInCents in $0.01 units), billing interval/unit

2. **JWT-auth endpoint: POST /api/subscriptions** (idempotent)
   - Extract logged-in user ID and email from JWT claims
   - **Step 2a (customer idempotency):** Call `ReadCustomerByReference(reference: userId)` to check for existing customer
     - If 404: call `CreateCustomer(body)` with `Reference` set to user ID
     - Otherwise: use existing customer
   - **Step 2b (subscription creation):** Call `CreateSubscription(body)` with `ProductHandle` or `ProductId` from request, `CustomerId` from step 2a
   - Return: subscription id, state, next billing date, plan name, plan price

3. **JWT-auth endpoint: GET /api/my-subscriptions**
   - Extract logged-in user ID from JWT claims
   - Call `ReadCustomerByReference(reference: userId)` to get customer (may not exist)
   - If customer found: call `ListCustomerSubscriptions(customerId)` to fetch all subscriptions
   - For each subscription, hydrate: plan name, plan price, state, next billing date
   - Return: array of subscriptions

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it. Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

| Step | Operation | Signature | Request Model + Fields | Response Envelope + Fields | Error Case | Pagination | Source |
|---|---|---|---|---|---|---|---|
| 1 (list plans) | `client.Products.ListProducts()` | `ListProducts(BasicDateField? dateField = null, ListProductsFilter? filter = null, DateTimeOffset? endDate = null, DateTimeOffset? endDatetime = null, DateTimeOffset? startDate = null, DateTimeOffset? startDatetime = null, bool? includeArchived = null, ListProductsInclude? include = null, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | — (no request body; all params optional query filters; defaults: page=1, perPage=20) | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>` — each item has `Product (product): MaxioAdvancedBilling.Models.Product !req` containing: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): MaxioAdvancedBilling.Models.Enums.IntervalUnit?`, `ProductFamily (product_family): MaxioAdvancedBilling.Models.ProductFamily?` (with `Handle (handle): string?`) | **Case B** — `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`: `.Error.StatusCode`, `.Error.ReadAsString()`, `.Error.ReadAsJson<T>()`, `.Error.ReadAsBytes()` | manual `page`+`perPage` | `operations/Products.md` |
| 2a (find customer) | `client.Customers.ReadCustomerByReference(reference)` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | `reference: string` (query param wire name `reference`) — the eShopOnWeb user ID to use as the customer reference | `MaxioAdvancedBilling.Models.CustomerResponse` with `Customer (customer): MaxioAdvancedBilling.Models.Customer !req` containing: `Id (id): int?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?` | **Case B** — `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`: `.Error.StatusCode` (404 if not found), `.Error.ReadAsString()`, `.Error.ReadAsJson<T>()`, `.Error.ReadAsBytes()` | none | `operations/Customers.md` |
| 2a (create customer) | `client.Customers.CreateCustomer(body)` | `CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, CancellationToken ct = default)` | `MaxioAdvancedBilling.Models.CreateCustomerRequest` with `Customer (customer): MaxioAdvancedBilling.Models.CreateCustomer !req` containing **required fields**: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`; **optional fields** to include for idempotency: `Reference (reference): string?` (set to user ID); other optional fields: `CcEmails (cc_emails): string?`, `Organization (organization): string?`, `Address (address): string?`, `Address2 (address_2): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `Locale (locale): string?`, `VatNumber (vat_number): string?`, `TaxExempt (tax_exempt): bool?`, `TaxExemptReason (tax_exempt_reason): string?`, `ParentId (parent_id): int?`, `SalesforceId (salesforce_id): string?` | `MaxioAdvancedBilling.Models.CustomerResponse` with `Customer (customer): MaxioAdvancedBilling.Models.Customer !req` containing: `Id (id): int?` (Maxio customer ID), `Reference (reference): string?` (the reference passed in) | **Case A** — `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>` with typed accessors: `.Error.TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422] or `.Error.TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` [fallback] | none | `operations/Customers.md`, `records-2-Cr-Ne.md` |
| 2b (create subscription) | `client.Subscriptions.CreateSubscription(body)` | `CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, CancellationToken ct = default)` | `MaxioAdvancedBilling.Models.CreateSubscriptionRequest` with `Subscription (subscription): MaxioAdvancedBilling.Models.CreateSubscription !req` containing: **required choice** `ProductHandle (product_handle): string?` OR `ProductId (product_id): int?` (choose one; both are optional but one must be set); **required choice** `CustomerId (customer_id): int?` OR `CustomerAttributes (customer_attributes): MaxioAdvancedBilling.Models.CustomerAttributes?` (use CustomerId from step 2a); **optional** `Reference (reference): string?` (for future lookup), `NextBillingAt (next_billing_at): DateTimeOffset?` (defaults to now + interval), `CouponCode (coupon_code): string?`, `PaymentCollectionMethod (payment_collection_method): MaxioAdvancedBilling.Models.Enums.CollectionMethod?`, others | `MaxioAdvancedBilling.Models.SubscriptionResponse` with `Subscription (subscription): MaxioAdvancedBilling.Models.Subscription?` containing: `Id (id): int?`, `State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `Customer (customer): MaxioAdvancedBilling.Models.Customer?` (includes name, email), `Product (product): MaxioAdvancedBilling.Models.Product?` (includes name, handle, price, interval) | **Case A** — `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>` with typed accessors: `.Error.TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422] or `.Error.TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` [fallback] | none | `operations/Subscriptions.md`, `records-2-Cr-Ne.md` |
| 3 (list subscriptions) | `client.Customers.ListCustomerSubscriptions(customerId)` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | `customerId: int` (Maxio customer ID from step 2a) | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>` — each item has `Subscription (subscription): MaxioAdvancedBilling.Models.Subscription?` containing: `Id (id): int?`, `State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `Product (product): MaxioAdvancedBilling.Models.Product?` (name, handle, price, interval) | **Case B** — `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`: `.Error.StatusCode`, `.Error.ReadAsString()`, `.Error.ReadAsJson<T>()`, `.Error.ReadAsBytes()` | none | `operations/Customers.md` |

### Subscription State Enum Values

`MaxioAdvancedBilling.Models.Enums.SubscriptionState` (wire values in parentheses):
- `Pending (pending)`
- `FailedToCreate (failed_to_create)`
- `Trialing (trialing)`
- `Assessing (assessing)`
- `Active (active)` ← user sees subscription as "active"
- `SoftFailure (soft_failure)`
- `PastDue (past_due)`
- `Suspended (suspended)`
- `Canceled (canceled)`
- `Expired (expired)`
- `Paused (paused)`
- `Unpaid (unpaid)`
- `TrialEnded (trial_ended)`
- `OnHold (on_hold)`
- `AwaitingSignup (awaiting_signup)`

### Client Construction & Configuration

**Using DI** (recommended for eShopOnWeb):

```csharp
services.AddMaxioAdvancedBillingClient(o =>
{
    o.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials
    {
        Username = configuration["Maxio:ApiKey"],  // from appsettings/env
        Password = "x"  // literal "x"
    };
    o.Environment = configuration["Maxio:Environment"] == "EU"
        ? MaxioAdvancedBilling.Servers.ServerEnvironment.Eu
        : MaxioAdvancedBilling.Servers.ServerEnvironment.Us;  // default: US
    o.Server.Production.Us.Site = configuration["Maxio:Subdomain"];  // e.g., "cp-exp-1"
    // Optional: o.Server.Production.Us.BaseUrl = "http://localhost:8080"; // if mocking
});
```

**Configuration binding keys** (map to .NET config):
- `Maxio:ApiKey` — Maxio API key (from env var `MAXIO_API_KEY`)
- `Maxio:Subdomain` — site subdomain, e.g. "cp-exp-1" (from env var `MAXIO_SITE_SUBDOMAIN`)
- `Maxio:Environment` — "US" or "EU" (from env var `MAXIO_ENVIRONMENT`; default US)
- `Maxio:ProductFamilyHandle` — e.g. "eshop-subscribe" (from env var `MAXIO_DEFAULT_PRODUCT_FAMILY`)
- `Maxio:BaseUrl` — optional override of base URL (from env var, if set)

**Namespaces to add:**
- `using MaxioAdvancedBilling;`
- `using MaxioAdvancedBilling.Api;`
- `using MaxioAdvancedBilling.Core.Authentication.Basic;`
- `using MaxioAdvancedBilling.Core.ErrorResponse;`
- `using MaxioAdvancedBilling.Errors;`
- `using MaxioAdvancedBilling.Models;`
- `using MaxioAdvancedBilling.Models.Enums;`
- `using MaxioAdvancedBilling.Servers;`

---

## Trap Notes

⚠ **Step 1 (list plans) — filtering by product family:** The `ListProducts` operation returns all products; the SDK offers no server-side filter by product family handle. **Filter on the client side** by iterating returned products and checking `product.ProductFamily?.Handle == config["Maxio:ProductFamilyHandle"]`. If `ProductFamily` is null on a product, skip it. **MUST load `dotnet-models`** to understand union/optional types and null checks on nested object graphs.

⚠ **Step 2a (find/create customer) — idempotency via reference:** If you create a customer with `Reference = userId` and then call `ReadCustomerByReference(userId)` again, it will return the same customer (not 404). **Do not guard against duplicate creation** by checking before create; instead: (1) attempt `ReadCustomerByReference` first; (2) if 404, create with `Reference` set to user ID; (3) if create fails with "reference already exists" error, retry the read. **MUST load `dotnet-error-handling`** to handle the 404 case (which is not an exception, it's a status code on the raw error) and the 422 case (CreateCustomerError with typed accessor).

⚠ **Step 2b (create subscription) — idempotency & payment method:** The operation accepts subscriptions **without requiring a payment method** if the product is configured as such (see sandbox catalog note: "payment method not required"). However, if you retry the same `CreateSubscription` call, it will attempt to create a duplicate subscription. Use `Reference` (set to a unique value like `userId-planHandle`) and check via `FindSubscription(reference)` before creating, or accept that the API will reject the duplicate with a 422 error. The sandbox setup allows no upfront payment, so focus on the happy path. **MUST load `dotnet-error-handling`** before writing the error boundary for 422 vs. 400.

⚠ **Step 2b & 3 (next billing date) — wire format:** The field `NextAssessmentAt (next_assessment_at)` arrives as ISO 8601 `DateTimeOffset`. Store/return it as-is; do not convert or reformat on the wire. **MUST load `dotnet-models`** to confirm `DateTimeOffset` deserialization and nullable handling.

⚠ **All operations — HTTP Basic auth:** Username must be your Maxio API key; password must be the literal string `"x"`. Do **not** use your password in the password field. **MUST load `dotnet-authentication`** before constructing the client.

⚠ **All operations — transient vs. singleton HTTP client:** The SDK accepts an `HttpClient` on construction and holds it for the lifetime of the SDK client. The `HttpClient` itself must be long-lived and reused (singleton or via `IHttpClientFactory`). Create a **single** `HttpClient` per app, not per request. **MUST load `dotnet-client-initialization`** to understand the HTTP pipeline and the `.AddMaxioAdvancedBillingClient()` DI extension.

⚠ **Error boundary — two classes of JsonException:** Read this **before** writing the catch ladder:
  - A drifted or malformed **2xx** response body (e.g. missing a `required` field) throws `System.Text.Json.JsonException` from deserialization, **not** an `SdkException` — the SDK exception-only design means this escapes an SDK-exception-only catch block.
  - A **non-2xx** body that doesn't match the operation's `{Operation}Error` shape throws `JsonException` *while constructing* the error object, replacing the `SdkException` and destroying the HTTP status.
  
  Map every `JsonException` to 5xx in your boundary, and **do not retry 5xx** if the cause was a shape mismatch (it will fail forever). **MUST load `dotnet-error-handling`** before the first `try/catch`.

⚠ **Logging & debugging:** The SDK's `RetryOptions` do **not** include a built-in logging hook; you must observe retries by wiring `ILogger` or `IHttpClientFactory` tracing. If you need to log Maxio calls (for audit), set up a logging handler in the `HttpClient` pipeline before registering the SDK client. **MUST load `dotnet-configuration-resilience`** to understand retry bounds and how to wire custom observability.

---

## REQUIRED READING

The following companion skills must be loaded **before implementation starts**. The sheet deliberately does not carry their contents — these skills contain the parts a one-line note cannot (defaults, worked examples, what you must wire yourself). Read them in order:

1. **`dotnet-client-initialization`** — Step 1 (client & DI setup): how to construct `MaxioAdvancedBillingClient`, the role of `HttpClient` (long-lived, singleton), the `.AddMaxioAdvancedBillingClient()` DI extension, and why DI is preferred for eShopOnWeb.
2. **`dotnet-authentication`** — Step 2 (credentials & Basic auth): how to set `BasicAuthCredentials` on the options (Username = API key, Password = literal `"x"`), when to set it (before client construction or in the DI callback), and how to load the API key from configuration.
3. **`dotnet-calling-endpoints`** — Step 3 (calling operations): how to invoke `client.Products.ListProducts()`, `client.Customers.ReadCustomerByReference()`, etc.; named vs. positional arguments; nullable parameter handling (pass `null` to skip optional filters).
4. **`dotnet-models`** — Step 4 (understanding request/response shapes): field names, JSON wire names, `required` vs. optional, nullable types, and how to null-check nested objects like `product.ProductFamily?.Handle`.
5. **`dotnet-error-handling`** — Step 5 (exception boundaries): Case A vs. Case B error types; how to use typed `.TryGet…()` accessors; why `JsonException` escapes the SDK boundary and how to handle it; the two classes of `JsonException` and their remediation.
6. **`dotnet-configuration-resilience`** — Step 6 (retries & timeouts): `RetryOptions` members (`MaxRetries`, `Delay`, `Timeout`, `HttpMethodsToRetry`, `StatusCodesToRetry`), why `Timeout` bounds a single attempt (not the whole call), why POST is retried on **transport failure** but not on **error status** (unless you add the status to `StatusCodesToRetry`), and the floor of 1 retry (you cannot disable retries).
7. **`dotnet-testing`** — Step 7 (unit testing): how to stub the SDK via the `HttpClient` constructor argument, match your project's test framework (xUnit, NUnit, MSTest), and write assertions on calls made.

---

## Assumptions & Blockers

- **Assumption:** The eShopOnWeb app already has JWT authentication in place for endpoints and can extract user ID and email from `User.FindFirst(ClaimTypes.NameIdentifier)` or similar.
- **Assumption:** Maxio sandbox site (`cp-exp-1`) is already seeded with products `eshop-pro` (ID 7126957) and `basic-plan` (ID 7126958) as stated in the catalog.
- **Assumption:** The .NET configuration system (appsettings.json + environment variables) is available and the four Maxio keys are already bound or will be added by the main agent.
- **Assumption:** The eShopOnWeb project targets `.NET 6+` or `.NET Framework 4.6.1+` (SDK minimum is `netstandard2.0`).
- **No Blockers** — all operations, enums, and error types are defined in the SDK map and present in the published NuGet package. The sandbox has no capacity limits noted for this integration scope.
