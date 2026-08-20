# Maxio Advanced Billing — eShopOnWeb subscription billing

NuGet `AsadAli.AdvancedBilling.Sdk` · root namespace `MaxioAdvancedBilling` · map stamp `v1.0.2` / `15db14b`.

## Scope & sequence

1. **Package + client** — add `AsadAli.AdvancedBilling.Sdk`; construct `MaxioAdvancedBillingClient` via DI (`AddMaxioAdvancedBillingClient`); set Basic auth + `{site}` subdomain + environment.
2. **Catalog map** — `ProductFamilies.ListProductFamilies` / `ReadProductFamily`; `Products.ListProducts` / `ReadProduct` / `ReadProductByHandle`; optional `ProductPricePoints.ListProductPricePoints` / `ReadProductPricePoint`. Persist Maxio `id`/`handle` against eShop catalog SKUs. Do **not** create products at checkout.
3. **Customer** — `Customers.ReadCustomerByReference` (eShop buyer/user id as `reference`); on miss, `CreateCustomer`. Optional `UpdateCustomer`. Never create a second customer with the same `reference`.
4. **Payment profile** — prefer `ChargifyToken` from Maxio.js on `CreatePaymentProfile` or nested on `CreateSubscription`. Fallback: existing `payment_profile_id`. Do not send raw PAN/CVV from the server unless the merchant is PCI-compliant.
5. **Checkout preview** — `Subscriptions.PreviewSubscription` with the same body shape as create.
6. **Create subscription** — `Subscriptions.CreateSubscription` with `product_id` or `product_handle`, `customer_id` or `customer_reference` (or nested `customer_attributes`), optional `payment_profile_id` / `payment_profile_attributes` / `coupon_code(s)` / `components`.
7. **Read status** — `Subscriptions.ReadSubscription` (and `FindSubscription` by app `reference`); `Customers.ListCustomerSubscriptions` for the account page.
8. **Update** — `Subscriptions.UpdateSubscription` (payment method, delayed product change, next billing). Immediate plan change with proration: `SubscriptionProducts.PreviewSubscriptionProductMigration` then `MigrateSubscriptionProduct`.
9. **Cancel / pause** — immediate: `SubscriptionStatus.CancelSubscription`; end-of-period: `InitiateDelayedCancellation` / `CancelDelayedCancellation`; hold: `PauseSubscription` / `ResumeSubscription`; canceled → `ReactivateSubscription`.
10. **Components / usage** (if the store sells add-ons or metered units) — `Components.ListComponents` / `FindComponent`; `SubscriptionComponents.ListSubscriptionComponents` / `AllocateComponent` / `CreateUsage` / `ListUsages`.
11. **Invoices** — `Invoices.ListInvoices` (filter `subscriptionId`) / `ReadInvoice`. Ad-hoc `CreateInvoice` only if the store issues extra charges.
12. **Coupons** — `Coupons.ValidateCoupon` at checkout; `Subscriptions.ApplyCouponsToSubscription` / `RemoveCouponFromSubscription` on an existing sub.
13. **Optional store UX** — `BillingPortal.EnableBillingPortalForCustomer` + `ReadBillingPortalLink` (cache URL until `NewLinkAvailableAt`).
14. **Error boundary + tests** — one throw-only boundary around every SDK call; fake `HttpClient` in tests.

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
| Package id | `AsadAli.AdvancedBilling.Sdk` | `sdk-map.md` |
| Client | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` | `MaxioAdvancedBillingClient.cs` |
| Options | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` | `MaxioAdvancedBillingClientOptions.cs` |
| Only ctor | `MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` |
| DI | `AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>)` on `IServiceCollection` | `ServiceCollectionExtensions.cs` |
| Options members | `Environment`: `MaxioAdvancedBilling.Servers.ServerEnvironment`; `Retry`: `MaxioAdvancedBilling.Core.Configuration.RetryOptions`; `Server`: server-options type from `ServerOptions.cs`; `BasicAuth`: `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md` |
| Auth | HTTP Basic. `BasicAuthCredentials.Username` = API key; `Password` = literal `"x"`. Set before/while constructing. | `sdk-map.md`, `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Environments | `ServerEnvironment.Us` (default, wire `US`) → `https://{site}.chargify.com`; `ServerEnvironment.Eu` (wire `EU`) → `https://{site}.ebilling.maxio.com` | `Servers/ServerEnvironment.cs` |
| Site / mock host | `options.Server.Production.Us.Site = "<subdomain>"` (default `{site}` is `subdomain`). Mock: `options.Server.Production.Us.BaseUrl`. EU: `.Eu.Site` / `.Eu.BaseUrl`. EBB events: `options.Server.Ebb.*.Site` / `.BaseUrl` (`https://events.chargify.com/{site}`). | `sdk-map.md` |
| RetryOptions | Namespace `MaxioAdvancedBilling.Core.Configuration`. All members `required` — full instance or `RetryOptions.Default()`. Members: `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry`. | `Core/Configuration/RetryOptions.cs` |
| Throw model | Every operation is throw-only. No `{Op}Result` / `ApiResult` variants. Errors: `MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>` with `.Error`. Case A: `TError` in `MaxioAdvancedBilling.Errors` with `TryGet…` + inherited `TryGetRawError`. Case B: `TError` is `MaxioAdvancedBilling.Core.ErrorResponse.RawError` (`StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`). Typed errors also inherit `ApiError.TryGetRawError(out RawError)`. | `sdk-map.md`, `Core/Exceptions/SdkException.cs`, `Core/ErrorResponse/` |

Controller properties on the client (this scope): `Customers`, `Subscriptions`, `SubscriptionStatus`, `SubscriptionProducts`, `SubscriptionComponents`, `Products`, `ProductFamilies`, `ProductPricePoints`, `PaymentProfiles`, `Components`, `Invoices`, `Coupons`, `BillingPortal`. Controllers live in `MaxioAdvancedBilling.Api`. Records in `MaxioAdvancedBilling.Models`. Enums in `MaxioAdvancedBilling.Models.Enums`. Unions in `MaxioAdvancedBilling.Models.AnyOf` / `.OneOf`. Error classes in `MaxioAdvancedBilling.Errors`.

### Error payload types (shared)

| Type (`MaxioAdvancedBilling.Models`) | Fields | Map |
|---|---|---|
| `ErrorListResponse1` | `Errors (errors): IReadOnlyList<string> !req` | `records-2-Cr-Ne.md` |
| `ErrorArrayMapResponse1` | `Errors (errors): IReadOnlyDictionary<string, object>?` | `records-2-Cr-Ne.md` |
| `ErrorStringMapResponse1` | `Errors (errors): IReadOnlyDictionary<string, string>?` | `records-2-Cr-Ne.md` |
| `SingleErrorResponse1` | `Error (error): string !req` | `records-3-Of-Su.md` |
| `SingleStringErrorResponse1` | `Errors (errors): string?` | `records-3-Of-Su.md` |
| `CustomerErrorResponse1` | `Errors (errors): Errors?` — and `Errors` is the record `PerPage (per_page): IReadOnlyList<string>?`, `PricePoint (price_point): IReadOnlyList<string>?` (`records-2-Cr-Ne.md`). **Suspicious shared model.** Treat 422 customer bodies as **UNVERIFIED** vs live wire: try `TryGetCustomerErrorResponse1`; if those fields are empty, `TryGetRawError` and `ReadAsString()` / best-effort parse. Do not assume `errors` is a string list. | `records-2-Cr-Ne.md` |
| `CancelSubscriptionErrorResponse` (union, `Models.AnyOf`) | Variants `ErrorListResponse1`, `SingleErrorResponse1`. Factories `CancelSubscriptionErrorResponse.ErrorListResponse1(…)`, `.SingleErrorResponse1(…)`. Read `TryGetErrorListResponse1` / `TryGetSingleErrorResponse1`. | `unions.md` |

