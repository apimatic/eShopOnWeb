# Maxio Advanced Billing — eShopOnWeb integration plan

Package `AsadAli.AdvancedBilling.Sdk` (map stamp `v1.0.2` / `15db14b`). Root namespace `MaxioAdvancedBilling`. Map pages cited per row.

## Scope & sequence

1. **Package, config, client** — NuGet `AsadAli.AdvancedBilling.Sdk`; site subdomain + API key + `ServerEnvironment` from configuration; register `MaxioAdvancedBillingClient` via `AddMaxioAdvancedBillingClient`. No operations yet.
2. **Integration error boundary** — wrap every SDK call; Case A vs Case B differs per operation (sheet below). Catch `SdkException<{Op}Error>` / `SdkException<RawError>` and `JsonException` (two directions — see trap notes).
3. **Customer sync** — map eShop buyer identity → Maxio customer `reference`. Lookup `ReadCustomerByReference`; on miss `CreateCustomer`; on profile change `UpdateCustomer`. Optional `ReadCustomer` by Maxio id.
4. **Payment profiles** — create from a Chargify.js / Maxio.js one-time token (`chargify_token`); never send raw PAN/CVV. `CreatePaymentProfile` then `ChangeSubscriptionDefaultPaymentProfile` when swapping cards. List/read for the buyer’s wallet UI.
5. **Catalog (plans)** — products live in the Maxio site. `ListProducts` / `ReadProductByHandle` / `ReadProduct` to drive plan picker. Do **not** `CreateProduct` from eShop.
6. **Create subscription** — `CreateSubscription` with existing `customer_id` (or `customer_reference`) + `product_handle` (or `product_id`) + `payment_profile_id` (or tokenized `payment_profile_attributes.chargify_token`). Optional `PreviewSubscription` at checkout. Persist Maxio `subscription.id` against the buyer.
7. **Read / list** — `ReadSubscription`, `ListCustomerSubscriptions`, optional `FindSubscription` by app `reference`.
8. **Cancel / pause / resume** — immediate `CancelSubscription`; end-of-period `InitiateDelayedCancellation` / undo `CancelDelayedCancellation`; hold `PauseSubscription`; leave hold `ResumeSubscription`; canceled → live `ReactivateSubscription`; optional auto-resume date `UpdateAutomaticSubscriptionResumption`.
9. **Plan change** — preview `PreviewSubscriptionProductMigration` then `MigrateSubscriptionProduct` (prorated, `active`/`trialing`). Delayed next-renewal change: `UpdateSubscription` with `product_handle`/`product_id` **and** `product_change_delayed: true`. Cancel delayed change: `next_product_id` empty string.
10. **Invoices** — `ListInvoices` filtered by `subscriptionId`; `ReadInvoice` by `uid` for detail (PDF is Accept-header / `.pdf` URL — not a separate SDK method).
11. **Webhooks** — `EnableWebhooks` + `CreateEndpoint` (full `webhook_subscriptions` list). Inbound POST is **not** an SDK operation: add an ASP.NET endpoint; use `ListWebhooks` / `ReplayWebhooks` for ops. Typical eShop subscriptions: signup/payment/renewal success+failure, `subscription_state_change`, `subscription_product_change`, `invoice_issued`, `expiring_card`, `pending_cancellation_change`.
12. **Usage (only if catalog has metered/prepaid components)** — `CreateUsage` / `ListUsages`. Skip if plans are flat product prices only.
13. **Tests** — stub `HttpClient`; assert envelopes, Case A accessors, Case B status/body, and `JsonException` paths.

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

### Client construction / auth / servers

