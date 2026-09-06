# Maxio Advanced Billing — integration plan & CONTRACT SHEET (eShopOnWeb `src/PublicApi`)

Grounded against the bundled SDK map (`sdk-map.md`, `map/operations/*.md`, `map/models/*.md`) and,
where the map left a real gap, against the SDK source at tag `v1.0.2`. Every row cites where it
came from. Nothing here is from memory of this API.

---

## 1. Scope & sequence

| # | Step | Maxio operations used |
|---|---|---|
| 1 | Add the NuGet package; add the `using` set; add a strongly-bound `Maxio:*` options class | — |
| 2 | Register the client in DI (auth + subdomain **or** verbatim base URL) | — (client construction only) |
| 3 | Write the error boundary that maps SDK exceptions → HTTP results (**write this before the endpoints**) | — |
| 4 | `GET /api/subscription-plans` — list plans of the configured product family | `ProductFamilies.ListProductsForProductFamily` |
| 5 | `POST /api/subscriptions` — ensure customer (idempotent), then ensure subscription (idempotent) | `Customers.ReadCustomerByReference` → `Customers.CreateCustomer` → `Subscriptions.FindSubscription` / `Customers.ListCustomerSubscriptions` → `Subscriptions.CreateSubscription` |
| 6 | `GET /api/my-subscriptions` — list this user's subscriptions | `Customers.ReadCustomerByReference` → `Customers.ListCustomerSubscriptions` |
| 7 | *(secondary)* expose product-family components | `ProductFamilies.ListProductFamilies` (resolve handle → numeric id) → `Components.ListComponentsForProductFamily` |

**Step-7 caveat, up front:** `ListComponentsForProductFamily` takes a **numeric** `productFamilyId`
— it does **not** accept the `handle:` form that `ListProductsForProductFamily` accepts (source:
`Api/Components.cs` line 311 signature vs `Api/ProductFamilies.cs` line 96 `<param>` doc). And
`ListProductFamilies` exposes **no handle/name filter and no `page`/`perPage` parameters at all**
(`operations/ProductFamilies.md`). So "list components" costs one extra unfiltered, unpageable
call plus a client-side match on `ProductFamily.Handle`. It is *supported*, but it is not cheap and
it is not handle-addressable. Decide on that basis.

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

### 2.0 Package, namespaces, controllers

| Item | Value | Source |
|---|---|---|
| NuGet package id | `AsadAli.AdvancedBilling.Sdk` | `sdk-map.md` |
| Version to install | **Pin explicitly.** The map's stamp is source tag `v1.0.2`; the `.csproj` at that same tag declares `<Version>1.0.0</Version>`. The two disagree, so the published version cannot be derived from either. Install with an explicit `Version="…"` and record what you pinned. | `sdk-map.md` stamp vs `MaxioAdvancedBilling.csproj` (source) — `UNVERIFIED` |
| Root namespace (differs from package id) | `MaxioAdvancedBilling` | `sdk-map.md` |
| Target framework | `netstandard2.0` | `sdk-map.md` |
| Transitive deps | `Polly`, `Microsoft.Extensions.Http`, `System.Net.Http.Json`, `System.Net.ServerSentEvents` | `MaxioAdvancedBilling.csproj` (source) |

Namespaces — add **each** one you use; C# does not import child namespaces transitively:

| Contents | Namespace |
|---|---|
| Client, options, `ServerOptions`, the DI extension | `MaxioAdvancedBilling` |
| Operation controllers (`client.Products`, `client.Customers`, …) | `MaxioAdvancedBilling.Api` |
| Records (`Product`, `Subscription`, `CreateSubscriptionRequest`, …) | `MaxioAdvancedBilling.Models` |
| Enums (`SubscriptionState`, `IntervalUnit`, …) | `MaxioAdvancedBilling.Models.Enums` |
| Typed error classes (`CreateSubscriptionError`, …) | `MaxioAdvancedBilling.Errors` |
| `SdkException<TError>` | `MaxioAdvancedBilling.Core.Exceptions` |
| `RawError`, `ApiError` | `MaxioAdvancedBilling.Core.ErrorResponse` |
| `BasicAuthCredentials` | `MaxioAdvancedBilling.Core.Authentication.Basic` |
| `RetryOptions` | `MaxioAdvancedBilling.Core.Configuration` |
| `ServerEnvironment`, `ProductionOptions` (+ nested `UsOptions`/`EuOptions`), `EbbOptions` | `MaxioAdvancedBilling.Servers` |

Source: `sdk-map.md` namespace table; `Core/Exceptions/SdkException.cs`,
`Core/Authentication/Basic/BasicAuthCredentials.cs`, `ServerOptions.cs`, `Servers/ProductionOptions.cs`.

⚠ **Name collision — do not `using MaxioAdvancedBilling.Errors;` and `using MaxioAdvancedBilling.Models;`
together and then write the bare identifier `Errors`.** There is a record `MaxioAdvancedBilling.Models.Errors`
*and* a namespace `MaxioAdvancedBilling.Errors`. Write `MaxioAdvancedBilling.Models.Errors` fully
qualified wherever you name that record. Source: `Models/Errors.cs`, `Errors/` directory.

Controllers used (all `MaxioAdvancedBilling.Api`, all reached as properties on the client):

| Property | Controller type | Source |
|---|---|---|
| `client.Products` | `MaxioAdvancedBilling.Api.Products` — owns `ListProducts`, `ReadProduct`, `ReadProductByHandle`. ⚠ It does **not** own `ListProductsForProductFamily` | `operations/Products.md` |
| `client.ProductFamilies` | `MaxioAdvancedBilling.Api.ProductFamilies` — owns `ListProductFamilies`, `ReadProductFamily` **and `ListProductsForProductFamily`** (products-per-family lives here, not on `client.Products`) | `operations/ProductFamilies.md` |
| `client.Customers` | `MaxioAdvancedBilling.Api.Customers` | `operations/Customers.md` |
| `client.Subscriptions` | `MaxioAdvancedBilling.Api.Subscriptions` | `operations/Subscriptions.md` |
| `client.Components` | `MaxioAdvancedBilling.Api.Components` | `operations/Components.md` |

