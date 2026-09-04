# PayPal .NET integration plan — eShopOnWeb

NuGet package: **`AsadAli.Checkout.Sdk`** — add version-less (`dotnet add package AsadAli.Checkout.Sdk`) so it floats to the latest release; the SDK targets `netstandard2.0`. Root namespace `PayPalServerSdk`; client `PayPalServerSdkClient`; options `PayPalServerSdkClientOptions`. Do **not** pin a version from memory.

---

## 1. Scope & sequence

### Feature 1 — Card payments on orders (authorize → capture → refund)
1. **Client registration** (once, DI): build `PayPalServerSdkClientOptions` from configuration keys `PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Environment`, `PayPal:Currency`, optional `PayPal:BaseUrl`. Register via `services.AddPayPalServerSdkClient(o => …)`.
2. **Authorize** — `client.Orders.CreateOrder` (intent `CheckoutPaymentIntent.Authorize`, one `PurchaseUnitRequest` with the exact total as `AmountWithBreakdown`, `PaymentSource.Card` = raw card) → `client.Orders.AuthorizeOrder` with `prefer: "return=representation"`. Persist: order id, **authorization id** (`PurchaseUnits[].Payments.Authorizations[0].Id`), authorization status, `ExpirationTime`.
3. **Check staleness / renew** — `client.Payments.GetAuthorizedPayment` (status + `ExpirationTime`); `client.Payments.ReauthorizePayment` (only from day 4 to day 29 of the authorization period; new 3-day honor period). Past 30 days: create a fresh authorization (step 2 again).
4. **Capture** — `client.Payments.CaptureAuthorizedPayment`. Persist: capture id, status, amount, `SellerReceivableBreakdown` (fee + net). Re-fetch/verify with `client.Payments.GetCapturedPayment`.
5. **Void** — `client.Payments.VoidPayment` (before fulfilment; cannot void after full capture).
6. **Refund** — `client.Payments.RefundCapturedPayment` (full: `body: null`; partial: `RefundRequest.Amount`), caller-supplied idempotency key via `payPalRequestId`. Persist: refund id, status.
7. **Reconcile** — `client.TransactionSearch.SearchTransactions` per date range, loop `page` 1 → `TotalPages`.

### Feature 2 — Vault saved cards
1. **Save card** — `client.Vault.CreatePaymentToken` (`POST /v3/vault/payment-tokens`): raw card + optional `Customer`. Returns `PaymentTokenResponse.Id` — the vault payment-method token. No prior payment required, no browser flow for this path. Persist: token id, customer id (`CustomerResponse.Id` if you sent `Customer.Id`, else your own `MerchantCustomerId`), last digits + brand from `PaymentTokenResponse.PaymentSource.Card` (display only).
2. **List** — `client.Vault.ListCustomerPaymentTokens(customerId, pageSize, page, totalRequired: true)`; loop pages via `TotalPages`.
3. **Delete** — `client.Vault.DeletePaymentToken(id)`.
4. **Pay with saved token** — `client.Orders.CreateOrder`/`AuthorizeOrder` with `PaymentSource.Card` carrying only `VaultId` = saved token id (+ `StoredCredential` for card-on-file flags); alternative: `PaymentSource.Token` (`Token.Id` + `TokenType.BillingAgreement`). Never store the full card; only PayPal's token.

### Cross-cutting
- Single client as DI singleton (`AddPayPalServerSdkClient` registers a singleton already). Auth is SDK-handled OAuth2 client-credentials (`/v1/oauth2/token`, Basic header) — set `Oauth2` on options before first call.
- Error boundary per CONTRACT SHEET §error rows; every operation is throw-only (no `…Result` variants exist in this SDK).
- Money: `Money { CurrencyCode (currency_code): string, Value (value): string }` — value is a **decimal string** (e.g. `"12.34"`), currency from `PayPal:Currency`.

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### 2.1 Client construction, auth, servers

