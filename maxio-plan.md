# Maxio Advanced Billing Integration for eShopOnWeb Subscriptions

## Scope & sequence

1. **Client registration & configuration** — Wire the SDK client into DI with auth from environment variables
2. **Fetch available plans** — Call `ListProducts` to populate the `GET /api/subscription-plans` endpoint
3. **Idempotent customer lookup** — Call `ReadCustomerByReference` to check if Maxio customer exists for the logged-in user
4. **Create customer (if needed)** — Call `CreateCustomer` for first-time users
5. **Create subscription** — Call `CreateSubscription` to enroll the user in the selected plan
6. **List subscriptions** — Call `ListCustomerSubscriptions` to populate the `GET /api/my-subscriptions` endpoint

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal
C# identifier. The cancellation-token parameter really is named `ct`: in named
arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take
each one from that type's own map row, never from where a neighbouring type sits. A members
table names the namespace outright; otherwise the row's source path implies it
(`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root
namespace). Enums, unions, auth, server and client-config types are spread across different
child namespaces, and two types configured side by side in the same options object routinely
live in different ones. Dropping a type to the root or to `.Models` makes the implementer
guess the wrong `using`, and the build breaks.

### List Products (fetch available subscription plans)

| Cell | Content |
|---|---|
| **Controller/Property** | `client.Products` |
| **Signature** | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| **Parameters** | 8 optional query params (all nullable, must pass explicitly as `null` to skip): `dateField`, `filter`, `endDate`, `endDatetime`, `startDate`, `startDatetime`, `includeArchived`, `include`; paging: `page` (default 1), `perPage` (default 20) |
| **Request body** | None |
| **Response envelope** | `IReadOnlyList<ProductResponse>` — direct array (each item is `ProductResponse` with `Product` field) |
| **Response fields to read** | From each `ProductResponse`: `Product.Id` (int?), `Product.Handle` (string?), `Product.Name` (string?), `Product.PriceInCents` (long? — price in cents, divide by 100 for display), `Product.Interval` (int?), `Product.IntervalUnit` (IntervalUnit? enum) |
| **Error case** | `SdkException<RawError>` — **Case B** · no typed accessors · `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` |
| **Pagination** | Manual `page`/`perPage`; no `TotalPages` field in response — iterate until list is empty |
| **Source** | `operations/Products.md` |

#### Notes on ListProducts from map
- Filter by active/archived products via `includeArchived` (default false, excludes archived)
- No filter by product family available in this operation; the `product_family_id` will be in the `Product.ProductFamily` nested object if included
- Query parameters are optional; pass all as `null` to get paginated default results (page 1, 20 items)

### Read Customer by Reference (idempotent lookup)

| Cell | Content |
|---|---|
| **Controller/Property** | `client.Customers` |
| **Signature** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` |
| **Parameters** | `reference` (required, string — the eShopOnWeb user ID or email) |
| **Request body** | None |
| **Response envelope** | `CustomerResponse` with `Customer` field required |
| **Response fields to read** | `Customer.Id` (int?), `Customer.Email` (string?), `Customer.FirstName` (string?), `Customer.LastName` (string?), `Customer.Reference` (string?) |
| **Error case** | `SdkException<RawError>` — **Case B** · no typed accessors · `StatusCode` 404 if not found, 200 if found |
| **Pagination** | None |
| **Source** | `operations/Customers.md` |

#### Notes on ReadCustomerByReference from map
- Returns 404 if customer does not exist (map: "It will return a single match")
- Use `Customer.Reference` to match against eShopOnWeb user identity (idempotent on double-click)
- Always query by `Reference` before creating — this is the idempotency key

### Create Customer

| Cell | Content |
|---|---|
| **Controller/Property** | `client.Customers` |
| **Signature** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` |
| **Parameters** | `body` (nullable but must pass explicitly) — wraps `CreateCustomerRequest` |
| **Request body** | `CreateCustomerRequest` with required field: `Customer (customer): CreateCustomer !req` (union — wait, actually it's a `CreateCustomer` type, not a union) |
| **Request fields to set** | Inside `body.Customer` (`MaxioAdvancedBilling.Models.CreateCustomer` — note: different type from response `Customer`): `Reference` (string? — required for idempotency, use eShopOnWeb user ID), `FirstName` (string?), `LastName` (string?), `Email` (string?) |
| **Response envelope** | `CustomerResponse` with `Customer` field required |
| **Response fields to read** | `Customer.Id` (int?), `Customer.Reference` (string?), `Customer.Email` (string?) |
| **Error case** | `SdkException<CreateCustomerError>` — **Case A** · accessor `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] + fallback `TryGetRawError(out RawError)` |
| **Pagination** | None |
| **Source** | `operations/Customers.md` |

