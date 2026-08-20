# Maxio recurring subscriptions — implementation plan

## Scope & sequence

| Step | Implementation outcome | Maxio operations |
|---|---|---|
| 1 | Add `AsadAli.AdvancedBilling.Sdk` to `src/PublicApi` and bind/validate `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and optional `Maxio:BaseUrl`. Register one long-lived `HttpClient` pipeline and SDK client. Keep all credentials out of repository files. | Client construction only (`sdk-map.md`) |
| 2 | Add a Maxio gateway behind an application-owned interface. SDK records and exceptions must not cross endpoint/application boundaries. | All operations below |
| 3 | Implement catalog discovery from stable handles: locate configured family handle, resolve `eshop-pro` and `basic-plan`, reject archived/wrong-family/misconfigured products, and expose `GET /api/subscription-plans`. Mark `eshop-pro` as the API default. The runtime must never persist or configure Maxio numeric catalog IDs. | `ProductFamilies.ListProductFamilies`, `ProductFamilies.ListProductsForProductFamily` and/or `Products.ReadProductByHandle`; catalog audit also uses `Components.FindComponent` (`operations/ProductFamilies.md`, `operations/Products.md`, `operations/Components.md`) |
| 4 | Add a relational enrollment ledger with a unique key on `(UserId, ProductHandle)`, a stable provider subscription reference, Maxio IDs as observations, state (`Pending`, `Completed`, `Uncertain`), lease/concurrency token, and timestamps. Maxio remains the subscription/customer system of record; this table is only the idempotency/workflow ledger. | None |
| 5 | Derive the caller exclusively from the authenticated JWT (`sub`/name-identifier); load trusted email/given/family name from claims or the application identity store. Never accept a Maxio customer ID/reference or identity fields in the request. Ensure the Maxio customer by exact reference: lookup, create on 404, and on a create conflict re-read the same reference. | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer` (`operations/Customers.md`) |
| 6 | Implement `POST /api/subscriptions`: default missing plan handle to `eshop-pro`; resolve/validate the handle live; reserve the ledger row transactionally; reconcile by stable subscription reference; create with `ProductHandle`, `CustomerReference`, and `Reference`, without payment-profile/card/trial/setup fields; persist the returned Maxio ID; return the normalized subscription view. Parallel double-clicks must join/re-read one ledger operation. | `Subscriptions.FindSubscription`, `Subscriptions.CreateSubscription` (`operations/Subscriptions.md`) |
| 7 | Implement authenticated `GET /api/my-subscriptions`: resolve the caller's customer by reference, return `[]` for a genuine customer 404, otherwise list that customer's Maxio subscriptions and map every response. Do not use the site-wide list operation, which has no customer/reference filter. | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` (`operations/Customers.md`) |
| 8 | Add the exception translation boundary, structured redacted logs/correlation IDs, cancellation, health/readiness catalog audit, unit/integration/concurrency tests, then run the sandbox e2e sequence. | Error cases in every operation row below |

## Architecture and persistence decisions

| Concern | Required implementation |
|---|---|
| Public API | `GET /api/subscription-plans` returns `{ handle, name, priceInCents, interval, intervalUnit, isDefault }`. `POST /api/subscriptions` accepts only optional `{ planHandle }` and returns `201` on first completed enrollment or `200` when replaying the same completed enrollment. `GET /api/my-subscriptions` returns a list of the same subscription view. POST and `my-subscriptions` require JWT authorization; plans may remain read-only/public unless the host's existing API policy requires authentication. |
| Subscription view | `{ id, reference, planHandle, planName, priceInCents, currency, state, nextBillingDate }`; exact SDK mapping is in the contract sheet. Preserve cents as an integer—do not use binary floating point for money. |
| Boundaries | Endpoint → application service → application-owned `IMaxioBillingGateway` → SDK adapter. Persistence is accessed by an enrollment repository/unit of work, not by endpoints. Generated SDK types stay inside the adapter. |
| Maxio as system of record | Always re-read Maxio for catalog/subscription state. The local row does not authoritatively store price, plan state, billing date, customer state, or subscription state. It stores only workflow/idempotency facts and last observed provider IDs. |
| Application-level idempotency | A database unique constraint on `(UserId, ProductHandle)` is the cross-request/cross-instance gate. The winning transaction owns a short lease; losers reload/join. A deterministic persisted `SubscriptionReference` is used for `FindSubscription` recovery. A completed row returns the live Maxio subscription. An uncertain outcome is reconciled by reference before any later create attempt. Never hold a database transaction open during network I/O. |
| In-memory local mode | EF Core InMemory does not prove relational unique constraints, transactions, or concurrency. Add an in-process keyed single-flight guard only to make local demos deterministic, but retain the database unique constraint for production. Exercise the real idempotency behavior with SQLite in-memory integration tests (shared open connection), not EF InMemory. |
| Catalog invariants | Exact family handle equals configured `ProductFamilyHandle`; exact active handles are `eshop-pro` and `basic-plan`; expected prices are respectively `29900` and `2900` cents; both have `Interval == 1`, `IntervalUnit == Month`, no archive timestamp, no initial charge, no trial interval/price, and `RequestCreditCard`/`RequireCreditCard` are not true. `api-call` must resolve in that family with `Kind == MeteredComponent`. A mismatch is a configuration/readiness failure, never silently corrected by this app. There is deliberately no usage-reporting endpoint. |
| Error/API behavior | Invalid/unsupported plan handle: 400. Missing/invalid JWT: 401/403 through existing auth. Deterministic Maxio validation: 422 problem details with a safe generic message. Enrollment currently owned by another request: bounded wait, then 409/202 according to existing API convention. Maxio auth/host/configuration failures: 503. Other upstream/transport failures: 502/503. Missing/malformed 2xx envelope: 502. Never return raw provider bodies or credentials. |
| Runtime verification | Preserve the repository target frameworks/global SDK policy. If the pinned SDK is unavailable, use an explicitly documented compatible `global.json` SDK roll-forward for local build selection; if only a newer runtime is installed, use an explicit local runtime roll-forward setting rather than silently retargeting the projects. Run tests under the repository's intended target/runtime as the final check. |

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

### Package, client, authentication, and server

| Fact | Exact contract | Source |
|---|---|---|
| NuGet | Package ID `AsadAli.AdvancedBilling.Sdk`; pin `Version="1.0.2"` because this sheet is generated from tag `v1.0.2`/commit `15db14b`; target is `netstandard2.0`. Package ID is not the C# namespace. | `sdk-map.md` |
| Constructor | `new MaxioAdvancedBilling.MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)`; this is the only constructor. | `sdk-map.md` |
| Options | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` members: `Environment: MaxioAdvancedBilling.Servers.ServerEnvironment`, `Retry: MaxioAdvancedBilling.Core.Configuration.RetryOptions`, `Server: MaxioAdvancedBilling.ServerOptions`, `BasicAuth: MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?`. | `sdk-map.md` |
| Authentication | `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = settings.ApiKey, Password = "x" }`. Basic auth is the only scheme; the literal password is required by this SDK contract. | `sdk-map.md` |
| Sandbox/region | `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` is the US template `https://{site}.chargify.com`; `Eu` is `https://{site}.ebilling.maxio.com`. These select hosting region, not production-vs-sandbox. This task is sandbox-only and its supplied site is used with `Us`; reject deployment configuration that is not explicitly sandbox. | `sdk-map.md` |
| Base URL | If `Maxio:BaseUrl` is nonblank, assign it verbatim to `options.Server.Production.Us.BaseUrl`. Otherwise assign `settings.Subdomain` to `options.Server.Production.Us.Site`, deriving `https://{site}.chargify.com`. Do not concatenate, trim, normalize, or hardcode a host. All in-scope calls use the Production server group. | `sdk-map.md` |
| Controller properties | `client.ProductFamilies`, `client.Products`, `client.Components`, `client.Customers`, and `client.Subscriptions`; controller types live in `MaxioAdvancedBilling.Api`. | `sdk-map.md` |

