# Maxio Advanced Billing — eShopOnWeb subscription billing

Package `AsadAli.AdvancedBilling.Sdk` · root namespace `MaxioAdvancedBilling` · map stamp `v1.0.2` / `15db14b` (`sdk-map.md`).

## Scope & sequence

Storefront subscription billing: map an eShop buyer to a Maxio customer, collect a tokenized card, subscribe them to a catalog product (plan), show status/invoices, change plan, and cancel/pause. Catalog products are **read** from Maxio, not created at checkout.

| Step | What | Operations |
|---|---|---|
| 1 | NuGet + client/DI + Basic auth + site subdomain | `AddMaxioAdvancedBillingClient` / `new MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)` (`sdk-map.md`) |
| 2 | Error boundary around every SDK call | throw-only ops; Case A/B per row below (`sdk-map.md` error model) |
| 3 | Plan catalog for the storefront | `Products.ListProducts`, `Products.ReadProduct` / `ReadProductByHandle`, `ProductPricePoints.ListProductPricePoints`, optional `ProductFamilies.ListProductFamilies` |
| 4 | Buyer → Maxio customer | `Customers.ReadCustomerByReference` then `CreateCustomer` (or `UpdateCustomer`); persist Maxio `Customer.Id` |
| 5 | Tokenized payment profile | `PaymentProfiles.CreatePaymentProfile` (`ChargifyToken`); `ListPaymentProfiles` / `ReadPaymentProfile`; `ChangeSubscriptionDefaultPaymentProfile` |
| 6 | Preview + create subscription | `Subscriptions.PreviewSubscription`, `Subscriptions.CreateSubscription` |
| 7 | Account: status, list, lookup | `Subscriptions.ReadSubscription`, `Customers.ListCustomerSubscriptions`, `Subscriptions.FindSubscription` |
| 8 | Plan change (prorated now vs delayed) | `SubscriptionProducts.PreviewSubscriptionProductMigration` + `MigrateSubscriptionProduct`; **or** `Subscriptions.UpdateSubscription` with `ProductChangeDelayed` |
| 9 | Cancel / pause / resume / retry | `SubscriptionStatus.CancelSubscription`, `InitiateDelayedCancellation`, `CancelDelayedCancellation`, `PauseSubscription`, `ResumeSubscription`, `ReactivateSubscription`, `RetrySubscription` |
| 10 | Invoices | `Invoices.ListInvoices`, `Invoices.ReadInvoice`; optional `SendInvoice` |
| 11 | Optional self-serve portal | `BillingPortal.EnableBillingPortalForCustomer`, `ReadBillingPortalLink` |
| 12 | Tests | stub `HttpClient` seam (`dotnet-testing`) |

Out of v1 storefront scope (do not call unless a later brief adds them): product/family **create/archive**, subscription groups, components/usage, coupons, ad-hoc `CreateInvoice` / void / refund, `OverrideSubscription`, `PurgeSubscription`, prepaid config.

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

### Client, auth, servers (`sdk-map.md`)

