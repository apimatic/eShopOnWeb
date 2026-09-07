# Maxio Advanced Billing Integration — eShopOnWeb Subscription Billing

## Scope & Sequence

**Implementation steps** (in order):

1. **Client registration & DI** — instantiate `MaxioAdvancedBillingClient` with Basic auth (API key + "x")
2. **Fetch subscription plans** — `GET /api/subscription-plans` calls `ListProductsForProductFamily` by product family handle
3. **Ensure customer exists (idempotent)** — extract user reference from JWT, call `ReadCustomerByReference` (if 404, create via `CreateCustomer`)
4. **Create subscription** — `POST /api/subscriptions` calls `CreateSubscription` with customer and product
5. **List user subscriptions** — `GET /api/my-subscriptions` calls `ListSubscriptions` filtered by customer reference
6. **Error boundary** — map SDK exceptions to HTTP responses

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operations

| Step | Operation | Signature | Request Model + Fields | Response / Error Case | Pagination | Source |
|---|---|---|---|---|---|---|
| 1 | **Client registration** | `new MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `options`: `Environment` (ServerEnvironment), `BasicAuth` (BasicAuthCredentials: Username = API key, Password = "x") | — | — | `MaxioAdvancedBillingClient.cs` |
| 2 | **Subscriptions.ListProductsForProductFamily** | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | Path param: `productFamilyId` (string, required); Query params: all nullable, pass `null` to skip; page/perPage defaults: 1, 20 | `IReadOnlyList<ProductResponse>` envelope: `{ product: Product !req }` per item. Fields: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?` | **Case A**: `SdkException<ListProductsForProductFamilyError>` with `TryGetString(out string)` [404], `TryGetRawError(out RawError)` [fallback] | Manual: `page`, `perPage` | `operations/ProductFamilies.md` |
| 3a | **Customers.ReadCustomerByReference** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | Query param: `reference` (string, required — the eShopOnWeb user ID or email) | `CustomerResponse` envelope: `{ customer: Customer !req }`. Key fields: `Id (id): int?`, `FirstName (first_name): string?`, `Email (email): string?`, `Reference (reference): string?`, `CreatedAt (created_at): DateTimeOffset?` | **Case B**: `SdkException<RawError>` with `.StatusCode: HttpStatusCode`, `.ReadAsString(): string`, `.ReadAsJson<T>(): T?` [on 404 return false to trigger create] | — | `operations/Customers.md` |
| 3b | **Customers.CreateCustomer** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` | Request envelope: `CreateCustomerRequest { Subscription (subscription): CreateCustomer !req }`. Nested `CreateCustomer` fields (all optional, no `!req`): `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?`, `Organization (organization): string?`. **Required for idempotence:** pass `reference` = user's unique ID from JWT | `CustomerResponse` envelope: `{ customer: Customer !req }` same as 3a | **Case A**: `SdkException<CreateCustomerError>` with `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422], `TryGetRawError(out RawError)` [fallback] | — | `operations/Customers.md` |
| 4 | **Subscriptions.CreateSubscription** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | Request envelope: `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription !req }`. Nested `CreateSubscription` fields (all optional unless noted): `ProductHandle (product_handle): string?` or `ProductId (product_id): int?` (one required), `CustomerId (customer_id): int?` or `CustomerReference (customer_reference): string?` (one required), `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` (optional). **Note:** task specifies no payment method required; for sandbox test use `PaymentCollectionMethod.Invoice` | `SubscriptionResponse` envelope: `{ subscription: Subscription? }`. Key fields for response: `Id (id): int?`, `State (state): SubscriptionState?`, `ProductHandle (product_handle): string?`, `CustomerId (customer_id): int?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextBillingAt (next_billing_at): DateTimeOffset?` | **Case A**: `SdkException<CreateSubscriptionError>` with `TryGetErrorListResponse1(out ErrorListResponse1)` [422], `TryGetRawError(out RawError)` [fallback] | — | `operations/Subscriptions.md` |
| 5 | **Subscriptions.ListSubscriptions** | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | Query params: all before `page`/`perPage` are nullable (pass `null` to skip); `page` = 1, `perPage` = 20 defaults. **For user's subscriptions:** filter by customer reference via metadata or iterate all and filter client-side (no customer_reference query param on this endpoint) | `IReadOnlyList<SubscriptionResponse>` (same envelope as 4 per item) | **Case B**: `SdkException<RawError>` with `.StatusCode`, `.ReadAsString()`, `.ReadAsJson<T>()` | Manual: `page`, `perPage` | `operations/Subscriptions.md` |

