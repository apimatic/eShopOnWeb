# eShopOnWeb Maxio Advanced Billing Integration — Contract Sheet

## Scope & Sequence

1. **Client registration & auth** — Register `MaxioAdvancedBillingClient` with HTTP Basic credentials (API key + "x")
2. **Configuration binding** — Load Maxio settings from `appsettings.json` (`Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl`)
3. **List subscription plans** — Fetch available products from `ListProducts` (Pro Plan: eshop-pro, Basic Plan: basic-plan)
4. **Ensure Maxio customer exists** — Idempotent customer creation via `ReadCustomerByReference` + `CreateCustomer` (use eShopOnWeb user ID as reference)
5. **Subscribe user to plan** — Call `CreateSubscription` with customer ID and plan handle
6. **List user's subscriptions** — Fetch subscriptions via `ListCustomerSubscriptions` or `ListSubscriptions` with customer filter

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

| Step | Controller.Method | Signature & Parameters | Request/Response | Error Case | Pagination | Source |
|------|-------------------|------------------------|-----------------|-----------|-----------|--------|
| **Step 1: List subscription plans** | `Products.ListProducts` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` · **must pass explicitly**: `dateField`, `filter`, `endDate`, `endDatetime`, `startDate`, `startDatetime`, `includeArchived`, `include` (pass `null` to skip); defaults: `page` = 1, `perPage` = 20 | **Request**: query params (wire ← C#): `date_field` ← `dateField`, `filter` ← `filter`, `end_date` ← `endDate`, `end_datetime` ← `endDatetime`, `start_date` ← `startDate`, `start_datetime` ← `startDatetime`, `page` ← `page`, `per_page` ← `perPage`, `include_archived` ← `includeArchived`, `include` ← `include` · **Response**: `IReadOnlyList<ProductResponse>` · Envelope: `ProductResponse` → `Product (product): Product !req` · `Product` fields: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?` | **Case B** (raw error) · `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` | Manual `page`+`perPage` | `operations/Products.md` |
| **Step 2: Ensure Maxio customer exists (read)** | `Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` · **must pass explicitly**: `reference` | **Request**: query param `reference` ← `reference` · **Response**: `CustomerResponse` · Envelope: `CustomerResponse` → `Customer (customer): Customer !req` · `Customer` fields: `Id (id): int?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, `Organization (organization): string?`, `Address (address): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?` | **Case B** (raw error) · `StatusCode: HttpStatusCode` · `ReadAsString(): string` · On 404: customer not found, proceed to create | None | `operations/Customers.md` |
| **Step 3: Ensure Maxio customer exists (create)** | `Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` · **must pass explicitly**: `body` | **Request**: `CreateCustomerRequest` → `Customer (customer): CreateCustomer !req` · `CreateCustomer` required (`!req`) fields: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req` · Optional fields: `Reference (reference): string?`, `Organization (organization): string?`, `Address (address): string?`, `Address2 (address_2): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `Locale (locale): string?`, `VatNumber (vat_number): string?`, `TaxExempt (tax_exempt): bool?`, `TaxExemptReason (tax_exempt_reason): string?`, `CcEmails (cc_emails): string?`, `ParentId (parent_id): int?`, `SalesforceId (salesforce_id): string?` · **Response**: `CustomerResponse` · Envelope: `CustomerResponse` → `Customer (customer): Customer !req` | **Case A (typed)** · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · Fallback: `TryGetRawError(out RawError)` · Error 422 payload: `CustomerErrorResponse1` → `Errors (errors): Errors?` | None | `operations/Customers.md` |
| **Step 4: Subscribe user to plan** | `Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` · **must pass explicitly**: `body` | **Request**: `CreateSubscriptionRequest` → `Subscription (subscription): CreateSubscription !req` · `CreateSubscription` key fields: `ProductHandle (product_handle): string?` OR `ProductId (product_id): int?`, `CustomerId (customer_id): int?` OR `CustomerAttributes (customer_attributes): CustomerAttributes?`, `Reference (reference): string?` (idempotent key), `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `ReceivesInvoiceEmails (receives_invoice_emails): string?`, `CouponCode (coupon_code): string?`, `CouponCodes (coupon_codes): IReadOnlyList<string>?`, `NextBillingAt (next_billing_at): DateTimeOffset?`, `Components (components): IReadOnlyList<CreateSubscriptionComponent>?`, `Metafields (metafields): IReadOnlyDictionary<string, string>?` · **Response**: `SubscriptionResponse` · Envelope: `SubscriptionResponse` → `Subscription (subscription): Subscription?` · `Subscription` fields: `Id (id): int?`, `State (state): SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `ActivatedAt (activated_at): DateTimeOffset?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, `Customer (customer): Customer?`, `Product (product): Product?`, `PaymentType (payment_type): string?`, `Reference (reference): string?` | **Case A (typed)** · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · Fallback: `TryGetRawError(out RawError)` · Error 422 payload: `ErrorListResponse1` → `Errors (errors): IReadOnlyList<string> !req` | None | `operations/Subscriptions.md` |
| **Step 5: List user's subscriptions** | `Subscriptions.ListSubscriptions` | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` · **must pass explicitly**: first 14 params (pass `null` to skip); defaults: `page` = 1, `perPage` = 20 | **Request**: query params (wire ← C#): `page` ← `page`, `per_page` ← `perPage`, `state` ← `state`, `product` ← `product`, `product_price_point_id` ← `productPricePointId`, `coupon` ← `coupon`, `coupon_code` ← `couponCode`, `date_field` ← `dateField`, `start_date` ← `startDate`, `end_date` ← `endDate`, `start_datetime` ← `startDatetime`, `end_datetime` ← `endDatetime`, `metadata` ← `metadata`, `direction` ← `direction`, `sort` ← `sort`, `include` ← `include` · **Response**: `IReadOnlyList<SubscriptionResponse>` · Envelope per item: `SubscriptionResponse` → `Subscription (subscription): Subscription?` | **Case B** (raw error) · `StatusCode: HttpStatusCode` · `ReadAsString(): string` | Manual `page`+`perPage` | `operations/Subscriptions.md` |
| **Step 5 (alt): List customer's subscriptions** | `Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | **Request**: path param `customer_id` ← `customerId` · **Response**: `IReadOnlyList<SubscriptionResponse>` · Envelope per item: `SubscriptionResponse` → `Subscription (subscription): Subscription?` | **Case B** (raw error) · `StatusCode: HttpStatusCode` · `ReadAsString(): string` | None (all at once) | `operations/Customers.md` |

