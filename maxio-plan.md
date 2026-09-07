# Maxio Advanced Billing Integration Plan — eShopOnWeb Subscriptions

## Scope & Sequence

1. **Client Registration & DI** — Register `MaxioAdvancedBillingClient` with dependency injection; wire HTTP client, auth, and server configuration from `Maxio:*` settings.
2. **Authentication Setup** — Configure HTTP Basic auth (API key as username, `"x"` as password) and environment (US/EU).
3. **Customer Management** — Find or create Maxio customer by eShopOnWeb user email (idempotent: use `reference` field to map eShopOnWeb user ID). Store Maxio customer ID in session/in-memory store.
4. **Product/Plans Listing** — Fetch all active products under the seeded product family (`eshop-subscribe` handle, ID 3023074). Return plan details (name, handle, price, billing frequency) to caller.
5. **Subscription Creation** — Link eShopOnWeb user's Maxio customer to a selected plan. Idempotent by `reference` (use eShopOnWeb user ID + plan handle). Return subscription state and next-billing date.
6. **Subscription Queries** — Read subscription state, list all subscriptions for a customer, and expose clean API contracts to callers.
7. **Error Boundary** — Trap Maxio SDK exceptions (typed and raw), convert to meaningful HTTP status codes and user-facing messages.

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operation Contracts

