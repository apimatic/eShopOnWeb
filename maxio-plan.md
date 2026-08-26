# Maxio Advanced Billing integration plan — eShopOnWeb PublicApi

Recurring-subscription billing for eShopOnWeb with Maxio Advanced Billing as billing system of record.
Three JWT-authenticated endpoints on `src/PublicApi`: list plans, subscribe (idempotent), list my subscriptions.
Metered component `api-call` exists on the family but usage reporting is **out of scope** — no plan rows for it.

## 1. Scope & sequence

| Step | Work | SDK operations used |
|---|---|---|
| 1 | Install package, bind `Maxio:*` config, construct/DI-register the client (auth + base URL) | — (client construction) |
| 2 | `GET /api/subscription-plans` — resolve family handle → numeric id (once, cached), list products in family | `ProductFamilies.ListProductFamilies`, `ProductFamilies.ListProductsForProductFamily` |
| 3 | `POST /api/subscriptions` — find-or-create customer by `reference` (= eShopOnWeb username), then create subscription by product handle with a deterministic subscription `reference` | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Subscriptions.FindSubscription` (idempotency pre-check), `Subscriptions.CreateSubscription` |
| 4 | `GET /api/my-subscriptions` — resolve customer by `reference`, list its subscriptions | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |
| 5 | Error boundary: translate SDK exceptions to HTTP problem responses | (error model below) |
| 6 | Tests for the integration layer | (test seam: the `HttpClient` constructor arg) |

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

### 2.0 Package, client construction, auth, base URL

| Fact | Value | Map source |
|---|---|---|
| NuGet package | `AsadAli.AdvancedBilling.Sdk` (version `1.0.2` per map stamp; `dotnet add package AsadAli.AdvancedBilling.Sdk`) | `sdk-map.md` |
| Root namespace | `MaxioAdvancedBilling` (**differs from package id**) | `sdk-map.md` |
| Client class | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` — only ctor: `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` (`MaxioAdvancedBillingClient.cs`) |
| Options type | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` — props: `Environment: ServerEnvironment`, `Retry: RetryOptions`, `Server: ServerOptions`, `BasicAuth: BasicAuthCredentials?` | `sdk-map.md` (`MaxioAdvancedBillingClientOptions.cs`) |
| Auth | `options.BasicAuth = new BasicAuthCredentials { Username = <apiKey>, Password = "x" }` — `BasicAuthCredentials` is in `MaxioAdvancedBilling.Core.Authentication.Basic` (`Core/Authentication/Basic/BasicAuthCredentials.cs`). Username = API key, Password = literal `"x"`. | `sdk-map.md` |
| Environment | `MaxioAdvancedBilling.Servers.ServerEnvironment` — `ServerEnvironment.Us` (default, `https://{site}.chargify.com`) / `ServerEnvironment.Eu` (`https://{site}.ebilling.maxio.com`). Sandbox `cp-exp-4` ⇒ `Us`. | `sdk-map.md` (`Servers/ServerEnvironment.cs`) |
| Base URL from subdomain | `options.Server.Production.Us.Site = "<subdomain>"` (`{site}` template slot; defaults to literal `subdomain` if unset) | `sdk-map.md` (`Servers/ProductionOptions.cs`) |
| Base URL verbatim override | `options.Server.Production.Us.BaseUrl = "<Maxio:BaseUrl>"` — used verbatim as the API base address (mock/dev host redirect). When `Maxio:BaseUrl` is set, prefer it over `Site`. | `sdk-map.md` (`Server.cs`, `ServerOptions.cs`) |
| DI alternative | `services.AddMaxioAdvancedBillingClient(o => { … })` (`ServiceCollectionExtensions.cs`, root namespace) | `sdk-map.md` |
| Config binding | `Maxio:ApiKey` → `BasicAuth.Username`; `Maxio:Subdomain` → `Server.Production.Us.Site`; `Maxio:BaseUrl` (optional) → `Server.Production.Us.BaseUrl` verbatim; `Maxio:ProductFamilyHandle` → app-level family resolution (step 2). Nothing hardcoded. | — |

### 2.1 `GET /api/subscription-plans` — list products in the configured family

**No by-handle product-family lookup exists in the SDK** (see Assumptions). Verified two-call approach — resolve the numeric family id once and cache it:

| # | Controller property · signature | Request | Response envelope + fields read | Error case | Map source |
|---|---|---|---|---|---|
| a | `client.ProductFamilies` · `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 nullable params **must be passed explicitly** (pass `null`) | — | `IReadOnlyList<ProductFamilyResponse>` → `.ProductFamily` (`ProductFamily?`, wire `product_family`) → match `Handle (handle): string?` == config handle, take `Id (id): int?`. No pagination. | **Case B** `SdkException<RawError>` | `operations/ProductFamilies.md`, `records-3-Of-Su.md` |
| b | `client.ProductFamilies` · `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 nullable params **must be passed explicitly** (pass `null`); `productFamilyId` is a **string** path param — pass the resolved numeric id as `id.ToString()` | — | `IReadOnlyList<ProductResponse>` → `.Product` (`Product`, **required**, wire `product`). Pagination: manual `page`/`perPage` (defaults 1/20). | **Case A** `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` | `operations/ProductFamilies.md`, `records-3-Of-Su.md` |

`Product` fields the endpoint returns (`records-3-Of-Su.md`, `Models/Product.cs` — all nullable on read):

| C# property (wire) | CLR type | Endpoint use |
|---|---|---|
| `Id (id)` | `int?` | plan id |
| `Name (name)` | `string?` | plan name |
| `Handle (handle)` | `string?` | plan handle (what `POST /api/subscriptions` takes) |
| `PriceInCents (price_in_cents)` | `long?` | price — **integer cents** ($299.00 ⇒ `29900`), not decimal |
| `Interval (interval)` | `int?` | interval length (e.g. `1`) |
| `IntervalUnit (interval_unit)` | `IntervalUnit?` | interval unit — enum below |
| `ProductFamily (product_family)` | `ProductFamily?` | family `Id`/`Handle` if needed |

### 2.2 `POST /api/subscriptions` — idempotent subscribe

**Find-or-create customer** (stable external reference = the user's identity/username stored as Maxio customer `reference`):

| # | Controller property · signature | Request model + fields | Response envelope + fields read | Error case | Map source |
|---|---|---|---|---|---|
| a | `client.Customers` · `ReadCustomerByReference(string reference, CancellationToken ct = default)` (query `reference`) | — | `CustomerResponse` → `.Customer` (`Customer`, **required**, wire `customer`) → `Id (id): int?`, `Reference (reference): string?` | **Case B** `SdkException<RawError>` — "not found" = `ex.Error.StatusCode == HttpStatusCode.NotFound` (404) | `operations/Customers.md`, `records-2-Cr-Ne.md` |
| b | `client.Customers` · `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly | `CreateCustomerRequest` { `Customer (customer): CreateCustomer` **required** }; `CreateCustomer` required: `FirstName (first_name): string`, `LastName (last_name): string`, `Email (email): string`; optional: `Reference (reference): string?` (+ address/org/phone etc.) — **set `Reference` = username** | `CustomerResponse.Customer` → `Id`, `Reference` | **Case A** `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` | `operations/Customers.md`, `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md` |

`reference` uniqueness is map-documented ("only one customer for a given reference value") — a retry-safe find-or-create is: `ReadCustomerByReference` → on 404 `CreateCustomer`; on a 422 race (concurrent create), re-`ReadCustomerByReference`.

**Idempotency pre-check + create subscription:**

| # | Controller property · signature | Request model + fields | Response envelope + fields read | Error case | Map source |
|---|---|---|---|---|---|
| c | `client.Subscriptions` · `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` must be passed explicitly (query `reference`) | deterministic subscription reference, e.g. `{username}:{productHandle}` | `SubscriptionResponse` → `.Subscription` (`Subscription?`, wire `subscription` — **nullable, null-check**) | **Case A** `SdkException<FindSubscriptionError>` — `TryGetNoContent(out RawError)` [404 = not yet subscribed] · `TryGetRawError(out RawError)` | `operations/Subscriptions.md`, `records-4-Su-We.md` |
| d | `client.Subscriptions` · `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly | `CreateSubscriptionRequest` { `Subscription (subscription): CreateSubscription` **required** }; `CreateSubscription` fields to set (all optional in the model): `ProductHandle (product_handle): string?` — **product by handle**; `CustomerId (customer_id): int?` — attach the resolved customer (alternative: `CustomerReference (customer_reference): string?` = the same username reference); `Reference (reference): string?` — the deterministic subscription reference from (c); `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` — see enum table | `SubscriptionResponse.Subscription` → `Id (id): int?`, `State (state): SubscriptionState?`, `Product (product): Product?` (name/handle/`ProductPriceInCents` is on the subscription, see below), `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `Customer (customer): Customer?` | **Case A** `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)`; `ErrorListResponse1.Errors (errors): IReadOnlyList<string>` **required** — the validation messages | `operations/Subscriptions.md`, `records-2-Cr-Ne.md`, `records-4-Su-We.md` |

Do **not** set `ProductId` when using `ProductHandle` (either/or per the operation's notes). No payment profile fields are set — the seeded products don't require a payment method (see Assumptions).

### 2.3 `GET /api/my-subscriptions` — list the user's subscriptions

| # | Controller property · signature | Request | Response envelope + fields read | Error case | Map source |
|---|---|---|---|---|---|
| a | `client.Customers` · `ReadCustomerByReference(username, ct)` | — | as 2.2a → customer `Id` (nullable `int?` — null-check before use) | Case B, 404 ⇒ user has no subscriptions (empty list) | `operations/Customers.md` |
| b | `client.Customers` · `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | customer id from (a) | `IReadOnlyList<SubscriptionResponse>` → each `.Subscription` (nullable). No pagination. | **Case B** `SdkException<RawError>` | `operations/Customers.md`, `records-4-Su-We.md` |

