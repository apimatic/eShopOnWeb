# Maxio Advanced Billing — eShopOnWeb Subscribe hero flow

Package `AsadAli.AdvancedBilling.Sdk` · root namespace `MaxioAdvancedBilling` · map stamp `v1.0.2` / `15db14b`. Additive PublicApi capability (does not replace cart/checkout). Brief HTTP surface (JWT-authenticated) on `src/PublicApi`: `GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions`.

## 1. Scope & sequence

| Step | What | Operations |
|---|---|---|
| 1 | Register the SDK client (Basic auth, US sandbox site, optional BaseUrl override) | client construction — no HTTP call |
| 2 | `GET /api/subscription-plans` — list plans in the configured product family | `client.ProductFamilies.ListProductsForProductFamily` |
| 3 | Resolve the authenticated shopper to a Maxio customer (lookup by reference, create only on miss) | `client.Customers.ReadCustomerByReference` then, on not-found, `client.Customers.CreateCustomer` |
| 4 | `POST /api/subscriptions` — idempotent subscribe: if an **active** subscription to the same product handle already exists, return it; otherwise create | `client.Customers.ListCustomerSubscriptions` then (when needed) `client.Subscriptions.CreateSubscription` |
| 5 | `GET /api/my-subscriptions` — shopper's subscriptions (plan / price / state / next billing) | `client.Customers.ReadCustomerByReference` + `client.Customers.ListCustomerSubscriptions` |
| 6 | Integration error boundary around every SDK call | (see §3 + §4) |
| 7 | Tests for the integration seam | (see §4 `dotnet-testing`) |

Not in this hero flow: metered component `api-call`, payment profiles, coupons, plan changes, webhooks.

Identify products by **handle** (`eshop-pro`, `basic-plan`), never by the numeric IDs in the sandbox table (those may be stale). Family handle from `Maxio:ProductFamilyHandle` (seeded `eshop-subscribe`).

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

### 2.1 Client construction & auth

| Fact | Value | Source |
|---|---|---|
| NuGet package | `AsadAli.AdvancedBilling.Sdk` | `sdk-map.md` |
| Client type | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` | `sdk-map.md` · `MaxioAdvancedBillingClient.cs` |
| Only constructor | `MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` |
| Options type | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` | `sdk-map.md` · `MaxioAdvancedBillingClientOptions.cs` |
| Options members | `Environment`: `MaxioAdvancedBilling.Servers.ServerEnvironment` (default `ServerEnvironment.Default()` → `Us`); `Retry`: `MaxioAdvancedBilling.Core.Configuration.RetryOptions` (default `RetryOptions.Default()`); `Server`: `MaxioAdvancedBilling.ServerOptions`; `BasicAuth`: `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md` · `MaxioAdvancedBillingClientOptions.cs` |
| Auth | HTTP Basic. `BasicAuthCredentials.Username` = API key; `BasicAuthCredentials.Password` = literal `"x"`. Both members `required`. | `sdk-map.md` · `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Environments | `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (wire `US`, default) → `https://{site}.chargify.com`; `.Eu` (wire `EU`) → `https://{site}.ebilling.maxio.com`. **No Sandbox enum.** Sandbox is a **site**, selected via subdomain / BaseUrl, not via `Environment`. Target this brief at `Us`. | `sdk-map.md` · `Servers/ServerEnvironment.cs` |
| Site / BaseUrl | `options.Server.Production` is `MaxioAdvancedBilling.Servers.ProductionOptions`. Nested `UsOptions`: `BaseUrl` default `"https://{site}.chargify.com"`; `Site` default `"subdomain"`. Set `options.Server.Production.Us.Site` from `Maxio:Subdomain`. When `Maxio:BaseUrl` is set, assign that value **verbatim** to `options.Server.Production.Us.BaseUrl` (do not derive a host from subdomain). `Resolve` always also passes `Site` as a `{site}` template param — whether a BaseUrl with no `{site}` ignores it is **UNVERIFIED**. | `sdk-map.md` · `ServerOptions.cs` · `Servers/ProductionOptions.cs` |
| DI helper | `MaxioAdvancedBilling.ServiceCollectionExtensions.AddMaxioAdvancedBillingClient(this IServiceCollection, Action<MaxioAdvancedBillingClientOptions>? configure = null)` | `sdk-map.md` · `ServiceCollectionExtensions.cs` |
| Settings keys (never hardcode values) | `Maxio:ApiKey` → `BasicAuth.Username`; `Maxio:Subdomain` → `Server.Production.Us.Site`; `Maxio:ProductFamilyHandle` → family handle for Step 2; `Maxio:BaseUrl` → when set, `Server.Production.Us.BaseUrl` verbatim | brief · `YOUR CALL — not in the map` for how the host binds these (including env `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_DEFAULT_PRODUCT_FAMILY`) |
| `MAXIO_ENVIRONMENT` | Listed as an env var in the brief; **no** matching settings key among the four above. SDK has only `Us` / `Eu`. For this sandbox target use `ServerEnvironment.Us`. Whether to bind `MAXIO_ENVIRONMENT` at all is | `YOUR CALL — not in the map` |

