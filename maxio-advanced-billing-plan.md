# Maxio Advanced Billing integration plan — eShopOnWeb subscription billing

Adds an **additive, parallel** recurring-subscription capability to eShopOnWeb, with Maxio
Advanced Billing as system of record. Three JWT-authenticated endpoints on `src/PublicApi`:

- `GET  /api/subscription-plans`   → list the plans in the configured product family
- `POST /api/subscriptions`        → subscribe the caller to a plan (idempotent)
- `GET  /api/my-subscriptions`     → list the caller's subscriptions

SDK: `MaxioAdvancedBilling` (netstandard2.0), vendored from source under
`src/MaxioAdvancedBilling.Sdk/` (not on NuGet). Client namespace `MaxioAdvancedBilling`.

---

## 1. Scope & sequence

1. **Vendor the SDK** into `src/MaxioAdvancedBilling.Sdk/` and reference it from
   `Infrastructure` (integration code) — `ApplicationCore` stays SDK-free.
2. **Config + fail-fast + client**: `MaxioSettings` bound from `Maxio:` section; validate at
   startup; register a singleton `MaxioAdvancedBillingClient` over `IHttpClientFactory`
   (Basic auth, US env, site = subdomain, optional verbatim BaseUrl override).
3. **Domain contract** (`ApplicationCore`): `ISubscriptionBillingService` + plain DTOs
   (`SubscriptionPlanDto`, `CustomerSubscriptionDto`, `SubscribeResult`, `SubscriberIdentity`).
4. **Integration service** (`Infrastructure`): `MaxioSubscriptionBillingService`:
   - `GetPlansAsync` → `client.ProductFamilies.ListProductsForProductFamily(familyHandle, …)`
   - `SubscribeAsync` → ensure-customer (`ReadCustomerByReference` → else `CreateCustomer`)
     then guard-then-create (`ListCustomerSubscriptions` match → else `CreateSubscription`)
   - `GetMySubscriptionsAsync` → `ReadCustomerByReference` (404 ⇒ empty) →
     `ListCustomerSubscriptions`
   - error boundary translating `SdkException`/`JsonException` to a domain result/exception.
5. **Endpoints** (`src/PublicApi/SubscriptionEndpoints/`) using `MinimalApi.Endpoint`
   `IEndpoint<…>` convention; identity taken from the JWT (`ClaimTypes.Name`), never the body.
6. **Tests** (`tests/UnitTests`) faking the SDK seam (`HttpClient`) — cover ensure-customer
   idempotency, subscribe guard, and error translation.
7. **Verify**: build, run PublicApi on the assigned port block, exercise all three endpoints
   against the Maxio sandbox with a real JWT.

No capability here is invented: every Maxio call maps to a map operation below.

---

## 2. CONTRACT SHEET

> ⚠ Signatures below are **generated code, verbatim**. Every parameter name is the literal C#
> identifier — named arguments must use them exactly (the cancellation-token parameter is
> literally `ct`, so write `ct:`). Optional/nullable list-params have **no C# default** and
> must be passed explicitly (`null` to skip) — call them with named arguments.
> ⚠ Every SDK type is written **fully-qualified with the namespace its source path implies**
> (`Models/` → `MaxioAdvancedBilling.Models`, `Models/Enums/` → `…Models.Enums`,
> `Errors/` → `…Errors`, root → `MaxioAdvancedBilling`), taken from the path the row gives.

### Operations

