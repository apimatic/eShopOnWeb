# Maxio subscription billing implementation plan

## 1. Scope & sequence

| Step | SDK operations / work | Integration consequence | Source |
|---|---|---|---|
| 1. Dependency and configuration | Pin NuGet `AsadAli.AdvancedBilling.Sdk` at `1.0.2`; bind one application options object from exactly `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl`; validate the first three as nonblank and treat `BaseUrl` as optional. | Keep secret values outside the repository. `MAXIO_ENVIRONMENT` is not an SDK server selector: this SDK exposes only `ServerEnvironment.Us` and `.Eu`; the requested configuration contract has no `Maxio:Environment` key. | `sdk-map.md`; configuration keys are **YOUR CALL — not in the map** |
| 2. Client registration | Construct one long-lived `MaxioAdvancedBilling.MaxioAdvancedBillingClient` over an `IHttpClientFactory`-managed, Maxio-named `HttpClient`. Configure Basic auth, the Production/US server node, timeouts/retries, and an outbound write-resend guard before construction. | If `Maxio:BaseUrl` is nonblank, assign it unchanged to `options.Server.Production.Us.BaseUrl`; otherwise assign `Maxio:Subdomain` to `options.Server.Production.Us.Site`. Select `MaxioAdvancedBilling.Servers.ServerEnvironment.Us`. | `sdk-map.md`; application DI layout is **YOUR CALL — not in the map** |
| 3. Resolve configured catalog | `client.ProductFamilies.ListProductFamilies(...)`, select the sole response whose `ProductFamily.Handle` equals `Maxio:ProductFamilyHandle`, validate its nullable `Id`, then `client.ProductFamilies.ListProductsForProductFamily(...)` with that numeric ID rendered invariantly. | Do not call `ReadProductFamily` for a handle: its Notes mention `handle:...`, but its generated parameter is `int`, so the SDK cannot express that documented form. Exclude archived products and drive every manual page until the short final page. | `operations/ProductFamilies.md`; `records-3-Of-Su.md` |
| 4. Present available plans | Map each product to an application DTO from `Product.Name`, `Handle`, `Description`, `PriceInCents`, `Interval`, `IntervalUnit`; never expose/rely on seeded numeric IDs. | Cache only briefly if desired; the Maxio catalog remains the system of record. | `records-3-Of-Su.md`; caching is **YOUR CALL — not in the map** |
| 5. Ensure customer | Derive a stable Maxio customer reference from the authenticated application's immutable user ID; call `client.Customers.ReadCustomerByReference(...)`; on a genuine 404 call `client.Customers.CreateCustomer(...)` with first name, last name, email, and the same reference. If a concurrent create returns 422, read by reference again and accept the found customer. | Maxio's CreateCustomer Notes explicitly enforce one customer per `reference`. A parse/transport failure is not absence and must never trigger CreateCustomer. Validate the nullable returned `Customer.Id`. | `operations/Customers.md`; `records-1-Ac-Cr.md`; `records-2-Cr-Ne.md` |
| 6. Claim subscription idempotency | Derive a deterministic application subscription key/reference from immutable user ID + requested product handle. Under a durable application-side unique claim/lease, call `client.Subscriptions.FindSubscription(reference, ...)` before creating; if found, verify it belongs to the same ensured customer and requested product and return it. | The SDK exposes no idempotency-key argument/header. `CreateSubscription.Reference` exists and `FindSubscription` reads it, but the map does **not** say subscription references are unique. Provider-side duplicate rejection is therefore `UNVERIFIED`; do not rely on it as the only concurrency control. In-memory persistence provides only single-process/single-run protection. | `operations/Subscriptions.md`; `records-2-Cr-Ne.md`; application claim is **YOUR CALL — not in the map** |
| 7. Enroll | Validate the requested handle against the configured-family product set, then call `client.Subscriptions.CreateSubscription(...)` with `ProductHandle`, ensured `CustomerId`, and deterministic `Reference`. Use no payment-profile fields and no explicit price point for the seeded products described by the task. | The operation Notes require a product selector and customer selector even though every inner model field is optional. The task states the selected products require no payment method. Reconcile an ambiguous write outcome through `FindSubscription`; release/expire the application claim only according to the settled outcome. | `operations/Subscriptions.md`; `records-2-Cr-Ne.md`; no-card catalog property is supplied by the user |
| 8. Confirm result | Unwrap `SubscriptionResponse.Subscription`; validate it is non-null and map ID/reference, nested product name/handle, `ProductPriceInCents`, state, and date fields. | Whether `NextAssessmentAt` is exactly the business-facing “next billing date” is not stated by the map: expose it best-effort, fall back to `CurrentPeriodEndsAt`, and preserve null; label this mapping `UNVERIFIED` until sandbox traffic confirms it. | `records-3-Of-Su.md`; `records-4-Su-We.md` |
| 9. Read the user's subscriptions | Ensure/read the customer, then call `client.Customers.ListCustomerSubscriptions(customerId, ...)`; unwrap every nullable `SubscriptionResponse.Subscription`, filter/authorize by the returned nested customer/reference, and map through the same result mapper. | This operation is not paginated. Maxio is the read system of record; a local mapping is an idempotency/index aid, not the authoritative subscription state. | `operations/Customers.md`; `records-4-Su-We.md`; authorization policy is **YOUR CALL — not in the map** |
| 10. Boundary and tests | Put every SDK call behind one application integration boundary; translate typed/raw provider errors, malformed bodies, transport failures and cancellation consistently. Unit-test through the injected `HttpClient`/fake `HttpMessageHandler`; add sandbox smoke tests separately. | Cover lookup miss vs malformed response, customer create race, simultaneous subscribe calls, ambiguous POST outcome/reconciliation, resend-guard behavior, pagination, nullable envelopes, and state/date mapping. | `sdk-map.md`; test scenario selection is **YOUR CALL — not in the map** |

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal
> C# identifier. The cancellation-token parameter really is named `ct`: in named
> arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take
> each one from that type's own map row, never from where a neighbouring type sits. A members
> table names the namespace outright; otherwise the row's source path implies it
> (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root
> namespace). Enums, unions, auth, server and client-config types are spread across different
> child namespaces, and two types configured side by side in the same options object routinely
> live in different ones. Dropping a type to the root or to `.Models` makes the implementer
> guess the wrong `using`, and the build breaks.

