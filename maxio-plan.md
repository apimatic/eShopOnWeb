# Maxio Advanced Billing integration plan

## 1. Scope & sequence

| Step | Maxio operation(s) | Integration consequence | Source |
|---|---|---|---|
| 0. Package, configuration, client | none | Reference NuGet `AsadAli.AdvancedBilling.Sdk` at `1.0.2` (the mapped/tagged contract), bind exactly `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and optional `Maxio:BaseUrl`, validate required settings at startup, and construct one SDK client over an `IHttpClientFactory`-managed `HttpClient`. Use Basic auth with the configured API key as username and literal `"x"` as password. | `sdk-map.md`; configuration names are fixed by the task |
| 1. Discover plans | `ProductFamilies.ListProductFamilies`; `ProductFamilies.ListProductsForProductFamily` | Find the configured family by exact `ProductFamily.Handle`, require its non-null `Id`, then page through that family's non-archived products. Numeric family/product IDs are runtime data only. The generated `ReadProductFamily(int id, ...)` cannot express the Notes-documented `handle:...` path, so it is deliberately not used for stable-handle resolution. | `operations/ProductFamilies.md`; `records-3-Of-Su.md` |
| 2. Ensure the shopper's Maxio customer | `Customers.ReadCustomerByReference`; when absent, `Customers.CreateCustomer`; after a concurrent-create rejection, read again | Use the app's canonical immutable user ID as Maxio customer `reference`. Maxio explicitly permits only one customer for a given reference. Customer creation also needs non-empty first name, last name, and email. | `operations/Customers.md`; `records-1-Ac-Cr.md`; `records-2-Cr-Ne.md` |
| 3. Enroll exactly once | `Subscriptions.FindSubscription`; `Subscriptions.CreateSubscription`; reconcile with `FindSubscription` after an ambiguous failure | Accept a product handle, verify it belongs to the configured family and is not archived, derive a deterministic application subscription reference from user ID + product handle, and serialize/reserve the `(userId, productHandle)` enrollment in application persistence. Create with `ProductHandle`, `CustomerReference`, `Reference`, and `PaymentCollectionMethod = CollectionMethod.Remittance`; do not use seeded numeric IDs. The sandbox rejected the omitted collection method by attempting automatic collection without a payment profile; `Remittance` is the mapped non-automatic option for current Relationship Invoicing. Maxio's mapped contract has no idempotency-key parameter/field and does not document subscription-reference uniqueness, so preflight lookup alone cannot prevent two simultaneous creates. | `operations/Subscriptions.md`; `records-2-Cr-Ne.md`; `enums.md`; rejection confirmed by sandbox traffic |
| 4. Return subscription confirmation | response from `CreateSubscription` or `FindSubscription` | Map product identity from `Subscription.Product`, recurring price from `Subscription.ProductPriceInCents`, state from `Subscription.State`, currency from `Subscription.Currency`, and next billing date from `Subscription.NextAssessmentAt` (with `CurrentPeriodEndsAt` exposed separately if desired). Treat missing envelope/member data as an upstream contract failure rather than inventing defaults. | `records-3-Of-Su.md`; `records-4-Su-We.md`; DTO naming is `YOUR CALL — not in the map` |
| 5. Show the caller's subscriptions | `Customers.ReadCustomerByReference`; `Customers.ListCustomerSubscriptions` | If no Maxio customer exists, return an empty application collection; otherwise require `Customer.Id` and return all subscriptions from the customer operation. | `operations/Customers.md`; `records-2-Cr-Ne.md`; exact lookup-miss status is `UNVERIFIED` by the map |
| 6. Boundary, resilience, tests | all operations above | Translate SDK failures at one integration boundary, preserve cancellation, bound calls, log correlation-safe metadata (never the API key), unit-test serialized requests/envelopes/errors with a fake handler, test concurrency against the application persistence seam, and run one sandbox smoke flow. | SDK surface below; application policy is `YOUR CALL — not in the map` |

HTTP exposure belongs to the application: JWT-authenticated `GET /api/subscription-plans`, `POST /api/subscriptions`, and `GET /api/my-subscriptions`; identity must come only from the authenticated token, never a request-supplied user ID. Suggested application request is `{ "productHandle": "..." }`; do not hard-code `eshop-pro`, `basic-plan`, `eshop-subscribe`, or any numeric ID. These are application contracts and therefore **YOUR CALL — not in the map**.

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

### SDK identity, client, authentication, and server

| Contract | Exact SDK surface | Source |
|---|---|---|
| Package/version | NuGet `AsadAli.AdvancedBilling.Sdk`, pin `1.0.2`; root namespace `MaxioAdvancedBilling`; target `netstandard2.0`. Do not add SDK source or a project reference. | `sdk-map.md` |
| Constructor | `MaxioAdvancedBilling.MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)`; API groups used are `.ProductFamilies`, `.Customers`, `.Subscriptions`. | `sdk-map.md` |
| Options | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` has `Environment: MaxioAdvancedBilling.Servers.ServerEnvironment`, `Retry: MaxioAdvancedBilling.Core.Configuration.RetryOptions`, `Server: MaxioAdvancedBilling.ServerOptions`, `BasicAuth: MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?`. | `sdk-map.md` |
| Authentication | `new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = configured ApiKey, Password = "x" }`; Basic auth is the only mapped scheme. | `sdk-map.md` |
| US site-derived URL | Set `options.Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us`; when `Maxio:BaseUrl` is blank, set `options.Server.Production.Us.Site` from `Maxio:Subdomain`, producing mapped template `https://{site}.chargify.com`. | `sdk-map.md`; configuration key is fixed by task |
| Verbatim base override | When `Maxio:BaseUrl` is nonblank, assign its value verbatim to `options.Server.Production.Us.BaseUrl` instead of deriving from the subdomain. All operations in this plan use the Production server group. | `sdk-map.md`; precedence is fixed by task |
| Sandbox | The SDK exposes hosting choices `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` and `.Eu`, not a `Sandbox` enum. For this task, sandbox isolation is supplied by the configured sandbox site/subdomain (or explicit base override); do not invent `Maxio:Environment` or hard-code `cp-exp-3`. | `sdk-map.md`; task mandate |
| SDK environment variable | `MAXIO_ENVIRONMENT` has no corresponding allowed `Maxio:` key in the task's exact four-key binding contract and no SDK `Sandbox` enum. Do not add a fifth option merely to consume it. | `YOUR CALL — not in the map; constrained by task` |