Controllers used: `client.ProductFamilies`, `client.Customers`, `client.Subscriptions` (`MaxioAdvancedBilling.Api.*`, properties on `MaxioAdvancedBillingClient`).

### 2.2 Operations

| # | Controller · method | Signature (order, required-nullable) | Request | Response envelope + fields this flow reads | Error | Pagination | Source |
|---|---|---|---|---|---|---|---|
| A | `client.ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, MaxioAdvancedBilling.Models.ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 params `dateField`…`include` nullable **no default → pass `null` explicitly**. | Path `productFamilyId`: product-family **numeric id as string**, **or** handle prefixed with `handle:` (XML param docs). Pass `"handle:" +` value of `Maxio:ProductFamilyHandle` (seeded `eshop-subscribe`). Do **not** use `ReadProductFamily` for this — its signature is `int id` and cannot take a handle, even though its Notes mention `handle:my-family`. Leave `filter` / date / `include` / `includeArchived` unset (`null`). `ListProductsFilter` cannot filter by family handle (`Ids`, `PrepaidProductPricePoint`, `UseSiteExchangeRate` only). | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>`. Envelope: `ProductResponse.Product` (`Product !req`). Inner `MaxioAdvancedBilling.Models.Product`: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?` (cents, not dollars; $299.00 → `29900`), `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`. Also available (not required for the browse DTO): `RequireCreditCard (require_credit_card): bool?`, `RequestCreditCard (request_credit_card): bool?`, `ProductFamily (product_family): ProductFamily?`. | **Case A** `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`. Accessors: `TryGetString(out string)` **[404]** · `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` fallback. No-throw variant: absent. | Manual `page`+`perPage`. Default page 1, perPage 20. Source XML: max `perPage` 200 (values over 200 coerced to 200). Loop until a page is empty / shorter than `perPage`. | `operations/ProductFamilies.md` · `records-3-Of-Su.md` (`Product`, `ProductResponse`) · `Api/ProductFamilies.cs` (param: id **or** `handle:` prefix) |
| B | `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | Query `reference` ← `reference`. Pass the eShopOnWeb user id (brief). | `MaxioAdvancedBilling.Models.CustomerResponse`. Envelope: `Customer (customer): Customer !req`. Inner `MaxioAdvancedBilling.Models.Customer` (read at least): `Id (id): int?`, `Reference (reference): string?`, `FirstName`, `LastName`, `Email`. | **Case B** `SdkException<RawError>`. Accessors: `StatusCode: HttpStatusCode` · `ReadAsString()` · `ReadAsJson<T>()` · `ReadAsBytes()`. **Not-found** = `ex.Error.StatusCode == HttpStatusCode.NotFound` (404). No typed `TryGet…`. No-throw variant: absent. | none | `operations/Customers.md` · `records-2-Cr-Ne.md` |
| C | `client.Customers.CreateCustomer` | `CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, **no default → must pass explicitly**. | Envelope `CreateCustomerRequest.Customer (customer): CreateCustomer !req`. Inner `CreateCustomer` **C# required**: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`. Notes-tied for this flow: set `Reference (reference): string?` to the eShopOnWeb user id (Notes: only one customer per reference; reference is how you look the customer back up). Left out (not Notes-tied to acceptance here): address/state/country/phone/locale/vat/tax/parent/salesforce. Notes: if you send `Country`/`State`, they must be ISO codes. | `CustomerResponse` → `.Customer` (`Customer !req`). Read `Id` (needed for list/create-subscription). | **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`. Accessors: `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` **[422]** · `TryGetRawError(out RawError)` fallback. 422 is the uniqueness/validation path (Notes: reference must be unique). Payload: `CustomerErrorResponse1.Errors (errors): Errors?`, and `Errors` only declares `PerPage (per_page): IReadOnlyList<string>?` and `PricePoint (price_point): IReadOnlyList<string>?` — **suspicious shared model**, will not carry a `reference` uniqueness message. **Do not** detect “already exists” by reading `Errors.PerPage` / `Errors.PricePoint`. On 422 (or `JsonException` while the 422 body is being bound): call **B** again; if the customer is now present, use it (double-click / race). If still missing, surface a generic 422 via `TryGetRawError` / `ReadAsString()` best-effort. Label: **UNVERIFIED** whether a live uniqueness body deserializes into `CustomerErrorResponse1` at all (array-or-map `errors` vs this `Errors` object). No-throw variant: absent. | none | `operations/Customers.md` · `records-1-Ac-Cr.md` (`CreateCustomer`, `CreateCustomerRequest`) · `records-2-Cr-Ne.md` (`CustomerErrorResponse1`, `Errors`) · `Errors/CreateCustomerError.cs` |
| D | `client.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | Path `customer_id` ← `customerId` (Maxio customer **numeric** `Customer.Id`). **No** list-by-customer-reference operation exists — always resolve the customer with **B** first. | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>`. Envelope: `SubscriptionResponse.Subscription (subscription): Subscription?` — **nullable**; skip a row if null. Inner `MaxioAdvancedBilling.Models.Subscription` fields this flow reads: `Id (id): int?`; `State (state): SubscriptionState?`; `Product (product): Product?` (plan: `Product.Handle`, `Product.Name`); `ProductPriceInCents (product_price_in_cents): long?`; `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` (next billing — Update-subscription Notes: the wire does **not** echo `next_billing_at`; `current_period_ends_at` is the field to read); `NextAssessmentAt (next_assessment_at): DateTimeOffset?` (additional assessment timestamp). There is **no** `NextBillingAt` on `Subscription`. | **Case B** `SdkException<RawError>`. Not-found / other failures: `StatusCode` + `ReadAsString()`. No-throw variant: absent. | **none** (returns the customer's subscriptions in one shot) | `operations/Customers.md` · `records-3-Of-Su.md` (`Subscription`) · `records-4-Su-We.md` (`SubscriptionResponse`) |
| E | `client.Subscriptions.CreateSubscription` | `CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, **no default → must pass explicitly**. | Envelope `CreateSubscriptionRequest.Subscription (subscription): CreateSubscription !req`. Inner `CreateSubscription`: **no field is C# `required`**. Notes that decide acceptance: specify the product with `product_id` **or** `product_handle`; identify an existing customer with `customer_id` **or** `customer_reference` (or create one via `customer_attributes`); payment “may be required … depending on the options for the Product”. **Set for this flow:** `ProductHandle (product_handle): string?` = chosen plan handle (`eshop-pro` / `basic-plan`) — **do not** send `ProductId` (IDs stale). `CustomerId (customer_id): int?` = `Customer.Id` from B/C (preferred once looked up). **Leave out:** `ProductId`; `CustomerReference` (redundant if `CustomerId` is set); `CustomerAttributes` (customer already exists — do not create a second customer inline); all payment fields (`PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes`, `AgreementAcceptance`) — brief: payment method **not** required; `Components` (metered `api-call` is seeded but **out of hero scope**); coupons, calendar billing, custom price, offer, group, trial/expiration overrides, `Reference` (subscription-level — see idempotency note below). Catalog (not request fields) already encodes: no trial, no setup fee, expires never, taxable no. | `SubscriptionResponse` → `.Subscription` (`Subscription?`). Read: `Id`, `State`, `Product` (handle/name), `ProductPriceInCents`, `CurrentPeriodEndsAt`, `NextAssessmentAt`. | **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`. Accessors: `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` **[422]** · `TryGetRawError(out RawError)` fallback. `ErrorListResponse1.Errors (errors): IReadOnlyList<string> !req`. No-throw variant: absent. | none | `operations/Subscriptions.md` · `records-2-Cr-Ne.md` (`CreateSubscription`, `CreateSubscriptionRequest`, `ErrorListResponse1`) · `records-3-Of-Su.md` |

