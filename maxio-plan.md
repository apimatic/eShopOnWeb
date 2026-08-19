# Maxio Advanced Billing — eShopOnWeb subscription billing plan

Package: `AsadAli.AdvancedBilling.Sdk` **version `1.0.2`** (map stamp `v1.0.2` / `15db14b`). Root namespace: `MaxioAdvancedBilling`. Pin: `dotnet add package AsadAli.AdvancedBilling.Sdk --version 1.0.2`.

**Hero flow (required PublicApi JWT endpoints — implement these; do not require payment, cancel, invoices, or plan-change):**

| Endpoint | SDK operations |
|---|---|
| `GET /api/subscription-plans` | `ProductFamilies.ListProductsForProductFamily` with `productFamilyId: "handle:" + Maxio:ProductFamilyHandle` (never numeric family id) |
| `POST /api/subscriptions` | Idempotent: `Customers.ReadCustomerByReference` → 404 then `CreateCustomer`; `Customers.ListCustomerSubscriptions` match by `Product.Handle`; if none live, `Subscriptions.CreateSubscription` **without** payment fields |
| `GET /api/my-subscriptions` | `Customers.ReadCustomerByReference`; on 404 return empty list; else `Customers.ListCustomerSubscriptions` |

Sandbox catalog **handles are stable; numeric IDs are not** — never key off ids. Family handle `eshop-subscribe` is the *value of* `Maxio:ProductFamilyHandle` in sandbox, not a literal to hard-code. Plan handles returned by list-plans: `eshop-pro`, `basic-plan`. Both plans: no trial, no setup fee, expire never, **`RequireCreditCard` is false** — subscribe works with no card / no 3-DS / no `chargify_token`.

Config (bind from `Maxio:` / env; never hard-code values): `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, optional `Maxio:BaseUrl` (verbatim API base), env `MAXIO_ENVIRONMENT` (2 chars, e.g. `US`/`EU`).

Rows below for payment profiles, coupons, cancel, invoices, plan-change, and `Products.ListProducts` remain as optional reference only — **out of the required endpoint set**.

---

## 1. Scope & sequence

| Step | What to implement | Operations |
|---|---|---|
| 1 | Add NuGet `AsadAli.AdvancedBilling.Sdk` **1.0.2**; bind `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, optional `Maxio:BaseUrl`, `MAXIO_ENVIRONMENT` | client construction only |
| 2 | Register `MaxioAdvancedBillingClient` in ASP.NET Core DI (`AddMaxioAdvancedBillingClient` or ctor + `IHttpClientFactory`) | — |
| 3 | `GET /api/subscription-plans` — list products for the configured family **handle** | `ProductFamilies.ListProductsForProductFamily` |
| 4 | Find-or-create Maxio customer (`reference` = eShop user id). Idempotent on double-click | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer` |
| 5 | `POST /api/subscriptions` — if a live sub already exists for that `Product.Handle`, return it; else create **without payment fields** | `Customers.ListCustomerSubscriptions`, `Subscriptions.CreateSubscription` |
| 6 | `GET /api/my-subscriptions` — list caller’s subs (empty if no Maxio customer) | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |
| 7 | Exception-translation boundary around every SDK call | per-operation Case A/B below |
| 8 | Tests against the `HttpClient` constructor seam | — |

Payment-token capture, cancel, invoices, plan-change, coupons, and components are **not** part of this hero set. Existing contract rows for those ops stay below for reference only.

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

Namespaces (add a `using` per kind — child namespaces are **not** imported transitively):

| Kind | Namespace |
|---|---|
| Client, options, DI | `MaxioAdvancedBilling` |
| Controllers (`client.X`) | `MaxioAdvancedBilling.Api` |
| Records | `MaxioAdvancedBilling.Models` |
| Enums (`StringEnum<T>`, not C# enums) | `MaxioAdvancedBilling.Models.Enums` |
| Unions | `MaxioAdvancedBilling.Models.AnyOf` · `MaxioAdvancedBilling.Models.OneOf` |
| Typed `{Op}Error` | `MaxioAdvancedBilling.Errors` |
| `SdkException<T>` | `MaxioAdvancedBilling.Core.Exceptions` |
| `ApiError`, `RawError` | `MaxioAdvancedBilling.Core.ErrorResponse` |
| `BasicAuthCredentials` | `MaxioAdvancedBilling.Core.Authentication.Basic` |
| `ServerEnvironment`, `ProductionOptions` | `MaxioAdvancedBilling.Servers` |
| `ServerOptions` (client options `.Server`) | `MaxioAdvancedBilling` (root — `ServerOptions.cs`) |
| `RetryOptions` | `MaxioAdvancedBilling.Core.Configuration` |

Every operation is **throw-only** (no `…Result` / `ApiResult` variants). Catch `SdkException<TError>`. Case A: `TError` is `{Op}Error : ApiError` with `TryGet…` + inherited `TryGetRawError(out RawError)`. Case B: `TError` is `RawError` — read `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` directly (no `TryGet*`).

---

### 2.1 Client construction / auth / environments

**Client** (`MaxioAdvancedBillingClient.cs`): only ctor is `MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`.

**DI** (`ServiceCollectionExtensions.cs`): `services.AddMaxioAdvancedBillingClient(o => { … });`

**`MaxioAdvancedBillingClientOptions`** (`MaxioAdvancedBillingClientOptions.cs`):

| Property | Type |
|---|---|
| `Environment` | `MaxioAdvancedBilling.Servers.ServerEnvironment` |
| `Retry` | `MaxioAdvancedBilling.Core.Configuration.RetryOptions` |
| `Server` | `MaxioAdvancedBilling.ServerOptions` (root namespace) |
| `BasicAuth` | `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?` |

**Auth — Basic only.** `options.BasicAuth = new BasicAuthCredentials { Username = apiKeyFromConfig, Password = "x" }`. Username = `Maxio:ApiKey`; Password = the **literal** string `"x"` (not the subdomain, not empty). (`sdk-map.md`, `MaxioAdvancedBillingClientOptions.cs`, `Core/Authentication/Basic/BasicAuthCredentials.cs`)

**`ServerEnvironment`** (`Servers/ServerEnvironment.cs`) — `StringEnum<ServerEnvironment>` in `MaxioAdvancedBilling.Servers`, **not** `Models.Enums`. Public API: static members + `TryGetKnownValue` (comparison is ordinal-ignore-case). There is **no** public `FromValue` on this type (`FromValueCore` is protected).

| Member | Wire (`Value`) | Default Production `BaseUrl` |
|---|---|---|
| `ServerEnvironment.Us` *(default via `Default()`)* | `US` | `https://{site}.chargify.com` |
| `ServerEnvironment.Eu` | `EU` | `https://{site}.ebilling.maxio.com` |

