# Maxio Advanced Billing — eShopOnWeb subscription billing

Additive, parallel capability on `src/PublicApi` (JWT). Does **not** replace Catalog → Basket → Order. Demo catalog is already seeded; do **not** call create-product / create-family operations.

NuGet: `AsadAli.AdvancedBilling.Sdk`. Root namespace: `MaxioAdvancedBilling`. Client: `MaxioAdvancedBillingClient`. SDK stamp: `v1.0.2` / `15db14b` (`sdk-map.md`).

---

## Scope & sequence

| Step | What | SDK operations |
|---|---|---|
| 1 | Bind `Maxio:` settings (no hardcoded values). Register `MaxioAdvancedBillingClient` in PublicApi DI. Target sandbox via subdomain + API key (US node unless `Maxio:BaseUrl` is set). | Client construction only (`MaxioAdvancedBillingClient` / `AddMaxioAdvancedBillingClient`) |
| 2 | Write the PublicApi error-translation boundary around every SDK call (Case A/B + `JsonException`). | — |
| 3 | `GET /api/subscription-plans` — list products in the configured product family. | `ProductFamilies.ListProductFamilies` → match `Handle` → `ProductFamilies.ListProductsForProductFamily` (paginate). Optional confirm: `Products.ReadProductByHandle` |
| 4 | `POST /api/subscriptions` — idempotent ensure-customer + idempotent enroll to a product handle (default `eshop-pro` if the body omits a handle). Confirm plan/price/state/next-billing-date. | `Customers.ReadCustomerByReference` · `Customers.CreateCustomer` · `Subscriptions.FindSubscription` · `Customers.ListCustomerSubscriptions` · `Subscriptions.CreateSubscription` |
| 5 | `GET /api/my-subscriptions` — list the caller’s subscriptions (plan, price, state, next billing date). | `Customers.ReadCustomerByReference` · `Customers.ListCustomerSubscriptions` |
| 6 | Tests for the integration layer (handler seam). | — |

HTTP surface (JWT; caller identity from the token; follow existing `src/PublicApi` endpoint conventions):

| Method | Route | Auth |
|---|---|---|
| GET | `/api/subscription-plans` | JWT |
| POST | `/api/subscriptions` | JWT |
| GET | `/api/my-subscriptions` | JWT |

Out of scope (seeded, not exposed by these endpoints): metered component handle `api-call`. The map has usage/allocation ops; this plan does not call them.

---

## CONTRACT SHEET

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

Throw-only SDK: **no** `{Operation}Result` / `ApiResult` variants. Every operation throws `MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>` on non-success.

### Client construction, auth, server node

