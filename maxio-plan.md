# Maxio subscription integration plan

## 1. Scope & sequence

| Step | Work and Maxio operation(s) | Source |
|---|---|---|
| 1 | Add `AsadAli.AdvancedBilling.Sdk` pinned to the SDK-map source release `1.0.2`; register one configured `MaxioAdvancedBilling.MaxioAdvancedBillingClient` behind an application-owned subscription gateway. Bind only `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and optional `Maxio:BaseUrl`; validate required values at startup without logging the key. | `sdk-map.md` |
| 2 | Implement `GET /api/subscription-plans`: `ListProductFamilies` to resolve the configured family by `ProductFamily.Handle`, then `ListProductsForProductFamily` with that returned numeric ID string. Return only the configured family’s non-archived plans and map the returned product’s identity, price, interval, trial/expiration, and card requirement. | `operations/ProductFamilies.md`; `records-3-Of-Su.md` |
| 3 | Resolve the caller from the API’s established JWT identity path and create an application-owned canonical Maxio customer reference. `ReadCustomerByReference` first; on absence create with `CreateCustomer` using that same reference; on a create 422, re-read by reference before deciding failure. Persist the returned Maxio customer ID in the application’s durable user mapping. | `operations/Customers.md`; `records-1-Ac-Cr.md`; `records-2-Cr-Ne.md` |
| 4 | Implement `POST /api/subscriptions`: validate the requested plan against Step 2’s configured-family lookup; take a durable per-caller-and-plan idempotency lock/record; resolve the customer; `FindSubscription` by a deterministic subscription reference; only if absent call `CreateSubscription` with the resolved customer ID, requested product handle, and that reference. Save the returned Maxio subscription ID/reference before releasing the lock. | `operations/Subscriptions.md`; `records-2-Cr-Ne.md`; `records-4-Su-We.md` |
| 5 | Return the creation result from `SubscriptionResponse.Subscription`: plan (`Product`), price (`ProductPriceInCents`), state, and next billing (`NextAssessmentAt`; it is the available next-assessment field). Do not assert an access decision from transient states. | `records-3-Of-Su.md`; `records-4-Su-We.md`; `models/enums.md` |
| 6 | Implement `GET /api/my-subscriptions`: resolve caller/customer, then `ListCustomerSubscriptions`; unwrap each `SubscriptionResponse.Subscription`; retain subscriptions whose nested `Product.ProductFamily.Handle` equals the configured handle and return the same subscription DTO. | `operations/Customers.md`; `records-3-Of-Su.md`; `records-4-Su-We.md` |
| 7 | Translate Maxio exceptions at the application gateway to sanitized API errors; test the gateway with an `HttpClient`/handler seam and endpoint authorization/idempotency/mapping behavior without real credentials. | `sdk-map.md`; `operations/Customers.md`; `operations/Subscriptions.md` |

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

| Controller property / operation | Generated signature (parameters in order) | Request model and fields used / intentional omissions | Response envelope and fields read | Error contract | Pagination | Source |
|---|---|---|---|---|---|---|
| `client.ProductFamilies.ListProductFamilies` | `ListProductFamilies(MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` | No body. Pass all five nullable filters explicitly as `null`. | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductFamilyResponse>`; unwrap `.ProductFamily` (`product_family`) and read `Id` (`id`), `Handle` (`handle`), `ArchivedAt` (`archived_at`). | Case B: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; `.Error.StatusCode`, `.Error.ReadAsString()`, `.Error.ReadAsJson<T>()`, `.Error.ReadAsBytes()`. | None | `operations/ProductFamilies.md`; `records-3-Of-Su.md`; `sdk-map.md` |
| `client.ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, MaxioAdvancedBilling.Models.Enums.ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | No body. Pass each nullable filter explicitly; use the ID found above as its string representation; pass `includeArchived: false`; leave `include` null. | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>`; unwrap required `.Product` (`product`). Read `Id`/`Handle`/`Name`/`Description`, `PriceInCents`, `Interval`, `IntervalUnit`, `TrialPriceInCents`, `TrialInterval`, `TrialIntervalUnit`, `ExpirationInterval`, `ExpirationIntervalUnit`, `ArchivedAt`, `RequestCreditCard`, `RequireCreditCard`, `Taxable`, `ProductFamily`, `ProductPricePointId`, `ProductPricePointHandle` (all wire names are the corresponding snake_case forms). | Case A: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`; `.Error.TryGetString(out string)` [404], then `.Error.TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)`. | Manual `page` + `perPage` | `operations/ProductFamilies.md`; `records-3-Of-Su.md`; `sdk-map.md` |
| `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | No body. `reference` wire query key is `reference`; supply the canonical application user reference. | `MaxioAdvancedBilling.Models.CustomerResponse`; unwrap required `.Customer` (`customer`); read `Id` (`id`) and `Reference` (`reference`). | Case B: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; raw accessors above. Treat only the application’s mapped not-found status as “create needed”; other statuses fail closed. | None | `operations/Customers.md`; `records-2-Cr-Ne.md`; `sdk-map.md` |
| `client.Customers.CreateCustomer` | `CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, CancellationToken ct = default)` | `CreateCustomerRequest.Customer` (`customer`): `MaxioAdvancedBilling.Models.CreateCustomer` with required `FirstName` (`first_name`), `LastName` (`last_name`), `Email` (`email`), and canonical `Reference` (`reference`). Other `CreateCustomer` fields are optional and intentionally omitted. The operation Notes say a supplied reference must be unique. | `MaxioAdvancedBilling.Models.CustomerResponse`; unwrap required `.Customer` (`customer`) and read `Id`, `Reference`. | Case A: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`; `.Error.TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422] or `.Error.TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)`. The typed body is `.Errors` (`errors`): `MaxioAdvancedBilling.Models.Errors?`; extract best-effort, fall back to a generic message. | None | `operations/Customers.md`; `records-1-Ac-Cr.md`; `records-2-Cr-Ne.md`; `sdk-map.md` |
| `client.Subscriptions.FindSubscription` | `FindSubscription(string? reference, CancellationToken ct = default)` | No body. `reference` wire query key is `reference`; pass the deterministic app subscription reference explicitly. | `MaxioAdvancedBilling.Models.SubscriptionResponse`; unwrap `.Subscription` (`subscription`), nullable. | Case A: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`; `.Error.TryGetNoContent(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` [404] or `.Error.TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)`. | None | `operations/Subscriptions.md`; `records-4-Su-We.md`; `sdk-map.md` |
| `client.Subscriptions.CreateSubscription` | `CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, CancellationToken ct = default)` | Required wrapper `CreateSubscriptionRequest.Subscription` (`subscription`): `MaxioAdvancedBilling.Models.CreateSubscription`. Set `ProductHandle` (`product_handle`), `CustomerId` (`customer_id`), canonical `Reference` (`reference`), and `PaymentCollectionMethod` (`payment_collection_method`) to `MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance` (`remittance`). The model itself marks no child fields required; operation Notes make product selection and existing-customer identification material: leave `ProductId`, `ProductPricePointHandle`, `ProductPricePointId`, `CustomerReference`, payment-profile fields, customer/payment attributes, and all billing/date/custom-price/component fields unset because this flow selects the product handle and pre-existing customer ID and uses the catalog default price point. The map establishes `remittance` as valid for current Relationship Invoicing, but acceptance for a particular site remains **UNVERIFIED**. | `MaxioAdvancedBilling.Models.SubscriptionResponse`; unwrap nullable `.Subscription` (`subscription`) and require it before reporting success. Read the fields in the `Subscription` row below. | Case A: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`; `.Error.TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422] or `.Error.TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)`. Typed `.Errors` (`errors`) is required `IReadOnlyList<string>`; return a safe validation summary, never raw credentials/request data. | None | `operations/Subscriptions.md`; `records-2-Cr-Ne.md`; `records-3-Of-Su.md`; `records-4-Su-We.md`; `models/enums.md`; `sdk-map.md` |
| `client.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | No body. | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>`; each wrapper has nullable `.Subscription` (`subscription`). Filter and map only non-null `Subscription` values that carry matching nested `Product.ProductFamily.Handle`. | Case B: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; raw accessors above. | None | `operations/Customers.md`; `records-3-Of-Su.md`; `records-4-Su-We.md`; `sdk-map.md` |

### Response records and enums used by the application

| SDK type (fully qualified) | Fields the integration reads | Source |
|---|---|---|
| `MaxioAdvancedBilling.Models.Subscription` | `Id (id): int?`; `Reference (reference): string?`; `State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?`; `ProductPriceInCents (product_price_in_cents): long?`; `CurrentBillingAmountInCents (current_billing_amount_in_cents): long?`; `NextAssessmentAt (next_assessment_at): DateTimeOffset?`; `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`; `Product (product): MaxioAdvancedBilling.Models.Product?`; `Customer (customer): MaxioAdvancedBilling.Models.Customer?`; `Currency (currency): string?`; `ProductPricePointId (product_price_point_id): int?`; `ProductPricePointType (product_price_point_type): MaxioAdvancedBilling.Models.Enums.PricePointType?`. | `records-3-Of-Su.md` |
| `MaxioAdvancedBilling.Models.Product` | `Id (id): int?`; `Name (name): string?`; `Handle (handle): string?`; `Description (description): string?`; `PriceInCents (price_in_cents): long?`; `Interval (interval): int?`; `IntervalUnit (interval_unit): MaxioAdvancedBilling.Models.Enums.IntervalUnit?`; `TrialPriceInCents (trial_price_in_cents): long?`; `TrialInterval (trial_interval): int?`; `TrialIntervalUnit (trial_interval_unit): MaxioAdvancedBilling.Models.Enums.IntervalUnit?`; `ExpirationInterval (expiration_interval): int?`; `ExpirationIntervalUnit (expiration_interval_unit): MaxioAdvancedBilling.Models.Enums.ExpirationIntervalUnit?`; `ArchivedAt (archived_at): DateTimeOffset?`; `RequestCreditCard (request_credit_card): bool?`; `RequireCreditCard (require_credit_card): bool?`; `Taxable (taxable): bool?`; `ProductFamily (product_family): MaxioAdvancedBilling.Models.ProductFamily?`. | `records-3-Of-Su.md` |
| `MaxioAdvancedBilling.Models.ProductFamily` | `Id (id): int?`; `Handle (handle): string?`; `ArchivedAt (archived_at): DateTimeOffset?`. | `records-3-Of-Su.md` |
| `MaxioAdvancedBilling.Models.Enums.SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)`. | `models/enums.md` |
| `MaxioAdvancedBilling.Models.Enums.IntervalUnit` | `Day (day)`, `Month (month)`. | `models/enums.md` |
| `MaxioAdvancedBilling.Models.Enums.ExpirationIntervalUnit` | `Day (day)`, `Month (month)`, `Never (never)`. | `models/enums.md` |
| `MaxioAdvancedBilling.Models.Enums.CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)`. This flow sends `Remittance`; the map lists it as valid for current Relationship Invoicing. | `models/enums.md` |

### SDK package, construction, auth, and server facts

| Concern | Contract fact | Source |
|---|---|---|
| Package and target | Package `AsadAli.AdvancedBilling.Sdk`; map source release `v1.0.2` / commit `15db14b`; target `netstandard2.0`. Root namespace `MaxioAdvancedBilling`. | `sdk-map.md` |
| Client | `MaxioAdvancedBilling.MaxioAdvancedBillingClient`; only constructor: `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)`. Controller accessors include `.Customers`, `.Subscriptions`, `.ProductFamilies`. | `sdk-map.md` |
| Basic auth | `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials` assigned to `options.BasicAuth`; `Username` is `Maxio:ApiKey`, `Password` is literal `"x"`. | `sdk-map.md` |
| Server resolution | Use `MaxioAdvancedBilling.Servers.ServerEnvironment.Us`; assign `options.Server.Production.Us.Site` from `Maxio:Subdomain`. When `Maxio:BaseUrl` is non-empty, assign that value verbatim to `options.Server.Production.Us.BaseUrl` instead of deriving the API address. Production US template absent override: `https://{site}.chargify.com`. | `sdk-map.md` |
| Retry | `MaxioAdvancedBilling.Core.Configuration.RetryOptions` has required members `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry`; begin from `RetryOptions.Default()` if customization is required. | `sdk-map.md` |
| Test seam | The `HttpClient` constructor argument is the SDK seam for a mocked handler; operations are throwing only (there are no `…Result` variants). | `sdk-map.md` |