Map env `MAXIO_ENVIRONMENT` (2 chars) with `ServerEnvironment.TryGetKnownValue(raw, out var env)` — `"US"` → `Us`, `"EU"` → `Eu`. If it returns false, reject configuration. Then `options.Environment = env`.

**Site + BaseUrl** (`Servers/ProductionOptions.cs`; nested `UsOptions` / `EuOptions`): always set **Site** on the selected environment from `Maxio:Subdomain`:

- `Us` → `options.Server.Production.Us.Site = subdomain`
- `Eu` → `options.Server.Production.Eu.Site = subdomain`

`Resolve` always passes `Site` as template param `{site}`. It is interpolated **only if** the active `BaseUrl` contains `{site}`.

**`Maxio:BaseUrl` optional override:** when set, assign that string **verbatim** to the selected environment’s Production base URL — do not concatenate the subdomain into it:

- `Us` → `options.Server.Production.Us.BaseUrl = maxioBaseUrl`
- `Eu` → `options.Server.Production.Eu.BaseUrl = maxioBaseUrl`

Still set `Site` as above. If the override has no `{site}` token, `Site` does not affect the request URL. When `Maxio:BaseUrl` is omitted, leave the default template and let `{site}` + `Site` produce `https://{subdomain}.chargify.com` (US) or `https://{subdomain}.ebilling.maxio.com` (EU). Ebb group unused. (`sdk-map.md` Servers & auth)

**`RetryOptions`** (`Core/Configuration/RetryOptions.cs`) — all members `required`; build a full instance or start from `RetryOptions.Default()`:

| Member | Type |
|---|---|
| `StatusCodesToRetry` | `IReadOnlyList<HttpStatusCode>` |
| `HttpMethodsToRetry` | `IReadOnlyList<HttpMethod>` |
| `MaxRetries` | `int` |
| `Delay` | `TimeSpan` |
| `Timeout` | `TimeSpan?` |
| `BackOffFactor` | `int` |
| `UseExponentialBackoff` | `bool` |
| `MaxJitter` | `TimeSpan` |
| `OnRetry` | `Action<RetryAttempt>?` |

---

### 2.2 Operations

#### Customers — `client.Customers` · `operations/Customers.md` · `Api/Customers.cs`

| Op | Signature | Envelope / return | Error | Pagination |
|---|---|---|---|---|
| **CreateCustomer** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, **must pass explicitly** | `CustomerResponse` → `.Customer` (`Customer !req`) | **A** `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` | none |
| **ReadCustomerByReference** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — query `reference` | `CustomerResponse` → `.Customer` | **B** `SdkException<RawError>` | none |
| **ReadCustomer** | `ReadCustomer(int id, CancellationToken ct = default)` | `CustomerResponse` → `.Customer` | **B** `SdkException<RawError>` | none |
| **ListCustomerSubscriptions** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | `IReadOnlyList<SubscriptionResponse>` — each `.Subscription` | **B** `SdkException<RawError>` | none |
| **ListCustomers** | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — 7 params `direction`…`q` **must pass explicitly** (`null` to skip) | `IReadOnlyList<CustomerResponse>` | **B** `SdkException<RawError>` | `page`+`perPage` |
| **UpdateCustomer** | `UpdateCustomer(int id, UpdateCustomerRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | `CustomerResponse` → `.Customer` | **A** `SdkException<UpdateCustomerError>`: `TryGetNoContent(out RawError)` [404] · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError` | none |

**Find-or-create (idempotent):** `ReadCustomerByReference(reference: eShopUserId)` — query wire `reference`. On **Case B** `SdkException<RawError>` with `ex.Error.StatusCode == HttpStatusCode.NotFound` → `CreateCustomer` with `Reference (reference)` = that same user id (plus required `FirstName`/`LastName`/`Email`). Exact match by reference is this lookup, not `ListCustomers(q:)`. Reference must be unique site-wide.