| Fact | Verbatim | Source |
|---|---|---|
| Package | `AsadAli.Checkout.Sdk` (version-less install; target `netstandard2.0`) | `sdk-map.md` |
| Constructor | `new PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)` | `sdk-map.md` |
| DI | `services.AddPayPalServerSdkClient(Action<PayPalServerSdkClientOptions>? configure = null)` — registers singleton, wires `IHttpClientFactory`-created `HttpClient`, attaches `ILoggerFactory` to logging | `sdk-map.md`; source `ServiceCollectionExtensions.cs` |
| Options props | `Environment: ServerEnvironment` · `Retry: RetryOptions` · `Logging: LoggingOptions` · `Server: ServerOptions` · `Oauth2: OAuth2ClientCredentials?` · `Oauth2TokenStrategy: IOAuth2TokenStrategy<OAuth2ClientCredentials>?` | `sdk-map.md`; source `PayPalServerSdkClientOptions.cs` |
| Credentials | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials { ClientId: string (required, init), ClientSecret: string (required, init), Scope: string? (init) }` — assign to `options.Oauth2` | source `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs` |
| Environments | `PayPalServerSdk.Servers.ServerEnvironment` has **exactly one member: `ServerEnvironment.Sandbox`** (default). **There is no `Production` member in this release.** Production or any custom host is set by overriding the base URL: `options.Server.Default.Sandbox.BaseUrl = "https://api-m.paypal.com"` (production) or the `PayPal:BaseUrl` value. Sandbox default is `"https://api-m.sandbox.paypal.com"`. | source `Servers/ServerEnvironment.cs`, `Servers/DefaultOptions.cs` |
| Base URL routing | Every request URL — including the OAuth token request `POST /v1/oauth2/token` — resolves through `ServerOptions.Default.Resolve(environment, path)`, so the `BaseUrl` override applies to **every** PayPal call, token request included. | source `Server.cs`, `AuthSchemes.cs`, `Servers/DefaultOptions.cs` |
| Auth behaviour | Token request is form-encoded `grant_type=client_credentials` with Basic auth header from ClientId/ClientSecret; SDK caches and re-uses the token. If `options.Oauth2` is left null, calls go out unauthenticated → 401. | source `AuthSchemes.cs`, `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentialsStrategy.cs` |
| Retry/timeout | `RetryOptions` members: `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry`. Start from `RetryOptions.Default()` (`PayPalServerSdk.Core.Configuration`). | `sdk-map.md` |
| Per-call options | `PayPalServerSdk.Core.RequestOptions { LogLevel: Microsoft.Extensions.Logging.LogLevel? }` — **no header/bag properties**. Per-call custom headers are not available through `RequestOptions`. | source `Core/RequestOptions.cs` |
| Idempotency | Surfaced as the literal parameter **`payPalRequestId: string?`** on `CreateOrder`, `AuthorizeOrder`, `CaptureOrder`, `CaptureAuthorizedPayment`, `ReauthorizePayment`, `RefundCapturedPayment`, `VoidPayment`, `CreatePaymentToken`, `CreateSetupToken`. Pass your own key for caller-supplied idempotency (refunds especially). `RequestOptions` has no header override (row above). | `map/operations/Orders.md`, `map/operations/Payments.md`, `map/operations/Vault.md` |

### 2.2 Feature 1 operations

| Operation | Signature (params in order) | Returns | Reads from response | Error case | Source |
|---|---|---|---|---|---|
| `client.Orders.CreateOrder` | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, PayPalServerSdk.Models.OrderRequest body, string? prefer = "return=minimal", PayPalServerSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` | `PayPalServerSdk.Models.Order` | `Id`, `Status` | Case A: `SdkException<PayPalServerSdk.Errors.CreateOrderError>` — `TryGetError(out Error)` [400,401,422] · `TryGetRawError(out RawError)` | `operations/Orders.md` |
| `client.Orders.AuthorizeOrder` | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, PayPalServerSdk.Models.OrderAuthorizeRequest? body, string? prefer = "return=minimal", …, CancellationToken ct = default)` | `PayPalServerSdk.Models.OrderAuthorizeResponse` | `PurchaseUnits[].Payments.Authorizations[0]` → `.Id`, `.Status`, `.Amount`, `.ExpirationTime`; also `.Id` (order id), `.Status` | Case A: `SdkException<AuthorizeOrderError>` — `TryGetError(out Error)` [400,401,403,404,422,500] · `TryGetRawError` | `operations/Orders.md` |
| `client.Payments.GetAuthorizedPayment` | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, PayPalServerSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` | `PayPalServerSdk.Models.PaymentAuthorization` | `Status` (`AuthorizationStatus`), `ExpirationTime`, `Amount` | Case A: `SdkException<GetAuthorizedPaymentError>` — `TryGetError(out Error)` [401,403,404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | `operations/Payments.md` |
| `client.Payments.ReauthorizePayment` | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, PayPalServerSdk.Models.ReauthorizeRequest? body, string? prefer = "return=minimal", …, CancellationToken ct = default)` | `PayPalServerSdk.Models.PaymentAuthorization` | new `Id`? (same id, refreshed status/`ExpirationTime`) | Case A: `SdkException<ReauthorizePaymentError>` — `TryGetError(out Error)` [400,401,403,404,422] · `TryGetNoContent` · `TryGetRawError` | `operations/Payments.md` |
| `client.Payments.CaptureAuthorizedPayment` | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, PayPalServerSdk.Models.CaptureRequest? body, string? prefer = "return=minimal", …, CancellationToken ct = default)` | `PayPalServerSdk.Models.CapturedPayment` | `Id` (capture id), `Status`, `Amount`, `SellerReceivableBreakdown.PaypalFee` + `.NetAmount` + `.GrossAmount` | Case A: `SdkException<CaptureAuthorizedPaymentError>` — `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent` · `TryGetRawError` | `operations/Payments.md` |
| `client.Payments.GetCapturedPayment` | `GetCapturedPayment(string captureId, string? payPalMockResponse, PayPalServerSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` | `PayPalServerSdk.Models.CapturedPayment` | same fields as above (verification/re-poll) | Case A: `SdkException<GetCapturedPaymentError>` — `TryGetError(out Error)` [401,403,404] · `TryGetNoContent` · `TryGetRawError` | `operations/Payments.md` |
| `client.Payments.VoidPayment` | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", …, CancellationToken ct = default)` | `PayPalServerSdk.Models.PaymentAuthorization` | `Status` (→ `VOIDED`) | Case A: `SdkException<VoidPaymentError>` — `TryGetError(out Error)` [401,403,404,409,422] · `TryGetNoContent` · `TryGetRawError` | `operations/Payments.md` |
| `client.Payments.RefundCapturedPayment` | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, PayPalServerSdk.Models.RefundRequest? body, string? prefer = "return=minimal", …, CancellationToken ct = default)` | `PayPalServerSdk.Models.Refund` | `Id` (refund id), `Status`, `Amount`, `SellerPayableBreakdown` | Case A: `SdkException<RefundCapturedPaymentError>` — `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent` · `TryGetRawError` | `operations/Payments.md` |
| `client.TransactionSearch.SearchTransactions` | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, PayPalServerSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` — the 8 nullable params `transactionId`…`terminalId` have **no default → must pass explicitly (pass `null`)**; call with named arguments | `PayPalServerSdk.Models.SearchResponse` | `TransactionDetails[].TransactionInfo` → `TransactionId`, `TransactionStatus` (string), `TransactionAmount` (`Money`), `TransactionInitiationDate`; `Page`, `TotalItems`, `TotalPages` for pagination loop | **Case B**: `SdkException<PayPalServerSdk.Core.ErrorResponse.RawError>` — `StatusCode: HttpStatusCode`, `ReadAsString()`, `ReadAsBytes()`, `ReadAsJson<T>()` | `operations/TransactionSearch.md` |

