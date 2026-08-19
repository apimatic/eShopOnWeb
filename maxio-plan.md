# Maxio Advanced Billing .NET SDK — eShopOnWeb integration plan

Package `AsadAli.AdvancedBilling.Sdk` **version `1.0.2`** · root namespace `MaxioAdvancedBilling` · map stamp `v1.0.2` / `15db14b`.

## Scope & sequence

**v1 hero (implement now)** — JWT endpoints on `src/PublicApi` only. Catalog is already seeded; do **not** create products/coupons/components. Handles are stable; numeric IDs are not. Subscribe **without** card / Chargify.js / payment profiles (seeded products have `RequireCreditCard = false`). Metered component `api-call` is seeded but **not** required for hero subscribe.

| PublicApi | Auth | SDK ops |
|---|---|---|
| `GET /api/subscription-plans` | JWT | `ProductFamilies.ListProductsForProductFamily` with `productFamilyId: "handle:" + Maxio:ProductFamilyHandle` (seeded family `eshop-subscribe`) |
| `POST /api/subscriptions` | JWT | Ensure customer (`ReadCustomerByReference` → `CreateCustomer`; 422 → re-read) then idempotent enroll (`ListCustomerSubscriptions` match `Product.Handle`, else `CreateSubscription` with `ProductHandle` + `CustomerId`, **no** payment fields). Default plan handle `eshop-pro`; `basic-plan` also seeded. |
| `GET /api/my-subscriptions` | JWT | `ReadCustomerByReference` → `ListCustomerSubscriptions`. Return plan handle, price, `State`, next billing (`NextAssessmentAt` / `CurrentPeriodEndsAt`). **Not** `FindSubscription` (that looks up a **subscription** `reference`, not a customer). |

Config bind from `Maxio:` (hard-code none): `ApiKey` ← env `MAXIO_API_KEY`; `Subdomain` ← `MAXIO_SITE_SUBDOMAIN`; `ProductFamilyHandle` ← `MAXIO_DEFAULT_PRODUCT_FAMILY`; `BaseUrl` optional verbatim Production BaseUrl; env `MAXIO_ENVIRONMENT` (2-char `US`/`EU`) → `ServerEnvironment`. Sandbox = US (or EU) **site**, not a third environment.

1. **Client & DI** — package `AsadAli.AdvancedBilling.Sdk` `1.0.2`, `AddMaxioAdvancedBillingClient`, Basic auth, `ServerEnvironment` + `Site` **or** verbatim `BaseUrl`, retry/timeout options.
2. **Customers** — `CreateCustomer`, `ReadCustomerByReference` (eShop user id as `reference`), `ReadCustomer`, `UpdateCustomer`, `ListCustomerSubscriptions`.
3. **Catalog browse** — `ListProductFamilies`, `ListProducts` / `ListProductsForProductFamily`, `ReadProduct` / `ReadProductByHandle`, `ListComponents` / `FindComponent`.
4. **Payment profiles** — unused in v1 hero (kept below). `CreatePaymentProfile` (Chargify.js `chargify_token`), `ListPaymentProfiles`, `ReadPaymentProfile`, `UpdatePaymentProfile`, `ChangeSubscriptionDefaultPaymentProfile`.
5. **Subscribe** — v1: `CreateSubscription` with `ProductHandle` + `CustomerId` only (no preview, no card). `PreviewSubscription` remains available.
6. **Manage subscriptions** — `ReadSubscription`, `FindSubscription`, `ListSubscriptions`, `UpdateSubscription` (payment method / delayed product change).
7. **Plan change (prorated migrate)** — `PreviewSubscriptionProductMigration` then `MigrateSubscriptionProduct`.
8. **Cancel / reactivate** — `CancelSubscription` (immediate), `InitiateDelayedCancellation` / `CancelDelayedCancellation`, `ReactivateSubscription`.
9. **Coupons** — `ValidateCoupon` / `FindCoupon`; `ApplyCouponsToSubscription` / `RemoveCouponFromSubscription`; optional `CouponCode` on create.
10. **Usage / components** — `ListSubscriptionComponents`, `ReadSubscriptionComponent`, `CreateUsage` (metered/prepaid), `AllocateComponent` (quantity/on-off), `ListUsages`. Seeded `api-call` not required for hero.
11. **Invoices** — `ListInvoices`, `ReadInvoice`.
12. **Error boundary + tests** — throw-only SDK; Case A/B per row; tests against the `HttpClient` constructor seam.

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

