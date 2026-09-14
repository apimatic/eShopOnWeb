# Maxio subscription billing plan

## 1. Scope & sequence

| Step | Application work | Maxio operations | Source |
|---|---|---|---|
| 1. Package/configuration | Add NuGet `AsadAli.AdvancedBilling.Sdk` pinned to `1.0.2`; bind a validated options object from exactly `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl`. Keep all values out of tracked files. `BaseUrl` is optional; require `Subdomain` when it is absent. | Construct `MaxioAdvancedBilling.MaxioAdvancedBillingClient` with Basic auth and the Production server group. | `sdk-map.md` (package/tag, client, Servers & auth) |
| 2. Client registration | Register the SDK through a long-lived HTTP-client pipeline and expose one application-owned billing gateway to endpoint handlers. Set `ServerEnvironment.Us`; if `Maxio:BaseUrl` is nonblank set `options.Server.Production.Us.BaseUrl` to it unchanged, otherwise leave the template unchanged and set `options.Server.Production.Us.Site = Maxio:Subdomain`. | Client construction only. | `sdk-map.md`; `MaxioAdvancedBillingClientOptions.cs`; `ServerOptions.cs`; `Servers/ProductionOptions.cs` |
| 3. Catalog | Resolve the configured family by calling `ListProductFamilies` and exact-matching `ProductFamily.Handle`; use the returned `Id` converted invariantly to a string for `ListProductsForProductFamily`. Return only products whose `ArchivedAt` is null. Never persist/accept family or product numeric IDs as public identifiers; expose handles. Resolve again after a 404 so a re-seed cannot strand a cached ID. | `ProductFamilies.ListProductFamilies`; `ProductFamilies.ListProductsForProductFamily`. | `operations/ProductFamilies.md`; `records-3-Of-Su.md` |
| 4. Caller/customer | Resolve the authenticated token to the app's stable user ID plus first name, last name, and email. Use the stable user ID as the Maxio customer `reference`. Call `ReadCustomerByReference`; on 404 call `CreateCustomer`. If create returns 422, re-read by reference: a found customer means a concurrent creator won; otherwise propagate a safe validation failure. | `Customers.ReadCustomerByReference`; `Customers.CreateCustomer`. | `operations/Customers.md`; `records-1-Ac-Cr.md`; `records-2-Cr-Ne.md` |
| 5. Idempotent subscribe | Accept only `productHandle`; validate it is an active product in the configured family. Compute one deterministic application idempotency key/reference for `(userId, productHandle)`. Serialize this key with a database unique constraint plus an in-process keyed lock for the in-memory verification profile. Reconcile first with local Maxio subscription ID through `ReadSubscription`, then with `FindSubscription(reference)`. Only the unique-key owner may call create; create with `ProductHandle`, `CustomerReference`, deterministic `Reference`, and `PaymentCollectionMethod = CollectionMethod.Remittance` so the current Relationship Invoicing site uses cardless remittance instead of automatic collection. Persist the returned ID/state after success. A failed/unknown write stays reconcilable; never blindly repeat it. | `Subscriptions.ReadSubscription`; `Subscriptions.FindSubscription`; `Subscriptions.CreateSubscription`. | SDK calls: `operations/Subscriptions.md`; request/response: `records-2-Cr-Ne.md`, `records-3-Of-Su.md`, `records-4-Su-We.md`; enum: `models/enums.md`; locking/persistence: `YOUR CALL — not in the map` |
| 6. My subscriptions | Resolve the customer by reference. A customer-lookup 404 returns an empty list. Otherwise call the customer-scoped list and map each envelope; Maxio remains the billing system of record, while the local enrollment record is only an idempotency/reconciliation aid. | `Customers.ReadCustomerByReference`; `Customers.ListCustomerSubscriptions`. | SDK calls: `operations/Customers.md`; system-of-record/application split: `YOUR CALL — not in the map` |
| 7. HTTP surface | Add JWT-authenticated PublicApi endpoints following its existing endpoint conventions: `GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions`. Resolve identity only from the authenticated principal; never accept user/customer IDs from requests. Return handle/name, price in cents, billing interval, state, and `nextBillingDate` from `NextAssessmentAt`. | Calls from steps 3–6. | Routes/auth/DTOs: `YOUR CALL — not in the map`; response fields: `records-3-Of-Su.md`, `records-4-Su-We.md` |
| 8. Verification | Unit-test endpoint auth/identity, validation, mapping, duplicate concurrency, 404/422 recovery, ambiguous-write reconciliation, malformed 2xx/non-2xx JSON, and base-URL behavior. Add handler-level SDK tests without live credentials; then run a sandbox smoke test against the configured family and a disposable authenticated app user. | All in-scope calls. | Test design: `YOUR CALL — not in the map`; SDK HTTP seam requires `dotnet-testing` |

