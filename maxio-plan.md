# Maxio recurring-subscription integration plan

## Scope & sequence

| Step | Additive implementation work | SDK operations / contract source |
|---:|---|---|
| 1 | Add the published package `AsadAli.AdvancedBilling.Sdk` pinned to `1.0.2`; bind and validate Maxio development configuration; register one long-lived `HttpClient` pipeline and construct the SDK client. Do not change the existing one-time commerce path. | Client/options/auth/server contracts in `sdk-map.md` |
| 2 | Add an authenticated-user adapter that derives a stable subject only from the validated JWT `ClaimsPrincipal`; never accept an eShop user id from a request body/query. Derive deterministic, non-PII customer and enrollment references from issuer + subject. | Application concern; `CreateCustomer.Reference` and `CreateSubscription.Reference` fields in `records-1-Ac-Cr.md` and `records-2-Cr-Ne.md` |
| 3 | Implement `GET /api/subscription-plans`: resolve the configured family by exact handle, use its runtime ID only for the immediate products call, exhaust product pages, reject archived/card-required products, and return stable handles plus price/interval data. | `ProductFamilies.ListProductFamilies`, `ProductFamilies.ListProductsForProductFamily` in `operations/ProductFamilies.md` |
| 4 | Implement the idempotent customer step: lookup by deterministic reference; on 404 create with required identity fields; on create 422, lookup again to resolve a concurrent winner. Never persist or configure a Maxio numeric customer ID as identity. | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer` in `operations/Customers.md` |
| 5 | Implement `POST /api/subscriptions`: validate the requested product handle against the configured family, acquire a database-backed unique enrollment gate, ensure the customer, reconcile by deterministic subscription reference, then create using customer/product/price-point handles. A second request must return the completed result or an in-progress response and must never issue a second create call. | `Products.ReadProductByHandle`, `Customers.ListCustomerSubscriptions`, `Subscriptions.CreateSubscription` in `operations/Products.md`, `operations/Customers.md`, `operations/Subscriptions.md` |
| 6 | Implement `GET /api/my-subscriptions`: lookup the current customer by deterministic reference; return `[]` on lookup 404; otherwise list the customer's subscriptions, retain only products in the configured family, and map plan, price, state, and next-billing data. | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` in `operations/Customers.md` |
| 7 | Put every SDK call behind one exception-translation boundary, preserve request cancellation, classify configuration/auth/validation/not-found/transient failures, sanitize provider bodies, and make ambiguous writes enter reconciliation rather than automatic application retry. | Per-operation error rows below; core error model in `sdk-map.md` |
| 8 | Add unit/integration tests at the `HttpClient` seam for configuration, auth, pagination, envelopes, nullable required data, duplicate requests, concurrent customer creation, reconciliation, errors, malformed JSON, and cancellation. No live Maxio traffic is required in automated tests. | `sdk-map.md`; companion testing/error/resilience skills listed under REQUIRED READING |

## CONTRACT SHEET

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

### SDK identity, installation, client, auth, and server

