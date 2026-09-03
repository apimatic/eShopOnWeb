# maxio-plan.md — Maxio subscription billing for eShopOnWeb

Additive, parallel capability on `src/PublicApi` (JWT). Hero flow: a logged-in shopper lists
plans, subscribes, and sees the subscription in their account. Maxio Advanced Billing is the
system of record. All SDK facts below come from the SDK map / named source files, not memory.

## 1. Scope & sequence

| # | Step | Layer | Maxio operations used |
|---|------|-------|-----------------------|
| 1 | Vendor SDK source into repo as a buildable project; opt it out of central package mgmt | `src/Maxio/` | — |
| 2 | `MaxioSettings` options + fail-fast validation; DI-register `MaxioClient` (Basic auth, US env, subdomain/BaseUrl) | Infrastructure | `AddMaxioClient` |
| 3 | Domain abstraction `ISubscriptionBillingService` + plain DTOs (no SDK types leak out) | ApplicationCore | — |
| 4 | `MaxioSubscriptionBillingService` implementing the abstraction + SDK→domain mapping + error translation | Infrastructure | see below |
| 5 | List plans | endpoint→service | `ProductFamilies.ListProductsForProductFamily` |
| 6 | Subscribe (ensure customer idempotently, dedupe subscription, create) | endpoint→service | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Customers.ListCustomerSubscriptions`, `Subscriptions.CreateSubscription` |
| 7 | My subscriptions | endpoint→service | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |
| 8 | Three `IEndpoint` endpoints on PublicApi under `/api/` | PublicApi | — |
| 9 | Integration tests (fake the SDK's `HttpClient` seam) | tests | — |

Endpoints (PublicApi `IEndpoint` convention, `AddRoute` + minimal-api handler):
`GET /api/subscription-plans` · `POST /api/subscriptions` · `GET /api/my-subscriptions`.
Caller identity = JWT `ClaimTypes.Name` (eShop username/email), used as the Maxio customer
`reference` (stable idempotency anchor) and email.

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, verbatim. Every parameter name is the literal C#
> identifier; the cancellation-token parameter is named `ct` (named args write `ct:`).
> ⚠ Every SDK type is written fully-qualified with the namespace its source path implies
> (taken from that type's own map/source path, not a neighbour's).

Namespaces: client/options `Maxio`; `ServerEnvironment` `Maxio.Servers`; `BasicAuthCredentials`
`Maxio.Core.Authentication.Basic`; controllers `Maxio.Api`; records `Maxio.Models`; enums
`Maxio.Models.Enums`; errors `Maxio.Errors`; `SdkException<T>` `Maxio.Core.Exceptions`;
`RawError` `Maxio.Core.ErrorResponse`.

| Operation (controller.method) | Signature (verbatim) | Request model → fields used | Response envelope → fields read | Error case + accessors | Pagination | Source |
|---|---|---|---|---|---|---|
| `ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `productFamilyId` = **`"handle:" + familyHandle`** (per the method's `<param>` doc: "Either the product family's id or its handle prefixed with `handle:`"; a bare handle 404s). Returns **`IReadOnlyList<ProductResponse>`** — unwrap each `.Product`. Pass the 7 middle nullables `null`; `includeArchived: false` | `ProductResponse.Product`: `Handle`,`Name`,`Description`,`PriceInCents`,`Interval`,`IntervalUnit`,`Id`,`ArchivedAt` | **Case A** `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` | none | map/operations/ProductFamilies.md; Api/ProductFamilies.cs `<param>`; Models/Product.cs |
| `Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `reference` = eShop username | `CustomerResponse.Customer` (required): `Id`,`Reference`,`Email`,`FirstName`,`LastName` | **Case B** `SdkException<RawError>` (404 ⇒ not found) | none | map/operations/Customers.md; Models/CustomerResponse.cs, Customer.cs |
| `Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateCustomerRequest.Customer (customer, required)` = `CreateCustomer{ FirstName(required), LastName(required), Email(required), Reference }` | `CustomerResponse.Customer`: `Id` | **Case A** `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError` | none | map/operations/Customers.md; Models/CreateCustomerRequest.cs, CreateCustomer.cs |
| `Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `customerId` | `IReadOnlyList<SubscriptionResponse>`; read each `.Subscription` | **Case B** `SdkException<RawError>` | none | map/operations/Customers.md; Models/SubscriptionResponse.cs |
| `Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateSubscriptionRequest.Subscription (subscription, required)` = `CreateSubscription{ ProductHandle, CustomerId, PaymentCollectionMethod, Reference }`. **`PaymentCollectionMethod = CollectionMethod.FromValue("remittance")`** is required so signup bills by invoice instead of auto-charging a card — a payment-method-not-required plan with `automatic` (the default) still 422s "No payment method was on file for the $299.00 balance". | `SubscriptionResponse.Subscription` (nullable): fields below | **Case A** `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` | none | map/operations/Subscriptions.md; Models/CreateSubscriptionRequest.cs, CreateSubscription.cs; Models/Enums/CollectionMethod.cs |

`SubscriptionResponse.Subscription` (`Models/Subscription.cs`) fields the integration reads:
`Id:int?` · `State:SubscriptionState?` · `ProductPriceInCents:long?` · `CurrentPeriodEndsAt:DateTimeOffset?`
· `NextAssessmentAt:DateTimeOffset?` · `CurrentPeriodStartedAt:DateTimeOffset?` · `ActivatedAt` ·
`Reference:string?` · nested `Product:Product?` (Handle/Name/PriceInCents), `Customer:Customer?`.

`Product` (`Models/Product.cs`): `Id:int?`,`Name:string?`,`Handle:string?`,`Description:string?`,
`PriceInCents:long?`,`Interval:int?`,`IntervalUnit:IntervalUnit?`,`ArchivedAt:DateTimeOffset?`.

Enums (build with static members / `.FromValue("wire")`; read wire string via `.Value`):
- `SubscriptionState : StringEnum<SubscriptionState>` (`Models/Enums/SubscriptionState.cs`) — live: `active`,`assessing`,`pending`,`trialing`,`paused`; problem: `past_due`,`soft_failure`,`unpaid`; EOL: `canceled`,`expired`,`failed_to_create`,`on_hold`,`suspended`,`trial_ended`,`awaiting_signup`. "Live/active" dedupe set = {`active`,`trialing`,`assessing`,`pending`,`paused`}.
- `IntervalUnit` (`Models/Enums/IntervalUnit.cs`) — `.Value` gives `month`/`day`. Read wire via `.Value`; do not assume members.

Client construction / auth / server (sources: `MaxioClient.cs`, `MaxioClientOptions.cs`, `ServiceCollectionExtensions.cs`, `Servers/ProductionOptions.cs`, `Core/Authentication/Basic/BasicAuthCredentials.cs`, sdk-map.md *Servers & auth*):
- DI: registered manually over a **named** `HttpClient` (`"Maxio"`) as a **singleton** `MaxioClient` (implementation refinement over `AddMaxioClient`, to scope the pipeline, set `PooledConnectionLifetime` + a per-attempt `Retry.Timeout`, and assign `LoggerFactory` explicitly). Options object built **once at registration**.
- Auth: `options.BasicAuth = new BasicAuthCredentials { Username = <ApiKey>, Password = "x" }`. (Basic works only US/EU; `password` is literally `x`.)
- Environment: `options.Environment = ServerEnvironment.Us` (sandbox site is on chargify.com).
- Base URL: default template `https://{site}.chargify.com`. Set `options.Server.Production.Us.Site = <Subdomain>`. If `Maxio:BaseUrl` is non-blank, set `options.Server.Production.Us.BaseUrl = <BaseUrl>` **verbatim** instead (override, may contain no `{site}`).

## 3. Trap notes (hazard + consequence; resolve by loading the named skill)

- **[Step 2] Retried verbs / write-retry ownership.** `CreateCustomer`/`CreateSubscription` are POSTs; whether the SDK ever resends them (and what a hung call costs) shapes idempotency and timeout. Do not assume the default set from memory. **MUST load `maxio-platforms-team:dotnet-configuration-resilience`.**
- **[Step 2] Timeout is not a total budget.** The caller-visible ceiling on a subscribe call is not the knob's face value once retries are in play; only a deadline bounds the whole call. **MUST load `maxio-platforms-team:dotnet-configuration-resilience`.**
- **[Step 2] Secret logging posture.** Request bodies can be logged unredacted, and an env var can arm body logging with no code change; customer PII (email/name) is in these bodies. **MUST load `maxio-platforms-team:dotnet-configuration-resilience`.**
- **[Step 2/4] Singleton captures options once ⇒ rotated key needs a restart.** Consequence for secret rotation. **MUST load `maxio-platforms-team:dotnet-client-initialization`.**
- **[Step 4] Basic-auth wiring + 401 semantics.** Exact place to set credentials and why a 401 is a config failure not a data failure. **MUST load `maxio-platforms-team:dotnet-authentication`.**
- **[Step 5/6/7] List ops need named arguments.** The list ops have many optional params with no C# default that mis-bind positionally; and the injected `Idempotency-Key` header is not a real key. **MUST load `maxio-platforms-team:dotnet-calling-endpoints`.**
- **[Step 4] Enum & model construction.** `StringEnum` is not a C# enum; nested required models must be fully set; unknown fields are retained. **MUST load `maxio-platforms-team:dotnet-models`.**
- **[Step 4] Error boundary — Case A vs B, and JsonException from two directions.** How to read typed vs raw errors and status safely. **MUST load `maxio-platforms-team:dotnet-error-handling`.**
- **[Step 9] SDK test seam.** The `HttpClient` ctor arg is the seam; match the project's xunit/NSubstitute style. **MUST load `maxio-platforms-team:dotnet-testing`.**

## 4. REQUIRED READING (load all before implementing; this sheet does NOT carry their contents)

| Skill | Governs |
|---|---|
| `maxio-platforms-team:dotnet-client-initialization` | Step 2/4 — client build, HttpClient lifetime, DI singleton |
| `maxio-platforms-team:dotnet-authentication` | Step 2/4 — Basic-auth credentials, 401 |
| `maxio-platforms-team:dotnet-calling-endpoints` | Step 5/6/7 — calling ops, named args |
| `maxio-platforms-team:dotnet-models` | Step 4 — request/response models, enums |
| `maxio-platforms-team:dotnet-error-handling` | Step 4/6 — try/catch boundary, Case A/B |
| `maxio-platforms-team:dotnet-configuration-resilience` | Step 2 — retries, timeout, logging/secrets |
| `maxio-platforms-team:dotnet-testing` | Step 9 — SDK test seam |

Mandatory hazard rows (both, verbatim): a drifted/malformed **2xx** body (a missing `required`
member) surfaces as `System.Text.Json.JsonException` from **deserialization**, NOT as
`SdkException` — an SDK-exception-only catch ladder lets it escape. A **non-2xx** body that does
not match its operation's generated `{Operation}Error` shape throws `JsonException` **while the
error object is being constructed**, so it **replaces** the `SdkException` and the HTTP status is
destroyed with it. The error boundary must also catch `JsonException` (and a generic fallback).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
|---|---------|----------|
| 1 | Credential fail-fast | `MaxioSettings` bound from `Maxio:` section. `AddMaxioBilling` throws at startup (before host runs) if `ApiKey`, `Subdomain`, or `ProductFamilyHandle` is null/whitespace — **each part checked** (a blank part ≠ missing). `BaseUrl` optional. Surfaces as startup failure, not a first-call 401. |
| 2 | Secret sourcing & rotation | Secrets come from **.NET user-secrets** (`Maxio:ApiKey` etc.), never from repo files. Options built once at `AddMaxioClient` registration and captured in the singleton ⇒ a rotated API key takes effect only on process restart. Rotation-without-restart is out of scope for this reference app; documented in code + verify guide. |
| 3 | Total timeout budget | Per-call deadline enforced by a `CancellationTokenSource` (default 100s) created per service call and passed as `ct:`, because SDK `Retry.Timeout` is **per attempt** not total. Value from `Maxio:TimeoutSeconds` (default 100). Confirm retry/timeout semantics via `dotnet-configuration-resilience` before finalizing. |
| 4 | Write-retry ownership | Reads (`ListProductsForProductFamily`, `ReadCustomerByReference`, `ListCustomerSubscriptions`) are GETs → SDK-retryable. Writes `CreateCustomer`/`CreateSubscription` are POST → **not** resent by the SDK default; app owns their retry/reconciliation (see #5). Verify the default retry-method set via `dotnet-configuration-resilience`. |
| 5 | Idempotency & ambiguous writes | **No caller-supplied idempotency key** exists on `CreateCustomer`/`CreateSubscription` (map rows show none; the injected `Idempotency-Key` header is per-call GUID, not a key). Reconciliation instead: (a) customer keyed by unique `reference`=username — `ReadCustomerByReference` first; on create race a duplicate-reference **422** is caught and the customer re-read. (b) subscription — before create, `ListCustomerSubscriptions` is scanned for a live subscription to the target product handle and returned if present; a per-username in-process `SemaphoreSlim` serialces a user's concurrent subscribe calls (single-host deployment) so a double-click cannot create two. Cross-process races are out of scope (documented). |
| 6 | Observability | `ILogger` logs at Information: customer resolved/created (id + reference, **no PII body**), subscription created/reused (subscription id, product handle, state). Warnings on 422/validation. On `RawError`, the provider status + `ReadAsString()` body snippet is logged at Warning/Error for correlation. SDK `LogRequestBody` left **off**. |
| 7 | Sensitive data | Request bodies carry customer **email + name** (`CreateCustomer.cs`). Therefore SDK `LogRequestBody` stays **off** and `options.Logging.LoggerFactory` is **assigned explicitly** (from DI `ILoggerFactory` in `AddMaxioBilling`) so `MAXIOCLIENT_LOG` cannot switch body logging on from outside code. Our own logs never echo request bodies. No card/bank data (payment method not required). |
| 8 | Environment selection | One server group in scope: `Production` on `ServerEnvironment.Us` → `https://{site}.chargify.com` with `{site}`=configured subdomain (from `Maxio:Subdomain`). Basic auth valid only for US/EU. No separate SDK "sandbox environment" exists; test traffic is kept off any live system by the deployment supplying the **sandbox** subdomain/API key via config — never hard-coded. `Maxio:BaseUrl` can point the client at a mock. `MAXIO_ENVIRONMENT` env is `US`; environment fixed to US in code (**YOUR CALL — not in the map**: only these 4 `Maxio:` keys are mandated; US is the sandbox hosting). |

## 6. Assumptions & Blockers

- **Assumption (minor):** "subscription-plans" = the Products in the configured product family
  (`eshop-pro`, `basic-plan`), listed via `ListProductsForProductFamily`. The metered `api-call`
  component is not a plan and is not listed. Proceeding.
- **Assumption (minor):** default subscribe target when the request omits a plan = `eshop-pro`
  (Pro Plan), per task. Request may specify any plan handle in the family.
- **Assumption (minor):** eShop identity ↔ Maxio customer is anchored on the Maxio customer
  `reference` = JWT username; no persisted local mapping (in-memory DB loses data on restart, and
  `reference` lookup makes a local table unnecessary).
- **No Blockers.** Every step maps to an existing SDK operation.

## 7. Source labels

All rows in §2 cite a map page and/or the declaring source file. §5 row 3 (timeout budget), row 5
(in-process lock / dedupe design), row 8 environment-fix = `YOUR CALL — not in the map`
(application decisions). No `UNVERIFIED` rows — all facts resolved from source this session.
