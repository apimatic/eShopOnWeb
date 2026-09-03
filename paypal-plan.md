# PayPal .NET SDK integration — contract plan (eShopOnWeb, `src/PublicApi`)

SDK: `PayPalServerSdk` (NuGet `AsadAli.Checkout.Sdk`, APIMatic-generated). Map provenance: tag `v1.0.1`, source commit `9653d18`. Install version-less: `dotnet add package AsadAli.Checkout.Sdk`.

This plan is the contract sheet. Load the `dotnet-*` companion skills named in **REQUIRED READING** before writing code — the trap notes point to them and deliberately do not resolve them.

---

## 1. Scope & sequence

| # | Capability | Operations (in call order) |
|---|---|---|
| 0 | Client + DI + auth + sandbox base-URL | `AddPayPalServerSdkClient` / `new PayPalServerSdkClient(HttpClient, options)` |
| 1 | Authorize (hold), one-off card | `client.Orders.CreateOrder` (intent=AUTHORIZE) → `client.Orders.AuthorizeOrder` (card in body) |
| 1b| Authorize (hold), vaulted card | same, but `payment_source.card.vault_id` = saved token |
| 2 | Capture at fulfilment + read fee/net | `client.Payments.CaptureAuthorizedPayment` → read `seller_receivable_breakdown` |
| 3 | Stale-auth handling | `client.Payments.ReauthorizePayment`; detect via `PaymentAuthorization.Status` + `.ExpirationTime` + capture/reauth error |
| 4 | Void / cancel hold | `client.Payments.VoidPayment` |
| 5 | Refund (full/partial, idempotent) | `client.Payments.RefundCapturedPayment` (PayPal-Request-Id) |
| 6 | Vault card standalone + reuse | `client.Vault.CreatePaymentToken` (raw card) → reuse token via `card.vault_id` at step 1b |
| 7 | Reconciliation (page whole range) | `client.TransactionSearch.SearchTransactions` (loop `page` 1..`total_pages`) |

**Direct-card, no browser round-trip.** Primary authorize flow (step 1): `CreateOrder` with `intent=AUTHORIZE` and purchase-unit amount/`invoice_id`/`custom_id`, **no** payment source → returns `Order` (status `CREATED`); then `AuthorizeOrder(orderId, …, body: OrderAuthorizeRequest{ PaymentSource.Card })`. The `AuthorizeOrder` map note confirms a valid `payment_source` in the authorize request removes the need for buyer approval (`operations/Orders.md`). Providing the card at `CreateOrder` instead and calling `AuthorizeOrder(id, …, body: null)` is an equivalent variant.

**3DS / browser-approval detection → STOP (see §2 "3DS detection" block).**

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

### Namespaces (add a `using` per kind — child namespaces are NOT imported transitively)

