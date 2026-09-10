# Maxio Advanced Billing integration plan — eShopOnWeb subscription billing

Additive, parallel capability: logged-in shopper browses plans, subscribes, sees their
subscriptions. Maxio Advanced Billing is the system of record. Exposed as JWT-authenticated
HTTP endpoints on `src/PublicApi`. Maxio is reached **only** through the maxio-platforms-team
plugin's .NET SDK (`MaxioAdvancedBilling`).

## 1. Scope & sequence

Layering follows eShopOnWeb: interface + DTOs in `ApplicationCore`, SDK-backed implementation
in `Infrastructure`, endpoints in `PublicApi`.

1. **Reference the SDK.** SDK is source-only (not on NuGet). Packed to a local `.nupkg`
   (`MaxioAdvancedBilling.1.0.0`), vendored under `libs/`, exposed via repo `nuget.config`
   local source, referenced from `Infrastructure` via CPM (`Directory.Packages.props` +
   `PackageReference`).
2. **Settings + fail-fast DI** (`Infrastructure/Maxio`): bind `Maxio:` section to `MaxioSettings`
   (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl`); register the SDK client via
   `services.AddMaxioAdvancedBillingClient(...)`; refuse to start if any required key is blank.
3. **`ISubscriptionBillingService`** (ApplicationCore interface) implemented by
   `MaxioSubscriptionBillingService` (Infrastructure):
   - `GetPlansAsync` → resolve family id by handle (`ProductFamilies.ListProductFamilies`),
     then `ProductFamilies.ListProductsForProductFamily(familyId,…)`; map non-archived products.
   - `SubscribeAsync(userKey, email, name, planHandle)` → ensure customer (idempotent), pre-check
     existing live subscription to that product (idempotent), else `Subscriptions.CreateSubscription`.
   - `GetMySubscriptionsAsync(userKey)` → resolve customer by reference, then
     `Customers.ListCustomerSubscriptions(customerId)`.
   - Ensure-customer: `Customers.ReadCustomerByReference(ref)`; on 404 create via
     `Customers.CreateCustomer`; on 422 (duplicate reference from a double-click race) re-read.
4. **PublicApi endpoints** (`SubscriptionEndpoints`, MinimalApi.Endpoint `IEndpoint` style, JWT):
   - `GET  /api/subscription-plans`  → plans (anonymous identity not required to browse, but
     endpoint requires auth per task: JWT).
   - `POST /api/subscriptions`       → subscribe; identity from token (`ClaimTypes.Name`).
   - `GET  /api/my-subscriptions`    → caller's subscriptions.
5. **Tests** (`tests/…`) for the service mapping/idempotency using the SDK's HttpClient seam.

No capability is invented: every Maxio touch is a listed operation below.

## 2. CONTRACT SHEET

> ⚠ **Signatures below are generated code, verbatim.** Every parameter name is the literal C#
> identifier — the cancellation-token parameter is named `ct`, so named args write `ct:`.
> ⚠ **Every SDK type is written fully-qualified by the namespace its source path implies**
> (`Models/` → `MaxioAdvancedBilling.Models`; `Models/Enums/` → `…Models.Enums`;
> `Errors/` → `…Errors`; root → `MaxioAdvancedBilling`; `Api/` → `…Api`), taken from the path
> the map gives for THAT type.

| Op | Signature | Request → fields used | Response → fields read | Error case | Source |
|----|-----------|----------------------|------------------------|-----------|--------|
| `client.ProductFamilies.ListProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, RequestOptions? requestOptions = null, CancellationToken ct = default)` | pass all filters `null` | `IReadOnlyList<ProductFamilyResponse>`; each `.ProductFamily` (`ProductFamily?`): `Id (id):int?`, `Handle (handle):string?`, `Name (name):string?` | **Case B** `SdkException<RawError>` | map/operations/ProductFamilies.md; Models/ProductFamilyResponse.cs; Models/ProductFamily.cs |
| `client.ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `productFamilyId` = resolved numeric family id (as string); `includeArchived=false`; rest `null`; page through `perPage=200` | `IReadOnlyList<ProductResponse>`; each `.Product` (required `Product`): `Id`, `Name (name)`, `Handle (handle)`, `Description (description)`, `PriceInCents (price_in_cents):long?`, `Interval (interval):int?`, `IntervalUnit (interval_unit):IntervalUnit?`, `RequireCreditCard (require_credit_card):bool?`, `ArchivedAt (archived_at):DateTimeOffset?` | **Case A** `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | map/operations/ProductFamilies.md; Models/ProductResponse.cs; Models/Product.cs |
| `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `reference` = per-user stable key | `CustomerResponse` → required `.Customer`: `Id (id):int?`, `Reference (reference):string?`, `Email`, `FirstName`, `LastName` | **Case B** `SdkException<RawError>` (404 when absent → `RawError.StatusCode`) | map/operations/Customers.md; Models/CustomerResponse.cs; Models/Customer.cs |
| `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateCustomerRequest{ Customer = CreateCustomer{ required FirstName, LastName, Email; Reference } }` (PII — see readiness row 7) | `CustomerResponse` → `.Customer.Id` | **Case A** `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError` [fallback] | map/operations/Customers.md; Models/CreateCustomerRequest.cs; Models/CreateCustomer.cs |
| `client.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `customerId` = resolved id | `IReadOnlyList<SubscriptionResponse>`; each `.Subscription` | **Case B** `SdkException<RawError>` | map/operations/Customers.md; Models/SubscriptionResponse.cs |
| `client.Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateSubscriptionRequest{ required Subscription = CreateSubscription{ ProductHandle (product_handle), CustomerId (customer_id) } }` — no card fields (payment not required) | `SubscriptionResponse` → `.Subscription` | **Case A** `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | map/operations/Subscriptions.md; Models/CreateSubscriptionRequest.cs; Models/CreateSubscription.cs |

