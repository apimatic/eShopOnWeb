# Maxio Advanced Billing — .NET SDK integration plan & CONTRACT SHEET (eShopOnWeb)

Scope: additive recurring-subscription billing on `src/PublicApi`, parallel to the existing
one-time Catalog→Basket→Order flow. Maxio Advanced Billing is the billing system of record;
eShopOnWeb stores no plan/price/state of its own.

---

## 1. Scope & sequence

| # | Step | Maxio operations used |
|---|---|---|
| 1 | Bind `Maxio:*` configuration; register the SDK client in DI (`src/PublicApi` composition root). | — (`AddMaxioAdvancedBillingClient`) |
| 2 | Write the integration boundary (one service type that wraps every SDK call and translates SDK exceptions into API results). | — |
| 3 | `GET /api/subscription-plans` — list the plans of the configured product family, drop archived ones, project to a plan DTO. | `client.ProductFamilies.ListProductsForProductFamily` (fallback: `client.ProductFamilies.ListProductFamilies`) |
| 4 | `POST /api/subscriptions` — ensure the Maxio customer exists for the caller (lookup-then-create by `reference`), then ensure the subscription exists (list-then-create), then return the confirmed subscription. | `client.Customers.ReadCustomerByReference` · `client.Customers.CreateCustomer` · `client.Customers.ListCustomerSubscriptions` · `client.Subscriptions.CreateSubscription` |
| 5 | `GET /api/my-subscriptions` — resolve the caller's Maxio customer, list their subscriptions, project plan/price/state/next-billing-date. | `client.Customers.ReadCustomerByReference` · `client.Customers.ListCustomerSubscriptions` |
| 6 | Tests for the integration boundary (happy path + each error path in §2.6). | — |

Sequencing note: step 2 must exist before steps 3–5 are written — the error boundary shape
(see §4) is decided once and every endpoint routes through it.

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

### 2.1 Package, namespaces, client construction, auth, base URL

| Fact | Value | Source |
|---|---|---|
| NuGet package id | `AsadAli.AdvancedBilling.Sdk` | `sdk-map.md` |
| Version to reference | **`1.0.2`** — published on nuget.org (only `1.0.0`, `1.0.1`, `1.0.2` exist; `1.0.2` is the newest and is the exact version the SDK map was generated from, tag `v1.0.2`, commit `15db14b`). `<PackageReference Include="AsadAli.AdvancedBilling.Sdk" Version="1.0.2" />` | `sdk-map.md` + nuget.org flat index for this package id |
| Root namespace (differs from the package id) | `MaxioAdvancedBilling` | `sdk-map.md` |
| Target framework | `netstandard2.0` — restorable from any modern .NET TFM in eShopOnWeb | `sdk-map.md` |
| Transitive runtime deps | `Polly`, `Microsoft.Extensions.Http`, `System.Net.Http.Json`, `System.Net.ServerSentEvents` | `sdk-map.md` |

