# Maxio Advanced Billing — contract sheet & plan (eShopOnWeb `src/PublicApi`)

Scope: three JWT-authenticated endpoints — `GET /api/subscription-plans`, `POST /api/subscriptions`,
`GET /api/my-subscriptions`. Grounded against the bundled SDK map (`sdk-map.md`,
`map/operations/*.md`, `map/models/*.md`) and, where the map was silent, the generated SDK source at
tag `v1.0.2` (commit `15db14b`). NuGet package `AsadAli.AdvancedBilling.Sdk`; root namespace
`MaxioAdvancedBilling` (they differ — install by package id, `using` the namespace).

---

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 0 | Register the SDK client + config binding (Program.cs / DI) | `ServiceCollectionExtensions.AddMaxioAdvancedBillingClient` or manual singleton |
| 1 | `GET /api/subscription-plans` — list plans of the configured family by **handle** | `client.ProductFamilies.ListProductsForProductFamily` |
| 2a | `POST /api/subscriptions` — find-or-create the Maxio customer by reference | `client.Customers.ReadCustomerByReference` → on 404 `client.Customers.CreateCustomer` |
| 2b | Idempotency check — does this customer already have an **active** subscription to this product? | `client.Customers.ListCustomerSubscriptions` (+ client-side state/handle filter) |
| 2c | Create the subscription (product by handle, customer by id) | `client.Subscriptions.CreateSubscription` |
| 2d | Project the response | `SubscriptionResponse.Subscription` fields |
| 3 | `GET /api/my-subscriptions` — re-derive the customer from identity, list their subscriptions | `client.Customers.ReadCustomerByReference` → `client.Customers.ListCustomerSubscriptions` |

Resolution order for the plan handle in step 2: request-body handle → configured default plan handle
(binding key below). No numeric id is ever configured or hard-coded; every numeric id used
(`customer_id`, and the `product` filter if you take the alternative in §2.7) is obtained at runtime
from a lookup in the same request.

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

### 2.1 Namespaces (one `using` per kind — C# does not import child namespaces transitively)

| Namespace | Types used by this plan | Source |
|---|---|---|
| `MaxioAdvancedBilling` | `MaxioAdvancedBillingClient`, `MaxioAdvancedBillingClientOptions`, `ServerOptions`, `ServiceCollectionExtensions` | `sdk-map.md`; `ServerOptions.cs`, `ServiceCollectionExtensions.cs` |
| `MaxioAdvancedBilling.Api` | controller classes `Products`, `ProductFamilies`, `Customers`, `Subscriptions` | `sdk-map.md` |
| `MaxioAdvancedBilling.Servers` | `ServerEnvironment`, `ProductionOptions` (+ nested `ProductionOptions.UsOptions` / `.EuOptions`), `EbbOptions` | `sdk-map.md`; `Servers/ProductionOptions.cs` |
| `MaxioAdvancedBilling.Core.Authentication.Basic` | `BasicAuthCredentials` | `sdk-map.md` |
| `MaxioAdvancedBilling.Core.Configuration` | `RetryOptions` | `sdk-map.md` |
| `MaxioAdvancedBilling.Core.Exceptions` | `SdkException<TError>` | `sdk-map.md`; `Core/Exceptions/SdkException.cs` |
| `MaxioAdvancedBilling.Core.ErrorResponse` | `ApiError`, `RawError` | `sdk-map.md` |
| `MaxioAdvancedBilling.Errors` | `CreateCustomerError`, `CreateSubscriptionError`, `ListProductsForProductFamilyError`, `FindSubscriptionError` | `sdk-map.md` |
| `MaxioAdvancedBilling.Models` | all records: `Product`, `ProductResponse`, `ProductFamily`, `ProductFamilyResponse`, `Customer`, `CustomerResponse`, `CreateCustomer`, `CreateCustomerRequest`, `CreateSubscription`, `CreateSubscriptionRequest`, `Subscription`, `SubscriptionResponse`, `ErrorListResponse1`, `CustomerErrorResponse1`, `Errors`, `ListProductsFilter`, `CustomerAttributes` | `sdk-map.md`; `map/models/records-*.md` |
| `MaxioAdvancedBilling.Models.Enums` | `IntervalUnit`, `SubscriptionState`, `SubscriptionStateFilter`, `BasicDateField`, `SortingDirection`, `ListProductsInclude`, `SubscriptionListInclude`, `SubscriptionInclude`, `SubscriptionSort`, `SubscriptionDateField` | `models/enums.md` |

### 2.2 Client construction, auth, base URL, DI

| Fact | Value | Source |
|---|---|---|
| Client type / only ctor | `MaxioAdvancedBilling.MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)` — **no** parameterless ctor and **no** builder class; the `HttpClient` is a constructor argument (and is the test seam) | `sdk-map.md` → *Getting a client*; `MaxioAdvancedBillingClient.cs` |
| Controller access | plain get-only properties on the client: `client.Products`, `client.ProductFamilies`, `client.Customers`, `client.Subscriptions` (types `MaxioAdvancedBilling.Api.Products` etc.) | `MaxioAdvancedBillingClient.cs` |
| Options — all four properties | `Environment: MaxioAdvancedBilling.Servers.ServerEnvironment` · `Retry: MaxioAdvancedBilling.Core.Configuration.RetryOptions` · `Server: MaxioAdvancedBilling.ServerOptions` · `BasicAuth: MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md` |
| Auth — the only scheme | HTTP **Basic**, set as `options.BasicAuth = new BasicAuthCredentials { Username = <API key>, Password = "x" }`. `BasicAuthCredentials` is a sealed class with `required string Username` and `required string Password` (both must be set in the object initializer) plus a public `Encode()`. **Username = the API key; Password = the literal string `"x"`.** There is no separate "auth manager" type to construct | `sdk-map.md` → *Servers & auth*; `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Environment selection | `options.Environment = ServerEnvironment.Us` (default) or `ServerEnvironment.Eu`. Us ⇒ template `https://{site}.chargify.com`; Eu ⇒ `https://{site}.ebilling.maxio.com` | `sdk-map.md`; `Servers/ProductionOptions.cs` |
| **Site subdomain** | `options.Server.Production.Us.Site = "cp-exp-2"` — type `string`, **SDK default `"subdomain"`**. With only a subdomain given, the derived URL is `https://cp-exp-2.chargify.com` | `Servers/ProductionOptions.cs` (`UsOptions { BaseUrl = "https://{site}.chargify.com"; Site = "subdomain"; }`) |
| **Explicit BaseUrl override (verbatim)** | `options.Server.Production.Us.BaseUrl = <configured url>` — type `string`, default `"https://{site}.chargify.com"`. The SDK substitutes only the literal `{site}` placeholder inside `BaseUrl`, then trims **one trailing `/`** and appends the operation path. An override containing no `{site}` is therefore used byte-for-byte as the origin/prefix and `Site` becomes irrelevant; if the override *does* contain `{site}`, set `Site` too. Assign the configured value directly — never compose the URL yourself | `Servers/ProductionOptions.cs`; `Core/TemplateParamsFactory.cs` (`ExpandTemplate` + `TrimEnd('/')`) |
| EU-hosted variant | the same two member names under `options.Server.Production.Eu.*`; `options.Environment` selects which of `Us`/`Eu` is read | `Servers/ProductionOptions.cs` (`Resolve` matches on the environment) |
| Ebb (event-ingest) group | `options.Server.Ebb.*` — **not used**: no operation in this plan is on the Ebb group | `sdk-map.md` |
| DI registration (built in) | `services.AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>? configure = null) : IServiceCollection` — an extension member on `IServiceCollection` declared in the **root** namespace (`MaxioAdvancedBilling`). It calls `services.AddHttpClient()`, then registers `MaxioAdvancedBillingClient` as a **singleton** built from `IHttpClientFactory.CreateClient()` (the default, unnamed client). `configure` is invoked **once, eagerly, at registration time** — the options instance is captured; there is no `IOptions<>`/reload path, so configuration must be readable at registration | `ServiceCollectionExtensions.cs` |
| Manual equivalent (use if the extension does not resolve — trap T1) | `services.AddHttpClient();` then `services.AddSingleton(sp => new MaxioAdvancedBillingClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient(), options));` — this is byte-for-byte what the extension does | `ServiceCollectionExtensions.cs` |
| HttpClient ownership / lifetime | one factory-created `HttpClient` is held by a singleton for the process lifetime; the SDK never disposes or re-creates it and never registers a named/typed client. Whether that lifetime is right for your handler pipeline is trap T1 | `ServiceCollectionExtensions.cs` |

