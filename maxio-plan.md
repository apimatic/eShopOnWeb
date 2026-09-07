# eShopOnWeb Recurring Subscription Billing — Maxio Integration Plan

## Scope & Sequence

The integration adds three HTTP endpoints and supporting database/API operations to enable recurring subscription management:

1. **List available subscription plans** (`GET /api/subscription-plans`)  
   Operation: `Products.ReadProductByHandle()` × 2 (fetch "eshop-pro" and "basic-plan" from the sandbox `eshop-subscribe` product family)

2. **Create or verify Maxio customer** (idempotent per eShopOnWeb user)  
   Operations: `Customers.ReadCustomerByReference()` (lookup by user ID), then `Customers.CreateCustomer()` (if missing)

3. **Subscribe a user to a plan** (`POST /api/subscriptions`)  
   Operation: `Subscriptions.CreateSubscription()` with customer and product identifiers

4. **Retrieve user's current subscriptions** (`GET /api/my-subscriptions`)  
   Operation: `Subscriptions.ListSubscriptions()` or `Subscriptions.ReadSubscription()` + database lookup by user ID

5. **Persist subscription state**  
   Database table: maps eShopOnWeb user ID ↔ Maxio subscription ID and records current plan/state

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operation 1: List Products (Plans)

| Aspect | Details |
|--------|---------|
| **Controller** | `client.Products` |
| **Method** | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` |
| **Parameters** | `apiHandle` = "eshop-pro" or "basic-plan" (wire: `api_handle` in query); `ct` = cancellation token (optional, default) |
| **Returns** | `MaxioAdvancedBilling.Models.ProductResponse` |
| **Response envelope** | `ProductResponse { Product: Product !req }` — read the inner `Product` field; its fields include `Id`, `Name`, `Handle`, `Description`, `PriceInCents`, `Interval`, `IntervalUnit` |
| **Error case** | **Case B** — `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` |
| **Error accessors** | `.Error.StatusCode` (HttpStatusCode), `.Error.ReadAsString()`, `.Error.ReadAsJson<T>()` |
| **Pagination** | None — single product by handle |
| **Notes** (from map) | Retrieves a Product object by its `api_handle` |
| **Source** | `operations/Products.md` |

#### Execution notes

- Call **twice**: once with `apiHandle: "eshop-pro"` (Pro plan, $299/mo), once with `apiHandle: "basic-plan"` (Basic plan, $29/mo)
- Extract from response: `Product.Id`, `Product.Name`, `Product.PriceInCents` (returned in cents; divide by 100 for display), `Product.Interval` (always 1 for these plans), `Product.IntervalUnit` (always `Month`)
- **Wire name mapping**: C# `apiHandle` → wire `api_handle`
- Both products belong to the product family `eshop-subscribe` (configured via environment variable `MAXIO_DEFAULT_PRODUCT_FAMILY` = `eshop-subscribe`)

---

### Operation 2: Create/Verify Customer (Idempotent)

#### Step 2a: Lookup existing customer by reference

| Aspect | Details |
|--------|---------|
| **Controller** | `client.Customers` |
| **Method** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` |
| **Parameters** | `reference` = eShopOnWeb user ID (wire: `reference` in query); `ct` = cancellation token (optional, default) |
| **Returns** | `MaxioAdvancedBilling.Models.CustomerResponse` |
| **Response envelope** | `CustomerResponse { Customer: Customer !req }` — read the inner `Customer` field; key fields: `Id` (Maxio ID), `Email`, `FirstName`, `LastName`, `Reference` (echoes the input) |
| **Error case** | **Case B** — `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` (returns 404 if not found; treat as "customer does not exist") |
| **Error accessors** | `.Error.StatusCode` |
| **Pagination** | None |
| **Notes** (from map) | Returns a customer by their unique reference ID. It will return a single match. |
| **Source** | `operations/Customers.md` |

#### Step 2b: Create customer if not found