`ReadProductByHandle(string apiHandle, ct)` (`GET /products/handle/{api_handle}.json`, Case B, returns `ProductResponse`) exists if you want to pre-validate a POST plan handle; it is **not** required — an unknown handle fails **E** as 422. Do not call `ReadProductFamily(int id)` to resolve a handle.

`ListSubscriptions` can filter by numeric `product` + `state` but **not** by customer — do not use it for “this shopper’s subscriptions.”

### 2.3 Idempotent subscribe (customer + subscription)

There is **no** create-subscription idempotency key and **no** “get subscription by customer + product” operation. Duplicate prevention is **list-then-create** in the application:

1. `ReadCustomerByReference(shopperUserId)`.
2. **404 (Case B `RawError.StatusCode`)** → `CreateCustomer` with `Reference = shopperUserId` plus required `FirstName`/`LastName`/`Email`. On **422** (or `JsonException` during 422 bind): retry `ReadCustomerByReference`; if found, continue; if not, fail. Never create a second customer for the same reference (Notes: one customer per reference).
3. `ListCustomerSubscriptions(customer.Id)`.
4. **Already subscribed:** any row whose `Subscription.Product.Handle` equals the requested plan handle **and** `Subscription.State == MaxioAdvancedBilling.Models.Enums.SubscriptionState.Active` → return that subscription; **do not** call `CreateSubscription`. Compare **handles**, not product ids.
5. Otherwise `CreateSubscription` with `ProductHandle` + `CustomerId`. Do not send payment or components.

