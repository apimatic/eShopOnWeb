# Maxio Advanced Billing plan — eShopOnWeb "Subscribe"

SDK root (plugin-relative): `sdk/dotnet/`. All `src/...` paths below are relative to it. Repo paths are relative to the project root.

## 1. Scope & sequence

| # | Step | Operations |
| --- | --- | --- |
| 1 | Bind and validate `Maxio:` settings (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl` optional); register one `MaxioAdvancedBillingClient` singleton in `src/PublicApi/Program.cs`. | none |
| 2 | `GET /api/subscription-plans`: list the plans of the configured product family. | `client.ProductFamilies.ListProductsForProductFamily` (paged by hand) |
| 3 | `POST /api/subscriptions`: validate the plan handle against step 2's set, claim customer, ensure customer, claim subscription, create subscription, record result. | `ListProductsForProductFamily`, `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Subscriptions.CreateSubscription`; settling: `Subscriptions.FindSubscription`, `Customers.ReadCustomerByReference` |
| 4 | `GET /api/my-subscriptions`: list the caller's subscriptions from the stored mapping and read them from Maxio. | `Customers.ListCustomerSubscriptions` (or `Subscriptions.ReadSubscription` per stored id) |
| 5 | EF Core claim entities in `CatalogContext` (existing store; in-memory provider in the run). | none |

Capability check: every needed operation exists in the map. No Blocker for capability. `ListCustomers` (`q`) is available but not needed; lookup is by reference. The `api-call` metered component is not subscribed to or priced by this scope (no `Components` are passed). Listing components is not requested.

## 2. CONTRACT SHEET

> Signatures are generated code, verbatim: each operation that takes input takes ONE request record as its first parameter (an operation with no inputs takes none), built with an object initializer whose property names are the record's own, never flat arguments.
> Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type, never from where a neighbouring type sits.

Common tail on every operation: `RequestOptions? requestOptions = null, CancellationToken cancellationToken = default`. Throw-only; no `…Result` variant. Not paginated by the SDK (no `Pageable`).

| Controller property | Signature | Request record + members | Body model + fields | Response envelope + fields read | Error case | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `client.ProductFamilies` | `Task<IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>> ListProductsForProductFamily(MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest request, ...)` | `ProductFamilyId: string, required` (doc: id OR `"handle:" + handle`, so pass `"handle:" + Maxio:ProductFamilyHandle`); `Page: int = 1` (min 1); `PerPage: int = 20` (max 200); `IncludeArchived: bool?`; others optional/left out (`DateField`, `Filter`, `Start/End Date(time)`, `Include`) | none | `ProductResponse.Product` (required, `product`) -> `MaxioAdvancedBilling.Models.Product`: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `ArchivedAt (archived_at): DateTimeOffset?`, `RequireCreditCard (require_credit_card): bool?`, `ProductFamily (product_family): ProductFamily?` (`Handle (handle): string?`) | A: `ApiException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`; `TryGetString(out string)` [404]; `TryGetRawError(out RawError)` [fallback] | manual: `Page`/`PerPage`; stop when a page returns fewer than `PerPage` | `map/operations/ProductFamilies.md`; `src/MaxioAdvancedBilling/Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs`; `Models/ProductResponse.cs`; `Models/Product.cs` |
| `client.Customers` | `Task<MaxioAdvancedBilling.Models.CustomerResponse> ReadCustomerByReference(MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest request, ...)` | `Reference: string, required` (wire `reference`) | none | `CustomerResponse.Customer` (required) -> `Customer`: `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?` | B: `ApiException<RawError>`; use `StatusCode` (404 expected when absent: UNVERIFIED, doc says "returns a single match") | none | `map/operations/Customers.md`; `Requests/Customers/ReadCustomerByReferenceRequest.cs`; `Models/Customer.cs` |
| `client.Customers` | `Task<MaxioAdvancedBilling.Models.CustomerResponse> CreateCustomer(MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest request, ...)` | `Body: CreateCustomerRequest?` (nothing marked required; set it, a null body sends nothing) | `CreateCustomerRequest.Customer (customer): CreateCustomer, required`; `CreateCustomer`: `FirstName (first_name): string, required`; `LastName (last_name): string, required`; `Email (email): string, required`; `Reference (reference): string?` (set it: the lookup key; doc calls it unique); `Organization`, address fields left out | `CustomerResponse.Customer.Id: int?` | A: `ApiException<MaxioAdvancedBilling.Errors.CreateCustomerError>`; `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422]; `TryGetRawError` [fallback] | none | `map/operations/Customers.md`; `Models/CreateCustomerRequest.cs`; `Models/CreateCustomer.cs` |
| `client.Subscriptions` | `Task<MaxioAdvancedBilling.Models.SubscriptionResponse> CreateSubscription(MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest request, ...)` | `Body: CreateSubscriptionRequest?` | `CreateSubscriptionRequest.Subscription (subscription): CreateSubscription, required`; `CreateSubscription` (nothing required): `ProductHandle (product_handle): string?` (set: the validated plan handle); `CustomerId (customer_id): int?` (set: from ensure-customer); `Reference (reference): string?` (set: deterministic key, see DUPLICATE CLAIMS); left out: `ProductId`, `ProductPricePointHandle/Id` (default price point used), `CouponCode(s)`, `PaymentProfileId`, `CreditCardAttributes`, `PaymentCollectionMethod`, `Components`, `CustomerAttributes`, `CustomerReference`, `InitialBillingAt`, `NextBillingAt`, `DeferSignup`. The endpoint doc ties acceptance to the product's payment options; task says plans do not require a payment method so none is sent (UNVERIFIED against the live site) | `SubscriptionResponse.Subscription (subscription): Subscription?` (nullable; guard null): `Id: int?`, `State (state): SubscriptionState?`, `ProductPriceInCents: long?`, `CurrentBillingAmountInCents: long?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `CurrentPeriodEndsAt: DateTimeOffset?`, `Reference: string?`, `Product (product): Product?` (`Handle`, `Name`, `PriceInCents`), `Customer (customer): Customer?` (`Id`, `Reference`) | A: `ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`; `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422]; `TryGetRawError` [fallback] | none | `map/operations/Subscriptions.md`; `Models/CreateSubscriptionRequest.cs`; `Models/CreateSubscription.cs`; `Models/Subscription.cs`; `Api/Subscriptions.cs` remarks |
| `client.Subscriptions` | `Task<SubscriptionResponse> FindSubscription(MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest request, ...)` | `Reference: string?` (wire `reference`; nothing marked required; set it) | none | `SubscriptionResponse.Subscription` | A: `ApiException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`; `TryGetNoContent(out RawError)` [404]; `TryGetRawError` [fallback] | none | `map/operations/Subscriptions.md` ; `src/MaxioAdvancedBilling/Requests/Subscriptions/FindSubscriptionRequest.cs` (only member: `Reference: string?`) |
| `client.Customers` | `Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptions(MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest request, ...)` | `CustomerId: int, required` | none | list of `SubscriptionResponse`; fields as above | B: `ApiException<RawError>` | none declared; result cap unknown (UNVERIFIED) | `map/operations/Customers.md`; `Requests/Customers/ListCustomerSubscriptionsRequest.cs` |
| `client.Subscriptions` | `Task<IReadOnlyList<SubscriptionResponse>> ListSubscriptions(MaxioAdvancedBilling.Requests.Subscriptions.ListSubscriptionsRequest request, ...)` | optional: `CustomerId: int?`, `Page: int = 1`, `PerPage: int = 20` (max 200); not needed unless `ListCustomerSubscriptions` proves insufficient | none | list of `SubscriptionResponse` | B | manual page/per_page | `map/operations/Subscriptions.md` |

