# Maxio Integration Plan — eShopOnWeb Subscription Billing

## Scope & Sequence

1. **Client & DI Setup** — Initialize MaxioAdvancedBillingClient with HTTP factory, register in DI
2. **Configuration** — Load credentials (API key, subdomain, environment) from `Maxio:` config section and env vars
3. **Authentication** — Set Basic auth (username = API key, password = literal `"x"`) on client options
4. **List Plans** — GET `/product_families/{id}/products.json` → expose as `GET /api/subscription-plans`
5. **Lookup/Create Customer** — GET `/customers/lookup.json` then POST `/customers.json` for idempotent customer → no public endpoint (internal only)
6. **Create Subscription** — POST `/subscriptions.json` → expose as `POST /api/subscriptions`
7. **List Subscriptions** — GET `/customers/{id}/subscriptions.json` → expose as `GET /api/my-subscriptions`
8. **Error Handling** — Catch `SdkException<T>` (typed or raw), map to HTTP 400/404/422/500 per operation

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

| Operation | Signature | Request Model (fields, `wire_name`, required?) | Response Envelope | Error Case · Accessors · Payload Type | Pagination | Source |
|-----------|-----------|-----------------------------------------------|-------------------|-----------------------------------------|------------|--------|
| **ListProductsForProductFamily** | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — *8 params (`dateField` … `include`) nullable, no default → **must pass explicitly** (pass `null` to skip); `page` defaults to 1, `perPage` defaults to 20* | No request body; all query params optional | `IReadOnlyList<ProductResponse>` — each item wraps `Product (product): Product !req` | **Case A** — `SdkException<ListProductsForProductFamilyError>` · `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | `page` + `perPage` (manual) | `operations/ProductFamilies.md` |
| **ReadCustomerByReference** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — *`reference` query param* | No request body; `reference` as query string | `CustomerResponse` — wraps `Customer (customer): Customer !req` | **Case B** — `SdkException<RawError>` · `StatusCode: HttpStatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | None | `operations/Customers.md` |
| **CreateCustomer** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — *`body` nullable, no default → **must pass explicitly*** | `CreateCustomerRequest` wraps `Customer (customer): CreateCustomer !req`; `CreateCustomer` fields: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `CcEmails (cc_emails): string?`, `Organization (organization): string?`, `Reference (reference): string?`, `Address (address): string?`, `Address2 (address_2): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `Locale (locale): string?`, `VatNumber (vat_number): string?`, `TaxExempt (tax_exempt): bool?`, `TaxExemptReason (tax_exempt_reason): string?`, `ParentId (parent_id): int?`, `SalesforceId (salesforce_id): string?` | `CustomerResponse` — wraps `Customer (customer): Customer !req` | **Case A** — `SdkException<CreateCustomerError>` · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | None | `operations/Customers.md` · `records-1-Ac-Cr.md` |
| **CreateSubscription** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — *`body` nullable, no default → **must pass explicitly*** | `CreateSubscriptionRequest` wraps `Subscription (subscription): CreateSubscription !req`; `CreateSubscription` key fields: `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?`, `ProductPricePointId (product_price_point_id): int?`, `CustomerId (customer_id): int?`, `CustomerReference (customer_reference): string?`, `CustomerAttributes (customer_attributes): CustomerAttributes?`, `PaymentProfileId (payment_profile_id): int?`, `PaymentProfileAttributes (payment_profile_attributes): PaymentProfileAttributes?`, `Reference (reference): string?`, `CouponCodes (coupon_codes): IReadOnlyList<string>?`, `Components (components): IReadOnlyList<CreateSubscriptionComponent>?` — *note: all optional; payment_profile_id omitted for "payment method not required" products; see Notes below* | `SubscriptionResponse` — wraps `Subscription (subscription): Subscription?` | **Case A** — `SdkException<CreateSubscriptionError>` · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | None | `operations/Subscriptions.md` · `records-2-Cr-Ne.md` |
| **ListCustomerSubscriptions** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | No request body; `customerId` in path | `IReadOnlyList<SubscriptionResponse>` — each item wraps `Subscription (subscription): Subscription?` | **Case B** — `SdkException<RawError>` · `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | None | `operations/Customers.md` |

**Response Envelope Details:**

