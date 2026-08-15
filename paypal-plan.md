# PayPal .NET SDK — Integration Contract Sheet (eShopOnWeb, .NET 8)

SDK: `AsadAli.Checkout.Sdk` (NuGet) · root namespace `PayPalServerSdk` · client `PayPalServerSdkClient` ·
map release `v1.0.1` / source commit `9653d18`. Install **version-less**:

```bash
dotnet add package AsadAli.Checkout.Sdk
```

> Map is authoritative for the SDK surface documented here; `AsadAli.Checkout.Sdk` floats to latest, so if
> any name below fails to compile, trust the compiler and report drift — do not patch from memory.

---

## 1. Scope & sequence

| # | Capability | Controller.Method | Request / Response types |
|---|---|---|---|
| 0 | Client + OAuth2 client-credentials, Sandbox, custom base URL | `new PayPalServerSdkClient` / `AddPayPalServerSdkClient` | `PayPalServerSdkClientOptions` |
| 1 | Create order (intent=AUTHORIZE, raw card in payment_source) | `client.Orders.CreateOrder` | `OrderRequest` → `Order` |
| 2 | Authorize the order (place hold) | `client.Orders.AuthorizeOrder` | `OrderAuthorizeRequest?` → `OrderAuthorizeResponse` |
| 3 | Capture an authorization | `client.Payments.CaptureAuthorizedPayment` | `CaptureRequest?` → `CapturedPayment` |
| 4 | Reauthorize a stale authorization | `client.Payments.ReauthorizePayment` | `ReauthorizeRequest?` → `PaymentAuthorization` |
| 5 | Void an authorization | `client.Payments.VoidPayment` | (no body) → `PaymentAuthorization` |
| 6 | Refund a captured payment (full/partial) | `client.Payments.RefundCapturedPayment` | `RefundRequest?` → `Refund` |
| 7 | Idempotency (PayPal-Request-Id) | `payPalRequestId` param on the calls above | n/a |
| 8 | Transaction search / reconciliation | `client.TransactionSearch.SearchTransactions` | query params → `SearchResponse` |
| 9 | Error handling + 3DS/payer-action detection | see error rows + `Order.Status` | `SdkException<…>` |
| 10 | Saved cards (vault a card, reuse to pay later) | `client.Vault.*` + `CardRequest.Attributes.Vault` / `CardRequest.VaultId` | see §2.10 |

Suggested implementation order: 0 → 1 → 2 → 3 → 6 → (4,5 as lifecycle branches) → 8 → 9 woven across all.

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

### Namespaces (add a `using` per type-kind — child namespaces are NOT imported transitively)

