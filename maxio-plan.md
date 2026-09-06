# Maxio Advanced Billing Integration Plan — eShopOnWeb Subscription Capability

## Scope & Sequence

1. **Client initialization & DI setup** — `MaxioAdvancedBillingClient` with HTTP Basic auth and site configuration
2. **Fetch available subscription plans** — List plans by product family handle, cache for GET /api/subscription-plans
3. **Idempotent customer lookup** — Search by email or create new customer before any subscription operation
4. **Create subscription** — Attach customer to a plan via POST /api/subscriptions
5. **Fetch user's subscriptions** — List subscriptions for authenticated user via GET /api/my-subscriptions
6. **Fetch subscription details** — Retrieve single subscription by ID (internal, no direct endpoint)

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operation Contract Table

| Operation | Signature | Request Model | Response Envelope | Error Case | Pagination | Source |
|---|---|---|---|---|---|---|
| **List Plans by Product Family** | `client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | Pass product family handle (e.g., `"eshop-subscribe"`) as `productFamilyId`; all other parameters null to skip, defaults: `page`=1, `perPage`=20 | `IReadOnlyList<ProductResponse>` — each element is `ProductResponse` which has exactly one field `Product: Product !req` (wire name `product`). Read from `.Product` for the product object. | **Case A (typed)**: `SdkException<ListProductsForProductFamilyError>` with `TryGetString(out string)` [404] for not found, `TryGetRawError(out RawError)` [fallback]. | Manual: `page`+`perPage` query params | `map/operations/ProductFamilies.md` |
| **Get Plan by Handle** | `client.Products.ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | `apiHandle`: the plan handle (e.g., `"eshop-pro"`, `"basic-plan"`) | `ProductResponse` with field `Product: Product !req` (wire name `product`). Read from `.Product`. | **Case B (raw)**: `SdkException<RawError>` with `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | None | `map/operations/Products.md` |
| **Create or Lookup Customer (Idempotent)** | `client.Customers.ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` (to find), then `client.Customers.CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` (if not found) | **Search**: pass `q: user_email` (use query param name-binding), all others null, defaults `page`=1, `perPage`=50. **Create**: wrap `CreateCustomerRequest` with `Subscription: CreateCustomer !req` field (wire name `customer`). The `CreateCustomer` record has fields: `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?` — **Required fields per Notes**: `Email` (uniqueness constraint); set `Reference` to user ID from your app for future idempotency via `ReadCustomerByReference`. | **Search**: `IReadOnlyList<CustomerResponse>` with field `Customer: Customer !req`. Read from `.Customer` for each result. **Create**: `CustomerResponse` with field `Customer: Customer !req` (wire name `customer`). Read from `.Customer`. Returned `Customer` has `Id: int?`, `Email: string?`, `Reference: string?`. | **Search Case B**: `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. **Create Case A**: `SdkException<CreateCustomerError>` with `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422], `TryGetRawError(out RawError)` [fallback]. | Search: manual `page`+`perPage` | `map/operations/Customers.md` |
| **Create Subscription** | `client.Subscriptions.CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | Wrap request in `CreateSubscriptionRequest` with `Subscription: CreateSubscription !req` field (wire name `subscription`). The `CreateSubscription` record fields: `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `CustomerId (customer_id): int?`, `Reference (reference): string?`, `PaymentProfileId (payment_profile_id): int?` (null when no payment required per spec). **Required per Notes**: `ProductHandle` or `ProductId`; `CustomerId` or `CustomerAttributes` (use `CustomerId` for existing customer); omit payment profile per spec ("payment method NOT required"). Pass `null` for all nullable params you don't set. | `SubscriptionResponse` with field `Subscription: Subscription?` (wire name `subscription`). Read from `.Subscription` for result. Contains `Id: int?`, `CustomerId: int?`, `ProductId: int?`, `State: SubscriptionState?`, `CurrentPeriodStartsAt: DateTimeOffset?`, `NextBillingAt: DateTimeOffset?`. | **Case A (typed)**: `SdkException<CreateSubscriptionError>` with `TryGetErrorListResponse1(out ErrorListResponse1)` [422], `TryGetRawError(out RawError)` [fallback]. | None | `map/operations/Subscriptions.md` |
| **List Subscriptions for Customer** | `client.Customers.ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | `customerId`: the Maxio customer ID (from the `Customer` object returned by create/lookup) | `IReadOnlyList<SubscriptionResponse>` — each has field `Subscription: Subscription?`. Read from `.Subscription` for each result. | **Case B (raw)**: `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | None | `map/operations/Customers.md` |
| **Get Subscription Details** | `client.Subscriptions.ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` | `subscriptionId`: the subscription ID. Pass `null` for `include`. | `SubscriptionResponse` with field `Subscription: Subscription?` (wire name `subscription`). Read from `.Subscription`. | **Case B (raw)**: `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | None | `map/operations/Subscriptions.md` |

