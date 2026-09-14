# Maxio recurring-subscription integration plan

## 1. Scope & sequence

| Step | Application work | Maxio operations | Source |
|---:|---|---|---|
| 1 | Add `AsadAli.AdvancedBilling.Sdk` at the map-pinned `1.0.2` contract; bind only `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and optional `Maxio:BaseUrl`; validate required non-secret settings at startup; register one named, long-lived `HttpClient` and one long-lived SDK client. Load the secret value into user-secrets outside repository files. | Client construction only | `sdk-map.md` |
| 2 | Add an application-owned subscription-link/idempotency record with a unique `(UserId, ProductHandle)` key, a stable Maxio subscription reference, state (`Pending`, `Succeeded`, `OutcomeUnknown`, `Rejected`), timestamps, and nullable Maxio subscription id. The stable reference is derived once from `(UserId, ProductHandle)` and reused. Never persist seeded product-family/product IDs; Maxio remains the billing system of record. | None | YOUR CALL — not in the map |
| 3 | Implement `GET /api/subscription-plans` under the PublicApi endpoint conventions. Require JWT identity per the brief. Resolve the configured family handle at runtime by listing families and matching `ProductFamily.Handle`; require a non-null runtime `ProductFamily.Id`, then manually page its products. Exclude archived products and map handle/name/description/default price/period. Cache only for a short application-chosen TTL; cache invalidation and endpoint DTOs are application decisions. | `ProductFamilies.ListProductFamilies`; `ProductFamilies.ListProductsForProductFamily` | `operations/ProductFamilies.md` |
| 4 | Implement the `POST /api/subscriptions` orchestration for a caller-supplied product handle. Validate the product by handle and require that `Product.ProductFamily.Handle` equals `Maxio:ProductFamilyHandle` and `ArchivedAt` is null. Resolve the authenticated user from the token/application identity path; use the stable application user id as the Maxio customer reference. | `Products.ReadProductByHandle` | `operations/Products.md` |
| 5 | Ensure the Maxio customer: lookup by unique reference; on 404 create with required name/email/reference; on a create race or unknown outcome, lookup again and accept only the exact reference match. Do not treat malformed/unreadable lookup data as absence. Do not persist or trust a seeded customer id; use the id returned by the current lookup/creation only. | `Customers.ReadCustomerByReference`; `Customers.CreateCustomer` | `operations/Customers.md` |
| 6 | Ensure the subscription idempotently. Serialize contenders through the unique local record; before a create, reconcile by stable subscription reference and, as a restart-safe second check, the customer's current subscriptions for the same product handle. Create with `ProductHandle`, `CustomerReference`, and the stable subscription `Reference`; omit numeric IDs and payment-profile fields. Place the SDK write inside the write-once transport scope required by the resilience skill. On any ambiguous write outcome, call `FindSubscription`; if it is still absent, retain `OutcomeUnknown` and do not automatically issue another create. Concurrent/double-click requests reuse or await the same record and never run a second provider POST. | `Subscriptions.FindSubscription`; `Customers.ListCustomerSubscriptions`; `Subscriptions.CreateSubscription` | `operations/Subscriptions.md`; `operations/Customers.md` |
| 7 | Return the provider-confirmed result from the create/reconciliation path: plan handle/name, actual subscription price, state, and next billing date. Use `Subscription.NextAssessmentAt` as `nextBillingDate`, falling back to `CurrentPeriodEndsAt` only as an explicit application compatibility policy; never fabricate a date. | Response mapping only | `records-3-Of-Su.md`; YOUR CALL — next-date fallback interpretation is not in the map |
| 8 | Implement `GET /api/my-subscriptions`. Lookup the customer by authenticated-user reference; a genuine 404 is an empty list. Resolve live subscriptions with the current returned customer id, filter to the configured product-family handle, and map the same DTO. This endpoint reads Maxio rather than trusting the local link table, so it reflects Maxio state across process/database restarts. | `Customers.ReadCustomerByReference`; `Customers.ListCustomerSubscriptions` | `operations/Customers.md` |
| 9 | Put every SDK call behind one application integration boundary with bounded cancellation, sanitized structured logging, correlation, and consistent typed/raw/provider-unreachable mappings. Never log credentials or raw personal/payment data. Add readiness/config validation without making a live Maxio call at startup. | All above | YOUR CALL — boundary design; SDK errors are grounded below |
| 10 | Add SDK-wire tests via a fake `HttpMessageHandler`, application orchestration tests, PublicApi authorization/contract tests, and an opt-in sandbox E2E test. Verify the family is selected by handle, both seeded plan handles can be listed/subscribed, a double-click produces one provider subscription, and returned state/price/next billing date are provider values. | All above | YOUR CALL — test layout; SDK test seam is governed by required reading |

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

| Controller property | Exact method signature | Request model and fields used | Response envelope and fields read | Error contract | Pagination | Source |
|---|---|---|---|---|---|---|
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.ProductFamilies` | `ListProductFamilies(MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)`; all five nullable arguments have no default and must be passed explicitly | None; pass all five filters as `null` | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductFamilyResponse>`; unwrap `ProductFamily (product_family): MaxioAdvancedBilling.Models.ProductFamily?`; read `Id (id): int?`, `Handle (handle): string?`, `ArchivedAt (archived_at): DateTimeOffset?` | Case B: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; read `StatusCode`, `ReadAsString()` | None | `operations/ProductFamilies.md`; `records-3-Of-Su.md` |
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.ProductFamilies` | `ListProductsForProductFamily(string productFamilyId, MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, MaxioAdvancedBilling.Models.Enums.ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)`; the eight nullable arguments from `dateField` through `include` have no default | None; pass the current resolved numeric family id as invariant text in `productFamilyId`; use named arguments, `includeArchived: false`, all other filters `null` | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>`; unwrap required `Product (product): MaxioAdvancedBilling.Models.Product`; read `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): MaxioAdvancedBilling.Models.Enums.IntervalUnit?`, `ArchivedAt (archived_at): DateTimeOffset?`, `ProductFamily (product_family): ProductFamily?` | Case A: `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`; `TryGetString(out string)` [404], then `TryGetRawError(out RawError)` fallback | Manual `page` + `perPage`; continue until a page contains fewer than `perPage` items | `operations/ProductFamilies.md`; `records-3-Of-Su.md` |
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.Products` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | None | `MaxioAdvancedBilling.Models.ProductResponse`; unwrap required `Product`; read `Handle`, `Name`, `ArchivedAt`, and nested `ProductFamily.Handle` | Case B: `SdkException<RawError>`; read `StatusCode`, `ReadAsString()` | None | `operations/Products.md`; `records-3-Of-Su.md` |
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.Customers` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | None; `reference` is the authenticated application's stable user id | `MaxioAdvancedBilling.Models.CustomerResponse`; unwrap required `Customer (customer): MaxioAdvancedBilling.Models.Customer`; read `Id (id): int?`, `Reference (reference): string?`, `FirstName`, `LastName`, `Email` | Case B: `SdkException<RawError>`; `StatusCode == HttpStatusCode.NotFound` is the only absence branch; otherwise preserve status and sanitize body | None | `operations/Customers.md`; `records-2-Cr-Ne.md` |
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.Customers` | `CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, CancellationToken ct = default)`; nullable body has no default and must be explicit | Outer `CreateCustomerRequest`: required `Customer (customer): MaxioAdvancedBilling.Models.CreateCustomer`. Inner: required `FirstName (first_name): string`, `LastName (last_name): string`, `Email (email): string`; optional but integration-required `Reference (reference): string?`. All address/tax/organization/phone/locale/parent/Salesforce fields are intentionally omitted. Notes state customer `reference` is unique and represents the app's id. | `CustomerResponse`; unwrap required `Customer`; read `Id`, `Reference` | Case A: `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`; `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422], then `TryGetRawError(out RawError)` fallback. Payload: `Errors (errors): Errors?`; extract best-effort and fall back to a generic caller-safe validation message | None | `operations/Customers.md`; `records-1-Ac-Cr.md`; `records-2-Cr-Ne.md` |
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.Customers` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | None; `customerId` comes from the current customer response | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>`; each unwraps nullable `Subscription (subscription): MaxioAdvancedBilling.Models.Subscription?`; read subscription fields listed below | Case B: `SdkException<RawError>`; read `StatusCode`, `ReadAsString()` | None | `operations/Customers.md`; `records-4-Su-We.md`; `records-3-Of-Su.md` |
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.Subscriptions` | `FindSubscription(string? reference, CancellationToken ct = default)`; nullable reference has no default and must be explicit | None; pass the stable application-generated subscription reference | `MaxioAdvancedBilling.Models.SubscriptionResponse`; unwrap nullable `Subscription`; read fields below | Case A: `SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`; `TryGetNoContent(out RawError)` [404], then `TryGetRawError(out RawError)` fallback. Despite its name, `TryGetNoContent` is the generated 404 accessor. | None | `operations/Subscriptions.md`; `records-4-Su-We.md` |
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.Subscriptions` | `CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, CancellationToken ct = default)`; nullable body has no default and must be explicit | Outer `CreateSubscriptionRequest`: required `Subscription (subscription): MaxioAdvancedBilling.Models.CreateSubscription`. Inner has no required members; acceptance therefore relies on Notes: set `ProductHandle (product_handle): string?`, `CustomerReference (customer_reference): string?`, and stable `Reference (reference): string?`. Intentionally omit `ProductId`, both product-price-point selectors (default price point is desired), `CustomerId`, customer attributes, payment-profile/card/bank fields, components, coupons, dates, group, offer, and all other optional fields. Notes say payment information may be required depending on product; the brief's seeded products do not require it. | `MaxioAdvancedBilling.Models.SubscriptionResponse`; unwrap nullable `Subscription`. Read `Id (id): int?`, `State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `Reference (reference): string?`, nested `Product (product): Product?` (`Handle`, `Name`, `ProductFamily.Handle`) | Case A: `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`; `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422], then `TryGetRawError(out RawError)` fallback. Payload: required `Errors (errors): IReadOnlyList<string>`. | None | `operations/Subscriptions.md`; `records-2-Cr-Ne.md`; `records-3-Of-Su.md`; `records-4-Su-We.md` |

