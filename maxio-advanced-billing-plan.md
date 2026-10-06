# Maxio Advanced Billing — integration plan (eShopOnWeb subscriptions)

SDK source (plugin-relative): `sdk/dotnet/` in the `maxio` plugin. Map: `sdk/dotnet/sdk-map.md` + `sdk/dotnet/map/operations/`.
All `source` cells below are relative to that SDK root.

## 1. Scope & sequence

| # | Step | Operations |
| --- | --- | --- |
| 1 | Settings (`Maxio:` section) + fail-fast validation + DI registration of the SDK client | — (client construction) |
| 2 | Plan catalog read: plans of the configured family, paged | `ProductFamilies.ListProductsForProductFamily` |
| 3 | Local claim store: `SubscriptionEnrollment` row keyed by user name (EF, `CatalogContext`) | — |
| 4 | Ensure Maxio customer for the eShop user (lookup by reference, else create) | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer` |
| 5 | Enroll: reconcile by subscription reference, else create | `Subscriptions.FindSubscription`, `Subscriptions.CreateSubscription` |
| 6 | My subscriptions: resolve customer by reference, list its subscriptions | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |
| 7 | PublicApi endpoints `GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions`; exception → HTTP mapping | — |
| 8 | Offline tests (fake `HttpMessageHandler` seam for the SDK; fakes for the service; EF in-memory for the claim) | — |

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, verbatim. Each operation that takes input takes ONE request record as its first parameter, built with an object initializer using the record's own property names — never flat arguments.
> ⚠ Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type (`Requests/Customers/*` → `MaxioAdvancedBilling.Requests.Customers`, `Models/*` → `MaxioAdvancedBilling.Models`, `Models/Enums/*` → `MaxioAdvancedBilling.Models.Enums`, `Models/AnyOf/*` → `MaxioAdvancedBilling.Models.AnyOf`, `Errors/*` → `MaxioAdvancedBilling.Errors`, `Core/Exceptions/*` → `MaxioAdvancedBilling.Core.Exceptions`, `Core/ErrorResponse/RawError` → `MaxioAdvancedBilling.Core.ErrorResponse`).

All operations: `(request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)`, throw-only, server group `Production`, auth `BasicAuth` OR `BearerAuth`.

| Controller | Method → returns | Request record (members) | Body model (fields) | Response fields read | Error case + accessors | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `client.ProductFamilies` | `ListProductsForProductFamily` → `IReadOnlyList<ProductResponse>` | `ListProductsForProductFamilyRequest`: `ProductFamilyId: string, required` ("id or handle prefixed with `handle:`"), `Page: int = 1` (min 1), `PerPage: int = 20` (max 200), `IncludeArchived: bool?` | — | `ProductResponse.Product` (`product`, required) → `Product`: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `ArchivedAt (archived_at): DateTimeOffset?` | A: `ApiException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` | **page-number** via `Page`/`PerPage` query params (request-record members; no `Pageable` return) | `map/operations/ProductFamilies.md`; `Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs`; `Models/ProductResponse.cs`; `Models/Product.cs`; `Errors/ListProductsForProductFamilyError.cs` |
| `client.Customers` | `ReadCustomerByReference` → `CustomerResponse` | `ReadCustomerByReferenceRequest`: `Reference: string, required` | — | `CustomerResponse.Customer` (`customer`, required) → `Customer`: `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?` | B: `ApiException<RawError>` (404 = not found — status read from `ApiException.StatusCode`) | none | `map/operations/Customers.md`; `Requests/Customers/ReadCustomerByReferenceRequest.cs`; `Models/CustomerResponse.cs`; `Models/Customer.cs` |
| `client.Customers` | `CreateCustomer` → `CustomerResponse` | `CreateCustomerOperationRequest`: `Body: CreateCustomerRequest?` | `CreateCustomerRequest`: `Customer (customer): CreateCustomer, required` → `CreateCustomer`: `FirstName (first_name): string, required`, `LastName (last_name): string, required`, `Email (email): string, required`, `Reference (reference): string?` (remarks: unique per site — "you may only create one customer for a given reference value"). Left out: address/phone/vat/locale/tax/parent/branding fields (not needed). | as above | A: `ApiException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] → `Errors (errors): Errors1?` (AnyOf: `TryGetCustomerError(out CustomerError)` → `Customer (customer): string?`; `TryGetListOfString(out IReadOnlyList<string>)`) · `TryGetRawError` | none | `map/operations/Customers.md`; `Api/Customers.cs` remarks; `Models/CreateCustomerRequest.cs`; `Models/CreateCustomer.cs`; `Errors/CreateCustomerError.cs`; `Models/CustomerErrorResponse1.cs`; `Models/AnyOf/Errors1.cs`; `Models/CustomerError.cs` |
| `client.Customers` | `ListCustomerSubscriptions` → `IReadOnlyList<SubscriptionResponse>` | `ListCustomerSubscriptionsRequest`: `CustomerId: int, required` | — | see Subscription fields below | B: `ApiException<RawError>` | none (single response) | `map/operations/Customers.md`; `Requests/Customers/ListCustomerSubscriptionsRequest.cs` |
| `client.Subscriptions` | `FindSubscription` → `SubscriptionResponse` | `FindSubscriptionRequest`: `Reference: string?` | — | see Subscription fields | A: `ApiException<FindSubscriptionError>` — `TryGetNoContent(out RawError)` [404 = not found] · `TryGetRawError` | none | `map/operations/Subscriptions.md`; `Requests/Subscriptions/FindSubscriptionRequest.cs`; `Errors/FindSubscriptionError.cs` |
| `client.Subscriptions` | `CreateSubscription` → `SubscriptionResponse` | `CreateSubscriptionOperationRequest`: `Body: CreateSubscriptionRequest?` | `CreateSubscriptionRequest`: `Subscription (subscription): CreateSubscription, required` → `CreateSubscription` (nothing required; remarks tie acceptance to): `ProductHandle (product_handle): string?` ("Required, unless a product_id is given"), `CustomerId (customer_id): int?` ("Required, unless a customer_reference or customer_attributes"), `Reference (reference): string?` (app's reference for the subscription), `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` (doc: Relationship Invoicing → `remittance`/`automatic`/`prepaid`; legacy Statements → `invoice`/`automatic`). Left out: `ProductId`, `ProductPricePointHandle/Id`, `CustomerReference`, `CustomerAttributes`, `PaymentProfileId/Attributes`, `Metafields`, `Currency`, components/coupons. | see Subscription fields | A: `ApiException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] → `Errors (errors): IReadOnlyList<string>, required` · `TryGetRawError` | none | `map/operations/Subscriptions.md`; `Api/Subscriptions.cs` remarks; `Models/CreateSubscriptionRequest.cs`; `Models/CreateSubscription.cs`; `Models/Enums/CollectionMethod.cs`; `Errors/CreateSubscriptionError.cs`; `Models/ErrorListResponse1.cs` |

**Subscription fields read** (`Models/SubscriptionResponse.cs`: `Subscription (subscription): Subscription?` — NOT required, so a null inner value must be handled; `Models/Subscription.cs`): `Id (id): int?`, `State (state): SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `CreatedAt (created_at): DateTimeOffset?`, `Reference (reference): string?`, `Currency (currency): string?`, `Customer (customer): Customer?`, `Product (product): Product?`.

**Enums** (open string enums; raw wire value via `.Value` — `Core/Enum/TypedEnum.cs`):

| Enum | Members used | Source |
| --- | --- | --- |
| `SubscriptionState` | `Pending`(pending) `FailedToCreate` `Trialing` `Assessing` `Active`(active) `SoftFailure` `PastDue` `Suspended` `Canceled` `Expired` `Paused` `Unpaid` `TrialEnded` `OnHold` `AwaitingSignup` — surfaced as wire string | `Models/Enums/SubscriptionState.cs` |
| `IntervalUnit` | `Day`(day) `Month`(month) — surfaced as wire string | `Models/Enums/IntervalUnit.cs` |
| `CollectionMethod` | `Automatic`(automatic) `Remittance`(remittance) `Prepaid`(prepaid) `Invoice`(invoice) — resolved from `Maxio:PaymentCollectionMethod` via `TryGetKnownValue` | `Models/Enums/CollectionMethod.cs` |

**Client construction / auth / server** (`sdk-map.md` *Getting a client*, *Servers & auth*; `Servers/ProductionOptions.cs`):
- Only ctor: `new MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)`.
- Auth: `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <API key>, Password = "x" }` (map: username = API key, password `x`). Bearer not used (US/EU direct).
- Environment: `options.Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default) — `Eu` exists; gateway not used.
- Base URL: `options.Server.Production.Us.Site = <subdomain>` (template `https://{site}.chargify.com`, default site `"subdomain"`); override `options.Server.Production.Us.BaseUrl` (and `.Eu.BaseUrl`) verbatim when `Maxio:BaseUrl` set.
- Exceptions: `ApiException<TError>` (`Core/Exceptions/ApiException.cs`, base `ApiException` has `StatusCode`, `Headers`, `ContentType`), `SdkTimeoutException`, `SdkConnectionException`, `ResponseDeserializationException` (`Core/Exceptions/`).

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| The plan handle a caller passes to subscribe must be a non-archived product the configured family returns | `CreateSubscription.ProductHandle` ← `ListProductsForProductFamily` (family `handle:{Maxio:ProductFamilyHandle}`) | `SubscriptionService.SubscribeAsync` checks the handle against `GetPlansAsync()` before any claim / Maxio write |
| The `CustomerId` passed to `CreateSubscription` / `ListCustomerSubscriptions` must be the customer this app resolved for the caller | `CreateSubscription.CustomerId`, `ListCustomerSubscriptions.CustomerId` ← `ReadCustomerByReference` / `CreateCustomer` (reference derived from the JWT user name) | `SubscriptionService.EnsureCustomerAsync` / `GetMySubscriptionsAsync`; never accepted from the caller |
| A subscription returned by `FindSubscription` is only adopted if it belongs to the caller's customer | `FindSubscription` ← `ReadCustomerByReference`/`CreateCustomer` | `SubscriptionService` reconcile step compares `Subscription.Customer.Id` with the resolved customer id |

## 3. Trap notes

| Step | Hazard → consequence | Skill |
| --- | --- | --- |
| 1 | `HttpClient` ownership/lifetime and whether the SDK client may be a singleton — wrong choice leaks sockets or pins stale DNS | MUST load `maxio:dotnet-client-initialization` |
| 1 | Where/when credentials are applied and what happens when one is missing — a blank key surfaces only as a 401 on first call | MUST load `maxio:dotnet-authentication` |
| 1, 4, 5 | What `RetryOptions.Timeout` bounds vs the whole call, and which verbs are retried — the 30 s caller budget can be blown silently, or a POST resent | MUST load `maxio:dotnet-configuration-resilience` |
| 1 | Logging defaults and the `MAXIOADVANCEDBILLINGCLIENT_LOG` switch — customer email/name can leak into logs | MUST load `maxio:dotnet-configuration-resilience` |
| 2, 4, 5 | Building request records / nested body models, open enums and AnyOf unions — wrong construction fails to compile or sends an empty body | MUST load `maxio:dotnet-calling-endpoints`, `maxio:dotnet-models` |
| 4, 5, 6 | Which exception types actually reach the catch (typed vs raw vs timeout vs deserialization) — an incomplete ladder lets a provider failure escape as a 500 | MUST load `maxio:dotnet-error-handling` |
| 8 | Which seam to fake for offline tests — faking the wrong layer tests nothing real | MUST load `maxio:dotnet-testing` |

## 4. REQUIRED READING (load before implementation starts; this sheet deliberately does not carry their contents)

Plugin-qualified (the `maxio` plugin's copies — other APIMatic plugins ship same-named skills):

- `maxio:dotnet-client-initialization` · step 1 (client + DI)
- `maxio:dotnet-authentication` · step 1 (credentials)
- `maxio:dotnet-configuration-resilience` · steps 1, 4, 5 (timeouts, retries, base URL, logging)
- `maxio:dotnet-calling-endpoints` · steps 2, 4, 5, 6
- `maxio:dotnet-models` · steps 2, 4, 5, 6 (bodies, enums, unions)
- `maxio:dotnet-error-handling` · steps 2, 4, 5, 6 (error boundary)
- `maxio:dotnet-testing` · step 8

Hazard row (verbatim): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `MaxioSettings` bound from `Maxio:`; `ValidateOnStart` rejects blank `ApiKey`, blank `Subdomain` (unless `BaseUrl` set), blank `ProductFamilyHandle`, non-absolute `BaseUrl`. Host refuses to start. |
| 2 | Secret sourcing & rotation | API key from .NET user-secrets (dev) / env var `Maxio__ApiKey` or a vault (prod). SDK options built once in the singleton factory → rotation requires a process restart (accepted; documented). |
| 3 | Total timeout budget | Caller never waits > 30 s: per-request `CancellationTokenSource` deadline in `SubscriptionService` (`Maxio:RequestBudgetSeconds`, default 25 s) for the main flow + a bounded settle read (`Maxio:SettleBudgetSeconds`, default 4 s) after an unknown write outcome; per-attempt SDK timeout `Maxio:AttemptTimeoutSeconds` (default 10 s). Budget exhaustion → HTTP 504 "Maxio did not respond…". |
| 4 | Write-retry ownership | Writes in scope are POSTs (`CreateCustomer`, `CreateSubscription`) — never resent by the SDK; app does not blindly resend either; reads (GET) may be retried by the SDK within the deadline. No PUT in scope. |
| 5 | Idempotency & ambiguous writes | No caller-supplied idempotency key exists on `CreateCustomerOperationRequest` / `CreateSubscriptionOperationRequest` (only `Body`); the generator's random `Idempotency-Key` header is not a key. Reconciliation: customer by deterministic `reference` (`ReadCustomerByReference`; Maxio enforces unique customer reference per remarks); subscription by deterministic `reference` (`FindSubscription`). |
| 6 | Observability | App logs (ILogger) at Information: plan list count, customer resolved/created (ids only), subscription created/adopted (ids, state); Warning: Maxio 4xx/422 (error strings), unknown outcomes; Error: auth failures/5xx/deserialization. SDK `LogRequestBody`/`LogResponseBody` off. Correlation: Maxio error bodies carry no documented correlation id in the map — HTTP status + operation name are logged; the SDK request line (Information) carries method + URL with query values masked (`reference=***`). The eShop user name (account id) is logged at Information on create/settle for traceability. |
| 7 | Sensitive data | `CreateCustomer` carries email + first/last name (PII). `LogRequestBody` stays off and `options.Logging.LoggerFactory` is assigned explicitly from DI so the env var cannot switch body logging on. No card data in scope. |
| 8 | Environment selection | Only server group `Production` is touched (no `Ebb`/`Oauth`). `Maxio:Environment` (US default, EU) selects the env; site = `Maxio:Subdomain`; `Maxio:BaseUrl` overrides verbatim. The SDK has no sandbox environment: test traffic is kept off live by the *site* — dev uses the sandbox site subdomain from user-secrets; automated tests use a fake `HttpMessageHandler` and a dummy key, never network. |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS. |
| 10 | Partial results | See PAGED READS. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

### DUPLICATE CLAIMS

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| `CreateSubscription` (POST /api/subscriptions double-click) | `SubscriptionEnrollments` table in `CatalogContext` (row per user, PK = `UserName`), inserted in state `Pending` before any Maxio write | Primary-key violation on insert of a second row with the same `UserName` (also enforced by EF in-memory); takeover of an `OutcomeUnknown`/stale row guarded by the `Version` concurrency token | `EfSubscriptionEnrollmentStore.TryClaimAsync` catches `DbUpdateException` (SQL Server) / `ArgumentException` (in-memory) and returns false when the key now exists; `EfSubscriptionEnrollmentStore.TrySaveAsync` catches `DbUpdateConcurrencyException` → `SubscriptionService.ResumeAsync` answers 409 (in progress) or the existing subscription | `EfSubscriptionEnrollmentStore.TryClaimAsync` (called from `SubscriptionService.SubscribeWithinBudgetAsync`; take-over via `EfSubscriptionEnrollmentStore.TrySaveAsync` in `SubscriptionService.ResumeAsync`) → `MaxioBillingGateway.CreateSubscriptionAsync` (called from `SubscriptionService.EnrollAsync`) |
| `CreateCustomer` (same request) | Same `SubscriptionEnrollments` row — customer creation only runs while the caller holds the user's enrollment claim; second line: Maxio's unique customer `reference` | Same PK claim; Maxio rejects a second customer with the same reference (422) | Same claim catch; Maxio 422 caught in `MaxioBillingGateway.CreateCustomerAsync` → re-read by reference | `EfSubscriptionEnrollmentStore.TryClaimAsync` (from `SubscriptionService.SubscribeWithinBudgetAsync`) → `MaxioBillingGateway.CreateCustomerAsync` (from `SubscriptionService.EnsureCustomerAsync`, inside `SubscriptionService.EnrollAsync`) |

### PAGED READS

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| `ListProductsForProductFamily` | `PerPage = 200`, at most `Maxio:MaxPlanPages` (default 5) pages | `BillingPlanCatalog.IsTruncated` → `ListSubscriptionPlansResponse.IsTruncated` | `MaxioBillingGateway.GetPlansAsync` (sets `BillingPlanCatalog.IsTruncated`) → `ListSubscriptionPlansEndpoint.HandleAsync` (sets `ListSubscriptionPlansResponse.IsTruncated`) |

### UNKNOWN OUTCOMES

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `CreateCustomer` | `ReadCustomerByReference` | customer `reference` = `{Maxio:ReferencePrefix}-{userName}` | `MaxioBillingGateway.CreateCustomerAsync` — `catch (Exception ex) when (IsUnknownWriteOutcome(ex))` re-reads via `TrySettleAsync(FindCustomerByReferenceAsync)` within `Maxio:SettleBudgetSeconds`; if still unsettled throws `BillingOutcomeUnknownException`, `SubscriptionService.EnrollAsync` releases the claim and the next request's `SubscriptionService.EnsureCustomerAsync` finds the customer by its (Maxio-unique) reference | `MaxioBillingGatewayTests.DroppedConnectionOnCreateCustomerIsSettledByReference`, `MaxioBillingGatewayTests.DroppedConnectionOnCreateCustomerThatCannotBeSettledIsAnUnknownOutcome`, `SubscribeTests.UnsettledCustomerCreateReleasesTheClaimAndTheRetryFindsTheCustomer` |
| `CreateSubscription` | `FindSubscription` | subscription `reference` = `{Maxio:ReferencePrefix}-sub-{userName}` | `MaxioBillingGateway.CreateSubscriptionAsync` turns no-answer / 5xx / unreadable-2xx into `BillingOutcomeUnknownException`; its `catch` in `SubscriptionService.EnrollAsync` calls `SubscriptionService.SettleUnknownSubscriptionAsync` (re-read by reference within `SettleBudget`); unsettled → `EnrollmentStatus.OutcomeUnknown` persisted, settled by `SubscriptionService.ResumeAsync` on the next request | `MaxioBillingGatewayTests.DroppedConnectionOnCreateIsAnUnknownOutcomeAndIsNeverResent`, `MaxioBillingGatewayTests.HungCreateTimesOutAsAnUnknownOutcome`, `SubscribeTests.UnansweredCreateIsSettledByReferenceInTheSameRequest`, `SubscribeTests.UnsettledCreateIsRecordedAsUnknownAndSettledByTheNextRequest`, `SubscriptionEndpointsTest.LostCreateResponseIsSettledByReferenceWithoutASecondSubscription` |

## 6. Assumptions & Blockers

- Assumption (YOUR CALL — not in the map): one Maxio subscription per eShop user through this API; a second subscribe for the same plan returns the existing one (200), for a different plan → 409. Plan changes/cancel are out of scope.
- Assumption (YOUR CALL — not in the map): eShop identity → Maxio customer reference `eshop-{userName}` (JWT carries only the user name; user name is stable across in-memory restarts, unlike the Identity GUID). Customer first/last name are not in eShop identity → derived from the email local part / "eShop" "Customer".
- UNVERIFIED: whether Maxio enforces uniqueness of the *subscription* `reference` — not stated in the map. Defensive directive: always `FindSubscription` by reference before `CreateSubscription`, and never rely on a 422 to detect a duplicate.
- ~~UNVERIFIED: `CreateSubscription` succeeds without payment data~~ → **settled by live traffic (2026-10-06, sandbox):** with the default (automatic) collection Maxio answered 422 "No payment method was on file for the $299.00 balance". Fix grounded in `Models/CreateSubscription.cs` / `Models/Enums/CollectionMethod.cs`: send `PaymentCollectionMethod` from `Maxio:PaymentCollectionMethod` (default `remittance`; `invoice` for legacy Statements sites). Verified live: 201, state `active`. The 422 path itself was verified to release the claim.
- No blockers.

## 7. Implementation notes (post-coding)

- SDK is referenced as a `ProjectReference` from `src/Infrastructure/Infrastructure.csproj` via the `MaxioSdkDir` MSBuild property (default: the plugin's `sdk/dotnet/` beside this repo). Machine-local: another machine/CI overrides `/p:MaxioSdkDir=...`. The SDK source is not copied into the repo.
- Duplicate-claim detection is provider-agnostic (a failed insert is a refused duplicate exactly when the key then exists). Referencing `SqlException` was removed: loading `Microsoft.Data.SqlClient` broke `MinimalApi.Endpoint` assembly scanning in later test hosts.
- New table ships as migration `AddSubscriptionEnrollments` (CreateTable only; pre-existing, unrelated model drift on `Orders`/`OrderItems`/`Catalog` was deliberately left out).
- Extra optional settings (all under `Maxio:`): `Environment` (US|EU), `RequestBudgetSeconds` (25), `SettleBudgetSeconds` (4), `AttemptTimeoutSeconds` (10), `MaxReadRetries` (2, GET only), `MaxPlanPages` (5), `PlanCacheSeconds` (60), `ReferencePrefix` (`eshop`), `PaymentCollectionMethod` (`remittance`).
