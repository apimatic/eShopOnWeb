# Maxio subscription billing — eShopOnWeb

NuGet: `AsadAli.AdvancedBilling.Sdk` · Root namespace: `MaxioAdvancedBilling` · Client: `MaxioAdvancedBillingClient` · Stamp: map `v1.0.2` / `15db14b`.

Install with `dotnet add package AsadAli.AdvancedBilling.Sdk` on the Web/Infrastructure project that will own the billing seam. Do **not** project-reference SDK source.

---

## 1. Scope & sequence

| Step | What | Operations |
|---|---|---|
| 1 | Register client + Basic auth + site subdomain from configuration | constructor / `AddMaxioAdvancedBillingClient` |
| 2 | Sync eShop Identity users ↔ Maxio customers (`reference` = eShop user id) | `Customers.CreateCustomer`, `ReadCustomerByReference`, `UpdateCustomer`, `ListCustomerSubscriptions` |
| 3 | Load Maxio products as the subscription catalog (plans) | `Products.ListProducts`, `ReadProduct`, `ReadProductByHandle` |
| 4 | Store a card via Chargify.js one-time token (no raw PAN in this app) | `PaymentProfiles.CreatePaymentProfile`, `ListPaymentProfiles`, `ChangeSubscriptionDefaultPaymentProfile` |
| 5 | Preview then create a subscription; read / find / list | `Subscriptions.PreviewSubscription`, `CreateSubscription`, `ReadSubscription`, `FindSubscription`, `ListSubscriptions` |
| 6 | Update payment method / delayed product change / next billing | `Subscriptions.UpdateSubscription` |
| 7 | Cancel (immediate or end-of-period), undo delayed cancel, reactivate, pause/resume, retry past-due | `SubscriptionStatus.CancelSubscription`, `InitiateDelayedCancellation`, `CancelDelayedCancellation`, `ReactivateSubscription`, `PauseSubscription`, `ResumeSubscription`, `RetrySubscription` |
| 8 | Plan change with proration preview then migrate | `SubscriptionProducts.PreviewSubscriptionProductMigration`, `MigrateSubscriptionProduct` |
| 9 | Catalog components + allocate quantity / record metered usage | `Components.ListComponents`, `FindComponent`; `SubscriptionComponents.ListSubscriptionComponents`, `PreviewAllocations`, `AllocateComponent`, `CreateUsage`, `ListUsages` |
| 10 | List/read/email invoices for a subscription | `Invoices.ListInvoices`, `ReadInvoice`, `SendInvoice` |
| 11 | Validate a checkout code; apply/remove on an existing subscription | `Coupons.ValidateCoupon`, `FindCoupon`; `Subscriptions.ApplyCouponsToSubscription`, `RemoveCouponFromSubscription` |
| 12 | Integration error boundary + tests of the billing seam | (no extra operations) |

Out of this shop-facing scope (do **not** implement unless a later brief asks): catalog admin (`CreateProduct` / `CreateCoupon` / `CreateMeteredComponent`), test-mode `PurgeSubscription`, import `OverrideSubscription`, events-based billing (`RecordEvent` / Ebb server), subscription groups, inbound HTTP webhook receiver (the `Webhooks` controller only manages endpoint registration).

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

### 2.1 Client construction & auth

| Fact | Value | Source |
|---|---|---|
| Package id | `AsadAli.AdvancedBilling.Sdk` | `sdk-map.md` |
| Client | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` | `sdk-map.md` |
| Only constructor | `MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` |
| DI | `AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>)` on `IServiceCollection` | `sdk-map.md` (`ServiceCollectionExtensions.cs`) |
| Options | `Environment`: `MaxioAdvancedBilling.Servers.ServerEnvironment`; `Retry`: `MaxioAdvancedBilling.Core.Configuration.RetryOptions`; `Server`; `BasicAuth`: `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md` |
| Auth | HTTP Basic. `BasicAuthCredentials.Username` = API key, `Password` = literal `"x"` | `sdk-map.md` |
| Environments | `ServerEnvironment.Us` (default) → `https://{site}.chargify.com`; `ServerEnvironment.Eu` → `https://{site}.ebilling.maxio.com` | `sdk-map.md` |
| Site | `{site}` defaults to `subdomain`. Set `options.Server.Production.Us.Site` (or `.Eu.Site`) to the Chargify/Maxio subdomain. Mock host: `options.Server.Production.Us.BaseUrl` | `sdk-map.md` |
| RetryOptions | namespace `MaxioAdvancedBilling.Core.Configuration`; all members `required`; start from `RetryOptions.Default()` or set every member: `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry` | `sdk-map.md` |
| Throw model | Every operation is throw-only. Errors are `MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>`. Case A: `TError` in `MaxioAdvancedBilling.Errors` with `TryGet…`. Case B: `TError` is `MaxioAdvancedBilling.Core.ErrorResponse.RawError` (`StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`). Inherited fallback on typed errors: `ApiError.TryGetRawError(out RawError)` | `sdk-map.md` |

Config keys the host must supply: API key, site subdomain, `Us` vs `Eu`.

### 2.2 Operations

#### Step 2 — Customers (`client.Customers`) — `operations/Customers.md` · `Api/Customers.cs`

