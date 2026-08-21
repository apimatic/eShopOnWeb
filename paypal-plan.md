# PayPal .NET SDK integration plan — eShopOnWeb

SDK: `AsadAli.Checkout.Sdk` (root namespace `PayPalServerSdk`, client `PayPalServerSdkClient`).
Map provenance: source commit `9653d18`, tag `v1.0.1`. All facts below cite the map page (or the
named SDK source file) they came from. Every operation the brief lists is **available in this SDK** —
see *Assumptions & Blockers* for the one environment constraint that is not a missing capability.

---

## 1. Scope & sequence

| # | Step | Operation(s) | Controller |
|---|---|---|---|
| 0 | Client construction + DI + config binding | `AddPayPalServerSdkClient` / `new PayPalServerSdkClient` | (root) |
| 1 | Create order, intent=AUTHORIZE, card OR vaulted card in `payment_source.card` | `CreateOrder` | `Orders` |
| 2 | Authorize the order → get authorization id + status; detect 3DS/PAYER_ACTION and STOP | `AuthorizeOrder` | `Orders` |
| 3 | Capture the authorization at fulfilment → read gross/fee/net | `CaptureAuthorizedPayment` | `Payments` |
| 4 | Re-authorize a stale authorization | `ReauthorizePayment` | `Payments` |
| 5 | Void an authorization (cancel before fulfilment) | `VoidPayment` | `Payments` |
| 6 | Refund a captured payment (full/partial) with idempotency key | `RefundCapturedPayment` | `Payments` |
| 7 | Transaction search over a date range, paginated | `SearchTransactions` | `TransactionSearch` |
| 8 | Vault a card; delete a vaulted card; reference it when paying | `CreatePaymentToken` / `DeletePaymentToken` | `Vault` |

**Clarification the brief asked for (step 1 vs 2):** intent is set **once** on `CreateOrder`
(`OrderRequest.Intent = CheckoutPaymentIntent.Authorize`). Placing the hold is a **separate
`AuthorizeOrder` call** against the created order id — that call is what produces an **authorization
id** you later capture/void/reauthorize. Capture (step 3) is a **third, later** call
(`CaptureAuthorizedPayment` on the Payments controller, keyed by the authorization id). So the flow is:
`CreateOrder(intent=AUTHORIZE, payment_source.card)` → `AuthorizeOrder(orderId, payment_source.card)`
→ (at fulfilment) `CaptureAuthorizedPayment(authorizationId)`. (Source: `operations/Orders.md`,
`operations/Payments.md`.)

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier.
> The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one
> from that type's own map row, never from where a neighbouring type sits. A members table names the
> namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒
> `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server
> and client-config types are spread across different child namespaces, and two types configured
> side by side in the same options object routinely live in different ones. Dropping a type to the
> root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### 2.0 Namespaces (`using` directives) — confirmed from source

| Type(s) | Namespace |
|---|---|
| `PayPalServerSdkClient`, `PayPalServerSdkClientOptions`, `ServerOptions`, `AddPayPalServerSdkClient` | `PayPalServerSdk` |
| `ServerEnvironment`, `DefaultOptions` (BaseUrl override) | `PayPalServerSdk.Servers` |
| All request/response **records** (`OrderRequest`, `Money`, `CardRequest`, `CapturedPayment`, `Refund`, `SearchResponse`, `PaymentTokenRequest`, …) and error-payload records (`Error`, `Error1`, `DefaultError`) | `PayPalServerSdk.Models` |
| All **enums** (`CheckoutPaymentIntent`, `OrderStatus`, `AuthorizationStatus`, `CaptureStatus`, `RefundStatus`, `CardBrand`, …) | `PayPalServerSdk.Models.Enums` |
| Per-operation error classes (`CreateOrderError`, `AuthorizeOrderError`, `CaptureAuthorizedPaymentError`, …) | `PayPalServerSdk.Errors` |
| `SdkException<T>` | `PayPalServerSdk.Core.Exceptions` |
| `RawError` | `PayPalServerSdk.Core.ErrorResponse` |
| `OAuth2ClientCredentials` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` |
| `IOAuth2TokenStrategy<T>` | `PayPalServerSdk.Core.Authentication.OAuth2` |
| `RetryOptions` | `PayPalServerSdk.Core.Configuration` |

