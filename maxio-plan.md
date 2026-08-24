# Maxio Advanced Billing — eShopOnWeb integration plan

Recurring-subscription billing for eShopOnWeb via the Maxio Advanced Billing .NET SDK, exposed as
JWT-authenticated endpoints on `src/PublicApi`: `GET /api/subscription-plans`,
`POST /api/subscriptions`, `GET /api/my-subscriptions`.

## 1. Scope & sequence

| Step | Work | SDK operations used |
|---|---|---|
| 0 | Bind `Maxio:` config section (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl`); register the SDK client in DI with Basic auth and server/base-URL selection | — (client construction) |
| 1 | `GET /api/subscription-plans` — resolve configured family handle → numeric id (runtime lookup, ids are unstable), list products in the family, project to `{ handle, name, priceInCents, interval, intervalUnit }` | `ProductFamilies.ListProductFamilies` → `ProductFamilies.ListProductsForProductFamily` |
| 2 | `POST /api/subscriptions` — ensure-customer (lookup by `reference` = eShopOnWeb userId, create on 404, re-read on duplicate-`reference` 422 race), then create the subscription by **product handle**; return `{ id, state, productHandle, productName, priceInCents, nextBillingDate }` | `Customers.ReadCustomerByReference` → (`Customers.CreateCustomer`) → `Subscriptions.CreateSubscription` (optional double-submit guard: `Subscriptions.FindSubscription`) |
| 3 | `GET /api/my-subscriptions` — lookup customer by `reference` (404 ⇒ empty list), list their subscriptions, project to `{ id, state, productHandle, productName, priceInCents, nextBillingDate }` | `Customers.ReadCustomerByReference` → `Customers.ListCustomerSubscriptions` |
| 4 | Integration error boundary — translate SDK exceptions to HTTP results | (error model below) |

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

### 2.1 SDK identity, client construction, auth, servers

| Fact | Value | Map page |
|---|---|---|
| NuGet package | `AsadAli.AdvancedBilling.Sdk` (map stamped from source tag `v1.0.2` — reference version `1.0.2`) | `sdk-map.md` |
| Root namespace | `MaxioAdvancedBilling` (≠ package id) | `sdk-map.md` |
| Client class | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` — type `MaxioAdvancedBillingClient` lives in the **root namespace** `MaxioAdvancedBilling` (there is no `MaxioAdvancedBillingClient` namespace); with `using MaxioAdvancedBilling;` write the simple name. Only ctor: `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` |
| Options class | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` — properties: `Environment: ServerEnvironment`, `Retry: RetryOptions`, `Server: ServerOptions`, `BasicAuth: BasicAuthCredentials?` | `sdk-map.md` |
| DI registration | `services.AddMaxioAdvancedBillingClient(o => { … })` (from `ServiceCollectionExtensions.cs`) | `sdk-map.md` |
| Auth | `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <API key>, Password = "x" }` — password is the literal string `"x"` | `sdk-map.md` |
| Environment | `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default) → `https://{site}.chargify.com`; `.Eu` → `https://{site}.ebilling.maxio.com` | `sdk-map.md` |
| Site subdomain | `options.Server.Production.Us.Site = "<subdomain>"` (fills `{site}`; default is the literal `subdomain`) | `sdk-map.md` |
| **BaseUrl override** | `options.Server.Production.Us.BaseUrl = "<url>"` — replaces the template entirely; use the configured `Maxio:BaseUrl` verbatim when set | `sdk-map.md` |
| `ServerOptions` / `ProductionOptions` | `ServerOptions.cs` at repo root ⇒ namespace `MaxioAdvancedBilling`; `Servers/ProductionOptions.cs` ⇒ `MaxioAdvancedBilling.Servers` | `sdk-map.md` |
| `RetryOptions` | namespace `MaxioAdvancedBilling.Core.Configuration`; all members `required` — start from `RetryOptions.Default()` | `sdk-map.md` |
| API groups | properties on the client: `client.ProductFamilies`, `client.Customers`, `client.Subscriptions` | `sdk-map.md` |

### 2.2 Operations

