# Maxio Advanced Billing integration — eShopOnWeb `src/PublicApi`

Additive, parallel subscription-billing capability. Does NOT replace the existing cart/checkout flow.
Three endpoints under `/api/`: `GET /api/subscription-plans`, `POST /api/subscriptions` (idempotent),
`GET /api/my-subscriptions`. Caller identity comes from the JWT token.

## 1. Scope & sequence

| Step | Work | SDK operations used |
|---|---|---|
| 1 | Add NuGet package `AsadAli.AdvancedBilling.Sdk` 1.0.2 to `src/PublicApi` | — |
| 2 | Register the client in DI: `AddMaxioAdvancedBillingClient(...)` with BasicAuth + base URL (subdomain-derived or `Maxio:BaseUrl` override) | — |
| 3 | `GET /api/subscription-plans`: resolve family by handle, list its products, map to a plan DTO (handle, name, price dollars, interval) | `ListProductFamilies` → `ListProductsForProductFamily` |
| 4 | `POST /api/subscriptions`: ensure customer (lookup by reference, create if absent), resolve plan by handle, idempotent subscription create, read back id/state/price/next-billing-date | `ReadCustomerByReference` → `CreateCustomer` · `ReadProductByHandle` · `FindSubscription` → `CreateSubscription` |
| 5 | `GET /api/my-subscriptions`: list the user's subscriptions, map to DTO (id, state, plan, price, next-billing-date) | `ListCustomerSubscriptions` |
| 6 | Error boundary: translate `SdkException<TError>` (Case A/B per operation) + `JsonException` + `HttpRequestException` into HTTP results | — |
| 7 | Tests for the integration layer | — |

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

### 2.1 Operations

