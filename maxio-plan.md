# Maxio Advanced Billing integration plan — eShopOnWeb recurring subscriptions

Grounded against the bundled SDK map (`maxio-getting-started` skill, SDK stamped at source commit `15db14b` / tag `v1.0.2`). Every signature, field, enum value, and error accessor below is map-verbatim unless marked `UNVERIFIED`.

## 1. Scope & sequence

| # | Step | SDK operations used |
|---|------|---------------------|
| 0 | Add NuGet package, `MaxioOptions` config class, client DI registration | — (client construction, §2.1) |
| 1 | `GET /api/subscription-plans` — resolve configured family handle → id (cache it), list products in family | `ProductFamilies.ListProductFamilies` → `ProductFamilies.ListProductsForProductFamily` |
| 2 | `POST /api/subscriptions` — ensure customer (lookup by `reference` = eShop user id; create on 404; re-read on 422 race) | `Customers.ReadCustomerByReference` → `Customers.CreateCustomer` |
| 3 | `POST /api/subscriptions` — dedupe: list caller's subscriptions, return existing live subscription to the same product handle | `Customers.ListCustomerSubscriptions` |
| 4 | `POST /api/subscriptions` — create subscription by product handle + customer id, no payment profile | `Subscriptions.CreateSubscription` |
| 5 | `GET /api/my-subscriptions` — list caller's subscriptions (state, product, next assessment) | `Customers.ListCustomerSubscriptions` |
| 6 | Error boundary + mapping to HTTP responses | error model, §2.4 |
| 7 | Tests for the service seam | §3 trap notes |

Out of scope: metered component `api-call` (usage reporting lives on `SubscriptionComponents`, 17 ops — not needed for the hero flow; add later as a separate plan). Webhooks, cancellation, plan changes: not requested.

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

### 2.1 SDK identity, client construction, auth, server

| Fact | Value | Map page |
|---|---|---|
| NuGet package | `AsadAli.AdvancedBilling.Sdk`, version **`1.0.2`** (the ref this sheet is grounded against; bump only after re-confirming names) | `sdk-map.md` |
| Root namespace | `MaxioAdvancedBilling` (≠ package id) | `sdk-map.md` |
| Client class | `MaxioAdvancedBillingClient` — only ctor: `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` (src `MaxioAdvancedBillingClient.cs`) |
| Options class | `MaxioAdvancedBillingClientOptions` — props: `Environment: ServerEnvironment`, `Retry: RetryOptions`, `Server: ServerOptions`, `BasicAuth: BasicAuthCredentials?` | `sdk-map.md` |
| DI alternative | `services.AddMaxioAdvancedBillingClient(o => { … })` | `sdk-map.md` (src `ServiceCollectionExtensions.cs`) |
| Auth | HTTP Basic: `BasicAuth = new BasicAuthCredentials { Username = <apiKey>, Password = "x" }` — password is the literal `"x"` | `sdk-map.md` |
| Environment | `ServerEnvironment.Us` (default) → `https://{site}.chargify.com`; `ServerEnvironment.Eu` → `https://{site}.ebilling.maxio.com` | `sdk-map.md` |
| Site (subdomain) | `options.Server.Production.Us.Site = "<subdomain>"` (`{site}` defaults to literal `subdomain` if unset) | `sdk-map.md` |
| **Base-URL override** (`Maxio:BaseUrl`) | `options.Server.Production.Us.BaseUrl = "<verbatim url>"` — replaces the derived URL entirely | `sdk-map.md` (src `ServerOptions.cs`, `Servers/ProductionOptions.cs`) |

Construction to match the required config (`Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` optional):

```csharp
using MaxioAdvancedBilling;                              // client, options, ServerOptions
using MaxioAdvancedBilling.Servers;                      // ServerEnvironment, ProductionOptions
using MaxioAdvancedBilling.Core.Authentication.Basic;    // BasicAuthCredentials

var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials { Username = cfg.ApiKey, Password = "x" },
    Environment = ServerEnvironment.Us,                  // sandbox site cp-exp-3 is US-hosted
};
options.Server.Production.Us.Site = cfg.Subdomain;       // "cp-exp-3"
if (!string.IsNullOrWhiteSpace(cfg.BaseUrl))
    options.Server.Production.Us.BaseUrl = cfg.BaseUrl;  // verbatim override wins
var client = new MaxioAdvancedBillingClient(httpClient, options);
```

Namespaces for `using` directives (map `sdk-map.md` namespace table + source-path rule):

