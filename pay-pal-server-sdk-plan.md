# PayPal Server SDK (.NET) — integration plan & contract sheet

SDK root (plugin-relative): `sdk/dotnet/` in the `paypal` plugin. All **source** cells below are relative to that root.
Scope: eShopOnWeb PublicApi — card payments (authorize → capture / void → refund), saved cards (Vault v3), reconciliation (Transaction Search).

## 1. Scope & sequence

| # | Step | Operations |
|---|---|---|
| 1 | Reference SDK project from `src/Infrastructure`; bind `PayPal:` options with fail-fast validation; register one long-lived `PayPalServerSdkClient` over a named `HttpClient` | — |
| 2 | Domain: `Order.Status`, `OrderPayment` (+ `PaymentRefund`), `SavedPaymentMethod`, `PaymentClaim` (string PK) in `CatalogContext` | — |
| 3 | Gateway (`Infrastructure/Payments/PayPal/PayPalPaymentGateway`) — single SDK boundary, one error ladder, one deadline | all below |
| 4 | Place order (`POST /api/orders`) — local only | — |
| 5 | Pay (`POST /api/orders/{id}/pay`) — create PayPal order (no payment source), then authorize it with the card / vault id | `Orders.CreateOrder`, `Orders.AuthorizeOrder`, `Orders.GetOrder` (settle) |
| 6 | Fulfil — renew stale authorization, then capture; record gross/fee/net | `Payments.GetAuthorizedPayment`, `Payments.ReauthorizePayment`, `Payments.CaptureAuthorizedPayment` |
| 7 | Cancel — void the authorization | `Payments.VoidPayment`, `Payments.GetAuthorizedPayment` (settle) |
| 8 | Refund (full/partial, caller idempotency key) | `Payments.RefundCapturedPayment` |
| 9 | Saved cards: save / list (local) / delete | `Vault.CreatePaymentToken`, `Vault.DeletePaymentToken`, `Vault.GetPaymentToken` (settle), `Vault.ListCustomerPaymentTokens` (sweep) |
| 10 | Reconciliation report over a date range (31-day windows × all pages) | `TransactionSearch.SearchTransactions` |
| 11 | Unknown-outcome sweeper (hosted service) | same settle operations as 5–9 |
| 12 | Offline tests: gateway against stub `HttpMessageHandler`; PublicApi end-to-end against an in-process fake PayPal | — |

The `…1` duplicates (`CaptureAuthorizedPayment1`, `VoidPayment1`, …) hit the identical route (`Api/Payments.cs`, `Api/Vault.cs`, `Api/TransactionSearch.cs`) — the un-suffixed operation is used everywhere.

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, verbatim — each operation that takes input takes ONE request record as its first parameter, built with an object initializer whose property names are the record's own, never flat arguments. Always pass `cancellationToken:` by name (the second parameter is `RequestOptions?`).
> ⚠ Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type: `Models/` → `PayPalServerSdk.Models`, `Models/Enums/` → `PayPalServerSdk.Models.Enums`, `Errors/` → `PayPalServerSdk.Errors`, `Requests/{Controller}/` → `PayPalServerSdk.Requests.{Controller}`, `ApiException<T>`/`SdkException` family → `PayPalServerSdk.Core.Exceptions`, `RawError` → `PayPalServerSdk.Core.ErrorResponse`.

