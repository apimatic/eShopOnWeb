# Maxio Advanced Billing — eShopOnWeb subscription billing

Package `AsadAli.AdvancedBilling.Sdk` · root namespace `MaxioAdvancedBilling` · map stamp `v1.0.2` / `15db14b`.

Storefront mapping: eShopOnWeb `ApplicationUser` ↔ Maxio `Customer` via `reference`; catalog (products / price points / components / coupons) is authored in Maxio and **listed/read** at checkout; subscriptions, usage, invoices, and plan changes are written through the SDK.

---

## Scope & sequence

| Step | What | Operations |
|---|---|---|
| 1 | Install package, register client, Basic auth, site subdomain | `new MaxioAdvancedBillingClient(httpClient, options)` or `AddMaxioAdvancedBillingClient` |
| 2 | Catalog: families, products, price points (plans) | `ProductFamilies.ListProductFamilies`, `ReadProductFamily`, `ListProductsForProductFamily`; `Products.ListProducts`, `ReadProduct`, `ReadProductByHandle`; `ProductPricePoints.ListProductPricePoints`, `ReadProductPricePoint` |
| 3 | Customers | `Customers.CreateCustomer`, `ReadCustomer`, `ReadCustomerByReference`, `UpdateCustomer`, `ListCustomers`, `ListCustomerSubscriptions`, `DeleteCustomer` |
| 4 | Payment method (token, not PAN) | `PaymentProfiles.CreatePaymentProfile`, `ListPaymentProfiles`, `ReadPaymentProfile`, `UpdatePaymentProfile`, `ChangeSubscriptionDefaultPaymentProfile`, `ReadOneTimeToken` |
| 5 | Coupons at checkout | `Coupons.ValidateCoupon`, `FindCoupon`, `ListCoupons`; `Subscriptions.ApplyCouponsToSubscription`, `RemoveCouponFromSubscription` |
| 6 | Subscribe | `Subscriptions.PreviewSubscription`, `CreateSubscription`, `ReadSubscription`, `FindSubscription`, `ListSubscriptions`, `UpdateSubscription`, `ActivateSubscription` |
| 7 | Components + usage | `Components.ListComponents`, `FindComponent`, `ReadComponent`; `SubscriptionComponents.ListSubscriptionComponents`, `ReadSubscriptionComponent`, `AllocateComponent`, `AllocateComponents`, `PreviewAllocations`, `CreateUsage`, `ListUsages` |
| 8 | Plan change | `SubscriptionProducts.PreviewSubscriptionProductMigration`, `MigrateSubscriptionProduct`; delayed change via `Subscriptions.UpdateSubscription` (`product_change_delayed`) |
| 9 | Invoices | `Invoices.ListInvoices`, `ReadInvoice`, `RecordPaymentForInvoice`, `RecordPaymentForSubscription`, `IssueInvoice`, `SendInvoice`, `RefundInvoice`, `VoidInvoice`, `ReopenInvoice` |
| 10 | Lifecycle | `SubscriptionStatus.CancelSubscription`, `InitiateDelayedCancellation`, `CancelDelayedCancellation`, `PauseSubscription`, `ResumeSubscription`, `ReactivateSubscription`, `RetrySubscription`, `PreviewRenewal` |
| 11 | Self-service portal | `BillingPortal.EnableBillingPortalForCustomer`, `ReadBillingPortalLink` |
| 12 | Error boundary + tests | wrap every call; fake `HttpClient` in tests |

Catalog **create/update/archive** ops (`Products.CreateProduct`, `Components.CreateMeteredComponent`, `Coupons.CreateCoupon`, …) are included below for admin seeding; checkout must not depend on them.

Out of storefront scope (not in this sheet): subscription groups, proforma/advance invoices, offers, webhooks, sales commissions, insights, API exports, custom fields, reason codes, referral codes, sites, events list, EBB segments, component price-point CRUD, coupon subcodes admin. EBB ingest (`RecordEvent` / `BulkRecordEvents`) is listed only under usage as optional.

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

All operations are **throw-only** (no `…Result` variants). Case A = `MaxioAdvancedBilling.Core.Exceptions.SdkException<{Op}Error>` with typed `TryGet…` + inherited `TryGetRawError`. Case B = `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` (`StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`). Typed error classes live in `MaxioAdvancedBilling.Errors`. Records in `MaxioAdvancedBilling.Models`. Enums in `MaxioAdvancedBilling.Models.Enums`. Unions in `MaxioAdvancedBilling.Models.AnyOf` / `MaxioAdvancedBilling.Models.OneOf`. Controllers in `MaxioAdvancedBilling.Api` (accessed as `client.{Controller}`).

### Client, auth, servers

| Fact | Value | Source |
|---|---|---|
| Package | `AsadAli.AdvancedBilling.Sdk` | `sdk-map.md` |
| Client | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` | `sdk-map.md` |
| Ctor | `MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` — **only** constructor | `sdk-map.md` |
| Options | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions`: `Environment` (`MaxioAdvancedBilling.Servers.ServerEnvironment`), `Retry` (`MaxioAdvancedBilling.Core.Configuration.RetryOptions`), `Server` (`MaxioAdvancedBilling.ServerOptions`), `BasicAuth` (`MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?`) | `sdk-map.md` |
| Auth | `BasicAuth = new BasicAuthCredentials { Username = "<api_key>", Password = "x" }` — both members `required` | `sdk-map.md` |
| Environment | `ServerEnvironment.Us` (wire `US`, default) → `https://{site}.chargify.com`; `ServerEnvironment.Eu` (wire `EU`) → `https://{site}.ebilling.maxio.com` | `sdk-map.md` |
| Site | `options.Server.Production.Us.Site = "<subdomain>"` (default `"subdomain"`). Nested types: `ServerOptions` is root `MaxioAdvancedBilling`; `ProductionOptions` / `EbbOptions` are `MaxioAdvancedBilling.Servers`. Mock host: `options.Server.Production.Us.BaseUrl` | `sdk-map.md` |
| EBB host | `options.Server.Ebb.Us.BaseUrl` default `https://events.chargify.com/{site}` — only `SubscriptionComponents.RecordEvent` / `BulkRecordEvents` | `sdk-map.md` |
| DI | `services.AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>? configure = null)` in namespace `MaxioAdvancedBilling` | `sdk-map.md` |
| RetryOptions | namespace `MaxioAdvancedBilling.Core.Configuration`; all members `required` — use `RetryOptions.Default()` or set every member: `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry` | `sdk-map.md` |

---

### Step 2 — Catalog (products / plans)

#### `client.ProductFamilies` · `operations/ProductFamilies.md`

| Op | Signature | Returns | Error | Pagination |
|---|---|---|---|---|
| `ListProductFamilies` | `(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — 5 nullable no-default → pass `null` | `IReadOnlyList<ProductFamilyResponse>` | **B** `SdkException<RawError>` | none |
| `ReadProductFamily` | `(int id, CancellationToken ct = default)` | `ProductFamilyResponse` | **B** | none |
| `ListProductsForProductFamily` | `(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 nullable no-default | `IReadOnlyList<ProductResponse>` | **A** `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError` | `page`+`perPage` |
| `CreateProductFamily` | `(CreateProductFamilyRequest? body, CancellationToken ct = default)` — `body` must pass | `ProductFamilyResponse` | **A** `CreateProductFamilyError`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] | none |