| Types | Namespace |
|---|---|
| `MaxioAdvancedBillingClient`, `MaxioAdvancedBillingClientOptions`, `ServerOptions` | `MaxioAdvancedBilling` |
| `ServerEnvironment`, `ProductionOptions` | `MaxioAdvancedBilling.Servers` |
| `BasicAuthCredentials` | `MaxioAdvancedBilling.Core.Authentication.Basic` |
| `RetryOptions` | `MaxioAdvancedBilling.Core.Configuration` |
| `SdkException<TError>` | `MaxioAdvancedBilling.Core.Exceptions` |
| `RawError`, `ApiError` | `MaxioAdvancedBilling.Core.ErrorResponse` |
| All records (`Product`, `Customer`, `Subscription`, requests/responses, error payloads) | `MaxioAdvancedBilling.Models` |
| All enums (`SubscriptionState`, `IntervalUnit`, …) — `StringEnum<T>`, **not** C# enums | `MaxioAdvancedBilling.Models.Enums` |
| Typed error classes (`CreateCustomerError`, `CreateSubscriptionError`, `ListProductsForProductFamilyError`) | `MaxioAdvancedBilling.Errors` |

### 2.2 Operations (one row per operation; params in order; "must-pass" = nullable, no C# default — pass `null` explicitly, by name)

| Controller · signature | Request model | Response envelope → fields read | Error case · accessors | Pagination | Map page |
|---|---|---|---|---|---|
| `client.ProductFamilies.ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — 5 must-pass, all `null` here | none | `IReadOnlyList<ProductFamilyResponse>` → `.ProductFamily` (`ProductFamily?`) → `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?`. Match `Handle == Maxio:ProductFamilyHandle`, keep `Id` | **B** `SdkException<RawError>` | none | `operations/ProductFamilies.md`, `records-3-Of-Su.md` |
| `client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — pass resolved id as `productFamilyId: id.ToString()`; the 8 filter params `null`; `perPage: 100` | none | `IReadOnlyList<ProductResponse>` → `.Product` (`Product`, **required non-null**) → `Handle (handle): string?`, `Name (name): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?` | **A** `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page`/`perPage` | `operations/ProductFamilies.md`, `records-3-Of-Su.md` |
| `client.Customers.ReadCustomerByReference(string reference, CancellationToken ct = default)` — `reference` = eShop user id from JWT | none | `CustomerResponse` → `.Customer` (`Customer`, **required**) → `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?` | **B** `SdkException<RawError>` — **404 = customer absent → create**; check `ex.Error.StatusCode == HttpStatusCode.NotFound` | none | `operations/Customers.md`, `records-2-Cr-Ne.md` |
| `client.Customers.CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — body must-pass | `CreateCustomerRequest { Customer = new CreateCustomer { FirstName = …, LastName = …, Email = …, Reference = userId } }`. `CreateCustomer` required: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`; optional used: `Reference (reference): string?` | `CustomerResponse.Customer.Id` | **A** `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. Server enforces `reference` uniqueness (op notes) ⇒ 422 on race → re-read by reference | none | `operations/Customers.md`, `records-1-Ac-Cr.md` |
| `client.Subscriptions.CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — body must-pass | `CreateSubscriptionRequest { Subscription = new CreateSubscription { ProductHandle = handle, CustomerId = id, Reference = $"eshop-{userId}-{handle}" } }`. All `CreateSubscription` fields nullable ⇒ **payment omittable**: `PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes` all `?` — omit entirely (seeded plans require no card). Product by **handle**: `ProductHandle (product_handle): string?` (alternative `ProductId (product_id): int?` unused). `CustomerId (customer_id): int?` (alternative `CustomerReference (customer_reference): string?` unused). `Reference (reference): string?` optional, enables `FindSubscription` later | `SubscriptionResponse` → `.Subscription` (`Subscription?` — **nullable, null-check**) → `Id (id): int?`, `State (state): SubscriptionState?`, `Product (product): Product?` → `.Handle`, `.Name`, `ProductPriceInCents (product_price_in_cents): long?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?` (next billing/renewal timestamp), `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` | **A** `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] → `Errors (errors): IReadOnlyList<string>` (required) · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md`, `records-2-Cr-Ne.md`, `records-3-Of-Su.md` |
| `client.Customers.ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — customer filter is the **path param**; note `Subscriptions.ListSubscriptions` has **no** customer-id filter param (map signature) — do not use it for "my subscriptions" | none | `IReadOnlyList<SubscriptionResponse>` → same `Subscription` fields as above (`State`, `Product.Handle/Name`, `NextAssessmentAt`, `ProductPriceInCents`) | **B** `SdkException<RawError>` | none | `operations/Customers.md`, `records-3-Of-Su.md` |

Family-handle resolution note: `ListProductsForProductFamily` takes `productFamilyId` as `string`, but the `handle:<name>` path format is documented in the map only on `ReadProductFamily`'s notes — passing `"handle:eshop-subscribe"` here is `UNVERIFIED`. Use the grounded resolve-first pattern (Step 1): `ListProductFamilies` → match `Handle` → `Id.ToString()`. Cache the resolved id (config-change-safe: key the cache by handle).

### 2.3 Enums actually needed (`MaxioAdvancedBilling.Models.Enums`; `StringEnum<T>` — static members or `Type.FromValue("wire")`, never C# enum syntax)

| Enum | Members `CSharpName (wire_value)` | Map page |
|---|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` | `models/enums.md` |
| `IntervalUnit` (product price interval unit) | `Day (day)`, `Month (month)` | `models/enums.md` |