### Operations

| Purpose / controller | Exact generated signature and return | Request/query contract | Response envelope and fields consumed | Error contract | Pagination | Source |
|---|---|---|---|---|---|---|
| Discover configured family · `client.ProductFamilies` | `ListProductFamilies(MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, System.DateTimeOffset? startDate, System.DateTimeOffset? endDate, System.DateTimeOffset? startDatetime, System.DateTimeOffset? endDatetime, System.Threading.CancellationToken ct = default)` → `IReadOnlyList<MaxioAdvancedBilling.Models.ProductFamilyResponse>` | No body. Pass all five nullable, no-default filters explicitly (named arguments, all `null` here). | Each `MaxioAdvancedBilling.Models.ProductFamilyResponse.ProductFamily (product_family): MaxioAdvancedBilling.Models.ProductFamily?`; read `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?`, `ArchivedAt (archived_at): DateTimeOffset?`. Match exact configured handle and require a usable ID. | Case B `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; read `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, or `ReadAsBytes()`. | none | `operations/ProductFamilies.md`; `records-3-Of-Su.md` |
| List family plans · `client.ProductFamilies` | `ListProductsForProductFamily(string productFamilyId, MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, MaxioAdvancedBilling.Models.ListProductsFilter? filter, System.DateTimeOffset? startDate, System.DateTimeOffset? endDate, System.DateTimeOffset? startDatetime, System.DateTimeOffset? endDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, System.Threading.CancellationToken ct = default)` → `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>` | No body. Pass the discovered numeric family ID as invariant string. Pass all nullable no-default arguments explicitly; set `includeArchived: false`; use named `page`, `perPage`, `ct`. `filter` fields (unused here): `Ids (ids): IReadOnlyList<int>?`, `PrepaidProductPricePoint (prepaid_product_price_point): MaxioAdvancedBilling.Models.PrepaidProductPricePointFilter?`, `UseSiteExchangeRate (use_site_exchange_rate): bool?`. | Each `MaxioAdvancedBilling.Models.ProductResponse.Product (product): MaxioAdvancedBilling.Models.Product` **required**; read `Id`, `Name`, `Handle`, `Description`, `PriceInCents`, `Interval`, `IntervalUnit`, `ArchivedAt`, `RequireCreditCard`, and `ProductFamily`. All product fields themselves are nullable. | Case A `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`; `TryGetString(out string)` [404], inherited `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` fallback. | manual `page` + `perPage`; continue until a page contains fewer than `perPage` items | `operations/ProductFamilies.md`; `records-2-Cr-Ne.md`; `records-3-Of-Su.md` |
| Lookup customer · `client.Customers` | `ReadCustomerByReference(string reference, System.Threading.CancellationToken ct = default)` → `MaxioAdvancedBilling.Models.CustomerResponse` | Query wire `reference`; pass canonical application user ID. | `MaxioAdvancedBilling.Models.CustomerResponse.Customer (customer): MaxioAdvancedBilling.Models.Customer` **required**; read `Id (id): int?`, `Reference`, `FirstName`, `LastName`, `Email`. | Case B `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` with `StatusCode` and raw readers. The map does not name the lookup-miss status/body: treat only an observed HTTP not-found as absence and all other statuses as failure. | none | `operations/Customers.md`; `records-2-Cr-Ne.md`; miss response is `UNVERIFIED` |
| Create customer · `client.Customers` | `CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, System.Threading.CancellationToken ct = default)` → `MaxioAdvancedBilling.Models.CustomerResponse` | Envelope `MaxioAdvancedBilling.Models.CreateCustomerRequest.Customer (customer): MaxioAdvancedBilling.Models.CreateCustomer` **required**. Inner required fields: `FirstName (first_name): string`, `LastName (last_name): string`, `Email (email): string`; also send `Reference (reference): string?` even though optional because the operation Notes make it the unique application identifier. Omit address/tax/locale fields unless the application has validated data for them. | Same customer envelope/fields as lookup. | Case A `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`; `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422], inherited raw fallback. Payload has `Errors (errors): MaxioAdvancedBilling.Models.Errors?`; that mapped type only exposes `PerPage` and `PricePoint`, unrelated to customer validation, so extract best-effort and fall back to a generic safe message. | none | `operations/Customers.md`; `records-1-Ac-Cr.md`; `records-2-Cr-Ne.md`; payload trust is `UNVERIFIED` |
| Find deterministic enrollment · `client.Subscriptions` | `FindSubscription(string? reference, System.Threading.CancellationToken ct = default)` → `MaxioAdvancedBilling.Models.SubscriptionResponse` | Query wire `reference`; although nullable in generated code, always pass the deterministic non-null application subscription reference. | `MaxioAdvancedBilling.Models.SubscriptionResponse.Subscription (subscription): MaxioAdvancedBilling.Models.Subscription?`; require it when returning a successful lookup. Read fields listed in the subscription projection below. | Case A `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`; `TryGetNoContent(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` [404], inherited raw fallback. | none | `operations/Subscriptions.md`; `records-4-Su-We.md` |
| Create enrollment · `client.Subscriptions` | `CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, System.Threading.CancellationToken ct = default)` → `MaxioAdvancedBilling.Models.SubscriptionResponse` | Envelope `MaxioAdvancedBilling.Models.CreateSubscriptionRequest.Subscription (subscription): MaxioAdvancedBilling.Models.CreateSubscription` **required**. `MaxioAdvancedBilling.Models.CreateSubscription` marks no fields required, but the operation Notes require one product selector and one customer selector. Send exactly `ProductHandle (product_handle): string?`, `CustomerReference (customer_reference): string?`, deterministic `Reference (reference): string?`, and `PaymentCollectionMethod (payment_collection_method): MaxioAdvancedBilling.Models.Enums.CollectionMethod? = CollectionMethod.Remittance`. Do not send product/customer numeric IDs, payment-profile fields, custom price, coupon, components, billing-date overrides, import fields, or customer attributes for this flow. The sandbox proved omission selects an automatic charge path and fails without a payment profile. | `MaxioAdvancedBilling.Models.SubscriptionResponse.Subscription` and projection below. | Case A `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`; `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422], inherited raw fallback. Payload `Errors (errors): IReadOnlyList<string>` **required**. | none | `operations/Subscriptions.md`; `records-2-Cr-Ne.md`; `records-4-Su-We.md`; `enums.md`; omission behavior confirmed by sandbox traffic |
| List caller subscriptions · `client.Customers` | `ListCustomerSubscriptions(int customerId, System.Threading.CancellationToken ct = default)` → `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>` | Path uses required Maxio customer ID from the lookup response. | Each envelope has nullable `Subscription`; require a value per successful list item before projecting it. | Case B `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` with `StatusCode` and raw readers. | none | `operations/Customers.md`; `records-4-Su-We.md` |

