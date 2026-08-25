# Maxio subscription billing plan

## 1. Scope & sequence

| Step | SDK work | Application consequence | Source |
|---|---|---|---|
| 1. Package and client | Reference NuGet package `AsadAli.AdvancedBilling.Sdk` (map version `1.0.2`). Construct one SDK wrapper over a long-lived `HttpClient`; configure Basic auth and the Production server node. | Bind only `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and optional `Maxio:BaseUrl`. Reject missing required settings during startup. If `Maxio:BaseUrl` is nonblank, assign it verbatim to `options.Server.Production.Us.BaseUrl`; otherwise assign `Maxio:Subdomain` to `options.Server.Production.Us.Site`, producing the US template `https://{site}.chargify.com`. The SDK has geography environments (`Us`/`Eu`), not a sandbox/live switch; the configured sandbox site/override selects the sandbox. | `sdk-map.md` |
| 2. List plans | Call `ProductFamilies.ListProductFamilies`, select the one whose inner `ProductFamily.Handle` exactly equals `Maxio:ProductFamilyHandle`, then use its numeric `Id` (formatted invariantly) with `ProductFamilies.ListProductsForProductFamily`. Follow manual `page`/`perPage` pagination until a short page and exclude archived products. | `GET /api/subscription-plans` exposes only products belonging to the configured family. Return handle, name, recurring price in cents, interval, and interval unit; never accept or persist seeded numeric IDs as configuration. | `operations/ProductFamilies.md`; `records-3-Of-Su.md` |
| 3. Resolve caller and customer | Derive one stable, opaque customer reference from the authenticated eShop user ID. Call `Customers.ReadCustomerByReference`. On 404, call `Customers.CreateCustomer` with required name/email fields and that `Reference`. If concurrent creation returns 422, re-read by reference and use the winner. | Caller identity and profile resolution are application concerns. Persist the returned customer ID/reference as a cache, but always make the provider reference the idempotency authority: Maxio documents customer `reference` as unique. | `operations/Customers.md`; `records-1-Ac-Cr.md`; `records-2-Cr-Ne.md`; caller identity is `YOUR CALL — not in the map` |
| 4. Reserve a subscription request | Before any Maxio write, atomically reserve a deterministic application idempotency key for the authenticated user and selected plan (or a caller-supplied request key scoped to that user), backed by a database unique constraint. A completed reservation returns its existing subscription; an in-progress reservation is awaited/re-read rather than issuing another write. | This is the authoritative double-click/concurrent-request guard. Do not rely on `CreateSubscription.Reference` for uniqueness: the map exposes the field and lookup operation but does not document unique enforcement for subscription references. | `YOUR CALL — not in the map`; lack of a documented idempotency parameter/uniqueness guarantee: `operations/Subscriptions.md`, `records-2-Cr-Ne.md` |
| 5. Validate selected plan | Resolve the requested product handle through the Step 2 configured-family catalog (or refresh that catalog) and reject handles outside it before writing. | The public POST accepts a stable product handle, never a numeric Maxio ID. | SDK lookup surface: `operations/ProductFamilies.md`; request contract/policy: `YOUR CALL — not in the map` |
| 6. Reconcile before create | Give the reservation a deterministic subscription reference and call `Subscriptions.FindSubscription(reference: ..., ct: ...)`. A found subscription completes the reservation and is returned; the typed 404 means no existing subscription was found. | This recovers a prior accepted request whose local completion was lost. The reference is a reconciliation aid, not the primary concurrency guard. | `operations/Subscriptions.md`; reference format is `YOUR CALL — not in the map` |
| 7. Create subscription | Call `Subscriptions.CreateSubscription` with `CreateSubscriptionRequest.Subscription` containing `ProductHandle`, `CustomerReference`, `Reference`, and `PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance`. Omit `ProductId`, `CustomerId`, all price-point selectors, all payment-profile/card/bank fields, and all component fields. The sandbox's default automatic collection rejected the cardless $299 balance; the map identifies `remittance` as a valid Relationship Invoicing collection method, so the request must explicitly select it to avoid automatic payment collection. | On success, persist Maxio subscription ID/reference, product handle, and terminal reservation state atomically. On an ambiguous exception, attempt `FindSubscription` reconciliation before allowing any later create; never blindly replay from application code. | `operations/Subscriptions.md`; `records-2-Cr-Ne.md`; `map/models/enums.md`; rejection text verified by sandbox traffic |
| 8. Confirm POST result | Read the response one level through `SubscriptionResponse.Subscription`. Return plan handle/name, the subscription-specific `ProductPriceInCents`, `State`, and `NextAssessmentAt` as the public next-billing date, plus Maxio subscription ID/reference. If a completed reservation is replayed, call `Subscriptions.ReadSubscription(id, include: null, ct: ...)` for current provider state. | Null/missing envelope members are upstream-contract failures, not successful empty results. | `operations/Subscriptions.md`; `records-3-Of-Su.md`; `records-4-Su-We.md` |
| 9. List caller subscriptions | Resolve the caller's Maxio customer as in Step 3, then call `Customers.ListCustomerSubscriptions(customerId, ct: ...)`. Map every `SubscriptionResponse.Subscription` to the same account DTO used by POST. If the customer lookup is 404, return an empty list without creating a customer on a read-only request. | `GET /api/my-subscriptions` reflects Maxio, the billing system of record, instead of treating the local mapping as current billing state. | `operations/Customers.md`; `records-3-Of-Su.md`; `records-4-Su-We.md` |
| 10. Error boundary | Handle each operation's exact Case A/Case B exception below, preserve cancellation, and map provider validation/not-found/dependency failures to the application's established HTTP error convention. Treat malformed/drifted JSON separately as required in Trap notes. | Do not expose credentials or raw provider bodies to API callers/logs. | SDK cases: operation pages below; public error contract/logging policy: `YOUR CALL — not in the map` |
| 11. Tests | Exercise the SDK through the constructor's `HttpClient` seam with a controlled handler; separately fake the application billing port for endpoint/auth tests. Cover family pagination/filtering, envelope nulls, customer lookup/create/race, subscription reservation replay/concurrency, create/lookup/read, both error cases, malformed 2xx/non-2xx JSON, cancellation, and BaseUrl precedence. | A concurrency test must prove two simultaneous identical subscribe commands issue at most one application-level `CreateSubscription` call and return the same subscription. | SDK seam/constructor: `sdk-map.md`; application port/test layout: `YOUR CALL — not in the map` |

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

