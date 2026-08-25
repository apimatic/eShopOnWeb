# maxio-plan.md — eShopOnWeb recurring-subscription billing via Maxio Advanced Billing .NET SDK

Additive billing integration on `src/PublicApi` (JWT-authenticated endpoints; caller identity from the token). Maxio is the billing system of record; the existing one-time cart/checkout flow is untouched.

## 1. Scope & sequence

| # | Step | SDK operations used |
|---|------|---------------------|
| 1 | Install package `AsadAli.AdvancedBilling.Sdk` into `src/PublicApi`; bind config section `Maxio:*` (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl` optional override) | — |
| 2 | Register `MaxioAdvancedBillingClient` in DI (auth + server/site or BaseUrl override) | — |
| 3 | `GET /api/subscription-plans` — resolve family `eshop-subscribe` → list its products → map to plan DTOs | `ProductFamilies.ListProductFamilies`, `ProductFamilies.ListProductsForProductFamily` |
| 4 | `POST /api/subscriptions` — ensure-customer (lookup by `reference` = eShopOnWeb user id; create on 404) → idempotency check (existing subscription for customer+product, or `FindSubscription` by deterministic `reference`) → create subscription → return plan/price/state/next-billing | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Customers.ListCustomerSubscriptions`, `Subscriptions.FindSubscription`, `Subscriptions.CreateSubscription` |
| 5 | `GET /api/my-subscriptions` — resolve customer by `reference` → list that customer's subscriptions → map to DTOs | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |
| 6 | Error boundary: translate SDK exceptions to HTTP responses; integration tests with faked HTTP seam | — |

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

### 2.0 SDK identity, client construction, auth, server (source: `sdk-map.md`)

| Fact | Value |
|---|---|
| NuGet package | `AsadAli.AdvancedBilling.Sdk` (map stamped at source tag `v1.0.2`; install latest 1.0.x — `dotnet add package AsadAli.AdvancedBilling.Sdk`) |
| Root namespace | `MaxioAdvancedBilling` (≠ package id) |
| Client class | `MaxioAdvancedBillingClient` (ns `MaxioAdvancedBilling`) — only ctor: `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` |
| Options class | `MaxioAdvancedBillingClientOptions` (ns `MaxioAdvancedBilling`) — properties: `Environment: ServerEnvironment`, `Retry: RetryOptions`, `Server: ServerOptions`, `BasicAuth: BasicAuthCredentials?` |
| Auth | HTTP Basic — `options.BasicAuth = new BasicAuthCredentials { Username = "<MAXIO_API_KEY>", Password = "x" }`. `Username` = API key, `Password` = literal string `"x"`. Type `BasicAuthCredentials` lives in ns `MaxioAdvancedBilling.Core.Authentication.Basic` |
| Environment | `ServerEnvironment.Us` (default) / `ServerEnvironment.Eu`, ns `MaxioAdvancedBilling.Servers`. Map `MAXIO_ENVIRONMENT` onto this |
| Site subdomain | `options.Server.Production.Us.Site = "cp-exp-1"` → base URL `https://cp-exp-1.chargify.com` (`{site}` defaults to `subdomain`) |
| BaseUrl override | When `Maxio:BaseUrl` is set, use it verbatim: `options.Server.Production.Us.BaseUrl = "<value>"` (override point per server group; Production group covers every operation in this scope) |
| DI alternative | `services.AddMaxioAdvancedBillingClient(o => { … })` from `ServiceCollectionExtensions.cs` |
| `RetryOptions` | ns `MaxioAdvancedBilling.Core.Configuration`; all members `required` — start from `RetryOptions.Default()` and mutate |

### 2.1 Operations (one row per operation; map page cited per row)

| Endpoint step | Controller property · signature (verbatim) | Request model | Response envelope | Error case + accessors | Pagination | Map page |
|---|---|---|---|---|---|---|
| Resolve family by handle | `client.ProductFamilies` · `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 nullable params **must be passed explicitly** (pass `null`) | — | `IReadOnlyList<ProductFamilyResponse>` — each wraps `ProductFamily (product_family): ProductFamily?`; match `ProductFamily.Handle == "eshop-subscribe"`, take `Id` | **Case B**: `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | none | `operations/ProductFamilies.md` |
| List plans in family | `client.ProductFamilies` · `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 nullable params **must be passed explicitly**; `productFamilyId` is `string` — pass `familyId.ToString()` | — | `IReadOnlyList<ProductResponse>` — each wraps `Product (product): Product` (**required**, one level down) | **Case A**: `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page`/`perPage` (defaults 1/20) | `operations/ProductFamilies.md` |
| Read single product by handle (optional helper) | `client.Products` · `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | — | `ProductResponse` → `.Product` | **Case B**: `SdkException<RawError>` | none | `operations/Products.md` |
| Ensure customer — lookup | `client.Customers` · `ReadCustomerByReference(string reference, CancellationToken ct = default)` — query wire name `reference`; pass the eShopOnWeb user id | — | `CustomerResponse` → `.Customer` (**required**) | **Case B**: `SdkException<RawError>` — a missing customer is `Error.StatusCode == HttpStatusCode.NotFound`; that is the "create" signal | none | `operations/Customers.md` |
| Ensure customer — create | `client.Customers` · `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, **must pass explicitly** | `CreateCustomerRequest { Customer = new CreateCustomer { … } }` — see 2.2 | `CustomerResponse` → `.Customer` | **Case A**: `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. 422 on duplicate `reference` (race) → re-run the lookup | none | `operations/Customers.md` |
| List a user's subscriptions | `client.Customers` · `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | — | `IReadOnlyList<SubscriptionResponse>` — each wraps `Subscription (subscription): Subscription?` (**nullable** — null-check) | **Case B**: `SdkException<RawError>` | none | `operations/Customers.md` |
| Idempotency pre-check (optional belt-and-braces) | `client.Subscriptions` · `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` nullable, **must pass explicitly**; query wire name `reference` | — | `SubscriptionResponse` → `.Subscription` | **Case A**: `SdkException<FindSubscriptionError>` — `TryGetNoContent(out RawError)` [404 = not found → safe to create] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |
| Create subscription | `client.Subscriptions` · `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, **must pass explicitly** | `CreateSubscriptionRequest { Subscription = new CreateSubscription { … } }` — see 2.2 | `SubscriptionResponse` → `.Subscription` | **Case A**: `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422; `Errors (errors): IReadOnlyList<string>` required] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |

### 2.2 Request/response model fields (wire names in parens; `!req` = C# `required`)

**`CreateCustomerRequest`** (`records-1-Ac-Cr.md`): `Customer (customer): CreateCustomer !req`
**`CreateCustomer`** (`records-1-Ac-Cr.md`): `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?` ← set to the eShopOnWeb user id (server enforces uniqueness of `reference`), plus optional `Organization`, `Address`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, …

**`CreateSubscriptionRequest`** (`records-2-Cr-Ne.md`): `Subscription (subscription): CreateSubscription !req`
**`CreateSubscription`** (`records-2-Cr-Ne.md`) — fields this integration sets:
- `ProductHandle (product_handle): string?` ← specify the plan by handle (`eshop-pro` / `basic-plan`); alternative `ProductId (product_id): int?` — **use the handle** (numeric ids unstable after re-seed)
- `CustomerId (customer_id): int?` ← the ensured customer's `Id`; alternative `CustomerReference (customer_reference): string?` (identify by the same reference string); `CustomerAttributes (customer_attributes): CustomerAttributes?` creates a new customer inline — **not used** here (ensure-customer is a separate step)
- `Reference (reference): string?` ← set a deterministic value (e.g. `eshop-{userId}-{productHandle}`) so `FindSubscription` can pre-check; a sibling field `Ref (ref): string?` also exists but `FindSubscription` queries `reference` — use `Reference`
- Everything else optional (`CouponCode`, `Components`, `NextBillingAt`, `PaymentProfileId`, …). No payment profile needed — seeded plans have payment method NOT required.

