# Maxio Advanced Billing — eShopOnWeb subscribe hero flow

NuGet: `AsadAli.AdvancedBilling.Sdk` · Root namespace: `MaxioAdvancedBilling` · Map stamp: `v1.0.2` / `15db14b`

Additive parallel capability. Existing cart/checkout stays. Maxio is the billing system of record. Seeded demo handles (`eshop-subscribe`, `eshop-pro`, `basic-plan`, site `cp-exp-1`) are **examples only** — every lookup is by configured handle / JWT reference so the client works against a different site and catalog.

---

## Scope & sequence

| Step | What | SDK operations |
|---|---|---|
| 1 | Client + DI + config | `MaxioAdvancedBillingClient` / `AddMaxioAdvancedBillingClient` — no HTTP yet |
| 2 | List plans in the configured product family | `ProductFamilies.ListProductFamilies` → match `Handle` to `Maxio:ProductFamilyHandle` → page `ProductFamilies.ListProductsForProductFamily`. Optional check: `Products.ReadProductByHandle` |
| 3 | Ensure Maxio customer (idempotent) | `Customers.ReadCustomerByReference` → on 404 `Customers.CreateCustomer` (then re-read on 422 race) |
| 4 | Enroll (idempotent) | `Customers.ListCustomerSubscriptions` → if live match on `Product.Handle`, return it; else `Subscriptions.CreateSubscription` by **product handle** (no numeric product id, no card) |
| 5 | Confirm to caller / list mine | Unwrap `SubscriptionResponse.Subscription` (plan handle/name/price, `State`, `NextAssessmentAt`). `GET /api/my-subscriptions` = step 3 lookup + `ListCustomerSubscriptions`. 404 on lookup → empty list |

**Recommended subscribe call sequence (hero POST):** find-or-create customer (step 3) → list that customer's subscriptions and return the existing live match for the chosen plan handle if any → otherwise `CreateSubscription` → return confirmation from the create response (do not re-GET unless create's inner `Subscription` is null).

HTTP surface (already mandated; this plan does not define controllers): `GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions`. JWT-authenticated PublicApi.

Metered component `api-call` is seeded on the demo family and is **out of scope** for this hero flow — do not allocate or report usage.

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

### Namespaces (every type this sheet uses)

| Namespace | Types |
|---|---|
| `MaxioAdvancedBilling` | `MaxioAdvancedBillingClient`, `MaxioAdvancedBillingClientOptions`, `AddMaxioAdvancedBillingClient` (`ServiceCollectionExtensions`) |
| `MaxioAdvancedBilling.Core.Authentication.Basic` | `BasicAuthCredentials` |
| `MaxioAdvancedBilling.Servers` | `ServerEnvironment` |
| `MaxioAdvancedBilling.Core.Configuration` | `RetryOptions` |
| `MaxioAdvancedBilling.Core.Exceptions` | `SdkException<TError>` |
| `MaxioAdvancedBilling.Core.ErrorResponse` | `ApiError`, `RawError` |
| `MaxioAdvancedBilling.Api` | controller classes behind `client.Products`, `client.ProductFamilies`, `client.Customers`, `client.Subscriptions` |
| `MaxioAdvancedBilling.Models` | all request/response records below |
| `MaxioAdvancedBilling.Models.Enums` | `IntervalUnit`, `CollectionMethod`, `SubscriptionState`, `BasicDateField`, `ListProductsInclude`, `ExpirationIntervalUnit` |
| `MaxioAdvancedBilling.Errors` | `CreateCustomerError`, `CreateSubscriptionError`, `ListProductsForProductFamilyError`, `FindSubscriptionError` |

`options.Server` is `ServerOptions` on the client-options class (source `ServerOptions.cs`). Nested Production/Ebb environment nodes: sources `Servers/ProductionOptions.cs`, `Servers/EbbOptions.cs` (`MaxioAdvancedBilling.Servers`). Assign through the property path the map names; do not invent sibling namespaces.

---

### Client construction / auth / server node