### Operations

All methods are async and return `System.Threading.Tasks.Task`/`Task<T>`; all are throw-only (there are no `...Result` siblings).

| Controller property | Exact operation signature and return | Request / parameters | Response fields read | Error and pagination | Source |
|---|---|---|---|---|---|
| `client.ProductFamilies` | `ListProductFamilies(MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, System.DateTimeOffset? startDate, System.DateTimeOffset? endDate, System.DateTimeOffset? startDatetime, System.DateTimeOffset? endDatetime, System.Threading.CancellationToken ct = default)` → `System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<MaxioAdvancedBilling.Models.ProductFamilyResponse>>` | Pass every non-defaulted nullable argument explicitly as `null`. | Each `ProductFamilyResponse.ProductFamily (product_family): MaxioAdvancedBilling.Models.ProductFamily?`; inner `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `ArchivedAt (archived_at): DateTimeOffset?`. | Case B: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; read `StatusCode`, `ReadAsString()`, `ReadAsBytes()`, `ReadAsJson<T>()`. No pagination. | `operations/ProductFamilies.md`; `records-3-Of-Su.md` |
| `client.ProductFamilies` | `ListProductsForProductFamily(string productFamilyId, MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, MaxioAdvancedBilling.Models.Enums.ListProductsFilter? filter, System.DateTimeOffset? startDate, System.DateTimeOffset? endDate, System.DateTimeOffset? startDatetime, System.DateTimeOffset? endDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, System.Threading.CancellationToken ct = default)` → `Task<IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>>` | `productFamilyId` is required; pass `dateField`, `filter`, all date fields, and `include` explicitly (`null` when unused); use `includeArchived: false`; drive `page`/`perPage`. | Each `ProductResponse.Product (product): MaxioAdvancedBilling.Models.Product` required; read `Id`, `Name`, `Handle`, `Description`, `PriceInCents`, `Interval`, `IntervalUnit`, `ArchivedAt`, `ProductFamily`, `ProductPricePointId`, `ProductPricePointHandle` (all nullable). | Case A: `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`; `TryGetString(out string)` [404], then inherited `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` [fallback]. Manual `page` + `perPage`. | `operations/ProductFamilies.md`; `records-3-Of-Su.md` |
| `client.Customers` | `ReadCustomerByReference(string reference, System.Threading.CancellationToken ct = default)` → `Task<MaxioAdvancedBilling.Models.CustomerResponse>` | `reference` required; wire query name `reference`. | `CustomerResponse.Customer (customer): MaxioAdvancedBilling.Models.Customer` required; read `Id (id): int?`, `Reference (reference): string?`, `FirstName`, `LastName`, `Email`. | Case B: `SdkException<RawError>`; inspect `RawError.StatusCode` for the genuine 404, body via `ReadAsString()`. No pagination. | `operations/Customers.md`; `records-2-Cr-Ne.md` |
| `client.Customers` | `CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, System.Threading.CancellationToken ct = default)` → `Task<MaxioAdvancedBilling.Models.CustomerResponse>` | Body must be passed explicitly and non-null. `CreateCustomerRequest.Customer (customer): MaxioAdvancedBilling.Models.CreateCustomer` required. Inner required: `FirstName (first_name): string`, `LastName (last_name): string`, `Email (email): string`; include optional `Reference (reference): string?`. All address, locale, organization, phone, tax, parent and Salesforce fields are intentionally omitted. Notes: `reference`, when supplied, must be unique. | Same customer envelope/fields as lookup. | Case A: `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`; `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422], then `TryGetRawError(out RawError)`. Payload has `Errors (errors): MaxioAdvancedBilling.Models.Errors?`; generated `Errors` exposes only `PerPage` and `PricePoint`, a suspicious mismatch for customer validation: extract best-effort and fall back to a sanitized generic message (`UNVERIFIED`). No pagination. | `operations/Customers.md`; `records-1-Ac-Cr.md`; `records-2-Cr-Ne.md` |
| `client.Subscriptions` | `FindSubscription(string? reference, System.Threading.CancellationToken ct = default)` → `Task<MaxioAdvancedBilling.Models.SubscriptionResponse>` | `reference` is nullable but has no default and must be passed explicitly; integration passes deterministic nonblank reference. | `SubscriptionResponse.Subscription (subscription): MaxioAdvancedBilling.Models.Subscription?`; validate non-null and use fields listed below. | Case A: `SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`; `TryGetNoContent(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` [404], then `TryGetRawError(out RawError)`. No pagination. | `operations/Subscriptions.md`; `records-4-Su-We.md` |
| `client.Subscriptions` | `CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, System.Threading.CancellationToken ct = default)` → `Task<MaxioAdvancedBilling.Models.SubscriptionResponse>` | `CreateSubscriptionRequest.Subscription (subscription): MaxioAdvancedBilling.Models.CreateSubscription` required. Inner model marks no fields required; integration MUST set `ProductHandle (product_handle): string?`, `CustomerId (customer_id): int?`, `Reference (reference): string?`. Notes require one of product ID/handle and one of customer ID/reference/customer attributes. Omit `ProductId`, `CustomerReference`, customer attributes, all payment fields, components, coupon, dates, currency, price-point selectors and import/prepaid/agreement fields; omitting price-point selector uses the product default according to CreateSubscription Notes. | Envelope member is nullable. Read `Id (id): int?`, `Reference (reference): string?`, `State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `CurrentBillingAmountInCents (current_billing_amount_in_cents): long?`, nested `Customer (customer): Customer?`, nested `Product (product): Product?`. | Case A: `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`; `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422], then `TryGetRawError(out RawError)`. Payload `Errors (errors): IReadOnlyList<string>` required. No pagination. No SDK idempotency-key parameter. | `operations/Subscriptions.md`; `records-2-Cr-Ne.md`; `records-3-Of-Su.md`; `records-4-Su-We.md` |
| `client.Customers` | `ListCustomerSubscriptions(int customerId, System.Threading.CancellationToken ct = default)` → `Task<IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>>` | `customerId` required. | Each wrapper contains nullable `Subscription`; validate/skip-or-fail deliberately, then map the same fields as CreateSubscription. | Case B: `SdkException<RawError>` with direct `StatusCode` / body readers. No pagination. | `operations/Customers.md`; `records-4-Su-We.md` |

