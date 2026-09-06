# Maxio Advanced Billing — integration plan for eShopOnWeb (`src/PublicApi`)

Scope: additive recurring-subscription billing alongside the existing Catalog→Basket→Order flow.
Every contract fact below is grounded in the bundled SDK map (page cited per row) or, where the map
truncates, in the SDK source file the map row names. Package `AsadAli.AdvancedBilling.Sdk`, SDK
`v1.0.2` (map stamp commit `15db14b`).

---

## 1. Scope & sequence

| # | Step | Maxio operations used |
|---|---|---|
| 1 | Bind `Maxio:*` configuration; register the SDK client (auth + subdomain + optional verbatim base URL) | — (client construction only) |
| 2 | Resolve `Maxio:ProductFamilyHandle` → numeric family id (handles are stable, ids are not) | `client.ProductFamilies.ListProductFamilies` |
| 3 | `GET /api/subscription-plans` — list purchasable products in that family, excluding archived | `client.ProductFamilies.ListProductsForProductFamily`, `client.Sites.ReadSite` (currency) |
| 4 | Find-or-create the Maxio customer for the authenticated caller, keyed on a stable reference | `client.Customers.ReadCustomerByReference`, `client.Customers.CreateCustomer` |
| 5 | `POST /api/subscriptions` — idempotency pre-check, then subscribe by product **handle** | `client.Customers.ListCustomerSubscriptions`, `client.Subscriptions.CreateSubscription` |
| 6 | `GET /api/my-subscriptions` — the caller's subscriptions | `client.Customers.ListCustomerSubscriptions` |
| 7 | Error boundary translating SDK exceptions to HTTP results | — (see §2.5, §4) |

Step 2 exists because **no operation lists products by family handle**. `ListProductsForProductFamily`
takes `string productFamilyId`; `ReadProductFamily` takes `int id` — so the `handle:my-family`
addressing its Notes mention cannot be expressed through that C# signature. Resolve the id by listing
families and matching `Handle` (never hard-code the id).

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

### 2.0 Namespaces (`using` directives you will need)

| Types | Namespace | Source |
|---|---|---|
| `MaxioAdvancedBillingClient`, `MaxioAdvancedBillingClientOptions`, `ServerOptions`, `ServiceCollectionExtensions` | `MaxioAdvancedBilling` | `sdk-map.md` (Namespaces table); root-level files `MaxioAdvancedBillingClient.cs`, `MaxioAdvancedBillingClientOptions.cs`, `ServerOptions.cs` |
| `ServerEnvironment`, `ProductionOptions` (+ nested `UsOptions`/`EuOptions`), `EbbOptions` | `MaxioAdvancedBilling.Servers` | `sdk-map.md` "Servers & auth"; `Servers/ServerEnvironment.cs`, `Servers/ProductionOptions.cs` |
| `BasicAuthCredentials` | `MaxioAdvancedBilling.Core.Authentication.Basic` | `sdk-map.md` "Getting a client" |
| `RetryOptions`, `RetryAttempt` | `MaxioAdvancedBilling.Core.Configuration` | `sdk-map.md` (RetryOptions table) |
| `SdkException<TError>` | `MaxioAdvancedBilling.Core.Exceptions` | `sdk-map.md` (`Core/Exceptions/SdkException.cs`) |
| `ApiError`, `RawError` | `MaxioAdvancedBilling.Core.ErrorResponse` | `sdk-map.md` (error-core table) |
| Controller classes (`Customers`, `Products`, `ProductFamilies`, `Subscriptions`, `Sites`) — needed only if you declare a field of the controller type rather than using `var` | `MaxioAdvancedBilling.Api` | `sdk-map.md` (Namespaces table) |
| All records (`Product`, `Customer`, `Subscription`, `CreateSubscriptionRequest`, …) | `MaxioAdvancedBilling.Models` | `map/models/records-*.md` header |
| All enums (`SubscriptionState`, `IntervalUnit`, `SubscriptionStateFilter`, `CollectionMethod`, …) | `MaxioAdvancedBilling.Models.Enums` | `map/models/enums.md` header |
| Typed errors (`CreateCustomerError`, `CreateSubscriptionError`, `ListProductsForProductFamilyError`) | `MaxioAdvancedBilling.Errors` | `sdk-map.md` (Namespaces table) |

### 2.1 Client construction & auth (step 1)

