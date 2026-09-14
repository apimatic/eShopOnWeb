# Maxio Advanced Billing — recurring subscriptions in eShopOnWeb (`src/PublicApi`)

Grounded against the bundled SDK map (package `AsadAli.AdvancedBilling.Sdk`, root namespace
`MaxioAdvancedBilling`, map stamp `v1.0.2` / commit `15db14b`) and, where the map stopped short,
against the SDK's own source files (named per row). Nothing below is from memory of this API.

---

## 1. Scope & sequence

| # | Step | Maxio operations used |
|---|---|---|
| 1 | Bind `Maxio:` settings (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl`) to an options type; validate `ApiKey`/`Subdomain` non-empty at startup. `BaseUrl` optional. | — |
| 2 | Register the SDK client in DI + auth + server selection (US host, subdomain or verbatim base URL). | — |
| 3 | `GET /api/subscription-plans`: resolve the configured **family handle → family id**, list that family's products, project to plan DTOs. Site currency read once. | `ProductFamilies.ListProductFamilies`, `ProductFamilies.ListProductsForProductFamily`, `Sites.ReadSite` |
| 4 | `POST /api/subscriptions` (a): ensure the Maxio customer for the caller — look up by reference, create only if absent. | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer` |
| 5 | `POST /api/subscriptions` (b/c/d): list that customer's subscriptions, match an "active-ish" one on the target product handle, otherwise create by product handle + customer id; project the result. | `Customers.ListCustomerSubscriptions`, `Subscriptions.CreateSubscription` |
| 6 | `GET /api/my-subscriptions`: reference → customer → subscriptions → same projection. No app-side persistence. | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |
| 7 | Integration error boundary (SDK exceptions → HTTP results). | — |
| 8 | Tests against the `HttpClient` seam (optional). | — |

Sequencing facts that drive the above:

- **Plan listing must go family-handle → id.** `ProductFamilies.ReadProductFamily(int id, …)` takes an
  `int`, so the documented `handle:my-family` addressing is not reachable through it. The list endpoint
  `ListProductsForProductFamily(string productFamilyId, …)` does take a string, but the SDK URL builder
  percent-escapes every path/template value (`Uri.EscapeDataString` in `Core/TemplateParamsFactory.cs`), so
  a `handle:my-family` value would go on the wire as `handle%3Amy-family`. Resolve the numeric id from
  `ListProductFamilies` by matching `ProductFamily.Handle` instead, then pass `Id.ToString()`.
- **`Product` carries no currency field** (see the record table in §2.3) — currency for plan display comes from
  `Sites.ReadSite` → `Site.Currency`; per-subscription currency comes from `Subscription.Currency`.
- Nothing in the plan writes to the eShopOnWeb DB; Maxio is the source of truth as required.

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

### 2.1 Namespaces (`using` directives you will need)

| Types | Namespace | Source |
|---|---|---|
| `MaxioAdvancedBillingClient`, `MaxioAdvancedBillingClientOptions`, `ServerOptions`, `AddMaxioAdvancedBillingClient` | `MaxioAdvancedBilling` | `sdk-map.md` |
| Controllers: `Customers`, `Subscriptions`, `Products`, `ProductFamilies`, `Sites`, `SubscriptionComponents` | `MaxioAdvancedBilling.Api` | `sdk-map.md` |
| Records: `Product`, `ProductResponse`, `ProductFamily`, `ProductFamilyResponse`, `Customer`, `CustomerResponse`, `CreateCustomer`, `CreateCustomerRequest`, `Subscription`, `SubscriptionResponse`, `CreateSubscription`, `CreateSubscriptionRequest`, `Site`, `SiteResponse`, `ErrorListResponse1`, `CustomerErrorResponse1`, `Errors`, `ListProductsFilter` | `MaxioAdvancedBilling.Models` | `sdk-map.md` |
| Enums: `SubscriptionState`, `IntervalUnit`, `ExpirationIntervalUnit`, `BasicDateField`, `ListProductsInclude`, `CollectionMethod`, `SortingDirection`, `SubscriptionStateFilter` | `MaxioAdvancedBilling.Models.Enums` | `enums.md` |
| Unions (only if you add usage): `SubscriptionIdOrReference`, `ComponentIdModel` | `MaxioAdvancedBilling.Models.AnyOf` | `unions.md` |
| Typed error classes: `CreateCustomerError`, `CreateSubscriptionError`, `ListProductsForProductFamilyError`, `UpdateCustomerError` | `MaxioAdvancedBilling.Errors` | `sdk-map.md` |
| `SdkException<TError>` | `MaxioAdvancedBilling.Core.Exceptions` | `sdk-map.md` |
| `RawError`, `ApiError` | `MaxioAdvancedBilling.Core.ErrorResponse` | `sdk-map.md` |
| `BasicAuthCredentials` | `MaxioAdvancedBilling.Core.Authentication.Basic` | `sdk-map.md` |
| `ServerEnvironment`, `ProductionOptions` (+ nested `ProductionOptions.UsOptions` / `.EuOptions`), `EbbOptions` | `MaxioAdvancedBilling.Servers` | `sdk-map.md`, `Servers/ProductionOptions.cs` |
| `RetryOptions`, `RetryAttempt` | `MaxioAdvancedBilling.Core.Configuration` | `sdk-map.md` |

### 2.2 Operations

| # | Controller property | Method signature (verbatim, params in order) | Request model + fields | Response envelope → payload path | Error case + accessors | Pagination | Source |
|---|---|---|---|---|---|---|---|
| O1 | `client.ProductFamilies` (`MaxioAdvancedBilling.Api.ProductFamilies`) | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 filters are **nullable with no C# default → must be passed explicitly** (`null` to skip) | none (query only) | `IReadOnlyList<ProductFamilyResponse>` → `.ProductFamily` (`ProductFamily?`, **nullable**) → `.Id (id): int?`, `.Handle (handle): string?`, `.Name (name): string?` | **Case B** — `SdkException<RawError>`; `ex.Error.StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | none | `operations/ProductFamilies.md`, `records-3-Of-Su.md` |
| O2 | `client.ProductFamilies` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 8 params `dateField`…`include` **must be passed explicitly**; `page`/`perPage` default to 1/20 | none (path + query). Wire names: `include_archived` ← `includeArchived`, `per_page` ← `perPage` | `IReadOnlyList<ProductResponse>` → `.Product` (`Product`, **`required` — non-null**) | **Case A** — `SdkException<ListProductsForProductFamilyError>`; `TryGetString(out string)` **[404]** · `TryGetRawError(out RawError)` [fallback] | manual `page` + `perPage` | `operations/ProductFamilies.md` |
| O3 | `client.Sites` (`MaxioAdvancedBilling.Api.Sites`) | `ReadSite(CancellationToken ct = default)` | none | `SiteResponse` → `.Site` (`Site`, **`required`**) → `.Currency (currency): string?`, `.Subdomain (subdomain): string?`, `.Test (test): bool?` | **Case B** — `SdkException<RawError>` | none | `operations/Sites.md`, `records-3-Of-Su.md` |
| O4 | `client.Customers` (`MaxioAdvancedBilling.Api.Customers`) | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — wire query `reference` ← `reference` | none | `CustomerResponse` → `.Customer` (`Customer`, **`required`**) → `.Id (id): int?`, `.Reference (reference): string?`, `.Email (email): string?` | **Case B** — `SdkException<RawError>`; read `ex.Error.StatusCode` to distinguish "absent" from a real failure | none | `operations/Customers.md`, `records-1-Ac-Cr.md` |
| O5 | `client.Customers` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` is nullable with **no default → pass it explicitly** | `CreateCustomerRequest { Customer (customer): CreateCustomer !req }`; `CreateCustomer` **required**: `FirstName (first_name): string`, `LastName (last_name): string`, `Email (email): string`. Optional but load-bearing here: `Reference (reference): string?` (the idempotency key — see the Notes table), `Organization (organization): string?`, `Locale (locale): string?` | `CustomerResponse` → `.Customer` → `.Id (id): int?` | **Case A** — `SdkException<CreateCustomerError>`; `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` **[422]** · `TryGetRawError(out RawError)` [fallback] | none | `operations/Customers.md`, `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md` |
| O6 | `client.Customers` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | none | `IReadOnlyList<SubscriptionResponse>` → `.Subscription` (`Subscription?`, **nullable — null-check every element**) | **Case B** — `SdkException<RawError>` | **none** (no `page`/`perPage` on this operation) | `operations/Customers.md`, `records-4-Su-We.md` |
| O7 | `client.Subscriptions` (`MaxioAdvancedBilling.Api.Subscriptions`) | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, **no default → pass explicitly** | `CreateSubscriptionRequest { Subscription (subscription): CreateSubscription !req }`. `CreateSubscription` marks **nothing** `required`; the fields this integration must set are `ProductHandle (product_handle): string?`, `CustomerId (customer_id): int?` (or `CustomerReference (customer_reference): string?`) — both named by the operation's Notes (see Notes table) — **and `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, which is what lets a subscription be created with no payment profile (§2.8 — confirmed live: without it the call is rejected 422 "No payment method was on file for the $X balance")**. All other members left unset — nulls are **not serialized** (`[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` on every property, `Models/CreateSubscription.cs`) | `SubscriptionResponse` → `.Subscription` (`Subscription?`, **nullable**) | **Case A** — `SdkException<CreateSubscriptionError>`; `TryGetErrorListResponse1(out ErrorListResponse1)` **[422]** · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md`, `records-2-Cr-Ne.md`, `records-4-Su-We.md` |
| O8 | `client.Subscriptions` | *(alternative to O6, not needed for this scope)* `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 14 params must be passed explicitly; **site-wide, not per-customer** | none | `IReadOnlyList<SubscriptionResponse>` | **Case B** — `SdkException<RawError>` | manual `page` + `perPage` | `operations/Subscriptions.md` |

**Notes rows the request bodies depend on** (provider prose from the map — these decide whether a call is
*accepted*, not merely well-formed):

| Operation | What the Notes bind you to | Fields I deliberately left unset | Source |
|---|---|---|---|
| `CreateCustomer` (O5) | "you may only create one customer for a given `reference` value… If provided, the `reference` value must be unique. It represents a unique identifier for the customer from your own app." Also: country must be ISO-3166-1 alpha-2 and state ISO-3166-2 **if you send them**. | `Address`/`City`/`State`/`Zip`/`Country`/`Phone` (omitted entirely, so no ISO validation applies), `TaxExempt`, `VatNumber`, `ParentId`, `SalesforceId`, `CcEmails` | `operations/Customers.md` |
| `CreateSubscription` (O7) | "Specify the product with `product_id` or `product_handle`… Identify an existing customer with `customer_id` or `customer_reference`… Payment information may be required to create a subscription, **depending on the options for the Product being subscribed**." A 3DS flow returns **422 with an `action_link`** — the same 422 channel the "No payment method was on file" rejection uses. **The Notes' "payment information" sentence points at the payment-*profile* members (`payment_profile_id`, `payment_profile_attributes`, `credit_card_attributes`) — it does NOT name the collection-method escape; that one is documented on the member itself (§2.8).** | `ProductPricePointHandle`/`ProductPricePointId` (Notes: the product's default price point is used), `PaymentProfileId`, `CreditCardAttributes`, `PaymentProfileAttributes`, `BankAccountAttributes`, `CustomerAttributes` (we always pass an existing `CustomerId`), `CouponCode(s)`, `Currency`, `Components`, `CalendarBilling`, `NextBillingAt`, `InitialBillingAt`, `Ref`/`Reference`, `OfferId`, `DeferSignup`, `NetTerms` — the last five are re-examined in §2.8 | `operations/Subscriptions.md`, `Models/CreateSubscription.cs` |
| `ListProductsForProductFamily` (O2) | Plain list of a family's products; pass `includeArchived: false` (or `null`) and additionally filter `Product.ArchivedAt` defensively so archived plans never reach the endpoint's output. | `filter`, all date params, `include` | `operations/ProductFamilies.md` |

