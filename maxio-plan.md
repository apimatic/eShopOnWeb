# eShopOnWeb Recurring Subscription Billing — Maxio Integration Plan

## Scope & Sequence

**Hero flow:** Logged-in shopper browses available plans, subscribes to one, system ensures Maxio customer exists (idempotent by email), enrolls them in chosen plan, confirms plan/price/state/next-billing-date.

**Endpoints to implement (src/PublicApi):**
1. GET /api/subscription-plans — list available plans (sand seeded: Pro eshop-pro $299/mo, Basic basic-plan $29/mo) in product family `eshop-subscribe`
2. POST /api/subscriptions — create subscription (idempotent on user email); customer auto-created or fetched; metered component `api-call` optional
3. GET /api/my-subscriptions — user's current subscriptions

**Config keys (read from environment, no secrets in repo):**
- `Maxio:ApiKey` ← MAXIO_API_KEY
- `Maxio:Subdomain` ← MAXIO_SITE_SUBDOMAIN
- `Maxio:ProductFamilyHandle` ← MAXIO_DEFAULT_PRODUCT_FAMILY (default: `eshop-subscribe`)
- `Maxio:BaseUrl` (optional override, e.g. for local mock)

**Credentials scope:** Sandbox only, HTTP Basic auth (username = API key, password = literal `"x"`), no payment profile required, no trial/setup fee, non-taxable.

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

| Step | Operation | Signature | Request | Response | Error | Pagination | Source |
|---|---|---|---|---|---|---|---|
| 1. List subscription plans | `ListProductsForProductFamily` | `client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, MaxioAdvancedBilling.Models.ListProductsFilter? filter, System.DateTimeOffset? startDate, System.DateTimeOffset? endDate, System.DateTimeOffset? startDatetime, System.DateTimeOffset? endDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, System.Threading.CancellationToken ct = default)` | `productFamilyId` = product family handle (from config: `eshop-subscribe`), all other params `null` except `page=1, perPage=20` | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>` — list of products with `Product (product): MaxioAdvancedBilling.Models.Product` envelope; read product fields: `Handle (product_handle): string?`, `Name (name): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): MaxioAdvancedBilling.Models.Enums.IntervalUnit?`, `Id (id): int?` | **Case A**: `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>` with `TryGetString(out string)` [404] accessor + `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` fallback | Manual `page`+`perPage` | `map/operations/ProductFamilies.md` |
| 2a. Fetch customer by email (idempotent) | `ReadCustomerByReference` | `client.Customers.ReadCustomerByReference(string reference, System.Threading.CancellationToken ct = default)` | `reference` = user email | `MaxioAdvancedBilling.Models.CustomerResponse` with `Customer (customer): MaxioAdvancedBilling.Models.Customer !req` envelope; read: `Id (id): int?`, `Email (email): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?` | **Case B**: `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` with `StatusCode: System.Net.HttpStatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | None | `map/operations/Customers.md` |
| 2b. Create customer (if not found) | `CreateCustomer` | `client.Customers.CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, System.Threading.CancellationToken ct = default)` | `CreateCustomerRequest` wrapping `CreateCustomer`: `Customer (customer): MaxioAdvancedBilling.Models.CreateCustomer !req`. Required fields in `CreateCustomer`: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`. Optional: `Reference (reference): string?` (store user ID here for reference). All others optional, all nullable. | `MaxioAdvancedBilling.Models.CustomerResponse` with `Customer (customer): MaxioAdvancedBilling.Models.Customer !req`; read same fields as step 2a + `CreatedAt (created_at): System.DateTimeOffset?` | **Case A**: `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>` with `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422] + `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` fallback | None | `map/operations/Customers.md` |
| 3. Create subscription | `CreateSubscription` | `client.Subscriptions.CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, System.Threading.CancellationToken ct = default)` | `CreateSubscriptionRequest` wrapping `CreateSubscription`: `Subscription (subscription): MaxioAdvancedBilling.Models.CreateSubscription !req`. Required (must pass): `CustomerId (customer_id): int?` or `CustomerReference (customer_reference): string?` (use email); `ProductHandle (product_handle): string?` or `ProductId (product_id): int?` (use handle from step 1: `eshop-pro` or `basic-plan`). Optional component: `Components (components): IReadOnlyList<MaxioAdvancedBilling.Models.CreateSubscriptionComponent>?` — array of `ComponentId (component_id): MaxioAdvancedBilling.Models.OneOf.ComponentId1 !` (union, use handle `api-call`) + optional `Quantity (quantity): int?`. Trial: none. Setup fee: none. Taxable: `false` (all `null` or default). | `MaxioAdvancedBilling.Models.SubscriptionResponse` with `Subscription (subscription): MaxioAdvancedBilling.Models.Subscription?` envelope; read: `Id (id): int?`, `State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `NextAssessmentAt (next_assessment_at): System.DateTimeOffset?`, `ActivatedAt (activated_at): System.DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at): System.DateTimeOffset?` | **Case A**: `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>` with `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422] + `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` fallback. Payload: `Errors (errors): IReadOnlyList<string> !req` (error messages). On 422: subscription may already exist for this customer+product; treat as idempotent: fetch and return existing if found. | None | `map/operations/Subscriptions.md` |
| 4. List user subscriptions | `ListCustomerSubscriptions` | `client.Customers.ListCustomerSubscriptions(int customerId, System.Threading.CancellationToken ct = default)` | `customerId` = Maxio customer ID from step 2a/2b | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>` — array of subscriptions (same envelope + model as step 3) | **Case B**: `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` with `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | None | `map/operations/Customers.md` |