| Method | Signature (must-pass-explicitly) | Request | Response envelope → read | Error | Pagination |
|---|---|---|---|---|---|
| `CreateCustomer` | `CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, CancellationToken ct = default)` · `body` nullable **must pass** | Envelope `CreateCustomerRequest`: `Customer (customer): CreateCustomer !req`. Inner `CreateCustomer` **required**: `FirstName (first_name): string`, `LastName (last_name): string`, `Email (email): string`. Optional: `Reference (reference): string?` (set to eShop user id), `Organization`, `Address`, `Address2`, `City`, `State`, `Zip`, `Country` (ISO-3166-1 alpha-2), `Phone`, `CcEmails`, `Locale`, `VatNumber`, `TaxExempt`, `TaxExemptReason`, `ParentId`, `SalesforceId` | `CustomerResponse` → `Customer (customer): Customer !req`. Read `Id`, `Reference`, `Email`, `FirstName`, `LastName` | **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)`. Payload `CustomerErrorResponse1.Errors (errors): Errors?` (`Errors` record: `PerPage`, `PricePoint`). **UNVERIFIED vs live 422 body** — if `TryGetCustomerErrorResponse1` is false, extract best-effort via `TryGetRawError` / `ReadAsString()` | none |
| `ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | query `reference` ← `reference` (eShop user id) | `CustomerResponse` → `.Customer` | **Case B** `SdkException<RawError>` | none |
| `ReadCustomer` | `ReadCustomer(int id, CancellationToken ct = default)` | path id | `CustomerResponse` → `.Customer` | **Case B** `SdkException<RawError>` | none |
| `UpdateCustomer` | `UpdateCustomer(int id, UpdateCustomerRequest? body, CancellationToken ct = default)` · `body` **must pass** | Envelope `UpdateCustomerRequest`: `Customer (customer): UpdateCustomer !req`. Inner fields all optional (`FirstName`, `LastName`, `Email`, `Reference`, address fields, `Phone`, `Locale`, `VatNumber`, `TaxExempt`, …) | `CustomerResponse` → `.Customer` | **Case A** `SdkException<UpdateCustomerError>`: `TryGetNoContent(out RawError)` [404] · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError` | none |
| `ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | path | `IReadOnlyList<SubscriptionResponse>` — each `.Subscription` | **Case B** `SdkException<RawError>` | none |

`Customer` (`records-2-Cr-Ne.md`): `Id (id): int?`, `Reference (reference): string?`, `FirstName`, `LastName`, `Email`, `Organization`, `Address`, `Address2`, `City`, `State`, `Zip`, `Country`, `Phone`, `CreatedAt`, `UpdatedAt`, `Locale`, `VatNumber`, `TaxExempt`, `Maxioid`.

Ensure-customer flow: `ReadCustomerByReference(reference)`; on Case B 404, `CreateCustomer` with the same `reference`. Do not create two customers with the same `reference` (API uniqueness).

#### Step 3 — Products (`client.Products`) — `operations/Products.md` · `Api/Products.cs`

