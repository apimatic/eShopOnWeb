# Maxio Advanced Billing — eShopOnWeb subscribe plan + contract sheet

Package `AsadAli.AdvancedBilling.Sdk` · root namespace `MaxioAdvancedBilling` · client `MaxioAdvancedBillingClient` · map stamp `v1.0.2` / `15db14b`. Additive parallel capability; do not replace cart/checkout. Look up catalog entities by **handle**, never hard-code numeric IDs.

Demo handles (stable): family `eshop-subscribe` · plans `eshop-pro` ($299.00/mo), `basic-plan` ($29.00/mo) · metered component `api-call` (seeded; do **not** allocate on list/subscribe). Do **not** send a payment profile / card / bank / 3-DS on subscribe. Omitting those fields is **not** enough on live `eshop-pro`: create returns 422 `ErrorListResponse1.Errors` `"No payment method was on file for the $299.00 balance"`. The only create-time collection field is `CreateSubscription.PaymentCollectionMethod` — whether `Invoice`/`Remittance` clears that 422 is **UNVERIFIED** (see E).

---

## Scope & sequence

| Step | App behavior | SDK operations |
|---|---|---|
| 1 | Register `MaxioAdvancedBillingClient` from config (API key, subdomain **or** BaseUrl, product-family handle, environment) | client construction only |
| 2 | `GET /api/subscription-plans` — list plans in the configured family | `ProductFamilies.ListProductFamilies` → match `Handle` → `ProductFamilies.ListProductsForProductFamily` (paginate) |
| 3 | `POST /api/subscriptions` — resolve plan by handle | `Products.ReadProductByHandle` |
| 4 | Ensure Maxio customer for JWT user (idempotent) | `Customers.ReadCustomerByReference`; if 404, `Customers.CreateCustomer` with `Reference` = eShop user id; on 422 race, re-read by reference |
| 5 | Subscribe idempotency — already enrolled in this plan? | `Customers.ListCustomerSubscriptions`; treat Live + Problem states (table below) for the same `Product.Handle` as already subscribed and return that row |
| 6 | Enroll if missing (no payment profile / card) | `Subscriptions.CreateSubscription` with `ProductHandle` + `CustomerId` + `PaymentCollectionMethod` (see E — 422 if omitted; Invoice/Remittance vs payment-on-file is **UNVERIFIED**) |
| 7 | Confirm plan / price / state / next billing to caller | read `CreateSubscription` / list envelopes — `SubscriptionResponse.Subscription`; extra fetch: `Subscriptions.ReadSubscription` if needed |
| 8 | `GET /api/my-subscriptions` | `Customers.ReadCustomerByReference` → `Customers.ListCustomerSubscriptions` (empty list if no customer) |

Optional extra idempotency key (not a substitute for step 5): set `CreateSubscription.Reference` and look up with `Subscriptions.FindSubscription`. The SDK has **no** idempotency-key header on create. Customer uniqueness **is** API-enforced on `reference` (`operations/Customers.md` CreateCustomer notes).

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

### Client construction & auth

| Fact | Value | Source |
|---|---|---|
| NuGet | `AsadAli.AdvancedBilling.Sdk` | `sdk-map.md` |
| Client | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` | `sdk-map.md` |
| Only ctor | `MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` |
| DI | `MaxioAdvancedBilling.ServiceCollectionExtensions.AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>? configure = null)` — registers the client as singleton over `IHttpClientFactory` | `ServiceCollectionExtensions.cs` (map: `sdk-map.md` DI block) |
| Options | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions`: `Environment` (`MaxioAdvancedBilling.Servers.ServerEnvironment`), `Retry` (`MaxioAdvancedBilling.Core.Configuration.RetryOptions`), `Server` (`MaxioAdvancedBilling.ServerOptions`), `BasicAuth` (`MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?`) | `sdk-map.md`, `MaxioAdvancedBillingClientOptions.cs` |
| Basic auth | `options.BasicAuth = new BasicAuthCredentials { Username = <api key>, Password = "x" }` — username **is** the API key; password is the **literal** `"x"` | `sdk-map.md` Servers & auth |
| Environments | `ServerEnvironment.Us` (wire `US`, default via `ServerEnvironment.Default()`) → `https://{site}.chargify.com`; `ServerEnvironment.Eu` (wire `EU`) → `https://{site}.ebilling.maxio.com`. **No sandbox enum** — sandbox is the site, not `Environment`. | `sdk-map.md`, `Servers/ServerEnvironment.cs` |
| Subdomain | `options.Server.Production.Us.Site` (or `.Eu.Site` when `Environment` is Eu). Nested types: `MaxioAdvancedBilling.Servers.ProductionOptions` / `.UsOptions` / `.EuOptions`. Default `Site` placeholder is `"subdomain"`. | `sdk-map.md`, `Servers/ProductionOptions.cs` |
| BaseUrl override | When `Maxio:BaseUrl` is set, assign it **verbatim** to `options.Server.Production.Us.BaseUrl` (or `.Eu.BaseUrl` for Eu) instead of deriving the host from subdomain. Default US template `https://{site}.chargify.com`. Ebb group is unused here. | `sdk-map.md`, `Servers/ProductionOptions.cs` |
| Retry | `options.Retry` is `RetryOptions`; all members `required` — build a full instance or `RetryOptions.Default()`. Namespace `MaxioAdvancedBilling.Core.Configuration`. | `sdk-map.md` |

