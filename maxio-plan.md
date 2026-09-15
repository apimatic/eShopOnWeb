# Maxio Advanced Billing — eShopOnWeb subscription billing plan

NuGet `AsadAli.AdvancedBilling.Sdk` · root namespace `MaxioAdvancedBilling` · map stamp `v1.0.2` / `15db14b` (`sdk-map.md`).

## Scope & sequence

Working storefront billing: customers, subscriptions, products/plans, components, coupons, usage, invoices, plan changes. Payment profiles and site lookup are required support ops (a subscription cannot collect without a profile or token). Subscription groups, custom fields, offers, and webhooks are out of this pass.

| Step | Work | Operations |
|---|---|---|
| 1 | Install package; register client + Basic auth + site subdomain | ctor / `AddMaxioAdvancedBillingClient`; `Sites.ReadSite` (health) |
| 2 | Catalog — families, products (plans), product price points | `ProductFamilies.CreateProductFamily`, `ListProductFamilies`, `ReadProductFamily`; `Products.CreateProduct`, `ListProducts`, `ReadProduct`, `ReadProductByHandle`, `ListProductsForProductFamily`; `ProductPricePoints.CreateProductPricePoint`, `ListProductPricePoints`, `ReadProductPricePoint`, `PromoteProductPricePointToDefault` |
| 3 | Catalog — components | `Components.CreateMeteredComponent`, `CreateQuantityBasedComponent`, `CreateOnOffComponent`, `CreatePrepaidUsageComponent`, `ListComponents`, `FindComponent`, `ReadComponent`; `ComponentPricePoints.CreateComponentPricePoint`, `ListComponentPricePoints` |
| 4 | Catalog — coupons | `Coupons.CreateCoupon`, `FindCoupon`, `ValidateCoupon`, `ListCoupons`, `ReadCoupon` |
| 5 | Customers | `Customers.CreateCustomer`, `ReadCustomer`, `ReadCustomerByReference`, `UpdateCustomer`, `ListCustomerSubscriptions`, `ListCustomers` |
| 6 | Payment method | `PaymentProfiles.CreatePaymentProfile` (prefer `chargify_token` from Maxio.js), `ListPaymentProfiles`, `ReadPaymentProfile`, `ChangeSubscriptionDefaultPaymentProfile`; `Sites.ListChargifyJsPublicKeys` |
| 7 | Subscribe | `Subscriptions.PreviewSubscription`, `CreateSubscription`, `ReadSubscription`, `FindSubscription`, `ListSubscriptions`, `UpdateSubscription`, `ActivateSubscription` |
| 8 | Plan change (migrate / delayed) | `SubscriptionProducts.PreviewSubscriptionProductMigration`, `MigrateSubscriptionProduct`; delayed change via `Subscriptions.UpdateSubscription` (`product_change_delayed`) |
| 9 | Allocations + usage | `SubscriptionComponents.ListSubscriptionComponents`, `ReadSubscriptionComponent`, `PreviewAllocations`, `AllocateComponent`, `CreateUsage`, `ListUsages` |
| 10 | Coupons on a subscription | `Subscriptions.ApplyCouponsToSubscription`, `RemoveCouponFromSubscription` |
| 11 | Invoices | `Invoices.ListInvoices`, `ReadInvoice`, `CreateInvoice`, `IssueInvoice`, `RecordPaymentForInvoice`, `RecordPaymentForSubscription`, `RefundInvoice`, `VoidInvoice`, `SendInvoice` |
| 12 | Lifecycle | `SubscriptionStatus.CancelSubscription`, `InitiateDelayedCancellation`, `CancelDelayedCancellation`, `PauseSubscription`, `ResumeSubscription`, `ReactivateSubscription`, `RetrySubscription`, `PreviewRenewal` |
| 13 | Error boundary + tests | throw-only SDK; Case A vs Case B per row below |

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

All records below: `MaxioAdvancedBilling.Models`. Enums: `MaxioAdvancedBilling.Models.Enums`. OneOf: `MaxioAdvancedBilling.Models.OneOf`. AnyOf: `MaxioAdvancedBilling.Models.AnyOf`. Typed errors: `MaxioAdvancedBilling.Errors`. Controllers: `MaxioAdvancedBilling.Api` via client properties.

`!req` = C# `required` (object initializer). Trailing `?` = optional. Nullable params with **no C# default** must be passed explicitly (`null` to skip).

### Client, auth, servers (`sdk-map.md`)

| Fact | Value |
|---|---|
| Package | `AsadAli.AdvancedBilling.Sdk` (not a project reference) |
| Client | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` |
| Ctor | `MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)` — only ctor |
| DI | `AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>)` (`ServiceCollectionExtensions.cs`) |
| Options | `Environment`: `MaxioAdvancedBilling.Servers.ServerEnvironment`; `Retry`: `MaxioAdvancedBilling.Core.Configuration.RetryOptions`; `Server`: `MaxioAdvancedBilling.ServerOptions`; `BasicAuth`: `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?` |
| Auth | HTTP Basic only. `BasicAuthCredentials { Username = "<api_key>", Password = "x" }` — password is the literal `"x"` |
| Environments | `ServerEnvironment.Us` (`US`, default) → `https://{site}.chargify.com`; `ServerEnvironment.Eu` (`EU`) → `https://{site}.ebilling.maxio.com` |
| Site subdomain | `options.Server.Production.Us.Site = "<subdomain>"` (and `.Eu.Site` if EU). Mock host: override `options.Server.Production.Us.BaseUrl` |
| Ebb (events ingest only) | `https://events.chargify.com/{site}` — used by `RecordEvent` / `BulkRecordEvents`, not by `CreateUsage` |
| RetryOptions | namespace `MaxioAdvancedBilling.Core.Configuration`; all members `required`; start from `RetryOptions.Default()` or set every member: `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry` |
| Throw model | every operation is throw-only — no `…Result` variants. Case A: `MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>` where `TError : MaxioAdvancedBilling.Core.ErrorResponse.ApiError` with status `TryGet…` + inherited `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)`. Case B: `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` |

### Operations

#### `client.Sites` — `operations/Sites.md` · `Api/Sites.cs`

| Op | Signature | Request | Response envelope (read) | Error | Pagination |
|---|---|---|---|---|---|
| `ReadSite` | `ReadSite(CancellationToken ct = default)` | none | `MaxioAdvancedBilling.Models.SiteResponse` → `Site (site): Site !req`. Inner: `Id`, `Name`, `Subdomain`, `Currency`, `RelationshipInvoicingEnabled`, `Test` | **B** `SdkException<RawError>` | none |
| `ListChargifyJsPublicKeys` | `ListChargifyJsPublicKeys(int? page = 1, int? perPage = 20, CancellationToken ct = default)` | query `page`, `per_page` | `ListPublicKeysResponse` → `ChargifyJsKeys (chargify_js_keys): IReadOnlyList<PublicKey>?`. `PublicKey.PublicKeyValue (public_key)` | **B** | `page`+`perPage` |

#### `client.ProductFamilies` — `operations/ProductFamilies.md` · `Api/ProductFamilies.cs`

