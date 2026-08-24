# maxio-plan.md — eShopOnWeb recurring-subscription billing (Maxio Advanced Billing .NET SDK)

Target project: `src/PublicApi` (JWT-authenticated minimal-API endpoints; user identity from the JWT).
SDK: NuGet `AsadAli.AdvancedBilling.Sdk` (root namespace `MaxioAdvancedBilling` — package id ≠ using namespace). Install: `dotnet add package AsadAli.AdvancedBilling.Sdk`. Map stamp: SDK `v1.0.2`, source commit `15db14b` (`sdk-map.md`).

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 1 | Add package; bind config (`Maxio:ApiKey`, `Maxio:SiteSubdomain`, `Maxio:ProductFamilyHandle`, optional `Maxio:BaseUrlOverride`, optional `Maxio:Environment` = `Us`/`Eu`); register the client in DI | — (client construction facts in §2) |
| 2 | `GET /api/subscription-plans` — list plans in the configured product family | `ProductFamilies.ListProductsForProductFamily` |
| 3 | `POST /api/subscriptions` — idempotent subscribe: find-or-create customer by `reference`, then create only if no active subscription | `Customers.ReadCustomerByReference` → (404 ⇒) `Customers.CreateCustomer` → `Customers.ListCustomerSubscriptions` → `Subscriptions.CreateSubscription` |
| 4 | `GET /api/my-subscriptions` — list the user's subscriptions | `Customers.ReadCustomerByReference` (404 ⇒ return empty list) → `Customers.ListCustomerSubscriptions` |

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

### 2a. Namespaces (one `using` per line — C# does not import child namespaces transitively) — `sdk-map.md`

| Namespace | Types used from it |
|---|---|
| `MaxioAdvancedBilling` | `MaxioAdvancedBillingClient`, `MaxioAdvancedBillingClientOptions`, `ServerOptions`, DI ext `AddMaxioAdvancedBillingClient` |
| `MaxioAdvancedBilling.Core.Authentication.Basic` | `BasicAuthCredentials` |
| `MaxioAdvancedBilling.Servers` | `ServerEnvironment`, `ProductionOptions` |
| `MaxioAdvancedBilling.Core.Configuration` | `RetryOptions` (only if tuning retries) |
| `MaxioAdvancedBilling.Core.Exceptions` | `SdkException<TError>` |
| `MaxioAdvancedBilling.Core.ErrorResponse` | `RawError` |
| `MaxioAdvancedBilling.Models` | all records below |
| `MaxioAdvancedBilling.Models.Enums` | `SubscriptionState`, `IntervalUnit` |
| `MaxioAdvancedBilling.Errors` | `CreateCustomerError`, `CreateSubscriptionError`, `ListProductsForProductFamilyError` |

### 2b. Client construction, auth, base URL — `sdk-map.md` (initializers confirmed against SDK source: `MaxioAdvancedBillingClientOptions.cs`, `ServerOptions.cs`, `Servers/ProductionOptions.cs`)

- Only constructor: `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`.
- DI alternative (root namespace): `services.AddMaxioAdvancedBillingClient(o => { … })`.
- `MaxioAdvancedBillingClientOptions` properties: `Environment: ServerEnvironment` (defaults to US), `Retry: RetryOptions` (pre-initialized to defaults), `Server: ServerOptions` (**pre-initialized** — `= new()`), `BasicAuth: BasicAuthCredentials?` (null until set).
- **Auth (Basic)**: `options.BasicAuth = new BasicAuthCredentials { Username = "<Maxio:ApiKey>", Password = "x" };` — username = API key, password = literal `"x"`.
- **Environment**: `options.Environment = ServerEnvironment.Us;` (default) or `ServerEnvironment.Eu`.
- **Site subdomain**: `options.Server.Production.Us.Site = "<Maxio:SiteSubdomain>";` — the `{site}` template param in `https://{site}.chargify.com` (EU: `https://{site}.ebilling.maxio.com`). Every level (`Server`, `.Production`, `.Us`) is pre-initialized — plain assignments, **no `new` anywhere**.
- **Verbatim base-URL override** (when `Maxio:BaseUrlOverride` is set, e.g. a mock/dev host): `options.Server.Production.Us.BaseUrl = "http://localhost:8080";` — replaces the whole URL template verbatim; the `{site}` template param is then simply unused. (EU account: the `.Eu.` equivalents + `ServerEnvironment.Eu`.)

### 2c. Operations (one row per operation; map page cited per row)

