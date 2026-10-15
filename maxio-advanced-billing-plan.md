# Maxio Advanced Billing — subscription billing for eShopOnWeb (plan + contract sheet)

SDK root (plugin-relative): `maxio-advanced-billing/sdk/dotnet/` — map `sdk-map.md`, pages `map/operations/*.md`.
All "source" cells below are relative to that SDK root.

## 1. Scope & sequence

| # | Step | Operations |
|---|------|-----------|
| 1 | Environment: `global.json` roll-forward; SDK `ProjectReference` from `src/Infrastructure`; user-secrets for `Maxio:*` on PublicApi | — |
| 2 | Settings + DI: `MaxioSettings` bound from `Maxio:` (ApiKey, Subdomain, ProductFamilyHandle, BaseUrl), fail-fast validation, singleton `MaxioAdvancedBillingClient` over a **named** `HttpClient` | client construction |
| 3 | Domain (ApplicationCore): `SubscriptionEnrollment` aggregate (claim row), `ISubscriptionBillingGateway`, `ISubscriptionEnrollmentStore`, `SubscriptionService` orchestration, billing exceptions | — |
| 4 | Infrastructure: `MaxioSubscriptionBillingGateway` (all SDK calls + one error ladder + budget), `EfSubscriptionEnrollmentStore` (claim = PK insert), EF config + SQL migration | all below |
| 5 | Plans: list offered plans of the configured family (cached), bounded page walk | `ProductFamilies.ListProductsForProductFamily` |
| 6 | Subscribe: claim → ensure customer → reconcile existing → create subscription → record | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Subscriptions.FindSubscription`, `Subscriptions.CreateSubscription` |
| 7 | My subscriptions: settle any unknown claim, then read customer's subscriptions from Maxio (system of record) | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions`, `Subscriptions.FindSubscription` |
| 8 | PublicApi endpoints `GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions` (JWT), exception mapping in `ExceptionMiddleware` | — |
| 9 | Tests (stubbed Maxio `HttpClient`), build, live sandbox verification | — |

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, verbatim — each operation that takes input takes ONE request record as its first parameter, built with an object initializer using the record's own property names; never flat arguments. Pass `cancellationToken:` by name.
> ⚠ Every SDK type is written fully-qualified with the namespace its source path implies — taken from the path the map gives for THAT type, never from a neighbouring type.

