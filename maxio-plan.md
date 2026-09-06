# Maxio Subscription Billing Integration Plan — eShopOnWeb PublicApi

## Scope & Sequence

1. **List subscription plans** (GET /api/subscription-plans) — retrieve all active products from the configured product family
   - Operation: `ListProducts` (filter by product family handle)
   
2. **Idempotent customer + subscription creation** (POST /api/subscriptions) — create a Maxio customer (keyed by eShop userId) and subscription to a plan
   - Operations: `ReadCustomerByReference` (lookup by userId reference), `CreateCustomer` (if not found), `CreateSubscription`
   - Idempotency: use eShop userId as the `reference` field on both customer and subscription creation

3. **Retrieve user's subscription state** (GET /api/my-subscriptions) — list the logged-in user's active subscriptions
   - Operations: `ReadCustomerByReference` (lookup by userId), `ListCustomerSubscriptions` (fetch subscriptions for that customer)

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operations table

| Operation | Signature | Request model + fields | Response envelope + inner fields | Error case | Pagination | Source |
|---|---|---|---|---|---|---|
| ListProducts | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — all 8 params (`dateField`…`include`) nullable, no default, must pass explicitly; `page` defaults to 1, `perPage` to 20 | None (GET query params: `date_field`, `filter`, `end_date`, `end_datetime`, `start_date`, `start_datetime`, `page`, `per_page`, `include_archived`, `include`) | `IReadOnlyList<ProductResponse>` — each wraps one `ProductResponse.Product (product): Product !req` with fields: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?` | Case B: `SdkException<RawError>` — `.StatusCode`, `.ReadAsString()`, `.ReadAsJson<T>()`, `.ReadAsBytes()` | Manual `page`+`perPage` | `operations/Products.md` |
| ReadCustomerByReference | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — `reference` is a query param (`reference` wire name) | None (reference passed as query param) | `CustomerResponse` — wraps `CustomerResponse.Customer (customer): Customer !req` with fields: `Id (id): int?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, `Address (address): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, others optional | Case B: `SdkException<RawError>` — `.StatusCode`, `.ReadAsString()`, `.ReadAsJson<T>()`, `.ReadAsBytes()` | None | `operations/Customers.md` |
| CreateCustomer | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default, must pass explicitly | `CreateCustomerRequest` — wraps `CreateCustomerRequest.Customer (customer): CreateCustomer !req` with **required** fields: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`; **optional** fields: `Reference (reference): string?`, `Phone (phone): string?`, `Address (address): string?`, `Address2 (address_2): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `CcEmails (cc_emails): string?`, `Organization (organization): string?`, `Locale (locale): string?`, `VatNumber (vat_number): string?`, `TaxExempt (tax_exempt): bool?`, `TaxExemptReason (tax_exempt_reason): string?`, `ParentId (parent_id): int?`, `SalesforceId (salesforce_id): string?` | `CustomerResponse` — wraps `CustomerResponse.Customer (customer): Customer !req` (same shape as ReadCustomerByReference response) | Case A: `SdkException<CreateCustomerError>` — `.Error.TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422], `.Error.TryGetRawError(out RawError)` [fallback] | None | `operations/Customers.md` |
| CreateSubscription | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, no default, must pass explicitly | `CreateSubscriptionRequest` — wraps `CreateSubscriptionRequest.Subscription (subscription): CreateSubscription !req` with **idempotency fields**: `Reference (reference): string?` (use eShop userId here for idempotency key); **product selection** (choose one): `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`; **customer selection** (choose one): `CustomerId (customer_id): int?`, `CustomerReference (customer_reference): string?`, `CustomerAttributes (customer_attributes): CustomerAttributes?`; **optional billing fields**: `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `PaymentProfileId (payment_profile_id): int?`, `PaymentProfileAttributes (payment_profile_attributes): PaymentProfileAttributes?`; `InitialBillingAt (initial_billing_at): DateTimeOffset?`, `NextBillingAt (next_billing_at): DateTimeOffset?`; others optional | `SubscriptionResponse` — wraps `SubscriptionResponse.Subscription (subscription): Subscription !req` with fields: `Id (id): int?`, `CustomerId (customer_id): int?`, `ProductId (product_id): int?` (inferred), `State (state): SubscriptionState?`, `BalanceInCents (balance_in_cents): long?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `ActivatedAt (activated_at): DateTimeOffset?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, others optional | Case A: `SdkException<CreateSubscriptionError>` — `.Error.TryGetErrorListResponse1(out ErrorListResponse1)` [422], `.Error.TryGetRawError(out RawError)` [fallback] | None | `operations/Subscriptions.md` |
| ListCustomerSubscriptions | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — `customerId` is a path param | None (customerId in URL path) | `IReadOnlyList<SubscriptionResponse>` — each wraps one `SubscriptionResponse.Subscription (subscription): Subscription !req` (same shape as CreateSubscription response) | Case B: `SdkException<RawError>` — `.StatusCode`, `.ReadAsString()`, `.ReadAsJson<T>()`, `.ReadAsBytes()` | None | `operations/Customers.md` |
| ReadSubscription | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `subscriptionId` is path param; `include` nullable query param (pass `null` to skip) | None (subscriptionId in URL path, include as optional query param) | `SubscriptionResponse` — wraps `SubscriptionResponse.Subscription (subscription): Subscription !req` (same shape as CreateSubscription response) | Case B: `SdkException<RawError>` — `.StatusCode`, `.ReadAsString()`, `.ReadAsJson<T>()`, `.ReadAsBytes()` | None | `operations/Subscriptions.md` |

### Enums referenced

| Enum | Namespace | Values (member names: wire values) | Source |
|---|---|---|---|
| `SubscriptionState` | `MaxioAdvancedBilling.Models.Enums` | `Pending (pending)`, `Trialing (trialing)`, `Active (active)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` [+ others not listed — see map for full set] | `models/enums.md` |
| `CollectionMethod` | `MaxioAdvancedBilling.Models.Enums` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` | `models/enums.md` |
| `IntervalUnit` | `MaxioAdvancedBilling.Models.Enums` | `Day (day)`, `Month (month)` | `models/enums.md` |
| `BasicDateField` | `MaxioAdvancedBilling.Models.Enums` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` | `models/enums.md` |
| `SubscriptionInclude` | `MaxioAdvancedBilling.Models.Enums` | `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)` | `models/enums.md` |

### Client construction & auth

Auth scheme: HTTP **Basic** — `Username` = API key (from `Maxio:ApiKey` config), `Password` = literal string `"x"` (not configurable).

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers; // ServerEnvironment enum

// From configuration
var apiKey = configuration["Maxio:ApiKey"];           // MAXIO_API_KEY env var
var subdomain = configuration["Maxio:Subdomain"];     // MAXIO_SITE_SUBDOMAIN env var
var environment = configuration["Maxio:Environment"]; // "US" or "EU", defaults to US
var baseUrlOverride = configuration["Maxio:BaseUrl"]; // optional override (e.g. for mock/local testing)

var serverEnv = environment?.Equals("EU", StringComparison.OrdinalIgnoreCase) == true
    ? ServerEnvironment.Eu
    : ServerEnvironment.Us;

var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials
    {
        Username = apiKey,
        Password = "x"
    },
    Environment = serverEnv,
};

// Override base URL if provided (e.g., for local testing)
if (!string.IsNullOrEmpty(baseUrlOverride))
{
    options.Server = new ServerOptions();
    options.Server.Production = new ProductionOptions();
    options.Server.Production.Us = new UrlServerOptions { BaseUrl = baseUrlOverride };
    // Note: for EU, also set options.Server.Production.Eu if needed
}

// Register client for dependency injection; reuse HttpClient
var client = new MaxioAdvancedBillingClient(httpClient, options);
// OR register via DI:
// services.AddMaxioAdvancedBillingClient(o => 
// {
//     o.BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" };
//     o.Environment = serverEnv;
// });
```

