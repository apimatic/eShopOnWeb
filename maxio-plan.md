# Maxio subscription integration plan

## 1. Scope & sequence

| Step | Work | Operations / outcome | Source |
|---|---|---|---|
| 1 | Add the published `AsadAli.AdvancedBilling.Sdk` package to the PublicApi host; bind and validate the four mandated `Maxio:` settings. Register one long-lived `MaxioAdvancedBillingClient` over an app-owned named `HttpClient`. | `MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)`; Basic auth; US Production server with the configured site and optional verbatim base-URL override. | `sdk-map.md` |
| 2 | Add a Maxio boundary service and provider-safe error translation. Route all SDK calls through its linked cancellation/timeout boundary and do not expose SDK exceptions or provider body text. | All calls below are throw-only. | `sdk-map.md`; `operations/*.md` |
| 3 | Build `GET /api/subscription-plans`: load product families, select exactly the configured family handle, then list its non-archived products. Return only each plan's provider handle, display name, recurring price in cents, interval and interval unit. Do not use numeric seed IDs. | `ProductFamilies.ListProductFamilies` then `ProductFamilies.ListProductsForProductFamily`. | `operations/ProductFamilies.md` |
| 4 | Build the subscription workflow behind `POST /api/subscriptions`: derive the caller's stable user identity and customer profile from the authenticated request; look up the customer by deterministic reference, create it only when absent, and persist the external IDs. Validate the requested plan handle against Step 3's configured-family results before enrolling. | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Subscriptions.CreateSubscription`. | `operations/Customers.md`; `operations/Subscriptions.md` |
| 5 | Make enrollment recoverable and single-write: use a durable, unique application operation for `(user, plan)` and a deterministic subscription reference; serialize the provider write, persist its outcome, and reconcile an uncertain/duplicate request through `Subscriptions.FindSubscription` before another write. A transport-retry guard must prevent an SDK retry from issuing a second provider `POST`. | `Subscriptions.FindSubscription`, `Subscriptions.CreateSubscription`. The provider's enforcement of `reference` uniqueness is **UNVERIFIED**. | `operations/Subscriptions.md`; `UNVERIFIED` |
| 6 | Return a subscription DTO from enrollment and build `GET /api/my-subscriptions` by resolving the caller's Maxio customer and listing its subscriptions. Unwrap every response envelope and map provider plan, price, state and next assessment date into the API DTO. | `Customers.ListCustomerSubscriptions`; response `SubscriptionResponse.Subscription`. | `operations/Customers.md`; `records-3-Of-Su.md`; `records-4-Su-We.md` |
| 7 | Test request serialization and failure translation at the SDK `HttpClient` seam, then live-verify the three API endpoints against sandbox with an authenticated caller. Test the same submit twice and an uncertain write recovery path. | All above; no mock-only claim of idempotency. | `YOUR CALL — not in the map` |

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

| Controller property · operation | Verbatim method signature | Request model / integration fields | Response envelope / fields read | Error case | Pagination | Source |
|---|---|---|---|---|---|---|
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.ProductFamilies` · `ListProductFamilies` | `ListProductFamilies(MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` | No body. Pass all five nullable filters explicitly as `null`. | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductFamilyResponse>`; unwrap `.ProductFamily`, read `.Handle`, `.Id`. | B — `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; read `.Error.StatusCode`, `.ReadAsString()`. | none | `operations/ProductFamilies.md`; `records-3-Of-Su.md` |
| `…ProductFamilies` · `ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, MaxioAdvancedBilling.Models.ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, double? page = 1, double? perPage = 20, CancellationToken ct = default)` | No body. Pass the selected family `.Id.ToString()` as `productFamilyId`; pass all nullable non-defaulted filters explicitly; set `includeArchived: false`. Configured family handle is an app setting, not a provider numeric ID. | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>`; unwrap `.Product`, read `.Handle`, `.Name`, `.PriceInCents` (`long?`), `.Interval` (`double?`), `.IntervalUnit`, `.ArchivedAt`, `.ProductFamily`. | A — `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`; `TryGetString(out string)` [404], then `TryGetRawError(out RawError)`. | manual `page` + `perPage` | `operations/ProductFamilies.md`; `records-2-Cr-Ne.md`; `records-3-Of-Su.md`; compiler-verified package surface |
| `…Customers` · `ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | No body. Pass deterministic application customer reference. The operation note says it returns a single customer by unique reference. | `MaxioAdvancedBilling.Models.CustomerResponse`; unwrap `.Customer`, read `.Id` (`double?` in the installed package), `.Reference`, `.Email`. | B — `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; 404 is the only absence signal that may trigger create; an unreadable result is never absence. | none | `operations/Customers.md`; `records-2-Cr-Ne.md`; compiler-verified package surface |
| `…Customers` · `CreateCustomer` | `CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, CancellationToken ct = default)` | `CreateCustomerRequest.Customer (customer): MaxioAdvancedBilling.Models.CreateCustomer !req`; `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?`. Populate the first three from the authenticated app user and reference deterministically. The operation note ties acceptance to unique `reference`; do not omit it. | `CustomerResponse.Customer: Customer !req`; read `.Id`, `.Reference`, `.Email`. | A — `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`; `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422], then `TryGetRawError(out RawError)`. On a collision/race, re-read by reference rather than treating it as a new customer. | none | `operations/Customers.md`; `records-1-Ac-Cr.md`; `records-2-Cr-Ne.md` |
| `…Subscriptions` · `FindSubscription` | `FindSubscription(string? reference, CancellationToken ct = default)` | No body. `reference` is nullable but has no C# default: pass it explicitly. Use only to reconcile deterministic application subscription reference; it is not a proof that Maxio enforces reference uniqueness. | `MaxioAdvancedBilling.Models.SubscriptionResponse`; unwrap `.Subscription`; read `.Id` (`double?` in the installed package), `.Reference`, `.State`, `.Product`, `.ProductPriceInCents` (`double?`), `.NextAssessmentAt`. | A — `SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`; `TryGetNoContent(out RawError)` [404], then `TryGetRawError(out RawError)`. | none | `operations/Subscriptions.md`; `records-3-Of-Su.md`; `records-4-Su-We.md`; compiler-verified package surface; `UNVERIFIED` |
| `…Subscriptions` · `CreateSubscription` | `CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, CancellationToken ct = default)` | `CreateSubscriptionRequest.Subscription (subscription): MaxioAdvancedBilling.Models.CreateSubscription !req`; set `ProductHandle (product_handle): string?`, `CustomerId (customer_id): int?`, `Reference (reference): string?`, and `PaymentCollectionMethod (payment_collection_method): MaxioAdvancedBilling.Models.Enums.CollectionMethod?`. The operation Notes require one of `product_id`/`product_handle` and an existing customer through `customer_id`/`customer_reference`; this workflow uses `ProductHandle` and `CustomerId`. To request manual invoicing on a legacy Statements Architecture site, set `PaymentCollectionMethod = CollectionMethod.Invoice`; leave `product_id`, customer attributes and payment profile fields out. | `SubscriptionResponse.Subscription: Subscription?`; read `.Id` (`double?` in the installed package), `.Reference`, `.State`, `.Product`, `.ProductPriceInCents` (`double?`), `.CurrentBillingAmountInCents` (`long?`), `.NextAssessmentAt`, `.CurrentPeriodEndsAt`, `.Currency`. `Product?.Handle` and `.Product?.Name` identify the plan. | A — `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`; `TryGetErrorListResponse1(out ErrorListResponse1)` [422], then `TryGetRawError(out RawError)`. | none | `operations/Subscriptions.md`; `records-2-Cr-Ne.md`; `records-3-Of-Su.md`; `records-4-Su-We.md`; `models/enums.md`; compiler-verified package surface |
| `…Customers` · `ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | No body. Supply the saved/resolved Maxio customer ID. | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>`; unwrap each `.Subscription`; map the same fields as create. | B — `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; read `.Error.StatusCode`, `.ReadAsString()`. | none | `operations/Customers.md`; `records-3-Of-Su.md`; `records-4-Su-We.md` |

### Enum values needed

| Fully-qualified enum | Values used / mapped | Source |
|---|---|---|
| `MaxioAdvancedBilling.Models.Enums.IntervalUnit` | `Day (day)`, `Month (month)` — map the response's raw `.Value`, guarding unknown values. | `models/enums.md` |
| `MaxioAdvancedBilling.Models.Enums.SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)`. Return the provider value as state; do not infer entitlement policy here. | `models/enums.md` |
| `MaxioAdvancedBilling.Models.Enums.CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)`. For legacy Statements Architecture, valid options are `Invoice` and `Automatic`; for Relationship Invoicing Architecture, valid options are `Remittance`, `Automatic`, and `Prepaid`. | `models/enums.md` |

### Client construction, auth, and server facts

| Fact | Contract | Source |
|---|---|---|
| Package / root types | NuGet `AsadAli.AdvancedBilling.Sdk`; client `MaxioAdvancedBilling.MaxioAdvancedBillingClient`; options `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions`; sole constructor `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`. | `sdk-map.md` |
| Authentication | `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials`; assign `MaxioAdvancedBillingClientOptions.BasicAuth` with configuration `Maxio:ApiKey` as `Username` and literal password `"x"`. | `sdk-map.md` |
| Configuration binding | Bind precisely `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and optional `Maxio:BaseUrl`. Validate nonblank required values at startup; never hard-code catalog identifiers or secret values. | YOUR CALL — not in the map |
| Server | `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` is the documented default; configure `options.Server.Production.Us.Site` from `Maxio:Subdomain`. When `Maxio:BaseUrl` is set, assign it verbatim to `options.Server.Production.Us.BaseUrl` before client construction. | `sdk-map.md` |
| Namespaces | Controllers: `MaxioAdvancedBilling.Api`; records: `MaxioAdvancedBilling.Models`; enums: `MaxioAdvancedBilling.Models.Enums`; typed errors: `MaxioAdvancedBilling.Errors`; `SdkException<T>`: `MaxioAdvancedBilling.Core.Exceptions`; `RawError`: `MaxioAdvancedBilling.Core.ErrorResponse`; retry: `MaxioAdvancedBilling.Core.Configuration`; servers: `MaxioAdvancedBilling.Servers`. | `sdk-map.md` |

