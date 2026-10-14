# Maxio Advanced Billing — subscription billing plan (eShopOnWeb PublicApi)

SDK root (plugin-relative): `sdk/dotnet/` · map: `sdk/dotnet/sdk-map.md` · SDK project: `sdk/dotnet/src/MaxioAdvancedBilling/MaxioAdvancedBilling.csproj` (assembly `Maxio.AdvancedBilling.Sdk`, netstandard2.0, local `ProjectReference` — not on NuGet).

## 1. Scope & sequence

| # | Step | Operations |
| --- | --- | --- |
| 1 | Settings bound from `Maxio:` (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl`, optional `Environment`), validated on start; client registered once over a named `HttpClient` | — (client construction) |
| 2 | `GET /api/subscription-plans` — list the plans of the configured product family (walk pages, capped, cached briefly) | `ProductFamilies.ListProductsForProductFamily` |
| 3 | `POST /api/subscriptions` — validate plan ∈ step-2 list → claim (local DB) → ensure Maxio customer (lookup by reference, else create) → create subscription → record result | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Subscriptions.CreateSubscription` |
| 3a | Unknown-outcome settlement for the two writes (in the write's own `catch`, plus a sweeper for anything still unknown) | `Customers.ReadCustomerByReference`, `Subscriptions.FindSubscription` |
| 4 | `GET /api/my-subscriptions` — resolve the caller's Maxio customer, list its subscriptions live from Maxio (system of record) | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, verbatim. Each operation that takes input takes ONE request record as its first parameter (an operation with no inputs takes none), built with an object initializer whose property names are the record's own — never flat arguments. Pass `cancellationToken:` by name (a `RequestOptions?` sits between).
> ⚠ Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type — never from where a neighbouring type sits (`Requests/Customers/*` → `MaxioAdvancedBilling.Requests.Customers`, `Models/*` → `MaxioAdvancedBilling.Models`, `Models/AnyOf/*` → `MaxioAdvancedBilling.Models.AnyOf`, `Models/Enums/*` → `MaxioAdvancedBilling.Models.Enums`, `Errors/*` → `MaxioAdvancedBilling.Errors`, `RawError` → `MaxioAdvancedBilling.Core.ErrorResponse`, `ApiException<T>`/`SdkException` family → `MaxioAdvancedBilling.Core.Exceptions`).

| Controller · method | Request record + members | Body model + fields (wire) | Response envelope → fields read | Error case + accessors | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| `client.ProductFamilies` · `ListProductsForProductFamily(ListProductsForProductFamilyRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` → `Task<IReadOnlyList<ProductResponse>>` | `MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest`: `ProductFamilyId: string, required` ("id or handle prefixed with `handle:`") · `Page: int = 1` ([Minimum(1)]) · `PerPage: int = 20` ([Maximum(200)], >200 clamped to 200) · `IncludeArchived: bool?` · others unused (`DateField`, `Filter`, `StartDate`, `EndDate`, `StartDatetime`, `EndDatetime`, `Include`) | — (GET) | each item `ProductResponse.Product` (`product`, **required**) → `Product`: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `TrialPriceInCents`/`TrialInterval`, `InitialChargeInCents (initial_charge_in_cents): long?`, `RequireCreditCard (require_credit_card): bool?`, `ArchivedAt (archived_at): DateTimeOffset?`, `ProductFamily (product_family): ProductFamily?` → `Handle (handle)` | **A** `ApiException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | none in SDK (default) — drive `Page`/`PerPage` manually; stop when a page returns `< PerPage` items | `map/operations/ProductFamilies.md`; `src/MaxioAdvancedBilling/Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs`; `Models/ProductResponse.cs`; `Models/Product.cs` |
| `client.Customers` · `ReadCustomerByReference(ReadCustomerByReferenceRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` → `Task<CustomerResponse>` | `MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest`: `Reference: string, required` (query `reference`) | — (GET `/customers/lookup.json`) | `CustomerResponse.Customer` (`customer`, **required**) → `Id (id): int?`, `Reference (reference): string?` | **B** `ApiException<RawError>` (404 = no such customer — status read off `ex.StatusCode`) | none | `map/operations/Customers.md`; `Api/Customers.cs` remarks: "Returns a customer by their unique reference ID. It will return a single match." |
| `client.Customers` · `CreateCustomer(CreateCustomerOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` → `Task<CustomerResponse>` | `MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest`: `Body: CreateCustomerRequest?` (nothing required) | `MaxioAdvancedBilling.Models.CreateCustomerRequest`: `Customer (customer): CreateCustomer, required` → `CreateCustomer`: `FirstName (first_name): string, required` · `LastName (last_name): string, required` · `Email (email): string, required` · `Reference (reference): string?` — sent (our idempotent lookup key). Left out: cc_emails, organization, address*, city, state, zip, country, phone, locale, vat_*, entity_identifier_*, tax_exempt*, surcharging, parent_id, salesforce_id, branding_theme_id | `CustomerResponse.Customer` → `Id (id): int?` | **A** `ApiException<MaxioAdvancedBilling.Errors.CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. `CustomerErrorResponse1.Errors (errors): Errors1?` (AnyOf, `Models/AnyOf/Errors1.cs`): `TryGetCustomerError(out CustomerError)` (`CustomerError.Customer (customer): string?`) · `TryGetListOfString(out IReadOnlyList<string>)` | none | `map/operations/Customers.md`; `Models/CreateCustomerRequest.cs`; `Models/CreateCustomer.cs`; `Models/CustomerErrorResponse1.cs`; `Models/AnyOf/Errors1.cs`; `Models/CustomerError.cs` |
| `client.Customers` · `ListCustomerSubscriptions(ListCustomerSubscriptionsRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` → `Task<IReadOnlyList<SubscriptionResponse>>` | `MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest`: `CustomerId: int, required` | — (GET) | each `SubscriptionResponse.Subscription` (`subscription`, **optional** `Subscription?`) → see Subscription fields below | **B** `ApiException<RawError>` | none (record has no page members) | `map/operations/Customers.md`; `Requests/Customers/ListCustomerSubscriptionsRequest.cs`; `Models/SubscriptionResponse.cs` |
| `client.Subscriptions` · `CreateSubscription(CreateSubscriptionOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` → `Task<SubscriptionResponse>` | `MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest`: `Body: CreateSubscriptionRequest?` | `MaxioAdvancedBilling.Models.CreateSubscriptionRequest`: `Subscription (subscription): CreateSubscription, required` → `CreateSubscription` (nothing `required`; prose ties acceptance to): `ProductHandle (product_handle): string?` — "Required, unless a `product_id` is given" · `CustomerId (customer_id): int?` — "Required, unless a `customer_reference` or a set of `customer_attributes` is given" · `Reference (reference): string?` — "reference value (provided by your app) for the subscription itself" (sent; our reconciliation key) · `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` — sent as `CollectionMethod.Remittance` (live sandbox rejected the default automatic collection with 422 "No payment method was on file"; the storefront captures no payment method). Left out: product_id, product_price_point_*, custom_price, coupon_*, payment profile / card / bank attributes (plans don't require a payment method), components, calendar_billing, metafields, customer_reference, customer_attributes, group, ref, next_billing_at, initial_billing_at, defer_signup (default false), expires_at, prepaid_configuration, etc. | `SubscriptionResponse.Subscription` (`Subscription?`) | **A** `ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] (`Errors (errors): IReadOnlyList<string>, required`) · `TryGetRawError(out RawError)` [fallback]. Remarks: 3DS → 422 with action_link (not applicable: no card) | none | `map/operations/Subscriptions.md`; `Api/Subscriptions.cs` remarks; `Models/CreateSubscriptionRequest.cs`; `Models/CreateSubscription.cs`; `Models/Enums/CollectionMethod.cs`; `Models/ErrorListResponse1.cs` |
| `client.Subscriptions` · `FindSubscription(FindSubscriptionRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` → `Task<SubscriptionResponse>` | `MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest`: `Reference: string?` (query `reference`) | — (GET `/subscriptions/lookup.json`) | `SubscriptionResponse.Subscription` | **A** `ApiException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback] | none | `map/operations/Subscriptions.md`; remarks "Finds a subscription by its reference." |
| `client.Subscriptions` · `ReadSubscription(ReadSubscriptionRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` → `Task<SubscriptionResponse>` | `MaxioAdvancedBilling.Requests.Subscriptions.ReadSubscriptionRequest`: `SubscriptionId: int, required` · `Include: IReadOnlyList<SubscriptionInclude>?` (unused) | — (GET) | `SubscriptionResponse.Subscription` | **B** `ApiException<RawError>` (404 = gone) | none | `map/operations/Subscriptions.md`; `Requests/Subscriptions/ReadSubscriptionRequest.cs` |

**Subscription fields read** (`Models/Subscription.cs`, all optional): `Id (id): int?` · `State (state): SubscriptionState?` · `ProductPriceInCents (product_price_in_cents): long?` · `CurrentBillingAmountInCents (current_billing_amount_in_cents): long?` · `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?` ("when the next regularly scheduled attempted charge will occur") · `NextAssessmentAt (next_assessment_at): DateTimeOffset?` (payment capture attempt; diverges on retry) · `ActivatedAt`, `CreatedAt` · `Currency (currency): string?` · `Reference (reference): string?` · `Customer (customer): Customer?` → `Id` · `Product (product): Product?` → `Handle`, `Name`, `Interval`, `IntervalUnit`, `ProductFamily.Handle`.

**Enums needed**

| Enum (namespace `MaxioAdvancedBilling.Models.Enums`) | Values (C# member → wire) | Source |
| --- | --- | --- |
| `SubscriptionState` | Pending→`pending`, FailedToCreate→`failed_to_create`, Trialing→`trialing`, Assessing→`assessing`, Active→`active`, SoftFailure→`soft_failure`, PastDue→`past_due`, Suspended→`suspended`, Canceled→`canceled`, Expired→`expired`, Paused→`paused`, Unpaid→`unpaid`, TrialEnded→`trial_ended`, OnHold→`on_hold`, AwaitingSignup→`awaiting_signup` | `Models/Enums/SubscriptionState.cs` |
| `IntervalUnit` | Day→`day`, Month→`month` | `Models/Enums/IntervalUnit.cs` |
| `CollectionMethod` | Automatic→`automatic`, Remittance→`remittance`, Prepaid→`prepaid`, Invoice→`invoice` (legacy Statements: invoice/automatic; Relationship Invoicing: remittance/automatic/prepaid) | `Models/Enums/CollectionMethod.cs` |

All are `OpenStringEnum<T>`: read the wire value with `.Value`, never `ToString()`/interpolation.

**Client construction / auth / servers**

| Fact | Value | Source |
| --- | --- | --- |
| Constructor | `new MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` (only ctor) | `sdk-map.md` *Getting a client* |
| Auth | `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <API key>, Password = "x" }` (both `required`) — "username is a Maxio Chargify API key; password is `x`" | `sdk-map.md` *Servers & auth* |
| Environment | `options.Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default, wire `US`) / `.Eu` (`EU`) | `Servers/ServerEnvironment.cs` |
| Server group in scope | `Production` only (`Us`: `https://{site}.chargify.com`, `Eu`: `https://{site}.ebilling.maxio.com`); `{site}` default `"subdomain"` → set `options.Server.Production.Us.Site` / `.Eu.Site`; literal override `options.Server.Production.Us.BaseUrl` / `.Eu.BaseUrl`. `Ebb` group not touched. | `sdk-map.md`; `Servers/ProductionOptions.cs` |
| Retry | `options.Retry = MaxioAdvancedBilling.Core.Configuration.RetryOptions.Default() with { … }` (all members `required`) | `sdk-map.md` |
| Logging | `options.Logging` (`LoggingOptions`, `Core.Configuration`) — `LoggerFactory`, `LogRequestBody` | dotnet-configuration-resilience |

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| The `planHandle` a caller POSTs must be the `Handle` of a non-archived product that the configured family's list returned | `CreateSubscription` ← `ListProductsForProductFamily` (handle `handle:{Maxio:ProductFamilyHandle}`) | `MaxioSubscriptionBillingService.SubscribeCoreAsync` → `MaxioPlanCatalog.FindPlanAsync` (reads `GetPlansAsync`), before the claim and before `CreateSubscription` |
| The `customer_id` sent to `CreateSubscription` must be the `Customer.Id` that `ReadCustomerByReference`/`CreateCustomer` returned for this user's own reference | `CreateSubscription` ← `ReadCustomerByReference` / `CreateCustomer` (stored in `BillingCustomer.MaxioCustomerId`) | `MaxioSubscriptionBillingService.EnsureCustomerAsync` (→ `TryFindCustomerAsync` / `CreateCustomerAsync`), never from caller input |
| The `CustomerId` given to `ListCustomerSubscriptions` must be the id stored/resolved for the caller (identity from JWT only) | `ListCustomerSubscriptions` ← `ReadCustomerByReference` / `CreateCustomer` | `MaxioSubscriptionBillingService.GetMySubscriptionsAsync` |

## 3. Trap notes

| Step | Hazard | Pointer |
| --- | --- | --- |
| 1 | `AddMaxioAdvancedBillingClient` lifetime / which `HttpClient` it resolves / stale DNS behind a singleton; options are captured once | MUST load `maxio-advanced-billing:dotnet-client-initialization` |
| 1 | An unset credential sends unauthenticated requests silently; a blank part of a two-part credential | MUST load `maxio-advanced-billing:dotnet-authentication` |
| 1 | What each "Timeout" knob bounds vs. what bounds a whole call; which verbs the SDK resends; `Environment` vs `Server` read-time difference | MUST load `maxio-advanced-billing:dotnet-configuration-resilience` |
| 1 | Logging defaults, env-var-armed body logging, redaction direction for JSON bodies carrying PII | MUST load `maxio-advanced-billing:dotnet-configuration-resilience` |
| 2 | List endpoint returns a plain list (not `Pageable`) — loop termination and truncation signalling are on us | MUST load `maxio-advanced-billing:dotnet-configuration-resilience` |
| 2–4 | Building records/bodies: `required` vs optional, request record vs body model, positional `requestOptions` | MUST load `maxio-advanced-billing:dotnet-calling-endpoints` |
| 2–4 | Open-string enums (`SubscriptionState`, `IntervalUnit`) printing the wrong text; AnyOf `Errors1` reading; nullable envelopes | MUST load `maxio-advanced-billing:dotnet-models` |
| 3 | Case A vs Case B catch ladders per operation; accessor order; deserialization failures that escape typed catches; mapping provider status to caller status without leaking | MUST load `maxio-advanced-billing:dotnet-error-handling` |
| 3a | A transport failure / timeout / own-budget cancellation on a POST leaves the outcome unknown | MUST load `maxio-advanced-billing:dotnet-configuration-resilience` |
| 3 | Double-submit / concurrent subscribe: claim must be atomic in a store that outlives the request | MUST load `maxio-advanced-billing:dotnet-configuration-resilience` |
| tests | Stubbing the SDK's HttpClient, reading request bodies, constructing exceptions, counting sends | MUST load `maxio-advanced-billing:dotnet-testing` |

## 4. REQUIRED READING (load all before implementation)

- `maxio-advanced-billing:dotnet-client-initialization` — step 1 (client + DI)
- `maxio-advanced-billing:dotnet-authentication` — step 1 (Basic credentials, fail-fast)
- `maxio-advanced-billing:dotnet-calling-endpoints` — steps 2–4
- `maxio-advanced-billing:dotnet-models` — steps 2–4 (enums, AnyOf, nullability)
- `maxio-advanced-billing:dotnet-error-handling` — steps 2–4 error boundary
- `maxio-advanced-billing:dotnet-configuration-resilience` — steps 1, 2, 3, 3a (timeouts, retries, logging, paging, unknown outcomes, duplicate claims)
- `maxio-advanced-billing:dotnet-testing` — tests
- Hazard (verbatim): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `MaxioSettings` bound from `Maxio:` with `AddOptions().Bind().Validate(...).ValidateOnStart()`: `ApiKey` non-blank; `ProductFamilyHandle` non-blank; `Subdomain` non-blank unless `BaseUrl` set; `BaseUrl` (if set) an absolute http(s) URI; `Environment` (optional) ∈ {US, EU}. Basic password is the provider-fixed `"x"` (not config). Failure message names the key, never the value. |
| 2 | Secret sourcing & rotation | Dev: .NET user-secrets on PublicApi (`Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:Environment`) loaded from `MAXIO_*` env vars; prod: env vars `Maxio__ApiKey` etc. or a secret store. Options built once inside the singleton factory → rotation takes effect on restart (documented; no hot rotation). |
| 3 | Total timeout budget | Per attempt: `Retry.Timeout = 10s`, `HttpClient.Timeout = 15s` backstop. Whole caller-facing operation: one linked `CancellationTokenSource` (request-aborted + 30s) created in `MaxioSubscriptionBillingService` and passed to every SDK call of that operation; unknown-outcome re-reads get their own fresh 10s budget (not tied to the request). Caller never waits > ~40s. |
| 4 | Write-retry ownership | Keep default `HttpMethodsToRetry` (GET/HEAD/PUT/OPTIONS): `CreateCustomer` and `CreateSubscription` (POST) are never resent by the SDK; GET lookups/lists retry (MaxRetries 2). No verb widening. |
| 5 | Idempotency & ambiguous writes | Neither `CreateCustomerOperationRequest` nor `CreateSubscriptionOperationRequest` has a key member → no provider idempotency key (the injected `Idempotency-Key` header is not one). Reconciliation keys: customer `reference` = `eshop-user-{identityUserId}` (re-read via `ReadCustomerByReference`); subscription `reference` = `eshop-sub-{identityUserId}-{familyHandle}` (re-read via `FindSubscription`). |
| 6 | Observability | `ILogger<…>`: Information — subscribe start/success (user id, plan handle, Maxio customer/subscription ids); Warning — provider 4xx (status, Maxio error strings), duplicate/in-progress claims; Error — provider 5xx, transport, deserialization, unknown outcome (and its settlement). Scope carries the PublicApi request `CorrelationId`. Maxio error bodies in scope carry no correlation id (ErrorListResponse1/CustomerErrorResponse1 only hold `errors`), so status + call (method + query-stripped URL from the SDK message) are logged. SDK built-in logger on with the host `ILoggerFactory`. |
| 7 | Sensitive data | Yes — `CreateCustomer` body carries email + name (PII). `LogRequestBody = false`, `LoggerFactory` assigned explicitly (disables `MAXIOADVANCEDBILLINGCLIENT_LOG`), no JSON form bodies. Our own logs never print request bodies or email. |
| 8 | Environment selection | Server group `Production` only (`Ebb` unused). `Maxio:Environment` US (default) or EU selects `ServerEnvironment`; `Maxio:Subdomain` fills `{site}` on the selected env; `Maxio:BaseUrl` when set overrides `BaseUrl` verbatim on the selected env. SDK has no sandbox environment: test traffic stays off live because dev/test deployments point at a Maxio sandbox *site* (`Maxio:Subdomain`), and the test suite points `Maxio:BaseUrl` at a stub/unroutable address. |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS. Store = existing EF Core `CatalogContext` (primary-key insert-or-fail). |
| 10 | Partial results | `ListProductsForProductFamily` walk capped at 5 pages × 200 → `SubscriptionPlanList.IsTruncated` / response `isTruncated`. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES: settle in the write's `catch` by re-read; if still unknown, row stays `Unknown` and `BillingReconciliationService` (hosted sweeper) + every later POST/GET for that user settle it. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| `CreateSubscription` | `SubscriptionEnrollments` table in `CatalogContext`, PK (`UserId`, `ProductFamilyHandle`) — one subscription per user per family | EF primary-key violation on `SaveChangesAsync` of the claim insert (relational PK → `DbUpdateException`; InMemory → `ArgumentException` "same key") | `EnrollmentStore.TryClaimEnrollmentAsync` `catch (Exception ex) when (IsPossibleDuplicateKey(ex))` → re-reads and returns the holder row (a `DbUpdateException` with no holder row is rethrown) | `EnrollmentStore.TryClaimEnrollmentAsync` (called from `MaxioSubscriptionBillingService.SubscribeCoreAsync`) → `MaxioSubscriptionBillingService.CreateSubscriptionAsync` (`_client.Subscriptions.CreateSubscription`) |
| `CreateCustomer` | `BillingCustomers` table in `CatalogContext`, PK `UserId` | EF primary-key violation on the claim insert | `EnrollmentStore.TryClaimCustomerAsync` `catch (Exception ex) when (IsPossibleDuplicateKey(ex))` → re-reads and returns the holder row | `EnrollmentStore.TryClaimCustomerAsync` (called from `MaxioSubscriptionBillingService.EnsureCustomerAsync`) → `MaxioSubscriptionBillingService.CreateCustomerAsync` (`_client.Customers.CreateCustomer`) |

**PAGED READS**

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| `ListProductsForProductFamily` | `MaxPlanPages = 5` × `PerPage = 200`; also stops on a short page | `SubscriptionPlanList.IsTruncated = true` → `ListSubscriptionPlansResponse.IsTruncated` | `MaxioPlanCatalog.FetchPlansAsync` (sets `IsTruncated: !reachedEnd`), surfaced by `SubscriptionPlanListEndpoint.HandleAsync` |

**UNKNOWN OUTCOMES**

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `CreateSubscription` | `Subscriptions.FindSubscription` | `reference` = `eshop-sub-{userId}-{familyHandle}` | `MaxioSubscriptionBillingService.CreateSubscriptionAsync` `catch (Exception ex) when (ex is SdkConnectionException or ResponseDeserializationException or OperationCanceledException)` → `SettleUnknownCreateAsync` → `SettleEnrollmentAsync`; still unknown → `EnrollmentState.Unknown`, settled by `BillingReconciliationService.ExecuteAsync` → `SettleUnsettledEnrollmentsAsync` and by every later subscribe / my-subscriptions call | `MaxioSubscriptionBillingServiceTests.CreateSubscription_ConnectionDrops_SettlesFromFindSubscription` (+ `..._ConnectionDropsAndLookupMisses_StaysUnknownUntilSettled`, `..._UnknownAndNeverLanded_ReleasedAfterGracePeriod`) |
| `CreateCustomer` | `Customers.ReadCustomerByReference` | `reference` = `eshop-user-{userId}` | `MaxioSubscriptionBillingService.CreateCustomerAsync` same `catch` → `SettleCustomerAsync`; still unknown → claim released in `EnsureCustomerAsync`, and the next attempt calls `TryFindCustomerAsync` before creating | `MaxioSubscriptionBillingServiceTests.CreateCustomer_ConnectionDrops_SettlesFromReadCustomerByReference` |

## 6. Assumptions & Blockers

- Assumption (YOUR CALL — not in the map): one subscription per user per configured product family; a repeat POST with the same plan returns the existing subscription (200, idempotent replay); a POST for a different plan while subscribed → 409 (plan change is out of scope).
- Assumption: the JWT carries only `ClaimTypes.Name` (username); the stable user identity is the Identity `ApplicationUser.Id` resolved via `UserManager.FindByNameAsync`. With `UseOnlyInMemoryDatabase=true`, Identity ids are regenerated each run, so a new run creates a new Maxio customer for the same username (consistent with the in-memory caveat).
- UNVERIFIED: whether Maxio enforces uniqueness of customer/subscription `reference` — code does not rely on it (local claim is the guard; re-reads validate the returned reference matches). Live sandbox: `ReadCustomerByReference` returned 404 for an unknown reference, as coded.
- UNVERIFIED: whether `ListProductsForProductFamily` excludes archived products by default — code sends `IncludeArchived = false` and also filters `ArchivedAt != null`.
- UNVERIFIED: whether a newly-created subscription is immediately visible to `FindSubscription` — a 404 on the re-read keeps the row `Unknown` (not released) until the sweeper's grace period (5 min) passes.
- Resolved during implementation: the seeded plans need no payment method, but the default automatic collection was rejected live (422 "No payment method was on file for the $299.00 balance"); subscriptions are therefore created with `PaymentCollectionMethod = Remittance` (invoice). Verified live: 201 `active`.
- Blockers: none. The SDK project (`netstandard2.0`, Microsoft.Extensions 10.x) builds and runs inside the net8.0 solution without version conflicts.

## 7. Source labels

Rows cite the map page / map-named file; `UNVERIFIED` and `YOUR CALL — not in the map` are marked inline above.
