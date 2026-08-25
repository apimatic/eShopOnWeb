# Maxio Advanced Billing — eShopOnWeb integration plan

Recurring-subscription billing for eShopOnWeb via the Maxio Advanced Billing .NET SDK, exposed as
JWT-authenticated endpoints on `src/PublicApi`. Additive capability; existing cart/checkout untouched.

## 1. Scope & sequence

| # | Step | SDK operations used |
|---|---|---|
| 1 | Add NuGet package `AsadAli.AdvancedBilling.Sdk` **1.0.2** to `src/PublicApi` (and to `src/ApplicationCore` only if the billing service class is placed there — see Assumptions). Package id ≠ root namespace: install `AsadAli.AdvancedBilling.Sdk`, write `using MaxioAdvancedBilling;`. Runtime deps (Polly, Microsoft.Extensions.Http, System.Net.Http.Json, System.Net.ServerSentEvents) come in transitively. | — |
| 2 | Bind `Maxio:` config section (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl`) + env vars; register the client in DI (`AddMaxioAdvancedBillingClient` or factory) with Basic auth, environment, site/BaseUrl. | — |
| 3 | `GET /api/subscription-plans` — resolve configured family handle → family id (cached), list products in family, map to plan DTO (handle, id, name, price, interval). | `ProductFamilies.ListProductFamilies`, `ProductFamilies.ListProductsForProductFamily` |
| 4 | `POST /api/subscriptions` — find-or-create customer by reference (= authenticated username), then find-or-create subscription by deterministic reference, return plan/price/state/next-billing. | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Subscriptions.FindSubscription`, `Subscriptions.CreateSubscription` |
| 5 | `GET /api/my-subscriptions` — resolve customer by reference (404 → empty list), list that customer's subscriptions, map to DTO. | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |
| 6 | Error boundary middleware/translation for the three endpoints. | (error types below) |
| 7 | Tests for the billing service + endpoints (fake at the `HttpClient` seam). | — |

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

### 2a. Package, client, auth, servers

| Fact | Value | Map page |
|---|---|---|
| NuGet package / version | `AsadAli.AdvancedBilling.Sdk` **1.0.2** (map generated from source tag `v1.0.2`) | `sdk-map.md` |
| Root namespace | `MaxioAdvancedBilling` (≠ package id) | `sdk-map.md` |
| Client class | `MaxioAdvancedBillingClient` — only ctor: `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` (namespace `MaxioAdvancedBilling`) | `sdk-map.md` |
| Options | `MaxioAdvancedBillingClientOptions` (namespace `MaxioAdvancedBilling`): `Environment: ServerEnvironment` · `Retry: RetryOptions` · `Server: ServerOptions` · `BasicAuth: BasicAuthCredentials?` | `sdk-map.md` |
| DI extension | `services.AddMaxioAdvancedBillingClient(o => { … })` — `ServiceCollectionExtensions.cs` at repo root ⇒ namespace `MaxioAdvancedBilling` | `sdk-map.md` |
| Auth | HTTP Basic. `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <api_key>, Password = "x" }` — password is the literal string `"x"` | `sdk-map.md` |
| Environment | `MaxioAdvancedBilling.Servers.ServerEnvironment` — `.Us` (default) → `https://{site}.chargify.com`; `.Eu` → `https://{site}.ebilling.maxio.com`. Map `MAXIO_ENVIRONMENT`/`Maxio:` config onto this. | `sdk-map.md` |
| Subdomain → site | `options.Server.Production.Us.Site = "<subdomain>"` (fills `{site}`; for EU use `.Eu.Site`). All operations in scope are on the **Production** server group. | `sdk-map.md` |
| **BaseUrl override** | When `Maxio:BaseUrl` is set: `options.Server.Production.Us.BaseUrl = "<baseUrl>"` (verbatim, replaces the derived URL; use `.Eu.BaseUrl` when Environment=EU). Do **not** also set `Site` in that case. | `sdk-map.md` |
| Retry options | `MaxioAdvancedBilling.Core.Configuration.RetryOptions` — **all members `required`**; start from `RetryOptions.Default()` and mutate, never `new` it bare. Members: `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout (TimeSpan?)`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry`. | `sdk-map.md` |

### 2b. Operations

| Step | Controller property · signature (verbatim) | Request model | Response envelope | Error case + accessors | Pagination |
|---|---|---|---|---|---|
| 3 resolve family | `client.ProductFamilies.ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 nullable, no defaults → **pass all explicitly as `null`**. Call: `ListProductFamilies(dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: ct)` | — | `IReadOnlyList<ProductFamilyResponse>`; each wraps `ProductFamily (product_family): ProductFamily?` (**nullable**). Find `pf.ProductFamily?.Handle == configuredHandle`, take `.Id` | **Case B** `SdkException<RawError>` — `.Error.StatusCode`, `.Error.ReadAsString()` | none (full list) |
| 3 list plans | `client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 middle params nullable, no defaults → pass explicitly as `null`. `productFamilyId` is a **string** — pass the resolved numeric id as `id.ToString(CultureInfo.InvariantCulture)` | — | `IReadOnlyList<ProductResponse>`; each wraps `Product (product): Product` (**required, non-null**) | **Case A** `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404, family not found] · `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage`; loop until a page returns < `perPage` (seeded catalog is 2 products — one page at `perPage: 50` suffices) |
| 4/5 find customer | `client.Customers.ReadCustomerByReference(string reference, CancellationToken ct = default)` — query param `reference` | — | `CustomerResponse` → `Customer (customer): Customer` (**required**) | **Case B** `SdkException<RawError>` — "customer not found" = `.Error.StatusCode == HttpStatusCode.NotFound` (404). There is no typed 404 accessor here | none |
| 4 create customer | `client.Customers.CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly** | `CreateCustomerRequest { Customer = new CreateCustomer { … } }` — `CreateCustomer.Customer (customer)` is **required**. `CreateCustomer` required members: `FirstName (first_name): string`, `LastName (last_name): string`, `Email (email): string`. Set also `Reference (reference): string?` = the stable eShopOnWeb username — **server enforces reference uniqueness** (one customer per reference value) | `CustomerResponse` → `Customer (customer): Customer` (required); read `Customer.Id (id): int?` | **Case A** `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422, e.g. duplicate reference] · `TryGetRawError(out RawError)` [fallback]. ⚠ The 422 payload `CustomerErrorResponse1.Errors` is an `Errors` record whose only fields are `PerPage (per_page)` / `PricePoint (price_point)` (`records-2-Cr-Ne.md`) — that shape does not look like customer-validation errors, so a real duplicate-reference 422 body may fail to deserialize into it. Treat the 422 payload as best-effort; rely on the status, not the parsed body (see JsonException hazard rows, §4) | none |
| 4 find subscription (idempotency) | `client.Subscriptions.FindSubscription(string? reference, CancellationToken ct = default)` — `reference` nullable, no default → **must pass explicitly** | — | `SubscriptionResponse` → `Subscription (subscription): Subscription?` (**nullable — null-check before reading**) | **Case A** `SdkException<FindSubscriptionError>` — `TryGetNoContent(out RawError)` [404, no such reference → safe to create] · `TryGetRawError(out RawError)` [fallback] | none |
| 4 create subscription | `client.Subscriptions.CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly** | `CreateSubscriptionRequest { Subscription = new CreateSubscription { … } }` — `Subscription (subscription)` **required**. `CreateSubscription` has **no required members**; set: `ProductHandle (product_handle): string?` = plan handle (`eshop-pro` / `basic-plan`); `CustomerId (customer_id): int?` = id from step 4-customer (alternative: `CustomerReference (customer_reference): string?` = same reference — either identifies the customer; use `CustomerId` since you already have it); `Reference (reference): string?` = deterministic idempotency key, e.g. `"{username}:{productHandle}"`. No payment fields needed (products don't require a card) | `SubscriptionResponse` → `Subscription (subscription): Subscription?` (nullable) | **Case A** `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. `ErrorListResponse1.Errors (errors): IReadOnlyList<string>` (**required**) — the 422 message list | none |
| 5 list my subscriptions | `client.Customers.ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | — | `IReadOnlyList<SubscriptionResponse>`; each `Subscription (subscription): Subscription?` (nullable) | **Case B** `SdkException<RawError>` | **none** — returns all of the customer's subscriptions in one call |

### 2c. Response-model fields the integration reads

| Model | Fields (C# name (wire_name): type) | Map page |
|---|---|---|
| `ProductFamily` | `Id (id): int?` · `Handle (handle): string?` · `Name (name): string?` | `records-3-Of-Su.md` |
| `Product` | `Id (id): int?` · `Name (name): string?` · `Handle (handle): string?` · `PriceInCents (price_in_cents): long?` · `Interval (interval): int?` · `IntervalUnit (interval_unit): IntervalUnit?` · `ArchivedAt (archived_at): DateTimeOffset?` (skip non-null when listing plans, or pass `includeArchived: false`) | `records-3-Of-Su.md` |
| `Customer` | `Id (id): int?` · `Reference (reference): string?` · `Email (email): string?` · `FirstName (first_name): string?` · `LastName (last_name): string?` | `records-2-Cr-Ne.md` |
| `Subscription` | `Id (id): int?` · `State (state): SubscriptionState?` · `ProductPriceInCents (product_price_in_cents): long?` · `Reference (reference): string?` · `Product (product): Product?` (nested — plan handle/name) · `Customer (customer): Customer?` · **next billing date: there is NO `next_billing_at` on this model** — use `NextAssessmentAt (next_assessment_at): DateTimeOffset?` as the next-billing-date value, falling back to `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` when null | `records-3-Of-Su.md` |

### 2d. Enums (all `StringEnum<T>`, namespace `MaxioAdvancedBilling.Models.Enums` — construct via static member or `Type.FromValue("wire")`, never C# enum syntax)

| Enum | Values (Member (wire)) | Map page |
|---|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` | `enums.md` |
| `IntervalUnit` | `Day (day)`, `Month (month)` | `enums.md` |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` (only referenced to pass `null`) | `enums.md` |
| `ListProductsFilter` | **record, not enum** — pass `null` | `records-2-Cr-Ne.md` |
| `ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)` — pass `null` | `enums.md` |

### 2e. Idempotency design (no SDK idempotency-key mechanism exists)

- The SDK exposes **no idempotency-key header/parameter** on any create operation (map rows for
  `CreateCustomer`/`CreateSubscription` carry none). Idempotency is achieved with **lookup-by-reference
  + server-side reference uniqueness**:
  - **Customer**: `reference` = authenticated username. `ReadCustomerByReference` → on 404
    (`SdkException<RawError>` with `StatusCode == NotFound`) call `CreateCustomer`; server enforces
    one-customer-per-reference, so a lost race surfaces as **422** — on 422, re-run
    `ReadCustomerByReference` and use the existing customer.
  - **Subscription**: `reference` = `"{username}:{productHandle}"`. `FindSubscription` → on 404
    (`TryGetNoContent`) call `CreateSubscription` with that `Reference`; on success or on a
    post-race re-find, return the found/created subscription. A double-click with the same body
    therefore returns the same subscription instead of creating a second one.
- ⚠ This guards the *logical* duplicate. Whether the SDK's retry layer can re-send a failed `POST`
  underneath you (transport failure after the server processed it) is a resilience-layer hazard —
  see trap notes; the deterministic `reference` is also what makes a retried create detectable.

## 3. Trap notes

> ⚠ Step 2 (client registration) — the `HttpClient`/handler pipeline behind the SDK client must be
> long-lived and factory-managed, not built per request; the SDK wrapper's own lifetime differs from
> the pipeline's. **MUST load `dotnet-client-initialization`** before writing the registration.

> ⚠ Step 2 (auth) — credentials must be set before the client is constructed (or in the DI callback)
> and the API key must come from configuration, never source. **MUST load `dotnet-authentication`**.

> ⚠ Steps 3–5 (every call) — list/search operations take many nullable parameters with **no C#
> default**; positional calls mis-bind. Call with named arguments only, passing explicit `null`s, and
> `ct:` for the token. **MUST load `dotnet-calling-endpoints`**.

> ⚠ Steps 3–5 (models) — enums are `StringEnum<T>` (not C# enums), records are immutable with
> `init`-only setters and `required` members, and unmodeled JSON fields are silently dropped on
> deserialize (so a field you don't read is a field you never see). **MUST load `dotnet-models`**.

> ⚠ Step 4 (create subscription) — whether a failed write `POST` can be re-sent by the SDK's retry
> layer, and what `RetryOptions.Timeout` actually bounds, determines whether your idempotency logic
> is sufficient and what a "timeout" means. **MUST load `dotnet-configuration-resilience`** before
> tuning `Retry` or relying on defaults.

> ⚠ Step 6 (error boundary) — `System.Text.Json.JsonException` reaches the boundary from two
> directions and they need opposite handling:
> - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
>   `JsonException` from deserialization, **not** as an `SdkException` — so an
>   SDK-exception-only catch ladder lets it escape the integration boundary;
> - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
>   throws `JsonException` *while the error object is being constructed*, so the `JsonException`
>   **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
>   maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
>   and a caller that retries 5xx retries something that can never succeed.
>
> This second row bites concretely at step 4: the 422 payload of `CreateCustomer`
> (`CustomerErrorResponse1` → `Errors` with only `PerPage`/`PricePoint` fields, §2b) is a
> suspicious-shape candidate. **MUST load `dotnet-error-handling`** before writing that boundary.

> ⚠ Step 7 (tests) — the `HttpClient` constructor argument is the test seam; fake there, not by
> wrapping SDK internals. **MUST load `dotnet-testing`**.

## 4. REQUIRED READING

Load **before implementation starts**; this sheet deliberately does not carry their contents.

- `dotnet-client-initialization` — step 2 (client construction & DI registration)
- `dotnet-authentication` — step 2 (Basic credentials wiring)
- `dotnet-calling-endpoints` — steps 3–5 (named-argument calls, must-pass nulls, `ct:`)
- `dotnet-models` — steps 3–5 (records, `StringEnum<T>`, required members, wire names)
- `dotnet-error-handling` — step 6 (Case A/B catch ladders, `TryGet…` accessors, the two `JsonException` directions above)
- `dotnet-configuration-resilience` — steps 2 & 4 (retry/timeout semantics, base-URL override mechanics)
- `dotnet-testing` — step 7 (faking at the `HttpClient` seam)

## 5. Assumptions & Blockers

**Assumptions**

1. Package placement: `src/PublicApi` gets the SDK reference. If the billing service class is placed
   in `src/ApplicationCore` (eShopOnWeb's usual layering), add the package there instead (or to both);
   the plan's contracts are identical either way.
2. `CreateCustomer` requires `FirstName`, `LastName`, `Email`, but eShopOnWeb identity may only
   guarantee a username/email. Assumed acceptable to derive names from the username (or use a
   placeholder) — flagged for the implementer to confirm against the actual identity claims.
3. Family-handle resolution uses `ListProductFamilies` + client-side handle match (both `Id` and
   `Handle` are on `ProductFamily`). Note: `ReadProductFamily(int id)`'s docs mention a
   `handle:my-family` format, but its signature takes `int` — a generated-SDK inconsistency; do not
   rely on it. Whether `ListProductsForProductFamily(string productFamilyId)` accepts a
   `"handle:…"` string directly is **UNVERIFIED** from the map — the resolve-then-list path above is
   the grounded one.
4. "Next billing date" is served from `Subscription.NextAssessmentAt` (fallback
   `CurrentPeriodEndsAt`) because the read model carries no `next_billing_at` field. Whether the
   live API populates `next_assessment_at` for these no-card products is **UNVERIFIED** — code the
   fallback.
5. `MAXIO_ENVIRONMENT` values map onto `ServerEnvironment.Us`/`Eu`; anything unrecognized should
   fail fast at startup, not silently default.
6. The metered component `api-call` is out of scope for these three endpoints (no usage-reporting
   endpoint was requested).

**Blockers** — none.