| Fact | Value | Source |
|---|---|---|
| NuGet package id | `AsadAli.AdvancedBilling.Sdk` (differs from the namespace) | `sdk-map.md` |
| Root namespace | `MaxioAdvancedBilling` | `sdk-map.md` |
| SDK version pinned by this map | `v1.0.2` (commit `15db14b`) | `sdk-map.md` |
| Client type | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` — `sealed`; **single** constructor `MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `sdk-map.md`; `MaxioAdvancedBillingClient.cs` |
| Options type | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` — properties: `Environment: ServerEnvironment` (default `ServerEnvironment.Default()` = `Us`), `Retry: RetryOptions` (default `RetryOptions.Default()`), `Server: ServerOptions` (default `new()`), `BasicAuth: BasicAuthCredentials?` (default `null`) | `sdk-map.md`; `MaxioAdvancedBillingClientOptions.cs` |
| Auth | `options.BasicAuth = new BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }`. `BasicAuthCredentials` has exactly two members, both `required string`: `Username`, `Password`. HTTP Basic; **username = API key, password = the literal `"x"`** | `sdk-map.md` "Servers & auth"; `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Environment enum | `MaxioAdvancedBilling.Servers.ServerEnvironment` — a `StringEnum`, **not** a C# enum. Exactly two members: `ServerEnvironment.Us` (wire `US`, default) and `ServerEnvironment.Eu` (wire `EU`). Also `ServerEnvironment.Default()` ⇒ `Us` | `sdk-map.md`; `Servers/ServerEnvironment.cs` |
| **There is no sandbox/production environment member** | `ServerEnvironment` has only `Us` and `Eu`. Sandbox targeting is done purely by **subdomain** (or by the verbatim base-URL override), not by an environment value. For site `cp-exp-1`: `Environment = Us` + `Site = "cp-exp-1"` | `sdk-map.md`; `Servers/ServerEnvironment.cs` |
| Set the site subdomain | `options.Server.Production.Us.Site = <Maxio:Subdomain>` (set `.Eu.Site` instead when `Environment = Eu`) | `sdk-map.md` "Servers & auth"; `Servers/ProductionOptions.cs` |
| Subdomain **default is the literal string `"subdomain"`** | `UsOptions.Site` defaults to `"subdomain"` and `UsOptions.BaseUrl` to `"https://{site}.chargify.com"`; an unbound `Maxio:Subdomain` therefore silently targets `https://subdomain.chargify.com` instead of failing fast — validate the bound value at startup | `Servers/ProductionOptions.cs` |
| **Verbatim base-URL override — SUPPORTED** | `options.Server.Production.Us.BaseUrl = <Maxio:BaseUrl>`. `BaseUrl` is a plain `string` template; `{site}` is substituted **only if the string contains it**, so a verbatim URL with no placeholder passes through unchanged (a trailing `/` is trimmed). Set it on the branch matching `Environment` — `ProductionOptions.Resolve` reads `Us.*` when `Environment == Us` and `Eu.*` when `Environment == Eu`; the other branch is ignored. When `Maxio:BaseUrl` is set, `Site` becomes unused (unless the operator's URL still contains `{site}`) | `sdk-map.md` "Servers & auth"; `Servers/ProductionOptions.cs`, `Core/TemplateParamsFactory.cs` |
| Environment × custom URL interaction | `Environment` chooses the **branch** (`Us` vs `Eu`); `BaseUrl` on that branch chooses the **host**. They are not alternatives — you always set both, and setting `BaseUrl` does not change which branch is read | `Servers/ProductionOptions.cs` |
| Ebb server group | A second group (`options.Server.Ebb.*`) exists for event-ingest endpoints only. **No operation in this plan uses it** — leave it at its default | `sdk-map.md` "Servers & auth" |
| DI registration (SDK-provided) | `services.AddMaxioAdvancedBillingClient(o => { … })` in namespace `MaxioAdvancedBilling`. It calls `services.AddHttpClient()`, then registers `MaxioAdvancedBillingClient` as a **singleton** built from `IHttpClientFactory.CreateClient()` | `sdk-map.md`; `ServiceCollectionExtensions.cs` |
| DI registration — **options are evaluated eagerly** | The `configure` callback runs **at registration time**, not from the service provider. You cannot resolve `IOptions<T>`/`IConfiguration` *inside* it from DI — capture the already-bound configuration values in the closure, or register the client yourself: `services.AddSingleton(sp => new MaxioAdvancedBillingClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("maxio"), options))` | `ServiceCollectionExtensions.cs` |
| DI registration — compiler note | `AddMaxioAdvancedBillingClient` is declared inside a C# 14 `extension(IServiceCollection services)` block. If the consuming project's toolchain does not bind it as an extension method, fall back to the manual `AddSingleton(...)` registration above — the client constructor is public and is the same thing the extension calls | `ServiceCollectionExtensions.cs` |
| Thread safety / lifetime | The client is `sealed`; every controller property is assigned once in the constructor and nothing is mutated afterwards, and the SDK's own DI extension registers it as a **singleton** — register one long-lived instance and reuse it | `MaxioAdvancedBillingClient.cs`, `ServiceCollectionExtensions.cs` |
| Retry/timeout knobs | `options.Retry` is a `RetryOptions` (namespace `MaxioAdvancedBilling.Core.Configuration`) with `StatusCodesToRetry: IReadOnlyList<HttpStatusCode>`, `HttpMethodsToRetry: IReadOnlyList<HttpMethod>`, `MaxRetries: int`, `Delay: TimeSpan`, `Timeout: TimeSpan?`, `BackOffFactor: int`, `UseExponentialBackoff: bool`, `MaxJitter: TimeSpan`, `OnRetry: Action<RetryAttempt>?`. **All members are `required`** — either start from `RetryOptions.Default()` and copy-with, or set every member. See the trap note in §3 before tuning | `sdk-map.md` (RetryOptions table) |
| Configuration keys to bind | `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` (optional). Bind by these key paths through the configuration/options system — do not read raw environment variables. The SDK documents no defaults for any of them; the only SDK-side defaults are the `Site`/`BaseUrl` values in the row above | brief + `Servers/ProductionOptions.cs` |

### 2.2 Operations

| # | Operation | Signature (verbatim, params in order) | Request model & fields | Response envelope → payload | Error case & accessors | Pagination | Source |
|---|---|---|---|---|---|---|---|
| A | `client.ProductFamilies.ListProductFamilies` — resolve family handle → id | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 filters are **nullable with no default → must be passed explicitly** (pass `null`) | none (GET) | `Task<IReadOnlyList<ProductFamilyResponse>>`; each item: `ProductFamilyResponse.ProductFamily (product_family): ProductFamily?` → `ProductFamily.Id (id): int?`, `Handle (handle): string?`, `Name (name): string?`, `ArchivedAt (archived_at): DateTimeOffset?` | **Case B** — `SdkException<RawError>`; `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`, `ReadAsBytes()` | **none** (no `page`/`perPage` params at all) | `operations/ProductFamilies.md`, `records-3-Of-Su.md` |
| B | `client.ProductFamilies.ListProductsForProductFamily` — plans list | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 8 params `dateField`…`include` are **nullable with no default → must be passed explicitly**; `page`/`perPage` default to `1`/`20` | none (GET). `productFamilyId` is a **string** — pass the numeric id from step A as a string. `includeArchived: false` excludes archived; `filter` is `ListProductsFilter { Ids (ids): IReadOnlyList<int>?, PrepaidProductPricePoint (prepaid_product_price_point): PrepaidProductPricePointFilter?, UseSiteExchangeRate (use_site_exchange_rate): bool? }` | `Task<IReadOnlyList<ProductResponse>>`; **each item is a wrapper** — `ProductResponse.Product (product): Product` is `required`, read `item.Product` | **Case A** — `SdkException<ListProductsForProductFamilyError>`; `TryGetString(out string)` **[404]** · `TryGetRawError(out RawError)` [fallback] | manual `page` + `perPage` (defaults 1 / 20) — loop until a page returns fewer than `perPage` items | `operations/ProductFamilies.md`, `records-3-Of-Su.md`, `records-2-Cr-Ne.md` |
| C | `client.Sites.ReadSite` — site currency for the plans list | `ReadSite(CancellationToken ct = default)` | none | `Task<SiteResponse>`; `SiteResponse.Site (site): Site` is `required` → `Site.Currency (currency): string?`, `Subdomain (subdomain): string?`, `Test (test): bool?`, `NonPrimaryCurrencies (non_primary_currencies): IReadOnlyList<string>?`, `DefaultPaymentCollectionMethod (default_payment_collection_method): string?` | **Case B** — `SdkException<RawError>` | none | `operations/Sites.md`, `records-3-Of-Su.md` |
| D | `client.Customers.ReadCustomerByReference` — idempotent lookup | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — query param `reference` ← `reference`; HTTP `GET /customers/lookup.json` | none | `Task<CustomerResponse>`; `CustomerResponse.Customer (customer): Customer` is `required` → see §2.3 | **Case B** — `SdkException<RawError>`; status via `ex.Error.StatusCode`, body via `ex.Error.ReadAsString()` | none | `operations/Customers.md`, `records-1-Ac-Cr.md` |
| E | `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` is **nullable with no default → must be passed explicitly** | `CreateCustomerRequest { Customer (customer): CreateCustomer }` — `Customer` is `required`. `CreateCustomer` **required**: `FirstName (first_name): string`, `LastName (last_name): string`, `Email (email): string`. Optional but load-bearing here: `Reference (reference): string?` (the idempotency key). Other optionals: `CcEmails`, `Organization`, `Address`, `Address2`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `VatNumber` (all `string?`), `TaxExempt (tax_exempt): bool?`, `TaxExemptReason: string?`, `ParentId (parent_id): int?`, `SalesforceId: string?` | `Task<CustomerResponse>` → `.Customer` | **Case A** — `SdkException<CreateCustomerError>`; `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` **[422]** · `TryGetRawError(out RawError)` [fallback]. ⚠ see §2.5 — the 422 payload shape carries no usable message | none | `operations/Customers.md`, `records-1-Ac-Cr.md` |
| F | `client.Customers.ListCustomerSubscriptions` — idempotency pre-check **and** `GET /api/my-subscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — HTTP `GET /customers/{customer_id}/subscriptions.json` | none | `Task<IReadOnlyList<SubscriptionResponse>>`; each item: `SubscriptionResponse.Subscription (subscription): Subscription?` — **optional/nullable, unlike the other envelopes** → null-check before dereferencing | **Case B** — `SdkException<RawError>` | **none** — the signature has no `page`/`perPage`. See §5 (UNVERIFIED) | `operations/Customers.md`, `records-4-Su-We.md` |
| G | `client.Subscriptions.CreateSubscription` — `POST /api/subscriptions` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` is **nullable with no default → must be passed explicitly** | `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription }` — `Subscription` is `required`. See §2.4 for the field selection | `Task<SubscriptionResponse>` → `.Subscription` (**nullable** — null-check) | **Case A** — `SdkException<CreateSubscriptionError>`; `TryGetErrorListResponse1(out ErrorListResponse1)` **[422]** · `TryGetRawError(out RawError)` [fallback]. ⚠ see §2.5 | none | `operations/Subscriptions.md`, `records-2-Cr-Ne.md`, `records-4-Su-We.md` |
| H | *(alternative, NOT used)* `client.Subscriptions.ListSubscriptions` | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 14 params `state`…`include` are **nullable with no default → must be passed explicitly** | none (GET) | `Task<IReadOnlyList<SubscriptionResponse>>` | **Case B** — `SdkException<RawError>` | manual `page` + `perPage` (1 / 20) | `operations/Subscriptions.md` |

**Why H is not used for the idempotency check:** `ListSubscriptions` has **no customer filter** — its filters are `state` (`SubscriptionStateFilter`), `product` (**`int?`** — a numeric product id, not a handle), `productPricePointId`, `coupon`, `couponCode`, date fields, `metadata`, `direction`, `sort`, `include`. Filtering by customer is only available through operation **F** (`ListCustomerSubscriptions(int customerId, …)`). Source: `operations/Subscriptions.md`, `operations/Customers.md`.

Also available if ever needed: `client.Products.ReadProductByHandle(string apiHandle, CancellationToken ct = default)` → `Task<ProductResponse>`, **Case B** (`operations/Products.md`) — resolves a single product by handle without the family. Not on the hot path here because step 3 already lists the family.

### 2.3 Model fields the endpoints read

**`Product`** (namespace `MaxioAdvancedBilling.Models`; source `records-2-Cr-Ne.md`) — every field is nullable:

| Purpose | C# property (wire name): CLR type |
|---|---|
| id | `Id (id): int?` |
| handle | `Handle (handle): string?` |
| name | `Name (name): string?` |
| description | `Description (description): string?` |
| price in cents | `PriceInCents (price_in_cents): long?` |
| interval (count) | `Interval (interval): int?` |
| interval unit | `IntervalUnit (interval_unit): IntervalUnit?` — enum, members **`Day` (`day`)** and **`Month` (`month`)** only |
| archived-at | `ArchivedAt (archived_at): DateTimeOffset?` — non-null ⇒ archived |
| card requirement (precondition for card-less subscribe) | `RequestCreditCard (request_credit_card): bool?`, `RequireCreditCard (require_credit_card): bool?` |
| expiry config | `ExpirationInterval (expiration_interval): int?`, `ExpirationIntervalUnit (expiration_interval_unit): ExpirationIntervalUnit?` — members `Day` (`day`), `Month` (`month`), `Never` (`never`) |
| trial config | `TrialPriceInCents (trial_price_in_cents): long?`, `TrialInterval (trial_interval): int?`, `TrialIntervalUnit (trial_interval_unit): IntervalUnit?` |
| taxability | `Taxable (taxable): bool?` |
| owning family | `ProductFamily (product_family): ProductFamily?` |
| price point | `ProductPricePointId (product_price_point_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?`, `ProductPricePointName (product_price_point_name): string?`, `DefaultProductPricePointId (default_product_price_point_id): int?` |

⚠ **`Product` has NO currency field.** Currency for `GET /api/subscription-plans` comes from `Site.Currency (currency): string?` via operation **C** (`ReadSite`) — one call, cached for the process. (Per-price-point multi-currency lives on `ProductPricePoint.CurrencyPrices (currency_prices): IReadOnlyList<CurrencyPrice>?`, where `CurrencyPrice.Currency (currency): string?` / `Price (price): double?`, reachable via the `ProductPricePoints` controller — out of scope unless the site is multi-currency.) Source: `records-2-Cr-Ne.md`, `records-3-Of-Su.md`.

**`Customer`** (source `records-1-Ac-Cr.md`) — fields used: `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `CreatedAt (created_at): DateTimeOffset?`. All nullable.

**`Subscription`** (source `records-4-Su-We.md`) — fields the responses must carry:

| Purpose | C# property (wire name): CLR type |
|---|---|
| subscription id | `Id (id): int?` |
| state | `State (state): SubscriptionState?` — `StringEnum`, **not** a C# enum |
| **next billing date (authoritative — see below)** | `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` |
| next assessment | `NextAssessmentAt (next_assessment_at): DateTimeOffset?` |
| period start | `CurrentPeriodStartedAt (current_period_started_at): DateTimeOffset?` |
| price actually billed | `ProductPriceInCents (product_price_in_cents): long?`, `CurrentBillingAmountInCents (current_billing_amount_in_cents): long?` |
| plan (nested product) | `Product (product): Product?` → `.Name`, `.Handle`, `.PriceInCents`, `.Interval`, `.IntervalUnit` (all as in the `Product` table above) |
| customer | `Customer (customer): Customer?` → `.Id (id): int?` |
| created | `CreatedAt (created_at): DateTimeOffset?` |
| currency | `Currency (currency): string?` |
| collection method | `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` |
| previous state | `PreviousState (previous_state): SubscriptionState?` |
| price point | `ProductPricePointId (product_price_point_id): int?`, `ProductPricePointType (product_price_point_type): PricePointType?` |
| your reference | `Reference (reference): string?` |

**Which next-billing field is authoritative → `CurrentPeriodEndsAt`.** Evidence, all map-side: Maxio's own prose on `UpdateSubscription` states that after setting `next_billing_at` "the server response will not return data under the key/value pair of `next_billing_at`. View the key/value pair of `current_period_ends_at` to verify that the `next_billing_at` date has been changed successfully" (`operations/Subscriptions.md`), and `SubscriptionDateField` exposes `CurrentPeriodEndsAt (current_period_ends_at)` as a filterable date field while offering no `next_assessment_at` member (`enums.md`). Exact C# property and type: **`CurrentPeriodEndsAt`, `System.DateTimeOffset?`**. Defensive directive: render `CurrentPeriodEndsAt`, and when it is null fall back to `NextAssessmentAt` before showing "unknown"; never throw on either being null. Whether the two ever diverge on live sandbox data is `UNVERIFIED`.

**Rendering a `StringEnum` as a string.** `SubscriptionState`, `IntervalUnit`, `CollectionMethod` etc. derive from `TypedEnum<string, T>`, which exposes `public TValue Value { get; init; }`, overrides `ToString()` to return that value, offers `IsKnownValue()`, and defines an implicit conversion to `string`. Render with `sub.State?.Value` (null-safe; yields the wire value, e.g. `"active"`). Build one with `SubscriptionState.Active` or `SubscriptionState.FromValue("active")`. Source: `enums.md` header; `Core/Enum/TypedEnum.cs`, `Core/Enum/StringEnum.cs`.

### 2.4 `CreateSubscription` — exact field selection (step 5)

`CreateSubscriptionRequest` marks only its envelope member required: `Subscription (subscription): CreateSubscription` is `required`. **`CreateSubscription` itself marks NOTHING required** — `required?` selects nothing for you, so the fields below are chosen from the operation's Notes, not from the required flags.

Set these (all on `MaxioAdvancedBilling.Models.CreateSubscription`):

| Field | Type | Why (from the operation Notes) |
|---|---|---|
| `ProductHandle (product_handle): string?` | `string?` | Notes: "Specify the product with `product_id` or `product_handle`." Use the handle — ids are not stable |
| `CustomerId (customer_id): int?` | `int?` | Notes: "Identify an existing customer with `customer_id` or `customer_reference`." Use the id returned by step 4 |

Deliberately **left unset**, each named by the Notes:

| Field | Why omitted |
|---|---|
| `ProductId (product_id): int?` | Redundant with `ProductHandle`; ids are not stable |
| `CustomerReference (customer_reference): string?` | Redundant with `CustomerId` (either identifies the customer) |
| `ProductPricePointHandle (product_price_point_handle): string?`, `ProductPricePointId (product_price_point_id): int?` | Notes: "To set a specific product price point" — not required; omitting uses the product's default price point |
| `PaymentProfileId (payment_profile_id): int?`, `CreditCardAttributes (credit_card_attributes): PaymentProfileAttributes?`, `PaymentProfileAttributes (payment_profile_attributes): PaymentProfileAttributes?`, `BankAccountAttributes (bank_account_attributes): BankAccountAttributes?` | **This is how "no payment profile" is expressed** — omit them entirely. Notes: "Optionally, include an existing payment profile using `payment_profile_id`"; "Payment information **may** be required … depending on the options for the Product" |
| `CustomerAttributes (customer_attributes): CustomerAttributes?` | Notes: "To create a new customer, pass customer_attributes" — we create the customer separately in step 4 so the reference-keyed idempotency is explicit |
| `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` | Leaving it null lets the site default apply (`Site.DefaultPaymentCollectionMethod`). Enum members if you ever set it: `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` |
| `CouponCode`/`CouponCodes`, `Components (components): IReadOnlyList<CreateSubscriptionComponent>?`, `CalendarBilling`, `Metafields`, `OfferId` (union), `PrepaidConfiguration`, `NextBillingAt`, `InitialBillingAt`, `ExpiresAt`, `Currency (currency): string?`, `AgreementAcceptance`, `AchAgreement`, `SkipBillingManifestTaxes` | Not needed by the hero flow. `Currency` unset ⇒ site currency. The metered `api-call` component is not attached at signup |
| `DeferSignup (defer_signup): bool? = false`, `DunningCommunicationDelayEnabled (dunning_communication_delay_enabled): bool? = false` | Have explicit generated defaults — leave them |

Card-less precondition check: before calling, read the chosen plan's `RequestCreditCard`/`RequireCreditCard` from step 3 and reject with a clear 4xx if either is `true` — the Notes make the payment requirement a per-product option, and the sandbox catalog states both plans need no payment method. Source: `operations/Subscriptions.md`, `records-2-Cr-Ne.md`.

### 2.5 Errors — exact hierarchy and payload shapes (step 7)

| Fact | Value | Source |
|---|---|---|
| The only exception the SDK throws for API failures | `MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>` — declared `public sealed class SdkException<TError> : Exception` with **exactly one member**: `public required TError Error { get; init; }` | `sdk-map.md`; `Core/Exceptions/SdkException.cs` |
| **There is NO non-generic base and NO `ApiException`** | `SdkException<T>` derives straight from `System.Exception` and is `sealed`, so `catch (SdkException<RawError>)` does **not** catch `SdkException<CreateSubscriptionError>`. Every closed generic in scope needs its own `catch` clause, or you catch `Exception` and type-test | `Core/Exceptions/SdkException.cs` |
| **The exception carries no status code and no message** | `SdkException<T>` sets no `Message` and exposes no `StatusCode`. The HTTP status is only reachable through the error object: Case B ⇒ `ex.Error.StatusCode`; Case A ⇒ `ex.Error.TryGetRawError(out var raw)` then `raw.StatusCode` | `Core/Exceptions/SdkException.cs`, `Core/ErrorResponse/RawError.cs` |
| `RawError` members | `StatusCode: System.Net.HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` · `ReadAsBytes(): ReadOnlyMemory<byte>` | `sdk-map.md` (error-core table) |
| `ApiError` (base of every typed error) | one member: `TryGetRawError(out RawError error): bool` | `sdk-map.md` (error-core table) |
| Case A ops in this plan | `ListProductsForProductFamily` → `ListProductsForProductFamilyError`: `TryGetString(out string)` **[404]**, `TryGetRawError` [fallback] · `CreateCustomer` → `CreateCustomerError`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` **[422]**, `TryGetRawError` · `CreateSubscription` → `CreateSubscriptionError`: `TryGetErrorListResponse1(out ErrorListResponse1)` **[422]**, `TryGetRawError` | `operations/ProductFamilies.md`, `operations/Customers.md`, `operations/Subscriptions.md` |
| Case B ops in this plan | `ListProductFamilies`, `ReadSite`, `ReadCustomerByReference`, `ListCustomerSubscriptions` → all `SdkException<RawError>` | same pages |
| **422 shape — `CreateSubscription`** | `ErrorListResponse1 { Errors (errors): IReadOnlyList<string> }` — **`required`**. A flat list of message strings | `records-2-Cr-Ne.md`; `Models/ErrorListResponse1.cs` |
| **422 shape — `CreateCustomer`** | `CustomerErrorResponse1 { Errors (errors): Errors? }` where `Errors { PerPage (per_page): IReadOnlyList<string>?, PricePoint (price_point): IReadOnlyList<string>? }` — **the generated 422 payload for customer creation models only `per_page` and `price_point`.** Both properties are optional, so deserialization will not throw; it will simply produce an `Errors` with both members null and no message. Trust judgement (evidence: the two generated definitions plainly disagree with the endpoint's purpose, and `Errors` is a shared model): **do not rely on this accessor for the message** — call it, and when it yields nothing usable fall back to `TryGetRawError(out var raw)` → `raw.StatusCode` + `raw.ReadAsString()`, log the raw body, and surface a generic message | `records-1-Ac-Cr.md`; `Models/CustomerErrorResponse1.cs`, `Models/Errors.cs` |
| 401 shape | For a Case A operation, 401 does **not** match the typed 422/404 branch — it falls to the `_ =>` raw branch, so `TryGetErrorListResponse1`/`TryGetCustomerErrorResponse1`/`TryGetString` return `false` and only `TryGetRawError` yields anything (`StatusCode = Unauthorized`, body via `ReadAsString()`). For a Case B operation it is `ex.Error.StatusCode == HttpStatusCode.Unauthorized`. Treat 401 as a **configuration** failure (`Maxio:ApiKey`, or wrong subdomain/base URL), never as a caller error | `Errors/CreateSubscriptionError.cs`; `sdk-map.md` |
| 404 shape | Only `ListProductsForProductFamily` models 404 typed — `TryGetString(out string)` yields a bare string body (a bad/unknown family id). Everywhere else 404 arrives as `RawError` with `StatusCode = NotFound` | `operations/ProductFamilies.md` |
| **Which operations return null on 404 — none** | The SDK generates **no** no-throw/`Result` variants for any of its 247 operations; every operation throws. The only null-valued outcome in this plan is the **optional** `SubscriptionResponse.Subscription`, which is a deserialization shape, not a 404 signal | `sdk-map.md` ("No-throw variants absent across this SDK"); `records-4-Su-We.md` |

### 2.6 Enum value tables actually needed

**`SubscriptionState`** (`MaxioAdvancedBilling.Models.Enums`) — **full member list**, `CSharpMember (wire_value)`:
`Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)`. Source: `enums.md`, `Models/Enums/SubscriptionState.cs`.

The enum's own doc-comment groups them (source `Models/Enums/SubscriptionState.cs`; the map summary truncates this grouping):

| Group | Members |
|---|---|
| **Live States** | `Active`, `Assessing`, `Pending`, `Trialing`, `Paused` |
| **Problem States** | `PastDue`, `SoftFailure`, `Unpaid` |
| **End of Life States** | `Canceled`, `Expired`, `FailedToCreate`, `OnHold`, `Suspended`, `TrialEnded` |
| Not classified by the doc-comment | `AwaitingSignup` |

For the idempotency pre-check, the SDK's own "Live States" set is the grounded definition of "already subscribed". Two of those five carry an explicit caveat in the same doc-comment — `assessing` and `pending` are described as "internal (transient)" with "Do not base any access decisions in your app on this state, as it may not always be exposed". Treat them as *blocking a second create* (they mean a create is in flight) but not as *granting access*. `AwaitingSignup` is unclassified — decide deliberately (`YOUR CALL — not in the map`).

**`SubscriptionStateFilter`** (the `state` query filter on `ListSubscriptions` — a **different, smaller** enum than `SubscriptionState`): `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)`. It has **no** `pending`, `assessing`, `paused`, `failed_to_create` or `awaiting_signup` member — another reason the customer-scoped, filter-in-memory approach (op F) is the correct pre-check. Source: `enums.md`.

**`IntervalUnit`**: `Day (day)`, `Month (month)`. **`ExpirationIntervalUnit`**: `Day (day)`, `Month (month)`, `Never (never)`. **`CollectionMethod`**: `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)`. **`PricePointType`**: `Catalog (catalog)`, `Default (default)`, `Custom (custom)`. **`BasicDateField`**: `UpdatedAt (updated_at)`, `CreatedAt (created_at)`. **`ListProductsInclude`**: `PrepaidProductPricePoint (prepaid_product_price_point)`. **`SortingDirection`**: `Asc (asc)`, `Desc (desc)`. Source: `enums.md`.

### 2.7 Async naming, cancellation, pagination helpers

| Fact | Value | Source |
|---|---|---|
| Async naming | **There is no `Async` suffix and no synchronous overload.** Every operation is declared `public Task<T> OperationName(…)` (or `public Task` for void ops) — e.g. `public Task<CustomerResponse> CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)`. Call and `await` the name exactly as the map prints it | `Api/Customers.cs`; `operations/*.md` |
| Cancellation token | Always the **last** parameter, literally named **`ct`**, typed `CancellationToken`, defaulted to `default`. In named-argument calls write `ct: cancellationToken` — `cancellationToken:` will not compile. Flow the ASP.NET Core request-aborted token through | `operations/*.md` (page headers) |
| Pagination helpers | **None are wired up.** A `Core/Pagination/PaginationModels.cs` exists in the SDK, but no controller in scope references it; every list operation here is manual `page` + `perPage`, or has no paging parameters at all (ops A and F). Loop yourself: request `page = 1, 2, …` until a page returns fewer than `perPage` items | `operations/*.md` ("Pagination: manual `page`+`perPage`"); `Api/Products.cs`, `Api/Subscriptions.cs` (no pagination references) |
| Optional params without C# defaults | Most list/search operations declare their filters as `T?` **with no default value**, so they must be passed explicitly (pass `null`). Call these with **named arguments** — a positional call silently mis-binds | `operations/*.md` (page headers, per-row "must pass explicitly" notes) |
| Unmodeled JSON | Records model a fixed field set; fields the SDK does not model are dropped on deserialize (this is why the `CustomerErrorResponse1` 422 body yields nothing usable) | `sdk-map.md` model conventions; `Models/Errors.cs` |

---

## 3. Trap notes

⚠ **Step 1 (client registration)** — the SDK's `RetryOptions` (`MaxRetries`, `Delay`, `Timeout`, `HttpMethodsToRetry`, `StatusCodesToRetry`, `UseExponentialBackoff`, `MaxJitter`, `BackOffFactor`) does **not** map onto "how long one `POST /api/subscriptions` can take" the way the names suggest, and it is not the timeout on the `HttpClient` you register. What the SDK re-sends and what it does not — and therefore **whether a failed subscribe write can be executed more than once**, which is precisely the double-click guarantee this feature promises — is decided here, not at your call site. **MUST load `dotnet-configuration-resilience`** before wiring the client or setting any retry value.

⚠ **Step 1 (client registration / HttpClient ownership)** — the SDK's DI extension registers the client as a singleton over `IHttpClientFactory.CreateClient()`; what that implies for handler lifetime, DNS refresh, and whether Maxio should get a *named* client alongside eShopOnWeb's existing `AddHttpClient` registrations is a lifetime question the constructor signature does not answer. **MUST load `dotnet-client-initialization`**.

⚠ **Step 1 (auth)** — `BasicAuthCredentials` is set on the options object, and *when* those options are read relative to the client's construction (see the eager-evaluation row in §2.1) determines whether a rotated `Maxio:ApiKey` ever reaches the wire. **MUST load `dotnet-authentication`**.

⚠ **Steps 2, 3, 5, 6 (every list/filter call)** — `ListProductsForProductFamily`, `ListProductFamilies` and `ListSubscriptions` declare 5–14 nullable filter parameters with no defaults; the consequence of getting the argument style wrong is a call that compiles and silently filters on the wrong thing. **MUST load `dotnet-calling-endpoints`** before the first call.

⚠ **Steps 3–6 (every model you build or read)** — `SubscriptionState`/`IntervalUnit`/`CollectionMethod` are `StringEnum` records rather than C# enums, request bodies are nested envelopes with `required` members, and JSON wire names differ from the C# property names throughout. How you construct, compare and map these decides whether an unknown state value arriving from the wire is handled or crashes. **MUST load `dotnet-models`**.

⚠ **Step 7 (error boundary)** — `SdkException<T>` is sealed with no shared base, carries no status code and no message, and the typed-vs-raw split differs per operation (§2.5). Whether your catch ladder actually catches what the SDK throws, and how you read a status without an accessor, is decided by mechanics the signature does not show. **MUST load `dotnet-error-handling`** before writing the boundary — see also the two mandatory hazards in §4.

⚠ **Tests** — the `HttpClient` constructor argument is the seam; how far you fake it decides whether the tests assert behaviour or the SDK's internals. **MUST load `dotnet-testing`**.

---

## 4. REQUIRED READING — load **before implementation starts**

These companion skills carry the defaults, worked examples and wiring that this sheet deliberately does **not** restate. The contract sheet is names and shapes; these are how to use them correctly.

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — constructing/DI-registering the client, `HttpClient` ownership and lifetime |
| `dotnet-authentication` | Step 1 — supplying `BasicAuth`, config-sourced keys, rotation |
| `dotnet-configuration-resilience` | Step 1 — retries, timeouts, base-URL/server selection, pagination loops, logging |
| `dotnet-calling-endpoints` | Steps 2–6 — every operation call, optional-parameter binding, cancellation |
| `dotnet-models` | Steps 3–6 — building `CreateSubscriptionRequest`/`CreateCustomerRequest`, reading enums and envelopes |
| `dotnet-error-handling` | Step 7 — the catch ladder, reading status and error bodies safely |
| `dotnet-testing` | Tests for the integration layer |

**Two mandatory hazards for the error boundary — `System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:**

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

Concrete instances of both inside *this* plan's scope (map/source evidence — treat them as live risks, not hypotheticals):

- 2xx direction — `ProductResponse.Product`, `CustomerResponse.Customer` and `SiteResponse.Site` are all **`required`**; a 200 whose body omits that member throws `JsonException` from ops B, C, D and E, which no `catch (SdkException<…>)` will see (`records-*.md`).
- non-2xx direction — `ErrorListResponse1.Errors` is **`required IReadOnlyList<string>`**, and `CreateSubscriptionError.Create` deserializes the 422 body into it (`Models/ErrorListResponse1.cs`, `Errors/CreateSubscriptionError.cs`). A 422 whose `errors` is an object/map rather than a string array — a shape this same SDK models elsewhere as `ErrorArrayMapResponse1 { Errors (errors): IReadOnlyDictionary<string, object>? }` — throws `JsonException` in place of `SdkException<CreateSubscriptionError>`, and the 422 is lost. A subscribe rejected for a deterministic reason would then be reported as a 5xx outage and retried forever.

---

## 5. Assumptions & Blockers

**Assumptions (about intent, not about the SDK)**

1. The caller's Maxio `reference` is derived from the JWT identity. Choosing *which* claim is the stable key is an application decision — `YOUR CALL — not in the map`. It must be stable across logins and never reused, because it is the sole idempotency key for the customer.
2. `Maxio:Subdomain` names the sandbox site (`cp-exp-1` in the brief) and `Environment` stays `ServerEnvironment.Us` unless the account is EU-hosted. There is no sandbox environment member to select (§2.1).
3. First/last name and email for `CreateCustomer` (all three are `required`) come from the application's own identity store. Where they come from, and what to do when the identity carries no name, are application decisions — `YOUR CALL — not in the map`.
4. "Already subscribed" for the idempotency pre-check means: a subscription on this customer whose `Product.Handle` equals the requested handle **and** whose `State` is in the SDK's Live States set (§2.6). The `AwaitingSignup` treatment is left to the implementer.
5. The plans list shows every non-archived product in the configured family. No further "purchasable" filter (e.g. price > 0) is applied — the map exposes no such flag.

**UNVERIFIED (only live traffic can confirm; each carries a defensive-coding directive)**

| Item | Directive |
|---|---|
| What `ReadCustomerByReference` does when the reference does not exist. It is Case B and throws on any non-2xx, but the map does not state the miss status; and because `CustomerResponse.Customer` is `required`, a 200 with an empty body would surface as `JsonException`, not `SdkException` | Treat **both** as "not found": catch `SdkException<RawError>` and map `ex.Error.StatusCode == HttpStatusCode.NotFound` → not-found; **also** catch `System.Text.Json.JsonException` around this one call and treat it as not-found. Any other status rethrows to the boundary. Never let a lookup miss become a 5xx |
| Whether Maxio rejects a duplicate `reference` on `CreateCustomer`, and with which status. The Notes say "you may only create one customer for a given reference value … must be unique", and 422 is the only typed branch — but the status is not stated | Write create-then-recover: on `SdkException<CreateCustomerError>`, re-run `ReadCustomerByReference` and use the customer it returns (the concurrent winner); only if that lookup also misses do you surface the error. Extract the message best-effort — `TryGetCustomerErrorResponse1` first, then `TryGetRawError` → `ReadAsString()` — and fall back to a generic message (§2.5 explains why the typed 422 shape carries nothing usable here) |
| Whether `ListCustomerSubscriptions` returns *all* of a customer's subscriptions. Its signature has **no** `page`/`perPage` parameters, so any server-side page cap is invisible and unpageable through the SDK | Do not treat an empty or short result as proof of "no existing subscription" beyond what it shows; log the returned count when it looks capped. For `GET /api/my-subscriptions`, return what the call returns and do not claim completeness in the response contract |
| Whether the sandbox accepts a card-less `CreateSubscription` for these two products. The Notes make it product-dependent ("Payment information **may** be required … depending on the options for the Product") | Read `Product.RequestCreditCard` / `Product.RequireCreditCard` from step 3 and reject with a clear 4xx before calling if either is `true`; on a 422 from `CreateSubscription`, extract `TryGetErrorListResponse1` → `Errors` best-effort, else `TryGetRawError` → `ReadAsString()`, and fall back to a generic message |
| Whether `CurrentPeriodEndsAt` and `NextAssessmentAt` ever diverge on live data | Render `CurrentPeriodEndsAt`; fall back to `NextAssessmentAt` when null (§2.3) |
| Whether `"handle:eshop-subscribe"` is accepted as the `productFamilyId` string on `ListProductsForProductFamily` (the `handle:` form appears only in `ReadProductFamily`'s Notes, and that operation's C# parameter is `int id`, so the form is unusable there) | Do not rely on it. Resolve the family id via `ListProductFamilies` + `Handle` match (step 2) and pass the numeric id as a string |

**Blockers**

None. The three brief items most at risk of being blockers all resolve:

- the verbatim base-URL override **is** supported (`options.Server.Production.Us.BaseUrl`, §2.1) — not a blocker;
- listing products by family handle is **not** directly supported, but resolving handle → id via `ListProductFamilies` is (step 2) — a required extra call, not a blocker;
- a customer-scoped subscription listing for the idempotency pre-check **is** available via `ListCustomerSubscriptions` (op F), because `ListSubscriptions` has no customer filter — not a blocker.

The one contract gap worth naming: **`Product` carries no currency field**, so plan currency must come from `Sites.ReadSite` → `Site.Currency` (§2.3). If the site is multi-currency and per-plan currency is required, that becomes a `ProductPricePoints` lookup and should be re-scoped before implementation.

**Application decisions this plan deliberately does not make** (`YOUR CALL — not in the map`): the DTO shapes and route conventions of the three PublicApi endpoints; which JWT claim supplies the customer reference; whether the Maxio customer id is persisted locally or re-resolved per request; how concurrent double-clicks are serialized inside the application (the SDK exposes no idempotency key on `CreateSubscription`, so the reference-keyed find-or-create plus the pre-check in op F are the only SDK-side levers); caching policy for the resolved family id, the plans list and `Site.Currency`; and money formatting from `PriceInCents`.
