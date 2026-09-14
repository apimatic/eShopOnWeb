# Maxio Advanced Billing — integration plan & contract sheet (eShopOnWeb / src/PublicApi)

Grounded in the bundled SDK map (`sdk-map.md` + `map/operations/*`, `map/models/*`) and, where the map
was silent, in the generated SDK source at the commit the map was stamped from (tag `v1.0.2`, commit
`15db14b`). Every row cites the page or source file it came from. Rows marked `UNVERIFIED` are facts only
live traffic can settle — each carries a defensive-coding directive. Rows marked `YOUR CALL — not in the
map` are application decisions, not SDK facts.

---

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 1 | New class library (Maxio-facing infrastructure) referencing the NuGet package; DI registration of `MaxioAdvancedBillingClient` + options binding (API key, subdomain, environment, optional verbatim base URL, family handle, default plan handle). | — (client construction only) |
| 2 | **Resolve the configured product-family handle → numeric id.** `ListProductFamilies(...)` returns the site's families; match `ProductFamily.Handle` against the configured handle. | `client.ProductFamilies.ListProductFamilies` |
| 3 | **GET /api/subscription-plans** — list products in that family, map to plan DTOs. Currency is **not** on `Product`; take it from `client.Sites.ReadSite()` → `Site.Currency` (one extra call) or from your own configuration. | `client.ProductFamilies.ListProductsForProductFamily`, `client.Sites.ReadSite` |
| 4 | **POST /api/subscriptions — plan validation.** Resolve the requested (or configured default) plan handle to a `Product` via `ReadProductByHandle`, and inspect its payment-method flags before attempting a no-payment-method create (step 7). | `client.Products.ReadProductByHandle` |
| 5 | **POST /api/subscriptions — customer idempotency.** Look the customer up by reference first (`ReadCustomerByReference`); create only if absent (`CreateCustomer`); on a create failure re-run the lookup to absorb a lost race. | `client.Customers.ReadCustomerByReference`, `client.Customers.CreateCustomer` |
| 6 | **POST /api/subscriptions — subscription idempotency.** List the customer's subscriptions and return an existing active one for the same product handle instead of creating a second. | `client.Customers.ListCustomerSubscriptions` |
| 7 | **POST /api/subscriptions — create.** `CreateSubscription` with `product_handle` + `customer_id`, no payment-profile fields. | `client.Subscriptions.CreateSubscription` |
| 8 | **GET /api/my-subscriptions** — resolve the caller's customer by reference, then list that customer's subscriptions. | `client.Customers.ReadCustomerByReference`, `client.Customers.ListCustomerSubscriptions` |
| 9 | Error boundary translating SDK exceptions (and `JsonException`) into HTTP results for the three endpoints. | — |

Metered usage (`api-call` component) is **out of scope** as instructed, and is not trivial to bolt on: the
operation is `CreateUsage` on `client.SubscriptionComponents` (`map/operations/SubscriptionComponents.md`);
its signature and error case are deliberately not carried in this sheet. Ask for that row when you take it on.

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

### 2.1 Package & namespaces

| Item | Value | Source |
|---|---|---|
| NuGet package id | `AsadAli.AdvancedBilling.Sdk` | `sdk-map.md` (identity table) |
| Package version to reference | **Pin explicitly in the `.csproj`; do not float.** The SDK surface described here is the source tagged `v1.0.2` (commit `15db14b`). The `.csproj` **inside that tag declares `<Version>1.0.0</Version>`**, so tag and packed version disagree and the published NuGet version cannot be settled from map or source. Directive: install with an explicit `--version` taken from the nuget.org listing, pin it, and if any name in this sheet fails to compile, trust the compiler and bring me the error. | `UNVERIFIED` (evidence: `sdk-map.md` stamp vs `MaxioAdvancedBilling.csproj` `<Version>`) |
| Transitive dependencies | `Microsoft.Extensions.Http`, `Polly`, `System.Net.Http.Json`, `System.Net.ServerSentEvents` | `MaxioAdvancedBilling.csproj` |
| SDK target framework | `netstandard2.0` | `sdk-map.md` |

`using` directives by kind of type — C# does **not** import child namespaces transitively, so one `using` per row:

| Types you will reference | Namespace | Source |
|---|---|---|
| `MaxioAdvancedBillingClient`, `MaxioAdvancedBillingClientOptions`, `ServerOptions`, `AddMaxioAdvancedBillingClient` (extension on `IServiceCollection`) | `MaxioAdvancedBilling` | `sdk-map.md` (namespaces table); `ServerOptions.cs`, `ServiceCollectionExtensions.cs` (root-level files ⇒ root namespace) |
| `ServerEnvironment`, `ProductionOptions` (+ nested `ProductionOptions.UsOptions` / `.EuOptions`), `EbbOptions` | `MaxioAdvancedBilling.Servers` | `sdk-map.md`; `Servers/ProductionOptions.cs` |
| `BasicAuthCredentials` | `MaxioAdvancedBilling.Core.Authentication.Basic` | `sdk-map.md` ("Getting a client") |
| `RetryOptions` | `MaxioAdvancedBilling.Core.Configuration` | `sdk-map.md` (RetryOptions table) |
| `SdkException<TError>` | `MaxioAdvancedBilling.Core.Exceptions` | `sdk-map.md` (`Core/Exceptions/SdkException.cs`) |
| `RawError`, `ApiError` | `MaxioAdvancedBilling.Core.ErrorResponse` | `sdk-map.md` (error-core table) |
| Controller classes (`Products`, `ProductFamilies`, `Customers`, `Subscriptions`, `Sites`) | `MaxioAdvancedBilling.Api` | `sdk-map.md` (namespaces table) |
| Records (`Product`, `ProductResponse`, `Subscription`, `CreateSubscriptionRequest`, …) | `MaxioAdvancedBilling.Models` | `map/models/records-*.md` (header) |
| Enums (`IntervalUnit`, `SubscriptionState`, `PricePointType`, `CollectionMethod`, `BasicDateField`, …) | `MaxioAdvancedBilling.Models.Enums` | `map/models/enums.md` (header) |
| Typed error classes (`CreateSubscriptionError`, `CreateCustomerError`, `ListProductsForProductFamilyError`) | `MaxioAdvancedBilling.Errors` | `sdk-map.md` (namespaces table) |

