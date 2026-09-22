# Maxio Advanced Billing — integration plan (eShopOnWeb subscription billing)

Additive, parallel recurring-subscription capability on `src/PublicApi`. Maxio Advanced Billing is
the system of record. Three JWT-authenticated endpoints:

- `GET /api/subscription-plans` — list plans in the configured product family.
- `POST /api/subscriptions` — subscribe the caller to a plan (idempotent customer + subscription).
- `GET /api/my-subscriptions` — the caller's subscriptions.

Caller identity comes from the JWT `ClaimTypes.Name` claim (the eShop username/email — see
`IdentityTokenClaimService`). That username is the stable Maxio customer **reference**.

---

## 1. Scope & sequence

1. **Vendor SDK + build wiring** — copy `maxio-csharp-sdk` source into `external/maxio-csharp-sdk/`,
   opt it out of central package management, add a `ProjectReference` from `Infrastructure`, add to solution.
2. **Config + client + fail-fast DI** (Infrastructure/Maxio) — bind `Maxio:` options, construct
   `MaxioAdvancedBillingClient` (Basic auth, US env, site/base-url), validate credentials at registration.
3. **`GET /api/subscription-plans`** — resolve the configured family **handle → numeric id** via
   `client.ProductFamilies.ListProductFamilies` (the products-by-family endpoint takes the numeric id, NOT
   the handle — **verified live: passing the handle 404s**), then
   `client.ProductFamilies.ListProductsForProductFamily(familyId, …)`.
4. **`POST /api/subscriptions`** — ensure customer (`Customers.ReadCustomerByReference` → on 404
   `Customers.CreateCustomer`) then `Subscriptions.CreateSubscription`; reconcile via `Subscriptions.FindSubscription`.
5. **`GET /api/my-subscriptions`** — `Customers.ReadCustomerByReference` → `Customers.ListCustomerSubscriptions(id)`.

No capability here is invented; every path maps to a map operation above.

---

## 2. CONTRACT SHEET

