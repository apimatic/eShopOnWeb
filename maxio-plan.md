# Maxio Advanced Billing — eShopOnWeb subscribe flow

Package `AsadAli.AdvancedBilling.Sdk` · root namespace `MaxioAdvancedBilling` · map stamp `v1.0.2` (`15db14b`).

Requested HTTP surface (app routes — not SDK): `GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions` on **src/PublicApi**, JWT-authenticated.

Sandbox catalog (handles stable; numeric IDs are not): family `eshop-subscribe`; products `eshop-pro` ($299.00/mo, default subscribe target), `basic-plan` ($29.00/mo); metered component `api-call` is seeded but **out of this sheet’s operations** (hero flow does not record usage).

---

## 1. Scope & sequence

| Step | App capability | SDK operations |
|---|---|---|
| 1 | Register client + bind `Maxio:` config | Construction only: `new MaxioAdvancedBillingClient(httpClient, options)` or `IServiceCollection.AddMaxioAdvancedBillingClient` |
| 2 | `GET /api/subscription-plans` — list plans in the configured family | `client.ProductFamilies.ListProductsForProductFamily` with `productFamilyId` = `"handle:" + Maxio:ProductFamilyHandle` |
| 3 | Resolve caller → Maxio customer (idempotent) | `client.Customers.ReadCustomerByReference` then, on miss, `client.Customers.CreateCustomer` (`Customer.Reference` = eShop user key). On 422 race, `ReadCustomerByReference` again |
| 4 | `POST /api/subscriptions` — enroll (idempotent) | `client.Subscriptions.FindSubscription` by a deterministic `reference`; on miss, `client.Subscriptions.CreateSubscription` with `ProductHandle` + existing customer (`CustomerId` or `CustomerReference`) + same `Reference` + `PaymentCollectionMethod` = non-`Automatic` (see CreateSubscription row); **omit** all payment-profile / card fields |
| 5 | Confirm plan/price/state/next-billing to the caller | Read `SubscriptionResponse.Subscription` (nullable envelope) after create/find; nested `Product` for plan/price |
| 6 | `GET /api/my-subscriptions` | `ReadCustomerByReference` then `client.Customers.ListCustomerSubscriptions` |
| 7 | Error boundary around every call | Per-operation Case A/B below; also `JsonException` (see trap notes) |

Defensive extra on step 4 (subscription `reference` uniqueness is **not** stated in the map the way customer `reference` is): if `FindSubscription` misses, you may still scan `ListCustomerSubscriptions` for an existing row whose nested `Product.Handle` matches the requested handle before creating. That scan is optional hardening, not a substitute for setting `CreateSubscription.Reference`.

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

### 2.1 Client construction, auth, server node