Date format for `startDate`/`endDate` (wire `start_date`/`end_date`): ISO-8601 date-time strings, e.g. `2026-09-01T00:00:00Z` → passed as plain strings (no typed converter in the signature). Loop: `page = 1 … SearchResponse.TotalPages`, `pageSize` up to 100.

### 2.3 Feature 2 operations (Vault)

| Operation | Signature | Returns | Reads from response | Error case | Source |
|---|---|---|---|---|---|
| `client.Vault.CreatePaymentToken` | `CreatePaymentToken(string? payPalRequestId, PayPalServerSdk.Models.PaymentTokenRequest body, PayPalServerSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` | `PayPalServerSdk.Models.PaymentTokenResponse` | `Id` (**the vault payment-method token — persist**), `Customer.Id`/`Customer.MerchantCustomerId` (persist), `PaymentSource.Card.LastDigits`/`.Brand`/`.Expiry` | Case A: `SdkException<PayPalServerSdk.Errors.CreatePaymentTokenError>` — `TryGetError1(out Error1)` [400,403,404,422,500] · `TryGetRawError` | `operations/Vault.md` |
| `client.Vault.ListCustomerPaymentTokens` | `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, PayPalServerSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` — `totalRequired: true` to get `TotalPages` | `PayPalServerSdk.Models.CustomerVaultPaymentTokensResponse` | `PaymentTokens[]` (each a `PaymentTokenResponse`), `TotalItems`, `TotalPages` | Case A: `SdkException<ListCustomerPaymentTokensError>` — `TryGetError1(out Error1)` [400,403,500] · `TryGetRawError` | `operations/Vault.md` |
| `client.Vault.GetPaymentToken` | `GetPaymentToken(string id, PayPalServerSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` | `PaymentTokenResponse` | as above | Case A: `SdkException<GetPaymentTokenError>` — `TryGetError1(out Error1)` [403,404,422,500] · `TryGetRawError` | `operations/Vault.md` |
| `client.Vault.DeletePaymentToken` | `DeletePaymentToken(string id, PayPalServerSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` | `void` (Task) | — | Case A: `SdkException<DeletePaymentTokenError>` — `TryGetError1(out Error1)` [400,403,500] · `TryGetRawError` | `operations/Vault.md` |
| `client.Vault.CreateSetupToken` *(alternative; approval-driven)* | `CreateSetupToken(string? payPalRequestId, PayPalServerSdk.Models.SetupTokenRequest body, PayPalServerSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` | `PayPalServerSdk.Models.SetupTokenResponse` | `Id`, `Status` (`PaymentTokenStatus`) | Case A: `SdkException<CreateSetupTokenError>` — `TryGetError1(out Error1)` [400,403,422,500] · `TryGetRawError` | `operations/Vault.md` |

