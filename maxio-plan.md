# Maxio Advanced Billing — eShopOnWeb recurring subscribe

Additive parallel billing (does not replace Catalog → Basket → Order). Hero flow: JWT shopper lists plans, subscribe (ensure customer + enroll), then list own subscriptions. Metered usage reporting for `api-call` is **out of scope**.

**Package:** `AsadAli.AdvancedBilling.Sdk` · **Root namespace:** `MaxioAdvancedBilling` · map stamp `v1.0.2` (`sdk-map.md`)

---

## Scope & sequence

1. **Client + config** — bind `Maxio:ApiKey` / `Maxio:Subdomain` / `Maxio:ProductFamilyHandle` / optional `Maxio:BaseUrl` / optional `MAXIO_ENVIRONMENT` from configuration (user-secrets only). Construct `MaxioAdvancedBillingClient` for the Production server group.
2. **GET `/api/subscription-plans`** — `ProductFamilies.ListProductsForProductFamily` with the family **handle** (not a numeric id). Map catalog fields from the `ProductResponse` envelope.
3. **POST `/api/subscriptions`** — (a) idempotent customer ensure: `Customers.ReadCustomerByReference` then `Customers.CreateCustomer`; (b) idempotent enroll: `Subscriptions.FindSubscription` then `Subscriptions.CreateSubscription` with `product_handle` and no payment profile. Default plan handle `eshop-pro`; caller may pass `basic-plan`.
4. **GET `/api/my-subscriptions`** — `Customers.ListCustomerSubscriptions` using the ensured customer’s runtime `Id`. Read plan / price / state / next-billing-date from the `SubscriptionResponse` envelope.
5. **Error boundary** around every SDK call (throw-only SDK; no `*Result` variants).

Seeded handles (never hard-code numeric IDs): family `eshop-subscribe`; plans `eshop-pro`, `basic-plan`. Component `api-call` is seeded but unused in this hero flow.

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

### 0. Client construction, auth, server / BaseUrl

| Fact | Contract | Cite |
|---|---|---|
| NuGet / namespace / client | Package `AsadAli.AdvancedBilling.Sdk`. `using MaxioAdvancedBilling;`. Client `MaxioAdvancedBilling.MaxioAdvancedBillingClient`. Options `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions`. | `sdk-map.md` |
| Only constructor | `MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` — SDK does **not** own the `HttpClient`. | `sdk-map.md` |
| DI helper | `MaxioAdvancedBilling.ServiceCollectionExtensions.AddMaxioAdvancedBillingClient(this Microsoft.Extensions.DependencyInjection.IServiceCollection, System.Action<MaxioAdvancedBillingClientOptions>? configure = null)` → `IServiceCollection`. C# 14 `extension(IServiceCollection)` member; call as `services.AddMaxioAdvancedBillingClient(o => { … })`. Internally `AddHttpClient()` + singleton client. | `ServiceCollectionExtensions.cs` |
| Options properties | `Environment`: `MaxioAdvancedBilling.Servers.ServerEnvironment` (default `ServerEnvironment.Default()` = `Us`). `Retry`: `MaxioAdvancedBilling.Core.Configuration.RetryOptions` (all members `required`; start from `RetryOptions.Default()` or supply a full instance). `Server`: `MaxioAdvancedBilling.ServerOptions`. `BasicAuth`: `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?`. | `sdk-map.md`, `MaxioAdvancedBillingClientOptions.cs` |
| Auth (Basic only) | `options.BasicAuth = new BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }`. Both `Username` and `Password` are `required string` (`init`). Username = API key; password is the **literal** `"x"`. | `sdk-map.md`, `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Environments | `ServerEnvironment.Us` (wire `US`) → US hosting. `ServerEnvironment.Eu` (wire `EU`) → EU hosting. There is **no** sandbox `ServerEnvironment`. Sandbox is the **site** (`Maxio:Subdomain`, e.g. `cp-exp-1`). Map `MAXIO_ENVIRONMENT` only to `Us`/`Eu`. | `sdk-map.md`, `Servers/ServerEnvironment.cs` |
| Production URL | US template `https://{site}.chargify.com`; EU `https://{site}.ebilling.maxio.com`. Set `{site}` via `options.Server.Production.Us.Site` or `.Eu.Site` (`string`, default `"subdomain"`). Nested types: `MaxioAdvancedBilling.ServerOptions.Production` → `MaxioAdvancedBilling.Servers.ProductionOptions`; `.Us` / `.Eu` → nested `ProductionOptions.UsOptions` / `EuOptions`. | `sdk-map.md`, `ServerOptions.cs`, `Servers/ProductionOptions.cs` |
| Optional `Maxio:BaseUrl` | When **set**, assign it **verbatim** to `options.Server.Production.<Us\|Eu>.BaseUrl` for the selected `Environment` (property type `string`; US default `"https://{site}.chargify.com"`). Do **not** also derive a host from subdomain in that case. When **unset**, leave `BaseUrl` at the template and set `Site` from `Maxio:Subdomain`. Always set `Site` from subdomain even when overriding `BaseUrl` (harmless if the override has no `{site}` placeholder). Hero flow uses Production only; do not touch `options.Server.Ebb`. | `sdk-map.md`, `Servers/ProductionOptions.cs` |
| Bindings (hard-code none) | `Maxio:ApiKey` ← `MAXIO_API_KEY`; `Maxio:Subdomain` ← `MAXIO_SITE_SUBDOMAIN`; `Maxio:ProductFamilyHandle` ← `MAXIO_DEFAULT_PRODUCT_FAMILY`; `Maxio:BaseUrl` optional; `MAXIO_ENVIRONMENT` optional → `ServerEnvironment`. Secrets → .NET user-secrets only. | user request |