| Fact | Value | Cite |
|---|---|---|
| Client | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` — only ctor `(System.Net.Http.HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` |
| Options | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions`: `Environment` (`MaxioAdvancedBilling.Servers.ServerEnvironment`), `Retry` (`MaxioAdvancedBilling.Core.Configuration.RetryOptions`), `Server` (`MaxioAdvancedBilling.ServerOptions` — root namespace; `.Production` is `MaxioAdvancedBilling.Servers.ProductionOptions`), `BasicAuth` (`MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?`) | `sdk-map.md` · `ServerOptions.cs` |
| NuGet version | Map stamp is git tag `v1.0.2` / commit `15db14b` (`sdk-map.md`). At that commit `MaxioAdvancedBilling.csproj` has `<PackageId>AsadAli.AdvancedBilling.Sdk</PackageId>` and `<Version>1.0.0</Version>` — tag and csproj **disagree**. Add `AsadAli.AdvancedBilling.Sdk` **1.0.2** to match the map tag the skills pin; if restore cannot find 1.0.2, the source Version at this commit is **1.0.0**. | `sdk-map.md` · `MaxioAdvancedBilling.csproj` |
| DI | `services.AddMaxioAdvancedBillingClient(o => { … })` (`ServiceCollectionExtensions.cs`, namespace `MaxioAdvancedBilling`). Registers **`AddSingleton`** of `MaxioAdvancedBillingClient` (factory uses `IHttpClientFactory.CreateClient()`). | `ServiceCollectionExtensions.cs` |
| Auth | HTTP Basic only. `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = "<api_key>", Password = "x" }` — password is the **literal** `"x"` | `sdk-map.md` |
| Environments | `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default, wire `US`) → `https://{site}.chargify.com`; `.Eu` (wire `EU`) → `https://{site}.ebilling.maxio.com`. **No public `FromValue(string)`** on `ServerEnvironment` (unlike `CollectionMethod.FromValue`). Public API: static `.Us` / `.Eu` / `.Default()`, plus inherited `StringEnum<T>.TryGetKnownValue(string, out T?)` (case-insensitive). Map config `"US"`/`"EU"` with `.Us`/`.Eu` or `TryGetKnownValue`. | `sdk-map.md` · `Servers/ServerEnvironment.cs` · `Core/Enum/StringEnum.cs` |
| Site / BaseUrl | Server group name is literally **`Production`**. `options.Server` is `MaxioAdvancedBilling.ServerOptions`; `.Production` is `MaxioAdvancedBilling.Servers.ProductionOptions`. US: `options.Server.Production.Us.Site` and `.Us.BaseUrl` (default `https://{site}.chargify.com`). **If `Environment` is `Eu`, set `options.Server.Production.Eu.Site` and `.Eu.BaseUrl`** (default `https://{site}.ebilling.maxio.com`) — `Resolve` reads only the nested options matching `Environment`. `{site}` defaults to `"subdomain"`. | `sdk-map.md` · `ServerOptions.cs` · `Servers/ProductionOptions.cs` |
| RetryOptions | namespace `MaxioAdvancedBilling.Core.Configuration`; all members `required` — full instance or `RetryOptions.Default()`. Members: `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout` (`TimeSpan?`), `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry` | `sdk-map.md` |
| Throw model | Every operation is throw-only (`SdkException<TError>`). No `…Result` / `ApiResult` variants. Case A: `TError` is `MaxioAdvancedBilling.Errors.{Op}Error` with `TryGet…` + inherited `TryGetRawError`. Case B: `TError` is `MaxioAdvancedBilling.Core.ErrorResponse.RawError` (`StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`). `SdkException<T>` → `MaxioAdvancedBilling.Core.Exceptions`. `ApiError`/`RawError` → `MaxioAdvancedBilling.Core.ErrorResponse` | `sdk-map.md` |

Controllers live on the client (`client.Customers`, `client.Subscriptions`, …) — types in `MaxioAdvancedBilling.Api`. Records → `MaxioAdvancedBilling.Models`. Enums → `MaxioAdvancedBilling.Models.Enums` (`StringEnum<T>` / `IntEnum<T>`, **not** C# enums: `CollectionMethod.Automatic` or `CollectionMethod.FromValue("automatic")`). OneOf → `MaxioAdvancedBilling.Models.OneOf`. AnyOf → `MaxioAdvancedBilling.Models.AnyOf`.

### Operations

#### Customers — `client.Customers` · `operations/Customers.md` · `Api/Customers.cs`

| Operation | Signature (params in order) | Request | Response envelope (read these) | Error | Pagination |
|---|---|---|---|---|---|
| **CreateCustomer** | `CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, **must pass explicitly** | Envelope `CreateCustomerRequest`: `Customer (customer): CreateCustomer !req`. Inner `CreateCustomer`: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`; optional `Reference (reference): string?` (eShop buyer id — **must be unique**), `CcEmails`, `Organization`, `Address`/`Address2`/`City`/`State`/`Zip`/`Country` (ISO 3166-1 alpha-2 / ISO 3166-2), `Phone`, `Locale`, `VatNumber`, `TaxExempt`, `TaxExemptReason`, `ParentId`, `SalesforceId` (`records-1-Ac-Cr.md`) | `CustomerResponse` → **`Customer` (`Customer !req`)**. Read: `Id (id): int?`, `Reference (reference): string?`, `Email`, `FirstName`, `LastName`, `CreatedAt` (`records-2-Cr-Ne.md`) | **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)`. Payload `CustomerErrorResponse1.Errors (errors): Errors?` — see UNVERIFIED note below | none |
| **ReadCustomerByReference** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | query `reference` | `CustomerResponse` → **`Customer`** (same fields) | **Case B** `SdkException<RawError>` | none |
| **ReadCustomer** | `ReadCustomer(int id, CancellationToken ct = default)` | path `id` | `CustomerResponse` → **`Customer`** | **Case B** `SdkException<RawError>` | none |
| **UpdateCustomer** | `UpdateCustomer(int id, MaxioAdvancedBilling.Models.UpdateCustomerRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | Envelope `UpdateCustomerRequest`: `Customer (customer): UpdateCustomer !req`. Inner: all optional (`FirstName`, `LastName`, `Email`, `Reference`, address, `Locale`, `VatNumber`, `TaxExempt`, …) (`records-4-Su-We.md`) | `CustomerResponse` → **`Customer`** | **Case A** `SdkException<MaxioAdvancedBilling.Errors.UpdateCustomerError>`: `TryGetNoContent(out RawError)` [404] · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError` | none |
| **ListCustomerSubscriptions** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | path `customer_id` | `IReadOnlyList<SubscriptionResponse>` — each item unwrap **`.Subscription`** (`Subscription?`) | **Case B** `SdkException<RawError>` | none |
| **ListCustomers** | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, ct = default)` — 7 leading params **must pass explicitly** (pass `null` to skip) | query `q` for search; `per_page` ← `perPage` | `IReadOnlyList<CustomerResponse>` → each **`.Customer`** | **Case B** `SdkException<RawError>` | manual `page`+`perPage` |

**Customer lookup rule:** `ReadCustomerByReference(eShopUserId)` first; on Case B 404, `CreateCustomer` with `Reference = eShopUserId`. Do not create twice for the same reference.

**UNVERIFIED (suspicious shared model):** `CustomerErrorResponse1.Errors` is typed as record `Errors` whose members are `PerPage (per_page)` and `PricePoint (price_point)` (`records-2-Cr-Ne.md`) — that is not a customer-validation shape. A separate union `Errors1` (`CustomerError` \| `IReadOnlyList<string>`, `unions.md`) exists but is **not** the accessor on this operation. On 422: call `TryGetCustomerErrorResponse1`; if it does not yield usable messages, **extract best-effort** via `TryGetRawError` + `ReadAsString()` / `ReadAsJson<T>()`. Do not assume `Errors.PerPage` is the buyer-facing text.

#### Products / plans — `client.Products` · `operations/Products.md`; families `operations/ProductFamilies.md`; price points `operations/ProductPricePoints.md`

| Operation | Signature | Request | Response (read) | Error | Pagination |
|---|---|---|---|---|---|
| **ListProducts** | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, ct = default)` — 8 leading **must pass explicitly** | `filter`: `ListProductsFilter` (`Ids`, `PrepaidProductPricePoint`, `UseSiteExchangeRate`) (`records-2-Cr-Ne.md`). `include` enum `ListProductsInclude.PrepaidProductPricePoint` (`prepaid_product_price_point`) | `IReadOnlyList<ProductResponse>` → each **`.Product` (`Product !req`)**. Read: `Id`, `Name`, `Handle`, `Description`, `PriceInCents (price_in_cents): long?`, `Interval`, `IntervalUnit`, `TrialPriceInCents`, `TrialInterval`, `TrialIntervalUnit`, `RequireCreditCard`, `ProductPricePointId`, `ProductPricePointHandle`, `DefaultProductPricePointId`, nested `ProductFamily` (`records-3-Of-Su.md`) | **Case B** `SdkException<RawError>` | `page`+`perPage` (default 20) |
| **ReadProduct** | `ReadProduct(int productId, ct = default)` | path | `ProductResponse` → **`.Product`** | **Case B** | none |
| **ReadProductByHandle** | `ReadProductByHandle(string apiHandle, ct = default)` | path `api_handle` | `ProductResponse` → **`.Product`** | **Case B** | none |
| **ListProductFamilies** | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, ct = default)` — all 5 **must pass explicitly** | — | `IReadOnlyList<ProductFamilyResponse>` → each **`.ProductFamily` (`ProductFamily?`)**: `Id`, `Name`, `Handle` | **Case B** | none |
| **ListProductsForProductFamily** | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, ct = default)` — 8 params after `productFamilyId` **must pass explicitly** | Path `{product_family_id}`: **id number as string, or handle prefixed `handle:`** (e.g. `handle:` + `Maxio:ProductFamilyHandle`). Do **not** pass the bare handle. | `IReadOnlyList<ProductResponse>` → each **`.Product` (`Product !req`)** | **Case A** `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError` | `page`+`perPage` (default 20, max 200) |
| **ListProductPricePoints** | `ListProductPricePoints(ProductIdModel productId, bool? currencyPrices, IReadOnlyList<PricePointType>? filterType, bool? archived, int? page = 1, int? perPage = 10, ct = default)` — 3 leading after `productId` **must pass explicitly** | `productId` is union `MaxioAdvancedBilling.Models.AnyOf.ProductIdModel`: factories `ProductIdModel.Int(int)` / `ProductIdModel.String(string)` (`unions.md`). query `filter[type]` ← `filterType`, `currency_prices` ← `currencyPrices` | `ListProductPricePointsResponse` → **`PricePoints (price_points): IReadOnlyList<ProductPricePoint> !req`**. Read: `Id`, `Name`, `Handle`, `PriceInCents`, `Interval`, `IntervalUnit`, `Trial*`, `Type (type): PricePointType?`, `ProductId` (`records-3-Of-Su.md`) | **Case B** `SdkException<RawError>` | `page`+`perPage` (default **10**) |
| **ReadProductPricePoint** | `ReadProductPricePoint(ProductIdModel productId, PricePointIdModel pricePointId, bool? currencyPrices, ct = default)` — `currencyPrices` **must pass explicitly** | `PricePointIdModel.Int` / `.String` (`unions.md`) | `ProductPricePointResponse` → **`PricePoint (price_point): ProductPricePoint !req`** | **Case B** | none |

Storefront identifies a plan as **product handle + price-point handle** (stable) or ids (numeric).

#### Payment profiles — `client.PaymentProfiles` · `operations/PaymentProfiles.md`

`PaymentProfileResponse.PaymentProfile` is **OneOf union** `MaxioAdvancedBilling.Models.OneOf.PaymentProfile` — construct `PaymentProfile.CreditCardPaymentProfile(…)`; read with `TryGetCreditCardPaymentProfile(out CreditCardPaymentProfile)`, `TryGetBankAccountPaymentProfile`, `TryGetPaypalPaymentProfile`, `TryGetApplePayPaymentProfile` (`unions.md`). **Do not `new PaymentProfile`.**

| Operation | Signature | Request | Response (read) | Error | Pagination |
|---|---|---|---|---|---|
| **CreatePaymentProfile** | `CreatePaymentProfile(CreatePaymentProfileRequest? body, ct = default)` — `body` **must pass explicitly** | Envelope: `PaymentProfile (payment_profile): CreatePaymentProfile !req`. Storefront fields: `ChargifyToken (chargify_token): string?` (Maxio.js / Chargify.js one-time token — **prefer this over PAN**), `CustomerId (customer_id): int?`, `FirstName`/`LastName`, billing address, `PaymentType (payment_type): PaymentType?`, `Cvv`, `FullNumber` (PCI — avoid in production), `ExpirationMonth`/`ExpirationYear` as `ExpirationMonth1`/`ExpirationYear1` unions (`Int`/`String` factories) (`records-1-Ac-Cr.md`) | `PaymentProfileResponse` → **`.PaymentProfile` (union)**. Credit-card variant `CreditCardPaymentProfile`: `Id`, `MaskedCardNumber`, `CardType`, `ExpirationMonth`/`Year` (`int?`), `CustomerId`, `PaymentType` default `PaymentType.CreditCard`, `Disabled` (`records-2-Cr-Ne.md`) | **Case A** `SdkException<CreatePaymentProfileError>`: `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError`. `ErrorListResponse1.Errors (errors): IReadOnlyList<string> !req` | none |
| **ListPaymentProfiles** | `ListPaymentProfiles(int? customerId, int? page = 1, int? perPage = 20, ct = default)` — `customerId` **must pass explicitly** | query `customer_id` | `IReadOnlyList<PaymentProfileResponse>` → each **`.PaymentProfile` (union)**. Empty list, not 404, when none | **Case B** `SdkException<RawError>` | `page`+`perPage` |
| **ReadPaymentProfile** | `ReadPaymentProfile(int paymentProfileId, ct = default)` | path | `PaymentProfileResponse` → union | **Case A** `SdkException<ReadPaymentProfileError>`: `TryGetNoContent` [404] · `TryGetRawError` | none |
| **ChangeSubscriptionDefaultPaymentProfile** | `ChangeSubscriptionDefaultPaymentProfile(int subscriptionId, int paymentProfileId, ct = default)` | path | `PaymentProfileResponse` → union | **Case A** `SdkException<ChangeSubscriptionDefaultPaymentProfileError>`: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| **UpdatePaymentProfile** | `UpdatePaymentProfile(int paymentProfileId, UpdatePaymentProfileRequest? body, ct = default)` — `body` **must pass explicitly** | Envelope `PaymentProfile (payment_profile): UpdatePaymentProfile !req`. Fields: `FirstName`, `LastName`, `FullNumber`, `ExpirationMonth`/`Year` (`string?`), billing address (`records-4-Su-We.md`). Changing card number generally requires a **new** profile | `PaymentProfileResponse` | **Case A** `SdkException<UpdatePaymentProfileError>`: `TryGetNoContent` [404] · `TryGetErrorStringMapResponse1(out ErrorStringMapResponse1)` [422] (`Errors: IReadOnlyDictionary<string, string>?`) · `TryGetRawError` | none |
| **DeleteUnusedPaymentProfile** | `DeleteUnusedPaymentProfile(int paymentProfileId, ct = default)` | — | `void` | **Case A** `SdkException<DeleteUnusedPaymentProfileError>`: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| **DeleteSubscriptionsPaymentProfile** | `DeleteSubscriptionsPaymentProfile(int subscriptionId, int paymentProfileId, ct = default)` | removes profile from **all** of that customer’s subscriptions | `void` | **Case B** `SdkException<RawError>` | none |
| **ReadOneTimeToken** | `ReadOneTimeToken(string chargifyToken, ct = default)` | inspect token before attach | `GetOneTimeTokenRequest` → **`PaymentProfile (payment_profile): GetOneTimeTokenPaymentProfile !req`** (masked card fields) | **Case A** `SdkException<ReadOneTimeTokenError>`: `TryGetErrorListResponse1` [404] · `TryGetRawError` | none |

Creating a profile **does not** make it current on existing subscriptions — call `ChangeSubscriptionDefaultPaymentProfile` after.

#### Subscriptions — `client.Subscriptions` · `operations/Subscriptions.md`

| Operation | Signature | Request | Response (read) | Error | Pagination |
|---|---|---|---|---|---|
| **PreviewSubscription** | `PreviewSubscription(CreateSubscriptionRequest? body, ct = default)` — `body` **must pass explicitly**; **same body type as create**; does **not** create | see CreateSubscription body | `SubscriptionPreviewResponse` → **`SubscriptionPreview (subscription_preview): SubscriptionPreview !req`**: `CurrentBillingManifest` / `NextBillingManifest` (`BillingManifest`: `TotalInCents`, `TotalTaxInCents`, `TotalDiscountInCents`, `SubtotalInCents`, `StartDate`, `EndDate`, `LineItems`) (`records-4-Su-We.md`, `records-1-Ac-Cr.md`) | **Case B** `SdkException<RawError>` | none |
| **CreateSubscription** | `CreateSubscription(CreateSubscriptionRequest? body, ct = default)` — `body` **must pass explicitly** | Envelope `CreateSubscriptionRequest`: `Subscription (subscription): CreateSubscription !req`. Identify product: `ProductId` **or** `ProductHandle`; price point: `ProductPricePointId` **or** `ProductPricePointHandle`. Identify customer: `CustomerId` **or** `CustomerReference` **or** nested `CustomerAttributes`. Payment: `PaymentProfileId` **or** `PaymentProfileAttributes` / `CreditCardAttributes` (both type `PaymentProfileAttributes` — set `ChargifyToken`) **or** `BankAccountAttributes`. Also: `CouponCode`/`CouponCodes`, `PaymentCollectionMethod`, `Reference` (eShop subscription key), `NextBillingAt`, `Components` (omit for v1), `AgreementAcceptance` (required when using Maxio Payments) (`records-2-Cr-Ne.md`) | `SubscriptionResponse` → **`Subscription (subscription): Subscription?`**. Read: `Id`, `State`, `BalanceInCents`, `ProductPriceInCents`, `CurrentPeriodEndsAt`, `CurrentPeriodStartedAt`, `NextAssessmentAt`, `ActivatedAt`, `CancelAtEndOfPeriod`, `CanceledAt`, `PaymentCollectionMethod`, nested `Customer`, nested `Product`, nested `CreditCard`, `ProductPricePointId`, `Reference`, `SelfServicePageToken` (only if requested via include on **Read**) (`records-3-Of-Su.md`) | **Case A** `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError`. 422 may include 3DS `action_link` in the raw body — **UNVERIFIED** whether that field is on `ErrorListResponse1` (only `Errors: IReadOnlyList<string>`); extract best-effort from `TryGetRawError`/`ReadAsJson` | none |
| **ReadSubscription** | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, ct = default)` — `include` **must pass explicitly** (`null` to skip) | `include`: `SubscriptionInclude.Coupons` / `SelfServicePageToken` | `SubscriptionResponse` → **`.Subscription`** | **Case B** `SdkException<RawError>` | none |
| **FindSubscription** | `FindSubscription(string? reference, ct = default)` — `reference` **must pass explicitly** | query `reference` | `SubscriptionResponse` → **`.Subscription`** | **Case A** `SdkException<FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError` | none |
| **ListSubscriptions** | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, ct = default)` — 14 leading **must pass explicitly** | prefer `ListCustomerSubscriptions` for a buyer | `IReadOnlyList<SubscriptionResponse>` → each **`.Subscription`** | **Case B** | `page`+`perPage` (default 20) |
| **UpdateSubscription** | `UpdateSubscription(int subscriptionId, UpdateSubscriptionRequest? body, ct = default)` — `body` **must pass explicitly** | Envelope `Subscription (subscription): UpdateSubscription !req`. Plan change without proration: `ProductHandle`/`ProductId` + optional `ProductPricePointId`/`Handle`; delayed: also `ProductChangeDelayed = true`. Cancel a delayed change: `NextProductId` = empty string. Card via `CreditCardAttributes` (`FullNumber`, `ExpirationMonth`/`Year` as `string?`). `NextBillingAt`, `Reference` (`records-4-Su-We.md`) | `SubscriptionResponse` → **`.Subscription`** | **Case A** `SdkException<UpdateSubscriptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |

`CreateSubscription` notes (map): payment may be required depending on the product; raw PAN needs PCI — use Maxio.js token. Nested `CustomerAttributes` fields are all optional in the model (`FirstName`, `LastName`, `Email`, `Reference`, address, …) (`records-2-Cr-Ne.md`) — still send name+email when creating the customer inline.

#### Plan change (prorated migration) — `client.SubscriptionProducts` · `operations/SubscriptionProducts.md`

Valid source states per map: `active` or `trialing`. Migrating to the **current** product is a common 422.

| Operation | Signature | Request | Response (read) | Error | Pagination |
|---|---|---|---|---|---|
| **PreviewSubscriptionProductMigration** | `PreviewSubscriptionProductMigration(int subscriptionId, SubscriptionMigrationPreviewRequest? body, ct = default)` — `body` **must pass explicitly** | Envelope `Migration (migration): SubscriptionMigrationPreviewOptions !req`. Target: `ProductId` **or** `ProductHandle`; `ProductPricePointId` **or** `ProductPricePointHandle`. Flags: `IncludeTrial = false`, `IncludeInitialCharge = false`, `IncludeCoupons = true`, `PreservePeriod = false`, `Proration`, `ProrationDate` (`records-4-Su-We.md`) | `SubscriptionMigrationPreviewResponse` → **`Migration (migration): SubscriptionMigrationPreview !req`**: `ProratedAdjustmentInCents`, `ChargeInCents`, `PaymentDueInCents`, `CreditAppliedInCents` | **Case A** `SdkException<PreviewSubscriptionProductMigrationError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| **MigrateSubscriptionProduct** | `MigrateSubscriptionProduct(int subscriptionId, SubscriptionProductMigrationRequest? body, ct = default)` — `body` **must pass explicitly** | Envelope `Migration (migration): SubscriptionProductMigration !req`. Same target/flags as preview **minus** `ProrationDate`. `Proration.PreservePeriod` (`records-4-Su-We.md`, `records-3-Of-Su.md`) | `SubscriptionResponse` → **`.Subscription`** | **Case A** `SdkException<MigrateSubscriptionProductError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` (3DS: same UNVERIFIED raw-body directive as create) | none |

**Which plan-change path:** immediate prorated upgrade/downgrade → preview + migrate. End-of-period change with no proration → `UpdateSubscription` with `ProductChangeDelayed = true`.

#### Lifecycle — `client.SubscriptionStatus` · `operations/SubscriptionStatus.md`

| Operation | Signature | Request | Response (read) | Error | Pagination |
|---|---|---|---|---|---|
| **CancelSubscription** | `CancelSubscription(int subscriptionId, CancellationRequest? body, ct = default)` — `body` **must pass explicitly** (pass `null` for immediate cancel with no options) | Envelope `Subscription (subscription): CancellationOptions !req` when body is sent: `CancellationMessage`, `ReasonCode`, `CancelAtEndOfPeriod`, `ScheduledCancellationAt`, `RefundPrepaymentAccountBalance` (`records-1-Ac-Cr.md`) | `SubscriptionResponse` → **`.Subscription`** (`State` → canceled) | **Case A** `SdkException<CancelSubscriptionApiError>` (type name is `CancelSubscriptionApiError`, not `CancelSubscriptionError`): `TryGetNoContent` [404] · `TryGetCancelSubscriptionErrorResponse(out CancelSubscriptionErrorResponse)` [422] · `TryGetRawError`. `CancelSubscriptionErrorResponse` is **AnyOf** `ErrorListResponse1` \| `SingleErrorResponse1` — `TryGetErrorListResponse1` / `TryGetSingleErrorResponse1` (`unions.md`). `SingleErrorResponse1.Error (error): string !req` | none |
| **InitiateDelayedCancellation** | `InitiateDelayedCancellation(int subscriptionId, CancellationRequest? body, ct = default)` — `body` **must pass explicitly** | same `CancellationRequest` | `DelayedCancellationResponse`: `Message (message): string?` (**not** a subscription envelope) | **Case A** `SdkException<InitiateDelayedCancellationError>`: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| **CancelDelayedCancellation** | `CancelDelayedCancellation(int subscriptionId, ct = default)` | — | `DelayedCancellationResponse` (`Message`) | **Case A** `SdkException<CancelDelayedCancellationError>`: `TryGetNoContent` [404] · `TryGetRawError` | none |
| **PauseSubscription** | `PauseSubscription(int subscriptionId, PauseRequest? body, ct = default)` — `body` **must pass explicitly** | `PauseRequest.Hold (hold): AutoResume?` — `AutomaticallyResumeAt` (`records-3-Of-Su.md`, `records-1-Ac-Cr.md`). Map: cannot pause if `next_billing_at` is within 24 hours | `SubscriptionResponse` → **`.Subscription`** | **Case A** `SdkException<PauseSubscriptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| **ResumeSubscription** | `ResumeSubscription(int subscriptionId, ResumptionCharge? calendarBillingResumptionCharge, ct = default)` — `calendarBillingResumptionCharge` **must pass explicitly** (`null` if not calendar billing) | query `calendar_billing['resumption_charge']` | `SubscriptionResponse` → **`.Subscription`** | **Case A** `SdkException<ResumeSubscriptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| **ReactivateSubscription** | `ReactivateSubscription(int subscriptionId, ReactivateSubscriptionRequest? body, ct = default)` — `body` **must pass explicitly** | `IncludeTrial`, `PreserveBalance`, `CouponCode`, `UseCreditsAndPrepayments`, `Resume` (union `Resume.Bool(bool)` / `Resume.ResumeOptions(ResumeOptions)` with `RequireResume`, `ForgiveBalance`), `CalendarBilling.ReactivationCharge` (`records-3-Of-Su.md`, `unions.md`) | `SubscriptionResponse` → **`.Subscription`** | **Case A** `SdkException<ReactivateSubscriptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| **RetrySubscription** | `RetrySubscription(int subscriptionId, ct = default)` | past-due capture now | `SubscriptionResponse` → **`.Subscription`** | **Case A** `SdkException<RetrySubscriptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |

Immediate cancel = `CancelSubscription(id, body: null)`. End-of-term = `InitiateDelayedCancellation` (or `CancellationOptions.CancelAtEndOfPeriod` on cancel if that site feature is on).

#### Invoices — `client.Invoices` · `operations/Invoices.md`

Envelope split: **list** wraps `ListInvoicesResponse.Invoices`; **read / issue / refund / void / reopen** return **`Invoice` directly** (no `InvoiceResponse` wrapper). **CreateInvoice** is the odd one: returns `InvoiceResponse` → `.Invoice`.

| Operation | Signature | Request | Response (read) | Error | Pagination |
|---|---|---|---|---|---|
| **ListInvoices** | `ListInvoices(string? startDate, string? endDate, InvoiceStatus? status, int? subscriptionId, string? subscriptionGroupUid, string? consolidationLevel, Direction? direction, InvoiceDateField? dateField, string? startDatetime, string? endDatetime, IReadOnlyList<int>? customerIds, IReadOnlyList<string>? number, IReadOnlyList<int>? productIds, InvoiceSortField? sort, int? page = 1, int? perPage = 20, bool? lineItems = false, bool? discounts = false, bool? taxes = false, bool? credits = false, bool? payments = false, bool? customFields = false, bool? refunds = false, ct = default)` — 14 leading **must pass explicitly** | Storefront: pass `subscriptionId:` or `customerIds:`; set `lineItems: true` (and `payments:`/`discounts:` as needed) — defaults are **false** so totals-only otherwise | `ListInvoicesResponse` → **`Invoices (invoices): IReadOnlyList<Invoice> !req`**. Read on `Invoice`: `Uid`, `Number`, `Status`, `IssueDate`, `DueDate`, `PaidDate`, `Currency`, `TotalAmount`, `DueAmount`, `PaidAmount`, `PublicUrl`, `SubscriptionId`, `CustomerId`, `LineItems` (`records-2-Cr-Ne.md`) | **Case B** `SdkException<RawError>` | `page`+`perPage` |
| **ReadInvoice** | `ReadInvoice(string uid, ct = default)` | path `uid` (invoice uid string, not numeric id) | **`Invoice`** (bare — **not** `InvoiceResponse`) | **Case B** `SdkException<RawError>` | none |
| **SendInvoice** | `SendInvoice(string uid, SendInvoiceRequest? body, ct = default)` — `body` **must pass explicitly** | `RecipientEmails`, `CcRecipientEmails`, `BccRecipientEmails`, `AttachmentUrls` (`records-3-Of-Su.md`). Empty recipients → subscription default email | `void` | **Case A** `SdkException<SendInvoiceError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |

Buyer invoice history: `ListInvoices(subscriptionId: subId, …, lineItems: true, payments: true, …)` then `ReadInvoice(uid)` for detail. Use `Invoice.Uid` on subsequent calls, not `Invoice.Id`.

#### Billing portal (optional) — `client.BillingPortal` · `operations/BillingPortal.md`

| Operation | Signature | Request | Response (read) | Error | Pagination |
|---|---|---|---|---|---|
| **EnableBillingPortalForCustomer** | `EnableBillingPortalForCustomer(int customerId, AutoInvite? autoInvite, ct = default)` — `autoInvite` **must pass explicitly** | query `auto_invite`. Enum `AutoInvite` is **IntEnum**: `Value0 (0)`, `Value1 (1)` (`enums.md`) | `CustomerResponse` → **`.Customer`** | **Case A** `SdkException<EnableBillingPortalForCustomerError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| **ReadBillingPortalLink** | `ReadBillingPortalLink(int customerId, ct = default)` | cache the URL; map: reuse until `NewLinkAvailableAt`; 15 req cap then 429 | `PortalManagementLink`: `Url`, `FetchCount`, `CreatedAt`, `NewLinkAvailableAt`, `ExpiresAt`, `LastInviteSentAt` (`records-3-Of-Su.md`) | **Case A** `SdkException<ReadBillingPortalLinkError>`: `TryGetErrorListResponse1` [422] · `TryGetTooManyManagementLinkRequestsError1(out TooManyManagementLinkRequestsError1)` [429] (`Errors.Error`, `Errors.NewLinkAvailableAt`) · `TryGetRawError` | none |

### Enums in scope (`map/models/enums.md` — namespace `MaxioAdvancedBilling.Models.Enums`)

| Enum | Members (C# identifier `(wire)`) |
|---|---|
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` |
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `SubscriptionStateFilter` | `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)` |
| `SubscriptionInclude` | `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)` |
| `SubscriptionListInclude` | `SelfServicePageToken (self_service_page_token)` |
| `SubscriptionDateField` | `CurrentPeriodEndsAt`, `CurrentPeriodStartsAt`, `CreatedAt`, `ActivatedAt`, `CanceledAt`, `ExpiresAt`, `TrialStartedAt`, `TrialEndedAt`, `UpdatedAt` (wires = snake_case of those names) |
| `SubscriptionSort` | `SignupDate (signup_date)`, `PeriodStart (period_start)`, `PeriodEnd (period_end)`, `NextAssessment (next_assessment)`, `UpdatedAt (updated_at)`, `CreatedAt (created_at)`, `TotalPayments (total_payments)`, `Id (id)`, `OpenBalance (open_balance)`, `ExpiresAt (expires_at)` |
| `SortingDirection` / `Direction` | `Asc (asc)`, `Desc (desc)` |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` |
| `IntervalUnit` | `Day (day)`, `Month (month)` |
| `PricePointType` | `Catalog (catalog)`, `Default (default)`, `Custom (custom)` |
| `PaymentType` | `CreditCard (credit_card)`, `BankAccount (bank_account)`, `PaypalAccount (paypal_account)`, `ApplePay (apple_pay)` |
| `CardType` | `Visa (visa)`, `Master (master)`, `Discover (discover)`, `AmericanExpress (american_express)`, `Bogus (bogus)`, … (full list `enums.md`) |
| `CreditCardVault` / `AllVaults` | include `Bogus (bogus)` for test; `Stripe (stripe)`, `MaxioPayments (maxio_payments)`, … |
| `InvoiceStatus` | `Draft (draft)`, `Open (open)`, `Paid (paid)`, `Pending (pending)`, `Voided (voided)`, `Canceled (canceled)`, `Processing (processing)` |
| `InvoiceDateField` | `CreatedAt (created_at)`, `DueDate (due_date)`, `IssueDate (issue_date)`, `UpdatedAt (updated_at)`, `PaidDate (paid_date)` |
| `InvoiceSortField` | `Status`, `TotalAmount`, `DueAmount`, `CreatedAt`, `UpdatedAt`, `IssueDate`, `DueDate`, `Number` (wires snake_case) |
| `InvoiceRole` | `Unset`, `Signup`, `Renewal`, `Usage`, `Reactivation`, `Proration`, `Migration`, `Adhoc`, `Backport`, `BackportBalanceReconciliation` (`backport-balance-reconciliation`) |
| `CancellationMethod` | `MerchantUi (merchant_ui)`, `MerchantApi (merchant_api)`, `Dunning (dunning)`, `BillingPortal (billing_portal)`, `Unknown (unknown)`, `Imported (imported)` |
| `ResumptionCharge` / `ReactivationCharge` | `Prorated (prorated)`, `Immediate (immediate)`, `Delayed (delayed)` |
| `FailedPaymentAction` | `LeaveOpenInvoice (leave_open_invoice)`, `RollbackToPending (rollback_to_pending)`, `InitiateDunning (initiate_dunning)` |
| `ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)` |
| `AutoInvite` | IntEnum `Value0 (0)`, `Value1 (1)` |
| `TrialType` | `NoObligation (no_obligation)`, `PaymentExpected (payment_expected)` |
| `ExpirationIntervalUnit` | `Day (day)`, `Month (month)`, `Never (never)` |

### Unions in scope (`map/models/unions.md`)

| Union | Namespace | Factories / TryGet |
|---|---|---|
| `PaymentProfile` | `MaxioAdvancedBilling.Models.OneOf` | `CreditCardPaymentProfile` / `BankAccountPaymentProfile` / `PaypalPaymentProfile` / `ApplePayPaymentProfile` |
| `ProductIdModel`, `PricePointIdModel` | `MaxioAdvancedBilling.Models.AnyOf` | `.Int(int)` / `.String(string)` |
| `ExpirationMonth1`/`Year1` (create profile), `ExpirationMonth2`/`Year2` (attributes) | `AnyOf` | `.Int` / `.String` |
| `Resume` | `AnyOf` | `.Bool(bool)` / `.ResumeOptions(ResumeOptions)` |
| `SnapDay1` (update subscription) | `AnyOf` | `.String` / `.Int` |
| `CancelSubscriptionErrorResponse` | `AnyOf` | `.ErrorListResponse1` / `.SingleErrorResponse1` |
| `Errors1` | `AnyOf` | **not** wired to CreateCustomer’s accessor — do not use in place of `TryGetCustomerErrorResponse1` |

---

## Trap notes

⚠ Step 1 (client registration) — `HttpClient` ownership and whether the SDK wrapper is transient vs the handler pipeline is long-lived is not visible from the constructor. **MUST load `dotnet-client-initialization`** before DI registration.

⚠ Step 1 (auth) — wrong property name, hardcoded key, or password other than the site convention yields 401 and looks like a “bad host”. **MUST load `dotnet-authentication`** before setting `BasicAuth`.

⚠ Step 1 (resilience) — `Retry` / `Timeout` on options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; which verbs retry on status vs transport failure changes whether a failed **CreateSubscription** / **MigrateSubscriptionProduct** / **CreatePaymentProfile** can run twice. **MUST load `dotnet-configuration-resilience`** before wiring `RetryOptions` or `Server.Production.*.Site`.

⚠ Steps 3–11 (every list/search call) — many nullable query params have **no C# default** and **must be passed explicitly**; positional calls mis-bind (`ListProducts`, `ListSubscriptions`, `ListInvoices`, `ListPaymentProfiles`, `ReadSubscription`’s `include`, `ResumeSubscription`’s `calendarBillingResumptionCharge`, `CreateCustomer`/`CreateSubscription` `body`, etc.). Named arguments; token is `ct:`. **MUST load `dotnet-calling-endpoints`** before the first `client.*` call.

⚠ Steps 3–11 (envelopes) — unwrap the map’s inner field (`CustomerResponse.Customer`, `SubscriptionResponse.Subscription` which is **nullable**, `ProductResponse.Product`, `PaymentProfileResponse.PaymentProfile` **union**, `ListInvoicesResponse.Invoices`). `ReadInvoice` / several invoice mutations return bare `Invoice`; `DelayedCancellationResponse` is `{ Message }` not a subscription. Treating all responses as the inner resource type fails at compile or NRE. **MUST load `dotnet-calling-endpoints`** and **`dotnet-models`**.

⚠ Steps 4–8 (models) — enums are `StringEnum<T>` (`CollectionMethod.Automatic`, not a C# enum); payment profile and id/month unions have **no object initializer** — factories + `TryGet…`. Unmodeled JSON is dropped. **MUST load `dotnet-models`** before building `CreateSubscriptionRequest` / reading `PaymentProfileResponse`.

⚠ Step 5 (PCI) — `CreatePaymentProfile` / `CreateSubscription` accept `FullNumber`; the operations map says collecting raw cards in production requires PCI and to use Maxio.js (`chargify_token`) otherwise. Sending PAN from eShopOnWeb is a compliance failure even if the call compiles.

⚠ Step 2 / all calls (error boundary) — Case A vs Case B **differs per operation** (CreateCustomer A, ReadCustomer B, CreateSubscription A, ReadSubscription B, ListInvoices B, ReadPaymentProfile A). A single `catch (SdkException<RawError>)` misses typed 422s; `TryGetRawError` is not a catch-all on the wrong `TError`. This SDK has **no** no-throw variants. **MUST load `dotnet-error-handling`** before any try/catch.

⚠ Step 2 (error boundary) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 2 (error boundary) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 12 (tests) — the seam is the `HttpClient` argument, not internal handlers or a fake `Customers` controller. **MUST load `dotnet-testing`** before stubbing.

---

## REQUIRED READING

Load **before implementation starts**. This sheet does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — construct/register `MaxioAdvancedBillingClient`, `HttpClient` lifetime, `AddMaxioAdvancedBillingClient` |
| `dotnet-authentication` | Step 1 — `BasicAuthCredentials` (`Username` = API key, `Password` = `"x"`), config not literals |
| `dotnet-configuration-resilience` | Step 1 — `RetryOptions`, `Timeout`, `Server.Production.*.Site`/`BaseUrl`, list pagination |
| `dotnet-calling-endpoints` | Steps 3–11 — named args, `ct:`, envelopes, async throw-only calls |
| `dotnet-models` | Steps 3–11 — `required`/nullable records, `StringEnum<T>`, OneOf/AnyOf factories + `TryGet…` |
| `dotnet-error-handling` | Step 2 — Case A/B catch ladder, `TryGet…` vs `RawError`, **both** `JsonException` paths (2xx deserialize **and** non-2xx error-shape mismatch) |
| `dotnet-testing` | Step 12 — `HttpClient` + `HttpMessageHandler` seam |

---

## Assumptions & Blockers

**Assumptions**

- eShopOnWeb is the ASP.NET Core storefront; Maxio types stay behind an application-layer billing service (Web project registers DI; Razor/API endpoints do not take SDK models).
- Catalog products/price points already exist in the Maxio site (Admin UI). The app **lists/reads** them; it does not `CreateProduct` at checkout.
- One Maxio customer per eShop buyer, keyed by `Customer.Reference` = stable eShop user id.
- Cards are tokenized with Maxio.js / Chargify.js (`chargify_token`); the server never stores PAN/CVV.
- v1 is a single product subscription (no components, groups, coupons, or prepaid wallets).
- Immediate plan changes use `SubscriptionProducts` migration; end-of-term changes use `UpdateSubscription.ProductChangeDelayed`.
- Hosting is `ServerEnvironment.Us` unless configuration says EU.
- Invoice UX is list + detail (`Uid`, amounts, `PublicUrl`); merchant ad-hoc invoice/refund/void is out of scope.

**Blockers**

- Site subdomain, API key, US vs EU, and (for JS tokens) the public Chargify.js key / gateway must be supplied via configuration — none of those values are in the SDK map.
- Product **handles** (or ids) and default price-point handles for the plans the storefront will sell must be known (or discovered via `ListProducts` against the live site).
- Whether the site uses Relationship Invoicing vs legacy statements, and whether products `RequireCreditCard`, is site configuration — `CreateSubscription` payment requirements follow the product, not this sheet.

---

## Revision — PublicApi JWT slice (no payment profiles / invoices / plan change / cancel)

Context: GET `/api/subscription-plans`, POST `/api/subscriptions`, GET `/api/my-subscriptions`. Catalog already seeded; payment method **not** required on products. Bind `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, optional `Maxio:BaseUrl`.

