# Maxio Advanced Billing Integration Plan — eShopOnWeb Subscriptions

## Scope & Sequence

The integration adds a parallel subscription-billing capability to eShopOnWeb via Maxio Advanced Billing, without replacing the existing cart/checkout. Three PublicApi endpoints expose the capability; all are JWT-authenticated. The hero flow is: logged-in user browses plans, selects one, and sees the subscription in their account immediately.

### Implementation Steps (in order):

1. **Client setup & DI** — register Maxio client with HttpClient; wire Basic auth (API key + literal `"x"`).
   - Operations: none (configuration only).

2. **Customer sync** — ensure a Maxio customer exists for the eShopOnWeb user (idempotent, using user ID as reference).
   - Operations: `ReadCustomerByReference`, `CreateCustomer`.

3. **Plans endpoint** — list available products (plans) from the configured product family.
   - Operations: `ListProducts`.

4. **Subscription endpoint** — create a subscription for the logged-in user to a selected plan.
   - Operations: `CreateSubscription`.

5. **My subscriptions endpoint** — retrieve the user's active/past subscriptions.
   - Operations: `ListCustomerSubscriptions`.

6. **Error boundary** — wrap all SDK calls in a unified error handler that maps SDK exceptions to application responses.

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Step 2: ReadCustomerByReference

| Field | Value |
|---|---|
| **Controller property** | `client.Customers` |
| **HTTP method** | `GET` |
| **Operation** | `ReadCustomerByReference` |
| **Signature** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` |
| **Parameters** | `reference` — the user's unique eShopOnWeb ID (passed as query param `reference`). Must not be null. |
| **Request body** | (none) |
| **Response envelope** | `CustomerResponse` (namespace: `MaxioAdvancedBilling.Models`) — single required field `Customer (customer): Customer !req` (namespace: `MaxioAdvancedBilling.Models`) |
| **Response fields to read** | `Customer.Id` (int), `Customer.Reference` (string), `Customer.Email` (string) |
| **Error case** | **Case B** — `SdkException<RawError>` (namespace: `MaxioAdvancedBilling.Core.ErrorResponse`). Status code must be checked: 404 = not found (idempotent, safe to create); other statuses are transient/auth errors. |
| **Pagination** | none |
| **Source** | `operations/Customers.md` |

### Step 2: CreateCustomer

| Field | Value |
|---|---|
| **Controller property** | `client.Customers` |
| **HTTP method** | `POST` |
| **Operation** | `CreateCustomer` |
| **Signature** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` |
| **Parameters** | `body` — nullable, **must pass explicitly** (pass `null` to skip, but the map states body is required for this operation; always pass a request). |
| **Request model** | `CreateCustomerRequest` (namespace: `MaxioAdvancedBilling.Models`) — single required field `Subscription (subscription): CreateSubscription !req`. |
| **Request fields to set** | Within `CreateCustomerRequest.Subscription` (type: `CreateCustomer`, namespace: `MaxioAdvancedBilling.Models`), set: `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?`. All are optional, but `Reference` and `Email` are recommended; `Reference` ties to eShopOnWeb user ID and enables idempotent lookups. Field wire names shown in parentheses. |
| **Response envelope** | `CustomerResponse` (namespace: `MaxioAdvancedBilling.Models`) — single required field `Customer (customer): Customer !req` (namespace: `MaxioAdvancedBilling.Models`) |
| **Response fields to read** | `Customer.Id` (int) — the Maxio customer ID; store for subscription creation. |
| **Error case** | **Case A** — `SdkException<CreateCustomerError>` (namespace: `MaxioAdvancedBilling.Errors`). Error accessors: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. |
| **Pagination** | none |
| **Source** | `operations/Customers.md` |

### Step 3: ListProducts

| Field | Value |
|---|---|
| **Controller property** | `client.Products` |
| **HTTP method** | `GET` |
| **Operation** | `ListProducts` |
| **Signature** | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| **Parameters** | All params nullable except `page` and `perPage` (defaults: `page` = 1, `perPage` = 20). To list a product family's products, pass `filter` = `new ListProductsFilter { Ids = [<product_id>, ...] }` (or `null` to fetch all products). For eShopOnWeb, query the configured product family ID from config (`Maxio:ProductFamilyHandle` or by lookup on first sync) and filter by product IDs 7126957 (Pro) and 7126958 (Basic). Pass `null` for all other params. |
| **Request body** | (none) |
| **Response envelope** | `IReadOnlyList<ProductResponse>` (namespace: `MaxioAdvancedBilling.Models`) — each element is `ProductResponse` with required field `Product (product): Product !req`. |
| **Response fields to read** | Per product: `Product.Id` (int), `Product.Name` (string), `Product.Handle` (string), `Product.PriceInCents` (long) — convert to decimal by dividing by 100. Map to UI: ID, name, price. |
| **Error case** | **Case B** — `SdkException<RawError>` (namespace: `MaxioAdvancedBilling.Core.ErrorResponse`). Status code: 401 = auth; 5xx = transient. |
| **Pagination** | manual `page` + `perPage` — response is a list; caller must paginate if needed. For eShopOnWeb MVP, fetch one page (default 20 products). |
| **Source** | `operations/Products.md` |