| Op | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `CreateProductFamily` | `CreateProductFamily(CreateProductFamilyRequest? body, CancellationToken ct = default)` — `body` must pass | Envelope `ProductFamily (product_family): CreateProductFamily !req`. Inner `CreateProductFamily`: `Name (name): string !req`, `Handle (handle): string?`, `Description (description): string?` | `ProductFamilyResponse` → `ProductFamily (product_family): ProductFamily?` (`Id`, `Name`, `Handle`) | **A** `SdkException<CreateProductFamilyError>` · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` | none |
| `ListProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — 5 nullable, no default, **must pass** (`null` to skip) | query `date_field`, `start_date`, `end_date`, `start_datetime`, `end_datetime` | `IReadOnlyList<ProductFamilyResponse>` | **B** | none |
| `ReadProductFamily` | `ReadProductFamily(int id, CancellationToken ct = default)` | path id (notes: handle form `handle:my-family` is HTTP-level; C# param is `int id`) | `ProductFamilyResponse` | **B** | none |
| `ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 middle params **must pass** | `productFamilyId` is `string` (not `int`). Filter: `Ids`, `PrepaidProductPricePoint`, `UseSiteExchangeRate` (`records-2`) | `IReadOnlyList<ProductResponse>` | **A** `SdkException<ListProductsForProductFamilyError>` · `TryGetString(out string)` [404] · `TryGetRawError` | `page`+`perPage` |

#### `client.Products` — `operations/Products.md` · `Api/Products.cs`

| Op | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `CreateProduct` | `CreateProduct(string productFamilyId, CreateOrUpdateProductRequest? body, CancellationToken ct = default)` — `body` must pass | Envelope `Product (product): CreateOrUpdateProduct !req`. Inner **required**: `Name (name)`, `Description (description)`, `PriceInCents (price_in_cents): long`, `Interval (interval): int`, `IntervalUnit (interval_unit): IntervalUnit`. Optional: `Handle`, `AccountingCode`, `RequireCreditCard`, `TrialPriceInCents`, `TrialInterval`, `TrialIntervalUnit`, `TrialType`, `ExpirationInterval`, `ExpirationIntervalUnit`, `TaxCode`, `AutoCreateSignupPage` | `ProductResponse` → `Product (product): Product !req` (`Id`, `Name`, `Handle`, `PriceInCents`, `Interval`, `IntervalUnit`, `ProductPricePointId`, `DefaultProductPricePointId`, `ProductFamily`) | **A** `CreateProductError` · `TryGetErrorListResponse1` [422] | none |
| `ListProducts` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 **must pass** | query `date_field`, `filter`, `end_date`, `end_datetime`, `start_date`, `start_datetime`, `page`, `per_page`, `include_archived`, `include` | `IReadOnlyList<ProductResponse>` | **B** | `page`+`perPage` (default 20) |
| `ReadProduct` | `ReadProduct(int productId, CancellationToken ct = default)` | | `ProductResponse` | **B** | none |
| `ReadProductByHandle` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | | `ProductResponse` | **B** | none |
| `UpdateProduct` | `UpdateProduct(int productId, CreateOrUpdateProductRequest? body, CancellationToken ct = default)` — `body` must pass | same body as create. **Notes:** updating via this op creates a **new default price point** | `ProductResponse` | **A** `UpdateProductError` · `TryGetErrorListResponse1` [422] | none |
| `ArchiveProduct` | `ArchiveProduct(int productId, CancellationToken ct = default)` | | `ProductResponse` | **A** `ArchiveProductError` · `TryGetErrorListResponse1` [422] | none |

#### `client.ProductPricePoints` — `operations/ProductPricePoints.md` · `Api/ProductPricePoints.cs`

Path ids `ProductIdModel` / `PricePointIdModel` are **AnyOf int\|string** (`unions.md`): factories `ProductIdModel.Int(int)` / `.String(string)` (handle form); same for `PricePointIdModel`. Implicit from `int`/`string`. Namespace `MaxioAdvancedBilling.Models.AnyOf`.

| Op | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `CreateProductPricePoint` | `CreateProductPricePoint(ProductIdModel productId, CreateProductPricePointRequest? body, CancellationToken ct = default)` — `body` must pass | Envelope `PricePoint (price_point): CreateProductPricePoint !req`. Inner **required**: `Name`, `PriceInCents: long`, `Interval: int`, `IntervalUnit`. Optional: `Handle`, trial/expiration fields, `UseSiteExchangeRate` default `true` | `ProductPricePointResponse` → `PricePoint (price_point): ProductPricePoint !req` (`Id`, `Handle`, `PriceInCents`, `Type`) | **A** `CreateProductPricePointError` · `TryGetProductPricePointErrorResponse1(out ProductPricePointErrorResponse1)` [422] — inner `Errors: ProductPricePointErrors !req` (`PricePoint`, `Interval`, `IntervalUnit`, `Name`, `Price`, `PriceInCents`) | none |
| `ListProductPricePoints` | `ListProductPricePoints(ProductIdModel productId, bool? currencyPrices, IReadOnlyList<PricePointType>? filterType, bool? archived, int? page = 1, int? perPage = 10, CancellationToken ct = default)` — 3 middle **must pass** | query `currency_prices`, `filter[type]`, `archived` | `ListProductPricePointsResponse` → `PricePoints (price_points): IReadOnlyList<ProductPricePoint> !req` | **B** | `page`+`perPage` (default **10**) |
| `ReadProductPricePoint` | `ReadProductPricePoint(ProductIdModel productId, PricePointIdModel pricePointId, bool? currencyPrices, CancellationToken ct = default)` — `currencyPrices` must pass | query `currency_prices` | `ProductPricePointResponse` | **B** | none |
| `PromoteProductPricePointToDefault` | `PromoteProductPricePointToDefault(int productId, int pricePointId, CancellationToken ct = default)` | ints, not unions | `ProductResponse` | **B** | none |
| `UpdateProductPricePoint` | `UpdateProductPricePoint(ProductIdModel productId, PricePointIdModel pricePointId, UpdateProductPricePointRequest? body, CancellationToken ct = default)` — `body` must pass | Envelope `PricePoint: UpdateProductPricePoint !req` — `Handle?`, `PriceInCents?`. Custom price points cannot be updated (op notes) | `ProductPricePointResponse` | **B** | none |

#### `client.Components` — `operations/Components.md` · `Api/Components.cs`

Create bodies wrap a typed inner with a **kind-specific wire name**.

| Op | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `CreateMeteredComponent` | `CreateMeteredComponent(string productFamilyId, CreateMeteredComponent? body, CancellationToken ct = default)` — `body` must pass | Envelope `MeteredComponent (metered_component): MeteredComponent !req`. Inner **required**: `Name`, `UnitName`, `PricingScheme`. Optional: `Handle`, `Prices` (`Price` rows), `UnitPrice` (union `UnitPrice1`), `Taxable`, `Interval`/`IntervalUnit` | `ComponentResponse` → `Component (component): Component !req` (`Id`, `Handle`, `Kind`, `DefaultPricePointId`) | **A** `CreateMeteredComponentError` · `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1` [422] | none |
| `CreateQuantityBasedComponent` | `CreateQuantityBasedComponent(string productFamilyId, CreateQuantityBasedComponent? body, …)` — `body` must pass | Envelope `QuantityBasedComponent (quantity_based_component): QuantityBasedComponent !req`. **Required**: `Name`, `UnitName`, `PricingScheme`. Set `Recurring (recurring): bool?` for recurring vs one-time | `ComponentResponse` | **A** same 404/422 pattern | none |
| `CreateOnOffComponent` | `CreateOnOffComponent(string productFamilyId, CreateOnOffComponent? body, …)` — `body` must pass | Envelope `OnOffComponent (on_off_component): OnOffComponent !req`. **Required**: `Name`, `UnitPrice (unit_price): UnitPrice3` (union, **required**) | `ComponentResponse` | **A** same 404/422 | none |
| `CreatePrepaidUsageComponent` | `CreatePrepaidUsageComponent(string productFamilyId, CreatePrepaidComponent? body, …)` — `body` must pass | Envelope `PrepaidUsageComponent (prepaid_usage_component): PrepaidUsageComponent !req`. **Required**: `Name`, `UnitName`, `PricingScheme`, `OveragePricing (overage_pricing): OveragePricing !req` (`PricingScheme !req`, `Prices?`) | `ComponentResponse` | **A** same 404/422 | none |
| `CreateEventBasedComponent` | `CreateEventBasedComponent(string productFamilyId, CreateEbbComponent? body, …)` — `body` must pass | Envelope `EventBasedComponent (event_based_component): EbbComponent !req`. **Required**: `Name`, `UnitName`, `PricingScheme`, `EventBasedBillingMetricId: int` | `ComponentResponse` | **A** same 404/422 | none |
| `ListComponents` | `ListComponents(BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, bool? includeArchived, ListComponentsFilter? filter, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 7 **must pass**. Date params are **`string?`**, not `DateTimeOffset` | Filter: `Ids`, `UseSiteExchangeRate` | `IReadOnlyList<ComponentResponse>` | **B** | `page`+`perPage` |
| `ListComponentsForProductFamily` | `ListComponentsForProductFamily(int productFamilyId, bool? includeArchived, ListComponentsFilter? filter, BasicDateField? dateField, string? endDate, string? endDatetime, string? startDate, string? startDatetime, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 7 **must pass**. Family id is **`int`** (create ops take `string productFamilyId`) | | `IReadOnlyList<ComponentResponse>` | **B** | `page`+`perPage` |
| `FindComponent` | `FindComponent(string handle, CancellationToken ct = default)` | query `handle` | `ComponentResponse` | **B** | none |
| `ReadComponent` | `ReadComponent(int productFamilyId, string componentId, CancellationToken ct = default)` | `componentId` may be id or `handle:…` | `ComponentResponse` | **B** | none |
| `UpdateComponent` | `UpdateComponent(string componentId, UpdateComponentRequest? body, CancellationToken ct = default)` — `body` must pass | Envelope `Component (component): UpdateComponent !req` (`Handle`, `Name`, `Description`, `Taxable`, `UpgradeCharge`, …) | `ComponentResponse` | **A** `UpdateComponentError` · `TryGetErrorListResponse1` [422] | none |
| `ArchiveComponent` | `ArchiveComponent(int productFamilyId, string componentId, CancellationToken ct = default)` | | **`Component`** (not `ComponentResponse`) | **A** `ArchiveComponentError` · `TryGetErrorListResponse1` [422] | none |

`Price` (`records-3`): `StartingQuantity !req` (union `StartingQuantity`), `EndingQuantity?` (union), `UnitPrice !req` (union `UnitPrice`). Build via factories on those AnyOf types (`unions.md`).

#### `client.ComponentPricePoints` — `operations/ComponentPricePoints.md`

| Op | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `CreateComponentPricePoint` | `CreateComponentPricePoint(int componentId, CreateComponentPricePointRequest? body, CancellationToken ct = default)` — `body` must pass | Envelope `PricePoint (price_point): PricePoint !req` (**union** `CreateComponentPricePoint` \| `CreatePrepaidUsageComponentPricePoint`). Catalog PP **required**: `Name`, `PricingScheme`, `Prices`. Prepaid PP also **requires** `OveragePricing` | `ComponentPricePointResponse` → `PricePoint (price_point): ComponentPricePoint !req` | **A** `CreateComponentPricePointError` · `TryGetErrorArrayMapResponse1(out ErrorArrayMapResponse1)` [422] | none |
| `ListComponentPricePoints` | `ListComponentPricePoints(int componentId, bool? currencyPrices, IReadOnlyList<PricePointType>? filterType, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 2 **must pass** | query `currency_prices`, `filter[type]` | `ComponentPricePointsResponse` → `PricePoints`, `Meta` | **B** | `page`+`perPage` |
| `ReadComponentPricePoint` | `ReadComponentPricePoint(ComponentIdModel componentId, PricePointIdModel pricePointId, bool? currencyPrices, …)` — `currencyPrices` must pass | | `ComponentPricePointCurrencyOverageResponse` → `PricePoint (price_point): CurrencyOveragePrices !req` | **B** | none |

#### `client.Coupons` — `operations/Coupons.md` · `Api/Coupons.cs`

| Op | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `CreateCoupon` | `CreateCoupon(int productFamilyId, CouponRequest? body, CancellationToken ct = default)` — `body` must pass | `Coupon (coupon): CouponPayload?` — amount coupon: `AmountInCents`; percent: `Percentage` (union `Percentage`). Optional `RestrictedProducts` / `RestrictedComponents` as `IReadOnlyDictionary<string, bool>` keyed by id | `CouponResponse` → `Coupon (coupon): Coupon?` (`Id`, `Code`, `AmountInCents`, `Percentage`, `Stackable`, `DiscountType`) | **A** `CreateCouponError` · `TryGetErrorListResponse1` [422] | none |
| `ValidateCoupon` | `ValidateCoupon(string code, int? productFamilyId, CancellationToken ct = default)` — `productFamilyId` must pass | query `code`, `product_family_id`. Pass family when the coupon is not on the site's first family | `CouponResponse` | **A** `ValidateCouponError` · `TryGetSingleStringErrorResponse1(out SingleStringErrorResponse1)` [404] — `Errors (errors): string?` | none |
| `FindCoupon` | `FindCoupon(int? productFamilyId, string? code, bool? currencyPrices, CancellationToken ct = default)` — all 3 **must pass** | query `product_family_id`, `code`, `currency_prices` | `CouponResponse` | **B** | none |
| `ListCoupons` | `ListCoupons(ListCouponsFilter? filter, bool? currencyPrices, int? page = 1, int? perPage = 30, CancellationToken ct = default)` — 2 **must pass** | Filter: `DateField`, `StartDate`/`EndDate` (`DateTimeOffset?`), `Ids`, `Codes`, `IncludeArchived`, `UseSiteExchangeRate` | `IReadOnlyList<CouponResponse>` | **B** | `page`+`perPage` (default **30**) |
| `ReadCoupon` | `ReadCoupon(int productFamilyId, int couponId, bool? currencyPrices, CancellationToken ct = default)` — `currencyPrices` must pass | | `CouponResponse` | **B** | none |
| `UpdateCoupon` | `UpdateCoupon(int productFamilyId, int couponId, CouponRequest? body, …)` — `body` must pass | same as create | `CouponResponse` | **A** `UpdateCouponError` · `TryGetErrorListResponse1` [422] | none |
| `ArchiveCoupon` | `ArchiveCoupon(int productFamilyId, int couponId, …)` | | `CouponResponse` | **B** | none |

#### `client.Customers` — `operations/Customers.md` · `Api/Customers.cs`

| Op | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` must pass | Envelope `Customer (customer): CreateCustomer !req`. **Required**: `FirstName (first_name)`, `LastName (last_name)`, `Email (email)`. Optional: `Reference` (unique app id — ISO country/state 2-char on `Country`/`State`) | `CustomerResponse` → `Customer (customer): Customer !req` (`Id`, `Reference`, `Email`, `FirstName`, `LastName`) | **A** `CreateCustomerError` · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError`. Payload `Errors (errors): Errors?` whose mapped members are only `PerPage`, `PricePoint` (`records-2`) — **suspicious shared model**; see Assumptions | none |
| `ReadCustomer` | `ReadCustomer(int id, CancellationToken ct = default)` | | `CustomerResponse` | **B** | none |
| `ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | query `reference` | `CustomerResponse` | **B** | none |
| `UpdateCustomer` | `UpdateCustomer(int id, UpdateCustomerRequest? body, CancellationToken ct = default)` — `body` must pass | Envelope `Customer (customer): UpdateCustomer !req` — all inner fields optional | `CustomerResponse` | **A** `UpdateCustomerError` · `TryGetNoContent(out RawError)` [404] · `TryGetCustomerErrorResponse1` [422] | none |
| `ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | | `IReadOnlyList<SubscriptionResponse>` | **B** | none |
| `ListCustomers` | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — 7 **must pass**. Dates are **`string?`** | query `q` for email/reference/name search | `IReadOnlyList<CustomerResponse>` | **B** | `page`+`perPage` (default **50**) |
| `DeleteCustomer` | `DeleteCustomer(int id, CancellationToken ct = default)` | | `void` | **B** | none |

#### `client.PaymentProfiles` — `operations/PaymentProfiles.md` · `Api/PaymentProfiles.cs`

| Op | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `CreatePaymentProfile` | `CreatePaymentProfile(CreatePaymentProfileRequest? body, CancellationToken ct = default)` — `body` must pass | Envelope `PaymentProfile (payment_profile): CreatePaymentProfile !req`. Storefront: set `ChargifyToken (chargify_token)` + `CustomerId`. Raw `FullNumber`/`Cvv` requires PCI. Does **not** auto-attach to a subscription | `PaymentProfileResponse` → `PaymentProfile (payment_profile): PaymentProfile !req` (**OneOf** — `TryGetCreditCardPaymentProfile` / `TryGetBankAccountPaymentProfile` / `TryGetPaypalPaymentProfile` / `TryGetApplePayPaymentProfile`). Card inner: `Id`, `MaskedCardNumber`, `CustomerId`, `PaymentType` | **A** `CreatePaymentProfileError` · `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] | none |
| `ListPaymentProfiles` | `ListPaymentProfiles(int? customerId, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — `customerId` must pass | query `customer_id` | `IReadOnlyList<PaymentProfileResponse>` (empty list, not 404, when none) | **B** | `page`+`perPage` |
| `ReadPaymentProfile` | `ReadPaymentProfile(int paymentProfileId, CancellationToken ct = default)` | | `PaymentProfileResponse` | **A** `ReadPaymentProfileError` · `TryGetNoContent` [404] | none |
| `ChangeSubscriptionDefaultPaymentProfile` | `ChangeSubscriptionDefaultPaymentProfile(int subscriptionId, int paymentProfileId, CancellationToken ct = default)` | | `PaymentProfileResponse` | **A** `ChangeSubscriptionDefaultPaymentProfileError` · `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] | none |
| `UpdatePaymentProfile` | `UpdatePaymentProfile(int paymentProfileId, UpdatePaymentProfileRequest? body, …)` — `body` must pass | Envelope `PaymentProfile: UpdatePaymentProfile !req` | `PaymentProfileResponse` | **A** `UpdatePaymentProfileError` · `TryGetNoContent` [404] · `TryGetErrorStringMapResponse1(out ErrorStringMapResponse1)` [422] — `Errors: IReadOnlyDictionary<string, string>?` | none |
| `ReadOneTimeToken` | `ReadOneTimeToken(string chargifyToken, CancellationToken ct = default)` | | `GetOneTimeTokenRequest` → `PaymentProfile: GetOneTimeTokenPaymentProfile !req` | **A** `ReadOneTimeTokenError` · `TryGetErrorListResponse1` [404] | none |

#### `client.Subscriptions` — `operations/Subscriptions.md` · `Api/Subscriptions.cs`

| Op | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must pass | Envelope `Subscription (subscription): CreateSubscription !req`. Identify product: `ProductId` **or** `ProductHandle`; price point: `ProductPricePointId` / `ProductPricePointHandle`. Identify customer: `CustomerId` **or** `CustomerReference` **or** nested `CustomerAttributes`. Payment: `PaymentProfileId` **or** `PaymentProfileAttributes` / `CreditCardAttributes` (`ChargifyToken`) **or** `BankAccountAttributes`. Coupons: `CouponCode` or `CouponCodes`. Components at signup: `Components: IReadOnlyList<CreateSubscriptionComponent>?` — `ComponentId` (union `ComponentId1`), `AllocatedQuantity` (union `AllocatedQuantity3`), `Enabled`, `UnitBalance`, `PricePointId` (union `PricePointId2`). Optional `Reference`, `PaymentCollectionMethod`, `AgreementAcceptance` (Maxio Payments) | `SubscriptionResponse` → `Subscription (subscription): Subscription?` — **nullable inner**. Read `Id`, `State`, `Customer`, `Product`, `ProductPricePointId`, `CouponCode`/`CouponCodes`, `CurrentPeriodEndsAt`, `BalanceInCents`, `CreditCard` | **A** `CreateSubscriptionError` · `TryGetErrorListResponse1` [422] (3DS may surface here as 422 with `action_link` in the **raw** body — `ErrorListResponse1` is `Errors: IReadOnlyList<string> !req`; if that shape does not match, `JsonException` replaces the SdkException — see trap) | none |
| `PreviewSubscription` | `PreviewSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must pass | same create payload; does not create | `SubscriptionPreviewResponse` → `SubscriptionPreview (subscription_preview): SubscriptionPreview !req` → `CurrentBillingManifest` / `NextBillingManifest` (`TotalInCents`, `LineItems`) | **B** | none |
| `ReadSubscription` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` must pass | query `include` — `SubscriptionInclude.Coupons` / `SelfServicePageToken` | `SubscriptionResponse` | **B** | none |
| `FindSubscription` | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` must pass | query `reference` | `SubscriptionResponse` | **A** `FindSubscriptionError` · `TryGetNoContent` [404] | none |
| `ListSubscriptions` | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 14 **must pass** | dates are `DateTimeOffset?` (unlike customer list) | `IReadOnlyList<SubscriptionResponse>` | **B** | `page`+`perPage` (default **20**) |
| `UpdateSubscription` | `UpdateSubscription(int subscriptionId, UpdateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must pass | Envelope `Subscription: UpdateSubscription !req`. Plan change without proration: `ProductHandle`/`ProductId` + optional `ProductPricePointId`/`Handle`. Delayed: also `ProductChangeDelayed = true`. Cancel delayed change: `NextProductId` empty string. Card: `CreditCardAttributes` (`FullNumber`, `ExpirationMonth`, `ExpirationYear`) | `SubscriptionResponse` | **A** `UpdateSubscriptionError` · `TryGetErrorListResponse1` [422] | none |
| `ActivateSubscription` | `ActivateSubscription(int subscriptionId, ActivateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must pass | `RevertOnFailure (revert_on_failure): bool?` | `SubscriptionResponse` | **A** `ActivateSubscriptionError` · `TryGetErrorArrayMapResponse1` [400] | none |
| `ApplyCouponsToSubscription` | `ApplyCouponsToSubscription(int subscriptionId, string? code, AddCouponsRequest? body, CancellationToken ct = default)` — `code` and `body` **must pass** | Prefer body `Codes (codes): IReadOnlyList<string>?` (adds). Query `code` **replaces** all existing (deprecated). Pass `code: null` when using body | `SubscriptionResponse` | **A** `ApplyCouponsToSubscriptionError` · `TryGetSubscriptionAddCouponError1(out SubscriptionAddCouponError1)` [422] — `Codes`, `CouponCode`, `CouponCodes`, `Subscription` as `IReadOnlyList<string>?` | none |
| `RemoveCouponFromSubscription` | `RemoveCouponFromSubscription(int subscriptionId, string? couponCode, CancellationToken ct = default)` — `couponCode` must pass | query `coupon_code` | **`string`** (not an envelope) | **A** `RemoveCouponFromSubscriptionError` · `TryGetSubscriptionRemoveCouponErrors1(out SubscriptionRemoveCouponErrors1)` [422] — `Subscription: IReadOnlyList<string> !req` | none |

#### `client.SubscriptionProducts` — plan change — `operations/SubscriptionProducts.md` · `Api/SubscriptionProducts.cs`

Prefer this over `UpdateSubscription` when the storefront needs **prorated** upgrade/downgrade. Target must be `active` or `trialing`.

| Op | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `PreviewSubscriptionProductMigration` | `PreviewSubscriptionProductMigration(int subscriptionId, SubscriptionMigrationPreviewRequest? body, CancellationToken ct = default)` — `body` must pass | Envelope `Migration (migration): SubscriptionMigrationPreviewOptions !req`. Product: `ProductId` **or** `ProductHandle`; PP: `ProductPricePointId` / `Handle`. Flags default: `IncludeTrial=false`, `IncludeInitialCharge=false`, `IncludeCoupons=true`, `PreservePeriod=false`. Optional `Proration.PreservePeriod`, `ProrationDate` | `SubscriptionMigrationPreviewResponse` → `Migration (migration): SubscriptionMigrationPreview !req` — `ProratedAdjustmentInCents`, `ChargeInCents`, `PaymentDueInCents`, `CreditAppliedInCents` | **A** `PreviewSubscriptionProductMigrationError` · `TryGetErrorListResponse1` [422] | none |
| `MigrateSubscriptionProduct` | `MigrateSubscriptionProduct(int subscriptionId, SubscriptionProductMigrationRequest? body, CancellationToken ct = default)` — `body` must pass | Envelope `Migration (migration): SubscriptionProductMigration !req`. Same product/PP/flag fields as preview (`IncludeTrial` default false, `IncludeCoupons` default true, `PreservePeriod` default false) + `Proration` | `SubscriptionResponse` | **A** `MigrateSubscriptionProductError` · `TryGetErrorListResponse1` [422] | none |

#### `client.SubscriptionComponents` — usage & allocations — `operations/SubscriptionComponents.md` · `Api/SubscriptionComponents.cs`

`CreateUsage` / `ListUsages` first two params are unions: `SubscriptionIdOrReference` (`Int`/`String`), `ComponentIdModel` (`Int`/`String`, handle as `handle:…`).

| Op | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `CreateUsage` | `CreateUsage(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, CreateUsageRequest? body, CancellationToken ct = default)` — `body` must pass | Envelope `Usage (usage): CreateUsage !req`. Inner: `Quantity (quantity): double?` (negative deducts; floor 0), `Memo`, `PricePointId: string?`. Metered/prepaid only; **one component per call** | `UsageResponse` → `Usage (usage): Usage !req` (`Id`, `Quantity` union `Quantity1`, `ComponentId`, `SubscriptionId`) | **A** `CreateUsageError` · `TryGetErrorListResponse1` [422] | none |
| `ListUsages` | `ListUsages(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, long? sinceId, long? maxId, DateTimeOffset? sinceDate, DateTimeOffset? untilDate, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 4 **must pass**. Not for quantity-based components | query `since_id`, `max_id`, `since_date`, `until_date` | `IReadOnlyList<UsageResponse>` | **B** | `page`+`perPage` |
| `AllocateComponent` | `AllocateComponent(int subscriptionId, int componentId, CreateAllocationRequest? body, CancellationToken ct = default)` — `body` must pass | Envelope `Allocation (allocation): CreateAllocation !req`. **Required**: `Quantity (quantity): double`. Optional: `Memo`, `ComponentId`, `PricePointId` (union `PricePointId1`), `UpgradeCharge` (`UpgradeChargeCreditType`), `DowngradeCredit` (`DowngradeCreditCreditType`), `AccrueCharge`. Quantity / On-Off / Prepaid only | `AllocationResponse` → `Allocation (allocation): Allocation?` (`AllocationId`, `Quantity` union, `PreviousQuantity`) | **A** `AllocateComponentError` · `TryGetErrorListResponse1` [422] | none |
| `PreviewAllocations` | `PreviewAllocations(int subscriptionId, PreviewAllocationsRequest? body, …)` — `body` must pass | `Allocations: IReadOnlyList<CreateAllocation> !req`; optional `EffectiveProrationDate`, `UpgradeCharge`, `DowngradeCredit` (`CreditType`) | `AllocationPreviewResponse` → `AllocationPreview (allocation_preview): AllocationPreview !req` (`TotalInCents`, `Direction`, `LineItems`) | **A** `PreviewAllocationsError` · `TryGetComponentAllocationError1(out ComponentAllocationError1)` [422] — `Errors: IReadOnlyList<ComponentAllocationErrorItem>?` | none |
| `ListSubscriptionComponents` | `ListSubscriptionComponents(int subscriptionId, SubscriptionListDateField? dateField, SortingDirection? direction, ListSubscriptionComponentsFilter? filter, string? endDate, string? endDatetime, IncludeNotNull? pricePointIds, IReadOnlyList<int>? productFamilyIds, ListSubscriptionComponentsSort? sort, string? startDate, string? startDatetime, IReadOnlyList<ListSubscriptionComponentsInclude>? include, bool? inUse, CancellationToken ct = default)` — **12 must pass**, **no page defaults** | include `HistoricUsages` / `Subscription` | `IReadOnlyList<SubscriptionComponentResponse>` → each `Component (component): SubscriptionComponent?` (`ComponentId`, `Kind`, `AllocatedQuantity` union, `UnitBalance`, `Enabled`) | **B** | none |
| `ReadSubscriptionComponent` | `ReadSubscriptionComponent(int subscriptionId, int componentId, CancellationToken ct = default)` | | `SubscriptionComponentResponse` | **A** `ReadSubscriptionComponentError` · `TryGetNoContent` [404] | none |
| `AllocateComponents` | `AllocateComponents(int subscriptionId, AllocateComponents? body, …)` — `body` must pass | `Allocations: IReadOnlyList<CreateAllocation>?`, plus top-level `UpgradeCharge`/`DowngradeCredit` (`CreditType`), `AccrueCharge` | `IReadOnlyList<AllocationResponse>` | **A** `AllocateComponentsError` · `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] | none |
| `ActivateEventBasedComponent` | `ActivateEventBasedComponent(int subscriptionId, int componentId, ActivateEventBasedComponent? body, …)` — `body` must pass | `PricePointId?`, `BillingSchedule?`, `CustomPrice?` | `void` | **B** | none |

Ebb ingest (`RecordEvent` / `BulkRecordEvents`) hits the **Ebb** server group, not Production, and is not required for metered `CreateUsage`.

#### `client.Invoices` — `operations/Invoices.md` · `Api/Invoices.cs`

Envelope split: `CreateInvoice` returns `InvoiceResponse` (`Invoice !req`); `ReadInvoice` / `IssueInvoice` / payment/refund/void return **`Invoice` directly** (no wrapper).

| Op | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `ListInvoices` | `ListInvoices(string? startDate, string? endDate, InvoiceStatus? status, int? subscriptionId, string? subscriptionGroupUid, string? consolidationLevel, Direction? direction, InvoiceDateField? dateField, string? startDatetime, string? endDatetime, IReadOnlyList<int>? customerIds, IReadOnlyList<string>? number, IReadOnlyList<int>? productIds, InvoiceSortField? sort, int? page = 1, int? perPage = 20, bool? lineItems = false, bool? discounts = false, bool? taxes = false, bool? credits = false, bool? payments = false, bool? customFields = false, bool? refunds = false, CancellationToken ct = default)` — 14 filters **must pass**. Breakdown flags default **false** — set `lineItems: true` (etc.) to get arrays | dates are **`string?`**. Filter storefront by `subscriptionId` | `ListInvoicesResponse` → `Invoices (invoices): IReadOnlyList<Invoice> !req`. Invoice fields to read: `Uid`, `Number`, `Status`, `TotalAmount`, `DueAmount`, `PaidAmount`, `IssueDate`, `DueDate`, `SubscriptionId`, `LineItems` (only if requested) | **B** | `page`+`perPage` |
| `ReadInvoice` | `ReadInvoice(string uid, CancellationToken ct = default)` | uid, not numeric id | **`Invoice`** | **B** | none |
| `CreateInvoice` | `CreateInvoice(int subscriptionId, CreateInvoiceRequest? body, CancellationToken ct = default)` — `body` must pass | Envelope `Invoice (invoice): CreateInvoice !req`. `LineItems: IReadOnlyList<CreateInvoiceItem>?` — custom: `Title` + `Quantity` (union `Quantity3`) + `UnitPrice` (union `UnitPrice7`); catalog: `ProductId` (union) or `ComponentId` (union). `Coupons: IReadOnlyList<CreateInvoiceCoupon>?` (`Code` **or** `Subcode`, not both). `Status` default `CreateInvoiceStatus.Open` | `InvoiceResponse` → `Invoice !req` | **A** `CreateInvoiceError` · `TryGetErrorArrayMapResponse1` [422] | none |
| `IssueInvoice` | `IssueInvoice(string uid, IssueInvoiceRequest? body, CancellationToken ct = default)` — `body` must pass | `OnFailedPayment` default `FailedPaymentAction.LeaveOpenInvoice` | **`Invoice`** | **A** `IssueInvoiceError` · `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] | none |
| `RecordPaymentForInvoice` | `RecordPaymentForInvoice(string uid, CreateInvoicePaymentRequest? body, …)` — `body` must pass | Envelope `Payment: CreateInvoicePayment !req` — `Amount` (union `Amount`), `Memo`, `Method` (`InvoicePaymentMethodType`), `PaymentProfileId`, `Details`. Optional sibling `Type: InvoicePaymentType?` (default external per enum docs) | **`Invoice`** | **A** `RecordPaymentForInvoiceError` · `TryGetErrorListResponse1` [422] | none |
| `RecordPaymentForSubscription` | `RecordPaymentForSubscription(int subscriptionId, RecordPaymentRequest? body, …)` — `body` must pass | Envelope `Payment: CreatePayment !req` — **all required**: `Amount: string`, `Memo: string`, `PaymentDetails: string`, `PaymentMethod: InvoicePaymentMethodType` | `RecordPaymentResponse` → `PaidInvoices`, `Prepayment` | **A** `RecordPaymentForSubscriptionError` · `TryGetErrorListResponse1` [422] | none |
| `RefundInvoice` | `RefundInvoice(string uid, RefundInvoiceRequest? body, …)` — `body` must pass | Envelope `Refund: Refund !req` (**union**). Segment: `RefundInvoice` with **required** `Amount: string`, `Memo: string`, `PaymentId: int`. Consolidated: `RefundConsolidatedInvoice` with **required** `Memo`, `PaymentId`, `SegmentUids` | **`Invoice`** | **A** `RefundInvoiceError` · `TryGetErrorListResponse1` [422] | none |
| `VoidInvoice` | `VoidInvoice(string uid, VoidInvoiceRequest? body, …)` — `body` must pass | Envelope `Void (void): VoidInvoice !req` — `Reason (reason): string !req` | **`Invoice`** | **A** `VoidInvoiceError` · `TryGetObject(out object?)` [404] · `TryGetErrorListResponse1` [422] | none |
| `SendInvoice` | `SendInvoice(string uid, SendInvoiceRequest? body, …)` — `body` must pass | `RecipientEmails`, `CcRecipientEmails`, `BccRecipientEmails`, `AttachmentUrls` | `void` | **A** `SendInvoiceError` · `TryGetErrorListResponse1` [422] | none |

#### `client.SubscriptionStatus` — `operations/SubscriptionStatus.md` · `Api/SubscriptionStatus.cs`

| Op | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `CancelSubscription` | `CancelSubscription(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` — `body` must pass | Envelope `Subscription: CancellationOptions !req` — `CancellationMessage`, `ReasonCode`, `CancelAtEndOfPeriod`, `ScheduledCancellationAt`, `RefundPrepaymentAccountBalance`. Immediate cancel: still pass body (options may be empty-ish but envelope `Subscription` is `!req`) | `SubscriptionResponse` | **A** `CancelSubscriptionApiError` · `TryGetNoContent` [404] · `TryGetCancelSubscriptionErrorResponse(out CancelSubscriptionErrorResponse)` [422]. **`CancelSubscriptionErrorResponse` is an AnyOf** (`unions.md`): then `TryGetErrorListResponse1` / `TryGetSingleErrorResponse1` (`Error (error): string !req`) | none |
| `InitiateDelayedCancellation` | `InitiateDelayedCancellation(int subscriptionId, CancellationRequest? body, …)` — `body` must pass | same `CancellationRequest` | `DelayedCancellationResponse` → `Message (message): string?` | **A** `InitiateDelayedCancellationError` · `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] | none |
| `CancelDelayedCancellation` | `CancelDelayedCancellation(int subscriptionId, CancellationToken ct = default)` | | `DelayedCancellationResponse` | **A** `CancelDelayedCancellationError` · `TryGetNoContent` [404] | none |
| `PauseSubscription` | `PauseSubscription(int subscriptionId, PauseRequest? body, …)` — `body` must pass | `Hold: AutoResume?` → `AutomaticallyResumeAt` | `SubscriptionResponse` | **A** `PauseSubscriptionError` · `TryGetErrorListResponse1` [422] | none |
| `ResumeSubscription` | `ResumeSubscription(int subscriptionId, ResumptionCharge? calendarBillingResumptionCharge, …)` — `calendarBillingResumptionCharge` must pass | query `calendar_billing['resumption_charge']` | `SubscriptionResponse` | **A** `ResumeSubscriptionError` · `TryGetErrorListResponse1` [422] | none |
| `ReactivateSubscription` | `ReactivateSubscription(int subscriptionId, ReactivateSubscriptionRequest? body, …)` — `body` must pass | `IncludeTrial`, `PreserveBalance`, `CouponCode`, `Resume` (union `bool` \| `ResumeOptions`), `CalendarBilling.ReactivationCharge` default `Prorated` | `SubscriptionResponse` | **A** `ReactivateSubscriptionError` · `TryGetErrorListResponse1` [422] | none |
| `RetrySubscription` | `RetrySubscription(int subscriptionId, CancellationToken ct = default)` | | `SubscriptionResponse` | **A** `RetrySubscriptionError` · `TryGetErrorListResponse1` [422] | none |
| `PreviewRenewal` | `PreviewRenewal(int subscriptionId, RenewalPreviewRequest? body, …)` — `body` must pass | `Components: IReadOnlyList<RenewalPreviewComponent>?` (omit/`null` body still must be passed — pass a body with null components for current quantities) | `RenewalPreviewResponse` → `RenewalPreview !req` (`TotalInCents`, `NextAssessmentAt`, `LineItems`) | **A** `PreviewRenewalError` · `TryGetErrorListResponse1` [422] | none |

`ErrorListResponse1` (`records-2`): `Errors (errors): IReadOnlyList<string> !req`. `ErrorArrayMapResponse1`: `Errors: IReadOnlyDictionary<string, object>?`. `ErrorStringMapResponse1`: `Errors: IReadOnlyDictionary<string, string>?`. `SingleStringErrorResponse1`: `Errors: string?`.

### Enums actually used (`map/models/enums.md`)

Construct `Type.FromValue("wire")` or static members (`CollectionMethod.Automatic`, not `.automatic`). Namespace `MaxioAdvancedBilling.Models.Enums`.

| Enum | Members (C# (wire)) |
|---|---|
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` |
| `IntervalUnit` | `Day (day)`, `Month (month)` |
| `TrialType` | `NoObligation (no_obligation)`, `PaymentExpected (payment_expected)` |
| `ExpirationIntervalUnit` | `Day (day)`, `Month (month)`, `Never (never)` |
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `SubscriptionStateFilter` | `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)` |
| `SubscriptionInclude` | `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)` |
| `SubscriptionListInclude` | `SelfServicePageToken (self_service_page_token)` |
| `SubscriptionDateField` | `CurrentPeriodEndsAt`, `CurrentPeriodStartsAt`, `CreatedAt`, `ActivatedAt`, `CanceledAt`, `ExpiresAt`, `TrialStartedAt`, `TrialEndedAt`, `UpdatedAt` (wires snake_case as listed in enums.md) |
| `SubscriptionSort` | `SignupDate (signup_date)`, `PeriodStart (period_start)`, `PeriodEnd (period_end)`, `NextAssessment (next_assessment)`, `UpdatedAt (updated_at)`, `CreatedAt (created_at)`, `TotalPayments (total_payments)`, `Id (id)`, `OpenBalance (open_balance)`, `ExpiresAt (expires_at)` |
| `SortingDirection` / `Direction` | `Asc (asc)`, `Desc (desc)` |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` |
| `ComponentKind` | `MeteredComponent (metered_component)`, `QuantityBasedComponent (quantity_based_component)`, `OnOffComponent (on_off_component)`, `PrepaidUsageComponent (prepaid_usage_component)`, `EventBasedComponent (event_based_component)` |
| `PricingScheme` | `Stairstep (stairstep)`, `Volume (volume)`, `PerUnit (per_unit)`, `Tiered (tiered)` |
| `PricePointType` | `Catalog (catalog)`, `Default (default)`, `Custom (custom)` |
| `CreditType` | `Full (full)`, `Prorated (prorated)`, `None (none)` |
| `UpgradeChargeCreditType` / `DowngradeCreditCreditType` | `Full (full)`, `Prorated (prorated)`, `None (none)` |
| `CreditScheme` | `None (none)`, `Credit (credit)`, `Refund (refund)` |
| `PaymentType` | `CreditCard (credit_card)`, `BankAccount (bank_account)`, `PaypalAccount (paypal_account)`, `ApplePay (apple_pay)` |
| `InvoiceStatus` | `Draft (draft)`, `Open (open)`, `Paid (paid)`, `Pending (pending)`, `Voided (voided)`, `Canceled (canceled)`, `Processing (processing)` |
| `InvoiceDateField` | `CreatedAt (created_at)`, `DueDate (due_date)`, `IssueDate (issue_date)`, `UpdatedAt (updated_at)`, `PaidDate (paid_date)` |
| `InvoiceSortField` | `Status`, `TotalAmount`, `DueAmount`, `CreatedAt`, `UpdatedAt`, `IssueDate`, `DueDate`, `Number` (wires snake_case) |
| `CreateInvoiceStatus` | `Draft (draft)`, `Open (open)` |
| `FailedPaymentAction` | `LeaveOpenInvoice (leave_open_invoice)`, `RollbackToPending (rollback_to_pending)`, `InitiateDunning (initiate_dunning)` |
| `InvoicePaymentMethodType` | `CreditCard (credit_card)`, `Check (check)`, `Cash (cash)`, `MoneyOrder (money_order)`, `Ach (ach)`, `Other (other)` |
| `InvoicePaymentType` | `External (external)`, `Prepayment (prepayment)`, `ServiceCredit (service_credit)`, `Payment (payment)` |
| `DiscountType` | `Amount (amount)`, `Percent (percent)` |
| `RecurringScheme` | `DoNotRecur (do_not_recur)`, `RecurIndefinitely (recur_indefinitely)`, `RecurWithDuration (recur_with_duration)` |
| `CompoundingStrategy` | `Compound (compound)`, `FullPrice (full-price)` |
| `ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)` |
| `ListSubscriptionComponentsInclude` | `Subscription (subscription)`, `HistoricUsages (historic_usages)` |
| `ResumptionCharge` / `ReactivationCharge` | `Prorated (prorated)`, `Immediate (immediate)`, `Delayed (delayed)` |
| `CancellationMethod` | `MerchantUi (merchant_ui)`, `MerchantApi (merchant_api)`, `Dunning (dunning)`, `BillingPortal (billing_portal)`, `Unknown (unknown)`, `Imported (imported)` |
| `ServerEnvironment` | `Us (US)`, `Eu (EU)` — namespace **`MaxioAdvancedBilling.Servers`**, not `.Models.Enums` |

### Unions the storefront must construct/read (`map/models/unions.md`)

| Union | NS | Factories / TryGet |
|---|---|---|
| `PaymentProfile` | `Models.OneOf` | `CreditCardPaymentProfile` / `BankAccountPaymentProfile` / `PaypalPaymentProfile` / `ApplePayPaymentProfile` — `TryGetCreditCardPaymentProfile` etc. |
| `ProductIdModel`, `PricePointIdModel`, `ComponentIdModel`, `SubscriptionIdOrReference` | `Models.AnyOf` | `.Int(int)` / `.String(string)` |
| `PricePoint` (create component PP) | `Models.AnyOf` | `CreateComponentPricePoint` / `CreatePrepaidUsageComponentPricePoint` |
| `PricePoint2` (bulk assign) | `Models.AnyOf` | `string` (incl. `"_default"`) / `int` |
| `Refund` | `Models.AnyOf` | `RefundInvoice` / `RefundConsolidatedInvoice` |
| `Resume` | `Models.AnyOf` | `bool` / `ResumeOptions` |
| `CancelSubscriptionErrorResponse` | `Models.AnyOf` | `ErrorListResponse1` / `SingleErrorResponse1` |
| `AllocatedQuantity3`, `ComponentId1`, `PricePointId2`, `Quantity`/`Quantity1`/`Quantity3`, `UnitPrice`/`UnitPrice1`/`UnitPrice3`/`UnitPrice7`, `Amount`, `Percentage`, `StartingQuantity`, `EndingQuantity`, `SnapDay1`, `NetTerms1`, `OfferId`, `ProductId`, `ProductFamilyId`, `PricePointId1`/`PricePointId4` | `Models.AnyOf` | int/string or double/string per row in unions.md |

---

## Trap notes

⚠ Step 1 (client registration) — `HttpClient` lifetime vs SDK client lifetime, and whether the DI helper owns the handler pipeline, are not visible from the ctor. **MUST load `dotnet-client-initialization`** before writing `new MaxioAdvancedBillingClient` or `AddMaxioAdvancedBillingClient`.

⚠ Step 1 (auth) — credential property names, when they must be set relative to construction, and loading the key from configuration rather than literals, are not visible from `BasicAuthCredentials`. **MUST load `dotnet-authentication`** before wiring credentials.

⚠ Step 1 (site / retries / timeouts) — `Retry`/`Timeout` on options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; which verbs retry on status vs transport, and what `Site`/`BaseUrl` actually select, are not visible from the options type. **MUST load `dotnet-configuration-resilience`** before registering or tuning the client.

⚠ Steps 2–12 (every call) — list/search ops have long nullable-without-default parameter lists; a positional call mis-binds. Cancellation is `ct:`. **MUST load `dotnet-calling-endpoints`** before the first `client.*.*(...)`.

⚠ Steps 2–12 (payloads) — envelopes wrap one field (`Customer`, `Subscription`, `Product`, `Invoice`, `Usage`, `Migration`, `Allocation`); several responses skip the wrapper (`ReadInvoice` → `Invoice`, `ArchiveComponent` → `Component`, `RemoveCouponFromSubscription` → `string`). Unions have no usable `new`. Enums are `StringEnum<T>`. **MUST load `dotnet-models`** before constructing or mapping any request/response that is not a plain string/number.

⚠ Step 13 (error boundary) — Case A vs Case B is per-operation (this sheet); `TryGetRawError` is not a catch-all on typed errors; there are **no** no-throw Result variants. **MUST load `dotnet-error-handling`** before any `try/catch`.

⚠ Step 13 (error boundary) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 13 (error boundary) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 13 (tests) — the seam for faking the SDK is not the generated controllers. **MUST load `dotnet-testing`** before stubbing.

---

## REQUIRED READING

Load **before implementation starts**. This sheet does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, `HttpClient` ownership, `AddMaxioAdvancedBillingClient` |
| `dotnet-authentication` | Step 1 — Basic credentials (`Username` / `Password`) |
| `dotnet-configuration-resilience` | Step 1 — retries, timeouts, `Server.Production.*.Site`/`BaseUrl`, list pagination |
| `dotnet-calling-endpoints` | Steps 2–12 — named arguments, `ct:`, required-nullable params |
| `dotnet-models` | Steps 2–12 — envelopes, `required`/nullable, `StringEnum<T>`, AnyOf/OneOf factories |
| `dotnet-error-handling` | Step 13 — Case A/B, `TryGet…`, both `JsonException` directions, throw-only API |
| `dotnet-testing` | Step 13 — test seam for the integration layer |

---

## Assumptions & Blockers

- **Assumed:** eShopOnWeb remains its stock ASP.NET Core layout; Maxio is added as an infrastructure billing service (DI-registered `MaxioAdvancedBillingClient`) rather than replacing the existing one-shot catalog checkout. Map the eShop buyer to Maxio via `Customer.Reference` = application user id.
- **Assumed:** site subdomain + API key come from configuration (`Server.Production.{Us|Eu}.Site`, `BasicAuth.Username`). Environment is `Us` unless the account is EU-hosted.
- **Assumed:** production card collection uses Maxio.js (`chargify_token` on `CreatePaymentProfile` / `CreateSubscription.PaymentProfileAttributes`); raw PAN fields exist on the models but are not the storefront path.
- **Assumed:** catalog (families/products/components/coupons) may already exist in the Maxio site; create ops are for admin/bootstrap. Storefront reads by handle (`ReadProductByHandle`, `FindComponent`, `ValidateCoupon`).
- **Assumed:** metered usage is recorded with `CreateUsage`; event-stream ingest (`RecordEvent` on the Ebb host) is out of scope unless the catalog uses event-based components.
- **Assumed:** prorated plan changes use `SubscriptionProducts.MigrateSubscriptionProduct`; end-of-period changes use `UpdateSubscription` with `ProductChangeDelayed = true`.
- **UNVERIFIED (map-shaped, live wire unknown):** `CreateCustomerError.TryGetCustomerErrorResponse1` types `Errors` as the `Errors` record whose only mapped fields are `PerPage` and `PricePoint` (`records-2-Cr-Ne.md`) — those names do not describe a customer 422. Extract those properties if present; otherwise fall back to `TryGetRawError` / `ReadAsString()` for the operator-facing message. Do not parse `SdkException.ToString()`.
- **UNVERIFIED:** 3DS `action_link` on create/migrate/reactivate 422s is described in operation notes, not as a generated field on `ErrorListResponse1`. If `TryGetErrorListResponse1` is false, fall back to `TryGetRawError` and best-effort JSON, then the generic message.
- **Blockers:** none from the map. Implementer still needs a Maxio site in test mode, an API key, and the subdomain before live calls succeed.
