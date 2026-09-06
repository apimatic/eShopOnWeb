# Maxio Advanced Billing — .NET SDK integration plan (eShopOnWeb `src/PublicApi`)

Scope: three JWT-authenticated endpoints (`GET /api/subscription-plans`, `POST /api/subscriptions`,
`GET /api/my-subscriptions`) backed by Maxio Advanced Billing as system of record.

Every contract fact below was read this session from the bundled SDK map (`sdk-map.md`,
`map/operations/*.md`, `map/models/*.md`) or, where the map was silent, from the generated SDK source at
the tag the map was stamped from. Each row cites its origin in the **Source** column.

---

## 1. Scope & sequence

| # | Step | Maxio operations used |
|---|---|---|
| 1 | Add the NuGet package; add the `Maxio:` options type bound to `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` | — |
| 2 | Register the SDK client in DI (`AddMaxioAdvancedBillingClient`) with Basic auth and server/base-URL resolution | — |
| 3 | Resolve the configured product family: list families, match on `Handle`, keep its numeric `Id` for the request | `ProductFamilies.ListProductFamilies` |
| 4 | `GET /api/subscription-plans` — list the family's products, project handle/name/description/price/interval/trial/setup-fee | `ProductFamilies.ListProductsForProductFamily` (+ `Sites.ReadSite` once for currency) |
| 5 | `POST /api/subscriptions` step A — ensure the Maxio customer exists for the caller (lookup by reference, create if absent) | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer` |
| 6 | `POST /api/subscriptions` step B — idempotency guard: list the customer's subscriptions, detect an existing live one on the same product handle | `Customers.ListCustomerSubscriptions` |
| 7 | `POST /api/subscriptions` step C — create the subscription by `product_handle` + `customer_id`, with **no** payment-profile fields | `Subscriptions.CreateSubscription` |
| 8 | `GET /api/my-subscriptions` — resolve customer by reference, list their subscriptions, project state/period/next assessment/revenue | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |
| 9 | Error boundary translating SDK exceptions to HTTP results | — (see §4/§5) |
| 10 | Tests for the integration layer | — |

**Optional plan resolution by handle.** If the request body's product handle must be validated (or a single
plan rendered) without listing the family, `Products.ReadProductByHandle(apiHandle)` reads one product
directly — see the sheet. The metered component `api-call` is out of scope for these three endpoints;
nothing below touches `client.Components`.

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

### 2.0 Package, namespaces, general call shape

| Fact | Value | Source |
|---|---|---|
| NuGet package id | `AsadAli.AdvancedBilling.Sdk` | `sdk-map.md` (SDK identity) |
| Version to pin | `1.0.2` — the map is stamped from tag `v1.0.2` / commit `15db14b`. **The repo's own `.csproj` at that tag declares `<Version>1.0.0</Version>`, which contradicts the tag.** Pin explicitly (`dotnet add package AsadAli.AdvancedBilling.Sdk --version 1.0.2`); if restore reports that version does not exist, fall back to `1.0.0` and re-verify the surface against this sheet. | `sdk-map.md` stamp vs. SDK source `.csproj` — `UNVERIFIED` (only nuget.org settles which version is published) |
| Root namespace | `MaxioAdvancedBilling` (differs from the package id) | `sdk-map.md` |
| Client class / options | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` · `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` | `sdk-map.md` ("Getting a client") |
| Controllers | `MaxioAdvancedBilling.Api.Customers`, `.Products`, `.ProductFamilies`, `.Subscriptions`, `.Sites` — reached as properties `client.Customers`, `client.Products`, `client.ProductFamilies`, `client.Subscriptions`, `client.Sites` | `sdk-map.md` (namespaces table) + each `map/operations/*.md` header |
| Records | `MaxioAdvancedBilling.Models` | `map/models/records-1-Ac-Cr.md` (header) |
| Enums | `MaxioAdvancedBilling.Models.Enums` | `map/models/enums.md` (header) |
| Typed error classes | `MaxioAdvancedBilling.Errors` | `sdk-map.md` (namespaces table) |
| `SdkException<T>` | `MaxioAdvancedBilling.Core.Exceptions` | `sdk-map.md` (error model) |
| `RawError`, `ApiError` | `MaxioAdvancedBilling.Core.ErrorResponse` | `sdk-map.md` (error-core table) |
| `BasicAuthCredentials` | `MaxioAdvancedBilling.Core.Authentication.Basic` | `sdk-map.md` (Servers & auth) |
| `RetryOptions` | `MaxioAdvancedBilling.Core.Configuration` | `sdk-map.md` (RetryOptions table) |
| `ServerEnvironment`, `ProductionOptions` (+ nested `UsOptions`/`EuOptions`), `EbbOptions` | `MaxioAdvancedBilling.Servers` — **not** `Models.Enums`, even though `ServerEnvironment` is a `StringEnum` | SDK source `Servers/ServerEnvironment.cs`, `Servers/ProductionOptions.cs` |
| `ServerOptions` | `MaxioAdvancedBilling` (root) | SDK source `ServerOptions.cs` |
| Async shape | **Every operation returns `Task<T>` (or `Task`) and has NO `Async` suffix** — e.g. `Task<CustomerResponse> ReadCustomerByReference(string reference, CancellationToken ct = default)`. `await` them directly. | SDK source `Api/Customers.cs` |
| No-throw variants | Absent across the whole SDK — every operation is throw-only; there is no `…Result`/`ApiResult` overload to fall back on. | `sdk-map.md` (error model) |

### 2.1 Client construction, auth, and the `Maxio:BaseUrl` override

`MaxioAdvancedBillingClientOptions` properties (all four, with their defaults from source):

| Property | Type | Default |
|---|---|---|
| `Environment` | `MaxioAdvancedBilling.Servers.ServerEnvironment` | `ServerEnvironment.Default()` → `ServerEnvironment.Us` |
| `Retry` | `MaxioAdvancedBilling.Core.Configuration.RetryOptions` | `RetryOptions.Default()` |
| `Server` | `MaxioAdvancedBilling.ServerOptions` | `new()` |
| `BasicAuth` | `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?` | `null` |

