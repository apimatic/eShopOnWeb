# Maxio Advanced Billing — eShopOnWeb subscriptions

SDK root (plugin-relative): `sdk/dotnet/`. Root namespace `MaxioAdvancedBilling`.

## 1. Scope & sequence

Additive billing on `src/PublicApi`. Does not change basket/checkout.

| Step | What | Operations |
| --- | --- | --- |
| 1 | Bind `Maxio` options, fail-fast, register one singleton client (Production / `ServerEnvironment.Us`) | — |
| 2 | `GET /api/subscription-plans` — products in the configured family | `ProductFamilies.ListProductsForProductFamily` |
| 3 | Ensure one Maxio customer for the signed-in user | `Customers.ReadCustomerByReference`, then `Customers.CreateCustomer` only when absent |
| 4 | `POST /api/subscriptions` — enroll on a listed plan | `Subscriptions.FindSubscription`, then `Subscriptions.CreateSubscription` only when absent |
| 5 | `GET /api/my-subscriptions` — that customer's subscriptions | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |
| 6 | Local claim rows in `CatalogContext` before each write; reconcile unknown writes by re-read | the writes in steps 3–4 and the lookups named in UNKNOWN OUTCOMES |

Caller identity is the JWT (`ClaimTypes.Name` → `UserManager<ApplicationUser>`). Routes follow `src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.cs` (`IEndpoint<…>`, `MapGet`/`MapPost` under `api/`, JWT `Authorize`). Service boundary follows `src/ApplicationCore/Interfaces/IOrderService.cs` (interface in ApplicationCore, implementation in Infrastructure). Persistence follows `src/Infrastructure/Data/CatalogContext.cs` plus `src/Infrastructure/Data/Config/CatalogItemConfiguration.cs`.