**Config keys (names only — never put secret values in code or this sheet):**

| Config | Env | Wires to |
|---|---|---|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | `BasicAuth.Username` |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | `Server.Production.{Us\|Eu}.Site` when BaseUrl is unset |
| `Maxio:BaseUrl` | (optional) | `Server.Production.{Us\|Eu}.BaseUrl` verbatim; **wins over** subdomain derivation |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | app filter; demo `eshop-subscribe` |
| (hosting) | `MAXIO_ENVIRONMENT` | map to `ServerEnvironment.Us` / `.Eu` only (see Assumptions) |

Password for Basic is always the literal `"x"`, not a config value.

⚠ Step 1 (client registration) — `HttpClient` ownership, whether the SDK client is long-lived, and how `AddMaxioAdvancedBillingClient` relates to the factory are not visible from the constructor alone. **MUST load `dotnet-client-initialization`** before writing the factory or DI registration.

⚠ Step 1 (auth) — which options property holds Basic credentials, and that the key must come from configuration rather than a literal, is a usage-layer fact. **MUST load `dotnet-authentication`** before setting `BasicAuth`.

⚠ Step 1 (server / retries) — `Retry` / `Timeout` / `HttpMethodsToRetry` do **not** bound a whole call the way an `HttpClient.Timeout` does, and a transport failure on `POST` (create customer / create subscription) has retry consequences for the idempotent ensure/enroll path. `Environment` vs live `Server` re-resolution also affects BaseUrl/Site. **MUST load `dotnet-configuration-resilience`** before wiring subdomain, BaseUrl, environment, or retry options.

### Operations

Call with **named arguments**. List/search ops have leading nullable params with **no C# default** — pass `null` to skip. Cancellation token argument name is `ct`.

⚠ Steps 2–8 (every call) — optional params without C# defaults mis-bind in a positional call; envelopes wrap the payload; methods are async throw-only (no `*Result` variants in this SDK). **MUST load `dotnet-calling-endpoints`** before the first `client.{Group}.{Op}` call.

#### A. List plans in a product family