⚠ **Name-collision hazard (CS0104).** The controller **classes** are plural nouns —
`MaxioAdvancedBilling.Api.Customers`, `.Api.Subscriptions`, `.Api.Products` — and `MaxioAdvancedBilling.Models`
contains records named `Customer`, `Product`, `Subscription` and one literally named **`Errors`**. If your
library declares its own types with these names and imports both namespaces, the build fails with CS0104
(ambiguous reference). Fully qualify, or alias (`using MaxioProduct = MaxioAdvancedBilling.Models.Product;`).
Source: `sdk-map.md` namespaces table; `map/models/records-*.md`.

### 2.2 Client construction, auth, environment, base URL

Only one constructor exists:
`MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`.
Source: `sdk-map.md` ("Getting a client") / `MaxioAdvancedBillingClient.cs`.

| Fact | Exact form | Source |
|---|---|---|
| Auth scheme | HTTP Basic. `options.BasicAuth = new BasicAuthCredentials { Username = "<api key>", Password = "x" }` — `Username` is the API key, `Password` is the **literal** `"x"`. Both members are `required string`, so both must be set in the object initializer. | `sdk-map.md` ("Servers & auth"); `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Options type | `MaxioAdvancedBillingClientOptions` has exactly four settable properties: `Environment: ServerEnvironment` (default `ServerEnvironment.Default()` == `.Us`), `Retry: RetryOptions` (default `RetryOptions.Default()`), `Server: ServerOptions` (default `new()`), `BasicAuth: BasicAuthCredentials?` (default `null`). | `sdk-map.md` (options table); `MaxioAdvancedBillingClientOptions.cs` |
| Environment selection | `ServerEnvironment` is a `StringEnum`, **not** a C# enum, with exactly two members: `ServerEnvironment.Us` (wire `US`, default) and `ServerEnvironment.Eu` (wire `EU`). Its constructor is private — use the static members. | `sdk-map.md`; `Servers/ServerEnvironment.cs` |
| **Sandbox vs production** | **The SDK has no sandbox/production switch.** `ServerEnvironment` selects **US vs EU hosting only**. A Maxio test/sandbox site is selected by pointing at that site's **subdomain** with that site's API key — configuration of `Site` + API key, not of `Environment`. (`Site.Test (test): bool?` from `Sites.ReadSite` tells you which kind of site you actually reached.) | `sdk-map.md` (servers table); `Servers/ServerEnvironment.cs`; `records-3-Of-Su.md` (`Site`) |
| Site subdomain | `options.Server.Production.Us.Site = "<subdomain>"` (and/or `.Eu.Site` when `Environment` is `Eu`). `Site` **defaults to the literal string `"subdomain"`** — leaving it unset silently produces `https://subdomain.chargify.com`. | `sdk-map.md`; `Servers/ProductionOptions.cs` |
| Base-URL templates (defaults) | `Production.Us.BaseUrl` defaults to `"https://{site}.chargify.com"`; `Production.Eu.BaseUrl` defaults to `"https://{site}.ebilling.maxio.com"`. | `Servers/ProductionOptions.cs` |
| **Verbatim base-URL override (`Maxio:BaseUrl`)** | **Supported — the SDK can do it.** Set `options.Server.Production.Us.BaseUrl = "<your verbatim URL>"` (and `.Eu.BaseUrl` as well if `Environment` may be `Eu`; `ProductionOptions.Resolve` picks the node matching `options.Environment`, so only that node's `BaseUrl` is used). It is used **verbatim**: the SDK performs a plain textual `{site}` substitution over the `BaseUrl` string, then joins `baseUrl.TrimEnd('/') + "/" + path.TrimStart('/')`. A URL containing no `{site}` token passes through unchanged and `Site` is simply **ignored** (no error, no appending). So: when `Maxio:BaseUrl` is present set `BaseUrl` from it and do not set `Site`; otherwise set `Site` and leave `BaseUrl` at its default. | `sdk-map.md` (override points); `Servers/ProductionOptions.cs`, `Core/TemplateParamsFactory.cs` |
| Ebb (events) server group | A second group `options.Server.Ebb.*` exists for event-ingest endpoints. **Nothing in this scope uses it** — every operation below is on the Production group. | `sdk-map.md` (servers table); `Server.cs` |
| DI registration — what the shipped extension literally does | `services.AddMaxioAdvancedBillingClient(o => { … })` (extension on `IServiceCollection`, namespace `MaxioAdvancedBilling`): constructs **one** `MaxioAdvancedBillingClientOptions`, invokes your callback **once at registration time**, calls `services.AddHttpClient()`, and registers `MaxioAdvancedBillingClient` as a **singleton** whose `HttpClient` comes from `IHttpClientFactory.CreateClient()` (the default, unnamed client) and is captured for that singleton's lifetime. Consequences you must decide on: options are a startup snapshot (no `IOptionsMonitor` reload path reaches the client), and the captured `HttpClient` is never re-created. | `ServiceCollectionExtensions.cs` |
| HttpClient ownership / thread-safety | The client never constructs an `HttpClient`; one is passed to the constructor (this is also the test seam). Whether you keep the shipped singleton-capturing registration or register the `HttpClient` yourself (named/typed client, handler lifetime, per-client timeout) is the decision the §3 trap note points you at. | `MaxioAdvancedBillingClient.cs`; `ServiceCollectionExtensions.cs` |
| Configuration binding keys | Bind an options class to a `Maxio` section: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:BaseUrl` (optional override), `Maxio:Environment` (`US`/`EU`), `Maxio:ProductFamilyHandle`, `Maxio:DefaultPlanHandle`. Only `Maxio:BaseUrl` is fixed by your brief; the rest of the key names are yours. The SDK reads no configuration itself. | `YOUR CALL — not in the map` |

### 2.3 Operations

All methods are **async-only**: they return `Task<T>` (or `Task`), have **no** `Async` name suffix and **no**
synchronous overload. Every method's last parameter is `CancellationToken ct = default`. Parameters listed
without a default have **no** C# default and must be passed explicitly (pass `null` to skip) — call these with
**named arguments**.

| # | Controller property | Method signature (verbatim, params in order) | Request model + fields | Response envelope + fields read | Error case + accessors | Pagination | Source |
|---|---|---|---|---|---|---|---|
| A | `client.ProductFamilies` | `Task<IReadOnlyList<ProductFamilyResponse>> ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 filters must be passed (`null`) | none (query only) | `IReadOnlyList<ProductFamilyResponse>`; `ProductFamilyResponse.ProductFamily (product_family): ProductFamily?` is **nullable → must null-check**. Unwrap: `resp.Select(r => r.ProductFamily).Where(f => f is not null)`. Read `ProductFamily.Id (id): int?`, `Handle (handle): string?`, `Name (name): string?`. | **Case B** — `SdkException<RawError>`; `ex.Error.StatusCode`, `.ReadAsString()`, `.ReadAsJson<T>()`, `.ReadAsBytes()` | **none** — no `page`/`perPage` on this operation | `operations/ProductFamilies.md`; `records-3-Of-Su.md` |
| B | `client.ProductFamilies` | `Task<IReadOnlyList<ProductResponse>> ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 8 params `dateField`…`include` must be passed explicitly | none (path + query). `productFamilyId` is a **`string`** while `ProductFamily.Id` is `int?` → pass `family.Id.Value.ToString(CultureInfo.InvariantCulture)`. Pass `includeArchived: false` to exclude archived plans. | `IReadOnlyList<ProductResponse>`; `ProductResponse.Product (product): Product` is **`required`** (non-null) → unwrap exactly as **`resp.Select(r => r.Product)`** | **Case A** — `SdkException<ListProductsForProductFamilyError>`; `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual: `page` (`int?`, default `1`), `perPage` (`int?`, default `20`) — **on the method signature**, wire `page` / `per_page`. Loop until a page returns fewer than `perPage` items. | `operations/ProductFamilies.md`; `records-3-Of-Su.md` (`ProductResponse`) |
| C | `client.Products` | `Task<ProductResponse> ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | none | `ProductResponse` → `.Product` (`required`) | **Case B** — `SdkException<RawError>`; an unknown handle surfaces here with `ex.Error.StatusCode` | none | `operations/Products.md` |
| D | `client.Sites` | `Task<SiteResponse> ReadSite(CancellationToken ct = default)` | none | `SiteResponse.Site (site): Site` is **`required`**; read `Site.Currency (currency): string?`, `Test (test): bool?`, `RelationshipInvoicingEnabled (relationship_invoicing_enabled): bool?`, `DefaultPaymentCollectionMethod (default_payment_collection_method): string?` | **Case B** — `SdkException<RawError>` | none | `operations/Sites.md`; `records-3-Of-Su.md` |
| E | `client.Customers` | `Task<CustomerResponse> ReadCustomerByReference(string reference, CancellationToken ct = default)` — `GET /customers/lookup.json`, wire query `reference` | none | `CustomerResponse.Customer (customer): Customer` is **`required`**; read `Customer.Id (id): int?`, `Reference (reference): string?`, `Email (email): string?` | **Case B** — `SdkException<RawError>`; status via `ex.Error.StatusCode` | none | `operations/Customers.md`; `records-1-Ac-Cr.md` |
| F | `client.Customers` | `Task<CustomerResponse> CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` is nullable with no default → **pass explicitly** | `CreateCustomerRequest { Customer (customer): CreateCustomer }` — `Customer` is **`required`**. `CreateCustomer` requireds: `FirstName (first_name): string`, `LastName (last_name): string`, `Email (email): string`. Optional but **carry it**: `Reference (reference): string?` — the operation's Notes make `reference` the unique-per-site key (§2.6). Notes-adjacent optionals deliberately left out: `Organization`, `CcEmails`, address fields (`Address`, `City`, `State`, `Zip`, `Country`), `Locale`, `VatNumber`, `TaxExempt`, `TaxExemptReason`, `ParentId`, `SalesforceId`. | `CustomerResponse` → `.Customer` (`required`) → `.Id` | **Case A** — `SdkException<CreateCustomerError>`; `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Customers.md`; `records-1-Ac-Cr.md` |
| G | `client.Customers` | `Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — `GET /customers/{customer_id}/subscriptions.json` | none | `IReadOnlyList<SubscriptionResponse>`; `SubscriptionResponse.Subscription (subscription): Subscription?` is **nullable** → `resp.Select(r => r.Subscription).Where(s => s is not null)` | **Case B** — `SdkException<RawError>` | **none** — this operation has **no** `page`/`perPage` parameters | `operations/Customers.md`; `records-4-Su-We.md` |
| H | `client.Subscriptions` | `Task<SubscriptionResponse> CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **pass explicitly** | `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription }` — `Subscription` is **`required`**. Exact `CreateSubscription` fields to set: §2.5. | `SubscriptionResponse.Subscription` is **nullable** → null-check before mapping the created result | **Case A** — `SdkException<CreateSubscriptionError>`; `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md`; `records-2-Cr-Ne.md` |

**Filtering a customer's subscriptions — "filter parameter or nested route?"**
Use the **nested route** (row G). `client.Subscriptions.ListSubscriptions` takes, verbatim,
`state, product, productPricePointId, coupon, couponCode, dateField, startDate, endDate, startDatetime,
endDatetime, metadata, direction, sort, include, page, perPage` — there is **no customer filter of any kind**,
and `product` is an `int?` **product id**, not a handle. Filter **client-side** over row G's result:
`s.Product?.Handle == planHandle && s.State == SubscriptionState.Active`.
Source: `operations/Subscriptions.md`, `operations/Customers.md`.

### 2.4 Response fields the endpoints read

`Product` (namespace `MaxioAdvancedBilling.Models`; source `records-3-Of-Su.md`) — every field is nullable:

| Purpose | Field (C# / wire) | Type |
|---|---|---|
| plan handle | `Handle (handle)` | `string?` |
| plan name | `Name (name)` | `string?` |
| description | `Description (description)` | `string?` |
| price amount | `PriceInCents (price_in_cents)` | `long?` (minor units) |
| price currency | **not present on `Product`** → use `Site.Currency` (row D) or configuration | — |
| billing interval | `Interval (interval)` | `int?` |
| billing interval unit | `IntervalUnit (interval_unit)` | `IntervalUnit?` (`Day (day)`, `Month (month)`) |
| trial price | `TrialPriceInCents (trial_price_in_cents)` | `long?` |
| trial length | `TrialInterval (trial_interval)` | `int?` |
| trial unit | `TrialIntervalUnit (trial_interval_unit)` | `IntervalUnit?` |
| charge after trial | `InitialChargeAfterTrial (initial_charge_after_trial)` | `bool?` |
| setup fee | `InitialChargeInCents (initial_charge_in_cents)` | `long?` |
| payment method required | `RequireCreditCard (require_credit_card)` | `bool?` — see the `UNVERIFIED` row in §2.5 |
| payment method requested | `RequestCreditCard (request_credit_card)` | `bool?` |
| archived marker | `ArchivedAt (archived_at)` | `DateTimeOffset?` |
| ids | `Id (id): int?`, `ProductFamily (product_family): ProductFamily?`, `DefaultProductPricePointId (default_product_price_point_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?` | — |

`Subscription` (namespace `MaxioAdvancedBilling.Models`; source `records-4-Su-We.md`) — every field nullable;
**all dates are `DateTimeOffset?`** (not `DateTime`, not `string`):

| Purpose | Field (C# / wire) | Type |
|---|---|---|
| subscription id | `Id (id)` | `int?` |
| state | `State (state)` | `SubscriptionState?` (StringEnum — §2.6) |
| previous state | `PreviousState (previous_state)` | `SubscriptionState?` |
| plan (product) | `Product (product)` | `Product?` → `.Handle`, `.Name`, `.PriceInCents` |
| price at signup | `ProductPriceInCents (product_price_in_cents)` | `long?` |
| current billing amount | `CurrentBillingAmountInCents (current_billing_amount_in_cents)` | `long?` |
| currency | `Currency (currency)` | `string?` |
| current period start | `CurrentPeriodStartedAt (current_period_started_at)` | `DateTimeOffset?` |
| current period end | `CurrentPeriodEndsAt (current_period_ends_at)` | `DateTimeOffset?` |
| next billing / assessment | `NextAssessmentAt (next_assessment_at)` | `DateTimeOffset?` |
| customer (reference echo) | `Customer (customer)` | `Customer?` → `.Reference`, `.Id`, `.Email` |
| subscription reference | `Reference (reference)` | `string?` |
| price point type | `ProductPricePointType (product_price_point_type)` | `PricePointType?` |
| collection method | `PaymentCollectionMethod (payment_collection_method)` | `CollectionMethod?` |
| lifecycle timestamps | `ActivatedAt`, `CanceledAt`, `ExpiresAt`, `TrialStartedAt`, `TrialEndedAt`, `CreatedAt`, `UpdatedAt` | all `DateTimeOffset?` |

`Customer` (source `records-1-Ac-Cr.md`): `Id (id): int?`, `Reference (reference): string?`,
`Email (email): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?`,
`CreatedAt (created_at): DateTimeOffset?`.

`ProductFamily` (source `records-3-Of-Su.md`): `Id (id): int?`, `Name (name): string?`,
`Handle (handle): string?`, `Description (description): string?`, `CreatedAt`/`UpdatedAt`/`ArchivedAt:
DateTimeOffset?`.

### 2.5 Creating a subscription with **no** payment method

⚠ `CreateSubscription` (namespace `MaxioAdvancedBilling.Models`; source `records-2-Cr-Ne.md`) marks **nothing**
as `required` — `required?` selects no field for you and no compiler error will appear if you omit an essential
one. Set exactly these, per the operation's Notes:

| Field (C#) | Wire name | Type | Why |
|---|---|---|---|
| `ProductHandle` | `product_handle` | `string?` | Selects the plan by **handle**. Notes: "Specify the product with `product_id` or `product_handle`." Use the handle (stable); do **not** also set `ProductId`. |
| `CustomerId` | `customer_id` | `int?` | Selects the existing customer resolved in step 5. Notes: "Identify an existing customer with `customer_id` or `customer_reference`." Prefer the id you just resolved; setting both it and `CustomerReference` is redundant and the Notes define no precedence. |
| `Reference` | ⚠ **`ref`** — the C# property is `Reference` but the JSON wire name is **`ref`**, not `reference` | `string?` | Optional: a subscription-level reference for traceability / `FindSubscription`. **Not** an idempotency guarantee — see §2.6. |

Fields deliberately left unset (all optional) and why: `CustomerReference (customer_reference)` — redundant with
`CustomerId`; `CustomerAttributes (customer_attributes)` — that is the *create-a-new-customer inline* path, which
would bypass the reference-based idempotency of step 5; `PaymentProfileId (payment_profile_id)`,
`CreditCardAttributes (credit_card_attributes)`, `PaymentProfileAttributes (payment_profile_attributes)`,
`BankAccountAttributes (bank_account_attributes)` — no payment method is captured;
`ProductPricePointHandle`/`ProductPricePointId` — omitted so the product's default price point applies;
`CouponCode`/`CouponCodes`, `Components`, `CalendarBilling`, `NextBillingAt`, `InitialBillingAt`, `DeferSignup`,
`Group`, `OfferId`, `PrepaidConfiguration`, `Metafields`, `Currency`, `NetTerms`, `AgreementAcceptance`,
`AchAgreement` — not in scope.

| Question | Answer | Source |
|---|---|---|
| Is there a request field that suppresses the payment-method demand? | **No.** The operation's Notes state: "Payment information **may be required to create a subscription, depending on the options for the Product** being subscribed." The demand is a **product-side** setting, not a `CreateSubscription` field; the model carries no `skip_payment`-style member. | `operations/Subscriptions.md` (CreateSubscription Notes); `records-2-Cr-Ne.md` (field list) |
| Which product flag says a plan needs no payment method? | `Product.RequireCreditCard (require_credit_card): bool?`, with a near-duplicate `Product.RequestCreditCard (request_credit_card): bool?` beside it; the map carries **no summary distinguishing them** and does not say which the live payload populates. Directive: treat a plan as "no payment method needed" only when **neither** is `true` (`RequireCreditCard != true && RequestCreditCard != true`); treat `null` as unknown, let the create attempt decide, and map its 422 to a clear "plan requires a payment method" result rather than a 500. | `UNVERIFIED` (evidence: `records-3-Of-Su.md`, two undocumented sibling flags) |
| Should I set `PaymentCollectionMethod`? | **Yes on a Relationship-Invoicing site — set it from configuration.** Leaving it null lets the site default apply, and a site whose `DefaultPaymentCollectionMethod` is `automatic` will attempt to collect the full balance at create and reject with 422 `"No payment method was on file for the $X balance"` even when `Product.RequireCreditCard` is `false` (observed live against the sandbox, 2026-09; confirmed fixed by the change below). Set `PaymentCollectionMethod = CollectionMethod.Remittance` (wire `remittance`) when `Site.RelationshipInvoicingEnabled == true` (row D); `CollectionMethod.Invoice` is **not** a valid option on such a site, and `Remittance` is **not** valid on a legacy Statements site — so drive the member from configuration, never hardcode it. The map does **not** document that this removes the payment-method demand; that link is inference from live traffic plus `IssueInvoice`'s Notes ("For Remittance subscriptions, the invoice will go into 'open' status and payment won't be attempted"). Say so in the code comment rather than presenting it as an SDK guarantee. | `UNVERIFIED` (evidence: `enums.md` `CollectionMethod` summary; `operations/Invoices.md`; `operations/Subscriptions.md` — no operation Notes anywhere mention `payment_collection_method`) |
| Trial / setup fee | No request field selects these; they come from the product (`TrialInterval`, `TrialPriceInCents`, `InitialChargeInCents`). Your brief says the plans have neither — assert it from the `Product` read in step 4 rather than assuming. | `records-3-Of-Su.md` |

### 2.6 Idempotency contracts

| Fact | Detail | Source |
|---|---|---|
| Customer `reference` uniqueness | Documented and provider-enforced: "you may only create one customer for a given reference value. If provided, the `reference` value must be unique. It represents a unique identifier for the customer from your own app." And the lookup: "Returns a customer by their unique reference ID. It will return a single match." So `ReadCustomerByReference` is an exact single-match lookup and a duplicate create is rejected. | `operations/Customers.md` (CreateCustomer + ReadCustomerByReference Notes) |
| What `ReadCustomerByReference` does when the reference is unknown | The SDK maps **any** non-2xx to `SdkException<RawError>` (Case B) carrying `ex.Error.StatusCode`; it never returns null on a 2xx, and `CustomerResponse.Customer` is `required`, so a 2xx body without a `customer` member throws `System.Text.Json.JsonException` instead. Whether "not found" arrives as **404** (rather than another status or an empty 200) is not stated in the map. Directive: `catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)` → treat as *absent* and proceed to create; **rethrow every other status**; and let `JsonException` reach the boundary rather than swallowing it as "absent". | `UNVERIFIED` (evidence: `operations/Customers.md` Case-B row; `Core/RawClient.cs`; `records-1-Ac-Cr.md` `CustomerResponse.Customer !req`) |
| Lost-race handling on customer create | Two concurrent requests can both observe "absent" and both call `CreateCustomer`; the loser gets a 422 whose typed payload cannot tell you *why* (next table). Directive: on **any** `SdkException<CreateCustomerError>`, re-run `ReadCustomerByReference`; if a customer now exists, continue with it (a lost race, not a failure); if it still reports absent, surface the failure. | `operations/Customers.md` Notes + §2.8 |
| Subscription-level idempotency | **No** provider-side uniqueness is documented for a subscription reference (contrast the explicit wording for customers), there is no idempotency-key parameter on `CreateSubscription`, and `FindSubscription(string? reference, …)`'s Notes say only "Finds a subscription by its reference." Directive: do **not** rely on `ref` as an idempotency key — guard with row G (`ListCustomerSubscriptions`) plus a client-side match on `Product.Handle` and `State`. Two truly simultaneous requests can still create two subscriptions unless your own code serializes them (Blocker 2). | `UNVERIFIED` (evidence: `operations/Subscriptions.md` — no uniqueness statement on CreateSubscription/FindSubscription) |
| Completeness of `ListCustomerSubscriptions` | The operation exposes **no** pagination parameters, so the integration cannot page it, and whether the provider caps the set for a customer with many subscriptions is not stated. Directive: match defensively (compare `Product?.Handle` and `State`, skip nulls) and, when no active match is found, let the create proceed — never present "the list looked empty" as proof of absence to the user. | `UNVERIFIED` (evidence: `operations/Customers.md`, Pagination: none) |
| `SubscriptionState` values | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)`. **`StringEnum`, not a C# enum** — compare `s.State == SubscriptionState.Active`, or build from wire text via `SubscriptionState.FromValue("active")`. | `enums.md` |
| Which states count as "already subscribed" | The map lists the values only; choosing the set (e.g. whether `Trialing`/`PastDue` block a second create) is a product decision. | `YOUR CALL — not in the map` |
| State immediately after create | Neither the map nor the `SubscriptionState` doc summary states a create-time landing state (the summary does not document `awaiting_signup` at all); the only documented create-time state facts sit on other fields — `InitialBillingAt` and `DeferSignup` say a subscription created with them lands in **Awaiting Signup** "rather than Active or Trialing". **Observed live** (sandbox, RI site, 2026-09): a `Remittance` subscription created with no payment profile lands in **`active`** immediately. Directive: read the state back off the `SubscriptionResponse` returned by `CreateSubscription` rather than assuming it; and note the doc's warning that `pending` and `assessing` are transient and "may not always be exposed" — safe inside an idempotency guard, not a basis for granting product access. | `UNVERIFIED` (observed, not documented — evidence: `enums.md`; `operations/Subscriptions.md`) |