Dedupe check (Step 3): treat `Active` and `Trialing` as "already subscribed" live states (both seeded plans have no trial, so expect `Active`).

### 2.4 Error handling (throw-only SDK — no `…Result` variants exist)

| Fact | Value | Source |
|---|---|---|
| Thrown type | `SdkException<TError>` — `sealed`, derives **directly from `System.Exception`**; `required TError Error { get; init; }`. **There is no non-generic `ApiException` base** — catch each operation's `SdkException<…>` specifically (or a shared helper); a blanket `catch (Exception)` is the only common root | SDK source `Core/Exceptions/SdkException.cs` (map gap resolved from source) |
| Case A base | typed errors derive `ApiError` → `TryGetRawError(out RawError)` fallback on every typed error | `sdk-map.md` |
| `RawError` members | `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` · `ReadAsBytes(): ReadOnlyMemory<byte>` | `sdk-map.md` |
| Case A ops here | `CreateCustomer` → `SdkException<CreateCustomerError>` (`TryGetCustomerErrorResponse1` [422]); `CreateSubscription` → `SdkException<CreateSubscriptionError>` (`TryGetErrorListResponse1` [422]); `ListProductsForProductFamily` → `SdkException<ListProductsForProductFamilyError>` (`TryGetString` [404]) | operation rows above |
| Case B ops here | `ListProductFamilies`, `ReadCustomerByReference`, `ListCustomerSubscriptions` → `SdkException<RawError>` (status via `.Error.StatusCode`, body via `.Error.ReadAsString()`) | operation rows above |

⚠ **`CustomerErrorResponse1` payload mismatch (source-verified):** its `Errors (errors)` is typed `Errors`, whose only fields are `PerPage (per_page)` and `PricePoint (price_point)` — clearly not customer-validation keys. The generated model will drop real customer error fields on deserialize. **Directive:** on `CreateCustomer` 422, extract messages best-effort from the typed payload, but treat `TryGetRawError(out var raw)` + `raw.ReadAsString()` as the authoritative message source. Whether the live 422 wire body matches either shape is `UNVERIFIED` (only live traffic confirms).

### 2.5 Idempotency (map evidence: no idempotency-key parameter exists on any in-scope signature)

- **Customer:** lookup-then-create. `ReadCustomerByReference(userId)` → on `SdkException<RawError>` with 404 → `CreateCustomer` with `Reference = userId`. Server enforces one customer per `reference` (CreateCustomer op notes) ⇒ a concurrent double-create loses with 422 → catch and re-read by reference. Net effect: double-click/retry never creates two customers.
- **Subscription:** dedupe-then-create. `ListCustomerSubscriptions(customerId)` → if any subscription has `Product.Handle == target` and `State` ∈ {`Active`, `Trialing`} → return it (200, no create). Else `CreateSubscription`. Set `CreateSubscription.Reference` to an app-derived value (`eshop-{userId}-{productHandle}`) so the subscription is later findable via `FindSubscription(reference)` if needed.
- Retry-layer duplication of `POST /subscriptions` is a separate transport-level hazard — see trap notes (Step 4).

## 3. Trap notes (hazards the signatures hide — load the named skill; do not code from the note alone)

> ⚠ Step 0 (client registration) — the `HttpClient`/handler pipeline behind the client has lifetime rules (long-lived, factory-managed) that `new MaxioAdvancedBillingClient(httpClient, …)` does not convey; the SDK wrapper and the `HttpClient` have different lifetime expectations. **MUST load `dotnet-client-initialization`** before writing DI registration.

> ⚠ Step 0 (auth) — when credentials must be set relative to client construction, and how to pull the key from configuration, is not visible from the options shape. **MUST load `dotnet-authentication`**.

