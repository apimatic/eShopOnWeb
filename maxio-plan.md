# Maxio subscription billing plan

## 1. Scope & sequence

| Step | Application work | Maxio operations |
| --- | --- | --- |
| 1 | Package the pinned source SDK as a repository-local NuGet package; bind and validate the four `Maxio:` settings; register one long-lived HTTP transport and the SDK adapter. | Client construction only |
| 2 | Add an additive subscription aggregate/mapping in `CatalogContext`, including a unique `(UserId, ProductHandle)` index and a migration. | None |
| 3 | Implement the plan catalog adapter, paging through only the configured family and excluding archived products. | `ProductFamilies.ListProductsForProductFamily` |
| 4 | Implement idempotent enrollment: resolve the authenticated Identity user, derive opaque deterministic customer/subscription references, serialize same-key requests, reconcile Maxio first, create or recover the unique customer, create the subscription once, and persist the completed mapping. | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Subscriptions.FindSubscription`, `Subscriptions.CreateSubscription` |
| 5 | Implement account lookup against Maxio as system of record; a user without a Maxio customer gets an empty list. | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |
| 6 | Expose three JWT-required MinimalApi endpoints following `CatalogItemEndpoints/CatalogItemListPagedEndpoint.cs`; map provider/domain failures to safe HTTP problem responses. | Operations above |
| 7 | Add adapter, idempotency, endpoint/auth, and persistence tests; then build/test and exercise the live sandbox hero flow. | Operations above |

Repository conventions: MinimalApi endpoint shape — `src/PublicApi/CatalogItemEndpoints/CatalogItemListPagedEndpoint.cs`; JWT identity creation — `src/Infrastructure/Identity/IdentityTokenClaimService.cs`; EF aggregate mapping — `src/Infrastructure/Data/CatalogContext.cs`; integration-host testing — `tests/PublicApiIntegrationTests/ProgramTest.cs`.

## 2. CONTRACT SHEET

WARNING: Signatures below are generated code, verbatim; every parameter name is the literal C# identifier, including named argument `ct:`.
WARNING: Every SDK type below is fully qualified from the namespace implied by the map-named source path for that type, never inferred from a neighbouring type.

| Controller property | Method signature | Request model / fields used | Response envelope / fields read | Error and pagination | Source |
| --- | --- | --- | --- | --- | --- |
| `ProductFamilies` | `ListProductsForProductFamily(string productFamilyId, MaxioAdvancedBilling.Models.Enums.BasicDateField? dateField, MaxioAdvancedBilling.Models.ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, MaxioAdvancedBilling.Models.Enums.ListProductsInclude? include, int? page = 1, int? perPage = 20, MaxioAdvancedBilling.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` | No body. `productFamilyId` accepts an ID or `handle:<handle>`; call with configured handle in that form, `includeArchived: false`, `perPage: 200`, and advance `page` until a short page. | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>`; `ProductResponse.Product (product): Product, required`; read `Product.Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `ArchivedAt (archived_at): DateTimeOffset?`, `RequireCreditCard (require_credit_card): bool?`, `DefaultProductPricePointId (default_product_price_point_id): int?`, `ProductPricePointId (product_price_point_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?`. | Case A `SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`; `TryGetString(out string)` for 404, `TryGetRawError(out RawError)` fallback. Page-based pagination; max `perPage` 200. | `map/operations/ProductFamilies.md`; `Api/ProductFamilies.cs`; `Models/ProductResponse.cs`; `Models/Product.cs` |
| `Customers` | `ReadCustomerByReference(string reference, MaxioAdvancedBilling.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` | No body; deterministic opaque app customer reference. | `MaxioAdvancedBilling.Models.CustomerResponse`; `Customer (customer): Customer, required`; read `Customer.Id (id): int?`, `Reference (reference): string?`, `Email (email): string?`. | Case B `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` exposing `StatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()`; no pagination. | `map/operations/Customers.md`; `Api/Customers.cs`; `Models/CustomerResponse.cs`; `Models/Customer.cs`; `sdk-map.md` |
| `Customers` | `CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, MaxioAdvancedBilling.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateCustomerRequest.Customer (customer): CreateCustomer, required`; `CreateCustomer.FirstName (first_name): string, required`, `LastName (last_name): string, required`, `Email (email): string, required`, `Reference (reference): string?, optional`. No address, phone, CC email, organization, tax, locale, or other optional fields are sent. Provider prose says only one customer may be created for a given reference and a supplied reference must be unique. | `MaxioAdvancedBilling.Models.CustomerResponse`; read required `Customer` envelope and its `Id`, `Reference`, `Email`. | Case A `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>`; `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` for 422, `TryGetRawError(out RawError)` fallback. Payload `CustomerErrorResponse1.Errors (errors): Errors1?, optional`; no pagination. | `map/operations/Customers.md`; `Api/Customers.cs`; `Models/CreateCustomerRequest.cs`; `Models/CreateCustomer.cs`; `Models/CustomerResponse.cs`; `Models/Customer.cs`; `Models/CustomerErrorResponse1.cs`; `Errors/CreateCustomerError.cs` |
| `Subscriptions` | `FindSubscription(string? reference, MaxioAdvancedBilling.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` | No body; deterministic opaque per-user/per-plan subscription reference. | `MaxioAdvancedBilling.Models.SubscriptionResponse`; `Subscription (subscription): Subscription?, optional`; read subscription fields listed below. | Case A `SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`; `TryGetNoContent(out RawError)` for 404, `TryGetRawError(out RawError)` fallback; no pagination. | `map/operations/Subscriptions.md`; `Api/Subscriptions.cs`; `Models/SubscriptionResponse.cs`; `Models/Subscription.cs`; `Errors/FindSubscriptionError.cs` |
| `Subscriptions` | `CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, MaxioAdvancedBilling.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CreateSubscriptionRequest.Subscription (subscription): CreateSubscription, required`; inner model marks no field `required`, but endpoint/model prose requires one product selector and one customer selector. Send `ProductHandle (product_handle): string?`, `CustomerId (customer_id): int?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?` as `CollectionMethod.Remittance`, and `Reference (reference): string?`. Remittance is a current Relationship Invoicing collection method and permits the seeded no-card flow without attempting an immediate automatic charge. Omit numeric product ID, price-point selector (therefore configured default price), customer attributes, payment-profile/card/bank data, coupons, components, billing-date overrides, and all other optionals. | `MaxioAdvancedBilling.Models.SubscriptionResponse`; read optional `Subscription`; then `Id (id): int?`, `State (state): SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `Customer (customer): Customer?`, `Product (product): Product?`, `ProductPricePointId (product_price_point_id): int?`, `Reference (reference): string?`, `Currency (currency): string?`; nested product fields as in the plan-list row. | Case A `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`; `TryGetErrorListResponse1(out ErrorListResponse1)` for 422, `TryGetRawError(out RawError)` fallback. Payload `ErrorListResponse1.Errors (errors): IReadOnlyList<string>, required`; no pagination. No caller-supplied idempotency-key parameter. | `map/operations/Subscriptions.md`; `Api/Subscriptions.cs`; `Models/CreateSubscriptionRequest.cs`; `Models/CreateSubscription.cs`; `Models/Enums/CollectionMethod.cs`; `Models/SubscriptionResponse.cs`; `Models/Subscription.cs`; `Models/ErrorListResponse1.cs`; `Errors/CreateSubscriptionError.cs` |
| `Customers` | `ListCustomerSubscriptions(int customerId, MaxioAdvancedBilling.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` | No body. | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>`; read the same subscription and nested product fields as above. | Case B `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` with the four raw accessors; no pagination. | `map/operations/Customers.md`; `Api/Customers.cs`; `Models/SubscriptionResponse.cs`; `Models/Subscription.cs`; `Models/Product.cs`; `sdk-map.md` |

Enums actually read:

| Type | Members / wire values | Source |
| --- | --- | --- |
| `MaxioAdvancedBilling.Models.Enums.IntervalUnit` | `Day` = `day`; `Month` = `month` | `Models/Enums/IntervalUnit.cs` |
| `MaxioAdvancedBilling.Models.Enums.SubscriptionState` | `Pending` = `pending`; `FailedToCreate` = `failed_to_create`; `Trialing` = `trialing`; `Assessing` = `assessing`; `Active` = `active`; `SoftFailure` = `soft_failure`; `PastDue` = `past_due`; `Suspended` = `suspended`; `Canceled` = `canceled`; `Expired` = `expired`; `Paused` = `paused`; `Unpaid` = `unpaid`; `TrialEnded` = `trial_ended`; `OnHold` = `on_hold`; `AwaitingSignup` = `awaiting_signup` | `Models/Enums/SubscriptionState.cs` |

Client/auth/server facts:

| Fact | Contract | Source |
| --- | --- | --- |
| Construction | `new MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions)`; controllers are lazy client properties. | `sdk-map.md`; `MaxioAdvancedBillingClient.cs` |
| Authentication | `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = ApiKey, Password = "x" }` assigned to `MaxioAdvancedBillingClientOptions.BasicAuth`; operations accept BasicAuth OR BearerAuth. | `sdk-map.md`; `MaxioAdvancedBillingClientOptions.cs` |
| Environment/server | Scope uses server group `Production`. `ServerEnvironment.Us` resolves `https://{site}.chargify.com`; set `options.Server.Production.Us.Site` from `Maxio:Subdomain`. If `Maxio:BaseUrl` is nonblank, assign it verbatim to `options.Server.Production.Us.BaseUrl`. | `sdk-map.md`; `Servers/ServerEnvironment.cs` |
| SDK distribution | SDK is not on NuGet. Build/package the SDK source at commit `ba14a644cfb57641ffe81e214bcfc6dcc6e769de` and consume the repository-local package so clean builds do not depend on an absolute machine path. | `dotnet-getting-started` Install section; `YOUR CALL — not in the map` for local-package layout |

## 3. Trap notes

- Client registration: transport ownership/lifetime can cause socket exhaustion or silently bypass the configured pipeline. **MUST load `maxio-platforms-team:dotnet-client-initialization`**
- Authentication/configuration: credential timing and scheme selection can cause unauthenticated calls. **MUST load `maxio-platforms-team:dotnet-authentication`**
- All calls: generated nullable parameters without defaults and named-argument binding can call the wrong shape. **MUST load `maxio-platforms-team:dotnet-calling-endpoints`**
- Request/response mapping: generated immutable records, `StringEnum<T>`, required envelopes, nullable inner data, and unknown fields can be mishandled. **MUST load `maxio-platforms-team:dotnet-models`**
- Error boundary: operation-specific typed-vs-raw errors and fallback accessors can lose provider context or leak bodies. **MUST load `maxio-platforms-team:dotnet-error-handling`**
- Error boundary: a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException`, so an SDK-exception-only catch ladder lets it escape. **MUST load `maxio-platforms-team:dotnet-error-handling`**
- Error boundary: a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so it **replaces** the `SdkException` and the HTTP status is destroyed with it. **MUST load `maxio-platforms-team:dotnet-error-handling`**
- Reads/writes: per-attempt timeout, retry eligibility, base-URL overrides, paging termination, ambiguous writes, and body logging can violate the total budget or duplicate/leak data. **MUST load `maxio-platforms-team:dotnet-configuration-resilience`**
- Tests: faking SDK internals instead of the supported HTTP seam makes behavior assertions brittle. **MUST load `maxio-platforms-team:dotnet-testing`**

## 4. REQUIRED READING

Load all of these before implementation starts; this sheet deliberately does not carry their contents.

| Skill | Governs |
| --- | --- |
| `maxio-platforms-team:dotnet-client-initialization` | SDK packaging, HTTP lifetime, DI registration |
| `maxio-platforms-team:dotnet-authentication` | Basic credential setup and rotation behavior |
| `maxio-platforms-team:dotnet-calling-endpoints` | Generated call binding and cancellation |
| `maxio-platforms-team:dotnet-models` | Request creation and response/enum mapping |
| `maxio-platforms-team:dotnet-error-handling` | Provider exception translation and JSON failures |
| `maxio-platforms-team:dotnet-configuration-resilience` | retries, total deadlines, base URL, pagination, logging |
| `maxio-platforms-team:dotnet-testing` | adapter and endpoint tests |

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | Bind `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and optional `Maxio:BaseUrl` in PublicApi startup. Validate on start: the first three are nonblank; BaseUrl, when present, is an absolute HTTPS URI. |
| 2 | Secret sourcing & rotation | Development values are copied from the named process environment variables into PublicApi .NET user-secrets; deployments supply the same configuration keys through their secret provider. DI constructs/captures one immutable options snapshot and one SDK client, so credential rotation takes effect after process restart. Hot rotation is out of scope. |
| 3 | Total timeout budget | Every provider workflow gets a 20-second linked cancellation deadline covering lookup(s), retry backoff, and writes; the SDK per-attempt timeout is 5 seconds. Inbound client cancellation is also honored. |
| 4 | Write-retry ownership | `CreateCustomer` and `CreateSubscription` are POSTs and are not resent by the SDK. Reads may retry according to the configured safe-method policy. This scope performs no PUT. |
| 5 | Idempotency & ambiguous writes | Neither write has a real caller-supplied key; the generated fresh GUID header is not relied upon. Customer deduplication uses Maxio's documented unique deterministic customer reference, with 422 reconciliation. Subscription deduplication uses a deterministic reference, an application unique `(UserId, ProductHandle)` row acquired before the write, same-key in-process serialization (needed because EF InMemory ignores unique indexes), loser wait/reload, and `FindSubscription` reconciliation before every create and after ambiguous failure. A stale pending row is reconciled rather than blindly replayed. |
| 6 | Observability | The SDK's structured transport logs record method, masked URL, outcome, status, and duration. The endpoint boundary records the safe error classification, retained HTTP status, and ASP.NET trace identifier. Scoped provider error models expose no provider correlation-id field, so none is claimed or parsed. Raw bodies, credentials, customer data, and provider validation text are neither logged nor returned. |
| 7 | Sensitive data | `CreateCustomer` carries name/email PII. SDK `LogRequestBody` remains off and `LoggerFactory` is assigned explicitly so `MAXIOCLIENT_LOG` cannot enable body logging. Application logs use opaque hashed references and never request/error bodies. |
| 8 | Environment selection | Scope touches only `Production`: US `https://{site}.chargify.com`, EU `https://{site}.ebilling.maxio.com`, Gateway `https://{connector}.api.maxio.com/api/v1/billing`; other SDK groups are Ebb and Oauth and are untouched. This integration selects US Basic Auth because the supplied environment is US. `Maxio:BaseUrl`, when supplied, overrides the Production/US address verbatim. The SDK has no sandbox environment; test traffic isolation comes from the supplied sandbox site subdomain/override and credentials. |

## 6. Assumptions & blockers

- Minor assumption: a user may hold at most one subscription per product handle; they may subscribe to different products in parallel.
- Minor assumption: because eShop Identity stores no personal name, customer first/last names are deterministic, nonblank values derived from the username/email; the email itself remains the Identity username.
- Minor assumption: omitting a price-point selector intentionally chooses the selected product's configured default price point, avoiding all unstable numeric IDs.
- Blockers: none.
