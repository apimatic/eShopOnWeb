# Maxio Advanced Billing Integration Plan — eShopOnWeb Subscription Billing

## Scope & sequence

Implement three HTTP endpoints (JWT-authenticated) on the `PublicApi` ASP.NET Core 8 project to expose Maxio subscription management:

1. **GET /api/subscription-plans** — Fetch the two available plans (Pro @ $299/mo, Basic @ $29/mo) from the product family `eshop-subscribe`. Calls `ListProductsForProductFamily` scoped to the family, filters active non-archived products, maps to a DTO.
2. **POST /api/subscriptions** — Create a subscription (idempotent on customer). Creates or retrieves customer (by reference = logged-in user ID) via `CreateCustomer` or `ReadCustomerByReference`; then calls `CreateSubscription` with selected product and customer. Returns subscription with its ID and state.
3. **GET /api/my-subscriptions** — Fetch current user's subscriptions. Resolves user → customer (by reference); calls `ListCustomerSubscriptions` to get all subscriptions for that customer.

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Endpoints & operations

| Controller · Method | Signature | Request model + fields | Response envelope + inner fields | Error case + accessors + payload type | Pagination | Source |
|---|---|---|---|---|---|---|
| **Products** · **ListProductsForProductFamily** | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` · Must pass `productFamilyId` = "3023074" · `dateField`, `filter`, `startDate`, `endDate`, `startDatetime`, `endDatetime`, `includeArchived`, `include` are nullable and must be passed explicitly (pass `null` to skip); `page`/`perPage` have defaults | None (query-param-only) | `IReadOnlyList<ProductResponse>` · Each wraps `ProductResponse.Product: Product` with fields: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `Description (description): string?`, `ArchivedAt (archived_at): DateTimeOffset?` | **Case A** · `TryGetString(out string)` [404]; `TryGetRawError(out RawError)` [fallback] | Manual `page`/`perPage` | `operations/ProductFamilies.md` |
| **Customers** · **CreateCustomer** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` · `body` is nullable, no default → **must pass explicitly** | `CreateCustomerRequest.Customer: CreateCustomer !req` with fields: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `CcEmails (cc_emails): string?`, `Organization (organization): string?`, `Reference (reference): string?`, `Address (address): string?`, `Address2 (address_2): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `Locale (locale): string?`, `VatNumber (vat_number): string?`, `TaxExempt (tax_exempt): bool?`, `TaxExemptReason (tax_exempt_reason): string?`, `ParentId (parent_id): int?`, `SalesforceId (salesforce_id): string?` | `CustomerResponse.Customer: Customer` with fields: `Id (id): int?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?` | **Case A** · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422]; `TryGetRawError(out RawError)` [fallback]. `CustomerErrorResponse1.Errors: Errors?` with `PerPage: IReadOnlyList<string>?`, `PricePoint: IReadOnlyList<string>?` | None | `operations/Customers.md` |
| **Customers** · **ReadCustomerByReference** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` · `reference` is query param (wire ← C#: `reference` ← `reference`) | None (query-param-only) | `CustomerResponse.Customer: Customer` with fields (as above) | **Case B** · `StatusCode: HttpStatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()` | None | `operations/Customers.md` |
| **Customers** · **ListCustomerSubscriptions** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` · `customerId` in URL | None | `IReadOnlyList<SubscriptionResponse>` · Each wraps `SubscriptionResponse.Subscription: Subscription` with fields: `Id (id): int?`, `State (state): SubscriptionState?`, `CustomerId (customer_id): int?`, `ProductId (product_id): int?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, `BalanceInCents (balance_in_cents): long?`, `CancelAtEndOfPeriod (cancel_at_end_of_period): bool?`, `CanceledAt (canceled_at): DateTimeOffset?` | **Case B** · `StatusCode: HttpStatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()` | None | `operations/Customers.md` |
| **Subscriptions** · **CreateSubscription** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` · `body` is nullable, no default → **must pass explicitly** | `CreateSubscriptionRequest.Subscription: CreateSubscription !req` with fields (selection for typical use): `ProductId (product_id): int?`, `ProductHandle (product_handle): string?` (one of product ID/handle required per Notes), `CustomerId (customer_id): int?`, `CustomerReference (customer_reference): string?` (one of customer ID/reference per Notes), `CustomerAttributes (customer_attributes): CustomerAttributes?` (for new-customer flow: `FirstName: string?`, `LastName: string?`, `Email: string?`, `Reference: string?`, `Address: string?`, `City: string?`, `State: string?`, `Zip: string?`, `Country: string?`, `Phone: string?`), `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `PaymentProfileAttributes (payment_profile_attributes): PaymentProfileAttributes?` (optional; omit if no payment method required), `Reference (reference): string?` (subscription reference for idempotency tracking) | `SubscriptionResponse.Subscription: Subscription` with fields: `Id (id): int?`, `State (state): SubscriptionState?`, `CustomerId (customer_id): int?`, `ProductId (product_id): int?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?` | **Case A** · `TryGetErrorListResponse1(out ErrorListResponse1)` [422]; `TryGetRawError(out RawError)` [fallback]. `ErrorListResponse1.Errors: IReadOnlyList<string> !req` | None | `operations/Subscriptions.md` |

### Enums (wire values in parentheses)

**SubscriptionState** (namespace `MaxioAdvancedBilling.Models.Enums`):
- `Pending (pending)`
- `FailedToCreate (failed_to_create)`
- `Trialing (trialing)`
- `Assessing (assessing)`
- `Active (active)`
- `SoftFailure (soft_failure)`
- `PastDue (past_due)`
- `Suspended (suspended)`
- `Canceled (canceled)`
- `Expired (expired)`
- `Paused (paused)`
- `Unpaid (unpaid)`
- `TrialEnded (trial_ended)`
- `OnHold (on_hold)`
- `AwaitingSignup (awaiting_signup)`

**IntervalUnit** (namespace `MaxioAdvancedBilling.Models.Enums`):
- `Day (day)`
- `Month (month)`

**CollectionMethod** (namespace `MaxioAdvancedBilling.Models.Enums`):
- `Automatic (automatic)`
- `Remittance (remittance)`
- `Prepaid (prepaid)`
- `Invoice (invoice)`

### Client construction & DI

**Root namespace:** `MaxioAdvancedBilling`

**Client class:** `MaxioAdvancedBillingClient`

**Constructor:** `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`

**Options class:** `MaxioAdvancedBillingClientOptions` (namespace `MaxioAdvancedBilling`, source `MaxioAdvancedBillingClientOptions.cs`)

**Auth:** HTTP Basic — `Username` = API key (from config `Maxio:ApiKey`), `Password` = literal `"x"`.

**BasicAuthCredentials** (namespace `MaxioAdvancedBilling.Core.Authentication.Basic`, source `Core/Authentication/Basic/BasicAuthCredentials.cs`): `Username: string`, `Password: string`

**Environment:** `ServerEnvironment.Us` (default; US-hosted).

**Server override (if needed):** `options.Server.Production.Us.Site = "cp-exp-1"` (the sandbox site subdomain from config `Maxio:Subdomain`)

**DI registration alternative:**
```csharp
services.AddMaxioAdvancedBillingClient(o =>
{
    o.BasicAuth = new BasicAuthCredentials { Username = "<api_key>", Password = "x" };
});
// Access via IMaxioAdvancedBillingClient from DI
```

---

## Trap notes

- **Step 2 & 3 (client registration)** — The SDK's retry/timeout options (`RetryOptions`) do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register. The `Timeout` option is per-attempt, not total, and `MaxRetries = 0` is rejected (floor is 1). **MUST load `dotnet-configuration-resilience`** before wiring the client.

- **Step 1 (client registration & DI)** — The `HttpClient`/handler pipeline must be long-lived and reused via `IHttpClientFactory`, not rebuilt per request. The SDK client wrapper over it may be transient (or reused). **MUST load `dotnet-client-initialization`** before constructing the client or using DI.

- **Step 2 (authentication)** — Credentials must be set before constructing the client or in the DI callback. Maxio uses HTTP Basic where the password is always the literal string `"x"`. **MUST load `dotnet-authentication`** before setting credentials.

- **Step 3 (calling endpoints & building request bodies)** — **All operations are throw-only; there is no no-throw `…Result` variant.** Most request parameters have no C# default and mis-bind in positional calls; use named arguments for optional params. **MUST load `dotnet-calling-endpoints`** before the first call to an SDK operation.

- **Step 4 (request/response models & enums)** — Response types wrap their payload in one field (e.g., `ProductResponse.Product`, `SubscriptionResponse.Subscription`) — reads go one level down. Enums are `StringEnum<T>` not C# enums; build with `Type.FromValue("wire")` or static members. `Required` fields on records must be set in the object initializer. **MUST load `dotnet-models`** when first building a request or reading a response.

- **Step 5 (error handling — CRITICAL)** — **Two JsonException routes exist:**
  - A drifted or malformed **2xx** body (missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
  - A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

  **MUST load `dotnet-error-handling`** before writing the error boundary. These rows belong in the FIRST sheet, not a later revision: the boundary is written early, and a caveat that arrives afterwards arrives too late to shape it.

- **Step 3 (customer creation)** — The `CreateCustomer` operation Notes state that if `reference` is provided, it must be unique and represents a unique identifier from your app (e.g., user ID). The operation does **not** enforce idempotency by reference; calling it twice with the same reference will fail 422. Use `ReadCustomerByReference` to check before creating, or handle the 422 error case. **MUST load `dotnet-error-handling`** and read the operation Notes.

- **Step 3 (product/plan selection)** — `ListProductsForProductFamily` returns an `IReadOnlyList<ProductResponse>`. Each product's price is in `Product.PriceInCents` (divide by 100 for USD display), interval and unit define the billing cycle. Filter by `!ArchivedAt.HasValue` to exclude archived products. **MUST load `dotnet-models`** to understand the response shape.

---

## REQUIRED READING

Before implementation starts, load these companion skills in order. The sheet deliberately does not carry their contents — the companion skills are the usage layer on top of the SDK signatures and are essential reading:

| Skill | Step(s) |
|---|---|
| `dotnet-client-initialization` | Client & DI setup, HttpClient lifetime |
| `dotnet-authentication` | Setting Basic Auth credentials |
| `dotnet-calling-endpoints` | Calling SDK operations, parameter binding, async/await |
| `dotnet-models` | Request/response field names, unions, enums, immutable records |
| `dotnet-error-handling` | **Exception boundary, Case A/B error accessors, JsonException routes** |
| `dotnet-configuration-resilience` | Retry/timeout tuning, per-attempt vs. total bounds |

---

## Assumptions & Blockers

**Assumptions:**
- The JWT authentication on the endpoints is already in place and will extract the user's identity (ID or reference) from the token.
- The Maxio site sandbox credentials are provisioned and the product family `eshop-subscribe` (ID 3023074), plans `eshop-pro`/`basic-plan` (IDs 7126957/7126958), and component `api-call` (ID 3057195) already exist on the `cp-exp-1` site.
- No payment method is required to create a subscription (the Scope notes this; omit `PaymentProfileAttributes` and `CreditCardAttributes`).
- Configuration binding key `Maxio:` is available in the ASP.NET options builder and will be wired at startup.

**Blockers:**
- None at map time. All required operations, request/response shapes, enums, and error types are defined and documented in the SDK map.