`ReadProductFamily(int id, CancellationToken ct = default)` is intentionally excluded from execution: its operation Notes say the path accepts `handle:my-family`, but the generated C# parameter is `int`, so this package cannot pass that form. List-and-match is the map-grounded stable-handle path. Source: `operations/ProductFamilies.md`.

### Focused model projection

| SDK model | Exact members used | Source |
|---|---|---|
| `MaxioAdvancedBilling.Models.Product` | `Id (id): int?`; `Name (name): string?`; `Handle (handle): string?`; `Description (description): string?`; `PriceInCents (price_in_cents): long?`; `Interval (interval): int?`; `IntervalUnit (interval_unit): MaxioAdvancedBilling.Models.Enums.IntervalUnit?`; `ArchivedAt (archived_at): DateTimeOffset?`; `RequireCreditCard (require_credit_card): bool?`; `ProductFamily (product_family): MaxioAdvancedBilling.Models.ProductFamily?` | `records-3-Of-Su.md` |
| `MaxioAdvancedBilling.Models.Subscription` | `Id (id): int?`; `State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?`; `ProductPriceInCents (product_price_in_cents): long?`; `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`; `NextAssessmentAt (next_assessment_at): DateTimeOffset?`; `Customer (customer): MaxioAdvancedBilling.Models.Customer?`; `Product (product): MaxioAdvancedBilling.Models.Product?`; `CurrentBillingAmountInCents (current_billing_amount_in_cents): long?`; `Reference (reference): string?`; `Currency (currency): string?` | `records-3-Of-Su.md` |
| `MaxioAdvancedBilling.Models.SubscriptionResponse` | `Subscription (subscription): MaxioAdvancedBilling.Models.Subscription?` | `records-4-Su-We.md` |

