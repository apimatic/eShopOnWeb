# Maxio Advanced Billing — eShopOnWeb subscription billing

NuGet: `AsadAli.AdvancedBilling.Sdk` · Root namespace: `MaxioAdvancedBilling` · Map stamp: `v1.0.2` / `15db14b`.

## Scope & sequence

1. **Client, auth, site** — construct/register `MaxioAdvancedBillingClient` with Basic auth and `{site}` subdomain. No billing operation until this exists.
2. **Catalog lookup** — `Products.ListProducts` / `Products.ReadProductByHandle` to resolve storefront plans to Maxio `product_id` / `product_handle`. `Components.FindComponent` when a plan has metered usage.
3. **Customers** — `Customers.ReadCustomerByReference` (store customer id → Maxio `reference`); on miss, `Customers.CreateCustomer`. Optional `Customers.UpdateCustomer` when store profile changes.
4. **Subscribe** — `Subscriptions.CreateSubscription` (existing customer via `customer_id` or `customer_reference`; product via `product_id` or `product_handle`; coupon via `coupon_code` / `coupon_codes`; payment via `payment_profile_id` or nested `payment_profile_attributes.chargify_token`). Optional `PaymentProfiles.CreatePaymentProfile` first. Optional `Subscriptions.PreviewSubscription` before charge.
5. **Read status** — `Subscriptions.ReadSubscription` by Maxio id; `Customers.ListCustomerSubscriptions` for all of a customer.
6. **Usage (metered)** — `SubscriptionComponents.CreateUsage` per component; `SubscriptionComponents.ListSubscriptionComponents` / `ListUsages` to inspect balances.
7. **Invoices** — `Invoices.ListInvoices` filtered by `customerIds` and/or `subscriptionId`; `Invoices.ReadInvoice` for one uid.
8. **Plan change** — `SubscriptionProducts.PreviewSubscriptionProductMigration` then `MigrateSubscriptionProduct` (immediate/prorated). Delayed next-period change via `Subscriptions.UpdateSubscription` (`product_handle`/`product_id` + `product_change_delayed: true`).
9. **Coupons** — `Coupons.ValidateCoupon` at checkout; `Subscriptions.ApplyCouponsToSubscription` on an existing sub; `Subscriptions.RemoveCouponFromSubscription` to drop a code.
10. **Error boundary** — wrap every SDK call; Case A vs Case B differs per operation (see CONTRACT SHEET). No `…Result` / no-throw variants exist.

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

### Client construction / auth / server (`sdk-map.md`)

| Fact | Value | Cite |
|---|---|---|
| Package | `AsadAli.AdvancedBilling.Sdk` | `sdk-map.md` |
| Client | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` | `MaxioAdvancedBillingClient.cs` |
| Only ctor | `MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` |
| Options | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` — `Environment`: `MaxioAdvancedBilling.Servers.ServerEnvironment`; `Retry`: `MaxioAdvancedBilling.Core.Configuration.RetryOptions`; `Server`: `MaxioAdvancedBilling.Servers.ServerOptions`; `BasicAuth`: `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?` | `MaxioAdvancedBillingClientOptions.cs` |
| Auth | HTTP Basic. `BasicAuthCredentials.Username` (required `string`) = API key; `Password` (required `string`) = literal `"x"`. | `sdk-map.md`, `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Environments | `ServerEnvironment.Us` (wire `US`, default) → `https://{site}.chargify.com`; `ServerEnvironment.Eu` (wire `EU`) → `https://{site}.ebilling.maxio.com`. Members are `StringEnum<T>` statics, not C# enums. | `sdk-map.md`, `Servers/ServerEnvironment.cs` |
| Site | `options.Server.Production.Us.Site = "<subdomain>"` (US) or `.Eu.Site` (EU). Default Site is `"subdomain"`. Mock host: override `options.Server.Production.Us.BaseUrl`. | `sdk-map.md`, `Servers/ProductionOptions.cs` |
| DI | `services.AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>? configure = null)` — `MaxioAdvancedBilling.ServiceCollectionExtensions` | `ServiceCollectionExtensions.cs` |
| RetryOptions | namespace `MaxioAdvancedBilling.Core.Configuration`; all members `required`; start from `RetryOptions.Default()`. Members: `StatusCodesToRetry` `IReadOnlyList<HttpStatusCode>`; `HttpMethodsToRetry` `IReadOnlyList<HttpMethod>`; `MaxRetries` `int`; `Delay` `TimeSpan`; `Timeout` `TimeSpan?`; `BackOffFactor` `int`; `UseExponentialBackoff` `bool`; `MaxJitter` `TimeSpan`; `OnRetry` `Action<RetryAttempt>?` | `sdk-map.md` |
| Throw model | Every operation is throw-only. Errors: `MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>` with `.Error`. Case A: `TError` is `MaxioAdvancedBilling.Errors.{Op}Error` with `TryGet…` + inherited `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)`. Case B: `TError` is `RawError` (`StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`). | `sdk-map.md`, `Core/Exceptions/SdkException.cs` |

Namespaces to import by kind (`sdk-map.md`): client/options `MaxioAdvancedBilling`; records `MaxioAdvancedBilling.Models`; enums `MaxioAdvancedBilling.Models.Enums`; unions `MaxioAdvancedBilling.Models.AnyOf` / `.OneOf`; errors `MaxioAdvancedBilling.Errors`; `SdkException<>` `MaxioAdvancedBilling.Core.Exceptions`; `RawError` `MaxioAdvancedBilling.Core.ErrorResponse`; `BasicAuthCredentials` `MaxioAdvancedBilling.Core.Authentication.Basic`; `RetryOptions` `MaxioAdvancedBilling.Core.Configuration`; `ServerEnvironment` `MaxioAdvancedBilling.Servers`.

---

### Operations

#### Step 2 — Products (`operations/Products.md` · `client.Products` · `Api/Products.cs`)