| Type kind / type | Namespace |
|---|---|
| Client, options, `ServerOptions` | `PayPalServerSdk` |
| Controllers (`Orders`, `Payments`, `TransactionSearch`) | `PayPalServerSdk.Api` |
| Records (all request/response models: `OrderRequest`, `Order`, `Money`, `CardRequest`, `CapturedPayment`, `Refund`, `SearchResponse`, `Error`, `DefaultError`, …) | `PayPalServerSdk.Models` |
| Enums (`CheckoutPaymentIntent`, `OrderStatus`, `AuthorizationStatus`, `CaptureStatus`, `RefundStatus`, `CardBrand`, `CardType`) | `PayPalServerSdk.Models.Enums` |
| Typed error classes (`CreateOrderError`, `CaptureAuthorizedPaymentError`, …) | `PayPalServerSdk.Errors` |
| `SdkException<T>` | `PayPalServerSdk.Core.Exceptions` |
| `RawError`, `ApiError` | `PayPalServerSdk.Core.ErrorResponse` |
| `RequestOptions` | `PayPalServerSdk.Core` |
| `RetryOptions` | `PayPalServerSdk.Core.Configuration` |
| `OAuth2ClientCredentials` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` |
| `ServerEnvironment`, `DefaultOptions` | `PayPalServerSdk.Servers` |

### 2.0 Client construction, auth, environment, base-URL override  [sdk-map.md; source `ServerEnvironment.cs`, `OAuth2ClientCredentials.cs`, `ServerOptions.cs`/`DefaultOptions.cs`, `AuthSchemes.cs`]

Constructor: `PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)`.
DI: `services.AddPayPalServerSdkClient(o => { … })` (`ServiceCollectionExtensions.cs`).

`PayPalServerSdkClientOptions` properties (source `PayPalServerSdkClientOptions.cs`):

| Property | Type |
|---|---|
| `Environment` | `ServerEnvironment` |
| `Oauth2` | `OAuth2ClientCredentials?` |
| `Oauth2TokenStrategy` | `IOAuth2TokenStrategy<OAuth2ClientCredentials>?` |
| `Server` | `ServerOptions` |
| `Retry` | `RetryOptions` |
| `Logging` | `LoggingOptions` |

- **OAuth2 client-credentials** — set `options.Oauth2 = new OAuth2ClientCredentials { ClientId = …, ClientSecret = … }`.
  `OAuth2ClientCredentials` members (source): `ClientId: string` (**required**), `ClientSecret: string` (**required**), `Scope: string?` (optional). Load `ClientId`/`ClientSecret` from configuration, not literals.
- **Sandbox** — `options.Environment = ServerEnvironment.Sandbox`. **`ServerEnvironment` has exactly ONE member: `Sandbox`** (source `ServerEnvironment.cs`; `.Default()` also returns `Sandbox`). There is **no Production/Live enum member** — see Assumptions & Blockers #1.
- **Custom base URL (verbatim string), incl. the token request** — override
  `options.Server.Default.Sandbox.BaseUrl = "https://your-custom-host";`
  (`ServerOptions.Default: DefaultOptions` → `Default.Sandbox: SandboxOptions` → `BaseUrl: string`, default `"https://api-m.sandbox.paypal.com"`, source `DefaultOptions.cs`). The OAuth token endpoint is built as `server.Default("/v1/oauth2/token")` (source `AuthSchemes.cs`), i.e. it resolves through the **same** `Server.Default` node, so overriding `BaseUrl` redirects **both** API calls and the token request to the custom host. Because only `Sandbox` exists, this `BaseUrl` field is the *only* way to reach any host other than the sandbox default.

### 2.1 CreateOrder  [operations/Orders.md; models records-1-Ac-Pa.md]

- **Signature**: `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - First 5 string params are nullable-with-no-default → **must be passed explicitly** (pass `null` to skip). `body` is required (non-null). Use named args.
- **Returns**: `Order`.
- **Request model `OrderRequest`**: `Intent (intent): CheckoutPaymentIntent` **!req** → `CheckoutPaymentIntent.Authorize` (wire `AUTHORIZE`); `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest>` **!req**; `Payer (payer): Payer?`; `PaymentSource (payment_source): PaymentSource?`; `ApplicationContext (application_context): OrderApplicationContext?`.
  - `PurchaseUnitRequest`: `Amount (amount): AmountWithBreakdown` **!req**; `ReferenceId`, `Description`, `CustomId`, `InvoiceId`, `SoftDescriptor`, `Items`, `Shipping`, `Payee` all optional.
  - `AmountWithBreakdown`: `CurrencyCode (currency_code): string` **!req**, `Value (value): string` **!req** (value as string to the cent, e.g. `"100.00"`), `Breakdown (breakdown): AmountBreakdown?`.
  - **Raw/direct card (no browser approval)** — `PaymentSource.Card (card): CardRequest?`. `CardRequest`: `Name (name): string?`, `Number (number): string?` (raw PAN), `Expiry (expiry): string?` (`YYYY-MM`), `SecurityCode (security_code): string?` (CVC), `BillingAddress (billing_address): Address?`, plus `Attributes`, `VaultId`, `ExperienceContext (experience_context): CardExperienceContext?` (`ReturnUrl`/`CancelUrl` for 3DS), etc. (all nullable). Passing PAN/CVV/expiry directly requires PCI SAQ D compliance — see trap.
  - `Address`: `AddressLine1 (address_line_1): string?`, `AddressLine2`, `AdminArea2 (admin_area_2): string?` (city), `AdminArea1 (admin_area_1): string?` (state), `PostalCode (postal_code): string?`, `CountryCode (country_code): string` **!req**.
  - `Name`: `GivenName (given_name): string?`, `Surname (surname): string?`.