⚠ Step 1 (client registration) — the SDK does not own `HttpClient`; ownership/lifetime and whether the client wrapper is singleton vs transient are not visible from the constructor. **MUST load `dotnet-client-initialization`** before wiring DI.

⚠ Step 1 (auth) — credentials must be present before the first call; hard-coding the API key is a secret leak. **MUST load `dotnet-authentication`** before setting `BasicAuth`.

⚠ Step 1 (BaseUrl / retries) — `Retry`/`Timeout` on options are **not** the timeout on the `HttpClient` you register, and they do **not** bound a whole call; a failed subscribe `POST` and transport retries have different consequences. **MUST load `dotnet-configuration-resilience`** before registering or tuning the client.

### 1. List plans in a product family (handle)

| | |
|---|---|
| Controller | `client.ProductFamilies` (`MaxioAdvancedBilling.Api.ProductFamilies`) |
| Operation | `ListProductsForProductFamily` |
| HTTP | `GET /product_families/{product_family_id}/products.json` (Production) |
| Signature | `ListProductsForProductFamily(string productFamilyId, MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, MaxioAdvancedBilling.Models.ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| Must-pass | `dateField` … `include` (8 params) are nullable with **no C# default** — pass `null` to skip. Use **named arguments**. `page` default 1, `perPage` default 20 (XML: max 200). |
| Handle lookup | `productFamilyId` = `"handle:" + config.ProductFamilyHandle` (e.g. `"handle:eshop-subscribe"`). XML: *“Either the product family's id or its handle prefixed with `handle:`”*. Never hard-code a numeric family id. (`ReadProductFamily` takes `int id` and **cannot** take a handle — do not use it for this.) |
| Returns | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>` |
| Envelope | `ProductResponse.Product` (`product`) : `MaxioAdvancedBilling.Models.Product` **!req** — read one level down. |
| Catalog fields to present | From inner `Product`: `Handle (handle): string?`, `Name (name): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?` (cents; $299.00 → `29900`), `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `ProductPricePointHandle (product_price_point_handle): string?`, `RequireCreditCard (require_credit_card): bool?`, `ProductFamily (product_family): ProductFamily?` (inner `Handle (handle): string?`). |
| Pagination | Manual `page`+`perPage`. Loop until a page returns fewer than `perPage` (or empty). |
| Error | **Case A** `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>` · `TryGetString(out string)` [404] · `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` [fallback] |
| Cite | `operations/ProductFamilies.md`, `records-3-Of-Su.md` (`Product`, `ProductResponse`, `ProductFamily`), `Api/ProductFamilies.cs` |

Optional single-plan read (validate POST handle): `client.Products.ReadProductByHandle(string apiHandle, CancellationToken ct = default)` → `ProductResponse` · **Case B** `SdkException<RawError>` · `operations/Products.md`.

`ListProductsFilter` (`Ids`, `PrepaidProductPricePoint`, `UseSiteExchangeRate`) has **no family-handle field** — do not use `ListProducts` as the family catalog. Cite: `records-2-Cr-Ne.md`.

⚠ Step 2 (list call) — eight leading optionals have no defaults; a positional call mis-binds. **MUST load `dotnet-calling-endpoints`** before the first list/find call.

### 2. Idempotent customer ensure

**Identity key:** `CreateCustomer.Reference` / lookup `reference` = the eShopOnWeb user’s **stable application id** (from the JWT), **not** email. Map notes: *“If provided, the `reference` value must be unique. It represents a unique identifier for the customer from your own app, i.e. the customer’s ID.”* Email is still **required** on create but is not the uniqueness key.

| | Find | Create (if not found) |
|---|---|---|
| Controller | `client.Customers` | `client.Customers` |
| Operation | `ReadCustomerByReference` | `CreateCustomer` |
| HTTP | `GET /customers/lookup.json?reference=` | `POST /customers.json` |
| Signature | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | `CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, **no default**, must pass explicitly |
| Request | query `reference` ← `reference` | Envelope `CreateCustomerRequest.Customer` (`customer`) : `CreateCustomer` **!req**. Inner **required**: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`. Set `Reference (reference): string?` to the eShop user id. Other fields optional. |
| Returns | `CustomerResponse` | `CustomerResponse` |
| Envelope | `CustomerResponse.Customer` (`customer`) : `Customer` **!req** | same |
| Read back | `Id (id): int?` (needed for list-subs), `Reference (reference): string?`, `Email (email): string?` | same |
| “Already exists” | Success ⇒ customer exists. Missing customer: **Case B** `SdkException<RawError>`; treat `ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound` as not-found and create. | Duplicate `reference` is rejected (map: only one customer per reference). **Case A** `SdkException<CreateCustomerError>` · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. `CustomerErrorResponse1.Errors` is typed `Errors?` whose generated members are only `PerPage` / `PricePoint` — **do not** treat that shape as a readable “reference taken” message. On 422, **re-call `ReadCustomerByReference`**; if found, return that customer (double-click / race). If still missing, surface the 422. **UNVERIFIED:** exact live 422 JSON vs this generated `Errors` record. |
| Cite | `operations/Customers.md`, `records-2-Cr-Ne.md` (`Customer`, `CustomerResponse`), `records-1-Ac-Cr.md` (`CreateCustomer`, `CreateCustomerRequest`), `records-2-Cr-Ne.md` (`CustomerErrorResponse1`, `Errors`) | |

`ListCustomers(..., q:)` is a **search** (array, not a single exact match). Do not use it for ensure; the lookup endpoint is `ReadCustomerByReference`.

### 3. Create subscription (handle, no payment, idempotent)

| | |
|---|---|
| Controller | `client.Subscriptions` |
| Operation | `CreateSubscription` |
| HTTP | `POST /subscriptions.json` |
| Signature | `CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly |
| Envelope | `CreateSubscriptionRequest.Subscription` (`subscription`) : `CreateSubscription` **!req** |
| Required-by-docs (all C# fields optional/`?`) | **Product:** `ProductHandle (product_handle): string?` **or** `ProductId (product_id): int?`. Use **handle only** (`eshop-pro` / `basic-plan`). XML: product id “is not currently published, so we recommend using the API Handle instead.” **Customer:** `CustomerId (customer_id): int?` **or** `CustomerReference (customer_reference): string?` **or** `CustomerAttributes`. After ensure, pass `CustomerId` (runtime id) **or** `CustomerReference` = same eShop user id. |
| Payment | **Omit** `PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes`. XML: payment *may* be required depending on product options; seeded plans are “payment method not required.” Do not send card data / 3-DS fields. |
| Idempotency key | `Reference (reference): string?` — “the reference value (provided by your app) for the subscription itself.” Use a **deterministic** value `{customerReference}:{productHandle}` (e.g. `{userId}:eshop-pro`). |
| Find before create | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` nullable, **must pass explicitly**. HTTP `GET /subscriptions/lookup.json`. Returns `SubscriptionResponse`. **Case A** `SdkException<FindSubscriptionError>` · `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback]. 404 ⇒ create; success ⇒ return existing (double-click). |
| Create error | **Case A** `SdkException<CreateSubscriptionError>` · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback]. `ErrorListResponse1.Errors (errors): IReadOnlyList<string> !req`. On 422 after a race, re-`FindSubscription` with the same reference; if found, return it. **UNVERIFIED:** whether live 422 text always means duplicate reference vs other validation. |
| Returns | `SubscriptionResponse` |
| Cite | `operations/Subscriptions.md`, `records-2-Cr-Ne.md` (`CreateSubscription`, `CreateSubscriptionRequest`), `Models/CreateSubscription.cs` |