`Subscription` fields read for the response (`records-3-Of-Su.md`, `Models/Subscription.cs` — all nullable):

| C# property (wire) | CLR type | Endpoint use |
|---|---|---|
| `Id (id)` | `int?` | subscription id |
| `State (state)` | `SubscriptionState?` | state — enum below |
| `Product (product)` | `Product?` | plan `Name`/`Handle` (nested `Product`, fields per 2.1) |
| `ProductPriceInCents (product_price_in_cents)` | `long?` | unit price, integer cents |
| `NextAssessmentAt (next_assessment_at)` | `DateTimeOffset?` | **next billing date — prefer this** |
| `CurrentPeriodEndsAt (current_period_ends_at)` | `DateTimeOffset?` | fallback for next billing date |
| `Customer (customer)` | `Customer?` | not needed in response (it's the caller) |

### 2.4 Enums actually needed (`map/models/enums.md`; namespace `MaxioAdvancedBilling.Models.Enums`)

Enums are `StringEnum<T>`, **not** C# enums — use the static members (`CollectionMethod.Automatic`) or `Type.FromValue("wire")`.

| Enum | Members (wire values) | Used in |
|---|---|---|
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` | `CreateSubscription.PaymentCollectionMethod` — use `Automatic` (seeded products require no payment method; `Remittance` = invoice-based fallback if the site rejects automatic signup — see Assumptions) |
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` | `Subscription.State` — read/compare |
| `IntervalUnit` | `Day (day)`, `Month (month)` | `Product.IntervalUnit` — read/compare |

### 2.5 Error model (applies to every call)

- All operations are **throw-only** — no `…Result`/no-throw variants exist in this SDK (`sdk-map.md`).
- Thrown type: `SdkException<TError>` — `MaxioAdvancedBilling.Core.Exceptions` (source `Core/Exceptions/SdkException.cs`); `.Error` exposes `TError`.
- **Case A** (typed): `TError` ∈ `MaxioAdvancedBilling.Errors` — `ListProductsForProductFamilyError`, `CreateCustomerError`, `FindSubscriptionError`, `CreateSubscriptionError` here. Use the per-operation `TryGet…` accessors in 2.1–2.3; every typed error also has inherited `TryGetRawError(out RawError)`.
- **Case B** (raw): `TError` = `RawError` — `MaxioAdvancedBilling.Core.ErrorResponse` (source `Core/ErrorResponse/RawError.cs`): `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`, `ReadAsBytes(): ReadOnlyMemory<byte>`.
- Not-found vs validation on these flows: 404 = `RawError.StatusCode` (Case B reads, `ListCustomerSubscriptions`), `TryGetString` (`ListProductsForProductFamily`), `TryGetNoContent` (`FindSubscription`); 422 validation = `TryGetErrorListResponse1` → `ErrorListResponse1.Errors` (`CreateSubscription`), `TryGetCustomerErrorResponse1` (`CreateCustomer` — see Assumptions re its under-modeled payload).
- CLR date types: every date on these models is `DateTimeOffset?` — no `DateTime` anywhere in scope.
- Unions on `CreateSubscriptionRequest`: exactly one — `CreateSubscription.OfferId (offer_id): OfferId?` (`MaxioAdvancedBilling.Models.AnyOf`, AnyOf `string`/`int`; factories `OfferId.String(string)` / `OfferId.Int(int)`). **Not needed** by these flows; no union construction required anywhere in scope.

## 3. Trap notes

- ⚠ Step 1 (client registration) — the `HttpClient`/handler pipeline and the SDK client wrapper have different required lifetimes; constructing either the wrong way leaks sockets or defeats pooling. **MUST load `dotnet-client-initialization`** before writing `new MaxioAdvancedBillingClient(...)` or the DI registration.
- ⚠ Step 1 (resilience) — what `RetryOptions.Timeout` actually bounds and whether a failed `CreateSubscription` POST can be re-sent by the retry layer are not visible from the options' names; this determines why the app-level reference/idempotency design in 2.2 is mandatory, not optional. **MUST load `dotnet-configuration-resilience`** before wiring `options.Retry` or tuning timeouts.
- ⚠ Step 1 (auth) — when in the construction/DI sequence credentials must be set, and how the key is loaded from configuration, are not shown by the options shape. **MUST load `dotnet-authentication`** before wiring `BasicAuth`.
- ⚠ Steps 2–4 (every call) — most optional parameters have **no C# default** and mis-bind in positional calls; the must-pass-explicitly `null`s and the literal `ct:` parameter name bite on every signature above. **MUST load `dotnet-calling-endpoints`** before the first call.
- ⚠ Steps 2–4 (models) — enums are `StringEnum<T>` (no `switch` on C# enum members), records are immutable with `required` init members, and **unmodeled JSON fields are silently dropped on deserialize** (directly relevant to the 2.2b 422 payload — see Assumptions). **MUST load `dotnet-models`** before constructing request bodies or mapping response models.
- ⚠ Step 5 (error boundary) — which of these six operations is Case A vs Case B, and the fact that `TryGetRawError` is not a catch-all on typed errors, determine the whole catch ladder. **MUST load `dotnet-error-handling`** before writing any `try/catch`.
- ⚠ Step 6 (tests) — the test seam and what to assert are not derivable from the signatures. **MUST load `dotnet-testing`** before stubbing the SDK.

## 4. REQUIRED READING

Load **before implementation starts** — this sheet deliberately does not carry their contents:

- `dotnet-client-initialization` — step 1 (client construction & DI lifetime)
- `dotnet-authentication` — step 1 (basic-auth credentials)
- `dotnet-calling-endpoints` — steps 2–4 (every operation call)
- `dotnet-models` — steps 2–4 (request/response models, enums, nullability)
- `dotnet-error-handling` — step 5 (the exception boundary)
- `dotnet-configuration-resilience` — step 1 (retry/timeout/base-URL tuning)
- `dotnet-testing` — step 6 (integration-layer tests)

Always include, verbatim, **both** of these hazard rows — `System.Text.Json.JsonException`
reaches the boundary from two directions and they need opposite handling:
- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
  maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
  and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

## 5. Assumptions & Blockers

1. **Family-by-handle lookup**: the SDK exposes no dedicated "get family by handle" operation. `ReadProductFamily` documents the `handle:my-family` path format but its SDK signature is `int id`, so the format can't be passed there. `ListProductsForProductFamily` takes `string productFamilyId`, so `"handle:eshop-subscribe"` *might* work verbatim — **UNVERIFIED** (only live traffic can confirm). The sheet therefore uses the fully map-grounded path: `ListProductFamilies` → match `Handle` → cache numeric id. If the app later prefers the `handle:` shortcut, verify it live first.
2. **Signup without a payment method**: the operation notes state payment info "may be required … depending on the options for the Product". The brief states the seeded products don't require one, so `CollectionMethod.Automatic` with no payment profile is planned — whether the live site accepts it is **UNVERIFIED** here. Defensive directive: on `CreateSubscriptionError` 422, surface `ErrorListResponse1.Errors` messages to the caller verbatim; if the site demands payment for automatic collection, switch `PaymentCollectionMethod` to `CollectionMethod.Remittance` (invoice-based) rather than adding payment-profile fields.
3. **`CreateCustomer` 422 payload is under-modeled**: `CustomerErrorResponse1.Errors` is typed as `Errors` (`Models/Errors.cs`), which declares only `PerPage (per_page)` and `PricePoint (price_point)` — a suspicious shared model that will drop the real field-error keys (e.g. `email`, `reference`) on deserialize. Directive: extract best-effort via `TryGetCustomerErrorResponse1`, but **fall back to `TryGetRawError` → `ReadAsString()`** for the actual messages. **UNVERIFIED** whether the live 422 body matches the generated shape.
4. **Subscription-reference uniqueness**: customer `reference` uniqueness is map-documented; subscription `reference` uniqueness enforcement server-side is **not** stated in the map. The idempotency design is check-then-create via `FindSubscription` with a deterministic reference; a narrow concurrent-double-submit race cannot be excluded by contract alone — on a create 422 after a raced double-submit, re-`FindSubscription` and return the existing one.
5. **Next billing date**: two candidate fields exist (`NextAssessmentAt`, `CurrentPeriodEndsAt`); the sheet prefers `NextAssessmentAt` with `CurrentPeriodEndsAt` as fallback. Which the live site populates for these products is **UNVERIFIED** — read both, display whichever is non-null.
6. **Read-model nullability**: nearly every field on `Product`, `Customer`, `Subscription` (and the `SubscriptionResponse.Subscription` envelope itself) is nullable — the endpoint mapping code must null-check throughout; `ProductResponse.Product` and `CustomerResponse.Customer` are the only `required` envelopes in scope.
7. No blockers.
