# Maxio Advanced Billing — eShopOnWeb subscription billing

Package `AsadAli.AdvancedBilling.Sdk` · root namespace `MaxioAdvancedBilling` · map stamp `v1.0.2` (`15db14b`). Install via NuGet only (`dotnet add package AsadAli.AdvancedBilling.Sdk`). Do not project-reference SDK source.

## Scope & sequence

1. **Client + DI + auth + site** — construct `MaxioAdvancedBillingClient` (or `AddMaxioAdvancedBillingClient`), set Basic auth and `{site}` subdomain, register in Infrastructure/Web.
2. **Catalog (plans)** — `ProductFamilies` + `Products` + `ProductPricePoints` list/read (admin create if the site has no catalog yet).
3. **Customers** — create/read/update; store Maxio `Customer.Id` and set `reference` to the eShop buyer/user id; lookup via `ReadCustomerByReference`.
4. **Payment profiles** — create (prefer `chargify_token` from Maxio.js; do not send raw PAN unless the app is PCI-compliant); attach via `payment_profile_id` on subscribe or `ChangeSubscriptionDefaultPaymentProfile`.
5. **Subscribe** — `PreviewSubscription` then `CreateSubscription` (existing `customer_id`/`customer_reference` + `product_id`/`product_handle` + optional coupons/components).
6. **Read/update subscription** — `ReadSubscription`, `UpdateSubscription` (card, `next_billing_at`, delayed product change).
7. **Cancel / pause / resume** — `SubscriptionStatus.CancelSubscription`, `InitiateDelayedCancellation` / `CancelDelayedCancellation`, `PauseSubscription`, `ResumeSubscription`; `ReactivateSubscription` for canceled/trial-ended only (not the on-hold resume path).
8. **Plan change** — immediate prorated: `SubscriptionProducts.PreviewSubscriptionProductMigration` then `MigrateSubscriptionProduct`. Next-period (no proration): `UpdateSubscription` with `product_id`/`product_handle` + `product_change_delayed: true`.
9. **Components + usage** — list/read components; `CreateUsage` for metered/prepaid; `AllocateComponent` for quantity/on-off/prepaid quantity.
10. **Coupons** — `ValidateCoupon` / `FindCoupon`; apply at create (`coupon_code`/`coupon_codes`) or `ApplyCouponsToSubscription`; `RemoveCouponFromSubscription`.
11. **Invoices** — `ListInvoices` (filter `subscriptionId`) + `ReadInvoice`; optional ad-hoc `CreateInvoice` / `IssueInvoice` / record payment.
12. **Error boundary + tests** — throw-only SDK; Case A/B per row below; tests fake `HttpClient`.

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

C# does **not** import child namespaces transitively. Add a `using` per kind: `MaxioAdvancedBilling`, `MaxioAdvancedBilling.Api`, `MaxioAdvancedBilling.Models`, `MaxioAdvancedBilling.Models.Enums`, `MaxioAdvancedBilling.Models.AnyOf`, `MaxioAdvancedBilling.Models.OneOf`, `MaxioAdvancedBilling.Errors`, `MaxioAdvancedBilling.Core.Exceptions`, `MaxioAdvancedBilling.Core.ErrorResponse`, `MaxioAdvancedBilling.Core.Authentication.Basic`, `MaxioAdvancedBilling.Core.Configuration`, `MaxioAdvancedBilling.Servers`.

Every operation is **throw-only** (no `…Result` / `ApiResult` variants). On error: `MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>` with `.Error`. Case A `TError` is `MaxioAdvancedBilling.Errors.{Op}Error` (`TryGet…` + inherited `TryGetRawError(out RawError)`). Case B `TError` is `MaxioAdvancedBilling.Core.ErrorResponse.RawError` (`StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`). Typed errors also inherit `ApiError.TryGetRawError`. Source: `sdk-map.md`, `Core/Exceptions/SdkException.cs`, `Core/ErrorResponse/`.

---

### Client construction / auth / servers

| Fact | Value | Source |
|---|---|---|
| Client | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` | `MaxioAdvancedBillingClient.cs` |
| Only ctor | `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` |
| Options | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` | `MaxioAdvancedBillingClientOptions.cs` |
| Options members | `Environment`: `MaxioAdvancedBilling.Servers.ServerEnvironment`; `Retry`: `MaxioAdvancedBilling.Core.Configuration.RetryOptions`; `Server`: `MaxioAdvancedBilling.ServerOptions`; `BasicAuth`: `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md` |
| Auth | HTTP Basic. `BasicAuthCredentials { Username = "<api_key>", Password = "x" }` — username = API key, password = literal `"x"` | `sdk-map.md`, `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Environments | `ServerEnvironment.Us` (default, wire `US`) → `https://{site}.chargify.com`; `ServerEnvironment.Eu` (wire `EU`) → `https://{site}.ebilling.maxio.com` | `Servers/ServerEnvironment.cs` |
| Site / base URL | `{site}` defaults to `subdomain`. Set `options.Server.Production.Us.Site = "<your-subdomain>"` (and `.Eu.*` if EU). Mock: `options.Server.Production.Us.BaseUrl = "http://localhost:…"`. Ebb ingest: `options.Server.Ebb.Us.Site` / `.BaseUrl` (`https://events.chargify.com/{site}`) | `sdk-map.md`, `ServerOptions.cs`, `Servers/ProductionOptions.cs`, `Servers/EbbOptions.cs` |
| DI | `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = …; })` | `ServiceCollectionExtensions.cs` |
| RetryOptions | Namespace `MaxioAdvancedBilling.Core.Configuration`. All members `required` — full instance or `RetryOptions.Default()`. Members: `StatusCodesToRetry: IReadOnlyList<HttpStatusCode>`, `HttpMethodsToRetry: IReadOnlyList<HttpMethod>`, `MaxRetries: int`, `Delay: TimeSpan`, `Timeout: TimeSpan?`, `BackOffFactor: int`, `UseExponentialBackoff: bool`, `MaxJitter: TimeSpan`, `OnRetry: Action<RetryAttempt>?` | `Core/Configuration/RetryOptions.cs` |
| Controllers used | `client.Customers`, `client.Subscriptions`, `client.SubscriptionStatus`, `client.SubscriptionProducts`, `client.Products`, `client.ProductFamilies`, `client.ProductPricePoints`, `client.Components`, `client.SubscriptionComponents`, `client.Coupons`, `client.PaymentProfiles`, `client.Invoices` | `MaxioAdvancedBillingClient.cs` |

---

### Shared error payload records (`MaxioAdvancedBilling.Models`)

| Record | Fields | Map |
|---|---|---|
| `ErrorListResponse1` | `Errors (errors): IReadOnlyList<string> !req` | `records-2-Cr-Ne.md` |
| `ErrorArrayMapResponse1` | `Errors (errors): IReadOnlyDictionary<string, object>?` | `records-2-Cr-Ne.md` |
| `ErrorStringMapResponse1` | `Errors (errors): IReadOnlyDictionary<string, string>?` | `records-2-Cr-Ne.md` |
| `SingleErrorResponse1` | `Error (error): string !req` | `records-3-Of-Su.md` |
| `SingleStringErrorResponse1` | `Errors (errors): string?` | `records-3-Of-Su.md` |
| `CustomerErrorResponse1` | `Errors (errors): Errors?` | `records-2-Cr-Ne.md` |
| `Errors` | `PerPage (per_page): IReadOnlyList<string>?`, `PricePoint (price_point): IReadOnlyList<string>?` | `records-2-Cr-Ne.md` |
| `CustomerError` | `Customer (customer): string?` | `records-2-Cr-Ne.md` |

`CustomerErrorResponse1.Errors` is typed as `Errors` (only `per_page` / `price_point`). Sibling union `Errors1` (`CustomerError` \| `IReadOnlyList<string>`) exists (`unions.md`) and would match a typical customer 422 body. **UNVERIFIED** whether the live 422 body matches `Errors` — extract best-effort from `CustomerErrorResponse1` / `TryGetRawError` (`ReadAsString` / `ReadAsJson<T>`), fall back to a generic message. Do not parse `ex.ToString()`.

---

### 1. Customers — `client.Customers` · `Api/Customers.cs` · `operations/Customers.md`