### Response/envelope and enum facts

| Type / member | Exact contract | Source |
|---|---|---|
| `MaxioAdvancedBilling.Models.ProductResponse` | Exactly `Product (product): MaxioAdvancedBilling.Models.Product`, required. | `records-3-Of-Su.md` |
| `MaxioAdvancedBilling.Models.ProductFamilyResponse` | Exactly `ProductFamily (product_family): MaxioAdvancedBilling.Models.ProductFamily?`. | `records-3-Of-Su.md` |
| `MaxioAdvancedBilling.Models.CustomerResponse` | Exactly `Customer (customer): MaxioAdvancedBilling.Models.Customer`, required. | `records-2-Cr-Ne.md` |
| `MaxioAdvancedBilling.Models.SubscriptionResponse` | Exactly `Subscription (subscription): MaxioAdvancedBilling.Models.Subscription?`. | `records-4-Su-We.md` |
| `MaxioAdvancedBilling.Models.Enums.IntervalUnit` | String-enum static members: `Day` → `day`; `Month` → `month`. | `enums.md` |
| `MaxioAdvancedBilling.Models.Enums.SubscriptionState` | String-enum static members/wire values: `Pending`/`pending`, `FailedToCreate`/`failed_to_create`, `Trialing`/`trialing`, `Assessing`/`assessing`, `Active`/`active`, `SoftFailure`/`soft_failure`, `PastDue`/`past_due`, `Suspended`/`suspended`, `Canceled`/`canceled`, `Expired`/`expired`, `Paused`/`paused`, `Unpaid`/`unpaid`, `TrialEnded`/`trial_ended`, `OnHold`/`on_hold`, `AwaitingSignup`/`awaiting_signup`. | `enums.md` |
| Enum serialization/readback | These are generated string-enum records, not C# enums; application DTOs should use the wire `.Value` and tolerate future unknown values. | `enums.md`; exact usage hazard deferred to required reading |