### Enums actually read

| Fully-qualified type | Generated static members and wire values | Source |
|---|---|---|
| `MaxioAdvancedBilling.Models.Enums.IntervalUnit` | `Day (day)`, `Month (month)` | `enums.md` |
| `MaxioAdvancedBilling.Models.Enums.SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` | `enums.md` |

Expose enum `.Value`/wire text through application DTOs rather than serializing SDK enum records directly.

### Client construction, auth, server, and configuration facts

| Concern | Exact SDK contract | Source |
|---|---|---|
| Package and namespaces | NuGet `AsadAli.AdvancedBilling.Sdk`, contract/source tag `v1.0.2`; root namespace `MaxioAdvancedBilling`; target `netstandard2.0` | `sdk-map.md` |
| Constructor | `MaxioAdvancedBilling.MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)`; this is the only client constructor | `sdk-map.md` |
| Options | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions`: `Environment: MaxioAdvancedBilling.Servers.ServerEnvironment`, `Retry: MaxioAdvancedBilling.Core.Configuration.RetryOptions`, `Server: MaxioAdvancedBilling.ServerOptions`, `BasicAuth: MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md` |
| Auth | Basic only: `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = value bound from Maxio:ApiKey, Password = "x" }` | `sdk-map.md` |
| Environment | `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` is default and uses Production template `https://{site}.chargify.com`; `ServerEnvironment.Eu` uses `https://{site}.ebilling.maxio.com` | `sdk-map.md` |
| Subdomain | With `Environment = ServerEnvironment.Us`, and no `Maxio:BaseUrl`, set `options.Server.Production.Us.Site` from `Maxio:Subdomain`; default template variable is `subdomain` | `sdk-map.md` |
| Verbatim base URL override | With `Environment = ServerEnvironment.Us`, when `Maxio:BaseUrl` is non-empty set `options.Server.Production.Us.BaseUrl` to that exact string and do not derive/append a host/path. All in-scope operations use the Production server group. | `sdk-map.md`; operation pages above |
| Retry members | `MaxioAdvancedBilling.Core.Configuration.RetryOptions` has required members `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry`; start from `RetryOptions.Default()` before overriding. | `sdk-map.md` |
| Error namespaces | `MaxioAdvancedBilling.Core.Exceptions.SdkException<TError>`; `MaxioAdvancedBilling.Core.ErrorResponse.ApiError` and `RawError`; per-operation typed errors under `MaxioAdvancedBilling.Errors` | `sdk-map.md` |