| Topic | Fact | Cite |
|---|---|---|
| Catalog list | Use `ProductFamilies.ListProductsForProductFamily` with `productFamilyId: "handle:" + ProductFamilyHandle` (not bare handle; not `ListProducts` + client-side `Product.ProductFamily.Handle` filter). Leading query params after id **must pass explicitly** (`null`). Envelope: `IReadOnlyList<ProductResponse>` → `.Product`. Case A 404: `TryGetString(out string)`. | `operations/ProductFamilies.md` · `Api/ProductFamilies.cs` param docs |
| CreateSubscription without payment | All inner `CreateSubscription` members are nullable (`WhenWritingNull`). **C# `required` is only** `CreateSubscriptionRequest.Subscription`. API-required: `ProductHandle` **or** `ProductId`; customer via `CustomerId` **or** `CustomerReference` **or** `CustomerAttributes`. Omit `PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes` entirely (leave null) when the product does not require a payment method. Optional `Reference` is the **subscription** key, not the customer. `AgreementAcceptance` is required only when creating with Maxio Payments — omit for this slice. | `Models/CreateSubscription.cs` · `Models/CreateSubscriptionRequest.cs` · `operations/Subscriptions.md` |
| FindSubscription idempotency | `FindSubscription(string? reference, ct = default)` — `reference` **must pass explicitly**. Case A: `TryGetNoContent(out RawError)` [404] then `CreateSubscription`. **No uniqueness constraint is declared** on `Subscription.Reference` / `CreateSubscription.Reference` (unlike `Customer.Reference`). XML: “The reference value (provided by your app) for the subscription itself.” Duplicate-reference create behavior is **UNVERIFIED** — app must use a unique string (e.g. eShop user + product handle); do not assume the API rejects collisions. | `operations/Subscriptions.md` · `Models/CreateSubscription.cs` · `Api/Subscriptions.cs` |
