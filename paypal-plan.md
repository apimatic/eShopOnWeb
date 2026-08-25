# PayPal .NET SDK integration plan — eShopOnWeb

SDK: `AsadAli.Checkout.Sdk` (root namespace `PayPalServerSdk`), source tag `v1.0.1`. Sandbox only,
direct-card processing (no wallet/redirect approval flow). All facts below are grounded in the
bundled SDK map (`sdk-map.md` + `map/operations/*.md` + `map/models/*.md`); a few (marked with
their source file) were resolved by opening the one named source file after the map ran out —
never from memory.

## 1. Scope & sequence

1. **Client & DI setup** — register `PayPalServerSdkClient` as a singleton via
   `AddPayPalServerSdkClient`, binding `PayPal:ClientId`, `PayPal:ClientSecret`,
   `PayPal:Environment`, `PayPal:Currency`, `PayPal:BaseUrl` from configuration.
2. **Direct card authorize (new order, raw card, no redirect)** — `Orders.CreateOrder` with
   `Intent = CheckoutPaymentIntent.Authorize` and `PaymentSource.Card` set to the raw card (single
   -step create-order call). Abort on `OrderStatus.PayerActionRequired`.
3. **Authorize with a vaulted card** — same `Orders.CreateOrder` call, but `PaymentSource.Card` is
   built with only `VaultId` set (no PAN/expiry/CVC).
4. **Capture a prior authorization at fulfilment time** — `Payments.CaptureAuthorizedPayment` by
   authorization id, read `SellerReceivableBreakdown` off the response.
5. **Detect staleness / reauthorize** — `Payments.GetAuthorizedPayment` to check
   `ExpirationTime`/`Status` before capture; `Payments.ReauthorizePayment` to renew.
6. **Void on cancellation** — `Payments.VoidPayment` by authorization id.
7. **Refund a capture** — `Payments.RefundCapturedPayment`, full or partial, with
   `PayPal-Request-Id` as the idempotency key.
8. **Vault a card / delete a vaulted card** — `Vault.CreatePaymentToken` /
   `Vault.DeletePaymentToken`.
9. **Transaction reporting** — `TransactionSearch.SearchTransactions`, paged by `page`/`pageSize`,
   loop while `page <= TotalPages`.

---

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

### 2.1 Namespaces actually needed in this integration