---

### Enums

**SubscriptionState** — namespace `MaxioAdvancedBilling.Models.Enums`
| C# Member | Wire Value |
|---|---|
| `Pending` | `pending` |
| `Active` | `active` |
| `Trialing` | `trialing` |
| `PastDue` | `past_due` |
| `Canceled` | `canceled` |
| `Expired` | `expired` |
| `Paused` | `paused` |
| `Suspended` | `suspended` |
| `Unpaid` | `unpaid` |
| (others) | — |

**CollectionMethod** — namespace `MaxioAdvancedBilling.Models.Enums`
| C# Member | Wire Value |
|---|---|
| `Automatic` | `automatic` |
| `Invoice` | `invoice` |
| `Prepaid` | `prepaid` |
| `Remittance` | `remittance` |

**IntervalUnit** — namespace `MaxioAdvancedBilling.Models.Enums`
| C# Member | Wire Value |
|---|---|
| `Day` | `day` |
| `Month` | `month` |

---

### Client Construction & Auth

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;

// Bind from config (example, adjust to your config strategy)
var apiKey = configuration["Maxio:ApiKey"];
var subdomain = configuration["Maxio:Subdomain"];
var environment = ServerEnvironment.Us; // or .Eu

var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" },
    Environment = environment,
    // Optionally override base URL:
    // Server = new ServerOptions
    // {
    //     Production = new ProductionOptions
    //     {
    //         Us = new ServerConfig { Site = subdomain }
    //     }
    // }
};