#### Notes on CreateCustomer from map
- **Required field**: `Reference` must be unique across the site (eShopOnWeb user ID recommended)
- `FirstName` and `LastName` are optional but should be provided if available
- If customer with same `Reference` already exists, call will fail with 422 — always check `ReadCustomerByReference` first
- Error payload on 422: inspect `CustomerErrorResponse1.Errors` for field-level messages

### Create Subscription

| Cell | Content |
|---|---|
| **Controller/Property** | `client.Subscriptions` |
| **Signature** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` |
| **Parameters** | `body` (nullable but must pass explicitly) — wraps `CreateSubscriptionRequest` |
| **Request body** | `CreateSubscriptionRequest` with required field: `Subscription (subscription): CreateSubscription !req` |
| **Request fields to set** (inside `body.Subscription`, type `MaxioAdvancedBilling.Models.CreateSubscription`) | **Must set one of:** `ProductHandle` (string? — e.g., `"eshop-pro"`), `ProductId` (int?) · **Idempotency:** `Reference` (string? — derived from eShopOnWeb user + plan, or user ID alone) · **Customer link:** `CustomerId` (int? — from earlier `CreateCustomer` response), OR `CustomerReference` (string? — eShopOnWeb user ID, alternative to CustomerId) · **Optional but recommended for user clarity:** none (handle required fields only) · **Payment:** `PaymentProfileId` (int? — optional; map notes "payment information may be required depending on product options"), leave null for "payment-method-not-required" products |
| **Response envelope** | `SubscriptionResponse` with `Subscription` field (nullable!) |
| **Response fields to read** | `Subscription.Id` (int?), `Subscription.State` (SubscriptionState? enum), `Subscription.ProductId` (int?), `Subscription.NextAssessmentAt` (DateTimeOffset? — next billing date), `Subscription.Product` (Product? — nested, contains plan details) |
| **Error case** | `SdkException<CreateSubscriptionError>` — **Case A** · accessor `TryGetErrorListResponse1(out ErrorListResponse1)` [422] + fallback `TryGetRawError(out RawError)` |
| **Pagination** | None |
| **Source** | `operations/Subscriptions.md` |

#### Notes on CreateSubscription from map
- **Idempotent subscription check:** There is **no built-in idempotency by signature**. Maxio will create a new subscription each time this call succeeds. Your application must track subscriptions per user per plan (via `Reference` or `CustomerId` + product); do not rely on the API to prevent duplicates.
- **Payment requirement:** Map says "Payment information may be required depending on product options." For sandbox products configured as "payment-method-not-required" (eshop-pro, basic-plan as stated in scope), omit `PaymentProfileId`. If required, error [422] will indicate missing payment.
- **Customer reference:** Use `CustomerReference` if you only have the eShopOnWeb user ID and want to avoid an extra lookup; the API will resolve it to `CustomerId` internally.
- **Response `Subscription` field is nullable:** Always check `if (response.Subscription != null)` before accessing fields.
- Error payload on 422: `ErrorListResponse1.Errors` is `IReadOnlyList<string>` (field-level error messages as strings, not a map).

### List Customer Subscriptions

| Cell | Content |
|---|---|
| **Controller/Property** | `client.Customers` |
| **Signature** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` |
| **Parameters** | `customerId` (required, int — from earlier `CreateCustomer` or lookup response) |
| **Request body** | None |
| **Response envelope** | `IReadOnlyList<SubscriptionResponse>` — direct array (each item has `Subscription` field, nullable) |
| **Response fields to read** | From each item, check `if (item.Subscription != null)`: `Subscription.Id`, `Subscription.State` (SubscriptionState? enum), `Subscription.ProductId`, `Subscription.NextAssessmentAt`, `Subscription.CurrentPeriodEndsAt` (DateTimeOffset? — current billing cycle end) |
| **Error case** | `SdkException<RawError>` — **Case B** · no typed accessors · `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` |
| **Pagination** | None — returns all subscriptions for the customer in a single call |
| **Source** | `operations/Customers.md` |