| Fact | Value | Source |
|---|---|---|
| Client ctor | `MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` — only ctor | `sdk-map.md` |
| DI | `IServiceCollection.AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>)` | `sdk-map.md` |
| Options properties | `Environment`: `MaxioAdvancedBilling.Servers.ServerEnvironment` · `Retry`: `MaxioAdvancedBilling.Core.Configuration.RetryOptions` · `Server`: `ServerOptions` · `BasicAuth`: `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md` |
| Auth | HTTP Basic. `options.BasicAuth = new BasicAuthCredentials { Username = <api key>, Password = "x" }`. Password is the **literal** `"x"`, never empty and never the key. | `sdk-map.md` |
| Config → options | `Maxio:ApiKey` / env `MAXIO_API_KEY` → `BasicAuth.Username`. `Maxio:Subdomain` / env `MAXIO_SITE_SUBDOMAIN` → `options.Server.Production.Us.Site` (when `Environment` is `Us`; `.Eu.Site` when `Eu`). Never hardcode `cp-exp-1`. | brief + `sdk-map.md` |
| Environments | `ServerEnvironment.Us` (default, value `US`) → `https://{site}.chargify.com`. `ServerEnvironment.Eu` (value `EU`) → `https://{site}.ebilling.maxio.com`. **There is no sandbox environment enum.** Sandbox is the **site** (subdomain + API key), not `Environment`. | `sdk-map.md` |
| Target hosting | Use `ServerEnvironment.Us` unless config explicitly selects EU hosting. `MAXIO_ENVIRONMENT=sandbox` is **not** a `ServerEnvironment` member — do not call `FromValue("sandbox")`. | `sdk-map.md` |
| **BaseUrl override** | When `Maxio:BaseUrl` is set, assign it **verbatim** to `options.Server.Production.Us.BaseUrl` (or `.Eu.BaseUrl` if `Environment` is `Eu`) — **instead of** deriving the host from subdomain. Map example: `options.Server.Production.Us.BaseUrl = "http://localhost:8080"`. Set `BaseUrl` on the Production group for the **same** environment selected on `options.Environment`; the other environment node is not read. Ebb (`options.Server.Ebb.*`) is unused (event-ingest only). | `sdk-map.md` |
| RetryOptions | All members `required`. Build a full instance or start from `RetryOptions.Default()`. Members: `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout` (`TimeSpan?`), `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry`. | `sdk-map.md` |

⚠ Step 1 (client registration) — `HttpClient` ownership/lifetime vs the SDK wrapper, and how `AddMaxioAdvancedBillingClient` wires the factory, are not visible from the ctor. **MUST load `dotnet-client-initialization`** before registering the client.

⚠ Step 1 (auth) — a 401/403 is a credentials/scheme failure, not a billing-domain error; the Basic username/password slots are easy to swap. **MUST load `dotnet-authentication`** before wiring `BasicAuth`.

⚠ Step 1 (BaseUrl / Site / Environment) — a verbatim `Maxio:BaseUrl` and the subdomain `Site` live on **different nested properties**, and only the environment selected at construction is consulted. Putting the override on the wrong node silently uses the templated host. **MUST load `dotnet-configuration-resilience`** before assigning `Server` / `Environment`.

⚠ Step 1 / Step 4 (POST subscribe retries) — the retry/timeout options do **not** bound a whole call, are **not** the timeout on the `HttpClient` you register, and interact with **whether a failed write can be re-sent**. A transport-level retry of `CreateSubscription` or `CreateCustomer` can execute the write more than once even after the application-level idempotency checks below. **MUST load `dotnet-configuration-resilience`** before wiring retries or calling POST.

---

### Operations

#### `client.ProductFamilies.ListProductFamilies` — resolve family by handle

