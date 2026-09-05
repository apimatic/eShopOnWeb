# Maxio Advanced Billing Integration Plan for eShopOnWeb

**Plan file location**: `C:\claude-runs\t1h45ali-maxio-sdk-haiku45high-006\repo\maxio-plan.md`

## NuGet Package & Version

| | |
|---|---|
| **Package ID** | `AsadAli.AdvancedBilling.Sdk` |
| **Version** | `v1.0.2` (source commit: `15db14b2e…`, tagged `v1.0.2`) |
| **NuGet URL** | https://www.nuget.org/packages/AsadAli.AdvancedBilling.Sdk/1.0.2 |
| **Installation** | `dotnet add package AsadAli.AdvancedBilling.Sdk --version 1.0.2` |
| **Root namespace** | `MaxioAdvancedBilling` (note: differs from package ID) |

**CRITICAL**: Version 3.26.1 (mentioned by coordinator) does not exist on nuget.org. The SDK map is pinned to **v1.0.2**. Install this exact version; mismatches cause compilation errors on generated types.

---

## Scope & Sequence

| Step | Description | Operations |
|------|-------------|-----------|
| 1 | **Client initialization & DI** | Register MaxioAdvancedBillingClient in dependency injection; configure Basic auth (API key + "x"); set Environment (US/EU); override base URL if needed for sandbox |
| 2 | **Configuration** | Load `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, optional `Maxio:BaseUrl` from IConfiguration |
| 3 | **Public API endpoints on AuthController/SubscriptionController** | Implement three JWT-authenticated endpoints (separate from Web host, possibly on different port) |
| 4 | **GET /api/subscription-plans** | Call `client.Products.ListProducts(…)` → filter to product family; return plan summaries (handle, name, price, interval) |
| 5 | **POST /api/subscriptions** | Idempotent customer lookup by reference (user identity); create customer if absent; create subscription with product handle; return subscription details + next billing date |
| 6 | **GET /api/my-subscriptions** | Look up customer by reference; list customer's subscriptions via `client.Customers.ListCustomerSubscriptions(…)` or `client.Subscriptions.ListSubscriptions(…)` with state filter; return subscription summaries |
| 7 | **Error boundary** | Catch `SdkException<TError>` (Case A & B); map HTTP status to user-friendly message; log provider errors; no secrets in output |
| 8 | **Testing** | Verify against sandbox plans/components; test idempotent create (same customer/product → existing subscription); confirm state/next billing date in response |

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `MaxioAdvancedBilling.Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operations & Models

