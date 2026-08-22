# PayPal .NET SDK — eShopOnWeb payment integration plan

NuGet: `AsadAli.Checkout.Sdk` (install **version-less**: `dotnet add package AsadAli.Checkout.Sdk`). Map documents tag `v1.0.1` / commit `9653d18`. Root namespace: `PayPalServerSdk`. Client: `PayPalServerSdkClient`. Controllers live on the client: `client.Orders`, `client.Payments`, `client.Vault`, `client.TransactionSearch` (`PayPalServerSdk.Api`). Enums are `StringEnum<T>` in `PayPalServerSdk.Models.Enums` — use static members (e.g. `CheckoutPaymentIntent.Authorize`), not C# enums. Records are immutable `init` in `PayPalServerSdk.Models`. Every operation is throw-only (no `…Result` variants). `ct:` is the cancellation-token named argument — never `cancellationToken:`.

---

## Scope & sequence

App-side (no PayPal call): place order; persist payment row; GET my-orders with stored PayPal ids/statuses; enforce remaining-refundable locally as captured − Σ refunds.

| Step | What | SDK operations |
|---|---|---|
| 0 | Bind `PayPal:*` config; construct client (sandbox; optional verbatim BaseUrl) | Client ctor / `AddPayPalServerSdkClient` |
| 1 | Pay: AUTHORIZE hold for the order total (raw card **or** vaulted `vault_id`). Amount = order total to the cent. Idempotent via `payPalRequestId` + local “already authorized” guard. | `Orders.CreateOrder` (`Intent = Authorize` + `PaymentSource.Card`) |
| 1b | If `Order.Status == PayerActionRequired` (3DS / browser challenge): **STOP** — do not design a return-url round-trip. Fail the pay and surface the gap. | detect on `CreateOrder` response (no extra op) |
| 2 | Persist PayPal-owned state: order id, authorization id + status + `expiration_time` | from `CreateOrder` body (or `Orders.GetOrder` / `Payments.GetAuthorizedPayment`) |
| 3 | Fulfil: if authorization expired/stale, `Payments.ReauthorizePayment`; persist **new** authorization id. If reauthorize cannot succeed, return operator-actionable error (do not capture). | `Payments.GetAuthorizedPayment` → `Payments.ReauthorizePayment` |
| 4 | Fulfil: CAPTURE the (current) authorization. Surface captured amount, PayPal fee, net. Idempotent via `payPalRequestId` + local “already captured” guard. | `Payments.CaptureAuthorizedPayment` (optional `Payments.GetCapturedPayment`) |
| 5 | Cancel before fulfilment: RELEASE the hold (void). Idempotent via `payPalRequestId`. | `Payments.VoidPayment` |
| 6 | Refund after fulfilment: full (omit amount) or partial (`RefundRequest.Amount`). Caller-supplied idempotency key → `payPalRequestId`. Never refund more than captured − already refunded. | `Payments.RefundCapturedPayment` (optional `Payments.GetRefund`) |
| 7 | Save card (vault). Persist token id + PayPal customer id + last digits / brand / expiry. Never persist PAN/CVC. | `Vault.CreatePaymentToken` |
| 8 | List caller’s saved cards | `Vault.ListCustomerPaymentTokens` (page until `TotalPages`) |
| 9 | Delete saved card | `Vault.DeletePaymentToken` |
| 10 | Reconciliation: PayPal transactions for ISO-8601 `from`/`to`; exhaust **every** page; line up to eShop orders | `TransactionSearch.SearchTransactions` |

Out of scope (SDK has them; this product does not use them): `Orders.CaptureOrder` (CAPTURE-intent checkout), `Orders.AuthorizeOrder` (authorize-later without `payment_source` on create), `Orders.ConfirmOrder`, tracking, subscriptions, `Vault.CreateSetupToken` / `GetSetupToken` (3DS/setup-token confirm path), `TransactionSearch.SearchBalances`.

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

Nullable parameters **without a C# default** must be passed explicitly (`null` to skip). Prefer `prefer: "return=representation"` on writes so ids, status, fee, and net are in the response (SDK default is `"return=minimal"`).

### Client construction & auth

| Fact | Value | Cite |
|---|---|---|
| Package | `AsadAli.Checkout.Sdk` (version-less install) | `sdk-map.md`, getting-started |
| Client | `PayPalServerSdk.PayPalServerSdkClient` | `sdk-map.md` |
| Ctor | `PayPalServerSdkClient(System.Net.Http.HttpClient httpClient, PayPalServerSdk.PayPalServerSdkClientOptions options)` | `sdk-map.md` |
| DI | `PayPalServerSdk.ServiceCollectionExtensions.AddPayPalServerSdkClient(Action<PayPalServerSdkClientOptions>? configure = null)` | `sdk-map.md` |
| Options | `Environment`: `PayPalServerSdk.Servers.ServerEnvironment` · `Retry`: `PayPalServerSdk.Core.Configuration.RetryOptions` · `Logging`: `LoggingOptions` · `Server`: `PayPalServerSdk.ServerOptions` · `Oauth2`: `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials?` · `Oauth2TokenStrategy`: `IOAuth2TokenStrategy<OAuth2ClientCredentials>?` | `sdk-map.md` |
| Credentials | `new OAuth2ClientCredentials { ClientId = cfg["PayPal:ClientId"], ClientSecret = cfg["PayPal:ClientSecret"] }` — both `required string`. Optional `Scope: string?`. Bind from `PAYPAL_CLIENT_ID` / `PAYPAL_CLIENT_SECRET`. Never hard-code. | `OAuth2ClientCredentials` source |
| Environment | **Only member:** `ServerEnvironment.Sandbox` (wire `"Sandbox"`). `Default()` → Sandbox. **No Live member.** Bind `PayPal:Environment` from `PAYPAL_ENVIRONMENT`; for this work set Sandbox. If config is not sandbox, fail at startup — do not invent a live URL. | `sdk-map.md` Servers & auth; `Servers/ServerEnvironment.cs` |
| Default API host | `https://api-m.sandbox.paypal.com` | `Servers/DefaultOptions.cs` |
| **BaseUrl override** | When `PayPal:BaseUrl` is set, assign it **verbatim** to `options.Server.Default.Sandbox.BaseUrl` (`PayPalServerSdk.ServerOptions.Default` is `PayPalServerSdk.Servers.DefaultOptions`; `Sandbox` is `DefaultOptions.SandboxOptions.BaseUrl: string`). This host is used for **every** path, including the token request `POST {BaseUrl}/v1/oauth2/token` (`AuthSchemes` builds the token URL via `server.Default("/v1/oauth2/token")`). When unset, leave the sandbox default. | `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `AuthSchemes.cs` |
| Currency | `PayPal:Currency` → `Money.CurrencyCode` / `AmountWithBreakdown.CurrencyCode` (ISO-4217, 3 chars, `required`). Amounts from catalog, formatted as `Money.Value` **string** (e.g. `"10.00"`), matching that currency’s fraction digits. | `records-1-Ac-Pa.md` `Money`, `AmountWithBreakdown` |
| Per-call options | `PayPalServerSdk.Core.RequestOptions` — only `LogLevel`; **not** a base-URL override. | `Core/RequestOptions.cs` |

Controller accessors (`PayPalServerSdkClient`): `Orders`, `Payments`, `Vault`, `TransactionSearch` — types `PayPalServerSdk.Api.Orders` / `Payments` / `Vault` / `TransactionSearch`.

---

### Operations

#### 1. `Orders.CreateOrder` — pay-time AUTHORIZE (raw card or vaulted card)

| | |
|---|---|
| Controller | `PayPalServerSdk.Api.Orders` via `client.Orders` |
| HTTP | `POST /v2/checkout/orders` |
| Signature | `Task<PayPalServerSdk.Models.Order> CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, PayPalServerSdk.Models.OrderRequest body, string? prefer = "return=minimal", PayPalServerSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | `payPalMockResponse`, `payPalRequestId`, `payPalPartnerAttributionId`, `payPalClientMetadataId`, `payPalAuthAssertion` (pass `null` to skip) |
| Idempotency | `payPalRequestId` — pass a stable key per eShop order pay attempt (also guard locally: if authorization id already stored, do not call again) |
| Error | **Case A** `PayPalServerSdk.Core.Exceptions.SdkException<PayPalServerSdk.Errors.CreateOrderError>` · `TryGetError(out PayPalServerSdk.Models.Error)` [400, 401, 422] · `TryGetRawError(out PayPalServerSdk.Core.ErrorResponse.RawError)` fallback |
| Pagination | none |
| Cite | `operations/Orders.md`, `records-1-Ac-Pa.md` `OrderRequest` / `Order` |