### 2.7 Enums used (all `StringEnum`, namespace `MaxioAdvancedBilling.Models.Enums`)

| Enum | Members (`CSharpName (wire)`) | Source |
|---|---|---|
| `IntervalUnit` | `Day (day)`, `Month (month)` | `enums.md` |
| `ExpirationIntervalUnit` | `Day (day)`, `Month (month)`, `Never (never)` | `enums.md` |
| `SubscriptionState` | 15 members — see §2.6 | `enums.md` |
| `SubscriptionStateFilter` (only if you ever call `ListSubscriptions`) | `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)` | `enums.md` |
| `PricePointType` | `Catalog (catalog)`, `Default (default)`, `Custom (custom)` | `enums.md` |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` | `enums.md` |
| `BasicDateField` (pass `null` in this plan) | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` | `enums.md` |
| `ListProductsInclude` (pass `null`) | `PrepaidProductPricePoint (prepaid_product_price_point)` | `enums.md` |
| `CancellationMethod` (only if you surface cancellation info) | `MerchantUi (merchant_ui)`, `MerchantApi (merchant_api)`, `Dunning (dunning)`, `BillingPortal (billing_portal)`, `Unknown (unknown)`, `Imported (imported)` | `enums.md` |

`ListProductsFilter` (pass `null` here) is a **record**, not an enum: `Ids (ids): IReadOnlyList<int>?`,
`PrepaidProductPricePoint (prepaid_product_price_point): PrepaidProductPricePointFilter?`,
`UseSiteExchangeRate (use_site_exchange_rate): bool?`. Source: `records-2-Cr-Ne.md`.