### Enum Values

**`MaxioAdvancedBilling.Models.Enums.IntervalUnit`** (wire_name → CSharp member):
- `day` → `Day`
- `month` → `Month`

**`MaxioAdvancedBilling.Models.Enums.SubscriptionState`** (wire_name → CSharp member):
- `active` → `Active` (normal, paid, up to date)
- `trialing` → `Trialing` (trial period active)
- `pending` → `Pending` (awaiting signup; not used here — no trial)
- `past_due` → `PastDue` (payment failure)
- `canceled` → `Canceled` (canceled by user or dunning)
- `failed_to_create` → `FailedToCreate` (signup failed)
- (others as documented; use `Active` as the success target)

**`MaxioAdvancedBilling.Models.Enums.ListProductsInclude`** (optional, can pass `null`):
- `prepaid_product_price_point` → `PrepaidProductPricePoint` (not needed here)

**`MaxioAdvancedBilling.Models.Enums.BasicDateField`** (optional, can pass `null`):
- `created_at` → `CreatedAt`
- `updated_at` → `UpdatedAt`

### Client Construction & Auth

**Namespace:** `MaxioAdvancedBilling`, `MaxioAdvancedBilling.Core.Authentication.Basic`, `MaxioAdvancedBilling.Servers`

```csharp
var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials 
    { 
        Username = "<API_KEY_from_Maxio:ApiKey_config>", 
        Password = "x"   // literal string "x"
    },
    Environment = ServerEnvironment.Us,  // default; or .Eu if account hosted in EU
    Server = new MaxioAdvancedBilling.Core.ServerConfiguration.ServerOptions
    {
        Production = new MaxioAdvancedBilling.Servers.ProductionOptions
        {
            Us = new MaxioAdvancedBilling.Servers.ServerOptions
            {
                Site = "<Maxio:Subdomain_from_config>"  // e.g., "your-subdomain"
                // BaseUrl can override to http://localhost:... for local mock
            }
        }
    }
};
var client = new MaxioAdvancedBillingClient(httpClient, options);
```

**Endpoints accessed:**
- `client.ProductFamilies` → `GET /product_families/{product_family_handle}/products.json`
- `client.Customers` → `GET /customers/lookup.json?reference=...` (ReadCustomerByReference), `POST /customers.json` (CreateCustomer), `GET /customers/{id}/subscriptions.json` (ListCustomerSubscriptions)
- `client.Subscriptions` → `POST /subscriptions.json` (CreateSubscription)

---

## Trap Notes

- ⚠ **Step 0 (client registration)** — The SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register. The `Timeout` option is per-attempt, and HTTP transports (including `POST` writes) are retried even when `HttpMethodsToRetry` does not include `POST`. Idempotent create operations may execute more than once on transport failure. **MUST load `dotnet-configuration-resilience`** before wiring the client.

- ⚠ **Step 2a/2b (customer lookup/create)** — `ReadCustomerByReference` with a non-existent email returns 404 (Case B error), not a successful response with null; wrap in try/catch and treat 404 as "customer not found". No exception type distinguishes 404 from other failures in Case B; parse `StatusCode` manually if needed. **MUST load `dotnet-error-handling`** for the proper catch ladder shape.

