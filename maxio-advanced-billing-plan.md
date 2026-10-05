# Maxio Advanced Billing — subscription billing for eShopOnWeb (plan + contract sheet)

SDK source: `sdk/dotnet/` inside the **maxio** plugin (plugin-relative path; all `source` cells below are relative to that SDK root).
Map index: `sdk/dotnet/sdk-map.md`.

## 1. Scope & sequence

| # | Step | Operations used |
|---|------|-----------------|
| 0 | Toolchain: `global.json` roll-forward so the .NET 10 SDK builds the net8.0 solution | — |
| 1 | Reference the plugin SDK project from `src/Infrastructure` (MSBuild property for the path, overridable) | — |
| 2 | Settings `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` bound + validated on start; secrets loaded into PublicApi user-secrets from env vars | — |
| 3 | Client registration: named `HttpClient`, singleton `MaxioAdvancedBillingClient`, retry/timeout/logging options | — |
| 4 | Gateway `MaxioBillingGateway` (Infrastructure) behind a provider-agnostic `IBillingGateway` (ApplicationCore) + one error-translation ladder | all below |
| 5 | Plans: list the products of the configured family (hand-paged, capped) | `ProductFamilies.ListProductsForProductFamily` |
| 6 | Ensure customer (claim → lookup → create → record) | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer` |
| 7 | Subscribe (validate plan ∈ step 5 → claim → ensure customer → create → record; settle unknown outcomes) | `Subscriptions.CreateSubscription`, `Subscriptions.FindSubscription` |
| 8 | My subscriptions (settle pending claims, then read Maxio as system of record) | `Customers.ListCustomerSubscriptions`, `Customers.ReadCustomerByReference`, `Subscriptions.FindSubscription` |
| 9 | PublicApi endpoints `GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions` (JWT) + exception mapping | — |
| 10 | Offline tests: gateway via stub `HttpMessageHandler`; service via fakes; endpoints via `WebApplicationFactory` with a fake gateway | — |

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, verbatim. Each operation that takes input takes **ONE request record** as its first parameter (none here take zero inputs), built with an object initializer whose property names are the record's own — never flat arguments. Pass `cancellationToken:` by name (a `RequestOptions?` sits before it).
> ⚠ Every SDK type is written with the namespace its own source path implies (`Models/` → `MaxioAdvancedBilling.Models`, `Models/Enums/` → `.Models.Enums`, `Errors/` → `.Errors`, `Requests/<Controller>/` → `.Requests.<Controller>`, `Core/Exceptions/` → `.Core.Exceptions`, `Core/ErrorResponse/` → `.Core.ErrorResponse`) — taken from the path the map gives for THAT type, never from a neighbour.

| Controller · method | Request record (members) | Body model (fields: C# (wire): type, required?) | Response + inner fields read | Error case + accessors | Pagination | source |
|---|---|---|---|---|---|---|
| `client.ProductFamilies` · `ListProductsForProductFamily(ListProductsForProductFamilyRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest`: `ProductFamilyId: string, required` (doc: "id or its handle prefixed with `handle:`"); `Page: int = 1` (`[Minimum(1)]`); `PerPage: int = 20` (`[Maximum(200)]`, >200 clamped to 200); `IncludeArchived: bool?`; others (`DateField`, `Filter`, `StartDate`, `EndDate`, `StartDatetime`, `EndDatetime`, `Include`) left unset | none (GET) | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>`; `ProductResponse.Product: Product, required` → `Product.Id: int?`, `Name: string?`, `Handle: string?`, `Description: string?`, `PriceInCents: long?`, `Interval: int?`, `IntervalUnit: IntervalUnit?`, `ArchivedAt: DateTimeOffset?`, `RequireCreditCard: bool?` | **A** `ApiException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | none from SDK (no `Pageable`); hand-driven `Page`/`PerPage` | `map/operations/ProductFamilies.md`; `Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs`; `Models/ProductResponse.cs`; `Models/Product.cs`; `Errors/ListProductsForProductFamilyError.cs` |
| `client.Customers` · `ReadCustomerByReference(ReadCustomerByReferenceRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest`: `Reference: string, required` | none (GET `/customers/lookup.json?reference=`) | `MaxioAdvancedBilling.Models.CustomerResponse`: `Customer: Customer, required` → `Customer.Id: int?`, `Reference: string?`, `Email: string?` | **B** `ApiException<RawError>` (status 404 = no such reference — UNVERIFIED that a miss is 404; any other status is a failure, never an absence) | none | `map/operations/Customers.md`; `Requests/Customers/ReadCustomerByReferenceRequest.cs`; `Models/CustomerResponse.cs`; `Models/Customer.cs` |
| `client.Customers` · `CreateCustomer(CreateCustomerOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest`: `Body: CreateCustomerRequest?` (nothing marked required; the call is meaningless without `Body`) | `MaxioAdvancedBilling.Models.CreateCustomerRequest`: `Customer (customer): CreateCustomer, required` → `MaxioAdvancedBilling.Models.CreateCustomer`: `FirstName (first_name): string, required`, `LastName (last_name): string, required`, `Email (email): string, required`, `Reference (reference): string?` (remarks: must be unique per site — "you may only create one customer for a given reference value"). Left out: cc_emails, organization, address/address_2/city/state/zip/country (remarks: ISO formats if sent), phone, locale, vat_number, tax_exempt, surcharging, tax_exempt_reason, parent_id, salesforce_id, branding_theme_id | `CustomerResponse.Customer.Id: int?` | **A** `ApiException<MaxioAdvancedBilling.Errors.CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. `CustomerErrorResponse1.Errors (errors): MaxioAdvancedBilling.Models.AnyOf.Errors1?` → `TryGetListOfString(out IReadOnlyList<string>)` · `TryGetCustomerError(out CustomerError)` with `CustomerError.Customer (customer): string?` (`Models/AnyOf/Errors1.cs`, `Models/CustomerError.cs`) | none | `map/operations/Customers.md`; `Requests/Customers/CreateCustomerOperationRequest.cs`; `Models/CreateCustomerRequest.cs`; `Models/CreateCustomer.cs`; `Errors/CreateCustomerError.cs`; `Models/CustomerErrorResponse1.cs`; `Api/Customers.cs` (remarks) |
| `client.Customers` · `ListCustomerSubscriptions(ListCustomerSubscriptionsRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest`: `CustomerId: int, required` | none (GET) | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>`; `SubscriptionResponse.Subscription: Subscription?` (nullable — skip nulls) | **B** `ApiException<RawError>` | none (no page params on the record) | `map/operations/Customers.md`; `Requests/Customers/ListCustomerSubscriptionsRequest.cs`; `Models/SubscriptionResponse.cs` |
| `client.Subscriptions` · `CreateSubscription(CreateSubscriptionOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest`: `Body: CreateSubscriptionRequest?` | `MaxioAdvancedBilling.Models.CreateSubscriptionRequest`: `Subscription (subscription): CreateSubscription, required` → `MaxioAdvancedBilling.Models.CreateSubscription` (nothing required): set `ProductHandle (product_handle): string?` (remarks: product via `product_id` or `product_handle`), `CustomerId (customer_id): int?` (remarks: existing customer via `customer_id` or `customer_reference`), `Reference (reference): string?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` = `CollectionMethod.Remittance` (field doc: Relationship Invoicing accepts `remittance`, `automatic`, `prepaid`; legacy Statements accepts `invoice`, `automatic`). Added after the first live run: without it Maxio answered 422 "No payment method was on file for the $299.00 balance". Left out: product_id, price point fields, custom_price, coupons, payment profile / credit_card / bank_account attributes (plans need no payment method), components, metafields, customer_attributes, all date overrides, everything else | `SubscriptionResponse.Subscription: Subscription?` → read fields listed in the Subscription row below | **A** `ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. `ErrorListResponse1.Errors (errors): IReadOnlyList<string>, required` | none | `map/operations/Subscriptions.md`; `Requests/Subscriptions/CreateSubscriptionOperationRequest.cs`; `Models/CreateSubscriptionRequest.cs`; `Models/CreateSubscription.cs`; `Errors/CreateSubscriptionError.cs`; `Models/ErrorListResponse1.cs`; `Api/Subscriptions.cs` (remarks) |
| `client.Subscriptions` · `FindSubscription(FindSubscriptionRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest`: `Reference: string?` (not required; always set) | none (GET `/subscriptions/lookup.json?reference=`) | `SubscriptionResponse.Subscription: Subscription?` | **A** `ApiException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404 = not found] · `TryGetRawError(out RawError)` [fallback] | none | `map/operations/Subscriptions.md`; `Requests/Subscriptions/FindSubscriptionRequest.cs`; `Errors/FindSubscriptionError.cs` |

`MaxioAdvancedBilling.Models.Subscription` fields read (all nullable): `Id (id): int?`, `State (state): SubscriptionState?`, `Reference (reference): string?`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentBillingAmountInCents (current_billing_amount_in_cents): long?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `ActivatedAt (activated_at): DateTimeOffset?`, `CreatedAt (created_at): DateTimeOffset?`, `Currency (currency): string?`, `Customer (customer): Customer?`, `Product (product): Product?` (→ `Handle`, `Name`, `Interval`, `IntervalUnit`). Source: `Models/Subscription.cs`.

Enum tables needed:

| Enum (source) | Members (C# → wire) |
|---|---|
| `MaxioAdvancedBilling.Models.Enums.SubscriptionState` (`Models/Enums/SubscriptionState.cs`) | Pending→`pending`, FailedToCreate→`failed_to_create`, Trialing→`trialing`, Assessing→`assessing`, Active→`active`, SoftFailure→`soft_failure`, PastDue→`past_due`, Suspended→`suspended`, Canceled→`canceled`, Expired→`expired`, Paused→`paused`, Unpaid→`unpaid`, TrialEnded→`trial_ended`, OnHold→`on_hold`, AwaitingSignup→`awaiting_signup` (open enum — unknown values still arrive; read via `.Value`) |
| `MaxioAdvancedBilling.Models.Enums.IntervalUnit` (`Models/Enums/IntervalUnit.cs`) | Day→`day`, Month→`month` (open; read via `.Value`) |
| `MaxioAdvancedBilling.Models.Enums.CollectionMethod` (`Models/Enums/CollectionMethod.cs`) | Automatic→`automatic`, Remittance→`remittance`, Prepaid→`prepaid`, Invoice→`invoice` |

Client construction / auth / server facts:

| Fact | Value | source |
|---|---|---|
| Constructor | `new MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` (only ctor) | `sdk-map.md` § Getting a client |
| DI extension | `AddMaxioAdvancedBillingClient` registers a singleton over the **default unnamed** `HttpClient` — not used; we register over a named client ourselves | `ServiceCollectionExtensions.cs` |
| Auth | `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }`; provider note: username is the API key, password is `x`; Basic works only with US/EU environments. `BearerAuth` left unset (gateway only) | `sdk-map.md` § Servers & auth; `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Environment | `options.Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default; `MAXIO_ENVIRONMENT=US`) | `Servers/ServerEnvironment.cs` |
| Server group | all in-scope ops use `Production` (no Server-group bullets) — `https://{site}.chargify.com`; `{site}` ← `options.Server.Production.Us.Site` (default `"subdomain"`); full override `options.Server.Production.Us.BaseUrl` (a literal URL with no placeholders is used as-is) | `sdk-map.md` § Servers & auth; `Servers/ProductionOptions.cs` |
| Options surface | `Environment`, `Retry` (`RetryOptions`, all members required → `RetryOptions.Default() with {…}`), `Logging` (`LoggingOptions`), `TimeProvider`, `Server`, `StreamReadTimeout`, `Hooks`, `BasicAuth`, `BearerAuth` | `MaxioAdvancedBillingClientOptions.cs`, `sdk-map.md` |
| Throw-only | no `…Result` variants exist in this SDK | `sdk-map.md` § Error-handling model |

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
|---|---|---|
| The plan handle a caller POSTs must be a handle the app offers, i.e. one returned by listing the configured family's products (non-archived) | `CreateSubscription` (`ProductHandle`) ← `ListProductsForProductFamily` (`Product.Handle`, family = `Maxio:ProductFamilyHandle`) | `SubscriptionService.SubscribeAsync`, before the claim and before `CreateSubscription` (unknown handle → 400; truncated catalog that lacks the handle → 400 as well, never a blind create) |
| The `CustomerId` sent to `CreateSubscription` must be the id Maxio returned for **this** user's customer | `CreateSubscription` (`CustomerId`) ← `CreateCustomer` / `ReadCustomerByReference` (`Customer.Id`), stored on the user's `BillingCustomer` row | `SubscriptionService.EnsureCustomerAsync` (only source of the id passed to create) |
| `ListCustomerSubscriptions.CustomerId` must be the id stored for the calling user (never caller-supplied) | `ListCustomerSubscriptions` ← `CreateCustomer` / `ReadCustomerByReference` | `SubscriptionService.GetMySubscriptionsAsync` |
| The reference used to settle an unknown create must be the one stored on the claim before the create was sent | `FindSubscription` (`Reference`) ← `CreateSubscription` (`Reference`) | `SubscriptionService.SettleEnrollmentAsync` reads `SubscriptionEnrollment.MaxioReference` |

## 3. Trap notes (named hazards — resolved only by loading the skill)

| Step | Hazard | Consequence if ignored | Skill |
|---|---|---|---|
| 3 | `HttpClient` / SDK client lifetime and the default-unnamed-client blast radius of the stock DI extension | socket exhaustion or stale DNS, or our timeouts leaking onto every other unnamed `HttpClient` in the app | MUST load `maxio:dotnet-client-initialization` |
| 2/3 | A credential that is unset produces no exception — the call goes out unauthenticated | first symptom is a 401 in production instead of a refused boot | MUST load `maxio:dotnet-authentication` |
| 3 | What `Retry.Timeout` and `HttpClient.Timeout` actually bound, and which verbs the SDK resends | a hung Maxio holds a request far past the 30 s promise; a POST resend would double-create | MUST load `maxio:dotnet-configuration-resilience` |
| 5 | A hand-driven page loop stops only on provider cooperation | an unbounded loop, or a silently truncated plan list | MUST load `maxio:dotnet-configuration-resilience` |
| 4/7 | Which exception types actually reach a catch (typed vs raw vs connection vs timeout vs own cancellation) and the ordering of `TryGet…` accessors | errors escape the ladder as 500s that leak SDK messages; a 404 miss is confused with a failure | MUST load `maxio:dotnet-error-handling` |
| 6/7 | A transport failure on a POST leaves the outcome unknown | reporting "failed" for a subscription that exists; a retry then double-subscribes | MUST load `maxio:dotnet-configuration-resilience` |
| 4 | Request record vs body model vs nested resource; `requestOptions` sits before the token | CS1503 compile errors; wrong envelope unwrapping | MUST load `maxio:dotnet-calling-endpoints` |
| 4 | Open enums are records, not C# enums — string conversion of the value | the debug form of an enum ends up in API responses | MUST load `maxio:dotnet-models` |
| 3 | Built-in SDK logger and the `MAXIOADVANCEDBILLINGCLIENT_LOG` switch | customer PII (email/name) written to logs from outside the code | MUST load `maxio:dotnet-configuration-resilience` |
| 10 | Which seam to fake, and reading request bodies after the call | tests that hit the network, or `ObjectDisposedException` when asserting bodies | MUST load `maxio:dotnet-testing` |

## 4. REQUIRED READING (load ALL before implementation starts — this sheet deliberately does not carry their contents)

Use the copies shipped in **this** maxio plugin (plugin-qualified names):

- `maxio:dotnet-client-initialization` — step 3 (client + DI registration)
- `maxio:dotnet-authentication` — steps 2–3 (credentials, fail-fast)
- `maxio:dotnet-calling-endpoints` — steps 4–8 (request records, envelopes)
- `maxio:dotnet-models` — steps 4–8 (enums, nullable fields)
- `maxio:dotnet-error-handling` — steps 4–9 (the single translation ladder + HTTP mapping)
- `maxio:dotnet-configuration-resilience` — steps 3, 5, 6, 7 (retries, timeouts, total budget, paging bound, logging, unknown outcomes)
- `maxio:dotnet-testing` — step 10

Mandatory hazard row: a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
|---|---|---|
| 1 | Credential fail-fast | `MaxioSettings` bound from section `Maxio`; `IValidateOptions` + `ValidateOnStart()` in PublicApi refuses to start when `Maxio:ApiKey` or `Maxio:ProductFamilyHandle` is missing/blank, when `Maxio:Subdomain` is blank **and** `Maxio:BaseUrl` is unset, or when `Maxio:BaseUrl` is set but not an absolute http(s) URL. Basic password is the fixed provider value `x`, so only the username part is configurable — that part is checked. Message names the key, never the value. |
| 2 | Secret sourcing & rotation | Source: .NET user-secrets of PublicApi (dev, loaded from `MAXIO_*` env vars by the operator) or any `IConfiguration` source (env vars `Maxio__ApiKey` etc. in deployment). Options object is built **once** when the singleton client is first constructed; a rotated key takes effect on process restart only. Rotation without restart is not required by the task — accepted. |
| 3 | Total timeout budget | **25 s** total per API request for all Maxio work, enforced by one linked `CancellationTokenSource` (`RequestAborted` + 25 s) created in `SubscriptionService` and passed to every SDK call (`MaxioCallBudget`). Per attempt: `Retry.Timeout = 8 s`, `MaxRetries = 2`; `HttpClient.Timeout = 10 s` backstop. Budget expiry / SDK timeout / connection failure → HTTP 504 `"Maxio did not respond…"`. 25 s < the 30 s promise, leaving headroom for local DB work. |
| 4 | Write-retry ownership | Writes in scope are `CreateCustomer` and `CreateSubscription`, both **POST** → never resent by the SDK (default `HttpMethodsToRetry` kept unchanged: GET/HEAD/PUT/OPTIONS). No PUT in scope. Reads (GET) may be retried by the SDK within the budget. |
| 5 | Idempotency & ambiguous writes | `CreateCustomer`: no caller key on the record (only the injected `Idempotency-Key: Guid.NewGuid()` header, which is NOT a key). Real dedupe = customer `reference` = `eshop-user-{userId}`, unique per site per the CreateCustomer remarks; reconciliation via `ReadCustomerByReference`. `CreateSubscription`: no key on the record; we send `reference = eshop-sub-{guid}` generated once and stored on the claim **before** the call; reconciliation via `FindSubscription(reference)`. Whether Maxio enforces subscription-reference uniqueness is UNVERIFIED — the local claim is the guard, the reference is only for lookup. |
| 6 | Observability | SDK built-in logger on with the host `ILoggerFactory` assigned explicitly; request/response lines at Information (category raised to Warning in appsettings so only failures/retries show by default), headers off, bodies off. Our own logs: Information on subscribe success (userId, plan handle, Maxio subscription id, state); Warning on unknown outcome / settle; Error with HTTP status on provider failures. Correlation: Maxio error bodies in scope (`ErrorListResponse1`, `CustomerErrorResponse1`) carry no correlation id field, so we log the status + the API request's own correlation id (`BaseMessage.CorrelationId()`), which is also returned to the caller. |
| 7 | Sensitive data | `CreateCustomer` carries PII (email, first/last name). `LogRequestBody = false`, `LoggerFactory` assigned explicitly (disables the `MAXIOADVANCEDBILLINGCLIENT_LOG` env switch). No card data in scope (plans need no payment method; payment fields are left unset). Our logs never echo request bodies or emails. |
| 8 | Environment selection | `ServerEnvironment.Us`, server group `Production` only (Ebb/Oauth groups not touched). Dev/test: site from `Maxio:Subdomain` = the sandbox site (user-secrets). SDK declares no sandbox environment — test traffic stays off live sites because (a) the site is configuration, (b) automated tests never touch the network: gateway tests use a stub `HttpMessageHandler`, endpoint tests replace the gateway and set `Maxio:BaseUrl` to a non-routable loopback URL. `Maxio:BaseUrl` (when set) overrides `Server.Production.Us.BaseUrl` verbatim. |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS. Claims are primary-key inserts in `CatalogContext` (the store the app already uses; SQL Server in prod, in-memory in dev — both refuse a duplicate PK). |
| 10 | Partial results | Plan listing: `SubscriptionPlanCatalog.IsTruncated` (surfaced as `isTruncated` on `GET /api/subscription-plans`). |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
|---|---|---|---|---|
| `CreateCustomer` (one Maxio customer per eShop user) | `BillingCustomers` table (`BillingCustomer`, PK `UserId`) in `CatalogContext` | primary-key violation on insert of a second row for the same `UserId` | `EfSubscriptionStore.TryClaimCustomerAsync` (catches `DbUpdateException` / in-memory duplicate-key `ArgumentException`, returns `false`) | claim: `SubscriptionService.EnsureCustomerAsync` → `EfSubscriptionStore.TryClaimCustomerAsync`; SDK call: `SubscriptionService.CreateCustomerAsync` → `MaxioBillingGateway.CreateCustomerAsync` (`client.Customers.CreateCustomer`) |
| `CreateSubscription` (one subscription per user per plan) | `SubscriptionEnrollments` table (`SubscriptionEnrollment`, PK `Id = "{userId}:{planHandle}"`) in `CatalogContext` | primary-key violation on insert of a second row for the same user+plan; stale-claim takeover guarded by a concurrency token (`ClaimToken`) | `EfSubscriptionStore.TryClaimEnrollmentAsync` / `TryRenewEnrollmentClaimAsync` (catch → `false`) | claim: `SubscriptionService.ClaimEnrollmentAsync` → `EfSubscriptionStore.TryClaimEnrollmentAsync`; SDK call: `SubscriptionService.SubscribeAsync` → `MaxioBillingGateway.CreateSubscriptionAsync` (`client.Subscriptions.CreateSubscription`) |

Claim released (row deleted) when Maxio refuses the create (422), so the user can retry.

**PAGED READS**

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
|---|---|---|---|
| `ListProductsForProductFamily` | `PerPage = 200`, max 5 pages (1 000 plans), plus the 25 s budget | `SubscriptionPlanCatalog.IsTruncated` → `isTruncated` in the API response | `MaxioBillingGateway.ListPlansAsync` (sets `IsTruncated`) → `SubscriptionPlanListEndpoint.HandleAsync` (copies it to `ListSubscriptionPlansResponse.IsTruncated`) |

**UNKNOWN OUTCOMES**

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
|---|---|---|---|---|
| `CreateCustomer` | `ReadCustomerByReference` | customer `reference` = `eshop-user-{userId}` | outcome-unknown `catch (BillingProviderException ex)` in `SubscriptionService.CreateCustomerAsync` re-reads via `MaxioBillingGateway.FindCustomerByReferenceAsync`; if still unknown the claim stays pending and is settled by the lookup-first path of `SubscriptionService.EnsureCustomerAsync` (stale takeover) and by `SubscriptionService.GetMySubscriptionsAsync` | `SubscriptionServiceTests.SettlesUnknownCustomerCreateByReference`, `SubscriptionServiceTests.KeepsCustomerClaimWhenUnknownCustomerCreateCannotBeConfirmed`; gateway ladder: `MaxioBillingGatewayTests.ConnectionFailureOnCreateIsUnknownOutcomeAndNeverResent` |
| `CreateSubscription` | `FindSubscription` | subscription `reference` stored on the `SubscriptionEnrollment` claim | outcome-unknown `catch (BillingProviderException ex)` in `SubscriptionService.SubscribeAsync` → `SubscriptionService.SettleUnknownSubscriptionAsync` (re-read via `MaxioBillingGateway.FindSubscriptionByReferenceAsync`); if still unknown the claim stays `Pending` and is settled by `SubscriptionService.ClaimEnrollmentAsync` (stale takeover looks up the stored reference before any new create, and re-creates with the **same** reference only if absent) and by `SubscriptionService.GetMySubscriptionsAsync` | `MaxioBillingGatewayTests.ConnectionFailureOnCreateIsUnknownOutcomeAndNeverResent`, `MaxioBillingGatewayTests.ServerErrorOnCreateIsUnknownOutcomeAndNeverResent`, `SubscriptionServiceTests.SettlesUnknownCreateOutcomeByReference`, `SubscriptionServiceTests.KeepsClaimPendingWhenUnknownCreateCannotBeConfirmed`, `SubscriptionEndpointsTest.UnresponsiveMaxioReturnsGatewayTimeoutAndKeepsRequestPending` |

## 6. Assumptions & Blockers

Assumptions (minor — proceeding):
- `ListProductsForProductFamily` accepts `handle:<family-handle>` in the path; the SDK sends it escaped (`handle%3Aeshop-subscribe`). **Verified live 2026-10-06** on the sandbox site: 200 with both plans.
- `ReadCustomerByReference` signals "no such customer" with HTTP 404 (Case B gives only the status). **Verified live 2026-10-06** (404 on first lookup, then create → 201). Code treats only 404 as absence; every other status is a failure.
- Subscription `reference` uniqueness at Maxio remains UNVERIFIED; correctness does not depend on it (the local claim is the guard; the reference is only used for lookup).
- Sites on the legacy Statements architecture accept `invoice` rather than `remittance` (field doc). The sandbox site accepted `remittance` (live: 201, state `active`). A legacy site would answer 422, which reaches the caller as 422 with Maxio's message and releases the claim.
- The JWT carries only `ClaimTypes.Name` (read in `IdentityTokenClaimService`); the caller is resolved to an `ApplicationUser` via `UserManager.FindByNameAsync`, and the immutable `ApplicationUser.Id` keys the Maxio customer reference. With `UseOnlyInMemoryDatabase=true`, user ids are regenerated on every start, so each run maps a user to a fresh Maxio customer (task-acknowledged caveat).
- First/last name: `ApplicationUser` stores none; the POST body accepts optional `firstName`/`lastName`, defaulting to the email local-part / `"Customer"`.
- "One subscription per user per plan": a repeated POST for a plan the user already holds returns the existing subscription (200) instead of creating another.
- Using the plugin SDK by `ProjectReference` to its install location (path overridable via the `MaxioSdkProject` MSBuild property); Docker builds of PublicApi/Web would need the SDK in the build context.

Blockers: none.

## 7. Implementation record

- Live sandbox run (2026-10-06, site from `Maxio:Subdomain`): plans listed; POST `eshop-pro` → 201 `active`, $299.00/mo, next billing 2026-11-06; concurrent duplicate POST → 409 with a single create sent; repeat POST → 200 `alreadySubscribed`; `basic-plan` → 201; my-subscriptions lists both, read from Maxio.
- Unresponsive Maxio (black-hole `Maxio:BaseUrl`): GET plans and POST subscribe both return 504 "Maxio did not respond within 25 seconds." at ≈25 s.
- Blank `Maxio:ApiKey`: the host refuses to start with `OptionsValidationException` naming the key.
- EF migration `AddSubscriptionBilling` adds only the two claim tables (pre-existing, unrelated model drift on Orders/OrderItems/Catalog deliberately left out).
