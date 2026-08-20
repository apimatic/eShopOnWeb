# Maxio Advanced Billing — eShopOnWeb recurring subscribe

Package `AsadAli.AdvancedBilling.Sdk` · root namespace `MaxioAdvancedBilling` · map stamp `v1.0.2` / `15db14b`. Additive PublicApi capability: JWT `GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions`. Metered component `api-call` is **out of scope** (not required to list plans or enroll).

## Scope & sequence

1. **Config + client** — bind `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` (env: `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_ENVIRONMENT`, `MAXIO_DEFAULT_PRODUCT_FAMILY`). Construct `MaxioAdvancedBillingClient` for the sandbox site. No secrets in the repo.
2. **GET /api/subscription-plans** — `ProductFamilies.ListProductsForProductFamily` with family handle `eshop-subscribe` (or configured `ProductFamilyHandle`). Map handle, name, price, interval. Never hard-code numeric product/family IDs.
3. **Ensure Maxio customer** (idempotent) — `Customers.ReadCustomerByReference` then, on miss, `Customers.CreateCustomer` with `CreateCustomer.Reference` = eShopOnWeb user id. Recover races via re-lookup. Email is **not** the uniqueness key.
4. **POST /api/subscriptions** — enroll by **product handle** (`eshop-pro` default; `basic-plan` allowed). `Subscriptions.CreateSubscription` with `CreateSubscription.ProductHandle` + existing customer + **`PaymentCollectionMethod = CollectionMethod.Remittance`**. Omit all payment-profile/card fields. Application-level idempotency: `FindSubscription` by app reference + `Customers.ListCustomerSubscriptions` (API has **no** unique constraint on customer+product).
5. **GET /api/my-subscriptions** — `Customers.ListCustomerSubscriptions`. Return plan/price/state/next-billing-date from the subscription envelope.
6. **Error boundary** around every SDK call (throw-only SDK; Case A vs B per row below).
7. **Tests** at the `HttpClient` constructor seam.

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

### Client construction / auth / server node

| Fact | Exact type / member | Cite |
|---|---|---|
| Client | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` | `sdk-map.md`, `MaxioAdvancedBillingClient.cs` |
| Only ctor | `MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` |
| Options | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` — `Environment` (`MaxioAdvancedBilling.Servers.ServerEnvironment`, default `ServerEnvironment.Default()` → `Us`), `Retry` (`MaxioAdvancedBilling.Core.Configuration.RetryOptions`, default `RetryOptions.Default()`), `Server` (`MaxioAdvancedBilling.ServerOptions`, default `new()`), `BasicAuth` (`MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?`) | `sdk-map.md`, `MaxioAdvancedBillingClientOptions.cs` |
| Basic auth | `new BasicAuthCredentials { Username = apiKey, Password = "x" }` — both members `required string`. Username = API key from `Maxio:ApiKey`; password is the literal `"x"`. | `sdk-map.md`, `BasicAuthCredentials.cs` |
| DI helper | `MaxioAdvancedBilling.ServiceCollectionExtensions.AddMaxioAdvancedBillingClient(this IServiceCollection, Action<MaxioAdvancedBillingClientOptions>? configure = null)` — builds options, `AddHttpClient()`, registers the client. | `ServiceCollectionExtensions.cs` |
| Hosting env (not sandbox) | `ServerEnvironment.Us` wire `US`; `ServerEnvironment.Eu` wire `EU`. **No sandbox/production enum.** Sandbox is the **site subdomain**. Bind `MAXIO_ENVIRONMENT` to Us/Eu (`TryGetKnownValue` / compare `"EU"`); default `Us` for this sandbox. | `sdk-map.md`, `Servers/ServerEnvironment.cs` |
| Subdomain | `options.Server.Production.Us.Site = subdomain` when `Environment` is `Us`; `.Eu.Site` when `Eu`. Nested types: `MaxioAdvancedBilling.Servers.ProductionOptions` → `UsOptions` / `EuOptions`, each `string BaseUrl` + `string Site` (Site default `"subdomain"`). `ServerOptions.Production` / `.Ebb`. | `sdk-map.md`, `ServerOptions.cs`, `Servers/ProductionOptions.cs` |
| Optional `Maxio:BaseUrl` | When set, assign **verbatim** to `options.Server.Production.Us.BaseUrl` (or `.Eu.BaseUrl` if Eu) — a complete origin (scheme+host[+port]), **not** derived from subdomain. Default Us template is `https://{site}.chargify.com`. Do not leave `{site}` in an override unless `Site` is also set to expand it. | `sdk-map.md` |
| Retry options | `RetryOptions` (namespace `MaxioAdvancedBilling.Core.Configuration`): all members `required` — `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry`. Start from `RetryOptions.Default()` if you touch retries. | `sdk-map.md` |