| Aspect | Details |
|--------|---------|
| **Controller** | `client.Customers` |
| **Method** | `CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, CancellationToken ct = default)` |
| **Parameters** | `body` — request envelope (must pass explicitly); `ct` = cancellation token |
| **Request model** | `CreateCustomerRequest { Customer: CreateCustomer !req }` |
| **Customer fields** | `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?`, plus optional: `Address`, `City`, `State`, `Zip`, `Country`, `Phone`, `Organization`, `Locale`, `VatNumber`, `TaxExempt` |
| **Returns** | `MaxioAdvancedBilling.Models.CustomerResponse` |
| **Response envelope** | `CustomerResponse { Customer: Customer !req }` — key response: `Customer.Id` (store in database for future reference) |
| **Error case** | **Case A** — `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>` |
| **Error accessors** | `.Error.TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` (422), `.Error.TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` (fallback) |
| **Pagination** | None |
| **Notes** (from map) | Creates a new customer; optional `reference` value must be unique (intended to be the eShopOnWeb user ID). The only validation restriction is that you may only create one customer for a given reference value. |
| **Source** | `operations/Customers.md` |

#### Implementation contract

- **Step 1:** Call `ReadCustomerByReference(reference: "<user-id>")` where `<user-id>` is the logged-in user's eShopOnWeb ID (string/int, treated as string in the API)
- **Step 2:** If the call succeeds (200), extract `Customer.Id` (Maxio customer ID) and store in local database
- **Step 3:** If the call fails with HTTP 404 (`RawError.StatusCode == HttpStatusCode.NotFound`), create the customer:
  - Build `CreateCustomerRequest` with nested `Customer` object
  - Required fields: `FirstName`, `LastName`, `Email` (from eShopOnWeb user data)
  - Optional: `Reference = "<user-id>"` (idempotency key; using this ensures future lookups succeed)
  - Send to `CreateCustomer(body)`
  - On success, extract `Customer.Id` and store in database
  - On error (e.g., 422 from `TryGetCustomerErrorResponse1`), surface the validation error to the user (e.g., "Email already in use")

---

### Operation 3: Create Subscription

| Aspect | Details |
|--------|---------|
| **Controller** | `client.Subscriptions` |
| **Method** | `CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, CancellationToken ct = default)` |
| **Parameters** | `body` — request envelope (must pass explicitly); `ct` = cancellation token |
| **Request model** | `CreateSubscriptionRequest { Subscription: CreateSubscription !req }` |
| **Subscription fields** | Core required/optional for minimal signup: `ProductId (product_id): int?`, `CustomerId (customer_id): int?`, plus optional: `ProductHandle (product_handle): string?`, `CustomerReference (customer_reference): string?`, `Reference (reference): string?` (subscription reference for your system). Other fields: `CouponCode`, `PaymentCollectionMethod`, `NextBillingAt`, `Components` (for metered usage) |
| **Returns** | `MaxioAdvancedBilling.Models.SubscriptionResponse` |
| **Response envelope** | `SubscriptionResponse { Subscription: Subscription? }` — key fields: `Subscription.Id` (Maxio subscription ID), `Subscription.State` (see enum SubscriptionState below), `Subscription.CurrentPeriodEndsAt`, `Subscription.BalanceInCents` |
| **Error case** | **Case A** — `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>` |
| **Error accessors** | `.Error.TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` (422), `.Error.TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` (fallback) |
| **Pagination** | None |
| **Notes** (from map) | Creates a Subscription for a customer and product. Specify the product with `product_id` or `product_handle`. Identify an existing customer with `customer_id` or `customer_reference`. Payment information may be required to create a subscription, depending on the options for the Product being subscribed. **For eShopOnWeb sandbox:** no payment method required (test mode). |
| **Source** | `operations/Subscriptions.md` |

#### Implementation contract