| Controller property | Exact method signature and call notes | Request model fields used | Response envelope and fields read | Error contract | Pagination | Source |
|---|---|---|---|---|---|---|
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.ProductFamilies` | `ListProductFamilies(MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)`; pass all five nullable/no-default parameters explicitly as `null`. | none | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductFamilyResponse>`; each envelope has `ProductFamily (product_family): MaxioAdvancedBilling.Models.ProductFamily?`; read `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?`, `ArchivedAt (archived_at): DateTimeOffset?`. | Case B: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; use `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, or `ReadAsBytes()` as governed by the error boundary. | none | `operations/ProductFamilies.md`; `records-3-Of-Su.md` |
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.ProductFamilies` | `ListProductsForProductFamily(string productFamilyId, MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, MaxioAdvancedBilling.Models.Enums.ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)`; pass all eight nullable/no-default parameters explicitly; use `includeArchived: false`, `include: null`. | none | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>`; envelope `Product (product): MaxioAdvancedBilling.Models.Product` is required. Read `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): MaxioAdvancedBilling.Models.Enums.IntervalUnit?`, `ArchivedAt (archived_at): DateTimeOffset?`, and optionally `ProductPricePointHandle (product_price_point_handle): string?`. | Case A: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`; `TryGetString(out string)` for 404; inherited `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` fallback. | manual `page` + `perPage`; defaults 1/20 | `operations/ProductFamilies.md`; `records-3-Of-Su.md` |
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.Customers` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | none | `MaxioAdvancedBilling.Models.CustomerResponse`; required `Customer (customer): MaxioAdvancedBilling.Models.Customer`; read `Id (id): int?`, `Reference (reference): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`. | Case B: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; inspect `StatusCode` (404 means absent) and safe raw accessors. | none | `operations/Customers.md`; `records-2-Cr-Ne.md` |
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.Customers` | `CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, CancellationToken ct = default)`; `body` is nullable but has no C# default and must be passed. Notes state `reference` is unique and represents the caller application's customer ID. | Envelope `MaxioAdvancedBilling.Models.CreateCustomerRequest`: `Customer (customer): MaxioAdvancedBilling.Models.CreateCustomer` required. Inner fields: `FirstName (first_name): string` required; `LastName (last_name): string` required; `Email (email): string` required; `Reference (reference): string?` included for idempotency. All address/tax/organization/payment-unrelated optional fields are intentionally omitted. | `MaxioAdvancedBilling.Models.CustomerResponse`; required `Customer`; read `Id`, `Reference`, `FirstName`, `LastName`, `Email`. | Case A: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`; `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` for 422, whose `Errors (errors): MaxioAdvancedBilling.Models.Errors?`; mapped `Errors` fields are `PerPage (per_page): IReadOnlyList<string>?` and `PricePoint (price_point): IReadOnlyList<string>?`; inherited `TryGetRawError(...)` fallback. On a possible uniqueness race, re-read by reference before classifying the 422. | none | `operations/Customers.md`; `records-1-Ac-Cr.md`; `records-2-Cr-Ne.md` |
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.Subscriptions` | `FindSubscription(string? reference, CancellationToken ct = default)`; `reference` is nullable/no-default and must be passed explicitly. Notes: finds a subscription by reference. | none | `MaxioAdvancedBilling.Models.SubscriptionResponse`; optional `Subscription (subscription): MaxioAdvancedBilling.Models.Subscription?`; fields listed in the shared subscription projection below. | Case A: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`; `TryGetNoContent(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` for 404; inherited `TryGetRawError(...)` fallback. | none | `operations/Subscriptions.md`; `records-4-Su-We.md` |
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.Subscriptions` | `CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, CancellationToken ct = default)`; `body` is nullable/no-default and must be passed. Notes require one product selector (`product_id` or `product_handle`) and one existing-customer selector (`customer_id` or `customer_reference`) when not creating a customer inline. | Envelope `MaxioAdvancedBilling.Models.CreateSubscriptionRequest`: `Subscription (subscription): MaxioAdvancedBilling.Models.CreateSubscription` required. Inner model marks no member C#-required, so acceptance fields are explicit: `ProductHandle (product_handle): string?`; `CustomerReference (customer_reference): string?`; `Reference (reference): string?`; `PaymentCollectionMethod (payment_collection_method): MaxioAdvancedBilling.Models.Enums.CollectionMethod? = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance`. Deliberately omit alternative `ProductId`, price-point selectors, `CustomerId`, `CustomerAttributes`, and all payment-profile/card/bank/component fields. | `MaxioAdvancedBilling.Models.SubscriptionResponse`; optional `Subscription`; shared projection below. | Case A: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`; `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` for 422, payload `Errors (errors): IReadOnlyList<string>` required; inherited `TryGetRawError(...)` fallback. | none | `operations/Subscriptions.md`; `records-2-Cr-Ne.md`; `records-4-Su-We.md`; `map/models/enums.md` |
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.Subscriptions` | `ReadSubscription(int subscriptionId, IReadOnlyList<MaxioAdvancedBilling.Models.Enums.SubscriptionInclude>? include, CancellationToken ct = default)`; pass `include: null` explicitly. | none | `MaxioAdvancedBilling.Models.SubscriptionResponse`; optional `Subscription`; shared projection below. | Case B: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; inspect `StatusCode` and safe raw accessors. | none | `operations/Subscriptions.md`; `records-4-Su-We.md` |
| `MaxioAdvancedBilling.MaxioAdvancedBillingClient.Customers` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | none | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>`; each optional `Subscription` is mapped through the shared projection below. | Case B: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; inspect `StatusCode` and safe raw accessors. | none | `operations/Customers.md`; `records-4-Su-We.md` |