---

### Operations

#### Customers — `client.Customers` · `operations/Customers.md` · `Api/Customers.cs`

| Op | Signature | Request | Response envelope (read) | Error | Pagination |
|---|---|---|---|---|---|
| `CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, **must pass explicitly** | Envelope `CreateCustomerRequest`: `Customer (customer): CreateCustomer !req`. Inner `CreateCustomer` **required**: `FirstName (first_name): string`, `LastName (last_name): string`, `Email (email): string`. Optional: `CcEmails (cc_emails)`, `Organization (organization)`, `Reference (reference)`, `Address (address)`, `Address2 (address_2)`, `City (city)`, `State (state)` (ISO 3166-2), `Zip (zip)`, `Country (country)` (ISO 3166-1 alpha-2), `Phone (phone)`, `Locale (locale)`, `VatNumber (vat_number)`, `TaxExempt (tax_exempt)`, `TaxExemptReason (tax_exempt_reason)`, `ParentId (parent_id)`, `SalesforceId (salesforce_id)`. Set `Reference` = eShop buyer id. | `CustomerResponse` → `Customer (customer): Customer !req`. Read `Id`, `Reference`, `Email`, `FirstName`, `LastName`. | Case A `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` | none |
| `ReadCustomer` | `ReadCustomer(int id, CancellationToken ct = default)` | path `id` | same envelope | Case B `SdkException<RawError>` | none |
| `ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | query `reference` ← `reference` | same envelope | Case B `SdkException<RawError>` | none |
| `ListCustomers` | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — first 7 params **must pass explicitly** (`null` to skip) | query: `direction`, `page`, `per_page` ← `perPage`, `date_field` ← `dateField`, `start_date`/`end_date`/`start_datetime`/`end_datetime`, `q` | `IReadOnlyList<CustomerResponse>` → each `.Customer` | Case B | manual `page`+`perPage` |
| `UpdateCustomer` | `UpdateCustomer(int id, UpdateCustomerRequest? body, CancellationToken ct = default)` — `body` **must pass** | Envelope `UpdateCustomerRequest`: `Customer (customer): UpdateCustomer !req`. Inner all optional: `FirstName`, `LastName`, `Email`, `CcEmails`, `Organization`, `Reference`, `Address`, `Address2`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `VatNumber`, `TaxExempt`, `TaxExemptReason`, `ParentId`, `Verified`, `SalesforceId` (same wires as create). | `CustomerResponse` → `.Customer` | Case A `SdkException<UpdateCustomerError>`: `TryGetNoContent(out RawError)` [404] · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError` | none |
| `ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | path `customer_id` ← `customerId` | `IReadOnlyList<SubscriptionResponse>` → each `.Subscription` | Case B | none |
| `DeleteCustomer` | `DeleteCustomer(int id, CancellationToken ct = default)` | path `id` | `void` | Case B | none |

