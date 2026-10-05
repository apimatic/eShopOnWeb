# Maxio Advanced Billing integration plan — eShopOnWeb subscriptions

Adds recurring-subscription billing to eShopOnWeb's `src/PublicApi` (JWT-authenticated), with Maxio
Advanced Billing as the billing system of record. Additive and parallel to the existing cart/checkout
flow. Endpoints: `GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions`.

## 1. Scope & sequence

| # | Step | Maxio operations used |
| --- | --- | --- |
| 1 | Config + client wiring: bind `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl`, `Maxio:Environment`; validate + fail-fast at startup; register the SDK client via DI | (none — client construction only) |
| 2 | `GET /api/subscription-plans` — resolve the configured product family, page through its products, map to plan DTOs | `ListProductsForProductFamily` |
| 3 | Identity bridge: JWT `ClaimTypes.Name` → Identity user → stable user id (the Maxio customer `reference`) | (none) |
| 4 | `POST /api/subscriptions` — ensure Maxio customer (idempotent), validate requested plan against the family's plans, subscribe idempotently via deterministic subscription `reference`, return plan/price/state/next-billing-date | `ListProductsForProductFamily`, `ReadCustomerByReference`, `CreateCustomer`, `FindSubscription`, `CreateSubscription`, `ReadSubscription` |
| 5 | `GET /api/my-subscriptions` — resolve customer, list their subscriptions, map to DTOs | `ReadCustomerByReference`, `ListCustomerSubscriptions` |
| 6 | Local claim/link records (EF) for in-process duplicate rejection and unknown-outcome settling; tests; end-to-end verification against the sandbox | (none) |

## 2. CONTRACT SHEET

⚠ **Signatures are generated code, verbatim.** Each operation that takes input takes ONE request record
as its first parameter, built with an object initializer whose property names are the record's own —
never flat arguments. An operation with no inputs takes none.

