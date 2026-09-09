# Maxio Advanced Billing integration — plan & contract sheet

Additive recurring-subscription billing for eShopOnWeb, with **Maxio Advanced Billing** as the
system of record. Exposed as JWT-authenticated endpoints on `src/PublicApi`. The existing
Catalog→Basket→Order flow is untouched.

## 1. Scope & sequence

Clean-Architecture layering (existing repo convention): SDK dependency lives **only in
Infrastructure**; PublicApi and ApplicationCore never see `MaxioAdvancedBilling.*` types.

1. **Vendor the SDK** (not on NuGet) under `src/Maxio.AdvancedBilling.Sdk/` and add a
   `ProjectReference` from `Infrastructure`. Opt the vendored csproj out of central package
   management (`ManagePackageVersionsCentrally=false`) — the repo root's `Directory.Packages.props`
   forbids per-reference `Version=`, which the SDK csproj uses.
2. **ApplicationCore** (SDK-free): `ISubscriptionBillingService` + plain DTOs
   (`SubscriptionPlanDto`, `CustomerSubscriptionDto`, `SubscribeRequest`, `SubscribeResult`) +
   `SubscriptionBillingException` (carries an optional HTTP status + caller-safe message).
3. **Infrastructure/Billing/Maxio**: `MaxioSettings` (bound + fail-fast), `MaxioClientRegistration`
   (`AddMaxioBilling` — named HttpClient, timeout, server/env, Basic auth, explicit LoggerFactory),
   `MaxioBillingService : ISubscriptionBillingService` (maps SDK ↔ DTOs, error boundary, per-call
   total-deadline `CancellationToken`).
4. **PublicApi/SubscriptionEndpoints** (MinimalApi.Endpoint `IEndpoint` style, matching
   `CatalogItemEndpoints`): `GET /api/subscription-plans`, `POST /api/subscriptions`,
   `GET /api/my-subscriptions`. Each resolves the caller from the JWT (`ClaimTypes.Name` = email),
   looks up the `ApplicationUser` (stable `Id` GUID) via `UserManager`, and delegates to the service.
5. **Program.cs (PublicApi)**: call `AddMaxioBilling(configuration)`; wire `AddEndpoints` already
   discovers `IEndpoint`s.
6. **Tests** (`tests/UnitTests` or a new billing test file): stub `HttpMessageHandler` behind a real
   `MaxioAdvancedBillingClient`, assert mapping + error translation + idempotency behaviour.

**Hero flow — Subscribe (`POST /api/subscriptions`)**: resolve caller → ensure Maxio customer exists
(idempotent by `reference`) → ensure subscription exists for (customer, plan) (idempotent by
subscription `reference`) → return plan/price/state/next-billing-date.

- Ensure customer: `Customers.ReadCustomerByReference(ref)`; on 404 `Customers.CreateCustomer`.
- Ensure subscription: `Subscriptions.FindSubscription(subRef)`; if a live one exists, return it;
  else `Subscriptions.CreateSubscription` with `product_handle` + `customer_id` + that `reference`.
- Plans list: `ProductFamilies.ListProductsForProductFamily("handle:" + familyHandle, …)`.
- My subscriptions: read customer by ref → `Customers.ListCustomerSubscriptions(customerId)`.

A capability the map lacks would be a Blocker (§6). None found — the map covers every step.

## 2. CONTRACT SHEET

> ⚠ **Signatures below are generated code, verbatim.** Every parameter name is the literal C#
> identifier; the cancellation-token parameter is named **`ct`**, so named args write `ct:`.
> Pass every leading nullable param explicitly (as `null`) — many have no C# default.
> ⚠ **Every SDK type is fully-qualified by the namespace its source path implies** (see the
> Namespaces map), taken from the path the map gives for THAT type — not a neighbour's.

Client accessor groups used: `client.Customers`, `client.Subscriptions`, `client.ProductFamilies`.