**`Subscription` (Models/Subscription.cs)** fields read: `Id (id):int?`, `State (state):SubscriptionState?`,
`ProductPriceInCents (product_price_in_cents):long?`, `CurrentPeriodEndsAt (current_period_ends_at):DateTimeOffset?`,
`NextAssessmentAt (next_assessment_at):DateTimeOffset?`, `Product (product):Product?`, `Customer (customer):Customer?`.
Next-billing date to confirm = `NextAssessmentAt ?? CurrentPeriodEndsAt`.

**Enums** (`Models/Enums/…`, `StringEnum<T>` — read wire value via `.Value` (string), from
`Core/Enum/TypedEnum.cs`; build via static members / `FromValue("wire")`):
- `SubscriptionState` (Models/Enums/SubscriptionState.cs): `Active="active"`, `Trialing="trialing"`,
  `Pending="pending"`, `PastDue="past_due"`, `Canceled="canceled"`, `Expired="expired"`,
  `AwaitingSignup="awaiting_signup"`, … Live (grants access): `active`, `trialing`, `assessing`, `pending`.
- `IntervalUnit` (Models/Enums/IntervalUnit.cs) — read `.Value` only (month/day); not built by us.
- `CollectionMethod` (Models/Enums/CollectionMethod.cs): `Automatic="automatic"` — **not sent**
  (leave `PaymentCollectionMethod` null; plans require no payment method, default applies).

**Client construction / auth / server node** (sdk-map.md · Servers & auth; ServerOptions.cs;
Servers/ProductionOptions.cs; ServiceCollectionExtensions.cs):
- `services.AddMaxioAdvancedBillingClient(options => …)` registers a **singleton** client built
  from `IHttpClientFactory`; options built **once at registration**.
- Auth: **Basic** — `options.BasicAuth = new BasicAuthCredentials { Username = <ApiKey>, Password = "x" }`
  (username = Chargify API key, password literal `"x"`; Basic valid for US/EU).
