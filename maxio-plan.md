# Maxio subscription billing integration plan

## 1. Scope & sequence

| Step | Application work | Maxio operations | Source |
|---|---|---|---|
| 1 | Add the pinned package to `src/PublicApi`; bind and validate exactly `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and optional `Maxio:BaseUrl`; keep all values outside source control. | Client construction only | `sdk-map.md` |
| 2 | Register one long-lived `HttpClient` pipeline and construct the SDK client with Basic auth and the US Production server node pointed at the configured sandbox site. `Production` is the SDK server-group name; the SDK has no sandbox/live enum. | Client construction only | `sdk-map.md` |
| 3 | Build a billing integration boundary owned by `PublicApi`, not by endpoints. It maps SDK envelopes into application DTOs, normalizes errors, honors cancellation, and never exposes credentials or raw provider bodies. | All operations below | `sdk-map.md` |
| 4 | Discover the configured family by stable handle, then page through its non-archived products. Never persist or embed seeded product/family numeric IDs. Return plan handle/name/description/price/period. | `ProductFamilies.ListProductFamilies`; `ProductFamilies.ListProductsForProductFamily` | `operations/ProductFamilies.md` |
| 5 | Derive deterministic customer and subscription references from the authenticated application's user identity. Use a compact, stable, non-secret ASCII representation; the concrete reference format is application-owned. | `Customers.ReadCustomerByReference`; `Customers.CreateCustomer`; `Subscriptions.FindSubscription` | `operations/Customers.md`; `operations/Subscriptions.md` |
| 6 | Make customer creation idempotent: lookup by reference; if absent, create using the token/account identity's first name, last name, and email; on a concurrent 422, lookup the same reference again and accept the found customer. | `Customers.ReadCustomerByReference`; `Customers.CreateCustomer` | `operations/Customers.md` |
| 7 | Make subscribe idempotent for a user + product handle: take an application-level per-key lock; check a persistence row protected by a unique `(UserId, ProductHandle)` constraint; reconcile by stable subscription reference; validate that the requested product belongs to the configured family; read the site's invoicing architecture and choose its documented non-automatic collection method (`Remittance` for Relationship Invoicing, `Invoice` for legacy Statements); create only when no record/provider subscription exists; persist the returned provider ID/reference before releasing the lock. | `Subscriptions.FindSubscription`; `Sites.ReadSite`; `Subscriptions.CreateSubscription` | `operations/Subscriptions.md`; `operations/Sites.md` |
| 8 | Return the created/reconciled subscription from the SDK response: product handle/name, price in cents, currency, state, and next assessment date. | `Subscriptions.CreateSubscription`; `Subscriptions.FindSubscription` | `operations/Subscriptions.md`; `records-3-Of-Su.md`; `records-4-Su-We.md` |
| 9 | List the caller's subscriptions by resolving their Maxio customer reference. A 404 customer lookup is an empty collection; otherwise list that customer's subscriptions and map the same summary fields. | `Customers.ReadCustomerByReference`; `Customers.ListCustomerSubscriptions` | `operations/Customers.md` |
| 10 | Expose the mandated JWT-authenticated routes using the project's endpoint conventions: `GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions`. Caller identity must come only from the authenticated principal; never accept a user/customer ID from the request. | Calls only through the integration boundary | YOUR CALL — not in the map |
| 11 | Add unit/integration tests for catalog paging, envelopes/nulls, identity isolation, customer-create race recovery, concurrent double-click, existing subscription reconciliation, malformed/typed/raw errors, cancellation, and configuration/base-URL selection. | All operations below through a fake `HttpMessageHandler` | YOUR CALL — not in the map |

Build order: package/configuration → client registration → integration boundary and DTOs → catalog → customer idempotency → subscription idempotency/persistence → endpoints → tests → build/live sandbox verification.

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

### SDK/package and client construction

| Contract | Exact fact | Source |
|---|---|---|
| Package | NuGet `AsadAli.AdvancedBilling.Sdk`, pin version `1.0.2` so code matches this contract sheet (map stamp: source tag `v1.0.2`, commit `15db14b`). Root namespace is `MaxioAdvancedBilling`; target is `netstandard2.0`. | `sdk-map.md` |
| Constructor | `new MaxioAdvancedBilling.MaxioAdvancedBillingClient(System.Net.Http.HttpClient httpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions options)`; this is the only constructor. | `sdk-map.md` |
| Auth | `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = apiKey, Password = "x" }`. Basic auth is the sole scheme. | `sdk-map.md` |
| Environment | Set `options.Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us`. SDK values are only `Us` (`US`, default) and `Eu` (`EU`); there is no SDK sandbox enum. | `sdk-map.md`; `map/models/enums.md` |
| Derived site URL | When `Maxio:BaseUrl` is absent, assign the configured `Maxio:Subdomain` to `options.Server.Production.Us.Site`; the SDK template is `https://{site}.chargify.com`. | `sdk-map.md` |
| Verbatim override | When `Maxio:BaseUrl` is non-empty, assign that exact string to `options.Server.Production.Us.BaseUrl` and do not derive, append, trim, or substitute the subdomain into it. | `sdk-map.md` + task requirement |
| Options surface | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` properties: `Environment: MaxioAdvancedBilling.Servers.ServerEnvironment`, `Retry: MaxioAdvancedBilling.Core.Configuration.RetryOptions`, `Server: MaxioAdvancedBilling.ServerOptions`, `BasicAuth: MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?`. | `sdk-map.md` |
| Retry construction | `MaxioAdvancedBilling.Core.Configuration.RetryOptions` has required members and can start from `RetryOptions.Default()`: `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry`. Usage semantics must come from the required resilience skill. | `sdk-map.md` |
| Configuration names | The integration binds only `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and `Maxio:BaseUrl`. No SDK contract supplies a `Maxio:Environment` binding key. | YOUR CALL — not in the map |

