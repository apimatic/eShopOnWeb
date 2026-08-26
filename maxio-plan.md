# Maxio Advanced Billing integration plan — eShopOnWeb recurring subscriptions

Scope: add JWT-authenticated recurring-subscription endpoints to `src/PublicApi`, backed by the
Maxio Advanced Billing .NET SDK (`AsadAli.AdvancedBilling.Sdk`, root namespace `MaxioAdvancedBilling`).
Additive to the existing one-time cart/checkout. All contract facts below are grounded in the bundled
SDK map (`sdk-map.md`, `map/operations/*.md`, `map/models/*.md`); one fact is grounded in the named SDK
source file `Api/ProductFamilies.cs` where the map was ambiguous (noted inline).

## 1. Scope & sequence

| # | Step | SDK operations used |
|---|------|---------------------|
| 1 | Add package: `PackageVersion Include="AsadAli.AdvancedBilling.Sdk" Version="1.0.2"` in `Directory.Packages.props` (not currently referenced anywhere — central package management is on), `PackageReference` in `src/PublicApi/PublicApi.csproj`. Version 1.0.2 = the tag the SDK map was generated from. | — |
| 2 | Bind config: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` (optional override), `Maxio:Environment` (`us`/`eu`, default `us`) from env vars `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_ENVIRONMENT`, `MAXIO_DEFAULT_PRODUCT_FAMILY`. | — |
| 3 | Register the client in PublicApi's DI (`AddMaxioAdvancedBillingClient`), setting Basic auth, environment, and site/base-URL. | — |
| 4 | `GET /api/subscription-plans` — list plans in the configured family. | `ProductFamilies.ListProductsForProductFamily` |
| 5 | `POST /api/subscriptions` — idempotent subscribe: find-or-create customer by `reference` = eShopOnWeb user id, pre-check existing subscriptions, create. | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Customers.ListCustomerSubscriptions`, `Subscriptions.CreateSubscription` |
| 6 | `GET /api/my-subscriptions` — list the caller's subscriptions. | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |
| 7 | Error boundary: translate SDK exceptions to ProblemDetails (401/404/422/5xx mapping). | — |
| 8 | Tests for the integration layer (fake at the `HttpClient` seam). | — |

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

### 2.1 Client construction, auth, server (source: `sdk-map.md` — *Getting a client*, *Servers & auth*)

| Fact | Value |
|---|---|
| Client class | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` — only ctor: `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` |
| Options class | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` — properties: `Environment` (`ServerEnvironment`), `Retry` (`RetryOptions`), `Server` (`ServerOptions`), `BasicAuth` (`BasicAuthCredentials?`) |
| Auth (Basic) | `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials` — `Username` = API key (`Maxio:ApiKey`), `Password` = the literal string `"x"`. Set `options.BasicAuth = new BasicAuthCredentials { Username = key, Password = "x" }` |
| Environment | `MaxioAdvancedBilling.Servers.ServerEnvironment` — `ServerEnvironment.Us` (default) → `https://{site}.chargify.com`; `ServerEnvironment.Eu` → `https://{site}.ebilling.maxio.com` |
| Site from subdomain | `options.Server.Production.Us.Site = "<subdomain>"` (or `.Eu.Site` when EU). `{site}` defaults to `subdomain`; set it to `Maxio:Subdomain` (`cp-exp-2` in sandbox) |
| Base-URL override | When `Maxio:BaseUrl` is set, use it verbatim instead: `options.Server.Production.Us.BaseUrl = "<Maxio:BaseUrl>"` (or `.Eu.BaseUrl`). `ServerOptions` is root-namespace `MaxioAdvancedBilling` (source file at repo root); `ProductionOptions` is `MaxioAdvancedBilling.Servers` |
| Retry options | `MaxioAdvancedBilling.Core.Configuration.RetryOptions` — **all members are C# `required`**; build a full instance or start from `RetryOptions.Default()` |
| DI registration | `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = …; o.Environment = …; o.Server.… = …; })` (extension in `ServiceCollectionExtensions.cs`) |
| API groups | Properties on the client: `client.ProductFamilies`, `client.Customers`, `client.Subscriptions` |
| Error types' namespaces | `SdkException<T>` ⇒ `MaxioAdvancedBilling.Core.Exceptions` (source `Core/Exceptions/SdkException.cs`); `RawError`, `ApiError` ⇒ `MaxioAdvancedBilling.Core.ErrorResponse` (source `Core/ErrorResponse/*.cs`); typed errors (`CreateCustomerError` etc.) ⇒ `MaxioAdvancedBilling.Errors` |
| Models / enums | Records ⇒ `MaxioAdvancedBilling.Models`; enums ⇒ `MaxioAdvancedBilling.Models.Enums` |

