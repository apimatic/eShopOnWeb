# Maxio Advanced Billing — subscription billing for eShopOnWeb (plan + contract sheet)

SDK: `MaxioAdvancedBilling` (.NET), source at `sdk/dotnet/` inside the **maxio** plugin (context-plugins
marketplace). All `source` cells below are relative to that SDK root. Host: `src/PublicApi` (JWT).

## 1. Scope & sequence

| # | Step | Operations |
|---|------|------------|
| 1 | `global.json` roll-forward; SDK `ProjectReference` from `src/Infrastructure` | — |
| 2 | Settings (`Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl`) + fail-fast validation; named `HttpClient` + client registration | — |
| 3 | Persistence: `SubscriptionEnrollment` (PK = buyer id) in `CatalogContext` + EF config + migration — the duplicate-claim store | — |
| 4 | `GET /api/subscription-plans` — list plans of the configured family (paged walk, cached briefly) | `ProductFamilies.ListProductsForProductFamily` |
| 5 | `POST /api/subscriptions` — validate plan ∈ family list → claim → ensure customer (by reference) → adopt live sub if one exists → create subscription (with our reference) → record | `ListProductsForProductFamily`, `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Customers.ListCustomerSubscriptions`, `Subscriptions.CreateSubscription`, `Subscriptions.FindSubscription` (reconcile), `Subscriptions.ReadSubscription` (stale-claim check) |
| 6 | `GET /api/my-subscriptions` — customer by reference → its subscriptions | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |
| 7 | Error boundary → HTTP (504 "Maxio did not respond", 502, 409, 422, 400) in `ExceptionMiddleware` | — |
| 8 | Offline tests (fake `HttpMessageHandler` seam) + live sandbox verification | — |

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, verbatim — each operation that takes input takes ONE request record as its first
> parameter, built with an object initializer whose property names are the record's own, never flat arguments.
> ⚠ Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map
> gives for THAT type, never from where a neighbouring type sits.

All ops: `Task<T> Op(Req request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)`;
throw-only; auth `BasicAuth` OR `BearerAuth`; server group `Production`.