Do not pass `Components` (metered `api-call` reporting is out of scope).

⚠ Step 3 (payloads) — request records use `required`/`init` and `StringEnum<T>`; unions are factory/`TryGet` (none required on this subscribe body). **MUST load `dotnet-models`** before constructing `CreateCustomer` / `CreateSubscription`.

### 4. List caller’s subscriptions

| | |
|---|---|
| Controller | `client.Customers` |
| Operation | `ListCustomerSubscriptions` |
| HTTP | `GET /customers/{customer_id}/subscriptions.json` |
| Signature | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` |
| `customerId` | Runtime `Customer.Id` from step 2 (ensure). Not a hard-coded catalog id. |
| Returns | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>` |
| Envelope | `SubscriptionResponse.Subscription` (`subscription`) : `MaxioAdvancedBilling.Models.Subscription?` — **nullable**; skip nulls. |
| Fields for GET `/api/my-subscriptions` | **Plan:** nested `Product (product): Product?` → `Handle (handle): string?`, `Name (name): string?`. **Price:** `ProductPriceInCents (product_price_in_cents): long?` (subscription’s current product price in cents). **State:** `State (state): SubscriptionState?`. **Next billing date:** `NextAssessmentAt (next_assessment_at): DateTimeOffset?`. Related period end (do not substitute unless the API DTO needs both): `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`. |
| Also useful | `Id (id): int?`, `Reference (reference): string?`, `Currency (currency): string?`, `Product.Interval` / `Product.IntervalUnit` |
| Pagination | **none** (single list). |
| Error | **Case B** `SdkException<RawError>` · `StatusCode` · `ReadAsString()` / `ReadAsJson<T>()` / `ReadAsBytes()` |
| Cite | `operations/Customers.md`, `records-4-Su-We.md` (`Subscription`, `SubscriptionResponse`), `records-3-Of-Su.md` (`Product`) |

