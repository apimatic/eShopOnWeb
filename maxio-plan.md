# Maxio Advanced Billing integration plan — eShopOnWeb PublicApi

Grounded against the bundled SDK map (`maxio-getting-started` skill, SDK stamp: commit `15db14b`, tag `v1.0.2`). Every signature, field, wire name, enum value and error accessor below comes from the cited map page.

## 1. Scope & sequence

Additive recurring-subscription billing on `src/PublicApi` (JWT-authenticated; caller identity from the token). Existing one-time cart/checkout is untouched.

| # | Step | SDK operations used |
|---|---|---|
| 0 | Add NuGet package; bind `Maxio:*` config | — |
| 1 | Register `MaxioAdvancedBillingClient` in DI (PublicApi startup) | client construction |
| 2 | `GET /api/subscription-plans` — resolve configured family handle → id, list its products | `ProductFamilies.ListProductFamilies`, `ProductFamilies.ListProductsForProductFamily` |
| 3 | `POST /api/subscriptions` — ensure customer (lookup → create), then subscribe by product handle | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Subscriptions.FindSubscription`, `Subscriptions.CreateSubscription` |
| 4 | `GET /api/my-subscriptions` — resolve customer by reference, list their subscriptions | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |
| 5 | Integration error boundary (translate SDK exceptions to HTTP responses) | error model below |
| 6 | Tests for the integration layer | — |

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

### 2.0 Package, client construction, auth, server selection

| Fact | Value | Map page |
|---|---|---|
| NuGet package | `AsadAli.AdvancedBilling.Sdk`, version `1.0.2` (the tag this sheet is grounded against) | `sdk-map.md` |
| Root namespace | `MaxioAdvancedBilling` (**differs from the package id**) | `sdk-map.md` |
| Client class | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` — only ctor: `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` |
| Options class | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` — properties: `Environment` (`ServerEnvironment`), `Retry` (`RetryOptions`), `Server` (`ServerOptions`), `BasicAuth` (`BasicAuthCredentials?`) | `sdk-map.md` |
| Auth | HTTP Basic. `options.BasicAuth = new BasicAuthCredentials { Username = "<API key>", Password = "x" }` — password is the literal string `"x"`. Type: `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials` | `sdk-map.md` |
| Environment | `options.Environment = ServerEnvironment.Us` (default) or `.Eu`. Type: `MaxioAdvancedBilling.Servers.ServerEnvironment`. US template `https://{site}.chargify.com`, EU `https://{site}.ebilling.maxio.com` | `sdk-map.md` |
| Subdomain | `options.Server.Production.Us.Site = "cp-exp-1"` (`{site}` defaults to `subdomain`; use `.Eu.Site` when Environment=Eu) | `sdk-map.md` |
| BaseUrl override | `options.Server.Production.Us.BaseUrl = "<Maxio:BaseUrl>"` — verbatim replacement of the whole base URL; when set, it wins over `Site` (use `.Eu.BaseUrl` when Environment=Eu) | `sdk-map.md` |
| DI alternative | `services.AddMaxioAdvancedBillingClient(o => { … })` (from `ServiceCollectionExtensions.cs`) | `sdk-map.md` |
| Retry options | `MaxioAdvancedBilling.Core.Configuration.RetryOptions` — all members `required`; start from `RetryOptions.Default()` | `sdk-map.md` |

Config binding (app-side): `Maxio:ApiKey` → `BasicAuth.Username`; `Maxio:Subdomain` → `Server.Production.*.Site`; `MAXIO_ENVIRONMENT` → `Environment`; `Maxio:BaseUrl` (optional) → `Server.Production.*.BaseUrl` verbatim; `Maxio:ProductFamilyHandle` (`eshop-subscribe`) → family resolution in step 2.

`using` directives the integration needs (child namespaces are **not** imported transitively): `MaxioAdvancedBilling`, `MaxioAdvancedBilling.Core.Authentication.Basic`, `MaxioAdvancedBilling.Servers`, `MaxioAdvancedBilling.Models`, `MaxioAdvancedBilling.Models.Enums`, `MaxioAdvancedBilling.Errors`, plus `MaxioAdvancedBilling.Core.Exceptions` (`SdkException<>`, path-implied from `Core/Exceptions/SdkException.cs`) and `MaxioAdvancedBilling.Core.ErrorResponse` (`RawError`, path-implied from `Core/ErrorResponse/RawError.cs`).

