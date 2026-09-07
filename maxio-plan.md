# Maxio Advanced Billing Integration Plan — eShopOnWeb Recurring Subscriptions

## Scope & Sequence

1. **Fetch subscription plans** — `ProductFamilies.ListProductsForProductFamily` to enumerate available plans (Pro Plan `eshop-pro`, Basic Plan `basic-plan`)
2. **Idempotent customer lookup/creation** — `Customers.ReadCustomerByReference` to detect existing Maxio customer; `Customers.CreateCustomer` if new
3. **Create subscription** — `Subscriptions.CreateSubscription` with the customer and selected plan; confirm plan/price/state/next-billing-date
4. **List user subscriptions** — `Customers.ListCustomerSubscriptions` to fetch user's active subscriptions
5. **Error boundary** — wrap all calls in a typed exception handler; handle `JsonException` from malformed 2xx bodies and replace-status errors on 422

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operation 1: Fetch Product Family and Plans

| Aspect | Details |
|--------|---------|
| **Controller** | `client.ProductFamilies` |
| **Method 1** | `ReadProductFamily(int id, CancellationToken ct = default)` |
| **Method 2** | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| **Query params (wire ← C#)** | `page` ← `page`, `per_page` ← `perPage`, `date_field` ← `dateField`, `filter` ← `filter`, `start_date` ← `startDate`, `end_date` ← `endDate`, `start_datetime` ← `startDatetime`, `end_datetime` ← `endDatetime`, `include_archived` ← `includeArchived`, `include` ← `include` |
| **Return type** | Method 1: `ProductFamilyResponse` (wraps `ProductFamily`); Method 2: `IReadOnlyList<ProductResponse>` (wraps `Product`) |
| **Error** | **Case B** (`SdkException<RawError>`) — `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` · `ReadAsBytes(): ReadOnlyMemory<byte>` |
| **Request body** | None (GET operations) |
| **Response envelope** | Method 1: `ProductFamilyResponse.ProductFamily` (nullable `ProductFamily?`); Method 2: direct list of `ProductResponse` items, each wrapping a `Product` |
| **Key fields in response** | `ProductFamily`: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`; `Product`: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?` |
| **Source** | `operations/ProductFamilies.md`, `records-3-Of-Su.md` (ProductFamily, Product, ProductFamilyResponse, ProductResponse) |

**Notes:**
- `ReadProductFamily` accepts both ID number or `"handle:my-family"` format (per operation Notes); use handle `eshop-subscribe` directly as the numeric ID in the path.
- `ListProductsForProductFamily` requires the product family ID as a path parameter and accepts optional date/filter query params; pass `null` for all optional params to skip them.
- Both return wrapped responses: extract the inner `ProductFamily`/`Product` from the envelope before consuming.

---

### Operation 2: Idempotent Customer Lookup/Create

| Aspect | Details |
|--------|---------|
| **Controller** | `client.Customers` |
| **Method 1** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` |
| **Method 2** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` |
| **Query params (wire ← C#)** | Method 1: `reference` ← `reference` |
| **Return type** | Both return `CustomerResponse` (wraps `Customer`) |
| **Error Method 1** | **Case B** (`SdkException<RawError>`) — `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` · `ReadAsBytes(): ReadOnlyMemory<byte>` |
| **Error Method 2** | **Case A** (`SdkException<CreateCustomerError>`) — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| **Request body** | Method 2: `CreateCustomerRequest` with required field `Customer (customer): CreateCustomer !req`; inner `CreateCustomer` has optional fields: `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?`, `Address (address): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?` |
| **Response envelope** | `CustomerResponse.Customer` (nullable `Customer?`) |
| **Key fields in Customer** | `Id (id): int?`, `Reference (reference): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?` |
| **Source** | `operations/Customers.md`, `records-2-Cr-Ne.md` (CreateCustomer, CreateCustomerRequest, Customer, CustomerResponse, CustomerErrorResponse1) |

**Idempotency pattern:**
1. Call `ReadCustomerByReference(reference: "user-app-id")` with the eShopOnWeb user's unique ID as reference.
2. If it returns successfully → extract `CustomerResponse.Customer.Id` (non-null when found) and use it for subscription creation.
3. If it throws `SdkException<RawError>` with status 404 (or any other status) → customer doesn't exist; proceed to create.
4. For creation, pass a `CreateCustomerRequest` with `Customer` wrapping a `CreateCustomer` with `FirstName`, `LastName`, `Email`, `Reference` (the same unique ID) set. **Do not pass payment profile info** — the task notes payment is not required.

---

### Operation 3: Create Subscription

| Aspect | Details |
|--------|---------|
| **Controller** | `client.Subscriptions` |
| **Method** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` |
| **Return type** | `SubscriptionResponse` (wraps `Subscription`) |
| **Error** | **Case A** (`SdkException<CreateSubscriptionError>`) — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| **Request body** | `CreateSubscriptionRequest` with required field `Subscription (subscription): CreateSubscription !req`; inner `CreateSubscription` has optional/nullable fields: `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `CustomerId (customer_id): int?`, `CustomerReference (customer_reference): string?`, `Reference (reference): string?` |
| **Response envelope** | `SubscriptionResponse.Subscription` (nullable `Subscription?`) |
| **Key fields in Subscription** | `Id (id): int?`, `State (state): SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `ActivatedAt (activated_at): DateTimeOffset?`, `CreatedAt (created_at): DateTimeOffset?`, `Product (product): Product?` (nested, includes product name/handle), `Customer (customer): Customer?` (nested, includes customer details) |
| **Source** | `operations/Subscriptions.md`, `records-2-Cr-Ne.md` (CreateSubscription, CreateSubscriptionRequest), `records-3-Of-Su.md` (Subscription, SubscriptionResponse) |

**Key contract points:**
- Either `ProductHandle` (e.g. `"eshop-pro"`) or `ProductId` must be supplied; prefer handle for the plan handles given in the task.
- Either `CustomerId` (from the Maxio customer ID retrieved above) or `CustomerReference` (the same unique ID) may be supplied; prefer the numeric ID if available, but the reference works.
- `Reference` is optional; use it if the application needs to track a unique identifier for the subscription at eShopOnWeb's side.
- Response fields `State` should be `SubscriptionState.Active` on success (enum, namespace `MaxioAdvancedBilling.Models.Enums`).
- `NextAssessmentAt` is the datetime of the next billing; return this to the UI.
- `ProductPriceInCents` is the monthly charge in cents (e.g. `29900` for $299.00/mo for Pro Plan).

---

### Operation 4: List User Subscriptions

| Aspect | Details |
|--------|---------|
| **Controller** | `client.Customers` |
| **Method** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` |
| **Return type** | `IReadOnlyList<SubscriptionResponse>` (each wraps a `Subscription`) |
| **Error** | **Case B** (`SdkException<RawError>`) — `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` · `ReadAsBytes(): ReadOnlyMemory<byte>` |
| **Request body** | None (GET operation); `customerId` is a path parameter |
| **Response envelope** | Direct list of `SubscriptionResponse` items; extract the inner `Subscription` from each |
| **Key fields in Subscription** | (Same as Operation 3 response) `Id`, `State`, `ProductPriceInCents`, `CurrentPeriodEndsAt`, `NextAssessmentAt`, `Product.Name`, `Product.Handle` |
| **Source** | `operations/Customers.md`, `records-3-Of-Su.md` (Subscription, SubscriptionResponse) |

---

### Enum Values (for response contract)

| Enum | C# member ← Wire value | Usage |
|------|------------------------|-------|
| `SubscriptionState` | `Active (active)`, `Canceled (canceled)`, `Trialing (trialing)`, `PastDue (past_due)`, `Suspended (suspended)`, `Expired (expired)` | Response field `Subscription.State` tells the subscription status |
| `IntervalUnit` | `Month (month)`, `Day (day)` | Response field `Product.IntervalUnit` (usually `Month`) |
| Namespace | `MaxioAdvancedBilling.Models.Enums` | All enum members live here |

---

### Client Construction & Authentication

| Aspect | Details |
|--------|---------|
| **Package** | `AsadAli.AdvancedBilling.Sdk` (NuGet) |
| **Root namespace** | `MaxioAdvancedBilling` (the `using` statement) |
| **Client class** | `MaxioAdvancedBillingClient` |
| **Options class** | `MaxioAdvancedBillingClientOptions` |
| **Auth scheme** | HTTP **Basic** — `Username` = API key (from config `Maxio:ApiKey`), `Password` = literal `"x"` |
| **Auth namespace** | `MaxioAdvancedBilling.Core.Authentication.Basic` (for `BasicAuthCredentials`) |
| **Environment** | `ServerEnvironment.Us` (default, from `MaxioAdvancedBilling.Servers`); override if EU hosting is used |
| **Server override** | `options.Server.Production.Us.BaseUrl` or `.Eu.*` to redirect to mock; `options.Server.Production.Us.Site` = subdomain (defaults to `"subdomain"` from config `Maxio:Subdomain`) |
| **Configuration namespace** | `MaxioAdvancedBilling.Core.Configuration` (for `RetryOptions`) |
| **Source** | `sdk-map.md` (Getting a client section) |

**Sample construction (from config):**
```csharp
var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials 
    { 
        Username = configuration["Maxio:ApiKey"], 
        Password = "x" 
    },
    Environment = ServerEnvironment.Us,
    Server = new ServerOptions
    {
        Production = new ProductionOptions
        {
            Us = new ServerConfig { Site = configuration["Maxio:Subdomain"] }
        }
    }
};
var client = new MaxioAdvancedBillingClient(httpClient, options);
```

---

## Trap Notes

⚠ **Step 1** (Fetch plans) — The product family can be queried by ID or in `"handle:handle-name"` format per the operation Notes, but the C# signature takes an `int id`; you must look up the numeric ID first, or infer from Maxio's API documentation. **MUST load `dotnet-calling-endpoints`** to confirm how to pass the product family identifier and handle the wrapped response envelope.

⚠ **Step 2** (Idempotent customer) — `ReadCustomerByReference` returns a 404 as a normal case (customer not found), not an exception; you **must** inspect the returned `CustomerResponse.Customer` field (which will be `null` if 404 is decoded as an empty body, or throw if the status is mishandled). The wrapped envelope is critical — always extract the inner object before checking null. **MUST load `dotnet-calling-endpoints`** to understand how nullable envelopes are deserialized and when a 404 becomes an exception vs. an empty response.

⚠ **Step 2** (Customer creation) — Case A typed error `CreateCustomerError` with accessor `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` on 422 is the standard validation-error shape. If a 422 arrives but the accessor returns `false` (shape mismatch), fall back to `TryGetRawError(out RawError)` and read the raw body; this path is triggered by a true API error shape that the generated code doesn't match — treat it as an outage if this path is taken. **MUST load `dotnet-error-handling`** to understand when and how to use both accessors.

⚠ **Step 3** (Subscription creation) — `CreateSubscription` has two ways to identify the customer: `CustomerId` (numeric Maxio ID, preferred) and `CustomerReference` (your app's unique reference string); if both are passed, behavior is undefined by the SDK docs — use only one. The same product/plan can be specified by handle (recommended, use `"eshop-pro"`) or numeric ID; prefer the handle for readability and to match the task spec. **MUST load `dotnet-calling-endpoints`** to confirm parameter binding and the request body shape.

⚠ **Step 3** (Subscription state on return) — The response includes a nested `Product` and `Customer` object; these are populated when the subscription is created, so you may extract pricing and customer details directly without a second call. Verify `Subscription.State` is `SubscriptionState.Active` to confirm creation succeeded; any other state (e.g. `AwaitingSignup`, `Canceled`) indicates a problem. **MUST load `dotnet-models`** to understand union types and how to construct/read nested objects.

⚠ **Error boundary** — `System.Text.Json.JsonException` can arise from two directions and needs opposite handling:
  1. A drifted or malformed **2xx** body (e.g., missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
  2. A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing the error boundary — the boundary must handle `JsonException` separately and preserve HTTP status on `SdkException`.

⚠ **Retry/resilience** — Maxio's API may return transient 5xx or transport-level failures; the SDK's built-in `Polly`-backed retry options do NOT distinguish between idempotent and non-idempotent verbs correctly (e.g. `POST /subscriptions` is **not** idempotent, but a transport failure **will** retry it). Ensure that `CreateCustomer` and `CreateSubscription` are wrapped in defensive code to detect and reject duplicates on retry (use the `reference` field to identify duplicates, and check for 409 Conflict or duplicate-key errors). **MUST load `dotnet-configuration-resilience`** to confirm retry bounds and decide whether to disable retries for write operations or detect idempotence violations at the boundary.

⚠ **Configuration** — Credentials, the API key, and the site subdomain **must** come from the application's configuration (`appsettings.json`, environment variables via `IConfiguration`), not hardcoded. The config binding key is `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:BaseUrl` (optional override), and `Maxio:ProductFamilyHandle` (if needed). **MUST load `dotnet-client-initialization`** for DI registration patterns and how to inject the client into controllers.

---

## REQUIRED READING

Load the following companion skills **before implementation starts**. The sheet deliberately does not carry their contents; these are the how-to guides:

| Skill | Governs |
|-------|---------|
| `dotnet-client-initialization` | Client construction, HttpClient factory registration, DI setup, options binding |
| `dotnet-authentication` | Basic auth credentials, setting username/password, per-environment config, rotating keys |
| `dotnet-calling-endpoints` | Operation signatures, parameter binding (positional vs. named), request body construction, response envelope unwrapping, async/await |
| `dotnet-models` | Record immutability, `required` fields, union (`OneOf`/`AnyOf`) types, enum construction (`StringEnum<T>`), nested objects |
| `dotnet-error-handling` | Exception boundaries, `SdkException<T>` shape, `TryGet…` accessors, Case A vs. B, `JsonException` handling, status preservation |
| `dotnet-configuration-resilience` | Retry options (`RetryOptions`, `Polly`), timeout semantics, base-URL overrides, per-attempt vs. total timeout, logging hooks, idempotence detection |
| `dotnet-testing` | Stubbing the SDK, `HttpClient` test seams, mock responses, assertion patterns |

**Both of the following hazard rows must be included in the first error-boundary implementation:**

- **Deserialization JsonException on 2xx response** — a malformed or missing `required` field in a success response surfaces as `System.Text.Json.JsonException`, not as `SdkException`. An SDK-exception-only catch will let it escape; the boundary must catch `JsonException` separately and map it to a 5xx with diagnostic detail. **MUST load `dotnet-error-handling`**.
- **Deserialization JsonException on non-2xx response** — if the HTTP status body does not match the operation's generated `{Operation}Error` shape, the `JsonException` is thrown **during** error object construction, replacing the `SdkException` and destroying the HTTP status. A boundary that maps every `JsonException` to a generic 5xx will report a real API error (e.g. 422 validation failure) as an outage, and callers that retry 5xx will retry a problem that cannot succeed. Preserve HTTP status on `SdkException`; investigate and log `JsonException` separately. **MUST load `dotnet-error-handling`**.

---

## Assumptions & Blockers

1. **Assumption: Product family lookup by handle** — The task provides product family handle `eshop-subscribe`, but `ReadProductFamily(int id)` takes a numeric ID. The plan assumes you will look up this numeric ID from Maxio's API outside this operation, or that you can call the operation with the handle in a string format (per the operation Notes: `"handle:eshop-subscribe"`). The exact mechanics of passing a handle vs. an ID are deferred to `dotnet-calling-endpoints`. If the API rejects handle-format strings in the path, you must fetch the numeric ID first.

2. **Assumption: No payment method required** — The task notes "payment method NOT required" for the plans. The plan does not pass `payment_profile_id` or payment attributes in `CreateSubscription`. If the Maxio API validation rejects subscriptions without a payment method at runtime, the implementation must handle this and either provide a dummy payment profile or adjust the API call shape. The error response will be a 422 with `TryGetErrorListResponse1` details.

3. **Assumption: Subscription state is always Active on 2xx** — The plan assumes that a 2xx response from `CreateSubscription` means the subscription state is at minimum in a live state (e.g. `Active`, `Trialing`). If Maxio returns `AwaitingSignup` or other non-live states on creation, the UI flow may need adjustment. Check `Subscription.State` in production to confirm.

4. **Assumption: Reference field uniqueness** — The plan uses the eShopOnWeb user ID as the `reference` field in `CreateCustomer` and `CreateSubscription` to enable idempotency. If Maxio's API does not enforce unique references or does not allow lookup by reference across multiple creates, the idempotency strategy will fail. The operation `ReadCustomerByReference` exists and should work, but it must be confirmed that `reference` is stable and never duplicated.

5. **Assumption: Site subdomain is stable** — The config binding `Maxio:Subdomain` is assumed to point to the correct Maxio site (e.g. `"cp-exp-3"`). If the subdomain or site changes, all calls will fail with auth or 404 errors. The plan does not include site validation; this is a deployment-time fact.

**Blockers: None identified.** The operation signatures match the task scope; no API capabilities are missing.