Two in-flight `POST /api/subscriptions` can both pass step 4 and both create — the SDK cannot serialize that. How the PublicApi serializes per-shopper subscribe is `YOUR CALL — not in the map`.

Do **not** use `CreateSubscription.Reference` as the primary idempotency key: `FindSubscription(reference)` looks up a **subscription** reference, and reusing a stable `{user}:{plan}` reference would collide if the shopper later resubscribes after cancel (422 / wrong row). Leave `Reference` unset unless you invent a unique-per-attempt value (`YOUR CALL — not in the map`).

If **B** 404s on `GET /api/my-subscriptions`, return an empty list rather than creating a customer (`YOUR CALL — not in the map`).

### 2.4 Enums used

| Enum | Namespace | Members needed | Source |
|---|---|---|---|
| `IntervalUnit` | `MaxioAdvancedBilling.Models.Enums` | `Day (day)`, `Month (month)` — `StringEnum<T>`, **not** a C# enum. Write `IntervalUnit.Month` or `IntervalUnit.FromValue("month")`. | `map/models/enums.md` |
| `SubscriptionState` | `MaxioAdvancedBilling.Models.Enums` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` | `map/models/enums.md` |
| `BasicDateField` | `MaxioAdvancedBilling.Models.Enums` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` — only if you pass `dateField` (this flow passes `null`) | `map/models/enums.md` |
| `ListProductsInclude` | `MaxioAdvancedBilling.Models.Enums` | `PrepaidProductPricePoint (prepaid_product_price_point)` — this flow passes `null` | `map/models/enums.md` |
| `ServerEnvironment` | `MaxioAdvancedBilling.Servers` | `Us (US)`, `Eu (EU)` | `sdk-map.md` · `Servers/ServerEnvironment.cs` |

“Active” for step 4 means `SubscriptionState.Active` (wire `active`). Whether `Trialing` / `PastDue` / `SoftFailure` also count as “already subscribed” is `YOUR CALL — not in the map` (brief said **active**).

### 2.5 Error cheat-sheet (every in-scope call)