- Environment: `options.Environment = ServerEnvironment.Us` (namespace `MaxioAdvancedBilling.Servers`).
- Base URL: if `Maxio:BaseUrl` set → `options.Server.Production.Us.BaseUrl = <BaseUrl>` verbatim;
  else → `options.Server.Production.Us.Site = <Subdomain>` (template `https://{site}.chargify.com`).
- `BasicAuthCredentials` namespace: confirm at impl (dotnet-authentication) — likely
  `MaxioAdvancedBilling.Core.Authentication` / `.Http`; take from source, not memory.

## 3. Trap notes (name the hazard; do not resolve — load the skill)

- **Client lifetime / HttpClient ownership** when registering the singleton and any custom HttpClient:
  the signature won't show what must be long-lived vs per-request. → **MUST load dotnet-client-initialization**.
- **Where/when to set `BasicAuth` and the exact credentials type + namespace** (username=key,
  password="x"): binding the credential wrong sends no auth and yields a silent 401. → **MUST load dotnet-authentication**.
- **List ops must be called with named arguments** — many optional params have no C# default and
  positional calls mis-bind; and no real idempotency key exists on these writes. → **MUST load dotnet-calling-endpoints**.
- **Enums are `StringEnum<T>` not C# enums; response wrappers nest one level; extension-data bags**
  — building/reading the wrong way compiles yet misbehaves. → **MUST load dotnet-models**.
- **Error boundary**: which exception types actually reach catch, Case A vs B, and the two
  `JsonException` directions (see REQUIRED READING). → **MUST load dotnet-error-handling**.
- **`Timeout` is per-attempt not total; retries gate on HTTP method; `LogRequestBody` never redacts;
  base-URL/server selection** — the option names hide all of this. → **MUST load dotnet-configuration-resilience**.
- **Which seam to fake (the `HttpClient` ctor arg) and asserting real behaviour** when testing the
  service. → **MUST load dotnet-testing**.

## 4. REQUIRED READING (load ALL before implementation; sheet does not carry their contents)

| Skill | Governs |
|-------|---------|
| `maxio-platforms-team:dotnet-client-initialization` | SDK client construction + DI singleton registration |
| `maxio-platforms-team:dotnet-authentication` | Setting `BasicAuth` (key / `"x"`) correctly |
| `maxio-platforms-team:dotnet-calling-endpoints` | Calling list/create ops; named args; idempotency-key reality |
| `maxio-platforms-team:dotnet-models` | Building requests, reading `StringEnum`, nested response envelopes |
| `maxio-platforms-team:dotnet-error-handling` | try/catch around every SDK call; Case A/B; JSON boundary |
| `maxio-platforms-team:dotnet-configuration-resilience` | Timeout budget, retry method-gating, base-URL, logging |
| `maxio-platforms-team:dotnet-testing` | Faking the HttpClient seam in service tests |

