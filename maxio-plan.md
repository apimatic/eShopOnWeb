# Maxio Advanced Billing integration plan — eShopOnWeb `src/PublicApi`

SDK: `AsadAli.AdvancedBilling.Sdk` (namespace `MaxioAdvancedBilling`), v1.0.2. Target: .NET 8 (SDK rolls forward on .NET 10 SDK).

## 1. Scope & sequence

| Step | Work | SDK operations used |
|---|---|---|
| 1 | Add package to `src/PublicApi`; bind `Maxio:` config section; register client in DI | none (construction only) |
| 2 | `GET /api/subscription-plans` — resolve family by handle, list plans, map to DTO | `ListProductFamilies`, `ListProductsForProductFamily` |
| 3 | `POST /api/subscriptions` — idempotent customer resolve/create, then idempotent subscription find/create | `ReadCustomerByReference`, `CreateCustomer`, `FindSubscription`, `CreateSubscription` |
| 4 | `GET /api/my-subscriptions` — resolve customer, list subscriptions | `ReadCustomerByReference`, `ListCustomerSubscriptions` |
| 5 | Confirmation payload (plan / price / state / next-billing-date) | reads from `SubscriptionResponse.Subscription` |
| 6 | Error boundary (exception translation) for all three endpoints | all of the above |
| 7 | Tests | HttpClient seam |

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal
> C# identifier. The cancellation-token parameter really is named `ct`: in named
> arguments write `ct:`, never `cancellationToken:`.**

> **Every SDK type is written fully-qualified with the namespace the map gives it** — take
> each one from that type's own map row, never from where a neighbouring type sits. A members
> table names the namespace outright; otherwise the row's source path implies it
> (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root
> namespace). Enums, unions, auth, server and client-config types are spread across different
> child namespaces, and two types configured side by side in the same options object routinely
> live in different ones. Dropping a type to the root or to `.Models` makes the implementer
> guess the wrong `using`, and the build breaks.

### Operations

| Controller property | Method signature (params in order) | Request model + fields | Response envelope + fields read | Error case + accessors + payload | Pagination | Source |
|---|---|---|---|---|---|---|
| `client.ProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — 5 nullable params, no default → **pass `null` explicitly** | none | `IReadOnlyList<ProductFamilyResponse>`; read `.ProductFamily.Handle` and match against `Maxio:ProductFamilyHandle` | **B** — `SdkException<RawError>`: `StatusCode` · `ReadAsString()` · `ReadAsJson<T>()` | none | operations/ProductFamilies.md |
| `client.ProductFamilies` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 nullable params, no default → **pass `null` explicitly**; `productFamilyId` accepts the id **or the handle prefixed with `handle:`** (source-confirmed) | none | `IReadOnlyList<ProductResponse>`; read `.Product.Handle`, `.Product.Name`, `.Product.PriceInCents`, `.Product.Interval`, `.Product.IntervalUnit`, `.Product.Description` | **A** — `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage` | operations/ProductFamilies.md |
| `client.Customers` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | none | `CustomerResponse`; read `.Customer.Id`, `.Customer.Reference` | **B** — `SdkException<RawError>`; 404 = `StatusCode == HttpStatusCode.NotFound` | none | operations/Customers.md |
| `client.Customers` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly** | `CreateCustomerRequest` → `Customer (customer): CreateCustomer !req`. `CreateCustomer`: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?`, `Organization?`, `Phone?`, `Locale?` | `CustomerResponse`; read `.Customer.Id` | **A** — `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | operations/Customers.md |
| `client.Subscriptions` | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` nullable, no default → **must pass explicitly** | none | `SubscriptionResponse`; read `.Subscription.Id` | **A** — `SdkException<FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback] | none | operations/Subscriptions.md |
| `client.Subscriptions` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly** | `CreateSubscriptionRequest` → `Subscription (subscription): CreateSubscription !req`. `CreateSubscription` (all fields optional in the model; Notes make the pairings required): `ProductHandle (product_handle): string?` — Notes: required unless `product_id` given; `CustomerId (customer_id): int?` / `CustomerReference (customer_reference): string?` — Notes: one of these or `customer_attributes` required; `Reference (reference): string?` — the app's own id for the subscription; `ProductPricePointHandle?`, `ProductPricePointId?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `Components?`, `Metafields?` | `SubscriptionResponse`; read `.Subscription` (see confirmation row) | **A** — `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | operations/Subscriptions.md |
| `client.Subscriptions` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` nullable, no default → **pass `null`** | none | `SubscriptionResponse`; read `.Subscription.State`, `.ProductPriceInCents`, `.CurrentPeriodEndsAt`, `.NextAssessmentAt`, `.Product` | **B** — `SdkException<RawError>` | none | operations/Subscriptions.md |
| `client.Customers` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | none | `IReadOnlyList<SubscriptionResponse>`; read each `.Subscription` | **B** — `SdkException<RawError>` | none | operations/Customers.md |

