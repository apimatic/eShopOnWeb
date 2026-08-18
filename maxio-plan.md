# Maxio Advanced Billing — eShopOnWeb recurring subscriptions

Additive parallel capability on `src/PublicApi` (JWT). Does **not** replace cart/checkout. Maxio is system of record.

**SDK identity** (`sdk-map.md`): NuGet `AsadAli.AdvancedBilling.Sdk` · root namespace `MaxioAdvancedBilling` · map stamp **v1.0.2** (`15db14b`). Never hard-code site, family, or numeric product IDs — bind handles from `Maxio:*` config.

## Scope & sequence

| Step | What | SDK operations |
|---|---|---|
| 1 | Bind `Maxio:*` config; register client + Basic auth + Production server (site / optional BaseUrl) | `AddMaxioAdvancedBillingClient` / `new MaxioAdvancedBillingClient` |
| 2 | `GET /api/subscription-plans` — list catalog products for the configured family handle | `ProductFamilies.ListProductsForProductFamily` (`productFamilyId: "handle:{familyHandle}"`). Fallback per-plan: `Products.ReadProductByHandle` |
| 3 | `POST /api/subscriptions` — idempotent ensure-customer, then enroll by **product handle** | `Customers.ReadCustomerByReference` → on 404 `Customers.CreateCustomer` (re-read on 422 race) → `Subscriptions.FindSubscription` → on 404 `Subscriptions.CreateSubscription`. Response envelope is confirmation (optional `Subscriptions.ReadSubscription`) |
| 4 | `GET /api/my-subscriptions` — plan / price / state / next-billing-date | `Customers.ReadCustomerByReference` → if 404 return `[]`; else `Customers.ListCustomerSubscriptions` |
| 5 | PublicApi error boundary around every SDK call | Case A/B per operation row below |

Out of hero scope: metered component `api-call`, plan changes, payment profiles, webhooks.

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

### Namespaces