Namespaces to `using` (C# does **not** import child namespaces transitively — one `using` per kind):

| Type(s) you will reference | Namespace | Source |
|---|---|---|
| `MaxioAdvancedBillingClient`, `MaxioAdvancedBillingClientOptions`, `ServerOptions`, `ServiceCollectionExtensions.AddMaxioAdvancedBillingClient` | `MaxioAdvancedBilling` | `sdk-map.md` (root-level sources `MaxioAdvancedBillingClient.cs`, `MaxioAdvancedBillingClientOptions.cs`, `ServerOptions.cs`, `ServiceCollectionExtensions.cs`) |
| `ServerEnvironment`, `ProductionOptions` (+ nested `ProductionOptions.UsOptions` / `.EuOptions`), `EbbOptions` | `MaxioAdvancedBilling.Servers` | `sdk-map.md` (`Servers/…`) |
| `BasicAuthCredentials` | `MaxioAdvancedBilling.Core.Authentication.Basic` | `sdk-map.md` (`Core/Authentication/Basic/BasicAuthCredentials.cs`) |
| `RetryOptions` | `MaxioAdvancedBilling.Core.Configuration` | `sdk-map.md` |
| `SdkException<TError>` | `MaxioAdvancedBilling.Core.Exceptions` | `sdk-map.md` (`Core/Exceptions/SdkException.cs`) |
| `RawError`, `ApiError` | `MaxioAdvancedBilling.Core.ErrorResponse` | `sdk-map.md` (`Core/ErrorResponse/…`) |
| Controller classes (`Products`, `ProductFamilies`, `Customers`, `Subscriptions`) | `MaxioAdvancedBilling.Api` | `sdk-map.md` |
| All records (`Product`, `Customer`, `Subscription`, `Create…Request`, error payload records) | `MaxioAdvancedBilling.Models` | `sdk-map.md` + `map/models/records-*.md` header |
| All enums (`SubscriptionState`, `IntervalUnit`, `SubscriptionStateFilter`, `BasicDateField`, …) | `MaxioAdvancedBilling.Models.Enums` | `map/models/enums.md` |
| Typed error classes (`CreateCustomerError`, `CreateSubscriptionError`, `ListProductsForProductFamilyError`) | `MaxioAdvancedBilling.Errors` | `sdk-map.md` |

**Client construction — the only constructor:**
`MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` (`sdk-map.md`).

**DI registration** (`ServiceCollectionExtensions.cs`, namespace `MaxioAdvancedBilling`):
`AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>? configure = null)`, an extension
on `IServiceCollection`. Internally it calls `services.AddHttpClient()`, builds the options **eagerly at
registration time** by invoking your callback once, and registers `MaxioAdvancedBillingClient` as a
**singleton** whose `HttpClient` comes from `IHttpClientFactory.CreateClient()` (the default, unnamed
client). Consequence for eShopOnWeb: the `Maxio:*` values must be readable from `IConfiguration` **at
`Program.cs` registration time** — the options object is captured once, not resolved per request, so an
`IOptionsMonitor`-style hot reload of `Maxio:*` will not reach the client. Source:
`ServiceCollectionExtensions.cs` (SDK source; the file is named by `sdk-map.md`).

**`MaxioAdvancedBillingClientOptions` members and their defaults** (`MaxioAdvancedBillingClientOptions.cs`;
map row: `sdk-map.md`):

| Property | Type | Default |
|---|---|---|
| `Environment` | `MaxioAdvancedBilling.Servers.ServerEnvironment` | `ServerEnvironment.Default()` (= `Us`) |
| `Retry` | `MaxioAdvancedBilling.Core.Configuration.RetryOptions` | `RetryOptions.Default()` |
| `Server` | `MaxioAdvancedBilling.ServerOptions` | `new()` |
| `BasicAuth` | `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?` | `null` |

**Auth (exact shape).** HTTP Basic; **`Username` = the Maxio API key, `Password` = the literal string `"x"`**:

```csharp
o.BasicAuth = new BasicAuthCredentials { Username = cfg["Maxio:ApiKey"]!, Password = "x" };
```

Source: `sdk-map.md` ("Servers & auth"); the SDK's XML doc on `MaxioAdvancedBillingClientOptions.BasicAuth`
states the same convention verbatim.

**Base URL — both required modes.** `ServerOptions` is `{ ProductionOptions Production; EbbOptions Ebb; }`,
each defaulted to `new()`, and `ProductionOptions` is `{ UsOptions Us; EuOptions Eu; }`, also defaulted — so
you mutate the defaults and never have to construct the whole tree. `ProductionOptions.UsOptions` has
exactly two settable properties (source: `Servers/ProductionOptions.cs`; map row: `sdk-map.md`):

| Member | Type | Default |
|---|---|---|
| `BaseUrl` | `string` | `"https://{site}.chargify.com"` |
| `Site` | `string` | `"subdomain"` |

The final URL is produced by literal string-replacement of `{site}` in `BaseUrl` with `Site`, then
`BaseUrl.TrimEnd('/') + "/" + path` (source: `Core/TemplateParamsFactory.cs`). That gives both modes:

```csharp
// (a) subdomain-derived — leave BaseUrl at its "https://{site}.chargify.com" default:
o.Server.Production.Us.Site = cfg["Maxio:Subdomain"]!;   // "cp-exp-2" -> https://cp-exp-2.chargify.com

// (b) verbatim override — a BaseUrl with no "{site}" placeholder passes through unchanged:
var baseUrl = cfg["Maxio:BaseUrl"];
if (!string.IsNullOrWhiteSpace(baseUrl)) o.Server.Production.Us.BaseUrl = baseUrl;
```

When `Maxio:BaseUrl` is set, `Site` becomes irrelevant (there is nothing to substitute) — setting both is
harmless. `Maxio:Subdomain` is **required** in mode (a): leaving it unset silently yields
`https://subdomain.chargify.com`, not an error. Every operation in this plan is on the **Production**
server group (`sdk-map.md`), so `options.Server.Ebb` is never touched, and `Environment` stays at its `Us`
default unless the account is EU-hosted.

### 2.2 Operations

| Step | Controller property · signature (verbatim) | Request model + fields | Response envelope + fields read | Error case + accessors | Pagination | Source |
|---|---|---|---|---|---|---|
| 3 | `client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | none (query params only). `productFamilyId` per the SDK's own param doc = **"Either the product family's id or its handle prefixed with `handle:`"** ⇒ pass `"handle:" + cfg["Maxio:ProductFamilyHandle"]` → `"handle:eshop-subscribe"`. The 8 params `dateField`…`include` have **no default — pass `null` explicitly**; pass `includeArchived: false` | `IReadOnlyList<ProductResponse>`; `ProductResponse.Product: Product` (**`!req`, non-null**). Read `Product.Handle/Name/Description/PriceInCents/Interval/IntervalUnit/ArchivedAt/ProductFamily` | **Case A** — `SdkException<ListProductsForProductFamilyError>`; `TryGetString(out string)` **[404]** (payload is a bare `string`) · `TryGetRawError(out RawError)` [fallback] | manual `page` + `perPage` (defaults 1 / 20) | `operations/ProductFamilies.md` |
| 3 (fallback only) | `client.ProductFamilies.ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` | none; all 5 params have **no default — pass `null`** | `IReadOnlyList<ProductFamilyResponse>`; `ProductFamilyResponse.ProductFamily: ProductFamily?` (**nullable**). Match `ProductFamily.Handle == "eshop-subscribe"`, take `ProductFamily.Id` (`int?`) | **Case B** — `SdkException<RawError>`; `StatusCode` · `ReadAsString()` · `ReadAsJson<T>()` · `ReadAsBytes()` | none — returns every family in one call | `operations/ProductFamilies.md` |
| 4, 5 | `client.Customers.ReadCustomerByReference(string reference, CancellationToken ct = default)` — `GET /customers/lookup.json`, `reference` ← query `reference`. Notes: *"Returns a customer by their unique reference ID. It will return a single match."* | none | `CustomerResponse`; `CustomerResponse.Customer: Customer` (**`!req`, non-null**). Read `Customer.Id (int?)`, `Reference`, `Email` | **Case B** — `SdkException<RawError>`; status via `ex.Error.StatusCode` | none | `operations/Customers.md` |
| 4 | `client.Customers.CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `POST /customers.json`; `body` nullable with **no default → must pass explicitly** | `CreateCustomerRequest { Customer (customer): CreateCustomer !req }`. `CreateCustomer` required: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`; also set `Reference (reference): string?` = the caller's stable app key (§2.5) | `CustomerResponse`; `Customer.Id (int?)` is what step 4 carries forward | **Case A** — `SdkException<CreateCustomerError>`; `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` **[422]** · `TryGetRawError(out RawError)` [fallback]. Payload `CustomerErrorResponse1 { Errors (errors): Errors? }` — **suspect shape, see §2.6** | none | `operations/Customers.md`, `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md` |
| 4, 5 | `client.Customers.ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — `GET /customers/{customer_id}/subscriptions.json`. Notes: *"Lists all subscriptions that belong to a customer."* | none | `IReadOnlyList<SubscriptionResponse>`; `SubscriptionResponse.Subscription: Subscription?` (**nullable — null-check every element**) | **Case B** — `SdkException<RawError>` | **none — the signature has no `page`/`perPage`.** There is no way to page this call; you get whatever the site returns in one response | `operations/Customers.md`, `records-4-Su-We.md` |
| 4 | `client.Subscriptions.CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `POST /subscriptions.json`; `body` nullable, **no default → must pass explicitly** | `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription !req }`. `CreateSubscription` marks **nothing** `required` — fields to set are in §2.4 | `SubscriptionResponse`; `Subscription: Subscription?` (**nullable**) | **Case A** — `SdkException<CreateSubscriptionError>`; `TryGetErrorListResponse1(out ErrorListResponse1)` **[422]** · `TryGetRawError(out RawError)` [fallback]. Payload `ErrorListResponse1 { Errors (errors): IReadOnlyList<string> !req }` | none | `operations/Subscriptions.md`, `records-2-Cr-Ne.md`, `records-4-Su-We.md` |
| — (**not used**; listed because it is the obvious wrong choice for step 5) | `client.Subscriptions.ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | none; 14 params `state`…`include` have **no default** | `IReadOnlyList<SubscriptionResponse>` | **Case B** — `SdkException<RawError>` | manual `page` + `perPage` (1 / 20) | `operations/Subscriptions.md` |