### 2.1 Operations

**`client.ProductFamilies.ListProductFamilies`** — `GET /product_families.json` · `operations/ProductFamilies.md`
- Signature: `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — all 5 filter params nullable, no default → **must pass explicitly** (pass `null`).
- Returns: `IReadOnlyList<ProductFamilyResponse>`; envelope `ProductFamilyResponse.ProductFamily (product_family): ProductFamily?` (**nullable**). `ProductFamily`: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `ArchivedAt (archived_at): DateTimeOffset?` (`records-3-Of-Su.md`).
- Use: find the element whose `ProductFamily.Handle` equals the configured family handle; take its `Id`. No pagination params.
- Error: **Case B** — `SdkException<RawError>` (`Error.StatusCode`, `Error.ReadAsString()`).

**`client.ProductFamilies.ListProductsForProductFamily`** — `GET /product_families/{product_family_id}/products.json` · `operations/ProductFamilies.md`
- Signature: `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — the 8 params `dateField…include` are nullable with no default → **must pass explicitly** (pass `null`); `page`/`perPage` defaulted.
- `productFamilyId` is a **`string`** — pass the resolved numeric family id as `id.ToString()`. (Whether the path segment also accepts a bare handle / `handle:xyz` is not stated in the map — `UNVERIFIED`; the id-resolution path above is fully verified, use it.)
- Returns: `IReadOnlyList<ProductResponse>`; envelope `ProductResponse.Product (product): Product` (**required**). `Product` fields the endpoint returns (`records-3-Of-Su.md`): `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `ArchivedAt (archived_at): DateTimeOffset?`, `RequireCreditCard (require_credit_card): bool?`, `RequestCreditCard (request_credit_card): bool?`.
- Error: **Case A** — `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback].
- Pagination: manual `page` + `perPage` (pass `perPage: 100`; the seeded family is tiny but don't rely on the default 20 silently truncating).

**`client.Products.ReadProductByHandle`** — `GET /products/handle/{api_handle}.json` · `operations/Products.md`
- Signature: `ReadProductByHandle(string apiHandle, CancellationToken ct = default)`.
- Returns: `ProductResponse` (`.Product` required — same `Product` fields as above).
- Use: validate the plan handle in `POST /api/subscriptions` before subscribing (404 ⇒ unknown plan ⇒ 400/404 to caller). Also read `Product.RequireCreditCard (require_credit_card): bool?` — per its source doc (`Models/Product.cs`) it "controls whether a payment profile is required to be entered for customers wishing to sign up on this product": the SDK-readable signal that a product will demand a payment method at signup. `RequestCreditCard (request_credit_card)` is documented as a deprecated value to ignore unless using legacy hosted pages — do not branch on it.
- Error: **Case B** — `SdkException<RawError>`; unknown handle ⇒ `Error.StatusCode == HttpStatusCode.NotFound`.

**`client.Customers.ReadCustomerByReference`** — `GET /customers/lookup.json` · `operations/Customers.md`
- Signature: `ReadCustomerByReference(string reference, CancellationToken ct = default)` (query param `reference`). Exact single-match lookup — prefer it over `ListCustomers`' fuzzy `q` search.
- Returns: `CustomerResponse`; envelope `Customer (customer): Customer` (**required**). `Customer` (`records-2-Cr-Ne.md`): `Id (id): int?`, `Reference (reference): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Organization (organization): string?`, `CreatedAt (created_at): DateTimeOffset?`, …
- Error: **Case B** — `SdkException<RawError>`; no such customer ⇒ `Error.StatusCode == HttpStatusCode.NotFound` ⇒ this is the "create the customer" branch, **not** a failure.

**`client.Customers.CreateCustomer`** — `POST /customers.json` · `operations/Customers.md`
- Signature: `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly**.
- Request: `CreateCustomerRequest.Customer (customer): CreateCustomer` (**required**) (`records-1-Ac-Cr.md`). `CreateCustomer` fields: `FirstName (first_name): string` **`!req`**, `LastName (last_name): string` **`!req`**, `Email (email): string` **`!req`**, `Reference (reference): string?` ← **always set** to the eShopOnWeb user id (idempotency key), `Organization (organization): string?`, `Address/City/State/Zip/Country (address/city/state/zip/country): string?`, `Phone (phone): string?`, `CcEmails (cc_emails): string?`, `Locale (locale): string?`, `TaxExempt (tax_exempt): bool?`, `ParentId (parent_id): int?`.
- Returns: `CustomerResponse` (`.Customer` required).
- Error: **Case A** — `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. ⚠ The 422 payload type `CustomerErrorResponse1.Errors (errors)` is typed as the shared `Errors` record, which models **only** `PerPage (per_page)` and `PricePoint (price_point)` string lists (`records-2-Cr-Ne.md`) — customer-field error messages (e.g. "reference has already been taken") have no modeled home and are dropped on deserialize. **Directive:** never branch on the typed 422 body of CreateCustomer; on *any* 422 treat it as a possible reference race → re-call `ReadCustomerByReference` and use that customer (self-healing). Whether the live 422 body really carries customer-field messages is `UNVERIFIED` (only live traffic could confirm) — the re-lookup directive is correct either way.

**`client.Subscriptions.FindSubscription`** — `GET /subscriptions/lookup.json` · `operations/Subscriptions.md`
- Signature: `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` nullable, no default → **must pass explicitly**.
- Returns: `SubscriptionResponse` (see below).
- Use: idempotency pre-check for `POST /api/subscriptions` — set a deterministic subscription reference (e.g. `"{userId}:{productHandle}"`) and look it up before creating.
- Error: **Case A** — `SdkException<FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback]. 404 ⇒ no subscription with that reference ⇒ safe to create.

**`client.Subscriptions.CreateSubscription`** — `POST /subscriptions.json` · `operations/Subscriptions.md`
- Signature: `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, no default → **must pass explicitly**.
- Request: `CreateSubscriptionRequest.Subscription (subscription): CreateSubscription` (**required**) (`records-2-Cr-Ne.md`). `CreateSubscription` fields this integration sets (all optional in the model; the API requires a customer identifier and a product identifier per the operation's doc notes):
  - `ProductHandle (product_handle): string?` ← the plan handle (`eshop-pro` / `basic-plan`) — handle-based, no numeric ids.
  - `CustomerId (customer_id): int?` ← the ensured Maxio customer's `Id`. (Alternative: `CustomerReference (customer_reference): string?` — same value as the customer `reference`; either identifies the customer. Prefer `CustomerId` since the ensure-step already returns it.)
  - `Reference (reference): string?` ← deterministic per user+plan (idempotency, see `FindSubscription`).
  - `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` ← set `CollectionMethod.Remittance` (wire `remittance`) for card-less signup so billing proceeds by remittance/invoice instead of an automatic card charge. Enum `MaxioAdvancedBilling.Models.Enums.CollectionMethod` (StringEnum): `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)`; per the source doc (`Models/CreateSubscription.cs`), Relationship Invoicing sites accept `remittance`/`automatic`/`prepaid` (`invoice` is legacy Statements only). **Live finding:** with this unset (automatic collection), the sandbox rejected the create with 422 "No payment method was on file for the $299.00 balance" despite the products being seeded "payment method not required". Whether `remittance` suppresses that 422 is `UNVERIFIED` by map/source (only live traffic confirms) — directive: set `Remittance` on every card-less signup; on a still-failing 422, surface `ErrorListResponse1.Errors` verbatim to the caller.
  - Signup-billing timing fields on the same model (`Models/CreateSubscription.cs` doc): `DeferSignup (defer_signup): bool? = false` — `true` creates the subscription in the *Awaiting Signup Date* state (first billing date unknown; set `initial_billing_at` later, or omit it to activate immediately); `InitialBillingAt (initial_billing_at): DateTimeOffset?` — schedules the first billing date. Both change *when* the first charge happens, not whether a card is required — use only if remittance collection is not acceptable.
  - Do **not** set `PaymentProfileAttributes`/`CreditCardAttributes`/`BankAccountAttributes` — no card capture in this integration.
- Returns: `SubscriptionResponse`; envelope `Subscription (subscription): Subscription?` (**nullable** — null-conditional reads) (`records-4-Su-We.md`). `Subscription` fields the integration reads (`records-3-Of-Su.md`):
  - `Id (id): int?`
  - `State (state): SubscriptionState?` (enum table below)
  - `ProductPriceInCents (product_price_in_cents): long?` ← unit price
  - `NextAssessmentAt (next_assessment_at): DateTimeOffset?` and `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` ← **the model has no `next_billing_at` field**; expose "next billing date" as `NextAssessmentAt ?? CurrentPeriodEndsAt`.
  - `Product (product): Product?` ← nested product (`Name`, `Handle`)
  - `Customer (customer): Customer?` ← nested customer
  - `Reference (reference): string?`, `CreatedAt (created_at): DateTimeOffset?`, `ActivatedAt (activated_at): DateTimeOffset?`, `CanceledAt (canceled_at): DateTimeOffset?`, `Currency (currency): string?`
- Error: **Case A** — `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. `ErrorListResponse1.Errors (errors): IReadOnlyList<string>` (**required**) (`records-2-Cr-Ne.md`) — safe to read the message list.

**`client.Customers.ListCustomerSubscriptions`** — `GET /customers/{customer_id}/subscriptions.json` · `operations/Customers.md`
- Signature: `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)`.
- Returns: `IReadOnlyList<SubscriptionResponse>` (same nullable envelope + `Subscription` fields as above). No pagination params — returns all of the customer's subscriptions.
- Use: backs `GET /api/my-subscriptions`. Resolve the caller's Maxio customer via `ReadCustomerByReference` first; 404 there ⇒ the user has never subscribed ⇒ return an empty list (not an error).
- Error: **Case B** — `SdkException<RawError>`.
- Note: `Subscriptions.ListSubscriptions` has **no** customer filter param (its filters are state/product/coupon/date/metadata) — `ListCustomerSubscriptions` is the correct op for per-user listing.

### 2.2 Enums needed (`map/models/enums.md`; namespace `MaxioAdvancedBilling.Models.Enums`)

`SubscriptionState` — StringEnum, members (wire values): `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)`.

`IntervalUnit` — StringEnum, members (wire values): `Day (day)`, `Month (month)`.

`CollectionMethod` — StringEnum, members (wire values): `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)`. Source doc (`Models/Enums/CollectionMethod.cs`): legacy Statements Architecture valid options are `invoice`, `automatic`; current Relationship Invoicing Architecture valid options are `remittance`, `automatic`, `prepaid`.

These are `StringEnum<T>`, **not** C# enums — construction/comparison/serialization rules come from `dotnet-models` (see trap notes).

### 2.3 Error model (applies to every call)

All operations are **throw-only** (no `…Result` no-throw variants exist in this SDK). On an error status the SDK throws `SdkException<TError>` with `.Error: TError`:
- **Case A (typed):** `TError` = generated `{Operation}Error : ApiError` with status-specific `TryGet…(out …)` accessors + inherited `TryGetRawError(out RawError)` fallback. In scope: `ListProductsForProductFamily`, `CreateCustomer`, `FindSubscription`, `CreateSubscription`.
- **Case B (raw):** `TError` = `RawError`: `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`, `ReadAsBytes(): ReadOnlyMemory<byte>`. In scope: `ListProductFamilies`, `ReadProductByHandle`, `ReadCustomerByReference`, `ListCustomerSubscriptions`.

### 2.4 Idempotency design (contract-grounded)

- Customer `reference` is unique per site (CreateCustomer operation notes: "you may only create one customer for a given reference value") → set `Reference` = eShopOnWeb user id on every create.
- Ensure-customer flow: `ReadCustomerByReference` → 404 (Case B `RawError.StatusCode == NotFound`) ⇒ `CreateCustomer` → 422 (possible race) ⇒ re-`ReadCustomerByReference`. A double-click then never creates two customers.
- Subscribe flow: set `CreateSubscription.Reference` to a deterministic `{userId}:{productHandle}` value; `FindSubscription` first (404 ⇒ create). Whether the API *enforces* subscription-reference uniqueness is `UNVERIFIED` (only live traffic could confirm) → also serialize the subscribe flow per user in app code (check-then-create has a race window), and treat an existing non-canceled subscription to the same plan from `ListCustomerSubscriptions` as "already subscribed".
- Numeric ids (family id `3023074`, product ids) are re-seeded per the environment facts — every lookup in this design is by **handle** or by **reference**; the only id used is the family id resolved fresh from `ListProductFamilies` and the customer id returned by the ensure-step.

## 3. Trap notes (named hazards — resolve by loading the named skill, not from this sheet)

> ⚠ Step 1 (client registration) — the `HttpClient`/handler pipeline behind the SDK client must be long-lived and reused via `IHttpClientFactory`, not rebuilt per request; the SDK client wrapper's own lifetime differs from the pipeline's. **MUST load `dotnet-client-initialization`** before wiring DI.

> ⚠ Step 1 (auth) — credentials must be set before the client is constructed (or in the DI callback), and the API key must come from configuration, never source. **MUST load `dotnet-authentication`**.

> ⚠ Steps 2–4 (every call) — call list/search operations with **named arguments**: many optional params have no C# default and mis-bind positionally (the sheet marks them "must pass explicitly"); the cancellation parameter is literally named `ct`. **MUST load `dotnet-calling-endpoints`**.

> ⚠ Steps 2–4 (models) — enums are `StringEnum<T>` not C# enums, records are immutable with `init`-only setters and `required` members, and unmodeled JSON fields are silently dropped on deserialize (this is why the CreateCustomer 422 detail is unreadable — see §2.1). **MUST load `dotnet-models`**.

> ⚠ Step 5 (error boundary) — Case A vs Case B is per-operation (§2.3); `TryGetRawError` on a typed error is not a catch-all; and see the two mandatory `JsonException` rows in §4. **MUST load `dotnet-error-handling`**.

> ⚠ Step 1/5 (resilience) — what the SDK's retry/timeout options actually bound, which failures get re-sent on which verbs, and whether a failed write (CreateSubscription is a POST) can be re-sent behind your back are all non-obvious and bear directly on the idempotency design in §2.4. **MUST load `dotnet-configuration-resilience`** before tuning `RetryOptions` or relying on retries.

> ⚠ Step 6 (tests) — the `HttpClient` constructor argument is the test seam; match the project's existing test framework and assertion style. **MUST load `dotnet-testing`**.

## 4. REQUIRED READING (load **before implementation starts** — this sheet deliberately does not carry their contents)

- `dotnet-client-initialization` — governs step 1 (client construction & DI registration).
- `dotnet-authentication` — governs step 1 (Basic-auth credentials wiring).
- `dotnet-calling-endpoints` — governs steps 2–4 (operation invocation, named arguments, envelopes).
- `dotnet-models` — governs steps 2–4 (request-model construction, StringEnum handling, nullability).
- `dotnet-error-handling` — governs step 5 (the exception boundary). Mandatory for every integration:
  - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
  - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

  **MUST load `dotnet-error-handling`** before writing that boundary.
- `dotnet-configuration-resilience` — governs step 1/5 (retries, timeouts, base-URL/server selection, pagination).
- `dotnet-testing` — governs step 6 (faking the SDK seam in tests).

## 5. Assumptions & Blockers

**Assumptions**
- Customer `reference` = the eShopOnWeb user id from the JWT (e.g. the `sub` claim), stored as a string; the same value is used for `CreateSubscription.CustomerReference` fallback and the deterministic subscription `Reference` (`{userId}:{productHandle}`).
- `GET /api/subscription-plans` returns all non-archived products in the configured family (`ArchivedAt == null` filter applied client-side; `includeArchived` left `null`).
- "Price" is exposed in cents as the SDK models it (`PriceInCents` / `ProductPriceInCents`, `long?`); formatting is an app concern.
- "Next billing date" maps to `Subscription.NextAssessmentAt ?? CurrentPeriodEndsAt` — the SDK's `Subscription` model has no `next_billing_at` field (map-verified).
- The metered component `api-call` is out of scope for these three endpoints (no usage reporting was requested); no component operations are planned.
- `Maxio:BaseUrl` override, when present, is applied to the `Production` group of the selected environment only (no Ebb/event-ingest endpoints are used).

**UNVERIFIED (only live traffic could confirm — defensive directives given above)**
- Whether `ListProductsForProductFamily`'s string `productFamilyId` accepts a bare/`handle:`-prefixed handle (verified id-resolution path used instead).
- Whether CreateCustomer's live 422 body carries customer-field messages (directive: never branch on it; re-lookup by reference).
- Whether subscription `reference` uniqueness is enforced server-side (directive: deterministic reference + per-user serialization + already-subscribed detection).
- Whether `PaymentCollectionMethod = CollectionMethod.Remittance` suppresses the signup 422 "No payment method was on file for the $… balance" on card-less creation (map/source state only which values each billing architecture accepts, not the card-requirement interaction; directive: set `Remittance`, and on a persistent 422 surface `ErrorListResponse1.Errors` verbatim).

**Blockers** — none.