### Step 4: CreateSubscription

| Field | Value |
|---|---|
| **Controller property** | `client.Subscriptions` |
| **HTTP method** | `POST` |
| **Operation** | `CreateSubscription` |
| **Signature** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` |
| **Parameters** | `body` — nullable, **must pass explicitly**. |
| **Request model** | `CreateSubscriptionRequest` (namespace: `MaxioAdvancedBilling.Models`) — single required field `Subscription (subscription): CreateSubscription !req`. |
| **Request fields to set** | Within `CreateSubscriptionRequest.Subscription` (type: `CreateSubscription`, namespace: `MaxioAdvancedBilling.Models`), **minimum required**: `CustomerId (customer_id): int?` (Maxio customer ID from step 2), `ProductId (product_id): int?` (product ID from step 3). **Recommended/configured**: `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` (enum, namespace: `MaxioAdvancedBilling.Models.Enums`; set to `CollectionMethod.Automatic` for credit-card auto-billing, or `CollectionMethod.Invoice` for manual invoice-based per the eShopOnWeb policy). **Optional but available**: `Reference (reference): string?` (maps to eShopOnWeb order/subscription reference), `NextBillingAt (next_billing_at): DateTimeOffset?`. **Task requirement**: per spec, no trial, no setup fee, no payment method required — so omit `TrialPriceInCents`, `InitialChargeInCents`, `PaymentProfileId`, `PaymentProfileAttributes`, and `CreditCardAttributes`. If the sandbox requires a payment method, contact the provider or use a test gateway token. |
| **Response envelope** | `SubscriptionResponse` (namespace: `MaxioAdvancedBilling.Models`) — single required field `Subscription (subscription): Subscription !req` (namespace: `MaxioAdvancedBilling.Models`). |
| **Response fields to read** | `Subscription.Id` (int) — the Maxio subscription ID; store. `Subscription.State` (enum `SubscriptionState`, namespace: `MaxioAdvancedBilling.Models.Enums`; values: `Active`, `Trialing`, `Canceled`, `PastDue`, `Suspended`, etc.) — expected: `Active` on success. `Subscription.CurrentPeriodStartsAt` (DateTimeOffset), `Subscription.NextAssessmentAt` (DateTimeOffset) — next billing date. `Subscription.ProductPriceInCents` (long) — plan price (divide by 100 for display). |
| **Error case** | **Case A** — `SdkException<CreateSubscriptionError>` (namespace: `MaxioAdvancedBilling.Errors`). Error accessors: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. Note: 422 errors carry field-level validation failures (e.g., missing payment method); extract and report to caller. |
| **Pagination** | none |
| **Source** | `operations/Subscriptions.md` |

### Step 5: ListCustomerSubscriptions

| Field | Value |
|---|---|
| **Controller property** | `client.Customers` |
| **HTTP method** | `GET` |
| **Operation** | `ListCustomerSubscriptions` |
| **Signature** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` |
| **Parameters** | `customerId` — Maxio customer ID (from step 2). No pagination params; the operation returns all subscriptions for that customer. |
| **Request body** | (none) |
| **Response envelope** | `IReadOnlyList<SubscriptionResponse>` (namespace: `MaxioAdvancedBilling.Models`) — each element has required field `Subscription (subscription): Subscription !req`. |
| **Response fields to read** | Per subscription: `Subscription.Id` (int), `Subscription.State` (enum `SubscriptionState`), `Subscription.ProductPriceInCents` (long), `Subscription.CurrentPeriodEndsAt` (DateTimeOffset), `Subscription.NextAssessmentAt` (DateTimeOffset). Filter to active/trialing states for "my subscriptions" display; include canceled/expired for history (if needed). |
| **Error case** | **Case B** — `SdkException<RawError>` (namespace: `MaxioAdvancedBilling.Core.ErrorResponse`). |
| **Pagination** | none (operation fetches all) |
| **Source** | `operations/Customers.md` |

---

## Enum Values (Needed)

### CollectionMethod

| C# Member | Wire Value |
|---|---|
| `CollectionMethod.Automatic` | `automatic` |
| `CollectionMethod.Remittance` | `remittance` |
| `CollectionMethod.Prepaid` | `prepaid` |
| `CollectionMethod.Invoice` | `invoice` |