| Operation | Thrown type | Not-found | Already-exists / 422 |
|---|---|---|---|
| ListProductsForProductFamily | `SdkException<ListProductsForProductFamilyError>` | `TryGetString(out string)` on 404 (unknown family handle/id) | n/a |
| ReadCustomerByReference | `SdkException<RawError>` | `Error.StatusCode == NotFound` | n/a |
| CreateCustomer | `SdkException<CreateCustomerError>` | n/a | 422 → `TryGetCustomerErrorResponse1`; **do not trust** `Errors.PerPage`/`PricePoint` for uniqueness text; re-lookup by reference (see C) |
| ListCustomerSubscriptions | `SdkException<RawError>` | `Error.StatusCode` (e.g. unknown customer id) | n/a |
| CreateSubscription | `SdkException<CreateSubscriptionError>` | No 404 accessor — unknown product/customer/payment-required all come through **422** (or the RawError fallback / `JsonException` if the body is not a string list). | 422 → `ex.Error.TryGetErrorListResponse1(out ErrorListResponse1 list)` then read `list.Errors` (`IReadOnlyList<string> !req`, wire `errors`). Join those strings for the caller. If the accessor is false: `TryGetRawError` + `ReadAsString()`. Provider does **not** reject a second subscription to the same product — duplicates are prevented only by step 4. Exact English strings for “product not found” / “payment required” / “customer missing” are **UNVERIFIED** (not in the map or generated model). |

`RawError` (`MaxioAdvancedBilling.Core.ErrorResponse.RawError`): `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. `ApiError.TryGetRawError` is the Case-A fallback, **not** a catch-all on Case B (Case B **is** `RawError`).

Every operation is **throw-only**.

### 2.6 CreateSubscription 422 follow-up (sandbox HTTP 422)

Request-model members in the live call are **correct** — do not rename them. Envelope and wire names:

| C# member | Wire | Required? | Source |
|---|---|---|---|
| `CreateSubscriptionRequest.Subscription` | `subscription` | C# `required` | `Models/CreateSubscriptionRequest.cs` · `records-2-Cr-Ne.md` |
| `CreateSubscription.ProductHandle` | `product_handle` | Notes: required **unless** `product_id` is sent. XML: “Required, unless a `product_id` is given instead.” | `Models/CreateSubscription.cs` · `operations/Subscriptions.md` |
| `CreateSubscription.CustomerId` | `customer_id` | Notes: required **unless** `customer_reference` or `customer_attributes`. XML: “The ID of an existing customer within Chargify.” | `Models/CreateSubscription.cs` |
| `Customer.Id` (from `CustomerResponse.Customer`) | `id` | `int?`. XML: “The customer ID in Chargify.” This **is** the int to pass as `CreateSubscription.CustomerId`. If `Id` is null, `[JsonIgnore(WhenWritingNull)]` **omits** `customer_id` and the provider will 422. Pass `customer.Id.Value` only after a null check. | `Models/Customer.cs` · `records-2-Cr-Ne.md` |
| `CreateSubscription.ProductPricePointHandle` | `product_price_point_handle` | **Not required.** Notes: “To set a **specific** product price point, use `product_price_point_handle` or `product_price_point_id`.” Omit it to use the product’s default price point. No-trial / no-setup / expires-never / taxable-no are catalog attributes, not extra create fields. | `operations/Subscriptions.md` · `Models/CreateSubscription.cs` |

Live 422 `ErrorListResponse1.Errors` (confirmed): `"No payment method was on file for the $299.00 balance"`. Payment is required for this catalog/site under the default (unset) collection method. See §2.7.

Extract 422 messages (do not parse `ex.ToString()`):

```
catch (SdkException<CreateSubscriptionError> ex)
{
    if (ex.Error.TryGetErrorListResponse1(out var list))
        // list.Errors : IReadOnlyList<string> !req  — join these
    else if (ex.Error.TryGetRawError(out var raw))
        // raw.ReadAsString()
}
```

`ErrorListResponse1` is `{ "errors": [ "<string>", ... ] }`. Live create-without-payment string: `"No payment method was on file for the $299.00 balance"`.

### 2.7 CreateSubscription payment — live 422 `"No payment method was on file for the $299.00 balance"`

**Product catalog flags** (`MaxioAdvancedBilling.Models.Product`, `records-3-Of-Su.md`, `Models/Product.cs`):

| C# | Wire | Type | XML / Notes |
|---|---|---|---|
| `RequireCreditCard` | `require_credit_card` | `bool?` | “Boolean that controls whether a payment profile is required to be entered for customers wishing to sign up on this product.” Closest catalog flag to this 422. |
| `RequestCreditCard` | `request_credit_card` | `bool?` | “Deprecated value that can be ignored unless you have legacy hosted pages. For Public Signup Page users, read this attribute from under the signup page.” Not a create-API payment-required flag. |
| `PriceInCents` | `price_in_cents` | `long?` | Product price in integer cents. Live $299.00 = `29900`. This is the **balance** named in the 422, not a boolean flag. |
| `InitialChargeInCents` | `initial_charge_in_cents` | `long?` | “The up front charge you have specified” (setup). Not the $299 plan price. |
| `RequireBillingAddress` | `require_billing_address` | `bool?` | “whether a billing address is required to add a payment profile, especially at signup.” Address, not “payment method on file.” |
| `RequestBillingAddress` | `request_billing_address` | `bool?` | Self-Service Pages only. |
| `RequireShippingAddress` | `require_shipping_address` | `bool?` | Shipping at signup. |

No other `Product` member is documented as emitting that exact 422 sentence. Create Notes (`operations/Subscriptions.md`): “Payment information may be required to create a subscription, depending on the options for the Product being subscribed.” Live default create (no payment fields) **does** require a payment method to collect `PriceInCents`.

**CreateSubscription fields that can avoid a stored card** (all optional, none C# `required`). None is documented as “skip the payment-method-on-file check for a due balance.” Whether they clear **this** 422 (except sending a card) is **UNVERIFIED**.

| C# | Wire | Type | Enum / Notes |
|---|---|---|---|
| `PaymentCollectionMethod` | `payment_collection_method` | `MaxioAdvancedBilling.Models.Enums.CollectionMethod?` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)`. XML: legacy Statements = `invoice`/`automatic`; Relationship Invoicing = `remittance`/`automatic`/`prepaid`. Unset behaved as card-required for the $299 balance. |
| `NextBillingAt` | `next_billing_at` | `DateTimeOffset?` | XML: future timestamp → “no trial or initial charges will be applied… no payment will be captured at all.” Import/defer-capture semantics, not a collection-method switch. |
| `InitialBillingAt` | `initial_billing_at` | `DateTimeOffset?` | Future → Awaiting Signup; payment due at that instant. |
| `DeferSignup` | `defer_signup` | `bool?` default `false` (serialized; no `WhenWritingNull`) | Awaiting Signup Date / unknown first billing. |
| `PaymentProfileId` | `payment_profile_id` | `int?` | Existing card/bank on the customer. Still a payment method. |
| `NetTerms` | `net_terms` | `string?` | Days-until-due on **invoice** billing (0–180). Does not by itself attach a card. |

