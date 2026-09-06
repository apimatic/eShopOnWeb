# Maxio Advanced Billing Integration Plan — eShopOnWeb Subscription Billing

## Scope & Sequence

**Step 1: Client initialization & configuration** — Wire the Maxio SDK client into DI, configure from settings.
**Step 2: GET /api/subscription-plans** — List available billing plans from Maxio.
**Step 3: POST /api/subscriptions** — Create a recurring subscription; ensure idempotency on double-click.
**Step 4: GET /api/my-subscriptions** — List subscriptions belonging to the authenticated user.

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Client Registration

| Item | Signature / Type | Details | Source |
|---|---|---|---|
| **Client class** | `MaxioAdvancedBillingClient` | Constructor: `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `MaxioAdvancedBillingClient.cs` |
| **Options class** | `MaxioAdvancedBillingClientOptions` | Properties: `Environment` (ServerEnvironment), `Retry` (RetryOptions), `Server` (ServerOptions), `BasicAuth` (BasicAuthCredentials?) | `MaxioAdvancedBillingClientOptions.cs` |
| **Auth type** | `BasicAuthCredentials` | Namespace: `MaxioAdvancedBilling.Core.Authentication.Basic` · Properties: `Username` (API key), `Password` (literal `"x"`) | `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| **Environment enum** | `ServerEnvironment` | Namespace: `MaxioAdvancedBilling.Servers` · Values: `ServerEnvironment.Us` (default), `ServerEnvironment.Eu` | `Servers/ServerEnvironment.cs` |
| **Root namespace** | `MaxioAdvancedBilling` | Use this for client, options, environments | — |
| **Configuration namespace** | `MaxioAdvancedBilling.Core.Configuration` | Use this for `RetryOptions` | — |

### Operation 1: GET /api/subscription-plans (List Products)

| Controller | Method | Parameters | Request | Response | Error | Pagination | Source |
|---|---|---|---|---|---|---|---|
| `client.Products` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | All params nullable; pass `null` to skip; defaults: `page`=1, `perPage`=20. **MUST pass all 8 optional params explicitly (null to skip).** | (none — GET with query params only) | `IReadOnlyList<ProductResponse>` · Response envelope: `ProductResponse` wraps `Product (product): Product !req` · **Extract inner `product` field from each item.** Key fields: `Product.Id`, `Product.Name`, `Product.Handle`, `Product.Description`, `Product.PriceInCents`, `Product.Interval`, `Product.IntervalUnit` | Case B: `SdkException<RawError>` · `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | Manual `page`+`perPage` | `operations/Products.md` |

### Operation 2: POST /api/subscriptions (Create Subscription)

| Controller | Method | Parameters | Request | Response | Error | Pagination | Source |
|---|---|---|---|---|---|---|---|
| `client.Subscriptions` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | `body` — nullable, no default → **must pass explicitly** | `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription !req }` · **Wrap CreateSubscription in CreateSubscriptionRequest.** · Required fields in `CreateSubscription`: none marked required; **carry notes-tied fields:** (1) product ID/handle — use `ProductId` or `ProductHandle` (at least one required by Notes); (2) customer identification — use `CustomerId` (existing), `CustomerReference` (idempotency key — **REQUIRED for idempotence**), or `CustomerAttributes` (new customer); (3) `Reference` (optional, for deduplication) | `SubscriptionResponse { Subscription (subscription): Subscription? }` · **Extract inner `subscription` field (optional).** Key fields: `Subscription.Id`, `Subscription.State` (SubscriptionState enum), `Subscription.BalanceInCents`, `Subscription.CurrentPeriodEndsAt`, `Subscription.NextAssessmentAt`, `Subscription.ProductPriceInCents`, `Subscription.Customer` | Case A: `SdkException<CreateSubscriptionError>` · Accessor: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] → field: `Errors (errors): IReadOnlyList<string> !req`; also `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |

### Operation 3: GET /api/my-subscriptions (List Subscriptions by Customer)

| Controller | Method | Parameters | Request | Response | Error | Pagination | Source |
|---|---|---|---|---|---|---|---|
| `client.Customers` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | `customerId` — required, positional; obtain from `ReadCustomerByReference` (see below) | (none — path param + GET) | `IReadOnlyList<SubscriptionResponse>` · **Extract inner `subscription` field from each item.** Key fields: `Subscription.Id`, `Subscription.State`, `Subscription.BalanceInCents`, `Subscription.CurrentPeriodEndsAt`, `Subscription.NextAssessmentAt` | Case B: `SdkException<RawError>` · `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | none (flat list) | `operations/Customers.md` |

**Idempotency contract:** When POST /api/subscriptions receives a duplicate creation request (same user/plan), Maxio rejects duplicate customer creation via the reference field and treats subsequent create calls as updates or duplicates. To implement idempotency: **store `CustomerReference` before sending, and always set it in `CreateSubscription.CustomerReference` to the authenticated user's ID (e.g. from JWT claims).** Maxio will find or create a single customer per reference; re-sending the subscription with the same customer reference + subscription reference will either find the existing subscription or fail cleanly.

### Supporting Operation: Get Customer by Reference (for listing subscriptions)

| Controller | Method | Parameters | Request | Response | Error | Pagination | Source |
|---|---|---|---|---|---|---|---|
| `client.Customers` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | `reference` — required, query param; use authenticated user's ID | (none — query param + GET) | `CustomerResponse { Customer (customer): Customer !req }` · **Extract inner `customer` field.** Key field: `Customer.Id` (pass to `ListCustomerSubscriptions`) | Case B: `SdkException<RawError>` · `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | none | `operations/Customers.md` |

### Supporting Operation: Create Customer (if needed on first subscription signup)

