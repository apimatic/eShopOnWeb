# Maxio Advanced Billing integration plan — eShopOnWeb

SDK: NuGet package `AsadAli.AdvancedBilling.Sdk`, root namespace `MaxioAdvancedBilling`, client class
`MaxioAdvancedBillingClient`, options class `MaxioAdvancedBillingClientOptions`. Target sandbox: subdomain
`apimatic-hackathon`, region US (`ServerEnvironment.Us`), product family handle `eshop-subscribe`
(products `eshop-pro`, `basic-plan`), metered component handle `api-call`.

## 1. Scope & sequence

1. **Client construction & DI** (ApplicationCore defines an abstraction; Infrastructure registers the
   concrete client). Uses: `MaxioAdvancedBillingClient` ctor, `BasicAuthCredentials`, `ServerEnvironment`,
   `ServerOptions`/`ProductionOptions`. See CONTRACT SHEET §"Client construction, auth, servers".
2. **Storefront "Plans" page — resolve family/products/plans** (item 1). Uses:
   `ProductFamilies.ListProductFamilies`, `Products.ListProductsForProductFamily`,
   `Products.ReadProductByHandle`.
3. **Customer lookup-or-create keyed on email/username** (item 2). Uses: `Customers.ReadCustomerByReference`,
   `Customers.CreateCustomer`.
4. **Startup validation of the metered component** (item 5). Uses: `Components.FindComponent` (or
   `Components.ReadComponent` if family-scoped). Run this once at app startup before enabling usage-recording
   features.
5. **Subscription enrollment with duplicate-detection** (items 3 + 4). Uses:
   `Customers.ListCustomerSubscriptions` (duplicate check) then `Subscriptions.CreateSubscription`.
6. **Usage recording + period-to-date read-back** (item 6). Uses: `SubscriptionComponents.CreateUsage`,
   `SubscriptionComponents.ReadSubscriptionComponent` (for the accumulated `unit_balance`),
   `SubscriptionComponents.ListUsages` (individual usage history, not a total).
7. **Plan-change preview/commit** (item 7). Uses: `SubscriptionProducts.PreviewSubscriptionProductMigration`,
   `SubscriptionProducts.MigrateSubscriptionProduct` (immediate + proration), and
   `Subscriptions.UpdateSubscription` (delayed, no proration, at next renewal).
8. **Lifecycle actions** (item 8). Uses: `SubscriptionStatus.PauseSubscription`,
   `SubscriptionStatus.ResumeSubscription`, `SubscriptionStatus.CancelSubscription`,
   `SubscriptionStatus.InitiateDelayedCancellation`, `SubscriptionStatus.CancelDelayedCancellation`,
   `SubscriptionStatus.ReactivateSubscription`.
9. **Single domain exception wrapper** (item 9): wrap every call site per the error-case table below,
   translating both `SdkException<TError>` (API errors) and `HttpRequestException`/`TaskCanceledException`
   (connection failures) into one project-level exception type.
10. **Tests**: inject a fake `HttpClient`/`HttpMessageHandler` per the SDK's test seam (no project mocking
    of the SDK itself) — see CONTRACT SHEET §"Testing seam".

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The
> cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** (e.g.
> `MaxioAdvancedBilling.Models.Enums.SubscriptionState`, `MaxioAdvancedBilling.Models.AnyOf.SubscriptionIdOrReference`,
> `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials`, and the **client-config types**:
> `MaxioAdvancedBilling.Servers.ServerEnvironment`, `MaxioAdvancedBilling.Core.Configuration.RetryOptions`,
> `MaxioAdvancedBilling.Core.Configuration.ServerOptions`). The map carries these namespaces (a members table
> names the namespace, or a row gives the source path `Core/Configuration/…` ⇒ namespace
> `MaxioAdvancedBilling.Core.Configuration`) — do not drop them to the root or `.Models`, or the implementer
> guesses the wrong `using` and the build breaks.

Namespaces used below (from `sdk-map.md`): client/options → `MaxioAdvancedBilling`; controllers
(`client.X`) → `MaxioAdvancedBilling.Api`; records → `MaxioAdvancedBilling.Models`; enums →
`MaxioAdvancedBilling.Models.Enums`; unions → `MaxioAdvancedBilling.Models.AnyOf` /
`MaxioAdvancedBilling.Models.OneOf`; error classes → `MaxioAdvancedBilling.Errors`; `SdkException<T>` →
`MaxioAdvancedBilling.Core.Exceptions`; `ApiError`/`RawError` → `MaxioAdvancedBilling.Core.ErrorResponse`;
`BasicAuthCredentials` → `MaxioAdvancedBilling.Core.Authentication.Basic`; `ServerEnvironment` →
`MaxioAdvancedBilling.Servers`; `RetryOptions`/`ServerOptions` → `MaxioAdvancedBilling.Core.Configuration`.

### Client construction, auth, servers (map page: `sdk-map.md`)

- Only constructor: `MaxioAdvancedBilling.MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)`.
- `MaxioAdvancedBillingClientOptions` properties: `Environment: MaxioAdvancedBilling.Servers.ServerEnvironment`
  · `Retry: MaxioAdvancedBilling.Core.Configuration.RetryOptions` · `Server: MaxioAdvancedBilling.Core.Configuration.ServerOptions`
  · `BasicAuth: MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?`.
- **Basic auth credential property names (exact, verbatim)**:
  `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username, Password }`.
  Convention: `Username` = the Maxio/Chargify API key, `Password` = the **literal string `"x"`**.