Envelope: `ProductFamilyResponse.ProductFamily (product_family): ProductFamily?` (`records-3-Of-Su.md`). Inner fields used: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`. Request: `CreateProductFamilyRequest.ProductFamily !req` → `CreateProductFamily`: `Name (name): string !req`, `Handle (handle): string?`, `Description (description): string?` (`records-1-Ac-Cr.md`).

#### `client.Products` · `operations/Products.md`

| Op | Signature | Returns | Error | Pagination |
|---|---|---|---|---|
| `ListProducts` | `(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 nullable no-default | `IReadOnlyList<ProductResponse>` | **B** | `page`+`perPage` |
| `ReadProduct` | `(int productId, CancellationToken ct = default)` | `ProductResponse` | **B** | none |
| `ReadProductByHandle` | `(string apiHandle, CancellationToken ct = default)` | `ProductResponse` | **B** | none |
| `CreateProduct` | `(string productFamilyId, CreateOrUpdateProductRequest? body, CancellationToken ct = default)` — `body` must pass | `ProductResponse` | **A** `CreateProductError`: `TryGetErrorListResponse1` [422] | none |
| `UpdateProduct` | `(int productId, CreateOrUpdateProductRequest? body, CancellationToken ct = default)` | `ProductResponse` | **A** `UpdateProductError`: `TryGetErrorListResponse1` [422] | none |
| `ArchiveProduct` | `(int productId, CancellationToken ct = default)` | `ProductResponse` | **A** `ArchiveProductError`: `TryGetErrorListResponse1` [422] | none |

Envelope: `ProductResponse.Product (product): Product !req` (`records-3-Of-Su.md`). Storefront reads: `Id`, `Name`, `Handle`, `Description`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `TrialPriceInCents`, `TrialInterval`, `RequireCreditCard (require_credit_card): bool?`, `ProductPricePointId`, `ProductPricePointHandle`, `ProductFamily (product_family): ProductFamily?`, `ArchivedAt`. Request envelope `CreateOrUpdateProductRequest.Product !req` → `CreateOrUpdateProduct`: `Name !req`, `Description !req`, `PriceInCents (price_in_cents): long !req`, `Interval (interval): int !req`, `IntervalUnit !req`, `Handle?`, `RequireCreditCard?`, trial/expiration optionals (`records-1-Ac-Cr.md`). **Updating a product via this endpoint creates a new default price point** (`operations/Products.md`).

`ListProductsFilter` (`records-2-Cr-Ne.md`): `Ids (ids): IReadOnlyList<int>?`, `PrepaidProductPricePoint?`, `UseSiteExchangeRate (use_site_exchange_rate): bool?`.

#### `client.ProductPricePoints` · `operations/ProductPricePoints.md`

Path unions: `ProductIdModel` / `PricePointIdModel` — `MaxioAdvancedBilling.Models.AnyOf`; factories `Int(int)` / `String(string)` (`unions.md`).

| Op | Signature | Returns | Error | Pagination |
|---|---|---|---|---|
| `ListProductPricePoints` | `(ProductIdModel productId, bool? currencyPrices, IReadOnlyList<PricePointType>? filterType, bool? archived, int? page = 1, int? perPage = 10, CancellationToken ct = default)` — 3 nullable no-default | `ListProductPricePointsResponse` | **B** | `page`+`perPage` |
| `ReadProductPricePoint` | `(ProductIdModel productId, PricePointIdModel pricePointId, bool? currencyPrices, CancellationToken ct = default)` | `ProductPricePointResponse` | **B** | none |
| `CreateProductPricePoint` | `(ProductIdModel productId, CreateProductPricePointRequest? body, CancellationToken ct = default)` | `ProductPricePointResponse` | **A** `CreateProductPricePointError`: `TryGetProductPricePointErrorResponse1(out ProductPricePointErrorResponse1)` [422] | none |
| `UpdateProductPricePoint` | `(ProductIdModel productId, PricePointIdModel pricePointId, UpdateProductPricePointRequest? body, CancellationToken ct = default)` | `ProductPricePointResponse` | **B** | none |
| `PromoteProductPricePointToDefault` | `(int productId, int pricePointId, CancellationToken ct = default)` | `ProductResponse` | **B** | none |
| `ListAllProductPricePoints` | `(SortingDirection? direction, ListPricePointsFilter? filter, ListProductsPricePointsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | `ListProductPricePointsResponse` | **A** `ListAllProductPricePointsError`: `TryGetErrorListResponse1` [422] | `page`+`perPage` |

`ListProductPricePointsResponse.PricePoints (price_points): IReadOnlyList<ProductPricePoint> !req` (`records-2-Cr-Ne.md`). `ProductPricePointResponse.PricePoint (price_point): ProductPricePoint !req`. Inner: `Id`, `Name`, `Handle`, `PriceInCents`, `Interval`, `IntervalUnit`, `Type (type): PricePointType?`, `ProductId`. Create request: `CreateProductPricePointRequest.PricePoint !req` → `Name !req`, `PriceInCents !req`, `Interval !req`, `IntervalUnit !req`, `Handle?` (`records-1-Ac-Cr.md`). Update: `UpdateProductPricePointRequest.PricePoint !req` → `Handle?`, `PriceInCents?` (`records-4-Su-We.md`).

---

### Step 3 — Customers · `client.Customers` · `operations/Customers.md`

| Op | Signature | Returns | Error |
|---|---|---|---|
| `CreateCustomer` | `(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` must pass | `CustomerResponse` | **A** `CreateCustomerError`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError` |
| `ReadCustomer` | `(int id, CancellationToken ct = default)` | `CustomerResponse` | **B** |
| `ReadCustomerByReference` | `(string reference, CancellationToken ct = default)` query `reference` | `CustomerResponse` | **B** |
| `UpdateCustomer` | `(int id, UpdateCustomerRequest? body, CancellationToken ct = default)` — `body` must pass | `CustomerResponse` | **A** `UpdateCustomerError`: `TryGetNoContent(out RawError)` [404] · `TryGetCustomerErrorResponse1` [422] |
| `ListCustomers` | `(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — 7 nullable no-default | `IReadOnlyList<CustomerResponse>` | **B** · pagination `page`+`perPage` |
| `ListCustomerSubscriptions` | `(int customerId, CancellationToken ct = default)` | `IReadOnlyList<SubscriptionResponse>` | **B** |
| `DeleteCustomer` | `(int id, CancellationToken ct = default)` | `void` | **B** |

Envelope: `CustomerResponse.Customer (customer): Customer !req` (`records-2-Cr-Ne.md`). Inner used: `Id (id): int?`, `Reference (reference): string?`, `Email`, `FirstName (first_name)`, `LastName (last_name)`, `Organization`, `Phone`, `Locale`, address fields (`Country` ISO-2), `TaxExempt`.

`CreateCustomerRequest.Customer !req` → `CreateCustomer`: **`FirstName`, `LastName`, `Email` are `string !req`**. Optional: `Reference` (must be unique if set — use eShopOnWeb user id), `CcEmails`, `Organization`, `Address`, `Address2`, `City`, `State` (ISO), `Zip`, `Country` (ISO-2), `Phone`, `Locale`, `VatNumber`, `TaxExempt`, `TaxExemptReason`, `ParentId`, `SalesforceId` (`records-1-Ac-Cr.md`).

`UpdateCustomerRequest.Customer !req` → `UpdateCustomer`: all fields optional (`records-4-Su-We.md`).

`CustomerErrorResponse1.Errors (errors): Errors?` where generated `Errors` only has `PerPage (per_page)` and `PricePoint (price_point)` (`records-2-Cr-Ne.md`; confirmed `Models/CustomerErrorResponse1.cs` + `Models/Errors.cs`). A sibling union `Errors1` (`CustomerError` \| `IReadOnlyList<string>`) exists (`unions.md`) but is **not** the 422 accessor type. **UNVERIFIED** whether live 422 bodies match `Errors`; if `TryGetCustomerErrorResponse1` yields empty/`null` useful fields, or if deserialization of the 422 body throws `JsonException`, extract best-effort via `TryGetRawError` → `ReadAsString()` / `ReadAsJson<T>()`.

---

### Step 4 — Payment profiles · `client.PaymentProfiles` · `operations/PaymentProfiles.md`

Do **not** send raw PAN in production; pass `chargify_token` from Maxio.js (`operations/PaymentProfiles.md`).

| Op | Signature | Returns | Error |
|---|---|---|---|
| `CreatePaymentProfile` | `(CreatePaymentProfileRequest? body, CancellationToken ct = default)` | `PaymentProfileResponse` | **A** `CreatePaymentProfileError`: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] |
| `ListPaymentProfiles` | `(int? customerId, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — `customerId` must pass (`null` = site-wide) | `IReadOnlyList<PaymentProfileResponse>` | **B** · pagination `page`+`perPage` |
| `ReadPaymentProfile` | `(int paymentProfileId, CancellationToken ct = default)` | `PaymentProfileResponse` | **A** `ReadPaymentProfileError`: `TryGetNoContent` [404] |
| `UpdatePaymentProfile` | `(int paymentProfileId, UpdatePaymentProfileRequest? body, CancellationToken ct = default)` | `PaymentProfileResponse` | **A** `UpdatePaymentProfileError`: `TryGetNoContent` [404] · `TryGetErrorStringMapResponse1(out ErrorStringMapResponse1)` [422] |
| `ChangeSubscriptionDefaultPaymentProfile` | `(int subscriptionId, int paymentProfileId, CancellationToken ct = default)` | `PaymentProfileResponse` | **A** `ChangeSubscriptionDefaultPaymentProfileError`: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] |
| `ReadOneTimeToken` | `(string chargifyToken, CancellationToken ct = default)` | `GetOneTimeTokenRequest` | **A** `ReadOneTimeTokenError`: `TryGetErrorListResponse1` [404] |
| `DeleteUnusedPaymentProfile` | `(int paymentProfileId, CancellationToken ct = default)` | `void` | **A** `DeleteUnusedPaymentProfileError`: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] |
| `DeleteSubscriptionsPaymentProfile` | `(int subscriptionId, int paymentProfileId, CancellationToken ct = default)` | `void` | **B** |
| `SendRequestUpdatePaymentEmail` | `(int subscriptionId, CancellationToken ct = default)` | `void` | **A**: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] |