| Kind | Namespace | Source |
|---|---|---|
| Client, options, `ServerOptions`, DI extension | `MaxioAdvancedBilling` | `sdk-map.md`, `ServiceCollectionExtensions.cs`, `ServerOptions.cs` |
| Controllers (`client.Customers`, etc.) | `MaxioAdvancedBilling.Api` | `sdk-map.md` |
| Records | `MaxioAdvancedBilling.Models` | `sdk-map.md` |
| Enums (`StringEnum<T>`, **not** C# enums) | `MaxioAdvancedBilling.Models.Enums` | `map/models/enums.md` |
| Unions | `MaxioAdvancedBilling.Models.AnyOf` · `MaxioAdvancedBilling.Models.OneOf` | `map/models/unions.md` |
| Typed `{Operation}Error` | `MaxioAdvancedBilling.Errors` | `sdk-map.md` |
| `SdkException<T>` | `MaxioAdvancedBilling.Core.Exceptions` | `sdk-map.md` (`Core/Exceptions/SdkException.cs`) |
| `ApiError`, `RawError` | `MaxioAdvancedBilling.Core.ErrorResponse` | `sdk-map.md` |
| `BasicAuthCredentials` | `MaxioAdvancedBilling.Core.Authentication.Basic` | `sdk-map.md` |
| `RetryOptions` | `MaxioAdvancedBilling.Core.Configuration` | `sdk-map.md` |
| `ServerEnvironment`, `ProductionOptions` (nested `UsOptions` / `EuOptions`) | `MaxioAdvancedBilling.Servers` | `sdk-map.md`, `Servers/ProductionOptions.cs` |

`SdkException<TError>` public member: `Error: TError` (`Core/Exceptions/SdkException.cs`). No-throw `*Result` variants: **absent** — every operation is throw-only (`sdk-map.md`).

---

### 1. Client construction / DI / HttpClient

| Fact | Value | Source |
|---|---|---|
| Client | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` | `sdk-map.md` |
| Options | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` | `sdk-map.md` |
| Only ctor | `MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` |
| Options members | `Environment: ServerEnvironment` · `Retry: RetryOptions` · `Server: ServerOptions` · `BasicAuth: BasicAuthCredentials?` | `sdk-map.md` |
| DI | `IServiceCollection.AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>? configure = null)` | `sdk-map.md`, `ServiceCollectionExtensions.cs` |
| DI lifetime | **`AddSingleton`** of `MaxioAdvancedBillingClient` | `ServiceCollectionExtensions.cs` |
| HttpClient ownership (DI) | Extension calls `services.AddHttpClient()`, then `IHttpClientFactory.CreateClient()` (unnamed) **once** inside the singleton factory and passes that instance to the client ctor. The SDK client is **not** `IDisposable`. | `ServiceCollectionExtensions.cs`, `MaxioAdvancedBillingClient.cs` |
| HttpClient ownership (manual `new`) | Caller supplies and owns the `HttpClient` passed to the ctor. | `sdk-map.md` |
| Retry options | All `RetryOptions` members are `required` — start from `RetryOptions.Default()` or set every member. | `sdk-map.md` |

Manual construction (`sdk-map.md`):

```csharp
var options = new MaxioAdvancedBillingClientOptions { /* BasicAuth, Environment, Server */ };
var client = new MaxioAdvancedBillingClient(httpClient, options);
```

Controller accessors used here: `client.ProductFamilies`, `client.Products`, `client.Customers`, `client.Subscriptions`.

Config **names only** (no secret values): `Maxio:ApiKey` (`MAXIO_API_KEY`), `Maxio:Subdomain` (`MAXIO_SITE_SUBDOMAIN`), `Maxio:ProductFamilyHandle` (`MAXIO_DEFAULT_PRODUCT_FAMILY`), optional `Maxio:BaseUrl`.

---

### 2. Auth

| Fact | Value | Source |
|---|---|---|
| Scheme | HTTP Basic only | `sdk-map.md` |
| Options property | `options.BasicAuth` | `sdk-map.md` |
| Credentials type | `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials` | `sdk-map.md` |
| Members | `Username: string` (**required**) · `Password: string` (**required**) | `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Pattern | `Username` = API key from `Maxio:ApiKey`; `Password` = literal `"x"` | `sdk-map.md` |

```csharp
o.BasicAuth = new BasicAuthCredentials { Username = configuration["Maxio:ApiKey"]!, Password = "x" };
```

---

### 3. Server / environment / BaseUrl

| Fact | Value | Source |
|---|---|---|
| `options.Environment` | `MaxioAdvancedBilling.Servers.ServerEnvironment` (`StringEnum`) | `sdk-map.md` |
| Constants | `ServerEnvironment.Us` (wire `"US"`, **default**) · `ServerEnvironment.Eu` (wire `"EU"`) | `sdk-map.md`, `Servers/ServerEnvironment.cs` |
| Sandbox constant | **None** — sandbox is a US-hosted site. Use `ServerEnvironment.Us`. | `sdk-map.md` |
| Production US template | `https://{site}.chargify.com` | `sdk-map.md` |
| Production EU template | `https://{site}.ebilling.maxio.com` | `sdk-map.md` |
| Site param (US) | `options.Server.Production.Us.Site` (`string`, default `"subdomain"`) ← bind `Maxio:Subdomain` | `sdk-map.md`, `Servers/ProductionOptions.cs` |
| BaseUrl (US) | `options.Server.Production.Us.BaseUrl` (`string`, default `"https://{site}.chargify.com"`) | `sdk-map.md`, `Servers/ProductionOptions.cs` |
| EU twins | `options.Server.Production.Eu.Site` / `.Eu.BaseUrl` | `sdk-map.md` |
| Nested types | `ServerOptions` (root ns) → `ProductionOptions` → nested `ProductionOptions.UsOptions` / `EuOptions` | `ServerOptions.cs`, `Servers/ProductionOptions.cs` |
| Ebb group | `options.Server.Ebb.Us|Eu.BaseUrl` / `.Site` — **not used** by this hero flow (event ingest only) | `sdk-map.md` |

When `Maxio:BaseUrl` **is set**: assign it **verbatim** to `options.Server.Production.Us.BaseUrl` (or `.Eu.BaseUrl` if `Environment` is `Eu`) instead of deriving the host from subdomain. When it is **unset**: leave the default template and set `.Site` from `Maxio:Subdomain`. If a custom BaseUrl still contains `{site}`, also set `.Site`.

Exact option path: `options.Server.{ServerName}.{Environment}.{Param}` e.g. `options.Server.Production.Us.Site`, `options.Server.Production.Us.BaseUrl`.

---

### 4. List products / plans for a family (by handle)

**Preferred:** `client.ProductFamilies.ListProductsForProductFamily` — `productFamilyId` is `string` and XML documents **either numeric id or handle prefixed with `handle:`**. Pass `"handle:" + Maxio:ProductFamilyHandle` (e.g. `"handle:eshop-subscribe"`). Do **not** hard-code the handle. (`operations/ProductFamilies.md`, `Api/ProductFamilies.cs`)

`ReadProductFamily(int id)` notes mention `handle:my-family` at HTTP level, but the generated signature is **`int id`** — the client **cannot** pass a handle there. Do not use it for this flow. (`operations/ProductFamilies.md`)

`ListProductsFilter` has **no** family-handle field (`Ids`, `PrepaidProductPricePoint`, `UseSiteExchangeRate` only) — `records-2-Cr-Ne.md`. Site-wide `Products.ListProducts` is not family-scoped.

| | |
|---|---|
| Controller | `client.ProductFamilies` |
| Method | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| Must-pass (no C# default) | `dateField`, `filter`, `startDate`, `endDate`, `startDatetime`, `endDatetime`, `includeArchived`, `include` — pass `null` to skip |
| HTTP | `GET /product_families/{product_family_id}/products.json` |
| Returns | `IReadOnlyList<ProductResponse>` |
| Envelope | `ProductResponse.Product` (`product`) — **required** `Product` (`records-3-Of-Su.md`) |
| Error | **Case A** `SdkException<ListProductsForProductFamilyError>` · `TryGetString(out string)` **[404]** · `TryGetRawError(out RawError)` fallback |
| Pagination | manual `page`+`perPage` (XML: max `perPage` 200) |
| Map | `operations/ProductFamilies.md` |

Hero call (named args):

```csharp
await client.ProductFamilies.ListProductsForProductFamily(
    productFamilyId: "handle:" + familyHandle,
    dateField: null, filter: null, startDate: null, endDate: null,
    startDatetime: null, endDatetime: null,
    includeArchived: false, include: null,
    page: 1, perPage: 200, ct: ct);
```

Page until a short/empty page. Map each `ProductResponse.Product` (fields below).

---

### 5. Find product by handle (if list-by-family is insufficient)

| | |
|---|---|
| Controller | `client.Products` |
| Method | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` |
| HTTP | `GET /products/handle/{api_handle}.json` |
| Returns | `ProductResponse` → `.Product` |
| Error | **Case B** `SdkException<RawError>` · `StatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()` |
| Map | `operations/Products.md` |

Seeded handles (config, not literals in client construction): `eshop-pro`, `basic-plan`.

`ReadProduct(int productId)` exists but **must not** be used as the primary lookup — numeric IDs are site-specific.

---

### 6. Find customer by reference (idempotent lookup)

| | |
|---|---|
| Controller | `client.Customers` |
| Method | `ReadCustomerByReference(string reference, CancellationToken ct = default)` |
| HTTP | `GET /customers/lookup.json` |
| Query | wire `reference` ← C# `reference` |
| Returns | `CustomerResponse` → `.Customer` (**required**) |
| Error | **Case B** `SdkException<RawError>` (404 = not found — check `ex.Error.StatusCode`) |
| Map | `operations/Customers.md` |

`ListCustomers(..., q, ...)` searches loosely (email/id/org/reference/name). **Do not** use `q` for exact reference match — the lookup endpoint is the exact match. (`operations/Customers.md`)

---

### 7. Create customer

| | |
|---|---|
| Controller | `client.Customers` |
| Method | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` |
| `body` | nullable, **no default → must pass explicitly** |
| HTTP | `POST /customers.json` |
| Returns | `CustomerResponse` → `.Customer` |
| Error | **Case A** `SdkException<CreateCustomerError>` · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` **[422]** · `TryGetRawError(out RawError)` fallback |
| Map | `operations/Customers.md` |

**Request envelope** `CreateCustomerRequest` (`records-1-Ac-Cr.md`): `Customer (customer): CreateCustomer !req`

**`CreateCustomer` fields** (`records-1-Ac-Cr.md` / `Models/CreateCustomer.cs`):

| C# (wire) | Type | Required? |
|---|---|---|
| `FirstName` (`first_name`) | `string` | **required** |
| `LastName` (`last_name`) | `string` | **required** |
| `Email` (`email`) | `string` | **required** |
| `Reference` (`reference`) | `string?` | optional in model; **set it** — unique; one customer per reference (`operations/Customers.md`) |
| `CcEmails`, `Organization`, `Address`, `Address2`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `VatNumber`, `TaxExempt`, `TaxExemptReason`, `ParentId`, `SalesforceId` | optional | skip for hero |

**Idempotency:** `ReadCustomerByReference` first. On 404, `CreateCustomer` with the same `Reference`. On 422 (duplicate reference race), `ReadCustomerByReference` again and use that customer. Notes: “you may only create one customer for a given reference value.” (`operations/Customers.md`)

**422 payload trust:** `CustomerErrorResponse1.Errors` is typed as record `Errors` with only `PerPage (per_page)` / `PricePoint (price_point)` (`records-2-Cr-Ne.md`, `Models/CustomerErrorResponse1.cs`, `Models/Errors.cs`). A separate union `Errors1` (`CustomerError` \| `IReadOnlyList<string>`) exists (`unions.md`) but is **not** wired to `CreateCustomerError`. **UNVERIFIED** whether the live 422 body matches `CustomerErrorResponse1` — extract best-effort from `TryGetCustomerErrorResponse1`; if `Errors` is null/unhelpful, `TryGetRawError` + `ReadAsString()` / `ReadAsJson<T>()`. A 422 body that is a string list can throw `JsonException` **instead of** `SdkException<CreateCustomerError>` while constructing the error object.

**Response `Customer` fields used:** `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?`, `FirstName`/`LastName`. (`records-2-Cr-Ne.md`)

---

### 8. Create subscription (enroll by product handle; no card)

| | |
|---|---|
| Controller | `client.Subscriptions` |
| Method | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` |
| `body` | nullable, **must pass explicitly** |
| HTTP | `POST /subscriptions.json` |
| Returns | `SubscriptionResponse` → `.Subscription` (`subscription`) — **nullable** `Subscription?` (`records-4-Su-We.md`) — null-check |
| Error | **Case A** `SdkException<CreateSubscriptionError>` · `TryGetErrorListResponse1(out ErrorListResponse1)` **[422]** · `TryGetRawError(out RawError)` fallback |
| Map | `operations/Subscriptions.md` |

**Request envelope** `CreateSubscriptionRequest`: `Subscription (subscription): CreateSubscription !req` (`records-2-Cr-Ne.md`)

**Hero `CreateSubscription` fields** (`records-2-Cr-Ne.md`, `Models/CreateSubscription.cs`):

| C# (wire) | Type | Hero usage |
|---|---|---|
| `ProductHandle` (`product_handle`) | `string?` | **Set** — “Required, unless `product_id` is given”. Never send numeric `ProductId`. |
| `ProductId` (`product_id`) | `int?` | **Omit** |
| `CustomerId` (`customer_id`) | `int?` | Set from `Customer.Id` **or** use `CustomerReference` (one of `customer_id` / `customer_reference` / `customer_attributes`) |
| `CustomerReference` (`customer_reference`) | `string?` | Alternative to `CustomerId` — same value as customer `reference` |
| `Reference` (`reference`) | `string?` | App-provided subscription reference (lookup via `FindSubscription`). **Not** an HTTP idempotency-key header — the SDK has none on this operation. |
| `PaymentCollectionMethod` (`payment_collection_method`) | `CollectionMethod?` | **Set — do not omit.** There is no other CreateSubscription member and no HTTP header that means “no payment method”. Live 422 `ErrorListResponse1` `"No payment method was on file for the $299.00 balance"` after omitting this field (even with `Product.RequireCreditCard == false`) means default/automatic collection still tries to capture the balance. **RI (current):** `CollectionMethod.Remittance` (wire `remittance`). **Legacy Statements only:** `CollectionMethod.Invoice` (wire `invoice`). Do **not** send `CollectionMethod.Automatic` (`automatic`) — that is the card-on-file path. Do **not** use `CollectionMethod.Prepaid` (`prepaid`) for no-method enroll. Which architecture this sandbox uses is **UNVERIFIED**; if `Remittance` 422s as invalid, try `Invoice`. (`records-2-Cr-Ne.md`, `enums.md`) |
| `PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes` | — | **Still omit.** No-card enroll is `PaymentCollectionMethod` + no profile/card/bank fields — not a card token. |
| `CustomerAttributes` | `CustomerAttributes?` | **Omit** when customer already exists (do not create a second customer here) |
| `ProductPricePointHandle` / `ProductPricePointId` | — | Omit — default price point |
| `NextBillingAt` (`next_billing_at`) | `DateTimeOffset?` | **Request-only** — omit (do not import/defer) |

**No HTTP idempotency-key** parameter or header on `CreateSubscription` / `CreateCustomer` (`operations/Subscriptions.md`, `operations/Customers.md`). Application idempotency = unique `Customer.Reference` + unique `CreateSubscription.Reference` + `FindSubscription` before POST.

**422 payload:** `ErrorListResponse1.Errors (errors): IReadOnlyList<string> !req` (`records-2-Cr-Ne.md`).

Default subscribe target handle (from catalog/config, not a numeric id): `eshop-pro`. Request body may select `basic-plan`.

---

### 9. Find subscription by reference (idempotent enroll)

| | |
|---|---|
| Controller | `client.Subscriptions` |
| Method | `FindSubscription(string? reference, CancellationToken ct = default)` |
| `reference` | nullable, **must pass explicitly** — pass the app key, not `null` |
| HTTP | `GET /subscriptions/lookup.json` |
| Query | wire `reference` ← C# `reference` |
| Returns | `SubscriptionResponse` |
| Error | **Case A** `SdkException<FindSubscriptionError>` · `TryGetNoContent(out RawError)` **[404]** · `TryGetRawError(out RawError)` fallback |
| Map | `operations/Subscriptions.md` |

Whether the API **rejects** duplicate subscription `reference` values on create is **UNVERIFIED** (unlike customer reference, which the map states is unique). Defensive: always `FindSubscription` before `CreateSubscription`; on 404 create; if a transport-retried POST still created two, `ListCustomerSubscriptions` and prefer the existing row with matching `Reference` / `Product.Handle`.

---

### 10. List subscriptions for a customer

`ListSubscriptions` has **no** `customer_id` / `customer_reference` filter (`operations/Subscriptions.md`). Use:

| | |
|---|---|
| Controller | `client.Customers` |
| Method | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` |
| HTTP | `GET /customers/{customer_id}/subscriptions.json` |
| Returns | `IReadOnlyList<SubscriptionResponse>` |
| Error | **Case B** `SdkException<RawError>` |
| Pagination | none |
| Map | `operations/Customers.md` |

`customerId` is the Maxio numeric `Customer.Id` (non-nullable `int`). Require `Customer.Id` after lookup/create.

---

### 11. Read a single subscription (optional confirmation)

`CreateSubscription` already returns `SubscriptionResponse`. Use `ReadSubscription` only if a later include is needed.

| | |
|---|---|
| Controller | `client.Subscriptions` |
| Method | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` |
| `include` | nullable, **must pass explicitly** (`null` to skip) |
| HTTP | `GET /subscriptions/{subscription_id}.json` |
| Returns | `SubscriptionResponse` |
| Error | **Case B** `SdkException<RawError>` |
| Map | `operations/Subscriptions.md` |

`SubscriptionInclude` members: `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)` (`enums.md`). Hero: pass `include: null`.

---

### Response fields the integration reads

**`Product`** (`records-3-Of-Su.md`) — plans list + nested on subscription:

| C# (wire) | Type | Role |
|---|---|---|
| `Handle` (`handle`) | `string?` | plan id for API/UI |
| `Name` (`name`) | `string?` | display |
| `Description` (`description`) | `string?` | display |
| `PriceInCents` (`price_in_cents`) | `long?` | **price — cents, not dollars** |
| `Interval` (`interval`) | `int?` | period count |
| `IntervalUnit` (`interval_unit`) | `IntervalUnit?` | `Day` / `Month` |
| `ProductFamily` (`product_family`) | `ProductFamily?` | nested family |
| `RequireCreditCard` (`require_credit_card`) | `bool?` | expect `false` on seeded plans |
| `Taxable` (`taxable`) | `bool?` | seeded `no` |

**There is no dollars / `price` field on `Product`.** Display dollars = `PriceInCents / 100m`. Union `PriceInCents` (`string` \| `long`, `unions.md`) is **not** the type of `Product.PriceInCents` (that is `long?`).

**`ProductFamily`:** `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?` (`records-3-Of-Su.md`). Envelope `ProductFamilyResponse.ProductFamily` is **nullable**.

**`Subscription`** (`records-3-Of-Su.md`) — my-subscriptions + create confirmation:

| C# (wire) | Type | Role |
|---|---|---|
| `Id` (`id`) | `int?` | Maxio id |
| `State` (`state`) | `SubscriptionState?` | state |
| `ProductPriceInCents` (`product_price_in_cents`) | `long?` | enrolled price (cents) |
| `CurrentBillingAmountInCents` (`current_billing_amount_in_cents`) | `long?` | current bill (cents) |
| `NextAssessmentAt` (`next_assessment_at`) | `DateTimeOffset?` | next assessment |
| `CurrentPeriodEndsAt` (`current_period_ends_at`) | `DateTimeOffset?` | period end (UpdateSubscription notes: response does **not** echo `next_billing_at`; verify via `current_period_ends_at`) |
| `Product` (`product`) | `Product?` | nested plan (handle/name/price/interval) |
| `Customer` (`customer`) | `Customer?` | nested customer |
| `Reference` (`reference`) | `string?` | app subscription key |
| `PaymentCollectionMethod` (`payment_collection_method`) | `CollectionMethod?` | collection method |
| `SignupRevenue` (`signup_revenue`) | `string?` | string money (not used as plan price) |

**Next billing date:** there is **no** `NextBillingAt` on the **response** `Subscription` (that name is request-only on `CreateSubscription` / `UpdateSubscription`). Present **`NextAssessmentAt`** to the user when non-null, else **`CurrentPeriodEndsAt`**. Which of the two the live wire treats as “next billing date” is **UNVERIFIED** — both exist on the generated model; do not invent a third field.

---

### Enums in scope (`MaxioAdvancedBilling.Models.Enums` — `map/models/enums.md`)

Construct with static members or `Type.FromValue("wire")`. **Not** C# enums.

**`SubscriptionState`** (`Models/Enums/SubscriptionState.cs`):

| Member | Wire |
|---|---|
| `Pending` | `pending` |
| `FailedToCreate` | `failed_to_create` |
| `Trialing` | `trialing` |
| `Assessing` | `assessing` |
| `Active` | `active` |
| `SoftFailure` | `soft_failure` |
| `PastDue` | `past_due` |
| `Suspended` | `suspended` |
| `Canceled` | `canceled` |
| `Expired` | `expired` |
| `Paused` | `paused` |
| `Unpaid` | `unpaid` |
| `TrialEnded` | `trial_ended` |
| `OnHold` | `on_hold` |
| `AwaitingSignup` | `awaiting_signup` |

**`IntervalUnit`:** `Day (day)`, `Month (month)`.

**`CollectionMethod`** (`MaxioAdvancedBilling.Models.Enums.CollectionMethod`, `enums.md` / `Models/Enums/CollectionMethod.cs`): `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)`. Enum summary: legacy Statements valid = `invoice`, `automatic`; Relationship Invoicing valid = `remittance`, `automatic`, `prepaid`. **No-card enroll:** set `CreateSubscription.PaymentCollectionMethod = CollectionMethod.Remittance` (RI) or `CollectionMethod.Invoice` (legacy). `Automatic` requires a payment method on file.

**`BasicDateField`:** `UpdatedAt (updated_at)`, `CreatedAt (created_at)`.

**`ListProductsInclude`:** `PrepaidProductPricePoint (prepaid_product_price_point)`.

**`SubscriptionInclude`:** `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)`.

---

### Unions in scope

Hero request/response money on `Product` / `Subscription` is `long?` cents — **no union**. Nearby (do not use unless a touched field is typed as one):

| Union | Factories / TryGet | Source |
|---|---|---|
| `PriceInCents` | `PriceInCents.String(string)`, `PriceInCents.Long(long)` · `TryGetString`, `TryGetLong` | `unions.md` — **not** `Product.PriceInCents` |
| `Errors1` | `Errors1.CustomerError(CustomerError)`, `Errors1.ListOfString(...)` · `TryGetCustomerError`, `TryGetListOfString` | `unions.md` — **not** wired to `CreateCustomerError` |

---

### Idempotency strategy (POST / transport retries)

The SDK exposes **no** idempotency-key header or parameter on create operations (`operations/Customers.md`, `operations/Subscriptions.md`).

Application keys:

1. **Customer:** `CreateCustomer.Reference` = eShop user id; lookup `ReadCustomerByReference`; unique per map notes.
2. **Subscription:** `CreateSubscription.Reference` = `{userId}:{productHandle}`; lookup `FindSubscription` before POST.

Double-click of the same plan must hit `FindSubscription` (or `ListCustomerSubscriptions` + match handle) and return the existing envelope without a second `CreateSubscription`.

---

### Case B `RawError` accessors (every Case B row)

`StatusCode: HttpStatusCode` · `ReadAsBytes(): ReadOnlyMemory<byte>` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` (`sdk-map.md`). Inherited on every Case A type: `TryGetRawError(out RawError)`.

---

## Trap notes

⚠ Step 1 (DI) — the `HttpClient`/handler pipeline lifetime is not the same problem as the SDK wrapper’s lifetime; registering a second client or a per-request `HttpClient` fights the extension’s ownership. **MUST load `dotnet-client-initialization`** before `AddMaxioAdvancedBillingClient` / `new MaxioAdvancedBillingClient`.

⚠ Step 1 (auth) — 401/403 and “which property holds the API key” are configuration failures, not catalog bugs; credentials belong on options before the first call. **MUST load `dotnet-authentication`**.

⚠ Step 1 (server) — `Timeout` / retry options on `RetryOptions` do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; status-code retry method lists are not the only retry trigger, so a **POST write can still execute more than once**. **MUST load `dotnet-configuration-resilience`** before wiring `options.Retry` or assuming creates are naturally idempotent.

⚠ Steps 2–4 (calls) — optional parameters with no C# default (`dateField`…`include`, `body`, `reference`, `include` on `ReadSubscription`) mis-bind if passed positionally. **MUST load `dotnet-calling-endpoints`**. Named arguments; cancellation is `ct:`.

⚠ Steps 2–4 (models) — envelopes wrap one field (`ProductResponse.Product`, `CustomerResponse.Customer`, `SubscriptionResponse.Subscription`); `Subscription` on the envelope is nullable; enums are `StringEnum<T>` (`SubscriptionState.Active`, not a C# enum). **MUST load `dotnet-models`**.

⚠ Step 5 (errors) — Case A vs Case B differs **per operation** (creates are A; most lookups/lists here are B). `TryGetRawError` is not a catch-all on the wrong `SdkException<T>`. **MUST load `dotnet-error-handling`**.

⚠ Step 5 (errors) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`**.

⚠ Step 5 (errors) — a **non-2xx** body that does not match its operation’s generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. This is especially relevant to `CreateCustomer` 422 vs `CustomerErrorResponse1`/`Errors`. **MUST load `dotnet-error-handling`**.

⚠ Tests — the `HttpClient` constructor argument is the test seam; do not mock controller method internals. **MUST load `dotnet-testing`**.

---

## REQUIRED READING

Load **before implementation starts**. This sheet does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — ctor, DI extension, HttpClient ownership / singleton wrapper |
| `dotnet-authentication` | Step 1 — `BasicAuth` / `BasicAuthCredentials` |
| `dotnet-configuration-resilience` | Step 1 — retries (including transport retries on POST), timeouts, BaseUrl/server selection, list pagination |
| `dotnet-calling-endpoints` | Steps 2–4 — named args, must-pass nulls, `ct:`, throw-only operations |
| `dotnet-models` | Steps 2–4 — envelopes, `required`/`init`, `StringEnum<T>`, cents vs dollars |
| `dotnet-error-handling` | Step 5 — Case A/B, `TryGet…`, `RawError`, **both** `JsonException` directions |
| `dotnet-testing` | Tests for the PublicApi integration layer |

---

## Assumptions & Blockers

**Assumptions**

- `Customer.Reference` = eShop authenticated user id (string). Same value for `ReadCustomerByReference` / `CreateCustomer.Reference` / optional `CreateSubscription.CustomerReference`.
- `CreateCustomer.FirstName` / `LastName` / `Email` come from the eShop Identity user. If first/last are empty, the app still must supply non-empty strings (SDK `required`) — split email/username or use a site-defined placeholder; that split is an app decision.
- `CreateSubscription.Reference` = `{userId}:{productHandle}` so a double-click of the **same** plan is idempotent via `FindSubscription`. A **different** plan for the same user is a second subscription (hero does not migrate products).
- `GET /api/my-subscriptions` does **not** create a Maxio customer; no customer → empty list.
- `POST /api/subscriptions` product handle comes from the request body; if omitted, default handle `eshop-pro` from catalog/config (not a numeric id).
- Target hosting is US (`ServerEnvironment.Us`). Optional `Maxio:BaseUrl` overrides Production US (or EU if Environment is set to Eu) verbatim.
- Seeded plans have `require_credit_card = false`, but that flag is **not** sufficient: `CreateSubscription` must set `PaymentCollectionMethod` to `CollectionMethod.Remittance` (wire `remittance`; RI) or `CollectionMethod.Invoice` (wire `invoice`; legacy Statements). Card/profile/bank fields stay omitted. Live 422 `"No payment method was on file for the $299.00 balance"` was caused by omitting `PaymentCollectionMethod`. Architecture of this sandbox (RI vs Statements) is **UNVERIFIED** — prefer `Remittance` first.
- JWT user on `src/PublicApi` is the eShop shopper whose id keys Maxio `reference`.

**Blockers**

- None that block the hero flow: list-by-family-handle, customer lookup/create, subscribe-by-`product_handle`, list-by-customer-id, and read-by-id are all in the map.
- **Not in the SDK map (do not invent):** HTTP idempotency-key header; dollars price field on `Product`/`Subscription`; `ServerEnvironment.Sandbox`; `ListSubscriptions` filter by customer reference; `ReadProductFamily` by handle in C# (`int id` only). Workarounds above stay inside mapped operations (`handle:` on `ListProductsForProductFamily`, cents fields, `Us`, `ListCustomerSubscriptions`, `ReadProductByHandle`).
- Metered component `api-call` is seeded and **out of scope**; no component operations are planned.
