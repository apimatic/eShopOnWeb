# Maxio Advanced Billing integration — eShopOnWeb recurring subscriptions

## 1. Scope & sequence

Additive to the existing one-time commerce flow. All endpoints JWT-authenticated, on the PublicApi project under `/api/`. SDK: NuGet `AsadAli.AdvancedBilling.Sdk`, root namespace `MaxioAdvancedBilling`.

| # | Step | SDK operations used |
|---|---|---|
| 1 | Install package, register client in DI, wire config (`Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:Environment`, `Maxio:ProductFamilyHandle`, optional `Maxio:BaseUrl`) | `AddMaxioAdvancedBillingClient` / `new MaxioAdvancedBillingClient(...)` |
| 2 | `GET /api/subscription-plans` — resolve product family by handle, list its products | `ProductFamilies.ListProductFamilies`, `Products.ListProductsForProductFamily` |
| 3 | `POST /api/subscriptions` — ensure customer (idempotent by reference), create subscription (idempotent by reference), confirm state | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Subscriptions.FindSubscription`, `Subscriptions.CreateSubscription`, (`Products.ReadProductByHandle`) |
| 4 | `GET /api/my-subscriptions` — resolve user → customer id, list that customer's subscriptions | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |
| 5 | Error boundary + config/resilience wiring | (skills, not operations) |

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

Namespaces you will need (add a separate `using` per kind — C# does not import child namespaces transitively):

| Kind | Namespace |
|---|---|
| Client, client options, `ServerOptions`/`ProductionOptions` | `MaxioAdvancedBilling` |
| Controllers (operation groups) | `MaxioAdvancedBilling.Api` |
| Records (request/response models) | `MaxioAdvancedBilling.Models` |
| Enums (`StringEnum<T>` — not C# enums) | `MaxioAdvancedBilling.Models.Enums` |
| Typed error classes (`…Error`) | `MaxioAdvancedBilling.Errors` |
| `SdkException<T>` | `MaxioAdvancedBilling.Core.Exceptions` |
| `RawError`, `ApiError` | `MaxioAdvancedBilling.Core.ErrorResponse` |
| `BasicAuthCredentials` | `MaxioAdvancedBilling.Core.Authentication.Basic` |
| `RetryOptions` | `MaxioAdvancedBilling.Core.Configuration` |
| `ServerEnvironment` | `MaxioAdvancedBilling.Servers` |

### 2.1 Client construction, auth, environment

| Fact | Contract | Source |
|---|---|---|
| Client class | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` — **only** constructor: `new MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` (Getting a client) |
| Options class | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` — properties: `Environment: ServerEnvironment` · `Retry: RetryOptions` · `Server: ServerOptions` · `BasicAuth: BasicAuthCredentials?` | `sdk-map.md` |
| Auth (Basic) | `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = "<api key>", Password = "x" }` — **Username = the Maxio API key, Password = the literal string `"x"`** | `sdk-map.md` (Servers & auth) |
| Environment enum | `MaxioAdvancedBilling.Servers.ServerEnvironment` — values `ServerEnvironment.Us` (wire `US`, default) and `ServerEnvironment.Eu` (wire `EU`). There is no sandbox/production switch — sandbox vs live is decided by which site's API key + subdomain you point at. | `sdk-map.md`, `map/models/enums.md` |
| Subdomain | `options.Server.Production.Us.Site = "<subdomain>"` (US template `https://{site}.chargify.com`; defaults to `"subdomain"` — must be set). EU analogue: `options.Server.Production.Eu.Site`. | `sdk-map.md` (Servers & auth) |
| Base-URL override | `options.Server.Production.Us.BaseUrl = "<url>"` replaces the whole US production template (e.g. to a mock/dev host). `Maxio:BaseUrl` from configuration maps here. | `sdk-map.md`, `Servers/ProductionOptions.cs` |
| Timeout / retries | `options.Retry` is `MaxioAdvancedBilling.Core.Configuration.RetryOptions`; every member is `required` — start from `RetryOptions.Default()` and mutate. `Timeout` is `TimeSpan?`. | `sdk-map.md` |
| DI registration | `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = …; o.Environment = …; o.Server.Production.Us.Site = …; });` (from `ServiceCollectionExtensions.cs`) | `sdk-map.md` |
| API groups | Every group is a property on the client: `client.ProductFamilies`, `client.Products`, `client.Customers`, `client.Subscriptions` (controllers in `MaxioAdvancedBilling.Api`). | `sdk-map.md` |