**Duplicate `CreateCustomer` (same `reference` already exists):** HTTP **422**. Catch **Case A** `SdkException<CreateCustomerError>` → `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422]. Then **re-call** `ReadCustomerByReference` and use that customer (double-click / race). Do not create a second customer. **UNVERIFIED** live 422 body vs generated `CustomerErrorResponse1.Errors` (`PerPage`/`PricePoint`); extract best-effort, else `TryGetRawError` → `ReadAsString()`.

**Already enrolled (same product handle):** after the customer exists, `ListCustomerSubscriptions(customerId: customer.Id)`. Match `Subscription.Product.Handle` to the requested plan handle (`eshop-pro` / `basic-plan` from the client — **never** `Product.Id`). Treat as already enrolled and **return that subscription** (do not `CreateSubscription`) when `Subscription.State` is any of: `Active`, `Trialing`, `Assessing`, `SoftFailure`, `PastDue`, `Suspended`, `Unpaid`, `Paused`, `OnHold`, `AwaitingSignup`, `Pending`. **Allow a new signup** when every match for that handle is `Canceled`, `Expired`, `TrialEnded`, or `FailedToCreate` (or there is no match). `ListCustomerSubscriptions` has no pagination (full list). If two POSTs race past this check, catch `CreateSubscription` **422** `TryGetErrorListResponse1`, re-list, and return the live sub if present.

**`GET /api/my-subscriptions`:** `ReadCustomerByReference`; on Case B 404 return `[]` (do not call list). Else `ListCustomerSubscriptions`. Return handle/name/price/`State`/`NextAssessmentAt`.

**`CreateCustomerRequest`** (`records-1-Ac-Cr.md`): `Customer (customer): CreateCustomer !req`

**`CreateCustomer`** required: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`. Optional: `CcEmails (cc_emails): string?`, `Organization (organization): string?`, `Reference (reference): string?`, `Address (address): string?`, `Address2 (address_2): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `Locale (locale): string?`, `VatNumber (vat_number): string?`, `TaxExempt (tax_exempt): bool?`, `TaxExemptReason (tax_exempt_reason): string?`, `ParentId (parent_id): int?`, `SalesforceId (salesforce_id): string?`

**`UpdateCustomerRequest`**: `Customer (customer): UpdateCustomer !req` — same fields as create except all optional; plus `Verified (verified): bool?`. (`records-4-Su-We.md`)

**`CustomerResponse`**: `Customer (customer): Customer !req` (`records-2-Cr-Ne.md`)

**`Customer` (read):** `Id (id): int?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?`, `Organization (organization): string?`, `Address (address): string?`, `Address2 (address_2): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?`, `Phone (phone): string?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, plus portal/tax/locale fields. Integration stores `Id` + `Reference`.

**`CustomerErrorResponse1`** (`records-2-Cr-Ne.md`): `Errors (errors): Errors?`. Generated `Errors` members are `PerPage (per_page): IReadOnlyList<string>?`, `PricePoint (price_point): IReadOnlyList<string>?`. **UNVERIFIED** whether a live 422 customer-validation body matches this shape. Defensive: if `TryGetCustomerErrorResponse1` yields no usable messages, `TryGetRawError` → `ReadAsString()` and fall back to a generic rejection message. Do not parse `ex.ToString()`.

---

#### Subscriptions — `client.Subscriptions` · `operations/Subscriptions.md` · `Api/Subscriptions.cs`

| Op | Signature | Envelope / return | Error | Pagination |
|---|---|---|---|---|
| **CreateSubscription** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | `SubscriptionResponse` → `.Subscription` (`Subscription?`) | **A** `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` | none |
| **ReadSubscription** | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` **must pass explicitly** (`null` to skip). Query `include` | `SubscriptionResponse` → `.Subscription` | **B** `SdkException<RawError>` | none |
| **FindSubscription** | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` **must pass explicitly**. Query `reference` | `SubscriptionResponse` | **A** `SdkException<FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError` | none |
| **ListSubscriptions** | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 14 params `state`…`include` **must pass explicitly** | `IReadOnlyList<SubscriptionResponse>` | **B** `SdkException<RawError>` | `page`+`perPage` (default 20) |
| **UpdateSubscription** | `UpdateSubscription(int subscriptionId, UpdateSubscriptionRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | `SubscriptionResponse` | **A** `SdkException<UpdateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` | none |
| **PreviewSubscription** | `PreviewSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | `SubscriptionPreviewResponse` → `.SubscriptionPreview` (`SubscriptionPreview !req`) | **B** `SdkException<RawError>` | none |
| **ApplyCouponsToSubscription** | `ApplyCouponsToSubscription(int subscriptionId, string? code, AddCouponsRequest? body, CancellationToken ct = default)` — `code` and `body` **must pass explicitly**. Prefer `body.Codes`; query `code` replaces existing codes (deprecated) | `SubscriptionResponse` | **A** `SdkException<ApplyCouponsToSubscriptionError>`: `TryGetSubscriptionAddCouponError1(out SubscriptionAddCouponError1)` [422] · `TryGetRawError` | none |

**Hero `CreateSubscription` WITHOUT a card** (`operations/Subscriptions.md`, `records-2-Cr-Ne.md`, `enums.md` `CollectionMethod`, `Models/CreateSubscription.cs`, `Models/Enums/CollectionMethod.cs`): sandbox plans do **not** require a card (`Product.RequireCreditCard` is false), but omitting `PaymentCollectionMethod` leaves the site default — **`automatic`** — which immediately collects the first invoice and 422s with `TryGetErrorListResponse1`: *"No payment method was on file for the $299.00 balance"*.

Set these three fields:

