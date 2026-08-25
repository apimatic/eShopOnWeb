# Maxio recurring-billing integration — plan & contract sheet (src/PublicApi)

## 1. Scope & sequence

1. **Package + config** — add NuGet package `AsadAli.AdvancedBilling.Sdk` to `src/PublicApi` (verified absent: no `AdvancedBilling`/`AsadAli` reference anywhere in the repo). Bind a `Maxio:` config section (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, optional `BaseUrl`).
2. **Client registration** — register `MaxioAdvancedBillingClient` in the PublicApi service container with Basic auth and server/site (or base-URL override) from config.
3. **GET /api/subscription-plans** — `ListProductFamilies` → match `ProductFamily.Handle == Maxio:ProductFamilyHandle` client-side → `ListProductsForProductFamily(familyId)` → map each `Product` (name, handle, price, interval).
4. **POST /api/subscriptions** (idempotent) — `ReadCustomerByReference(userId)` → on 404 `CreateCustomer` → `FindSubscription(deterministicReference)` → on 404 `CreateSubscription` → return plan/price/state/next-billing from the `Subscription`.
5. **GET /api/my-subscriptions** — `ReadCustomerByReference(userId)` → `ListCustomerSubscriptions(customer.Id)` → map state, product name/handle, unit price, next assessment.
6. **Error boundary + tests** — one translation layer around all SDK calls; fake the `HttpClient` seam in tests.

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

### Package & client construction (sdk-map.md)

| Fact | Value |
|---|---|
| NuGet package | `AsadAli.AdvancedBilling.Sdk` (`dotnet add package AsadAli.AdvancedBilling.Sdk` on `src/PublicApi`) — **not currently referenced** in the repo |
| Root namespace / client / options | `MaxioAdvancedBilling` / `MaxioAdvancedBilling.MaxioAdvancedBillingClient` / `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` |
| Target framework | `netstandard2.0` — fine on the repo's .NET 8 (roll-forward .NET 10) |
| Only constructor | `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` |
| Options properties | `Environment: ServerEnvironment` · `Retry: RetryOptions` · `Server: ServerOptions` · `BasicAuth: BasicAuthCredentials?` |
| Basic auth | `options.BasicAuth = new BasicAuthCredentials { Username = <apiKey>, Password = "x" }` — `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials` (source `Core/Authentication/Basic/BasicAuthCredentials.cs`); password is the **literal `"x"`** |
| Environment | `MaxioAdvancedBilling.Servers.ServerEnvironment` — `ServerEnvironment.Us` (default) → `https://{site}.chargify.com`; `ServerEnvironment.Eu` → `https://{site}.ebilling.maxio.com` |
| Site subdomain | `options.Server.Production.Us.Site = "cp-exp-1"` (`{site}` defaults to `subdomain`) |
| Base-URL override | when `Maxio:BaseUrl` is set: `options.Server.Production.Us.BaseUrl = <value>` **verbatim** (replaces the derived host; used verbatim as API base address) |
| DI extension | `services.AddMaxioAdvancedBillingClient(o => { … })` (source `ServiceCollectionExtensions.cs`, root namespace `MaxioAdvancedBilling`) |

`using` directives needed across the integration: `MaxioAdvancedBilling` (client/options/DI ext), `MaxioAdvancedBilling.Core.Authentication.Basic` (credentials), `MaxioAdvancedBilling.Servers` (`ServerEnvironment`), `MaxioAdvancedBilling.Models` (all records below), `MaxioAdvancedBilling.Models.Enums` (`SubscriptionState`, `IntervalUnit`), `MaxioAdvancedBilling.Errors` (typed `…Error` classes), `MaxioAdvancedBilling.Core.Exceptions` (`SdkException<T>`, implied by source `Core/Exceptions/SdkException.cs`), `MaxioAdvancedBilling.Core.ErrorResponse` (`RawError`, implied by source `Core/ErrorResponse/RawError.cs`).

### Operations (one row per op; all are throw-only — no `…Result` variants exist in this SDK)