Notes:
- **Finding the family by handle**: `ReadProductFamily(int id, …)` takes an `int` — the `handle:my-family` format its Notes mention is **not** reachable through that method. Use `ListProductFamilies` + client-side `ProductFamily.Handle` filter, or skip family resolution entirely and call `ListProductsForProductFamily("handle:" + Maxio:ProductFamilyHandle, …)` directly (the `productFamilyId` param accepts the `handle:` prefix — source-confirmed).
- **Single-plan lookup alternative** (optional validation before subscribing): `client.Products.ReadProductByHandle(string apiHandle, CancellationToken ct = default)` → `ProductResponse`, Case B. Source: operations/Products.md.
- **Subscription `reference` uniqueness is NOT documented provider-side** (unlike customer `reference`, whose uniqueness the CreateCustomer Notes state). The app's own concurrency control is what guarantees "never two subscriptions" — see §5.

### Confirmation payload (step 5) — from `SubscriptionResponse.Subscription`

| Confirmed item | Field (wire_name): type | Notes | Source |
|---|---|---|---|
| Plan | `.Product.Handle` / `.Product.Name` (nested `Product (product): Product?`) | nested object presence in list responses is `UNVERIFIED` — extract best-effort, fall back to the requested plan handle | records-3-Of-Su.md |
| Price | `.ProductPriceInCents (product_price_in_cents): long?` | recurring amount in cents; may differ from the product's current price if the subscription is on an older version | records-3-Of-Su.md |
| State | `.State (state): SubscriptionState?` | enum — values below | records-3-Of-Su.md |
| Next billing date | `.CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` | documented as "when the next regularly scheduled attempted charge will occur"; `.NextAssessmentAt (next_assessment_at): DateTimeOffset?` is the retry timestamp and diverges after a failed renewal | records-3-Of-Su.md |

### Enums (namespace `MaxioAdvancedBilling.Models.Enums`)

| Enum | C# member (wire value) | Source |
|---|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` | map/models/enums.md |
| `IntervalUnit` | `Day (day)`, `Month (month)` | map/models/enums.md |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` | map/models/enums.md |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` | map/models/enums.md |
| `ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)` | map/models/enums.md |
| `SubscriptionInclude` | `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)` | map/models/enums.md |

### Error payload shapes (namespace `MaxioAdvancedBilling.Models`)

| Type | Shape | Source |
|---|---|---|
| `CustomerErrorResponse1` | `Errors (errors): Errors?` | records-2-Cr-Ne.md |
| `Errors` | `PerPage (per_page): IReadOnlyList<string>?`, `PricePoint (price_point): IReadOnlyList<string>?` — **shared model**: a duplicate-`reference` 422 body's message fields are NOT modeled here, so read the real message via `TryGetRawError(out RawError).ReadAsString()` | records-2-Cr-Ne.md |
| `ErrorListResponse1` | `Errors (errors): IReadOnlyList<string> !req` | records-2-Cr-Ne.md |

### Client construction & DI

| Fact | Value | Source |
|---|---|---|
| Constructor | `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` — the only constructor | sdk-map.md |
| DI | `services.AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>? configure = null)` — namespace `MaxioAdvancedBilling`; registers the client as a **singleton** over an `IHttpClientFactory`-created `HttpClient`; the options object is captured once at registration | ServiceCollectionExtensions.cs |
| Options | `Environment: ServerEnvironment` · `Retry: RetryOptions` · `Server: ServerOptions` · `BasicAuth: BasicAuthCredentials?` | sdk-map.md |
| Auth | `BasicAuth = new BasicAuthCredentials { Username = <api_key>, Password = "x" }` — namespace `MaxioAdvancedBilling.Core.Authentication.Basic` | sdk-map.md |
| Environment | `ServerEnvironment.Us` (default) / `ServerEnvironment.Eu` — namespace `MaxioAdvancedBilling.Servers` | sdk-map.md |
| Server | `ServerOptions` (namespace `MaxioAdvancedBilling`): `Production.Us.Site` (default `"subdomain"`), `Production.Us.BaseUrl` (default `"https://{site}.chargify.com"`); `ProductionOptions` lives in `MaxioAdvancedBilling.Servers` | sdk-map.md, ProductionOptions.cs |
| Retry | `RetryOptions` — namespace `MaxioAdvancedBilling.Core.Configuration`; all members `required` → start from `RetryOptions.Default()` | sdk-map.md |

### Config binding (`Maxio:` section — bind by these keys, never by invented env-var names)

| Key | Binds to |
|---|---|
| `Maxio:ApiKey` | `BasicAuth.Username` |
| `Maxio:Subdomain` | `Server.Production.Us.Site` |
| `Maxio:ProductFamilyHandle` | the handle used in `handle:`-prefixed arguments (`ListProductsForProductFamily`) and the `ListProductFamilies` client-side filter |
| `Maxio:BaseUrl` (optional) | `Server.Production.Us.BaseUrl` — used **verbatim** as the API base address when set; when absent, the default `https://{site}.chargify.com` template applies with `Site` = subdomain |