(Namespaces verified in source: `SdkException.cs` → `PayPalServerSdk.Core.Exceptions`; `RawError.cs`
→ `PayPalServerSdk.Core.ErrorResponse`; `OAuth2ClientCredentials.cs` →
`PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`; others from `sdk-map.md` namespace
table.)

### 2.1 Client construction, auth, environment, BaseUrl override (config-bound, nothing hard-coded)

Config keys → SDK wiring:

| Config key | Where it goes | Source |
|---|---|---|
| `PayPal:ClientId` | `options.Oauth2 = new OAuth2ClientCredentials { ClientId = …, ClientSecret = … }` (`ClientId` is `required`) | `OAuth2ClientCredentials.cs` |
| `PayPal:ClientSecret` | same object, `ClientSecret` (`required`) | `OAuth2ClientCredentials.cs` |
| `PayPal:Environment` | `options.Environment` — value maps to a `ServerEnvironment` member; **only `ServerEnvironment.Sandbox` exists** (see Blockers) | `Servers/ServerEnvironment.cs` |
| `PayPal:BaseUrl` (optional override) | `options.Server.Default.Sandbox.BaseUrl = "<value>"` — used **verbatim** as the API base for **every** call **including the OAuth `/v1/oauth2/token` request** (see note) | `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `AuthSchemes.cs` |
| `PayPal:Currency` | your value for every `Money.CurrencyCode` / `AmountWithBreakdown.CurrencyCode` | `Models/Money.cs` |

`OAuth2ClientCredentials` (source `OAuth2ClientCredentials.cs`): `ClientId: string` (required),
`ClientSecret: string` (required), `Scope: string?` (optional).

`PayPalServerSdkClientOptions` members (source `PayPalServerSdkClientOptions.cs`): `Environment:
ServerEnvironment`, `Retry: RetryOptions`, `Logging: LoggingOptions`, `Server: ServerOptions`,
`Oauth2: OAuth2ClientCredentials?`, `Oauth2TokenStrategy: IOAuth2TokenStrategy<OAuth2ClientCredentials>?`.

BaseUrl override path (source): `options.Server` is a `ServerOptions`; `ServerOptions.Default` is a
`PayPalServerSdk.Servers.DefaultOptions`; `DefaultOptions.Sandbox` is a `SandboxOptions` whose
`BaseUrl` defaults to `"https://api-m.sandbox.paypal.com"`. Set
`options.Server.Default.Sandbox.BaseUrl = config["PayPal:BaseUrl"]` **only when the key is present**;
leave the default otherwise. **Why this covers the OAuth token request:** `AuthSchemes.cs` builds the
token endpoint as `server.Default("/v1/oauth2/token")`, and `Server.Default(path)` resolves through
`DefaultOptions.Resolve(...)` → `new UrlTemplate(Sandbox.BaseUrl, path, [])` — i.e. the token request
is built from the **same** `Sandbox.BaseUrl` as every operation, so the override applies to it
automatically.

Constructors (source `PayPalServerSdkClient.cs`): `new PayPalServerSdkClient(HttpClient httpClient,
PayPalServerSdkClientOptions options)`. DI: `services.AddPayPalServerSdkClient(o => { … })` (source
`ServiceCollectionExtensions.cs`) — it internally calls `services.AddHttpClient()`, pulls an
`HttpClient` from `IHttpClientFactory`, and registers the client as a **singleton**. Controller
accessors are properties on the client: `client.Orders`, `client.Payments`, `client.Vault`,
`client.TransactionSearch` (source `PayPalServerSdkClient.cs`).

### 2.2 Operations

Legend: fields as `Name (wire_name): Type` · `!req` = C# `required`. Money is `{ CurrencyCode
(currency_code): string !req, Value (value): string !req }` — amounts are **strings**, formatted to
the currency's minor units ("to the cent"). Cite: `operations/*.md`, `records-1-Ac-Pa.md`,
`records-2-Pa-Ve.md`, `enums.md`.

---

**Step 1 — `client.Orders.CreateOrder`** (`operations/Orders.md`)

- Signature: `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - First 5 string params are nullable-with-no-default → **must pass explicitly** (pass `null` to skip). Pass the idempotency key as `payPalRequestId:`. Use named args.
- Returns: **`Order`** (`records-1-Ac-Pa.md`). Read: `order.Id` (order id), `order.Status` (`OrderStatus`), `order.PurchaseUnits`.
- Request body `OrderRequest` (`records-1`): `Intent (intent): CheckoutPaymentIntent !req`, `Payer (payer): Payer?`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req`, `PaymentSource (payment_source): PaymentSource?`, `ApplicationContext (application_context): OrderApplicationContext?`.
  - `Intent = CheckoutPaymentIntent.Authorize` (wire `AUTHORIZE`) (`enums.md`).
  - `PurchaseUnitRequest` (`records-2`): `Amount (amount): AmountWithBreakdown !req` (+ optional `ReferenceId`, `CustomId`, `InvoiceId`, `Description`, `Items`). Set `CustomId`/`InvoiceId` to the eShop order id for later reconciliation.
  - `AmountWithBreakdown` (`records-1`): `CurrencyCode (currency_code): string !req`, `Value (value): string !req`, `Breakdown (breakdown): AmountBreakdown?`. `Value` = order total as a string to the cent; `CurrencyCode` = `PayPal:Currency`.
  - `PaymentSource` (`records-2`): the card lives at `Card (card): CardRequest?`.
  - **Raw card** — `CardRequest` (`records-1`): `Name (name): string?`, `Number (number): string?` (`"4111111111111111"`), `Expiry (expiry): string?` (`"YYYY-MM"`), `SecurityCode (security_code): string?`, `BillingAddress (billing_address): Address?`, `VaultId (vault_id): string?`, `Attributes (attributes): CardAttributes?`, `ExperienceContext (experience_context): CardExperienceContext?`.
  - **Vaulted card** — same `CardRequest`, set **`VaultId (vault_id)`** to the saved token id **instead of** `Number/Expiry/SecurityCode`. (This is the map-grounded way to pay with a saved card.)
- Error: `SdkException<CreateOrderError>` — **Case A**. Accessors: `TryGetError(out Error)` [400,401,422] · `TryGetRawError(out RawError)` [fallback]. `Error` (`records-1`): `Name`, `Message`, `DebugId`, `Details: IReadOnlyList<ErrorDetails>?` (each `ErrorDetails.Issue !req`, `.Field`, `.Description`), `Links`.

---

**Step 2 — `client.Orders.AuthorizeOrder`** (`operations/Orders.md`) — places the hold, returns the authorization

- Signature: `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `id` = order id from step 1. First 4 nullable strings + `body` → **must pass explicitly**. Idempotency key → `payPalRequestId:`.
- Request body `OrderAuthorizeRequest` (`records-1`): `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?`. `OrderAuthorizeRequestPaymentSource` (`records-1`): `Card (card): CardRequest?`, `Token`, `Paypal`, `ApplePay`, `GooglePay`, `Venmo`. Supply the same `CardRequest` (raw or `VaultId`) here to authorize with the card. (Body may be `null` if the buyer already approved via redirect — not our card flow.)
- Returns: **`OrderAuthorizeResponse`** (`records-1`): `Id (id): string?`, `Status (status): OrderStatus?`, `PaymentSource (payment_source): OrderAuthorizeResponsePaymentSource?`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?`, `Links (links): IReadOnlyList<LinkDescription>?`.
  - **Authorization id + status accessor path:** `resp.PurchaseUnits[0].Payments.Authorizations[0].Id` and `.Status`.
    - `PurchaseUnit.Payments (payments): PaymentCollection?` (`records-2`); `PaymentCollection.Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?` (`records-2`); `AuthorizationWithAdditionalData` (`records-1`): `Id (id): string?`, `Status (status): AuthorizationStatus?`, `Amount (amount): Money?`, `ExpirationTime (expiration_time): string?`.
  - `AuthorizationStatus` values (`enums.md`): `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)`. Persist the authorization id + `ExpirationTime` for steps 3–5.
- **3DS / challenge detection → STOP (no browser round-trip):** the map-grounded signal is
  `resp.Status == OrderStatus.PayerActionRequired` (`OrderStatus` wire `PAYER_ACTION_REQUIRED`,
  `enums.md`). When present, the order needs buyer browser approval — **do not proceed to capture,
  surface a "payment requires additional verification" outcome and stop.** Supporting detail (also
  map-grounded): `resp.PaymentSource.Card.AuthenticationResult` →
  `AuthenticationResponse.LiabilityShift (LiabilityShiftIndicator: No/Possible/Unknown)` and
  `.ThreeDSecure (ThreeDSecureAuthenticationResponse{ AuthenticationStatus: ParesStatus,
  EnrollmentStatus })` (`records-1`, `enums.md`). See the UNVERIFIED note on the HATEOAS
  `payer-action` link in *Assumptions & Blockers*.
- Error: `SdkException<AuthorizeOrderError>` — **Case A**. `TryGetError(out Error)` [400,401,403,404,422,500] · `TryGetRawError(out RawError)`.

---

**Step 3 — `client.Payments.CaptureAuthorizedPayment`** (`operations/Payments.md`) — capture at fulfilment

- Signature: `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `authorizationId` from step 2. 4 nullable params + `body` → **must pass explicitly**. Idempotency key → `payPalRequestId:`.
  - `body` = `null` for full capture, or `CaptureRequest` (`records-1`): `Amount (amount): Money?`, `InvoiceId (invoice_id): string?`, `FinalCapture (final_capture): bool? = false`, `NoteToPayer`, `SoftDescriptor`. Set `FinalCapture = true` for the final/only capture.
- Returns: **`CapturedPayment`** (`records-1`): `Id (id): string?` (**capture id** — keep for refunds), `Status (status): CaptureStatus?`, `Amount (amount): Money?`, `SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?`.
  - `CaptureStatus` (`enums.md`): `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)`.
  - **Gross / PayPal fee / net accessor path** (`SellerReceivableBreakdown`, `records-2`):
    - gross: `captured.SellerReceivableBreakdown?.GrossAmount.Value` (+ `.CurrencyCode`) — `GrossAmount (gross_amount): Money !req`
    - PayPal fee: `captured.SellerReceivableBreakdown?.PaypalFee?.Value` — `PaypalFee (paypal_fee): Money?`
    - net proceeds: `captured.SellerReceivableBreakdown?.NetAmount?.Value` — `NetAmount (net_amount): Money?`
    - **Null-guard:** the whole breakdown, and `PaypalFee`/`NetAmount` within it, are nullable; the map notes the breakdown "is not available for transactions that are in pending state" — treat `Status == Pending` / null breakdown as "fee/net not yet known".
- Error: `SdkException<CaptureAuthorizedPaymentError>` — **Case A**. `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`.

---

**Step 4 — `client.Payments.ReauthorizePayment`** (`operations/Payments.md`) — re-authorize a stale hold

- Signature: `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 3 nullable params + `body` → **must pass explicitly**. Idempotency key → `payPalRequestId:`.
  - `body` = `ReauthorizeRequest` (`records-2`): only `Amount (amount): Money?` (the API supports only `amount`).
- **When valid** (from the map's operation note): after the 3-day honor period expires, days 4–29 of the 29-day authorization window; once 30 days have passed since the original authorization you must create a **new** authorized payment instead.
- Returns: **`PaymentAuthorization`** (`records-2`): `Id (id): string?`, `Status (status): AuthorizationStatus?`, `Amount (amount): Money?`, `ExpirationTime (expiration_time): string?`.
- Error: `SdkException<ReauthorizePaymentError>` — **Case A**. `TryGetError(out Error)` [400,401,403,404,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`.
  - **Operator-actionable "cannot re-authorize any more" message:** on the typed `Error`, read `Error.Message` and iterate `Error.Details` reading `ErrorDetails.Issue` (`!req`) + `.Description`. Surface those to the operator. The exact `Issue` string that means "authorization window expired / already captured" is a live-wire value — see UNVERIFIED note in *Assumptions & Blockers*; code defensively (match on the specific issue if you know it, else fall back to `Error.Message`).

---

**Step 5 — `client.Payments.VoidPayment`** (`operations/Payments.md`) — release held funds

- Signature: `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 3 nullable params → **must pass explicitly** (note the param **order** here is `payPalMockResponse, payPalAuthAssertion, payPalRequestId` — different from other ops). Idempotency key → `payPalRequestId:`.
- Returns: **`PaymentAuthorization`** — check `Status == AuthorizationStatus.Voided`.
- Note (map): you cannot void an authorization that has been fully captured.
- Error: `SdkException<VoidPaymentError>` — **Case A**. `TryGetError(out Error)` [401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`.

---

**Step 6 — `client.Payments.RefundCapturedPayment`** (`operations/Payments.md`) — full/partial refund

- Signature: `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `captureId` from step 3. 4 nullable params + `body` → **must pass explicitly**.
  - **Idempotency key (caller-supplied) → `payPalRequestId:`** (maps to the `PayPal-Request-Id` header). Pass the same key on retries so a double-click never double-refunds.
  - `body` = `null` (or an empty `RefundRequest`) for a **full** refund; for a **partial** refund set `RefundRequest.Amount`. `RefundRequest` (`records-2`): `Amount (amount): Money?`, `CustomId (custom_id): string?`, `InvoiceId (invoice_id): string?`, `NoteToPayer (note_to_payer): string?`, `PaymentInstruction (payment_instruction): RefundPaymentInstruction?`.
- Returns: **`Refund`** (`records-2`): `Id (id): string?` (**refund id**), `Status (status): RefundStatus?`, `Amount (amount): Money?`, `SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?`.
  - `RefundStatus` (`enums.md`): `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)`.
- Error: `SdkException<RefundCapturedPaymentError>` — **Case A**. `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`.

**Idempotency-key summary (all write ops that accept it, param → header `PayPal-Request-Id`):**
`CreateOrder.payPalRequestId`, `AuthorizeOrder.payPalRequestId`,
`CaptureAuthorizedPayment.payPalRequestId`, `ReauthorizePayment.payPalRequestId`,
`VoidPayment.payPalRequestId`, `RefundCapturedPayment.payPalRequestId`,
`CreatePaymentToken.payPalRequestId`. (Note: `ConfirmOrder` has **no** `payPalRequestId` param —
only `payPalClientMetadataId`, `payPalAuthAssertion`.) Cite: `operations/Orders.md`,
`operations/Payments.md`, `operations/Vault.md`.

---

**Step 7 — `client.TransactionSearch.SearchTransactions`** (`operations/TransactionSearch.md`) — reconciliation, paginated

- Signature: `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `startDate`/`endDate` are **required** (non-nullable). Date wire names: `start_date` ← `startDate`, `end_date` ← `endDate`. Values are ISO-8601 date-time strings (see UNVERIFIED format note in Blockers).
  - The 8 middle params (`transactionId`…`terminalId`) are nullable-no-default → **must pass explicitly** (pass `null`). **Call with named arguments** so the optional filters and paging args bind correctly.
  - Paging query params: `page_size` ← `pageSize` (default 100), `page` ← `page` (default 1). Keep `fields: "transaction_info"` so each row carries the transaction detail you read below.
- Returns: **`SearchResponse`** (`records-2`): `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?`, `Page (page): int?`, `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`, `Links (links): IReadOnlyList<LinkDescription>?`.
  - **Pagination to cover the whole range:** the SDK has **no auto-pager** for this op (map: "Pagination: none (only `page`, no `perPage`)"). Loop yourself: request `page = 1`, read `TotalPages`, then request `page = 2 … TotalPages` with the same `start_date`/`end_date`/`pageSize`, accumulating `TransactionDetails`. Stop when `page > TotalPages`.
  - **Per-transaction id / amount / status accessor path:** for each `TransactionDetails` (`records-2`) read `.TransactionInfo` (`TransactionInformation`, `records-2`):
    - id: `td.TransactionInfo?.TransactionId` (wire `transaction_id`)
    - amount: `td.TransactionInfo?.TransactionAmount` (`Money?` → `.Value` + `.CurrencyCode`)
    - status: `td.TransactionInfo?.TransactionStatus` (**plain `string?`**, not an enum)
    - reconcile against eShop orders via `td.TransactionInfo?.InvoiceId` / `.CustomField` (whichever you set at step 1).
- Error: **Case B** — `SdkException<RawError>` (this is the SDK's **only** Case-B op). No typed accessors: read `ex.Error.StatusCode` and `ex.Error.ReadAsString()` / `ex.Error.ReadAsJson<T>()`. (See error-handling trap below — a Case-B catch is shaped differently from the Case-A ladder used everywhere else.)

---

**Step 8 — Vault a card / delete / reference** (`operations/Vault.md`)

- **Save a card — `client.Vault.CreatePaymentToken`:** `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)`.
  - `payPalRequestId` nullable-no-default → **must pass explicitly** (also your idempotency key). `body` required.
  - `PaymentTokenRequest` (`records-2`): `Customer (customer): Customer?`, `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req`. `Customer` (`records-1`): `Id (id): string?`, `MerchantCustomerId (merchant_customer_id): string?` — set one to tie the token to your eShop customer (needed to list tokens later).
  - `PaymentTokenRequestPaymentSource` (`records-2`): `Card (card): PaymentTokenRequestCard?`, `Token`. `PaymentTokenRequestCard` (`records-2`): `Name`, `Number (number): string?`, `Expiry (expiry): string?`, `SecurityCode (security_code): string?`, `Brand (brand): CardBrand?`, `BillingAddress (billing_address): Address?`.
  - Returns **`PaymentTokenResponse`** (`records-2`): `Id (id): string?` = **the vault id / token** you store and reuse; `Customer (customer): CustomerResponse?`; `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?`.
    - **Safe card descriptors** (`PaymentTokenResponsePaymentSource.Card` = `CardPaymentTokenEntity`, `records-1`): `Brand (brand): CardBrand?`, `LastDigits (last_digits): string?`, `Expiry (expiry): string?`, `Type (type): CardType?`, `BillingAddress`. Show brand + last4 + expiry only — never raw PAN (the SDK does not return it).
  - Error: `SdkException<CreatePaymentTokenError>` — **Case A**. Accessors are named **`TryGetError1(out Error1)`** [400,403,404,422,500] · `TryGetRawError(out RawError)`. Note the payload type is **`Error1`** (not `Error`) — `Error1` (`records-1`): `Name`, `Message`, `DebugId`, `Details: IReadOnlyList<ErrorDetails1>?`, `Links: IReadOnlyList<ErrorLinkDescription>?`.
- **Delete a vaulted card — `client.Vault.DeletePaymentToken`:** `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)`. Returns `void` (Task). Error: `SdkException<DeletePaymentTokenError>` — Case A, `TryGetError1(out Error1)` [400,403,500] · `TryGetRawError`.
- **List a customer's saved cards (optional) — `client.Vault.ListCustomerPaymentTokens`:** `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, …)` → `CustomerVaultPaymentTokensResponse` (`records-1`): `PaymentTokens: IReadOnlyList<PaymentTokenResponse>?`, `TotalItems`, `TotalPages`. Same manual paging pattern as step 7.
- **Reference a vaulted card when paying:** set `CardRequest.VaultId (vault_id)` = the token `Id` from `CreatePaymentToken`, in the `payment_source.card` of `CreateOrder` (step 1) or `AuthorizeOrder` (step 2). Do **not** send `Number/Expiry/SecurityCode` with a `VaultId`.

### 2.3 Enum value tables actually needed (all `PayPalServerSdk.Models.Enums`, `enums.md`)

| Enum | Members (`CSharpName` → wire) |
|---|---|
| `CheckoutPaymentIntent` | `Capture`→CAPTURE, `Authorize`→AUTHORIZE |
| `OrderStatus` | `Created`→CREATED, `Saved`→SAVED, `Approved`→APPROVED, `Voided`→VOIDED, `Completed`→COMPLETED, `PayerActionRequired`→PAYER_ACTION_REQUIRED |
| `AuthorizationStatus` | `Created`→CREATED, `Captured`→CAPTURED, `Denied`→DENIED, `PartiallyCaptured`→PARTIALLY_CAPTURED, `Voided`→VOIDED, `Pending`→PENDING |
| `CaptureStatus` | `Completed`→COMPLETED, `Declined`→DECLINED, `PartiallyRefunded`→PARTIALLY_REFUNDED, `Pending`→PENDING, `Refunded`→REFUNDED, `Failed`→FAILED |
| `RefundStatus` | `Cancelled`→CANCELLED, `Failed`→FAILED, `Pending`→PENDING, `Completed`→COMPLETED |
| `CardBrand` | `Visa`→VISA, `Mastercard`→MASTERCARD, `Discover`→DISCOVER, `Amex`→AMEX, `Jcb`→JCB, `Diners`→DINERS, `Elo`→ELO, `Rupay`→RUPAY, `Maestro`→MAESTRO, `Unknown`→UNKNOWN, … (30 total; use `.FromValue(...)` for any not listed) |
| `CardType` | `Credit`→CREDIT, `Debit`→DEBIT, `Prepaid`→PREPAID, `Store`→STORE, `Unknown`→UNKNOWN |
| `LiabilityShiftIndicator` | `No`→NO, `Possible`→POSSIBLE, `Unknown`→UNKNOWN |
| `PaymentTokenStatus` | `Created`→CREATED, `PayerActionRequired`→PAYER_ACTION_REQUIRED, `Approved`→APPROVED, `Vaulted`→VAULTED, `Tokenized`→TOKENIZED |

Reminder: these are `StringEnum<T>`, **not** C# enums — construct via the static member
(`CheckoutPaymentIntent.Authorize`) or `CheckoutPaymentIntent.FromValue("AUTHORIZE")`; compare with
`==` against members.

---

## 3. Trap notes (load the named skill before writing that step)

- ⚠ **Step 0 (client & DI)** — the SDK client wraps an `HttpClient` whose handler pipeline must be
  long-lived and reused via `IHttpClientFactory`, not rebuilt per request; the correct lifetime for
  the client vs the handler is not visible in the constructor. **MUST load
  `dotnet-client-initialization`** before wiring the client / `AddPayPalServerSdkClient`.
- ⚠ **Step 0 (auth)** — credentials must be set at the right moment relative to client construction,
  and secrets come from configuration, not literals; the `Oauth2` property alone does not show token
  lifetime/refresh behaviour. **MUST load `dotnet-authentication`** before wiring `OAuth2ClientCredentials`.
- ⚠ **Step 0 (resilience / BaseUrl / retries)** — whether a failed **write** (create/authorize/
  capture/refund) can be transparently re-sent by the retry layer, and what `Timeout` actually
  bounds, are not inferable from the option names; this directly interacts with the idempotency keys
  above. **MUST load `dotnet-configuration-resilience`** before setting `RetryOptions`, the timeout,
  or the base URL.
- ⚠ **Steps 1–8 (building request bodies)** — enums are `StringEnum<T>` (not C# enums), `Money`
  values are strings, and unmodeled JSON fields are dropped on deserialize; how to construct nested
  request records safely is the skill's job. **MUST load `dotnet-models`** before constructing any
  payload.
- ⚠ **Steps 1–8 (calling)** — most operations have several nullable-no-default params that mis-bind
  in a positional call; whether a given optional arg needs an explicit `null` is easy to get wrong.
  **MUST load `dotnet-calling-endpoints`** before the first call.
- ⚠ **All steps (error boundary)** — 7 of the 8 ops are Case A (typed `…Error` with
  `TryGetError`/`TryGetError1`) but **`SearchTransactions` is Case B** (`SdkException<RawError>`,
  no typed accessors); a single catch ladder shaped for Case A will not read the search op's error,
  and `TryGetRawError` is not a catch-all on the typed errors. **MUST load `dotnet-error-handling`**
  before writing any try/catch (see REQUIRED READING for the two `JsonException` hazards).
- ⚠ **Step 8 tests / integration seam** — the `HttpClient` constructor argument is the test seam;
  match the project's existing framework. **MUST load `dotnet-testing`** before stubbing the SDK.

---

## 4. REQUIRED READING (load BEFORE implementation starts)

The contract sheet deliberately does **not** carry these skills' contents — load each one before the
step it governs:

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 0 — client construction, `HttpClient` lifetime, `AddPayPalServerSdkClient` DI |
| `dotnet-authentication` | Step 0 — `OAuth2ClientCredentials` wiring, token lifetime |
| `dotnet-configuration-resilience` | Step 0 — retries/backoff, timeout, `PayPal:BaseUrl` override, pagination loops |
| `dotnet-calling-endpoints` | Steps 1–8 — named-argument calls, async/cancellation |
| `dotnet-models` | Steps 1–8 — building nested request records, `StringEnum<T>`, `Money` |
| `dotnet-error-handling` | All steps — the Case A vs Case B boundary (always needed) |
| `dotnet-testing` | Tests for the integration layer |

**Two `JsonException` hazards for the error boundary (verbatim — `System.Text.Json.JsonException`
reaches the boundary from two directions, handled oppositely):**

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException`
  from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it
  escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces**
  the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every
  `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that
  retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

- **No missing capabilities.** Every one of the 9 requested interactions maps to a real SDK
  operation — nothing here is invented or unavailable.
- **BLOCKER-ish (environment):** `ServerEnvironment` defines **only** `Sandbox` (source
  `Servers/ServerEnvironment.cs`; the `Match` helper throws for any other value). There is **no
  `Production`/`Live` environment member.** To target production you must rely on the `PayPal:BaseUrl`
  override (set `options.Server.Default.Sandbox.BaseUrl` to the live host, e.g.
  `https://api-m.paypal.com`), which — as shown in §2.1 — is applied verbatim to every call including
  the OAuth token request. Confirm with the product owner that pointing the Sandbox base URL at the
  live host is the intended go-live mechanism. Treat `PayPal:Environment` as effectively fixed to
  `Sandbox` unless/until the SDK adds a production environment.
- **UNVERIFIED (3DS `payer-action` link):** the reliable stop signal for a card challenge is
  `OrderStatus.PayerActionRequired` (map-grounded). PayPal additionally returns a HATEOAS link with
  `rel` = `"payer-action"` (the approve URL), but that exact `rel` **string** is a live-wire value
  not carried in the map/source. Code defensively: branch on `Status == PayerActionRequired`
  (authoritative), and if you also scan `resp.Links` for the approval link, match `rel`
  case-insensitively and treat its absence as "still needs approval, stop" rather than as success.
- **UNVERIFIED (reauthorize "no longer allowed" issue code):** the typed `Error.Details[].Issue`
  string that means the 29-day window has closed / the auth was already captured is a live-wire
  value not in the map. Extract best-effort: prefer a match on the specific `Issue` if known, else
  fall back to surfacing `Error.Message` verbatim to the operator. Do not hard-fail on a string you
  cannot confirm.
- **UNVERIFIED (SearchTransactions date format):** `start_date`/`end_date` are ISO-8601 date-time
  strings; PayPal's reporting API is known to require a full offset-aware timestamp
  (`yyyy-MM-ddTHH:mm:ss-0700`-style), but the exact accepted format is a live-API contract not
  carried in the map. Format defensively with a full ISO-8601 offset timestamp and, on a Case-B
  400, read `ex.Error.ReadAsString()` to see the API's format complaint rather than assuming.
- **Assumption (reconciliation key):** step 7 lines transactions up against eShop orders via the
  `invoice_id`/`custom_id` you set on the purchase unit at step 1 — this plan assumes you populate
  one of them with the eShop order id at order creation.
- **Assumption (currency):** `PayPal:Currency` is a single account currency applied to every `Money`;
  multi-currency carts are out of scope of this plan.