## 3. Trap notes

⚠ Step 1 (client registration) — the SDK wrapper’s lifetime and the `HttpClient`/handler lifetime have different ownership rules; accidental per-request handler construction causes production connection and DNS failures. **MUST load `dotnet-client-initialization`** before registration.

⚠ Step 1 (authentication) — credentials are Basic auth and a configuration/binding error can look indistinguishable from authorization failure at the API boundary. **MUST load `dotnet-authentication`** before configuring credentials.

⚠ Steps 2–6 (calling operations) — list methods have required-to-pass nullable parameters and response models add an envelope level; positional calls or reading fields directly from an envelope silently mis-shape the integration. **MUST load `dotnet-calling-endpoints`** before writing calls.

⚠ Steps 3–6 (request/response models) — generated records have required wrappers, nullable contents, wire names distinct from C# identifiers, and `StringEnum` values that must not be treated as C# enums. **MUST load `dotnet-models`** before building requests or mapping results.

⚠ Step 7 (error boundary) — Case A typed errors and Case B raw errors expose different safe error-body access paths; a catch ladder that treats them alike loses validation/not-found semantics. **MUST load `dotnet-error-handling`** before writing the boundary.

⚠ Step 1 (resilience) — retry and timeout configuration has consequences for an interrupted write and a duplicate subscription creation; the application idempotency record must cover the consequence. **MUST load `dotnet-configuration-resilience`** before configuring client resilience.