| Controller · signature (params in order) | Request model | Response envelope → fields read | Error case + accessors | Pagination | Map page |
|---|---|---|---|---|---|
| `client.ProductFamilies` · `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — **all 5 must be passed explicitly (pass `null`)** | — | `IReadOnlyList<ProductFamilyResponse>` → `.ProductFamily` (`ProductFamily?`) → `.Id (id): int?`, `.Handle (handle): string?`, `.Name (name): string?` | **Case B** `SdkException<RawError>` | none | `operations/ProductFamilies.md` |
| `client.ProductFamilies` · `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — **8 params `dateField`…`include` must be passed explicitly (pass `null`)** | `productFamilyId` is a `string` — pass the resolved numeric id as a string (see §5 on the `handle:` prefix) | `IReadOnlyList<ProductResponse>` → `.Product` (`Product` **required, non-null**) → `.Handle`, `.Name`, `.PriceInCents (price_in_cents): long?` (**cents**, e.g. $299/mo = `29900`), `.Interval (interval): int?`, `.IntervalUnit (interval_unit): IntervalUnit?` | **Case A** `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage` | `operations/ProductFamilies.md` |
| `client.Customers` · `ReadCustomerByReference(string reference, CancellationToken ct = default)` — exact-match lookup (`GET /customers/lookup.json`) | `reference` = eShopOnWeb userId | `CustomerResponse` → `.Customer` (`Customer` **required, non-null**) → `.Id (id): int?`, `.Reference (reference): string?` | **Case B** `SdkException<RawError>` — "customer absent" = `ex.Error.StatusCode == HttpStatusCode.NotFound` | none | `operations/Customers.md` |
| `client.Customers` · `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` **must be passed explicitly** | `CreateCustomerRequest { Customer = new CreateCustomer { FirstName = …, LastName = …, Email = …, Reference = <userId> } }` — `CreateCustomer`: `FirstName (first_name): string` **!req**, `LastName (last_name): string` **!req**, `Email (email): string` **!req**, `Reference (reference): string?` | `CustomerResponse` → `.Customer.Id` | **Case A** `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. Duplicate-`reference` race ⇒ 422: re-run `ReadCustomerByReference` and use that customer | none | `operations/Customers.md` |
| `client.Subscriptions` · `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` **must be passed explicitly** | `CreateSubscriptionRequest { Subscription = new CreateSubscription { CustomerId = <id>, ProductHandle = "<handle>" } }` — `CreateSubscription` fields used: `CustomerId (customer_id): int?`, `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `CustomerReference (customer_reference): string?`, `Reference (reference): string?` (all optional; set `ProductHandle` **or** `ProductId`, never both — see unions note §2.4) | `SubscriptionResponse` → `.Subscription` (`Subscription?` — **nullable, null-check**) → `.Id`, `.State`, `.Product`, `.CurrentPeriodEndsAt`, `.NextAssessmentAt` (field list §2.3) | **Case A** `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] (`ErrorListResponse1.Errors (errors): IReadOnlyList<string>` **required**) · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |
| `client.Customers` · `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | `customerId` from the ensured customer | `IReadOnlyList<SubscriptionResponse>` → per item `.Subscription` (`Subscription?`) → fields §2.3 | **Case B** `SdkException<RawError>` | none | `operations/Customers.md` |
| `client.Subscriptions` · `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` **must be passed explicitly** (optional double-submit guard) | deterministic subscription `Reference`, e.g. `"{userId}:{productHandle}"` | `SubscriptionResponse` → `.Subscription` | **Case A** `SdkException<FindSubscriptionError>` — `TryGetNoContent(out RawError)` [404 = not yet created] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |

Note: `Subscriptions.ListSubscriptions` exists but has **no customer filter** in its generated
signature — `Customers.ListCustomerSubscriptions(customerId)` is the correct op for step 3
(`operations/Subscriptions.md`, `operations/Customers.md`).

### 2.3 Response models read by the endpoints

`Subscription` (`MaxioAdvancedBilling.Models`, `records-3-Of-Su.md`) — fields the integration reads:
`Id (id): int?` · `State (state): SubscriptionState?` · `Product (product): Product?` ·
`Customer (customer): Customer?` · `ProductPriceInCents (product_price_in_cents): long?` ·
`CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` ·
`NextAssessmentAt (next_assessment_at): DateTimeOffset?` · `Reference (reference): string?`.