| Fact | Contract | Source |
|---|---|---|
| Package / namespace | NuGet `AsadAli.AdvancedBilling.Sdk`; `using MaxioAdvancedBilling;` | `sdk-map.md` |
| Client | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` — **only** ctor `MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` |
| Options type | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` | `sdk-map.md` |
| Options members | `Environment`: `MaxioAdvancedBilling.Servers.ServerEnvironment`; `Retry`: `MaxioAdvancedBilling.Core.Configuration.RetryOptions`; `Server`: `MaxioAdvancedBilling.ServerOptions`; `BasicAuth`: `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md` |
| Auth | HTTP Basic only. `options.BasicAuth = new BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }`. `Username` and `Password` are `required string`. Bind `Username` from **`Maxio:ApiKey`**. Never hard-code the key. | `sdk-map.md`; `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Environments | `ServerEnvironment.Us` (wire `US`, **default** — `ServerEnvironment.Default()` returns `Us`) → US template; `ServerEnvironment.Eu` (wire `EU`) → EU template. There is **no** Sandbox enum. Target the sandbox **site** via `Site`, with `Environment = ServerEnvironment.Us`. | `sdk-map.md`; `Servers/ServerEnvironment.cs` |
| Production URL templates | US `https://{site}.chargify.com`; EU `https://{site}.ebilling.maxio.com`. `{site}` defaults to the literal `"subdomain"` until you set it. | `sdk-map.md`; `Servers/ProductionOptions.cs` |
| Subdomain | `options.Server.Production.Us.Site` = value of **`Maxio:Subdomain`**. Nested types: `MaxioAdvancedBilling.ServerOptions` (root) → `MaxioAdvancedBilling.Servers.ProductionOptions` → `ProductionOptions.UsOptions`. `Us.Site` default `"subdomain"`; `Us.BaseUrl` default `"https://{site}.chargify.com"`. | `sdk-map.md`; `ServerOptions.cs`; `Servers/ProductionOptions.cs` |
| Optional base URL | When **`Maxio:BaseUrl`** is set, assign it to `options.Server.Production.Us.BaseUrl` (verbatim). That is the Production-US override point. Keep `Environment = ServerEnvironment.Us` so this node is the one resolved. If the override string still contains `{site}`, `Site` is still substituted; if it does not, `Site` is unused for URL building. | `sdk-map.md` |
| DI helper | `MaxioAdvancedBilling.ServiceCollectionExtensions.AddMaxioAdvancedBillingClient(this IServiceCollection, Action<MaxioAdvancedBillingClientOptions>? configure = null)` | `sdk-map.md`; `ServiceCollectionExtensions.cs` |
| Retry options | All members `required`; build a full instance or start from `RetryOptions.Default()`. Members: `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout` (`TimeSpan?`), `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry`. Do **not** treat these as settled semantics here. | `sdk-map.md` |
| Config keys (app) | Bind **`Maxio:ApiKey`**, **`Maxio:Subdomain`**, **`Maxio:ProductFamilyHandle`**, optional **`Maxio:BaseUrl`**. Hard-code none of these values. | YOUR CALL — not in the map |
| Caller identity | eShop user key / name / email come from the JWT; this sheet does not name token claims. | YOUR CALL — not in the map |

Controllers used: `client.ProductFamilies` (`MaxioAdvancedBilling.Api.ProductFamilies`, source `Api/ProductFamilies.cs`), `client.Customers` (`Api/Customers.cs`), `client.Subscriptions` (`Api/Subscriptions.cs`).

### 2.2 Operations

#### List products in a family (plans catalog)

| | |
|---|---|
| Controller | `client.ProductFamilies` |
| Method | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| Must-pass | `productFamilyId` required. Eight params `dateField` … `include` are nullable with **no default** → pass `null` to skip. `page` defaults 1, `perPage` defaults 20. |
| Path id | `productFamilyId` is `string`. Pass **`"handle:"` + `Maxio:ProductFamilyHandle`** (XML: “Either the product family's id or its handle prefixed with `handle:`”). Example for this site: `"handle:eshop-subscribe"`. Do **not** use `ReadProductFamily` for a handle: its C# signature is `ReadProductFamily(int id, …)` even though its Notes mention `handle:my-family`. |
| Filter model | `MaxioAdvancedBilling.Models.ListProductsFilter` — `Ids (ids): IReadOnlyList<int>?`, `PrepaidProductPricePoint (prepaid_product_price_point): PrepaidProductPricePointFilter?`, `UseSiteExchangeRate (use_site_exchange_rate): bool?`. **No family-handle field.** Leave `filter: null`. Leave `include: null` (only value is prepaid price point). |
| Returns | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>` |
| Envelope | `ProductResponse.Product` (`product`) : `Product` **!req** |
| Error | **Case A** `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>` · `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] |
| Pagination | manual `page` + `perPage` (default 20). Two seeded plans fit one page; still pass an explicit `perPage` or walk pages until a short page. |
| Notes-tied fields left out | date/archive/`include` filters — not needed to list current family products. |
| Source | `operations/ProductFamilies.md`; `Api/ProductFamilies.cs` (handle prefix); `records-2-Cr-Ne.md` (`ListProductsFilter`); `records-3-Of-Su.md` (`ProductResponse`, `Product`) |

