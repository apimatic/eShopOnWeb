# Maxio subscription integration plan

## 1. Scope & sequence

| Step | Implementation outcome | Maxio operations |
|---|---|---|
| 1 | Add package `AsadAli.AdvancedBilling.Sdk`; bind and validate the four `Maxio:` keys only (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl`). Put values in the PublicApi user-secrets from the named environment variables; never add their values to repository content. Register one named, long-lived HTTP pipeline and one `MaxioAdvancedBilling.MaxioAdvancedBillingClient`; select `ServerEnvironment.Us`, set the Production US `Site` from `Subdomain`, and when non-empty set the Production US `BaseUrl` verbatim. | — |
| 2 | Add a durable application-owned Maxio linkage/operation ledger. Keep a unique eShop user id → Maxio customer id/reference mapping and a unique user id + requested product handle enrollment record containing a deterministic Maxio subscription reference, status, and resolved Maxio subscription id. Perform provider writes only while the record is the admitted/owner attempt; persist the intent before the call and reconcile by reference after an ambiguous result. | `ReadCustomerByReference`, `CreateCustomer`, `FindSubscription`, `CreateSubscription` |
| 3 | Implement the Maxio gateway/service boundary: derive the customer reference from the authenticated application user, provision/repair the customer idempotently, resolve the configured product family by `Handle` at runtime, enumerate its non-archived products, and select a requested product by its stable product `Handle`. Do not retain or use seeded numeric ids. | `ListProductFamilies`, `ListProductsForProductFamily`, `ReadCustomerByReference`, `CreateCustomer`, `FindSubscription`, `CreateSubscription`, `ListCustomerSubscriptions` |
| 4 | Add JWT-authorized PublicApi endpoints: `GET /api/subscription-plans`, `POST /api/subscriptions`, and `GET /api/my-subscriptions`. Derive caller identity exclusively from the JWT/current user path; never accept a user/customer id from the request. Translate the gateway result into application DTOs with product handle/name, cents/currency/interval, state, and next billing date. | steps 2–3 operations |
| 5 | Make the endpoint result idempotent: the POST first joins/replays the durable enrollment record, then calls `FindSubscription(reference)` before any create. A completed provider create is saved with the returned subscription id. After an unknown POST outcome, retries reconcile with `FindSubscription` and must not issue another create merely because the prior HTTP result was lost. A 422 duplicate customer race re-reads the customer by reference. | `FindSubscription`, `CreateSubscription`, `ReadCustomerByReference`, `CreateCustomer` |
| 6 | Test request serialization, response-envelope mapping, error translation, authorization/identity isolation, pagination loop, and the idempotency ledger with the SDK HTTP-handler seam. Separately run the PublicApi with user-secrets, `UseOnlyInMemoryDatabase=true`, and the rollout environment specified by the task; obtain a PublicApi bearer token and exercise all three endpoints. | all above |

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

| Operation (controller property) | Exact generated signature | Request model / fields used (C# wire-name): type, required? | Response envelope and fields read | Error boundary | Pagination | Source |
|---|---|---|---|---|---|---|
| `MaxioAdvancedBilling.Api.ProductFamilies.ListProductFamilies` | `ListProductFamilies(MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` | no body; all five leading nullable args must be supplied (use named `null`) | `MaxioAdvancedBilling.Models.ProductFamilyResponse.ProductFamily` → `Handle (handle)`, `Id (id)`; compare `Handle` to the configured `ProductFamilyHandle` and use returned runtime `Id` | Case B: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; `StatusCode`, `ReadAsString()` | none | `operations/ProductFamilies.md`; `records-3-Of-Su.md` |
| `MaxioAdvancedBilling.Api.ProductFamilies.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, MaxioAdvancedBilling.Models.Enums.ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | no body; pass all leading nullable args explicitly by name; set `includeArchived: false`; convert the resolved runtime family id to `productFamilyId` | each `MaxioAdvancedBilling.Models.ProductResponse.Product` → `Handle (handle)`, `Name (name)`, `Description (description)`, `PriceInCents (price_in_cents)`, `Interval (interval)`, `IntervalUnit (interval_unit)`, `Currency` is not supplied by this product record; return price in cents and interval, with any currency only from a subscription response | Case A: `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`; `TryGetString(out string)` [404], then `TryGetRawError(out RawError)` | manual `page` + `perPage`; continue until a returned page has fewer than requested items | `operations/ProductFamilies.md`; `records-3-Of-Su.md` |
| `MaxioAdvancedBilling.Api.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | no body; `reference` is the application-derived stable customer reference | `MaxioAdvancedBilling.Models.CustomerResponse.Customer` → `Id (id)`, `Reference (reference)` | Case B: `SdkException<RawError>`; inspect `StatusCode` to distinguish 404 miss from other provider rejection; use `ReadAsString()` for safe diagnostics | none | `operations/Customers.md`; `records-2-Cr-Ne.md` |
| `MaxioAdvancedBilling.Api.Customers.CreateCustomer` | `CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, CancellationToken ct = default)` | `CreateCustomerRequest.Customer (customer)`: `MaxioAdvancedBilling.Models.CreateCustomer` !required; set `FirstName (first_name): string` !required, `LastName (last_name): string` !required, `Email (email): string` !required, `Reference (reference): string?`. Omit all optional address/profile fields. Provider notes say reference may only identify one customer and must be unique if supplied. | `CustomerResponse.Customer` → `Id (id)`, `Reference (reference)` | Case A: `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`; `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422], then `TryGetRawError(out RawError)`. On the unique-reference 422 race, re-read by reference; do not create with a different reference. | none | `operations/Customers.md`; `records-1-Ac-Cr.md`; `records-2-Cr-Ne.md` |
| `MaxioAdvancedBilling.Api.Subscriptions.FindSubscription` | `FindSubscription(string? reference, CancellationToken ct = default)` | no body; nullable-without-default `reference` must be passed explicitly; use the persisted deterministic enrollment reference | `MaxioAdvancedBilling.Models.SubscriptionResponse.Subscription` → `Id (id)`, `Reference (reference)`, `State (state)`, `ProductPriceInCents (product_price_in_cents)`, `Currency (currency)`, `NextAssessmentAt (next_assessment_at)`, `CurrentPeriodEndsAt (current_period_ends_at)`; its `Product (product)` → `Handle (handle)`, `Name (name)` | Case A: `SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`; `TryGetNoContent(out RawError)` [404], then `TryGetRawError(out RawError)`. Only a documented 404 begins a create attempt; parse/transport failures are not absence. | none | `operations/Subscriptions.md`; `records-3-Of-Su.md`; `records-4-Su-We.md` |
| `MaxioAdvancedBilling.Api.Subscriptions.CreateSubscription` | `CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, CancellationToken ct = default)` | `CreateSubscriptionRequest.Subscription (subscription)`: `MaxioAdvancedBilling.Models.CreateSubscription` !required; set `ProductHandle (product_handle): string?`, `CustomerReference (customer_reference): string?`, and `Reference (reference): string?`. Omit `ProductId`, `CustomerId`, payment-profile/card/bank fields, `CustomerAttributes`, pricing overrides, dates, and components. The operation notes permit product identification by `product_handle` and an existing customer by `customer_reference`; payment requirements depend on product configuration. | `SubscriptionResponse.Subscription` → `Id`, `Reference`, `State`, `ProductPriceInCents`, `Currency`, `NextAssessmentAt`, `CurrentPeriodEndsAt`; `Product` separately carries handle/name as above | Case A: `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`; `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422], then `TryGetRawError(out RawError)`. Do not expose raw provider messages; retain provider status and a caller-safe detail. | none | `operations/Subscriptions.md`; `records-2-Cr-Ne.md`; `records-3-Of-Su.md`; `records-4-Su-We.md` |
| `MaxioAdvancedBilling.Api.Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | no body; use mapped/repaired runtime `customerId` only | each nullable `SubscriptionResponse.Subscription` → `Id`, `Reference`, `State`, `ProductPriceInCents`, `Currency`, `NextAssessmentAt`, `CurrentPeriodEndsAt`; its nullable `Product` → `Handle`, `Name` | Case B: `SdkException<RawError>`; `StatusCode`, `ReadAsString()` | none | `operations/Customers.md`; `records-3-Of-Su.md`; `records-4-Su-We.md` |

