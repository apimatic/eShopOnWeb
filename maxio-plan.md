# Maxio Advanced Billing Integration for eShopOnWeb — Contract & Implementation Plan

## Scope & Sequence

1. **SDK client initialization & DI registration** — configure HttpClient, auth, and endpoints
2. **List available subscription plans** — read product family and products from Maxio sandbox
3. **Enroll user in a subscription** — create customer (if needed) and subscription, enforcing idempotency per user
4. **Retrieve user's current subscriptions** — list subscriptions for an authenticated user
5. **Error boundary & resilience** — handle Maxio SDK exceptions, transport failures, and timeouts

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### 1. List Subscription Plans (from Maxio product family)

| Aspect | Detail |
|---|---|
| **Controller** | `client.ProductFamilies` → `ListProductsForProductFamily` |
| **HTTP** | `GET /product_families/{product_family_id}/products.json` |
| **Signature** | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — pass `null` for all optional params except `page` and `perPage` which have defaults |
| **Returns** | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>` — each element wraps a `Product` at `element.Product` |
| **Queries** | All params nullable except `productFamilyId`; pass `null` to skip optional ones; `page`=1, `perPage`=20 defaults |
| **Error** | `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>` — **Case A (typed)** — `TryGetString(out string)` for 404 (family not found), `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` fallback |
| **Pagination** | Manual via `page`+`perPage`; request is stateless, no cursor |
| **Notes** | Hardcode `productFamilyId = "eshop-subscribe"` (the product family handle from the sandbox); loop pagination manually if needed for >20 plans, though sandbox seed has only 2 plans |
| **Source** | `map/operations/ProductFamilies.md` |

**Request model:** none (path + query only)

**Response model:**  
- Wrap: `MaxioAdvancedBilling.Models.ProductResponse` (from `Models/` namespace)
  - `Product (product): MaxioAdvancedBilling.Models.Product !req` — contains the plan details

**Product fields the endpoint returns** (extract for the UI):
- `Id (id): int?` — Maxio product ID  
- `Name (name): string?` — display name ("Pro Plan", "Basic Plan")  
- `Handle (handle): string?` — API handle ("eshop-pro", "basic-plan")  
- `PriceInCents (price_in_cents): long?` — monthly price in cents (29900 = $299.00)  
- `Interval (interval): int?`, `IntervalUnit (interval_unit): MaxioAdvancedBilling.Models.Enums.IntervalUnit?` — billing period (e.g. 1 month)  
- `Description (description): string?` — plan description

---

### 2. Create/Enroll User in a Subscription

| Aspect | Detail |
|---|---|
| **Controller** | `client.Subscriptions` → `CreateSubscription` |
| **HTTP** | `POST /subscriptions.json` |
| **Signature** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** |
| **Returns** | `MaxioAdvancedBilling.Models.SubscriptionResponse` (wraps `Subscription` field) |
| **Error** | `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>` — **Case A (typed)** — `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` for 422 (validation), `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` fallback |
| **Pagination** | none |
| **Notes** | Specify customer via `CustomerId` (if existing in Maxio) or via `CustomerAttributes` (for new customer). Specify product via `ProductHandle` or `ProductId`. **Idempotency per user:** before calling, check if a subscription already exists for this user (via `ListCustomerSubscriptions` with the Maxio customer ID, or lookup by customer reference). If one exists and is active, skip creation and return the existing subscription. **Payment:** the sandbox allows test-mode subscriptions without real payment info; populate `PaymentProfileAttributes` with test card (fake number, any exp date) only if the product requires payment. |
| **Source** | `map/operations/Subscriptions.md` |

**Request model:**  
- Envelope: `MaxioAdvancedBilling.Models.CreateSubscriptionRequest`  
  - `Subscription (subscription): MaxioAdvancedBilling.Models.CreateSubscription !req`

**CreateSubscription fields** (minimal set for the plan; note: **most fields are optional**):
- `ProductHandle (product_handle): string?` — OR `ProductId`: the plan handle (e.g. "eshop-pro") **required-to-be-accepted per Notes** 
- `ProductId (product_id): int?` — OR `ProductHandle`: Maxio product ID  
- `CustomerId (customer_id): int?` — existing Maxio customer ID; **omit if creating a new customer below**  
- `CustomerAttributes (customer_attributes): MaxioAdvancedBilling.Models.CustomerAttributes?` — **required-to-be-accepted** if `CustomerId` not supplied (create new customer inline)  
  - `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?` — application's user ID for idempotency lookup  
- `PaymentProfileAttributes (payment_profile_attributes): MaxioAdvancedBilling.Models.PaymentProfileAttributes?` — test card for sandbox (required on most products)  
- `Reference (reference): string?` — optional but **recommended**: your app's user ID; use the same value as `CustomerAttributes.Reference` for idempotency  

**Response model:**  
- Envelope: `MaxioAdvancedBilling.Models.SubscriptionResponse`  
  - `Subscription (subscription): MaxioAdvancedBilling.Models.Subscription !req` — contains subscription details

**Subscription fields to extract** (from response):
- `Id (id): int?` — Maxio subscription ID  
- `CustomerId (customer_id): int?` — Maxio customer ID  
- `ProductId (product_id): int?` — product ID  
- `State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?` — status ("active", "canceled", "past_due", etc.)  
- `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` — renewal date  
- `CreatedAt (created_at): DateTimeOffset?` — subscription start

---

### 3. Get User's Current Subscriptions

| Aspect | Detail |
|---|---|
| **Controller** | `client.Customers` → `ListCustomerSubscriptions` |
| **HTTP** | `GET /customers/{customer_id}/subscriptions.json` |
| **Signature** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — `customerId` is path param (required) |
| **Returns** | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>` — array of subscriptions, each wraps a `Subscription` |
| **Error** | `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` — **Case B** — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` |
| **Pagination** | none (returns all subscriptions for customer) |
| **Notes** | Requires the Maxio `customer_id` (obtained from customer creation or lookup). Filter the list in-app for "active" subscriptions only if needed. |
| **Source** | `map/operations/Customers.md` |

**Request model:** none (path-only)

**Response model:**  
- Array element type: `MaxioAdvancedBilling.Models.SubscriptionResponse`  
  - `Subscription (subscription): MaxioAdvancedBilling.Models.Subscription !req`

---

### 4. Alternative: Lookup Customer by Reference (for idempotency)

| Aspect | Detail |
|---|---|
| **Controller** | `client.Customers` → `ReadCustomerByReference` |
| **HTTP** | `GET /customers/lookup.json?reference={ref}` |
| **Signature** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — `reference` is a query param |
| **Returns** | `MaxioAdvancedBilling.Models.CustomerResponse` (wraps `Customer`) |
| **Error** | `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` — **Case B** |
| **Notes** | Use to find the Maxio customer ID from your app's user ID (the `reference` field). Before creating a subscription, call this first to see if the user already has a Maxio customer. If found, use the returned `Customer.Id` for subscription lookup/creation. |
| **Source** | `map/operations/Customers.md` |

**Response model:**  
- Envelope: `MaxioAdvancedBilling.Models.CustomerResponse`  
  - `Customer (customer): MaxioAdvancedBilling.Models.Customer !req` — contains `Id` (Maxio ID) and `Reference` (your app ID)

---

## Enum Values (Reference)

**SubscriptionState** (from `map/models/enums.md`):  
Wire value → C# member name (construct via `SubscriptionState.FromValue("wire")` or use the static member):
- `"active"` → `SubscriptionState.Active`
- `"canceled"` → `SubscriptionState.Canceled`
- `"past_due"` → `SubscriptionState.PastDue`
- `"pending"` → `SubscriptionState.Pending`
- `"trial"` → `SubscriptionState.Trial`
- (others exist; full list in `map/models/enums.md`)

**IntervalUnit** (billing period):
- `"month"` → `IntervalUnit.Month`
- `"year"` → `IntervalUnit.Year`
- etc.

---

## Client Construction & Auth

**Namespace & using statements** (required for each):
```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Api;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
```

**Client construction** (from `sdk-map.md`):
```csharp
var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials 
    { 
        Username = "<MAXIO_API_KEY>",    // from config
        Password = "x"                    // literal "x"
    },
    Environment = ServerEnvironment.Us,  // or .Eu
    Server = new MaxioAdvancedBilling.Servers.ServerOptions
    {
        Production = new MaxioAdvancedBilling.Servers.ProductionOptions
        {
            Us = new MaxioAdvancedBilling.Servers.ProductionServerOptions
            {
                Site = "<MAXIO_SITE_SUBDOMAIN>"  // from config
            }
        }
    }
};
var client = new MaxioAdvancedBillingClient(httpClient, options);
```

**Configuration sources** (from environment variables → .NET user-secrets):
- `MAXIO_API_KEY` → secrets key `Maxio:ApiKey`
- `MAXIO_SITE_SUBDOMAIN` → secrets key `Maxio:SiteSubdomain`
- `MAXIO_ENVIRONMENT` → secrets key `Maxio:Environment` (value: "Us" or "Eu"; default "Us")
- `MAXIO_DEFAULT_PRODUCT_FAMILY` → secrets key `Maxio:DefaultProductFamily` (hardcode as "eshop-subscribe" or read from config for flexibility)

---

## Trap Notes — Load Before Implementation

⚠ **Step 1 (client initialization)** — The SDK's retry/timeout options do NOT bound a whole call and are NOT the timeout on the HttpClient you register; they are per-attempt retry backoff semantics managed by Polly. **MUST load `dotnet-configuration-resilience`** before wiring the client's retry options and understanding the behavior on failures (e.g. POST idempotency and transport-error retry).

⚠ **Step 2 (authentication)** — Credentials must be set BEFORE constructing the client or in the DI callback; never load the API key from hardcoded defaults or read it at request time. Rotation or refresh of the key requires a new client instance. **MUST load `dotnet-authentication`** before wiring credentials and before deciding where to source the key (config, vault, etc.).

⚠ **Step 3 (calling endpoints)** — Many optional parameters have no C# default and bind incorrectly in positional calls; use named arguments for clarity and to avoid the signature trap where adding a param shifts the positional binding. For example, `ListSubscriptions` has 14 optional params before `page`/`perPage`—pass them all as `null` or by name, never positionally. **MUST load `dotnet-calling-endpoints`** before the first call.

⚠ **Step 4 (models & response envelopes)** — Response types wrap their payload in exactly one field (e.g. `SubscriptionResponse.Subscription`, `ProductResponse.Product`); read down one level to access the data. Enums are `StringEnum<T>`, not C# enums—construct via `.FromValue("wire")` or static members (e.g. `SubscriptionState.Active`). Unions (used for some component/offering types) are built with factory methods and read via `TryGet…(out …)`. **MUST load `dotnet-models`** before referencing any model field or enum.

⚠ **Step 5a (error handling — typed errors)** — `CreateSubscription`, `ListProductsForProductFamily`, `ReadProductFamily`, and several other write/list operations throw **Case A** (`SdkException<{Operation}Error>`) with typed `TryGet…` accessors for specific HTTP statuses. Call the accessor for the status you expect (e.g. `TryGetErrorListResponse1(out …)` for 422), then fall back to `TryGetRawError(out …)` for other statuses. Ignore other accessors; the map row lists only the ones actually present.

⚠ **Step 5b (error handling — raw errors)** — `ListCustomerSubscriptions`, `ListProductsForProductFamily` (404 case), and read operations throw **Case B** (`SdkException<RawError>`), which has NO typed accessors—use `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` directly. The error object carries no `TryGet…` methods; the SDK has no payload-specific class for it.

⚠ **JsonException tunnel (deserialization boundaries)** — Two directions:  
  - A drifted or malformed **2xx** body (a missing `required` member in the response) surfaces as a `JsonException` from deserialization, **NOT** as an `SdkException`—an SDK-exception-only catch ladder lets it escape the integration boundary, and the caller sees a 2xx with a broken payload as an outage.
  - A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed—a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.
  
  **Both of these must be handled differently in the boundary.** **MUST load `dotnet-error-handling`** before writing the catch ladder; read that skill for the Case A/B mechanics and the `JsonException` tunnel (the skill carries the parts a one-line note cannot).

⚠ **Step 6 (resilience & config)** — `HttpMethodsToRetry` gates only the **status** trigger (5xx codes), so a `503` on a `POST` is not resent—but a **transport failure** (`HttpRequestException`, network timeout, DNS) is retried on **every** verb, `POST` included, so a non-idempotent write can execute more than once and there is no setting to disable this (`MaxRetries = 0` is rejected at construction). `Timeout` is **per-attempt**, not total, so three retries with a 10s timeout = up to 30s elapsed. **MUST load `dotnet-configuration-resilience`** before tuning retry behavior and before deciding how to guarantee subscription idempotency (hint: use customer reference + state check, not retry-less API calls).

⚠ **Step 7 (testing)** — The `HttpClient` passed to the SDK constructor is the test seam; inject a mocked/stubbed client (via test-doubles, HttpClientFactory, or a mock handler) to stub Maxio calls in unit tests. Do **not** call the live Maxio endpoint in unit tests; always inject a fake transport. **MUST load `dotnet-testing`** before writing SDK integration tests.

---

## REQUIRED READING

Load these companion skills **before implementation starts**; the sheet deliberately does not carry their contents:

| Skill | Governs | Notes |
|---|---|---|
| `dotnet-client-initialization` | Step 1 — Client & DI setup | HttpClient lifetime, transient vs long-lived, DI registration via `AddMaxioAdvancedBillingClient` |
| `dotnet-authentication` | Step 2 — Auth wiring | Credentials source, rotation, Basic auth username/password semantics |
| `dotnet-calling-endpoints` | Step 3 — First operation call | Named arguments, optional-param binding, async/await, `ct` cancellation token |
| `dotnet-models` | Step 4 — Request/response shapes | Field names (wire vs C#), enums, unions, response envelopes, `required` fields |
| `dotnet-error-handling` | Step 5a & 5b & JsonException tunnel | Typed (Case A) vs raw (Case B) errors, `TryGet…` accessors, `SdkException<T>` + `JsonException` handling |
| `dotnet-configuration-resilience` | Step 6 — Retry/timeout semantics | Polly retry gates, per-attempt vs total timeout, `HttpMethodsToRetry` vs transport-error retry, idempotency hazard |
| `dotnet-testing` | Step 7 — Stub the SDK | Test double patterns, HttpClient mocking, avoiding live calls in tests |

---

## Assumptions & Blockers

**Assumptions:**
1. eShopOnWeb's JWT authentication is already in place and the endpoint handlers can extract the current user's identity (user ID, email) from the JWT.
2. The Maxio sandbox credentials (API key, site subdomain) are available and valid; they will be stored in .NET user-secrets during local development and in an environment variable / configuration vault in production.
3. The sandbox is seeded with product family "eshop-subscribe", products "eshop-pro" (basic-plan) and "basic-plan" (both with valid price/billing period), and a metered component "api-call".
4. Subscriptions created via `CreateSubscription` may require payment profile information (credit card); for sandbox testing, test card numbers (e.g. 4111-1111-1111-1111) are accepted without processing.
5. The application owns the decision about when to call `ListCustomerSubscriptions` vs retry subscription creation for idempotency—the plan does not govern app-level retry logic or state management across sessions.
6. All endpoints in src/PublicApi are secured via JWT; no separate OAuth/API key mechanism is needed between the app and eShopOnWeb's own API.

**Blockers:**
- None identified. The map covers all operations needed; the SDK is production-ready.

