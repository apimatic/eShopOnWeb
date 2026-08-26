# Maxio Advanced Billing integration plan — eShopOnWeb PublicApi

Recurring-subscription billing on `src/PublicApi` (JWT-authenticated endpoints), Maxio Advanced Billing as
billing system of record. Additive to the existing one-time commerce flow.

## 1. Scope & sequence

| # | Step | SDK operations used |
|---|---|---|
| 1 | Add NuGet package `AsadAli.AdvancedBilling.Sdk` **1.0.2** to `src/PublicApi` (`dotnet add package AsadAli.AdvancedBilling.Sdk --version 1.0.2`). Version verified published on nuget.org; matches the SDK source tag `v1.0.2`. Package id ≠ root namespace: install by package id, `using MaxioAdvancedBilling;` in code. | — |
| 2 | Bind a `MaxioOptions` POCO from the `Maxio:` config section (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl`). Register the SDK client in DI. | client construction (sheet §B) |
| 3 | `GET /api/subscription-plans` — resolve configured family handle → numeric id (cached), list products, map to plan DTO (name, handle, price, interval). | `ProductFamilies.ListProductFamilies`, `ProductFamilies.ListProductsForProductFamily` |
| 4 | `POST /api/subscriptions` — identity from JWT → stable customer reference; find-or-create customer; find-or-create subscription by deterministic subscription reference; return subscription DTO. | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Subscriptions.FindSubscription`, `Subscriptions.CreateSubscription` |
| 5 | `GET /api/my-subscriptions` — lookup customer by reference (404 ⇒ empty list), list their subscriptions, map to DTO (plan, price, state, next-billing). | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |
| 6 | Error-translation boundary around all SDK calls (SDK exceptions → ProblemDetails). | error model (sheet §D) |
| 7 | Tests for the integration layer. | — |

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

### A. Operations

**`client.ProductFamilies.ListProductFamilies`** — `GET /product_families.json` · map: `operations/ProductFamilies.md`
- Signature: `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 params nullable with **no C# default → must pass explicitly** (pass `null`).
- Returns: `IReadOnlyList<ProductFamilyResponse>`. Envelope: `ProductFamilyResponse.ProductFamily (product_family): ProductFamily?` (nullable — null-check).
- `ProductFamily` fields read: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?` (`records-3-Of-Su.md`).
- Use: match `ProductFamily.Handle == Maxio:ProductFamilyHandle` (`eshop-subscribe`) client-side; take `Id`. Cache the resolved id (handles are stable config; the id lookup is once per app lifetime). `ReadProductFamily` cannot be used for this — its signature is `ReadProductFamily(int id, …)`; the `handle:my-family` format its notes mention is not expressible through the typed `int` parameter.
- Error: **Case B** `SdkException<RawError>` (`StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`). Pagination: none.

**`client.ProductFamilies.ListProductsForProductFamily`** — `GET /product_families/{product_family_id}/products.json` · map: `operations/ProductFamilies.md`
- Signature: `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 8 params `dateField … include` are nullable with **no default → must pass explicitly** (pass `null`); `page`/`perPage` defaulted.
- `productFamilyId` is a **`string`** — pass the resolved numeric id as `id.Value.ToString(CultureInfo.InvariantCulture)`.
- Returns: `IReadOnlyList<ProductResponse>`. Envelope: `ProductResponse.Product (product): Product !req` (required, non-nullable).
- `Product` fields read (`records-3-Of-Su.md`): `Name (name): string?`, `Handle (handle): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `ArchivedAt (archived_at): DateTimeOffset?` (skip archived products when `includeArchived` is left null — still filter defensively on `ArchivedAt == null`).
- Error: **Case A** `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback]. Pagination: manual `page`+`perPage` (default 20/page — two products expected; loop pages if `Count == perPage`).

**`client.Customers.ReadCustomerByReference`** — `GET /customers/lookup.json` · map: `operations/Customers.md`
- Signature: `ReadCustomerByReference(string reference, CancellationToken ct = default)` (query param `reference`).
- Returns: `CustomerResponse`. Envelope: `CustomerResponse.Customer (customer): Customer !req`.
- `Customer` fields read (`records-2-Cr-Ne.md`): `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?`.
- Error: **Case B** `SdkException<RawError>` — customer-not-found surfaces as `ex.Error.StatusCode == HttpStatusCode.NotFound`; that 404 is the find-or-create branch signal, not a failure.

**`client.Customers.CreateCustomer`** — `POST /customers.json` · map: `operations/Customers.md`
- Signature: `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly.
- Request: `CreateCustomerRequest.Customer (customer): CreateCustomer !req`. `CreateCustomer` (`records-1-Ac-Cr.md`): **`FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`** — all three required in the object initializer; plus `Reference (reference): string?` — **always set** to the stable eShopOnWeb user id (see Idempotency, §E).
- Returns: `CustomerResponse` (as above).
- Error: **Case A** `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. ⚠ `CustomerErrorResponse1.Errors (errors)` is typed as the shared `Errors` record, which the map lists with only `PerPage (per_page)` / `PricePoint (price_point)` fields (`records-2-Cr-Ne.md`, `Models/Errors.cs`) — that shape cannot represent customer validation messages, so the typed 422 payload is suspect. **UNVERIFIED** (only live traffic can confirm the real 422 body). Directive: extract messages best-effort from the typed payload, fall back to `TryGetRawError(out var raw)` + `raw.ReadAsString()`; on any 422 from `CreateCustomer`, re-run `ReadCustomerByReference` before surfacing an error (a duplicate-reference race means the customer now exists).

**`client.Subscriptions.FindSubscription`** — `GET /subscriptions/lookup.json` · map: `operations/Subscriptions.md`
- Signature: `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` nullable, **no default → must pass explicitly**.
- Returns: `SubscriptionResponse`. Envelope: `SubscriptionResponse.Subscription (subscription): Subscription?` (**nullable** — null-check even on 200).
- Error: **Case A** `SdkException<FindSubscriptionError>` — `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback]. The 404 accessor is the not-yet-subscribed branch signal.

