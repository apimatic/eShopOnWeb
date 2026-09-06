# Maxio Advanced Billing — integration plan & CONTRACT SHEET (eShopOnWeb)

Scope: additive recurring-subscription billing on `src/PublicApi`. Three JWT-authenticated endpoints.
Every SDK fact below is grounded in the bundled SDK map, or (where the row says so) in the named SDK
source file. Nothing here is from memory of this API.

---

## 1. Scope & sequence

| # | Step | Maxio operations used |
|---|---|---|
| 1 | Configuration binding + client registration (`Maxio:` section → `MaxioAdvancedBillingClientOptions`) | — (construction only) |
| 2 | Resolve the configured product family **by handle** → its products | `client.ProductFamilies.ListProductFamilies` → `client.ProductFamilies.ListProductsForProductFamily` |
| 3 | `GET /api/subscription-plans` — project products to plan DTOs; site currency | step 2 + `client.Sites.ReadSite` |
| 4 | `POST /api/subscriptions` — ensure customer by reference | `client.Customers.ReadCustomerByReference` → (on 404) `client.Customers.CreateCustomer` |
| 5 | `POST /api/subscriptions` — read existing subscriptions, decide, then create | `client.Customers.ListCustomerSubscriptions` → `client.Subscriptions.CreateSubscription` |
| 6 | `GET /api/my-subscriptions` — caller's subscriptions | `client.Customers.ReadCustomerByReference` → `client.Customers.ListCustomerSubscriptions` |
| 7 | Error boundary (all three endpoints) | — (see §4 error contract + REQUIRED READING) |
| 8 | *(optional, out of hero scope)* record metered usage for `api-call` | `client.SubscriptionComponents.CreateUsage` |
| 9 | Tests around the integration layer | — |

Plan-by-handle validation of a single plan (step 5) uses `client.Products.ReadProductByHandle`; it is the
cheapest "does this handle exist" check and returns the same `ProductResponse` shape as step 2.

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

### 2A. Package, namespaces, using-directives

| Item | Value | Source |
|---|---|---|
| NuGet package id | `AsadAli.AdvancedBilling.Sdk` | `sdk-map.md` |
| Version to reference | **`1.0.2`** — the map is stamped from commit `15db14b`, tagged `v1.0.2`; nuget.org publishes exactly `1.0.0`, `1.0.1`, `1.0.2` for this id. (The in-repo `MaxioAdvancedBilling.csproj` still says `<Version>1.0.0</Version>` at that tag — a stale field in the repo, not the published version. Pin `1.0.2`.) | `sdk-map.md` stamp + `MaxioAdvancedBilling.csproj` (SDK source) |
| Target framework | `netstandard2.0` — consumable from the eShopOnWeb `net*` projects | `sdk-map.md` |
| Transitive deps pulled in | `Polly`, `Microsoft.Extensions.Http`, `System.Net.Http.Json`, `System.Net.ServerSentEvents`, `PolySharp` (build-only) | `MaxioAdvancedBilling.csproj` (SDK source) |

`using` directives — one per namespace, **C# does not import child namespaces transitively**:

| Namespace | Types you will reference from it |
|---|---|
| `MaxioAdvancedBilling` | `MaxioAdvancedBillingClient`, `MaxioAdvancedBillingClientOptions`, `ServerOptions`, `ServiceCollectionExtensions` (`AddMaxioAdvancedBillingClient`) |
| `MaxioAdvancedBilling.Servers` | `ServerEnvironment`, `ProductionOptions` (+ nested `ProductionOptions.UsOptions` / `.EuOptions`), `EbbOptions` |
| `MaxioAdvancedBilling.Core.Authentication.Basic` | `BasicAuthCredentials` |
| `MaxioAdvancedBilling.Core.Configuration` | `RetryOptions` |
| `MaxioAdvancedBilling.Core.Exceptions` | `SdkException<TError>` |
| `MaxioAdvancedBilling.Core.ErrorResponse` | `RawError`, `ApiError` |
| `MaxioAdvancedBilling.Api` | controller types `Customers`, `Products`, `ProductFamilies`, `Subscriptions`, `Sites`, `SubscriptionComponents` (needed only if you type a local/field as the controller) |
| `MaxioAdvancedBilling.Models` | `Product`, `ProductResponse`, `ProductFamily`, `ProductFamilyResponse`, `Customer`, `CustomerResponse`, `CreateCustomer`, `CreateCustomerRequest`, `Subscription`, `SubscriptionResponse`, `CreateSubscription`, `CreateSubscriptionRequest`, `Site`, `SiteResponse`, `ErrorListResponse1`, `CustomerErrorResponse1`, `Errors`, `ListProductsFilter`, `CreateUsage`, `CreateUsageRequest`, `UsageResponse` |
| `MaxioAdvancedBilling.Models.Enums` | `SubscriptionState`, `IntervalUnit`, `ExpirationIntervalUnit`, `CollectionMethod`, `SubscriptionStateFilter`, `SortingDirection`, `BasicDateField`, `ListProductsInclude`, `PricePointType`, `CancellationMethod` |
| `MaxioAdvancedBilling.Models.AnyOf` | `SubscriptionIdOrReference`, `ComponentIdModel` (step 8 only) |
| `MaxioAdvancedBilling.Errors` | `CreateCustomerError`, `CreateSubscriptionError`, `ListProductsForProductFamilyError`, `CreateUsageError` |
| `MaxioAdvancedBilling.Core.Enum` | `StringEnum<T>` / `TypedEnum<TValue,TEnum>` — only if you write a generic helper over enum types |

*Source for the namespace table: `sdk-map.md` "Namespaces by content type" + each type's own map row / source path.*

### 2B. Client construction, auth, environment, base URL

`MaxioAdvancedBillingClientOptions` (namespace `MaxioAdvancedBilling`; source `MaxioAdvancedBillingClientOptions.cs`) — **all four properties, verbatim, with their real defaults**:

| Property | Type (fully qualified) | Default | Source |
|---|---|---|---|
| `Environment` | `MaxioAdvancedBilling.Servers.ServerEnvironment` | `ServerEnvironment.Default()` ⇒ `ServerEnvironment.Us` | `MaxioAdvancedBillingClientOptions.cs` |
| `Retry` | `MaxioAdvancedBilling.Core.Configuration.RetryOptions` | `RetryOptions.Default()` | `MaxioAdvancedBillingClientOptions.cs`, `sdk-map.md` |
| `Server` | `MaxioAdvancedBilling.ServerOptions` | `new()` | `MaxioAdvancedBillingClientOptions.cs` |
| `BasicAuth` | `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?` | `null` | `MaxioAdvancedBillingClientOptions.cs` |

**Auth — grounded, exact.** `BasicAuthCredentials` has exactly two members, both `required` + `init`:
`Username: string` and `Password: string`. The SDK's own XML doc on `MaxioAdvancedBillingClientOptions.BasicAuth`
reads: *"The `username` is a Maxio Chargify API key. The `password` is `x`."* `Encode()` produces
`Base64(Username + ":" + Password)`. So: **`Username` = `Maxio:ApiKey`, `Password` = the literal string `"x"`.**
Basic is the only auth scheme in this SDK. *(Source: `sdk-map.md` "Servers & auth"; `Core/Authentication/Basic/BasicAuthCredentials.cs`; `MaxioAdvancedBillingClientOptions.cs`.)*

**Constructor.** The only constructor is
`MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`.
*(Source: `sdk-map.md`; `MaxioAdvancedBillingClient.cs`.)*

**Base URL — the exact mechanism for both cases.** `ServerOptions.Production` is a `ProductionOptions`
with two nested option objects, `Us` and `Eu`; `options.Environment` selects **which one is read**
(`ProductionOptions.Resolve` matches `Us` ⇒ `Us.BaseUrl`/`Us.Site`, `Eu` ⇒ `Eu.BaseUrl`/`Eu.Site`).
Each has exactly two settable strings:

| Member | Default | Source |
|---|---|---|
| `options.Server.Production.Us.BaseUrl` | `"https://{site}.chargify.com"` | `Servers/ProductionOptions.cs` |
| `options.Server.Production.Us.Site` | `"subdomain"` | `Servers/ProductionOptions.cs` |
| `options.Server.Production.Eu.BaseUrl` | `"https://{site}.ebilling.maxio.com"` | `Servers/ProductionOptions.cs` |
| `options.Server.Production.Eu.Site` | `"subdomain"` | `Servers/ProductionOptions.cs` |