**`CustomerResponse`** (`records-2-Cr-Ne.md`): `Customer (customer): Customer !req`
**`Customer`** (`records-2-Cr-Ne.md`): `Id (id): int?`, `Reference (reference): string?`, `FirstName`, `LastName`, `Email`, `Organization`, … (all nullable)

**`ProductFamilyResponse`** (`records-3-Of-Su.md`): `ProductFamily (product_family): ProductFamily?`
**`ProductFamily`** (`records-3-Of-Su.md`): `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`

**`ProductResponse`** (`records-3-Of-Su.md`): `Product (product): Product !req`
**`Product`** (`records-3-Of-Su.md`) — fields the plan DTO reads: `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `ProductFamily (product_family): ProductFamily?`, `ArchivedAt (archived_at): DateTimeOffset?` (skip archived). **No currency field on `Product`** — see Assumptions.

**`SubscriptionResponse`** (`records-4-Su-We.md`): `Subscription (subscription): Subscription?`
**`Subscription`** (`records-3-Of-Su.md`) — fields the subscription DTO reads: `Id (id): int?`, `State (state): SubscriptionState?`, `Product (product): Product?` (id/handle/name), `ProductPriceInCents (product_price_in_cents): long?` (unit price), `Currency (currency): string?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` ← **this is the next-billing-date field** (the model carries no `next_billing_at`; the API docs note the server does not return `next_billing_at` on reads — use `current_period_ends_at`), `CurrentPeriodStartedAt (current_period_started_at): DateTimeOffset?`, `Reference (reference): string?`, `Customer (customer): Customer?`, `ActivatedAt (activated_at): DateTimeOffset?`, `CanceledAt (canceled_at): DateTimeOffset?`

**Error payloads** (`records-2-Cr-Ne.md`): `ErrorListResponse1` → `Errors (errors): IReadOnlyList<string> !req`. `CustomerErrorResponse1` → `Errors (errors): Errors?` where `Errors` models only `PerPage (per_page): IReadOnlyList<string>?` and `PricePoint (price_point): IReadOnlyList<string>?` — see the defensive directive in Assumptions & Blockers.

### 2.3 Enum values needed (all ns `MaxioAdvancedBilling.Models.Enums`; `StringEnum<T>` — static members, not C# enums) (`enums.md`)

| Enum | Members (C# name = wire value) |
|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `IntervalUnit` | `Day (day)`, `Month (month)` |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` (only if ever passing `dateField`; this integration passes `null`) |

### 2.4 Idempotency design (grounded in the operations above)

- **Customer**: `reference` uniqueness is enforced server-side (one customer per reference). Sequence: `ReadCustomerByReference(userId)` → on Case-B 404, `CreateCustomer` with `Reference = userId` → on Case-A 422 (lost race), re-run `ReadCustomerByReference`.
- **Subscription**: no unique-token field on create, but `CreateSubscription.Reference` + `FindSubscription(reference)` give a lookup path. Sequence: `ListCustomerSubscriptions(customerId)` and treat an existing non-terminal subscription (`State` in `Active`, `Trialing`, `AwaitingSignup`, `PastDue`, `OnHold`) on the same product handle as already-subscribed → return it instead of creating; optionally also set `Reference = $"eshop-{userId}-{productHandle}"` and pre-check with `FindSubscription`. The check-then-create window is closed app-side (per-user locking), not by the SDK.

## 3. Trap notes

