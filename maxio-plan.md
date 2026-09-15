# Maxio Advanced Billing — eShopOnWeb subscription billing plan

Package `AsadAli.AdvancedBilling.Sdk` · root namespace `MaxioAdvancedBilling` · map stamp `v1.0.2` (`15db14b`). Install via NuGet only (`dotnet add package AsadAli.AdvancedBilling.Sdk`). Do not project-reference SDK source.

**PHASE-BUILD hero flow (overrides earlier catalog/payment assumptions):** catalog is already seeded — do **not** create families/products/components. Family handle `eshop-subscribe`; product handles `eshop-pro` and `basic-plan`. Numeric IDs are unstable; use handles. Both plans do **not** require a payment method — subscribe with no card, no Chargify.js token, no `payment_profile_id`, no 3DS. Hero ops only: list plans for the family, idempotent ensure-customer, create subscription, list that buyer’s subscriptions.

Buyer mapping: one Maxio customer per eShop user; `CreateCustomer.Reference` = buyer/user id; lookup `ReadCustomerByReference`. Subscription idempotency: `CreateSubscription.Reference` = `buyerId + ":" + productHandle`; lookup `FindSubscription`.

---

## Scope & sequence

1. **Client + DI + auth (hero)** — `AddMaxioAdvancedBillingClient` / ctor; `BasicAuth.Username` ← config `Maxio:ApiKey`, `Password` = `"x"`; `Server.Production.{Us|Eu}.Site` ← `Maxio:Subdomain`; when `Maxio:BaseUrl` is set, assign it verbatim to `Server.Production.{Us|Eu}.BaseUrl` instead of deriving the host from subdomain. Family handle from `Maxio:ProductFamilyHandle`.
2. **Ensure customer (hero)** — `ReadCustomerByReference(reference: buyerId)` → on 404 only, `CreateCustomer` with `FirstName`/`LastName`/`Email` + `Reference` = buyerId.
3. **List plans (hero)** — `ListProductsForProductFamily(productFamilyId: "handle:" + Maxio:ProductFamilyHandle, …)` — **not** a bare handle. Do **not** create catalog.
4. **Payment profile — skip (hero)** — do not call `CreatePaymentProfile`; do not send token / profile ids on subscribe.
5. **Subscribe (hero)** — `FindSubscription(reference: buyerId + ":" + productHandle)` → on 404 only, `CreateSubscription` with `ProductHandle` + `CustomerId` (or `CustomerReference`) + `Reference`; omit all payment fields.
6. **List buyer subscriptions (hero)** — `ListCustomerSubscriptions(customerId)` (optional client-side filter by `Subscription.Product.Handle` + non-EOL state).
7. **Plan change** — delayed/next-period: `UpdateSubscription` (`product_id`/`product_handle`, optional `product_change_delayed`); prorated migration: `PreviewSubscriptionProductMigration` then `MigrateSubscriptionProduct`.
8. **Cancel** — immediate `CancelSubscription`; end-of-period `InitiateDelayedCancellation`; undo `CancelDelayedCancellation`.
9. **Coupons** — apply-on-subscribe is `CreateSubscription.CouponCode`/`CouponCodes`. Existing sub: `ApplyCouponsToSubscription` / `RemoveCouponFromSubscription`. Validate at checkout: `ValidateCoupon` / `FindCoupon`.
10. **Invoices** — `ListInvoices` (`subscriptionId` and/or `customerIds`); `ReadInvoice`.
11. **Usage (metered → invoice)** — allocate at signup via `CreateSubscription.Components`; later `CreateUsage` / `ListUsages`; inspect with `ListSubscriptionComponents` / `ReadSubscriptionComponent`. Event-based ingest (`RecordEvent` / Ebb server) is **out of scope**.
12. **Error boundary** — wrap every call; Case A vs Case B per operation row below; also catch `JsonException` (see trap notes).
13. **Tests** — fake at the `HttpClient` constructor argument.

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

Throw-only SDK: **no** `{Operation}Result` / no-throw variants. Every operation throws `MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>`. Case A `TError` is `MaxioAdvancedBilling.Errors.{Operation}Error` (`: MaxioAdvancedBilling.Core.ErrorResponse.ApiError`) with status `TryGet…` accessors plus inherited `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)`. Case B `TError` is `RawError`: `StatusCode: HttpStatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. (`sdk-map.md`)

Records live in `MaxioAdvancedBilling.Models`. Enums in `MaxioAdvancedBilling.Models.Enums` (`StringEnum<T>` — **not** C# enums; use `Type.Member` or `Type.FromValue("wire")`). Unions in `MaxioAdvancedBilling.Models.AnyOf` / `.OneOf`. Controllers in `MaxioAdvancedBilling.Api` (accessed as `client.{Group}`).

### Client construction / auth / server (`sdk-map.md`)

| Fact | Value |
|---|---|
| Client | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` — **only** ctor `MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)` |
| Options | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions`: `Environment` (`MaxioAdvancedBilling.Servers.ServerEnvironment`), `Retry` (`MaxioAdvancedBilling.Core.Configuration.RetryOptions`), `Server`, `BasicAuth` (`MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?`) |
| DI | `services.AddMaxioAdvancedBillingClient(o => { … })` (`ServiceCollectionExtensions.cs`) |
| Auth | HTTP Basic only. `BasicAuthCredentials { Username = "<api_key>", Password = "x" }` — username **is** the API key; password **is** the literal `"x"`. (`Core/Authentication/Basic/BasicAuthCredentials.cs`) |
| Environment | `MaxioAdvancedBilling.Servers.ServerEnvironment` is **US vs EU hosting**, not sandbox vs live. Members: `ServerEnvironment.Us` (default, wire `"US"`) → template `https://{site}.chargify.com`; `ServerEnvironment.Eu` (wire `"EU"`) → `https://{site}.ebilling.maxio.com`. **No sandbox member.** Map 2-char env (`MAXIO_ENVIRONMENT`): `"US"` → `Us`, `"EU"` → `Eu`; omit/unknown → `Us` (`ServerEnvironment.Default()`). Sandbox sites still use `Us` unless the account requested EU hosting. (`sdk-map.md`, `Servers/ServerEnvironment.cs`) |
| Config → options | `Maxio:ApiKey` → `options.BasicAuth.Username` (password literal `"x"`). `Maxio:Subdomain` → `options.Server.Production.Us.Site` when `Environment` is `Us`, else `options.Server.Production.Eu.Site`. **`Maxio:BaseUrl` (optional verbatim API base):** when set, assign the string to `options.Server.Production.Us.BaseUrl` (`MaxioAdvancedBilling.Servers.ProductionOptions.UsOptions.BaseUrl`, default `"https://{site}.chargify.com"`) or `.Eu.BaseUrl` (default `"https://{site}.ebilling.maxio.com"`) matching `Environment` — do **not** also derive the host from subdomain. When unset, leave `BaseUrl` at the template and set `Site` from `Maxio:Subdomain`. `Maxio:ProductFamilyHandle` is **not** a client option; pass it as `ListProductsForProductFamily` `productFamilyId` with the `handle:` prefix. Nested types: `MaxioAdvancedBilling.ServerOptions.Production` (`ServerOptions.cs` at root ns) → `MaxioAdvancedBilling.Servers.ProductionOptions`. (`sdk-map.md`, `Servers/ProductionOptions.cs`) |
| Site / BaseUrl paths | Exact C# paths: `options.Server.Production.Us.Site`, `options.Server.Production.Us.BaseUrl`, `options.Server.Production.Eu.Site`, `options.Server.Production.Eu.BaseUrl`. Hero ops use **Production**, not Ebb. |
| Retry | `RetryOptions` (`MaxioAdvancedBilling.Core.Configuration`): all members `required` — build a full instance or start from `RetryOptions.Default()`. Members: `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry`. |