### 2.2 Operations

Signatures verbatim. "nullable, no default" parameters must be passed explicitly — pass `null` to skip.

#### 2.2.1 `client.ProductFamilies` — resolve the family by handle

| | |
|---|---|
| Method | `ListProductFamilies(MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, System.DateTimeOffset? startDate, System.DateTimeOffset? endDate, System.DateTimeOffset? startDatetime, System.DateTimeOffset? endDatetime, System.Threading.CancellationToken ct = default)` |
| HTTP | `GET /product_families.json` |
| Returns | `System.Collections.Generic.IReadOnlyList<MaxioAdvancedBilling.Models.ProductFamilyResponse>` — envelope: **one field** `ProductFamily (product_family): ProductFamily?` |
| Inner `ProductFamily` fields | `Id (id): int?` · `Name (name): string?` · `Handle (handle): string?` · `AccountingCode`, `Description`, `CreatedAt`, `UpdatedAt`, `ArchivedAt` — all nullable |
| Error | **Case B** — `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`: `StatusCode: System.Net.HttpStatusCode` · `ReadAsString(): string` · `ReadAsBytes()` · `ReadAsJson<T>()` |
| Pagination | none |
| **Source** | `operations/ProductFamilies.md` |

There is **no lookup-by-handle operation for product families**. Resolve the configured family (`Maxio:ProductFamilyHandle`) by calling `ListProductFamilies` (pass all five optional params as `null`) and matching `resp.ProductFamily.Handle == configuredHandle` client-side; use its `Id`. Note `ReadProductFamily(int id, ct)` takes an **`int`** — its documentation mentions a `handle:my-family` format, but the generated signature cannot accept it; do not try to pass a handle there.

#### 2.2.2 `client.Products` — list plans / read one plan

