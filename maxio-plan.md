# Maxio Recurring Subscription Integration Plan — eShopOnWeb

## Scope & Sequence

1. **SDK Client Initialization & Configuration**
   - Register the Maxio SDK client via DI with credentials (API key, subdomain, environment)
   - Map configuration from env vars to `Maxio:` config section

2. **Fetch Subscription Plans**
   - Call `client.Products.ListProducts()` filtered by product family handle `eshop-subscribe`
   - Return plan details (name, handle, price, interval)

3. **Ensure Maxio Customer (Idempotent)**
   - Lookup customer by eShopOnWeb `userId` via `client.Customers.ReadCustomerByReference(reference)`
   - Create customer if not found via `client.Customers.CreateCustomer()`
   - Map and persist eShopOnWeb userId ↔ Maxio customerId

4. **Create Subscription**
   - Call `client.Subscriptions.CreateSubscription()` with plan handle and customerId
   - Confirm state, next-billing-date, and price in response

5. **List Subscriptions for User**
   - Query by Maxio customerId via `client.Customers.ListCustomerSubscriptions(customerId)`
   - Return subscription list with state and next-billing-date

6. **Error Handling Boundary**
   - Catch operation-specific `SdkException<{Operation}Error>` (Case A: typed) and `SdkException<RawError>` (Case B: generic)
   - Handle `JsonException` for malformed responses (drifted 2xx body, non-2xx deserialization failure)
   - Map to application-layer HTTP status codes

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operation Reference