| Concern | Exact contract | Source |
|---|---|---|
| Package | NuGet package `AsadAli.AdvancedBilling.Sdk`; this sheet is grounded to source/tag `v1.0.2` / spec commit `15db14b`, so pin package version `1.0.2` and re-ground if a different package version is selected. Root namespace is `MaxioAdvancedBilling`; target is `netstandard2.0`. | `sdk-map.md` |
| Constructor | The only constructor is `MaxioAdvancedBilling.MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)`. Controllers are properties such as `.ProductFamilies`, `.Products`, `.Customers`, and `.Subscriptions`. | `sdk-map.md` (`MaxioAdvancedBillingClient.cs`) |
| Options | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` has `Environment: MaxioAdvancedBilling.Servers.ServerEnvironment`, `Retry: MaxioAdvancedBilling.Core.Configuration.RetryOptions`, `Server: MaxioAdvancedBilling.ServerOptions`, and `BasicAuth: MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?`. | `sdk-map.md` (`MaxioAdvancedBillingClientOptions.cs`) |
| Auth | `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = apiKey, Password = "x" }`. This is HTTP Basic; username is the API key and password is the literal `"x"`. | `sdk-map.md` |
| Region | `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` has wire value `US` and default template `https://{site}.chargify.com`; `.Eu` has wire value `EU` and template `https://{site}.ebilling.maxio.com`. There is no SDK `Sandbox` environment member. | `sdk-map.md` |
| Site / base URL | For US set `options.Server.Production.Us.Site`; for EU set `options.Server.Production.Eu.Site`. When `Maxio:BaseUrl` is non-empty, assign that exact string to the selected branch's `.BaseUrl` (`options.Server.Production.Us.BaseUrl` or `.Eu.BaseUrl`) and do not derive, normalize, trim, append, or replace it. Otherwise set the selected branch's `.Site` from `Maxio:Subdomain`. The in-scope operations all use the Production server group, not Ebb. | `sdk-map.md` (`Server.cs`, `ServerOptions.cs`, `Servers/ProductionOptions.cs`) |
| Retry options | `MaxioAdvancedBilling.Core.Configuration.RetryOptions` members are `StatusCodesToRetry: IReadOnlyList<HttpStatusCode>`, `HttpMethodsToRetry: IReadOnlyList<HttpMethod>`, `MaxRetries: int`, `Delay: TimeSpan`, `Timeout: TimeSpan?`, `BackOffFactor: int`, `UseExponentialBackoff: bool`, `MaxJitter: TimeSpan`, `OnRetry: Action<RetryAttempt>?`; all are required, so use `RetryOptions.Default()` as the construction baseline before intentional overrides. | `sdk-map.md` (`Core/Configuration/RetryOptions.cs`) |

### Configuration binding and secret boundary

| Input | Application key / action | Validation and repository rule |
|---|---|---|
| .NET configuration | `Maxio:ApiKey` | Required and non-whitespace in Development. Never put a value in tracked files; the main agent loads the development value through .NET user-secrets. |
| .NET configuration | `Maxio:Subdomain` | Required when `Maxio:BaseUrl` is absent; non-secret, but no seeded value is hardcoded. |
| .NET configuration | `Maxio:ProductFamilyHandle` | Required; the sandbox value is expected to be supplied as `eshop-subscribe`, never as a numeric ID. |
| .NET configuration | `Maxio:BaseUrl` | Optional. If supplied it is used verbatim through the selected Production `.BaseUrl` property. |
| Environment alias | `MAXIO_API_KEY` → `Maxio:ApiKey` | Explicit alias/override; never echo or log it. |
| Environment alias | `MAXIO_SITE_SUBDOMAIN` → `Maxio:Subdomain` | Explicit alias/override. |
| Environment alias | `MAXIO_DEFAULT_PRODUCT_FAMILY` → `Maxio:ProductFamilyHandle` | Explicit alias/override. |
| Environment selector | `MAXIO_ENVIRONMENT` | Read separately from the four bound keys and accept only the SDK region values `US`/`EU` (case-insensitive) unless the assumption below is clarified. It selects the server branch; it does not represent Maxio site test mode. |
| Development guard | hosting environment + `MAXIO_ENVIRONMENT` | Register the integration only for the requested Development/sandbox deployment and fail closed outside it; the SDK itself has no sandbox enum. Never write any supplied environment value to a tracked file. |

### Operations

The signature text below preserves the map's exact parameter order and literal names. `Returns` is listed separately exactly as the map does; calls are asynchronous and must be awaited. Every optional parameter without a C# default still must be passed explicitly, normally as a named `null`.

