# Maxio Advanced Billing subscriptions in eShopOnWeb — plan and contract sheet

SDK root (plugin-relative): `sdk/dotnet/`. All source labels below are relative to it. Project root: `C:\claude-runs\t1basehamza-t1base-plugin-control-sonnet55medium-004\repo`.

Repo survey (read only; imitate these):

| Pattern | Exemplar |
| --- | --- |
| MinimalApi.Endpoint endpoint class (`IEndpoint<IResult, ...>`, `AddRoute`, `HandleAsync`, `.Produces`, `.WithTags`) | `src/PublicApi/CatalogBrandEndpoints/CatalogBrandListEndpoint.cs` |
| JWT-protected route (`[Authorize(..., AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]` on the lambda) | `src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.cs` |
| Request/response DTO file naming (`X.Request.cs`, `X.Response.cs`) | `src/PublicApi/CatalogItemEndpoints/` and `src/PublicApi/CatalogBrandEndpoints/CatalogBrandListEndpoint.ListCatalogBrandsResponse.cs` |
| Error to HTTP mapping (`DuplicateException` -> 409, everything else 500) | `src/PublicApi/Middleware/ExceptionMiddleware.cs` |
| EF entity configuration (`IEntityTypeConfiguration<T>`, picked up by `ApplyConfigurationsFromAssembly`) | `src/Infrastructure/Data/Config/BasketConfiguration.cs` |
| The app's one EF store for app records (DbContext) | `src/Infrastructure/Data/CatalogContext.cs`; in-memory registration in `src/Infrastructure/Dependencies.cs` |
| Config registration and `Program.cs` composition root (already calls `AddEnvironmentVariables()`; PublicApi has a `UserSecretsId`) | `src/PublicApi/Program.cs`, `src/PublicApi/PublicApi.csproj` |