| Method | Signature | Request | Response envelope → read | Error | Pagination |
|---|---|---|---|---|---|
| `ListProducts` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` · first **8** params **must pass** (`null` to skip) | query: `date_field`, `filter`, `end_date`, `end_datetime`, `start_date`, `start_datetime`, `page`, `per_page`, `include_archived`, `include` | `IReadOnlyList<ProductResponse>` → each `.Product` | **Case B** `SdkException<RawError>` | manual `page`+`perPage` (default 20) |
| `ReadProduct` | `ReadProduct(int productId, CancellationToken ct = default)` | path | `ProductResponse` → `Product (product): Product !req` | **Case B** | none |
| `ReadProductByHandle` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | path `api_handle` | `ProductResponse` → `.Product` | **Case B** | none |

`Product` (`records-3-Of-Su.md`) fields the shop reads: `Id`, `Name`, `Handle`, `Description`, `PriceInCents`, `Interval`, `IntervalUnit`, `TrialPriceInCents`, `TrialInterval`, `TrialIntervalUnit`, `RequireCreditCard`, `ProductFamily` (`Id`/`Name`/`Handle`), `ProductPricePointId`, `ProductPricePointHandle`, `ArchivedAt`. Envelope field is `Product`, not a flat product.

`ListProductsFilter` (`records-2-Cr-Ne.md`): `Ids (ids): IReadOnlyList<int>?`, `PrepaidProductPricePoint`, `UseSiteExchangeRate`.

#### Step 4 — Payment profiles (`client.PaymentProfiles`) — `operations/PaymentProfiles.md` · `Api/PaymentProfiles.cs`

| Method | Signature | Request | Response envelope → read | Error | Pagination |
|---|---|---|---|---|---|
| `CreatePaymentProfile` | `CreatePaymentProfile(CreatePaymentProfileRequest? body, CancellationToken ct = default)` · `body` **must pass** | Envelope: `PaymentProfile (payment_profile): CreatePaymentProfile !req`. Set `CustomerId (customer_id): int?` + `ChargifyToken (chargify_token): string?` from Chargify.js. Optional billing address fields. Do **not** send `FullNumber`/`Cvv` from this app. | `PaymentProfileResponse` → `PaymentProfile (payment_profile): PaymentProfile !req` **(union)** | **Case A** `SdkException<CreatePaymentProfileError>`: `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` | none |
| `ListPaymentProfiles` | `ListPaymentProfiles(int? customerId, int? page = 1, int? perPage = 20, CancellationToken ct = default)` · `customerId` **must pass** | query `customer_id`, `page`, `per_page` | `IReadOnlyList<PaymentProfileResponse>` | **Case B** | manual `page`+`perPage` |
| `ChangeSubscriptionDefaultPaymentProfile` | `ChangeSubscriptionDefaultPaymentProfile(int subscriptionId, int paymentProfileId, CancellationToken ct = default)` | path | `PaymentProfileResponse` → union `.PaymentProfile` | **Case A** `SdkException<ChangeSubscriptionDefaultPaymentProfileError>`: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `ReadPaymentProfile` | `ReadPaymentProfile(int paymentProfileId, CancellationToken ct = default)` | path | `PaymentProfileResponse` | **Case A** `SdkException<ReadPaymentProfileError>`: `TryGetNoContent` [404] · `TryGetRawError` | none |

`PaymentProfile` union (`unions.md`, `Models/OneOf/PaymentProfile.cs`): factories `PaymentProfile.CreditCardPaymentProfile(…)`, `.BankAccountPaymentProfile`, `.PaypalPaymentProfile`, `.ApplePayPaymentProfile`. Read with `TryGetCreditCardPaymentProfile(out CreditCardPaymentProfile)`. Credit-card variant fields to show: `Id`, `MaskedCardNumber`, `CardType`, `ExpirationMonth`, `ExpirationYear`, `CustomerId`, `PaymentType` (default `PaymentType.CreditCard`).

Creating a profile does **not** attach it to a subscription; call `ChangeSubscriptionDefaultPaymentProfile` (or pass `payment_profile_id` on create).

#### Step 5–6 — Subscriptions (`client.Subscriptions`) — `operations/Subscriptions.md` · `Api/Subscriptions.cs`

| Method | Signature | Request | Response envelope → read | Error | Pagination |
|---|---|---|---|---|---|
| `PreviewSubscription` | `PreviewSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` · `body` **must pass** | Same envelope as create (no card required for tax-off preview) | `SubscriptionPreviewResponse` → `SubscriptionPreview (subscription_preview): SubscriptionPreview !req` → `CurrentBillingManifest` / `NextBillingManifest` (`TotalInCents`, `SubtotalInCents`, `TotalTaxInCents`, `TotalDiscountInCents`, `StartDate`, `EndDate`, `LineItems`) | **Case B** `SdkException<RawError>` | none |
| `CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` · `body` **must pass** | Envelope: `Subscription (subscription): CreateSubscription !req`. Identify product: `ProductId` **or** `ProductHandle`; optional `ProductPricePointId` / `ProductPricePointHandle`. Identify customer: `CustomerId` **or** `CustomerReference` (eShop user id) **or** nested `CustomerAttributes`. Payment: `PaymentProfileId` **or** `PaymentProfileAttributes.ChargifyToken`. Optional: `CouponCode` / `CouponCodes`, `Reference` (eShop subscription id), `Components`, `PaymentCollectionMethod`, `AgreementAcceptance` (required for Maxio Payments). | `SubscriptionResponse` → `Subscription (subscription): Subscription?`. Read `Id`, `State`, `Reference`, `Product`, `Customer`, `CurrentPeriodEndsAt`, `NextAssessmentAt`, `BalanceInCents`, `CouponCodes`, `CreditCard` | **Case A** `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError`. 422 may include a 3DS `action_link` — **UNVERIFIED** on the generated `ErrorListResponse1` (`Errors: IReadOnlyList<string>`); if the accessor misses it, `TryGetRawError` + `ReadAsString()` / `ReadAsJson` | none |
| `ReadSubscription` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` · `include` **must pass** | query `include` | `SubscriptionResponse` → `.Subscription` | **Case B** `SdkException<RawError>` | none |
| `FindSubscription` | `FindSubscription(string? reference, CancellationToken ct = default)` · `reference` **must pass** | query `reference` | `SubscriptionResponse` → `.Subscription` | **Case A** `SdkException<FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError` | none |
| `ListSubscriptions` | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` · first **14** params **must pass** | Prefer `ListCustomerSubscriptions` for a shopper. Site-wide list uses named args; pass `null` for unused filters. | `IReadOnlyList<SubscriptionResponse>` | **Case B** | manual `page`+`perPage` (default 20) |
| `UpdateSubscription` | `UpdateSubscription(int subscriptionId, UpdateSubscriptionRequest? body, CancellationToken ct = default)` · `body` **must pass** | Envelope: `Subscription (subscription): UpdateSubscription !req`. Payment: `CreditCardAttributes`. Product change (no proration): `ProductHandle`/`ProductId` + optional `ProductChangeDelayed`. Delayed-change cancel: `NextProductId` empty string. Billing: `NextBillingAt`. | `SubscriptionResponse` → `.Subscription` | **Case A** `SdkException<UpdateSubscriptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `ApplyCouponsToSubscription` | `ApplyCouponsToSubscription(int subscriptionId, string? code, AddCouponsRequest? body, CancellationToken ct = default)` · `code` and `body` **must pass** | Pass `code: null`. Body `AddCouponsRequest.Codes (codes): IReadOnlyList<string>?` **adds** codes. Query `code` **replaces** all existing codes (deprecated). | `SubscriptionResponse` → `.Subscription` (`CouponCodes`) | **Case A** `SdkException<ApplyCouponsToSubscriptionError>`: `TryGetSubscriptionAddCouponError1(out SubscriptionAddCouponError1)` [422] (`Codes`/`CouponCode`/`CouponCodes`/`Subscription` string lists) · `TryGetRawError` | none |
| `RemoveCouponFromSubscription` | `RemoveCouponFromSubscription(int subscriptionId, string? couponCode, CancellationToken ct = default)` · `couponCode` **must pass** | query `coupon_code` ← `couponCode` | `string` | **Case A** `SdkException<RemoveCouponFromSubscriptionError>`: `TryGetSubscriptionRemoveCouponErrors1(out SubscriptionRemoveCouponErrors1)` [422] (`Subscription: IReadOnlyList<string>`) · `TryGetRawError` | none |

