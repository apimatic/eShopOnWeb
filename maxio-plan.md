# Maxio Advanced Billing — eShopOnWeb integration plan

Package `AsadAli.AdvancedBilling.Sdk` · root namespace `MaxioAdvancedBilling` · map stamp `v1.0.2` / `15db14b`.

Storefront scope: map an eShop buyer to a Maxio customer, present Maxio products as subscription plans, collect payment via Chargify.js token (not PAN), create/manage subscriptions, apply coupons, change plans, record metered usage, show invoices, and keep local state in sync via webhooks / portal. Catalog **authoring** (create/archive products, components, coupons) stays in the Maxio UI — this app **consumes** the catalog.

---

## Scope & sequence

1. **Package + client + DI + auth + site** — `AddMaxioAdvancedBillingClient` / `MaxioAdvancedBillingClient`; Basic auth; `Server.Production.*.Site`.
2. **Error boundary** — wrap every SDK call (throw-only SDK; mixed Case A/B; `JsonException` from two directions).
3. **Catalog (plans)** — `ProductFamilies.ListProductFamilies` / `ReadProductFamily` / `ListProductsForProductFamily`; `Products.ListProducts` / `ReadProduct` / `ReadProductByHandle`; `ProductPricePoints.ListProductPricePoints` / `ReadProductPricePoint`; `Components.ListComponents` / `ListComponentsForProductFamily` / `FindComponent` / `ReadComponent`.
4. **Customers** — `CreateCustomer`, `ReadCustomerByReference` (eShop user id → `reference`), `ReadCustomer`, `UpdateCustomer`, `ListCustomerSubscriptions`.
5. **Payment profiles** — Chargify.js token → `CreatePaymentProfile`; `ListPaymentProfiles` / `ReadPaymentProfile` / `UpdatePaymentProfile` / `ChangeSubscriptionDefaultPaymentProfile` / `ReadOneTimeToken`.
6. **Signup** — `PreviewSubscription` then `CreateSubscription` (existing customer + token or `payment_profile_id`; optional coupon + component allocations).
7. **Read / list** — `ReadSubscription`, `FindSubscription`, `ListSubscriptions`, `ActivateSubscription` (awaiting-signup / trial).
8. **Coupons** — `ValidateCoupon` / `FindCoupon` at checkout; `ApplyCouponsToSubscription` / `RemoveCouponFromSubscription` on an existing sub.
9. **Plan change** — `PreviewSubscriptionProductMigration` then `MigrateSubscriptionProduct` (prorated). Delayed product change via `UpdateSubscription.ProductChangeDelayed` is the non-prorated alternative.
10. **Lifecycle** — `CancelSubscription` / `InitiateDelayedCancellation` / `CancelDelayedCancellation` / `ReactivateSubscription` / `PauseSubscription` / `ResumeSubscription` / `RetrySubscription` / `PreviewRenewal`.
11. **Components + usage** — `ListSubscriptionComponents` / `ReadSubscriptionComponent`; `PreviewAllocations` / `AllocateComponent`; `CreateUsage` / `ListUsages` (metered / prepaid).
12. **Invoices** — `ListInvoices` / `ReadInvoice` / `SendInvoice`; remittance: `RecordPaymentForInvoice` / `RecordPaymentForSubscription`; `ListCreditNotes` / `ReadCreditNote`.
13. **Self-service** — `EnableBillingPortalForCustomer` / `ReadBillingPortalLink`.
14. **Webhooks** — `EnableWebhooks` / `CreateEndpoint` / `UpdateEndpoint` / `ListWebhooks` / `ReplayWebhooks` for subscription/invoice/payment events.
15. **Tests** — fake at the `HttpClient` seam.

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

### Client, auth, servers