⚠ Step 7 (tests) — testing an application service through SDK controller internals rather than its HTTP seam makes tests coupled to generated implementation details. **MUST load `dotnet-testing`** before testing the gateway.

⚠ Step 7 (error boundary) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary; **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 7 (error boundary) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

## 4. REQUIRED READING

Load these before implementation starts. This sheet deliberately does not carry their contents.

| Skill | Governing step |
|---|---|
| `dotnet-client-initialization` | Step 1: client/DI and `HttpClient` ownership |
| `dotnet-authentication` | Step 1: Basic credentials and configuration |
| `dotnet-calling-endpoints` | Steps 2–6: signatures, optional arguments, envelopes, cancellation |
| `dotnet-models` | Steps 3–6: request wrappers, nullability, enums, mapping |
| `dotnet-error-handling` | Step 7: Case A/Case B and both `JsonException` paths |
| `dotnet-configuration-resilience` | Step 1: retry, timeout, base URL, and idempotent writes |
| `dotnet-testing` | Step 7: `HttpClient` handler test seam |

## 5. Assumptions & Blockers

| Type | Item |
|---|---|
| Assumption | The API’s authenticated identity path can supply or resolve the shopper’s first name, last name, and email. `CreateCustomer` requires all three; if they cannot be obtained, reject the subscription request before calling Maxio. |
| Assumption | The configured product-family handle identifies exactly one non-archived family. The plan lookup rejects ambiguous/missing family and missing/archived plan rather than accepting a numeric ID. |
| Assumption | The sandbox is US-hosted. The map exposes US/EU hosting, while the mandated `Maxio:` key set has no hosting-environment setting; this plan uses `ServerEnvironment.Us` and the provided optional base-URL override. |
| YOUR CALL — not in the map | Define canonical user and subscription reference formats, durable mapping schema, lock scope, and recovery state. They must make repeated and concurrent requests for the same caller/plan return the one recorded Maxio subscription. The Maxio map documents customer-reference uniqueness, but does **not** document uniqueness enforcement for a subscription’s `reference`; therefore do not rely on `FindSubscription` + `CreateSubscription` alone for double-click idempotency. |
| YOUR CALL — not in the map | Define external HTTP DTOs/status codes and authorization policy, while preserving the mandated JWT caller identity and routes. |