| Operation | Signature (verbatim) | Request model + fields used | Response envelope → inner fields read | Error case + accessors | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| `Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` | query `reference` ← `reference` | `CustomerResponse` → `.Customer` (`Customer.Id:int?`, `.Reference`, `.Email`, `.FirstName`, `.LastName`) | **Case B** `SdkException<RawError>` (404 when absent → treat as "no customer") | none | map/operations/Customers.md; Models/CustomerResponse.cs; Models/Customer.cs |
| `Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateCustomerRequest{ Customer(required): CreateCustomer{ FirstName(req), LastName(req), Email(req), Reference } }` | `CustomerResponse` → `.Customer.Id` | **Case A** `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | map/operations/Customers.md; Models/CreateCustomerRequest.cs; Models/CreateCustomer.cs; Models/CustomerErrorResponse1.cs |
| `Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | path `customerId` | `IReadOnlyList<SubscriptionResponse>` → each `.Subscription` | **Case B** `SdkException<RawError>` | none | map/operations/Customers.md; Models/SubscriptionResponse.cs |
| `Subscriptions.FindSubscription` | `FindSubscription(string? reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` | query `reference` ← `reference` (**pass explicitly**) | `SubscriptionResponse` → `.Subscription` | **Case A** `SdkException<FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback] | none | map/operations/Subscriptions.md; Models/SubscriptionResponse.cs; Errors/FindSubscriptionError.cs |
| `Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateSubscriptionRequest{ Subscription(required): CreateSubscription{ ProductHandle, CustomerId(int?), Reference, PaymentCollectionMethod=CollectionMethod.Remittance } }` | `SubscriptionResponse` → `.Subscription` (see fields below) | **Case A** `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | map/operations/Subscriptions.md; Models/CreateSubscription.cs; Models/Enums/CollectionMethod.cs; Models/ErrorListResponse1.cs |
| `ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)` | path `productFamilyId` = `"handle:" + familyHandle` (per method remarks: id OR `handle:` prefix). All 8 nullable filters passed `null`; `includeArchived: false` | `IReadOnlyList<ProductResponse>` → each `.Product` | **Case A** `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | none (has `page`/`perPage`; not a `Pageable` — single response) | map/operations/ProductFamilies.md; Api/ProductFamilies.cs (remarks); Models/ProductResponse.cs; Models/Product.cs |

**`Subscription` fields read back** (`Models/Subscription.cs`): `Id:int?`, `State:SubscriptionState?`
(enum), `CurrentPeriodEndsAt:DateTimeOffset?` (**the next-billing date** — "when the next regularly
scheduled attempted charge will occur"), `NextAssessmentAt:DateTimeOffset?`,
`ProductPriceInCents:long?`, `Product:Product?`, `Reference:string?`, `CreatedAt`.

**`Product` fields read back** (`Models/Product.cs`): `Id:int?`, `Name`, `Handle`, `Description`,
`PriceInCents:long?`, `Interval:int?`, `IntervalUnit:IntervalUnit?` (enum),
`ProductPricePointHandle`, `RequireCreditCard:bool?`.

**Enums** (read the wire string with `.Value`, **never** `ToString()`/interpolation — see trap notes):
- `SubscriptionState` (`Models/Enums/SubscriptionState.cs`, `StringEnum`): values incl. `active`,
  `trialing`, `pending`, `canceled`, `expired`, `awaiting_signup`, … Used only for read-back.
- `IntervalUnit` (`Models/Enums/IntervalUnit.cs`, `StringEnum`): `month`/`day`. Read-back only.
- No enum is **built** by this integration (no `FromValue` needed).

**Client construction / auth / server** (sdk-map.md → Getting a client, Servers & auth):
- Ctor: `new MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`.
- Options: `BasicAuth = new BasicAuthCredentials { Username = <API key>, Password = "x" }`
  (`Core/Authentication/Basic`; Servers&auth: username = Chargify API key, password = `x`).
- `Environment = ServerEnvironment.Us` (`MaxioAdvancedBilling.Servers`) — env var `MAXIO_ENVIRONMENT=US`.
- Server group **Production**, `Us`: template `{site}` → `options.Server.Production.Us.Site = <subdomain>`.
- Optional base-URL override: when `Maxio:BaseUrl` is set → `options.Server.Production.Us.BaseUrl = <value>`
  verbatim (a literal URL with no `{placeholders}` is used as-is).
- DI: hand-registered over a **named** HttpClient (own timeout + primary handler
  `PooledConnectionLifetime`), options built once at registration, `Logging.LoggerFactory` set
  explicitly from `ILoggerFactory`.

## 3. Trap notes (name the hazard; the skill resolves it)

- **Client/HttpClient lifetime & singleton stale-DNS.** Reuse one long-lived client + pooled handler;
  the naive singleton caches DNS forever. `MUST load dotnet-client-initialization`.
- **Basic auth is two halves; an unset credential sends NO Authorization header and 401s a layer
  away.** `MUST load dotnet-authentication`.
- **Named args on `ListProductsForProductFamily`** (11 params, 8 leading nullables w/o C# default) —
  positional mis-binds `ct` into `requestOptions`. `MUST load dotnet-calling-endpoints`.
- **Enum read-back: `.Value`, not `ToString()`/interpolation** (record shadows the override → debug
  form leaks into JSON). `MUST load dotnet-models`.
- **Case A vs Case B differ per op; `TryGetRawError` is LAST, not a catch-all; naming a neighbour's
  `{Operation}Error` compiles but never matches.** `MUST load dotnet-error-handling`.
- **`Timeout` is per-attempt not total; a `CancellationToken` deadline is the only whole-call bound;
  `POST` is not resent by default (good — avoids duplicate writes).** `MUST load dotnet-configuration-resilience`.
- **Test seam is the `HttpClient` ctor arg; read the request body inside the handler (disposed after).**
  `MUST load dotnet-testing`.

## 4. REQUIRED READING (load ALL before implementing; sheet omits their contents)

| Skill | Governs |
| --- | --- |
| `maxio-platforms-team:dotnet-client-initialization` | client construction + DI registration (step 3) |
| `maxio-platforms-team:dotnet-authentication` | Basic-auth wiring + startup credential fail-fast (step 3) |
| `maxio-platforms-team:dotnet-calling-endpoints` | all SDK calls, named args (steps 3–4) |
| `maxio-platforms-team:dotnet-models` | request models + enum read-back (step 3) |
| `maxio-platforms-team:dotnet-error-handling` | error boundary — always required (step 3) |
| `maxio-platforms-team:dotnet-configuration-resilience` | timeout budget, retries, logging/PII (step 3) |
| `maxio-platforms-team:dotnet-testing` | integration-layer tests (step 6) |

**Mandatory hazard rows (both directions of `System.Text.Json.JsonException` reach the boundary):**
- A drifted/malformed **2xx** body (missing `required` member) surfaces as `JsonException` from
  deserialization, **not** as `SdkException` — an SDK-exception-only catch ladder lets it escape.
  → the boundary catches `JsonException` and maps to a caller-safe 502 "unprocessable response".
- A **non-2xx** body that does not match the operation's `{Operation}Error` shape throws
  `JsonException` **while the error object is constructed**, replacing the `SdkException` and
  destroying the HTTP status. → same boundary catch; treated as a provider failure (5xx), not absence.

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `MaxioSettings` bound from `Maxio:` with `[Required]` on `ApiKey` & `Subdomain`; `AddOptions().ValidateDataAnnotations().ValidateOnStart()` **plus** an explicit non-blank guard at registration. Host refuses to boot if either is missing/blank. Both Basic halves covered (password is the constant `"x"`, so only the key is a secret). |
| 2 | Secret sourcing & rotation | `Maxio:ApiKey` from **.NET user-secrets** (loaded from env `MAXIO_API_KEY`; never written to any repo file). Options built **once at registration** and captured in the singleton client → a rotated key needs a process restart. Documented; acceptable for this reference app (no hot-rotation requirement). |
| 3 | Total timeout budget | Per-attempt `Retry.Timeout = 15s` **and** named-HttpClient `Timeout = 20s` (hard hang backstop). Whole-call bound = a linked `CancellationToken` (`CancelAfter(30s)` linked to `HttpContext.RequestAborted`) applied in one `Bounded()` helper the service routes every call through. |
| 4 | Write-retry ownership | Writes are `CreateCustomer`/`CreateSubscription` = **POST** → default `HttpMethodsToRetry` (`GET,HEAD,PUT,OPTIONS`) never resends them. No `PUT` in scope. Reads (`GET`) retry normally. Keep the default list unchanged. |
| 5 | Idempotency & ambiguous writes | No caller-supplied idempotency-key param on these ops (the injected `Idempotency-Key` header is NOT one). **Own idempotency via deterministic `reference` keys**: customer `reference = eShop ApplicationUser.Id`; subscription `reference = "{userId}:{productHandle}"`. Both writes are preceded by a read (`ReadCustomerByReference` / `FindSubscription`) that short-circuits duplicates. Race/duplicate reconciliation: on a create that returns 422, re-read by reference and return the existing record if present. Whether Maxio enforces `reference` uniqueness is **UNVERIFIED** (not in the map) → the read-first + re-read-on-422 path is the defensive reconciliation, not an assumption of provider-side uniqueness. |
| 6 | Observability | `ILogger` at Information (customer/subscription ensured, plan counts) and Warning/Error on provider failures; the provider's error text (`RawError.ReadAsString()` / `ErrorListResponse1.Errors`) is logged server-side and a caller-safe message returned. `LogRequestBody` stays **off**, so no unredacted body logging. |
| 7 | Sensitive data | `CreateCustomer` body carries **PII** (email, names). Therefore `LogRequestBody` = off (default) **and** `Logging.LoggerFactory` assigned explicitly at registration → the `MAXIOADVANCEDBILLINGCLIENT_LOG` env var cannot switch body logging on from outside the code. No card/bank data is ever sent (plans require no payment method). |
| 8 | Environment selection | One server group in scope: **Production**, environment **`Us`** (`MAXIO_ENVIRONMENT=US`). Base URL `https://{subdomain}.chargify.com` with `{subdomain}` from `Maxio:Subdomain`, overridable verbatim by `Maxio:BaseUrl`. All traffic targets the configured **sandbox site**; there is no separate "sandbox" ServerEnvironment — isolation is by which subdomain/API-key the deployment supplies, so a prod deployment simply supplies prod values. `Ebb`/`Oauth` groups and the EU/Gateway environments are untouched. |