### Enum values actually consumed

These are `StringEnum<T>` generated types, not C# enums; load the models skill before constructing, comparing, or extracting their wire values.

| Fully-qualified type | Static member → wire value | Source |
|---|---|---|
| `MaxioAdvancedBilling.Models.Enums.CollectionMethod` | `Automatic` → `automatic`; `Remittance` → `remittance`; `Prepaid` → `prepaid`; `Invoice` → `invoice`. For current Relationship Invoicing the mapped valid values are `Remittance`, `Automatic`, and `Prepaid`; `Invoice` is for legacy Statements Architecture. | `enums.md` |
| `MaxioAdvancedBilling.Models.Enums.IntervalUnit` | `Day` → `day`; `Month` → `month` | `enums.md` |
| `MaxioAdvancedBilling.Models.Enums.SubscriptionState` | `Pending` → `pending`; `FailedToCreate` → `failed_to_create`; `Trialing` → `trialing`; `Assessing` → `assessing`; `Active` → `active`; `SoftFailure` → `soft_failure`; `PastDue` → `past_due`; `Suspended` → `suspended`; `Canceled` → `canceled`; `Expired` → `expired`; `Paused` → `paused`; `Unpaid` → `unpaid`; `TrialEnded` → `trial_ended`; `OnHold` → `on_hold`; `AwaitingSignup` → `awaiting_signup` | `enums.md` |

### Idempotency contract

| Mechanism | What the mapped contract guarantees | Required consequence | Source |
|---|---|---|---|
| Customer reference | `CreateCustomer` Notes explicitly allow only one customer for a given `reference`; `ReadCustomerByReference` returns a single match. | Use canonical user ID; lookup-before-create; on a concurrent 422/fallback rejection, lookup again and accept the winner. | `operations/Customers.md` |
| Subscription reference | `CreateSubscription.Reference` exists and `FindSubscription` finds by reference. The map does **not** say the reference is unique. | Use a deterministic reference for reconciliation, but do not treat it as an atomic duplicate barrier. | `operations/Subscriptions.md`; `records-2-Cr-Ne.md` |
| Idempotency key | No idempotency header parameter or request field exists in the full generated `CreateSubscription` signature/model. | Enforce a unique `(userId, productHandle)` reservation in application persistence and serialize competing enrollments; reconcile ambiguous outcomes with `FindSubscription`. | `operations/Subscriptions.md`; `records-2-Cr-Ne.md`; persistence is `YOUR CALL — not in the map` |

### Error-boundary directives

- Catch the exact generic SDK exception for each operation; Case A typed errors and Case B raw errors are not interchangeable.
- For typed errors, try the status-specific accessor first and inherited `TryGetRawError` second. For Case B, use `RawError.StatusCode` and its readers. Do not parse exception `.ToString()`.
- Customer validation detail is low-trust: the generated `CustomerErrorResponse1.Errors` type exposes only `PerPage` and `PricePoint`. Extract best-effort, log correlation-safe raw metadata if available, and return a generic validation message when the generated shape cannot represent the provider body. **UNVERIFIED** until sandbox traffic confirms it.
- Never return Maxio raw bodies, credentials, or internal exception text to the caller.