| Fact | Value | Source |
|---|---|---|
| Package id | `AsadAli.AdvancedBilling.Sdk` | `sdk-map.md` |
| Client | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` | `MaxioAdvancedBillingClient.cs` |
| Constructor | `MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)` — only ctor | `sdk-map.md` |
| DI helper | `MaxioAdvancedBilling.ServiceCollectionExtensions.AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>? configure = null)` | `sdk-map.md`, `ServiceCollectionExtensions.cs` |
| Options type | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` | `MaxioAdvancedBillingClientOptions.cs` |
| Options members | `Environment`: `MaxioAdvancedBilling.Servers.ServerEnvironment` · `Retry`: `MaxioAdvancedBilling.Core.Configuration.RetryOptions` · `Server`: `MaxioAdvancedBilling.ServerOptions` · `BasicAuth`: `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md`, `MaxioAdvancedBillingClientOptions.cs` |
| Auth | HTTP Basic. `BasicAuthCredentials.Username` **required** `string` = API key; `Password` **required** `string` = literal `"x"`. | `sdk-map.md`, `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Environments | `ServerEnvironment.Us` (wire `US`, **default**) · `ServerEnvironment.Eu` (wire `EU`). **No Sandbox enum.** Sandbox is a site on US or EU hosting. | `sdk-map.md`, `Servers/ServerEnvironment.cs` |
| Production US template | `https://{site}.chargify.com` | `sdk-map.md`, `Servers/ProductionOptions.cs` |
| Production EU template | `https://{site}.ebilling.maxio.com` | `sdk-map.md`, `Servers/ProductionOptions.cs` |
| Subdomain | `options.Server.Production.Us.Site` (`string`, generated default `"subdomain"`). Nested types: `MaxioAdvancedBilling.ServerOptions.Production` → `MaxioAdvancedBilling.Servers.ProductionOptions.Us` → `UsOptions.Site` / `UsOptions.BaseUrl`. | `sdk-map.md`, `ServerOptions.cs`, `Servers/ProductionOptions.cs` |
| BaseUrl override | `options.Server.Production.Us.BaseUrl` (`string`). When `Maxio:BaseUrl` is set, assign it **verbatim**. When unset, leave the generated template. | `sdk-map.md`, `Servers/ProductionOptions.cs` |
| Which node is read | `options.Environment` captured at **construction** selects Us vs Eu nested options. Set `Site`/`BaseUrl` on the node that matches `Environment`. This plan uses `ServerEnvironment.Us` (see Assumptions). | `sdk-map.md` |
| Controllers used | `client.ProductFamilies` · `client.Products` · `client.Customers` · `client.Subscriptions` (`MaxioAdvancedBilling.Api.*`) | `sdk-map.md` |

**App settings (bind these keys only; hard-code none of their values):**

| Config key | Env var | Purpose |
|---|---|---|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | Basic `Username` |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | `Server.Production.Us.Site` |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | Family handle for plan listing (seeded example: `eshop-subscribe`) |
| `Maxio:BaseUrl` | *(optional; no dedicated env var in the request)* | When non-empty, verbatim Production US `BaseUrl` |

Seeded handles (stable; numeric IDs are not — never persist product/family IDs as the integration key): family `eshop-subscribe`; products `eshop-pro` ($299.00/mo, default subscribe target), `basic-plan` ($29.00/mo). Both: no trial, no setup, expires never, taxable no, payment method not required.

### Operations

#### 1. List products in a product family (by handle)

`ReadProductFamily` **cannot** take a handle: its C# param is `int id` even though its remarks mention `handle:my-family` (`operations/ProductFamilies.md`, `Api/ProductFamilies.cs`). Do **not** use it for handle lookup.

Resolve handle → id with `ListProductFamilies`, then list products. `ListProductsForProductFamily.productFamilyId` is `string` (path `{product_family_id}`). Pass the numeric id as a string (`family.Id.Value.ToString()`). Do **not** invent a `handle:` prefix on this operation — that format is documented on `ReadProductFamily` remarks, not on `ListProductsForProductFamily`.

| | |
|---|---|
| Controller | `client.ProductFamilies` |
| Method | `ListProductFamilies(MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` |
| Must-pass | `dateField` … `endDatetime` — nullable, no C# default → pass `null` to skip |
| Returns | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductFamilyResponse>` |
| Envelope | each item: `ProductFamily (product_family): ProductFamily?` — read `.ProductFamily` |
| Match | `ProductFamily.Handle (handle): string?` == `Maxio:ProductFamilyHandle` |
| Then take | `ProductFamily.Id (id): int?` |
| Error | **Case B** `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` — `StatusCode` · `ReadAsString()` · `ReadAsJson<T>()` · `ReadAsBytes()` |
| Pagination | none (map) |
| Map | `operations/ProductFamilies.md`, `records-3-Of-Su.md` |

| | |
|---|---|
| Controller | `client.ProductFamilies` |
| Method | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, MaxioAdvancedBilling.Models.ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| Must-pass | `dateField` … `include` (8 params) — nullable, no default → **named args**, pass `null` to skip |
| Path | `GET /product_families/{product_family_id}/products.json` · wire `product_family_id` ← `productFamilyId` |
| Query wire | `page`, `per_page`, `date_field`, `filter`, `start_date`, `end_date`, `start_datetime`, `end_datetime`, `include_archived`, `include` |
| Returns | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>` |
| Envelope | `Product (product): Product !req` — read `.Product` |
| Error | **Case A** `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>` — `TryGetString(out string)` **[404]** · `TryGetRawError(out RawError)` fallback |
| Pagination | manual `page`+`perPage` (defaults 1 / 20). Loop until an empty page. |
| Map | `operations/ProductFamilies.md`, `records-3-Of-Su.md` |

