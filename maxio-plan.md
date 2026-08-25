# maxio-plan.md — eShopOnWeb recurring-subscription billing via Maxio Advanced Billing .NET SDK

SDK: `AsadAli.AdvancedBilling.Sdk` (NuGet) · root namespace `MaxioAdvancedBilling` · pin **1.0.2** (this sheet was
grounded against the SDK map generated from source tag `v1.0.2`) · `netstandard2.0`, works on .NET 8.
Install: `dotnet add package AsadAli.AdvancedBilling.Sdk --version 1.0.2` (package id ≠ root namespace).

## 1. Scope & sequence

| # | Step | Operations used |
|---|------|-----------------|
| 0 | Add NuGet package to `src/PublicApi`; add config keys `Maxio:ApiKey`, `Maxio:Site` (subdomain, e.g. `cp-exp-1`), `Maxio:ProductFamilyHandle` (`eshop-subscribe`), optional `Maxio:BaseUrl` (verbatim override), optional `Maxio:Environment` (`Us`/`Eu`) | — |
| 1 | Register the SDK client in PublicApi DI with Basic auth + server/base-URL wiring | — (client construction) |
| 2 | `GET /api/subscription-plans` — resolve product-family id from `Maxio:ProductFamilyHandle` at runtime (cache it; numeric ids are unstable across re-seeds), then list products in the family; map to plan DTO (id, handle, name, price, interval) | `ProductFamilies.ListProductFamilies`, `ProductFamilies.ListProductsForProductFamily` |
| 3 | `POST /api/subscriptions` — (a) find-or-create customer keyed on `reference` = eShopOnWeb user id; (b) short-circuit if an active subscription for the requested product already exists, else create with `ProductHandle` + deterministic `Reference`; (c) return id/state/product/next-billing | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Customers.ListCustomerSubscriptions`, `Subscriptions.CreateSubscription`, (`Subscriptions.FindSubscription` for unknown-outcome recovery) |
| 4 | `GET /api/my-subscriptions` — find customer by reference (404 ⇒ empty list), list their subscriptions, map same DTO | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |
| 5 | Integration error boundary: translate SDK exceptions to HTTP results (401/404/422/5xx) | all of the above |
| 6 | Tests for the integration layer (fake at the SDK's `HttpClient` seam) | — |

JWT authentication on the endpoints is app-side (existing PublicApi infrastructure) and out of SDK scope.

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

### Client construction, auth, base URL (map: `sdk-map.md`)

| Fact | Value |
|---|---|
| Client class | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` — only ctor: `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` |
| Options class | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` — properties: `Environment` (`ServerEnvironment`), `Retry` (`RetryOptions`), `Server` (`ServerOptions`), `BasicAuth` (`BasicAuthCredentials?`) |
| Basic auth | `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = "<API key>", Password = "x" }` — password is the **literal string `"x"`** |
| Environment | `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default) / `.Eu` |
| Subdomain | `options.Server.Production.Us.Site = "cp-exp-1"` → base URL `https://cp-exp-1.chargify.com` |
| **`Maxio:BaseUrl` override** | `options.Server.Production.Us.BaseUrl = "<value>"` — used **verbatim** as the API base address, replacing the derived `https://{site}.chargify.com`. Set this *instead of* `Site` when the config key is present. (EU accounts: the `.Eu.*` siblings.) `ServerOptions` lives at the root namespace `MaxioAdvancedBilling`; `ProductionOptions` lives in `MaxioAdvancedBilling.Servers` |
| DI alternative | `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = …; o.Server.Production.Us.Site = …; })` (`ServiceCollectionExtensions.cs`, root namespace) |
| Retry config | `MaxioAdvancedBilling.Core.Configuration.RetryOptions` — all members `required`; start from `RetryOptions.Default()` |

### Operations

