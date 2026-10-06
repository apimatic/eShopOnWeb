# maxio-plan.md — Recurring subscriptions on eShopOnWeb via Maxio Advanced Billing (.NET SDK)

## 1. Scope & sequence

Additive/parallel capability: JWT-authenticated endpoints on `src/PublicApi` under `/api/`.
Maxio = billing system of record. Package `AsadAli.AdvancedBilling.Sdk`, root namespace
`MaxioAdvancedBilling`, target the sandbox (US hosting, `ServerEnvironment.Us`).

| # | Step | SDK operations used |
|---|---|---|
| 1 | Config binding + DI client registration | `AddMaxioAdvancedBillingClient` (SDK's `ServiceCollectionExtensions`) |
| 2 | GET `/api/subscription-plans` | `Products.ReadProductByHandle` per configured plan handle (`eshop-pro`, `basic-plan`); optional family-level listing via `ProductFamilies.ListProductFamilies` + `ProductFamilies.ListProductsForProductFamily` |
| 3 | Ensure Maxio customer (idempotent) | `Customers.ReadCustomerByReference` → `Customers.CreateCustomer` |
| 4 | POST `/api/subscriptions` (idempotent) | `Subscriptions.FindSubscription` (404 check) → `Subscriptions.CreateSubscription` |
| 5 | GET `/api/my-subscriptions` | `Customers.ListCustomerSubscriptions` (optionally `Subscriptions.ReadSubscription`) |
| 6 | Record metered usage (`api-call`, $0.01/unit) | `Components.FindComponent` → `SubscriptionComponents.CreateUsage` (read-back via `SubscriptionComponents.ListUsages`) |
| 7 | Error boundary | per-operation Case A/B handling, 404 distinguished from other errors (see §2 error model + §3 trap notes) |

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal
> C# identifier. The cancellation-token parameter really is named `ct`: in named
> arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take
> each one from that type's own map row, never from where a neighbouring type sits. A members
> table names the namespace outright; otherwise the row's source path implies it
> (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root
> namespace). Enums, unions, auth, server and client-config types are spread across different
> child namespaces, and two types configured side by side in the same options object routinely
> live in different ones. Dropping a type to the root or to `.Models` makes the implementer
> guess the wrong `using`, and the build breaks.

General rules from the map: all operations are **throw-only** (no `…Result` variants); every
`body`-like parameter marked "nullable, no default" **must be passed explicitly** (pass `null`
to skip); records are immutable with `init`-only setters; `!req` = C# `required` (must be set
in the object initializer); a trailing `?` = nullable/optional. Every SDK fact below cites its
map page.

### 2.1 Client construction / registration (source: `sdk-map.md` "Getting a client", "Servers & auth")

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;   // BasicAuthCredentials
using MaxioAdvancedBilling.Servers;                     // ServerEnvironment