### Operation contracts

| Controller property | Exact async signature and call purpose | Request/query model (C# name, wire name, type, required?) | Response envelope and fields read | Error contract | Pagination | Source |
|---|---|---|---|---|---|---|
| `client.ProductFamilies` | `System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<MaxioAdvancedBilling.Models.ProductFamilyResponse>> ListProductFamilies(MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, System.DateTimeOffset? startDate, System.DateTimeOffset? endDate, System.DateTimeOffset? startDatetime, System.DateTimeOffset? endDatetime, System.Threading.CancellationToken ct = default)`; pass all five nullable parameters explicitly as `null`, and `ct:`. | Query params: `dateField` (`date_field`), `startDate` (`start_date`), `endDate` (`end_date`), `startDatetime` (`start_datetime`), `endDatetime` (`end_datetime`); all nullable, no C# default. | Each `ProductFamilyResponse.ProductFamily (product_family): MaxioAdvancedBilling.Models.ProductFamily?`; select exact ordinal handle match using `ProductFamily.Handle (handle): string?`; require non-null `Id (id): int?` for the next call. | Case B: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; `RawError.StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. | None. | `operations/ProductFamilies.md`; `records-3-Of-Su.md` |
| `client.ProductFamilies` | `System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>> ListProductsForProductFamily(string productFamilyId, MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, MaxioAdvancedBilling.Models.ListProductsFilter? filter, System.DateTimeOffset? startDate, System.DateTimeOffset? endDate, System.DateTimeOffset? startDatetime, System.DateTimeOffset? endDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, System.Threading.CancellationToken ct = default)`; pass runtime-discovered family ID formatted invariantly, `null` filters/dates/include, `includeArchived: false`, and named paging/`ct:`. | `productFamilyId` is the route value. Query names are `date_field`, `filter`, `start_date`, `end_date`, `start_datetime`, `end_datetime`, `include_archived`, `include`, `page`, `per_page`. `ListProductsFilter` fields (unused): `Ids (ids): IReadOnlyList<int>?`, `PrepaidProductPricePoint (prepaid_product_price_point): PrepaidProductPricePointFilter?`, `UseSiteExchangeRate (use_site_exchange_rate): bool?`. | Each required `ProductResponse.Product (product): MaxioAdvancedBilling.Models.Product`; read `Id`, `Name`, `Handle`, `Description`, `PriceInCents`, `Interval`, `IntervalUnit`, `ArchivedAt`, `RequireCreditCard`, `ProductFamily`, `ProductPricePointHandle`. All inner fields are nullable. | Case A: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`; `TryGetString(out string)` for 404; inherited `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` fallback. | Manual `page` + `perPage`; continue until a page has fewer than `perPage` items. | `operations/ProductFamilies.md`; `records-2-Cr-Ne.md`; `records-3-Of-Su.md` |
| `client.Customers` | `System.Threading.Tasks.Task<MaxioAdvancedBilling.Models.CustomerResponse> ReadCustomerByReference(string reference, System.Threading.CancellationToken ct = default)`; use the deterministic application customer reference. | Query `reference` (`reference`): non-null `string`. | Required `CustomerResponse.Customer (customer): MaxioAdvancedBilling.Models.Customer`; read `Id`, `Reference`, `FirstName`, `LastName`, `Email`. Fields are nullable. | Case B: `SdkException<RawError>` as fully qualified above; inspect `StatusCode` for 404, otherwise use safe generic mapping and bounded diagnostics. | None. | `operations/Customers.md`; `records-2-Cr-Ne.md` |
| `client.Customers` | `System.Threading.Tasks.Task<MaxioAdvancedBilling.Models.CustomerResponse> CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, System.Threading.CancellationToken ct = default)`; `body` is nullable but has no default and must be passed. | `CreateCustomerRequest.Customer (customer): MaxioAdvancedBilling.Models.CreateCustomer`, C# required. Inner required fields: `FirstName (first_name): string`, `LastName (last_name): string`, `Email (email): string`. Set optional `Reference (reference): string?` to the deterministic customer reference. Omit address/tax/locale/etc. unless the application actually has authoritative values. | Required `CustomerResponse.Customer`; read `Id`, `Reference`, names, email. | Case A: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`; `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` for 422; inherited `TryGetRawError(out RawError)` fallback. `CustomerErrorResponse1.Errors (errors): MaxioAdvancedBilling.Models.Errors?`; generated `Errors` only exposes nullable `PerPage (per_page): IReadOnlyList<string>?` and `PricePoint (price_point): IReadOnlyList<string>?`, which do not describe customer fields. Therefore extract best-effort, then fall back to a generic validation message and reconcile by reference. | None. | `operations/Customers.md`; `records-1-Ac-Cr.md`; `records-2-Cr-Ne.md` |
| `client.Subscriptions` | `System.Threading.Tasks.Task<MaxioAdvancedBilling.Models.SubscriptionResponse> FindSubscription(string? reference, System.Threading.CancellationToken ct = default)`; pass stable subscription reference explicitly and `ct:`. | Query `reference` (`reference`): nullable `string?`, no default; integration supplies non-null. | `SubscriptionResponse.Subscription (subscription): MaxioAdvancedBilling.Models.Subscription?`; null is legal in the generated envelope and must be treated as malformed/no result. Read summary fields listed below. | Case A: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`; `TryGetNoContent(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` for 404; inherited `TryGetRawError(out RawError)` fallback. | None. | `operations/Subscriptions.md`; `records-4-Su-We.md` |
| `client.Sites` | `System.Threading.Tasks.Task<MaxioAdvancedBilling.Models.SiteResponse> ReadSite(System.Threading.CancellationToken ct = default)`; call before constructing a paymentless subscription request. | No body/query. | Required `SiteResponse.Site (site): MaxioAdvancedBilling.Models.Site`; read `RelationshipInvoicingEnabled (relationship_invoicing_enabled): bool?`. `true` selects `CollectionMethod.Remittance`; `false` selects `CollectionMethod.Invoice`; null is an incomplete site response and must not be guessed. `DefaultPaymentCollectionMethod` is a nullable string but is not needed for this choice. | Case B: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; `RawError.StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. | None. | `operations/Sites.md`; `records-3-Of-Su.md` |
| `client.Subscriptions` | `System.Threading.Tasks.Task<MaxioAdvancedBilling.Models.SubscriptionResponse> CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, System.Threading.CancellationToken ct = default)`; `body` nullable/no default but integration always supplies it. | `CreateSubscriptionRequest.Subscription (subscription): MaxioAdvancedBilling.Models.CreateSubscription`, C# required. Inner model marks no fields C# required; acceptance Notes require selecting product by `ProductHandle (product_handle): string?` or ID and existing customer by `CustomerReference (customer_reference): string?` or ID. Set `ProductHandle`, `CustomerReference`, stable `Reference (reference): string?`, and `PaymentCollectionMethod (payment_collection_method): MaxioAdvancedBilling.Models.Enums.CollectionMethod?` to the site-compatible non-automatic value from `ReadSite`. Intentionally omit numeric product/customer IDs, `CustomerAttributes`, payment/bank/card/profile fields, product price-point fields (default point is selected), and other optional billing overrides. | `SubscriptionResponse.Subscription` nullable. Read `Id (id): int?`, `Reference (reference): string?`, `State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `CurrentPeriodEndsAt`, `Currency`, `Customer`, and nested `Product` (`Name`, `Handle`, `PriceInCents`, `Interval`, `IntervalUnit`, `ProductPricePointHandle`). | Case A: `MaxioAdvancedBilling.Core.Exceptions.SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`; `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` for 422, whose required `Errors (errors)` is `IReadOnlyList<string>`; inherited `TryGetRawError(out RawError)` fallback. | None. | `operations/Subscriptions.md`; `records-2-Cr-Ne.md`; `records-3-Of-Su.md`; `records-4-Su-We.md`; `Models/CreateSubscription.cs` |
| `client.Customers` | `System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>> ListCustomerSubscriptions(int customerId, System.Threading.CancellationToken ct = default)`; use only a runtime customer ID returned by lookup/create. | Route `customerId`: non-null `int`; no body/query. | List of `SubscriptionResponse`; each inner `Subscription` is nullable. Map the same subscription summary fields. | Case B: `SdkException<RawError>` as fully qualified above. | None documented. | `operations/Customers.md`; `records-4-Su-We.md` |

### Request construction

```csharp
var customerBody = new MaxioAdvancedBilling.Models.CreateCustomerRequest
{
    Customer = new MaxioAdvancedBilling.Models.CreateCustomer
    {
        FirstName = firstName,
        LastName = lastName,
        Email = email,
        Reference = customerReference,
    },
};

var subscriptionBody = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
{
    Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
    {
        ProductHandle = productHandle,
        CustomerReference = customerReference,
        Reference = subscriptionReference,
        PaymentCollectionMethod = site.Site.RelationshipInvoicingEnabled switch
        {
            true => MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance,
            false => MaxioAdvancedBilling.Models.Enums.CollectionMethod.Invoice,
            null => throw new MaxioBillingException("Maxio returned an incomplete site response."),
        },
    },
};
```

The seeded product configuration permits omission of a payment profile, but it does not choose the subscription's collection method. `CreateSubscription.PaymentCollectionMethod` controls collection: current Relationship Invoicing accepts `remittance`, `automatic`, or `prepaid`; legacy Statements accepts `invoice` or `automatic`. A positive signup balance with the default/automatic method still requires an on-file payment method, so a paymentless enrollment explicitly uses `Remittance` on RI sites or `Invoice` on legacy sites. (`Models/CreateSubscription.cs`; `Models/Enums/CollectionMethod.cs`; `operations/Sites.md`)

### Response projection

| DTO field | SDK source field | Null handling | Source |
|---|---|---|---|
| Plan handle/name | `Subscription.Product?.Handle` / `.Name`; catalog uses `Product.Handle` / `.Name` | A product without a handle/name is skipped from plan discovery; a subscription missing product identity is a dependency-shape failure, not silently fabricated. | `records-3-Of-Su.md` |
| Price | Subscription: `Subscription.ProductPriceInCents`; catalog: `Product.PriceInCents` | Preserve integer cents. Do not infer a decimal/currency. Subscription may return `Currency`; catalog `Product` has no currency field. | `records-3-Of-Su.md` |
| Billing period | `Product.Interval`, `Product.IntervalUnit` | Nullable; serialize a nullable interval/unit. | `records-3-Of-Su.md`; `map/models/enums.md` |
| State | `Subscription.State` | Nullable/malformed response must not be called active. Serialize the documented wire value using the enum/string-enum support. | `records-3-Of-Su.md`; `map/models/enums.md` |
| Next billing date | `Subscription.NextAssessmentAt` | Nullable for states without a next assessment; do not substitute `CurrentPeriodEndsAt` as though equivalent. | `records-3-Of-Su.md` |

### Enum values used/read

All are `StringEnum<T>` in `MaxioAdvancedBilling.Models.Enums`, not C# enums.

| Type | Literal static members (wire values) | Source |
|---|---|---|
| `SubscriptionState` | `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` | `map/models/enums.md` |
| `IntervalUnit` | `Day (day)`, `Month (month)` | `map/models/enums.md` |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)`; the integration passes null. | `map/models/enums.md` |
| `ListProductsInclude` | `PrepaidProductPricePoint (prepaid_product_price_point)`; the integration passes null. | `map/models/enums.md` |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)`. The generated docs restrict legacy Statements to `Invoice`/`Automatic` and current Relationship Invoicing to `Remittance`/`Automatic`/`Prepaid`. | `map/models/enums.md`; `Models/CreateSubscription.cs`; `Models/Enums/CollectionMethod.cs` |