There is **no** query filter for family handle. `ListProductsFilter` only has `Ids`, `PrepaidProductPricePoint`, `UseSiteExchangeRate` (`records-2-Cr-Ne.md`). `ReadProductFamily(int id)` cannot take a handle (C# type is `int`) even though the HTTP notes mention `handle:my-family`.

**A1 — resolve family id from handle**

| | |
|---|---|
| Controller | `client.ProductFamilies` (`MaxioAdvancedBilling.Api`) |
| Method | `ListProductFamilies(MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` |
| Must pass | `dateField` … `endDatetime` explicitly (`null` to skip) |
| Returns | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductFamilyResponse>` |
| Envelope | `ProductFamilyResponse.ProductFamily` (`product_family`): `ProductFamily?` |
| Family fields (read) | `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?` |
| Error | **Case B** `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` |
| Pagination | none (single response list) |
| Map | `operations/ProductFamilies.md`, `records-3-Of-Su.md` |

Match `ProductFamily.Handle ==` configured handle (`eshop-subscribe`). Use `Id` as string for A2. If no match: app-level not-found (do not guess an id).

**A2 — list products for that family**

| | |
|---|---|
| Controller | `client.ProductFamilies` |
| Method | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, MaxioAdvancedBilling.Models.ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| Must pass | `dateField` … `include` explicitly (`null` to skip). `page`/`perPage` have defaults. |
| Path | `productFamilyId` — pass **numeric id as string** from A1 (whether `handle:eshop-subscribe` is accepted is UNVERIFIED — do not rely on it) |
| Returns | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>` |
| Envelope | `ProductResponse.Product` (`product`): `Product` **!req** |
| Product fields for GET /api/subscription-plans | `Handle (handle): string?`, `Name (name): string?`, `PriceInCents (price_in_cents): long?` (**cents**, not dollars), `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `ProductFamily (product_family): ProductFamily?`, `RequireCreditCard (require_credit_card): bool?`, `RequestCreditCard (request_credit_card): bool?`, `ArchivedAt (archived_at): DateTimeOffset?` |
| Error | **Case A** `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>` — `TryGetString(out string)` **[404]** · `TryGetRawError(out RawError)` fallback (401/403/other) |
| Pagination | manual `page` + `perPage` (default 20). Loop until a page returns fewer than `perPage`. |
| Map | `operations/ProductFamilies.md`, `records-3-Of-Su.md`, `records-2-Cr-Ne.md` (`ListProductsFilter`) |

⚠ Step 2 (pagination) — how far `perPage` goes, and that the client does not auto-follow pages, is a resilience/config fact. **MUST load `dotnet-configuration-resilience`** before writing the list loop.

Alternate site-wide list `client.Products.ListProducts(...)` returns the same `ProductResponse` envelope (**Case B**) but **cannot** filter by family handle — do not use it as the primary path.

#### B. Look up one product by handle (subscribe-time)

| | |
|---|---|
| Controller | `client.Products` |
| Method | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` |
| HTTP | `GET /products/handle/{api_handle}.json` |
| Returns | `ProductResponse` — unwrap `.Product` (!req) |
| Error | **Case B** `SdkException<RawError>` — 404/401/403 via `StatusCode` + `ReadAsString()` |
| Map | `operations/Products.md` |

Use `apiHandle:` `eshop-pro` or `basic-plan` (from POST body). Do not call `ReadProduct(int productId)` for this flow.

#### C. Find customer by reference (eShop user id)

| | |
|---|---|
| Controller | `client.Customers` |
| Method | `ReadCustomerByReference(string reference, CancellationToken ct = default)` |
| HTTP | `GET /customers/lookup.json?reference=` |
| First-class? | **Yes** — this is the exact-match-by-reference operation (`operations/Customers.md`; ListCustomers notes say to use this lookup for a single exact match) |
| Returns | `CustomerResponse` |
| Envelope | `CustomerResponse.Customer` (`customer`): `Customer` **!req** |
| Customer fields | `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?` |
| Error | **Case B** `SdkException<RawError>` — missing customer is **404** (`StatusCode`). 401/403 same type. |
| Map | `operations/Customers.md`, `records-2-Cr-Ne.md` |

`ListCustomers(..., q: reference)` is a **search**, not exact match — do not use it for ensure-customer.

#### D. Create customer

| | |
|---|---|
| Controller | `client.Customers` |
| Method | `CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, CancellationToken ct = default)` |
| Must pass | `body` explicitly (nullable, no default) |
| Request envelope | `CreateCustomerRequest.Customer` (`customer`): `CreateCustomer` **!req** |
| `CreateCustomer` fields | **!req:** `FirstName (first_name): string`, `LastName (last_name): string`, `Email (email): string`. Optional: `Reference (reference): string?` — **set this** to the eShop user id. Other address/tax fields optional; omit. |
| Returns | `CustomerResponse` → `.Customer.Id` for subscribe |
| Error | **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` **[422]** · `TryGetRawError(out RawError)` fallback |
| 422 payload | `CustomerErrorResponse1.Errors` (`errors`): `Errors?` where `Errors` is **only** `PerPage (per_page)`, `PricePoint (price_point)` (`records-2-Cr-Ne.md`). That typed shape does **not** model a string-list or `customer` message. **UNVERIFIED** vs live 422 (duplicate reference). Defensive: if `TryGetCustomerErrorResponse1` is false **or** `Errors` is empty/unhelpful, use `TryGetRawError` → `ReadAsString()` / `ReadAsJson<T>()` and extract best-effort; fall back to a generic “could not create customer” message. Duplicate-reference 422: re-call `ReadCustomerByReference` and proceed if found. |
| Idempotency | API: “you may only create one customer for a given reference value” (`operations/Customers.md`). No idempotency-key param. |
| Map | `operations/Customers.md`, `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md` |

#### E. Create subscription (no payment method)

| | |
|---|---|
| Controller | `client.Subscriptions` |
| Method | `CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, CancellationToken ct = default)` |
| Must pass | `body` explicitly |
| Request envelope | `CreateSubscriptionRequest.Subscription` (`subscription`): `CreateSubscription` **!req** |
| Identify product | **`ProductHandle (product_handle): string?` is accepted** (preferred). Also `ProductId (product_id): int?`. Notes: “Specify the product with `product_id` or `product_handle`.” (`operations/Subscriptions.md`) |
| Identify customer | `CustomerId (customer_id): int?` **or** `CustomerReference (customer_reference): string?`. Prefer `CustomerId` from step 4. |
| Payment profiles (omit) | `PaymentProfileId (payment_profile_id): int?`, `PaymentProfileAttributes (payment_profile_attributes): PaymentProfileAttributes?`, `CreditCardAttributes (credit_card_attributes): PaymentProfileAttributes?`, `BankAccountAttributes (bank_account_attributes): BankAccountAttributes?` — **omit all**. Do **not** invent a dummy card/profile. |
| Collection method | **`PaymentCollectionMethod (payment_collection_method): MaxioAdvancedBilling.Models.Enums.CollectionMethod?`** — this is the field (not `CollectionMethod`). Members: `CollectionMethod.Automatic` (wire `automatic`), `CollectionMethod.Remittance` (`remittance`), `CollectionMethod.Prepaid` (`prepaid`), `CollectionMethod.Invoice` (`invoice`). Enum summary: legacy Statements Architecture → `invoice`, `automatic`; Relationship Invoicing → `remittance`, `automatic`, `prepaid` (`enums.md`). **UNVERIFIED** whether `Invoice` or `Remittance` (or any member) lets create succeed with a positive `PriceInCents` and no payment profile. Live omit-all-payment 422: `"No payment method was on file for the $299.00 balance"`. Do **not** use `Prepaid` without `PrepaidConfiguration`. Do **not** use `Automatic` expecting no card. Candidate for “no card, still enroll” **if** the site is Relationship Invoicing: `CollectionMethod.Remittance`; **if** legacy Statements: `CollectionMethod.Invoice`. Confirm against live 422; map does not document that either member suppresses payment-on-file. |
| Other fields vs initial charge | Map does **not** document any non-payment field as suppressing the payment-method-on-file requirement. Present but **not** claimed to fix the 422: `DeferSignup (defer_signup): bool? = false`; `InitialBillingAt (initial_billing_at): DateTimeOffset?`; `NextBillingAt (next_billing_at): DateTimeOffset?`; `CalendarBilling (calendar_billing): CalendarBilling?` (`CalendarBillingFirstCharge (calendar_billing_first_charge): FirstChargeType?` — `Prorated`/`Immediate`/`Delayed`); `SkipBillingManifestTaxes (skip_billing_manifest_taxes): bool?` (taxes only). Create notes: “Payment information may be required to create a subscription, **depending on the options for the Product being subscribed**.” (`operations/Subscriptions.md`) |
| Catalog mutate (prefer not) | **No** `UpdateProductByHandle`. Only `client.Products.UpdateProduct(int productId, CreateOrUpdateProductRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly. `CreateOrUpdateProduct.RequireCreditCard (require_credit_card): bool?` exists; `RequestCreditCard` is on `Product` (response) only, **not** on `CreateOrUpdateProduct`. Update also requires `Name`, `Description`, `PriceInCents`, `Interval`, `IntervalUnit`. Notes: this endpoint **creates a new price point and sets it as default**. Resolve id via `ReadProductByHandle` first. Prefer create-time `PaymentCollectionMethod` over mutating catalog. |
| Other useful optionals | `Reference (reference): string?` (subscription-level, for `FindSubscription`); `Components` — omit (`api-call` not required) |
| Returns | `SubscriptionResponse` |
| Envelope | `SubscriptionResponse.Subscription` (`subscription`): `Subscription?` — **nullable** (null-check; unlike `ProductResponse.Product`) |
| Error | **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` **[422]** · `TryGetRawError` fallback |
| 422 payload | `ErrorListResponse1.Errors` (`errors`): `IReadOnlyList<string>` **!req** — join these for the user-visible message (live payment-on-file string above) |
| Map | `operations/Subscriptions.md`, `operations/Products.md`, `records-2-Cr-Ne.md`, `records-1-Ac-Cr.md` (`CreateOrUpdateProduct`, `CalendarBilling`), `enums.md`, `records-4-Su-We.md` |

#### F. List subscriptions for a customer

| | |
|---|---|
| Controller | `client.Customers` |
| Method | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` |
| Returns | `IReadOnlyList<SubscriptionResponse>` |
| Pagination | **none** in the signature |
| Error | **Case B** `SdkException<RawError>` |
| Map | `operations/Customers.md` |

Use this for `GET /api/my-subscriptions` **and** subscribe idempotency (same user + same product handle). Site-wide `Subscriptions.ListSubscriptions` filters `product` by **int id**, not handle, and not by customer — do not use it here.

#### G. Fields to return (plan / price / state / next billing)

From `Subscription` (`records-3-Of-Su.md`) after unwrapping `SubscriptionResponse.Subscription`:

| C# | Wire | Type | Use |
|---|---|---|---|
| `Id` | `id` | `int?` | subscription id |
| `State` | `state` | `SubscriptionState?` | state |
| `ProductPriceInCents` | `product_price_in_cents` | `long?` | price **in cents** |
| `NextAssessmentAt` | `next_assessment_at` | `DateTimeOffset?` | **next billing date** to surface |
| `CurrentPeriodEndsAt` | `current_period_ends_at` | `DateTimeOffset?` | period end (secondary) |
| `Product` | `product` | `Product?` | nested plan |
| `Currency` | `currency` | `string?` | if present |
| `Reference` | `reference` | `string?` | if set at create |

From nested `Product` (`records-3-Of-Su.md`): `Handle (handle)`, `Name (name)`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`.

**Price:** always cents (`long`), never dollars. Display ÷ 100. **Interval:** integer count + `IntervalUnit` (`Day` / `Month`). Product has no currency field; subscription may.

#### H. Read one subscription

Create already returns `SubscriptionResponse`. Extra read only if the create result is insufficient:

| | |
|---|---|
| Controller | `client.Subscriptions` |
| Method | `ReadSubscription(int subscriptionId, IReadOnlyList<MaxioAdvancedBilling.Models.Enums.SubscriptionInclude>? include, CancellationToken ct = default)` |
| Must pass | `include` explicitly (`null` to skip). Includes: `Coupons`, `SelfServicePageToken` — not needed for this flow. |
| Returns | `SubscriptionResponse` |
| Error | **Case B** `SdkException<RawError>` |
| Map | `operations/Subscriptions.md` |

**Find by subscription reference** (optional idempotency aid, **not** customer reference):

| | |
|---|---|
| Method | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` must be passed explicitly |
| HTTP | `GET /subscriptions/lookup.json?reference=` |
| Returns | `SubscriptionResponse` |
| Error | **Case A** `SdkException<FindSubscriptionError>` — `TryGetNoContent(out RawError)` **[404]** · `TryGetRawError` fallback |
| Map | `operations/Subscriptions.md` |

Whether subscription `reference` is unique site-wide is **UNVERIFIED** — do not use FindSubscription as the only double-click guard; always check `ListCustomerSubscriptions` by product handle + state.

---

### Enums (needed values)

Namespace `MaxioAdvancedBilling.Models.Enums`. These are `StringEnum<T>` records, **not** C# enums — use static members (e.g. `SubscriptionState.Active`) or `Type.FromValue("wire")`. Compare with `==`.

**`IntervalUnit`** (`enums.md`, `Models/Enums/IntervalUnit.cs`): `Day (day)`, `Month (month)`.

**`SubscriptionState`** (`enums.md` + `Models/Enums/SubscriptionState.cs` XML):

| Member | Wire | Class (from XML) |
|---|---|---|
| `Pending` | `pending` | Live (transient — do not use for access decisions) |
| `Assessing` | `assessing` | Live (transient) |
| `Active` | `active` | Live |
| `Trialing` | `trialing` | Live |
| `Paused` | `paused` | Live (XML: account arrears) |
| `PastDue` | `past_due` | Problem |
| `SoftFailure` | `soft_failure` | Problem |
| `Unpaid` | `unpaid` | Problem |
| `Canceled` | `canceled` | End of life |
| `Expired` | `expired` | End of life |
| `FailedToCreate` | `failed_to_create` | End of life |
| `OnHold` | `on_hold` | End of life |
| `Suspended` | `suspended` | End of life |
| `TrialEnded` | `trial_ended` | End of life |
| `AwaitingSignup` | `awaiting_signup` | **unclassified in XML** |

**Already-subscribed (do not create a second):** Live + Problem for the **same** `Product.Handle` — `Active`, `Assessing`, `Pending`, `Trialing`, `Paused`, `PastDue`, `SoftFailure`, `Unpaid`. End-of-life (`Canceled`, `Expired`, `FailedToCreate`, `OnHold`, `Suspended`, `TrialEnded`) may subscribe again. `AwaitingSignup`: treat conservatively as already subscribed (UNVERIFIED classification).

**`ListProductsInclude`:** `PrepaidProductPricePoint (prepaid_product_price_point)` — pass `null` for this flow.

**`BasicDateField`:** `UpdatedAt (updated_at)`, `CreatedAt (created_at)` — pass `null` on list calls.

**`CollectionMethod`** (`enums.md`) — set on create as `CreateSubscription.PaymentCollectionMethod`: `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)`. Architecture split in the enum summary: Statements → `invoice`/`automatic`; Relationship Invoicing → `remittance`/`automatic`/`prepaid`. Whether `Invoice` or `Remittance` avoids payment-on-file 422 is **UNVERIFIED**.

⚠ Steps 2–7 (models) — `required` init members, `StringEnum<T>` vs C# enums, and dropped unmodeled JSON are not visible from field lists alone. No unions are required on the request path for this flow (`CreateSubscription.OfferId` is a union — omit it). **MUST load `dotnet-models`** before constructing `CreateCustomer` / `CreateSubscription` or mapping envelopes.

### Error types by HTTP status (integration boundary)

| Status | Typical ops | How to read |
|---|---|---|
| 401 / 403 | all | Case A: `TryGetRawError` fallback (no typed 401 accessor). Case B: `ex.Error.StatusCode` + `ReadAsString()`. |
| 404 product | `ReadProductByHandle` | Case B `RawError.StatusCode` |
| 404 family products | `ListProductsForProductFamily` | Case A `TryGetString(out string)` |
| 404 customer | `ReadCustomerByReference` | Case B — treat as “no customer yet” |
| 404 subscription | `FindSubscription` | Case A `TryGetNoContent(out RawError)`; `ReadSubscription` Case B |
| 422 customer | `CreateCustomer` | `TryGetCustomerErrorResponse1` then **also** raw body (typed `Errors` is PerPage/PricePoint only) |
| 422 subscription | `CreateSubscription` | `TryGetErrorListResponse1` → `Errors` string list |
| Other | | `TryGetRawError` / Case B `RawError` |

Core types: `MaxioAdvancedBilling.Core.Exceptions.SdkException<T>`, `MaxioAdvancedBilling.Core.ErrorResponse.ApiError` (`TryGetRawError`), `MaxioAdvancedBilling.Core.ErrorResponse.RawError`, operation errors in `MaxioAdvancedBilling.Errors`.

⚠ Steps 4–8 (error boundary) — Case A vs Case B differs **per operation** (this sheet marks each); `TryGetRawError` is not a catch-all on the wrong `TError`; this SDK is throw-only. **MUST load `dotnet-error-handling`** before any `try/catch`.

⚠ Error boundary — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Error boundary — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. This is especially relevant for `CreateCustomer` 422 (`CustomerErrorResponse1` / `Errors` vs a likely string-list body). **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step tests — the constructor `HttpClient` is the test seam; match the existing eShopOnWeb test stack. **MUST load `dotnet-testing`** before stubbing the SDK.

---

## Trap notes

⚠ Step 1 (client registration) — the SDK does not own `HttpClient` lifetime; per-request construction vs factory registration has connection-pool consequences. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ Step 1 (auth) — Basic username/password roles and loading the key from configuration are usage-layer, not implied by `BasicAuthCredentials` property names. **MUST load `dotnet-authentication`** before setting credentials.

⚠ Step 1 (BaseUrl / Site / Retry) — which knob actually selects the host, what `Timeout` bounds, and whether a failed **write** (`CreateCustomer` / `CreateSubscription`) can be re-sent, are not the option names. **MUST load `dotnet-configuration-resilience`** before registering the client.

⚠ Steps 2–8 (calls) — named arguments and envelope unwrap (`.Product` / `.Customer` / `.Subscription`) vs treating the response type as the payload. **MUST load `dotnet-calling-endpoints`** before the first call.

⚠ Steps 3–6 (payloads) — `CreateCustomer` `required` first/last/email; `StringEnum<T>` for `IntervalUnit` / `SubscriptionState`; cents vs dollars. **MUST load `dotnet-models`** before mapping.

⚠ Steps 4–8 (errors) — mixed Case A/B, 404-as-empty vs 404-as-failure, `JsonException` from both 2xx and non-2xx. **MUST load `dotnet-error-handling`** before the boundary.

⚠ Tests — fake the `HttpClient` handler, not SDK internals. **MUST load `dotnet-testing`** before writing tests.

---

## REQUIRED READING

Load these **before implementation starts**. This sheet does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — ctor, DI `AddMaxioAdvancedBillingClient`, `HttpClient` lifetime |
| `dotnet-authentication` | Step 1 — `BasicAuth` / API key / `"x"` |
| `dotnet-configuration-resilience` | Step 1 — Site vs BaseUrl, Us/Eu, retries/timeouts; Step 2 — list pagination |
| `dotnet-calling-endpoints` | Steps 2–8 — named args, `ct:`, envelopes, throw-only calls |
| `dotnet-models` | Steps 3–7 — request records, required members, `StringEnum<T>`, cents fields |
| `dotnet-error-handling` | Steps 4–8 — Case A/B, `TryGet…`, `JsonException` both directions (always required) |
| `dotnet-testing` | Tests for the integration layer |

---

## Assumptions & Blockers

**Assumptions**

- JWT yields a stable unique user id (string) for `Customer.Reference`, plus email and first/last name (or a split display name) because `CreateCustomer` requires `FirstName`, `LastName`, `Email`.
- `MAXIO_ENVIRONMENT` is meant to select hosting region: values `US` / `Us` → `ServerEnvironment.Us`; `EU` / `Eu` → `ServerEnvironment.Eu`. Anything else (including a literal “sandbox”) is **not** an SDK environment — default `Us` and treat sandbox as the **site subdomain**.
- Seeded “payment not required” is **contradicted by live create** on `eshop-pro` (422 payment-on-file). Still omit payment profile/card/bank. Set `PaymentCollectionMethod` to `CollectionMethod.Remittance` (RI) or `CollectionMethod.Invoice` (Statements); **UNVERIFIED** that either clears the 422. Do not create a dummy payment profile. Do not mutate catalog unless create-time collection method fails live.
- `ProductFamily.Id` is present on the list row for `eshop-subscribe` so A2 can pass it as `productFamilyId`.
- `GET /api/my-subscriptions` when `ReadCustomerByReference` is 404 returns an empty list (no Maxio customer yet), not an API error.

**Blockers**

- None that prevent the flow: list-by-family-handle is not a first-class filter, but A1+A2 (list families → match handle → list products by numeric id string) is in the SDK. `ReadProductFamily` cannot take a handle (`int id` only).
- No SDK idempotency-key header on `CreateCustomer` or `CreateSubscription`. Customer `reference` uniqueness is documented; subscription uniqueness by customer+product is **not** — application must use `ListCustomerSubscriptions`.
- `CustomerErrorResponse1.Errors` generated members (`per_page` / `price_point` only) do not look like a customer-validation body — live 422 shape is **UNVERIFIED**; follow the defensive extract path above. A mismatch can surface as `JsonException` instead of `SdkException<CreateCustomerError>`.
- `SubscriptionState.AwaitingSignup` is not classified in the enum XML live/problem/eol lists.
- Whether `ListProductsForProductFamily` accepts `handle:eshop-subscribe` as `productFamilyId` is **UNVERIFIED** — pass the numeric id as string.
- Whether subscription `Reference` is unique (so `FindSubscription` is a reliable lock) is **UNVERIFIED**.
- Metered component `api-call` is out of scope for list/subscribe; there is no blocker unless a later story must allocate it (`SubscriptionComponents` not planned here).
- **UNVERIFIED:** whether `CreateSubscription.PaymentCollectionMethod = CollectionMethod.Invoice` or `.Remittance` allows enroll with positive product price and no payment profile. Map documents the field and members only; it does not document that they suppress payment-on-file. No other create field is documented to suppress that requirement. There is no update-product-by-handle operation.
