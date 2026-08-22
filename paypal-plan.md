# eShopOnWeb × PayPalServerSdk — plan + contract sheet

NuGet: `AsadAli.Checkout.Sdk` (version-less). Root namespace: `PayPalServerSdk`. Client: `PayPalServerSdk.PayPalServerSdkClient`. Map stamp: tag `v1.0.1` / commit `9653d18`.

---

## Scope & sequence

1. **Config + client** — bind `PayPal:*`; construct `PayPalServerSdkClient` with OAuth2 client-id/secret, `ServerEnvironment.Sandbox`, optional verbatim `BaseUrl`.
2. **Persist PayPal-owned state** on eShop orders/payment-methods (never PAN/CVC): PayPal order id + `OrderStatus`; authorization id + `AuthorizationStatus` + `expiration_time`; capture id + `CaptureStatus` + gross/fee/net; refund ids + amounts + `RefundStatus`; vault customer id; payment-token ids.
3. **POST /api/orders** — create the eShop order in “awaiting payment”. No money movement. (PayPal `CreateOrder` may be deferred to pay.)
4. **POST /api/orders/{orderId}/pay** — `Orders.CreateOrder` with `intent=AUTHORIZE` and amount equal to the eShop total, then `Orders.AuthorizeOrder` with either raw card or `card.vault_id`. Persist hold ids/status. If `OrderStatus.PayerActionRequired` (or vault `PaymentTokenStatus.PayerActionRequired`) → stop; 3DS/browser challenge is a GAP.
5. **POST /api/orders/{orderId}/fulfil** — `Payments.GetAuthorizedPayment`; if the hold is stale, `Payments.ReauthorizePayment` (replace stored authorization id with the **new** id); then `Payments.CaptureAuthorizedPayment`. Persist captured amount, PayPal fee, net. If reauthorize/capture cannot proceed, return operator-actionable `Error.Name` + `Details[].Issue` + `Details[].Description`.
6. **POST /api/orders/{orderId}/cancel** — `Payments.VoidPayment` on the stored authorization id (before capture).
7. **POST /api/orders/{orderId}/refunds** — `Payments.RefundCapturedPayment` (full: omit amount; partial: `RefundRequest.Amount`). Refuse when remaining captured-minus-refunded is insufficient. Pass caller idempotency as `payPalRequestId`.
8. **GET /api/my-orders** — return eShop orders plus persisted PayPal ids/statuses (optionally refresh via `GetOrder` / `GetAuthorizedPayment` / `GetCapturedPayment` / `GetRefund`).
9. **GET /api/reconciliation?from=&to=** — `TransactionSearch.SearchTransactions` walking **every** `page` until `page >= TotalPages`.
10. **POST /api/payment-methods** — `Vault.CreatePaymentToken` with card fields; return token id + last digits/brand/expiry.
11. **GET /api/payment-methods** — `Vault.ListCustomerPaymentTokens` (paginate `page` / `TotalPages`).
12. **DELETE /api/payment-methods/{paymentMethodId}** — `Vault.DeletePaymentToken`.
13. **Error boundary + tests** — per-operation `SdkException<{Op}Error>` (and Case B on search); never log PAN/CVC.

App-level idempotency (check persisted PayPal ids before re-calling) is required in addition to `payPalRequestId` — see CONTRACT SHEET idempotency row.

---

## CONTRACT SHEET

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

No-throw `…Result` variants: **absent** on every operation. All calls throw.

### Client construction, auth, BaseUrl

