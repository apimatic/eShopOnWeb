# Maxio Subscription Integration Plan — eShopOnWeb PublicApi

## Scope & Sequence

The integration adds subscription management to PublicApi with three endpoints:

1. **GET /api/subscription-plans** — List products from the `eshop-subscribe` family
2. **POST /api/subscriptions** — Enroll logged-in user in a subscription plan
3. **GET /api/my-subscriptions** — Retrieve the user's active subscriptions

Each step follows a contract with Maxio's API and enforces idempotent customer creation. Subscriptions are mapped to logged-in callers via in-memory store (user ID → subscription ID).

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

### Operation Reference

| Step | Controller.Method | Signature | Request Model + Fields | Response Envelope + Inner Fields | Error Case | Pagination | Source |
|---|---|---|---|---|---|---|---|
| 1. List plans | `Products.ListProducts` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | None; all parameters are query filters. To list all products, pass `null` for `dateField`, `filter`, `endDate`, `endDatetime`, `startDate`, `startDatetime`, `includeArchived`, `include`. Pass `page=1, perPage=20` (defaults). | `IReadOnlyList<ProductResponse>` — unwrap each element: `ProductResponse.Product` (type `MaxioAdvancedBilling.Models.Product`). Fields in scope: `Id: int?`, `Name: string?`, `Handle: string?`, `PriceInCents: long?`, `Interval: int?`, `IntervalUnit: IntervalUnit?` (enum). | **Case B:** `SdkException<RawError>` — `.Error.StatusCode: HttpStatusCode`, `.Error.ReadAsString(): string`, `.Error.ReadAsJson<T>(): T?` | Manual `page`+`perPage` | `operations/Products.md` |
| 2. Lookup customer by reference | `Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | Query param `reference` (wire: `reference`) — pass the logged-in user's ID as the reference value. | `CustomerResponse` — unwrap: `CustomerResponse.Customer` (type `MaxioAdvancedBilling.Models.Customer`). Field in scope: `Id: int?` (Maxio-generated customer ID). | **Case B:** `SdkException<RawError>` — `.Error.StatusCode`, `.Error.ReadAsString()`, `.Error.ReadAsJson<T>()` | None | `operations/Customers.md` |
| 3. Create customer (idempotent) | `Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` mandatory explicit pass | `CreateCustomerRequest` { `Customer (customer): CustomerAttributes !req` }. `CustomerAttributes` fields: `FirstName: string?`, `LastName: string?`, `Email: string?`, `Reference: string?` (wire: `reference` — must be unique; use logged-in user ID). All other fields optional. | `CustomerResponse` — unwrap: `CustomerResponse.Customer` → `Customer.Id: int?` | **Case A:** `SdkException<CreateCustomerError>` — `.Error.TryGetCustomerErrorResponse1(out CustomerErrorResponse1): bool` [422], `.Error.TryGetRawError(out RawError): bool` [fallback]. `CustomerErrorResponse1` payload carries structured errors. | None | `operations/Customers.md` |
| 4. Create subscription | `Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` mandatory explicit pass | `CreateSubscriptionRequest` { `Subscription (subscription): CreateSubscription !req` }. `CreateSubscription` fields (all optional except where product specified): `ProductHandle: string?` (wire: `product_handle`), `ProductId: int?` (wire: `product_id`) — **one** of these must be set; `CustomerId: int?` (wire: `customer_id`), `CustomerReference: string?` (wire: `customer_reference`) — **one** must identify the customer. **Notes:** To accept the subscription, exactly one of `ProductHandle` or `ProductId` must be provided; exactly one of `CustomerId` or `CustomerReference` must be provided. | `SubscriptionResponse` — unwrap: `SubscriptionResponse.Subscription` (type `MaxioAdvancedBilling.Models.Subscription`). Fields in scope: `Id: int?` (subscription ID), `State: SubscriptionState?` (enum — `SubscriptionState.Active`, etc.), `ProductHandle: string?` (wire: `product_handle`), `CurrentPeriodEndsAt: DateTimeOffset?` (wire: `current_period_ends_at`), `CustomerId: int?` (wire: `customer_id`). | **Case A:** `SdkException<CreateSubscriptionError>` — `.Error.TryGetErrorListResponse1(out ErrorListResponse1): bool` [422], `.Error.TryGetRawError(out RawError): bool` [fallback]. | None | `operations/Subscriptions.md` |
| 5. List subscriptions (customer) | `Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | Route param `customerId` (path `/customers/{customer_id}/subscriptions.json`). | `IReadOnlyList<SubscriptionResponse>` — unwrap each: `SubscriptionResponse.Subscription` → fields: `Id: int?`, `State: SubscriptionState?`, `ProductHandle: string?`, `CurrentPeriodEndsAt: DateTimeOffset?`. | **Case B:** `SdkException<RawError>` | None | `operations/Customers.md` |
| 5. Alternate: List subscriptions (global filter) | `Subscriptions.ListSubscriptions` | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | Query params; pass `null` for all filters to list all. To filter by state, pass `SubscriptionStateFilter.Active` (enum member, wire: `"active"`). | `IReadOnlyList<SubscriptionResponse>` — unwrap each: `SubscriptionResponse.Subscription` → fields as above. | **Case B:** `SdkException<RawError>` | Manual `page`+`perPage` | `operations/Subscriptions.md` |

