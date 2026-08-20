# Maxio Advanced Billing — eShopOnWeb recurring subscribe

Additive parallel capability (does not replace Catalog → Basket → Order). Catalog is already seeded on site `cp-exp-4`; do **not** create products, families, or the `api-call` component.

Seeded handles (stable; never persist numeric IDs):

| Entity | Handle |
| --- | --- |
| Product family | `eshop-subscribe` (config `Maxio:ProductFamilyHandle`) |
| Pro plan (default subscribe target) | `eshop-pro` |
| Basic plan | `basic-plan` |
| Metered component (out of hero scope) | `api-call` |

Package: `AsadAli.AdvancedBilling.Sdk` · root namespace: `MaxioAdvancedBilling` · client: `MaxioAdvancedBillingClient`.

---

## Scope & sequence

1. **Client + config** — bind `Maxio:` (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, optional `BaseUrl`); construct `MaxioAdvancedBillingClient` with Basic auth, `ServerEnvironment.Us`, site subdomain, and optional Production `BaseUrl` override. Register in PublicApi DI.
2. **GET `/api/subscription-plans`** — `ProductFamilies.ListProductsForProductFamily` with `productFamilyId: "handle:{ProductFamilyHandle}"`. Map each `ProductResponse.Product` to handle/name/price/interval/description/archived.
3. **POST `/api/subscriptions`** (JWT identity) — idempotent find-or-create customer (`Customers.ReadCustomerByReference` then `Customers.CreateCustomer`); idempotent enroll (`Subscriptions.FindSubscription` by `{userId}:{productHandle}` **and** `Customers.ListCustomerSubscriptions` filtered by product handle + live state); else `Subscriptions.CreateSubscription` with `product_handle` (no card). Return id/state/plan/price/next-billing.
4. **GET `/api/my-subscriptions`** — `ReadCustomerByReference`; on 404 return empty list; else `ListCustomerSubscriptions` and project plan/price/state/next-billing-date.
5. **Error boundary** around every SDK call (Case A/B mixed; `JsonException` from two directions).
6. **Tests** for the integration seam (HttpClient constructor), not SDK internals.

Hero default product handle: `eshop-pro` when the POST body omits one. Payment method is not required (seeded plans: no trial, no setup fee, expires never, taxable no).

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

| Fact | Value | Cite |
| --- | --- | --- |
| NuGet package | `AsadAli.AdvancedBilling.Sdk` | `sdk-map.md` |
| Root namespace | `MaxioAdvancedBilling` | `sdk-map.md` |
| Client | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` | `sdk-map.md`, `MaxioAdvancedBillingClient.cs` |
| Only constructor | `MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` |
| Options | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` | `MaxioAdvancedBillingClientOptions.cs` |
| Options members | `Environment`: `MaxioAdvancedBilling.Servers.ServerEnvironment` (default `ServerEnvironment.Default()` → `Us`); `Retry`: `MaxioAdvancedBilling.Core.Configuration.RetryOptions` (default `RetryOptions.Default()`); `Server`: `MaxioAdvancedBilling.ServerOptions` (default `new()`); `BasicAuth`: `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md`, `MaxioAdvancedBillingClientOptions.cs` |
| Auth | HTTP Basic. `BasicAuthCredentials.Username` = API key (`Maxio:ApiKey`); `BasicAuthCredentials.Password` = literal `"x"`. Both members are C# `required`. | `sdk-map.md`, `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Environments | `ServerEnvironment` is `StringEnum<ServerEnvironment>` in `MaxioAdvancedBilling.Servers` — **not** a C# enum. Members: `Us ("US")` (default, US hosting `https://{site}.chargify.com`), `Eu ("EU")`. **There is no Sandbox member.** Sandbox is the site subdomain on US or EU. Use `ServerEnvironment.Us` for `cp-exp-4`. | `sdk-map.md`, `Servers/ServerEnvironment.cs`, `enums.md` N/A |
| Site (subdomain) | `options.Server.Production.Us.Site` (`string`, default `"subdomain"`). Set to `Maxio:Subdomain`. | `sdk-map.md`, `Servers/ProductionOptions.cs` |
| Custom BaseUrl | `options.Server.Production.Us.BaseUrl` (`string`, default `"https://{site}.chargify.com"`). **When `Maxio:BaseUrl` is set, assign it verbatim to this property** — it replaces the template, it is not concatenated. Nested type: `MaxioAdvancedBilling.Servers.ProductionOptions.UsOptions`. Sibling EU override (not used here): `options.Server.Production.Eu.BaseUrl` / `.Site`. Ebb group is unused (event ingest only). | `sdk-map.md`, `ServerOptions.cs`, `Servers/ProductionOptions.cs` |
| `ServerOptions` | namespace `MaxioAdvancedBilling` (repo-root file). Members: `Production`: `MaxioAdvancedBilling.Servers.ProductionOptions`; `Ebb`: `MaxioAdvancedBilling.Servers.EbbOptions`. | `ServerOptions.cs` |
| DI | `MaxioAdvancedBilling.ServiceCollectionExtensions.AddMaxioAdvancedBillingClient(this IServiceCollection, Action<MaxioAdvancedBillingClientOptions>? configure = null)` | `sdk-map.md`, `ServiceCollectionExtensions.cs` |
| Controllers used | `client.ProductFamilies` (`MaxioAdvancedBilling.Api.ProductFamilies`), `client.Products` (`…Api.Products`), `client.Customers` (`…Api.Customers`), `client.Subscriptions` (`…Api.Subscriptions`) | `sdk-map.md` |