## 3. Trap notes

⚠ Step 1 (client registration) — ownership, lifetime, handler rotation, and isolating the provider pipeline from the host's unnamed `HttpClient` determine whether the integration is safe under load. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ Step 1 (authentication) — credentials must be configured before construction and must never be represented by source-controlled settings. **MUST load `dotnet-authentication`** before wiring auth.

⚠ Steps 3–6 (calls) — nullable leading parameters without C# defaults, response envelopes, and exact `ct` naming can turn a compiling call into a mis-bound request. **MUST load `dotnet-calling-endpoints`** before writing SDK calls.

⚠ Steps 3–6 (models) — immutable models, nullable fields versus required fields, string-enums, and future/unknown response values affect request construction and DTO mapping. **MUST load `dotnet-models`** before constructing payloads or mapping SDK models.

⚠ Steps 2–6 (error boundary) — Case A and Case B exceptions have different readable error paths, and mapping a provider rejection to the wrong API result makes retry behavior unsafe. **MUST load `dotnet-error-handling`** before writing the boundary.

⚠ Step 5 (enrollment write) — retry/timeout semantics and transport failures can cause a write to be re-sent; the guard and recovery design must settle whether the provider accepted the deterministic operation. **MUST load `dotnet-configuration-resilience`** before configuring retries, timeouts, base URL, or the write guard.