| Controller · method | Request record (members) | Body model (fields) | Returns → fields read | Error case | Paging | Source |
|---|---|---|---|---|---|---|
| `client.ProductFamilies` · `ListProductsForProductFamily` | `MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest`: `ProductFamilyId: string, required` (id **or** `handle:<handle>`), `Page: int = 1` (min 1), `PerPage: int = 20` (max 200), `IncludeArchived: bool?` | — (GET) | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>` → `.Product` (`required Product`) → `Id int?`, `Name string?`, `Handle string?`, `Description string?`, `PriceInCents long?`, `Interval int?`, `IntervalUnit IntervalUnit?`, `ArchivedAt DateTimeOffset?`, `RequireCreditCard bool?`, `ProductFamily ProductFamily?` | **A** `ApiException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` | map: none; record carries `Page`/`PerPage` → app walks pages | `map/operations/ProductFamilies.md`; `Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs`; `Models/ProductResponse.cs`; `Models/Product.cs` |
| `client.Customers` · `ReadCustomerByReference` | `MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest`: `Reference: string, required` | — | `MaxioAdvancedBilling.Models.CustomerResponse` → `.Customer` (`required Customer`) → `Id int?`, `Reference string?`, `Email string?` | **B** `ApiException<RawError>` (not-found arrives here; status read from `StatusCode`) | none | `map/operations/Customers.md`; `Requests/Customers/ReadCustomerByReferenceRequest.cs`; `Models/CustomerResponse.cs`; `Models/Customer.cs` |
| `client.Customers` · `CreateCustomer` | `MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest`: `Body: CreateCustomerRequest?` (nothing required; we always set it) | `MaxioAdvancedBilling.Models.CreateCustomerRequest`: `Customer (customer): CreateCustomer, required` → `MaxioAdvancedBilling.Models.CreateCustomer`: `FirstName (first_name): string, required`, `LastName (last_name): string, required`, `Email (email): string, required`, `Reference (reference): string?` (prose: must be unique per site — the dedupe key). Left out: address/phone/vat/tax/locale/org/parent/branding fields | `CustomerResponse` → `.Customer.Id` | **A** `ApiException<MaxioAdvancedBilling.Errors.CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError` | — | `map/operations/Customers.md`; `Requests/Customers/CreateCustomerOperationRequest.cs`; `Models/CreateCustomerRequest.cs`; `Models/CreateCustomer.cs`; `Errors/CreateCustomerError.cs`; `Models/CustomerErrorResponse1.cs` (`Errors (errors): MaxioAdvancedBilling.Models.AnyOf.Errors1?`) ; remarks `Api/Customers.cs` |
| `client.Customers` · `ListCustomerSubscriptions` | `MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest`: `CustomerId: int, required` | — | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>` → `.Subscription` (**nullable** `Subscription?`) | **B** | none (no page params) | `map/operations/Customers.md`; `Requests/Customers/ListCustomerSubscriptionsRequest.cs`; `Models/SubscriptionResponse.cs` |
| `client.Subscriptions` · `CreateSubscription` | `MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest`: `Body: CreateSubscriptionRequest?` | `MaxioAdvancedBilling.Models.CreateSubscriptionRequest`: `Subscription (subscription): CreateSubscription, required` → `MaxioAdvancedBilling.Models.CreateSubscription` (no `required` members): we set `ProductHandle (product_handle): string?` ("required unless product_id"), `CustomerId (customer_id): int?` ("required unless customer_reference / customer_attributes"), `Reference (reference): string?` (our per-claim reference), `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` = `Remittance` (see §6). Left out: payment profile / card / bank attributes, coupons, components, metafields, dates | `SubscriptionResponse` → `.Subscription?` → `Id int?`, `State SubscriptionState?`, `ProductPriceInCents long?`, `CurrentPeriodEndsAt`, `NextAssessmentAt`, `CreatedAt`, `ActivatedAt`, `Currency string?`, `Reference string?`, `Product Product?`, `Customer Customer?` | **A** `ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] (`Errors (errors): IReadOnlyList<string>, required`) · `TryGetRawError` | — | `map/operations/Subscriptions.md`; `Requests/Subscriptions/CreateSubscriptionOperationRequest.cs`; `Models/CreateSubscriptionRequest.cs`; `Models/CreateSubscription.cs`; `Errors/CreateSubscriptionError.cs`; `Models/ErrorListResponse1.cs`; `Models/Subscription.cs` |
| `client.Subscriptions` · `FindSubscription` | `MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest`: `Reference: string?` | — | `SubscriptionResponse` | **A** `ApiException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError` | — | `map/operations/Subscriptions.md`; `Requests/Subscriptions/FindSubscriptionRequest.cs`; `Errors/FindSubscriptionError.cs` |
| `client.Subscriptions` · `ReadSubscription` | `MaxioAdvancedBilling.Requests.Subscriptions.ReadSubscriptionRequest`: `SubscriptionId: required` | — | `SubscriptionResponse` | **B** | — | `map/operations/Subscriptions.md`; `Requests/Subscriptions/ReadSubscriptionRequest.cs` |

**Enums** (`OpenStringEnum`, not C# enums — `.Value` is the wire string; `Match(..., otherwise)` for unknowns):

| Enum | Members (wire) | Source |
|---|---|---|
| `MaxioAdvancedBilling.Models.Enums.SubscriptionState` | `Pending` pending, `FailedToCreate` failed_to_create, `Trialing`, `Assessing`, `Active`, `SoftFailure` soft_failure, `PastDue` past_due, `Suspended`, `Canceled`, `Expired`, `Paused`, `Unpaid`, `TrialEnded` trial_ended, `OnHold` on_hold, `AwaitingSignup` awaiting_signup | `Models/Enums/SubscriptionState.cs` |
| `MaxioAdvancedBilling.Models.Enums.IntervalUnit` | `Day` day, `Month` month | `Models/Enums/IntervalUnit.cs` |
| `MaxioAdvancedBilling.Models.Enums.CollectionMethod` | `Automatic` automatic, `Remittance` remittance, `Prepaid` prepaid, `Invoice` invoice | `Models/Enums/CollectionMethod.cs` |

**Client / auth / servers**: ctor `new MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)` (only ctor) — `MaxioAdvancedBillingClient.cs`.
Options (`MaxioAdvancedBillingClientOptions.cs`): `Environment` (`MaxioAdvancedBilling.Servers.ServerEnvironment.Us` default), `Retry` (`MaxioAdvancedBilling.Core.Configuration.RetryOptions`, start from `RetryOptions.Default()`), `Logging`, `Server`, `BasicAuth` (`MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <api key>, Password = "x" }` — sdk-map *Servers & auth*), `BearerAuth` (gateway only; unused).
Base URL: `options.Server.Production.Us.BaseUrl` (default `https://{site}.chargify.com`), `options.Server.Production.Us.Site` (default `"subdomain"`) — `Servers/ProductionOptions.cs`. Exceptions: `MaxioAdvancedBilling.Core.Exceptions.{ApiException, ApiException<T>, ResponseDeserializationException, SdkConnectionException, SdkTimeoutException, SdkException}`; `MaxioAdvancedBilling.Core.ErrorResponse.RawError`; `MaxioAdvancedBilling.Core.RequestOptions` (`Core/RequestOptions.cs`).

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
|---|---|---|
| `planHandle` accepted by subscribe must be a non-archived product handle returned by the configured family's product list | `CreateSubscription` ← `ListProductsForProductFamily` (`handle:<Maxio:ProductFamilyHandle>`) | `MaxioSubscriptionBillingService.SubscribeAsync` → `ResolvePlanAsync` before the claim and before `CreateSubscription` |
| `customer_id` passed to `CreateSubscription` must be the id returned for **this shopper's** reference | `CreateSubscription` ← `ReadCustomerByReference` / `CreateCustomer` | `EnsureCustomerAsync` result is the only source of `CustomerId` |
| subscription adopted / returned on replay must belong to this shopper's customer | `ListCustomerSubscriptions` / `FindSubscription` ← customer from `EnsureCustomerAsync` | adoption only scans `ListCustomerSubscriptions(customerId)`; `FindSubscription` result accepted only if its `Customer.Id` matches |