> **"Next billing date":** the `Subscription` record has **no** `next_billing_at` field. The
> `UpdateSubscription` map row's notes state the server does not return `next_billing_at` and to
> read `current_period_ends_at` instead. Project `nextBillingDate` from `CurrentPeriodEndsAt`
> (fall back to `NextAssessmentAt`). (`records-3-Of-Su.md`, `operations/Subscriptions.md`)

`Product` (`records-3-Of-Su.md`): `Id`, `Name (name): string?`, `Handle (handle): string?`,
`PriceInCents (price_in_cents): long?`, `Interval (interval): int?`,
`IntervalUnit (interval_unit): IntervalUnit?`, `ProductFamily (product_family): ProductFamily?`.
`Customer` (`records-2-Cr-Ne.md`): `Id (id): int?`, `Reference (reference): string?`,
`FirstName`, `LastName`, `Email`. `ProductFamily` (`records-3-Of-Su.md`): `Id`, `Handle`, `Name`.

### 2.4 Unions / enums

- **Product identification is NOT a union.** `CreateSubscription` carries two independent nullable
  fields, `ProductHandle (product_handle): string?` and `ProductId (product_id): int?` — set
  exactly one (this integration: `ProductHandle` only). The only union-typed field on
  `CreateSubscription` is `OfferId (offer_id): OfferId?` (`Models/AnyOf/OfferId.cs`), unused here.
  (`records-2-Cr-Ne.md`, `unions.md`)