### Enums Used

| Enum | Namespace | Values/Notes | Source |
|---|---|---|---|
| `SubscriptionState` | `MaxioAdvancedBilling.Models.Enums` | Returned by subscription read operations; wire values: `active`, `assessing`, `past_due`, `suspended`, `canceled`, `expired`, `trialing`, `awaiting_signup`, `pending_cancellation`, `signed`, `unpaid` (consult `map/models/enums.md` for full list and exact member names) | `map/models/enums.md` |

### Error Accessors & Payloads

**ListProductsForProductFamily** (Case A):
- `TryGetString(out string error)` → returns `true` if status is 404; `error` contains plain string message

**CreateCustomer** (Case A):
- `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` → 422; payload has `Errors: Errors?` field (wire name `errors`)
  - `Errors` record: `PerPage: IReadOnlyList<string>?`, `PricePoint: IReadOnlyList<string>?`

**CreateSubscription** (Case A):
- `TryGetErrorListResponse1(out ErrorListResponse1)` → 422; payload has `Errors: IReadOnlyList<string> !req` field (wire name `errors`)

### Client Initialization & Configuration

**Constructor** (from `maxio-plan.md` instruction set):
```csharp
var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials 
    { 
        Username = apiKey,      // from Maxio:ApiKey config
        Password = "x"          // literal string
    },
    Environment = ServerEnvironment.Us,
    Server = new ServerOptions 
    { 
        Production = new ProductionOptions 
        { 
            Us = new ServerUrlTemplateOptions 
            { 
                Site = subdomain  // from Maxio:Subdomain config
            } 
        } 
    }
};
var client = new MaxioAdvancedBillingClient(httpClient, options);
```

**Namespaces to import:**
- `MaxioAdvancedBilling` — client, `ServerEnvironment`
- `MaxioAdvancedBilling.Core.Authentication.Basic` — `BasicAuthCredentials`
- `MaxioAdvancedBilling.Servers` — `ServerEnvironment`, `ServerOptions`, `ProductionOptions`, `ServerUrlTemplateOptions`
- `MaxioAdvancedBilling.Api` — controller accessors (e.g. `client.Subscriptions`)
- `MaxioAdvancedBilling.Models` — request/response records (`CreateSubscriptionRequest`, `CreateSubscription`, `ProductResponse`, `CustomerResponse`, `SubscriptionResponse`, etc.)
- `MaxioAdvancedBilling.Models.Enums` — enums like `SubscriptionState`
- `MaxioAdvancedBilling.Errors` — error types (`CreateCustomerError`, `CreateSubscriptionError`, etc.)
- `MaxioAdvancedBilling.Core.Exceptions` — `SdkException<T>`

---

## Trap Notes

⚠ **Step 1 (Client initialization)** — the SDK's `RetryOptions` (gating `Timeout`, `HttpMethodsToRetry`, backoff) are **not** the same as the `HttpClient`'s `Timeout` and are **not** request-body-validation timeouts. Per spec, payment is NOT required; the SDK will NOT demand it at client construction. **MUST load `dotnet-client-initialization`** before wiring the client and `IHttpClientFactory`.

⚠ **Step 2 (Calling endpoint)** — named arguments are mandatory for many optional params; see the operation table's `Signature` column. The response envelope wraps the payload: every operation returns a `…Response` record with exactly one field (e.g. `ProductResponse.Product`, `SubscriptionResponse.Subscription`); extract one level down. **MUST load `dotnet-calling-endpoints`** before the first operation call.

⚠ **Step 3 (Customer idempotency)** — `ListCustomers` searches via the `q` query parameter (pass `email` as the query); no type-safe overload guards against forgetting the binding. The operation Notes say you may only create one customer per unique `reference` (or unique email by convention); set `reference` to your app's user ID to enable `ReadCustomerByReference` for true idempotency across runs. **MUST load `dotnet-calling-endpoints`** for query-parameter binding details.

