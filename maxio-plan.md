# Maxio Advanced Billing Integration — Subscription Plans & Management

## Scope & Implementation Sequence

1. **Client configuration & registration** — wire Maxio SDK client with credentials and sandbox environment
2. **List subscription plans** — HTTP GET `/api/subscription-plans` → fetch plans in a product family (handle: `eshop-subscribe`)
3. **Find or create customer** — for the logged-in shopper; idempotent lookup by email reference
4. **Create subscription** — HTTP POST `/api/subscriptions` → bind customer to a plan
5. **Retrieve subscriptions** — HTTP GET `/api/my-subscriptions` → list active/inactive for the shopper

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

| Operation | Signature | Request model + fields | Response envelope | Error case | Pagination | Source |
|---|---|---|---|---|---|---|
| **ListProductsForProductFamily** | `Task<IReadOnlyList<ProductResponse>> ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | **No request body** · Query params: `productFamilyId` (path), `page`, `per_page`, `date_field`, `filter`, `start_date`, `end_date`, `start_datetime`, `end_datetime`, `include_archived`, `include` | `IReadOnlyList<ProductResponse>` · each item: `ProductResponse { Product (product): Product !req }` · `Product` fields: `Id`, `Name`, `Handle`, `PriceInCents`, `Interval`, `IntervalUnit`, `TrialPriceInCents`, `TrialInterval`, `TrialIntervalUnit`, `Description`, `CreatedAt`, `UpdatedAt` | **Case A (typed):** `SdkException<ListProductsForProductFamilyError>` · `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage`; defaults: `page=1`, `perPage=20` | `map/operations/ProductFamilies.md` |
| **ReadCustomerByReference** | `Task<CustomerResponse> ReadCustomerByReference(string reference, CancellationToken ct = default)` | **No request body** · Query param: `reference` (the shopper's JWT sub / email) | `CustomerResponse { Customer (customer): Customer !req }` · `Customer` fields: `Id`, `FirstName`, `LastName`, `Email`, `Reference`, `CreatedAt`, `UpdatedAt`, `Phone`, `Organization`, `State`, `Country`, `TaxExempt`, `Locale`, `Address`, `City`, `Zip` | **Case B:** `SdkException<RawError>` · `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | none | `map/operations/Customers.md` |
| **CreateCustomer** | `Task<CustomerResponse> CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` | `CreateCustomerRequest { Customer (customer): CreateCustomer !req }` · `CreateCustomer` fields: `FirstName (first_name) !req`, `LastName (last_name) !req`, `Email (email) !req`, `Reference (reference)?`, `Organization (organization)?`, `Address (address)?`, `City (city)?`, `State (state)?`, `Zip (zip)?`, `Country (country)?`, `Phone (phone)?`, `Locale (locale)?`, `CcEmails (cc_emails)?`, `TaxExempt (tax_exempt)?` | `CustomerResponse { Customer (customer): Customer !req }` · same structure as ReadCustomerByReference | **Case A (typed):** `SdkException<CreateCustomerError>` · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `map/operations/Customers.md` |
| **CreateSubscription** | `Task<SubscriptionResponse> CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription !req }` · `CreateSubscription` fields (relevant subset): `CustomerId (customer_id)?`, `CustomerAttributes (customer_attributes)?` [nested], `ProductHandle (product_handle)?`, `ProductId (product_id)?`, `ProductPricePointHandle (product_price_point_handle)?`, `ProductPricePointId (product_price_point_id)?`, `CouponCode (coupon_code)?`, `PaymentCollectionMethod (payment_collection_method)?`, `Reference (reference)?`, `ReceivesInvoiceEmails (receives_invoice_emails)?`, `DeferSignup (defer_signup): bool? = false` | `SubscriptionResponse { Subscription (subscription): Subscription !req }` · `Subscription` fields: `Id`, `State` (SubscriptionState enum), `BalanceInCents`, `ProductPriceInCents`, `CurrentPeriodEndsAt`, `NextAssessmentAt`, `ActivatedAt`, `CanceledAt`, `CreatedAt`, `UpdatedAt`, `Customer` (nested), `Product` (nested), `Reference` | **Case A (typed):** `SdkException<CreateSubscriptionError>` · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `map/operations/Subscriptions.md` |
| **ListCustomerSubscriptions** | `Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | **No request body** · Path param: `customerId` | `IReadOnlyList<SubscriptionResponse>` · same structure as CreateSubscription response | **Case B:** `SdkException<RawError>` · `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | none | `map/operations/Customers.md` |