### Operation contracts

| Use | Controller · exact generated signature | Return/envelope and fields read | Error and pagination | Source |
|---|---|---|---|---|
| Resolve configured family handle | `client.ProductFamilies.ListProductFamilies(MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, System.DateTimeOffset? startDate, System.DateTimeOffset? endDate, System.DateTimeOffset? startDatetime, System.DateTimeOffset? endDatetime, System.Threading.CancellationToken ct = default)`; pass all five nullable parameters explicitly as `null`. | `System.Collections.Generic.IReadOnlyList<MaxioAdvancedBilling.Models.ProductFamilyResponse>`; each response has `ProductFamily (product_family): MaxioAdvancedBilling.Models.ProductFamily?`; read `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?`, `ArchivedAt (archived_at): System.DateTimeOffset?`. Require exactly one active exact handle match and a nonnull ID. | Case B `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; use `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, or `ReadAsBytes()`. No pagination. | `operations/ProductFamilies.md`; `records-3-Of-Su.md` |
| List/filter products within resolved family | `client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, MaxioAdvancedBilling.Models.ListProductsFilter? filter, System.DateTimeOffset? startDate, System.DateTimeOffset? endDate, System.DateTimeOffset? startDatetime, System.DateTimeOffset? endDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, System.Threading.CancellationToken ct = default)`; pass runtime-resolved family ID as invariant string, all nullable filters `null` except `includeArchived: false`, and paginate until a short/empty page. | `System.Collections.Generic.IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>`; required `Product (product): MaxioAdvancedBilling.Models.Product`. Filter client-side by exact `Handle`. Read `Id`, `Name`, `Handle`, `PriceInCents`, `Interval`, `IntervalUnit`, `InitialChargeInCents`, `TrialPriceInCents`, `TrialInterval`, `TrialIntervalUnit`, `ArchivedAt`, `RequestCreditCard`, `RequireCreditCard`, and nested `ProductFamily.Handle`. | Case A `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`; `TryGetString(out string)` for 404; inherited `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` fallback. Manual `page` + `perPage`. | `operations/ProductFamilies.md`; `records-2-Cr-Ne.md`; `records-3-Of-Su.md` |
| Direct product handle reconciliation | `client.Products.ReadProductByHandle(string apiHandle, System.Threading.CancellationToken ct = default)`. | `MaxioAdvancedBilling.Models.ProductResponse`; required `Product (product): MaxioAdvancedBilling.Models.Product`, fields as above. Use for live re-read of a selected handle and revalidate its embedded family handle. | Case B `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` with `StatusCode`/raw readers. No pagination. | `operations/Products.md`; `records-3-Of-Su.md` |
| Audit metered component | `client.Components.FindComponent(string handle, System.Threading.CancellationToken ct = default)` with `handle: "api-call"`. | `MaxioAdvancedBilling.Models.ComponentResponse`; required `Component (component): MaxioAdvancedBilling.Models.Component`. Read `Handle (handle): string?`, `ProductFamilyHandle (product_family_handle): string?`, `Kind (kind): MaxioAdvancedBilling.Models.Enums.ComponentKind?`, and `Archived (archived): bool?`. | Case B `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` with `StatusCode`/raw readers. No pagination. | `operations/Components.md`; `records-1-Ac-Cr.md` |
| Find customer by app user | `client.Customers.ReadCustomerByReference(string reference, System.Threading.CancellationToken ct = default)`. | `MaxioAdvancedBilling.Models.CustomerResponse`; required `Customer (customer): MaxioAdvancedBilling.Models.Customer`. Read `Id (id): int?`, `Reference (reference): string?`, `Email`, `FirstName`, `LastName`. | Case B `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; 404 is determined from `MaxioAdvancedBilling.Core.ErrorResponse.RawError.StatusCode`. No pagination. | `operations/Customers.md`; `records-2-Cr-Ne.md` |
| Create customer | `client.Customers.CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, System.Threading.CancellationToken ct = default)`; body is nullable but must be passed explicitly and must not be null in this integration. | Request envelope `MaxioAdvancedBilling.Models.CreateCustomerRequest` has required `Customer (customer): MaxioAdvancedBilling.Models.CreateCustomer`. Inner required fields are `FirstName (first_name): string`, `LastName (last_name): string`, `Email (email): string`; set `Reference (reference): string?` to the authenticated app user ID. Response is required `MaxioAdvancedBilling.Models.CustomerResponse.Customer`. | Case A `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`; `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` for 422, where `Errors (errors): MaxioAdvancedBilling.Models.Errors?`; inherited `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` fallback. The operation documentation states customer `reference` is unique—on a race/conflict, re-read it. No pagination. | `operations/Customers.md`; `records-1-Ac-Cr.md`; `records-2-Cr-Ne.md` |
| Find subscription by ledger reference | `client.Subscriptions.FindSubscription(string? reference, System.Threading.CancellationToken ct = default)`; nullable but pass the nonnull persisted reference explicitly. | `MaxioAdvancedBilling.Models.SubscriptionResponse`; `Subscription (subscription): MaxioAdvancedBilling.Models.Subscription?`; a successful response with null inner payload is malformed. | Case A `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`; `TryGetNoContent(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` for 404; inherited `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` fallback. No pagination. | `operations/Subscriptions.md`; `records-3-Of-Su.md` |
| Create subscription | `client.Subscriptions.CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, System.Threading.CancellationToken ct = default)`; body is nullable but must be passed explicitly and nonnull here. | Request envelope has required `Subscription (subscription): MaxioAdvancedBilling.Models.CreateSubscription`. Set only `ProductHandle (product_handle): string?`, `CustomerReference (customer_reference): string?`, and stable `Reference (reference): string?`. Leave `ProductId`, price-point IDs, customer/card/payment-profile attributes, `InitialBillingAt`, `NextBillingAt`, `DeferSignup`, `CustomPrice`, trial/setup-like overrides, and components unset. Response is `MaxioAdvancedBilling.Models.SubscriptionResponse` with nullable inner subscription. | Case A `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`; `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` for 422 where required `Errors (errors): System.Collections.Generic.IReadOnlyList<string>`; inherited `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` fallback. No pagination. | `operations/Subscriptions.md`; `records-2-Cr-Ne.md`; `records-3-Of-Su.md` |
| List caller subscriptions | `client.Customers.ListCustomerSubscriptions(int customerId, System.Threading.CancellationToken ct = default)`; use the just-resolved nonnull Maxio customer ID. | `System.Collections.Generic.IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>`; every item has nullable `MaxioAdvancedBilling.Models.Subscription`; malformed null items are upstream failures, not empty subscriptions. | Case B `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` with `StatusCode`/raw readers. No pagination. | `operations/Customers.md`; `records-3-Of-Su.md` |

### Request/response construction and mappings

SDK records are immutable/init-only; there is no positional-constructor contract in the map. Construct envelopes with object initializers and set every `!req` property:

```csharp
new MaxioAdvancedBilling.Models.CreateCustomerRequest
{
    Customer = new MaxioAdvancedBilling.Models.CreateCustomer
    {
        FirstName = firstName,
        LastName = lastName,
        Email = email,
        Reference = userId
    }
};