Manual construct: `new MaxioAdvancedBillingClient(httpClient, options)` with `options.BasicAuth`, `options.Environment`, `options.Server.Production.Us.Site` (and `.BaseUrl` when configured).

### Operations

#### 1. List plans for family handle — `GET /api/subscription-plans`

| | |
|---|---|
| Controller | `client.ProductFamilies` (`MaxioAdvancedBilling.Api.ProductFamilies`) |
| Method | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| Must-pass | `dateField` … `include` (8 params) have **no C# default** — pass `null` (or `false` for `includeArchived`) explicitly. Named arguments. |
| `productFamilyId` | **Handle form:** `"handle:" + ProductFamilyHandle` e.g. `"handle:eshop-subscribe"`. XML: “Either the product family's id or its handle prefixed with `handle:`”. Never hard-code a numeric family id. |
| HTTP | `GET /product_families/{product_family_id}/products.json` |
| Returns | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>` |
| Envelope | `ProductResponse.Product` (`product`) **required** `Product` |
| Pagination | manual `page` + `perPage`; default 20; **max 200** (values over 200 become 200). Two seeded plans → `perPage: 200`, `page: 1` is enough; still loop while a page is full. |
| Error | **Case A** `SdkException<ListProductsForProductFamilyError>` (`MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError`) — `TryGetString(out string)` **[404]** · `TryGetRawError(out RawError)` fallback |
| Cite | `operations/ProductFamilies.md`, `Api/ProductFamilies.cs`, `records-3-Of-Su.md` |

**Do not use** `ReadProductFamily(int id, …)` to resolve a handle — C# `id` is `int` even though HTTP remarks mention `handle:my-family`. **Do not use** `ListProducts` as the primary path: `ListProductsFilter` has only `Ids`, `PrepaidProductPricePoint`, `UseSiteExchangeRate` — **no family-handle filter**. Fallback if `handle:` 404s: `ListProductFamilies` (no pagination) and match `ProductFamily.Handle`, then recall list with the numeric id as string — still never bake that id into source.

Optional single-plan read: `client.Products.ReadProductByHandle(string apiHandle, CancellationToken ct = default)` → `ProductResponse`, **Case B** `SdkException<RawError>`. Cite: `operations/Products.md`.

**Product fields to map** (`MaxioAdvancedBilling.Models.Product`, `records-3-Of-Su.md`):

| C# (wire) | Type | Use |
|---|---|---|
| `Handle (handle)` | `string?` | plan id in PublicApi (`eshop-pro`, `basic-plan`) |
| `Name (name)` | `string?` | display name |
| `PriceInCents (price_in_cents)` | `long?` | price (cents; $299.00 → 29900 from API, do not hard-code) |
| `Interval (interval)` | `int?` | e.g. `1` |
| `IntervalUnit (interval_unit)` | `IntervalUnit?` | see enums |
| `ProductFamily (product_family)` | `ProductFamily?` | confirm `ProductFamily.Handle` == configured family |
| `ProductPricePointHandle (product_price_point_handle)` | `string?` | default price point handle (omit on subscribe → product default) |
| `RequireCreditCard (require_credit_card)` | `bool?` | seeded plans: payment not required |
| `Taxable (taxable)` | `bool?` | seeded `no` |
| `ArchivedAt (archived_at)` | `DateTimeOffset?` | skip archived |

`ProductFamily`: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, … (`records-3-Of-Su.md`).

`ListProductsFilter` (`records-2-Cr-Ne.md`): `Ids (ids): IReadOnlyList<int>?`, `PrepaidProductPricePoint (prepaid_product_price_point): PrepaidProductPricePointFilter?`, `UseSiteExchangeRate (use_site_exchange_rate): bool?` — unused for handle listing.

#### 2. Find or create customer — idempotent

**Stable idempotency key: `CreateCustomer.Reference` / lookup `reference` = eShopOnWeb user id string.** Email is searchable but **not** unique. Do not use `ListCustomers` `q` for exact match (fuzzy).

| | **ReadCustomerByReference** | **CreateCustomer** |
|---|---|---|
| Controller | `client.Customers` | `client.Customers` |
| HTTP | `GET /customers/lookup.json?reference=` | `POST /customers.json` |
| Signature | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable **no default → must pass explicitly** (pass a request, not `null`) |
| Returns | `CustomerResponse` | `CustomerResponse` |
| Envelope | `CustomerResponse.Customer` (`customer`) **required** `Customer` | same |
| Error | **Case B** `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | **Case A** `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` **[422]** · `TryGetRawError(out RawError)` fallback |
| Cite | `operations/Customers.md` | `operations/Customers.md`, `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md` |