### Application/provider mappings

| Item | Decision | Source |
|---|---|---|
| Caller identity | Resolve the stable user id, first name, last name, and email through the application's authenticated identity/profile path; never accept a customer reference/id from the POST body | YOUR CALL — not in the map |
| Subscribe request | Body contains `productHandle`; uniqueness scope is one subscription per `(authenticated user id, product handle)` for this capability | YOUR CALL — not in the map |
| Customer idempotency | Provider-enforced unique customer `reference`; lookup, create, then lookup on race/ambiguity | `operations/Customers.md` |
| Subscription idempotency | Local unique reservation + stable subscription `Reference` + `FindSubscription`/customer-subscription reconciliation. The map provides a reference field and lookup operation but does **not** state that duplicate subscription references are rejected. Never rely on provider uniqueness for subscription references. | `operations/Subscriptions.md`; YOUR CALL — local concurrency design |
| Numeric identifiers | Runtime customer/family ids may be used only from a current SDK response to call an id-based endpoint; no seeded catalog id is configuration or persistence | YOUR CALL — not in the map |
| Price | Plans use `Product.PriceInCents`; enrolled subscriptions use `Subscription.ProductPriceInCents`, preserving integer cents | `records-3-Of-Su.md` |
| Next billing date | Primary field `Subscription.NextAssessmentAt`; explicit fallback to `CurrentPeriodEndsAt`; null remains null | `records-3-Of-Su.md`; YOUR CALL — semantic selection/fallback |
| HTTP results | 200 for plan/list reads; successful/reconciled subscribe returns the endpoint convention's create/success result; genuine caller/provider 4xx remains a 4xx; transport/malformed-success failures become sanitized 5xx; an unresolved write outcome remains a distinct non-success/pending result and is never reported as a definite rejection | YOUR CALL — not in the map |