new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
{
    Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
    {
        ProductHandle = productHandle,
        CustomerReference = userId,
        Reference = subscriptionReference
    }
};
```

| API field | Exact SDK source | Mapping rule | Source |
|---|---|---|---|
| Plan handle/name | `Subscription.Product?.Handle` / `.Name`; catalog uses `Product.Handle` / `.Name`. | Require nonnull values in a successful hero response. | `records-3-Of-Su.md` |
| Price | Hero response: `Subscription.ProductPriceInCents (product_price_in_cents): long?`; catalog: `Product.PriceInCents (price_in_cents): long?`. Currency is `Subscription.Currency (currency): string?`. | Return cents directly. Require price for configured products; do not recompute it from the seeded expectation. | `records-3-Of-Su.md` |
| State | `Subscription.State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?`. | Map by generated static-member equality to the wire strings listed below; preserve unknown/null as an upstream-contract failure rather than inventing a state. | `records-3-Of-Su.md`; `enums.md` |
| Next billing date | `Subscription.CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`. | Return as nullable `nextBillingDate`. The operation map's update-subscription note explicitly directs callers to `current_period_ends_at` to verify a `next_billing_at` change. Keep `NextAssessmentAt (next_assessment_at)` separate if later exposed; do not silently substitute it. | `operations/Subscriptions.md`; `records-3-Of-Su.md` |
| Maxio identity | `Subscription.Id`, `Subscription.Reference`, `Customer.Id`, `Customer.Reference`. | Provider IDs are response observations only. Catalog numeric IDs are never configured/persisted. | `records-2-Cr-Ne.md`; `records-3-Of-Su.md` |

### Relevant enum values

| Type | Generated C# member → wire value | Source |
|---|---|---|
| `MaxioAdvancedBilling.Models.Enums.IntervalUnit` | `Day` → `day`; `Month` → `month` | `enums.md` |
| `MaxioAdvancedBilling.Models.Enums.ComponentKind` | `MeteredComponent` → `metered_component`; `QuantityBasedComponent` → `quantity_based_component`; `OnOffComponent` → `on_off_component`; `PrepaidUsageComponent` → `prepaid_usage_component`; `EventBasedComponent` → `event_based_component` | `enums.md` |
| `MaxioAdvancedBilling.Models.Enums.CollectionMethod` | `Automatic` → `automatic`; `Remittance` → `remittance`; `Prepaid` → `prepaid`; `Invoice` → `invoice`. This integration leaves the request field unset; product/site configuration governs no-card collection. | `enums.md`; `records-2-Cr-Ne.md` |
| `MaxioAdvancedBilling.Models.Enums.SubscriptionState` | `Pending` → `pending`; `FailedToCreate` → `failed_to_create`; `Trialing` → `trialing`; `Assessing` → `assessing`; `Active` → `active`; `SoftFailure` → `soft_failure`; `PastDue` → `past_due`; `Suspended` → `suspended`; `Canceled` → `canceled`; `Expired` → `expired`; `Paused` → `paused`; `Unpaid` → `unpaid`; `TrialEnded` → `trial_ended`; `OnHold` → `on_hold`; `AwaitingSignup` → `awaiting_signup` | `enums.md` |
| `MaxioAdvancedBilling.Servers.ServerEnvironment` | `Us` → `US`; `Eu` → `EU` | `sdk-map.md` |

### Idempotency trust boundary and blocker

| Contract | What can be trusted | Consequence | Source |
|---|---|---|---|
| Customer reference | `CreateCustomer` explicitly documents that only one customer may exist for a reference and that reference is the app's unique ID. | Lookup/create/conflict/re-read is provider-backed idempotency for customers. | `operations/Customers.md` |
| Subscription reference | `CreateSubscription.Reference` exists and `FindSubscription(reference)` exists, but neither map row states reference uniqueness. `CreateSubscription` has no idempotency-key/header parameter. | The relational ledger and single-flight gate prevent ordinary concurrent double-clicks at the application layer and support best-effort reconciliation. They cannot establish provider-side exactly-once creation from this SDK contract alone. | `operations/Subscriptions.md`; `records-2-Cr-Ne.md` |
| Ambiguous POST retry | The SDK contract exposes only the body and cancellation token; no idempotency argument exists. Retry behavior is a required companion-skill concern. | **BLOCKER B1:** the requirement “never duplicate subscriptions” cannot be guaranteed end-to-end under an ambiguous transport failure with SDK-only evidence. Do not claim exactly-once behavior or enable a production create path until Maxio supplies a documented uniqueness/idempotency guarantee available through this SDK, or the product owner explicitly narrows the requirement to application-level double-click coalescing. No invented header/workaround is permitted. | `operations/Subscriptions.md`; `sdk-map.md` |

## Trap notes

- ⚠ Step 1 (client/DI) — `HttpClient` ownership and SDK-wrapper lifetime can cause socket exhaustion or lost handler policy. **MUST load `dotnet-client-initialization`** before registration.
- ⚠ Step 1 (auth) — credential timing/rotation and the Basic-auth manager boundary affect whether calls carry the configured identity. **MUST load `dotnet-authentication`** before wiring secrets.
- ⚠ Steps 1 and 6 (resilience/base URL) — retry, timeout, cancellation, verb safety, and the difference between SDK timeout and registered `HttpClient` timeout affect whether a failed write can be re-sent and whether `BaseUrl` reaches the correct server group. **MUST load `dotnet-configuration-resilience`** before configuration.
- ⚠ Steps 3, 5, 6, and 7 (calls) — nullable parameters without C# defaults and manual pagination make positional/partial calls unsafe. **MUST load `dotnet-calling-endpoints`** before the first operation call.
- ⚠ Steps 3, 5, 6, and 7 (models) — required init members, nullable envelopes, string-enum behavior, and wire-name/C#-name differences can silently corrupt mapping. **MUST load `dotnet-models`** before constructing or mapping records.
- ⚠ Step 8 (error boundary) — typed Case A errors and raw Case B errors have different access paths; parsing exception text loses status and payload shape. **MUST load `dotnet-error-handling`** before any catch ladder.
- ⚠ Step 8 (2xx JSON drift) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.
- ⚠ Step 8 (non-2xx JSON drift) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `System.Text.Json.JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.
- ⚠ Step 8 (tests) — mocking generated controllers couples tests to SDK internals and misses wire/envelope/error behavior. **MUST load `dotnet-testing`** before choosing the test seam.