### Operations

#### Step 2 — Customers (`client.Customers`, `operations/Customers.md`)

| Op | Signature | Request | Response envelope | Error | Pagination |
|---|---|---|---|---|---|
| **CreateCustomer** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, **must pass explicitly** | Envelope `CreateCustomerRequest`: `Customer (customer): CreateCustomer !req`. C# **`required` only**: `FirstName (first_name): string`, `LastName (last_name): string`, `Email (email): string`. Also set **`Reference (reference): string?`** = eShop buyer/user id (not `required` in C#, but unique-if-present: “you may only create one customer for a given reference value”). No other C# `required` members. Runtime-if-sent (not required): `Country (country)` ISO-3166-1 alpha-2; `State (state)` ISO if sent. Do **not** send address/country unless you have ISO values. (`records-1-Ac-Cr.md`, `operations/Customers.md`) | `CustomerResponse` → **`.Customer`** (`Customer !req`). Read: `Id (id)`, `Reference (reference)`, `Email (email)`, `FirstName (first_name)`, `LastName (last_name)`. (`records-2-Cr-Ne.md`) | **Case A** `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` **[422]** · `TryGetRawError(out RawError)`. Duplicate `reference` surfaces as 422 — treat as “already exists” then re-read by reference. Payload `CustomerErrorResponse1.Errors (errors): Errors?` (`records-2-Cr-Ne.md`) | none |
| **ReadCustomerByReference** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` · query `reference` ← `reference` | `reference` = buyer/user id (same string stored on create) | `CustomerResponse` → `.Customer` | **Case B** `SdkException<RawError>` — **not** a typed `{Op}Error`. Catch `SdkException<RawError>` only. **404 / not found:** `ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound` (then create). **Do not** treat any other status (401/403/422/5xx) as miss — those are `StatusCode` ≠ `NotFound`; read body with `ex.Error.ReadAsString()`. Accessors: `StatusCode: HttpStatusCode` · `ReadAsString()` · `ReadAsJson<T>()` · `ReadAsBytes()`. (`operations/Customers.md`, `sdk-map.md`) | none |
| **ReadCustomer** | `ReadCustomer(int id, CancellationToken ct = default)` | — | `CustomerResponse` → `.Customer` | **Case B** `SdkException<RawError>` | none |
| **ListCustomers** | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — 7 params `direction`…`q` nullable **must pass explicitly** (`null` to skip). Query: `direction`, `page`, `per_page`←`perPage`, `date_field`←`dateField`, `start_date`, `end_date`, `start_datetime`, `end_datetime`, `q` | `q` searches email / id / organization / reference / name | `IReadOnlyList<CustomerResponse>` — each `.Customer` | **Case B** `SdkException<RawError>` | manual `page`+`perPage` |
| **ListCustomerSubscriptions** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | **No** product/state query params — filter in process. Nested `Subscription.Product.Handle` + `Subscription.State`. Sufficient as a second idempotency check (or listing UI) if you exclude EOL states below; **`FindSubscription(reference)` is the precise same-plan lookup.** | `IReadOnlyList<SubscriptionResponse>` — each `.Subscription` (`Subscription?`). Product handle: `.Subscription.Product.Handle`. State: `.Subscription.State` (`SubscriptionState?`). | **Case B** `SdkException<RawError>` | none |
| **UpdateCustomer** | `UpdateCustomer(int id, UpdateCustomerRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | Envelope `UpdateCustomerRequest`: `Customer (customer): UpdateCustomer !req`. All inner fields optional: `FirstName`, `LastName`, `Email`, `CcEmails`, `Organization`, `Reference`, `Address`, `Address2`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `VatNumber`, `TaxExempt`, `TaxExemptReason`, `ParentId`, `Verified`, `SalesforceId`. (`records-4-Su-We.md`) | `CustomerResponse` → `.Customer` | **Case A** `SdkException<UpdateCustomerError>`: `TryGetNoContent(out RawError)` **[404]** · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` **[422]** · `TryGetRawError` | none |

`DeleteCustomer` is out of the eShop buyer flow (destroys the Maxio customer).

#### Step 3 — Catalog (`operations/ProductFamilies.md`, `Products.md`, `ProductPricePoints.md`, `Components.md`)

| Op | Signature | Request | Response envelope | Error | Pagination |
|---|---|---|---|---|---|
| **ListProductFamilies** | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — 5 params **must pass explicitly** | — | `IReadOnlyList<ProductFamilyResponse>` → each `.ProductFamily` (`ProductFamily?`): `Id (id)`, `Name (name)`, `Handle (handle)`, `AccountingCode (accounting_code)`, `Description (description)`, `ArchivedAt (archived_at)`. (`records-3-Of-Su.md`) | **Case B** `SdkException<RawError>` | none |
| **ReadProductFamily** | `ReadProductFamily(int id, CancellationToken ct = default)` — notes: path also accepts `handle:my-family` at the HTTP layer; C# param is `int id` | — | `ProductFamilyResponse` → `.ProductFamily` | **Case B** | none |
| **CreateProductFamily** *(optional admin)* | `CreateProductFamily(CreateProductFamilyRequest? body, CancellationToken ct = default)` — `body` **must pass** | Envelope: `ProductFamily (product_family): CreateProductFamily !req`. Inner: `Name (name): string !req`, `Handle (handle): string?`, `Description (description): string?`. (`records-1-Ac-Cr.md`) | `ProductFamilyResponse` → `.ProductFamily` | **Case A** `SdkException<CreateProductFamilyError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` **[422]** · `TryGetRawError`. Payload `ErrorListResponse1.Errors (errors): IReadOnlyList<string> !req` (`records-2-Cr-Ne.md`) | none |
| **ListProducts** | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 params `dateField`…`include` **must pass explicitly**. Query: `date_field`, `filter`, `end_date`, `end_datetime`, `start_date`, `start_datetime`, `page`, `per_page`, `include_archived`, `include` | Filter `ListProductsFilter`: `Ids (ids): IReadOnlyList<int>?`, `PrepaidProductPricePoint (prepaid_product_price_point): PrepaidProductPricePointFilter?`, `UseSiteExchangeRate (use_site_exchange_rate): bool?` (`records-2-Cr-Ne.md`) | `IReadOnlyList<ProductResponse>` → each **`.Product`** (`Product !req`). Read: `Id`, `Name`, `Handle`, `PriceInCents (price_in_cents)`, `Interval`, `IntervalUnit`, `TrialPriceInCents`, `RequireCreditCard (require_credit_card)`, `ProductFamily (product_family)`, `DefaultProductPricePointId (default_product_price_point_id)`, `ProductPricePointId`, `ProductPricePointHandle`, `ArchivedAt`. (`records-3-Of-Su.md`) | **Case B** | manual `page`+`perPage` |
| **ListProductsForProductFamily** | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 params after id **must pass explicitly** (`null` to skip) | **Handle form (required for unstable numeric ids):** `productFamilyId` is **not** the bare handle. XML param: “Either the product family's id or its handle prefixed with `handle:`”. Pass `"handle:eshop-subscribe"` (or `"handle:" + Maxio:ProductFamilyHandle`). Bare `"eshop-subscribe"` is **not** the documented form. **Include/archived for plan list:** `Handle`/`Name`/`PriceInCents`/`Interval`/`IntervalUnit`/`RequireCreditCard` are default `Product` fields — **no** `include` needed. Pass `include: null`. `ListProductsInclude` has only `PrepaidProductPricePoint (prepaid_product_price_point)` (not these fields). Pass `includeArchived: false` (or `null`) so archived products are omitted. Other must-pass params (`dateField`…`endDatetime`, `filter`): `null`. (`operations/ProductFamilies.md`, `Api/ProductFamilies.cs` param doc, `records-3-Of-Su.md`, `enums.md`) | `IReadOnlyList<ProductResponse>` → each **`.Product`**. Read: `Handle (handle)`, `Name (name)`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `RequireCreditCard (require_credit_card): bool?` (“Boolean that controls whether a payment profile is required…”). Filter client-side to `eshop-pro` / `basic-plan`. | **Case A** `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` **[404]** · `TryGetRawError` | manual `page`+`perPage` |
| **ReadProduct** | `ReadProduct(int productId, CancellationToken ct = default)` | — | `ProductResponse` → `.Product` | **Case B** | none |
| **ReadProductByHandle** | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | — | `ProductResponse` → `.Product` | **Case B** | none |
| **CreateProduct** *(optional admin)* | `CreateProduct(string productFamilyId, CreateOrUpdateProductRequest? body, CancellationToken ct = default)` — `body` **must pass** | Envelope: `Product (product): CreateOrUpdateProduct !req`. Inner **required**: `Name (name)`, `Description (description)`, `PriceInCents (price_in_cents): long`, `Interval (interval): int`, `IntervalUnit (interval_unit): IntervalUnit`. Optional: `Handle`, `AccountingCode`, `RequireCreditCard`, trial/expiration fields, `TaxCode`. (`records-1-Ac-Cr.md`) | `ProductResponse` → `.Product` | **Case A** `SdkException<CreateProductError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` **[422]** · `TryGetRawError` | none |
| **ListProductPricePoints** | `ListProductPricePoints(ProductIdModel productId, bool? currencyPrices, IReadOnlyList<PricePointType>? filterType, bool? archived, int? page = 1, int? perPage = 10, CancellationToken ct = default)` — `currencyPrices`, `filterType`, `archived` **must pass explicitly**. Query: `page`, `per_page`, `currency_prices`, `filter[type]`←`filterType`, `archived` | Path `productId`: union `MaxioAdvancedBilling.Models.AnyOf.ProductIdModel` — factories `ProductIdModel.Int(int)` / `ProductIdModel.String(string)`; `TryGetInt` / `TryGetString`; implicit from `int`/`string`. (`unions.md`) | `ListProductPricePointsResponse`: `PricePoints (price_points): IReadOnlyList<ProductPricePoint> !req` (**no extra envelope**). Read each: `Id`, `Name`, `Handle`, `PriceInCents`, `Interval`, `IntervalUnit`, `Type (type): PricePointType?`, `ProductId`, `TrialPriceInCents`. (`records-2-Cr-Ne.md`, `records-3-Of-Su.md`) | **Case B** | manual `page`+`perPage` |
| **ReadProductPricePoint** | `ReadProductPricePoint(ProductIdModel productId, PricePointIdModel pricePointId, bool? currencyPrices, CancellationToken ct = default)` — `currencyPrices` **must pass**. Query `currency_prices` | `pricePointId`: `MaxioAdvancedBilling.Models.AnyOf.PricePointIdModel` — `PricePointIdModel.Int(int)` / `.String(string)`. (`unions.md`) | `ProductPricePointResponse` → **`.PricePoint`** (`ProductPricePoint !req`) | **Case B** | none |
| **CreateProductPricePoint** *(optional admin)* | `CreateProductPricePoint(ProductIdModel productId, CreateProductPricePointRequest? body, CancellationToken ct = default)` — `body` **must pass** | Envelope: `PricePoint (price_point): CreateProductPricePoint !req`. Inner **required**: `Name`, `PriceInCents: long`, `Interval: int`, `IntervalUnit: IntervalUnit`. Optional: `Handle`, trial/initial/expiration, `UseSiteExchangeRate` default `true`. (`records-1-Ac-Cr.md`) | `ProductPricePointResponse` → `.PricePoint` | **Case A** `SdkException<CreateProductPricePointError>`: `TryGetProductPricePointErrorResponse1(out ProductPricePointErrorResponse1)` **[422]** · `TryGetRawError`. Payload `Errors (errors): ProductPricePointErrors !req` (`records-3-Of-Su.md`) | none |
| **ListComponents** | `ListComponents(BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, bool? includeArchived, ListComponentsFilter? filter, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 7 params **must pass explicitly** | Filter: `Ids (ids)`, `UseSiteExchangeRate (use_site_exchange_rate)` (`records-2-Cr-Ne.md`) | `IReadOnlyList<ComponentResponse>` → each **`.Component`** (`Component !req`). Read: `Id`, `Name`, `Handle`, `Kind (kind): ComponentKind?`, `UnitName`, `PricingScheme`, `ProductFamilyId`, `DefaultPricePointId`, `Recurring`. (`records-1-Ac-Cr.md`) | **Case B** | manual `page`+`perPage` |
| **ListComponentsForProductFamily** | `ListComponentsForProductFamily(int productFamilyId, bool? includeArchived, ListComponentsFilter? filter, BasicDateField? dateField, string? endDate, string? endDatetime, string? startDate, string? startDatetime, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 7 params after id **must pass** | — | `IReadOnlyList<ComponentResponse>` → `.Component` | **Case B** | manual `page`+`perPage` |
| **FindComponent** | `FindComponent(string handle, CancellationToken ct = default)` · query `handle` | — | `ComponentResponse` → `.Component` | **Case B** | none |
| **ReadComponent** | `ReadComponent(int productFamilyId, string componentId, CancellationToken ct = default)` — `componentId` may be numeric id or `handle:…` | — | `ComponentResponse` → `.Component` | **Case B** | none |

Creating component **definitions** (`CreateMeteredComponent`, quantity/on-off/prepaid/ebb) is catalog-admin, not the buyer subscribe path. If a metered add-on must be provisioned: `CreateMeteredComponent(string productFamilyId, CreateMeteredComponent? body, ct)` — envelope `MeteredComponent (metered_component): MeteredComponent !req`; inner required `Name`, `UnitName`, `PricingScheme`; returns `ComponentResponse`; **Case A** `CreateMeteredComponentError`: `TryGetNoContent` **[404]** · `TryGetErrorListResponse1` **[422]** (`operations/Components.md`, `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md`).

#### Step 4 — Payment profiles (`client.PaymentProfiles`, `operations/PaymentProfiles.md`)

| Op | Signature | Request | Response envelope | Error | Pagination |
|---|---|---|---|---|---|
| **CreatePaymentProfile** | `CreatePaymentProfile(CreatePaymentProfileRequest? body, CancellationToken ct = default)` — `body` **must pass** | Envelope: `PaymentProfile (payment_profile): CreatePaymentProfile !req`. Prefer `ChargifyToken (chargify_token)` + `CustomerId (customer_id)`. Also: `PaymentType`, `FirstName`, `LastName`, billing address fields, bank fields. Do **not** send `FullNumber`/`Cvv` in production unless PCI-compliant. (`records-1-Ac-Cr.md`) | `PaymentProfileResponse` → **`.PaymentProfile`** — union `MaxioAdvancedBilling.Models.OneOf.PaymentProfile`. Read with `TryGetCreditCardPaymentProfile(out CreditCardPaymentProfile)`, `TryGetBankAccountPaymentProfile`, `TryGetPaypalPaymentProfile`, `TryGetApplePayPaymentProfile`. Factories `PaymentProfile.CreditCardPaymentProfile(…)`. Implicit from each variant. (`unions.md`, `records-3-Of-Su.md`) Credit-card variant fields to read: `Id`, `MaskedCardNumber`, `CardType`, `ExpirationMonth`, `ExpirationYear`, `CustomerId`, `PaymentType` (default `PaymentType.CreditCard`). (`records-2-Cr-Ne.md`) | **Case A** `SdkException<CreatePaymentProfileError>`: `TryGetNoContent` **[404]** · `TryGetErrorListResponse1(out ErrorListResponse1)` **[422]** · `TryGetRawError` | none |
| **ListPaymentProfiles** | `ListPaymentProfiles(int? customerId, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — `customerId` **must pass explicitly**. Query `page`, `per_page`, `customer_id` | Pass eShop Maxio customer id | `IReadOnlyList<PaymentProfileResponse>` → each `.PaymentProfile` (union) | **Case B** | manual `page`+`perPage` |
| **ReadPaymentProfile** | `ReadPaymentProfile(int paymentProfileId, CancellationToken ct = default)` | — | `PaymentProfileResponse` → `.PaymentProfile` (union) | **Case A** `SdkException<ReadPaymentProfileError>`: `TryGetNoContent` **[404]** · `TryGetRawError` | none |

**Hero: skip this controller entirely.** No-card subscribe omits every payment-profile member.

#### Step 5 — Subscribe (`client.Subscriptions`, `operations/Subscriptions.md`)

| Op | Signature | Request | Response envelope | Error | Pagination |
|---|---|---|---|---|---|
| **PreviewSubscription** | `PreviewSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` **must pass** | Same envelope as create | `SubscriptionPreviewResponse` → **`.SubscriptionPreview`** (`SubscriptionPreview !req`): `CurrentBillingManifest (current_billing_manifest): BillingManifest?`, `NextBillingManifest (next_billing_manifest): BillingManifest?`. Manifest totals: `TotalInCents`, `SubtotalInCents`, `TotalTaxInCents`, `TotalDiscountInCents`, `StartDate`, `EndDate`, `LineItems`. (`records-4-Su-We.md`, `records-1-Ac-Cr.md`) | **Case B** `SdkException<RawError>` | none |
| **CreateSubscription** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` **must pass** | Envelope: `Subscription (subscription): CreateSubscription !req`. **Hero no-card body (all payment members omitted):** `ProductHandle (product_handle): string?` = `"eshop-pro"` or `"basic-plan"` (source: required unless `product_id`; prefer handle). Customer: `CustomerId (customer_id): int?` **or** `CustomerReference (customer_reference): string?` (source: one of `customer_id` / `customer_reference` / `customer_attributes` required). Idempotency: `Reference (reference): string?` = `buyerId + ":" + productHandle` (source: “The reference value (provided by your app) for the subscription itself.”). **No payment fields are C# `required`.** Omit `PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes`, and any Chargify.js token. **`PaymentCollectionMethod` is not required** for no-card: `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` is optional; the map/source do **not** mandate a value when `Product.RequireCreditCard` is false. Do **not** set `CollectionMethod.Automatic` to “make subscribe work.” If you set a method anyway (RI architecture, no card): `CollectionMethod.Remittance` (wire `remittance`); legacy statements: `CollectionMethod.Invoice` (wire `invoice`). `AgreementAcceptance` is only “Required when creating a subscription with Maxio Payments” — omit for no-card. Notes: “Payment information may be required … depending on the options for the Product”; product flag is `RequireCreditCard (require_credit_card)`. 3DS notes apply only “When a payment requires 3DS” — no payment payload ⇒ no 3DS flow. (`records-2-Cr-Ne.md`, `operations/Subscriptions.md`, `Models/CreateSubscription.cs`) | `SubscriptionResponse` → **`.Subscription`**. Read: `Id`, `State`, `Reference`, `Product.Handle`, `Customer.Id`. | **Case A** `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` **[422]** · `TryGetRawError`. Duplicate `reference` / validation → 422 strings in `ErrorListResponse1.Errors`. | none |

**Hero subscribe idempotency — `Subscription.State` (`enums.md` + `Models/Enums/SubscriptionState.cs` XML groups):**

| Bucket | Members (`CSharp (wire)`) | Double-click |
|---|---|---|
| Live (enrolled) | `Pending (pending)`, `Assessing (assessing)`, `Trialing (trialing)`, `Active (active)`, `Paused (paused)` | treat as **already enrolled** — do not create another |
| Problem (enrolled) | `PastDue (past_due)`, `SoftFailure (soft_failure)`, `Unpaid (unpaid)` | **already enrolled** |
| End of life — canceled/expired/failed | `Canceled (canceled)`, `Expired (expired)`, `FailedToCreate (failed_to_create)`, `TrialEnded (trial_ended)` | **not enrolled** — a new subscribe for the same plan is allowed (new `FindSubscription` miss if you also change `Reference`, or reuse reference only after the old row is gone) |
| End of life — still a current sub | `OnHold (on_hold)`, `Suspended (suspended)` | **already enrolled** for double-click (do not create a second) |
| Unclassified in the XML groups | `AwaitingSignup (awaiting_signup)` | **already enrolled** (signup in flight; likely for no-card products) |

List-and-filter is sufficient **if** you match `Subscription.Product.Handle` to the plan **and** exclude `{Canceled, Expired, FailedToCreate, TrialEnded}`. It is **not** as precise as `FindSubscription(buyerId + ":" + productHandle)` (no extra product-id filter; `ListSubscriptions.product` is numeric id only — “the product handle cannot be used”).

`ComponentId1` factories: `ComponentId1.Int(int)`, `ComponentId1.String(string)` (`unions.md`). `PricePointId2`: `PricePointId2.Int` / `.String`.

#### Step 6 — Read / list subscriptions (`operations/Subscriptions.md`, `Customers.md`)

| Op | Signature | Request | Response envelope | Error | Pagination |
|---|---|---|---|---|---|
| **ReadSubscription** | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` **must pass explicitly**. Query `include` | `SubscriptionInclude.Coupons` / `SubscriptionInclude.SelfServicePageToken` | `SubscriptionResponse` → `.Subscription` | **Case B** `SdkException<RawError>` | none |
| **FindSubscription** | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` **must pass**. Query `reference` ← `reference` | Looks up **`CreateSubscription.Reference`** (same wire `reference`) — **not** customer reference. Hero format `buyerId + ":" + productHandle` is an app string; the field is unconstrained. Param XML: “Subscription reference”. Remarks: “Finds a subscription by its reference.” (`operations/Subscriptions.md`, `Models/CreateSubscription.cs`) | `SubscriptionResponse` → `.Subscription` | **Case A** `SdkException<FindSubscriptionError>`: **`TryGetNoContent(out RawError)` [404]** — that is the miss path (`true` ⇒ not found; do **not** also require `StatusCode` checks on other exception types). Other statuses: `TryGetNoContent` is `false`; use `TryGetRawError(out RawError)` and read `raw.StatusCode` — **not** a miss. (`operations/Subscriptions.md`, `Errors/FindSubscriptionError.cs`) | none |
| **ListSubscriptions** | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 14 params `state`…`include` **must pass explicitly** | Prefer `ListCustomerSubscriptions` for a buyer; use this for site-wide admin | `IReadOnlyList<SubscriptionResponse>` → each `.Subscription` | **Case B** | manual `page`+`perPage` |
| **ListCustomerSubscriptions** | see Customers table (hero) | client-side filter by `Product.Handle` + non-EOL `State`; no server-side product/state args | `IReadOnlyList<SubscriptionResponse>` | **Case B** | none |

#### Step 7 — Plan change (`operations/Subscriptions.md`, `SubscriptionProducts.md`)

| Op | Signature | Request | Response envelope | Error | Pagination |
|---|---|---|---|---|---|
| **UpdateSubscription** (next-period product change) | `UpdateSubscription(int subscriptionId, UpdateSubscriptionRequest? body, CancellationToken ct = default)` — `body` **must pass** | Envelope: `Subscription (subscription): UpdateSubscription !req`. Product change: `ProductHandle` / `ProductId`; delayed: `ProductChangeDelayed (product_change_delayed): bool?`; price point: `ProductPricePointId` / `ProductPricePointHandle`; cancel delayed change: `NextProductId (next_product_id): string?` (empty string). Also `NextBillingAt`, `PaymentCollectionMethod (payment_collection_method): string?` (wire string, **not** the enum on this model). (`records-4-Su-We.md`) | `SubscriptionResponse` → `.Subscription` | **Case A** `SdkException<UpdateSubscriptionError>`: `TryGetErrorListResponse1` **[422]** · `TryGetRawError` | none |
| **PreviewSubscriptionProductMigration** | `PreviewSubscriptionProductMigration(int subscriptionId, SubscriptionMigrationPreviewRequest? body, CancellationToken ct = default)` — `body` **must pass** | Envelope: `Migration (migration): SubscriptionMigrationPreviewOptions !req`. Fields: `ProductId`, `ProductHandle`, `ProductPricePointId`, `ProductPricePointHandle`, `IncludeTrial` default `false`, `IncludeInitialCharge` default `false`, `IncludeCoupons` default `true`, `PreservePeriod` default `false`, `Proration (proration): Proration?` (`PreservePeriod (preserve_period): bool?`), `ProrationDate (proration_date): DateTimeOffset?`. (`records-4-Su-We.md`) | `SubscriptionMigrationPreviewResponse` → **`.Migration`**: `ProratedAdjustmentInCents`, `ChargeInCents`, `PaymentDueInCents`, `CreditAppliedInCents` | **Case A** `SdkException<PreviewSubscriptionProductMigrationError>`: `TryGetErrorListResponse1` **[422]** · `TryGetRawError` | none |
| **MigrateSubscriptionProduct** | `MigrateSubscriptionProduct(int subscriptionId, SubscriptionProductMigrationRequest? body, CancellationToken ct = default)` — `body` **must pass** | Envelope: `Migration (migration): SubscriptionProductMigration !req`. Same product/price-point/include/preserve/proration fields as preview **except no `ProrationDate`**. Sub must be `active` or `trialing`. (`records-4-Su-We.md`) | `SubscriptionResponse` → `.Subscription` | **Case A** `SdkException<MigrateSubscriptionProductError>`: `TryGetErrorListResponse1` **[422]** · `TryGetRawError` | none |

Use **UpdateSubscription** when the new price should apply at next period (no proration). Use **MigrateSubscriptionProduct** for immediate/prorated moves.

#### Step 8 — Cancel (`client.SubscriptionStatus`, `operations/SubscriptionStatus.md`)

| Op | Signature | Request | Response envelope | Error | Pagination |
|---|---|---|---|---|---|
| **CancelSubscription** (immediate) | `CancelSubscription(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** (pass `null` for immediate with no options) | Envelope: `Subscription (subscription): CancellationOptions !req`. Options: `CancellationMessage (cancellation_message)`, `ReasonCode (reason_code)`, `CancelAtEndOfPeriod (cancel_at_end_of_period)`, `ScheduledCancellationAt`, `RefundPrepaymentAccountBalance`. (`records-1-Ac-Cr.md`) | `SubscriptionResponse` → `.Subscription` (`State` → canceled) | **Case A** `SdkException<CancelSubscriptionApiError>`: `TryGetNoContent` **[404]** · `TryGetCancelSubscriptionErrorResponse(out CancelSubscriptionErrorResponse)` **[422]** · `TryGetRawError`. `CancelSubscriptionErrorResponse` is a **union** (`MaxioAdvancedBilling.Models.AnyOf`): `TryGetErrorListResponse1(out ErrorListResponse1)` / `TryGetSingleErrorResponse1(out SingleErrorResponse1)` (`SingleErrorResponse1.Error (error): string !req`). Factories `CancelSubscriptionErrorResponse.ErrorListResponse1(…)` / `.SingleErrorResponse1(…)`. (`unions.md`, `records-3-Of-Su.md`) | none |
| **InitiateDelayedCancellation** | `InitiateDelayedCancellation(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` — `body` **must pass** | same `CancellationRequest` | `DelayedCancellationResponse`: `Message (message): string?` (**not** a subscription envelope) | **Case A** `SdkException<InitiateDelayedCancellationError>`: `TryGetNoContent` **[404]** · `TryGetErrorListResponse1` **[422]** · `TryGetRawError` | none |
| **CancelDelayedCancellation** | `CancelDelayedCancellation(int subscriptionId, CancellationToken ct = default)` | — | `DelayedCancellationResponse` | **Case A** `SdkException<CancelDelayedCancellationError>`: `TryGetNoContent` **[404]** · `TryGetRawError` | none |

#### Step 9 — Coupons (`operations/Subscriptions.md`, `Coupons.md`)

Apply-on-subscribe path is **`CreateSubscription.CouponCode` / `CouponCodes`** (no extra coupon API call). Remaining ops:

| Op | Signature | Request | Response envelope | Error | Pagination |
|---|---|---|---|---|---|
| **ValidateCoupon** | `ValidateCoupon(string code, int? productFamilyId, CancellationToken ct = default)` — `productFamilyId` **must pass**. Query `code`, `product_family_id` | Pass family id when the coupon is not on the site’s first family | `CouponResponse` → **`.Coupon`** (`Coupon?`). Read: `Id`, `Name`, `Code`, `Percentage`, `AmountInCents`, `Recurring`, `Stackable`, `DiscountType`, `EndDate`, `ProductFamilyId`. (`records-1-Ac-Cr.md`) | **Case A** `SdkException<ValidateCouponError>`: `TryGetSingleStringErrorResponse1(out SingleStringErrorResponse1)` **[404]** · `TryGetRawError`. Payload `Errors (errors): string?` (`records-3-Of-Su.md`) | none |
| **FindCoupon** | `FindCoupon(int? productFamilyId, string? code, bool? currencyPrices, CancellationToken ct = default)` — all three **must pass**. Query `product_family_id`, `code`, `currency_prices` | — | `CouponResponse` → `.Coupon` | **Case B** (404 if missing) | none |
| **ApplyCouponsToSubscription** | `ApplyCouponsToSubscription(int subscriptionId, string? code, AddCouponsRequest? body, CancellationToken ct = default)` — `code` and `body` **must pass**. Query `code` is **deprecated** (replaces all codes). Prefer body | Body `AddCouponsRequest`: `Codes (codes): IReadOnlyList<string>?` — **adds** to existing codes (`records-1-Ac-Cr.md`) | `SubscriptionResponse` → `.Subscription` | **Case A** `SdkException<ApplyCouponsToSubscriptionError>`: `TryGetSubscriptionAddCouponError1(out SubscriptionAddCouponError1)` **[422]** · `TryGetRawError`. Payload fields: `Codes`, `CouponCode`, `CouponCodes`, `Subscription` — each `IReadOnlyList<string>?` (`records-3-Of-Su.md`) | none |
| **RemoveCouponFromSubscription** | `RemoveCouponFromSubscription(int subscriptionId, string? couponCode, CancellationToken ct = default)` — `couponCode` **must pass**. Query `coupon_code` | — | `string` (not an envelope) | **Case A** `SdkException<RemoveCouponFromSubscriptionError>`: `TryGetSubscriptionRemoveCouponErrors1(out SubscriptionRemoveCouponErrors1)` **[422]** · `TryGetRawError`. Payload `Subscription (subscription): IReadOnlyList<string> !req` (`records-4-Su-We.md`) | none |

Catalog coupon CRUD (`CreateCoupon` / `ListCoupons`) is admin-only; not required for checkout if codes already exist in Maxio.

#### Step 10 — Invoices (`client.Invoices`, `operations/Invoices.md`)

| Op | Signature | Request | Response envelope | Error | Pagination |
|---|---|---|---|---|---|
| **ListInvoices** | `ListInvoices(string? startDate, string? endDate, InvoiceStatus? status, int? subscriptionId, string? subscriptionGroupUid, string? consolidationLevel, Direction? direction, InvoiceDateField? dateField, string? startDatetime, string? endDatetime, IReadOnlyList<int>? customerIds, IReadOnlyList<string>? number, IReadOnlyList<int>? productIds, InvoiceSortField? sort, int? page = 1, int? perPage = 20, bool? lineItems = false, bool? discounts = false, bool? taxes = false, bool? credits = false, bool? payments = false, bool? customFields = false, bool? refunds = false, CancellationToken ct = default)` — 14 params `startDate`…`sort` **must pass explicitly**. Breakdown flags default `false` (index returns totals only unless set `true`). Query: `start_date`, `end_date`, `status`, `subscription_id`, `subscription_group_uid`, `consolidation_level`, `page`, `per_page`, `direction`, `line_items`, `discounts`, `taxes`, `credits`, `payments`, `custom_fields`, `refunds`, `date_field`, `start_datetime`, `end_datetime`, `customer_ids`, `number`, `product_ids`, `sort` | Filter by `subscriptionId` **or** `customerIds` | `ListInvoicesResponse`: `Invoices (invoices): IReadOnlyList<Invoice> !req` (**Invoice is the item — no per-item envelope**). Read: `Uid (uid)`, `Number (number)`, `Status (status): InvoiceStatus?`, `SubscriptionId`, `CustomerId`, `IssueDate`, `DueDate`, `PaidDate`, `TotalAmount (total_amount): string?`, `DueAmount`, `PaidAmount`, `SubtotalAmount`, `DiscountAmount`, `TaxAmount`, `Currency`, `CollectionMethod`, `ProductName`, `LineItems` (only if `lineItems: true`). (`records-2-Cr-Ne.md`) | **Case B** `SdkException<RawError>` | manual `page`+`perPage` |
| **ReadInvoice** | `ReadInvoice(string uid, CancellationToken ct = default)` | invoice **uid** (string, not numeric `Id`) | `Invoice` **directly** (not `InvoiceResponse`) | **Case B** | none |

`InvoiceResponse` (`Invoice (invoice): Invoice !req`) is the envelope for **CreateInvoice** only — do not unwrap `ReadInvoice`/`ListInvoices` items a second time. Ad-hoc `CreateInvoice` / payments / void / refund are out of the typical eShop list/read path.

#### Step 11 — Usage / metered (`client.SubscriptionComponents`, `operations/SubscriptionComponents.md`)

Standard subscribe+invoice path: pass components on `CreateSubscription`; record metered usage with `CreateUsage` (quantity accumulates to `unit_balance` and bills at period end).

| Op | Signature | Request | Response envelope | Error | Pagination |
|---|---|---|---|---|---|
| **CreateUsage** | `CreateUsage(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, CreateUsageRequest? body, CancellationToken ct = default)` — `body` **must pass** | Path unions: `MaxioAdvancedBilling.Models.AnyOf.SubscriptionIdOrReference` — `SubscriptionIdOrReference.Int(int)` / `.String(string)`; `ComponentIdModel.Int(int)` / `.String(string)` (handle as string). Envelope: `Usage (usage): CreateUsage !req`. Inner: `Quantity (quantity): double?` (negative deducts; floor 0), `PricePointId (price_point_id): string?`, `Memo (memo): string?`, `BillingSchedule`, `CustomPrice`. (`records-2-Cr-Ne.md`, `unions.md`) | `UsageResponse` → **`.Usage`** (`Usage !req`): `Id`, `Quantity (quantity): Quantity1?` (union int/string — `TryGetInt`/`TryGetString`), `ComponentId`, `ComponentHandle`, `SubscriptionId`, `Memo`, `CreatedAt`. (`records-4-Su-We.md`) | **Case A** `SdkException<CreateUsageError>`: `TryGetErrorListResponse1` **[422]** · `TryGetRawError` | none |
| **ListUsages** | `ListUsages(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, long? sinceId, long? maxId, DateTimeOffset? sinceDate, DateTimeOffset? untilDate, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 4 params `sinceId`…`untilDate` **must pass** | not for quantity-based components | `IReadOnlyList<UsageResponse>` → each `.Usage` | **Case B** | manual `page`+`perPage` |
| **ListSubscriptionComponents** | `ListSubscriptionComponents(int subscriptionId, SubscriptionListDateField? dateField, SortingDirection? direction, ListSubscriptionComponentsFilter? filter, string? endDate, string? endDatetime, IncludeNotNull? pricePointIds, IReadOnlyList<int>? productFamilyIds, ListSubscriptionComponentsSort? sort, string? startDate, string? startDatetime, IReadOnlyList<ListSubscriptionComponentsInclude>? include, bool? inUse, CancellationToken ct = default)` — 12 params **must pass explicitly** | — | `IReadOnlyList<SubscriptionComponentResponse>` → each `.Component` (`SubscriptionComponent?`). Read: `ComponentId`, `ComponentHandle`, `Kind`, `Enabled`, `UnitBalance`, `AllocatedQuantity` (union `AllocatedQuantity2`), `PricePointId`. (`records-3-Of-Su.md`) | **Case B** | none |
| **ReadSubscriptionComponent** | `ReadSubscriptionComponent(int subscriptionId, int componentId, CancellationToken ct = default)` | — | `SubscriptionComponentResponse` → `.Component` | **Case A** `SdkException<ReadSubscriptionComponentError>`: `TryGetNoContent` **[404]** · `TryGetRawError` | none |

`AllocateComponent` is for quantity/on-off/prepaid **quantity changes**, not metered usage (metered uses `CreateUsage`). Skip unless the catalog uses quantity-based add-ons.

### Enums in scope (`map/models/enums.md` — `MaxioAdvancedBilling.Models.Enums`)

| Enum | Members (`CSharp (wire)`) |
|---|---|
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` |
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `SubscriptionStateFilter` | `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)` |
| `InvoiceStatus` | `Draft (draft)`, `Open (open)`, `Paid (paid)`, `Pending (pending)`, `Voided (voided)`, `Canceled (canceled)`, `Processing (processing)` |
| `IntervalUnit` | `Day (day)`, `Month (month)` |
| `PricePointType` | `Catalog (catalog)`, `Default (default)`, `Custom (custom)` |
| `ComponentKind` | `MeteredComponent (metered_component)`, `QuantityBasedComponent (quantity_based_component)`, `OnOffComponent (on_off_component)`, `PrepaidUsageComponent (prepaid_usage_component)`, `EventBasedComponent (event_based_component)` |
| `PricingScheme` | `Stairstep (stairstep)`, `Volume (volume)`, `PerUnit (per_unit)`, `Tiered (tiered)` |
| `DiscountType` | `Amount (amount)`, `Percent (percent)` |
| `PaymentType` | `CreditCard (credit_card)`, `BankAccount (bank_account)`, `PaypalAccount (paypal_account)`, `ApplePay (apple_pay)` |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` |
| `SortingDirection` | `Asc (asc)`, `Desc (desc)` |
| `Direction` | `Asc (asc)`, `Desc (desc)` |
| `ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)` |
| `SubscriptionDateField` | `CurrentPeriodEndsAt (current_period_ends_at)`, `CurrentPeriodStartsAt (current_period_starts_at)`, `CreatedAt (created_at)`, `ActivatedAt (activated_at)`, `CanceledAt (canceled_at)`, `ExpiresAt (expires_at)`, `TrialStartedAt (trial_started_at)`, `TrialEndedAt (trial_ended_at)`, `UpdatedAt (updated_at)` |
| `SubscriptionSort` | `SignupDate (signup_date)`, `PeriodStart (period_start)`, `PeriodEnd (period_end)`, `NextAssessment (next_assessment)`, `UpdatedAt (updated_at)`, `CreatedAt (created_at)`, `TotalPayments (total_payments)`, `Id (id)`, `OpenBalance (open_balance)`, `ExpiresAt (expires_at)` |
| `SubscriptionListInclude` | `SelfServicePageToken (self_service_page_token)` |
| `SubscriptionInclude` | `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)` |
| `InvoiceDateField` | `CreatedAt (created_at)`, `DueDate (due_date)`, `IssueDate (issue_date)`, `UpdatedAt (updated_at)`, `PaidDate (paid_date)` |
| `InvoiceSortField` | `Status (status)`, `TotalAmount (total_amount)`, `DueAmount (due_amount)`, `CreatedAt (created_at)`, `UpdatedAt (updated_at)`, `IssueDate (issue_date)`, `DueDate (due_date)`, `Number (number)` |
| `InvoiceRole` | `Unset (unset)`, `Signup (signup)`, `Renewal (renewal)`, `Usage (usage)`, `Reactivation (reactivation)`, `Proration (proration)`, `Migration (migration)`, `Adhoc (adhoc)`, `Backport (backport)`, `BackportBalanceReconciliation (backport-balance-reconciliation)` |
| `CancellationMethod` | `MerchantUi (merchant_ui)`, `MerchantApi (merchant_api)`, `Dunning (dunning)`, `BillingPortal (billing_portal)`, `Unknown (unknown)`, `Imported (imported)` |
| `TrialType` | `NoObligation (no_obligation)`, `PaymentExpected (payment_expected)` |
| `CompoundingStrategy` | `Compound (compound)`, `FullPrice (full-price)` |
| `RecurringScheme` | `DoNotRecur (do_not_recur)`, `RecurIndefinitely (recur_indefinitely)`, `RecurWithDuration (recur_with_duration)` |

`ServerEnvironment` is **not** a `Models.Enums` StringEnum — it is `MaxioAdvancedBilling.Servers.ServerEnvironment` (`Us` / `Eu`).

### Error payload types (shared)

| Type | Fields | Page |
|---|---|---|
| `ErrorListResponse1` | `Errors (errors): IReadOnlyList<string> !req` | `records-2-Cr-Ne.md` |
| `ErrorArrayMapResponse1` | `Errors (errors): IReadOnlyDictionary<string, object>?` | `records-2-Cr-Ne.md` |
| `CustomerErrorResponse1` | `Errors (errors): Errors?` — `Errors` record is `PerPage (per_page): IReadOnlyList<string>?`, `PricePoint (price_point): IReadOnlyList<string>?` (shared generated shape; if those keys are empty, also `TryGetRawError` / `ReadAsString`) | `records-2-Cr-Ne.md` |
| `SingleErrorResponse1` | `Error (error): string !req` | `records-3-Of-Su.md` |
| `SingleStringErrorResponse1` | `Errors (errors): string?` | `records-3-Of-Su.md` |
| `RawError` | `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | `sdk-map.md` |

---

## Trap notes

⚠ Step 1 (client registration) — `HttpClient` / handler lifetime vs the SDK wrapper lifetime is not visible from the constructor; registering a new client per request vs factory-owned pipeline changes socket and DNS behavior. **MUST load `dotnet-client-initialization`** before writing `new MaxioAdvancedBillingClient` or `AddMaxioAdvancedBillingClient`.

⚠ Step 1 (auth) — credentials must be on the options **before** the client is used; hardcoding the API key vs configuration binding is not expressed by `BasicAuthCredentials`. **MUST load `dotnet-authentication`** before wiring `Username`/`Password`.

⚠ Step 1 (resilience) — `RetryOptions.Timeout`, `HttpMethodsToRetry`, and `MaxRetries` do **not** bound a whole call the way an `HttpClient.Timeout` does, and they do not tell you whether a failed **write** (`CreateSubscription`, `CreateUsage`, `MigrateSubscriptionProduct`, `CancelSubscription`) can execute more than once. **MUST load `dotnet-configuration-resilience`** before setting `options.Retry` or assuming POST safety.

⚠ Steps 2–11 (calls) — list/search signatures have many nullable parameters **without C# defaults**; a positional call binds the wrong argument (e.g. `page` into `q` / `state`). Named arguments are required. The token parameter is `ct:`. **MUST load `dotnet-calling-endpoints`** before the first `client.{Group}.{Op}(…)`.

⚠ Steps 2–11 (models) — envelopes (`CustomerResponse.Customer`, `SubscriptionResponse.Subscription`, `ProductResponse.Product`, `CouponResponse.Coupon`, `UsageResponse.Usage`) drop fields if you read the wrapper; unions (`PaymentProfile`, `ProductIdModel`, `ComponentId1`, `AllocatedQuantity3`, `CancelSubscriptionErrorResponse`) are factories + `TryGet…`, not `new`; enums are `StringEnum<T>` members (`CollectionMethod.Automatic`), not C# enum casts; unmodeled JSON is dropped on deserialize. **MUST load `dotnet-models`** before constructing request bodies or mapping responses onto eShop types.

⚠ Step 12 (error boundary) — Case A vs Case B differs **per operation** (create customer is A / `CreateCustomerError`; read/list customer is B / `RawError`; cancel’s 422 payload is itself a union). `TryGetRawError` is not a catch-all on typed errors. There are **no** Result/no-throw methods. **MUST load `dotnet-error-handling`** before writing any `try/catch`.

⚠ Step 12 (error boundary) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 12 (error boundary) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 13 (tests) — the seam to fake is the `HttpClient` passed to the client constructor, not internal controllers. **MUST load `dotnet-testing`** before stubbing Maxio in eShopOnWeb tests.

---

## REQUIRED READING

Load these **before implementation starts**. This sheet deliberately does not carry their contents.

- `dotnet-client-initialization` — step 1: construct / DI-register `MaxioAdvancedBillingClient` and `HttpClient` ownership
- `dotnet-authentication` — step 1: Basic credentials (`Username` = API key, `Password` = `"x"`) from configuration
- `dotnet-configuration-resilience` — step 1: retries, timeouts, `{site}` / base URL, list pagination
- `dotnet-calling-endpoints` — steps 2–11: named arguments, `ct:`, throw-only calls
- `dotnet-models` — steps 2–11: envelopes, `required`/`init`, StringEnum, AnyOf/OneOf factories
- `dotnet-error-handling` — step 12: Case A/B, `TryGet…`, `RawError`, and **both** `JsonException` directions above
- `dotnet-testing` — step 13: `HttpClient` test seam

---

## Assumptions & Blockers

**Assumptions**
- PHASE-BUILD: catalog already seeded (`eshop-subscribe` / `eshop-pro` / `basic-plan`); both products have `require_credit_card` false so no-card `CreateSubscription` is accepted by the site.
- One Maxio customer per eShop user: `CreateCustomer.Reference` = buyer/user id; `ReadCustomerByReference` then create on 404 only.
- One subscription per buyer+plan: `CreateSubscription.Reference` = `buyerId + ":" + productHandle`; `FindSubscription` then create on `TryGetNoContent` only.
- Config keys from env, never hardcoded: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, optional `Maxio:BaseUrl`; `MAXIO_ENVIRONMENT` is 2 chars `US`/`EU`.
- Hero implementation does **not** create catalog, capture cards, or call invoice/coupon/usage/cancel/migrate ops.

**Blockers**
- None. Bare family handle `"eshop-subscribe"` is **not** documented; documented form is `"handle:eshop-subscribe"`. `PaymentCollectionMethod` is optional — not a blocker for no-card subscribe.
