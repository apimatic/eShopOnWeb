# Maxio Advanced Billing — subscription billing for eShopOnWeb (plan + contract sheet)

SDK folder: the `maxio-advanced-billing` plugin's `sdk/dotnet` folder (referred to below as `<sdk>`; every
SDK path in this file is relative to it). Built against as a `<ProjectReference>` to
`src/MaxioAdvancedBilling/MaxioAdvancedBilling.csproj` (the SDK is not published; `docs/getting-started.md`, *Install*).

## 1. Scope & sequence

| # | Step | Operations |
|---|---|---|
| 0 | Repo prerequisites: `global.json` roll-forward (`latestMajor`) so the .NET 10 SDK builds the 8.0 solution; project reference to the SDK from `src/Infrastructure` | — |
| 1 | Configuration: bind `Maxio:` section (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl`, optional `Environment` = `US`/`EU`); map `MAXIO_*` env vars onto those keys as lowest-priority fallback; fail at startup when a required value is blank. Load values into user-secrets of `src/PublicApi` (never into a repo file) | — |
| 2 | Client: one long-lived `MaxioAdvancedBillingClient` singleton over a named `HttpClient` (130 s), `RequestLimit.WithoutUnsafeRetries`, explicit `LoggerFactory`, `LogRequestBody` off; `BaseUrl` override set on `Server.Production.Us/Eu.BaseUrl` verbatim | — |
| 3 | Site facts once per process (lazy, cached on success): test flag, Relationship Invoicing, currency (L2 step 1, R3). Writes refused in Development unless `Site.Test == true` (R2) | `Sites.ReadSite` |
| 4 | `GET /api/subscription-plans`: list the family's plans by `handle:<family>`, paged to an empty page (R8), cached 5 min | `ProductFamilies.ListProductsForProductFamily` |
| 5 | `POST /api/subscriptions` `{ planHandle }`: (a) plan ∈ step-4 set; (b) claim signup row in our DB (PK refusal) holding reference + one token per write; (c) customer: `ReadCustomerByReference` → 404 → `CreateCustomer` (own token; dup-reference 422 → read back); (d) found customer → held subscription check on this plan (L2 step 6); (e) `SafeSignup.SignUp` (FindSubscription by reference first, then create once with body-root `uniqueness_token`, explicit `payment_collection_method`); (f) settle Created/Existing/Recovered/Unknown, record on claim; (g) confirm plan/price/state/next billing date | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Subscriptions.ListSubscriptions`, `Subscriptions.FindSubscription`, `Subscriptions.CreateSubscription` |
| 6 | `GET /api/my-subscriptions`: customer by reference (404 → empty), list its subscriptions paged, plus our own unsettled (pending) signup claims | `Customers.ReadCustomerByReference`, `Subscriptions.ListSubscriptions` |
| 7 | Background reconciler: re-runs step 5 (same reference, same tokens) for claims left `Unknown`, once 120 s have passed since first send (R11) | as step 5 |
| 8 | Tests (fake `HttpMessageHandler`, no Maxio) + live sandbox verification | — |
| 9 | Project block: add `<sdk>/knowledge/AGENTS.block.md` verbatim to a new repo-root `AGENTS.md` (no `CLAUDE.md` exists) | — |

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, verbatim. Each operation that takes input takes ONE request record as its first
> parameter, built with an object initializer whose property names are the record's own — never flat arguments.
> ⚠ Every SDK type is written fully-qualified by the namespace its source path implies (Models → `MaxioAdvancedBilling.Models`,
> Enums → `.Models.Enums`, Errors → `.Errors`, request records → `.Requests.<Controller>`, `ApiException<T>` →
> `MaxioAdvancedBilling.Core.Exceptions`, `RawError` → `MaxioAdvancedBilling.Core.ErrorResponse`).

| Operation | Signature | Request record (members) | Body model (wire) | Response read | Error case | Paging | Provider notes | Source |
|---|---|---|---|---|---|---|---|---|
| `client.Sites.ReadSite` | `ReadSite(RequestOptions? requestOptions = null, CancellationToken ct = default)` | none | — | `SiteResponse.Site` (required) → `Test: bool?`, `RelationshipInvoicingEnabled: bool?`, `Currency: string?`, `Subdomain: string?`, `DefaultPaymentCollectionMethod: string?` | B `ApiException<RawError>` | none | `knowledge/operations/sites.md#readsite`: once per process; keep invoicing architecture, currency, `test` | `map/operations/Sites.md`; `src/MaxioAdvancedBilling/Models/SiteResponse.cs`, `Models/Site.cs` |
| `client.ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(ListProductsForProductFamilyRequest request, RequestOptions? = null, CancellationToken = default)` | `ProductFamilyId: string, required` (`handle:<family>` allowed per notes) · `Page: int = 1` · `PerPage: int = 20` · `IncludeArchived: bool?` | — | `IReadOnlyList<ProductResponse>`; `ProductResponse.Product` (required) → `Id: int?`, `Handle`, `Name`, `Description: string?`, `PriceInCents: long?`, `Interval: int?`, `IntervalUnit: IntervalUnit?`, `ArchivedAt`, `RequireCreditCard: bool?`, `ProductFamily` | A `ApiException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError` | page/per_page — page to empty page, per_page 200 (R8) | `knowledge/operations/product-families.md#listproductsforproductfamily`: id or `handle:<family>`; archived hidden by default; currency from site | `map/operations/ProductFamilies.md`; `Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs`, `Models/ProductResponse.cs`, `Models/Product.cs` |
| `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(ReadCustomerByReferenceRequest request, RequestOptions? = null, CancellationToken = default)` | `Reference: string, required` | — | `CustomerResponse.Customer` (required) → `Id: int?`, `Reference`, `Email` | B `ApiException<RawError>`; 404 = "not there" | none | `knowledge/operations/customers.md#readcustomerbyreference`: exact-match lookup; 404 → create | `map/operations/Customers.md`; `Requests/Customers/ReadCustomerByReferenceRequest.cs`, `Models/CustomerResponse.cs`, `Models/Customer.cs` |
| `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerOperationRequest request, RequestOptions? = null, CancellationToken = default)` | `Body: CreateCustomerRequest?` | `CreateCustomerRequest.Customer (customer): CreateCustomer, required`; `CreateCustomer.FirstName (first_name)`, `LastName (last_name)`, `Email (email)`: string, **required**; `Reference (reference): string?` — **set** (provider rule); `uniqueness_token` at body root via `CreateCustomerRequest.AdditionalProperties` — **set** (R12) | `CustomerResponse.Customer` | A `ApiException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError` | none | **Provider rules**: send user id as `reference`; `ReadCustomerByReference` first, create only on 404; duplicate-reference 422 → read back (L2). `knowledge/operations/customers.md#createcustomer` | `map/operations/Customers.md`; `Requests/Customers/CreateCustomerOperationRequest.cs`, `Models/CreateCustomerRequest.cs`, `Models/CreateCustomer.cs`, `Errors/CreateCustomerError.cs`, `Models/CustomerErrorResponse1.cs` |
| `client.Subscriptions.FindSubscription` | `FindSubscription(FindSubscriptionRequest request, RequestOptions? = null, CancellationToken = default)` | `Reference: string?` | — | `SubscriptionResponse.Subscription: Subscription?` | A `ApiException<FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError` | none | **Provider rules**: 404 = "not there", not proof a create failed; follow R11 after unknown outcome. `knowledge/operations/subscriptions.md#findsubscription` | `map/operations/Subscriptions.md`; `Requests/Subscriptions/FindSubscriptionRequest.cs`, `Errors/FindSubscriptionError.cs` |
| `client.Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionOperationRequest request, RequestOptions? = null, CancellationToken = default)` | `Body: CreateSubscriptionRequest?` | `CreateSubscriptionRequest.Subscription (subscription): CreateSubscription, required`; `CreateSubscription.ProductHandle (product_handle): string?` — set; `CustomerReference (customer_reference): string?` — set (D1, one customer source); `Reference (reference): string?` — set (provider rule); `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` — **set, R14**; `uniqueness_token` at body root via `CreateSubscriptionRequest.AdditionalProperties` — **set, R12**. Left out on purpose: `customer_id`/`customer_attributes` (one source only, D1), all payment fields (no card capture, D2), `next_billing_at`/`initial_billing_at`, coupons, components | `SubscriptionResponse.Subscription` → `Id: int?`, `State: SubscriptionState?`, `Product: Product?` (`Handle`, `Name`), `ProductPriceInCents: long?`, `CurrentPeriodEndsAt: DateTimeOffset?` (next billing date, L2 step 8), `NextAssessmentAt`, `Currency: string?`, `Reference`, `PaymentCollectionMethod`, `CreatedAt` | A `ApiException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422; `Errors: IReadOnlyList<string>` required; `action_link` in `AdditionalProperties`] · `TryGetRawError` | none | **Provider rules**: find customer + plan it holds (L2); one customer source (D1); `payment_collection_method` (R14), `reference`, body-root `uniqueness_token` kept on resends (R12); timeout/409 unknown (R11). `knowledge/operations/subscriptions.md#createsubscription` | `map/operations/Subscriptions.md`; `Requests/Subscriptions/CreateSubscriptionOperationRequest.cs`, `Models/CreateSubscriptionRequest.cs`, `Models/CreateSubscription.cs`, `Models/SubscriptionResponse.cs`, `Models/Subscription.cs`, `Errors/CreateSubscriptionError.cs`, `Models/ErrorListResponse1.cs` |
| `client.Subscriptions.ReadSubscription` (added during implementation: re-reads the standing of a signup we recorded as completed) | `ReadSubscription(ReadSubscriptionRequest request, RequestOptions? = null, CancellationToken = default)` | `SubscriptionId: int, required` · `Include` (not set) | — | `SubscriptionResponse.Subscription` | B `ApiException<RawError>`; 404 → null | none | `knowledge/operations/subscriptions.md#readsubscription`: access from `state` via L3; `product` may be null | `map/operations/Subscriptions.md`; `Requests/Subscriptions/ReadSubscriptionRequest.cs` |
| `client.Subscriptions.ListSubscriptions` | `ListSubscriptions(ListSubscriptionsRequest request, RequestOptions? = null, CancellationToken = default)` | `CustomerId: int?` · `Page: int = 1` · `PerPage: int = 20` · `Sort: SubscriptionSort = SignupDate` · `Direction: SortingDirection?` | — | `IReadOnlyList<SubscriptionResponse>` | B `ApiException<RawError>` | page/per_page — page to empty page (R8) | **Provider rules**: page until empty (R8); `state` filter cannot name `soft_failure` → no state filter, classify each row (R9, R16); filter product by id/name, never handle. `knowledge/operations/subscriptions.md#listsubscriptions` | `map/operations/Subscriptions.md`; `Requests/Subscriptions/ListSubscriptionsRequest.cs`, `Models/Enums/SubscriptionSort.cs`, `Models/Enums/SortingDirection.cs` |

Enum values needed:

| Enum (source) | Members used |
|---|---|
| `CollectionMethod` (`Models/Enums/CollectionMethod.cs`) | `Automatic` ("automatic"), `Remittance` ("remittance"), `Invoice` ("invoice"), `Prepaid` (not used) |
| `SubscriptionState` (`Models/Enums/SubscriptionState.cs`) | all 15, classified by `recipes/SubscriptionState.cs` (`Match` is exhaustive) |
| `IntervalUnit` (`Models/Enums/IntervalUnit.cs`) | `Day`, `Month` (read only, shown as wire value) |
| `ServerEnvironment` (`Servers/ServerEnvironment.cs`, closed enum, `TryGetKnownValue`) | `Us` ("US"), `Eu` ("EU") |

Client construction / auth / servers (`sdk-map.md` *Getting a client*, *Servers & auth*; `docs/getting-started.md` "Maxio: site and credentials"):
- `new MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)` — the only constructor.
- `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = apiKey, Password = "x" }`.
- `options.Environment = ServerEnvironment.Us|Eu`; `options.Server.Production.Us.Site`/`.Eu.Site` and `options.Server.Ebb.Us/Eu.Site` = subdomain (all four).
- `Maxio:BaseUrl` override: `options.Server.Production.Us.BaseUrl` and `.Eu.BaseUrl` = the value verbatim (`Servers/ProductionOptions.cs`; default templates `https://{site}.chargify.com`, `https://{site}.ebilling.maxio.com`).
- `options.Retry: RetryOptions` (`Core/Configuration/RetryOptions.cs`), `options.Logging: LoggingOptions`, `options.Hooks`.

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
|---|---|---|
| `planHandle` sent as `product_handle` must be one of the plans this app offers (configured family, not archived) | `CreateSubscription` ← `ListProductsForProductFamily` (`handle:<Maxio:ProductFamilyHandle>`) | `SubscriptionSignupService.SubscribeAsync`, before the claim and before any Maxio write (reads the cached plan list; unknown handle → 400) |
| `customer_reference` on the create must be a customer that exists with our user's reference | `CreateSubscription` ← `ReadCustomerByReference` / `CreateCustomer` | `SubscriptionSignupService` customer step, before `SafeSignup.SignUp` |
| `customer_id` filter on the list must be the id Maxio returned for our user's reference | `ListSubscriptions` ← `ReadCustomerByReference` | `MaxioBillingGateway.FindHeldSubscriptionAsync` / `ListCustomerSubscriptionsAsync` (take `Customer.Id` from the lookup answer) |

## 3. Trap notes

| Step | Hazard → consequence | Pointer |
|---|---|---|
| 1 | A missing or blank site is not an error: calls silently go to a site named `subdomain`; a region mismatch silently ignores the site | MUST read `docs/getting-started.md` "Maxio: site and credentials", `docs/authentication.md` |
| 2 | Where the `HttpClient` comes from and how long it lives decides socket exhaustion / DNS staleness | MUST read `docs/client-initialization.md` |
| 2 | What `Timeout` bounds, and whether the SDK resends a POST/PUT/429 — decides whether a write is sent twice or abandoned early | MUST read `docs/configuration-resilience.md` "Maxio: retries, timeouts and paging" |
| 2 | The SDK logger can print request bodies (customer name/email) depending on options and an env var | MUST read `docs/configuration-resilience.md` (logging) |
| 5 | `uniqueness_token` is on no model; where it lands in the JSON decides whether Maxio honours it; `action_link` is undeclared too | MUST read `docs/models.md` "Fields the spec does not declare" |
| 5 | Building request records / bodies wrong (flat args, wrong envelope) fails compile or sends the wrong JSON | MUST read `docs/calling-endpoints.md`, `docs/models.md` |
| 5,6 | Status and Maxio's message are reached differently for Case A and Case B; an unreadable body escapes a typed catch | MUST read `docs/error-handling.md` "Maxio: reading a rejection" |
| 4,6 | Silence on paging in the map is not completeness; a short page is not the end | MUST read `docs/configuration-resilience.md` "Maxio: retries, timeouts and paging" |
| 8 | How to fake the transport without touching Maxio | MUST read `docs/testing.md` |

## 4. REQUIRED READING (read before implementation starts; this sheet deliberately does not carry their contents)

- `docs/getting-started.md` — client, site, credentials (steps 1–2)
- `docs/client-initialization.md` — client & DI lifetime (step 2)
- `docs/authentication.md` — BasicAuth, failure messages (step 1)
- `docs/calling-endpoints.md` — request records, responses, cancellation (steps 3–7)
- `docs/models.md` — undeclared fields (`uniqueness_token`, `action_link`), enums (steps 4–6)
- `docs/error-handling.md` — error boundary, "Maxio: reading a rejection" (steps 3–7)
- `docs/configuration-resilience.md` — retries, timeouts, paging, logging (steps 2, 4, 6)
- `docs/testing.md` — fake transport (step 8)
- Hazard (verbatim): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
|---|---|---|
| 1 | Credential fail-fast | `MaxioOptions` bound from `Maxio:`; `MaxioOptions.Validate()` runs in `AddMaxioBilling` at service registration (i.e. before `builder.Build()`), and throws naming the key when `ApiKey` or `ProductFamilyHandle` is blank, when `Subdomain` is blank and no `BaseUrl` is set, when `BaseUrl` is set but not an absolute http(s) URI, or when `Environment` is set to anything but `US`/`EU`. The API key, the site and the region are all checked; no value is echoed. |
| 2 | Secret sourcing & rotation | Dev: .NET user-secrets of `src/PublicApi` (loaded from `MAXIO_*` env vars by `dotnet user-secrets set`); prod: `Maxio__ApiKey` env var / Key Vault (existing config providers). `MAXIO_*` env vars are also mapped onto `Maxio:*` as the lowest-priority source. Options are read once at registration and captured in the singleton client: a rotated key takes effect on process restart (accepted; documented). |
| 3 | Total timeout budget | Reads: 30 s deadline per call (`CancellationTokenSource` linked to `HttpContext.RequestAborted`). Writes (`CreateCustomer`, `CreateSubscription`): client `Retry.Timeout` and `HttpClient.Timeout` 130 s; the write runs under the host's `ApplicationStopping`-independent token (`CancellationToken.None` past the claim), never the request-aborted token. Whole POST worst case ≈ 4 reads×30 s + 2 writes×130 s + 30 s read-back. |
| 4 | Write-retry ownership | `options.Retry = RequestLimit.WithoutUnsafeRetries(RetryOptions.Default() with { Timeout = 130 s })`: only GET/HEAD/OPTIONS are resent by the SDK, 429 never. POST creates are never resent by the SDK; `SafeSignup`'s per-call hook also refuses a second send. Resends are ours only (same token, R11). |
| 5 | Idempotency & ambiguous writes | `CreateCustomer`: `uniqueness_token` = `SubscriptionSignup.CustomerUniquenessToken`; `CreateSubscription`: `SubscriptionSignup.SubscriptionUniquenessToken` — each a GUID minted once when the claim row is inserted, stored on the row, sent on every resend; replaced only after a corrected 422 or at R11 step 4 (409 + 120 s + not found). The SDK's injected `Idempotency-Key` header is not a key and is not relied on. Reconciliation: row 11. |
| 6 | Observability | Our `ILogger`: Information for signup outcomes (signup id, Maxio subscription id, state), Warning for unknown outcomes/429/rejections with status + Maxio's error strings (never bodies of our requests). SDK logging: `LoggerFactory` = app's `ILoggerFactory`, `LogRequestBody` off, `LogResponseBody` off. Correlation: our `SubscriptionSignup.Id` + reference in every log line and in the API response; Maxio's error list text is logged. |
| 7 | Sensitive data | `CreateCustomer` carries name + email (PII) → `LogRequestBody` stays off and `LoggerFactory` is assigned explicitly so `MAXIOADVANCEDBILLINGCLIENT_LOG` cannot switch body logging on. No card data in scope (no payment fields sent). |
| 8 | Environment selection | Server groups: `Production` (used) and `Ebb` (not used; site set anyway). Region from optional `Maxio:Environment` (`US` default, `EU`). Base URL = `Maxio:BaseUrl` verbatim when set, else template + `Maxio:Subdomain`. The SDK has no sandbox environment: test traffic is kept on the test site configured in user-secrets; `Site.Test` is checked (cached `ReadSite`) before any write when the host environment is Development, and logged at startup in every environment. |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS. |
| 10 | Partial results | Plan list and subscription list are walked by the adapted `ListAll` to an empty page with a ceiling; reaching the ceiling sets `IsComplete = false` on the API response instead of returning a silently short list. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. Settled in the write's own catch (SafeSignup read-back); still unknown → claim stays `Unknown` with `FirstSentAt`; API answers 202 with the signup id; the next POST by the user or the background reconciler resumes with the same reference + token after 120 s. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| `CreateCustomer` + `CreateSubscription` (one signup per user per plan) | `SubscriptionSignups` table in `CatalogContext` (in-memory or SQL), PK `Id` = `"{userId}:{planHandle}:{attempt}"` | Primary-key refusal on the insert of the claim row (a second click computes the same `attempt`) | `catch (Exception ex) when (IsDuplicateKey(ex))` — SQL Server `DbUpdateException` with `SqlException` 2627/2601, in-memory `ArgumentException` "same key" — → `SubscriptionBillingError.SignupInProgress` (HTTP 409) | Claim: `MaxioSubscriptionService.ClaimAsync` (`SubscriptionSignups.Add` + `SaveChangesAsync`); then SDK calls `MaxioGateway.CreateCustomerAsync` and `MaxioGateway.SignUpAsync` from `MaxioSubscriptionService.RunSignupAsync` |
| Resume of an existing unsettled claim (second worker/click) | Same row: `LeaseId` + `LeaseExpiresAt`, guarded by concurrency token `Version` | Lease still valid (`IsLeased`) or optimistic-concurrency refusal on the lease update (`DbUpdateConcurrencyException`) | `MaxioSubscriptionService.TryLeaseAsync` catch → `InProgress()` (HTTP 409) | Claim: `MaxioSubscriptionService.TryLeaseAsync` (`AcquireLease` + `SaveChangesAsync`); then SDK calls `MaxioGateway.CreateCustomerAsync` / `MaxioGateway.SignUpAsync` in `RunSignupAsync` |

**PAGED READS**

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| `ListProductsForProductFamily` (plans) | `MaxPages` ceiling (10 pages × 200) | `ListSubscriptionPlansResponse.IsComplete = false` | `ListAll.PagesOrPartial` (`PagedRows.IsComplete`) in `MaxioGateway.ListPlansAsync` → `SubscriptionPlanList.IsComplete` → `SubscriptionPlanListEndpoint.HandleAsync` |
| `ListSubscriptions` by `customer_id` (my subscriptions) | `MaxPages` ceiling (10 × 200) | `ListMySubscriptionsResponse.IsComplete = false` | `MaxioGateway.ListCustomerSubscriptionsAsync` → `CustomerSubscriptionList.IsComplete` → `MySubscriptionListEndpoint.HandleAsync` |
| `ListSubscriptions` by `customer_id` (held-plan check before create) | `MaxPages` ceiling | `SafeSignup.FindHeldSubscription` throws `InvalidOperationException` past `HeldMaxPages`; `Guarded` maps it to `ProviderUnavailable` (HTTP 503) and no create is sent | `MaxioGateway.FindHeldSubscriptionAsync` |

**UNKNOWN OUTCOMES**

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `CreateCustomer` | `ReadCustomerByReference` | customer `reference` = `eshop-{userId}` | `MaxioGateway.CreateCustomerAsync` catch `MaxioErrors.OutcomeUnknown` → `ReadBackCustomerAsync`; not found → `CustomerCreateResult.OutcomeUnknown` → claim `RecordCustomerCreateUnknown`, settled by `RunSignupAsync` on the next call or by `SubscriptionSignupReconciler` (same token; new token only after 409 + 120 s) | `MaxioSubscriptionServiceTests.DroppedConnectionOnCustomerCreateIsReadBackByReference`; live: a dropped connection through a local proxy answered 202 `pending`, never a failure |
| `CreateSubscription` | `FindSubscription` | subscription `reference` = claim reference (`eshop-{userId}-{plan}-{attempt}`) | `SafeSignup.SignUp` catch (read-back by reference → `Recovered`/`Unknown`), called from `MaxioGateway.SignUpAsync`; `Unknown` → claim `RecordSubscriptionCreateUnknown` → `RunSignupAsync` resume (read back; after 120 s resend with the same token, or a new token after 409) on the next POST or `SubscriptionSignupReconciler.ReconcileOnceAsync` | `DroppedConnectionOnCreateIsSettledByReadingBackTheReference`, `UnsettledCreateIsPendingAndIsResentWithTheSameTokenOnlyAfter120Seconds`, `ConflictWithNothingFoundAfter120SecondsStartsAgainWithANewToken`, `ReconcilerSettlesAnUnknownSignupWithoutTheShopperRetrying`; live: proxy dropped Maxio's 201 → app answered `created`, one POST, one subscription |

## 6. Assumptions & Blockers

- **Collection method (R14, D3) — decided, not asked (headless).** The app has no card capture (no Maxio.js), the plans owe money at signup ($29/$299), so `automatic` would be rejected (422 "No payment method was on file…"). Intent: the merchant bills the shopper by invoice → `remittance` on a Relationship Invoicing site, `invoice` on a legacy Statements site, chosen from `ReadSite.RelationshipInvoicingEnabled` (R3). Recorded as an application decision; card capture is out of scope.
- Customer identity: `reference = "eshop-" + ApplicationUser.Id` (the JWT carries only the user name; the id is resolved via `UserManager`). With `UseOnlyInMemoryDatabase=true` identity ids are regenerated on restart, so a restart yields a new Maxio customer for the same login (task-acknowledged caveat).
- Customer names: eShop users have no first/last name; first name = e-mail local part, last name = "eShop customer" (required by `CreateCustomer`).
- `Maxio:Environment` is an extra optional key (default `US`) because the SDK needs the region; the four mandated keys are bound exactly as named.
- `Maxio:BaseUrl`, when set, is used verbatim for both regions; `Subdomain` then only keys the request limiter.
- No blockers: every capability needed (site, plan list, customer lookup/create, subscription find/create/list) is in the map.

## GO-LIVE CHECKS

All live checks ran against the configured sandbox site (`Maxio:Subdomain`) after `ReadSite` returned `test: true` (logged on first use:
`Maxio site <subdomain>: test=True, relationshipInvoicing=True, currency=USD`).

| Check | How it was proved against a test site, or `not run` and why |
| --- | --- |
| Probe read-only | Run: `ReadSite` (test=true), `ListProductsForProductFamily` by `handle:eshop-subscribe` (2 plans, page 2 empty), `ReadCustomerByReference` for a new user → 404 |
| G1 collection method | Run: every created subscription read back via `GET /api/my-subscriptions` shows `payment_collection_method = remittance` (Relationship Invoicing site; no card captured by design). A unit test asserts it is on the wire |
| G2 customer by reference | Run: one user subscribed to `basic-plan`, then `eshop-pro` → one Maxio customer (the second signup found it by reference; no second customer create in the log) |
| G3 token / concurrency / unknown outcome | Run: 6 and 8 parallel POSTs for one user and plan → one 201, the rest 409, exactly one subscription in Maxio and one create POST in the SDK log. Dropped-response test through a local proxy (`Maxio:BaseUrl`): Maxio answered 201, the response was dropped, the app read back and answered `created`; a repeat answered `already_subscribed`; one POST, one subscription. Unit tests assert the body-root `uniqueness_token`, the same token on resend, and a new token only after 409 + 120 s |
| G4 paging | Run: the plan list fetched page 1 and an empty page 2 with `per_page=200` (log). A unit test covers the ceiling → `IsComplete=false` |
| G5 money | Run: Pro shows `price 299`, `priceInCents 29900`; Basic `29` / `2900`, matching the catalog |
| G6 access / already subscribed | Run: a repeat subscribe answered `already_subscribed` with state `active`. States are classified by the copied `SubscriptionState` recipe (exhaustive `Match`); a unit test covers an `on_hold` subscription blocking a second signup. Not run live for non-active states (producing them needs extra Maxio writes outside this scope) |
| G7 request limit / 429 | Unit test: a stubbed 429 is not retried and pauses later calls; every call goes through `RequestLimit.Run`; PUT and 429 are out of the SDK retry policy. 429 was not provoked live |
| G8 4xx handling | Unit tests: stubbed 422 → `Rejected` without Maxio's text in the message; 422 with `action_link` → `action_required`; 409 → unknown, settled by read-back. Live: 401 without a token, 400 for an unknown plan |
| G9 / G10 / G11 / G14 | N/A: no plan change, cancellation, webhook receiver or payment profile in scope |
| G12 card data | N/A by design: no card data is collected; the API key stays server-side (PublicApi only) |
| G13 configuration | Run: the diff was searched for the API key value and the subdomain — neither is in tracked files; values live in user-secrets. Startup refuses a blank `Maxio:ApiKey`/`ProductFamilyHandle`/`Subdomain` (unit tests) |
| G15 nulls | Run: plan responses with `description: null` deserialize; unknown fields are kept in `AdditionalProperties` |