| Fact | Value | Source |
|---|---|---|
| NuGet package | `AsadAli.AdvancedBilling.Sdk` | `sdk-map.md` |
| Root namespace | `MaxioAdvancedBilling` | `sdk-map.md` |
| Client | `MaxioAdvancedBillingClient` | `MaxioAdvancedBillingClient.cs` |
| Options | `MaxioAdvancedBillingClientOptions` | `MaxioAdvancedBillingClientOptions.cs` |
| Only ctor | `MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` |
| DI | `IServiceCollection.AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>)` | `ServiceCollectionExtensions.cs` |
| Options members | `Environment`: `MaxioAdvancedBilling.Servers.ServerEnvironment`; `Retry`: `MaxioAdvancedBilling.Core.Configuration.RetryOptions`; `Server`: server options; `BasicAuth`: `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md` |
| Auth | HTTP Basic. `BasicAuthCredentials.Username` = API key; `Password` = literal `"x"`. Set before/at construction. | `sdk-map.md` |
| Environments | `ServerEnvironment.Us` (`US`, default) → `https://{site}.chargify.com`; `ServerEnvironment.Eu` (`EU`) → `https://{site}.ebilling.maxio.com` | `sdk-map.md` / `Servers/ServerEnvironment.cs` |
| Site | `{site}` defaults to `subdomain`. Set `options.Server.Production.Us.Site` (or `.Eu.Site`). Mock host: override `BaseUrl`. | `sdk-map.md` |
| Ebb host | Only `SubscriptionComponents` event-ingest ops use Ebb (`https://events.chargify.com/{site}`). Storefront metered usage (`CreateUsage`) is Production. | `sdk-map.md` |
| `RetryOptions` | Namespace `MaxioAdvancedBilling.Core.Configuration`. All members `required`; start from `RetryOptions.Default()` or set every member: `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout` (`TimeSpan?`), `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry`. | `Core/Configuration/RetryOptions.cs` |
| Throw model | Every op throws. **No** `{Op}Result` / no-throw variants. Case A: `MaxioAdvancedBilling.Core.Exceptions.SdkException<{Op}Error>` with `ex.Error.TryGet…`; Case B: `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`. Typed errors inherit `ApiError.TryGetRawError(out RawError)`. `RawError`: `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. | `sdk-map.md` |
| Namespaces | Client/options: `MaxioAdvancedBilling`. Controllers: `MaxioAdvancedBilling.Api`. Records: `MaxioAdvancedBilling.Models`. Enums: `MaxioAdvancedBilling.Models.Enums`. Unions: `MaxioAdvancedBilling.Models.AnyOf` / `.OneOf`. Errors: `MaxioAdvancedBilling.Errors`. | `sdk-map.md` |
| Enums | `StringEnum<T>` / `IntEnum<T>` — **not** C# enums. Use static members (`CollectionMethod.Automatic`) or `Type.FromValue("wire")`. | `sdk-map.md` / `enums.md` |
| Records | Immutable `init`-only. `!req` must be set in the object initializer. | records pages |

### Envelope rule

Response wrappers have **one** payload property. Read one level down. Exceptions called out per row (e.g. `ReadInvoice` returns `Invoice` directly; list-invoice index returns `ListInvoicesResponse.Invoices` as bare `Invoice`s).

`SubscriptionResponse.Subscription` is **nullable** (`Subscription?`) — null-check before mapping. `CustomerResponse.Customer` and `ProductResponse.Product` are `!req`.

---

### Operations

#### `client.Customers` — `Api/Customers.cs` · `operations/Customers.md`

| Op | Signature (params in order) | Request | Response envelope + fields this app reads | Error | Pagination |
|---|---|---|---|---|---|
| `CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, **must pass explicitly** | Envelope `CreateCustomerRequest`: `Customer (customer): CreateCustomer !req`. Inner **required**: `FirstName (first_name): string`, `LastName (last_name): string`, `Email (email): string`. Storefront optionals: `Reference (reference): string?` (set to eShop user id; unique per site), `Organization (organization)`, `Address (address)`, `Address2 (address_2)`, `City (city)`, `State (state)` ISO, `Zip (zip)`, `Country (country)` ISO-3166-1 alpha-2, `Phone (phone)`, `Locale (locale)`, `VatNumber (vat_number)`, `TaxExempt (tax_exempt): bool?`. `records-1-Ac-Cr.md` | `CustomerResponse` → `Customer (customer): Customer !req`. Read: `Id`, `Reference`, `Email`, `FirstName`, `LastName`, `Organization`, address fields, `CreatedAt`, `UpdatedAt`, portal timestamps. `records-2-Cr-Ne.md` | **Case A** `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)`. Payload: `CustomerErrorResponse1.Errors (errors): Errors?` with `PerPage`, `PricePoint` — **UNVERIFIED** vs live 422 body (suspicious shared `Errors` model). Extract best-effort; if accessors do not yield a useful message, fall back to `TryGetRawError` / `ReadAsString()`. | none |
| `ReadCustomer` | `ReadCustomer(int id, CancellationToken ct = default)` | path `id` | `CustomerResponse` → `.Customer` | **Case B** `SdkException<RawError>` | none |
| `ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | query `reference` ← `reference` | `CustomerResponse` → `.Customer` | **Case B** `SdkException<RawError>` | none |
| `UpdateCustomer` | `UpdateCustomer(int id, UpdateCustomerRequest? body, CancellationToken ct = default)` — `body` **must pass** | Envelope `UpdateCustomerRequest`: `Customer (customer): UpdateCustomer !req`. All inner fields optional (`FirstName`, `LastName`, `Email`, `Reference`, address, `Phone`, `Locale`, `VatNumber`, `TaxExempt`, …). `records-4-Su-We.md` | `CustomerResponse` → `.Customer` | **Case A** `SdkException<UpdateCustomerError>`: `TryGetNoContent(out RawError)` [404] · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError` | none |
| `ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | path `customerId` | `IReadOnlyList<SubscriptionResponse>` — each `.Subscription` | **Case B** `SdkException<RawError>` | none |
| `ListCustomers` | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — 7 params `direction`…`q` **must pass** (`null` to skip) | query: `direction`, `page`, `per_page`←`perPage`, `date_field`←`dateField`, `start_date`, `end_date`, `start_datetime`, `end_datetime`, `q` | `IReadOnlyList<CustomerResponse>` | **Case B** `SdkException<RawError>` | manual `page`+`perPage` (default 1/50) |

Do **not** use `DeleteCustomer` from the storefront.

#### `client.PaymentProfiles` — `Api/PaymentProfiles.cs` · `operations/PaymentProfiles.md`

| Op | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `CreatePaymentProfile` | `CreatePaymentProfile(CreatePaymentProfileRequest? body, CancellationToken ct = default)` — `body` **must pass** | Envelope: `PaymentProfile (payment_profile): CreatePaymentProfile !req`. PCI-safe: `ChargifyToken (chargify_token): string?` + `CustomerId (customer_id): int?`. Also: `PaymentType (payment_type): PaymentType?`, billing name/address, `ExpirationMonth (expiration_month): ExpirationMonth1?` (union int\|string), `ExpirationYear (expiration_year): ExpirationYear1?`. **Do not** set `FullNumber` / `Cvv` in production unless the merchant is PCI-compliant. `records-1-Ac-Cr.md` | `PaymentProfileResponse` → `PaymentProfile (payment_profile): PaymentProfile !req` (**OneOf**). Read via `TryGetCreditCardPaymentProfile` / `TryGetBankAccountPaymentProfile` / `TryGetPaypalPaymentProfile` / `TryGetApplePayPaymentProfile`. Credit-card fields: `Id`, `MaskedCardNumber`, `CardType`, `ExpirationMonth/Year`, `CustomerId`, `PaymentType` (default `CreditCard`). `unions.md`, `records-2-Cr-Ne.md`, `records-3-Of-Su.md` | **Case A** `SdkException<CreatePaymentProfileError>`: `TryGetNoContent` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] (`Errors (errors): IReadOnlyList<string> !req`) · `TryGetRawError` | none |
| `ListPaymentProfiles` | `ListPaymentProfiles(int? customerId, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — `customerId` **must pass** | query `customer_id`, `page`, `per_page` | `IReadOnlyList<PaymentProfileResponse>` | **Case B** `SdkException<RawError>` | `page`+`perPage` (1/20) |
| `ReadPaymentProfile` | `ReadPaymentProfile(int paymentProfileId, CancellationToken ct = default)` | path id | `PaymentProfileResponse` → union `.PaymentProfile` | **Case A** `SdkException<ReadPaymentProfileError>`: `TryGetNoContent` [404] · `TryGetRawError` | none |
| `UpdatePaymentProfile` | `UpdatePaymentProfile(int paymentProfileId, UpdatePaymentProfileRequest? body, CancellationToken ct = default)` — `body` **must pass** | Envelope: `PaymentProfile (payment_profile): UpdatePaymentProfile !req`. Billing/contact + optional card fields. Changing PAN generally requires a **new** profile. `records-4-Su-We.md` | `PaymentProfileResponse` | **Case A** `SdkException<UpdatePaymentProfileError>`: `TryGetNoContent` [404] · `TryGetErrorStringMapResponse1(out ErrorStringMapResponse1)` [422] (`Errors: IReadOnlyDictionary<string,string>?`) · `TryGetRawError` | none |
| `ChangeSubscriptionDefaultPaymentProfile` | `ChangeSubscriptionDefaultPaymentProfile(int subscriptionId, int paymentProfileId, CancellationToken ct = default)` | path ids | `PaymentProfileResponse` | **Case A** `SdkException<ChangeSubscriptionDefaultPaymentProfileError>`: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `ReadOneTimeToken` | `ReadOneTimeToken(string chargifyToken, CancellationToken ct = default)` | path `chargifyToken` | `GetOneTimeTokenRequest` → `PaymentProfile (payment_profile): GetOneTimeTokenPaymentProfile !req` (masked card metadata). `records-2-Cr-Ne.md` | **Case A** `SdkException<ReadOneTimeTokenError>`: `TryGetErrorListResponse1` [404] · `TryGetRawError` | none |
| `SendRequestUpdatePaymentEmail` | `SendRequestUpdatePaymentEmail(int subscriptionId, CancellationToken ct = default)` | path | `void` | **Case A** `SdkException<SendRequestUpdatePaymentEmailError>`: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |

Skip subscription-**group** payment-profile ops.

#### `client.Subscriptions` — `Api/Subscriptions.cs` · `operations/Subscriptions.md`

| Op | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` **must pass** | Envelope: `Subscription (subscription): CreateSubscription !req`. Identify product: `ProductId (product_id): int?` **or** `ProductHandle (product_handle): string?`. Price point: `ProductPricePointId` / `ProductPricePointHandle`. Identify customer: `CustomerId (customer_id): int?` **or** `CustomerReference (customer_reference): string?` **or** nested `CustomerAttributes (customer_attributes)`. Payment: `PaymentProfileId (payment_profile_id): int?` **or** `PaymentProfileAttributes` / `CreditCardAttributes` (prefer `ChargifyToken`). Coupons: `CouponCode (coupon_code)` or `CouponCodes (coupon_codes)`. Components at signup: `Components (components): IReadOnlyList<CreateSubscriptionComponent>?` — `ComponentId (component_id): ComponentId1?` (union int\|string; factories `ComponentId1.Int` / `.String`), `Enabled`, `AllocatedQuantity (allocated_quantity): AllocatedQuantity3?`, `UnitBalance`, `Quantity`, `PricePointId (price_point_id): PricePointId2?`. Other storefront: `Reference (reference)` (eShop order/sub key), `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `AgreementAcceptance` (required when using Maxio Payments). `records-2-Cr-Ne.md` | `SubscriptionResponse` → `Subscription (subscription): Subscription?`. Read: `Id`, `State`, `BalanceInCents`, `ProductPriceInCents`, `CurrentPeriodStartedAt` / `CurrentPeriodEndsAt`, `NextAssessmentAt`, `ActivatedAt`, `CanceledAt`, `CancelAtEndOfPeriod`, `CouponCode` / `CouponCodes`, `PaymentCollectionMethod`, nested `Customer`, `Product`, `CreditCard`, `ProductPricePointId`, `Reference`, `Currency`, `SelfServicePageToken` (only if requested via include on read). `records-3-Of-Su.md` | **Case A** `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError`. 422 may carry 3DS `action_link` in the raw body — **UNVERIFIED** whether that lands in `ErrorListResponse1.Errors` or only in `RawError`; if `TryGetErrorListResponse1` does not expose a link, `TryGetRawError` + `ReadAsString()` / `ReadAsJson`. | none |
| `PreviewSubscription` | `PreviewSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — **same body type as create**; `body` **must pass** | Same `CreateSubscriptionRequest`. Tax preview needs billing/shipping address; card number not required for preview. `SkipBillingManifestTaxes (skip_billing_manifest_taxes): bool?` | `SubscriptionPreviewResponse` → `SubscriptionPreview (subscription_preview): SubscriptionPreview !req` → `CurrentBillingManifest` / `NextBillingManifest` (`BillingManifest`: `LineItems`, `TotalInCents`, `TotalTaxInCents`, `TotalDiscountInCents`, `SubtotalInCents`, `StartDate`, `EndDate`, `ExistingBalanceInCents`). `records-4-Su-We.md`, `records-1-Ac-Cr.md` | **Case B** `SdkException<RawError>` | none |
| `ReadSubscription` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` **must pass** (`null` to skip) | query `include`. `SubscriptionInclude.Coupons` / `SelfServicePageToken` | `SubscriptionResponse` → `.Subscription` | **Case B** `SdkException<RawError>` | none |
| `FindSubscription` | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` **must pass** | query `reference` | `SubscriptionResponse` | **Case A** `SdkException<FindSubscriptionError>`: `TryGetNoContent` [404] · `TryGetRawError` | none |
| `ListSubscriptions` | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string,string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 14 params `state`…`include` **must pass** | query wires as listed on the op page (`per_page`←`perPage`, `product_price_point_id`, `coupon_code`, `date_field`, …) | `IReadOnlyList<SubscriptionResponse>` | **Case B** `SdkException<RawError>` | `page`+`perPage` (1/20) |
| `UpdateSubscription` | `UpdateSubscription(int subscriptionId, UpdateSubscriptionRequest? body, CancellationToken ct = default)` — `body` **must pass** | Envelope: `Subscription (subscription): UpdateSubscription !req`. Plan fields: `ProductHandle` / `ProductId`, `ProductPricePointId` / `Handle`, `ProductChangeDelayed (product_change_delayed): bool?` (delayed = next renewal, no proration). Cancel delayed change: `NextProductId (next_product_id): string?` empty. Payment: `CreditCardAttributes`. Dates: `NextBillingAt`. `PaymentCollectionMethod` here is `string?` (not `CollectionMethod`). `records-4-Su-We.md` | `SubscriptionResponse` | **Case A** `SdkException<UpdateSubscriptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `ApplyCouponsToSubscription` | `ApplyCouponsToSubscription(int subscriptionId, string? code, AddCouponsRequest? body, CancellationToken ct = default)` — `code` and `body` **must pass** | Prefer **body** `AddCouponsRequest.Codes (codes): IReadOnlyList<string>?` (adds to existing). Query `code` **replaces** all coupons (deprecated). Pass `code: null` when using body. `records-1-Ac-Cr.md` | `SubscriptionResponse` | **Case A** `SdkException<ApplyCouponsToSubscriptionError>`: `TryGetSubscriptionAddCouponError1(out SubscriptionAddCouponError1)` [422] (`Codes`, `CouponCode`, `CouponCodes`, `Subscription` — each `IReadOnlyList<string>?`) · `TryGetRawError` | none |
| `RemoveCouponFromSubscription` | `RemoveCouponFromSubscription(int subscriptionId, string? couponCode, CancellationToken ct = default)` — `couponCode` **must pass** | query `coupon_code` ← `couponCode` | `string` (not an envelope) | **Case A** `SdkException<RemoveCouponFromSubscriptionError>`: `TryGetSubscriptionRemoveCouponErrors1(out SubscriptionRemoveCouponErrors1)` [422] (`Subscription (subscription): IReadOnlyList<string> !req`) · `TryGetRawError` | none |
| `ActivateSubscription` | `ActivateSubscription(int subscriptionId, ActivateSubscriptionRequest? body, CancellationToken ct = default)` — `body` **must pass** | `RevertOnFailure (revert_on_failure): bool?`. Pass `null` body only if explicitly passing the nullable. | `SubscriptionResponse` | **Case A** `SdkException<ActivateSubscriptionError>`: `TryGetErrorArrayMapResponse1(out ErrorArrayMapResponse1)` [400] (`Errors: IReadOnlyDictionary<string,object>?`) · `TryGetRawError` | none |

Skip `OverrideSubscription`, `PurgeSubscription`, `UpdatePrepaidSubscriptionConfiguration` (not storefront).

#### `client.SubscriptionStatus` — `Api/SubscriptionStatus.cs` · `operations/SubscriptionStatus.md`

| Op | Signature | Request | Response | Error |
|---|---|---|---|---|
| `CancelSubscription` | `CancelSubscription(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` — `body` **must pass** | Envelope: `Subscription (subscription): CancellationOptions !req`. Options: `CancellationMessage`, `ReasonCode`, `CancelAtEndOfPeriod`, `ScheduledCancellationAt`, `RefundPrepaymentAccountBalance`. Immediate cancel: omit schedule fields. `records-1-Ac-Cr.md` | `SubscriptionResponse` | **Case A** `SdkException<CancelSubscriptionApiError>`: `TryGetNoContent` [404] · `TryGetCancelSubscriptionErrorResponse(out CancelSubscriptionErrorResponse)` [422] — **union** `ErrorListResponse1` \| `SingleErrorResponse1`; factories/TryGet `TryGetErrorListResponse1` / `TryGetSingleErrorResponse1` (`unions.md`) · `TryGetRawError` |
| `InitiateDelayedCancellation` | `InitiateDelayedCancellation(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` — `body` **must pass** | Same `CancellationRequest` | `DelayedCancellationResponse` → `Message (message): string?` (not a subscription envelope) | **Case A** `SdkException<InitiateDelayedCancellationError>`: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `CancelDelayedCancellation` | `CancelDelayedCancellation(int subscriptionId, CancellationToken ct = default)` | path | `DelayedCancellationResponse` | **Case A** `SdkException<CancelDelayedCancellationError>`: `TryGetNoContent` [404] · `TryGetRawError` |
| `ReactivateSubscription` | `ReactivateSubscription(int subscriptionId, ReactivateSubscriptionRequest? body, CancellationToken ct = default)` — `body` **must pass** | Flat (no inner envelope): `IncludeTrial`, `PreserveBalance`, `CouponCode`, `UseCreditsAndPrepayments`, `CalendarBilling (calendar_billing): ReactivationBilling?` (`ReactivationCharge`), `Resume (resume): Resume?` (**union** `bool` \| `ResumeOptions`; `Resume.Bool` / `Resume.ResumeOptions`; `ResumeOptions`: `RequireResume`, `ForgiveBalance`). `records-3-Of-Su.md` | `SubscriptionResponse` | **Case A** `SdkException<ReactivateSubscriptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError`. 3DS same UNVERIFIED note as create. |
| `PauseSubscription` | `PauseSubscription(int subscriptionId, PauseRequest? body, CancellationToken ct = default)` — `body` **must pass** | `Hold (hold): AutoResume?` → `AutomaticallyResumeAt (automatically_resume_at): DateTimeOffset?` | `SubscriptionResponse` | **Case A** `SdkException<PauseSubscriptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `ResumeSubscription` | `ResumeSubscription(int subscriptionId, ResumptionCharge? calendarBillingResumptionCharge, CancellationToken ct = default)` — `calendarBillingResumptionCharge` **must pass** | query `calendar_billing['resumption_charge']` ← enum | `SubscriptionResponse` | **Case A** `SdkException<ResumeSubscriptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `RetrySubscription` | `RetrySubscription(int subscriptionId, CancellationToken ct = default)` | path | `SubscriptionResponse` | **Case A** `SdkException<RetrySubscriptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `PreviewRenewal` | `PreviewRenewal(int subscriptionId, RenewalPreviewRequest? body, CancellationToken ct = default)` — `body` **must pass** | `Components (components): IReadOnlyList<RenewalPreviewComponent>?` to override quantities; `null` body still must be passed. | `RenewalPreviewResponse` → `RenewalPreview (renewal_preview): RenewalPreview !req`: `NextAssessmentAt`, `SubtotalInCents`, `TotalTaxInCents`, `TotalDiscountInCents`, `TotalInCents`, `ExistingBalanceInCents`, `TotalAmountDueInCents`, `LineItems`. | **Case A** `SdkException<PreviewRenewalError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` |

`CancelDunning` / `UpdateAutomaticSubscriptionResumption` are optional ops-console features — not required for the first storefront slice.

#### `client.SubscriptionProducts` — `Api/SubscriptionProducts.cs` · `operations/SubscriptionProducts.md`

| Op | Signature | Request | Response | Error |
|---|---|---|---|---|
| `PreviewSubscriptionProductMigration` | `PreviewSubscriptionProductMigration(int subscriptionId, SubscriptionMigrationPreviewRequest? body, CancellationToken ct = default)` — `body` **must pass** | Envelope: `Migration (migration): SubscriptionMigrationPreviewOptions !req`. Product: `ProductId` **or** `ProductHandle`; price point: `ProductPricePointId` / `Handle`. Flags (defaults): `IncludeTrial = false`, `IncludeInitialCharge = false`, `IncludeCoupons = true`, `PreservePeriod = false`. `Proration (proration): Proration?` (`PreservePeriod (preserve_period): bool?`). `ProrationDate (proration_date): DateTimeOffset?` for a future-in-period preview. `records-4-Su-We.md` | `SubscriptionMigrationPreviewResponse` → `Migration (migration): SubscriptionMigrationPreview !req`: `ProratedAdjustmentInCents`, `ChargeInCents`, `PaymentDueInCents`, `CreditAppliedInCents` | **Case A** `SdkException<PreviewSubscriptionProductMigrationError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `MigrateSubscriptionProduct` | `MigrateSubscriptionProduct(int subscriptionId, SubscriptionProductMigrationRequest? body, CancellationToken ct = default)` — `body` **must pass** | Envelope: `Migration (migration): SubscriptionProductMigration !req`. Same product/price-point/flags as preview **except no `ProrationDate`**. Valid states: `active` / `trialing`. | `SubscriptionResponse` | **Case A** `SdkException<MigrateSubscriptionProductError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError`. 3DS UNVERIFIED note as create. |

#### `client.Products` — `Api/Products.cs` · `operations/Products.md`

| Op | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `ListProducts` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 params `dateField`…`include` **must pass** | `ListProductsFilter`: `Ids`, `PrepaidProductPricePoint`, `UseSiteExchangeRate`. `ListProductsInclude.PrepaidProductPricePoint`. | `IReadOnlyList<ProductResponse>` → each `.Product` (`Product !req`): `Id`, `Name`, `Handle`, `Description`, `PriceInCents`, `Interval`, `IntervalUnit`, `TrialPriceInCents` / `TrialInterval` / `TrialIntervalUnit`, `InitialChargeInCents`, `RequireCreditCard`, `ProductFamily`, `DefaultProductPricePointId`, `ProductPricePointId` / `Handle`, `ArchivedAt`. `records-3-Of-Su.md` | **Case B** `SdkException<RawError>` | `page`+`perPage` (1/20) |
| `ReadProduct` | `ReadProduct(int productId, CancellationToken ct = default)` | path | `ProductResponse` → `.Product` | **Case B** | none |
| `ReadProductByHandle` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | path `apiHandle` | `ProductResponse` → `.Product` | **Case B** | none |

Skip `CreateProduct` / `UpdateProduct` / `ArchiveProduct` (catalog authoring).

#### `client.ProductFamilies` — `Api/ProductFamilies.cs` · `operations/ProductFamilies.md`

| Op | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `ListProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — 5 params **must pass** | query date filters | `IReadOnlyList<ProductFamilyResponse>` → `.ProductFamily` (`ProductFamily?`): `Id`, `Name`, `Handle`, `Description`, `ArchivedAt` | **Case B** | none |
| `ReadProductFamily` | `ReadProductFamily(int id, CancellationToken ct = default)` | path. Notes: id **or** `handle:my-family` in the URL — the C# param is `int id`, so handles go through list/filter, not this overload. | `ProductFamilyResponse` | **Case B** | none |
| `ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 params `dateField`…`include` **must pass** | path `productFamilyId` is **`string`** (numeric id or `handle:…`) | `IReadOnlyList<ProductResponse>` | **Case A** `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError` | `page`+`perPage` (1/20) |

Skip `CreateProductFamily`.

#### `client.ProductPricePoints` — `Api/ProductPricePoints.cs` · `operations/ProductPricePoints.md`

| Op | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `ListProductPricePoints` | `ListProductPricePoints(ProductIdModel productId, bool? currencyPrices, IReadOnlyList<PricePointType>? filterType, bool? archived, int? page = 1, int? perPage = 10, CancellationToken ct = default)` — 3 optionals **must pass** | `productId`: **union** `ProductIdModel.Int(int)` / `ProductIdModel.String(string)`. query `currency_prices`, `filter[type]`←`filterType`, `archived` | `ListProductPricePointsResponse` → `PricePoints (price_points): IReadOnlyList<ProductPricePoint> !req` (bare, not `ProductPricePointResponse`). Fields: `Id`, `Name`, `Handle`, `PriceInCents`, `Interval`, `IntervalUnit`, trial/initial-charge, `Type`, `TaxIncluded`, `CurrencyPrices`. | **Case B** `SdkException<RawError>` | `page`+`perPage` (**default perPage 10**) |
| `ReadProductPricePoint` | `ReadProductPricePoint(ProductIdModel productId, PricePointIdModel pricePointId, bool? currencyPrices, CancellationToken ct = default)` — `currencyPrices` **must pass** | both path ids are unions `Int`/`String` (handle ok as string) | `ProductPricePointResponse` → `PricePoint (price_point): ProductPricePoint !req` | **Case B** | none |

Skip create/archive/promote (catalog authoring). `ListAllProductPricePoints` is optional site-wide listing.

#### `client.Components` — `Api/Components.cs` · `operations/Components.md`

| Op | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `ListComponents` | `ListComponents(BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, bool? includeArchived, ListComponentsFilter? filter, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 7 params **must pass** | `ListComponentsFilter`: `Ids`, `UseSiteExchangeRate`. Dates here are **`string?`**, not `DateTimeOffset`. | `IReadOnlyList<ComponentResponse>` → `.Component` (`Component !req`): `Id`, `Name`, `Handle`, `Kind`, `PricingScheme`, `UnitName`, `UnitPrice`, `ProductFamilyId` / `Handle`, `DefaultPricePointId` / `Name`, `Recurring`, `Archived`, `Prices`. `records-1-Ac-Cr.md` | **Case B** | `page`+`perPage` (1/20) |
| `ListComponentsForProductFamily` | `ListComponentsForProductFamily(int productFamilyId, bool? includeArchived, ListComponentsFilter? filter, BasicDateField? dateField, string? endDate, string? endDatetime, string? startDate, string? startDatetime, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 7 params **must pass** | path `productFamilyId` is **`int`** (unlike products-for-family which takes `string`) | `IReadOnlyList<ComponentResponse>` | **Case B** | 1/20 |
| `FindComponent` | `FindComponent(string handle, CancellationToken ct = default)` | query `handle` | `ComponentResponse` | **Case B** | none |
| `ReadComponent` | `ReadComponent(int productFamilyId, string componentId, CancellationToken ct = default)` | `componentId` may be numeric or `handle:…` | `ComponentResponse` | **Case B** | none |

Skip create-by-kind / archive / update (catalog authoring). Metered vs quantity vs on/off is `Component.Kind`.

#### `client.SubscriptionComponents` — `Api/SubscriptionComponents.cs` · `operations/SubscriptionComponents.md`

| Op | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `ListSubscriptionComponents` | `ListSubscriptionComponents(int subscriptionId, SubscriptionListDateField? dateField, SortingDirection? direction, ListSubscriptionComponentsFilter? filter, string? endDate, string? endDatetime, IncludeNotNull? pricePointIds, IReadOnlyList<int>? productFamilyIds, ListSubscriptionComponentsSort? sort, string? startDate, string? startDatetime, IReadOnlyList<ListSubscriptionComponentsInclude>? include, bool? inUse, CancellationToken ct = default)` — **12 params must pass** | include: `Subscription`, `HistoricUsages` | `IReadOnlyList<SubscriptionComponentResponse>` → `Component (component): SubscriptionComponent?`. Read: `Id`, `Name`, `Kind`, `Enabled`, `UnitBalance`, `AllocatedQuantity` (**union** `AllocatedQuantity2` int\|string — `TryGetInt`/`TryGetString`), `ComponentId` / `Handle`, `PricePointId` / `Handle`, `Recurring`. `records-3-Of-Su.md` | **Case B** | none |
| `ReadSubscriptionComponent` | `ReadSubscriptionComponent(int subscriptionId, int componentId, CancellationToken ct = default)` | path | `SubscriptionComponentResponse` | **Case A** `SdkException<ReadSubscriptionComponentError>`: `TryGetNoContent` [404] · `TryGetRawError` | none |
| `PreviewAllocations` | `PreviewAllocations(int subscriptionId, PreviewAllocationsRequest? body, CancellationToken ct = default)` — `body` **must pass** | `Allocations (allocations): IReadOnlyList<CreateAllocation> !req`; optional `EffectiveProrationDate`, `UpgradeCharge` / `DowngradeCredit` (`CreditType`) | `AllocationPreviewResponse` → `AllocationPreview (allocation_preview): AllocationPreview !req`: cents totals, `Direction`, `LineItems`, `Allocations` | **Case A** `SdkException<PreviewAllocationsError>`: `TryGetComponentAllocationError1(out ComponentAllocationError1)` [422] (`Errors: IReadOnlyList<ComponentAllocationErrorItem>?` — `ComponentId`, `Message`, `Kind`, `On`) · `TryGetRawError` | none |
| `AllocateComponent` | `AllocateComponent(int subscriptionId, int componentId, CreateAllocationRequest? body, CancellationToken ct = default)` — `body` **must pass** | Envelope: `Allocation (allocation): CreateAllocation !req`. **Required**: `Quantity (quantity): double`. Optionals: `Memo`, `ComponentId`, `PricePointId (price_point_id): PricePointId1?` (union string\|int), `UpgradeCharge` / `DowngradeCredit` (`UpgradeChargeCreditType` / `DowngradeCreditCreditType`), `AccrueCharge`, `InitiateDunning`. Quantity/on-off/prepaid only — not metered. `records-1-Ac-Cr.md` | `AllocationResponse` → `Allocation (allocation): Allocation?` | **Case A** `SdkException<AllocateComponentError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `CreateUsage` | `CreateUsage(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, CreateUsageRequest? body, CancellationToken ct = default)` — `body` **must pass** | Path unions: `SubscriptionIdOrReference.Int(int)` / `.String(string)` (reference); `ComponentIdModel.Int(int)` / `.String(string)` (`handle:…` ok). Envelope: `Usage (usage): CreateUsage !req` — `Quantity (quantity): double?` (negative deducts; floor 0), `Memo`, `PricePointId (price_point_id): string?`. Metered/prepaid; one component per call. | `UsageResponse` → `Usage (usage): Usage !req`: `Id`, `Quantity` (union `Quantity1`), `Memo`, `ComponentId` / `Handle`, `SubscriptionId`, `CreatedAt` | **Case A** `SdkException<CreateUsageError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `ListUsages` | `ListUsages(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, long? sinceId, long? maxId, DateTimeOffset? sinceDate, DateTimeOffset? untilDate, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 4 params `sinceId`…`untilDate` **must pass** | same path unions | `IReadOnlyList<UsageResponse>` | **Case B** | `page`+`perPage` (1/20) |
| `ListAllocations` | `ListAllocations(int subscriptionId, int componentId, int? page = 1, CancellationToken ct = default)` | query `page` only | `IReadOnlyList<AllocationResponse>` | **Case A** `SdkException<ListAllocationsError>`: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` | `page` only (no `perPage`; 50 most recent) |

Skip EBB `RecordEvent` / `BulkRecordEvents` (different host; not typical eShop metered billing). Skip prepaid-allocation destroy unless a prepaid SKU is added later.

#### `client.Coupons` — `Api/Coupons.cs` · `operations/Coupons.md`

| Op | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `ValidateCoupon` | `ValidateCoupon(string code, int? productFamilyId, CancellationToken ct = default)` — `productFamilyId` **must pass** | query `code`, `product_family_id`. Pass `null` family if the coupon is on the site default family. | `CouponResponse` → `Coupon (coupon): Coupon?`. Read: `Id`, `Name`, `Code`, `Amount` / `AmountInCents`, `Percentage`, `DiscountType`, `Recurring`, `EndDate`, `Stackable`, `ProductFamilyId`. `records-1-Ac-Cr.md` | **Case A** `SdkException<ValidateCouponError>`: `TryGetSingleStringErrorResponse1(out SingleStringErrorResponse1)` [404] (`Errors (errors): string?`) · `TryGetRawError` | none |
| `FindCoupon` | `FindCoupon(int? productFamilyId, string? code, bool? currencyPrices, CancellationToken ct = default)` — all three **must pass** | query `product_family_id`, `code`, `currency_prices` | `CouponResponse` | **Case B** `SdkException<RawError>` (404 if missing) | none |
| `ListCoupons` | `ListCoupons(ListCouponsFilter? filter, bool? currencyPrices, int? page = 1, int? perPage = 30, CancellationToken ct = default)` — `filter` + `currencyPrices` **must pass** | `ListCouponsFilter`: `DateField`, date range, `Ids`, `Codes`, `IncludeArchived`, `UseSiteExchangeRate` | `IReadOnlyList<CouponResponse>` | **Case B** | `page`+`perPage` (**default 30**) |

Skip create/update/archive coupon (catalog). Apply/remove live on `client.Subscriptions`.

#### `client.Invoices` — `Api/Invoices.cs` · `operations/Invoices.md`

| Op | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `ListInvoices` | `ListInvoices(string? startDate, string? endDate, InvoiceStatus? status, int? subscriptionId, string? subscriptionGroupUid, string? consolidationLevel, Direction? direction, InvoiceDateField? dateField, string? startDatetime, string? endDatetime, IReadOnlyList<int>? customerIds, IReadOnlyList<string>? number, IReadOnlyList<int>? productIds, InvoiceSortField? sort, int? page = 1, int? perPage = 20, bool? lineItems = false, bool? discounts = false, bool? taxes = false, bool? credits = false, bool? payments = false, bool? customFields = false, bool? refunds = false, CancellationToken ct = default)` — 14 params `startDate`…`sort` **must pass** | Storefront: `subscriptionId` and/or `customerIds`; set `lineItems`/`payments`/`discounts`/`taxes` **true** when the UI needs breakdowns (index omits them by default). Dates are **`string?`**. | `ListInvoicesResponse` → `Invoices (invoices): IReadOnlyList<Invoice> !req` — **bare `Invoice`**, not `InvoiceResponse`. Read: `Uid`, `Id`, `Number`, `Status`, `IssueDate`, `DueDate`, `PaidDate`, `TotalAmount`, `DueAmount`, `PaidAmount`, `SubtotalAmount`, `DiscountAmount`, `TaxAmount`, `Currency`, `SubscriptionId`, `CustomerId`, `PublicUrl`, `LineItems`, `Payments`. `records-2-Cr-Ne.md` | **Case B** `SdkException<RawError>` | `page`+`perPage` (1/20) |
| `ReadInvoice` | `ReadInvoice(string uid, CancellationToken ct = default)` | path **`uid`** (string, e.g. `inv_…`) — not integer id | **`Invoice`** directly (no wrapper) | **Case B** | none |
| `SendInvoice` | `SendInvoice(string uid, SendInvoiceRequest? body, CancellationToken ct = default)` — `body` **must pass** | `RecipientEmails`, `CcRecipientEmails`, `BccRecipientEmails`, `AttachmentUrls`. Empty recipients → subscription default email. | `void` (204 queued, not delivered) | **Case A** `SdkException<SendInvoiceError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `RecordPaymentForInvoice` | `RecordPaymentForInvoice(string uid, CreateInvoicePaymentRequest? body, CancellationToken ct = default)` — `body` **must pass** | `Payment (payment): CreateInvoicePayment !req` (`Amount`: union string\|double `Amount.String`/`Amount.Double`; `Memo`; `Method`: `InvoicePaymentMethodType`; `PaymentProfileId`; `Details`) + `Type (type): InvoicePaymentType?` | **`Invoice`** | **Case A** `SdkException<RecordPaymentForInvoiceError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `RecordPaymentForSubscription` | `RecordPaymentForSubscription(int subscriptionId, RecordPaymentRequest? body, CancellationToken ct = default)` — `body` **must pass** | Envelope: `Payment (payment): CreatePayment !req` — **all required**: `Amount (amount): string`, `Memo (memo): string`, `PaymentDetails (payment_details): string`, `PaymentMethod (payment_method): InvoicePaymentMethodType`. Oldest open invoice first; remainder → prepayment. | `RecordPaymentResponse`: `PaidInvoices`, `Prepayment` | **Case A** `SdkException<RecordPaymentForSubscriptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `ListCreditNotes` | `ListCreditNotes(int? subscriptionId, int? page = 1, int? perPage = 20, bool? lineItems = false, bool? discounts = false, bool? taxes = false, bool? refunds = false, bool? applications = false, CancellationToken ct = default)` — `subscriptionId` **must pass** | query flags default false | `ListCreditNotesResponse` → `CreditNotes (credit_notes): IReadOnlyList<CreditNote> !req` | **Case B** | 1/20 |
| `ReadCreditNote` | `ReadCreditNote(string uid, CancellationToken ct = default)` | path uid | **`CreditNote`** (no wrapper) | **Case B** | none |

Skip ad-hoc `CreateInvoice`, `VoidInvoice`, `RefundInvoice`, `IssueInvoice` for the first storefront slice (merchant back-office). `RefundInvoiceRequest.Refund` is union `Refund.RefundInvoice(RefundInvoice)` / `Refund.RefundConsolidatedInvoice` if added later (`RefundInvoice`: `Amount`, `Memo`, `PaymentId` all `!req`).

#### `client.BillingPortal` — `Api/BillingPortal.cs` · `operations/BillingPortal.md`

| Op | Signature | Request | Response | Error |
|---|---|---|---|---|
| `EnableBillingPortalForCustomer` | `EnableBillingPortalForCustomer(int customerId, AutoInvite? autoInvite, CancellationToken ct = default)` — `autoInvite` **must pass** | query `auto_invite`. `AutoInvite` is **IntEnum**: `Value0 (0)`, `Value1 (1)` — 1 sends invite email. | `CustomerResponse` | **Case A** `SdkException<EnableBillingPortalForCustomerError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `ReadBillingPortalLink` | `ReadBillingPortalLink(int customerId, CancellationToken ct = default)` | path | `PortalManagementLink`: `Url`, `FetchCount`, `CreatedAt`, `NewLinkAvailableAt`, `ExpiresAt`, `LastInviteSentAt`. Cache until `NewLinkAvailableAt`; 15-request cap before then → 429. | **Case A** `SdkException<ReadBillingPortalLinkError>`: `TryGetErrorListResponse1` [422] · `TryGetTooManyManagementLinkRequestsError1(out TooManyManagementLinkRequestsError1)` [429] (`Errors.Error`, `Errors.NewLinkAvailableAt`) · `TryGetRawError` |

#### `client.Webhooks` — `Api/Webhooks.cs` · `operations/Webhooks.md`

| Op | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `EnableWebhooks` | `EnableWebhooks(EnableWebhooksRequest? body, CancellationToken ct = default)` — `body` **must pass** | `WebhooksEnabled (webhooks_enabled): bool !req` | `EnableWebhooksResponse` → `WebhooksEnabled (webhooks_enabled): bool?` | **Case B** | none |
| `CreateEndpoint` | `CreateEndpoint(CreateOrUpdateEndpointRequest? body, CancellationToken ct = default)` — `body` **must pass** | Envelope: `Endpoint (endpoint): CreateOrUpdateEndpoint !req` — `Url (url): string !req`, `WebhookSubscriptions (webhook_subscriptions): IReadOnlyList<WebhookSubscription> !req` (complete list). | `EndpointResponse` → `Endpoint (endpoint): Endpoint?` (`Id`, `Url`, `Status`, `WebhookSubscriptions` as `IReadOnlyList<string>?`) | **Case A** `SdkException<CreateEndpointError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `UpdateEndpoint` | `UpdateEndpoint(int endpointId, CreateOrUpdateEndpointRequest? body, CancellationToken ct = default)` — `body` **must pass** | Same body; empty `webhook_subscriptions` unsubscribes all | `EndpointResponse` | **Case A** `SdkException<UpdateEndpointError>`: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `ListEndpoints` | `ListEndpoints(CancellationToken ct = default)` | — | `IReadOnlyList<Endpoint>` (bare) | **Case B** | none |
| `ListWebhooks` | `ListWebhooks(WebhookStatus? status, string? sinceDate, string? untilDate, WebhookOrder? order, int? subscription, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 5 params **must pass** | query `subscription` = subscription id | `IReadOnlyList<WebhookResponse>` → `.Webhook`: `Id`, `Event`, `Successful`, `LastError`, `Body`, `Signature` / `SignatureHmacSha256` | **Case B** | 1/20 |
| `ReplayWebhooks` | `ReplayWebhooks(ReplayWebhooksRequest? body, CancellationToken ct = default)` — `body` **must pass** | `Ids (ids): IReadOnlyList<long> !req` (max 1000) | `ReplayWebhooksResponse` → `Status (status): string?` | **Case B** | none |

Inbound webhook HTTP handler is application code (verify signature from payload), not an SDK op.

---

### Enums in scope (`map/models/enums.md` · namespace `MaxioAdvancedBilling.Models.Enums`)

Member `(wire)`. Build with the static member, not a C# enum literal of the wire string.

| Enum | Members |
|---|---|
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` — RI architecture: remittance / automatic / prepaid |
| `SubscriptionState` | `Pending`, `FailedToCreate`, `Trialing`, `Assessing`, `Active`, `SoftFailure`, `PastDue`, `Suspended`, `Canceled`, `Expired`, `Paused`, `Unpaid`, `TrialEnded`, `OnHold`, `AwaitingSignup` (wires: `pending`, `failed_to_create`, `trialing`, `assessing`, `active`, `soft_failure`, `past_due`, `suspended`, `canceled`, `expired`, `paused`, `unpaid`, `trial_ended`, `on_hold`, `awaiting_signup`) |
| `SubscriptionStateFilter` | `Active`, `Canceled`, `Expired`, `ExpiredCards`, `OnHold`, `PastDue`, `PendingCancellation`, `PendingRenewal`, `Suspended`, `TrialEnded`, `Trialing`, `Unpaid` |
| `SubscriptionDateField` | `CurrentPeriodEndsAt`, `CurrentPeriodStartsAt`, `CreatedAt`, `ActivatedAt`, `CanceledAt`, `ExpiresAt`, `TrialStartedAt`, `TrialEndedAt`, `UpdatedAt` |
| `SubscriptionSort` | `SignupDate`, `PeriodStart`, `PeriodEnd`, `NextAssessment`, `UpdatedAt`, `CreatedAt`, `TotalPayments`, `Id`, `OpenBalance`, `ExpiresAt` |
| `SubscriptionInclude` | `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)` |
| `SubscriptionListInclude` | `SelfServicePageToken (self_service_page_token)` |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` |
| `SortingDirection` / `Direction` | `Asc (asc)`, `Desc (desc)` |
| `IntervalUnit` | `Day (day)`, `Month (month)` |
| `ExpirationIntervalUnit` | `Day (day)`, `Month (month)`, `Never (never)` |
| `TrialType` | `NoObligation (no_obligation)`, `PaymentExpected (payment_expected)` |
| `PricePointType` | `Catalog (catalog)`, `Default (default)`, `Custom (custom)` |
| `PricingScheme` | `Stairstep (stairstep)`, `Volume (volume)`, `PerUnit (per_unit)`, `Tiered (tiered)` |
| `ComponentKind` | `MeteredComponent (metered_component)`, `QuantityBasedComponent (quantity_based_component)`, `OnOffComponent (on_off_component)`, `PrepaidUsageComponent (prepaid_usage_component)`, `EventBasedComponent (event_based_component)` |
| `CreditType` | `Full (full)`, `Prorated (prorated)`, `None (none)` |
| `UpgradeChargeCreditType` / `DowngradeCreditCreditType` | `Full`, `Prorated`, `None` (same wires) |
| `PaymentType` | `CreditCard (credit_card)`, `BankAccount (bank_account)`, `PaypalAccount (paypal_account)`, `ApplePay (apple_pay)` |
| `InvoiceStatus` | `Draft`, `Open`, `Paid`, `Pending`, `Voided`, `Canceled`, `Processing` |
| `InvoiceDateField` | `CreatedAt`, `DueDate`, `IssueDate`, `UpdatedAt`, `PaidDate` |
| `InvoiceSortField` | `Status`, `TotalAmount`, `DueAmount`, `CreatedAt`, `UpdatedAt`, `IssueDate`, `DueDate`, `Number` |
| `InvoicePaymentMethodType` | `CreditCard`, `Check`, `Cash`, `MoneyOrder`, `Ach`, `Other` |
| `InvoicePaymentType` | `External`, `Prepayment`, `ServiceCredit`, `Payment` |
| `InvoiceRole` | `Unset`, `Signup`, `Renewal`, `Usage`, `Reactivation`, `Proration`, `Migration`, `Adhoc`, `Backport`, `BackportBalanceReconciliation (backport-balance-reconciliation)` |
| `InvoiceConsolidationLevel` | `None`, `Child`, `Parent` |
| `DiscountType` | `Amount (amount)`, `Percent (percent)` |
| `RecurringScheme` | `DoNotRecur (do_not_recur)`, `RecurIndefinitely (recur_indefinitely)`, `RecurWithDuration (recur_with_duration)` |
| `CompoundingStrategy` | `Compound (compound)`, `FullPrice (full-price)` |
| `CancellationMethod` | `MerchantUi`, `MerchantApi`, `Dunning`, `BillingPortal`, `Unknown`, `Imported` |
| `ResumptionCharge` / `ReactivationCharge` | `Prorated`, `Immediate`, `Delayed` |
| `ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)` |
| `ListSubscriptionComponentsInclude` | `Subscription`, `HistoricUsages` |
| `ListSubscriptionComponentsSort` | `Id`, `UpdatedAt` |
| `SubscriptionListDateField` | `UpdatedAt (updated_at)` |
| `IncludeNotNull` | `NotNull (not_null)` |
| `AutoInvite` | **IntEnum** `Value0 (0)`, `Value1 (1)` |
| `WebhookStatus` | `Successful`, `Failed`, `Pending`, `Paused` |
| `WebhookOrder` | `NewestFirst (newest_first)`, `OldestFirst (oldest_first)` |
| `WebhookSubscription` | `BillingDateChange`, `ComponentAllocationChange`, `ChjsTokenizationFailure`, `ChjsTokenizationSuccess`, `CustomerCreate`, `CustomerUpdate`, `DunningStepReached`, `ExpiringCard`, `ExpirationDateChange`, `InvoiceIssued`, `InvoicePending`, `MeteredUsage`, `PaymentFailure`, `PaymentSuccess`, `DirectDebitPaymentPending`, `DirectDebitPaymentPaidOut`, `DirectDebitPaymentRejected`, `PrepaidSubscriptionBalanceChanged`, `PrepaidUsage`, `RefundFailure`, `RefundSuccess`, `RenewalFailure`, `RenewalSuccess`, `SignupFailure`, `SignupSuccess`, `StatementClosed`, `StatementSettled`, `SubscriptionCardUpdate`, `SubscriptionGroupCardUpdate`, `SubscriptionProductChange`, `SubscriptionStateChange`, `TrialEndNotice`, `UpcomingRenewalNotice`, `UpgradeDowngradeFailure`, `UpgradeDowngradeSuccess`, `PendingCancellationChange`, `SubscriptionPrepaymentAccountBalanceChanged`, `SubscriptionServiceCreditAccountBalanceChanged` |
| `CreateInvoiceStatus` | `Draft (draft)`, `Open (open)` — only if ad-hoc invoices are added later |
| `FailedPaymentAction` | `LeaveOpenInvoice`, `RollbackToPending`, `InitiateDunning` |
| `CardType` | `Visa`, `Master`, `Elo`, `Cabal`, `Alelo`, `Discover`, `AmericanExpress`, `Naranja`, `DinersClub`, `Jcb`, `Dankort`, `Maestro`, `MaestroNoLuhn`, `Forbrugsforeningen`, `Sodexo`, `Alia`, `Vr`, `Unionpay`, `Carnet`, `CartesBancaires`, `Olimpica`, `Creditel`, `Confiable`, `Synchrony`, `Routex`, `Mada`, `BpPlus`, `Passcard`, `Edenred`, `Anda`, `TarjetaD (tarjeta-d)`, `Hipercard`, `Bogus`, `Switch`, `Solo`, `Laser` |
| `AllocationPreviewDirection` | `Upgrade (upgrade)`, `Downgrade (downgrade)` |

### Unions used at call sites (`map/models/unions.md`)

| Union | Namespace | Factories | Read |
|---|---|---|---|
| `PaymentProfile` | `MaxioAdvancedBilling.Models.OneOf` | `PaymentProfile.CreditCardPaymentProfile(…)` etc. | `TryGetCreditCardPaymentProfile` / `BankAccount` / `Paypal` / `ApplePay` |
| `ProductIdModel` / `PricePointIdModel` / `ComponentIdModel` / `SubscriptionIdOrReference` | `.AnyOf` | `.Int(int)` / `.String(string)` | `TryGetInt` / `TryGetString` |
| `CancelSubscriptionErrorResponse` | `.AnyOf` | `ErrorListResponse1` / `SingleErrorResponse1` | matching `TryGet…` |
| `Resume` | `.AnyOf` | `Resume.Bool(bool)` / `Resume.ResumeOptions(ResumeOptions)` | `TryGetBool` / `TryGetResumeOptions` |
| `AllocatedQuantity2` / `Quantity1` / `Amount` / `PricePointId1` / `ComponentId1` | `.AnyOf` | `Int`/`String` or `String`/`Double` as listed above | matching `TryGet…` |
| `Refund` | `.AnyOf` | `Refund.RefundInvoice` / `Refund.RefundConsolidatedInvoice` | matching `TryGet…` |

---

## Trap notes

⚠ Step 1 (client registration) — the SDK client constructor takes an `HttpClient` whose lifetime is not the same question as the generated client wrapper; registering the wrong lifetime duplicates handlers or disposes a shared client. **MUST load `dotnet-client-initialization`** before `AddMaxioAdvancedBillingClient` / `new MaxioAdvancedBillingClient`.

⚠ Step 1 (auth) — Basic credentials have a non-obvious username/password split and must be in place before the client is used; a 401 is a credential/config failure, not a payload bug. **MUST load `dotnet-authentication`** before wiring `BasicAuth`.

⚠ Step 1 (resilience) — the SDK retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; whether a failed write can be re-sent is not visible from the options property names. **MUST load `dotnet-configuration-resilience`** before setting `Retry` or `Server`.

⚠ Steps 3–12 (calls) — list/search operations have long positional lists of required-but-nullable params with no C# defaults; a positional call binds the wrong argument. The cancellation token is `ct`. **MUST load `dotnet-calling-endpoints`** before the first `client.{Group}.{Op}`.

⚠ Steps 4–12 (models) — envelopes, `StringEnum<T>`, and `OneOf`/`AnyOf` (no `new` on unions; factory + `TryGet`) are not obvious from property types; unmodeled JSON is dropped on deserialize. **MUST load `dotnet-models`** before constructing bodies or mapping responses.

⚠ Step 2 (error boundary) — Case A vs Case B is per operation (this sheet); `TryGetRawError` is not a catch-all on every error type; there are no Result-style APIs. **MUST load `dotnet-error-handling`** before any `try/catch`.

⚠ Step 2 (error boundary) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 2 (error boundary) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 15 (tests) — the test seam is the `HttpClient` argument, not a generated interface on each controller. **MUST load `dotnet-testing`** before stubbing.

⚠ Step 5 (PCI) — `CreatePaymentProfile.FullNumber` / `Cvv` and nested card attributes on `CreateSubscription` collect PAN. Production storefronts that are not PCI-compliant must send `ChargifyToken` from Chargify.js / Maxio.js only (operation notes on `PaymentProfiles` / `Subscriptions`).

⚠ Step 6 (3DS) — create / migrate / reactivate / retry / create-payment-profile can return 422 with an SCA `action_link`. Treat that path as **UNVERIFIED** in the typed 422 payload; if `TryGetErrorListResponse1` does not yield the link, fall back to `TryGetRawError` + `ReadAsString()` / `ReadAsJson`.

⚠ Step 13 (portal) — `ReadBillingPortalLink` is rate-limited (429 + `NewLinkAvailableAt`). Cache `Url` until that timestamp; do not fetch per page view.

---

## REQUIRED READING

Load **before implementation starts**. This sheet does not carry their contents.

- `dotnet-client-initialization` — Step 1 client construction and DI.
- `dotnet-authentication` — Step 1 Basic credentials.
- `dotnet-configuration-resilience` — Step 1 retries, timeouts, site/base URL, pagination loops.
- `dotnet-calling-endpoints` — Steps 3–12 every operation call (`ct:`, named args).
- `dotnet-models` — Steps 4–12 request/response mapping, enums, unions, envelopes.
- `dotnet-error-handling` — Step 2 boundary (Case A/B, `TryGet…`, both `JsonException` directions). **Always required.**
- `dotnet-testing` — Step 15.

---

## Assumptions & Blockers

- **Assumption:** Product families, products, price points, components, and coupons already exist in the Maxio site. This integration lists/reads them and does not create catalog entities.
- **Assumption:** eShop `ApplicationUser` / buyer id is stored as Maxio `Customer.Reference` and used with `ReadCustomerByReference`.
- **Assumption:** Card data is tokenized in the browser (Chargify.js / Maxio.js `chargify_token`); the API never receives PAN.
- **Assumption:** Storefront signup has no payment profile (plans are payment-method-not-required). `CreateSubscription.PaymentCollectionMethod` is `CollectionMethod.Remittance` (RI architecture; wire `remittance`) — not `Automatic`, which 422s with "No payment method was on file" when there is a balance and no card. `Invoice` is legacy Statements Architecture only. `enums.md` · `operations/Subscriptions.md`
- **Assumption:** Usage means metered (or prepaid) `CreateUsage`, not Events-Based Billing ingest (`RecordEvent` on the Ebb host). EBB is out of scope unless a SKU is `EventBasedComponent`.
- **Assumption:** Subscription groups, offers, proforma/advance invoices, sales commissions, custom fields, and Insights are out of scope for the first storefront slice.
- **Assumption:** Hosting is US (`ServerEnvironment.Us`) unless config says EU.
- **UNVERIFIED:** `CreateCustomer` 422 payload vs generated `CustomerErrorResponse1.Errors` (`PerPage`/`PricePoint`). Defensive: typed accessor then raw body.
- **UNVERIFIED:** 3DS `action_link` placement on 422 for signup/migrate/reactivate/retry/payment-profile. Defensive: typed list then raw body.
- **Blockers:** none from the SDK map. Site subdomain, API key, and Chargify.js public key are environment/config values the implementer must obtain; they are not SDK-contract gaps.