`ProductFamilies.ReadProductFamily` is not used: `ReadProductFamilyRequest.Id` is `int`, so a handle cannot be passed. Family selection is `ListProductsForProductFamilyRequest.ProductFamilyId` = `handle:` + `Maxio:ProductFamilyHandle` (that member's own docs).

No catalog creation. The metered component is out of scope.

## 2. CONTRACT SHEET

Signatures are generated code, verbatim — each operation that takes input takes ONE request record as its first parameter (an operation with no inputs takes none), built with an object initializer whose property names are the record's own, never flat arguments.

Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type, never from where a neighbouring type sits.

| Controller property | Method signature | Request record + members | Body model + fields the call sends | Response envelope + fields read | Error | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `MaxioAdvancedBilling.Api.ProductFamilies` (`client.ProductFamilies`) | `ListProductsForProductFamily(ListProductsForProductFamilyRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest`. `ProductFamilyId: string, required` — id or `handle:` + handle. `Page: int = 1` (`page`). `PerPage: int = 20` (`per_page`, `[Maximum(200)]`, docs: default 20, values over 200 become 200). `DateField`, `Filter`, `StartDate`, `EndDate`, `StartDatetime`, `EndDatetime`, `IncludeArchived`, `Include` optional — **left out** (no date filter, no archived products, no extra include). | none | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>`. Envelope `Product` (`product`) required. Read `Product.Id` (`id`), `Name` (`name`), `Handle` (`handle`), `Description` (`description`), `PriceInCents` (`price_in_cents`), `Interval` (`interval`), `IntervalUnit` (`interval_unit`), `RequireCreditCard` (`require_credit_card`), `Currency` is not on `Product`. | Case A `ApiException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`: `TryGetString(out string)` [404], `TryGetRawError(out RawError)` [fallback] | Not a `Pageable` (no Pagination bullet). Caller drives `Page`/`PerPage`. Docs: empty set when a page has no rows. | `map/operations/ProductFamilies.md`; `Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs`; `Api/ProductFamilies.cs`; `Models/ProductResponse.cs`; `Models/Product.cs` |
| `MaxioAdvancedBilling.Api.Customers` (`client.Customers`) | `ReadCustomerByReference(ReadCustomerByReferenceRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest`. `Reference: string, required` (`reference`). | none | `MaxioAdvancedBilling.Models.CustomerResponse`. Envelope `Customer` (`customer`) required. Read `Id` (`id`), `Reference` (`reference`), `Email` (`email`). | Case B `ApiException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`: `StatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()`. **UNVERIFIED** which status a missing reference returns — treat `404` as absent; anything else is a failure. | none | `map/operations/Customers.md`; `Requests/Customers/ReadCustomerByReferenceRequest.cs`; `Api/Customers.cs` remarks: single match by reference; `Models/CustomerResponse.cs`; `Models/Customer.cs` |
| `MaxioAdvancedBilling.Api.Customers` | `CreateCustomer(CreateCustomerOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest`. `Body: CreateCustomerRequest?` — not marked required; **always set** (it is the document). No idempotency-key member. Method sends `Idempotency-Key: Guid.NewGuid()` — not a key. | `MaxioAdvancedBilling.Models.CreateCustomerRequest.Customer` (`customer`) required → `MaxioAdvancedBilling.Models.CreateCustomer`. **Send:** `FirstName` (`first_name`) required, `LastName` (`last_name`) required, `Email` (`email`) required, `Reference` (`reference`) optional on the model, **sent** — remarks: one customer per reference; it is the app's own id. **Left out:** `CcEmails`, `Organization`, `Address`, `Address2`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `VatNumber`, `VatCountry`, `EntityIdentifierKind`, `EntityIdentifierValue`, `TaxExempt`, `Surcharging`, `TaxExemptReason`, `ParentId`, `SalesforceId`, `BrandingThemeId` (no profile data; country/state/tax rules in the remarks do not apply when omitted). | `CustomerResponse` / `Customer`. Read `Id`, `Reference`. | Case A `ApiException<MaxioAdvancedBilling.Errors.CreateCustomerError>`: `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422], `TryGetRawError(out RawError)` [fallback]. `CustomerErrorResponse1.Errors` (`errors`) is `MaxioAdvancedBilling.Models.AnyOf.Errors1?`: `TryGetCustomerError` → `MaxioAdvancedBilling.Models.CustomerError.Customer` (`customer`: string); `TryGetListOfString` → `IReadOnlyList<string>`. | none | `map/operations/Customers.md`; `Api/Customers.cs` remarks (reference unique); `Models/CreateCustomerRequest.cs`; `Models/CreateCustomer.cs`; `Models/CustomerErrorResponse1.cs`; `Models/AnyOf/Errors1.cs`; `Models/CustomerError.cs` |
| `MaxioAdvancedBilling.Api.Subscriptions` (`client.Subscriptions`) | `FindSubscription(FindSubscriptionRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest`. `Reference: string?` (`reference`) — not marked required; **always set** (it is the lookup). | none | `MaxioAdvancedBilling.Models.SubscriptionResponse`. Envelope `Subscription` (`subscription`) is **nullable**. Read fields below. | Case A `ApiException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404], `TryGetRawError(out RawError)` [fallback] | none | `map/operations/Subscriptions.md`; `Api/Subscriptions.cs` remarks: find by reference; `Requests/Subscriptions/FindSubscriptionRequest.cs`; `Models/SubscriptionResponse.cs` |
| `MaxioAdvancedBilling.Api.Subscriptions` | `CreateSubscription(CreateSubscriptionOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest`. `Body: CreateSubscriptionRequest?` — not marked required; **always set**. No idempotency-key member. Method sends `Idempotency-Key: Guid.NewGuid()` — not a key. | `MaxioAdvancedBilling.Models.CreateSubscriptionRequest.Subscription` (`subscription`) required → `MaxioAdvancedBilling.Models.CreateSubscription`. **Send:** `ProductHandle` (`product_handle`) — remarks: required unless `product_id`; `CustomerReference` (`customer_reference`) — remarks: required unless `customer_id` or `customer_attributes`; `Reference` (`reference`) — app reference for `FindSubscription`. **Left out:** `ProductId` (numeric ids are unstable), `ProductPricePointHandle` / `ProductPricePointId` / `CustomPrice` (product default price), `CouponCode` / `CouponCodes`, `PaymentCollectionMethod`, `ReceivesInvoiceEmails`, `NetTerms`, `CustomerId`, `BrandingThemeId`, `NextBillingAt` / `InitialBillingAt` / `DeferSignup` (would change billing timing), `StoredCredentialTransactionId`, `SalesRepId`, `PaymentProfileId`, `CustomerAttributes`, `PaymentProfileAttributes`, `CreditCardAttributes`, `BankAccountAttributes` (no card capture; payment may be required only when the product says so — remarks “Payment information”), `Components`, `CalendarBilling`, `Metafields`, `Group`, `Ref`, cancellation fields, `Currency`, `ExpiresAt`, `ExpirationTracksNextBillingChange`, ACH/agreement fields, `OfferId`, `PrepaidConfiguration`, `PreviousBillingAt`, `ImportMrr`, `CanceledAt`, `ActivatedAt`, `ProductChangeDelayed`, `SkipBillingManifestTaxes`, dunning-delay fields. | `SubscriptionResponse.Subscription`. Read `Id` (`id`), `State` (`state`), `ProductPriceInCents` (`product_price_in_cents`), `CurrentPeriodEndsAt` (`current_period_ends_at`) — remarks: next regularly scheduled charge; if null, `NextAssessmentAt` (`next_assessment_at`). `Reference` (`reference`), `Currency` (`currency`), `Product.Handle` (`product.handle`), `Product.Name` (`product.name`). `Subscription` itself may be null. | Case A `ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`: `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422], `TryGetRawError(out RawError)` [fallback]. `ErrorListResponse1.Errors` (`errors`): required `IReadOnlyList<string>`. | none | `map/operations/Subscriptions.md`; `Api/Subscriptions.cs` remarks; `Models/CreateSubscriptionRequest.cs`; `Models/CreateSubscription.cs`; `Models/Subscription.cs`; `Models/ErrorListResponse1.cs` |
| `MaxioAdvancedBilling.Api.Customers` | `ListCustomerSubscriptions(ListCustomerSubscriptionsRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest`. `CustomerId: int, required`. | none | `IReadOnlyList<SubscriptionResponse>`. Same subscription fields as create. Remarks: lists **all** subscriptions for the customer; `Product` may be null. | Case B `ApiException<RawError>` | none — request has no page members | `map/operations/Customers.md`; `Api/Customers.cs`; `Requests/Customers/ListCustomerSubscriptionsRequest.cs` |

### Enums

`MaxioAdvancedBilling.Models.Enums.SubscriptionState` : `OpenStringEnum` — read with `.Value`, never `ToString()` / interpolation. Wire values: `pending`, `failed_to_create`, `trialing`, `assessing`, `active`, `soft_failure`, `past_due`, `suspended`, `canceled`, `expired`, `paused`, `unpaid`, `trial_ended`, `on_hold`, `awaiting_signup`. Source: `Models/Enums/SubscriptionState.cs`.

`MaxioAdvancedBilling.Models.Enums.IntervalUnit` : `OpenStringEnum`. Wire values: `day`, `month`. Same `.Value` rule. Source: `Models/Enums/IntervalUnit.cs`.

### Client, auth, servers

- Client: `MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)`. DI: `AddMaxioAdvancedBillingClient`. Source: `sdk-map.md` Getting a client.
- Auth: `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }`. Username is the API key; password is the literal `x`. Source: `sdk-map.md` Servers & auth. Every in-scope operation's Auth bullet is `options.BasicAuth`.
- `options.Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default). In-scope operations use server group **Production** (no Server group bullet). `Us` template `https://{site}.chargify.com`, `{site}` default `"subdomain"`, override `options.Server.Production.Us.Site`. Verbatim base override: `options.Server.Production.Us.BaseUrl`. **Ebb** is unused. `ServerEnvironment.Eu` is unused — `Maxio` has no environment key (see Assumptions).
- Options captured once at registration. `Environment` is read at construction; set `Server` before `new`.

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| `product_handle` accepted by create-subscription must be a `Product.Handle` returned for the configured family | `Subscriptions.CreateSubscription` ← `ProductFamilies.ListProductsForProductFamily` (`ProductFamilyId` = `handle:` + `Maxio:ProductFamilyHandle`) | subscribe path checks membership against that list before the subscription claim is written |

## 3. Trap notes

- Step 1 — constructing the client and who owns `HttpClient`, and what a late `Environment` assignment does. **MUST load dotnet-client-initialization**
- Step 1 — which `BasicAuthCredentials` property takes the API key versus the literal password, and a blank part still sends. **MUST load dotnet-authentication**
- Steps 2–5 — one request record, cancellation token, and `RequestOptions` versus client options. **MUST load dotnet-calling-endpoints**
- Steps 2–5 — `required` init members, open enums (`.Value` vs `ToString()`), `Errors1` `TryGet…`, nullable `Subscription` envelope, `DateTimeOffset` fields. **MUST load dotnet-models**
- Steps 3–5 — Case A vs Case B, `TryGet…` vs `RawError`, and a body that does not match its declared type. **MUST load dotnet-error-handling**
- Step 1 and every call — what `Retry.Timeout` bounds, which verbs are resent, manual `page`/`per_page` loops, `LogRequestBody`, and `LoggerFactory` left null. **MUST load dotnet-configuration-resilience**
- Tests — stubbing the HTTP seam and faking the clock. **MUST load dotnet-testing**

## 4. REQUIRED READING

- `maxio-advanced-billing` `dotnet-client-initialization` — step 1, client and DI
- `maxio-advanced-billing` `dotnet-authentication` — step 1, Basic auth
- `maxio-advanced-billing` `dotnet-calling-endpoints` — steps 2–5, every call
- `maxio-advanced-billing` `dotnet-models` — steps 2–5, payloads and enums
- `maxio-advanced-billing` `dotnet-error-handling` — steps 3–5, catch boundaries
- `maxio-advanced-billing` `dotnet-configuration-resilience` — step 1 and call bounds, retries, paging, logging
- `maxio-advanced-billing` `dotnet-testing` — unit tests for claims and unknown outcomes

A body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | Bind section `Maxio` to keys `ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl`. `ValidateOnStart` rejects a missing or whitespace `ApiKey`, `Subdomain`, or `ProductFamilyHandle`. `BaseUrl` may be empty; when set it must be an absolute `http`/`https` URI or startup fails. The Basic password is the map literal `x`, not a fourth secret. |
| 2 | Secret sourcing & rotation | Values come from user-secrets (Development) and environment variables, never from committed files. The registration builds `MaxioAdvancedBillingClientOptions` once and the singleton client captures it. A rotated `Maxio:ApiKey` applies on process restart only. |
| 3 | Total timeout budget | **30 seconds** for one PublicApi billing request, enforced by a single `CancellationTokenSource` linked to `HttpContext.RequestAborted` inside the billing service, passed to every SDK call that request makes (including plan pages). Per-attempt `options.Retry.Timeout` = **8s** (not the 100s default). `HttpClient.Timeout` = **10s** as the per-attempt header backstop. `Retry.Timeout` does not bound the whole call. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` is GET, HEAD, PUT, OPTIONS. `CreateCustomer` and `CreateSubscription` are POST, so the SDK does not resend them. POST is not added to that list. A `DelegatingHandler` also refuses a second send of a POST inside an `AsyncLocal` scope the service opens around each write, by throwing a private exception that is not `HttpRequestException`. GETs may retry up to `MaxRetries` 3 and are cut by the 30s token. |
| 5 | Idempotency & ambiguous writes | Neither write's request record has an idempotency member. The injected `Idempotency-Key` header is a new GUID per call and is not a key. Customer reconciliation key is `CreateCustomer.Reference` = the eShop user id, re-read with `ReadCustomerByReference`. Subscription reconciliation key is `CreateSubscription.Reference` = `eshop:{userId}:{productHandle}`, re-read with `FindSubscription`. **UNVERIFIED** whether Maxio rejects a duplicate subscription reference; the local claim is what stops a second send. |
| 6 | Observability | Information: operation name and Maxio id on success. Warning/Error: HTTP status plus `CustomerError.Customer`, `Errors1` string list, or `ErrorListResponse1.Errors`. Those payloads have no correlation-id member; logs do not invent one. `RawError` contributes status only, not the raw body (it can echo input). |
| 7 | Sensitive data | Create-customer sends email and name. `LogRequestBody` stays false. `Logging.LoggerFactory` is set to the host factory so `MAXIOADVANCEDBILLINGCLIENT_LOG=trace` cannot turn bodies on. No card/form fields are sent, so no extra `RedactedKeys`. Application logs omit email and request JSON. |
| 8 | Environment selection | Groups: Production (`Us` `https://{site}.chargify.com`, `Eu` `https://{site}.ebilling.maxio.com`) and Ebb (unused). Scope uses Production only. Deployment sets `options.Environment = ServerEnvironment.Us`, `options.Server.Production.Us.Site` = `Maxio:Subdomain` when `Maxio:BaseUrl` is empty, and when `Maxio:BaseUrl` is set assigns that string to `options.Server.Production.Us.BaseUrl` and does not derive a host from the subdomain. The SDK has no sandbox environment; sandbox vs live is which site those two settings name. |
| 9 | Duplicate prevention | See DUPLICATE CLAIMS. Store is `CatalogContext` (the app's existing database). Claim key is the primary key, which the in-memory provider refuses. |
| 10 | Partial results | See PAGED READS. `Truncated` on the plan-list result is true only when the page cap stops the walk on a full page. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. A connection failure re-reads before returning. If the re-read misses or fails, the claim stays `Unknown` (not failed) and the HTTP result is 503. A later subscribe or my-subscriptions call runs the sweep. |

### DUPLICATE CLAIMS

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| `Customers.CreateCustomer` | `CatalogContext` entity `BillingClaim`, primary key `customer:{userId}` | `DbUpdateException` on the primary key | `catch (DbUpdateException)` in the customer ensure path | TBD |
| `Subscriptions.CreateSubscription` | `CatalogContext` entity `BillingClaim`, primary key `subscription:{userId}:{productHandle}` | `DbUpdateException` on the primary key | `catch (DbUpdateException)` in the subscribe path | TBD |

Claim is inserted `Pending` before the SDK write and updated `Completed` with the provider id after success. Provider refusal (after a re-read that does not find the resource) deletes the row. A second caller that finds `Completed` returns that subscription (refreshed via the lookup) and does not call create. A second caller that finds a fresh `Pending` gets 409 and is not told the write finished. A `Pending`/`Unknown` row older than 2 minutes is settled by the re-read, then released if the provider still has nothing.

### PAGED READS

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| `ProductFamilies.ListProductsForProductFamily` | 25 pages, `PerPage` 200; stop earlier when a page returns fewer than 200 | `SubscriptionPlanList.Truncated` (`true` when the cap stops the walk while the last page was full) | TBD |

### UNKNOWN OUTCOMES

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `Customers.CreateCustomer` | `Customers.ReadCustomerByReference` | `Reference` = eShop `ApplicationUser.Id` | TBD | TBD |
| `Subscriptions.CreateSubscription` | `Subscriptions.FindSubscription` | `Reference` = `eshop:{userId}:{productHandle}` | TBD | TBD |

## 6. Assumptions & Blockers

Assumptions (minor — proceeding):

- `ServerEnvironment.Us` is correct for this sandbox. `MAXIO_ENVIRONMENT` is not one of the bound keys (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl`). A different host is expressed with `Maxio:BaseUrl`.
- `ApplicationUser` has email and id only. Customer `FirstName` is the email local-part; `LastName` is `Shopper`.
- One subscription per user per product handle. A repeat POST returns the existing subscription instead of creating another.
- `require_credit_card == true` on a listed product is refused locally (422) without calling create. Seeded plans are expected to be false; no card fields are sent.
- Next-billing date shown to the user is `CurrentPeriodEndsAt`, or `NextAssessmentAt` when that is null.
- `CatalogContext` is the claim store. On this machine `UseOnlyInMemoryDatabase=true` drops rows on process exit; the same entity survives when the app uses SQL Server. That is an environment limit, not a missing store.
- `Program.cs` calls `UseAuthorization` without `UseAuthentication`. The pipeline will call `UseAuthentication` first so JWT `[Authorize]` runs.
- `global.json` `rollForward` becomes `latestMajor` so the installed .NET 10 SDK builds the net8.0 projects. Processes run with `DOTNET_ROLL_FORWARD=Major`.
- The SDK `ProjectReference` points at this machine's plugin install. Another machine must re-point it. The SDK source is not copied into the repo.
- `appsettings.test.json` gets non-secret dummy `Maxio` strings so existing PublicApi host tests still start. Real credentials stay in user-secrets.

Blockers: none. Every call the flow needs is on the map.

## 7. Source labels

Used in the sheet: `sdk-map.md`, `map/operations/Customers.md`, `map/operations/Subscriptions.md`, `map/operations/ProductFamilies.md`, `map/operations/Products.md` (not called — handle lookup is on the family list), and the model/request files named in the table. `UNVERIFIED` rows: missing-customer HTTP status on `ReadCustomerByReference`; whether a repeated subscription `reference` is rejected by the provider. `YOUR CALL — not in the map`: claim table shape, name derivation, 30s budget, 25-page cap, 2-minute stale window, US environment, `Truncated` flag, refusal when `RequireCreditCard` is true.