### Enum values actually read

| Fully-qualified enum | Literal C# member (wire value) | Use | Source |
|---|---|---|---|
| `MaxioAdvancedBilling.Models.Enums.SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` | map state to the API DTO using the wire value; do not treat an unrecognized future value as a successful entitlement | `models/enums.md` |
| `MaxioAdvancedBilling.Models.Enums.IntervalUnit` | `Day (day)`, `Month (month)` | plan display interval | `models/enums.md` |

### Client, authentication, server facts

| Fact | Contract | Source |
|---|---|---|
| Package / root types | Package `AsadAli.AdvancedBilling.Sdk`; `MaxioAdvancedBilling.MaxioAdvancedBillingClient`; `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions`; constructor `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` |
| SDK namespaces | Controllers: `MaxioAdvancedBilling.Api`; records: `MaxioAdvancedBilling.Models`; enums: `MaxioAdvancedBilling.Models.Enums`; errors: `MaxioAdvancedBilling.Errors`; `SdkException<T>`: `MaxioAdvancedBilling.Core.Exceptions`; `RawError`: `MaxioAdvancedBilling.Core.ErrorResponse`; Basic credentials: `MaxioAdvancedBilling.Core.Authentication.Basic`; server enum: `MaxioAdvancedBilling.Servers` | `sdk-map.md` |
| Basic authentication | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions.BasicAuth` is `BasicAuthCredentials?`; set `Username` to the configured `Maxio:ApiKey` and `Password` to literal `"x"` before client construction. | `sdk-map.md` |
| Server selection and override | `ServerEnvironment.Us` is the documented US default. Set `options.Server.Production.Us.Site` from `Maxio:Subdomain`; if `Maxio:BaseUrl` is set, set `options.Server.Production.Us.BaseUrl` to it verbatim. Production US derives `https://{site}.chargify.com` when no override is supplied. | `sdk-map.md` |
| Application configuration | Bind exactly `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl`. Read raw secret values from the named environment variables only to set user-secrets; reject absent/blank required credentials and family handle at startup. | YOUR CALL — not in the map |
| User identity and storage | Resolve a current application user from PublicApi JWT claims; user-to-Maxio mapping, durable operation ledger, unique indexes, transaction/isolation strategy, DTO shape, and HTTP status design are application decisions. | YOUR CALL — not in the map |