- **Environment**: `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default) or `.Eu`.
- **Base-URL / subdomain override** (two independent server groups; only Production matters for this
  integration — `Ebb` is only for the event-ingest endpoints `BulkRecordEvents`/`RecordEvent`, out of scope
  here):
  | Group | US template | EU template | Override points |
  |---|---|---|---|
  | Production | `https://{site}.chargify.com` | `https://{site}.ebilling.maxio.com` | `options.Server.Production.Us.BaseUrl` / `.Us.Site` (and `.Eu.BaseUrl` / `.Eu.Site`) |
  - **Explicit BaseUrl config wins, else derive host from subdomain + region** — implement exactly this
    precedence:
    ```csharp
    options.Environment = region == Region.Eu ? ServerEnvironment.Eu : ServerEnvironment.Us;
    if (!string.IsNullOrWhiteSpace(explicitBaseUrl))
    {
        if (region == Region.Eu) options.Server.Production.Eu.BaseUrl = explicitBaseUrl;
        else                     options.Server.Production.Us.BaseUrl = explicitBaseUrl;
    }
    else
    {
        if (region == Region.Eu) options.Server.Production.Eu.Site = subdomain;
        else                     options.Server.Production.Us.Site = subdomain;
    }
    ```
    (A literal `BaseUrl` with no `{placeholders}` is used as-is and makes `Site` irrelevant for that group.)
- DI: `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = ...; o.Environment = ...; })` — registers
  the client transient over a shared `IHttpClientFactory`-managed default `HttpClient`.

### Testing seam (map page: n/a — SDK constructor + `dotnet-testing`)

- The **only** test seam is the `HttpClient` constructor argument — the SDK ships no mocking helpers.
  Construct `new MaxioAdvancedBillingClient(new HttpClient(fakeHandler), new MaxioAdvancedBillingClientOptions())`
  where `fakeHandler` is a custom `HttpMessageHandler` (or `HttpClientHandler` subclass) that returns
  canned `HttpResponseMessage`s and records the outgoing `HttpRequestMessage` for assertions. Auth is not
  required on the options object for a stub client (no real network call happens).
- Assert success by deserializing the stub JSON into the expected envelope (e.g.
  `{ "customer": { "id": 123 } }` → `response.Customer.Id == 123`).
- Assert error paths by asserting the concrete `SdkException<TError>` per operation's Case A/B (below) —
  never via reflection.

### Operations — Product catalog (map page: `operations/ProductFamilies.md`, `operations/Products.md`)

| Op | Signature | Request/response | Error | Pagination |
|---|---|---|---|---|
| `client.ProductFamilies.ListProductFamilies` | `ListProductFamilies(MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 leading params **must be passed explicitly** (pass `null`) | Returns `IReadOnlyList<MaxioAdvancedBilling.Models.ProductFamilyResponse>`; each wraps `ProductFamily (product_family): ProductFamily?` with fields `Id: int?`, `Name: string?`, `Handle: string?` | `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` (Case B) | none |
| `client.Products.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — **`productFamilyId` is `string`** (accepts the numeric id as a string) | Returns `IReadOnlyList<ProductResponse>`; each wraps `Product (product): Product !req` | `SdkException<RawError>` (Case B) | manual `page`+`perPage` |
| `client.Products.ReadProductByHandle` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | Returns `ProductResponse { Product: Product !req }` | `SdkException<RawError>` (Case B) | none |
| `client.ProductFamilies.ReadProductFamily` | `ReadProductFamily(int id, CancellationToken ct = default)` — **`id` is `int`, NOT string/handle**, despite the endpoint's prose claiming a `handle:my-family` path form works — the typed method only accepts the numeric id | Returns `ProductFamilyResponse` | `SdkException<RawError>` (Case B) | none |

**`Product` fields read for the storefront** (`records/records-3-Of-Su.md`): `Id: int?`, `Name: string?`,
`Handle: string?`, `Description: string?`, `PriceInCents: long?`, `Interval: int?`,
`IntervalUnit: MaxioAdvancedBilling.Models.Enums.IntervalUnit?` (`Day`/`Month`), `RequestCreditCard: bool?`,
`RequireCreditCard: bool?` (two distinct, similarly-named fields — see Trap notes),
`ProductFamily: ProductFamily?`.

**No typed "read product family by handle" op exists** — resolving `eshop-subscribe` → its numeric id
requires `ListProductFamilies()` then a client-side filter on `.Handle == "eshop-subscribe"`.
`ReadProductByHandle("eshop-pro")` / `ReadProductByHandle("basic-plan")`, by contrast, resolve directly with
no family id needed.

### Operations — Customers (map page: `operations/Customers.md`)