`CreateSubscription` fields used at signup (`records-2-Cr-Ne.md`): `ProductHandle`, `ProductId`, `ProductPricePointHandle`, `ProductPricePointId`, `CouponCode`, `CouponCodes`, `PaymentCollectionMethod`, `CustomerId`, `PaymentProfileId`, `Reference`, `CustomerAttributes`, `PaymentProfileAttributes`, `CreditCardAttributes` (alias of payment-profile attrs), `Components` (`CreateSubscriptionComponent`: `ComponentId` union, `Enabled`, `AllocatedQuantity` union, `Quantity`, `UnitBalance`, `PricePointId` union), `CustomerReference`, `AgreementAcceptance` (`IpAddress`, `TermsUrl`, …).

`CustomerAttributes` (`records-2-Cr-Ne.md`): `FirstName`, `LastName`, `Email`, `Reference`, address, `Phone`, `VatNumber`, `TaxExempt`, `Metafields`.

`Subscription` (`records-3-Of-Su.md`) — inner payload (nullable on the envelope): `Id`, `State`, `BalanceInCents`, `ProductPriceInCents`, `CurrentPeriodEndsAt`, `CurrentPeriodStartedAt`, `NextAssessmentAt`, `TrialStartedAt`, `TrialEndedAt`, `ActivatedAt`, `CanceledAt`, `CancelAtEndOfPeriod`, `DelayedCancelAt`, `CouponCode`, `CouponCodes`, `PaymentCollectionMethod`, `Customer`, `Product`, `CreditCard`, `Reference`, `ProductPricePointId`, `NextProductId`, `AutomaticallyResumeAt`, `Currency`, `CreditBalanceInCents`, `PrepaymentBalanceInCents`, `SelfServicePageToken` (only if `include` asked).

#### Step 7 — Subscription status (`client.SubscriptionStatus`) — `operations/SubscriptionStatus.md` · `Api/SubscriptionStatus.cs`

| Method | Signature | Request | Response envelope → read | Error | Pagination |
|---|---|---|---|---|---|
| `CancelSubscription` | `CancelSubscription(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` · `body` **must pass** | Envelope: `Subscription (subscription): CancellationOptions !req`. Options: `CancellationMessage`, `ReasonCode`, `CancelAtEndOfPeriod`, `ScheduledCancellationAt`, `RefundPrepaymentAccountBalance`. Immediate cancel: pass body with empty/default options (omit schedule fields). | `SubscriptionResponse` → `.Subscription.State` | **Case A** `SdkException<CancelSubscriptionApiError>`: `TryGetNoContent` [404] · `TryGetCancelSubscriptionErrorResponse(out CancelSubscriptionErrorResponse)` [422] · `TryGetRawError`. `CancelSubscriptionErrorResponse` **union**: `TryGetErrorListResponse1` / `TryGetSingleErrorResponse1` (`Error (error): string`) | none |
| `InitiateDelayedCancellation` | `InitiateDelayedCancellation(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` · `body` **must pass** | same `CancellationRequest` | `DelayedCancellationResponse` → `Message (message): string?` | **Case A** `SdkException<InitiateDelayedCancellationError>`: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `CancelDelayedCancellation` | `CancelDelayedCancellation(int subscriptionId, CancellationToken ct = default)` | path | `DelayedCancellationResponse` → `.Message` | **Case A** `SdkException<CancelDelayedCancellationError>`: `TryGetNoContent` [404] · `TryGetRawError` | none |
| `ReactivateSubscription` | `ReactivateSubscription(int subscriptionId, ReactivateSubscriptionRequest? body, CancellationToken ct = default)` · `body` **must pass** | `IncludeTrial`, `PreserveBalance`, `CouponCode`, `UseCreditsAndPrepayments`, `Resume` **union** (`Resume.Bool(true)` to resume same period), `CalendarBilling.ReactivationCharge` | `SubscriptionResponse` → `.Subscription` | **Case A** `SdkException<ReactivateSubscriptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `PauseSubscription` | `PauseSubscription(int subscriptionId, PauseRequest? body, CancellationToken ct = default)` · `body` **must pass** | `PauseRequest.Hold (hold): AutoResume?` (`AutomaticallyResumeAt`). Pass `null` body only if the signature is given `null` explicitly. Cannot pause if `next_billing_at` is within 24h. | `SubscriptionResponse` → `.Subscription` (`OnHoldAt`, `AutomaticallyResumeAt`) | **Case A** `SdkException<PauseSubscriptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `ResumeSubscription` | `ResumeSubscription(int subscriptionId, ResumptionCharge? calendarBillingResumptionCharge, CancellationToken ct = default)` · `calendarBillingResumptionCharge` **must pass** (`null` if not calendar billing) | query `calendar_billing['resumption_charge']` | `SubscriptionResponse` | **Case A** `SdkException<ResumeSubscriptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `RetrySubscription` | `RetrySubscription(int subscriptionId, CancellationToken ct = default)` | path | `SubscriptionResponse` | **Case A** `SdkException<RetrySubscriptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |

#### Step 8 — Plan change (`client.SubscriptionProducts`) — `operations/SubscriptionProducts.md` · `Api/SubscriptionProducts.cs`