Envelope: `PaymentProfileResponse.PaymentProfile (payment_profile): PaymentProfile !req` **(union OneOf)** — `PaymentProfile.TryGetCreditCardPaymentProfile` / `TryGetBankAccountPaymentProfile` / `TryGetPaypalPaymentProfile` / `TryGetApplePayPaymentProfile` (`unions.md`, `records-3-Of-Su.md`). Card inner: `Id`, `MaskedCardNumber`, `CardType`, `ExpirationMonth/Year`, `CustomerId`, `PaymentType` default `PaymentType.CreditCard`.

`CreatePaymentProfileRequest.PaymentProfile !req` → `CreatePaymentProfile`: `ChargifyToken (chargify_token): string?`, `CustomerId (customer_id): int?`, `FirstName`/`LastName`, billing address, `PaymentType`, `ExpirationMonth` (`ExpirationMonth1` union), `ExpirationYear` (`ExpirationYear1` union), bank fields, `Cvv?` (`records-1-Ac-Cr.md`). Storefront: set `ChargifyToken` + `CustomerId`.

---

### Step 5 — Coupons · `client.Coupons` · `operations/Coupons.md`

Plus apply/remove on `client.Subscriptions` (Step 6).

| Op | Signature | Returns | Error |
|---|---|---|---|
| `ValidateCoupon` | `(string code, int? productFamilyId, CancellationToken ct = default)` — `productFamilyId` must pass | `CouponResponse` | **A** `ValidateCouponError`: `TryGetSingleStringErrorResponse1(out SingleStringErrorResponse1)` [404] |
| `FindCoupon` | `(int? productFamilyId, string? code, bool? currencyPrices, CancellationToken ct = default)` — 3 must pass | `CouponResponse` | **B** |
| `ListCoupons` | `(ListCouponsFilter? filter, bool? currencyPrices, int? page = 1, int? perPage = 30, CancellationToken ct = default)` | `IReadOnlyList<CouponResponse>` | **B** · pagination |
| `ListCouponsForProductFamily` | `(int productFamilyId, ListCouponsFilter? filter, bool? currencyPrices, int? page = 1, int? perPage = 30, CancellationToken ct = default)` | `IReadOnlyList<CouponResponse>` | **B** |
| `ReadCoupon` | `(int productFamilyId, int couponId, bool? currencyPrices, CancellationToken ct = default)` | `CouponResponse` | **B** |
| `ReadCouponUsage` | `(int productFamilyId, int couponId, CancellationToken ct = default)` | `IReadOnlyList<CouponUsage>` | **B** |
| `CreateCoupon` | `(int productFamilyId, CouponRequest? body, CancellationToken ct = default)` | `CouponResponse` | **A** `CreateCouponError`: `TryGetErrorListResponse1` [422] |
| `UpdateCoupon` | `(int productFamilyId, int couponId, CouponRequest? body, CancellationToken ct = default)` | `CouponResponse` | **A** `UpdateCouponError`: `TryGetErrorListResponse1` [422] |
| `ArchiveCoupon` | `(int productFamilyId, int couponId, CancellationToken ct = default)` | `CouponResponse` | **B** |

`CouponResponse.Coupon (coupon): Coupon?` (`records-1-Ac-Cr.md`). Inner used: `Id`, `Name`, `Code`, `AmountInCents`, `Percentage (percentage): string?`, `DiscountType`, `Recurring`, `Stackable`, `EndDate`, `ProductFamilyId`, `ArchivedAt`. `SingleStringErrorResponse1.Errors (errors): string?` (`records-3-Of-Su.md`). `CouponRequest`: `Coupon (coupon): CouponPayload?` + `RestrictedProducts` / `RestrictedComponents` dicts. `CouponPayload`: `Name`, `Code`, `Percentage` (`Percentage` union), `AmountInCents`, `Recurring`, `Stackable`, `EndDate`, `ProductFamilyId (product_family_id): string?`. `ListCouponsFilter`: `Codes (codes): IReadOnlyList<string>?`, `Ids`, date fields, `IncludeArchived` (`records-2-Cr-Ne.md`).

Apply to an **existing** subscription (`operations/Subscriptions.md`):

| Op | Signature | Returns | Error |
|---|---|---|---|
| `ApplyCouponsToSubscription` | `(int subscriptionId, string? code, AddCouponsRequest? body, CancellationToken ct = default)` — `code` and `body` must pass | `SubscriptionResponse` | **A** `ApplyCouponsToSubscriptionError`: `TryGetSubscriptionAddCouponError1(out SubscriptionAddCouponError1)` [422] |
| `RemoveCouponFromSubscription` | `(int subscriptionId, string? couponCode, CancellationToken ct = default)` query `coupon_code` | `string` | **A** `RemoveCouponFromSubscriptionError`: `TryGetSubscriptionRemoveCouponErrors1(out SubscriptionRemoveCouponErrors1)` [422] |

Prefer **body** `AddCouponsRequest.Codes (codes): IReadOnlyList<string>?` (appends, stackable). Query `code` **replaces** all existing codes and is deprecated (`operations/Subscriptions.md`). Pass `code: null` when using body. `SubscriptionAddCouponError1`: `Codes`, `CouponCode`, `CouponCodes`, `Subscription` as `IReadOnlyList<string>?` (`records-3-Of-Su.md`). `SubscriptionRemoveCouponErrors1.Subscription (subscription): IReadOnlyList<string> !req` (`records-4-Su-We.md`).

At **signup**, set `CreateSubscription.CouponCode` or `CouponCodes` (Step 6) — do not call apply-coupon until the subscription exists.

---

### Step 6 — Subscriptions · `client.Subscriptions` · `operations/Subscriptions.md`

Identify product with `product_id` **or** `product_handle`; customer with `customer_id` **or** `customer_reference`; optional `payment_profile_id`. 3DS: 422 with `action_link` — **UNVERIFIED** whether that lives on `ErrorListResponse1.Errors` strings or only the raw body; if typed accessor has no link, `TryGetRawError` → `ReadAsString()`.