## 3. Trap notes

- ⚠ Step 1 (client registration) — the SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register. **MUST load `dotnet-configuration-resilience`** before wiring the client.
- ⚠ Step 1 (client registration) — `AddMaxioAdvancedBillingClient` captures the options once and registers the client as a singleton over an `IHttpClientFactory`-created `HttpClient`; what must stay long-lived and what may be transient is decided there. **MUST load `dotnet-client-initialization`**.
- ⚠ Step 1 (auth) — Maxio is HTTP Basic (username = API key, password = literal `"x"`); credentials must be set before the client is constructed and loaded from configuration, not hardcoded. **MUST load `dotnet-authentication`**.
- ⚠ Steps 2–4 (calls) — call list/search ops with named arguments: many optional parameters have no C# default and mis-bind in a positional call. **MUST load `dotnet-calling-endpoints`**.
- ⚠ Steps 2–5 (models) — enums are `StringEnum<T>`, not C# enums; unions are built with factory methods and read via `TryGet…`; unmodeled JSON fields are dropped on deserialize. **MUST load `dotnet-models`**.
- ⚠ Step 6 (error boundary) — `System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:
  - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
  - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

  **MUST load `dotnet-error-handling`** before writing that boundary.
- ⚠ Step 3 (idempotency) — whether a failed write can be re-sent by the SDK's own retry layer (transport failures are retried on every verb, `POST` included, and no setting disables that) decides how much of the double-click guarantee the app must enforce itself. **MUST load `dotnet-configuration-resilience`**.
- ⚠ Step 7 (tests) — the `HttpClient` constructor argument is the test seam; which seam to fake and how to keep tests independent of SDK internals is covered there. **MUST load `dotnet-testing`**.

## 4. REQUIRED READING

Load **before implementation starts** — the sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, HttpClient ownership/lifetime, DI registration |
| `dotnet-authentication` | Step 1 — Basic auth credentials wiring |
| `dotnet-calling-endpoints` | Steps 2–4 — controller access, named arguments, envelope shapes |
| `dotnet-models` | Steps 2–5 — request building, enums, unions, wire names |
| `dotnet-error-handling` | Step 6 — exception boundary, status/body access, the two `JsonException` traps |
| `dotnet-configuration-resilience` | Steps 1 & 3 — retries/timeouts, base URL, what a timeout bounds, POST re-send |
| `dotnet-testing` | Step 7 — faking the SDK, covering error/edge paths |

## 5. Assumptions & Blockers

**Assumptions**
- `Maxio:BaseUrl`, when set, is bound verbatim to `Server.Production.Us.BaseUrl` (the map's documented override point for the Production group); when absent, `Maxio:Subdomain` feeds `Server.Production.Us.Site` and the default `https://{site}.chargify.com` template applies.
- The eShopOnWeb user's stable identifier (from the JWT) is used as the Maxio customer `reference` and as the subscription `reference`; the exact identity path is the app's own — `YOUR CALL — not in the map`.
- Sandbox facts (payment method NOT required, no trial/setup fee, handles stable, numeric IDs unstable) are `UNVERIFIED` against the map: the map's CreateSubscription Notes say payment may be required depending on product options, and the map cannot confirm live sandbox behavior. The plan relies on the brief's sandbox description; if the provider rejects a no-card create, the 422 path in the error boundary surfaces it.
- Whether `ListCustomerSubscriptions` responses include the nested `Product` object is `UNVERIFIED` (only live traffic can confirm). Directive: extract the plan name from `Subscription.Product` best-effort; fall back to the requested plan handle when null. State, price, and next-billing-date are top-level on `Subscription` and always available.
- Subscription `reference` uniqueness is not documented provider-side (customer `reference` uniqueness is, per CreateCustomer Notes). The app must serialize subscription creation per user (its own lock/DB constraint) to guarantee "never two subscriptions"; the provider is the backstop only for customers.

**Blockers**
- None. (Near-miss resolved: `ReadProductFamily(int id, …)` cannot take a handle, but `ListProductFamilies` + client-side `Handle` filter, and `ListProductsForProductFamily("handle:…")` directly, cover the requirement.)
