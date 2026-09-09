# Maxio Advanced Billing integration plan — eShopOnWeb subscriptions

Additive, parallel recurring-subscription capability on `src/PublicApi` (JWT). Maxio Advanced Billing
is the system of record. Nothing in the existing Catalog/Basket/Order flow changes.

## 1. Scope & sequence

| # | Step | SDK operations used |
| --- | --- | --- |
| 1 | Vendor the SDK source (not on NuGet) into `third-party/maxio-advanced-billing/`, shielded from the repo's central package management, and `ProjectReference` it from `Infrastructure`. | — |
| 2 | `MaxioSettings` options bound from `Maxio:` config section, fail-fast validated at host start. | — |
| 3 | DI: register `MaxioAdvancedBillingClient` (Basic auth, US env, subdomain or BaseUrl override, retry/timeout/logging) + `ISubscriptionBillingService`. | client construction |
| 4 | `GET /api/subscription-plans` → list the plans (Maxio *products*) in the configured product family. | `ProductFamilies.ListProductsForProductFamily` |
| 5 | `POST /api/subscriptions` → ensure a Maxio customer exists for the caller (idempotent), guard against a duplicate active subscription, then enroll. | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Customers.ListCustomerSubscriptions`, `Subscriptions.CreateSubscription` |
| 6 | `GET /api/my-subscriptions` → the caller's subscriptions (empty if no Maxio customer yet). | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |

Idempotency design (§ prod-readiness row 5): the eShop user identity (JWT name, an email) is the Maxio customer
`reference`. Subscribe = read-by-reference → (404 ⇒ create; on create-422 the reference race was lost, so
re-read) → list the customer's subscriptions → if one already exists for the requested product in a
non-terminal state, return it instead of creating a second. So a double-click never yields two customers or
two subscriptions.

## 2. CONTRACT SHEET

> ⚠ Signatures below are generated code, copied verbatim. Every parameter name is the literal C# identifier
> (the cancellation-token parameter is named `ct` → named calls write `ct:`). Optional params with no C#
> default must be passed explicitly (pass `null` to skip).
> ⚠ Every SDK type is written fully-qualified against the namespace its source path implies (`Models/` →
> `MaxioAdvancedBilling.Models`, `Models/Enums/` → `MaxioAdvancedBilling.Models.Enums`,
> `Core/…` per file). Take each type's namespace from the path the map gives for THAT type.

### Operations

| Operation | Signature (verbatim) | Request model → fields used | Response envelope → fields read | Error case | Source |
| --- | --- | --- | --- | --- | --- |
| `client.ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `productFamilyId` = the family **handle** string; rest `null`; `includeArchived: false` | `IReadOnlyList<ProductResponse>`; each `.Product` → `Id, Name, Handle, Description, PriceInCents, Interval, IntervalUnit, ArchivedAt, ProductPricePointHandle` | **A** typed `ListProductsForProductFamilyError`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | map `ProductFamilies.md`; `Models/Product.cs` |
| `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `reference` = eShop username | `CustomerResponse.Customer` → `Id, Reference, Email` | **B** `SdkException<RawError>` (404 ⇒ not found → `RawError.StatusCode`) | map `Customers.md`; `Models/CustomerResponse.cs`, `Models/Customer.cs` |
| `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateCustomerRequest.Customer` = `CreateCustomer { required FirstName, required LastName, required Email, Reference }` | `CustomerResponse.Customer` → `Id` | **A** typed `CreateCustomerError`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError` | map `Customers.md`; `Models/CreateCustomerRequest.cs`, `Models/CreateCustomer.cs` |
| `client.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `customerId` | `IReadOnlyList<SubscriptionResponse>` (note: this op returns bare `SubscriptionResponse` items whose `.Subscription` is nullable — read one level down) → `Subscription { Id, State, Product, CurrentPeriodEndsAt, NextAssessmentAt, ProductPriceInCents, CurrentBillingAmountInCents, Currency, CreatedAt }` | **B** `SdkException<RawError>` | map `Customers.md`; `Models/SubscriptionResponse.cs`, `Models/Subscription.cs` |
| `client.Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateSubscriptionRequest.Subscription` = `CreateSubscription { ProductHandle, CustomerId }` (leave `PaymentCollectionMethod` unset — product config governs; see §6 blocker note) | `SubscriptionResponse.Subscription` → same fields as above | **A** typed `CreateSubscriptionError`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` | map `Subscriptions.md`; `Models/CreateSubscriptionRequest.cs`, `Models/CreateSubscription.cs` |