**Why step 5 must not use `ListSubscriptions`:** its query params (`state`, `product`,
`product_price_point_id`, `coupon`, `coupon_code`, `date_field`, `start_date`, `end_date`,
`start_datetime`, `end_datetime`, `metadata`, `direction`, `sort`, `include`, `page`, `per_page`) contain
**no customer filter** — there is no `customer_id` / `customer_reference` parameter. Filtering by customer
is only available through `ListCustomerSubscriptions`. Filtering by **state** is only available on
`ListSubscriptions` (via `SubscriptionStateFilter`), so a per-customer state filter has to be applied in
your own code over the `ListCustomerSubscriptions` result. Source: `operations/Subscriptions.md`,
`operations/Customers.md`.

Available and **not needed** here: `client.Products.ReadProductByHandle(string apiHandle, CancellationToken ct = default)`
→ `ProductResponse`, **Case B** `SdkException<RawError>` (`operations/Products.md`) — use it only to confirm
a single plan handle outside the family listing. `client.Products.ListProducts` lists the whole **site**,
not one family, so it is the wrong operation for step 3.

### 2.3 Response models — the fields the endpoints read

`Product` (namespace `MaxioAdvancedBilling.Models`; source `records-3-Of-Su.md`) — every field nullable:

| Field (wire) | Type | Use |
|---|---|---|
| `Handle (handle)` | `string?` | plan id in your API surface (`eshop-pro`, `basic-plan`) — the value POSTed back to `/api/subscriptions` |
| `Name (name)` | `string?` | display name |
| `Description (description)` | `string?` | display description |
| `PriceInCents (price_in_cents)` | `long?` | **cents** — `$299.00` ⇒ `29900`, `$29.00` ⇒ `2900`. Divide by 100 for display; it is `long`, not `int` |
| `Interval (interval)` | `int?` | billing interval count |
| `IntervalUnit (interval_unit)` | `IntervalUnit?` | enum — §2.7 (`day`, `month` only) |
| `ArchivedAt (archived_at)` | `DateTimeOffset?` | **the archived flag** — non-null ⇒ archived. Filter to `ArchivedAt is null` |
| `ProductFamily (product_family)` | `ProductFamily?` | nested: `Id`, `Name`, `Handle`, `AccountingCode`, `Description`, `CreatedAt`, `UpdatedAt`, `ArchivedAt` — all nullable |
| `TrialPriceInCents / TrialInterval / TrialIntervalUnit` | `long? / int? / IntervalUnit?` | expected null/zero (no trial on these plans) |
| `InitialChargeInCents (initial_charge_in_cents)` | `long?` | expected null/zero (no setup fee) |
| `RequireCreditCard (require_credit_card)` / `RequestCreditCard (request_credit_card)` | `bool?` / `bool?` | expected `false` — **two distinct generated fields**, do not treat them as one |
| `ExpirationInterval / ExpirationIntervalUnit` | `int? / ExpirationIntervalUnit?` | `ExpirationIntervalUnit` has a `Never (never)` member (§2.7) |
| `Taxable (taxable)` | `bool?` | expected `false` |
| `DefaultProductPricePointId`, `ProductPricePointId`, `ProductPricePointHandle`, `ProductPricePointName` | `int? / int? / string? / string?` | not needed — omitting the price point on create uses the product default |

**There is no `hidden` / `visible` / `public` boolean on `Product`.** The only archival signals in the map
are `ArchivedAt` above and the request-level `includeArchived` query parameter. Filter on both: pass
`includeArchived: false` **and** drop rows where `ArchivedAt is not null` — the parameter is `bool?` and
the map does not document its server-side default when omitted. Source: `records-3-Of-Su.md`,
`operations/ProductFamilies.md`.

`Subscription` (namespace `MaxioAdvancedBilling.Models`; source `records-3-Of-Su.md`) — the fields
`/api/my-subscriptions` and the `POST` confirmation read:

| Field (wire) | Type | Use |
|---|---|---|
| `Id (id)` | `int?` | subscription id |
| `State (state)` | `SubscriptionState?` | **`StringEnum`, not a C# enum** — values in §2.7 |
| `PreviousState (previous_state)` | `SubscriptionState?` | diagnostics only |
| `NextAssessmentAt (next_assessment_at)` | `DateTimeOffset?` | **the "next billing date"** to show |
| `CurrentPeriodEndsAt (current_period_ends_at)` | `DateTimeOffset?` | current period end; display fallback when `NextAssessmentAt` is null |
| `CurrentPeriodStartedAt (current_period_started_at)` | `DateTimeOffset?` | optional |
| `ProductPriceInCents (product_price_in_cents)` | `long?` | **the price actually being billed**, in cents |
| `CurrentBillingAmountInCents (current_billing_amount_in_cents)` | `long?` | amount of the next bill, in cents (diverges from `ProductPriceInCents` once components/coupons apply) |
| `Product (product)` | `Product?` | **nested full `Product`** — read `Product.Handle`, `.Name`, `.PriceInCents`, `.Interval`, `.IntervalUnit` from here; no second call needed |
| `Customer (customer)` | `Customer?` | nested full `Customer` |
| `CreatedAt (created_at)` | `DateTimeOffset?` | creation timestamp |
| `ActivatedAt (activated_at)` | `DateTimeOffset?` | activation timestamp |
| `Reference (reference)` | `string?` | your app's reference echoed back (§2.5) |
| `CanceledAt`, `CancelAtEndOfPeriod`, `ExpiresAt`, `DelayedCancelAt` | `DateTimeOffset? / bool? / DateTimeOffset? / DateTimeOffset?` | optional lifecycle display |
| `Currency (currency)` | `string?` | display currency |

**There is no `next_billing_at` on the `Subscription` response model** — `NextBillingAt` exists only on the
**request** model `CreateSubscription`. Use `NextAssessmentAt`, fall back to `CurrentPeriodEndsAt`, and make
the DTO field nullable. Source: `records-3-Of-Su.md`, `records-2-Cr-Ne.md`.

`Customer` (source `records-2-Cr-Ne.md`) — read `Id (id): int?`, `Reference (reference): string?`,
`Email (email): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?`.

**Response envelopes — reads go one level down, and the four envelopes are NOT alike:**

| Envelope | Inner field | Required? | Source |
|---|---|---|---|
| `ProductResponse` | `Product (product): Product` | **`!req` — non-nullable** | `records-3-Of-Su.md` |
| `CustomerResponse` | `Customer (customer): Customer` | **`!req` — non-nullable** | `records-2-Cr-Ne.md` |
| `SubscriptionResponse` | `Subscription (subscription): Subscription?` | **nullable — null-check it** | `records-4-Su-We.md` |
| `ProductFamilyResponse` | `ProductFamily (product_family): ProductFamily?` | **nullable — null-check it** | `records-3-Of-Su.md` |

Consequence: `subscriptionResponse.Subscription` and `familyResponse.ProductFamily` can legitimately be
`null` and your code must handle it; `productResponse.Product` and `customerResponse.Customer` cannot be
`null` — but that is enforced by *deserialization*, an entirely different failure mode (first hazard row
in §4).

### 2.4 `CreateSubscription` — the request fields to set (and the ones deliberately left out)

`CreateSubscriptionRequest { Subscription (subscription): CreateSubscription !req }`.
**`CreateSubscription` marks NOTHING `required`**, so `required?` selects nothing for you and the compiler
will happily send an empty object. The provider-side requirements come from the SDK's own per-field
documentation (read from `Models/CreateSubscription.cs`; map row: `records-2-Cr-Ne.md`):

| Field (wire) | Type | Set it? | Provider-documented rule |
|---|---|---|---|
| `ProductHandle (product_handle)` | `string?` | **yes** — `"eshop-pro"` / `"basic-plan"` | *"The API Handle of the product for which you are creating a subscription. **Required, unless a `product_id` is given instead**."* |
| `ProductId (product_id)` | `int?` | no | the id alternative to `ProductHandle`; ids are not stable in this sandbox |
| `CustomerId (customer_id)` | `int?` | **yes** — from step 4's lookup/create | *"The ID of an existing customer within Chargify. **Required, unless a `customer_reference` or a set of `customer_attributes` is given**."* |
| `CustomerReference (customer_reference)` | `string?` | no (alternative to `CustomerId`) | *"The reference value (provided by your app) of an existing customer within Chargify. Required, unless a `customer_id` or a set of `customer_attributes` is given."* — usable instead of the explicit lookup, but then you never learn the customer id |
| `CustomerAttributes (customer_attributes)` | `CustomerAttributes?` | no | third alternative: creates the customer inline; its own `Reference (reference): string?` field is how a one-call flow would keep idempotency. Not used here because the two-step flow gives a cleaner 422 boundary |
| `Reference (reference)` | `string?` | **yes** — the caller's stable app key | SDK doc: *"The reference value (provided by your app) for the subscription itself."* This is what `FindSubscription` looks up |
| `ProductPricePointHandle` / `ProductPricePointId` | `string? / int?` | no | *"The user-friendly API handle of a product's particular price point."* Omitting both uses the product's default price point |
| `PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes` | — | **no** | these plans do not require a payment method, so no card capture / 3-DS path |
| `PaymentCollectionMethod (payment_collection_method)` | `CollectionMethod?` | no (leave to the site default) | enum values in §2.7 |
| `Ref (ref)` | `string?` | **NO — never set this** | SDK doc: *"A valid **referral code**… If supplied, must be valid, or else subscription creation will fail."* `Ref`/`ref` is **not** short for `Reference`/`reference`; putting your app key here fails the create |
| `DeferSignup (defer_signup)` | `bool? = false` | no | generated default `false` |
| `NextBillingAt`, `InitialBillingAt`, `PreviousBillingAt`, `CalendarBilling`, `ExpiresAt` | — | no | scheduling overrides, outside the hero flow |
| `CouponCode`, `CouponCodes`, `OfferId`, `Components`, `Metafields`, `Group` | — | no | out of scope (the `api-call` metered component is **not** attached at signup) |