---

### Request/Response Model Details

#### CreateCustomerRequest & CreateCustomer
Namespace: `MaxioAdvancedBilling.Models`

`CreateCustomerRequest` wraps a `CreateCustomer` object:
```csharp
new CreateCustomerRequest
{
    Customer = new CreateCustomer
    {
        FirstName = "John",
        LastName = "Doe",
        Email = "john@example.com",
        Reference = userId  // unique customer ref
    }
}
```

`CreateCustomer` fields:

| Field | Type | Required | Wire Name | Notes |
|---|---|---|---|---|
| `FirstName` | `string?` | No | `first_name` | Optional but recommended for plan data |
| `LastName` | `string?` | No | `last_name` | Optional but recommended |
| `Email` | `string?` | No | `email` | Optional but recommended |
| `Reference` | `string?` | No | `reference` | **Must be unique per site.** Use logged-in user's unique ID here (e.g. UUID or app user ID). The Maxio Notes state: "unique identifier for the customer from your own app". |

#### CreateSubscription
Namespace: `MaxioAdvancedBilling.Models`

| Field | Type | Required | Wire Name | Acceptance Notes |
|---|---|---|---|---|
| `ProductHandle` | `string?` | No | `product_handle` | **One of `ProductHandle` or `ProductId` must be provided.** Plan handle is `"eshop-pro"` or `"basic-plan"` (sandbox site `cp-exp-4`). |
| `ProductId` | `int?` | No | `product_id` | **Alternate to `ProductHandle`.** Not needed if handle is used. |
| `CustomerId` | `int?` | No | `customer_id` | **One of `CustomerId` or `CustomerReference` must be provided.** Use Maxio-generated customer ID from CreateCustomer or ReadCustomerByReference. |
| `CustomerReference` | `string?` | No | `customer_reference` | **Alternate to `CustomerId`.** Use logged-in user ID matching customer's reference field. |
| All other fields | various | No | — | Omit unless explicitly needed (payment method, coupon, etc.). |

#### Product (from ProductResponse.Product)
Namespace: `MaxioAdvancedBilling.Models`

| Field | Type | Wire Name | Notes |
|---|---|---|---|
| `Id` | `int?` | `id` | Maxio product ID. |
| `Name` | `string?` | `name` | Display name: "eshop-pro" or "basic-plan". |
| `Handle` | `string?` | `handle` | API handle (same as name in sandbox). |
| `PriceInCents` | `long?` | `price_in_cents` | Monthly price in cents: 29900 ($299.00) or 2900 ($29.00). |
| `Interval` | `int?` | `interval` | Billing interval count: typically 1. |
| `IntervalUnit` | `IntervalUnit?` | `interval_unit` | Enum `IntervalUnit` — wire value `"month"` (member `IntervalUnit.Month`). |

#### Subscription (from SubscriptionResponse.Subscription)
Namespace: `MaxioAdvancedBilling.Models`