- **Response `Order`**: `Id (id): string?`, `Status (status): OrderStatus?`, `Intent (intent): CheckoutPaymentIntent?`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?`, `PaymentSource (payment_source): PaymentSourceResponse?`, `Payer`, `Links (links): IReadOnlyList<LinkDescription>?`, `CreateTime`, `UpdateTime`.
- **Error**: `SdkException<CreateOrderError>` — Case A. Accessors: `TryGetError(out Error)` [400, 401, 422]; `TryGetRawError(out RawError)` [fallback].

### 2.1a Direct/unbranded card — required vs optional fields, and TRANSACTION_REFUSED  [operations/Orders.md; models records-1; enums.md]

For a direct-card AUTHORIZE the **only contract-required** fields in the whole chain are: `OrderRequest.Intent` + `.PurchaseUnits`; `PurchaseUnitRequest.Amount`; `AmountWithBreakdown.CurrencyCode` + `.Value`; `Address.CountryCode`. **Every `CardRequest` field is optional** (`Number`/`Expiry`/`SecurityCode`/`Name`/`BillingAddress` are all nullable). None of the following are required by the SDK contract:
- `card.attributes.verification` — `CardRequest.Attributes (CardAttributes) → Verification (CardVerification) → Method (OrdersCardVerificationMethod?, default `ScaWhenRequired`)`. Enum values: `ScaAlways (SCA_ALWAYS)`, `ScaWhenRequired (SCA_WHEN_REQUIRED)`, `_3DSecure (3D_SECURE)`, `AvsCvv (AVS_CVV)`. Optional.
- `card.experience_context` — `CardExperienceContext { ReturnUrl (return_url): string?, CancelUrl (cancel_url): string? }`. Optional; only used for a 3DS approval redirect.
- `card.stored_credential` — `CardStoredCredential { PaymentInitiator (payment_initiator): PaymentInitiator !req, PaymentType (payment_type): StoredPaymentSourcePaymentType !req, Usage?, PreviousNetworkTransactionReference? }`. Optional (the `!req` fields apply only *if* you include the object).
- **`processing_instruction` does not exist on `OrderRequest` in this SDK** — `OrderRequest` has only `Intent`, `Payer`, `PurchaseUnits`, `PaymentSource`, `ApplicationContext`. (Do not try to set it.)

**TRANSACTION_REFUSED is a runtime decline, not an SDK/contract defect.** It arrives as HTTP 422 → `SdkException<CreateOrderError>`/`<AuthorizeOrderError>` → `TryGetError(out Error)` with `Error.Name == "UNPROCESSABLE_ENTITY"` and `Error.Details[i].Issue == "TRANSACTION_REFUSED"`. The map/source document status codes only — **not** issue-string semantics — and a well-formed request (all required fields present) that returns TRANSACTION_REFUSED has passed schema validation and been *refused downstream* (processor/risk/account), which is outside the SDK's contract surface. Adding `verification`/`experience_context`/`stored_credential` will not cure it. Diagnose by logging `Error.Name`, every `Error.Details[].Issue`/`.Description`, and `Error.DebugId`, and checking the account/processor side — not the request shape.

### 2.2 AuthorizeOrder  [operations/Orders.md; models records-1-Ac-Pa.md]

- **Signature**: `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `id` = the order id from 2.1. 5 params (`payPalMockResponse`…`body`) must be passed explicitly (`null` to skip). `body` may be `null` when the card was already supplied at create time.
- **Returns**: `OrderAuthorizeResponse`.
- **Request `OrderAuthorizeRequest`**: `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` (only field). `OrderAuthorizeRequestPaymentSource.Card (card): CardRequest?` (same `CardRequest` as 2.1), plus `Token`, `Paypal`, `ApplePay`, `GooglePay`, `Venmo`.
- **Read authorization id + status** (response envelope is nested):
  `OrderAuthorizeResponse.PurchaseUnits` (`IReadOnlyList<PurchaseUnit>?`) → `PurchaseUnit.Payments (payments): PaymentCollection?` → `PaymentCollection.Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?` → each `AuthorizationWithAdditionalData`: `Id (id): string?` (**authorization id**), `Status (status): AuthorizationStatus?`, `Amount (amount): Money?`, `ExpirationTime (expiration_time): string?`, `ProcessorResponse`.
- **Error**: `SdkException<AuthorizeOrderError>` — Case A. `TryGetError(out Error)` [400, 401, 403, 404, 422, 500]; `TryGetRawError(out RawError)` [fallback].

### 2.3 CaptureAuthorizedPayment  [operations/Payments.md; models records-1/2]

- **Signature**: `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 4 params (`payPalMockResponse`…`body`) explicit; `body` may be `null` for full capture.
- **Returns**: `CapturedPayment`.
- **Request `CaptureRequest`**: `Amount (amount): Money?` (partial capture), `InvoiceId`, `FinalCapture (final_capture): bool? = false`, `NoteToPayer`, `SoftDescriptor`, `PaymentInstruction`. `Money`: `CurrencyCode (currency_code): string` **!req**, `Value (value): string` **!req**.
- **Read amounts** — `CapturedPayment`: `Id (id): string?`, `Status (status): CaptureStatus?`, `Amount (amount): Money?`, `SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?`.
  - `SellerReceivableBreakdown`: `GrossAmount (gross_amount): Money` **!req** (captured amount), `PaypalFee (paypal_fee): Money?` (PayPal fee), `NetAmount (net_amount): Money?` (net proceeds), `PaypalFeeInReceivableCurrency`, `ReceivableAmount`, `ExchangeRate`, `PlatformFees`. Read each `Money` as `.CurrencyCode` + `.Value`.
- **Error**: `SdkException<CaptureAuthorizedPaymentError>` — Case A. `TryGetError(out Error)` [400, 401, 403, 404, 409, 422]; `TryGetNoContent(out RawError)` [500]; `TryGetRawError(out RawError)` [fallback].

### 2.4 ReauthorizePayment  [operations/Payments.md; models records-2]

- **Signature**: `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `payPalRequestId`, `payPalAuthAssertion`, `body` must be passed explicitly.
- **Returns**: `PaymentAuthorization`.
- **Request `ReauthorizeRequest`**: `Amount (amount): Money?` (only field — the API supports only `amount`).
- **Read** `PaymentAuthorization`: `Id (id): string?`, `Status (status): AuthorizationStatus?`, `Amount (amount): Money?`, `ExpirationTime (expiration_time): string?`, `CreateTime`, `UpdateTime`.
- **Error**: `SdkException<ReauthorizePaymentError>` — Case A. `TryGetError(out Error)` [400, 401, 403, 404, 422]; `TryGetNoContent(out RawError)` [500]; `TryGetRawError(out RawError)` [fallback].