**Request** `CreateCustomerRequest` (`Models/CreateCustomerRequest.cs`): `Customer (customer): CreateCustomer !req`.

**`CreateCustomer` required members** (`records-1-Ac-Cr.md` / `CreateCustomer.cs`):

| C# (wire) | Type | Required? |
|---|---|---|
| `FirstName (first_name)` | `string` | **required** |
| `LastName (last_name)` | `string` | **required** |
| `Email (email)` | `string` | **required** |
| `Reference (reference)` | `string?` | optional in C#; **MUST set** to eShop user id for uniqueness |
| `CcEmails`, `Organization`, `Address`, `Address2`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `VatNumber`, `TaxExempt`, `TaxExemptReason`, `ParentId`, `SalesforceId` | optional | omit |

API: “you may only create one customer for a given reference value. If provided, the `reference` value must be unique.” Duplicate reference → HTTP 422.

**`Customer` fields to keep** (`records-2-Cr-Ne.md`): `Id (id): int?` (pass into subscribe), `Reference (reference): string?`, `Email (email): string?`, `FirstName` / `LastName`.

**Duplicate / double-click algorithm**

1. `ReadCustomerByReference(reference: eshopUserId, ct:)`.
2. Success → use `Customer.Id`.
3. `SdkException<RawError>` and `ex.Error.StatusCode == HttpStatusCode.NotFound` → `CreateCustomer`. (Exact miss status is **UNVERIFIED** if not 404; treat any miss that is not a 2xx as “try create”, then re-lookup.)
4. On `SdkException<CreateCustomerError>` 422 **or** `JsonException` during error mapping: `ReadCustomerByReference` again; if found, treat as success (lost race). If still missing, fail.

**422 payload trap (generated-model mismatch):** `CustomerErrorResponse1.Errors` is typed as record `MaxioAdvancedBilling.Models.Errors` with only `PerPage (per_page)` and `PricePoint (price_point)` — **not** the customer-duplicate message. A sibling union `Errors1` (`CustomerError` \| `IReadOnlyList<string>`, accessors `TryGetCustomerError` / `TryGetListOfString`) exists but is **not** what `CreateCustomerError` deserializes. `CustomerError.Customer (customer): string?` exists unused by this operation. **Do not** decide “duplicate” by reading `Errors.PerPage` / `PricePoint`. Recover by **re-lookup**. If the 422 body is a JSON array, deserialization of `CustomerErrorResponse1` can throw `JsonException` **instead of** `SdkException<CreateCustomerError>` — catch both. **UNVERIFIED:** live wire wording of the duplicate message. Cite: `records-2-Cr-Ne.md`, `unions.md`, `CustomerErrorResponse1.cs`, `Errors.cs`, `CreateCustomerError.cs`.

#### 3. Create subscription by product handle — `POST /api/subscriptions`