| Controller · method | Request record (members) | Body model (fields) | Response envelope → fields read | Error case · accessors | Pagination | Source |
|---|---|---|---|---|---|---|
| `client.ProductFamilies` · `ListProductsForProductFamily(ListProductsForProductFamilyRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest` — `ProductFamilyId: string, required` ("id or handle prefixed with `handle:`") · `Page: int = 1` (`[Minimum(1)]`) · `PerPage: int = 20` (`[Maximum(200)]`) · `IncludeArchived: bool?` (left unset) | — | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>` → `.Product` (`required Product`) → `Id: int?`, `Handle (handle): string?`, `Name (name): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `ArchivedAt (archived_at): DateTimeOffset?` | A · `ApiException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | none on map (no `Pageable`); `page`/`per_page` driven by caller | `map/operations/ProductFamilies.md`; `src/MaxioAdvancedBilling/Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs`; `src/MaxioAdvancedBilling/Models/Product.cs` |
| `client.Customers` · `ReadCustomerByReference(ReadCustomerByReferenceRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest` — `Reference: string, required` | — | `MaxioAdvancedBilling.Models.CustomerResponse` → `.Customer` (`required Customer`) → `Id (id): int?`, `Reference (reference): string?` | B · `ApiException<RawError>`; 404 = no customer for that reference | none | `map/operations/Customers.md`; `src/MaxioAdvancedBilling/Models/Customer.cs` |
| `client.Customers` · `CreateCustomer(CreateCustomerOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest` — `Body: CreateCustomerRequest?` (nothing required; we always set it) | `MaxioAdvancedBilling.Models.CreateCustomerRequest` — `Customer (customer): CreateCustomer, required` → `MaxioAdvancedBilling.Models.CreateCustomer` — `FirstName (first_name): string, required` · `LastName (last_name): string, required` · `Email (email): string, required` · `Reference (reference): string?` (**carried**: remarks — "you can only create one customer for a given reference value"). Left out: address/phone/locale/VAT/entity identifiers/tax fields (not needed). | `CustomerResponse` → `.Customer.Id: int?` | A · `ApiException<MaxioAdvancedBilling.Errors.CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] (`.Errors: MaxioAdvancedBilling.Models.AnyOf.Errors1?` → `TryGetCustomerError(out CustomerError)` (`.Customer: string?`) / `TryGetListOfString(out IReadOnlyList<string>)`) · `TryGetRawError(out RawError)` [fallback] | none | `map/operations/Customers.md`; `Api/Customers.cs` remarks; `Models/CreateCustomer.cs`; `Models/CustomerErrorResponse1.cs`; `Models/AnyOf/Errors1.cs`; `Models/CustomerError.cs` |
| `client.Customers` · `ListCustomerSubscriptions(ListCustomerSubscriptionsRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest` — `CustomerId: int, required` | — | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>` → `.Subscription: Subscription?` (fields as CreateSubscription row) | B · `ApiException<RawError>` | none (single response, "lists all subscriptions that belong to a customer") | `map/operations/Customers.md`; `Api/Customers.cs` remarks |
| `client.Subscriptions` · `FindSubscription(FindSubscriptionRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest` — `Reference: string?` (set always) | — | `SubscriptionResponse` → `.Subscription: Subscription?` | A · `ApiException<MaxioAdvancedBilling.Errors.FindSubscriptionError>` — `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback] | none | `map/operations/Subscriptions.md`; `Api/Subscriptions.cs` ("Finds a subscription by its reference") |
| `client.Subscriptions` · `CreateSubscription(CreateSubscriptionOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest` — `Body: CreateSubscriptionRequest?` | `MaxioAdvancedBilling.Models.CreateSubscriptionRequest` — `Subscription (subscription): CreateSubscription, required` → `MaxioAdvancedBilling.Models.CreateSubscription` — `ProductHandle (product_handle): string?` (**carried**: "Required, unless a product_id is given") · `CustomerId (customer_id): int?` (**carried**: "Required, unless customer_reference or customer_attributes") · `Reference (reference): string?` (**carried**: app reference for the subscription — reconciliation key) · `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` (**carried** after live traffic: with the default automatic collection the sandbox answered 422 "No payment method was on file for the $299.00 balance"; values `Automatic`/`Remittance`/`Prepaid`/`Invoice` per `Models/Enums/CollectionMethod.cs`; we send `Maxio:PaymentCollectionMethod`, default `remittance`). Left out: price point, coupons, payment profile/card/bank attributes (plans need no payment method), components, metafields, dates. | `SubscriptionResponse` → `.Subscription` → `Id (id): int?`, `State (state): SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `ActivatedAt (activated_at): DateTimeOffset?`, `Reference (reference): string?`, `Product (product): Product?` (`Handle`, `Name`, `Interval`, `IntervalUnit`) | A · `ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] (`.Errors: IReadOnlyList<string>, required`) · `TryGetRawError(out RawError)` [fallback] | none | `map/operations/Subscriptions.md`; `Api/Subscriptions.cs` remarks; `Models/CreateSubscription.cs`; `Models/Subscription.cs`; `Models/ErrorListResponse1.cs` |

Namespaces: `ApiException<T>`, `ResponseDeserializationException`, `SdkConnectionException`, `SdkTimeoutException`, `AuthSchemeException` → `MaxioAdvancedBilling.Core.Exceptions`; `RawError` → `MaxioAdvancedBilling.Core.ErrorResponse`; typed errors → `MaxioAdvancedBilling.Errors`; `RequestOptions` → `MaxioAdvancedBilling.Core`. Source: `sdk-map.md` Error-handling model.

**Enums needed**