### Enums (values needed)

| Enum | Access | Values in scope | Source |
| --- | --- | --- | --- |
| `MaxioAdvancedBilling.Models.Enums.SubscriptionState` | `StringEnum`; read `.Value` (string). Terminal set = `canceled, expired, failed_to_create, trial_ended` | non-terminal ⇒ counts as an existing subscription for idempotency | `Models/Enums/SubscriptionState.cs` |
| `MaxioAdvancedBilling.Models.Enums.IntervalUnit` | `StringEnum`; `.Value` → `"month"`/`"day"` | render billing period | `Models/Enums/IntervalUnit.cs` |
| `MaxioAdvancedBilling.Models.Enums.CollectionMethod` | not set on request (default automatic) | — | `Models/Enums/CollectionMethod.cs` |

`StringEnum<T>` exposes `.Value` (string) and `ToString()` → the wire value (`Core/Enum/TypedEnum.cs`, `Core/Enum/StringEnum.cs`).

### Client construction / auth / servers

- Construct: `new MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)`; groups are properties (`client.Customers`, `client.Subscriptions`, `client.ProductFamilies`). DI helper `services.AddMaxioAdvancedBillingClient(options => …)` builds options **once at registration** and registers a **singleton** over `IHttpClientFactory` (`ServiceCollectionExtensions.cs`).
- Auth: HTTP **Basic**. `options.BasicAuth = new BasicAuthCredentials { Username = <API key>, Password = "x" }`. The `<api_key>:x` mapping is the SDK's own documented pattern (SDK `README.md` line 33: `curl -u <api_key>:x`). `BasicAuthCredentials` (`Core/Authentication/Basic/BasicAuthCredentials.cs`) has **required** `Username`, `Password`.
- Server: default `ServerEnvironment.Us` → base `https://{site}.chargify.com` (the sandbox host). Set the `{site}` via `options.Server.Production.Us.Site = <subdomain>`. `Maxio:BaseUrl` override → set `options.Server.Production.Us.BaseUrl` to the literal (no `{site}` placeholder ⇒ used verbatim) (`Servers/ProductionOptions.cs`, `Servers/ServerEnvironment.cs`).

## 3. Trap notes

- Client is a long-lived singleton over `IHttpClientFactory`; do not `new` it per request or wrap a fresh `HttpClient` per call. **MUST load dotnet-client-initialization.**
- Basic auth needs BOTH halves non-blank; an unset credentials property = unauthenticated request that only fails one round-trip later at the provider. **MUST load dotnet-authentication.**
- List/family ops have many optional params with no C# default that mis-bind positionally — call with named arguments. **MUST load dotnet-calling-endpoints.**
- `StringEnum<T>` is not a C# enum; response models keep unknown fields in an extension bag; `required` init-only members must all be set. **MUST load dotnet-models.**
- Two disjoint error shapes here (Case A typed vs Case B `RawError`), and a `JsonException` can reach the boundary from a drifted 2xx body or while building a non-2xx typed error — an SDK-exception-only ladder misses both. **MUST load dotnet-error-handling.**
- `Timeout` is per-attempt not total; `HttpMethodsToRetry` gates every retry so `POST` is never resent by default; `LogRequestBody` logs JSON unredacted and `MAXIOADVANCEDBILLINGCLIENT_LOG` can arm body logging unless `LoggerFactory` is set. **MUST load dotnet-configuration-resilience.**
- The `HttpClient` ctor arg is the test seam; match the repo's xUnit + NSubstitute style. **MUST load dotnet-testing.**

## 4. REQUIRED READING (load all before implementing; contents deliberately not copied here)

| Skill (plugin `maxio-platforms-team`) | Governs |
| --- | --- |
| `maxio-platforms-team:dotnet-client-initialization` | Step 3 client + DI |
| `maxio-platforms-team:dotnet-authentication` | Step 3 Basic-auth credentials + fail-fast |
| `maxio-platforms-team:dotnet-calling-endpoints` | Steps 4–6 operation calls |
| `maxio-platforms-team:dotnet-models` | request/response model + enum mapping |
| `maxio-platforms-team:dotnet-error-handling` | error boundary in `MaxioBillingService` |
| `maxio-platforms-team:dotnet-configuration-resilience` | Step 3 retry/timeout/logging |
| `maxio-platforms-team:dotnet-testing` | integration-layer tests |