| Method | Signature | Request | Response envelope → read | Error | Pagination |
|---|---|---|---|---|---|
| `PreviewSubscriptionProductMigration` | `PreviewSubscriptionProductMigration(int subscriptionId, SubscriptionMigrationPreviewRequest? body, CancellationToken ct = default)` · `body` **must pass** | Envelope: `Migration (migration): SubscriptionMigrationPreviewOptions !req`. `ProductId` **or** `ProductHandle`; optional `ProductPricePointId`/`Handle`, `IncludeTrial` (default false), `IncludeInitialCharge` (false), `IncludeCoupons` (true), `PreservePeriod` (false), `Proration.PreservePeriod`, `ProrationDate` | `SubscriptionMigrationPreviewResponse` → `Migration (migration): SubscriptionMigrationPreview !req`: `ProratedAdjustmentInCents`, `ChargeInCents`, `PaymentDueInCents`, `CreditAppliedInCents` | **Case A** `SdkException<PreviewSubscriptionProductMigrationError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `MigrateSubscriptionProduct` | `MigrateSubscriptionProduct(int subscriptionId, SubscriptionProductMigrationRequest? body, CancellationToken ct = default)` · `body` **must pass** | Envelope: `Migration (migration): SubscriptionProductMigration !req`. Same product identifiers + `IncludeTrial`/`IncludeInitialCharge`/`IncludeCoupons`/`PreservePeriod`/`Proration`. Subscription must be `active` or `trialing`. Migrating to the **current** product is a common 422. | `SubscriptionResponse` → `.Subscription.Product` | **Case A** `SdkException<MigrateSubscriptionProductError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |

Use this pair for prorated upgrades/downgrades. `UpdateSubscription` product fields are delayed/next-period only (no proration).

#### Step 9 — Components catalog (`client.Components`) — `operations/Components.md` · `Api/Components.cs`

| Method | Signature | Request | Response envelope → read | Error | Pagination |
|---|---|---|---|---|---|
| `ListComponents` | `ListComponents(BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, bool? includeArchived, ListComponentsFilter? filter, int? page = 1, int? perPage = 20, CancellationToken ct = default)` · first **7** **must pass** | query `date_field`, `start_date`, `end_date`, `start_datetime`, `end_datetime`, `include_archived`, `page`, `per_page`, `filter` | `IReadOnlyList<ComponentResponse>` → `Component (component): Component !req` | **Case B** | manual `page`+`perPage` |
| `FindComponent` | `FindComponent(string handle, CancellationToken ct = default)` | query `handle` | `ComponentResponse` → `.Component` | **Case B** | none |

`Component` fields to read: `Id`, `Name`, `Handle`, `Kind`, `UnitName`, `UnitPrice`, `PricingScheme`, `Recurring`, `ProductFamilyId`, `Archived`.

#### Step 9 — Subscription components / usage (`client.SubscriptionComponents`) — `operations/SubscriptionComponents.md` · `Api/SubscriptionComponents.cs`

| Method | Signature | Request | Response envelope → read | Error | Pagination |
|---|---|---|---|---|---|
| `ListSubscriptionComponents` | `ListSubscriptionComponents(int subscriptionId, SubscriptionListDateField? dateField, SortingDirection? direction, ListSubscriptionComponentsFilter? filter, string? endDate, string? endDatetime, IncludeNotNull? pricePointIds, IReadOnlyList<int>? productFamilyIds, ListSubscriptionComponentsSort? sort, string? startDate, string? startDatetime, IReadOnlyList<ListSubscriptionComponentsInclude>? include, bool? inUse, CancellationToken ct = default)` · **12** optionals **must pass** (`null` to skip) | named args | `IReadOnlyList<SubscriptionComponentResponse>` → `Component (component): SubscriptionComponent?` | **Case B** | none |
| `PreviewAllocations` | `PreviewAllocations(int subscriptionId, PreviewAllocationsRequest? body, CancellationToken ct = default)` · `body` **must pass** | `Allocations (allocations): IReadOnlyList<CreateAllocation> !req`; optional `EffectiveProrationDate`, `UpgradeCharge`, `DowngradeCredit` | `AllocationPreviewResponse` → `AllocationPreview (allocation_preview): AllocationPreview !req` (`TotalInCents`, `Direction`, `LineItems`) | **Case A** `SdkException<PreviewAllocationsError>`: `TryGetComponentAllocationError1(out ComponentAllocationError1)` [422] · `TryGetRawError` | none |
| `AllocateComponent` | `AllocateComponent(int subscriptionId, int componentId, CreateAllocationRequest? body, CancellationToken ct = default)` · `body` **must pass** | Envelope: `Allocation (allocation): CreateAllocation !req`. `Quantity (quantity): double !req`. Optional `Memo`, `UpgradeCharge`, `DowngradeCredit`, `AccrueCharge`, `PricePointId` union, `ComponentId`. Quantity / on-off / prepaid only — **not** metered. | `AllocationResponse` → `Allocation (allocation): Allocation?` (`Quantity` union, `PreviousQuantity` union, `ComponentId`) | **Case A** `SdkException<AllocateComponentError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `CreateUsage` | `CreateUsage(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, CreateUsageRequest? body, CancellationToken ct = default)` · `body` **must pass** | Envelope: `Usage (usage): CreateUsage !req`. `Quantity (quantity): double?` (negative deducts; floor 0), `Memo`, `PricePointId (price_point_id): string?`. One component per call. | `UsageResponse` → `Usage (usage): Usage !req` (`Id`, `Quantity` union, `ComponentId`, `SubscriptionId`) | **Case A** `SdkException<CreateUsageError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `ListUsages` | `ListUsages(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, long? sinceId, long? maxId, DateTimeOffset? sinceDate, DateTimeOffset? untilDate, int? page = 1, int? perPage = 20, CancellationToken ct = default)` · four filters **must pass** | query `since_id`, `max_id`, `since_date`, `until_date`, `page`, `per_page` | `IReadOnlyList<UsageResponse>` | **Case B** | manual `page`+`perPage` |
| `ReadSubscriptionComponent` | `ReadSubscriptionComponent(int subscriptionId, int componentId, CancellationToken ct = default)` | path | `SubscriptionComponentResponse` → `.Component` (`UnitBalance`, `AllocatedQuantity` union, `Kind`, `Enabled`) | **Case A** `SdkException<ReadSubscriptionComponentError>`: `TryGetNoContent` [404] · `TryGetRawError` | none |

