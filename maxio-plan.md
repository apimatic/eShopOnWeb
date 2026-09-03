# maxio-plan.md — Subscription billing for eShopOnWeb (Maxio Advanced Billing)

Additive, parallel capability on `src/PublicApi`. Maxio is the billing system of record. Three
JWT-authenticated endpoints, caller identity from the token (`ClaimTypes.Name` = eShop username/email,
set by `IdentityTokenClaimService`).

## 1. Scope & sequence

| Step | What | Maxio operations |
| --- | --- | --- |
| A | Vendor the Maxio C# SDK into the repo as a buildable project (`src/Maxio.Sdk/`), `ManagePackageVersionsCentrally=false` (SDK pins its own package versions and is not on NuGet). ProjectReference from `Infrastructure`. | — |
| B | ApplicationCore: `ISubscriptionBillingService` + POCO models (`SubscriptionPlan`, `CustomerSubscription`, `SubscribeToPlanRequest/Result`). Endpoints depend only on this interface. | — |
| C | Infrastructure/Maxio: `MaxioSettings` (bound from `Maxio:` section), `MaxioBillingService` (implements the interface via `MaxioClient`), `MaxioServiceCollectionExtensions.AddMaxioBilling` (fail-fast validation + `AddMaxioClient` + service registration). | — |
| D | GET `/api/subscription-plans` — list plans in the configured product family. | `ProductFamilies.ListProductsForProductFamily(productFamilyHandle, null×7, page:1, perPage:200)` |
| E | POST `/api/subscriptions` — ensure customer (idempotent) then subscribe (idempotent). | `Customers.ReadCustomerByReference` → (404) `Customers.CreateCustomer`; `Subscriptions.FindSubscription(reference)` → (404) `Subscriptions.CreateSubscription` |
| F | GET `/api/my-subscriptions` — the caller's subscriptions. | `Customers.ReadCustomerByReference` → (404 ⇒ empty) `Customers.ListCustomerSubscriptions(customerId)` |
| G | Wire user-secrets config + `AddMaxioBilling` in `PublicApi/Program.cs`. Load secrets into user-secrets from env vars (never into repo files). | — |
| H | Integration-layer tests faking the SDK's HttpClient seam. | — |

Idempotency design (YOUR CALL — see §5 rows 4/5): the customer's Maxio `reference` = eShop username;
the subscription's Maxio `reference` = `eshop:{username}:{productHandle}`. Subscribe = read-by-reference-first,
create-on-404, guarded by an in-process per-username `SemaphoreSlim` so a double-click serializes and the
second call observes the first's result.

## 2. CONTRACT SHEET

> ⚠ Signatures below are generated code, copied verbatim; every parameter name is the literal C#
> identifier (the cancellation-token parameter is literally named `ct`; named args write `ct:`).
> ⚠ Every SDK type is written fully-qualified with the namespace its source path implies (`Models/` →
> `Maxio.Models`, `Models/Enums/` → `Maxio.Models.Enums`, `Errors/` → `Maxio.Errors`, root → `Maxio`,
> `Core/Authentication/Basic/` → `Maxio.Core.Authentication.Basic`, `Servers/` → `Maxio.Servers`), taken
> from that type's own source path — not a neighbour's.