All optional fields carry `[JsonIgnore(WhenWritingNull)]`, so unset fields are omitted from the JSON body
rather than sent as `null` (source: `Models/CreateSubscription.cs`).

Minimal accepted body for this integration:

```csharp
new CreateSubscriptionRequest
{
    Subscription = new CreateSubscription
    {
        ProductHandle = planHandle,     // "eshop-pro" (default) or "basic-plan"
        CustomerId    = customerId,     // int, from step 4
        Reference     = callerAppKey,   // your stable per-caller/per-plan key
    }
}
```

`CreateCustomerRequest { Customer (customer): CreateCustomer !req }` — `CreateCustomer` requires
**`FirstName`, `LastName`, `Email`** (all `string !req`; the compiler enforces these) and you must also set
`Reference (reference): string?` for idempotency (§2.5). Everything else (`Organization`, `Address`,
`Address2`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `VatNumber`, `TaxExempt`,
`TaxExemptReason`, `ParentId`, `SalesforceId`, `CcEmails`) is optional and left unset. The CreateCustomer
Notes warn that **if** you send `country` it must be a 2-character ISO 3166-1 code and **if** you send
`state` it must be an ISO 3166-2 code — we send neither, so that rule cannot bite. Source:
`operations/Customers.md`, `records-1-Ac-Cr.md`.

### 2.5 Idempotency — what Maxio guarantees and what it does not

| Question | Answer | Source |
|---|---|---|
| Does any operation accept an idempotency key / `Idempotency-Key` header? | **No.** `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` and `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` take only a body and `ct` — no header parameter — and neither `CreateSubscription` nor `CreateCustomer` carries an idempotency-key field | `operations/Subscriptions.md`, `operations/Customers.md`, `records-2-Cr-Ne.md`, `records-1-Ac-Cr.md` |
| Is customer `reference` unique? | **Yes, provider-enforced.** CreateCustomer Notes, verbatim: *"The only validation restriction is that you may only create one customer for a given reference value. If provided, the `reference` value must be unique. It represents a unique identifier for the customer from your own app, i.e. the customer's ID."* This makes the **customer** half safe against a double-click: the second create is rejected by the provider rather than deduplicated into a second row | `operations/Customers.md` |
| What status/type does a duplicate `reference` produce? | `CreateCustomer` is Case A; its only status-specific accessor is `TryGetCustomerErrorResponse1` **[422]**, everything else falls to `TryGetRawError`. That *duplicate reference* specifically lands on 422 rather than another 4xx is **not** stated in the map. **Directive:** catch `SdkException<CreateCustomerError>` and, on **any** status, re-run `ReadCustomerByReference`; if it now returns a customer, treat the create as a lost race and continue with that customer. Surface an error only if the re-lookup also fails | `operations/Customers.md` — **UNVERIFIED** (status code) |
| Is subscription `reference` unique? | **Not documented anywhere in the map.** `FindSubscription(string? reference, CancellationToken ct = default)` retrieves *"a subscription by its reference"* (Case A; `TryGetNoContent(out RawError)` [404] · `TryGetRawError` [fallback]), which implies at most one match, but no uniqueness constraint is stated for creation. **Do not rely on it as a dedupe guarantee** — treat `Reference` as a lookup aid only | `operations/Subscriptions.md` — **UNVERIFIED** |
| Does anything on the Maxio side stop a double-click creating two subscriptions? | **No.** There is no idempotency key and no documented uniqueness on subscription `reference`. The only Maxio-side tool is read-then-write: `ListCustomerSubscriptions(customerId)` before `CreateSubscription`. That is inherently racy | `operations/Customers.md`, `operations/Subscriptions.md` |
| Closing that race | Serializing concurrent `POST /api/subscriptions` for the same caller (per-caller lock, DB unique constraint on (user, plan), queue) — the SDK offers no primitive for it | **YOUR CALL — not in the map** |
| The caller's stable app key (what goes into customer `reference`) | eShopOnWeb user id vs. email, and how it is read out of the JWT | **YOUR CALL — not in the map** |

Step-4 sequence implied by the above:

1. `ReadCustomerByReference(callerKey, ct: ct)` → on success use `resp.Customer`. On
   `SdkException<RawError>` with `ex.Error.StatusCode == HttpStatusCode.NotFound` → go to 2; on any other
   status → surface through the boundary.
2. `CreateCustomer(body, ct: ct)` with `Reference = callerKey`. On `SdkException<CreateCustomerError>` →
   re-run `ReadCustomerByReference(callerKey)`; if it succeeds, continue with that customer, else fail.
3. `ListCustomerSubscriptions(customer.Id!.Value, ct: ct)` → if any element has
   `Subscription?.Product?.Handle == planHandle` and a live `State` (§2.7), **return that subscription
   without creating anything**.
4. Otherwise `CreateSubscription(body, ct: ct)` and return the created subscription.

`Customer.Id` is `int?` while `ListCustomerSubscriptions` takes a non-nullable `int` — handle the null case
explicitly rather than `!`-suppressing it.

> **UNVERIFIED — `ReadCustomerByReference` not-found status.** The SDK applies no status whitelist to this
> operation (every non-2xx becomes `SdkException<RawError>`; source `Api/Customers.cs`), and the map does
> not state which status a missing reference returns. **Directive:** branch to "create the customer" on
> `HttpStatusCode.NotFound` **only**; on any other non-2xx do not create — surface the failure. If live
> traffic shows a different not-found status (or a 2xx with an unexpected body), widen that branch then,
> not now.