| Op | Controller · signature | Request model + fields used | Response envelope → fields read | Error case + accessors | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| List plans | `client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `productFamilyId` = `handle:{ProductFamilyHandle}` (the path param takes a numeric id **or** a `handle:`-prefixed handle, per the op's `<param>` doc — plain handle 404s); all filters `null`; `includeArchived: false`; `page`/`perPage` for paging | `IReadOnlyList<ProductResponse>` → each `.Product` (`required`, non-null): `Id, Name, Handle, Description, PriceInCents, Interval, IntervalUnit (.Value), RequireCreditCard` | **Case A** `SdkException<ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | `page`+`per_page` (no page-size cap given → loop until short page) | map/operations/ProductFamilies.md · Models/Product.cs · Models/Enums/IntervalUnit.cs |
| Find customer by ref | `client.Customers.ReadCustomerByReference(string reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` | query `reference` ← our stable per-user reference | `CustomerResponse` → `.Customer` (`Id, Reference, Email, FirstName, LastName`) | **Case B** `SdkException<RawError>` — `StatusCode` (404 ⇒ not found) | none | map/operations/Customers.md · Models/CustomerResponse.cs · Models/Customer.cs |
| Create customer | `client.Customers.CreateCustomer(CreateCustomerRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateCustomerRequest{ Customer = CreateCustomer{ FirstName*, LastName*, Email*, Reference } }` (`*` = `required`) | `CustomerResponse` → `.Customer.Id` | **Case A** `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError` [fallback] | none | map/operations/Customers.md · Models/CreateCustomerRequest.cs · Models/CreateCustomer.cs · Models/CustomerResponse.cs |
| Create subscription | `client.Subscriptions.CreateSubscription(CreateSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateSubscriptionRequest{ Subscription = CreateSubscription{ ProductHandle, CustomerId, Reference } }` (no field is `required`; `payment_collection_method` left null — plans need no payment method) | `SubscriptionResponse` → `.Subscription` (`Id, State (.Value), Product.Handle/Name, ProductPriceInCents, CurrentPeriodEndsAt, NextAssessmentAt, ActivatedAt, Reference`) | **Case A** `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` [fallback] | none | map/operations/Subscriptions.md · Models/CreateSubscriptionRequest.cs · Models/CreateSubscription.cs · Models/SubscriptionResponse.cs · Models/Subscription.cs |
| List customer subs | `client.Customers.ListCustomerSubscriptions(int customerId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `customerId` = Maxio customer id | `IReadOnlyList<SubscriptionResponse>` → each `.Subscription` | **Case B** `SdkException<RawError>` | none | map/operations/Customers.md · Models/SubscriptionResponse.cs |

`SubscriptionResponse.Subscription` is **nullable** (`Subscription?`) — null-check before reading.
`CustomerResponse.Customer` / `ProductResponse.Product` are `required` (non-null when present).
Enums (`SubscriptionState`, `IntervalUnit`) are `StringEnum<T>` — read the wire string via
`.Value` (or `?.Value`); do **not** assume a C# enum. (source: Core/Enum/TypedEnum.cs → `Value`)

### Client construction / auth / server (source: sdk-map.md *Getting a client* & *Servers & auth*; MaxioAdvancedBillingClientOptions.cs; Servers/ProductionOptions.cs)

- Constructor: `new MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`.
- Auth: **Basic** — `options.BasicAuth = new BasicAuthCredentials { Username = <ApiKey>, Password = "x" }`
  (`Core.Authentication.Basic`). US/EU only; we use US.
- Environment: `options.Environment = ServerEnvironment.Us` (namespace `MaxioAdvancedBilling.Servers`; also the default).
- Site: `options.Server.Production.Us.Site = <Subdomain>` → base `https://{subdomain}.chargify.com`.
- BaseUrl override (optional, verbatim): when `Maxio:BaseUrl` set, `options.Server.Production.Us.BaseUrl = <BaseUrl>`.
- `options.Server` / `.Production` / `.Us` are all default-constructed (non-null) — safe to assign into.

---

## 3. Trap notes (name the hazard + skill; do not resolve here)

- **Client/HttpClient lifetime & DI**: how long the `HttpClient`/handler pipeline must live vs
  the client wrapper decides whether we leak sockets or lose config on rotation. **MUST load
  dotnet-client-initialization** (step 2).
- **Basic-auth wiring order**: when credentials must be set relative to client construction, and
  the silent "no credential sent" failure mode. **MUST load dotnet-authentication** (step 2).
- **List-op named-argument binding**: the many no-C#-default nullable params mis-bind in a
  positional call. **MUST load dotnet-calling-endpoints** (step 4).
- **Model building / enums / extension-data**: `required` init-only members, `StringEnum<T>`
  construction/reading, union/AnyOf accessors on error payloads. **MUST load dotnet-models** (step 4).
- **Error boundary**: which exception types actually reach the catch, Case A vs B accessor
  mechanics, and the two `JsonException` directions (see REQUIRED READING). **MUST load
  dotnet-error-handling** (steps 4, 6).
- **Retry / per-attempt timeout / total budget / logging redaction**: what `Timeout` bounds,
  which verbs the SDK resends, and how the log env var can arm body logging. **MUST load
  dotnet-configuration-resilience** (steps 2, 6).
- **Test seam**: the `HttpClient` ctor arg is the fake seam; match project test style. **MUST
  load dotnet-testing** (step 6).

---

## 4. REQUIRED READING (load all before implementation; contents deliberately not inlined here)

- `maxio-platforms-team:dotnet-client-initialization` — client construction & DI registration (step 2).
- `maxio-platforms-team:dotnet-authentication` — Basic-auth credential wiring (step 2).
- `maxio-platforms-team:dotnet-calling-endpoints` — list/create call shapes, named args (step 4).
- `maxio-platforms-team:dotnet-models` — request models, `required`, `StringEnum<T>`, AnyOf (step 4).
- `maxio-platforms-team:dotnet-error-handling` — try/catch boundary, Case A/B (steps 4 & 6).
- `maxio-platforms-team:dotnet-configuration-resilience` — retries, timeout budget, logging (steps 2 & 6).
- `maxio-platforms-team:dotnet-testing` — faking the SDK seam (step 6).