`ListProducts` (`GET /products.json`) lists the **site**, not a family, and `ListProductsFilter` cannot select by family handle — do not use it for this catalog.

#### Lookup customer by app reference

| | |
|---|---|
| Controller | `client.Customers` |
| Method | `ReadCustomerByReference(string reference, CancellationToken ct = default)` |
| HTTP | `GET /customers/lookup.json?reference={reference}` |
| Returns | `MaxioAdvancedBilling.Models.CustomerResponse` |
| Envelope | `CustomerResponse.Customer` (`customer`) : `Customer` **!req** |
| Error | **Case B** `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` · `StatusCode` · `ReadAsString()` / `ReadAsJson<T>()` / `ReadAsBytes()`. Map does **not** name the miss status. |
| Miss handling | **UNVERIFIED** which `HttpStatusCode` the provider sends when no customer exists. Catch Case B and branch on `ex.Error.StatusCode`; treat not-found as “create next”, treat other statuses as failure. Do not parse `Exception.ToString()`. |
| Source | `operations/Customers.md`; `records-2-Cr-Ne.md` |

#### Create customer

| | |
|---|---|
| Controller | `client.Customers` |
| Method | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, **no default → must pass explicitly** |
| HTTP | `POST /customers.json` |
| Request envelope | `MaxioAdvancedBilling.Models.CreateCustomerRequest` · `Customer (customer): CreateCustomer !req` |
| Request fields to set (`CreateCustomer`) | **Required:** `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`. **Idempotency (Notes):** `Reference (reference): string?` — “you may only create one customer for a given reference value”; “If provided, the `reference` value must be unique. It represents a unique identifier for the customer from your own app.” |
| Fields left out | `CcEmails`, `Organization`, `Address`/`Address2`/`City`/`State`/`Zip`/`Country`/`Phone`, `Locale`, `VatNumber`, `TaxExempt`/`TaxExemptReason`, `ParentId`, `SalesforceId` — Notes do not tie these to acceptance for this flow. Country/state ISO rules apply **only if** you later send those fields. |
| Returns | `CustomerResponse` · inner `Customer` **!req** |
| Error | **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>` · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| 422 payload | `CustomerErrorResponse1.Errors (errors): Errors?` where `MaxioAdvancedBilling.Models.Errors` is the record `{ PerPage (per_page): IReadOnlyList<string>?, PricePoint (price_point): IReadOnlyList<string>? }` (`Models/Errors.cs`). A separate union `Errors1` (`CustomerError` \| `IReadOnlyList<string>`) exists but is **not** the type on this model. **UNVERIFIED** whether a live 422 body matches that `Errors` record. Extract best-effort from `TryGetCustomerErrorResponse1` when it succeeds; if deserialization throws `JsonException`, fall back to a generic message (see trap notes). |
| Race | Concurrent double-create with the same `reference`: expect 422; then `ReadCustomerByReference` and proceed with the existing `Customer.Id`. |
| Source | `operations/Customers.md`; `records-1-Ac-Cr.md`; `records-2-Cr-Ne.md`; `Models/CustomerErrorResponse1.cs`; `Models/Errors.cs`; `Errors/CreateCustomerError.cs` |

#### Find subscription by app reference (idempotent enroll)

| | |
|---|---|
| Controller | `client.Subscriptions` |
| Method | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` nullable, **must pass explicitly** |
| HTTP | `GET /subscriptions/lookup.json?reference={reference}` |
| Returns | `MaxioAdvancedBilling.Models.SubscriptionResponse` |
| Envelope | `SubscriptionResponse.Subscription` (`subscription`) : `Subscription?` (**nullable** — go one level down; null-check) |
| Error | **Case A** `SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>` · `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback] |
| Notes | “Finds a subscription by its reference.” Source XML: `Reference` on create is “The reference value (provided by your app) for the subscription itself.” Map does **not** state uniqueness the way `CreateCustomer` does. |
| Source | `operations/Subscriptions.md`; `records-4-Su-We.md`; `Models/CreateSubscription.cs`; `Errors/FindSubscriptionError.cs` |

#### Create subscription (no payment method)

| | |
|---|---|
| Controller | `client.Subscriptions` |
| Method | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, **must pass explicitly** |
| HTTP | `POST /subscriptions.json` |
| Request envelope | `CreateSubscriptionRequest` · `Subscription (subscription): CreateSubscription !req` |
| Notes (acceptance) | “Specify the product with `product_id` or `product_handle`.” “Identify an existing customer with `customer_id` or `customer_reference`.” “Payment information may be required … depending on the options for the Product being subscribed.” “Optionally, include an existing payment profile using `payment_profile_id`.” Product `RequireCreditCard = false` does **not** skip collecting a signup balance under `automatic` collection. |
| Fields to set | `ProductHandle (product_handle): string?` (handles `eshop-pro` / `basic-plan`; default target `eshop-pro` is the task’s default, not an SDK default). Identify customer with **either** `CustomerId (customer_id): int?` **or** `CustomerReference (customer_reference): string?` (same string used as customer `reference`). `Reference (reference): string?` — app-owned subscription key for `FindSubscription`. **`PaymentCollectionMethod (payment_collection_method): CollectionMethod?`** — optional on the model; **must be set** so the site default (`automatic`) is not used. Set `CollectionMethod.Remittance` (wire `remittance`) for Relationship Invoicing, or `CollectionMethod.Invoice` (wire `invoice`) for legacy Statements. Do **not** set `CollectionMethod.Automatic` (wire `automatic`) — that is the 422 below. Do **not** set `CollectionMethod.Prepaid` (wire `prepaid`) for these catalog products. |
| Fields left out (do not send) | `ProductId` (prefer handle; IDs are not stable). `ProductPricePointHandle` / `ProductPricePointId` (default price point). **`PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes` — map does not require these for no-card enroll; do not invent card numbers.** `CustomerAttributes` (customer already exists). `Components` (metered `api-call` not part of enroll). `CouponCode`/`CouponCodes`, `CustomPrice`, `Group`, `OfferId`, prepaid/calendar/dunning/import date fields (`NextBillingAt` / `InitialBillingAt` / `DeferSignup` are import/schedule levers, not the no-card collection lever). |
| Returns | `SubscriptionResponse` · inner `Subscription?` |
| Error | **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>` · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| 422 payload | `ErrorListResponse1.Errors (errors): IReadOnlyList<string> !req`. Live (this session): omitting `PaymentCollectionMethod` → `TryGetErrorListResponse1` succeeded with the single string `"No payment method was on file for the $299.00 balance"`. That is automatic collection of the signup balance with no profile — not a missing `CreditCardAttributes` shape. |
| Not these ops | Do **not** send card attributes. Do **not** call `UpdateProduct` to flip `RequireCreditCard` (already `false` on both plans; that flag is not this 422). Do **not** substitute `PreviewSubscription` (`POST /subscriptions/preview.json` — “A subscription will not be created”). Optional architecture probe only: `client.Sites.ReadSite(CancellationToken ct = default)` → `SiteResponse` envelope `Site (site): Site !req`; read `RelationshipInvoicingEnabled (relationship_invoicing_enabled): bool?` and `DefaultPaymentCollectionMethod (default_payment_collection_method): string?`; **Case B** `SdkException<RawError>`. If `RelationshipInvoicingEnabled == true` → `Remittance`; if `false` → `Invoice`. **UNVERIFIED** which this sandbox is until that read or a 422 on the wrong member. |
| Source | `operations/Subscriptions.md`; `records-2-Cr-Ne.md`; `enums.md` (`CollectionMethod`); `Models/CreateSubscription.cs`; `Errors/CreateSubscriptionError.cs`; `operations/Sites.md`; `records-3-Of-Su.md` (`Site` / `SiteResponse`); `operations/Products.md` (`UpdateProduct` — not used) |