Call shape (named arguments required):

```csharp
var families = await client.ProductFamilies.ListProductFamilies(
    dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: ct);

var products = await client.ProductFamilies.ListProductsForProductFamily(
    productFamilyId: familyIdString,
    dateField: null, filter: null, startDate: null, endDate: null,
    startDatetime: null, endDatetime: null, includeArchived: null, include: null,
    page: page, perPage: 20, ct: ct);
```

**Product fields this integration reads** (`MaxioAdvancedBilling.Models.Product`, `records-3-Of-Su.md`):

| C# | Wire | Type |
|---|---|---|
| `Name` | `name` | `string?` |
| `Handle` | `handle` | `string?` |
| `Description` | `description` | `string?` |
| `PriceInCents` | `price_in_cents` | `long?` |
| `Interval` | `interval` | `int?` |
| `IntervalUnit` | `interval_unit` | `IntervalUnit?` |
| `TrialPriceInCents` | `trial_price_in_cents` | `long?` |
| `TrialInterval` | `trial_interval` | `int?` |
| `InitialChargeInCents` | `initial_charge_in_cents` | `long?` |
| `RequireCreditCard` | `require_credit_card` | `bool?` |
| `Taxable` | `taxable` | `bool?` |
| `ProductFamily` | `product_family` | `ProductFamily?` |
| `ProductPricePointName` | `product_price_point_name` | `string?` |
| `ProductPricePointHandle` | `product_price_point_handle` | `string?` |
| `ProductPricePointId` | `product_price_point_id` | `int?` |
| `DefaultProductPricePointId` | `default_product_price_point_id` | `int?` |
| `ArchivedAt` | `archived_at` | `DateTimeOffset?` |

`ListProductsFilter` (`records-2-Cr-Ne.md`): `Ids (ids): IReadOnlyList<int>?`, `PrepaidProductPricePoint`, `UseSiteExchangeRate` — **no family-handle filter**. Do not use `ListProducts` as the family-scoped list.

Optional single-plan read (`operations/Products.md`): `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` → `ProductResponse` · **Case B** `SdkException<RawError>` · `GET /products/handle/{api_handle}.json`.

#### 2. Find / create customer idempotently

Customer unique key: `CreateCustomer.Reference` / `Customer.Reference` (wire `reference`) = the eShopOnWeb user id from the JWT (stable string). Map notes: **only one customer per reference**; lookup is `ReadCustomerByReference`. (`operations/Customers.md`)

**Lookup**

| | |
|---|---|
| Controller | `client.Customers` |
| Method | `ReadCustomerByReference(string reference, CancellationToken ct = default)` |
| Query | `reference` ← `reference` |
| HTTP | `GET /customers/lookup.json` |
| Returns | `MaxioAdvancedBilling.Models.CustomerResponse` |
| Envelope | `Customer (customer): Customer !req` — read `.Customer` then `.Id`, `.Reference`, `.Email`, … |
| Error | **Case B** `SdkException<RawError>` — treat `StatusCode == NotFound` as “no customer yet” |
| Map | `operations/Customers.md`, `records-2-Cr-Ne.md` |

**Create** (only after lookup 404)