### 2.5 VoidPayment  [operations/Payments.md; models records-2]

- **Signature**: `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `payPalMockResponse`, `payPalAuthAssertion`, `payPalRequestId` explicit. **No request body.**
- **Returns**: `PaymentAuthorization` (read `Status` → `AuthorizationStatus.Voided`). Note: you cannot void a fully-captured authorization (surfaces as 409/422 error).
- **Error**: `SdkException<VoidPaymentError>` — Case A. `TryGetError(out Error)` [401, 403, 404, 409, 422]; `TryGetNoContent(out RawError)` [500]; `TryGetRawError(out RawError)` [fallback].

### 2.6 RefundCapturedPayment  [operations/Payments.md; models records-2]

- **Signature**: `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 4 params (`payPalMockResponse`…`body`) explicit.
- **Full vs partial refund**: **full** = pass `body: null` (empty body); **partial** = pass a `RefundRequest` with `Amount (amount): Money?` set.
- **Idempotency key**: pass `payPalRequestId:` (the PayPal-Request-Id header) — a direct method parameter, no manual header wiring.
- **Request `RefundRequest`**: `Amount (amount): Money?`, `CustomId`, `InvoiceId`, `NoteToPayer`, `PaymentInstruction`.
- **Returns `Refund`**: `Id (id): string?` (**refund id**), `Status (status): RefundStatus?`, `Amount (amount): Money?`, `SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?`, `CreateTime`, `UpdateTime`.
- **Error**: `SdkException<RefundCapturedPaymentError>` — Case A. `TryGetError(out Error)` [400, 401, 403, 404, 409, 422]; `TryGetNoContent(out RawError)` [500]; `TryGetRawError(out RawError)` [fallback].

### 2.7 Idempotency (PayPal-Request-Id)  [operations/Orders.md, Payments.md]

The idempotency header is a **method parameter named `payPalRequestId`** on: `CreateOrder`,
`AuthorizeOrder`, `CaptureOrder`, `CaptureAuthorizedPayment`, `ReauthorizePayment`,
`RefundCapturedPayment`, `VoidPayment`. Pass your merchant-supplied key as `payPalRequestId: myKey`.
There is no separate header collection to populate for this. (Note: `GetOrder`, `GetAuthorizedPayment`
and the read ops do **not** have this parameter — expected, they are idempotent GETs.)

### 2.8 SearchTransactions  [operations/TransactionSearch.md; models records-2]