### Idempotency contract and constraints

| Fact/decision | Directive | Source |
|---|---|---|
| Customer reference | `CreateCustomer` Notes state only one customer may exist for a given `reference`; when supplied it must be unique and represents the caller application's ID. Use the same reference for lookup and create. | `operations/Customers.md` |
| Customer create race | A 422 is not enough to distinguish a duplicate reference from other validation failures. After 422, call `ReadCustomerByReference`; accept success only if the found reference matches, otherwise return the normalized validation failure. | `operations/Customers.md`; `records-2-Cr-Ne.md` |
| Subscription reconciliation field | `CreateSubscription.Reference` exists and `FindSubscription` finds a subscription by `reference`. Use one deterministic reference per application user + product handle for lookup-before-create and ambiguous-outcome reconciliation. | `operations/Subscriptions.md`; `records-2-Cr-Ne.md` |
| Subscription reference uniqueness | Neither `CreateSubscription` Notes nor the `CreateSubscription.Reference` model row says the provider enforces uniqueness. Do not rely on provider uniqueness for double-click protection. | UNVERIFIED |
| Provider idempotency-key parameter | `CreateSubscription` exposes only `body` and `ct`; the operation map documents no idempotency-key parameter/header. Do not invent one. | `operations/Subscriptions.md` |
| Application duplicate protection | Unique persistence key `(UserId, ProductHandle)` plus serialized execution is the primary duplicate gate; persisted states should distinguish in-progress, completed, and ambiguous/reconciliation-needed. On restart/ambiguous result, run `FindSubscription(reference)` before any new create. | YOUR CALL — not in the map |
| Provider retry ambiguity | Treat any transport/timeout result from create as ambiguous: do not issue an application-level create retry; reconcile with `FindSubscription` before allowing another create. Retry behavior inside the SDK must be configured with the required resilience skill. | YOUR CALL — not in the map |

