# PayPal Integration Plan — eShopOnWeb

## 1. Scope & Sequence

| Step | Work | Operations used |
|---|---|---|
| 1 | Install package, register client & auth in DI | `AddPayPalServerSdkClient`, `PayPalServerSdkClientOptions` |
| 2 | Authorize one-off card payment | `client.Orders.CreateOrder` → `client.Orders.AuthorizeOrder` |
| 3 | Authorize with vaulted card | `client.Orders.CreateOrder` → `client.Orders.AuthorizeOrder` (VaultId variant) |
| 4 | Capture an authorization | `client.Payments.CaptureAuthorizedPayment` |
| 5 | Void an authorization | `client.Payments.VoidPayment` |
| 6 | Re-authorize a stale authorization | `client.Payments.ReauthorizePayment` |
| 7 | Refund a capture | `client.Payments.RefundCapturedPayment` |
| 8 | Transaction reconciliation (paginated) | `client.TransactionSearch.SearchTransactions` (manual page loop) |
| 9 | Vault a card | `client.Vault.CreatePaymentToken` |
| 10 | List vaulted cards | `client.Vault.ListCustomerPaymentTokens` (manual page loop) |
| 11 | Delete a vault token | `client.Vault.DeletePaymentToken` |
| 12 | Error boundary | `SdkException<T>` + `TryGet…` accessors — see CONTRACT SHEET below |

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier.
> The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each
> one from that type's own map row, never from where a neighbouring type sits. Enums, unions,
> auth, server and client-config types are spread across different child namespaces. Dropping a
> type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build
> breaks.

### NuGet package

```
dotnet add package AsadAli.Checkout.Sdk
```

Install version-less (no pinned version). Source: `paypal-getting-started` sdk-map.md.

---

### Namespaces (add a separate `using` for each)

| Contents | Namespace |
|---|---|
| Client class, options class | `PayPalServerSdk` |
| `ServerEnvironment` | `PayPalServerSdk.Servers` |
| All record models | `PayPalServerSdk.Models` |
| All enum types | `PayPalServerSdk.Models.Enums` |
| Error classes (`AuthorizeOrderError`, `CreateOrderError`, …) | `PayPalServerSdk.Errors` |
| `SdkException<T>`, `RawError` | load `dotnet-error-handling` for the exact namespace |
| `OAuth2ClientCredentials` | load `dotnet-authentication` for the exact namespace |

Source: `sdk-map.md` (Namespaces by content type).

---

### Client construction & auth