| Fact | Value | Map |
|---|---|---|
| Package id | `AsadAli.AdvancedBilling.Sdk` | `sdk-map.md` |
| Root namespace | `MaxioAdvancedBilling` | `sdk-map.md` |
| Client | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` — only ctor `(System.Net.Http.HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` |
| Options | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions`: `Environment` (`MaxioAdvancedBilling.Servers.ServerEnvironment`), `Retry` (`MaxioAdvancedBilling.Core.Configuration.RetryOptions`), `Server` (`ServerOptions` — source `ServerOptions.cs` ⇒ `MaxioAdvancedBilling.ServerOptions`), `BasicAuth` (`MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?`) | `sdk-map.md` |
| DI | `services.AddMaxioAdvancedBillingClient(o => { … })` (`MaxioAdvancedBilling.ServiceCollectionExtensions`) | `sdk-map.md` |
| Auth | HTTP Basic only. `options.BasicAuth = new BasicAuthCredentials { Username = "<api_key>", Password = "x" }` — **password is the literal `"x"`** | `sdk-map.md` |
| Environments | `ServerEnvironment.Us` (wire `US`, default) → `https://{site}.chargify.com`; `ServerEnvironment.Eu` (wire `EU`) → `https://{site}.ebilling.maxio.com` | `sdk-map.md` |
| Site / base URL | `{site}` default `subdomain`. Set `options.Server.Production.Us.Site = "your-subdomain"` (and `.Eu.*` if EU). Mock: `options.Server.Production.Us.BaseUrl = "http://localhost:…"`. Ebb (events ingest) is a **second** server group (`https://events.chargify.com/{site}`) — only `SubscriptionComponents` RecordEvent/BulkRecordEvents use it | `sdk-map.md` |
| RetryOptions | Namespace `MaxioAdvancedBilling.Core.Configuration`. All members `required` — full instance or `RetryOptions.Default()`. Members: `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry` | `sdk-map.md` |
| Throw model | Throw-only. No `{Op}Result` / `ApiResult` variants. Case A: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.{Op}Error>` with `TryGet…` + inherited `TryGetRawError`. Case B: `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` (`StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`). `ApiError` + `RawError` live in `MaxioAdvancedBilling.Core.ErrorResponse` | `sdk-map.md` |
| Controller namespaces | Controllers: `MaxioAdvancedBilling.Api`. Records: `MaxioAdvancedBilling.Models`. Enums: `MaxioAdvancedBilling.Models.Enums`. Unions: `MaxioAdvancedBilling.Models.AnyOf` / `.OneOf`. Errors: `MaxioAdvancedBilling.Errors` | `sdk-map.md` |

`RetryOptions` is **not** copied here as “what Timeout bounds” — see trap notes.

---

### Operations

#### Customers — `client.Customers` · `operations/Customers.md` · source `Api/Customers.cs`

| Method | Signature | Request | Response envelope | Error | Pagination |
|---|---|---|---|---|---|
| `CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, **must pass explicitly** | Envelope `CreateCustomerRequest`: `Customer (customer): CreateCustomer !req`. Inner `CreateCustomer`: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `CcEmails (cc_emails): string?`, `Organization (organization): string?`, `Reference (reference): string?`, `Address (address): string?`, `Address2 (address_2): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?` (ISO 3166-1 alpha-2), `Phone (phone): string?`, `Locale (locale): string?`, `VatNumber (vat_number): string?`, `TaxExempt (tax_exempt): bool?`, `TaxExemptReason (tax_exempt_reason): string?`, `ParentId (parent_id): int?`, `SalesforceId (salesforce_id): string?` · `records-1-Ac-Cr.md` | `CustomerResponse` → **`Customer (customer): Customer !req`** | Case A `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` | none |
| `UpdateCustomer` | `UpdateCustomer(int id, UpdateCustomerRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | Envelope `UpdateCustomerRequest`: `Customer (customer): UpdateCustomer !req`. Inner `UpdateCustomer` (all optional): `FirstName (first_name)`, `LastName (last_name)`, `Email (email)`, `CcEmails (cc_emails)`, `Organization (organization)`, `Reference (reference)`, `Address (address)`, `Address2 (address_2)`, `City (city)`, `State (state)`, `Zip (zip)`, `Country (country)`, `Phone (phone)`, `Locale (locale)`, `VatNumber (vat_number)`, `TaxExempt (tax_exempt)`, `TaxExemptReason (tax_exempt_reason)`, `ParentId (parent_id)`, `Verified (verified)`, `SalesforceId (salesforce_id)` — all `string?` except `TaxExempt`/`Verified` `bool?`, `ParentId` `int?` · `records-4-Su-We.md` | `CustomerResponse` → `Customer` | Case A `SdkException<UpdateCustomerError>`: `TryGetNoContent(out RawError)` [404] · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError` | none |
| `ReadCustomer` | `ReadCustomer(int id, CancellationToken ct = default)` | — | `CustomerResponse` → `Customer` | Case B `SdkException<RawError>` | none |
| `ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` query `reference` | — | `CustomerResponse` → `Customer` | Case B `SdkException<RawError>` | none |
| `ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | — | `IReadOnlyList<SubscriptionResponse>` — each item unwraps `.Subscription` | Case B `SdkException<RawError>` | none |
| `ListCustomers` (admin only) | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — 7 leading params **must pass explicitly** (`null` to skip) | query: `direction`, `page`, `per_page`←`perPage`, `date_field`←`dateField`, `start_date`, `end_date`, `start_datetime`, `end_datetime`, `q` | `IReadOnlyList<CustomerResponse>` | Case B `SdkException<RawError>` | manual `page`+`perPage` (default 1/50) |

**`Customer` inner fields the integration reads** (`records-2-Cr-Ne.md`): `Id (id): int?`, `Reference (reference): string?`, `FirstName (first_name)`, `LastName (last_name)`, `Email (email)`, `Organization (organization)`, `Address`/`Address2`/`City`/`State`/`StateName`/`Zip`/`Country`/`CountryName`/`Phone`, `CreatedAt`/`UpdatedAt`, `TaxExempt`, `VatNumber`, `Locale`, `Verified`. Set eShop buyer id on `Reference` (unique per site).

**422 payload:** `CustomerErrorResponse1.Errors (errors): Errors?` where map `Errors` is `PerPage (per_page): IReadOnlyList<string>?`, `PricePoint (price_point): IReadOnlyList<string>?` (`records-2-Cr-Ne.md`). **UNVERIFIED** whether a live 422 body populates that `Errors` shape. Extract best-effort from `e422.Errors` if present; otherwise `TryGetRawError` + `ReadAsString()`. A mismatched 422 body can throw `JsonException` while constructing `CreateCustomerError` (see trap notes).

---

#### Subscriptions — `client.Subscriptions` · `operations/Subscriptions.md` · `Api/Subscriptions.cs`

| Method | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | Envelope `CreateSubscriptionRequest`: `Subscription (subscription): CreateSubscription !req` · `records-2-Cr-Ne.md`. Identify product: `ProductHandle (product_handle): string?` **or** `ProductId (product_id): int?`. Price point: `ProductPricePointHandle` / `ProductPricePointId`. Identify customer: `CustomerId (customer_id): int?` **or** `CustomerReference (customer_reference): string?` **or** nested `CustomerAttributes (customer_attributes): CustomerAttributes?`. Payment: `PaymentProfileId (payment_profile_id): int?` **or** `PaymentProfileAttributes (payment_profile_attributes)` / `CreditCardAttributes` / `BankAccountAttributes`. Also used: `Reference (reference): string?` (subscription-level), `CouponCode` / `CouponCodes`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `Components`, `Metafields`, `Currency`, `NextBillingAt`, `DeferSignup` default `false`, `AgreementAcceptance` (required when using Maxio Payments). Prefer existing customer + `payment_profile_id` or `payment_profile_attributes.ChargifyToken` — do not set `FullNumber`/`Cvv` unless PCI-compliant | `SubscriptionResponse` → **`Subscription (subscription): Subscription?`** (nullable inner — null-check) · `records-4-Su-We.md` | Case A `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError`. 422 `ErrorListResponse1.Errors (errors): IReadOnlyList<string> !req` · `records-2-Cr-Ne.md`. 3DS can 422 with an `action_link` (may not match `ErrorListResponse1` — **UNVERIFIED**; fall back to `ReadAsString()`) | none |
| `PreviewSubscription` | `PreviewSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | same `CreateSubscriptionRequest` as create (card not required for tax preview) | `SubscriptionPreviewResponse` (not unwrapped here unless checkout shows preview amounts) | Case B `SdkException<RawError>` | none |
| `ReadSubscription` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` **must pass explicitly** (`null` to skip). query `include` | `SubscriptionInclude.Coupons` / `SelfServicePageToken` | `SubscriptionResponse` → `Subscription?` | Case B `SdkException<RawError>` | none |
| `FindSubscription` | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` **must pass explicitly**. query `reference` | — | `SubscriptionResponse` → `Subscription?` | Case A `SdkException<FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError` | none |
| `UpdateSubscription` | `UpdateSubscription(int subscriptionId, UpdateSubscriptionRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | Envelope `UpdateSubscriptionRequest`: `Subscription (subscription): UpdateSubscription !req` · `records-4-Su-We.md`. Delayed plan change: `ProductHandle`/`ProductId` + `ProductChangeDelayed (product_change_delayed): bool?`. Cancel delayed: `NextProductId (next_product_id): string?` empty string. Also `ProductPricePointId`/`ProductPricePointHandle`, `NextBillingAt`, `Reference`, `CreditCardAttributes`, `PaymentCollectionMethod (payment_collection_method): string?` (wire string, **not** `CollectionMethod` enum on this model) | `SubscriptionResponse` → `Subscription?` | Case A `SdkException<UpdateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` | none |
| `ListSubscriptions` (admin) | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 14 leading params **must pass explicitly** | query: `state`, `product`, `product_price_point_id`, `coupon`, `coupon_code`, `date_field`, `start_date`, `end_date`, `start_datetime`, `end_datetime`, `metadata`, `direction`, `sort`, `include`, `page`, `per_page` | `IReadOnlyList<SubscriptionResponse>` | Case B `SdkException<RawError>` | manual `page`+`perPage` (default 1/20) |

**`CreateSubscription` fields actually set in eShop signup** (`records-2-Cr-Ne.md`): `ProductHandle` or `ProductId`; `ProductPricePointHandle` if not default; `CustomerId` or `CustomerReference`; `PaymentProfileId` or `PaymentProfileAttributes.ChargifyToken (chargify_token)`; `Reference` = eShop subscription/order id; `CouponCode` if cart has one; `PaymentCollectionMethod` = `CollectionMethod.Automatic` for card.

**`Subscription` fields the integration reads** (`records-3-Of-Su.md`): `Id (id): int?`, `State (state): SubscriptionState?`, `BalanceInCents`, `ProductPriceInCents`, `CurrentPeriodEndsAt`, `CurrentPeriodStartedAt`, `NextAssessmentAt`, `CanceledAt`, `CancelAtEndOfPeriod`, `DelayedCancelAt`, `OnHoldAt`, `AutomaticallyResumeAt`, `Reference`, `Customer (customer): Customer?`, `Product (product): Product?`, `CreditCard`, `ProductPricePointId`, `NextProductId`/`NextProductHandle`, `PaymentCollectionMethod`, `CouponCode`/`CouponCodes`.

---

#### Subscription status — `client.SubscriptionStatus` · `operations/SubscriptionStatus.md` · `Api/SubscriptionStatus.cs`

| Method | Signature | Request | Response | Error |
|---|---|---|---|---|
| `CancelSubscription` | `CancelSubscription(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** (`null` = immediate cancel) | Envelope `CancellationRequest`: `Subscription (subscription): CancellationOptions !req` · `records-1-Ac-Cr.md`. `CancellationOptions`: `CancellationMessage (cancellation_message): string?`, `ReasonCode (reason_code): string?`, `CancelAtEndOfPeriod (cancel_at_end_of_period): bool?`, `ScheduledCancellationAt (scheduled_cancellation_at): DateTimeOffset?`, `RefundPrepaymentAccountBalance (refund_prepayment_account_balance): bool?` | `SubscriptionResponse` → `Subscription?` | Case A `SdkException<CancelSubscriptionApiError>`: `TryGetNoContent(out RawError)` [404] · `TryGetCancelSubscriptionErrorResponse(out CancelSubscriptionErrorResponse)` [422] · `TryGetRawError`. **`CancelSubscriptionErrorResponse` is a union** (`unions.md`): factories `ErrorListResponse1` / `SingleErrorResponse1`; readers `TryGetErrorListResponse1` / `TryGetSingleErrorResponse1`. `SingleErrorResponse1.Error (error): string !req` |
| `InitiateDelayedCancellation` | `InitiateDelayedCancellation(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | same `CancellationRequest` | `DelayedCancellationResponse`: `Message (message): string?` · `records-2-Cr-Ne.md` | Case A `SdkException<InitiateDelayedCancellationError>`: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `CancelDelayedCancellation` | `CancelDelayedCancellation(int subscriptionId, CancellationToken ct = default)` | — | `DelayedCancellationResponse` | Case A `SdkException<CancelDelayedCancellationError>`: `TryGetNoContent` [404] · `TryGetRawError` |
| `PauseSubscription` | `PauseSubscription(int subscriptionId, PauseRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly**. Cannot hold if `next_billing_at` is within 24 hours (API rule) | `PauseRequest`: `Hold (hold): AutoResume?`. `AutoResume`: `AutomaticallyResumeAt (automatically_resume_at): DateTimeOffset?` · `records-3-Of-Su.md` / `records-1-Ac-Cr.md` | `SubscriptionResponse` → `Subscription?` | Case A `SdkException<PauseSubscriptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `ResumeSubscription` | `ResumeSubscription(int subscriptionId, ResumptionCharge? calendarBillingResumptionCharge, CancellationToken ct = default)` — `calendarBillingResumptionCharge` **must pass explicitly** (`null` if not calendar-billing). query `calendar_billing['resumption_charge']` | enum `ResumptionCharge` | `SubscriptionResponse` → `Subscription?` | Case A `SdkException<ResumeSubscriptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `ReactivateSubscription` | `ReactivateSubscription(int subscriptionId, ReactivateSubscriptionRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly**. Works for `canceled` / `trial_ended` / `unpaid` | `ReactivateSubscriptionRequest`: `CalendarBilling (calendar_billing): ReactivationBilling?`, `IncludeTrial (include_trial): bool?`, `PreserveBalance (preserve_balance): bool?`, `CouponCode (coupon_code): string?`, `UseCreditsAndPrepayments (use_credits_and_prepayments): bool?`, `Resume (resume): Resume?` **(union)** · `records-3-Of-Su.md`. `Resume` union (`unions.md`): `Resume.Bool(bool)` / `Resume.ResumeOptions(ResumeOptions)`; `TryGetBool` / `TryGetResumeOptions`. `ResumeOptions`: `RequireResume`, `ForgiveBalance`. `ReactivationBilling.ReactivationCharge` default `ReactivationCharge.Prorated` | `SubscriptionResponse` → `Subscription?` | Case A `SdkException<ReactivateSubscriptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `UpdateAutomaticSubscriptionResumption` | `UpdateAutomaticSubscriptionResumption(int subscriptionId, PauseRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly**. Set `automatically_resume_at` null to clear | same `PauseRequest` | `SubscriptionResponse` → `Subscription?` | Case A `SdkException<UpdateAutomaticSubscriptionResumptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` |

Pause = on-hold (`POST …/hold.json`). Resume from hold = `ResumeSubscription`. “Resume” a **canceled** sub = `ReactivateSubscription` with `Resume` union — different operation.

---

#### Plan change — `client.SubscriptionProducts` · `operations/SubscriptionProducts.md` · `Api/SubscriptionProducts.cs`

| Method | Signature | Request | Response | Error |
|---|---|---|---|---|
| `PreviewSubscriptionProductMigration` | `PreviewSubscriptionProductMigration(int subscriptionId, SubscriptionMigrationPreviewRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | Envelope: `Migration (migration): SubscriptionMigrationPreviewOptions !req` · `records-4-Su-We.md`. Options: `ProductId` / `ProductHandle`, `ProductPricePointId` / `ProductPricePointHandle`, `IncludeTrial` default `false`, `IncludeInitialCharge` default `false`, `IncludeCoupons` default `true`, `PreservePeriod` default `false`, `Proration (proration): Proration?` (`PreservePeriod (preserve_period): bool?` · `records-3-Of-Su.md`), `ProrationDate (proration_date): DateTimeOffset?` | Envelope `SubscriptionMigrationPreviewResponse` → **`Migration (migration): SubscriptionMigrationPreview !req`**: `ProratedAdjustmentInCents`, `ChargeInCents`, `PaymentDueInCents`, `CreditAppliedInCents` (all `long?`) | Case A `SdkException<PreviewSubscriptionProductMigrationError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `MigrateSubscriptionProduct` | `MigrateSubscriptionProduct(int subscriptionId, SubscriptionProductMigrationRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly**. Sub must be `active` or `trialing`. Migrating to the **current** product commonly 422s | Envelope: `Migration (migration): SubscriptionProductMigration !req`. Fields: `ProductId` / `ProductHandle`, `ProductPricePointId` / `ProductPricePointHandle`, `IncludeTrial` default `false`, `IncludeInitialCharge` default `false`, `IncludeCoupons` default `true`, `PreservePeriod` default `false`, `Proration` | `SubscriptionResponse` → `Subscription?` | Case A `SdkException<MigrateSubscriptionProductError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError`. 3DS 422 **UNVERIFIED** vs `ErrorListResponse1` — fall back to raw string |

Delayed (non-prorated) plan change uses `UpdateSubscription`, not this controller.

---

#### Products / plans — `client.Products` · `operations/Products.md` · `Api/Products.cs`

| Method | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `ListProducts` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 leading params **must pass explicitly** | `ListProductsFilter`: `Ids (ids): IReadOnlyList<int>?`, `PrepaidProductPricePoint`, `UseSiteExchangeRate` · `records-2-Cr-Ne.md`. query: `date_field`, `filter`, `end_date`, `end_datetime`, `start_date`, `start_datetime`, `page`, `per_page`, `include_archived`, `include` | `IReadOnlyList<ProductResponse>` → each **`Product (product): Product !req`** | Case B `SdkException<RawError>` | manual `page`+`perPage` (default 1/20) |
| `ReadProduct` | `ReadProduct(int productId, CancellationToken ct = default)` | — | `ProductResponse` → `Product` | Case B `SdkException<RawError>` | none |
| `ReadProductByHandle` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | path `api_handle` | `ProductResponse` → `Product` | Case B `SdkException<RawError>` | none |

**`Product` fields read** (`records-3-Of-Su.md`): `Id`, `Name`, `Handle`, `Description`, `PriceInCents`, `Interval`, `IntervalUnit`, `TrialPriceInCents`, `TrialInterval`, `TrialIntervalUnit`, `RequireCreditCard`, `ProductPricePointId`, `ProductPricePointHandle`, `ProductPricePointName`, `ArchivedAt`, `ProductFamily`.

Do not call `CreateProduct` / `UpdateProduct` / `ArchiveProduct` from eShop (catalog is site-configured).

---

#### Invoices — `client.Invoices` · `operations/Invoices.md` · `Api/Invoices.cs`

| Method | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `ListInvoices` | `ListInvoices(string? startDate, string? endDate, InvoiceStatus? status, int? subscriptionId, string? subscriptionGroupUid, string? consolidationLevel, Direction? direction, InvoiceDateField? dateField, string? startDatetime, string? endDatetime, IReadOnlyList<int>? customerIds, IReadOnlyList<string>? number, IReadOnlyList<int>? productIds, InvoiceSortField? sort, int? page = 1, int? perPage = 20, bool? lineItems = false, bool? discounts = false, bool? taxes = false, bool? credits = false, bool? payments = false, bool? customFields = false, bool? refunds = false, CancellationToken ct = default)` — 14 leading params **must pass explicitly** | eShop: `subscriptionId:` buyer’s Maxio sub id; set `lineItems`/`payments`/`taxes` `true` only when the UI needs breakdowns (default false = totals only). query `subscription_id`←`subscriptionId`, `status`, `page`, `per_page`, `line_items`, … | **`ListInvoicesResponse`**: `Invoices (invoices): IReadOnlyList<Invoice> !req` — **not** `InvoiceResponse`, **not** a list of envelopes · `records-2-Cr-Ne.md` | Case B `SdkException<RawError>` | manual `page`+`perPage` (default 1/20) |
| `ReadInvoice` | `ReadInvoice(string uid, CancellationToken ct = default)` | path invoice **uid** (string, not int) | **`Invoice` directly** (no `InvoiceResponse` wrapper) | Case B `SdkException<RawError>` | none |

**`Invoice` fields read** (`records-2-Cr-Ne.md`): `Uid (uid): string?`, `Number (number)`, `SubscriptionId (subscription_id): int?`, `CustomerId`, `Status (status): InvoiceStatus?`, `IssueDate`, `DueDate`, `PaidDate`, `TotalAmount (total_amount): string?`, `DueAmount`, `PaidAmount`, `SubtotalAmount`, `TaxAmount`, `DiscountAmount`, `CreditAmount`, `Currency`, `PublicUrl`, `LineItems`, `Payments`. Amounts are **strings**, not cents.

`CreateInvoice` returns `InvoiceResponse` (wrapped) — out of scope unless eShop issues ad-hoc invoices.

---

#### Payment profiles — `client.PaymentProfiles` · `operations/PaymentProfiles.md` · `Api/PaymentProfiles.cs`

| Method | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `CreatePaymentProfile` | `CreatePaymentProfile(CreatePaymentProfileRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly**. New profile is **not** auto-default on existing subs | Envelope: `PaymentProfile (payment_profile): CreatePaymentProfile !req` · `records-1-Ac-Cr.md`. Set `CustomerId (customer_id): int?` + `ChargifyToken (chargify_token): string?`. Optional billing address fields. Leave `FullNumber`/`Cvv` unset | `PaymentProfileResponse` → **`PaymentProfile (payment_profile): PaymentProfile !req` (OneOf union)** | Case A `SdkException<CreatePaymentProfileError>`: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `ListPaymentProfiles` | `ListPaymentProfiles(int? customerId, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — `customerId` **must pass explicitly**. Empty list **not** 404 | query `customer_id`, `page`, `per_page` | `IReadOnlyList<PaymentProfileResponse>` | Case B `SdkException<RawError>` | manual `page`+`perPage` (default 1/20) |
| `ReadPaymentProfile` | `ReadPaymentProfile(int paymentProfileId, CancellationToken ct = default)` | — | `PaymentProfileResponse` → `PaymentProfile` union | Case A `SdkException<ReadPaymentProfileError>`: `TryGetNoContent` [404] · `TryGetRawError` | none |
| `UpdatePaymentProfile` | `UpdatePaymentProfile(int paymentProfileId, UpdatePaymentProfileRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | Envelope: `PaymentProfile (payment_profile): UpdatePaymentProfile !req` — billing/contact; `FullNumber` present on model but changing card number requires a **new** profile · `records-4-Su-We.md` | `PaymentProfileResponse` | Case A `SdkException<UpdatePaymentProfileError>`: `TryGetNoContent` [404] · `TryGetErrorStringMapResponse1(out ErrorStringMapResponse1)` [422] · `TryGetRawError`. `ErrorStringMapResponse1.Errors (errors): IReadOnlyDictionary<string, string>?` | none |
| `ChangeSubscriptionDefaultPaymentProfile` | `ChangeSubscriptionDefaultPaymentProfile(int subscriptionId, int paymentProfileId, CancellationToken ct = default)` | — | `PaymentProfileResponse` | Case A `SdkException<ChangeSubscriptionDefaultPaymentProfileError>`: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |

**`PaymentProfile` union** (`unions.md`, `MaxioAdvancedBilling.Models.OneOf`): factories `ApplePayPaymentProfile` / `BankAccountPaymentProfile` / `CreditCardPaymentProfile` / `PaypalPaymentProfile`; readers `TryGetApplePayPaymentProfile` / `TryGetBankAccountPaymentProfile` / `TryGetCreditCardPaymentProfile` / `TryGetPaypalPaymentProfile`. Card display: `CreditCardPaymentProfile.Id`, `MaskedCardNumber`, `CardType`, `ExpirationMonth`/`Year`, `CustomerId` · `records-2-Cr-Ne.md`.

---

#### Webhooks (outbound config + delivery log) — `client.Webhooks` · `operations/Webhooks.md` · `Api/Webhooks.cs`

Inbound Maxio→eShop HTTP is **not** an SDK call. Use this controller to register the eShop URL and inspect deliveries.

| Method | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `EnableWebhooks` | `EnableWebhooks(EnableWebhooksRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | `WebhooksEnabled (webhooks_enabled): bool !req` · `records-2-Cr-Ne.md` | `EnableWebhooksResponse`: `WebhooksEnabled (webhooks_enabled): bool?` | Case B `SdkException<RawError>` | none |
| `CreateEndpoint` | `CreateEndpoint(CreateOrUpdateEndpointRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | Envelope: `Endpoint (endpoint): CreateOrUpdateEndpoint !req`. Inner: `Url (url): string !req`, `WebhookSubscriptions (webhook_subscriptions): IReadOnlyList<WebhookSubscription> !req` · `records-1-Ac-Cr.md` | `EndpointResponse` → `Endpoint (endpoint): Endpoint?` (`Id`, `Url`, `SiteId`, `Status`, `WebhookSubscriptions` as `IReadOnlyList<string>?`) · `records-2-Cr-Ne.md` | Case A `SdkException<CreateEndpointError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `UpdateEndpoint` | `UpdateEndpoint(int endpointId, CreateOrUpdateEndpointRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly**. **Always send the complete** `webhook_subscriptions` list (empty list unsubscribes all) | same as create | `EndpointResponse` | Case A `SdkException<UpdateEndpointError>`: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `ListEndpoints` | `ListEndpoints(CancellationToken ct = default)` | — | `IReadOnlyList<Endpoint>` (not wrapped) | Case B `SdkException<RawError>` | none |
| `ListWebhooks` | `ListWebhooks(WebhookStatus? status, string? sinceDate, string? untilDate, WebhookOrder? order, int? subscription, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 5 leading **must pass explicitly** | query `status`, `since_date`, `until_date`, `order`, `subscription`, `page`, `per_page` | `IReadOnlyList<WebhookResponse>` → `Webhook (webhook): Webhook?` | Case B `SdkException<RawError>` | manual `page`+`perPage` |
| `ReplayWebhooks` | `ReplayWebhooks(ReplayWebhooksRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly**. Up to 1000 ids; queued, not immediate | `Ids (ids): IReadOnlyList<long> !req` | `ReplayWebhooksResponse`: `Status (status): string?` | Case B `SdkException<RawError>` | none |

**`Webhook` delivery record** (`records-4-Su-We.md`): `Event (event): string?`, `Id (id): long?`, `Body (body): string?`, `Signature (signature): string?`, `SignatureHmacSha256 (signature_hmac_sha_256): string?`, `Successful`, `LastError`, `LastSentUrl`, timestamps. **UNVERIFIED** that the inbound POST body matches this `Webhook` record — inbound handler should parse event `key` + payload defensively (string/JSON), verify HMAC with the site webhook signing key from configuration, and fall back to a generic reject on parse failure.

**Typical eShop `WebhookSubscription` list** (enum values below): `SignupSuccess`, `SignupFailure`, `PaymentSuccess`, `PaymentFailure`, `RenewalSuccess`, `RenewalFailure`, `SubscriptionStateChange`, `SubscriptionProductChange`, `InvoiceIssued`, `ExpiringCard`, `PendingCancellationChange`.

---

#### Site events (optional admin) — `client.Events` · `operations/Events.md` · `Api/Events.cs`

| Method | Signature | Response | Error | Pagination |
|---|---|---|---|---|
| `ListSubscriptionEvents` | `ListSubscriptionEvents(int subscriptionId, long? sinceId, long? maxId, Direction? direction, IReadOnlyList<EventKey>? filter, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 4 after id **must pass explicitly** | `IReadOnlyList<EventResponse>` → `Event (event): Event !req` | Case B `SdkException<RawError>` | manual `page`+`perPage` |

**`Event`** (`records-2-Cr-Ne.md`): `Id (id): long !req`, `Key (key): EventKey !req`, `Message (message): string !req`, `SubscriptionId (subscription_id): int? !req`, `CustomerId (customer_id): int? !req`, `CreatedAt (created_at): DateTimeOffset !req`, `EventSpecificData (event_specific_data): EventSpecificData? !req` **(union, required-but-nullable)**. Read via `EventSpecificData.TryGetSubscriptionStateChange` / `TryGetSubscriptionProductChange` / `TryGetPaymentRelatedEvents` / `TryGetInvoiceIssued` / … (`unions.md`).

---

#### Usage (metered/prepaid only) — `client.SubscriptionComponents` · `operations/SubscriptionComponents.md` · `Api/SubscriptionComponents.cs`

Skip unless a plan includes a metered or prepaid component.

| Method | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `CreateUsage` | `CreateUsage(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, CreateUsageRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | Path unions (`unions.md`, `MaxioAdvancedBilling.Models.AnyOf`): `SubscriptionIdOrReference.Int(int)` / `.String(string)`; `ComponentIdModel.Int(int)` / `.String(string)` (handle form `"handle:…"`, per operation notes). Envelope: `Usage (usage): CreateUsage !req`. `CreateUsage`: `Quantity (quantity): double?`, `PricePointId (price_point_id): string?`, `Memo (memo): string?`, `BillingSchedule`, `CustomPrice` · `records-2-Cr-Ne.md` | `UsageResponse` → `Usage (usage): Usage !req`: `Id`, `Quantity` (union `Quantity1`: `TryGetInt`/`TryGetString`), `Memo`, `ComponentId`, `SubscriptionId`, `CreatedAt` · `records-4-Su-We.md` | Case A `SdkException<CreateUsageError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `ListUsages` | `ListUsages(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, long? sinceId, long? maxId, DateTimeOffset? sinceDate, DateTimeOffset? untilDate, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 4 after path **must pass explicitly** | query `since_id`, `max_id`, `since_date`, `until_date`, `page`, `per_page` | `IReadOnlyList<UsageResponse>` | Case B `SdkException<RawError>` | manual `page`+`perPage` |

`RecordEvent` / `BulkRecordEvents` hit the **Ebb** server group — out of scope unless eShop streams events-based billing.

---

### Enums in scope (`map/models/enums.md` — `StringEnum<T>`, **not** C# enums)

Write `Type.Member` (e.g. `CollectionMethod.Automatic`) or `Type.FromValue("wire")`. Namespace `MaxioAdvancedBilling.Models.Enums`.

| Enum | Members (C# (wire)) |
|---|---|
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` |
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `SubscriptionStateFilter` | `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)` |
| `InvoiceStatus` | `Draft (draft)`, `Open (open)`, `Paid (paid)`, `Pending (pending)`, `Voided (voided)`, `Canceled (canceled)`, `Processing (processing)` |
| `InvoiceDateField` | `CreatedAt (created_at)`, `DueDate (due_date)`, `IssueDate (issue_date)`, `UpdatedAt (updated_at)`, `PaidDate (paid_date)` |
| `InvoiceSortField` | `Status (status)`, `TotalAmount (total_amount)`, `DueAmount (due_amount)`, `CreatedAt (created_at)`, `UpdatedAt (updated_at)`, `IssueDate (issue_date)`, `DueDate (due_date)`, `Number (number)` |
| `Direction` | `Asc (asc)`, `Desc (desc)` |
| `SortingDirection` | `Asc (asc)`, `Desc (desc)` |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` |
| `SubscriptionInclude` | `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)` |
| `SubscriptionListInclude` | `SelfServicePageToken (self_service_page_token)` |
| `ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)` |
| `IntervalUnit` | `Day (day)`, `Month (month)` |
| `ResumptionCharge` | `Prorated (prorated)`, `Immediate (immediate)`, `Delayed (delayed)` |
| `ReactivationCharge` | `Prorated (prorated)`, `Immediate (immediate)`, `Delayed (delayed)` |
| `CancellationMethod` | `MerchantUi (merchant_ui)`, `MerchantApi (merchant_api)`, `Dunning (dunning)`, `BillingPortal (billing_portal)`, `Unknown (unknown)`, `Imported (imported)` |
| `PaymentType` | `CreditCard (credit_card)`, `BankAccount (bank_account)`, `PaypalAccount (paypal_account)`, `ApplePay (apple_pay)` |
| `CardType` | `Visa (visa)`, `Master (master)`, `Elo (elo)`, `Cabal (cabal)`, `Alelo (alelo)`, `Discover (discover)`, `AmericanExpress (american_express)`, `Naranja (naranja)`, `DinersClub (diners_club)`, `Jcb (jcb)`, `Dankort (dankort)`, `Maestro (maestro)`, `MaestroNoLuhn (maestro_no_luhn)`, `Forbrugsforeningen (forbrugsforeningen)`, `Sodexo (sodexo)`, `Alia (alia)`, `Vr (vr)`, `Unionpay (unionpay)`, `Carnet (carnet)`, `CartesBancaires (cartes_bancaires)`, `Olimpica (olimpica)`, `Creditel (creditel)`, `Confiable (confiable)`, `Synchrony (synchrony)`, `Routex (routex)`, `Mada (mada)`, `BpPlus (bp_plus)`, `Passcard (passcard)`, `Edenred (edenred)`, `Anda (anda)`, `TarjetaD (tarjeta-d)`, `Hipercard (hipercard)`, `Bogus (bogus)`, `Switch (switch)`, `Solo (solo)`, `Laser (laser)` |
| `WebhookStatus` | `Successful (successful)`, `Failed (failed)`, `Pending (pending)`, `Paused (paused)` |
| `WebhookOrder` | `NewestFirst (newest_first)`, `OldestFirst (oldest_first)` |
| `WebhookSubscription` | `BillingDateChange (billing_date_change)`, `ComponentAllocationChange (component_allocation_change)`, `ChjsTokenizationFailure (chjs_tokenization_failure)`, `ChjsTokenizationSuccess (chjs_tokenization_success)`, `CustomerCreate (customer_create)`, `CustomerUpdate (customer_update)`, `DunningStepReached (dunning_step_reached)`, `ExpiringCard (expiring_card)`, `ExpirationDateChange (expiration_date_change)`, `InvoiceIssued (invoice_issued)`, `InvoicePending (invoice_pending)`, `MeteredUsage (metered_usage)`, `PaymentFailure (payment_failure)`, `PaymentSuccess (payment_success)`, `DirectDebitPaymentPending (direct_debit_payment_pending)`, `DirectDebitPaymentPaidOut (direct_debit_payment_paid_out)`, `DirectDebitPaymentRejected (direct_debit_payment_rejected)`, `PrepaidSubscriptionBalanceChanged (prepaid_subscription_balance_changed)`, `PrepaidUsage (prepaid_usage)`, `RefundFailure (refund_failure)`, `RefundSuccess (refund_success)`, `RenewalFailure (renewal_failure)`, `RenewalSuccess (renewal_success)`, `SignupFailure (signup_failure)`, `SignupSuccess (signup_success)`, `StatementClosed (statement_closed)`, `StatementSettled (statement_settled)`, `SubscriptionCardUpdate (subscription_card_update)`, `SubscriptionGroupCardUpdate (subscription_group_card_update)`, `SubscriptionProductChange (subscription_product_change)`, `SubscriptionStateChange (subscription_state_change)`, `TrialEndNotice (trial_end_notice)`, `UpcomingRenewalNotice (upcoming_renewal_notice)`, `UpgradeDowngradeFailure (upgrade_downgrade_failure)`, `UpgradeDowngradeSuccess (upgrade_downgrade_success)`, `PendingCancellationChange (pending_cancellation_change)`, `SubscriptionPrepaymentAccountBalanceChanged (subscription_prepayment_account_balance_changed)`, `SubscriptionServiceCreditAccountBalanceChanged (subscription_service_credit_account_balance_changed)` |
| `EventKey` | `PaymentSuccess (payment_success)`, `PaymentFailure (payment_failure)`, `SignupSuccess (signup_success)`, `SignupFailure (signup_failure)`, `DelayedSignupCreationSuccess (delayed_signup_creation_success)`, `DelayedSignupCreationFailure (delayed_signup_creation_failure)`, `BillingDateChange (billing_date_change)`, `ExpirationDateChange (expiration_date_change)`, `RenewalSuccess (renewal_success)`, `RenewalFailure (renewal_failure)`, `SubscriptionStateChange (subscription_state_change)`, `SubscriptionProductChange (subscription_product_change)`, `PendingCancellationChange (pending_cancellation_change)`, `ExpiringCard (expiring_card)`, `CustomerUpdate (customer_update)`, `CustomerCreate (customer_create)`, `CustomerDelete (customer_delete)`, `ComponentAllocationChange (component_allocation_change)`, `MeteredUsage (metered_usage)`, `PrepaidUsage (prepaid_usage)`, `UpgradeDowngradeSuccess (upgrade_downgrade_success)`, `UpgradeDowngradeFailure (upgrade_downgrade_failure)`, `StatementClosed (statement_closed)`, `StatementSettled (statement_settled)`, `SubscriptionCardUpdate (subscription_card_update)`, `SubscriptionGroupCardUpdate (subscription_group_card_update)`, `SubscriptionBankAccountUpdate (subscription_bank_account_update)`, `RefundSuccess (refund_success)`, `RefundFailure (refund_failure)`, `UpcomingRenewalNotice (upcoming_renewal_notice)`, `TrialEndNotice (trial_end_notice)`, `DunningStepReached (dunning_step_reached)`, `InvoiceIssued (invoice_issued)`, `InvoicePending (invoice_pending)`, `PrepaidSubscriptionBalanceChanged (prepaid_subscription_balance_changed)`, `SubscriptionGroupSignupSuccess (subscription_group_signup_success)`, `SubscriptionGroupSignupFailure (subscription_group_signup_failure)`, `DirectDebitPaymentPaidOut (direct_debit_payment_paid_out)`, `DirectDebitPaymentRejected (direct_debit_payment_rejected)`, `DirectDebitPaymentPending (direct_debit_payment_pending)`, `PendingPaymentCreated (pending_payment_created)`, `PendingPaymentFailed (pending_payment_failed)`, `PendingPaymentCompleted (pending_payment_completed)`, `ProformaInvoiceIssued (proforma_invoice_issued)`, `SubscriptionPrepaymentAccountBalanceChanged (subscription_prepayment_account_balance_changed)`, `SubscriptionServiceCreditAccountBalanceChanged (subscription_service_credit_account_balance_changed)`, `CustomFieldValueChange (custom_field_value_change)`, `ItemPricePointChanged (item_price_point_changed)`, `RenewalSuccessRecreated (renewal_success_recreated)`, `RenewalFailureRecreated (renewal_failure_recreated)`, `PaymentSuccessRecreated (payment_success_recreated)`, `PaymentFailureRecreated (payment_failure_recreated)`, `SubscriptionDeletion (subscription_deletion)`, `SubscriptionGroupBankAccountUpdate (subscription_group_bank_account_update)`, `SubscriptionPaypalAccountUpdate (subscription_paypal_account_update)`, `SubscriptionGroupPaypalAccountUpdate (subscription_group_paypal_account_update)`, `SubscriptionCustomerChange (subscription_customer_change)`, `AccountTransactionChanged (account_transaction_changed)`, `GoCardlessPaymentPaidOut (go_cardless_payment_paid_out)`, `GoCardlessPaymentRejected (go_cardless_payment_rejected)`, `GoCardlessPaymentPending (go_cardless_payment_pending)`, `StripeDirectDebitPaymentPaidOut (stripe_direct_debit_payment_paid_out)`, `StripeDirectDebitPaymentRejected (stripe_direct_debit_payment_rejected)`, `StripeDirectDebitPaymentPending (stripe_direct_debit_payment_pending)`, `MaxioPaymentsDirectDebitPaymentPaidOut (maxio_payments_direct_debit_payment_paid_out)`, `MaxioPaymentsDirectDebitPaymentRejected (maxio_payments_direct_debit_payment_rejected)`, `MaxioPaymentsDirectDebitPaymentPending (maxio_payments_direct_debit_payment_pending)`, `InvoiceInCollectionsCanceled (invoice_in_collections_canceled)`, `SubscriptionAddedToGroup (subscription_added_to_group)`, `SubscriptionRemovedFromGroup (subscription_removed_from_group)`, `ChargebackOpened (chargeback_opened)`, `ChargebackLost (chargeback_lost)`, `ChargebackAccepted (chargeback_accepted)`, `ChargebackClosed (chargeback_closed)`, `ChargebackWon (chargeback_won)`, `PaymentCollectionMethodChanged (payment_collection_method_changed)`, `ComponentBillingDateChanged (component_billing_date_changed)`, `ChjsTokenizationFailure (chjs_tokenization_failure)`, `ChjsTokenizationSuccess (chjs_tokenization_success)`, `SubscriptionTermRenewalScheduled (subscription_term_renewal_scheduled)`, `SubscriptionTermRenewalPending (subscription_term_renewal_pending)`, `SubscriptionTermRenewalActivated (subscription_term_renewal_activated)`, `SubscriptionTermRenewalRemoved (subscription_term_renewal_removed)` |

---

## Trap notes

⚠ Step 1 (client registration) — `HttpClient` ownership/lifetime and whether the SDK client wrapper is registered the same way as the handler pipeline are **not** implied by the ctor. **MUST load `dotnet-client-initialization`** before DI.

⚠ Step 1 (auth) — the options property name, when credentials must be set relative to construction, and loading the key from configuration (not source) are not visible from `BasicAuthCredentials` alone. **MUST load `dotnet-authentication`** before wiring Basic auth (username = API key, password literal `"x"`).

⚠ Step 1 (server / retries) — `Retry` / `Timeout` on `RetryOptions` do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; which verbs retry on status vs transport, and whether a failed write can be re-sent, are not in the options property names. Site `{site}` vs `BaseUrl` override and which environment’s `Server.*` node is actually read are also not on the type. **MUST load `dotnet-configuration-resilience`** before registering or tuning the client.

⚠ Steps 3–12 (every call) — list/search ops have many nullable params **without C# defaults**; a positional call mis-binds. Cancellation token argument is `ct:`. Response envelopes wrap (`CustomerResponse.Customer`, `SubscriptionResponse.Subscription` nullable, `ProductResponse.Product`) **except** `ReadInvoice` (`Invoice`) and `ListInvoices` (`ListInvoicesResponse.Invoices`). **MUST load `dotnet-calling-endpoints`** before the first `client.{Group}.{Op}` call.

⚠ Steps 3–12 (models) — enums are `StringEnum<T>` (static members / `FromValue`), not C# enums. `PaymentProfile`, `Resume`, `CancelSubscriptionErrorResponse`, `SubscriptionIdOrReference`, `ComponentIdModel`, `EventSpecificData` are unions (factories + `TryGet…`, no `new`). Unmodeled JSON is dropped on deserialize. `Invoice` amounts are strings. **MUST load `dotnet-models`** before building payloads or mapping responses.

⚠ Step 2 (error boundary) — this sheet mixes Case A typed `{Op}Error` and Case B `RawError`; `TryGetRawError` is not a catch-all on Case B (`RawError` has no `TryGet*`). There are **no** no-throw Result variants. Catch the concrete `SdkException<T>` per operation. **MUST load `dotnet-error-handling`** before any try/catch.

⚠ Step 2 (error boundary) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 2 (error boundary) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 13 (tests) — the test seam is the `HttpClient` ctor argument; match eShopOnWeb’s existing test framework/assertions; do not mock SDK internals. **MUST load `dotnet-testing`** before writing integration tests.

⚠ Step 4 (PCI) — `CreatePaymentProfile` / `CreateSubscription` models contain `FullNumber`/`Cvv`. Collecting raw cards in production requires PCI compliance; use Maxio.js `chargify_token` instead (operation notes on `PaymentProfiles` / `Subscriptions`).

⚠ Step 8 vs 9 — `PauseSubscription`/`ResumeSubscription` are hold/unhold. Reactivating a **canceled** sub is `ReactivateSubscription` (optional `Resume` union). Delayed product change is `UpdateSubscription` (`product_change_delayed`); prorated change is `MigrateSubscriptionProduct`. Mixing these calls compiles and still does the wrong billing action.

---

## REQUIRED READING

Load these **before implementation starts**. This sheet does not carry their contents.

- `dotnet-client-initialization` — Step 1 client construction and DI (`AddMaxioAdvancedBillingClient` / `HttpClient` lifetime).
- `dotnet-authentication` — Step 1 Basic credentials on `options.BasicAuth`.
- `dotnet-configuration-resilience` — Step 1 retries, timeouts, `{site}` / `BaseUrl`, list pagination.
- `dotnet-calling-endpoints` — Steps 3–12 every operation call (named args, `ct:`, envelopes).
- `dotnet-models` — Steps 3–12 records, `StringEnum<T>`, unions (`PaymentProfile`, `Resume`, path AnyOfs).
- `dotnet-error-handling` — Step 2 boundary (Case A/B, `JsonException` from 2xx **and** from failed error-object construction). Always required.
- `dotnet-testing` — Step 13 `HttpClient` stub seam.

---

## Assumptions & Blockers

**Assumptions**

- eShop buyer identity (ASP.NET Identity user id / buyer id) is stored as Maxio `Customer.Reference` (unique per site) and used with `ReadCustomerByReference` before create.
- Membership/plan SKUs already exist in the Maxio site as Products with stable `handle`s; eShop does not create/update/archive products via the API.
- Card collection uses Maxio.js / Chargify.js one-time tokens (`chargify_token`), not raw PAN, unless the merchant is PCI-compliant (out of default scope).
- Default collection method is `CollectionMethod.Automatic`. Remittance/invoice-only sites would set `Remittance` instead.
- Usage/metered components are **out of the default eShop catalog path**; `CreateUsage` is implemented only if a product family actually has metered/prepaid components.
- Webhooks are part of a typical eShop Maxio integration (keep local subscription/entitlement state in sync on payment failure, renewal, cancel, plan change). Inbound POST handling is application HTTP, not `client.Webhooks.*`.
- US vs EU hosting is a configuration switch (`ServerEnvironment.Us` default); site subdomain comes from config (`Server.Production.{Env}.Site`).

**Blockers**

- None from the SDK map. Implementation still needs live config values the map cannot supply: API key, site subdomain, US/EU, product handles for each eShop plan, webhook public URL, and webhook signing secret (inbound verify — **UNVERIFIED** against the `Webhook` list-delivery model).
- 3DS `action_link` on 422 for create/migrate/payment **UNVERIFIED** vs generated `ErrorListResponse1`; boundary must extract best-effort then fall back to `ReadAsString()` / `JsonException` handling.
- `CustomerErrorResponse1.Errors` map shape (`PerPage`/`PricePoint`) **UNVERIFIED** vs live 422 customer bodies; same defensive extract + raw fallback.

---

## CONTRACT ADDENDUM (narrow lookup)

Sources: `operations/ProductFamilies.md`, `operations/Customers.md`, `operations/Subscriptions.md`, `records-2-Cr-Ne.md`, `records-3-Of-Su.md`, `enums.md`, `sdk-map.md`; plus named source files `Api/ProductFamilies.cs`, `Api/Customers.cs`, `Api/Subscriptions.cs`, `ServiceCollectionExtensions.cs`, `Servers/ServerEnvironment.cs`, `ServerOptions.cs`, `Servers/ProductionOptions.cs`, `Servers/EbbOptions.cs`, `Core/Enum/StringEnum.cs`, `Core/Enum/TypedEnum.cs`, `Models/Enums/IntervalUnit.cs`, `Models/Enums/SubscriptionState.cs`, `Models/CreateSubscription.cs`, `Models/EnableWebhooksRequest.cs`, `Models/ProductFamily.cs`, `Models/Customer.cs`, `Models/Subscription.cs`, `Errors/FindSubscriptionError.cs`.

### 1. ProductFamilies — `client.ProductFamilies` · `MaxioAdvancedBilling.Api.ProductFamilies`

No dedicated “lookup family by handle” method. `ReadProductFamily` is **numeric id only** (`int id`). Remarks in `Api/ProductFamilies.cs` claim `handle:my-family` also works, but that string cannot be passed to `int id` — generated notes and signature **disagree**. For handle, use `ListProductsForProductFamily` (path is `string`) or `ListProductFamilies` and match `ProductFamily.Handle`.

| Method | Signature | Envelope | Error |
|---|---|---|---|
| `ListProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — 5 leading **must pass explicitly** | `IReadOnlyList<ProductFamilyResponse>` → `ProductFamily (product_family): ProductFamily?` | Case B `SdkException<RawError>` |
| `ReadProductFamily` | `ReadProductFamily(int id, CancellationToken ct = default)` | `ProductFamilyResponse` → `ProductFamily?` | Case B `SdkException<RawError>` |
| `ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 after path **must pass explicitly** | `IReadOnlyList<ProductResponse>` → each `Product (product): Product !req` | Case A `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError` |
| `CreateProductFamily` | out of eShop catalog-read scope | `ProductFamilyResponse` | Case A `SdkException<CreateProductFamilyError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` |

### 2. `Product.ProductFamily`

- Type: `MaxioAdvancedBilling.Models.ProductFamily?` (namespace `MaxioAdvancedBilling.Models`) · wire `product_family`
- Members: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `AccountingCode (accounting_code): string?`, `Description (description): string?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, `ArchivedAt (archived_at): DateTimeOffset?`
- Envelope `ProductFamilyResponse.ProductFamily` is **nullable** (`ProductFamily?`), unlike `ProductResponse.Product` (`Product !req`)

### 3. `AddMaxioAdvancedBillingClient` lifetime

Registers **`AddSingleton`** for `MaxioAdvancedBillingClient` (`ServiceCollectionExtensions.cs`):

```csharp
services.AddHttpClient();
services.AddSingleton(sp =>
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient();
    return new MaxioAdvancedBillingClient(httpClient, options);
});
```

`MaxioAdvancedBilling.ServiceCollectionExtensions.AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>? configure = null)`.

### 4. `ServerEnvironment`

- Namespace: `MaxioAdvancedBilling.Servers` · `record ServerEnvironment : StringEnum<ServerEnvironment>` (`Servers/ServerEnvironment.cs`)
- Public static members: `Us` (wire `"US"`, default), `Eu` (wire `"EU"`), `Default()` → `Us`
- Constructor is **private**. **`FromValue` is not declared** (unlike `IntervalUnit` / `SubscriptionState`)
- Inherited from `MaxioAdvancedBilling.Core.Enum.StringEnum<T>`: `TryGetKnownValue(string value, out TEnum? result)` — `KnownValues` for string enums uses **ordinal ignore-case** keys (`TypedEnum.cs`)
- Map a config string: `ServerEnvironment.TryGetKnownValue(s, out var env)` — `"US"` / `"Us"` / `"us"` → `Us`; `"EU"` / `"Eu"` / `"eu"` → `Eu`; unknown → `false` (do not call a non-existent `FromValue`)
- Read wire: `env.Value` (`string`) or `env.ToString()`

### 5. `Site` / `BaseUrl` paths

| Path | Site / BaseUrl types |
|---|---|
| `options.Server` | `MaxioAdvancedBilling.ServerOptions` (`ServerOptions.cs`, root namespace) |
| `options.Server.Production` | `MaxioAdvancedBilling.Servers.ProductionOptions` |
| `options.Server.Production.Us` | nested `MaxioAdvancedBilling.Servers.ProductionOptions.UsOptions` — `Site: string` default `"subdomain"`, `BaseUrl: string` default `"https://{site}.chargify.com"` |
| `options.Server.Production.Eu` | nested `MaxioAdvancedBilling.Servers.ProductionOptions.EuOptions` — `Site` default `"subdomain"`, `BaseUrl` default `"https://{site}.ebilling.maxio.com"` |
| `options.Environment` | `MaxioAdvancedBilling.Servers.ServerEnvironment` — pick `Us` or `Eu` so the matching nested node is the one `Resolve` reads |
| `options.Server.Ebb.Us` / `.Eu` | `MaxioAdvancedBilling.Servers.EbbOptions` + nested `EbbOptions.UsOptions` / `EuOptions` (events ingest only) |

Set US: `options.Environment = ServerEnvironment.Us;` then `options.Server.Production.Us.Site = "your-subdomain";` and optionally `options.Server.Production.Us.BaseUrl`. Set EU on `.Production.Eu.*` with `Environment = ServerEnvironment.Eu`.

### 6. `CreateSubscription` without payment

`CreateSubscription` inner fields are **all nullable** (no `!req` except envelope `CreateSubscriptionRequest.Subscription`). `PaymentProfileId` / `PaymentProfileAttributes` / `CreditCardAttributes` / `BankAccountAttributes` may be omitted (`null`). XML: payment “**may be required** … depending on the options for the Product” (`Api/Subscriptions.cs`). Identify **product** (`product_id` or `product_handle`) and **customer** (`customer_id` or `customer_reference` or nested `customer_attributes`). `AgreementAcceptance` is C# optional; XML on that record says required when using Maxio Payments. If the product’s `RequireCreditCard` is true, a missing profile is a **live 422**, not a compile error — catch `TryGetErrorListResponse1`.

### 7. Lookup misses

| Op | Case | Miss |
|---|---|---|
| `ReadCustomerByReference` | **B** `SdkException<RawError>` (`operations/Customers.md`, `Api/Customers.cs` uses `RawErrorResponse`) | **UNVERIFIED** that miss is HTTP 404 — Case B does **not** special-case 404. Read `ex.Error.StatusCode`. Treat `HttpStatusCode.NotFound` as miss **if** that is what arrives; any other status is also `RawError` |
| `FindSubscription` | **A** `SdkException<FindSubscriptionError>` | Confirmed: `TryGetNoContent(out RawError)` **[404]** (`operations/Subscriptions.md`, `Errors/FindSubscriptionError.cs` maps `404 => AsNoContent`) |

### 8. Subscription `Reference` uniqueness

Map + `CreateSubscription.Reference` XML: “The reference value (provided by your app) for the subscription itself.” **No uniqueness constraint** is stated (contrast **customer** `reference`: unique per site — `operations/Customers.md`). Duplicate-create status/body is **UNVERIFIED**. If the site rejects it, expect Case A `SdkException<CreateSubscriptionError>` → `TryGetErrorListResponse1(out ErrorListResponse1)` [422] (`Errors (errors): IReadOnlyList<string> !req`); if it allows duplicates, `FindSubscription` still returns a single match. Idempotency: `FindSubscription(reference: …)` first; do not assume 422.

### 9. `ListProductsForProductFamily` identity

`productFamilyId` is **`string`**. XML (`Api/ProductFamilies.cs`): “Either the product family's id or its handle prefixed with `handle:`”. Numeric id as string (`"123"`) **or** `"handle:my-family"`. Not handle-without-prefix.

### 10. `ProductResponse` / `Product`

Envelope: `ProductResponse.Product (product): Product !req`. Fields: `Handle (handle): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?` (`MaxioAdvancedBilling.Models.Enums.IntervalUnit`). Members: `Day (day)`, `Month (month)`. `public static IntervalUnit FromValue(string value)`. Read wire: `product.IntervalUnit?.Value` (`string`) — `ToString()` and implicit `string` conversion also yield `Value` (`TypedEnum.cs`).

### 11. `Customer.Id` after create/read

Declared `Id (id): int?` (`records-2-Cr-Ne.md`, `Models/Customer.cs` — “The customer ID in Chargify”, still nullable). Successful `CreateCustomer`/`ReadCustomer*` return `CustomerResponse` with `Customer !req`, but **`Id` itself is nullable in the generated model**. Do not dereference without a null check. **UNVERIFIED** that a live 2xx always populates `id` — if missing, that is a malformed 2xx → `JsonException` only if the member were `required` (it is not). Persist only after `if (resp.Customer.Id is int id)`.

### 12. `Subscription` fields + `SubscriptionState` → string

| Member | CLR | Wire |
|---|---|---|
| `Id` | `int?` | `id` |
| `State` | `MaxioAdvancedBilling.Models.Enums.SubscriptionState?` | `state` |
| `Product` | `MaxioAdvancedBilling.Models.Product?` | `product` |
| `CurrentPeriodEndsAt` | `DateTimeOffset?` | `current_period_ends_at` |
| `ProductPriceInCents` | `long?` | `product_price_in_cents` |

Envelope: `SubscriptionResponse.Subscription (subscription): Subscription?` (nullable wrapper). `SubscriptionState` has public `FromValue(string)`. Wire string for your API: `sub.State?.Value` (e.g. `"active"`) — **not** the C# member name `Active`. `ToString()` == `Value`. Implicit conversion to `string` also uses `Value`.

### 13. `EnableWebhooksRequest`

**No inner wrapper.** `MaxioAdvancedBilling.Models.EnableWebhooksRequest` has `WebhooksEnabled (webhooks_enabled): bool !req` directly on the request record (`records-2-Cr-Ne.md`, `Models/EnableWebhooksRequest.cs`). Pass `new EnableWebhooksRequest { WebhooksEnabled = true }` as `body`.