### 2.3 Models — the exact fields this integration reads/writes

`Product` (namespace `MaxioAdvancedBilling.Models`, source `Models/Product.cs`, map page `records-3-Of-Su.md`) —
every member below is optional/nullable in the generated record:

| C# property | Wire name | Type | Used for |
|---|---|---|---|
| `Id` | `id` | `int?` | internal only (unstable in sandbox — never expose) |
| `Handle` | `handle` | `string?` | plan handle (the stable key; the `productHandle` in the POST body) |
| `Name` | `name` | `string?` | plan name |
| `Description` | `description` | `string?` | plan description |
| `PriceInCents` | `price_in_cents` | `long?` | price (cents → display) |
| `Interval` | `interval` | `int?` | billing interval count |
| `IntervalUnit` | `interval_unit` | `IntervalUnit?` | billing interval unit (`Day`/`Month`) |
| `TrialPriceInCents` | `trial_price_in_cents` | `long?` | trial info |
| `TrialInterval` | `trial_interval` | `int?` | trial info |
| `TrialIntervalUnit` | `trial_interval_unit` | `IntervalUnit?` | trial info |
| `InitialChargeInCents` | `initial_charge_in_cents` | `long?` | setup fee (expect 0/null for the seeded plans) |
| `InitialChargeAfterTrial` | `initial_charge_after_trial` | `bool?` | setup-fee timing |
| `RequireCreditCard` | `require_credit_card` | `bool?` | **pre-flight for step 5**: a true value means `CreateSubscription` needs payment data |
| `RequestCreditCard` | `request_credit_card` | `bool?` | same family of flags (distinct wire field — do not conflate) |
| `ArchivedAt` | `archived_at` | `DateTimeOffset?` | filter archived plans out |
| `ProductFamily` | `product_family` | `ProductFamily?` | sanity-check the family on each product |
| `ExpirationInterval` / `ExpirationIntervalUnit` | `expiration_interval` / `expiration_interval_unit` | `int?` / `ExpirationIntervalUnit?` | not used |