**Two mandatory `JsonException` hazard rows** (both reach the error boundary; opposite handling):
1. A drifted/malformed **2xx** body (e.g. a missing `required` member) surfaces as
   `System.Text.Json.JsonException` from **deserialization**, **not** as an `SdkException` — an
   SDK-exception-only catch ladder lets it escape.
2. A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
   throws `JsonException` **while the error object is constructed**, so it **replaces** the
   `SdkException` and the HTTP status is destroyed with it.
   → The boundary catches `SdkException<T>` **and** `JsonException` (and a final `Exception`).

---

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `MaxioSettings` bound from `Maxio:` in `AddMaxioSubscriptionBilling`. Host **throws at startup** if `ApiKey`, `Subdomain`, or `ProductFamilyHandle` is null/whitespace (each checked separately — a blank part ≠ a missing one). `BaseUrl` is optional. Not deferred to a first-call 401. |
| 2 | Secret sourcing & rotation | Secrets come from **.NET user-secrets** (loaded from env vars `MAXIO_API_KEY`/`MAXIO_SITE_SUBDOMAIN`/`MAXIO_DEFAULT_PRODUCT_FAMILY`; values never in repo). Options object is built **once at registration** and captured in the singleton client → a rotated key takes effect only on process restart. Documented; acceptable for this reference app (no hot-rotation requirement). |
| 3 | Total timeout budget | SDK `Timeout` is **per attempt** (default 100 s) and GET is retried up to 3× → a hung GET could cost ~400 s. The service passes a **linked `CancellationToken` with a 30 s deadline** as `ct:` on every call, which is the only thing bounding the whole call. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS. Our writes are **POST** (`CreateCustomer`, `CreateSubscription`) → the SDK **never resends** them. Good (no accidental duplicate write); the flip side is row 5. |
| 5 | Idempotency & ambiguous writes | Neither create op takes a real caller idempotency key (no key param in either signature; the generator's `Idempotency-Key: Guid.NewGuid()` header is **not** a key). Reconciliation instead: (a) stable per-user **customer `reference`** + `ReadCustomerByReference` before create; (b) `ListCustomerSubscriptions` matched on product handle + live state before `CreateSubscription`; (c) a per-user in-process `SemaphoreSlim` serialises a user's subscribe calls so a double-click cannot race the guard. **Caveat:** the lock is process-local and the userId↔Maxio mapping is reconstructed from Maxio each call (no local persistence) — correct within a single run, which matches the in-memory-DB constraint; cross-process races reconcile on the next read. A deterministic subscription `Reference` (`eshop-{ref}-{handle}`) is set for traceability. |
| 6 | Observability | Structured `ILogger` logs at Information (subscribe start/created/reused, customer created), Warning (validation/422), Error (unexpected). `LogRequestBody` stays **off** (default). On `RawError` we log `StatusCode` + `ReadAsString()` truncated; on typed errors, the mapped payload message. No secret is logged. |
| 7 | Sensitive data | Request models in scope carry **PII** (email, names) but **no** card/bank data — plans need no payment method, and we never send `credit_card_attributes`/`payment_profile_attributes`. Posture: `LogRequestBody` off **and** `options.Logging.LoggerFactory` assigned **explicitly** (from DI `ILoggerFactory`) so the `MAXIOADVANCEDBILLINGCLIENT_LOG` env var cannot arm body logging from outside the code. Our own logs never echo request bodies. |
| 8 | Environment selection | Groups: `Production` (the only group our ops touch), `Ebb`, `Oauth`. Deployment sets US env → `Production.Us` base `https://{subdomain}.chargify.com`, subdomain from config; `Maxio:BaseUrl` overrides verbatim when set. **Target is the Maxio sandbox site** — Maxio has no separate "sandbox" env value; test isolation is by pointing `Maxio:Subdomain`/`Maxio:ApiKey` at the sandbox site (from config, never in the repo), never a production site. |

---

## 6. Assumptions & Blockers

- **Assumption**: eShop username == email (Identity seeds username = email; JWT carries
  `ClaimTypes.Name` = username). Used as the stable customer `reference` and `email`;
  `first_name`/`last_name` derived from it (`required` by `CreateCustomer`). Minor.
- **Assumption**: `POST /api/subscriptions` accepts `{ "planHandle": "<handle>" }`; if omitted →
  400 (client picks a plan from `GET /api/subscription-plans`). Avoids hard-coding a catalog
  handle. Minor.
- **Assumption**: "live" subscription for the idempotency guard = state not in
  {canceled, expired, failed_to_create}. Minor.
- **Blockers**: none. Every required capability exists in the map.

---

## 7. Source labels

All rows in §2 cite a map page and/or the map-named declaring file (verified this session).
No row is `UNVERIFIED`. Application-design rows (persistence, locking, request contract, plan
defaulting) are §5/§6 `YOUR CALL — not in the map` decisions, weighed against the task.