The final address is built by `TemplateParamsFactory.Create`:
`baseUrl = BaseUrl.Replace("{site}", Uri.EscapeDataString(Site))`, then
`$"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}"`. It is a plain `string.Replace` — **no validation,
no requirement that `{site}` appear at all**. Therefore:

- **Subdomain-derived (no `Maxio:BaseUrl` set):** leave `BaseUrl` at its default and set
  `options.Server.Production.Us.Site = <Maxio:Subdomain>`. For subdomain `cp-exp-2` that yields
  `https://cp-exp-2.chargify.com`.
- **Verbatim override (`Maxio:BaseUrl` set):** set
  `options.Server.Production.Us.BaseUrl = <Maxio:BaseUrl>` **exactly as configured**. Because the value
  contains no `{site}` placeholder, `Replace` is a no-op and the string is used **verbatim** (only a
  trailing `/` is trimmed before the path is appended). Setting `Site` alongside it is harmless and has no
  effect — but do **not** leave the `{site}` token in a value you intend to be verbatim, or it will be substituted.
- **No `Environment` enum value is involved in a custom base URL.** `ServerEnvironment` is a
  `StringEnum<ServerEnvironment>` with a **private constructor** and exactly two public members,
  `Us` (`"US"`) and `Eu` (`"EU"`), plus `Default()` ⇒ `Us`. There is **no `FromValue`, no `Custom`, no
  `Sandbox`** on it — a third value cannot be constructed. `Environment` only selects the `Us` vs `Eu`
  branch, so **whichever branch `Environment` selects is the branch your override must be written to.**
  With the default (`Us`), override `…Production.Us.BaseUrl`.
- The second server group, `options.Server.Ebb.*`, is used **only** by the `SubscriptionComponents`
  event-ingest endpoints. None of the operations in this plan (including `CreateUsage`, which is
  Production) touch it — leave it alone.

*(Source: `sdk-map.md` "Servers & auth"; `Server.cs`, `ServerOptions.cs`, `Servers/ProductionOptions.cs`, `Servers/ServerEnvironment.cs`, `Core/TemplateParamsFactory.cs`.)*