## 3. Trap notes

| Step | Hazard → consequence | Skill |
|---|---|---|
| 2 | `HttpClient` lifetime vs. client lifetime; the SDK's own DI helper and what it captures → socket/DNS staleness or untestable seam | MUST load `maxio:dotnet-client-initialization` |
| 2 | How the Basic credential is applied and what a missing credential does at send time → silent unauthenticated calls / 401 in prod | MUST load `maxio:dotnet-authentication` |
| 2,4–6 | What `Timeout` bounds, which methods retry, how a per-attempt timeout compounds → 30 s budget silently exceeded | MUST load `maxio:dotnet-configuration-resilience` |
| 2 | Base-URL override + `{site}` template semantics → calls to the wrong host | MUST load `maxio:dotnet-configuration-resilience` |
| 2 | `LogRequestBody` + unset `LoggerFactory` + `MAXIOADVANCEDBILLINGCLIENT_LOG` → PII (email/name) in logs | MUST load `maxio:dotnet-configuration-resilience` |
| 4–6 | Request-record construction, optional vs required members, cancellation token placement | MUST load `maxio:dotnet-calling-endpoints` |
| 4–6 | Open enums, nullable envelopes (`SubscriptionResponse.Subscription?`), AnyOf error payloads | MUST load `maxio:dotnet-models` |
| 5–7 | Which exception types reach the catch, Case A vs Case B, 404 detection on Case B ops → wrong mapping / escaped exceptions | MUST load `maxio:dotnet-error-handling` |
| 8 | Which seam to fake and how to stub wire-shaped bodies → tests that pass without exercising the SDK | MUST load `maxio:dotnet-testing` |

## 4. REQUIRED READING (load all **before implementation starts**; the sheet deliberately does not carry their contents)