| # | Controller · signature (verbatim) | Request model + fields used (`Name (wire_name): Type, req?`) | Response envelope + fields read | Error case + accessors | Pagination |
|---|---|---|---|---|---|
| 1 | `client.ProductFamilies.ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 filter params nullable-no-default → **pass `null` explicitly** (operations/ProductFamilies.md) | — | `IReadOnlyList<ProductFamilyResponse>`; `ProductFamilyResponse.ProductFamily (product_family): ProductFamily?`; `ProductFamily`: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?` (records-3-Of-Su.md). No handle filter exists server-side → match `Handle` client-side | **Case B** `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | none |
| 2 | `client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 middle params nullable-no-default → **pass `null` explicitly**; `productFamilyId` is `string` → pass `family.Id.ToString()` (operations/ProductFamilies.md) | — | `IReadOnlyList<ProductResponse>`; `ProductResponse.Product (product): Product !req` (required); `Product` fields read: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?` (records-3-Of-Su.md) | **Case A** `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page`/`perPage` (defaults 1/20) |
| 3 | `client.Customers.ReadCustomerByReference(string reference, CancellationToken ct = default)` — query `reference` (operations/Customers.md) | — | `CustomerResponse.Customer (customer): Customer !req`; `Customer`: `Id (id): int?`, `Reference (reference): string?`, `FirstName (first_name)`, `LastName (last_name)`, `Email (email)` (records-2-Cr-Ne.md) | **Case B** `SdkException<RawError>` — customer-not-found = `ex.Error.StatusCode == HttpStatusCode.NotFound` | none |
| 4 | `client.Customers.CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable-no-default → **pass explicitly** (operations/Customers.md) | `CreateCustomerRequest.Customer (customer): CreateCustomer !req`; `CreateCustomer`: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?` ← set to the eShopOnWeb user id (records-1-Ac-Cr.md). Server enforces `reference` uniqueness | `CustomerResponse` (as row 3) | **Case A** `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. Payload: `CustomerErrorResponse1.Errors (errors): Errors?`; the generated `Errors` record models **only** `PerPage (per_page): IReadOnlyList<string>?` and `PricePoint (price_point): IReadOnlyList<string>?` (records-2-Cr-Ne.md) — see trust note below | none |
| 5 | `client.Subscriptions.FindSubscription(string? reference, CancellationToken ct = default)` — `reference` nullable-no-default → **pass explicitly** (operations/Subscriptions.md) | — | `SubscriptionResponse` (as row 6) | **Case A** `SdkException<FindSubscriptionError>` — `TryGetNoContent(out RawError)` [404 = no such subscription] · `TryGetRawError(out RawError)` [fallback] | none |
| 6 | `client.Subscriptions.CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable-no-default → **pass explicitly** (operations/Subscriptions.md) | `CreateSubscriptionRequest.Subscription (subscription): CreateSubscription !req`; `CreateSubscription` fields used (records-2-Cr-Ne.md): `ProductHandle (product_handle): string?` ← `"eshop-pro"`/`"basic-plan"`, `CustomerId (customer_id): int?` ← from row 3/4, `Reference (reference): string?` ← deterministic app-side idempotency key (e.g. `"{userId}:{planHandle}"`), `CustomerReference (customer_reference): string?` (alternative to `CustomerId` — use one). **No payment fields**: seeded products don't require a payment method, so omit `PaymentProfileId`/`CreditCardAttributes`/etc. | `SubscriptionResponse.Subscription (subscription): Subscription?` (records-4-Su-We.md); `Subscription` fields read (records-3-Of-Su.md): `Id (id): int?`, `State (state): SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?` (current price), `NextAssessmentAt (next_assessment_at): DateTimeOffset?` (**next billing date — the model carries no `next_billing_at`**), `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `Product (product): Product?` (nested; `Name`/`Handle`) | **Case A** `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. Payload: `ErrorListResponse1.Errors (errors): IReadOnlyList<string> !req` (records-2-Cr-Ne.md) | none |
| 7 | `client.Customers.ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` (operations/Customers.md) | — | `IReadOnlyList<SubscriptionResponse>` → per item the same `Subscription` fields as row 6 (`State`, `Product.Name`/`Product.Handle`, `ProductPriceInCents` = unit price, `NextAssessmentAt`) | **Case B** `SdkException<RawError>` | none |

(`ListSubscriptions` exists with a `SubscriptionStateFilter? state` filter but **no customer filter** — it is not the right op for my-subscriptions; use row 7. operations/Subscriptions.md.)

### Enums actually used (map/models/enums.md) — all `StringEnum<T>`, **not** C# enums

| Enum (namespace `MaxioAdvancedBilling.Models.Enums`) | Members (wire values) |
|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `IntervalUnit` | `Day (day)`, `Month (month)` |

### Error-handling model (sdk-map.md)

Throw-based only: `SdkException<TError>` with `.Error`. Case A: typed `…Error : ApiError` with the status-specific `TryGet…` accessors above plus inherited `TryGetRawError(out RawError)` fallback. Case B: `SdkException<RawError>` — `RawError.StatusCode: HttpStatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. Per-operation cases are in the table above — do not assume.

**Trust note (map-visible evidence):** the 422 payload for `CreateCustomer` (`CustomerErrorResponse1.Errors`) is typed to a shared `Errors` record that models only `per_page`/`price_point` error keys, while a real customer-validation 422 (duplicate `reference`, bad email) would carry other keys — which deserialization drops. Directive: extract `Errors.PerPage`/`PricePoint` best-effort, and **always also fall back to `TryGetRawError` → `ReadAsString()`** for the raw body so no validation detail is lost. Whether the live 422 wire shape carries more keys is `UNVERIFIED` (only live traffic could confirm).