**Paying with a saved token** (authorize step): put the token in `PayPalServerSdk.Models.CardRequest.VaultId` (card sub-object of `PaymentSource` on `OrderRequest`/`OrderAuthorizeRequest`) — do **not** resend the number. Alternative: `PaymentSource.Token` = `PayPalServerSdk.Models.Token { Id (id): string !req, Type (type): TokenType !req }` with `TokenType.BillingAgreement (BILLING_AGREEMENT)`. For card-on-file semantics set `CardRequest.StoredCredential` = `PayPalServerSdk.Models.CardStoredCredential { PaymentInitiator: PaymentInitiator !req, PaymentType: StoredPaymentSourcePaymentType !req, Usage: StoredPaymentSourceUsageType? = Derived, PreviousNetworkTransactionReference: NetworkTransaction? }`.

### 2.4 Request/response model fields (wire names)

All in namespace `PayPalServerSdk.Models` unless noted. `!req` = C# `required`.

| Model | Fields |
|---|---|
| `Money` | `CurrencyCode (currency_code): string !req` · `Value (value): string !req` |
| `AmountWithBreakdown` | `CurrencyCode (currency_code): string !req` · `Value (value): string !req` · `Breakdown (breakdown): AmountBreakdown?` |
| `OrderRequest` | `Intent (intent): CheckoutPaymentIntent !req` · `Payer (payer): Payer?` · `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req` · `PaymentSource (payment_source): PaymentSource?` · `ApplicationContext (application_context): OrderApplicationContext?` |
| `PurchaseUnitRequest` | `ReferenceId (reference_id): string?` · `Amount (amount): AmountWithBreakdown !req` · `Payee`, `PaymentInstruction`, `Description`, `CustomId`, `InvoiceId`, `SoftDescriptor`, `Items`, `Shipping`, `SupplementaryData` — all `?` |
| `PaymentSource` | `Card (card): CardRequest?` · `Token (token): Token?` · `Paypal`, `Bancontact`, `Blik`, `Eps`, `Giropay`, `Ideal`, `Mybank`, `P24`, `Sofort`, `Trustly`, `ApplePay`, `GooglePay`, `Venmo` — all `?` |
| `CardRequest` | `Name (name): string?` · `Number (number): string?` · `Expiry (expiry): string?` · `SecurityCode (security_code): string?` (cvc) · `BillingAddress (billing_address): Address?` · `Attributes (attributes): CardAttributes?` · `VaultId (vault_id): string?` · `SingleUseToken (single_use_token): string?` · `StoredCredential (stored_credential): CardStoredCredential?` · `NetworkToken`, `ExperienceContext` — all `?` |
| `Address` | `AddressLine1 (address_line_1)`, `AddressLine2 (address_line_2)`, `AdminArea2 (admin_area_2)` (city), `AdminArea1 (admin_area_1)` (state), `PostalCode (postal_code)`: all `string?` · `CountryCode (country_code): string !req` |
| `Name` | `GivenName (given_name): string?` · `Surname (surname): string?` |
| `CardAttributes` | `Customer (customer): CardCustomerInformation?` · `Vault (vault): VaultInstructionBase?` · `Verification (verification): CardVerification?` |
| `VaultInstructionBase` | `StoreInVault (store_in_vault): StoreInVaultInstruction?` |
| `OrderAuthorizeRequest` | `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` (same `Card`/`Token`/… shape as `PaymentSource`) |
| `OrderAuthorizeResponse` | `Id`, `Status: OrderStatus?`, `Intent: CheckoutPaymentIntent?`, `Payer`, `PurchaseUnits: IReadOnlyList<PurchaseUnit>?`, `PaymentSource`, `Links`, `CreateTime`, `UpdateTime` |
| `PurchaseUnit` (response) | `… Payments (payments): PaymentCollection?` plus the request-side fields |
| `PaymentCollection` | `Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?` · `Captures (captures): IReadOnlyList<OrdersCapture>?` · `Refunds (refunds): IReadOnlyList<Refund>?` |
| `AuthorizationWithAdditionalData` / `PaymentAuthorization` | `Id (id): string?` · `Status (status): AuthorizationStatus?` · `StatusDetails (status_details): AuthorizationStatusDetails?` · `Amount (amount): Money?` · `ExpirationTime (expiration_time): string?` · `InvoiceId`, `CustomId`, `NetworkTransactionReference`, `SellerProtection`, `Links`, `CreateTime`, `UpdateTime`, `Payee` — all `?` |
| `CaptureRequest` | `Amount (amount): Money?` · `InvoiceId (invoice_id): string?` · `FinalCapture (final_capture): bool? = false` · `PaymentInstruction`, `NoteToPayer`, `SoftDescriptor` — all `?` |
| `CapturedPayment` | `Id (id): string?` · `Status (status): CaptureStatus?` · `StatusDetails` · `Amount (amount): Money?` · `SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?` · `FinalCapture (final_capture): bool? = false` · `ProcessorResponse`, `DisbursementMode`, `Links`, `CreateTime`, `UpdateTime`, `Payee`, `SupplementaryData` |
| `SellerReceivableBreakdown` (fees/net on capture) | `GrossAmount (gross_amount): Money !req` · `PaypalFee (paypal_fee): Money?` · `PaypalFeeInReceivableCurrency (paypal_fee_in_receivable_currency): Money?` · `NetAmount (net_amount): Money?` · `ReceivableAmount (receivable_amount): Money?` · `ExchangeRate` · `PlatformFees` |
| `RefundRequest` | `Amount (amount): Money?` (omit → full refund) · `CustomId (custom_id): string?` · `InvoiceId (invoice_id): string?` · `NoteToPayer (note_to_payer): string?` · `PaymentInstruction (payment_instruction): RefundPaymentInstruction?` |
| `Refund` | `Id (id): string?` · `Status (status): RefundStatus?` · `StatusDetails` · `Amount (amount): Money?` · `SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?` · `NoteToPayer`, `InvoiceId`, `CustomId`, `AcquirerReferenceNumber`, `Payer`, `Links`, `CreateTime`, `UpdateTime` |
| `SellerPayableBreakdown` | `GrossAmount`, `PaypalFee`, `PaypalFeeInReceivableCurrency`, `NetAmount`, `NetAmountInReceivableCurrency`, `PlatformFees`, `NetAmountBreakdown`, `TotalRefundedAmount (total_refunded_amount)` — all `Money?`/lists |
| `ReauthorizeRequest` | `Amount (amount): Money?` — **the only supported request parameter** |
| `PaymentTokenRequest` | `Customer (customer): Customer?` (optional) · `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req` |
| `Customer` (vault request) | `Id (id): string?` (PayPal-side customer id) · `MerchantCustomerId (merchant_customer_id): string?` (your id) |
| `PaymentTokenRequestPaymentSource` | `Card (card): PaymentTokenRequestCard?` · `Token (token): VaultTokenRequest?` |
| `PaymentTokenRequestCard` | `Name (name)`, `Number (number)`, `Expiry (expiry)`, `SecurityCode (security_code)`: `string?` · `Brand (brand): CardBrand?` · `BillingAddress (billing_address): Address?` |
| `PaymentTokenResponse` | `Id (id): string?` · `Customer (customer): CustomerResponse?` (`Id`, `MerchantCustomerId` — both `?`) · `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?` (`.Card: CardPaymentTokenEntity?` → `LastDigits`, `Brand`, `Expiry`, `BillingAddress`, …) · `Links` |
| `SetupTokenRequest` / `SetupTokenResponse` | request: `Customer: Customer?` · `PaymentSource: SetupTokenRequestPaymentSource !req` (`.Card: SetupTokenRequestCard?` adds `VerificationMethod (verification_method): VaultCardVerificationMethod?`); response: `Id`, `Customer`, `Status: PaymentTokenStatus? = Created`, `PaymentSource`, `Links` |
| `CustomerVaultPaymentTokensResponse` | `TotalItems (total_items): int?` · `TotalPages (total_pages): int?` · `Customer (customer): VaultResponseCustomer?` · `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?` · `Links` |
| `SearchResponse` | `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?` · `AccountNumber`, `StartDate`, `EndDate`, `LastRefreshedDatetime` · `Page (page): int?` · `TotalItems (total_items): int?` · `TotalPages (total_pages): int?` · `Links` |
| `TransactionDetails` | `TransactionInfo (transaction_info): TransactionInformation?` · `PayerInfo`, `ShippingInfo`, `CartInfo`, `StoreInfo`, `AuctionInfo`, `IncentiveInfo` |
| `TransactionInformation` | `TransactionId (transaction_id): string?` · `TransactionStatus (transaction_status): string?` · `TransactionAmount (transaction_amount): Money?` · `TransactionInitiationDate (transaction_initiation_date): string?` · `TransactionUpdatedDate`, `FeeAmount`, `PaypalReferenceId`, `PaypalReferenceIdType`, `TransactionEventCode`, `InvoiceId`, `PaymentMethodType`, `InstrumentType`, … all `?` |
| Error payloads | `Error` / `Error1`: `Name (name): string !req` · `Message (message): string !req` · `DebugId (debug_id): string !req` · `Details (details): IReadOnlyList<ErrorDetails/ErrorDetails1>?` · `Links` — `ErrorDetails(.1)`: `Field (field)`, `Value (value)`, `Location (location) = "body"`, `Issue (issue): string !req`, `Description (description)` · `DefaultError` (SearchBalances only): adds `InformationLink`, `Details: IReadOnlyList<TransactionSearchErrorDetails>?` · `RawError`: `StatusCode`, `ReadAsString()`, `ReadAsBytes()`, `ReadAsJson<T>()` |