| # | Controller property | Method signature (params in order) | Request model + fields used | Response envelope + inner fields read | Error case + accessors + payload | Pagination | Source |
|---|---|---|---|---|---|---|---|
| 1 | `client.ProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — 5 nullable params, **no C# default → pass `null` explicitly** (named args) | none | `IReadOnlyList<ProductFamilyResponse>`; each wraps `ProductFamily (product_family): ProductFamily?` (**nullable envelope**). Read: `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?` | **Case B** `SdkException<RawError>`: `ex.Error.StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` | none | `operations/ProductFamilies.md` |
| 2 | `client.ProductFamilies` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 nullable params (`dateField`…`include`) **must pass explicitly** (pass `null`); `page`/`perPage` have defaults | none | `IReadOnlyList<ProductResponse>`; each wraps `Product (product): Product !req`. Read: `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `RequireCreditCard (require_credit_card): bool?`, `ArchivedAt (archived_at): DateTimeOffset?` | **Case A** `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage` | `operations/ProductFamilies.md` |
| 3 | `client.Products` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | none | `ProductResponse` → `Product` (fields as row 2) | **Case B** `SdkException<RawError>` | none | `operations/Products.md` |
| 4 | `client.Customers` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — query param `reference` | none | `CustomerResponse` → `Customer (customer): Customer !req`. Read: `Id (id): int?`, `Reference (reference): string?` | **Case B** `SdkException<RawError>` — 404 = `ex.Error.StatusCode == HttpStatusCode.NotFound` | none | `operations/Customers.md` |
| 5 | `client.Customers` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly** | `CreateCustomerRequest` wraps `Customer (customer): CreateCustomer !req`. `CreateCustomer` required: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`. Optional used: `Reference (reference): string?` — the app's own customer id; **Notes: "you may only create one customer for a given reference value"** (server-enforced uniqueness). Others optional: `Organization`, `Phone`, `Locale`, `TaxExempt`, … | `CustomerResponse` → `Customer.Id: int?` | **Case A** `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] — payload `CustomerErrorResponse1` = `Errors (errors): Errors?` (`Errors` = `PerPage (per_page): IReadOnlyList<string>?`, `PricePoint (price_point): IReadOnlyList<string>?`) · `TryGetRawError(out RawError)` [fallback] | none | `operations/Customers.md` |
| 6 | `client.Subscriptions` | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` nullable, no default → **must pass explicitly** | none | `SubscriptionResponse` → `Subscription (subscription): Subscription?` (**nullable envelope**) | **Case A** `SdkException<FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |
| 7 | `client.Subscriptions` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly** | `CreateSubscriptionRequest` wraps `Subscription (subscription): CreateSubscription !req`. `CreateSubscription` fields used (all optional): `ProductHandle (product_handle): string?` (or `ProductId (product_id): int?`), `CustomerId (customer_id): int?` (or `CustomerReference (customer_reference): string?`), `Reference (reference): string?` (subscription reference — the idempotency key, looked up via `FindSubscription`), `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `DeferSignup (defer_signup): bool? = false`, `NextBillingAt (next_billing_at): DateTimeOffset?`, `InitialBillingAt (initial_billing_at): DateTimeOffset?`, `Components (components): IReadOnlyList<CreateSubscriptionComponent>?`, `Metafields (metafields): IReadOnlyDictionary<string, string>?`. **No card/payment-profile fields needed**: the request model has no "require card" flag — acceptance without a payment method is governed by the product's `RequireCreditCard` setting (sandbox: `false`; Notes: "Payment information may be required … depending on the options for the Product being subscribed") | `SubscriptionResponse` → `Subscription?`. Read: `Id (id): int?`, `State (state): SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `Reference (reference): string?`, `Product (product): Product?` (nested: `Handle`, `Name`, `PriceInCents`, `Interval`, `IntervalUnit`) | **Case A** `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] — payload `ErrorListResponse1` = `Errors (errors): IReadOnlyList<string> !req` · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |
| 8 | `client.Customers` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | none | `IReadOnlyList<SubscriptionResponse>`; each wraps `Subscription?`. Read per item: `Id`, `State`, `ProductPriceInCents`, `NextAssessmentAt`, `CurrentPeriodEndsAt`, `Product.Handle/Name/PriceInCents/Interval/IntervalUnit` | **Case B** `SdkException<RawError>` | none | `operations/Customers.md` |

Notes on the rows:
- **Envelope rule**: every response wraps its payload one level down — `ProductResponse.Product`, `CustomerResponse.Customer`, `SubscriptionResponse.Subscription` (**nullable**), `ProductFamilyResponse.ProductFamily` (**nullable**). Reads go one level down; null-check the nullable envelopes.
- **`ReadProductFamily(int id, …)` is NOT usable for handle lookup** — the parameter is `int` (confirmed in `Api/ProductFamilies.cs`); the "`handle:my-family`" format in its Notes is not reachable through the SDK signature. Resolve the family by handle via row 1 + filter on `ProductFamily.Handle == "eshop-subscribe"`.
- **`ListProductsForProductFamily` returns products only** (`ProductResponse` items). The `api-call` metered component is a component (separate `Components` controller), not a product — it is not in this response and is out of scope for these three endpoints.
- **Price is cents**: `Product.PriceInCents` / `Subscription.ProductPriceInCents` are `long?` cents. Dollars = `cents / 100.0` (use `decimal`). Billing interval = `Interval: int?` + `IntervalUnit: IntervalUnit?`.
- **Next billing date**: read `Subscription.NextAssessmentAt` (`next_assessment_at`); `CurrentPeriodEndsAt` (`current_period_ends_at`) is the fallback. Which one the live sandbox populates for a no-card subscription is `UNVERIFIED` — extract best-effort, fall back to the other, then to the generic message.
- **State after no-card creation** is read back and surfaced, never assumed: `UNVERIFIED` whether the live sandbox returns `active` or `awaiting_signup` for a no-card signup. Do not gate the response on a specific state; report the state string as returned.
- **Idempotency**: customer — `ReadCustomerByReference` (row 4); on 404 → `CreateCustomer` (row 5) with `Reference` set. Subscription — `FindSubscription` (row 6) by the subscription `Reference`; on 404 → `CreateSubscription` (row 7) with the same `Reference`. Server-side uniqueness is documented for the **customer** reference only; subscription-reference uniqueness is `UNVERIFIED` (see §5).

### 2.2 Client construction, auth, server/base URL

| Fact | Value | Source |
|---|---|---|
| NuGet package | `AsadAli.AdvancedBilling.Sdk` — reference version **1.0.2** (map stamp: source commit `15db14b`, tag `v1.0.2`). Root namespace differs from package id: `using MaxioAdvancedBilling;` | `sdk-map.md` |
| Client class | `MaxioAdvancedBillingClient` — **only constructor**: `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` (namespace `MaxioAdvancedBilling`) | `sdk-map.md` |
| Options class | `MaxioAdvancedBillingClientOptions` (namespace `MaxioAdvancedBilling`): `Environment: ServerEnvironment` (default `ServerEnvironment.Default()` = Us) · `Retry: RetryOptions` (default `RetryOptions.Default()`) · `Server: ServerOptions` (default `new()`) · `BasicAuth: BasicAuthCredentials?` | `MaxioAdvancedBillingClientOptions.cs` |
| Auth | HTTP **Basic**: `BasicAuthCredentials { Username = <API key>, Password = "x" }` — **username = API key, password = literal `"x"`**. Both members `required` (set in the initializer). Namespace `MaxioAdvancedBilling.Core.Authentication.Basic` | `sdk-map.md`, `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Environments | `ServerEnvironment.Us` (default) → `https://{site}.chargify.com` · `ServerEnvironment.Eu` → `https://{site}.ebilling.maxio.com`. Namespace `MaxioAdvancedBilling.Servers` | `sdk-map.md`, `Servers/ServerEnvironment.cs` |
| Base URL — derive from subdomain | `options.Server.Production.Us.Site = "<subdomain>"` → base `https://<subdomain>.chargify.com` (Us) / `https://<subdomain>.ebilling.maxio.com` (Eu). Sandbox subdomain: `cp-exp-1` | `sdk-map.md`, `Servers/ProductionOptions.cs` |
| Base URL — verbatim override | `options.Server.Production.Us.BaseUrl = "<verbatim>"` — replaces the template as-is (e.g. `http://localhost:8080`). When set, `Site` is irrelevant. This is the `Maxio:BaseUrl` override point | `sdk-map.md`, `Servers/ProductionOptions.cs` |
| Server options types | `ServerOptions` (namespace `MaxioAdvancedBilling`): `Production: ProductionOptions`, `Ebb: EbbOptions`. `ProductionOptions`/`EbbOptions` (namespace `MaxioAdvancedBilling.Servers`): `Us`/`Eu`, each a nested options class with `BaseUrl: string` and `Site: string` | `ServerOptions.cs`, `Servers/ProductionOptions.cs`, `Servers/EbbOptions.cs` |
| DI registration | `services.AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>? configure = null)` — namespace `MaxioAdvancedBilling` | `ServiceCollectionExtensions.cs` |
| Retry options | `RetryOptions` (namespace `MaxioAdvancedBilling.Core.Configuration`): `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout: TimeSpan?`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry` — **all members `required`**: build a full instance or start from `RetryOptions.Default()` | `sdk-map.md` |

### 2.3 Namespaces to import (each kind of type needs its own `using`)

| Contents | Namespace |
|---|---|
| Client, options, `ServerOptions`, DI extension | `MaxioAdvancedBilling` |
| Controllers (only if you name a controller type; `client.X` calls need no using) | `MaxioAdvancedBilling.Api` |
| Records (request/response models) | `MaxioAdvancedBilling.Models` |
| Enums | `MaxioAdvancedBilling.Models.Enums` |
| Typed error classes (`CreateSubscriptionError`, …) | `MaxioAdvancedBilling.Errors` |
| `SdkException<TError>` | `MaxioAdvancedBilling.Core.Exceptions` |
| `RawError`, `ApiError` | `MaxioAdvancedBilling.Core.ErrorResponse` |
| `BasicAuthCredentials` | `MaxioAdvancedBilling.Core.Authentication.Basic` |
| `ServerEnvironment`, `ProductionOptions`, `EbbOptions` | `MaxioAdvancedBilling.Servers` |
| `RetryOptions` | `MaxioAdvancedBilling.Core.Configuration` |

### 2.4 Enums actually needed

| Enum | C# member (wire value) | Source |
|---|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` | `map/models/enums.md` |
| `IntervalUnit` | `Day (day)`, `Month (month)` | `map/models/enums.md` |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` | `map/models/enums.md` |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` | `map/models/enums.md` |
| `ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)` | `map/models/enums.md` |