- **Signature**: `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `startDate`/`endDate` required (wire `start_date`/`end_date`; ISO-8601 strings). The 8 middle nullable params (`transactionId`…`terminalId`) must be passed explicitly (`null` to skip) — **call with named args** or they mis-bind.
  - `fields` defaults to `"transaction_info"` (leave as-is to populate `TransactionInfo`); `pageSize` (wire `page_size`) default 100; `page` default 1.
- **Returns `SearchResponse`**: `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?`, `Page (page): int?`, `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`, `StartDate`, `EndDate`, `LastRefreshedDatetime`, `AccountNumber`, `Links`.
  - **Pagination**: request param `page` (1-based) + response `TotalPages`. There is **no `page_size` in the response and no next-cursor** — iterate `for (page = 1; page <= resp.TotalPages; page++)`, re-calling with the same `pageSize`, until `page > TotalPages`. (Map marks pagination "none — only `page`, no `perPage`"; the SDK does not auto-page — you loop manually.)
  - `TransactionDetails.TransactionInfo (transaction_info): TransactionInformation?` → per-transaction fields: `TransactionId (transaction_id): string?`, `TransactionStatus (transaction_status): string?` (**plain string, not an enum**), `TransactionAmount (transaction_amount): Money?`, `FeeAmount (fee_amount): Money?`, `TransactionInitiationDate (transaction_initiation_date): string?`, `TransactionUpdatedDate (transaction_updated_date): string?`, `PaypalReferenceId`, `EndingBalance`, `AvailableBalance`, `InvoiceId`, `CustomField`, plus many optional amount fields.
- **Error**: `SdkException<RawError>` — **Case B (raw, the only Case-B op in the SDK)**. No `TryGetError`; read `ex.Error.StatusCode`, `ex.Error.ReadAsString()`, `ex.Error.ReadAsJson<T>()`. (`SearchBalances` — the sibling op — is Case A with `TryGetDefaultError(out DefaultError)`; do not copy Case-A handling onto `SearchTransactions`.)

### 2.9 Error reading + 3DS / payer-action detection

- **Exception type**: every op throws `SdkException<TError>` (`PayPalServerSdk.Core.Exceptions`). There is **no `ApiException`** name and **no no-throw `…Result` variant** anywhere in this SDK.
- **Case A (39 of 40 ops here)**: `catch (SdkException<{Op}Error> ex)` → `ex.Error` is the typed error. Read via the op's accessors (`TryGetError(out Error)`, some ops also `TryGetNoContent(out RawError)`), else `ex.Error.TryGetRawError(out RawError raw)`.
- **Case B (only `SearchTransactions`)**: `catch (SdkException<RawError> ex)` → `ex.Error.StatusCode` / `.ReadAsString()`.
- **HTTP status**: on Case A statuses come from *which* `TryGet…` matched (accessor→status list above) or `raw.StatusCode` on the fallback; on Case B directly from `ex.Error.StatusCode`.
- **Error body model `Error`** (payload of `TryGetError`): `Name (name): string` **!req**, `Message (message): string` **!req**, `DebugId (debug_id): string` **!req**, `Details (details): IReadOnlyList<ErrorDetails>?`, `Links`.
  - `ErrorDetails`: `Field (field): string?`, `Value (value): string?`, `Location (location): string? = "body"`, `Issue (issue): string` **!req**, `Description (description): string?`.
  - `DefaultError` (payload of `SearchBalances.TryGetDefaultError`): same shape but `Details: IReadOnlyList<TransactionSearchErrorDetails>?`.
- **3DS / payer-action-required detection (STOP signal)** — the contract signal is on the create/authorize response, not an exception:
  - `Order.Status` / `OrderAuthorizeResponse.Status` is `OrderStatus?`; the enum member `OrderStatus.PayerActionRequired` (wire `PAYER_ACTION_REQUIRED`) means the order needs buyer/browser approval (3DS challenge) and was **not** authorized directly → **STOP** and surface the approval `Links` (rel `payer-action`) to the caller rather than proceeding to authorize/capture.
  - Card 3DS auth outcome (when present) is under `PaymentSourceResponse.Card` → `CardResponse.AuthenticationResult` (`AuthenticationResponse`): `LiabilityShift (liability_shift): LiabilityShiftIndicator?` (`No`/`Possible`/`Unknown`), `ThreeDSecure (three_d_secure): ThreeDSecureAuthenticationResponse?`.
  - **UNVERIFIED (live-wire only):** exactly which of `Status == PAYER_ACTION_REQUIRED`, a `Links` entry with `rel == "payer-action"`, or `LiabilityShift`/`ThreeDSecure` the live API populates for a given card cannot be confirmed from map or source. **Directive:** treat the order as "needs approval / STOP" if `Status == OrderStatus.PayerActionRequired` **OR** any `Links` entry has `rel == "payer-action"`; only treat it as directly authorized when `Status` reached `APPROVED`/authorization exists AND no payer-action link is present. Extract best-effort; if neither the status nor links can be read, fall back to STOP (do not silently proceed to capture).

### 2.10 Saved cards — VAULT contract  [operations/Vault.md; models records-1/2; enums.md]

**Yes — the SDK exposes a dedicated Vault controller: `client.Vault` (source `Api/Vault.cs`, 6 operations).** Tokenization is fully in the SDK surface. Namespaces are the same as elsewhere (controller in `PayPalServerSdk.Api`; all models in `PayPalServerSdk.Models`; enums in `PayPalServerSdk.Models.Enums`).

**Controller signatures** (parameter names literal; `ct` is the token; note `body` is **required/non-null** on the create ops):

| Op | Signature | Returns | Error (Case A) |
|---|---|---|---|
| CreatePaymentToken | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `PaymentTokenResponse` | `SdkException<CreatePaymentTokenError>` — `TryGetError1(out Error1)` [400,403,404,422,500]; `TryGetRawError(out RawError)` |
| CreateSetupToken | `CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `SetupTokenResponse` | `SdkException<CreateSetupTokenError>` — `TryGetError1(out Error1)` [400,403,422,500]; `TryGetRawError` |
| GetPaymentToken | `GetPaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `PaymentTokenResponse` | `SdkException<GetPaymentTokenError>` — `TryGetError1(out Error1)` [403,404,422,500]; `TryGetRawError` |
| GetSetupToken | `GetSetupToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `SetupTokenResponse` | `SdkException<GetSetupTokenError>` — `TryGetError1(out Error1)` [403,404,422,500]; `TryGetRawError` |
| DeletePaymentToken | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `void` (Task) | `SdkException<DeletePaymentTokenError>` — `TryGetError1(out Error1)` [400,403,500]; `TryGetRawError` |
| ListCustomerPaymentTokens | `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CustomerVaultPaymentTokensResponse` | `SdkException<ListCustomerPaymentTokensError>` — `TryGetError1(out Error1)` [400,403,500]; `TryGetRawError` |