⚠ **Every SDK type below is written with the namespace its source path implies** (path under the SDK
root ⇒ namespace per the map's *Namespaces by content type* table). Namespaces split per kind; do not
infer a neighbour's namespace.

| Operation | Controller property · signature | Request record + members | Body model + fields (`Name (wire_name)`) | Response envelope + fields read | Error case + accessors | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `ListProductsForProductFamily` | `client.ProductFamilies` · `ListProductsForProductFamily(ListProductsForProductFamilyRequest request, RequestOptions?, CancellationToken)` | `Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs` — `ProductFamilyId: string, required` ("either the product family's id or its handle prefixed with `handle:`"); `Page: int = 1`; `PerPage: int = 20` (max 200); optional date/filter fields (unused) | — | `IReadOnlyList<ProductResponse>`; read `.Product.{Id, Handle, Name, Description, PriceInCents, Interval, IntervalUnit, ProductFamily.Id, ProductFamily.Handle, ArchivedAt, RequireCreditCard, Taxable}` — envelope wraps `Product (product): required` | Case A `ApiException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | Page-based; **cut short** when a page returns exactly `PerPage` records | map/operations/ProductFamilies.md; Models/Product.cs; Models/ProductResponse.cs; Models/ProductFamily.cs; Errors/ListProductsForProductFamilyError.cs |
| `ReadCustomerByReference` | `client.Customers` · `ReadCustomerByReference(ReadCustomerByReferenceRequest request, RequestOptions?, CancellationToken)` | `Requests/Customers/ReadCustomerByReferenceRequest.cs` — `Reference: string, required` | — | `CustomerResponse`; read `.Customer.{Id, Reference, Email, FirstName, LastName}` | Case B `ApiException<RawError>` (404 ⇒ customer does not exist yet — expected, not exceptional) | none | map/operations/Customers.md; Models/Customer.cs; Models/CustomerResponse.cs |
| `CreateCustomer` | `client.Customers` · `CreateCustomer(CreateCustomerOperationRequest request, RequestOptions?, CancellationToken)` | `Requests/Customers/CreateCustomerOperationRequest.cs` — `Body: CreateCustomerRequest?` | `CreateCustomerRequest` → `.Customer (customer): CreateCustomer, required` — `FirstName (first_name): string, required`, `LastName (last_name): string, required`, `Email (email): string, required`, `Reference (reference): string?, optional` | `CustomerResponse`; read `.Customer.Id`, `.Customer.Reference` | Case A `ApiException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | map/operations/Customers.md; Models/CreateCustomer.cs; Models/CustomerResponse.cs; Errors/CreateCustomerError.cs |
| `FindSubscription` | `client.Subscriptions` · `FindSubscription(FindSubscriptionRequest request, RequestOptions?, CancellationToken)` | `Requests/Subscriptions/FindSubscriptionRequest.cs` — `Reference: string?, optional` | — | `SubscriptionResponse`; read `.Subscription.*` (same shape as CreateSubscription response) | Case A `ApiException<FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404 ⇒ no subscription with that reference] · `TryGetRawError(out RawError)` [fallback] | none | map/operations/Subscriptions.md; Requests/Subscriptions/FindSubscriptionRequest.cs |
| `CreateSubscription` | `client.Subscriptions` · `CreateSubscription(CreateSubscriptionOperationRequest request, RequestOptions?, CancellationToken)` | `Requests/Subscriptions/CreateSubscriptionOperationRequest.cs` — `Body: CreateSubscriptionRequest?` | `CreateSubscriptionRequest` → `.Subscription (subscription): CreateSubscription, required` — `ProductHandle (product_handle): string?, optional`, `CustomerId (customer_id): int?, optional`, `CustomerReference (customer_reference): string?, optional`, `Reference (reference): string?, optional` (app-supplied subscription reference — the only reconciliation key; **no real idempotency-key member exists on this record**), `PaymentCollectionMethod (payment_collection_method): CollectionMethod?, optional` — sent as `Remittance` so the no-card-required plans sign up without card capture (`Automatic` gets 422 "No payment method was on file for the balance"); everything else left unset | `SubscriptionResponse`; read `.Subscription.{Id, Reference, State, ProductPriceInCents, CurrentPeriodEndsAt, ActivatedAt, Currency, Product.Handle, Product.Name, Product.PriceInCents, Product.Interval, Product.IntervalUnit, Customer.Id}` | Case A `ApiException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422 — payload `errors: string[]`] · `TryGetRawError(out RawError)` [fallback] | none | map/operations/Subscriptions.md; Models/CreateSubscription.cs; Models/Subscription.cs; Models/SubscriptionResponse.cs; Models/ErrorListResponse1.cs |
| `ReadSubscription` | `client.Subscriptions` · `ReadSubscription(ReadSubscriptionRequest request, RequestOptions?, CancellationToken)` | `Requests/Subscriptions/ReadSubscriptionRequest.cs` — `SubscriptionId: int, required`; `Include: IReadOnlyList<SubscriptionInclude>?, optional` (unused) | — | `SubscriptionResponse`; read `.Subscription.*` | Case B `ApiException<RawError>` (404 possible) | none | map/operations/Subscriptions.md |
| `ListCustomerSubscriptions` | `client.Customers` · `ListCustomerSubscriptions(ListCustomerSubscriptionsRequest request, RequestOptions?, CancellationToken)` | `Requests/Customers/ListCustomerSubscriptionsRequest.cs` — `CustomerId: int, required` | — | `IReadOnlyList<SubscriptionResponse>`; read `.Subscription.{Id, Reference, State, ProductPriceInCents, CurrentPeriodEndsAt, Product.Handle, Product.Name}` per item | Case B `ApiException<RawError>` | none | map/operations/Customers.md |

### SubscriptionState (Models/Enums/SubscriptionState.cs — `MaxioAdvancedBilling.Models.Enums`)

`OpenStringEnum<SubscriptionState>` — static members, not C# enums. Live: `Active`, `Trialing`.
Problem: `PastDue`, `SoftFailure`, `Unpaid`. End-of-life: `Canceled`, `Expired`, `OnHold`, `TrialEnded`,
`FailedToCreate`, `Suspended`, `Paused`, `AwaitingSignup`. Transient: `Pending`, `Assessing`.
Unknown wire values resolve through `Match`'s `otherwise` arm / `.Value` — never parse the string in app code.

### Client construction / auth / server facts