Facts read in the app: the JWT minted by `src/Infrastructure/Identity/IdentityTokenClaimService.cs` carries only `ClaimTypes.Name` (the username, the user's e-mail in the seeded accounts) plus role claims. There is no `NameIdentifier`/`sub` claim. The caller identity therefore comes from `ClaimTypes.Name` on `HttpContext.User`.

## 1. Scope & sequence

| # | Step | Operations used |
| --- | --- | --- |
| 1 | Config: bind `Maxio` section (`Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` optional); validate at startup; register the SDK client once as a singleton. | none |
| 2 | Persistence: two entities on the existing `CatalogContext` (DbSets + `IEntityTypeConfiguration`): customer link (PK = user id) and subscription link (PK = user id + plan handle). No migration is needed for the in-memory store. | none |
| 3 | `GET /api/subscription-plans`: list the plans of the configured family. | `client.ProductFamilies.ListProductsForProductFamily` |
| 4 | `POST /api/subscriptions` (body: plan handle): validate the handle against step 3's list; claim the customer link; ensure the Maxio customer (adopt-by-reference or create); claim the subscription link; create the subscription; record ids and the confirmed state. | `ProductFamilies.ListProductsForProductFamily`, `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Subscriptions.FindSubscription`, `Subscriptions.CreateSubscription` |
| 5 | `GET /api/my-subscriptions`: from the caller's customer link, list the customer's subscriptions with plan, price, state and next billing date. | `Customers.ListCustomerSubscriptions` |
| 6 | Tests (stubbed `HttpClient`) and `dotnet user-secrets` population from `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_DEFAULT_PRODUCT_FAMILY` into the PublicApi project (never in the repo). | none |

The metered component `api-call` is out of scope (the request asks only for subscribe, list and confirm). No component operation is used.

## 2. CONTRACT SHEET

> Signatures below are generated code, verbatim. Each operation that takes input takes ONE request record as its first parameter (an operation with no inputs takes none), built with an object initializer whose property names are the record's own, never flat arguments.
>
> Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type, never from where a neighbouring type sits.

All operations: `RequestOptions? requestOptions = null, CancellationToken cancellationToken = default` follow the request record. Auth: `options.BasicAuth` for all. Server group `Production` for all (default). Pagination: none declared by the map for any operation here (default); `ListProductsForProductFamily` nevertheless has `Page`/`PerPage` query members (see its row).

| Controller property | Signature | Request record + members | Body model + fields | Response envelope + fields read | Error case, accessors, payload | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `client.ProductFamilies` | `Task<IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>> ListProductsForProductFamily(MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest request, ...)` | `ProductFamilyId: string, required` (doc: the id, or the handle prefixed with `handle:`); `Page: int = 1`; `PerPage: int = 20` (max 200); `IncludeArchived: bool?` (leave unset; archived plans must not be offered). Left out: date, filter and include members. | none | `ProductResponse.Product` (`product`, required) -> `MaxioAdvancedBilling.Models.Product`: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): MaxioAdvancedBilling.Models.Enums.IntervalUnit?`, `RequireCreditCard (require_credit_card): bool?`, `ArchivedAt (archived_at): DateTimeOffset?`, `TrialPriceInCents`, `InitialChargeInCents` | Case A: `ApiException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`; `TryGetString(out string? value)` [404]; `TryGetRawError(out RawError? error)` [fallback] | Not paginated per the map (returns a list); the caller drives `Page`/`PerPage` and stops on a page shorter than `PerPage` | `map/operations/ProductFamilies.md`; `Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs`; `Models/ProductResponse.cs`, `Models/Product.cs` |
| `client.Customers` | `Task<MaxioAdvancedBilling.Models.CustomerResponse> ReadCustomerByReference(MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest request, ...)` | `Reference: string, required` (query `reference`) | none | `CustomerResponse.Customer` (`customer`, required) -> `MaxioAdvancedBilling.Models.Customer`: `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?` | Case B: `ApiException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. Not-found shows as `StatusCode` (value UNVERIFIED) | none | `map/operations/Customers.md`; `Requests/Customers/ReadCustomerByReferenceRequest.cs`; `Models/CustomerResponse.cs`, `Models/Customer.cs`. Not-found status: UNVERIFIED |
| `client.Customers` | `Task<MaxioAdvancedBilling.Models.CustomerResponse> CreateCustomer(MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest request, ...)` | `Body: MaxioAdvancedBilling.Models.CreateCustomerRequest?` (nothing marked required on the record) | `CreateCustomerRequest`: `Customer (customer): MaxioAdvancedBilling.Models.CreateCustomer, required`. `CreateCustomer`: `FirstName (first_name): string, required`; `LastName (last_name): string, required`; `Email (email): string, required`; `Reference (reference): string?` (carry it: it is the lookup key for reconciliation). Others optional, left out (address, vat, tax, parent id, etc.) | `CustomerResponse.Customer.Id (id): int?` | Case A: `ApiException<MaxioAdvancedBilling.Errors.CreateCustomerError>`; `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1? value)` [422]; `TryGetRawError` [fallback] | none | `map/operations/Customers.md`; `Requests/Customers/CreateCustomerOperationRequest.cs`; `Models/CreateCustomerRequest.cs`, `Models/CreateCustomer.cs`. No idempotency key member exists on the record. |
| `client.Subscriptions` | `Task<MaxioAdvancedBilling.Models.SubscriptionResponse> CreateSubscription(MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest request, ...)` | `Body: MaxioAdvancedBilling.Models.CreateSubscriptionRequest?` (nothing marked required) | `CreateSubscriptionRequest`: `Subscription (subscription): MaxioAdvancedBilling.Models.CreateSubscription, required`. `CreateSubscription` (nothing required). Carry per the method `<remarks>`: `ProductHandle (product_handle): string?` (specifies the product), `CustomerId (customer_id): int?` (identifies the existing customer), `Reference (reference): string?` (our deterministic lookup key). The remarks tie acceptance to payment information "depending on the options for the Product": carry no payment members (the plans need none); `PaymentProfileId` and `*_attributes` left out. Left out: price point, coupons, components, dates, custom price | `SubscriptionResponse.Subscription (subscription): MaxioAdvancedBilling.Models.Subscription?` (nullable). `Subscription`: `Id (id): int?`, `State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `Reference (reference): string?`, `Product (product): MaxioAdvancedBilling.Models.Product?`, `Customer (customer): MaxioAdvancedBilling.Models.Customer?` | Case A: `ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`; `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1? value)` [422]; `TryGetRawError` [fallback]. A 422 can also carry a 3DS `action_link` per the remarks (not expected: no payment) | none | `map/operations/Subscriptions.md`; `Api/Subscriptions.cs` (`CreateSubscription` remarks); `Models/CreateSubscription.cs`, `Models/Subscription.cs`, `Models/SubscriptionResponse.cs`. No idempotency key member on the record. |
| `client.Subscriptions` | `Task<MaxioAdvancedBilling.Models.SubscriptionResponse> FindSubscription(MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest request, ...)` | `Reference: string?` (query `reference`; optional on the record, always set it) | none | as above (`Subscription` nullable) | Case A: `ApiException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`; `TryGetNoContent(out RawError? value)` [404]; `TryGetRawError` [fallback] | none | `map/operations/Subscriptions.md`; `Requests/Subscriptions/FindSubscriptionRequest.cs` |
| `client.Customers` | `Task<IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>> ListCustomerSubscriptions(MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest request, ...)` | `CustomerId: int, required` | none | each item `SubscriptionResponse.Subscription` as above | Case B: `ApiException<RawError>` | none declared; the record has no page members, so the whole list comes in one call | `map/operations/Customers.md`; `Requests/Customers/ListCustomerSubscriptionsRequest.cs` |

Namespaces for the catch ladder: `MaxioAdvancedBilling.Core.Exceptions` (`ApiException<T>`, `SdkException` family, `ResponseDeserializationException`, `SdkConnectionException`, `SdkTimeoutException`), `MaxioAdvancedBilling.Core.ErrorResponse` (`RawError`), `MaxioAdvancedBilling.Errors` (typed errors).

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| The plan handle a shopper subscribes to must be one the family currently offers (non-archived product of the configured family), not merely a handle that exists on the site | `CreateSubscription` (`ProductHandle`) <- `ListProductsForProductFamily` (`ProductFamilyId = "handle:" + Maxio:ProductFamilyHandle`) | Before `CreateSubscription`, and before any claim is written: the subscribe service reads the plan list and rejects an unknown handle with 400. The `GET /api/subscription-plans` call and this check share one read. |
| The customer id passed to `CreateSubscription` must be the one this application stored for that user (never taken from the caller) | `CreateSubscription` (`CustomerId`) <- `CreateCustomer` / `ReadCustomerByReference` result recorded in the customer link | Before `CreateSubscription`: the id comes only from the caller's customer-link row. |
| The subscription handle sought in `my-subscriptions` is restricted to the caller's own customer id | `ListCustomerSubscriptions` (`CustomerId`) <- customer link | Before the call: the id comes from the link row of the token's identity. |

### Enums actually needed

| Enum | Members used | Source |
| --- | --- | --- |
| `MaxioAdvancedBilling.Models.Enums.SubscriptionState` (open string enum, static members) | `Pending`, `Active`, `Trialing`, `PastDue`, `Canceled`, `Expired`, `OnHold`, `Unpaid`, `SoftFailure`, `FailedToCreate`, `AwaitingSignup`, etc. Surface the wire value via the type's own members. Treat an unrecognised value as passthrough. | `Models/Enums/SubscriptionState.cs` |
| `MaxioAdvancedBilling.Models.Enums.IntervalUnit` | read as returned for display of the billing interval | `Models/Product.cs` (member type); values in `Models/Enums/IntervalUnit.cs` (open at implementation time) |

### Client construction / auth / server-node facts

| Fact | Value | Source |
| --- | --- | --- |
| Client | `MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions)`; DI helper `services.AddMaxioAdvancedBillingClient(options => ...)` | `sdk-map.md` (Getting a client) |
| Auth | `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username, Password }`. Username = the API key (`Maxio:ApiKey`); password is `x`. | `sdk-map.md` Servers & auth |
| Subdomain | `options.Server.Production.Us.Site` (template var `{site}`, default `"subdomain"`); fill from `Maxio:Subdomain` | `sdk-map.md` Servers & auth |
| Environment | `ServerEnvironment.Us` is the default (namespace `MaxioAdvancedBilling.Servers`). Which of Us/Eu hosts the sandbox site is UNVERIFIED by the map (the map says Us is "valid for the majority" of accounts); use Us. | `sdk-map.md`, `Servers/ServerEnvironment.cs` |
| Custom base URL (grounded) | `options.Server.Production.Us.BaseUrl` is a settable string (default `https://{site}.chargify.com`). The map's *Servers & auth* names it as the override point. `dotnet-configuration-resilience` states a literal URL with no `{placeholders}` is used as-is. So when `Maxio:BaseUrl` is set, assign it verbatim to `options.Server.Production.Us.BaseUrl` and ignore the subdomain for URL derivation; when unset, leave the default template and set `Site`. Set it before constructing the client; `options.Environment` is captured at construction and only that environment's options are read, so the override must be on the environment selected (Us). | `sdk-map.md` Servers & auth; `Servers/ProductionOptions.cs`; `dotnet-configuration-resilience` |
| Server groups touched | `Production` only. `Ebb` (events) is not touched: no events/usage call is in scope. | `sdk-map.md` |
| Retry | `options.Retry` is `MaxioAdvancedBilling.Core.Configuration.RetryOptions`; all members `required`; start from `RetryOptions.Default()`. | `sdk-map.md` |