| Controller · method | Request record (members used) | Body model (fields used, wire name) | Returns · fields read | Error case · accessors | Pagination | Source |
|---|---|---|---|---|---|---|
| `client.Orders` · `CreateOrder(CreateOrderRequest, RequestOptions?, CancellationToken)` | `Body: OrderRequest` **required**; `PayPalRequestId: string?` (1–108; "server stores keys for 6 hours"); `Prefer: string = "return=minimal"` → set `"return=representation"` | `OrderRequest`: `Intent (intent): CheckoutPaymentIntent` **req** = `Authorize`; `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest>` **req** (1–10). `PurchaseUnitRequest`: `Amount (amount): AmountWithBreakdown` **req**; `CustomId (custom_id)`, `InvoiceId (invoice_id)` (unique per merchant account), `Description (description)`. `AmountWithBreakdown`: `CurrencyCode (currency_code): string` **req**, `Value (value): string` **req**. `PaymentSource` left null (supplied at authorize). | `Order` · `Id`, `Status: OrderStatus?` | A · `CreateOrderError`: `TryGetError(out Error)` [400,401,422] · `TryGetRawError` | none | `map/operations/Orders.md#CreateOrder`; `Requests/Orders/CreateOrderRequest.cs`; `Models/OrderRequest.cs`; `Models/PurchaseUnitRequest.cs`; `Models/AmountWithBreakdown.cs`; `Models/Order.cs` |
| `client.Orders` · `AuthorizeOrder(AuthorizeOrderRequest, …)` | `Id: string` **required** (`^[A-Z0-9]+$`, ≤36); `PayPalRequestId: string?`; `Prefer` → `"return=representation"`; `Body: OrderAuthorizeRequest?` | `OrderAuthorizeRequest`: `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` → `Card (card): CardRequest?`. `CardRequest`: `Name (name)`, `Number (number)` (PAN), `Expiry (expiry)` `YYYY-MM`, `SecurityCode (security_code)`, `BillingAddress (billing_address): Address?`, `VaultId (vault_id)` — all optional. `Address`: `AddressLine1 (address_line_1)`, `AddressLine2`, `AdminArea2 (admin_area_2)` city, `AdminArea1 (admin_area_1)` state, `PostalCode (postal_code)`, `CountryCode (country_code)` **req** | `OrderAuthorizeResponse` · `Id`, `Status: OrderStatus?`, `PaymentSource.Card: CardResponse?` (`LastDigits`, `Brand: CardBrand?`, `Expiry`), `PurchaseUnits[0].Payments.Authorizations[]: AuthorizationWithAdditionalData` (`Id`, `Status: AuthorizationStatus?`, `Amount: Money?`, `ExpirationTime: string?`, `CreateTime: string?`) | A · `AuthorizeOrderError`: `TryGetError(out Error)` [400,401,403,404,422,500] · `TryGetRawError` | none | `Orders.md#AuthorizeOrder`; `Requests/Orders/AuthorizeOrderRequest.cs`; `Models/OrderAuthorizeRequest.cs`; `Models/OrderAuthorizeRequestPaymentSource.cs`; `Models/CardRequest.cs`; `Models/Address.cs`; `Models/OrderAuthorizeResponse.cs`; `Models/OrderAuthorizeResponsePaymentSource.cs`; `Models/CardResponse.cs`; `Models/PurchaseUnit.cs`; `Models/PaymentCollection.cs`; `Models/AuthorizationWithAdditionalData.cs` |
| `client.Orders` · `GetOrder(GetOrderRequest, …)` | `Id` **required** | — | `Order` · `Status`, `PaymentSource.Card`, `PurchaseUnits[0].Payments.Authorizations[]` (as above) | A · `GetOrderError`: `TryGetError(out Error)` [401,404] · `TryGetRawError` | none | `Orders.md#GetOrder`; `Requests/Orders/GetOrderRequest.cs`; `Models/PaymentSourceResponse.cs` |
| `client.Payments` · `GetAuthorizedPayment(GetAuthorizedPaymentRequest, …)` | `AuthorizationId` **required** | — | `PaymentAuthorization` · `Id`, `Status: AuthorizationStatus?`, `ExpirationTime`, `CreateTime`, `Amount` | A · `GetAuthorizedPaymentError`: `TryGetError(out Error)` [401,403,404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | none | `Payments.md#GetAuthorizedPayment`; `Requests/Payments/GetAuthorizedPaymentRequest.cs`; `Models/PaymentAuthorization.cs` |
| `client.Payments` · `ReauthorizePayment(ReauthorizePaymentRequest, …)` | `AuthorizationId` **required**; `PayPalRequestId`; `Prefer` → representation; `Body: ReauthorizeRequest?` | `ReauthorizeRequest`: `Amount (amount): Money?` (sent = original total). `Money`: `CurrencyCode (currency_code)` **req**, `Value (value)` **req** | `PaymentAuthorization` · `Id` (new authorization), `Status`, `ExpirationTime`, `CreateTime` | A · `ReauthorizePaymentError`: `TryGetError(out Error)` [400,401,403,404,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | none | `Payments.md#ReauthorizePayment`; `Requests/Payments/ReauthorizePaymentRequest.cs`; `Models/ReauthorizeRequest.cs`; `Models/Money.cs`; remarks in `Api/Payments.cs` (honor period 3 days; reauthorize days 4–29; after 30 days a new authorization is required) |
| `client.Payments` · `CaptureAuthorizedPayment(CaptureAuthorizedPaymentRequest, …)` | `AuthorizationId` **required**; `PayPalRequestId`; `Prefer` → representation; `Body: CaptureRequest?` | `CaptureRequest`: `Amount (amount): Money?`, `FinalCapture (final_capture): bool? = false` → `true`, `InvoiceId (invoice_id)` | `CapturedPayment` · `Id`, `Status: CaptureStatus?`, `Amount`, `SellerReceivableBreakdown: SellerReceivableBreakdown?` (`GrossAmount` **req**, `PaypalFee: Money?`, `NetAmount: Money?`) | A · `CaptureAuthorizedPaymentError`: `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | none | `Payments.md#CaptureAuthorizedPayment`; `Requests/Payments/CaptureAuthorizedPaymentRequest.cs`; `Models/CaptureRequest.cs`; `Models/CapturedPayment.cs`; `Models/SellerReceivableBreakdown.cs` |
| `client.Payments` · `VoidPayment(VoidPaymentRequest, …)` | `AuthorizationId` **required**; `PayPalRequestId`; `Prefer` → representation; no body | — | `PaymentAuthorization` · `Status` (may be empty under `return=minimal` → treat 2xx as voided) | A · `VoidPaymentError`: `TryGetError(out Error)` [401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | none | `Payments.md#VoidPayment`; `Requests/Payments/VoidPaymentRequest.cs` |
| `client.Payments` · `RefundCapturedPayment(RefundCapturedPaymentRequest, …)` | `CaptureId` **required**; `PayPalRequestId`; `Prefer` → representation; `Body: RefundRequest?` (remarks: empty body = full refund, `amount` = partial) | `RefundRequest`: `Amount (amount): Money?` (always sent explicitly), `CustomId (custom_id)`, `NoteToPayer (note_to_payer)` | `Refund` · `Id`, `Status: RefundStatus?`, `Amount`, `SellerPayableBreakdown?.TotalRefundedAmount` | A · `RefundCapturedPaymentError`: `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | none | `Payments.md#RefundCapturedPayment`; `Requests/Payments/RefundCapturedPaymentRequest.cs`; `Models/RefundRequest.cs`; `Models/Refund.cs`; `Models/SellerPayableBreakdown.cs` |
| `client.Vault` · `CreatePaymentToken(CreatePaymentTokenRequest, …)` | `Body: PaymentTokenRequest` **required**; `PayPalRequestId: string?` | `PaymentTokenRequest`: `PaymentSource (payment_source): PaymentTokenRequestPaymentSource` **req** → `Card (card): PaymentTokenRequestCard?` (`Name`, `Number`, `Expiry` `YYYY-MM`, `SecurityCode (security_code)`, `BillingAddress: Address?`); `Customer (customer): Customer?` → `Id (id)` = the shopper's PayPal customer id once known | `PaymentTokenResponse` · `Id` (vault id), `Customer: CustomerResponse?` (`Id`), `PaymentSource.Card: CardPaymentTokenEntity?` (`LastDigits`, `Brand: CardBrand?`, `Expiry`, `Name`) | A · `CreatePaymentTokenError`: `TryGetError(out Error)` [400,403,404,422,500] · `TryGetRawError` | none | `Vault.md#CreatePaymentToken`; `Requests/Vault/CreatePaymentTokenRequest.cs`; `Models/PaymentTokenRequest.cs`; `Models/PaymentTokenRequestPaymentSource.cs`; `Models/PaymentTokenRequestCard.cs`; `Models/Customer.cs`; `Models/PaymentTokenResponse.cs`; `Models/CustomerResponse.cs`; `Models/PaymentTokenResponsePaymentSource.cs`; `Models/CardPaymentTokenEntity.cs` |
| `client.Vault` · `DeletePaymentToken(DeletePaymentTokenRequest, …)` | `Id` **required** | — | `void` | A · `DeletePaymentTokenError`: `TryGetError(out Error)` [400,403,500] · `TryGetRawError` | none | `Vault.md#DeletePaymentToken`; `Requests/Vault/DeletePaymentTokenRequest.cs` |
| `client.Vault` · `GetPaymentToken(GetPaymentTokenRequest, …)` | `Id` **required** | — | `PaymentTokenResponse` | A · `GetPaymentTokenError`: `TryGetError(out Error)` [403,404,422,500] · `TryGetRawError` | none | `Vault.md#GetPaymentToken`; `Requests/Vault/GetPaymentTokenRequest.cs` |
| `client.Vault` · `ListCustomerPaymentTokens(ListCustomerPaymentTokensRequest, …)` | `CustomerId` **required**; `PageSize: int = 5`; `Page: int = 1`; `TotalRequired: bool = false` | — | `CustomerVaultPaymentTokensResponse` · `PaymentTokens: IReadOnlyList<PaymentTokenResponse>?`, `TotalPages: int?` | A · `ListCustomerPaymentTokensError`: `TryGetError(out Error)` [400,403,500] · `TryGetRawError` | none declared → hand-driven `Page` loop | `Vault.md#ListCustomerPaymentTokens`; `Requests/Vault/ListCustomerPaymentTokensRequest.cs`; `Models/CustomerVaultPaymentTokensResponse.cs` |
| `client.TransactionSearch` · `SearchTransactions(SearchTransactionsRequest, …)` | `StartDate: string` **required**, `EndDate: string` **required** (RFC 3339 with seconds; max range 31 days); `Fields: string = "transaction_info"`; `BalanceAffectingRecordsOnly: string = "Y"` (kept: money movements only); `PageSize: int = 100` (1–500) → 500; `Page: int = 1` | — | `SearchResponse` · `TransactionDetails[]: TransactionDetails` → `TransactionInfo: TransactionInformation?` (`TransactionId`, `PaypalReferenceId`, `TransactionEventCode`, `TransactionInitiationDate`, `TransactionAmount: Money?`, `FeeAmount: Money?`, `TransactionStatus`, `InvoiceId`, `CustomField`); `Page: int?`, `TotalPages: int?`, `TotalItems: int?` | **B** · `ApiException<RawError>` | none declared → hand-driven `Page` loop | `TransactionSearch.md#SearchTransactions`; `Requests/TransactionSearch/SearchTransactionsRequest.cs`; `Models/SearchResponse.cs`; `Models/TransactionDetails.cs`; `Models/TransactionInformation.cs` |

Error payload `Error` (`Models/Error.cs`): `Name (name)` **req**, `Message (message)` **req**, `DebugId (debug_id)` **req**, `Details (details): IReadOnlyList<ErrorDetails>?` → `Issue (issue)` **req**, `Description (description)`, `Field (field)`.

### Enums used

| Enum (`Models/Enums/…`) | Members used |
|---|---|
| `CheckoutPaymentIntent` | `Authorize` ("AUTHORIZE") |
| `OrderStatus` | `Created`, `Saved`, `Approved`, `Voided`, `Completed`, `PayerActionRequired` |
| `AuthorizationStatus` | `Created`, `Captured`, `Denied`, `PartiallyCaptured`, `Voided`, `Pending` |
| `CaptureStatus` | `Completed`, `Declined`, `PartiallyRefunded`, `Pending`, `Refunded`, `Failed` |
| `RefundStatus` | `Cancelled`, `Failed`, `Pending`, `Completed` |
| `CardBrand` | read-only (`.Value` for display) |

### Client construction / auth / server

| Fact | Value | Source |
|---|---|---|
| Constructor | `new PayPalServerSdk.PayPalServerSdkClient(HttpClient, PayPalServerSdk.PayPalServerSdkClientOptions)` | `sdk-map.md` § Getting a client |
| Credentials | `options.Oauth2 = new PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials { ClientId, ClientSecret }` | `sdk-map.md` § Servers & auth |
| Environments | only `PayPalServerSdk.Servers.ServerEnvironment.Sandbox` (default) | `Servers/ServerEnvironment.cs` |
| Base URL override | `options.Server.Default.Sandbox.BaseUrl` (default `https://api-m.sandbox.paypal.com`) | `Servers/DefaultOptions.cs` |
| Token endpoint | resolved through the same server (`server.Default("/v1/oauth2/token")`) → the BaseUrl override also moves the token request | `AuthSchemes.cs` |
| Retry / logging | `options.Retry: PayPalServerSdk.Core.Configuration.RetryOptions` (all members `required`; start from `RetryOptions.Default()`), `options.Logging: LoggingOptions` | `sdk-map.md`; `Core/Configuration/LoggingOptions.cs` |

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
|---|---|---|
| A `vault_id` sent to `AuthorizeOrder` must be a token this app stored for **this** shopper and has not deleted | `AuthorizeOrder` ← `CreatePaymentToken` (stored `SavedPaymentMethod.PayPalVaultId`) | `PaymentService.PayAsync` loads the saved method by (`id`, `buyerId`, `Active`) before any SDK call |
| The authorization captured/voided/reauthorized must be the one stored on this order's payment | `CaptureAuthorizedPayment` / `VoidPayment` / `ReauthorizePayment` ← `AuthorizeOrder` (stored `OrderPayment.AuthorizationId`) | `PaymentService.FulfilAsync` / `CancelAsync` read the id only from the order's own `OrderPayment` |
| The capture refunded must be this order's stored capture, and Σ refunds ≤ captured amount | `RefundCapturedPayment` ← `CaptureAuthorizedPayment` (stored `OrderPayment.CaptureId`, `CapturedAmount`) | `PaymentService.RefundAsync` (ownership + remaining-amount check under the per-payment refund-slot claim) |
| The PayPal order authorized must be the one created for this eShop order | `AuthorizeOrder` / `GetOrder` ← `CreateOrder` (stored `OrderPayment.PayPalOrderId`) | `PaymentService.PayAsync` / settle path |
| Deleting a token: only one this shopper owns | `DeletePaymentToken` ← `CreatePaymentToken` | `PaymentMethodService.DeleteAsync` loads by (`id`, `buyerId`) |
| `customer.id` sent on a later `CreatePaymentToken` must be the PayPal customer id returned for this shopper earlier | `CreatePaymentToken` ← `CreatePaymentToken` (stored `SavedPaymentMethod.PayPalCustomerId`) | `PaymentMethodService.SaveAsync` |

## 3. Trap notes

| Step | Hazard | Cost | Pointer |
|---|---|---|---|
| 1 | Who owns the `HttpClient`, the singleton's DNS staleness, and the per-client token cache | token round-trip per request or a client pinned to a dead IP | MUST load `paypal:dotnet-client-initialization` |
| 1 | An unset credential is sent unauthenticated without any exception | first shopper sees a 401-shaped outage | MUST load `paypal:dotnet-authentication` |
| 1 | What each "timeout" knob actually bounds, and which verbs the SDK resends | a 30 s caller budget silently exceeded; a write resent | MUST load `paypal:dotnet-configuration-resilience` |
| 1 | Unset `LoggerFactory` arms an env-var switch that can log request bodies (card PANs) | card data in logs | MUST load `paypal:dotnet-configuration-resilience` |
| 3 | Typed vs raw error per operation, accessor order, deserialization failures escaping typed catches | provider errors surface as 500s with SDK type names | MUST load `paypal:dotnet-error-handling` |
| 3 | The injected `Idempotency-Key` header looks like protection | double authorize/capture/refund | MUST load `paypal:dotnet-configuration-resilience` |
| 5–9 | Unknown outcome after a transport failure on a write | money moved while the app records "failed" | MUST load `paypal:dotnet-configuration-resilience` |
| 5,9 | Request record vs body model, `required` members, `Prefer` default | minimal responses missing authorization/capture details | MUST load `paypal:dotnet-calling-endpoints` |
| 5–10 | Open enums: comparing / printing values | wrong status branching, debug-form strings in responses | MUST load `paypal:dotnet-models` |
| 10 | A hand-driven page loop with provider-only stop conditions | unbounded calls / silently partial report | MUST load `paypal:dotnet-configuration-resilience` |
| 12 | Which seam to fake; reading request bodies after the call | flaky or network-bound tests | MUST load `paypal:dotnet-testing` |

## 4. REQUIRED READING

Loaded **before implementation starts**; this sheet deliberately does not carry their contents.

- `paypal:dotnet-client-initialization` — step 1 (client + DI)
- `paypal:dotnet-authentication` — step 1 (credentials, fail-fast)
- `paypal:dotnet-calling-endpoints` — steps 5–10 (request records, envelopes)
- `paypal:dotnet-models` — steps 5–10 (enums, nested models)
- `paypal:dotnet-error-handling` — step 3 (error boundary)
- `paypal:dotnet-configuration-resilience` — steps 1, 3, 5–11 (timeouts, retries, logging, paging, unknown outcomes)
- `paypal:dotnet-testing` — step 12

Hazard (verbatim requirement): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
|---|---|---|
| 1 | Credential fail-fast | `PayPalOptions` bound from `PayPal:`; `AddOptions().Bind().Validate(…).ValidateOnStart()` rejects blank `ClientId`, blank `ClientSecret`, blank/invalid `Currency`, an `Environment` other than `sandbox` with no `BaseUrl`, and a non-absolute `BaseUrl`. Message names the key, never the value. Host refuses to start. |
| 2 | Secret sourcing & rotation | Dev: .NET user-secrets (`PayPal:*`) on PublicApi; deployments: any `IConfiguration` source (`PayPal__ClientId` env vars / Key Vault). `PAYPAL_*` env vars are mapped into `PayPal:*` as the lowest-priority source. Options are read once when the singleton client is built → rotation takes effect on process restart (documented; no hot-rotation requirement). |
| 3 | Total timeout budget | Caller waits ≤ **25 s** of PayPal time per API request (< 30 s mandate): one scoped `PayPalRequestBudget` (a 25 s `CancellationTokenSource` per DI scope = per API request / per sweeper item), linked with the request-aborted token inside `PayPalPaymentGateway.RunAsync` and passed to every SDK call in that request; per-attempt `Retry.Timeout` = 10 s and `HttpClient.Timeout` = 10 s; GET `MaxRetries` = 2. Deadline expiry / `SdkTimeoutException` → HTTP 504 "PayPal did not respond". |
| 4 | Write-retry ownership | SDK resends only GETs (`GetOrder`, `GetAuthorizedPayment`, `GetPaymentToken`, `ListCustomerPaymentTokens`, `SearchTransactions`). All writes are POST/DELETE → never resent by the SDK (`HttpMethodsToRetry` left at default without `PUT` use in scope); the app resends a write only with the same stored `PayPal-Request-Id`. |
| 5 | Idempotency & ambiguous writes | Real key = `PayPalRequestId` (header `PayPal-Request-Id`, record member on CreateOrder / AuthorizeOrder / CaptureAuthorizedPayment / ReauthorizePayment / VoidPayment / RefundCapturedPayment / CreatePaymentToken). Value = a GUID generated once per claim and stored on the claim/payment row (never per call), so resends dedupe and restarts of the in-memory store cannot collide with old keys. `DeletePaymentToken` has no key → reconcile with `GetPaymentToken` (404 = gone). Injected `Idempotency-Key` is not relied on. |
| 6 | Observability | SDK built-in logger on host `ILoggerFactory`: request/response lines at Information, retries Warning, failures Error. App logs PayPal `debug_id`, `name`, issue on every provider error (Warning) with eShop order id. No bodies, no headers. |
| 7 | Sensitive data | Card PAN/CVV/expiry flow through `CardRequest` / `PaymentTokenRequestCard` → `LogRequestBody = false`, `LogRequestHeaders = false`, `LoggerFactory` assigned explicitly (disables `PAYPALSERVERSDKCLIENT_LOG`). App DTOs holding card data override `ToString()` and are never logged; DB stores only vault id, brand, last digits, expiry. |
| 8 | Environment selection | One server group (`Default`), one environment (`Sandbox`). `PayPal:Environment=sandbox` → SDK default URL; any other value (e.g. live) **requires** `PayPal:BaseUrl`, else startup fails — test traffic cannot drift to live and live cannot silently hit sandbox. `PayPal:BaseUrl` set → used verbatim for every call incl. token. |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS. Claims are rows in `PaymentClaims` (string primary key) in `CatalogContext`. |
| 10 | Partial results | Reconciliation: page cap per 31-day window and a range cap; response carries `complete: false` + `incompleteReason` when hit. Sweeper list loop capped (pages) and leaves rows unknown when capped. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. Settle in the write's own catch when budget remains; otherwise persist an `…Unknown` state that the next request for the same action and the `PaymentOutcomeSweeper` settle. |

### DUPLICATE CLAIMS

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
|---|---|---|---|---|
| Pay (CreateOrder + AuthorizeOrder) | `PaymentClaims` row `authorize:{orderId}:{attempt}` | primary-key violation on insert | `EfPaymentClaimStore.TryClaimAsync` (`DbUpdateException` / in-memory duplicate-key) → `PaymentService` returns 409 / existing result | `PaymentService.PayAsync` (`ClaimAsync("authorize:{orderId}:{attempt}")` → `EfPaymentClaimStore.TryClaimAsync`) → `PayPalPaymentGateway.CreateOrderAsync` / `PayPalPaymentGateway.AuthorizeOrderAsync` (via `ContinueAuthorizationAsync`) |
| Fulfil (Reauthorize + Capture) | `PaymentClaims` row `settle:{paymentId}` (shared with cancel) | primary-key violation | same | `PaymentService.FulfilAsync` (`ClaimAsync(SettleClaimKey(payment))`) → `PayPalPaymentGateway.ReauthorizeAsync` / `PayPalPaymentGateway.CaptureAsync` (via `ContinueFulfilmentAsync`) |
| Cancel (Void) | `PaymentClaims` row `settle:{paymentId}` | primary-key violation | same | `PaymentService.CancelAsync` (`ClaimAsync(SettleClaimKey(payment))`) → `PayPalPaymentGateway.VoidAsync` (via `ContinueVoidAsync`) |
| Refund | `PaymentClaims` rows `refund:{paymentId}:{idempotencyKey}` then `refund-slot:{paymentId}:{n}` | primary-key violation | same | `PaymentService.RefundAsync` (`_claims.TryClaimAsync("refund:{paymentId}:{key}")`, then `ClaimAsync("refund-slot:{paymentId}:{n}")`) → `PayPalPaymentGateway.RefundAsync` (via `SendRefundAsync`) |
| Save card | none by design: a double-submit vaults two independent tokens (no money moves; each is listed and deletable). A claim would need a card fingerprint, i.e. derived card data in the database — rejected (see §6) | — | — | `PaymentMethodService.SaveAsync` → `PayPalPaymentGateway.VaultCardAsync` (each save has its own stored `PayPalRequestId`, so a re-send of the same save never vaults twice) |
| Delete card | local soft-delete row update first (card unusable immediately); `PaymentClaims` row `delete-card:{id}` | primary-key violation | same | `PaymentMethodService.DeleteAsync` (`_claims.TryClaimAsync("delete-card:{id}")`) → `PayPalPaymentGateway.DeleteVaultedCardAsync` (via `RemoveVaultTokenAsync`) |

### PAGED READS

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
|---|---|---|---|
| `SearchTransactions` (reconciliation) | ≤ 13 windows of 31 days (≈ 1 year range; larger → 400), ≤ 20 pages × 500 per window, 25 s deadline | `ReconciliationReport.Complete = false` + `IncompleteReason` | `ReconciliationService.BuildAsync` (sets `ReconciliationReport.Complete` / `IncompleteReason`) |
| `ListCustomerPaymentTokens` (sweeper) | ≤ 10 pages | `ListVaultedCardsAsync` returns `Complete = false`; the sweeper then leaves the saved card unsettled (no deletion decided on a partial list) | `PayPalPaymentGateway.ListVaultedCardsAsync` (returns `Complete`) → `PaymentMethodService.SettleAsync` |

### UNKNOWN OUTCOMES

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
|---|---|---|---|---|
| CreateOrder | `CreateOrder` resent | same stored `PayPal-Request-Id` (body deterministic from the order) | `PaymentService.ContinueAuthorizationAsync` → `WithOneResendOnUnknownAsync` (in-request re-send); else catch → `OrderPayment.MarkOutcomeUnknown`, settled by the next pay request / `PaymentService.SettleAsync` | `PaymentServiceTests.Pay_CreateOrderConnectionFails_ResendsWithSameRequestId` |
| AuthorizeOrder | `GetOrder` | stored `PayPalOrderId` | `PaymentService.ContinueAuthorizationAsync` inner catch → `IPaymentGateway.GetOrderAsync`; else `MarkOutcomeUnknown` → next pay request / `PaymentService.SettleAsync` | `PaymentServiceTests.Pay_AuthorizeConnectionFails_SettledFromGetOrder`, `Pay_ProviderSilent_ReturnsPending_AndRepeatSettlesWithoutSecondHold` |
| ReauthorizePayment | `ReauthorizePayment` resent | stored `PayPal-Request-Id` | `PaymentService.RenewAuthorizationAsync` (`WithOneResendOnUnknownAsync`); else `ContinueFulfilmentAsync` catch → `MarkOutcomeUnknown` (status `Reauthorizing`), re-sent by the next fulfil / `SettleAsync` | `PaymentServiceTests.Fulfil_ReauthorizeUnknown_RecordedAndResent` |
| CaptureAuthorizedPayment | `CaptureAuthorizedPayment` resent | stored `PayPal-Request-Id` | `PaymentService.ContinueFulfilmentAsync` (`WithOneResendOnUnknownAsync`); else catch → `MarkOutcomeUnknown` (status `Capturing`), re-sent by the next fulfil / `SettleAsync` | `PaymentServiceTests.Fulfil_CaptureConnectionFails_ResendSettles`, `Fulfil_ProviderSilent_PendingThenSweeperSettles` |
| VoidPayment | `GetAuthorizedPayment` | stored `AuthorizationId` (status `VOIDED`) | `PaymentService.ContinueVoidAsync` catch → `IPaymentGateway.GetAuthorizationAsync`; else `MarkOutcomeUnknown` (status `Voiding`) → next cancel / `SettleAsync` | `PaymentServiceTests.Cancel_VoidConnectionFails_SettledFromGetAuthorization` |
| RefundCapturedPayment | `RefundCapturedPayment` resent | stored refund `PayPal-Request-Id` | `PaymentService.SendRefundAsync` (`WithOneResendOnUnknownAsync`); else catch → `OrderPayment.RefundOutcomeUnknown`, re-sent by a same-key retry (`ReplayRefundAsync`) / `SettleAsync` | `PaymentServiceTests.Refund_ConnectionFails_ResendWithSameKeySettles`, `Refund_ProviderSilent_RepeatWithSameKeySettlesOnce` |
| CreatePaymentToken | `CreatePaymentToken` resent (in-request only — card data is not kept) / `ListCustomerPaymentTokens` (sweeper, when the PayPal customer id is known) | stored `PayPal-Request-Id`; customer id | `PaymentMethodService.SaveAsync` inner catch → re-send; else `SavedPaymentMethod.SaveOutcomeUnknown` → `PaymentMethodService.SettleAsync` (sweeper) | `PaymentMethodServiceTests.Save_ConnectionFails_ResendsWithSameRequestId` |
| DeletePaymentToken | `GetPaymentToken` (404 = deleted) | vault id | `PaymentMethodService.RemoveVaultTokenAsync` catch → `IPaymentGateway.VaultedCardExistsAsync`; else `VaultTokenRemoved = false` → `PaymentMethodService.SettleAsync` (sweeper) | `PaymentMethodServiceTests.Delete_ConnectionFails_CardHiddenAndDeletePending` |

## 6. Assumptions & Blockers

- **UNVERIFIED** — `AuthorizeOrder` on an order created without a payment source, with `payment_source.card` (raw card or `vault_id`) in the authorize body, authorizes without buyer approval (remarks: "a valid payment_source must be provided in the request"). If PayPal answers `PAYER_ACTION_REQUIRED` (3-D Secure), the app returns 422 and the run **stops and reports** — no approval round-trip is built.
- **UNVERIFIED** — `CreatePaymentToken` accepts a raw card in `payment_source.card` (model `PaymentTokenRequestCard` carries number/expiry/CVV) without a setup-token step.
- **UNVERIFIED** — `PayPal-Request-Id` on these Payments/Vault operations deduplicates resends (record docs: "server stores keys for 6 hours").
- Residual risk (accepted): a first-ever card save whose outcome stays unknown after the in-request resend cannot be located later (PayPal generates the customer id), so a token may be orphaned at PayPal; it is never usable from eShop because no local row is active.
- Save card has no duplicate claim: a double-submit creates two independent, individually deletable tokens and moves no money; a claim would require storing a card fingerprint.
- Refunds are shopper-scoped (task: only fulfil/cancel/reconciliation are operator actions).
- Blockers: none.

## 7. Verification log (2026-10-06)

- Sandbox, end to end through PublicApi: authorize (`CREATED` hold of the exact order total) → capture at fulfil (gross, PayPal fee and net as reported by PayPal) → partial refunds → same-key replay returned the same refund → over-refund rejected locally → full refund of the remainder; card saved to the vault, reused to authorize and capture a second order; a concurrent double-click produced one authorization (second caller 409); cancel voided the hold (`VOIDED`); a deleted card disappeared and was refused for payment; reconciliation walked several report pages. No 3-D Secure challenge was returned for the sandbox test card, so the UNVERIFIED rows in §6 held on the live sandbox.
- Offline: 60 tests in `tests/IntegrationTests` (gateway boundary + flows against an in-process fake PayPal) and 9 HTTP-level tests in `tests/PublicApiIntegrationTests`.