| Envelope Type | Inner Type | Field Name (wire) | Nullability | Notes |
|---|---|---|---|---|
| `CustomerResponse` | `Customer` | `Customer (customer)` | `!req` (required in record init) | Always present on 200; read via `.Customer` |
| `SubscriptionResponse` | `Subscription` | `Subscription (subscription)` | `?` (optional) | Read via `.Subscription` |
| `ProductResponse` | `Product` | `Product (product)` | `!req` (required in record init) | Always present on 200; read via `.Product` |

**Error Payload Models (422 errors):**

| Error Accessor | Payload Type | Fields |
|---|---|---|
| `TryGetCustomerErrorResponse1` | `CustomerErrorResponse1` | `Errors (errors): Errors?` where `Errors` has `PerPage (per_page): IReadOnlyList<string>?`, `PricePoint (price_point): IReadOnlyList<string>?` |
| `TryGetErrorListResponse1` | `ErrorListResponse1` | `Errors (errors): IReadOnlyList<string> !req` — list of error messages |

**Key Enum & Value Notes:**

- `BasicDateField?` (operations filter) — omit with `null` when not filtering by date
- `ListProductsFilter?` — omit with `null` when not filtering; see union variants in `unions.md` if needed
- `ListProductsInclude?` — omit with `null` unless extending product response
- `IntervalUnit` — required enum on product price points; wire values: `day`, `month`, `year` (see `enums.md`)
- `CollectionMethod?` — optional enum on subscription; wire values e.g. `automatic`, `invoice`, `remittance`

**Namespace Mapping (add these `using` directives):**

```csharp
using MaxioAdvancedBilling;                                    // Client, options
using MaxioAdvancedBilling.Api;                                // Controllers (client.Customers, client.Subscriptions, etc.)
using MaxioAdvancedBilling.Core.Authentication.Basic;          // BasicAuthCredentials
using MaxioAdvancedBilling.Models;                             // Request/response records
using MaxioAdvancedBilling.Errors;                             // Error classes
using MaxioAdvancedBilling.Servers;                            // ServerEnvironment
using MaxioAdvancedBilling.Core.Configuration;                 // RetryOptions
```

---

## Trap Notes

- ⚠ **Step 1 (client & DI setup)** — The `HttpClient` must be long-lived and reused via `IHttpClientFactory`, not rebuilt per request; the SDK client wrapper over it may be transient. **MUST load `dotnet-client-initialization`** before wiring the client.

- ⚠ **Step 2 & 3 (configuration & auth)** — Credentials must be set **before** constructing the client or in the DI callback; load the API key from configuration (binding key `Maxio:ApiKey`), not from environment directly. Basic auth is username = API key, password = literal `"x"`. **MUST load `dotnet-authentication`** before wiring credentials.

- ⚠ **Step 4–7 (calling operations)** — All operations are **throw-based**; there is no `.Result` variant. Call with named arguments when passing `null` for optional params (e.g., `dateField: null, filter: null, …`); positional `null` arguments can mis-bind. Response envelopes wrap the payload — read one level down (e.g., `response.Customer`, `response.Product`). **MUST load `dotnet-calling-endpoints`** before the first call.