Config section `Maxio:`:

| Config key | Env var | SDK mapping |
| --- | --- | --- |
| `Maxio:ApiKey` | `MAXIO_API_KEY` | `options.BasicAuth.Username` |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | `options.Server.Production.Us.Site` |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | request arg `handle:{value}` (not a client option) |
| `Maxio:BaseUrl` | optional | when non-empty: `options.Server.Production.Us.BaseUrl` verbatim |

Manual construction:

```csharp
var options = new MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions
{
    BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials
    {
        Username = apiKey,
        Password = "x",
    },
    Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us,
};
options.Server.Production.Us.Site = subdomain;
if (!string.IsNullOrWhiteSpace(baseUrl))
    options.Server.Production.Us.BaseUrl = baseUrl;
var client = new MaxioAdvancedBilling.MaxioAdvancedBillingClient(httpClient, options);
```

---

### Operations

#### 1. List plans — `ListProductsForProductFamily` (preferred)

| | |
| --- | --- |
| Controller | `client.ProductFamilies` |
| HTTP | `GET /product_families/{product_family_id}/products.json` |
| Signature | `Task<IReadOnlyList<ProductResponse>> ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| Must-pass | `productFamilyId`; the 8 params `dateField` … `include` are nullable **with no C# default** → pass `null` to skip. `page`/`perPage`/`ct` have defaults. |
| Handle lookup | `productFamilyId` is **either** the numeric id as a string **or** the handle prefixed with `handle:` (XML: “Either the product family's id or its handle prefixed with `handle:`”). Pass `"handle:" + configuredProductFamilyHandle` (e.g. `"handle:eshop-subscribe"`). Never store/use the family's numeric id. |
| Return | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>` |
| Envelope | `ProductResponse.Product` (`product`) — `Product` is `required` |
| Error | **Case A** `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>` · `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback, including 401/403] |
| Pagination | manual `page`+`perPage` (default 20, max 200 per XML). Two seeded plans fit one page; still pass `perPage: 200` or loop pages. |
| Cite | `operations/ProductFamilies.md`, `Api/ProductFamilies.cs`, `records-3-Of-Su.md` |

**Inner `Product` fields this endpoint must project** (`MaxioAdvancedBilling.Models.Product`, `records-3-Of-Su.md`):

| C# (wire) | Type | Use |
| --- | --- | --- |
| `Handle` (`handle`) | `string?` | plan id shown to client / POST body |
| `Name` (`name`) | `string?` | display |
| `Description` (`description`) | `string?` | display if present |
| `PriceInCents` (`price_in_cents`) | `long?` | price (29900 = $299.00; 2900 = $29.00) |
| `Interval` (`interval`) | `int?` | period count |
| `IntervalUnit` (`interval_unit`) | `IntervalUnit?` | `Day` / `Month` |
| `ArchivedAt` (`archived_at`) | `DateTimeOffset?` | null ⇒ not archived (no product-state enum) |
| `ProductFamily` (`product_family`) | `ProductFamily?` | confirm `Handle` == configured family |
| `RequireCreditCard` (`require_credit_card`) | `bool?` | **project on list-plans DTO.** XML: “Boolean that controls whether a payment profile is required to be entered for customers wishing to sign up on this product.” Independent of `PaymentCollectionMethod`. Live 422 “No payment method was on file for the $299.00 balance” can still fire when this is `false` if collection is Automatic and a balance is assessed. Seed notes say false — **verify from this field**, do not assume. |
| `RequestCreditCard` (`request_credit_card`) | `bool?` | XML: deprecated unless legacy hosted pages — ignore |
| `ProductPricePointHandle` (`product_price_point_handle`) | `string?` | optional; omit on create to use default PP |

`ProductFamily` (`records-3-Of-Su.md`): `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `ArchivedAt (archived_at): DateTimeOffset?`.