## 3. Trap notes

⚠ Step 1 (client registration) — client/`HttpClient` lifetimes and the generated DI extension's unnamed-client behavior can change DNS rotation and unrelated consumers. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ Step 1 (authentication) — credential timing and the Basic-auth credentials-property shape determine whether calls are authenticated without leaking the key. **MUST load `dotnet-authentication`** before wiring credentials.

⚠ Steps 3–8 (endpoint calls) — nullable parameters without C# defaults, named-argument binding, response envelopes, and `ct:` make superficially plausible calls compile incorrectly or read the wrong layer. **MUST load `dotnet-calling-endpoints`** before the first call.

⚠ Steps 3–8 (model mapping) — generated string-enum readback, nullable response members, immutable request records, and wire names can silently change DTO/payload behavior. **MUST load `dotnet-models`** before constructing or mapping models.

⚠ Steps 1, 3, 6, and 9 (resilience) — retry/timeout options do not by themselves bound the caller-visible total, list operations require their documented paging behavior, and a transport fault can resend a subscription-creation POST. **MUST load `dotnet-configuration-resilience`** before client registration and before the write-once scope.

⚠ Steps 5–9 (error boundary) — Case A accessors and Case B `RawError` have different catch shapes, provider statuses must survive translation, and raw details must not leak. **MUST load `dotnet-error-handling`** before writing the boundary.

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
  maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
  and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 10 (tests) — the SDK's actual fake seam and distinct status-vs-transport retry cases determine whether a test really proves one upstream write. **MUST load `dotnet-testing`** before writing tests.

## 4. REQUIRED READING

Load every item below **before implementation starts**. This contract sheet deliberately does not carry their contents.

| Skill | Step governed |
|---|---|
| `dotnet-client-initialization` | Step 1 client/DI/HTTP lifetime |
| `dotnet-authentication` | Step 1 Basic authentication |
| `dotnet-calling-endpoints` | Steps 3–8 operation invocation and envelopes |
| `dotnet-models` | Steps 3–8 requests, enums, nullable models, mapping |
| `dotnet-error-handling` | Steps 5–9 exception translation and JSON-drift boundary |
| `dotnet-configuration-resilience` | Steps 1, 3, 6, 9 server selection, paging, retry/write-once, budgets, logging |
| `dotnet-testing` | Step 10 SDK-wire and retry/idempotency tests |

## 5. Assumptions & Blockers

### Assumptions

- The application's authenticated identity/profile path can provide a stable user id plus non-empty first name, last name, and email; `CreateCustomer` requires all three profile strings.
- This capability defines one subscription per authenticated user and product handle. Cancellation/resubscription is outside the requested endpoints; a future resubscribe feature must introduce a new subscription intent/reference rather than weakening this idempotency key.
- The configured product family is on the SDK's US Production server group unless `Maxio:BaseUrl` supplies the complete alternate base address verbatim. No `Maxio:Environment` binding key was requested.
- The seeded sandbox products require no payment method, as stated in the brief. A different catalog whose product requires payment information will produce the documented create-subscription rejection and is not silently bypassed.
- Live E2E execution uses an authenticated PublicApi JWT and a disposable/test user appropriate for creating persistent sandbox billing records.

### Blockers

- None.