**`client.Subscriptions.CreateSubscription`** — `POST /subscriptions.json` · map: `operations/Subscriptions.md`
- Signature: `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must be passed explicitly.
- Request: `CreateSubscriptionRequest.Subscription (subscription): CreateSubscription !req`. `CreateSubscription` (`records-2-Cr-Ne.md`) — set exactly:
  - `ProductHandle (product_handle): string?` — the plan handle (`eshop-pro` / `basic-plan`);
  - `CustomerId (customer_id): int?` — the Maxio customer id from find-or-create;
  - `Reference (reference): string?` — deterministic, e.g. `{userReference}:{productHandle}` (see §E).
  - Set **no** payment fields (`PaymentProfileId`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes` stay null) — the products are configured "payment method not required"; the operation notes confirm payment info is only required depending on product configuration.
- Returns: `SubscriptionResponse` (nullable `Subscription` — null-check).
- Error: **Case A** `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. `ErrorListResponse1.Errors (errors): IReadOnlyList<string> !req` (`records-2-Cr-Ne.md`).

**`client.Customers.ListCustomerSubscriptions`** — `GET /customers/{customer_id}/subscriptions.json` · map: `operations/Customers.md`
- Signature: `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)`.
- Returns: `IReadOnlyList<SubscriptionResponse>` (each envelope's `Subscription` nullable).
- `Subscription` fields read (`records-3-Of-Su.md`): `Id (id): int?`, `State (state): SubscriptionState?`, `Product (product): Product?` (→ `Handle`, `Name`, `PriceInCents`), `ProductPriceInCents (product_price_in_cents): long?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `CancelAtEndOfPeriod (cancel_at_end_of_period): bool?`, `CanceledAt (canceled_at): DateTimeOffset?`, `Reference (reference): string?`.
- **Next-billing date**: the SDK's `Subscription` has **no** `next_billing_at` field; the `UpdateSubscription` notes state the server does not return `next_billing_at` and to read `current_period_ends_at` instead — expose `CurrentPeriodEndsAt` as the next-billing/renewal date.
- Error: **Case B** `SdkException<RawError>`. Pagination: none (endpoint returns all).