| Op | Signature | Returns | Error | Pagination |
|---|---|---|---|---|
| `CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, **must pass explicitly** | `CustomerResponse` | **A** `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError` | none |
| `ReadCustomer` | `ReadCustomer(int id, CancellationToken ct = default)` | `CustomerResponse` | **B** `SdkException<RawError>` | none |
| `ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` query `reference` | `CustomerResponse` | **B** `SdkException<RawError>` | none |
| `UpdateCustomer` | `UpdateCustomer(int id, UpdateCustomerRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | `CustomerResponse` | **A** `SdkException<UpdateCustomerError>`: `TryGetNoContent(out RawError)` [404] · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError` | none |
| `ListCustomers` | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — 7 params `direction`…`q` **must pass explicitly** (`null` to skip) | `IReadOnlyList<CustomerResponse>` | **B** `SdkException<RawError>` | `page`+`perPage` (default 1/50) |
| `ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | `IReadOnlyList<SubscriptionResponse>` | **B** `SdkException<RawError>` | none |
| `DeleteCustomer` | `DeleteCustomer(int id, CancellationToken ct = default)` | `void` | **B** `SdkException<RawError>` | none |

ListCustomers query wire ← C#: `direction` ← `direction`, `page` ← `page`, `per_page` ← `perPage`, `date_field` ← `dateField`, `start_date` ← `startDate`, `end_date` ← `endDate`, `start_datetime` ← `startDatetime`, `end_datetime` ← `endDatetime`, `q` ← `q`.

**Envelopes / request models** (`records-1-Ac-Cr.md`, `records-2-Cr-Ne.md`, `records-4-Su-We.md`):

- `CreateCustomerRequest`: `Customer (customer): CreateCustomer !req`
- `CreateCustomer` **required**: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`. Optional: `CcEmails (cc_emails)`, `Organization (organization)`, `Reference (reference)`, `Address (address)`, `Address2 (address_2)`, `City (city)`, `State (state)`, `Zip (zip)`, `Country (country)`, `Phone (phone)`, `Locale (locale)`, `VatNumber (vat_number)`, `TaxExempt (tax_exempt): bool?`, `TaxExemptReason (tax_exempt_reason)`, `ParentId (parent_id): int?`, `SalesforceId (salesforce_id)` (all `string?` unless noted).
- `UpdateCustomerRequest`: `Customer (customer): UpdateCustomer !req`
- `UpdateCustomer`: same address/name/email fields as create, all optional (`string?` / `bool?` / `int?`); plus `Verified (verified): bool?`. No `!req` members.
- `CustomerResponse`: `Customer (customer): Customer !req` — read **one level down**.
- `Customer` fields to persist/display: `Id (id): int?`, `FirstName (first_name)`, `LastName (last_name)`, `Email (email)`, `Reference (reference)`, `Organization (organization)`, `Address`/`Address2`/`City`/`State`/`StateName`/`Zip`/`Country`/`CountryName`/`Phone`, `CreatedAt`/`UpdatedAt`, `TaxExempt (tax_exempt): bool?`, `VatNumber (vat_number)`, `Locale (locale)`, `Verified (verified): bool?`, `ParentId (parent_id): int?`, `SalesforceId (salesforce_id)`, `DefaultSubscriptionGroupUid (default_subscription_group_uid)`, `DefaultAutoRenewalProfileId (default_auto_renewal_profile_id): int?`, `Maxioid (maxioid)`, portal timestamps.

Country = ISO 3166-1 alpha-2; US state = ISO 3166-2 (2 chars). `reference` must be unique per site if set — use eShop buyer id.

---

### 2. Subscriptions — `client.Subscriptions` · `Api/Subscriptions.cs` · `operations/Subscriptions.md`

| Op | Signature | Returns | Error | Pagination |
|---|---|---|---|---|
| `CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | `SubscriptionResponse` | **A** `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` | none |
| `ReadSubscription` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` **must pass explicitly** | `SubscriptionResponse` | **B** `SdkException<RawError>` | none |
| `UpdateSubscription` | `UpdateSubscription(int subscriptionId, UpdateSubscriptionRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | `SubscriptionResponse` | **A** `SdkException<UpdateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` | none |
| `FindSubscription` | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` **must pass explicitly**; query `reference` | `SubscriptionResponse` | **A** `SdkException<FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError` | none |
| `ListSubscriptions` | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 14 params `state`…`include` **must pass explicitly** | `IReadOnlyList<SubscriptionResponse>` | **B** `SdkException<RawError>` | `page`+`perPage` (default 1/20) |
| `PreviewSubscription` | `PreviewSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | `SubscriptionPreviewResponse` | **B** `SdkException<RawError>` | none |
| `ApplyCouponsToSubscription` | `ApplyCouponsToSubscription(int subscriptionId, string? code, AddCouponsRequest? body, CancellationToken ct = default)` — `code` and `body` **must pass explicitly**. Prefer **body** (`Codes`); query `code` **replaces** all existing coupons (deprecated). | `SubscriptionResponse` | **A** `SdkException<ApplyCouponsToSubscriptionError>`: `TryGetSubscriptionAddCouponError1(out SubscriptionAddCouponError1)` [422] · `TryGetRawError` | none |
| `RemoveCouponFromSubscription` | `RemoveCouponFromSubscription(int subscriptionId, string? couponCode, CancellationToken ct = default)` — `couponCode` **must pass explicitly**; query `coupon_code` | `string` | **A** `SdkException<RemoveCouponFromSubscriptionError>`: `TryGetSubscriptionRemoveCouponErrors1(out SubscriptionRemoveCouponErrors1)` [422] · `TryGetRawError` | none |
| `ActivateSubscription` | `ActivateSubscription(int subscriptionId, ActivateSubscriptionRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | `SubscriptionResponse` | **A** `SdkException<ActivateSubscriptionError>`: `TryGetErrorArrayMapResponse1(out ErrorArrayMapResponse1)` [400] · `TryGetRawError` | none |

**Create envelope** (`records-2-Cr-Ne.md`):

- `CreateSubscriptionRequest`: `Subscription (subscription): CreateSubscription !req`
- Identify product: `ProductId (product_id): int?` **or** `ProductHandle (product_handle): string?`
- Price point: `ProductPricePointId (product_price_point_id): int?` **or** `ProductPricePointHandle (product_price_point_handle): string?` (else product default)
- Existing customer: `CustomerId (customer_id): int?` **or** `CustomerReference (customer_reference): string?`
- New customer inline: `CustomerAttributes (customer_attributes): CustomerAttributes?`
- Payment: `PaymentProfileId (payment_profile_id): int?` **or** `PaymentProfileAttributes (payment_profile_attributes): PaymentProfileAttributes?` **or** `CreditCardAttributes (credit_card_attributes): PaymentProfileAttributes?` **or** `BankAccountAttributes (bank_account_attributes): BankAccountAttributes?`
- Coupons: `CouponCode (coupon_code): string?` and/or `CouponCodes (coupon_codes): IReadOnlyList<string>?`
- Components at signup: `Components (components): IReadOnlyList<CreateSubscriptionComponent>?`
- Other used: `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `Reference (reference): string?`, `NextBillingAt (next_billing_at): DateTimeOffset?`, `InitialBillingAt (initial_billing_at): DateTimeOffset?`, `DeferSignup (defer_signup): bool? = false`, `CustomPrice (custom_price): SubscriptionCustomPrice?`, `Currency (currency): string?`, `ReceivesInvoiceEmails (receives_invoice_emails): string?`, `NetTerms (net_terms): string?`, `Metafields (metafields): IReadOnlyDictionary<string, string>?`, `CalendarBilling (calendar_billing): CalendarBilling?`, `AgreementAcceptance (agreement_acceptance): AgreementAcceptance?`, `SkipBillingManifestTaxes (skip_billing_manifest_taxes): bool?`, `OfferId (offer_id): OfferId?` (union), `PrepaidConfiguration (prepaid_configuration): UpsertPrepaidConfiguration?`

`CreateSubscriptionComponent`: `ComponentId (component_id): ComponentId1?` (union int\|string), `Enabled (enabled): bool?`, `UnitBalance (unit_balance): int?`, `AllocatedQuantity (allocated_quantity): AllocatedQuantity3?` (union), `Quantity (quantity): int?`, `PricePointId (price_point_id): PricePointId2?` (union), `CustomPrice (custom_price): ComponentCustomPrice?`.

`CustomerAttributes`: `FirstName`, `LastName`, `Email`, `CcEmails`, `Organization`, `Reference`, `Address`, `Address2`, `City`, `State`, `Zip`, `Country`, `Phone` (all `string?`); `Verified (verified): bool?`, `TaxExempt (tax_exempt): bool?`, `VatNumber (vat_number)`, `Metafields (metafields): IReadOnlyDictionary<string, string>?`, `ParentId (parent_id): int?`, `SalesforceId (salesforce_id)`, `DefaultAutoRenewalProfileId (default_auto_renewal_profile_id): int?`.

`PaymentProfileAttributes` (also used as create-subscription `credit_card_attributes`): `ChargifyToken (chargify_token)`, `FullNumber (full_number)`, `ExpirationMonth (expiration_month): ExpirationMonth2?` (union int\|string), `ExpirationYear (expiration_year): ExpirationYear2?` (union), `FirstName`, `LastName`, `CardType (card_type): CardType?`, `PaymentType (payment_type): PaymentType?`, billing address fields, `Cvv (cvv)`, `VaultToken`, `CustomerVaultToken`, `CustomerId (customer_id): int?`, `PaypalEmail`, `PaymentMethodNonce`, `GatewayHandle`, `LastFour`, `CurrentVault (current_vault): AllVaults?`. Prefer `chargify_token`.

**Update envelope** (`records-4-Su-We.md`):

- `UpdateSubscriptionRequest`: `Subscription (subscription): UpdateSubscription !req`
- `UpdateSubscription`: `ProductHandle`, `ProductId (product_id): int?`, `ProductChangeDelayed (product_change_delayed): bool?`, `NextProductId (next_product_id): string?` (empty string cancels a delayed change), `NextProductPricePointId (next_product_price_point_id): string?`, `ProductPricePointId (product_price_point_id): int?`, `ProductPricePointHandle`, `SnapDay (snap_day): SnapDay1?` (union), `NextBillingAt`, `ExpiresAt`, `InitialBillingAt`, `DeferSignup (defer_signup): bool? = false`, `PaymentCollectionMethod (payment_collection_method): string?` (wire string, not `CollectionMethod` enum), `ReceivesInvoiceEmails (receives_invoice_emails): bool?`, `NetTerms (net_terms): NetTerms1?` (union string\|int), `Reference`, `CreditCardAttributes (credit_card_attributes): CreditCardAttributes?` (`FullNumber`, `ExpirationMonth`, `ExpirationYear` — all `string?`), `CustomPrice`, `Components (components): IReadOnlyList<UpdateSubscriptionComponent>?`, `DunningCommunicationDelayEnabled`, `DunningCommunicationDelayTimeZone`, `StoredCredentialTransactionId (stored_credential_transaction_id): int?`.

Delayed product change: set `product_handle`/`product_id` **and** `product_change_delayed = true`. Immediate product change on this endpoint charges at next period (no proration). Prorated migrate = Step 8.

**Response envelope** (`records-4-Su-We.md`, `records-3-Of-Su.md`):

- `SubscriptionResponse`: `Subscription (subscription): Subscription?` — read **one level down**.
- `Subscription` fields the app reads: `Id (id): int?`, `State (state): SubscriptionState?`, `BalanceInCents (balance_in_cents): long?`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `CurrentPeriodStartedAt (current_period_started_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `ActivatedAt`, `CanceledAt`, `ExpiresAt`, `CancelAtEndOfPeriod (cancel_at_end_of_period): bool?`, `DelayedCancelAt`, `ScheduledCancellationAt`, `AutomaticallyResumeAt (automatically_resume_at): DateTimeOffset?`, `OnHoldAt`, `CouponCode`, `CouponCodes`, `Coupons (coupons): IReadOnlyList<SubscriptionIncludedCoupon>?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `Customer (customer): Customer?`, `Product (product): Product?`, `CreditCard (credit_card): CreditCardPaymentProfile?`, `BankAccount (bank_account): BankAccountPaymentProfile?`, `ProductPricePointId`, `NextProductId`, `NextProductHandle`, `NextProductPricePointId`, `Reference`, `Currency`, `ReceivesInvoiceEmails`, `SelfServicePageToken (self_service_page_token)` (only if `include` contains `SubscriptionInclude.SelfServicePageToken`), `PrepaidConfiguration`.
- `SubscriptionPreviewResponse`: `SubscriptionPreview (subscription_preview): SubscriptionPreview !req` → `CurrentBillingManifest` / `NextBillingManifest` (`BillingManifest`: `TotalInCents`, `SubtotalInCents`, `TotalTaxInCents`, `TotalDiscountInCents`, `StartDate`, `EndDate`, `LineItems`).
- `AddCouponsRequest`: `Codes (codes): IReadOnlyList<string>?`
- `ActivateSubscriptionRequest`: `RevertOnFailure (revert_on_failure): bool?`
- `SubscriptionAddCouponError1`: `Codes`, `CouponCode`, `CouponCodes`, `Subscription` — each `IReadOnlyList<string>?`
- `SubscriptionRemoveCouponErrors1`: `Subscription (subscription): IReadOnlyList<string> !req`

422 on create/update may include 3DS `action_link` in the error list — surface to the buyer; do not treat as a generic 5xx.

---

### 3. Subscription status (cancel / pause / resume) — `client.SubscriptionStatus` · `Api/SubscriptionStatus.cs` · `operations/SubscriptionStatus.md`

| Op | Signature | Returns | Error |
|---|---|---|---|
| `CancelSubscription` | `CancelSubscription(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** (pass `null` for immediate cancel with no message) | `SubscriptionResponse` | **A** `SdkException<CancelSubscriptionApiError>`: `TryGetNoContent(out RawError)` [404] · `TryGetCancelSubscriptionErrorResponse(out CancelSubscriptionErrorResponse)` [422] · `TryGetRawError` |
| `InitiateDelayedCancellation` | `InitiateDelayedCancellation(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | `DelayedCancellationResponse` | **A** `SdkException<InitiateDelayedCancellationError>`: `TryGetNoContent` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` |
| `CancelDelayedCancellation` | `CancelDelayedCancellation(int subscriptionId, CancellationToken ct = default)` | `DelayedCancellationResponse` | **A** `SdkException<CancelDelayedCancellationError>`: `TryGetNoContent` [404] · `TryGetRawError` |
| `PauseSubscription` | `PauseSubscription(int subscriptionId, PauseRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly**. Cannot pause if `next_billing_at` is within 24 hours. | `SubscriptionResponse` | **A** `SdkException<PauseSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` |
| `ResumeSubscription` | `ResumeSubscription(int subscriptionId, ResumptionCharge? calendarBillingResumptionCharge, CancellationToken ct = default)` — `calendarBillingResumptionCharge` **must pass explicitly** (`null` if not calendar-billing). Query wire `calendar_billing['resumption_charge']`. On-hold → active. | `SubscriptionResponse` | **A** `SdkException<ResumeSubscriptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `UpdateAutomaticSubscriptionResumption` | `UpdateAutomaticSubscriptionResumption(int subscriptionId, PauseRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | `SubscriptionResponse` | **A** `SdkException<UpdateAutomaticSubscriptionResumptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `ReactivateSubscription` | `ReactivateSubscription(int subscriptionId, ReactivateSubscriptionRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly`. For **canceled / trial_ended / unpaid**, not on-hold. | `SubscriptionResponse` | **A** `SdkException<ReactivateSubscriptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `RetrySubscription` | `RetrySubscription(int subscriptionId, CancellationToken ct = default)` | `SubscriptionResponse` | **A** `SdkException<RetrySubscriptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` |

**Models** (`records-1-Ac-Cr.md`, `records-2-Cr-Ne.md`, `records-3-Of-Su.md`):

- `CancellationRequest`: `Subscription (subscription): CancellationOptions !req`
- `CancellationOptions`: `CancellationMessage (cancellation_message): string?`, `ReasonCode (reason_code): string?`, `CancelAtEndOfPeriod (cancel_at_end_of_period): bool?`, `ScheduledCancellationAt (scheduled_cancellation_at): DateTimeOffset?`, `RefundPrepaymentAccountBalance (refund_prepayment_account_balance): bool?`
- `PauseRequest`: `Hold (hold): AutoResume?` — `AutoResume.AutomaticallyResumeAt (automatically_resume_at): DateTimeOffset?` (null clears resume date on update)
- `DelayedCancellationResponse`: `Message (message): string?`
- `ReactivateSubscriptionRequest`: `CalendarBilling (calendar_billing): ReactivationBilling?`, `IncludeTrial (include_trial): bool?`, `PreserveBalance (preserve_balance): bool?`, `CouponCode (coupon_code): string?`, `UseCreditsAndPrepayments (use_credits_and_prepayments): bool?`, `Resume (resume): Resume?` (union `bool` \| `ResumeOptions`)
- `ResumeOptions`: `RequireResume (require_resume): bool?`, `ForgiveBalance (forgive_balance): bool?`
- `ReactivationBilling`: `ReactivationCharge (reactivation_charge): ReactivationCharge? = ReactivationCharge.Prorated`
- `CancelSubscriptionErrorResponse` (**union** `MaxioAdvancedBilling.Models.AnyOf`): variants `ErrorListResponse1`, `SingleErrorResponse1`. Factories: `CancelSubscriptionErrorResponse.ErrorListResponse1(…)`, `.SingleErrorResponse1(…)`. Read: `TryGetErrorListResponse1(out …)`, `TryGetSingleErrorResponse1(out …)`.

Pause = hold (`POST …/hold.json`). Resume on-hold = `ResumeSubscription`. Reactivate canceled = `ReactivateSubscription` (optional `Resume` union to keep the current period).

---

### 4. Plan / product change — `client.SubscriptionProducts` · `Api/SubscriptionProducts.cs` · `operations/SubscriptionProducts.md`

| Op | Signature | Returns | Error |
|---|---|---|---|
| `PreviewSubscriptionProductMigration` | `PreviewSubscriptionProductMigration(int subscriptionId, SubscriptionMigrationPreviewRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | `SubscriptionMigrationPreviewResponse` | **A** `SdkException<PreviewSubscriptionProductMigrationError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `MigrateSubscriptionProduct` | `MigrateSubscriptionProduct(int subscriptionId, SubscriptionProductMigrationRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly**. Subscription should be `active` or `trialing`. | `SubscriptionResponse` | **A** `SdkException<MigrateSubscriptionProductError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` |

**Models** (`records-4-Su-We.md`):

- `SubscriptionProductMigrationRequest`: `Migration (migration): SubscriptionProductMigration !req`
- `SubscriptionProductMigration`: `ProductId (product_id): int?` **or** `ProductHandle (product_handle): string?`; `ProductPricePointId (product_price_point_id): int?` **or** `ProductPricePointHandle (product_price_point_handle): string?`; `IncludeTrial (include_trial): bool? = false`; `IncludeInitialCharge (include_initial_charge): bool? = false`; `IncludeCoupons (include_coupons): bool? = true`; `PreservePeriod (preserve_period): bool? = false`; `Proration (proration): Proration?` (`PreservePeriod (preserve_period): bool?`)
- `SubscriptionMigrationPreviewRequest`: `Migration (migration): SubscriptionMigrationPreviewOptions !req` — same product identifiers + `ProrationDate (proration_date): DateTimeOffset?` (future date still inside current period)
- `SubscriptionMigrationPreviewResponse`: `Migration (migration): SubscriptionMigrationPreview !req`
- `SubscriptionMigrationPreview`: `ProratedAdjustmentInCents (prorated_adjustment_in_cents): long?`, `ChargeInCents (charge_in_cents): long?`, `PaymentDueInCents (payment_due_in_cents): long?`, `CreditAppliedInCents (credit_applied_in_cents): long?`

Migrating to the **current** product is a common 422.

---

### 5. Products / families / price points (plans)

#### ProductFamilies — `client.ProductFamilies` · `operations/ProductFamilies.md`

| Op | Signature | Returns | Error | Pagination |
|---|---|---|---|---|
| `ListProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — 5 filters **must pass explicitly** | `IReadOnlyList<ProductFamilyResponse>` | **B** `SdkException<RawError>` | none |
| `ReadProductFamily` | `ReadProductFamily(int id, CancellationToken ct = default)` — C# takes `int` only | `ProductFamilyResponse` | **B** `SdkException<RawError>` | none |
| `ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — `productFamilyId` is **`string`**; 8 filters **must pass explicitly** | `IReadOnlyList<ProductResponse>` | **A** `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError` | `page`+`perPage` (1/20) |
| `CreateProductFamily` | `CreateProductFamily(CreateProductFamilyRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | `ProductFamilyResponse` | **A** `SdkException<CreateProductFamilyError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |

- `CreateProductFamilyRequest`: `ProductFamily (product_family): CreateProductFamily !req`
- `CreateProductFamily`: `Name (name): string !req`, `Handle (handle): string?`, `Description (description): string?`
- `ProductFamilyResponse`: `ProductFamily (product_family): ProductFamily?`
- `ProductFamily`: `Id (id): int?`, `Name (name)`, `Handle (handle)`, `AccountingCode (accounting_code)`, `Description (description)`, `CreatedAt`, `UpdatedAt`, `ArchivedAt`

#### Products — `client.Products` · `operations/Products.md`

| Op | Signature | Returns | Error | Pagination |
|---|---|---|---|---|
| `ListProducts` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 filters **must pass explicitly** | `IReadOnlyList<ProductResponse>` | **B** `SdkException<RawError>` | `page`+`perPage` (1/20) |
| `ReadProduct` | `ReadProduct(int productId, CancellationToken ct = default)` | `ProductResponse` | **B** `SdkException<RawError>` | none |
| `ReadProductByHandle` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | `ProductResponse` | **B** `SdkException<RawError>` | none |
| `CreateProduct` | `CreateProduct(string productFamilyId, CreateOrUpdateProductRequest? body, CancellationToken ct = default)` — `productFamilyId` is **`string`**; `body` **must pass explicitly** | `ProductResponse` | **A** `SdkException<CreateProductError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `UpdateProduct` | `UpdateProduct(int productId, CreateOrUpdateProductRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly**. Update creates a **new default price point**. | `ProductResponse` | **A** `SdkException<UpdateProductError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |

- `CreateOrUpdateProductRequest`: `Product (product): CreateOrUpdateProduct !req`
- `CreateOrUpdateProduct` **required**: `Name (name): string !req`, `Description (description): string !req`, `PriceInCents (price_in_cents): long !req`, `Interval (interval): int !req`, `IntervalUnit (interval_unit): IntervalUnit !req`. Optional: `Handle`, `AccountingCode`, `RequireCreditCard (require_credit_card): bool?`, `TrialPriceInCents`, `TrialInterval`, `TrialIntervalUnit`, `TrialType`, `ExpirationInterval`, `ExpirationIntervalUnit`, `AutoCreateSignupPage`, `TaxCode`.
- `ProductResponse`: `Product (product): Product !req`
- `Product` (read): `Id`, `Name`, `Handle`, `Description`, `PriceInCents (price_in_cents): long?`, `Interval`, `IntervalUnit`, `RequireCreditCard (require_credit_card): bool?`, `RequestCreditCard (request_credit_card): bool?`, `TrialPriceInCents`, `TrialInterval`, `TrialIntervalUnit`, `InitialChargeInCents`, `InitialChargeAfterTrial`, `ProductFamily (product_family): ProductFamily?`, `ProductPricePointId`, `ProductPricePointHandle`, `ProductPricePointName`, `DefaultProductPricePointId`, `Taxable`, `TaxCode`, `ArchivedAt`, `UseSiteExchangeRate`.
- `ListProductsFilter`: `Ids (ids): IReadOnlyList<int>?`, `PrepaidProductPricePoint`, `UseSiteExchangeRate (use_site_exchange_rate): bool?`

#### ProductPricePoints — `client.ProductPricePoints` · `operations/ProductPricePoints.md`

| Op | Signature | Returns | Error | Pagination |
|---|---|---|---|---|
| `ListProductPricePoints` | `ListProductPricePoints(ProductIdModel productId, bool? currencyPrices, IReadOnlyList<PricePointType>? filterType, bool? archived, int? page = 1, int? perPage = 10, CancellationToken ct = default)` — 3 filters **must pass explicitly** | `ListProductPricePointsResponse` | **B** `SdkException<RawError>` | `page`+`perPage` (1/10) |
| `ReadProductPricePoint` | `ReadProductPricePoint(ProductIdModel productId, PricePointIdModel pricePointId, bool? currencyPrices, CancellationToken ct = default)` — `currencyPrices` **must pass explicitly** | `ProductPricePointResponse` | **B** `SdkException<RawError>` | none |
| `CreateProductPricePoint` | `CreateProductPricePoint(ProductIdModel productId, CreateProductPricePointRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | `ProductPricePointResponse` | **A** `SdkException<CreateProductPricePointError>`: `TryGetProductPricePointErrorResponse1(out ProductPricePointErrorResponse1)` [422] · `TryGetRawError` | none |

`ProductIdModel` / `PricePointIdModel`: AnyOf `int` \| `string` — `ProductIdModel.Int(id)` / `.String("handle:…")`; implicit from `int` and `string`. Query `filter[type]` ← `filterType`, `currency_prices` ← `currencyPrices`.

- `CreateProductPricePointRequest`: `PricePoint (price_point): CreateProductPricePoint !req`
- `CreateProductPricePoint` **required**: `Name (name): string !req`, `PriceInCents (price_in_cents): long !req`, `Interval (interval): int !req`, `IntervalUnit (interval_unit): IntervalUnit !req`. Optional: `Handle`, trial/initial/expiration fields, `UseSiteExchangeRate (use_site_exchange_rate): bool? = true`.
- `ProductPricePointResponse`: `PricePoint (price_point): ProductPricePoint !req`
- `ListProductPricePointsResponse`: `PricePoints (price_points): IReadOnlyList<ProductPricePoint> !req`
- `ProductPricePoint`: `Id`, `Name`, `Handle`, `PriceInCents`, `Interval`, `IntervalUnit`, `Trial*`, `InitialChargeInCents`, `Type (type): PricePointType?`, `ProductId`, `TaxIncluded`, `ArchivedAt`
- `ProductPricePointErrorResponse1`: `Errors (errors): ProductPricePointErrors !req` — `PricePoint (price_point): string?` plus `Interval`/`IntervalUnit`/`Name`/`Price`/`PriceInCents` as `IReadOnlyList<string>?`

---

### 6. Components (catalog) — `client.Components` · `operations/Components.md`

| Op | Signature | Returns | Error | Pagination |
|---|---|---|---|---|
| `ListComponents` | `ListComponents(BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, bool? includeArchived, ListComponentsFilter? filter, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 7 filters **must pass explicitly** | `IReadOnlyList<ComponentResponse>` | **B** | `page`+`perPage` (1/20) |
| `ListComponentsForProductFamily` | `ListComponentsForProductFamily(int productFamilyId, bool? includeArchived, ListComponentsFilter? filter, BasicDateField? dateField, string? endDate, string? endDatetime, string? startDate, string? startDatetime, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 7 filters **must pass explicitly** | `IReadOnlyList<ComponentResponse>` | **B** | `page`+`perPage` |
| `FindComponent` | `FindComponent(string handle, CancellationToken ct = default)` query `handle` | `ComponentResponse` | **B** | none |
| `ReadComponent` | `ReadComponent(int productFamilyId, string componentId, CancellationToken ct = default)` — `componentId` may be `"handle:<handle>"` | `ComponentResponse` | **B** | none |
| `CreateMeteredComponent` | `CreateMeteredComponent(string productFamilyId, CreateMeteredComponent? body, CancellationToken ct = default)` — `body` **must pass explicitly** | `ComponentResponse` | **A** `SdkException<CreateMeteredComponentError>`: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `CreateQuantityBasedComponent` | `CreateQuantityBasedComponent(string productFamilyId, CreateQuantityBasedComponent? body, …)` — `body` **must pass explicitly** | `ComponentResponse` | **A** `CreateQuantityBasedComponentError` (404 NoContent, 422 ErrorList) | none |
| `CreateOnOffComponent` | `CreateOnOffComponent(string productFamilyId, CreateOnOffComponent? body, …)` | `ComponentResponse` | **A** `CreateOnOffComponentError` (404, 422) | none |
| `CreatePrepaidUsageComponent` | `CreatePrepaidUsageComponent(string productFamilyId, CreatePrepaidComponent? body, …)` | `ComponentResponse` | **A** `CreatePrepaidUsageComponentError` (404, 422) | none |

- `ComponentResponse`: `Component (component): Component !req`
- `Component` (read): `Id`, `Name`, `Handle`, `Kind (kind): ComponentKind?`, `UnitName`, `UnitPrice (unit_price): string?`, `PricingScheme`, `ProductFamilyId`, `ProductFamilyHandle`, `DefaultPricePointId`, `DefaultPricePointName`, `Taxable`, `Recurring`, `Archived`, `Interval`, `IntervalUnit`
- `ListComponentsFilter`: `Ids (ids): IReadOnlyList<int>?`, `UseSiteExchangeRate (use_site_exchange_rate): bool?`
- `CreateMeteredComponent`: `MeteredComponent (metered_component): MeteredComponent !req`
- `MeteredComponent` **required**: `Name (name): string !req`, `UnitName (unit_name): string !req`, `PricingScheme (pricing_scheme): PricingScheme !req`. Optional: `Handle`, `Description`, `Prices (prices): IReadOnlyList<Price>?`, `UnitPrice (unit_price): UnitPrice1?` (union string\|double), `Taxable`, `Interval`, `IntervalUnit`
- `Price` **required**: `StartingQuantity (starting_quantity): StartingQuantity !req` (union int\|string), `UnitPrice (unit_price): UnitPrice !req` (union double\|string); optional `EndingQuantity (ending_quantity): EndingQuantity?`
- `CreateQuantityBasedComponent`: `QuantityBasedComponent (quantity_based_component): QuantityBasedComponent !req` — required `Name`, `UnitName`, `PricingScheme`; optional `Recurring (recurring): bool?`
- `CreateOnOffComponent`: `OnOffComponent (on_off_component): OnOffComponent !req` — required `Name`, `UnitPrice (unit_price): UnitPrice3 !req` (union)
- `CreatePrepaidComponent`: `PrepaidUsageComponent (prepaid_usage_component): PrepaidUsageComponent !req` — required `Name`, `UnitName`, `PricingScheme`, `OveragePricing (overage_pricing): OveragePricing !req` (`PricingScheme !req`, `Prices?`)

---

### 7. Usage / allocations — `client.SubscriptionComponents` · `operations/SubscriptionComponents.md`

| Op | Signature | Returns | Error | Pagination |
|---|---|---|---|---|
| `CreateUsage` | `CreateUsage(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, CreateUsageRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly**. One component per call. Negative `quantity` deducts (floor 0). | `UsageResponse` | **A** `SdkException<CreateUsageError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `ListUsages` | `ListUsages(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, long? sinceId, long? maxId, DateTimeOffset? sinceDate, DateTimeOffset? untilDate, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 4 filters **must pass explicitly** | `IReadOnlyList<UsageResponse>` | **B** | `page`+`perPage` |
| `ListSubscriptionComponents` | `ListSubscriptionComponents(int subscriptionId, SubscriptionListDateField? dateField, SortingDirection? direction, ListSubscriptionComponentsFilter? filter, string? endDate, string? endDatetime, IncludeNotNull? pricePointIds, IReadOnlyList<int>? productFamilyIds, ListSubscriptionComponentsSort? sort, string? startDate, string? startDatetime, IReadOnlyList<ListSubscriptionComponentsInclude>? include, bool? inUse, CancellationToken ct = default)` — 12 filters **must pass explicitly** | `IReadOnlyList<SubscriptionComponentResponse>` | **B** | none |
| `ReadSubscriptionComponent` | `ReadSubscriptionComponent(int subscriptionId, int componentId, CancellationToken ct = default)` | `SubscriptionComponentResponse` | **A** `SdkException<ReadSubscriptionComponentError>`: `TryGetNoContent` [404] · `TryGetRawError` | none |
| `AllocateComponent` | `AllocateComponent(int subscriptionId, int componentId, CreateAllocationRequest? body, CancellationToken ct = default)` — quantity / on-off / prepaid only | `AllocationResponse` | **A** `SdkException<AllocateComponentError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `PreviewAllocations` | `PreviewAllocations(int subscriptionId, PreviewAllocationsRequest? body, CancellationToken ct = default)` | `AllocationPreviewResponse` | **A** `SdkException<PreviewAllocationsError>`: `TryGetComponentAllocationError1(out ComponentAllocationError1)` [422] · `TryGetRawError` | none |

`SubscriptionIdOrReference` / `ComponentIdModel`: AnyOf int\|string; factories `.Int` / `.String`; implicit from `int` and `string`. Handle form for component: `"handle:<handle>"`.

- `CreateUsageRequest`: `Usage (usage): CreateUsage !req`
- `CreateUsage`: `Quantity (quantity): double?`, `PricePointId (price_point_id): string?`, `Memo (memo): string?`, `BillingSchedule (billing_schedule): BillingSchedule?` (`InitialBillingAt`)
- `UsageResponse`: `Usage (usage): Usage !req` — `Id (id): long?`, `Quantity (quantity): Quantity1?` (union int\|string), `Memo`, `CreatedAt`, `ComponentId`, `ComponentHandle`, `SubscriptionId`, `PricePointId`, `OverageQuantity`
- `SubscriptionComponentResponse`: `Component (component): SubscriptionComponent?`
- `SubscriptionComponent` (read): `Id`, `Name`, `Kind`, `ComponentId`, `ComponentHandle`, `SubscriptionId`, `Enabled`, `UnitBalance (unit_balance): int?`, `AllocatedQuantity (allocated_quantity): AllocatedQuantity2?` (union int\|string — `TryGetInt` / `TryGetString`), `PricingScheme`, `PricePointId`/`Handle`/`Name`, `Recurring`, `UnitName`
- `CreateAllocationRequest`: `Allocation (allocation): CreateAllocation !req`
- `CreateAllocation` **required**: `Quantity (quantity): double !req`. Optional: `ComponentId (component_id): int?`, `Memo`, `UpgradeCharge (upgrade_charge): UpgradeChargeCreditType?`, `DowngradeCredit (downgrade_credit): DowngradeCreditCreditType?`, `AccrueCharge (accrue_charge): bool?`, `PricePointId (price_point_id): PricePointId1?` (union)
- `AllocationResponse`: `Allocation (allocation): Allocation?`
- `AllocationPreviewResponse`: `AllocationPreview (allocation_preview): AllocationPreview !req` — `TotalInCents`, `Direction (direction): AllocationPreviewDirection?`, `LineItems`
- `PreviewAllocationsRequest`: `Allocations (allocations): IReadOnlyList<CreateAllocation> !req`, `EffectiveProrationDate`, `UpgradeCharge`, `DowngradeCredit`
- `ComponentAllocationError1`: `Errors (errors): IReadOnlyList<ComponentAllocationErrorItem>?` — item: `ComponentId`, `Message`, `Kind`, `On`

Ebb ingest (`RecordEvent` / `BulkRecordEvents`) uses the **Ebb** server group, not Production. Out of default eShop metered path — only if event-based components are configured.

---

### 8. Coupons — `client.Coupons` · `operations/Coupons.md`

| Op | Signature | Returns | Error | Pagination |
|---|---|---|---|---|
| `ValidateCoupon` | `ValidateCoupon(string code, int? productFamilyId, CancellationToken ct = default)` — `productFamilyId` **must pass explicitly** (`null` = site default family) | `CouponResponse` | **A** `SdkException<ValidateCouponError>`: `TryGetSingleStringErrorResponse1(out SingleStringErrorResponse1)` [404] · `TryGetRawError` | none |
| `FindCoupon` | `FindCoupon(int? productFamilyId, string? code, bool? currencyPrices, CancellationToken ct = default)` — all three **must pass explicitly** | `CouponResponse` | **B** | none |
| `ListCoupons` | `ListCoupons(ListCouponsFilter? filter, bool? currencyPrices, int? page = 1, int? perPage = 30, CancellationToken ct = default)` — `filter` and `currencyPrices` **must pass explicitly** | `IReadOnlyList<CouponResponse>` | **B** | `page`+`perPage` (1/30) |
| `ReadCoupon` | `ReadCoupon(int productFamilyId, int couponId, bool? currencyPrices, CancellationToken ct = default)` — `currencyPrices` **must pass explicitly** | `CouponResponse` | **B** | none |
| `CreateCoupon` | `CreateCoupon(int productFamilyId, CouponRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly**. Either `amount_in_cents` **or** `percentage`. | `CouponResponse` | **A** `SdkException<CreateCouponError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |

- `CouponResponse`: `Coupon (coupon): Coupon?`
- `Coupon` (read): `Id`, `Name`, `Code`, `Description`, `Amount (amount): double?`, `AmountInCents (amount_in_cents): long?`, `Percentage (percentage): string?`, `DiscountType (discount_type): DiscountType?`, `Recurring`, `Stackable`, `CompoundingStrategy`, `ProductFamilyId`, `StartDate`, `EndDate`, `ArchivedAt`, `AllowNegativeBalance`
- `CouponRequest`: `Coupon (coupon): CouponPayload?`, `RestrictedProducts (restricted_products): IReadOnlyDictionary<string, bool>?`, `RestrictedComponents (restricted_components): IReadOnlyDictionary<string, bool>?`
- `CouponPayload`: `Name`, `Code`, `Description`, `Percentage (percentage): Percentage?` (union string\|double), `AmountInCents (amount_in_cents): long?`, `Recurring`, `Stackable`, `CompoundingStrategy`, `EndDate`, `AllowNegativeBalance`
- `ListCouponsFilter`: `DateField`, `StartDate`/`EndDate`/`StartDatetime`/`EndDatetime`, `Ids`, `Codes (codes): IReadOnlyList<string>?`, `UseSiteExchangeRate`, `IncludeArchived`
- Apply to a subscription: Step 2 `ApplyCouponsToSubscription` / create-time `coupon_code(s)` — not `Coupons.CreateCoupon`.

---

### 9. Payment profiles — `client.PaymentProfiles` · `operations/PaymentProfiles.md`

Required for subscribe when the product has `require_credit_card`. Creating a profile does **not** make it default on existing subscriptions.

| Op | Signature | Returns | Error | Pagination |
|---|---|---|---|---|
| `CreatePaymentProfile` | `CreatePaymentProfile(CreatePaymentProfileRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | `PaymentProfileResponse` | **A** `SdkException<CreatePaymentProfileError>`: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `ReadPaymentProfile` | `ReadPaymentProfile(int paymentProfileId, CancellationToken ct = default)` | `PaymentProfileResponse` | **A** `SdkException<ReadPaymentProfileError>`: `TryGetNoContent` [404] · `TryGetRawError` | none |
| `ListPaymentProfiles` | `ListPaymentProfiles(int? customerId, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — `customerId` **must pass explicitly** | `IReadOnlyList<PaymentProfileResponse>` | **B** | `page`+`perPage` |
| `UpdatePaymentProfile` | `UpdatePaymentProfile(int paymentProfileId, UpdatePaymentProfileRequest? body, CancellationToken ct = default)` | `PaymentProfileResponse` | **A** `SdkException<UpdatePaymentProfileError>`: `TryGetNoContent` [404] · `TryGetErrorStringMapResponse1(out ErrorStringMapResponse1)` [422] · `TryGetRawError` | none |
| `ChangeSubscriptionDefaultPaymentProfile` | `ChangeSubscriptionDefaultPaymentProfile(int subscriptionId, int paymentProfileId, CancellationToken ct = default)` | `PaymentProfileResponse` | **A** `SdkException<ChangeSubscriptionDefaultPaymentProfileError>`: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `ReadOneTimeToken` | `ReadOneTimeToken(string chargifyToken, CancellationToken ct = default)` | `GetOneTimeTokenRequest` | **A** `SdkException<ReadOneTimeTokenError>`: `TryGetErrorListResponse1` [404] · `TryGetRawError` | none |

- `CreatePaymentProfileRequest`: `PaymentProfile (payment_profile): CreatePaymentProfile !req`
- `CreatePaymentProfile`: `CustomerId (customer_id): int?` (required in practice to attach), `ChargifyToken (chargify_token): string?` (preferred), `PaymentType (payment_type): PaymentType?`, `FullNumber`, `ExpirationMonth (expiration_month): ExpirationMonth1?` (union), `ExpirationYear (expiration_year): ExpirationYear1?`, `FirstName`, `LastName`, `Cvv`, billing address, bank fields, `CurrentVault (current_vault): AllVaults?` (`AllVaults.Bogus` for test)
- `PaymentProfileResponse`: `PaymentProfile (payment_profile): PaymentProfile !req` — **OneOf union** (`MaxioAdvancedBilling.Models.OneOf.PaymentProfile`). Read via `TryGetCreditCardPaymentProfile(out CreditCardPaymentProfile)`, `TryGetBankAccountPaymentProfile`, `TryGetPaypalPaymentProfile`, `TryGetApplePayPaymentProfile`. Factories: `PaymentProfile.CreditCardPaymentProfile(…)`, etc. Implicit from each variant.
- `CreditCardPaymentProfile` (read): `Id`, `MaskedCardNumber`, `CardType`, `ExpirationMonth`/`Year` (`int?`), `CustomerId`, `PaymentType` default `PaymentType.CreditCard`, `CurrentVault (current_vault): CreditCardVault?`, billing fields, `Disabled`
- `UpdatePaymentProfileRequest`: `PaymentProfile (payment_profile): UpdatePaymentProfile !req` — billing/name/`ExpirationMonth`/`Year` as `string?`; changing PAN requires a **new** profile on most gateways.
- `GetOneTimeTokenRequest`: `PaymentProfile (payment_profile): GetOneTimeTokenPaymentProfile !req`

---

### 10. Invoices — `client.Invoices` · `operations/Invoices.md`

| Op | Signature | Returns | Error | Pagination |
|---|---|---|---|---|
| `ListInvoices` | `ListInvoices(string? startDate, string? endDate, InvoiceStatus? status, int? subscriptionId, string? subscriptionGroupUid, string? consolidationLevel, Direction? direction, InvoiceDateField? dateField, string? startDatetime, string? endDatetime, IReadOnlyList<int>? customerIds, IReadOnlyList<string>? number, IReadOnlyList<int>? productIds, InvoiceSortField? sort, int? page = 1, int? perPage = 20, bool? lineItems = false, bool? discounts = false, bool? taxes = false, bool? credits = false, bool? payments = false, bool? customFields = false, bool? refunds = false, CancellationToken ct = default)` — 14 filters `startDate`…`sort` **must pass explicitly**. Breakdown flags default `false` (totals only). | `ListInvoicesResponse` | **B** | `page`+`perPage` (1/20) |
| `ReadInvoice` | `ReadInvoice(string uid, CancellationToken ct = default)` | `Invoice` (not wrapped) | **B** | none |
| `CreateInvoice` | `CreateInvoice(int subscriptionId, CreateInvoiceRequest? body, CancellationToken ct = default)` | `InvoiceResponse` | **A** `SdkException<CreateInvoiceError>`: `TryGetErrorArrayMapResponse1` [422] · `TryGetRawError` | none |
| `IssueInvoice` | `IssueInvoice(string uid, IssueInvoiceRequest? body, CancellationToken ct = default)` | `Invoice` | **A** `SdkException<IssueInvoiceError>`: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `RecordPaymentForInvoice` | `RecordPaymentForInvoice(string uid, CreateInvoicePaymentRequest? body, CancellationToken ct = default)` | `Invoice` | **A** `SdkException<RecordPaymentForInvoiceError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `RecordPaymentForSubscription` | `RecordPaymentForSubscription(int subscriptionId, RecordPaymentRequest? body, CancellationToken ct = default)` | `RecordPaymentResponse` | **A** `SdkException<RecordPaymentForSubscriptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |

- `ListInvoicesResponse`: `Invoices (invoices): IReadOnlyList<Invoice> !req` — **not** an `InvoiceResponse` wrapper
- `InvoiceResponse` (create): `Invoice (invoice): Invoice !req`
- `Invoice` (read — key fields): `Id (id): long?`, `Uid (uid): string?` (path id for Read/Issue), `Number (number): string?`, `Status (status): InvoiceStatus?`, `SubscriptionId (subscription_id): int?`, `CustomerId (customer_id): int?`, `IssueDate`, `DueDate`, `PaidDate`, `TotalAmount (total_amount): string?`, `DueAmount (due_amount): string?`, `PaidAmount`, `SubtotalAmount`, `DiscountAmount`, `TaxAmount`, `CreditAmount`, `Currency`, `CollectionMethod`, `Role (role): InvoiceRole?`, `PublicUrl (public_url)`, `LineItems` (only if list `lineItems: true` or on Read)
- `CreateInvoiceRequest`: `Invoice (invoice): CreateInvoice !req`
- `CreateInvoice`: `LineItems (line_items): IReadOnlyList<CreateInvoiceItem>?`, `IssueDate`, `NetTerms (net_terms): int?`, `Memo`, `Coupons`, `Status (status): CreateInvoiceStatus? = CreateInvoiceStatus.Open`
- `CreateInvoiceItem`: `Title`, `Quantity (quantity): Quantity3?` (union double\|string), `UnitPrice (unit_price): UnitPrice7?`, `ProductId (product_id): ProductId?` (union), `ComponentId (component_id): ComponentId3?`
- `IssueInvoiceRequest`: `OnFailedPayment (on_failed_payment): FailedPaymentAction? = FailedPaymentAction.LeaveOpenInvoice`
- `CreateInvoicePaymentRequest`: `Payment (payment): CreateInvoicePayment !req`, `Type (type): InvoicePaymentType?`
- `CreateInvoicePayment`: `Amount (amount): Amount?` (union string\|double), `Memo`, `Method (method): InvoicePaymentMethodType?`, `Details`, `PaymentProfileId (payment_profile_id): int?`
- `RecordPaymentRequest`: `Payment (payment): CreatePayment !req` — `Amount (amount): string !req`, `Memo (memo): string !req`, `PaymentDetails (payment_details): string !req`, `PaymentMethod (payment_method): InvoicePaymentMethodType !req`
- `RecordPaymentResponse`: `PaidInvoices (paid_invoices): IReadOnlyList<PaidInvoice>?`, `Prepayment (prepayment): InvoicePrePayment?`

---

### Unions the integration constructs / reads (`map/models/unions.md`)

Namespace `MaxioAdvancedBilling.Models.AnyOf` unless noted `OneOf`.

| Union | Variants | Construct | Read |
|---|---|---|---|
| `SubscriptionIdOrReference` | int, string | `.Int(int)`, `.String(string)`; implicit | `TryGetInt`, `TryGetString` |
| `ComponentIdModel` / `ProductIdModel` / `PricePointIdModel` | int, string | `.Int`, `.String`; implicit | `TryGetInt`, `TryGetString` |
| `ComponentId1` (create-sub component id) | int, string | `.Int`, `.String` | `TryGetInt`, `TryGetString` |
| `AllocatedQuantity2` / `Quantity1` / `UnitPrice` / `UnitPrice1` / `Percentage` / `Amount` / `OfferId` / `SnapDay1` / `NetTerms1` / `PriceInCents` / `Interval` / `ExpirationMonth1`/`2` / `ExpirationYear1`/`2` | see unions.md | matching `.Int`/`.String`/`.Double`/`.Long` | matching `TryGet…` |
| `Resume` | bool, `ResumeOptions` | `Resume.Bool(bool)`, `Resume.ResumeOptions(ResumeOptions)` | `TryGetBool`, `TryGetResumeOptions` |
| `CancelSubscriptionErrorResponse` | `ErrorListResponse1`, `SingleErrorResponse1` | factories named as variants | `TryGetErrorListResponse1`, `TryGetSingleErrorResponse1` |
| `PaymentProfile` (**OneOf**, `Models.OneOf`) | ApplePay / BankAccount / CreditCard / Paypal profiles | `PaymentProfile.CreditCardPaymentProfile(…)` etc. | `TryGetCreditCardPaymentProfile` etc. |
| `Refund` (invoice refund body) | `RefundInvoice`, `RefundConsolidatedInvoice` | `Refund.RefundInvoice(…)`, `.RefundConsolidatedInvoice(…)` | matching `TryGet…` |

Never `new` a union; never cast. `IntervalUnit? !req` on `SubscriptionCustomPrice.IntervalUnit` is required-but-nullable — pass explicitly in the initializer.

---

### Enums actually used (`map/models/enums.md`) — `MaxioAdvancedBilling.Models.Enums`

These are **`StringEnum<T>` records, not C# enums**. Write `CollectionMethod.Automatic` or `CollectionMethod.FromValue("automatic")`. Member = C# identifier; parenthetical = wire value.

| Enum | Members |
|---|---|
| `ServerEnvironment` | `Us (US)`, `Eu (EU)` — namespace **`MaxioAdvancedBilling.Servers`**, not Models.Enums |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` |
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `SubscriptionStateFilter` | `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)` |
| `SubscriptionInclude` | `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)` |
| `SubscriptionListInclude` | `SelfServicePageToken (self_service_page_token)` |
| `SubscriptionDateField` | `CurrentPeriodEndsAt (current_period_ends_at)`, `CurrentPeriodStartsAt (current_period_starts_at)`, `CreatedAt (created_at)`, `ActivatedAt (activated_at)`, `CanceledAt (canceled_at)`, `ExpiresAt (expires_at)`, `TrialStartedAt (trial_started_at)`, `TrialEndedAt (trial_ended_at)`, `UpdatedAt (updated_at)` |
| `SubscriptionSort` | `SignupDate (signup_date)`, `PeriodStart (period_start)`, `PeriodEnd (period_end)`, `NextAssessment (next_assessment)`, `UpdatedAt (updated_at)`, `CreatedAt (created_at)`, `TotalPayments (total_payments)`, `Id (id)`, `OpenBalance (open_balance)`, `ExpiresAt (expires_at)` |
| `SortingDirection` | `Asc (asc)`, `Desc (desc)` |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` |
| `IntervalUnit` | `Day (day)`, `Month (month)` |
| `ExpirationIntervalUnit` | `Day (day)`, `Month (month)`, `Never (never)` |
| `TrialType` | `NoObligation (no_obligation)`, `PaymentExpected (payment_expected)` |
| `PricePointType` | `Catalog (catalog)`, `Default (default)`, `Custom (custom)` |
| `PricingScheme` | `Stairstep (stairstep)`, `Volume (volume)`, `PerUnit (per_unit)`, `Tiered (tiered)` |
| `ComponentKind` | `MeteredComponent (metered_component)`, `QuantityBasedComponent (quantity_based_component)`, `OnOffComponent (on_off_component)`, `PrepaidUsageComponent (prepaid_usage_component)`, `EventBasedComponent (event_based_component)` |
| `CreditType` | `Full (full)`, `Prorated (prorated)`, `None (none)` |
| `UpgradeChargeCreditType` / `DowngradeCreditCreditType` | `Full (full)`, `Prorated (prorated)`, `None (none)` |
| `ResumptionCharge` | `Prorated (prorated)`, `Immediate (immediate)`, `Delayed (delayed)` |
| `ReactivationCharge` | `Prorated (prorated)`, `Immediate (immediate)`, `Delayed (delayed)` |
| `PaymentType` | `CreditCard (credit_card)`, `BankAccount (bank_account)`, `PaypalAccount (paypal_account)`, `ApplePay (apple_pay)` |
| `CardType` | `Visa (visa)`, `Master (master)`, `Discover (discover)`, `AmericanExpress (american_express)`, `Bogus (bogus)`, … (full list in `enums.md`) |
| `CreditCardVault` / `AllVaults` | include `Bogus (bogus)`, `Stripe (stripe)`, `Authorizenet (authorizenet)`, `MaxioPayments (maxio_payments)`, … |
| `InvoiceStatus` | `Draft (draft)`, `Open (open)`, `Paid (paid)`, `Pending (pending)`, `Voided (voided)`, `Canceled (canceled)`, `Processing (processing)` |
| `InvoiceDateField` | `CreatedAt (created_at)`, `DueDate (due_date)`, `IssueDate (issue_date)`, `UpdatedAt (updated_at)`, `PaidDate (paid_date)` |
| `InvoiceSortField` | `Status (status)`, `TotalAmount (total_amount)`, `DueAmount (due_amount)`, `CreatedAt (created_at)`, `UpdatedAt (updated_at)`, `IssueDate (issue_date)`, `DueDate (due_date)`, `Number (number)` |
| `InvoiceRole` | `Unset (unset)`, `Signup (signup)`, `Renewal (renewal)`, `Usage (usage)`, `Reactivation (reactivation)`, `Proration (proration)`, `Migration (migration)`, `Adhoc (adhoc)`, `Backport (backport)`, `BackportBalanceReconciliation (backport-balance-reconciliation)` |
| `CreateInvoiceStatus` | `Draft (draft)`, `Open (open)` |
| `FailedPaymentAction` | `LeaveOpenInvoice (leave_open_invoice)`, `RollbackToPending (rollback_to_pending)`, `InitiateDunning (initiate_dunning)` |
| `InvoicePaymentMethodType` | `CreditCard (credit_card)`, `Check (check)`, `Cash (cash)`, `MoneyOrder (money_order)`, `Ach (ach)`, `Other (other)` |
| `InvoicePaymentType` | `External (external)`, `Prepayment (prepayment)`, `ServiceCredit (service_credit)`, `Payment (payment)` |
| `Direction` | `Asc (asc)`, `Desc (desc)` |
| `DiscountType` | `Amount (amount)`, `Percent (percent)` |
| `CompoundingStrategy` | `Compound (compound)`, `FullPrice (full-price)` |
| `ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)` |
| `ListSubscriptionComponentsInclude` | `Subscription (subscription)`, `HistoricUsages (historic_usages)` |
| `ListSubscriptionComponentsSort` | `Id (id)`, `UpdatedAt (updated_at)` |
| `IncludeNotNull` | `NotNull (not_null)` |
| `CancellationMethod` | `MerchantUi (merchant_ui)`, `MerchantApi (merchant_api)`, `Dunning (dunning)`, `BillingPortal (billing_portal)`, `Unknown (unknown)`, `Imported (imported)` |
| `AllocationPreviewDirection` | `Upgrade (upgrade)`, `Downgrade (downgrade)` |
| `CreditScheme` | `None (none)`, `Credit (credit)`, `Refund (refund)` |
| `RecurringScheme` | `DoNotRecur (do_not_recur)`, `RecurIndefinitely (recur_indefinitely)`, `RecurWithDuration (recur_with_duration)` |

---

## Trap notes

⚠ Step 1 (client registration) — the `HttpClient`/handler pipeline must be long-lived; the SDK client wrapper over it may not share that lifetime; DI registration vs `new` is not obvious from the constructor. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ Step 1 (authentication) — credentials must be present before the first call; Username/Password mapping is easy to invert; do not hardcode the API key. **MUST load `dotnet-authentication`**.

⚠ Step 1 (resilience) — the SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; whether a failed write can be re-sent is not visible from `RetryOptions` members alone. **MUST load `dotnet-configuration-resilience`** before wiring the client.

⚠ Steps 2–11 (calls) — list/search operations have many optional parameters with **no C# default**; a positional call mis-binds (e.g. `ListCustomers`, `ListSubscriptions`, `ListInvoices`, `ListComponents`, `ReadSubscription`'s `include`). Named arguments only; token is `ct:`. **MUST load `dotnet-calling-endpoints`**.

⚠ Steps 3–11 (models) — envelopes wrap payload (`CustomerResponse.Customer`, `SubscriptionResponse.Subscription`, `ProductResponse.Product`, `PaymentProfileResponse.PaymentProfile`); unions are factories + `TryGet…` (no `new`); enums are `StringEnum<T>` (`CollectionMethod.Automatic`, not `CollectionMethod.automatic`); `required` members must appear in the object initializer; `CreateProduct`/`ListProductsForProductFamily` take `string productFamilyId` while `ReadProductFamily` takes `int id`. **MUST load `dotnet-models`**.

⚠ Step 12 (error boundary) — Case A vs Case B differ **per operation** (reads are often B; writes often A); `TryGetRawError` is not a catch-all on the wrong exception type. A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 12 (tests) — the `HttpClient` constructor argument is the test seam; do not fake SDK controller types or internals. **MUST load `dotnet-testing`**.

---

## REQUIRED READING

Load **before implementation starts**. This sheet does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — constructing/registering `MaxioAdvancedBillingClient`, `HttpClient` lifetime, `AddMaxioAdvancedBillingClient` |
| `dotnet-authentication` | Step 1 — Basic credentials on options / DI callback |
| `dotnet-configuration-resilience` | Step 1 — `RetryOptions`, timeouts, server/`Site`/`BaseUrl`, list pagination |
| `dotnet-calling-endpoints` | Steps 2–11 — first call to any operation; named args; `ct` |
| `dotnet-models` | Steps 3–11 — request/response records, envelopes, enums, unions/AnyOf |
| `dotnet-error-handling` | Step 12 — catch ladder, Case A/B, both `JsonException` directions |
| `dotnet-testing` | Step 12 — faking the `HttpClient` seam |

Always include `dotnet-error-handling`: every integration writes an error boundary. The two `JsonException` directions in the trap notes need opposite handling.

---

## Assumptions & Blockers

**Assumptions**

- eShop buyers map 1:1 to Maxio customers via `Customer.Reference` = application user/buyer id; persist Maxio `Customer.Id` and `Subscription.Id` on the eShop side.
- Catalog (product families / products / price points / components) is authored in Maxio (or once via admin APIs); the storefront lists/reads handles and subscribes by `product_handle` / `product_id`.
- Card collection in production uses Maxio.js `chargify_token` (or an existing `payment_profile_id`), not raw PAN, unless the host is PCI-compliant. Sandbox may use `AllVaults.Bogus`.
- Pause/resume means on-hold (`PauseSubscription` / `ResumeSubscription`). Reactivation of canceled subscriptions is a separate path (`ReactivateSubscription`).
- Immediate prorated plan change uses `MigrateSubscriptionProduct`; next-renewal change uses `UpdateSubscription.ProductChangeDelayed = true`.
- Site subdomain + API key come from configuration (`Maxio:ApiKey`, `Maxio:Site`, `Maxio:Environment`). Default environment is `ServerEnvironment.Us`.
- Invoice list for a buyer filters `subscriptionId` (and/or `customerIds`); line-item breakdown requires the boolean include flags (they default false).
- Event-based billing ingest (Ebb server) is out of the default eShop path unless the catalog uses event-based components.

**Blockers**

- None that block planning. Runtime cannot call the API until a real site subdomain and API key are configured. Customer 422 payload shape vs `CustomerErrorResponse1.Errors` (`Errors` vs sibling `Errors1`) is **UNVERIFIED** — handle defensively as noted above.