Unions (`unions.md`, `MaxioAdvancedBilling.Models.AnyOf`):

- `SubscriptionIdOrReference.Int(int)` / `.String(string)` — pass the numeric id via `.Int(subscriptionId)`.
- `ComponentIdModel.Int(int)` / `.String(string)` — handle form is the string `"handle:{handle}"` when using the string variant (operation notes).
- `Quantity` / `Quantity1` / `AllocatedQuantity2`: `Int` or `String` — always `TryGetInt` then `TryGetString`.

Metered vs quantity: `CreateUsage` for `ComponentKind.MeteredComponent` / prepaid usage; `AllocateComponent` for `QuantityBasedComponent` / `OnOffComponent` / prepaid allocation.

#### Step 10 — Invoices (`client.Invoices`) — `operations/Invoices.md` · `Api/Invoices.cs`

| Method | Signature | Request | Response envelope → read | Error | Pagination |
|---|---|---|---|---|---|
| `ListInvoices` | `ListInvoices(string? startDate, string? endDate, InvoiceStatus? status, int? subscriptionId, string? subscriptionGroupUid, string? consolidationLevel, Direction? direction, InvoiceDateField? dateField, string? startDatetime, string? endDatetime, IReadOnlyList<int>? customerIds, IReadOnlyList<string>? number, IReadOnlyList<int>? productIds, InvoiceSortField? sort, int? page = 1, int? perPage = 20, bool? lineItems = false, bool? discounts = false, bool? taxes = false, bool? credits = false, bool? payments = false, bool? customFields = false, bool? refunds = false, CancellationToken ct = default)` · first **14** **must pass** | Shopper history: `subscriptionId:` or `customerIds:`. Set `lineItems: true` (etc.) when the UI needs breakdowns — defaults are **false** (totals only). | `ListInvoicesResponse` → `Invoices (invoices): IReadOnlyList<Invoice> !req` (not an `InvoiceResponse` wrapper per row) | **Case B** | manual `page`+`perPage` (default 20) |
| `ReadInvoice` | `ReadInvoice(string uid, CancellationToken ct = default)` | path `uid` (invoice uid string, not numeric id) | `Invoice` (unwrapped) | **Case B** | none |
| `SendInvoice` | `SendInvoice(string uid, SendInvoiceRequest? body, CancellationToken ct = default)` · `body` **must pass** | `RecipientEmails`, `CcRecipientEmails`, `BccRecipientEmails`, `AttachmentUrls`. Empty recipients → subscription default email. Success is 204 / `Task`. | `void` | **Case A** `SdkException<SendInvoiceError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |

`Invoice` (`records-2-Cr-Ne.md`) fields the UI reads: `Uid`, `Id`, `Number`, `Status`, `IssueDate`, `DueDate`, `PaidDate`, `Currency`, `TotalAmount`, `DueAmount`, `PaidAmount`, `SubtotalAmount`, `DiscountAmount`, `TaxAmount`, `PublicUrl`, `SubscriptionId`, `CustomerId`, `LineItems` (only if requested on list). Amounts are **strings**, not cents.

#### Step 11 — Coupons catalog (`client.Coupons`) — `operations/Coupons.md` · `Api/Coupons.cs`

| Method | Signature | Request | Response envelope → read | Error | Pagination |
|---|---|---|---|---|---|
| `ValidateCoupon` | `ValidateCoupon(string code, int? productFamilyId, CancellationToken ct = default)` · `productFamilyId` **must pass** (`null` if the site’s first/default family is correct) | query `code`, `product_family_id` | `CouponResponse` → `Coupon (coupon): Coupon?` | **Case A** `SdkException<ValidateCouponError>`: `TryGetSingleStringErrorResponse1(out SingleStringErrorResponse1)` [404] (`Errors (errors): string?`) · `TryGetRawError` | none |
| `FindCoupon` | `FindCoupon(int? productFamilyId, string? code, bool? currencyPrices, CancellationToken ct = default)` · all three **must pass** | query `product_family_id`, `code`, `currency_prices` | `CouponResponse` → `.Coupon` | **Case B** (404 if missing) | none |

`Coupon` (`records-1-Ac-Cr.md`) to display: `Id`, `Name`, `Code`, `Description`, `Amount`/`AmountInCents`, `Percentage` (string), `Recurring`, `RecurringScheme`, `Stackable`, `CompoundingStrategy`, `StartDate`, `EndDate`, `ArchivedAt`, `ProductFamilyId`.

Checkout: `ValidateCoupon` then either `CreateSubscription.CouponCode`/`CouponCodes` or `ApplyCouponsToSubscription` with `body.Codes` and `code: null`.

### 2.3 Enums in scope (`map/models/enums.md` · namespace `MaxioAdvancedBilling.Models.Enums`)

These are `StringEnum<T>` records, **not** C# enums. Write `CollectionMethod.Automatic`, or `CollectionMethod.FromValue("automatic")`.

| Enum | Members (C# identifier · wire) |
|---|---|
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` |
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `SubscriptionStateFilter` | `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)` |
| `SubscriptionDateField` | `CurrentPeriodEndsAt (current_period_ends_at)`, `CurrentPeriodStartsAt (current_period_starts_at)`, `CreatedAt (created_at)`, `ActivatedAt (activated_at)`, `CanceledAt (canceled_at)`, `ExpiresAt (expires_at)`, `TrialStartedAt (trial_started_at)`, `TrialEndedAt (trial_ended_at)`, `UpdatedAt (updated_at)` |
| `SubscriptionSort` | `SignupDate (signup_date)`, `PeriodStart (period_start)`, `PeriodEnd (period_end)`, `NextAssessment (next_assessment)`, `UpdatedAt (updated_at)`, `CreatedAt (created_at)`, `TotalPayments (total_payments)`, `Id (id)`, `OpenBalance (open_balance)`, `ExpiresAt (expires_at)` |
| `SubscriptionInclude` | `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)` |
| `SubscriptionListInclude` | `SelfServicePageToken (self_service_page_token)` |
| `SortingDirection` / `Direction` | `Asc (asc)`, `Desc (desc)` |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` |
| `IntervalUnit` | `Day (day)`, `Month (month)` |
| `ComponentKind` | `MeteredComponent (metered_component)`, `QuantityBasedComponent (quantity_based_component)`, `OnOffComponent (on_off_component)`, `PrepaidUsageComponent (prepaid_usage_component)`, `EventBasedComponent (event_based_component)` |
| `CreditType` / `UpgradeChargeCreditType` / `DowngradeCreditCreditType` | `Full (full)`, `Prorated (prorated)`, `None (none)` |
| `PaymentType` | `CreditCard (credit_card)`, `BankAccount (bank_account)`, `PaypalAccount (paypal_account)`, `ApplePay (apple_pay)` |
| `CardType` | `Visa (visa)`, `Master (master)`, `Discover (discover)`, `AmericanExpress (american_express)`, `Bogus (bogus)`, … (full list on `enums.md`) |
| `CreditCardVault` / `AllVaults` | include `Bogus (bogus)` for sandbox |
| `InvoiceStatus` | `Draft (draft)`, `Open (open)`, `Paid (paid)`, `Pending (pending)`, `Voided (voided)`, `Canceled (canceled)`, `Processing (processing)` |
| `InvoiceDateField` | `CreatedAt (created_at)`, `DueDate (due_date)`, `IssueDate (issue_date)`, `UpdatedAt (updated_at)`, `PaidDate (paid_date)` |
| `InvoiceSortField` | `Status (status)`, `TotalAmount (total_amount)`, `DueAmount (due_amount)`, `CreatedAt (created_at)`, `UpdatedAt (updated_at)`, `IssueDate (issue_date)`, `DueDate (due_date)`, `Number (number)` |
| `ResumptionCharge` / `ReactivationCharge` | `Prorated (prorated)`, `Immediate (immediate)`, `Delayed (delayed)` |
| `ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)` |
| `ListSubscriptionComponentsInclude` | `Subscription (subscription)`, `HistoricUsages (historic_usages)` |
| `ListSubscriptionComponentsSort` | `Id (id)`, `UpdatedAt (updated_at)` |
| `SubscriptionListDateField` | `UpdatedAt (updated_at)` |
| `IncludeNotNull` | `NotNull (not_null)` |
| `CompoundingStrategy` | `Compound (compound)`, `FullPrice (full-price)` |
| `RecurringScheme` | `DoNotRecur (do_not_recur)`, `RecurIndefinitely (recur_indefinitely)`, `RecurWithDuration (recur_with_duration)` |
| `PricingScheme` | `Stairstep (stairstep)`, `Volume (volume)`, `PerUnit (per_unit)`, `Tiered (tiered)` |
| `PricePointType` | `Catalog (catalog)`, `Default (default)`, `Custom (custom)` |
| `CancellationMethod` | `MerchantUi (merchant_ui)`, `MerchantApi (merchant_api)`, `Dunning (dunning)`, `BillingPortal (billing_portal)`, `Unknown (unknown)`, `Imported (imported)` |
| `FailedPaymentAction` | `LeaveOpenInvoice (leave_open_invoice)`, `RollbackToPending (rollback_to_pending)`, `InitiateDunning (initiate_dunning)` |
| `InvoicePaymentMethodType` | `CreditCard (credit_card)`, `Check (check)`, `Cash (cash)`, `MoneyOrder (money_order)`, `Ach (ach)`, `Other (other)` |
| `ServerEnvironment` | C# members `Us` / `Eu` (wire `US` / `EU`) — type lives in `MaxioAdvancedBilling.Servers`, not `.Models.Enums`. Nested host: `options.Server.Production.Us` / `.Eu` (`BaseUrl`, `Site`) |

### 2.4 Shared error payloads (`records-2-Cr-Ne.md` / `records-3-Of-Su.md`)

| Type | Fields | Used by |
|---|---|---|
| `ErrorListResponse1` | `Errors (errors): IReadOnlyList<string> !req` | most 422s (create/update subscription, migrate, allocate, usage, send invoice, payment profile, …) |
| `SingleErrorResponse1` | `Error (error): string !req` | cancel-subscription union variant |
| `SingleStringErrorResponse1` | `Errors (errors): string?` | `ValidateCoupon` 404 |
| `ErrorArrayMapResponse1` | `Errors (errors): IReadOnlyDictionary<string, object>?` | (not in the shop write path above; listed for completeness) |
| `SubscriptionAddCouponError1` | `Codes`, `CouponCode`, `CouponCodes`, `Subscription` — each `IReadOnlyList<string>?` | apply coupon 422 |
| `SubscriptionRemoveCouponErrors1` | `Subscription: IReadOnlyList<string> !req` | remove coupon 422 |
| `ComponentAllocationError1` | `Errors: IReadOnlyList<ComponentAllocationErrorItem>?` | preview allocations 422 |
| `CustomerErrorResponse1` | `Errors: Errors?` | create/update customer 422 — see UNVERIFIED note above |
| `RawError` | `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | every Case B and every typed fallback |