Source: `sdk-map.md` (options table) + SDK source `MaxioAdvancedBillingClientOptions.cs`.

**Auth type and members.** `BasicAuthCredentials` is a sealed class with exactly two `required string` init-only
members, `Username` and `Password`, plus `Encode()`. There is no builder method and no other auth manager —
Basic is the only scheme. Convention: **`Username` = the Maxio API key (`Maxio:ApiKey`), `Password` = the
literal string `"x"`.** Source: `sdk-map.md` (Servers & auth) + SDK source `Core/Authentication/Basic/BasicAuthCredentials.cs`.

**Base-URL resolution — exactly how both modes work.** `ServerOptions.Production` is a `ProductionOptions`
with nested `Us`/`Eu` option objects, each carrying two settable strings:

| Member | Type | Default |
|---|---|---|
| `options.Server.Production.Us.BaseUrl` | `string` | `"https://{site}.chargify.com"` |
| `options.Server.Production.Us.Site` | `string` | `"subdomain"` (the literal word — **not** your subdomain) |
| `options.Server.Production.Eu.BaseUrl` | `string` | `"https://{site}.ebilling.maxio.com"` |
| `options.Server.Production.Eu.Site` | `string` | `"subdomain"` |

The URL is built by textually replacing the token `{site}` inside `BaseUrl` with `Site`, then trimming a
trailing `/` and appending the operation path. Consequences, both grounded in that code:

- **Subdomain mode** (`Maxio:BaseUrl` unset): leave `BaseUrl` at its default and set
  `options.Server.Production.Us.Site = <Maxio:Subdomain>`. If you forget `Site`, every call goes to
  `https://subdomain.chargify.com` — a real host that is not your site.
- **Verbatim mode** (`Maxio:BaseUrl` set): assign it to `options.Server.Production.Us.BaseUrl`. Because the
  replacement only touches the literal token `{site}`, a URL containing no `{site}` is used **verbatim**
  (minus a trailing slash). **No special `ServerEnvironment` value is required** — there is no "Custom"
  environment; `Environment` stays `ServerEnvironment.Us` and only selects which of the `Us`/`Eu` option
  objects is read. A custom base URL that *does* contain `{site}` will still have `{site}` substituted, so
  set `Site` too if you use a templated override.
- `ServerEnvironment` has exactly two members: `ServerEnvironment.Us` (wire `"US"`) and `ServerEnvironment.Eu`
  (wire `"EU"`); `ServerEnvironment.Default()` returns `Us`.
- Ebb (`options.Server.Ebb.*`) is only used by `SubscriptionComponents` event-ingest endpoints — nothing in
  this plan touches it, so leave it alone.

Source: `sdk-map.md` (Servers & auth) + SDK source `ServerOptions.cs`, `Servers/ProductionOptions.cs`,
`Servers/ServerEnvironment.cs`, `Core/TemplateParamsFactory.cs`.

**DI registration — exact shape.** `MaxioAdvancedBilling.ServiceCollectionExtensions` declares an extension
member on `IServiceCollection`:

`IServiceCollection AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>? configure = null)`

What it actually does, verbatim from source: builds `new MaxioAdvancedBillingClientOptions()`, invokes
`configure` on it **immediately at registration time**, calls `services.AddHttpClient()`, and registers
`MaxioAdvancedBillingClient` as a **singleton** whose `HttpClient` comes from
`IHttpClientFactory.CreateClient()` (the default, unnamed client).

Two consequences that shape the wiring code:

- The `configure` callback runs **before** the service provider exists and receives no `IServiceProvider`.
  It cannot resolve `IOptions<…>` or `IConfiguration` from DI — the API key, subdomain and base URL must be
  captured from the already-built `IConfiguration` in the closure at registration time.
- The client is a singleton over the factory's default `HttpClient`. Any handler/timeout customisation must
  therefore be applied to that default client registration, not to a named one.

The only constructor is `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`
if you prefer to register it yourself.

Source: `sdk-map.md` ("Getting a client", DI alternative) + SDK source `ServiceCollectionExtensions.cs`,
`MaxioAdvancedBillingClient.cs`.

### 2.2 Operations