- Client: `MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)`. The only constructor. DI: `services.AddMaxioAdvancedBillingClient(...)` (registers a **singleton** client over `IHttpClientFactory` — HttpClient is long-lived; options object built **once at registration**).
- Auth: `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <API key>, Password = "x" }` (map: username is the Maxio/Chargify API key, password is `x`).
- Environments: `options.Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default) or `.Eu`. Bound from `Maxio:Environment` (`US` default).
- Server override: `options.Server.Production.Us.BaseUrl` (verbatim when `Maxio:BaseUrl` is set) else `options.Server.Production.Us.Site = <Maxio:Subdomain>` — Production group only; the `Ebb` group is out of scope (no Events usage).
- Retry/logging knobs on `MaxioAdvancedBillingClientOptions`: `Retry` (`RetryOptions` — see §3 rows 3–4), `Logging` (`LogRequestBody` stays **off**; `LoggerFactory` assigned explicitly).

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| a `productHandle` accepted by `CreateSubscription` must be one `ListProductsForProductFamily` returned for the configured family | `CreateSubscription` ← `ListProductsForProductFamily` | in the subscribe flow, before `CreateSubscription` is called: the plan list for `handle:{Maxio:ProductFamilyHandle}` is fetched and the requested handle must match (case-insensitive) a non-archived product's handle — `POST /api/subscriptions` returns 404/422 otherwise |
| a `customerId` passed to `CreateSubscription`/`ListCustomerSubscriptions` must be one Maxio returned for this app's customer | `CreateSubscription`, `ListCustomerSubscriptions` ← `ReadCustomerByReference`/`CreateCustomer` | the customer is resolved (or created) through `ReadCustomerByReference(reference = eShop user id)` before either write; the id used is always the response's `.Customer.Id` — never invented |
| a `subscription reference` passed to `FindSubscription` must be one this app created | `FindSubscription` ← `CreateSubscription` | references are only ever produced by this app's deterministic formatter (`eshopweb:{userId}:{productHandle}`), and `FindSubscription` is only called with references that formatter produced |

## 3. Trap notes (hazard + consequence only — MUST load the named skill)

- **Client/DI lifetime** — building the client per request or newing an `HttpClient` per call burns sockets and drops the handler pipeline: MUST load dotnet-client-initialization (before step 1).
- **Credential application & failure modes** — Basic auth credentials must be set before/at client construction; a credential you never set is *skipped, not an error* — a 401 can mean "nothing was sent": MUST load dotnet-authentication (before step 1).
- **Request-record construction & response-envelope unwrapping** — every input sits on the request record; responses wrap payloads one level down (`SubscriptionResponse.Subscription`): MUST load dotnet-calling-endpoints (before step 4).
- **OpenStringEnum / union reading** — `SubscriptionState` etc. are not C# enums; read via `Match`/`.Value`, never `ToString()` on a maybe-unknown value: MUST load dotnet-models (before step 2).
- **Catch-ladder shape** — Case A vs Case B differ per operation (see sheet); `ResponseDeserializationException` is not `ApiException<TError>`: MUST load dotnet-error-handling (before step 4).
- **Timeout is per attempt; retries multiply it; `POST` is never resent but `PUT` is** — the SDK's default `HttpMethodsToRetry` gates every retry trigger, so a hung call costs a multiple of `Timeout`, and write retries are not what they seem: MUST load dotnet-configuration-resilience (before steps 1 and 4).
- **Testing seam** — faking the SDK client wrapper is the wrong seam: MUST load dotnet-testing (before step 6).

## 4. REQUIRED READING (load before implementation starts)

- `dotnet-client-initialization` — governs step 1 (client construction, options object, HttpClient ownership, DI registration).
- `dotnet-authentication` — governs step 1 (Basic credentials, `BasicAuthCredentials`).
- `dotnet-calling-endpoints` — governs steps 2/4/5 (request records, envelopes, async).
- `dotnet-models` — governs steps 2/4/5 (OpenStringEnum, records, wire names).
- `dotnet-error-handling` — governs steps 2/4/5 (Case A/B, `ResponseDeserializationException`, catch ladder).
- `dotnet-configuration-resilience` — governs steps 1/4 (retries, timeout budget, base URL override, logging posture).
- `dotnet-testing` — governs step 6 (the test seam).

These are to be loaded **before implementation starts**; the sheet deliberately does not carry their contents.

**Mandatory hazard row (verbatim):** a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | The decision the plan records |
| --- | --- | --- |
| 1 | **Credential fail-fast** | Bound in `MaxioOptions` from the `Maxio:` section (keys: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl`, `Maxio:Environment`); an `IValidateOptions<MaxioOptions>` runs via `ValidateOnStart()` — the PublicApi host refuses to start when `ApiKey`, `Subdomain`, or `ProductFamilyHandle` is missing/blank (blank is treated as missing). `Maxio:BaseUrl` may be empty; `Maxio:Environment` must be `US` or `EU` when set (defaults `US`). Integration-test hosts supply placeholder values so the existing suite keeps running. |
| 2 | **Secret sourcing & rotation** | Secrets come from .NET user-secrets on `src/PublicApi`, populated by the operator from `MAXIO_API_KEY` / `MAXIO_SITE_SUBDOMAIN` / `MAXIO_ENVIRONMENT` / `MAXIO_DEFAULT_PRODUCT_FAMILY` (names only, never values, inside the repo). The SDK client is a **singleton built once at DI registration**, so a rotated `Maxio:ApiKey` takes effect on process restart — documented; no hot-reload path is offered. |
| 3 | **Total timeout budget** | `Retry.Timeout` (per attempt) is set to 10 s with `MaxRetries = 2` ⇒ worst-case read ≈ 30 s. Every endpoint handler takes the ASP.NET Core request-abort `CancellationToken`; Maxio service methods accept a caller `CancellationToken`, which is the only bound on the whole call. |
| 4 | **Write-retry ownership** | The scope's writes are `CreateCustomer` and `CreateSubscription` — both `POST`, which the SDK never resends by default (`HttpMethodsToRetry` default excludes `POST`), so no write is duplicated by the SDK's retry pipeline. `ReadSubscription` is only ever used for reconciliation, never `PUT`. |
| 5 | **Idempotency & ambiguous writes** | No Maxio operation in scope takes a real caller-supplied idempotency key (the generated `Idempotency-Key` header is injected and fresh per call — not a key; the `CreateSubscription` request record carries no key member). Idempotency is owned by this app: deterministic Maxio references (`customer reference = eShop user id`; `subscription reference = eshopweb:{userId}:{productHandle}`) + look-before-create (`ReadCustomerByReference`, `FindSubscription`) + a per-user in-process lock + the EF claim rows in DUPLICATE CLAIMS. |
| 6 | **Observability** | Endpoints log at Information (outcome: subscription id, reference, state) and Warning/Error on Maxio failures, always with the correlation reference (`eshopweb:…`); Maxio 422 messages are surfaced to the caller and logged. The SDK's own logger is wired to the host `ILoggerFactory`; `LogRequestBody` stays **off**. Maxio error bodies do not carry a request-id field in scope, so our reference is the correlation id in both directions. |
| 7 | **Sensitive data** | Request bodies in scope carry no card/bank/payment-profile data (`CreateSubscription` sends only handle/id/reference fields; no `payment_profile_attributes`). `LogRequestBody` stays off and `options.Logging.LoggerFactory` is set explicitly so the `MAXIOADVANCEDBILLINGCLIENT_LOG` environment variable cannot switch body logging on. Customer email/first/last names are logged only by exception messages we control (never a body dump). |
| 8 | **Environment selection** | `Maxio:Environment` ∈ {`US`, `EU`} selects `ServerEnvironment.Us`/`.Eu` (Production group); `Maxio:BaseUrl`, when set, overrides `options.Server.Production.Us.BaseUrl` verbatim. There is no separate sandbox server group in this SDK — sandbox vs production is a **different site/subdomain**, selected by config (`Maxio:Subdomain` / `Maxio:BaseUrl`). Development and testing run against the sandbox site from `MAXIO_SITE_SUBDOMAIN`; production deployments must point at a production site. |
| 9 | **Duplicate prevention under concurrency** | See DUPLICATE CLAIMS. |
| 10 | **Partial results** | `ListProductsForProductFamily` is the only paged read; the plan endpoint pages until a page returns fewer than `PerPage` (loop with a hard page cap of 25), so the caller cannot receive a silently truncated list. `ListCustomerSubscriptions` has no pagination (single response). |
| 11 | **Unknown outcomes** | See UNKNOWN OUTCOMES. |

