# Maxio Advanced Billing — eShopOnWeb recurring subscribe

Package `AsadAli.AdvancedBilling.Sdk` · root namespace `MaxioAdvancedBilling` · map stamp `v1.0.2` / `15db14b`. Additive parallel capability beside Catalog → Basket → Order. System of record is Maxio; numeric catalog IDs are never hard-coded.

## Scope & sequence

| Step | What | SDK operations |
|---|---|---|
| 1 | Bind config (`Maxio:ApiKey` ← `MAXIO_API_KEY`, `Maxio:Subdomain` ← `MAXIO_SITE_SUBDOMAIN`, `Maxio:ProductFamilyHandle` ← `MAXIO_DEFAULT_PRODUCT_FAMILY`, optional `Maxio:BaseUrl`, `MAXIO_ENVIRONMENT`). Construct and DI-register the client. | constructor / `AddMaxioAdvancedBillingClient` |
| 2 | `GET /api/subscription-plans` (JWT): list plans in the configured product family by **handle**. Return handle, name, price, interval. | `ProductFamilies.ListProductsForProductFamily` |
| 3 | Find-or-create Maxio customer for the logged-in eShop user (reference = user id). Double-click must not create two customers. | `Customers.ReadCustomerByReference` then `Customers.CreateCustomer` |
| 4 | Idempotent enroll: detect an existing **live** subscription for this customer + product handle **before** create; on race, re-read rather than insert a second. | `Customers.ListCustomerSubscriptions` (+ optional `Subscriptions.FindSubscription`) then `Subscriptions.CreateSubscription` |
| 5 | `POST /api/subscriptions` (JWT): enroll onto a product **handle** (default `eshop-pro` when the body omits a handle). No payment profile / card / 3-DS. Confirm plan/price/state/next-billing-date. | steps 3–4 |
| 6 | `GET /api/my-subscriptions` (JWT): list this user’s subscriptions with plan/price/state/next-billing-date. | step 3 + `Customers.ListCustomerSubscriptions` |
| 7 | Integration error boundary around every SDK call. | see Error sheet |
| 8 | Tests for the integration layer (no live Maxio required for unit tests). | — |

Out of scope (do not call): usage reporting / metered `api-call` component, cart/checkout changes, webhooks, payment profiles.

Sandbox handles (config values, not code literals for numeric IDs): family `eshop-subscribe`, plans `eshop-pro` ($299.00/mo, default subscribe target) and `basic-plan` ($29.00/mo), seeded metered component `api-call` (not called).

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

### 1. Client construction & auth

| Fact | Value | Cite |
|---|---|---|
| NuGet package id | `AsadAli.AdvancedBilling.Sdk` | `sdk-map.md` |
| Root / `using` namespace | `MaxioAdvancedBilling` | `sdk-map.md` |
| Client type | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` | `sdk-map.md`, `MaxioAdvancedBillingClient.cs` |
| **Only** constructor | `MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` — both required, no other overload | `sdk-map.md` |
| Options type | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` | `sdk-map.md` |
| Options members | `Environment`: `MaxioAdvancedBilling.Servers.ServerEnvironment` (default `ServerEnvironment.Default()` → `Us`); `Retry`: `MaxioAdvancedBilling.Core.Configuration.RetryOptions` (default `RetryOptions.Default()`); `Server`: `MaxioAdvancedBilling.ServerOptions`; `BasicAuth`: `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md`, `MaxioAdvancedBillingClientOptions.cs` |
| DI method | `MaxioAdvancedBilling.ServiceCollectionExtensions.AddMaxioAdvancedBillingClient(this IServiceCollection, Action<MaxioAdvancedBillingClientOptions>? configure = null)` | `sdk-map.md`, `ServiceCollectionExtensions.cs` |
| Controller accessors | `client.Customers`, `client.ProductFamilies`, `client.Products`, `client.Subscriptions` | `sdk-map.md` |
| Auth scheme | HTTP Basic only. `options.BasicAuth = new BasicAuthCredentials { Username = <api key>, Password = "x" }` | `sdk-map.md` |
| `BasicAuthCredentials` | namespace `MaxioAdvancedBilling.Core.Authentication.Basic`; `Username`: `string` **required**; `Password`: `string` **required**; `Encode()` exists but is not needed at the call site | `BasicAuthCredentials.cs` |
| Config → credentials | `Username` ← `Maxio:ApiKey` / `MAXIO_API_KEY`. `Password` is the literal `"x"` (not a secret). Never hard-code the key. | this sheet + `sdk-map.md` |