| Use | Controller property · exact method signature | Returns / request and response fields used | Error contract | Pagination | Source |
|---|---|---|---|---|---|
| Resolve configured family | `client.ProductFamilies.ListProductFamilies(MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)`; pass all five filters explicitly as `null`. | Returns `IReadOnlyList<MaxioAdvancedBilling.Models.ProductFamilyResponse>`. Envelope field `ProductFamily (product_family): MaxioAdvancedBilling.Models.ProductFamily?`; read `Id (id): int?`, `Handle (handle): string?`, `ArchivedAt (archived_at): DateTimeOffset?`. Exact-match `Handle` to configuration and require a non-null runtime `Id`. | **Case B:** `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; body accessors are `StatusCode: HttpStatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()`. | None. | `operations/ProductFamilies.md`; `records-3-Of-Su.md` |
| List family products | `client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, MaxioAdvancedBilling.Models.ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)`. Pass the freshly resolved family `Id.Value.ToString(CultureInfo.InvariantCulture)`, never a stored/configured numeric ID. | Returns `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>`. Envelope `Product (product): MaxioAdvancedBilling.Models.Product` is required. Read `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): MaxioAdvancedBilling.Models.Enums.IntervalUnit?`, `ArchivedAt (archived_at): DateTimeOffset?`, `RequireCreditCard (require_credit_card): bool?`, `ProductFamily (product_family): ProductFamily?`, and `ProductPricePointHandle (product_price_point_handle): string?`. | **Case A:** `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`; `TryGetString(out string)` for 404; inherited `TryGetRawError(out RawError)` fallback. | Manual `page` + `perPage`, defaults 1/20. Continue until the returned page count is less than the requested `perPage`; deduplicate by stable product handle defensively. | `operations/ProductFamilies.md`; `records-3-Of-Su.md` |
| Validate selected product by handle | `client.Products.ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | Returns `MaxioAdvancedBilling.Models.ProductResponse`; unwrap required `.Product`. Read the same product fields above and require `ProductFamily?.Handle` to exactly equal `Maxio:ProductFamilyHandle`. Require `ArchivedAt is null`, `RequireCreditCard is not true`, and a non-null handle. | **Case B:** `SdkException<RawError>` with `StatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()`; treat 404 as an unknown plan. | None. | `operations/Products.md`; `records-3-Of-Su.md` |
| Lookup customer | `client.Customers.ReadCustomerByReference(string reference, CancellationToken ct = default)` | Returns `MaxioAdvancedBilling.Models.CustomerResponse`; required envelope field `Customer (customer): MaxioAdvancedBilling.Models.Customer`. Read `Id (id): int?` and `Reference (reference): string?`; require both when listing subscriptions. | **Case B:** `SdkException<RawError>` with the raw accessors above; 404 means no customer for GET and starts ensure/create for POST. | None. | `operations/Customers.md`; `records-2-Cr-Ne.md` |
| Create customer | `client.Customers.CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, CancellationToken ct = default)` | Request envelope `Customer (customer): MaxioAdvancedBilling.Models.CreateCustomer` **required**. Set `FirstName (first_name): string` **required**, `LastName (last_name): string` **required**, `Email (email): string` **required**, `Reference (reference): string?`. Response is `CustomerResponse`; read `Customer.Id` and `Customer.Reference`. | **Case A:** `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`; `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` for 422, where `Errors (errors): MaxioAdvancedBilling.Models.Errors?`; generated `Errors` has only `PerPage (per_page): IReadOnlyList<string>?` and `PricePoint (price_point): IReadOnlyList<string>?`. Also `TryGetRawError(out RawError)` fallback. Because that generated 422 body is suspicious for this operation, extract best-effort, then re-lookup by reference and otherwise fall back to a generic message — **UNVERIFIED** against live traffic. | None. | `operations/Customers.md`; `records-1-Ac-Cr.md`; `records-2-Cr-Ne.md` |
| Reconcile/list customer's subscriptions | `client.Customers.ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` using only the just-resolved runtime `Customer.Id.Value`. | Returns `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>`. Envelope `Subscription (subscription): MaxioAdvancedBilling.Models.Subscription?`. Read `Id (id): int?`, `Reference (reference): string?`, `State (state): SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `Currency (currency): string?`, and nested `Product` fields `Name`, `Handle`, `ProductFamily.Handle`. | **Case B:** `SdkException<RawError>` with the raw accessors above. | None. | `operations/Customers.md`; `records-4-Su-We.md`; `records-3-Of-Su.md` |
| Create subscription | `client.Subscriptions.CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, CancellationToken ct = default)` | Request envelope `Subscription (subscription): MaxioAdvancedBilling.Models.CreateSubscription` **required**. Set `ProductHandle (product_handle): string?`, optionally the validated current `ProductPricePointHandle (product_price_point_handle): string?`, `CustomerReference (customer_reference): string?`, deterministic `Reference (reference): string?`, and `PaymentCollectionMethod (payment_collection_method): MaxioAdvancedBilling.Models.Enums.CollectionMethod?` to `CollectionMethod.Remittance` (`remittance`) for the current Relationship Invoicing no-card flow. Do **not** set numeric product/customer IDs, payment profile, card, bank, component, or metered fields. Returns `SubscriptionResponse`; read the same subscription fields above. | **Case A:** `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`; `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` for 422, whose required `Errors (errors)` is `IReadOnlyList<string>`; inherited `TryGetRawError(out RawError)` fallback. Log the typed list safely at the server boundary while keeping the HTTP response sanitized. | None. The method has no idempotency-key/header parameter. | `operations/Subscriptions.md`; `records-2-Cr-Ne.md`; `records-4-Su-We.md`; `records-3-Of-Su.md`; `map/models/enums.md` |