| Types | Namespace |
|---|---|
| `PayPalServerSdkClient`, `PayPalServerSdkClientOptions`, `ServerOptions` | `PayPalServerSdk` |
| `ServerEnvironment`, `DefaultOptions`, `DefaultOptions.SandboxOptions` | `PayPalServerSdk.Servers` |
| `OAuth2ClientCredentials` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` |
| `IOAuth2TokenStrategy<T>` | `PayPalServerSdk.Core.Authentication.OAuth2` |
| `RetryOptions` | `PayPalServerSdk.Core.Configuration` |
| Controllers (`Orders`, `Payments`, `Vault`, `TransactionSearch`) | `PayPalServerSdk.Api` |
| All request/response records (`OrderRequest`, `CardRequest`, `Money`, …) | `PayPalServerSdk.Models` |
| All enums (`OrderStatus`, `CardBrand`, `CheckoutPaymentIntent`, …) | `PayPalServerSdk.Models.Enums` |
| Typed error classes (`AuthorizeOrderError`, `RefundCapturedPaymentError`, …) | `PayPalServerSdk.Errors` |
| `SdkException<T>` | `PayPalServerSdk.Core.Exceptions` |
| `RawError`, `ApiError` | `PayPalServerSdk.Core.ErrorResponse` |

### Operations

| Op (controller) | Signature (params in order — all pre-`prefer` nullables have NO default → pass explicitly, `null` to skip) | Request body model | Response — inner fields read | Error case + accessors | Source |
|---|---|---|---|---|---|
| `Orders.CreateOrder` | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` | `OrderRequest` (req: `Intent`, `PurchaseUnits`) | `Order`: `Id`, `Status` (OrderStatus), `PurchaseUnits[]` | A: `SdkException<CreateOrderError>` · `TryGetError(out Error)` [400,401,422] · `TryGetRawError(out RawError)` | operations/Orders.md; records-1 |
| `Orders.AuthorizeOrder` | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` | `OrderAuthorizeRequest` (`PaymentSource`) | `OrderAuthorizeResponse`: `Id`, `Status`, **`PurchaseUnits[].Payments.Authorizations[].Id`** (= authorization_id), `.Status`, `.ExpirationTime`, `.Links[]` | A: `SdkException<AuthorizeOrderError>` · `TryGetError(out Error)` [400,401,403,404,422,500] · `TryGetRawError` | operations/Orders.md; records-1 |
| `Payments.CaptureAuthorizedPayment` | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` | `CaptureRequest` (all optional; `Amount`, `FinalCapture`, `InvoiceId`) | `CapturedPayment`: `Id` (=capture_id), `Status` (CaptureStatus), **`SellerReceivableBreakdown.GrossAmount` / `.PaypalFee` / `.NetAmount`** (each `Money`) | A: `SdkException<CaptureAuthorizedPaymentError>` · `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | operations/Payments.md; records-1/2 |
| `Payments.ReauthorizePayment` | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` | `ReauthorizeRequest` (`Amount` only) | `PaymentAuthorization`: `Id`, `Status`, `ExpirationTime`, `Amount` | A: `SdkException<ReauthorizePaymentError>` · `TryGetError(out Error)` [400,401,403,404,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | operations/Payments.md; records-2 |
| `Payments.VoidPayment` | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` — **note `payPalRequestId` is the 4th param here** | none | `PaymentAuthorization`: `Status` (→ `Voided`) | A: `SdkException<VoidPaymentError>` · `TryGetError(out Error)` [401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | operations/Payments.md |
| `Payments.RefundCapturedPayment` | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` | `RefundRequest` (all optional; `Amount` for partial, omit/null for full; `InvoiceId`, `CustomId`, `NoteToPayer`) | `Refund`: `Id`, `Status` (RefundStatus), `Amount`, **`SellerPayableBreakdown.TotalRefundedAmount` / `.NetAmount` / `.GrossAmount` / `.PaypalFee`** | A: `SdkException<RefundCapturedPaymentError>` · `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | operations/Payments.md; records-2 |
| `Payments.GetCapturedPayment` | `GetCapturedPayment(string captureId, string? payPalMockResponse, RequestOptions? requestOptions=null, CancellationToken ct=default)` | none | `CapturedPayment`: `Amount` (captured gross), `Status`, `SellerReceivableBreakdown` | A: `SdkException<GetCapturedPaymentError>` · `TryGetError(out Error)` [401,403,404] · `TryGetNoContent` [500] · `TryGetRawError` | operations/Payments.md |
| `Payments.GetAuthorizedPayment` | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions=null, CancellationToken ct=default)` | none | `PaymentAuthorization`: `Status`, `ExpirationTime` (poll staleness before capture) | A: `SdkException<GetAuthorizedPaymentError>` · `TryGetError(out Error)` [401,403,404] · `TryGetNoContent` [500] · `TryGetRawError` | operations/Payments.md |
| `Vault.CreatePaymentToken` | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions=null, CancellationToken ct=default)` | `PaymentTokenRequest` (req: `PaymentSource`) | `PaymentTokenResponse`: **`Id` (=vault token)**, `PaymentSource.Card` (`CardPaymentTokenEntity`: `Brand`, `LastDigits`, `Expiry`) | A: `SdkException<CreatePaymentTokenError>` · `TryGetError1(out Error1)` [400,403,404,422,500] · `TryGetRawError` | operations/Vault.md; records-2 |
| `Vault.CreateSetupToken` | `CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions? requestOptions=null, CancellationToken ct=default)` | `SetupTokenRequest` (req: `PaymentSource`) | `SetupTokenResponse`: `Id`, `Status` (PaymentTokenStatus) | A: `SdkException<CreateSetupTokenError>` · `TryGetError1(out Error1)` [400,403,422,500] · `TryGetRawError` | operations/Vault.md; records-2 |
| `Vault.GetPaymentToken` | `GetPaymentToken(string id, RequestOptions? requestOptions=null, CancellationToken ct=default)` | none | `PaymentTokenResponse` (re-read descriptor) | A: `TryGetError1(out Error1)` [403,404,422,500] · `TryGetRawError` | operations/Vault.md |
| `Vault.DeletePaymentToken` | `DeletePaymentToken(string id, RequestOptions? requestOptions=null, CancellationToken ct=default)` | none | `void` | A: `TryGetError1(out Error1)` [400,403,500] · `TryGetRawError` | operations/Vault.md |
| `TransactionSearch.SearchTransactions` | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields="transaction_info", string? balanceAffectingRecordsOnly="Y", int? pageSize=100, int? page=1, RequestOptions? requestOptions=null, CancellationToken ct=default)` — **8 nullables `transactionId`..`terminalId` have no default → pass explicitly; call with named args** | none (query params) | `SearchResponse`: `TransactionDetails[]`, `Page`, `TotalItems`, **`TotalPages`**, `Links[]` | **B: `SdkException<RawError>`** (the ONLY Case-B op) · `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | operations/TransactionSearch.md; records-2 |

### Request models — fields to construct (`CSharpName (wire_name): Type`, `!req` = C# `required`)

- **`OrderRequest`** — `Intent (intent): CheckoutPaymentIntent !req` (=`Authorize`), `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req`, `PaymentSource (payment_source): PaymentSource?`, `Payer?`, `ApplicationContext?`.
- **`PurchaseUnitRequest`** — `Amount (amount): AmountWithBreakdown !req`, `ReferenceId?`, `InvoiceId (invoice_id): string?` ← **reconciliation key**, `CustomId (custom_id): string?` ← secondary correlation, `Description?`, `Payee?`.
- **`AmountWithBreakdown`** — `CurrencyCode (currency_code): string !req` (from config), `Value (value): string !req` (order total, exact cents as decimal string), `Breakdown?`. (`Money` = `{ CurrencyCode !req, Value !req }`, both strings.)
- **`OrderAuthorizeRequest`** — `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?`.
- **`OrderAuthorizeRequestPaymentSource`** — `Card (card): CardRequest?`, `Token?`, `Paypal?`, `ApplePay?`, `GooglePay?`, `Venmo?`.
- **`CardRequest`** (one-off card AND vaulted reuse) — `Name?`, `Number (number): string?`, `Expiry (expiry): string?` (`YYYY-MM`), `SecurityCode (security_code): string?`, `BillingAddress (billing_address): Address?`, `VaultId (vault_id): string?` ← **set this (and leave Number/Expiry null) to pay with a saved token**, `Attributes (attributes): CardAttributes?` (holds `Verification` + `Vault` instruction), `ExperienceContext (experience_context): CardExperienceContext?` (`ReturnUrl`/`CancelUrl` — supplying these opts into a 3DS redirect; see 3DS block), `StoredCredential (stored_credential): CardStoredCredential?`.
- **`Address`** — `CountryCode (country_code): string !req`; `AddressLine1/2?`, `AdminArea1/2?`, `PostalCode?`. (Sandbox Visa billing address.)
- **`CaptureRequest`** — nothing `!req`; `Amount (amount): Money?` (omit for full capture of the authorized amount), `FinalCapture (final_capture): bool? = false`, `InvoiceId?`, `NoteToPayer?`, `SoftDescriptor?`.
- **`ReauthorizeRequest`** — `Amount (amount): Money?` only (per op note: supports only `amount`).
- **`RefundRequest`** — nothing `!req`; `Amount (amount): Money?` (present = partial, omit/null = full), `InvoiceId?`, `CustomId?`, `NoteToPayer?`, `PaymentInstruction?`.
- **`PaymentTokenRequest`** (standalone vault of a raw card) — `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req`, `Customer (customer): Customer?` (`Customer` = `{ Id?, MerchantCustomerId? }`).
- **`PaymentTokenRequestPaymentSource`** — `Card (card): PaymentTokenRequestCard?`, `Token (token): VaultTokenRequest?` (use `Token` when converting a setup token).
- **`PaymentTokenRequestCard`** — `Name?`, `Number (number): string?`, `Expiry (expiry): string?`, `SecurityCode (security_code): string?`, `Brand (brand): CardBrand?`, `BillingAddress (billing_address): Address?`. (No verification/experience-context here — this is the no-3DS direct-vault path.)
- **`SetupTokenRequest` / `SetupTokenRequestPaymentSource` / `SetupTokenRequestCard`** (only if you need card verification/3DS before vaulting) — `SetupTokenRequestCard` adds `VerificationMethod (verification_method): VaultCardVerificationMethod?` and `ExperienceContext (experience_context): VaultCardExperienceContext?`. Convert to a permanent token with `CreatePaymentToken` + `PaymentTokenRequestPaymentSource.Token = VaultTokenRequest{ Id = setupTokenId, Type = VaultTokenRequestType.SetupToken }`.

### Response read-paths (nested — the id/values are one or two levels down)

- **Authorization id** (after `AuthorizeOrder`): `resp.PurchaseUnits[0].Payments.Authorizations[0].Id`; status `.Status` (`AuthorizationStatus`); expiry `.Authorizations[0].ExpirationTime` (ISO-8601 string). (`OrderAuthorizeResponse.PurchaseUnits` → `PurchaseUnit.Payments` (`PaymentCollection`) → `.Authorizations` (`IReadOnlyList<AuthorizationWithAdditionalData>`).)
- **Capture fee/net** (after `CaptureAuthorizedPayment`): `cap.SellerReceivableBreakdown.GrossAmount.Value`, `.PaypalFee?.Value`, `.NetAmount?.Value` (+ each `.CurrencyCode`). Only `GrossAmount` is `!req`; `PaypalFee`/`NetAmount` are nullable and, per the `SellerReceivableBreakdown` summary, absent while the capture is `PENDING` — guard for null.
- **Refund breakdown**: `refund.SellerPayableBreakdown.TotalRefundedAmount?.Value` (cumulative refunded), `.GrossAmount?`, `.NetAmount?`, `.PaypalFee?`.
- **Vault descriptor** (after `CreatePaymentToken`): token = `tok.Id`; safe descriptor = `tok.PaymentSource.Card.Brand` (`CardBrand`), `.LastDigits`, `.Expiry`.
- **Reconciliation match**: iterate `SearchResponse.TransactionDetails[].TransactionInfo` (`TransactionInformation`): `.InvoiceId (invoice_id)` ← matches the `invoice_id` you stamped, `.CustomField (custom_field)`, `.TransactionId`, `.PaypalReferenceId` (+ `.PaypalReferenceIdType`), `.TransactionAmount`, `.FeeAmount`, `.TransactionStatus`.

### 3DS / browser-approval detection → STOP

The SDK surfaces a required buyer approval (incl. a 3DS challenge on a direct card) as an **order status of `PAYER_ACTION_REQUIRED`** plus a HATEOAS action link — there is no separate exception for it:

- After `CreateOrder`/`AuthorizeOrder`, check `resp.Status == OrderStatus.PayerActionRequired` (`OrderStatus` enum member `PayerActionRequired`, wire `PAYER_ACTION_REQUIRED`). If so, a redirect/challenge is required — **do not proceed; surface and stop.**
- Corroborate via `resp.Links` (`IReadOnlyList<LinkDescription>`; each `LinkDescription` = `Href !req`, `Rel !req`, `Method?`): a link whose `Rel` is the payer-action/approve relation carries the challenge URL. `LinkDescription.Rel` is a free `string` in the map — the exact literal (e.g. `"payer-action"`) is not enumerated in the SDK, so match case-insensitively and defensively. `UNVERIFIED` — only live traffic confirms the literal `rel` value.
- 3DS authentication outcome, when present, is under the card response: `OrderAuthorizeResponse.PaymentSource.Card` (`CardResponse`) → `AuthenticationResult (authentication_result): AuthenticationResponse` → `LiabilityShift (LiabilityShiftIndicator)` and `ThreeDSecure (ThreeDSecureAuthenticationResponse: AuthenticationStatus (ParesStatus), EnrollmentStatus)`. Treat `EnrollmentStatus`/`AuthenticationStatus` indicating a required challenge, or a non-`CREATED`/non-successful authorization, as a stop condition. `UNVERIFIED` — which combination the sandbox actually returns for the test Visa can only be confirmed against live traffic; extract best-effort and fall back to "approval required — stopped" on any non-happy-path shape. Source: records-1 (`CardResponse`, `AuthenticationResponse`), enums.md (`OrderStatus`, `LiabilityShiftIndicator`).

### Stale-authorization handling (step 3)

- Reauthorize window (from `ReauthorizePayment` op note): initial 3-day honor period; reauthorize days 4–29; after 30 days you must create a **new** authorized payment, not reauthorize. Amount may be raised up to ~115% (US) / +$75 cap — geography-dependent per the note.
- Detect staleness **before** acting: `PaymentAuthorization.Status` (`AuthorizationStatus`: `Created`/`Captured`/`Denied`/`PartiallyCaptured`/`Voided`/`Pending`) via `GetAuthorizedPayment`, plus `.ExpirationTime` (ISO-8601). An authorization not in `Created` (e.g. `Voided`, `Captured`, `Denied`) or past `ExpirationTime` cannot be captured/reauthorized.
- Detect "no longer reauthorizable" from the throw: `ReauthorizePayment` → `SdkException<ReauthorizePaymentError>` (`TryGetError(out Error)`), `CaptureAuthorizedPayment` → `SdkException<CaptureAuthorizedPaymentError>`. The operator-facing reason lives in `Error.Details[].Issue` (`ErrorDetails.Issue: string !req`) + `Error.Message`/`Error.DebugId`. The literal issue codes (e.g. an expired/too-late authorization) are NOT enumerated in the SDK — `UNVERIFIED`: read `Details[].Issue` best-effort for the operator message and fall back to `Message`/`DebugId`. Source: operations/Payments.md; records-1 (`Error`, `ErrorDetails`).

### Refund over-refund guard (step 5)

`Refund.SellerPayableBreakdown.TotalRefundedAmount` reports cumulative refunded; the captured gross is `CapturedPayment.Amount` (from capture response or `GetCapturedPayment`); `CaptureStatus` moves to `PartiallyRefunded` then `Refunded`. There is no single "net remaining" field on `CapturedPayment` in the map — computing remaining = captured gross − cumulative refunded, and refusing a refund that exceeds it, is an application decision (persist per-capture refunded total). `YOUR CALL — not in the map` for the enforcement; the fields above are the inputs.

### Idempotency (PayPal-Request-Id)

Pass the caller-supplied idempotency key as the `payPalRequestId` parameter (SDK sends header `PayPal-Request-Id`): `CreateOrder` (2nd param), `AuthorizeOrder` (3rd), `CaptureAuthorizedPayment` (3rd), `RefundCapturedPayment` (3rd), `ReauthorizePayment` (2nd), `VoidPayment` (**4th**), `CreatePaymentToken`/`CreateSetupToken` (1st). Source: operations pages.

### Reconciliation paging + reference stamping (step 7)

- Stamp the eShop order reference at authorize time via `PurchaseUnitRequest.InvoiceId (invoice_id)` (reliable correlation) and optionally `CustomId (custom_id)`. Both are echoed on `Authorization`/`CapturedPayment` (`InvoiceId`, `CustomId`).
- Page the whole range: call `SearchTransactions(startDate, endDate, …, page: n)` for `n = 1..resp.TotalPages`, accumulating `resp.TransactionDetails`. Do not stop at page 1. `page`/`pageSize` default 1/100.
- Match each `TransactionInformation.InvoiceId` back to the stamped `invoice_id`. Whether `custom_id` re-surfaces as `TransactionInformation.CustomField` is `UNVERIFIED` (only live traffic confirms) — rely on `invoice_id` as primary key, treat `CustomField` as best-effort.
- Dates: `startDate`/`endDate` are `string` (ISO-8601 date-time). The exact required format and the per-request date-range cap are not in the map — see Assumptions.

### Enum value lists (namespace `PayPalServerSdk.Models.Enums`; write `Enum.Member`, wire value in parens)

| Enum | Members (`Member (WIRE)`) |
|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` |
| `AuthorizationIncompleteReason` | `PendingReview (PENDING_REVIEW)`, `DeclinedByRiskFraudFilters (DECLINED_BY_RISK_FRAUD_FILTERS)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `CardBrand` | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Amex (AMEX)`, `Discover (DISCOVER)`, … (30 members) |
| `PaymentTokenStatus` | `Created (CREATED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`, `Approved (APPROVED)`, `Vaulted (VAULTED)`, `Tokenized (TOKENIZED)` |
| `VaultStatus` | `Vaulted (VAULTED)`, `Created (CREATED)`, `Approved (APPROVED)` |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` |
| `LiabilityShiftIndicator` | `No (NO)`, `Possible (POSSIBLE)`, `Unknown (UNKNOWN)` |

Source for all: enums.md.

### Client construction / auth / sandbox / base-URL override (source-confirmed from SDK)

- Construct: `new PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)`; or DI `services.AddPayPalServerSdkClient(o => { … })`. Source: `PayPalServerSdkClient.cs`, `ServiceCollectionExtensions.cs` (sdk-map.md).
- `PayPalServerSdkClientOptions` members: `Environment (ServerEnvironment)`, `Retry (RetryOptions)`, `Logging (LoggingOptions)`, `Server (ServerOptions)`, `Oauth2 (OAuth2ClientCredentials?)`, `Oauth2TokenStrategy (IOAuth2TokenStrategy<OAuth2ClientCredentials>?)`.
- **Auth = OAuth2 client credentials.** Set `options.Oauth2 = new OAuth2ClientCredentials { ClientId = <cfg>, ClientSecret = <cfg>, Scope = <optional> }`. `ClientId` and `ClientSecret` are `required`. The SDK obtains the bearer token itself by Basic-auth POST to `/v1/oauth2/token`. Source: SDK source `OAuth2ClientCredentials.cs`, `AuthSchemes.cs`.
- **Sandbox environment.** `options.Environment = ServerEnvironment.Sandbox` (also the default via `ServerEnvironment.Default()`). **Sandbox is the ONLY environment** the SDK defines — there is no Live/Production member. Source: SDK source `Servers/ServerEnvironment.cs`. (Production ⇒ override the base URL, next bullet — see Blockers.)
- **Explicit BaseUrl override, used verbatim for ALL calls incl. the token request.** Set `options.Server.Default.Sandbox.BaseUrl = "<url>"`. `ServerOptions.Default` is a `DefaultOptions` (ns `PayPalServerSdk.Servers`) whose `Sandbox` is a `DefaultOptions.SandboxOptions { string BaseUrl }` (default `https://api-m.sandbox.paypal.com`). The OAuth token URL is resolved through the **same** `DefaultOptions.Resolve` (`server.Default("/v1/oauth2/token")` in `AuthSchemes.cs`), so overriding `Sandbox.BaseUrl` applies to the credential/token request too. Source: SDK source `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Server.cs`, `AuthSchemes.cs`.
- **HttpClient ownership / lifetime** — `MUST load dotnet-client-initialization` (do not new-up an `HttpClient` per call; use `IHttpClientFactory`, keep the handler pipeline long-lived). Config default binding keys (`ClientId`/`ClientSecret`/`CurrencyCode`/`BaseUrl`) are the application's to define — `YOUR CALL — not in the map`.