var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials
    {
        Username = <"Maxio:ApiKey" value>,   // username = API key
        Password = "x"                       // password = the literal string "x"
    },
    Environment = ServerEnvironment.Us       // US-hosted sandbox; EU only for EU-hosted accounts
};
// Base URL:
//   Maxio:BaseUrl set  → options.Server.Production.Us.BaseUrl = <value, used verbatim>
//   otherwise          → options.Server.Production.Us.Site   = <"Maxio:Subdomain" value>
//                        (US template resolves to https://{site}.chargify.com, {site} = subdomain)
var client = new MaxioAdvancedBillingClient(httpClient, options); // only ctor: (HttpClient, MaxioAdvancedBillingClientOptions)
```

| Fact | Value | Source |
|---|---|---|
| Client class | `MaxioAdvancedBillingClient` (root ns `MaxioAdvancedBilling`), sole ctor `(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` |
| Options properties | `Environment: ServerEnvironment` · `Retry: RetryOptions` · `Server: ServerOptions` · `BasicAuth: BasicAuthCredentials?` | `sdk-map.md` |
| DI registration | `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = …; o.Environment = …; o.Server… })` | `sdk-map.md` |
| Config binding keys | `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` (BaseUrl optional override, used verbatim when set; otherwise derive host from `Maxio:Subdomain` via `options.Server.Production.Us.Site`) | task brief + `sdk-map.md` server override points |
| Every API group is a property | `client.Customers`, `client.Subscriptions`, `client.Products`, `client.ProductFamilies`, `client.Components`, `client.SubscriptionComponents` | `sdk-map.md` |
| Auth | HTTP Basic only; **Username = API key, Password = literal `"x"`** | `sdk-map.md` Servers & auth |

### 2.2 Operations

Signature notation: `Op(param: type, …) : returnType`. "must-pass-explicitly" params are
repeated in every call (named args recommended).

| # | Controller · method | Signature (params in order) | Returns | Error case + accessors | Pagination | Source |
|---|---|---|---|---|---|---|
| 1 | `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | `MaxioAdvancedBilling.Models.CustomerResponse` | **Case B** `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` — 404 ⇒ `ex.Error.StatusCode == HttpStatusCode.NotFound`; else `ex.Error.ReadAsString()` / `ReadAsJson<T>()` | none | `operations/Customers.md` |
| 2 | `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` must-pass-explicitly | `CustomerResponse` | **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Customers.md` |
| 3 | `client.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | `IReadOnlyList<SubscriptionResponse>` | **Case B** `SdkException<RawError>` | none (all subscriptions returned) | `operations/Customers.md` |
| 4 | `client.Customers.ReadCustomer` | `ReadCustomer(int id, CancellationToken ct = default)` | `CustomerResponse` | **Case B** `SdkException<RawError>` | none | `operations/Customers.md` |
| 5 | `client.Products.ReadProductByHandle` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | `MaxioAdvancedBilling.Models.ProductResponse` | **Case B** `SdkException<RawError>` (unknown handle ⇒ 404 via `StatusCode`) | none | `operations/Products.md` |
| 6 | `client.Products.ListProducts` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 8 nullable params have **no default** and must be passed explicitly (pass `null` to skip) | `IReadOnlyList<ProductResponse>` | **Case B** `SdkException<RawError>` | manual `page`+`perPage` | `operations/Products.md` |
| 7 | `client.ProductFamilies.ListProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 must-pass-explicitly | `IReadOnlyList<ProductFamilyResponse>` | **Case B** `SdkException<RawError>` | none | `operations/ProductFamilies.md` |
| 8 | `client.ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — note `productFamilyId` is a **string** here | `IReadOnlyList<ProductResponse>` | **Case A** `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` | manual `page`+`perPage` | `operations/ProductFamilies.md` |
| 9 | `client.Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must-pass-explicitly | `MaxioAdvancedBilling.Models.SubscriptionResponse` | **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` | none | `operations/Subscriptions.md` |
| 10 | `client.Subscriptions.FindSubscription` | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` must-pass-explicitly | `SubscriptionResponse` | **Case A** `SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`: `TryGetNoContent(out RawError)` [**404**] · `TryGetRawError(out RawError)` | none | `operations/Subscriptions.md` |
| 11 | `client.Subscriptions.ReadSubscription` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` must-pass-explicitly (pass `null`) | `SubscriptionResponse` | **Case B** `SdkException<RawError>` | none | `operations/Subscriptions.md` |
| 12 | `client.Components.FindComponent` | `FindComponent(string handle, CancellationToken ct = default)` — lookup by handle (`api-call`) | `MaxioAdvancedBilling.Models.ComponentResponse` | **Case B** `SdkException<RawError>` (unknown handle ⇒ 404 via `StatusCode`) | none | `operations/Components.md` |
| 13 | `client.SubscriptionComponents.CreateUsage` | `CreateUsage(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, CreateUsageRequest? body, CancellationToken ct = default)` — `body` must-pass-explicitly | `MaxioAdvancedBilling.Models.UsageResponse` | **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateUsageError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` | none | `operations/SubscriptionComponents.md` |
| 14 | `client.SubscriptionComponents.ListUsages` | `ListUsages(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, long? sinceId, long? maxId, DateTimeOffset? sinceDate, DateTimeOffset? untilDate, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 4 nullable params must-pass-explicitly | `IReadOnlyList<UsageResponse>` | **Case B** `SdkException<RawError>` | manual `page`+`perPage` | `operations/SubscriptionComponents.md` |

`SubscriptionIdOrReference` and `ComponentIdModel` are **AnyOf unions**
(`MaxioAdvancedBilling.Models.AnyOf`): construct with `SubscriptionIdOrReference.Int(int)` /
`.String(string)` and `ComponentIdModel.Int(int)` / `.String(string)`; read via
`TryGetInt(out …)` / `TryGetString(out …)`. Source: `map/models/unions.md`.

### 2.3 Request models to construct

All records live in `MaxioAdvancedBilling.Models`; fields are `CSharpName (wire_name): Type`.

**`CreateCustomerRequest`** — `operations/Customers.md`, `models/records-1-Ac-Cr.md`
| Field | Type |
|---|---|
| `Customer (customer): CreateCustomer` | **required** — the only field |

`CreateCustomer` (the inner payload):
| Field | Type |
|---|---|
| `FirstName (first_name)` | `string` **required** |
| `LastName (last_name)` | `string` **required** |
| `Email (email)` | `string` **required** |
| `Reference (reference)` | `string?` — **the idempotency key: set it to the stable eShopOnWeb user reference** |
| `Organization`, `Phone`, `Locale`, `Address`, `Address2`, `City`, `State`, `Zip`, `Country`, `CcEmails`, `VatNumber` | `string?` |
| `TaxExempt (tax_exempt)` | `bool?` |

Note (map Notes, CreateCustomer): **"you may only create one customer for a given reference
value"** — a duplicate-reference create is rejected (422 ⇒ `TryGetCustomerErrorResponse1`).
That rejection is the race-condition backstop behind the lookup-first flow.

**`CreateSubscriptionRequest`** — `operations/Subscriptions.md`, `models/records-2-Cr-Ne.md`
| Field | Type |
|---|---|
| `Subscription (subscription): CreateSubscription` | **required** — the only field |

`CreateSubscription` — fields this integration sets (all others are optional and unused):
| Field | Type | Why |
|---|---|---|
| `ProductHandle (product_handle)` | `string?` — OR `ProductId (product_id): int?`; identify the plan by handle (`eshop-pro` / `basic-plan`) | CreateSubscription Notes: "Specify the product with `product_id` or `product_handle`" |
| `CustomerId (customer_id)` | `int?` — OR `CustomerReference (customer_reference): string?` | Notes: "Identify an existing customer with `customer_id` or `customer_reference`" |
| `Reference (reference)` | `string?` — set a stable per-user key (e.g. derived from user id + plan) so `FindSubscription` can implement idempotency | Notes: FindSubscription "Finds a subscription by its reference" |
| `ProductPricePointHandle (product_price_point_handle)` | `string?` — omit ⇒ the product's default price point is used | Notes |
| **Payment fields — omit all of them** | `PaymentProfileId: int?`, `CreditCardAttributes: PaymentProfileAttributes?`, `PaymentProfileAttributes: PaymentProfileAttributes?`, `BankAccountAttributes: BankAccountAttributes?` — all nullable/optional | field list; Notes: "Payment information **may** be required … depending on the options for the Product being subscribed". ⚠ **LIVE-VERIFIED (sandbox cp-exp-1, `eshop-pro`, 2026-10): omission alone is NOT sufficient** — the provider returned 422 with `Errors` = ["No payment method was on file for the $299.00 balance"] (see §5 B1) |
| `PaymentCollectionMethod (payment_collection_method)` | `MaxioAdvancedBilling.Models.Enums.CollectionMethod?` (`Automatic`, `Remittance`, `Prepaid`, `Invoice`) — **set `CollectionMethod.Remittance` for the payment-less path** | optional; enum summary (`models/enums.md`): "For legacy Statements Architecture valid options are - `invoice`, `automatic`. For current Relationship Invoicing Architecture valid options are - `remittance`, `automatic`, `prepaid`" — **do not send `Invoice`** (legacy-only; itself invalid under RIA). Whether `Remittance` is accepted payment-less is `UNVERIFIED` until re-tested live |
| `NetTerms (net_terms)` / `ReceivesInvoiceEmails (receives_invoice_emails)` | `string?` / `string?` — remittance-style billing companions on the create payload; leave unset unless the app wants invoice-email behaviour | field list (`models/records-2-Cr-Ne.md`) |

**`CreateUsageRequest`** — `operations/SubscriptionComponents.md`, `models/records-2-Cr-Ne.md`
| Field | Type |
|---|---|
| `Usage (usage): CreateUsage` | **required** — the only field |

`CreateUsage` (inner):
| Field | Type |
|---|---|
| `Quantity (quantity)` | `double?` — usage units (negative quantity deducts; map Notes) |
| `Memo (memo)` | `string?` |
| `PricePointId (price_point_id)` | `string?` — omit ⇒ component's default price point ($0.01/unit) applies |
| `BillingSchedule`, `CustomPrice` | optional, unused here |

### 2.4 Response models to read

**`CustomerResponse`** — one field: `Customer (customer): Customer` **required**
(`models/records-1-Ac-Cr.md`). Read `Customer.Id (id): int?`, `Customer.Reference (reference): string?`,
`Customer.Email (email): string?`. Response envelopes wrap their payload one level down —
always read `.Customer`, never treat the response as the customer.

**`SubscriptionResponse`** — one field: `Subscription (subscription): Subscription?`
(**nullable** — null-check before use; `models/records-4-Su-We.md`).

`Subscription` — fields this integration reads (`models/records-3-Of-Su.md`):
| Field | Type | Meaning for this task |
|---|---|---|
| `Id (id)` | `int?` | Maxio subscription id (store it) |
| `Reference (reference)` | `string?` | the per-user idempotency key sent at create |
| `State (state)` | `MaxioAdvancedBilling.Models.Enums.SubscriptionState?` | plan/state — enum below |
| `Customer (customer)` | `Customer?` | wrapped customer (read `.Id`) |
| `Product (product)` | `Product?` | **plan + price live here** — see `Product` below |
| `ProductPriceInCents (product_price_in_cents)` | `long?` | recurring product price in cents |
| `CurrentBillingAmountInCents (current_billing_amount_in_cents)` | `long?` | total current billing amount in cents |
| `CurrentPeriodEndsAt (current_period_ends_at)` | `DateTimeOffset?` | **next-billing-date read**: the current period ends here (next renewal/billing date); `NextAssessmentAt (next_assessment_at): DateTimeOffset?` is also carried |
| `PaymentCollectionMethod (payment_collection_method)` | `CollectionMethod?` | |
| `ProductPricePointId (product_price_point_id)` / `ProductPricePointType (product_price_point_type): PricePointType?` | | price-point categorization on the subscription |
| `ActivatedAt`, `CanceledAt`, `CancelAtEndOfPeriod` | | lifecycle |

`Product` (inner, `models/records-3-Of-Su.md`):
| Field | Type |
|---|---|
| `Id (id)` | `int?` |
| `Name (name)`, `Handle (handle)` | `string?` |
| `PriceInCents (price_in_cents)` | `long?` — plan price (e.g. 29900 / 2900) |
| `Interval (interval): int?` / `IntervalUnit (interval_unit): IntervalUnit?` | billing period (`IntervalUnit`: `Day (day)`, `Month (month)`) |
| `RequestCreditCard (request_credit_card): bool?` / `RequireCreditCard (require_credit_card): bool?` | read these to confirm the plan does not demand a payment profile (see §5) |
| `ProductFamily (product_family): ProductFamily?` | family of the plan (`ProductFamily.Handle` / `.Id`) |
| `ProductPricePointId` / `ProductPricePointHandle` / `ProductPricePointName` | default price point identifiers |
| `ArchivedAt (archived_at): DateTimeOffset?` | filter archived plans out of the listing |

**`ProductResponse`** — one field: `Product (product): Product` **required**.
**`ProductFamilyResponse`** — one field: `ProductFamily (product_family): ProductFamily?`
(`Id: int?`, `Name`, `Handle`, `AccountingCode`, `Description`, `ArchivedAt` — `models/records-3-Of-Su.md`).

**`ComponentResponse`** — one field: `Component (component): Component` **required**
(`models/records-1-Ac-Cr.md`). `Component` fields read here: `Id (id): int?`,
`Handle (handle): string?`, `Name`, `UnitName`, `PricePerUnitInCents (price_per_unit_in_cents): long?`
(the $0.01/unit price), `ProductFamilyId`, `ProductFamilyHandle`, `Kind (kind): ComponentKind?`,
`Archived: bool?`.

**`UsageResponse`** — one field: `Usage (usage): Usage` **required** (`models/records-4-Su-We.md`).
`Usage`: `Id (id): long?`, `Quantity (quantity): Quantity1?` (**union** — read via
`Quantity1.TryGetInt(out …)` / `.TryGetString(out …)`, `MaxioAdvancedBilling.Models.AnyOf`),
`Memo`, `CreatedAt`, `ComponentId`, `ComponentHandle`, `SubscriptionId`, `PricePointId`, `OverageQuantity`.

### 2.5 Enums (`MaxioAdvancedBilling.Models.Enums` — `map/models/enums.md`)

Enums are **not** C# enums — they are `StringEnum<T>` wrappers; build with the static members
below or `Type.FromValue("wire_value")`. `!req`-free usage: pass `.Name` instances directly.

| Enum | Members — `Member (wire_value)` |
|---|---|
| `SubscriptionState` (subscription state) | `Pending (pending)` · `FailedToCreate (failed_to_create)` · `Trialing (trialing)` · `Assessing (assessing)` · `Active (active)` · `SoftFailure (soft_failure)` · `PastDue (past_due)` · `Suspended (suspended)` · `Canceled (canceled)` · `Expired (expired)` · `Paused (paused)` · `Unpaid (unpaid)` · `TrialEnded (trial_ended)` · `OnHold (on_hold)` · `AwaitingSignup (awaiting_signup)` |
| `SubscriptionStateFilter` (list-filter state values) | `Active (active)` · `Canceled (canceled)` · `Expired (expired)` · `ExpiredCards (expired_cards)` · `OnHold (on_hold)` · `PastDue (past_due)` · `PendingCancellation (pending_cancellation)` · `PendingRenewal (pending_renewal)` · `Suspended (suspended)` · `TrialEnded (trial_ended)` · `Trialing (trialing)` · `Unpaid (unpaid)` |
| `PricePointType` (product/component price-point categorization) | `Catalog (catalog)` · `Default (default)` · `Custom (custom)` |
| `CollectionMethod` | `Automatic (automatic)` · `Remittance (remittance)` · `Prepaid (prepaid)` · `Invoice (invoice)` |
| `IntervalUnit` | `Day (day)` · `Month (month)` |
| `SortingDirection` | `Asc (asc)` · `Desc (desc)` |
| `SubscriptionInclude` (for `ReadSubscription`'s `include`) | `Coupons (coupons)` · `SelfServicePageToken (self_service_page_token)` |
| `BasicDateField` | `UpdatedAt (updated_at)` · `CreatedAt (created_at)` |
| `ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)` |

### 2.6 Error/exception model (source: `sdk-map.md` error-handling model + `Core/Exceptions/SdkException.cs` v1.0.2)

- Every operation throws; the exception is
  `MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>` — namespace
  `MaxioAdvancedBilling.Core.Exceptions`. Its **only** relevant member is `.Error` (of
  `TError`); the exception object itself carries **no** status code — read the status from the
  error object.
- **Case B** (`TError = MaxioAdvancedBilling.Core.ErrorResponse.RawError`): status via
  `ex.Error.StatusCode: HttpStatusCode`; body via `ex.Error.ReadAsString(): string`,
  `ex.Error.ReadAsJson<T>(): T?`, `ex.Error.ReadAsBytes(): ReadOnlyMemory<byte>`.
  **404 detection for Case B lookups** (ReadCustomerByReference, ReadCustomer,
  ListCustomerSubscriptions, ReadProductByHandle, ReadSubscription, FindComponent, ListUsages):
  `ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound`.
- **Case A** (`TError = MaxioAdvancedBilling.Errors.{Operation}Error`): typed accessors per
  operation (table §2.2) plus the inherited `TryGetRawError(out RawError)` fallback
  (`MaxioAdvancedBilling.Core.ErrorResponse.ApiError` base). The 404 accessor on
  FindSubscription is `TryGetNoContent(out RawError)` — it returns `true` exactly when the
  server answered 404.
- 422 payloads: `MaxioAdvancedBilling.Models.ErrorListResponse1` (field
  `Errors (errors): IReadOnlyList<string>`) for CreateSubscription / CreateUsage;
  `MaxioAdvancedBilling.Models.CustomerErrorResponse1` (field `Errors (errors): Errors?`) for
  CreateCustomer — read best-effort and fall back to `TryGetRawError` + `ReadAsString()` for
  the generic message.
- The SDK generates **no** no-throw/Result variants; always wrap the throwing call.

### 2.7 Pagination summary

| Operation | Shape |
|---|---|
| `ListProducts`, `ListProductsForProductFamily`, `ListSubscriptions`, `ListUsages` | manual `page` + `perPage` (defaults per row §2.2); no cursor/token is exposed in the map — iterate pages until a short/empty page |
| `ListCustomerSubscriptions`, `ListProductFamilies`, all read/lookup ops | none |

### 2.8 Idempotency design (SDK facts only; the surrounding concurrency is the app's call)

- **Customer**: `ReadCustomerByReference(reference)` is the exact-match lookup ("It will
  return a single match" — map Notes). Set `CreateCustomer.Customer.Reference` to the stable
  eShopOnWeb user reference; a duplicate-reference create is rejected with 422 (map Notes:
  "you may only create one customer for a given reference value"), so lookup-first + catch-422
  on create + re-lookup covers the race. Email search exists only via
  `Customers.ListCustomers(direction, dateField, startDate, endDate, startDatetime, endDatetime, q, page = 1, perPage = 50, ct)`
  (Case B, paginated; `q` searches email/reference/name) — it is a filter, not an exact
  lookup; the reference lookup is the contract-grade path.
- **Subscription**: set `CreateSubscription.Subscription.Reference` to a stable per-user key,
  then `FindSubscription(reference)` before creating — its 404 accessor
  (`TryGetNoContent`) is the "not yet subscribed" signal. (Whether Maxio itself rejects a
  duplicate subscription reference at create is **UNVERIFIED** — see §5.)

---

## 3. Trap notes (hazards — load the named skill; do not code from these one-liners)

- ⚠ Step 1 (client registration) — the `HttpClient` handed to the single
  `MaxioAdvancedBillingClient(HttpClient, options)` constructor must come from a long-lived
  factory pipeline, and handler lifetime differs from client-wrapper lifetime; getting this
  wrong silently churns connections or pins handlers. **MUST load
  `dotnet-client-initialization`** before wiring the client into DI.
- ⚠ Step 1 (credentials) — Maxio is HTTP Basic where the *username* is the API key and the
  *password* is the literal `"x"`; credentials must be in place before the client is built,
  and rotated per environment. **MUST load `dotnet-authentication`** before setting
  `BasicAuth`.
- ⚠ Step 2–6 (every call) — nullable params without C# defaults (e.g. all of
  `ListProducts`'s first 8, `ReadSubscription`'s `include`, `CreateUsage`'s `body`) must be
  passed explicitly or the call mis-binds; list/search ops are safest with named arguments,
  and the token parameter is `ct:`. **MUST load `dotnet-calling-endpoints`** before the first
  SDK call.
- ⚠ Step 2–6 (payloads) — enums are `StringEnum<T>` wrappers (not C# enums), unions
  (`SubscriptionIdOrReference`, `ComponentIdModel`, `Quantity1`) are built via static
  factories and read via `TryGet…`, response payloads sit one level down inside the envelope,
  and unmodeled JSON fields are dropped on deserialize. **MUST load `dotnet-models`** before
  constructing or reading any request/response model.
- ⚠ Step 7 (error boundary) — Case A vs Case B differ per operation (§2.2), the typed errors'
  `TryGet…` accessors are status-specific, `TryGetRawError` is not a catch-all on typed
  errors, and no Result-style variants exist. **MUST load `dotnet-error-handling`** before
  writing any try/catch around an SDK call.
- ⚠ Step 1 & 4 & 6 (resilience) — whether a failed write can be re-sent is not guaranteed by
  settings: transport failures are retried across verbs including POST, while status-triggered
  retries are verb-gated, so a create can execute more than once no matter how options are
  tuned; also what `Timeout` actually bounds (per attempt, not the whole call) and how the
  `Maxio:BaseUrl` override interacts with environment/server options. **MUST load
  `dotnet-configuration-resilience`** before tuning `RetryOptions` or wiring BaseUrl.
- ⚠ Steps 3–6 (tests) — the test seam is the `HttpClient` constructor argument; stub there,
  not inside the SDK. **MUST load `dotnet-testing`** before writing integration tests.
- ⚠ Step 7 (boundary) — the two `System.Text.Json.JsonException` directions below need
  **opposite** handling, and both reach the boundary:
  - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
    `JsonException` from deserialization, **not** as an `SdkException` — so an
    SDK-exception-only catch ladder lets it escape the integration boundary;
  - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
    throws `JsonException` *while the error object is being constructed*, so the
    `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it —
    a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection
    as an outage, and a caller that retries 5xx retries something that can never succeed.

  **MUST load `dotnet-error-handling`** before writing that boundary.

---

## 4. REQUIRED READING (load before implementation starts)

The sheet deliberately does not carry these skills' contents — each is loaded in full by the
implementer at the step it governs.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — DI registration, `HttpClient` ownership/lifetime, builder/options shape for `MaxioAdvancedBillingClient` |
| `dotnet-authentication` | Step 1 — Basic credentials (username = API key, password = `"x"`), per-environment config, rotation |
| `dotnet-calling-endpoints` | Steps 2–6 — calling conventions, must-pass-explicitly params, named args, `ct:`, envelope reads |
| `dotnet-models` | Steps 2–6 — `StringEnum<T>` construction, union factories/`TryGet…`, wire names, required members |
| `dotnet-error-handling` | Step 7 — Case A/B ladder, status/body accessors, the two `JsonException` directions |
| `dotnet-configuration-resilience` | Steps 1, 4, 6 — retry/timeout semantics, BaseUrl override, pagination, non-idempotent-write retry hazard |
| `dotnet-testing` | Steps 3–6 — faking the `HttpClient` seam, covering error/edge paths |

---

## 5. Assumptions & Blockers

**Assumptions (app-side; not SDK facts):**

- **A1.** The JWT identity on `src/PublicApi` supplies a stable user id suitable as the Maxio
  customer `reference` (and an email for the customer's `email`/display fields). The choice of
  reference value (user id vs email) is the app's; the contract only fixes that the same
  reference must be used for lookup and create.
- **A2.** The sandbox site is pre-provisioned: product family handle per `Maxio:ProductFamilyHandle`,
  products `eshop-pro` and `basic-plan`, and metered component `api-call` at $0.01/unit all
  exist under that family. No provisioning calls are planned; if a handle lookup 404s, the
  plan endpooint reports the plan as unavailable rather than creating it. (The SDK does carry
  create/provision operations — e.g. `CreateProductFamily`, `CreateProduct`,
  `CreateMeteredComponent` — if provisioning is later wanted in scope.)
- **A3.** Plan/price display data (Pro $299/mo, Basic $29/mo) is read from the Maxio product
  response (`Product.PriceInCents`, `Product.Interval`, `Product.IntervalUnit`), not hardcoded
  in the app.
- **A4.** Maxio customer first/last name fields (`CreateCustomer` marks them **required**) are
  filled from the app's user record; if the app has no name, any stable placeholder is an
  app-side decision.

**Blockers / confirm-before-coding:**

- **B1. Payment-less signup — live-verified 422; fix chosen, final acceptance `UNVERIFIED`.**
  The task brief claims the seeded plans are "payment method not required", but **live traffic
  (sandbox site cp-exp-1, product `eshop-pro`) disproved it**: `CreateSubscription` with
  `ProductHandle` + `CustomerId` + `Reference` and every payment field omitted returned **422**
  (`TryGetErrorListResponse1` → `ErrorListResponse1.Errors`) containing exactly
  **"No payment method was on file for the $299.00 balance"**. This matches the map Notes:
  "Payment information may be required to create a subscription, depending on the options for
  the Product being subscribed" — the requirement is a property of the product/site, which only
  live traffic can confirm, never the brief. **Chosen fix (§2.3):** add
  `PaymentCollectionMethod = CollectionMethod.Remittance` (the Relationship-Invoicing-valid
  manual-payment method; `Invoice` is legacy-Statements-only per the enum summary and must not
  be sent). Whether `Remittance` alone satisfies the provider without a payment profile is
  **`UNVERIFIED`** — the main agent must re-test live; if the 422 persists, the remaining
  documented paths are `PaymentProfileId` (attach an existing profile) or an inline
  `CreditCardAttributes`/`BankAccountAttributes`/`PaymentProfileAttributes` payload, and the
  site's product-level require-credit-card setting itself may need an app-side or sandbox-side
  change the SDK cannot make (product updates exist — `UpdateProduct` with
  `CreateOrUpdateProduct.RequireCreditCard` — but changing billing config is out of this
  integration's scope). `Product.RequestCreditCard` / `Product.RequireCreditCard` on the
  `ReadProductByHandle` response exist to detect the setting, but their precise semantics are
  not documented in the map (`UNVERIFIED` — field presence only, `models/records-3-Of-Su.md`).
- **B2. Duplicate subscription `reference` at create — `UNVERIFIED`.** The map documents
  `FindSubscription(reference)` lookup and customer-reference uniqueness, but nowhere states
  whether `CreateSubscription` rejects a second subscription carrying the same `reference`.
  The defensive flow (FindSubscription-first, 404 ⇒ create) must therefore be the sole
  idempotency guarantee for double-clicks; do not rely on the provider rejecting a duplicate.
- **B3.** `ReadProductFamily` takes `int id` even though its map Notes mention a
  `handle:my-family` format — the generated signature cannot accept a handle. Resolve the
  family id by matching `Handle` over `ListProductFamilies` (no pagination), or skip family
  resolution entirely and key plans off `ReadProductByHandle`. The parameter-type discrepancy
  is visible in the map (`operations/ProductFamilies.md`); treat the Notes' handle syntax as
  unusable through this signature.
- **B4.** `CreateUsage`'s `componentId` accepts an int or string union, and the map's
  handle-prefix note ("component handle prefixed by `handle:`") is documented for
  `ListUsages`/`ReadComponent` — not stated for `CreateUsage`. Resolve the component id via
  `FindComponent("api-call")` and pass `ComponentIdModel.Int(id)` — the deterministic path;
  whether `CreateUsage` accepts `"handle:api-call"` is `UNVERIFIED`.
- **B5.** Next-billing-date semantics: the response carries both
  `CurrentPeriodEndsAt` and `NextAssessmentAt`; the map documents both fields but not which
  one the API guarantees to equal "the next charge date" in every state (e.g. trialing,
  `AwaitingSignup`). Surface both or pick `CurrentPeriodEndsAt` and label the state-dependent
  edge cases `UNVERIFIED` against live sandbox traffic.