All records: `MaxioAdvancedBilling.Models`. All enums: `MaxioAdvancedBilling.Models.Enums` (`StringEnum<T>` — use static members, e.g. `CollectionMethod.Automatic`, not C# enums). Unions: `MaxioAdvancedBilling.Models.AnyOf` / `MaxioAdvancedBilling.Models.OneOf`. Typed errors: `MaxioAdvancedBilling.Errors`. `SdkException<T>`: `MaxioAdvancedBilling.Core.Exceptions`. `RawError` / `ApiError`: `MaxioAdvancedBilling.Core.ErrorResponse`. Controllers: `MaxioAdvancedBilling.Api` via `client.{Property}`.

Every operation is **throw-only** (no `…Result` variants). Nullable params with **no C# default must be passed explicitly** (`null` to skip).

### Client construction / auth / server (`sdk-map.md`, `ServiceCollectionExtensions.cs`, `Core/Configuration/RetryOptions.cs`, `ServerOptions.cs`, `Servers/ProductionOptions.cs`, `Servers/ServerEnvironment.cs`)

| Fact | Value | Cite |
|---|---|---|
| NuGet | Package id `AsadAli.AdvancedBilling.Sdk`, version **`1.0.2`** (map stamp tagged `v1.0.2`) | `sdk-map.md` |
| Client | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` — **only** ctor `(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `sdk-map.md`, `MaxioAdvancedBillingClient.cs` |
| Options | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions`: `Environment` (`ServerEnvironment`, default `ServerEnvironment.Default()` = `Us`), `Retry` (default `RetryOptions.Default()`), `Server` (`new ServerOptions()`), `BasicAuth` | `MaxioAdvancedBillingClientOptions.cs` |
| DI | `MaxioAdvancedBilling.ServiceCollectionExtensions.AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>? configure = null)` — C# extension on `IServiceCollection`. Calls `services.AddHttpClient()` then registers `MaxioAdvancedBillingClient` as **Singleton** built with `IHttpClientFactory.CreateClient()` (unnamed client) + the configured options. The extension **does** take HttpClient from the factory; it does **not** use the two-arg ctor from app code. Manual registration: `new MaxioAdvancedBillingClient(httpClient, options)`. | `ServiceCollectionExtensions.cs` |
| Auth | `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials` — `Username` = `Maxio:ApiKey` (`MAXIO_API_KEY`), `Password` = literal `"x"` | `sdk-map.md` |
| Environment | `MaxioAdvancedBilling.Servers.ServerEnvironment` (`StringEnum`): **only** `Us` (wire `US`, default) and `Eu` (wire `EU`). **No sandbox member.** Map `MAXIO_ENVIRONMENT` with `ServerEnvironment.FromValue` (or `Us`/`Eu` statics) after normalizing to `US`/`EU`. Sandbox is the **site** (subdomain), still `Us` or `Eu`. | `sdk-map.md`, `Servers/ServerEnvironment.cs` |
| Production URL templates | US `https://{site}.chargify.com` · EU `https://{site}.ebilling.maxio.com` | `sdk-map.md`, `Servers/ProductionOptions.cs` |
| Nested types for base URL | `options.Server` → `MaxioAdvancedBilling.ServerOptions.Production` → `MaxioAdvancedBilling.Servers.ProductionOptions`. Then **environment-selected node**: `.Us` → nested `ProductionOptions.UsOptions` (`BaseUrl`, `Site`) or `.Eu` → nested `ProductionOptions.EuOptions` (`BaseUrl`, `Site`). `Resolve` picks Us vs Eu from `options.Environment` — setting `Us.BaseUrl` while `Environment` is `Eu` is ignored. | `ServerOptions.cs`, `Servers/ProductionOptions.cs` |
| When **only** `Maxio:Subdomain` | Set `options.Server.Production.Us.Site` (if `Us`) or `.Eu.Site` (if `Eu`) to the subdomain. Leave `BaseUrl` at the default template so `{site}` is substituted. | `sdk-map.md`, `Servers/ProductionOptions.cs` |
| When `Maxio:BaseUrl` **is set** | Assign it **verbatim** to the environment-selected `BaseUrl`: `options.Server.Production.Us.BaseUrl` or `.Eu.BaseUrl`. Do **not** derive a host from subdomain. `Site` is only interpolated if that `BaseUrl` still contains `{site}`. | `sdk-map.md`, `Servers/ProductionOptions.cs` |
| Retry | `MaxioAdvancedBilling.Core.Configuration.RetryOptions` — all members `required`; `RetryOptions.Default()` **exists**. Default: `HttpMethodsToRetry` = `GET`, `HEAD`, `PUT`, `OPTIONS` (**not POST**); `StatusCodesToRetry` = 408, 429, 500, 502, 503, 504; `MaxRetries` = 3; `Delay` = 1s; `BackOffFactor` = 2; `UseExponentialBackoff` = true; `MaxJitter` = 500ms; `Timeout` = 100s; `OnRetry` = null. Status-code retries therefore do **not** re-send `CreateSubscription` (POST). Options object already initializes `Retry = RetryOptions.Default()`. | `sdk-map.md`, `Core/Configuration/RetryOptions.cs` |
| Ebb | Only `SubscriptionComponents` event-ingest (`RecordEvent` / `BulkRecordEvents`) uses Ebb (`https://events.chargify.com/{site}`). **Out of scope** for this storefront. | `sdk-map.md`, `operations/SubscriptionComponents.md` |

### Operations

#### Customers — `client.Customers` (`operations/Customers.md`, `Api/Customers.cs`)

| Op | Signature | Request | Response envelope (fields read) | Error | Pagination |
|---|---|---|---|---|---|
| `CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` must pass explicitly | Envelope `CreateCustomerRequest`: `Customer (customer): CreateCustomer !req`. Inner **required**: `FirstName (first_name)`, `LastName (last_name)`, `Email (email)`. Set `Reference (reference)` to the eShop user id (unique). Optional: `Organization`, `Address`/`Address2`/`City`/`State`/`Zip`/`Country` (ISO 2-char country; ISO state), `Phone`, `CcEmails`, `Locale`, `VatNumber`, `TaxExempt`, `TaxExemptReason`, `ParentId`, `SalesforceId` (`records-1-Ac-Cr.md`) | `CustomerResponse` → `Customer (customer): Customer !req`. Read: `Id`, `Reference`, `Email`, `FirstName`, `LastName`, `Organization`, `CreatedAt` (`records-2-Cr-Ne.md`) | **A** `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)`. Payload `CustomerErrorResponse1.Errors (errors): Errors?` (`PerPage`/`PricePoint` lists — **not** a dedicated “duplicate reference” accessor). Duplicate `reference` is a 422. **Idempotent enroll:** `ReadCustomerByReference` first; on miss `CreateCustomer`; on 422 race, `ReadCustomerByReference` again and use that customer. Do not treat 422 payload shape as the source of truth for “already exists” (`operations/Customers.md`). | none |
| `ReadCustomer` | `ReadCustomer(int id, CancellationToken ct = default)` | — | `CustomerResponse` → `.Customer` | **B** `SdkException<RawError>`: `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | none |
| `ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` · query `reference` | exact match on customer `reference` (eShop user id). Miss = Case B (typically 404 via `StatusCode`) | `CustomerResponse` → `.Customer` | **B** `SdkException<RawError>` | none |
| `UpdateCustomer` | `UpdateCustomer(int id, UpdateCustomerRequest? body, CancellationToken ct = default)` — `body` must pass | Envelope `UpdateCustomerRequest`: `Customer (customer): UpdateCustomer !req`. All inner fields optional (`FirstName`, `LastName`, `Email`, `Reference`, address, `Locale`, …) (`records-4-Su-We.md`) | `CustomerResponse` → `.Customer` | **A** `SdkException<UpdateCustomerError>`: `TryGetNoContent(out RawError)` [404] · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError` | none |
| `ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | **Hero `GET /api/my-subscriptions` and POST idempotency.** Keyed by **customer id** (after `ReadCustomerByReference`). Returns every subscription for that customer (no filter param). Match plan via nested `Subscription.Product.Handle`. | `IReadOnlyList<SubscriptionResponse>` — each `.Subscription` | **B** `SdkException<RawError>` | none |
| `ListCustomers` | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — 7 params `direction`…`q` must pass | query: `q` search (email/id/org/reference/name). Exact reference match → use `ReadCustomerByReference` | `IReadOnlyList<CustomerResponse>` | **B** `SdkException<RawError>` | `page`+`perPage` |

#### Products / families — `client.Products`, `client.ProductFamilies` (`operations/Products.md`, `operations/ProductFamilies.md`)

| Op | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `ListProducts` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 params `dateField`…`include` must pass | Filter `ListProductsFilter`: `Ids`, `PrepaidProductPricePoint`, `UseSiteExchangeRate` (`records-2-Cr-Ne.md`) | `IReadOnlyList<ProductResponse>` → each `.Product (product): Product !req`. Read: `Id`, `Name`, `Handle`, `Description`, `PriceInCents`, `Interval`, `IntervalUnit`, `TrialPriceInCents`, `TrialInterval`, `RequireCreditCard`, `ProductFamily`, `ProductPricePointId`, `ProductPricePointHandle` (`records-3-Of-Su.md`) | **B** `SdkException<RawError>` | `page`+`perPage` |
| `ReadProduct` | `ReadProduct(int productId, CancellationToken ct = default)` | — | `ProductResponse` → `.Product` | **B** | none |
| `ReadProductByHandle` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | path `api_handle` | `ProductResponse` → `.Product` | **B** | none |
| `ListProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 must pass | — | `IReadOnlyList<ProductFamilyResponse>` → `.ProductFamily (product_family): ProductFamily?`. Read: `Id`, `Name`, `Handle` | **B** | none |
| `ReadProductFamily` | `ReadProductFamily(int id, CancellationToken ct = default)` | id number (notes also mention `handle:my-family` format on the HTTP path; C# param is `int id`) | `ProductFamilyResponse` | **B** | none |
| `ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 params `dateField`…`include` must pass | `productFamilyId` is `string`: **either the numeric id as a string, or the handle prefixed with `handle:`** (XML: “Either the product family's id or its handle prefixed with `handle:`”). Hero: `"handle:" + Maxio:ProductFamilyHandle` e.g. `"handle:eshop-subscribe"`. Bare `"eshop-subscribe"` is **not** the documented form. Fallback if you only have a handle and refuse the prefix: `ListProductFamilies` + match `ProductFamily.Handle`, then pass `Id.ToString()`. `ReadProductFamily(int id)` cannot take a handle (C# param is `int`). | `IReadOnlyList<ProductResponse>` → each `.Product`. Read `Handle`, `Name`, `PriceInCents` (`long?`), `RequireCreditCard`, `Interval`/`IntervalUnit` | **A** `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError` | `page`+`perPage` |

#### Components catalog — `client.Components` (`operations/Components.md`)

| Op | Signature | Response | Error | Pagination |
|---|---|---|---|---|
| `ListComponents` | `ListComponents(BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, bool? includeArchived, ListComponentsFilter? filter, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 7 params `dateField`…`filter` must pass | `IReadOnlyList<ComponentResponse>` → `.Component (component): Component !req`. Read: `Id`, `Name`, `Handle`, `Kind`, `UnitName`, `UnitPrice`, `ProductFamilyId`, `PricingScheme` (`records-1-Ac-Cr.md`) | **B** | `page`+`perPage` |
| `FindComponent` | `FindComponent(string handle, CancellationToken ct = default)` · query `handle` | `ComponentResponse` → `.Component` | **B** | none |
| `ListComponentsForProductFamily` | `ListComponentsForProductFamily(int productFamilyId, bool? includeArchived, ListComponentsFilter? filter, BasicDateField? dateField, string? endDate, string? endDatetime, string? startDate, string? startDatetime, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 7 params `includeArchived`…`startDatetime` must pass | `IReadOnlyList<ComponentResponse>` | **B** | `page`+`perPage` |
| `ReadComponent` | `ReadComponent(int productFamilyId, string componentId, CancellationToken ct = default)` — `componentId` may be numeric id **or** `handle:<handle>` | `ComponentResponse` | **B** | none |

#### Payment profiles — `client.PaymentProfiles` (`operations/PaymentProfiles.md`)

| Op | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `CreatePaymentProfile` | `CreatePaymentProfile(CreatePaymentProfileRequest? body, CancellationToken ct = default)` — `body` must pass | Envelope: `PaymentProfile (payment_profile): CreatePaymentProfile !req`. Prefer `ChargifyToken (chargify_token)` + `CustomerId (customer_id)`. Other card fields (`FullNumber`, `Cvv`, …) exist but collecting raw PAN in production requires PCI on the merchant (`records-1-Ac-Cr.md`) | `PaymentProfileResponse` → `PaymentProfile (payment_profile): PaymentProfile !req` **(OneOf union)**. Read via `TryGetCreditCardPaymentProfile` / `TryGetBankAccountPaymentProfile` / `TryGetPaypalPaymentProfile` / `TryGetApplePayPaymentProfile` (`unions.md`, `records-3-Of-Su.md`). Credit-card variant: `Id`, `MaskedCardNumber`, `CustomerId`, `PaymentType` | **A** `SdkException<CreatePaymentProfileError>`: `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError`. 422 also used for 3DS `action_link` post-auth flow | none |
| `ListPaymentProfiles` | `ListPaymentProfiles(int? customerId, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — `customerId` must pass | empty array (not 404) when none | `IReadOnlyList<PaymentProfileResponse>` | **B** | `page`+`perPage` |
| `ReadPaymentProfile` | `ReadPaymentProfile(int paymentProfileId, CancellationToken ct = default)` | — | `PaymentProfileResponse` (union as above) | **A** `SdkException<ReadPaymentProfileError>`: `TryGetNoContent` [404] · `TryGetRawError` | none |
| `UpdatePaymentProfile` | `UpdatePaymentProfile(int paymentProfileId, UpdatePaymentProfileRequest? body, CancellationToken ct = default)` — `body` must pass | Envelope: `PaymentProfile (payment_profile): UpdatePaymentProfile !req` (`records-4-Su-We.md`) | `PaymentProfileResponse` | **A** `SdkException<UpdatePaymentProfileError>`: `TryGetNoContent` [404] · `TryGetErrorStringMapResponse1(out ErrorStringMapResponse1)` [422] · `TryGetRawError`. Payload `Errors (errors): IReadOnlyDictionary<string, string>?` | none |
| `ChangeSubscriptionDefaultPaymentProfile` | `ChangeSubscriptionDefaultPaymentProfile(int subscriptionId, int paymentProfileId, CancellationToken ct = default)` | — | `PaymentProfileResponse` | **A** `SdkException<ChangeSubscriptionDefaultPaymentProfileError>`: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |

`ErrorListResponse1`: `Errors (errors): IReadOnlyList<string> !req` (`records-2-Cr-Ne.md`).

#### Subscriptions — `client.Subscriptions` (`operations/Subscriptions.md`)

| Op | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must pass | Envelope **required**: `Subscription (subscription): CreateSubscription !req`. Inner C# members are **all optional** (`?`); no `!req` on `CreateSubscription`. Notes: product via `product_id` **or** `product_handle`; customer via `customer_id` **or** `customer_reference` **or** `customer_attributes`; “Payment information **may** be required … depending on the options for the Product”. **v1 hero (seeded `RequireCreditCard = false`):** set `ProductHandle (product_handle)` (e.g. `eshop-pro` / `basic-plan`) **and** `CustomerId (customer_id)` (from ensure-customer). **Omit** `PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes` (no Chargify.js / no card). Do **not** send numeric `ProductId` (IDs not stable). Optional: `Reference` for a stable subscription key. Do not send `api-call` in `Components`. (`records-2-Cr-Ne.md`, `operations/Subscriptions.md`) | `SubscriptionResponse` → `Subscription (subscription): Subscription?`. Read: `Id`, `State` (`SubscriptionState?`), `Product` (`.Handle`, `.PriceInCents`), `ProductPriceInCents (product_price_in_cents): long?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?` (`records-3-Of-Su.md`) | **A** `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError`. 3DS `action_link` is **out of v1** (no card) | none |
| `PreviewSubscription` | `PreviewSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — same body shape; does **not** create | same as create | `SubscriptionPreviewResponse` → `SubscriptionPreview (subscription_preview): SubscriptionPreview !req` → `CurrentBillingManifest` / `NextBillingManifest` (`TotalInCents`, `LineItems`, dates) (`records-4-Su-We.md`, `records-1-Ac-Cr.md`) | **B** `SdkException<RawError>` | none |
| `ReadSubscription` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` must pass | `include`: `Coupons`, `SelfServicePageToken` (self-service token **not** returned unless requested) | `SubscriptionResponse` → `.Subscription` | **B** | none |
| `FindSubscription` | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` must pass · query `reference` | Looks up a **subscription** `reference` (`CreateSubscription.Reference`), **not** a customer. **Do not use for `GET /api/my-subscriptions`.** That endpoint is `ListCustomerSubscriptions`. Optional extra POST guard only if you set a stable subscription `Reference` (e.g. `{userId}:{productHandle}`) at create. | `SubscriptionResponse` | **A** `SdkException<FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError` | none |
| `ListSubscriptions` | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 14 params `state`…`include` must pass | — | `IReadOnlyList<SubscriptionResponse>` | **B** | `page`+`perPage` (default 20) |
| `UpdateSubscription` | `UpdateSubscription(int subscriptionId, UpdateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must pass | Envelope: `Subscription (subscription): UpdateSubscription !req`. Product change (same family, next period, no proration): `ProductHandle`/`ProductId` + optional `ProductPricePointId`/`Handle`. Delayed: also `ProductChangeDelayed = true`. Cancel delayed change: `NextProductId` empty string. Payment card nested `CreditCardAttributes`. `NextBillingAt`, `SnapDay` (union) (`records-4-Su-We.md`) | `SubscriptionResponse` | **A** `SdkException<UpdateSubscriptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `ApplyCouponsToSubscription` | `ApplyCouponsToSubscription(int subscriptionId, string? code, AddCouponsRequest? body, CancellationToken ct = default)` — `code` and `body` must pass | **Use body**, not query `code` (query replaces all coupons; deprecated). Body `AddCouponsRequest`: `Codes (codes): IReadOnlyList<string>?` — **adds** to existing (`records-1-Ac-Cr.md`) | `SubscriptionResponse` | **A** `SdkException<ApplyCouponsToSubscriptionError>`: `TryGetSubscriptionAddCouponError1(out SubscriptionAddCouponError1)` [422] · `TryGetRawError`. Payload fields `Codes`/`CouponCode`/`CouponCodes`/`Subscription` as `IReadOnlyList<string>?` (`records-3-Of-Su.md`) | none |
| `RemoveCouponFromSubscription` | `RemoveCouponFromSubscription(int subscriptionId, string? couponCode, CancellationToken ct = default)` — `couponCode` must pass · query `coupon_code` | — | `string` (not an envelope) | **A** `SdkException<RemoveCouponFromSubscriptionError>`: `TryGetSubscriptionRemoveCouponErrors1(out SubscriptionRemoveCouponErrors1)` [422] · `TryGetRawError`. Payload `Subscription (subscription): IReadOnlyList<string> !req` | none |

`CreateSubscriptionComponent` (signup): `ComponentId (component_id): ComponentId1?` (union int/string), `Enabled`, `AllocatedQuantity` (union), `Quantity`, `PricePointId` (`PricePointId2` union) (`records-2-Cr-Ne.md`, `unions.md`).

#### Plan change — `client.SubscriptionProducts` (`operations/SubscriptionProducts.md`)

| Op | Signature | Request | Response | Error |
|---|---|---|---|---|
| `PreviewSubscriptionProductMigration` | `PreviewSubscriptionProductMigration(int subscriptionId, SubscriptionMigrationPreviewRequest? body, CancellationToken ct = default)` — `body` must pass | Envelope: `Migration (migration): SubscriptionMigrationPreviewOptions !req`. Product: `ProductId` **or** `ProductHandle`; price point optional. Flags: `IncludeTrial` default false, `IncludeInitialCharge` default false, `IncludeCoupons` default true, `PreservePeriod` default false, `Proration`, `ProrationDate` (`records-4-Su-We.md`) | `SubscriptionMigrationPreviewResponse` → `Migration (migration): SubscriptionMigrationPreview !req`: `ProratedAdjustmentInCents`, `ChargeInCents`, `PaymentDueInCents`, `CreditAppliedInCents` | **A** `SdkException<PreviewSubscriptionProductMigrationError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `MigrateSubscriptionProduct` | `MigrateSubscriptionProduct(int subscriptionId, SubscriptionProductMigrationRequest? body, CancellationToken ct = default)` — `body` must pass | Envelope: `Migration (migration): SubscriptionProductMigration !req`. Same product/price-point/flags as preview **minus** `ProrationDate`. Subscription must be `active` or `trialing`. Migrating to the **current** product is a common 422. 3DS may 422 with `action_link` | `SubscriptionResponse` | **A** `SdkException<MigrateSubscriptionProductError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` |

Use **migrate** for prorated mid-period plan changes. Use `UpdateSubscription` product fields for next-period (non-prorated) / delayed change.

#### Cancel / reactivate — `client.SubscriptionStatus` (`operations/SubscriptionStatus.md`)

| Op | Signature | Request | Response | Error |
|---|---|---|---|---|
| `CancelSubscription` | `CancelSubscription(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` — `body` must pass | Envelope: `Subscription (subscription): CancellationOptions !req`. Options: `CancellationMessage`, `ReasonCode`, `CancelAtEndOfPeriod`, `ScheduledCancellationAt`, `RefundPrepaymentAccountBalance`. Omit schedule fields for immediate cancel (`records-1-Ac-Cr.md`) | `SubscriptionResponse` | **A** `SdkException<CancelSubscriptionApiError>`: `TryGetNoContent` [404] · `TryGetCancelSubscriptionErrorResponse(out CancelSubscriptionErrorResponse)` [422] · `TryGetRawError`. `CancelSubscriptionErrorResponse` is an **AnyOf**: `TryGetErrorListResponse1` / `TryGetSingleErrorResponse1` (`unions.md`). `SingleErrorResponse1`: `Error (error): string !req` |
| `InitiateDelayedCancellation` | `InitiateDelayedCancellation(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` — `body` must pass | same `CancellationRequest` | `DelayedCancellationResponse` → `Message (message): string?` (not a subscription envelope) | **A** `SdkException<InitiateDelayedCancellationError>`: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` |
| `CancelDelayedCancellation` | `CancelDelayedCancellation(int subscriptionId, CancellationToken ct = default)` | — | `DelayedCancellationResponse` | **A** `SdkException<CancelDelayedCancellationError>`: `TryGetNoContent` [404] · `TryGetRawError` |
| `ReactivateSubscription` | `ReactivateSubscription(int subscriptionId, ReactivateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must pass | `IncludeTrial`, `PreserveBalance`, `CouponCode`, `UseCreditsAndPrepayments`, `Resume` (**AnyOf** `bool` \| `ResumeOptions` — factories `Resume.Bool` / `Resume.ResumeOptions`), `CalendarBilling.ReactivationCharge` (`records-3-Of-Su.md`) | `SubscriptionResponse` | **A** `SdkException<ReactivateSubscriptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError`. 3DS may 422 |

#### Coupons — `client.Coupons` (`operations/Coupons.md`)

| Op | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `ValidateCoupon` | `ValidateCoupon(string code, int? productFamilyId, CancellationToken ct = default)` — `productFamilyId` must pass | Pass `productFamilyId` when the coupon is not on the site’s first/default family | `CouponResponse` → `Coupon (coupon): Coupon?`. Read: `Id`, `Name`, `Code`, `Amount`/`AmountInCents`/`Percentage`, `DiscountType`, `Recurring`, `Stackable`, `EndDate`, `ProductFamilyId` (`records-1-Ac-Cr.md`) | **A** `SdkException<ValidateCouponError>`: `TryGetSingleStringErrorResponse1(out SingleStringErrorResponse1)` [404] · `TryGetRawError`. Payload `Errors (errors): string?` | none |
| `FindCoupon` | `FindCoupon(int? productFamilyId, string? code, bool? currencyPrices, CancellationToken ct = default)` — all three must pass | 404 if not found (Case B) | `CouponResponse` | **B** `SdkException<RawError>` | none |
| `ListCoupons` | `ListCoupons(ListCouponsFilter? filter, bool? currencyPrices, int? page = 1, int? perPage = 30, CancellationToken ct = default)` — `filter` and `currencyPrices` must pass | Filter: `Ids`, `Codes`, `DateField`, date range, `IncludeArchived` | `IReadOnlyList<CouponResponse>` | **B** | `page`+`perPage` (default 30) |
| `ReadCoupon` | `ReadCoupon(int productFamilyId, int couponId, bool? currencyPrices, CancellationToken ct = default)` — `currencyPrices` must pass | — | `CouponResponse` | **B** | none |

Apply/remove on a subscription: `Subscriptions.ApplyCouponsToSubscription` / `RemoveCouponFromSubscription` (above). Create-time: `CreateSubscription.CouponCode` / `CouponCodes`.

#### Usage / allocations — `client.SubscriptionComponents` (`operations/SubscriptionComponents.md`)

| Op | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `CreateUsage` | `CreateUsage(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, CreateUsageRequest? body, CancellationToken ct = default)` — `body` must pass | Path unions (`MaxioAdvancedBilling.Models.AnyOf`): `SubscriptionIdOrReference.Int(int)` / `.String(string)`; `ComponentIdModel.Int(int)` / `.String(string)` (string may be `handle:…`). Envelope: `Usage (usage): CreateUsage !req` — `Quantity (quantity): double?`, `Memo`, `PricePointId (price_point_id): string?`. Negative `Quantity` deducts; `unit_balance` floors at 0 (`records-2-Cr-Ne.md`, `unions.md`) | `UsageResponse` → `Usage (usage): Usage !req`. Read: `Id`, `Quantity` (union int/string — `TryGetInt`/`TryGetString`), `ComponentId`, `SubscriptionId`, `Memo` (`records-4-Su-We.md`) | **A** `SdkException<CreateUsageError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `ListUsages` | `ListUsages(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, long? sinceId, long? maxId, DateTimeOffset? sinceDate, DateTimeOffset? untilDate, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 4 params `sinceId`…`untilDate` must pass | metered only (not quantity-based) | `IReadOnlyList<UsageResponse>` | **B** | `page`+`perPage` |
| `AllocateComponent` | `AllocateComponent(int subscriptionId, int componentId, CreateAllocationRequest? body, CancellationToken ct = default)` — `body` must pass | Envelope: `Allocation (allocation): CreateAllocation !req`. **Required**: `Quantity (quantity): double`. Optional: `Memo`, `UpgradeCharge` (`UpgradeChargeCreditType`), `DowngradeCredit` (`DowngradeCreditCreditType`), `AccrueCharge`, `PricePointId` (union `PricePointId1`) (`records-1-Ac-Cr.md`) | `AllocationResponse` → `Allocation (allocation): Allocation?`. Read: `AllocationId`, `ComponentId`, `Quantity` (union), `Memo` | **A** `SdkException<AllocateComponentError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` | none |
| `ListSubscriptionComponents` | `ListSubscriptionComponents(int subscriptionId, SubscriptionListDateField? dateField, SortingDirection? direction, ListSubscriptionComponentsFilter? filter, string? endDate, string? endDatetime, IncludeNotNull? pricePointIds, IReadOnlyList<int>? productFamilyIds, ListSubscriptionComponentsSort? sort, string? startDate, string? startDatetime, IReadOnlyList<ListSubscriptionComponentsInclude>? include, bool? inUse, CancellationToken ct = default)` — 12 params `dateField`…`inUse` must pass | — | `IReadOnlyList<SubscriptionComponentResponse>` → `.Component (component): SubscriptionComponent?`. Read: `ComponentId`, `Name`, `Kind`, `AllocatedQuantity` (union), `UnitBalance`, `Enabled`, `PricePointId` (`records-3-Of-Su.md`) | **B** | none |
| `ReadSubscriptionComponent` | `ReadSubscriptionComponent(int subscriptionId, int componentId, CancellationToken ct = default)` | — | `SubscriptionComponentResponse` | **A** `SdkException<ReadSubscriptionComponentError>`: `TryGetNoContent` [404] · `TryGetRawError` | none |

#### Invoices — `client.Invoices` (`operations/Invoices.md`)

| Op | Signature | Request | Response | Error | Pagination |
|---|---|---|---|---|---|
| `ListInvoices` | `ListInvoices(string? startDate, string? endDate, InvoiceStatus? status, int? subscriptionId, string? subscriptionGroupUid, string? consolidationLevel, Direction? direction, InvoiceDateField? dateField, string? startDatetime, string? endDatetime, IReadOnlyList<int>? customerIds, IReadOnlyList<string>? number, IReadOnlyList<int>? productIds, InvoiceSortField? sort, int? page = 1, int? perPage = 20, bool? lineItems = false, bool? discounts = false, bool? taxes = false, bool? credits = false, bool? payments = false, bool? customFields = false, bool? refunds = false, CancellationToken ct = default)` — 14 params `startDate`…`sort` must pass | Breakdown arrays **omitted unless** the matching bool is `true` | `ListInvoicesResponse` → `Invoices (invoices): IReadOnlyList<Invoice> !req` (**not** `InvoiceResponse` wrappers). Read: `Uid`, `Number`, `Status`, `SubscriptionId`, `CustomerId`, `TotalAmount`, `DueAmount`, `PaidAmount`, `IssueDate`, `DueDate`, `PublicUrl` (`records-2-Cr-Ne.md`) | **B** `SdkException<RawError>` | `page`+`perPage` |
| `ReadInvoice` | `ReadInvoice(string uid, CancellationToken ct = default)` | uid string (not int) | `Invoice` **directly** (no envelope). Same fields; detail arrays present on read | **B** | none |

`CreateInvoice` returns `InvoiceResponse` (`Invoice` wrapped). `IssueInvoice` / `ReadInvoice` / payment-record ops return bare `Invoice`. Do not assume one envelope for all invoice ops.

### Enums in scope (`map/models/enums.md`)

| Enum | Members (C# identifier (wire)) |
|---|---|
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` |
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `SubscriptionStateFilter` | `Active`, `Canceled`, `Expired`, `ExpiredCards`, `OnHold`, `PastDue`, `PendingCancellation`, `PendingRenewal`, `Suspended`, `TrialEnded`, `Trialing`, `Unpaid` (wires = snake of those names except `expired_cards`, `on_hold`, `pending_cancellation`, `pending_renewal`, `trial_ended`) |
| `SubscriptionInclude` | `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)` |
| `SubscriptionListInclude` | `SelfServicePageToken (self_service_page_token)` |
| `SubscriptionSort` | `SignupDate (signup_date)`, `PeriodStart (period_start)`, `PeriodEnd (period_end)`, `NextAssessment (next_assessment)`, `UpdatedAt (updated_at)`, `CreatedAt (created_at)`, `TotalPayments (total_payments)`, `Id (id)`, `OpenBalance (open_balance)`, `ExpiresAt (expires_at)` |
| `SubscriptionDateField` | `CurrentPeriodEndsAt (current_period_ends_at)`, `CurrentPeriodStartsAt (current_period_starts_at)`, `CreatedAt`, `ActivatedAt`, `CanceledAt`, `ExpiresAt`, `TrialStartedAt`, `TrialEndedAt`, `UpdatedAt` |
| `SortingDirection` | `Asc (asc)`, `Desc (desc)` |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` |
| `IntervalUnit` | `Day (day)`, `Month (month)` |
| `ComponentKind` | `MeteredComponent (metered_component)`, `QuantityBasedComponent (quantity_based_component)`, `OnOffComponent (on_off_component)`, `PrepaidUsageComponent (prepaid_usage_component)`, `EventBasedComponent (event_based_component)` |
| `InvoiceStatus` | `Draft (draft)`, `Open (open)`, `Paid (paid)`, `Pending (pending)`, `Voided (voided)`, `Canceled (canceled)`, `Processing (processing)` |
| `InvoiceDateField` | `CreatedAt (created_at)`, `DueDate (due_date)`, `IssueDate (issue_date)`, `UpdatedAt (updated_at)`, `PaidDate (paid_date)` |
| `InvoiceSortField` | `Status`, `TotalAmount (total_amount)`, `DueAmount (due_amount)`, `CreatedAt`, `UpdatedAt`, `IssueDate (issue_date)`, `DueDate (due_date)`, `Number (number)` |
| `Direction` | `Asc (asc)`, `Desc (desc)` |
| `DiscountType` | `Amount (amount)`, `Percent (percent)` |
| `PaymentType` | `CreditCard (credit_card)`, `BankAccount (bank_account)`, `PaypalAccount (paypal_account)`, `ApplePay (apple_pay)` |
| `CreditType` / `UpgradeChargeCreditType` / `DowngradeCreditCreditType` | `Full (full)`, `Prorated (prorated)`, `None (none)` |
| `ReactivationCharge` | `Prorated (prorated)`, `Immediate (immediate)`, `Delayed (delayed)` |
| `ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)` |
| `ListSubscriptionComponentsInclude` | `Subscription (subscription)`, `HistoricUsages (historic_usages)` |
| `ListSubscriptionComponentsSort` | `Id (id)`, `UpdatedAt (updated_at)` |
| `CancellationMethod` (read on `Subscription`) | `MerchantUi (merchant_ui)`, `MerchantApi (merchant_api)`, `Dunning (dunning)`, `BillingPortal (billing_portal)`, `Unknown (unknown)`, `Imported (imported)` |
| `PricingScheme` | `Stairstep (stairstep)`, `Volume (volume)`, `PerUnit (per_unit)`, `Tiered (tiered)` |
| `CompoundingStrategy` | `Compound (compound)`, `FullPrice (full-price)` |
| `RecurringScheme` | `DoNotRecur (do_not_recur)`, `RecurIndefinitely (recur_indefinitely)`, `RecurWithDuration (recur_with_duration)` |
| `CreditCardVault` (testing) | include `Bogus (bogus)` for sandbox |

### Unions in scope (`map/models/unions.md`)

| Union | Namespace | Construct | Read |
|---|---|---|---|
| `SubscriptionIdOrReference` | `Models.AnyOf` | `.Int(int)` / `.String(string)` (implicit from `int`/`string`) | `TryGetInt` / `TryGetString` |
| `ComponentIdModel` | `Models.AnyOf` | `.Int(int)` / `.String(string)` | `TryGetInt` / `TryGetString` |
| `ComponentId1` (create-subscription component) | `Models.AnyOf` | `.Int(int)` / `.String(string)` | `TryGetInt` / `TryGetString` |
| `PaymentProfile` (response) | `Models.OneOf` | factories per variant | `TryGetCreditCardPaymentProfile` / `TryGetBankAccountPaymentProfile` / `TryGetPaypalPaymentProfile` / `TryGetApplePayPaymentProfile` |
| `CancelSubscriptionErrorResponse` | `Models.AnyOf` | — | `TryGetErrorListResponse1` / `TryGetSingleErrorResponse1` |
| `Resume` | `Models.AnyOf` | `.Bool(bool)` / `.ResumeOptions(ResumeOptions)` | `TryGetBool` / `TryGetResumeOptions` |
| `Quantity` / `Quantity1` / `AllocatedQuantity2` | `Models.AnyOf` | `.Int(int)` / `.String(string)` | `TryGetInt` / `TryGetString` |
| `SnapDay1` (update subscription) | `Models.AnyOf` | `.String(string)` / `.Int(int)` | `TryGetString` / `TryGetInt` |

Do **not** `new` a union.

### Hero presentation types (`records-3-Of-Su.md`, `map/models/enums.md`)

| Field | Type | Present as |
|---|---|---|
| `Product.PriceInCents (price_in_cents)` | `long?` | Dollars = `PriceInCents.Value / 100m` (cents, not a decimal on the wire). Same for `Subscription.ProductPriceInCents`. |
| `Subscription.State (state)` | `SubscriptionState?` — `StringEnum<SubscriptionState>` in `MaxioAdvancedBilling.Models.Enums`, **not** a C# enum. Compare to `SubscriptionState.Active` (wire `active`), etc. | Return the wire string via the enum member (e.g. `Active` → `active`) for the PublicApi DTO. |
| `Subscription.NextAssessmentAt (next_assessment_at)` | `DateTimeOffset?` | Next billing instant; prefer this for “next-billing-date”. |
| `Subscription.CurrentPeriodEndsAt (current_period_ends_at)` | `DateTimeOffset?` | Period end (notes: after `next_billing_at` updates, verify via this field on read). |
| `Product.RequireCreditCard (require_credit_card)` | `bool?` | Seeded plans are false — omit payment fields on create. Also `RequestCreditCard (request_credit_card): bool?`. |
| `Product.Handle (handle)` | `string?` | Stable plan key: `eshop-pro`, `basic-plan`. |

---

## Trap notes

⚠ Step 1 (client registration) — the `HttpClient` handed to `MaxioAdvancedBillingClient` (and the handler pipeline behind it) has a lifetime the signature does not document; a per-request client vs a factory-owned one changes socket/DNS behavior under load. `AddMaxioAdvancedBillingClient` registers the SDK client as Singleton over `IHttpClientFactory.CreateClient()`. **MUST load `dotnet-client-initialization`** before writing `new MaxioAdvancedBillingClient` or `AddMaxioAdvancedBillingClient`.

⚠ Step 1 (auth) — Basic credentials must be on the options object the client is built from, and the API key must come from configuration (`Maxio:ApiKey`) rather than source. A client constructed first and “authed later” is not how this options object works. **MUST load `dotnet-authentication`** before wiring `BasicAuthCredentials`.

⚠ Step 1 (resilience) — `RetryOptions.Timeout` / retry lists on `MaxioAdvancedBillingClientOptions` are **not** the timeout on the `HttpClient` you register and do **not** bound a whole logical call the way a storefront “give up after N seconds” usually means. **MUST load `dotnet-configuration-resilience`** before copying `RetryOptions.Default()` or setting `Timeout`.

⚠ Step 1 (BaseUrl vs Site) — `Resolve` selects `Production.Us` vs `.Eu` from `Environment`; a `BaseUrl` set on the wrong node is unused. Whether a verbatim `BaseUrl` still interpolates `{site}` is a wiring mistake that yields the wrong host. **MUST load `dotnet-configuration-resilience`** before assigning `Site` / `BaseUrl`.

⚠ Step 5 (subscribe / usage writes) — whether a transport-failed `CreateSubscription` / `CreateUsage` / `AllocateComponent` / `MigrateSubscriptionProduct` can execute more than once is not visible from the method signature; `HttpMethodsToRetry` is not the whole story. **MUST load `dotnet-configuration-resilience`** before registering the client that will POST those operations.

⚠ Steps 2–11 (every call) — list/search operations have long nullable-without-default parameter lists; a positional call binds the wrong arguments. Named arguments and explicit `null` are required. The cancellation token parameter is `ct`. **MUST load `dotnet-calling-endpoints`** before the first `client.{Group}.{Op}(…)`.

⚠ Steps 2–11 (models) — envelopes wrap payloads (`CustomerResponse.Customer`, `SubscriptionResponse.Subscription`, `ProductResponse.Product`); invoice **list** is `ListInvoicesResponse.Invoices` while **read** is a bare `Invoice`; `PaymentProfile` and path ids are unions; enums are `StringEnum<T>`; `required` members (`CreateCustomer.FirstName/LastName/Email`, `CreateSubscriptionRequest.Subscription`, `CreateAllocation.Quantity`, …) must be set in the initializer. Unmodeled JSON is dropped. **MUST load `dotnet-models`** before constructing any request or mapping any response.

⚠ Step 2 (customer 422) — `CustomerErrorResponse1.Errors` is `Errors?` (`PerPage`/`PricePoint`), not a typed “duplicate reference” field; a race on the same `Reference` is a 422 whose payload may not match that model. **MUST load `dotnet-error-handling`** before writing the ensure-customer catch (re-read by reference rather than parsing the 422 as the only signal).

⚠ Step 8 (cancel error) — `TryGetCancelSubscriptionErrorResponse` yields a **union**, not a flat string list; a catch that only reads `ErrorListResponse1` misses the `SingleErrorResponse1` variant. **MUST load `dotnet-models`** and **`dotnet-error-handling`** before writing that catch.

⚠ Step 12 (error boundary) — Case A vs Case B is per-operation (this sheet’s Error column); `TryGetRawError` is inherited on typed errors but is not a catch-all for Case B ops; there are no no-throw variants. **MUST load `dotnet-error-handling`** before any `try/catch` around an SDK call.

⚠ Step 12 (error boundary) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 12 (error boundary) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 12 (tests) — the seam to fake is the `HttpClient` constructor argument, not the generated controller types. **MUST load `dotnet-testing`** before writing integration-layer tests.

---

## REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — constructing/registering `MaxioAdvancedBillingClient` and `HttpClient` lifetime |
| `dotnet-authentication` | Step 1 — `BasicAuthCredentials` (API key + `"x"`) from configuration |
| `dotnet-configuration-resilience` | Step 1 — `RetryOptions` / `Timeout` / server `Site`+`BaseUrl`; Step 5 — POST retry/idempotency hazard |
| `dotnet-calling-endpoints` | Steps 2–11 — named arguments, `ct:`, must-pass nullables, async calls |
| `dotnet-models` | Steps 2–11 — envelopes, `required`, unions/`TryGet…`, `StringEnum<T>`, mapping |
| `dotnet-error-handling` | Step 12 — Case A/B, `TryGet…`, both `JsonException` directions (2xx drift **and** non-2xx mismatch that destroys status) |
| `dotnet-testing` | Step 12 — `HttpClient` test seam |

---

## Assumptions & Blockers

**Assumptions**

- v1 is the three PublicApi JWT endpoints only; unused sheet ops stay for later, not for this cut.
- eShop buyer/user identity maps to Maxio `Customer.Reference` (application user id). Ensure-customer: `ReadCustomerByReference` → `CreateCustomer` → on 422 re-read (reference unique).
- Catalog is seeded: family `eshop-subscribe`, products `eshop-pro` ($299/mo) and `basic-plan` ($29/mo), component `api-call` unused in hero. Handles are stable; do not persist numeric IDs.
- Seeded products: no trial, no setup fee, expires never, taxable no, **`RequireCreditCard = false`**. `CreateSubscription` omits all payment-profile / card / Chargify.js fields.
- Default POST target is `eshop-pro` unless the request names `basic-plan`.
- Config from `Maxio:` / env only (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, optional `BaseUrl`, `MAXIO_ENVIRONMENT` → `US`/`EU`). No hardcoded secrets or hosts.
- `GET /api/my-subscriptions` uses `ListCustomerSubscriptions`, not `FindSubscription`.
- POST double-click: after ensure-customer, if `ListCustomerSubscriptions` already has a live sub for that `Product.Handle`, return it; else `CreateSubscription`. Optional extra: set `CreateSubscription.Reference` to `{userId}:{productHandle}` and `FindSubscription` first.
- Single-customer (no `SubscriptionGroups`). Invoice/coupon/migrate/cancel unused in v1 hero.
- Sandbox is the Chargify **test site** (subdomain), still `ServerEnvironment.Us` or `.Eu`.

**Blockers**

- Whether a duplicate-`reference` 422 body actually populates `CustomerErrorResponse1.Errors` (`PerPage`/`PricePoint`) is **UNVERIFIED** (live wire). Defensive: on any 422 from `CreateCustomer`, `ReadCustomerByReference` and proceed if found; generic conflict if still missing.
- Whether two concurrent `CreateSubscription` calls for the same customer+handle can both succeed (no unique subscription-reference constraint unless you set `Reference`) is **UNVERIFIED**. Defensive: `ListCustomerSubscriptions` before create **and** set a stable `Subscription.Reference`; if create still 422, list again and return the existing row.
- Whether `RetryOptions.Default()` transport failures re-send POST (`CreateSubscription`) is **not** answered by `HttpMethodsToRetry` alone — **MUST load `dotnet-configuration-resilience`**. Combine with the List-before-create guard.