`ListProductsFilter` (`records-2-Cr-Ne.md`): `Ids (ids): IReadOnlyList<int>?`, `PrepaidProductPricePoint (prepaid_product_price_point): PrepaidProductPricePointFilter?`, `UseSiteExchangeRate (use_site_exchange_rate): bool?` — **cannot filter by family handle**. Pass `filter: null`.

#### 1b. `ReadProductFamily` — **do not use for this flow**

| | |
| --- | --- |
| Signature | `Task<ProductFamilyResponse> ReadProductFamily(int id, CancellationToken ct = default)` |
| Catch | HTTP notes say the family can be specified as id **or** `handle:my-family`, but the generated C# parameter is `int id`. **The SDK cannot pass a handle to this operation.** Envelope: `ProductFamilyResponse.ProductFamily` (`product_family`) is `ProductFamily?` (nullable). Error **Case B** `SdkException<RawError>`. |
| Cite | `operations/ProductFamilies.md` |

#### 1c. `ListProducts` — site-wide fallback only

| | |
| --- | --- |
| Controller | `client.Products` |
| HTTP | `GET /products.json` |
| Signature | `Task<IReadOnlyList<ProductResponse>> ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| Must-pass | 8 params `dateField` … `include` — pass `null` to skip |
| Return / envelope | same `IReadOnlyList<ProductResponse>` / `.Product` |
| Error | **Case B** `SdkException<RawError>` |
| Use | only if family-scoped list is unavailable. Filter client-side: `Product.ProductFamily?.Handle == configuredHandle`. Prefer 1. |
| Cite | `operations/Products.md` |

#### 1d. `ReadProductByHandle` — single plan by handle

| | |
| --- | --- |
| Controller | `client.Products` |
| HTTP | `GET /products/handle/{api_handle}.json` |
| Signature | `Task<ProductResponse> ReadProductByHandle(string apiHandle, CancellationToken ct = default)` |
| Return | `ProductResponse` → `.Product` (`required`) |
| Error | **Case B** `SdkException<RawError>` (404 = `StatusCode` NotFound) |
| Use | lookup `eshop-pro` / `basic-plan` individually; not a family list. |
| Cite | `operations/Products.md` |

---

#### 2. Find customer by eShop user — `ReadCustomerByReference`

Idempotency key: Maxio `Customer.Reference` (`reference`) = the eShopOnWeb user id (stable unique id from the JWT). **Do not key uniqueness on email** (emails can change; reference cannot collide). Email is still a required create field.

| | |
| --- | --- |
| Controller | `client.Customers` |
| HTTP | `GET /customers/lookup.json?reference=` |
| Signature | `Task<CustomerResponse> ReadCustomerByReference(string reference, CancellationToken ct = default)` |
| Return | `CustomerResponse` |
| Envelope | `CustomerResponse.Customer` (`customer`) — `Customer` is `required` |
| Read | `Customer.Id` (`id`): `int?` — this is the Maxio customer id for later calls. Also `Reference`, `Email`, `FirstName`, `LastName`. |
| Error | **Case B** `SdkException<RawError>` · `StatusCode` · `ReadAsString()` / `ReadAsJson<T>()` / `ReadAsBytes()`. **404** ⇒ no customer for that reference (not found). **401/403** via same `StatusCode`. |
| Cite | `operations/Customers.md`, `records-2-Cr-Ne.md` (`CustomerResponse`, `Customer`) |

`ListCustomers(..., q: reference)` is a **search**, not an exact match. Notes: “To retrieve a single, exact match by reference, use the lookup endpoint.” Do not use `ListCustomers` for find-or-create.

---

#### 3. Create customer — `CreateCustomer`

| | |
| --- | --- |
| Controller | `client.Customers` |
| HTTP | `POST /customers.json` |
| Signature | `Task<CustomerResponse> CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` |
| Must-pass | `body` is nullable **with no default** → pass explicitly (never omit the arg). |
| Return / envelope | `CustomerResponse` → `.Customer` (`required`) → `.Id` |
| Error | **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>` · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| Duplicate | Notes: “you may only create one customer for a given reference value.” Duplicate `reference` → **422**. There is no HTTP idempotency-key header. |
| Cite | `operations/Customers.md`, `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md`, `Errors/CreateCustomerError.cs` |