| | |
|---|---|
| Controller | `client.Customers` |
| Method | `CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, CancellationToken ct = default)` |
| Must-pass | `body` — nullable, no default → pass explicitly |
| HTTP | `POST /customers.json` |
| Returns | `CustomerResponse` (envelope `Customer !req`) |
| Error | **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` **[422]** · `TryGetRawError(out RawError)` fallback |
| Map | `operations/Customers.md`, `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md` |

Request envelope (`records-1-Ac-Cr.md`):

```
CreateCustomerRequest.Customer (customer): CreateCustomer !req
```

`CreateCustomer` fields used:

| C# | Wire | Type | Required? |
|---|---|---|---|
| `FirstName` | `first_name` | `string` | **!req** |
| `LastName` | `last_name` | `string` | **!req** |
| `Email` | `email` | `string` | **!req** |
| `Reference` | `reference` | `string?` | set to eShop user id |
| `Organization`, `Phone`, `Locale`, address fields, … | matching snake_case | optional | omit unless the JWT/user store has them |

`Customer` response fields (`records-2-Cr-Ne.md`): `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?`, `FirstName`/`LastName`, `Maxioid (maxioid): string?`, …

**Idempotent algorithm (double-click):**

1. `ReadCustomerByReference(reference: userId, ct: ct)`.
2. On success → use `.Customer.Id` (must be non-null to list/create subscriptions).
3. On Case B 404 → `CreateCustomer` with `Reference = userId` and required name/email from the authenticated user.
4. On Case A 422 (lost race: reference already taken) → `ReadCustomerByReference` again and use that customer. Do **not** retry `CreateCustomer` in a loop.

**422 payload trust (generated models disagree):** `CreateCustomerError.TryGetCustomerErrorResponse1` types `CustomerErrorResponse1.Errors` as `MaxioAdvancedBilling.Models.Errors?` (`PerPage (per_page)`, `PricePoint (price_point)` only) — `records-2-Cr-Ne.md`, `Models/CustomerErrorResponse1.cs`, `Models/Errors.cs`. A **different** generated union `MaxioAdvancedBilling.Models.AnyOf.Errors1` is `CustomerError | IReadOnlyList<string>` (`unions.md`) and is **not** what this accessor deserializes. Live Chargify 422 bodies are **UNVERIFIED** against `Models.Errors`. **Defensive:** if `TryGetCustomerErrorResponse1` succeeds, do not treat `PerPage`/`PricePoint` as customer messages; if it fails or is empty, `TryGetRawError` + `ReadAsString()` / `ReadAsJson`; if `JsonException` is thrown **instead of** `SdkException<CreateCustomerError>` while constructing the 422 object, extract best-effort from the exception and fall back to a generic message — never map that to a retryable 5xx.

#### 3. Create subscription idempotently (by product handle)

Specify product with `CreateSubscription.ProductHandle` (wire `product_handle`) — **not** numeric `ProductId` (IDs are unstable). Identify the customer with `CustomerId` **or** `CustomerReference` (wire `customer_reference`) = same eShop user id. Do **not** send payment-profile / card fields (seeded plans: payment method not required). (`operations/Subscriptions.md`)

**App-level subscription reference** (wire `reference` on create; lookup via `FindSubscription`): deterministic `{userId}:{productHandle}` (e.g. `{userId}:eshop-pro`). Map documents lookup-by-reference; it does **not** document uniqueness-on-create (unlike customer reference). Treat uniqueness as **UNVERIFIED**; still send the reference and **always** look up before create.

**Lookup by reference**

| | |
|---|---|
| Controller | `client.Subscriptions` |
| Method | `FindSubscription(string? reference, CancellationToken ct = default)` |
| Must-pass | `reference` — nullable, no default → pass explicitly |
| Query | `reference` ← `reference` |
| HTTP | `GET /subscriptions/lookup.json` |
| Returns | `MaxioAdvancedBilling.Models.SubscriptionResponse` |
| Envelope | `Subscription (subscription): Subscription?` — read `.Subscription` (nullable) |
| Error | **Case A** `SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>` — `TryGetNoContent(out RawError)` **[404]** · `TryGetRawError(out RawError)` fallback |
| Map | `operations/Subscriptions.md`, `records-4-Su-We.md` |

**Existing subs for the customer** (second guard)

| | |
|---|---|
| Controller | `client.Customers` |
| Method | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` |
| HTTP | `GET /customers/{customer_id}/subscriptions.json` |
| Returns | `IReadOnlyList<SubscriptionResponse>` |
| Envelope | each `.Subscription` (`Subscription?`) |
| Error | **Case B** `SdkException<RawError>` |
| Pagination | none |
| Map | `operations/Customers.md` |