| Step | Controller.Method | Signature | Request Model + Fields | Response Envelope & Inner Fields | Error Case | Pagination | Source |
|------|-------------------|-----------|------------------------|----------------------------------|------------|----------|--------|
| **1.1: Client Init** | `MaxioAdvancedBillingClient` | `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | **Options fields (all `required`):**<br>`Environment: ServerEnvironment.Us` or `.Eu`<br>`BasicAuth: BasicAuthCredentials { Username = "<api_key>", Password = "x" }`<br>`Server: ServerOptions` (optional for base-URL override)<br>`Retry: RetryOptions` (optional) | — | — | — | `sdk-map.md`; `MaxioAdvancedBillingClient.cs`, `MaxioAdvancedBillingClientOptions.cs` |
| **2.1: List Plans** | `client.Products.ListProducts()` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — all 8 nullable params (except `page`/`perPage`) **must pass explicitly**; pass `null` to skip. | **Optional query params:** filter by product family not available in signature — use list all, filter in-app by `ProductFamily.Handle` field. | `IReadOnlyList<ProductResponse>` — each element wraps `ProductResponse.Product: Product` containing:<br>`Id (id): int?`<br>`Name (name): string?`<br>`Handle (handle): string?` (wire: `api_handle`)<br>`PriceInCents (price_in_cents): long?`<br>`Interval (interval): int?`<br>`IntervalUnit (interval_unit): IntervalUnit?` (enum)<br>`ProductFamily (product_family): ProductFamily?` with `Handle (handle): string?`<br>**NB:** Response does NOT filter by product family; caller must check `Subscription.ProductFamily.Handle == "eshop-subscribe"`. | Case B: `SdkException<RawError>` — use `StatusCode`, `ReadAsString()` | Manual `page` + `perPage` | `operations/Products.md`, `records-3-Of-Su.md` |
| **3.1: Lookup Cust by Ref** | `client.Customers.ReadCustomerByReference(reference)` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | Query param: `reference` = eShopOnWeb userId (string) | `CustomerResponse` wraps `CustomerResponse.Customer: Customer` containing:<br>`Id (id): int?`<br>`FirstName (first_name): string?`<br>`LastName (last_name): string?`<br>`Email (email): string?`<br>`Reference (reference): string?`<br>`CreatedAt (created_at): DateTimeOffset?` | Case B: `SdkException<RawError>` (404 if not found) — use `StatusCode`, `ReadAsString()` | None | `operations/Customers.md`, `records-2-Cr-Ne.md` |
| **3.2: Create Cust** | `client.Customers.CreateCustomer(body)` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — **must pass explicitly** | `CreateCustomerRequest` wraps `Customer: CreateCustomer` (required) containing:<br>`FirstName (first_name): string !req`<br>`LastName (last_name): string !req`<br>`Email (email): string !req`<br>`Reference (reference): string?` ← eShopOnWeb userId (idempotency key)<br>`Address (address): string?`<br>`City (city): string?`<br>`State (state): string?`<br>`Zip (zip): string?`<br>`Country (country): string?`<br>`Phone (phone): string?` | `CustomerResponse` wraps `CustomerResponse.Customer: Customer` (same as 3.1 response) | Case A: `SdkException<CreateCustomerError>` with `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` for 422 | None | `operations/Customers.md`, `records-1-Ac-Cr.md` |
| **4.1: Create Sub** | `client.Subscriptions.CreateSubscription(body)` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — **must pass explicitly** | `CreateSubscriptionRequest` wraps `Subscription: CreateSubscription` (required) containing:<br>`CustomerId (customer_id): int?` ← from 3.1/3.2<br>`ProductHandle (product_handle): string?` or `ProductId (product_id): int?` ← plan handle or ID<br>`ProductPricePointHandle (product_price_point_handle): string?` (optional; defaults to product's default)<br>`Reference (reference): string?` (optional; subscription ref for idempotency/lookup)<br>**Scope:** No payment profile required per spec (payment_method_not_required) — omit `PaymentProfileId`, `CreditCardAttributes`, `BankAccountAttributes`<br>**Scope:** No trial, no setup fee — omit trial/initial-charge fields<br>**Scope:** Optional: `NextBillingAt (next_billing_at): DateTimeOffset?` to backdate billing | `SubscriptionResponse` wraps `SubscriptionResponse.Subscription: Subscription` containing:<br>`Id (id): int?`<br>`CustomerId (customer_id): int?`<br>`ProductId (product_id): int?`<br>`ProductHandle (product_handle): string?`<br>`State (state): SubscriptionState?` (enum: `active`, `trialing`, etc.)<br>`CurrentPeriodStartsAt (current_period_starts_at): DateTimeOffset?`<br>`CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`<br>`NextBillingAt (next_billing_at): DateTimeOffset?`<br>`CurrentBalanceInCents (current_balance_in_cents): long?`<br>`CreatedAt (created_at): DateTimeOffset?` | Case A: `SdkException<CreateSubscriptionError>` with `TryGetErrorListResponse1(out ErrorListResponse1)` for 422 | None | `operations/Subscriptions.md`, `records-2-Cr-Ne.md` |
| **5.1: List User Subs** | `client.Customers.ListCustomerSubscriptions(customerId)` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | Path param: customerId (int from 3.1/3.2) | `IReadOnlyList<SubscriptionResponse>` — each element wraps `SubscriptionResponse.Subscription: Subscription` (same fields as 4.1 response) | Case B: `SdkException<RawError>` — use `StatusCode`, `ReadAsString()` | None | `operations/Customers.md`, `records-2-Cr-Ne.md` |

### Enums

| Enum | Namespace | Values | Source |
|------|-----------|--------|--------|
| `SubscriptionState` | `MaxioAdvancedBilling.Models.Enums` | `Pending`, `FailedToCreate`, `Trialing`, `Assessing`, `Active`, `SoftFailure`, `PastDue`, `Suspended`, `Canceled`, `Expired`, `Paused`, `Unpaid`, `TrialEnded`, `OnHold`, `AwaitingSignup` (wire: `pending`, `failed_to_create`, `trialing`, …) | `enums.md` |
| `IntervalUnit` | `MaxioAdvancedBilling.Models.Enums` | `Day`, `Month` (wire: `day`, `month`) | `enums.md` |
| `ServerEnvironment` | `MaxioAdvancedBilling.Servers` | `Us`, `Eu` (wire: `US`, `EU`) | `sdk-map.md` |

### Client Construction & Auth

**Namespace imports required:**
```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Api;
using MaxioAdvancedBilling.Errors;
```

**Authentication:**
- Scheme: HTTP Basic
- Username = `MAXIO_API_KEY` (from env var / config `Maxio:ApiKey`)
- Password = literal string `"x"`

**Base URL / Environment:**
- Default: `https://{site}.chargify.com` (US) where `{site}` = `MAXIO_SITE_SUBDOMAIN` (from env var / config `Maxio:Subdomain`)
- Override: `options.Server.Production.Us.BaseUrl = "http://localhost:8080"` (for local/sandbox testing)
- EU: `options.Environment = ServerEnvironment.Eu` → `https://{site}.ebilling.maxio.com`

**HttpClient Lifetime:**
- Construct **once** per application (singleton), reuse via `IHttpClientFactory` or manual injection
- Do **not** create a new client per request

**Config Binding:**
Bind config section `Maxio:` with keys:
- `ApiKey` (required) ← `MAXIO_API_KEY`
- `Subdomain` (required) ← `MAXIO_SITE_SUBDOMAIN`
- `ProductFamilyHandle` (optional) ← `MAXIO_DEFAULT_PRODUCT_FAMILY`
- `BaseUrl` (optional) ← `MAXIO_BASE_URL` (for override)