### Idempotency (grounded answer: **no native support**)

Neither `CreateCustomer` nor `CreateSubscription` accepts an idempotency key — the signatures carry only `body` + `ct`, and neither request model has an idempotency-key field (operations/Customers.md, operations/Subscriptions.md, records-1-Ac-Cr.md, records-2-Cr-Ne.md). Implement find-or-create app-side:

- **Customer**: `reference` = eShopOnWeb user id → `ReadCustomerByReference` → on Case-B 404, `CreateCustomer` (server also enforces `reference` uniqueness, so a lost race surfaces as a 422 — treat that 422 as "re-read by reference").
- **Subscription**: set `CreateSubscription.Reference` to a deterministic value (e.g. `"{userId}:{planHandle}"`) → `FindSubscription(reference)` first → on Case-A 404 (`TryGetNoContent`), `CreateSubscription`. A double-click then converges on the existing subscription instead of creating a second.

## 3. Trap notes (hazards — load the named skill; resolutions deliberately omitted)

> ⚠ Step 2 (client registration) — the `HttpClient`/handler pipeline behind the SDK must be long-lived and reused, not built per request; how the SDK client wrapper itself may be registered differs from that. **MUST load `dotnet-client-initialization`** before writing the DI registration.
>
> ⚠ Step 2 (auth) — credentials must be supplied at the right point in the options/DI callback and the API key must come from configuration, never code; a mis-wired scheme surfaces as 401/403s. **MUST load `dotnet-authentication`**.
>
> ⚠ Steps 3–5 (every call) — most list/read signatures carry nullable parameters with **no C# default** that mis-bind in positional calls; call with named arguments (and `ct:` for the token). **MUST load `dotnet-calling-endpoints`** before the first call.
>
> ⚠ Steps 3–5 (models) — enums are `StringEnum<T>` (compare/build via static members or `FromValue("wire")`, not C# enum semantics); records are immutable with `required` init members; unmodeled JSON fields are silently dropped on deserialize (this is exactly why the `CreateCustomer` 422 payload above needs the raw-body fallback). **MUST load `dotnet-models`**.
>
> ⚠ Step 6 (error boundary) — which exception types actually reach a `catch` (Case A typed vs Case B raw per the table above) and how status/body are read safely are not guessable from signatures; `TryGetRawError` is not a catch-all on typed errors. **MUST load `dotnet-error-handling`** before writing any `try/catch`.
>
> ⚠ Step 4 (idempotency) — whether a failed `CreateSubscription` **POST can be re-sent by the SDK's own retry layer** (and what `Timeout` actually bounds) decides how much of the idempotency design the app must own; the option names alone do not reveal it. **MUST load `dotnet-configuration-resilience`** before wiring the client.
>
> ⚠ Step 6 (tests) — the test seam is a specific constructor argument, not mocking SDK internals; match the repo's existing test framework/assertion style. **MUST load `dotnet-testing`** before writing integration tests.

## 4. REQUIRED READING — load **before implementation starts**

This sheet deliberately does not carry these skills' contents; each governs the step named:

- `dotnet-client-initialization` — Step 2, client construction & DI registration.
- `dotnet-authentication` — Step 2, Basic credentials wiring (401/403 failures).
- `dotnet-calling-endpoints` — Steps 3–5, every operation call (named arguments, envelopes, async).
- `dotnet-models` — Steps 3–5, building request records, `StringEnum<T>` handling, wire-name mapping.
- `dotnet-error-handling` — Step 6, the exception-translation boundary (always required — an integration always writes an error boundary).
- `dotnet-configuration-resilience` — Steps 2 & 4, retries/timeout/base-URL behavior and its idempotency consequences.
- `dotnet-testing` — Step 6, faking the SDK seam in tests.

Two `System.Text.Json.JsonException` hazards reach the boundary from opposite directions and need opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

## 5. Assumptions & Blockers

- **Assumed US hosting** (`ServerEnvironment.Us`, the SDK default): the `Maxio:` config section has no environment key, so the base URL derives as `https://cp-exp-1.chargify.com` unless `Maxio:BaseUrl` overrides it verbatim.
- **Assumed JWT claims carry email + name** (or the identity system can supply them) to populate the `!req` `CreateCustomer.FirstName`/`LastName`/`Email`; `Reference` = the eShopOnWeb user id (app-side choice).
- **"Next billing date"** maps to `Subscription.NextAssessmentAt` (with `CurrentPeriodEndsAt` as period end): the generated `Subscription` model carries no `next_billing_at` field (records-3-Of-Su.md).
- **Plan price** maps to `Product.PriceInCents` (long, cents) + `Interval`/`IntervalUnit`; subscription current/unit price maps to `Subscription.ProductPriceInCents`.
- **UNVERIFIED**: the live 422 wire shape for `CreateCustomer` (see trust note in §2) — defensive raw-body fallback directive given; only live traffic could confirm.
- No blockers.