#### List a customer’s subscriptions

| | |
|---|---|
| Controller | `client.Customers` (not `client.Subscriptions`) |
| Method | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` |
| HTTP | `GET /customers/{customer_id}/subscriptions.json` |
| Returns | `IReadOnlyList<SubscriptionResponse>` |
| Envelope | each item: `SubscriptionResponse.Subscription` (`subscription`) : `Subscription?` |
| Error | **Case B** `SdkException<RawError>` |
| Pagination | **none** (full list in one call) |
| Source | `operations/Customers.md`; `records-4-Su-We.md` |

`ListSubscriptions` is site-wide with 14 must-pass-null filters and **no customer-id parameter** — do not use it for `GET /api/my-subscriptions`.

### 2.3 Response fields the integration reads

All records below are `MaxioAdvancedBilling.Models.*` unless noted. Envelopes wrap one property — **never** treat the envelope type as the product/customer/subscription.

#### `ProductResponse` / `Product` (catalog + nested on subscription)

| C# (wire) | Type | Why |
|---|---|---|
| `Handle (handle)` | `string?` | Plan identity (`eshop-pro`, `basic-plan`) |
| `Name (name)` | `string?` | Display |
| `PriceInCents (price_in_cents)` | `long?` | Price (29900 / 2900 for the seeded plans — **UNVERIFIED** live cents; read the field, do not hard-code) |
| `Interval (interval)` + `IntervalUnit (interval_unit)` | `int?` + `IntervalUnit?` | Period |
| `ProductPricePointHandle (product_price_point_handle)` | `string?` | Default price-point handle |
| `RequireCreditCard (require_credit_card)` | `bool?` | Confirm payment not required |
| `ProductFamily (product_family)` | `ProductFamily?` | `ProductFamily.Handle (handle)` should match `eshop-subscribe` |
| `Id (id)` | `int?` | Maxio id (unstable across sites — do not persist as the public plan key) |

`ProductFamily`: `Id`, `Name`, `Handle`, `AccountingCode`, `Description`, timestamps. Source: `records-3-Of-Su.md`.

#### `CustomerResponse` / `Customer`

| C# (wire) | Type | Why |
|---|---|---|
| `Id (id)` | `int?` | Pass to `ListCustomerSubscriptions` / `CreateSubscription.CustomerId` |
| `Reference (reference)` | `string?` | App key; `CreateSubscription.CustomerReference` |
| `Email (email)`, `FirstName (first_name)`, `LastName (last_name)` | `string?` | Echo / debug |
| `Maxioid (maxioid)` | `string?` | Alternate Maxio id string if you surface it |

Source: `records-2-Cr-Ne.md`.

#### `SubscriptionResponse` / `Subscription` (plan / price / state / next billing)

There is **no** `next_billing_at` on the `Subscription` record. Read these:

| C# (wire) | Type | Why |
|---|---|---|
| `Id (id)` | `int?` | Maxio subscription id |
| `Reference (reference)` | `string?` | App idempotency key |
| `State (state)` | `SubscriptionState?` | Lifecycle |
| `ProductPriceInCents (product_price_in_cents)` | `long?` | Price on the subscription |
| `CurrentBillingAmountInCents (current_billing_amount_in_cents)` | `long?` | Current billed amount |
| `NextAssessmentAt (next_assessment_at)` | `DateTimeOffset?` | Next assessment |
| `CurrentPeriodEndsAt (current_period_ends_at)` | `DateTimeOffset?` | Period end (Update Notes elsewhere say this is how you verify a billing-date change; create responses do not echo a `next_billing_at` key) |
| `CurrentPeriodStartedAt (current_period_started_at)` | `DateTimeOffset?` | Period start |
| `Product (product)` | `Product?` | Nested plan: `Handle`, `Name`, `PriceInCents`, interval |
| `Customer (customer)` | `Customer?` | Nested customer |
| `PaymentCollectionMethod (payment_collection_method)` | `CollectionMethod?` | Collection mode |

Source: `records-3-Of-Su.md`, `records-4-Su-We.md`.

**UNVERIFIED:** which of `NextAssessmentAt` vs `CurrentPeriodEndsAt` the live create payload populates as “next billing date”. Return **both** to the caller (or prefer whichever is non-null). Do not invent a `next_billing_at` DTO field as if it were an SDK member.

### 2.4 Enums in scope (`MaxioAdvancedBilling.Models.Enums` — `StringEnum<T>`, **not** C# enums)

Construct with static members or `Type.FromValue("wire")`. Read via the companion skill — do not assume `.ToString()` is the wire value.

| Enum | Members (C# name (wire)) | Source |
|---|---|---|
| `IntervalUnit` | `Day (day)`, `Month (month)` | `enums.md` |
| `ExpirationIntervalUnit` | `Day (day)`, `Month (month)`, `Never (never)` | `enums.md` |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)`. XML/enum summary: legacy Statements valid options = `invoice`, `automatic`; Relationship Invoicing valid options = `remittance`, `automatic`, `prepaid`. No-card enroll = **not** `Automatic`. RI no-card member = `Remittance (remittance)`. Legacy no-card member = `Invoice (invoice)`. `Prepaid (prepaid)` is RI prepaid, not this catalog. | `enums.md`; `Models/CreateSubscription.cs` |
| `ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)` | `enums.md` |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` | `enums.md` |
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` | `enums.md` |
| `PricePointType` | `Catalog (catalog)`, `Default (default)`, `Custom (custom)` | `enums.md` |