### 2.1 Client construction, auth, environment, base URL

`MaxioAdvancedBillingClient` has **exactly one** constructor:
`MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`
(source: `MaxioAdvancedBillingClient.cs`; also `sdk-map.md`).

`MaxioAdvancedBillingClientOptions` — all four properties are `get; set;` with non-null defaults except auth
(source: `MaxioAdvancedBillingClientOptions.cs`):

| Property | Type (fully qualified) | Default |
|---|---|---|
| `Environment` | `MaxioAdvancedBilling.Servers.ServerEnvironment` | `ServerEnvironment.Default()` ⇒ `ServerEnvironment.Us` |
| `Retry` | `MaxioAdvancedBilling.Core.Configuration.RetryOptions` | `RetryOptions.Default()` |
| `Server` | `MaxioAdvancedBilling.ServerOptions` | `new()` |
| `BasicAuth` | `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?` | `null` |

`BasicAuthCredentials` — **both** members are C# `required`, `init`-only:
`Username: string` (= the API key) and `Password: string` (= the literal `"x"`).
Source: `Core/Authentication/Basic/BasicAuthCredentials.cs`; `sdk-map.md` "Servers & auth".

`ServerEnvironment` is **not** a C# enum — it is a `StringEnum<ServerEnvironment>` record with two
static readonly members and a private constructor, so `Us`/`Eu` are the only reachable values
(source: `Servers/ServerEnvironment.cs`):

| Member | Wire value | Resolves to |
|---|---|---|
| `ServerEnvironment.Us` *(default)* | `US` | `https://{site}.chargify.com` |
| `ServerEnvironment.Eu` | `EU` | `https://{site}.ebilling.maxio.com` |

⚠ **There is no `Sandbox` environment value.** A Maxio sandbox site is just a site subdomain on the
US host — use `ServerEnvironment.Us` and set the subdomain. Source: `Servers/ServerEnvironment.cs`
(the type declares exactly two members).

Server/base-URL override points — `ServerOptions.Production` is `ProductionOptions` (auto-initialised
`new()`), `ProductionOptions.Us` is the **nested** type `ProductionOptions.UsOptions` (auto-initialised
`new()`), with two `get; set;` string properties (source: `ServerOptions.cs`, `Servers/ProductionOptions.cs`):

| Path | Type | Default |
|---|---|---|
| `options.Server.Production.Us.BaseUrl` | `string` | `"https://{site}.chargify.com"` |
| `options.Server.Production.Us.Site` | `string` | `"subdomain"` |
| `options.Server.Production.Eu.BaseUrl` / `.Eu.Site` | `string` | `"https://{site}.ebilling.maxio.com"` / `"subdomain"` |
| `options.Server.Ebb.Us.BaseUrl` / `.Us.Site` | `string` | `https://events.chargify.com/{site}` — **not used by any operation in this plan** |

**How `Maxio:BaseUrl` maps onto that.** `BaseUrl` is a **template**: the resolver substitutes the
`{site}` placeholder from `Site` (source: `ProductionOptions.Resolve` builds
`new UrlTemplate(Us.BaseUrl, path, [TemplateParam.ForServer("site", Us.Site)])`). A verbatim URL
containing no `{site}` placeholder therefore passes through unchanged. So:

| `Maxio:BaseUrl` | What to set | Source |
|---|---|---|
| **set** | `options.Server.Production.Us.BaseUrl = <Maxio:BaseUrl>` — used verbatim; leave `Site` alone (it has nothing to substitute into) | `Servers/ProductionOptions.cs` |
| **not set** | leave `BaseUrl` at its default template and set `options.Server.Production.Us.Site = <Maxio:Subdomain>` | `Servers/ProductionOptions.cs` |

Both branches set `options.Environment = ServerEnvironment.Us` (this is also the default) and
`options.BasicAuth = new BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }`.

**DI registration.** `MaxioAdvancedBilling.ServiceCollectionExtensions` adds one extension on
`IServiceCollection` (source: `ServiceCollectionExtensions.cs`):

`IServiceCollection AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>? configure = null)`

What its body actually does — read this before you decide how to wire it:

- it calls `services.AddHttpClient()` and resolves `IHttpClientFactory.CreateClient()` (the **default,
  unnamed** client — you cannot name it or configure its handler through this overload);
- it registers `MaxioAdvancedBillingClient` as a **Singleton**;
- `configure` is invoked **eagerly, at registration time**, against a single captured options instance
  — the callback has **no `IServiceProvider`**, so `IConfiguration`/`IOptions` values must be read by
  the caller at registration and passed in.

Configuration binding keys this plan uses (bind by key; do not name raw environment variables):

| Key | Required | Use |
|---|---|---|
| `Maxio:ApiKey` | yes | `BasicAuthCredentials.Username` |
| `Maxio:Subdomain` | yes, unless `Maxio:BaseUrl` is set | `options.Server.Production.Us.Site` |
| `Maxio:BaseUrl` | optional | `options.Server.Production.Us.BaseUrl`, verbatim |
| `Maxio:ProductFamilyHandle` | yes | `productFamilyId: $"handle:{…}"` in step 4; matched against `ProductFamily.Handle` in step 7 |

### 2.2 Operations

Read the two literal warning lines at the top of this section before using any signature below.