> ⚠ Steps 1–5 (every call) — list/search operations carry many nullable parameters with **no C# default**; positional calls mis-bind. Call with named arguments only. **MUST load `dotnet-calling-endpoints`**.

> ⚠ Steps 1–5 (models) — enums are `StringEnum<T>` records, not C# enums: how to compare `sub.State` to `SubscriptionState.Active` correctly, and the fact that unmodeled JSON fields are silently dropped on deserialize (bit us on `CustomerErrorResponse1`, §2.4), come from the skill, not the signature. **MUST load `dotnet-models`**.

> ⚠ Step 6 (error boundary) — which operations are Case A vs Case B is per-operation (table above); `TryGetRawError` is not a catch-all on typed errors; and `System.Text.Json.JsonException` reaches the boundary from two directions needing opposite handling (rows in §4). **MUST load `dotnet-error-handling`** before writing any `try/catch`.

> ⚠ Step 4 (create-subscription resilience) — whether a failed `POST /subscriptions` can be re-sent by the SDK's retry layer (and what `RetryOptions.Timeout` actually bounds) determines whether the dedupe check in Step 3 is the only duplicate defense. **MUST load `dotnet-configuration-resilience`** before tuning `options.Retry` or relying on defaults.

> ⚠ Step 7 (tests) — the test seam for stubbing the SDK (which constructor argument to fake) and matching the repo's existing test style. **MUST load `dotnet-testing`**.

## 4. File layout, DI, build order (follow eShopOnWeb layering; adjust names to existing conventions)

| Layer | File | Contents |
|---|---|---|
| `src/ApplicationCore` | `Settings/MaxioOptions.cs` (or existing settings location) | `ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl` (optional) — bound from `Maxio:*` |
| `src/ApplicationCore` | `Interfaces/IMaxioBillingService.cs` | app-facing abstraction: `ListPlans`, `EnsureCustomer`, `Subscribe`, `ListMySubscriptions` — returns app DTOs, **no SDK types leak** |
| `src/Infrastructure` | `Services/MaxioBillingService.cs` | SDK-backed implementation (only project referencing the NuGet package); holds resolved family-id cache |
| `src/Infrastructure` | `DependencyInjection.cs` (existing pattern) | `AddMaxioAdvancedBillingClient(o => …)` wired from `MaxioOptions` per §2.1; register `MaxioBillingService` |
| `src/PublicApi` | endpoints file per project convention | `GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions` — minimal-API, JWT-authenticated, user id from claims; map service results/errors to HTTP |

Build/verify order: (1) `dotnet add package AsadAli.AdvancedBilling.Sdk --version 1.0.2` on `src/Infrastructure` → build; (2) options + DI registration → build; (3) service implementation → build; (4) endpoints → build; (5) service tests against the fake seam → `dotnet test`; (6) live sandbox smoke (`cp-exp-3`) last, by whoever runs the app.

## 5. REQUIRED READING (load **before implementation starts** — this sheet deliberately does not carry their contents)

- `dotnet-client-initialization` — Step 0, client construction & DI lifetime.
- `dotnet-authentication` — Step 0, Basic credentials wiring.
- `dotnet-calling-endpoints` — Steps 1–5, named-argument calling, envelopes, async/`ct`.
- `dotnet-models` — Steps 1–5, `StringEnum<T>` handling, required members, dropped unmodeled fields.
- `dotnet-error-handling` — Step 6, the exception boundary. Mandatory in every integration:
  - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
  - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

  **MUST load `dotnet-error-handling`** before writing that boundary.
- `dotnet-configuration-resilience` — Step 4, retry/timeout semantics before touching `options.Retry`.
- `dotnet-testing` — Step 7, faking the SDK seam.

## 6. Assumptions & Blockers

**Assumptions**
- Package version `1.0.2` (map stamp, tag `v1.0.2`); a different version invalidates names until re-grounded.
- Sandbox site `cp-exp-3` is US-hosted ⇒ `ServerEnvironment.Us`; EU hosting would flip both the enum and the override path (`.Eu.*`).
- The eShopOnWeb JWT carries a stable user id usable verbatim as Maxio customer `reference`; email/name for `CreateCustomer` required fields come from the user's profile/claims.
- `NextAssessmentAt (next_assessment_at)` is the field surfaced as "next billing date" (with `CurrentPeriodEndsAt` as period end); both are nullable on the model.
- `Product.PriceInCents`/`Interval`/`IntervalUnit` are populated on list responses for the seeded products — `UNVERIFIED` against live wire (generated model permits nulls; code defensively).
- Metered component `api-call` excluded (not trivial to the hero flow; separate plan via `SubscriptionComponents` if wanted).

**Blockers** — none.