- `maxio:dotnet-client-initialization` — step 2 (client + DI)
- `maxio:dotnet-authentication` — step 2 (Basic credential)
- `maxio:dotnet-calling-endpoints` — steps 4–6
- `maxio:dotnet-models` — steps 4–6
- `maxio:dotnet-error-handling` — steps 5–7 (error boundary)
- `maxio:dotnet-configuration-resilience` — step 2 (timeouts, retries, base URL, logging)
- `maxio:dotnet-testing` — step 8
- Hazard (verbatim): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
|---|---|---|
| 1 | Credential fail-fast | `MaxioSettings` bound from `Maxio:`; `ValidateOnStart`: `ApiKey` non-blank, `ProductFamilyHandle` non-blank, `Subdomain` non-blank **unless** `BaseUrl` set; `BaseUrl` (if set) must be absolute http(s). Host refuses to start otherwise. Basic password is the constant `x` (map), so only the key part is checked. |
| 2 | Secret sourcing & rotation | Dev: .NET user-secrets of `src/PublicApi` (loaded from `MAXIO_*` env vars by the operator); prod: any config provider (env `Maxio__ApiKey`, Key Vault). Legacy `MAXIO_*` env names are a read-only fallback when the `Maxio:` key is absent. Options object built **once** into a singleton → rotation requires restart (accepted; documented). |
| 3 | Total timeout budget | Caller-facing budget **25 s** per API request (< 30 s mandate), enforced by a linked `CancellationTokenSource` deadline passed to every SDK call: 20 s for the main flow + 5 s reserve for reconciling an ambiguous create. SDK per-attempt timeout 8 s, max 2 retries (idempotent GETs only). Deadline hit / `SdkTimeoutException` → `BillingProviderUnavailableException` → HTTP 504 "Maxio did not respond…". |
| 4 | Write-retry ownership | SDK resends only GET/HEAD/PUT/OPTIONS (default) — our writes are POST (`CreateCustomer`, `CreateSubscription`) → never resent by the SDK; the app never blindly re-POSTs either: it reconciles (row 11). |
| 5 | Idempotency & ambiguous writes | No real caller-supplied idempotency key exists on either create record (the injected `Idempotency-Key` header is not one). `CreateCustomer`: provider-enforced unique `reference` = `eshop:<username>`; ensure is read-first. `CreateSubscription`: unique `reference` = `eshop-sub-<guid>` minted per claim, reconciled via `FindSubscription`. |
| 6 | Observability | `ILogger`: Information = plan list/subscribe outcomes (enrollment reference, Maxio customer/subscription ids); Warning = ambiguous outcome, stale claim settled, provider 4xx; Error = provider 5xx/deserialization. SDK `Logging.LoggerFactory` set explicitly; `LogRequestBody`/`LogResponseBody` off (JSON bodies would log unredacted). No provider correlation id is documented in the map → we log our enrollment reference + HTTP status; `ApiException.Headers` not mined (UNVERIFIED which header Maxio uses). |
| 7 | Sensitive data | `CreateCustomer` carries email + names (PII). → `LogRequestBody` stays off, `LoggerFactory` assigned explicitly (env var cannot enable trace bodies), our logs never echo request bodies or emails. No card data in scope. |
| 8 | Environment selection | Only server group `Production` is touched (`Ebb`, `Oauth` unused). `Environment = ServerEnvironment.Us`; `Server.Production.Us.Site = Maxio:Subdomain`; if `Maxio:BaseUrl` set it replaces `Server.Production.Us.BaseUrl` verbatim (EU sites / mocks use this). The SDK has no "sandbox" environment — sandbox is a separate Maxio **site**; dev/test traffic is kept off live by the subdomain (the value of `Maxio:Subdomain`) living only in dev user-secrets, and offline tests point `BaseUrl` at a fake handler. |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS. |
| 10 | Partial results | See PAGED READS. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
|---|---|---|---|---|
| `CreateSubscription` (double-click / concurrent POST) | `SubscriptionEnrollments` table in `CatalogContext` (row per shopper, PK `BuyerId`, status `Pending`) | primary-key violation on insert of a 2nd row for the same `BuyerId` (SQL Server: `DbUpdateException`; in-memory provider: `ArgumentException` "same key" — found by live run) | `SubscriptionEnrollmentStore.TryClaimAsync` catch `DbUpdateException` / catch `ArgumentException` (only when the competing row exists) → `false` → service returns existing (same plan, completed) or 409 | `SubscriptionEnrollmentStore.TryClaimAsync` → `MaxioSubscriptionBillingService.CreateSubscriptionReconcilingAsync` |
| `CreateCustomer` (same shopper twice) | same enrollment claim (customer is only created while holding it) + Maxio's unique customer `reference` as second line | PK violation above; Maxio 422 on duplicate reference | as above; 422 caught in `EnsureCustomerAsync` → re-read by reference | `SubscriptionEnrollmentStore.TryClaimAsync` → `MaxioSubscriptionBillingService.EnsureCustomerAsync` |