Note the Vault error payload is **`Error1`** (not `Error`): `Name (name): string` !req, `Message (message): string` !req, `DebugId (debug_id): string` !req, `Details (details): IReadOnlyList<ErrorDetails1>?`, `Links`. There is **no `…Result` no-throw variant** (all throw). `ListCustomerPaymentTokens` returns `TotalItems`/`TotalPages` — page with `page`/`pageSize` the same manual way as §2.8; set `totalRequired: true` to get `TotalItems`/`TotalPages` populated.

**(1) Store a card directly → reusable token — `CreatePaymentToken`:**
- `PaymentTokenRequest`: `Customer (customer): Customer?`, `PaymentSource (payment_source): PaymentTokenRequestPaymentSource` **!req**.
  - `Customer`: `Id (id): string?`, `MerchantCustomerId (merchant_customer_id): string?` (use to tie the token to your eShop user).
  - `PaymentTokenRequestPaymentSource`: `Card (card): PaymentTokenRequestCard?`, `Token (token): VaultTokenRequest?`.
  - **Raw card** → `PaymentTokenRequestCard`: `Name (name): string?`, `Number (number): string?` (PAN), `Expiry (expiry): string?` (`YYYY-MM`), `SecurityCode (security_code): string?` (CVC), `Brand (brand): CardBrand?`, `BillingAddress (billing_address): Address?`.
- **Response `PaymentTokenResponse`**: `Id (id): string?` (**the reusable payment-token id**), `Customer (customer): CustomerResponse?`, `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?`, `Links`.
  - **Safe card description read-back**: `PaymentTokenResponsePaymentSource.Card (card): CardPaymentTokenEntity?` → `Brand (brand): CardBrand?`, `LastDigits (last_digits): string?` (last4), `Expiry (expiry): string?`, `Name (name): string?`, `BillingAddress (billing_address): CardResponseAddress?`, `VerificationStatus`, `Type (type): CardType?`. No PAN/CVC is returned.

**(1b) Two-step setup-token → payment-token (optional, e.g. when 3DS/verification is needed first):**
- `CreateSetupToken` with `SetupTokenRequest{ PaymentSource = SetupTokenRequestPaymentSource{ Card = SetupTokenRequestCard{ Number, Expiry, SecurityCode, Name, BillingAddress, VerificationMethod? } } }` → `SetupTokenResponse.Id` (+ `Status: PaymentTokenStatus?`, default `Created`).
- Then `CreatePaymentToken` with `PaymentTokenRequestPaymentSource.Token = VaultTokenRequest{ Id = <setupTokenId>, Type = VaultTokenRequestType.SetupToken }` → `PaymentTokenResponse.Id`.
  - `VaultTokenRequest`: `Id (id): string` **!req**, `Type (type): VaultTokenRequestType` **!req**. `VaultTokenRequestType` has ONLY `SetupToken (SETUP_TOKEN)`.

**(2) Vault-on-purchase via `CardRequest.Attributes` (vault during an order, no separate Vault call):**
- `CardRequest.Attributes (attributes): CardAttributes?`.
  - `CardAttributes`: `Customer (customer): CardCustomerInformation?`, `Vault (vault): VaultInstructionBase?`, `Verification (verification): CardVerification?`.
  - `VaultInstructionBase.StoreInVault (store_in_vault): StoreInVaultInstruction?`. **`StoreInVaultInstruction` has exactly ONE member: `OnSuccess (ON_SUCCESS)`** — so the only settable value is `StoreInVaultInstruction.OnSuccess`. (Set `card.attributes.vault.store_in_vault = ON_SUCCESS` when creating/authorizing the order.)
- **Read the resulting vault id + card description off the ORDER response** (create → `Order.PaymentSource`; authorize → `OrderAuthorizeResponse.PaymentSource`; both are `CardResponse` under `.Card`):
  - Vault id: `CardResponse.Attributes (attributes): CardAttributesResponse?` → `CardAttributesResponse.Vault (vault): CardVaultResponse?` → `CardVaultResponse.Id (id): string?` (**the vault id**), `Status (status): VaultStatus?`, `Customer (customer): CardCustomerInformation?`.
  - Card brand/last4/expiry: read directly off `CardResponse`: `Brand (brand): CardBrand?`, `LastDigits (last_digits): string?`, `Expiry (expiry): string?`, `Name (name): string?`.