| Op | Signature (verbatim) | Request model + fields used | Response envelope → inner fields read | Error case + accessors | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| `client.ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)` | pass `productFamilyId` = handle; all filters `null`; `includeArchived:false`; `page:1 perPage:200` | `IReadOnlyList<Maxio.Models.ProductResponse>` → `.Product` (`Maxio.Models.Product`): `Id`,`Name`,`Handle`,`Description`,`PriceInCents`,`Interval`,`IntervalUnit`,`ProductPricePointHandle`,`RequireCreditCard`,`ArchivedAt` | **Case A** `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | page-based (no page-size cap documented on row) | map/operations/ProductFamilies.md |
| `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` | query `reference` ← `reference` | `Maxio.Models.CustomerResponse` → `.Customer` (`required`): `Id`,`Reference`,`Email`,`FirstName`,`LastName` | **Case B** `SdkException<RawError>` (404 when absent) | none | map/operations/Customers.md |
| `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — body must pass explicitly | `Maxio.Models.CreateCustomerRequest{ Customer (customer) = Maxio.Models.CreateCustomer{ FirstName(first_name) req, LastName(last_name) req, Email(email) req, Reference(reference) } }` | `Maxio.Models.CustomerResponse` → `.Customer.Id` | **Case A** `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` | none | map/operations/Customers.md |
| `client.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `IReadOnlyList<Maxio.Models.SubscriptionResponse>` → `.Subscription` | **Case B** `SdkException<RawError>` | none | map/operations/Customers.md |
| `client.Subscriptions.FindSubscription` | `FindSubscription(string? reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` — reference must pass explicitly | query `reference` ← `reference` | `Maxio.Models.SubscriptionResponse` → `.Subscription` | **Case A** `SdkException<FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` | none | map/operations/Subscriptions.md |
| `client.Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — body must pass explicitly | `Maxio.Models.CreateSubscriptionRequest{ Subscription (subscription) = Maxio.Models.CreateSubscription{ ProductHandle(product_handle), CustomerReference(customer_reference), Reference(reference) } }` (product_handle OR product_id; customer_reference OR customer_id OR customer_attributes — see `<remarks>` in Api/CreateSubscription source) | `Maxio.Models.SubscriptionResponse` → `.Subscription` | **Case A** `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` | none | map/operations/Subscriptions.md |

Response inner model `Maxio.Models.Subscription` fields the DTO maps: `Id`, `State` (`Maxio.Models.Enums.SubscriptionState`, `.Value`→wire string), `CurrentPeriodEndsAt` (**next billing date** — "when the next regularly scheduled attempted charge will occur"), `NextAssessmentAt`, `ProductPriceInCents`, `CurrentPeriodStartedAt`, `CreatedAt`, `Reference`, nested `Product` (Name/Handle/PriceInCents/Interval/IntervalUnit), nested `Customer`. Source: `Models/Subscription.cs`.

Typed error payloads read in the boundary:
- `Maxio.Models.ErrorListResponse1`: `Errors (errors): IReadOnlyList<string>` (required). Source `Models/ErrorListResponse1.cs`.
- `Maxio.Models.CustomerErrorResponse1`: `Errors (errors): Maxio.Models.AnyOf.Errors1?`. Source `Models/CustomerErrorResponse1.cs` (surfaced via raw fallback string; union not deep-parsed).

Enum rendering: `Maxio.Core.Enum.StringEnum<T>` exposes `Value` (wire string) and `ToString()`==Value. `SubscriptionState` members: `Active`("active"), `Trialing`, `Pending`, `Canceled`, `PastDue`, … Source `Models/Enums/SubscriptionState.cs`, `Core/Enum/TypedEnum.cs`.

Client construction / auth / server (sources: `sdk-map.md` §Getting a client + §Servers & auth, `MaxioClientOptions.cs`, `ServiceCollectionExtensions.cs`, `Servers/ProductionOptions.cs`, `Core/Authentication/Basic/BasicAuthCredentials.cs`):
- DI: `services.AddMaxioClient(options => …)` (extension on `IServiceCollection`, registers `MaxioClient` as **singleton**; builds the options object **once at registration**). Client is `Maxio.MaxioClient`; groups are properties (`client.Subscriptions`, `client.Customers`, `client.ProductFamilies`).
- Auth (US/EU only, Basic): `options.BasicAuth = new Maxio.Core.Authentication.Basic.BasicAuthCredentials{ Username = <API key>, Password = "x" }`. Username is the Chargify API key, password literal `"x"`.
- Environment: `options.Environment = Maxio.Servers.ServerEnvironment.Us` (task env var `MAXIO_ENVIRONMENT` = `US`).
- Site / base URL: default Production/Us template `https://{site}.chargify.com` with `{site}` = `options.Server.Production.Us.Site`. Set `Site` = `Maxio:Subdomain`. If `Maxio:BaseUrl` is non-empty, set `options.Server.Production.Us.BaseUrl` = that value **verbatim** (used as-is; no `{site}` to substitute).

## 3. Trap notes (hazard + consequence + skill; not resolved here)

- **Step C/D–F error boundary — `System.Text.Json.JsonException` reaches the boundary from two directions the `SdkException` ladder does not catch** (drifted 2xx body; non-2xx body not matching its `{Operation}Error` shape destroying the HTTP status). Getting this wrong silently leaks 500s or loses the status. **MUST load maxio-platforms-team:dotnet-error-handling.**
- **Step D/E/F — list/optional-parameter calls mis-bind if written positionally**; many nullable params have no C# default and passing `null` explicitly / using named args is required, or the wrong query is sent. **MUST load maxio-platforms-team:dotnet-calling-endpoints.**
- **Step B/C — building request records and reading `StringEnum`/nested response envelopes**: wire names ≠ C# names, enums aren't C# enums, response payloads sit one level down. Hand-rolling these drifts from the wire. **MUST load maxio-platforms-team:dotnet-models.**
- **Step C — client lifetime & DI**: the `HttpClient`/handler pipeline must be long-lived (via `IHttpClientFactory`), and the options object is captured once at registration. Rebuilding per request or misreading the singleton capture breaks pooling / rotation. **MUST load maxio-platforms-team:dotnet-client-initialization.**
- **Step C — credentials & where secrets load from**: setting credentials at the wrong point, or letting the SDK log env var arm body logging. **MUST load maxio-platforms-team:dotnet-authentication.**
- **Step C — timeout is per-attempt not total; POST is never auto-resent but the injected `Idempotency-Key` is not a real key; `LogRequestBody` does not redact JSON.** Choosing a timeout/retry/logging posture from the knob names alone is wrong. **MUST load maxio-platforms-team:dotnet-configuration-resilience.**
- **Step H — the SDK seam to fake is the `HttpClient` ctor argument**; faking SDK internals gives brittle tests. **MUST load maxio-platforms-team:dotnet-testing.**

## 4. REQUIRED READING (load every one before implementation; contents deliberately not carried here)

| Skill | Governs |
| --- | --- |
| `maxio-platforms-team:dotnet-client-initialization` | Step C — client construction & DI registration |
| `maxio-platforms-team:dotnet-authentication` | Step C — Basic auth credentials & secret sourcing |
| `maxio-platforms-team:dotnet-calling-endpoints` | Steps D/E/F — calling operations, named args |
| `maxio-platforms-team:dotnet-models` | Steps B/C — building request records, reading enums/envelopes |
| `maxio-platforms-team:dotnet-error-handling` | All call sites — try/catch, Case A/B, JsonException boundary |
| `maxio-platforms-team:dotnet-configuration-resilience` | Step C — timeout/retry/logging posture |
| `maxio-platforms-team:dotnet-testing` | Step H — faking the HttpClient seam |

Mandatory hazard rows (verbatim, both directions of `System.Text.Json.JsonException`):
- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — an SDK-exception-only catch ladder lets it escape.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` **while the error object is being constructed**, so it **replaces** the `SdkException` and the HTTP status is destroyed with it.

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `MaxioSettings` validated at startup in `AddMaxioBilling`: throw if `ApiKey` or `Subdomain` is null/blank (and if `BaseUrl` is blank, derive from subdomain — never a blank site). Password is the literal `"x"`, not a credential part. Host refuses to start otherwise — not discovered as a first-call 401. |
| 2 | Secret sourcing & rotation | Secrets come from **.NET user-secrets** (loaded there from env vars; never written to repo files). `AddMaxioClient` builds the options once at registration and captures them in the singleton `MaxioClient`, so a rotated key needs a process restart. Acceptable for this reference app; documented. |
| 3 | Total timeout budget | Each outbound call is wrapped with a `CancellationToken` deadline (default 30 s) enforced in `MaxioBillingService`, because the SDK `Timeout` is per-attempt. The token bounds the whole call including retries. |
| 4 | Write-retry ownership | `POST` (CreateCustomer, CreateSubscription) is **never auto-resent** by the SDK (default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS). Reads may retry safely. No PUT in scope. YOUR CALL — not in the map: leave SDK retry at default; do not add POST to the retry set. |
| 5 | Idempotency & ambiguous writes | No REAL caller-supplied idempotency key exists on CreateCustomer/CreateSubscription (map rows show no such parameter; the injected `Idempotency-Key` header is `Guid.NewGuid()` and is not one). **Reconciliation path**: deterministic Maxio `reference` values (customer = username; subscription = `eshop:{username}:{productHandle}`) + read-by-reference-before-create + an in-process per-username `SemaphoreSlim` serializing a user's subscribe calls. This makes a double-click safe within one process; cross-process dedup relies on the deterministic reference for reconciliation. YOUR CALL — not in the map. |
| 6 | Observability | `MaxioBillingService` logs at Information (subscribe start/outcome: username, plan handle, resulting subscription id + state) and Warning/Error on provider failures, including the provider error strings from `ErrorListResponse1.Errors` / `RawError.ReadAsString()`. SDK `LogRequestBody` left **off**. `LoggerFactory` supplied by DI (via `AddMaxioClient`). |
| 7 | Sensitive data | Scope request models (`CreateCustomer`, `CreateSubscription`) carry name/email + our own reference — no card/bank data (payment method not required; no `credit_card_attributes` sent). Still: `LogRequestBody` stays **off** and `LoggerFactory` is set explicitly (via `AddMaxioClient` DI wiring) so `MAXIOCLIENT_LOG` cannot switch body logging on from outside the code. Our own logs never echo request bodies. |
| 8 | Environment selection | One server group in scope: `Production`. Env = `Us` (`{site}.chargify.com`). Deployment sets `Maxio:Subdomain` (+ optional `Maxio:BaseUrl` verbatim override). Sandbox isolation: the Maxio **sandbox** is a distinct site subdomain supplied by config — test traffic never reaches a live site because the subdomain/BaseUrl points only at the sandbox. `Ebb`/`Oauth` groups unused. |

## 6. Assumptions & Blockers

- **Assumption (minor):** "next-billing-date" ⇒ `Subscription.CurrentPeriodEndsAt` (source doc: the timestamp of the next scheduled charge). Proceed.
- **Assumption (minor):** plans = non-archived products in the configured product family; the metered `api-call` component is not a plan and is out of scope for the subscribe flow. Proceed.
- **Assumption (minor):** subscribe defaults to the family's Pro plan handle when the request omits a plan; any seeded plan handle may be passed. Proceed.
- No blockers: every capability needed is exposed by the SDK (list-products-for-family, read/create customer, find/create subscription, list customer subscriptions).

## 7. Source labels

Every operation row cites its map page; enum/model shapes cite their declaring source file; idempotency,
retry, and timeout application are labelled `YOUR CALL — not in the map`. No `UNVERIFIED` rows: all
contract facts resolved from the map and SDK source this session.
