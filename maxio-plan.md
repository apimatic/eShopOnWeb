# Maxio subscription integration plan

## 1. Scope & sequence

| Step | Work and SDK operations | Source |
|---|---|---|
| 1 | Add package `AsadAli.AdvancedBilling.Sdk`; construct one `MaxioAdvancedBilling.MaxioAdvancedBillingClient` over an application-owned `HttpClient`, bind only `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and optional `Maxio:BaseUrl`. Configure Basic credentials; select the Production US server and set `Production.Us.Site` from `Maxio:Subdomain`. If `Maxio:BaseUrl` is nonblank, set `Production.Us.BaseUrl` to that value verbatim instead. | `sdk-map.md` |
| 2 | Implement the authenticated plan read: list product families, select the one whose returned `Handle` equals `Maxio:ProductFamilyHandle`, then list that family’s products; return only that family’s non-archived plans and their provider-supplied handle/name/description/price/interval. Operations: `ProductFamilies.ListProductFamilies`, `ProductFamilies.ListProductsForProductFamily`. | `operations/ProductFamilies.md`, `records-3-Of-Su.md` |
| 3 | Implement customer ensure using a deterministic application customer reference derived from the authenticated application user. Lookup with `Customers.ReadCustomerByReference`; on its raw 404, create via `Customers.CreateCustomer` with the same reference. The create operation’s Notes guarantee only one customer for a supplied reference; if concurrent creation returns its typed 422, re-read by reference and continue only when that lookup succeeds. | `operations/Customers.md`, `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md` |
| 4 | Implement an enrollment command: validate the submitted plan handle against Step 2’s configured-family product list, then use the application’s durable idempotency/concurrency boundary before provider writes. Lookup the command’s deterministic subscription reference with `Subscriptions.FindSubscription`; when absent, call `Subscriptions.CreateSubscription` using the validated product handle, ensured customer ID, and that reference. Reconcile an uncertain/duplicate write by finding the same reference before reporting failure. Return the provider’s subscription response. | `operations/Subscriptions.md`, `records-2-Cr-Ne.md`, `records-3-Of-Su.md` |
| 5 | Implement the authenticated “my subscriptions” read by resolving the user’s Maxio customer through the same reference and calling `Customers.ListCustomerSubscriptions`. Map provider responses to plan handle/name, price in cents, state, current-period end (the billing-period renewal date), next-assessment time (the payment-capture/retry time), and provider subscription ID. Maxio remains the source for this read, not a locally cached display projection. | `operations/Customers.md`, `records-3-Of-Su.md`, `Models/Subscription.cs` |
| 6 | Expose the application’s JWT-protected `/api/subscription-plans`, `/api/subscriptions`, and `/api/my-subscriptions` endpoints using the host’s established endpoint and identity conventions. Convert validated provider rejections to the API’s client-error shape, distinguish upstream availability failures, and cover request serialization, envelope handling, idempotency/recovery, and error paths with the project’s test conventions. | YOUR CALL — not in the map |

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

| Controller property · method signature | Request model + fields used/omitted | Response envelope + fields read | Error case | Pagination | Source |
|---|---|---|---|---|---|
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.ProductFamilies.ListProductFamilies(MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` | No body. Pass all five nullable filters explicitly as `null`. | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductFamilyResponse>`; each `ProductFamily (product_family): ProductFamily?`, then `Id (id): int?`, `Handle (handle): string?`, `ArchivedAt (archived_at): DateTimeOffset?`. Match configured handle and require usable ID. | B: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; read `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, or `ReadAsBytes()`. | none | `operations/ProductFamilies.md`, `records-3-Of-Su.md`, `sdk-map.md` |
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.ProductFamilies.ListProductsForProductFamily(string productFamilyId, MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, MaxioAdvancedBilling.Models.Enums.ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | No body. `productFamilyId` is the selected family ID rendered as its string route value. Pass the eight nullable non-default filters explicitly; omit archived products. | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>`; `Product (product): Product !req`, read `Id`, `Name`, `Handle`, `Description`, `PriceInCents`, `Interval`, `IntervalUnit`, `ArchivedAt`, `RequireCreditCard`, `Taxable`, `ProductFamily`. | A: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`; `TryGetString(out string)` [404], then inherited `TryGetRawError(out RawError)`. | manual `page` + `perPage` | `operations/ProductFamilies.md`, `records-3-Of-Su.md`, `sdk-map.md` |
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.Customers.ReadCustomerByReference(string reference, CancellationToken ct = default)` | No body; stable application customer reference is the query value. | `MaxioAdvancedBilling.Models.CustomerResponse` → `Customer (customer): Customer !req` → `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?`. | B: `SdkException<RawError>`; raw status/body accessors above. Treat only raw 404 as not-found; do not turn other provider failures into customer creation. | none | `operations/Customers.md`, `records-2-Cr-Ne.md`, `sdk-map.md` |
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.Customers.CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, CancellationToken ct = default)` | `CreateCustomerRequest.Customer (customer): CreateCustomer !req`; `CreateCustomer.FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?`. Omit address, tax, organization, locale, and all other optional properties unless application-owned profile data supplies them. Its Notes tie accepted creation to unique `reference`; the `Country` field is deliberately omitted, so no country format is sent. | `CustomerResponse` → required `Customer` → `Id`, `Reference`, `Email`. | A: `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`; `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422], then `TryGetRawError(out RawError)`. | none | `operations/Customers.md`, `records-1-Ac-Cr.md`, `records-2-Cr-Ne.md`, `sdk-map.md` |
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.Subscriptions.FindSubscription(string? reference, CancellationToken ct = default)` | No body; nullable `reference` has no default and must be passed explicitly. | `MaxioAdvancedBilling.Models.SubscriptionResponse` → `Subscription (subscription): Subscription?`; ensure non-null before use. | A: `SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`; `TryGetNoContent(out RawError)` [404], then `TryGetRawError(out RawError)`. | none | `operations/Subscriptions.md`, `records-3-Of-Su.md`, `sdk-map.md` |
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.Subscriptions.CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, CancellationToken ct = default)` | `CreateSubscriptionRequest.Subscription (subscription): CreateSubscription !req`; set `ProductHandle (product_handle): string?`, `CustomerId (customer_id): int?`, and deterministic `Reference (reference): string?`. Notes allow product via `product_handle` (instead of `product_id`) and existing customer via `customer_id` (instead of `customer_reference`). Omit price-point override, payment profile, customer attributes, card/bank details, and every other optional field; payment information may still be required by the selected product’s site configuration. | `SubscriptionResponse` → `Subscription?`; read `Id`, `Reference`, `State`, `ProductPriceInCents`, `CurrentBillingAmountInCents`, `CurrentPeriodEndsAt`, `NextAssessmentAt`, and nested `Product` (`Handle`, `Name`, `PriceInCents`). `CurrentPeriodEndsAt` is the period end; `NextAssessmentAt` is payment capture/retry time and can diverge after failed renewal. | A: `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`; `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422], then `TryGetRawError(out RawError)`. | none | `operations/Subscriptions.md`, `records-2-Cr-Ne.md`, `records-3-Of-Su.md`, `Models/Subscription.cs`, `sdk-map.md` |
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.Customers.ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | No body. | `IReadOnlyList<SubscriptionResponse>`; unwrap each nullable `Subscription` and map the same fields listed for `CreateSubscription`. | B: `SdkException<RawError>`; raw status/body accessors above. | none | `operations/Customers.md`, `records-3-Of-Su.md`, `sdk-map.md` |