### Enum Values

**SubscriptionState** (returned in `Subscription.State`, namespace `MaxioAdvancedBilling.Models.Enums`):
- `Active (active)`, `AwaitingSignup (awaiting_signup)`, `Canceled (canceled)`, `CancellationRejected (cancellation_rejected)`, `Churned (churned)`, `Dormant (dormant)`, `DunningFailed (dunning_failed)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `PendingResume (pending_resume)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)`

**SubscriptionStateFilter** (filter param for `ListSubscriptions`, namespace `MaxioAdvancedBilling.Models.Enums`):
- `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)`

**IntervalUnit** (returned in `Product.IntervalUnit`, namespace `MaxioAdvancedBilling.Models.Enums`):
- `Day (day)`, `Month (month)`, `Week (week)`, `Year (year)`

**CollectionMethod** (in subscription payload, namespace `MaxioAdvancedBilling.Models.Enums`):
- `Automatic (automatic)`, `Invoice (invoice)`, `Remittance (remittance)`

### Client Construction, Auth & Server Configuration

**Client registration** (no DI shown; use `dotnet-client-initialization` for full setup):
```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers; // ServerEnvironment

var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials { Username = "<api_key>", Password = "x" },
    Environment = ServerEnvironment.Us, // or ServerEnvironment.Eu
    Server = new ServerOptions
    {
        Production = new ProductionOptions
        {
            Us = new ServerOptNode { Site = "<subdomain>" } // e.g. "cp-exp-1"
        }
    }
};
var client = new MaxioAdvancedBillingClient(httpClient, options);
```

**Namespaces to add to your files**:
- `using MaxioAdvancedBilling;` (client root)
- `using MaxioAdvancedBilling.Api;` (controller accessors)
- `using MaxioAdvancedBilling.Models;` (records: `CreateCustomerRequest`, `CreateSubscriptionRequest`, etc.)
- `using MaxioAdvancedBilling.Models.Enums;` (enums: `SubscriptionState`, `IntervalUnit`, `CollectionMethod`, etc.)
- `using MaxioAdvancedBilling.Errors;` (error types: `CreateCustomerError`, `CreateSubscriptionError`)
- `using MaxioAdvancedBilling.Core.Authentication.Basic;` (auth)
- `using MaxioAdvancedBilling.Servers;` (environments)

---

## Trap Notes

⚠ **Step 1 (client registration)** — The SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register. Per-attempt vs. total timeout, retry scope on transport errors, and HTTP method gating require explicit wiring. **MUST load `dotnet-configuration-resilience`** before building client options.

⚠ **Step 2–5 (calling operations)** — Call signatures have many nullable parameters with **no C# default**; positional calls will silently mis-bind them. Use named arguments (`dateField: null, filter: null, …`). **MUST load `dotnet-calling-endpoints`** before writing the first operation call.

⚠ **Step 2–3 (customer creation idempotence)** — `CreateCustomer` rejects duplicate reference values. Implement a read-before-create flow: `ReadCustomerByReference(reference)` returns 404 if not found (Case B: examine `StatusCode`). On 404, proceed to create; on 200, reuse the existing customer ID. **MUST load `dotnet-error-handling`** to correctly read the 404 status without re-throwing.

⚠ **Step 3 (error boundaries)** — `JsonException` reaches the boundary from two directions and they need opposite handling:
  - A drifted or malformed **2xx** body (missing `required` member) surfaces as `JsonException` from deserialization, **not** as `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
  - A **non-2xx** body that does not match the operation's generated error shape throws `JsonException` while the error object is being constructed, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed — a boundary that maps every `JsonException` to 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing the error boundary.