### Enum values used or returned

All model enums below are generated `StringEnum<T>` types in `MaxioAdvancedBilling.Models.Enums`; use the literal C# static member or `FromValue`, not a C# enum cast.

| Type | Exact C# member → wire value | Use | Source |
|---|---|---|---|
| `BasicDateField` | `UpdatedAt` → `updated_at`; `CreatedAt` → `created_at` | Signature only; this integration passes `null`. | `map/models/enums.md` |
| `ListProductsInclude` | `PrepaidProductPricePoint` → `prepaid_product_price_point` | Signature only; this integration passes `null`. | `map/models/enums.md` |
| `IntervalUnit` | `Day` → `day`; `Month` → `month` | Plan DTO billing interval. | `map/models/enums.md` |
| `SubscriptionState` | `Pending` → `pending`; `FailedToCreate` → `failed_to_create`; `Trialing` → `trialing`; `Assessing` → `assessing`; `Active` → `active`; `SoftFailure` → `soft_failure`; `PastDue` → `past_due`; `Suspended` → `suspended`; `Canceled` → `canceled`; `Expired` → `expired`; `Paused` → `paused`; `Unpaid` → `unpaid`; `TrialEnded` → `trial_ended`; `OnHold` → `on_hold`; `AwaitingSignup` → `awaiting_signup` | Subscription DTO state; emit its wire value. | `map/models/enums.md` |
| `CollectionMethod` | `Automatic` → `automatic`; `Remittance` → `remittance`; `Prepaid` → `prepaid`; `Invoice` → `invoice` | Set `Remittance` on create for this payment-free/no-card flow. The enum contract identifies `remittance` as valid for current Relationship Invoicing; `invoice` is a legacy Statements Architecture value. | `map/models/enums.md` |
| `PricePointType` | `Catalog` → `catalog`; `Default` → `default`; `Custom` → `custom` | Returned by subscriptions if later exposed; not required in the initial DTO. | `map/models/enums.md` |

### Endpoint DTO mapping