#### Notes on ListCustomerSubscriptions from map
- No pagination parameters; entire subscription list is returned
- Response is an array of `SubscriptionResponse` objects; filter by state (e.g., `State == SubscriptionState.Active`) in your code if needed
- Each `Subscription` object is nullable; skip any null entries

---

## Enum Values (from map/models/enums.md)

### `IntervalUnit` (from `Product.IntervalUnit`)

| C# Member | Wire Value |
|---|---|
| `Month` | `"month"` |
| `Day` | `"day"` |
| `Week` | `"week"` |
| `Year` | `"year"` |

**Source:** `map/models/enums.md`

### `SubscriptionState` (from `Subscription.State`)

| C# Member | Wire Value |
|---|---|
| `Trialing` | `"trialing"` |
| `Assessing` | `"assessing"` |
| `Active` | `"active"` |
| `SuspendedForNonpayment` | `"suspended_for_non_payment"` |
| `PastDue` | `"past_due"` |
| `Canceled` | `"canceled"` |
| `Expired` | `"expired"` |
| `AwaitingSignup` | `"awaiting_signup"` |
| `Paused` | `"paused"` |
| `Dunning` | `"dunning"` |
| `OnHold` | `"on_hold"` |

**Source:** `map/models/enums.md`

---

## Client Construction & Configuration

### Auth Setup

**Type:** `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials`

```csharp
var credentials = new BasicAuthCredentials
{
    Username = apiKey,    // from env var MAXIO_API_KEY
    Password = "x"        // literal string "x" (password for HTTP Basic auth)
};
```

**Source:** `sdk-map.md` (auth section)

### Client Options & Wiring

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;

var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = credentials,
    Environment = ServerEnvironment.Us,  // or .Eu if applicable
    Server = new MaxioAdvancedBilling.Servers.ServerOptions
    {
        Production = new MaxioAdvancedBilling.Servers.ProductionOptions
        {
            Us = new MaxioAdvancedBilling.Servers.ProductionServer
            {
                Site = subdomain  // e.g., "cp-exp-1" from env var MAXIO_SITE_SUBDOMAIN
            }
        }
    }
};