| Controller · operation | Signature (params in order; **bold** = must pass explicitly, pass `null` to skip) | Request model | Response envelope → fields read | Error case + accessors | Pagination |
|---|---|---|---|---|---|
| `client.ProductFamilies` · `ListProductsForProductFamily` (`operations/ProductFamilies.md`) | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 nullable params (`dateField`…`include`) have **no C# default → pass explicitly** (use named args, `null` to skip) | none (GET). `productFamilyId` accepts **either the numeric id or the handle prefixed with `handle:`** — pass `"handle:" + Maxio:ProductFamilyHandle` (e.g. `"handle:eshop-subscribe"`); no family-id lookup needed (confirmed in the SDK source doc comment for this parameter) | `IReadOnlyList<ProductResponse>` → per item `.Product` (`Product`, required) → `Handle`, `Name`, `PriceInCents`, `Interval`, `IntervalUnit` | **Case A**: `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404, family not found] · `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage` (defaults 1/20; `perPage` max 200). Loop `page` until a short/empty page if the family can exceed one page |
| `client.Customers` · `ReadCustomerByReference` (`operations/Customers.md`) | `ReadCustomerByReference(string reference, CancellationToken ct = default)` (query param `reference`) | none (GET). `reference` = the eShopOnWeb user identity/username | `CustomerResponse` → `.Customer` (`Customer`, required) → `Id (id): int?`, `Reference`, `Email`, `FirstName`, `LastName` | **Case B**: `SdkException<RawError>` — `ex.Error.StatusCode` (404 = customer does not exist ⇒ create), `ReadAsString()` | none |
| `client.Customers` · `CreateCustomer` (`operations/Customers.md`) | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` **must be passed explicitly** | `CreateCustomerRequest { Customer = new CreateCustomer { … } }` — `CreateCustomer.Customer (customer)` is **required**. `CreateCustomer` fields: `FirstName (first_name): string` **required**, `LastName (last_name): string` **required**, `Email (email): string` **required**, `Reference (reference): string?` (set to the same stable user identity used in the lookup; unique per customer) | `CustomerResponse` → `.Customer` → `Id` | **Case A**: `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] (payload: `.Errors` of type `Errors?` — record with `PerPage`, `PricePoint` string-list fields) · `TryGetRawError(out RawError)` [fallback] | none |
| `client.Customers` · `ListCustomerSubscriptions` (`operations/Customers.md`) | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | none (GET path param) | `IReadOnlyList<SubscriptionResponse>` → per item `.Subscription` (`Subscription?` — **nullable**, null-check) → `Id`, `State`, `Product?.Name` / `Product?.Handle`, `ProductPriceInCents`, `CurrentBillingAmountInCents`, `Currency`, `NextAssessmentAt`, `CurrentPeriodEndsAt` | **Case B**: `SdkException<RawError>` — `StatusCode`, `ReadAsString()` | none (full list returned) |
| `client.Subscriptions` · `CreateSubscription` (`operations/Subscriptions.md`) | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` **must be passed explicitly** | `CreateSubscriptionRequest { Subscription = new CreateSubscription { … } }` — `Subscription (subscription)` is **required**. `CreateSubscription` fields used: `ProductHandle (product_handle): string?` (the plan handle — alternative `ProductId (product_id): int?`), `CustomerId (customer_id): int?` (from the find-or-create — alternative `CustomerReference (customer_reference): string?`). All other fields nullable/omittable; plans here need no payment profile, trial, or setup fields | `SubscriptionResponse` → `.Subscription` (`Subscription?` — null-check) → `Id`, `State`, `Product`, `ProductPriceInCents`, `NextAssessmentAt`, `CurrentPeriodEndsAt` | **Case A**: `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] (payload: `.Errors: IReadOnlyList<string>` required — join for the message) · `TryGetRawError(out RawError)` [fallback] | none |

**Negative contract facts (settled, do not look for alternatives):**

- `Subscriptions.ListSubscriptions` has **no customer-id filter** (its 14 filter params are state/product/coupon/date/metadata/sort only) — "list subscriptions for a customer" is `Customers.ListCustomerSubscriptions`. (`operations/Subscriptions.md`)
- The `Subscription` read model has **no `NextBillingAt` field** — "next billing date" is `NextAssessmentAt (next_assessment_at): DateTimeOffset?` and/or `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`. (`records-3-Of-Su.md`)
- There is **no `ErrorListResponseException` and no `ApiException`** in this SDK — every error path is `SdkException<TError>` with `.Error`. (`sdk-map.md`)
- No-throw `…Result`/`ApiResult` variants: **absent across the SDK** — every operation is throw-only; wrap every call. (`sdk-map.md`)

