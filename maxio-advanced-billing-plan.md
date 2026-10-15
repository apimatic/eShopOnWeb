# Maxio Advanced Billing — eShopOnWeb subscription billing plan

SDK root (plugin-relative): `sdk/dotnet/` in the `maxio-advanced-billing` plugin. All `src/…` / `map/…` paths below are relative to that root.

## 1. Scope & sequence

| # | Step | Operations |
| --- | --- | --- |
| 1 | Reference the SDK project from `src/Infrastructure`; global.json roll-forward for the local SDK | — |
| 2 | `MaxioSettings` bound from `Maxio:` (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl`, optional `Environment`), validated on start; singleton `MaxioAdvancedBillingClient` over a named `HttpClient` — registered only by PublicApi | — |
| 3 | `MaxioBillingGateway` (Infrastructure) behind `ISubscriptionBillingGateway` (ApplicationCore): one error-translation ladder, one per-call budget | all below |
| 4 | Plans: list the configured family's products (manual paging, page cap, `Truncated` flag, short cache) | `ProductFamilies.ListProductsForProductFamily` |
| 5 | Local claim store (`CatalogContext`): `BillingCustomer` (PK BuyerId) and `SubscriptionEnrollment` (PK BuyerId+PlanHandle) | — |
| 6 | Ensure customer (claim → lookup by reference → create → record) | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer` |
| 7 | Subscribe (claim → create with our `reference` → record; unknown outcome settled by reference lookup) | `Subscriptions.CreateSubscription`, `Subscriptions.FindSubscription` |
| 8 | My subscriptions (settle unsettled claims, then list the customer's subscriptions) | `Customers.ListCustomerSubscriptions`, `Subscriptions.FindSubscription` |
| 9 | PublicApi endpoints `GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions` (JWT); middleware mapping; tests; EF migration | — |

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, verbatim. Each operation that takes input takes ONE request record as its first parameter, built with an object initializer using the record's own property names — never flat arguments.
> ⚠ Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type (Models → `MaxioAdvancedBilling.Models`, Enums → `.Models.Enums`, AnyOf → `.Models.AnyOf`, Errors → `.Errors`, Requests → `.Requests.<Controller>`, exceptions → `.Core.Exceptions`, `RawError` → `.Core.ErrorResponse`).

| Controller · method | Request record + members | Body model + fields | Response + fields read | Error case + accessors | Paging | Source |
| --- | --- | --- | --- | --- | --- | --- |
| `client.ProductFamilies` · `ListProductsForProductFamily(ListProductsForProductFamilyRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `Requests.ProductFamilies.ListProductsForProductFamilyRequest`: `ProductFamilyId: string, required` (id or `handle:<handle>`), `Page: int = 1`, `PerPage: int = 20` (max 200), `IncludeArchived: bool?` | — | `IReadOnlyList<Models.ProductResponse>` → `.Product` (required `Models.Product`): `Id int?` (id), `Name string?` (name), `Handle string?` (handle), `Description string?` (description), `PriceInCents long?` (price_in_cents), `Interval int?` (interval), `IntervalUnit Models.Enums.IntervalUnit?` (interval_unit), `ArchivedAt DateTimeOffset?` (archived_at), `ProductFamily.Handle string?` | **A** `ApiException<Errors.ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` | none declared by SDK — drive `Page`/`PerPage` manually | `map/operations/ProductFamilies.md`; `src/MaxioAdvancedBilling/Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs`; `src/MaxioAdvancedBilling/Models/Product.cs` |
| `client.Customers` · `ReadCustomerByReference(ReadCustomerByReferenceRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `Requests.Customers.ReadCustomerByReferenceRequest`: `Reference: string, required` | — | `Models.CustomerResponse` → `.Customer` (required `Models.Customer`): `Id int?` (id), `Reference string?` (reference) | **B** `ApiException<RawError>` (404 = no such customer) | — | `map/operations/Customers.md`; remarks in `src/MaxioAdvancedBilling/Api/Customers.cs` ("returns a single match") |
| `client.Customers` · `CreateCustomer(CreateCustomerOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `Requests.Customers.CreateCustomerOperationRequest`: `Body: Models.CreateCustomerRequest?` (nothing required on the record) | `Models.CreateCustomerRequest`: `Customer (customer): Models.CreateCustomer, required` → `FirstName (first_name): string, required`, `LastName (last_name): string, required`, `Email (email): string, required`, `Reference (reference): string?` — remarks: reference must be unique per site. Left out: address, phone, VAT/entity identifiers, locale, parent, tax fields | `Models.CustomerResponse` → `.Customer.Id` | **A** `ApiException<Errors.CreateCustomerError>`: `TryGetCustomerErrorResponse1(out Models.CustomerErrorResponse1)` [422] → `.Errors: Models.AnyOf.Errors1?` (`TryGetCustomerError(out Models.CustomerError)` → `.Customer string?`; `TryGetListOfString(out IReadOnlyList<string>)`) · `TryGetRawError(out RawError)` | — | `map/operations/Customers.md`; `src/MaxioAdvancedBilling/Models/CreateCustomer.cs`; `src/MaxioAdvancedBilling/Models/AnyOf/Errors1.cs` |
| `client.Customers` · `ListCustomerSubscriptions(ListCustomerSubscriptionsRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `Requests.Customers.ListCustomerSubscriptionsRequest`: `CustomerId: int, required` | — | `IReadOnlyList<Models.SubscriptionResponse>` → `.Subscription` (`Models.Subscription?`) fields as below | **B** `ApiException<RawError>` | none (single response) | `map/operations/Customers.md` |
| `client.Subscriptions` · `CreateSubscription(CreateSubscriptionOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `Requests.Subscriptions.CreateSubscriptionOperationRequest`: `Body: Models.CreateSubscriptionRequest?` | `Models.CreateSubscriptionRequest`: `Subscription (subscription): Models.CreateSubscription, required` → nothing required; remarks tie acceptance to: `ProductHandle (product_handle): string?` **or** `ProductId`; `CustomerId (customer_id): int?` **or** `CustomerReference` **or** `CustomerAttributes`; payment info "may be required depending on product options". Sent: `ProductHandle`, `CustomerId`, `Reference (reference): string?` ("reference value provided by your app for the subscription"), `PaymentCollectionMethod (payment_collection_method): Models.Enums.CollectionMethod?` (doc: Relationship Invoicing → `remittance`/`automatic`/`prepaid`; legacy Statements → `invoice`/`automatic`) — live sandbox rejected the default (automatic) create with "No payment method was on file", so the integration sends the configured `Maxio:PaymentCollectionMethod` (default `remittance`). Left out: price-point, coupons, payment profile/card/bank attributes, components, calendar billing, metafields, dates, `defer_signup`, group, offer, prepaid config | `Models.SubscriptionResponse` → `.Subscription` (`Models.Subscription?`): `Id int?`, `State Models.Enums.SubscriptionState?` (`.Value`), `Reference string?`, `ProductPriceInCents long?` (product_price_in_cents), `Currency string?`, `NextAssessmentAt DateTimeOffset?` (next_assessment_at), `CurrentPeriodEndsAt DateTimeOffset?`, `ActivatedAt DateTimeOffset?`, `CreatedAt DateTimeOffset?`, `Product Models.Product?` (handle/name/interval), `Customer Models.Customer?` | **A** `ApiException<Errors.CreateSubscriptionError>`: `TryGetErrorListResponse1(out Models.ErrorListResponse1)` [422] → `.Errors: IReadOnlyList<string>, required` · `TryGetRawError(out RawError)` | — | `map/operations/Subscriptions.md`; `src/MaxioAdvancedBilling/Models/CreateSubscription.cs`; remarks in `src/MaxioAdvancedBilling/Api/Subscriptions.cs` |
| `client.Subscriptions` · `FindSubscription(FindSubscriptionRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `Requests.Subscriptions.FindSubscriptionRequest`: `Reference: string?` | — | `Models.SubscriptionResponse` → `.Subscription` | **A** `ApiException<Errors.FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` | — | `map/operations/Subscriptions.md` |

**Enums needed**

| Enum (namespace) | Values used | Source |
| --- | --- | --- |
| `SubscriptionState` (`MaxioAdvancedBilling.Models.Enums`, `OpenStringEnum`) | read-only, surfaced as `.Value` (wire: `pending`, `failed_to_create`, `trialing`, `assessing`, `active`, `soft_failure`, `past_due`, `suspended`, `canceled`, `expired`, `paused`, `unpaid`, `trial_ended`, `on_hold`, `awaiting_signup`, …) | `src/MaxioAdvancedBilling/Models/Enums/SubscriptionState.cs` |
| `IntervalUnit` (`MaxioAdvancedBilling.Models.Enums`) | read-only `.Value` (`day`, `month`) | `src/MaxioAdvancedBilling/Models/Enums/IntervalUnit.cs` |
| `CollectionMethod` (`MaxioAdvancedBilling.Models.Enums`, `OpenStringEnum`) | written: `Remittance` (`remittance`, default), `Invoice` (`invoice`), `Automatic` (`automatic`), `Prepaid` (`prepaid`) — resolved from config with `TryGetKnownValue` | `src/MaxioAdvancedBilling/Models/Enums/CollectionMethod.cs` |
| `ServerEnvironment` (`MaxioAdvancedBilling.Servers`, closed) | `Us` (`US`, default), `Eu` (`EU`) — resolved with `TryGetKnownValue` | `src/MaxioAdvancedBilling/Servers/ServerEnvironment.cs` |

**Client construction / auth / servers**: ctor `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`; `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <API key>, Password = "x" }` (map *Servers & auth*: password is literally `x`). All in-scope operations are on server group `Production`: `options.Server.Production.Us.Site` / `.Eu.Site` fill `{site}`; `options.Server.Production.{Us|Eu}.BaseUrl` overrides the template (`https://{site}.chargify.com` / `https://{site}.ebilling.maxio.com`). `options.Retry` (`MaxioAdvancedBilling.Core.Configuration.RetryOptions`, start from `RetryOptions.Default()`), `options.Logging` (`LoggingOptions`). Source: `sdk-map.md`; `src/MaxioAdvancedBilling/Servers/ProductionOptions.cs`.

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| The plan handle a caller passes to POST must be a non-archived product returned for the configured family (`Maxio:ProductFamilyHandle`) | `CreateSubscription` ← `ListProductsForProductFamily` | `SubscriptionService.SubscribeAsync` reads the (cached) catalog via `ISubscriptionBillingGateway.ListPlansAsync` and rejects unknown handles before claiming / calling `CreateSubscription` |
| The `customer_id` sent on create must be the Maxio customer bound to the calling buyer | `CreateSubscription` ← `ReadCustomerByReference` / `CreateCustomer` (stored on `BillingCustomer`) | `SubscriptionService.EnsureCustomerAsync` returns only the id stored for/created with this buyer's deterministic reference |
| The `CustomerId` listed in my-subscriptions is the one stored for the calling buyer | `ListCustomerSubscriptions` ← `CreateCustomer` / `ReadCustomerByReference` | `SubscriptionService.GetMySubscriptionsAsync` reads `BillingCustomer` by the token's buyer id only |
| A reference used to settle an unknown outcome is the one this app generated and stored on the claim | `FindSubscription` ← `CreateSubscription` | `SubscriptionService.SettleAsync` looks up `SubscriptionEnrollment.SubscriptionReference` only |

## 3. Trap notes

| Step | Hazard | Pointer |
| --- | --- | --- |
| 2 | Lifetime of the `HttpClient` behind a singleton SDK client, and DNS staleness | MUST load `maxio-advanced-billing:dotnet-client-initialization` |
| 2 | A blank credential sends unauthenticated requests rather than failing; which parts need validating | MUST load `maxio-advanced-billing:dotnet-authentication` |
| 2 | When `Environment` vs `Server` options are read; which per-environment `BaseUrl` is honoured | MUST load `maxio-advanced-billing:dotnet-configuration-resilience` |
| 2 | What each of the three timeout layers bounds, and which retries fire on which verbs | MUST load `maxio-advanced-billing:dotnet-configuration-resilience` |
| 2 | SDK built-in logging switches and the env-var that can force body logging | MUST load `maxio-advanced-billing:dotnet-configuration-resilience` |
| 3–8 | Positional argument order around `requestOptions` | MUST load `maxio-advanced-billing:dotnet-calling-endpoints` |
| 3–8 | Which exception types reach the catch, Case A vs B, accessor order, parse failures | MUST load `maxio-advanced-billing:dotnet-error-handling` |
| 4,7,8 | Open enums' string form; AnyOf error union reading; nullable response members | MUST load `maxio-advanced-billing:dotnet-models` |
| 4 | Manual paging termination and partial results | MUST load `maxio-advanced-billing:dotnet-configuration-resilience` |
| 6,7 | A transport failure on a POST leaves the outcome unknown | MUST load `maxio-advanced-billing:dotnet-configuration-resilience` |
| 9 | Stubbing the SDK, request-body capture, constructing SDK exceptions | MUST load `maxio-advanced-billing:dotnet-testing` |

## 4. REQUIRED READING

- `maxio-advanced-billing:dotnet-client-initialization` — step 2 (client + DI)
- `maxio-advanced-billing:dotnet-authentication` — step 2 (Basic auth, fail-fast)
- `maxio-advanced-billing:dotnet-configuration-resilience` — steps 2, 4, 6, 7 (server, retries, timeouts, paging, unknown outcomes, logging)
- `maxio-advanced-billing:dotnet-calling-endpoints` — steps 3–8
- `maxio-advanced-billing:dotnet-models` — steps 4, 7, 8
- `maxio-advanced-billing:dotnet-error-handling` — steps 3–8 (gateway ladder)
- `maxio-advanced-billing:dotnet-testing` — step 9

Hazard (verbatim): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `MaxioSettings` bound from `Maxio:` with `[Required]` on `ApiKey`, `Subdomain`, `ProductFamilyHandle` + `IValidatableObject` (blank/whitespace rejected, `BaseUrl` must be absolute https/http URI when set, `Environment` must be `US`/`EU`) + `ValidateOnStart()`. Basic-auth password is the constant `x` (map), so the only secret part is `ApiKey`. Failure message names the key, never the value. Registered only in PublicApi (Web does not need Maxio). |
| 2 | Secret sourcing & rotation | `Maxio:ApiKey` comes from user-secrets (dev) / environment (`Maxio__ApiKey`) / secret store (prod); never in repo files. Options built once at registration into the singleton client → rotation requires a process restart (documented). |
| 3 | Total timeout budget | Per attempt: `Retry.Timeout = 10s`; `HttpClient.Timeout = 15s` backstop; per SDK call: linked CTS budget `Maxio:CallBudgetSeconds` (default 15s) applied in the gateway's single `BoundedAsync`. Reads linked to `HttpContext.RequestAborted`; writes are NOT linked to the caller's abort (a claimed write runs to a settled outcome). Worst-case POST /api/subscriptions = catalog read + customer lookup + customer create + subscription create + reconciliation lookup ≈ 5 × 15s = 75s; typical < 3s. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` kept (GET/HEAD/PUT/OPTIONS) → `CreateCustomer` and `CreateSubscription` (POST) are never resent by the SDK; GETs retried up to 3 within the call budget. Not widened. |
| 5 | Idempotency & ambiguous writes | `CreateCustomer`: no key member on the record; real dedupe is the body's `customer.reference` (remarks: must be unique per site) = deterministic `eshop-user-<buyerId>`; reconciliation = `ReadCustomerByReference`. `CreateSubscription`: no key member on the record; body `subscription.reference` = app-generated reference stored on the claim before the call (provider-side uniqueness `UNVERIFIED`); reconciliation = `FindSubscription(Reference)`. The injected `Idempotency-Key` header is not relied on. |
| 6 | Observability | App logs (via `IAppLogger`/`ILogger`): Information = subscribe created/replayed/settled with buyer id + subscription reference + Maxio ids; Warning = provider 4xx rejection (status + provider error strings), claim refusals; Error = unavailable/unknown outcomes (status/exception type, call name). Provider error bodies in scope (`ErrorListResponse1`, `CustomerErrorResponse1`) carry no correlation id, so our own request correlation id (`BaseMessage.CorrelationId()`) and the subscription reference are logged. SDK built-in logger uses the host `ILoggerFactory` (request line at Information, failures at Warning/Error). |
| 7 | Sensitive data | Yes: `CreateCustomer` body carries personal data (first/last name, email). `LogRequestBody = false`, `LogRequestHeaders/ResponseHeaders = false`, `LoggerFactory` set explicitly (disables `MAXIOADVANCEDBILLINGCLIENT_LOG`). JSON bodies only — no form bodies, so no deny-list additions needed. App logs never include email/names. |
| 8 | Environment selection | Server groups: `Production` (used by every in-scope op) and `Ebb` (events-based billing — not touched). `Maxio:Environment` (`US` default, `EU`) selects `ServerEnvironment`; `{site}` = `Maxio:Subdomain`; when `Maxio:BaseUrl` is set it is assigned verbatim to `options.Server.Production.<selected env>.BaseUrl`. The SDK declares no sandbox environment: test traffic is kept off live data by pointing dev/test at the sandbox *site* (`Maxio:Subdomain` from `MAXIO_SITE_SUBDOMAIN`) and its own API key; unit/integration tests use a stub `HttpMessageHandler` or a fake gateway, never the network. |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS. Store = `CatalogContext` (the app's existing EF store); refusal = primary-key insert. |
| 10 | Partial results | `ListProductsForProductFamily` manual paging capped (`MaxPlanPages` = 10 × 200) → `SubscriptionPlanCatalog.Truncated` → `ListSubscriptionPlansResponse.Truncated`. `ListCustomerSubscriptions` is a single unpaged response — N/A. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| `CreateSubscription` (double-click / concurrent POST for the same buyer + plan) | `SubscriptionEnrollments` table in `CatalogContext`, PK (`BuyerId`, `PlanHandle`), state `Pending` + generated `SubscriptionReference` | Primary-key violation on insert — `DbUpdateException` on SQL Server; the EF in-memory provider refuses with `ArgumentException` (observed live). A no-tracking `AnyAsync` pre-check short-circuits sequential replays only; the PK insert is the guard | `EfSubscriptionClaimStore.TryInsertAsync` `catch (Exception ex) when (IsDuplicateKey(ex))` → `TryClaimEnrollmentAsync` returns `false`; `SubscriptionService.SubscribeAsync` then loads the existing row (replay if Active, 409 if in flight, settle if Unknown/stale) | `EfSubscriptionClaimStore.TryClaimEnrollmentAsync` → `MaxioBillingGateway.CreateSubscriptionAsync` (called from `SubscriptionService.CreateAndRecordAsync`) |
| `CreateCustomer` (two concurrent subscribes by the same buyer for different plans) | `BillingCustomers` table in `CatalogContext`, PK `BuyerId`, holds deterministic `CustomerReference`, `MaxioCustomerId` null until recorded | Primary-key violation on insert (same `IsDuplicateKey` rule) | `EfSubscriptionClaimStore.TryInsertAsync` catch → `TryClaimCustomerAsync` returns `false`; `SubscriptionService.EnsureCustomerAsync` then uses the recorded id, or (claim held, not stale) lookup-by-reference only and 409 if not yet created; stale claim (> `ClaimStaleAfter`) is taken over (provider-side reference uniqueness backstops) | `EfSubscriptionClaimStore.TryClaimCustomerAsync` (called from `SubscriptionService.EnsureCustomerAsync`) → `MaxioBillingGateway.CreateCustomerAsync` (called from `SubscriptionService.CreateCustomerAsync`) |

Release: when the provider refuses `CreateSubscription` (4xx) or a definite pre-create failure occurs, the enrollment claim row is deleted (`ReleaseEnrollmentAsync`); unsettled claims are settled (and released when the provider has no such reference) by `SettleAsync`.

**PAGED READS**

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| `ListProductsForProductFamily` (manual `Page`/`PerPage=200`) | `MaxPlanPages` (10) page cap + per-call budget; stops when a page returns < `PerPage` items | `SubscriptionPlanCatalog.Truncated` → `ListSubscriptionPlansResponse.Truncated` (`truncated: true` in JSON) | `MaxioBillingGateway.ListPlansAsync` (sets `Truncated`) → `SubscriptionPlanListEndpoint.HandleAsync` |

**UNKNOWN OUTCOMES**

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `CreateSubscription` | `Subscriptions.FindSubscription` | `SubscriptionEnrollment.SubscriptionReference` (sent as `subscription.reference`) | `SubscriptionService.CreateAndRecordAsync` `catch (BillingOutcomeUnknownException)` → `SettleAsync`; if still unknown → row marked `Unknown`, POST returns 202, settled later by `SettleAsync` from `SubscribeAsync`/`GetMySubscriptionsAsync` | `SubscriptionServiceTests.CreateSubscription_ConnectionFails_SettlesFromProviderLookup`, `…_LookupAlsoFails_RecordsUnknownAndReturnsPending`; gateway: `MaxioBillingGatewayTests.CreateSubscription_TransportFailure_ThrowsOutcomeUnknown_AndSendsOnce` |
| `CreateCustomer` | `Customers.ReadCustomerByReference` | `BillingCustomer.CustomerReference` (sent as `customer.reference`) | `SubscriptionService.CreateCustomerAsync` `catch (BillingOutcomeUnknownException)` → `FindCustomerByReferenceAsync`; not found/unreachable → customer and enrollment claims released, 503 — the next attempt re-reads by the same deterministic reference before creating, so a create that did land is adopted, never duplicated | `SubscriptionServiceTests.CreateCustomer_ConnectionFails_SettlesFromReferenceLookup`, `…_ConnectionFailsAndNotFound_ReleasesBothClaims` |

## 6. Assumptions & Blockers

- Assumption: one subscription per (buyer, plan); a repeat POST for a plan the buyer already holds returns the existing subscription (200) instead of creating another. Plan changes/cancellation are out of scope.
- Assumption: Maxio customer `reference` = `eshop-user-<Identity user id>`; with the in-memory DB the Identity ids are regenerated each run, so a restart yields a new customer (task-acknowledged caveat).
- Assumption: buyer names are not in Identity; POST accepts optional `firstName`/`lastName`, defaulting to the email local-part / `"Customer"`.
- `UNVERIFIED`: whether Maxio rejects a duplicate subscription `reference`; the design does not rely on it (claim is local).
- `UNVERIFIED`: whether `FindSubscription` 404 is immediate after a create that landed (read-after-write consistency); a 404 during settlement releases the claim only after the claim is stale (> `ClaimStaleAfter`) — a fresh 404 leaves it `Unknown`.
- Blockers: none.

## 7. Source labels

Rows cite `map/operations/*.md` or the named `src/MaxioAdvancedBilling/...` file; `UNVERIFIED` and `YOUR CALL — not in the map` items are listed in §6 (design choices in §5 are `YOUR CALL — not in the map`).

## 8. Implementation & verification record

| Item | Outcome |
| --- | --- |
| Live sandbox finding | `CreateSubscription` without a collection method → 422 `No payment method was on file for the $299.00 balance`. Decision (`YOUR CALL — not in the map`): card capture is out of scope, so subscriptions are invoice-collected via `Maxio:PaymentCollectionMethod` (default `remittance`; validated against `CollectionMethod` at startup). Set `invoice` on a legacy Statements site, `automatic` once a payment-profile flow exists. |
| Live sandbox finding | The EF in-memory provider refuses a duplicate primary key with `ArgumentException`, not `DbUpdateException`; `EfSubscriptionClaimStore.IsDuplicateKey` handles both. |
| Live verification (site from `Maxio:Subdomain`) | Plans listed from the family handle; 3 and 5 concurrent `POST /api/subscriptions` → exactly one `201 created`, the rest `409`; replay → `200 existing`; second plan → `201` reusing the same Maxio customer; `GET /api/my-subscriptions` shows plan/price/state/next billing date; unknown plan → `400`; blank `Maxio:ApiKey` → host refuses to start naming the key; `Maxio:BaseUrl` override used verbatim (GET retried, then 503 + `Retry-After` at the 15 s budget). |
| Tests | 27 new unit tests (`SubscriptionServiceTests`, `MaxioBillingGatewayTests` over a stub `HttpMessageHandler`), 4 EF claim-store integration tests (`ClaimEnrollment`, incl. 20 concurrent claims → one winner), 5 endpoint tests (`SubscriptionEndpointsTest`, fake gateway). Whole solution: 110 passing. |
| Migration | `AddSubscriptionBilling` creates `BillingCustomers` and `SubscriptionEnrollments` only (pre-existing unrelated nullability drift deliberately left out). |
| Portability | `src/Infrastructure` references the SDK project at this plugin's install path (`../marketplace/plugins/maxio-advanced-billing/sdk/dotnet/...`); a machine or CI runner without the plugin there (including the Docker build) must re-point that `ProjectReference`. |
