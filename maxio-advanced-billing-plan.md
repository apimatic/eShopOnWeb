# Maxio Advanced Billing — subscription billing for eShopOnWeb (plan + contract sheet)

SDK root (plugin-relative): `maxio-advanced-billing/sdk/dotnet/` — all `src/...` and `map/...` paths below are relative to it.

## 1. Scope & sequence

| # | Step | Operations |
| --- | --- | --- |
| 1 | Reference the plugin SDK project from `src/Infrastructure`; settings type `MaxioSettings` bound from `Maxio:` (ApiKey, Subdomain, ProductFamilyHandle, BaseUrl + optional tuning), validated at startup; named `HttpClient` + singleton `MaxioAdvancedBillingClient` | — |
| 2 | Claim store: two new EF entities in `CatalogContext` (`BillingCustomer`, `SubscriptionEnrollment`) + SQL migration | — |
| 3 | `GET /api/subscription-plans` — list the plans of the configured product family (paged walk, capped, cached) | `ProductFamilies.ListProductsForProductFamily` |
| 4 | Ensure Maxio customer for the token's user (deterministic reference, claim → lookup → create → settle) | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer` |
| 5 | `POST /api/subscriptions` — validate plan ∈ step 3 set, claim enrollment, ensure customer, adopt an existing live subscription for that plan if the provider already holds one, else create; settle unknown outcomes by reference | `Customers.ListCustomerSubscriptions`, `Subscriptions.CreateSubscription`, `Subscriptions.FindSubscription` |
| 6 | `GET /api/my-subscriptions` — settle the caller's unknown/stale enrollments, then list the customer's subscriptions from Maxio (system of record) | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions`, `Subscriptions.FindSubscription` |
| 7 | Error boundary (one ladder) → `BillingException` → HTTP status in `ExceptionMiddleware`; tests (stub-handler unit tests + PublicApi integration tests); live sandbox verification | — |

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, verbatim — each operation that takes input takes ONE request record as its first parameter (an operation with no inputs takes none), built with an object initializer whose property names are the record's own, never flat arguments.
> ⚠ Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type, never from where a neighbouring type sits.

| Controller · signature | Request record (members) | Body model (fields) | Response envelope → fields read | Error case · accessors | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| `client.ProductFamilies` · `ListProductsForProductFamily(MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `ProductFamilyId: string, required` (id **or** `handle:<handle>`) · `Page: int = 1` · `PerPage: int = 20` (max 200) · `IncludeArchived: bool?` · others unused | — | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>` → `.Product` (`MaxioAdvancedBilling.Models.Product`, required): `Id int?`, `Name string?`, `Handle string?`, `Description string?`, `PriceInCents long?`, `Interval int?`, `IntervalUnit IntervalUnit?`, `ArchivedAt DateTimeOffset?`, `RequireCreditCard bool?`, `TrialInterval int?` | **A** `ApiException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>` · `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | none in SDK (single list); page/per_page driven by us | `map/operations/ProductFamilies.md`; `src/MaxioAdvancedBilling/Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs`; `src/MaxioAdvancedBilling/Models/Product.cs` |
| `client.Customers` · `ReadCustomerByReference(MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `Reference: string, required` | — | `MaxioAdvancedBilling.Models.CustomerResponse` → `.Customer` (required `MaxioAdvancedBilling.Models.Customer`): `Id int?`, `Reference string?` | **B** `ApiException<RawError>` | none | `map/operations/Customers.md`; remarks: "returns a customer by their unique reference ID" (`src/MaxioAdvancedBilling/Api/Customers.cs`) |
| `client.Customers` · `CreateCustomer(MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `Body: MaxioAdvancedBilling.Models.CreateCustomerRequest?` | `CreateCustomerRequest.Customer (customer): CreateCustomer, required` → `CreateCustomer`: `FirstName (first_name): string, required`, `LastName (last_name): string, required`, `Email (email): string, required`, `Reference (reference): string?` — remarks tie acceptance to: reference must be unique per site; country/state ISO formats (we send neither). Left out: address/phone/vat/entity-identifier/locale/parent/branding fields | `CustomerResponse` → `.Customer.Id` | **A** `ApiException<MaxioAdvancedBilling.Errors.CreateCustomerError>` · `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422] (`.Errors: MaxioAdvancedBilling.Models.AnyOf.Errors1?` → `TryGetCustomerError` / `TryGetListOfString`) · `TryGetRawError` [fallback] | none | `map/operations/Customers.md`; `src/MaxioAdvancedBilling/Models/CreateCustomer.cs`; `src/MaxioAdvancedBilling/Api/Customers.cs` remarks |
| `client.Customers` · `ListCustomerSubscriptions(MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `CustomerId: int, required` | — | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>` → `.Subscription` (**nullable** `MaxioAdvancedBilling.Models.Subscription`) | **B** `ApiException<RawError>` | none | `map/operations/Customers.md` |
| `client.Subscriptions` · `CreateSubscription(MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `Body: MaxioAdvancedBilling.Models.CreateSubscriptionRequest?` | `CreateSubscriptionRequest.Subscription (subscription): CreateSubscription, required` → `CreateSubscription` (nothing required): `ProductHandle (product_handle): string?` ("required unless product_id"), `CustomerId (customer_id): int?` ("required unless customer_reference or customer_attributes"), `Reference (reference): string?` (subscription's own app reference), `PaymentCollectionMethod (payment_collection_method): MaxioAdvancedBilling.Models.Enums.CollectionMethod?` (docs: Relationship Invoicing → `remittance`/`automatic`/`prepaid`; legacy Statements → `invoice`/`automatic`) — added after live sandbox rejected a create without it ("No payment method was on file for the $299.00 balance"). Left out: price point, coupons, payment profile/card/bank attributes (plans need no payment method), components, metafields, dates | `MaxioAdvancedBilling.Models.SubscriptionResponse` → `.Subscription?`: `Id int?`, `State SubscriptionState?`, `ProductPriceInCents long?`, `CurrentBillingAmountInCents long?`, `NextAssessmentAt DateTimeOffset?`, `CurrentPeriodEndsAt DateTimeOffset?`, `ActivatedAt`, `CreatedAt`, `Reference string?`, `Currency string?`, `Product? → Handle, Name, Interval, IntervalUnit, PriceInCents` | **A** `ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>` · `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422] (`Errors: IReadOnlyList<string>, required`) · `TryGetRawError` [fallback] | none | `map/operations/Subscriptions.md`; `src/MaxioAdvancedBilling/Models/CreateSubscription.cs`; `src/MaxioAdvancedBilling/Models/Subscription.cs` |
| `client.Subscriptions` · `FindSubscription(MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `Reference: string?` | — | `SubscriptionResponse` → `.Subscription?` | **A** `ApiException<MaxioAdvancedBilling.Errors.FindSubscriptionError>` · `TryGetNoContent(out RawError)` [404] · `TryGetRawError` [fallback] | none | `map/operations/Subscriptions.md` |

Enums needed:

| Enum (namespace `MaxioAdvancedBilling.Models.Enums`) | Values (C# → wire) | Source |
| --- | --- | --- |
| `SubscriptionState` | Pending `pending`, FailedToCreate `failed_to_create`, Trialing `trialing`, Assessing `assessing`, Active `active`, SoftFailure `soft_failure`, PastDue `past_due`, Suspended `suspended`, Canceled `canceled`, Expired `expired`, Paused `paused`, Unpaid `unpaid`, TrialEnded `trial_ended`, OnHold `on_hold`, AwaitingSignup `awaiting_signup` (open enum — read `.Value`, never `ToString()`) | `src/MaxioAdvancedBilling/Models/Enums/SubscriptionState.cs` |
| `IntervalUnit` | Day `day`, Month `month` | `src/MaxioAdvancedBilling/Models/Enums/IntervalUnit.cs` |
| `CollectionMethod` | Automatic `automatic`, Remittance `remittance`, Prepaid `prepaid`, Invoice `invoice` | `src/MaxioAdvancedBilling/Models/Enums/CollectionMethod.cs`; member docs in `src/MaxioAdvancedBilling/Models/CreateSubscription.cs` |

Client / auth / server facts:

| Fact | Value | Source |
| --- | --- | --- |
| Constructor | `new MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions)` | `sdk-map.md` Getting a client |
| Auth | `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <API key>, Password = "x" }` | `sdk-map.md` Servers & auth |
| Environment | `options.Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default) / `.Eu`; closed enum, wire `US`/`EU` | `src/MaxioAdvancedBilling/Servers/ServerEnvironment.cs` |
| Server group used | `Production` only (all 6 ops; none carries a Server-group bullet). `options.Server.Production.Us.Site` / `.Eu.Site` (template `{site}`, default `"subdomain"`); `options.Server.Production.{Us,Eu}.BaseUrl` (default `https://{site}.chargify.com` / `https://{site}.ebilling.maxio.com`) | `src/MaxioAdvancedBilling/Servers/ProductionOptions.cs` |
| Retry | `options.Retry` (`MaxioAdvancedBilling.Core.Configuration.RetryOptions`, all members required → `RetryOptions.Default() with {...}`) | `sdk-map.md` |
| Logging | `options.Logging` (`MaxioAdvancedBilling.Core.Configuration.LoggingOptions`) | `sdk-map.md` |
| Exceptions | `MaxioAdvancedBilling.Core.Exceptions`: `ApiException<T>`, `ApiException`, `ResponseDeserializationException`, `SdkConnectionException`, `SdkTimeoutException`, `AuthSchemeException`, `SdkException`; `RawError` in `MaxioAdvancedBilling.Core.ErrorResponse` | `sdk-map.md` Error-handling model |

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| `planHandle` accepted by POST /api/subscriptions (sent as `CreateSubscription.ProductHandle`) must be the handle of a non-archived product that the configured family returned | `CreateSubscription` ← `ListProductsForProductFamily` (`ProductFamilyId = "handle:" + Maxio:ProductFamilyHandle`) | `MaxioSubscriptionBillingService.SubscribeAsync`, via `GetOfferedPlanAsync`, before the enrollment claim and before `CreateSubscription` |
| `CreateSubscription.CustomerId` / `ListCustomerSubscriptions.CustomerId` must be the id Maxio returned for **this caller's** deterministic customer reference — never a client-supplied id | `CreateSubscription`, `ListCustomerSubscriptions` ← `ReadCustomerByReference` / `CreateCustomer` (stored in `BillingCustomer`) | `MaxioSubscriptionBillingService.EnsureCustomerAsync` / `FindCustomerIdAsync` — the only producers of the id passed on |
| A subscription adopted or reported for a plan must belong to the caller's customer and carry that plan's handle | `SubscribeAsync` adoption ← `ListCustomerSubscriptions` | `MaxioSubscriptionBillingService.FindLiveSubscriptionForPlanAsync` |

## 3. Trap notes

| Step | Hazard | Pointer |
| --- | --- | --- |
| 1 | `Add…Client` registers a singleton over the shared default `HttpClient` — DNS staleness and shared-handler side effects | MUST load `maxio-advanced-billing:dotnet-client-initialization` |
| 1 | An unset `BasicAuth` sends unauthenticated requests silently; a blank half of the pair is a misconfiguration | MUST load `maxio-advanced-billing:dotnet-authentication` |
| 1 | `Environment` vs `Server` are read at different times; an override on the wrong environment node is ignored | MUST load `maxio-advanced-billing:dotnet-configuration-resilience` |
| 1 | What `Retry.Timeout` and `HttpClient.Timeout` actually bound, and which layer bounds a whole call | MUST load `maxio-advanced-billing:dotnet-configuration-resilience` |
| 1 | Built-in logger defaults and the `MAXIOADVANCEDBILLINGCLIENT_LOG` switch; body logging redaction | MUST load `maxio-advanced-billing:dotnet-configuration-resilience` |
| 3 | A provider-driven page loop with no independent bound; silent truncation | MUST load `maxio-advanced-billing:dotnet-configuration-resilience` |
| 3,5 | Open-enum string rendering (`ToString()` vs wire value) when mapping state/interval | MUST load `maxio-advanced-billing:dotnet-models` |
| 3,5 | Nullable envelope members (`SubscriptionResponse.Subscription`) and unions in error bodies (`Errors1`) | MUST load `maxio-advanced-billing:dotnet-models` |
| 4,5 | Which verbs the SDK resends; what a transport failure on a write means | MUST load `maxio-advanced-billing:dotnet-configuration-resilience` |
| 4,5,6 | Case A vs Case B catch types; accessor order; exceptions that escape a typed-only ladder | MUST load `maxio-advanced-billing:dotnet-error-handling` |
| 4,5,6 | Leaking SDK messages/URLs to callers; collapsing distinct failures into one status | MUST load `maxio-advanced-billing:dotnet-error-handling` |
| all | Positional `CancellationToken` vs `RequestOptions?` parameter slot | MUST load `maxio-advanced-billing:dotnet-calling-endpoints` |
| 7 | Request bodies disposed before assertion; faking transport faults vs statuses | MUST load `maxio-advanced-billing:dotnet-testing` |

## 4. REQUIRED READING

| Skill | Governs |
| --- | --- |
| `maxio-advanced-billing:dotnet-client-initialization` | step 1 client + DI |
| `maxio-advanced-billing:dotnet-authentication` | step 1 credentials + fail-fast |
| `maxio-advanced-billing:dotnet-configuration-resilience` | steps 1, 3, 4, 5 (timeouts, retries, paging, logging, unknown outcomes) |
| `maxio-advanced-billing:dotnet-calling-endpoints` | steps 3–6 call shape |
| `maxio-advanced-billing:dotnet-models` | steps 3–6 enums, nullable envelopes, unions |
| `maxio-advanced-billing:dotnet-error-handling` | steps 3–7 error boundary |
| `maxio-advanced-billing:dotnet-testing` | step 7 |

Hazard row (verbatim): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `MaxioSettings` bound from `Maxio:` with `ValidateOnStart()`; validator requires non-blank `ApiKey`, `ProductFamilyHandle`, and `Subdomain` unless `BaseUrl` is set (then BaseUrl must be an absolute http(s) URI). Password half is the constant `x` from the map, so only the key can be blank. Failure message names the key, never the value. |
| 2 | Secret sourcing & rotation | Sources: user-secrets (dev), env vars `Maxio__*`, or the `MAXIO_*` aliases (lowest precedence fallback). Options built once when the singleton client is first resolved; rotation needs a process restart (accepted; documented). |
| 3 | Total timeout budget | Per attempt `Retry.Timeout = Maxio:AttemptTimeoutSeconds` (default 8s), `HttpClient.Timeout` = attempt + 2s backstop, `MaxRetries = 2` (GETs only). Whole-operation budget `Maxio:OperationBudgetSeconds` (default 45s) via one linked `CancellationTokenSource` in `MaxioSubscriptionBillingService.Bounded`, linked to `HttpContext.RequestAborted`. Settlement after an ambiguous write runs on its own fresh budget (`Maxio:SettlementBudgetSeconds`, default 15s) so a caller abort cannot skip it. |
| 4 | Write-retry ownership | Writes are `CreateCustomer` and `CreateSubscription` (both POST). `HttpMethodsToRetry` left at default (GET/HEAD/PUT/OPTIONS) → SDK never resends them; reads may retry. Test asserts a single POST on transport failure. |
| 5 | Idempotency & ambiguous writes | `CreateCustomer`: real key = body `Reference` (remarks: must be unique per site) = deterministic `eshop-` + SHA-256(buyer id) prefix; a 422 or ambiguous outcome reconciles via `ReadCustomerByReference`. `CreateSubscription`: no documented unique key; body `Reference` = per-claim `eshop-sub-<guid>` stored in the claim *before* the call; reconciles via `FindSubscription(Reference)`; plus provider-side adoption via `ListCustomerSubscriptions` (survives local store loss). The injected `Idempotency-Key` header is not relied upon. |
| 6 | Observability | Our service logs: Information on customer created/adopted and subscription created/adopted (Maxio ids + our references); Warning on provider rejections (status + Maxio `errors` strings) and on settlement; Error on transport/unknown outcomes and config errors (404 family). SDK built-in logger on, through the host `ILoggerFactory`. Provider error bodies (`ErrorListResponse1`, `CustomerErrorResponse1`) carry no correlation id → we log our own references (customer ref / subscription ref) and HTTP status; endpoint scope carries the request's `CorrelationId()`. |
| 7 | Sensitive data | `CreateCustomer` carries PII (name, email). `LogRequestBody = false`, `LogRequestHeaders/ResponseHeaders = false`, `LoggerFactory` assigned explicitly (disables the env-var switch). Customer reference is a hash (no email in URLs/logs). No card data in scope. |
| 8 | Environment selection | Server groups: `Production` (used) and `Ebb` (not touched). `Maxio:Environment` (`US` default / `EU`) selects the node; `Maxio:Subdomain` sets that node's `Site`; `Maxio:BaseUrl` (optional) replaces that node's `BaseUrl` verbatim. The SDK has no sandbox environment: sandbox vs live is the site (subdomain + key) each deployment supplies; dev/test uses the sandbox site via user-secrets; integration tests point `Maxio:BaseUrl` at a non-routable stub and never reach Maxio. |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS. Store = `CatalogContext` (the app's existing EF store); refusal = primary key. |
| 10 | Partial results | `ListProductsForProductFamily` walk capped at `MaxPlanPages` (5 × 200) → `PlanCatalog.IsTruncated` → response field `isTruncated`. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| `CreateSubscription` (per buyer + plan) | `SubscriptionEnrollments` table in `CatalogContext` (PK `BuyerId`+`PlanHandle`, status Pending, reference, lease time) | primary key violation on insert of the claim row | `catch (DbUpdateException)` / in-memory duplicate-key `InvalidOperationException`/`ArgumentException` in `SubscriptionClaimStore.TryClaimEnrollmentAsync` → returns existing row; service replays Active, 409 on live Pending, settles/takes over stale Pending/Unknown (concurrency token) | 1. `SubscriptionClaimStore.TryClaimEnrollmentAsync` (called from `MaxioSubscriptionBillingService.SubscribeCoreAsync`); 2. `MaxioSubscriptionBillingService.CreateSubscriptionAsync` (`_client.Subscriptions.CreateSubscription`) |
| `CreateCustomer` (per buyer) | `BillingCustomers` table in `CatalogContext` (PK `BuyerId`, `BillingCustomerId` null until created) | primary key violation on insert of the claim row | same catch in `SubscriptionClaimStore.TryClaimCustomerAsync` → service re-reads by reference, else 409 in-progress | 1. `SubscriptionClaimStore.TryClaimCustomerAsync` (called from `MaxioSubscriptionBillingService.EnsureCustomerAsync`); 2. `MaxioSubscriptionBillingService.CreateCustomerAsync` (`_client.Customers.CreateCustomer`) |

**PAGED READS**

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| `ListProductsForProductFamily` (`PerPage = 200`, page walk until a short page) | `MaxPlanPages` = 5 page cap + operation budget token | `SubscriptionPlanCatalog.IsTruncated` → `ListSubscriptionPlansResponse.IsTruncated` | `MaxioSubscriptionBillingService.FetchPlansAsync` (sets it); `SubscriptionPlanListEndpoint.HandleAsync` (copies it to the response) |

**UNKNOWN OUTCOMES**

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `CreateSubscription` | `Subscriptions.FindSubscription` | `CreateSubscription.Reference` = enrollment's `SubscriptionReference` (stored in claim before the call) | catch in `MaxioSubscriptionBillingService.CreateSubscriptionAsync` → `SettleCreateSubscriptionAsync` (own budget); still unknown → `EnrollmentStatus.Unknown`, settled by `SettleOpenEnrollmentsAsync` (every `ListSubscriptionsAsync`) and `ResolveExistingEnrollmentAsync` (next subscribe) | `SubscribeTests.ConnectionLostAfterMaxioCreatedTheSubscriptionIsSettledAsCreated`, `SubscribeTests.ConnectionLostBeforeMaxioActedReleasesTheClaim`, `SubscribeTests.AnUnsettledOutcomeIsRecordedAndSettledByTheNextRead` |
| `CreateCustomer` | `Customers.ReadCustomerByReference` | `CreateCustomer.Reference` = deterministic customer reference | catch in `MaxioSubscriptionBillingService.CreateCustomerAsync` → `SettleCreateCustomerAsync` (own budget); still unknown → unlinked `BillingCustomer` claim, re-read by reference in `EnsureCustomerAsync` on the next attempt (a duplicate create is refused by Maxio's unique reference → 422 → re-read) | `SubscribeTests.ConnectionLostAfterMaxioCreatedTheCustomerIsSettledByReference`, `SubscribeTests.ATakenCustomerReferenceIsResolvedToTheExistingCustomer` |

## 6. Assumptions & Blockers

- Assumption: buyer identity = JWT `ClaimTypes.Name` (user name), matching the repo's existing `BuyerId` convention (orders/baskets); email for the Maxio customer comes from the Identity user (falls back to the user name when it is an email). First/last name optional in the POST body; defaults derived from the email local part. `YOUR CALL — not in the map`.
- Assumption: one subscription per buyer per plan; a second POST for the same plan returns the existing subscription (200), not a second one. `YOUR CALL — not in the map`.
- Assumption: plans with `RequireCreditCard == true` are refused up front (this flow captures no payment method). `YOUR CALL — not in the map`.
- `ReadCustomerByReference` signals "no such customer" with HTTP 404 (Case B, no documented status) — observed live on the sandbox site (2026-10-15); code treats only 404 as absence; any other status/unreadable body is an error, never an absence.
- Live finding (2026-10-15): with no payment method and no trial, a default (`automatic`) signup is rejected with 422 "No payment method was on file". Decision: send `payment_collection_method` from `Maxio:PaymentCollectionMethod` (default `remittance`, validated against `CollectionMethod`). `UNVERIFIED` which invoicing architecture other sites use → the setting exists so `invoice` can be chosen for legacy Statements sites. `YOUR CALL — not in the map`.
- Error mapping: every expected 404 is handled at its call site (`ReadCustomerByReference`, `FindSubscription`, family 404 via `TryGetString`); any other provider 404 is treated as configuration/provider error (502), not the caller's fault.
- `UNVERIFIED`: subscription `reference` uniqueness is undocumented → code does not depend on it; uses a fresh GUID reference per claim and only *looks up* by it.
- `UNVERIFIED`: `ListCustomerSubscriptions` returns all of a customer's subscriptions (no paging params on the record) → treated as complete.
- Blockers: none.