## Verification matrix

| Layer | Required cases |
|---|---|
| Configuration | Missing/blank every required setting fails startup without printing its value; `BaseUrl` is assigned verbatim when present; otherwise the subdomain site variable is used; sandbox guard rejects a non-sandbox deployment marker; secrets are absent from tracked files/log output. |
| Catalog | Exact configured family handle; both product handles and exact monthly prices; wrong family; archived/missing/duplicate handle; trial/setup/card-required mismatch; all pages traversed; `api-call` exact family/kind; malformed/null envelope. |
| Customer | Existing lookup; 404→create; concurrent 404→one create conflict→re-read; missing JWT subject; missing trusted required identity fields; typed 422 and raw fallback. |
| Enrollment | Default and explicit plan; unsupported handle; two simultaneous identical POSTs return one Maxio subscription ID in the test double; completed replay; pending lease; crash/uncertain reconciliation via `FindSubscription`; 404 then create; typed 422; malformed response. Mark the ambiguous provider POST-retry scenario blocked rather than asserting an ungrounded guarantee. |
| Listing/mapping | Customer 404→empty list; all customer subscriptions mapped; cents/currency/state/current-period-end mapping; null inner subscription→upstream problem. |
| Persistence | SQLite in-memory verifies the unique `(UserId, ProductHandle)` index and optimistic concurrency. EF InMemory tests may cover endpoint plumbing only and must not be cited as concurrency proof. |
| HTTP boundary | Auth status, 400, 409/202 convention, 422, 502/503, cancellation; typed/raw exceptions; both 2xx and non-2xx `JsonException` paths; raw body and API key never leak. |