| Controller / Method | Signature | Request Model | Response Envelope | Error Case | Pagination | Source |
|---|---|---|---|---|---|---|
| **client.Products** | | | | | | |
| `ReadProductByHandle` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | none (apiHandle passed directly) | `ProductResponse { Product (product): Product !req }` | Case B: `SdkException<RawError>` [404/other] → `ex.Error.StatusCode`, `ReadAsString()` | none | `operations/Products.md` |
| `ListProducts` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | none (query params only; 8 params nullable, pass `null` to skip; `page`/`perPage` have defaults 1/20) | `IReadOnlyList<ProductResponse>` (list of responses, each contains one `Product`) | Case B: `SdkException<RawError>` → `ex.Error.StatusCode`, `ReadAsString()` | manual `page`+`perPage` | `operations/Products.md` |
| **client.Subscriptions** | | | | | | |
| `CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly** | `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription !req }` with **required** inner fields: `ProductHandle` (wire: `product_handle`) OR `ProductId`; `CustomerId` OR `CustomerAttributes` (wire: `customer_attributes`); **optional**: `Reference`, `PaymentProfileId`, `CouponCode`, `Components` (wire: `components`), `NextBillingAt`, others | `SubscriptionResponse { Subscription (subscription): Subscription? }` | Case A: `SdkException<CreateSubscriptionError>` → `TryGetErrorListResponse1(out ErrorListResponse1)` [422] or `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |
| `ListSubscriptions` | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 14 params nullable (pass `null` to skip); `page`/`perPage` have defaults 1/20 | none (query params only) | `IReadOnlyList<SubscriptionResponse>` | Case B: `SdkException<RawError>` → `ex.Error.StatusCode`, `ReadAsString()` | manual `page`+`perPage` | `operations/Subscriptions.md` |
| `ReadSubscription` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` nullable, no default → **must pass explicitly** | none (path param `subscriptionId`) | `SubscriptionResponse { Subscription (subscription): Subscription? }` | Case B: `SdkException<RawError>` → `ex.Error.StatusCode`, `ReadAsString()` | none | `operations/Subscriptions.md` |
| **client.Customers** | | | | | | |
| `CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly** | `CreateCustomerRequest { Customer (customer): CreateCustomer !req }` with **optional** fields: `FirstName`, `LastName`, `Email` (wire: `email`), `Reference` (wire: `reference`) — **store app user ID in `Reference` for idempotency**; no required fields per map, but **Notes say** create succeeds only with at least name/email or see provider Notes | `CustomerResponse { Customer (customer): Customer !req }` | Case A: `SdkException<CreateCustomerError>` → `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] or `TryGetRawError(out RawError)` [fallback] | none | `operations/Customers.md` |
| `ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | none (reference passed as query param) | `CustomerResponse { Customer (customer): Customer !req }` | Case B: `SdkException<RawError>` [404 if not found] → `ex.Error.StatusCode`, `ReadAsString()` | none | `operations/Customers.md` |
| `ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | none (customerId in path) | `IReadOnlyList<SubscriptionResponse>` | Case B: `SdkException<RawError>` → `ex.Error.StatusCode`, `ReadAsString()` | none (no pagination) | `operations/Customers.md` |

### Key Models & Fields

**All model types below live in namespace `MaxioAdvancedBilling.Models` unless otherwise noted.**

#### Subscription (response model)

| Field (wire name) | Type | Notes |
|---|---|---|
| `Id (id)` | `int?` | Maxio-assigned subscription ID |
| `State (state)` | `SubscriptionState?` | Enum: `Active`, `Trialing`, `PastDue`, `Suspended`, `Canceled`, `Expired`, etc. |
| `BalanceInCents (balance_in_cents)` | `long?` | Outstanding balance in cents |
| `ProductPriceInCents (product_price_in_cents)` | `long?` | Current plan price in cents |
| `CurrentPeriodEndsAt (current_period_ends_at)` | `DateTimeOffset?` | End of current billing period |
| `NextAssessmentAt (next_assessment_at)` | `DateTimeOffset?` | **Next billing date** |
| `ActivatedAt (activated_at)` | `DateTimeOffset?` | When subscription became active |
| `TrialStartedAt (trial_started_at)` | `DateTimeOffset?` | Trial start date if applicable |
| `TrialEndedAt (trial_ended_at)` | `DateTimeOffset?` | Trial end date if applicable |
| `CanceledAt (canceled_at)` | `DateTimeOffset?` | Cancellation date if canceled |
| `ExpiresAt (expires_at)` | `DateTimeOffset?` | Expiration date if applicable |
| `CouponCode (coupon_code)` | `string?` | Active coupon code |
| `Customer (customer)` | `Customer?` | Nested customer object |
| `Product (product)` | `Product?` | Nested product object |
| `Reference (reference)` | `string?` | Your app's reference (typically your user ID) |
| `PaymentCollectionMethod (payment_collection_method)` | `CollectionMethod?` | `Automatic`, `Invoice`, `Remittance`, `Prepaid` |
| `CreditCard (credit_card)` | `CreditCardPaymentProfile?` | Payment method (optional for sandbox) |

#### Product (response model)

| Field (wire name) | Type | Notes |
|---|---|---|
| `Id (id)` | `int?` | Maxio-assigned product ID |
| `Handle (handle)` | `string?` | Product handle (e.g. `eshop-pro`, `basic-plan`) |
| `Name (name)` | `string?` | Display name |
| `Description (description)` | `string?` | Product description |
| `PriceInCents (price_in_cents)` | `long?` | Recurring price in cents |
| `Interval (interval)` | `int?` | Billing interval (typically 1 for monthly) |
| `IntervalUnit (interval_unit)` | `IntervalUnit?` | `Month` or `Day` |
| `ProductFamily (product_family)` | `ProductFamily?` | Parent product family object |
| `CreatedAt (created_at)` | `DateTimeOffset?` | Creation timestamp |
| `ArchivedAt (archived_at)` | `DateTimeOffset?` | Null if active, set if archived |

#### Customer (response model)

| Field (wire name) | Type | Notes |
|---|---|---|
| `Id (id)` | `int?` | Maxio-assigned customer ID |
| `FirstName (first_name)` | `string?` | Customer first name |
| `LastName (last_name)` | `string?` | Customer last name |
| `Email (email)` | `string?` | Customer email |
| `Reference (reference)` | `string?` | **Your app's reference** (e.g., app user ID) |
| `CreatedAt (created_at)` | `DateTimeOffset?` | Creation timestamp |
| `UpdatedAt (updated_at)` | `DateTimeOffset?` | Last update timestamp |

#### CreateSubscription (request model, nested under CreateSubscriptionRequest)

**Only fields used in this integration are listed; many optional fields exist.**

| Field (wire name) | Type | Required? | Notes |
|---|---|---|---|
| `ProductHandle (product_handle)` | `string?` | ~yes (or ProductId) | Handle of the plan to subscribe to (e.g., `eshop-pro`) |
| `ProductId (product_id)` | `int?` | ~yes (or ProductHandle) | ID of the plan (alternative to handle) |
| `CustomerId (customer_id)` | `int?` | ~yes (or CustomerAttributes) | Maxio customer ID (if customer already exists) |
| `CustomerAttributes (customer_attributes)` | `CustomerAttributes?` | ~yes (or CustomerId) | Inline customer creation object with `FirstName`, `LastName`, `Email`, `Reference` |
| `Reference (reference)` | `string?` | optional | Your app's reference for the subscription |
| `PaymentProfileId (payment_profile_id)` | `int?` | optional | Maxio payment profile ID (not required for sandbox) |
| `CouponCode (coupon_code)` | `string?` | optional | Coupon code to apply |
| `NextBillingAt (next_billing_at)` | `DateTimeOffset?` | optional | Override next billing date |
| `DeferSignup (defer_signup)` | `bool?` | optional, = false | If true, subscription created but not activated |

#### CreateCustomer (request model, nested under CreateCustomerRequest)

| Field (wire name) | Type | Required? | Notes |
|---|---|---|---|
| `FirstName (first_name)` | `string?` | per Notes | Customer first name |
| `LastName (last_name)` | `string?` | per Notes | Customer last name |
| `Email (email)` | `string?` | per Notes | Customer email |
| `Reference (reference)` | `string?` | optional | **Store your app's user ID here for lookup & idempotency** |

**Notes on required fields**: `CreateCustomer.FirstName`, `LastName`, `Email` are marked optional in the model definition, but the operation's Notes state "you may only create one customer for a given reference value" and the API accepts a reference as the unique identifier. **Carry `Reference` always; `FirstName`/`LastName`/`Email` are recommended but check the operation Notes in the map for the exact acceptance rule.** ← **LEFT OUT HERE because map row says "only validation restriction is … reference value must be unique."**

### Enums (from `MaxioAdvancedBilling.Models.Enums`)

#### SubscriptionState

| Member (wire value) | Use |
|---|---|
| `Active (active)` | Subscription is active and paid up-to-date |
| `Trialing (trialing)` | In trial period |
| `PastDue (past_due)` | Payment overdue |
| `Suspended (suspended)` | Suspended (dunning) |
| `Canceled (canceled)` | Canceled |
| `Expired (expired)` | Expired (past expiration date) |
| `Paused (paused)` | On hold |
| `OnHold (on_hold)` | On hold |
| `AwaitingSignup (awaiting_signup)` | Pending customer completion |
| `FailedToCreate (failed_to_create)` | Creation failed |
| `Pending (pending)` | Pending activation |
| `Unpaid (unpaid)` | Unpaid |
| `TrialEnded (trial_ended)` | Trial ended |
| `Assessing (assessing)` | Internal state (transient) |
| `SoftFailure (soft_failure)` | Transient failure |

#### CollectionMethod

| Member (wire value) | Use |
|---|---|
| `Automatic (automatic)` | Auto payment (card on file) |
| `Invoice (invoice)` | Invoice-based (Legacy Statements) |
| `Remittance (remittance)` | Remittance (Relationship Invoicing) |
| `Prepaid (prepaid)` | Prepaid account |

#### SubscriptionListInclude

| Member (wire value) | Use |
|---|---|
| `SelfServicePageToken (self_service_page_token)` | Include self-service portal token in response |

#### SubscriptionInclude

| Member (wire value) | Use |
|---|---|
| `Coupons (coupons)` | Include applied coupons list |
| `SelfServicePageToken (self_service_page_token)` | Include self-service portal token |

---

### Error Accessors by Operation

| Operation | Error Case | Type | Accessors | HTTP Status |
|---|---|---|---|---|
| `ReadProductByHandle` | Case B | `SdkException<RawError>` | `ex.Error.StatusCode` (HttpStatusCode), `ex.Error.ReadAsString()`, `ex.Error.ReadAsJson<T>()` | 404, 5xx |
| `ListProducts` | Case B | `SdkException<RawError>` | `ex.Error.StatusCode`, `ex.Error.ReadAsString()` | 401, 5xx |
| `CreateSubscription` | Case A | `SdkException<CreateSubscriptionError>` | `TryGetErrorListResponse1(out ErrorListResponse1)` [422] returns field-level errors; `TryGetRawError(out RawError)` fallback for others | 422, 5xx |
| `ListSubscriptions` | Case B | `SdkException<RawError>` | `ex.Error.StatusCode`, `ex.Error.ReadAsString()` | 401, 5xx |
| `ReadSubscription` | Case B | `SdkException<RawError>` | `ex.Error.StatusCode`, `ex.Error.ReadAsString()` | 404, 5xx |
| `CreateCustomer` | Case A | `SdkException<CreateCustomerError>` | `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422]; `TryGetRawError(out RawError)` fallback | 422, 5xx |
| `ReadCustomerByReference` | Case B | `SdkException<RawError>` | `ex.Error.StatusCode`, `ex.Error.ReadAsString()` | 404, 5xx |
| `ListCustomerSubscriptions` | Case B | `SdkException<RawError>` | `ex.Error.StatusCode`, `ex.Error.ReadAsString()` | 404, 5xx |