- `SubscriptionState` (`MaxioAdvancedBilling.Models.Enums`, `StringEnum<T>` — **not** a C# enum):
  `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`,
  `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`,
  `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`,
  `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`,
  `AwaitingSignup (awaiting_signup)`. (`enums.md`)
- `IntervalUnit`: `Day (day)`, `Month (month)`. (`enums.md`)
- Filter enums available but unused (pass `null`): `BasicDateField` (`UpdatedAt`, `CreatedAt`),
  `ListProductsInclude` (`PrepaidProductPricePoint`), `SubscriptionStateFilter`. (`enums.md`)

### 2.5 Error model (applies to every call)

- All operations are **throw-only** — no `…Result`/`ApiResult` no-throw variants exist in this SDK.
  (`sdk-map.md`)
- `SdkException<TError>` (source `Core/Exceptions/SdkException.cs` ⇒ namespace
  `MaxioAdvancedBilling.Core.Exceptions`) exposes `.Error: TError`. (`sdk-map.md`)
- **Case A** (typed): `TError` is a generated class in namespace `MaxioAdvancedBilling.Errors`
  with status-specific `TryGet…(out …)` accessors + inherited `TryGetRawError(out RawError)`.
  **Case B** (raw): `TError` is `RawError`. (`sdk-map.md`)
- `RawError` (source `Core/ErrorResponse/RawError.cs` ⇒ namespace
  `MaxioAdvancedBilling.Core.ErrorResponse`): `StatusCode: HttpStatusCode`,
  `ReadAsString(): string`, `ReadAsJson<T>(): T?`, `ReadAsBytes(): ReadOnlyMemory<byte>`.
  (`sdk-map.md`)
- Per-operation cases: step 1 — `ListProductFamilies` B, `ListProductsForProductFamily` A
  (`TryGetString` [404]); step 2 — `ReadCustomerByReference` B (404 = absent), `CreateCustomer` A
  (`TryGetCustomerErrorResponse1` [422]), `CreateSubscription` A (`TryGetErrorListResponse1` [422]),
  `FindSubscription` A (`TryGetNoContent` [404]); step 3 — `ListCustomerSubscriptions` B.
- ⚠ **Trust caveat (map-visible):** `CreateCustomer`'s 422 payload type `CustomerErrorResponse1`
  has `Errors (errors): Errors?`, and the shared `Errors` record (`Models/Errors.cs`) declares
  only `PerPage (per_page)` and `PricePoint (price_point)` string lists — a shape that cannot
  represent a duplicate-`reference` customer error. Whether the live 422 body matches this
  generated model is **UNVERIFIED**. Defensive directive: extract the 422 message best-effort from
  the typed accessor, and fall back to `TryGetRawError(out var raw)` + `raw.ReadAsString()` for
  the generic message — never assume `Errors.PerPage`/`PricePoint` carry the real failure.
  (`records-2-Cr-Ne.md`)

## 3. Trap notes

> ⚠ Step 0 (client registration) — the `HttpClient`/handler pipeline behind the client has
> lifetime rules (socket exhaustion if rebuilt per request) that the constructor signature does
> not show, and whether `options.Server`/`options.Retry` come pre-initialized is not visible from
> the options property list. **MUST load `dotnet-client-initialization`** before wiring DI.

> ⚠ Step 0 (auth) — credentials must be set before the client is constructed (or in the DI
> callback) and the API key must come from configuration, never source. **MUST load
> `dotnet-authentication`.**

> ⚠ Steps 1–3 (every call) — the must-pass-explicitly nullable params have no C# default and
> mis-bind in positional calls; call with named arguments (and the token is `ct:`). **MUST load
> `dotnet-calling-endpoints`.**

> ⚠ Steps 1–3 (models) — `SubscriptionState`/`IntervalUnit` are `StringEnum<T>` records, not C#
> enums: how the wire string (e.g. `active`, `month`) is read back out for the HTTP response is
> not on the record's field list, and unmodeled JSON fields are silently dropped on deserialize.
> **MUST load `dotnet-models`.**

> ⚠ Step 2 (create path) — the SDK's retry behavior on a non-idempotent `POST` is not what the
> options names suggest: whether a failed `CreateSubscription` can be re-sent by the resilience
> pipeline (and what `Timeout` actually bounds) decides how strong the client-side idempotency
> guard (`Reference` + `FindSubscription`) must be. **MUST load
> `dotnet-configuration-resilience`.**

> ⚠ Step 4 (error boundary) — which exception types actually reach a `catch` (Case A vs Case B per
> operation, `TryGetRawError` is not a catch-all on typed errors) determines the whole catch
> ladder. **MUST load `dotnet-error-handling`** — plus the two mandatory hazard rows in §4.

> ⚠ Testing — the SDK's test seam is a specific constructor argument, not an interface; match the
> repo's existing test framework/assertion style. **MUST load `dotnet-testing`** before writing
> integration tests.

## 4. REQUIRED READING

Load **before implementation starts** (this sheet deliberately does not carry their contents):

- `dotnet-client-initialization` — step 0, client construction & DI registration.
- `dotnet-authentication` — step 0, Basic credentials wiring.
- `dotnet-calling-endpoints` — steps 1–3, every operation call.
- `dotnet-models` — steps 1–3, enums/unions/response mapping.
- `dotnet-error-handling` — step 4, the exception boundary.
- `dotnet-configuration-resilience` — step 2, retry/timeout semantics behind the create path.
- `dotnet-testing` — tests for the integration layer.

Mandatory hazard rows (verbatim) — `System.Text.Json.JsonException` reaches the boundary from two
directions and they need opposite handling:

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

- **Assumed** package version `1.0.2` (the map's source stamp, tag `v1.0.2`); confirm NuGet
  resolves `AsadAli.AdvancedBilling.Sdk` at that version.
- **Assumed** US hosting (`ServerEnvironment.Us`, `*.chargify.com`) for sandbox `cp-exp-1`; EU
  accounts would need `ServerEnvironment.Eu` and the `.Eu.*` override points instead.
- **Assumed** the eShopOnWeb userId is stable and unique per user; it is stored as the Maxio
  customer `reference` (the API enforces `reference` uniqueness — that constraint is what makes
  ensure-customer idempotent, per the `CreateCustomer` map row's notes).
- **Assumed** per the brief that plans need no payment method/trial/setup fee, so
  `CreateSubscription` is sent with only `CustomerId` + `ProductHandle` (+ optional `Reference`);
  all payment/trial fields left null.
- **UNVERIFIED (map-silent):** whether `ListProductsForProductFamily`'s `string productFamilyId`
  accepts the `handle:eshop-subscribe` format — only `ReadProductFamily`'s notes mention a
  `handle:` prefix, and that operation's SDK signature takes `int id`, so the notes do not
  transfer. The plan therefore resolves the family handle → numeric id at runtime via
  `ListProductFamilies` (match `.ProductFamily.Handle`, cache the result); never hardcode the id.
- **UNVERIFIED (live-traffic only):** whether the real 422 body of `CreateCustomer` matches the
  generated `CustomerErrorResponse1`/`Errors` shape — see the trust caveat in §2.5; the
  best-effort-then-raw fallback directive covers both outcomes.
- **Blockers:** none.