| Step | Controller.Method | Signature | Request Model + Fields | Response Envelope | Error Case | Pagination | Source |
|---|---|---|---|---|---|---|---|
| 3a: Find customer by reference (email fallback) | `Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | **Query param only**: `reference` (wire: `reference`, C#: `reference`) — **must pass explicitly** | `CustomerResponse` wraps `Customer`: { `Id: int?`, `Email: string?`, `FirstName: string?`, `LastName: string?` } | `SdkException<RawError>` (Case B) · `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` · **404 on not found** | none | `operations/Customers.md` |
| 3b: Create customer (fallback if not found) | `Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — **body nullable, must pass explicitly** | `CreateCustomerRequest` wraps `Customer`: { **`FirstName (first_name): string !req`**, **`LastName (last_name): string !req`**, **`Email (email): string !req`**, `Reference (reference): string?`, `Organization (organization): string?`, `Address (address): string?`, `Address2 (address_2): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `Locale (locale): string?`, `VatNumber (vat_number): string?`, `TaxExempt (tax_exempt): bool?`, `TaxExemptReason (tax_exempt_reason): string?`, `ParentId (parent_id): int?`, `SalesforceId (salesforce_id): string?`, `CcEmails (cc_emails): string?` } | `CustomerResponse` wraps `Customer`: { `Id: int?`, `Email: string?`, `FirstName: string?`, `LastName: string?`, `Reference: string?`, `CreatedAt: DateTimeOffset?` } | `SdkException<CreateCustomerError>` (Case A) · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Customers.md` `records-2-Cr-Ne.md` |
| 4a: List products by family handle | `ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — **8 nullable params before pagination; pass `null` to skip each** | **Query params (wire ← C#)**: `productFamilyId` is path param (required). `dateField`, `filter`, `startDate`, `endDate`, `startDatetime`, `endDatetime`, `includeArchived`, `include` are query (pass `null` to omit). Pagination defaults: `page = 1`, `perPage = 20`. **All nullable params must be passed explicitly.** | `IReadOnlyList<ProductResponse>` — each wraps `Product`: { `Id: int?`, `Name: string?`, `Handle: string?`, `Description: string?`, `PriceInCents: long?`, `Interval: int?`, `IntervalUnit: IntervalUnit?`, `TrialPriceInCents: long?` } | `SdkException<ListProductsForProductFamilyError>` (Case A) · `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage` | `operations/ProductFamilies.md` `records-3-Of-Su.md` |
| 4b: Get product by handle (alternate lookup) | `Products.ReadProductByHandle` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | `apiHandle` (wire: `api_handle`, path param, required, positional) | `ProductResponse` wraps `Product`: { `Id: int?`, `Name: string?`, `Handle: string?`, `Description: string?`, `PriceInCents: long?`, `Interval: int?`, `IntervalUnit: IntervalUnit?` } | `SdkException<RawError>` (Case B) · `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` · **404 on not found** | none | `operations/Products.md` |
| 5: Create subscription | `Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — **body nullable, must pass explicitly** | `CreateSubscriptionRequest` wraps `Subscription`: { **`ProductHandle (product_handle): string?`** or **`ProductId (product_id): int?`** (one required), `CustomerId (customer_id): int?`, `Reference (reference): string?`, `CustomerAttributes (customer_attributes): CustomerAttributes?` (used only if creating customer inline; not needed if `customer_id` passed), `InitialBillingAt (initial_billing_at): DateTimeOffset?`, `NextBillingAt (next_billing_at): DateTimeOffset?`, `DeferSignup (defer_signup): bool? = false`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `CouponCode (coupon_code): string?`, `CouponCodes (coupon_codes): IReadOnlyList<string>?`, ... (40+ optional fields; focus on those listed) } | `SubscriptionResponse` wraps `Subscription`: { `Id: int?`, `CustomerId: int?`, `ProductId: int?`, `State: SubscriptionState?`, `CurrentPeriodStartsAt: DateTimeOffset?`, `CurrentPeriodEndsAt: DateTimeOffset?`, `NextBillingAt: DateTimeOffset?`, `TrialStartedAt: DateTimeOffset?`, `TrialEndedAt: DateTimeOffset?`, `ActivatedAt: DateTimeOffset?`, `CanceledAt: DateTimeOffset?`, `Reference: string?` } | `SdkException<CreateSubscriptionError>` (Case A) · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` `records-2-Cr-Ne.md` |
| 6a: Read subscription by ID | `Subscriptions.ReadSubscription` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — **include nullable, must pass explicitly** | `subscriptionId` (path param, positional, required). `include` (query param, optional, pass `null` to omit). | `SubscriptionResponse` wraps `Subscription`: { `Id: int?`, `CustomerId: int?`, `State: SubscriptionState?`, `NextBillingAt: DateTimeOffset?`, `CurrentPeriodEndsAt: DateTimeOffset?`, `TrialStartedAt: DateTimeOffset?`, `TrialEndedAt: DateTimeOffset?`, `ActivatedAt: DateTimeOffset?`, `CanceledAt: DateTimeOffset?` } | `SdkException<RawError>` (Case B) · `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` · **404 on not found** | none | `operations/Subscriptions.md` |
| 6b: Find subscription by reference | `Subscriptions.FindSubscription` | `FindSubscription(string? reference, CancellationToken ct = default)` — **reference nullable, must pass explicitly** | **Query param only**: `reference` (wire: `reference`, C#: `reference`) | `SubscriptionResponse` wraps `Subscription`: { `Id: int?`, `CustomerId: int?`, `State: SubscriptionState?`, `NextBillingAt: DateTimeOffset?` } | `SdkException<FindSubscriptionError>` (Case A) · `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |
| 6c: List subscriptions by customer ID | `Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | `customerId` (path param, positional, required) | `IReadOnlyList<SubscriptionResponse>` — each wraps `Subscription`: { `Id: int?`, `State: SubscriptionState?`, `NextBillingAt: DateTimeOffset?` } | `SdkException<RawError>` (Case B) · `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | none | `operations/Customers.md` |

### Request/Response Model Details

**`CollectionMethod` enum** (wire names for `payment_collection_method`):
- `CollectionMethod.Automatic` (wire: `automatic`)
- `CollectionMethod.Remittance` (wire: `remittance`)
- `CollectionMethod.Prepaid` (wire: `prepaid`)
- `CollectionMethod.Invoice` (wire: `invoice`)

**`SubscriptionState` enum** (subscription state values):
- `SubscriptionState.Pending` (wire: `pending`)
- `SubscriptionState.Trialing` (wire: `trialing`)
- `SubscriptionState.Active` (wire: `active`)
- `SubscriptionState.SoftFailure` (wire: `soft_failure`)
- `SubscriptionState.PastDue` (wire: `past_due`)
- `SubscriptionState.Suspended` (wire: `suspended`)
- `SubscriptionState.Canceled` (wire: `canceled`)
- `SubscriptionState.Expired` (wire: `expired`)
- `SubscriptionState.Paused` (wire: `paused`)
- `SubscriptionState.AwaitingSignup` (wire: `awaiting_signup`)

**`IntervalUnit` enum** (billing frequency):
- `IntervalUnit.Day` (wire: `day`)
- `IntervalUnit.Month` (wire: `month`)

### Namespaces (add `using` directives)

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Api;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Core.Exceptions;
```

### Client Construction & Configuration

**From config (appsettings.json or environment)**:
- `Maxio:ApiKey` → HTTP Basic auth username
- `Maxio:Subdomain` → site subdomain for base URL
- `Maxio:ProductFamilyHandle` → handle of the product family containing plans (e.g., `"eshop-subscribe"`)
- `Maxio:BaseUrl` (optional) → override Production base URL for dev/testing

**DI Registration** (pseudo-code; see `dotnet-client-initialization`):
```csharp
services.AddMaxioAdvancedBillingClient(options =>
{
    options.BasicAuth = new BasicAuthCredentials 
    { 
        Username = config["Maxio:ApiKey"], 
        Password = "x" 
    };
    options.Environment = ServerEnvironment.Us; // or .Eu based on config
    options.Server.Production.Us.Site = config["Maxio:Subdomain"];
    // Retry defaults apply (Polly-backed). Tune if needed via options.Retry.
});
```

**Error-response payload types** (for typed error accessors):
- `CustomerErrorResponse1` (422 from CreateCustomer) → `Errors` (dict of field names to error arrays)
- `ErrorListResponse1` (422 from CreateSubscription) → `Errors` (list of error strings)
- `RawError` (fallback / other statuses) → `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`

---

## Trap Notes

⚠ **Step 2 (authentication)** — HTTP Basic credentials must be set **before** client construction or in the DI callback. The password is the literal string `"x"`, not a placeholder. **MUST load `dotnet-authentication`** before wiring credentials.

⚠ **Step 1 (client registration)** — The SDK client is a thin wrapper over an `IHttpClientFactory`-managed `HttpClient`. Do not create a new client per request; register it once and inject. The SDK's `RetryOptions` do **not** bound a whole call end-to-end and are **not** the timeout on the `HttpClient` you register. **MUST load `dotnet-configuration-resilience`** before tuning retries or timeouts.

⚠ **Steps 3–6 (all calls)** — All operations are throw-only; there is no `{Operation}Result` or no-throw variant. Catch `SdkException<T>` (typed or `RawError`). Many list/read operations are Case B (no typed error accessors); confirm each row's error case in the CONTRACT SHEET. **MUST load `dotnet-error-handling`** before writing any `try/catch`.

⚠ **Step 4 (product listing)** — `ListProductsForProductFamily` requires `productFamilyId` (path param). You may pass the product family **handle** (e.g., `"eshop-subscribe"`) or **ID** (e.g., `"3023074"`); the API accepts both. **Confirm with live traffic whether handles work; if not, pre-fetch the family ID by handle using `ReadProductFamily`.** All 8 query-filter params (`dateField`, `filter`, `startDate`, etc.) are **nullable and must be passed explicitly**—pass `null` to skip each one.

⚠ **Step 5 (subscription creation)** — The `CreateSubscription` request requires either `product_handle` or `product_id` (both optional in the type, but one must be present in the wire payload, per API notes). Equally, you must supply either `customer_id` (lookup) or `customer_attributes` (inline create). The idempotency key is **not automatic**; if you want to avoid duplicate subscriptions, you must pass a unique `reference` (wire: `reference`, C# field: `Reference`) and check `FindSubscription` before creating. **Do not rely on Maxio's de-duplication—it is not guaranteed.** **MUST load `dotnet-models`** before constructing unions or enums in request bodies.

⚠ **Steps 5–6 (response envelope)** — Response types wrap their payload in a single field. For example, `SubscriptionResponse.Subscription`, not `response.subscription` or direct access. The compiler enforces this via the record shape; a read that skips unwrapping will not compile.

⚠ **Steps 3, 5, 6 (error boundary)** — Two JSON-deserialization errors can escape the SDK:
   - A **2xx response with a missing required field** (e.g., `Id` on `Customer`) surfaces as `JsonException` from deserialization, **not** `SdkException`. An error-only catch ladder lets it escape and crashes the boundary.
   - A **non-2xx response body that doesn't match the operation's `{Operation}Error` shape** throws `JsonException` **while the error object is being built**, destroying the HTTP status. A boundary that maps all `JsonException` to 5xx logs a successful HTTP error as an outage, and callers retrying 5xx never succeed.

   **MUST load `dotnet-error-handling`** for the full error-boundary pattern. Design the boundary **before implementation**.

⚠ **Step 1 (configuration binding)** — Use `IConfiguration` and `IOptions<MaxioOptions>` pattern (or direct binding). Hard-coded settings must never appear in code. Environment variables are read by ASP.NET Core by default; do not re-load them manually.

---

## REQUIRED READING

Before implementation begins, load these companion skills (in order):

| Skill | Step(s) it governs |
|---|---|
| `dotnet-client-initialization` | 1: Client registration and DI setup; HttpClient factory lifecycle. |
| `dotnet-authentication` | 2: HTTP Basic credentials (username = API key, password = `"x"`); per-environment config; rotating keys. |
| `dotnet-calling-endpoints` | 5–6: Calling SDK operations; optional parameter binding; named vs. positional arguments; async/await and cancellation. |
| `dotnet-models` | 5: Building request bodies (enums, unions); reading response fields; immutable records and `init`-only setters. |
| `dotnet-error-handling` | 7: Catch hierarchies; typed vs. raw error accessors; JSON deserialization errors at the boundary; defensive extraction. |
| `dotnet-configuration-resilience` | 1: Retry/timeout semantics; `Polly` integration; per-attempt vs. end-to-end; logging hooks. |

These are **mandatory** before implementation. The contract sheet deliberately omits their contents—it is your job to load each skill at its step and confirm types/names/defaults match the sheet and your code.

Additionally, both of these deserialize-error rows apply to ALL operations:
- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary. These rows belong in the FIRST sheet, not a later revision: the boundary is written early, and a caveat that arrives afterwards arrives too late to shape it.

---

## Assumptions & Blockers

**Assumptions:**
1. Maxio customer ↔ eShopOnWeb user is 1:1 stable (created once, keyed by `reference` field set to eShopOnWeb user ID).
2. One subscription per user per plan at a time is acceptable (no multi-subscription-to-same-plan handling yet; idempotency by reference prevents accidental duplicates).
3. The seeded sandbox entities (product family handle `eshop-subscribe`, plan handles `eshop-pro` and `basic-plan`) are stable and will not be deleted during the integration window.
4. Configuration binding (`IOptions<T>` or raw `IConfiguration`) is available; no secret management (vaults, KMS) is in scope.
5. Callers are authenticated (JWT on PublicApi) and identity is extracted from token claims; no per-call re-auth is needed beyond the SDK's HTTP Basic.
6. All timestamps from Maxio are assumed to be in UTC; no timezone conversion is needed for display (or is handled at the UI layer).

**Blockers:**
- None identified. The SDK map is complete for the operations in scope. Configuration and DI setup are standard ASP.NET patterns.

---

## Implementation Checklist (for the main agent)

- [ ] Add NuGet package `AsadAli.AdvancedBilling.Sdk` to the PublicApi project.
- [ ] Create `IMaxioService` abstraction wrapping the SDK (customers, products, subscriptions) — shields the API layer from SDK details.
- [ ] Load and implement each REQUIRED READING skill's pattern before touching the corresponding step.
- [ ] Design error boundary **first** (before any SDK calls): catch `SdkException<T>`, typed accessors, `JsonException` handling per `dotnet-error-handling`.
- [ ] Implement Step 1 (DI) and Step 2 (auth) together; verify client construction works (smoke test).
- [ ] Implement Step 3 (customer management); test idempotency by reference.
- [ ] Implement Steps 4–6 (products and subscriptions); hand back Swagger/OpenAPI for the three HTTP endpoints.
- [ ] Test against sandbox site `cp-exp-4` with seeded entities; confirm state transitions and next-billing-date accuracy.
- [ ] All configuration keys (`Maxio:ApiKey`, `Maxio:Subdomain`, etc.) read from `appsettings.json` or environment; no hardcodes.