### Model Namespaces & Key Enums

**Enum: `SubscriptionState`** (namespace `MaxioAdvancedBilling.Models.Enums`)
- Members: `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Trialing (trialing)`, `PastDue (past_due)`, `Suspended (suspended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)`, `FailedToCreate (failed_to_create)`, `Assessing (assessing)`, `SoftFailure (soft_failure)`, `TrialEnded (trial_ended)`, `Unpaid (unpaid)`, `Pending (pending)`

**Enum: `IntervalUnit`** (namespace `MaxioAdvancedBilling.Models.Enums`)
- Members: `Day (day)`, `Month (month)`

**Enum: `CollectionMethod`** (namespace `MaxioAdvancedBilling.Models.Enums`)
- Members: `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)`

**Record namespaces:**
- `MaxioAdvancedBilling.Models`: `CreateCustomerRequest`, `CustomerResponse`, `Customer`, `CreateSubscriptionRequest`, `ProductResponse`, `Product`, `SubscriptionResponse`, `Subscription`, `CreateCustomer`, `CreateSubscription`
- `MaxioAdvancedBilling.Models.Enums`: all enums above
- `MaxioAdvancedBilling`: client and options
- `MaxioAdvancedBilling.Core.Authentication.Basic`: `BasicAuthCredentials`
- `MaxioAdvancedBilling.Servers`: `ServerEnvironment`
- `MaxioAdvancedBilling.Core.Configuration`: `RetryOptions`

### Client Construction & Configuration

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;

var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials 
    { 
        Username = configuration["Maxio:ApiKey"], // API key from config
        Password = "x" 
    },
    Environment = ServerEnvironment.Us, // or .Eu; from config "Maxio:Environment"
};

// If using a custom base URL (non-standard sandbox/production):
// options.Server.Production.Us.BaseUrl = configuration["Maxio:BaseUrl"];
// options.Server.Production.Us.Site = configuration["Maxio:Subdomain"];

var client = new MaxioAdvancedBillingClient(httpClient, options);
// where httpClient is an IHttpClientFactory-managed HttpClient (long-lived, reused)
```

---

## Trap Notes

⚠ **Step 1 (client configuration)** — the `HttpClient` passed to the SDK must be long-lived and obtained from `IHttpClientFactory`, not created per-request. The SDK wraps it but does not own its lifetime. **MUST load `dotnet-client-initialization`** before wiring the client into DI or the controller.

⚠ **Step 1 (authentication)** — credentials are HTTP Basic: **`Username` = your Maxio API key (from environment), `Password` = the literal string `"x"`**. A swapped or missing password causes all calls to fail with 401 Unauthorized. **MUST load `dotnet-authentication`** before setting credentials or constructing the client.

⚠ **Step 1 (server configuration)** — if no `Maxio:BaseUrl` is configured, the SDK uses the environment's base URL (`https://{site}.chargify.com` for US or `https://{site}.ebilling.maxio.com` for EU). The `{site}` placeholder is replaced by `options.Server.Production.Us.Site` (or .Eu). This defaults to the account's subdomain if not overridden. On sandbox, the subdomain is typically `cp-exp-1`. **MUST load `dotnet-configuration-resilience`** to confirm retry/timeout semantics before deciding if a call can be re-sent on failure.