- ⚠ **Step 3 (subscription create)** — The endpoint accepts `customer_reference` (email) *and* `customer_id` (Maxio ID); if both are provided, behavior is undefined — provide only one. On 422 (validation error), the response body is `ErrorListResponse1` with `Errors (errors): IReadOnlyList<string>` (plain error strings, not keyed); extract the messages and decide whether the error is recoverable (e.g., "subscription already exists for this customer" → return existing) or permanent (bad plan handle → fail). Check error text; no structured error subtype differentiates the two. **MUST load `dotnet-error-handling`** for the boundary.

- ⚠ **Step 3 (idempotent subscription)** — When a customer subscribes to the same product twice, Maxio's 422 response does not provide a subscription ID. To make the call idempotent: on 422, call `ListCustomerSubscriptions` to find the existing subscription; return it. This adds a second call on conflict, but ensures idempotency.

- ⚠ **Step 4 (list subscriptions)** — Returns all subscriptions for a customer, including canceled and expired ones. Filter in-memory by `State` if you need only active subscriptions (e.g., `SubscriptionState.Active`).

- ⚠ **Deserialization boundary (all responses)** — A drifted or malformed **2xx** body (e.g., missing required `subscription` field in `SubscriptionResponse`) surfaces as `System.Text.Json.JsonException` from deserialization, **not** as `SdkException` — the `JsonException` is thrown *outside* the exception-handling scope of the operation, bypassing `SdkException<T>` catch blocks. A boundary that only catches `SdkException<T>` lets the `JsonException` escape; it must catch `JsonException` separately and log or return a deterministic error. **MUST load `dotnet-error-handling`** for both error cases (typed SdkException AND JsonException).

- ⚠ **Non-2xx deserialization (error boundary)** — A **non-2xx** body that does not match its operation's generated error shape (e.g., `CreateSubscriptionError` or `RawError`) throws `JsonException` *while the error object itself is being constructed*, **replacing** the `SdkException` and **destroying** the HTTP status with it. A boundary that maps every `JsonException` to a 5xx then instructs the caller to retry 5xx will retry something that can never succeed (provider bug or wire corruption, not transient). The boundary must distinguish: if `JsonException` occurs *after* a non-2xx status code, map to 5xx and log the mismatch; if *no* status code was seen (transport failure), retry the transport. **MUST load `dotnet-error-handling`** before writing the integration boundary.

---

## REQUIRED READING

Load these companion skills **before implementation starts**. The sheet deliberately does not carry their contents; the skills hold defaults, worked examples, and the parts a one-line note cannot.

| Skill | Step(s) | Purpose |
|---|---|---|
| `dotnet-client-initialization` | 0 | Client construction, DI registration, `HttpClient` setup |
| `dotnet-authentication` | 0 | Basic auth (username = API key, password = `"x"`), credential rotation |
| `dotnet-configuration-resilience` | 0, 3 | Retry options (transport vs status trigger), timeout per-attempt, base-URL override, logging hooks |
| `dotnet-calling-endpoints` | 1–4 | Named arguments, optional param binding, async usage, cancellation |
| `dotnet-models` | 1–4 | Request/response envelopes, unions (factories + `TryGet…`), enums (`StringEnum<T>`, `FromValue()`), unmodeled fields dropped |
| `dotnet-error-handling` | 1–4 | Case A (typed `{Operation}Error` + `TryGet…`) vs Case B (`RawError`), `JsonException` distinction, catch-ladder order, non-throw variants absent |
| `dotnet-testing` | (later) | `HttpClient` seam for test mocks, assertion patterns |

---

## Assumptions & Blockers

**Assumptions:**
- eShopOnWeb user email is globally unique within the Maxio sandbox site; customer `reference` field will store the eShopOnWeb user ID for future reference lookups.
- Sandbox sandbox seeded with product family handle `eshop-subscribe`, products with handles `eshop-pro` and `basic-plan`, metered component with handle `api-call` (present but optional on initial subscription).
- JWT claims in the endpoint context carry authenticated user identity (sub/email); endpoint layer extracts user email and ID before calling Maxio integration.
- No payment profile is required for subscription creation (Maxio sandbox allows `null` payment method); production would need payment handling.

**Blockers:**
- None identified. All required operations are present in the map. Idempotence on subscription create (on 422) is handled via secondary lookup (ListCustomerSubscriptions), not a built-in operation.