---

## 3. Trap notes

⚠ Step 1 (client registration) — `HttpClient` ownership and whether the SDK wrapper is registered transient vs the handler pipeline is long-lived is not visible from the constructor. **MUST load `dotnet-client-initialization`** before `AddMaxioAdvancedBillingClient` / `new MaxioAdvancedBillingClient`.

⚠ Step 1 (auth) — putting the API key in source or constructing the client before `BasicAuth` is set yields 401s that look like “wrong host”. **MUST load `dotnet-authentication`** before wiring credentials (`Username` = key, `Password` = `"x"` from config).

⚠ Step 1 (resilience) — the SDK retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; a failed write’s retryability is not obvious from `RetryOptions` property names. **MUST load `dotnet-configuration-resilience`** before setting `Retry` or `Server`.

⚠ Steps 2–11 (calls) — list/search operations have many nullable parameters **without C# defaults**; a positional call binds the cancellation token into the wrong slot. **MUST load `dotnet-calling-endpoints`** before the first `client.{Api}.{Op}(...)`.

⚠ Steps 2–11 (models) — envelopes wrap one property (`Customer`, `Subscription`, `Product`, `Component`, `Invoice` on create, `PaymentProfile` union); required `init` members, `StringEnum<T>`, and `AnyOf`/`OneOf` factories/`TryGet…` are not obvious from the operation signature. **MUST load `dotnet-models`** before building any request body or mapping a response.