| | |
|---|---|
| Method | `ListProductsForProductFamily(string productFamilyId, MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, MaxioAdvancedBilling.Models.ListProductsFilter? filter, System.DateTimeOffset? startDate, System.DateTimeOffset? endDate, System.DateTimeOffset? startDatetime, System.DateTimeOffset? endDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, System.Threading.CancellationToken ct = default)` |
| HTTP | `GET /product_families/{product_family_id}/products.json` |
| Returns | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>` — envelope: **one field** `Product (product): Product` (**required**) |
| Inner `Product` fields you read | `Id (id): int?` · `Name (name): string?` · `Handle (handle): string?` · `PriceInCents (price_in_cents): long?` · `Interval (interval): int?` · `IntervalUnit (interval_unit): IntervalUnit?` · trial settings: `TrialPriceInCents (trial_price_in_cents): long?`, `TrialInterval (trial_interval): int?`, `TrialIntervalUnit (trial_interval_unit): IntervalUnit?` · card requirement: `RequireCreditCard (require_credit_card): bool?`, `RequestCreditCard (request_credit_card): bool?` · `ProductFamily (product_family): ProductFamily?` |
| `IntervalUnit` enum | `Day (day)` · `Month (month)` — `StringEnum<T>` in `MaxioAdvancedBilling.Models.Enums` |
| Error | **Case A** — `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` [fallback] |
| Pagination | **manual** `page` + `perPage` (defaults 1 / 20) — loop until a short page |
| **Source** | `operations/Products.md`, `records-3-Of-Su.md` (`Product`) |

For the default target plan: `ReadProductByHandle(string apiHandle, System.Threading.CancellationToken ct = default)` → `ProductResponse` (single field `Product (product): Product`, required) — **Case B** `SdkException<RawError>`, 404 surfaces as `ex.Error.StatusCode == HttpStatusCode.NotFound`. **Source:** `operations/Products.md`.

#### 2.2.3 `client.Customers` — idempotent customer + user's subscriptions

| | |
|---|---|
| Lookup by reference | `ReadCustomerByReference(string reference, System.Threading.CancellationToken ct = default)` — `reference` is a **query param** (`reference` ← `reference`) · `GET /customers/lookup.json` · returns `MaxioAdvancedBilling.Models.CustomerResponse` — envelope: **one field** `Customer (customer): Customer` (required) · **Case B** `SdkException<RawError>` — **404 means "no such customer"; that is your create signal, not an outage** |
| Create | `CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, System.Threading.CancellationToken ct = default)` — `body` nullable, no default → must pass · `POST /customers.json` · returns `CustomerResponse` · **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`: `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| Create request model | `CreateCustomerRequest` — **one field**: `Customer (customer): MaxioAdvancedBilling.Models.CreateCustomer` (**required**). `CreateCustomer` fields: `FirstName (first_name): string` **required** · `LastName (last_name): string` **required** · `Email (email): string` **required** · `Reference (reference): string?` ← **the dedupe key** · `Organization`, `Address`, `Address2`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `CcEmails`, `VatNumber`, `TaxExempt`, `TaxExemptReason`, `ParentId`, `SalesforceId` — all optional |
| `Customer` (response) fields | `Id (id): int?` · `Reference (reference): string?` · `FirstName`, `LastName`, `Email`, `Organization`, `CreatedAt`, `UpdatedAt`, … — all nullable |
| List customer's subscriptions | `ListCustomerSubscriptions(int customerId, System.Threading.CancellationToken ct = default)` — `GET /customers/{customer_id}/subscriptions.json` · returns `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>` · **Case B** `SdkException<RawError>` · no pagination (returns all for that customer) |
| **Source** | `operations/Customers.md`, `records-1-Ac-Cr.md` (`CreateCustomer`), `records-2-Cr-Ne.md` (`CustomerResponse`, `Customer`, `CustomerErrorResponse1`), `records-4-Su-We.md` (`SubscriptionResponse`) |

**Customer idempotency (the double-click rule).** Create the customer with `Reference` = a stable per-eShopOnWeb-user value (the app's user id — the exact value is `YOUR CALL — not in the map`). Flow: `ReadCustomerByReference(reference)` → found ⇒ reuse `.Customer.Id`; 404 ⇒ `CreateCustomer` with the same `Reference`. Maxio enforces uniqueness itself — its own Notes: *"you may only create one customer for a given reference value … If provided, the `reference` value must be unique"* — so a concurrent double-create loses the race with a **422**, never two customers; on 422 re-run the reference lookup. There is no server-side upsert, so this lookup-then-create is the only mechanism the SDK offers.

#### 2.2.4 `client.Subscriptions` — create (idempotent) + lookup + read

| | |
|---|---|
| Create | `CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, System.Threading.CancellationToken ct = default)` — `body` nullable, no default → must pass · `POST /subscriptions.json` · returns `MaxioAdvancedBilling.Models.SubscriptionResponse` · **Case A** `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`: `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| Create request model | `CreateSubscriptionRequest` — **one field**: `Subscription (subscription): MaxioAdvancedBilling.Models.CreateSubscription` (**required**). Fields you set: `ProductHandle (product_handle): string?` **or** `ProductId (product_id): int?` · `CustomerId (customer_id): int?` **or** `CustomerReference (customer_reference): string?` · `Reference (reference): string?` ← **subscription dedupe key** · `PaymentProfileId (payment_profile_id): int?` — omit for no card · `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` · `Metafields (metafields): IReadOnlyDictionary<string,string>?` · `DeferSignup (defer_signup): bool? = false`. **No field on the model is required** — requiredness comes from the operation's Notes, below. |
| Payment-profile rules | CreateSubscription's Notes: *"Payment information may be required to create a subscription, depending on the options for the Product being subscribed."* The product's own flags (`Product.RequireCreditCard`, `Product.RequestCreditCard`) are the in-model evidence: a plan configured not to require a card accepts a create with no `PaymentProfileId` and no card attributes; a plan that requires one returns **422** (`ErrorListResponse1`). Whether the sandbox products are configured card-free is a Maxio site-config fact — see §5. |
| Lookup by reference | `FindSubscription(string? reference, System.Threading.CancellationToken ct = default)` — `reference` nullable, no default → must pass · `GET /subscriptions/lookup.json` · returns `SubscriptionResponse` · **Case A** `SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback] — **404 = no subscription with that reference** |
| Read one | `ReadSubscription(int subscriptionId, System.Collections.Generic.IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionInclude>? include, System.Threading.CancellationToken ct = default)` — `include` nullable, no default → pass `null` · **Case B** `SdkException<RawError>` |
| List (site-wide) | `ListSubscriptions(...)` — 14 optional params (`state`, `product`, `productPricePointId`, `coupon`, `couponCode`, `dateField`, `startDate`, `endDate`, `startDatetime`, `endDatetime`, `metadata`, `direction`, `sort`, `include`), `page = 1`, `perPage = 20` · **Case B** · manual pagination. **It has no customer or customer-reference filter** — do not use it for "my subscriptions"; use `Customers.ListCustomerSubscriptions(customerId)` (§2.2.3). |
| **Source** | `operations/Subscriptions.md`, `records-2-Cr-Ne.md` (`CreateSubscription`), `records-3-Of-Su.md` (`Subscription`), `records-4-Su-We.md` (`SubscriptionResponse`) |

**Subscription idempotency.** `CreateSubscription` carries `Reference` and `FindSubscription` looks it up — same pattern as customers: before creating, `FindSubscription("<stable per-user-per-plan reference>")`; `TryGetNoContent` (404) ⇒ create with that `Reference`; hit ⇒ reuse the returned subscription. On a 422 from a lost race, re-run the lookup.

#### 2.2.5 `SubscriptionResponse` → `Subscription` — the fields the endpoints confirm back

Envelope: `SubscriptionResponse` has **one field** `Subscription (subscription): Subscription?` (nullable — null-check before reading). `Subscription` (`MaxioAdvancedBilling.Models.Subscription`), all nullable:

| Field (C#) | Wire name | Type | Used for |
|---|---|---|---|
| `Id` | `id` | `int?` | Maxio subscription id |
| `State` | `state` | `Models.Enums.SubscriptionState?` | live state (enum below) |
| `CurrentPeriodEndsAt` | `current_period_ends_at` | `DateTimeOffset?` | **next-billing date** |
| `ProductPriceInCents` | `product_price_in_cents` | `long?` | price, scalar fallback |
| `Product` | `product` | `Models.Product?` | nested product: `Handle`, `Name`, `PriceInCents`, `Interval`, `IntervalUnit` |
| `Customer` | `customer` | `Models.Customer?` | nested customer (`Id`, `Reference`) |
| `Reference` | `reference` | `string?` | your dedupe key, echoed back |
| `BalanceInCents` | `balance_in_cents` | `long?` | outstanding balance |
| `PaymentCollectionMethod` | `payment_collection_method` | `Models.Enums.CollectionMethod?` | `Automatic (automatic)` / `Remittance (remittance)` / `Prepaid (prepaid)` / `Invoice (invoice)` |

`SubscriptionState` — full value list (`MaxioAdvancedBilling.Models.Enums.SubscriptionState`, `StringEnum<T>`; member name is the literal C# identifier, parenthesized value is the wire value):

`Pending (pending)` · `FailedToCreate (failed_to_create)` · `Trialing (trialing)` · `Assessing (assessing)` · `Active (active)` · `SoftFailure (soft_failure)` · `PastDue (past_due)` · `Suspended (suspended)` · `Canceled (canceled)` · `Expired (expired)` · `Paused (paused)` · `Unpaid (unpaid)` · `TrialEnded (trial_ended)` · `OnHold (on_hold)` · `AwaitingSignup (awaiting_signup)`

(`SubscriptionStateFilter` — the filter enum for `ListSubscriptions`, not the response type — has a different value set: `Active`, `Canceled`, `Expired`, `ExpiredCards`, `OnHold`, `PastDue`, `PendingCancellation`, `PendingRenewal`, `Suspended`, `TrialEnded`, `Trialing`, `Unpaid`.)

**Source:** `records-3-Of-Su.md` (`Subscription`), `records-4-Su-We.md` (`SubscriptionResponse`), `map/models/enums.md`.

**Trust note on the nested `Product`.** `Subscription.Product` is the only place a subscription response carries the plan handle — the `Subscription` record has no scalar `product_handle`/`product_id` field. Whether the wire always populates `product` (and `product_price_in_cents`) on create/list responses cannot be confirmed from the map or SDK source — **UNVERIFIED**. Defensive directive: read plan identity best-effort (`Subscription.Product?.Handle`, `Subscription.Product?.PriceInCents ?? Subscription.ProductPriceInCents`), fall back to `Subscription.ProductPriceInCents` alone, and if a plan handle is mandatory re-fetch the subscription or look the plan up by its configured handle — never assume the nested object is present.

#### 2.2.6 Error handling — exception types and reading a failure

- Every operation is **throw-based**; there are no no-throw `…Result` variants anywhere in this SDK. On an error status the SDK throws `MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>` exposing `.Error`.
- **Case A (typed)** — `TError` is a generated `MaxioAdvancedBilling.Errors.<Operation>Error` with status-specific `TryGet…(out …)` accessors plus the inherited `TryGetRawError(out RawError)` fallback. Which accessors: see each row above.
- **Case B (raw)** — `TError` is `MaxioAdvancedBilling.Core.ErrorResponse.RawError`: `StatusCode: System.Net.HttpStatusCode`, `ReadAsString(): string`, `ReadAsBytes()`, `ReadAsJson<T>()`. The Case-B operations in scope: `ListProductFamilies`, `ReadProductByHandle`, `ReadCustomerByReference`, `ListCustomerSubscriptions`, `ReadSubscription`, `ListSubscriptions`.
- **422 validation shape** — `CreateCustomer`/`CreateSubscription` 422s: `CreateSubscriptionError.TryGetErrorListResponse1(out ErrorListResponse1)` where `ErrorListResponse1.Errors (errors): System.Collections.Generic.IReadOnlyList<string>` (**required** — a flat list of messages). `CreateCustomer` 422s go through `CustomerErrorResponse1.Errors (errors): Models.Errors`, and that `Errors` record documents only `PerPage (per_page)` / `PricePoint (price_point)` lists — a shared model that clearly does not describe customer-validation messages. **Treat the customer-422 body shape as untrusted (UNVERIFIED)**: read it best-effort via the typed accessor, and when that yields nothing useful fall back to `TryGetRawError(out var raw)` + `raw.ReadAsString()` for the message.
- 404s: on Case B ops check `ex.Error.StatusCode == HttpStatusCode.NotFound` (customer-by-reference, product-by-handle); on Case A ops use the typed accessor (`FindSubscriptionError.TryGetNoContent`) or `TryGetRawError` and inspect its `StatusCode`.

---

## 3. Trap notes (attached to the step where they bite)

- ⚠ Step 1 (client registration) — the `HttpClient`/handler pipeline must be long-lived and reused via `IHttpClientFactory`, not rebuilt per request; the SDK client wrapper over it may be transient. **MUST load `dotnet-client-initialization`** before wiring the client.
- ⚠ Step 1 (credentials) — credentials go on `options.BasicAuth` before/inside the DI callback and must come from configuration, not literals; get the exact property shape and per-environment handling there. **MUST load `dotnet-authentication`** before wiring credentials.
- ⚠ Step 1 (config/resilience) — the SDK's retry/timeout options do **not** bound a whole call the way an `HttpClient` timeout would: `RetryOptions.Timeout` is per-attempt, the retry status-gate covers only some methods, and a transport failure is retried on every verb — so a non-idempotent write can execute more than once and no setting fully disables it. Whether a failed write can be re-sent is exactly the question this raises for the two create calls above. **MUST load `dotnet-configuration-resilience`** before tuning the client, and also for the manual `page`/`perPage` loops in §2.2.2.
- ⚠ Steps 2–4 (first call) — many optional parameters have no C# default and mis-bind in a positional call; call the list/search operations with named arguments (`ct:` included). **MUST load `dotnet-calling-endpoints`** before the first `client.{Group}.{Operation}(...)` call.
- ⚠ Steps 3–4 (request models) — enums are `StringEnum<T>` (built from static members or `Type.FromValue(wire)`), not C# enums; records are immutable with `init`-only setters and `required` members must be set in the initializer. **MUST load `dotnet-models`** before constructing `CreateCustomerRequest`/`CreateSubscriptionRequest` or reading `SubscriptionState`.
- ⚠ Step 5 (error boundary) — `System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:
  - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
  - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.
  
  **MUST load `dotnet-error-handling`** before writing that boundary. (Relevant here: the map's `CustomerErrorResponse1.Errors` model plainly doesn't describe a real customer-validation body — a drifted non-2xx shape is a live possibility on exactly the 422 path this integration depends on.)