| # | Controller property | Method signature (verbatim) | Request model + fields | Response envelope → fields read | Error case + accessors | Pagination | Source |
|---|---|---|---|---|---|---|---|
| 3 | `client.ProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 filters are nullable with **no default → must be passed explicitly** (`null` to skip) | none (GET) | `IReadOnlyList<ProductFamilyResponse>`; `ProductFamilyResponse.ProductFamily (product_family): ProductFamily?` — **nullable, null-check it**. Read `ProductFamily.Handle (handle): string?`, `ProductFamily.Id (id): int?`, `Name`, `Description` | **Case B** — `SdkException<RawError>`; `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` · `ReadAsBytes()` | **none** — no `page`/`perPage` params; the whole list comes back in one call | `operations/ProductFamilies.md`, `records-3-Of-Su.md` |
| 4 | `client.ProductFamilies` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 8 params `dateField`…`include` are nullable with **no default → must be passed explicitly** | none (GET). `productFamilyId` is a **`string`** — pass `family.Id!.Value.ToString(CultureInfo.InvariantCulture)`. Pass `includeArchived: false` so archived plans do not reach the API response | `IReadOnlyList<ProductResponse>`; `ProductResponse.Product (product): Product` is **`!req`** (non-null). Fields read: see §2.3 | **Case A** — `SdkException<ListProductsForProductFamilyError>`; `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page` + `perPage` (defaults 1 / 20) | `operations/ProductFamilies.md`, `records-3-Of-Su.md` |
| 4b | `client.Products` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | none (GET) | `ProductResponse` → `.Product` (`!req`) | **Case B** — `SdkException<RawError>`; `StatusCode` · `ReadAsString()` · `ReadAsJson<T>()` · `ReadAsBytes()`. A non-existent handle surfaces as this exception with `StatusCode == HttpStatusCode.NotFound` | none | `operations/Products.md` |
| 4c | `client.Sites` | `ReadSite(CancellationToken ct = default)` | none (GET) | `SiteResponse.Site (site): Site` (**`!req`**) → `Site.Currency (currency): string?`, `Site.NonPrimaryCurrencies (non_primary_currencies): IReadOnlyList<string>?`, **`Site.RelationshipInvoicingEnabled (relationship_invoicing_enabled): bool?`**, **`Site.DefaultPaymentCollectionMethod (default_payment_collection_method): string?`** — the last two are **load-bearing for step 7**: they select the collection method (§2.5) and reveal what an omitted `payment_collection_method` defaults to | **Case B** — `SdkException<RawError>` | none | `operations/Sites.md`, `records-3-Of-Su.md` |
| 5a | `client.Customers` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — sends `reference` as the `reference` query param on `GET /customers/lookup.json` | none (GET) | `CustomerResponse.Customer (customer): Customer` (**`!req`**) → `Customer.Id (id): int?`, `Customer.Reference (reference): string?`, `Customer.Email (email): string?` | **Case B** — `SdkException<RawError>`. **"Not found" is an exception, not a null return**: catch `SdkException<RawError>` and treat `ex.Error.StatusCode == HttpStatusCode.NotFound` as "no customer yet" | none | `operations/Customers.md`, `records-1-Ac-Cr.md` |
| 5b | `client.Customers` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` is nullable with **no default → must be passed explicitly** | `CreateCustomerRequest { Customer (customer): CreateCustomer !req }`; `CreateCustomer` **required**: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`. Optional but load-bearing here: **`Reference (reference): string?`** — the per-user idempotency key. Other optionals: `CcEmails`, `Organization`, `Address`, `Address2`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `VatNumber`, `TaxExempt (bool?)`, `TaxExemptReason`, `ParentId (int?)`, `SalesforceId` | `CustomerResponse` → `.Customer` (`!req`) → `.Id` | **Case A** — `SdkException<CreateCustomerError>`; `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. **See §5 on the 422 payload's shape.** | none | `operations/Customers.md`, `records-1-Ac-Cr.md` |
| 5c *(alt lookup)* | `client.Customers` | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — the 7 params `direction`…`q` are nullable with **no default → must be passed explicitly**. Note `startDate`/`endDate`/`startDatetime`/`endDatetime` here are **`string?`**, not `DateTimeOffset?` | none (GET). **`q` is the only search field** — a free-text search the operation's Notes say matches email, Maxio id, organization, your reference value, or first/last name. There is no dedicated `email` query parameter | `IReadOnlyList<CustomerResponse>`; each `.Customer` is `!req` | **Case B** — `SdkException<RawError>` | manual `page` + `perPage` (defaults 1 / **50**) | `operations/Customers.md` |
| 6 / 8 | `client.Customers` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | none (GET) | `IReadOnlyList<SubscriptionResponse>`; `SubscriptionResponse.Subscription (subscription): Subscription?` — **nullable, null-check every element**. Fields read: see §2.4 | **Case B** — `SdkException<RawError>` | **none** — the signature has no `page`/`perPage`; this is the only customer-scoped subscription list in the SDK, and it cannot be paged | `operations/Customers.md`, `records-4-Su-We.md` |
| 7 | `client.Subscriptions` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` is nullable with **no default → must be passed explicitly** | `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription !req }`. `CreateSubscription` marks **nothing** `required` — see §2.5 for exactly which fields to set and which to omit | `SubscriptionResponse.Subscription (subscription): Subscription?` — **nullable even on success; null-check before projecting** | **Case A** — `SdkException<CreateSubscriptionError>`; `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. `ErrorListResponse1 { Errors (errors): IReadOnlyList<string> !req }` — **see §5 for the required-member hazard** | none | `operations/Subscriptions.md`, `records-2-Cr-Ne.md` |
| *(ref)* | `client.Subscriptions` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` is nullable with **no default → must be passed explicitly** (`null` to skip) | none (GET) | `SubscriptionResponse` → `.Subscription` (nullable) | **Case B** — `SdkException<RawError>` | none | `operations/Subscriptions.md` |
| *(ref)* | `client.Subscriptions` | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 14 params `state`…`include` nullable with **no default → must be passed explicitly** | none (GET). **There is no customer filter on this operation** — `product` is an `int?` product id, not a handle. Do **not** use it for `/api/my-subscriptions`; use `ListCustomerSubscriptions` | `IReadOnlyList<SubscriptionResponse>` | **Case B** — `SdkException<RawError>` | manual `page` + `perPage` (defaults 1 / 20) | `operations/Subscriptions.md` |

**Pagination summary.** Only three of the operations above page at all, all of them manually via
`page` + `perPage` int params: `ListProductsForProductFamily` (defaults 1/20),
`ListCustomers` (defaults 1/**50**), `ListSubscriptions` (defaults 1/20). There is no auto-pager, no
continuation token, and no total-count field in any envelope: page until a page returns fewer than
`perPage` items. `ListProductFamilies` and `ListCustomerSubscriptions` have **no** paging parameters at all.
A maximum allowed `perPage` is **not** stated anywhere in the map or the SDK source — `UNVERIFIED`; pick a
conservative page size (e.g. 100 for `ListCustomers`) and if the API rejects it, the SDK surfaces it as
`SdkException<RawError>` with the status, so log `ex.Error.ReadAsString()` and lower the size rather than
retrying blindly. Source: `operations/*.md` pagination rows.

### 2.3 `Product` — the fields `/api/subscription-plans` reads

Namespace `MaxioAdvancedBilling.Models`. Source: `map/models/records-3-Of-Su.md`.

| Field (wire) | Type | Use |
|---|---|---|
| `Handle (handle)` | `string?` | the stable plan identifier (`eshop-pro`, `basic-plan`) |
| `Name (name)` | `string?` | display name |
| `Description (description)` | `string?` | description |
| `Id (id)` | `int?` | numeric id — **do not persist or expose**; the brief states these are not stable |
| `PriceInCents (price_in_cents)` | `long?` | **cents**. Format as `value / 100m` |
| `Interval (interval)` | `int?` | interval count |
| `IntervalUnit (interval_unit)` | `IntervalUnit?` | interval unit enum |
| `TrialPriceInCents (trial_price_in_cents)` | `long?` | trial price, **cents** |
| `TrialInterval (trial_interval)` | `int?` | trial length |
| `TrialIntervalUnit (trial_interval_unit)` | `IntervalUnit?` | trial unit |
| `InitialChargeInCents (initial_charge_in_cents)` | `long?` | **the setup fee**, in cents |
| `InitialChargeAfterTrial (initial_charge_after_trial)` | `bool?` | whether the setup fee is charged after the trial |
| `RequireCreditCard (require_credit_card)` | `bool?` | whether a payment profile is mandatory for this product |
| `RequestCreditCard (request_credit_card)` | `bool?` | whether a card is merely requested |
| `ArchivedAt (archived_at)` | `DateTimeOffset?` | non-null ⇒ archived |
| `ProductFamily (product_family)` | `ProductFamily?` | nested family (`Id`, `Name`, `Handle`, `Description`, `CreatedAt`, `UpdatedAt`, `ArchivedAt`, `AccountingCode`) |
| `ProductPricePointId / ProductPricePointHandle / ProductPricePointName` | `int?` / `string?` / `string?` | the default price point in effect |
| `Taxable (taxable)` | `bool?` | — |

**There is no `Currency` field on `Product`.** It is not on `ProductPricePoint` either — that model carries
only `CurrencyPrices (currency_prices): IReadOnlyList<CurrencyPrice>?` for *alternative* currencies
(`CurrencyPrice { Id: int?, Currency (currency): string?, Price (price): double?, FormattedPrice (formatted_price): string?, PriceId: int?, PricePointId: int?, ProductPricePointId: int?, Role (role): CurrencyPriceRole? }`).
The site's base currency comes from `Sites.ReadSite()` → `SiteResponse.Site.Currency (currency): string?`
(row 4c). Call it **once** and cache it for the process — it does not change per request. The same one call
also yields the two site facts the subscribe flow needs (§2.5):
`Site.RelationshipInvoicingEnabled (relationship_invoicing_enabled): bool?` and
`Site.DefaultPaymentCollectionMethod (default_payment_collection_method): string?` — cache all three
together. Source: `records-3-Of-Su.md` (`Product`, `ProductPricePoint`, `Site`),
`records-1-Ac-Cr.md` (`CurrencyPrice`).

### 2.4 `Subscription` — the fields `/api/subscriptions` and `/api/my-subscriptions` read

Namespace `MaxioAdvancedBilling.Models`. Source: `map/models/records-4-Su-We.md`. **Every field below is
nullable.**

| Field (wire) | Type | Use |
|---|---|---|
| `Id (id)` | `int?` | subscription id |
| `State (state)` | `SubscriptionState?` | subscription state (enum — §2.6) |
| `PreviousState (previous_state)` | `SubscriptionState?` | — |
| `Product (product)` | `Product?` | nested product → read `.Handle` and `.Name` for the response |
| `Customer (customer)` | `Customer?` | nested customer → `.Id`, `.Reference` |
| `CurrentPeriodStartedAt (current_period_started_at)` | `DateTimeOffset?` | current period start |
| `CurrentPeriodEndsAt (current_period_ends_at)` | `DateTimeOffset?` | current period end |
| `NextAssessmentAt (next_assessment_at)` | `DateTimeOffset?` | **next billing / assessment date** |
| `TotalRevenueInCents (total_revenue_in_cents)` | `long?` | total revenue, **cents** |
| `ProductPriceInCents (product_price_in_cents)` | `long?` | plan price on the subscription, **cents** |
| `CurrentBillingAmountInCents (current_billing_amount_in_cents)` | `long?` | current billing amount, **cents** |
| `BalanceInCents (balance_in_cents)` | `long?` | **cents** |
| `Currency (currency)` | `string?` | subscription currency |
| `ActivatedAt / CreatedAt / UpdatedAt / CanceledAt / ExpiresAt / TrialStartedAt / TrialEndedAt / DelayedCancelAt / ScheduledCancellationAt / OnHoldAt / AutomaticallyResumeAt` | `DateTimeOffset?` | lifecycle timestamps |
| `CancelAtEndOfPeriod (cancel_at_end_of_period)` | `bool?` | — |
| `CancellationMethod (cancellation_method)` | `CancellationMethod?` | enum: `MerchantUi (merchant_ui)`, `MerchantApi (merchant_api)`, `Dunning (dunning)`, `BillingPortal (billing_portal)`, `Unknown (unknown)`, `Imported (imported)` |
| `PaymentCollectionMethod (payment_collection_method)` | `CollectionMethod?` | enum: `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` |
| `Reference (reference)` | `string?` | your own subscription reference, if you set one |
| `ProductPricePointId / ProductPricePointType` | `int?` / `PricePointType?` | `PricePointType`: `Catalog (catalog)`, `Default (default)`, `Custom (custom)` |
| `NextProductId / NextProductHandle` | `int?` / `string?` | pending delayed product change |
| `CreditCard (credit_card)` | `CreditCardPaymentProfile?` | will be `null` for no-payment-method subscriptions |
| `SelfServicePageToken (self_service_page_token)` | `string?` | only populated when `include[]=self_service_page_token` is requested |

**All date/time fields on every model in this plan are `DateTimeOffset?`** (nullable) — with one exception in
`ListCustomers`, whose four date *query parameters* are `string?`. Source: `records-3-Of-Su.md`,
`records-4-Su-We.md`, `operations/Customers.md`.

### 2.5 `CreateSubscription` — exactly what to set for a no-payment-method subscribe

`CreateSubscriptionRequest { Subscription (subscription): CreateSubscription !req }`. `CreateSubscription`
declares **no `required` members at all**, so `required?` selects nothing for you — the operation's Notes are
the only guide, and they say: *"Specify the product with `product_id` or `product_handle`. … Identify an
existing customer with `customer_id` or `customer_reference`. Optionally, include an existing payment profile
using `payment_profile_id`. To create a new customer, pass `customer_attributes`."*

**Set these:**

| Field (wire) | Type | Why |
|---|---|---|
| `ProductHandle (product_handle)` | `string?` | identifies the plan by **handle** — the stable identifier. Use this instead of `ProductId (product_id): int?` |
| `CustomerId (customer_id)` | `int?` | the Maxio customer id resolved in step 5. Alternative: `CustomerReference (customer_reference): string?` — the same reference key used for lookup. Set **one** of the two, not both |
| `PaymentCollectionMethod (payment_collection_method)` | `MaxioAdvancedBilling.Models.Enums.CollectionMethod?` | **Mandatory for a no-payment-profile subscribe — set it explicitly.** See the block below |

**Deliberately omitted** (Notes-named fields left out, and why): `PaymentProfileId (payment_profile_id)`,
`PaymentProfileAttributes (payment_profile_attributes)`, `CreditCardAttributes (credit_card_attributes)`,
`BankAccountAttributes (bank_account_attributes)` — the seeded plans require no payment method;
`CustomerAttributes (customer_attributes)` — the customer is created explicitly in step 5 so the flow can be
made idempotent (letting `CreateSubscription` create the customer makes a double-click create two customers);
`ProductPricePointHandle` / `ProductPricePointId` — omitting them selects the product's default price point;
`CouponCode` / `CouponCodes`, `Components`, `CalendarBilling`, `PrepaidConfiguration`, `Group`, `OfferId`,
`AgreementAcceptance` / `AchAgreement` (Maxio-Payments-only), `NextBillingAt` / `InitialBillingAt` /
`PreviousBillingAt` (let Maxio set the schedule), `Currency` (site default applies), `NetTerms`.

**Optional fields worth knowing exist:** `Reference (ref): string?` — note the C# property is `Ref` and the
wire name is `ref`; there is *also* a separate `Reference (reference): string?` property. Two distinct
fields; if you set a subscription-side reference, pick one deliberately.
`DeferSignup (defer_signup): bool? = false` and `DunningCommunicationDelayEnabled: bool? = false` already
carry the right defaults.

**Payment-requirement precondition — the collection method, not any product flag.** The operation's Notes
state payment information *may* be required depending on the product's options, and the deciding field is
`PaymentCollectionMethod`. **Leaving it unset is not safe:** the site default then applies, and on a site
whose `Site.DefaultPaymentCollectionMethod` is `"automatic"` Maxio attempts to charge a card for the first
period's balance at creation time and rejects the call with **HTTP 422 — "No payment method was on file for
the $N balance"** when no payment profile exists. Set it explicitly.

| | |
|---|---|
| C# identifier | `PaymentCollectionMethod` on `MaxioAdvancedBilling.Models.CreateSubscription` |
| C# type | `MaxioAdvancedBilling.Models.Enums.CollectionMethod?` — needs `using MaxioAdvancedBilling.Models.Enums;` of its own |
| Wire name | `payment_collection_method` |
| Kind | `sealed record CollectionMethod : StringEnum<CollectionMethod>` — **not** a C# enum; the constructor is private. Write the static member (`CollectionMethod.Remittance`); `CollectionMethod.FromValue("remittance")` also exists but is unnecessary for a compile-time literal. Read back with `.Value` |
| Members (all four) | `CollectionMethod.Automatic` (`automatic`) · `CollectionMethod.Remittance` (`remittance`) · `CollectionMethod.Prepaid` (`prepaid`) · `CollectionMethod.Invoice` (`invoice`) |

**Which value — read the architecture, never assume it.** The enum's own summary, verbatim: *"The type of
payment collection to be used in the subscription. For legacy Statements Architecture valid options are -
`invoice`, `automatic`. For current Relationship Invoicing Architecture valid options are - `remittance`,
`automatic`, `prepaid`."* Sending the wrong architecture's value is not a valid option. Select it from
`Sites.ReadSite()` → `Site.RelationshipInvoicingEnabled (relationship_invoicing_enabled): bool?` (row 4c):

| Site | Value to send | Status |
|---|---|---|
| `RelationshipInvoicingEnabled == true` (Relationship Invoicing) | `CollectionMethod.Remittance` | **CONFIRMED — live, this session.** On `cp-exp-2` (`RelationshipInvoicingEnabled == true`, `DefaultPaymentCollectionMethod == "automatic"`) the previously-422 body succeeded with **HTTP 201**, subscription `94212077`, state `active`, `payment_collection_method: "remittance"`, `next_assessment_at` one month out, **no payment profile anywhere in the request** |
| `RelationshipInvoicingEnabled == false` (legacy Statements) | `CollectionMethod.Invoice` | `UNVERIFIED` — selected from the same flag by symmetry with the enum summary, but never exercised. If a legacy site is ever targeted, expect the 422 above and treat this path as untested |

Note the asymmetry if you ever set this on an **existing** subscription: `UpdateSubscription`'s counterpart
property `PaymentCollectionMethod (payment_collection_method)` is declared **`string?`**, not
`CollectionMethod?` — the two generated models disagree on the type of the same wire field, so on update you
must pass the raw wire string (e.g. `CollectionMethod.Remittance.Value`). Source: `records-4-Su-We.md`
(`UpdateSubscription`) vs `records-2-Cr-Ne.md` (`CreateSubscription`).

Source: `operations/Subscriptions.md` (Notes + signature), `records-2-Cr-Ne.md` (`CreateSubscription`,
`CreateSubscriptionRequest`), `records-3-Of-Su.md` (`Site`), `enums.md` (`CollectionMethod`).

### 2.6 Enums

Namespace `MaxioAdvancedBilling.Models.Enums`. These are **`StringEnum<T>` records, not C# enums** — write
the literal static member name (`SubscriptionState.Active`, not `SubscriptionState.active`). Source:
`map/models/enums.md`.

| Enum | C# member (wire value) |
|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `IntervalUnit` | `Day (day)`, `Month (month)` — **only two members**; there is no `year` |
| `ExpirationIntervalUnit` | `Day (day)`, `Month (month)`, `Never (never)` |
| `SubscriptionStateFilter` (query filter for `ListSubscriptions` only) | `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)` |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` |
| `CancellationMethod` | `MerchantUi (merchant_ui)`, `MerchantApi (merchant_api)`, `Dunning (dunning)`, `BillingPortal (billing_portal)`, `Unknown (unknown)`, `Imported (imported)` |
| `PricePointType` | `Catalog (catalog)`, `Default (default)`, `Custom (custom)` |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` |
| `SortingDirection` | `Asc (asc)`, `Desc (desc)` |
| `SubscriptionSort` | `SignupDate (signup_date)`, `PeriodStart (period_start)`, `PeriodEnd (period_end)`, `NextAssessment (next_assessment)`, `UpdatedAt (updated_at)`, `CreatedAt (created_at)`, `TotalPayments (total_payments)`, `Id (id)`, `OpenBalance (open_balance)`, `ExpiresAt (expires_at)` |
| `SubscriptionDateField` | `CurrentPeriodEndsAt (current_period_ends_at)`, `CurrentPeriodStartsAt (current_period_starts_at)`, `CreatedAt (created_at)`, `ActivatedAt (activated_at)`, `CanceledAt (canceled_at)`, `ExpiresAt (expires_at)`, `TrialStartedAt (trial_started_at)`, `TrialEndedAt (trial_ended_at)`, `UpdatedAt (updated_at)` |
| `SubscriptionInclude` (for `ReadSubscription`) | `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)` |
| `SubscriptionListInclude` (for `ListSubscriptions`) | `SelfServicePageToken (self_service_page_token)` |
| `ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)` — the only member |

**Reading an enum back out to JSON.** The base type exposes `public TValue Value { get; init; }`, so
`subscription.State?.Value` is the wire string (`"active"`), `ToString()` returns the same, and there is an
implicit conversion to `string`. It also exposes `bool IsKnownValue()`. **Deserialization accepts values the
SDK does not know**: an unrecognised state does *not* throw — it arrives as an instance whose `Value` is the
unknown wire string and whose `IsKnownValue()` is `false`. A `switch` over the known members must therefore
have a default arm. Source: SDK source `Core/Enum/StringEnum.cs`, `Core/Enum/TypedEnum.cs`.

### 2.7 Error handling — the exact types

| Fact | Value | Source |
|---|---|---|
| Exception type | `MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>` — **`sealed class SdkException<TError> : Exception`**, whose only member is `public required TError Error { get; init; }` | `sdk-map.md` + SDK source `Core/Exceptions/SdkException.cs` |
| **There is no non-generic `SdkException` base** | You cannot catch every SDK error with one clause. Each closed generic (`SdkException<RawError>`, `SdkException<CreateCustomerError>`, `SdkException<CreateSubscriptionError>`, `SdkException<ListProductsForProductFamilyError>`) needs its own `catch`, or a `catch (Exception)` outer arm | SDK source `Core/Exceptions/SdkException.cs` |
| **`SdkException` carries no status code of its own** | The HTTP status is reachable **only** through a `RawError`: Case B → `ex.Error.StatusCode`; Case A → `ex.Error.TryGetRawError(out var raw)` then `raw.StatusCode` | `sdk-map.md` error-core table + SDK source |
| `RawError` members | `StatusCode: HttpStatusCode` · `ReadAsBytes(): ReadOnlyMemory<byte>` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` — namespace `MaxioAdvancedBilling.Core.ErrorResponse` | `sdk-map.md` error-core table |
| `ApiError` | `abstract` base of all typed error classes; single member `TryGetRawError(out RawError error): bool` | `sdk-map.md` error-core table |
| Other public exception types | `MaxioAdvancedBilling.Core.Exceptions.AuthSchemeException : Exception` (`SchemeFailures: IReadOnlyList<Exception>`) — thrown when **no auth scheme succeeds**, i.e. credentials were never configured; plus `SseException` / `SseTimeoutException` / `SseDeserializationException`, which none of these endpoints can hit | SDK source `Core/Exceptions/` |
| Typed vs. string-map errors | **Both shapes exist in this SDK.** `ErrorListResponse1 { Errors (errors): IReadOnlyList<string> !req }` is a *list of strings*. `CustomerErrorResponse1 { Errors (errors): Errors? }` wraps the shared `Errors` record, whose only fields are `PerPage (per_page): IReadOnlyList<string>?` and `PricePoint (price_point): IReadOnlyList<string>?` | `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md` |

**404 vs 422 — what each actually surfaces as, per operation in scope:**

| Situation | What you catch | How to read it |
|---|---|---|
| Customer reference not found (`ReadCustomerByReference`) | `SdkException<RawError>` | `ex.Error.StatusCode == HttpStatusCode.NotFound` |
| Product handle not found (`ReadProductByHandle`) | `SdkException<RawError>` | `ex.Error.StatusCode == HttpStatusCode.NotFound` |
| Product family id not found (`ListProductsForProductFamily`) | `SdkException<ListProductsForProductFamilyError>` | `TryGetString(out var msg)` → the 404 body is a **bare string**; else `TryGetRawError(out var raw)` |
| Customer validation failure (`CreateCustomer`, 422) | `SdkException<CreateCustomerError>` | `TryGetCustomerErrorResponse1(out var e422)` → `e422.Errors` (type `Errors`) — **see §5 for why this is nearly always empty**; also `TryGetRawError(out var raw)` for status + raw body |
| Subscription validation failure (`CreateSubscription`, 422) | `SdkException<CreateSubscriptionError>` | `TryGetErrorListResponse1(out var e422)` → `e422.Errors` is `IReadOnlyList<string>`; else `TryGetRawError` |
| Any other status on any of the above | the same exception type | `TryGetRawError(out var raw)` (Case A) or `ex.Error` directly (Case B) → `raw.StatusCode`, `raw.ReadAsString()` |
| 401 / wrong host / DNS failure | `SdkException<RawError>` (401 has a status) or a transport exception | 401 ⇒ check `Maxio:ApiKey` and that `Password` is the literal `"x"`; a wrong host usually means `Server.Production.Us.Site` was left at its `"subdomain"` default |

---

## 3. Trap notes

> ⚠ **Step 2 (client registration & DI)** — `AddMaxioAdvancedBillingClient` registers the client as a
> singleton over `IHttpClientFactory`'s default client, so the handler pipeline's lifetime, rotation and any
> per-request customisation are decided by *that* registration, not by this SDK. Whether the SDK client
> wrapper may be resolved per request, and how to keep the handler long-lived, is exactly what the
> initialization skill covers. **MUST load `dotnet-client-initialization`** before writing the registration.

> ⚠ **Step 2 (credentials)** — where the credentials object must be set relative to constructing the client,
> and how to source the key from configuration rather than baking it in, are not visible in the
> `BasicAuthCredentials` shape. **MUST load `dotnet-authentication`** before wiring `Maxio:ApiKey`.

> ⚠ **Steps 3–8 (every list/search call)** — several of these operations have 5, 8, 9 or 14 consecutive
> nullable parameters with no C# defaults; whether a positional call can silently bind an argument to the
> wrong slot, and what the safe calling convention is, is the calling-endpoints skill's subject.
> **MUST load `dotnet-calling-endpoints`** before writing the first `client.…` call.

> ⚠ **Steps 4–8 (reading responses, building request bodies)** — the models here are `init`-only records with
> `required` members, `StringEnum` fields that are not C# enums, and envelopes whose payload property is
> sometimes `!req` and sometimes nullable; and what happens to JSON fields the SDK does not model is a
> property of the deserializer, not of the record. **MUST load `dotnet-models`** before constructing
> `CreateCustomerRequest` / `CreateSubscriptionRequest` or mapping responses onto eShopOnWeb DTOs.

> ⚠ **Steps 5–7 (idempotency of the Subscribe flow)** — whether a write that appears to have failed may in
> fact have reached Maxio, and therefore whether "lookup, then create" is safe to re-run, depends on the
> SDK's retry behaviour, which verbs it re-sends, and what its timeout actually bounds — none of which the
> option names reveal. This decides whether a double-click can produce two customers or two subscriptions.
> **MUST load `dotnet-configuration-resilience`** before wiring the client and before finalising the
> idempotency design.

> ⚠ **Step 9 (error boundary)** — there is no non-generic `SdkException` base, the status code lives only on
> `RawError`, and typed error accessors return `false` for statuses they do not cover; which exceptions
> actually reach a catch block and how to read a body safely is the error skill's subject.
> **MUST load `dotnet-error-handling`** before writing the boundary. See also the two mandatory
> `JsonException` hazards in §4.

> ⚠ **Step 10 (tests)** — the seam this SDK offers is the `HttpClient` constructor argument, and what to
> assert (behaviour, not execution) is not derivable from the signatures. **MUST load `dotnet-testing`**
> before stubbing the SDK.

---

## 4. REQUIRED READING

**Load all of these before implementation starts.** This sheet deliberately does **not** carry their
contents — it names the SDK's call surface only; the usage rules, defaults and worked examples live in the
skills.

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 2 — constructing/registering `MaxioAdvancedBillingClient`, HttpClient ownership and lifetime |
| `dotnet-authentication` | Step 2 — supplying `BasicAuthCredentials` from `Maxio:ApiKey`, and 401 diagnosis |
| `dotnet-calling-endpoints` | Steps 3–8 — every `client.{Controller}.{Operation}(…)` call, parameter binding, cancellation |
| `dotnet-models` | Steps 4–8 — building `CreateCustomerRequest` / `CreateSubscriptionRequest`, reading envelopes and `StringEnum` values |
| `dotnet-configuration-resilience` | Step 2 and steps 5–7 — retries/timeouts/base URL/pagination, and their effect on Subscribe idempotency |
| `dotnet-error-handling` | Step 9 — the exception boundary for all three endpoints |
| `dotnet-testing` | Step 10 — tests for the integration layer |

**Two mandatory hazard rows — `System.Text.Json.JsonException` reaches the boundary from two directions and
they need opposite handling:**

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
  maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
  and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

Concretely, in this scope the `required` members that can trigger direction #1 are
`ProductResponse.Product`, `CustomerResponse.Customer`, `SiteResponse.Site`, and
`ErrorListResponse1.Errors`; `SubscriptionResponse.Subscription` and `ProductFamilyResponse.ProductFamily`
are nullable and will arrive as `null` instead of throwing. Source: `records-3-Of-Su.md`,
`records-4-Su-We.md`, `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md`.

---

## 5. How far these error contracts can be trusted

Two findings, both visible in the map itself — not from memory of this API and not a claim about live traffic:

1. **`CreateCustomerError`'s 422 accessor is very likely to yield nothing usable.**
   `TryGetCustomerErrorResponse1` hands back `CustomerErrorResponse1 { Errors (errors): Errors? }`, and the
   `Errors` record it points at declares exactly two fields: `PerPage (per_page): IReadOnlyList<string>?` and
   `PricePoint (price_point): IReadOnlyList<string>?`. That is a **shared, pagination-shaped model reused for
   a customer validation error** — a duplicate-`reference` or invalid-country message has no field to land
   in. Since neither field is `required` and unmodeled JSON is dropped, this deserializes successfully to an
   object with both properties `null` rather than throwing. **Directive: never build the user-facing message
   from `e422.Errors`. Extract best-effort — if `PerPage`/`PricePoint` happen to be populated, log them —
   then call `TryGetRawError(out var raw)` and log `raw.StatusCode` + `raw.ReadAsString()`, and return the
   generic rejection message to the caller.** Whether the live 422 body really carries a field-keyed error
   map that this model cannot represent is `UNVERIFIED` — only live traffic settles it, which is precisely
   why the fallback must be unconditional. Source: `records-1-Ac-Cr.md` (`CustomerErrorResponse1`, `Errors`),
   `operations/Customers.md`.

2. **`CreateSubscriptionError`'s 422 payload has a `required` member and is therefore the highest-risk
   deserialization point in this integration.** `ErrorListResponse1.Errors` is
   `IReadOnlyList<string> !req` — a required array of strings. If the live 422 body sends `errors` as an
   object/map, or omits it, constructing the error object fails and (per hazard row #2 above) a
   `JsonException` **replaces** `SdkException<CreateSubscriptionError>`, taking the 422 status with it. This
   is the single most likely place for a deterministic rejection to be misreported as an outage.
   **Directive: wrap the `CreateSubscription` call so that `JsonException` is caught alongside the
   `SdkException` arms and mapped to a client-error result (not a 5xx), with the exception logged in full;
   extract the message list best-effort and fall back to the generic message.** Which shape the live wire
   sends is `UNVERIFIED`. Source: `records-2-Cr-Ne.md` (`ErrorListResponse1`),
   `operations/Subscriptions.md`.

Everything else in §2.7 is directly generated: the `TryGet…` accessor names, the statuses they map to, and
each operation's Case A/B classification all come from the operation pages, and `SdkException<T>`'s shape
from the SDK source.

---

## 6. Assumptions & Blockers

### Assumptions

| # | Assumption |
|---|---|
| A1 | The customer **reference** key is derived from the JWT identity (the brief says "identity = email from JWT"). Deriving it — email verbatim, a hash, or a local user id — is an application decision (see the YOUR CALL rows below); the SDK only requires that it be a string that is unique per user, because `CreateCustomer`'s Notes state you may only create one customer for a given `reference`. |
| A2 | `Maxio:Subdomain` is the `{site}` value (`cp-exp-2`), assigned to `options.Server.Production.Us.Site`. |
| A3 | The account is US-hosted, so `Environment` stays at its `ServerEnvironment.Us` default. If `cp-exp-2` were EU-hosted, `Environment` must be `ServerEnvironment.Eu` **and** the override must be written to `Server.Production.Eu.*` instead — the `Us`/`Eu` option objects are independent and setting the wrong one silently does nothing. |
| A4 | `CreateCustomer` requires `FirstName`, `LastName` and `Email` (all `!req`). eShopOnWeb's JWT is assumed to yield an email; **first/last name have no SDK-side source**, so the application must supply something for both — they cannot be omitted or the object initializer will not compile. |
| A5 | Package version `1.0.2` (see §2.0) — confirm at restore; the in-repo `.csproj` disagrees with the tag. |

### YOUR CALL — decisions the map does not settle

| Decision | Note | Label |
|---|---|---|
| Caller identity → customer `reference` derivation | resolve from the app's own identity path | `YOUR CALL — not in the map` |
| First/last name values sent to `CreateCustomer` | the SDK requires both; their source is the application's user record | `YOUR CALL — not in the map` |
| Concurrency control for the double-click (per-user lock, unique constraint, or single-flight) | the SDK offers **no idempotency-key parameter on any operation in scope** — `CreateCustomer` and `CreateSubscription` take only `body` + `ct`. "Look up, then create" is the only SDK-level guard, and it is not atomic. Closing the remaining race is an application-side concurrency decision | `YOUR CALL — not in the map` |
| Which `SubscriptionState` values count as "already subscribed" for the idempotency guard | the enum's 15 values are in §2.6; which of them should block a second subscribe is a product decision | `YOUR CALL — not in the map` |
| Caching of `Sites.ReadSite()` currency and of the product-family id lookup | lifetime/invalidations are the application's | `YOUR CALL — not in the map` |
| HTTP status mapping for each SDK failure at the boundary | the SDK gives you the status; the API's contract to its own callers is yours | `YOUR CALL — not in the map` |

### Blockers

| # | Blocker |
|---|---|
| B1 | **The SDK cannot read a product family by handle.** `ProductFamilies.ReadProductFamily(int id, …)` takes an `int`, even though the operation's own Notes say the API accepts the `handle:my-family` form. The plan therefore resolves the family via `ListProductFamilies(null, null, null, null, null, ct: ct)` and matching `ProductFamily.Handle == "eshop-subscribe"` — that route is fully grounded and has no paging limit, so it is safe. Recording it as a blocker only because it is a real SDK gap the implementer should not try to work around by passing a handle string somewhere it does not belong. (`ListProductsForProductFamily` *does* take a `string productFamilyId`, but the SDK percent-escapes path parameters via `Uri.EscapeDataString`, so `"handle:eshop-subscribe"` would go on the wire as `handle%3Aeshop-subscribe`; whether Maxio accepts that is `UNVERIFIED` — use the numeric id from the family lookup.) Source: `operations/ProductFamilies.md`, SDK source `Core/TemplateParamsFactory.cs`. |
| B2 | **`perPage` maximum is undocumented** in both the map and the SDK source (`UNVERIFIED`). If the site ever holds more products in the family than one page, or more subscriptions than `ListCustomerSubscriptions` returns unpaged, the integration silently sees a truncated list. For `ListProductsForProductFamily`, page explicitly until a short page comes back. For `ListCustomerSubscriptions` there is **no paging parameter at all** — if a customer can accumulate more subscriptions than the API's implicit cap, this endpoint cannot be made complete with the SDK as generated, and that gap must be reported rather than worked around. |
| B3 | **NOT A BLOCKER — this row was wrong and is corrected here.** `Product.RequireCreditCard (require_credit_card): bool?` does **not** predict whether a card-less subscribe is rejected: the SDK documents it as *"Boolean that controls whether a payment profile is required to be entered for customers wishing to sign up on this product"* — it gates the **provider's hosted signup form**, not the API's balance collection. Its neighbour `RequestCreditCard (request_credit_card): bool?` is documented as *"Deprecated value that can be ignored unless you have legacy hosted pages"* — do not use it as a predictor either. This was demonstrated live: both seeded plans report `require_credit_card == false`, yet `CreateSubscription` with no payment fields was still rejected **422 "No payment method was on file for the $299.00 balance"**. **The real precondition is the collection method, not any product flag** — set `CreateSubscription.PaymentCollectionMethod` explicitly per §2.5, chosen from `Site.RelationshipInvoicingEnabled`. **Nothing on `Product` or `ProductPricePoint` predicts whether a plan is subscribable without a card** — the nearest candidate, `ProductPricePoint.TrialType (trial_type): TrialType?` (`NoObligation (no_obligation)` / `PaymentExpected (payment_expected)`), is documented only for how a **trial ends** with no card on file, which does not apply to these no-trial plans. `GET /api/subscription-plans` therefore cannot flag card-free subscribability from plan data alone; card-free subscribe is a property of the site's collection method, which is uniform across plans. Source: `records-3-Of-Su.md` (`Product`, `ProductPricePoint`, `Site`), `enums.md` (`TrialType`, `CollectionMethod`), `operations/Subscriptions.md`. |