### 2d. Response/request model details — fields the integration reads or sets

| Model (namespace `MaxioAdvancedBilling.Models`) | Fields (C# name (wire_name): type) | Map page |
|---|---|---|
| `ProductResponse` | `Product (product): Product` **required** — reads go one level down | `records-3-Of-Su.md` |
| `Product` | `Id (id): int?` · `Name (name): string?` · `Handle (handle): string?` · `PriceInCents (price_in_cents): long?` · `Interval (interval): int?` · `IntervalUnit (interval_unit): IntervalUnit?` · `ArchivedAt (archived_at): DateTimeOffset?` (skip archived plans when listing) | `records-3-Of-Su.md` |
| `CustomerResponse` | `Customer (customer): Customer` **required** | `records-2-Cr-Ne.md` |
| `Customer` | `Id (id): int?` · `Reference (reference): string?` · `FirstName (first_name): string?` · `LastName (last_name): string?` · `Email (email): string?` | `records-2-Cr-Ne.md` |
| `CreateCustomerRequest` → `CreateCustomer` | envelope `Customer (customer): CreateCustomer` **required**; then `FirstName`/`LastName`/`Email` **required `string`**, `Reference (reference): string?` | `records-1-Ac-Cr.md` |
| `CreateSubscriptionRequest` → `CreateSubscription` | envelope `Subscription (subscription): CreateSubscription` **required**; then `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `CustomerId (customer_id): int?`, `CustomerReference (customer_reference): string?` | `records-2-Cr-Ne.md` |
| `SubscriptionResponse` | `Subscription (subscription): Subscription?` — **nullable** (unlike the other envelopes) | `records-4-Su-We.md` |
| `Subscription` | `Id (id): int?` · `State (state): SubscriptionState?` · `Product (product): Product?` · `Customer (customer): Customer?` · `ProductPriceInCents (product_price_in_cents): long?` · `CurrentBillingAmountInCents (current_billing_amount_in_cents): long?` · `Currency (currency): string?` · `NextAssessmentAt (next_assessment_at): DateTimeOffset?` · `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` | `records-3-Of-Su.md` |
| `ErrorListResponse1` (422 payload of `CreateSubscription`) | `Errors (errors): IReadOnlyList<string>` **required** | `records-2-Cr-Ne.md` |
| `CustomerErrorResponse1` (422 payload of `CreateCustomer`) | `Errors (errors): Errors?` — `Errors` record: `PerPage (per_page): IReadOnlyList<string>?`, `PricePoint (price_point): IReadOnlyList<string>?` | `records-2-Cr-Ne.md` |

Records are immutable with `init`-only setters; `required` members must be set in the object initializer. No field the integration touches is a union — no `TryGet…`/factory reads needed on this path.

### 2e. Enum values — namespace `MaxioAdvancedBilling.Models.Enums` (`map/models/enums.md`)

These are `StringEnum<T>` records, **not C# enums** — use the static members below (literal C# identifiers; wire value in parens).

| Enum | Members (wire values) |
|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `IntervalUnit` | `Day (day)`, `Month (month)` |

Idempotency check for step 3: an existing subscription blocks creation when its `State` is `SubscriptionState.Active` (plans have no trial, so `Trialing` should not occur; widen the blocking set — e.g. `PastDue`, `Unpaid`, `OnHold` — per product decision).

### 2f. Error-handling model (`sdk-map.md`)

- All operations throw `SdkException<TError>` (`MaxioAdvancedBilling.Core.Exceptions`); the payload is `ex.Error` of type `TError`.
- **Case A** (typed): `TError` = `{Operation}Error : ApiError` with status-specific `TryGet…(out …)` accessors (return `true` when that shape is present) plus inherited `TryGetRawError(out RawError)` fallback for any other status. In scope: `CreateCustomer` → `TryGetCustomerErrorResponse1` [422]; `CreateSubscription` → `TryGetErrorListResponse1` [422]; `ListProductsForProductFamily` → `TryGetString` [404].
- **Case B** (raw): `TError` = `RawError` (`MaxioAdvancedBilling.Core.ErrorResponse`): `StatusCode: HttpStatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. In scope: `ReadCustomerByReference` (404 ⇒ customer missing ⇒ create), `ListCustomerSubscriptions`.
- Find-or-create race: two concurrent first-time requests can both 404 on the lookup and both create — the second `CreateCustomer` then fails 422 (`reference` must be unique). Treat a 422 on `CreateCustomer` as "re-run `ReadCustomerByReference` and continue".

## 3. Trap notes

- ⚠ Step 1 (client registration) — the `HttpClient`/handler pipeline behind the SDK has ownership and lifetime rules (who disposes what, and why per-request construction breaks); the SDK wrapper's lifetime differs from the pipeline's. **MUST load `dotnet-client-initialization`** before writing `new MaxioAdvancedBillingClient(...)` or the DI registration.
- ⚠ Step 1 (auth) — when credentials must be set relative to client construction, and where the API key comes from (configuration, not source), are not visible from the options shape. **MUST load `dotnet-authentication`** before wiring `BasicAuthCredentials`.
- ⚠ Steps 2–4 (every call) — several operations take nullable parameters with **no C# default** (`ListProductsForProductFamily` has 8); positional calls mis-bind, and list/search ops want named arguments. **MUST load `dotnet-calling-endpoints`** before the first call.
- ⚠ Steps 2–4 (models) — enums are `StringEnum<T>` records (how a read `State` is compared to `SubscriptionState.Active`, and how `IntervalUnit` is rendered, is governed here, not by C#-enum intuition); records are immutable with `required` init members; JSON fields with no modeled property are dropped on deserialize. **MUST load `dotnet-models`** before constructing payloads or mapping responses.
- ⚠ Steps 3–4 (error boundary) — which operations are Case A vs Case B is per-operation (table above), `TryGetRawError` is not a catch-all on typed errors, and two `JsonException` paths bypass/replace `SdkException` entirely (see REQUIRED READING). **MUST load `dotnet-error-handling`** before writing any `try/catch`.
- ⚠ Step 3 (`CreateSubscription` is a non-idempotent POST) — whether a failed write can be re-sent by the SDK's retry layer, what the `Timeout` option actually bounds, and what remains for you to wire are not answerable from the option names. **MUST load `dotnet-configuration-resilience`** before tuning or relying on retries/timeouts.
- ⚠ Tests — the seam to fake for SDK-calling code, and how to cover error/edge paths without depending on SDK internals. **MUST load `dotnet-testing`** before writing tests for the integration layer.

## 4. REQUIRED READING

Load **before implementation starts**; this sheet deliberately does not carry their contents.

- `dotnet-client-initialization` — step 1: client construction, `HttpClient` ownership, DI registration.
- `dotnet-authentication` — step 1: Basic credentials wiring.
- `dotnet-calling-endpoints` — steps 2–4: calling every operation above.
- `dotnet-models` — steps 2–4: request/response models, `StringEnum<T>` enums.
- `dotnet-error-handling` — steps 3–4: the exception boundary (mandatory in every integration).
- `dotnet-configuration-resilience` — step 3: retries/timeouts around a non-idempotent POST; base-URL/server selection.
- `dotnet-testing` — tests for the integration layer.

Always-true hazard rows for the error boundary — `System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:

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

**Assumptions**

- The Maxio customer `reference` is the eShopOnWeb user's stable identity/username string from the JWT; the same value is used for `ReadCustomerByReference` and `CreateCustomer.Reference`.
- `CreateCustomer` requires `FirstName`, `LastName`, **and** `Email` (all `required string`). The brief only names first/last name, email, reference — assumed all three are obtainable from JWT claims or the user's profile; if the JWT carries only a username, the endpoint must derive/placeholder the missing names.
- "Active subscription" for the idempotency check means `Subscription.State == SubscriptionState.Active` (plans have no trial); widening the blocking set is a product decision left to the implementer.
- Plans need no payment method per the brief, so `CreateSubscription` sends only `ProductHandle` + `CustomerId`. If a product is later configured to require payment, the API rejects with 422 — surfaced via `ErrorListResponse1.Errors`.
- `GET /api/my-subscriptions` returns an empty list (not an error) when the user has no Maxio customer yet (lookup 404).
- `UNVERIFIED`: `ListProductsForProductFamily`'s `productFamilyId` accepting the `"handle:…"` prefix is documented in the generated SDK source's own parameter doc comment, but only live traffic can confirm the server honors it. Defensive directive: on the 404 branch (`TryGetString`), fall back to `ProductFamilies.ListProductFamilies(null, null, null, null, null)` (Case B; returns `IReadOnlyList<ProductFamilyResponse>` → `.ProductFamily` → `Id`/`Handle`), match `Handle` client-side, and retry with the numeric id.

**Blockers** — none.