| Fact | Value | Source |
|---|---|---|
| Client class | `PayPalServerSdkClient` | `sdk-map.md` |
| Options class | `PayPalServerSdkClientOptions` | `sdk-map.md` |
| DI extension | `services.AddPayPalServerSdkClient(o => { … })` | `sdk-map.md` |
| Constructor | `new PayPalServerSdkClient(httpClient, options)` where `httpClient: System.Net.Http.HttpClient` | `sdk-map.md` |
| Sandbox environment | `options.Environment = ServerEnvironment.Sandbox` | `sdk-map.md` (Servers & auth) |
| Auth credential property | `options.Oauth2 = new OAuth2ClientCredentials { … }` | `sdk-map.md` (Servers & auth) |
| Base-URL override | `options.Server.Default.Sandbox.BaseUrl = "<url>"` — this single property overrides BOTH all API call base URLs AND the OAuth2 token endpoint (`/v1/oauth2/token`) because auth uses the same `server.Default(…)` path internally. There is no separate token-endpoint override. Namespace: `PayPalServerSdk` (`ServerOptions`) + `PayPalServerSdk.Servers` (`DefaultOptions`). | `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `AuthSchemes.cs` |

**Configuration keys → options wiring:**

| Config key | Maps to |
|---|---|
| `PayPal:ClientId` | credential property on `OAuth2ClientCredentials` — load `dotnet-authentication` for field name |
| `PayPal:ClientSecret` | credential property on `OAuth2ClientCredentials` — load `dotnet-authentication` for field name |
| `PayPal:Environment` | compare to `"Sandbox"`, set `options.Environment = ServerEnvironment.Sandbox` |
| `PayPal:Currency` | stored in application config, passed as `CurrencyCode` in `Money` / `AmountWithBreakdown` |
| `PayPal:BaseUrl` | applied via `options.Server` — load `dotnet-configuration-resilience` for the override field name |

None of these are hard-coded — all come from `IConfiguration` / environment variables.

---

### Step 2 — Authorize a one-off card payment

Two SDK calls: `CreateOrder` then `AuthorizeOrder`.

**2a. CreateOrder**

Controller property: `client.Orders` · Source: `map/operations/Orders.md`

```
Task<Order> CreateOrder(
    string? payPalMockResponse,
    string? payPalRequestId,
    string? payPalPartnerAttributionId,
    string? payPalClientMetadataId,
    string? payPalAuthAssertion,
    OrderRequest body,
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

All five header params before `body` are nullable with no default — **must pass explicitly**
(pass `null` to skip).

**Idempotency:** Pass the eShop order ID as `payPalRequestId` to prevent double-authorization on
double-click. PayPal deduplicates calls sharing the same `payPalRequestId` from the same client.

Request model — `OrderRequest` (`PayPalServerSdk.Models`):

| C# property | Wire name | Type | Required? |
|---|---|---|---|
| `Intent` | `intent` | `CheckoutPaymentIntent` | yes |
| `PurchaseUnits` | `purchase_units` | `IReadOnlyList<PurchaseUnitRequest>` | yes |
| `PaymentSource` | `payment_source` | `PaymentSource?` | no — omit here; supply card in AuthorizeOrder |

`PurchaseUnitRequest` (`PayPalServerSdk.Models`):

| C# property | Wire name | Type | Required? |
|---|---|---|---|
| `Amount` | `amount` | `AmountWithBreakdown` | yes |
| `ReferenceId` | `reference_id` | `string?` | no — use eShop order ID for traceability |
| `CustomId` | `custom_id` | `string?` | no |
| `InvoiceId` | `invoice_id` | `string?` | no |

`AmountWithBreakdown` (`PayPalServerSdk.Models`):

| C# property | Wire name | Type | Required? |
|---|---|---|---|
| `CurrencyCode` | `currency_code` | `string` | yes |
| `Value` | `value` | `string` | yes — decimal string e.g. `"10.00"` |

Enum: `CheckoutPaymentIntent.Authorize` (wire: `AUTHORIZE`) — from `PayPalServerSdk.Models.Enums`.
Source: `map/models/enums.md`.

Response: `Order` (`PayPalServerSdk.Models`)

| C# property | Purpose |
|---|---|
| `Id` | PayPal order ID — pass to AuthorizeOrder |
| `Status` | `OrderStatus` — must not be `PAYER_ACTION_REQUIRED` (see Blockers) |

Error: `SdkException<CreateOrderError>` — **Case A**
- `TryGetError(out Error typed)` — statuses 400, 401, 422
- `TryGetRawError(out RawError raw)` — fallback

Source: `map/operations/Orders.md`.

---

**2b. AuthorizeOrder (one-off card)**

```
Task<OrderAuthorizeResponse> AuthorizeOrder(
    string id,
    string? payPalMockResponse,
    string? payPalRequestId,
    string? payPalClientMetadataId,
    string? payPalAuthAssertion,
    OrderAuthorizeRequest? body,
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Five params after `id` (`payPalMockResponse` … `body`) are nullable with no default —
**must pass explicitly**.

**Idempotency:** Pass the same eShop order ID as `payPalRequestId`.

Request model — `OrderAuthorizeRequest` (`PayPalServerSdk.Models`):

| C# property | Wire name | Type | Required? |
|---|---|---|---|
| `PaymentSource` | `payment_source` | `OrderAuthorizeRequestPaymentSource?` | no (required in practice for no-redirect flow) |

`OrderAuthorizeRequestPaymentSource` (`PayPalServerSdk.Models`):

| C# property | Wire name | Type |
|---|---|---|
| `Card` | `card` | `CardRequest?` |
| `Token` | `token` | `Token?` |

`CardRequest` (`PayPalServerSdk.Models`) — for raw card details:

| C# property | Wire name | Type | Required? | Notes |
|---|---|---|---|---|
| `Name` | `name` | `string?` | no | Cardholder name |
| `Number` | `number` | `string?` | no | Raw PAN — PCI SAQ D required |
| `Expiry` | `expiry` | `string?` | no | `YYYY-MM` format |
| `SecurityCode` | `security_code` | `string?` | no | CVC |
| `BillingAddress` | `billing_address` | `Address?` | no | |
| `VaultId` | `vault_id` | `string?` | no | Set for vaulted card (Step 3) |
| `Attributes` | `attributes` | `CardAttributes?` | no | |

Sandbox test card: Number `4111111111111111`, Expiry any future `YYYY-MM`, SecurityCode any 3 digits.

Response: `OrderAuthorizeResponse` (`PayPalServerSdk.Models`)

| C# property | Purpose |
|---|---|
| `Id` | PayPal order ID |
| `Status` | `OrderStatus` — check not `PAYER_ACTION_REQUIRED` |
| `PurchaseUnits[0].Payments.Authorizations[0].Id` | **Authorization ID** — persist this |
| `PurchaseUnits[0].Payments.Authorizations[0].Status` | `AuthorizationStatus` |
| `PurchaseUnits[0].Payments.Authorizations[0].ExpirationTime` | ISO-8601 expiry string |

Navigation path for authorization ID:
`OrderAuthorizeResponse` → `.PurchaseUnits` (`IReadOnlyList<PurchaseUnit>`) → `[0].Payments`
(`PaymentCollection`) → `.Authorizations` (`IReadOnlyList<AuthorizationWithAdditionalData>`) →
`[0].Id` (`string?`)

`AuthorizationStatus` enum values (`PayPalServerSdk.Models.Enums`):
`Created`, `Captured`, `Denied`, `PartiallyCaptured`, `Voided`, `Pending`
Source: `map/models/enums.md`.

Error: `SdkException<AuthorizeOrderError>` — **Case A**
- `TryGetError(out Error typed)` — statuses 400, 401, 403, 404, 422, 500
- `TryGetRawError(out RawError raw)` — fallback

Source: `map/operations/Orders.md`.

---

### Step 3 — Authorize with a vaulted card

Same two-step flow as Step 2. `CreateOrder` is identical. `AuthorizeOrder` differs only in the
payment source: use `CardRequest.VaultId` instead of raw card fields.

```csharp
body: new OrderAuthorizeRequest
{
    PaymentSource = new OrderAuthorizeRequestPaymentSource
    {
        Card = new CardRequest { VaultId = vaultTokenId }
    }
}
```

`vaultTokenId` is the `Id` returned from `CreatePaymentToken` (Step 9). No raw card details
are sent; no raw details are stored by the application.

Source: `map/models/records-1-Ac-Pa.md` (`CardRequest`, `OrderAuthorizeRequestPaymentSource`).

---

### Step 4 — Capture an authorization

Controller property: `client.Payments` · Source: `map/operations/Payments.md`

```
Task<CapturedPayment> CaptureAuthorizedPayment(
    string authorizationId,
    string? payPalMockResponse,
    string? payPalRequestId,
    string? payPalAuthAssertion,
    CaptureRequest? body,
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Four params after `authorizationId` (`payPalMockResponse` … `body`) are nullable with no
default — **must pass explicitly**.

`CaptureRequest` (`PayPalServerSdk.Models`):

| C# property | Wire name | Type | Notes |
|---|---|---|---|
| `Amount` | `amount` | `Money?` | Omit or null for full capture |
| `FinalCapture` | `final_capture` | `bool? = false` | Set `true` to prevent further captures |
| `InvoiceId` | `invoice_id` | `string?` | Optional reference |
| `NoteToPayer` | `note_to_payer` | `string?` | Optional |

Response: `CapturedPayment` (`PayPalServerSdk.Models`)

| C# property | Wire name | Purpose — persist these |
|---|---|---|
| `Id` | `id` | Capture ID |
| `Status` | `status` | `CaptureStatus` |
| `Amount` | `amount` | Captured `Money` (`CurrencyCode`, `Value`) |
| `SellerReceivableBreakdown.GrossAmount` | `gross_amount` | Gross captured amount |
| `SellerReceivableBreakdown.PaypalFee` | `paypal_fee` | PayPal fee (`Money?`) |
| `SellerReceivableBreakdown.NetAmount` | `net_amount` | Net amount (`Money?`) |
| `CreateTime` | `create_time` | ISO-8601 timestamp |

`CaptureStatus` enum values (`PayPalServerSdk.Models.Enums`):
`Completed`, `Declined`, `PartiallyRefunded`, `Pending`, `Refunded`, `Failed`
Source: `map/models/enums.md`.

Error: `SdkException<CaptureAuthorizedPaymentError>` — **Case A**
- `TryGetError(out Error typed)` — statuses 400, 401, 403, 404, 409, 422
- `TryGetNoContent(out RawError raw)` — status 500
- `TryGetRawError(out RawError raw)` — fallback

Source: `map/operations/Payments.md`.

---

### Step 5 — Void an authorization

```
Task<PaymentAuthorization> VoidPayment(
    string authorizationId,
    string? payPalMockResponse,
    string? payPalAuthAssertion,
    string? payPalRequestId,
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

**Parameter order note:** `payPalMockResponse`, `payPalAuthAssertion`, `payPalRequestId` are the
three nullable must-pass-explicitly params (in that exact order — do not permute).

Response: `PaymentAuthorization` (`PayPalServerSdk.Models`)

| C# property | Purpose |
|---|---|
| `Id` | Authorization ID |
| `Status` | Should be `AuthorizationStatus.Voided` on success |

Error: `SdkException<VoidPaymentError>` — **Case A**
- `TryGetError(out Error typed)` — statuses 401, 403, 404, 409, 422
- `TryGetNoContent(out RawError raw)` — status 500
- `TryGetRawError(out RawError raw)` — fallback

Source: `map/operations/Payments.md`.

---

### Step 6 — Re-authorize a stale authorization

```
Task<PaymentAuthorization> ReauthorizePayment(
    string authorizationId,
    string? payPalRequestId,
    string? payPalAuthAssertion,
    ReauthorizeRequest? body,
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Three params after `authorizationId` (`payPalRequestId`, `payPalAuthAssertion`, `body`) are
nullable with no default — **must pass explicitly**.

`ReauthorizeRequest` (`PayPalServerSdk.Models`):

| C# property | Wire name | Type | Notes |
|---|---|---|---|
| `Amount` | `amount` | `Money?` | New amount; may be up to 115% of original (US), max +$75 |

**Re-authorization constraints (from API notes):**
- Valid window: 4 to 29 days after the original 3-day honor period (i.e., days 4–29 after original auth).
- If > 30 days elapsed: re-authorization is impossible — a 422 is returned. The integration MUST
  catch the 422 error and surface a clear message: "Re-authorization is not possible; create a new
  authorization instead." Do not retry automatically.
- A re-authorized payment itself has a new 3-day honor period.

Response: `PaymentAuthorization` (`PayPalServerSdk.Models`) — same fields as Step 5.

Error: `SdkException<ReauthorizePaymentError>` — **Case A**
- `TryGetError(out Error typed)` — statuses 400, 401, 403, 404, 422 (**422 = impossible**)
- `TryGetNoContent(out RawError raw)` — status 500
- `TryGetRawError(out RawError raw)` — fallback

The 422 typed error payload is `Error` (`PayPalServerSdk.Models`): `Name`, `Message`, `DebugId`,
`Details` (`IReadOnlyList<ErrorDetails>?`). Surface `Name` + `Message` to the caller.

Source: `map/operations/Payments.md`.

---

### Step 7 — Refund a capture

```
Task<Refund> RefundCapturedPayment(
    string captureId,
    string? payPalMockResponse,
    string? payPalRequestId,
    string? payPalAuthAssertion,
    RefundRequest? body,
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Four params after `captureId` (`payPalMockResponse` … `body`) are nullable with no default —
**must pass explicitly**.

**Idempotency:** Pass a caller-supplied idempotency key as `payPalRequestId`. Rules:
- Same key on a retry → PayPal returns the same refund (no double-refund).
- A distinct partial refund of the same capture must use a **different** key.
- The caller is responsible for generating and storing unique keys per refund intent.

`RefundRequest` (`PayPalServerSdk.Models`):

| C# property | Wire name | Type | Notes |
|---|---|---|---|
| `Amount` | `amount` | `Money?` | Null or omit = full refund; set for partial |
| `CustomId` | `custom_id` | `string?` | Optional local reference |
| `NoteToPayer` | `note_to_payer` | `string?` | Optional |

Response: `Refund` (`PayPalServerSdk.Models`)

| C# property | Wire name | Purpose — persist these |
|---|---|---|
| `Id` | `id` | Refund ID |
| `Status` | `status` | `RefundStatus` |
| `Amount` | `amount` | Refunded `Money` |
| `SellerPayableBreakdown.GrossAmount` | `gross_amount` | Gross refund amount (`Money?`) |
| `SellerPayableBreakdown.PaypalFee` | `paypal_fee` | Fee reversed (`Money?`) |
| `SellerPayableBreakdown.NetAmount` | `net_amount` | Net refund (`Money?`) |
| `CreateTime` | `create_time` | ISO-8601 timestamp |

`RefundStatus` enum values (`PayPalServerSdk.Models.Enums`):
`Cancelled`, `Failed`, `Pending`, `Completed`
Source: `map/models/enums.md`.

Error: `SdkException<RefundCapturedPaymentError>` — **Case A**
- `TryGetError(out Error typed)` — statuses 400, 401, 403, 404, 409, 422
- `TryGetNoContent(out RawError raw)` — status 500
- `TryGetRawError(out RawError raw)` — fallback

Note: A 409 typically means a conflicting refund request (concurrent). Surface the error rather
than retrying automatically.

Source: `map/operations/Payments.md`.

---

### Step 8 — Transaction reconciliation report (paginated)

Controller property: `client.TransactionSearch` · Source: `map/operations/TransactionSearch.md`

```
Task<SearchResponse> SearchTransactions(
    string startDate,
    string endDate,
    string? transactionId,
    string? transactionType,
    string? transactionStatus,
    string? transactionAmount,
    string? transactionCurrency,
    string? paymentInstrumentType,
    string? storeId,
    string? terminalId,
    string? fields = "transaction_info",
    string? balanceAffectingRecordsOnly = "Y",
    int? pageSize = 100,
    int? page = 1,
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Eight params (`transactionId` … `terminalId`) are nullable with no default — **must pass
explicitly** (pass `null` for each to use defaults for the others).

**Date format:** ISO-8601 with timezone offset, e.g. `"2024-01-01T00:00:00-0000"`. The API
requires both `startDate` and `endDate`.

**Full-range pagination pattern** — the SDK has no built-in cursor/iterator:

```
page 1: call with page = 1
read SearchResponse.TotalPages
loop page = 2 .. TotalPages: call again with page = N
aggregate all SearchResponse.TransactionDetails lists
```

Response: `SearchResponse` (`PayPalServerSdk.Models`)

| C# property | Wire name | Purpose |
|---|---|---|
| `TransactionDetails` | `transaction_details` | `IReadOnlyList<TransactionDetails>?` — aggregate across pages |
| `TotalPages` | `total_pages` | `int?` — page-loop upper bound |
| `TotalItems` | `total_items` | `int?` — total record count |
| `Page` | `page` | `int?` — current page (for verification) |

`TransactionDetails` → `.TransactionInfo` (`TransactionInformation?`):

| C# property | Wire name | Purpose — eShop correlation |
|---|---|---|
| `TransactionId` | `transaction_id` | PayPal transaction ID |
| `PaypalReferenceId` | `paypal_reference_id` | Reference to order/auth/capture |
| `PaypalReferenceIdType` | `paypal_reference_id_type` | `PayPalReferenceIdType?` |
| `TransactionAmount` | `transaction_amount` | `Money?` |
| `FeeAmount` | `fee_amount` | `Money?` |
| `TransactionStatus` | `transaction_status` | `string?` |
| `TransactionInitiationDate` | `transaction_initiation_date` | ISO-8601 string |
| `InvoiceId` | `invoice_id` | `string?` — use for eShop order correlation |
| `CustomField` | `custom_field` | `string?` |

**Error — Case B (raw):** `SdkException<RawError>`
- `ex.Error.StatusCode` (`HttpStatusCode`)
- `ex.Error.ReadAsString()` — raw response body
- `ex.Error.ReadAsJson<T>()` — parse if structured error is expected

Source: `map/operations/TransactionSearch.md`.

---

### Step 9 — Vault a card

Controller property: `client.Vault` · Source: `map/operations/Vault.md`

```
Task<PaymentTokenResponse> CreatePaymentToken(
    string? payPalRequestId,
    PaymentTokenRequest body,
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

`payPalRequestId` is nullable with no default — **must pass explicitly**.

**Idempotency:** Pass a unique key per vault-card operation as `payPalRequestId`.

`PaymentTokenRequest` (`PayPalServerSdk.Models`):

| C# property | Wire name | Type | Required? |
|---|---|---|---|
| `Customer` | `customer` | `Customer?` | no — set to associate with a customer |
| `PaymentSource` | `payment_source` | `PaymentTokenRequestPaymentSource` | yes |

`Customer` (`PayPalServerSdk.Models`):

| C# property | Wire name | Type |
|---|---|---|
| `Id` | `id` | `string?` — PayPal customer ID (if known) |
| `MerchantCustomerId` | `merchant_customer_id` | `string?` — your system's customer ID |

`PaymentTokenRequestPaymentSource` (`PayPalServerSdk.Models`):

| C# property | Wire name | Type |
|---|---|---|
| `Card` | `card` | `PaymentTokenRequestCard?` |

`PaymentTokenRequestCard` (`PayPalServerSdk.Models`):

| C# property | Wire name | Type | Notes |
|---|---|---|---|
| `Name` | `name` | `string?` | Cardholder name |
| `Number` | `number` | `string?` | Raw PAN — sent to PayPal, never stored by app |
| `Expiry` | `expiry` | `string?` | `YYYY-MM` format |
| `SecurityCode` | `security_code` | `string?` | CVC — sent to PayPal, never stored by app |
| `BillingAddress` | `billing_address` | `Address?` | |

Response: `PaymentTokenResponse` (`PayPalServerSdk.Models`)

| C# property | Wire name | Purpose — safe descriptor to persist |
|---|---|---|
| `Id` | `id` | Vault token ID (`string?`) — persist this |
| `Customer.Id` | `id` | PayPal customer ID (`string?`) |
| `Customer.MerchantCustomerId` | `merchant_customer_id` | Your customer ID echo |
| `PaymentSource.Card.LastDigits` | `last_digits` | Last 4 digits (`string?`) |
| `PaymentSource.Card.Brand` | `brand` | `CardBrand?` |
| `PaymentSource.Card.Expiry` | `expiry` | `YYYY-MM` (`string?`) |

`PaymentSource` here is `PaymentTokenResponsePaymentSource` → `.Card` is `CardPaymentTokenEntity`.
Source: `map/models/records-2-Pa-Ve.md` (`PaymentTokenResponse`, `PaymentTokenResponsePaymentSource`,
`CardPaymentTokenEntity`).

Error: `SdkException<CreatePaymentTokenError>` — **Case A**
- `TryGetError1(out Error1 typed)` — statuses 400, 403, 404, 422, 500
- `TryGetRawError(out RawError raw)` — fallback

Note: The accessor is `TryGetError1` (not `TryGetError`) and the out type is `Error1`
(not `Error`) — these are distinct types for the Vault controller.

`Error1` (`PayPalServerSdk.Models`): `Name (name): string !req`, `Message (message): string !req`,
`DebugId (debug_id): string !req`, `Details (details): IReadOnlyList<ErrorDetails1>?`
Source: `map/models/records-1-Ac-Pa.md`.

Source: `map/operations/Vault.md`.

---

### Step 10 — List vaulted cards

```
Task<CustomerVaultPaymentTokensResponse> ListCustomerPaymentTokens(
    string customerId,
    int? pageSize = 5,
    int? page = 1,
    bool? totalRequired = false,
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Pass `totalRequired: true` to receive `TotalPages` for pagination.

**Query wire names:** `customer_id` ← `customerId`, `page_size` ← `pageSize`, `page` ← `page`,
`total_required` ← `totalRequired`.

**Pagination:** Same manual loop pattern as Step 8 — loop page 1..TotalPages.

Response: `CustomerVaultPaymentTokensResponse` (`PayPalServerSdk.Models`)

| C# property | Wire name | Purpose |
|---|---|---|
| `PaymentTokens` | `payment_tokens` | `IReadOnlyList<PaymentTokenResponse>?` |
| `TotalItems` | `total_items` | `int?` |
| `TotalPages` | `total_pages` | `int?` |
| `Customer` | `customer` | `VaultResponseCustomer?` |

Each `PaymentTokenResponse` in the list has the same fields as Step 9's response.

Error: `SdkException<ListCustomerPaymentTokensError>` — **Case A**
- `TryGetError1(out Error1 typed)` — statuses 400, 403, 500
- `TryGetRawError(out RawError raw)` — fallback

Source: `map/operations/Vault.md`.

---

### Step 11 — Delete a vault token

```
Task DeletePaymentToken(
    string id,
    RequestOptions? requestOptions = null,
    CancellationToken ct = default)
```

Returns `void` (Task). No response body on success.

Error: `SdkException<DeletePaymentTokenError>` — **Case A**
- `TryGetError1(out Error1 typed)` — statuses 400, 403, 500
- `TryGetRawError(out RawError raw)` — fallback

Source: `map/operations/Vault.md`.

---

### Idempotency keys — summary table

| Operation | SDK parameter | Caller supplies |
|---|---|---|
| CreateOrder | `payPalRequestId` | eShop order ID |
| AuthorizeOrder | `payPalRequestId` | same eShop order ID |
| RefundCapturedPayment | `payPalRequestId` | caller-supplied key per refund intent |
| CreatePaymentToken | `payPalRequestId` | unique key per vault call |

Double-click prevention for authorization: both `CreateOrder` and `AuthorizeOrder` receive the same
`payPalRequestId` (the eShop order ID). If `CreateOrder` succeeds but `AuthorizeOrder` is retried,
PayPal's idempotency layer returns the original response without re-authorizing.

---

### Enum value table — values used in this integration

| Enum | Namespace | Members used | Wire values |
|---|---|---|---|
| `CheckoutPaymentIntent` | `PayPalServerSdk.Models.Enums` | `Authorize` | `AUTHORIZE` |
| `AuthorizationStatus` | `PayPalServerSdk.Models.Enums` | `Created`, `Captured`, `Denied`, `PartiallyCaptured`, `Voided`, `Pending` | as shown |
| `CaptureStatus` | `PayPalServerSdk.Models.Enums` | `Completed`, `Declined`, `PartiallyRefunded`, `Pending`, `Refunded`, `Failed` | as shown |
| `RefundStatus` | `PayPalServerSdk.Models.Enums` | `Cancelled`, `Failed`, `Pending`, `Completed` | as shown |
| `OrderStatus` | `PayPalServerSdk.Models.Enums` | `Created`, `Approved`, `Voided`, `Completed`, `PayerActionRequired` | `PAYER_ACTION_REQUIRED` is the 3DS blocker status |
| `PaymentTokenStatus` | `PayPalServerSdk.Models.Enums` | `Created`, `PayerActionRequired`, `Vaulted` | |
| `StoreInVaultInstruction` | `PayPalServerSdk.Models.Enums` | `OnSuccess` | `ON_SUCCESS` |

Source: `map/models/enums.md`.

---

### Error type reference — all operations

| Operation | Error type | Accessor(s) | Payload type |
|---|---|---|---|
| `CreateOrder` | `CreateOrderError` | `TryGetError(out Error)` [400,401,422] | `Error` |
| `AuthorizeOrder` | `AuthorizeOrderError` | `TryGetError(out Error)` [400,401,403,404,422,500] | `Error` |
| `CaptureAuthorizedPayment` | `CaptureAuthorizedPaymentError` | `TryGetError(out Error)` [400-422] · `TryGetNoContent(out RawError)` [500] | `Error` / `RawError` |
| `VoidPayment` | `VoidPaymentError` | `TryGetError(out Error)` [401-422] · `TryGetNoContent(out RawError)` [500] | `Error` / `RawError` |
| `ReauthorizePayment` | `ReauthorizePaymentError` | `TryGetError(out Error)` [400-422] · `TryGetNoContent(out RawError)` [500] | `Error` / `RawError` |
| `RefundCapturedPayment` | `RefundCapturedPaymentError` | `TryGetError(out Error)` [400-422] · `TryGetNoContent(out RawError)` [500] | `Error` / `RawError` |
| `SearchTransactions` | `RawError` (Case B) | `.StatusCode` · `.ReadAsString()` · `.ReadAsJson<T>()` | `RawError` |
| `CreatePaymentToken` | `CreatePaymentTokenError` | `TryGetError1(out Error1)` [400-500] | `Error1` |
| `ListCustomerPaymentTokens` | `ListCustomerPaymentTokensError` | `TryGetError1(out Error1)` [400-500] | `Error1` |
| `DeletePaymentToken` | `DeletePaymentTokenError` | `TryGetError1(out Error1)` [400-500] | `Error1` |

All error classes are in `PayPalServerSdk.Errors`. All are Case A except `SearchTransactions`
(Case B). **No no-throw variants exist anywhere in this SDK.**

`Error` (`PayPalServerSdk.Models`): `Name !req`, `Message !req`, `DebugId !req`,
`Details: IReadOnlyList<ErrorDetails>?`

`Error1` (`PayPalServerSdk.Models`): `Name !req`, `Message !req`, `DebugId !req`,
`Details: IReadOnlyList<ErrorDetails1>?`

Source: `sdk-map.md` (error-handling model) + `map/operations/*.md` (per-operation rows) +
`map/models/records-1-Ac-Pa.md` (`Error`, `Error1`).

---

## 3. Trap Notes

> ⚠ Step 1 (client registration) — the SDK's `Timeout` option does **not** bound a whole call
> and is **not** the same as the `HttpClient` timeout; `HttpMethodsToRetry` gates only the status
> trigger, but transport failures (`HttpRequestException`) are retried on every verb including
> `POST`, so non-idempotent writes can execute more than once if retries are not configured
> carefully. **MUST load `dotnet-configuration-resilience`** before wiring the client options.

> ⚠ Step 1 (auth) — `OAuth2ClientCredentials` credentials properties (field names for ClientId
> and ClientSecret) are not shown in the map's namespace table; the auth token endpoint is also
> not the same as the API base URL, and `PayPal:BaseUrl` must be applied to the right override
> point. **MUST load `dotnet-authentication`** before setting credentials or overriding the base
> URL.

> ⚠ Step 1 (DI / HttpClient lifetime) — the `HttpClient` passed to `PayPalServerSdkClient`
> must be long-lived and managed by `IHttpClientFactory`; rebuilding the client per request
> exhausts sockets. **MUST load `dotnet-client-initialization`** before registering the client.

> ⚠ Steps 2–11 (calling operations) — many operations have nullable parameters with no C# default
> that **must be passed explicitly** (cannot be skipped positionally). Using positional arguments
> silently binds the wrong parameter. Always use named arguments or explicitly pass `null`.
> **MUST load `dotnet-calling-endpoints`** before writing the first call.

> ⚠ Steps 9–11 (Vault, models) — `CreatePaymentToken` and list/delete use `TryGetError1(out
> Error1)` not `TryGetError(out Error)` — two distinct types despite similar names. Mixing them
> causes `CS1503` at build. **MUST load `dotnet-models`** before constructing model objects with
> `StringEnum<T>` enum values (enums are not C# enums; build with static members or `.FromValue`).

> ⚠ Steps 2b / 3 (card + 3DS) — if PayPal's risk engine triggers SCA for a card authorization
> in sandbox, `AuthorizeOrder` returns an `OrderAuthorizeResponse` with
> `Status = OrderStatus.PayerActionRequired` and a `rel:payer-action` HATEOAS link rather than
> completing inline. This constitutes a browser-redirect requirement. The integration MUST check
> the response status and treat `PayerActionRequired` as a hard failure — stop processing, surface
> a clear error, and do not follow the approval URL. Whether the sandbox test card
> `4111111111111111` triggers SCA on PayPal's sandbox is UNVERIFIED — validate in sandbox before
> assuming it bypasses 3DS.

> ⚠ Step 8 (pagination — SearchTransactions) — the SDK has no built-in pagination iterator. The
> `page` parameter is 1-based. `SearchResponse.TotalPages` must be read from the first response
> and pages 2..N fetched manually. **MUST load `dotnet-configuration-resilience`** for
> retry/timeout guidance when looping across pages.

---

## 4. REQUIRED READING

Load ALL of the following **before implementation begins**, in this order. The contract sheet
above deliberately does not carry their contents — each skill resolves a hazard that a one-line
note cannot fully settle.

| Skill | Step(s) governed |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, `IHttpClientFactory`, DI registration |
| `dotnet-authentication` | Step 1 — `OAuth2ClientCredentials` field names, token endpoint, base-URL override |
| `dotnet-configuration-resilience` | Step 1, 8 — retry semantics per verb, `Timeout` scope, base-URL override, pagination |
| `dotnet-calling-endpoints` | Steps 2–11 — named arguments, must-pass-explicitly params, async usage, `ct:` |
| `dotnet-models` | Steps 2–11 — `StringEnum<T>` construction, `init`-only setters, `required` fields |
| `dotnet-error-handling` | Steps 2–11 — `SdkException<T>` namespace, Case A/B mechanics, `TryGet…` accessor pattern, `JsonException` boundary |
| `dotnet-testing` | All — `HttpClient` test seam, mocking pattern, framework alignment |

**`dotnet-error-handling` special mandatory rows** — write these into the error boundary before
any other code:

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
  maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
  and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing the error boundary.

---

## 5. Assumptions & Blockers

**Assumptions:**
1. The eShop project is ASP.NET Core (DI via `IServiceCollection`) — DI registration uses
   `AddPayPalServerSdkClient`.
2. `PayPal:Currency` is a single configured currency code (e.g. `"USD"`); mixed-currency orders
   are out of scope.
3. The "customer ID" for vault operations is the eShop's own customer identifier, stored as
   `MerchantCustomerId` in `Customer`. If PayPal also returns a `Customer.Id`, persist both.
4. Partial refunds use a caller-supplied idempotency key (e.g. a GUID generated at refund-intent
   creation time and stored alongside the refund record).
5. `SearchTransactions` date range parameters are passed as ISO-8601 strings with timezone offset
   by the caller. The API docs require this format; the SDK passes them as raw query strings.

**Blockers:**
1. **3DS / PAYER_ACTION_REQUIRED (UNVERIFIED):** Whether the sandbox test card
   `4111111111111111` bypasses SCA on PayPal's sandbox is unverified from the map or source
   alone — only live sandbox traffic can confirm it. If `AuthorizeOrder` returns
   `Status = PAYER_ACTION_REQUIRED`, the integration cannot proceed without a browser redirect,
   which is out of scope per the brief. Validate this in sandbox before committing to the
   direct-card path for production.
2. **PCI SAQ D scope:** Passing raw card numbers (`CardRequest.Number`) and security codes
   (`CardRequest.SecurityCode`) directly through the application means the application is in
   scope for PCI SAQ D. This is a compliance decision for the eShop team, not a technical
   blocker for sandbox testing, but must be resolved before any production deployment.
3. **Vault card 3DS (UNVERIFIED):** `CreatePaymentToken` with raw card fields may or may not
   trigger 3DS verification in sandbox (similar to the authorization flow). If
   `SetupTokenResponse.Status = PAYER_ACTION_REQUIRED` is returned, a browser approval step
   is required — this would be a blocker. Validate in sandbox.