- ⚠ Testing — the `HttpClient` constructor argument is the test seam; match the project's existing test framework and assertion style. **MUST load `dotnet-testing`** before stubbing the SDK in tests.

---

## 4. REQUIRED READING

Load **before implementation starts**; this sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — DI registration, `HttpClient` lifetime, factory wiring for `MaxioAdvancedBillingClient`. |
| `dotnet-authentication` | Step 1 — `BasicAuthCredentials` shape (API key as username, `"x"` as password), loading from config, rotation. |
| `dotnet-configuration-resilience` | Step 1 — retry/timeout semantics (`Timeout` bounds, which verbs retry), base-URL/subdomain override, manual pagination loops. |
| `dotnet-calling-endpoints` | Steps 2–4 — named-argument calling, optional-parameter binding, async/cancellation on every operation. |
| `dotnet-models` | Steps 3–4 — `StringEnum<T>` construction, `required` members, wire names vs C# names on request payloads. |
| `dotnet-error-handling` | Step 5 — the `SdkException` catch ladder, `RawError` reading, and the two-directional `JsonException` hazard above. |
| `dotnet-testing` | Step 5 — faking at the `HttpClient` seam without depending on SDK internals. |

---

## 5. Assumptions & Blockers

- **Assumption** — sandbox site config: the products serving as plans exist in the family whose handle `Maxio:ProductFamilyHandle` names, and are configured **not to require a credit card** (`require_credit_card` false). The SDK exposes the flag (`Product.RequireCreditCard`) but cannot create or verify the site config; if a plan does require a card, `CreateSubscription` returns 422 and POST /api/subscriptions cannot succeed without a payment profile. Verify in the Maxio sandbox admin before wiring the endpoint.
- **Assumption** — `Maxio:Environment` is a site-level choice expressed through the API key + subdomain (US vs EU); the SDK's `ServerEnvironment.Us`/`.Eu` selects hosting only. Mapping the config string to the enum value and validating it is the app's configuration work (`YOUR CALL — not in the map`).
- **Assumption** — the customer `Reference` value format (e.g. the eShopOnWeb user id) and the subscription `Reference` value format (per user+plan) are chosen by the app; Maxio only requires uniqueness per customer reference (`YOUR CALL — not in the map`).
- **Blocker (live-verification required)** — whether `SubscriptionResponse.Subscription` populates the nested `Product` (plan handle) on create/list responses. Marked **UNVERIFIED** in §2.2.5 with the defensive fallback; only live sandbox traffic can settle it.
- **Blocker (live-verification required)** — the exact 422 body shape Maxio sends for customer validation, given the drifted shared `Errors` model in the SDK (§2.2.6). Marked **UNVERIFIED**; the boundary must read it best-effort with a raw-string fallback.