Configuration the app must supply — named by binding key (the SDK does not name these; you do):

| Purpose | Binding key (proposed) | Source |
|---|---|---|
| API key → `BasicAuth.Username` | `Maxio:ApiKey` — from user-secrets; never a literal in the repo | YOUR CALL — not in the map |
| Site subdomain → `Server.Production.Us.Site` (SDK default `"subdomain"`) | `Maxio:Subdomain` (sandbox value `cp-exp-2`) | YOUR CALL — not in the map |
| Optional verbatim base-URL override → `Server.Production.Us.BaseUrl` (SDK default `"https://{site}.chargify.com"`) | `Maxio:BaseUrl` — when present and non-empty, assign it as-is | YOUR CALL — not in the map |
| Product family handle | `Maxio:ProductFamilyHandle` (sandbox value `eshop-subscribe`) | YOUR CALL — not in the map |
| Default plan handle for the step-2 fallback | `Maxio:DefaultProductHandle` (e.g. `basic-plan`) | YOUR CALL — not in the map |
| Per-user Maxio `reference` string | derive from the app's own identity path; must be stable across restarts and unique per user | YOUR CALL — not in the map |

### 2.3 Operations

| # | Controller property | Method signature (verbatim, params in order) | Request model + fields | Response envelope → payload | Error case + accessors | Pagination | Source |
|---|---|---|---|---|---|---|---|
| O1 | `client.ProductFamilies` (`MaxioAdvancedBilling.Api.ProductFamilies`) | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 8 params `dateField`…`include` are nullable with **no default**: pass `null` explicitly for each you skip | none (GET). `productFamilyId` is a **string** path param and the generated XML doc says it accepts *"Either the product family's id or its handle prefixed with `handle:`"* → pass `$"handle:{familyHandle}"`. `filter` (optional) is `ListProductsFilter { Ids (ids): IReadOnlyList<int>?, PrepaidProductPricePoint (prepaid_product_price_point): PrepaidProductPricePointFilter?, UseSiteExchangeRate (use_site_exchange_rate): bool? }` — not needed here | `IReadOnlyList<ProductResponse>`; `ProductResponse.Product (product): Product` is **`required`/non-null** → `resp.Select(r => r.Product)` | **Case A** `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>` · `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page` + `perPage` (wire `page`, `per_page`); defaults 1 / 20 | `operations/ProductFamilies.md`; `Api/ProductFamilies.cs` (param doc) |
| O2 | `client.Products` (`MaxioAdvancedBilling.Api.Products`) | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` → `GET /products/handle/{api_handle}.json` | none | `ProductResponse` → `.Product` (`required`) | **Case B** `SdkException<RawError>` · `StatusCode` · `ReadAsString()` · `ReadAsJson<T>()` · `ReadAsBytes()` | none | `operations/Products.md` |
| O3 | `client.Products` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — **site-wide**, not family-scoped; fallback only. ⚠ its shared params sit in a **different order** than O1's | none | `IReadOnlyList<ProductResponse>` | **Case B** `SdkException<RawError>` | manual `page`+`perPage`; defaults 1 / 20 | `operations/Products.md` |
| O4 | `client.ProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — **there is no read-family-by-handle**: `ReadProductFamily(int id, CancellationToken ct = default)` takes an `int`, so the `handle:my-family` form its Notes mention is not expressible in C#. Use this list and match on `ProductFamily.Handle` when you need the family object/id | none | `IReadOnlyList<ProductFamilyResponse>`; `ProductFamilyResponse.ProductFamily (product_family): ProductFamily?` — **nullable**, guard | **Case B** `SdkException<RawError>` | **none** (no `page`/`perPage` params exist) | `operations/ProductFamilies.md` |
| O5 | `client.Customers` (`MaxioAdvancedBilling.Api.Customers`) | `ReadCustomerByReference(string reference, CancellationToken ct = default)` → `GET /customers/lookup.json?reference=…`; Notes: *"Returns a customer by their unique reference ID. It will return a single match."* This is the **read-by-reference** operation | none | `CustomerResponse` → `.Customer (customer): Customer` — **`required`/non-null** | **Case B** `SdkException<RawError>` — read `ex.Error.StatusCode`; treat `HttpStatusCode.NotFound` as "no such customer" | none | `operations/Customers.md` |
| O6 | `client.Customers` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` is nullable with no default → **must pass explicitly** | `CreateCustomerRequest { Customer (customer): CreateCustomer !req }`; `CreateCustomer` = **`FirstName (first_name): string !req`**, **`LastName (last_name): string !req`**, **`Email (email): string !req`**, `Reference (reference): string?`, plus optional `CcEmails (cc_emails)`, `Organization`, `Address (address)`, `Address2 (address_2)`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `VatNumber (vat_number)`, `TaxExempt (tax_exempt): bool?`, `TaxExemptReason (tax_exempt_reason)`, `ParentId (parent_id): int?`, `SalesforceId (salesforce_id)`. ⚠ **`Reference` is *optional* in C# but is the idempotency key** — the Notes state *"you may only create one customer for a given reference value … If provided, the `reference` value must be unique"*, so it must be set | `CustomerResponse` → `.Customer` (`required`) | **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>` · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Customers.md`; `records-1-Ac-Cr.md` |
| O7 | `client.Customers` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` → `GET /customers/{customer_id}/subscriptions.json` | none | `IReadOnlyList<SubscriptionResponse>`; `SubscriptionResponse.Subscription (subscription): Subscription?` — **nullable**, guard every element | **Case B** `SdkException<RawError>` | **none** — no `page`/`perPage`/`state` params exist on this operation | `operations/Customers.md`; `records-4-Su-We.md` |
| O8 | `client.Customers` | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — the 7 params `direction`…`q` are nullable, no default. ⚠ **There is no `reference` filter and no `email` filter parameter**: search is the free-text `q` (wire `q`) only — the Notes list search by email / Advanced Billing id / organization / your reference value / first or last name, and add *"To retrieve a single, exact match by reference, use the lookup endpoint"* (= O5). ⚠ `startDate`…`endDatetime` are **`string?`** here, not `DateTimeOffset?` | none | `IReadOnlyList<CustomerResponse>` | **Case B** `SdkException<RawError>` | manual `page`+`perPage`; defaults 1 / **50** | `operations/Customers.md` |
| O9 | `client.Subscriptions` (`MaxioAdvancedBilling.Api.Subscriptions`) | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, no default → pass explicitly | `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription !req }` — field list and legal combinations in §2.5 | `SubscriptionResponse` → `.Subscription` — **nullable**, guard | **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>` · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md`; `records-2-Cr-Ne.md` |
| O10 | `client.Subscriptions` | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 14 nullable no-default params. **State filtering is the `state` parameter typed `SubscriptionStateFilter` → wire `state=<value>`** (`SubscriptionStateFilter.Active` ⇒ `state=active`). **`product` is `int?` — numeric product id only, no handle variant. There is NO customer filter on this operation** | none | `IReadOnlyList<SubscriptionResponse>` | **Case B** `SdkException<RawError>` | manual `page`+`perPage`; defaults 1 / 20 | `operations/Subscriptions.md` |
| O11 | `client.Subscriptions` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` nullable, no default → pass `null` explicitly | none | `SubscriptionResponse` → `.Subscription` (nullable) | **Case B** `SdkException<RawError>` | none | `operations/Subscriptions.md` |
| O12 | `client.Subscriptions` | `FindSubscription(string? reference, CancellationToken ct = default)` → `GET /subscriptions/lookup.json?reference=…` — looks up by the **subscription's own** `reference` (`CreateSubscription.Reference`), not the customer's. Only relevant if you choose to stamp a per-user-per-plan subscription reference | none | `SubscriptionResponse` | **Case A** `SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>` · `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |

### 2.4 Step-by-step call recipes

**Step 1 — `GET /api/subscription-plans`.** One call, O1, all optional params named:

`client.ProductFamilies.ListProductsForProductFamily(productFamilyId: $"handle:{familyHandle}", dateField: null, filter: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, includeArchived: false, include: null, page: 1, perPage: 200, ct: ct)`

Then per `ProductResponse r`: `r.Product` (non-null) → project `Handle`, `Name`, `Description`,
`PriceInCents / 100m`, `Interval`, `IntervalUnit?.Value`, card flag (trap T4). Drop any product with
`ArchivedAt is not null` client-side in addition to `includeArchived: false` (assumption 4). Works for
2+ products by construction — the return is a list; paginate per §2.8 if a family ever exceeds `perPage`.

**Step 2a — find-or-create customer.** `O5 ReadCustomerByReference(reference: userReference, ct: ct)`;
catch `SdkException<RawError>` and, when `ex.Error.StatusCode == HttpStatusCode.NotFound`, call
`O6 CreateCustomer(body: new CreateCustomerRequest { Customer = new CreateCustomer { FirstName = …, LastName = …, Email = …, Reference = userReference } }, ct: ct)`.
On a `CreateCustomer` failure, re-run O5 once and use the result if it now exists (the reference-uniqueness
rule in O6's Notes makes the loser of a race a deterministic failure, not a duplicate) — trap T2.

**Step 2b — existing-subscription check.** `O7 ListCustomerSubscriptions(customerId: customer.Id!.Value, ct: ct)`,
then in C#: `resp.Select(r => r.Subscription).OfType<Subscription>().FirstOrDefault(s => s.State == SubscriptionState.Active && s.Product?.Handle == planHandle)`.
If found, return it instead of creating (trap T8 for the caveat).

**Step 2c — create.** `O9 CreateSubscription(body: new CreateSubscriptionRequest { Subscription = new CreateSubscription { ProductHandle = planHandle, CustomerId = customer.Id!.Value } }, ct: ct)`.
No payment/card fields (§2.5).

**Step 3 — `GET /api/my-subscriptions`.** `O5` (404 ⇒ empty list) → `O7` → same projection as 2d. No local
persistence is involved: the only key is the reference string derived from the caller's identity.

### 2.5 `CreateSubscription` — legal field combinations (⚠ nothing on this model is `required`)

**The C# model marks *no* field `required`, so `required?` selects nothing for you: the compiler will
happily accept an empty `CreateSubscription` and the rejection arrives only as a 422 from the provider.**
The operation's Notes are the contract: *"Specify the product with `product_id` or `product_handle`. To set
a specific product price point, use `product_price_point_handle` or `product_price_point_id`. Identify an
existing customer with `customer_id` or `customer_reference`. Optionally, include an existing payment
profile using `payment_profile_id`. To create a new customer, pass customer_attributes."*

| Purpose | C# property (wire name) : type | Decision |
|---|---|---|
| Product by handle | `ProductHandle (product_handle): string?` | **use** — `eshop-pro` / `basic-plan` |
| Product by numeric id | `ProductId (product_id): int?` | legal alternative; not used (ids unstable) |
| Price point | `ProductPricePointHandle (product_price_point_handle): string?` / `ProductPricePointId (product_price_point_id): int?` | omit ⇒ the product's default price point |
| Existing customer by id | `CustomerId (customer_id): int?` | **use** — from step 2a (`Customer.Id` is `int?`; null-guard before assigning) |
| Existing customer by reference | `CustomerReference (customer_reference): string?` | legal alternative to `CustomerId` (either identifies an existing customer) |
| New customer inline | `CustomerAttributes (customer_attributes): CustomerAttributes?` — `FirstName (first_name)`, `LastName (last_name)`, `Email (email)`, `Reference (reference)`, address fields, `Metafields`, … | **not used**: step 2a already guarantees the customer, and combining it with `CustomerId` is not one of the documented combinations |
| Payment | `PaymentProfileId (payment_profile_id): int?`, `PaymentProfileAttributes (payment_profile_attributes)`, `CreditCardAttributes (credit_card_attributes)`, `BankAccountAttributes (bank_account_attributes)` | **omitted deliberately** — sandbox plans are "payment method not required". The Notes warn *"Payment information may be required to create a subscription, depending on the options for the Product being subscribed"*. ⚠ **CORRECTED after live testing: omitting all payment fields is NOT sufficient** — a card-free create with only `ProductHandle` + `CustomerId` is rejected 422 (`errors: ["No payment method was on file for the $299.00 balance"]`) even when `require_credit_card` is `false`. The collection method must be set — see **§2.11** |
| Collection method | `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` | **required in practice for this integration** — see §2.11 |
| Subscription's own reference | `Ref (ref): string?` **and** `Reference (reference): string?` — two distinct properties on this one model | optional; only needed if you adopt the O12 lookup |
| Other Notes-relevant fields left out | `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `CouponCode`/`CouponCodes`, `NextBillingAt (next_billing_at)`, `InitialBillingAt`, `PreviousBillingAt`, `CalendarBilling`, `CalendarBillingFirstCharge`, `Components`, `Metafields`, `Currency`, `NetTerms`, `OfferId (offer_id)` (union), `DeferSignup (defer_signup): bool? = false`, `SkipBillingManifestTaxes`, `AgreementAcceptance`, `Group` | intentionally omitted — none is named by the Notes as a precondition of acceptance for a card-free signup on a default price point |

Minimal accepted body for this integration: `product_handle` + `customer_id`.
Source: `operations/Subscriptions.md` (Notes) · `records-2-Cr-Ne.md` (`CreateSubscription`, `CreateSubscriptionRequest`).

### 2.6 Response models — the exact properties these endpoints read

`Product` (`MaxioAdvancedBilling.Models.Product`) — source `records-3-Of-Su.md`:

| C# property (wire name) | Type | Use |
|---|---|---|
| `Id (id)` | `int?` | only for the O10 `product` filter; never configured or persisted |
| `Handle (handle)` | `string?` | plan handle |
| `Name (name)` | `string?` | plan name |
| `Description (description)` | `string?` | plan description |
| `PriceInCents (price_in_cents)` | `long?` | dollars = `value / 100m` (decimal division; guard null) |
| `Interval (interval)` | `int?` | billing interval count |
| `IntervalUnit (interval_unit)` | `MaxioAdvancedBilling.Models.Enums.IntervalUnit?` | interval unit — members `Day (day)`, `Month (month)` **only** |
| `ArchivedAt (archived_at)` | `DateTimeOffset?` | non-null ⇒ archived |
| `RequireCreditCard (require_credit_card)` | `bool?` | "payment method required" |
| `RequestCreditCard (request_credit_card)` | `bool?` | second, separately generated card flag — trap T4 |
| `ProductFamily (product_family)` | `ProductFamily?` = `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `AccountingCode (accounting_code): string?`, `Description (description): string?`, `CreatedAt`, `UpdatedAt`, `ArchivedAt` (all nullable) | echo/verify the family handle |
| also present, unused | `TrialPriceInCents`, `TrialInterval`, `TrialIntervalUnit (trial_interval_unit): IntervalUnit?`, `ExpirationInterval`, `ExpirationIntervalUnit: ExpirationIntervalUnit?`, `InitialChargeInCents`, `Taxable`, `VersionNumber`, `DefaultProductPricePointId`, `ProductPricePointId`, `ProductPricePointHandle`, `ProductPricePointName`, `PublicSignupPages`, `RequestBillingAddress`, `RequireBillingAddress`, `RequireShippingAddress`, `AccountingCode`, `TaxCode`, `ItemCategory`, `UseSiteExchangeRate`, `CreatedAt`, `UpdatedAt` | — |

`Subscription` (`MaxioAdvancedBilling.Models.Subscription`) — source `records-3-Of-Su.md`:

| C# property (wire name) | Type | Note |
|---|---|---|
| `Id (id)` | `int?` | the Maxio subscription id |
| `State (state)` | `MaxioAdvancedBilling.Models.Enums.SubscriptionState?` | §2.7 |
| `Product (product)` | `Product?` | nested; same record above → `Handle`, `Name`, `PriceInCents`, `Interval`, `IntervalUnit` |
| `Customer (customer)` | `Customer?` | nested customer object |
| `CurrentPeriodStartedAt (current_period_started_at)` | `DateTimeOffset?` | ⚠ wire name is `…started_at` (past tense) while the end field is `…ends_at` |
| `CurrentPeriodEndsAt (current_period_ends_at)` | `DateTimeOffset?` | |
| `NextAssessmentAt (next_assessment_at)` | `DateTimeOffset?` | **this is the next billing / next assessment date.** `Subscription` has **no** `next_billing_at` property — `next_billing_at` exists only as an *input* on `CreateSubscription`/`UpdateSubscription`, and `UpdateSubscription`'s Notes confirm the server does not return it |
| `CreatedAt (created_at)` | `DateTimeOffset?` | |
| `ProductPriceInCents (product_price_in_cents)` | `long?` | the subscription's own price snapshot — prefer over `Product.PriceInCents` when reporting what the subscriber pays |
| also present, unused | `ActivatedAt`, `CanceledAt`, `CancelAtEndOfPeriod`, `CancellationMessage`, `CancellationMethod`, `PreviousState`, `Reference (reference): string?`, `CurrentBillingAmountInCents`, `BalanceInCents`, `TotalRevenueInCents`, `ProductPricePointId`, `ProductPricePointType`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `Currency`, `Locale`, `CouponCode(s)`, `CreditCard`, `BankAccount`, `Group`, `PrepaidConfiguration`, `SelfServicePageToken`, `ExpiresAt`, `TrialStartedAt`, `TrialEndedAt`, `UpdatedAt` | — |

`Customer` (`MaxioAdvancedBilling.Models.Customer`) — source `records-1-Ac-Cr.md`:
`Id (id): int?`, `Reference (reference): string?`, `Email (email): string?`, `FirstName (first_name): string?`,
`LastName (last_name): string?`, `Organization (organization): string?`, `CcEmails (cc_emails): string?`,
address fields (`Address (address)`, `Address2 (address_2)`, `City`, `State`, `StateName (state_name)`, `Zip`,
`Country`, `CountryName (country_name)`), `Phone`, `Verified`, `TaxExempt`, `VatNumber`, `ParentId`, `Locale`,
`Maxioid (maxioid): string?`, `CreatedAt`, `UpdatedAt` — **every field is nullable, including `Id`.**

Envelopes (reads always go one level down):

| Envelope | Inner field | Nullability | Source |
|---|---|---|---|
| `ProductResponse` | `Product (product): Product` | `required` — non-null | `records-3-Of-Su.md` |
| `ProductFamilyResponse` | `ProductFamily (product_family): ProductFamily?` | nullable | `records-3-Of-Su.md` |
| `CustomerResponse` | `Customer (customer): Customer` | `required` — non-null | `records-1-Ac-Cr.md` |
| `SubscriptionResponse` | `Subscription (subscription): Subscription?` | **nullable — guard on every read, including the create response** | `records-4-Su-We.md` |

### 2.7 Enums used (they are `StringEnum<T>` records, **not** C# enums)

| Enum (`MaxioAdvancedBilling.Models.Enums`) | Members `CSharpName (wire_value)` |
|---|---|
| `IntervalUnit` | `Day (day)`, `Month (month)` |
| `CollectionMethod` (type of `CreateSubscription.PaymentCollectionMethod` **and** of `Subscription.PaymentCollectionMethod`) | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` — enum summary: *"The type of payment collection to be used in the subscription. For legacy Statements Architecture valid options are - `invoice`, `automatic`. For current Relationship Invoicing Architecture valid options are - `remittance`, `automatic`, `prepaid`."* |
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, **`Active (active)`**, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `SubscriptionStateFilter` (the O10 `state` query param — a **different, smaller** set) | `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)` — ⚠ it has **no** `pending`/`assessing`/`failed_to_create`/`paused`/`awaiting_signup`, so it is not interchangeable with `SubscriptionState` |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` |
| `SortingDirection` | `Asc (asc)`, `Desc (desc)` |
| `ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)` |
| `SubscriptionListInclude` | `SelfServicePageToken (self_service_page_token)` |
| `SubscriptionInclude` | `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)` |
| `SubscriptionSort` | `SignupDate (signup_date)`, `PeriodStart (period_start)`, `PeriodEnd (period_end)`, `NextAssessment (next_assessment)`, `UpdatedAt (updated_at)`, `CreatedAt (created_at)`, `TotalPayments (total_payments)`, `Id (id)`, `OpenBalance (open_balance)`, `ExpiresAt (expires_at)` |
| `SubscriptionDateField` | `CurrentPeriodEndsAt`, `CurrentPeriodStartsAt`, `CreatedAt`, `ActivatedAt`, `CanceledAt`, `ExpiresAt`, `TrialStartedAt`, `TrialEndedAt`, `UpdatedAt` (wire = snake_case of each) |