`ReadProductFamily` is deliberately not used for handle lookup: although its provider note mentions `handle:my-family`, the generated C# signature accepts `int id`, so the configured stable handle must be resolved through `ListProductFamilies`. Source: `operations/ProductFamilies.md`.

The SDK exposes no idempotency-key argument on `CreateSubscription`, and its Notes do not state that subscription `Reference` is unique. The deterministic reference is therefore a reconciliation key, not a provider-enforced mutex; application serialization is required. Source: `operations/Subscriptions.md` plus `YOUR CALL — not in the map` for the locking design.

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

All methods are asynchronous (`Task`-based); use named arguments for every nullable list/filter parameter.

| Controller property | Exact method signature | Request/query fields used | Response envelope and fields read | Error contract | Pagination | Source |
|---|---|---|---|---|---|---|
| `client.ProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` | Pass all five nullable filters explicitly as `null`. `BasicDateField` is `MaxioAdvancedBilling.Models.Enums.BasicDateField`. | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductFamilyResponse>`; each `.ProductFamily` is nullable. Read `Id (id): int?`, `Handle (handle): string?`, `ArchivedAt (archived_at): DateTimeOffset?` from `MaxioAdvancedBilling.Models.ProductFamily`. | Case B: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; `.Error.StatusCode`, `.ReadAsString()`, `.ReadAsJson<T>()`, `.ReadAsBytes()`. | None. | `operations/ProductFamilies.md`; `records-3-Of-Su.md` |
| `client.ProductFamilies` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | `productFamilyId` is the resolved numeric ID rendered as a string; pass the eight nullable filters explicitly (`includeArchived: false`; the rest `null`) and page deliberately. Filter/include enums live in `MaxioAdvancedBilling.Models.Enums`. | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>`; required `.Product`. Read `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `ArchivedAt (archived_at): DateTimeOffset?`, `ProductFamily (product_family): ProductFamily?`, `ProductPricePointHandle (product_price_point_handle): string?`. | Case A: `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`; `TryGetString(out string)` [404], inherited `TryGetRawError(out RawError)` fallback. | Manual `page` + `perPage`; continue until a page contains fewer than `perPage` items. | `operations/ProductFamilies.md`; `records-3-Of-Su.md` |
| `client.Customers` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | Query `reference` (wire `reference`) = stable app user ID. | `MaxioAdvancedBilling.Models.CustomerResponse`; required `.Customer`. Read `Id (id): int?`, `Reference (reference): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`. | Case B: `SdkException<RawError>`; use `RawError.StatusCode` for 404 and safe body accessors for diagnostics. | None. | `operations/Customers.md`; `records-2-Cr-Ne.md` |
| `client.Customers` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` | `MaxioAdvancedBilling.Models.CreateCustomerRequest.Customer (customer): CreateCustomer`, required. `CreateCustomer` fields: `FirstName (first_name): string`, `LastName (last_name): string`, `Email (email): string` are all required; set optional `Reference (reference): string?`. Leave address/tax/parent/Salesforce fields absent. | `CustomerResponse.Customer`; read the same customer fields above. | Case A: `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`; `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422], inherited `TryGetRawError(out RawError)` fallback. `CustomerErrorResponse1.Errors` is `Errors?`; the generated shared `Errors` model only declares `PerPage (per_page)` and `PricePoint (price_point)`, so do not depend on it to identify a reference collision—re-read by reference and otherwise emit a generic validation message. Live 422 shape: **UNVERIFIED**. | None. | `operations/Customers.md`; `records-1-Ac-Cr.md`; `records-2-Cr-Ne.md` |
| `client.Customers` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | Path `customerId` from `Customer.Id`; reject a successful customer envelope with null ID as an upstream-contract failure. | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>`; nullable `.Subscription`; subscription fields are in the shared mapping below. | Case B: `SdkException<RawError>`. | None. | `operations/Customers.md`; `records-4-Su-We.md` |
| `client.Subscriptions` | `FindSubscription(string? reference, CancellationToken ct = default)` | Pass deterministic subscription reference explicitly. | `MaxioAdvancedBilling.Models.SubscriptionResponse`; nullable `.Subscription`; shared mapping below. | Case A: `SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`; `TryGetNoContent(out RawError)` [404], inherited `TryGetRawError(out RawError)` fallback. | None. | `operations/Subscriptions.md`; `records-4-Su-We.md` |
| `client.Subscriptions` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | `MaxioAdvancedBilling.Models.CreateSubscriptionRequest.Subscription (subscription): CreateSubscription`, required. Set optional `ProductHandle (product_handle): string?`, `CustomerReference (customer_reference): string?`, `Reference (reference): string?`, and `PaymentCollectionMethod (payment_collection_method): MaxioAdvancedBilling.Models.Enums.CollectionMethod?` to `CollectionMethod.Remittance` (`remittance`) for cardless collection on current Relationship Invoicing. (`CollectionMethod.Invoice` / `invoice` is the legacy Statements Architecture value; it is not interchangeable.) Deliberately omit provider Notes alternatives `product_id`, product-price-point ID/handle (therefore default price point), `customer_id`, `customer_attributes`, and `payment_profile_id`. | `SubscriptionResponse.Subscription`; shared mapping below. | Case A: `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`; `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422], inherited `TryGetRawError(out RawError)` fallback. `ErrorListResponse1.Errors (errors): IReadOnlyList<string>` is required. | None. | `operations/Subscriptions.md`; `records-2-Cr-Ne.md`; `records-4-Su-We.md`; `models/enums.md` |
| `client.Subscriptions` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` | Pass `include: null` explicitly. `SubscriptionInclude` is `MaxioAdvancedBilling.Models.Enums.SubscriptionInclude`. | `SubscriptionResponse.Subscription`; shared mapping below. | Case B: `SdkException<RawError>`; treat 404 as a signal to reconcile through `FindSubscription`, not immediate permission to create. | None. | `operations/Subscriptions.md`; `records-4-Su-We.md` |