⚠ **Step 4 (subscription creation)** — Idempotent subscription creation requires a unique `Reference` field per user. Set `Reference` to a stable user ID from eShopOnWeb (e.g., user GUID or numeric ID). On duplicate, the provider returns 422 with error message; the caller should **not** retry but **check if subscription exists** before creating. **MUST load `dotnet-error-handling`** to parse 422 error details.

⚠ **Step 3 & 4 (response envelopes)** — Both `CustomerResponse` and `SubscriptionResponse` wrap their payload in a single field: `Customer` and `Subscription` respectively. Reads go one level down: `response.Customer.Customer`, `response.Subscription.Subscription` (or use implicit property accessor if the response type unpacks it). **MUST load `dotnet-models`** before handling response payloads.

⚠ **Configuration binding** — API key and subdomain must come from `appsettings.json` under `Maxio:` section (bindings: `ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl` optional override). Never hardcode credentials. **MUST load `dotnet-authentication`** for credential lifecycle and `dotnet-configuration-resilience`** for server-node URL override semantics.

---

## REQUIRED READING

**Load these companion skills before implementation starts.** The sheet deliberately does not carry their contents; each skill carries usage patterns, defaults, worked examples, and the parts a one-line note cannot express.

| Skill | Step(s) | Purpose |
|-------|---------|---------|
| `dotnet-client-initialization` | 1 | Client construction, DI registration, `HttpClient` lifecycle (long-lived reuse via `IHttpClientFactory`) |
| `dotnet-authentication` | 1, Configuration | Basic auth wiring (username = API key, password = `"x"`), credential rotation |
| `dotnet-configuration-resilience` | 1, Configuration | Retry/timeout semantics (per-attempt vs. total), HTTP method gating, server-node URL override |
| `dotnet-calling-endpoints` | 2–5 | Operation call signatures, named arguments on nullable params, cancellation token usage |
| `dotnet-models` | 2–5 | Request/response model construction, immutable records, optional field handling, envelope unwrapping |
| `dotnet-error-handling` | 2–5 | Case A (typed) vs. Case B (raw) error patterns, `TryGet…` accessors, `JsonException` vs. `SdkException` distinction, 404 handling |

---

## Assumptions & Blockers

**Assumptions:**
- eShopOnWeb user identities (email, first/last name, ID/GUID) are available at subscription time and stable for idempotent customer creation.
- The subscription reference (used for idempotence) will be the eShopOnWeb user ID or a user-stable identifier.
- The sandbox site `cp-exp-1` and plan handles (`eshop-pro`, `basic-plan`) exist and are accessible with the provided API key.
- Payment collection is not required for plan signup (per the brief: "payment method not required").
- No custom pricing or metered usage is needed for initial MVP; only fixed plans are subscribed.

**Blockers:**
- None identified. All required operations are in the map, error cases are documented, and the contract is complete.