Source: `models/enums.md`. Mechanics from `Core/Enum/StringEnum.cs` + `Core/Enum/TypedEnum.cs`: each is a
`sealed record` over `public TValue Value { get; init; }`, so `s.State == SubscriptionState.Active` is
correct record equality (and null-safe when `State` is null), `.Value` is the wire string, there is an
implicit conversion to `string`, and an **unrecognised wire value does not throw** — it deserializes into a
new instance carrying that value (`IsKnownValue()` false, every `==` against a known member false).

**Filtering subscriptions by customer and by state — the exact way:**

- **By customer:** O7 `ListCustomerSubscriptions(customerId: id, ct: ct)` — path-scoped to `GET /customers/{customer_id}/subscriptions.json`. It has **no state filter and no pagination parameters**.
- **State on that result:** client-side — `r.Subscription is { } s && s.State == SubscriptionState.Active` (plus `s.Product?.Handle == planHandle` for the per-plan check).
- **State server-side:** only on O10 `ListSubscriptions`, via `state:` typed `SubscriptionStateFilter` (wire `state=active`) — but O10 **cannot filter by customer at all**, so it is not a substitute; its `product:` filter would additionally force an O2 call to resolve the numeric product id.
- Conclusion for step 2b: O7 + client-side filtering.

### 2.8 Pagination