**Environments** (`MaxioAdvancedBilling.Servers.ServerEnvironment`, `StringEnum`, members `Us ("US")`, `Eu ("EU")` — **no Sandbox member**):

| Member | Wire | Hosting | Cite |
|---|---|---|---|
| `ServerEnvironment.Us` | `US` | default; US-hosted (`https://{site}.chargify.com`) | `sdk-map.md`, `Servers/ServerEnvironment.cs` |
| `ServerEnvironment.Eu` | `EU` | EU-hosted (`https://{site}.ebilling.maxio.com`) | same |

`MAXIO_ENVIRONMENT` selects **Us vs Eu hosting**, not sandbox vs live. The sandbox site is `Maxio:Subdomain` / `MAXIO_SITE_SUBDOMAIN` (currently `cp-exp-1`; must remain a config value so the same build hits a different site).

**Server override types** (root `ServerOptions` vs `Servers.ProductionOptions` — different namespaces):

| Type | Namespace | Members | Cite |
|---|---|---|---|
| `ServerOptions` | `MaxioAdvancedBilling` | `Production`: `ProductionOptions`; `Ebb`: `EbbOptions` | `ServerOptions.cs` |
| `ProductionOptions` | `MaxioAdvancedBilling.Servers` | `Us`: `ProductionOptions.UsOptions`; `Eu`: `ProductionOptions.EuOptions` | `Servers/ProductionOptions.cs` |
| `ProductionOptions.UsOptions` | nested in `ProductionOptions` | `BaseUrl`: `string` default `"https://{site}.chargify.com"`; `Site`: `string` default `"subdomain"` | `Servers/ProductionOptions.cs` |
| `ProductionOptions.EuOptions` | nested in `ProductionOptions` | `BaseUrl`: `string` default `"https://{site}.ebilling.maxio.com"`; `Site`: `string` default `"subdomain"` | `Servers/ProductionOptions.cs` |

**How to wire this app’s config:**

1. Always set `options.Server.Production.Us.Site` and `.Eu.Site` from `Maxio:Subdomain` (the `{site}` template param). Do **not** hard-code `cp-exp-1`.
2. Set `options.Environment` from `MAXIO_ENVIRONMENT`: `"US"` → `ServerEnvironment.Us`, `"EU"` → `ServerEnvironment.Eu` (case-insensitive). Any other value (including `"sandbox"`) → `ServerEnvironment.Us` (sandbox sites are US-hosted).
3. When `Maxio:BaseUrl` is **set**, assign it **verbatim** to the Production node that `Environment` will resolve: `options.Server.Production.Us.BaseUrl` if Us, `.Eu.BaseUrl` if Eu. Resolution is `new UrlTemplate(Us.BaseUrl, path, [TemplateParam.ForServer("site", Us.Site)])` (and Eu equivalent) — `{site}` in the template is replaced by `Site`; a verbatim host with no `{site}` token uses `BaseUrl` as-is and ignores `Site`.
4. When `Maxio:BaseUrl` is **unset**, leave the default template and let `Site` produce `https://{subdomain}.chargify.com` (Us) or `https://{subdomain}.ebilling.maxio.com` (Eu).
5. Do not touch `options.Server.Ebb` (event-ingest only).

`RetryOptions` members are all `required` if you construct one by hand; prefer leaving `options.Retry` at `RetryOptions.Default()` unless the app explicitly tunes it. Namespace `MaxioAdvancedBilling.Core.Configuration`. Cite: `sdk-map.md`.

---

### 2. List subscription plans by family **handle**

**Operation:** `client.ProductFamilies.ListProductsForProductFamily` — `GET /product_families/{product_family_id}/products.json` · `map/operations/ProductFamilies.md` · `Api/ProductFamilies.cs`