⚠ Step 7 (tests) — the SDK client `HttpClient` seam, not SDK internals, must prove request shape and failure paths including duplicate-write prevention. **MUST load `dotnet-testing`** before writing integration tests.

⚠ Steps 2–6 (2xx deserialization) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary; **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Steps 2–6 (non-2xx deserialization) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

## 4. REQUIRED READING

Load these before implementation starts; this sheet deliberately does not carry their contents.

| Skill | Governing step |
|---|---|
| `dotnet-client-initialization` | Step 1 — named `HttpClient`, client construction, DI lifetime. |
| `dotnet-authentication` | Step 1 — Basic credentials from configuration. |
| `dotnet-calling-endpoints` | Steps 3–6 — operation calls, named parameters, envelopes, cancellation. |
| `dotnet-models` | Steps 3–6 — request models, enum/date mapping. |
| `dotnet-error-handling` | Steps 2–6 — typed/raw error and `JsonException` boundary. |
| `dotnet-configuration-resilience` | Steps 1 and 5 — server override, retry/timeout/write safety. |
| `dotnet-testing` | Step 7 — `HttpClient` seam and failure-path tests. |

## 5. Assumptions & Blockers

| Type | Item |
|---|---|
| Assumption | The authenticated app identity can supply a stable user identifier plus the first name, last name, and email that `CreateCustomer` requires. If any required profile field is absent, reject enrollment before provider contact; do not manufacture a customer profile. |
| Assumption | The application owns a durable Maxio linkage/enrollment record and a uniqueness/concurrency mechanism for `(user, plan)`; exact persistence schema and JWT-claim selection are **YOUR CALL — not in the map**. |
| Assumption | The configured product-family handle resolves to exactly one active family, and the requested product handle must be an active member of it; empty/multiple/missing results are configuration or request failures, not enrollment attempts. |
| UNVERIFIED | `CreateSubscription.Subscription.Reference` can be supplied and `FindSubscription` searches it, but the map does not document provider-side uniqueness. Do not rely on Maxio alone to make duplicate references harmless; use a deterministic reference, an application single-write guard, and post-failure reconciliation. |
| Blocker | None in the SDK map. |