| Fact | Value | Cite |
|---|---|---|
| Constructor | `PayPalServerSdk.PayPalServerSdkClient(System.Net.Http.HttpClient httpClient, PayPalServerSdk.PayPalServerSdkClientOptions options)` | `sdk-map.md` |
| DI | `PayPalServerSdk.ServiceCollectionExtensions.AddPayPalServerSdkClient(Action<PayPalServerSdkClientOptions>? configure = null)` | `sdk-map.md`, `ServiceCollectionExtensions.cs` |
| Options | `Environment`: `PayPalServerSdk.Servers.ServerEnvironment`; `Retry`: `PayPalServerSdk.Core.Configuration.RetryOptions`; `Logging`: `LoggingOptions`; `Server`: `PayPalServerSdk.ServerOptions`; `Oauth2`: `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials?`; `Oauth2TokenStrategy`: `IOAuth2TokenStrategy<OAuth2ClientCredentials>?` | `sdk-map.md`, `PayPalServerSdkClientOptions.cs` |
| Credentials | `new OAuth2ClientCredentials { ClientId = …, ClientSecret = …, Scope = … }` — `ClientId` and `ClientSecret` are `required string`; `Scope` is `string?` | `OAuth2ClientCredentials.cs` |
| Environments | **`ServerEnvironment.Sandbox` only** (wire `"Sandbox"`). `Default()` returns Sandbox. **No Live/Production member exists.** Bind `PayPal:Environment=sandbox` → `Sandbox`. Any other value is a GAP. | `sdk-map.md` Servers & auth, `Servers/ServerEnvironment.cs` |
| Default BaseUrl | `PayPalServerSdk.Servers.DefaultOptions.SandboxOptions.BaseUrl` defaults to `"https://api-m.sandbox.paypal.com"` | `Servers/DefaultOptions.cs` |
| Custom BaseUrl (all calls **including token**) | `options.Server.Default.Sandbox.BaseUrl = <PayPal:BaseUrl verbatim>`. Token URL is `server.Default("/v1/oauth2/token")` — same Default server node, so this override applies to the credential request too. There is no separate auth-server BaseUrl. | `ServerOptions.cs` (root ns `PayPalServerSdk`), `Servers/DefaultOptions.cs`, `Server.cs`, `AuthSchemes.cs` |
| Controllers | `client.Orders`, `client.Payments`, `client.Vault`, `client.TransactionSearch` | `sdk-map.md` |

Config keys (hard-code none): `PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Environment`, `PayPal:Currency`, `PayPal:BaseUrl` (optional).

### Idempotency headers

Caller-controlled header is **`PayPal-Request-Id`**, passed as parameter `payPalRequestId` (nullable, **must pass explicitly** — `null` to skip).

| Operation | `payPalRequestId`? | Also sent by SDK |
|---|---|---|
| `CreateOrder`, `AuthorizeOrder` | yes (XML: **mandatory** when the create carries a payment source such as Card / vault_id) | `Prefer`, plus SDK-injected `Idempotency-Key: Guid.NewGuid()` every call |
| `CaptureAuthorizedPayment`, `ReauthorizePayment`, `RefundCapturedPayment`, `VoidPayment` | yes (`RefundCapturedPayment` / capture store the key 45 days per XML) | same random `Idempotency-Key` |
| `CreatePaymentToken`, `CreateSetupToken` | yes (stored 3 hours per XML) | same random `Idempotency-Key` |
| `DeletePaymentToken`, GETs, `SearchTransactions` | no `payPalRequestId` param | Delete still sends random `Idempotency-Key` |

Cite: `operations/Orders.md`, `operations/Payments.md`, `operations/Vault.md`; header wiring `Api/Orders.cs`, `Api/Payments.cs`, `Api/Vault.cs`.

**UNVERIFIED** which header the live API keys on when both `PayPal-Request-Id` (stable) and `Idempotency-Key` (new GUID per invocation) are present. Defensive: always pass a stable `payPalRequestId` **and** short-circuit in eShop if the order already has the target PayPal id/status (do not re-issue a write). Refunds use the caller-supplied idempotency key as `payPalRequestId`.

`prefer`: default `"return=minimal"` (id, status, HATEOAS only). For pay/fulfil/refund **pass `prefer: "return=representation"`** so authorization/capture/fee/net bodies are present. Cite: `Api/Orders.cs` / `Api/Payments.cs` XML.

### Enums in scope (`PayPalServerSdk.Models.Enums`, `StringEnum<T>` — not C# enums)