**DI registration.** `ServiceCollectionExtensions.AddMaxioAdvancedBillingClient(this IServiceCollection services, Action<MaxioAdvancedBillingClientOptions>? configure = null)`
(namespace `MaxioAdvancedBilling`; declared as a C# 14 `extension(IServiceCollection services)` member).
Facts you must plan around, read verbatim from `ServiceCollectionExtensions.cs`:

- it calls `services.AddHttpClient()` and registers `MaxioAdvancedBillingClient` as a **singleton**,
  resolving `IHttpClientFactory` and calling `CreateClient()` — the **default, unnamed** client;
- the `configure` callback runs **once, at registration time**, on a `new MaxioAdvancedBillingClientOptions()`
  — *not* per resolve. Consequence: the `Maxio:` values must already be readable from `IConfiguration` at
  `ConfigureServices` time, no scoped service can be consulted inside the callback, and the options object is
  captured for the process lifetime (config reload will not re-read it).
- Alternative, if you want a **named** `HttpClient` (own handler lifetime, own `HttpClient.Timeout`, own
  DelegatingHandlers) or non-singleton lifetime: register your own factory that news up
  `new MaxioAdvancedBillingClient(httpClientFactory.CreateClient("maxio"), options)`. Whether you take the
  built-in helper or your own registration is an ownership/lifetime decision — ⚠ see trap note T1.

### 2C. Operations table

Read every cell as literal C# unless the cell says otherwise. `ct` is always the last parameter.

| # | Controller property | Method signature (verbatim) | Request model + fields | Response envelope + inner fields read | Error case + accessors + payload type | Pagination | Source |
|---|---|---|---|---|---|---|---|
| O1 | `client.ProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 filter params are nullable **with no default → pass `null` explicitly** | none (GET) | `IReadOnlyList<ProductFamilyResponse>`; `ProductFamilyResponse.ProductFamily (product_family): ProductFamily?` → `ProductFamily.Id (id): int?`, `.Handle (handle): string?`, `.Name (name): string?` | **Case B** — `SdkException<RawError>`; `.Error.StatusCode`, `.Error.ReadAsString()`, `.Error.ReadAsJson<T>()`, `.Error.ReadAsBytes()` | **none** — returns every family on the site in one call | `operations/ProductFamilies.md`, `models/records-3-Of-Su.md` |
| O2 | `client.ProductFamilies` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 8 params `dateField`…`include` are nullable **with no default → pass `null`**; `page`/`perPage` default to `1`/`20` | none (GET). `productFamilyId` is a **path** param — the SDK's own XML doc for it reads: *"Either the product family's id or its handle prefixed with `handle:`"* | `IReadOnlyList<ProductResponse>`; `ProductResponse.Product (product): Product` **`required`** → see O3 field list | **Case A** — `SdkException<ListProductsForProductFamilyError>`; `TryGetString(out string)` **[404]** · `TryGetRawError(out RawError)` **[every other status]**. The two are mutually exclusive (see §4). | manual `page` + `perPage`; wire `page` / `per_page`. `perPage` max is **200** (the SDK's XML doc: any value over 200 is coerced to 200) | `operations/ProductFamilies.md`, `models/records-3-Of-Su.md`; `Api/ProductFamilies.cs`, `Errors/ListProductsForProductFamilyError.cs` |
| O3 | `client.Products` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | none (GET); `apiHandle` = the bare handle, e.g. `"eshop-pro"` (path `GET /products/handle/{api_handle}.json`) | `ProductResponse` → `.Product (product): Product` **`required`**. Fields this integration reads: `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `RequireCreditCard (require_credit_card): bool?`, `RequestCreditCard (request_credit_card): bool?`, `Taxable (taxable): bool?`, `TrialPriceInCents (trial_price_in_cents): long?`, `TrialInterval (trial_interval): int?`, `ExpirationInterval (expiration_interval): int?`, `ExpirationIntervalUnit (expiration_interval_unit): ExpirationIntervalUnit?`, `ArchivedAt (archived_at): DateTimeOffset?`, `ProductFamily (product_family): ProductFamily?`, `ProductPricePointId (product_price_point_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?`, `ProductPricePointName (product_price_point_name): string?`, `DefaultProductPricePointId (default_product_price_point_id): int?` | **Case B** — `SdkException<RawError>`; `.Error.StatusCode` (a bad handle ⇒ 404), `.Error.ReadAsString()` | none | `operations/Products.md`, `models/records-3-Of-Su.md` |
| O4 | `client.Sites` | `ReadSite(CancellationToken ct = default)` | none (GET) | `SiteResponse` → `.Site (site): Site` **`required`** → `Site.Currency (currency): string?` (the site's primary currency), `Site.Subdomain (subdomain): string?`, `Site.NonPrimaryCurrencies (non_primary_currencies): IReadOnlyList<string>?`, `Site.Test (test): bool?`, **`Site.RelationshipInvoicingEnabled (relationship_invoicing_enabled): bool?`** (selects which `CollectionMethod` values are valid — see §2D), **`Site.DefaultPaymentCollectionMethod (default_payment_collection_method): string?`** (⚠ a raw `string?`, **not** a `CollectionMethod` — compare against `CollectionMethod.Remittance.Value`, never against the enum object), **`Site.NetTerms (net_terms): NetTerms?`** → `NetTerms.DefaultNetTerms (default_net_terms): int? = 0`, `.AutomaticNetTerms (automatic_net_terms): int? = 0`, `.RemittanceNetTerms (remittance_net_terms): int? = 0`, `.NetTermsOnRemittanceSignupsEnabled (net_terms_on_remittance_signups_enabled): bool? = false`, `.CustomNetTermsEnabled (custom_net_terms_enabled): bool? = false` | **Case B** — `SdkException<RawError>` | none | `operations/Sites.md`, `models/records-3-Of-Su.md`, `models/records-2-Cr-Ne.md` |
| O5 | `client.Customers` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | none (GET); `reference` is a **query** param (`GET /customers/lookup.json?reference=…`). Notes: *"Returns a customer by their unique reference ID. It will return a single match."* | `CustomerResponse` → `.Customer (customer): Customer` **`required`** → `Customer.Id (id): int?`, `.Reference (reference): string?`, `.Email (email): string?`, `.FirstName (first_name): string?`, `.LastName (last_name): string?`, `.CreatedAt (created_at): DateTimeOffset?` | **Case B** — `SdkException<RawError>`; **`.Error.StatusCode == HttpStatusCode.NotFound` is the "no such reference" signal** — see §4 | none | `operations/Customers.md`, `models/records-1-Ac-Cr.md`; `Api/Customers.cs` |
| O6 | `client.Customers` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` is nullable **with no default → pass it explicitly** | `CreateCustomerRequest` → `Customer (customer): CreateCustomer` **`required`**. `CreateCustomer`: **`FirstName (first_name): string` required**, **`LastName (last_name): string` required**, **`Email (email): string` required**; then all optional — `Reference (reference): string?` ← **set this to the eShopOnWeb stable id**, `CcEmails`, `Organization`, `Address`, `Address2`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `VatNumber`, `TaxExempt: bool?`, `TaxExemptReason`, `ParentId: int?`, `SalesforceId` | `CustomerResponse` → `.Customer` **`required`** (fields as O5) | **Case A** — `SdkException<CreateCustomerError>`; `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` **[422]** · `TryGetRawError(out RawError)` **[other]**. `CustomerErrorResponse1.Errors (errors): Errors?` → `Errors.PerPage (per_page): IReadOnlyList<string>?`, `Errors.PricePoint (price_point): IReadOnlyList<string>?` — ⚠ note this payload shape carries **no general message list**; see §4 | none | `operations/Customers.md`, `models/records-1-Ac-Cr.md`, `models/records-2-Cr-Ne.md` |
| O7 | `client.Customers` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | none (GET). Notes: *"Lists all subscriptions that belong to a customer."* | `IReadOnlyList<SubscriptionResponse>`; `SubscriptionResponse.Subscription (subscription): Subscription?` — **nullable, NOT `required`** ⇒ null-check each element | **Case B** — `SdkException<RawError>` | **none — no `page`/`perPage` parameters exist on this operation**; it returns the customer's whole set in one call | `operations/Customers.md`, `models/records-4-Su-We.md`; `Api/Customers.cs` |
| O8 | `client.Subscriptions` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable **with no default → pass explicitly** | `CreateSubscriptionRequest` → `Subscription (subscription): CreateSubscription` **`required`**. See the field selection in §2D — **`CreateSubscription` marks nothing `required`**, so the compiler will not stop you from posting an empty body | `SubscriptionResponse` → `.Subscription (subscription): Subscription?` — **nullable** ⇒ null-check before reading | **Case A** — `SdkException<CreateSubscriptionError>`; `TryGetErrorListResponse1(out ErrorListResponse1)` **[422]** · `TryGetRawError(out RawError)` **[other]**. `ErrorListResponse1.Errors (errors): IReadOnlyList<string>` **`required`** — the human-readable validation messages | none | `operations/Subscriptions.md`, `models/records-2-Cr-Ne.md`, `models/records-4-Su-We.md`; `Errors/CreateSubscriptionError.cs` |
| O9 | `client.Subscriptions` | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string,string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 14 nullable params with no default → pass `null` | none (GET) | `IReadOnlyList<SubscriptionResponse>` | **Case B** — `SdkException<RawError>` | manual `page` + `perPage` | `operations/Subscriptions.md` |
| O10 | `client.SubscriptionComponents` | *(optional, step 8)* `CreateUsage(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, CreateUsageRequest? body, CancellationToken ct = default)` — `body` nullable with no default → pass explicitly. `SubscriptionIdOrReference` is an AnyOf union: factories `SubscriptionIdOrReference.Int(int)` / `.String(string)`, implicit from `int`/`string`. `ComponentIdModel` likewise: `.Int(int)` / `.String(string)` — the Notes say the handle form is `"handle:api-call"` | `CreateUsageRequest` → `Usage (usage): CreateUsage` **`required`**. `CreateUsage`: `Quantity (quantity): double?`, `PricePointId (price_point_id): string?`, `Memo (memo): string?`, `BillingSchedule`, `CustomPrice` — all optional | `UsageResponse` → `.Usage (usage): Usage` **`required`** → `Usage.Id (id): long?`, `.Quantity (quantity): Quantity1?` **(union)**, `.ComponentHandle (component_handle): string?`, `.SubscriptionId (subscription_id): int?` | **Case A** — `SdkException<CreateUsageError>`; `TryGetErrorListResponse1(out ErrorListResponse1)` **[422]** · `TryGetRawError(out RawError)` **[other]** | none | `operations/SubscriptionComponents.md`, `models/records-1-Ac-Cr.md`, `models/records-4-Su-We.md`, `models/unions.md` |

**Family handle → products: the full path.** `ReadProductFamily` takes `int id` and therefore **cannot**
accept a handle, despite its prose mentioning the `handle:my-family` form. Two grounded routes:

- **Route A (recommended, no encoding risk):** `O1 ListProductFamilies(null, null, null, null, null, ct: ct)`
  → first element whose `.ProductFamily?.Handle == <Maxio:ProductFamilyHandle>` → take `.ProductFamily.Id`
  → `O2 ListProductsForProductFamily(id.Value.ToString(CultureInfo.InvariantCulture), null, null, null, null, null, null, includeArchived: false, null, page: 1, perPage: 200, ct: ct)`.
  O1 is unpaginated, so one call sees every family.
- **Route B (one call):** `O2 ListProductsForProductFamily("handle:" + <Maxio:ProductFamilyHandle>, …)`.
  The SDK's XML doc for `productFamilyId` explicitly sanctions the `handle:` prefix. **`UNVERIFIED`:** path
  template params are expanded through `Uri.EscapeDataString`, so the value is sent as
  `handle%3Aeshop-subscribe`; whether the provider decodes `%3A` back to `:` when routing can only be
  confirmed against live traffic. **Defensive directive:** if you take Route B, catch
  `SdkException<ListProductsForProductFamilyError>`, and when `TryGetString(out _)` is true (404) **fall
  back to Route A** rather than surfacing "family not found" to the caller.

### 2D. `CreateSubscription` — the field selection (⚠ nothing is `required`)

`CreateSubscription` marks **no** member `required`, so the `required?` column selects nothing for you and
no compiler error will catch a dropped field. From the operation's Notes verbatim: *"Specify the product with
`product_id` or `product_handle`. … Identify an existing customer with `customer_id` or `customer_reference`.
… To create a new customer, pass customer_attributes."* Accordingly:

| Set | C# member (wire name) : type | Why |
|---|---|---|
| ✅ | `ProductHandle (product_handle): string?` | Notes-named product identifier. Set to the requested plan handle (`eshop-pro` / `basic-plan`). Resolves by handle — satisfies the "never hard-code numeric ids" rule. |
| ✅ | `CustomerId (customer_id): int?` | Notes-named customer identifier. Set to the `Customer.Id` from O5/O6 — the id you just resolved, so it is exact. |
| ⬜ alternative | `CustomerReference (customer_reference): string?` | The other Notes-named customer identifier. Use **either** `CustomerId` **or** this, not both. Preferring `CustomerId` keeps the ensure-customer step and the create step consistent. |
| ⬜ | `Reference (reference): string?` | The subscription's **own** reference (SDK XML doc: *"The reference value (provided by your app) for the subscription itself."*). Setting it gives you a stable client-owned key you can later look up with `Subscriptions.FindSubscription(reference, ct: ct)` (Case A; `TryGetNoContent(out RawError)` [404]). **`UNVERIFIED`:** the map records no uniqueness constraint on subscription `reference` (unlike customer `reference`, whose uniqueness the CreateCustomer Notes state outright), so do **not** treat it as provider-side dedupe — see §5. |
| ❌ | `Ref (ref): string?` | **Not** the subscription reference — its XML doc says it is a **referral code**, and *"if supplied, must be valid, or else subscription creation will fail."* Leave null. |
| ❌ | `ProductId`, `ProductPricePointId`, `ProductPricePointHandle` | Not needed: with no price point specified, the product's default price point applies. `Product.DefaultProductPricePointId` is available on O3 if you ever need to display it. |
| ❌ | `PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes` | No card in this flow. |
| ❌ | `CustomerAttributes (customer_attributes): CustomerAttributes?` | This is the *create-a-new-customer-inline* path. **Do not set it** in the hero flow — it bypasses your idempotent ensure-customer step. |
| ❌ | `CouponCode`, `CouponCodes`, `OfferId`, `Components`, `CalendarBilling`, `Group`, `Metafields`, `PrepaidConfiguration`, `CustomPrice` | Out of scope. |
| ❌ | `DeferSignup (defer_signup): bool? = false` | Generated default `false` — leave it. Setting `true` would not activate the subscription. |
| ✅ **(corrected 2026-09-06 — was ❌)** | `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` | **This is the mechanism for "a balance is due at signup and there is no payment method on file".** Set it to `CollectionMethod.Remittance` (wire `"remittance"`) on a Relationship-Invoicing site, or `CollectionMethod.Invoice` (wire `"invoice"`) on a legacy Statements-Architecture site — the enum's own doc fixes which set is valid per architecture, and `Site.RelationshipInvoicingEnabled` tells you which you are on. Leaving it unset means the site default applies, which on this site is card-collection and produces the 422. See the corrected paragraph below. |

**"What to set so no payment method is required" — CORRECTED 2026-09-06 after a live 422.**
*(Superseded text: this paragraph previously said "the grounded answer: nothing" and told you to leave
`PaymentCollectionMethod` unset. That was wrong and is retracted. Live wire:
`POST /subscriptions.json` with only `ProductHandle` + `CustomerId` → **422**,
`ErrorListResponse1.Errors = ["No payment method was on file for the $299.00 balance"]`.)*

Two distinct things were conflated. `Product.RequireCreditCard`'s own XML doc reads: *"Boolean that
controls whether a **payment profile is required to be entered for customers wishing to sign up** on this
product."* That governs whether a profile must be **entered at signup** — it does **not** say the platform
will waive **collecting a balance that is due**. A $299.00 first-period balance is assessed at creation
regardless, and with no profile and a card-collection method there is nothing to charge it against. Hence
the 422 despite `require_credit_card` being false/null.

The grounded lever is **`CreateSubscription.PaymentCollectionMethod (payment_collection_method): CollectionMethod?`**
(namespace of the enum: `MaxioAdvancedBilling.Models.Enums`). Its XML doc — identical on the member and on
the enum type — reads verbatim: *"The type of payment collection to be used in the subscription. For legacy
Statements Architecture valid options are `invoice`, `automatic`. For current Relationship Invoicing
Architecture valid options are `remittance`, `automatic`, `prepaid`."* `automatic` is the card-charging
method; the non-card methods are `remittance` (Relationship Invoicing) and `invoice` (legacy Statements).
**Choose by architecture, do not hard-code:** `client.Sites.ReadSite(ct: ct)` →
`SiteResponse.Site.RelationshipInvoicingEnabled (relationship_invoicing_enabled): bool?` → `true` ⇒
`CollectionMethod.Remittance`, `false`/null ⇒ `CollectionMethod.Invoice`. Log
`Site.DefaultPaymentCollectionMethod (default_payment_collection_method): string?` alongside it — that is
the site default your unset field was falling through to.

**A second, independent grounded mechanism** that removes the signup balance entirely, from
`CreateSubscription.NextBillingAt`'s XML doc verbatim: *"If you provide a next_billing_at timestamp that is
in the future, no trial or initial charges will be applied when you create the subscription. In fact, no
payment will be captured at all. … If you do not provide a value for next_billing_at, any trial and/or
initial charges will be assessed and charged at the time of subscription creation."* This is a
subscription-import mechanism, not a normal signup path, and it moves the first charge — prefer the
collection-method fix and treat this only as the fallback if collection method is rejected.

**Defensive directive:** carry `Product.RequireCreditCard` into the plan DTO for visibility, but do **not**
treat `false` as "no balance will be collected" — it does not mean that. Do **not** branch on
`Product.RequestCreditCard`: its XML doc says *"Deprecated value that can be ignored unless you have legacy
hosted pages."* When the subscribe call still 422s, surface `ErrorListResponse1.Errors` verbatim to logs —
that message is the only place the amount and the reason appear.

### 2D-bis. EVERY `CreateSubscription` member bearing on payment collection / balance-due-at-signup / invoicing

*(Added 2026-09-06.)* Complete list — nothing in `CreateSubscription` that touches this area is omitted, so
you are choosing from the real surface. All are optional. Namespace `MaxioAdvancedBilling.Models`.

| C# member (wire name) : type | What the SDK's own doc says | Verdict for this flow |
|---|---|---|
| `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` | *"The type of payment collection to be used in the subscription. For legacy Statements Architecture valid options are `invoice`, `automatic`. For current Relationship Invoicing Architecture valid options are `remittance`, `automatic`, `prepaid`."* | **THE fix.** `Remittance` (RI) / `Invoice` (legacy). |
| `NextBillingAt (next_billing_at): DateTimeOffset?` | *"Set this attribute to a future date/time to sync imported subscriptions to your existing renewal schedule. **If you provide a next_billing_at timestamp that is in the future, no trial or initial charges will be applied when you create the subscription. In fact, no payment will be captured at all.** The first payment will be captured … near the time specified by next_billing_at. **If you do not provide a value for next_billing_at, any trial and/or initial charges will be assessed and charged at the time of subscription creation. If the card cannot be successfully charged, the subscription will not be created.**"* | Independent second mechanism — removes the signup balance. Import-shaped; fallback only. Explains **why** the unset call charged at creation. |
| `InitialBillingAt (initial_billing_at): DateTimeOffset?` | *"Set this attribute to a future date/time to create a subscription in the **Awaiting Signup** state, rather than Active or Trialing. … **If the payment is due at the initial_billing_at and it fails the subscription will be immediately canceled.**"* | Defers the problem, does not remove it. Not recommended. |
| `DeferSignup (defer_signup): bool? = false` | *"Set this attribute to true to create the subscription in the **Awaiting Signup Date** state. Use this when you want to create a subscription that has an unknown first billing date."* | No — the shopper would not be subscribed. |
| `NetTerms (net_terms): string?` | *"(Optional) Default: null The number of days after renewal (**on invoice billing**) that a subscription is due. A value between 0 (due immediately) and 180."* | Only meaningful **once** a non-card collection method is set. ⚠ note the type is `string?` here even though it is a day count. |
| `ReceivesInvoiceEmails (receives_invoice_emails): string?` | *"(Optional) Default: True — Whether or not this subscription is set to receive emails related to this subscription."* | Optional. ⚠ `string?`, not `bool?`. Consider `"false"` if you do not want Maxio emailing eShopOnWeb shoppers. |
| `CalendarBilling (calendar_billing): CalendarBilling?` | *"(Optional). Cannot be used when also specifying next_billing_at"* | Out of scope; mutually exclusive with `NextBillingAt`. |
| `CalendarBillingFirstCharge (calendar_billing_first_charge): string?` | *"(Optional) One of "prorated" (the default – the prorated product price will be charged immediately), "immediate" (the full product price will be charged immediately), or "**delayed**" (the full product price will be charged with the first scheduled renewal)."* | Only with calendar billing. `"delayed"` would also defer the charge, but only on that path. |
| `PaymentProfileId (payment_profile_id): int?` | *"The Payment Profile ID of an existing card or bank account … If … you want to use a new (unstored) card or bank account, use `payment_profile_attributes` instead."* | The card path — explicitly not this flow. |
| `PaymentProfileAttributes` / `CreditCardAttributes` : `PaymentProfileAttributes?` · `BankAccountAttributes: BankAccountAttributes?` | create a new payment profile with the subscription | The card path — not this flow (and PCI-relevant). |
| `AgreementTerms (agreement_terms): string?` | *"(Optional) The **ACH authorization agreement terms**. If enabled, an email will be sent to the customer with a copy of the terms."* | ACH/bank-account only. |
| `AuthorizerFirstName (authorizer_first_name): string?` · `AuthorizerLastName (authorizer_last_name): string?` · `AchAgreement (ach_agreement): AchAgreement?` · `AgreementAcceptance (agreement_acceptance): AgreementAcceptance?` | ACH / agreement acceptance metadata | ACH/bank-account only. |
| `DunningCommunicationDelayEnabled (dunning_communication_delay_enabled): bool? = false` · `DunningCommunicationDelayTimeZone (dunning_communication_delay_time_zone): string?` | *"Enable Communication Delay feature, making sure no communication (email or SMS) is sent to the Customer between 9PM and 8AM in time zone set by …"* | Dunning **quiet hours only** — does not affect whether a balance is collectible. |
| `SkipBillingManifestTaxes (skip_billing_manifest_taxes): bool?` | *"**Valid only for the Subscription Preview endpoint.** When set to `true` it skips calculating taxes …"* | Ignored by `CreateSubscription` — preview-only. |
| `CustomPrice (custom_price): SubscriptionCustomPrice?` | *"(Optional) Used in place of `product_price_point_id` to define a custom price point unique to the subscription."* | Could set the signup price to 0, but that changes what the shopper is billed — a pricing decision, not a fix. `YOUR CALL — not in the map`. |
| `PreviousBillingAt (previous_billing_at): DateTimeOffset?` · `ImportMrr (import_mrr): bool?` · `ActivatedAt`, `CanceledAt : DateTimeOffset?` | import/backfill attributes; `previous_billing_at` *"Can only be used if next_billing_at is also passed"*; `import_mrr` *"For this value to be honored, a next_billing_at must be present and set to a future date"* | Import-only. |
| `ExpiresAt (expires_at): DateTimeOffset?` · `ExpirationTracksNextBillingChange (expiration_tracks_next_billing_change): string?` | expiration handling | Unrelated to collection. |
| `CustomerAttributes (customer_attributes): CustomerAttributes?` | inline customer creation | **Carries no payment-collection field** (see §2D-ter) and bypasses your idempotent ensure-customer step. Still ❌. |
| `Currency (currency): string?` · `CouponCode` / `CouponCodes` · `OfferId` · `Components` · `Group` · `Metafields` · `PrepaidConfiguration` · `SalesRepId` · `StoredCredentialTransactionId` · `ReasonCode` · `CancellationMessage` / `CancellationMethod` · `ProductChangeDelayed` · `Reference` / `Ref` | — | No bearing on payment collection. |

**There is no "generate invoice", "skip payment", "no payment required" or "bill later" boolean on
`CreateSubscription`.** The collection method *is* the surface for this. *(Source: `models/records-2-Cr-Ne.md` for the full field list; `Models/CreateSubscription.cs` for every quoted doc; `models/enums.md` + `Models/Enums/CollectionMethod.cs` for the enum.)*

### 2D-ter. Customer-side and site-side collection settings

**Customer side — nothing exists.** *(Added 2026-09-06.)* Across `CreateCustomer`, `Customer`,
`UpdateCustomer`, `UpdateCustomerRequest` and `CustomerAttributes` there is **no** payment-collection,
default-collection-method, net-terms or invoicing member of any kind — the closest members are
`TaxExempt (tax_exempt): bool?`, `VatNumber`, `ParentId (parent_id): int?` and
`DefaultAutoRenewalProfileId (default_auto_renewal_profile_id): int?` (a *payment profile* id, not a
method). Sweeping every record page for the `payment_collection_method` wire name returns exactly
`CreateSubscription`, `UpdateSubscription`, `Subscription`, `Site` (as `default_payment_collection_method`),
`AllocateComponents`, `SubscriptionGroup`, `SubscriptionGroupSignup`, `SubscriptionGroupSignupResponse`,
`SubscriptionGroupSignupFailureData` — **no customer record among them.** So you cannot make a payer
"inherit remittance"; it must be set **per subscription** on every `CreateSubscription` call.
*(Source: `models/records-1-Ac-Cr.md`, `models/records-4-Su-We.md`, `operations/Customers.md`.)*

**Site side — readable, not writable.** `Site.DefaultPaymentCollectionMethod (default_payment_collection_method): string?`
is the site default (a raw `string?`, not the enum), and `Site.RelationshipInvoicingEnabled (relationship_invoicing_enabled): bool?`
tells you which `CollectionMethod` value set is legal. Read both via O4 and **log/branch on them rather than
hard-coding** `Remittance` vs `Invoice`. The three `Sites` operations are `ReadSite`,
`ListChargifyJsPublicKeys` and `ClearSite` — **there is no operation that writes site settings**, so the
default cannot be changed through this SDK. *(Source: `operations/Sites.md`, `models/records-3-Of-Su.md`.)*

⚠ **Trust note, from evidence visible in the generated code — not from memory of this API.** The same
`payment_collection_method` wire field is generated with **three different C# types** across the SDK:
`CollectionMethod?` on `CreateSubscription`, plain **`string?`** on `UpdateSubscription`, and plain
**`string?`** on `Site.default_payment_collection_method`. Two generated definitions of one wire field that
disagree means the enum is not a reliable statement of the field's full value domain. Treat
`CollectionMethod`'s four members as the *documented* set, keep the `IsKnownValue()` else-branch when
reading one back, and never compare a `Site.DefaultPaymentCollectionMethod` string to a `CollectionMethod`
object — compare to `.Value`.

### 2E. Reading a subscription back (`Subscription`, namespace `MaxioAdvancedBilling.Models`)

Members this integration reads, with wire names and exact CLR types — **every one is nullable**:

| C# member (wire name) | Type | Use |
|---|---|---|
| `Id (id)` | `int?` | subscription id |
| `State (state)` | `SubscriptionState?` | current state — render via `.Value` (see 2F) |
| `PreviousState (previous_state)` | `SubscriptionState?` | — |
| `NextAssessmentAt (next_assessment_at)` | **`DateTimeOffset?`** | **the "next billing date"** to show the user |
| `CurrentPeriodEndsAt (current_period_ends_at)` | `DateTimeOffset?` | period end; the `UpdateSubscription` Notes state that after a billing-date change it is `current_period_ends_at` — not `next_billing_at` — that reflects the new date, so treat it as the corroborating field |
| `CurrentPeriodStartedAt (current_period_started_at)` | `DateTimeOffset?` | — |
| `ActivatedAt (activated_at)` | `DateTimeOffset?` | — |
| `CanceledAt (canceled_at)` · `ExpiresAt (expires_at)` · `DelayedCancelAt (delayed_cancel_at)` · `ScheduledCancellationAt (scheduled_cancellation_at)` · `TrialEndedAt (trial_ended_at)` · `OnHoldAt (on_hold_at)` | `DateTimeOffset?` | lifecycle timestamps |
| `CancelAtEndOfPeriod (cancel_at_end_of_period)` | `bool?` | — |
| `ProductPriceInCents (product_price_in_cents)` | **`long?`** | the subscription's plan price, in **cents** |
| `CurrentBillingAmountInCents (current_billing_amount_in_cents)` | `long?` | next charge amount, in cents |
| `Currency (currency)` | `string?` | subscription currency (present here even though `Product` has none) |
| `Product (product)` | **`Product?`** | the **same** `Product` record as O3 ⇒ `Subscription.Product?.Handle`, `?.Name`, `?.PriceInCents`, `?.Interval`, `?.IntervalUnit` give the subscription's plan handle/name/price/interval |
| `Customer (customer)` | `Customer?` | `?.Id`, `?.Reference` |
| `Reference (reference)` | `string?` | your app's subscription reference, if you set it |
| `PaymentCollectionMethod (payment_collection_method)` | `CollectionMethod?` | — |
| `ProductPricePointId (product_price_point_id)` | `int?` · `ProductPricePointType (product_price_point_type)`: `PricePointType?` | — |
| `CancellationMethod (cancellation_method)` | `CancellationMethod?` | — |

*Source: `models/records-4-Su-We.md`, `models/records-3-Of-Su.md`, `operations/Subscriptions.md`.*

> ⚠ **Envelope asymmetry — this is real and map-visible, not a guess.** `ProductResponse.Product`,
> `CustomerResponse.Customer`, `SiteResponse.Site`, `UsageResponse.Usage` and `ErrorListResponse1.Errors`
> are C# **`required`**; `SubscriptionResponse.Subscription` and `ProductFamilyResponse.ProductFamily` are
> **not**. Two consequences, opposite in direction: a `SubscriptionResponse` element can legitimately
> deserialize with a `null` payload (null-check it), whereas a `ProductResponse` whose body lacks
> `"product"` throws a `System.Text.Json.JsonException` out of deserialization instead of an
> `SdkException` — see the REQUIRED READING hazard rows.
>
> ⚠ **Shared-model caution.** `Subscription.Product` is the very same generated `Product` record returned
> by `Products.ReadProductByHandle`, and every one of its ~37 members is nullable. The generated type
> therefore cannot tell you which subset the subscription endpoint actually populates. **`UNVERIFIED`:**
> only live traffic can confirm that a nested `product` on a subscription carries `handle`, `name`,
> `price_in_cents` and `interval_unit`. **Defensive directive:** read them best-effort with `?.` and fall
> back — prefer `Subscription.ProductPriceInCents` for price, and if `Subscription.Product?.Handle` is null,
> resolve the plan from your own record of what the caller subscribed to, or re-read it via O3; never render
> an empty plan name to the user.

### 2F. Enums (namespace `MaxioAdvancedBilling.Models.Enums`) — and how to render them

These are **`StringEnum<T>` records, not C# enums.** Confirmed members on the base type
`MaxioAdvancedBilling.Core.Enum.TypedEnum<TValue,TEnum>` (source `Core/Enum/TypedEnum.cs`):

- **`Value`** — `public TValue Value { get; init; }`; for a `StringEnum<T>` this is the **wire string**.
  `subscription.State?.Value` ⇒ `"active"`. **This is how you render state to the user.**
- **`ToString()`** — overridden to return `Value.ToString()`, so interpolation prints the wire value too.
- **implicit conversion to the underlying value** — a `SubscriptionState` assigns straight to a `string`.
- **`IsKnownValue()`** — `bool`; false when the wire sent a value that is not one of the generated members.
  Deserialization **accepts unknown values** (`FromValueCore` reflects a new instance rather than throwing),
  so a future provider state will arrive as a live object with `IsKnownValue() == false`.
- **`FromValue(string)`** — per-enum static (e.g. `SubscriptionState.FromValue("active")`); use the named
  members in code, `FromValue` only when mapping a caller-supplied string.
- **`GetKnownValues()`** — `IReadOnlyCollection<TEnum>` of the generated members.
- Equality is record equality over `Value`; known-value lookup is `OrdinalIgnoreCase`.

| Enum | Members (`CSharpName (wire)`) | Source |
|---|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` | `models/enums.md` |
| `IntervalUnit` | `Day (day)`, `Month (month)` — **only two**; there is no `Year` | `models/enums.md` |
| `ExpirationIntervalUnit` | `Day (day)`, `Month (month)`, `Never (never)` | `models/enums.md` |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` | `models/enums.md` |
| `SubscriptionStateFilter` (O9 `state` filter only — **a different type from `SubscriptionState`**) | `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)` | `models/enums.md` |
| `PricePointType` | `Catalog (catalog)`, `Default (default)`, `Custom (custom)` | `models/enums.md` |
| `CancellationMethod` | `MerchantUi (merchant_ui)`, `MerchantApi (merchant_api)`, `Dunning (dunning)`, `BillingPortal (billing_portal)`, `Unknown (unknown)`, `Imported (imported)` | `models/enums.md` |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` | `models/enums.md` |
| `SortingDirection` | `Asc (asc)`, `Desc (desc)` | `models/enums.md` |
| `ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)` | `models/enums.md` |
| `SubscriptionListInclude` | `SelfServicePageToken (self_service_page_token)` | `models/enums.md` |
| `SubscriptionInclude` | `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)` | `models/enums.md` |
| `SubscriptionSort` | `SignupDate (signup_date)`, `PeriodStart (period_start)`, `PeriodEnd (period_end)`, `NextAssessment (next_assessment)`, `UpdatedAt (updated_at)`, `CreatedAt (created_at)`, `TotalPayments (total_payments)`, `Id (id)`, `OpenBalance (open_balance)`, `ExpiresAt (expires_at)` | `models/enums.md` |
| `ServerEnvironment` (namespace `MaxioAdvancedBilling.Servers`) | `Us (US)`, `Eu (EU)` — **and no others; private ctor, no `FromValue`** | `sdk-map.md`, `Servers/ServerEnvironment.cs` |

**`CollectionMethod` — which member to use (added 2026-09-06).** Exact members, from
`Models/Enums/CollectionMethod.cs`: `CollectionMethod.Automatic` → `"automatic"`,
`CollectionMethod.Remittance` → `"remittance"`, `CollectionMethod.Prepaid` → `"prepaid"`,
`CollectionMethod.Invoice` → `"invoice"`; plus `CollectionMethod.FromValue(string)`. The type's XML doc
partitions them by architecture verbatim: *"For legacy Statements Architecture valid options are `invoice`,
`automatic`. For current Relationship Invoicing Architecture valid options are `remittance`, `automatic`,
`prepaid`."* `Automatic` is the card-charging method that produced the 422; `Prepaid` requires a prepayment
balance (and `SubscriptionState.Suspended` means *"a prepaid subscription has used up all their prepayment
balance"*), so the non-card, invoice-the-customer choice is **`Remittance` when
`Site.RelationshipInvoicingEnabled == true`, otherwise `Invoice`**.

**Expected state after the fix — `UNVERIFIED`.** The enum's doc defines `Active` as *"A normal, active
subscription. It is not in a trial and is **paid and up to date**"* and `PastDue` as *"the most recent
payment has failed, and payment is past due"*. Neither description is written for an
issued-but-unpaid invoice, and the map does not state which state a remittance/invoice signup lands in —
only live traffic can settle it. **Defensive directive:** do **not** assert `State == Active` before
returning success from `POST /api/subscriptions`. Treat a non-null `SubscriptionResponse.Subscription` with
a non-null `Id` as the success signal, render `State?.Value` through to the caller as-is, and log the state
you actually observed so the acceptance rule can be tightened from evidence rather than assumption.

**Live-state rule for "is the caller already subscribed to this plan":** the states that mean a paying,
in-force subscription are `Active` and `Trialing`; `Pending`, `Assessing`, `AwaitingSignup`, `SoftFailure`,
`PastDue`, `Suspended`, `Unpaid`, `OnHold`, `Paused` are in-flight or dunning; `Canceled`, `Expired`,
`TrialEnded`, `FailedToCreate` are terminal. Which of the middle band your app treats as "already
subscribed, don't create another" is a product decision — `YOUR CALL — not in the map`. What the map
*does* settle: the value list above, and that an unknown future value can arrive (`IsKnownValue()`), so the
comparison must have a defined else-branch rather than assuming exhaustiveness.

### 2G. Model-construction conventions

Records are immutable with `init`-only setters — **object-initializer syntax, never constructor
arguments**, and `required` members must be set in the initializer:

```csharp
var body = new CreateSubscriptionRequest
{
    Subscription = new CreateSubscription
    {
        ProductHandle = planHandle,
        CustomerId    = customer.Id!.Value,
    },
};
var resp = await client.Subscriptions.CreateSubscription(body, ct: ct);
var sub  = resp.Subscription;   // nullable — check it
```

Wire names are `[JsonPropertyName]` snake_case as shown in every field cell; C# names are PascalCase.
`CreateSubscription`'s optional members are emitted with
`[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`, so members you leave null are omitted from
the payload rather than sent as `null`. Unmodeled JSON fields are dropped on deserialize —
⚠ see trap note T3. *(Source: `sdk-map.md` model conventions; `Models/CreateSubscription.cs`.)*

### 2H. Configuration keys (bind from the `Maxio:` section)

| Binding key | Maps to | Default / note | Source |
|---|---|---|---|
| `Maxio:ApiKey` | `options.BasicAuth.Username` (with `Password = "x"`) | no default — required; without it `BasicAuth` stays `null` and every call is unauthenticated | `sdk-map.md`, `MaxioAdvancedBillingClientOptions.cs` |
| `Maxio:Subdomain` | `options.Server.Production.Us.Site` | SDK default is the literal `"subdomain"` — a placeholder that will produce `https://subdomain.chargify.com`, so an unset key fails as a DNS/host error, not a config error | `Servers/ProductionOptions.cs` |
| `Maxio:BaseUrl` | `options.Server.Production.Us.BaseUrl`, **verbatim**, when non-empty | SDK default `"https://{site}.chargify.com"`; leave untouched when the key is absent | `Servers/ProductionOptions.cs` |
| `Maxio:ProductFamilyHandle` | your own lookup argument in O1/O2 — **not** an SDK option | no SDK default | `operations/ProductFamilies.md` |
| environment selection | `options.Environment` | `ServerEnvironment.Default()` ⇒ `Us`. Only `Us`/`Eu` exist — see §5 for what this means for `MAXIO_ENVIRONMENT` | `MaxioAdvancedBillingClientOptions.cs`, `Servers/ServerEnvironment.cs` |
| how these keys are populated from the environment | the app's own configuration providers | `YOUR CALL — not in the map` — the SDK has no environment-variable awareness of any kind; it reads only what you assign to the options object |

---

## 3. Trap notes

Each names a hazard and its consequence and points at the skill that resolves it. **Do not treat these
lines as the answer** — they deliberately are not.

> ⚠ **T1 — Step 1 (client registration).** `AddMaxioAdvancedBillingClient` registers the client as a
> **singleton** over `IHttpClientFactory.CreateClient()` (the default, unnamed client) and runs your
> `configure` callback once at registration. Whether that lifetime, that handler pipeline and that shared
> unnamed client are right for a PublicApi that also uses `HttpClient` elsewhere — and what
> `HttpClient.Timeout` on that instance does to an SDK call — is exactly what the signature will not tell
> you. **MUST load `dotnet-client-initialization`** before wiring the client.

> ⚠ **T2 — Step 1 (resilience & base URL).** `options.Retry` is a fully-`required` `RetryOptions` (build it
> from `RetryOptions.Default()`), and its `Timeout`, `MaxRetries`, `HttpMethodsToRetry` and
> `StatusCodesToRetry` do **not** mean what their names suggest about **whether a failed
> `POST /subscriptions.json` can be re-sent** or **what span of time a timeout actually bounds** — and that
> question is load-bearing here, because a re-sent subscribe is a duplicate subscription. **MUST load
> `dotnet-configuration-resilience`** before you register or tune the client.

> ⚠ **T3 — Steps 2–6 (models).** `SubscriptionState`/`IntervalUnit` are `StringEnum<T>` records rather than
> C# enums, `SubscriptionIdOrReference`/`ComponentIdModel`/`Quantity1` are unions built and read through
> factories and `TryGet…`, and fields the SDK does not model are silently dropped on deserialize. Getting
> any of these wrong produces data that is *quietly* wrong rather than a build break. **MUST load
> `dotnet-models`** before constructing request payloads or mapping SDK models to your DTOs.

> ⚠ **T4 — Steps 2–6 (calling).** Every in-scope list operation has a long run of nullable parameters with
> **no C# default** (`ListProductsForProductFamily` has 8, `ListSubscriptions` has 14) sitting before
> `page`/`perPage`; a positional call mis-binds silently, and the token parameter is `ct`. **MUST load
> `dotnet-calling-endpoints`** before the first `client.…` call.

> ⚠ **T5 — Step 1 (auth).** Where the API key is read from, when credentials are attached, and what a
> 401/403 from this scheme actually looks like are not visible in `BasicAuthCredentials`' two properties.
> **MUST load `dotnet-authentication`** before wiring credentials.

> ⚠ **T6 — Step 7 (error boundary).** The catch ladder for these endpoints has to span Case A and Case B
> operations *and* a non-`SdkException` failure mode; a ladder that looks obviously complete is the usual
> way this goes wrong. **MUST load `dotnet-error-handling`** before writing any `try`/`catch`.

> ⚠ **T7 — Step 9 (tests).** The `HttpClient` constructor argument is the seam; what to fake and what to
> assert so the tests do not encode SDK internals is the skill's subject. **MUST load `dotnet-testing`**
> before stubbing the SDK.

---

## 4. Error-handling contract (what actually reaches your catch blocks)

Confirmed from source, not inferred:

- **`SdkException<TError>` (`MaxioAdvancedBilling.Core.Exceptions`) is `sealed` and declares exactly one
  member: `public required TError Error { get; init; }`.** There is **no `StatusCode` property on the
  exception**, and no message is set — `ex.Message` is the framework default text. Never read a status
  from `ex.Message` or `ex.ToString()`. *(Source: `Core/Exceptions/SdkException.cs`.)*
- **Case B operations** (O1, O3, O4, O5, O7, O9): `TError` is `RawError`. Status and body come from
  `ex.Error.StatusCode` (`System.Net.HttpStatusCode`), `ex.Error.ReadAsString()`,
  `ex.Error.ReadAsJson<T>()`, `ex.Error.ReadAsBytes()`. *(Source: `sdk-map.md`; `Core/ErrorResponse/RawError.cs`.)*
- **Case A operations** (O2, O6, O8, O10): `TError` is a generated `…Error : ApiError`. The generated
  `Create` is a `switch` on the status: the matching status builds the **typed** branch with the raw
  fallback left `default`, every other status builds the **fallback** branch with the typed slot left
  `default`. **The two are mutually exclusive**, so on the typed status **`TryGetRawError` returns `false`
  and neither the status code nor the raw body is recoverable** — you must infer the status from *which*
  accessor matched. Concretely for `CreateSubscriptionError`: 422 ⇒ `TryGetErrorListResponse1` true /
  `TryGetRawError` false; anything else ⇒ `TryGetRawError` true / typed false.
  *(Source: `Errors/CreateSubscriptionError.cs`, `Errors/ListProductsForProductFamilyError.cs`, `Core/ErrorResponse/ApiError.cs`.)*
- **Distinguishing "customer not found by reference" (the idempotent ensure-customer path).**
  `ReadCustomerByReference` is **Case B**, so this is unambiguous and needs no typed accessor:

  ```csharp
  catch (SdkException<RawError> ex) when (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
  {
      // no customer with that reference → create one (O6)
  }
  ```

  Any other `StatusCode` (401, 403, 5xx, …) must **not** be treated as "not found" — it must propagate, or
  the ensure step will create a duplicate customer on every transient failure. *(Source:
  `operations/Customers.md`; `Api/Customers.cs` — the call passes `RawErrorResponse.Instance`.)*
  **`UNVERIFIED`:** whether the lookup endpoint answers a missing reference with `404` rather than a `200`
  carrying an empty body is a live-wire fact. **Defensive directive:** treat *both* `404` **and** a `200`
  whose `CustomerResponse.Customer.Id` is null as "not found", and let every other status propagate to the
  generic failure path.
- **Reading the 422 body on subscribe:** `ErrorListResponse1.Errors (errors): IReadOnlyList<string>` —
  `required`, the human-readable validation messages. Safe to surface (after your own scrubbing) as the
  reason a subscribe was rejected. On `CreateCustomer` the 422 payload is a **different** shape:
  `CustomerErrorResponse1.Errors (errors): Errors?` where `Errors` has only
  `PerPage (per_page): IReadOnlyList<string>?` and `PricePoint (price_point): IReadOnlyList<string>?` —
  **it carries no general message list**, so a customer-creation 422 may yield an object with both
  properties null. **Defensive directive:** extract best-effort from those two lists and, when both are
  empty, fall back to a generic message rather than rendering an empty string.
- **A third failure mode reaches the boundary that is not an `SdkException` at all** — see the two
  mandatory hazard rows in REQUIRED READING. **MUST load `dotnet-error-handling`** before writing the
  boundary.
- **No no-throw variants exist anywhere in this SDK** — all 247 operations are throw-only, so every call
  above must be wrapped. *(Source: `sdk-map.md`.)*

---

## 5. Pagination summary

| Operation | Pages? | Parameter names (C# → wire) | Notes |
|---|---|---|---|
| O1 `ListProductFamilies` | **No** | — | no `page`/`perPage` parameters exist |
| O2 `ListProductsForProductFamily` | **Yes** | `page` → `page` (default `1`), `perPage` → `per_page` (default `20`) | max `perPage` is **200**; values above are coerced to 200 (SDK XML doc). With two products in the family, `perPage: 200` retrieves everything in one call — but loop on `page` if you cannot assume the family stays small |
| O5 `ReadCustomerByReference` | No | — | single result |
| O7 `ListCustomerSubscriptions` | **No** | — | **the operation has no pagination parameters at all** — one call returns the customer's whole set |
| O9 `ListSubscriptions` | **Yes** | `page` → `page` (default `1`), `perPage` → `per_page` (default `20`) | site-wide list; only needed if you deliberately choose the filtered route over O7 |
| O10 `CreateUsage` | No | — | — |
| `ListProducts` / `ListCustomers` (not used here) | Yes | `page`/`perPage`; `ListCustomers` defaults `perPage` to **50** | for reference only |

*Source: `operations/ProductFamilies.md`, `operations/Customers.md`, `operations/Subscriptions.md`, `operations/Products.md`.*

---

## 6. Idempotency — what the SDK and API actually offer

| Question | Answer | Source |
|---|---|---|
| Idempotency-key header or parameter? | **None.** No operation in this SDK accepts an idempotency key; `CreateSubscription` and `CreateCustomer` send an **empty header-parameter array** — the generated calls pass no headers beyond auth. | `Api/Subscriptions.cs`, `Api/Customers.cs`; no `Idempotency` symbol exists anywhere in the SDK surface |
| Customer uniqueness by reference? | **Yes, provider-enforced.** The `CreateCustomer` Notes state it verbatim: *"The only validation restriction is that you may only create one customer for a given reference value. If provided, the `reference` value must be unique."* So a duplicate `CreateCustomer` with the same reference is **rejected** (expect the 422 typed branch) rather than creating a second customer. | `operations/Customers.md` |
| Subscription uniqueness by reference? | **Not stated anywhere in the map.** `CreateSubscription.Reference` exists and `FindSubscription` looks a subscription up by it, but no Notes or model constraint asserts uniqueness. `UNVERIFIED` — do not rely on it as dedupe. | `operations/Subscriptions.md`, `models/records-2-Cr-Ne.md` |
| Any "create-or-return-existing" subscription operation? | **No such operation exists** in the 12 `Subscriptions` operations. `CreateSubscription` is an unconditional create. | `operations/Subscriptions.md` |

**Consequence for the "advise the right rule" question — the fact, and where it stops being mine:**
the only dedupe the provider gives you for free is on the **customer** reference. For the subscription there
is no provider-side guard at all, so the read-then-create sequence (O7 → O8) is a check-then-act with a
window between the two calls. Whether the rule is *"no active subscription for that plan"* or *"no active
subscription at all"*, and how the window is closed (a per-user lock, a unique constraint in the eShopOnWeb
database, a queue), are the application's concurrency and persistence design — `YOUR CALL — not in the map`.
What the map settles is that **the SDK will not close it for you.** See §8 Blockers.

---

## 7. REQUIRED READING — load **before implementation starts**

This sheet deliberately does **not** carry these skills' contents. Load each one before writing the code
for the step it governs:

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — constructing/registering `MaxioAdvancedBillingClient`, HttpClient ownership and lifetime |
| `dotnet-authentication` | Step 1 — supplying `BasicAuthCredentials`, credential sourcing, 401/403 behaviour |
| `dotnet-configuration-resilience` | Step 1 — `RetryOptions`, timeouts, base-URL/server selection, pagination loops, logging |
| `dotnet-calling-endpoints` | Steps 2–6, 8 — every `client.…` call, named arguments, `ct`, response envelopes |
| `dotnet-models` | Steps 2–6, 8 — request bodies, `required`/nullable members, `StringEnum<T>`, unions, wire names |
| `dotnet-error-handling` | Step 7 — the whole error boundary (and the two rows below) |
| `dotnet-testing` | Step 9 — the SDK seam and what to assert |

**Two `System.Text.Json.JsonException` hazard rows — it reaches the boundary from two directions and they
need opposite handling:**

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from
  deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the
  integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the
  `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a
  5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something
  that can never succeed.

Both are concrete here, not theoretical: `ProductResponse.Product`, `CustomerResponse.Customer`,
`SiteResponse.Site` and `ErrorListResponse1.Errors` are all C# `required`, and
`CreateSubscriptionError.Create` deserializes a 422 body straight into `ErrorListResponse1`
(`Errors/CreateSubscriptionError.cs`) — a 422 whose body is not that shape yields a `JsonException`
instead of `SdkException<CreateSubscriptionError>`.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 8. Assumptions & Blockers

### Assumptions

1. **Customer reference value.** The plan assumes a single stable per-user string is used as
   `CreateCustomer.Reference` and as the O5 lookup key. *Which* eShopOnWeb identity value that is (the
   ASP.NET Identity user id, the buyer id, the JWT `sub`, …) is `YOUR CALL — not in the map`; the SDK only
   requires that it be stable, unique per user, and never reused.
2. **Customer name/email for `CreateCustomer`.** `FirstName`, `LastName` and `Email` are C# `required`, so
   they must come from somewhere. eShopOnWeb's identity carries an email; first/last name is
   `YOUR CALL — not in the map` — you must supply *some* non-null strings, and inventing a placeholder is a
   product decision, not an SDK fact.
3. **Environment is `Us`.** `ServerEnvironment.Us` (the SDK default) is assumed, giving
   `https://cp-exp-2.chargify.com` from the subdomain. Switch to `Eu` only if the account requested EU
   hosting — and remember the override must then be written to `…Production.Eu.*`.
4. **`Maxio:BaseUrl` when set does not contain a `{site}` token.** If it did, `TemplateParamsFactory` would
   substitute it and the value would not be verbatim.
5. **The `api-call` metered component (step 8) is optional and out of the hero flow.** Recording usage
   needs the subscription id (from O8/O7) and the component identified as `"handle:api-call"` through
   `ComponentIdModel`; it is listed for completeness only and should not expand the delivery.

### Blockers — resolve these before or during implementation; do not code around them

1. **`MAXIO_ENVIRONMENT` (sandbox) has no SDK counterpart. `ServerEnvironment` cannot express it.**
   `Servers/ServerEnvironment.cs` declares a `private` constructor, exactly two public members
   (`Us` = `"US"`, `Eu` = `"EU"`), and **no `FromValue`** — `ServerEnvironment.FromValue("sandbox")` does
   not compile and a third value cannot be constructed. In this SDK a sandbox is a **site** (a subdomain
   such as `cp-exp-2`), not an environment. **Decide explicitly** whether `MAXIO_ENVIRONMENT` maps to
   nothing at all (recommended: ignore it in the SDK options and let `Maxio:Subdomain` /
   `Maxio:BaseUrl` do the targeting) or to something app-side. Do **not** invent an SDK mechanism for it.
   If a sandbox assertion is wanted at startup, the grounded check is `client.Sites.ReadSite(ct: ct)` →
   `SiteResponse.Site.Test (test): bool?`.
2. **Double-click safety for the subscribe endpoint is NOT achievable from the SDK alone.** There is no
   idempotency key and no create-or-get subscription operation (§6). The customer half is safe
   (provider-enforced reference uniqueness), but two concurrent `POST /api/subscriptions` for the same user
   can both pass the O7 check and both call O8, producing two subscriptions. **The plan is incomplete until
   the application supplies the guard** — a per-caller lock/serialization or a uniqueness constraint in
   eShopOnWeb's own store, keyed on (user, plan). Its design is the implementer's; the fact that it is
   *required* is the SDK's, and it must not be waved off as a caveat inside step 5. Compounding this, T2
   flags that whether the client itself may re-send a failed `POST` is a `dotnet-configuration-resilience`
   question — answer it before shipping the subscribe path.
3. **`Product` carries no currency field.** The `Product` model (37 members) has **no `currency`** — plan
   currency for `GET /api/subscription-plans` is not available from the products list. The grounded source
   is `client.Sites.ReadSite(ct: ct)` → `SiteResponse.Site.Currency (currency): string?` (site primary
   currency; `Site.NonPrimaryCurrencies` lists the others). Per-plan multi-currency prices exist only at
   the **price-point** level (`ProductPricePoint.CurrencyPrices: IReadOnlyList<CurrencyPrice>?`, each with
   `Currency`, `Price: double?`, `FormattedPrice`), reachable through the `ProductPricePoints` controller —
   **out of the current scope**. Confirm that "the site's primary currency for all plans" is acceptable; if
   per-plan currency is genuinely required, this is a scope expansion, not a field you can read off `Product`.
4. **RESOLVED 2026-09-06 — was: "no payment-method waiver exists". It does exist; this is NOT a stop-and-report gap.**
   The live 422 (`"No payment method was on file for the $299.00 balance"`) is fixed by setting
   `CreateSubscription.PaymentCollectionMethod` to `CollectionMethod.Remittance` (Relationship Invoicing)
   or `CollectionMethod.Invoice` (legacy Statements), branched on
   `Site.RelationshipInvoicingEnabled`. My original instruction not to set that field was wrong and is
   retracted — see §2D and §2D-bis. **Remaining action, and it is a real one:** the task brief said both
   sandbox products were configured "payment method NOT required", yet the platform still assessed and
   tried to collect a $299.00 signup balance. `Product.RequireCreditCard` only governs whether a profile
   must be *entered at signup*; it does not waive *collection*. **Report this discrepancy** rather than
   silently papering over it — and confirm the collection-method change is an accepted billing behaviour
   for eShopOnWeb (it means Maxio issues an invoice for $299.00 that someone must actually settle), not
   just a way to make the 422 disappear. If the intent was genuinely "$0 due at signup", that is a
   **catalog/pricing** change (a $0 or trial price point), not a request-body change. The SDK can toggle
   `require_credit_card` via `Products.UpdateProduct(int productId, CreateOrUpdateProductRequest? body, ct: ct)`
   → `CreateOrUpdateProduct.RequireCreditCard (require_credit_card): bool?`, but ⚠ that operation's Notes
   warn *"Updating a product using this endpoint will **create a new price point and set it as the default
   price point** for this product"* — do not run it from application code as a workaround.
5. **Price is in *cents*, as `long?`.** `Product.PriceInCents` and `Subscription.ProductPriceInCents` are
   `long?` minor units ($299.00 ⇒ `29900`). There is no decimal/formatted price on `Product`. Any
   conversion and rounding for display is the application's, and must not be done implicitly.