**Note:** `CustomerId` and `ProductHandle` are NOT direct fields. Access them through nested objects:
- `subscription.Customer?.Id` (from nested `Customer` object, type `Customer?`, wire: `"customer"`)
- `subscription.Product?.Handle` (from nested `Product` object, type `Product?`, wire: `"product"`)

| Field | Type | Wire Name | Notes |
|---|---|---|---|
| `Id` | `int?` | `id` | Maxio subscription ID. |
| `State` | `SubscriptionState?` | `state` | Enum `SubscriptionState` — `Active` (wire `"active"`), `Canceled` (wire `"canceled"`), etc. See enums.md for all values. |
| `Customer` | `Customer?` | `customer` | Nested customer object. Access `Id`, `FirstName`, `Email`, etc. from this. |
| `Product` | `Product?` | `product` | Nested product object. Access `Handle`, `Name`, `PriceInCents`, etc. from this. |
| `CurrentPeriodEndsAt` | `DateTimeOffset?` | `current_period_ends_at` | Next billing date. |
| `CouponCode` | `string?` | `coupon_code` | Applied coupon (if any). |
| `ActivatedAt` | `DateTimeOffset?` | `activated_at` | When subscription was activated. |

---

### Error Types & Accessors

#### Case A: CreateCustomerError
Namespace: `MaxioAdvancedBilling.Errors`

Thrown as `SdkException<CreateCustomerError>`. Accessors:
- `.Error.TryGetCustomerErrorResponse1(out CustomerErrorResponse1): bool` — returns `true` on **422 Unprocessable Entity**. Payload type: `CustomerErrorResponse1` (namespace `MaxioAdvancedBilling.Models`).
- `.Error.TryGetRawError(out RawError): bool` — fallback for other statuses.

#### Case A: CreateSubscriptionError
Namespace: `MaxioAdvancedBilling.Errors`

Thrown as `SdkException<CreateSubscriptionError>`. Accessors:
- `.Error.TryGetErrorListResponse1(out ErrorListResponse1): bool` — returns `true` on **422**. Payload type: `ErrorListResponse1` with `Errors: IReadOnlyList<string> !req`.
- `.Error.TryGetRawError(out RawError): bool` — fallback.

#### Case B: RawError
Namespace: `MaxioAdvancedBilling.Core.ErrorResponse`

Thrown as `SdkException<RawError>`. Fields:
- `.Error.StatusCode: HttpStatusCode`
- `.Error.ReadAsString(): string`
- `.Error.ReadAsJson<T>(): T?` — parse JSON body as type `T`.

---

### Enums in Scope

All enums are in namespace `MaxioAdvancedBilling.Models.Enums`. Construct via static members or `Type.FromValue(wireValue)`.

| Enum | Members | Wire Values | Notes |
|---|---|---|---|
| `SubscriptionState` | `Active`, `Canceled`, `Expired`, `Trialing`, `PastDue`, `Suspended`, `Paused`, `AwaitingSignup`, `OnHold`, others | `"active"`, `"canceled"`, `"expired"`, `"trialing"`, `"past_due"`, etc. | The subscription's current state. Only `Active` is fully accepted/active. |
| `IntervalUnit` | `Month`, `Day` | `"month"`, `"day"` | Billing period unit. Plans use `Month`. |
| `SubscriptionStateFilter` | `Active`, `Canceled`, `Expired`, `OnHold`, `PastDue`, `Suspended`, `TrialEnded`, `Trialing`, `Unpaid`, others | `"active"`, `"canceled"`, etc. | Filter when listing subscriptions. |
| `CollectionMethod` | `Automatic`, `Invoice`, `Prepaid`, `Remittance` | `"automatic"`, `"invoice"`, etc. | Payment collection strategy (if explicitly setting). |

---

### Client Construction & Configuration

Namespace for client: `MaxioAdvancedBilling`  
Namespace for auth: `MaxioAdvancedBilling.Core.Authentication.Basic`  
Namespace for servers: `MaxioAdvancedBilling.Servers`