`Customer` response fields used by the store (`records-2-Cr-Ne.md`): `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Organization (organization): string?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, plus address/phone/locale/tax fields as needed.

#### Subscriptions — `client.Subscriptions` · `operations/Subscriptions.md` · `Api/Subscriptions.cs`

| Op | Signature | Request | Response envelope (read) | Error | Pagination |
|---|---|---|---|---|---|
| `CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` **must pass** | Envelope `CreateSubscriptionRequest`: `Subscription (subscription): CreateSubscription !req` (`records-2-Cr-Ne.md`). Identify product with `ProductId (product_id): int?` **or** `ProductHandle (product_handle): string?`. Price point: `ProductPricePointId (product_price_point_id): int?` / `ProductPricePointHandle (product_price_point_handle): string?`. Identify customer: `CustomerId (customer_id): int?` **or** `CustomerReference (customer_reference): string?` **or** nested `CustomerAttributes (customer_attributes): CustomerAttributes?`. Payment: `PaymentProfileId (payment_profile_id): int?` **or** `PaymentProfileAttributes (payment_profile_attributes)` / `CreditCardAttributes (credit_card_attributes)` (both `PaymentProfileAttributes`) **or** `BankAccountAttributes`. Prefer `ChargifyToken (chargify_token)` on payment-profile attributes. Coupons: `CouponCode (coupon_code)` or `CouponCodes (coupon_codes)`. Collection: `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`. App id: `Reference (reference): string?`. Add-ons at signup: `Components (components): IReadOnlyList<CreateSubscriptionComponent>?`. Other optional store-relevant: `NextBillingAt`, `InitialBillingAt`, `DeferSignup` (default `false`), `Currency`, `OfferId` (union `OfferId`), `AgreementAcceptance`, `Metafields`, `SkipBillingManifestTaxes`. | `SubscriptionResponse` → `Subscription (subscription): Subscription?` (**nullable** — null-check). Read `Id`, `State`, `Reference`, `Customer.Id`, `Product.Id`/`Handle`, `CurrentPeriodEndsAt`, `NextAssessmentAt`, `BalanceInCents`, `CouponCode`/`CouponCodes`, `CreditCard.Id`. | Case A `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError`. 422 may include 3DS `action_link` in the raw body — **UNVERIFIED** whether that field is on `ErrorListResponse1`; if `Errors` does not contain it, `TryGetRawError` + `ReadAsString()`. | none |
| `PreviewSubscription` | `PreviewSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — same body type as create; `body` **must pass** | same as create | `SubscriptionPreviewResponse` → `SubscriptionPreview (subscription_preview): SubscriptionPreview !req` → `CurrentBillingManifest` / `NextBillingManifest` (`BillingManifest`: `TotalInCents`, `SubtotalInCents`, `TotalTaxInCents`, `TotalDiscountInCents`, `StartDate`, `EndDate`, `LineItems`). | Case B `SdkException<RawError>` | none |
| `ReadSubscription` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` **must pass** (`null` to skip) | query `include`. Pass `SubscriptionInclude.Coupons` and/or `SubscriptionInclude.SelfServicePageToken` when needed. | `SubscriptionResponse` → `.Subscription?` | Case B | none |
| `FindSubscription` | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` **must pass** | query `reference` | `SubscriptionResponse` → `.Subscription?` | Case A `SdkException<FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError` | none |
| `ListSubscriptions` | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — first 14 params **must pass** | query wires: `state`, `product`, `product_price_point_id`, `coupon`, `coupon_code`, `date_field`, `start_date`, `end_date`, `start_datetime`, `end_datetime`, `metadata`, `direction`, `sort`, `include`, `page`, `per_page` | `IReadOnlyList<SubscriptionResponse>` | Case B | `page`+`perPage` (default 20) |
| `UpdateSubscription` | `UpdateSubscription(int subscriptionId, UpdateSubscriptionRequest? body, CancellationToken ct = default)` — `body` **must pass** | Envelope `UpdateSubscriptionRequest`: `Subscription (subscription): UpdateSubscription !req` (`records-4-Su-We.md`). Store-relevant: `ProductHandle`/`ProductId`, `ProductChangeDelayed (product_change_delayed): bool?` (delayed product change), `NextProductId (next_product_id): string?` (empty string cancels delayed change), `ProductPricePointId`/`ProductPricePointHandle`, `CreditCardAttributes`, `NextBillingAt`, `PaymentCollectionMethod (payment_collection_method): string?` (wire string, **not** the `CollectionMethod` enum on this model), `Reference`, `ReceivesInvoiceEmails`, `ExpiresAt`, `SnapDay` (union `SnapDay1`), `Components`. | `SubscriptionResponse` → `.Subscription?` | Case A `SdkException<UpdateSubscriptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `ApplyCouponsToSubscription` | `ApplyCouponsToSubscription(int subscriptionId, string? code, AddCouponsRequest? body, CancellationToken ct = default)` — `code` and `body` **must pass**. Query `code` **replaces** all coupons (deprecated). Prefer body. | `AddCouponsRequest`: `Codes (codes): IReadOnlyList<string>?` | `SubscriptionResponse` | Case A `SdkException<ApplyCouponsToSubscriptionError>`: `TryGetSubscriptionAddCouponError1(out SubscriptionAddCouponError1)` [422] · `TryGetRawError`. Payload: `Codes`, `CouponCode`, `CouponCodes`, `Subscription` — each `IReadOnlyList<string>?`. | none |
| `RemoveCouponFromSubscription` | `RemoveCouponFromSubscription(int subscriptionId, string? couponCode, CancellationToken ct = default)` — `couponCode` **must pass** | query `coupon_code` ← `couponCode` | `string` (not an envelope) | Case A `SdkException<RemoveCouponFromSubscriptionError>`: `TryGetSubscriptionRemoveCouponErrors1(out SubscriptionRemoveCouponErrors1)` [422] · `TryGetRawError`. Payload: `Subscription (subscription): IReadOnlyList<string> !req`. | none |
| `ActivateSubscription` | `ActivateSubscription(int subscriptionId, ActivateSubscriptionRequest? body, CancellationToken ct = default)` — `body` **must pass** | `ActivateSubscriptionRequest`: `RevertOnFailure (revert_on_failure): bool?` | `SubscriptionResponse` | Case A `SdkException<ActivateSubscriptionError>`: `TryGetErrorArrayMapResponse1` [400] · `TryGetRawError` | none |

`CreateSubscriptionComponent` (`records-2-Cr-Ne.md`): `ComponentId (component_id): ComponentId1?` (union int/string), `Enabled (enabled): bool?`, `UnitBalance (unit_balance): int?`, `AllocatedQuantity (allocated_quantity): AllocatedQuantity3?` (union), `Quantity (quantity): int?`, `PricePointId (price_point_id): PricePointId2?` (union), `CustomPrice`.

`CustomerAttributes` (`records-2-Cr-Ne.md`) — all optional: `FirstName`, `LastName`, `Email`, `CcEmails`, `Organization`, `Reference`, `Address`, `Address2`, `City`, `State`, `Zip`, `Country`, `Phone`, `Verified`, `TaxExempt`, `VatNumber`, `Metafields`, `ParentId`, `SalesforceId`, `DefaultAutoRenewalProfileId`.

`PaymentProfileAttributes` (`records-3-Of-Su.md`): `ChargifyToken (chargify_token)`, `FullNumber (full_number)`, `ExpirationMonth`/`ExpirationYear` (unions `ExpirationMonth2`/`ExpirationYear2`), `FirstName`, `LastName`, `CardType`, billing address fields, `Cvv`, `PaymentType`, `VaultToken`, `GatewayHandle`, `CustomerId`, `PaypalEmail`, `PaymentMethodNonce`, `LastFour`, `CurrentVault`. **Use `ChargifyToken`, not `FullNumber`/`Cvv`, unless PCI.**

`Subscription` inner fields the store reads (`records-3-Of-Su.md`): `Id`, `State`, `BalanceInCents`, `ProductPriceInCents`, `CurrentPeriodEndsAt`, `CurrentPeriodStartedAt`, `NextAssessmentAt`, `TrialStartedAt`/`TrialEndedAt`, `ActivatedAt`, `CanceledAt`, `CancelAtEndOfPeriod`, `DelayedCancelAt`, `ExpiresAt`, `Reference`, `CouponCode`/`CouponCodes`, `PaymentCollectionMethod`, `Customer` (`Customer?`), `Product` (`Product?`), `CreditCard` (`CreditCardPaymentProfile?`), `BankAccount`, `ProductPricePointId`, `SelfServicePageToken` (only if included), `AutomaticallyResumeAt`, `OnHoldAt`, `Currency`.

Out of store checkout path (do not call unless importing/purging test data): `OverrideSubscription`, `PurgeSubscription`, `UpdatePrepaidSubscriptionConfiguration`.

#### SubscriptionStatus — `client.SubscriptionStatus` · `operations/SubscriptionStatus.md`

| Op | Signature | Request | Response | Error |
|---|---|---|---|---|
| `CancelSubscription` | `CancelSubscription(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` — `body` **must pass** | Envelope `CancellationRequest`: `Subscription (subscription): CancellationOptions !req`. Inner: `CancellationMessage (cancellation_message)`, `ReasonCode (reason_code)`, `CancelAtEndOfPeriod (cancel_at_end_of_period)`, `ScheduledCancellationAt (scheduled_cancellation_at)`, `RefundPrepaymentAccountBalance (refund_prepayment_account_balance)`. Immediate cancel: omit schedule fields (still pass a body or `null` explicitly). | `SubscriptionResponse` → `.Subscription?` | Case A `SdkException<CancelSubscriptionApiError>` (**not** `CancelSubscriptionError`): `TryGetNoContent` [404] · `TryGetCancelSubscriptionErrorResponse(out CancelSubscriptionErrorResponse)` [422] · `TryGetRawError`. Then on the union: `TryGetErrorListResponse1` / `TryGetSingleErrorResponse1`. |
| `InitiateDelayedCancellation` | `InitiateDelayedCancellation(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` — `body` **must pass** | same `CancellationRequest` | `DelayedCancellationResponse`: `Message (message): string?` | Case A `SdkException<InitiateDelayedCancellationError>`: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `CancelDelayedCancellation` | `CancelDelayedCancellation(int subscriptionId, CancellationToken ct = default)` | — | `DelayedCancellationResponse` | Case A `SdkException<CancelDelayedCancellationError>`: `TryGetNoContent` [404] · `TryGetRawError` |
| `PauseSubscription` | `PauseSubscription(int subscriptionId, PauseRequest? body, CancellationToken ct = default)` — `body` **must pass** | `PauseRequest`: `Hold (hold): AutoResume?`. `AutoResume`: `AutomaticallyResumeAt (automatically_resume_at): DateTimeOffset?`. Cannot hold if `next_billing_at` is within 24 hours (API rule in op notes). | `SubscriptionResponse` | Case A `SdkException<PauseSubscriptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `UpdateAutomaticSubscriptionResumption` | `UpdateAutomaticSubscriptionResumption(int subscriptionId, PauseRequest? body, CancellationToken ct = default)` — `body` **must pass** | same `PauseRequest`; set `AutomaticallyResumeAt` to `null` to clear | `SubscriptionResponse` | Case A `SdkException<UpdateAutomaticSubscriptionResumptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `ResumeSubscription` | `ResumeSubscription(int subscriptionId, ResumptionCharge? calendarBillingResumptionCharge, CancellationToken ct = default)` — `calendarBillingResumptionCharge` **must pass** | query `calendar_billing['resumption_charge']` ← `calendarBillingResumptionCharge` | `SubscriptionResponse` | Case A `SdkException<ResumeSubscriptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `ReactivateSubscription` | `ReactivateSubscription(int subscriptionId, ReactivateSubscriptionRequest? body, CancellationToken ct = default)` — `body` **must pass** | `ReactivateSubscriptionRequest`: `CalendarBilling (calendar_billing): ReactivationBilling?` (`ReactivationCharge` default `Prorated`), `IncludeTrial (include_trial): bool?`, `PreserveBalance (preserve_balance): bool?`, `CouponCode (coupon_code): string?`, `UseCreditsAndPrepayments (use_credits_and_prepayments): bool?`, `Resume (resume): Resume?` (union: `Resume.Bool(bool)` or `Resume.ResumeOptions(ResumeOptions)` with `RequireResume`, `ForgiveBalance`). | `SubscriptionResponse` | Case A `SdkException<ReactivateSubscriptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `RetrySubscription` | `RetrySubscription(int subscriptionId, CancellationToken ct = default)` | — | `SubscriptionResponse` | Case A `SdkException<RetrySubscriptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `CancelDunning` | `CancelDunning(int subscriptionId, CancellationToken ct = default)` | — | `SubscriptionResponse` | Case A `SdkException<CancelDunningError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `PreviewRenewal` | `PreviewRenewal(int subscriptionId, RenewalPreviewRequest? body, CancellationToken ct = default)` — `body` **must pass** | optional component quantity overrides in body (only if showing next-charge UI) | `RenewalPreviewResponse` | Case A `SdkException<PreviewRenewalError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` |

#### SubscriptionProducts — `client.SubscriptionProducts` · `operations/SubscriptionProducts.md`

| Op | Signature | Request | Response | Error |
|---|---|---|---|---|
| `PreviewSubscriptionProductMigration` | `PreviewSubscriptionProductMigration(int subscriptionId, SubscriptionMigrationPreviewRequest? body, CancellationToken ct = default)` — `body` **must pass** | Envelope: `Migration (migration): SubscriptionMigrationPreviewOptions !req`. Inner: `ProductId`/`ProductHandle`, `ProductPricePointId`/`ProductPricePointHandle`, `IncludeTrial` (default `false`), `IncludeInitialCharge` (default `false`), `IncludeCoupons` (default `true`), `PreservePeriod` (default `false`), `Proration`, `ProrationDate`. | `SubscriptionMigrationPreviewResponse` → `Migration (migration): SubscriptionMigrationPreview !req`: `ProratedAdjustmentInCents`, `ChargeInCents`, `PaymentDueInCents`, `CreditAppliedInCents`. | Case A `SdkException<PreviewSubscriptionProductMigrationError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `MigrateSubscriptionProduct` | `MigrateSubscriptionProduct(int subscriptionId, SubscriptionProductMigrationRequest? body, CancellationToken ct = default)` — `body` **must pass** | Envelope: `Migration (migration): SubscriptionProductMigration !req`. Same product/price-point/include flags as preview **minus** `ProrationDate`. `Proration (proration): Proration?` (`PreservePeriod (preserve_period): bool?`). Valid source states: `active` / `trialing`. | `SubscriptionResponse` | Case A `SdkException<MigrateSubscriptionProductError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` |

#### Products / ProductFamilies / ProductPricePoints

`client.Products` · `operations/Products.md`

| Op | Signature | Notes | Response | Error | Pagination |
|---|---|---|---|---|---|
| `ListProducts` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — first 8 **must pass** | query `date_field`, `filter`, `end_date`, `end_datetime`, `start_date`, `start_datetime`, `page`, `per_page`, `include_archived`, `include`. Filter model: `Ids`, `PrepaidProductPricePoint`, `UseSiteExchangeRate`. | `IReadOnlyList<ProductResponse>` → each `Product (product): Product !req`. Read `Id`, `Name`, `Handle`, `PriceInCents`, `Interval`, `IntervalUnit`, `RequireCreditCard`, `ProductFamily.Id`/`Handle`, `ProductPricePointId`, `ArchivedAt`. | Case B | `page`+`perPage` (20) |
| `ReadProduct` | `ReadProduct(int productId, CancellationToken ct = default)` | | `ProductResponse` → `.Product` | Case B | none |
| `ReadProductByHandle` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | path `api_handle` | `ProductResponse` → `.Product` | Case B | none |

Catalog **admin** (`CreateProduct`, `UpdateProduct`, `ArchiveProduct`) is out of the store checkout path; signatures exist on the same controller if a seed job is added later (`CreateProduct(string productFamilyId, CreateOrUpdateProductRequest? body, …)` — `body` **must pass**; Case A `CreateProductError` / `TryGetErrorListResponse1` [422]).

`client.ProductFamilies` · `operations/ProductFamilies.md`

| Op | Signature | Response | Error | Pagination |
|---|---|---|---|---|
| `ListProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 **must pass** | `IReadOnlyList<ProductFamilyResponse>` → `ProductFamily (product_family): ProductFamily?`. Read `Id`, `Name`, `Handle`. | Case B | none |
| `ReadProductFamily` | `ReadProductFamily(int id, CancellationToken ct = default)` | `ProductFamilyResponse` → `.ProductFamily?` | Case B | none |
| `ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 filter params **must pass**. Path `product_family_id` is **`string`** (id or `handle:…`). | `IReadOnlyList<ProductResponse>` | Case A `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError` | `page`+`perPage` |

`ProductFamily` (`records-3-Of-Su.md`): `Id`, `Name`, `Handle`, `AccountingCode`, `Description`, `CreatedAt`, `UpdatedAt`, `ArchivedAt`.

`client.ProductPricePoints` · `operations/ProductPricePoints.md`

| Op | Signature | Response | Error | Pagination |
|---|---|---|---|---|
| `ListProductPricePoints` | `ListProductPricePoints(ProductIdModel productId, bool? currencyPrices, IReadOnlyList<PricePointType>? filterType, bool? archived, int? page = 1, int? perPage = 10, CancellationToken ct = default)` — `currencyPrices`/`filterType`/`archived` **must pass**. `productId` is union `ProductIdModel.Int(int)` or `.String(string)` (`Models.AnyOf`). Query `filter[type]` ← `filterType`. | `ListProductPricePointsResponse` → `PricePoints (price_points): IReadOnlyList<ProductPricePoint> !req` (not an envelope-per-item list). Read `Id`, `Name`, `Handle`, `PriceInCents`, `Interval`, `IntervalUnit`, `Type`. | Case B | `page`+`perPage` (default **10**) |
| `ReadProductPricePoint` | `ReadProductPricePoint(ProductIdModel productId, PricePointIdModel pricePointId, bool? currencyPrices, CancellationToken ct = default)` — `currencyPrices` **must pass**. `pricePointId`: `PricePointIdModel.Int` / `.String`. | `ProductPricePointResponse` → `PricePoint (price_point): ProductPricePoint !req` | Case B | none |

#### PaymentProfiles — `client.PaymentProfiles` · `operations/PaymentProfiles.md`

| Op | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `CreatePaymentProfile` | `CreatePaymentProfile(CreatePaymentProfileRequest? body, CancellationToken ct = default)` — `body` **must pass** | Envelope: `PaymentProfile (payment_profile): CreatePaymentProfile !req`. Store path: `ChargifyToken (chargify_token)` + `CustomerId (customer_id)`. Other card fields (`FullNumber`, `Cvv`, `ExpirationMonth` union `ExpirationMonth1`, `ExpirationYear` union `ExpirationYear1`) are PCI. Also: `PaymentType`, `FirstName`, `LastName`, billing address, `GatewayHandle`. | `PaymentProfileResponse` → `PaymentProfile (payment_profile): PaymentProfile !req` (**OneOf** `Models.OneOf`). Read via `TryGetCreditCardPaymentProfile` / `TryGetBankAccountPaymentProfile` / `TryGetPaypalPaymentProfile` / `TryGetApplePayPaymentProfile`. Credit-card variant: `Id`, `MaskedCardNumber`, `CardType`, `ExpirationMonth`/`Year` (`int?`), `CustomerId`, `PaymentType` default `CreditCard`. | Case A `SdkException<CreatePaymentProfileError>`: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `ListPaymentProfiles` | `ListPaymentProfiles(int? customerId, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — `customerId` **must pass** | query `customer_id`, `page`, `per_page` | `IReadOnlyList<PaymentProfileResponse>` | Case B | `page`+`perPage` |
| `ReadPaymentProfile` | `ReadPaymentProfile(int paymentProfileId, CancellationToken ct = default)` | | `PaymentProfileResponse` (OneOf inner) | Case A `SdkException<ReadPaymentProfileError>`: `TryGetNoContent` [404] · `TryGetRawError` | none |
| `ChangeSubscriptionDefaultPaymentProfile` | `ChangeSubscriptionDefaultPaymentProfile(int subscriptionId, int paymentProfileId, CancellationToken ct = default)` | | `PaymentProfileResponse` | Case A `SdkException<ChangeSubscriptionDefaultPaymentProfileError>`: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `UpdatePaymentProfile` | `UpdatePaymentProfile(int paymentProfileId, UpdatePaymentProfileRequest? body, CancellationToken ct = default)` — `body` **must pass** | Envelope: `PaymentProfile (payment_profile): UpdatePaymentProfile !req` — billing/contact + optional `FullNumber`/`ExpirationMonth`/`ExpirationYear` as **strings**. | `PaymentProfileResponse` | Case A `SdkException<UpdatePaymentProfileError>`: `TryGetNoContent` [404] · `TryGetErrorStringMapResponse1` [422] · `TryGetRawError` | none |
| `DeleteSubscriptionsPaymentProfile` | `DeleteSubscriptionsPaymentProfile(int subscriptionId, int paymentProfileId, CancellationToken ct = default)` | | `void` | Case B | none |
| `ReadOneTimeToken` | `ReadOneTimeToken(string chargifyToken, CancellationToken ct = default)` | path `chargify_token` | `GetOneTimeTokenRequest` → `PaymentProfile (payment_profile): GetOneTimeTokenPaymentProfile !req` | Case A `SdkException<ReadOneTimeTokenError>`: `TryGetErrorListResponse1` [404] · `TryGetRawError` | none |

#### Components / SubscriptionComponents

`client.Components` · `operations/Components.md` — catalog lookup only for the store:

| Op | Signature | Response | Error | Pagination |
|---|---|---|---|---|
| `ListComponents` | `ListComponents(BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, bool? includeArchived, ListComponentsFilter? filter, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — first 7 **must pass** | `IReadOnlyList<ComponentResponse>` → `Component (component): Component !req`. Read `Id`, `Name`, `Handle`, `Kind`, `UnitName`, `PricingScheme`, `ProductFamilyId`. | Case B | `page`+`perPage` |
| `ListComponentsForProductFamily` | `ListComponentsForProductFamily(int productFamilyId, bool? includeArchived, ListComponentsFilter? filter, BasicDateField? dateField, string? endDate, string? endDatetime, string? startDate, string? startDatetime, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 7 filters **must pass**. Path `productFamilyId` is **`int`**. | same | Case B | `page`+`perPage` |
| `FindComponent` | `FindComponent(string handle, CancellationToken ct = default)` | query `handle` → `ComponentResponse` | Case B | none |
| `ReadComponent` | `ReadComponent(int productFamilyId, string componentId, CancellationToken ct = default)` | `componentId` may be id or `handle:…` → `ComponentResponse` | Case B | none |

`client.SubscriptionComponents` · `operations/SubscriptionComponents.md`

| Op | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `ListSubscriptionComponents` | `ListSubscriptionComponents(int subscriptionId, SubscriptionListDateField? dateField, SortingDirection? direction, ListSubscriptionComponentsFilter? filter, string? endDate, string? endDatetime, IncludeNotNull? pricePointIds, IReadOnlyList<int>? productFamilyIds, ListSubscriptionComponentsSort? sort, string? startDate, string? startDatetime, IReadOnlyList<ListSubscriptionComponentsInclude>? include, bool? inUse, CancellationToken ct = default)` — all 12 filters **must pass** | | `IReadOnlyList<SubscriptionComponentResponse>` → `Component (component): SubscriptionComponent?`. Read `ComponentId`, `ComponentHandle`, `Kind`, `AllocatedQuantity` (union `AllocatedQuantity2`: `TryGetInt`/`TryGetString`), `UnitBalance`, `Enabled`, `PricePointId`. | Case B | none |
| `ReadSubscriptionComponent` | `ReadSubscriptionComponent(int subscriptionId, int componentId, CancellationToken ct = default)` | | `SubscriptionComponentResponse` | Case A `SdkException<ReadSubscriptionComponentError>`: `TryGetNoContent` [404] · `TryGetRawError` | none |
| `AllocateComponent` | `AllocateComponent(int subscriptionId, int componentId, CreateAllocationRequest? body, CancellationToken ct = default)` — `body` **must pass** | Envelope: `Allocation (allocation): CreateAllocation !req`. Inner **required**: `Quantity (quantity): double`. Optional: `Memo`, `ComponentId`, `PricePointId` (union `PricePointId1`), `UpgradeCharge` (`UpgradeChargeCreditType`), `DowngradeCredit` (`DowngradeCreditCreditType`), `AccrueCharge`, `InitiateDunning`. | `AllocationResponse` → `Allocation (allocation): Allocation?`. Read `AllocationId`, `Quantity` (union `Quantity`), `ComponentId`. | Case A `SdkException<AllocateComponentError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `PreviewAllocations` | `PreviewAllocations(int subscriptionId, PreviewAllocationsRequest? body, CancellationToken ct = default)` — `body` **must pass** | `Allocations (allocations): IReadOnlyList<CreateAllocation> !req`, plus optional proration fields | `AllocationPreviewResponse` → `AllocationPreview (allocation_preview): AllocationPreview !req` (`TotalInCents`, `LineItems`) | Case A `SdkException<PreviewAllocationsError>`: `TryGetComponentAllocationError1(out ComponentAllocationError1)` [422] · `TryGetRawError`. Payload: `Errors: IReadOnlyList<ComponentAllocationErrorItem>?` (`ComponentId`, `Message`, `Kind`, `On`). | none |
| `CreateUsage` | `CreateUsage(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, CreateUsageRequest? body, CancellationToken ct = default)` — `body` **must pass**. Path unions: `SubscriptionIdOrReference.Int(int)` / `.String(string)`; `ComponentIdModel.Int(int)` / `.String(string)` (handle as string). | Envelope: `Usage (usage): CreateUsage !req`. Inner: `Quantity (quantity): double?`, `PricePointId (price_point_id): string?`, `Memo (memo): string?`. Negative quantity deducts; `unit_balance` floor is 0 (op notes). | `UsageResponse` → `Usage (usage): Usage !req`. Read `Id`, `Quantity` (union `Quantity1`), `ComponentId`, `SubscriptionId`. | Case A `SdkException<CreateUsageError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `ListUsages` | `ListUsages(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, long? sinceId, long? maxId, DateTimeOffset? sinceDate, DateTimeOffset? untilDate, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 4 filters **must pass** | query `since_id`, `max_id`, `since_date`, `until_date`, `page`, `per_page` | `IReadOnlyList<UsageResponse>` | Case B | `page`+`perPage` |

EBB ingest (`RecordEvent` / `BulkRecordEvents`) uses the **Ebb** server group, not Production — only if the store streams events-based usage.

#### Invoices — `client.Invoices` · `operations/Invoices.md`

| Op | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `ListInvoices` | `ListInvoices(string? startDate, string? endDate, InvoiceStatus? status, int? subscriptionId, string? subscriptionGroupUid, string? consolidationLevel, Direction? direction, InvoiceDateField? dateField, string? startDatetime, string? endDatetime, IReadOnlyList<int>? customerIds, IReadOnlyList<string>? number, IReadOnlyList<int>? productIds, InvoiceSortField? sort, int? page = 1, int? perPage = 20, bool? lineItems = false, bool? discounts = false, bool? taxes = false, bool? credits = false, bool? payments = false, bool? customFields = false, bool? refunds = false, CancellationToken ct = default)` — first 14 **must pass**. Breakdown flags default `false` — set `true` when the UI needs lines. | query `subscription_id` ← `subscriptionId`, `status`, `start_date`, `end_date`, `page`, `per_page`, `line_items`, … | `ListInvoicesResponse` → `Invoices (invoices): IReadOnlyList<Invoice> !req` (**not** `InvoiceResponse` wrappers). Read `Uid`, `Number`, `Status`, `TotalAmount`, `DueAmount`, `PaidAmount`, `IssueDate`, `DueDate`, `PublicUrl`, `SubscriptionId`. | Case B | `page`+`perPage` |
| `ReadInvoice` | `ReadInvoice(string uid, CancellationToken ct = default)` | path `uid` | **`Invoice` unwrapped** (not `InvoiceResponse`) | Case B | none |
| `CreateInvoice` | `CreateInvoice(int subscriptionId, CreateInvoiceRequest? body, CancellationToken ct = default)` — `body` **must pass** | Envelope: `Invoice (invoice): CreateInvoice !req`. `LineItems (line_items): IReadOnlyList<CreateInvoiceItem>?` (`Title`/`Quantity` union `Quantity3`/`UnitPrice` union `UnitPrice7`, or catalog `ProductId`/`ComponentId` unions). Optional `Coupons`, `Status` default `CreateInvoiceStatus.Open`. | `InvoiceResponse` → `Invoice (invoice): Invoice !req` | Case A `SdkException<CreateInvoiceError>`: `TryGetErrorArrayMapResponse1` [422] · `TryGetRawError` | none |
| `IssueInvoice` | `IssueInvoice(string uid, IssueInvoiceRequest? body, CancellationToken ct = default)` — `body` **must pass** | `OnFailedPayment (on_failed_payment): FailedPaymentAction?` default `LeaveOpenInvoice` | **`Invoice` unwrapped** | Case A `SdkException<IssueInvoiceError>`: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `RecordPaymentForInvoice` | `RecordPaymentForInvoice(string uid, CreateInvoicePaymentRequest? body, CancellationToken ct = default)` — `body` **must pass** | Envelope: `Payment (payment): CreateInvoicePayment !req`; optional `Type (type): InvoicePaymentType?` | **`Invoice` unwrapped** | Case A `SdkException<RecordPaymentForInvoiceError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `RecordPaymentForSubscription` | `RecordPaymentForSubscription(int subscriptionId, RecordPaymentRequest? body, CancellationToken ct = default)` — `body` **must pass** | Envelope: `Payment (payment): CreatePayment !req` — `Amount`, `Memo`, `PaymentDetails`, `PaymentMethod` all `!req` | `RecordPaymentResponse`: `PaidInvoices`, `Prepayment` | Case A `SdkException<RecordPaymentForSubscriptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `VoidInvoice` | `VoidInvoice(string uid, VoidInvoiceRequest? body, CancellationToken ct = default)` — `body` **must pass** | Envelope: `Void (void): VoidInvoice !req` with `Reason (reason): string !req` | **`Invoice` unwrapped** | Case A `SdkException<VoidInvoiceError>`: `TryGetObject(out object?)` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `RefundInvoice` | `RefundInvoice(string uid, RefundInvoiceRequest? body, CancellationToken ct = default)` — `body` **must pass** | Envelope: `Refund (refund): Refund !req` (union `Refund.RefundInvoice(RefundInvoice)` / `.RefundConsolidatedInvoice(…)`). `RefundInvoice`: `Amount`, `Memo`, `PaymentId` all `!req`. | **`Invoice` unwrapped** | Case A `SdkException<RefundInvoiceError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `SendInvoice` | `SendInvoice(string uid, SendInvoiceRequest? body, CancellationToken ct = default)` — `body` **must pass** | | `void` | Case A `SdkException<SendInvoiceError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |

**Envelope trap:** `CreateInvoice` returns `InvoiceResponse.Invoice`; `ReadInvoice` / `IssueInvoice` / payment / void / refund return bare `Invoice`.

#### Coupons — `client.Coupons` · `operations/Coupons.md`

| Op | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `ValidateCoupon` | `ValidateCoupon(string code, int? productFamilyId, CancellationToken ct = default)` — `productFamilyId` **must pass** (`null` if the coupon is on the site’s first/default family) | query `code`, `product_family_id` | `CouponResponse` → `Coupon (coupon): Coupon?`. Read `Id`, `Code`, `Name`, `Percentage`, `AmountInCents`, `Recurring`, `Stackable`, `EndDate`, `ProductFamilyId`. | Case A `SdkException<ValidateCouponError>`: `TryGetSingleStringErrorResponse1(out SingleStringErrorResponse1)` [404] · `TryGetRawError`. Payload: `Errors (errors): string?`. | none |
| `FindCoupon` | `FindCoupon(int? productFamilyId, string? code, bool? currencyPrices, CancellationToken ct = default)` — all three **must pass** | query `product_family_id`, `code`, `currency_prices` | `CouponResponse` | Case B | none |
| `ListCoupons` | `ListCoupons(ListCouponsFilter? filter, bool? currencyPrices, int? page = 1, int? perPage = 30, CancellationToken ct = default)` — `filter`/`currencyPrices` **must pass** | `ListCouponsFilter`: `DateField`, date range, `Ids`, `Codes`, `UseSiteExchangeRate`, `IncludeArchived` | `IReadOnlyList<CouponResponse>` | Case B | `page`+`perPage` (default **30**) |
| `ReadCoupon` | `ReadCoupon(int productFamilyId, int couponId, bool? currencyPrices, CancellationToken ct = default)` — `currencyPrices` **must pass** | | `CouponResponse` | Case B | none |

Catalog-admin create/update/archive of coupons is out of checkout; apply/remove on a subscription is on `client.Subscriptions` (above).

#### BillingPortal (optional account UX) — `client.BillingPortal` · `operations/BillingPortal.md`

| Op | Signature | Response | Error |
|---|---|---|---|
| `EnableBillingPortalForCustomer` | `EnableBillingPortalForCustomer(int customerId, AutoInvite? autoInvite, CancellationToken ct = default)` — `autoInvite` **must pass**. `AutoInvite` is **IntEnum**: `Value0 (0)`, `Value1 (1)`. Query `auto_invite`. | `CustomerResponse` | Case A `SdkException<EnableBillingPortalForCustomerError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `ReadBillingPortalLink` | `ReadBillingPortalLink(int customerId, CancellationToken ct = default)` | `PortalManagementLink`: `Url`, `FetchCount`, `CreatedAt`, `NewLinkAvailableAt`, `ExpiresAt`, `LastInviteSentAt`. Cache until `NewLinkAvailableAt`. | Case A `SdkException<ReadBillingPortalLinkError>`: `TryGetErrorListResponse1` [422] · `TryGetTooManyManagementLinkRequestsError1(out TooManyManagementLinkRequestsError1)` [429] · `TryGetRawError`. 429 payload: `Errors (errors): TooManyManagementLinkRequests !req` with `Error (error): string !req`, `NewLinkAvailableAt (new_link_available_at): DateTimeOffset !req`. |

---

### Enums in scope (`MaxioAdvancedBilling.Models.Enums` — `StringEnum<T>` / `IntEnum<T>`, **not** C# enums)

Construct with static members (e.g. `CollectionMethod.Automatic`) or `Type.FromValue("wire")`. Source: `map/models/enums.md`.

| Enum | Members (`CSharp (wire)`) |
|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `SubscriptionStateFilter` | `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)` |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` — Relationship Invoicing: `remittance`/`automatic`/`prepaid`; legacy statements: `invoice`/`automatic` |
| `IntervalUnit` | `Day (day)`, `Month (month)` |
| `PricePointType` | `Catalog (catalog)`, `Default (default)`, `Custom (custom)` |
| `ComponentKind` | `MeteredComponent (metered_component)`, `QuantityBasedComponent (quantity_based_component)`, `OnOffComponent (on_off_component)`, `PrepaidUsageComponent (prepaid_usage_component)`, `EventBasedComponent (event_based_component)` |
| `InvoiceStatus` | `Draft (draft)`, `Open (open)`, `Paid (paid)`, `Pending (pending)`, `Voided (voided)`, `Canceled (canceled)`, `Processing (processing)` |
| `InvoiceDateField` | `CreatedAt (created_at)`, `DueDate (due_date)`, `IssueDate (issue_date)`, `UpdatedAt (updated_at)`, `PaidDate (paid_date)` |
| `InvoiceSortField` | `Status (status)`, `TotalAmount (total_amount)`, `DueAmount (due_amount)`, `CreatedAt (created_at)`, `UpdatedAt (updated_at)`, `IssueDate (issue_date)`, `DueDate (due_date)`, `Number (number)` |
| `Direction` / `SortingDirection` | both: `Asc (asc)`, `Desc (desc)` |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` |
| `SubscriptionDateField` | `CurrentPeriodEndsAt (current_period_ends_at)`, `CurrentPeriodStartsAt (current_period_starts_at)`, `CreatedAt (created_at)`, `ActivatedAt (activated_at)`, `CanceledAt (canceled_at)`, `ExpiresAt (expires_at)`, `TrialStartedAt (trial_started_at)`, `TrialEndedAt (trial_ended_at)`, `UpdatedAt (updated_at)` |
| `SubscriptionSort` | `SignupDate (signup_date)`, `PeriodStart (period_start)`, `PeriodEnd (period_end)`, `NextAssessment (next_assessment)`, `UpdatedAt (updated_at)`, `CreatedAt (created_at)`, `TotalPayments (total_payments)`, `Id (id)`, `OpenBalance (open_balance)`, `ExpiresAt (expires_at)` |
| `SubscriptionInclude` | `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)` |
| `SubscriptionListInclude` | `SelfServicePageToken (self_service_page_token)` |
| `ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)` |
| `PaymentType` | `CreditCard (credit_card)`, `BankAccount (bank_account)`, `PaypalAccount (paypal_account)`, `ApplePay (apple_pay)` |
| `CardType` | `Visa (visa)`, `Master (master)`, `Discover (discover)`, `AmericanExpress (american_express)`, `Jcb (jcb)`, `DinersClub (diners_club)`, `Bogus (bogus)`, plus others in `enums.md` |
| `CreditCardVault` / `AllVaults` | include `Bogus (bogus)` for test sites; full lists in `enums.md` |
| `CreditType` / `UpgradeChargeCreditType` / `DowngradeCreditCreditType` | `Full (full)`, `Prorated (prorated)`, `None (none)` |
| `ResumptionCharge` / `ReactivationCharge` | `Prorated (prorated)`, `Immediate (immediate)`, `Delayed (delayed)` |
| `FailedPaymentAction` | `LeaveOpenInvoice (leave_open_invoice)`, `RollbackToPending (rollback_to_pending)`, `InitiateDunning (initiate_dunning)` |
| `CreateInvoiceStatus` | `Draft (draft)`, `Open (open)` |
| `InvoicePaymentType` | `External (external)`, `Prepayment (prepayment)`, `ServiceCredit (service_credit)`, `Payment (payment)` |
| `InvoicePaymentMethodType` | `CreditCard (credit_card)`, `Check (check)`, `Cash (cash)`, `MoneyOrder (money_order)`, `Ach (ach)`, `Other (other)` |
| `DiscountType` | `Amount (amount)`, `Percent (percent)` |
| `CompoundingStrategy` | `Compound (compound)`, `FullPrice (full-price)` |
| `RecurringScheme` | `DoNotRecur (do_not_recur)`, `RecurIndefinitely (recur_indefinitely)`, `RecurWithDuration (recur_with_duration)` |
| `PricingScheme` | `Stairstep (stairstep)`, `Volume (volume)`, `PerUnit (per_unit)`, `Tiered (tiered)` |
| `CancellationMethod` | `MerchantUi (merchant_ui)`, `MerchantApi (merchant_api)`, `Dunning (dunning)`, `BillingPortal (billing_portal)`, `Unknown (unknown)`, `Imported (imported)` |
| `ListSubscriptionComponentsInclude` | `Subscription (subscription)`, `HistoricUsages (historic_usages)` |
| `ListSubscriptionComponentsSort` | `Id (id)`, `UpdatedAt (updated_at)` |
| `SubscriptionListDateField` | `UpdatedAt (updated_at)` |
| `IncludeNotNull` | `NotNull (not_null)` |
| `AutoInvite` | IntEnum `Value0 (0)`, `Value1 (1)` |
| `CreditScheme` | `None (none)`, `Credit (credit)`, `Refund (refund)` |

### Unions in scope (`MaxioAdvancedBilling.Models.AnyOf` unless noted)

| Union | Build | Read |
|---|---|---|
| `SubscriptionIdOrReference` | `.Int(int)` / `.String(string)` | `TryGetInt` / `TryGetString` |
| `ComponentIdModel` / `ProductIdModel` / `PricePointIdModel` | `.Int(int)` / `.String(string)` | `TryGetInt` / `TryGetString` |
| `AllocatedQuantity2` / `Quantity` / `Quantity1` | `.Int(int)` / `.String(string)` | `TryGetInt` / `TryGetString` |
| `Resume` | `.Bool(bool)` / `.ResumeOptions(ResumeOptions)` | `TryGetBool` / `TryGetResumeOptions` |
| `PaymentProfile` (`Models.OneOf`) | factories per variant | `TryGetCreditCardPaymentProfile` / `TryGetBankAccountPaymentProfile` / `TryGetPaypalPaymentProfile` / `TryGetApplePayPaymentProfile` |
| `CancelSubscriptionErrorResponse` | `.ErrorListResponse1` / `.SingleErrorResponse1` | matching `TryGet…` |
| `SnapDay1` (on `UpdateSubscription`) | `.String(string)` / `.Int(int)` | `TryGetString` / `TryGetInt` |
| `OfferId` | `.String(string)` / `.Int(int)` | `TryGetString` / `TryGetInt` |
| `NetTerms1` | `.String(string)` / `.Int(int)` | `TryGetString` / `TryGetInt` |
| `Percentage` / `Amount` family | `.String` / `.Double` (check `unions.md` for which variant is first) | matching `TryGet…` |

---

## Trap notes

⚠ Step 1 (client registration) — `HttpClient` lifetime vs the SDK wrapper, and whether the DI extension owns the handler pipeline, are not visible from the constructor. **MUST load `dotnet-client-initialization`** before `AddMaxioAdvancedBillingClient` / `new MaxioAdvancedBillingClient`.

⚠ Step 1 (auth) — credentials must be applied on the options object the client is built from; a 401 after a late assign is a wiring failure, not a retry. Username/password roles are API key + literal `"x"`. **MUST load `dotnet-authentication`**.

⚠ Step 1 (site / retries) — `Timeout` / retry options on `RetryOptions` do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; `HttpMethodsToRetry` does not tell you whether a failed write can be re-sent. `{site}` must be set or Production URLs stay on the default subdomain. **MUST load `dotnet-configuration-resilience`**.

⚠ Steps 3–12 (every call) — list/search ops have long positional lists of nullable-without-default parameters; a positional call mis-binds. Named arguments; cancellation token is `ct:`. **MUST load `dotnet-calling-endpoints`**.

⚠ Steps 3–12 (models) — envelopes wrap payload (`CustomerResponse.Customer`, `SubscriptionResponse.Subscription` **nullable**, `ProductResponse.Product`); `ReadInvoice`/`IssueInvoice` do **not**. Unions have no usable `new`. Enums are `StringEnum<T>`. `UpdateSubscription.PaymentCollectionMethod` is `string?`, not `CollectionMethod`. **MUST load `dotnet-models`**.

⚠ Step 4 (PCI) — `CreatePaymentProfile` / nested `PaymentProfileAttributes.FullNumber` accept raw cards; production use without PCI compliance is a merchant-policy failure. Prefer `ChargifyToken`. **MUST load `dotnet-models`**.

⚠ Step 14 (errors) — Case A vs Case B differs **per operation** (this sheet). `TryGetRawError` is not a catch-all on typed errors until the typed `TryGet…` miss. No Result variants exist. Catch `SdkException<CreateCustomerError>` separately from `SdkException<RawError>`. Cancel uses `CancelSubscriptionApiError`. **MUST load `dotnet-error-handling`**.

⚠ Step 14 (`JsonException` from 2xx) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`**.

⚠ Step 14 (`JsonException` from non-2xx) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`**.

⚠ Step 14 (tests) — the `HttpClient` constructor argument is the test seam; do not mock controller classes. **MUST load `dotnet-testing`**.

⚠ Customer 422 shape — `CustomerErrorResponse1.Errors` is typed as record `Errors` (`per_page` / `price_point` lists), which is a suspicious shared model vs typical customer validation bodies. Extract best-effort; fall back to `TryGetRawError` + generic message. **UNVERIFIED** vs live wire. **MUST load `dotnet-error-handling`**.

---

## REQUIRED READING

Load these **before implementation starts**. This sheet does not carry their contents.

- `dotnet-client-initialization` — Step 1: constructing/registering `MaxioAdvancedBillingClient` and `HttpClient` ownership.
- `dotnet-authentication` — Step 1: BasicAuth credentials on options / DI callback.
- `dotnet-configuration-resilience` — Step 1: retries, timeouts, Production `{site}` / BaseUrl, list pagination.
- `dotnet-calling-endpoints` — Steps 3–12: named arguments, `ct:`, throw-only calls.
- `dotnet-models` — Steps 3–12: envelopes, `required` init, `StringEnum<T>`, AnyOf/OneOf factories + `TryGet…`.
- `dotnet-error-handling` — Step 14: Case A/B catch ladder, `TryGet…` vs `RawError`, both `JsonException` directions above.
- `dotnet-testing` — Step 14: `HttpClient` seam for the integration layer.

---

## Assumptions & Blockers

**Assumptions**

- eShop buyer identity is stored on Maxio as `Customer.Reference` (stable application user id). Lookup is `ReadCustomerByReference` before `CreateCustomer`.
- Sellable plans already exist in Maxio as Products (and optional Components) under a Product Family. The store maps eShop SKUs to Maxio `handle`/`id`; it does not create catalog items at checkout.
- Checkout is not PCI-compliant for raw PAN: payment data enters Maxio via Maxio.js `chargify_token` (or a previously stored `payment_profile_id`).
- Default hosting is US (`ServerEnvironment.Us` + `options.Server.Production.Us.Site`). Switch to `Eu` only if the Maxio site is EU-hosted.
- Usage/allocation APIs are implemented behind the same billing service but only invoked if the mapped product family has components.
- Billing Portal and webhook endpoint CRUD are optional; webhook *receipt* in eShop (HTTP listener) is app code, not an SDK operation.

**Blockers**

- None that block planning. Runtime needs a Maxio API key and site subdomain in configuration (not in this sheet).
- Live 422 bodies for `CreateCustomer` / 3DS `action_link` on `CreateSubscription` are labeled **UNVERIFIED**; the boundary must extract best-effort and fall back to `RawError.ReadAsString()`.