There is **no** `SkipPayment` / `SkipCollection` member.

**If a payment method MUST be sent (this 422):** nest `MaxioAdvancedBilling.Models.PaymentProfileAttributes` on **either** interchangeable property (same type):

- `CreateSubscription.PaymentProfileAttributes` → wire `payment_profile_attributes` — XML: “alias to credit_card_attributes”
- `CreateSubscription.CreditCardAttributes` → wire `credit_card_attributes` — XML: “Credit Card data… Interchangeable with `payment_profile_attributes`.” **Property type is `PaymentProfileAttributes?`, not the 3-field `CreditCardAttributes` record.**

Do **not** construct `MaxioAdvancedBilling.Models.CreditCardAttributes` for this call (`FullNumber`/`ExpirationMonth`/`ExpirationYear` as `string?` only) — that type is not what `CreateSubscription.CreditCardAttributes` accepts.

`PaymentProfileAttributes` (`records-3-Of-Su.md`, `Models/PaymentProfileAttributes.cs`) — **no C# `required` members**. XML-tied for a new card (not import):

| C# | Wire | Type | XML requiredness |
|---|---|---|---|
| `FullNumber` | `full_number` | `string?` | “The full credit card number (string representation, i.e. 5424000000000015)” — that PAN is a **format illustration**, not labeled as a sandbox test card. |
| `ExpirationMonth` | `expiration_month` | `MaxioAdvancedBilling.Models.AnyOf.ExpirationMonth2?` | “Optional when performing a Subscription Import via vault_token, **required otherwise**.” Factory: `ExpirationMonth2.Int(int)` / `.String(string)` (implicit from `int`/`string`). |
| `ExpirationYear` | `expiration_year` | `MaxioAdvancedBilling.Models.AnyOf.ExpirationYear2?` | Same: required unless vault-token import. 4-digit year. Factory: `ExpirationYear2.Int(int)` / `.String(string)`. |
| `Cvv` | `cvv` | `string?` | “Optional, may be required by your gateway settings.” |
| `FirstName` / `LastName` | `first_name` / `last_name` | `string?` | Optional; omitted → customer’s names. |
| `ChargifyToken` | `chargify_token` | `string?` | Optional Maxio.js token; “must be passed as a sole attribute” if used. |
| `BillingAddress`, `BillingCity`, `BillingState`, `BillingCountry`, `BillingZip` | `billing_*` | `string?` | “Optional, may be required by your product configuration or gateway settings.” Country ISO-3166-1 alpha-2. Also gated by `Product.RequireBillingAddress`. |
| `PaymentType` | `payment_type` | `MaxioAdvancedBilling.Models.Enums.PaymentType?` | `CreditCard (credit_card)`, `BankAccount (bank_account)`, `PaypalAccount (paypal_account)`, `ApplePay (apple_pay)`. |
| `CardType` | `card_type` | `CardType?` | Optional, import/UI. Includes `Bogus (bogus)`. |