| Op | Signature | Request/response | Error | Pagination |
|---|---|---|---|---|
| `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | Returns `CustomerResponse { Customer: Customer !req }` | `SdkException<RawError>` (Case B) — **404 on no match** | none |
| `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly | `CreateCustomerRequest { Customer: CreateCustomer !req }`; `CreateCustomer` fields: `FirstName: string !req`, `LastName: string !req`, `Email: string !req`, `Reference: string?`, `Organization/Address/Address2/City/State/Zip/Country/Phone/Locale/VatNumber: string?`, `TaxExempt: bool?` | Returns `CustomerResponse { Customer: Customer !req }` | `SdkException<CreateCustomerError>` (Case A) — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none |
| `client.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | Returns `IReadOnlyList<SubscriptionResponse>` — **all subscriptions for the customer, across every product/family; no filter param** — filter client-side on `Subscription.Product?.Handle` and `.State` | `SdkException<RawError>` (Case B) | none |

**Idempotent lookup-or-create recipe** (item 2): call `ReadCustomerByReference(reference: emailOrUsername)`;
on `SdkException<RawError>` with `ex.Error.StatusCode == HttpStatusCode.NotFound`, call `CreateCustomer`
with `Reference = emailOrUsername`, `Email = emailOrUsername` (or the real email), and **required**
`FirstName`/`LastName` — the SDK requires both even if the caller only has an email/username (source:
`CreateCustomer !req` flags). On a `CreateCustomer` race (two concurrent requests for the same reference),
expect `SdkException<CreateCustomerError>` with `TryGetCustomerErrorResponse1` true — see Trap notes below
on why that typed payload is not useful for the message and to fall back to the raw body.

### Operations — Component validation (map page: `operations/Components.md`)

| Op | Signature | Request/response | Error | Pagination |
|---|---|---|---|---|
| `client.Components.FindComponent` | `FindComponent(string handle, CancellationToken ct = default)` — **site-wide lookup, no product-family id needed** | Returns `ComponentResponse { Component: Component !req }` | `SdkException<RawError>` (Case B) | none |
| `client.Components.ReadComponent` | `ReadComponent(int productFamilyId, string componentId, CancellationToken ct = default)` — family-scoped alternative; `componentId` must be prefixed `"handle:api-call"` when passing a handle | Returns `ComponentResponse { Component: Component !req }` | `SdkException<RawError>` (Case B) | none |

**`Component` fields relevant to startup validation**: `Id: int?`, `Handle: string?`,
`Kind: MaxioAdvancedBilling.Models.Enums.ComponentKind?`, `UnitName: string?`. **Assert
`Component.Kind == ComponentKind.MeteredComponent`** (wire value `metered_component`); fail startup with a
clear message if it is any other `ComponentKind` (`QuantityBasedComponent`, `OnOffComponent`,
`PrepaidUsageComponent`, `EventBasedComponent`).

### Operations — Subscription creation & lookup (map page: `operations/Subscriptions.md`)

| Op | Signature | Request/response | Error | Pagination |
|---|---|---|---|---|
| `client.Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | `CreateSubscriptionRequest { Subscription: CreateSubscription !req }`; key `CreateSubscription` fields for this flow: `ProductHandle: string?` / `ProductId: int?`, `CustomerId: int?` / `CustomerReference: string?`, `Reference: string?`, `ProductPricePointHandle/ProductPricePointId: —?` (omit to use default price point), `PaymentCollectionMethod: MaxioAdvancedBilling.Models.Enums.CollectionMethod?` (wire `payment_collection_method`), `NetTerms: string?` (wire `net_terms` — **note: `string?` here on the create side vs. `int?` on the read-side `Subscription.NetTerms`, don't conflate the two types**) | Returns `SubscriptionResponse { Subscription: Subscription? }` | `SdkException<CreateSubscriptionError>` (Case A) — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] (`Errors: IReadOnlyList<string> !req`) · `TryGetRawError(out RawError)` [fallback] | none |
| `client.Subscriptions.ListSubscriptions` | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string,string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — all 14 leading params must be passed (named args); **no single-id filter param exists** (filters are state/product/productPricePointId/coupon/couponCode/date-range/metadata/sort only — no `id`) | Returns `IReadOnlyList<SubscriptionResponse>` | `SdkException<RawError>` (Case B) | manual `page`+`perPage` |
| `client.Subscriptions.ReadSubscription` | `ReadSubscription(int subscriptionId, IReadOnlyList<MaxioAdvancedBilling.Models.Enums.SubscriptionInclude>? include, CancellationToken ct = default)` — **the single read-by-numeric-id op**; `include` has no C# default, pass `null` to skip | Returns `SubscriptionResponse { Subscription: Subscription? }` | `SdkException<RawError>` (Case B) — 404 on unknown id surfaces via `.StatusCode`/`.ReadAsString()`, no typed accessor | none |
| `client.Subscriptions.FindSubscription` | `FindSubscription(string? reference, CancellationToken ct = default)` — GET `/subscriptions/lookup.json`; looks up by the subscription's `Reference` field value. Notes say only "finds a subscription by its reference" — the map/source do **not** state whether this also accepts the numeric id as a string, so don't rely on that; use `ReadSubscription(int, ...)` for id-based lookups instead | Returns `SubscriptionResponse` | `SdkException<FindSubscriptionError>` (Case A) — `TryGetNoContent(out RawError)` [404] · `TryGetRawError` [fallback] | none |

**"No card capture" plans**: check `Product.RequireCreditCard` (and, defensively, `RequestCreditCard` too
— see Trap notes) on the target `eshop-pro`/`basic-plan` product before omitting payment attributes from
`CreateSubscription`; when `RequireCreditCard` is `false`/`null`, create the subscription with only
`ProductHandle` + `CustomerId`/`CustomerReference` + `Reference`, no `PaymentProfileAttributes`/
`CreditCardAttributes`/`BankAccountAttributes`. **Live-sandbox correction**: on a balance-bearing product
(`eshop-pro` $29.00/$299.00 price points), `RequireCreditCard: false` alone was not sufficient against the
live `apimatic-hackathon` sandbox — the site still 422'd with "No payment method was on file for the
$X.XX balance" when no `PaymentCollectionMethod` was set (the field defaults server-side to automatic
card-based collection regardless of the product's own require-card flag). Set
`Subscription.PaymentCollectionMethod = CollectionMethod.Invoice` explicitly for a no-card-on-file
enrollment (legacy Statements-Architecture sites); if the site instead runs the newer Relationship
Invoicing Architecture, use `CollectionMethod.Remittance` instead — the map/source cannot tell you which
architecture a given site runs (that's a site-level setting, not an SDK fact), so this choice must be
confirmed against the actual sandbox response, not assumed. `CollectionMethod.Automatic`/`.Prepaid` still
require a payment method on file; only `.Invoice`/`.Remittance` are documented (in the enum's own map
summary) as not requiring automatic card collection.

**Duplicate-enrollment check** (item 4): `ListCustomerSubscriptions(customerId)`, then filter for a
`Subscription` whose `Product?.Handle` matches the target product handle **and** whose `State` is one this
integration treats as "active" — see Assumptions for exactly which `SubscriptionState` values qualify.

### Operations — Usage recording & read-back (map page: `operations/SubscriptionComponents.md`)

| Op | Signature | Request/response | Error | Pagination |
|---|---|---|---|---|
| `client.SubscriptionComponents.CreateUsage` | `CreateUsage(MaxioAdvancedBilling.Models.AnyOf.SubscriptionIdOrReference subscriptionIdOrReference, MaxioAdvancedBilling.Models.AnyOf.ComponentIdModel componentId, CreateUsageRequest? body, CancellationToken ct = default)` — **both id params are unions, not `int`**: construct with `SubscriptionIdOrReference.Int(id)` / `.String(reference)` and `ComponentIdModel.Int(id)` / `.String("handle:api-call")` (the `handle:` prefix convention for the component-id path segment is documented on the sibling `ListUsages` endpoint's notes and applies to the same path segment here) | `CreateUsageRequest { Usage: CreateUsage !req }`; `CreateUsage` fields: `Quantity: double?`, `Memo: string?`, `PricePointId: string?` | Returns `UsageResponse { Usage: Usage !req }`; `Usage` fields: `Id: long?`, `Memo: string?`, `Quantity: Quantity1?` (union `AnyOf<int,string>` — read via `TryGetInt`/`TryGetString`), `ComponentId: int?`, `SubscriptionId: int?` | `SdkException<CreateUsageError>` (Case A) — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | none |
| `client.SubscriptionComponents.ReadSubscriptionComponent` | `ReadSubscriptionComponent(int subscriptionId, int componentId, CancellationToken ct = default)` — **plain `int`, not a union** (needs the numeric component id, e.g. from `FindComponent`) | Returns `SubscriptionComponentResponse { Component: SubscriptionComponent? }` | `SdkException<ReadSubscriptionComponentError>` (Case A) — `TryGetNoContent(out RawError)` [404] · `TryGetRawError` [fallback] | none |
| `client.SubscriptionComponents.ListUsages` | `ListUsages(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, long? sinceId, long? maxId, DateTimeOffset? sinceDate, DateTimeOffset? untilDate, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | Returns `IReadOnlyList<UsageResponse>` — **individual usage entries, not an aggregate total** | `SdkException<RawError>` (Case B) | manual `page`+`perPage` |

**Period-to-date usage total** (item 6, second half): the accumulated total for a metered component on a
subscription is **`SubscriptionComponent.UnitBalance: int?`**, read via `ReadSubscriptionComponent` — NOT
via `ListUsages` (which only returns the individual usage log entries). Source note on `CreateUsage`:
"the `quantity` from usage for each component is accumulated to the `unit_balance` on the Component Line
Item for the subscription", confirming `UnitBalance` is the right field.

### Operations — Plan change: preview & commit (map page: `operations/SubscriptionProducts.md`, `operations/Subscriptions.md`)

| Op | Signature | Request/response | Error | Pagination |
|---|---|---|---|---|
| `client.SubscriptionProducts.PreviewSubscriptionProductMigration` | `PreviewSubscriptionProductMigration(int subscriptionId, SubscriptionMigrationPreviewRequest? body, CancellationToken ct = default)` | `SubscriptionMigrationPreviewRequest { Migration: SubscriptionMigrationPreviewOptions !req }`; fields: `ProductId: int?` / `ProductHandle: string?`, `ProductPricePointId: int?` / `ProductPricePointHandle: string?`, `Proration: MaxioAdvancedBilling.Models.Proration?` (`{ PreservePeriod: bool? }`), `ProrationDate: DateTimeOffset?` (preview a future date within the current period) | Returns `SubscriptionMigrationPreviewResponse { Migration: SubscriptionMigrationPreview !req }`; fields: `ProratedAdjustmentInCents: long?`, `ChargeInCents: long?`, `PaymentDueInCents: long?`, `CreditAppliedInCents: long?` | `SdkException<PreviewSubscriptionProductMigrationError>` (Case A) — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | none |
| `client.SubscriptionProducts.MigrateSubscriptionProduct` | `MigrateSubscriptionProduct(int subscriptionId, SubscriptionProductMigrationRequest? body, CancellationToken ct = default)` — **this is the "immediate, with proration" commit path** | `SubscriptionProductMigrationRequest { Migration: SubscriptionProductMigration !req }`; same fields as `SubscriptionMigrationPreviewOptions` minus `ProrationDate` | Returns `SubscriptionResponse` | `SdkException<MigrateSubscriptionProductError>` (Case A) — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | none |
| `client.Subscriptions.UpdateSubscription` | `UpdateSubscription(int subscriptionId, UpdateSubscriptionRequest? body, CancellationToken ct = default)` — **this is the "at next renewal, no proration" commit path**: set `ProductChangeDelayed = true` | `UpdateSubscriptionRequest { Subscription: UpdateSubscription !req }`; fields used: `ProductHandle: string?` / `ProductId: int?`, `ProductChangeDelayed: bool?`, `ProductPricePointId: int?` / `ProductPricePointHandle: string?` (source note: "No proration applies in this case"; to cancel a pending delayed change, set `NextProductId` to `""`) | Returns `SubscriptionResponse` | `SdkException<UpdateSubscriptionError>` (Case A) — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | none |

Only two products in scope (`eshop-pro`, `basic-plan`) — resolve the target `ProductId`/`ProductHandle` via
`ReadProductByHandle` before calling any of the three ops above; both products already belong to the same
family (`eshop-subscribe`) per the sandbox seed data, so no family-membership check is required by the SDK
itself (Migrations/UpdateSubscription do not validate family membership client-side — a cross-family
product id would surface as a 422 from the server, caught via the same `ErrorListResponse1`/`RawError`
accessors above).

**There is no preview endpoint for the delayed/no-proration path** — `PreviewSubscriptionProductMigration`
only previews the immediate/prorated `Migrations` path. If a preview of the delayed change's future charge
is needed, the closest typed op is `SubscriptionStatus.PreviewRenewal`
(`PreviewRenewal(int subscriptionId, RenewalPreviewRequest? body, ct)` → `RenewalPreviewResponse`, Case A:
`TryGetErrorListResponse1` [422] / `TryGetRawError` [fallback]) — out of this plan's named scope but flagged
here since the user's item 7 implies wanting both directions previewable.

### `Subscription` record — full field list (map page: `records/records-3-Of-Su.md`, source `Models/Subscription.cs`)

For the ApplicationCore domain model mirroring `Subscription` (returned nested in `SubscriptionResponse`
from `CreateSubscription`, `ListCustomerSubscriptions`, etc.) — namespace `MaxioAdvancedBilling.Models`
unless noted. **All fields are nullable** (no `!req` field exists on this record):

`Id: int?` · `State: MaxioAdvancedBilling.Models.Enums.SubscriptionState?` · `PreviousState: SubscriptionState?`
· `BalanceInCents: long?` · `TotalRevenueInCents: long?` · `ProductPriceInCents: long?` ·
`ProductVersionNumber: int?` · `CurrentPeriodStartedAt: DateTimeOffset?` · `CurrentPeriodEndsAt: DateTimeOffset?`
· `NextAssessmentAt: DateTimeOffset?` · `TrialStartedAt: DateTimeOffset?` · `TrialEndedAt: DateTimeOffset?` ·
`ActivatedAt: DateTimeOffset?` · `ExpiresAt: DateTimeOffset?` · `CreatedAt: DateTimeOffset?` ·
`UpdatedAt: DateTimeOffset?` · `CancellationMessage: string?` ·
`CancellationMethod: MaxioAdvancedBilling.Models.Enums.CancellationMethod?` · `CancelAtEndOfPeriod: bool?` ·
`CanceledAt: DateTimeOffset?` · `DelayedCancelAt: DateTimeOffset?` · `ScheduledCancellationAt: DateTimeOffset?`
· `OnHoldAt: DateTimeOffset?` · `AutomaticallyResumeAt: DateTimeOffset?` · `CouponCode: string?` ·
`CouponCodes: IReadOnlyList<string>?` · `CouponUseCount: int?` · `CouponUsesAllowed: int?` ·
`Coupons: IReadOnlyList<SubscriptionIncludedCoupon>?` · `SnapDay: string?` ·
`PaymentCollectionMethod: MaxioAdvancedBilling.Models.Enums.CollectionMethod?` · `Customer: Customer?` ·
`Product: Product?` · `CreditCard: CreditCardPaymentProfile?` · `BankAccount: BankAccountPaymentProfile?` ·
`PaymentType: string?` · `Group: NestedSubscriptionGroup?` · `ReferralCode: string?` ·
`NextProductId: int?` · `NextProductHandle: string?` · `NextProductPricePointId: int?` ·
`ProductPricePointId: int?` · `ProductPricePointType: MaxioAdvancedBilling.Models.Enums.PricePointType?` ·
`ReasonCode: string?` · `OfferId: int?` · `PayerId: int?` · `CurrentBillingAmountInCents: long?` ·
`NetTerms: int?` · `StoredCredentialTransactionId: int?` · `Reference: string?` · `PrepaidDunning: bool?` ·
`DunningCommunicationDelayEnabled: bool?` · `DunningCommunicationDelayTimeZone: string?` ·
`ReceivesInvoiceEmails: bool?` · `Locale: string?` · `Currency: string?` · `CreditBalanceInCents: long?` ·
`PrepaymentBalanceInCents: long?` · `PrepaidConfiguration: PrepaidConfiguration?` ·
`SelfServicePageToken: string?`.

**Direct answers to the 6 questions:**
1. Subscription's own numeric id: **`Id: int?`** — nullable, directly on `Subscription`.
2. **No `CustomerId` field exists directly on `Subscription`.** The owning customer's id is only reachable
   via the nested `Customer: Customer?` object → `Customer.Id: int?`. If the domain model needs a flat
   customer-id column, it must be populated from `subscription.Customer?.Id`, with null-handling for the
   (unlikely but nullable-typed) case where `Customer` itself is null.
3. Next billing / renewal date: **both `NextAssessmentAt: DateTimeOffset?` and
   `CurrentPeriodEndsAt: DateTimeOffset?` exist directly** on `Subscription` (plus
   `CurrentPeriodStartedAt: DateTimeOffset?` for the period start) — no single field is authoritatively "the"
   renewal date; `NextAssessmentAt` is the more direct "when will this be billed next" field, per its name.
4. Delayed-cancellation fields, confirmed directly on `Subscription`: **`CancelAtEndOfPeriod: bool?`** and
   **`DelayedCancelAt: DateTimeOffset?`** — both nullable, exact casing as written. (`ScheduledCancellationAt:
   DateTimeOffset?` also exists directly on `Subscription`, separate from `DelayedCancelAt` — the map does
   not further distinguish the two beyond their names; if the domain model needs exactly one "scheduled
   cancel date" column, this is a fact worth confirming against a live sandbox response before picking one.)
5. `Product.Name: string?` **confirmed**. Also present and worth showing on a "plan" listing:
   **`Description: string?`** (already added to the sheet's Product-fields line above) — this was missing
   from the first pass. Nothing else beyond what you already have (`Id/Name/Handle/Description/
   PriceInCents/Interval/IntervalUnit/RequestCreditCard/RequireCreditCard/ProductFamily`) stood out as
   plan-listing-relevant on the `Product` record.
6. `Subscription.State` — confirmed exact declared type is **`MaxioAdvancedBilling.Models.Enums.SubscriptionState?`**
   (nullable), matching your defensive-mapping plan.

### Operations — Subscription lifecycle (map page: `operations/SubscriptionStatus.md`)

| Action | Op & signature | Resulting `SubscriptionState` | Error |
|---|---|---|---|
| Pause | `PauseSubscription(int subscriptionId, PauseRequest? body, CancellationToken ct = default)` — `PauseRequest { Hold: AutoResume? }` (`AutoResume { AutomaticallyResumeAt: DateTimeOffset? }`); pass `body: null` for an indefinite hold | `SubscriptionState.OnHold` (wire `on_hold`) | `SdkException<PauseSubscriptionError>` (Case A) — `TryGetErrorListResponse1` [422] · `TryGetRawError` [fallback] |
| Resume | `ResumeSubscription(int subscriptionId, MaxioAdvancedBilling.Models.Enums.ResumptionCharge? calendarBillingResumptionCharge, CancellationToken ct = default)` — pass `null` unless the subscription uses calendar billing (`ResumptionCharge`: `Prorated`/`Immediate`/`Delayed`) | `SubscriptionState.Active` (or `Trialing` if resumed mid-trial) | `SdkException<ResumeSubscriptionError>` (Case A) — `TryGetErrorListResponse1` [422] · `TryGetRawError` [fallback] |
| Cancel — immediate | `CancelSubscription(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` — pass `body: null` (or a `CancellationRequest{ Subscription: CancellationOptions{...} }` with no schedule fields) to cancel now | `SubscriptionState.Canceled` | `SdkException<CancelSubscriptionApiError>` (Case A) — `TryGetNoContent(out RawError)` [404] · `TryGetCancelSubscriptionErrorResponse(out CancelSubscriptionErrorResponse)` [422] · `TryGetRawError` [fallback] |
| Cancel — end-of-period (delayed) | `InitiateDelayedCancellation(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` — `CancellationRequest { Subscription: CancellationOptions !req }` (`CancelAtEndOfPeriod`, `ScheduledCancellationAt`, `CancellationMessage`, `ReasonCode`, `RefundPrepaymentAccountBalance`, all `bool?`/`string?`/`DateTimeOffset?`) | Stays `Active` with `CancelAtEndOfPeriod = true` and `DelayedCancelAt` set, until the period ends and it auto-transitions to `Canceled` | `SdkException<InitiateDelayedCancellationError>` (Case A) — `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` [fallback] |
| Cancel — undo the delayed cancel | `CancelDelayedCancellation(int subscriptionId, CancellationToken ct = default)` — idempotent | Back to `Active` (no `CancelAtEndOfPeriod`) | `SdkException<CancelDelayedCancellationError>` (Case A) — `TryGetNoContent(out RawError)` [404] · `TryGetRawError` [fallback] |
| Reactivate | `ReactivateSubscription(int subscriptionId, ReactivateSubscriptionRequest? body, CancellationToken ct = default)` — `ReactivateSubscriptionRequest { CalendarBilling: ReactivationBilling?, IncludeTrial: bool?, PreserveBalance: bool?, CouponCode: string?, UseCreditsAndPrepayments: bool?, Resume: MaxioAdvancedBilling.Models.AnyOf.Resume? }` (`Resume` is a union of `bool` or `ResumeOptions` — `Resume.Bool(true)` for `resume=true`) — only works from `Canceled`/`Unpaid`/`TrialEnded` | `SubscriptionState.Active` or `.Trialing` | `SdkException<ReactivateSubscriptionError>` (Case A) — `TryGetErrorListResponse1` [422] · `TryGetRawError` [fallback] |

**`SubscriptionState` full value list** (`models/enums.md`): `Pending`, `FailedToCreate`, `Trialing`,
`Assessing`, `Active`, `SoftFailure`, `PastDue`, `Suspended`, `Canceled`, `Expired`, `Paused`, `Unpaid`,
`TrialEnded`, `OnHold`, `AwaitingSignup` (wire values are the snake_case of each name, e.g. `on_hold`).
Note: the enum also has a distinct `Paused` constant (wire `paused`) separate from `OnHold` (wire
`on_hold`) — `PauseSubscription`/`ResumeSubscription` (the "hold" endpoints) transition through `OnHold`,
per the endpoint's own path (`/hold.json`) and notes; nothing in the map ties the `Paused` constant to any
operation in scope here — flagged as `UNVERIFIED` which, if any, operation ever produces `Paused` instead
of `OnHold`.

### Error-handling model, applied to this scope

**Exact declared shapes (source: `Core/ErrorResponse/RawError.cs`, `Core/Exceptions/SdkException.cs`)** —
`RawError.StatusCode` is `System.Net.HttpStatusCode` (**not** `int`; compare with
`rawError.StatusCode == System.Net.HttpStatusCode.NotFound`, or cast `(int)rawError.StatusCode` for the
numeric form). `SdkException<TError>` is `public sealed class SdkException<TError> : Exception { public
required TError Error { get; init; } }` — so `catch (SdkException<RawError> ex)` gives `ex.Error` as the
`RawError` instance directly (`ex.Error.StatusCode`/`.ReadAsString()`/`.ReadAsBytes()`/`.ReadAsJson<T>()`),
and `catch (SdkException<{Operation}Error> ex)` gives `ex.Error` as that typed instance, with every
`TryGet…` an instance method returning `bool` and `out`-populating its payload on `true`.

Every operation above is throw-only (`SdkException<TError>`; no `*Result` no-throw sibling exists anywhere
in this SDK). Two shapes:

- **Case A (typed)**: catch `SdkException<{Operation}Error>` (namespace `MaxioAdvancedBilling.Errors`);
  enumerate every `TryGet…` accessor listed per-op above, in order, `TryGetRawError` last.
- **Case B (raw)**: catch `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` directly; read
  `.StatusCode` / `.ReadAsString()` / `.ReadAsJson<T>()`.

**Single domain exception (item 9)** — wrap every call site:

```csharp
catch (SdkException<SomeOperationError> ex)              // Case A — per-op typed accessors first, TryGetRawError last
{
    // extract best-effort message from whichever typed accessor returns true;
    // fall back to ex.Error.TryGetRawError(out var raw) ? raw.ReadAsString() : ex.Message
    throw new MaxioIntegrationException(message, ex);
}
catch (SdkException<RawError> ex)                         // Case B
{
    throw new MaxioIntegrationException(ex.Error.ReadAsString(), ex) { StatusCode = ex.Error.StatusCode };
}
catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)  // connection failure — not an SdkException
{
    throw new MaxioIntegrationException("Maxio unreachable", ex);
}
```

Distinguish the four category needs the user listed (validation / 404 / provider rejection / connection) by
`StatusCode` on the wrapped exception: 422 → validation, 404 → not found, other 4xx/5xx → provider
rejection, no status → connection failure.

### Enum reference

**Enum type shape (source: `Core/Enum/TypedEnum.cs`, `Core/Enum/StringEnum.cs`, and each concrete file
below) — none of these, including `ServerEnvironment`, is a real C# `enum`.** Each is a `sealed record`
(`ServerEnvironment` is a plain, non-sealed `record`) inheriting `StringEnum<TEnum> : TypedEnum<string,
TEnum>`; the listed "values" (`ComponentKind.MeteredComponent`, `SubscriptionState.Active`, `IntervalUnit.Day`,
`ServerEnvironment.Us`, …) are `public static readonly` instances with a private ctor, not enum literals.
Usage:
- **Equality** — plain `==`/`.Equals` work (record-synthesized value equality over the single `Value`
  property): `if (component.Kind == ComponentKind.MeteredComponent)`.
- **`switch`** — `case ComponentKind.MeteredComponent:` does **not** compile (`static readonly` fields
  aren't compile-time constants → CS0150). Use `when`-guards (`x switch { var s when s == SubscriptionState.Active => ... }`)
  or switch on the raw wire string via `.Value`.
- **Wire string** — `.ToString()` is overridden to return `Value` (the wire string) directly, e.g.
  `ComponentKind.MeteredComponent.ToString() == "metered_component"`; `.Value` gives the identical string.
- **Implicit conversion** — one direction only: `TypedEnum<TValue,TEnum>` defines
  `public static implicit operator TValue(...)`, so each of these converts implicitly to its underlying
  `string` (all four here are `StringEnum<T>`, so `TValue = string`) — `string wire = ComponentKind.MeteredComponent;`
  compiles with no `.Value` needed. There is **no** implicit conversion the other way; for a
  not-known-at-compile-time wire value use the static factory `{EnumType}.FromValue(string)` (never throws —
  wraps unknown values too) or guard with the static `TryGetKnownValue(string, out {EnumType}?)`.

| Enum | Namespace | Values |
|---|---|---|
| `ComponentKind` | `MaxioAdvancedBilling.Models.Enums` | `MeteredComponent`(`metered_component`), `QuantityBasedComponent`, `OnOffComponent`, `PrepaidUsageComponent`, `EventBasedComponent` |
| `SubscriptionState` | `MaxioAdvancedBilling.Models.Enums` | `Pending`, `FailedToCreate`, `Trialing`, `Assessing`, `Active`, `SoftFailure`, `PastDue`, `Suspended`, `Canceled`, `Expired`, `Paused`, `Unpaid`, `TrialEnded`, `OnHold`, `AwaitingSignup` |
| `SubscriptionStateFilter` (query-param enum for `ListSubscriptions`) | `MaxioAdvancedBilling.Models.Enums` | `Active`, `Canceled`, `Expired`, `ExpiredCards`, `OnHold`, `PastDue`, `PendingCancellation`, `PendingRenewal`, `Suspended`, `TrialEnded`, `Trialing`, `Unpaid` |
| `IntervalUnit` | `MaxioAdvancedBilling.Models.Enums` | `Day`, `Month` |
| `ResumptionCharge` | `MaxioAdvancedBilling.Models.Enums` | `Prorated`, `Immediate`, `Delayed` |
| `ReactivationCharge` | `MaxioAdvancedBilling.Models.Enums` | `Prorated`, `Immediate`, `Delayed` |
| `CancellationMethod` | `MaxioAdvancedBilling.Models.Enums` | `MerchantUi`, `MerchantApi`, `Dunning`, `BillingPortal`, `Unknown`, `Imported` |
| `CollectionMethod` | `MaxioAdvancedBilling.Models.Enums` | `Automatic`(`automatic`), `Remittance`(`remittance`), `Prepaid`(`prepaid`), `Invoice`(`invoice`) — per the map's own summary: legacy Statements-Architecture sites use `Invoice`/`Automatic`; current Relationship Invoicing Architecture sites use `Remittance`/`Automatic`/`Prepaid` |

### Unions reference

| Union | Namespace | Variants | Factories / readers |
|---|---|---|---|
| `SubscriptionIdOrReference` | `MaxioAdvancedBilling.Models.AnyOf` | `int`, `string` | `.Int(int)`/`.String(string)`; `TryGetInt`/`TryGetString` |
| `ComponentIdModel` | `MaxioAdvancedBilling.Models.AnyOf` | `int`, `string` | `.Int(int)`/`.String(string)`; `TryGetInt`/`TryGetString` |
| `Resume` | `MaxioAdvancedBilling.Models.AnyOf` | `bool`, `ResumeOptions` | `.Bool(bool)`/`.ResumeOptions(ResumeOptions)`; `TryGetBool`/`TryGetResumeOptions` |
| `Quantity1` (on `Usage.Quantity`) | `MaxioAdvancedBilling.Models.AnyOf` | `int`, `string` | `TryGetInt`/`TryGetString` |

---

## 3. Trap notes

- **Step 1 (client construction)**: the `HttpClient` you pass in is not owned by the SDK — construct one
  long-lived instance (or use `IHttpClientFactory`) and reuse it; do not build one per request. The
  `MaxioAdvancedBillingClient` itself is also meant to be long-lived; register it once in DI, don't
  `new` it per call.
- **Step 1 (auth)**: set `BasicAuth` before constructing the client, or inside the `AddMaxioAdvancedBillingClient`
  DI callback — never after. Load the API key from configuration, never hardcode it.
- **Step 2 (product families)**: `ReadProductFamily(int id, ...)` takes only an `int` — you cannot pass
  `"handle:eshop-subscribe"` through the typed method despite the endpoint's prose describing that path
  form; resolve the handle via `ListProductFamilies()` + client-side filter instead.
- **Step 2 (products)**: `Product` carries **two** similarly-named, easy-to-swap booleans —
  `RequestCreditCard` and `RequireCreditCard` — verified independently in the source (`Models/Product.cs`
  row in the map). Gate "no card capture" logic on `RequireCreditCard`, and treat `RequestCreditCard` as UI
  hint only; do not conflate the two in code or tests.
- **Step 3 (customer create error)**: the typed 422 payload `CustomerErrorResponse1.Errors` deserializes to
  the shared `Errors` model, whose only fields are `PerPage`/`PricePoint` (confirmed by reading
  `Models/CustomerErrorResponse1.cs` and `Models/Errors.cs` directly — this is a generic/shared error shape
  reused across unrelated operations, not a customer-specific validation payload). **Do not build a
  customer-facing message from `TryGetCustomerErrorResponse1`'s fields** — they will not carry a customer
  validation reason. Defensive coding: always fall back to `TryGetRawError(out var raw)` and
  `raw.ReadAsString()` for the actual `{"errors": [...]}` body on this operation's 422, and only use the
  typed accessor to confirm "this was a 422" if needed.
- **Step 3 (customer create fields)**: `FirstName`/`LastName`/`Email` are all `required` on `CreateCustomer`
  — if eShopOnWeb only has an email/username at signup, you must still supply non-empty first/last name
  values (e.g. derive a placeholder, or require the profile to carry them) or the call fails client-side
  before any network round-trip.
- **Step 4 (component validation)**: `FindComponent` is a **site-wide** lookup by handle (no product-family
  scoping) — simpler than `ReadComponent`, which requires the numeric `productFamilyId`. Prefer
  `FindComponent` unless you specifically need family-scoped disambiguation of a handle that repeats across
  families.
- **Step 5 (duplicate detection)**: `ListCustomerSubscriptions` has **no product/state filter parameter** —
  it returns every subscription the customer has ever had, across every product/family. Filter client-side
  on `Subscription.Product?.Handle` and `Subscription.State`.
- **Step 6 (usage recording)**: `CreateUsage`/`ListUsages` take `SubscriptionIdOrReference` and
  `ComponentIdModel` — both `AnyOf<int,string>` unions, not plain `int`/`string` parameters. Build with
  `SubscriptionIdOrReference.Int(...)`/`.String(...)` and `ComponentIdModel.Int(...)`/`.String(...)`; there
  is no object-initializer syntax for a union.
- **Step 6 (usage total)**: `ListUsages` returns the individual usage log, **not** a running total; the
  accumulated period-to-date quantity is `SubscriptionComponent.UnitBalance` from
  `ReadSubscriptionComponent`. Reaching for `ListUsages` and summing client-side would double-count if any
  entries are corrections/negative-quantity deductions — use `UnitBalance` directly.
- **Step 6 (retries)**: `CreateUsage` is a `POST`; the SDK's default `RetryOptions.HttpMethodsToRetry` is
  `GET/HEAD/PUT/OPTIONS` only, so a transient 5xx on usage recording is **not** retried automatically. If
  usage recording needs to be resilient to transient failures, add your own retry/idempotency at the call
  site (the API has no client-supplied idempotency key on this endpoint per the map/source read).
- **Step 7 (plan change)**: `MigrateSubscriptionProduct`/`PreviewSubscriptionProductMigration` is the
  immediate+prorated path; `UpdateSubscription` with `ProductChangeDelayed = true` is the delayed+no-proration
  path — these are two different controllers/operations, not two modes of one call. There is no typed
  preview op for the delayed path (only `PreviewRenewal` comes close, and it previews a renewal, not a
  pending product change).
- **Step 7 (`Proration.PreservePeriod`)**: the map/source only name this field (`Proration { PreservePeriod:
  bool? }`) — its exact business effect beyond "preserve the current period" is not further specified in
  the map or source docstrings, and no training-data claim about live behavior may be used. Label as
  `UNVERIFIED`: confirm the sandbox's actual proration math for both `PreservePeriod = true` and `false`/omitted
  before shipping a UI that promises a specific proration outcome.
- **Step 8 (`SubscriptionState.Paused` vs `.OnHold`)**: the enum has both a `Paused` and an `OnHold`
  constant; `PauseSubscription`/`ResumeSubscription` operate on the `/hold.json` endpoint and, per its
  notes, produce the on-hold state — but no map row or source doc-comment read this session confirms
  whether any operation ever yields `Paused` instead. `UNVERIFIED` — write lifecycle-state comparisons
  against `OnHold` and treat an observed `Paused` value defensively (log and treat as "not active") rather
  than asserting it can never occur.
- **Step 9 (error handling in general)**: `TryGetRawError` is not a catch-all — it only fires for statuses
  that have no more-specific typed accessor on that `{Operation}Error`. Enumerate every `TryGet…` the sheet
  lists for an operation before falling back to it, and never write a shared helper typed as `ApiError`
  hoping to reach a typed accessor — those live only on the concrete `{Operation}Error`.
- **Step 10 (verify-on-the-wire)**: path/template parameters are not type-checked against the route
  internally — a wrong verb or a leftover `{placeholder}` compiles cleanly and only shows up as a runtime
  404/422. Attach a logging `DelegatingHandler` to the `HttpClient` for the first real run of each new call
  and confirm verb, path, and query string before trusting the integration.

---

## 4. Assumptions & Blockers

- **Which `SubscriptionState` values count as "active" for duplicate-enrollment detection** is a product
  decision the map cannot answer. Assumed: `Active`, `Trialing`, `PastDue`, `OnHold` block re-enrollment
  (the customer already has a live relationship with that product); `Canceled`, `Expired`, `TrialEnded`,
  `Unpaid`, `AwaitingSignup`, `Pending`, `FailedToCreate`, `SoftFailure`, `Assessing`, `Suspended`, `Paused`
  do not. Revisit this list with the product owner before shipping — it is a policy choice, not an SDK fact.
- **Reference-string format** for the customer `reference` field is assumed to be the eShopOnWeb user's
  email/username verbatim (per the task description); no SDK-side format constraint was found beyond
  "must be unique per customer" (`Customers.md` notes).
  Assumed `FirstName`/`LastName` will be sourced from the eShopOnWeb identity profile if present, otherwise
  a placeholder (e.g. the local part of the email) — this is a product decision, not resolvable from the SDK.
- **No blockers.** Every operation, model field, enum value, and error case named in this sheet was
  resolved from the SDK map (and, for the one flagged discrepancy, cross-checked against the exact source
  files `Models/CustomerErrorResponse1.cs` / `Models/Errors.cs`) — nothing here is a `SOURCE-LOOKUP NEEDED`
  punt.