| | |
|---|---|
| HTTP | `GET /product_families.json` (Production) |
| Signature | `ListProductFamilies(MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` |
| Must-pass | `dateField` … `endDatetime` — nullable, **no C# default** → pass `null` to skip (named args) |
| Returns | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductFamilyResponse>` |
| Envelope | each item: `ProductFamily (product_family): ProductFamily?` — unwrap `.ProductFamily` |
| Inner fields used | `Handle (handle): string?`, `Id (id): int?`, `Name (name): string?` |
| Error | **Case B** `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` |
| Pagination | **none** (signature has no `page`/`perPage`) |
| Map | `operations/ProductFamilies.md`, `records-3-Of-Su.md` |

Match `ProductFamily.Handle` to config `Maxio:ProductFamilyHandle` / `MAXIO_DEFAULT_PRODUCT_FAMILY`. Then pass `ProductFamily.Id` as a decimal string into the next call.

`ReadProductFamily(int id, …)` **cannot** take a handle: C# parameter is `int` even though the HTTP notes mention `handle:my-family`. Do not use it for this flow. (`operations/ProductFamilies.md`)

---

#### `client.ProductFamilies.ListProductsForProductFamily` — list plans

| | |
|---|---|
| HTTP | `GET /product_families/{product_family_id}/products.json` (Production) |
| Signature | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, MaxioAdvancedBilling.Models.ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| Must-pass | `dateField` … `include` (8 params) — nullable, **no C# default** → pass `null` to skip. `productFamilyId` required. Defaults: `page` = 1, `perPage` = 20 |
| Named-arg call | `productFamilyId: id.ToString()`, `dateField: null`, `filter: null`, `startDate: null`, `endDate: null`, `startDatetime: null`, `endDatetime: null`, `includeArchived: false`, `include: null`, `page: page`, `perPage: 20`, `ct: ct` |
| `productFamilyId` | `string` — **not** the `ProductFamilyId` union (that union is for invoice coupons, `unions.md`). Pass the numeric family **id as a string** after the handle match above. Do **not** pass a bare handle; do **not** use numeric product ids as the catalog key. |
| Returns | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>` |
| Envelope | each item: `Product (product): Product !req` — unwrap `.Product` |
| Inner fields used | see Product fields table |
| Error | **Case A** `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` **[404]** · `TryGetRawError(out RawError)` fallback |
| Pagination | **manual** `page`+`perPage` (default 20). Loop until a page returns fewer than `perPage` items. A different catalog may exceed one page. |
| Filter model | `ListProductsFilter`: `Ids (ids): IReadOnlyList<int>?`, `PrepaidProductPricePoint`, `UseSiteExchangeRate` — **no family-handle filter**. Do not use `ListProducts`+filter as the family-scoped path. |
| Map | `operations/ProductFamilies.md`, `records-3-Of-Su.md`, `records-2-Cr-Ne.md` (`ListProductsFilter`) |

`ListProducts` (`client.Products`, site-wide, Case B, same envelope) is **not** the primary list: it cannot filter by family handle (`ListProductsFilter` has no handle). Use it only as a last resort after paging the whole site. Prefer family-scoped list. (`operations/Products.md`)

---

#### `client.Products.ReadProductByHandle` — plan lookup by stable handle