---

### Client Initialization & Configuration

**Namespace**: `MaxioAdvancedBilling` (root), `MaxioAdvancedBilling.Core.Authentication.Basic`, `MaxioAdvancedBilling.Servers`

```csharp
// HttpClient registration (DI)
services.AddHttpClient();

// Or use DI extension (preferred):
services.AddMaxioAdvancedBillingClient(o =>
{
    var config = serviceProvider.GetRequiredService<IConfiguration>();
    o.BasicAuth = new BasicAuthCredentials
    {
        Username = config["Maxio:ApiKey"],  // API key from environment
        Password = "x"                       // Literal "x"
    };
    o.Environment = ServerEnvironment.Us;  // or .Eu
    o.Server.Production.Us.Site = config["Maxio:Subdomain"];
    
    // Optional: override base URL for sandbox/dev
    if (!string.IsNullOrEmpty(config["Maxio:BaseUrl"]))
        o.Server.Production.Us.BaseUrl = config["Maxio:BaseUrl"];
});

// Manual construction:
var client = new MaxioAdvancedBillingClient(httpClient, new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" },
    Environment = ServerEnvironment.Us,
    Server = new ServerOptions
    {
        Production = new ProductionOptions
        {
            Us = new ServerNodeOptions { Site = "your-subdomain" }
        }
    }
});
```