### Enums in scope (`MaxioAdvancedBilling.Models.Enums`)

These are `StringEnum<T>` records, **not** C# enums. Write `IntervalUnit.Month`, not `IntervalUnit.month`. Construct with the static member or `Type.FromValue("wire")`. Cite: `map/models/enums.md`.

| Enum | Members (C# · wire) |
|---|---|
| `IntervalUnit` | `Day (day)`, `Month (month)` |
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` — pass `null` on list-plans |
| `ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)` — pass `null` |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` — omit on subscribe unless you intentionally set collection |

### Error types that actually reach `catch` (all in-scope ops)

SDK operations are **throw-only**. Catch `MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>` (`public required TError Error { get; init; }`). `TError` is **not** interchangeable across operations.

| Operation | `TError` | Status / accessors | Payload |
|---|---|---|---|
| `ListProductsForProductFamily` | `ListProductsForProductFamilyError` : `ApiError` | 404 `TryGetString(out string)` · else `TryGetRawError` | string / `RawError` |
| `ReadProductByHandle` | `RawError` (Case B) | `StatusCode: HttpStatusCode` · `ReadAsString()` · `ReadAsJson<T>()` · `ReadAsBytes()` | raw body |
| `ReadCustomerByReference` | `RawError` (Case B) | same | 404 = not found |
| `CreateCustomer` | `CreateCustomerError` : `ApiError` | 422 `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` · else `TryGetRawError` | `CustomerErrorResponse1.Errors (errors): Errors?` |
| `FindSubscription` | `FindSubscriptionError` : `ApiError` | 404 `TryGetNoContent(out RawError)` · else `TryGetRawError` | `RawError` |
| `CreateSubscription` | `CreateSubscriptionError` : `ApiError` | 422 `TryGetErrorListResponse1(out ErrorListResponse1)` · else `TryGetRawError` | `ErrorListResponse1.Errors: IReadOnlyList<string> !req` |
| `ListCustomerSubscriptions` | `RawError` (Case B) | `StatusCode` + read methods | raw body |