**Client instantiation:**
```csharp
var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials 
    { 
        Username = "<API_KEY>", 
        Password = "x" 
    },
    Environment = ServerEnvironment.Us,
    Server = new ServerOptions
    {
        Production = new ProductionOptions
        {
            Us = new ProductionOptions.UsOptions 
            { 
                Site = "cp-exp-4" 
            }
        }
    }
};
var client = new MaxioAdvancedBillingClient(httpClient, options);
```

**Configuration:**
- `ApiKey` (config key) → `options.BasicAuth.Username`
- `Subdomain` (config key, default `"cp-exp-4"`) → `options.Server.Production.Us.Site`
- `BaseUrl` — override via `options.Server.Production.Us.BaseUrl` (type: `string`, default: `"https://{site}.chargify.com"`); for EU: `options.Server.Production.Eu.BaseUrl` and `options.Server.Production.Eu.Site`
- `ProductFamilyHandle` (config key, value `"eshop-subscribe"`) — passed to filter/read in app logic, not SDK config
- `Environment` — `ServerEnvironment.Us` for US hosting (default); `ServerEnvironment.Eu` if EU account
- **Note:** `ServerOptions.Production` is of type `ProductionOptions`, which has `Us` (type `ProductionOptions.UsOptions`) and `Eu` (type `ProductionOptions.EuOptions`) properties. There is no `ServerConfig` type.

**Read from environment variables:**
```
MAXIO_API_KEY=<key>
MAXIO_SITE_SUBDOMAIN=cp-exp-4
MAXIO_ENVIRONMENT=us
MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe
```

Bind these to an `IOptions<MaxioSettings>` via `IConfiguration`, then inject into controller/service.

---

### Idempotency & Customer Mapping

**Customer Creation (idempotent):**