| | |
|---|---|
| Controller | `client.Subscriptions` |
| Method | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly |
| HTTP | `POST /subscriptions.json` |
| Returns | `SubscriptionResponse` |
| Envelope | `SubscriptionResponse.Subscription` (`subscription`) **nullable** `Subscription?` — null-check before read |
| Error | **Case A** `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` **[422]** · `TryGetRawError(out RawError)` fallback |
| Cite | `operations/Subscriptions.md`, `records-2-Cr-Ne.md`, `records-3-Of-Su.md`, `records-4-Su-We.md` |

**Request** `CreateSubscriptionRequest`: `Subscription (subscription): CreateSubscription !req`.

**`CreateSubscription` — send these; omit the rest** (`CreateSubscription.cs`, `enums.md`):

Live 422 `{"errors":["No payment method was on file for the $299.00 balance"]}` is `ErrorListResponse1` via `TryGetErrorListResponse1`. Omitting `PaymentCollectionMethod` left the site default (`Site.DefaultPaymentCollectionMethod`, `records-3-Of-Su.md`) — which assessed the $299 opening balance under **automatic** collection and required a payment profile. Product `RequireCreditCard` does **not** set collection method; there is no CreateSubscription flag named “payment not required.”

| C# (wire) | Type | Rule |
|---|---|---|
| `ProductHandle (product_handle)` | `string?` | **Set.** XML: required unless `product_id`. Use `eshop-pro` (default) or `basic-plan`. Never send `ProductId`. |
| `CustomerId (customer_id)` | `int?` | **Set** from ensure-customer. XML: required unless `customer_reference` or `customer_attributes`. |
| `CustomerReference (customer_reference)` | `string?` | Allowed instead of / in addition to id; prefer `CustomerId` after lookup. |
| `Reference (reference)` | `string?` | **Set** to app key `{eshopUserId}:{productHandle}` for lookup via `FindSubscription`. |
| `PaymentCollectionMethod (payment_collection_method)` | `CollectionMethod?` | **Set** `CollectionMethod.Remittance` (wire `remittance`). RI-valid non-automatic methods are `remittance`, `automatic`, `prepaid` (`enums.md` / `CollectionMethod.cs`). Do **not** send `CollectionMethod.Automatic`. `CollectionMethod.Invoice` (wire `invoice`) is Statements-architecture only — use only if Remittance 422s as an invalid method. `CollectionMethod.Prepaid` needs prepaid config — do not send. |
| `ProductPricePointHandle` / `ProductPricePointId` | | **Omit** → product default price point |
| `PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes`, `AgreementAcceptance`, `AchAgreement` | | **Omit** (no card / no 3-DS) |
| `Components` | | **Omit** (metered `api-call` not part of hero enroll) |
| `NextBillingAt`, `InitialBillingAt`, `DeferSignup` | | **Omit.** `NextBillingAt` in the future skips trial/initial charges (import semantics, not no-card enroll). `DeferSignup = true` creates Awaiting Signup Date (unknown first billing). `DeferSignup` default is `false`. |
| `NetTerms` | `string?` | **Omit** (optional invoice due-days; not required to enroll) |
| `CustomerAttributes` | | **Omit** — customer already exists |

C# has **no** `required` members on `CreateSubscription`; business-required: `ProductHandle` + (`CustomerId` \| `CustomerReference`) + `PaymentCollectionMethod = CollectionMethod.Remittance`.

Not a SDK-capability blocker: the no-card lever **is** `PaymentCollectionMethod`. If Remittance still 422s with the same “No payment method was on file…” string, that is a **site/product config** blocker (opening balance still collected as automatic) — the SDK has no other member that means “assess $299 with no payment profile.” **UNVERIFIED** until that live retry.

**Response fields to return to the shopper** (`Subscription`, `records-3-Of-Su.md`):