var client = new MaxioAdvancedBillingClient(httpClient, options); // httpClient: long-lived, from IHttpClientFactory
```

**Namespace hints for `using` directives:**
- `MaxioAdvancedBilling` — client, options
- `MaxioAdvancedBilling.Models` — request/response records (`CreateCustomerRequest`, `CustomerResponse`, etc.)
- `MaxioAdvancedBilling.Models.Enums` — enums (`SubscriptionState`, `CollectionMethod`, etc.)
- `MaxioAdvancedBilling.Servers` — `ServerEnvironment`
- `MaxioAdvancedBilling.Core.Authentication.Basic` — `BasicAuthCredentials`
- `MaxioAdvancedBilling.Errors` — `CreateCustomerError`, `CreateSubscriptionError`, etc.

---

## Trap Notes

⚠ **Step 1 (client registration)** — The `HttpClient` you pass to the SDK must be **long-lived and reused** across requests, never created per call. Register it via `IHttpClientFactory` in DI. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ **Step 3a/3b (customer lookup/create)** — When `ReadCustomerByReference` returns 404 (Case B, status = `NotFound`), the catch handler receives `SdkException<RawError>`. Do **not** treat this as an error; it means the customer doesn't exist yet — proceed to create. Only if status is `Unauthorized` or `InternalServerError` should you surface the error to the caller. **MUST load `dotnet-error-handling`** for the Case A/B mechanics.

⚠ **Step 4 (subscription creation)** — A `CreateSubscriptionError` (Case A) on 422 exposes `TryGetErrorListResponse1`, which returns field-level errors as `ErrorListResponse1 { Errors: IReadOnlyList<string> }`. Parse these to provide the caller with specific validation feedback (e.g., "Product not found", "Customer reference invalid"). **MUST load `dotnet-error-handling`** before writing the catch block.

⚠ **Step 5 (list subscriptions)** — The `ListSubscriptions` endpoint has no `customer_reference` query filter. To retrieve only the signed-in user's subscriptions, **either** (a) filter client-side by the `CustomerId` field in the response against the known Maxio customer ID from step 3, **or** (b) pass customer ID as the `customerId` route if your endpoint design allows it. The task specifies `/api/my-subscriptions` — ensure the endpoint enforces that `my` means the JWT-authenticated user only. **MUST load `dotnet-error-handling`** for Case B.

⚠ **Subscription state & billing dates** — The `SubscriptionState` returned in `SubscriptionResponse.Subscription.State` is a `StringEnum<SubscriptionState>` (not a C# enum). Read its wire value via the `.Value` property or compare with static members (e.g., `SubscriptionState.Active`). The `NextBillingAt` field is a `DateTimeOffset?` — it may be `null` if the subscription is in certain states (e.g., `Canceled`). **MUST load `dotnet-models`** for union and enum handling.

⚠ **JsonException from mismatched response** — A 2xx body missing a required field (e.g., missing `subscription.id` in `SubscriptionResponse`) throws `JsonException` **from deserialization, not from the SDK's error handler** — it does **not** become an `SdkException`. Conversely, a non-2xx body that doesn't match the generated `{Operation}Error` shape throws `JsonException` *while constructing the error object*, **replacing** the `SdkException` and **destroying the HTTP status code**. An error boundary that catches only `SdkException` lets the 2xx-body one escape unhandled; one that maps `JsonException` → 5xx then retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing your boundary — both patterns appear in its worked examples.

---

## REQUIRED READING

The following companion skills must be loaded **before implementation starts**. The sheet deliberately does not carry their contents; each covers traps and details the one-line note above cannot hold.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 (HttpClient lifetime, DI registration) |
| `dotnet-authentication` | Step 1 (Basic auth: username = API key, password = "x") |
| `dotnet-calling-endpoints` | Steps 2–5 (calling operations, required params, async/await, named args) |
| `dotnet-models` | Steps 3–5 (records, enums, unions, constructing request bodies, reading response fields) |
| `dotnet-error-handling` | Steps 3–5 (Case A/B exceptions, `TryGet…` accessors, JsonException from 2xx/non-2xx bodies, boundary design) |
| `dotnet-configuration-resilience` | Step 1 (retries, timeouts per-attempt vs. total, no built-in logging) |
| `dotnet-testing` | Integration tests (stubbing HttpClient, matching SDK exception surface) |

---

## Assumptions & Blockers

### Assumptions

1. **Customer idempotence via reference** — The plan assumes eShopOnWeb has a stable user identifier (e.g., user ID, email) that can be passed as the Maxio customer `reference` field. This is **required** to ensure `ReadCustomerByReference` deterministically finds or reports missing the same customer on each call. If the reference changes per request (e.g., random UUID) idempotence breaks.

2. **No payment method required at signup** — The task states "no payment method required" for sandbox plans. The implementation must **not** require `PaymentProfileAttributes` or `PaymentProfileId` on `CreateSubscriptionRequest`. If the Maxio site *does* require a payment method (site-level configuration), the plan will fail at runtime; the implementer must confirm site settings allow payment-optional subscriptions before deployment.

3. **No custom pricing** — The plan assumes plans are already configured in Maxio with fixed pricing. If custom per-customer pricing is needed later, `SubscriptionCustomPrice` on `CreateSubscription` must be added (union-based, see `dotnet-models`).

4. **JWT user extraction is implementer's concern** — The plan does not specify how the JWT is parsed or which field becomes the customer reference. The endpoint must extract the authenticated user's identity and pass it consistently.

### Blockers

None identified. All required operations exist on the map and the SDK surface is stable for this scope.

---

**Plan date:** 2026-09-07 | **Map version:** `v1.0.2` (commit `15db14b`)