⚠ Step 12 (error boundary) — Case A vs Case B differs **per operation** (this sheet marks each); there are **no** `…Result` no-throw variants; `TryGetRawError` is not a catch-all on the wrong exception type. **MUST load `dotnet-error-handling`** before any `try/catch`.

⚠ Step 12 (error boundary) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 12 (error boundary) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 12 (tests) — the seam to fake is not the generated controller type. **MUST load `dotnet-testing`** before stubbing Maxio in eShopOnWeb tests.

---

## 4. REQUIRED READING

Load **before implementation starts**. This sheet does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — constructing / DI-registering `MaxioAdvancedBillingClient` and `HttpClient` lifetime |
| `dotnet-authentication` | Step 1 — `BasicAuthCredentials`, config-sourced API key |
| `dotnet-configuration-resilience` | Step 1 — `RetryOptions`, timeouts, `Server` / site / base URL, list pagination |
| `dotnet-calling-endpoints` | Steps 2–11 — named arguments, `ct:`, nullable-without-default params, async throw-only calls |
| `dotnet-models` | Steps 2–11 — envelopes, `required` init, enums, unions (`PaymentProfile`, `SubscriptionIdOrReference`, `ComponentIdModel`, `Resume`, `Quantity*`) |
| `dotnet-error-handling` | Step 12 — Case A/B catch ladder, `TryGet…`, both `JsonException` directions (always required) |
| `dotnet-testing` | Step 12 — faking the SDK at the `HttpClient` seam |

---

## 5. Assumptions & Blockers

**Assumptions**

- eShopOnWeb consumes an existing Maxio site catalog (products, price points, components, coupons created in Maxio). This plan does not create those catalog objects via API.
- Each shopper maps to one Maxio customer whose `reference` is the eShop Identity user id; each subscription may set `reference` to an eShop-side id for `FindSubscription`.
- Card data enters Maxio through Chargify.js (`chargify_token`); the ASP.NET app never posts PAN/CVV.
- Collection method is `CollectionMethod.Automatic` unless configuration says otherwise.
- Usage in the shop is metered (`CreateUsage`) and/or quantity allocation (`AllocateComponent`); events-based billing (Ebb server) is out of scope.
- Inbound Maxio webhooks are out of scope (keep shop state by reading the API after writes, or add a receiver in a later brief).
- Hosting is `ServerEnvironment.Us` unless config sets `Eu`.

**Blockers**

- None for contract grounding. Implementation still needs a live site subdomain, API key, at least one Product (and optional Component/Coupon handles) in that site, and a Chargify.js public key if cards are collected in the browser.
- `CustomerErrorResponse1.Errors` is mapped as the `Errors` record (`PerPage`/`PricePoint`); whether a live 422 customer body fills that shape is **UNVERIFIED** — handle via `TryGetCustomerErrorResponse1` then fall back to `TryGetRawError`/`ReadAsString()`.
- 3DS `action_link` on subscription/payment 422s is **UNVERIFIED** against `ErrorListResponse1` — same fallback.