| C# (wire) | Type | Maps to |
|---|---|---|
| `Id (id)` | `int?` | subscription id |
| `State (state)` | `SubscriptionState?` | state |
| `ProductPriceInCents (product_price_in_cents)` | `long?` | price |
| `Product (product)` | `Product?` | plan: `Product.Handle`, `Product.Name`, `Product.PriceInCents`, `Product.Interval`, `Product.IntervalUnit` |
| `NextAssessmentAt (next_assessment_at)` | `DateTimeOffset?` | **next billing date** (there is **no** `NextBillingAt` on the response model) |
| `CurrentPeriodEndsAt (current_period_ends_at)` | `DateTimeOffset?` | period end (Update notes: response does not echo `next_billing_at`; verify via `current_period_ends_at`) |
| `Reference (reference)` | `string?` | app idempotency key |
| `Customer (customer)` | `Customer?` | nested customer |
| `ProductPricePointType (product_price_point_type)` | `PricePointType?` | default/catalog/custom |
| `CurrentBillingAmountInCents (current_billing_amount_in_cents)` | `long?` | optional extra |

**Idempotency (double-click subscribe)** — the SDK/API does **not** document a unique constraint on (customer, product). `ListSubscriptions` has **no** customer id/reference filter. Native “already subscribed” is therefore **application-level**:

1. `subscriptionReference = $"{eshopUserId}:{productHandle}"`.
2. `FindSubscription(reference: subscriptionReference, ct:)` — Case A `SdkException<FindSubscriptionError>`: `TryGetNoContent(out RawError)` **[404]** · `TryGetRawError` fallback. Hit → return existing.
3. On 404: `ListCustomerSubscriptions(customerId)` and if any subscription has `Product.Handle == productHandle` and `State` not end-of-life (`Canceled`, `Expired`, `FailedToCreate`, `TrialEnded`), return that (covers enrolls without our reference).
4. Else `CreateSubscription` as above.
5. On 422 or `JsonException`: repeat 2–3. If still none, map `ErrorListResponse1.Errors` (`IReadOnlyList<string> !req`, wire `errors`) to the PublicApi error. **UNVERIFIED:** whether duplicate `Reference` 422s; recover by lookup regardless of message text. **UNVERIFIED:** whether Maxio allows two live subs to the same product for one customer if step 3 is skipped.

`FindSubscription(string? reference, CancellationToken ct = default)` — `reference` nullable **must pass explicitly**. Cite: `operations/Subscriptions.md`, `FindSubscriptionError.cs`.

#### 4. List my subscriptions — `GET /api/my-subscriptions`