### Shared subscription projection

| SDK model | Fields used | Source |
|---|---|---|
| `MaxioAdvancedBilling.Models.Subscription` | `Id (id): int?`; `Reference (reference): string?`; `State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?`; `ProductPriceInCents (product_price_in_cents): long?`; `NextAssessmentAt (next_assessment_at): DateTimeOffset?` (public `nextBillingDate`); `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`; `Customer (customer): MaxioAdvancedBilling.Models.Customer?`; `Product (product): MaxioAdvancedBilling.Models.Product?`; `ProductPricePointId (product_price_point_id): int?`. From nested `Product`, read `Handle`, `Name`, `Interval`, `IntervalUnit`, and `PriceInCents`. | `records-3-Of-Su.md` |
| `MaxioAdvancedBilling.Models.SubscriptionResponse` | Exactly one mapped field: `Subscription (subscription): MaxioAdvancedBilling.Models.Subscription?`. The inner value is nullable even on a successful response; guard it. | `records-4-Su-We.md` |

### Enum values actually used

| Fully-qualified SDK type | Generated static members and wire values | Source |
|---|---|---|
| `MaxioAdvancedBilling.Models.Enums.IntervalUnit` | `Day` (`day`), `Month` (`month`) | `map/models/enums.md` |
| `MaxioAdvancedBilling.Models.Enums.CollectionMethod` | `Automatic` (`automatic`), `Remittance` (`remittance`), `Prepaid` (`prepaid`), `Invoice` (`invoice`). Relationship Invoicing accepts `remittance`, `automatic`, and `prepaid`; this integration uses `Remittance`. | `map/models/enums.md` |
| `MaxioAdvancedBilling.Models.Enums.SubscriptionState` | `Pending` (`pending`), `FailedToCreate` (`failed_to_create`), `Trialing` (`trialing`), `Assessing` (`assessing`), `Active` (`active`), `SoftFailure` (`soft_failure`), `PastDue` (`past_due`), `Suspended` (`suspended`), `Canceled` (`canceled`), `Expired` (`expired`), `Paused` (`paused`), `Unpaid` (`unpaid`), `TrialEnded` (`trial_ended`), `OnHold` (`on_hold`), `AwaitingSignup` (`awaiting_signup`) | `map/models/enums.md` |
| `MaxioAdvancedBilling.Servers.ServerEnvironment` | `Us` (wire/value `US`, SDK default), `Eu` (`EU`) | `sdk-map.md` |