| Operation | Params (wire) | Defaults | How to iterate |
|---|---|---|---|
| O1 `ListProductsForProductFamily` | `int? page` (`page`), `int? perPage` (`per_page`) | 1 / 20 | manual loop |
| O3 `ListProducts` | same | 1 / 20 | manual loop |
| O8 `ListCustomers` | same | 1 / **50** | manual loop |
| O10 `ListSubscriptions` | same | 1 / 20 | manual loop |
| O4 `ListProductFamilies`, O7 `ListCustomerSubscriptions` | **none at all** | — | single call; page 2 cannot be requested |

There is **no** auto-paginating/`IAsyncEnumerable` wrapper and **no** total-count or `Link` metadata on
these return types (they are bare `IReadOnlyList<T>`, not a paged envelope). To iterate: call with
`page: n`, increment while the returned count `== perPage`, stop on a short or empty page.
Source: `operations/ProductFamilies.md`, `operations/Products.md`, `operations/Customers.md`, `operations/Subscriptions.md`.

### 2.9 Error handling

| Fact | Value | Source |
|---|---|---|
| Exception type | `MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>` — `sealed class : Exception` whose **only** member is `public required TError Error { get; init; }` | `Core/Exceptions/SdkException.cs` |
| ⚠ No status, no message on the exception | `SdkException<T>` has **no** `StatusCode` property and sets no custom message: `ex.Message` is the framework's default text and `ex.ToString()` carries no HTTP status. The status is reachable **only** through `RawError.StatusCode` | `Core/Exceptions/SdkException.cs` |
| Case B payload | `MaxioAdvancedBilling.Core.ErrorResponse.RawError` — `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`, `ReadAsBytes(): ReadOnlyMemory<byte>` | `sdk-map.md` |
| Case A payload | a generated `…Error : MaxioAdvancedBilling.Core.ErrorResponse.ApiError` with status-specific `TryGet…(out …)` accessors plus the inherited `TryGetRawError(out RawError)` | `sdk-map.md` |
| ⚠ `TryGetRawError` is **not** a catch-all | on a typed error the raw fallback is populated **only** when the typed shape was not used — `CreateCustomerError.Create` maps 422 → `CustomerErrorResponse1` with `fallback = default`, every other status → raw. So when `TryGetCustomerErrorResponse1` returns `true`, `TryGetRawError` returns `false`, and **the only signal that the status was 422 is which accessor succeeded** | `Errors/CreateCustomerError.cs` |
| Success boundary | 2xx = success; every other status goes down the error path (no operation in scope configures a spec-specific allowlist) | `Core/HttpStatusPolicy.cs` |
| No-throw variants | **absent across the whole SDK** — every operation throws; there is no `…Result`/`ApiResult` overload | `sdk-map.md` |
| 401 / wrong host / timeout | 401 surfaces as Case B `SdkException<RawError>` with `StatusCode == HttpStatusCode.Unauthorized`; host/DNS/timeout failures surface as `HttpClient` transport exceptions. Check `Maxio:ApiKey`, `Server.Production.Us.Site`/`BaseUrl` and retry settings before touching call sites | `sdk-map.md`; `Core/HttpStatusPolicy.cs` |

Per-operation case for everything in scope:

| Operation | Case | Exception + accessors |
|---|---|---|
| O1 `ListProductsForProductFamily` | **A** | `SdkException<ListProductsForProductFamilyError>` · `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` |
| O6 `CreateCustomer` | **A** | `SdkException<CreateCustomerError>` · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` |
| O9 `CreateSubscription` | **A** | `SdkException<CreateSubscriptionError>` · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` |
| O12 `FindSubscription` | **A** | `SdkException<FindSubscriptionError>` · `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` |
| O2, O3, O4, O5, O7, O8, O10, O11 | **B** | `SdkException<RawError>` · `StatusCode` / `ReadAsString()` / `ReadAsJson<T>()` / `ReadAsBytes()` |

Error-body payload shapes (ordinary records):

| Payload | Fields | Source |
|---|---|---|
| `ErrorListResponse1` — subscription-create 422 | `Errors (errors): IReadOnlyList<string>` — **`required`** | `records-2-Cr-Ne.md`; `Models/ErrorListResponse1.cs` |
| `CustomerErrorResponse1` — customer-create/update 422 | `Errors (errors): Errors?`, where `Errors` = `PerPage (per_page): IReadOnlyList<string>?`, `PricePoint (price_point): IReadOnlyList<string>?` | `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md`; `Models/CustomerErrorResponse1.cs`, `Models/Errors.cs` |

**How far the customer-422 contract can be trusted — evidence from the generated code only.** The two 422
shapes in scope contradict each other: `CreateSubscription`'s 422 models `errors` as a **JSON array of
strings** (`IReadOnlyList<string>`, and `required`), while `CreateCustomer`'s 422 models `errors` as a
**JSON object** whose only modelled keys are `per_page` and `price_point` — keys belonging to
list/price-point validation, i.e. `Errors` is a shared model reused here rather than a customer-specific
one. Both cannot describe the same provider convention. Consequence: if a real customer 422 sends `errors`
as an array, deserialization into `Errors` fails inside `CreateCustomerError.Create`, the `JsonException`
**replaces** the `SdkException`, and the 422 status is destroyed with it (see the REQUIRED READING hazard
rows). Directive: never make a customer-create failure path depend on reading `CustomerErrorResponse1` —
catch `SdkException<CreateCustomerError>` **and** `System.Text.Json.JsonException` around O6, extract
messages best-effort (`e.Errors?.PerPage` / `e.Errors?.PricePoint`, else `TryGetRawError` →
`ReadAsString()`), and fall back to a generic message plus the recovery step in trap T2. Which shape the
live sandbox actually returns: `UNVERIFIED`.

### 2.10 Idempotency — what the SDK does and does not give you

| Fact | Value | Source |
|---|---|---|
| Idempotency-key support | **none.** `MaxioAdvancedBillingClientOptions` has exactly four properties (`Environment`, `Retry`, `Server`, `BasicAuth`) — no header hook, no request-interceptor, no idempotency option; and no operation in scope takes an idempotency parameter | `sdk-map.md` (options table); `operations/Customers.md`, `operations/Subscriptions.md` |
| The only idempotency the provider offers here | uniqueness on the customer `reference` (`CreateCustomer` Notes: *"you may only create one customer for a given reference value"*), plus your own read-before-write via O5/O7 | `operations/Customers.md` |
| Consequence | double-click safety = O5-then-O6 with the "recreate lost the race → re-read" recovery (step 2a), plus the 2b active-subscription check; there is no server-side de-duplication of `CreateSubscription` | — |
| Retry interaction | whether a failed write can be re-sent by the SDK's own pipeline is trap T2 — resolve it in `dotnet-configuration-resilience` before enabling retries around these POSTs | — |

### 2.11 Creating a subscription with no payment profile on file (added after a live 422)

Live result being corrected: `CreateSubscription` with only `ProductHandle` + `CustomerId` → HTTP 422,
`errors: ["No payment method was on file for the $299.00 balance"]`, with the product reporting
`require_credit_card: false` and `request_credit_card: null`. The product-level card flags therefore do
**not** govern whether the first period's balance is settled against an automatic payment method; the
subscription's **collection method** does.

| Field / type | Exact C# member | Type | What the generated docs say | Source |
|---|---|---|---|---|
| Collection method (request) | `CreateSubscription.PaymentCollectionMethod` (wire `payment_collection_method`) | `MaxioAdvancedBilling.Models.Enums.CollectionMethod?` | *"The type of payment collection to be used in the subscription. For legacy Statements Architecture valid options are - `invoice`, `automatic`. For current Relationship Invoicing Architecture valid options are - `remittance`, `automatic`, `prepaid`."* | `models/enums.md`; `Models/CreateSubscription.cs` (property doc) |
| Collection method (response) | `Subscription.PaymentCollectionMethod` (wire `payment_collection_method`) | `MaxioAdvancedBilling.Models.Enums.CollectionMethod?` — **nullable**; surface `.Value` (the wire string) | same enum, echoed back | `records-3-Of-Su.md` |
| Members | `Automatic (automatic)` · `Remittance (remittance)` · `Prepaid (prepaid)` · `Invoice (invoice)` | `StringEnum<CollectionMethod>` record — write `CollectionMethod.Remittance`, not `"remittance"` | — | `models/enums.md` |

**What the Notes do and do not say.** The `CreateSubscription` **operation** Notes say nothing about
`payment_collection_method` — they only warn *"Payment information may be required to create a subscription,
depending on the options for the Product being subscribed"*. The documented distinction lives in the
**enum's** summary (above) and, corroboratively, in the `Invoices.IssueInvoice` Notes: *"For Remittance
subscriptions, the invoice will go into 'open' status and payment won't be attempted. … For Automatic
subscriptions, prepayments and service credits will apply to the invoice before payment is attempted."*
That is the only place in the map that states, in the provider's own prose, that a non-automatic collection
method means **payment is not attempted** — which is exactly the failure being corrected.
`Remittance`/`Invoice` is therefore the documented route to a subscription with no payment profile; the
enum summary decides which name is legal on your site (Relationship Invoicing ⇒ `Remittance`; legacy
Statements ⇒ `Invoice`). Which architecture the `cp-exp-2` sandbox runs on is `UNVERIFIED` — directive:
send `CollectionMethod.Remittance` first, and if the 422 names the collection method as invalid, retry once
with `CollectionMethod.Invoice`; do not send both and do not guess a third value.