- ⚠ **Step 8 (error handling)** — Two **distinct** error patterns in scope: (A) **CreateCustomer** and **CreateSubscription** throw `SdkException<{Operation}Error>` with typed `TryGet…` accessors (422 maps to `CustomerErrorResponse1` / `ErrorListResponse1`); (B) **ReadCustomerByReference** and **ListCustomerSubscriptions** throw `SdkException<RawError>` (no typed accessors). A `JsonException` from deserialization (missing `required` field in 2xx body) surfaces **outside** the `SdkException` boundary and must be caught separately. A malformed 422 body (not matching the operation's error shape) throws `JsonException` **during error object construction**, replacing the `SdkException` and destroying the HTTP status. **MUST load `dotnet-error-handling`** before writing the boundary, and include BOTH rows below in the FIRST sheet:
  - A drifted or malformed **2xx** body (missing `required` member) surfaces as `JsonException` from deserialization, **not** as `SdkException` — SDK-exception-only catch ladder lets it escape the boundary.
  - A **non-2xx** body not matching operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, **replacing** the `SdkException` and destroying HTTP status — a boundary that maps every `JsonException` to 5xx reports a deterministic rejection as an outage, and a caller retrying 5xx retries something that can never succeed.

- ⚠ **Step 1 & 8 (resilience & config)** — Retry/timeout options on the client do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register. `MaxRetries = 0` is rejected at construction (floor is 1); a transport failure on a `POST` is retried regardless of `HttpMethodsToRetry`, so a non-idempotent write can execute more than once. The `Timeout` property is per-attempt, not total. For idempotent creates (customer, subscription), implement **client-side deduplication** (e.g., lookup-before-create with `reference`). **MUST load `dotnet-configuration-resilience`** before tuning retries, timeouts, or the base URL.

---

## REQUIRED READING

Load these companion skills **before implementation starts**. The sheet deliberately does not carry their contents — the context cost is outweighed by the risk of stale inline copies.

| Skill | Step(s) it Governs |
|-------|-------------------|
| `dotnet-client-initialization` | Client construction, DI registration, HttpClient factory |
| `dotnet-authentication` | Setting Basic auth credentials (API key + "x") |
| `dotnet-calling-endpoints` | Calling operations, named arguments, response reading |
| `dotnet-models` | Request/response record fields, unions, enums |
| `dotnet-error-handling` | Catch boundaries, typed vs raw errors, JsonException handling |
| `dotnet-configuration-resilience` | Retry options, timeout semantics, base URL override |

---

## Assumptions & Blockers

**Assumptions:**

- Product Family ID (3023074) and product/component IDs are pre-created on Maxio sandbox `cp-exp-3` and will not change during integration.
- Configuration binding key is `Maxio:` (e.g., `Maxio:ApiKey`), and all required settings (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, `Environment`) are supplied at startup.
- Environment variable mappings are: `MAXIO_API_KEY` → `Maxio:ApiKey`, `MAXIO_SITE_SUBDOMAIN` → `Maxio:Subdomain`, `MAXIO_ENVIRONMENT` → `Maxio:Environment`, `MAXIO_DEFAULT_PRODUCT_FAMILY` → `Maxio:ProductFamilyHandle`.
- User identity (from JWT) is available as a string (e.g., user ID or email) to use as the `reference` field when creating/looking up a customer, ensuring idempotency.
- **Idempotency strategy:** On subscription create, always call `ReadCustomerByReference` first; if 404, create via `CreateCustomer` (with `reference` = user identity). This prevents duplicate customer records on retry.
- Error responses from Maxio (e.g., 422 validation) are mapped to HTTP 400/422 (not swallowed or re-mapped to 500).
- No SQL Server; in-memory database is used, so customer ↔ Maxio ID mapping can be stored in `DbContext` without persistence concerns for this plan.

**Blockers:**

- None at planning stage. All required Maxio operations exist in the SDK map, error models are documented, and the authentication scheme is straightforward. Implementation will confirm SDK versions and error shape details at compile time.

---

## Client Construction & Configuration Facts

| Item | Value / Source |
|------|--------|
| **NuGet Package** | `AsadAli.AdvancedBilling.Sdk` (`netstandard2.0`) |
| **Root Namespace** | `MaxioAdvancedBilling` |
| **Client Class** | `MaxioAdvancedBillingClient` |
| **Constructor** | `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` |
| **Auth Type** | HTTP **Basic** only: username = API key, password = literal `"x"` |
| **Auth Class** | `BasicAuthCredentials` (namespace `MaxioAdvancedBilling.Core.Authentication.Basic`) |
| **Server Environments** | `ServerEnvironment.Us` (default, `https://{site}.chargify.com`) · `ServerEnvironment.Eu` (`https://{site}.ebilling.maxio.com`) |
| **Site Subdomain** | Set via `options.Server.Production.Us.Site = "cp-exp-3"` (or read from config) |
| **Base URL Override** | `options.Server.Production.Us.BaseUrl = "custom_url"` if needed (e.g., mock server for tests) |
| **Retry Options** | `options.Retry` (`RetryOptions` namespace `MaxioAdvancedBilling.Core.Configuration`); all members `required` — use `RetryOptions.Default()` as starting point; **floors:** `MaxRetries` ≥ 1, `Timeout` is per-attempt |

---

*Plan produced by Maxio Advanced Billing .NET SDK specialist. All signatures, enum values, error accessors, and type namespaces are sourced from the bundled SDK map (`sdk-map.md` + `map/operations/` + `map/models/`) dated `v1.0.2` (commit `15db14b`). Companion skills are mandatory before implementation.*
