# maxio-plan.md — Maxio Advanced Billing subscription billing for eShopOnWeb (PublicApi)

Additive, parallel subscription capability on `src/PublicApi` (JWT-authenticated endpoints), Maxio
Advanced Billing as system of record. Package `AsadAli.AdvancedBilling.Sdk`, root namespace
`MaxioAdvancedBilling` (note: package id ≠ using-namespace). .NET 8 host; the SDK targets
`netstandard2.0` and runs on it.

## 1. Scope & sequence

| # | Step | Maxio operations used |
|---|------|-----------------------|
| 1 | Config binding (`Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl`) + SDK client registration in PublicApi DI (sandbox targeting, optional BaseUrl override) | — |
| 2 | Plan catalog — resolve product family by handle, list its products → `GET /api/subscription-plans` | `ProductFamilies.ListProductFamilies`, `ProductFamilies.ListProductsForProductFamily` |
| 3 | Single-product resolve by handle (`eshop-pro` default target; never cache numeric IDs) | `Products.ReadProductByHandle` |
| 4 | Idempotent customer ensure for the JWT user (lookup-first, create on miss) | `Customers.ReadCustomerByReference`, `Customers.ListCustomers` (`q` search), `Customers.CreateCustomer` |
| 5 | Idempotent subscribe → `POST /api/subscriptions`; confirm plan/price/state/next-billing | `Subscriptions.FindSubscription`, `Subscriptions.CreateSubscription`, `Subscriptions.ReadSubscription` |
| 6 | Shopper's subscriptions → `GET /api/my-subscriptions` | `Customers.ListCustomerSubscriptions` (or `Subscriptions.ReadSubscription` per stored id) |
| 7 | Error boundary (Case A/B ladder + `JsonException` handling) and resilience/config tuning | — |
| 8 | Tests for the integration layer | — |

Idempotency levers (both documented, see rows): a **customer `reference` value is unique** — one
customer per reference (`Customers.CreateCustomer` Notes, `operations/Customers.md`) — and
`Subscriptions.FindSubscription(reference)` reads a subscription back by the app-supplied
`reference` set at creation (`operations/Subscriptions.md`). The format of those reference values
and the userId↔customerId mapping persistence are application decisions.

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

### 2.1 Operations

Every listed parameter with no default **must be passed explicitly** — pass `null` to skip a
nullable one. All operations are throw-only; there are **no** `…Result`/no-throw variants in
this SDK.