**Adjacent fields that defer rather than avoid the charge** (all from `Models/CreateSubscription.cs`
property docs — the map's records page carries the names/types, not these summaries):

| C# property (wire) | Type | Documented effect | Side effects on `State` / `NextAssessmentAt` |
|---|---|---|---|
| `NextBillingAt (next_billing_at)` | `DateTimeOffset?` | *"(Optional) Set this attribute to a future date/time to sync imported subscriptions to your existing renewal schedule. … If you provide a next_billing_at timestamp that is in the future, no trial or initial charges will be applied when you create the subscription. In fact, no payment will be captured at all. The first payment will be captured, according to the prices defined by the product, near the time specified by next_billing_at. If you do not provide a value for next_billing_at, any trial and/or initial charges will be assessed and charged at the time of subscription creation. If the card cannot be successfully charged, the subscription will not be created."* | Suppresses the create-time charge and sets when the first payment is captured. The docs do not name the resulting `state`. Note it is an **import-oriented** field, and `CalendarBilling` cannot be combined with it |
| `InitialBillingAt (initial_billing_at)` | `DateTimeOffset?` | *"(Optional) Set this attribute to a future date/time to create a subscription in the Awaiting Signup state, rather than Active or Trialing. You can omit the initial_billing_at date to activate the subscription immediately. … When the initial_billing_at date hits, the subscription will transition to the expected state. If the product has a trial, the subscription will enter a trial, otherwise it will go active. … If the payment is due at the initial_billing_at and it fails the subscription will be immediately canceled."* | Lands the subscription in **`SubscriptionState.AwaitingSignup`**, not `Active` — and the charge is only deferred, so the same "no payment method" failure returns at the billing date (then the subscription is *canceled*) |
| `DeferSignup (defer_signup)` | `bool?` with generated default `= false` (note: emitted even when null — it is the one field on this model without `WhenWritingNull`) | *"(Optional) Set this attribute to true to create the subscription in the Awaiting Signup Date state. Use this when you want to create a subscription that has an unknown first billing date. When the first billing date is known, update a subscription and set the `initial_billing_at` date. The subscription moves to the Awaiting Signup state with a scheduled initial billing date. You can omit the initial_billing_at date to activate the subscription immediately."* | Lands in the **Awaiting Signup Date** state and requires a later `UpdateSubscription` to set `initial_billing_at`. Wrong shape for a self-service "Subscribe" button |
| `CalendarBilling (calendar_billing)` | `MaxioAdvancedBilling.Models.CalendarBilling?` = `SnapDay (snap_day): SnapDay?` (union) · `CalendarBillingFirstCharge (calendar_billing_first_charge): FirstChargeType?` | record summary: *"(Optional). Cannot be used when also specifying next_billing_at"* | Aligns billing to a calendar day; the docs say nothing about avoiding the initial charge, and it is mutually exclusive with `NextBillingAt` |
| `PreviousBillingAt (previous_billing_at)` | `DateTimeOffset?` | *"Providing a previous_billing_at that is in the past will set the current_period_starts_at when the subscription is created. … Can only be used if next_billing_at is also passed."* | Import-only; requires `NextBillingAt` |
| `NetTerms (net_terms)` | **`string?`** (not `int?`) | *"(Optional) Default: null The number of days after renewal (on invoice billing) that a subscription is due. A value between 0 (due immediately) and 180."* | Pairs with invoice/remittance billing; does not change whether payment is attempted |
| `ReceivesInvoiceEmails (receives_invoice_emails)` | **`string?`** (not `bool?`) | *"(Optional) Default: True - Whether or not this subscription is set to receive emails related to this subscription."* | Controls invoice emails only |

**Recommended body** (the collection method is the documented route; the deferral fields are not):
`new CreateSubscription { ProductHandle = planHandle, CustomerId = customer.Id!.Value, PaymentCollectionMethod = CollectionMethod.Remittance }`.
Add `NetTerms` only if the app wants a due-date other than the site default.

**Resulting state.** No operation Notes, no enum summary and no property doc in the SDK states which
`SubscriptionState` a remittance/invoice subscription lands in immediately after create. `SubscriptionState`
does document `active` = *"A normal, active subscription. It is not in a trial and is paid and up to date."*
and `pending` = *"An internal (transient) state that indicates a subscription is in the creation process. Do
not base any access decisions in your app on this state, as it may not always be exposed."* — that second
sentence is itself a directive. `UNVERIFIED`: do **not** gate the 201 response or entitlement on
`State == SubscriptionState.Active`; read `Subscription.State` back, surface `State?.Value` verbatim, and
treat any non-null state as "created", logging the value the first time an unexpected one appears.

**Cheap way to verify before creating:** `client.Subscriptions.PreviewSubscription(body: <same CreateSubscriptionRequest>, ct: ct)`
→ `SubscriptionPreviewResponse`, Case **B** (`SdkException<RawError>`), no pagination; per its Notes *"A
subscription will not be created by utilizing this endpoint; it is meant to serve as a prediction"* and *"You
do not need to include a card number to generate tax information when you are previewing a subscription."*
Source: `operations/Subscriptions.md`.

---

## 3. Trap notes

- ⚠ **T1 — Step 0 (client registration).** `AddMaxioAdvancedBillingClient` registers the SDK client as a **singleton** wrapping one `IHttpClientFactory`-created `HttpClient` that is never replaced, and invokes your `configure` callback once at registration. Whether that lifetime is what an ASP.NET Core app should use, and how the factory/handler pipeline must be owned, decides whether long-running instances keep working. Separately, the extension is declared as a C# 14 `extension(IServiceCollection services)` member — **if it fails to resolve when compiled by the .NET 8 SDK, use the manual registration in §2.2, which is byte-for-byte what the extension does** (a build-time check, not a guess). **MUST load `dotnet-client-initialization`** before wiring the client.
- ⚠ **T2 — Steps 2a/2c (writes).** The SDK exposes no idempotency-key mechanism (§2.10). Whether a failed or timed-out `POST` can be silently re-sent by the built-in resilience pipeline — and therefore whether your double-click guard is sufficient — is governed by the retry semantics, which the signatures do not show. **MUST load `dotnet-configuration-resilience`** before enabling or tuning retries around these writes.
- ⚠ **T3 — Step 0 (timeouts).** `RetryOptions` exposes `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry` — **all members are `required`**, so you must build a complete instance or start from `RetryOptions.Default()`. What `Timeout` actually bounds, and how it relates to the timeout on the registered `HttpClient` and to the request's `ct`, is not what the property name suggests. **MUST load `dotnet-configuration-resilience`**.
- ⚠ **T4 — Step 1 (plan projection).** `Product` carries **two** separately generated card flags — `RequireCreditCard (require_credit_card)` and `RequestCreditCard (request_credit_card)`, both `bool?` — and neither the map nor the source says which the live payload populates for "payment method required". Directive: compute `requiresPaymentMethod = p.RequireCreditCard ?? p.RequestCreditCard ?? false` and treat a null/null pair as "not required" for these sandbox plans. `UNVERIFIED` — only live traffic can settle which flag is sent.
- ⚠ **T5 — Steps 1–3 (model reading/building).** Almost every field on `Product`, `Subscription` and `Customer` is nullable (including `Customer.Id`, `Subscription.Id` and `SubscriptionResponse.Subscription` itself), enums are `StringEnum<T>` records rather than C# enums, and JSON fields the SDK does not model are dropped on deserialize. How to build request models and map responses onto your DTOs without silently emitting or swallowing nulls is the skill's subject. **MUST load `dotnet-models`** before writing the mapping layer.
- ⚠ **T6 — Steps 1–3 (call sites).** Every list/lookup operation in scope has a run of nullable parameters with **no C# default** (O1 has 8, O10 has 14, O8 has 7): all must be passed, a positional call mis-binds silently, and O1/O3 order their shared parameters differently. Use named arguments — and the token parameter is `ct:`. **MUST load `dotnet-calling-endpoints`** before the first call.
- ⚠ **T7 — Steps 1–3 (error boundary).** Every operation is throw-only, the exception carries no status, and the Case A/B split is per-operation (§2.9). How to build a catch ladder that neither swallows nor mis-classifies is the skill's subject. **MUST load `dotnet-error-handling`** before writing the boundary.
- ⚠ **T8 — Step 2b (idempotency window).** O7 has no pagination parameters, so if the provider caps that response the "no active subscription found" branch could create a duplicate. Directive: treat an empty or short result as *not proof of absence* — serialize the write per user in the application, and on a `CreateSubscription` 422 re-run O7 once and return the existing subscription if one is now visible. `UNVERIFIED` — whether the provider caps this response can only be seen on live traffic.
- ⚠ **T9 — Step 1 (family-by-handle path segment).** The SDK passes `productFamilyId` through `Uri.EscapeDataString` before substituting it into the path (`Core/TemplateParamsFactory.cs`), so `handle:eshop-subscribe` reaches the wire as `handle%3Aeshop-subscribe`. Directive: implement the handle form as primary, and on a 404 (`TryGetString` / `TryGetRawError` → `HttpStatusCode.NotFound`) fall back once to O4 `ListProductFamilies` + match `ProductFamily.Handle` + retry with `family.Id!.Value.ToString()` — a runtime-derived id, never configured. `UNVERIFIED` — whether the provider decodes the escaped colon can only be seen on live traffic.
- ⚠ **T10 — Tests.** The `HttpClient` constructor argument of `MaxioAdvancedBillingClient` is the seam; the controller classes are concrete with no interface to mock. **MUST load `dotnet-testing`** before stubbing the SDK.
- ⚠ **T12 — Step 2c (collection method).** The product's `require_credit_card`/`request_credit_card` flags do **not** decide whether Maxio attempts to settle the first period's balance — the subscription's `PaymentCollectionMethod` does (§2.11), and a card-free create without it is rejected 422. `CollectionMethod` is a `StringEnum<T>` record, so `CollectionMethod.Remittance` (not the string `"remittance"`) — and it is the one field whose legal value set differs by site architecture. Whether the response's `State` for such a subscription is `active` is `UNVERIFIED`; do not gate on it. **MUST load `dotnet-models`** for the `StringEnum` construction/comparison rules before assigning it.
- ⚠ **T11 — Step 0 (auth).** Basic auth with `Username` = the API key and `Password` = the literal `"x"`; both `BasicAuthCredentials` members are `required`. Where the credentials should be read from and how they reach the client (given the eager `configure` callback in T1) is the skill's subject. **MUST load `dotnet-authentication`**.

---

## 4. REQUIRED READING — load **before implementation starts**

This sheet deliberately does **not** carry these skills' contents: the trap notes name the hazard, the
skill carries the resolution, the defaults and the worked examples.

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 0 — constructing/registering `MaxioAdvancedBillingClient`, `HttpClient` ownership and lifetime (T1) |
| `dotnet-authentication` | Step 0 — supplying the Basic credentials from configuration (T11) |
| `dotnet-configuration-resilience` | Step 0 and every write — retries, timeouts, base-URL/server selection, pagination (T2, T3) |
| `dotnet-calling-endpoints` | Steps 1–3 — every controller call, named arguments, `ct:` (T6) |
| `dotnet-models` | Steps 1–3 — building `CreateCustomer`/`CreateSubscription`, reading nullable and `StringEnum` fields (T5) |
| `dotnet-error-handling` | Steps 1–3 — the integration's exception boundary (T7 and both rows below) |
| `dotnet-testing` | Tests for the integration layer (T10) |

Two hazards that must shape the boundary from its first version — `System.Text.Json.JsonException`
reaches it from two directions and they need opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from
  deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the
  integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the
  `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a
  5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something
  that can never succeed.

Both bite concretely here: `ProductResponse.Product`, `CustomerResponse.Customer` and
`ErrorListResponse1.Errors` are `required` (direction 1), and the `CreateCustomer` 422 shape is the
mismatch analysed in §2.9 (direction 2).

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions**

1. The Maxio customer `reference` is a stable per-user string derived from the eShopOnWeb identity (the brief says "email + a stable per-user reference string"). I assume the **reference**, not the email, is the idempotency key: `ReadCustomerByReference` is the only exact-match lookup the SDK exposes, and `CreateCustomer`'s Notes enforce uniqueness on `reference`. The exact composition of that string is `YOUR CALL — not in the map`.
2. The sandbox site is on the `Us` server group (`https://{site}.chargify.com`). If the account is EU-hosted, set `Environment = ServerEnvironment.Eu` and configure `Server.Production.Eu.Site` / `.BaseUrl` — the same two member names on the `Eu` object.
3. `GET /api/subscription-plans` surfaces every non-archived product of the family regardless of price point; no price-point filtering is planned, and `ProductPricePointHandle`/`Id` are left unset on create so the product's default price point applies.
4. "Not archived" is filtered both server-side (`includeArchived: false`) and client-side (`ArchivedAt is null`). The client-side belt-and-braces filter is deliberate: the map does not state that `include_archived=false` is the server default or that it excludes archived products completely.
5. The metered component `api-call` is out of scope — no `Components`/`SubscriptionComponents` operation is planned and `CreateSubscription.Components` is left unset.
6. `POST /api/subscriptions` reports `NextAssessmentAt` as the "next billing" date, because `Subscription` exposes no `next_billing_at` (§2.6).
7. Concurrency control for the double-click case (per-user locking, request de-duplication, or equivalent) is the application's design decision — the SDK supplies no idempotency key (§2.10). The plan supplies the recovery path (T2, T8); choosing and placing the guard is yours.
8. The `cp-exp-2` sandbox runs the current **Relationship Invoicing** architecture, so `CollectionMethod.Remittance` is the legal no-payment-profile value (§2.11). If it is a legacy Statements site, `CollectionMethod.Invoice` is the legal value instead — the one-retry directive in §2.11 covers both without guessing a third value.
9. The three endpoints derive the caller's identity from the existing JWT authentication of `src/PublicApi`; how the identity (email, names, reference) is obtained from the authenticated principal is `YOUR CALL — not in the map`.

**Blockers**

*(none — no planned call is expected to be rejected by the provider. The two provider-behaviour
uncertainties, T9's escaped-colon path segment and T4's card flag, each carry a concrete fallback inside
the step that uses them rather than blocking the plan.)*