## 3. Trap notes

⚠ Step 0 (client registration) — `HttpClient` ownership/lifetime and the SDK wrapper's DI lifetime can create socket churn or stale configuration if wired incorrectly. **MUST load `maxio-sdk:dotnet-client-initialization`** before registering the client.

⚠ Step 0 (authentication) — credential timing, rotation, and configuration binding determine whether every call fails with 401/403. **MUST load `maxio-sdk:dotnet-authentication`** before wiring Basic auth.

⚠ Steps 1–5 (calls) — nullable parameters without C# defaults still must be supplied, and positional calls can silently bind the wrong filter; `ct` is the literal cancellation name. **MUST load `maxio-sdk:dotnet-calling-endpoints`** before the first SDK call.

⚠ Steps 1–5 (models) — required envelopes, nullable inner objects, `StringEnum<T>` value handling, and dropped unmodeled JSON can corrupt mapping or make a valid-looking initializer fail. **MUST load `maxio-sdk:dotnet-models`** before constructing or projecting models.

⚠ Steps 2–6 (error boundary) — Case A/Case B exception shapes, raw fallbacks, and status preservation determine whether validation, absence, and outages are classified correctly. **MUST load `maxio-sdk:dotnet-error-handling`** before writing any catch block.

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
  maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
  and a caller that retries 5xx retries something that can never succeed.

**MUST load `maxio-sdk:dotnet-error-handling`** before writing that boundary.

⚠ Steps 0, 1, and 3 (resilience/server/pagination) — base-address precedence, what a timeout bounds, and whether a failed subscription write can be re-sent directly affect cross-site safety and duplicate creation. **MUST load `maxio-sdk:dotnet-configuration-resilience`** before configuring the client or retry policy.

⚠ Step 6 (tests) — choosing the wrong fake seam couples tests to generated controllers and misses actual serialization/error behavior. **MUST load `maxio-sdk:dotnet-testing`** before writing integration-layer tests.

## 4. REQUIRED READING

Load every item below **before implementation starts**. This sheet deliberately does not carry their contents.

- `maxio-sdk:dotnet-client-initialization` — Step 0 client/DI construction and `HttpClient` ownership.
- `maxio-sdk:dotnet-authentication` — Step 0 Basic credentials and rotation.
- `maxio-sdk:dotnet-calling-endpoints` — Steps 1–5 controller calls, named optional arguments, async/cancellation.
- `maxio-sdk:dotnet-models` — Steps 1–5 request envelopes, required/nullability rules, `StringEnum<T>` values.
- `maxio-sdk:dotnet-error-handling` — Steps 2–6 exact exception ladder, raw/typed payloads, both `JsonException` directions.
- `maxio-sdk:dotnet-configuration-resilience` — Steps 0, 1, 3 retries, timeout, server override, manual pagination, logging limitations.
- `maxio-sdk:dotnet-testing` — Step 6 HTTP seam, success/error/concurrency coverage.

## 5. Assumptions & Blockers

### Assumptions

- The application's authenticated identity path supplies a canonical immutable user ID plus non-empty first name, last name, and email. The first three customer fields are required by `MaxioAdvancedBilling.Models.CreateCustomer`; how the app obtains them is **YOUR CALL — not in the map**.
- A POST selects a product by handle and the application verifies that handle against the configured family's live, non-archived product list. Seed handles are test data, not constants.
- `Subscription.NextAssessmentAt` is the application's `nextBillingDate` projection. That semantic DTO choice is **YOUR CALL — not in the map**; the SDK also exposes `CurrentPeriodEndsAt` separately.
- The seeded no-payment products require explicit `CollectionMethod.Remittance` for this no-card flow. Sandbox traffic proved that omitting the collection method attempts automatic collection and rejects the `$299.00` balance without a payment profile. A different configured catalog must reject or route payment-required products to a separately designed payment flow.
- Development with the in-memory provider can guarantee idempotency only within one process run; durable production behavior requires the application's persistent unique enrollment reservation.
- A missing customer lookup is represented by an HTTP not-found raw error. The map does not name that status for `ReadCustomerByReference`; this is **UNVERIFIED** and must be confirmed by the sandbox smoke test.

### Blockers

- None for planning. The mapped SDK has no atomic subscription-idempotency primitive, so the application-level unique reservation/concurrency guard is mandatory to satisfy the task.