| | |
|---|---|
| Signature | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| Must-pass-explicitly | 8 params `dateField` … `include` (nullable, no C# default) — pass `null` to skip. Call with **named arguments**. |
| `productFamilyId` | `string`. Source param docs: **either the family’s numeric id or its handle prefixed with `handle:`**. Pass `"handle:" + configuredFamilyHandle` (e.g. `"handle:eshop-subscribe"`). Never a hard-coded numeric id. (`ReadProductFamily` also mentions `handle:my-family` in remarks but its C# param is `int id` — **do not use it** for handle lookup.) |
| Returns | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>` |
| Envelope | `ProductResponse.Product` (`Product` **required**) — `records-3-Of-Su.md` |
| Error | **Case A** `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>` |
| Accessors | `TryGetString(out string)` **[404]** · `TryGetRawError(out RawError)` fallback (401 and every other non-2xx) |
| Pagination | manual `page` + `perPage` (default 20; source max 200). Loop until a page is empty or `Count < perPage`. |

**Do not use for this GET:** `Products.ListProducts` (site-wide; `ListProductsFilter.Ids` is `IReadOnlyList<int>?` — numeric, no family-handle filter) · `Products.ReadProductByHandle` (single product) · `ProductFamilies.ListProductFamilies` (families only; unused if `handle:` prefix is used).

**Fields to map onto the HTTP response** (from `ProductResponse.Product`):

| C# | Wire | Type | Use |
|---|---|---|---|
| `Handle` | `handle` | `string?` | plan handle (`eshop-pro`, `basic-plan`) |
| `Name` | `name` | `string?` | display name |
| `PriceInCents` | `price_in_cents` | `long?` | price (cents; 29900 → $299.00) |
| `Interval` | `interval` | `int?` | interval count (1) |
| `IntervalUnit` | `interval_unit` | `IntervalUnit?` | `Month` / `Day` |
| `ProductFamily` | `product_family` | `ProductFamily?` | optional sanity-check `ProductFamily.Handle` |
| `RequireCreditCard` | `require_credit_card` | `bool?` | catalog is “payment not required”; do not send cards regardless |
| `ProductPricePointHandle` | `product_price_point_handle` | `string?` | not required on subscribe (default price point) |

`ListProductsFilter` (`records-2-Cr-Ne.md`): `Ids`, `PrepaidProductPricePoint`, `UseSiteExchangeRate` — **no handle filter**; pass `filter: null`.

---

### 3. Find-or-create customer (idempotent)

Customer **reference** = eShopOnWeb user id (stable string). That is the shared key.

#### 3a. Lookup — `client.Customers.ReadCustomerByReference`

`GET /customers/lookup.json` · `map/operations/Customers.md`

| | |
|---|---|
| Signature | `ReadCustomerByReference(string reference, CancellationToken ct = default)` |
| Query | `reference` ← `reference` |
| Returns | `MaxioAdvancedBilling.Models.CustomerResponse` |
| Envelope | `CustomerResponse.Customer` (`Customer` **required**) — `records-2-Cr-Ne.md` |
| Error | **Case B** `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` |
| Accessors | `StatusCode: HttpStatusCode` · `ReadAsString()` · `ReadAsJson<T>()` · `ReadAsBytes()` |
| Miss | Treat `StatusCode == HttpStatusCode.NotFound` as “no customer yet” and proceed to 3b. **UNVERIFIED** that a miss is always 404 (Case B rows do not list statuses); any other status is a real failure. |

#### 3b. Create — `client.Customers.CreateCustomer`

`POST /customers.json` · `map/operations/Customers.md`

| | |
|---|---|
| Signature | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly** |
| Returns | `CustomerResponse` (unwrap `.Customer`) |
| Error | **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>` |
| Accessors | `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` **[422]** · `TryGetRawError(out RawError)` fallback (401 and others) |
| Duplicate signal | Notes: “you may only create one customer for a given reference value.” Duplicate reference → **422**. On 422 (or a 422 that surfaces as `JsonException` — see Errors model below): call `ReadCustomerByReference` again and use that customer. Do not retry `CreateCustomer` in a loop. |

**Request envelope** `CreateCustomerRequest` (`records-1-Ac-Cr.md`):

| C# | Wire | Type | Required? |
|---|---|---|---|
| `Customer` | `customer` | `CreateCustomer` | **required** (`!req`) |

**Inner `CreateCustomer`:**

| C# | Wire | Type | Required? |
|---|---|---|---|
| `FirstName` | `first_name` | `string` | **required** |
| `LastName` | `last_name` | `string` | **required** |
| `Email` | `email` | `string` | **required** |
| `Reference` | `reference` | `string?` | optional in the model; **must set** to the eShop user id for idempotency |
| `CcEmails`, `Organization`, `Address`, `Address2`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `VatNumber`, `TaxExempt`, `TaxExemptReason`, `ParentId`, `SalesforceId` | matching snake_case | optional | omit |

**Response `Customer` fields used later:** `Id (id): int?` (needed by `ListCustomerSubscriptions`), `Reference (reference): string?`, `Email (email): string?`. `Customer.Id` is nullable in the model; after a successful read/create it is the Maxio customer id — if it is null, fail the request (cannot list or enroll).

**422 payload shape (trust):** `CreateCustomerError` deserializes 422 as `CustomerErrorResponse1` whose `Errors (errors): Errors?` is the record `Errors` with **only** `PerPage (per_page)` and `PricePoint (price_point)` (`records-2-Cr-Ne.md`, `Models/Errors.cs`). That record is also used by event-based-billing segment errors — **suspicious shared model**. The union `Errors1` (`CustomerError` \| `IReadOnlyList<string>`, `unions.md`) is **not** what this accessor returns. **UNVERIFIED** that the live 422 body matches `Errors`. Extract best-effort from `TryGetCustomerErrorResponse1`; if that is empty/unhelpful, `TryGetRawError` + `ReadAsString()`; if deserialization throws `JsonException` instead of `SdkException`, fall back to the generic message and **re-lookup by reference**.

`ListCustomers` (`q` search) is **not** the exact-match path — notes tell you to use the lookup endpoint. Do not use it for find-or-create.

---

### 4. Create / enroll subscription (product **handle**, no payment)

**Operation:** `client.Subscriptions.CreateSubscription` — `POST /subscriptions.json` · `map/operations/Subscriptions.md`

| | |
|---|---|
| Signature | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly |
| Returns | `MaxioAdvancedBilling.Models.SubscriptionResponse` |
| Envelope | `SubscriptionResponse.Subscription` (`Subscription?` — **nullable**, null-check) — `records-4-Su-We.md` / `records-3-Of-Su.md` |
| Error | **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>` |
| Accessors | `TryGetErrorListResponse1(out ErrorListResponse1)` **[422]** · `TryGetRawError(out RawError)` fallback (401 and others) |
| 422 payload | `ErrorListResponse1.Errors (errors): IReadOnlyList<string> !req` — `records-2-Cr-Ne.md`. Messages are opaque strings (product not found, validation, payment required, duplicate reference, …). There is **no** typed “already subscribed” / “product not found” accessor. |

**Request envelope** `CreateSubscriptionRequest` (`records-2-Cr-Ne.md`): `Subscription (subscription): CreateSubscription !req`.

**Inner `CreateSubscription` — set these; omit payment:**

| C# | Wire | Type | Required? | This integration |
|---|---|---|---|---|
| `ProductHandle` | `product_handle` | `string?` | optional in model; **set** | request handle, or default `eshop-pro` from config. Notes: identify product with `product_id` **or** `product_handle`. Do **not** set `ProductId`. |
| `CustomerId` | `customer_id` | `int?` | optional | set to `Customer.Id` from step 3 |
| `CustomerReference` | `customer_reference` | `string?` | optional | alternative to `CustomerId` (notes: existing customer via `customer_id` **or** `customer_reference`). Prefer `CustomerId` once `Id` is known; setting both to the same person is fine. |
| `Reference` | `reference` | `string?` | optional | **set** to a deterministic `{customerReference}:{productHandle}` so `FindSubscription` can re-find after a race |
| `PaymentCollectionMethod` | `payment_collection_method` | `CollectionMethod?` | optional in model; **set** | **`CollectionMethod.Remittance`** — see no-card enrollment below. Omitting this field produced live 422 `ErrorListResponse1.Errors`: `"No payment method was on file for the $299.00 balance"` (automatic collection of the signup balance with no profile). |
| `ProductPricePointHandle` / `ProductPricePointId` | | | optional | omit (default price point) |
| `ReceivesInvoiceEmails` | `receives_invoice_emails` | `string?` | optional | **omit** — email flag only, not a collection-method lever (`Models/CreateSubscription.cs`) |
| `NetTerms` | `net_terms` | `string?` | optional | **omit** unless you need a due-days override. Docs: “number of days after renewal (**on invoice billing**) that a subscription is due” (0–180). Companion to invoice/remittance billing, not a substitute for `PaymentCollectionMethod`. |
| `PrepaidConfiguration` | `prepaid_configuration` | `UpsertPrepaidConfiguration?` | optional | **omit** — prepaid architecture, not “no payment profile” |
| `NextBillingAt` / `InitialBillingAt` / `DeferSignup` | | | optional | **omit** — these delay/defer first capture (import / Awaiting Signup); they are not the no-card collection method |
| `PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes`, `CustomerAttributes`, `Components`, `AgreementAcceptance` | | | optional | **omit** — no card capture / 3-DS / inline customer create |

All other `CreateSubscription` members: omit.

**No-card enrollment (live 422 fix).** `CreateSubscription.PaymentCollectionMethod` is the map’s lever for collecting without a payment profile. Cite: `map/models/enums.md`, `Models/Enums/CollectionMethod.cs`, `Models/CreateSubscription.cs` (`payment_collection_method`).

| Architecture (enum XML) | Valid members | Use for no payment profile | Do not use |
|---|---|---|---|
| Current Relationship Invoicing | `remittance`, `automatic`, `prepaid` | **`Remittance`** | `Automatic` (charges a profile; live 422 above). `Prepaid` (prepaid balance, not invoice/remittance). |
| Legacy Statements | `invoice`, `automatic` | **`Invoice`** | `Automatic`. `Remittance` / `Prepaid` are not in the legacy valid set. |

Exact C# assignment on the `CreateSubscription` object already built (`ProductHandle`, `CustomerId`, `CustomerReference`, `Reference`):

```
PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance
```

(`StringEnum` static member; namespace `MaxioAdvancedBilling.Models.Enums`; wire `"remittance"`. Do **not** write `CollectionMethod.invoice` / `CollectionMethod.remittance` — those identifiers do not exist.)

This sandbox is treated as Relationship Invoicing, so **`Remittance` is the member to send**. If that 422s with an invalid-collection-method string, send `CollectionMethod.Invoice` (`wire "invoice"`) instead (legacy Statements valid set). If **either** still returns `"No payment method was on file for …"` (or a require-credit-card string), that is a **catalog/site BLOCKER** — the map has no further CreateSubscription field that enrolls without a profile; do not add card / 3-DS / payment-profile fields.

**3-DS note (operation remarks):** a card that needs 3-DS returns **422** with an `action_link`. This flow sends no card, so that path must not trigger. If a 422 string mentions 3-DS / `action_link`, treat as a catalog/config failure, not something to implement.

**Response fields to confirm back to the user** (from `SubscriptionResponse.Subscription`, null-checked):

| C# | Wire | Type | Maps to |
|---|---|---|---|
| `Product.Handle` | nested `product.handle` | `string?` | plan handle |
| `Product.Name` | nested `product.name` | `string?` | plan name |
| `ProductPriceInCents` | `product_price_in_cents` | `long?` | price (prefer this; fallback `Product.PriceInCents`) |
| `State` | `state` | `SubscriptionState?` | state |
| `NextAssessmentAt` | `next_assessment_at` | `DateTimeOffset?` | **next billing date** |
| `CurrentPeriodEndsAt` | `current_period_ends_at` | `DateTimeOffset?` | related; do not substitute for next billing unless `NextAssessmentAt` is null (**UNVERIFIED** they always agree) |
| `Id` | `id` | `int?` | Maxio subscription id (internal) |
| `Reference` | `reference` | `string?` | echo of the idempotency key |
| `PaymentCollectionMethod` | `payment_collection_method` | `CollectionMethod?` | informational |

Nested `Subscription.Product` is `Product?` — same `Product` record as listing.

`Products.ReadProductByHandle(string apiHandle, ct)` (`GET /products/handle/{api_handle}.json`, Case B `SdkException<RawError>`) may be used to **validate** a handle before create; it is not required if `CreateSubscription` 422 strings are surfaced. Path already contains `/handle/`, so pass the raw handle (`eshop-pro`), not `handle:eshop-pro`.

---

### 5. List subscriptions for the current user

**There is no list-by-customer-reference operation.** Resolve the customer (step 3) then:

**Operation:** `client.Customers.ListCustomerSubscriptions` — `GET /customers/{customer_id}/subscriptions.json` · `map/operations/Customers.md`

| | |
|---|---|
| Signature | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` |
| Returns | `IReadOnlyList<SubscriptionResponse>` |
| Envelope | each item `.Subscription` (`Subscription?`) — same fields as §4 |
| Error | **Case B** `SdkException<RawError>` — `StatusCode` / `ReadAsString()` / `ReadAsJson<T>()` / `ReadAsBytes()` |
| Pagination | **none** (returns all subscriptions for that customer) |
| Filters | **none** — filter in-process by `Product.Handle` / `State` |

**Do not use** `Subscriptions.ListSubscriptions` for this GET: no `customer_id` / `customer_reference` parameter; `product` filter is `int?` (numeric id — forbidden to hard-code). Site-wide list is the wrong scope.

Map each non-null `Subscription` to handle/name (`Product.Handle` / `Product.Name`), price (`ProductPriceInCents`), state (`State`), next billing (`NextAssessmentAt`). Return every subscription for the customer (including EOL); the client uses `state` to distinguish current vs canceled.

---

### 6. Idempotent subscribe (double-click never creates two)

The map does **not** expose a unique customer+product constraint on `CreateSubscription`. Idempotency is **application-side**, using operations that exist:

**Before create**

1. `ListCustomerSubscriptions(customerId)` (no server-side state/product-handle filter).
2. If any item has `Subscription.Product.Handle == requestedHandle` **and** `Subscription.State` is in the **already-subscribed** set below → return that subscription; do **not** call `CreateSubscription`.
3. Optional second key: `Subscriptions.FindSubscription(string? reference, ct)` — `GET /subscriptions/lookup.json` · `map/operations/Subscriptions.md`. `reference` is nullable with no default → **must pass explicitly**. Returns `SubscriptionResponse`. **Case A** `SdkException<FindSubscriptionError>`: `TryGetNoContent(out RawError)` **[404]** · `TryGetRawError` fallback. Use the deterministic `{customerReference}:{productHandle}` from §4. 404 → proceed to create.

**Already-subscribed states** (count as “already enrolled” — do not create another). Grouping from `Models/Enums/SubscriptionState.cs` XML (map row was truncated):

| Group | Members (C# = wire) |
|---|---|
| Live | `Active (active)`, `Assessing (assessing)`, `Pending (pending)`, `Trialing (trialing)`, `Paused (paused)` |
| Problem | `PastDue (past_due)`, `SoftFailure (soft_failure)`, `Unpaid (unpaid)` |
| Unclassified in XML; treat as enrolled | `AwaitingSignup (awaiting_signup)` |

**May subscribe again** (End of Life — a new subscription is a new enrollment):

`Canceled (canceled)`, `Expired (expired)`, `FailedToCreate (failed_to_create)`, `OnHold (on_hold)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`.

`SubscriptionState` is `StringEnum<SubscriptionState>` in `MaxioAdvancedBilling.Models.Enums` — compare with `== SubscriptionState.Active` (etc.), not C# enums. Compare via `.Equals` / `==` on the record, or `State == SubscriptionState.FromValue("active")`.

**`SubscriptionStateFilter`** (`Active`, `Canceled`, `Expired`, `ExpiredCards`, `OnHold`, `PastDue`, `PendingCancellation`, `PendingRenewal`, `Suspended`, `TrialEnded`, `Trialing`, `Unpaid`) applies only to `ListSubscriptions`, which this flow does not use for per-user listing. `ListCustomerSubscriptions` has no `state` param.

**After a racing create (422 or transport retry)**

- `TryGetErrorListResponse1` → read `Errors` strings (best-effort; **UNVERIFIED** that Maxio emits a stable “already exists” sentence).
- Re-run `ListCustomerSubscriptions` (and `FindSubscription` if a reference was sent). If a matching already-subscribed row exists, return it as success.
- If none exists, surface the 422 strings as a validation failure (product missing, payment required, etc.).

`CreateSubscription.Components` is **not** required for subscribe (metered `api-call` is seeded on the catalog; usage reporting is out of scope).

---

### 7. Errors — what to catch

`SdkException<TError>` lives in `MaxioAdvancedBilling.Core.Exceptions` and exposes **only** `TError Error { get; init; }` (`Core/Exceptions/SdkException.cs`). There is **no** status-code property on the exception. Status comes from the typed `TryGet…` (status implied by which accessor hits) or from `RawError.StatusCode`. Base of Case A types: `MaxioAdvancedBilling.Core.ErrorResponse.ApiError` with `TryGetRawError(out RawError)`. No `{Op}Result` / no-throw variants exist on this SDK.

| Situation | Operation | Catch | How to read status / body |
|---|---|---|---|
| Customer not found (lookup) | `ReadCustomerByReference` | `SdkException<RawError>` | `ex.Error.StatusCode == NotFound`; body `ReadAsString()` |
| Duplicate / invalid customer | `CreateCustomer` | `SdkException<CreateCustomerError>` | 422: `TryGetCustomerErrorResponse1`; else `TryGetRawError` → `StatusCode` + `ReadAsString()`. Then re-lookup. |
| Product family not found | `ListProductsForProductFamily` | `SdkException<ListProductsForProductFamilyError>` | 404: `TryGetString(out string)` (plain-string body); else `TryGetRawError` |
| Product not found (read-by-handle, optional) | `ReadProductByHandle` | `SdkException<RawError>` | `StatusCode` (expect 404) + `ReadAsString()` |
| Product not found / validation / “already exists” / payment required | `CreateSubscription` | `SdkException<CreateSubscriptionError>` | 422: `TryGetErrorListResponse1` → `Errors` (`IReadOnlyList<string>`); 401 and others: `TryGetRawError` |
| Subscription lookup miss | `FindSubscription` | `SdkException<FindSubscriptionError>` | 404: `TryGetNoContent(out RawError)`; else `TryGetRawError` |
| List my subscriptions / list customer subs | `ListCustomerSubscriptions` | `SdkException<RawError>` | `StatusCode` + `ReadAsString()` (404 if customer id gone) |
| 401 / 403 (bad API key, wrong site) | any | Case A: `TryGetRawError` (not the 422 accessor). Case B: `RawError.StatusCode` | Check `BasicAuth` username=API key, password=`"x"`, `Site` / `BaseUrl` / `Environment` before changing call sites |
| 404 vs 422 vs 401 (summary) | — | 404 = missing resource (family/customer/subscription lookup). 422 = validation (typed payload). 401 = auth, always fallback `RawError` | none of the in-scope Case A types have a 401 accessor |

`RawError` (`MaxioAdvancedBilling.Core.ErrorResponse`): `StatusCode: HttpStatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()`.

---

### Enums actually needed

Namespace `MaxioAdvancedBilling.Models.Enums`. `StringEnum<T>` — members below are the **literal C# identifiers**; parenthesized value is the wire string. Construct with the static member or `Type.FromValue("wire")`. Cite: `map/models/enums.md`.

**`IntervalUnit`** — `Day (day)`, `Month (month)`.

**`CollectionMethod`** (`MaxioAdvancedBilling.Models.Enums`, `StringEnum`) — `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)`. Enum XML: legacy Statements valid = `invoice` + `automatic`; current Relationship Invoicing valid = `remittance` + `automatic` + `prepaid`. **On create set `PaymentCollectionMethod = CollectionMethod.Remittance`** (RI no-profile enrollment). Do not omit (omit → live 422 needing a payment method). Do not send `Automatic` or `Prepaid`. `Invoice` only as fallback if `Remittance` is rejected as invalid for the site. Do not send payment profiles. Cite: `map/models/enums.md`, `Models/Enums/CollectionMethod.cs`.

**`SubscriptionState`** — `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)`.

**`ListProductsInclude`** — `PrepaidProductPricePoint (prepaid_product_price_point)`. Pass `null`.

**`BasicDateField`** — `UpdatedAt (updated_at)`, `CreatedAt (created_at)`. Pass `null` on list-plans.

**`ServerEnvironment`** — see §1 (`MaxioAdvancedBilling.Servers`, not `.Models.Enums`).

---

### Trap notes

⚠ Step 1 (client registration) — the `HttpClient` argument is required on the only constructor, and `AddMaxioAdvancedBillingClient` also constructs one internally; lifetime/ownership of that client versus the SDK wrapper is not visible from the signature. **MUST load `dotnet-client-initialization`** before writing the factory or DI registration.

⚠ Step 1 (auth) — `BasicAuthCredentials.Username` / `Password` are `required`; putting the API key in source or swapping username/password produces 401s that look like “wrong site”. **MUST load `dotnet-authentication`** before wiring credentials.

⚠ Step 1 (BaseUrl / site / retries) — `Timeout` / `Retry` on `MaxioAdvancedBillingClientOptions` are not the timeout on the `HttpClient` you pass in, and they do not bound an entire logical call; `HttpMethodsToRetry` does not tell you whether a failed **write** can run more than once (double-subscribe). Pagination for `ListProductsForProductFamily` is caller-driven (`page`/`perPage`, empty page = done). **MUST load `dotnet-configuration-resilience`** before registering the client, setting `BaseUrl`, or looping pages.

⚠ Steps 2–6 (every call) — list/search methods have many must-pass-explicitly nullables with **no** C# default; a positional call binds the cancellation token (or skips a required null) silently. The token parameter is `ct`. **MUST load `dotnet-calling-endpoints`** before the first `client.*` call.

⚠ Steps 2–6 (models) — envelopes wrap one property (`ProductResponse.Product`, `CustomerResponse.Customer`, `SubscriptionResponse.Subscription` which is **nullable**); enums are `StringEnum<T>` not C# enums; `CreateCustomer` / `CreateSubscriptionRequest.Subscription` use `required` + `init`. **MUST load `dotnet-models`** before constructing payloads or mapping responses.

⚠ Step 7 (error boundary) — in-scope reads are a mix of Case A (`CreateCustomer`, `CreateSubscription`, `ListProductsForProductFamily`, `FindSubscription`) and Case B (`ReadCustomerByReference`, `ListCustomerSubscriptions`, `ReadProductByHandle`); there are no Result-style overloads, and `TryGetRawError` is not a substitute for the status-specific `TryGet…` on Case A. **MUST load `dotnet-error-handling`** before any `try/catch`.

⚠ Step 7 (error boundary) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 7 (error boundary) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. This is especially live on `CreateCustomer` 422 because `CustomerErrorResponse1.Errors` is the shared `Errors` record (`per_page` / `price_point` only). **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 8 (tests) — the `HttpClient` constructor argument is the seam; do not stub generated controller classes or private `RawClient`. **MUST load `dotnet-testing`** before writing tests for the integration layer.

---

## REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — constructing `MaxioAdvancedBillingClient`, `HttpClient` ownership, `AddMaxioAdvancedBillingClient` |
| `dotnet-authentication` | Step 1 — `BasicAuthCredentials`, config-sourced API key, 401 diagnosis |
| `dotnet-calling-endpoints` | Steps 2–6 — named arguments, `ct`, throw-only operations |
| `dotnet-models` | Steps 2–6 — envelopes, `required`/`init`, `StringEnum<T>`, cents vs dollars |
| `dotnet-error-handling` | Step 7 — Case A/B, `TryGet…`, `RawError`, **both** `JsonException` directions below |
| `dotnet-configuration-resilience` | Step 1 & 2 — `BaseUrl`/`Site`, retries vs writes, timeouts, list pagination |
| `dotnet-testing` | Step 8 — faking the `HttpClient` seam |

`System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## Assumptions & Blockers

**Assumptions**

- Customer `reference` is the eShopOnWeb ASP.NET Identity user id (string). `CreateCustomer.Email` / `FirstName` / `LastName` come from that user (split a single display name if the Identity user has no separate last name; these three are `required` on `CreateCustomer`).
- Default `POST /api/subscriptions` product handle is `eshop-pro` when the body omits one; both that handle and `Maxio:ProductFamilyHandle` are configuration (handles are stable). Numeric Maxio ids are never stored as config.
- `MAXIO_ENVIRONMENT` maps to `ServerEnvironment.Us` / `Eu` only; `"sandbox"` (or anything else) → `Us`. Sandbox vs live is the **subdomain**, not the environment enum.
- Payment profile / card / 3-DS fields stay omitted. Signup without a card is done by setting `CreateSubscription.PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance` (RI valid non-automatic member). Live omit produced 422 `"No payment method was on file for the $299.00 balance"`. If `Remittance` 422s as an invalid collection method, try `CollectionMethod.Invoice` (legacy). If the same “no payment method on file” 422 persists after that, treat as a catalog/site BLOCKER — do not invent a card flow.
- `Subscription.Reference` = `{userId}:{productHandle}` is an extra idempotency key. Uniqueness of subscription references on create is **UNVERIFIED**; the primary guard is `ListCustomerSubscriptions` + already-subscribed states.
- `GET /api/my-subscriptions` returns all subscriptions for the customer (every state). “Already subscribed” filtering applies only to `POST` idempotency.
- Metered component `api-call` is catalog-only; no Components / usage operations.
- JWT auth, PublicApi routing, and mapping onto eShop DTOs are application work, not SDK contract.

**Blockers**

- None. Every required capability is on the map (family-by-handle via `ListProductsForProductFamily` `handle:` prefix documented on `Api/ProductFamilies.cs`; customer lookup+create; subscribe by `product_handle`; list-by-customer-id). There is no list-by-customer-reference — not a blocker because `ReadCustomerByReference` then `ListCustomerSubscriptions` covers it. There is no atomic unique customer+product create — not a blocker because the pre-check + 422 re-read sequence uses exposed operations.