## 3. Trap notes

⚠ Step 2 (client registration) — `HttpClient` ownership and the generated client wrapper have different lifetime concerns; choosing the wrong DI seam can exhaust sockets or make tests brittle. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ Step 2 (authentication) — credentials must be present at client construction and the Basic credential property/type are easy to place in the wrong namespace. **MUST load `dotnet-authentication`** before wiring credentials.

⚠ Steps 4–9 (calling operations) — optional parameters without C# defaults must still be passed explicitly, and positional calls can silently mis-bind. **MUST load `dotnet-calling-endpoints`** before the first call.

⚠ Steps 4–9 (models) — immutable records, required initializers, nullable response envelopes, and `StringEnum<T>` projection all affect correctness. **MUST load `dotnet-models`** before constructing or mapping models.

⚠ Steps 3, 6–9 (error boundary) — typed and raw SDK exceptions have different safe access paths, and the customer 422 payload is a suspicious generated shape. **MUST load `dotnet-error-handling`** before writing catches.

⚠ Step 2 and Step 7 (resilience) — timeout scope, retry triggers, write re-sends, server-node selection, and what cancellation bounds determine whether create outcomes are safe to retry. **MUST load `dotnet-configuration-resilience`** before configuring the client or the idempotency state machine.

⚠ Step 11 (tests) — the correct fake seam and meaningful failure-path assertions are not visible from controller signatures. **MUST load `dotnet-testing`** before writing integration tests.

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
  maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
  and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