### 2.5 Enum values needed (verbatim, `PayPalServerSdk.Models.Enums`, `StringEnum<T>` — build via static members or `Type.FromValue("wire")`)

| Enum | Members |
|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)` · `Authorize (AUTHORIZE)` |
| `OrderStatus` | `Created (CREATED)` · `Saved (SAVED)` · `Approved (APPROVED)` · `Voided (VOIDED)` · `Completed (COMPLETED)` · `PayerActionRequired (PAYER_ACTION_REQUIRED)` |
| `AuthorizationStatus` | `Created (CREATED)` · `Captured (CAPTURED)` · `Denied (DENIED)` · `PartiallyCaptured (PARTIALLY_CAPTURED)` · `Voided (VOIDED)` · `Pending (PENDING)` — stale/expired is read from `ExpirationTime` + status (`Denied`, `Voided`, past-honor-period `Created`) |
| `CaptureStatus` | `Completed (COMPLETED)` · `Declined (DECLINED)` · `PartiallyRefunded (PARTIALLY_REFUNDED)` · `Pending (PENDING)` · `Refunded (REFUNDED)` · `Failed (FAILED)` |
| `RefundStatus` | `Cancelled (CANCELLED)` · `Failed (FAILED)` · `Pending (PENDING)` · `Completed (COMPLETED)` |
| `PaymentTokenStatus` | `Created (CREATED)` · `PayerActionRequired (PAYER_ACTION_REQUIRED)` · `Approved (APPROVED)` · `Vaulted (VAULTED)` · `Tokenized (TOKENIZED)` |
| `StoreInVaultInstruction` | `OnSuccess (ON_SUCCESS)` |
| `TokenType` | `BillingAgreement (BILLING_AGREEMENT)` |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` |
| `PaymentInitiator` | `Customer (CUSTOMER)` · `Merchant (MERCHANT)` |
| `StoredPaymentSourcePaymentType` | `OneTime (ONE_TIME)` · `Recurring (RECURRING)` · `Unscheduled (UNSCHEDULED)` |
| `StoredPaymentSourceUsageType` | `First (FIRST)` · `Subsequent (SUBSEQUENT)` · `Derived (DERIVED)` |
| `CardBrand` (subset) | `Visa (VISA)` · `Mastercard (MASTERCARD)` · `Discover (DISCOVER)` · `Amex (AMEX)` · … (full list on the enums page) |

