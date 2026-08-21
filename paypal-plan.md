# PayPal integration plan — eShopOnWeb (ASP.NET Core, C#) · PayPal .NET SDK (`PayPalServerSdk`)

Target: **PayPal sandbox**, direct-card drivable (no browser). SDK package `AsadAli.Checkout.Sdk`
(install version-less). Grounded against the bundled SDK map (release `v1.0.1`, commit `9653d18`) and,
where the map fell short, the SDK source for that release. Every row cites its map page; the two facts
the map did not carry (base-URL override reaching the token endpoint; only-`Sandbox` environment) were
confirmed from source and are marked (source-confirmed).

---

## 1. Scope & sequence

| # | Step | Operations (controller.method) |
|---|---|---|
| 1 | Client construction, auth, environment/BaseUrl selection, DI | `new PayPalServerSdkClient` + `PayPalServerSdkClientOptions`; `services.AddPayPalServerSdkClient` |
| 2 | Create order (intent=AUTHORIZE) then place the hold | `Orders.CreateOrder` → `Orders.AuthorizeOrder` |
| 3 | Capture the authorization at fulfilment; read fee/net | `Payments.CaptureAuthorizedPayment` |
| 4 | Re-authorize a stale authorization | `Payments.ReauthorizePayment` |
| 5 | Void an authorization (release hold) | `Payments.VoidPayment` |
| 6 | Refund a captured payment (full/partial, idempotent) | `Payments.RefundCapturedPayment` |
| 7 | Idempotency on all writes | `payPalRequestId` param on each write op (below) |
| 8 | Vault a card; list/delete tokens per shopper | `Vault.CreatePaymentToken`, `Vault.ListCustomerPaymentTokens`, `Vault.DeletePaymentToken` |
| 9 | Reconciliation / transaction reporting (paged) | `TransactionSearch.SearchTransactions` |
| 10 | Error boundary + 3DS/challenge detection | (cross-cutting — see CONTRACT SHEET error rows + trap notes) |

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

### 2a. Namespaces (add a separate `using` per kind — child namespaces are NOT imported transitively)

| Type(s) | Namespace |
|---|---|
| `PayPalServerSdkClient`, `PayPalServerSdkClientOptions`, `ServerOptions`, `Server` | `PayPalServerSdk` |
| `ServerEnvironment`, `DefaultOptions` (+ nested `DefaultOptions.SandboxOptions`) | `PayPalServerSdk.Servers` |
| `OAuth2ClientCredentials` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` |
| `IOAuth2TokenStrategy<>` | `PayPalServerSdk.Core.Authentication.OAuth2` |
| `RetryOptions`, `RetryAttempt` | `PayPalServerSdk.Core.Configuration` |
| Controllers (`Orders`, `Payments`, `Vault`, `TransactionSearch`) | `PayPalServerSdk.Api` |
| All request/response records (`OrderRequest`, `CardRequest`, `Money`, `CapturedPayment`, …) | `PayPalServerSdk.Models` |
| All enums (`CheckoutPaymentIntent`, `OrderStatus`, `AuthorizationStatus`, `CardBrand`, …) | `PayPalServerSdk.Models.Enums` |
| Typed error classes (`AuthorizeOrderError`, `CreateOrderError`, `CaptureAuthorizedPaymentError`, …) | `PayPalServerSdk.Errors` |
| `SdkException<>` | `PayPalServerSdk.Core.Exceptions` |
| `RawError`, `ApiError` | `PayPalServerSdk.Core.ErrorResponse` |

### 2b. Client construction, auth, environment & BaseUrl override — Q1 & Q7 (source: `sdk-map.md`; source-confirmed items noted)

Construction (`sdk-map.md` "Getting a client"):
- `new PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)` — the `HttpClient` is a constructor arg (also the test seam).
- DI: `services.AddPayPalServerSdkClient(o => { ... })` (`ServiceCollectionExtensions.cs`).

`PayPalServerSdkClientOptions` properties (`sdk-map.md` client-options table):

| Property | Type | Use |
|---|---|---|
| `Environment` | `ServerEnvironment` | `ServerEnvironment.Sandbox` — **the ONLY member; there is NO `Live`/`Production`** (source-confirmed: `Servers/ServerEnvironment.cs` declares only `Sandbox`; `Default()` returns `Sandbox`). |
| `Oauth2` | `OAuth2ClientCredentials?` | client-credentials creds (below) |
| `Oauth2TokenStrategy` | `IOAuth2TokenStrategy<OAuth2ClientCredentials>?` | leave null → SDK auto-builds the client-credentials strategy against `/v1/oauth2/token` |
| `Server` | `ServerOptions` | BaseUrl override (below) |
| `Retry` | `RetryOptions` | resilience — see trap note |
| `Logging` | `LoggingOptions` | logging |

`OAuth2ClientCredentials` (source-confirmed: `OAuth2ClientCredentials.cs`) — sealed class, object initializer:
- `ClientId: string` **required**, `ClientSecret: string` **required**, `Scope: string?` optional.
- Wire it as `options.Oauth2 = new OAuth2ClientCredentials { ClientId = <cfg>, ClientSecret = <cfg> };`

**Sandbox vs live selection + arbitrary BaseUrl override (Q1) — the important finding:**
- The API base URL is `options.Server.Default.Sandbox.BaseUrl` (a plain `string`, source-confirmed
  `Servers/DefaultOptions.cs`; default `"https://api-m.sandbox.paypal.com"`). `options.Server` is
  `ServerOptions`; `.Default` is `DefaultOptions`; `.Sandbox` is the nested `SandboxOptions`.
- Because `ServerEnvironment` has **only** `Sandbox`, you do **not** switch environments to reach live.
  Selection is done by setting `BaseUrl`: sandbox = leave default (or set `https://api-m.sandbox.paypal.com`);
  live = set `https://api-m.paypal.com`; explicit override = set the caller's verbatim value.