**PAGED READS**

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
|---|---|---|---|
| `ListProductsForProductFamily` (plans) | `PerPage = 200`, at most 5 pages (1000 products) | `SubscriptionPlanCatalog.Truncated` → `ListSubscriptionPlansResponse.Truncated` (`true` when the 5th page was full) | `MaxioSubscriptionBillingService.FetchPlansAsync` |

**UNKNOWN OUTCOMES**

| Write | Re-read with | Search reference | Where in the code | Test that fails the connection |
|---|---|---|---|---|
| `CreateSubscription` | `Subscriptions.FindSubscription` | subscription `reference` (`eshop-sub-<guid>`) minted on the claim | catches (`SdkConnectionException`, budget `OperationCanceledException`, 5xx, unreadable 2xx) in `MaxioSubscriptionBillingService.CreateSubscriptionReconcilingAsync` → `ReconcileCreateAsync`; if still unknown the claim stays `Pending` and `SettleStaleClaimAsync` resolves it on the next POST after `StaleClaimAge` | `MaxioSubscriptionBillingServiceTests.Subscribe_WhenCreateTimesOut_ButMaxioCreatedIt_ReconcilesByReference` / `Subscribe_WhenCreateConnectionDrops_ButMaxioCreatedIt_ReconcilesByReference` / `Subscribe_WhenCreateConnectionFails_AndNothingIsFound_ReportsMaxioDidNotRespond_AndKeepsClaimPending` / `Subscribe_WithStalePendingClaim_WhoseCreateLanded_SettlesItInsteadOfCreatingAgain` |
| `CreateCustomer` | `Customers.ReadCustomerByReference` | customer `reference` (`eshop:<username>`) | catch in `MaxioSubscriptionBillingService.EnsureCustomerAsync` → `TryReadCustomerByReferenceAsync` | `MaxioSubscriptionBillingServiceTests.Subscribe_WhenCreateCustomerConnectionDrops_ButCustomerExists_ContinuesWithIt` |

## 6. Assumptions & Blockers

- **Assumption (YOUR CALL — not in the map):** one enrollment per shopper (tiers in one family are mutually exclusive). Same plan again → idempotent replay (200, existing). Different plan while one is live → 409 (plan change is out of scope). Canceled/expired subscription → claim released, may resubscribe.
- **Assumption:** shopper identity = JWT `ClaimTypes.Name` (eShop username = email). Maxio customer: `email` = username, `first_name` = email local-part, `last_name` = "Shopper"; `reference` = `eshop:<lower-case username>` (stable across in-memory restarts, unlike the Identity GUID).
- ~~UNVERIFIED: `payment_collection_method` left unset~~ → **settled by live sandbox traffic (2026-10-06):** with it unset Maxio answered 422 `"No payment method was on file for the $299.00 balance"` (automatic collection charges at signup). The create now sends `PaymentCollectionMethod = CollectionMethod.Remittance` (invoice billing; no card capture in scope) and succeeds (`state: active`). 422 `errors[]` are still surfaced verbatim as 422 and the claim released.
- **UNVERIFIED:** subscription `reference` uniqueness at Maxio is not stated; we mint a GUID per claim so it is unique on our side, and `FindSubscription` results are accepted only when `Customer.Id` matches.
- `ReadCustomerByReference` not-found is 404 (Case B) — confirmed by live sandbox traffic; `StatusCode == 404` → not found, anything else propagates.
- Migration `AddSubscriptionEnrollments` is scoped to the new table; the EF tooling also surfaced older, unrelated nullability drift on Orders/OrderItems/Catalog, deliberately left out of this change.
- **Next billing date** = `NextAssessmentAt ?? CurrentPeriodEndsAt` (field docs: next_assessment_at = when capture is next tried).
- Blockers: none.