**`OrderRequest` (body)** — `Intent (intent): CheckoutPaymentIntent !req`, `Payer (payer): Payer?`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req`, `PaymentSource (payment_source): PaymentSource?`, `ApplicationContext (application_context): OrderApplicationContext?`.

Set **intent to AUTHORIZE**: `Intent = PayPalServerSdk.Models.Enums.CheckoutPaymentIntent.Authorize` (wire `AUTHORIZE`). Do **not** use `CheckoutPaymentIntent.Capture`.

**Amount + currency** — one `PurchaseUnitRequest`: `Amount (amount): AmountWithBreakdown !req` with `CurrencyCode (currency_code): string !req` = `PayPal:Currency`, `Value (value): string !req` = order total to the cent. Optional `CustomId (custom_id)` and `InvoiceId (invoice_id)`: set both to the eShop order id so reconciliation can match `TransactionInformation.InvoiceId` / `CustomField`. Optional `ReferenceId (reference_id)`.

**Raw card (one-off)** — `PaymentSource.Card = new CardRequest { … }` (`PaymentSource.Card (card): CardRequest?`):

| C# (`CardRequest`) | Wire | Type | Notes |
|---|---|---|---|
| `Name` | `name` | `string?` | cardholder name |
| `Number` | `number` | `string?` | PAN, 13–19 digits. **Never persist or log.** |
| `Expiry` | `expiry` | `string?` | ISO-8601 **`YYYY-MM`** (length 7) |
| `SecurityCode` | `security_code` | `string?` | CVC, 3–4 digits. **Never persist or log.** |
| `BillingAddress` | `billing_address` | `Address?` | `Address.CountryCode (country_code): string !req`; optional `AddressLine1`, `AddressLine2`, `AdminArea2` (city), `AdminArea1` (state), `PostalCode` |
| `VaultId` | `vault_id` | `string?` | **omit** on raw-card pay |
| `Attributes` | `attributes` | `CardAttributes?` | see 3DS detection below |
| `StoredCredential` | `stored_credential` | `CardStoredCredential?` | omit for first-time one-off |

**Vaulted card** — do **not** send PAN/CVC. `PaymentSource.Card = new CardRequest { VaultId = savedTokenId }` (the vault payment-token id from `PaymentTokenResponse.Id`). Optional `StoredCredential`: `PaymentInitiator = PaymentInitiator.Customer` (`CUSTOMER`), `PaymentType = StoredPaymentSourcePaymentType.Unscheduled` or `OneTime`, `Usage = StoredPaymentSourceUsageType.Subsequent`. **Do not** use `PaymentSource.Token`: `Token.Type` is only `TokenType.BillingAgreement` — that is not a vault card.

**3DS / browser challenge — STOP, do not implement a round-trip.** After `CreateOrder`, if `Order.Status == OrderStatus.PayerActionRequired` (wire `PAYER_ACTION_REQUIRED`), or `Order.Links` contains `Rel` `payer-action`, or `Order.PaymentSource.Card.AuthenticationResult.ThreeDSecure` indicates a challenge (`ParesStatus.C`): **fail pay, report gap**. Do not set `CardExperienceContext.ReturnUrl` / `CancelUrl` to build an approval flow. Optional: set `Card.Attributes.Verification.Method = OrdersCardVerificationMethod.AvsCvv` (wire `AVS_CVV`) so the request asks for AVS/CVV rather than SCA; default is `ScaWhenRequired`. If PayPal still returns `PAYER_ACTION_REQUIRED`, it is a blocker.

**Response envelope `Order`** (not wrapped in another property):

| Read | Path | Wire |
|---|---|---|
| PayPal order id | `Order.Id` | `id` |
| Order status | `Order.Status` (`OrderStatus`) | `status` |
| Intent echo | `Order.Intent` | `intent` |
| Authorization id | `Order.PurchaseUnits[0].Payments.Authorizations[0].Id` | `purchase_units[].payments.authorizations[].id` |
| Authorization status | `….Status` (`AuthorizationStatus`) | `status` |
| Hold amount | `….Amount` (`Money`) | `amount` |
| Authorization expiry | `….ExpirationTime` | `expiration_time` |
| Processor info | `AuthorizationWithAdditionalData.ProcessorResponse` | `processor_response` |

`PurchaseUnit.Payments` is `PaymentCollection`: `Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?`, `Captures`, `Refunds`. Persist order id + authorization id + status + expiration. Call with `prefer: "return=representation"` so these fields are present; if using default minimal, follow with `GetOrder`.

---

#### 2. `Orders.GetOrder` — refresh PayPal order (ids/status)

| | |
|---|---|
| Signature | `Task<Order> GetOrder(string id, string? fields, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | `fields`, `payPalMockResponse`, `payPalAuthAssertion` |
| Error | **Case A** `SdkException<GetOrderError>` · `TryGetError(out Error)` [401, 404] · `TryGetRawError` |
| Idempotency | none (GET) |
| Cite | `operations/Orders.md` |

`id` = PayPal order id. Same `Order` envelope as create.

---

#### 3. `Payments.GetAuthorizedPayment` — detect stale hold

| | |
|---|---|
| Controller | `PayPalServerSdk.Api.Payments` via `client.Payments` |
| HTTP | `GET /v2/payments/authorizations/{authorization_id}` |
| Signature | `Task<PayPalServerSdk.Models.PaymentAuthorization> GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | `payPalMockResponse`, `payPalAuthAssertion` |
| Error | **Case A** `SdkException<GetAuthorizedPaymentError>` · `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` |
| Cite | `operations/Payments.md`, `records-2-Pa-Ve.md` `PaymentAuthorization` |

**Stale detection:** read `PaymentAuthorization.ExpirationTime (expiration_time): string?` and `Status (status): AuthorizationStatus?`. If `Status` is `Voided`, `Captured`, `Denied`, or `PartiallyCaptured`, do not reauthorize/capture as a fresh hold. If `ExpirationTime` is in the past (or honor period elapsed) and status is still `Created` / `Pending`, call `ReauthorizePayment` before capture. If already past the original authorization window (operation notes: after 30 days you cannot reauthorize — must create a new authorized payment), do **not** silently re-pay; return an operator-actionable error from the failed reauthorize (below).

`PaymentAuthorization` also: `Id`, `Amount`, `CreateTime`, `UpdateTime`.

---

#### 4. `Payments.ReauthorizePayment` — renew stale authorization

| | |
|---|---|
| HTTP | `POST /v2/payments/authorizations/{authorization_id}/reauthorize` |
| Signature | `Task<PaymentAuthorization> ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, PayPalServerSdk.Models.ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | `payPalRequestId`, `payPalAuthAssertion`, `body` |
| Idempotency | `payPalRequestId` |
| Error | **Case A** `SdkException<ReauthorizePaymentError>` · `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` |
| Cite | `operations/Payments.md`, `records-2-Pa-Ve.md` `ReauthorizeRequest` |

**Body:** `ReauthorizeRequest.Amount (amount): Money?` — original order total (`CurrencyCode` + `Value`). Operation notes: supports only the `amount` parameter; new honor period; not possible after 30 days from original auth.

**Response:** `PaymentAuthorization` — persist **`Id` as the new authorization id** (subsequent capture/void use this id, not the stale one).

If this throws: surface `Error.Name`, `Error.Message`, `Error.DebugId`, and each `Error.Details[].Issue` + `Description` + `Field` to the operator. That is the “can no longer be renewed” path. Do not invent a second pay.

---

#### 5. `Payments.CaptureAuthorizedPayment` — capture at fulfilment

| | |
|---|---|
| HTTP | `POST /v2/payments/authorizations/{authorization_id}/capture` |
| Signature | `Task<PayPalServerSdk.Models.CapturedPayment> CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, PayPalServerSdk.Models.CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | `payPalMockResponse`, `payPalRequestId`, `payPalAuthAssertion`, `body` |
| Idempotency | `payPalRequestId` (stable per eShop fulfilment). Local guard: if capture id already stored, skip. `TryGetError` includes **409** — treat conflict as “already captured” and `GetCapturedPayment`. |
| Error | **Case A** `SdkException<CaptureAuthorizedPaymentError>` · `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` |
| Cite | `operations/Payments.md`, `records-1-Ac-Pa.md` `CaptureRequest` / `CapturedPayment`, `records-2-Pa-Ve.md` `SellerReceivableBreakdown` |

**Body `CaptureRequest`:** `Amount (amount): Money?` — omit for full capture of the authorized amount; or set equal to remaining authorized. `FinalCapture (final_capture): bool? = false` — set `true` at fulfilment. Optional `InvoiceId`, `NoteToPayer`, `SoftDescriptor`.

**Do not** call `Orders.CaptureOrder` (that captures a CAPTURE-intent checkout order).

**Response `CapturedPayment` — captured amount, fee, net:**

| Merchant display | Path | Wire |
|---|---|---|
| Capture id | `CapturedPayment.Id` | `id` |
| Capture status | `CapturedPayment.Status` (`CaptureStatus`) | `status` |
| Captured amount | `CapturedPayment.Amount` **or** `SellerReceivableBreakdown.GrossAmount` | `amount` / `seller_receivable_breakdown.gross_amount` |
| PayPal fee | `SellerReceivableBreakdown.PaypalFee` | `paypal_fee` |
| Net proceeds | `SellerReceivableBreakdown.NetAmount` | `net_amount` |

`SellerReceivableBreakdown`: `GrossAmount (gross_amount): Money !req`, `PaypalFee (paypal_fee): Money?`, `NetAmount (net_amount): Money?`, plus optional receivable/FX/platform fees. Notes: breakdown is **not** available when capture is `Pending`. Persist capture id, status, gross, fee, net. Use `prefer: "return=representation"`.

---

#### 6. `Payments.GetCapturedPayment`

| | |
|---|---|
| Signature | `Task<CapturedPayment> GetCapturedPayment(string captureId, string? payPalMockResponse, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | `payPalMockResponse` |
| Error | **Case A** `SdkException<GetCapturedPaymentError>` · `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent` [500] · `TryGetRawError` |
| Cite | `operations/Payments.md` |

Same `CapturedPayment` envelope (fee/net/status). Use to refresh after 409 or for my-orders.

---

#### 7. `Payments.VoidPayment` — cancel before fulfilment (release hold)

| | |
|---|---|
| HTTP | `POST /v2/payments/authorizations/{authorization_id}/void` |
| Signature | `Task<PaymentAuthorization> VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | `payPalMockResponse`, `payPalAuthAssertion`, `payPalRequestId` |
| Idempotency | `payPalRequestId`. Notes: cannot void a fully captured auth. Error **409** if already voided/captured — map to already-cancelled vs already-captured for the operator. |
| Error | **Case A** `SdkException<VoidPaymentError>` · `TryGetError(out Error)` [401, 403, 404, 409, 422] · `TryGetNoContent` [500] · `TryGetRawError` |
| Cite | `operations/Payments.md` |

No body. Response `PaymentAuthorization` with `Status = AuthorizationStatus.Voided`. Persist voided status. No money moved.

---

#### 8. `Payments.RefundCapturedPayment` — full / partial refund

| | |
|---|---|
| HTTP | `POST /v2/payments/captures/{capture_id}/refund` |
| Signature | `Task<PayPalServerSdk.Models.Refund> RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, PayPalServerSdk.Models.RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | `payPalMockResponse`, `payPalRequestId`, `payPalAuthAssertion`, `body` |
| Idempotency | **`payPalRequestId` = caller-supplied idempotency key** (required by product). Also refuse locally if requested amount > captured − Σ successful refunds. |
| Error | **Case A** `SdkException<RefundCapturedPaymentError>` · `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent` [500] · `TryGetRawError` |
| Cite | `operations/Payments.md`, `records-2-Pa-Ve.md` `RefundRequest` / `Refund` / `SellerPayableBreakdown` |

**Full refund:** `body: null` or `new RefundRequest()` with no `Amount` (notes: empty payload). **Partial:** `RefundRequest.Amount = new Money { CurrencyCode = …, Value = partial }` (`Amount (amount): Money?`). Optional `CustomId`, `InvoiceId`, `NoteToPayer`.

**Response `Refund`:** `Id (id)`, `Status (status): RefundStatus?`, `Amount (amount): Money?`. `SellerPayableBreakdown.TotalRefundedAmount (total_refunded_amount)` is the running total refunded against the capture — use it (plus local sum) so a partly-refunded order cannot exceed captured. Persist every refund id + amount + status.

`CaptureStatus` after refunds: `PartiallyRefunded` / `Refunded` (`GetCapturedPayment`).

---

#### 9. `Payments.GetRefund`

| | |
|---|---|
| Signature | `Task<Refund> GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | `payPalMockResponse`, `payPalAuthAssertion` |
| Error | **Case A** `SdkException<GetRefundError>` · `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent` [500] · `TryGetRawError` |
| Cite | `operations/Payments.md` |

---

#### 10. `Vault.CreatePaymentToken` — save card

| | |
|---|---|
| Controller | `PayPalServerSdk.Api.Vault` via `client.Vault` |
| HTTP | `POST /v3/vault/payment-tokens` |
| Signature | `Task<PayPalServerSdk.Models.PaymentTokenResponse> CreatePaymentToken(string? payPalRequestId, PayPalServerSdk.Models.PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | `payPalRequestId` |
| Idempotency | `payPalRequestId` |
| Error | **Case A** `SdkException<CreatePaymentTokenError>` · `TryGetError1(out PayPalServerSdk.Models.Error1)` [400, 403, 404, 422, 500] · `TryGetRawError` *(note: `TryGetError1` / `Error1`, not `TryGetError` / `Error`)* |
| Cite | `operations/Vault.md`, `records-2-Pa-Ve.md` |

**Body `PaymentTokenRequest`:** `Customer (customer): Customer?` — set `MerchantCustomerId (merchant_customer_id)` to the signed-in shopper id (and `Id` only if we already have a PayPal customer id). `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req` with `Card (card): PaymentTokenRequestCard?`: `Name`, `Number`, `Expiry` (`YYYY-MM`), `SecurityCode`, `BillingAddress` (`CountryCode` required), optional `Brand`. **Never persist Number/SecurityCode.**

**Response `PaymentTokenResponse`:**

| Persist / display | Path | Wire |
|---|---|---|
| Saved-card id (vault token) | `PaymentTokenResponse.Id` | `id` |
| PayPal customer id (required later for list) | `Customer.Id` (`CustomerResponse`) | `customer.id` |
| Merchant customer echo | `Customer.MerchantCustomerId` | `merchant_customer_id` |
| Last digits | `PaymentSource.Card.LastDigits` (`CardPaymentTokenEntity`) | `payment_source.card.last_digits` |
| Brand | `PaymentSource.Card.Brand` (`CardBrand`) | `brand` |
| Expiry | `PaymentSource.Card.Expiry` | `expiry` |

No PAN on the response model. If `Links` include a payer-action / 3DS challenge, **STOP** (same 3DS gap) — do not implement `CreateSetupToken` + browser return as a workaround.

---

#### 11. `Vault.ListCustomerPaymentTokens` — list saved cards

| | |
|---|---|
| HTTP | `GET /v3/vault/payment-tokens` |
| Signature | `Task<PayPalServerSdk.Models.CustomerVaultPaymentTokensResponse> ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Query wire | `customer_id` ← `customerId`, `page_size` ← `pageSize`, `page` ← `page`, `total_required` ← `totalRequired` |
| Error | **Case A** `SdkException<ListCustomerPaymentTokensError>` · `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError` |
| Pagination | SDK has **no auto-pager**. Loop `page = 1..TotalPages` with `pageSize` (raise from default 5; e.g. 20) and `totalRequired: true` on the first page so `TotalPages` / `TotalItems` populate. |
| Cite | `operations/Vault.md`, `records-1-Ac-Pa.md` `CustomerVaultPaymentTokensResponse` |

`customerId` is **PayPal’s** vault customer id (`CustomerResponse.Id` from save), not the eShop user id. Persist that id at vault time. Response: `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?` — same safe display fields as save. SDK **does** expose list; still persist token id + display fields locally so my-account works if list is paged, but PayPal list is source of truth after delete.

---

#### 12. `Vault.GetPaymentToken`

| | |
|---|---|
| Signature | `Task<PaymentTokenResponse> GetPaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Error | **Case A** `SdkException<GetPaymentTokenError>` · `TryGetError1(out Error1)` [403, 404, 422, 500] · `TryGetRawError` |
| Cite | `operations/Vault.md` |

---

#### 13. `Vault.DeletePaymentToken` — delete saved card

| | |
|---|---|
| HTTP | `DELETE /v3/vault/payment-tokens/{id}` |
| Signature | `Task DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Returns | `void` (`Task`) |
| Error | **Case A** `SdkException<DeletePaymentTokenError>` · `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError` |
| Idempotency | **no** `payPalRequestId` on this operation |
| Cite | `operations/Vault.md` |

`id` = vault payment-token id. After success: drop local row; list/get must not return it; pay with that `VaultId` must fail (PayPal 404/422 — operator/shopper-actionable). No `payPalRequestId` on delete — make delete locally idempotent (already-deleted → success).

---

#### 14. `TransactionSearch.SearchTransactions` — reconciliation over a date range

| | |
|---|---|
| Controller | `PayPalServerSdk.Api.TransactionSearch` via `client.TransactionSearch` |
| HTTP | `GET /v1/reporting/transactions` |
| Signature | `Task<PayPalServerSdk.Models.SearchResponse> SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | the 8 nullables `transactionId` … `terminalId` (pass `null` to skip). **`startDate` and `endDate` are required `string`s** — pass the API `from`/`to` ISO-8601 date-times verbatim. |
| Query wire | `start_date` ← `startDate`, `end_date` ← `endDate`, plus the optional filters; `page_size` ← `pageSize`, `page` ← `page` |
| Error | **Case B (only Case B op in this SDK)** `SdkException<PayPalServerSdk.Core.ErrorResponse.RawError>` · `StatusCode: HttpStatusCode` · `ReadAsString()` / `ReadAsJson<T>()` / `ReadAsBytes()`. Catch **`SdkException<RawError>`**, not a typed `{Op}Error`. |
| Pagination | **No SDK pager.** `SearchResponse.Page`, `TotalItems`, `TotalPages`, `Links`. Loop `page` from 1 to `TotalPages` (keep `pageSize` ≤ 100 default) until the whole `[startDate, endDate]` is consumed. Do not stop after page 1. |
| Cite | `operations/TransactionSearch.md`, `records-2-Pa-Ve.md` `SearchResponse` / `TransactionDetails` / `TransactionInformation` |

**Line-up fields** (`SearchResponse.TransactionDetails[]` → `TransactionInfo`):

| Use | C# | Wire |
|---|---|---|
| PayPal transaction id | `TransactionInformation.TransactionId` | `transaction_id` |
| Related PayPal id | `PaypalReferenceId` + `PaypalReferenceIdType` | `paypal_reference_id` / `_type` (`Odr`/`Txn`/…) |
| When | `TransactionInitiationDate` / `TransactionUpdatedDate` | `transaction_initiation_date` / `transaction_updated_date` |
| Amount | `TransactionAmount` (`Money`) | `transaction_amount` |
| Fee | `FeeAmount` | `fee_amount` |
| Status | `TransactionStatus` (`string?`) | `transaction_status` |
| Invoice (eShop order id if we sent it) | `InvoiceId` | `invoice_id` |
| Custom (eShop order id if we sent `custom_id`) | `CustomField` | `custom_field` |
| Event code | `TransactionEventCode` | `transaction_event_code` |

Match to eShop: `InvoiceId`/`CustomField` → order id; also match stored PayPal capture/authorization/order ids to `TransactionId` / `PaypalReferenceId`. Default `fields = "transaction_info"` is enough for these properties. Notes: executed transactions can take up to three hours to appear; window is previous three years.

---

### Error types (what actually reaches `catch`)

| Layer | Type | How to read status / body |
|---|---|---|
| Thrown | `PayPalServerSdk.Core.Exceptions.SdkException<TError>` — property `Error` only (no `StatusCode` on the exception itself) | `sdk-map.md`, `Core/Exceptions/SdkException.cs` |
| Case A (all ops except search) | `TError` = `{Operation}Error` in `PayPalServerSdk.Errors` | Orders/Payments: `ex.Error.TryGetError(out PayPalServerSdk.Models.Error)` then `Error.Name`, `Message`, `DebugId`, `Details[]` (`Issue` !req, `Description`, `Field`, `Value`). Vault: `TryGetError1(out Error1)` (same shape, `ErrorDetails1`). Fallback `TryGetRawError(out RawError)` → `RawError.StatusCode` + `ReadAsString()`. Some payment ops also `TryGetNoContent(out RawError)` for 500. Typed `Error` does **not** carry HTTP status; status grouping is on the accessor. |
| Case B | `SearchTransactions` only: `SdkException<RawError>` | `ex.Error.StatusCode`, `ex.Error.ReadAsString()` / `ReadAsJson<T>()` |
| `RawError` | `PayPalServerSdk.Core.ErrorResponse.RawError` | `StatusCode: HttpStatusCode` · `ReadAsString()` · `ReadAsJson<T>()` · `ReadAsBytes()` |
| 2xx / error-body JSON | see trap notes — `System.Text.Json.JsonException` | REQUIRED READING |

No-throw variants: **absent**.

---

### Enums in scope (`PayPalServerSdk.Models.Enums`)

| Enum | Members we use (C# = wire) |
|---|---|
| `CheckoutPaymentIntent` | `Authorize (AUTHORIZE)`, `Capture (CAPTURE)` — **pay with Authorize only** |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, **`PayerActionRequired (PAYER_ACTION_REQUIRED)`** ← 3DS stop |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `CardBrand` | `Visa (VISA)`, `Mastercard (MASTERCARD)`, … `Unknown (UNKNOWN)` — display only |
| `OrdersCardVerificationMethod` | `ScaAlways (SCA_ALWAYS)`, `ScaWhenRequired (SCA_WHEN_REQUIRED)`, `_3DSecure (3D_SECURE)`, `AvsCvv (AVS_CVV)` |
| `PaymentInitiator` | `Customer (CUSTOMER)`, `Merchant (MERCHANT)` |
| `StoredPaymentSourcePaymentType` | `OneTime (ONE_TIME)`, `Recurring (RECURRING)`, `Unscheduled (UNSCHEDULED)` |
| `StoredPaymentSourceUsageType` | `First (FIRST)`, `Subsequent (SUBSEQUENT)`, `Derived (DERIVED)` |
| `ParesStatus` | `Y`, `N`, `U`, `A`, **`C` (challenge)**, `R`, `D`, `I` — `C` ⇒ 3DS stop |
| `EnrollmentStatus` | `Y`, `N`, `U`, `B` |
| `ServerEnvironment` | **`Sandbox` only** (wire `Sandbox`) — namespace `PayPalServerSdk.Servers`, not `.Models.Enums` |
| `VaultStatus` | `Vaulted (VAULTED)`, `Created (CREATED)`, `Approved (APPROVED)` |
| `PaymentTokenStatus` | `Created`, `PayerActionRequired`, `Approved`, `Vaulted`, `Tokenized` (setup-token; unused if we only `CreatePaymentToken`) |
| `TokenType` | `BillingAgreement` **only** — cannot represent a vault card |

Cite: `map/models/enums.md`.

---

### Idempotency / `payPalRequestId` (SDK PayPal-Request-Id)

| Operation | `payPalRequestId` param? |
|---|---|
| `CreateOrder` | yes — pay authorize |
| `CaptureAuthorizedPayment` | yes — capture |
| `ReauthorizePayment` | yes |
| `VoidPayment` | yes |
| `RefundCapturedPayment` | yes — **caller key** |
| `CreatePaymentToken` | yes |
| `DeletePaymentToken`, all GETs, `ListCustomerPaymentTokens`, `SearchTransactions` | **no** |

Pass a new string per logical action; reuse the same string on retry of the **same** action. Combine with local payment-state guards so a double-click never authorizes or captures twice even if PayPal is also idempotent.

---

### PayPal-owned state to persist on the eShop payment (never PAN)

`PaypalOrderId`, `AuthorizationId`, `AuthorizationStatus`, `AuthorizationExpirationTime`, `CaptureId`, `CaptureStatus`, `CapturedGross` / `PaypalFee` / `NetAmount`, refund list (`RefundId`, amount, `RefundStatus`), last `payPalRequestId`s, vault: `PaypalCustomerId`, `PaymentTokenId`, `LastDigits`, `Brand`, `Expiry`.

---

## Trap notes

⚠ Step 0 (client registration) — `HttpClient` / handler lifetime vs the SDK wrapper is not visible from the ctor; registering the wrong lifestyle duplicates handlers or disposes a shared client. **MUST load `dotnet-client-initialization`** before `new PayPalServerSdkClient` / `AddPayPalServerSdkClient`.

⚠ Step 0 (auth) — credentials belong on `options.Oauth2` **before** construct; secrets come from `PayPal:ClientId` / `PayPal:ClientSecret`, not literals. Wrong property/namespace (`Oauth2` vs a guessed `ClientId` on options) yields 401. **MUST load `dotnet-authentication`**.

⚠ Step 0 (BaseUrl / retries / timeout) — `Retry` / `Timeout` on options do **not** bound a whole logical pay/capture and are **not** the timeout on the `HttpClient` you register; a transport failure can retry verbs the status-code list does not mention, which changes whether a failed write can be re-sent. Custom BaseUrl is `options.Server.Default.Sandbox.BaseUrl` only — not `RequestOptions`. **MUST load `dotnet-configuration-resilience`** before wiring the client.

⚠ Steps 1–10 (calls) — five-plus nullable no-default parameters **mis-bind if passed positionally**; named arguments are required; the token parameter is `ct:`. **MUST load `dotnet-calling-endpoints`** before the first `client.Orders` / `Payments` / `Vault` / `TransactionSearch` call.

⚠ Steps 1, 7 (models) — `required` + `init` records, `StringEnum<T>` members, vault vs raw card fields, and **PAN/CVC must never be copied into EF entities or logs**; unmodeled JSON is dropped on deserialize so do not round-trip unknown fields. **MUST load `dotnet-models`** before constructing `OrderRequest` / `CardRequest` / `PaymentTokenRequest` / `Money`.

⚠ Steps 1–10 (errors) — Orders/Payments use `TryGetError(out Error)`; Vault uses `TryGetError1(out Error1)`; `SearchTransactions` is Case B `SdkException<RawError>` — a single `catch (SdkException<CreateOrderError>)` will not catch vault or search failures. Typed `Error` has no status code. **MUST load `dotnet-error-handling`** before any try/catch.

⚠ A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ A **non-2xx** body that does not match its operation’s generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Tests — the `HttpClient` constructor argument is the fake seam; do not mock generated controller internals. **MUST load `dotnet-testing`** before writing integration tests.

---

## REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 0 — constructing / DI-registering `PayPalServerSdkClient` and `HttpClient` lifetime |
| `dotnet-authentication` | Step 0 — `Oauth2` / `OAuth2ClientCredentials` wiring from config |
| `dotnet-configuration-resilience` | Step 0 — retries, timeouts, BaseUrl (`Server.Default.Sandbox.BaseUrl`), pagination loops |
| `dotnet-calling-endpoints` | Steps 1–10 — named args, must-pass nulls, `ct:`, `prefer` |
| `dotnet-models` | Steps 1, 4–8, 7 — request/response records, StringEnums, Money strings, vault vs PAN |
| `dotnet-error-handling` | All PayPal calls — Case A vs Case B, `TryGetError` vs `TryGetError1`, both `JsonException` directions |
| `dotnet-testing` | Tests for the integration layer |

---

## Assumptions & Blockers

**Assumptions**

- Pay-time AUTHORIZE is a single `CreateOrder` with `Intent = Authorize` and `PaymentSource.Card` (raw PAN **or** `VaultId`). `Orders.AuthorizeOrder` / `Orders.CaptureOrder` are not on this path.
- eShop persists the PayPal ids/statuses listed above; GET my-orders is app-side over that row.
- Reconciliation matches `invoice_id` / `custom_field` (we send eShop order id on `PurchaseUnitRequest`) and stored PayPal capture/order ids.
- Sandbox direct card + vault are enabled as stated. Test card: Visa `4111111111111111`, any future `YYYY-MM`, any CVC.
- Vault `ListCustomerPaymentTokens` is used (SDK has it). We still persist PayPal `customer.id` because list **requires** that id.
- `PayPal:BaseUrl`, when set, is assigned verbatim to `Server.Default.Sandbox.BaseUrl` and therefore applies to token + all API calls.
- Amounts: catalog decimals → `Money.Value` string with the fraction digits of `PayPal:Currency`.

**Blockers**

- **3DS / browser challenge:** If PayPal returns `OrderStatus.PayerActionRequired`, a `payer-action` HATEOAS link, or 3DS `ParesStatus.C`, this integration **must stop and report the gap**. The SDK exposes `CardExperienceContext.ReturnUrl`/`CancelUrl` and `CreateSetupToken`, but the product forbids designing that round-trip. Not a missing-SDK-capability gap until live traffic requires it; it **is** a hard product blocker the moment it appears.
- **No Live environment member:** `ServerEnvironment` only has `Sandbox`. Cannot select live via a generated enum member.
- Reauthorize is **exposed** (`Payments.ReauthorizePayment`). If PayPal rejects reauthorize for card authorizations (typical 422), that is the operator-actionable “cannot renew” outcome — not an SDK gap; do not invent a new authorize as a silent workaround.
- `DeletePaymentToken` has no `payPalRequestId` — local idempotency only.
- `PaymentSource.Token` cannot carry a vault card (`TokenType` is only `BillingAgreement`); vault pay **must** use `CardRequest.VaultId`.
- PCI: `CardRequest` XML notes passing PAN/CVC requires PCI SAQ D. Product asks for direct card on sandbox; still never store full card details in DB or logs.

No SDK gap for: create+authorize with card, capture authorized payment, reauthorize, void, refund + idempotency key, vault save/list/delete, transaction search paging, BaseUrl override including token.

---

## Follow-up contract rows (Q1–Q22)

Cite: `records-1-Ac-Pa.md`, `records-2-Pa-Ve.md`, `enums.md`, `sdk-map.md`; `CardAttributes`/`CardVerification`/`OrderStatus`/`OrdersCardVerificationMethod`/`TypedEnum`/`PurchaseUnitRequest`/`ServiceCollectionExtensions`/`PayPalServerSdkClient` source for equality, DI lifetime, `invoice_id`/`custom_id` lengths, and `"payer-action"` rel text.

| # | Fact |
|---|---|
| Q1 | `CardRequest.Attributes` type: `PayPalServerSdk.Models.CardAttributes?`. Members (all optional): `Customer (customer): PayPalServerSdk.Models.CardCustomerInformation?`, `Vault (vault): PayPalServerSdk.Models.VaultInstructionBase?`, `Verification (verification): PayPalServerSdk.Models.CardVerification?`. Nested `CardVerification`: only `Method (method): PayPalServerSdk.Models.Enums.OrdersCardVerificationMethod? = OrdersCardVerificationMethod.ScaWhenRequired` (not `required`). Nested `CardCustomerInformation`: `Id (id): string?`, `EmailAddress (email_address): string?`, `Phone (phone): PhoneWithType?`, `Name (name): Name?`, `MerchantCustomerId (merchant_customer_id): string?`. Nested `VaultInstructionBase`: `StoreInVault (store_in_vault): StoreInVaultInstruction?`. Set AVS/CVV: `Attributes = new CardAttributes { Verification = new CardVerification { Method = PayPalServerSdk.Models.Enums.OrdersCardVerificationMethod.AvsCvv } }` (wire `AVS_CVV`). Enum members: `ScaAlways (SCA_ALWAYS)`, `ScaWhenRequired (SCA_WHEN_REQUIRED)`, `_3DSecure (3D_SECURE)`, `AvsCvv (AVS_CVV)`. |
| Q2 | `CardRequest.BillingAddress` and `PaymentTokenRequestCard.BillingAddress` are both `PayPalServerSdk.Models.Address?`. `Address` members: `AddressLine1 (address_line_1): string?`, `AddressLine2 (address_line_2): string?`, `AdminArea2 (admin_area_2): string?`, `AdminArea1 (admin_area_1): string?`, `PostalCode (postal_code): string?`, **`CountryCode (country_code): string !req`**. Only `CountryCode` is required, and only if you construct an `Address` (omitting `BillingAddress` entirely is valid). |
| Q3 | `PaymentTokenRequest.Customer`: `PayPalServerSdk.Models.Customer?` — `Id (id): string?`, `MerchantCustomerId (merchant_customer_id): string?`. `PaymentTokenResponse.Customer`: `PayPalServerSdk.Models.CustomerResponse?` — `Id (id): string?`, `MerchantCustomerId (merchant_customer_id): string?`. Neither `Id` nor `MerchantCustomerId` is `required`. |
| Q4 | `PaymentTokenRequest.PaymentSource`: `PayPalServerSdk.Models.PaymentTokenRequestPaymentSource !req`. Members: `Card (card): PayPalServerSdk.Models.PaymentTokenRequestCard?`, `Token (token): PayPalServerSdk.Models.VaultTokenRequest?`. `PaymentTokenRequestCard`: `Name (name): string?`, `Number (number): string?`, `Expiry (expiry): string?`, `SecurityCode (security_code): string?`, `Brand (brand): PayPalServerSdk.Models.Enums.CardBrand?`, `BillingAddress (billing_address): PayPalServerSdk.Models.Address?`. **None of Name/Number/Expiry/SecurityCode/BillingAddress/Brand is `required`.** (`PaymentTokenRequest` itself: `Customer` optional, `PaymentSource` required.) |
| Q5 | `PaymentTokenResponse.PaymentSource`: `PayPalServerSdk.Models.PaymentTokenResponsePaymentSource?`. Members: `Card (card): PayPalServerSdk.Models.CardPaymentTokenEntity?`, `Paypal (paypal): PayPalPaymentToken?`, `Venmo (venmo): VenmoPaymentToken?`, `ApplePay (apple_pay): ApplePayPaymentToken?`. `CardPaymentTokenEntity`: `Name (name): string?`, **`LastDigits (last_digits): string?`**, **`Brand (brand): PayPalServerSdk.Models.Enums.CardBrand?`**, **`Expiry (expiry): string?`**, `BillingAddress (billing_address): CardResponseAddress?`, `VerificationStatus (verification_status): CardVerificationStatus?`, `Verification (verification): CardVerificationDetails?`, `NetworkTransactionReference (network_transaction_reference): NetworkTransactionReferenceEntity?`, `AuthenticationResult (authentication_result): CardAuthenticationResponse?`, `BinDetails (bin_details): BinDetails?`, `Type (type): CardType?`. Brand is `CardBrand?` (not a string). |
| Q6 | `PayPalServerSdk.Models.AmountWithBreakdown`: **`CurrencyCode (currency_code): string !req`**, **`Value (value): string !req`**, `Breakdown (breakdown): AmountBreakdown?`. No other required members. **Yes — construct with only `CurrencyCode` + `Value`.** |
| Q7 | `PayPalServerSdk.Models.PurchaseUnitRequest`: **only `Amount (amount): AmountWithBreakdown !req` is required.** All else optional: `ReferenceId (reference_id): string?`, `Payee (payee): PayeeBase?`, `PaymentInstruction (payment_instruction): PaymentInstruction?`, `Description (description): string?`, `CustomId (custom_id): string?` (max 255, min 1), `InvoiceId (invoice_id): string?` (max 127, min 1), `SoftDescriptor (soft_descriptor): string?`, `Items (items): IReadOnlyList<ItemRequest>?`, `Shipping (shipping): ShippingDetails?`, `SupplementaryData (supplementary_data): SupplementaryData?`. **Yes — pass a numeric eShop order id as a string** (e.g. `InvoiceId = order.Id.ToString()`, `CustomId = order.Id.ToString()`). No digit-only restriction on the type; keep length ≥ 1. |
| Q8 | `CapturedPayment.SellerReceivableBreakdown` (wire `seller_receivable_breakdown`): `PayPalServerSdk.Models.SellerReceivableBreakdown?`. Nested: **`GrossAmount (gross_amount): PayPalServerSdk.Models.Money !req`**, **`PaypalFee (paypal_fee): Money?`**, `PaypalFeeInReceivableCurrency (paypal_fee_in_receivable_currency): Money?`, **`NetAmount (net_amount): Money?`**, `ReceivableAmount (receivable_amount): Money?`, `ExchangeRate (exchange_rate): ExchangeRate?`, `PlatformFees (platform_fees): IReadOnlyList<PlatformFee>?`. Property names are `SellerReceivableBreakdown`, `PaypalFee`, `NetAmount`, `GrossAmount` (not `PayPalFee`). |
| Q9 | `Order.Links`: `IReadOnlyList<PayPalServerSdk.Models.LinkDescription>?`. Item: `LinkDescription` with `Href (href): string !req`, **`Rel (rel): string !req`** (not an enum), `Method (method): PayPalServerSdk.Models.Enums.LinkHttpMethod?`. Detect 3DS/challenge link: `link.Rel == "payer-action"` (exact string). `OrderStatus.PayerActionRequired` XML names rel `"payer-action"`. |
| Q10 | CreateOrder `Order.PurchaseUnits[].Payments.Authorizations` is `IReadOnlyList<PayPalServerSdk.Models.AuthorizationWithAdditionalData>?` — **not** `PaymentAuthorization`. Shared readable fields (all nullable on both): `Id (id): string?`, `Status (status): AuthorizationStatus?`, `ExpirationTime (expiration_time): string?`, `Amount (amount): Money?`, plus `StatusDetails`, `InvoiceId`, `CustomId`, `NetworkTransactionReference`, `SellerProtection`, `Links`, `CreateTime`, `UpdateTime`. Extra on `AuthorizationWithAdditionalData` only: `ProcessorResponse (processor_response): ProcessorResponse?`. Extra on `PaymentAuthorization` (Get/Reauthorize/Void): `SupplementaryData`, `Payee`. Same C# names for Id/Status/ExpirationTime/Amount. |
| Q11 | `PayPalServerSdk.Models.Money`: **`CurrencyCode (currency_code): string !req`**, **`Value (value): string !req`**. **Yes — construct with only those two.** |
| Q12 | `PayPalServerSdk.Models.ReauthorizeRequest`: only `Amount (amount): Money?` (optional). **Yes — `new ReauthorizeRequest { Amount = new Money { CurrencyCode = …, Value = … } }`.** Empty `new ReauthorizeRequest()` also compiles; pass original order total as `Amount`. |
| Q13 | `PayPalServerSdk.Models.RefundRequest`: **no required members.** `Amount (amount): Money?`, `CustomId (custom_id): string?`, `InvoiceId (invoice_id): string?`, `NoteToPayer (note_to_payer): string?`, `PaymentInstruction (payment_instruction): RefundPaymentInstruction?`. **Full refund: `body: null` or `new RefundRequest()` (omit `Amount`). Partial: set `Amount` to `Money`.** |
| Q14 | Do **not** set `CardRequest.ExperienceContext` (`PayPalServerSdk.Models.CardExperienceContext?` — `ReturnUrl (return_url): string?`, `CancelUrl (cancel_url): string?`). Do **not** set `OrderRequest.ApplicationContext.ReturnUrl`/`CancelUrl`. Do **not** set `CardAttributes.Customer` / `CardAttributes.Vault` unless vault-on-pay is intended. **Omission is valid** — all of those properties are optional/`?`. Setting only `CardAttributes.Verification.Method = AvsCvv` does not require ReturnUrl. |
| Q15 | `AddPayPalServerSdkClient` registers **`PayPalServerSdkClient` as Singleton** (`services.AddSingleton(...)`) and uses `IHttpClientFactory.CreateClient()` with the **unnamed** client. Ctor is exactly `PayPalServerSdk.PayPalServerSdkClient(System.Net.Http.HttpClient httpClient, PayPalServerSdk.PayPalServerSdkClientOptions options)`. Options property **`Oauth2`**: `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials?` with `required string ClientId`, `required string ClientSecret`, `string? Scope`. To supply your own named `HttpClient`, **do not use** `AddPayPalServerSdkClient` for that client — `new PayPalServerSdkClient(namedHttpClient, options)` instead. **MUST load `dotnet-client-initialization`** before choosing that registration. |
| Q16 | `Order.Status` type: **`PayPalServerSdk.Models.Enums.OrderStatus?`** (nullable). `OrderStatus` is a `sealed record` : `StringEnum<OrderStatus>` with `Value: string`. **`order.Status == OrderStatus.PayerActionRequired` is valid** (record equality on `Value`). Null status ⇒ comparison is false. Member `PayerActionRequired` wire `PAYER_ACTION_REQUIRED`. **MUST load `dotnet-models`** before treating StringEnums as C# enums. |
| Q17 | `PaymentTokenResponse.Id`: **`string?`** (wire `id`). Not `required`. Null-check before persisting/using as `VaultId`. |
| Q18 | `SearchResponse.TransactionDetails` (wire `transaction_details`): `IReadOnlyList<PayPalServerSdk.Models.TransactionDetails>?`. Item type **`TransactionDetails`**. Path: `SearchResponse.TransactionDetails[i].TransactionInfo` (wire `transaction_info`) typed **`PayPalServerSdk.Models.TransactionInformation?`**. There is no property named `TransactionInformation` on `SearchResponse`. `SearchResponse` also has `Page (page): int?`, `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`. |
| Q19 | `PayPalServerSdk.Models.LinkDescription`: `Href (href): string !req`, `Rel (rel): string !req`, `Method (method): LinkHttpMethod?`. On `Order.Links`. Compare `Rel` as string (`"payer-action"`), not an enum. |
| Q20 | `PayPalServerSdk.Models.Error` (Orders/Payments `TryGetError`): `Name (name): string !req`, `Message (message): string !req`, `DebugId (debug_id): string !req`, `Details (details): IReadOnlyList<PayPalServerSdk.Models.ErrorDetails>?`, `Links (links): IReadOnlyList<LinkDescription>?`. `ErrorDetails`: `Field (field): string?`, `Value (value): string?`, `Location (location): string? = "body"`, **`Issue (issue): string !req`**, `Links (links): IReadOnlyList<LinkDescription>?`, `Description (description): string?`. Vault `TryGetError1`: `PayPalServerSdk.Models.Error1` — same Name/Message/DebugId required; `Details (details): IReadOnlyList<ErrorDetails1>?`; `Links (links): IReadOnlyList<ErrorLinkDescription>?`. `ErrorDetails1`: `Field: string?`, `Value: string?`, `Location: string? = "body"`, **`Issue: string !req`**, `Links: IReadOnlyList<ErrorLinkDescription>?`, `Description: string?`. `ErrorLinkDescription.Rel` is `string?` (optional), unlike `LinkDescription.Rel`. |
| Q21 | **`CardRequest` has no `required` members.** All 11 properties are `?`. **`new CardRequest { VaultId = tokenId }` is valid** (omit Number/Expiry/SecurityCode/Name/BillingAddress). |
| Q22 | `PayPalServerSdk.Models.CustomerVaultPaymentTokensResponse`: `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`, `Customer (customer): VaultResponseCustomer?`, `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?`, `Links (links): IReadOnlyList<LinkDescription>?`. **There is no `Page` property on this response** (unlike `SearchResponse.Page`). Request paging is `ListCustomerPaymentTokens(..., pageSize, page, totalRequired)` — loop `page` yourself using `TotalPages`. |