### 2.8 Exception surface — what can reach your catch blocks

| Fact | Detail | Source |
|---|---|---|
| The exception type | `public sealed class SdkException<TError> : Exception` with a single member `public required TError Error { get; init; }`. It is **generic and sealed**, and there is **no non-generic base** (`ApiException` does not exist in this SDK). Consequence: `SdkException<CreateSubscriptionError>` and `SdkException<RawError>` are unrelated closed types — no single `catch (SdkException<…>)` clause covers both, and none covers "all SDK errors". | `Core/Exceptions/SdkException.cs`; `sdk-map.md` |
| No status code on the exception | `SdkException<TError>` carries **no** `StatusCode` and no meaningful `Message`. HTTP status is reachable **only** through the error object: Case B → `ex.Error.StatusCode`; Case A → `ex.Error.TryGetRawError(out var raw)` then `raw.StatusCode`. | `Core/Exceptions/SdkException.cs`; `Core/ErrorResponse/RawError.cs` |
| ⚠ Case-A trap: typed payload and raw error are **mutually exclusive** | Each generated `{Operation}Error` is built by a status switch: on the modelled status (e.g. `422 => FromJson<ErrorListResponse1>`) the typed value is set and the raw fallback is left `default`; on every other status only the raw fallback is set. So when `TryGetErrorListResponse1` succeeds, **`TryGetRawError` returns `false`** and the status code and raw body are unavailable; when `TryGetRawError` succeeds the typed accessor returns `false`. Write both branches; `TryGetRawError` is not a catch-all. | `Errors/CreateSubscriptionError.cs`, `Errors/CreateCustomerError.cs`, `Errors/ListProductsForProductFamilyError.cs`, `Core/ErrorResponse/ApiError.cs` |
| Field-level validation payloads in this scope | `CreateSubscription` 422 → `ErrorListResponse1 { Errors (errors): IReadOnlyList<string> }` — **`required`**, a flat list of message strings with no field names. `CreateCustomer` 422 → `CustomerErrorResponse1 { Errors (errors): Errors? }`, where the generated `Errors` record has **only two members**: `PerPage (per_page): IReadOnlyList<string>?` and `PricePoint (price_point): IReadOnlyList<string>?`. `ListProductsForProductFamily` 404 → a bare `string` via `TryGetString`. | `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md`, `operations/*.md` |
| How far these error contracts can be trusted | **Low — and the evidence is inside the map.** `CustomerErrorResponse1.Errors` is typed as a record whose only members are `per_page` and `price_point`, pagination/price-point concepts unrelated to customer validation; any real per-field message (`email`, `reference`, …) is an unmodelled JSON member and is **dropped on deserialize**. Sibling operations model the same `errors` key three incompatible ways — `IReadOnlyList<string>` (required, `ErrorListResponse1`), `IReadOnlyDictionary<string, object>?` (`ErrorArrayMapResponse1`, `ActivateSubscription`) and `Error: string` (required, `SingleErrorResponse1`, `OverrideSubscription`) — so at most one can match any given wire shape. Directive: extract messages **best-effort** (`ErrorListResponse1.Errors` joined; for customers `Errors.PerPage`/`Errors.PricePoint` when non-null) and **always** fall back to a generic, non-leaking message; never take a control-flow decision (e.g. "reference already taken") from parsed text — re-query instead (§2.6). | `UNVERIFIED` (evidence: `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md`, `operations/Subscriptions.md`) |
| Which operation declares which error type (this scope) | **Case A:** `ListProductsForProductFamily` → `ListProductsForProductFamilyError`; `CreateCustomer` → `CreateCustomerError`; `CreateSubscription` → `CreateSubscriptionError`. **Case B (`SdkException<RawError>`):** `ListProductFamilies`, `ReadProductByHandle`, `ReadSite`, `ReadCustomerByReference`, `ListCustomerSubscriptions`. | `operations/ProductFamilies.md`, `operations/Products.md`, `operations/Sites.md`, `operations/Customers.md`, `operations/Subscriptions.md` |
| No-throw variants | **Absent across the whole SDK** — every operation throws. Of 247 operations, 163 are Case A and 84 are Case B. Ignore any `…Result`/`ApiResult` pattern from general APIMatic guidance. | `sdk-map.md` |
| 401 / auth failures | A 401 arrives as the operation's normal error case (Case B → `ex.Error.StatusCode == HttpStatusCode.Unauthorized`; Case A → via `TryGetRawError`). Check the API key, `Site`/`BaseUrl`, and that `Password` is the literal `"x"` before touching call sites. | `sdk-map.md`; `Core/RawClient.cs` |
| Rate limiting (429) | The SDK models **no** rate-limit type, no 429-specific accessor and no `Retry-After` surface on any operation in this scope: a 429 arrives as Case B `SdkException<RawError>` with `StatusCode == (HttpStatusCode)429`, or as the Case-A raw fallback. Whether the SDK's retry pipeline re-sends it, and on which verbs, is the resilience question in §3. | `sdk-map.md`; `operations/*.md` |
| Cancellation | Every operation's last parameter is `CancellationToken ct = default`; flow the endpoint's token through as `ct:`. How cancellation vs. timeout presents at the boundary is companion-skill territory (§3). | `operations/*.md` |