---

## Trap Notes

⚠ **Step 1 (client setup)** — The SDK's retry/timeout options (`options.Retry`) do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register. **MUST load `dotnet-configuration-resilience`** to understand how retries and timeouts compose.

⚠ **Step 2–5 (all operations)** — Most read/list/find operations throw `SdkException<RawError>` (Case B); only mutation operations (`CreateCustomer`, `CreateSubscription`) throw typed `{Operation}Error` (Case A). The map row for each operation declares its case. **MUST load `dotnet-calling-endpoints`** to confirm which operations require named arguments and which accept positional.

⚠ **Step 3 (customer lookup)** — `ReadCustomerByReference` returns a **single** customer; if the reference is not found, it throws a 404 `SdkException<RawError>`. The exception does **not** indicate an error state for the business logic (customer does not yet exist is expected); wrap the call and handle the 404 as "proceed to create customer." **MUST load `dotnet-error-handling`** to distinguish between HTTP 404 (not found, expected) and other error statuses.

⚠ **Error Boundary: JsonException Handling** — Two directions require opposite handling:
1. A drifted or malformed **2xx body** (a missing required member) surfaces as `JsonException` from deserialization, **not** as `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
2. A **non-2xx body** that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ **Step 3.2 (create customer)** — `Reference` field is the idempotency key. If the same reference is sent twice with different customer data, the SDK does **not** deduplicate — it returns a 422 validation error. Always check `ReadCustomerByReference` first; if 404, proceed to create. **MUST load `dotnet-calling-endpoints`** to understand when a call is truly idempotent vs when it may fail on retry.

---

## REQUIRED READING

**Before implementation starts, load each of these companion skills:**

| Skill | Step It Governs |
|-------|-----------------|
| `dotnet-client-initialization` | 1: Client & DI setup (HttpClient lifetime, singleton vs transient, options binding) |
| `dotnet-authentication` | 1: Wiring credentials (Basic auth, API key rotation, per-environment config) |
| `dotnet-calling-endpoints` | 2–5: Operation signatures, named arguments, return types, async/await, cancellation |
| `dotnet-models` | 2–5: Request/response field names, wire names, unions (`TryGet…`), enums (`StringEnum<T>`) |
| `dotnet-error-handling` | 6: Catch hierarchy, typed vs raw errors, `TryGet…` accessors, `JsonException` boundary |
| `dotnet-configuration-resilience` | 1: Retry policy, timeout semantics, backoff, logging hooks |

---

## Assumptions & Blockers

**Assumptions:**
1. eShopOnWeb has a table or key-value store (or separate microservice) to persist the mapping: eShopOnWeb userId ↔ Maxio `Customer.Id`. The plan does not dictate where or how; the implementation owns that persistence layer.
2. The Maxio site `cp-exp-1` is already seeded with the four entities named in the scope (product family `eshop-subscribe`, plans `eshop-pro` and `basic-plan`, component `api-call`). No SDK call creates them.
3. Subscriptions are created for authenticated users only (JWT guard on POST `/api/subscriptions`). The eShopOnWeb identity layer determines `userId`.
4. "Payment method not required" means Maxio's product settings allow subscription creation without a stored card; the implementation does not pass payment profile data.
5. `NextBillingAt` in the response is populated by Maxio based on the product interval and current time; the implementation reads it, does not set it (unless explicitly backdating via `next_billing_at` in the request, which is optional per scope).

**Blockers:**
None identified.

---

## Notes on Response Envelopes

All operations on `Subscriptions` and `Customers` controllers wrap responses in a single-field envelope:
- `ProductResponse.Product: Product` (the actual data lives in `.Product`)
- `CustomerResponse.Customer: Customer` (the actual data lives in `.Customer`)
- `SubscriptionResponse.Subscription: Subscription` (the actual data lives in `.Subscription`)

Reads one level down when accessing fields; deserialization does the envelope unwrap automatically, but field access is explicit in code.

---

## Notes on Idempotency

- **Customers** — `Reference` field must be unique per site (eShopOnWeb userId). `ReadCustomerByReference(reference)` is the idempotency check; 404 means customer does not exist.
- **Subscriptions** — No built-in idempotency key beyond the combination of `CustomerId + ProductId + (ProductPricePointId or custom price)`. If the same customer is subscribed to the same plan twice, the second `CreateSubscription` succeeds and creates a duplicate subscription. The implementation must avoid this (e.g., query current subscriptions first, or use `Reference` to tag subscriptions for dedup).
