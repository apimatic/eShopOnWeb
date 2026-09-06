# Maxio Advanced Billing Integration Plan — eShopOnWeb Subscription Capability

## Scope & Sequence

1. **List subscription plans** — Expose product family's plans as shopper-facing DTOs. Operation: `ListProductsForProductFamily`.
2. **Create/Get Maxio customer** — Ensure a customer exists for the eShopOnWeb user, mapped by user ID (reference field). Operations: `CreateCustomer`, `ReadCustomerByReference`.
3. **Subscribe** — Create a subscription for the user to a plan, idempotent. Operation: `CreateSubscription`.
4. **Fetch user subscriptions** — List active/pending subscriptions for the logged-in user. Operation: `ListCustomerSubscriptions`.

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

| Operation | Signature | Request Model + Fields | Response Envelope + Fields | Error Case | Pagination | Source |
|---|---|---|---|---|---|---|
| **List subscription plans** (from product family) | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — `productFamilyId` (required pass, accept the config's `Maxio:ProductFamilyHandle` as the value), all date/filter params nullable (pass `null` to skip), defaults: `page` = 1, `perPage` = 20. Query params: `page`, `per_page`, `date_field`, `filter`, `start_date`, `end_date`, `start_datetime`, `end_datetime`, `include_archived`, `include`. | — (no body) | `IReadOnlyList<ProductResponse>` — unwrap each as: `ProductResponse.Product (product): Product` — read `Product.Handle`, `Product.Name`, `Product.PriceInCents` (in cents; divide by 100 for USD), `Product.Interval`, `Product.IntervalUnit`. | **Case A**: `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404 — family not found], `TryGetRawError(out RawError)` [fallback]. | Manual via `page`+`perPage`. | `operations/ProductFamilies.md` |
| **Create customer** (idempotent by reference) | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` must pass explicitly. | `CreateCustomerRequest { Customer (customer): CreateCustomer !req }`. **CreateCustomer fields (all in `MaxioAdvancedBilling.Models` namespace)**: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?` (wire: `reference` — use eShopOnWeb user ID here; omit to let Maxio generate). Other optional fields: `CcEmails`, `Organization`, `Address`, `Address2`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `VatNumber`, `TaxExempt`, `TaxExemptReason`, `ParentId`, `SalesforceId`. | `CustomerResponse { Customer (customer): Customer !req }` — read `Customer.Id` (Maxio-assigned), `Customer.Reference` (your reference value, if sent), `Customer.Email`, `Customer.FirstName`, `Customer.LastName`. | **Case A**: `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422 — validation error; check if reference is duplicate], `TryGetRawError(out RawError)` [fallback]. | — | `operations/Customers.md` |
| **Read customer by reference** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — `reference` (eShopOnWeb user ID) must pass explicitly. Query param: `reference` ← `reference`. | — (no body) | `CustomerResponse { Customer (customer): Customer !req }` — read `Customer.Id`, `Customer.Reference`. | **Case B**: `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. (404 if not found.) | — | `operations/Customers.md` |
| **Create subscription** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must pass explicitly. | `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription !req }`. **CreateSubscription fields (all in `MaxioAdvancedBilling.Models`)**: `ProductHandle (product_handle): string?` or `ProductId (product_id): int?`, `CustomerId (customer_id): int?` or `CustomerReference (customer_reference): string?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` (enum — optional; set to `invoice` if needed — see enums.md for `CollectionMethod` values), `CouponCode (coupon_code): string?`, `CouponCodes (coupon_codes): IReadOnlyList<string>?`, `Reference (reference): string?` (optional: idempotency key; use eShopOnWeb subscription ID). Other optional: `NextBillingAt`, `InitialBillingAt`, `PaymentProfileId`, `Components`, `CustomerAttributes` (if creating customer inline), `DeferSignup`, etc. **Notes from operation**: payment method not required per sandbox config. | `SubscriptionResponse { Subscription (subscription): Subscription? }` — read `Subscription.Id`, `Subscription.State` (enum `SubscriptionState`), `Subscription.ProductId`, `Subscription.CustomerId`, `Subscription.NextAssessmentAt`, `Subscription.CurrentPeriodStartedAt`, `Subscription.CurrentPeriodEndsAt`, `Subscription.ActivatedAt`, `Subscription.CanceledAt`, `Subscription.Reference`, `Subscription.BalanceInCents`. | **Case A**: `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422 — validation error; e.g. plan not found, customer not found, duplicate reference], `TryGetRawError(out RawError)` [fallback]. | — | `operations/Subscriptions.md` |
| **List customer subscriptions** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — `customerId` (Maxio customer ID from `ReadCustomerByReference` or `CreateCustomer` response) required. | — (no body) | `IReadOnlyList<SubscriptionResponse>` — each response's `Subscription (subscription): Subscription?` — read same fields as Create response. | **Case B**: `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, etc. (404 if customer not found.) | — | `operations/Customers.md` |

### Enums (wire values from `map/models/enums.md`)

**SubscriptionState** (values by wire name — build with `SubscriptionState.FromValue("wire_value")`):
- `active`, `assigning`, `pending`, `paused`, `past_due`, `canceled`, `expired`, `on_hold`, `trialing`, `trial_ended`, `awaiting_signup`, `awaiting_payment`

**CollectionMethod** (wire values):
- `automatic`, `remittance`, `invoice`

**IntervalUnit** (wire values — for products):
- `day`, `week`, `month`, `year`

### Client Construction & Auth

**Namespace for root + options**: `MaxioAdvancedBilling`, `MaxioAdvancedBilling.Servers` (for `ServerEnvironment`).
**Namespace for types**: `MaxioAdvancedBilling.Models`, `MaxioAdvancedBilling.Models.Enums`, `MaxioAdvancedBilling.Errors`.

**Auth**: HTTP Basic. `BasicAuthCredentials { Username = MAXIO_API_KEY, Password = "x" }` (password is literal "x").

**Server**: `ServerEnvironment.Us` (default, US-hosted). Base URL template: `https://{site}.chargify.com` (Production group). Set `options.Server.Production.Us.Site = subdomain_from_config` or override `BaseUrl` entirely via config if present.

---

## Trap Notes

- ⚠ **Step 1 (client DI)** — The SDK wraps an `HttpClient` you must provide long-lived via `IHttpClientFactory` or register in DI. The SDK client itself may be transient, but the underlying `HttpClient` policy and handler pipeline must not be rebuilt per request. **MUST load `dotnet-client-initialization`** to understand the DI wiring and the HttpClient lifecycle.

- ⚠ **Step 2 (authentication)** — Credentials must be set before client construction (in options) or via DI callback. Do **not** hardcode the API key. Bind it from configuration: `Maxio:ApiKey` (from env `MAXIO_API_KEY`). The password is the literal string `"x"`, not a placeholder. **MUST load `dotnet-authentication`** before wiring credentials.

- ⚠ **Step 3 (calling endpoints)** — Many optional parameters on operations have no C# default; pass `null` explicitly to skip them. For `ListProductsForProductFamily`, all date/filter params are nullable — pass `null` to ignore. Do not rely on positional arguments for optional params. **MUST load `dotnet-calling-endpoints`** for calling best practices.

- ⚠ **Step 4 (request/response models)** — Response types wrap their payload in one field: `ProductResponse.Product`, `SubscriptionResponse.Subscription`, `CustomerResponse.Customer`. Unwrap one level to read the actual object. Enums are **not** C# `enum`; build with `.FromValue("wire")` or use the static member names listed in enums.md (e.g. `SubscriptionState.Active`, not `SubscriptionState.active`). **MUST load `dotnet-models`** to understand union construction, enum build, and nullable field handling.

- ⚠ **Step 5 (error handling)** — Operations use throw-based error reporting. `CreateCustomer` and `CreateSubscription` are **Case A** (typed `{Operation}Error` with `TryGet…` accessors); `ListProductsForProductFamily`, `ReadCustomerByReference`, `ListCustomerSubscriptions` are **Case B** (raw `RawError`). Always wrap calls in `try/catch(SdkException<TError>)`. **MUST load `dotnet-error-handling`** before writing any error boundary. **Critical**: Deserialization of a malformed 2xx body raises `JsonException` (not `SdkException`) — a boundary that catches only `SdkException` will let it escape; a boundary that maps every `JsonException` to 5xx then retries on 5xx retries something that can never succeed. A **non-2xx** body that does not match the typed error shape throws `JsonException` *while constructing the error object*, destroying the HTTP status. Load the skill to understand both cases.

- ⚠ **Step 6 (configuration & resilience)** — `Timeout` bounds **per-attempt**, not the whole call. A transport failure (`HttpRequestException`) is retried on **every** HTTP verb including POST (non-idempotent writes can execute multiple times; no setting disables that — `MaxRetries = 0` is rejected). Idempotency is your caller's responsibility: subscribe operations should send a `reference` (idempotency key) to guard double-clicks. **MUST load `dotnet-configuration-resilience`** before configuring timeouts, retries, or base URL overrides.

---

## REQUIRED READING

Load these companion skills **before implementation starts**. The sheet deliberately does not carry their contents; each skill carries the gotchas, worked examples, and configuration defaults a one-liner cannot hold.

| Skill | Step(s) it governs |
|---|---|
| `dotnet-client-initialization` | Client & DI setup; HttpClient lifecycle. |
| `dotnet-authentication` | Credential binding; auth scheme. |
| `dotnet-calling-endpoints` | Calling operations; optional parameter passing; async/cancellation. |
| `dotnet-models` | Request/response model shape; unwrapping envelope fields; union construction; enum building; nullability. |
| `dotnet-error-handling` | Error boundary; `SdkException<TError>` vs `RawError`; `TryGet…` accessors; the two `JsonException` cases and their handling. |
| `dotnet-configuration-resilience` | Timeout semantics (per-attempt); retry gates; base URL override; idempotency. |

**Important**: The SDK throws `JsonException` in two scenarios that belong to different error-handling paths:
1. A drifted or malformed **2xx** body (missing `required` member) surfaces as `JsonException` from deserialization, **not** as `SdkException` — an SDK-exception-only catch ladder lets it escape the integration boundary.
2. A **non-2xx** body that does not match the operation's generated `{Operation}Error` shape throws `JsonException` *while constructing the error object*, **replacing** the `SdkException` — the HTTP status is destroyed with it. A boundary that maps every `JsonException` to 5xx then retries 5xx will retry something that can never succeed.

Load **`dotnet-error-handling`** before writing the error boundary. These two rows belong in the FIRST sheet, not a later revision: the boundary is written early.

---

## Assumptions & Blockers

### Assumptions

1. The `ProductFamilyHandle` (from config `Maxio:ProductFamilyHandle`) is stable and correctly identifies the seeded product family on the Maxio site.
2. Products within that family are stable (handles remain consistent across site re-seeding, per task note on stable handles).
3. Customer references are unique per eShopOnWeb user (one user ↔ one Maxio customer by reference).
4. Subscription references (idempotency keys) are unique per eShopOnWeb subscription intent (one user + plan + signup time ↔ one Maxio subscription by reference).
5. The Maxio site's payment-collection method is configured to allow non-payment-required subscriptions (per sandbox config: "payment method not required").
6. All operations are called with a valid, non-expired API key bound from configuration.

### Blockers

None identified. The Maxio API surface covers all four operations; the SDK is published and available via NuGet. Configuration binding, error handling, and the HTTP pipeline are application concerns, not SDK blockers.