**(3) Charge a saved card LATER — the canonical mechanism is `payment_source.card.vault_id`:**
- On `CreateOrder`, set `PaymentSource.Card = new CardRequest { VaultId = "<payment-token id from PaymentTokenResponse.Id or CardVaultResponse.Id>" }` — **with NO `Number`/`SecurityCode`/`Expiry`**. `CardRequest.VaultId (vault_id): string?` is the field.
- **`PaymentSource.Token` is NOT the vaulted-card path in this SDK.** `PaymentSource.Token` is of type `Token` (`Id (id): string` !req, `Type (type): TokenType` !req), and **`TokenType` has exactly ONE member: `BillingAgreement (BILLING_AGREEMENT)`** — there is **no `PAYMENT_METHOD_TOKEN` value**. So `PaymentSource.Token` cannot represent a vaulted card; use `card.vault_id`. (The coordinator's hypothesized `Token.Type = PAYMENT_METHOD_TOKEN` does not exist in this SDK.)
- **UNVERIFIED (live-wire only):** that the live API accepts a v3 payment-token id (`PaymentTokenResponse.Id`) in `card.vault_id` on a v2 order cannot be confirmed from map/source — the model surface permits exactly this field and no other card-token path. **Directive:** build on `card.vault_id`; on the first live call, if the order is rejected referencing the vault id, surface the provider `Error`/`Error1` message best-effort rather than assuming an outage, and verify the id type against the sandbox before rollout.

**(4) Canonical recommendation for THIS SDK.** To (a) store a card + get a reusable token and safe description, and (b) charge it later:
- **Store**: prefer **`client.Vault.CreatePaymentToken`** with a raw `PaymentTokenRequestCard` when vaulting is a standalone "save card" action → `PaymentTokenResponse.Id` + `CardPaymentTokenEntity` (brand/last4/expiry). If you are already creating a paying order, **vault-on-purchase** via `CardRequest.Attributes.Vault.StoreInVault = OnSuccess` and read `CardVaultResponse.Id` back is the lower-friction path (one API round trip instead of two). Both yield the same kind of reusable token id.
- **Charge later**: **`client.Orders.CreateOrder`** with `payment_source.card.vault_id = <token>` (no PAN), then `AuthorizeOrder`/`CaptureAuthorizedPayment` exactly as §2.2–2.3. Use `Vault.GetPaymentToken` / `ListCustomerPaymentTokens` to show saved cards, `Vault.DeletePaymentToken` to remove one.
- **Error types**: all six Vault ops throw `SdkException<{Op}Error>` with `TryGetError1(out Error1)` + `TryGetRawError(out RawError)`; the later charge throws the Orders/Payments error types already in §2.1–2.3.

### Enum value tables (only those in scope)  [models/enums.md]

- `CheckoutPaymentIntent` (StringEnum): `Capture (CAPTURE)`, `Authorize (AUTHORIZE)`.
- `OrderStatus`: `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`.
- `AuthorizationStatus`: `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)`.
- `CaptureStatus`: `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)`.
- `RefundStatus`: `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)`.
- `LiabilityShiftIndicator`: `No (NO)`, `Possible (POSSIBLE)`, `Unknown (UNKNOWN)`.
- `CardType`: `Credit (CREDIT)`, `Debit (DEBIT)`, `Prepaid (PREPAID)`, `Store (STORE)`, `Unknown (UNKNOWN)`.
- `CardBrand`: `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Amex (AMEX)`, `Discover (DISCOVER)`, `Jcb (JCB)`, `Diners (DINERS)`, `Elo (ELO)`, `Maestro (MAESTRO)`, … `Unknown (UNKNOWN)` (30 members total).
- `StoreInVaultInstruction`: `OnSuccess (ON_SUCCESS)` — **only member**.
- `TokenType` (for `PaymentSource.Token`): `BillingAgreement (BILLING_AGREEMENT)` — **only member** (no `PAYMENT_METHOD_TOKEN`).
- `VaultTokenRequestType`: `SetupToken (SETUP_TOKEN)` — **only member**.
- `VaultStatus`: `Vaulted (VAULTED)`, `Created (CREATED)`, `Approved (APPROVED)`.
- `PaymentTokenStatus`: `Created (CREATED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`, `Approved (APPROVED)`, `Vaulted (VAULTED)`, `Tokenized (TOKENIZED)`.
- Enums are `StringEnum<T>`, **not** C# enums — use the static member (`CheckoutPaymentIntent.Authorize`) or `CheckoutPaymentIntent.FromValue("AUTHORIZE")`; never `.AUTHORIZE`.

---

## 3. Trap notes (each bites at the step named; load the skill before writing that step)

⚠ Step 0 (client registration) — the `HttpClient`/handler pipeline the client wraps must be long-lived and shared (via `IHttpClientFactory`), not rebuilt per request; the SDK client wrapper's own lifetime is a separate question. **MUST load `dotnet-client-initialization`** before writing `new PayPalServerSdkClient(...)` / `AddPayPalServerSdkClient`.

⚠ Step 0 (auth) — *when* credentials must be set relative to client construction, and how the token is acquired/refreshed against the (possibly overridden) base URL, are not shown by the property types. **MUST load `dotnet-authentication`** before wiring `Oauth2` / `Oauth2TokenStrategy`.

⚠ Step 0 (resilience / base URL / retries) — the SDK's `Retry.Timeout` does **not** bound a whole call and is not the `HttpClient` timeout you register; and `HttpMethodsToRetry` gates only the *status-code* retry trigger, so whether a failed non-idempotent write (create/authorize/capture/refund POST) can be silently re-sent on a transport failure is exactly the thing the option names hide — decisive here because these POSTs move money. This is *why* the `payPalRequestId` idempotency key (2.7) matters on every write. **MUST load `dotnet-configuration-resilience`** before tuning retries/timeouts/base URL.

⚠ Steps 1–2 (building request models) — enums are `StringEnum<T>` not C# enums; unions are built by factory + read by `TryGet…`; and **fields you don't model are dropped on (de)serialize** — the trap that determines whether a response field you skipped is silently lost. Raw-card fields (`Number`/`SecurityCode`/`Expiry`) put you in PCI SAQ D scope — confirm that is intended before sending a PAN. **MUST load `dotnet-models`** before constructing payloads.

⚠ Steps 1–8 (calling) — list/search ops have optional params with **no C# default** that mis-bind positionally; call every op (especially `SearchTransactions`) with **named arguments**, and remember `ct:` is the token name. **MUST load `dotnet-calling-endpoints`** before the first call.

⚠ Step 9 (error boundary) — see REQUIRED READING; the JsonException rows below are load-bearing and must shape the boundary from the start.

---

## 4. REQUIRED READING — load BEFORE implementation starts (this sheet deliberately omits their contents)

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 0 — client construction, HttpClient lifetime, DI |
| `dotnet-authentication` | Step 0 — OAuth2 client-credentials wiring, token acquisition/refresh |
| `dotnet-configuration-resilience` | Step 0 — retries, timeout semantics, base-URL override, manual pagination |
| `dotnet-models` | Steps 1–2 — building request models, StringEnum, unions, dropped fields |
| `dotnet-calling-endpoints` | Steps 1–8 — named args, required vs optional params, response envelopes |
| `dotnet-error-handling` | Step 9 — Case A/B mechanics, safe status/body reading, the traps below |
| `dotnet-testing` | Tests — the `HttpClient` constructor arg is the test seam |

**Two mandatory `System.Text.Json.JsonException` hazards for the error boundary (write these into the boundary from the start):**

- A drifted or malformed **2xx** body (e.g. a missing `required` member) surfaces as a
  `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only
  catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces**
  the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every
  `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that
  retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

1. **No Production/Live environment enum.** `ServerEnvironment` exposes only `Sandbox` (source `ServerEnvironment.cs`). To target PayPal Live (or any non-sandbox host) you must set `options.Server.Default.Sandbox.BaseUrl` to the live/custom host string explicitly — there is no `ServerEnvironment.Production`. Confirm the intended target host; if Live is required, the base-URL override (2.0) is the mechanism, and it also redirects the OAuth token request (verified in `AuthSchemes.cs`).
2. **Raw card / PCI scope assumed intentional.** Capabilities 2–3 send raw PAN/CVC via `CardRequest`, which the SDK's own model doc flags as requiring PCI SAQ D compliance. Assumed the merchant accepts that scope (vs hosted fields / vault). Flag if not.
3. **3DS field population is UNVERIFIED** (live-wire only) — see 2.9. Planned as a defensive STOP based on `OrderStatus.PayerActionRequired` OR a `payer-action` link, falling back to STOP when neither is readable.
4. **Plan-file path defaulted.** The brief did not dictate a path, so this file was written to the repo-root default `<repo>/paypal-plan.md`.
6. **Vaulting IS in the SDK** (§2.10) — dedicated `client.Vault` controller (6 ops) plus vault-on-purchase via `CardRequest.Attributes.Vault`. Two open items are UNVERIFIED (live-wire only): (a) that a v3 `PaymentTokenResponse.Id` is accepted in a v2 order's `card.vault_id`; both are planned defensively (build on `card.vault_id`; verify in sandbox before rollout). Note the constraint that surfaced from source enums: charging a vaulted **card** must use `card.vault_id` — `PaymentSource.Token` cannot (its `TokenType` only allows `BILLING_AGREEMENT`), and `StoreInVaultInstruction` only allows `ON_SUCCESS`.
7. Everything else in scope resolved from the map (release `v1.0.1` / commit `9653d18`) or SDK source; no open contract rows remain.