## 3. Trap notes

⚠ Step 1 (client registration) — HTTP-client ownership, handler lifetime, and SDK client lifetime can cause connection exhaustion or stale DNS. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ Step 1 (authentication) — the SDK accepts a specific Basic-credentials options property and its credentials must be installed before construction. **MUST load `dotnet-authentication`** before wiring credentials.

⚠ Step 3 (operations) — list operations contain nullable parameters without C# defaults, and envelopes add a resource layer before the fields used by the application. **MUST load `dotnet-calling-endpoints`** before writing calls.

⚠ Step 3 (models) — request records are immutable/init-only, `required` members and nullable omission control serialized payload shape, and string-enums must be read safely. **MUST load `dotnet-models`** before constructing or mapping models.

⚠ Step 4 (error boundary) — typed and raw operation errors require different catch/accessor paths, and provider status must survive translation without leaking provider internals. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 1/5 (resilience and idempotency) — retry, timeout, cancellation, and base-URL settings affect whether a failed enrollment write can be re-sent and how ambiguous outcomes are reconciled. **MUST load `dotnet-configuration-resilience`** before wiring the client or the enrollment workflow.

⚠ Step 6 (tests) — faking the wrong seam hides serialization, retry, and envelope defects. **MUST load `dotnet-testing`** before writing integration tests.

⚠ Step 4 (error boundary) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary; **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 4 (error boundary) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

## 4. REQUIRED READING

Load these before implementation starts; this sheet deliberately does not carry their contents.

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 1 client/DI registration |
| `dotnet-authentication` | Step 1 Basic credentials |
| `dotnet-calling-endpoints` | Step 3 operation calls and response envelopes |
| `dotnet-models` | Step 3 request and response models/enums |
| `dotnet-error-handling` | Step 4 provider-error and JSON-error boundary |
| `dotnet-configuration-resilience` | Steps 1 and 5 server override, timeout, retry, cancellation |
| `dotnet-testing` | Step 6 SDK HTTP-handler seam and behavioral tests |

## 5. Assumptions & Blockers

| Type | Item |
|---|---|
| Assumption | The authenticated application user can supply stable first name, last name, and email. `CreateCustomer` requires all three; the Maxio map cannot establish where the application stores them. |
| Assumption | The configured site is US-hosted for the SDK's documented `ServerEnvironment.Us`/Production server node. The supplied configuration contract has no `Maxio:Environment` key, while the map exposes only US/EU server selection. A non-empty `Maxio:BaseUrl` overrides the production base address verbatim. |
| UNVERIFIED | The map documents `FindSubscription(reference)` but does not say that subscription `reference` is provider-unique. The durable app admission record plus lookup-before-create/reconcile-after-unknown-outcome prevents application retries and concurrent requests from creating another subscription; live verification must confirm the provider's reference behavior. |
| Blocker | None in the SDK map. |