| Step | Controller property · signature (map page) | Request model + fields | Response envelope + fields the integration reads | Error case + accessors | Pagination |
|---|---|---|---|---|---|
| 2 | `client.ProductFamilies` · `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 nullable params **must be passed explicitly** (pass `null`) (`operations/ProductFamilies.md`) | — | `IReadOnlyList<ProductFamilyResponse>`; `ProductFamilyResponse.ProductFamily (product_family): ProductFamily?`; `ProductFamily.Id (id): int?`, `.Name (name): string?`, `.Handle (handle): string?` — match `Handle == Maxio:ProductFamilyHandle` client-side to resolve the family id | **Case B** `SdkException<RawError>` — `Error.StatusCode`, `Error.ReadAsString()` | none |
| 2 | `client.ProductFamilies` · `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 nullable params (`dateField`…`include`) **must be passed explicitly** (`operations/ProductFamilies.md`) | `productFamilyId` = resolved family id as string (`family.Id.Value.ToString()`) | `IReadOnlyList<ProductResponse>`; `ProductResponse.Product (product): Product` (`required`, non-null); `Product.Id (id): int?`, `.Handle (handle): string?`, `.Name (name): string?`, `.PriceInCents (price_in_cents): long?`, `.Interval (interval): int?`, `.IntervalUnit (interval_unit): IntervalUnit?`, `.ArchivedAt (archived_at): DateTimeOffset?` (skip archived) | **Case A** `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage` (catalog is 2 products; one page suffices, but loop if `perPage` is ever lowered) |
| 3a | `client.Customers` · `ReadCustomerByReference(string reference, CancellationToken ct = default)` (`operations/Customers.md`) | `reference` = eShopOnWeb user id | `CustomerResponse.Customer (customer): Customer` (`required`); `Customer.Id (id): int?`, `.Reference (reference): string?`, `.Email (email): string?`, `.FirstName (first_name)`, `.LastName (last_name)` | **Case B** `SdkException<RawError>` — "customer not found" = `Error.StatusCode == HttpStatusCode.NotFound` ⇒ proceed to create | none |
| 3a | `client.Customers` · `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, **must pass explicitly** (`operations/Customers.md`) | `CreateCustomerRequest.Customer (customer): CreateCustomer` (`required`). `CreateCustomer`: `FirstName (first_name): string` **!req**, `LastName (last_name): string` **!req**, `Email (email): string` **!req**, `Reference (reference): string?` ← always set = eShopOnWeb user id (uniqueness key; server enforces one customer per reference) | `CustomerResponse.Customer (customer): Customer` (`required`) → read `Id` | **Case A** `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. ⚠ `CustomerErrorResponse1.Errors (errors): Errors?` and the shared `Errors` record models **only** `PerPage (per_page)`, `PricePoint (price_point)` — real customer validation keys are unmodeled and dropped on deserialize (see Assumptions). On 422 treat as possible duplicate-reference race: re-call `ReadCustomerByReference` and use its `Id`; log the body via `TryGetRawError` → `ReadAsString()` | none |
| 3b, 4 | `client.Customers` · `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` (`operations/Customers.md`) | `customerId` from step 3a | `IReadOnlyList<SubscriptionResponse>`; `SubscriptionResponse.Subscription (subscription): Subscription?` (**nullable** — null-check before reading). Fields read: `Subscription.Id (id): int?`, `.State (state): SubscriptionState?`, `.Product (product): Product?` → `.Handle`/`.Name`/`.PriceInCents`/`.Interval`/`.IntervalUnit`, `.ProductPriceInCents (product_price_in_cents): long?`, `.CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `.NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `.Reference (reference): string?` | **Case B** `SdkException<RawError>` | none (returns all of the customer's subscriptions) |
| 3b | `client.Subscriptions` · `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, **must pass explicitly** (`operations/Subscriptions.md`) | `CreateSubscriptionRequest.Subscription (subscription): CreateSubscription` (`required`). `CreateSubscription` has **no `required` members** — set: `CustomerId (customer_id): int?` ← from 3a; `ProductHandle (product_handle): string?` ← plan handle from the API request (the SDK accepts handle **or** `ProductId (product_id): int?` — prefer the handle, stable across re-seeds); `Reference (reference): string?` ← deterministic, e.g. `"{userId}:{productHandle}"` (idempotency anchor). Do **not** set `ProductId` from config. No payment profile needed on the seeded sandbox (payment method not required) | `SubscriptionResponse.Subscription (subscription): Subscription?` (nullable) → same fields as above for the response DTO. **There is no `next_billing_at` on the `Subscription` response record** (request-side only) — return `CurrentPeriodEndsAt` as the next-billing anchor and optionally `NextAssessmentAt` | **Case A** `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422]; `ErrorListResponse1.Errors (errors): IReadOnlyList<string>` (`required`) · `TryGetRawError(out RawError)` [fallback] | none |
| 3b (recovery) | `client.Subscriptions` · `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` nullable, **must pass explicitly** (`operations/Subscriptions.md`) | the deterministic `Reference` above | `SubscriptionResponse` (as above) | **Case A** `SdkException<FindSubscriptionError>` — `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback]. Use after an unknown-outcome create (e.g. transport exception): 404 ⇒ safe to re-create; found ⇒ return the existing one | none |

**Not used, and why:** `Subscriptions.ListSubscriptions` has **no customer filter** (its filters are `state`, `product`, `coupon`, dates, metadata — `operations/Subscriptions.md`); "my subscriptions" goes through `Customers.ListCustomerSubscriptions`. `ProductFamilies.ReadProductFamily(int id, …)` takes an `int`, so the documented `handle:my-family` format cannot be passed through it — resolve the family id via `ListProductFamilies` + handle match instead.

### Enum values (map: `models/enums.md`; all are `StringEnum<T>`, namespace `MaxioAdvancedBilling.Models.Enums` — construct/compare per `dotnet-models`, not as C# enums)

| Enum | Members (wire value) |
|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |
| `SubscriptionStateFilter` | `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)` |
| `IntervalUnit` | `Day (day)`, `Month (month)` |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` (only passed as `null` here) |
| `ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)` (only passed as `null` here) |