| | |
|---|---|
| HTTP | `GET /products/handle/{api_handle}.json` (Production) |
| Signature | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` |
| Returns | `MaxioAdvancedBilling.Models.ProductResponse` |
| Envelope | `Product (product): Product !req` |
| Error | **Case B** `SdkException<RawError>` |
| Pagination | none |
| Use | Validate the POST body's plan handle before subscribe; confirm the product exists on **this** site. Do **not** call `ReadProduct(int productId)` — numeric ids are reassigned on re-seed. |
| Map | `operations/Products.md`, `records-3-Of-Su.md` |

---

#### Product fields the integration reads (`MaxioAdvancedBilling.Models.Product`)

| C# (wire) | Type | Role |
|---|---|---|
| `Handle (handle)` | `string?` | plan id returned to API clients; subscribe input |
| `Name (name)` | `string?` | display name |
| `PriceInCents (price_in_cents)` | `long?` | unit price in **cents** (seeded demo: 29900 / 2900) |
| `Interval (interval)` | `int?` | count of `IntervalUnit` (monthly plans: `1`) |
| `IntervalUnit (interval_unit)` | `IntervalUnit?` | `Day` / `Month` |
| `ProductFamily (product_family)` | `ProductFamily?` | nested; `Handle` available if you ever list site-wide |
| `RequireCreditCard (require_credit_card)` | `bool?` | seeded hero products do not require a card; if a foreign catalog is `true`, create will 422 without a profile — do not invent card collection |
| `ProductPricePointHandle (product_price_point_handle)` | `string?` | omit on create to use the default price point |

Map: `records-3-Of-Su.md`. Envelope: `ProductResponse` (`records-3-Of-Su.md`).

---

#### `client.Customers.ReadCustomerByReference` — lookup by eShop user id

| | |
|---|---|
| HTTP | `GET /customers/lookup.json` (Production) |
| Signature | `ReadCustomerByReference(string reference, CancellationToken ct = default)` |
| Query | `reference` ← `reference` |
| Returns | `MaxioAdvancedBilling.Models.CustomerResponse` |
| Envelope | `Customer (customer): Customer !req` — unwrap `.Customer` |
| Error | **Case B** `SdkException<RawError>` — treat `StatusCode == NotFound` as “no customer yet”; any other status is a failure |
| Pagination | none |
| **Reference field** | Maxio `Customer.Reference (reference)` / create `CreateCustomer.Reference (reference)`. Store the **stable JWT identity user id** (not email, not username — those can change). Notes: “The only validation restriction is that you may only create one customer for a given reference value.” (`operations/Customers.md`) |
| Map | `operations/Customers.md`, `records-2-Cr-Ne.md` |

Do **not** use `ListCustomers(..., q: userId)` for the exact match — the same notes say to use this lookup endpoint for a single exact reference match.

---

#### `client.Customers.CreateCustomer` — create if missing

| | |
|---|---|
| HTTP | `POST /customers.json` (Production) |
| Signature | `CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, CancellationToken ct = default)` |
| Must-pass | `body` — nullable, **no default** → pass explicitly (never omit the arg) |
| Returns | `MaxioAdvancedBilling.Models.CustomerResponse` |
| Envelope | `Customer (customer): Customer !req` |
| Error | **Case A** `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` **[422]** · `TryGetRawError(out RawError)` fallback |
| Pagination | none |
| Map | `operations/Customers.md`, `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md` |

**Request envelope:** `CreateCustomerRequest` — `Customer (customer): CreateCustomer !req`.

**`CreateCustomer` members we set** (`records-1-Ac-Cr.md`):

| C# (wire) | Type | Required? |
|---|---|---|
| `FirstName (first_name)` | `string` | **!req** |
| `LastName (last_name)` | `string` | **!req** |
| `Email (email)` | `string` | **!req** |
| `Reference (reference)` | `string?` | set to JWT user id (unique) |

Leave address/locale/tax fields unset.

**Idempotent ensure:** `ReadCustomerByReference` → if Case B 404, `CreateCustomer` with the same `Reference`. If create returns 422, immediately `ReadCustomerByReference` again (double-click race); if found, proceed. Do not parse 422 as fatal until the re-read also misses.

`CustomerErrorResponse1.Errors` is typed as `MaxioAdvancedBilling.Models.Errors?` whose generated fields are `PerPage` / `PricePoint` (`records-2-Cr-Ne.md`) — a **suspicious shared model**, not a customer-validation shape. **UNVERIFIED** whether a live 422 body binds to those fields. Defensive: do **not** branch on `Errors.PerPage` / `Errors.PricePoint`; use the 422 **status** + re-read-by-reference; if still missing, surface a generic validation message (best-effort `ReadAsString` via `TryGetRawError` only if that accessor returns true).

**`Customer` response fields used:** `Id (id): int?` (needed by `ListCustomerSubscriptions`), `Reference (reference): string?`, `Email (email): string?`. (`records-2-Cr-Ne.md`)

---

#### `client.Customers.ListCustomerSubscriptions` — list mine + subscribe idempotency

| | |
|---|---|
| HTTP | `GET /customers/{customer_id}/subscriptions.json` (Production) |
| Signature | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` |
| Returns | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>` |
| Envelope | each item: `Subscription (subscription): Subscription?` — **nullable**; skip nulls |
| Error | **Case B** `SdkException<RawError>` |
| Pagination | **none** |
| Map | `operations/Customers.md`, `records-4-Su-We.md`, `records-3-Of-Su.md` |

Needs Maxio **numeric** `customerId` from the ensure step (`Customer.Id`). If `Id` is null after a successful read/create, fail the integration (cannot list).

**Detect existing enrollment for `(user, plan handle)`:** among unwrapped subscriptions, match `Subscription.Product?.Handle` to the requested plan handle (handles are stable; **do not** compare `Product.Id`). Treat as already enrolled when `State` is any of: `Active`, `Trialing`, `Assessing`, `PastDue`, `SoftFailure`, `Unpaid`, `Pending`, `AwaitingSignup`, `OnHold`, `Paused`, `Suspended`. Allow a new subscribe when the only matches are `Canceled`, `Expired`, `FailedToCreate`, `TrialEnded` (or `State` is null). The hero requirement is “active”; the extra live states prevent double-billing while dunning/hold. Compare handles as returned (config/request string vs `Product.Handle`).

---

#### `client.Subscriptions.CreateSubscription` — enroll by handle, no card

| | |
|---|---|
| HTTP | `POST /subscriptions.json` (Production) |
| Signature | `CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, CancellationToken ct = default)` |
| Must-pass | `body` — nullable, **no default** → pass explicitly |
| Returns | `MaxioAdvancedBilling.Models.SubscriptionResponse` |
| Envelope | `Subscription (subscription): Subscription?` — null-check before reading |
| Error | **Case A** `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` **[422]** · `TryGetRawError(out RawError)` fallback |
| 422 payload | `ErrorListResponse1.Errors (errors): IReadOnlyList<string> !req` — usable messages |
| Pagination | none |
| Map | `operations/Subscriptions.md`, `records-2-Cr-Ne.md`, `records-4-Su-We.md`, `records-3-Of-Su.md` |

**Request envelope:** `CreateSubscriptionRequest` — `Subscription (subscription): CreateSubscription !req`.

**`CreateSubscription` members we set / omit** (`records-2-Cr-Ne.md`):

| C# (wire) | Type | This flow |
|---|---|---|
| `ProductHandle (product_handle)` | `string?` | **set** — chosen plan handle (`eshop-pro` / `basic-plan` on the demo family; whatever the client posted against a live catalog) |
| `ProductId (product_id)` | `int?` | **omit** — ids are not stable |
| `ProductPricePointHandle` / `ProductPricePointId` | | **omit** — default price point |
| `CustomerId (customer_id)` | `int?` | **set** — from ensure step |
| `CustomerReference (customer_reference)` | `string?` | alternative to `CustomerId`; use **one**. Prefer `CustomerId` after ensure so list/create share the same id |
| `CustomerAttributes` | `CustomerAttributes?` | **omit** — do not create-customer-inline; that bypasses the ensure path |
| `PaymentCollectionMethod (payment_collection_method)` | `CollectionMethod?` | **set explicitly** to `CollectionMethod.Remittance` (current Relationship Invoicing no-card). `Invoice` is the legacy Statements value. Do **not** omit — omitted default is **UNVERIFIED** and may be `Automatic` (card). |
| `PaymentProfileId` / `PaymentProfileAttributes` / `CreditCardAttributes` / `BankAccountAttributes` | | **omit** — no payment method |
| `Components` | | **omit** — metered `api-call` not in hero flow |
| `Reference (reference)` | `string?` | optional extra: `{userId}:{productHandle}` so `FindSubscription` can look it up later. Uniqueness of subscription `reference` is **UNVERIFIED** — **primary** idempotency is `ListCustomerSubscriptions`, not this field |
| `OfferId (offer_id)` | `OfferId?` **(union)** | **omit** — do not construct this union |

Notes (operation row): identify product with `product_id` **or** `product_handle`; identify customer with `customer_id` **or** `customer_reference`. Payment info may be required depending on the product; seeded hero products do not require a card.

---

#### `client.Subscriptions.FindSubscription` — optional secondary lookup by subscription reference

| | |
|---|---|
| HTTP | `GET /subscriptions/lookup.json` (Production) |
| Signature | `FindSubscription(string? reference, CancellationToken ct = default)` |
| Must-pass | `reference` — nullable, **no default** → pass explicitly |
| Returns | `MaxioAdvancedBilling.Models.SubscriptionResponse` |
| Error | **Case A** `SdkException<FindSubscriptionError>` — `TryGetNoContent(out RawError)` **[404]** · `TryGetRawError(out RawError)` fallback |
| Pagination | none |
| Use | Only if you set `CreateSubscription.Reference`. Not a substitute for listing by customer. |
| Map | `operations/Subscriptions.md` |

---

#### Subscription fields the integration returns (`MaxioAdvancedBilling.Models.Subscription`)

Map: `records-3-Of-Su.md`. Envelope: `SubscriptionResponse.Subscription` is **nullable** (`records-4-Su-We.md`).

| C# (wire) | Type | Role |
|---|---|---|
| `Id (id)` | `int?` | Maxio subscription id (internal; not a catalog key) |
| `State (state)` | `SubscriptionState?` | plan state returned to the user |
| `ProductPriceInCents (product_price_in_cents)` | `long?` | price on the subscription (cents) |
| `NextAssessmentAt (next_assessment_at)` | `DateTimeOffset?` | **next-billing-date** to confirm (there is no `NextBillingAt` on the response model; that name is create-input only) |
| `CurrentPeriodEndsAt (current_period_ends_at)` | `DateTimeOffset?` | period end; secondary to `NextAssessmentAt` |
| `PaymentCollectionMethod (payment_collection_method)` | `CollectionMethod?` | echo |
| `Product (product)` | `Product?` | nested: `Handle`, `Name`, `PriceInCents`, `Interval`, `IntervalUnit` |
| `Customer (customer)` | `Customer?` | nested customer |
| `Reference (reference)` | `string?` | subscription reference if set |

Confirmation payload for POST: handle + name from `Product`, price from `ProductPriceInCents` (fallback `Product.PriceInCents`), state from `State`, next billing from `NextAssessmentAt`.

---

### Enums actually used (`MaxioAdvancedBilling.Models.Enums`)

These are `StringEnum<T>` records, **not** C# enums. Write `CollectionMethod.Remittance`, never `.remittance`. Construct via the static member or `Type.FromValue("wire")`. Map: `enums.md`.

| Enum | Members we use | Wire |
|---|---|---|
| `IntervalUnit` | `Day`, `Month` | `day`, `month` |
| `CollectionMethod` | `Remittance` (no-card, current RI), `Automatic`, `Prepaid`, `Invoice` (legacy Statements) | `remittance`, `automatic`, `prepaid`, `invoice` |
| `SubscriptionState` | `Pending`, `FailedToCreate`, `Trialing`, `Assessing`, `Active`, `SoftFailure`, `PastDue`, `Suspended`, `Canceled`, `Expired`, `Paused`, `Unpaid`, `TrialEnded`, `OnHold`, `AwaitingSignup` | matching snake: `pending`, `failed_to_create`, `trialing`, `assessing`, `active`, `soft_failure`, `past_due`, `suspended`, `canceled`, `expired`, `paused`, `unpaid`, `trial_ended`, `on_hold`, `awaiting_signup` |
| `BasicDateField` | unused (pass `null`) | `updated_at`, `created_at` |
| `ListProductsInclude` | unused (pass `null`); only member `PrepaidProductPricePoint` | `prepaid_product_price_point` |
| `ExpirationIntervalUnit` | read-only on product if present; seeded “expires never” | `day`, `month`, `never` |
| `ServerEnvironment` | `Us`, `Eu` | `US`, `EU` — **not an Models.Enums type**; namespace `MaxioAdvancedBilling.Servers` |

⚠ Every request/response that is not a plain string/int — especially `StringEnum<T>`, `required` init members, and envelope unwrap — **MUST load `dotnet-models`** before constructing bodies or mapping to API DTOs.

⚠ Every list/search call with many optional-but-must-pass params **MUST load `dotnet-calling-endpoints`** and use **named arguments** (`ct:` not `cancellationToken:`). Positional calls mis-bind.

---

### Unions in this flow

No request field we set is a union. Do **not** set `CreateSubscription.OfferId` (`OfferId` union, `unions.md`).

`ProductFamilyId` (`TryGetString` / `TryGetInt`, `unions.md`) is **not** the type of `ListProductsForProductFamily.productFamilyId` (that parameter is `string`).

`CustomerErrorResponse1.Errors` is **not** the `Errors1` union; it is the `Errors` record (suspicious shared model — see CreateCustomer).

---

### Error boundary (every operation is throw-only)

There are **no** `…Result` / `ApiResult` no-throw variants. Catch `SdkException<T>` per operation case above. Case A: use the named `TryGet…` accessors, then `TryGetRawError`. Case B: `ex.Error.StatusCode` + `ReadAsString()`. Map: `sdk-map.md` error-handling model.

A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.

A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Tests that fake the SDK **MUST load `dotnet-testing`** — the seam is the `HttpClient` constructor argument, not the generated controllers.

---

## Trap notes (index)

- ⚠ Step 1 (client registration) — HttpClient/factory lifetime vs the SDK wrapper. **MUST load `dotnet-client-initialization`**.
- ⚠ Step 1 (auth) — Basic slots and 401/403 vs domain errors. **MUST load `dotnet-authentication`**.
- ⚠ Step 1 (server URL) — verbatim `BaseUrl` vs `Site`, and which environment node is read. **MUST load `dotnet-configuration-resilience`**.
- ⚠ Step 1 / Step 4 — what retry/timeout options actually bound, and whether a failed POST subscribe/customer-create can be re-sent. **MUST load `dotnet-configuration-resilience`**.
- ⚠ Steps 2–5 (calls) — optional params with no C# default; cancellation token is `ct`. **MUST load `dotnet-calling-endpoints`**.
- ⚠ Steps 2–5 (models) — envelopes, `required` inits, `StringEnum<T>`. **MUST load `dotnet-models`**.
- ⚠ All steps (errors) — Case A vs B per row; `JsonException` from 2xx **and** from failed error-object construction. **MUST load `dotnet-error-handling`**.
- ⚠ Tests — HttpClient seam. **MUST load `dotnet-testing`**.

---

## REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — constructing `MaxioAdvancedBillingClient` / `AddMaxioAdvancedBillingClient`, HttpClient lifetime |
| `dotnet-authentication` | Step 1 — `BasicAuth` username = API key, password = `"x"`, config-sourced credentials |
| `dotnet-configuration-resilience` | Step 1 — `Server.Production.{Us\|Eu}.Site` / `.BaseUrl`, Environment vs Server, retries/timeouts, **POST transport retries**, list pagination |
| `dotnet-calling-endpoints` | Steps 2–5 — named arguments, `ct:`, throw-only operations |
| `dotnet-models` | Steps 2–5 — envelopes, required members, StringEnum, cents vs dollars |
| `dotnet-error-handling` | All SDK calls — Case A/B, TryGet accessors, **both** `JsonException` directions below |
| `dotnet-testing` | Test doubles for the integration layer |

`JsonException` reaches the boundary from two directions and they need opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## Assumptions & Blockers

### Assumptions

- Maxio customer `reference` = the JWT **user id** (stable). Username/email are only used to populate `CreateCustomer` name/email, not as the lookup key.
- `CreateCustomer.FirstName` / `LastName` / `Email` are taken from Identity (email/username). If last name is missing, use a single-token fallback (e.g. copy username / `"Customer"`) so the three `!req` strings are non-empty. If email is missing, fail the request — do not invent an address.
- `MAXIO_ENVIRONMENT=sandbox` means “use the sandbox **site** credentials,” not `ServerEnvironment.Sandbox` (does not exist). Default `ServerEnvironment.Us`.
- No-card collection method is `CollectionMethod.Remittance`. If a given site is still on legacy Statements architecture, create may 422 and the site would need `CollectionMethod.Invoice` instead — that is site config, not a missing SDK operation.
- “Already has an active subscription” is implemented as the live-state set listed under `ListCustomerSubscriptions`, matched on **product handle**.
- Next billing date returned to the user is `Subscription.NextAssessmentAt`.
- Plan price displayed from list is `Product.PriceInCents` (cents). Confirmation may use `ProductPriceInCents`.
- Config keys: `Maxio:ApiKey` ← `MAXIO_API_KEY`, `Maxio:Subdomain` ← `MAXIO_SITE_SUBDOMAIN`, `Maxio:ProductFamilyHandle` ← `MAXIO_DEFAULT_PRODUCT_FAMILY`, optional `Maxio:BaseUrl`. Values never hardcoded.
- Double-click protection is **read-then-create**. `CreateSubscription` has **no** documented unique constraint on (customer, product). A remaining race (two concurrent POSTs both seeing “no live sub”) plus transport retries on POST can still create two subscriptions; application-level per-user serialization is outside the SDK. Customer create **is** unique on `reference` (documented on the create operation).
- Handle string comparison is exact against the values Maxio returns / the client sends.

### Blockers

None for the scoped capabilities: list-by-family-handle, customer find-or-create by reference, subscribe by product handle without a card, list a customer's subscriptions, and live-state idempotency are all present on the map.

Not blockers, but trust caveats (map evidence only):

- `CustomerErrorResponse1.Errors` generated shape (`PerPage` / `PricePoint`) does not look like a customer-create error body — **UNVERIFIED** live payload; follow the defensive 422 path above.
- `ListProductFamilies` and `ListCustomerSubscriptions` declare **no pagination**. If a site returns a truncated list with no page params, further pages cannot be requested through this SDK. Unlikely for the hero catalog; **UNVERIFIED** for a huge foreign site.
- Subscription `reference` uniqueness is **UNVERIFIED**; do not rely on `FindSubscription` as the only idempotency mechanism.