Mandatory hazard rows (`System.Text.Json.JsonException` reaches the boundary from two directions,
handled oppositely):
- A drifted/malformed **2xx** body (e.g. a missing `required` member) surfaces as a `JsonException`
  from deserialization, **not** an `SdkException` — an SDK-exception-only catch ladder lets it escape.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` **while the error object is being constructed**, so it **replaces** the
  `SdkException` and the HTTP status is destroyed with it.

## 5. PRODUCTION READINESS

| # | Concern | Decision |
|---|---------|----------|
| 1 | Credential fail-fast | `MaxioSettings` bound from `Maxio:`; `AddMaxioSubscriptionBilling` throws `InvalidOperationException` **during service registration** (before the app serves anything) if `ApiKey`, `Subdomain`, or `ProductFamilyHandle` is null/whitespace — each checked individually (blank ≠ missing), message names the config key, never the value. `BaseUrl` optional. (Implemented as an explicit guard rather than `ValidateDataAnnotations`/`ValidateOnStart` to avoid adding the `Microsoft.Extensions.Options.DataAnnotations` dependency; the effect — refuse to boot without credentials — is the same.) **Verified:** app boots with secrets present; unit + live flows green. |
| 2 | Secret sourcing & rotation | Values come from env vars → loaded into **.NET user-secrets** under `Maxio:` (never in repo). SDK options are built **once at registration** and captured in the singleton → a rotated key takes effect only on process restart. Acceptable for this sandbox integration; documented in code + verify guide. |
| 3 | Total timeout budget | SDK `Timeout` is **per-attempt**; the caller-visible bound is a `CancellationToken` deadline. Service links the incoming request token with a bounded `CancellationTokenSource` (default 30s) and passes it as `ct:` to every op, so a hung retryable GET cannot exceed the budget. → dotnet-configuration-resilience. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS. Our writes are **POST** (`CreateCustomer`, `CreateSubscription`) → never auto-resent by the SDK (good: no silent duplicate writes). GET lookups may retry (idempotent, safe). Defaults kept. |
| 5 | Idempotency & ambiguous writes | Neither write takes a caller idempotency key (the injected `Idempotency-Key: Guid.NewGuid()` header is **not** one). **Customer**: reconciled by unique `reference` — lookup-before-create, and on 422 duplicate-reference (double-click race) re-read by reference. **Subscription**: no key → reconcile by `ListCustomerSubscriptions` filtered to a live subscription for the same product **before** creating; a hit returns the existing subscription (idempotent subscribe). POST is not auto-retried, so ambiguity is confined to genuine concurrent double-clicks, which the pre-check covers. |
| 6 | Observability | Info: resolved customer id, subscription id, plan handle, idempotent-hit flag. Warning/Error: Maxio HTTP status + error body message read via the typed accessor / `RawError.ReadAsString()`. `LogRequestBody` stays **off**. No request body is echoed in our own logs. |
| 7 | Sensitive data | `CreateCustomer` request carries **PII** (email, first/last name). `CreateSubscription` carries **no** card data (payment method not required; we never set `*_attributes`). Therefore `LogRequestBody` stays off **and** `options.Logging.LoggerFactory` is set explicitly (via the DI extension) so `MAXIOADVANCEDBILLINGCLIENT_LOG` cannot force body logging on from outside the code. Our diagnostics never log the request body. |
| 8 | Environment selection | US region only: `ServerEnvironment.Us`, `Production` server group. Base URL = `https://{Subdomain}.chargify.com` (from `Maxio:Subdomain`), or `Maxio:BaseUrl` verbatim override. The SDK has **no** distinct sandbox environment; test traffic is isolated by targeting the **sandbox site** subdomain + sandbox API key only — the build never hard-codes a site and is never pointed at a production site in config. `Ebb`/`Oauth` groups untouched. |

## 6. Assumptions & Blockers

- **User→customer key.** JWT carries `ClaimTypes.Name` = eShop username (email). Used as the Maxio
  customer `reference` (stable across the in-memory-DB restarts, since Maxio — not the local DB — is
  the record of truth). No local userId↔subscription table is needed, which sidesteps the in-memory
  DB persistence caveat. (`YOUR CALL — not in the map`.)
- **Plan = Maxio Product** within the configured product family; price shown from `price_in_cents` +
  `interval`/`interval_unit`. (`YOUR CALL — not in the map`.)
- **Default subscribe target** when the request omits a plan: `Maxio:ProductFamilyHandle`'s products
  are all valid targets; the request must name a `planHandle`. If omitted, the endpoint returns 400
  rather than guessing. (`YOUR CALL — not in the map`.)
- Family-id resolution: `ListProductsForProductFamily` takes `productFamilyId` as string; to avoid
  depending on handle-acceptance there (unverified), resolve the numeric id from `ListProductFamilies`
  by matching `Handle`. No blocker.
- No blockers: every required capability (list families, list products, read/create customer, list
  customer subscriptions, create subscription) exists as a mapped operation.