### 2.6 Error handling — types, status, body

Every operation in this SDK is **throw-only**; there are no `…Result` / no-throw variants anywhere
(`sdk-map.md`). Of 247 operations, 163 are Case A and 84 are Case B, so the case must be checked per
operation — it is not a property of the verb.

| Operation | Exception type to catch | How to read the HTTP status | How to read the error body |
|---|---|---|---|
| `ListProductsForProductFamily` | `SdkException<ListProductsForProductFamilyError>` (`MaxioAdvancedBilling.Errors`) | `ex.Error.TryGetString(out var msg)` ⇒ **404**; otherwise `ex.Error.TryGetRawError(out var raw)` ⇒ `raw.StatusCode` | 404 payload is a bare `string`; fallback `RawError.ReadAsString()` |
| `CreateCustomer` | `SdkException<CreateCustomerError>` | `ex.Error.TryGetCustomerErrorResponse1(out var e)` ⇒ **422**; otherwise `TryGetRawError(out var raw)` ⇒ `raw.StatusCode` | `CustomerErrorResponse1 { Errors (errors): Errors? }` — **see the trust note below** |
| `CreateSubscription` | `SdkException<CreateSubscriptionError>` | `ex.Error.TryGetErrorListResponse1(out var e)` ⇒ **422**; otherwise `TryGetRawError(out var raw)` ⇒ `raw.StatusCode` | `ErrorListResponse1 { Errors (errors): IReadOnlyList<string> !req }` — a flat list of message strings, ready to join for the API response |
| `ReadCustomerByReference`, `ListCustomerSubscriptions`, `ListProductFamilies`, `ListSubscriptions`, `ReadProductByHandle` | `SdkException<RawError>` | `ex.Error.StatusCode` (`System.Net.HttpStatusCode`) | `ex.Error.ReadAsString()` · `ReadAsJson<T>()` · `ReadAsBytes()` |

Core error types (`MaxioAdvancedBilling.Core.ErrorResponse`, from `sdk-map.md`): `ApiError` (abstract base
of all typed errors) exposes only `TryGetRawError(out RawError): bool`; `RawError` exposes
`StatusCode: HttpStatusCode`, `ReadAsBytes(): ReadOnlyMemory<byte>`, `ReadAsString(): string`,
`ReadAsJson<T>(): T?`.

**Which type is thrown for 401 / 404 / 422** — the answer depends on the operation, not the status:

| Status | On a Case A op (`CreateCustomer`, `CreateSubscription`, `ListProductsForProductFamily`) | On a Case B op (`ReadCustomerByReference`, `ListCustomerSubscriptions`, …) |
|---|---|---|
| **401** (bad/missing API key) | `SdkException<{Op}Error>`; **none** of these three has a 401-specific accessor, so it reaches `TryGetRawError(out var raw)` with `raw.StatusCode == Unauthorized` | `SdkException<RawError>`, `ex.Error.StatusCode == Unauthorized` |
| **404** | `ListProductsForProductFamily` only: `TryGetString(out string)`. `CreateCustomer` / `CreateSubscription` have no 404 accessor ⇒ `TryGetRawError` | `SdkException<RawError>`, `StatusCode == NotFound` |
| **422** | `CreateCustomer`: `TryGetCustomerErrorResponse1`. `CreateSubscription`: `TryGetErrorListResponse1`. `ListProductsForProductFamily` has no 422 accessor ⇒ `TryGetRawError` | `SdkException<RawError>`, `StatusCode == UnprocessableEntity` |

Source for the subsection: `operations/Customers.md`, `operations/Subscriptions.md`,
`operations/ProductFamilies.md`, `operations/Products.md`, `sdk-map.md` ("Error-handling model").

> **Trust judgment — `CustomerErrorResponse1` is a suspect generated shape (UNVERIFIED).**
> Evidence visible in the map: `CustomerErrorResponse1` has the single field `Errors (errors): Errors?`,
> and the `Errors` record it points at declares exactly two fields —
> `PerPage (per_page): IReadOnlyList<string>?` and `PricePoint (price_point): IReadOnlyList<string>?`
> (`records-2-Cr-Ne.md`, `Models/Errors.cs`). Neither has anything to do with customer validation, and the
> same `Errors` record is shared across unrelated operations. Compare the sibling payload
> `ErrorListResponse1 { Errors: IReadOnlyList<string> !req }` that `CreateSubscription` uses for the *same*
> 422 role: the two generated definitions of "the errors of a 422" **disagree in kind** (object vs. array
> of strings). Which one the live customer-create 422 matches can only be settled by live traffic.
> **Directive (defensive coding):** in the `CreateCustomer` 422 branch extract `e.Errors?.PerPage` /
> `e.Errors?.PricePoint` **best-effort** — if both are null or empty, fall back to the generic message
> rather than surfacing an empty error list; and wrap that branch so a `JsonException` raised *while the
> error object is being constructed* is handled by the boundary described in §4 instead of escaping as an
> outage.

> **UNVERIFIED — the `handle:` path segment is percent-encoded by the SDK.**
> `ListProductsForProductFamily` documents `productFamilyId` as *"Either the product family's id or its
> handle prefixed with `handle:`"* (SDK source `Api/ProductFamilies.cs`) and passes it as
> `new TemplateParam("product_family_id", productFamilyId)`. The SDK's template expander runs every value
> through `Uri.EscapeDataString` (`Core/TemplateParamsFactory.cs`), so the request path becomes
> `/product_families/handle%3Aeshop-subscribe/products.json`. Whether the provider accepts the
> percent-encoded colon is only confirmable on the wire. **Directive:** call it with
> `"handle:" + familyHandle` first; if it throws `SdkException<ListProductsForProductFamilyError>` reporting
> **404** (via `TryGetString` or `TryGetRawError`), fall back **once** to
> `ListProductFamilies(null, null, null, null, null, ct: ct)`, match `ProductFamily.Handle`, and retry with
> `family.Id!.Value.ToString(CultureInfo.InvariantCulture)`. Do not swallow any other status.