**Create**

| | |
|---|---|
| Controller | `client.Subscriptions` |
| Method | `CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, CancellationToken ct = default)` |
| Must-pass | `body` — nullable, no default |
| HTTP | `POST /subscriptions.json` |
| Returns | `SubscriptionResponse` |
| Error | **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` **[422]** · `TryGetRawError(out RawError)` fallback |
| 422 payload | `ErrorListResponse1.Errors (errors): IReadOnlyList<string> !req` (`records-2-Cr-Ne.md`) |
| Map | `operations/Subscriptions.md`, `records-2-Cr-Ne.md` |

Request envelope:

```
CreateSubscriptionRequest.Subscription (subscription): CreateSubscription !req
```

`CreateSubscription` fields this call **sets** (`records-2-Cr-Ne.md`, `enums.md`, `Models/CreateSubscription.cs`):

| C# | Wire | Type | Notes |
|---|---|---|---|
| `ProductHandle` | `product_handle` | `string?` | chosen plan; default app value `eshop-pro` if omitted |
| `CustomerId` | `customer_id` | `int?` | from ensure-customer **or** use `CustomerReference` |
| `CustomerReference` | `customer_reference` | `string?` | same eShop user id (alternative to `CustomerId`); does **not** change payment-profile rules |
| `Reference` | `reference` | `string?` | `{userId}:{productHandle}` |
| `PaymentCollectionMethod` | `payment_collection_method` | `MaxioAdvancedBilling.Models.Enums.CollectionMethod?` | **set** `CollectionMethod.Remittance` (wire `remittance`). RI-valid values are `Remittance (remittance)`, `Automatic (automatic)`, `Prepaid (prepaid)`. `Invoice (invoice)` is **legacy Statements only** — do not send it. Unset defaults to the site’s collection method, which live 422 `"No payment method was on file for the $299.00 balance"` shows is charging a balance (automatic). The map does **not** document that remittance waives a payment profile; if 422 persists, that is site/product `require_credit_card`, which CreateSubscription cannot override. |

Leave unset: `PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes`, `CustomerAttributes` (customer already exists), `Components` (metered `api-call` not in this flow), `ProductPricePointHandle` / `ProductPricePointId` (use the product’s default price point), `CalendarBilling` / `CalendarBillingFirstCharge` (snap-day / first-charge timing, not a no-card switch), `DeferSignup` (Awaiting Signup Date — wrong hero-flow state), `NextBillingAt` / `InitialBillingAt` (import/schedule knobs; `NextBillingAt` in the future skips capture at create but does not enroll as a normal immediate subscribe), `SkipBillingManifestTaxes` (preview-only). There is **no** skip-payment / no-card flag on `CreateSubscription`.

**Idempotent algorithm (double-click):**

1. Ensure customer (section 2).
2. `FindSubscription(reference: $"{userId}:{productHandle}", ct: ct)`. If `.Subscription` is present → return it (do not create).
3. `ListCustomerSubscriptions(customerId)`. If any `.Subscription.Product.Handle` equals the requested handle **and** `.State` is a live state (`Active`, `Trialing`, `Assessing`, `PastDue`, `SoftFailure`, `Unpaid`, `OnHold`, `AwaitingSignup`, `Paused`, `Suspended` — see enum table) → return that subscription.
4. `CreateSubscription` with `ProductHandle` + `CustomerId`/`CustomerReference` + `Reference` + `PaymentCollectionMethod = CollectionMethod.Remittance`.
5. On 422: `FindSubscription` again; if found, return it. Otherwise map `ErrorListResponse1.Errors` to a 4xx.

#### 4. List a customer’s subscriptions (plan, price, state, next billing)

Same `ListCustomerSubscriptions` as above. If `ReadCustomerByReference` is 404, return an empty list (user has never subscribed) — do not create a customer on GET.

**Subscription fields this integration reads** (`MaxioAdvancedBilling.Models.Subscription`, `records-3-Of-Su.md`):

| C# | Wire | Type | Use |
|---|---|---|---|
| `Id` | `id` | `int?` | identity |
| `State` | `state` | `SubscriptionState?` | state |
| `ProductPriceInCents` | `product_price_in_cents` | `long?` | price (cents) |
| `NextAssessmentAt` | `next_assessment_at` | `DateTimeOffset?` | **next billing / assessment date** |
| `CurrentPeriodEndsAt` | `current_period_ends_at` | `DateTimeOffset?` | period end (include alongside next assessment) |
| `CurrentPeriodStartedAt` | `current_period_started_at` | `DateTimeOffset?` | optional |
| `ActivatedAt` | `activated_at` | `DateTimeOffset?` | optional |
| `Reference` | `reference` | `string?` | idempotency key |
| `Product` | `product` | `Product?` | nested plan: `Handle`, `Name`, `PriceInCents`, `Interval`, `IntervalUnit` |
| `ProductPricePointId` | `product_price_point_id` | `int?` | |
| `ProductPricePointType` | `product_price_point_type` | `PricePointType?` | |
| `Currency` | `currency` | `string?` | |
| `PaymentCollectionMethod` | `payment_collection_method` | `CollectionMethod?` | |

Envelope: `SubscriptionResponse.Subscription (subscription): Subscription?` (`records-4-Su-We.md`) — **one level down; field is nullable**.

PublicApi JSON should project: plan handle/name, price (`ProductPriceInCents` / nested `Product.PriceInCents`), `State` wire value, `NextAssessmentAt`.

### Enums in scope (`map/models/enums.md`, namespace `MaxioAdvancedBilling.Models.Enums`)

These are `StringEnum<T>`, **not** C# enums — use static members or `FromValue("wire")`.

| Enum | Members `(wire_value)` | Where |
|---|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` | `Subscription.State` / `PreviousState` |
| `SubscriptionStateFilter` | `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)` | `ListSubscriptions` only — **not** used; listing is per-customer |
| `PricePointType` | `Catalog (catalog)`, `Default (default)`, `Custom (custom)` | `Subscription.ProductPricePointType` |
| `IntervalUnit` | `Day (day)`, `Month (month)` | `Product.IntervalUnit` |
| `ExpirationIntervalUnit` | `Day (day)`, `Month (month)`, `Never (never)` | `Product.ExpirationIntervalUnit` |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` | `Subscription.PaymentCollectionMethod` |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` | list filters (pass `null`) |
| `ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)` | pass `null` |
| `SortingDirection` | `Asc (asc)`, `Desc (desc)` | unused here |
| `TrialType` | `NoObligation (no_obligation)`, `PaymentExpected (payment_expected)` | price-point trial handling (read-only if present) |