### 2.6 Error-handling boundary facts

- All 40 operations are **throw-only**; no `…Result` variants. `SdkException<TError>` (`PayPalServerSdk.Core.ErrorResponse` hierarchy; exception in `Core/Exceptions/SdkException.cs`) exposes `.Error`.
- 39 operations are Case A (typed `{Operation}Error`, per-row `TryGet…` accessors above + inherited `TryGetRawError(out RawError)`); `SearchTransactions` is **Case B** (`SdkException<RawError>`).
- `PayPal-Request-Id` idempotency errors (409 on capture/refund) surface through the typed `TryGetError(out Error)` — read `Name`/`Message`/`DebugId`/`Details[].Issue`.

> ⚠ a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
>
> ⚠ a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

---

## 3. Trap notes

- ⚠ Step 1 (client registration) — the SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; `HttpRequestException` (transport failure) is retried on **every** verb including non-idempotent `POST`, so a capture/refund can execute more than once without you asking. **MUST load `dotnet-configuration-resilience`** before wiring the client.
- ⚠ Step 1 (credentials) — set `options.Oauth2` (ClientId/Secret from `PayPal:ClientId`/`PayPal:ClientSecret`) in the DI callback before the client is constructed; a null `Oauth2` means unauthenticated calls. **MUST load `dotnet-authentication`**.
- ⚠ Steps 2–7 & all Vault steps (calls) — many optional parameters have **no C# default** (`payPalRequestId`, `body`, `fields`, …) and mis-bind in a positional call; call with named arguments and pass `null` explicitly. **MUST load `dotnet-calling-endpoints`** before the first call.
- ⚠ Request/response building — `required` members (`OrderRequest.Intent`, `PurchaseUnitRequest.Amount`, `Money.CurrencyCode`/`Value`, …) must be set in the initializer; enums are `StringEnum<T>` (not C# enums); unmodeled JSON fields are dropped on deserialize. **MUST load `dotnet-models`** before constructing any payload.
- ⚠ Error boundary — Case A vs Case B per operation (§2.2/§2.3), plus the two `JsonException` hazards above. **MUST load `dotnet-error-handling`** before writing any try/catch.
- ⚠ Tests — the `HttpClient` constructor argument is the test seam; cover the 409/duplicate-idempotency and `PAYER_ACTION_REQUIRED` paths. **MUST load `dotnet-testing`** before stubbing the SDK.

## 4. REQUIRED READING

Load **before implementation starts** — the sheet deliberately does not carry their contents:

- `dotnet-client-initialization` · Step 1 — client/DI registration, HttpClient lifetime.
- `dotnet-authentication` · Step 1 — OAuth2 client-credentials wiring, credential rotation.
- `dotnet-calling-endpoints` · Steps 2–7, all Vault steps — call shape, named arguments, envelopes.
- `dotnet-models` · all payload construction — required members, `StringEnum<T>`, wire names.
- `dotnet-error-handling` · the §2.6 boundary — Case A/B, `TryGet…`, JsonException hazards.
- `dotnet-configuration-resilience` · Step 1 — retry/timeout semantics, base-URL override, pagination.
- `dotnet-testing` · test seam for the integration layer.

## 5. Assumptions & Blockers

- **Assumption**: config `PayPal:Environment` accepts `"sandbox"` / `"production"`; the SDK has **no `Production` member** on `ServerEnvironment` (only `ServerEnvironment.Sandbox`, the default). Mapping chosen: `"sandbox"` → leave defaults; `"production"` → set `options.Server.Default.Sandbox.BaseUrl = "https://api-m.paypal.com"`. Any other value (`PayPal:BaseUrl` set) → use that value verbatim the same way. The override routes **all** calls including `/v1/oauth2/token` (verified in source: `Server.cs`, `Servers/DefaultOptions.cs`, `AuthSchemes.cs`).
- **Blocker (compliance, not code)**: sending raw card number/expiry/cvv via the API requires PCI **SAQ D** compliance (stated on `CardRequest` itself). Consequence is the app owner's to accept; if unacceptable, the alternative (hosted fields / JS SDK) is outside this SDK's surface.
- **Blocker (live-outcome, UNVERIFIED)**: whether sandbox accepts authorize-only **raw-card** payments, and vault-**with-raw-card** on `/v3/vault/payment-tokens`, without extra merchant qualification cannot be confirmed from the map or SDK source — the Sandbox note in the client doc says the Vault API is **US-only**. Defensive directive: treat decline/`PAYER_ACTION_REQUIRED` as a first-class outcome (check `Order.Status` and `AuthorizationStatus.StatusDetails`), extract best-effort, fall back to the generic message.
- **Blocker (possible browser approval, UNVERIFIED)**: some cards trigger 3DS — `OrderStatus.PayerActionRequired` exists precisely for that, and `CardExperienceContext` carries `ReturnUrl`/`CancelUrl`. If the sandbox issuer demands 3DS, authorize-only cannot complete server-side; detect via order/authorization status and route to a manual/declined path. Only live sandbox traffic can confirm which test cards do this.
- **Note (grounded)**: `SearchTransactions`/`SearchBalances` results lag up to **3 hours** (operation Notes) — reconciliation windows must tolerate that, and it is why capture/refund verification uses `GetCapturedPayment`/`GetRefund`, not transaction search.