---

## 3. Trap notes

Each names a hazard and its consequence; the answer is in the named skill, which you must load before writing
that step.

- ⚠ **Step 1 (client & DI registration)** — the shipped `AddMaxioAdvancedBillingClient` registers a
  **singleton** that captures one `IHttpClientFactory`-created `HttpClient` for the process lifetime and
  snapshots your options at registration time. Whether that lifetime is right for an ASP.NET Core app (handler
  rotation, DNS refresh, whether the SDK wrapper should be transient over a named/typed client, and where a
  per-request timeout belongs) is exactly what the signature cannot tell you.
  **MUST load `dotnet-client-initialization`** before wiring it.
- ⚠ **Step 1 (credentials)** — when credentials must be set relative to client construction, and what happens to
  an already-constructed singleton when the API key rotates, is not visible in `BasicAuthCredentials`.
  **MUST load `dotnet-authentication`**.
- ⚠ **Step 1 (resilience)** — `options.Retry` governs whether a failed `POST /subscriptions` can be **re-sent**,
  which is precisely what your idempotency requirement turns on; `RetryOptions.Timeout` is also not the same
  thing as the timeout on the `HttpClient` you register, and it interacts with the `ct` you flow in. Do not
  reason about this from the option names. **MUST load `dotnet-configuration-resilience`** before wiring the
  client or choosing a policy for the write in step 7.