### 2.2 Operations

| Endpoint step | Controller property · signature (verbatim) | Request model | Response envelope → fields read | Error case + accessors | Pagination |
|---|---|---|---|---|---|
| List plans (step 4) | `client.ProductFamilies` · `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 8 params `dateField…include` are nullable with **no C# default → must be passed explicitly** (pass `null`). Pass `productFamilyId: "handle:" + productFamilyHandle` (e.g. `"handle:eshop-subscribe"`): the param is `string` and its doc comment in `Api/ProductFamilies.cs` states it accepts "Either the product family's id or its handle prefixed with `handle:`" — this is the handle-based lookup; **never hard-code the numeric family id** (ids are re-seeded). (map: `operations/ProductFamilies.md`; doc comment: `Api/ProductFamilies.cs`) | none (query only) | `IReadOnlyList<ProductResponse>` → each `.Product` (`Product`, C# `required`) → `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `ArchivedAt (archived_at): DateTimeOffset?` (map: `records-3-Of-Su.md`) | **Case A**: `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404 — unknown family handle] · `TryGetRawError(out RawError)` [fallback] | Manual `page`/`perPage` (defaults 1/20). Loop while a full page is returned; seeded catalog fits one page |
| Find customer (steps 5, 6) | `client.Customers` · `ReadCustomerByReference(string reference, CancellationToken ct = default)` — `reference` = the eShopOnWeb user id (stable JWT subject/NameIdentifier). (map: `operations/Customers.md`) | none (query `reference`) | `CustomerResponse` → `.Customer` (`Customer`, C# `required`) → `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?` (map: `records-2-Cr-Ne.md`) | **Case B**: `SdkException<RawError>` — **404 = customer absent** → create path. Read via `.Error.StatusCode` (`HttpStatusCode`); body via `.Error.ReadAsString()` / `.Error.ReadAsJson<T>()` | none |
| Create customer (step 5) | `client.Customers` · `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly**. (map: `operations/Customers.md`) | `CreateCustomerRequest` { `Customer (customer): CreateCustomer` **!req** } → `CreateCustomer` fields: `FirstName (first_name): string` **!req**, `LastName (last_name): string` **!req**, `Email (email): string` **!req**, `Reference (reference): string?` ← **always set to the eShopOnWeb user id** — the API enforces reference uniqueness, which is the idempotency anchor. Optional: `Organization`, `Address`, `City`, `State`, `Zip`, `Country`, `Phone` (map: `records-1-Ac-Cr.md`) | `CustomerResponse` → `.Customer.Id` | **Case A**: `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. ⚠ Trust caveat (map-visible): `CustomerErrorResponse1.Errors` is typed `Errors?`, and the shared `Errors` record models only `PerPage (per_page)` / `PricePoint (price_point)` — it does **not** model customer fields, so per-field messages (e.g. duplicate `reference`) may be dropped on deserialize. Defensive directive: treat **any** 422 from `CreateCustomer` as a possible duplicate-reference race → re-call `ReadCustomerByReference` and use its result; extract detail best-effort via `TryGetRawError`/`ReadAsString()`, fall back to a generic message. Exact 422 wire shape: **UNVERIFIED** (only live traffic confirms) (map: `records-2-Cr-Ne.md`) | none |
| Pre-check existing subs (step 5) + list mine (step 6) | `client.Customers` · `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` (map: `operations/Customers.md`) | none (path id) | `IReadOnlyList<SubscriptionResponse>` → each `.Subscription` (`Subscription?` — nullable, null-check) → fields below | **Case B**: `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | **None** — returns all of the customer's subscriptions |
| Create subscription (step 5) | `client.Subscriptions` · `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly**. (map: `operations/Subscriptions.md`) | `CreateSubscriptionRequest` { `Subscription (subscription): CreateSubscription` **!req** } → `CreateSubscription` fields used: `CustomerId (customer_id): int?` ← from find-or-create; `ProductHandle (product_handle): string?` ← **plan handle (`eshop-pro` / `basic-plan`), never the numeric id** (`ProductId (product_id): int?` exists but ids are unstable); `Reference (reference): string?` ← optional extra idempotency handle (e.g. `"{userId}:{planHandle}"`). Do **not** set `NextBillingAt`/`InitialBillingAt`/payment fields — seeded plans need no card. (map: `records-2-Cr-Ne.md`) | `SubscriptionResponse` → `.Subscription` (`Subscription?` — nullable, null-check) → `Id (id): int?`, `State (state): SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` ← **this is the "next billing date"** (the `Subscription` model has **no** `next_billing_at` field; `NextAssessmentAt (next_assessment_at): DateTimeOffset?` also exists), `CurrentPeriodStartedAt (current_period_started_at): DateTimeOffset?`, `Product (product): Product?` → `Name`, `Handle`, `PriceInCents`, `Interval`, `IntervalUnit`, `Customer (customer): Customer?`, `Reference (reference): string?` (map: `records-3-Of-Su.md`, envelope in `records-4-Su-We.md`) | **Case A**: `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] → `ErrorListResponse1.Errors (errors): IReadOnlyList<string>` **!req** · `TryGetRawError(out RawError)` [fallback] (map: `records-2-Cr-Ne.md`) | none |