---

## Trap Notes

⚠ **Step 1 (client initialization)** — The SDK client needs a long-lived, reused `HttpClient` from `IHttpClientFactory`, not a new one per request. The client wrapper may be transient, but the underlying HTTP handler must be shared. **MUST load `dotnet-client-initialization`** before wiring DI or constructing the client.

⚠ **Step 2 (credentials & auth)** — Maxio uses HTTP **Basic** authentication: `Username` = your API key (from `Maxio:ApiKey` config), `Password` = the literal string `"x"`. Set credentials **before** client construction or in the DI callback. Never hardcode keys; always load from `IConfiguration`. **MUST load `dotnet-authentication`** before storing or rotating credentials.

⚠ **Step 3 (calling operations)** — Each operation's signature lists parameters in order; nullable/no-default params **must be passed explicitly** (pass `null` to skip, do not omit). `ListProducts`, `ListSubscriptions`, `ReadSubscription` have many optional query/filter params — use **named arguments** to pass only what you need. The `ct` (cancellation token) parameter is named exactly `ct`, not `cancellationToken`. **MUST load `dotnet-calling-endpoints`** before the first call.

⚠ **Step 4 (models & deserialization)** — Request bodies wrap an inner model: `CreateSubscriptionRequest { Subscription: CreateSubscription }`, `CreateCustomerRequest { Customer: CreateCustomer }`. Response envelopes also wrap: `ProductResponse { Product }`, `SubscriptionResponse { Subscription }`, `CustomerResponse { Customer }` — access the inner object one level down. Enum values are `StringEnum<T>` (not C# enums); build via static members e.g. `SubscriptionState.Active` or `SubscriptionState.FromValue("active")`. **MUST load `dotnet-models`** before referencing response or request shapes.

⚠ **Step 5 (error handling — two JsonException routes)** — 
1. A drifted or malformed **2xx** body (missing `required` member) surfaces as `JsonException` from deserialization, **not** `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
2. A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.
**MUST load `dotnet-error-handling`** before writing the error boundary. Catch both `SdkException<CreateSubscriptionError>` (typed, Case A), `SdkException<RawError>` (raw, Case B), and `JsonException` separately; never assume status code survives in a `JsonException`.

⚠ **Step 5a (provider 422 errors on Create)** — `CreateSubscription` and `CreateCustomer` throw `SdkException<CreateSubscriptionError>` / `SdkException<CreateCustomerError>` on HTTP 422 validation failures. Call `ex.Error.TryGetErrorListResponse1(out var e422)` to extract field-level errors; if that returns false, fall back to `TryGetRawError(…)` for the raw payload. The list may contain messages like "customer reference must be unique" — parse and return to the caller, do not retry. **MUST load `dotnet-error-handling`**.

⚠ **Step 6 (idempotent customer creation)** — To avoid duplicate customers on re-click, always query by `Reference` first using `ReadCustomerByReference(reference)`. If 404 (`ex.Error.StatusCode == HttpStatusCode.NotFound`), create the customer with that reference. If it succeeds, reuse the existing customer ID for subscription creation. **Never create blind**; always check first. The operation Notes enforce "only one customer per reference value."

⚠ **Step 6a (pagination)** — `ListProducts`, `ListSubscriptions`, `ListCustomers` return paginated results. Pass `page` and `perPage` explicitly (defaults 1 and 20 or 50); loop or fetch all pages as needed. Responses are `IReadOnlyList<T>`, not a wrapper — interpret the list length vs. `perPage` to detect final page.

⚠ **Step 7 (no payment method required for sandbox)** — In sandbox, `PaymentProfileId` (card on file) is optional; subscriptions can be created without payment info. In production, the product's `RequireCreditCard` flag controls whether a card is mandatory. For sandbox plans `eshop-pro` and `basic-plan`, omit `PaymentProfileId` to match the "payment method not required" requirement.

⚠ **Step 7a (configuration & retry semantics)** — Retry options (`MaxRetries`, `Timeout`, `HttpMethodsToRetry`) are set on `MaxioAdvancedBillingClientOptions.Retry`. **`Timeout` bounds per-attempt time, not total call time.** `HttpMethodsToRetry` gates only HTTP **status** triggers (e.g., 503 on POST is not resent), but **transport failures** (`HttpRequestException`, timeouts) are retried on **every** verb including POST — a non-idempotent `CreateSubscription` can execute twice if the first attempt times out mid-send. **MUST load `dotnet-configuration-resilience`** to understand retry scope and set `MaxRetries = 0` (if forced) or design for idempotency.

⚠ **Step 8 (no secrets in output or logs)** — Never log or echo `Maxio:ApiKey`, payment details, or customer email beyond what the user explicitly requested. Store keys in .NET user-secrets (dev) or environment variables (prod), never in source or appsettings.json. **MUST load `dotnet-authentication`** for secrets management patterns.

---

## REQUIRED READING

The following companion skills carry binding details not in this sheet — load them **before implementation starts**. This sheet deliberately does not carry their defaults or semantics; each skill is the authority for its step.

| Skill | Step It Governs |
|---|---|
| `dotnet-client-initialization` | Client construction, HttpClient factory, DI registration, transient vs. long-lived instance |
| `dotnet-authentication` | Credential storage, auth scheme (HTTP Basic), loading from config, rotating keys |
| `dotnet-calling-endpoints` | Operation signatures, parameter binding (named args, nullable pass-through), async/await, cancellation token usage |
| `dotnet-models` | Request/response envelope shapes, `StringEnum<T>` construction & reading, union types, deserialization |
| `dotnet-error-handling` | Exception boundary design, `SdkException<T>` case A/B distinction, `JsonException` routes, status-code mapping, `TryGet…` accessors, provider error parsing |
| `dotnet-configuration-resilience` | Retry policy, timeout scope (per-attempt), HTTP method filtering, exponential backoff, base-URL override, logging hooks |
| `dotnet-testing` | HTTP client mocking, test doubles, assertion patterns |

**Both of these hazards must be caught and handled differently — they arrive from opposite directions and need opposite handling:**

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing any error boundary.

---

## Assumptions & Blockers

| Category | Item | Status |
|---|---|---|
| **Assumptions** | App user ID (from JWT claim or ClaimsIdentity) will be stored in Maxio `Reference` field for idempotent lookup and subscription association. | YOUR CALL — not in the map |
| **Assumptions** | Sandbox entities (product handles `eshop-pro`, `basic-plan`; component handle `api-call`; product family `eshop-subscribe`) exist and are stable by handle. Numeric IDs may change; handles are stable. | YOUR CALL — not in the map |
| **Assumptions** | Configuration keys `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` (optional) are supplied by environment/appsettings and loaded via `IConfiguration`. | YOUR CALL — not in the map |
| **Assumptions** | PublicApi controller will run on a separate host/port from the Web host (or at least with separate auth). JWT middleware will verify requests; SDK calls use Basic auth (separate from JWT). | YOUR CALL — not in the map |
| **Assumptions** | eShopOnWeb uses in-memory database; no persistent store ties to Maxio IDs. Caller must retain Maxio subscription ID and state in a local cache, session, or response object for the user session. | YOUR CALL — not in the map |
| **Assumptions** | "Payment method not required" for sandbox means `PaymentProfileId` is optional in `CreateSubscription` request. If a payment method is needed for production plans, the plan's `RequireCreditCard` flag will reject the call; implement accordingly. | YOUR CALL — not in the map |
| (none) | No active blockers. All operations are mapped, all enums are listed, all error types are identified. | — |

---

**End of plan.**