### B. Client construction, auth, server

Map: `sdk-map.md` (*Getting a client*, *Servers & auth*).

- Client: `MaxioAdvancedBillingClient` (namespace `MaxioAdvancedBilling`) — only ctor: `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`.
- Options: `MaxioAdvancedBillingClientOptions` (namespace `MaxioAdvancedBilling`) — properties: `Environment: ServerEnvironment`, `Retry: RetryOptions`, `Server: ServerOptions`, `BasicAuth: BasicAuthCredentials?`.
- Auth (HTTP Basic): `options.BasicAuth = new BasicAuthCredentials { Username = "<Maxio:ApiKey>", Password = "x" }` — `BasicAuthCredentials` is in namespace `MaxioAdvancedBilling.Core.Authentication.Basic`; password is the literal string `"x"`.
- Environment: `options.Environment = ServerEnvironment.Us` (default; `MaxioAdvancedBilling.Servers`). US base template `https://{site}.chargify.com`.
- Subdomain: `options.Server.Production.Us.Site = "<Maxio:Subdomain>"` (`cp-exp-1`).
- BaseUrl override: when `Maxio:BaseUrl` is set, `options.Server.Production.Us.BaseUrl = "<Maxio:BaseUrl>"` **verbatim** (replaces the derived template; use instead of setting `Site`).
- DI alternative (`ServiceCollectionExtensions.cs`): `services.AddMaxioAdvancedBillingClient(o => { …same assignments… })`.
- `using` directives needed: `MaxioAdvancedBilling`, `MaxioAdvancedBilling.Api` (controller types, if referenced), `MaxioAdvancedBilling.Models`, `MaxioAdvancedBilling.Models.Enums`, `MaxioAdvancedBilling.Core.Authentication.Basic`, `MaxioAdvancedBilling.Core.Configuration` (only if touching `RetryOptions`), `MaxioAdvancedBilling.Core.Exceptions` / wherever `SdkException<>` lives per the error skill, `MaxioAdvancedBilling.Errors` (typed error payloads), `MaxioAdvancedBilling.Servers` (only if naming `ServerEnvironment`). Child namespaces are **not** imported transitively — one `using` per kind.

### C. Enums (all `StringEnum<T>` records, namespace `MaxioAdvancedBilling.Models.Enums`; construct via static members or `Type.FromValue("wire")`) — map: `models/enums.md`