## 6. Assumptions & Blockers

- **Assumption (minor):** eShopOnWeb JWT tokens carry only `ClaimTypes.Name` (username) — identity resolution does a `UserManager` lookup and uses the ASP.NET Identity user **id** (stable GUID string) as the Maxio customer `reference`. Names come from `ApplicationUser` if present; otherwise first/last fall back to the username split with a fixed marker.
- **Assumption (minor):** subscribing to a *different* plan while holding an active subscription is allowed (Maxio supports multiple subscriptions per customer); duplicate prevention is per `(user, plan)`, not per user.
- **Assumption (minor):** `POST /api/subscriptions` body is `{ "productHandle": "…" }`; no body ⇒ 400.
- **Assumption (minor):** all three endpoints require JWT auth (task states the capability is JWT-authenticated and identity comes from the token).
- **Environment note (not a blocker):** this machine runs EF with `UseOnlyInMemoryDatabase=true` — local link/claim rows do not survive a process restart; Maxio references remain the durable reconciliation key and the flows are restart-safe by design (look-before-create re-derives state from Maxio on cold start).
- No blockers.

## DUPLICATE CLAIMS

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| Ensure Maxio customer for user | `MaxioCustomerLink` row (`UserId` primary key), saved before `CreateCustomer` | PK conflict on a second concurrent claim for the same user; plus a per-user in-process lock serializing the whole ensure/subscribe flow | the EF save's `DbUpdateException`/PK-violation catch → re-read the winner's link and continue with it | `MaxioBillingService.EnsureCustomerAsync` — link claim write + `DbUpdateException` catch (src/PublicApi/Maxio/MaxioBillingService.cs); per-user `SemaphoreSlim` lock in `SubscribeAsync` |
| Create subscription for (user, plan) | `MaxioSubscriptionClaim` row (`RequestId` = deterministic `eshopweb:{userId}:{productHandle}`, primary key), saved before `CreateSubscription` | PK conflict on a second concurrent claim with the same reference; plus the per-user lock; plus `FindSubscription` look-before-create | the EF save's `DbUpdateException`/PK-violation catch → re-run `FindSubscription` and return the winner's subscription | `MaxioBillingService.SubscribeAsync` — claim write after the `FindSubscription` miss, `DbUpdateException` catch that re-reads by reference (src/PublicApi/Maxio/MaxioBillingService.cs) |