| Use | Call (client accessor) | Signature (verbatim) | Returns | Error case + accessors | Pagination | Source |
|---|---|---|---|---|---|---|
| List all product families (match `Handle` to configured handle) | `client.ProductFamilies.ListProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductFamilyResponse>` | Case B: `MaxioAdvancedBilling.Core.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | none (returns the site's families; no page params exist) | `operations/ProductFamilies.md` |
| List products in the resolved family | `client.ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>` | Case A: `MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError` — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | manual `page`+`perPage` (defaults 1/20) | `operations/ProductFamilies.md` |
| Resolve one plan by handle (`eshop-pro`, `basic-plan`) | `client.Products.ReadProductByHandle` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | `MaxioAdvancedBilling.Models.ProductResponse` | Case B: `SdkException<RawError>` — 404 = unknown handle | none | `operations/Products.md` |
| Find customer by app reference (exact match) | `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | `MaxioAdvancedBilling.Models.CustomerResponse` | Case B: `SdkException<RawError>` — 404 = no such reference | none | `operations/Customers.md` |
| Search customers (Notes: search by email, via `q`) | `client.Customers.ListCustomers` | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` | `IReadOnlyList<MaxioAdvancedBilling.Models.CustomerResponse>` | Case B: `SdkException<RawError>` | manual `page`+`perPage` (defaults 1/50) | `operations/Customers.md` |
| Create the Maxio customer on miss | `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` | `MaxioAdvancedBilling.Models.CustomerResponse` | Case A: `MaxioAdvancedBilling.Errors.CreateCustomerError` — `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Customers.md` |
| Idempotent subscribe lookup (before create: double-click guard) | `client.Subscriptions.FindSubscription` | `FindSubscription(string? reference, CancellationToken ct = default)` | `MaxioAdvancedBilling.Models.SubscriptionResponse` | Case A: `MaxioAdvancedBilling.Errors.FindSubscriptionError` — `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |
| Create the subscription | `client.Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | `MaxioAdvancedBilling.Models.SubscriptionResponse` | Case A: `MaxioAdvancedBilling.Errors.CreateSubscriptionError` — `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Subscriptions.md` |
| Read one subscription (confirmation / by stored id) | `client.Subscriptions.ReadSubscription` | `ReadSubscription(int subscriptionId, IReadOnlyList<MaxioAdvancedBilling.Models.Enums.SubscriptionInclude>? include, CancellationToken ct = default)` | `MaxioAdvancedBilling.Models.SubscriptionResponse` | Case B: `SdkException<RawError>` | none | `operations/Subscriptions.md` |
| List a customer's subscriptions (`GET /api/my-subscriptions`) | `client.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>` | Case B: `SdkException<RawError>` | none | `operations/Customers.md` |

### 2.2 Request models

**`MaxioAdvancedBilling.Models.CreateSubscriptionRequest`** (`records-2-Cr-Ne.md`) — envelope with
one field: `Subscription (subscription): CreateSubscription !req`.

**`MaxioAdvancedBilling.Models.CreateSubscription`** (`records-2-Cr-Ne.md`) — **nothing is marked
required**; `required?` selects nothing. The operation's Notes drive what you pass
(`operations/Subscriptions.md`: identify product via `product_id`/`product_handle`; identify
customer via `customer_id`/`customer_reference`; payment info "may be required … depending on the
options for the Product being subscribed"). Fields carried for this flow (wire names verbatim):

| Field | Wire name | Type |
|---|---|---|
| `ProductHandle` | `product_handle` | `string?` |
| `ProductId` | `product_id` | `int?` |
| `CustomerId` | `customer_id` | `int?` |
| `CustomerReference` | `customer_reference` | `string?` |
| `Reference` | `reference` | `string?` — app-supplied unique reference; later readable via `FindSubscription` |
| `PaymentCollectionMethod` | `payment_collection_method` | `CollectionMethod?` |
| `PaymentProfileId` | `payment_profile_id` | `int?` — **omit**: no card capture in this flow |
| `DeferSignup` | `defer_signup` | `bool? = false` |

Left out on purpose (present on the record, not tied to this flow by the Notes): `custom_price`,
coupon fields, `credit_card_attributes`/`bank_account_attributes`/`payment_profile_attributes`,
`components`, `calendar_billing`, `metafields`, net terms, currency, author/agreement fields and
the rest of the record's optional members. **No compiler catches a field you drop.**

**Runtime-corrected (sandbox 422):** a paid product ($299/mo, no trial) returned HTTP 422 "No payment
method was on file for the $299.00 balance" when created with only product + customer + reference.
The map documents no field that *guarantees* a paid subscription without a payment profile; absence
of payment fields is still the mechanism for no-card flows, but for products whose options require
payment the documented knobs are (semantics beyond the map are UNVERIFIED): `PaymentCollectionMethod
= CollectionMethod.Remittance` (valid on Relationship Invoicing; map says nothing about waiving the
payment requirement), `DeferSignup = true` (→ awaiting-signup state; activation later attempts
payment per the ActivateSubscription Notes), `NetTerms` (string on create; accepted format not
documented in the map). Never send a "skip payment" flag — there is none in the request model.

**`MaxioAdvancedBilling.Models.CreateCustomerRequest`** (`records-1-Ac-Cr.md`) — envelope:
`Customer (customer): CreateCustomer !req`.

**`MaxioAdvancedBilling.Models.CreateCustomer`** (`records-1-Ac-Cr.md`) — required:
`FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email):
string !req`. Optional, used here: `Reference (reference): string?` (the **unique** per-app-user
key — the map's documented uniqueness constraint), `Organization (organization): string?`,
`Phone (phone): string?`. Country/state, when ever sent, are ISO-2/ISO-3166-2 per the Notes.

### 2.3 Response envelopes — one level down

| Envelope | Single field | Inner record fields this integration reads |
|---|---|---|
| `ProductFamilyResponse` (`records-3-Of-Su.md`) | `ProductFamily (product_family): ProductFamily?` | `ProductFamily`: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `ArchivedAt (archived_at): DateTimeOffset?` |
| `ProductResponse` (`records-3-Of-Su.md`) | `Product (product): Product !req` | `Product`: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `RequireCreditCard (require_credit_card): bool?`, `RequestCreditCard (request_credit_card): bool?`, `Taxable (taxable): bool?`, `TrialInterval (trial_interval): int?`, `TrialPriceInCents (trial_price_in_cents): long?`, `ExpirationIntervalUnit (expiration_interval_unit): ExpirationIntervalUnit?`, `ArchivedAt (archived_at): DateTimeOffset?`, `ProductFamily (product_family): ProductFamily?` |
| `CustomerResponse` (`records-2-Cr-Ne.md`) | `Customer (customer): Customer !req` | `Customer`: `Id (id): int?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?`, `CreatedAt (created_at): DateTimeOffset?` |
| `SubscriptionResponse` (`records-4-Su-We.md`) | `Subscription (subscription): Subscription?` — **nullable; null-check before reading** | `Subscription` (full record, from SDK source `Models/Subscription.cs` — the map row is truncated there): `Id (id): int?`, `State (state): SubscriptionState?`, `Reference (reference): string?`, `ProductPriceInCents (product_price_in_cents): long?`, **`CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`** — the documented "when the next regularly scheduled attempted charge will occur", i.e. the **next-billing-date to confirm to the user; the record has no `next_billing_at` response field**, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `CurrentPeriodStartedAt (current_period_started_at): DateTimeOffset?`, `ActivatedAt (activated_at): DateTimeOffset?`, `ExpiresAt (expires_at): DateTimeOffset?`, `BalanceInCents (balance_in_cents): long?`, `Currency (currency): string?`, `Customer (customer): Customer?`, `Product (product): Product?` (same `Product` record as above — plan name/handle/price/interval for the confirmation), `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `CancelAtEndOfPeriod (cancel_at_end_of_period): bool?`, `CouponCodes (coupon_codes): IReadOnlyList<string>?` |