### Client construction, auth, server and configuration

| Fact | Exact contract | Source |
|---|---|---|
| Package | NuGet package ID `AsadAli.AdvancedBilling.Sdk`; map/source tag version `1.0.2`; target `netstandard2.0`; root namespace `MaxioAdvancedBilling`. Pin `1.0.2` so code and this sheet stay aligned. | `sdk-map.md` |
| Constructor | The only constructor is `MaxioAdvancedBilling.MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)`. Controller properties include `ProductFamilies`, `Customers`, `Subscriptions`. | `sdk-map.md` |
| Options | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions`: `Environment: MaxioAdvancedBilling.Servers.ServerEnvironment`; `Retry: MaxioAdvancedBilling.Core.Configuration.RetryOptions`; `Server: MaxioAdvancedBilling.ServerOptions`; `BasicAuth: MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?`. | `sdk-map.md` |
| Auth | Set `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = Maxio:ApiKey, Password = "x" }`. Basic is the only auth scheme. | `sdk-map.md` |
| Server selection | `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` is the default and its Production template is `https://{site}.chargify.com`. Set `options.Server.Production.Us.Site = Maxio:Subdomain`. The SDK's other environment is `ServerEnvironment.Eu` with `https://{site}.ebilling.maxio.com`. There is no `Sandbox` enum member. | `sdk-map.md` |
| Literal base override | When `Maxio:BaseUrl` is set, assign its value verbatim to `options.Server.Production.Us.BaseUrl`; a literal URL with no placeholders is used as-is. Configure before client construction. | `sdk-map.md` |
| Retry/server initialization | Start from `MaxioAdvancedBilling.Core.Configuration.RetryOptions.Default()` when changing retry properties because all record members are required. Exact retry/timeout/resend choices are deliberately deferred to required reading. | `sdk-map.md` |
| Configuration provenance | `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` are application binding keys required by the task, not SDK property names. | `YOUR CALL — not in the map` |