## PAGED READS

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| `ListProductsForProductFamily` | page size (`PerPage` ≤ 200) + a hard page cap (25 pages) in our loop | the plan list is fully consumed before it is returned (loop until a page yields fewer than `PerPage`; exceeding the page cap throws → 502, so a truncated list is never returned as success) | `MaxioBillingService.ListPlansCoreAsync` — the page-cap branch throws `MaxioBillingException(ProviderUnavailable)` (src/PublicApi/Maxio/MaxioBillingService.cs) |

## UNKNOWN OUTCOMES

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `CreateSubscription` | `FindSubscription` | the deterministic subscription reference (`eshopweb:{userId}:{productHandle}`) | the `catch` around `CreateSubscription`: on connection-level failure (`SdkConnectionException`/`SdkTimeoutException`) or ambiguous errors, re-read by reference; if found, the outcome is settled (return/record it); if not found, the claim row is marked `Unknown` and the endpoint returns 502 | `MaxioBillingService.SubscribeAsync` — `SdkException`/`ResponseDeserializationException` catch blocks + `ConfirmClaimAsync`/`MarkClaimUnknownAsync` (src/PublicApi/Maxio/MaxioBillingService.cs) |
| `CreateCustomer` | `ReadCustomerByReference` | the customer reference (= eShop user id) | the `catch` around `CreateCustomer`: re-read by reference; if found, treat as created | `MaxioBillingService.EnsureCustomerAsync` — `SdkException` catch that re-reads by reference (src/PublicApi/Maxio/MaxioBillingService.cs) |

**Test that fails the connection (row 1):** `MaxioBillingUnknownOutcomeTest` (tests/PublicApiIntegrationTests/SubscriptionEndpoints/) — a stub `HttpMessageHandler` throws `HttpRequestException("connection reset")` on the CreateSubscription POST (asserting exactly one attempt — the SDK never resends a POST) while the reference lookup first misses (404, look-before-create) and then answers: the settlement test asserts the landed subscription is returned with the claim row `Confirmed`, and the no-landing test asserts the claim row is left `Unknown` and the caller gets `FailureKind.UnknownOutcome`. Row 2's settle path is covered by the same flow shape in `EnsureCustomerAsync` (re-read by reference) — no separate live test, since the customer row's settlement mirrors the subscription row's.