### Error types — how to read status/body

Core (`sdk-map.md`):

| Type | Namespace | Members |
|---|---|---|
| `SdkException<TError>` | `MaxioAdvancedBilling.Core.Exceptions` | `Error: TError` (required) |
| `ApiError` | `MaxioAdvancedBilling.Core.ErrorResponse` | `TryGetRawError(out RawError): bool` |
| `RawError` | `MaxioAdvancedBilling.Core.ErrorResponse` | `StatusCode: HttpStatusCode` · `ReadAsString()` · `ReadAsJson<T>()` · `ReadAsBytes()` |
| Typed `{Op}Error` | `MaxioAdvancedBilling.Errors` | per-op `TryGet…` + inherited `TryGetRawError` |

| Operation | Case | Catch | Accessors |
|---|---|---|---|
| `ListProductFamilies` | B | `SdkException<RawError>` | `ex.Error.StatusCode` / `ReadAsString()` |
| `ListProductsForProductFamily` | A | `SdkException<ListProductsForProductFamilyError>` | `TryGetString` [404]; `TryGetRawError` |
| `ReadProductByHandle` | B | `SdkException<RawError>` | status/body |
| `ReadCustomerByReference` | B | `SdkException<RawError>` | 404 = not found |
| `CreateCustomer` | A | `SdkException<CreateCustomerError>` | `TryGetCustomerErrorResponse1` [422]; `TryGetRawError` — see 422 trust note |
| `ListCustomerSubscriptions` | B | `SdkException<RawError>` | status/body |
| `FindSubscription` | A | `SdkException<FindSubscriptionError>` | `TryGetNoContent` [404]; `TryGetRawError` |
| `CreateSubscription` | A | `SdkException<CreateSubscriptionError>` | `TryGetErrorListResponse1` [422] → `.Errors` (`IReadOnlyList<string>`); `TryGetRawError` |