### 2.5 Error core (every operation)

| Type | Namespace | Members | Source |
|---|---|---|---|
| `SdkException<TError>` | `MaxioAdvancedBilling.Core.Exceptions` | `required TError Error` | `sdk-map.md`; `Core/Exceptions/SdkException.cs` |
| `ApiError` | `MaxioAdvancedBilling.Core.ErrorResponse` | `TryGetRawError(out RawError): bool` | `sdk-map.md` |
| `RawError` | `MaxioAdvancedBilling.Core.ErrorResponse` | `StatusCode: HttpStatusCode`; `ReadAsBytes()`; `ReadAsString()`; `ReadAsJson<T>()` | `sdk-map.md` |
| Case A typed errors | `MaxioAdvancedBilling.Errors` | per-op `TryGet…` + inherited `TryGetRawError` | `sdk-map.md` |

No `{Operation}Result` / no-throw variants exist. Every call is throw-only.

Idempotency keys (SDK):

| Resource | App-owned unique field | Lookup | Provider uniqueness in map/Notes |
|---|---|---|---|
| Customer | `CreateCustomer.Reference` / `Customer.Reference` (`reference`) | `ReadCustomerByReference(reference)` | **Yes** — one customer per reference |
| Subscription | `CreateSubscription.Reference` / `Subscription.Reference` (`reference`) | `FindSubscription(reference)` | **Not stated** as unique. Customer is also identified by `customer_id` / `customer_reference`. Product by `product_handle` / `product_id`. |
| Product / family | handles (`handle`, path `handle:…`) | `ListProductsForProductFamily`; `ReadProductByHandle(string apiHandle)` if you ever need a single product | Handles are the stable public ids (task) |