Enums are **not** C# enums — they are `StringEnum<T>`; C# member names are PascalCase (`SubscriptionState.Active`), wire values are lowercase snake (`active`). Compare/switch on the C# members; the wire value is only what goes over the wire.

### 2.5 Error-handling model (applies to every operation)

- **No `ApiException` exists in this SDK.** The brief's premise is wrong for this package: `SdkException<TError>` is `sealed` and derives directly from `Exception` (namespace `MaxioAdvancedBilling.Core.Exceptions`), exposing `public required TError Error { get; init; }`. Other exception types that can reach a catch block: `AuthSchemeException` (auth configuration, `MaxioAdvancedBilling.Core.Exceptions`), `HttpRequestException` (transport, from `HttpClient`/Polly), `JsonException` (deserialization — see REQUIRED READING).
- **Case A (typed)**: `SdkException<{Operation}Error>` where `{Operation}Error : ApiError` (namespace `MaxioAdvancedBilling.Errors`). Status-specific `TryGet…(out …)` accessors (per-row above) plus inherited `TryGetRawError(out RawError)` fallback. `ApiError` lives in `MaxioAdvancedBilling.Core.ErrorResponse`.
- **Case B (raw)**: `SdkException<RawError>` — `RawError.StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`, `ReadAsBytes(): ReadOnlyMemory<byte>`.
- **No no-throw variants**: every operation is throw-only; always wrap the call.
- Per-operation cases: rows 1, 3, 4, 8 are **Case B**; rows 2, 5, 6, 7 are **Case A** with the accessors listed in §2.1.