- Pass `ProductId` (from step 1 lookup, e.g., product ID from `ReadProductByHandle`) and `CustomerId` (from step 2, Maxio customer ID)
- Optional: set `Reference = "<subscription-reference>"` (e.g., a business-side order ID or a user+plan combo for your system's lookup)
- On success, extract `Subscription.Id` and store in local database, mapped to the user ID
- On error (e.g., 422), check `TryGetErrorListResponse1` for validation messages (e.g., "Customer already has an active subscription for this product") and surface to the user
- **Sandbox context:** The API accepts subscriptions with no payment profile. In production (live mode), payment is required

---

### Operation 4: List Subscriptions (User's Subscriptions)

| Aspect | Details |
|--------|---------|
| **Controller** | `client.Subscriptions` |
| **Method** | `ListSubscriptions(MaxioAdvancedBilling.Models.Enums.SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, MaxioAdvancedBilling.Models.Enums.SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, MaxioAdvancedBilling.Models.Enums.SortingDirection? direction, MaxioAdvancedBilling.Models.Enums.SubscriptionSort? sort, IReadOnlyList<MaxioAdvancedBilling.Models.Enums.SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| **Parameters** | All filter params nullable (pass `null` to skip); pagination defaults: `page` = 1, `perPage` = 20. **Must pass explicitly:** `state`, `product`, `dateField`, etc. (all nullable, no C# defaults). `ct` = cancellation token. |
| **Query params (wire ← C#)** | `page` ← `page`, `per_page` ← `perPage`, `state` ← `state`, `product` ← `product`, `date_field` ← `dateField`, etc. |
| **Returns** | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>` |
| **Response envelope** | Each item is `SubscriptionResponse { Subscription: Subscription? }` — extract `Subscription.Id`, `Subscription.State`, `Subscription.ProductId`, `Subscription.Customer.Id`, etc. |
| **Error case** | **Case B** — `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` |
| **Error accessors** | `.Error.StatusCode`, `.Error.ReadAsString()` |
| **Pagination** | Manual `page` + `perPage`. Default perPage is 20; increment `page` to fetch next batch. |
| **Notes** (from map) | Returns an array of subscriptions from a Site. Use query strings to filter (e.g., by state, product, date). |
| **Source** | `operations/Subscriptions.md` |

#### Implementation contract

**Option A (recommended for single-user endpoint):** Query the local database for the logged-in user's subscription IDs (populated in step 3), then call `ReadSubscription` for each to fetch current state.

**Option B (broader query):** Call `ListSubscriptions` with filters and then match to the logged-in user via the subscription's `Customer.Id` or a business-level reference you store in the database. This requires either:
- Storing the subscription reference (`Subscription.Reference`) and querying by it, or
- Keeping a database mapping of user ID ↔ subscription IDs

**Enum values for state filter:**  
Use `MaxioAdvancedBilling.Models.Enums.SubscriptionStateFilter` (wire values listed below; construct via static member or `FromValue`):
- `Active` (wire: `active`) — normal, active, not in trial, paid and up to date
- `Canceled` (wire: `canceled`)
- `Expired` (wire: `expired`)
- `OnHold` (wire: `on_hold`)
- `PastDue` (wire: `past_due`)
- `Suspended` (wire: `suspended`)
- `Trialing` (wire: `trialing`) — in trial period
- `Unpaid` (wire: `unpaid`)
- And others (see enums.md for full list)

To filter for only active subscriptions, pass `state: MaxioAdvancedBilling.Models.Enums.SubscriptionStateFilter.Active`.

---

## Enum Values Needed

### SubscriptionState (for response interpretation)

Namespace: `MaxioAdvancedBilling.Models.Enums`

| C# Member | Wire Value | Meaning |
|-----------|------------|---------|
| `Active` | `active` | Normal, active subscription |
| `Trialing` | `trialing` | In trial period |
| `PastDue` | `past_due` | Payment past due |
| `Canceled` | `canceled` | Subscription canceled |
| `Expired` | `expired` | Subscription expired |
| `Suspended` | `suspended` | Suspended (e.g., dunning) |
| `Paused` | `paused` | On hold |
| `Unpaid` | `unpaid` | Unpaid invoice |

**Construction:** Use static members (`SubscriptionState.Active`) or `SubscriptionState.FromValue("active")`.

### SubscriptionStateFilter (for list filtering)

Namespace: `MaxioAdvancedBilling.Models.Enums`

Used with `ListSubscriptions(state: ...)`. Similar to SubscriptionState but with additional filters (e.g., `ExpiredCards`, `PendingCancellation`). Use static members or `FromValue`.

### IntervalUnit (for product display)

Namespace: `MaxioAdvancedBilling.Models.Enums`

| C# Member | Wire Value |
|-----------|------------|
| `Day` | `day` |
| `Month` | `month` |

Both plans in eShopOnWeb sandbox use `Month` (monthly billing).

### CollectionMethod (for payment collection mode, if needed)

Namespace: `MaxioAdvancedBilling.Models.Enums`

| C# Member | Wire Value |
|-----------|------------|
| `Automatic` | `automatic` |
| `Remittance` | `remittance` |
| `Prepaid` | `prepaid` |
| `Invoice` | `invoice` |

For eShopOnWeb, use `Automatic` (payment collected automatically at renewal) or accept the site default.

---

## Client Construction & Auth

### Client Registration (DI or Direct)

```csharp
// DI setup (Startup.cs / Program.cs)
services.AddHttpClient<IMaxioClient, MaxioAdvancedBillingClient>(httpClient =>
{
    // Configure timeout, retries, etc. — see dotnet-configuration-resilience
})
    .ConfigureHttpClient((provider, client) =>
    {
        // HTTP client lifecycle managed by IHttpClientFactory
    });

services.AddMaxioAdvancedBillingClient(options =>
{
    var config = provider.GetRequiredService<IConfiguration>();
    var apiKey = config["Maxio:ApiKey"] ?? throw new InvalidOperationException("Maxio:ApiKey not configured");
    var subdomain = config["Maxio:Subdomain"] ?? throw new InvalidOperationException("Maxio:Subdomain not configured");
    var environment = config["Maxio:Environment"];

    options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials
    {
        Username = apiKey,
        Password = "x" // Literal string "x" per Maxio Basic auth
    };

    options.Environment = environment == "Eu" 
        ? MaxioAdvancedBilling.Servers.ServerEnvironment.Eu 
        : MaxioAdvancedBilling.Servers.ServerEnvironment.Us;

    // Override base URL if needed (e.g., for mock/dev)
    // options.Server.Production.Us.Site = subdomain; // OR
    // options.Server.Production.Us.BaseUrl = $"https://{subdomain}.chargify.com";
});
```

### Configuration Binding Keys

From environment or `appsettings.json`:
- `Maxio:ApiKey` — API key from Maxio sandbox account
- `Maxio:Subdomain` — site subdomain (e.g., `cp-exp-3`)
- `Maxio:Environment` — `"Us"` (default) or `"Eu"` (optional; defaults to US)
- `Maxio:ProductFamilyHandle` — `eshop-subscribe` (for product lookups; optional if hardcoded in the integration layer)
- `Maxio:BaseUrl` — (optional) override base URL for sandbox/mock testing

### Authentication

- **Type:** HTTP Basic
- **Username:** API key (from `Maxio:ApiKey`)
- **Password:** literal string `"x"`
- **Per-environment:** `ServerEnvironment.Us` (default) → `https://{site}.chargify.com` · `ServerEnvironment.Eu` → `https://{site}.ebilling.maxio.com`

---

## ⚠ Trap Notes (MUST LOAD COMPANION SKILLS)

- **Step 1 (client registration, auth)** — The SDK's retry/timeout options are **not** a whole-call timeout and are **not** the `HttpClient` timeout. Register the `HttpClient` via `IHttpClientFactory` with appropriate timeouts **before** passing to the SDK client. **MUST load `dotnet-configuration-resilience`** before wiring timeouts and retry strategy.

- **Step 2 & 3 (API calls)** — List and read operations are typically **Case B (RawError)**; create/update operations are **Case A (typed errors)**. Confirm each operation's error case in the map row **before writing catch logic**. A catch ladder for `SdkException<CreateCustomerError>` will not catch `SdkException<RawError>` from a list operation. **MUST load `dotnet-error-handling`** before writing the error boundary.

- **Step 3 (subscription creation)** — The `CreateSubscriptionRequest` is deeply nested (`CreateSubscriptionRequest { Subscription: CreateSubscription { ... } }`). The inner model has many optional fields. Verify in the map that fields you omit are truly optional and carry the business meaning you intend (e.g., omitting `PaymentCollectionMethod` uses the site default, which is `Automatic` for most sandboxes). **MUST load `dotnet-models`** before building the request envelope.

- **Step 4 (enum construction & filtering)** — `SubscriptionState`, `SubscriptionStateFilter`, and other enums are **NOT** C# enums but `StringEnum<T>` records. Build with static members (e.g., `SubscriptionState.Active`) or `FromValue("active")`. Do **not** try to cast string directly to an enum type; the compiler will reject it. **MUST load `dotnet-models`** before constructing enums.

- **JsonException from deserialization** — A drifted or malformed 2xx body (e.g., a missing required field on `Subscription`) surfaces as `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException`. An SDK-exception-only catch ladder lets it escape the integration boundary. A non-2xx body that does not match the operation's `{Operation}Error` shape throws `JsonException` **while the error object is being constructed**, so the `JsonException` replaces the `SdkException` and the HTTP status is destroyed. A boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage. **MUST load `dotnet-error-handling`** before writing the integration boundary.

---

## REQUIRED READING

Load these companion skills **before implementation starts**. The sheet deliberately does not carry their contents; the skills contain defaults, worked examples, and gotchas a one-liner cannot capture:

| Skill | Step(s) it governs |
|-------|-------------------|
| `dotnet-client-initialization` | Step 1 — HttpClient lifecycle, DI registration |
| `dotnet-authentication` | Step 1 — HTTP Basic credentials, auth manager shape |
| `dotnet-configuration-resilience` | Step 1 — retry strategy, timeout per-attempt vs. per-call, retry gates on verbs and status codes |
| `dotnet-calling-endpoints` | Steps 2–4 — named vs. positional arguments, pagination semantics |
| `dotnet-models` | Steps 2–4 — building request envelopes, union factories, enum construction and wire names |
| `dotnet-error-handling` | Steps 2–4 — Case A vs. Case B, `TryGet…` accessors, JsonException boundary |

---

## Assumptions & Blockers

### Assumptions

1. **Maxio sandbox is already set up** with:
   - Site subdomain: `cp-exp-3`
   - Product Family: `eshop-subscribe` (contains Pro/Basic plans)
   - Pro Plan: `eshop-pro` ($299/mo, monthly billing, no trial)
   - Basic Plan: `basic-plan` ($29/mo, monthly billing, no trial)
   - Metered component: `api-call` ($0.01/unit, for future usage tracking)
   - No payment method required (test mode)

2. **Environment variables are supplied** at runtime:
   - `Maxio:ApiKey` (API key from sandbox account)
   - `Maxio:Subdomain` (site subdomain, e.g., `cp-exp-3`)
   - `Maxio:Environment` (optional, defaults to `Us`)

3. **Database schema supports subscription tracking:**
   - A table or mapping exists to store `UserId ↔ MaxioCustomerId` and `UserId ↔ SubscriptionIds`
   - This enables quick lookup of subscriptions when the user re-visits `/api/my-subscriptions`

4. **JWT authentication is implemented** for the PublicApi endpoints and is already working (e.g., via an existing token endpoint).

5. **.NET SDK 8.0 pinned in the project** but only .NET 10 SDK installed — the build will use `rollForward` or explicit environment variable to resolve. This is not an SDK integration blocker; it is a project setup task.

6. **In-memory database is sufficient** for subscription metadata and user–subscription mapping during the pilot. No SQL Server LocalDB is available in the environment.

### Blockers

None identified. The map and companion skills provide all required signatures, error shapes, and patterns.

---

## Summary

This plan implements a three-endpoint PublicApi for eShopOnWeb subscription management:
- **List plans** — fetch Pro ($299/mo) and Basic ($29/mo) from Maxio
- **Subscribe user** — create or verify Maxio customer (idempotent by user ID), then create subscription
- **View subscriptions** — retrieve user's active subscriptions from Maxio and display status

All operations use the Maxio Advanced Billing .NET SDK (`AsadAli.AdvancedBilling.Sdk`). HTTP Basic auth (API key + "x") is configured at client setup. Request/response envelopes are type-safe and generated. Error handling is split between Case A (typed) and Case B (raw) exceptions per operation; the map row names each case. Required companion skills address auth, error boundaries, model construction, and resilience; load them before coding.

Database persistence tracks user ↔ customer and user ↔ subscription mappings for idempotency and fast lookup.