### 2.7 Enums (verbatim — `StringEnum<T>` records in `MaxioAdvancedBilling.Models.Enums`, **not** C# enums)

Members are written `CSharpMemberName (wire_value)`; construct with the static member or
`Type.FromValue("wire")`. Source for this whole subsection: `map/models/enums.md`.

`SubscriptionState` — `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`,
`Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`,
`Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`,
`TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)`.

The enum's own doc warns that `assessing` is *"an internal (transient) state… Do not base any access
decisions in your app on this state, as it may not always be exposed."* For the step-4 "already
subscribed?" check, which states mean *do not create a second subscription*
(`Active`/`Trialing`/`Pending`/`AwaitingSignup` being the obvious starting set) is a product decision —
**YOUR CALL — not in the map**.

`SubscriptionStateFilter` (valid **only** on `ListSubscriptions`'s `state` param — a different, smaller
list than `SubscriptionState`: it adds `ExpiredCards`, `PendingCancellation`, `PendingRenewal` and lacks
`Pending`, `FailedToCreate`, `Assessing`, `SoftFailure`, `Paused`, `AwaitingSignup`) —
`Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`,
`OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`,
`PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`,
`Trialing (trialing)`, `Unpaid (unpaid)`.

`IntervalUnit` — `Day (day)`, `Month (month)`. **Only two members; there is no `Year`.** A yearly plan is
expressed as `Interval = 12` + `IntervalUnit.Month`.

`ExpirationIntervalUnit` — `Day (day)`, `Month (month)`, `Never (never)`.

`CollectionMethod` — `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`,
`Invoice (invoice)`.

`BasicDateField` (the `dateField` param on both list ops) — `UpdatedAt (updated_at)`,
`CreatedAt (created_at)`.

`ListProductsInclude` (the `include` param on the product list ops) —
`PrepaidProductPricePoint (prepaid_product_price_point)` (single member).

`PricePointType` — `Catalog (catalog)`, `Default (default)`, `Custom (custom)`.

`SortingDirection` — `Asc (asc)`, `Desc (desc)`.

`SubscriptionSort` — `SignupDate (signup_date)`, `PeriodStart (period_start)`, `PeriodEnd (period_end)`,
`NextAssessment (next_assessment)`, `UpdatedAt (updated_at)`, `CreatedAt (created_at)`,
`TotalPayments (total_payments)`, `Id (id)`, `OpenBalance (open_balance)`, `ExpiresAt (expires_at)`.

`SubscriptionDateField` — `CurrentPeriodEndsAt (current_period_ends_at)`,
`CurrentPeriodStartsAt (current_period_starts_at)`, `CreatedAt (created_at)`, `ActivatedAt (activated_at)`,
`CanceledAt (canceled_at)`, `ExpiresAt (expires_at)`, `TrialStartedAt (trial_started_at)`,
`TrialEndedAt (trial_ended_at)`, `UpdatedAt (updated_at)`.

`SubscriptionListInclude` — `SelfServicePageToken (self_service_page_token)` (single member).

`ListProductsFilter` (the `filter` param, a **record** not an enum; source `records-2-Cr-Ne.md`) —
`Ids (ids): IReadOnlyList<int>?`, `PrepaidProductPricePoint (prepaid_product_price_point): PrepaidProductPricePointFilter?`,
`UseSiteExchangeRate (use_site_exchange_rate): bool?`. Not needed here — pass `filter: null`.

### 2.8 Configuration keys

| Binding key | Required | Consumed as | Default if unset |
|---|---|---|---|
| `Maxio:ApiKey` | yes | `o.BasicAuth.Username` (password is the literal `"x"`) | none — `BasicAuth` defaults to `null` ⇒ unauthenticated calls ⇒ 401 |
| `Maxio:Subdomain` | yes, unless `Maxio:BaseUrl` is set | `o.Server.Production.Us.Site` | SDK default `"subdomain"` ⇒ `https://subdomain.chargify.com` (silently wrong, not an error) |
| `Maxio:BaseUrl` | optional | `o.Server.Production.Us.BaseUrl`, **verbatim** (any `{site}` placeholder is substituted; a URL without one passes through unchanged, trailing `/` trimmed) | SDK default `"https://{site}.chargify.com"` |
| `Maxio:ProductFamilyHandle` | yes | prefixed with `handle:` and passed as `productFamilyId` | none — a missing value yields `"handle:"` and a 404 |

Sandbox values for this environment: `Maxio:Subdomain` = `cp-exp-2`, `Maxio:ProductFamilyHandle` =
`eshop-subscribe`. Plan handles `eshop-pro` (default subscribe target, `PriceInCents` 29900) and
`basic-plan` (`PriceInCents` 2900); metered component `api-call` is **not** touched by this plan. Where
`Maxio:ApiKey` is stored (user-secrets, Key Vault, environment) is **YOUR CALL — not in the map**; never
hard-code it and never log it.

---

## 3. Trap notes

⚠ **Step 1 (client registration)** — `AddMaxioAdvancedBillingClient` registers the client as a
**singleton** over `IHttpClientFactory.CreateClient()`, and the SDK's retry/timeout options do **not**
bound a whole call and are **not** the timeout on the `HttpClient` ASP.NET Core hands it. Whether a
long-running Maxio call can hang a request, and what `Timeout` actually covers, is decided by options you
have not configured yet. **MUST load `dotnet-client-initialization`** and
**`dotnet-configuration-resilience`** before wiring the client.