## 3. Trap notes

> ⚠ Step 2 (client registration) — the SDK's retry/timeout options do **not** bound a whole
> call and are **not** the timeout on the `HttpClient` you register. **MUST load
> `dotnet-configuration-resilience`** before wiring the client.

> ⚠ Step 2 (auth wiring) — the auth scheme/manager shape and where credentials must be supplied
> (before construction / in the DI callback) is not visible in the signature. **MUST load
> `dotnet-authentication`** before setting credentials.

> ⚠ Steps 3–6 (every call) — many optional parameters have no C# default and mis-bind in a
> positional call; call with named arguments. **MUST load `dotnet-calling-endpoints`** before
> the first `client.{Group}.{Operation}(...)` call.

> ⚠ Steps 4/6 (request/response models) — enums are `StringEnum<T>` not C# enums, unions are
> built with factories and read via `TryGet…` (no `new`), and unmodeled JSON fields are dropped
> on deserialize. **MUST load `dotnet-models`** when building request payloads or mapping
> responses.

> ⚠ Step 4 (idempotency) — a transport failure (`HttpRequestException`) is retried on **every**
> verb, `POST` included, and `MaxRetries = 0` is rejected at construction (floor 1) — so a
> non-idempotent write can execute more than once and no setting disables that; double-click
> protection must not rely on retry suppression. **MUST load `dotnet-configuration-resilience`**
> before finalizing the idempotency design.

> ⚠ Step 6 (error boundary) — Case A vs Case B differs per operation (see §2.5), `TryGetRawError`
> is not a catch-all on the typed errors, and this SDK generates **no** no-throw variants — every
> operation is throw-only. **MUST load `dotnet-error-handling`** before writing any `try/catch`.

> ⚠ Step 7 (tests) — the `HttpClient` constructor argument is the test seam; match the project's
> existing framework and assertion style. **MUST load `dotnet-testing`** before stubbing the SDK.

## 4. REQUIRED READING

Load every skill below **before implementation starts**. The contract sheet deliberately does
not carry their contents — it names the hazard and hands you the skill that resolves it.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 2 — client construction, `HttpClient` ownership/lifetime, DI registration |
| `dotnet-authentication` | Step 2 — Basic auth credentials, scheme/manager shape, per-environment config |
| `dotnet-calling-endpoints` | Steps 3–6 — controller lookup, required vs optional params, named-argument calls, envelopes |
| `dotnet-models` | Steps 4/6 — request construction, required members, enums, wire names |
| `dotnet-error-handling` | Step 6 — the exception boundary, status/body access, Case A/B mechanics |
| `dotnet-configuration-resilience` | Steps 2/4 — retries, timeouts, base URL, what a timeout actually bounds |
| `dotnet-testing` | Step 7 — the fake seam, error/edge-path coverage |

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

- **Assumption — user→customer mapping**: the eShopOnWeb user is mapped to a Maxio customer via
  the customer `Reference` field, set to a deterministic string derived from the JWT subject
  (e.g. `eshop-{sub}`). The app's own identity path decides the exact derivation.
  `YOUR CALL — not in the map`.
- **Assumption — subscription idempotency key**: `CreateSubscription.Reference` is set to a
  deterministic per-(user, plan) string (e.g. `eshop-{sub}-{planHandle}`) and looked up via
  `FindSubscription` before create. Server-side uniqueness of the **subscription** reference is
  NOT documented in the map (only the customer reference uniqueness is) — `UNVERIFIED`. Combined
  with the POST retry-on-transport-failure fact (§3), the app needs its own per-user concurrency
  guard (e.g. a lock) to make a double-click truly single-create. The SDK fact forces this
  decision; the mechanism is the app's.
- **Assumption — environment mapping**: `MAXIO_ENVIRONMENT` selects `ServerEnvironment.Us` vs
  `ServerEnvironment.Eu`; the sandbox itself is selected by the subdomain `cp-exp-1` (base
  `https://cp-exp-1.chargify.com`). `YOUR CALL — not in the map`.
- **Assumption — base URL override**: when `Maxio:BaseUrl` is set, it is applied verbatim to
  `options.Server.Production.Us.BaseUrl` and takes precedence over subdomain derivation.
- **Assumption — no-card signup**: no payment-profile fields are sent at subscription creation;
  the sandbox product's `RequireCreditCard = false` makes the call accepted without card/3-DS
  (per the brief). The resulting subscription state is read back and surfaced, not assumed —
  `UNVERIFIED` which state the live sandbox returns.
- **Assumption — next billing date**: read from `Subscription.NextAssessmentAt`, falling back to
  `CurrentPeriodEndsAt` — `UNVERIFIED` which the live payload populates for a no-card
  subscription.
- **Blocker**: none.