**Namespaces required:**
```csharp
using MaxioAdvancedBilling;                        // client & base types
using MaxioAdvancedBilling.Api;                    // operation controllers (not always needed)
using MaxioAdvancedBilling.Models;                 // request/response records
using MaxioAdvancedBilling.Models.Enums;           // StringEnum types
using MaxioAdvancedBilling.Errors;                 // error types
using MaxioAdvancedBilling.Core.Authentication.Basic; // BasicAuthCredentials
using MaxioAdvancedBilling.Servers;                // ServerEnvironment
```

---

## TRAP NOTES

⚠ **Step 1 (client initialization)** — The `HttpClient` passed to `MaxioAdvancedBillingClient` constructor must be long-lived and reused, never created fresh per request. Use `IHttpClientFactory` for DI registration in ASP.NET Core to manage pooling. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ **Step 2 (authentication)** — Credentials must be set on the options *before* constructing the client. Load the API key from `Maxio:ApiKey` config (bound from `MAXIO_API_KEY` env var). The password is always the literal string `"x"` — this is part of the Maxio API contract, not a secret. **MUST load `dotnet-authentication`** before writing credential handling code.

⚠ **Step 2–3 (idempotent customer creation)** — To prevent duplicate customers on double-click subscription button, set the `reference` field to the eShop user's ID (or email, any unique local identifier) when creating both the customer and the subscription. Then, **before** creating a subscription, call `ReadCustomerByReference` to check if the customer already exists. If it does, use that customer's `Id` for the subscription. If 404 (not found), proceed to create the customer. This pattern avoids "duplicate reference" 422 errors from Maxio.