## 6. Assumptions & Blockers

- **Assumption (minor):** the eShop user's stable identity for the Maxio `reference` is
  `ApplicationUser.Id` (GUID); display name is derived from the email local-part since eShop identity
  stores no first/last name. Email = the user's identity email.
- **Assumption (minor):** "next-billing-date" = `Subscription.CurrentPeriodEndsAt`.
- **Decision (verified live):** these no-payment-method plans still fail an automatic collection
  attempt at signup ("No payment method was on file for the $299.00 balance"). Setting
  `PaymentCollectionMethod = CollectionMethod.Remittance` (invoice-style, no card capture) on
  `CreateSubscription` makes the card-less subscribe succeed with an `active` subscription. This is a
  documented SDK field, not a workaround. `YOUR CALL — not in the map` (application decision).
- **Decision (portability):** default subscribe target when the caller names no plan = the **first
  plan in the family** (catalog-agnostic), overridable by the optional `Maxio:DefaultProductHandle`
  setting. Hardcoding `eshop-pro` was rejected — it would 404 against the different catalog the task
  says the same build must run on. Any seeded plan handle may be passed explicitly in the body.
- **UNVERIFIED (row 5):** Maxio `reference` uniqueness enforcement — handled defensively, never assumed.
- **Blockers:** none. The SDK map covers every operation the hero flow needs.

## 7. Source labels — every contract row cites its map page or declaring file (see §2 Source column);
the two idempotency-uniqueness facts are `UNVERIFIED`; the application-design decisions
(persistence-free reconciliation, reference scheme, deadline budget, endpoint shape) are
`YOUR CALL — not in the map` and decided above against the task.