**Source:** `models/enums.md`

### SubscriptionState

| C# Member | Wire Value |
|---|---|
| `SubscriptionState.Active` | `active` |
| `SubscriptionState.Trialing` | `trialing` |
| `SubscriptionState.Canceled` | `canceled` |
| `SubscriptionState.PastDue` | `past_due` |
| `SubscriptionState.Suspended` | `suspended` |
| `SubscriptionState.Expired` | `expired` |
| `SubscriptionState.OnHold` | `on_hold` |
| `SubscriptionState.Unpaid` | `unpaid` |

**Source:** `models/enums.md`

---

## Client Construction & Auth

| Item | Details | Source |
|---|---|---|
| **Root namespace** | `MaxioAdvancedBilling` |  |
| **Client class** | `MaxioAdvancedBillingClient` | `MaxioAdvancedBillingClient.cs` |
| **Options class** | `MaxioAdvancedBillingClientOptions` | `MaxioAdvancedBillingClientOptions.cs` |
| **Auth scheme** | HTTP Basic — `Username` = API key, `Password` = literal `"x"` | `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| **Auth credentials type** | `BasicAuthCredentials` (namespace: `MaxioAdvancedBilling.Core.Authentication.Basic`) | — |
| **Environment enum** | `ServerEnvironment` (namespace: `MaxioAdvancedBilling.Servers`). Members: `ServerEnvironment.Us` (default, → `https://{site}.chargify.com`), `ServerEnvironment.Eu` (→ `https://{site}.ebilling.maxio.com`). | `Servers/ServerEnvironment.cs` |
| **Constructor** | `new MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`. The `HttpClient` must be registered via `IHttpClientFactory` and reused across requests (see `dotnet-client-initialization` for the DI pattern). | `MaxioAdvancedBillingClient.cs` |
| **DI alternative** | `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = ...; })` — uses the registered `HttpClient` automatically. | `ServiceCollectionExtensions.cs` |
| **Configuration** | `options.BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" }; options.Environment = ServerEnvironment.Us; options.Server.Production.Us.Site = subdomain;` | `MaxioAdvancedBillingClientOptions.cs` + `Servers/ServerOptions.cs` |

**All namespaces:** `MaxioAdvancedBilling` (root) · `MaxioAdvancedBilling.Api` (controllers) · `MaxioAdvancedBilling.Models` (request/response records) · `MaxioAdvancedBilling.Models.Enums` (enums) · `MaxioAdvancedBilling.Errors` (typed error classes) · `MaxioAdvancedBilling.Core.ErrorResponse` (raw error, base error types) · `MaxioAdvancedBilling.Core.Authentication.Basic` (auth credentials) · `MaxioAdvancedBilling.Servers` (environments) · `MaxioAdvancedBilling.Core.Configuration` (retry/timeout options).

---

## Trap Notes

⚠ **Step 1 (Client & DI setup)** — the SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register. `Timeout` is per-attempt. `MaxRetries = 0` is rejected at construction; floor is 1. A **transport failure** (non-2xx status, network error) on a `POST` is retried on every verb, so non-idempotent writes can execute more than once. **MUST load `dotnet-configuration-resilience`** before wiring the client.

⚠ **Step 2 & 4 (Customer & subscription creation)** — `ReadCustomerByReference` (step 2) returns `SdkException<RawError>` (no typed accessors), so check `ex.Error.StatusCode == HttpStatusCode.NotFound` to detect customer-not-found (idempotent). `CreateCustomer` and `CreateSubscription` throw typed `SdkException<CreateCustomerError>` and `SdkException<CreateSubscriptionError>` — use `TryGet…` accessors to extract field-level 422 errors. **MUST load `dotnet-error-handling`** before writing error handlers.

⚠ **Step 1, 2, 4, 5 (all call sites)** — all operations are **throw-only** (no `Result`/`ApiResult` no-throw variants). **MUST load `dotnet-calling-endpoints`** before the first operation call to understand named-argument binding and nullable-parameter handling.

⚠ **Step 3 (ListProducts)** — the request filter is `ListProductsFilter` (namespace: `MaxioAdvancedBilling.Models`), with optional `Ids: IReadOnlyList<int>?` field. Pass `filter: null` to fetch all, or `new ListProductsFilter { Ids = [7126957, 7126958] }` to fetch only Pro and Basic plans. **MUST load `dotnet-models`** before building request bodies.