> ⚠ **Signatures are generated code, verbatim.** Every parameter name below is the literal C#
> identifier; the cancellation-token parameter really is named `ct`, so named arguments write `ct:`.
> Optional query/list params have **no C# default** and must be passed explicitly (pass `null` to skip) —
> call them with **named arguments**.
> ⚠ **Every SDK type is written fully-qualified with the namespace its source path implies**
> (`Models/` → `MaxioAdvancedBilling.Models`, `Models/Enums/` → `MaxioAdvancedBilling.Models.Enums`,
> `Errors/` → `MaxioAdvancedBilling.Errors`, `Core/…` → the path's namespace), taken from the path the
> map gives for THAT type — never from where a neighbouring type sits.

### Client construction / auth / server (source: `sdk-map.md` → *Getting a client*, *Servers & auth*)

- Client: `new MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`
  — the only constructor. DI helper `services.AddMaxioAdvancedBillingClient(Action<options>)` exists
  (source: `ServiceCollectionExtensions.cs`) — confirm shape at impl time via **dotnet-client-initialization**.
- Auth: **Basic** — `options.BasicAuth = new BasicAuthCredentials { Username = <API key>, Password = "x" }`
  (`Core/…`; username is the Chargify API key, password is the literal `"x"`). Basic works only with
  `Us`/`Eu` environments (direct `chargify.com`). `BasicAuthCredentials` namespace resolved at impl time.
- Environment: `options.Environment = ServerEnvironment.Us` (namespace `MaxioAdvancedBilling.Servers`).
  US chosen: sandbox is US and Basic auth requires US/EU. Not a bound key (see §5 row 8).
- Server host: `Production`·`Us` template `https://{site}.chargify.com`, `{site}` default `"subdomain"`.
  - When `Maxio:BaseUrl` is **set** → `options.Server.Production.Us.BaseUrl = <BaseUrl>` (verbatim, overrides template).
  - Else → `options.Server.Production.Us.Site = <Subdomain>`.

### Operations

| # | Controller.Op · signature (verbatim) | Request model → fields used | Response → fields read | Error case + accessors | Pagination | source |
|---|---|---|---|---|---|---|
| A0 | `client.ProductFamilies.ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, RequestOptions? requestOptions = null, CancellationToken ct = default)` | all `null` | `IReadOnlyList<ProductFamilyResponse>`; each `.ProductFamily` → `Id`, `Handle` (match `Maxio:ProductFamilyHandle` → get numeric `Id`) | **Case B** `SdkException<RawError>` | none (single response) | ProductFamilies.md; `Models/ProductFamily.cs`, `Models/ProductFamilyResponse.cs` |
| A | `client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `productFamilyId` = family **numeric id** (from A0 — the route needs the id, not the handle; **verified live**); all filters `null`; `includeArchived: false`; `page`/`perPage` looped | `IReadOnlyList<ProductResponse>`; each `.Product` → `Id`, `Name`, `Handle`, `Description`, `PriceInCents`, `Interval`, `IntervalUnit`, `ProductFamily` | **Case A** `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | **page/perPage** (offset) — loop until short page | ProductFamilies.md; `Models/Product.cs`, `Models/ProductResponse.cs` |
| B | `client.Customers.ReadCustomerByReference(string reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `reference` = username | `CustomerResponse.Customer` → `Id`, `Reference`, `Email` | **Case B** `SdkException<RawError>` (404 ⇒ not found) | none | Customers.md; `Models/CustomerResponse.cs`, `Models/Customer.cs` |
| C | `client.Customers.CreateCustomer(CreateCustomerRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateCustomerRequest.Customer` = `CreateCustomer { FirstName, LastName, Email (required); Reference = username }` | `CustomerResponse.Customer.Id` | **Case A** `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | Customers.md; `Models/CreateCustomerRequest.cs`, `Models/CreateCustomer.cs`, `Models/CustomerErrorResponse1.cs` |
| D | `client.Subscriptions.CreateSubscription(CreateSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateSubscriptionRequest.Subscription` = `CreateSubscription { CustomerReference or CustomerId; ProductHandle; Reference = deterministic }` | `SubscriptionResponse.Subscription` → `Id`, `State`, `NextAssessmentAt`, `CurrentPeriodEndsAt`, `ProductPriceInCents`, `Product`, `Reference` | **Case A** `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | Subscriptions.md; `Models/CreateSubscriptionRequest.cs`, `Models/CreateSubscription.cs`, `Models/Subscription.cs`, `Models/ErrorListResponse1.cs` |
| E | `client.Subscriptions.FindSubscription(string? reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `reference` = deterministic subscription reference (pass explicitly) | `SubscriptionResponse.Subscription` (same fields as D) | **Case A** `SdkException<FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback] | none | Subscriptions.md; `Errors/FindSubscriptionError.cs` |
| F | `client.Customers.ListCustomerSubscriptions(int customerId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `customerId` from B | `IReadOnlyList<SubscriptionResponse>`; each `.Subscription` (same fields as D) | **Case B** `SdkException<RawError>` | none (single response — no cap) | Customers.md; `Models/SubscriptionResponse.cs` |

**CreateSubscription optional-field decisions** (each optional field carries a purpose or it is omitted):
- `ProductHandle` (`product_handle`): the plan handle chosen by the caller. **Set** — identifies the plan.
- `CustomerReference` (`customer_reference`) **or** `CustomerId` (`customer_id`): link to the existing
  customer. **Set** `CustomerId` (from step B/C — unambiguous int) — avoids re-resolving reference server-side.
- `Reference` (`reference`): deterministic `eshop:{username}:{productHandle}`. **Set** — used as the
  reconciliation key (§ UNKNOWN OUTCOMES). Purpose: traceability + reconciliation. Not provider-unique (see §6).
- `PaymentCollectionMethod` (`payment_collection_method`): **SET = `CollectionMethod.Remittance`.**
  **Verified live:** with the field omitted (provider default `automatic`) a fresh subscribe to the $299 plan
  is rejected `422 "No payment method was on file for the $299.00 balance"` — automatic collection attempts to
  charge the balance at signup. `remittance` (Relationship Invoicing) creates the subscription **active** and
  invoices the customer, so a shopper subscribes without capturing a card. (Enum member from
  `Models/Enums/CollectionMethod.cs`.)
- Every other `CreateSubscription` field is omitted: card/bank/payment-profile attributes (no card capture),
  trial/billing-date imports (`next_billing_at`, `initial_billing_at`, `defer_signup` — `import/migration only`),
  coupons, offers, components, currency — none apply to the hero flow.

**CreateCustomer optional-field decisions:** `Reference` (`reference`) **set** = username (the durable unique
claim, § DUPLICATE CLAIMS). `FirstName`/`LastName`/`Email` are `required`. All other fields omitted
(no address/tax/branding data in scope) → provider defaults.

### Enums (source files)

| Enum | Members used | notes | source |
|---|---|---|---|
| `SubscriptionState` (StringEnum) | `Active`,`Trialing`,`Pending`,`Assessing`,`PastDue`,`SoftFailure` (non-terminal) vs `Canceled`,`Expired`,`FailedToCreate`,`Unpaid`,`TrialEnded`,`OnHold`,`Suspended`,`Paused`,`AwaitingSignup` | read wire value via StringEnum accessor (confirm `.Value` via **dotnet-models**); surfaced to caller verbatim | `Models/Enums/SubscriptionState.cs` |
| `IntervalUnit` (StringEnum) | read-only (`month`/`day`) for plan display | | `Models/Enums/IntervalUnit.cs` |
| `CollectionMethod` (StringEnum) | not set (omit) | `Automatic`/`Remittance`/`Prepaid`/`Invoice` exist | `Models/Enums/CollectionMethod.cs` |

### CROSS-OPERATION INVARIANTS

| invariant | operations | enforced where |
|---|---|---|
| The configured `Maxio:ProductFamilyHandle` must resolve to a numeric family id before products can be listed | `ListProductsForProductFamily` (A) ← `ListProductFamilies` (A0) | `MaxioSubscriptionService.ResolveProductFamilyIdAsync` (cached per process) |
| The `productHandle` a caller may pass to `POST /api/subscriptions` must be one of the plan handles returned by `ListProductsForProductFamily` | `CreateSubscription` (D) ← `ListProductsForProductFamily` (A) | `MaxioSubscriptionService.SubscribeAsync` validates the handle against the live plan list; unknown → `BillingProviderException` (400) |
| The `customer_id`/`customer_reference` on `CreateSubscription` must be an existing customer | `CreateSubscription` (D) ← `ReadCustomerByReference`/`CreateCustomer` (B/C) | `MaxioSubscriptionService.EnsureCustomerAsync` — customer ensured first, its `Id` passed to D |

---

## 3. Trap notes

- **Step 2 (client & DI):** the SDK `HttpClient`/handler pipeline must be long-lived and reused, not
  rebuilt per request; mis-lifetime causes socket exhaustion. **MUST load dotnet-client-initialization.**
- **Step 2 (auth):** credentials must be set before/at client construction and captured in the singleton;
  a rotated secret's take-effect timing is a hazard. **MUST load dotnet-authentication.**
- **Steps 3–5 (calling ops):** optional list/query params have no C# default and mis-bind in positional
  calls; whether a write takes a real idempotency key is a per-op fact (the injected `Idempotency-Key`
  header is **not** one). **MUST load dotnet-calling-endpoints.**
- **Steps 3–5 (models):** enums are `StringEnum<T>` (not C# enums), request bodies are `init`-only records
  with `required` members, response payloads are wrapped one level down. **MUST load dotnet-models.**
- **Steps 3–5 (errors):** each op is Case A (typed `{Op}Error`) or Case B (`RawError`); reading the wrong
  accessor or an SDK-only catch ladder lets a `JsonException` escape. **MUST load dotnet-error-handling.**
- **Step 2 (config/resilience):** `Timeout` is per-attempt not total; `HttpMethodsToRetry` decides which
  verbs the SDK resends; `LogRequestBody` logs JSON bodies unredacted and the log env var can arm it.
  **MUST load dotnet-configuration-resilience.**
- **Tests:** the `HttpClient` constructor argument is the fake seam. **MUST load dotnet-testing.**

---

## 4. REQUIRED READING (load all before implementation; contents intentionally not restated here)

| skill (plugin `maxio-platforms-team`) | governs |
|---|---|
| `maxio-platforms-team:dotnet-client-initialization` | client construction + DI registration + HttpClient lifetime (step 2) |
| `maxio-platforms-team:dotnet-authentication` | Basic-auth credential wiring + rotation (step 2) |
| `maxio-platforms-team:dotnet-calling-endpoints` | invoking ops, named-argument binding of optional params (steps 3–5) |
| `maxio-platforms-team:dotnet-models` | building request records, reading `StringEnum`/wrapped responses (steps 3–5) |
| `maxio-platforms-team:dotnet-error-handling` | Case A/B catch ladders + the two `JsonException` directions (all steps) |
| `maxio-platforms-team:dotnet-configuration-resilience` | retries, per-attempt timeout, total budget, logging posture (step 2) |
| `maxio-platforms-team:dotnet-testing` | faking the `HttpClient` seam (tests) |

Mandatory hazard rows (both apply — `System.Text.Json.JsonException` reaches the boundary two ways):
- A drifted/malformed **2xx** body (a missing `required` member) throws `JsonException` from
  deserialization, **not** `SdkException` — an SDK-exception-only catch ladder lets it escape.
- A **non-2xx** body that does not match the operation's generated `{Op}Error` shape throws `JsonException`
  **while the error object is being constructed**, replacing the `SdkException` and destroying the HTTP status.
Both are handled by a final `catch (Exception)` in the integration boundary that maps to a generic 502/500.

---

## 5. PRODUCTION READINESS

| # | Concern | Decision | where in the code |
|---|---|---|---|
| 1 | Credential fail-fast | `AddMaxioBillingServices` validates: `ApiKey` non-blank; and (`BaseUrl` non-blank **or** `Subdomain` non-blank); and `ProductFamilyHandle` non-blank. Throws `InvalidOperationException` at registration → host refuses to start. Basic auth has two parts but password is the constant `"x"`, so only the API-key part can be blank; it is checked. | `Infrastructure/Maxio/MaxioServiceCollectionExtensions.cs` |
| 2 | Secret sourcing & rotation | `Maxio:ApiKey` comes from user-secrets (loaded from env var `MAXIO_API_KEY`). Options object built once at registration and captured in the singleton client → a rotated key needs a process restart. Documented; acceptable for this reference app. | same |
| 3 | Total timeout budget | Each call is bounded by a `CancellationTokenSource` deadline (default 30s) created in the service, passed as `ct:` — this bounds the whole call, since SDK `Timeout` is per-attempt. | `MaxioSubscriptionService` |
| 4 | Write-retry ownership | Writes in scope are `CreateCustomer`/`CreateSubscription` (POST). Default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS, so the SDK never auto-resends these POSTs — safe from duplicate auto-retry. Left at default. | client options (default `Retry`) |
| 5 | Idempotency & ambiguous writes | `CreateCustomer`: real durable key = customer `reference` (server-enforced unique, confirmed in `Api/Customers.cs` remarks). `CreateSubscription`: **no** provider-enforced unique key (source documents uniqueness only for customers) → deterministic `reference` used for reconciliation, not dedup; see §6 Blocker + §DUPLICATE CLAIMS. The generator's `Idempotency-Key` header is not cited. | `MaxioSubscriptionService.SubscribeAsync` |
| 6 | Observability | `IAppLogger`/`ILogger` logs at Information (customer ensured, subscription created with state) and Error (SDK failures with provider status + body string via `RawError.ReadAsString()`). No request bodies logged. | `MaxioSubscriptionService` |
| 7 | Sensitive data | Scope request models (`CreateCustomer`, `CreateSubscription` as built) carry name/email only — no card/bank fields are set (no payment capture). Still: `LogRequestBody` left **off** and `LoggerFactory` set explicitly so the `MAXIOADVANCEDBILLINGCLIENT_LOG` env var cannot arm body logging. Our own logs never echo request bodies. | client options + service logging |
| 8 | Environment selection | `ServerEnvironment.Us` (sandbox is US; Basic auth requires US/EU). Only the `Production` server group is touched. Each deployment sets `Maxio:Subdomain` (or `Maxio:BaseUrl` verbatim). No live-vs-test env switch needed — the whole build targets one configured site; test traffic is kept off any live system by pointing `Maxio:Subdomain`/`BaseUrl` at the sandbox site. | `MaxioServiceCollectionExtensions` |
| 9 | Duplicate prevention under concurrency | **Customer write:** store = Maxio customers, column = `reference`; Maxio's per-site reference uniqueness rejects the second `CreateCustomer` (422), caught → re-read by reference. **Subscription write:** no provider-enforced unique column exists and EF-InMemory neither persists across restart nor enforces unique indexes → **Blocker in §6**, mitigated (YOUR CALL) by per-reference in-process serialization + existence check + reconciliation. | `MaxioSubscriptionService.EnsureCustomerAsync` / `SubscribeAsync`; see §DUPLICATE CLAIMS |
| 10 | Partial results | `ListProductsForProductFamily` is paged (perPage): the service loops pages until a short page; a safety cap (`MaxPlanPages`) bounds it and, if hit, sets `Truncated = true` on the returned result so the caller learns coverage was cut short. `ListCustomerSubscriptions` returns a single un-paged response (SDK contract) — no truncation. | `MaxioSubscriptionService.GetPlansAsync` → `PlanListResult.Truncated` |
| 11 | Startup validation vs the existing test host | `PublicApiIntegrationTests` boots the host (`WebApplicationFactory<Program>`). It must be given placeholder `Maxio:` config (via `appsettings.test.json`/env) so the fail-fast does not abort test boot. Verified by running that project green (§6 task). | `tests/PublicApiIntegrationTests` + `appsettings.test.json` |
| 12 | Ordering & no-op side effects | Customer is ensured (read/create in Maxio) **before** the subscription call; the deterministic subscription reference is computed **before** the create. The subscribe path emits no unconditional outbound notification — nothing is gated on a state change beyond returning the state to the caller. On an idempotent re-subscribe (existing active subscription found) no new create is issued. | `SubscribeAsync` |
| 13 | Unknown outcomes | If `CreateSubscription` transport fails (timeout/`HttpRequestException`), the catch re-reads via `FindSubscription(reference)`; if the subscription is present it is treated as success, else the failure is surfaced. Reference searched by = deterministic subscription `reference`. | `SubscribeAsync` catch block |
| 14 | Provider status & reconciliation clocks | `CreateSubscription`/`FindSubscription`/`ListCustomerSubscriptions` return `subscription.state`. The service reads it verbatim (never defaults an absent state to success — absent state ⇒ surfaced as `unknown`) and returns it to the caller; a `failed_to_create` state is surfaced as a failed subscribe. No two-source reconciliation clock in scope. | `MaxioSubscriptionService` mapping |

> All `where in the code` cells below refer to members of
> `src/Infrastructure/Maxio/MaxioSubscriptionService.cs` unless noted.

### DUPLICATE CLAIMS

| write | where the claim is stored | what rejects the second one | where that rejection is caught | where in the code |
|---|---|---|---|---|
| `CreateCustomer` | Maxio customers, `reference` column (per-site unique, server-enforced) | Maxio returns 422 `CustomerErrorResponse1` on a duplicate reference | `catch (SdkException<CreateCustomerError>)` → re-read `ReadCustomerByReference` and use the existing customer | `MaxioSubscriptionService.EnsureCustomerAsync` |
| `CreateSubscription` | **none durable** — Maxio does not enforce subscription-reference uniqueness; EF-InMemory does not persist/enforce (see §6 Blocker B) | (no unique constraint) — mitigation only: in-process per-reference lock + existence check via `ListCustomerSubscriptions` + reconciliation via `FindSubscription` | mitigation, not a rejection — see §6 | `MaxioSubscriptionService.SubscribeAsync` |

### PAGED READS

| read | what caps it | how the caller learns the answer was cut short | where in the code |
|---|---|---|---|
| `ListProductsForProductFamily` | page loop until a page returns `< perPage`; safety cap `MaxPlanPages` | `PlanListResult.Truncated` bool (→ surfaced on the endpoint response) | `MaxioSubscriptionService.GetPlansAsync` |
| `ListCustomerSubscriptions` | none — single un-paged SDK response | n/a (no cap per SDK contract) | `MaxioSubscriptionService.GetMySubscriptionsAsync` |

### REPEATED OPERATIONS

| operation | what tells you the state actually changed | the effects gated on that | where in the code |
|---|---|---|---|
| `POST /api/subscriptions` (subscribe) | an existing non-terminal subscription to the same product is found via `ListCustomerSubscriptions` ⇒ state did NOT change | gated: the `CreateSubscription` call itself (skipped on an idempotent hit) | `SubscribeAsync` |
| ensure-customer | `ReadCustomerByReference` returns a customer ⇒ no creation | gated: the `CreateCustomer` call (skipped when the customer already exists) | `EnsureCustomerAsync` |

### UNKNOWN OUTCOMES

| write | the operation you re-read with | the reference you search by | where in the code |
|---|---|---|---|
| `CreateSubscription` | `FindSubscription` | deterministic subscription `reference` = `eshop:{username}:{productHandle}` | `SubscribeAsync` transport-failure catch |
| `CreateCustomer` | `ReadCustomerByReference` | customer `reference` = username | `EnsureCustomerAsync` transport-failure/422 catch |

### OPERATION OUTCOMES

| write | the status field | every value it can hold, and what the app does with each | where in the code |
|---|---|---|---|
| `CreateSubscription` | `subscription.state` (`SubscriptionState`) | non-terminal (`active`,`trialing`,`pending`,`assessing`,`past_due`,`soft_failure`,`awaiting_signup`) → success, state returned to caller; terminal-fail (`failed_to_create`) → surfaced as failed subscribe; other terminal (`canceled`,`expired`,`unpaid`,`trial_ended`,`suspended`,`paused`,`on_hold`) → returned verbatim as the subscription's state; **absent** state → surfaced as `unknown` (never defaulted to success) | `MaxioSubscriptionService` mapping / `SubscribeAsync` |

### WRITE ORDER

| write | what exists BEFORE the call | what is written after it returns | where in the code |
|---|---|---|---|
| `CreateSubscription` | the customer (ensured in Maxio, keyed by `reference`=username) and the deterministic subscription `reference` (computed pre-call, the reconciliation key) | the Maxio subscription; its returned `state`/`id`/dates are mapped to the response DTO | `SubscribeAsync` |
| `CreateCustomer` | the deterministic customer `reference` (=username) computed pre-call from the JWT identity | the Maxio customer; its `Id` is used for the subscription call | `EnsureCustomerAsync` |

> Note on "local store": this integration keeps **no durable local mirror** — Maxio is the system of
> record and the machine has only EF-InMemory (lost on restart, no unique-index enforcement). The
> "before the call" artifact is therefore the deterministic **reference** (derived from the JWT identity),
> which is stable across retries and is the key every reconcile/dedup path searches by.

---

## 6. Assumptions & Blockers

**Assumptions**
- Caller identity = JWT `ClaimTypes.Name` (eShop username/email); used verbatim as the Maxio customer
  `reference`. (Confirmed in `IdentityTokenClaimService`.)
- "Plans" = the products in the configured product family; `ListProductsForProductFamily(handle)` lists them.
- Default subscribe target when the caller omits a plan handle = the first plan whose handle matches the
  configured default, else the caller must pass a handle. (Endpoint accepts an explicit `planHandle`.)
- Seeded plans are "payment method not required", but **verified live** that `CreateSubscription` needs
  `payment_collection_method = remittance` (invoice-based) to subscribe without a card — the default
  `automatic` collection tries to charge the balance at signup and returns
  `422 "No payment method was on file for the $299.00 balance"`. (Resolved; no longer `UNVERIFIED`.)

**Blockers** *(headless — decided and proceeding; recorded for the reviewer)*
- **Blocker B — no concurrency-proof claim for the subscription write.** Maxio's source documents
  reference uniqueness only for *customers*, not subscriptions; and this machine has no durable local
  store (EF-InMemory loses data on restart and does not enforce unique indexes). A provider-enforced
  unique constraint on the subscription write therefore does not exist. **Decision (YOUR CALL):** anchor
  idempotency on the durable customer claim, and for the subscription add (a) an in-process per-reference
  `SemaphoreSlim` that serializes a single host's concurrent subscribe calls for the same user,
  (b) a pre-create existence check over `ListCustomerSubscriptions` for a non-terminal subscription to the
  same product, and (c) a deterministic subscription `reference` enabling `FindSubscription` reconciliation.
  This makes the realistic double-click safe on the demo (single-host) topology; true multi-host
  duplicate-safety for the subscription write is not achievable without provider support or a shared
  durable store, neither of which exists in this environment.