- `ProductHandle (product_handle): string?` — the plan handle (`eshop-pro` / `basic-plan`). **Do not** set `ProductId`.
- `CustomerId (customer_id): int?` — from find-or-create.
- `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` = **`CollectionMethod.Remittance`** (wire `remittance`). Current Relationship Invoicing valid values are `remittance`, `automatic`, `prepaid`. **`Remittance` is the no-card path** (invoice the subscriber; do not capture a payment method). Do **not** use `CollectionMethod.Automatic` (requires a card). Do **not** use `CollectionMethod.Invoice` (legacy Statements Architecture only — not valid on RI). Do **not** use `Prepaid` (needs prepaid funding).

**Omit entirely:** `PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes`, any `chargify_token`. **Omit** `ProductPricePointId` / `ProductPricePointHandle`. **Omit** coupons, components, `CustomerAttributes`.

**`CreateSubscriptionRequest`:** `Subscription (subscription): CreateSubscription !req`

Return to the API: `Subscription.Product.Handle` / `.Name` / `.PriceInCents`, `Subscription.State`, `Subscription.NextAssessmentAt (next_assessment_at)` (next billing date). Envelope: `SubscriptionResponse.Subscription` is `Subscription?` — null-check.

**`AddCouponsRequest`:** `Codes (codes): IReadOnlyList<string>?` (`records-1-Ac-Cr.md`)

**`UpdateSubscriptionRequest`:** `Subscription (subscription): UpdateSubscription !req` (`records-4-Su-We.md`)