⚠ **Step 4 (Subscription creation)** — the Notes say payment information is **optional** if the product does not require it (your spec says it does not). The `CreateSubscription` record has no `required` marker on payment fields — all are optional; omit them. The SDK will **not** validate the presence/absence at construction — validation happens on the wire, surfacing as a 422 error (Case A payload). **MUST load `dotnet-models`** to understand when a field's absence is valid.

⚠ **Step 5 (Error boundary)** — this SDK generates **no `Result<T>`** variants; every operation is throw-only. Separate `try/catch` blocks by error case (Case A typed vs Case B raw) as shown in the `sdk-map.md` example. Two directions to `JsonException`:
- A drifted or malformed **2xx** body (missing `required` member) surfaces as `JsonException` from deserialization, **not** as `SdkException` — an SDK-exception-only catch ladder lets it escape the boundary;
- A **non-2xx** body that does not match the operation's generated `{Operation}Error` shape throws `JsonException` *while constructing the error object*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it.

**MUST load `dotnet-error-handling`** before writing the error boundary. Map every `JsonException` defensively (e.g., extract best-effort, fall back to the generic message), and never map `JsonException` to a 5xx whose caller retries — a retried 5xx can never succeed if the payload shape was the real issue.

⚠ **Step 6 (Configuration & resilience)** — `Timeout` is per-attempt, not total; `HttpMethodsToRetry` gates only the **status** trigger, so a `503` on a `POST` is not resent, but a **transport failure** (`HttpRequestException`) is retried on **every** verb, including `POST`, so a non-idempotent write can execute more than once. There is no setting to disable retries (`MaxRetries` has a floor of 1). **MUST load `dotnet-configuration-resilience`** before tuning retry behavior.

---

## REQUIRED READING

Load these companion skills **before implementation starts**. The sheet deliberately omits their contents — each skill carries worked examples, defaults, and gotchas the sheet cannot.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Client construction, DI registration, `HttpClient` lifecycle |
| `dotnet-authentication` | Basic auth wiring, credential rotation, per-environment config |
| `dotnet-calling-endpoints` | Operation invocation, parameter binding, return type extraction |
| `dotnet-models` | Request/response record shape, nullable/required flags, unions, enums |
| `dotnet-error-handling` | Try/catch structure, typed vs raw error cases, `JsonException` defense |
| `dotnet-configuration-resilience` | Retry options, timeout semantics, per-attempt backoff, logging hooks |
| `dotnet-testing` | Mocking the `HttpClient`, test fixtures, assertion patterns |

---

## Assumptions & Blockers

| Assumption | Rationale |
|---|---|
| eShopOnWeb's user identity (JWT subject) maps to Maxio customer via email or your own `reference` field | The plan uses email search as the primary lookup path and `reference` (user ID) for true idempotency. No other user-to-customer binding strategy is documented in the spec. |
| Configuration keys `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle` are bound via ASP.NET configuration (user-secrets, env vars, or appsettings.json) and injected into the integration layer | The spec names binding keys; the plan does not invent defaults or assume a hardcoded string. |
| The "no payment method required" spec means the `PaymentProfileId` (and all payment attributes) are omitted from `CreateSubscriptionRequest` when sent to the wire — the API will reject 422 if the product actually requires payment | No special SDK affordance bypasses validation; the error boundary must handle 422 with typed error payload and surface the reason to the caller. |
| The caller (PublicApi endpoints) is responsible for extracting the user's identity from the JWT and passing it to the integration layer | The plan does not JWT-decode or identity-extract; that is the application layer's contract. |

| Blocker | Status |
|---|---|
| *None* | Plan is complete; no SDK gaps or unresolved provider behavior block execution. |

---

## Configuration Defaults (from map)

- **Base URL** (Production, US): `https://{site}.chargify.com` — site is `Maxio:Subdomain` binding key
- **Retry floor**: `MaxRetries = 1` (cannot be disabled)
- **HTTP timeout**: per-attempt, not total (set via `options.Retry.Timeout`)
- **Environments**: `ServerEnvironment.Us` (default), `ServerEnvironment.Eu` (if your account requested EU hosting)

---

**Generated from SDK map v1.0.2 (commit 15db14b2e663ebe9e957e061bd67634630429035)**