## 3. Trap notes

| Step | Hazard | Skill |
| --- | --- | --- |
| 1 | Credential placement and which property takes which half of the Basic credential; what happens on a missing secret. | MUST load `maxio:dotnet-authentication` |
| 1 | How the client and its `HttpClient` are built and owned under DI, and what lifetime captures the options. | MUST load `maxio:dotnet-client-initialization` |
| 1 | Which server options are actually read and when, so a base-URL override can silently be ignored. | MUST load `maxio:dotnet-configuration-resilience` |
| 3-5 | Per-operation request records, return shapes and cancellation. | MUST load `maxio:dotnet-calling-endpoints` |
| 3-5 | Nullability of envelope members, open enums, `AdditionalProperties`. | MUST load `maxio:dotnet-models` |
| 4 | Which write verbs the SDK may resend, and what a transport failure on a write leaves unknown. | MUST load `maxio:dotnet-configuration-resilience` |
| 3 | The plan list is an unpaginated list return with page members of its own: where the loop stops and what a cap hides. | MUST load `maxio:dotnet-configuration-resilience` |
| 3-5 | Every error exception type that can reach a catch, including ones that are not `ApiException<TError>`. | MUST load `maxio:dotnet-error-handling` |
| 6 | Seam for faking the SDK's HTTP calls and asserting the outgoing request. | MUST load `maxio:dotnet-testing` |
| 4 | EF in-memory duplicate-key behaviour differs between a tracked duplicate and a duplicate across contexts: the claim catch must cover what this store raises. (No skill; verify against the running store.) | n/a |