401/403: Case B or fallback `TryGetRawError` — check Basic username=API key, password=`"x"`, and `Site`/`BaseUrl` before changing call sites.

### Idempotency mechanisms the map actually exposes

| Mechanism | Map fact | Use |
|---|---|---|
| Customer `reference` | Unique; one customer per value; `ReadCustomerByReference` | eShop user id |
| Subscription `reference` | `CreateSubscription.Reference`; `FindSubscription(reference)` finds by it | `{userId}:{productHandle}` — uniqueness-on-create **UNVERIFIED** |
| `customer_reference` on create sub | Identify existing customer without numeric id | optional instead of `CustomerId` |
| `product_handle` | Stable catalog key | never persist numeric product ids |
| `ListCustomerSubscriptions` | All subs for a customer, no pagination | extra duplicate-product guard |

There is **no** SDK idempotency-key header. Transport retries can re-send writes — see trap notes.

---

## Trap notes

⚠ Step 1 (client registration) — `HttpClient` ownership/lifetime and whether the SDK wrapper is registered as singleton vs per-request are not visible from the constructor. **MUST load `dotnet-client-initialization`** before writing `new MaxioAdvancedBillingClient` or `AddMaxioAdvancedBillingClient`.

⚠ Step 1 (auth) — which options property holds Basic credentials, when they must be set relative to construction, and loading the key from configuration (not source) are not in the signature. **MUST load `dotnet-authentication`** before wiring `BasicAuth`.

⚠ Step 1 (server / BaseUrl / retries) — `Environment` vs `Server.*.Site` / `BaseUrl`, which nested Us/Eu node is actually read, what `Retry`/`Timeout` bound, and whether a failed `POST` (create customer/subscription) can be re-sent are not implied by the option names. **MUST load `dotnet-configuration-resilience`** before registering or tuning the client.

⚠ Steps 3–5 (calls) — list/search methods have many nullable parameters **without** C# defaults; a positional call mis-binds. Cancellation is `ct:`. Response payloads sit one level down (`ProductResponse.Product`, `CustomerResponse.Customer`, `SubscriptionResponse.Subscription`). **MUST load `dotnet-calling-endpoints`** before the first `client.{Group}.{Op}` call.

⚠ Steps 3–5 (models) — request records need `required` members in the object initializer; enums are `StringEnum<T>` not C# enums; `SubscriptionResponse.Subscription` is nullable; cents are `long?` (`price_in_cents`), not a decimal dollars field. **MUST load `dotnet-models`** before constructing `CreateCustomerRequest` / `CreateSubscriptionRequest` or mapping responses onto PublicApi DTOs.

⚠ Step 2 (error boundary) — Case A vs Case B differs **per operation** (this sheet’s table); `TryGetRawError` is not a catch-all on every error type the same way; there are no no-throw Result variants. **MUST load `dotnet-error-handling`** before any `try/catch`.

