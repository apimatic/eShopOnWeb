# Maxio Advanced Billing — eShopOnWeb subscriptions

## 1. Scope & sequence

Additive billing on `src/PublicApi`. Existing cart/checkout stays. SDK project reference from `src/Infrastructure` to the plugin SDK (`sdk/dotnet/src/MaxioAdvancedBilling/MaxioAdvancedBilling.csproj`). No SDK source copied into the repo.

1. Bind `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` and fail fast when ApiKey, Subdomain, or ProductFamilyHandle is missing or blank. Bridge `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_DEFAULT_PRODUCT_FAMILY` into those keys. `MAXIO_ENVIRONMENT` selects `ServerEnvironment` only (`US` → `Us`, `EU` → `Eu`); it is not a fifth `Maxio:` key.
2. Register one singleton `MaxioAdvancedBillingClient` (Production server group only).
3. `GET /api/subscription-plans` — `ListProductsForProductFamily` (page loop) for family `handle:{Maxio:ProductFamilyHandle}`.
4. `POST /api/subscriptions` — invariant check against that list, then `ReadCustomerByReference` / `CreateCustomer`, then `FindSubscription` / `CreateSubscription`.
5. `GET /api/my-subscriptions` — `ReadCustomerByReference` (no create) then `ListCustomerSubscriptions`.
6. Local primary-key claims in `CatalogContext` before each write; reconcile unknown writes by re-read.
7. `global.json` `rollForward: latestMajor`. Tests + live sandbox calls.

## 2. CONTRACT SHEET

Signatures are generated code, verbatim — each operation that takes input takes ONE request record as its first parameter (an operation with no inputs takes none), built with an object initializer whose property names are the record's own, never flat arguments.

Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type, never from where a neighbouring type sits.