## 4. REQUIRED READING

Load all before implementing:

- `maxio:dotnet-authentication` · step 1, credential wiring
- `maxio:dotnet-client-initialization` · step 1, DI registration
- `maxio:dotnet-configuration-resilience` · steps 1, 3, 4 (base URL, retries, timeouts, paging, logging)
- `maxio:dotnet-calling-endpoints` · steps 3-5
- `maxio:dotnet-models` · steps 3-5
- `maxio:dotnet-error-handling` · steps 3-5 (always required)
- `maxio:dotnet-testing` · step 6

Hazard row (verbatim): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | Credentials are bound from `IConfiguration` section `Maxio` (user-secrets in development; environment variables `Maxio__ApiKey` etc. elsewhere; the repo's `Program.cs` already adds environment variables). The SDK's own `MAXIO_ADVANCED_BILLING_BASIC_AUTH_*` variables are not used (value in code wins). At startup the host throws when `Maxio:ApiKey`, `Maxio:ProductFamilyHandle` is null/whitespace, or when `Maxio:Subdomain` is blank and `Maxio:BaseUrl` is also blank. Both halves of the Basic credential are covered: the username is the key and the password is the fixed `x` from the map, so a blank key is the only blank part. |
| 2 | Secret sourcing & rotation | Key comes from .NET user-secrets populated from `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_DEFAULT_PRODUCT_FAMILY` (never written into the repo or appsettings). The DI registration builds options once and the singleton captures them: a rotated key takes effect on process restart. Rotation without restart is not required. |
| 3 | Total timeout budget | Budget is a config-free constant in the subscribe/list service (30 s) enforced by a linked `CancellationTokenSource` from `HttpContext.RequestAborted` in a single `Bounded` helper that every SDK call passes through; the subscribe path makes up to 4 SDK calls under ONE shared token. Per-attempt `Retry.Timeout` set to 10 s and `HttpClient.Timeout` as a backstop. Number caller experiences: at most 30 s per request. Exact bounds: `dotnet-configuration-resilience`. |
| 4 | Write-retry ownership | `CreateCustomer` and `CreateSubscription` are POSTs: the SDK default `HttpMethodsToRetry` does not resend them and the plan keeps that list at the default; the SDK owns retries of GETs (reads) only. The application owns write recovery via the claim plus reconciliation (rows 5, 9, 11). Application code never re-issues a write on its own. |
| 5 | Idempotency & ambiguous writes | `CreateCustomer`: no request-record key (only the body field `customer.reference`, whose server-side uniqueness is UNVERIFIED); reconciliation by `ReadCustomerByReference` with the reference `eshop-user:{userId}` before create and after an unknown outcome. `CreateSubscription`: no record key (only body `subscription.reference`, dedupe behaviour UNVERIFIED); reconciliation by `FindSubscription` with reference `eshop-sub:{userId}:{planHandle}`. The injected `Idempotency-Key` header is not a key. |
| 6 | Observability | SDK logging left at the host's logger (`Information` request/response lines, `Warning` on failure and retries). Own logs: `Information` for claim taken / customer adopted or created / subscription created (user id hashed or omitted, Maxio ids included); `Warning` for refused duplicate claims and 4xx from Maxio; `Error` for 5xx and unknown outcomes. The Maxio error correlation id: UNVERIFIED (no request-id member is on any typed error in the map); log `RawError.StatusCode` and the response body of Case B, and `TryGetErrorListResponse1` messages for 422. |
| 7 | Sensitive data | Scope carries the shopper's e-mail and name (personal data) and no card data. `Logging.LogRequestBody` off (default); `Logging.LoggerFactory` assigned explicitly (the DI helper supplies the container's) so `MAXIO_ADVANCED_BILLING_LOG` is ignored; no form bodies are used so the deny-list is not needed. Own logs never print e-mail. |
| 8 | Environment selection | Group `Production` only, `Us` environment. Deployment sets `Maxio:Subdomain` (sandbox site subdomain) for the derived `https://{site}.chargify.com`, or `Maxio:BaseUrl` to use a full address verbatim. The `Eu` environment and `Ebb` group are not used. The SDK declares no sandbox environment: sandbox traffic is kept away from the live site solely by the sandbox site subdomain and sandbox API key in user-secrets. Target: sandbox. |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS. |
| 10 | Partial results | See PAGED READS. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| `CreateCustomer` | Row in a new `MaxioCustomerLink` entity on the existing `CatalogContext` (in-memory in this environment), written and saved BEFORE any SDK call; columns: `UserId` (PK, max 256), `MaxioCustomerId int?`, `State`, timestamps | Primary key on `UserId`: the store refuses the second insert even in-memory | In the `catch` around the claim `SaveChangesAsync` (the exception this store raises for a duplicate key, to be confirmed against the running store, both `DbUpdateException` and `InvalidOperationException` for a tracked duplicate); on refusal, re-read the row: complete row -> reuse its customer id; in-flight row -> 409 Conflict (retry later). Claim is released (row deleted) if Maxio refuses the call with a definite 4xx. | `MaxioSubscriptionService.EnsureCustomerAsync` (claim: `_db.SaveChangesAsync` after `MaxioCustomerLinks.Add`; catch also covers `ArgumentException`, which is what the in-memory store raises) then `CreateCustomerClaimedAsync` (`Customers.CreateCustomer`) |
| `CreateSubscription` | Row in a new `MaxioSubscriptionLink` entity on `CatalogContext`, written and saved BEFORE the SDK call; columns: `UserId` + `PlanHandle` (composite PK), `MaxioSubscriptionId int?`, `Reference`, `State`, timestamps | Composite primary key `(UserId, PlanHandle)`: the second insert is refused | `catch` around the claim `SaveChangesAsync`; on refusal, load the earlier row and return its result (HTTP 200 with the existing subscription if complete, 409 if still in flight). Released (row deleted) when Maxio definitively refuses. | `MaxioSubscriptionService.EnsureSubscriptionAsync` (claim save) then `CreateSubscriptionClaimedAsync` (`Subscriptions.CreateSubscription`) |