| Enum | Members (C# id = wire) | Use |
|---|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, **`Authorize (AUTHORIZE)`** | Pay flow **must** use `Authorize`. Never `Capture` at checkout. `enums.md` |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, **`PayerActionRequired (PAYER_ACTION_REQUIRED)`** | After create/authorize: if `PayerActionRequired` → **GAP / stop** (3DS/browser). `enums.md` |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` | **No `EXPIRED` member.** Staleness is `ExpirationTime` and/or error `Details[].Issue`. `enums.md` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` | Fulfil + remaining-refundable. `enums.md` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` | `enums.md` |
| `CardBrand` | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Discover (DISCOVER)`, `Amex (AMEX)`, … `Unknown (UNKNOWN)` | Display only. `enums.md` |
| `PaymentTokenStatus` | `Created (CREATED)`, **`PayerActionRequired (PAYER_ACTION_REQUIRED)`**, `Approved (APPROVED)`, `Vaulted (VAULTED)`, `Tokenized (TOKENIZED)` | Setup-token path; `PayerActionRequired` → GAP. `enums.md` |
| `VaultStatus` | `Vaulted (VAULTED)`, `Created (CREATED)`, `Approved (APPROVED)` | Vault-on-authorize response. `enums.md` |
| `StoreInVaultInstruction` | **`OnSuccess (ON_SUCCESS)`** (only member) | Vault-at-pay. `enums.md` |
| `TokenType` | **`BillingAgreement (BILLING_AGREEMENT)` only** | `PaymentSource.Token` is **not** the saved-card handle. Saved cards use `CardRequest.VaultId`. `enums.md`, `Models/Token.cs` |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` | Only if exchanging a setup token for a payment token. `enums.md` |
| `OrdersCardVerificationMethod` | `ScaAlways (SCA_ALWAYS)`, `ScaWhenRequired (SCA_WHEN_REQUIRED)`, `_3DSecure (3D_SECURE)`, `AvsCvv (AVS_CVV)` | Default on `CardVerification` is `ScaWhenRequired`. Do **not** build a 3DS return-url round-trip. `enums.md` |

---

### Operations

#### 1. `client.Orders.CreateOrder` — start AUTHORIZE order

- **HTTP**: `POST /v2/checkout/orders` · `operations/Orders.md` · `Api/Orders.cs`
- **Signature**: `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must-pass-explicitly** (nullable, no default): `payPalMockResponse`, `payPalRequestId`, `payPalPartnerAttributionId`, `payPalClientMetadataId`, `payPalAuthAssertion`. Pass `null` to skip. `body` is required (not nullable).
- **Request `PayPalServerSdk.Models.OrderRequest`** (`records-1-Ac-Pa.md`):
  - `Intent (intent): CheckoutPaymentIntent !req` → `CheckoutPaymentIntent.Authorize`
  - `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req`
  - `PaymentSource (payment_source): PaymentSource?` — may be set here **or** only on AuthorizeOrder
  - `Payer (payer): Payer?`, `ApplicationContext (application_context): OrderApplicationContext?`
- **`PurchaseUnitRequest`**: `Amount (amount): AmountWithBreakdown !req`; `CustomId (custom_id): string?`; `InvoiceId (invoice_id): string?`. Put the eShop order id in `CustomId` and/or `InvoiceId` for reconciliation.
- **`AmountWithBreakdown`**: `CurrencyCode (currency_code): string !req`, `Value (value): string !req` — **string**, not decimal. `Value` must equal the eShop order total to the cent. Currency from `PayPal:Currency`.
- **`PaymentSource`**: `Card (card): CardRequest?` (do not use `Token` for vaulted cards). `records-2-Pa-Ve.md`
- **`CardRequest`** (`records-1-Ac-Pa.md` + `Models/CardRequest.cs`):
  - One-off: `Name (name)`, `Number (number)` (13–19 digits), `Expiry (expiry)` **`YYYY-MM`**, `SecurityCode (security_code)` (CVC), `BillingAddress (billing_address): Address?`
  - Saved card: `VaultId (vault_id): string?` — PayPal payment-token id. **Do not send number/CVC with vault_id.**
  - Vault-at-pay: `Attributes (attributes): CardAttributes?` → `Vault (vault): VaultInstructionBase?` → `StoreInVault (store_in_vault): StoreInVaultInstruction?` = `OnSuccess`; `Customer (customer): CardCustomerInformation?`
- **`Address`**: `CountryCode (country_code): string !req`; optional `AddressLine1`, `AddressLine2`, `AdminArea2` (city), `AdminArea1` (state), `PostalCode`. `records-1-Ac-Pa.md`
- **Response**: `PayPalServerSdk.Models.Order` — **not wrapped**. Fields: `Id (id)`, `Status (status): OrderStatus?`, `Intent`, `PurchaseUnits`, `PaymentSource`, `Links`. `records-1-Ac-Pa.md`
- **Error**: Case A `PayPalServerSdk.Core.Exceptions.SdkException<PayPalServerSdk.Errors.CreateOrderError>`
  - `TryGetError(out PayPalServerSdk.Models.Error)` **[400, 401, 422]**
  - `TryGetRawError(out PayPalServerSdk.Core.ErrorResponse.RawError)` fallback
- **Pagination**: none

#### 2. `client.Orders.AuthorizeOrder` — place the hold (not capture)

- **HTTP**: `POST /v2/checkout/orders/{id}/authorize` · `operations/Orders.md`
- **Signature**: `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must-pass-explicitly**: `payPalMockResponse` … `body` (5 params). `id` = PayPal order id from CreateOrder.
- **Request `OrderAuthorizeRequest`**: `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` with `Card (card): CardRequest?` (raw card **or** `VaultId`). `records-1-Ac-Pa.md`
- **Response `OrderAuthorizeResponse`**: same shape as Order (not wrapped): `Id`, `Status`, `PurchaseUnits`. Hold lives at `PurchaseUnits[n].Payments.Authorizations[n]`.
- **`PurchaseUnit.Payments`**: `PaymentCollection` — `Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?`. Read `Id`, `Status`, `Amount`, `ExpirationTime (expiration_time)`, `CreateTime`. `records-2-Pa-Ve.md`, `records-1-Ac-Pa.md`
- **Stop if** `Status == OrderStatus.PayerActionRequired` **or** authorization `Status == AuthorizationStatus.Denied`. Do not follow `Links` into a browser.
- **Error**: Case A `SdkException<AuthorizeOrderError>` — `TryGetError(out Error)` **[400, 401, 403, 404, 422, 500]** · `TryGetRawError`
- Pass `prefer: "return=representation"`. Persist authorization `Id` + `Status` + `ExpirationTime` + PayPal order `Id` + `Status`.

**Authorize + vault in one call:** supported. On the `CardRequest` used for CreateOrder and/or AuthorizeOrder set `Attributes.Vault.StoreInVault = StoreInVaultInstruction.OnSuccess` (and customer ids). Read `OrderAuthorizeResponse.PaymentSource.Card.Attributes.Vault` → `CardVaultResponse`: `Id (id)`, `Status (status): VaultStatus?`, `Customer`. Cite: `CardAttributes`, `VaultInstructionBase`, `CardVaultResponse`, `CardAttributesResponse` in `records-1-Ac-Pa.md`.

**Authorize using a vaulted token:** `CardRequest.VaultId` = `PaymentTokenResponse.Id`. Not `PaymentSource.Token` (`TokenType` is billing-agreement only).

#### 3. `client.Payments.GetAuthorizedPayment` — inspect hold before fulfil

- **HTTP**: `GET /v2/payments/authorizations/{authorization_id}` · `operations/Payments.md`
- **Signature**: `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 2 nullable no-default params must be passed explicitly.
- **Returns** `PaymentAuthorization`: `Id`, `Status`, `Amount`, `ExpirationTime (expiration_time)`, `CreateTime`, `UpdateTime`. `records-2-Pa-Ve.md`
- **Error**: Case A `SdkException<GetAuthorizedPaymentError>` — `TryGetError(out Error)` **[401, 403, 404]** · `TryGetNoContent(out RawError)` **[500]** · `TryGetRawError`

If `ExpirationTime` is in the past (or capture later returns an expiry issue) → reauthorize. If `Status` is `Voided` / `Denied` / `Captured` → cannot reauthorize; tell the operator using status + error details.

#### 4. `client.Payments.ReauthorizePayment` — renew a stale hold

- **HTTP**: `POST /v2/payments/authorizations/{authorization_id}/reauthorize` · `operations/Payments.md`
- **Signature**: `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must-pass-explicitly**: `payPalRequestId`, `payPalAuthAssertion`, `body`.
- **Request `ReauthorizeRequest`**: `Amount (amount): Money?` only. Use the original hold amount (`CurrencyCode` + `Value` strings). `records-2-Pa-Ve.md`
- **Returns** `PaymentAuthorization` — **new `Id`**. Replace the stored authorization id; subsequent capture/void use the new id.
- **Error**: Case A `SdkException<ReauthorizePaymentError>` — `TryGetError(out Error)` **[400, 401, 403, 404, 422]** · `TryGetNoContent(out RawError)` **[500]** · `TryGetRawError`
- **Cannot renew (map notes, `operations/Payments.md`)**: after **30 days** from the original authorization you must create a **new** authorized payment rather than reauthorize. Honor period is 3 days; reauthorize window is days 4–29. Model summary on `ReauthorizeRequest` says “only once”; operation notes say multiple reauthorizations are allowed — **the two generated texts disagree**; treat a **422** `TryGetError` as terminal for this hold and surface `Error.Name` + each `Details[].Issue` + `Details[].Description` to the operator (do not invent a new checkout silently).
- There is **no** `AuthorizationStatus.Expired`. **UNVERIFIED** that live `Details[].Issue` equals `AUTHORIZATION_EXPIRED`; read `Issue` as a string and include it in the operator message (fallback: `Error.Name` + `Error.Message`).

#### 5. `client.Payments.CaptureAuthorizedPayment` — money movement at fulfil

- **HTTP**: `POST /v2/payments/authorizations/{authorization_id}/capture` · `operations/Payments.md`
- **Signature**: `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must-pass-explicitly**: `payPalMockResponse` … `body` (4 params).
- **Request `CaptureRequest`**: `Amount (amount): Money?` (omit for full remaining); `FinalCapture (final_capture): bool? = false` → set `true` on the fulfilment capture; `InvoiceId`, `NoteToPayer`, `SoftDescriptor`, `PaymentInstruction` optional. `records-1-Ac-Pa.md`
- **Do not** call `Orders.CaptureOrder` (that is intent-CAPTURE completion). Fulfil uses this Payments capture.
- **Response `CapturedPayment`** (not wrapped). Read with `prefer: "return=representation"`:
  - `Id (id)`, `Status (status): CaptureStatus?`
  - `Amount (amount): Money?` — captured amount
  - `SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?`
    - `GrossAmount (gross_amount): Money !req`
    - `PaypalFee (paypal_fee): Money?`
    - `NetAmount (net_amount): Money?`
  - Map note: breakdown is **not** available when the capture is pending. `records-1-Ac-Pa.md`, `records-2-Pa-Ve.md`
- **Error**: Case A `SdkException<CaptureAuthorizedPaymentError>` — `TryGetError(out Error)` **[400, 401, 403, 404, 409, 422]** · `TryGetNoContent` **[500]** · `TryGetRawError`
- Persist capture id, status, gross, fee, net. 409 = already captured / conflict.

#### 6. `client.Payments.VoidPayment` — release hold on cancel

- **HTTP**: `POST /v2/payments/authorizations/{authorization_id}/void` · `operations/Payments.md`
- **Notes**: cannot void a fully captured authorization.
- **Signature**: `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must-pass-explicitly**: `payPalMockResponse`, `payPalAuthAssertion`, `payPalRequestId`.
- **Returns** `PaymentAuthorization` (`Status` expected `Voided`).
- **Error**: Case A `SdkException<VoidPaymentError>` — `TryGetError(out Error)` **[401, 403, 404, 409, 422]** · `TryGetNoContent` **[500]** · `TryGetRawError`

#### 7. `client.Payments.RefundCapturedPayment` — full / partial refund

- **HTTP**: `POST /v2/payments/captures/{capture_id}/refund` · `operations/Payments.md`
- **Signature**: `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must-pass-explicitly**: `payPalMockResponse` … `body` (4 params).
- **Idempotency**: caller key → `payPalRequestId`.
- **Request `RefundRequest`**: full refund → `body: null` or object with no `Amount`; partial → `Amount (amount): Money?` (`CurrencyCode` + `Value`). Also `CustomId`, `InvoiceId`, `NoteToPayer`. `records-2-Pa-Ve.md`
- **Response `Refund`**: `Id`, `Status: RefundStatus?`, `Amount`, `SellerPayableBreakdown` (`GrossAmount`, `PaypalFee`, `NetAmount`, `TotalRefundedAmount`). `records-2-Pa-Ve.md`
- **Error**: Case A `SdkException<RefundCapturedPaymentError>` — `TryGetError(out Error)` **[400, 401, 403, 404, 409, 422]** · `TryGetNoContent` **[500]** · `TryGetRawError`
- eShop remaining-refundable = captured `Amount.Value` minus sum of successful refund `Amount.Value`. Refuse when the request would exceed that. `CaptureStatus.PartiallyRefunded` / `Refunded` are signals, not a substitute for the arithmetic.
- Optional refresh: `GetCapturedPayment(string captureId, string? payPalMockResponse, …)` / `GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, …)`.

#### 8. `client.Vault.CreatePaymentToken` — save card (Flow 2)

- **HTTP**: `POST /v3/vault/payment-tokens` · `operations/Vault.md`
- **Signature**: `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `payPalRequestId` must be passed explicitly.
- **Request `PaymentTokenRequest`**: `Customer (customer): Customer?` (`Id`, `MerchantCustomerId` — pass eShop user id as `MerchantCustomerId`); `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req` → `Card (card): PaymentTokenRequestCard?` with `Name`, `Number`, `Expiry` (`YYYY-MM`), `SecurityCode`, `BillingAddress`. Alternative: `Token (token): VaultTokenRequest?` (`Id !req`, `Type !req` = `VaultTokenRequestType.SetupToken`) after `CreateSetupToken`. `records-2-Pa-Ve.md`
- **Response `PaymentTokenResponse`**: `Id (id)` (this is `vault_id` / paymentMethodId), `Customer (customer): CustomerResponse?` (**persist `Customer.Id` for list**), `PaymentSource.Card: CardPaymentTokenEntity?` — **safe display**: `LastDigits (last_digits)`, `Brand (brand): CardBrand?`, `Expiry (expiry)`, `Name`. Never persist/log `Number`/`SecurityCode`. `records-2-Pa-Ve.md`, `records-1-Ac-Pa.md`
- **Error**: Case A `SdkException<CreatePaymentTokenError>` — **`TryGetError(out Error)`** (AsadAli.Checkout.Sdk **1.0.0**; map v1.0.1’s `TryGetError1(out Error1)` is **not** in this package) **[400, 403, 404, 422, 500]** · `TryGetRawError`. **401 is not in the typed list** → `TryGetRawError` / `RawError.StatusCode`.
- `CreateSetupToken` is available (`SetupTokenRequest` / `SetupTokenResponse.Status: PaymentTokenStatus`) but if status is `PayerActionRequired`, that is the same 3DS GAP — do not build an approval round-trip.

#### 9. `client.Vault.ListCustomerPaymentTokens` / `GetPaymentToken`

- **List HTTP**: `GET /v3/vault/payment-tokens` · `operations/Vault.md`
- **Signature**: `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- Query wire: `customer_id` ← `customerId` (PayPal vault customer id, **required**), `page_size`, `page`, `total_required`.
- **Returns** `CustomerVaultPaymentTokensResponse`: `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?`, `TotalItems`, `TotalPages`, `Customer`. Walk `page` 1..`TotalPages`. Map pagination cell: “none (only `page`, no `perPage`)”.
- **Error**: Case A `SdkException<ListCustomerPaymentTokensError>` — `TryGetError(out Error)` **[400, 403, 500]** · `TryGetRawError`
- **Get**: `GetPaymentToken(string id, …)` → `PaymentTokenResponse`. Error `TryGetError(out Error)` **[403, 404, 422, 500]**.

#### 10. `client.Vault.DeletePaymentToken` — unvault

- **HTTP**: `DELETE /v3/vault/payment-tokens/{id}` · `operations/Vault.md`
- **Signature**: `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns** `void` (`Task`). No `payPalRequestId` parameter.
- **Error**: Case A `SdkException<DeletePaymentTokenError>` — `TryGetError(out Error)` **[400, 403, 500]** · `TryGetRawError`

#### 11. `client.TransactionSearch.SearchTransactions` — reconciliation

- **HTTP**: `GET /v1/reporting/transactions` · `operations/TransactionSearch.md`
- **Signature**: `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must-pass-explicitly**: `transactionId` … `terminalId` (8 params) — pass `null` to skip. `startDate` / `endDate` are required strings (ISO-8601 from `from`/`to`).
- **Returns `SearchResponse`**: `TransactionDetails (transaction_details)`, `Page`, `TotalItems`, `TotalPages`, `StartDate`, `EndDate`, `LastRefreshedDatetime`. **Walk `page` from 1 while `page <= TotalPages`** (do not stop after page 1). `records-2-Pa-Ve.md`
- **Line-up fields** on `TransactionDetails.TransactionInfo` (`TransactionInformation`): `TransactionId (transaction_id)`, `PaypalReferenceId (paypal_reference_id)`, `InvoiceId (invoice_id)`, `CustomField (custom_field)`, `TransactionAmount`, `FeeAmount (fee_amount)`, `TransactionStatus (transaction_status): string?`, `TransactionInitiationDate`. Match `InvoiceId`/`CustomField` to the eShop id sent as purchase-unit `invoice_id`/`custom_id`.
- **Error**: **Case B** `SdkException<PayPalServerSdk.Core.ErrorResponse.RawError>` — the **only** Case B operation in this SDK. Accessors: `StatusCode: HttpStatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. Optional `ReadAsJson<SearchError>()` (`Name`, `Message`, `DebugId`, `Details: IReadOnlyList<TransactionSearchErrorDetails>?` with `Issue !req`).
- **Notes** (map): executed transactions can take **up to three hours** to appear; empty recent ranges are expected. Lists up to previous three years.

---

### Error reading (all money-movement + vault)

Typed payload `PayPalServerSdk.Models.Error` (`records-1-Ac-Pa.md`): `Name (name): string !req`, `Message (message): string !req`, `DebugId (debug_id): string !req`, `Details (details): IReadOnlyList<ErrorDetails>?`.

`ErrorDetails`: `Issue (issue): string !req`, `Description (description): string?`, `Field`, `Value`, `Location`. **Issue is a free-form string — not an enum.** The SDK does **not** list `INSTRUMENT_DECLINED`, `AUTHORIZATION_EXPIRED`, etc.

Vault typed payload on **1.0.0** is the same `PayPalServerSdk.Models.Error` as Orders/Payments (`TryGetError`): `Name`, `Message`, `DebugId` required; `Details: IReadOnlyList<ErrorDetails>?` (`Issue !req`, `Description?`). `Error1` / `ErrorDetails1` / `TryGetError1` exist only in map/package **1.0.1**, not in the pinned 1.0.0 assembly.

`PayPalServerSdk.Core.Exceptions.SdkException<TError>` exposes **only** `.Error` (`SdkException.cs`). Case A `Error` **has no HTTP status property**. Status is implied only by which `TryGet…` matched, and `TryGetError` **collapses 400/401/403/404/422/500** onto one payload — distinguish 401 vs 422 vs decline via `Error.Name` + `Details[].Issue`, not via a status code. Case B / `TryGetRawError` / `TryGetNoContent`: use `RawError.StatusCode`.

**UNVERIFIED** live literals: `INSTRUMENT_DECLINED`, `AUTHORIZATION_EXPIRED`, `AUTHENTICATION_FAILURE`. Defensive: extract `Details[].Issue` when present; else `Error.Name` + `Error.Message`; include `DebugId` for PayPal support. Never parse `exception.ToString()`.

---

### Money / display types (shared)

| Type | Fields used | Cite |
|---|---|---|
| `PayPalServerSdk.Models.Money` | `CurrencyCode (currency_code): string !req`, `Value (value): string !req` | `records-1-Ac-Pa.md` |
| `CardPaymentTokenEntity` | `LastDigits`, `Brand`, `Expiry`, `Name` | `records-1-Ac-Pa.md` |
| `LinkDescription` | `Href !req`, `Rel !req`, `Method` | `records-1-Ac-Pa.md` |

---

## Trap notes

⚠ Step 1 (client / DI) — `HttpClient` lifetime and whether the SDK wrapper is registered as a long-lived singleton vs per-request is not visible from the constructor. **MUST load `dotnet-client-initialization`** before `new PayPalServerSdkClient` or `AddPayPalServerSdkClient`.

⚠ Step 1 (auth) — credentials must be set on `options.Oauth2` from configuration (not literals); a 401 on create/authorize is an auth-scheme failure as well as a call-site failure. **MUST load `dotnet-authentication`** before wiring `OAuth2ClientCredentials`.

⚠ Step 1 (BaseUrl / retries / timeout) — `Retry` / `Timeout` on options do **not** bound a whole logical pay/capture/refund and are **not** the timeout on the `HttpClient` you register; a transport retry of a POST can execute a write more than once (interacts with the random `Idempotency-Key` the SDK injects). **MUST load `dotnet-configuration-resilience`** before registering the client or setting `options.Server.Default.Sandbox.BaseUrl`.

⚠ Steps 4–12 (calls) — every in-scope write has several **nullable parameters with no C# default**; positional calls mis-bind. Named arguments; skip with `null`; cancellation token is `ct:`. List/search (`SearchTransactions`, `ListCustomerPaymentTokens`) likewise. **MUST load `dotnet-calling-endpoints`** before the first `client.{Controller}.{Op}`.

⚠ Steps 4–12 (models) — enums are `StringEnum<T>` (static members / `FromValue`), not C# enums; `required` properties must be object-initializer-set; `Money.Value` is a string. **MUST load `dotnet-models`** before building `OrderRequest` / `CardRequest` / vault bodies.

⚠ Step 13 (error boundary) — Orders/Payments **and Vault (1.0.0)** use `TryGetError(out Error)`; SearchTransactions is Case B `SdkException<RawError>` with no `TryGetError`. Mixing these misses 401/422. **MUST load `dotnet-error-handling`** before any `try/catch`.

⚠ Step 13 — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 13 — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 13 (tests) — the test seam is the `HttpClient` constructor argument, not the generated controllers. **MUST load `dotnet-testing`** before stubbing PayPal.

⚠ Logging — request bodies for pay/vault contain PAN/CVC. Do not enable body logging. **MUST load `dotnet-configuration-resilience`** (logging options) before turning logging on.

---

## REQUIRED READING

Load **before implementation starts**. This sheet does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — constructing / DI-registering `PayPalServerSdkClient` and `HttpClient` lifetime |
| `dotnet-authentication` | Step 1 — `Oauth2` / `OAuth2ClientCredentials` |
| `dotnet-configuration-resilience` | Step 1 — BaseUrl, retries, timeout, pagination walking, logging |
| `dotnet-calling-endpoints` | Steps 4–12 — named args, must-pass-null, `ct:`, list paging |
| `dotnet-models` | Steps 4–12 — `StringEnum<T>`, required inits, Money strings, nested records |
| `dotnet-error-handling` | Step 13 — Case A vs B, `TryGetError`, both `JsonException` directions |
| `dotnet-testing` | Step 13 — `HttpClient` seam |

---

## Assumptions & Blockers

**Assumptions**

- Place-order creates only the eShop row; PayPal `CreateOrder` + `AuthorizeOrder` run at **pay**. Fulfil captures; cancel voids; refunds hit the stored capture id.
- Amounts are formatted as PayPal string `Value`s (cent-accurate) in `PayPal:Currency`.
- eShop maps `PayPal:Environment=sandbox` → `ServerEnvironment.Sandbox`. Test card `4111111111111111` is a runtime input, not an SDK contract.
- Saved-card `paymentMethodId` is `PaymentTokenResponse.Id` (`vault_id`). PayPal `Customer.Id` from the vault response is stored per shopper for `ListCustomerPaymentTokens`.
- Reconciliation matches `TransactionInformation.InvoiceId` / `CustomField` to purchase-unit `invoice_id` / `custom_id`.

**Blockers / GAPs (do not invent workarounds)**

- **No Live environment in this SDK.** `ServerEnvironment` members: `Sandbox` only. `PayPal:Environment` values other than sandbox cannot be expressed.
- **3DS / browser challenge is a GAP.** If `OrderStatus.PayerActionRequired` or `PaymentTokenStatus.PayerActionRequired` (or a payer-action HATEOAS link), stop and report; do not implement return-url / `CardExperienceContext` approval.
- **Issue codes are not in the map.** `INSTRUMENT_DECLINED`, `AUTHORIZATION_EXPIRED`, etc. are not enums; only `ErrorDetails.Issue: string`. Cannot-renew is a 422 `TryGetError` after the 30-day window described in `operations/Payments.md`, plus `ExpirationTime` — not an `AuthorizationStatus` member.
- **`TokenType` has no payment-token value.** Paying with a saved card is `CardRequest.VaultId`, not `PaymentSource.Token`.
- **SDK injects a fresh `Idempotency-Key` GUID on every mutating call** (`Api/Orders.cs`, `Api/Payments.cs`, `Api/Vault.cs`). Caller-stable idempotency is only `payPalRequestId` → `PayPal-Request-Id`. Whether PayPal honors that over the random key is **UNVERIFIED**; eShop must still no-op when PayPal ids are already stored.
- **Case A typed `Error` does not carry HTTP status**; 401 vs 422 share `TryGetError` on several operations. Vault 401 is not in `TryGetError`’s 1.0.0 list (same as map’s old `TryGetError1` status set).
- Transaction Search lag (up to three hours) is expected; empty recent `from`/`to` ranges are not a defect.
- ReauthorizeRequest XML vs ReauthorizePayment notes **disagree** on one-vs-many reauthorizations; do not assume a second reauthorize will succeed.