| Op | Signature | Request | Response envelope | Error | Pagination |
|---|---|---|---|---|---|
| `ListProducts` | `ListProducts(MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, MaxioAdvancedBilling.Models.ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 params `dateField`…`include` nullable, **must pass explicitly** (`null` to skip) | query: `date_field`←`dateField`, `filter`←`filter`, `end_date`←`endDate`, `end_datetime`←`endDatetime`, `start_date`←`startDate`, `start_datetime`←`startDatetime`, `page`←`page`, `per_page`←`perPage`, `include_archived`←`includeArchived`, `include`←`include`. Filter (`records-2-Cr-Ne.md`): `Ids (ids): IReadOnlyList<int>?`, `PrepaidProductPricePoint (prepaid_product_price_point): PrepaidProductPricePointFilter?`, `UseSiteExchangeRate (use_site_exchange_rate): bool?` | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>` — each `Product (product): Product !req`. Read `Product.Id`, `Handle`, `Name`, `PriceInCents`, `Interval`, `IntervalUnit`, `RequireCreditCard`, `ProductPricePointId`, `ProductPricePointHandle` | **Case B** `SdkException<RawError>` | manual `page`+`perPage` (default 1/20) |
| `ReadProduct` | `ReadProduct(int productId, CancellationToken ct = default)` | path `{product_id}` | `ProductResponse` → `.Product` | **Case B** `SdkException<RawError>` | none |
| `ReadProductByHandle` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | path `{api_handle}` | `ProductResponse` → `.Product` | **Case B** `SdkException<RawError>` | none |