- **The override reaches the token endpoint too (source-confirmed `AuthSchemes.cs`):** the OAuth token
  request is built via `server.Default("/v1/oauth2/token")`, the same resolver that applies
  `options.Server.Default.Sandbox.BaseUrl`. So one BaseUrl value governs BOTH the token request and every
  API call, verbatim — exactly the requirement. Recommended config shape: read `PayPal:Mode`
  (Sandbox|Live) and optional `PayPal:BaseUrlOverride`; if the override is set, assign it verbatim;
  else map Mode→the two known hosts.

**Idempotency — `PayPal-Request-Id` (Q7).** Passed as the `payPalRequestId` (type `string?`) **method
parameter** (NOT a body field), present on every write op below. Supply the SAME value on a retry of the
same logical action so a double-click never authorizes/captures/refunds twice.

### 2c. Operations table

Legend: params listed in order; `!` = required non-nullable; `?/no-default` = nullable but **no C# default → must pass explicitly** (pass `null` to skip); `=x` = has default. All ops are **throw-only** (no `…Result` variant). Error case per `sdk-map.md`.

| Op (controller.method) · map page | Signature (params in order) | Request model & key fields (`Name (wire): type, req?`) | Response envelope → fields to read | Error case + accessors |
|---|---|---|---|---|
| **Orders.CreateOrder** · `operations/Orders.md` | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` — first 5 `?/no-default`; `body` required | `OrderRequest`: `Intent (intent): CheckoutPaymentIntent !` (=`Authorize`), `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !`, `PaymentSource (payment_source): PaymentSource?`, `Payer?`, `ApplicationContext?`. `PurchaseUnitRequest`: `Amount (amount): AmountWithBreakdown !` (`CurrencyCode !`,`Value !`), `ReferenceId?`,`InvoiceId?`,`CustomId?`,`Items?`. | `Order`: `Id (id): string?`, `Status (status): OrderStatus?`, `PurchaseUnits`, `PaymentSource (PaymentSourceResponse?)`, `Links (IReadOnlyList<LinkDescription>)`. Read `Id`, `Status`. | **Case A** `SdkException<CreateOrderError>`: `TryGetError(out Error)` [400,401,422] · `TryGetRawError(out RawError)` [fallback] |
| **Orders.AuthorizeOrder** · `operations/Orders.md` | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` — `id` required; next 5 `?/no-default` | `OrderAuthorizeRequest`: `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?`. If payment source already on the order, pass `body: null`. | `OrderAuthorizeResponse`: `Id`, `Status (OrderStatus?)`, `PurchaseUnits`. **Authorization id/status are nested:** `PurchaseUnits[i].Payments (PaymentCollection?).Authorizations (IReadOnlyList<AuthorizationWithAdditionalData>?)[j]` → `.Id (id): string?`, `.Status (status): AuthorizationStatus?`, `.ExpirationTime (expiration_time): string?`, `.Amount (Money?)`, `.ProcessorResponse?`. | **Case A** `SdkException<AuthorizeOrderError>`: `TryGetError(out Error)` [400,401,403,404,422,500] · `TryGetRawError` [fallback] |
| **Payments.CaptureAuthorizedPayment** · `operations/Payments.md` | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` — `authorizationId` required; next 4 `?/no-default` | `CaptureRequest` (all optional): `Amount (amount): Money?` (partial capture; omit for full), `FinalCapture (final_capture): bool?=false`, `InvoiceId?`, `NoteToPayer?`, `SoftDescriptor?`. Pass `body: null` for full capture of the whole hold. | `CapturedPayment`: `Id (id): string?`, `Status (status): CaptureStatus?`, `Amount (Money?)`, `SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?` → `.GrossAmount (gross_amount): Money !`, `.PaypalFee (paypal_fee): Money?`, `.NetAmount (net_amount): Money?` (each `Money`: `CurrencyCode`,`Value`). | **Case A** `SdkException<CaptureAuthorizedPaymentError>`: `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` [fallback] |
| **Payments.ReauthorizePayment** · `operations/Payments.md` | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` — `authorizationId` required; next 3 `?/no-default` | `ReauthorizeRequest`: `Amount (amount): Money?` (only param supported). | `PaymentAuthorization`: `Id`, `Status (AuthorizationStatus?)`, `Amount`, `ExpirationTime (expiration_time): string?`, `StatusDetails (AuthorizationStatusDetails?)` → `.Reason (AuthorizationIncompleteReason?)`. | **Case A** `SdkException<ReauthorizePaymentError>`: `TryGetError(out Error)` [400,401,403,404,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` [fallback] |
| **Payments.VoidPayment** · `operations/Payments.md` | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` — `authorizationId` required; next 3 `?/no-default`. **NOTE the order: here `payPalRequestId` is the 4th param (after `payPalAuthAssertion`), unlike the other writes.** | none (no body) | `PaymentAuthorization`: `Status (AuthorizationStatus?)` → expect `Voided`; `Id`. | **Case A** `SdkException<VoidPaymentError>`: `TryGetError(out Error)` [401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` [fallback] |
| **Payments.RefundCapturedPayment** · `operations/Payments.md` | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` — `captureId` required; next 4 `?/no-default` | `RefundRequest` (all optional): `Amount (amount): Money?` (partial: set it; **full: pass `body: null`** / empty body), `InvoiceId?`, `NoteToPayer?`, `CustomId?`, `PaymentInstruction?`. Idempotency key → `payPalRequestId`. | `Refund`: `Id (id): string?`, `Status (status): RefundStatus?`, `Amount (Money?)`, `SellerPayableBreakdown (SellerPayableBreakdown?)` → `.GrossAmount`, `.PaypalFee`, `.NetAmount`, `.TotalRefundedAmount`. | **Case A** `SdkException<RefundCapturedPaymentError>`: `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` [fallback] |
| **Vault.CreatePaymentToken** · `operations/Vault.md` | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions=null, CancellationToken ct=default)` — `payPalRequestId` `?/no-default`; `body` required | `PaymentTokenRequest`: `Customer (customer): Customer?` (`Id (id): string?` = PayPal customer id, `MerchantCustomerId?`), `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !` → `.Card (card): PaymentTokenRequestCard?` (`Number?`,`Expiry?`,`SecurityCode?`,`Name?`,`Brand (CardBrand?)`,`BillingAddress?`) OR `.Token (VaultTokenRequest?)`. | `PaymentTokenResponse`: `Id (id): string?` = **the reusable vault id/token**, `Customer (CustomerResponse?)` → `.Id` (store per shopper), `PaymentSource (PaymentTokenResponsePaymentSource?)` → `.Card (CardPaymentTokenEntity?)` → safe display: `.Brand (CardBrand?)`, `.LastDigits (last_digits): string?`, `.Expiry (expiry): string?`. | **Case A** `SdkException<CreatePaymentTokenError>`: `TryGetError1(out Error1)` [400,403,404,422,500] · `TryGetRawError` [fallback] |
| **Vault.ListCustomerPaymentTokens** · `operations/Vault.md` | `ListCustomerPaymentTokens(string customerId, int? pageSize=5, int? page=1, bool? totalRequired=false, RequestOptions? requestOptions=null, CancellationToken ct=default)` — `customerId` required (wire `customer_id`) | query params: `customer_id←customerId`, `page_size←pageSize`, `page←page`, `total_required←totalRequired` | `CustomerVaultPaymentTokensResponse`: `PaymentTokens (IReadOnlyList<PaymentTokenResponse>?)`, `TotalItems (int?)`, `TotalPages (int?)`, `Customer (VaultResponseCustomer?)`, `Links`. Page while `page < TotalPages` (set `totalRequired:true` to get counts). | **Case A** `SdkException<ListCustomerPaymentTokensError>`: `TryGetError1(out Error1)` [400,403,500] · `TryGetRawError` [fallback] |
| **Vault.DeletePaymentToken** · `operations/Vault.md` | `DeletePaymentToken(string id, RequestOptions? requestOptions=null, CancellationToken ct=default)` — `id` required (the payment-token id) | none | `void` (Task) | **Case A** `SdkException<DeletePaymentTokenError>`: `TryGetError1(out Error1)` [400,403,500] · `TryGetRawError` [fallback] |
| **TransactionSearch.SearchTransactions** · `operations/TransactionSearch.md` | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields="transaction_info", string? balanceAffectingRecordsOnly="Y", int? pageSize=100, int? page=1, RequestOptions? requestOptions=null, CancellationToken ct=default)` — `startDate`,`endDate` required; `transactionId`…`terminalId` (8) `?/no-default` | query: `start_date←startDate`, `end_date←endDate` (**ISO-8601 date-time**, e.g. `2026-08-01T00:00:00-0700`), `page_size←pageSize` (max per call), `page←page`, plus optional filters. Call with **named args** (many optional). | `SearchResponse`: `TransactionDetails (IReadOnlyList<TransactionDetails>?)` → each `.TransactionInfo (TransactionInformation?)`: `.TransactionId (transaction_id): string?`, `.TransactionAmount (transaction_amount): Money?`, `.FeeAmount (Money?)`, `.TransactionStatus (transaction_status): string?` (plain string, NOT an enum), `.TransactionInitiationDate (string?)`, `.TransactionUpdatedDate (string?)`, `.InvoiceId (string?)`, `.CustomField (string?)`. Paging: `Page (int?)`, `TotalPages (int?)`, `TotalItems (int?)`. **Detect more pages: loop while `Page < TotalPages`**, incrementing `page`. | **Case B** `SdkException<RawError>` (the ONLY Case-B op): `.StatusCode`, `.ReadAsString()`, `.ReadAsJson<T>()`. |

Ops not in scope but adjacent (read-side helpers if needed, all Case A): `Orders.GetOrder`→`Order`; `Payments.GetAuthorizedPayment`→`PaymentAuthorization`; `Payments.GetCapturedPayment`→`CapturedPayment`; `Payments.GetRefund`→`Refund`; `Vault.GetPaymentToken`→`PaymentTokenResponse`.

### 2d. Payment-source shapes (Q2) — card vs vaulted card

Two request-side payment-source containers; both expose a `Card (card): CardRequest?` and a `Token (token): Token?`:
- On **create**: `OrderRequest.PaymentSource (PaymentSource)` — has `Card`, `Token`, `Paypal`, wallets, APMs.
- On **authorize**: `OrderAuthorizeRequest.PaymentSource (OrderAuthorizeRequestPaymentSource)` — `Card`, `Token`, `Paypal`, `ApplePay`, `GooglePay`, `Venmo`.

`CardRequest` (`records-1-Ac-Pa.md`) key fields:
- **(a) Raw one-off card** (sandbox Visa `4111111111111111`): `Number (number): string?`, `Expiry (expiry): string?` (`YYYY-MM`), `SecurityCode (security_code): string?`, `Name (name): string?`, `BillingAddress (billing_address): Address?`. (Raw PAN requires PCI SAQ-D — noted in the record's own doc.)
- **(b) Previously vaulted card**: `CardRequest.VaultId (vault_id): string?` = the vault id from `PaymentTokenResponse.Id`. Do NOT also send `Number`.
- `Token` type is for `BILLING_AGREEMENT` tokens (`TokenType.BillingAgreement` only) — **not** the shape for a vaulted card; use `CardRequest.VaultId` for saved cards.

`Address` (`records-1`): `AddressLine1?`,`AddressLine2?`,`AdminArea2?` (city),`AdminArea1?` (state),`PostalCode?`,`CountryCode (country_code): string !`.

### 2e. Enum value tables (namespace `PayPalServerSdk.Models.Enums`; write the C# member, e.g. `CheckoutPaymentIntent.Authorize`)

| Enum | Members (`CSharp (WIRE)`) — from `models/enums.md` |
|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` |
| `AuthorizationIncompleteReason` (`AuthorizationStatusDetails.Reason`) | `PendingReview (PENDING_REVIEW)`, `DeclinedByRiskFraudFilters (DECLINED_BY_RISK_FRAUD_FILTERS)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `CardBrand` (safe display) | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Amex (AMEX)`, `Discover (DISCOVER)`, `Jcb (JCB)`, `Diners (DINERS)`, `Maestro (MAESTRO)`, `Elo (ELO)`, `Rupay (RUPAY)`, `Unknown (UNKNOWN)`, … (30 members total) |
| `CardType` | `Credit (CREDIT)`, `Debit (DEBIT)`, `Prepaid (PREPAID)`, `Store (STORE)`, `Unknown (UNKNOWN)` |
| `PaymentTokenStatus` (`SetupTokenResponse.Status`) | `Created (CREATED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`, `Approved (APPROVED)`, `Vaulted (VAULTED)`, `Tokenized (TOKENIZED)` |
| `LiabilityShiftIndicator` (3DS) | `No (NO)`, `Possible (POSSIBLE)`, `Unknown (UNKNOWN)` |
| `ParesStatus` (3DS `authentication_status`) | `Y`,`N`,`U`,`A`,`C`,`R`,`D`,`I` (wire = same letters) |
| `EnrollmentStatus` (3DS `enrollment_status`) | `Y (Y)`, `N (N)`, `U (U)`, `B (B)` |

### 2f. 3DS / challenge detection (Q10)

The card response carries the authentication outcome: `CardResponse.AuthenticationResult (authentication_result): AuthenticationResponse?` (in `OrderAuthorizeResponsePaymentSource.Card` / `PaymentSourceResponse.Card`) →
`.LiabilityShift (LiabilityShiftIndicator?)`, `.ThreeDSecure (ThreeDSecureAuthenticationResponse?)` → `.AuthenticationStatus (ParesStatus?)`, `.EnrollmentStatus (EnrollmentStatus?)`.
A challenge-required / redirect outcome also surfaces as `Order/OrderAuthorizeResponse.Status = OrderStatus.PayerActionRequired` together with a HATEOAS `Links` entry whose `Rel` indicates payer action. **STOP** and report to the operator in that case rather than attempting an approval round-trip (this integration is no-browser).

### 2g. Error model & reading status/body safely (Q10)

- `SdkException<TError>` (source-confirmed `Core/Exceptions/SdkException.cs`) exposes **only** `.Error` — there is no `.StatusCode`/`.Message` HTTP status on the exception itself.
- **Body fields** (Case A typed): via the operation's `TryGetError(out Error)` / `TryGetError1(out Error1)` / `TryGetDefaultError(out DefaultError)` → record with `Name (name)`, `Message (message)`, `DebugId (debug_id)`, `Details (IReadOnlyList<ErrorDetails/ErrorDetails1>)` (each `Field`,`Value`,`Issue`,`Description`), `Links`. (`records-1-Ac-Pa.md`: `Error`, `Error1`, `DefaultError`.)
- **HTTP status code**: from `RawError.StatusCode` — for Case A via `TryGetRawError(out RawError)`, for Case B (`SearchTransactions`) `.Error` is already `RawError`. `RawError` also gives `.ReadAsString()` / `.ReadAsJson<T>()`.
- 39 ops are Case A (typed), 1 (`SearchTransactions`) is Case B. No `…Result` no-throw variants anywhere.

---

## 3. Trap notes (load the named skill before writing that step)

> ⚠ Step 1 (client & DI) — the `HttpClient`/handler pipeline lifetime and whether the SDK client wrapper is singleton/transient are not visible from the constructor signature. **MUST load `dotnet-client-initialization`** before wiring `AddPayPalServerSdkClient` / `new PayPalServerSdkClient`.

> ⚠ Step 1 (auth) — WHEN credentials must be set relative to client construction, and how to source the secret from configuration (not hardcoded), is not shown by the property type. **MUST load `dotnet-authentication`** before setting `options.Oauth2`.

> ⚠ Step 1 (resilience) — the SDK's `Retry`/`Timeout` options do **not** bound a whole call and are **not** the `HttpClient` timeout; and which HTTP verbs actually retry (including whether a non-idempotent write can be re-sent on a transport failure) is not visible from the option names. This directly affects idempotency-key reuse. **MUST load `dotnet-configuration-resilience`** before tuning `RetryOptions`/BaseUrl.

> ⚠ Steps 2/3/6/8 (building bodies) — enums here are `StringEnum<T>` (not C# enums; build via the static member or `Type.FromValue("WIRE")`), `required` record members must be set in the object initializer, and JSON fields the SDK doesn't model are dropped on deserialize. **MUST load `dotnet-models`** before constructing request payloads or mapping responses.

> ⚠ Steps 2–9 (calling) — list/search ops (`SearchTransactions`, `ListCustomerPaymentTokens`) have many optional params with no C# default; call them with **named arguments** to avoid mis-binding, and use `ct:` for the cancellation token. **MUST load `dotnet-calling-endpoints`** before the first call.

> ⚠ Step 10 (error boundary) — which exception types actually reach the catch, the Case A vs Case B mechanics, whether both a typed accessor and `TryGetRawError` can succeed, and how to get the numeric status without a `.StatusCode` on the exception, are all skill-level. **MUST load `dotnet-error-handling`** before writing the boundary. (See also the mandatory `JsonException` rows in REQUIRED READING.)

> ⚠ Any step with tests — the `HttpClient` constructor arg is the fake seam. **MUST load `dotnet-testing`** before stubbing the SDK.

---

## 4. REQUIRED READING (load BEFORE implementation starts; the sheet deliberately omits their contents)

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Client construction, HttpClient lifetime, DI registration (step 1) |
| `dotnet-authentication` | Setting OAuth2 client-credentials, secret sourcing (step 1) |
| `dotnet-configuration-resilience` | Retry/timeout semantics, BaseUrl/server config, pagination (steps 1, 9) |
| `dotnet-calling-endpoints` | Named-argument calls, required vs optional params, async/`ct` (steps 2–9) |
| `dotnet-models` | Building request models, `required`/nullability, `StringEnum<T>`, wire names (steps 2–8) |
| `dotnet-error-handling` | Exception types, Case A/B, reading status/body safely (step 10 — always) |
| `dotnet-testing` | Faking the `HttpClient` seam (tests) |

**Two mandatory `System.Text.Json.JsonException` hazards for the error boundary — it reaches the boundary from two directions needing opposite handling:**
- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions**
- Plan-file path defaulting: the brief dictated the exact path, so no default was used.
- "Sandbox vs live from a config value" is implemented via the BaseUrl (there is no `Live` environment member — see below); assumed acceptable to map a `Mode` config value to the two known hosts and honor an explicit override verbatim.
- Direct-card capture of the hold "to the cent" = full capture (`body: null` or `Amount` equal to the authorized total); partial capture uses `CaptureRequest.Amount`.
- Per-shopper token scoping uses PayPal's `customer.id` returned by the first `CreatePaymentToken`; the app persists that id and passes it to `ListCustomerPaymentTokens(customerId)` and on subsequent `CreatePaymentToken` calls.

**Blockers / limitations**
- **No `Live`/`Production` `ServerEnvironment` member exists** (source-confirmed): `ServerEnvironment` declares only `Sandbox`. Reaching live is ONLY possible by overriding `options.Server.Default.Sandbox.BaseUrl` to `https://api-m.paypal.com`. This is a workable path (the override reaches the token endpoint too), but note that `options.Environment` cannot express "live" — do not rely on it for mode selection.
- **UNVERIFIED (live-traffic only):** whether a sandbox direct-card AUTHORIZE actually returns `OrderStatus.PayerActionRequired` (vs. a populated `authentication_result` with a challenge indicator) for a 3DS challenge cannot be settled from the map or source. Defensive directive for step 10: treat BOTH signals as "challenge/stop" — if `Status == OrderStatus.PayerActionRequired` OR a `Links` entry's `Rel` denotes payer action OR `AuthenticationResult.ThreeDSecure` indicates enrollment/authentication requiring a challenge, extract the best-effort reason and STOP, reporting an operator-actionable message; fall back to the generic error message if the shape is absent.
- **UNVERIFIED (live-traffic only):** exactly which `AuthorizationStatus`/error `Issue` values PayPal returns for a no-longer-renewable (stale, >29-day) authorization on `ReauthorizePayment`. Contract facts available: reauthorize is allowed days 4–29 after the 3-day honor period; beyond 30 days you must create a new authorized payment (per the op's map notes). Defensive directive for step 4: on `SdkException<ReauthorizePaymentError>`, read `Error.Name`/`Details[].Issue` best-effort and surface an operator-actionable "authorization no longer renewable — create a new order" message; also treat a returned `AuthorizationStatus` of `Denied`/`Voided`/`Expired`-like as non-renewable. Fall back to the generic message if the body doesn't parse.
</content>
</invoke>