⚠ **Step 2 & 5 (response deserialization)** — a drifted or malformed **2xx body** (e.g. missing required `Subscription` field in `SubscriptionResponse`) surfaces as `System.Text.Json.JsonException` from deserialization, **not** as `SdkException`. An SDK-exception-only catch ladder lets it escape the integration boundary and becomes an unhandled 500 to the caller. A **non-2xx body** that does not match the operation's generated error shape throws `JsonException` *while the error object is being constructed*, destroying the HTTP status with it. A boundary that maps every `JsonException` to 5xx then reports a deterministic rejection as an outage. **MUST load `dotnet-error-handling`** for the `JsonException` hazard rows — these are critical to the boundary shape.

⚠ **Step 4 (CreateSubscription)** — per task spec, no trial, no setup fee, and "no payment method required." The Maxio sandbox sandbox may or may not enforce payment-method presence depending on product configuration. If `CreateSubscription` fails with a 422 "Payment method required" error, confirm the Pro/Basic products are configured for `require_credit_card: false` on the sandbox, or use a test gateway token (e.g., `vault: "bogus"` for a fake Bogus gateway). **MUST load `dotnet-error-handling`** to extract and surface these validation errors to the caller.

---

## REQUIRED READING

Load these companion skills **before implementation starts.** The sheet deliberately does not carry their contents; each governs a step and surfaces traps the signature alone cannot show.

| Skill | Step | Reason |
|---|---|---|
| `dotnet-client-initialization` | 1 | Client construction, DI registration, HttpClient long-livedness, no per-request rebuild. |
| `dotnet-authentication` | 1 | Basic auth credentials wiring, pre-client setup, env-var loading, no hardcoding. |
| `dotnet-calling-endpoints` | 2, 3, 4, 5 | Named-argument binding, nullable-param handling, async usage, cancellation semantics. |
| `dotnet-models` | 2, 3, 4, 5 | Request/response record immutability, `required` fields, nullable optionals, no `new` for unions. |
| `dotnet-error-handling` | 2, 4, 5, 6 | Typed vs. raw error cases, `TryGet…` accessors, no-throw variants (absent here — all throw), `JsonException` hazards. |
| `dotnet-configuration-resilience` | 1, 6 | Retry/timeout bounds, `Timeout` per-attempt not total, `MaxRetries` floor, transport-failure retry on all verbs. |
| `dotnet-testing` | (after 6) | HttpClient seam, mock setup, test framework integration. |

**Mandatory hazard rows (for the error boundary):**

- **`System.Text.Json.JsonException` from 2xx deserialization:** Missing required model field surfaces here, not as `SdkException`. Catch separately or the exception escapes the boundary.
- **`System.Text.Json.JsonException` from error-response deserialization:** Non-2xx body that doesn't match the operation's `{Operation}Error` shape throws `JsonException` *during* error construction, destroying the HTTP status. A boundary that maps all `JsonException` to 5xx will misreport deterministic validation failures as outages.

---

## Assumptions & Blockers

### Assumptions

1. eShopOnWeb user ID is unique and stable; it will be passed to Maxio as `customer.reference` for idempotent lookups and subscription reference.
2. The application will load `Maxio:ApiKey`, `Maxio:Subdomain`, and `Maxio:ProductFamilyHandle` from user-secrets (or config). If `Maxio:BaseUrl` is provided, it overrides the computed URL (useful for testing/mocking).
3. Subscriptions are created with `CollectionMethod.Automatic` (credit-card auto-billing) by default, or `CollectionMethod.Invoice` if configured otherwise.
4. The integration is backend-only; the three PublicApi endpoints are exposed via a new subscriptions controller, all JWT-authenticated.
5. No webhooks are configured for this MVP; subscription state is queried on-demand.
6. The application will handle non-Maxio errors (network, timeouts, auth) separately from Maxio-specific errors (422 validation, 404 not found).

### Blockers

None identified. The Maxio sandbox entities (product family, plans, metered component) are pre-configured and stable per the task. All required operations are present in the SDK.

---

## Summary

This plan adds JWT-authenticated subscriptions to eShopOnWeb via three endpoints:
- **GET /api/subscription-plans** — lists available plans (Pro $299/mo, Basic $29/mo).
- **POST /api/subscriptions** — creates a subscription for the logged-in user.
- **GET /api/my-subscriptions** — retrieves the user's subscriptions.

Customer sync is idempotent (lookup by user ID, create if not found). Errors are typed where available (422 validation failures) and raw otherwise (auth, 5xx). The boundary must handle both `SdkException` and `System.Text.Json.JsonException` to avoid misreporting client errors as outages. The client is registered via DI and reused across requests; retry/timeout are per-attempt (not total-call) and governed by Polly configuration.

Integration order: client setup → customer sync → plans list → subscription creation → my-subscriptions read → error boundary. All operations use the Maxio Production environment (US or EU per config).