**PAGED READS**

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| `ListProductsForProductFamily` (plans), driven by `Page`/`PerPage = 200` | A page cap of 10 pages inside the service | The service's result type carries `IsTruncated: bool` (also surfaced in the `GET /api/subscription-plans` response body); if the cap is hit while validating a subscribe request, the unknown handle is not reported as invalid but the request fails with 503. | `MaxioSubscriptionService.ListPlansCoreAsync` sets `PlanListResult.IsTruncated`; `SubscribeCoreAsync` turns it into 503 |
| `ListCustomerSubscriptions` | none: the operation takes no page members and returns one list | n/a (no cap) | n/a: `ListMineCoreAsync` |

**UNKNOWN OUTCOMES**

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `CreateCustomer` | `Customers.ReadCustomerByReference` | `customer.reference` = `eshop-user:{userId}` sent on the create | `CreateCustomerClaimedAsync` catch for `SdkConnectionException`/`ResponseDeserializationException`/5xx -> `ReadCustomerByReferenceAsync`; otherwise 409 and claim stays in flight; swept by `SettleInFlightCustomerAsync` on the next call | No dedicated connection-failure test for customer (subscription path covered the same way by review only; live-tested adopt-by-reference after store reset) |
| `CreateSubscription` | `Subscriptions.FindSubscription` | `subscription.reference` = `eshop-sub:{userId}:{planHandle}` sent on the create | `CreateSubscriptionClaimedAsync` same catch -> `FindSubscriptionAsync`; swept by the catch branch of `EnsureSubscriptionAsync` | No connection-failure test written; concurrency and repeat paths are tested in `MaxioSubscriptionServiceTests` |