- ⚠ **Steps 2–8 (every call)** — most list operations have optional parameters with **no C# default**, so a
  positional call mis-binds silently instead of failing to compile, and the token parameter is named `ct`.
  **MUST load `dotnet-calling-endpoints`** before the first `client.…` call.
- ⚠ **Steps 3–8 (models)** — `StringEnum` is not a C# enum, `required` members change what a null check can
  protect you from, and JSON members the SDK does not model are dropped silently on deserialize (which is why
  the error payloads in §2.8 lose data). **MUST load `dotnet-models`** before mapping SDK models onto DTOs.
- ⚠ **Step 9 (error boundary)** — which exceptions actually reach a `catch`, and how to read a status code
  without destroying it, is the easiest thing here to get wrong (see the two `JsonException` rows in §4).
  **MUST load `dotnet-error-handling`** before writing any `try`/`catch`.
- ⚠ **Tests** — the `HttpClient` constructor argument is the seam; how to stub it without coupling tests to SDK
  internals is not derivable from the signature. **MUST load `dotnet-testing`** before writing tests for the
  integration layer.

---

## 4. REQUIRED READING (load **before implementation starts**)

This sheet deliberately does **not** carry these skills' contents — defaults, worked examples, and the parts you
must still wire yourself live in the skills, not here.

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, DI registration shape, `HttpClient` ownership/lifetime |
| `dotnet-authentication` | Step 1 — Basic credentials wiring, key rotation |
| `dotnet-configuration-resilience` | Step 1 — retries/backoff, timeouts, base-URL/server selection, pagination loops |
| `dotnet-calling-endpoints` | Steps 2–8 — named arguments, optional params without defaults, async/cancellation |
| `dotnet-models` | Steps 3–8 — request construction, `required` members, `StringEnum`, wire names |
| `dotnet-error-handling` | Step 9 — the catch ladder, status/body extraction, translation to HTTP results |
| `dotnet-testing` | Tests for the new library |