### Enum values needed

| Fully-qualified enum | Values | Source |
|---|---|---|
| `MaxioAdvancedBilling.Models.Enums.SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` | `models/enums.md` |
| `MaxioAdvancedBilling.Models.Enums.BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)`; no value is needed when filters are null. | `models/enums.md` |
| `MaxioAdvancedBilling.Models.Enums.ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)`; leave unset for this flow. | `models/enums.md` |

### Client, auth, and server facts

| Fact | Contract | Source |
|---|---|---|
| Package/client | NuGet package `AsadAli.AdvancedBilling.Sdk`; client `MaxioAdvancedBilling.MaxioAdvancedBillingClient`; constructor `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`. | `sdk-map.md` |
| Options | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions`: `Environment: MaxioAdvancedBilling.Servers.ServerEnvironment`, `Retry: MaxioAdvancedBilling.Core.Configuration.RetryOptions`, `Server: MaxioAdvancedBilling.Servers.ServerOptions`, `BasicAuth: MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?`. | `sdk-map.md` |
| Authentication | Set `BasicAuth.Username` from `Maxio:ApiKey`; set `BasicAuth.Password` to the SDK-mandated literal `"x"`. | `sdk-map.md` |
| Server selection | `ServerEnvironment.Us` is the documented default. Production US derives `https://{site}.chargify.com`; bind `site` from `Maxio:Subdomain` at `options.Server.Production.Us.Site`. When the optional `Maxio:BaseUrl` is set, assign it verbatim to `options.Server.Production.Us.BaseUrl`. | `sdk-map.md` |
| Settings values and secret ingress | The application reads only `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and `Maxio:BaseUrl`. Populate the first three from the named environment variables into .NET user-secrets outside the repository; never copy values into a repo file. | YOUR CALL — not in the map |

## 3. Trap notes

⚠ Step 1 (client registration) — ownership/lifetime of the supplied `HttpClient`, and whether a DI helper uses a shared unnamed client, can couple provider reliability to unrelated callers. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ Step 1 (authentication) — credentials must be applied at client construction from configuration without exposing the API key. **MUST load `dotnet-authentication`** before wiring credentials.

⚠ Steps 2–5 (calls) — list signatures have nullable-but-must-pass parameters, and responses are wrappers that require explicit unwrapping. **MUST load `dotnet-calling-endpoints`** before writing calls.

⚠ Steps 2–5 (models) — immutable request records, nullable provider responses, and string-enum wire values can produce a compiling but malformed payload or mapper. **MUST load `dotnet-models`** before building requests or response mappers.

⚠ Steps 3–5 (writes/resilience) — transient transport behavior can leave the outcome of a provider POST uncertain, so idempotency/recovery has to withstand it. **MUST load `dotnet-configuration-resilience`** before tuning the client or relying on retries.

⚠ Step 6 (boundary) — raw and typed SDK exceptions have different status/body access paths, so a single catch shape loses actionable provider failures. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 6 (boundary) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary; **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 6 (boundary) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 6 (tests) — mocking the SDK controller rather than the provided HTTP transport hides request-envelope, serialization, and retry behavior. **MUST load `dotnet-testing`** before writing integration tests.

## 4. REQUIRED READING

Load these before implementation starts; this sheet deliberately does not carry their contents.

| Skill | Governing step |
|---|---|
| `dotnet-client-initialization` | Step 1 client/DI registration |
| `dotnet-authentication` | Step 1 credentials |
| `dotnet-calling-endpoints` | Steps 2–5 operation invocation/envelopes |
| `dotnet-models` | Steps 2–5 request construction and model mapping |
| `dotnet-configuration-resilience` | Steps 1 and 3–5 timeouts/retries/base URL |
| `dotnet-error-handling` | Step 6 exception boundary |
| `dotnet-testing` | Step 6 integration testing |

## 5. Assumptions & Blockers

| Type | Item |
|---|---|
| Assumption | The JWT identity path supplies application-owned, nonempty email and name values required by `CreateCustomer`; the application decides its fallback/validation behavior when those values are unavailable. |
| Assumption | The caller submits a provider plan handle; the application validates it against the configured product family instead of embedding any seeded plan or numeric ID. |
| Assumption | The application supplies a durable, atomic idempotency/concurrency boundary for an enrollment key. `CreateCustomer` documents uniqueness of customer reference; `FindSubscription` documents lookup by subscription reference, but the map does not document uniqueness enforcement for a subscription reference. Therefore the application must not claim provider-side reference uniqueness as its sole double-submit guarantee. |
| Assumption | The configured sandbox is US-hosted when no `Maxio:BaseUrl` override is supplied. The SDK map exposes US and EU server values, but the mandated configuration keys include no environment selector; the map’s documented default is US. |
| Blockers | None. |

## 6. Source index

All SDK facts above are from the bundled map pages named in their source cells; the precise semantics of `Subscription.CurrentPeriodEndsAt` and `Subscription.NextAssessmentAt` were checked in the map-named `Models/Subscription.cs` source file because the map’s field list does not carry their documentation comments.