`ReadProductByHandle(string apiHandle, CancellationToken ct = default)` → `ProductResponse`; **Case B** `SdkException<RawError>`; source `operations/Products.md`. Not required if create uses `product_handle` directly.

---

## 3. Trap notes

⚠ Step 1 (client registration) — the ctor takes an `HttpClient` you do not own ad hoc per request; DI vs manual lifetime and whether the SDK wrapper is long-lived are not visible from the signature. **MUST load `dotnet-client-initialization`** before writing `new MaxioAdvancedBillingClient` or `AddMaxioAdvancedBillingClient`.

⚠ Step 1 (auth) — Basic credentials must be on the options before the first call; `Password` is the literal scheme value, not an app secret, and the API key must come from **`Maxio:ApiKey`**. A 401/403 is a credentials/server-node problem, not a payload problem. **MUST load `dotnet-authentication`** before wiring `BasicAuth`.

⚠ Step 1 (server / retry) — `Retry` / `Timeout` on options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; `HttpMethodsToRetry` does not tell you whether a failed **write** (`CreateCustomer`, `CreateSubscription`) can execute more than once. **MUST load `dotnet-configuration-resilience`** before registering or tuning the client (including `Maxio:BaseUrl`).

⚠ Steps 2–6 (calls) — eight-plus optional parameters on `ListProductsForProductFamily` have **no C# default**; a positional call mis-binds. Named arguments; cancellation is `ct:`. **MUST load `dotnet-calling-endpoints`** before the first `client.{Group}.{Op}` call.