Sandbox e2e, after B1 is resolved or explicitly narrowed: copy current process environment values into .NET user-secrets as `Maxio:ApiKey` ← `MAXIO_API_KEY`, `Maxio:Subdomain` ← `MAXIO_SITE_SUBDOMAIN`, and `Maxio:ProductFamilyHandle` ← `MAXIO_DEFAULT_PRODUCT_FAMILY`; validate `MAXIO_ENVIRONMENT` denotes sandbox and do not write it to tracked configuration. Start with the repository's required SDK/runtime policy, obtain a real JWT, call plans and assert `eshop-pro`/`basic-plan`, issue two concurrent identical POSTs, assert the same subscription ID, call `my-subscriptions`, and verify in Maxio that the user reference has one customer and the expected subscription. Run the component audit for `api-call`; do not send usage.

## Assumptions & Blockers

- Assumption: the supplied site is US-hosted; `MAXIO_ENVIRONMENT` identifies sandbox, not the SDK's US/EU hosting enum.
- Assumption: JWT claims or the application's trusted identity store can supply nonempty first name, last name, and email required by `CreateCustomer`; otherwise enrollment must reject the request and identity provisioning is an application blocker.
- Assumption: the explicit handles and prices in the brief are intended as validation invariants; the application discovers them but does not create or repair Maxio catalog objects.
- Assumption: one enrollment per `(user, product handle)` is the intended double-click identity. A different business rule (for example multiple subscriptions to the same plan) requires an explicit caller idempotency key and a different ledger key.
- **BLOCKER B1:** the SDK exposes subscription reference lookup but does not document subscription-reference uniqueness or an idempotency key on create. Absolute provider-side duplicate prevention under ambiguous POST execution cannot be completed or claimed from the SDK contract; see the blocker table above.

## REQUIRED READING

Load every item below **before implementation starts**. This contract sheet deliberately does not carry their contents.

- `dotnet-client-initialization` — Step 1 client construction, `HttpClient` ownership, and DI lifetime.
- `dotnet-authentication` — Step 1 credentials and authentication lifecycle.
- `dotnet-configuration-resilience` — Steps 1, 3, 6, and 8 base URL, retries, timeout/cancellation, pagination, and write safety.
- `dotnet-calling-endpoints` — Steps 3, 5, 6, and 7 exact async call and optional-parameter usage.
- `dotnet-models` — Steps 3, 5, 6, and 7 record, envelope, nullable, and string-enum handling.
- `dotnet-error-handling` — Steps 5 through 8 typed/raw/JSON exception boundary; mandatory for every integration.
- `dotnet-testing` — Step 8 SDK seam, HTTP behavior, concurrency, and error-path tests.