**Request envelope** (`CreateCustomerRequest`, `records-1-Ac-Cr.md`):

| C# (wire) | Type | Required |
| --- | --- | --- |
| `Customer` (`customer`) | `CreateCustomer` | **`required`** |

**`CreateCustomer` fields** (`records-1-Ac-Cr.md`):

| C# (wire) | Type | Required? | This flow |
| --- | --- | --- | --- |
| `FirstName` (`first_name`) | `string` | **`required`** | from Identity profile / given name |
| `LastName` (`last_name`) | `string` | **`required`** | from Identity profile / surname |
| `Email` (`email`) | `string` | **`required`** | from Identity |
| `Reference` (`reference`) | `string?` | optional in C# | **set** to eShop user id |
| `CcEmails`, `Organization`, `Address`, `Address2`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `VatNumber`, `TaxExempt`, `TaxExemptReason`, `ParentId`, `SalesforceId` | various `?` | optional | omit |

**`Customer` (response) fields used** (`records-2-Cr-Ne.md`): `Id (id): int?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?`. If `Id` is null after a successful create/lookup, do not call create-subscription with `customer_id`; use `customer_reference` instead (same user id string).

**422 payload (generated):** `CustomerErrorResponse1.Errors` (`errors`) is `MaxioAdvancedBilling.Models.Errors?`, and `Errors` only has `PerPage (per_page): IReadOnlyList<string>?` and `PricePoint (price_point): IReadOnlyList<string>?` (`records-2-Cr-Ne.md`, `Models/Errors.cs`). A separate union `MaxioAdvancedBilling.Models.AnyOf.Errors1` (`CustomerError` \| `IReadOnlyList<string>`, accessors `TryGetCustomerError` / `TryGetListOfString`) matches typical customer-error JSON **but is not** what `CreateCustomerError.TryGetCustomerErrorResponse1` returns. **UNVERIFIED** whether live 422 for a duplicate reference deserializes into `CustomerErrorResponse1`. Defensive: on 422 (or `JsonException` while mapping the error), call `ReadCustomerByReference` again; for the user-facing message extract best-effort from `TryGetCustomerErrorResponse1` then `TryGetRawError` → `ReadAsString()`, else a generic validation message.

**Find-or-create sequence:** `ReadCustomerByReference(userId)` → if Case B 404, `CreateCustomer` with `Reference = userId` → if 422, `ReadCustomerByReference` again. That closes the double-click race (unique reference + lookup-first).

---

#### 4. Create subscription — `CreateSubscription`