### Shared response mapping

| SDK model | Fields consumed | Source |
|---|---|---|
| `MaxioAdvancedBilling.Models.SubscriptionResponse` | `Subscription (subscription): MaxioAdvancedBilling.Models.Subscription?`; null on a 2xx is an upstream-contract failure. | `records-4-Su-We.md` |
| `MaxioAdvancedBilling.Models.Subscription` | `Id (id): int?`; `State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?`; `ProductPriceInCents (product_price_in_cents): long?`; `NextAssessmentAt (next_assessment_at): DateTimeOffset?` (map to API `nextBillingDate`); `Reference (reference): string?`; `Customer (customer): Customer?`; `Product (product): Product?`. Read product `Name`, `Handle`, `PriceInCents`, `Interval`, `IntervalUnit`; for an enrolled subscription prefer `ProductPriceInCents` as its confirmed price and fall back to `Product.PriceInCents` only when absent. | `records-3-Of-Su.md` |
| String-backed enums | `SubscriptionState.Value` and `IntervalUnit.Value` expose the exact wire string; `ToString()` also returns `Value`. | `Models/Enums/SubscriptionState.cs`; `Core/Enum/TypedEnum.cs` (pinned `v1.0.2` source lookup) |

### Enums actually consumed

All are generated `StringEnum<T>` records in `MaxioAdvancedBilling.Models.Enums`, not C# enums.

| Type | Static member → wire value | Source |
|---|---|---|
| `IntervalUnit` | `Day` → `day`; `Month` → `month` | `models/enums.md` |
| `SubscriptionState` | `Pending` → `pending`; `FailedToCreate` → `failed_to_create`; `Trialing` → `trialing`; `Assessing` → `assessing`; `Active` → `active`; `SoftFailure` → `soft_failure`; `PastDue` → `past_due`; `Suspended` → `suspended`; `Canceled` → `canceled`; `Expired` → `expired`; `Paused` → `paused`; `Unpaid` → `unpaid`; `TrialEnded` → `trial_ended`; `OnHold` → `on_hold`; `AwaitingSignup` → `awaiting_signup` | `models/enums.md` |

### Client construction, auth, and server nodes