⚠ Steps 2–6 (models) — envelopes wrap one property (`ProductResponse.Product`, `CustomerResponse.Customer`, `SubscriptionResponse.Subscription` which is **nullable**); `CreateCustomer` has three `required` members; enums are `StringEnum<T>` not C# enums; unmodeled JSON is dropped. **MUST load `dotnet-models`** before constructing payloads or mapping responses.

⚠ Steps 3–7 (error boundary) — Case A vs Case B differ per operation (`CreateCustomer`/`CreateSubscription`/`FindSubscription`/`ListProductsForProductFamily` are A; `ReadCustomerByReference`/`ListCustomerSubscriptions` are B). `TryGetRawError` is not a catch-all on the wrong generic. **MUST load `dotnet-error-handling`** before any `try/catch`.

⚠ Error boundary — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Error boundary — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. This is the live risk on `CreateCustomer` 422 if the body is not the generated `Errors` (`per_page` / `price_point`) record. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Tests — the `HttpClient` constructor argument is the test seam. **MUST load `dotnet-testing`** before stubbing the SDK.

---

## 4. REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — ctor, `HttpClient` lifetime, `AddMaxioAdvancedBillingClient` |
| `dotnet-authentication` | Step 1 — `BasicAuthCredentials`, `Maxio:ApiKey` |
| `dotnet-configuration-resilience` | Step 1 — `Site` / `BaseUrl`, retries, timeouts, pagination |
| `dotnet-calling-endpoints` | Steps 2–6 — named args, must-pass nulls, `ct:` |
| `dotnet-models` | Steps 2–6 — envelopes, `required`, `StringEnum<T>`, omitted JSON |
| `dotnet-error-handling` | Step 7 — Case A/B, `TryGet…`, **both** `JsonException` paths |
| `dotnet-testing` | Tests for the integration layer |

---

## 5. Assumptions & Blockers

### Assumptions

- Hosting is US (`ServerEnvironment.Us`). The sandbox site is selected by **`Maxio:Subdomain`**, not by a distinct SDK environment.
- Seeded products have `RequireCreditCard = false`; omit all payment-profile / card fields. Signup still collects the product balance unless `PaymentCollectionMethod` is a non-`Automatic` member (`Remittance` on RI, `Invoice` on legacy Statements). Live 422 `"No payment method was on file for the $299.00 balance"` is that omission, not a missing card shape. **UNVERIFIED** which architecture this sandbox uses until `ReadSite` or a 422 on the wrong member.
- The JWT yields a stable user key (for customer `reference`), plus values for `CreateCustomer`’s required `FirstName`, `LastName`, `Email`. Claim names are YOUR CALL.
- `POST /api/subscriptions` product handle: request body vs default `eshop-pro` is YOUR CALL. Family handle for listing comes from **`Maxio:ProductFamilyHandle`** (expected `eshop-subscribe`).
- Metered component `api-call` is out of scope for these three endpoints.
- Subscription `reference` string format (e.g. user-key + product handle) is YOUR CALL. It must be stable across double-clicks of the **same** enroll.
- Which `SubscriptionState` values count as “already enrolled” when scanning `ListCustomerSubscriptions` is YOUR CALL.

### Blockers

*(none)* — every in-scope operation exists on the map. The customer 422 payload shape and the subscription-reference uniqueness rule are labeled **UNVERIFIED** above with defensive handling; they do not block planning.