**`UpdateSubscription` (plan change / dates / payment):** `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `ProductChangeDelayed (product_change_delayed): bool?` — `true` = change at next renewal, no proration. `ProductPricePointId (product_price_point_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?`, `NextProductId (next_product_id): string?` — empty string cancels a delayed product change. `NextBillingAt (next_billing_at): DateTimeOffset?`, `CreditCardAttributes (credit_card_attributes): CreditCardAttributes?`, `PaymentCollectionMethod (payment_collection_method): string?`, `Reference (reference): string?`. Same-family product change via this op is charged at next period. Complex/prorated upgrades → `MigrateSubscriptionProduct` (distinct op).

**`SubscriptionResponse`:** `Subscription (subscription): Subscription?` — inner is **nullable**. (`records-4-Su-We.md`)

**`Subscription` fields the storefront/admin read:** `Id (id): int?`, `State (state): SubscriptionState?`, `BalanceInCents (balance_in_cents): long?`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `CurrentPeriodStartedAt (current_period_started_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `ActivatedAt (activated_at): DateTimeOffset?`, `CancelAtEndOfPeriod (cancel_at_end_of_period): bool?`, `CanceledAt (canceled_at): DateTimeOffset?`, `CancellationMethod (cancellation_method): CancellationMethod?`, `CancellationMessage (cancellation_message): string?`, `DelayedCancelAt (delayed_cancel_at): DateTimeOffset?`, `ScheduledCancellationAt (scheduled_cancellation_at): DateTimeOffset?`, `CouponCode (coupon_code): string?`, `CouponCodes (coupon_codes): IReadOnlyList<string>?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `Customer (customer): Customer?`, `Product (product): Product?`, `CreditCard (credit_card): CreditCardPaymentProfile?`, `ProductPricePointId (product_price_point_id): int?`, `NextProductId (next_product_id): int?`, `NextProductHandle (next_product_handle): string?`, `Reference (reference): string?`, `Currency (currency): string?`, `SelfServicePageToken (self_service_page_token): string?` (only if `include` contains `SubscriptionInclude.SelfServicePageToken`).

**`ErrorListResponse1`:** `Errors (errors): IReadOnlyList<string> !req` (`records-2-Cr-Ne.md`)

**`SubscriptionAddCouponError1`:** `Codes`, `CouponCode`, `CouponCodes`, `Subscription` — each `IReadOnlyList<string>?` (`records-3-Of-Su.md`)

**`SubscriptionPreviewResponse`:** `SubscriptionPreview (subscription_preview): SubscriptionPreview !req` with `CurrentBillingManifest` / `NextBillingManifest` (`BillingManifest`: `TotalInCents`, `LineItems`, dates). (`records-4-Su-We.md`, `records-1-Ac-Cr.md`)

---

#### SubscriptionProducts (distinct plan-change / migration) — `client.SubscriptionProducts` · `operations/SubscriptionProducts.md`

| Op | Signature | Envelope / return | Error |
|---|---|---|---|
| **MigrateSubscriptionProduct** | `MigrateSubscriptionProduct(int subscriptionId, SubscriptionProductMigrationRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | `SubscriptionResponse` | **A** `SdkException<MigrateSubscriptionProductError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` |
| **PreviewSubscriptionProductMigration** | `PreviewSubscriptionProductMigration(int subscriptionId, SubscriptionMigrationPreviewRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | `SubscriptionMigrationPreviewResponse` → `.Migration` | **A** `SdkException<PreviewSubscriptionProductMigrationError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` |

Use when the storefront needs **immediate prorated** upgrade/downgrade. Valid states: `active` or `trialing`. Pass `ProductId` or `ProductHandle`; optional price point; else product default.

**`SubscriptionProductMigrationRequest`:** `Migration (migration): SubscriptionProductMigration !req` (`records-4-Su-We.md`)

**`SubscriptionProductMigration`:** `ProductId (product_id): int?`, `ProductHandle (product_handle): string?`, `ProductPricePointId (product_price_point_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?`, `IncludeTrial (include_trial): bool? = false`, `IncludeInitialCharge (include_initial_charge): bool? = false`, `IncludeCoupons (include_coupons): bool? = true`, `PreservePeriod (preserve_period): bool? = false`, `Proration (proration): Proration?` (`PreservePeriod (preserve_period): bool?` on `Proration`)

**`SubscriptionMigrationPreviewRequest`:** `Migration (migration): SubscriptionMigrationPreviewOptions !req` — same product identifiers plus `ProrationDate (proration_date): DateTimeOffset?`

**`SubscriptionMigrationPreview`:** `ProratedAdjustmentInCents`, `ChargeInCents`, `PaymentDueInCents`, `CreditAppliedInCents` — all `long?`

---

#### SubscriptionStatus (cancel) — `client.SubscriptionStatus` · `operations/SubscriptionStatus.md` · `Api/SubscriptionStatus.cs`

| Op | Signature | Envelope / return | Error |
|---|---|---|---|
| **CancelSubscription** (immediate) | `CancelSubscription(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** (`null` = cancel now, no schedule) | `SubscriptionResponse` | **A** `SdkException<CancelSubscriptionApiError>`: `TryGetNoContent(out RawError)` [404] · `TryGetCancelSubscriptionErrorResponse(out CancelSubscriptionErrorResponse)` [422] · `TryGetRawError` |
| **InitiateDelayedCancellation** | `InitiateDelayedCancellation(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly**. Cancels at end of current period. Cannot if past due; cannot set `cancel_at_end_of_period` at create time | `DelayedCancellationResponse` | **A** `SdkException<InitiateDelayedCancellationError>`: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| **CancelDelayedCancellation** | `CancelDelayedCancellation(int subscriptionId, CancellationToken ct = default)` — idempotent; clears `cancel_at_end_of_period` | `DelayedCancellationResponse` | **A** `SdkException<CancelDelayedCancellationError>`: `TryGetNoContent` [404] · `TryGetRawError` |

Immediate vs delayed: omit schedule fields / pass `body: null` on `CancelSubscription` for immediate (`state` → `canceled`). Delayed = `InitiateDelayedCancellation` (site must have Schedule Subscription Cancellation enabled — `Site.ScheduleSubscriptionCancellationEnabled`). Optional scheduled cancel also via `CancellationOptions.ScheduledCancellationAt` on the same `CancellationRequest` body for `CancelSubscription`.

**`CancellationRequest`:** `Subscription (subscription): CancellationOptions !req` (`records-1-Ac-Cr.md`)

**`CancellationOptions`:** `CancellationMessage (cancellation_message): string?`, `ReasonCode (reason_code): string?`, `CancelAtEndOfPeriod (cancel_at_end_of_period): bool?`, `ScheduledCancellationAt (scheduled_cancellation_at): DateTimeOffset?`, `RefundPrepaymentAccountBalance (refund_prepayment_account_balance): bool?`

**`DelayedCancellationResponse`:** `Message (message): string?` (`records-2-Cr-Ne.md`)

**`CancelSubscriptionErrorResponse`** (union, `unions.md`): `ErrorListResponse1` \| `SingleErrorResponse1`. Factories: `CancelSubscriptionErrorResponse.ErrorListResponse1(...)`, `.SingleErrorResponse1(...)`. Accessors: `TryGetErrorListResponse1(out …)`, `TryGetSingleErrorResponse1(out …)`. `SingleErrorResponse1`: `Error (error): string !req`. Namespace: `MaxioAdvancedBilling.Models.AnyOf`.

---

#### Invoices — `client.Invoices` · `operations/Invoices.md`

| Op | Signature | Envelope / return | Error | Pagination |
|---|---|---|---|---|
| **ListInvoices** | `ListInvoices(string? startDate, string? endDate, InvoiceStatus? status, int? subscriptionId, string? subscriptionGroupUid, string? consolidationLevel, Direction? direction, InvoiceDateField? dateField, string? startDatetime, string? endDatetime, IReadOnlyList<int>? customerIds, IReadOnlyList<string>? number, IReadOnlyList<int>? productIds, InvoiceSortField? sort, int? page = 1, int? perPage = 20, bool? lineItems = false, bool? discounts = false, bool? taxes = false, bool? credits = false, bool? payments = false, bool? customFields = false, bool? refunds = false, CancellationToken ct = default)` — 14 params `startDate`…`sort` **must pass explicitly** | `ListInvoicesResponse` → `.Invoices` (`IReadOnlyList<Invoice> !req`) — **not** `InvoiceResponse` wrappers | **B** `SdkException<RawError>` | `page`+`perPage` |
| **ReadInvoice** | `ReadInvoice(string uid, CancellationToken ct = default)` | `Invoice` **directly** (no wrapper) | **B** `SdkException<RawError>` | none |

Storefront: `subscriptionId:` the buyer’s Maxio subscription id, `lineItems: true` when showing a detail row. Admin: `customerIds:` and/or date/status filters. Default list **omits** line item breakdowns unless those bools are true.

**`Invoice` fields shown:** `Uid (uid): string?` (path id for `ReadInvoice`), `Id (id): long?`, `Number (number): string?`, `Status (status): InvoiceStatus?`, `SubscriptionId (subscription_id): int?`, `CustomerId (customer_id): int?`, `IssueDate (issue_date): DateTimeOffset?`, `DueDate (due_date): DateTimeOffset?`, `PaidDate (paid_date): DateTimeOffset?`, `TotalAmount (total_amount): string?`, `DueAmount (due_amount): string?`, `PaidAmount (paid_amount): string?`, `SubtotalAmount (subtotal_amount): string?`, `ProductName (product_name): string?`, `Currency (currency): string?`, `PublicUrl (public_url): string?`, `LineItems (line_items): IReadOnlyList<InvoiceLineItem>?`. (`records-2-Cr-Ne.md`)

---

#### PaymentProfiles — `client.PaymentProfiles` · `operations/PaymentProfiles.md`

| Op | Signature | Envelope / return | Error |
|---|---|---|---|
| **CreatePaymentProfile** | `CreatePaymentProfile(CreatePaymentProfileRequest? body, CancellationToken ct = default)` — `body` **must pass explicitly** | `PaymentProfileResponse` → `.PaymentProfile` (`PaymentProfile !req` **union**) | **A** `SdkException<CreatePaymentProfileError>`: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| **ListPaymentProfiles** | `ListPaymentProfiles(int? customerId, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — `customerId` **must pass explicitly** | `IReadOnlyList<PaymentProfileResponse>` | **B** `SdkException<RawError>` |
| **ChangeSubscriptionDefaultPaymentProfile** | `ChangeSubscriptionDefaultPaymentProfile(int subscriptionId, int paymentProfileId, CancellationToken ct = default)` | `PaymentProfileResponse` | **A** `SdkException<ChangeSubscriptionDefaultPaymentProfileError>`: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` |

Creating a profile does **not** attach it to a subscription; pass `PaymentProfileId` on create or call `ChangeSubscriptionDefaultPaymentProfile`.

**`CreatePaymentProfileRequest`:** `PaymentProfile (payment_profile): CreatePaymentProfile !req` (`records-1-Ac-Cr.md`)

**`CreatePaymentProfile`:** `ChargifyToken (chargify_token): string?`, `CustomerId (customer_id): int?`, `PaymentType (payment_type): PaymentType?`, `FirstName`, `LastName`, `ExpirationMonth`/`ExpirationYear` (unions `ExpirationMonth1` / `ExpirationYear1`), billing address, bank fields. Prefer token + `CustomerId`.

**`PaymentProfile` union** (`unions.md`, `MaxioAdvancedBilling.Models.OneOf`): variants `ApplePayPaymentProfile`, `BankAccountPaymentProfile`, `CreditCardPaymentProfile`, `PaypalPaymentProfile`. Read: `TryGetCreditCardPaymentProfile(out CreditCardPaymentProfile)`, `TryGetBankAccountPaymentProfile`, `TryGetPaypalPaymentProfile`, `TryGetApplePayPaymentProfile`. Factories: `PaymentProfile.CreditCardPaymentProfile(...)`. **Do not `new` the union.**

**`CreditCardPaymentProfile` (read):** `Id (id): int?`, `MaskedCardNumber (masked_card_number): string?`, `CardType (card_type): CardType?`, `ExpirationMonth (expiration_month): int?`, `ExpirationYear (expiration_year): int?`, `CustomerId (customer_id): int?`, `PaymentType (payment_type): PaymentType = PaymentType.CreditCard`. (`records-2-Cr-Ne.md`)

---

#### ProductFamilies — list plans by HANDLE (hero `GET /api/subscription-plans`) — `operations/ProductFamilies.md` · `Api/ProductFamilies.cs`

`Products.ListProducts` has **no** family-handle argument. `ListProductsFilter` (`records-2-Cr-Ne.md`) is only `Ids (ids): IReadOnlyList<int>?`, `PrepaidProductPricePoint (prepaid_product_price_point): PrepaidProductPricePointFilter?`, `UseSiteExchangeRate (use_site_exchange_rate): bool?` — **do not** use `Ids` (numeric, unstable). `ProductFamilies.ReadProductFamily(int id, …)` takes **`int`** and cannot take a handle.

**Hero op:** `ListProductsForProductFamily`. XML-doc on `productFamilyId`: *Either the product family's id or its handle prefixed with `handle:`*. Pass `"handle:" + Maxio:ProductFamilyHandle` (sandbox value `eshop-subscribe` → `"handle:eshop-subscribe"`). Never a numeric family id.

| Op | Signature | Envelope / return | Error | Pagination |
|---|---|---|---|---|
| **ListProductsForProductFamily** | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 params `dateField`…`include` **must pass explicitly** (`null` to skip). Path `{product_family_id}` ← `productFamilyId` | `IReadOnlyList<ProductResponse>` → each `.Product` (`Product !req`) | **A** `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` | `page`+`perPage` (default 20; max 200) |

**Plan-card fields on `Product`** (`records-3-Of-Su.md`) — all present:

| C# | Wire | Type |
|---|---|---|
| `Handle` | `handle` | `string?` |
| `Name` | `name` | `string?` |
| `Description` | `description` | `string?` |
| `PriceInCents` | `price_in_cents` | `long?` |
| `Interval` | `interval` | `int?` |
| `IntervalUnit` | `interval_unit` | `IntervalUnit?` (`Day (day)`, `Month (month)`) |
| `RequireCreditCard` | `require_credit_card` | `bool?` |
| `ProductFamily` | `product_family` | `ProductFamily?` |

**Nested `ProductFamily.Handle (handle): string?`** exists (`records-3-Of-Su.md`). Also `ProductFamily.Name`, `Id` (do not key off `Id`).

**`ProductResponse`:** `Product (product): Product !req`

Reference-only (not hero): `Products.ListProducts` / `ReadProduct` / `ReadProductByHandle`; `ListProductFamilies`; `ListProductPricePoints`. `ReadProductByHandle(string apiHandle)` can resolve a single plan by handle if needed; list-plans still uses `ListProductsForProductFamily`.

---

#### Coupons (optional at signup) — `client.Coupons` · `operations/Coupons.md`

| Op | Signature | Envelope / return | Error |
|---|---|---|---|
| **FindCoupon** | `FindCoupon(int? productFamilyId, string? code, bool? currencyPrices, CancellationToken ct = default)` — all three **must pass explicitly**. Query `product_family_id`, `code`, `currency_prices` | `CouponResponse` → `.Coupon` (`Coupon?`) | **B** `SdkException<RawError>` (404 if missing) |

Apply at create via `CreateSubscription.CouponCode` / `CouponCodes`, or later `ApplyCouponsToSubscription`. **`Coupon`:** `Id`, `Name`, `Code`, `Percentage`, `AmountInCents`, `Recurring`, `Stackable`. (`records-1-Ac-Cr.md`)

---

#### Sites (public keys for Maxio.js) — `client.Sites` · `operations/Sites.md`

| Op | Signature | Return | Error |
|---|---|---|---|
| **ListChargifyJsPublicKeys** | `ListChargifyJsPublicKeys(int? page = 1, int? perPage = 20, CancellationToken ct = default)` | `ListPublicKeysResponse` — `ChargifyJsKeys (chargify_js_keys): IReadOnlyList<PublicKey>?`, `Meta` | **B** `SdkException<RawError>` |
| **ReadSite** | `ReadSite(CancellationToken ct = default)` | `SiteResponse` → `.Site` (`Site !req`) | **B** `SdkException<RawError>` |

**`PublicKey`:** `PublicKeyValue (public_key): string?`. **`Site`:** `Subdomain (subdomain): string?`, `Currency (currency): string?`, `ScheduleSubscriptionCancellationEnabled (schedule_subscription_cancellation_enabled): bool?`, `Test (test): bool?`.

---

### 2.3 Error core (every operation)

| Type | Namespace | Members | Source |
|---|---|---|---|
| `SdkException<TError>` | `MaxioAdvancedBilling.Core.Exceptions` | `Error: TError` | `Core/Exceptions/SdkException.cs` |
| `ApiError` | `MaxioAdvancedBilling.Core.ErrorResponse` | `TryGetRawError(out RawError error): bool` | `Core/ErrorResponse/ApiError.cs` |
| `RawError` | `MaxioAdvancedBilling.Core.ErrorResponse` | `StatusCode: HttpStatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()` | `Core/ErrorResponse/RawError.cs` |
| Typed `{Op}Error` | `MaxioAdvancedBilling.Errors` | status-specific `TryGet…` + inherited `TryGetRawError` | `Errors/` |

HTTP status on Case A: only via the matching `TryGet…` (or `raw.StatusCode` after `TryGetRawError`). There is no status property on `ApiError` itself. Case B: `ex.Error.StatusCode`.

---

### 2.4 Enums in scope (`map/models/enums.md`) — `StringEnum<T>`: use `Type.Member` or `Type.FromValue("wire")`

| Enum | Members `CSharpName (wire)` |
|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `SubscriptionStateFilter` | `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)` |
| `CancellationMethod` | `MerchantUi (merchant_ui)`, `MerchantApi (merchant_api)`, `Dunning (dunning)`, `BillingPortal (billing_portal)`, `Unknown (unknown)`, `Imported (imported)` |
| `PaymentType` | `CreditCard (credit_card)`, `BankAccount (bank_account)`, `PaypalAccount (paypal_account)`, `ApplePay (apple_pay)` |
| `CollectionMethod` | `Automatic (automatic)` — requires a payment method; `Remittance (remittance)` — **hero no-card path (Relationship Invoicing)**; `Prepaid (prepaid)`; `Invoice (invoice)` — legacy Statements only |
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
| `IntervalUnit` | `Day (day)`, `Month (month)` |
| `PricePointType` | `Catalog (catalog)`, `Default (default)`, `Custom (custom)` |
| `CardType` | `Visa (visa)`, `Master (master)`, `Discover (discover)`, `AmericanExpress (american_express)`, `Bogus (bogus)`, … (full list on `enums.md`; tests use `Bogus`) |
| `ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)` |

`ServerEnvironment` is **not** this enum table — see §2.1 (`MaxioAdvancedBilling.Servers`).

---

### 2.5 Recommended call shapes (named args; `ct:` last) — hero flow

List plans by family handle:

```csharp
await client.ProductFamilies.ListProductsForProductFamily(
    productFamilyId: "handle:" + productFamilyHandleFromConfig,
    dateField: null, filter: null, startDate: null, endDate: null,
    startDatetime: null, endDatetime: null, includeArchived: null, include: null,
    page: 1, perPage: 20, ct: ct);
```

Find customer:

```csharp
await client.Customers.ReadCustomerByReference(reference: eShopUserId, ct: ct);
```

Create customer (only on 404):

```csharp
await client.Customers.CreateCustomer(
    body: new CreateCustomerRequest
    {
        Customer = new CreateCustomer
        {
            FirstName = first,
            LastName = last,
            Email = email,
            Reference = eShopUserId,
        },
    },
    ct: ct);
```

List caller subscriptions:

```csharp
await client.Customers.ListCustomerSubscriptions(customerId: maxioCustomerId, ct: ct);
```

Create subscription **without payment**:

```csharp
await client.Subscriptions.CreateSubscription(
    body: new CreateSubscriptionRequest
    {
        Subscription = new CreateSubscription
        {
            ProductHandle = planHandle, // e.g. eshop-pro / basic-plan — not ProductId
            CustomerId = maxioCustomerId,
            PaymentCollectionMethod = CollectionMethod.Remittance, // no-card RI path; omit → automatic → 422 no payment method
        },
    },
    ct: ct);
```

`ListProductsForProductFamily`: pass `null` for every unused non-defaulted parameter; do not call positionally.

---

## 3. Trap notes

⚠ Step 1–2 (client / DI) — `HttpClient` vs SDK-client lifetime and whether the wrapper is registered transient while the handler pipeline is reused; constructing `new HttpClient()` per request is the defect this skill exists to prevent. **MUST load `dotnet-client-initialization`** before writing the factory or `AddMaxioAdvancedBillingClient` call.

⚠ Step 2 (auth) — credentials must be on options before the client is used; a missing `BasicAuth` or a password other than the literal `"x"` surfaces as 401/403 at call time, not at construction. Load the API key from configuration, never source. **MUST load `dotnet-authentication`** before wiring `BasicAuthCredentials`.

⚠ Step 2 (site URL / retries) — `Retry` / `Timeout` on options are not the timeout of the `HttpClient` you register, and they do not bound a whole logical operation the way a caller might assume; `Site` vs `BaseUrl` vs `Environment` are independent knobs — setting the wrong one talks to the wrong host. **MUST load `dotnet-configuration-resilience`** before registering or tuning the client.

⚠ Steps 3–6 (every call) — list/filter operations have many nullable parameters **without C# defaults**; a positional call binds the wrong argument. Cancellation token argument name is `ct`. **MUST load `dotnet-calling-endpoints`** before the first `client.{Group}.{Op}` call.

⚠ Steps 3–5 (payloads) — records are `init`/`required`; enums are `StringEnum<T>` (`SubscriptionState.Active`, not a C# enum); compare plans by `Product.Handle`, never id. Unmodeled JSON is dropped on deserialize. **MUST load `dotnet-models`** before building requests or mapping responses.

⚠ Step 7 (error boundary) — Case A vs Case B differ **per operation** (this sheet); `TryGetRawError` is not a catch-all on typed errors in the way a single ladder might assume; there are no no-throw result types. **MUST load `dotnet-error-handling`** before any `try/catch`.

⚠ Step 7 — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`**.

⚠ Step 7 — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`**.

⚠ Step 5 (create subscription / customer) — a failed write may still have executed on the provider; whether a retry is safe is not something to infer from the method verb alone. Double-click idempotency is `ReadCustomerByReference` + `ListCustomerSubscriptions` **before** create, and 422 → re-read. **MUST load `dotnet-configuration-resilience`** before choosing retry behavior around `CreateSubscription` / `CreateCustomer`.

⚠ Step 8 (tests) — the `HttpClient` constructor argument is the seam; match eShopOnWeb’s existing test framework and assertions. **MUST load `dotnet-testing`** before stubbing.

---

## 4. REQUIRED READING

Load **before implementation starts**. This sheet does **not** carry their contents — an unloaded pointer is a gap.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Steps 1–2 — ctor, options, DI, `HttpClient` lifetime |
| `dotnet-authentication` | Step 2 — `BasicAuthCredentials`, config-sourced API key |
| `dotnet-configuration-resilience` | Steps 2, 5 — `Environment`/`Site`/`BaseUrl`, retries, timeouts, pagination, logging |
| `dotnet-calling-endpoints` | Steps 3–6 — named args, must-pass-explicitly params, `ct:`, async |
| `dotnet-models` | Steps 3–5 — required records, `StringEnum<T>`, handle vs id |
| `dotnet-error-handling` | Step 7 — Case A/B, accessors, **both** `JsonException` paths below |
| `dotnet-testing` | Step 8 — `HttpClient` test seam |

**`JsonException` reaches the boundary from two directions and they need opposite handling** (must shape the boundary on the first sheet):

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions**

- Required surface is three JWT endpoints: list plans, idempotent subscribe, list my subscriptions. Payment token / Maxio.js / 3-DS is **not** used for this hero flow.
- Cancel, invoices, plan-change, coupons, payment profiles, and components are **out of the required endpoint set** (contract rows kept only as already-written reference).
- Buyer correlation key is `Customer.Reference` = eShop user id string.
- Catalog is identified by **handles** (`Maxio:ProductFamilyHandle`, product `Handle`); numeric IDs are never stored or used as keys.
- Subscribe request body identifies the plan by product handle from list-plans (`eshop-pro` / `basic-plan` in sandbox).
- `CollectionMethod.Remittance` (wire `remittance`) is required on create so the first invoice is not collected automatically; omitting it inherits site `automatic` and 422s without a card. `Invoice` is legacy Statements-only.

**Blockers**

- `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and `MAXIO_ENVIRONMENT` (`US`/`EU`) must come from configuration; optional `Maxio:BaseUrl` when the host is not the default template.
- Sandbox products (`eshop-pro`, `basic-plan` under family `eshop-subscribe`) must already exist; this integration does not create the catalog.
- `CustomerErrorResponse1.Errors` generated shape (`PerPage` / `PricePoint`) vs live duplicate-reference 422 body is **UNVERIFIED**; extract best-effort, then `RawError.ReadAsString()`, then re-`ReadCustomerByReference`.