⚠ **Step 1 (auth)** — `BasicAuth` is a nullable option with no default; where in the lifetime you set the
credentials, and what happens when the key rotates behind a singleton client, are not visible in the
signature. **MUST load `dotnet-authentication`** before setting credentials.

⚠ **Step 4 (`POST /api/subscriptions` is a non-idempotent write)** — whether a failed or
transport-broken `CreateSubscription` can be re-sent by the SDK's own resilience layer, and whether any
option disables that, determines whether one shopper click can become two subscriptions **inside the
SDK**, underneath your double-click guard. Settle this before you design the application-side lock.
**MUST load `dotnet-configuration-resilience`.**

⚠ **Steps 3–5 (every list/read call)** — the list operations here have 5–14 parameters that are nullable
**with no C# default**, so a positional call can silently mis-bind (a `DateTimeOffset?` landing in the
wrong slot) and still compile. Which arguments must be named, and how `ct` threads through, is a calling
convention rather than a signature fact. **MUST load `dotnet-calling-endpoints`** before the first
`client.…` call.

⚠ **Steps 3–5 (reading `State` / `IntervalUnit`, and mapping to DTOs)** — `SubscriptionState` and
`IntervalUnit` are `StringEnum<T>` **records**, not C# enums, so how you compare one, how you switch on
one, and what an unknown wire value does are all different from ordinary enum handling — as is what
happens to JSON fields the model does not declare. **MUST load `dotnet-models`** before mapping any SDK
model onto an eShopOnWeb DTO.

⚠ **Step 2 (the error boundary)** — this integration mixes Case A and Case B operations on the *same*
request path (step 4 calls two Case B ops and two Case A ops), and one payload record on that path is
suspect (§2.6). Which exception types actually reach your catch blocks, and why `TryGetRawError` is not a
catch-all on the typed errors, is the difference between a boundary that reports a deterministic 422 as a
422 and one that reports it as an outage. **MUST load `dotnet-error-handling`** before writing any
`try/catch`.

⚠ **Step 6 (tests)** — the `HttpClient` constructor argument is the seam, but which behaviours are worth
asserting (versus merely asserting that the SDK executed) is a testing decision no signature shows.
**MUST load `dotnet-testing`** before stubbing the SDK.

---

## 4. REQUIRED READING

**Load every skill below BEFORE implementation starts.** This sheet deliberately does **not** carry their
contents — it names the hazards and points at the skill that resolves each. A trap resolved inline here
would read as settled and stop you opening the skill that carries the defaults, the worked examples, and
the parts you must still wire yourself.

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — constructing / DI-registering `MaxioAdvancedBillingClient`, `HttpClient` ownership and lifetime |
| `dotnet-authentication` | Step 1 — supplying `BasicAuthCredentials`, credential lifetime and rotation |
| `dotnet-configuration-resilience` | Steps 1 and 4 — retries, what `Timeout` bounds, base-URL/server selection, and whether the non-idempotent `CreateSubscription` can be re-sent |
| `dotnet-calling-endpoints` | Steps 3–5 — named vs. positional arguments on the multi-parameter list ops, async and cancellation |
| `dotnet-models` | Steps 3–5 — building `CreateSubscriptionRequest`/`CreateCustomerRequest`, `StringEnum` handling, required members and nullability, wire names vs. C# names |
| `dotnet-error-handling` | Step 2 and every call site — the Case A/Case B catch ladder, reading status safely, the two `JsonException` directions below |
| `dotnet-testing` | Step 6 — the seam to fake, covering the error paths in §2.6 |

**Two hazard rows that must shape the boundary written in step 2 — `System.Text.Json.JsonException`
reaches the boundary from two directions and they need opposite handling:**

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
  maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
  and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

Both directions have a concrete instance in this integration: `ProductResponse.Product` and
`CustomerResponse.Customer` are `required` (§2.3), so a 2xx body missing `product`/`customer` is direction
one; and the suspect `CustomerErrorResponse1`/`Errors` shape on `CreateCustomer`'s 422 (§2.6) is direction
two, where a duplicate-reference rejection could arrive as a `JsonException` with the 422 already lost.

---

## 5. Assumptions & Blockers

**Assumptions**

1. The three endpoints are added to `src/PublicApi` and sit behind that project's existing JWT
   authentication; the caller's identity is read from the token by the same mechanism the existing
   PublicApi endpoints use. I have not read that code and name no claim, header or route of it.
2. `POST /api/subscriptions` accepts a plan **handle** (the value returned by
   `GET /api/subscription-plans`), defaulting to `eshop-pro` when the caller sends none. The request
   contract of your own endpoint — field names, validation, response envelope — is yours to set.
3. eShopOnWeb stores no plan, price or subscription state locally; every read in
   `GET /api/my-subscriptions` goes to Maxio. If caching is wanted, the SDK offers no primitive for it and
   it is an application decision.
4. "Available plans" = every non-archived product in the configured family. If that family ever holds
   products that must not be offered, an extra filter is a product decision, not an SDK fact — the
   `Product` model exposes no visibility/hidden flag to filter on (§2.3).
5. The two seeded plans require no payment method, so the create path sends no payment profile and never
   enters the 3-DS post-authentication flow described in `CreateSubscription`'s Notes. If a plan is later
   configured to require a card, that Notes text applies and this plan does not cover it.

**Blockers**

*(none)* — every operation the hero flow needs exists in the map, and the two facts the map could not
settle (the not-found status of `ReadCustomerByReference`, and whether the percent-encoded `handle:` path
segment is accepted) are carried as `UNVERIFIED` rows in §2.5 / §2.6, each with a concrete defensive
fallback, rather than as open lookups.