There is **no** `currency` member on `Product` — take currency from `Sites.ReadSite` → `Site.Currency`
(`records-3-Of-Su.md`).

`Subscription` (source `Models/Subscription.cs`, map page `records-4-Su-We.md`) — the members this integration reads:

| C# property | Wire name | Type | Used for |
|---|---|---|---|
| `Id` | `id` | `int?` | subscription id in the response |
| `State` | `state` | `SubscriptionState?` | state + the "active-ish" test |
| `CurrentPeriodEndsAt` | `current_period_ends_at` | `DateTimeOffset?` | current period end |
| `NextAssessmentAt` | `next_assessment_at` | `DateTimeOffset?` | next billing date |
| `CurrentPeriodStartedAt` | `current_period_started_at` | `DateTimeOffset?` | period start (optional in output) |
| `TrialStartedAt` / `TrialEndedAt` | `trial_started_at` / `trial_ended_at` | `DateTimeOffset?` | trial state |
| `Product` | `product` | `Product?` | plan name (`.Name`), handle (`.Handle`), `PriceInCents` |
| `ProductPriceInCents` | `product_price_in_cents` | `long?` | price at subscription level |
| `CurrentBillingAmountInCents` | `current_billing_amount_in_cents` | `long?` | next charge amount |
| `Currency` | `currency` | `string?` | per-subscription currency |
| `Customer` | `customer` | `Customer?` | `.Id`, `.Reference` (customer id in the response) |
| `CanceledAt`, `CancelAtEndOfPeriod`, `ExpiresAt` | `canceled_at`, `cancel_at_end_of_period`, `expires_at` | `DateTimeOffset?` / `bool?` | optional lifecycle detail |

`Customer` (source `Models/Customer.cs`, `records-1-Ac-Cr.md`): `Id (id): int?`, `Reference (reference): string?`,
`Email (email): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?`,
`Organization (organization): string?` — all optional/nullable.

Envelopes, exactly (a read that forgets the wrapper will not compile — and the nullability differs per type):

| Envelope | Single member | Nullability | Source |
|---|---|---|---|
| `ProductResponse` | `Product (product): Product` | **`required`, non-null** | `records-3-Of-Su.md` |
| `ProductFamilyResponse` | `ProductFamily (product_family): ProductFamily?` | nullable | `records-3-Of-Su.md` |
| `CustomerResponse` | `Customer (customer): Customer` | **`required`, non-null** | `records-1-Ac-Cr.md` |
| `SubscriptionResponse` | `Subscription (subscription): Subscription?` | **nullable — check before dereferencing** | `records-4-Su-We.md` |
| `SiteResponse` | `Site (site): Site` | **`required`, non-null** | `records-3-Of-Su.md` |
| `UsageResponse` | `Usage (usage): Usage` | **`required`, non-null** | `records-4-Su-We.md` |

### 2.4 Enums (`MaxioAdvancedBilling.Models.Enums`) — literal member names

These are **not** C# enums (`StringEnum<T>` records). Compare with `==` (record equality over the underlying
`Value`), read `.Value` (`string`) or `.ToString()` for the wire value, and build from a string with
`SubscriptionState.FromValue("active")`. An unknown wire value deserializes into an instance carrying that
unknown string rather than failing; `IsKnownValue()` tells the two apart (`Core/Enum/TypedEnum.cs`,
`Core/Enum/StringEnum.cs`) — so a `switch` over states needs a real default branch.