| Endpoint | Input / output | Mapping and invariant |
|---|---|---|
| `GET /api/subscription-plans` | Array of `{ handle, name, description, priceInCents, interval, intervalUnit, pricePointHandle }` | All values come from each unwrapped `Product`; omit invalid/null-handle, archived, or `RequireCreditCard == true` products. `priceInCents` stays an integer because `Product` has no currency field. Seeded handles (`eshop-pro`, `basic-plan`) are discovered, not hardcoded. |
| `POST /api/subscriptions` | Body `{ productHandle: string }`; return the subscription DTO below with 201 for the winning create, 200 for an existing completed enrollment, and a deterministic in-progress/conflict response for a concurrent winner. | Re-read the product by handle, verify family handle/card eligibility, then use the validated handles with `PaymentCollectionMethod = CollectionMethod.Remittance` in `CreateSubscription`. Never accept price, family, user, customer ID, product ID, or price-point ID from the caller. |
| `GET /api/my-subscriptions` and POST result | Array/item `{ id, planHandle, planName, priceInCents, currency, state, nextBillingAt }` | `id ← Subscription.Id`; plan fields ← nested `Product`; price ← `ProductPriceInCents`; currency ← `Currency`; state ← `SubscriptionState` wire value. Map `nextBillingAt` best-effort from `NextAssessmentAt`, fall back to `CurrentPeriodEndsAt`, else null. The map proves both generated fields but not their live business equivalence, so this fallback is **UNVERIFIED** and must remain explicit in tests/DTO documentation. Filter GET results by nested `Product.ProductFamily.Handle`. |

### Deterministic identity and duplicate protection