Leave out import-only: `VaultToken`, `CurrentVault`, `CustomerVaultToken`, `LastFour`, `Id`, `CustomerId`, `MaskedCardNumber`. Square: `PaymentMethodNonce`. Multi-gateway: `GatewayHandle`.

**Sandbox test PAN/CVV/expiry:** **not** in the map or CreateSubscription/PaymentProfiles Notes. Notes: “Do not use real card information for testing. See the Sites articles that cover testing your site setup.” `AllVaults.Bogus` / `CreditCardVault.Bogus` / `BankAccountVault.Bogus` XML: “Use `bogus` for testing” (vault name, not a PAN).

**Also present on `CreateSubscription`:**

| C# | Wire | Type | Required fields |
|---|---|---|---|
| `CreditCardAttributes` | `credit_card_attributes` | `PaymentProfileAttributes?` | none C# `required`; card XML as above |
| `PaymentProfileAttributes` | `payment_profile_attributes` | `PaymentProfileAttributes?` | same object; interchangeable with `CreditCardAttributes` — send **one**, not both |
| `BankAccountAttributes` | `bank_account_attributes` | `MaxioAdvancedBilling.Models.BankAccountAttributes?` | none C# `required`. XML when ACH: `BankName`, `BankRoutingNumber`, `BankAccountNumber` required; `BankIban`/`BankBranchCode` GoCardless. `BankAccountType` default checking; `BankAccountHolderType` default personal. `CurrentVault`: “Use `bogus` for testing.” |

Construct unions with factories (`dotnet-models`). PCI: Notes warn raw PAN in production requires PCI; Maxio.js token is the alternative (`ChargifyToken` as sole attribute).

---

## 3. Trap notes

⚠ Step 1 (client registration) — the `HttpClient` handed to `MaxioAdvancedBillingClient` must be a long-lived pipeline (factory), not built per request; the DI helper’s lifetime vs the wrapper is not obvious from the constructor. **MUST load `dotnet-client-initialization`** before registering the client.

⚠ Step 1 (auth) — credentials go on `BasicAuth` before the client is used; the password is the literal `"x"`, not the subdomain. Load the key from `Maxio:ApiKey`, never a hardcoded string. **MUST load `dotnet-authentication`** before wiring credentials.

⚠ Step 1 (BaseUrl / retries / timeouts) — `Retry` / `Timeout` on `MaxioAdvancedBillingClientOptions` do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; transport retries can re-send writes. **MUST load `dotnet-configuration-resilience`** before wiring `Retry`, `Timeout`, or `Server.Production.Us.BaseUrl`.