### Client construction, authentication, and server node

| Fact | Exact contract | Source |
|---|---|---|
| Client | `MaxioAdvancedBilling.MaxioAdvancedBillingClient`; only constructor: `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)`. Every controller above is a property on this client. | `sdk-map.md` (`MaxioAdvancedBillingClient.cs`) |
| Options | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` properties: `Environment: MaxioAdvancedBilling.Servers.ServerEnvironment`; `Retry: MaxioAdvancedBilling.Core.Configuration.RetryOptions`; `Server` (server-node options); `BasicAuth: MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?`. | `sdk-map.md` |
| Basic auth | `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }`. Username is the API key; password is the literal `"x"`. | `sdk-map.md` |
| Derived US host | Set `options.Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us` and `options.Server.Production.Us.Site = <Maxio:Subdomain>`; Production US template is `https://{site}.chargify.com`. | `sdk-map.md` |
| Base URL override | When `Maxio:BaseUrl` is set, assign it verbatim to `options.Server.Production.Us.BaseUrl` instead of deriving from subdomain. These operations all use the Production server group, not Ebb. | `sdk-map.md` |
| Configuration names | Bind exactly `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and `Maxio:BaseUrl`; no SDK map fact supplies any additional application configuration key. | Binding names: `YOUR CALL — not in the map`; SDK option destinations: `sdk-map.md` |
| Retry model | `MaxioAdvancedBilling.Core.Configuration.RetryOptions` has required members and a `Default()` factory; implementation semantics must come from the required resilience skill before configuring it. | `sdk-map.md` |

## 3. Trap notes

⚠ Step 1 (client registration) — `HttpClient` ownership and SDK-wrapper lifetime determine socket reuse and testability. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ Step 1 (authentication) — credential timing and the exact credentials property determine whether requests are authenticated. **MUST load `dotnet-authentication`** before setting Basic auth.

⚠ Steps 2–9 (endpoint calls) — optional parameters without C# defaults can silently mis-bind in positional calls, and response envelopes add an inner level. **MUST load `dotnet-calling-endpoints`** before the first call.

⚠ Steps 2–9 (models) — generated string-enum and nullable/required-member behavior affects mapping and request construction. **MUST load `dotnet-models`** before constructing or reading SDK models.

⚠ Steps 4–8 (idempotent write) — retry and timeout behavior determines whether a failed subscription write can be sent more than once and whether an outcome is ambiguous. **MUST load `dotnet-configuration-resilience`** before configuring the client or finalizing the write/reconciliation boundary.

⚠ Step 2 (pagination) — list-page termination and cancellation must be implemented explicitly. **MUST load `dotnet-configuration-resilience`** before writing the pagination loop.

⚠ Step 10 (typed versus raw errors) — the catch type and safe body access differ by operation, so a generic SDK catch ladder can lose useful status/error information. **MUST load `dotnet-error-handling`** before writing that boundary.

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
  maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
  and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 11 (tests) — choosing the wrong seam couples tests to generated controllers and misses the serialized HTTP contract. **MUST load `dotnet-testing`** before writing integration-layer tests.

## 4. REQUIRED READING

Load every skill below **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 client construction, `HttpClient` ownership, and DI lifetime |
| `dotnet-authentication` | Step 1 Basic credentials and configuration timing |
| `dotnet-calling-endpoints` | Steps 2–9 controller ownership, named arguments, async calls, envelopes, cancellation |
| `dotnet-models` | Steps 2–9 required/nullable records and string enums |
| `dotnet-error-handling` | Step 10 exact exception boundary, typed/raw payload access, and both `JsonException` paths |
| `dotnet-configuration-resilience` | Steps 1–8 server override, retry/timeout consequences, cancellation, pagination |
| `dotnet-testing` | Step 11 `HttpClient` seam, realistic wire fixtures, error/concurrency coverage |

## 5. Assumptions & Blockers

### Assumptions

- The authenticated application identity supplies one stable eShop user identifier and enough profile data to populate Maxio's required customer `FirstName`, `LastName`, and `Email`; the exact claim/profile lookup and any deterministic non-empty fallback are application decisions (`YOUR CALL — not in the map`).
- `Subscription.NextAssessmentAt` is the account API's `nextBillingDate`; this is the SDK's next assessment timestamp, while the response model has no `NextBillingAt` member (`records-3-Of-Su.md`).
- Double-click idempotence is enforced by the application's durable unique reservation before it enters the SDK. `CreateSubscription.Reference` plus `FindSubscription` is used for reconciliation, but the map does not document subscription-reference uniqueness and exposes no idempotency-key parameter (`UNVERIFIED`; `operations/Subscriptions.md`, `records-2-Cr-Ne.md`).
- The supplied sandbox seed contract is authoritative that the selected products need no payment method. The live sandbox nevertheless rejected the default collection path for lack of a payment method, so `CreateSubscription.PaymentCollectionMethod` is explicitly `CollectionMethod.Remittance`; payment-profile/card/bank fields remain absent.

### Blockers

- None for the requested browse/subscribe/account flow. Exactly-once creation after an accepted-but-response-lost provider POST cannot be proven from the bundled map because subscription-reference uniqueness is undocumented (`UNVERIFIED`); the implementation must never blind-replay such an ambiguous write and must reconcile by reference first.