| Layer | Required design | Capability boundary / source |
|---|---|---|
| JWT identity | Require authenticated issuer and subject (`sub`, with the framework's mapped `ClaimTypes.NameIdentifier` accepted as the same subject), plus email/first/last name from trusted claims or the application's identity store. Form `userKey = Base64Url(SHA256(UTF8(issuer + "\n" + subject)))`; `customerReference = "eshop-user:" + userKey`. Do not log raw tokens/claims. | `CreateCustomer` requires first name, last name, and email; `Reference` is optional. `records-1-Ac-Cr.md` |
| Customer exactly-once | Read by `customerReference`. Create only on raw 404. `CreateCustomer` documents that only one customer can exist for a given reference. If create returns 422, read by the same reference again; if present, another request won. | Unique-reference guarantee and operations: `operations/Customers.md` |
| Enrollment key | `subscriptionReference = "eshop-subscription:" + Base64Url(SHA256(UTF8(customerReference + "\n" + normalizedProductHandle)))`. Store a local enrollment row with a database unique constraint on `(userKey, normalizedProductHandle)`, status (`Pending`, `Completed`, `NeedsReconciliation`), deterministic reference, nullable remote subscription ID, timestamps, and concurrency token. This is additive persistence; do not reuse or change order/cart entities. | Application guarantee; the SDK exposes `CreateSubscription.Reference` but documents no uniqueness for it. `records-2-Cr-Ne.md`; `operations/Subscriptions.md` |
| Double click/concurrency | Only the transaction that inserts/claims the unique enrollment row may call Maxio. Losers load and return the completed result, or report in-progress; they never call create. Before the winner calls create, list the customer subscriptions and match exact `Subscription.Reference`; if found, complete locally without creating. | `ListCustomerSubscriptions` is unpaginated; `Subscription.Reference` exists. `operations/Customers.md`; `records-3-Of-Su.md` |
| Ambiguous write | On timeout/transport uncertainty after issuing create, record `NeedsReconciliation`; do not automatically initiate another application-level create. Reconcile by customer + exact subscription reference. If absent, keep the operation unresolved for controlled retry/operations review rather than claiming exactly-once. | `CreateSubscription` has no idempotency parameter/header and the map does not document uniqueness for subscription `Reference`. Absolute provider-side exactly-once cannot be proven from this SDK contract. `operations/Subscriptions.md`; `records-2-Cr-Ne.md` |

### Error boundary

| Condition | Boundary behavior | Grounding |
|---|---|---|
| Case A typed error | Catch the exact closed `MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>`, try its operation-specific 422/404 accessor, then inherited `TryGetRawError(out RawError)`. Never assume a typed accessor exists on a Case B operation. | Each operation row above; `sdk-map.md` |
| Case B raw error | Catch `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; classify using `Error.StatusCode`; read a bounded/sanitized body only when needed. | `sdk-map.md` |
| 401/403 | Treat as server configuration/auth failure, never as caller authentication failure; return a sanitized dependency error and log no credentials. | Auth and raw status contracts in `sdk-map.md` |
| Product/customer 404 | Product handle 404 is caller-visible unknown plan; customer-by-reference 404 is expected absence (`[]` for GET, create path for POST). | `operations/Products.md`; `operations/Customers.md` |
| 422 create customer | Attempt typed extraction best-effort, then lookup by deterministic reference; existing customer means concurrent success, otherwise return a sanitized validation/dependency failure. | `operations/Customers.md`; `records-2-Cr-Ne.md` |
| 422 create subscription | Extract `ErrorListResponse1.Errors`; do not expose unsanitized provider text. Mark local enrollment failed only for a deterministic rejection; ambiguous failures remain `NeedsReconciliation`. | `operations/Subscriptions.md`; `records-2-Cr-Ne.md` |
| Cancellation | Pass the incoming request token as named `ct:` on every call and allow cancellation to propagate; do not translate caller cancellation into a dependency outage. | All operation pages above |

### SDK limitations / capability gaps

| Gap | Required disposition | Source |
|---|---|---|
| `ReadProductFamily` notes mention handle addressing, but its generated signature is `ReadProductFamily(int id, CancellationToken ct = default)` and cannot accept a handle. | Do not invent or call a string overload. Resolve the family from `ListProductFamilies` by exact handle, then use the returned transient numeric ID for `ListProductsForProductFamily`. | `operations/ProductFamilies.md` |
| Product/customer/subscription response fields needed here are nullable, and `ProductFamilyResponse.ProductFamily` / `SubscriptionResponse.Subscription` may be null. | Validate envelopes/fields and classify a missing required business field as provider contract drift; never dereference blindly. | `records-3-Of-Su.md`; `records-4-Su-We.md` |
| Create-customer's generated 422 payload points to a shared `Errors` model whose fields are `per_page` and `price_point`, not customer fields. | Treat typed extraction as best-effort and use reference re-lookup; otherwise fall back to a generic sanitized message. **UNVERIFIED** live error shape. | `operations/Customers.md`; `records-2-Cr-Ne.md` |
| `CreateSubscription` exposes no idempotency header/key parameter, and uniqueness of `CreateSubscription.Reference` is not documented. | The local unique enrollment gate prevents double-click/concurrent application calls. Absolute remote exactly-once across an ambiguous transport result remains unprovable and is a blocker if the acceptance criterion requires that stronger guarantee. | `operations/Subscriptions.md`; `records-2-Cr-Ne.md` |
| No-card signup is conditional on product/site options; omitting collection method caused the live sandbox to choose charge collection and reject the $299 balance without a payment method. | Send no payment/card/bank fields, expose only products where `RequireCreditCard is not true`, and set `PaymentCollectionMethod = CollectionMethod.Remittance` for current Relationship Invoicing. `CollectionMethod.Invoice` is not substituted because the enum contract limits it to legacy Statements Architecture. | `operations/Subscriptions.md`; `records-2-Cr-Ne.md`; `records-3-Of-Su.md`; `map/models/enums.md` |
| `Product` has `PriceInCents` but no currency; `Subscription` has `Currency`. | Plans return integer cents without a fabricated currency. Add a separately grounded site/currency operation only if the UI requires a currency code on the plans endpoint. | `records-3-Of-Su.md` |

## Trap notes

⚠ Step 1 (client registration) — `HttpClient` ownership/lifetime and the SDK wrapper's DI lifetime can cause socket churn or handler loss if guessed from the constructor. **MUST load `dotnet-client-initialization`** before registering the client.

⚠ Step 1 (authentication) — credential timing, rotation, and safe configuration can make requests unauthenticated or leak a key if inferred from the property names. **MUST load `dotnet-authentication`** before wiring credentials.

⚠ Steps 3–6 (calls) — generated optional parameters without C# defaults, response envelopes, and the literal `ct` name can silently mis-bind calls or drop cancellation. **MUST load `dotnet-calling-endpoints`** before the first SDK call.

⚠ Steps 3–6 (models) — required members, nullable response payloads, wire names, and `StringEnum<T>` handling can cause compile failures or incorrect DTO values. **MUST load `dotnet-models`** before constructing or mapping SDK models.

⚠ Steps 1, 5, and 7 (resilience) — retry/timeout/server settings can resend a write, outlive the intended request budget, or route to the wrong host; this directly affects duplicate-subscription guarantees. **MUST load `dotnet-configuration-resilience`** before wiring the client or write policy.

⚠ Step 7 (error boundary) — Case A and Case B exceptions expose status/body through different paths, and cancellation/configuration failures need different translations. **MUST load `dotnet-error-handling`** before writing any catch ladder.

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
  maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
  and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 8 (tests) — mocking generated controllers couples tests to SDK internals and misses envelope/error/retry behavior. **MUST load `dotnet-testing`** before choosing the test seam.

## REQUIRED READING

Load every item below **before implementation starts**. This contract sheet deliberately does not carry their contents.

| Skill | Step governed |
|---|---|
| `dotnet-client-initialization` | Step 1 — `HttpClient`, constructor, and DI registration |
| `dotnet-authentication` | Step 1 — Basic credentials and configuration/rotation |
| `dotnet-calling-endpoints` | Steps 3–6 — controller calls, named parameters, envelopes, async, cancellation |
| `dotnet-models` | Steps 3–6 — request initialization, nullability, wire names, `StringEnum<T>` |
| `dotnet-configuration-resilience` | Steps 1, 3, 5, 7 — server selection, verb-sensitive retry risk, timeout budget, pagination |
| `dotnet-error-handling` | Steps 4–7 — Case A/B exception and `JsonException` boundary |
| `dotnet-testing` | Step 8 — `HttpClient` seam and behavioral coverage |

## Assumptions & Blockers

- **Assumption:** `MAXIO_ENVIRONMENT` will contain the SDK region selector `US` or `EU`. The SDK map exposes only `ServerEnvironment.Us` and `.Eu`; it has no `Sandbox` member. If the supplied value is `sandbox`, a URL, or another label, its intended mapping must be clarified before client registration.
- **Assumption:** the validated JWT or the application's identity store can provide a stable issuer + subject and the required customer first name, last name, and email. If those attributes are unavailable, `CreateCustomer` cannot satisfy its generated required model without an agreed profile-source or fallback policy.
- **Assumption:** additive application persistence and a database unique constraint may be added for the enrollment gate. This does not alter existing one-time commerce behavior.
- **Blocker for a stronger exactly-once claim:** the SDK contract provides no idempotency parameter/header on `CreateSubscription`, and the map does not document subscription-reference uniqueness. The local gate guarantees that a double click/concurrent application request does not issue two create calls, but absolute provider-side exactly-once after an ambiguous transport outcome cannot be proven. Such outcomes must stop in reconciliation unless the user accepts this boundary or a live/provider guarantee is supplied.
- **Assumption:** the sandbox uses current Relationship Invoicing, for which the SDK enum contract accepts `CollectionMethod.Remittance`; the live 422 confirmed that omitting this field selected charge collection. If the site is actually on legacy Statements Architecture, its supported no-card value is `CollectionMethod.Invoice` and the configured architecture must be confirmed before substitution.
- **Assumption / UNVERIFIED semantic mapping:** `nextBillingAt` uses generated `NextAssessmentAt`, falling back to generated `CurrentPeriodEndsAt`. Both fields are map-grounded, but the map does not establish their live business equivalence for every subscription state.
- **Limitation:** catalog `Product` exposes `PriceInCents` but no currency. The initial plans endpoint therefore returns cents without inventing a currency; requiring a catalog currency code expands SDK scope and needs another grounded operation.