`Product` fields used (`records-3-Of-Su.md`): `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `TrialPriceInCents (trial_price_in_cents): long?`, `TrialInterval (trial_interval): int?`, `RequireCreditCard (require_credit_card): bool?`, `ProductFamily (product_family): ProductFamily?`, `DefaultProductPricePointId (default_product_price_point_id): int?`, `ProductPricePointId (product_price_point_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?`. Nested `ProductFamily` (`records-3-Of-Su.md`): `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`.

#### Step 2 — Components lookup (`operations/Components.md` · `client.Components` · `Api/Components.cs`)

| Op | Signature | Request | Response envelope | Error | Pagination |
|---|---|---|---|---|---|
| `FindComponent` | `FindComponent(string handle, CancellationToken ct = default)` | query `handle`←`handle` | `ComponentResponse` — `Component (component): Component !req` (`records-1-Ac-Cr.md`) | **Case B** `SdkException<RawError>` | none |
| `ListComponents` | `ListComponents(BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, bool? includeArchived, ListComponentsFilter? filter, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 7 params `dateField`…`filter` **must pass explicitly** | query `date_field`, `start_date`, `end_date`, `start_datetime`, `end_datetime`, `include_archived`, `page`, `per_page`, `filter`. Filter: `Ids (ids): IReadOnlyList<int>?`, `UseSiteExchangeRate (use_site_exchange_rate): bool?` | `IReadOnlyList<ComponentResponse>` → each `.Component` | **Case B** `SdkException<RawError>` | manual `page`+`perPage` (1/20) |

`Component` fields used: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Kind (kind): ComponentKind?`, `UnitName (unit_name): string?`, `UnitPrice (unit_price): string?`, `ProductFamilyId (product_family_id): int?`, `DefaultPricePointId (default_price_point_id): int?`, `Recurring (recurring): bool?`.

#### Step 3 — Customers (`operations/Customers.md` · `client.Customers` · `Api/Customers.cs`)

| Op | Signature | Request | Response envelope | Error | Pagination |
|---|---|---|---|---|---|
| `ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | query `reference`←`reference` (store customer id) | `CustomerResponse` — `Customer (customer): Customer !req` | **Case B** `SdkException<RawError>` (404 when missing — read `StatusCode`) | none |
| `CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, **must pass explicitly** | envelope `CreateCustomerRequest`: `Customer (customer): CreateCustomer !req`. Inner `CreateCustomer` (`records-1-Ac-Cr.md`): `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `CcEmails (cc_emails): string?`, `Organization (organization): string?`, `Reference (reference): string?` (unique store id), `Address (address): string?`, `Address2 (address_2): string?`, `City (city): string?`, `State (state): string?` (ISO 3166-2), `Zip (zip): string?`, `Country (country): string?` (ISO 3166-1 alpha-2), `Phone (phone): string?`, `Locale (locale): string?`, `VatNumber (vat_number): string?`, `TaxExempt (tax_exempt): bool?`, `TaxExemptReason (tax_exempt_reason): string?`, `ParentId (parent_id): int?`, `SalesforceId (salesforce_id): string?` | `CustomerResponse` → `.Customer`. Persist `.Customer.Id` | **Case A** `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. Payload `CustomerErrorResponse1`: `Errors (errors): Errors?` where generated `Errors` is `PerPage (per_page): IReadOnlyList<string>?`, `PricePoint (price_point): IReadOnlyList<string>?` (`records-2-Cr-Ne.md`, `Models/CustomerErrorResponse1.cs`, `Models/Errors.cs`). **UNVERIFIED** whether a live 422 body matches that `Errors` record (the SDK also generates unused union `Errors1` = `CustomerError` \| `IReadOnlyList<string>`). Extract best-effort from `TryGetCustomerErrorResponse1`; if construction throws `JsonException`, fall back to the generic message — do not treat as a 5xx outage. | none |
| `ReadCustomer` | `ReadCustomer(int id, CancellationToken ct = default)` | path `{id}` | `CustomerResponse` → `.Customer` | **Case B** `SdkException<RawError>` | none |
| `UpdateCustomer` | `UpdateCustomer(int id, UpdateCustomerRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | envelope `UpdateCustomerRequest`: `Customer (customer): UpdateCustomer !req`. Inner (`records-4-Su-We.md`): `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `CcEmails (cc_emails): string?`, `Organization (organization): string?`, `Reference (reference): string?`, `Address (address): string?`, `Address2 (address_2): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `Locale (locale): string?`, `VatNumber (vat_number): string?`, `TaxExempt (tax_exempt): bool?`, `TaxExemptReason (tax_exempt_reason): string?`, `ParentId (parent_id): int?`, `Verified (verified): bool?`, `SalesforceId (salesforce_id): string?` | `CustomerResponse` → `.Customer` | **Case A** `SdkException<UpdateCustomerError>`: `TryGetNoContent(out RawError)` [404] · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError` [fallback] | none |
| `ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | path `{customer_id}` | `IReadOnlyList<SubscriptionResponse>` — each `.Subscription` | **Case B** `SdkException<RawError>` | none |
| `ListCustomers` | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — 7 params `direction`…`q` **must pass explicitly** | query `direction`, `page`, `per_page`←`perPage`, `date_field`←`dateField`, `start_date`, `end_date`, `start_datetime`, `end_datetime`, `q`. Use `q` for email/name search; exact reference match is `ReadCustomerByReference` | `IReadOnlyList<CustomerResponse>` | **Case B** `SdkException<RawError>` | manual `page`+`perPage` (1/50) |

`Customer` fields to persist/display (`records-2-Cr-Ne.md`): `Id (id): int?`, `Reference (reference): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Organization (organization): string?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, `Address/Address2/City/State/Zip/Country`, `Phone (phone): string?`, `Locale (locale): string?`.

#### Step 4 — Payment profiles (`operations/PaymentProfiles.md` · `client.PaymentProfiles` · `Api/PaymentProfiles.cs`)

| Op | Signature | Request | Response envelope | Error | Pagination |
|---|---|---|---|---|---|
| `CreatePaymentProfile` | `CreatePaymentProfile(CreatePaymentProfileRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | envelope: `PaymentProfile (payment_profile): CreatePaymentProfile !req` (`records-1-Ac-Cr.md`). Storefront path: `ChargifyToken (chargify_token): string?` + `CustomerId (customer_id): int?`. Also: `PaymentType (payment_type): PaymentType?`, `FirstName/LastName`, `FullNumber (full_number): string?` (raw PAN — PCI), `ExpirationMonth (expiration_month): ExpirationMonth1?` (union int\|string), `ExpirationYear (expiration_year): ExpirationYear1?` (union), billing address fields, `Cvv (cvv): string?`, bank fields, `GatewayHandle (gateway_handle): string?` | `PaymentProfileResponse` — `PaymentProfile (payment_profile): PaymentProfile !req` **(union)** (`records-3-Of-Su.md`, `unions.md`). Read via `TryGetCreditCardPaymentProfile(out CreditCardPaymentProfile)` / `TryGetBankAccountPaymentProfile` / `TryGetPaypalPaymentProfile` / `TryGetApplePayPaymentProfile`. Credit-card inner: `Id (id): int?`, `MaskedCardNumber (masked_card_number): string?`, `CardType (card_type): CardType?`, `CustomerId (customer_id): int?`, `PaymentType (payment_type): PaymentType` default `CreditCard` | **Case A** `SdkException<CreatePaymentProfileError>`: `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback]. `ErrorListResponse1`: `Errors (errors): IReadOnlyList<string> !req` | none |
| `ListPaymentProfiles` | `ListPaymentProfiles(int? customerId, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — `customerId` **must pass explicitly** | query `page`, `per_page`←`perPage`, `customer_id`←`customerId` | `IReadOnlyList<PaymentProfileResponse>` | **Case B** `SdkException<RawError>` | manual `page`+`perPage` (1/20) |

`PaymentProfile` union factories (`unions.md`, `Models/OneOf/PaymentProfile.cs`): `PaymentProfile.CreditCardPaymentProfile(…)`, `.BankAccountPaymentProfile(…)`, `.PaypalPaymentProfile(…)`, `.ApplePayPaymentProfile(…)`. Implicit from each variant.

#### Step 4 — Create subscription (`operations/Subscriptions.md` · `client.Subscriptions` · `Api/Subscriptions.cs`)

| Op | Signature | Request | Response envelope | Error | Pagination |
|---|---|---|---|---|---|
| `CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | envelope: `Subscription (subscription): CreateSubscription !req`. Inner (`records-2-Cr-Ne.md`): identify product `ProductHandle (product_handle): string?` **or** `ProductId (product_id): int?`; price point `ProductPricePointHandle (product_price_point_handle): string?` / `ProductPricePointId (product_price_point_id): int?`; customer `CustomerId (customer_id): int?` **or** `CustomerReference (customer_reference): string?` **or** nested `CustomerAttributes (customer_attributes): CustomerAttributes?`; payment `PaymentProfileId (payment_profile_id): int?` **or** `PaymentProfileAttributes (payment_profile_attributes): PaymentProfileAttributes?` (alias of credit-card attrs; `ChargifyToken (chargify_token): string?`) **or** `CreditCardAttributes` / `BankAccountAttributes`; coupons `CouponCode (coupon_code): string?`, `CouponCodes (coupon_codes): IReadOnlyList<string>?`; `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`; `Reference (reference): string?` (store subscription key); `Components (components): IReadOnlyList<CreateSubscriptionComponent>?`; `DeferSignup (defer_signup): bool? = false`; `NextBillingAt`/`InitialBillingAt`; `Currency (currency): string?`; `AgreementAcceptance (agreement_acceptance): AgreementAcceptance?` (required when using Maxio Payments). `CreateSubscriptionComponent`: `ComponentId (component_id): ComponentId1?` (union int\|string), `Enabled (enabled): bool?`, `UnitBalance (unit_balance): int?`, `AllocatedQuantity (allocated_quantity): AllocatedQuantity3?` (union), `Quantity (quantity): int?`, `PricePointId (price_point_id): PricePointId2?` (union) | `SubscriptionResponse` — `Subscription (subscription): Subscription?`. Read `.Subscription.Id`, `.State`, `.Customer.Id`, `.Product.Id`/`.Handle`, `.BalanceInCents`, `.CurrentPeriodEndsAt`, `.CouponCode`/`.CouponCodes` | **Case A** `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback]. 422 may include 3DS `action_link` in the error list — **UNVERIFIED** live shape; extract best-effort from `Errors` strings, fall back to generic message | none |
| `PreviewSubscription` | `PreviewSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | same `CreateSubscriptionRequest` as create (no card required for tax preview) | `SubscriptionPreviewResponse` — `SubscriptionPreview (subscription_preview): SubscriptionPreview !req` (`records-4-Su-We.md`): `CurrentBillingManifest (current_billing_manifest): BillingManifest?`, `NextBillingManifest (next_billing_manifest): BillingManifest?`. Manifest (`records-1-Ac-Cr.md`): `TotalInCents (total_in_cents): long?`, `TotalDiscountInCents`, `TotalTaxInCents`, `SubtotalInCents`, `StartDate`, `EndDate`, `LineItems` | **Case B** `SdkException<RawError>` | none |
| `FindSubscription` | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` **must pass explicitly** | query `reference`←`reference` | `SubscriptionResponse` → `.Subscription` | **Case A** `SdkException<FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError` [fallback] | none |

`CustomerAttributes` (`records-2-Cr-Ne.md`) — all optional (unlike `CreateCustomer`): `FirstName`, `LastName`, `Email`, `Reference`, address, `Phone`, `TaxExempt`, `VatNumber`, `Metafields (metafields): IReadOnlyDictionary<string, string>?`.

`PaymentProfileAttributes` (`records-3-Of-Su.md`): `ChargifyToken (chargify_token): string?`, `FullNumber (full_number): string?`, `ExpirationMonth (expiration_month): ExpirationMonth2?` (union), `ExpirationYear (expiration_year): ExpirationYear2?` (union), `Cvv (cvv): string?`, billing address, `CustomerId (customer_id): int?`, `PaymentType (payment_type): PaymentType?`.

#### Step 5 — Read subscription (`operations/Subscriptions.md`)

| Op | Signature | Request | Response envelope | Error | Pagination |
|---|---|---|---|---|---|
| `ReadSubscription` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` **must pass explicitly** (`null` to skip) | query `include`←`include`. Pass `SubscriptionInclude.Coupons` to populate coupon details | `SubscriptionResponse` → `.Subscription` | **Case B** `SdkException<RawError>` | none |
| `ListSubscriptions` | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 14 params `state`…`include` **must pass explicitly** | query `page`, `per_page`, `state`, `product`, `product_price_point_id`, `coupon`, `coupon_code`, `date_field`, `start_date`, `end_date`, `start_datetime`, `end_datetime`, `metadata`, `direction`, `sort`, `include` | `IReadOnlyList<SubscriptionResponse>` | **Case B** `SdkException<RawError>` | manual `page`+`perPage` (1/20) |

`Subscription` fields to read (`records-3-Of-Su.md`): `Id (id): int?`, `State (state): SubscriptionState?`, `BalanceInCents (balance_in_cents): long?`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `CurrentPeriodStartedAt (current_period_started_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `ActivatedAt (activated_at): DateTimeOffset?`, `CanceledAt (canceled_at): DateTimeOffset?`, `CancelAtEndOfPeriod (cancel_at_end_of_period): bool?`, `PreviousState (previous_state): SubscriptionState?`, `CouponCode (coupon_code): string?`, `CouponCodes (coupon_codes): IReadOnlyList<string>?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `Customer (customer): Customer?`, `Product (product): Product?`, `CreditCard (credit_card): CreditCardPaymentProfile?`, `Reference (reference): string?`, `ProductPricePointId (product_price_point_id): int?`, `NextProductId (next_product_id): int?`, `NextProductHandle (next_product_handle): string?`, `CurrentBillingAmountInCents (current_billing_amount_in_cents): long?`, `Currency (currency): string?`, `ReceivesInvoiceEmails (receives_invoice_emails): bool?`, `Coupons (coupons): IReadOnlyList<SubscriptionIncludedCoupon>?` (when `include` has Coupons). `SubscriptionIncludedCoupon` (`records-4-Su-We.md`): `Code (code): string?`, `UseCount (use_count): int?`, `UsesAllowed (uses_allowed): int?`, `ExpiresAt (expires_at): string?`, `Recurring (recurring): bool?`, `AmountInCents (amount_in_cents): long?`, `Percentage (percentage): string?`.

#### Step 6 — Usage (`operations/SubscriptionComponents.md` · `client.SubscriptionComponents` · `Api/SubscriptionComponents.cs`)

| Op | Signature | Request | Response envelope | Error | Pagination |
|---|---|---|---|---|---|
| `CreateUsage` | `CreateUsage(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, CreateUsageRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | path unions (`unions.md`): `SubscriptionIdOrReference.Int(int)` or `.String(string)` (reference); `ComponentIdModel.Int(int)` or `.String(string)` (`handle:` prefix allowed per op notes). Envelope: `Usage (usage): CreateUsage !req`. Inner (`records-2-Cr-Ne.md`): `Quantity (quantity): double?` (negative deducts; `unit_balance` floors at 0), `PricePointId (price_point_id): string?`, `Memo (memo): string?`, `BillingSchedule (billing_schedule): BillingSchedule?` (`InitialBillingAt (initial_billing_at): DateTimeOffset?`), `CustomPrice (custom_price): ComponentCustomPrice?` | `UsageResponse` — `Usage (usage): Usage !req`. Inner (`records-4-Su-We.md`): `Id (id): long?`, `Memo (memo): string?`, `CreatedAt (created_at): DateTimeOffset?`, `PricePointId (price_point_id): int?`, `Quantity (quantity): Quantity1?` **(union int\|string)** — read `TryGetInt` / `TryGetString`, `OverageQuantity (overage_quantity): int?`, `ComponentId (component_id): int?`, `ComponentHandle (component_handle): string?`, `SubscriptionId (subscription_id): int?` | **Case A** `SdkException<CreateUsageError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | none |
| `ListUsages` | `ListUsages(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, long? sinceId, long? maxId, DateTimeOffset? sinceDate, DateTimeOffset? untilDate, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 4 params `sinceId`…`untilDate` **must pass explicitly** | query `since_id`, `max_id`, `since_date`, `until_date`, `page`, `per_page`. `since_date`/`until_date` default to midnight on that date | `IReadOnlyList<UsageResponse>` | **Case B** `SdkException<RawError>` | manual `page`+`perPage` (1/20) |
| `ListSubscriptionComponents` | `ListSubscriptionComponents(int subscriptionId, SubscriptionListDateField? dateField, SortingDirection? direction, ListSubscriptionComponentsFilter? filter, string? endDate, string? endDatetime, IncludeNotNull? pricePointIds, IReadOnlyList<int>? productFamilyIds, ListSubscriptionComponentsSort? sort, string? startDate, string? startDatetime, IReadOnlyList<ListSubscriptionComponentsInclude>? include, bool? inUse, CancellationToken ct = default)` — 12 params `dateField`…`inUse` **must pass explicitly** | query `date_field`, `direction`, `filter`, `end_date`, `end_datetime`, `price_point_ids`, `product_family_ids`, `sort`, `start_date`, `start_datetime`, `include`, `in_use`. Filter: `Currencies (currencies): IReadOnlyList<string>?`, `UseSiteExchangeRate (use_site_exchange_rate): bool?` | `IReadOnlyList<SubscriptionComponentResponse>` — `Component (component): SubscriptionComponent?` | **Case B** `SdkException<RawError>` | none |
| `ReadSubscriptionComponent` | `ReadSubscriptionComponent(int subscriptionId, int componentId, CancellationToken ct = default)` | path ids | `SubscriptionComponentResponse` → `.Component` | **Case A** `SdkException<ReadSubscriptionComponentError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError` [fallback] | none |

`SubscriptionComponent` fields (`records-3-Of-Su.md`): `Id (id): int?`, `Name (name): string?`, `Kind (kind): ComponentKind?`, `UnitName (unit_name): string?`, `UnitBalance (unit_balance): int?`, `AllocatedQuantity (allocated_quantity): AllocatedQuantity2?` (union int\|string), `ComponentId (component_id): int?`, `ComponentHandle (component_handle): string?`, `SubscriptionId (subscription_id): int?`, `Enabled (enabled): bool?`, `PricePointId (price_point_id): int?`, `PricePointHandle (price_point_handle): string?`.

Quantity-based (non-metered) add-ons use `AllocateComponent` (`CreateAllocationRequest` → `Allocation (allocation): CreateAllocation !req` with `Quantity (quantity): double !req`) — **Case A** `TryGetErrorListResponse1` [422]. Not required unless the store has quantity/on-off add-ons.

#### Step 7 — Invoices (`operations/Invoices.md` · `client.Invoices` · `Api/Invoices.cs`)

| Op | Signature | Request | Response envelope | Error | Pagination |
|---|---|---|---|---|---|
| `ListInvoices` | `ListInvoices(string? startDate, string? endDate, InvoiceStatus? status, int? subscriptionId, string? subscriptionGroupUid, string? consolidationLevel, Direction? direction, InvoiceDateField? dateField, string? startDatetime, string? endDatetime, IReadOnlyList<int>? customerIds, IReadOnlyList<string>? number, IReadOnlyList<int>? productIds, InvoiceSortField? sort, int? page = 1, int? perPage = 20, bool? lineItems = false, bool? discounts = false, bool? taxes = false, bool? credits = false, bool? payments = false, bool? customFields = false, bool? refunds = false, CancellationToken ct = default)` — 14 params `startDate`…`sort` **must pass explicitly** | query: `start_date`, `end_date`, `status`, `subscription_id`←`subscriptionId`, `subscription_group_uid`, `consolidation_level`, `page`, `per_page`, `direction`, `line_items`←`lineItems` (default false — totals only unless `true`), `discounts`, `taxes`, `credits`, `payments`, `custom_fields`, `refunds`, `date_field`, `start_datetime`, `end_datetime`, `customer_ids`←`customerIds`, `number`, `product_ids`, `sort` | `ListInvoicesResponse` — `Invoices (invoices): IReadOnlyList<Invoice> !req` (**not** `InvoiceResponse`; items are bare `Invoice`) | **Case B** `SdkException<RawError>` | manual `page`+`perPage` (1/20) |
| `ReadInvoice` | `ReadInvoice(string uid, CancellationToken ct = default)` | path `{uid}` | `Invoice` **directly** (no envelope) | **Case B** `SdkException<RawError>` | none |

`Invoice` fields to display (`records-2-Cr-Ne.md`): `Id (id): long?`, `Uid (uid): string?`, `CustomerId (customer_id): int?`, `SubscriptionId (subscription_id): int?`, `Number (number): string?`, `Status (status): InvoiceStatus?`, `IssueDate (issue_date): DateTimeOffset?`, `DueDate (due_date): DateTimeOffset?`, `PaidDate (paid_date): DateTimeOffset?`, `Currency (currency): string?`, `ProductName (product_name): string?`, `SubtotalAmount (subtotal_amount): string?`, `DiscountAmount (discount_amount): string?`, `TaxAmount (tax_amount): string?`, `TotalAmount (total_amount): string?`, `PaidAmount (paid_amount): string?`, `DueAmount (due_amount): string?`, `CreditAmount (credit_amount): string?`, `PublicUrl (public_url): string?`, `LineItems (line_items): IReadOnlyList<InvoiceLineItem>?` (when `lineItems: true`), `Payments (payments): IReadOnlyList<InvoicePayment>?`. Amounts are **strings**, not cents.

`CreateInvoice` returns `InvoiceResponse` (`Invoice (invoice): Invoice !req`) — different envelope from `ReadInvoice` / `ListInvoices`. Not in storefront list-status scope unless issuing ad-hoc invoices.

#### Step 8 — Plan change (`operations/SubscriptionProducts.md` · `client.SubscriptionProducts` · `Api/SubscriptionProducts.cs`)

| Op | Signature | Request | Response envelope | Error | Pagination |
|---|---|---|---|---|---|
| `PreviewSubscriptionProductMigration` | `PreviewSubscriptionProductMigration(int subscriptionId, SubscriptionMigrationPreviewRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | envelope: `Migration (migration): SubscriptionMigrationPreviewOptions !req` (`records-4-Su-We.md`): `ProductId (product_id): int?` **or** `ProductHandle (product_handle): string?`; `ProductPricePointId (product_price_point_id): int?` / `ProductPricePointHandle (product_price_point_handle): string?`; `IncludeTrial (include_trial): bool? = false`; `IncludeInitialCharge (include_initial_charge): bool? = false`; `IncludeCoupons (include_coupons): bool? = true`; `PreservePeriod (preserve_period): bool? = false`; `Proration (proration): Proration?` (`PreservePeriod (preserve_period): bool?`); `ProrationDate (proration_date): DateTimeOffset?` | `SubscriptionMigrationPreviewResponse` — `Migration (migration): SubscriptionMigrationPreview !req`: `ProratedAdjustmentInCents (prorated_adjustment_in_cents): long?`, `ChargeInCents (charge_in_cents): long?`, `PaymentDueInCents (payment_due_in_cents): long?`, `CreditAppliedInCents (credit_applied_in_cents): long?` | **Case A** `SdkException<PreviewSubscriptionProductMigrationError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | none |
| `MigrateSubscriptionProduct` | `MigrateSubscriptionProduct(int subscriptionId, SubscriptionProductMigrationRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | envelope: `Migration (migration): SubscriptionProductMigration !req` (`records-4-Su-We.md`): `ProductId (product_id): int?` **or** `ProductHandle (product_handle): string?`; `ProductPricePointId` / `ProductPricePointHandle`; `IncludeTrial (include_trial): bool? = false`; `IncludeInitialCharge (include_initial_charge): bool? = false`; `IncludeCoupons (include_coupons): bool? = true`; `PreservePeriod (preserve_period): bool? = false`; `Proration (proration): Proration?`. Subscriptions should be `active` or `trialing`. Migrating to the **current** product is a common 422 | `SubscriptionResponse` → `.Subscription` (updated product/state) | **Case A** `SdkException<MigrateSubscriptionProductError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback]. 422 may include 3DS `action_link` — **UNVERIFIED** live shape; extract best-effort | none |
| `UpdateSubscription` (delayed change) | `UpdateSubscription(int subscriptionId, UpdateSubscriptionRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | envelope: `Subscription (subscription): UpdateSubscription !req` (`records-4-Su-We.md`): `ProductHandle (product_handle): string?` / `ProductId (product_id): int?`; `ProductChangeDelayed (product_change_delayed): bool?` (**true** = next renewal, no proration); `ProductPricePointId (product_price_point_id): int?` / `ProductPricePointHandle`; `NextProductId (next_product_id): string?` (empty string cancels a delayed change); `NextBillingAt (next_billing_at): DateTimeOffset?`; `PaymentCollectionMethod (payment_collection_method): string?` (wire string, not `CollectionMethod` enum); `Reference (reference): string?`; `ReceivesInvoiceEmails (receives_invoice_emails): bool?` | `SubscriptionResponse` → `.Subscription` | **Case A** `SdkException<UpdateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | none |

Immediate prorated upgrade/downgrade = `MigrateSubscriptionProduct`. Next-period (no proration) = `UpdateSubscription` with `product_change_delayed: true`.

#### Step 9 — Coupons (`operations/Coupons.md` · `client.Coupons`; apply via `client.Subscriptions`)

| Op | Signature | Request | Response envelope | Error | Pagination |
|---|---|---|---|---|---|
| `ValidateCoupon` | `ValidateCoupon(string code, int? productFamilyId, CancellationToken ct = default)` — `productFamilyId` **must pass explicitly** (`null` if default family) | query `code`←`code`, `product_family_id`←`productFamilyId` | `CouponResponse` — `Coupon (coupon): Coupon?`. Valid → 200 | **Case A** `SdkException<ValidateCouponError>`: `TryGetSingleStringErrorResponse1(out SingleStringErrorResponse1)` [404] · `TryGetRawError` [fallback]. `SingleStringErrorResponse1`: `Errors (errors): string?` (`records-3-Of-Su.md`) — not-found / invalid / expired | none |
| `FindCoupon` | `FindCoupon(int? productFamilyId, string? code, bool? currencyPrices, CancellationToken ct = default)` — all three **must pass explicitly** | query `product_family_id`, `code`, `currency_prices` | `CouponResponse` → `.Coupon` | **Case B** `SdkException<RawError>` (404 if missing) | none |
| `ApplyCouponsToSubscription` | `ApplyCouponsToSubscription(int subscriptionId, string? code, AddCouponsRequest? body, CancellationToken ct = default)` — `code` and `body` **must pass explicitly** | **Prefer body**: `AddCouponsRequest.Codes (codes): IReadOnlyList<string>?` — **adds** to existing codes. Query `code`←`code` is deprecated and **replaces** all existing codes. Pass `code: null` when using body | `SubscriptionResponse` → `.Subscription` | **Case A** `SdkException<ApplyCouponsToSubscriptionError>`: `TryGetSubscriptionAddCouponError1(out SubscriptionAddCouponError1)` [422] · `TryGetRawError` [fallback]. Payload (`records-3-Of-Su.md`): `Codes (codes): IReadOnlyList<string>?`, `CouponCode (coupon_code): IReadOnlyList<string>?`, `CouponCodes (coupon_codes): IReadOnlyList<string>?`, `Subscription (subscription): IReadOnlyList<string>?` | none |
| `RemoveCouponFromSubscription` | `RemoveCouponFromSubscription(int subscriptionId, string? couponCode, CancellationToken ct = default)` — `couponCode` **must pass explicitly** | query `coupon_code`←`couponCode` | `string` (not an envelope) | **Case A** `SdkException<RemoveCouponFromSubscriptionError>`: `TryGetSubscriptionRemoveCouponErrors1(out SubscriptionRemoveCouponErrors1)` [422] · `TryGetRawError` [fallback]. Payload: `Subscription (subscription): IReadOnlyList<string> !req` | none |

Apply-at-signup: set `CreateSubscription.CouponCode` / `CouponCodes` — do not also call `ApplyCouponsToSubscription` for the same signup.

`Coupon` fields (`records-1-Ac-Cr.md`): `Id (id): int?`, `Name (name): string?`, `Code (code): string?`, `Description (description): string?`, `AmountInCents (amount_in_cents): long?`, `Percentage (percentage): string?`, `DiscountType (discount_type): DiscountType?`, `Recurring (recurring): bool?`, `Stackable (stackable): bool?`, `EndDate (end_date): DateTimeOffset?`, `ProductFamilyId (product_family_id): int?`, `ArchivedAt (archived_at): DateTimeOffset?`.

---

### Enums in scope (`map/models/enums.md` · namespace `MaxioAdvancedBilling.Models.Enums`)

These are `StringEnum<T>` records, **not** C# enums. Write `SubscriptionState.Active`, or `SubscriptionState.FromValue("active")`. Never `SubscriptionState.active`.

| Enum | Members (`CSharpMember (wire)`) |
|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `SubscriptionStateFilter` | `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)` |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` |
| `InvoiceStatus` | `Draft (draft)`, `Open (open)`, `Paid (paid)`, `Pending (pending)`, `Voided (voided)`, `Canceled (canceled)`, `Processing (processing)` |
| `InvoiceDateField` | `CreatedAt (created_at)`, `DueDate (due_date)`, `IssueDate (issue_date)`, `UpdatedAt (updated_at)`, `PaidDate (paid_date)` |
| `InvoiceSortField` | `Status (status)`, `TotalAmount (total_amount)`, `DueAmount (due_amount)`, `CreatedAt (created_at)`, `UpdatedAt (updated_at)`, `IssueDate (issue_date)`, `DueDate (due_date)`, `Number (number)` |
| `Direction` | `Asc (asc)`, `Desc (desc)` |
| `SortingDirection` | `Asc (asc)`, `Desc (desc)` |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` |
| `SubscriptionDateField` | `CurrentPeriodEndsAt (current_period_ends_at)`, `CurrentPeriodStartsAt (current_period_starts_at)`, `CreatedAt (created_at)`, `ActivatedAt (activated_at)`, `CanceledAt (canceled_at)`, `ExpiresAt (expires_at)`, `TrialStartedAt (trial_started_at)`, `TrialEndedAt (trial_ended_at)`, `UpdatedAt (updated_at)` |
| `SubscriptionSort` | `SignupDate (signup_date)`, `PeriodStart (period_start)`, `PeriodEnd (period_end)`, `NextAssessment (next_assessment)`, `UpdatedAt (updated_at)`, `CreatedAt (created_at)`, `TotalPayments (total_payments)`, `Id (id)`, `OpenBalance (open_balance)`, `ExpiresAt (expires_at)` |
| `SubscriptionInclude` | `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)` |
| `SubscriptionListInclude` | `SelfServicePageToken (self_service_page_token)` |
| `ComponentKind` | `MeteredComponent (metered_component)`, `QuantityBasedComponent (quantity_based_component)`, `OnOffComponent (on_off_component)`, `PrepaidUsageComponent (prepaid_usage_component)`, `EventBasedComponent (event_based_component)` |
| `PaymentType` | `CreditCard (credit_card)`, `BankAccount (bank_account)`, `PaypalAccount (paypal_account)`, `ApplePay (apple_pay)` |
| `IntervalUnit` | `Day (day)`, `Month (month)` |
| `DiscountType` | `Amount (amount)`, `Percent (percent)` |
| `ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)` |
| `ListSubscriptionComponentsInclude` | `Subscription (subscription)`, `HistoricUsages (historic_usages)` |
| `ListSubscriptionComponentsSort` | `Id (id)`, `UpdatedAt (updated_at)` |
| `SubscriptionListDateField` | `UpdatedAt (updated_at)` |
| `IncludeNotNull` | `NotNull (not_null)` |
| `CreditType` | `Full (full)`, `Prorated (prorated)`, `None (none)` |
| `CardType` | `Visa (visa)`, `Master (master)`, `Elo (elo)`, `Cabal (cabal)`, `Alelo (alelo)`, `Discover (discover)`, `AmericanExpress (american_express)`, `Naranja (naranja)`, `DinersClub (diners_club)`, `Jcb (jcb)`, `Dankort (dankort)`, `Maestro (maestro)`, `MaestroNoLuhn (maestro_no_luhn)`, `Forbrugsforeningen (forbrugsforeningen)`, `Sodexo (sodexo)`, `Alia (alia)`, `Vr (vr)`, `Unionpay (unionpay)`, `Carnet (carnet)`, `CartesBancaires (cartes_bancaires)`, `Olimpica (olimpica)`, `Creditel (creditel)`, `Confiable (confiable)`, `Synchrony (synchrony)`, `Routex (routex)`, `Mada (mada)`, `BpPlus (bp_plus)`, `Passcard (passcard)`, `Edenred (edenred)`, `Anda (anda)`, `TarjetaD (tarjeta-d)`, `Hipercard (hipercard)`, `Bogus (bogus)`, `Switch (switch)`, `Solo (solo)`, `Laser (laser)` |
| `InvoiceRole` | `Unset (unset)`, `Signup (signup)`, `Renewal (renewal)`, `Usage (usage)`, `Reactivation (reactivation)`, `Proration (proration)`, `Migration (migration)`, `Adhoc (adhoc)`, `Backport (backport)`, `BackportBalanceReconciliation (backport-balance-reconciliation)` |
| `InvoiceConsolidationLevel` | `None (none)`, `Child (child)`, `Parent (parent)` |
| `CancellationMethod` | `MerchantUi (merchant_ui)`, `MerchantApi (merchant_api)`, `Dunning (dunning)`, `BillingPortal (billing_portal)`, `Unknown (unknown)`, `Imported (imported)` |
| `PricePointType` | `Catalog (catalog)`, `Default (default)`, `Custom (custom)` |
| `RecurringScheme` | `DoNotRecur (do_not_recur)`, `RecurIndefinitely (recur_indefinitely)`, `RecurWithDuration (recur_with_duration)` |
| `CompoundingStrategy` | `Compound (compound)`, `FullPrice (full-price)` |
| `ServerEnvironment` | **namespace `MaxioAdvancedBilling.Servers`**, not `.Models.Enums`: `Us (US)`, `Eu (EU)` |

### Unions in scope (`map/models/unions.md`)

| Union | Namespace | Construct | Read |
|---|---|---|---|
| `SubscriptionIdOrReference` | `MaxioAdvancedBilling.Models.AnyOf` | `SubscriptionIdOrReference.Int(int)` / `.String(string)` (implicit from `int`/`string`) | `TryGetInt` / `TryGetString` |
| `ComponentIdModel` | `.AnyOf` | `ComponentIdModel.Int(int)` / `.String(string)` | `TryGetInt` / `TryGetString` |
| `ComponentId1` | `.AnyOf` | `ComponentId1.Int(int)` / `.String(string)` | `TryGetInt` / `TryGetString` |
| `AllocatedQuantity2` / `AllocatedQuantity3` / `Quantity1` | `.AnyOf` | `.Int` / `.String` | `TryGetInt` / `TryGetString` |
| `ExpirationMonth1`/`2`, `ExpirationYear1`/`2` | `.AnyOf` | `.Int` / `.String` | `TryGetInt` / `TryGetString` |
| `PricePointId2` | `.AnyOf` | `.Int` / `.String` | `TryGetInt` / `TryGetString` |
| `SnapDay1` | `.AnyOf` | `.String` / `.Int` | `TryGetString` / `TryGetInt` |
| `NetTerms1` | `.AnyOf` | `.String` / `.Int` | `TryGetString` / `TryGetInt` |
| `PaymentProfile` | `MaxioAdvancedBilling.Models.OneOf` | `PaymentProfile.CreditCardPaymentProfile(CreditCardPaymentProfile)` (etc.) | `TryGetCreditCardPaymentProfile` / `TryGetBankAccountPaymentProfile` / `TryGetPaypalPaymentProfile` / `TryGetApplePayPaymentProfile` |

Do **not** `new` a union.

---

## Trap notes

⚠ Step 1 (client registration) — `HttpClient` ownership, DI lifetime, and whether `AddMaxioAdvancedBillingClient` is the right registration for this host are not visible from the constructor. **MUST load `dotnet-client-initialization`** before writing `new MaxioAdvancedBillingClient` or the DI callback.

⚠ Step 1 (auth) — credentials must be in place before the first call; username/password property names and where to load the key from config are not obvious from `BasicAuthCredentials`. **MUST load `dotnet-authentication`** before wiring `BasicAuth`.

⚠ Step 1 (resilience) — the SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; whether a failed write can be re-sent is also not in the options names. **MUST load `dotnet-configuration-resilience`** before setting `Retry` or `Server`.

⚠ Steps 2–9 (every call) — list/search ops have many optional parameters with **no C# default**; a positional call mis-binds. The cancellation token is `ct:`. **MUST load `dotnet-calling-endpoints`** before the first `client.{Group}.{Op}(…)`.

⚠ Steps 3–9 (models) — envelopes wrap payload (`CustomerResponse.Customer`, `SubscriptionResponse.Subscription`, `ProductResponse.Product`, `UsageResponse.Usage`, `CouponResponse.Coupon`); `ListInvoices`/`ReadInvoice` do **not** use `InvoiceResponse`; unions have no public constructor; enums are `StringEnum<T>`. **MUST load `dotnet-models`** before constructing any request body or mapping a response.

⚠ Step 10 (error boundary) — Case A vs Case B is **per operation** (this sheet); `TryGetRawError` is not a catch-all on typed errors; there are **no** no-throw `…Result` variants. A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. This applies in particular to `CreateCustomer` 422 (`CustomerErrorResponse1.Errors` is generated as the `Errors` record with `per_page`/`price_point`, which may not match a live customer-error body — **UNVERIFIED**). **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 10 (tests) — the test seam is not the controller types. **MUST load `dotnet-testing`** before stubbing the SDK.

---

## REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

- `dotnet-client-initialization` — Step 1: constructing/registering `MaxioAdvancedBillingClient` and `HttpClient` lifetime.
- `dotnet-authentication` — Step 1: Basic credentials on options / DI callback.
- `dotnet-configuration-resilience` — Step 1: `RetryOptions`, timeouts, `{site}` / base URL, list pagination.
- `dotnet-calling-endpoints` — Steps 2–9: named arguments, `ct:`, must-pass-explicitly nullables, async calls.
- `dotnet-models` — Steps 3–9: envelopes, `required`/`init`, `StringEnum<T>`, union factories/`TryGet…`, wire names.
- `dotnet-error-handling` — Step 10: Case A/B catch ladder, `TryGet…` vs `RawError`, and **both** `JsonException` directions (malformed 2xx vs failed `{Operation}Error` construction on non-2xx).
- `dotnet-testing` — tests for the integration layer.

---

## Assumptions & Blockers

**Assumptions**

- Storefront maps eShopOnWeb customer identity onto Maxio `Customer.Reference` (unique). Lookup is `ReadCustomerByReference`; create on Case-B 404.
- Maxio catalog (products, price points, metered components, coupons) already exists in the site. This integration **looks up and subscribes**; it does not create products/components/coupons.
- Product/plan mapping is configuration (handles or ids), not hardcoded from this sheet.
- Payment method: prefer `chargify_token` (Maxio.js / Chargify.js) on `PaymentProfileAttributes` / `CreatePaymentProfile`. Sending `full_number` requires PCI compliance on the storefront.
- `CollectionMethod.Automatic` unless the site is remittance/invoice architecture.
- Environment: `ServerEnvironment.Us` unless the Maxio account is EU-hosted.
- Metered usage is implemented only when a subscribed product has `ComponentKind.MeteredComponent` (or prepaid usage recorded the same `CreateUsage` way). Quantity/on-off allocations are out of scope unless the catalog uses them.
- Promo codes = Maxio coupon `code`. Validate with `ValidateCoupon` (pass `productFamilyId` when the coupon is not on the site’s default family).
- Immediate upgrade/downgrade = `MigrateSubscriptionProduct`; delayed = `UpdateSubscription` + `product_change_delayed: true`.
- 3DS `action_link` on 422 for `CreateSubscription` / `MigrateSubscriptionProduct` is **UNVERIFIED** against live payloads; extract best-effort from `ErrorListResponse1.Errors`, fall back to generic message.

**Blockers**

- None that block coding. Site subdomain, API key, product handles, and (if used) component handles / coupon family id are runtime configuration the implementer must supply — not SDK-contract gaps.