1. Call `ReadCustomerByReference(userId)` (where `userId` = logged-in user's unique ID).
2. If it succeeds → customer exists; use returned `Customer.Id`.
3. If it throws `SdkException<RawError>` with `StatusCode == HttpStatusCode.NotFound` (404) → customer doesn't exist; proceed to step 4.
4. Call `CreateCustomer(new CreateCustomerRequest { Subscription = new CreateSubscription { ... Customer = new CustomerAttributes { Reference = userId, ... } } })` to create.
5. Extract and store `CustomerResponse.Customer.Id`.

**Subscription Storage (in-memory):**

Maintain a `Dictionary<string, int>` mapping:
- Key: logged-in user ID (claim from JWT)
- Value: Maxio subscription ID

Load at startup or on first use; persist across request scope (if using singleton or app-level cache). On logout, optionally clear the user's entry. On subscription creation, insert; on cancellation, optionally remove.

---

## Trap Notes

⚠ **Step 1 (client registration)** — the SDK client is injected via DI. The `HttpClient` must be **long-lived and reused** across requests; the SDK client wrapper may be transient. **MUST load `dotnet-client-initialization`** before wiring DI or instantiating the client.

⚠ **Step 2 (authentication)** — credentials are set in `BasicAuthCredentials` before client construction. **Username = API key, Password = literal `"x"`**. Do not hardcode; load from environment or `IConfiguration`. **MUST load `dotnet-authentication`** before setting credentials.

⚠ **Step 3 (calling endpoints / building requests)** — each operation has a unique signature with both nullable and non-nullable required params. **ListProducts requires passing `null` for most filters; CreateCustomer and CreateSubscription require passing a non-null body object explicitly (no default).** When passing `null` to a nullable param, the SDK omits it from the query string. **MUST load `dotnet-calling-endpoints`** before writing the first call.

⚠ **Step 4 (models & response envelopes)** — operations return wrapped responses: `ProductResponse.Product`, `CustomerResponse.Customer`, `SubscriptionResponse.Subscription`. Always unwrap one level. Enums are **not** C# enums; construct via `IntervalUnit.Month` (static member) or `IntervalUnit.FromValue("month")`. **MUST load `dotnet-models`** before reading or building any response.

⚠ **Step 5 (error handling)** — **Case A (typed error)** applies to CreateCustomer and CreateSubscription; **Case B (raw error)** applies to ListProducts, ReadCustomerByReference, and ListCustomerSubscriptions. A mismatch (catching the wrong case) silently drops the error. **A `JsonException` from a malformed 2xx response (missing required field) is NOT an `SdkException`** — the deserialization fails before the error object is built, so a boundary that catches only `SdkException<T>` lets it escape; **a 4xx/5xx response with a mismatched body shape throws `JsonException` WHILE constructing the error object, destroying the HTTP status — any boundary that maps `JsonException` → 500 will retry the unreplayable request.** **MUST load `dotnet-error-handling`** before writing `try/catch`.

⚠ **Step 6 (configuration & resilience)** — `HttpMethodsToRetry` gates only **status** triggers (e.g. 503); a **transport failure** (network down) retries on **every** verb including POST, so non-idempotent writes (like CreateSubscription) can execute more than once. **Lookup-then-create is idempotent; creation without lookup is not.** `Timeout` bounds **per-attempt**, not total call time. **MUST load `dotnet-configuration-resilience`** before tuning retries or timeouts.

⚠ **JsonException reach** — a drifted **2xx** body (missing `required` member) surfaces as `JsonException` from deserialization, **not** `SdkException`; a **non-2xx** body that doesn't match the operation's generated error shape throws `JsonException` *while the error object is being constructed*, so the HTTP status is lost — a boundary that maps every `JsonException` to 5xx will misclassify retriable errors and cause cascading failures. **MUST load `dotnet-error-handling`** and design the boundary to distinguish (2xx malformed) from (error deserialization failed) before mapping either to app status.

⚠ **Non-2xx JsonException** (error payload deserialization failure) — when a non-2xx response body doesn't match the operation's error shape, the SDK throws `JsonException` while *constructing* the typed error object, so the HTTP status (`StatusCode` on `RawError`) is destroyed. A caller that retries 5xx retries something that will always fail. The boundary must log the raw body and status before mapping to a safe app response. **MUST load `dotnet-error-handling`** for the mechanics of detecting and handling this case.

---

## REQUIRED READING

These companion skills are **mandatory** — load all before implementation starts. The sheet deliberately does not carry their contents; each governs the step where it is named.

| Skill | Governs | Reason |
|---|---|---|
| `maxio-sdk:dotnet-client-initialization` | Step 1: Client & DI setup | HttpClient lifecycle, transient vs. singleton, DI registration patterns |
| `maxio-sdk:dotnet-authentication` | Step 2: Credential wiring | How to set BasicAuth, load from config, rotate credentials |
| `maxio-sdk:dotnet-calling-endpoints` | Step 3: Operation calls | Parameter passing (positional vs. named), nullable-param handling, async/await, cancellation |
| `maxio-sdk:dotnet-models` | Step 4: Request/response building and reading | Record immutability, `required` fields, nullable fields, unions (factory + `TryGet…`), enums (static members vs. `FromValue`) |
| `maxio-sdk:dotnet-error-handling` | Step 5: Exception boundary | Case A vs. Case B, `TryGet…` accessors, RawError flow, JsonException from 2xx deserialization failure, JsonException from error-object construction (4xx/5xx shape mismatch) |
| `maxio-sdk:dotnet-configuration-resilience` | Step 6: Retry/timeout tuning, server/base-URL override | Retry scope (status vs. transport), per-attempt timeout semantics, retry floor (MaxRetries >= 1), BaseUrl override for dev hosts |
| `maxio-sdk:dotnet-testing` | Step 7 (if adding tests) | Stubbing via HttpClient, matching project test style |

---

## Assumptions & Blockers

**Assumptions:**
- The PublicApi project uses JWT for authentication; the logged-in user's unique ID is available as a claim and is suitable as Maxio customer `reference`.
- In-memory subscription mapping is acceptable (no persistent store); server restart clears the map. If a production user needs to recover subscriptions, they must be re-fetched from Maxio via ListCustomerSubscriptions.
- Payment collection method defaults to `automatic` (Maxio-configured default); the endpoints do not accept payment-method parameters. Callers must have payment profiles configured in Maxio or the subscription creation will fail.

**No blockers identified.** All contract facts come from the SDK map and are compile-verifiable.