**"Already subscribed" short-circuit set (step 3b):** treat a same-product (`Product.Handle` match) subscription as existing when `State` is one of `Active`, `Trialing`, `PastDue`, `OnHold`, `AwaitingSignup`, `SoftFailure`, `Unpaid`, `Suspended`, `Paused`, `Pending`, `Assessing`; treat `Canceled`, `Expired`, `FailedToCreate`, `TrialEnded` as terminal (allow re-subscribe). Adjust to product taste — see Assumptions.

### Error-handling model (map: `sdk-map.md`)

- Every operation is **throw-only** (no `…Result`/`ApiResult` variants exist in this SDK). On error status it throws `MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>` with `.Error: TError`.
- **Case A** (typed): `TError` ∈ `MaxioAdvancedBilling.Errors.*` (`CreateCustomerError`, `CreateSubscriptionError`, `ListProductsForProductFamilyError`, `FindSubscriptionError`) — status-specific `TryGet…(out …)` as tabled above, plus inherited `TryGetRawError(out RawError)` fallback (not a catch-all — returns true only when no typed shape matched).
- **Case B** (raw): `TError` = `MaxioAdvancedBilling.Core.ErrorResponse.RawError` — `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`, `ReadAsBytes(): ReadOnlyMemory<byte>`.
- Statuses to expect here: **401** (bad API key — surfaces via `RawError.StatusCode` on Case-B ops, `TryGetRawError` on Case-A ops), **404** (customer lookup, product family, subscription lookup), **422** (customer/subscription validation).

## 3. Trap notes