### 2.4 Error payloads

| Type | Fields | Notes |
|---|---|---|
| `MaxioAdvancedBilling.Models.ErrorListResponse1` (`records-2-Cr-Ne.md`) | `Errors (errors): IReadOnlyList<string> !req` | 422 payload for `CreateSubscription`. Generated shape is a **flat list of strings**; a real field-keyed 422 body may not match it. Extract best-effort; on any mismatch fall back to `TryGetRawError(out RawError)` → `ReadAsString()` and surface raw body + status. Exact live 422 text — **UNVERIFIED**. |
| `MaxioAdvancedBilling.Models.CustomerErrorResponse1` (`records-2-Cr-Ne.md`) | `Errors (errors): Errors?` | 422 payload for `CreateCustomer`. The generated `MaxioAdvancedBilling.Models.Errors` record carries only `PerPage (per_page): IReadOnlyList<string>?` and `PricePoint (price_point): IReadOnlyList<string>?` — a duplicate-reference/duplicate-email 422 will not fit those two fields; expect empty accessors. **This is a map-visible mismatch between the generated 422 shape and any realistic customer-validation body.** Extract best-effort, fall back to `TryGetRawError(out RawError)` → `ReadAsString()`. — **UNVERIFIED** |
| `MaxioAdvancedBilling.Core.ErrorResponse.RawError` (`sdk-map.md`) | `StatusCode: HttpStatusCode` · `ReadAsBytes(): ReadOnlyMemory<byte>` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` | Case B payload for `ReadCustomerByReference`, `ListCustomers`, `ReadProductByHandle`, `ListProductFamilies`, `ReadSubscription`, `ListCustomerSubscriptions`. 404 = not found (unknown handle/reference/id). |
| `MaxioAdvancedBilling.Errors.FindSubscriptionError` (`operations/Subscriptions.md`) | `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback] | 404 on `FindSubscription` arrives as a no-content body — read it via the `RawError` it hands you, not via a parsed model. |