⚠ Step 2 (error boundary) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 2 (error boundary) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. This is acute on `CreateCustomer` 422 (`CustomerErrorResponse1.Errors` vs live body, see contract note). **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 6 (tests) — the test seam is the `HttpClient` constructor argument, not SDK internals; match PublicApi’s existing test framework. **MUST load `dotnet-testing`** before stubbing the client.

---

## REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — constructing/registering `MaxioAdvancedBillingClient`, `HttpClient` lifetime, `AddMaxioAdvancedBillingClient` |
| `dotnet-authentication` | Step 1 — `BasicAuth` username=API key / password=`"x"`, config-sourced credentials |
| `dotnet-configuration-resilience` | Step 1 — `Server.Production.Us.Site` / `BaseUrl`, `Environment`, retries/timeouts/pagination |
| `dotnet-calling-endpoints` | Steps 3–5 — named args, `ct:`, envelopes, async throw-only calls |
| `dotnet-models` | Steps 3–5 — `required` init, `StringEnum<T>`, wire vs C# names, nullable subscription envelope |
| `dotnet-error-handling` | Step 2 — Case A/B, `TryGet…`, **both** `JsonException` directions below |
| `dotnet-testing` | Step 6 — `HttpClient` handler seam |

An integration always writes an error boundary, so `dotnet-error-handling` is mandatory.

`System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## Assumptions & Blockers

**Assumptions**

- `MAXIO_ENVIRONMENT` is listed as an env var but is **not** one of the four `Maxio:` bind keys. The SDK has no Sandbox environment — only `ServerEnvironment.Us` / `Eu`. This plan sets `Environment = ServerEnvironment.Us` and `Server.Production.Us.Site` from `Maxio:Subdomain`. When `Maxio:BaseUrl` is set, it is assigned verbatim to `Server.Production.Us.BaseUrl`. Do not add a `Maxio:Environment` setting.
- JWT identity can supply `CreateCustomer`’s required `FirstName`, `LastName`, and `Email` (eShopOnWeb user). If a claim is missing, the PublicApi layer must fail the request before calling Maxio — the SDK will 422 on missing required fields.
- `POST /api/subscriptions` body may omit product handle; the app defaults to `eshop-pro`. Alternate plan is requested as handle `basic-plan`.
- GET `/api/my-subscriptions` on a user with no Maxio customer returns `[]`, not 404.
- Live `CreateSubscription` 422 `"No payment method was on file for the $299.00 balance"` (read via `TryGetErrorListResponse1` → `ErrorListResponse1.Errors`) means the site attempted to collect a $299 balance with no payment profile. The SDK has no skip-payment field; collection is selected only by `PaymentCollectionMethod`. This plan sets `CollectionMethod.Remittance`. Whether remittance actually creates without a card is **UNVERIFIED** (map lists architecture-valid values only). If 422 persists, inspect `Product.RequireCreditCard` / `RequestCreditCard` on the listed plan — CreateSubscription cannot override those.
- Metered component `api-call` is catalog context only; no usage recording in this scope.
- PublicApi endpoint style (FastEndpoints vs controllers, DTO naming) follows existing files in `src/PublicApi`; this sheet does not dictate that style.

**Blockers**

- None that prevent the three endpoints. Needed operations (list family products, customer lookup/create by reference, subscription create/find/list) are all on the map.
- `ReadProductFamily(int id)` cannot accept `handle:…` despite remarks — not a blocker because `ListProductFamilies` + `ListProductsForProductFamily(string productFamilyId)` covers handle-based listing.
- Customer 422 typed payload (`Models.Errors` vs unused `Errors1` union) is a generated-shape conflict: handled by the defensive extract-best-effort directive, not by inventing accessors.
- Subscription `reference` uniqueness-on-create is not documented on the map: handled by Find + ListCustomerSubscriptions + 422 re-lookup, labeled UNVERIFIED for the uniqueness constraint itself.