`SubscriptionState` (member names/wire values: `enums.md`; the Live/Problem/End-of-Life classification is the
SDK's own doc comment in `Models/Enums/SubscriptionState.cs`):

| C# member | Wire value | Class per SDK doc comment |
|---|---|---|
| `SubscriptionState.Active` | `active` | Live |
| `SubscriptionState.Trialing` | `trialing` | Live |
| `SubscriptionState.Assessing` | `assessing` | Live (transient — doc says do not gate access on it) |
| `SubscriptionState.Pending` | `pending` | Live (transient) |
| `SubscriptionState.Paused` | `paused` | Live |
| `SubscriptionState.PastDue` | `past_due` | Problem |
| `SubscriptionState.SoftFailure` | `soft_failure` | Problem |
| `SubscriptionState.Unpaid` | `unpaid` | Problem |
| `SubscriptionState.Canceled` | `canceled` | End of Life |
| `SubscriptionState.Expired` | `expired` | End of Life |
| `SubscriptionState.FailedToCreate` | `failed_to_create` | End of Life |
| `SubscriptionState.OnHold` | `on_hold` | End of Life |
| `SubscriptionState.Suspended` | `suspended` | End of Life |
| `SubscriptionState.TrialEnded` | `trial_ended` | End of Life |
| `SubscriptionState.AwaitingSignup` | `awaiting_signup` | **not classified by the doc comment** — see the `UNVERIFIED` row in §2.6 |

**"Active-ish" set for the idempotency check (step 5)** = Live ∪ Problem ∪ `AwaitingSignup`:
`Active`, `Trialing`, `Assessing`, `Pending`, `Paused`, `PastDue`, `SoftFailure`, `Unpaid`, `AwaitingSignup`.
Everything else (`Canceled`, `Expired`, `FailedToCreate`, `OnHold`, `Suspended`, `TrialEnded`) is End of Life
and must **not** block a new subscription.

`IntervalUnit`: `IntervalUnit.Day` (`day`), `IntervalUnit.Month` (`month`). `ExpirationIntervalUnit`:
`Day` (`day`), `Month` (`month`), `Never` (`never`). `BasicDateField`: `UpdatedAt` (`updated_at`),
`CreatedAt` (`created_at`). `ListProductsInclude`: `PrepaidProductPricePoint` (`prepaid_product_price_point`).
`SortingDirection`: `Asc` (`asc`), `Desc` (`desc`). `CollectionMethod`: `Automatic` (`automatic`),
`Remittance` (`remittance`), `Prepaid` (`prepaid`), `Invoice` (`invoice`). Source: `enums.md`.

### 2.5 Client construction, auth, server node, DI

The map's *Getting a client* / *Servers & auth* sections give the option names and the override **path
expression**; the member types, defaults and DI lifetime below were read from the four SDK source files named
in the rows.

| Fact | Exact contract | Source |
|---|---|---|
| Only constructor | `new MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` (namespace `MaxioAdvancedBilling`) | `sdk-map.md`, `MaxioAdvancedBillingClient.cs` |
| Options type | `MaxioAdvancedBillingClientOptions` — plain class, all members settable, **no `required` members**: `Environment: ServerEnvironment = ServerEnvironment.Default()`, `Retry: RetryOptions = RetryOptions.Default()`, `Server: ServerOptions = new()`, `BasicAuth: BasicAuthCredentials?` (the only one left unset) | `sdk-map.md`; `MaxioAdvancedBillingClientOptions.cs` |
| Auth | `options.BasicAuth = new BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }`. Both members are C# `required` and `init`-only, so both must appear in the initializer. **Username = API key, Password = the literal `"x"`.** Namespace `MaxioAdvancedBilling.Core.Authentication.Basic` | `sdk-map.md`; `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Server node | `options.Environment = ServerEnvironment.Us` (namespace `MaxioAdvancedBilling.Servers`; `Us` is also the default). `Eu` only for EU-hosted accounts | `sdk-map.md`, `Servers/ServerEnvironment.cs` |
| Subdomain (derived host) | `options.Server.Production.Us.Site = <Maxio:Subdomain>`. `options.Server` is `ServerOptions` (namespace **`MaxioAdvancedBilling`**, not `.Servers`); `.Production` is `ProductionOptions` (namespace `MaxioAdvancedBilling.Servers`); `.Us` is the nested `ProductionOptions.UsOptions` with `BaseUrl: string = "https://{site}.chargify.com"` and `Site: string = "subdomain"`. Result: `https://<subdomain>.chargify.com` | `sdk-map.md`; `ServerOptions.cs`, `Servers/ProductionOptions.cs` |
| **Verbatim base-URL override — supported** | `options.Server.Production.Us.BaseUrl = <Maxio:BaseUrl>`. The base URL is expanded by a literal `string.Replace("{site}", …)` and its trailing `/` trimmed (`Core/TemplateParamsFactory.cs`), so a value containing **no** `{site}` placeholder is used exactly as given and `Site` becomes irrelevant — no exception, no validation. A configured value that *does* contain `{site}` is still substituted from `Site`. **Not a blocker: set `BaseUrl` when `Maxio:BaseUrl` is present, otherwise set `Site` from `Maxio:Subdomain`.** | `Servers/ProductionOptions.cs`, `Core/TemplateParamsFactory.cs` |
| Scope of the override | `options.Server.Production.*` covers every operation in this plan (all rows in §2.2 are on the **Production** group). The separate `options.Server.Ebb.*` group is used only by the `SubscriptionComponents` event-ingest endpoints — `CreateUsage` is **not** one of them (it is Production) | `sdk-map.md`, `operations/SubscriptionComponents.md` |
| DI helper | `services.AddMaxioAdvancedBillingClient(o => { … })` — an extension member on `IServiceCollection` in namespace `MaxioAdvancedBilling` (`ServiceCollectionExtensions`). It (1) invokes your `configure` delegate **once, eagerly, at registration time** on a fresh options instance, (2) calls `services.AddHttpClient()`, (3) registers `MaxioAdvancedBillingClient` as a **singleton** built from `IHttpClientFactory.CreateClient()` (the default, unnamed client) on first resolve. Consequence: the options snapshot is fixed at registration — bind `IConfiguration` there; `IOptionsMonitor`-style reloads never reach the client, and the singleton client holds one `HttpClient` for the app's lifetime | `sdk-map.md`; `ServiceCollectionExtensions.cs` |
| Retry/timeout knobs | `options.Retry` is `RetryOptions` (namespace `MaxioAdvancedBilling.Core.Configuration`, source `Core/Configuration/RetryOptions.cs`); members `StatusCodesToRetry: IReadOnlyList<HttpStatusCode>`, `HttpMethodsToRetry: IReadOnlyList<HttpMethod>`, `MaxRetries: int`, `Delay: TimeSpan`, `Timeout: TimeSpan?`, `BackOffFactor: int`, `UseExponentialBackoff: bool`, `MaxJitter: TimeSpan`, `OnRetry: Action<RetryAttempt>?`. **All members are `required`** — start from `RetryOptions.Default()` and use a `with` expression, or set every member. What these actually bound is a trap, not a fact to copy — see §3 | `sdk-map.md` |
| Success/error boundary | Anything outside **200–299** is an error and throws; 2xx is deserialized into the response type | `Core/RawClient.cs`, `Core/HttpStatusPolicy.cs` |

Configuration binding keys (exactly as dictated; **no environment-variable names appear in code** — which
provider supplies the values is a deployment concern):

| Binding key | Required | SDK member it feeds | Default if absent |
|---|---|---|---|
| `Maxio:ApiKey` | yes | `BasicAuthCredentials.Username` (password is the literal `"x"`) | none — fail startup |
| `Maxio:Subdomain` | yes (unless `Maxio:BaseUrl` is set) | `options.Server.Production.Us.Site` | the SDK default is the literal string `"subdomain"`, yielding `https://subdomain.chargify.com` — never usable, so validate |
| `Maxio:BaseUrl` | no | `options.Server.Production.Us.BaseUrl` (verbatim) | SDK default `https://{site}.chargify.com` |
| `Maxio:ProductFamilyHandle` | yes | matched against `ProductFamily.Handle` (O1) | none — fail startup |
| default plan handle | — | `CreateSubscription.ProductHandle` when the request body omits `productHandle` | `YOUR CALL — not in the map` (the brief calls it "a configured/default plan handle"; it is **not** one of the four dictated keys, so either add a key or fall back to the family's single product) |

### 2.6 Error boundary — exact types and how to read them

`SdkException<TError>` (namespace `MaxioAdvancedBilling.Core.Exceptions`) is `sealed` and exposes **exactly one
member: `Error` of type `TError`**. It has **no** `StatusCode` property — the status is reachable only through
the error payload (`Core/Exceptions/SdkException.cs`).

| Operation | Catch | Read status / body |
|---|---|---|
| O1, O3, O4, O6 (+O8) | `catch (SdkException<RawError> ex)` | `ex.Error.StatusCode` (`HttpStatusCode`), `ex.Error.ReadAsString()`, `ex.Error.ReadAsJson<T>()`, `ex.Error.ReadAsBytes()` |
| O2 | `catch (SdkException<ListProductsForProductFamilyError> ex)` | `ex.Error.TryGetString(out var msg)` → **404**; else `ex.Error.TryGetRawError(out var raw)` → `raw.StatusCode` |
| O5 | `catch (SdkException<CreateCustomerError> ex)` | `ex.Error.TryGetCustomerErrorResponse1(out var e422)` → **422**; else `ex.Error.TryGetRawError(out var raw)` → `raw.StatusCode` |
| O7 | `catch (SdkException<CreateSubscriptionError> ex)` | `ex.Error.TryGetErrorListResponse1(out var e422)` → **422**, `e422.Errors` is `IReadOnlyList<string>` (**`required`**); else `ex.Error.TryGetRawError(out var raw)` → `raw.StatusCode` |

**The two accessors on a typed error are mutually exclusive.** Each generated error class is built by a status
switch (`422 => FromJson<…>(…)`, `_ => FromRawBody(…)`) and the branch that fills the typed payload passes
`default` for the raw fallback (`Errors/CreateSubscriptionError.cs`, `Errors/CreateCustomerError.cs`). So on a
422 `TryGetRawError` returns **false**, and neither the status code nor the raw body can be recovered from the
exception — the accessor that succeeds *is* the status signal. Do not write `TryGetRawError` as a catch-all.

| Typed 422 payload | Shape | Consequence |
|---|---|---|
| `ErrorListResponse1` (O7) | `Errors (errors): IReadOnlyList<string>` **`required`** | Usable messages. But `required` means a differently shaped 422 body throws `JsonException` *instead of* the `SdkException` — see §4 |
| `CustomerErrorResponse1` (O5) | `Errors (errors): Errors?`, where `Errors` = `{ PerPage (per_page): IReadOnlyList<string>?, PricePoint (price_point): IReadOnlyList<string>? }` | The generated payload for a **customer** 422 carries only `per_page` and `price_point` — a pagination/price-point shape reused verbatim as the customer error model (`Models/Errors.cs`). Both members are optional, so it deserializes without throwing and simply yields `null`s: **the 422 detail is silently unavailable.** See the `UNVERIFIED` row below |

| Uncertainty | Label | Directive |
|---|---|---|
| Whether a `CreateCustomer` 422 body actually matches `CustomerErrorResponse1`/`Errors` (`per_page`, `price_point`). The generated model is a suspicious shared shape, and no map page or source file settles what the wire sends | `UNVERIFIED` | Defensive coding: extract best-effort from `e422.Errors?.PerPage` / `.PricePoint`, and when both are null fall back to the generic message ("customer could not be created — a customer with this reference may already exist") for whatever status you map. Never assume the accessor yields text |
| Whether a `CreateSubscription` 422 body is always `{"errors":[ …strings… ]}` (what `ErrorListResponse1` requires) rather than an object or other shape | `UNVERIFIED` | Defensive coding: place a `catch (JsonException)` on this call **before** the generic catch (see §4) and fall back to the generic message; never let a shape mismatch surface as an outage |
| Whether `ReadCustomerByReference` answers **404** (vs. 200 with an empty body) for an unknown reference | **RESOLVED live on site `cp-exp-2`** — 404 is the miss signal | Keep the coded behaviour: `SdkException<RawError>` with `StatusCode == HttpStatusCode.NotFound` ⇒ "absent → create"; **rethrow every other status**; treat a `JsonException` from this call as "cannot determine" and fail rather than creating a duplicate customer |
| Whether `awaiting_signup` counts as active-ish (the SDK's state doc comment classifies every other member but omits this one) | `UNVERIFIED` | Count it as active-ish for the idempotency check (a just-created, not-yet-activated subscription must not be duplicated), and never treat it as "entitled" for access decisions |
| Whether the seeded plans really require no payment method | **DISPROVED live** — `RequireCreditCard == false` **and** `RequestCreditCard == false` on both plans, yet `CreateSubscription` was still rejected 422 `["No payment method was on file for the $299.00 balance"]`. The two flags govern whether the *signup form* requests/requires a card; they do **not** predict whether signup produces a balance the provider tries to collect | The `Product`-level pre-check is **not** a sufficient guard — replace it per §2.8. Keep at most `RequireCreditCard == true ⇒ refuse` as a cheap early-out, and rely on `PaymentCollectionMethod` for the actual fix |

### 2.7 Application-side decisions (not SDK contract)

| Decision | Where it lands | Source |
|---|---|---|
| Caller identity → the Maxio customer `reference` value (must be stable across restarts and unique per user) | resolve from the app's own identity path; it must be a `string`, unique per customer | `YOUR CALL — not in the map` |
| `CreateCustomer.FirstName` / `LastName` when the identity carries only an email (both are `required` in the model, so *something* must be sent) | app decides the derivation | `YOUR CALL — not in the map` (the `required` flag itself: `records-2-Cr-Ne.md`) |
| Price formatting from cents (culture, symbol) and the DTO shape of the three endpoints | app | `YOUR CALL — not in the map` |
| Protecting the ensure-customer / ensure-subscription pair against concurrent double-clicks beyond the read-then-write check | app concurrency control | `YOUR CALL — not in the map` |
| Which HTTP status each Maxio failure maps to at the endpoint boundary | app | `YOUR CALL — not in the map` |

**Idempotency, as far as the SDK contract carries it:**

- *Customer*: `reference` uniqueness is enforced by the provider (`CreateCustomer` Notes, `operations/Customers.md`),
  so the read-then-create race resolves itself: on `SdkException<CreateCustomerError>` whose 422 accessor
  succeeds, re-run `ReadCustomerByReference` and use the winner. That recovery is required, not optional.
- *Subscription*: **the map documents no uniqueness constraint and no idempotency key on `CreateSubscription`**
  (`operations/Subscriptions.md`, `records-2-Cr-Ne.md`) — two accepted calls create two subscriptions. The only
  contract-level guard is the pre-check against `ListCustomerSubscriptions` (O6), filtered to the target
  `Product.Handle` and the active-ish state set. That check is read-then-write and therefore not atomic; closing
  the remaining window is the application's call.

### 2.8 Enrolling with **no payment profile** — the exact contract

Triggered by the live 422 `["No payment method was on file for the $299.00 balance"]` on a body of
`{ ProductHandle, CustomerId }` only. The SDK **does** expose a way to create the subscription without a
payment profile; it is a member of `CreateSubscription`, not an operation parameter.

**The member (answer to Q1):**

| C# member | Wire name | Type | Namespace of the type | Source |
|---|---|---|---|---|
| `PaymentCollectionMethod` | `payment_collection_method` | `CollectionMethod?` | `MaxioAdvancedBilling.Models.Enums` | `records-2-Cr-Ne.md`, `Models/CreateSubscription.cs` |

Its own doc comment (`Models/CreateSubscription.cs`), verbatim: *"The type of payment collection to be used in
the subscription. For legacy Statements Architecture valid options are `invoice`, `automatic`. For current
Relationship Invoicing Architecture valid options are `remittance`, `automatic`, `prepaid`."* — identical text
to the `CollectionMethod` enum summary in `enums.md`.

`CollectionMethod` — **every** member, name and wire value (`enums.md`, `Models/Enums/CollectionMethod.cs`):

| C# member | Wire value | Valid on which architecture (per the doc comment) |
|---|---|---|
| `CollectionMethod.Automatic` | `automatic` | both — this is the card-charging mode that produced the 422 |
| `CollectionMethod.Remittance` | `remittance` | **Relationship Invoicing only** — invoice the customer |
| `CollectionMethod.Prepaid` | `prepaid` | Relationship Invoicing only |
| `CollectionMethod.Invoice` | `invoice` | **legacy Statements Architecture only** — invoice the customer |

**Which of `Remittance` / `Invoice` this site takes is decided by a field you already read:**
`Sites.ReadSite` → `Site.RelationshipInvoicingEnabled (relationship_invoicing_enabled): bool?`
(`records-3-Of-Su.md`). `true` ⇒ send `CollectionMethod.Remittance`; `false`/null ⇒ send
`CollectionMethod.Invoice`. Select it from that flag rather than hard-coding, and cache it beside the currency
you already read from the same call.

**Other members bearing on the same rejection (answer to Q2)** — all on `CreateSubscription`, doc comments
verbatim from `Models/CreateSubscription.cs`:

| C# member | Wire name | Type | What its doc comment says | Bearing |
|---|---|---|---|---|
| `PaymentCollectionMethod` | `payment_collection_method` | `CollectionMethod?` | see above | **The one to use.** Changes *how* the balance is collected — invoice instead of card — so no payment profile is needed |
| `NetTerms` | `net_terms` | `string?` (**string, not int**, on the request; `Subscription.NetTerms` on the response is `int?`) | "(Optional) Default: null The number of days after renewal (**on invoice billing**) that a subscription is due. A value between 0 (due immediately) and 180." | Companion to invoice/remittance collection — sets the due date of the invoice. Optional; omit unless you want terms |
| `NextBillingAt` | `next_billing_at` | `DateTimeOffset?` | "If you provide a next_billing_at timestamp that is in the future, **no trial or initial charges will be applied** when you create the subscription. In fact, **no payment will be captured at all.** The first payment will be captured… near the time specified by next_billing_at… If the card cannot be successfully charged, the subscription will not be created." | Would also dodge the immediate charge, but it *defers* rather than *invoices*, and the same doc says the later capture still needs a card. Import-oriented ("sync imported subscriptions to your existing renewal schedule"). Not the fix |
| `InitialBillingAt` | `initial_billing_at` | `DateTimeOffset?` | "Set this attribute to a future date/time to create a subscription in the **Awaiting Signup** state, rather than Active or Trialing… When the initial_billing_at date hits, the subscription will transition… **If the payment is due at the initial_billing_at and it fails the subscription will be immediately canceled.**" | Postpones the problem and adds a state you must handle; the charge still needs a card later. Not the fix |
| `DeferSignup` | `defer_signup` | `bool?` (**default `false`, and this member is NOT `[JsonIgnore(WhenWritingNull)]` — `"defer_signup": false` is on the wire of every request you send**) | "Set this attribute to true to create the subscription in the **Awaiting Signup Date** state. Use this when you want to create a subscription that has an unknown first billing date." | Same objection as `InitialBillingAt`: defers, does not invoice |
| `PaymentProfileId` / `PaymentProfileAttributes` / `CreditCardAttributes` / `BankAccountAttributes` | `payment_profile_id` / `payment_profile_attributes` / `credit_card_attributes` / `bank_account_attributes` | `int?` / `PaymentProfileAttributes?` / `PaymentProfileAttributes?` / `BankAccountAttributes?` | `payment_profile_id`: "The Payment Profile ID of an existing card or bank account… If [it] does not exist already… use `payment_profile_attributes` instead" | These *satisfy* the rejection by supplying a payment method — the opposite of the requirement. Out of scope (the brief says no card capture) |
| `CalendarBillingFirstCharge` | `calendar_billing_first_charge` | `string?` | "One of 'prorated' (the default…), 'immediate'…, or 'delayed' (the full product price will be charged with the first scheduled renewal)." | Only applies to calendar-billing products; still a *charge*. Not the fix |

**Which one the Notes tie to the rejection:** the operation's Notes sentence you quoted ("Payment information
may be required… depending on the options for the Product being subscribed. See the Payments Profile endpoint
for details on payment parameters") points at the payment-*profile* members in the row above — i.e. the Notes
only describe *supplying* a payment method. The invoice/remittance escape is documented on
`payment_collection_method`'s own doc comment and on the `CollectionMethod` enum, not in the operation Notes.
So: **use `PaymentCollectionMethod`; the others are deferral, not collection-mode, and none of them removes the
need for a card.**

Body to send (all four members; nulls elsewhere are not serialized):

`new CreateSubscriptionRequest { Subscription = new CreateSubscription { ProductHandle = <handle>, CustomerId = <id>, PaymentCollectionMethod = <Remittance | Invoice per Site.RelationshipInvoicingEnabled> } }`

**Response shape and states (answer to Q3).** The return type is unchanged: `SubscriptionResponse` →
`.Subscription` (`Subscription?`, nullable) — `PaymentCollectionMethod` is a request member, not an operation
switch, so no envelope, error case, or accessor changes. Read back
`Subscription.PaymentCollectionMethod (payment_collection_method): CollectionMethod?` to confirm what the
provider recorded.

| Uncertainty | Label | Directive |
|---|---|---|
| Which `SubscriptionState` an invoice/remittance signup lands in (`active` immediately, vs. a problem state while the first invoice is outstanding). Neither the map nor the source states this — only live traffic can | `UNVERIFIED` | **Do not narrow the active-ish set.** §2.4's set already contains every state this can plausibly produce (`Active`, `Trialing`, `Pending`, `Assessing`, `Paused`, `PastDue`, `SoftFailure`, `Unpaid`, `AwaitingSignup`), so the idempotency check stays correct either way. Read `Subscription.State` from the create response and echo it rather than assuming `active`; never assert `state == Active` in code or tests. If you later add `InitialBillingAt`/`DeferSignup`, `AwaitingSignup` becomes the *expected* state — it is already in the set (its inclusion is the `UNVERIFIED` row in §2.6, and this is the second reason to keep it) |
| Whether an unpaid first invoice eventually moves the subscription to `PastDue`/`Unpaid` and what that should mean for entitlement | `UNVERIFIED` | Entitlement policy is the application's; expose `State` in the endpoint's response so the caller can decide, and do not treat "created" as "paid" |

**Pre-check (answer to Q4) — the map exposes no field that predicts this.** I checked every member of
`Product` (`records-3-Of-Su.md`), `ProductFamily` (`records-3-Of-Su.md`) and `ProductPricePoint`
(`records-3-Of-Su.md`). The only payment-method-shaped fields anywhere on them are
`Product.RequireCreditCard (require_credit_card): bool?` and `Product.RequestCreditCard
(request_credit_card): bool?` — both `false` on your plans, and both disproved by the live 422. There is **no**
"payment method optional", "collection method" or "invoiceable" flag on the product, the family, or the price
point. Plainly: **the map exposes no product-side predictor; drop the pre-check as a correctness guard.**

Two grounded substitutes, in order of cost:

1. **Just set `PaymentCollectionMethod`** — then a nonzero signup balance is invoiced rather than charged and
   the pre-check has nothing to predict. Keep `RequireCreditCard == true ⇒ refuse` only as a cheap early-out.
2. **Ask the provider what the signup would cost**, if you want the balance before committing:
   `client.Subscriptions.PreviewSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)`
   → `SubscriptionPreviewResponse` → `.SubscriptionPreview` (**`required`**) →
   `.CurrentBillingManifest (current_billing_manifest): BillingManifest?` →
   `.TotalInCents (total_in_cents): long?`, `.SubtotalInCents`, `.TotalTaxInCents`, `.TotalDiscountInCents`,
   `.ExistingBalanceInCents`, `.LineItems (line_items): IReadOnlyList<BillingManifestItem>?`. Takes the **same
   request body type** as `CreateSubscription`, creates nothing, and is **Case B**
   (`SdkException<RawError>`). Its Notes: *"A subscription will not be created by utilizing this endpoint; it
   is meant to serve as a prediction… You do not need to include a card number to generate tax information
   when you are previewing a subscription."* Source: `operations/Subscriptions.md`, `records-3-Of-Su.md`,
   `records-1-Ac-Cr.md`. Cost: one extra round trip per POST — optional, not required by the fix.

Also site-level, for context rather than for gating: `Site.DefaultPaymentCollectionMethod
(default_payment_collection_method): string?` (a raw `string`, **not** the `CollectionMethod` enum) —
`records-3-Of-Su.md`. It reports the site default that applied when you sent no
`payment_collection_method`; do not rely on it instead of setting the member explicitly.

### 2.9 Secondary — metered `api-call` usage (NOT in scope; cheap if added later)

Yes, it is one call: `client.SubscriptionComponents.CreateUsage(SubscriptionIdOrReference
subscriptionIdOrReference, ComponentIdModel componentId, CreateUsageRequest? body, CancellationToken ct =
default)` → `UsageResponse` → `.Usage` (**`required`**). **Case A**: `SdkException<CreateUsageError>` with
`TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. Body:
`CreateUsageRequest { Usage (usage): CreateUsage !req }`; `CreateUsage { Quantity (quantity): double?,
PricePointId (price_point_id): string?, Memo (memo): string?, BillingSchedule (billing_schedule): BillingSchedule?,
CustomPrice (custom_price): ComponentCustomPrice? }` — nothing inside `CreateUsage` is `required`. The first two
parameters are AnyOf unions (namespace `MaxioAdvancedBilling.Models.AnyOf`), built by factory or implicit
conversion: `SubscriptionIdOrReference.Int(int)` / `.String(string)`, `ComponentIdModel.Int(int)` /
`.String(string)` — so the component can be addressed by its `api-call` handle rather than a numeric id. Notes:
one component per call; a negative `quantity` deducts; omitting the price point uses the component's default.
Source: `operations/SubscriptionComponents.md`, `records-1-Ac-Cr.md`, `records-4-Su-We.md`, `unions.md`.

---

## 3. Trap notes

> ⚠ **Step 2 (client registration / HttpClient ownership)** — the DI helper registers a **singleton** client over
> one `IHttpClientFactory`-created `HttpClient`; whether that is the right lifetime and handler-rotation story for
> a long-running ASP.NET Core app, and what you must own yourself if you construct the client manually instead, is
> not something the signature settles. **MUST load `dotnet-client-initialization`** before wiring the client.

> ⚠ **Step 2 (credentials)** — *when* credentials must be set relative to construction, and how to keep the API key
> out of source and logs while still failing fast on a missing key, is a usage concern the `BasicAuthCredentials`
> shape does not express. **MUST load `dotnet-authentication`** before setting `options.BasicAuth`.

> ⚠ **Step 2 (retries/timeouts)** — the SDK's retry/timeout options do **not** bound a whole call and are **not** the
> timeout on the `HttpClient` the DI helper hands the client; whether a failed `POST /subscriptions.json` can be
> re-sent — and therefore whether "create subscription" can execute more than once from a single call — is decided
> by these knobs and by transport-failure behaviour, not by your code. **MUST load
> `dotnet-configuration-resilience`** before wiring the client or tuning `options.Retry`.

> ⚠ **Steps 3, 5, 6 (making the calls)** — every list operation in §2.2 has a long run of nullable parameters with
> **no C# default**, so a positional call silently mis-binds, and the cancellation parameter is literally `ct`. How
> to call and cancel these safely is the companion's subject. **MUST load `dotnet-calling-endpoints`** before the
> first `client.*` call.

> ⚠ **Steps 3, 5 (models)** — `SubscriptionState` / `IntervalUnit` are `StringEnum<T>` records rather than C# enums,
> `CreateUsage`'s first two parameters are unions, and JSON fields the generated records do not model are dropped on
> deserialize; the consequences for comparison, exhaustive `switch` and round-tripping are the companion's subject.
> **MUST load `dotnet-models`** before building request bodies or mapping responses onto your DTOs.

> ⚠ **Step 5 (write path)** — `CreateSubscription` is a non-idempotent write with no provider-side uniqueness and no
> idempotency key (§2.7); whether one call can reach the provider twice is decided at the resilience layer, not
> here. **MUST load `dotnet-configuration-resilience`** before relying on the pre-check as the only duplicate guard.

> ⚠ **Step 7 (error boundary)** — which exception types actually reach your `catch`, and how to read a status safely
> when `SdkException<T>` has no status member and the typed accessors are mutually exclusive, is exactly what the
> companion covers. **MUST load `dotnet-error-handling`** before writing any `try`/`catch` around an SDK call.

> ⚠ **Step 8 (tests)** — which seam to fake and how to avoid asserting on SDK internals. **MUST load
> `dotnet-testing`** before stubbing the SDK.

---

## 4. REQUIRED READING — load **before implementation starts**

This sheet deliberately does **not** carry these skills' contents (defaults, semantics, worked examples, and the
parts you must still wire yourself). Load each at the step it governs:

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 2 — client construction, DI registration, `HttpClient` ownership/lifetime |
| `dotnet-authentication` | Step 2 — supplying the Basic credentials and keeping the key out of source |
| `dotnet-configuration-resilience` | Steps 2 and 5 — retries, timeouts, base-URL/server selection, pagination |
| `dotnet-calling-endpoints` | Steps 3, 5, 6 — calling operations, optional parameters, cancellation |
| `dotnet-models` | Steps 3, 5, 6 — request bodies, `StringEnum<T>`, unions, wire names |
| `dotnet-error-handling` | Step 7 — the exception boundary (mandatory; see the two rows below) |
| `dotnet-testing` | Step 8 — testing the integration layer |

**Two hazard rows that must shape the boundary from its first version** — `System.Text.Json.JsonException`
reaches the boundary from two directions and they need opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from
  deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the
  integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the
  `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx
  then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can
  never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

Where this bites in *this* integration (each is a generated `required` member or a scalar error parse, confirmed
in the sources named in §2.3/§2.6): `ProductResponse.Product`, `CustomerResponse.Customer`, `SiteResponse.Site`
(2xx direction); `ErrorListResponse1.Errors` on a `CreateSubscription` **422**, and the `FromJson<string>` parse
of a `ListProductsForProductFamily` **404** body — a 404 body that is a JSON object rather than a JSON string
throws `JsonException` instead of surfacing as `SdkException<ListProductsForProductFamilyError>` (non-2xx
direction).

---

## 5. Assumptions & Blockers

**Assumptions**

1. The JWT-authenticated caller yields a stable per-user string (user name / email) from the app's existing
   identity path. I have not read eShopOnWeb's token or claims code and name no claim type; §2.7 leaves the
   derivation to the implementer.
2. "Sandbox" means an ordinary Maxio site reached by its subdomain (or by `Maxio:BaseUrl`). The SDK exposes
   **no** sandbox/test switch — `ServerEnvironment` has exactly `Us` and `Eu` (`sdk-map.md`); test-ness is a
   property of the site, observable after the fact as `Site.Test` from `Sites.ReadSite`.
3. `Maxio:ProductFamilyHandle` matches exactly one `ProductFamily.Handle` on the site; if `ListProductFamilies`
   returns none or several matches, the plans endpoint should fail loudly rather than pick one.
4. The three endpoints are additive and touch no existing Catalog/Basket/Order code or database entity.
5. The default plan handle for a `productHandle`-less POST comes from configuration; it is **not** one of the
   four dictated `Maxio:` keys, so either a fifth key is added or the family's single product is used (§2.5).

**Blockers**

None. In particular, the base-URL question is resolved rather than blocked: a verbatim
`options.Server.Production.Us.BaseUrl` override **is** supported (§2.5), with the subdomain-derived host as the
fallback when `Maxio:BaseUrl` is unset.