var client = new MaxioAdvancedBillingClient(httpClient, options);
```

**HttpClient requirement:** Must be injected or retrieved from `IHttpClientFactory` (long-lived, reused across requests). **MUST load `dotnet-client-initialization` before wiring.**

**Environment variables:**
- `MAXIO_API_KEY` → `BasicAuth.Username`
- `MAXIO_SITE_SUBDOMAIN` → `Server.Production.Us.Site`
- `MAXIO_ENVIRONMENT` (optional; default "Us") → `Environment` (parse to `ServerEnvironment.Us` or `.Eu`)
- `MAXIO_DEFAULT_PRODUCT_FAMILY` (unused by API calls directly; for filtering/business logic)
- `MAXIO_BASE_URL` (optional; override `Server.Production.Us.BaseUrl` if provided)

**DI alternative:** Use `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = …; })` if not constructing manually.

**Source:** `sdk-map.md` (Getting a client section)

---

## Trap Notes

⚠ **Step 1 (client registration)** — The SDK's `Retry` options do NOT bound a whole subscription-creation flow; they gate only per-request retries on specific status codes. A request timeout, retry floor, and exponential backoff are configurable, but `Timeout` bounds **per-attempt**, not total call time. **MUST load `dotnet-configuration-resilience`** before tuning retries or timeouts.

⚠ **Step 3 (idempotent customer lookup) & Step 4 (create customer)** — `ReadCustomerByReference` returns 404 if not found; you must catch `SdkException<RawError>` and check `.Error.StatusCode == HttpStatusCode.NotFound` to detect this state (no typed error accessor). Always call lookup before create to prevent duplicate-customer errors. **MUST load `dotnet-error-handling`** for the error boundary.

⚠ **Step 5 (create subscription)** — The response envelope wraps the payload in `SubscriptionResponse.Subscription`, which is **nullable**. Always check `if (response.Subscription != null)` before reading fields. Do not assume the 200 response always carries a non-null subscription object. **MUST load `dotnet-calling-endpoints`** to understand when to use named arguments (nullable optional params in ListProducts/ListCustomerSubscriptions require explicit `null` if omitted).

⚠ **Step 5 (create subscription)** — Maxio will **not** prevent duplicate subscriptions from double-clicked requests. Your code must track active subscriptions per user per plan (via subscription list query or local DB) and reject redundant sign-ups at the application boundary. The SDK has no built-in result type (all operations throw on error); catch and log the 422 if a user re-clicks "subscribe."

⚠ **Error boundary** — Two `JsonException` sources require opposite handling:
- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an `SdkException`-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, replacing the `SdkException` and destroying the HTTP status — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage. **MUST load `dotnet-error-handling`** before writing the catch boundary.

---

## REQUIRED READING

Load the following skills before implementation starts. These skills govern the steps listed and carry defaults, examples, and hazards a one-line contract fact cannot cover:

- **`dotnet-client-initialization`** — Step 1 (client registration & DI; HttpClient lifetime and reuse; constructor vs DI registration)
- **`dotnet-authentication`** — Step 1 (auth credentials setup; rotating/refreshing; per-environment config; BasicAuthCredentials wiring)
- **`dotnet-calling-endpoints`** — Steps 2–6 (operation signatures; named arguments for optional params; request/response envelope shapes; async/await and cancellation)
- **`dotnet-models`** — Steps 3–5 (record immutability; union construction via factories and `TryGet…`; enum wire values and member names; optional/required fields with defaults)
- **`dotnet-error-handling`** — Steps 3–6 (Case A vs B error types; `TryGet…` accessors; catch ladder structure; `JsonException` sources and handling; RawError payload access)
- **`dotnet-configuration-resilience`** — Step 1 (retry options semantics; timeout per-attempt vs total; exponential backoff; HttpMethodsToRetry gates status, not transport failures; POST is not retried on transport-failure unless explicitly configured)
- **`dotnet-testing`** — Tests (HttpClient test seam; matching test framework and assertions to project style)

**Mandatory error-handling rows (include in your first error boundary, not a later revision):**

- **Row 1:** A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an `SdkException`-only catch ladder lets it escape the integration boundary. Map every `JsonException` to a deterministic 5xx response, or re-throw for high-level handling.
- **Row 2:** A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, **replacing** the `SdkException` and destroying the HTTP status — so a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

---

## Assumptions & Blockers

**Assumptions:**

1. eShopOnWeb user identity (email or user ID) is stable and unique per user; this will be used as the `Reference` field for Maxio customer records and subscription idempotency keys.
2. The PublicApi project has a JWT-authenticated context from which the logged-in user ID can be resolved (available in a controller or middleware).
3. Environment variables (`MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`) are supplied at deployment time; the .NET configuration system (e.g., `IConfiguration`) will hydrate `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` (optional).
4. Sandbox products `eshop-pro` and `basic-plan` exist on the Maxio site `cp-exp-1` and are configured with `payment-method-not-required = true`.
5. The integration runs on the same version of .NET as the main eShopOnWeb codebase (in-memory DB, no external LocalDB dependency stated).

**Blockers:**

None identified. All operations and models are present in the SDK map; no capabilities are missing. The plan is ready for implementation.