### Idempotency support and limits

| Capability | SDK/provider-visible fact | Consequence | Source |
|---|---|---|---|
| Customer uniqueness | `CreateCustomer.Reference` is optional, and CreateCustomer Notes state only one customer may exist for a supplied reference; `ReadCustomerByReference(string reference, ...)` returns a single match. | Deterministic application user reference + read/create/re-read resolves a customer creation race. | `operations/Customers.md`; `records-1-Ac-Cr.md` |
| Subscription correlation | `CreateSubscription.Reference` is optional; `FindSubscription(string? reference, ...)` finds a subscription by reference. | Use a deterministic reference for reconciliation. | `operations/Subscriptions.md`; `records-2-Cr-Ne.md` |
| Subscription uniqueness | Neither CreateSubscription Notes nor its request/operation surface states that `Reference` is unique; there is no idempotency-key/header argument. | Provider duplicate prevention through reference is `UNVERIFIED`; a durable application unique claim is required for concurrent callers, and transport ambiguity must be reconciled. | `operations/Subscriptions.md`; `records-2-Cr-Ne.md` |
| SDK transport retry | A POST can be re-sent after a transport failure even though status retry method lists omit POST. | A local lock alone cannot guarantee at-most-one upstream send; the outbound write guard and reconciliation design must be completed after loading the resilience skill. | Exact usage hazard deferred to `maxio-sdk-merged:dotnet-configuration-resilience` |

### Error boundary contract