| Fact | Exact contract | Source |
|---|---|---|
| Package/version | NuGet package `AsadAli.AdvancedBilling.Sdk`; this sheet is generated against source tag `v1.0.2` / commit `15db14b`, so pin `Version="1.0.2"` to keep the map and compiler aligned. Root namespace is `MaxioAdvancedBilling`; target is `netstandard2.0`. | `sdk-map.md` |
| Constructor | Only constructor: `MaxioAdvancedBilling.MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)`. | `sdk-map.md` |
| Options | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions`: `Environment: MaxioAdvancedBilling.Servers.ServerEnvironment`; `Retry: MaxioAdvancedBilling.Core.Configuration.RetryOptions`; `Server: MaxioAdvancedBilling.ServerOptions`; `BasicAuth: MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?`. | `sdk-map.md`; `MaxioAdvancedBillingClientOptions.cs` |
| Auth | Basic only: `BasicAuthCredentials.Username = Maxio:ApiKey`; `Password = "x"` (literal provider convention). | `sdk-map.md` |
| Hosting environment | `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (`"US"`, SDK default) resolves Production to `https://{site}.chargify.com`; `.Eu` (`"EU"`) resolves to `https://{site}.ebilling.maxio.com`. These values select hosting region, not sandbox versus production. | `sdk-map.md`; `Servers/ServerEnvironment.cs` |
| Site-derived URL | With US selected and no override, set `options.Server.Production.Us.Site = Maxio:Subdomain`; its default `BaseUrl` template is `https://{site}.chargify.com`. | `sdk-map.md`; `Servers/ProductionOptions.cs` |
| Verbatim override | When `Maxio:BaseUrl` is nonblank, assign it unchanged to `options.Server.Production.Us.BaseUrl`. Do not append paths, normalize, or combine it with the subdomain. These operations all use the Production group. | `sdk-map.md`; `Servers/ProductionOptions.cs`; in-scope operation pages |
| Configuration provenance | `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` are application binding keys dictated by the task. The SDK map has no `Sandbox` `ServerEnvironment` value and the task defines no `Maxio:Environment` binding key. | First clause: `YOUR CALL — not in the map`; environment values: `sdk-map.md` |

## 3. Trap notes

⚠ Step 1 (package/configuration) — package ID, root namespace, and child namespaces differ; missing child imports break compilation. **MUST load `dotnet-client-initialization`** before registration.

⚠ Step 2 (authentication) — credential timing/rotation can leave a constructed client unauthenticated or stale. **MUST load `dotnet-authentication`** before wiring secrets.

⚠ Steps 3–6 (calling endpoints) — many nullable parameters have no C# defaults and positional calls can silently bind the wrong filter. **MUST load `dotnet-calling-endpoints`** before the first call.

⚠ Steps 3–7 (models) — response envelopes, nullable inner payloads, generated string-enum access, and required initializers affect mapping and request construction. **MUST load `dotnet-models`** before writing models/mappers.

⚠ Steps 2 and 5 (resilience) — retry/timeout behavior determines whether an ambiguous failed POST might have executed and therefore whether it is safe to resend; base-URL override and pagination are in the same configuration surface. **MUST load `dotnet-configuration-resilience`** before configuring the client or retrying writes.

⚠ Steps 4–6 (error boundary) — Case A typed errors and Case B raw errors expose different status/body paths; confusing them loses actionable provider failures. **MUST load `dotnet-error-handling`** before writing catches or HTTP translation.

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
  maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
  and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 8 (tests) — faking controller internals rather than the supported HTTP seam makes tests couple to generated implementation details. **MUST load `dotnet-testing`** before writing integration-layer tests.

## 4. REQUIRED READING

Load all of these **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Steps 1–2: construction, DI, HTTP-client ownership/lifetime |
| `dotnet-authentication` | Step 2: Basic credentials and configuration/rotation |
| `dotnet-calling-endpoints` | Steps 3–6: controller calls, named optional parameters, cancellation |
| `dotnet-models` | Steps 3–7: required request members, nullable envelopes, enum values |
| `dotnet-error-handling` | Steps 4–6: Case A/Case B boundaries and `JsonException` paths |
| `dotnet-configuration-resilience` | Steps 2–5: server override, retry/timeout consequences, pagination |
| `dotnet-testing` | Step 8: HTTP seam and behavior-focused tests |

## 5. Assumptions & Blockers

### Assumptions

- The configured sandbox is US-hosted, so `ServerEnvironment.Us` and the Production server group are correct. A nonblank `Maxio:BaseUrl` remains the supported verbatim escape hatch.
- `MAXIO_ENVIRONMENT` describes deployment intent (`sandbox`) but is not persisted under an invented configuration key: the mandated binding surface has no `Maxio:Environment`, and the SDK environment type represents US/EU hosting only. Sandbox targeting comes from the sandbox subdomain or `Maxio:BaseUrl`.
- The PublicApi identity path can resolve a stable user ID, email, first name, and last name for every authenticated caller; the SDK requires all three customer contact fields.
- The product enrollment rule is at most one subscription per `(userId, productHandle)`. If repeat purchases of the same plan are later required, the caller contract needs a separate idempotency key.
- The sandbox uses current Relationship Invoicing, so cardless creation explicitly selects `CollectionMethod.Remittance`; the map reserves `CollectionMethod.Invoice` for legacy Statements Architecture. If a different configured catalog still requires automatic payment, this endpoint will receive Maxio's 422 and will not attempt card/3DS collection.

### Blockers

- None.