Enum/type facts:

| Type | Values used | Source |
| --- | --- | --- |
| `MaxioAdvancedBilling.Models.Enums.SubscriptionState` (open enum; static members, not C# enum; use `TryGetKnownValue`/`Match`, `otherwise` arm for unknown) | `Pending`, `Trialing`, `Assessing`, `Active`, `SoftFailure`, `PastDue`, `Suspended`, `Canceled`, `Expired`, `Paused`, `Unpaid`, `TrialEnded`, `OnHold`, `AwaitingSignup`, `FailedToCreate` | `src/MaxioAdvancedBilling/Models/Enums/SubscriptionState.cs` |

Client facts:

| Fact | Value | Source |
| --- | --- | --- |
| Client | `MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)`; DI: `services.AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>)` registers a singleton, the options callback runs once when the singleton is first resolved | `sdk-map.md`; `src/MaxioAdvancedBilling/ServiceCollectionExtensions.cs` |
| Auth | `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }` | `sdk-map.md` Servers & auth |
| Environment | `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default) | `sdk-map.md` |
| Server group | all in-scope operations use group `Production` (`{site}.chargify.com`). Site: `options.Server.Production.Us.Site = Maxio:Subdomain`. Verbatim override: when `Maxio:BaseUrl` is non-blank set `options.Server.Production.Us.BaseUrl = Maxio:BaseUrl` (a template; with no `{site}` token it is used as written). `Ebb` group not touched | `src/MaxioAdvancedBilling/Servers/ProductionOptions.cs` |
| Retry default | `RetryOptions.Default()`: retries 408/429/500/502/503/504 on GET, HEAD, PUT, OPTIONS only; `MaxRetries 3`, `Delay 1s`, backoff x2, jitter 500ms, `Timeout 100s`. POST is not retried | `src/MaxioAdvancedBilling/Core/Configuration/RetryOptions.cs` |

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| A plan handle accepted by `POST /api/subscriptions` (`CreateSubscription.ProductHandle`) must be the `Product.Handle` of a non-archived product the plans listing (product family `Maxio:ProductFamilyHandle`) returned. A single-product lookup would only prove the product exists at Maxio, not that this family offers it. | `Subscriptions.CreateSubscription` <- `ProductFamilies.ListProductsForProductFamily` | In the subscribe service, before the customer claim and before `CreateSubscription`: it calls the same listing helper `GET /api/subscription-plans` uses (full walk of pages) and rejects with 400/422 when the handle is absent. |
| The customer id passed as `CreateSubscription.CustomerId` must be the id of the customer whose `Reference` is this user's stable key (the caller's identity from the token). | `Subscriptions.CreateSubscription` <- `Customers.CreateCustomer` / `Customers.ReadCustomerByReference` | In the subscribe service: the id is only taken from the response of those two calls (or from the stored claim row filled by them), never from the request body. |
| `/api/my-subscriptions` returns only subscriptions whose customer id equals the stored link for the token's user. | `Customers.ListCustomerSubscriptions` <- stored `MaxioCustomerLink` | In the my-subscriptions service: customer id is read from the store by caller identity, never from a query parameter. |

## 3. Trap notes

| Step | Hazard | Skill |
| --- | --- | --- |
| 1 | Options are captured once in a singleton; the credential object and its `Password` half are both needed; what blank values do. | MUST load maxio-advanced-billing:dotnet-authentication and maxio-advanced-billing:dotnet-client-initialization |
| 1 | The base-URL override sits on a per-environment, per-group server node, not on the client options root; picking the wrong node silently calls the default host. | MUST load maxio-advanced-billing:dotnet-configuration-resilience |
| 2 | The listing is a plain list read by page number; it carries no marker that more pages exist and a naive loop either stops early or never stops. | MUST load maxio-advanced-billing:dotnet-configuration-resilience |
| 3 | The three typed/untyped error shapes differ per operation (A vs B); the "not found" signal for the lookups is a different accessor or a status on each. | MUST load maxio-advanced-billing:dotnet-error-handling |
| 3 | Request records and models have `required`, nullable and open-enum members and `AdditionalProperties`; the response envelope wraps the payload one level down and `Subscription` is nullable. | MUST load maxio-advanced-billing:dotnet-models and maxio-advanced-billing:dotnet-calling-endpoints |
| 3 | A write that fails after the provider acted (timeout, dropped connection) is a distinct exception family from an error status; what each means for the claim. | MUST load maxio-advanced-billing:dotnet-error-handling |
| 5 | Tests need a stubbed HttpClient seam and constructed SDK exceptions. | MUST load maxio-advanced-billing:dotnet-testing |

## 4. REQUIRED READING

Load all before implementation:

- maxio-advanced-billing:dotnet-error-handling · every try/catch around SDK calls, claim release and settling
- maxio-advanced-billing:dotnet-authentication · credential wiring and fail-fast
- maxio-advanced-billing:dotnet-client-initialization · DI registration and lifetime
- maxio-advanced-billing:dotnet-configuration-resilience · retries, timeout budget, base URL node, paging, logging
- maxio-advanced-billing:dotnet-calling-endpoints · request records, cancellation
- maxio-advanced-billing:dotnet-models · model construction, nullability, open enums
- maxio-advanced-billing:dotnet-testing · stubbing the client

Hazard row (verbatim): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | Bind `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle` with `ValidateOnStart` (or an eager check in `Program.cs` before `Build()`); host refuses to start when any is null/whitespace. `BasicAuth.Password` is the constant `"x"` (a part the SDK documents), so it is not a setting; `Username` (the API key) is the checked part. `Maxio:BaseUrl` is optional, but if set it must parse as an absolute URI or startup fails. |
| 2 | Secret sourcing & rotation | `Maxio:ApiKey` comes from .NET user-secrets (`UserSecretsId` already in `src/PublicApi/PublicApi.csproj`; `dotnet user-secrets set "Maxio:ApiKey" ...`), never in repo or `appsettings*.json`. Options built once in the `AddMaxioAdvancedBillingClient` callback and captured by the singleton: rotation takes effect on process restart. No rotation without restart is required. |
| 3 | Total timeout budget | SDK `Retry.Timeout` default 100s is per attempt (confirm in `dotnet-configuration-resilience`). Set it explicitly so a read finishes within the budget; the handler also honors the request `CancellationToken` (`HttpContext.RequestAborted`) and applies a linked 30s overall `CancellationTokenSource` per endpoint call, which is the number the caller gets (30s total for subscribe incl. listing + customer + create; 15s for reads). |
| 4 | Write-retry ownership | `CreateCustomer` and `CreateSubscription` are POST: not in the SDK's default retried verbs (GET/HEAD/PUT/OPTIONS), so the SDK never resends them; the app owns resends and does none automatically; a client retry goes through the claim. Reads (list products, read by reference, find subscription, list subscriptions) may be retried by the SDK. |
| 5 | Idempotency & ambiguous writes | `CreateCustomer`: no idempotency key on its request record (members: `Body` only); reconciliation = `ReadCustomerByReference` with the stable reference. `CreateSubscription`: request record has `Body` only; no real key. The `Idempotency-Key` header the SDK injects is a fresh Guid per call and is NOT a key. The subscription `Reference` member is set to the deterministic key as a search handle for reconciliation, not as a dedupe guarantee (provider uniqueness of subscription references UNVERIFIED). Reconciliation = `FindSubscription` by that reference. |
| 6 | Observability | Information: claim won/lost, customer created vs found (user key hashed or user name only, no API key), subscription created (ids, handle, state). Warning: claim released after provider refusal, unknown outcome settled. Error: unexpected SDK exceptions. From provider error bodies, log HTTP status plus ids/messages from `ErrorListResponse1` / `CustomerErrorResponse1` / `RawError.ReadAsString()` (truncated) with the app request `TraceIdentifier`. A provider correlation id/header is not on the SDK error surface read (UNVERIFIED), so none is promised. |
| 7 | Sensitive data | Customer email/name and (later) card data are personal. Plans send no card data. `options.Logging.LogRequestBody` stays off; `options.Logging.LoggerFactory` assigned explicitly (the DI helper does this from `ILoggerFactory`; make sure `MAXIOADVANCEDBILLINGCLIENT_LOG` cannot force body logging in production, per `dotnet-configuration-resilience`). Never log the API key. |
| 8 | Environment selection | Server group `Production` only (`Ebb` not touched). Default `ServerEnvironment.Us` -> `https://{site}.chargify.com` with `Site = Maxio:Subdomain` (`cp-exp-4`). `Maxio:BaseUrl` overrides `options.Server.Production.Us.BaseUrl` verbatim. Sandbox only: the sandbox site and key come from user-secrets/config; no production site value anywhere in the repo. EU not used (SDK has no sandbox environment member; test traffic is kept off live by using the sandbox site's key and subdomain). |
| 9 | Duplicate prevention | See DUPLICATE CLAIMS. |
| 10 | Partial results | See PAGED READS. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

**DUPLICATE CLAIMS**

Store: EF Core `CatalogContext` (the store the codebase keeps its records in; in-memory provider in this run enforces primary keys, not unique indexes). Two new entities in `src/ApplicationCore/Entities/` with `IEntityTypeConfiguration` in `src/Infrastructure/Data/Config/` (picked up by `ApplyConfigurationsFromAssembly`): `MaxioCustomerLink` (PK `UserKey`; columns `MaxioCustomerId?`, `Status` Pending/Ready) and `MaxioSubscriptionLink` (composite PK `UserKey` + `PlanHandle`; columns `MaxioSubscriptionId?`, `Reference`, `Status` Pending/Active/Unknown). `UserKey` = the value of the token's `ClaimTypes.Name` claim (read: `IdentityTokenClaimService` issues only `Name` and `Role` claims; no `NameIdentifier`/`sub`), also used as the Maxio customer `Reference`. A migration is only needed for non-in-memory databases (existing `src/Infrastructure/Data/Migrations`); not part of this in-memory run.

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| `Customers.CreateCustomer` | `MaxioCustomerLink` row inserted (status Pending) and saved BEFORE `ReadCustomerByReference`/`CreateCustomer` | primary key on `UserKey`: second `SaveChanges` of the same key fails | the `catch (Exception ex) when (IsDuplicateClaim(ex))` (DbUpdateException, plus ArgumentException/InvalidOperationException, which the in-memory provider raises; confirmed live) around the claim save; loser re-reads the row: Ready -> reuse the stored id; Pending -> 409 `DuplicateException` (existing `ExceptionMiddleware` maps it to 409). Row removed when the provider refuses the create (422); marked unknown on ambiguous failure | `MaxioSubscriptionService.EnsureCustomerAsync`: `_db.MaxioCustomerLinks.Add` + `SaveChangesAsync`, then `Customers.ReadCustomerByReference` / `Customers.CreateCustomer` |
| `Subscriptions.CreateSubscription` | `MaxioSubscriptionLink` row (status Pending) saved BEFORE the SDK call | composite primary key (`UserKey`, `PlanHandle`) | same `catch` on the claim save; loser: Active -> return the stored subscription (read from Maxio by stored id), Pending -> 409; claim deleted when provider refuses (422); status Unknown on ambiguous failure | `MaxioSubscriptionService.EnsureSubscriptionAsync`: `_db.MaxioSubscriptionLinks.Add` + `SaveChangesAsync`, then `Subscriptions.CreateSubscription` |

The Maxio `Reference` for the subscription is `$"eshop:{UserKey}:{PlanHandle}"` style (derived, nothing hard-coded beyond the format); it sits beside the claim, never in place of it.

**PAGED READS**

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| Plans listing (`ListProductsForProductFamily`) | `PerPage` max 200 and an application page ceiling (e.g. 20 pages) to bound the loop | response DTO field `IsTruncated: bool` set true when the ceiling stops the walk with the last page full; the subscribe validation treats a truncated walk as a failure (503) rather than accepting an unverified handle | `MaxioSubscriptionService.ReadPlansAsync` sets `PlanList.IsTruncated` |
| `ListCustomerSubscriptions` | SDK declares no paging; any server-side cap is UNVERIFIED | not detectable from the return type; documented in the response DTO as `IsComplete` is not claimed. If the count equals a round server default (e.g. 20/50) fall back to `ListSubscriptions` with `CustomerId` and `Page`/`PerPage` and walk to a short page (response field `IsTruncated` same rule) | `MaxioSubscriptionService.ListMineCoreAsync` (uses `Subscriptions.ListSubscriptions` with `CustomerId` and `Page`/`PerPage` walk; sets `SubscriptionList.IsTruncated`) |

**UNKNOWN OUTCOMES**

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `CreateCustomer` | `Customers.ReadCustomerByReference` | customer `Reference` = `UserKey` | `EnsureCustomerAsync`: `catch (Exception ex) when (IsAmbiguousWrite(ex))` -> `TryFindCustomerAsync` | no dedicated test (the same path is tested for subscriptions) |
| `CreateSubscription` | `Subscriptions.FindSubscription` | subscription `Reference` (derived key above) | `EnsureSubscriptionAsync`: `catch (Exception ex) when (IsAmbiguousWrite(ex))` -> `TryFindSubscriptionAsync` | `MaxioSubscriptionServiceTests.Subscribe_ConnectionFailureOnCreate_IsSettledByReReadingTheReference` |

Settling happens in the failing write's own `catch` for `SdkConnectionException` / `SdkTimeoutException` (and `ResponseDeserializationException` on a 2xx): re-read once; if found -> mark Ready/Active and return it; if provably absent (404/NoContent) -> release the claim and report failure; if the re-read itself fails -> leave the row Unknown (a later request for the same key runs the same re-read before anything else).

## 6. Assumptions & Blockers

Blockers: none. Every needed capability exists in the map (product family products listing, customers create/read-by-reference/list-subscriptions, subscriptions create/find/list).

Assumptions (confirm when relevant):

1. Product-family handle goes into `ListProductsForProductFamilyRequest.ProductFamilyId` as `"handle:" + Maxio:ProductFamilyHandle` (documented on the member). No numeric id anywhere.
2. Default subscribe target `eshop-pro` conflicts with "nothing hard-coded". Proposal: the POST body's plan handle is optional and, when omitted, falls back to an optional extra config key `Maxio:DefaultPlanHandle` (non-secret, set to `eshop-pro` in `appsettings.json`). If absent and no handle given -> 400. This adds a fifth key beyond the four requested; say if you prefer a different mechanism. The fallback handle is also checked against the listing.
3. Caller identity = `ClaimTypes.Name` of the JWT (the only identity claim the existing token service issues). Endpoints use `[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]` like the existing admin endpoints, without the admin role.
4. `CreateCustomer` needs first name, last name and email; the token has only the user name. Email is taken from the Identity user (via `UserManager`, looked up by name) falling back to the user name when it is email-shaped; first/last name come from optional request-body fields, else derived from the email local part. Placeholder names are an application decision, not a Maxio fact.
5. Claim granularity: one subscription claim per (user, plan handle); a user may hold different plans concurrently. If one subscription per user overall is wanted, make `UserKey` the sole primary key.
6. "Next billing date" is read from `Subscription.NextAssessmentAt` (doc: when capture of payment will be tried), falling back to `CurrentPeriodEndsAt`. UNVERIFIED which the sandbox populates for a zero-payment-profile subscription.
7. UNVERIFIED (live only): a subscription is accepted with no payment profile for plans that do not require a card; Maxio rejects a duplicate customer `reference` (the doc calls it unique); `ReadCustomerByReference` returns 404 (raw error) when absent; `IncludeArchived` default excludes archived products (the app also filters on `Product.ArchivedAt`); subscription `Reference` uniqueness is not enforced by Maxio. All handled defensively as above.
8. State/price shown to the user come from the Maxio response (`State`, `Product.Handle/Name`, `ProductPriceInCents`/`CurrentBillingAmountInCents`, next date); amounts are cents and are formatted by the endpoint DTO.
9. The userId<->subscription link lives only in the in-memory EF store for the run; after a restart the link is lost (accepted, stated in the task). Maxio data (customer by reference) is recoverable by reference; `my-subscriptions` on a missing link first tries `ReadCustomerByReference(UserKey)` to rebuild the link.

## 7. Implementation notes (post-coding)

- Live sandbox showed `CreateSubscription` for the $299 plan returns 422 "No payment method was on file" with the default (automatic) collection; the request sets `PaymentCollectionMethod = CollectionMethod.Remittance` (a documented value on that member) so no card is needed. Assumption 7 (no payment profile accepted) is now verified for remittance.
- `Maxio:DefaultPlanHandle` (assumption 2) is set to `eshop-pro` in `src/PublicApi/appsettings.json`. `Maxio:Environment` (US/EU) and `Maxio:RequestBudgetSeconds` are optional extra keys.