---

## 3. Trap notes

- ⚠ Step 0 (client registration) — `HttpClient`/handler pipeline must be long-lived and factory-managed, not rebuilt per request; the SDK client wrapper may be transient. **MUST load `dotnet-client-initialization`.**
- ⚠ Step 0 (auth) — set credentials before the client is built / in the DI callback, and load `ClientId`/`ClientSecret` from configuration, never hardcoded; token acquisition and 401 behaviour are the skill's. **MUST load `dotnet-authentication`.**
- ⚠ Step 0 (base URL / retries / timeouts) — the SDK `RetryOptions.Timeout` is **per-attempt**, not a whole-call budget, and is **not** the `HttpClient` timeout; `HttpMethodsToRetry` gates only the status-code trigger while a transport `HttpRequestException` is retried on **every** verb (POST included) — i.e. a non-idempotent write can execute more than once. This is exactly why PayPal-Request-Id idempotency (step 5) matters. **MUST load `dotnet-configuration-resilience`.**
- ⚠ Steps 1–7 (calls) — call `SearchTransactions` and other multi-optional ops with **named arguments**; the pre-`prefer` nullable params have no C# default and mis-bind positionally. **MUST load `dotnet-calling-endpoints`.**
- ⚠ Steps 1–7 (models) — enums are `StringEnum<T>` (build via `Enum.Member` or `Type.FromValue("WIRE")`, not C# enum cast); `Money.Value` is a **string** — format the order total to the currency's minor units exactly; unmodeled JSON is dropped on deserialize. **MUST load `dotnet-models`.**
- ⚠ Steps 1–7 (error boundary) — see REQUIRED READING; `SearchTransactions` is the lone Case-B op (`SdkException<RawError>`, no typed accessors) while every other op is Case A with `TryGetError`/`TryGetError1`; `TryGetRawError` is a fallback, not a catch-all substitute. **MUST load `dotnet-error-handling`.**
- ⚠ Tests — the `HttpClient` constructor argument is the test seam. **MUST load `dotnet-testing`.**

---

## 4. REQUIRED READING (load BEFORE implementation starts — this sheet does not carry their contents)

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 0 — client construction, HttpClient lifetime, DI registration |
| `dotnet-authentication` | Step 0 — OAuth2 client-credentials wiring, secret loading, 401 handling |
| `dotnet-configuration-resilience` | Step 0 — retries/timeouts semantics, base-URL/server, pagination |
| `dotnet-calling-endpoints` | Steps 1–7 — named-argument calls, request/response envelopes, cancellation |
| `dotnet-models` | Steps 1–7 — request models, `StringEnum<T>`, wire vs C# names, nullability |
| `dotnet-error-handling` | Every try/catch and the error-translation boundary |
| `dotnet-testing` | Integration-layer tests |

**Error-boundary hazard rows (`System.Text.Json.JsonException` reaches the boundary from two directions — opposite handling):**
- A drifted or malformed **2xx** body (a missing `required` member — e.g. `SellerReceivableBreakdown.GrossAmount`, `Money.CurrencyCode`/`Value`) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions**
- Direct card, no browser round-trip: the plan authorizes with the raw sandbox Visa (or a vault token) supplied in the `AuthorizeOrder` body. If the sandbox returns `PAYER_ACTION_REQUIRED`/a 3DS challenge for the test card, the integration stops and reports it (see 3DS block) rather than driving an approval redirect.
- `currency_code` and PayPal client-id/secret and any BaseUrl override come from the application's configuration; their binding keys/defaults are the implementer's to define (not in the map).
- Standalone card vault uses `CreatePaymentToken` with a raw card (`PaymentTokenRequestCard`) — the no-verification path. If card verification/3DS-before-vault is required, switch to the `CreateSetupToken` → `CreatePaymentToken(Token=SETUP_TOKEN)` flow (fields noted in §2).
- Over-refund enforcement (remaining = captured gross − cumulative refunded) is implemented and persisted by the application; the SDK supplies the inputs but no single "net remaining" field.

**`UNVERIFIED` (only live traffic can confirm — coded defensively per §2):** the literal `rel` value of the payer-action link; the exact 3DS `EnrollmentStatus`/`AuthenticationStatus`/`LiabilityShift` combination the sandbox returns for the test Visa; the literal `Details[].Issue` codes for expired/too-late authorizations; whether `custom_id` re-surfaces as `TransactionInformation.CustomField`; the exact ISO-8601 date-time format string and any per-request date-range cap that `SearchTransactions` enforces.

**Blockers**
- **No Live/Production environment in the SDK.** `ServerEnvironment` defines only `Sandbox` (source-confirmed). This is correct for the stated sandbox scope, but production go-live cannot be selected by environment — it requires overriding `options.Server.Default.Sandbox.BaseUrl` to the live host (`api-m.paypal.com`) and live credentials. Flagged so a later production phase does not assume an environment switch exists.