⚠ **Step 2 (list products)** — the `ListProductsForProductFamily` operation requires the product family ID or handle (e.g., `"eshop-subscribe"`). The API accepts both numeric ID and handle syntax (`"handle:eshop-subscribe"`); use the handle if stable. Pagination is manual: you control `page` and `perPage`. **MUST load `dotnet-calling-endpoints`** to confirm how to pass optional query parameters (many have no C# default, so named arguments are safer than positional).

⚠ **Step 3 (idempotent customer lookup)** — before creating a customer, call `ReadCustomerByReference(reference: shopper_email_or_identifier)` to check if the customer already exists. If 404, create; if 200, reuse the existing customer. The `Reference` field is arbitrary text you choose (e.g., the shopper's UUID or email in your system) and must be unique per site. **MUST load `dotnet-error-handling`** to distinguish a 404 (customer not found, safe to create) from other error codes.

⚠ **Step 3 & 4 (customer creation & subscription binding)** — `CreateCustomer` and `CreateSubscription` both throw `SdkException<…>` on error (no no-throw Result variant exists). A 422 Unprocessable Entity wraps validation errors in the typed error accessors (e.g., `TryGetCustomerErrorResponse1`). Always wrap both calls in a try/catch that reads the error payload via the accessor, not by parsing `.ToString()`. **MUST load `dotnet-error-handling`** before writing the exception boundary.

⚠ **Step 4 (create subscription — payment method optional)** — the spec says both plans ("Pro" and "Basic") have `payment_method_not_required = true`. The SDK will not validate or collect card details if the product does not require one. Omit or set `PaymentProfileId` and all payment-related fields to null/empty. If the backend rejects a subscription creation for "missing payment method," it contradicts the plan configuration in Maxio. **MUST load `dotnet-calling-endpoints`** to confirm optional parameter handling (many SDK fields have no C# default, so `null` must be passed explicitly).

⚠ **Step 5 (list subscriptions)** — the `ListCustomerSubscriptions(int customerId)` operation has no pagination; it returns all subscriptions for a single customer as a flat list. If the shopper has many subscriptions, the list may be large but is not paginated by the API. Cache or filter on the client side if needed. **MUST load `dotnet-calling-endpoints`** to confirm the signature and return type.

⚠ **Both JsonException paths must be handled in the error boundary:**
- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary. These rows belong in the FIRST sheet, not a later revision: the boundary is written early, and a caveat that arrives afterwards arrives too late to shape it.

---

## REQUIRED READING

Before implementation starts, **load these companion skills in order.** The sheet below names each and the step(s) it governs. Do **not** invent answers from prior knowledge; the skills carry defaults, worked examples, and caveats a signature cannot show.

| Skill | Step(s) | Purpose |
|---|---|---|
| `dotnet-client-initialization` | 1 | Client & DI wiring; `IHttpClientFactory` ownership; no per-request construction |
| `dotnet-authentication` | 1 | Basic auth setup; credential loading from configuration; username = API key, password = `"x"` |
| `dotnet-calling-endpoints` | 2, 3, 4, 5 | Operation signatures; required vs optional parameters; request body vs query params; cancellation token binding |
| `dotnet-models` | 3, 4 | Record construction; immutable init-only setters; required fields; nullable vs optional; wire names |
| `dotnet-error-handling` | 3, 4, 5 | Exception boundaries; typed vs raw errors; `TryGet…` accessors; `JsonException` handling (2xx and non-2xx) |
| `dotnet-configuration-resilience` | 1, 2 | Retry semantics; timeout (per-attempt, not total); server override; logging hooks; idempotence on transport failures |
| `dotnet-testing` | (after impl) | Test seams; `HttpClient` mocking; test framework alignment |

---

## Assumptions & Blockers

- **Assumption:** The shopper's identity is available from the JWT token (via `User.FindFirst(…)` or an identity claims accessor) as a string identifier (email, UUID, or custom reference). This is used as the `Reference` on `CreateCustomer` and `ReadCustomerByReference`.
- **Assumption:** The product family handle (`"eshop-subscribe"`) is stable and seeded on the sandbox site (`cp-exp-1`). If re-seeding changes handles, the implementation must be updated or made configurable.
- **Assumption:** Payment is **not** required for either plan; the configuration `payment_method_not_required = true` holds for both. If the provider rejects a subscription without a card, the plan configuration in Maxio does not match the spec.
- **Assumption:** The customer reference (email/identifier used with `ReadCustomerByReference`) is unique per site. If your identity model uses non-unique or non-persistent identifiers, collisions or re-creation will occur.
- **No blockers identified.** All operations and models needed are available in the map.