| | |
| --- | --- |
| Controller | `client.Subscriptions` |
| HTTP | `POST /subscriptions.json` |
| Signature | `Task<SubscriptionResponse> CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` |
| Must-pass | `body` nullable, no default → pass explicitly |
| Return | `SubscriptionResponse` |
| Envelope | `SubscriptionResponse.Subscription` (`subscription`): `Subscription?` — **nullable** (unlike `ProductResponse.Product`). Null-check before reading. |
| Error | **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>` · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| 422 body | `ErrorListResponse1.Errors` (`errors`): `IReadOnlyList<string>` **`required`** — join these strings for the user message |
| Cite | `operations/Subscriptions.md`, `records-2-Cr-Ne.md`, `records-3-Of-Su.md`, `records-4-Su-We.md`, `Errors/CreateSubscriptionError.cs` |

**Request envelope** (`CreateSubscriptionRequest`):

| C# (wire) | Type | Required |
| --- | --- | --- |
| `Subscription` (`subscription`) | `CreateSubscription` | **`required`** |

**`CreateSubscription` — set these; omit the rest** (every field is optional in C#; API needs a product xor and a customer xor):

| C# (wire) | Type | This flow |
| --- | --- | --- |
| `ProductHandle` (`product_handle`) | `string?` | **set** (`eshop-pro` / `basic-plan`). Do **not** set `ProductId`. |
| `CustomerId` (`customer_id`) | `int?` | **set** from `Customer.Id` after find-or-create |
| `CustomerReference` (`customer_reference`) | `string?` | optional extra: same eShop user id (alternative to id) |
| `Reference` (`reference`) | `string?` | **set** `{userId}:{productHandle}` for subscription uniqueness / `FindSubscription` |
| `ProductPricePointHandle` / `ProductPricePointId` | `string?` / `int?` | omit → default price point |
| `PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes` | optional | **omit** — no card capture in this flow |
| `CustomerAttributes` | `CustomerAttributes?` | omit (customer already exists) |
| `PaymentCollectionMethod` (`payment_collection_method`) | `CollectionMethod?` | **set** `CollectionMethod.Remittance` (wire `remittance`). Do **not** omit (omit left the site default; live 422 was “No payment method was on file for the $299.00 balance”). Do **not** set `Automatic` (that path collects the assessed balance and needs a payment profile). Do **not** set `Prepaid` (needs `PrepaidConfiguration`). `Invoice` (wire `invoice`) is **legacy Statements only** — invalid on current Relationship Invoicing. |
| `NextBillingAt` / `InitialBillingAt` / `DeferSignup` | `DateTimeOffset?` / `DateTimeOffset?` / `bool? = false` | **omit.** `NextBillingAt` in the future skips all capture (import-sync); `DeferSignup=true` creates Awaiting Signup. Neither is the no-card *active* enroll path. |
| `Ref` (`ref`) | `string?` | **do not use** — not the lookup key (`FindSubscription` is by `reference`) |

All other `CreateSubscription` members (`CouponCode`, `Components`, `AgreementAcceptance`, `OfferId` union, …) — omit. Do not attach `api-call`.

Notes (operation): “Specify the product with `product_id` or `product_handle`.” “Identify an existing customer with `customer_id` or `customer_reference`.” “Payment information may be required … depending on the options for the Product.” **No-card enroll (hero):** set `PaymentCollectionMethod = CollectionMethod.Remittance` and omit all payment-profile fields. Exact initializer:

```csharp
PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance,
```

`CollectionMethod` XML (`enums.md`, `Models/Enums/CollectionMethod.cs`): legacy Statements valid options = `invoice`, `automatic`; current Relationship Invoicing valid options = `remittance`, `automatic`, `prepaid`. `NextBillingAt` XML (`Models/CreateSubscription.cs`): if omitted, “any trial and/or initial charges will be assessed and charged at the time of subscription creation. If the card cannot be successfully charged, the subscription will not be created.” That is why omitting collection method assessed $299 immediately and then 422’d without a card. **UNVERIFIED** that omit defaults specifically to `Automatic` — the map does not name that default; `Site.DefaultPaymentCollectionMethod` exists on the Site model but **do not add** `Sites.ReadSite` (out of hero scope). Remittance is the current-architecture no-card member; if create 422s that remittance is not valid, the site is Statements — then set `CollectionMethod.Invoice` instead (same field, no card). No 3-DS; do not send a card.

**No SDK idempotency-key header** exists on this operation. Double-click prevention is application-side:

1. `FindSubscription(reference: "{userId}:{productHandle}", …)` — if found, return it.
2. `ListCustomerSubscriptions(customerId)` — if any row has `Product.Handle` equal to the requested handle **and** a live `State` (see enum table), return that row (covers older subs created without `reference`).
3. Else `CreateSubscription` with that same `Reference`.
4. If create returns 422, re-run (1)+(2) and return the existing sub if present; otherwise surface `ErrorListResponse1.Errors`.

Live states for “already enrolled”: `Active`, `Trialing`, `Assessing`, `PastDue`, `SoftFailure`, `Pending`, `AwaitingSignup`, `OnHold`, `Paused`, `Suspended`, `Unpaid`. Treat `Canceled`, `Expired`, `TrialEnded`, `FailedToCreate` as not enrolled (allow a new subscribe).

---

#### 5. Find subscription by reference — `FindSubscription`

| | |
| --- | --- |
| Controller | `client.Subscriptions` |
| HTTP | `GET /subscriptions/lookup.json?reference=` |
| Signature | `Task<SubscriptionResponse> FindSubscription(string? reference, CancellationToken ct = default)` |
| Must-pass | `reference` is nullable **with no default** → pass the string explicitly (do not omit) |
| Return / envelope | `SubscriptionResponse` → `.Subscription` (`Subscription?`) |
| Error | **Case A** `SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>` · `TryGetNoContent(out RawError)` **[404]** · `TryGetRawError(out RawError)` [fallback] |
| 404 | `TryGetNoContent` succeeding **is** “no subscription with that reference” — not a user-facing failure. |
| Cite | `operations/Subscriptions.md`, `Errors/FindSubscriptionError.cs` |

---

#### 6. List a customer's subscriptions — `ListCustomerSubscriptions`

| | |
| --- | --- |
| Controller | `client.Customers` |
| HTTP | `GET /customers/{customer_id}/subscriptions.json` |
| Signature | `Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` |
| Return | `IReadOnlyList<SubscriptionResponse>` — each `.Subscription` is `Subscription?` |
| Error | **Case B** `SdkException<RawError>` (404 if customer id unknown) |
| Pagination | none (full list) |
| Cite | `operations/Customers.md` |

Lookup is **by numeric customer id**, not by reference. Resolve id via `ReadCustomerByReference` first. If that 404s, return an empty list to the shopper (they have never subscribed).

`ListSubscriptions` (`client.Subscriptions`) can filter `product:` as `int?` only — **numeric product id, not handle**. Do not use it for this flow.

---

#### Response fields to confirm back to the user (`Subscription`, `records-3-Of-Su.md`)

| C# (wire) | Type | Meaning |
| --- | --- | --- |
| `Id` (`id`) | `int?` | subscription id |
| `State` (`state`) | `SubscriptionState?` | plan state |
| `ProductPriceInCents` (`product_price_in_cents`) | `long?` | recurring price |
| `CurrentPeriodEndsAt` (`current_period_ends_at`) | `DateTimeOffset?` | period end / next bill boundary |
| `NextAssessmentAt` (`next_assessment_at`) | `DateTimeOffset?` | next assessment (next billing date) |
| `Reference` (`reference`) | `string?` | our idempotency key |
| `Customer` (`customer`) | `Customer?` | nested; read `.Id` |
| `Product` (`product`) | `Product?` | nested; read `.Handle`, `.Name`, `.PriceInCents`, `.Interval`, `.IntervalUnit` |

Project **next billing date** as `CurrentPeriodEndsAt` (fallback `NextAssessmentAt` if the former is null). **UNVERIFIED** which of the two the live sandbox populates on a no-card signup — read both, prefer `CurrentPeriodEndsAt`.

---

### Error types (every in-scope operation)

`MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>` exposes **only** `required TError Error` — no status property on the exception (`Core/Exceptions/SdkException.cs`). Status comes from the error object. Base: `MaxioAdvancedBilling.Core.ErrorResponse.ApiError.TryGetRawError(out RawError)`. `RawError` (`MaxioAdvancedBilling.Core.ErrorResponse`): `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`, `ReadAsBytes(): ReadOnlyMemory<byte>`.

No `…Result` / no-throw variants exist. Every call throws.

| Operation | TError | Accessors | 422 | 404 | 401/403 |
| --- | --- | --- | --- | --- | --- |
| `ListProductsForProductFamily` | `ListProductsForProductFamilyError` (A) | `TryGetString(out string)` [404]; `TryGetRawError` | via fallback `RawError` | `TryGetString` | `TryGetRawError` → `StatusCode` |
| `ListProducts` / `ReadProductByHandle` / `ReadProductFamily` | `RawError` (B) | `StatusCode`, `ReadAsString` | `StatusCode` | `StatusCode` | `StatusCode` |
| `ReadCustomerByReference` / `ListCustomerSubscriptions` / `ListCustomers` | `RawError` (B) | same | same | `StatusCode == NotFound` | `StatusCode` |
| `CreateCustomer` | `CreateCustomerError` (A) | `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422]; `TryGetRawError` | typed accessor (payload may not match live body — see §3) | fallback | `TryGetRawError` |
| `CreateSubscription` | `CreateSubscriptionError` (A) | `TryGetErrorListResponse1(out ErrorListResponse1)` [422]; `TryGetRawError` | `ErrorListResponse1.Errors` (`IReadOnlyList<string>`) | fallback | `TryGetRawError` |
| `FindSubscription` | `FindSubscriptionError` (A) | `TryGetNoContent(out RawError)` [404]; `TryGetRawError` | fallback | `TryGetNoContent` | `TryGetRawError` |