| Op | Signature | Returns | Error | Pagination |
|---|---|---|---|---|
| `PreviewSubscription` | `(CreateSubscriptionRequest? body, CancellationToken ct = default)` | `SubscriptionPreviewResponse` | **B** | none |
| `CreateSubscription` | `(CreateSubscriptionRequest? body, CancellationToken ct = default)` | `SubscriptionResponse` | **A** `CreateSubscriptionError`: `TryGetErrorListResponse1` [422] | none |
| `ReadSubscription` | `(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` must pass | `SubscriptionResponse` | **B** | none |
| `FindSubscription` | `(string? reference, CancellationToken ct = default)` — `reference` must pass | `SubscriptionResponse` | **A** `FindSubscriptionError`: `TryGetNoContent` [404] | none |
| `ListSubscriptions` | `(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 14 nullable no-default | `IReadOnlyList<SubscriptionResponse>` | **B** | `page`+`perPage` |
| `UpdateSubscription` | `(int subscriptionId, UpdateSubscriptionRequest? body, CancellationToken ct = default)` | `SubscriptionResponse` | **A** `UpdateSubscriptionError`: `TryGetErrorListResponse1` [422] | none |
| `ActivateSubscription` | `(int subscriptionId, ActivateSubscriptionRequest? body, CancellationToken ct = default)` | `SubscriptionResponse` | **A** `ActivateSubscriptionError`: `TryGetErrorArrayMapResponse1(out ErrorArrayMapResponse1)` [400] | none |
| `OverrideSubscription` | `(int subscriptionId, OverrideSubscriptionRequest? body, CancellationToken ct = default)` | `void` | **A** `OverrideSubscriptionError`: `TryGetSingleErrorResponse1(out SingleErrorResponse1)` [422] | none |
| `UpdatePrepaidSubscriptionConfiguration` | `(int subscriptionId, UpsertPrepaidConfigurationRequest? body, CancellationToken ct = default)` | `PrepaidConfigurationResponse` | **A** `UpdatePrepaidSubscriptionConfigurationError`: `TryGetPrepaidConfigurationErrorResponse(out PrepaidConfigurationErrorResponse)` [422] — union `ErrorStringMapResponse1` \| `ErrorListResponse1` (`unions.md`) | none |
| `PurgeSubscription` | `(int subscriptionId, int ack, IReadOnlyList<SubscriptionPurgeType>? cascade, CancellationToken ct = default)` — test-mode only | `SubscriptionResponse` | **A** `PurgeSubscriptionError`: `TryGetSubscriptionResponse(out SubscriptionResponse)` [400] | none |

**Envelope:** `SubscriptionResponse.Subscription (subscription): Subscription?` — **nullable**, unlike `CustomerResponse` (`records-4-Su-We.md`). Always null-check. Inner used: `Id`, `State (state): SubscriptionState?`, `BalanceInCents`, `CurrentPeriodEndsAt`, `CurrentPeriodStartedAt`, `NextAssessmentAt`, `CancelAtEndOfPeriod`, `CanceledAt`, `CouponCode`/`CouponCodes`, `PaymentCollectionMethod`, `Customer (customer): Customer?`, `Product (product): Product?`, `CreditCard`, `ProductPricePointId`, `Reference`, `SelfServicePageToken` (only if `include` contains `SubscriptionInclude.SelfServicePageToken`).

`CreateSubscriptionRequest.Subscription !req` → `CreateSubscription` fields the storefront sets (`records-2-Cr-Ne.md`):

| Field | Wire | Type | Notes |
|---|---|---|---|
| `ProductId` / `ProductHandle` | `product_id` / `product_handle` | `int?` / `string?` | one required in practice |
| `ProductPricePointId` / `ProductPricePointHandle` | `product_price_point_id` / `product_price_point_handle` | `int?` / `string?` | else product default PP |
| `CustomerId` / `CustomerReference` | `customer_id` / `customer_reference` | `int?` / `string?` | or nested `CustomerAttributes` |
| `PaymentProfileId` | `payment_profile_id` | `int?` | existing profile |
| `PaymentProfileAttributes` / `CreditCardAttributes` | `payment_profile_attributes` / `credit_card_attributes` | `PaymentProfileAttributes?` | token: `ChargifyToken` |
| `CouponCode` / `CouponCodes` | `coupon_code` / `coupon_codes` | `string?` / `IReadOnlyList<string>?` | signup coupons |
| `PaymentCollectionMethod` | `payment_collection_method` | `CollectionMethod?` | |
| `Reference` | `reference` | `string?` | storefront subscription key |
| `Components` | `components` | `IReadOnlyList<CreateSubscriptionComponent>?` | initial allocations |
| `CustomerAttributes` | `customer_attributes` | `CustomerAttributes?` | inline new customer |
| `AgreementAcceptance` | `agreement_acceptance` | `AgreementAcceptance?` | required for Maxio Payments |
| `SkipBillingManifestTaxes` | `skip_billing_manifest_taxes` | `bool?` | preview tax skip |

`CreateSubscriptionComponent`: `ComponentId` (`ComponentId1` union), `Enabled?`, `AllocatedQuantity` (`AllocatedQuantity3` union), `Quantity?`, `PricePointId` (`PricePointId2` union), `UnitBalance?`, `CustomPrice?` (`records-2-Cr-Ne.md`).

Preview envelope: `SubscriptionPreviewResponse.SubscriptionPreview !req` → `CurrentBillingManifest` / `NextBillingManifest` (`BillingManifest`: `TotalInCents`, `LineItems`, …) (`records-4-Su-We.md`, `records-1-Ac-Cr.md`).

`UpdateSubscriptionRequest.Subscription !req` → delayed plan change: `ProductHandle`/`ProductId` + `ProductChangeDelayed (product_change_delayed): bool?`; cancel delayed: `NextProductId (next_product_id): string?` empty string; also `NextBillingAt`, `ProductPricePointId`/`Handle`, `CreditCardAttributes` (`records-4-Su-We.md`).

`ActivateSubscriptionRequest.RevertOnFailure (revert_on_failure): bool?` (`records-1-Ac-Cr.md`).

---

### Step 7 — Components & usage

#### Catalog · `client.Components` · `operations/Components.md`

| Op | Signature | Returns | Error |
|---|---|---|---|
| `ListComponents` | `(BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, bool? includeArchived, ListComponentsFilter? filter, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 7 must-pass | `IReadOnlyList<ComponentResponse>` | **B** · pagination |
| `ListComponentsForProductFamily` | `(int productFamilyId, bool? includeArchived, ListComponentsFilter? filter, BasicDateField? dateField, string? endDate, string? endDatetime, string? startDate, string? startDatetime, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | `IReadOnlyList<ComponentResponse>` | **B** |
| `FindComponent` | `(string handle, CancellationToken ct = default)` query `handle` | `ComponentResponse` | **B** |
| `ReadComponent` | `(int productFamilyId, string componentId, CancellationToken ct = default)` — handle prefix `handle:` | `ComponentResponse` | **B** |
| `CreateMeteredComponent` | `(string productFamilyId, CreateMeteredComponent? body, CancellationToken ct = default)` | `ComponentResponse` | **A**: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] |
| `CreateQuantityBasedComponent` | `(string productFamilyId, CreateQuantityBasedComponent? body, …)` | `ComponentResponse` | same 404/422 |
| `CreateOnOffComponent` | `(string productFamilyId, CreateOnOffComponent? body, …)` | `ComponentResponse` | same |
| `CreatePrepaidUsageComponent` | `(string productFamilyId, CreatePrepaidComponent? body, …)` | `ComponentResponse` | same |
| `CreateEventBasedComponent` | `(string productFamilyId, CreateEbbComponent? body, …)` | `ComponentResponse` | same |
| `UpdateComponent` | `(string componentId, UpdateComponentRequest? body, …)` | `ComponentResponse` | **A** `UpdateComponentError`: `TryGetErrorListResponse1` [422] |
| `ArchiveComponent` | `(int productFamilyId, string componentId, …)` | `Component` (not enveloped) | **A** `ArchiveComponentError`: `TryGetErrorListResponse1` [422] |

`ComponentResponse.Component (component): Component !req` (`records-1-Ac-Cr.md`). Inner: `Id`, `Name`, `Handle`, `Kind (kind): ComponentKind?`, `UnitName`, `UnitPrice (unit_price): string?`, `PricingScheme`, `Recurring`, `ProductFamilyId`, `DefaultPricePointId`, `Archived`.

Create envelopes (`records-1-Ac-Cr.md` / `records-2-Cr-Ne.md` / `records-3-Of-Su.md`):

- `CreateMeteredComponent.MeteredComponent !req` → `Name !req`, `UnitName !req`, `PricingScheme !req`; `Prices?`, `UnitPrice` (`UnitPrice1` union), `Handle?`
- `CreateQuantityBasedComponent.QuantityBasedComponent !req` → `Name !req`, `UnitName !req`, `PricingScheme !req`; `Recurring?` (false = one-time)
- `CreateOnOffComponent.OnOffComponent !req` → `Name !req`, `UnitPrice (UnitPrice3) !req`
- `CreatePrepaidComponent.PrepaidUsageComponent !req` → `Name !req`, `UnitName !req`, `PricingScheme !req`, `OveragePricing !req`
- `CreateEbbComponent.EventBasedComponent !req` → `Name !req`, `UnitName !req`, `PricingScheme !req`, `EventBasedBillingMetricId !req`

`Price`: `StartingQuantity !req` (`StartingQuantity` union), `UnitPrice !req` (`UnitPrice` union), `EndingQuantity?` (`records-3-Of-Su.md`).

#### On a subscription · `client.SubscriptionComponents` · `operations/SubscriptionComponents.md`

Usage path unions: `SubscriptionIdOrReference` (`Int`/`String`), `ComponentIdModel` (`Int`/`String`) — `MaxioAdvancedBilling.Models.AnyOf` (`unions.md`).

| Op | Signature | Returns | Error | Pagination |
|---|---|---|---|---|
| `CreateUsage` | `(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, CreateUsageRequest? body, CancellationToken ct = default)` | `UsageResponse` | **A** `CreateUsageError`: `TryGetErrorListResponse1` [422] | none |
| `ListUsages` | `(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, long? sinceId, long? maxId, DateTimeOffset? sinceDate, DateTimeOffset? untilDate, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 4 must-pass | `IReadOnlyList<UsageResponse>` | **B** | `page`+`perPage` |
| `AllocateComponent` | `(int subscriptionId, int componentId, CreateAllocationRequest? body, CancellationToken ct = default)` | `AllocationResponse` | **A** `AllocateComponentError`: `TryGetErrorListResponse1` [422] | none |
| `AllocateComponents` | `(int subscriptionId, AllocateComponents? body, CancellationToken ct = default)` | `IReadOnlyList<AllocationResponse>` | **A** `AllocateComponentsError`: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] | none |
| `PreviewAllocations` | `(int subscriptionId, PreviewAllocationsRequest? body, CancellationToken ct = default)` | `AllocationPreviewResponse` | **A** `PreviewAllocationsError`: `TryGetComponentAllocationError1(out ComponentAllocationError1)` [422] | none |
| `ListSubscriptionComponents` | `(int subscriptionId, SubscriptionListDateField? dateField, SortingDirection? direction, ListSubscriptionComponentsFilter? filter, string? endDate, string? endDatetime, IncludeNotNull? pricePointIds, IReadOnlyList<int>? productFamilyIds, ListSubscriptionComponentsSort? sort, string? startDate, string? startDatetime, IReadOnlyList<ListSubscriptionComponentsInclude>? include, bool? inUse, CancellationToken ct = default)` — 12 must-pass | `IReadOnlyList<SubscriptionComponentResponse>` | **B** | none |
| `ReadSubscriptionComponent` | `(int subscriptionId, int componentId, CancellationToken ct = default)` | `SubscriptionComponentResponse` | **A** `ReadSubscriptionComponentError`: `TryGetNoContent` [404] | none |
| `ListAllocations` | `(int subscriptionId, int componentId, int? page = 1, CancellationToken ct = default)` | `IReadOnlyList<AllocationResponse>` | **A**: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] | `page` only |
| `ListSubscriptionComponentsForSite` | `(ListSubscriptionComponentsSort? sort, SortingDirection? direction, ListSubscriptionComponentsForSiteFilter? filter, …, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 12 must-pass | `ListSubscriptionComponentsResponse` | **B** | `page`+`perPage` |
| `ActivateEventBasedComponent` | `(int subscriptionId, int componentId, ActivateEventBasedComponent? body, …)` | `void` | **B** | none |
| `DeactivateEventBasedComponent` | `(int subscriptionId, int componentId, …)` | `void` | **B** | none |
| `RecordEvent` | `(string apiHandle, string? storeUid, EbbEvent? body, …)` **Ebb server** | `void` | **B** | none |
| `BulkRecordEvents` | `(string apiHandle, string? storeUid, IReadOnlyList<EbbEvent>? body, …)` **Ebb server** | `void` | **B** | none |
| `DeletePrepaidUsageAllocation` | `(int subscriptionId, int componentId, int allocationId, CreditSchemeRequest? body, …)` | `void` | **A**: `TryGetNoContent` [404] · `TryGetSubscriptionComponentAllocationError1` [422] | none |

**Metered/prepaid usage (not quantity-based):** `CreateUsageRequest.Usage !req` → `CreateUsage`: `Quantity (quantity): double?` (negative deducts; floor 0), `Memo?`, `PricePointId (price_point_id): string?`, `BillingSchedule?`, `CustomPrice?` (`records-2-Cr-Ne.md`). Response: `UsageResponse.Usage !req` → `Id`, `Quantity` (`Quantity1` union), `ComponentId`, `SubscriptionId`, `Memo`, `CreatedAt` (`records-4-Su-We.md`). One component per call (`operations/SubscriptionComponents.md`).

**Quantity / on-off / prepaid allocation:** `CreateAllocationRequest.Allocation !req` → `CreateAllocation`: `Quantity (quantity): double !req`, `ComponentId?`, `Memo?`, `UpgradeCharge` (`UpgradeChargeCreditType?`), `DowngradeCredit` (`DowngradeCreditCreditType?`), `AccrueCharge?`, `PricePointId` (`PricePointId1` union) (`records-1-Ac-Cr.md`). `AllocationResponse.Allocation (allocation): Allocation?`. `AllocateComponents` body: `Allocations: IReadOnlyList<CreateAllocation>?` plus top-level proration/accrue (`records-1-Ac-Cr.md`). `PreviewAllocationsRequest.Allocations !req`. Envelope `AllocationPreviewResponse.AllocationPreview !req` → `TotalInCents`, `Direction` (`AllocationPreviewDirection?`), `LineItems`.

`SubscriptionComponentResponse.Component (component): SubscriptionComponent?` (`records-3-Of-Su.md`). Inner: `ComponentId`, `Name`, `Kind`, `Enabled`, `UnitBalance`, `AllocatedQuantity` (`AllocatedQuantity2` union), `PricePointId`.

---

### Step 8 — Plan changes · `client.SubscriptionProducts` · `operations/SubscriptionProducts.md`

Migrate requires subscription `active` or `trialing`. Pass `product_id` **or** `product_handle`. Same-product migrate is a common 422 (`operations/SubscriptionProducts.md`).

| Op | Signature | Returns | Error |
|---|---|---|---|
| `PreviewSubscriptionProductMigration` | `(int subscriptionId, SubscriptionMigrationPreviewRequest? body, CancellationToken ct = default)` | `SubscriptionMigrationPreviewResponse` | **A** `PreviewSubscriptionProductMigrationError`: `TryGetErrorListResponse1` [422] |
| `MigrateSubscriptionProduct` | `(int subscriptionId, SubscriptionProductMigrationRequest? body, CancellationToken ct = default)` | `SubscriptionResponse` | **A** `MigrateSubscriptionProductError`: `TryGetErrorListResponse1` [422] |

`SubscriptionProductMigrationRequest.Migration !req` → `SubscriptionProductMigration`: `ProductId?`, `ProductHandle?`, `ProductPricePointId?`, `ProductPricePointHandle?`, `IncludeTrial (include_trial): bool? = false`, `IncludeInitialCharge = false`, `IncludeCoupons = true`, `PreservePeriod = false`, `Proration (proration): Proration?` (`Proration.PreservePeriod (preserve_period): bool?`) (`records-4-Su-We.md`).

Preview request uses `SubscriptionMigrationPreviewOptions` (same product fields + `ProrationDate (proration_date): DateTimeOffset?`). Response: `SubscriptionMigrationPreviewResponse.Migration !req` → `ProratedAdjustmentInCents`, `ChargeInCents`, `PaymentDueInCents`, `CreditAppliedInCents`.

Delayed (next renewal, no proration): `UpdateSubscription` with `ProductChangeDelayed = true` (Step 6). Immediate prorated: this migrate pair.

---

### Step 9 — Invoices · `client.Invoices` · `operations/Invoices.md`

List totals-only unless include flags are `true`. `ReadInvoice` returns `Invoice` **directly** (no envelope). `CreateInvoice` returns `InvoiceResponse`.

| Op | Signature | Returns | Error | Pagination |
|---|---|---|---|---|
| `ListInvoices` | `(string? startDate, string? endDate, InvoiceStatus? status, int? subscriptionId, string? subscriptionGroupUid, string? consolidationLevel, Direction? direction, InvoiceDateField? dateField, string? startDatetime, string? endDatetime, IReadOnlyList<int>? customerIds, IReadOnlyList<string>? number, IReadOnlyList<int>? productIds, InvoiceSortField? sort, int? page = 1, int? perPage = 20, bool? lineItems = false, bool? discounts = false, bool? taxes = false, bool? credits = false, bool? payments = false, bool? customFields = false, bool? refunds = false, CancellationToken ct = default)` — 14 must-pass | `ListInvoicesResponse` | **B** | `page`+`perPage` |
| `ReadInvoice` | `(string uid, CancellationToken ct = default)` | `Invoice` | **B** | none |
| `CreateInvoice` | `(int subscriptionId, CreateInvoiceRequest? body, CancellationToken ct = default)` | `InvoiceResponse` | **A** `CreateInvoiceError`: `TryGetErrorArrayMapResponse1` [422] | none |
| `IssueInvoice` | `(string uid, IssueInvoiceRequest? body, CancellationToken ct = default)` | `Invoice` | **A** `IssueInvoiceError`: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] | none |
| `RecordPaymentForInvoice` | `(string uid, CreateInvoicePaymentRequest? body, CancellationToken ct = default)` | `Invoice` | **A** `RecordPaymentForInvoiceError`: `TryGetErrorListResponse1` [422] | none |
| `RecordPaymentForSubscription` | `(int subscriptionId, RecordPaymentRequest? body, CancellationToken ct = default)` | `RecordPaymentResponse` | **A** `RecordPaymentForSubscriptionError`: `TryGetErrorListResponse1` [422] | none |
| `RecordPaymentForMultipleInvoices` | `(CreateMultiInvoicePaymentRequest? body, CancellationToken ct = default)` | `MultiInvoicePaymentResponse` | **A**: `TryGetErrorListResponse1` [422] | none |
| `RefundInvoice` | `(string uid, RefundInvoiceRequest? body, CancellationToken ct = default)` | `Invoice` | **A** `RefundInvoiceError`: `TryGetErrorListResponse1` [422] | none |
| `VoidInvoice` | `(string uid, VoidInvoiceRequest? body, CancellationToken ct = default)` | `Invoice` | **A** `VoidInvoiceError`: `TryGetObject(out object?)` [404] · `TryGetErrorListResponse1` [422] | none |
| `ReopenInvoice` | `(string uid, CancellationToken ct = default)` | `Invoice` | **A** `ReopenInvoiceError`: `TryGetObject(out object?)` [404] · `TryGetErrorListResponse1` [422] | none |
| `SendInvoice` | `(string uid, SendInvoiceRequest? body, CancellationToken ct = default)` | `void` | **A** `SendInvoiceError`: `TryGetErrorListResponse1` [422] | none |
| `ListCreditNotes` | `(int? subscriptionId, int? page = 1, int? perPage = 20, bool? lineItems = false, …)` | `ListCreditNotesResponse` | **B** | `page`+`perPage` |
| `ReadCreditNote` | `(string uid, CancellationToken ct = default)` | `CreditNote` | **B** | none |

`ListInvoicesResponse.Invoices (invoices): IReadOnlyList<Invoice> !req`. `InvoiceResponse.Invoice !req`. Storefront `Invoice` fields (`records-2-Cr-Ne.md`): `Uid (uid): string?`, `Id`, `SubscriptionId`, `CustomerId`, `Number`, `Status (status): InvoiceStatus?`, `DueDate`, `IssueDate`, `TotalAmount (total_amount): string?`, `DueAmount`, `PaidAmount`, `Currency`, `PublicUrl`, `LineItems` (when included).

`CreateInvoiceRequest.Invoice !req` → `LineItems`, `Coupons`, `Status` default `CreateInvoiceStatus.Open`, `IssueDate`, `NetTerms`, `Memo` (`records-1-Ac-Cr.md`). `CreateInvoicePaymentRequest.Payment !req` → `Amount` (`Amount` union), `Memo?`, `Method` (`InvoicePaymentMethodType?`), `PaymentProfileId?`; outer `Type (type): InvoicePaymentType?`. `RecordPaymentRequest.Payment !req` → `CreatePayment`: `Amount !req`, `Memo !req`, `PaymentDetails !req`, `PaymentMethod !req`. `IssueInvoiceRequest.OnFailedPayment` default `FailedPaymentAction.LeaveOpenInvoice`. `VoidInvoiceRequest.Void !req` → `VoidInvoice.Reason (reason): string !req`. `RefundInvoiceRequest.Refund !req` **union** `Refund`: `Refund.RefundInvoice(RefundInvoice)` or `Refund.RefundConsolidatedInvoice` (`unions.md`); `RefundInvoice`: `Amount !req`, `Memo !req`, `PaymentId !req`. `SendInvoiceRequest`: `RecipientEmails`, `CcRecipientEmails`, `BccRecipientEmails`, `AttachmentUrls`.

---

### Step 10 — Lifecycle · `client.SubscriptionStatus` · `operations/SubscriptionStatus.md`

| Op | Signature | Returns | Error |
|---|---|---|---|
| `CancelSubscription` | `(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` — omit body fields for immediate cancel; `body` still must be passed (`null` ok) | `SubscriptionResponse` | **A** `CancelSubscriptionApiError`: `TryGetNoContent` [404] · `TryGetCancelSubscriptionErrorResponse(out CancelSubscriptionErrorResponse)` [422] — union `ErrorListResponse1` \| `SingleErrorResponse1` (`unions.md`) |
| `InitiateDelayedCancellation` | `(int subscriptionId, CancellationRequest? body, …)` | `DelayedCancellationResponse` | **A**: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] |
| `CancelDelayedCancellation` | `(int subscriptionId, …)` | `DelayedCancellationResponse` | **A**: `TryGetNoContent` [404] |
| `PauseSubscription` | `(int subscriptionId, PauseRequest? body, …)` | `SubscriptionResponse` | **A**: `TryGetErrorListResponse1` [422] |
| `UpdateAutomaticSubscriptionResumption` | `(int subscriptionId, PauseRequest? body, …)` | `SubscriptionResponse` | **A**: `TryGetErrorListResponse1` [422] |
| `ResumeSubscription` | `(int subscriptionId, ResumptionCharge? calendarBillingResumptionCharge, …)` — must pass | `SubscriptionResponse` | **A**: `TryGetErrorListResponse1` [422] |
| `ReactivateSubscription` | `(int subscriptionId, ReactivateSubscriptionRequest? body, …)` | `SubscriptionResponse` | **A**: `TryGetErrorListResponse1` [422] |
| `RetrySubscription` | `(int subscriptionId, …)` | `SubscriptionResponse` | **A**: `TryGetErrorListResponse1` [422] |
| `PreviewRenewal` | `(int subscriptionId, RenewalPreviewRequest? body, …)` | `RenewalPreviewResponse` | **A**: `TryGetErrorListResponse1` [422] |
| `CancelDunning` | `(int subscriptionId, …)` | `SubscriptionResponse` | **A**: `TryGetErrorListResponse1` [422] |

`CancellationRequest.Subscription !req` → `CancellationOptions`: `CancellationMessage?`, `ReasonCode?`, `CancelAtEndOfPeriod?`, `ScheduledCancellationAt?`, `RefundPrepaymentAccountBalance?` (`records-1-Ac-Cr.md`). `DelayedCancellationResponse.Message (message): string?`. `PauseRequest.Hold (hold): AutoResume?` → `AutomaticallyResumeAt`. `ReactivateSubscriptionRequest`: `IncludeTrial?`, `PreserveBalance?`, `CouponCode?`, `Resume` (`Resume` union: `bool` \| `ResumeOptions`). `RenewalPreviewResponse.RenewalPreview !req` → `TotalInCents`, `NextAssessmentAt`, `LineItems`.

---

### Step 11 — Billing portal · `client.BillingPortal` · `operations/BillingPortal.md`

| Op | Signature | Returns | Error |
|---|---|---|---|
| `EnableBillingPortalForCustomer` | `(int customerId, AutoInvite? autoInvite, CancellationToken ct = default)` — `autoInvite` must pass | `CustomerResponse` | **A**: `TryGetErrorListResponse1` [422] |
| `ReadBillingPortalLink` | `(int customerId, CancellationToken ct = default)` | `PortalManagementLink` | **A**: `TryGetErrorListResponse1` [422] · `TryGetTooManyManagementLinkRequestsError1(out TooManyManagementLinkRequestsError1)` [429] |
| `ResendBillingPortalInvitation` | `(int customerId, …)` | `ResentInvitation` | **A**: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] |
| `RevokeBillingPortalAccess` | `(int customerId, …)` | `RevokedInvitation` | **B** |

`PortalManagementLink`: `Url`, `FetchCount`, `NewLinkAvailableAt`, `ExpiresAt` (`records-3-Of-Su.md`). Cache URL; 429 payload `TooManyManagementLinkRequestsError1.Errors !req` → `Error (error): string !req`, `NewLinkAvailableAt !req` (`records-4-Su-We.md`).

---

### Shared error payloads

| Type | Fields | Page |
|---|---|---|
| `ErrorListResponse1` | `Errors (errors): IReadOnlyList<string> !req` | `records-2-Cr-Ne.md` |
| `ErrorArrayMapResponse1` | `Errors (errors): IReadOnlyDictionary<string, object>?` | `records-2-Cr-Ne.md` |
| `ErrorStringMapResponse1` | `Errors (errors): IReadOnlyDictionary<string, string>?` | `records-2-Cr-Ne.md` |
| `SingleErrorResponse1` | `Error (error): string !req` | `records-3-Of-Su.md` |
| `SingleStringErrorResponse1` | `Errors (errors): string?` | `records-3-Of-Su.md` |
| `ComponentAllocationError1` | `Errors: IReadOnlyList<ComponentAllocationErrorItem>?` (`ComponentId`, `Message`, `Kind`, `On`) | `records-1-Ac-Cr.md` |
| `ProductPricePointErrorResponse1` | `Errors: ProductPricePointErrors !req` | `records-3-Of-Su.md` |
| `ApiError` | `TryGetRawError(out RawError): bool` | `sdk-map.md` |
| `RawError` | `StatusCode: HttpStatusCode` · `ReadAsString()` · `ReadAsJson<T>()` · `ReadAsBytes()` | `sdk-map.md` |
| `SdkException<TError>` | `Error: TError` · namespace `MaxioAdvancedBilling.Core.Exceptions` | `sdk-map.md` |

---

### Enums in scope (`map/models/enums.md` — `StringEnum<T>` / `IntEnum<T>`, **not** C# enums)

Use `Type.Member` or `Type.FromValue("wire")`. Namespace `MaxioAdvancedBilling.Models.Enums` except `ServerEnvironment` (`MaxioAdvancedBilling.Servers`).

| Enum | Members (`CSharp (wire)`) |
|---|---|
| `ServerEnvironment` | `Us (US)`, `Eu (EU)` |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` |
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `SubscriptionStateFilter` | `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)` |
| `SubscriptionInclude` | `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)` |
| `SubscriptionListInclude` | `SelfServicePageToken (self_service_page_token)` |
| `SubscriptionSort` | `SignupDate (signup_date)`, `PeriodStart (period_start)`, `PeriodEnd (period_end)`, `NextAssessment (next_assessment)`, `UpdatedAt (updated_at)`, `CreatedAt (created_at)`, `TotalPayments (total_payments)`, `Id (id)`, `OpenBalance (open_balance)`, `ExpiresAt (expires_at)` |
| `SubscriptionDateField` | `CurrentPeriodEndsAt`, `CurrentPeriodStartsAt`, `CreatedAt`, `ActivatedAt`, `CanceledAt`, `ExpiresAt`, `TrialStartedAt`, `TrialEndedAt`, `UpdatedAt` (wires snake_case) |
| `SubscriptionPurgeType` | `Customer (customer)`, `PaymentProfile (payment_profile)` |
| `IntervalUnit` | `Day (day)`, `Month (month)` |
| `PricePointType` | `Catalog (catalog)`, `Default (default)`, `Custom (custom)` |
| `ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)` |
| `ListProductsPricePointsInclude` | `CurrencyPrices (currency_prices)` |
| `ComponentKind` | `MeteredComponent (metered_component)`, `QuantityBasedComponent (quantity_based_component)`, `OnOffComponent (on_off_component)`, `PrepaidUsageComponent (prepaid_usage_component)`, `EventBasedComponent (event_based_component)` |
| `PricingScheme` | `Stairstep (stairstep)`, `Volume (volume)`, `PerUnit (per_unit)`, `Tiered (tiered)` |
| `CreditType` | `Full (full)`, `Prorated (prorated)`, `None (none)` |
| `UpgradeChargeCreditType` / `DowngradeCreditCreditType` | `Full`, `Prorated`, `None` (same wires) |
| `CreditScheme` | `None (none)`, `Credit (credit)`, `Refund (refund)` |
| `InvoiceStatus` | `Draft (draft)`, `Open (open)`, `Paid (paid)`, `Pending (pending)`, `Voided (voided)`, `Canceled (canceled)`, `Processing (processing)` |
| `InvoiceDateField` | `CreatedAt`, `DueDate`, `IssueDate`, `UpdatedAt`, `PaidDate` |
| `InvoiceSortField` | `Status`, `TotalAmount`, `DueAmount`, `CreatedAt`, `UpdatedAt`, `IssueDate`, `DueDate`, `Number` |
| `InvoicePaymentType` | `External (external)`, `Prepayment (prepayment)`, `ServiceCredit (service_credit)`, `Payment (payment)` |
| `InvoicePaymentMethodType` | `CreditCard (credit_card)`, `Check (check)`, `Cash (cash)`, `MoneyOrder (money_order)`, `Ach (ach)`, `Other (other)` |
| `CreateInvoiceStatus` | `Draft (draft)`, `Open (open)` |
| `FailedPaymentAction` | `LeaveOpenInvoice (leave_open_invoice)`, `RollbackToPending (rollback_to_pending)`, `InitiateDunning (initiate_dunning)` |
| `DiscountType` | `Amount (amount)`, `Percent (percent)` |
| `CompoundingStrategy` | `Compound (compound)`, `FullPrice (full-price)` |
| `RecurringScheme` | `DoNotRecur (do_not_recur)`, `RecurIndefinitely (recur_indefinitely)`, `RecurWithDuration (recur_with_duration)` |
| `PaymentType` | `CreditCard (credit_card)`, `BankAccount (bank_account)`, `PaypalAccount (paypal_account)`, `ApplePay (apple_pay)` |
| `SortingDirection` / `Direction` | `Asc (asc)`, `Desc (desc)` |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` |
| `ListSubscriptionComponentsInclude` | `Subscription (subscription)`, `HistoricUsages (historic_usages)` |
| `ListSubscriptionComponentsSort` | `Id (id)`, `UpdatedAt (updated_at)` |
| `IncludeNotNull` | `NotNull (not_null)` |
| `ResumptionCharge` | `Prorated (prorated)`, `Immediate (immediate)`, `Delayed (delayed)` |
| `ReactivationCharge` | `Prorated`, `Immediate`, `Delayed` |
| `AutoInvite` | IntEnum `Value0 (0)`, `Value1 (1)` |
| `TrialType` | `NoObligation (no_obligation)`, `PaymentExpected (payment_expected)` |
| `ExpirationIntervalUnit` | `Day (day)`, `Month (month)`, `Never (never)` |
| `AllocationPreviewDirection` | `Upgrade (upgrade)`, `Downgrade (downgrade)` |
| `CancellationMethod` | `MerchantUi`, `MerchantApi`, `Dunning`, `BillingPortal`, `Unknown`, `Imported` |
| `InvoiceEventType` | `IssueInvoice`, `ApplyCreditNote`, `CreateCreditNote`, `ApplyPayment`, `ApplyDebitNote`, `CreateDebitNote`, `RefundInvoice`, `VoidInvoice`, `VoidRemainder`, `BackportInvoice`, `ChangeInvoiceStatus`, `ChangeInvoiceCollectionMethod`, `RemovePayment`, `FailedPayment`, `ChangeChargebackStatus` |

---

### Unions in scope (`map/models/unions.md`)

Construct with factories; read with `TryGet…`. Never `new` a union.

| Union | Namespace | Factories / TryGet |
|---|---|---|
| `ProductIdModel`, `PricePointIdModel`, `ComponentIdModel`, `SubscriptionIdOrReference` | `Models.AnyOf` | `Int(int)`, `String(string)` |
| `PaymentProfile` | `Models.OneOf` | `CreditCardPaymentProfile`, `BankAccountPaymentProfile`, `PaypalPaymentProfile`, `ApplePayPaymentProfile` |
| `Quantity` / `Quantity1` / `AllocatedQuantity2` / `AllocatedQuantity3` | `AnyOf` | `Int` / `String` |
| `Amount`, `Amount1`, `Percentage`, `UnitPrice`, `UnitPrice1`, `UnitPrice3` | `AnyOf` | `String` / `Double` (order per row in `unions.md`) |
| `ExpirationMonth1`/`Year1`, `ExpirationMonth2`/`Year2` | `AnyOf` | `Int` / `String` |
| `Refund` | `AnyOf` | `RefundInvoice`, `RefundConsolidatedInvoice` |
| `Resume` | `AnyOf` | `Bool(bool)`, `ResumeOptions` |
| `CancelSubscriptionErrorResponse` | `AnyOf` | `ErrorListResponse1`, `SingleErrorResponse1` |
| `PrepaidConfigurationErrorResponse` | `AnyOf` | `ErrorStringMapResponse1`, `ErrorListResponse1` |
| `PricePointId1`/`2`/`3`/`4`, `ComponentId1`/`2`/`3`, `ProductId`, `ProductFamilyId`, `SnapDay1`, `NetTerms1` | `AnyOf` | `Int`/`String` as listed in `unions.md` |

---

## Trap notes

⚠ Step 1 (client registration) — `HttpClient` ownership/lifetime vs the SDK wrapper lifetime is not visible on the constructor. **MUST load `dotnet-client-initialization`** before `new MaxioAdvancedBillingClient` or `AddMaxioAdvancedBillingClient`.

⚠ Step 1 (auth) — credential property names, when they must be set relative to construction, and loading the API key from configuration are not on `BasicAuthCredentials`. **MUST load `dotnet-authentication`** before wiring `BasicAuth`.

⚠ Step 1 (resilience) — `RetryOptions.Timeout` / retry members do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; whether a failed write can be re-sent is not settled by the option names. **MUST load `dotnet-configuration-resilience`** before setting `options.Retry` or `options.Server`.

⚠ Steps 2–11 (every list/search call) — many optional parameters have no C# default and mis-bind positionally (`ListCustomers`, `ListSubscriptions`, `ListInvoices`, `ListProducts`, `FindCoupon`, `ReadSubscription.include`, …). **MUST load `dotnet-calling-endpoints`** before the first `client.{Group}.{Op}`.

⚠ Steps 3–9 (payloads) — unions (`PaymentProfile`, `ProductIdModel`, `SubscriptionIdOrReference`, `Quantity*`, `Refund`, `Resume`) have no usable `new`; enums are `StringEnum<T>` not C# enums; `required` vs nullable members are easy to miss on envelopes. **MUST load `dotnet-models`** before constructing any request or reading a union envelope.

⚠ Step 12 (error boundary) — Case A vs Case B differs per operation (this sheet marks each row); `TryGetRawError` is not a catch-all on typed errors; there are **no** no-throw variants. **MUST load `dotnet-error-handling`** before any `try/catch`.

⚠ Step 12 — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 12 — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. Highest-risk here: `CreateCustomer`/`UpdateCustomer` 422 (`CustomerErrorResponse1.Errors` typed as generated `Errors` with only `per_page`/`price_point`). **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 12 (tests) — the constructor `HttpClient` argument is the test seam; match eShopOnWeb's existing test framework. **MUST load `dotnet-testing`** before stubbing the SDK.

---

## REQUIRED READING

Load **before implementation starts**. This sheet does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — constructing / DI-registering `MaxioAdvancedBillingClient` |
| `dotnet-authentication` | Step 1 — `BasicAuthCredentials` |
| `dotnet-configuration-resilience` | Step 1 — `RetryOptions`, timeouts, `ServerOptions` site/base URL, list pagination |
| `dotnet-calling-endpoints` | Steps 2–11 — every operation call (named args, `ct:`) |
| `dotnet-models` | Steps 3–9 — request records, envelopes, unions, `StringEnum<T>` |
| `dotnet-error-handling` | Step 12 — Case A/B, `TryGet…`, both `JsonException` directions (always required) |
| `dotnet-testing` | Step 12 — faking the SDK via `HttpClient` |

---

## Assumptions & Blockers

**Assumptions**

- eShopOnWeb identity maps to Maxio via `Customer.Reference` = application user id; `ReadCustomerByReference` is the lookup. Duplicate `reference` on create is a 422.
- Product catalog, components, and coupons are created in Maxio (UI or admin APIs in this sheet); the public storefront lists/reads them and does not create products at checkout.
- Card data is tokenized with Maxio.js (`chargify_token` on `CreatePaymentProfile` / `PaymentProfileAttributes`); the storefront is not PCI-compliant for raw PAN.
- Default hosting is US (`ServerEnvironment.Us`); EU is only if the Maxio account is EU-hosted.
- Site subdomain and API key come from configuration (`options.Server.Production.Us.Site` + `BasicAuth.Username`); they are not in this repo.
- Plan change at checkout uses `SubscriptionProducts.MigrateSubscriptionProduct` (prorated). Delayed change uses `UpdateSubscription.ProductChangeDelayed`.
- Metered usage uses `CreateUsage`; quantity add-ons use `AllocateComponent`. EBB `RecordEvent` is optional and uses the Ebb server group.
- Immediate cancel: `CancelSubscription` with `body: null`. End-of-period: `InitiateDelayedCancellation` or `CancellationOptions.CancelAtEndOfPeriod`.
- Invoice identity on the wire is `uid` (`string`), not numeric `id`, for `ReadInvoice` / pay / void / refund.

**Blockers**

- None for planning. Implementation cannot call Maxio until API key + site subdomain are available in configuration.
- **UNVERIFIED** (live traffic only): whether a 422 create-customer body matches `CustomerErrorResponse1`/`Errors`, and where 3DS `action_link` appears on subscription create/migrate 422s — extract best-effort from typed accessors, fall back to `RawError.ReadAsString()`.