For both, the settling code is in the failing write's own `catch` for `SdkConnectionException` (which covers `SdkTimeoutException`): it re-reads by reference, adopts what it finds and records the ids in the link row; when the re-read finds nothing or fails too, the row stays in an in-flight state (not failed) and the response is 202/409 "outcome unknown, retry shortly". The next subscribe call by the same user finds the in-flight row, runs the same reference lookup first, and settles it before creating anything (the sweep).

## 6. Assumptions & Blockers

Blockers: none that stop planning. The plugin map has every operation needed. The persistence requirement is met by the repo's existing `CatalogContext`.

Assumptions:

1. **Default target plan.** DECIDED: body `planHandle` (alias `productHandle`); when omitted, optional `Maxio:DefaultPlanHandle`, else the only offered plan, else 400 listing offered handles (`SubscribeCoreAsync`). Nothing hard-coded.
2. **Plan identity.** Plans are the non-archived products in the family named by `Maxio:ProductFamilyHandle`, read through `ProductFamilyId = "handle:<value>"` (documented on the request record). No numeric id is configured or stored by hand; ids appear only as values read from responses and in link rows.
3. **Caller identity.** The `ClaimTypes.Name` claim of the validated JWT (the only identity claim the repo's token carries). Not read from the request body.
4. **Customer names for Maxio.** `CreateCustomer` requires `FirstName` and `LastName`, and the app's token has only the e-mail. DECIDED: first name = e-mail local part, last name = fixed text `eShopOnWeb` (`CreateCustomerClaimedAsync`).
5. **Subscription uniqueness granularity.** One subscription per (user, plan). A user may hold different plans; re-subscribing to the same plan returns the earlier record (`EnsureSubscriptionAsync`). DECIDED.
6. **Payment profile.** None sent: the plans do not require one (per the request). `Product.RequireCreditCard` is read from the plan list; if a plan reports it as true the subscribe call is refused with 422 before any claim. If Maxio returns a payment related 422 anyway, that is surfaced as a 422/400 to the caller, with the claim released. `UNVERIFIED` whether Maxio accepts a create without a payment profile on this site.
7. **Reference semantics.** Whether Maxio enforces uniqueness of `customer.reference` / `subscription.reference`, and the not-found status of `ReadCustomerByReference`, are `UNVERIFIED`; the code must treat any `RawError` 404 as not found, and any other failure on the re-read as unknown.
8. **Next billing date** is read as `Subscription.NextAssessmentAt`, falling back to `CurrentPeriodEndsAt` when null (both documented in `Models/Subscription.cs`).
9. **User-secrets** load only in the Development environment; other environments supply `Maxio__*` environment variables. Population of secrets from the three `MAXIO_*` variables is a developer/CI step, not repo content.
10. **Local SDK reference.** The SDK is referenced as a `ProjectReference` to the plugin's `sdk/dotnet/src/MaxioAdvancedBilling/MaxioAdvancedBilling.csproj` (machine local; per `dotnet-getting-started`, say so on handover). It is netstandard2.0 and uses `Nullable enable`.
11. **JWT only on these three routes**, using the same `[Authorize(... AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]` style as the admin catalog endpoints, without a role requirement.

## 7. Decisions made during implementation

- **Collection method.** First live attempt with `automatic` collection was refused by Maxio (422, no payment method on file for the balance). `CreateSubscription.PaymentCollectionMethod` is documented on `Models/CreateSubscription.cs`; the code sends `CollectionMethod.Remittance` (invoice-based, no card) in `CreateSubscriptionClaimedAsync`. YOUR CALL — decided: remittance. A site on the legacy statements architecture would need `invoice`.
- **Stale claims.** An in-flight claim older than 2 minutes with nothing found at Maxio is taken over (`SettleInFlightCustomerAsync`, `EnsureSubscriptionAsync`). YOUR CALL — decided.
- **Extra optional key** `Maxio:DefaultPlanHandle` (not required by the task). YOUR CALL — decided.
- **Verified live (sandbox):** plans listed; concurrent double POST created one subscription (loser 409, then 200 with `alreadySubscribed: true`); `my-subscriptions` shows plan, price, state `active`, next billing date.