| Enum | Members (C# member = wire value) |
|---|---|
| `IntervalUnit` | `Day (day)`, `Month (month)` |
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` |

("Live" states for the my-subscriptions view per the enum's doc summary: `active`, `trialing`, `assessing` is transient — do not base access decisions on it.)

### D. Error model (applies to every call)

All operations are **throw-only** (no `…Result` no-throw variants exist in this SDK). On error status the SDK throws `SdkException<TError>` with `.Error: TError`. Case A ops throw `SdkException<{Operation}Error>` with status-specific `TryGet…(out …)` accessors plus inherited `TryGetRawError(out RawError)`; Case B ops throw `SdkException<RawError>` (`StatusCode: HttpStatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`). Per-operation cases and accessors are in §A. `TryGetRawError` is the fallback on typed errors, not a catch-all.

### E. Idempotency

- **Customer** — server-side uniqueness of `reference` **is enforced** (CreateCustomer notes: "you may only create one customer for a given reference value"). Flow: `ReadCustomerByReference(userRef)` → 404 ⇒ `CreateCustomer` with `Reference = userRef` → 422 (race) ⇒ re-`ReadCustomerByReference`. A double-click never creates two customers.
- **Subscription** — the map carries **no** server-side uniqueness statement for subscription `reference`. **UNVERIFIED.** Defensive flow (works regardless): deterministic reference `{userRef}:{productHandle}`; `FindSubscription(reference)` → 404 (`TryGetNoContent`) ⇒ `CreateSubscription` with that `Reference`; on `CreateSubscription` 422, re-run `FindSubscription` and return the existing subscription if found before surfacing an error. Belt-and-braces: the subscribe flow may instead/also use `ListCustomerSubscriptions` and match `Product.Handle` + a live `State`.

## 3. Trap notes

- ⚠ Step 2 (client registration) — the ctor takes an `HttpClient`, but the signature says nothing about who owns that client or how long the handler pipeline must live; getting this wrong exhausts sockets under load. **MUST load `dotnet-client-initialization`** before wiring DI.
- ⚠ Step 2 (auth) — when credentials must be supplied relative to client construction, and how to load the API key from configuration rather than hardcoding it, are not visible from the options type. **MUST load `dotnet-authentication`**.
- ⚠ Steps 3–5 (calling endpoints) — the list/lookup operations carry many nullable parameters with **no C# default**; a positional call mis-binds them. How to call these safely (named arguments) is a usage-layer concern. **MUST load `dotnet-calling-endpoints`**.
- ⚠ Steps 3–5 (models) — SDK enums are `StringEnum<T>` records, not C# enums, and unmodeled JSON fields are dropped on deserialize; both bite when mapping `Product`/`Subscription` onto DTOs (e.g. comparing `State`). **MUST load `dotnet-models`**.
- ⚠ Step 6 (error boundary) — which operations are Case A vs Case B and how the `TryGet…` accessors behave determines the whole catch ladder; see also the two mandatory `JsonException` hazards in REQUIRED READING. **MUST load `dotnet-error-handling`**.
- ⚠ Step 2 (resilience) — whether a failed `CreateSubscription` POST can be re-sent by the retry layer (a non-idempotent write executing twice), and what `Timeout` actually bounds, are consequences the option names do not reveal; this interacts directly with §E. **MUST load `dotnet-configuration-resilience`** before tuning or accepting retry defaults.
- ⚠ Step 7 (tests) — the test seam for SDK-calling code is specific (the `HttpClient` ctor argument); stubbing the wrong seam produces tests that assert nothing. **MUST load `dotnet-testing`**.

## 4. REQUIRED READING

Load **before implementation starts**; this sheet deliberately does not carry their contents.

- `dotnet-client-initialization` — step 2 (client construction & DI registration).
- `dotnet-authentication` — step 2 (Basic credentials wiring).
- `dotnet-calling-endpoints` — steps 3–5 (every operation call).
- `dotnet-models` — steps 3–5 (request/response model construction & mapping).
- `dotnet-error-handling` — step 6 (the exception boundary).
- `dotnet-configuration-resilience` — step 2 (retry/timeout/base-URL behaviour).
- `dotnet-testing` — step 7 (integration-layer tests).

Mandatory hazard rows for the error boundary — `System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:

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

- **Assumption — US hosting.** `ServerEnvironment.Us` assumed (default; subdomain `cp-exp-1` on `chargify.com`). If the account is EU-hosted, switch to `ServerEnvironment.Eu` and the `.Eu.*` server options.
- **Assumption — JWT claims.** The JWT carries a stable user id claim usable as the Maxio customer `reference`, plus email. `CreateCustomer` requires `FirstName`/`LastName`/`Email` (`!req`) — if the token lacks name claims, the endpoint must derive them from the eShopOnWeb user profile or reject with a 400 asking the user to complete their profile.
- **Assumption — family id caching.** The product-family handle→id resolution is cached in memory for app lifetime; handles are stable configuration.
- **UNVERIFIED — customer 422 payload shape.** The generated `Errors` record behind `CustomerErrorResponse1.Errors` carries only `per_page`/`price_point` fields, which cannot represent customer validation errors; the sheet's defensive directive (best-effort typed read, raw-body fallback, re-lookup on 422) stands regardless.
- **UNVERIFIED — subscription reference uniqueness.** No server-side uniqueness guarantee for subscription `reference` appears in the SDK surface; §E's check-then-create + 422-recheck flow does not depend on it.
- **Out of scope** — metered component `api-call` usage reporting (per request).
- **Blockers** — none.