- ⚠ Step 1 (client registration) — the `HttpClient`/handler pipeline and the SDK client wrapper have different required lifetimes; constructing either per-request or socket-exhausting is the classic failure. **MUST load `dotnet-client-initialization`** before writing the DI registration.
- ⚠ Step 1 (auth) — credentials must be in place before the client is constructed / inside the DI callback, and the API key must come from configuration, never source. **MUST load `dotnet-authentication`**.
- ⚠ Steps 2–4 (calls) — list/search operations carry must-pass-explicitly nullable parameters with no C# defaults; positional calls mis-bind, and the cancellation token binds only as `ct:`. **MUST load `dotnet-calling-endpoints`** before the first call.
- ⚠ Steps 2–4 (models) — `SubscriptionState`/`IntervalUnit` are `StringEnum<T>`, not C# enums (construction, comparison and wire-value semantics differ); records are immutable with `init`-only setters; unmodeled JSON fields are silently dropped on deserialize (this is exactly what bites `CustomerErrorResponse1.Errors`). **MUST load `dotnet-models`**.
- ⚠ Step 3 (idempotency vs retries) — whether a failed write (`POST` create-customer / create-subscription) can be re-sent by the SDK's retry layer, what `Timeout` actually bounds, and the `MaxRetries` floor are all non-obvious; this is why the `reference`-keyed find-or-create + deterministic subscription `Reference` + `FindSubscription` recovery above are **mandatory, not optional**. **MUST load `dotnet-configuration-resilience`** before wiring the client.
- ⚠ Step 5 (error boundary) — Case A vs Case B is per-operation (confirm each in the sheet above), `TryGetRawError` is not a catch-all on typed errors, and every operation throws. **MUST load `dotnet-error-handling`** before writing any `try/catch`.
- ⚠ Step 6 (tests) — the SDK's test seam is the `HttpClient` constructor argument; match the repo's existing test framework/assertion style. **MUST load `dotnet-testing`**.

## 4. REQUIRED READING

Load **before implementation starts** — this sheet deliberately does not carry their contents:

- `dotnet-client-initialization` — Step 1 (client construction, `HttpClient` ownership, DI registration).
- `dotnet-authentication` — Step 1 (Basic credentials, per-environment config).
- `dotnet-calling-endpoints` — Steps 2–4 (named arguments, must-pass params, async/cancellation).
- `dotnet-models` — Steps 2–4 (records, `StringEnum<T>` enums, wire names vs C# names).
- `dotnet-error-handling` — Step 5 (the exception boundary; Case A/B mechanics).
- `dotnet-configuration-resilience` — Steps 1 & 3 (retries, timeouts, base-URL selection, pagination).
- `dotnet-testing` — Step 6 (faking the SDK in tests).

These two hazard rows are part of the first sheet because the boundary is written early:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
  maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
  and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

## 5. Assumptions & Blockers

- **Assumed:** the eShopOnWeb user id is stored in `Customer.Reference` and is the customer idempotency key; the subscription `Reference` is `"{userId}:{productHandle}"`. Both are app-side conventions the SDK merely permits.
- **Assumed:** family resolution is done at runtime (`ListProductFamilies` + client-side `Handle` match, cached) rather than by configuring a numeric family id, because ids are unstable across re-seeds. `ReadProductFamily` cannot help — its signature is `int id` despite its doc note about the `handle:…` format (map-visible tension).
- **UNVERIFIED (live traffic only):** `ListProductsForProductFamily` takes `string productFamilyId`, so passing `"handle:eshop-subscribe"` directly *may* work on the wire (a sibling operation documents the `handle:` format). Defensive directive: do **not** rely on it — resolve the numeric id at runtime as planned; if the string form is ever used, fall back to id resolution on a 404.
- **UNVERIFIED (map-visible suspicious shared model):** `CustomerErrorResponse1.Errors` is typed as the shared `Errors` record, which models only `per_page`/`price_point` — real 422 customer validation keys (`reference`, `email`, …) are likely unmodeled and dropped on deserialize. Defensive directive: never parse the typed 422 payload for customer field errors; treat any `CreateCustomer` 422 as a possible duplicate-reference race → re-`ReadCustomerByReference`; log the raw body via `TryGetRawError` → `ReadAsString()`.
- **Assumed:** the seeded sandbox products (`eshop-pro`, `basic-plan`) require no payment profile, so `CreateSubscription` is called without `PaymentProfileId`; against a payment-requiring product the same call fails 422 (`ErrorListResponse1.Errors` carries the messages).
- **Assumed (product decision):** the "already subscribed" short-circuit state set in §2; tighten/loosen in app code without SDK changes.
- **Blockers:** none.