## 4. REQUIRED READING

Load every item below **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 2: `HttpClient`, SDK wrapper, and DI lifetimes |
| `dotnet-authentication` | Step 2: Basic credentials and configuration timing |
| `dotnet-calling-endpoints` | Steps 4–9: exact async invocation, named args, cancellation |
| `dotnet-models` | Steps 4–9: immutable requests, envelopes, nullability, string enums |
| `dotnet-error-handling` | Steps 3, 6–9: Case A/B catches, raw fallback, both `JsonException` directions |
| `dotnet-configuration-resilience` | Steps 2, 4, 7: server/base URL, retries, timeout, pagination, logging consequences |
| `dotnet-testing` | Step 11: `HttpMessageHandler` seam and behavioral tests |

## 5. Assumptions & Blockers

- Assumption: the configured product-family handle identifies exactly one non-archived family; zero or multiple matches are treated as catalog/configuration failure.
- Assumption: the selected products remain configured not to require a payment profile, as stated in the task. The integration still explicitly selects the site's non-automatic collection method so a positive signup balance is invoiced/remitted rather than charged automatically.
- Assumption: the authenticated application identity can supply authoritative first name, last name, and email values required by `CreateCustomer`; how those are obtained is application-owned.
- Assumption: “double-click never creates two subscriptions” is implemented with application persistence + serialization and reconciliation by stable reference. Provider enforcement of unique subscription references is not documented and is not relied upon.
- Blockers: none for the requested sandbox flow. Exactly-once provider execution after an ambiguous transport failure cannot be proven from this SDK surface; the defensive requirement is to record the ambiguous state and reconcile by `FindSubscription` before permitting any later create.