**Two hazards for the error boundary — `System.Text.Json.JsonException` reaches it from two directions and they
need opposite handling:**

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from
  deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the
  integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the
  `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a
  5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something
  that can never succeed.

Both are live in this scope, concretely: `ProductResponse.Product`, `CustomerResponse.Customer` and
`SiteResponse.Site` are `required` (direction 1); and `CreateSubscription`'s 422 is deserialized as
`ErrorListResponse1`, whose `Errors` is a **required `IReadOnlyList<string>`**, while sibling operations model
the same `errors` key as a dictionary or a single string (direction 2). **MUST load `dotnet-error-handling`**
before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions**

1. **Customer reference value.** The plan keys the Maxio customer on a stable per-user reference string, but
   *which* eShopOnWeb identity value that is (ASP.NET Identity user id vs. email) is an application decision —
   `YOUR CALL — not in the map`. The SDK fact forcing it: the reference must be **unique per site** and is the
   only exact-match lookup key (§2.6); an email is mutable in eShopOnWeb, a user id is not. I have not read your
   endpoint code, so I name no JWT claim here.
2. **`CreateCustomer` requires `FirstName`, `LastName` and `Email`** (all C# `required`). Whatever eShopOnWeb can
   supply for first/last name — it may hold only an email — must be mapped to non-null strings by your code; it
   will not compile otherwise.
3. Purchasable plans are assumed to be exactly the non-archived products of the configured family
   (`includeArchived: false`). If the family also holds products you do not want to sell, filtering them is
   `YOUR CALL — not in the map`.
4. Currency for `GET /api/subscription-plans` is assumed to be the **site** currency from `Sites.ReadSite()`.
   If plans can be multi-currency on your site, per-product currency needs the price-point operations
   (`ProductPricePoint.CurrencyPrices`, `map/operations/ProductPricePoints.md`) — not planned here; ask.
5. No caching layer is planned; caching the family-handle→id resolution and the plan list is
   `YOUR CALL — not in the map`.
6. The optional `Maxio:BaseUrl` is assumed to target the **Production** server group (every operation in scope
   is Production). Redirecting the Ebb/events group would be a separate override (`options.Server.Ebb.*`) and is
   out of scope.

**Blockers**

1. **A family handle cannot be resolved to an id in one call.** `ReadProductFamily(int id, …)` takes an `int`,
   so the API's documented `handle:my-family` form is unreachable through it. `ListProductsForProductFamily`
   does take `string productFamilyId`, but the SDK URL-escapes path parameters with `Uri.EscapeDataString`,
   which encodes `:` as `%3A`; whether Maxio's router accepts `handle%3Amy-family` there is `UNVERIFIED`. The
   plan therefore uses `ListProductFamilies` + client-side handle match (step 2) — and that operation has **no
   pagination parameters at all**. If the site has enough product families for the response to be truncated,
   the configured family may be unfindable and step 2 fails. Directive: fail step 2 with an explicit
   "configured product family handle not found" configuration error (never silently fall back to listing all
   products of the site), and confirm with Maxio that the family list is complete for your site before go-live.
   (Evidence: `operations/ProductFamilies.md`; `Core/TemplateParamsFactory.cs`.)
2. **True double-click safety cannot be achieved with SDK calls alone.** Uniqueness is documented only for the
   **customer** `reference`; nothing documents uniqueness for a subscription reference, `CreateSubscription` has
   no idempotency-key parameter, and the check-then-create across steps 6–7 is not atomic. Two simultaneous
   requests can still create two subscriptions. Closing that window requires an application-side mechanism (a
   per-user lock, or a uniqueness constraint in the app's own store) — that design is the implementer's, but the
   requirement "a double-click must never create two subscriptions" is not satisfiable until someone decides it.
3. **The published NuGet version is unresolved** (§2.1): the source tag `v1.0.2` this sheet describes ships a
   `.csproj` declaring `<Version>1.0.0</Version>`. Someone must confirm the exact version string on nuget.org
   and pin it before the first build; if the resolved package's surface differs from this sheet, the compiler is
   authoritative — bring me the error.