Two mandatory `JsonException` hazards, verbatim: (1) a drifted or malformed **2xx** body (missing `required`
member) surfaces as `System.Text.Json.JsonException` from deserialization, **not** as `SdkException`, so an
SDK-exception-only catch ladder lets it escape; (2) a **non-2xx** body that does not match its operation's
generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so it
**replaces** the `SdkException` and the HTTP status is destroyed with it. The boundary must catch
`JsonException` explicitly.

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `MaxioSettings` bound from `Maxio:` with `[Required]` on `ApiKey`, `Subdomain`, `ProductFamilyHandle`; `ValidateOnStart()` in PublicApi startup → host refuses to boot on a missing/blank part. Basic auth's password is the fixed literal `"x"`, so only the key half comes from config. |
| 2 | Secret sourcing & rotation | Secrets come from **.NET user-secrets** (`Maxio:ApiKey` etc.), loaded from the `MAXIO_*` env vars by the operator; never in-repo. DI builds options once at registration and captures them in the singleton ⇒ a rotated key takes effect on process restart (acceptable for this reference app; no hot-rotation requirement). |
| 3 | Total timeout budget | `RetryOptions.Timeout` is per-attempt. Each inbound HTTP request already carries `HttpContext.RequestAborted`; the endpoints thread that `CancellationToken` through to every SDK call so the whole operation is bounded by the request lifetime, not just one attempt. Retry set to a small bounded budget (see row 4). |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = `GET,HEAD,PUT,OPTIONS`, so `CreateCustomer`/`CreateSubscription` (`POST`) are **never** auto-resent — correct, since they are not idempotent at the wire level. Reads (`GET`) may retry. Keep the default method set. |
| 5 | Idempotency & ambiguous writes | No caller-supplied idempotency key exists on `CreateCustomer`/`CreateSubscription` (map rows show none). Reconciliation instead: customer keyed by unique `reference` (create-422 ⇒ re-read); subscription de-duped by listing the customer's existing non-terminal subscriptions for the product before creating. A `POST` that succeeds server-side but whose response is lost is recovered on the next call by the same read-before-write. |
| 6 | Observability | `ILogger` in `MaxioBillingService` logs at Information (subscribe start/outcome, customer ensured, dedupe hit) and Warning/Error on provider faults, including the provider error text from `RawError.ReadAsString()` / typed error accessors for correlation. `LogRequestBody` stays **off**. |
| 7 | Sensitive data | Request models in scope (`CreateCustomer`: name/email; `CreateSubscription`: product handle + customer id) carry no card/bank data — this build never captures a payment method (plans require none). Still, `LogRequestBody` stays off and `options.Logging.LoggerFactory` is set explicitly from DI so `MAXIOADVANCEDBILLINGCLIENT_LOG` cannot switch body logging on from outside code. |
| 8 | Environment selection | Only `ServerEnvironment.Us` (`https://{site}.chargify.com`) is used — the sandbox and any real site are both Chargify US hosts distinguished by subdomain. No live/sandbox enum split exists in the SDK; test traffic is kept off any live system purely by which `Maxio:Subdomain`/`Maxio:ApiKey` (the sandbox site's) the deployment supplies. `Maxio:BaseUrl` can point at any host for on-prem/EU without code change. |

## 6. Assumptions & Blockers

- **Assumption — plan = Maxio product.** "Subscription plans" map to Maxio *products* within the configured
  product family; `ListProductsForProductFamily` is the plan list. (Price points exist but the seeded plans
  each expose a single default price, surfaced via `PriceInCents`.)
- **Assumption — `planHandle` is required on `POST /api/subscriptions`.** The catalog is not fixed (build must
  run against another site/family), so the endpoint takes the plan handle from the client (chosen from
  `GET /api/subscription-plans`) rather than hardcoding `eshop-pro`.
- **Assumption — no payment method captured.** Seeded plans are "payment method not required"; the create
  request omits `payment_collection_method` (Maxio default `automatic`) and captures no card, so subscribe
  succeeds with no 3-DS. If a given site rejects a card-less `automatic` create, that surfaces as a typed
  422 through the error boundary with the provider message — not a silent failure. No Blocker: the seeded
  plans are configured to accept this.
- No blockers to planning; every operation the flow needs exists on the map.