| Enum (`MaxioAdvancedBilling.Models.Enums`) | Values (C# member → wire) | Source |
|---|---|---|
| `SubscriptionState` | `Pending`→pending, `FailedToCreate`→failed_to_create, `Trialing`, `Assessing`, `Active`, `SoftFailure`→soft_failure, `PastDue`→past_due, `Suspended`, `Canceled`, `Expired`, `Paused`, `Unpaid`, `TrialEnded`→trial_ended, `OnHold`→on_hold, `AwaitingSignup`→awaiting_signup (open enum — read `.Value`) | `src/MaxioAdvancedBilling/Models/Enums/SubscriptionState.cs` |
| `IntervalUnit` | `Day`→day, `Month`→month (open enum — read `.Value`) | `src/MaxioAdvancedBilling/Models/Enums/IntervalUnit.cs` |

**Client construction / auth / servers** (source: `sdk-map.md` Getting a client, Servers & auth; `src/MaxioAdvancedBilling/Servers/ProductionOptions.cs`)

- Only ctor: `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`.
- Auth: `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <api key>, Password = "x" }` (provider note: password is `x`).
- `options.Environment`: `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default, wire `US`) / `.Eu` (`EU`) — closed enum, no public factory.
- Server group in scope: **`Production`** only (all 6 ops; no Server-group bullet). `Us` template `https://{site}.chargify.com`, `Eu` `https://{site}.ebilling.maxio.com`; `{site}` default `"subdomain"` → set `options.Server.Production.Us.Site` / `.Eu.Site`; override `options.Server.Production.<Env>.BaseUrl`. `Ebb` group not touched.
- `options.Retry: RetryOptions` (`MaxioAdvancedBilling.Core.Configuration`, all members required → `RetryOptions.Default() with {…}`); `options.Logging: LoggingOptions` (same namespace).

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| The plan handle a caller POSTs must be one the configured family offers (non-archived) | `CreateSubscription` (`ProductHandle`) ← `ListProductsForProductFamily` | `SubscriptionService.SubscribeAsync` checks the requested handle against `ISubscriptionBillingGateway.ListPlansAsync` before the claim/create |
| The `CustomerId` sent to `CreateSubscription` must be the Maxio customer whose `Reference` is the caller's derived reference | `CreateSubscription` (`CustomerId`) ← `ReadCustomerByReference` / `CreateCustomer` | `MaxioSubscriptionBillingGateway.EnsureCustomerAsync` returns the id; only that value is passed to `CreateSubscriptionAsync` |
| Subscriptions shown as "mine" must belong to the caller's customer | `ListCustomerSubscriptions` (`CustomerId`) ← `ReadCustomerByReference` | `MaxioSubscriptionBillingGateway.ListSubscriptionsForCustomerAsync` resolves the id from the caller's reference only; never accepted from the HTTP request |

## 3. Trap notes

- Step 2 — a missing/blank credential does not throw; the call simply goes out unauthenticated and the failure shows up far from its cause. **MUST load `maxio-advanced-billing:dotnet-authentication`**
- Step 2 — the DI extension's lifetime and which `HttpClient` it resolves decide DNS freshness and what other consumers share. **MUST load `maxio-advanced-billing:dotnet-client-initialization`**
- Step 2 — `Environment` vs `Server` options are read at different times; setting `BaseUrl` on the wrong environment is silently ignored. **MUST load `maxio-advanced-billing:dotnet-configuration-resilience`**
- Step 2 — what `Retry.Timeout` and `HttpClient.Timeout` actually bound, and which verbs are resent. **MUST load `maxio-advanced-billing:dotnet-configuration-resilience`**
- Step 2/7 — leaving `Logging.LoggerFactory` unset arms an environment variable that can dump bodies (customer PII). **MUST load `maxio-advanced-billing:dotnet-configuration-resilience`**
- Step 5 — a hand-driven page loop with no provider-independent bound, and a cap that silently truncates. **MUST load `maxio-advanced-billing:dotnet-configuration-resilience`**
- Step 4–7 — Case A vs Case B per operation; wrong typed `catch` compiles and never matches; `TryGetRawError` ordering. **MUST load `maxio-advanced-billing:dotnet-error-handling`**
- Step 6 — a transport failure on `CreateCustomer`/`CreateSubscription` is not "it failed". **MUST load `maxio-advanced-billing:dotnet-configuration-resilience`**
- Step 6/7 — a 404 vs an unreadable body on a lookup that gates a create. **MUST load `maxio-advanced-billing:dotnet-error-handling`**
- Step 5–7 — open enums print their debug form under interpolation; union body on the 422 customer error. **MUST load `maxio-advanced-billing:dotnet-models`**
- Step 4 — positional `CancellationToken` vs `RequestOptions?`; envelope unwrapping. **MUST load `maxio-advanced-billing:dotnet-calling-endpoints`**
- Step 9 — request bodies are disposed after the attempt; `ApiException<T>` cannot be constructed directly. **MUST load `maxio-advanced-billing:dotnet-testing`**

## 4. REQUIRED READING (load before implementation — all loaded)

- `maxio-advanced-billing:dotnet-getting-started` — map, namespaces, idempotency, sensitive data
- `maxio-advanced-billing:dotnet-client-initialization` — step 2 (client + DI)
- `maxio-advanced-billing:dotnet-authentication` — step 2 (Basic auth, fail-fast)
- `maxio-advanced-billing:dotnet-configuration-resilience` — steps 2, 5, 6 (servers, retries, budget, paging, unknown outcomes, logging)
- `maxio-advanced-billing:dotnet-calling-endpoints` — steps 4–7
- `maxio-advanced-billing:dotnet-models` — steps 5–7
- `maxio-advanced-billing:dotnet-error-handling` — steps 4–8 (error ladder)
- `maxio-advanced-billing:dotnet-testing` — step 9
- Hazard (verbatim): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
|---|---|---|
| 1 | Credential fail-fast | `MaxioSettings` bound from `Maxio:` via `AddOptions<MaxioSettings>().Bind(...).Validate(...).ValidateOnStart()`; `ApiKey` and `ProductFamilyHandle` non-blank; `Subdomain` non-blank unless `BaseUrl` set; `BaseUrl` (if set) an absolute http(s) URI. Password is the fixed provider value `x`, so both Basic parts are always non-blank. Failure message names the key, never the value. Only PublicApi registers Maxio. |
| 2 | Secret sourcing & rotation | `Maxio:ApiKey` from .NET user-secrets (dev) / env var `Maxio__ApiKey` or a secret store (deployed). Options built **once** inside the singleton factory → rotation needs a process restart (accepted; documented). |
| 3 | Total timeout budget | Per attempt `Retry.Timeout = 10s`, `HttpClient.Timeout = 15s` backstop; each gateway call wrapped in a linked CTS with `Maxio:RequestTimeoutSeconds` (default 20s) total; the subscribe endpoint as a whole is bounded by the sum of its ≤6 calls but each call is individually ≤20s (reads: up to 3 attempts × 10s capped by the 20s budget). |
| 4 | Write-retry ownership | SDK default `HttpMethodsToRetry` (GET/HEAD/PUT/OPTIONS) kept, `MaxRetries = 2`: reads may be resent; `CreateCustomer`/`CreateSubscription` are POST → never resent by the SDK. No PUT in scope. |
| 5 | Idempotency & ambiguous writes | No request record in scope carries a key member. `CreateCustomer`: `reference` = deterministic hashed user reference; provider documents one customer per reference → 422 on a duplicate → re-read by reference. `CreateSubscription`: `reference` = deterministic `{customerRef}-{productFamilyHandle}`; provider uniqueness **UNVERIFIED** → reconciliation by `FindSubscription(reference)` + local claim. The injected `Idempotency-Key` header is not relied on. |
| 6 | Observability | Info: plan cache refresh, customer created (Maxio id), subscription created (Maxio id, state). Warning: unknown outcome, reconcile result, 422 rejections (provider messages). Error: provider 5xx/transport/auth failures with status + SDK call message. Correlation: our `BaseMessage.CorrelationId()` from the endpoint is passed into the service and logged in scope; Maxio error bodies in scope (`ErrorListResponse1`, `CustomerErrorResponse1`) carry no correlation id — none to propagate. SDK logger gets the host `ILoggerFactory`. |
| 7 | Sensitive data | Yes — `CreateCustomer` body carries email/name (PII). `LogRequestBody = false`, `LoggerFactory` set explicitly (host factory), no headers logged; customer reference is a SHA-256-derived token (no email in URLs/logs); our own logs never echo emails or request bodies. |
| 8 | Environment selection | Server group `Production` only. `Maxio:Environment` (optional, `US` default / `EU`) selects `ServerEnvironment`; `Maxio:Subdomain` → `Server.Production.<Env>.Site`; `Maxio:BaseUrl` (optional) → `Server.Production.<Env>.BaseUrl` verbatim. No SDK-level sandbox environment exists: test traffic is isolated by pointing `Subdomain` at the sandbox site (the sandbox subdomain lives only in dev user-secrets); production sets its own site via its secret store. |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS. |
| 10 | Partial results | See PAGED READS. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| `CreateSubscription` (one subscription per user per product family) | `SubscriptionEnrollments` table in `CatalogContext` — row PK `EnrollmentKey` = `{productFamilyHandle}:{customerReference}`, state `Pending` | primary-key violation on `SaveChangesAsync` (enforced by SQL Server and by the EF in-memory store) | `EfSubscriptionEnrollmentStore.TryClaimAsync` `catch (Exception ex) when (IsDuplicateKey(ex))` — `DbUpdateException` (SQL Server) or the in-memory provider's `ArgumentException` "same key" (verified by `ClaimAndUpdate.SecondClaimForSameKey_FromAnotherContext_IsRefused`) → returns `false`; `SubscriptionService.ResolveExistingClaimAsync` then returns the existing subscription, 409 while a fresh claim is pending, or settles an unknown one | `EfSubscriptionEnrollmentStore.TryClaimAsync` → `MaxioSubscriptionBillingGateway.CreateSubscriptionAsync` |
| `CreateCustomer` | same claim row (customer ensure happens only while holding the enrollment claim); Maxio's one-customer-per-reference rule is a second line | same PK violation; provider 422 on duplicate reference → re-read | same catch; provider 422 caught in `MaxioSubscriptionBillingGateway.CreateCustomerAsync` (`catch (ApiException<CreateCustomerError>)` → `FindCustomerIdAsync`) | `EfSubscriptionEnrollmentStore.TryClaimAsync` → `MaxioSubscriptionBillingGateway.EnsureCustomerAsync` |

Claim release: on a definite provider refusal (4xx/5xx with a body) the claim row is deleted (`ReleaseAsync`); a `Pending` claim older than `Maxio:ClaimLeaseSeconds` (default 120s) is settled by re-reading Maxio before anyone may retake it.

**PAGED READS**

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| `ListProductsForProductFamily` (`PerPage = 200`, walk `Page` until a short page) | `MaxPlanPages = 5` page cap + per-call budget | `PlanCatalog.IsComplete == false` → surfaced as `isComplete: false` on `GET /api/subscription-plans`; a POST whose plan is absent from an incomplete catalog fails with `BillingUnavailableException` (503) rather than "plan not offered" | `MaxioSubscriptionBillingGateway.ListPlansAsync` (sets `PlanCatalog.IsComplete`) |

**UNKNOWN OUTCOMES**

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `CreateCustomer` | `Customers.ReadCustomerByReference` | `Reference` = derived customer reference | `MaxioSubscriptionBillingGateway.CreateCustomerAsync` `catch (SdkConnectionException)` / `catch (TimeoutException)` (budget) / 2xx `ResponseDeserializationException` → `SettleCustomerAsync` (fresh-token `FindCustomerIdAsync`); still unknown → `BillingOutcomeUnknownException` → `SubscriptionService.EnrollAsync` marks the claim `OutcomeUnknown`; the next attempt's `EnsureCustomerAsync` is lookup-first | `MaxioSubscriptionBillingGatewayTests.CreateCustomer_ConnectionDropped_SettlesByReadingCustomerByReference`; `…CreateCustomer_ConnectionDropped_AndNotFound_IsOutcomeUnknown` |
| `CreateSubscription` | `Subscriptions.FindSubscription` | `Reference` = `{customerRef}-{productFamilyHandle}` | `MaxioSubscriptionBillingGateway.CreateSubscriptionAsync` same three catches → `SettleSubscriptionAsync` (fresh-token `FindSubscriptionByReferenceAsync`); not settled → `SubscriptionService.EnrollAsync` marks `OutcomeUnknown`, settled by `SubscriptionService.SettleAsync` on the next POST (`ResolveExistingClaimAsync`) or GET `my-subscriptions` (`GetSubscriptionsAsync`) | `MaxioSubscriptionBillingGatewayTests.CreateSubscription_ConnectionDropped_SettlesByFindSubscription`; `…CreateSubscription_ConnectionDropped_AndNotFound_IsOutcomeUnknown`; `SubscriptionServiceTests.UnknownClaim_IsSettledOnNextRead` |

## 6. Assumptions & Blockers

- **Assumption (YOUR CALL — not in the map):** one subscription per user per configured product family; a second POST for the same plan returns the existing subscription (idempotent 200), for a different plan → 409 (plan change out of scope). Re-subscribing after cancellation is out of scope (same deterministic subscription reference).
- **Assumption:** customer reference = `eshop-` + first 32 hex chars of SHA-256(normalized user name). The JWT carries only `ClaimTypes.Name`; user name is unique in Identity and stable across in-memory restarts. Customer `first_name` = e-mail local part, `last_name` = `"eShopOnWeb"` (Identity has no name fields; Maxio requires both).
- **Assumption:** "next billing date" = `next_assessment_at`, falling back to `current_period_ends_at` (per field docs in `Models/Subscription.cs`).
- **UNVERIFIED:** whether Maxio rejects a duplicate subscription `reference`; code does not depend on it (local claim + `FindSubscription` before create).
- **UNVERIFIED:** `ListProductsForProductFamily` honours `page`/`per_page` (record documents them; map lists no pagination) — loop stops on a short page and is capped.
- Blockers: none. The plugin exposes every operation the scope needs.
- **Found in live traffic (YOUR CALL — not in the map):** the seeded plans need no payment method at signup, but under the default `automatic` collection the sandbox refused the create with 422 "No payment method was on file for the $299.00 balance". eShopOnWeb captures no card, so subscriptions are created with `payment_collection_method = remittance` (customer is invoiced); configurable via `Maxio:PaymentCollectionMethod` (`invoice` for legacy Statements sites, `automatic` once payment profiles are collected).
- **Added config keys (all optional, defaults in `MaxioSettings`):** `Maxio:Environment` (US/EU), `Maxio:DefaultPlanHandle`, `Maxio:PaymentCollectionMethod`, `Maxio:RequestTimeoutSeconds`, `Maxio:AttemptTimeoutSeconds`, `Maxio:PlanCacheSeconds`.

## 8. Verification (2026-10-15, sandbox site from user-secrets, PublicApi on Kestrel :39083)

- Missing `Maxio:ApiKey` → host refuses to start: `OptionsValidationException: Maxio:ApiKey is not configured.`
- `GET /api/subscription-plans` → 200, both seeded plans with prices from Maxio.
- `POST /api/subscriptions` (default plan) → 201 `created`, state `active`, $299.00, next billing one month out; repeat → 200 `already_subscribed` (same id); other plan → 409; unknown plan → 400.
- 5 parallel POSTs for a second user → one 201 + four 409; exactly one `POST /subscriptions.json` reached Maxio.
- Restart (in-memory claims lost) → `GET /api/my-subscriptions` still lists the subscription (read from Maxio); repeat POST → 200 `already_subscribed` with zero POSTs to Maxio (reconciled by `FindSubscription`).
- Log scan: no e-mail addresses, no API key; lookup references masked (`reference=***`).
- Tests: UnitTests 74/74, IntegrationTests 6/6, FunctionalTests 12/12. PublicApiIntegrationTests: 6 pass / 15 fail. The 12 pre-existing failures fail identically on untouched HEAD, and 3 new ones fail the same way: ASP.NET Core 10 runtime (roll-forward) + net8 `TestHost` → "ResponseBodyPipeWriter does not implement PipeWriter.UnflushedBytes" on any JSON response.

## 7. Source labels

Rows cite map pages / map-named files relative to the SDK root; `UNVERIFIED` and `YOUR CALL — not in the map` used as marked above.