> ⚠ Step 2 (client registration) — the `HttpClient`/handler pipeline behind the client has lifetime rules the ctor signature does not show; building it per request or disposing it with the client breaks socket reuse. **MUST load `dotnet-client-initialization`** before writing the registration.
>
> ⚠ Step 2 (auth) — credentials must be in place before the client is constructed (or set in the DI callback), and the key must come from configuration, never source. **MUST load `dotnet-authentication`** before wiring `BasicAuthCredentials`.
>
> ⚠ Steps 3–5 (every call) — list/search operations take many nullable parameters with **no C# defaults** that mis-bind in positional calls; call them with named arguments only. **MUST load `dotnet-calling-endpoints`** before the first `client.{Group}.{Operation}(...)` call.
>
> ⚠ Steps 3–5 (models) — `SubscriptionState`/`IntervalUnit` are `StringEnum<T>` records, not C# enums; unions (none needed on the write path here) build via factories; unmodeled JSON fields are silently dropped on deserialize (see the `CustomerErrorResponse1` caveat). **MUST load `dotnet-models`** before mapping SDK models onto DTOs.
>
> ⚠ Step 6 (error boundary) — the operations in this sheet mix Case A typed errors and Case B raw errors per operation (see each row); `TryGetRawError` is not a catch-all on typed errors; and this SDK has **no** no-throw `…Result` variants — every call is throw-only and must be wrapped. **MUST load `dotnet-error-handling`** before writing any `try/catch`.
>
> ⚠ Step 6 (error boundary) — `System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:
> - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
> - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.
>
> **MUST load `dotnet-error-handling`** before writing that boundary.
>
> ⚠ Step 2 (resilience) — whether a failed `CreateSubscription`/`CreateCustomer` POST can be re-sent by the retry layer, what `Timeout` actually bounds, and the `MaxRetries` floor are all consequences the options' member names do not reveal; this matters directly to the idempotency design in 2.4. **MUST load `dotnet-configuration-resilience`** before tuning `options.Retry` or relying on defaults.
>
> ⚠ Step 6 (tests) — the test seam is a specific constructor argument, not an interface over the client; match the project's existing test framework and assertion style. **MUST load `dotnet-testing`** before stubbing the SDK.

## 4. REQUIRED READING

Load **before implementation starts** — this sheet deliberately does not carry their contents:

- `dotnet-client-initialization` — governs step 2 (client construction + DI registration)
- `dotnet-authentication` — governs step 2 (Basic credentials wiring)
- `dotnet-calling-endpoints` — governs steps 3–5 (named-argument calls, envelopes, `ct:`)
- `dotnet-models` — governs steps 3–5 (StringEnum, required members, wire names)
- `dotnet-error-handling` — governs step 6 (Case A/B boundary, `JsonException` hazards)
- `dotnet-configuration-resilience` — governs step 2 (retry/timeout semantics vs. idempotency)
- `dotnet-testing` — governs step 6 (faking the HTTP seam)

## 5. Assumptions & Blockers

- **Family resolution**: numeric IDs are unstable after re-seed, so the plan resolves the family by listing all families (`ListProductFamilies` has no pagination) and matching `Handle == Maxio:ProductFamilyHandle`, then passes `Id.ToString()` to `ListProductsForProductFamily`. Whether that `string productFamilyId` parameter also accepts a `"handle:…"` form is `UNVERIFIED` (only live traffic could confirm) — the plan does not rely on it.
- **Currency**: the `Product` model carries no currency field; prices are `PriceInCents` (long). Plan DTOs should take currency from `Subscription.Currency` where a subscription exists, and otherwise assume the site's currency (USD per the seeded catalog). `UNVERIFIED` against live traffic; defensive directive: surface currency when present, omit/format as USD otherwise.
- **Next billing date**: mapped from `Subscription.CurrentPeriodEndsAt` (`current_period_ends_at`) — the map's `Subscription` row has no `next_billing_at` field, and the API notes state reads do not return it.
- **`CustomerErrorResponse1` shape suspicion**: its `Errors` payload model carries only `per_page`/`price_point` keys — customer validation messages (e.g. duplicate `reference`) would be unmodeled JSON and dropped on deserialize. Defensive directive: on `CreateCustomer` 422, extract `TryGetCustomerErrorResponse1` best-effort and fall back to `TryGetRawError`/`ReadAsString()` for the actual messages. `UNVERIFIED` against live traffic.
- **Seeded catalog** (no trial, no setup fee, payment method not required) means `CreateSubscription` needs no payment profile fields; if the catalog is re-seeded differently, a 422 from `CreateSubscriptionError.TryGetErrorListResponse1` carries the messages.
- **JWT/caller identity** is app-side (eShopOnWeb PublicApi), outside SDK scope; the user id from the token becomes the Maxio customer `reference`.
- No blockers.
