# Maxio Advanced Billing integration plan — eShopOnWeb recurring subscriptions

Grounded against the bundled SDK map (`sdk-map.md`, source stamp `15db14b` / tag `v1.0.2`) with two
targeted source confirmations (`Api/ProductFamilies.cs`, `Models/Errors.cs`). Every row cites its map page.

## 1. Scope & sequence

Additive recurring-subscription billing on `src/PublicApi` (JWT-authenticated endpoints), Maxio as billing
system of record. Existing one-time cart/checkout untouched.

| # | Step | Operations used |
|---|---|---|
| 1 | Client registration & config (`Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, optional `Maxio:BaseUrl`) | — (client construction only) |
| 2 | `GET /api/subscription-plans` — list plans in the configured family | `ProductFamilies.ListProductsForProductFamily` |
| 3 | `POST /api/subscriptions` — idempotent subscribe-by-handle | `Customers.ReadCustomerByReference` → `Customers.CreateCustomer` → `Subscriptions.FindSubscription` → `Subscriptions.CreateSubscription` |
| 4 | `GET /api/my-subscriptions` — current user's subscriptions | `Customers.ReadCustomerByReference` → `Customers.ListCustomerSubscriptions` |

Resolve everything by **handle**; never hard-code numeric IDs. Sandbox: subdomain `cp-exp-1`, family
handle `eshop-subscribe`, products `eshop-pro` / `basic-plan`.

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

### SDK identity & client construction (map: `sdk-map.md`)

| Fact | Value |
|---|---|
| NuGet package | `AsadAli.AdvancedBilling.Sdk` (map grounded against tag `v1.0.2`; install latest 1.0.x) |
| Root namespace | `MaxioAdvancedBilling` (≠ package id) |
| Client class | `MaxioAdvancedBillingClient` — sole ctor `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` |
| Options class | `MaxioAdvancedBillingClientOptions` — props: `Environment: ServerEnvironment`, `Retry: RetryOptions`, `Server: ServerOptions`, `BasicAuth: BasicAuthCredentials?` |
| DI alternative | `services.AddMaxioAdvancedBillingClient(o => { … })` (`ServiceCollectionExtensions.cs`) |
| Auth | `o.BasicAuth = new BasicAuthCredentials { Username = <apiKey>, Password = "x" }` — `BasicAuthCredentials` is in `MaxioAdvancedBilling.Core.Authentication.Basic`; password is the literal string `"x"` |
| Environment | `o.Environment = ServerEnvironment.Us` (default) — `ServerEnvironment` is in `MaxioAdvancedBilling.Servers` |
| Subdomain → site | `o.Server.Production.Us.Site = <subdomain>` → base URL `https://{site}.chargify.com` |
| Base-URL override | `o.Server.Production.Us.BaseUrl = <Maxio:BaseUrl>` — verbatim, replaces the derived URL (use when config sets it) |
| Config mapping | `Maxio:ApiKey`→`BasicAuth.Username` · `Maxio:Subdomain`→`Server.Production.Us.Site` · `Maxio:BaseUrl`→`Server.Production.Us.BaseUrl` (when set) · `Maxio:ProductFamilyHandle`→app-side only (no SDK setting) |

### Operations

| Controller property · signature (verbatim) | Request model + fields used | Response envelope + fields read | Error case + accessors | Pagination |
|---|---|---|---|---|
| `client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 nullable params (`dateField`…`include`) have no defaults → **must pass explicitly** (pass `null`); use named args (map: `operations/ProductFamilies.md`) | none (query only). **`productFamilyId` accepts the family id or its handle prefixed `handle:`** — confirmed in source XML-doc on `Api/ProductFamilies.cs`: pass `"handle:" + Maxio:ProductFamilyHandle` (e.g. `handle:eshop-subscribe`). `ListProductsFilter` has only `Ids`, `PrepaidProductPricePoint`, `UseSiteExchangeRate` — **no handle filter exists** (map: `records-2-Cr-Ne.md`) | `IReadOnlyList<ProductResponse>`; each `ProductResponse.Product (product): Product !req`. Read from `Product`: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?` (map: `records-3-Of-Su.md`) | **Case A** `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page`/`perPage` (default 20; source doc: values over 200 are clamped to 200) |
| `client.Products.ReadProductByHandle(string apiHandle, CancellationToken ct = default)` (map: `operations/Products.md`) | none — `apiHandle` is the product handle (`eshop-pro`, `basic-plan`) | `ProductResponse.Product` as above | **Case B** `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | none |
| `client.Customers.ReadCustomerByReference(string reference, CancellationToken ct = default)` (map: `operations/Customers.md`) | none — `reference` = stable app-side key (e.g. eShopOnWeb username) | `CustomerResponse.Customer (customer): Customer !req`; read `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?` (map: `records-2-Cr-Ne.md`) | **Case B** `SdkException<RawError>` — not-found ⇒ `ex.Error.StatusCode == HttpStatusCode.NotFound` | none |
| `client.Customers.CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default → must pass explicitly (map: `operations/Customers.md`) | `CreateCustomerRequest.Customer (customer): CreateCustomer !req`. `CreateCustomer` fields: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?` ← set it (map: `records-1-Ac-Cr.md`) | `CustomerResponse.Customer`; read `Id` | **Case A** `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. Payload: `CustomerErrorResponse1.Errors (errors): Errors?`; `Errors` has **only** `PerPage (per_page): IReadOnlyList<string>?` and `PricePoint (price_point): IReadOnlyList<string>?` (map `records-2-Cr-Ne.md`, confirmed in `Models/Errors.cs`) — see UNVERIFIED note below | none |
| `client.Subscriptions.FindSubscription(string? reference, CancellationToken ct = default)` — `reference` nullable, no default → must pass explicitly (map: `operations/Subscriptions.md`) | none — query `reference` | `SubscriptionResponse.Subscription (subscription): Subscription?` (nullable — map: `records-4-Su-We.md`) | **Case A** `SdkException<FindSubscriptionError>` — `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback] | none |
| `client.Subscriptions.CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, no default → must pass explicitly (map: `operations/Subscriptions.md`) | `CreateSubscriptionRequest.Subscription (subscription): CreateSubscription !req`. `CreateSubscription` fields used (all optional, map: `records-2-Cr-Ne.md`): `CustomerId (customer_id): int?` **or** `CustomerReference (customer_reference): string?`, `ProductHandle (product_handle): string?` ← subscribe by handle, `Reference (reference): string?` ← set for idempotency (a sibling `Ref (ref): string?` also exists; the lookup endpoint queries `reference`, so use `Reference`). No payment fields needed (products don't require a card) | `SubscriptionResponse.Subscription`; read `Id (id): int?`, `State (state): SubscriptionState?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` | **Case A** `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. Payload: `ErrorListResponse1.Errors (errors): IReadOnlyList<string> !req` (map: `records-2-Cr-Ne.md`) | none |
| `client.Customers.ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` (map: `operations/Customers.md`) | none — `customerId` from the find-or-create step | `IReadOnlyList<SubscriptionResponse>`; per item read `Subscription.Id`, `State`, `NextAssessmentAt`, `CurrentPeriodEndsAt`, `ProductPriceInCents (product_price_in_cents): long?`, and nested `Product (product): Product?` → `Name`, `Handle`, `PriceInCents`, `Interval`, `IntervalUnit` (map: `records-3-Of-Su.md`) | **Case B** `SdkException<RawError>` | none |
| `client.Subscriptions.ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` must be passed explicitly (pass `null`) (map: `operations/Subscriptions.md`) | none | `SubscriptionResponse.Subscription` as above | **Case B** `SdkException<RawError>` | none |

Notes on choices:
- **Plan listing** uses `ListProductsForProductFamily` with `"handle:<family>"` — no numeric family ID needed.
  `ListSubscriptions` has **no customer filter** (its filters are `state`, `product` (int id), `coupon`,
  dates, metadata — map: `operations/Subscriptions.md`), so per-user listing goes through
  `Customers.ListCustomerSubscriptions`.
- **No union/AnyOf accessors are needed** in this scope (`CreateSubscription.OfferId` is a union but unused).

### Enums needed (map: `models/enums.md`; all `StringEnum<T>` — static members, e.g. `SubscriptionState.Active`, not C# enums; namespace `MaxioAdvancedBilling.Models.Enums`)

| Enum | Values (C# member (wire)) |
|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `IntervalUnit` | `Day (day)`, `Month (month)` |
| `SubscriptionStateFilter` (only if list filtering is added) | `Active`, `Canceled`, `Expired`, `ExpiredCards`, `OnHold`, `PastDue`, `PendingCancellation`, `PendingRenewal`, `Suspended`, `TrialEnded`, `Trialing`, `Unpaid` (wires: snake_case) |

### Idempotency (map-grounded)

- **No idempotency-key parameter exists** on `CreateCustomer` or `CreateSubscription` — the map carries the
  full parameter lists and there is none. Idempotency is built on `reference` uniqueness + lookups:
  - Customer: `reference` is unique per site (CreateCustomer notes, `operations/Customers.md`: "you may only
    create one customer for a given reference value"). Find-or-create = `ReadCustomerByReference` → on
    `RawError.StatusCode == 404` → `CreateCustomer` with `Reference` set. On a 422 race (duplicate
    reference), re-`ReadCustomerByReference` and continue.
  - Subscription: set `Reference` on create (e.g. `"{user}:{productHandle}"`); before creating, and after any
    uncertain failure (timeout/transport error), call `FindSubscription(reference)` — 404 via
    `TryGetNoContent` means safe to (re)create; a hit means return the existing one.
  - Neither lookup nor uniqueness replaces app-side serialization of the double-click: keep a per-user
    guard (lock/unique constraint on a local user↔customer mapping) so two concurrent requests don't both
    pass the 404 check.

## 3. Trap notes

> ⚠ Step 1 (client registration) — the `HttpClient`/handler pipeline behind the SDK client has lifetime
> requirements a per-request `new HttpClient()` violates, and the DI helper exists precisely for this.
> **MUST load `dotnet-client-initialization`** before wiring the client.
>
> ⚠ Step 1 (auth) — credentials must be in place before the client is constructed / inside the DI callback,
> and the key belongs in configuration, not code. **MUST load `dotnet-authentication`**.
>
> ⚠ Steps 2–4 (every call) — many optional params have no C# default and mis-bind in positional calls;
> call with named arguments (`ct:` for the token). **MUST load `dotnet-calling-endpoints`**.
>
> ⚠ Steps 2–4 (models) — SDK enums are `StringEnum<T>`, not C# enums, and unmodeled JSON fields are dropped
> on deserialize (matters for the 422 payloads below). **MUST load `dotnet-models`**.
>
> ⚠ Steps 3–4 (error boundary) — Case A vs Case B differs per operation in this plan (see sheet); a
> `SdkException<RawError>`-only ladder loses the typed 422 payloads, and a typed-only ladder misses the
> Case B reads. **MUST load `dotnet-error-handling`**.
>
> ⚠ Step 3 (idempotency vs retries/timeouts) — whether a failed `CreateSubscription` POST may be re-sent by
> the SDK's resilience layer, and what `Timeout` actually bounds, directly affect the "never two
> subscriptions" guarantee; the option names alone do not tell you. **MUST load
> `dotnet-configuration-resilience`** before tuning or relying on retry/timeout behavior.
>
> ⚠ Tests — the SDK's test seam is specific (not an interface over controllers).
> **MUST load `dotnet-testing`** before stubbing the SDK.

## 4. REQUIRED READING

Load **before implementation starts**; this sheet deliberately does not carry their contents.

- `dotnet-client-initialization` — Step 1 (client construction, options, DI registration)
- `dotnet-authentication` — Step 1 (Basic credentials wiring)
- `dotnet-calling-endpoints` — Steps 2–4 (named-argument calls, envelopes)
- `dotnet-models` — Steps 2–4 (records, `StringEnum<T>` enums, nullability)
- `dotnet-error-handling` — Steps 3–4 (the integration's error boundary)
- `dotnet-configuration-resilience` — Step 3 (retry/timeout interaction with idempotency)
- `dotnet-testing` — integration-layer tests

Two hazard rows apply verbatim — `System.Text.Json.JsonException` reaches the boundary from two directions
and they need opposite handling:

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

- **Assumption** — the eShopOnWeb user's stable identity (username) is the customer `reference`.
  `CreateCustomer` requires `FirstName`, `LastName`, `Email` (`!req`); if eShopOnWeb identity carries only
  username/email, the endpoint must derive or placeholder the names — decide before coding Step 3.
- **Assumption** — `Maxio:BaseUrl`, when set, fully replaces the derived `https://{subdomain}.chargify.com`
  (assigned verbatim to `Server.Production.Us.BaseUrl`); when unset, `Site` is used. US environment assumed
  (sandbox `cp-exp-1`); EU accounts would need `ServerEnvironment.Eu` + `.Eu.Site`.
- **Assumption** — "next billing date" in `GET /api/my-subscriptions` maps to
  `Subscription.NextAssessmentAt`, with `CurrentPeriodEndsAt` exposed alongside as period end.
- **UNVERIFIED** — `CreateCustomer`'s 422 payload model (`CustomerErrorResponse1.Errors` → `Errors` with
  only `PerPage`/`PricePoint` lists) does not resemble a customer-validation error shape; the map and the
  generated source agree, but only live traffic can confirm what the wire actually sends. Defensive
  directive: read `TryGetCustomerErrorResponse1` best-effort, then fall back to `TryGetRawError` →
  `ReadAsString()` for the real message; never rely on the typed payload alone (unmodeled JSON fields are
  dropped on deserialize).
- **UNVERIFIED** — whether `CreateSubscription` accepts `CustomerReference` alone (without `CustomerId`) for
  an existing customer is stated in the operation notes ("Identify an existing customer with `customer_id`
  or `customer_reference`", `operations/Subscriptions.md`) but not exercised here; the plan passes
  `CustomerId` from the find-or-create step, which is unambiguous.
- No blockers.