| | |
|---|---|
| Controller | `client.Customers` |
| Method | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` |
| HTTP | `GET /customers/{customer_id}/subscriptions.json` |
| Returns | `IReadOnlyList<SubscriptionResponse>` |
| Envelope | each item `.Subscription` (`Subscription?`) |
| Pagination | **none** (full list) |
| Filter | customer **numeric** id only (from ensure-customer). No handle/reference filter on this op. |
| Error | **Case B** `SdkException<RawError>` |
| Cite | `operations/Customers.md` |

Do **not** use `ListSubscriptions` for this endpoint: filters are `state`, `product` (int id), `productPricePointId`, coupons, dates, metadata — **not** customer id/reference (`operations/Subscriptions.md`).

Optional one-by-id: `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` must be passed (`null` to skip). Case B. Not required for the list endpoint.

Map the same shopper fields as create (plan/price/state/`NextAssessmentAt`).

### Error types that actually reach catch blocks

Every operation is **throw-only** (no `…Result` / `ApiResult` variants). `MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>` exposes **only** `required TError Error` — **no** `StatusCode` on the exception. Namespace `MaxioAdvancedBilling.Core.Exceptions`. Cite: `sdk-map.md`, `SdkException.cs`.

| `TError` | Status / body |
|---|---|
| Case A `ApiError` (`MaxioAdvancedBilling.Core.ErrorResponse.ApiError`) | HTTP status = which `TryGet…` matched. Fallback: `TryGetRawError(out RawError)` then `raw.StatusCode` + `ReadAsString()`. |
| Case B `RawError` | `StatusCode: HttpStatusCode` · `ReadAsString()` · `ReadAsJson<T>()` · `ReadAsBytes()` |

| Operation | Catch type | Accessors |
|---|---|---|
| `ListProductsForProductFamily` | `SdkException<ListProductsForProductFamilyError>` | `TryGetString` [404], `TryGetRawError` |
| `ReadProductByHandle` | `SdkException<RawError>` | `StatusCode` / `ReadAsString` |
| `ListProductFamilies` | `SdkException<RawError>` | same |
| `ReadCustomerByReference` | `SdkException<RawError>` | same |
| `CreateCustomer` | `SdkException<CreateCustomerError>` | `TryGetCustomerErrorResponse1` [422], `TryGetRawError` |
| `ListCustomerSubscriptions` | `SdkException<RawError>` | Case B |
| `CreateSubscription` | `SdkException<CreateSubscriptionError>` | `TryGetErrorListResponse1` [422] (`ErrorListResponse1.Errors: IReadOnlyList<string> !req`), `TryGetRawError` |
| `FindSubscription` | `SdkException<FindSubscriptionError>` | `TryGetNoContent(out RawError)` [404], `TryGetRawError` |
| `ReadSubscription` | `SdkException<RawError>` | Case B |
| `ListSubscriptions` | `SdkException<RawError>` | Case B (not used for my-subscriptions) |

Also catch `System.Text.Json.JsonException` at the same boundary (see REQUIRED READING hazard rows). Do not parse `Exception.ToString()` when an accessor exists. Do not catch `SdkException<RawError>` for Case A operations expecting it to wrap 422 — those throw `SdkException<{Op}Error>`.

`SdkException<T>` is `sealed` and inherits `Exception`; `Message` is the base default — **not** the provider body.

### Enums to serialize on PublicApi responses

All `MaxioAdvancedBilling.Models.Enums.*` except `ServerEnvironment` (`MaxioAdvancedBilling.Servers`). These are `StringEnum<T>` records, **not** C# enums. Static members below; wire value in parens. Read wire via `.Value` (`TypedEnum.Value`). Construct with the static member or `T.FromValue("wire")` where that factory exists (`SubscriptionState`, `IntervalUnit`, `PricePointType` do; `ServerEnvironment` has **no** public `FromValue` — use `Us` / `Eu` / `TryGetKnownValue`).

**`SubscriptionState`** (`enums.md`, `Models/Enums/SubscriptionState.cs`) — serialize `State.Value`:

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

No-card enroll **UNVERIFIED** exact landing state; return `State.Value` as received (expect `active` or `awaiting_signup` given payment-not-required). End-of-life for reuse check: `canceled`, `expired`, `failed_to_create`, `trial_ended`.

**`IntervalUnit`:** `Day (day)`, `Month (month)`.

**`PricePointType`:** `Catalog (catalog)`, `Default (default)`, `Custom (custom)`.

**`ExpirationIntervalUnit`** (product “expires never”): `Day (day)`, `Month (month)`, `Never (never)`.

**`CollectionMethod`** (`MaxioAdvancedBilling.Models.Enums.CollectionMethod`) — **set on create** to `Remittance`; also appears on `Subscription.PaymentCollectionMethod`: `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)`. RI: `remittance` \| `automatic` \| `prepaid`. Statements: `invoice` \| `automatic`.

**`ListProductsInclude`:** `PrepaidProductPricePoint (prepaid_product_price_point)` — pass `null` on list.

**`BasicDateField`:** `UpdatedAt (updated_at)`, `CreatedAt (created_at)` — pass `null` on list.

**`SubscriptionStateFilter`** (only if using `ListSubscriptions`, which we do not): `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)`.

**`ServerEnvironment`:** `Us (US)`, `Eu (EU)`.

---

## Trap notes

⚠ Step 1 (client registration) — the SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register. **MUST load `dotnet-configuration-resilience`** before wiring the client.

⚠ Step 1 (client registration) — `HttpClient` ownership/lifetime vs the SDK wrapper is not visible from the constructor. **MUST load `dotnet-client-initialization`** before `new MaxioAdvancedBillingClient` or `AddMaxioAdvancedBillingClient`.

⚠ Step 1 (auth) — credentials must be applied on options from configuration (`Maxio:ApiKey`), never literals in source; 401/403 follow the Basic username/password shape. **MUST load `dotnet-authentication`** before setting `BasicAuth`.

⚠ Step 1 / 4 (writes) — whether a failed `CreateCustomer` / `CreateSubscription` can be re-sent (transport vs status retry) determines whether double-click *and* automatic retry both require the lookup-before/after algorithm above. **MUST load `dotnet-configuration-resilience`** before tuning `Retry` or assuming POST safety.

⚠ Steps 2–5 (calls) — list/find signatures have long nullable must-pass parameter lists; a positional call mis-binds. Cancellation token is `ct:`. **MUST load `dotnet-calling-endpoints`** before the first `client.*` call.

⚠ Steps 2–5 (models) — `StringEnum<T>` is not a C# enum (do not `switch` as `enum`); `CreateCustomer` has `required` members; response envelopes wrap one field; unmodeled JSON (including customer-duplicate `errors.customer`) is dropped. **MUST load `dotnet-models`** before building payloads or mapping `Subscription`/`Product`.

⚠ Step 6 (error boundary) — Case A vs Case B differ per operation (table above); `TryGetRawError` is not a catch-all on the wrong exception type; there are no no-throw variants. **MUST load `dotnet-error-handling`** before any `try/catch`.

⚠ Step 6 — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 6 — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 7 (tests) — the `HttpClient` constructor argument is the test seam; do not fake SDK internals. **MUST load `dotnet-testing`** before writing tests.

---

## REQUIRED READING

Load **before implementation starts**. This sheet does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — ctor / `AddMaxioAdvancedBillingClient` / `HttpClient` lifetime |
| `dotnet-authentication` | Step 1 — `BasicAuthCredentials`, config-sourced API key |
| `dotnet-calling-endpoints` | Steps 2–5 — named args, `ct:`, throw-only calls |
| `dotnet-models` | Steps 2–5 — records, `required`, `StringEnum<T>`, envelopes, dropped JSON |
| `dotnet-error-handling` | Step 6 — Case A/B, `TryGet…`, `JsonException` from 2xx **and** from failed error-object construction |
| `dotnet-configuration-resilience` | Step 1 — retries, timeouts, base URL, pagination |
| `dotnet-testing` | Step 7 — `HttpClient` seam |

---

## Assumptions & Blockers

**Assumptions**

- `MAXIO_ENVIRONMENT` selects SDK hosting `ServerEnvironment.Us` / `Eu` (wire `US`/`EU`), not sandbox vs live. Sandbox targeting is `Maxio:Subdomain` (seeded site `cp-exp-1`).
- Customer `Reference` = stable eShopOnWeb user id string; `FirstName` / `LastName` / `Email` come from the logged-in shopper.
- `POST /api/subscriptions` accepts a product handle and defaults to `eshop-pro` when omitted.
- Next billing date for PublicApi = `Subscription.NextAssessmentAt` (fallback display `CurrentPeriodEndsAt` if assessment is null).
- Application idempotency key `{userId}:{productHandle}` on `CreateSubscription.Reference`; reuse an existing non-end-of-life subscription for the same product handle.
- Hero flow does not allocate or report usage on `api-call`.
- `Maxio:BaseUrl` when present is an origin only (SDK appends `/products.json` etc.).
- Sandbox site uses Relationship Invoicing, so no-card enroll is `CollectionMethod.Remittance` (`remittance`), not `Invoice`. **UNVERIFIED** until the remittance retry; if 422 says invalid collection method, send `CollectionMethod.Invoice` instead.

**Blockers**

- None from the SDK surface: set `CreateSubscription.PaymentCollectionMethod = CollectionMethod.Remittance`. If that live call still returns “No payment method was on file for the $299.00 balance”, the site is still collecting the opening balance as automatic — that is a **sandbox product/site config** blocker, not a missing SDK field.
- **Missing native capability (workaround specified):** no SDK operation rejects or upserts “this customer already has this product.” Idempotency is application-level (`FindSubscription` + `ListCustomerSubscriptions`). Not a ship blocker if that algorithm is implemented.
- **`ReadProductFamily` cannot take a handle** (`int id` only). Listing uses `ListProductsForProductFamily(string productFamilyId)` with `handle:{handle}`.
- **`CustomerErrorResponse1.Errors` does not model customer-duplicate text.** Recovery is re-lookup, not parsing that record. Live 422 body shape **UNVERIFIED**.