⚠ Steps 2–5 (calls) — list/create methods have many nullable parameters **without C# defaults**; a positional call mis-binds (e.g. a `CancellationToken` into `dateField`). Use **named arguments**, including `ct:`. **MUST load `dotnet-calling-endpoints`** before the first `client.{Group}.{Op}` call.

⚠ Steps 2–5 (models) — envelopes wrap one level (`ProductResponse.Product`, `CustomerResponse.Customer`, `SubscriptionResponse.Subscription` **nullable**); enums are `StringEnum<T>` (`SubscriptionState.Active`, not `active`); `CreateCustomer` has three `required` members the compiler will demand; extra JSON is dropped on deserialize. **MUST load `dotnet-models`** before constructing requests or mapping responses.

⚠ Step 6 (error boundary) — mixed Case A / Case B: list/lookup/list-subscriptions are often Case B (`SdkException<RawError>`), while ListProductsForProductFamily / CreateCustomer / CreateSubscription are Case A with **different** `TryGet…` names. `TryGetRawError` is not a Case-B catch-all. CreateCustomer 422’s generated payload (`Errors` = `per_page`/`price_point` only) is a suspicious shared model — uniqueness must be recovered by re-lookup, not by parsing that payload. **MUST load `dotnet-error-handling`** before any `try/catch`.

⚠ Step 6 — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 6 — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. This is especially likely on **CreateCustomer 422** given the `CustomerErrorResponse1` / `Errors` shape mismatch. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 7 (tests) — the constructor `HttpClient` argument is the test seam; do not stub SDK internals. **MUST load `dotnet-testing`** before writing integration-layer tests.

---

## 4. REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — constructing / DI-registering `MaxioAdvancedBillingClient` and `HttpClient` lifetime |
| `dotnet-authentication` | Step 1 — `BasicAuthCredentials` (API key + `"x"`) from `Maxio:ApiKey` |
| `dotnet-configuration-resilience` | Step 1 — `Retry` / timeouts / `Server.Production.Us.BaseUrl` vs `Site` |
| `dotnet-calling-endpoints` | Steps 2–5 — named arguments, `ct:`, throw-only operations |
| `dotnet-models` | Steps 2–5 — envelopes, `required` members, `StringEnum<T>`, cents vs dollars |
| `dotnet-error-handling` | Step 6 — Case A vs B, `TryGet…`, both `JsonException` directions |
| `dotnet-testing` | Step 7 — `HttpClient` test seam |

---

## 5. Assumptions & Blockers

**Assumptions**

- Customer `reference` is the eShopOnWeb user id (brief). How that id is read off the JWT-authenticated request is `YOUR CALL — not in the map`.
- `CreateCustomer` `FirstName` / `LastName` / `Email` come from the authenticated shopper record. Exact source fields are `YOUR CALL — not in the map`.
- Brief said payment is not required. Live CreateSubscription 422 with `ProductHandle`+`CustomerId` and no payment fields: `ErrorListResponse1.Errors` = `"No payment method was on file for the $299.00 balance"`. Default (unset) `PaymentCollectionMethod` collects `Product.PriceInCents` at signup and needs a payment method. Send `PaymentProfileAttributes` / `CreditCardAttributes` (type `PaymentProfileAttributes`), or try `CollectionMethod.Remittance`/`Invoice`/`Prepaid` / future `NextBillingAt` — those alternatives are **UNVERIFIED** against this exact 422.
- `Components` omitted (`api-call` out of scope).
- Product identity is handle-only (`eshop-pro`, `basic-plan`); numeric IDs in the sandbox table are not used.
- Family listing uses `productFamilyId: "handle:" + Maxio:ProductFamilyHandle`.
- “Already subscribed” means same `Product.Handle` and `State == SubscriptionState.Active`.
- SDK `Environment` for this sandbox target is `ServerEnvironment.Us`; sandbox vs live is the site (`Maxio:Subdomain` / `Maxio:BaseUrl`), not an enum.
- PublicApi DTO shapes, status codes returned to the shopper, and per-user concurrency control are `YOUR CALL — not in the map`.

**Blockers**

*(none for missing operations — payment-profile nested models exist on `CreateSubscription`. The brief’s “payment method not required” is contradicted by the live 422 above; implement via §2.7.)*