⚠ **Step 3 (calling operations)** — Many optional parameters have no C# default and mis-bind in positional calls. Always use **named arguments** for optional params (e.g., `dateField: null, filter: null, …`). The cancellation token parameter is literally named `ct` (not `cancellationToken` or `CancellationToken`), so in named calls write `ct: cancellationToken`. **MUST load `dotnet-calling-endpoints`** before writing the first operation call.

⚠ **Step 4 (models & enums)** — Enums are `StringEnum<T>` records, not C# enums. Construct them with static members (e.g., `SubscriptionState.Active`) or `Type.FromValue("wire")`. Union types use factory methods and `TryGet…(out …)` accessors — never `new`. **MUST load `dotnet-models`** before wiring request/response bodies.

⚠ **Step 5 (error handling) — Case A vs. Case B distinction** — This integration uses both:
   - **Case A (typed)** operations: `CreateCustomer`, `CreateSubscription` throw `SdkException<{OperationName}Error>` with typed `TryGet…` accessors.
   - **Case B (raw)** operations: `ListProducts`, `ReadCustomerByReference`, `ListCustomerSubscriptions` throw `SdkException<RawError>` — no typed accessors, use `.StatusCode`, `.ReadAsString()`, etc.
   - **Deserialization errors**: A drifted or malformed **2xx** body (missing required field) surfaces as `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — catch-only-SDK-exceptions leaves it unhandled. A **non-2xx** body that doesn't match the operation's `{Operation}Error` shape throws `JsonException` *during error construction*, replacing the `SdkException` and destroying the HTTP status — a boundary that maps `JsonException` to 500 then retries loses the actual error. **MUST load `dotnet-error-handling`** before writing the error/exception boundary; handle *both* `SdkException<T>` and `JsonException`.

⚠ **Step 5 (timeouts & retries)** — The SDK's `RetryOptions.Timeout` is **per-attempt**, not per-call. `HttpMethodsToRetry` gates only **HTTP status code** triggers (e.g., 503), not **transport failures** (e.g., `HttpRequestException`) — those retry on **every** verb including `POST`, so non-idempotent writes may execute more than once. `MaxRetries` minimum is 1 (0 is rejected); there is no built-in logging hook. **MUST load `dotnet-configuration-resilience`** before tuning retry/timeout settings.

⚠ **Two JsonException sources require opposite handling** — always include both rows in the error boundary:
   1. A malformed or drifted **2xx** response body (missing `required` field) throws `JsonException` from deserialization — **not** caught by `catch (SdkException)`.
   2. A **non-2xx** body that doesn't match the operation's typed error shape throws `JsonException` *while constructing the error object itself*, **replacing** the `SdkException` and destroying the HTTP status.

---

## REQUIRED READING

Load these skills **before implementation starts**:

| Skill | Governs | Notes |
|---|---|---|
| `dotnet-client-initialization` | Client construction, DI registration, HttpClient pooling | Must configure long-lived HttpClient before passing to SDK client |
| `dotnet-authentication` | Credential loading, auth scheme setup | HTTP Basic: username = API key, password = literal `"x"` |
| `dotnet-calling-endpoints` | Operation calls, parameter binding, async/cancellation | Use named arguments for optional params; `ct:` is the token param name |
| `dotnet-models` | Request/response record construction, enum/union handling | `StringEnum<T>` + static members or `.FromValue(wire)`; no `new` for unions |
| `dotnet-error-handling` | Exception boundary, Case A/B distinction, JsonException handling | Catch both `SdkException<T>` and `JsonException`; status is lost on error deserialization failure |
| `dotnet-configuration-resilience` | Retry/timeout tuning, base-URL override, logging | `Timeout` is per-attempt; transport failures retry all verbs; `MaxRetries` ≥ 1 |

The sheets deliberately do not carry these skills' contents — load them before wiring code.

---

## ASSUMPTIONS & BLOCKERS

**No blockers. The plan is actionable.**

**Assumption:** The eShop "user ID" (the identity the plan stores in the Maxio `customer.reference` field) is stable, unique, and never reassigned to a different user. The integration uses this reference as an idempotency key: if the same user clicks "subscribe" twice in quick succession, the second request will find the customer already created and reuse it, avoiding a 422 "duplicate reference" error.

**Assumption:** The Maxio sandbox site (`cp-exp-1`) is pre-seeded with:
- Product family handle: `eshop-subscribe`
- Plan handles: `eshop-pro` ($299/mo), `basic-plan` ($29/mo)
- Both plans are active and queryable via `ListProducts`

If these assumptions are false, the GET /api/subscription-plans endpoint will return an empty list, and POST /api/subscriptions will fail on product lookup.