Duplicate/idempotency collision: the map documents the **constraint** ("you may only create one
customer for a given reference value", `CreateCustomer` Notes) but not the message text a
duplicate produces — **UNVERIFIED**. Defensive directive: lookup-first, and on `CreateCustomer`
422 re-run the lookup (by reference, then by email `q`) and return the existing customer.

### 2.5 Enums (`map/models/enums.md` — `StringEnum<T>` records, NOT C# enums; members are the literal C# names, parenthesized values go on the wire; build via the static member or `Type.FromValue("wire")`; namespace `MaxioAdvancedBilling.Models.Enums`)

| Enum | Members (C# member (wire value)) |
|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` — compare subscriptions with `SubscriptionState.Active`, never with the string `"active"` |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` |
| `IntervalUnit` | `Day (day)`, `Month (month)` |
| `ExpirationIntervalUnit` | `Day (day)`, `Month (month)`, `Never (never)` |
| `SortingDirection` | `Asc (asc)`, `Desc (desc)` |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` |
| `SubscriptionInclude` | `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)` — pass `null` for `ReadSubscription`'s `include` |
| `ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)` — pass `null` for `ListProductsForProductFamily`'s `include` |

### 2.6 Client construction, auth, servers (all: `sdk-map.md`)

- Only constructor: `new MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` (root namespace `MaxioAdvancedBilling`).
- `MaxioAdvancedBillingClientOptions` properties: `Environment` (`ServerEnvironment`), `Retry` (`RetryOptions`), `Server` (`ServerOptions`), `BasicAuth` (`BasicAuthCredentials?`).
- Auth — Basic only: `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = "<api key>", Password = "x" }` — **Username = the API key, Password = the literal `"x"`**.
- Hosting: `options.Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us` → US template `https://{site}.chargify.com` (`ServerEnvironment.Eu` → `https://{site}.ebilling.maxio.com`). **The map documents no distinct "sandbox" host**: a sandbox is a site in test mode on the same hosts. Derive the base URL for the sandbox from the subdomain via `options.Server.Production.Us.Site = "<Maxio:Subdomain>"` (`{site}` defaults to the subdomain).
- `Maxio:BaseUrl` override: when set, assign it verbatim — `options.Server.Production.Us.BaseUrl = "<Maxio:BaseUrl>"` (documented override point: "To redirect … override `BaseUrl` on the relevant group"). When unset, use the `Site` derivation above. `MaxioAdvancedBilling.Servers.ServerOptions` / `…Servers.ProductionOptions` live in `MaxioAdvancedBilling.Servers`.
- DI alternative exists: `services.AddMaxioAdvancedBillingClient(o => { … })` (`ServiceCollectionExtensions.cs`).
- Every API group is a property on the client: `client.ProductFamilies`, `client.Products`, `client.Customers`, `client.Subscriptions` (namespace `MaxioAdvancedBilling.Api`).
- `MaxioAdvancedBilling.Core.Configuration.RetryOptions` has **all members `required`** — build a full instance or start from `RetryOptions.Default()`.
- The configured `Maxio:ProductFamilyHandle` resolves the family; **numeric IDs are never cached** — the plan catalog and subscribe flows re-resolve by handle (family handle from configuration; product handle per request), per the brief's re-seed constraint.

### 2.7 Judgment calls

| Item | Decision | Label |
|---|---|---|
| Customer reference format & userId↔Maxio-customerId mapping, subscription reference format, their persistence (EF Core InMemory in PublicApi) | application design | YOUR CALL — not in the map |
| Value of `Maxio:Environment`/`MAXIO_ENVIRONMENT` → `ServerEnvironment` mapping (map documents only `Us`/`Eu`) | application config mapping; sandbox targeting itself is via subdomain + site test mode | YOUR CALL — not in the map |
| `ListProductsForProductFamily` accepting a handle-style id (`"handle:eshop-subscribe"`) instead of the numeric id | the map's Notes document the `handle:my-family` format only for `ReadProductFamily` — whose generated signature is `int id`, so it is **not callable** with a handle there; the family-products row's Notes say nothing. Resolve the numeric id via `ListProductFamilies` and pass `id.ToString()`. If you try the handle string anyway, treat the outcome as best-effort — **UNVERIFIED** | UNVERIFIED (facts above from `operations/ProductFamilies.md`) |
| Exact live 422 body shapes vs `ErrorListResponse1` / `CustomerErrorResponse1` (see §2.4) | extract best-effort, fall back to raw body + status | UNVERIFIED |

## 3. Trap notes (load the named skill at the step; the sheet deliberately does not carry its contents)