Not used, and why: `Products.ListProducts` / `Products.ReadProductByHandle` — site-wide; its `ListProductsFilter` record has only `Ids`, `PrepaidProductPricePoint`, `UseSiteExchangeRate` (no family-handle filter), so family-scoped listing goes through `ListProductsForProductFamily` (map: `operations/Products.md`, `records-2-Cr-Ne.md`). `ProductFamilies.ListProductFamilies` (`ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)`, all 5 must be passed explicitly, returns `IReadOnlyList<ProductFamilyResponse>` → `.ProductFamily` → `Id`/`Handle`, Case B, no pagination) is the fallback if the `handle:` path-param form is ever rejected: resolve family id by matching `ProductFamily.Handle` at runtime, never hard-code it (map: `operations/ProductFamilies.md`, `records-3-Of-Su.md`).

### 2.3 Idempotency design (step 5)

1. `reference` = authenticated user's stable id on both `Customer` and (optionally) `Subscription`.
2. Find-or-create customer: `ReadCustomerByReference` → on `SdkException<RawError>` with `StatusCode == HttpStatusCode.NotFound`, `CreateCustomer`. On **any** 422 from `CreateCustomer` (possible duplicate-reference race — see trust caveat), re-`ReadCustomerByReference` and continue with the winner.
3. Double-click subscribe: before `CreateSubscription`, call `ListCustomerSubscriptions(customerId)`; if a subscription with matching `Product.Handle` in a live state (`active`, `trialing`, `assessing`, `pending`) already exists, return it instead of creating a second.
4. Alternative belt-and-braces: set `CreateSubscription.Reference = "{userId}:{planHandle}"` and pre-check with `Subscriptions.FindSubscription(string? reference, CancellationToken ct = default)` — Case A `SdkException<FindSubscriptionError>`, `TryGetNoContent(out RawError)` [404 = absent] (map: `operations/Subscriptions.md`).

### 2.4 Enums (map: `enums.md`) — `StringEnum<T>`, **not** C# enums; namespace `MaxioAdvancedBilling.Models.Enums`