Typed errors inherit `ApiError.TryGetRawError(out RawError)` (`MaxioAdvancedBilling.Core.ErrorResponse`). `TryGetRawError` is **not** a catch-all for the 422/404 typed branches — those populate the specific `TryGet…` accessor, not the fallback.

Case B / fallback `RawError` (`MaxioAdvancedBilling.Core.ErrorResponse.RawError`): `StatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()`. Do not parse `Exception.ToString()` when an accessor exists.

Namespaces: exceptions `MaxioAdvancedBilling.Core.Exceptions`; error classes `MaxioAdvancedBilling.Errors`; `ApiError`/`RawError` `MaxioAdvancedBilling.Core.ErrorResponse`; payloads `MaxioAdvancedBilling.Models`. Cite: `sdk-map.md` (error-core), each operations page, `Core/Exceptions/SdkException.cs`.

⚠ Error boundary — `System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:
- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Tests — the `HttpClient` constructor argument is the fake seam; do not stub SDK internals. **MUST load `dotnet-testing`** before writing integration tests.

---

## Trap notes

- ⚠ Step 1 (client registration) — HttpClient / client lifetime vs per-request construction. **MUST load `dotnet-client-initialization`**.
- ⚠ Step 1 (auth) — when credentials are applied relative to client construction; load key from config. **MUST load `dotnet-authentication`**.
- ⚠ Step 1 (resilience / BaseUrl) — retry/timeout options vs the registered `HttpClient`; whether a failed write can be re-sent. **MUST load `dotnet-configuration-resilience`**.
- ⚠ Steps 2–4 (calls) — list/find signatures with many no-default optionals; cancellation token is `ct:`. **MUST load `dotnet-calling-endpoints`**.
- ⚠ Steps 2–4 (models) — `required` init members, `StringEnum<T>`, envelope `.Product` / `.Customer` / `.Subscription`. **MUST load `dotnet-models`**.
- ⚠ All steps (errors) — Case A vs Case B per operation; `JsonException` from 2xx vs non-2xx (rows above). **MUST load `dotnet-error-handling`**.
- ⚠ Tests — `HttpClient` seam. **MUST load `dotnet-testing`**.

---

## REQUIRED READING

Load **before implementation starts**. This sheet does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — constructing/`AddMaxioAdvancedBillingClient`, HttpClient ownership |
| `dotnet-authentication` | Step 1 — `BasicAuthCredentials` |
| `dotnet-configuration-resilience` | Step 1 — `Server`/`BaseUrl`, `Retry`/`Timeout` |
| `dotnet-calling-endpoints` | Steps 2–4 — named args, `ct:`, throw-only calls |
| `dotnet-models` | Steps 2–4 — request/response records, enums, envelopes |
| `dotnet-error-handling` | All steps — catch types, accessors, `JsonException` dual path |
| `dotnet-testing` | Tests for the integration layer |

---

## Assumptions & Blockers

### Assumptions

- Customer **reference** = eShopOnWeb user id from the JWT (stable, unique). `FirstName` / `LastName` / `Email` come from the authenticated user profile (email required by `CreateCustomer`).
- Subscribe request identifies the plan by **handle** (`eshop-pro` default, `basic-plan` alternate). Idempotency is **per user + product handle** via subscription `Reference` `{customerReference}:{productHandle}` — a double-click of the same plan returns the existing subscription; subscribing to a *different* plan creates a second subscription.
- `MAXIO_ENVIRONMENT` selects `ServerEnvironment.Us` vs `Eu` (hosting region), not sandbox-vs-production. Sandbox targeting is `Maxio:Subdomain` (and optional verbatim `Maxio:BaseUrl`).
- Seeded products really have payment-not-required; the SDK still omits all payment-profile fields.
- PublicApi JSON DTOs may convert `PriceInCents` to a decimal for the shopper; the SDK field is `long?` cents.

### Blockers

*(empty — every hero-flow capability is present in the SDK: family listing by `handle:` prefix, customer lookup-by-reference, subscribe by `product_handle` without payment fields, subscription lookup-by-reference, list-by-customer.)*