401/403 never have a dedicated `TryGet…`; they always land on `RawError.StatusCode`.

---

### Enums in scope (`MaxioAdvancedBilling.Models.Enums` unless noted; `StringEnum<T>` — write `Type.Member`, not `Type.member`; or `Type.FromValue("wire")`)

| Enum | Members (C# (wire)) | Cite |
| --- | --- | --- |
| `MaxioAdvancedBilling.Servers.ServerEnvironment` | `Us (US)`, `Eu (EU)` | `Servers/ServerEnvironment.cs` |
| `IntervalUnit` | `Day (day)`, `Month (month)` | `enums.md` |
| `ExpirationIntervalUnit` | `Day (day)`, `Month (month)`, `Never (never)` | `enums.md` |
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` | `enums.md` |
| `SubscriptionStateFilter` | `Active`, `Canceled`, `Expired`, `ExpiredCards`, `OnHold`, `PastDue`, `PendingCancellation`, `PendingRenewal`, `Suspended`, `TrialEnded`, `Trialing`, `Unpaid` (wires match snake of the C# names except `expired_cards`, `pending_cancellation`, `pending_renewal`, `trial_ended`) | `enums.md` — used only if you call `ListSubscriptions` (not in hero path) |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` | `enums.md` / `Models/Enums/CollectionMethod.cs`. **Hero: set `CollectionMethod.Remittance`.** `Automatic` = collect assessed balance (needs a payment profile). `Prepaid` = prepaid architecture. `Invoice` = legacy Statements only. |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` | `enums.md` — pass `null` on lists |
| `ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)` | `enums.md` — pass `null` |
| `PricePointType` | `Catalog (catalog)`, `Default (default)`, `Custom (custom)` | `enums.md` — response only |
| `TrialType` | `NoObligation (no_obligation)`, `PaymentExpected (payment_expected)` | `enums.md` — unused (no trial) |

No product-state enum: archived ⇔ `Product.ArchivedAt != null`.

**Unions actually touched:** none on the request path. `CreateSubscription.OfferId` is a union — do not set it. `Errors1` exists but is **not** the CreateCustomer 422 `out` type (see §3).

---

## Trap notes

⚠ Step 1 (client registration) — the constructor **requires** a `HttpClient`; DI via `AddMaxioAdvancedBillingClient` also constructs one. Who owns that client, whether it is long-lived, and whether the SDK wrapper should be singleton vs per-request is not visible from the signature. **MUST load `dotnet-client-initialization`** before wiring DI.

⚠ Step 1 (auth) — credentials live on `options.BasicAuth` (`Username`/`Password`, both `required`) and must be present before the first call; loading the key from configuration vs hardcoding, and what happens if `BasicAuth` is left null, are usage traps. **MUST load `dotnet-authentication`** before binding `Maxio:ApiKey`.

⚠ Step 1 (BaseUrl / retries) — `Retry`/`Timeout` on `MaxioAdvancedBillingClientOptions` are not the timeout of the `HttpClient` you pass in, and they do not bound a whole call. Create customer/subscription are `POST`s: whether a failed write can be re-sent (transport vs status) is why find-or-create and subscription `reference` exist. **MUST load `dotnet-configuration-resilience`** before registering or tuning the client.

⚠ Steps 2–4 (calling) — `ListProductsForProductFamily` / `ListProducts` have eight nullable parameters **without C# defaults**; a positional call will mis-bind. Named arguments; skip with `null`. Cancellation token is `ct:`. **MUST load `dotnet-calling-endpoints`** before the first operation call.

⚠ Steps 2–4 (models) — records are `init`-only; `required` members (`CreateCustomerRequest.Customer`, `CreateCustomer.FirstName/LastName/Email`, `CreateSubscriptionRequest.Subscription`, `ErrorListResponse1.Errors`, envelopes noted `!req`) must appear in the object initializer. Enums are `StringEnum<T>` (`SubscriptionState.Active`, not a C# enum). `SubscriptionResponse.Subscription` is nullable. **MUST load `dotnet-models`** before building payloads or mapping responses.

⚠ Step 5 (errors) — operations in this flow mix Case A (`CreateCustomer`, `CreateSubscription`, `FindSubscription`, `ListProductsForProductFamily`) and Case B (`ReadCustomerByReference`, `ListCustomerSubscriptions`, `ReadProductByHandle`). `TryGetRawError` is not a catch-all on the wrong `TError`. There are no Result/no-throw variants. **MUST load `dotnet-error-handling`** before writing the catch ladder.

⚠ Step 5 (errors) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 5 (errors) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. This is the realistic path for `CreateCustomer` 422 if live `errors` is a string list rather than `Models.Errors`. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 6 (tests) — the test seam is the `HttpClient` constructor argument, not internal SDK types. **MUST load `dotnet-testing`** before stubbing.

---

## REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
| --- | --- |
| `dotnet-client-initialization` | Step 1 — constructing/`AddMaxioAdvancedBillingClient`, HttpClient ownership/lifetime |
| `dotnet-authentication` | Step 1 — `BasicAuthCredentials`, config-bound API key, password `"x"` |
| `dotnet-configuration-resilience` | Step 1 — retries/timeout vs HttpClient, BaseUrl/server selection, pagination, whether a failed POST can be re-sent |
| `dotnet-calling-endpoints` | Steps 2–4 — named args, must-pass nullables, `ct:`, throw-only operations |
| `dotnet-models` | Steps 2–4 — required/init records, `StringEnum<T>`, nullable envelopes, wire vs C# names |
| `dotnet-error-handling` | Step 5 — Case A/B, `TryGet…`, `RawError`, **both** `JsonException` directions |
| `dotnet-testing` | Step 6 — HttpClient seam, asserting behaviour not SDK internals |

---

## Assumptions & Blockers

**Assumptions**

- Maxio customer `reference` = eShopOnWeb user id from the JWT (not email). Email/first/last name are taken from the Identity user; if first/last are missing, the implementer supplies non-empty placeholders (API requires both strings).
- Sandbox site `cp-exp-4` is US-hosted → `ServerEnvironment.Us` + `Production.Us.Site` / optional `Production.Us.BaseUrl`.
- POST `/api/subscriptions` body carries a product handle; default `eshop-pro` when omitted. Handles allowed: `eshop-pro`, `basic-plan` (reject others).
- Re-subscribe is allowed when the only matching subscription is `Canceled` / `Expired` / `TrialEnded` / `FailedToCreate`.
- `api-call` metered component is out of hero scope (no allocations/usages).
- PublicApi JWT conventions, route registration, and DTO shapes follow existing eShopOnWeb PublicApi endpoints (main agent; not an SDK fact).
- Sandbox `cp-exp-4` is **current Relationship Invoicing** (not legacy Statements), so the no-card `CollectionMethod` member is `Remittance` not `Invoice`. If create 422s that remittance is invalid, switch that one field to `CollectionMethod.Invoice`.
- Product `RequireCreditCard` may still be `true` on the live seed despite task notes — confirm from `ListProductsForProductFamily` → `Product.RequireCreditCard`. A `true` value is a product-catalog issue, not a reason to send a card. Remittance remains the create-side no-card flag.

**Blockers**

- None. `ReadProductFamily(int id)` cannot take a handle in this SDK; that is not a blocker because `ListProductsForProductFamily(string productFamilyId)` accepts `handle:{handle}`.
- CreateCustomer 422 payload vs live wire is **UNVERIFIED** (generated `CustomerErrorResponse1.Errors` vs unused `Errors1` union). Handled by the defensive directive in §3 — not a blocker.