| Enum | Values (C# member → wire) |
|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `IntervalUnit` | `Day (day)`, `Month (month)` |
| `SubscriptionStateFilter` (only if `Subscriptions.ListSubscriptions` is ever used) | `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)` |
| `BasicDateField`, `ListProductsInclude` | Passed as `null` in this integration (`BasicDateField`: `UpdatedAt (updated_at)`, `CreatedAt (created_at)`; `ListProductsInclude`: `PrepaidProductPricePoint (prepaid_product_price_point)`) |

### 2.5 Error-handling model (map: `sdk-map.md` — *Error-handling model*)

- Every operation is **throw-only** (no `…Result` no-throw variants exist in this SDK). On error status it throws `SdkException<TError>` exposing `.Error`.
- Case A (typed): `TError` = generated `…Error : ApiError` with status-specific `TryGet…(out …)` plus inherited `TryGetRawError(out RawError)` fallback. In scope: `CreateCustomer` [422], `CreateSubscription` [422], `ListProductsForProductFamily` [404], `FindSubscription` [404].
- Case B (raw): `TError` = `RawError` → `StatusCode: HttpStatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. In scope: `ReadCustomerByReference`, `ListCustomerSubscriptions`, `ListProductFamilies`.
- A 401 (bad API key / wrong password literal) surfaces through the same channel: `TryGetRawError` fallback on Case A ops, `RawError.StatusCode` on Case B ops — there is no dedicated auth exception type.
- Pagination is manual `page`/`perPage` everywhere it exists; `ListCustomerSubscriptions` and `ListProductFamilies` have none.

## 3. Trap notes (hazards — resolve by loading the named skill, not from this sheet)

> ⚠ Step 3 (client registration) — the `HttpClient`/handler pipeline behind the SDK has lifetime rules (who owns it, how it is reused) that the constructor signature does not show; getting it wrong exhausts sockets. **MUST load `dotnet-client-initialization`** before wiring DI.

> ⚠ Step 3 (auth) — credentials must be in place before the client is constructed / inside the DI callback, and the key must come from configuration, never source. **MUST load `dotnet-authentication`**.

> ⚠ Steps 4–6 (every call) — list/search operations take many nullable parameters with **no C# defaults**; a positional call mis-binds them silently. Call with named arguments (and the token is `ct:`). **MUST load `dotnet-calling-endpoints`**.

> ⚠ Steps 4–6 (models) — `SubscriptionState`/`IntervalUnit` are `StringEnum<T>`, not C# enums (compare/build via `Type.FromValue("wire")` or static members); records are immutable with `init`-only setters and `required` members; **unmodeled JSON fields are dropped on deserialize** (this is why the `CustomerErrorResponse1.Errors` caveat in §2.2 bites). **MUST load `dotnet-models`**.

> ⚠ Step 5 (subscribe) — `CreateSubscription` is a non-idempotent `POST`; whether the SDK's retry layer can re-send a failed write under the hood, and what `Timeout` actually bounds, decides whether the §2.3 pre-check is your only protection against duplicates. **MUST load `dotnet-configuration-resilience`**.

> ⚠ Step 7 (error boundary) — which operations are Case A vs Case B is per-operation (§2.2, §2.5); `TryGetRawError` is not a catch-all on typed errors; and `System.Text.Json.JsonException` reaches the boundary from two directions needing opposite handling:
> - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
> - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.
>
> **MUST load `dotnet-error-handling`** before writing that boundary.

> ⚠ Step 8 (tests) — the test seam is the `HttpClient` constructor argument; match the repo's existing xunit/NSubstitute style. **MUST load `dotnet-testing`**.

## 4. REQUIRED READING — load before implementation starts

This sheet deliberately does not carry these skills' contents; load each **before** writing the step it governs:

- `dotnet-client-initialization` — step 3 (client construction & DI lifetime)
- `dotnet-authentication` — step 3 (Basic credentials from config)
- `dotnet-calling-endpoints` — steps 4–6 (named-argument calling convention, envelopes)
- `dotnet-models` — steps 4–6 (StringEnum, required members, dropped unmodeled fields)
- `dotnet-error-handling` — step 7 (Case A/B mechanics, the two `JsonException` directions above)
- `dotnet-configuration-resilience` — steps 3, 5 (retry/timeout semantics, manual pagination)
- `dotnet-testing` — step 8 (HttpClient seam)

## 5. Assumptions & Blockers

**Assumptions**
- The authenticated eShopOnWeb user's stable id (JWT `sub`/NameIdentifier — Identity user id) is available in PublicApi endpoints and is used verbatim as the Maxio customer `reference`.
- `CreateCustomer` requires `FirstName`, `LastName`, `Email` (all C# `required`); assumed the Identity user's email plus a derived/display-name split (or a placeholder like the email local-part) satisfies them — confirm what profile data PublicApi can see.
- `MAXIO_ENVIRONMENT` values map `us`→`ServerEnvironment.Us`, `eu`→`ServerEnvironment.Eu`; default `us` when unset/ unparsable.
- Plan price is exposed only as `PriceInCents` (`long?`, cents) on `Product` — the API DTO divides by 100; there is no decimal price field on the model.
- "Next billing date" in the endpoint responses maps to `Subscription.CurrentPeriodEndsAt` (`current_period_ends_at`); the `Subscription` model has no `next_billing_at` field.
- Package version to add: **1.0.2** (the SDK map's stamped tag; the package is not yet referenced anywhere in the repo).
- Seeded plans need no payment profile, so `CreateSubscription` is called with only `CustomerId` + `ProductHandle` (+ optional `Reference`); if a future plan requires a card, that flow is out of scope here.
- **UNVERIFIED** (only live traffic can confirm): the exact 422 wire shape for a duplicate customer `reference` — the generated `Errors` record visibly models only `per_page`/`price_point`, so the defensive re-lookup directive in §2.2 stands regardless of the true shape.

**Blockers** — none.