| Type(s) | Namespace | Map/source basis |
|---|---|---|
| `PayPalServerSdkClient`, `PayPalServerSdkClientOptions`, `ServerOptions` | `PayPalServerSdk` | root-level `.cs` files; sdk-map.md namespace table |
| `Orders`, `Payments`, `Vault`, `TransactionSearch` (controllers) | `PayPalServerSdk.Api` | sdk-map.md namespace table |
| All request/response records (`OrderRequest`, `Order`, `CardRequest`, `Money`, `Address`, `Authorization*`, `CapturedPayment`, `Refund*`, `PaymentToken*`, `SearchResponse`, `Error`, `Error1`, `DefaultError`, …) | `PayPalServerSdk.Models` | records-*.md header: "All records on these pages live in namespace `PayPalServerSdk.Models`." |
| All enums (`CheckoutPaymentIntent`, `OrderStatus`, `AuthorizationStatus`, `CardBrand`, …) | `PayPalServerSdk.Models.Enums` | enums.md header |
| Per-operation error wrappers (`CreateOrderError`, `AuthorizeOrderError`, `CaptureAuthorizedPaymentError`, `ReauthorizePaymentError`, `VoidPaymentError`, `RefundCapturedPaymentError`, `CreatePaymentTokenError`, `DeletePaymentTokenError`, `SearchBalancesError`, …) | `PayPalServerSdk.Errors` | sdk-map.md namespace table |
| `SdkException<T>` | `PayPalServerSdk.Core.Exceptions` | source: `Core/Exceptions/SdkException.cs` (opened after map gap — map only gave the file path, not the namespace) |
| `ApiError`, `RawError` | `PayPalServerSdk.Core.ErrorResponse` | source: `Core/ErrorResponse/ApiError.cs`, `RawError.cs` (same gap) |
| `RequestOptions` | `PayPalServerSdk.Core` | source: `Core/RequestOptions.cs` (same gap) |
| `ServerEnvironment`, `DefaultOptions` (+ nested `SandboxOptions`) | `PayPalServerSdk.Servers` | sdk-map.md "Servers & auth"; source: `Servers/ServerEnvironment.cs`, `Servers/DefaultOptions.cs` |
| `OAuth2ClientCredentials` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` | source: `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs` (map's *Servers & auth* section names the property but not this namespace — real gap, resolved from source) |

### 2.2 Client construction, auth, server/base-URL override — resolved facts

- `PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)` — one
  constructor. (`sdk-map.md`)
- Credentials: `PayPalServerSdkClientOptions.Oauth2 = new OAuth2ClientCredentials { ClientId =
  <PayPal:ClientId>, ClientSecret = <PayPal:ClientSecret> }` (`Scope` optional, leave null unless
  a narrower scope is required). (`sdk-map.md` "Servers & auth"; `OAuth2ClientCredentials.cs`)
- Environment: `options.Environment = ServerEnvironment.Sandbox`. **This SDK build's
  `ServerEnvironment` enum has only one member, `Sandbox` — there is no `Production`/`Live`
  member at all** (source: `Servers/ServerEnvironment.cs` — `Match<T>` throws
  `ArgumentOutOfRangeException` for anything but `Sandbox`). Bind `PayPal:Environment` from config
  but the only legal value this SDK accepts today is Sandbox — see Assumptions & Blockers.
- **BaseUrl override — supported, not just a fixed enum.** `options.Server` is a `ServerOptions`
  (`PayPalServerSdk`) wrapping `Default: DefaultOptions` (`PayPalServerSdk.Servers`), which wraps
  `Sandbox: DefaultOptions.SandboxOptions { BaseUrl: string }` — default value
  `"https://api-m.sandbox.paypal.com"`. To honor `PayPal:BaseUrl` when configured:
  `options.Server.Default.Sandbox.BaseUrl = configuredBaseUrl;` (only if the config key is
  non-empty — otherwise leave the SDK default). Source: `Servers/DefaultOptions.cs`,
  `ServerOptions.cs` (real gap — the map's "Servers & auth" section only says "Base-URL templates
  and override points live under `Servers/` and `options.Server`" without the exact path; resolved
  from source).
- DI: `services.AddPayPalServerSdkClient(o => { … })` — this extension method **registers the
  client as `AddSingleton`**, and it builds the `HttpClient` once via
  `IHttpClientFactory.CreateClient()` inside that singleton factory — the `HttpClient` is not
  rebuilt per request and is owned by the SDK's DI registration, not by caller code. Source:
  `ServiceCollectionExtensions.cs` (map gap — sdk-map.md shows the DI call shape but not its
  lifetime; resolved from source). Register once at startup; do not also register
  `PayPalServerSdkClient` yourself.
- `RetryOptions` (`PayPalServerSdk.Core.Configuration`) is `required`-heavy — either build a full
  instance or start from `RetryOptions.Default()`. Do not touch beyond defaults without loading
  `dotnet-configuration-resilience` (trap notes below).

### 2.3 Operations table

Every operation below is **Case A (typed error)** unless noted. `prefer` defaults to
`"return=minimal"` on every op that has it — **pass `prefer: "return=representation"` explicitly**
on every write op in this integration (`CreateOrder`, `AuthorizeOrder`, `CaptureOrder`,
`CaptureAuthorizedPayment`, `ReauthorizePayment`, `RefundCapturedPayment`, `VoidPayment`), or the
response body comes back with only `id`/`status`/`links` — none of the fields this integration
reads (`PurchaseUnits`, `PaymentSource`, `SellerReceivableBreakdown`, …) will be populated.
(`map/operations/Orders.md`, `map/operations/Payments.md`)

| Controller.Method | Signature (params in call order) | Request model (`Field (wire): Type, req?`) | Response / envelope fields read | Error case + accessors + payload | Notes |
|---|---|---|---|---|---|
| `client.Orders.CreateOrder` | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `OrderRequest`: `Intent (intent): CheckoutPaymentIntent, req`; `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest>, req` (each: `Amount (amount): AmountWithBreakdown, req` = `CurrencyCode (currency_code): string, req` + `Value (value): string, req`); `PaymentSource (payment_source): PaymentSource?` = `Card (card): CardRequest?` (see 2.4); `Payer (payer): Payer?`; `ApplicationContext (application_context): OrderApplicationContext?` | `Order`: `Status (status): OrderStatus?`, `Id (id): string?`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?` → `[0].Payments.Authorizations[0]` is the `AuthorizationWithAdditionalData` (`Id`, `Status`, `Amount`, `ExpirationTime`) when a `payment_source` was supplied in the same call | `SdkException<CreateOrderError>` — `TryGetError(out Error)` [400,401,422] · `TryGetRawError(out RawError)` [fallback] | `payPalRequestId` (→ header `PayPal-Request-Id`, confirmed in `Api/Orders.cs`) is **mandatory** per the SDK's own XML doc for "single-step create order calls" (i.e. whenever `payment_source` is set) — always pass it. Server retains the dedup key 6h (extendable to 72h by PayPal account manager). |
| `client.Orders.AuthorizeOrder` | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `OrderAuthorizeRequest`: `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` = `Card (card): CardRequest?` | `OrderAuthorizeResponse`: same shape as `Order` (`Status`, `PurchaseUnits[0].Payments.Authorizations[0]`) | `SdkException<AuthorizeOrderError>` — `TryGetError(out Error)` [400,401,403,404,422,500] · `TryGetRawError` [fallback] | Only needed for a **two-call** flow (order created earlier without `payment_source`). This integration's primary path is the single-call `CreateOrder` above; keep this op available for the case where an order already exists un-authorized. |
| `client.Orders.GetOrder` | `GetOrder(string id, string? fields, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — (`fields` is an optional query param, wire name `fields`) | `Order` | `SdkException<GetOrderError>` — `TryGetError(out Error)` [401,404] · `TryGetRawError` [fallback] | Use for reconciliation lookups by order id. |
| `client.Payments.GetAuthorizedPayment` | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `PaymentAuthorization`: `Status (status): AuthorizationStatus?`, `Id`, `Amount (Money)`, `ExpirationTime (expiration_time): string?`, `Links` | `SdkException<GetAuthorizedPaymentError>` — `TryGetError(out Error)` [401,403,404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` [fallback] | Call before `CaptureAuthorizedPayment` to check `ExpirationTime` vs `DateTimeOffset.UtcNow` — see trap note on `AuthorizationStatus` below. |
| `client.Payments.CaptureAuthorizedPayment` | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CaptureRequest`: `Amount (amount): Money?` (omit for full-amount capture), `FinalCapture (final_capture): bool? = false`, `InvoiceId`, `NoteToPayer`, `SoftDescriptor`, `PaymentInstruction?` | `CapturedPayment`: `Status (status): CaptureStatus?`, `Id (id): string?`, `Amount (amount): Money?`, `SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?` = `GrossAmount (gross_amount): Money, req`, `PaypalFee (paypal_fee): Money?`, `NetAmount (net_amount): Money?`, `ReceivableAmount`, `ExchangeRate`, `PlatformFees` | `SdkException<CaptureAuthorizedPaymentError>` — `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` [fallback] | `payPalRequestId` = idempotency key, server retains 45 days (`Api/Payments.cs` doc comment). `CaptureStatus` enum: `Completed, Declined, PartiallyRefunded, Pending, Refunded, Failed` — no separate "expired" state; a stale-authorization capture attempt surfaces only as an `SdkException<CaptureAuthorizedPaymentError>`, not a distinct status value. |
| `client.Payments.ReauthorizePayment` | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `ReauthorizeRequest`: `Amount (amount): Money?` (only field the request model exposes) | `PaymentAuthorization` (new `Id`/`Status`/`ExpirationTime` for the renewed authorization) | `SdkException<ReauthorizePaymentError>` — `TryGetError(out Error)` [400,401,403,404,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` [fallback] | Model's own doc summary: reauthorize only once, days 4–29 after original auth; past day 30 you must create a new authorized payment instead. No typed limit-exceeded error — see trap note. |
| `client.Payments.VoidPayment` | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | — (no body) | `PaymentAuthorization` with `Status = AuthorizationStatus.Voided` | `SdkException<VoidPaymentError>` — `TryGetError(out Error)` [401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` [fallback] | `409` is the conflict slot most likely hit on a second void — see trap note (double-void behavior is UNVERIFIED). |
| `client.Payments.RefundCapturedPayment` | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `RefundRequest`: `Amount (amount): Money?` (omit body/leave null for full refund; set for partial), `CustomId`, `InvoiceId`, `NoteToPayer`, `PaymentInstruction?` | `Refund`: `Status (status): RefundStatus?`, `Id (id): string?`, `Amount (amount): Money?` | `SdkException<RefundCapturedPaymentError>` — `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` [fallback] | `payPalRequestId` = idempotency key (45-day retention, same doc comment as capture). Two distinct partial refunds against the same capture: pass a **different** `payPalRequestId` per call (or `null`) — a repeated call with the **same** id returns the original refund instead of refunding again. Over-refund rejection has no typed issue enum — see trap note. |
| `client.Payments.GetCapturedPayment` | `GetCapturedPayment(string captureId, string? payPalMockResponse, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `CapturedPayment` | `SdkException<GetCapturedPaymentError>` — `TryGetError(out Error)` [401,403,404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` [fallback] | Reconciliation lookup by capture id. |
| `client.Payments.GetRefund` | `GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `Refund` | `SdkException<GetRefundError>` — `TryGetError(out Error)` [401,403,404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` [fallback] | — |
| `client.Vault.CreatePaymentToken` | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `PaymentTokenRequest`: `Customer (customer): Customer?` = `Id (id): string?` / `MerchantCustomerId (merchant_customer_id): string?` (set `MerchantCustomerId` to your own customer id); `PaymentSource (payment_source): PaymentTokenRequestPaymentSource, req` = `Card (card): PaymentTokenRequestCard?` = `Name`, `Number`, `Expiry`, `SecurityCode`, `Brand (CardBrand)?`, `BillingAddress (Address)?` | `PaymentTokenResponse`: `Id (id): string?` (the vault/payment-token id), `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?` = `Card (card): CardPaymentTokenEntity?` = `LastDigits (last_digits): string?`, `Brand (brand): CardBrand?`, `Expiry (expiry): string?`, `Name`, `BillingAddress` — **no PAN/CVV field exists on this response type**, safe to display/store as-is | `SdkException<CreatePaymentTokenError>` — `TryGetError1(out Error1)` [400,403,404,422,500] · `TryGetRawError` [fallback] | `payPalRequestId` idempotency key, server retains 3h (`Api/Vault.cs` doc comment). |
| `client.Vault.GetPaymentToken` | `GetPaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `PaymentTokenResponse` | `SdkException<GetPaymentTokenError>` — `TryGetError1(out Error1)` [403,404,422,500] · `TryGetRawError` [fallback] | — |
| `client.Vault.DeletePaymentToken` | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `void` (Task) | `SdkException<DeletePaymentTokenError>` — `TryGetError1(out Error1)` [400,403,500] · `TryGetRawError` [fallback] | **404 (unknown/already-deleted token) is not in the typed accessor's status list** — a 404 response fails `TryGetError1` and falls to `ApiError.TryGetRawError(out RawError)`; check `raw.StatusCode == HttpStatusCode.NotFound` there, not on `Error1`. |
| `client.Vault.ListCustomerPaymentTokens` | `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)` | query: `customer_id`←`customerId`, `page_size`←`pageSize`, `page`←`page`, `total_required`←`totalRequired` | `CustomerVaultPaymentTokensResponse`: `TotalItems`, `TotalPages`, `PaymentTokens (IReadOnlyList<PaymentTokenResponse>)?` | `SdkException<ListCustomerPaymentTokensError>` — `TryGetError1(out Error1)` [400,403,500] · `TryGetRawError` [fallback] | Pass `totalRequired: true` explicitly if you need `TotalPages` to know when to stop paging — it defaults to `false` and `TotalPages` stays null otherwise. |
| `client.TransactionSearch.SearchTransactions` | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `startDate`/`endDate` = ISO-8601 strings (wire `start_date`/`end_date`), all 8 middle params nullable-but-must-pass-explicitly (pass `null` to skip) | `SearchResponse`: `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?` → each `.TransactionInfo`: `TransactionId (transaction_id): string?`, `TransactionStatus (transaction_status): string?`, `TransactionAmount (transaction_amount): Money?`, `TransactionInitiationDate`, `PaypalReferenceId (paypal_reference_id): string?` + `PaypalReferenceIdType (paypal_reference_id_type): PayPalReferenceIdType?` (correlate back to the originating order/capture id); `Page (page): int?`, `TotalItems (total_items): int?`, `TotalPages (total_pages): int?` | **Case B** — `SdkException<RawError>`: `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | Only Case-B operation in the whole SDK — no typed `SearchTransactionsError`/`TryGet…`. Page by incrementing `page` while `page <= response.TotalPages`; `PayerActionRequired`/pagination cursor concepts don't apply here — it's plain page-number paging, no `perPage`/cursor token. |
| `client.TransactionSearch.SearchBalances` | `SearchBalances(string? asOfTime, string? currencyCode, RequestOptions? requestOptions = null, CancellationToken ct = default)` | query: `as_of_time`←`asOfTime`, `currency_code`←`currencyCode` | `BalancesResponse` | `SdkException<SearchBalancesError>` — `TryGetDefaultError(out DefaultError)` [400,403,500] · `TryGetRawError` [fallback] | Not in the 11-item scope but available if reconciliation needs merchant balance too. |

### 2.4 `CardRequest` — the raw-card / vaulted-card payment source (`PayPalServerSdk.Models`)

Used as `OrderRequest.PaymentSource.Card` (CreateOrder), `OrderAuthorizeRequest.PaymentSource.Card`
(AuthorizeOrder) — same record shape both times:

| Field (wire) | Type | Required? | Notes |
|---|---|---|---|
| `Name (name)` | `string?` | no | cardholder name |
| `Number (number)` | `string?` | set for **raw card** flow only | PAN |
| `Expiry (expiry)` | `string?` | set for **raw card** flow only | card expiry |
| `SecurityCode (security_code)` | `string?` | set for **raw card** flow only | CVC/CVV |
| `BillingAddress (billing_address)` | `Address?` | recommended | `AddressLine1/2`, `AdminArea1/2`, `PostalCode`, `CountryCode` (`string, req` on `Address`) |
| `VaultId (vault_id)` | `string?` | set for **vaulted-card** flow only | the payment-token id from `CreatePaymentToken`/`GetPaymentToken` — this is how item 4 (pay with a saved card) is expressed; do **not** also set `Number`/`Expiry`/`SecurityCode` when using `VaultId` |
| `Attributes (attributes)` | `CardAttributes?` | optional | `Attributes.Vault.StoreInVault = StoreInVaultInstruction.OnSuccess` (`PayPalServerSdk.Models.Enums`) vaults the card **at authorize time** as an alternative to a separate `CreatePaymentToken` call — only member of `StoreInVaultInstruction` is `OnSuccess` |
| `Attributes.Verification.Method` | `OrdersCardVerificationMethod?` | optional | default `ScaWhenRequired`; other members `ScaAlways`, `_3DSecure` (wire `3D_SECURE`), `AvsCvv` — governs whether a 3DS challenge can be triggered (`Models/enums.md`) |

`Address.CountryCode` is the only `required` field on `Address`; everything else on it is optional.

### 2.5 Detecting "requires shopper action" instead of building a redirect

`Order.Status` / `OrderAuthorizeResponse.Status` is `OrderStatus?`
(`PayPalServerSdk.Models.Enums.OrderStatus`) with members `Created, Saved, Approved, Voided,
Completed, PayerActionRequired`. **After `CreateOrder`/`AuthorizeOrder`, if
`response.Status == OrderStatus.PayerActionRequired`, abort the flow with a clear
"3DS/SCA challenge required, direct processing not possible" error** — do not attempt to build an
approval/redirect round-trip (out of scope per the non-negotiable constraints). This is the one
concrete, map-grounded signal; treat it as the primary check.

### 2.6 Enum quick-reference (`PayPalServerSdk.Models.Enums`, values are `Type.Member (WIRE_VALUE)`)

- `CheckoutPaymentIntent`: `Capture (CAPTURE)`, `Authorize (AUTHORIZE)`
- `OrderStatus`: `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`
- `AuthorizationStatus`: `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` — **no `Expired` member**
- `CaptureStatus`: `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)`
- `RefundStatus`: `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)`
- `CardBrand`: `Visa, Mastercard, Discover, Amex, Solo, Jcb, Star, Delta, Switch, Maestro, CbNationale, Configoga, Confidis, Electron, Cetelem, ChinaUnionPay, Diners, Elo, Hiper, Hipercard, Rupay, Ge, Synchrony, Eftpos, CarteBancaire, StarAccess, Pulse, Nyce, Accel, Unknown` (wire values are `SCREAMING_SNAKE` of the same names)
- `StoreInVaultInstruction`: `OnSuccess (ON_SUCCESS)` — only member
- `OrdersCardVerificationMethod`: `ScaAlways (SCA_ALWAYS)`, `ScaWhenRequired (SCA_WHEN_REQUIRED)`, `_3DSecure (3D_SECURE)`, `AvsCvv (AVS_CVV)`
- `ServerEnvironment` (`PayPalServerSdk.Servers`): `Sandbox` — **only member in this SDK build**

---

## 3. Trap notes

⚠ Step 1 (client & DI) — the SDK's retry/timeout options are configured on
`PayPalServerSdkClientOptions.Retry` (a `RetryOptions`), not on the `HttpClient` the DI extension
builds for you via `IHttpClientFactory` — which one actually bounds a call, and whether a
transport failure on a non-idempotent write (`CaptureAuthorizedPayment`, `RefundCapturedPayment`)
gets retried, isn't visible from the option names alone. **MUST load
`dotnet-configuration-resilience`** before tuning anything beyond the defaults.

⚠ Step 1 (auth) — `Oauth2` vs `Oauth2TokenStrategy` on `PayPalServerSdkClientOptions`: two
credential-shaped properties exist side by side, and only one wiring is correct for a static
client-id/secret from configuration. **MUST load `dotnet-authentication`** before setting either.

⚠ Step 2/3 (payment source construction) — `PaymentSource`, `OrderAuthorizeRequestPaymentSource`,
`PaymentTokenRequestPaymentSource`, etc. are plain records with ~6–15 nullable sibling fields
(`Card`, `Token`, `Paypal`, `Bancontact`, …) rather than a modeled union — setting more than one
sibling, or reading the wrong one back off a response, produces a request/response mismatch the
compiler won't catch. **MUST load `dotnet-models`** before building or reading any `*PaymentSource`
object.

⚠ Step 5 (reauthorize / expiration) — the model's XML summary states a once-only, days-4-to-29
reauthorization window, but the SDK exposes no typed status or issue code for "too old to
reauthorize" — whether that failure surfaces as a 422 vs. a 404, and what the `Error.Details[].Issue`
string actually says, is unconfirmed. **UNVERIFIED — defensive coding directive:** on
`SdkException<ReauthorizePaymentError>`, call `TryGetError(out var err)`; if that fails, fall back to
`TryGetRawError`; in both cases surface `err.Message`/`raw.ReadAsString()` verbatim as an
operator-actionable message rather than pattern-matching a specific issue string. Same directive
applies to the "over-refund" case on `RefundCapturedPayment` and the "double-void" case on
`VoidPayment` — none of these have a typed issue enum; extract best-effort, fall back to the
generic message.

⚠ Step 5/6 (capture/void error bodies) — every `Payments` operation's error case includes a
`TryGetNoContent(out RawError)` accessor for HTTP 500 specifically (empty body), **separate from**
the generic `TryGetRawError` fallback — a catch block that only tries `TryGetError` then
`TryGetRawError` and skips `TryGetNoContent` will fail to parse a 500 from these ops. **MUST load
`dotnet-error-handling`** for the full accessor-ordering pattern.

⚠ Step 9 (transaction search) — this is the SDK's **only Case B operation**
(`SdkException<RawError>`, no typed `{Operation}Error`) in an otherwise all-Case-A SDK; a catch
ladder written by copying the pattern from any other operation in this integration will not
compile against it. **MUST load `dotnet-error-handling`** to confirm the Case A/B split before
writing this one catch block.

**Both of the following apply to every error boundary in this integration — MUST load
`dotnet-error-handling` before writing it:**

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `System.Text.Json.JsonException` *while the error object is being constructed*, so the
  `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a
  boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an
  outage, and a caller that retries 5xx retries something that can never succeed.

---

## 4. REQUIRED READING (load before implementation starts — this sheet does not carry their contents)

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — constructing/registering `PayPalServerSdkClient`, `HttpClient` ownership via `AddPayPalServerSdkClient` |
| `dotnet-authentication` | Step 1 — wiring `OAuth2ClientCredentials` onto `PayPalServerSdkClientOptions.Oauth2` vs `Oauth2TokenStrategy` |
| `dotnet-calling-endpoints` | Steps 2–9 — calling `client.Orders.*`, `client.Payments.*`, `client.Vault.*`, `client.TransactionSearch.*` with named arguments (many nullable params have no default and must be passed explicitly) |
| `dotnet-models` | Steps 2–8 — building `CardRequest`/`PaymentSource`/`PaymentTokenRequest` request bodies, reading `StringEnum<T>` values, JSON wire names |
| `dotnet-error-handling` | Steps 2–9 (all) — the Case A/B split per operation, `TryGet…`/`TryGetNoContent`/`TryGetRawError` ordering, the two `JsonException` hazard rows above |
| `dotnet-configuration-resilience` | Step 1 — retry/timeout tuning, the `PayPal:BaseUrl` override, pagination semantics for `ListCustomerPaymentTokens`/`SearchTransactions` |
| `dotnet-testing` | Any step — the `HttpClient` constructor argument is the test seam for stubbing SDK calls |

---

## 5. Assumptions & Blockers

- **Blocker (flagged per the non-negotiable constraints, not worked around):** if a card requires
  a 3DS/SCA challenge, `CreateOrder`/`AuthorizeOrder` returns `Order.Status ==
  OrderStatus.PayerActionRequired`. This SDK/API path has no way to complete that challenge without
  a shopper browser redirect — the integration must abort and surface an operator-facing error at
  that point, per the "no wallet/redirect approval flow" constraint. There is no server-side
  "force no 3DS" switch beyond `Attributes.Verification.Method` on the card (`AvsCvv` avoids SCA at
  PayPal's discretion, not by contract — UNVERIFIED whether PayPal sandbox honors it as a hard
  override).
- **Blocker/assumption:** this SDK build's `ServerEnvironment` enum has only a `Sandbox` member —
  there is no `Production` value to promote to later without an SDK regeneration. Since the task is
  sandbox-only this doesn't block current work, but it means `PayPal:Environment` config binding
  can only ever validly resolve to `Sandbox` against this SDK version.
- **Assumption:** "customer id" in item 9 (vaulting) is the merchant's own identifier, bound to
  `Customer.MerchantCustomerId` — the SDK's `Customer`/`VaultResponseCustomer` records also expose
  a PayPal-assigned `Id`, but nothing in the map or source states which one the API expects on a
  first-time vault call; both are plain optional strings at the model layer. UNVERIFIED which one
  PayPal's server requires to be present.
- **Assumption:** whether voiding an already-voided authorization is a no-op or a hard error is not
  determinable from the SDK (server-side behavior, not modeled) — see the trap note in §3 for the
  defensive handling directive.
- No blockers on scope coverage: all 11 requested capabilities map onto operations that exist in
  this SDK (`Orders`, `Payments`, `Vault`, `TransactionSearch` controllers) — nothing in the
  business requirements was found to be genuinely unsupported.