| Controller property | Method signature | Request record + members | Body model + fields used | Response envelope + fields read | Error | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `client.ProductFamilies` | `ListProductsForProductFamily(ListProductsForProductFamilyRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest`. `ProductFamilyId: string, required`. `Page: int = 1`. `PerPage: int = 20` (max 200). `IncludeArchived: bool?`. Left out: `DateField`, `Filter`, `StartDate`, `EndDate`, `StartDatetime`, `EndDatetime`, `Include` — not needed to list the family catalog. | none | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>` → `.Product` (`required`) of `MaxioAdvancedBilling.Models.Product`: `Id (id)`, `Name (name)`, `Handle (handle)`, `Description (description)`, `PriceInCents (price_in_cents)`, `Interval (interval)`, `IntervalUnit (interval_unit)`, `ArchivedAt (archived_at)` | Case A `ApiException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>` · `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | No SDK `Pageable`. Request `Page`/`PerPage` driven by us. Default page 1, default per page 20, max 200. Stop when a page returns fewer than `PerPage`. | `sdk/dotnet/map/operations/ProductFamilies.md`; `Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs`; `Api/ProductFamilies.cs` remarks; `Models/ProductResponse.cs`; `Models/Product.cs` |
| `client.Customers` | `ReadCustomerByReference(ReadCustomerByReferenceRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest`. `Reference: string, required`. | none | `MaxioAdvancedBilling.Models.CustomerResponse` → `.Customer` (`required`) of `MaxioAdvancedBilling.Models.Customer`: `Id (id)`, `Reference (reference)` | Case B `ApiException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` · `StatusCode`, `ReadAsString()`, `ReadAsBytes()`, `ReadAsJson<T>()` | none | `sdk/dotnet/map/operations/Customers.md`; `Api/Customers.cs` remarks ("single match") |
| `client.Customers` | `CreateCustomer(CreateCustomerOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest`. `Body: CreateCustomerRequest?` optional on the record. No idempotency member. | `MaxioAdvancedBilling.Models.CreateCustomerRequest.Customer (customer): CreateCustomer, required`. `CreateCustomer`: `FirstName (first_name): string, required`; `LastName (last_name): string, required`; `Email (email): string, required`; `Reference (reference): string?` — passed. Remarks: reference must be unique; one customer per reference. Left out: address, phone, locale, vat/entity identifier, organization, parent, branding — eShop identity has email and username only. | `CustomerResponse` → `.Customer.Id`, `.Customer.Reference` | Case A `ApiException<MaxioAdvancedBilling.Errors.CreateCustomerError>` · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. `CustomerErrorResponse1.Errors (errors): Errors1?` union `TryGetCustomerError` / `TryGetListOfString`. `CustomerError.Customer (customer): string?`. | none | `sdk/dotnet/map/operations/Customers.md`; `Api/Customers.cs` remarks; `Models/CreateCustomer.cs`; `Models/CreateCustomerRequest.cs`; `Models/CustomerErrorResponse1.cs`; `Models/AnyOf/Errors1.cs`; `Models/CustomerError.cs` |
| `client.Subscriptions` | `FindSubscription(FindSubscriptionRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest`. `Reference: string?` — passed (the doc calls it the subscription reference). | none | `MaxioAdvancedBilling.Models.SubscriptionResponse` → `.Subscription` (`Subscription?`) | Case A `ApiException<MaxioAdvancedBilling.Errors.FindSubscriptionError>` · `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback] | none | `sdk/dotnet/map/operations/Subscriptions.md`; `Api/Subscriptions.cs` remarks ("Finds a subscription by its reference") |
| `client.Subscriptions` | `CreateSubscription(CreateSubscriptionOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest`. `Body: CreateSubscriptionRequest?`. No idempotency member. Injected `Idempotency-Key` is `Guid.NewGuid()` per call — not a key. | `MaxioAdvancedBilling.Models.CreateSubscriptionRequest.Subscription (subscription): CreateSubscription, required`. Passed: `ProductHandle (product_handle): string?` (remarks: required unless `product_id`); `CustomerId (customer_id): int?` (remarks: required unless `customer_reference` or `customer_attributes`); `Reference (reference): string?` ("reference value provided by your app"). Passed: `PaymentCollectionMethod = CollectionMethod.Remittance` (`payment_collection_method`). Automatic collection was declined by the sandbox with 422 "No payment method was on file for the $299.00 balance" even though the seeded plans do not require a card. Remittance is the Relationship Invoicing collection method that does not charge a card at signup. When `Product.RequireCreditCard` is true, subscribe returns 400 and does not call create. Left out: payment profile / card / bank, coupons, components, price-point overrides, `next_billing_at`; default price point applies when price point is omitted. | `SubscriptionResponse` → `.Subscription`: `Id (id)`, `State (state)`, `ProductPriceInCents (product_price_in_cents)`, `CurrentPeriodEndsAt (current_period_ends_at)`, `Currency (currency)`, `Reference (reference)`, `Product (product)` → `Name`, `Handle` | Case A `ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>` · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. `ErrorListResponse1.Errors (errors): IReadOnlyList<string>, required`. | none | `sdk/dotnet/map/operations/Subscriptions.md`; `Api/Subscriptions.cs` remarks; `Models/CreateSubscription.cs`; `Models/CreateSubscriptionRequest.cs`; `Models/Subscription.cs`; `Models/SubscriptionResponse.cs`; `Models/ErrorListResponse1.cs` |
| `client.Customers` | `ListCustomerSubscriptions(ListCustomerSubscriptionsRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest`. `CustomerId: int, required`. | none | `IReadOnlyList<SubscriptionResponse>` → same subscription fields as create | Case B `ApiException<RawError>` | none (no page params; remarks: lists all for the customer) | `sdk/dotnet/map/operations/Customers.md`; `Api/Customers.cs` remarks |

### Enums

`MaxioAdvancedBilling.Models.Enums.SubscriptionState` : `OpenStringEnum` — wire values: `pending`, `failed_to_create`, `trialing`, `assessing`, `active`, `soft_failure`, `past_due`, `suspended`, `canceled`, `expired`, `paused`, `unpaid`, `trial_ended`, `on_hold`, `awaiting_signup`. Source: `Models/Enums/SubscriptionState.cs`.

`MaxioAdvancedBilling.Models.Enums.IntervalUnit` : `OpenStringEnum` — `day`, `month`. Source: `Models/Enums/IntervalUnit.cs`.

### Client / auth / server

- Client: `MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)`. Options source: `sdk/dotnet/sdk-map.md` Getting a client.
- Auth: `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = api key, Password = "x" }`. Username is the API key. Password is the literal `x`. Source: `sdk-map.md` Servers & auth.
- Environments: `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (host `https://{site}.chargify.com`) and `.Eu` (host `https://{site}.ebilling.maxio.com`). This deployment's `MAXIO_ENVIRONMENT` is `US`, so `ServerEnvironment.Us`. `EU` selects `.Eu`. Anything else selects `.Us`.
- Site: `options.Server.Production.{Us\|Eu}.Site` = `Maxio:Subdomain` (`{site}` default `"subdomain"`).
- `Maxio:BaseUrl` when non-blank: `options.Server.Production.{selected}.BaseUrl` verbatim. Set on the environment actually selected (the other environment's options are never read).
- Scope uses server group `Production` only. `Ebb` is not used.
- Credential not set is skipped and the request is still sent — fail fast before the client is built.

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| `productHandle` accepted by subscribe must be a `Product.Handle` returned by `ListProductsForProductFamily` for `handle:{Maxio:ProductFamilyHandle}` (archived rows dropped) | `CreateSubscription` ← `ListProductsForProductFamily` | `MaxioSubscriptionBillingService.SubscribeAsync` checks membership before the subscription claim insert and before `CreateSubscription` |

## 3. Trap notes

- Client constructor, `HttpClient` lifetime, and `AddMaxioAdvancedBillingClient` versus `new` — wrong ownership leaks sockets or captures a disposed client. **MUST load dotnet-client-initialization**
- `BasicAuth` username vs password, and a missing credential is sent anyway. **MUST load dotnet-authentication**
- One request record per call; `Idempotency-Key` header is not a caller key. **MUST load dotnet-calling-endpoints**
- `SubscriptionState` and `IntervalUnit` are `OpenStringEnum`, not C# enums; `Errors1` is an `AnyOf`. **MUST load dotnet-models**
- Case A vs B catch types differ; a body that does not match its declared type is `ResponseDeserializationException`. **MUST load dotnet-error-handling**
- What `Retry.Timeout` bounds, which verbs retry, and how a non-`Pageable` list is paged. **MUST load dotnet-configuration-resilience**
- Stubbing the client for the unknown-outcome tests. **MUST load dotnet-testing**

## 4. REQUIRED READING

- `dotnet-client-initialization` · step 2, client singleton
- `dotnet-authentication` · step 2, `BasicAuth`
- `dotnet-calling-endpoints` · steps 3–5, every SDK call
- `dotnet-models` · steps 3–5, enums and `Errors1`
- `dotnet-error-handling` · steps 3–5, catch ladder
- `dotnet-configuration-resilience` · steps 2–3, timeouts, retries, paging, logging
- `dotnet-testing` · step 7, HttpClient stub and unknown-outcome test

Hazard (always): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | Composition root binds `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl`. Host throws before `Run` if ApiKey, Subdomain, or ProductFamilyHandle is null or whitespace. `BaseUrl` may be blank. Basic-auth password is the fixed `x`, not a configured part. |
| 2 | Secret sourcing & rotation | Values come from user-secrets and from `MAXIO_API_KEY` / `MAXIO_SITE_SUBDOMAIN` / `MAXIO_DEFAULT_PRODUCT_FAMILY` copied into the `Maxio:` keys at startup. No values in repo files. Options object is built once when the singleton client is constructed; a rotated secret applies on process restart. |
| 3 | Total timeout budget | Caller-visible budget is **20 seconds** for one PublicApi request, enforced by a single `CancellationTokenSource` linked to `RequestAborted` with `CancelAfter(20s)` inside `MaxioSubscriptionBillingService` and passed to every SDK call in that request. `Retry.Timeout` is **8 seconds per attempt**. `HttpClient.Timeout` is **8 seconds per attempt**. |
| 4 | Write-retry ownership | `CreateCustomer` and `CreateSubscription` are POST. `HttpMethodsToRetry` stays the default `GET, HEAD, PUT, OPTIONS` — POST is not resent. No PUT/PATCH/DELETE in scope. A `DelegatingHandler` also refuses a second send of those two POST paths inside one `AsyncLocal` scope (private exception, not `HttpRequestException`). |
| 5 | Idempotency & ambiguous writes | Neither write's request record has an idempotency member. Customer `reference` is documented unique (422 on a duplicate). Subscription `reference` is the `FindSubscription` lookup key; provider uniqueness of that field is `UNVERIFIED`. Reconciliation is the re-read in UNKNOWN OUTCOMES, not the injected header. |
| 6 | Observability | SDK logger at Information for the request line and Warning/Error for failure (built-in). Service logs Information: user id, product handle, Maxio ids, state. Warning: HTTP status + endpoint correlation id. Error payloads in scope have no correlation-id field. Do not log `RawError.ReadAsString()` (may echo email). `SdkException.Message` already names the call with the query stripped. |
| 7 | Sensitive data | `CreateCustomer` carries email and name. Card fields exist on `CreateSubscription` and are left unset. `LogRequestBody` false. `LoggerFactory` set to the host factory so `MAXIOADVANCEDBILLINGCLIENT_LOG=trace` cannot turn bodies on. Our logs omit email and names. |
| 8 | Environment selection | Production group only. `US` → `ServerEnvironment.Us` base `https://{site}.chargify.com`. `EU` → `ServerEnvironment.Eu` base `https://{site}.ebilling.maxio.com`. `Maxio:BaseUrl` replaces that template when set. `Ebb` unused. This machine is `US`. |
| 9 | Duplicate prevention | See DUPLICATE CLAIMS. |
| 10 | Partial results | See PAGED READS. `Truncated` on the plans response. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

### DUPLICATE CLAIMS

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| `CreateCustomer` | `CatalogContext` entity `MaxioCustomerClaim`, primary key = `ApplicationUser.Id` | Primary-key insert of that row (`SaveChanges`) | `catch (DbUpdateException)`, `catch (InvalidOperationException)`, and `catch (ArgumentException)` in `BillingClaimStore.TryInsertAsync` (in-memory raises `ArgumentException`: "An item with the same key has already been added") | `BillingClaimStore.TryInsertCustomerAsync`, `MaxioSubscriptionBillingService.CreateCustomerAsync` |
| `CreateSubscription` | `CatalogContext` entity `SubscriptionEnrollmentClaim`, primary key = `{userId}:{productHandle}` | Primary-key insert of that row (`SaveChanges`) | `catch (DbUpdateException)`, `catch (InvalidOperationException)`, and `catch (ArgumentException)` in `BillingClaimStore.TryInsertAsync` | `BillingClaimStore.TryInsertSubscriptionAsync`, `MaxioSubscriptionBillingService.CreateSubscriptionAsync` |

Claim → SDK call → record the result. Provider refusal (422, and the re-read does not find the resource) deletes the claim. A second caller that loses the insert does not call the SDK: completed row returns the stored enrollment; pending/unknown younger than 60s returns 409.

### PAGED READS

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| `ListProductsForProductFamily` | `PerPage` 200, stop after 25 pages, and the 20s call budget | `ListSubscriptionPlansResponse.Truncated` is true when the 25-page cap is hit and the last page was full | `MaxioSubscriptionBillingService.ListPlansCoreAsync` |

### UNKNOWN OUTCOMES

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `CreateCustomer` | `ReadCustomerByReference` | `ApplicationUser.Id` sent as `Reference` | `MaxioSubscriptionBillingService.CreateCustomerAsync` (`catch (SdkConnectionException)` → `SettleCustomerAfterUnknownWrite`) | `SubscriptionBillingTests.CreateCustomer_WhenConnectionFails_RereadsByReference` |
| `CreateSubscription` | `FindSubscription` | `{userId}:{productHandle}` sent as `Reference` | `MaxioSubscriptionBillingService.CreateSubscriptionAsync` (`catch (SdkConnectionException)` → `SettleSubscriptionAfterUnknownWrite`) | `SubscriptionBillingTests.CreateSubscription_WhenConnectionFails_RereadsByReference` |

A 404 or a failed re-read marks the claim `Unknown` and the HTTP result is 503. A later `POST /api/subscriptions` or `GET /api/my-subscriptions` re-reads. A claim still unknown, older than 60s, and still 404 is deleted so a new create may proceed.

## 6. Assumptions & Blockers

Assumptions (minor):

- POST body is `productHandle` (stable handle, not numeric product id). Omitted or unknown handle is 400.
- One Maxio customer per eShop user. One subscription per user per product handle; a repeat POST returns the existing subscription (200). A different handle is a second subscription.
- `ApplicationUser` has no given name. `FirstName` is the email local-part (or `"Shopper"`), `LastName` is `"Shopper"`.
- JWT `ClaimTypes.Name` is the username. The user id is loaded with `UserManager`.
- Next billing date shown to the shopper is `Subscription.CurrentPeriodEndsAt`. Price shown is `ProductPriceInCents` / product `PriceInCents`.
- `CatalogContext` is the application store (SQL Server when configured). `UseOnlyInMemoryDatabase=true` drops claims on process exit; Maxio lookup by reference reconciles the next run. Not a blocker: the codebase already has this database.
- Plans endpoint is JWT-authenticated, same as the other two. Shopper role, not administrator-only.
- `global.json` roll-forward and `DOTNET_ROLL_FORWARD=Major` because this machine has the .NET 10 SDK and no ASP.NET Core 8 runtime.
- SDK `ProjectReference` is local to this plugin install.

Blockers: none. `ListProductsForProductFamilyRequest.ProductFamilyId` documents `handle:` prefix, so the family is addressed without a numeric id. `ReadProductFamilyRequest.Id` is `int` and is not used.

## 7. Source labels

`UNVERIFIED`: subscription `reference` uniqueness at the provider (lookup is singular; rejection of a duplicate is not stated in the create remarks). Customer `reference` uniqueness is stated in `Api/Customers.cs` remarks.

`YOUR CALL — not in the map`: claim table shape, 20s/8s budgets, 25-page cap, 60s stale window, name derivation, HTTP status mapping, `CurrentPeriodEndsAt` as the next-billing date, US/EU selection from `MAXIO_ENVIRONMENT`.

Repo patterns to imitate (no snippets):

- Endpoint: `src/PublicApi/CatalogItemEndpoints/CatalogItemGetByIdEndpoint.cs` (`IEndpoint`, `api/...` route). Auth attribute: `src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.cs`.
- Request/response: `src/PublicApi/BaseRequest.cs`, `src/PublicApi/BaseResponse.cs`.
- EF configuration: `src/Infrastructure/Data/Config/CatalogItemConfiguration.cs` applied from `src/Infrastructure/Data/CatalogContext.cs`.
- DI: `src/Infrastructure/Dependencies.cs`.
- Identity: `src/Infrastructure/Identity/IdentityTokenClaimService.cs`, `src/Infrastructure/Identity/ApplicationUser.cs`.
- Errors: `src/PublicApi/Middleware/ExceptionMiddleware.cs` (`DuplicateException` → 409).
- API tests: `tests/PublicApiIntegrationTests/ProgramTest.cs`.