| # | Operation | Signature (verbatim) | Request model | Response envelope → fields read | Error | Pagination | Source |
|---|---|---|---|---|---|---|---|
| 4 | **`client.ProductFamilies.ListProductsForProductFamily`** — ⚠ it returns *products* but it is owned by the **ProductFamilies** controller, not `client.Products` (`operations/ProductFamilies.md` header: `Accessor: client.ProductFamilies` · `Source: Api/ProductFamilies.cs`). Calling it on `client.Products` is `CS1061`. | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 8 params `dateField`…`include` are nullable **with no default**: pass `null` explicitly | none (GET). `productFamilyId` accepts **either** the numeric id **or** `handle:my-family` (`Api/ProductFamilies.cs` line 96 `<param>` doc) | `IReadOnlyList<ProductResponse>`; `ProductResponse.Product (product): Product` is **`required`** → one level down per item. Read `Product.Handle`, `.Name`, `.Description`, `.PriceInCents (price_in_cents): long?`, `.Interval (interval): int?`, `.IntervalUnit (interval_unit): IntervalUnit?`, `.ArchivedAt`, `.ProductFamily (product_family): ProductFamily?` | **Case A** `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` **[404]** · `TryGetRawError(out RawError)` [fallback] | manual `page` + `perPage` (defaults 1 / 20; `perPage` max is 200 per the SDK's `<param>` doc) | `operations/ProductFamilies.md`, `records-3-Of-Su.md`, `records-2-Cr-Ne.md` |
| 5a | `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — query `reference` ← `reference` | none (GET) | `CustomerResponse`; `.Customer (customer): Customer` is **`required`** → read `Customer.Id (id): int?`, `.Reference`, `.Email`, `.FirstName`, `.LastName` | **Case B** `SdkException<RawError>` — `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` · `ReadAsBytes()` | none | `operations/Customers.md`, `records-1-Ac-Cr.md` |
| 5b | `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly** | `CreateCustomerRequest { Customer (customer): CreateCustomer }` — `Customer` is **`required`**. `CreateCustomer` **required** members: `FirstName (first_name): string`, `LastName (last_name): string`, `Email (email): string`. Optional but load-bearing here: **`Reference (reference): string?`** — see idempotency below. Others optional: `Organization`, `Address`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `CcEmails`, `VatNumber`, `TaxExempt`, `TaxExemptReason`, `ParentId`, `SalesforceId` — all left unset by this plan | `CustomerResponse` → `.Customer.Id`, `.Customer.Reference` | **Case A** `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` **[422]** · `TryGetRawError(out RawError)` [fallback] | none | `operations/Customers.md`, `records-1-Ac-Cr.md` |
| 5c | `client.Subscriptions.FindSubscription` | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` nullable, no default → **must pass explicitly**; query `reference` ← `reference` | none (GET) | `SubscriptionResponse`; `.Subscription (subscription): Subscription?` is **nullable** → null-check before dereferencing | **Case A** `SdkException<FindSubscriptionError>` — `TryGetNoContent(out RawError)` **[404]** · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md`, `records-4-Su-We.md` |
| 5d / 6 | `client.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | none (GET) | `IReadOnlyList<SubscriptionResponse>` → each `.Subscription?` (nullable) → `Id`, `State`, `ProductPriceInCents`, `CurrentPeriodEndsAt`, `CurrentPeriodStartedAt`, `NextAssessmentAt`, `Reference`, `Product?.Name`, `Product?.Handle` | **Case B** `SdkException<RawError>` | **none — the SDK exposes no `page`/`perPage` on this operation.** If a customer can exceed the server's page size you cannot walk past page 1 here; use op 5e instead | `operations/Customers.md` |
| 5e | `client.Subscriptions.ListSubscriptions` *(fallback for 5d when filtering/paging is needed)* | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 14 params `state`…`include` are nullable **with no default**: pass `null` explicitly | none (GET). **⚠ It has no customer filter** — `state`, `product`, `coupon`, dates, `metadata` only. Site-wide results must be filtered client-side by `Subscription.Customer?.Id` | `IReadOnlyList<SubscriptionResponse>` (same reads as 5d) | **Case B** `SdkException<RawError>` | manual `page` + `perPage` (defaults 1 / 20) | `operations/Subscriptions.md` |
| 5f | `client.Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly** | `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription }` — `Subscription` is **`required`**. See the field table below — **`CreateSubscription` marks nothing `required`**, so `required?` selects nothing for you | `SubscriptionResponse` → `.Subscription?` (**nullable**) → `Id`, `State`, `ProductPriceInCents`, `CurrentPeriodEndsAt`, `NextAssessmentAt`, `Reference`, `Product?.Name`, `Product?.Handle` | **Case A** `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` **[422]** · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md`, `records-2-Cr-Ne.md`, `records-4-Su-We.md` |
| 7a | `client.ProductFamilies.ListProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 nullable, no default → pass `null` | none (GET) | `IReadOnlyList<ProductFamilyResponse>`; `.ProductFamily (product_family): ProductFamily?` is **nullable** → match `.Handle` client-side, take `.Id` | **Case B** `SdkException<RawError>` | **none at all** (no `page`/`perPage`) | `operations/ProductFamilies.md`, `records-3-Of-Su.md` |
| 7b | `client.Components.ListComponentsForProductFamily` | `ListComponentsForProductFamily(int productFamilyId, bool? includeArchived, ListComponentsFilter? filter, BasicDateField? dateField, string? endDate, string? endDatetime, string? startDate, string? startDatetime, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 7 params `includeArchived`…`startDatetime` are nullable, no default → pass `null`. **`productFamilyId` is `int`: no `handle:` form** | none (GET) | `IReadOnlyList<ComponentResponse>`; `.Component (component): Component` is **`required`** → `Component.Handle`, `.Name`, `.UnitName`, `.PricingScheme`, `.Kind (kind): ComponentKind?`, `.PricePerUnitInCents`, `.UnitPrice (unit_price): string?`, `.Archived`, `.Recurring` | **Case B** `SdkException<RawError>` | manual `page` + `perPage` (defaults 1 / 20) | `operations/Components.md`, `records-1-Ac-Cr.md` |

Related operations deliberately **not** used, so you don't go looking:

| Operation | Why not | Source |
|---|---|---|
| `client.Products.ReadProductByHandle(string apiHandle, …)` | Reads one product by `api_handle`; useful if you ever need to validate a single requested plan handle before op 5f. Returns `ProductResponse`, **Case B** | `operations/Products.md` |
| `client.Customers.ListCustomers(… string? q …)` | Search-by-email/reference, **fuzzy, returns an array**. The Notes say explicitly: *"To retrieve a single, exact match by reference, use the lookup endpoint."* That lookup **is** op 5a — prefer it for identity | `operations/Customers.md` |
| `client.ProductFamilies.ReadProductFamily(int id, …)` | ⚠ Its own prose says the family "can be specified either with the id number, or with the `handle:my-family` format", but the **generated signature takes `int id`** — the prose and the signature contradict each other, so a handle cannot be passed. This is why step 7 needs op 7a | `operations/ProductFamilies.md`; `Api/ProductFamilies.cs` line 158 |
| `client.Subscriptions.PreviewSubscription` | Same body type as 5f, creates nothing; only if you later want a pre-signup price preview. Returns `SubscriptionPreviewResponse`, **Case B** | `operations/Subscriptions.md` |

### 2.3 `CreateSubscription` request fields (op 5f) — **nothing is marked `required`**

`CreateSubscription` (namespace `MaxioAdvancedBilling.Models`, source `records-2-Cr-Ne.md` /
`Models/CreateSubscription.cs`) declares **no `required` members at all**. The compiler will therefore
accept an empty body. What makes the call *accepted* comes from the operation's Notes, not the type:

> *"Specify the product with `product_id` or `product_handle`. … Identify an existing customer with
> `customer_id` or `customer_reference`. … To create a new customer, pass customer_attributes."*
> — `operations/Subscriptions.md`, CreateSubscription Notes

Fields this plan **sets**:

| C# property (wire name) | Type | Why |
|---|---|---|
| `ProductHandle (product_handle)` | `string?` | The plan the caller chose. Notes: product must be given as `product_handle` **or** `ProductId (product_id): int?` |
| `CustomerId (customer_id)` | `int?` | The `Customer.Id` from op 5a/5b. Notes: customer must be given as `customer_id` **or** `CustomerReference (customer_reference): string?` (whose doc reads: *"The reference value (provided by your app) of an existing customer within Chargify. Required, unless a `customer_id` or a set of `customer_attributes` is given."*) |
| `Reference (reference)` | `string?` | *"The reference value (provided by your app) for the subscription itself."* — this is the idempotency key, and what op 5c looks up |

Fields this plan **deliberately leaves unset**, each named in the Notes or adjacent to them:

| C# property (wire name) | Type | Why left out |
|---|---|---|
| `PaymentCollectionMethod (payment_collection_method)` | `CollectionMethod?` | Left unset so the site/product default applies. Setting it wrongly is the most likely cause of an unexpected 422 on a card-less signup — see the `UNVERIFIED` row in §5 |
| `CreditCardAttributes` / `PaymentProfileAttributes` / `BankAccountAttributes` / `PaymentProfileId` | `PaymentProfileAttributes?` / `BankAccountAttributes?` / `int?` | The brief states the plans do not require a payment method. Notes: *"Payment information may be required to create a subscription, depending on the options for the Product being subscribed."* |
| `CustomerAttributes (customer_attributes)` | `CustomerAttributes?` | We create the customer ourselves in op 5b so we control the `reference`. Passing this instead would create a customer implicitly and defeat the idempotency design |
| `ProductPricePointHandle` / `ProductPricePointId` | `string?` / `int?` | Unset ⇒ the product's default price point |
| `CouponCode` / `CouponCodes` / `OfferId` / `Components` / `CalendarBilling` / `NextBillingAt` / `InitialBillingAt` / `Currency` / `NetTerms` / `Metafields` / `Group` | various | Out of scope for this flow |
| **`Ref (ref)`** | `string?` | ⚠ **Trap: this is NOT the subscription reference.** Its doc reads *"A valid referral code… If supplied, must be valid, or else subscription creation will fail."* (`Models/CreateSubscription.cs` line 188). Confusing `Ref` with `Reference` turns every signup into a guaranteed failure. Use `Reference` |
| `DeferSignup (defer_signup)` | `bool? = false` | Has a generated default of `false` — leave it |
| `SkipBillingManifestTaxes`, `ImportMrr`, `PreviousBillingAt`, `ActivatedAt`, `CanceledAt`, `AgreementAcceptance`, `AchAgreement`, `DunningCommunicationDelay*`, `StoredCredentialTransactionId`, `SalesRepId`, `ReceivesInvoiceEmails`, `ReasonCode`, `ProductChangeDelayed`, `ExpiresAt`, `ExpirationTracksNextBillingChange`, `AuthorizerFirstName`, `AuthorizerLastName`, `AgreementTerms`, `CancellationMessage`, `CancellationMethod`, `CalendarBillingFirstCharge`, `CustomPrice`, `PrepaidConfiguration` | various | Not applicable to a no-trial, no-fee, card-less signup |

Null fields are **omitted from the JSON**, not sent as `null` — every optional property carries
`[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` (source: `Models/CreateSubscription.cs`).

### 2.4 Idempotency mechanics (the exact lookups that exist)

**Customer.** The mechanism the brief asked about exists and is exact:

- **Set** a stable reference on create: `CreateCustomer.Reference (reference): string?`.
- **Look up** by it: `Customers.ReadCustomerByReference(string reference, CancellationToken ct = default)`
  → `GET /customers/lookup.json?reference=…`. Its Notes: *"Returns a customer by their unique reference ID.
  It will return a single match."*
- The uniqueness guarantee is the provider's, stated in the CreateCustomer Notes: *"The only validation
  restriction is that you may only create one customer for a given reference value. If provided, the
  `reference` value must be unique."* So a duplicate create is **rejected**, and that rejection arrives as
  the 422 branch of `SdkException<CreateCustomerError>`.
- ⇒ Sequence: lookup → on 404 create → **on 422 from create, re-run the lookup and use the existing
  customer** (that 422 is the concurrent-double-click case, not a client bug).
- There is **no** lookup-customer-by-email exact-match operation. `ListCustomers(q: …)` is a fuzzy search
  returning an array (its own Notes redirect you to the lookup endpoint for exact matches). Do not build
  identity on email.
  Source: `operations/Customers.md`.

**Subscription.**

- **Set** `CreateSubscription.Reference` to a value your app derives deterministically from (user, plan).
- **Look up** by it: `Subscriptions.FindSubscription(string? reference, …)` → `GET /subscriptions/lookup.json`.
  Absent ⇒ 404 ⇒ `SdkException<FindSubscriptionError>` with `TryGetNoContent` true.
- **List/filter for an existing active one**: `Customers.ListCustomerSubscriptions(customerId)` (all states,
  filter on `Subscription.State` client-side against `SubscriptionState.Active` etc.), or
  `Subscriptions.ListSubscriptions(state: SubscriptionStateFilter.Active, …)` — note **`ListSubscriptions`
  has no customer filter**, so it returns the whole site and you filter on `Subscription.Customer?.Id`.
- ⚠ **The map does not state that a subscription `reference` is unique-enforced** the way the customer
  Notes state it for customers. See the `UNVERIFIED` row in §5 — the double-click guard must not assume
  the provider will reject a duplicate.
  Source: `operations/Subscriptions.md`, `operations/Customers.md`.

### 2.5 Response fields you need, by name

`Product` (namespace `MaxioAdvancedBilling.Models`; source `records-3-Of-Su.md`) — all nullable:

| C# property (wire name) | Type | Use |
|---|---|---|
| `Handle (handle)` | `string?` | the plan handle passed to op 5f |
| `Name (name)` / `Description (description)` | `string?` | display |
| `PriceInCents (price_in_cents)` | `long?` | price **in cents** |
| `Interval (interval)` | `int?` | e.g. `1` |
| `IntervalUnit (interval_unit)` | `IntervalUnit?` | `day` \| `month` |
| `ArchivedAt (archived_at)` | `DateTimeOffset?` | non-null ⇒ archived |
| `ProductFamily (product_family)` | `ProductFamily?` | `.Id`, `.Name`, `.Handle` |
| `TrialPriceInCents` / `TrialInterval` / `TrialIntervalUnit` | `long?` / `int?` / `IntervalUnit?` | confirm "no trial" |
| `InitialChargeInCents (initial_charge_in_cents)` | `long?` | confirm "no setup fee" |
| `RequireCreditCard (require_credit_card)` / `RequestCreditCard (request_credit_card)` | `bool?` | confirm "payment method not required" — **two distinct fields, both present** |
| `ProductPricePointId` / `ProductPricePointHandle` / `ProductPricePointName` / `DefaultProductPricePointId` | `int?` / `string?` / `string?` / `int?` | price-point display, if wanted |

`Subscription` (namespace `MaxioAdvancedBilling.Models`; source `records-4-Su-We.md`) — all nullable:

| C# property (wire name) | Type | Use |
|---|---|---|
| `Id (id)` | `int?` | |
| `State (state)` | `SubscriptionState?` | see enum table |
| `ProductPriceInCents (product_price_in_cents)` | `long?` | price at signup |
| `CurrentBillingAmountInCents (current_billing_amount_in_cents)` | `long?` | current recurring amount |
| `CurrentPeriodStartedAt` / `CurrentPeriodEndsAt` | `DateTimeOffset?` | current period |
| `NextAssessmentAt (next_assessment_at)` | `DateTimeOffset?` | next billing date |
| `Reference (reference)` | `string?` | your idempotency key, echoed back |
| `Product (product)` | `Product?` | `.Name`, `.Handle`, `.PriceInCents`, `.Interval`, `.IntervalUnit` |
| `Customer (customer)` | `Customer?` | `.Id`, `.Reference` — needed to filter op 5e |
| `ActivatedAt` / `CanceledAt` / `CancelAtEndOfPeriod` / `ExpiresAt` / `PreviousState` / `TrialStartedAt` / `TrialEndedAt` | various | lifecycle, if displayed |

`Customer` (namespace `MaxioAdvancedBilling.Models`; source `records-1-Ac-Cr.md`) — `Id (id): int?`,
`Reference (reference): string?`, `Email (email): string?`, `FirstName (first_name): string?`,
`LastName (last_name): string?`, `Organization`, `CreatedAt`, `UpdatedAt`, … all nullable.

**Envelope recap — reads go one level down, and the nullability differs per envelope:**

| Envelope | Inner field | Nullability | Source |
|---|---|---|---|
| `ProductResponse` | `Product (product): Product` | **`required`** | `records-3-Of-Su.md` |
| `CustomerResponse` | `Customer (customer): Customer` | **`required`** | `records-1-Ac-Cr.md` |
| `ComponentResponse` | `Component (component): Component` | **`required`** | `records-1-Ac-Cr.md` |
| `SubscriptionResponse` | `Subscription (subscription): Subscription?` | **nullable — null-check it** | `records-4-Su-We.md` |
| `ProductFamilyResponse` | `ProductFamily (product_family): ProductFamily?` | **nullable — null-check it** | `records-3-Of-Su.md` |

A `required` inner field is not a convenience: if a 2xx body omits it, deserialization throws
`System.Text.Json.JsonException` — see the REQUIRED READING hazard rows.

### 2.6 Enums (namespace `MaxioAdvancedBilling.Models.Enums`)

These are `StringEnum<T>` **records, not C# enums** — you cannot `switch` them as enum constants; build
with the static members or `T.FromValue("wire")`. Members below are the literal C# identifiers; the
parenthesised text is the wire value. Source: `models/enums.md`.

| Enum | Members (C# name → wire) |
|---|---|
| `SubscriptionState` | `Pending`→`pending`, `FailedToCreate`→`failed_to_create`, `Trialing`→`trialing`, `Assessing`→`assessing`, `Active`→`active`, `SoftFailure`→`soft_failure`, `PastDue`→`past_due`, `Suspended`→`suspended`, `Canceled`→`canceled`, `Expired`→`expired`, `Paused`→`paused`, `Unpaid`→`unpaid`, `TrialEnded`→`trial_ended`, `OnHold`→`on_hold`, `AwaitingSignup`→`awaiting_signup` |
| `SubscriptionStateFilter` *(op 5e `state` param — a **different, smaller** set)* | `Active`→`active`, `Canceled`→`canceled`, `Expired`→`expired`, `ExpiredCards`→`expired_cards`, `OnHold`→`on_hold`, `PastDue`→`past_due`, `PendingCancellation`→`pending_cancellation`, `PendingRenewal`→`pending_renewal`, `Suspended`→`suspended`, `TrialEnded`→`trial_ended`, `Trialing`→`trialing`, `Unpaid`→`unpaid` |
| `IntervalUnit` | `Day`→`day`, `Month`→`month` |
| `ExpirationIntervalUnit` | `Day`→`day`, `Month`→`month`, `Never`→`never` |
| `CollectionMethod` | `Automatic`→`automatic`, `Remittance`→`remittance`, `Prepaid`→`prepaid`, `Invoice`→`invoice` |
| `ComponentKind` | `MeteredComponent`→`metered_component`, `QuantityBasedComponent`→`quantity_based_component`, `OnOffComponent`→`on_off_component`, `PrepaidUsageComponent`→`prepaid_usage_component`, `EventBasedComponent`→`event_based_component` |
| `PricingScheme` | `Stairstep`→`stairstep`, `Volume`→`volume`, `PerUnit`→`per_unit`, `Tiered`→`tiered` |
| `PricePointType` | `Catalog`→`catalog`, `Default`→`default`, `Custom`→`custom` |
| `SortingDirection` | `Asc`→`asc`, `Desc`→`desc` |
| `BasicDateField` | `UpdatedAt`→`updated_at`, `CreatedAt`→`created_at` |
| `SubscriptionDateField` | `CurrentPeriodEndsAt`, `CurrentPeriodStartsAt`, `CreatedAt`, `ActivatedAt`, `CanceledAt`, `ExpiresAt`, `TrialStartedAt`, `TrialEndedAt`, `UpdatedAt` (wire = snake_case of each) |
| `SubscriptionSort` | `SignupDate`→`signup_date`, `PeriodStart`→`period_start`, `PeriodEnd`→`period_end`, `NextAssessment`→`next_assessment`, `UpdatedAt`, `CreatedAt`, `TotalPayments`→`total_payments`, `Id`→`id`, `OpenBalance`→`open_balance`, `ExpiresAt`→`expires_at` |
| `SubscriptionListInclude` | `SelfServicePageToken`→`self_service_page_token` *(only member)* |
| `SubscriptionInclude` | `Coupons`→`coupons`, `SelfServicePageToken`→`self_service_page_token` |
| `ListProductsInclude` | `PrepaidProductPricePoint`→`prepaid_product_price_point` *(only member)* |
| `CancellationMethod` | `MerchantUi`→`merchant_ui`, `MerchantApi`→`merchant_api`, `Dunning`→`dunning`, `BillingPortal`→`billing_portal`, `Unknown`→`unknown`, `Imported`→`imported` |

There is **no product-family enum** — a product family is the record `ProductFamily`, addressed by
`Id`/`Handle`. Source: `records-3-Of-Su.md`.

Filter records used as parameters (namespace `MaxioAdvancedBilling.Models`, source `records-2-Cr-Ne.md`):
`ListProductsFilter { Ids (ids): IReadOnlyList<int>?, PrepaidProductPricePoint (prepaid_product_price_point): PrepaidProductPricePointFilter?, UseSiteExchangeRate (use_site_exchange_rate): bool? }` ·
`ListComponentsFilter { Ids (ids): IReadOnlyList<int>?, UseSiteExchangeRate (use_site_exchange_rate): bool? }`.
This plan passes `null` for both.

### 2.7 Exceptions — exact types, and how to get a status code

`MaxioAdvancedBilling.Core.Exceptions.SdkException<TError> : Exception` declares **exactly one member**:
`public required TError Error { get; init; }` (source: `Core/Exceptions/SdkException.cs`).

⚠ **`SdkException<T>` has no `StatusCode` property and no message worth reading.** The only place a
status code exists is `RawError.StatusCode`. `RawError` (namespace `MaxioAdvancedBilling.Core.ErrorResponse`)
members, verbatim: `StatusCode: HttpStatusCode` · `ReadAsBytes(): ReadOnlyMemory<byte>` ·
`ReadAsString(): string` · `ReadAsJson<T>(): T?` (source: `sdk-map.md` core-error table,
`Core/ErrorResponse/RawError.cs`).

⚠ **On a Case-A typed error the two accessors are mutually exclusive.** The generated `Create` switches on
status and populates *either* the typed slot *or* the raw fallback — never both. Verbatim from
`Errors/CreateSubscriptionError.cs`:

```
422 => FromJson<ErrorListResponse1>(response, ct).As(AsErrorListResponse1),
_   => FromRawBody(response, ct).As(AsFallback)
```

…where `AsErrorListResponse1` passes `default` (i.e. *no value*) as the fallback. **Consequence:
when `TryGetErrorListResponse1` returns `true`, `TryGetRawError` returns `false` and there is no
status code to read at all** — the fact that the typed accessor matched *is* how you know it was a 422.
Conversely, when the typed accessor returns `false`, `TryGetRawError` gives you `RawError.StatusCode`.
So the 404-vs-422 discrimination the brief asks for is:

| Operation | 404 | 422 | anything else |
|---|---|---|---|
| 5a `ReadCustomerByReference` (Case B) | `catch (SdkException<RawError> ex)` → `ex.Error.StatusCode == HttpStatusCode.NotFound` | n/a | `ex.Error.StatusCode` |
| 5b `CreateCustomer` (Case A) | falls to `TryGetRawError` → check `.StatusCode` | `TryGetCustomerErrorResponse1(out var e)` returns `true` ⇒ **it was a 422** | `TryGetRawError` → `.StatusCode` |
| 5c `FindSubscription` (Case A) | `TryGetNoContent(out var raw)` returns `true` ⇒ **it was a 404** (`raw` is a `RawError`, so `raw.StatusCode` is also available on this branch) | n/a | `TryGetRawError` → `.StatusCode` |
| 4 `ListProductsForProductFamily` (Case A) | `TryGetString(out var s)` returns `true` ⇒ **it was a 404**, `s` is the body as a plain string | n/a | `TryGetRawError` → `.StatusCode` |
| 5f `CreateSubscription` (Case A) | falls to `TryGetRawError` → check `.StatusCode` | `TryGetErrorListResponse1(out var e)` returns `true` ⇒ **it was a 422** | `TryGetRawError` → `.StatusCode` |
| 5d/5e/7a/7b (Case B) | `ex.Error.StatusCode` | `ex.Error.StatusCode` | `ex.Error.StatusCode` |

Typed 422 payload shapes (namespace `MaxioAdvancedBilling.Models`):

| Payload | Fields | Source |
|---|---|---|
| `ErrorListResponse1` *(CreateSubscription 422)* | `Errors (errors): IReadOnlyList<string>` — **`required`** | `records-2-Cr-Ne.md`; `Models/ErrorListResponse1.cs` |
| `CustomerErrorResponse1` *(CreateCustomer 422)* | `Errors (errors): MaxioAdvancedBilling.Models.Errors?` where that record is `{ PerPage (per_page): IReadOnlyList<string>?, PricePoint (price_point): IReadOnlyList<string>? }` | `records-1-Ac-Cr.md`; `Models/CustomerErrorResponse1.cs`, `Models/Errors.cs` |

⚠ **How far to trust `CustomerErrorResponse1` — evidence, not memory.** Its `Errors` payload is the
shared record `MaxioAdvancedBilling.Models.Errors`, whose only two fields are `per_page` and
`price_point` — pagination/price-point keys that have nothing to do with customer validation, and which
appear nowhere in the CreateCustomer contract. That is a generated model reused across unrelated
operations. Two consequences follow from the source alone: an actual customer-validation body whose
`errors` is an **array** cannot deserialize into an object-typed `Errors` and will throw
`JsonException` *instead of* the `SdkException` (see the REQUIRED READING hazard rows); and an
`errors` **object** with any other keys deserializes to an `Errors` whose two properties are both
`null`, so `TryGetCustomerErrorResponse1` returns `true` while carrying **no message**.
**Directive: treat `CustomerErrorResponse1` as best-effort only** — concatenate
`e.Errors?.PerPage` and `e.Errors?.PricePoint` if either is non-empty, otherwise fall back to a
generic "customer could not be created" message; never let the response body's absence be an
unhandled null. Label: `UNVERIFIED` (only live traffic can show what Maxio actually sends here).

⚠ `ErrorListResponse1.Errors` is `required`. A 422 whose `errors` key is missing or is not an array of
strings throws `JsonException` while the error object is being built — same trap, same directive:
extract `string.Join(...)` best-effort inside a guard, fall back to the generic message.

---

## 3. Trap notes

⚠ **Step 2 (client registration)** — `AddMaxioAdvancedBillingClient` registers the client **Singleton**
over one `IHttpClientFactory.CreateClient()` instance held for the process lifetime, and runs its
`configure` callback eagerly at registration with no `IServiceProvider`. Whether that lifetime is the
right one, and what it costs you in handler rotation and in reading configuration, is exactly what the
client-initialization guidance covers. **MUST load `dotnet-client-initialization`** before wiring the
client into DI.

⚠ **Step 2 (auth)** — `BasicAuth` is a plain settable property on an options object that is captured
once. When and where credentials are supplied — and what happens if the key rotates while the singleton
lives — is not visible in the property's type. **MUST load `dotnet-authentication`** before setting
`BasicAuthCredentials`.

⚠ **Step 2 (base URL, retries, timeouts)** — `options.Server.Production.Us.BaseUrl` is a *template*, and
`options.Retry` is a fully-`required` `RetryOptions` you either build whole or start from
`RetryOptions.Default()`. What the retry settings actually bound, which verbs and which failures are
resent, and whether the timeout you set is the timeout you think it is, are not answerable from the
option names. **MUST load `dotnet-configuration-resilience`** before you tune any of it.

⚠ **Step 5 (`POST /api/subscriptions`) — the idempotency hazard that outlives your own guard.** Your
pre-check (op 5c/5d) closes the double-click window in *your* app. It does not close the window inside
the SDK: whether a `CreateSubscription` that failed in transit can be put on the wire a second time by
the client itself — and whether any setting turns that off — decides whether a shopper can end up with
two subscriptions despite a correct pre-check. Establish that before you ship step 5.
**MUST load `dotnet-configuration-resilience`.**

⚠ **Steps 4–7 (every list/search call)** — ops 4, 5e, 7a and 7b have long runs of nullable-no-default
parameters that must be passed positionally in exact order or bound by name. How these mis-bind in a
positional call, and why named arguments are the norm here, is the calling-endpoints material.
**MUST load `dotnet-calling-endpoints`** before the first call.

⚠ **Steps 4–7 (models)** — `SubscriptionState`, `IntervalUnit`, `CollectionMethod` etc. are
`StringEnum<T>` records rather than C# enums, response records are `init`-only with `required` members,
and JSON keys the model does not declare are dropped silently on deserialize. How to compare, construct
and map these safely is the models material. **MUST load `dotnet-models`** before you build a request
body or map an SDK record onto an eShopOnWeb DTO.

⚠ **Step 3 (the error boundary)** — every operation here is throw-only (no `…Result` variants exist in
this SDK), the catch ladder has to cover two structurally different error cases, and `JsonException`
reaches the boundary from two directions that need opposite handling. **MUST load
`dotnet-error-handling`** before writing the boundary — and write the boundary *before* the endpoints,
not after.

⚠ **Tests** — the `HttpClient` constructor argument on `MaxioAdvancedBillingClient` is the only seam the
SDK gives you; which layer to fake and what to assert is the testing material.
**MUST load `dotnet-testing`** before stubbing the SDK.

---

## 4. REQUIRED READING

Load **all** of these **before implementation starts**. This sheet deliberately does **not** carry their
contents — it names the hazards, they carry the defaults, the worked examples, and the parts you must
still wire yourself.

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 2 — constructing/DI-registering `MaxioAdvancedBillingClient`, HttpClient ownership and lifetime |
| `dotnet-authentication` | Step 2 — supplying `BasicAuthCredentials` from configuration, rotation |
| `dotnet-configuration-resilience` | Step 2 — `options.Retry`, timeouts, base-URL override; **and step 5**, where retry behaviour bears on subscription idempotency; **and steps 4/5e/7b** pagination |
| `dotnet-calling-endpoints` | Steps 4–7 — the first call to each controller, nullable-no-default parameters, named arguments, cancellation |
| `dotnet-models` | Steps 4–7 — building `CreateCustomerRequest`/`CreateSubscriptionRequest`, `StringEnum<T>` comparison, mapping records to DTOs |
| `dotnet-error-handling` | Step 3 — the whole error boundary, both error cases, the `JsonException` rows below |
| `dotnet-testing` | Tests for the integration layer |

**Two `System.Text.Json.JsonException` hazard rows — it reaches the boundary from two directions and
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

Both rows have concrete instances in this integration, so they are not theoretical:
`ProductResponse.Product`, `CustomerResponse.Customer` and `ComponentResponse.Component` are `required`
(row 1); `ErrorListResponse1.Errors` is `required` and `CustomerErrorResponse1.Errors` is the suspicious
shared `Errors` record (row 2). Source: `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md`, `records-3-Of-Su.md`,
`Models/CustomerErrorResponse1.cs`, `Models/Errors.cs`.

---

## 5. Assumptions & Blockers

**Assumptions**

| # | Assumption |
|---|---|
| A1 | The three brief-supplied endpoint routes and their JWT protection are the application's design. This sheet describes only the Maxio calls behind them. |
| A2 | The plans in `Maxio:ProductFamilyHandle` really are configured no-trial / no-setup-fee / card-not-required, as the brief states. Ops 4/5f let you *verify* it at runtime from `Product.TrialInterval`, `Product.InitialChargeInCents`, `Product.RequireCreditCard`. |
| A3 | The Maxio sandbox site is US-hosted, so `ServerEnvironment.Us` applies. There is no sandbox-specific environment value in this SDK. |
| A4 | `Maxio:BaseUrl`, when set, is a complete origin with no `{site}` placeholder (that is what "verbatim" means given the template substitution in `Servers/ProductionOptions.cs`). If it *did* contain `{site}`, the SDK would substitute `Site` into it. |

**Facts this sheet cannot settle from map or source — labelled, with the defensive directive**

| # | Fact | Label & directive |
|---|---|---|
| U1 | Whether Maxio enforces uniqueness on a **subscription** `reference` the way its CreateCustomer Notes explicitly do for a **customer** `reference`. The Subscriptions Notes are silent. | `UNVERIFIED`. Do **not** rely on the provider rejecting a duplicate subscription reference. Keep the op-5c/5d pre-check, and treat a second `CreateSubscription` that succeeds where you expected a rejection as a real outcome to be reconciled, not an impossibility. |
| U2 | Whether a card-less `CreateSubscription` with `PaymentCollectionMethod` unset is accepted on your sandbox site, or whether the site default forces `automatic` and yields a 422. The operation's Notes say only that payment information "may be required… depending on the options for the Product". | `UNVERIFIED`. Send it unset first. On a 422, extract `ErrorListResponse1.Errors` best-effort and surface it verbatim to the operator rather than swallowing it — the message is the only signal telling you whether to set `CollectionMethod.Invoice`/`.Remittance`. |
| U3 | Whether a Maxio 422 body actually matches the generated `CustomerErrorResponse1`/`ErrorListResponse1` shapes (§2.7 shows why `CustomerErrorResponse1` is doubtful on the SDK's own evidence). | `UNVERIFIED`. Extract best-effort inside a guard; fall back to the generic message; never let a malformed error body decide the HTTP status you return. |
| U4 | The exact published NuGet version. The map's stamp (`v1.0.2`) and the `.csproj` at that tag (`<Version>1.0.0</Version>`) disagree. | `UNVERIFIED`. Pin an explicit `Version` on the `PackageReference` and record it; do not float. |

**Application decisions this sheet does not make**

| Decision | Where it belongs | Label |
|---|---|---|
| Which stable value from the logged-in identity becomes the customer `reference` | resolve from the app's own identity path | `YOUR CALL — not in the map` |
| The format of the deterministic subscription `Reference` (per user, or per user+plan) | your idempotency design | `YOUR CALL — not in the map` |
| Whether an existing subscription in a non-`Active` state (e.g. `PastDue`, `Trialing`, `Canceled`) blocks a new signup or is re-used | your product rules — the SDK only reports `Subscription.State` | `YOUR CALL — not in the map` |
| Whether Maxio results are cached or mirrored in the eShopOnWeb database, and any concurrency control around the double-click window | your persistence and concurrency design | `YOUR CALL — not in the map` |
| The HTTP status your endpoints return for each Maxio failure class | your API contract with your own callers | `YOUR CALL — not in the map` |

**Blockers:** none. Every operation the scope needs exists in the SDK, with two capability limits stated
in the sheet rather than worked around: `ListComponentsForProductFamily` requires a numeric family id
(no `handle:` form) and `ListProductFamilies` offers neither a handle filter nor pagination; and
`ListCustomerSubscriptions` offers no pagination, so `ListSubscriptions` + client-side filtering on
`Subscription.Customer?.Id` is the only pageable path.