- ⚠ Step 1 (client registration) — the `HttpClient`/handler pipeline must be long-lived and reused via `IHttpClientFactory` (not rebuilt per request); the SDK client wrapper over it may be transient, and there is a DI extension with its own registration shape. **MUST load `dotnet-client-initialization`** before wiring the client into PublicApi's service container.
- ⚠ Step 1 (credentials) — `BasicAuthCredentials` must be attached with the credentials sourced from configuration (never hard-coded), and 401/403 from Maxio is a config-shaped failure to diagnose before touching call sites. **MUST load `dotnet-authentication`** before setting credentials.
- ⚠ Steps 2–6 (every call) — the list/search operations above take 7–14 nullable parameters with **no C# default**; omitting one is a compile error, and positional calls can mis-bind. Call with named arguments, passing `null` explicitly. **MUST load `dotnet-calling-endpoints`** before the first `client.…` call.
- ⚠ Steps 2–6 (request/response models) — enums are `StringEnum<T>` records (a plain C# enum switch on `Subscription.State` will not compile); `required` envelope members must be set in the object initializer; response reads go one level down into the envelope field; unmodeled JSON fields are silently dropped on deserialize. **MUST load `dotnet-models`** before constructing payloads or mapping responses onto app DTOs.
- ⚠ Steps 2–7 (error boundary) — the SDK splits Case A (typed `{Operation}Error` with `TryGet…` accessors) and Case B (`SdkException<RawError>`) per operation (see §2.1); a `TryGetRawError` is not a catch-all on the typed errors. Write the ladder from the rows, not from a uniform assumption. **MUST load `dotnet-error-handling`** before writing any try/catch around an SDK call.
- ⚠ Step 7 (resilience/config) — whether a failed `POST /subscriptions.json` / `POST /customers.json` can execute more than once (double-create risk against the idempotency design), what `Timeout` actually bounds, and the mechanics of the `ServerOptions` base-URL/site override are not what the option names suggest. **MUST load `dotnet-configuration-resilience`** before tuning or registering the client.
- ⚠ Step 8 (tests) — the `HttpClient` constructor argument is the test seam; match the project's existing framework and assertion style. **MUST load `dotnet-testing`** before stubbing the SDK.

## 4. REQUIRED READING

Load **before implementation starts** — one line each: skill · the step it governs. The sheet deliberately does not carry their contents.

- `dotnet-client-initialization` · Step 1 — client construction, options shape, HttpClient ownership, DI registration.
- `dotnet-authentication` · Step 1 — Basic credentials (API key as username, `"x"` password) sourced from configuration.
- `dotnet-calling-endpoints` · Steps 2–6 — calling operations with named arguments, required-nullable parameters, envelopes, cancellation.
- `dotnet-models` · Steps 2–6 — request records, `StringEnum<T>` enums, wire names vs C# names, union accessors.
- `dotnet-error-handling` · Step 7 — Case A/B catch ladder, `TryGet…` accessors, raw-error fallback, `JsonException` handling.
- `dotnet-configuration-resilience` · Steps 1 & 7 — retries/timeouts semantics, base-URL/site override, pagination handling.
- `dotnet-testing` · Step 8 — faking at the `HttpClient` seam, covering error and edge paths.

Mandatory hazard rows — `System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

## 5. Assumptions & Blockers

- **Assumption:** the four binding keys (`Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl`) receive their values from the environment variables named in the brief (`MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_DEFAULT_PRODUCT_FAMILY`); the key names themselves are the binding contract, and no value is hard-coded anywhere.
- **Assumption (falsified by sandbox 422):** the brief's "payment method not required" held for the no-card flow only on products whose options don't demand payment; the live $299 product rejected a bare create ("No payment method was on file"). Whether `payment_collection_method: remittance`, `defer_signup: true`, or `net_terms` makes that product creatable without a payment profile is not documented in the map — treat as UNVERIFIED and settle against the sandbox before shipping that path.
- **Assumption:** the family handle configured under `Maxio:ProductFamilyHandle` is `eshop-subscribe` in the deployed environment; the code reads the configuration, never the handle literally.
- **UNVERIFIED:** exact live-wire 422 bodies for `CreateCustomer`/`CreateSubscription` (see §2.4) — handled defensively via best-effort typed extraction + raw fallback.
- **UNVERIFIED:** handle-style id support on `ListProductsForProductFamily` (see §2.7) — plan uses the numeric id resolved from `ListProductFamilies`.
- **Blockers:** none — every operation in scope is covered by a map row, and the one truncated map row (`Subscription` record) was resolved from the SDK source.