| Controller | Method | Parameters | Request | Response | Error | Pagination | Source |
|---|---|---|---|---|---|---|---|
| `client.Customers` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` | `body` — nullable, no default → **must pass explicitly** | `CreateCustomerRequest { Customer (customer): CreateCustomer !req }` · **Wrap CreateCustomer in CreateCustomerRequest.** · Fields in `CreateCustomer`: all optional; carry: `FirstName`, `LastName`, `Email`, `Reference` (idempotency: use authenticated user's ID) | `CustomerResponse { Customer (customer): Customer !req }` · **Extract inner `customer` field.** Key field: `Customer.Id` | Case A: `SdkException<CreateCustomerError>` · Accessor: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] → field: `Errors (errors): Errors?`; also `TryGetRawError(out RawError)` [fallback] | none | `operations/Customers.md` |

### Enums & Model Types Used

| Enum / Type | Namespace | Values / Purpose | Source |
|---|---|---|---|
| `SubscriptionState` | `MaxioAdvancedBilling.Models.Enums` | Possible values: `Active`, `Trialing`, `PastDue`, `Suspended`, `Canceled`, `Expired`, `OnHold`, `AwaitingSignup`, `Pending`, `FailedToCreate`, `Assessing`, `SoftFailure`, `Unpaid`, `TrialEnded`. Use to check subscription status. | `Models/Enums/SubscriptionState.cs` |
| `CollectionMethod` | `MaxioAdvancedBilling.Models.Enums` | Optional in `CreateSubscription`; values: `Automatic`, `Remittance`, `Prepaid`, `Invoice`. For eShopOnWeb subscriptions, default to `Automatic`. | `Models/Enums/CollectionMethod.cs` |
| `Product` | `MaxioAdvancedBilling.Models` | Record model. Use `Product.Id`, `Product.Handle`, `Product.Name`, `Product.PriceInCents`, `Product.Interval`, `Product.IntervalUnit`. | `Models/Product.cs` |
| `Subscription` | `MaxioAdvancedBilling.Models` | Record model. Use `Subscription.Id`, `Subscription.State`, `Subscription.BalanceInCents`, `Subscription.CurrentPeriodEndsAt`, `Subscription.NextAssessmentAt`, `Subscription.ProductPriceInCents`. | `Models/Subscription.cs` |
| `Customer` | `MaxioAdvancedBilling.Models` | Record model. Use `Customer.Id`, `Customer.FirstName`, `Customer.LastName`, `Customer.Email`, `Customer.Reference`. | `Models/Customer.cs` |
| `CreateSubscription` | `MaxioAdvancedBilling.Models` | Immutable record; pass fields: `ProductId` or `ProductHandle`, `CustomerId` or `CustomerReference` or `CustomerAttributes`, `Reference` (optional). | `Models/CreateSubscription.cs` |
| `CreateSubscriptionRequest` | `MaxioAdvancedBilling.Models` | Envelope: wraps `CreateSubscription` in field `Subscription (subscription)`. | `Models/CreateSubscriptionRequest.cs` |
| `CreateCustomer` | `MaxioAdvancedBilling.Models` | Immutable record; pass fields: `FirstName`, `LastName`, `Email`, `Reference`. | `Models/CreateCustomer.cs` |
| `CreateCustomerRequest` | `MaxioAdvancedBilling.Models` | Envelope: wraps `CreateCustomer` in field `Customer (customer)`. | `Models/CreateCustomerRequest.cs` |

---

## Trap Notes

⚠ **Step 1 (client initialization)** — The `HttpClient` you pass to the SDK client must be **long-lived and reused via `IHttpClientFactory`**, not instantiated per request. The SDK wrapper around it may be transient, but the underlying HTTP transport must not. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ **Step 1 (authentication)** — Basic auth: `Username` = your Maxio API key (from config), `Password` = literal string `"x"`. Set credentials **before** constructing the client or inside the DI callback. Never hardcode the key. **MUST load `dotnet-authentication`** before wiring credentials.

⚠ **Step 2 & 3 (calling endpoints)** — Operations with many optional parameters (e.g. `ListProducts` has 8 optional query params) **must pass explicit named arguments**; positional calls mis-bind. All 8 nullable params in `ListProducts` MUST be passed explicitly, even if `null`. **MUST load `dotnet-calling-endpoints`** before the first operation call.

⚠ **Step 2, 3, 4 (response envelopes)** — Response types wrap their payload in one field: `ProductResponse.Product`, `SubscriptionResponse.Subscription`, `CustomerResponse.Customer`. **Reads go one level down** — extract the inner field before using the data. Failing to unwrap adds an extra layer and breaks model access.

⚠ **Step 2, 3 (error handling)** — **Two error paths, both generate `JsonException` on malformed responses:**
   - **2xx body with missing required field** (e.g. `ProductResponse` missing `Product`): deserialization throws `JsonException` **not** `SdkException` — an SDK-exception-only catch misses it and lets it escape the boundary.
   - **Non-2xx body (e.g. 422) that doesn't match the operation's error shape** (e.g. `CreateSubscriptionError`): `JsonException` replaces the `SdkException` and the HTTP status is lost. A boundary that maps every `JsonException` to 5xx reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

   **MUST load `dotnet-error-handling`** before writing the error boundary. These two patterns must be handled together, not separately.

⚠ **Step 2, 3, 4 (idempotency on subscription create)** — Maxio's API is **not** idempotent on subscription creation by default. To implement idempotence in the endpoint: (1) **always set `CustomerReference` to the authenticated user's ID** (from JWT); (2) **always set `Reference` on the subscription** (unique key for this user + plan combo); (3) **query by reference before create** (use `FindSubscription(reference)` or catch errors); (4) **on 422 or duplicate, return the existing subscription**, don't retry. The call itself is not idempotent — **your endpoint must be.** **MUST load `dotnet-configuration-resilience`** to understand retry semantics (transport failures retry on POST; status retries do not).

---

## REQUIRED READING

Load these companion skills **before implementation starts**. The sheet does not carry their contents, only the step where each is critical.

| Skill | Used at step(s) |
|---|---|
| `dotnet-client-initialization` | 1 (client + HttpClient wiring, DI) |
| `dotnet-authentication` | 1 (Basic auth setup, credential storage) |
| `dotnet-calling-endpoints` | 2, 3, 4 (parameter binding, named args, response envelope unwrapping) |
| `dotnet-models` | 2, 3, 4 (immutable records, StringEnum construction, field access) |
| `dotnet-error-handling` | 2, 3, 4 (Case A/B distinction, `TryGet…` accessors, `JsonException` on malformed responses, boundary design) |
| `dotnet-configuration-resilience` | 2, 3 (retry semantics, transport vs status, idempotency contract) |

**Two mandatory rows from `dotnet-error-handling` for the first error-handling sheet:**

1. **`System.Text.Json.JsonException` from 2xx deserialization** — a drifted or malformed **2xx** body (missing `required` member like `Product` in `ProductResponse`) throws `JsonException` from deserialization, **not** `SdkException`. An SDK-exception-only catch ladder lets it escape the integration boundary.

2. **`System.Text.Json.JsonException` from non-2xx deserialization** — a **non-2xx** body that doesn't match the operation's generated `{Operation}Error` shape (e.g. 422 response isn't valid `CreateSubscriptionError`) throws `JsonException` **while the error object is being constructed**, replacing the `SdkException` and destroying the HTTP status. A boundary that maps every `JsonException` to 5xx misreports the rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

---

## Assumptions & Blockers

### Assumptions
1. **Configuration source** — Maxio credentials (API key, subdomain, environment, product family handle) are supplied via application configuration binding under a `Maxio:` section (or environment variables `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_ENVIRONMENT`, `MAXIO_DEFAULT_PRODUCT_FAMILY`). The implementer must wire the config provider.

2. **JWT authentication** — Authenticated user identity is extracted from JWT claims (e.g. `sub`, `nameid`, or app-specific claim) and used as `CustomerReference` in subscription operations. The implementer supplies the claim name or extraction logic.

3. **Idempotency key** — `CustomerReference` = authenticated user ID; `Reference` (subscription) = deterministic combo of user ID + product ID/handle. The implementer must supply the composition logic.

4. **No existing Maxio customers in eShopOnWeb DB** — Plan assumes all customers are created via the SDK on first subscription signup. If legacy customers exist, the implementer must migrate or alias them via `Reference`.

5. **Payment method handling** — Subscription creation does not pass payment details (card/bank); `PaymentProfileId` is `null`. Maxio's sandbox/test site allows subscriptions without payment on file. Production deployments must align with Maxio's payment-method requirements.

### Blockers
None — all operations are documented in the map and match the SDK v1.0.2 signature.

---

## Notes

**Endpoint conventions (eShopOnWeb PublicApi):**
- Request/response DTOs via AutoMapper
- Minimal API with `IEndpoint<IResult, T>` and `AddRoute`/`HandleAsync`
- JWT user identity from token
- Dependency injection for services

**Sandbox site (cp-exp-1):**
- Product Family: `eshop-subscribe` (handle) / 3023074 (ID)
- Plans: `eshop-pro` ($299/mo), `basic-plan` ($29/mo)
- Metered component: `api-call` ($0.01/unit)
- No trial, no setup fee, never expires, no payment required

**Config binding keys (supply to DI):**
- `Maxio:ApiKey` → Basic auth username
- `Maxio:Subdomain` → Site subdomain (e.g. `cp-exp-1`)
- `Maxio:Environment` → `US` or `EU` (maps to `ServerEnvironment`)
- `Maxio:ProductFamilyHandle` → default product family for plan lookup