| Operation | Concrete catch and body path | Application mapping guidance | Source |
|---|---|---|---|
| `ListProductFamilies`, `ReadCustomerByReference`, `ListCustomerSubscriptions` | Catch `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; status is `ex.Error.StatusCode`, body is best read with `ReadAsString()`. | Recognize only an operation-appropriate 404 as absence; sanitize bodies in external responses. | operation pages above; `sdk-map.md` |
| `ListProductsForProductFamily` | Catch `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`; branch `TryGetString` [404], then `TryGetRawError`. | Configured-family miss is a catalog/configuration failure; do not pretend it is an empty catalog. | `operations/ProductFamilies.md` |
| `CreateCustomer` | Catch `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`; branch `TryGetCustomerErrorResponse1` [422], then `TryGetRawError`. | On 422, re-read deterministic reference first to distinguish the expected create race; otherwise surface a sanitized validation rejection. Generated typed payload trust is `UNVERIFIED` as noted above. | `operations/Customers.md`; `records-2-Cr-Ne.md` |
| `FindSubscription` | Catch `SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`; branch `TryGetNoContent(out RawError)` [404], then `TryGetRawError`. | Only `TryGetNoContent` is “not found”; parse/transport failures are unknown, never absence. | `operations/Subscriptions.md` |
| `CreateSubscription` | Catch `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`; branch `TryGetErrorListResponse1` [422], then `TryGetRawError`; typed messages are `payload.Errors`. | Return caller-safe validation/conflict detail; reconcile unknown transport/write outcomes before declaring failure. | `operations/Subscriptions.md`; `records-2-Cr-Ne.md` |
| Raw fallback | `MaxioAdvancedBilling.Core.ErrorResponse.RawError`: `StatusCode: System.Net.HttpStatusCode`, `ReadAsString(): string`, `ReadAsBytes(): ReadOnlyMemory<byte>`, `ReadAsJson<T>(): T?`. Typed errors inherit `TryGetRawError(out RawError)` but it is only the untyped fallback. | Keep Maxio auth/config failures distinct from the caller's JWT authentication; do not leak credentials, raw payloads or framework exception text. | `sdk-map.md`; HTTP mapping is **YOUR CALL — not in the map** |

## 3. Trap notes

⚠ Step 2 (client registration) — client/`HttpClient` lifetime, named-client isolation, handler rotation, and when options are captured affect correctness under load. **MUST load `maxio-sdk-merged:dotnet-client-initialization`** before wiring the client.

⚠ Step 2 (authentication) — credential timing and the exact Basic-auth options property determine whether every operation is authenticated. **MUST load `maxio-sdk-merged:dotnet-authentication`** before wiring credentials.

⚠ Steps 3–9 (calls) — nullable parameters without C# defaults, response-envelope unwrapping, named arguments and `ct:` can otherwise mis-bind or silently read the wrapper. **MUST load `maxio-sdk-merged:dotnet-calling-endpoints`** before the first operation call.

⚠ Steps 4–9 (models) — required nested records, string-enum readback, nullable envelopes and unknown future enum values affect request validity and response mapping. **MUST load `maxio-sdk-merged:dotnet-models`** before constructing or mapping SDK models.

⚠ Steps 2, 6–7 (resilience/idempotency) — retry/timeout knobs, whole-call cancellation, literal server override, manual pagination and whether a failed POST can be re-sent determine whether enrollment can duplicate or hang. **MUST load `maxio-sdk-merged:dotnet-configuration-resilience`** before client configuration or idempotency implementation.

⚠ Step 10 (boundary) — Case A and Case B have incompatible status/body access paths; an incomplete catch ladder loses typed bodies or misclassifies failure. **MUST load `maxio-sdk-merged:dotnet-error-handling`** before writing the boundary.

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `maxio-sdk-merged:dotnet-error-handling`** before writing that boundary.

⚠ Step 10 (tests) — the injected `HttpClient` is the stable seam; retries and envelope deserialization mean mocking controller internals does not test actual integration behavior. **MUST load `maxio-sdk-merged:dotnet-testing`** before writing tests.

## 4. REQUIRED READING

Load all of these **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `maxio-sdk-merged:dotnet-client-initialization` | Step 2 client construction, DI and lifetime |
| `maxio-sdk-merged:dotnet-authentication` | Step 2 Basic credentials |
| `maxio-sdk-merged:dotnet-calling-endpoints` | Steps 3–9 exact async calls, named args, envelopes and cancellation |
| `maxio-sdk-merged:dotnet-models` | Steps 4–9 request records, nullable fields and enums |
| `maxio-sdk-merged:dotnet-configuration-resilience` | Steps 2–7 server override, timeouts, retries, write safety and pagination |
| `maxio-sdk-merged:dotnet-error-handling` | Step 10 typed/raw/transport/JSON boundary |
| `maxio-sdk-merged:dotnet-testing` | Step 10 `HttpClient` seam and retry/error tests |

## 5. Assumptions & Blockers

### Assumptions

- The sandbox site is US-hosted, so the only no-override derivation permitted by the requested four-key configuration contract is `ServerEnvironment.Us` + `Production.Us.Site`. A nonblank `Maxio:BaseUrl` overrides that Production/US base URL verbatim.
- The application can resolve nonblank first name, last name and email for the authenticated immutable user ID; Maxio's `CreateCustomer` model requires all three.
- The task's seeded products are authoritative for the no-payment-method development flow; the SDK operation itself says payment information may be required depending on product configuration.
- The implementation will use durable relational persistence in production; the task's required in-memory development mode deliberately loses the local user/subscription idempotency mapping at restart.
- Mapping `Subscription.NextAssessmentAt` (falling back to `CurrentPeriodEndsAt`) to the UI's next-billing date is `UNVERIFIED` until observed against the sandbox response.
- Maxio subscription-reference uniqueness is `UNVERIFIED`; the implementation must not depend on it as its sole duplicate-prevention mechanism.

### Blockers

- None.
