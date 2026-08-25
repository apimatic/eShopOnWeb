# PayPal .NET SDK Integration Plan — eShopOnWeb `src/PublicApi`

> **Signatures are generated code, verbatim — every parameter name is the literal
> C# identifier. The cancellation-token parameter really is named `ct`: in named
> arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take
> each one from that type's own map row, never from where a neighbouring type sits. A members
> table names the namespace outright; otherwise the row's source path implies it. Dropping a
> type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build
> breaks.

---

## 1. Scope & Sequence

| Step | Description | Operations used |
|---|---|---|
| 1 | Install SDK package into `src/PublicApi` | `dotnet add package AsadAli.Checkout.Sdk` |
| 2 | Register SDK client in DI | `AddPayPalServerSdkClient` / `PayPalServerSdkClient` ctor |
| 3 | Auth credentials from config | `OAuth2ClientCredentials` on options |
| 4 | Environment / base-URL wiring | `ServerEnvironment`, `ServerOptions`, `DefaultOptions` |
| 5 | Authorize (create order + authorize) | `Orders.CreateOrder`, then `Orders.AuthorizeOrder` |
| 6 | Capture authorized payment | `Payments.CaptureAuthorizedPayment` |
| 7 | Void authorization | `Payments.VoidPayment` |
| 8 | Refund captured payment | `Payments.RefundCapturedPayment` |
| 9 | Reauthorize stale authorization | `Payments.ReauthorizePayment` |
| 10 | Vault a card (no charge) — two-step | `Vault.CreateSetupToken` → `Vault.CreatePaymentToken` (with setup token) |
| 11 | List vaulted cards | `Vault.ListCustomerPaymentTokens` |
| 12 | Delete vaulted card | `Vault.DeletePaymentToken` |
| 13 | Pay with vaulted card (authorize) | `Orders.CreateOrder` (not needed separately) + `Orders.AuthorizeOrder` with `Token` payment source |
| 14 | List transactions (paginated) | `TransactionSearch.SearchTransactions` — manual page loop on `TotalPages` |
| 15 | Error boundary | Wrap every call; see error section below |

---

## 2. CONTRACT SHEET

### NuGet & Namespaces

```
dotnet add package AsadAli.Checkout.Sdk   // version-less (floats to latest)
```

Required `using` directives (C# does NOT transitively import child namespaces):

| Namespace | Contents |
|---|---|
| `PayPalServerSdk` | `PayPalServerSdkClient`, `PayPalServerSdkClientOptions`, `ServerOptions` |
| `PayPalServerSdk.Servers` | `ServerEnvironment`, `DefaultOptions` |
| `PayPalServerSdk.Models` | All request/response records |
| `PayPalServerSdk.Models.Enums` | All enums |
| `PayPalServerSdk.Errors` | `AuthorizeOrderError`, `CaptureAuthorizedPaymentError`, etc. |
| `PayPalServerSdk.Core.Exceptions` | `SdkException<TError>` |
| `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` | `OAuth2ClientCredentials` |

Source: `sdk-map.md` (Namespaces section) + SDK source `PayPalServerSdkClientOptions.cs`

---

### Client Construction & Auth

**Client class**: `PayPalServerSdkClient` (namespace `PayPalServerSdk`)
**Options class**: `PayPalServerSdkClientOptions` (namespace `PayPalServerSdk`)
**Constructor**: `PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)`
**DI extension**: `services.AddPayPalServerSdkClient(o => { ... })`

| `PayPalServerSdkClientOptions` property | Type | Source |
|---|---|---|
| `Environment` | `PayPalServerSdk.Servers.ServerEnvironment` | `PayPalServerSdkClientOptions.cs` |
| `Server` | `PayPalServerSdk.ServerOptions` | `PayPalServerSdkClientOptions.cs` |
| `Oauth2` | `OAuth2ClientCredentials?` | `PayPalServerSdkClientOptions.cs` |
| `Oauth2TokenStrategy` | `IOAuth2TokenStrategy<OAuth2ClientCredentials>?` | `PayPalServerSdkClientOptions.cs` |
| `Retry` | `RetryOptions` | `PayPalServerSdkClientOptions.cs` |
| `Logging` | `LoggingOptions` | `PayPalServerSdkClientOptions.cs` |

**Auth credentials type**: `OAuth2ClientCredentials` (namespace `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`)

```csharp
// Fields (all source: SDK clone OAuth2ClientCredentials.cs)
required string ClientId    { get; init; }
required string ClientSecret { get; init; }
string? Scope               { get; init; }  // optional
```

Set as: `options.Oauth2 = new OAuth2ClientCredentials { ClientId = ..., ClientSecret = ... };`

Source: SDK source `PayPalServerSdkClientOptions.cs`, `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs`

---

### Environment & Base-URL Configuration

**CRITICAL — SDK v1.0.1 has exactly ONE defined environment: `ServerEnvironment.Sandbox`.**
There is no `ServerEnvironment.Production` member. The only way to point to the live PayPal
endpoint is to keep `Environment = ServerEnvironment.Sandbox` and override the base URL.

| Config value `PayPal:Environment` | SDK wiring |
|---|---|
| `"Sandbox"` | `options.Environment = ServerEnvironment.Sandbox;` — use default base URL (`https://api-m.sandbox.paypal.com`) |
| `"Production"` | `options.Environment = ServerEnvironment.Sandbox;` — override base URL to `https://api-m.paypal.com` (see below) |

**`PayPal:BaseUrl` override** — when set, apply it verbatim as the API base for ALL calls
(including token/auth calls, since OAuth2 token acquisition uses the same base URL):

```csharp
// ServerOptions (namespace: PayPalServerSdk)
//   .Default (type: DefaultOptions, namespace: PayPalServerSdk.Servers)
//   .Sandbox  (type: DefaultOptions.SandboxOptions, nested class)
//   .BaseUrl  (type: string)
options.Server = new ServerOptions
{
    Default = new DefaultOptions
    {
        Sandbox = new DefaultOptions.SandboxOptions { BaseUrl = baseUrlFromConfig }
    }
};
```

Recommended logic: read `PayPal:BaseUrl` first; if set, use it (override `Server`). If not set,
derive from `PayPal:Environment` ("Sandbox" → no override, "Production" → override to
`https://api-m.paypal.com`).

Source: SDK source `ServerOptions.cs`, `Servers/ServerEnvironment.cs`, `Servers/DefaultOptions.cs`

---

### Enum Values (all source: `map/models/enums.md`)

| Enum | Namespace | Members (C# name → wire value) |
|---|---|---|
| `CheckoutPaymentIntent` | `PayPalServerSdk.Models.Enums` | `Authorize ("AUTHORIZE")`, `Capture ("CAPTURE")` |
| `OrdersCardVerificationMethod` | `PayPalServerSdk.Models.Enums` | `ScaAlways ("SCA_ALWAYS")`, `ScaWhenRequired ("SCA_WHEN_REQUIRED")`, `_3DSecure ("3D_SECURE")`, `AvsCvv ("AVS_CVV")` |
| `TokenType` | `PayPalServerSdk.Models.Enums` | `BillingAgreement ("BILLING_AGREEMENT")` only. For vault payment tokens use `TokenType.FromValue("PAYMENT_METHOD_TOKEN")` — see Op 9 notes |
| `VaultTokenRequestType` | `PayPalServerSdk.Models.Enums` | `SetupToken ("SETUP_TOKEN")` |
| `VaultCardVerificationMethod` | `PayPalServerSdk.Models.Enums` | `ScaWhenRequired ("SCA_WHEN_REQUIRED")`, `ScaAlways ("SCA_ALWAYS")` |
| `StoreInVaultInstruction` | `PayPalServerSdk.Models.Enums` | `OnSuccess ("ON_SUCCESS")` |
| `VaultInstructionAction` | `PayPalServerSdk.Models.Enums` | `OnCreatePaymentTokens ("ON_CREATE_PAYMENT_TOKENS")`, `OnPayerApproval ("ON_PAYER_APPROVAL")` |
| `AuthorizationStatus` | `PayPalServerSdk.Models.Enums` | `Created`, `Captured`, `Denied`, `Expired`, `PartiallyCaptered`, `Voided`, `Pending` |

---

### Operation 1 — Authorize (Create Order with AUTHORIZE intent + Authorize it)

**This is a two-call flow.** PayPal separates order creation from payment authorization.

#### Step 1a: CreateOrder

**Controller**: `client.Orders` · Source: `map/operations/Orders.md`

```
CreateOrder(
    string? payPalMockResponse,           // null for non-mock
    string? payPalRequestId,              // idempotency key (UUID) — pass for safety
    string? payPalPartnerAttributionId,   // null unless partner
    string? payPalClientMetadataId,       // null unless needed
    string? payPalAuthAssertion,          // null unless marketplace
    OrderRequest body,                    // !req — not nullable
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default
)
```

All 5 nullable header params have no default → **must pass explicitly** (pass `null` to skip).
**Returns**: `PayPalServerSdk.Models.Order`
**Error**: `SdkException<CreateOrderError>` — Case A · `TryGetError(out Error)` [400, 401, 422] · `TryGetRawError(out RawError)` [fallback]

**Request model: `OrderRequest`** (`PayPalServerSdk.Models`)

| C# property (wire_name) | Type | Required? |
|---|---|---|
| `Intent (intent)` | `CheckoutPaymentIntent` | **required** — use `CheckoutPaymentIntent.Authorize` |
| `PurchaseUnits (purchase_units)` | `IReadOnlyList<PurchaseUnitRequest>` | **required** |
| `PaymentSource (payment_source)` | `PaymentSource?` | optional — include for direct card; omit for redirect flow |
| `Payer (payer)` | `Payer?` | optional |
| `ApplicationContext (application_context)` | `OrderApplicationContext?` | optional |

Source: `map/models/records-1-Ac-Pa.md` (`OrderRequest`)

**`PurchaseUnitRequest`** (`PayPalServerSdk.Models`):

| C# property (wire_name) | Type | Required? |
|---|---|---|
| `Amount (amount)` | `AmountWithBreakdown` | **required** |
| `ReferenceId (reference_id)` | `string?` | optional |
| `CustomId (custom_id)` | `string?` | optional — use to store eShop `Order.Id` |

**`AmountWithBreakdown`** (`PayPalServerSdk.Models`):

| C# property (wire_name) | Type | Required? |
|---|---|---|
| `CurrencyCode (currency_code)` | `string` | **required** (e.g. `"USD"`) |
| `Value (value)` | `string` | **required** (decimal as string, e.g. `"59.99"`) |
| `Breakdown (breakdown)` | `AmountBreakdown?` | optional |

Source: `map/models/records-1-Ac-Pa.md` (`AmountWithBreakdown`)

**For direct card processing** (no browser redirect, no 3DS) — include a `PaymentSource` with `Card`:

```
PaymentSource {
    Card = new CardRequest {
        Number       = "...",          // raw PAN — requires PCI SAQ D
        Expiry       = "YYYY-MM",
        SecurityCode = "...",
        Name         = "...",
        BillingAddress = new Address { CountryCode = "US" /* required */, ... },
        Attributes   = new CardAttributes {
            Verification = new CardVerification {
                Method = OrdersCardVerificationMethod.AvsCvv  // bypasses 3DS
            }
        }
    }
}
```

**`CardRequest`** fields (source: `map/models/records-1-Ac-Pa.md`):
`Name?`, `Number?`, `Expiry?`, `SecurityCode?`, `BillingAddress?` (Address), `Attributes?` (CardAttributes), `VaultId?`, `SingleUseToken?`, `StoredCredential?`, `NetworkToken?`, `ExperienceContext?`

**`CardAttributes`** → `Verification (verification): CardVerification?` → `Method: OrdersCardVerificationMethod? = ScaWhenRequired`

**Return value**: `Order` → only `Order.Id` is needed at this step (the PayPal Order ID used in AuthorizeOrder).

#### Step 1b: AuthorizeOrder

**Controller**: `client.Orders` · Source: `map/operations/Orders.md`

```
AuthorizeOrder(
    string id,                        // PayPal Order ID from CreateOrder
    string? payPalMockResponse,       // null
    string? payPalRequestId,          // idempotency key
    string? payPalClientMetadataId,   // null
    string? payPalAuthAssertion,      // null
    OrderAuthorizeRequest? body,      // null for no additional payment source
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default
)
```

All 5 nullable header params + body have no default → **must pass explicitly**.
Use `prefer = "return=representation"` to get the full authorization detail in the response body (otherwise `PurchaseUnits.Payments.Authorizations` may be absent).

**Returns**: `PayPalServerSdk.Models.OrderAuthorizeResponse`
**Error**: `SdkException<AuthorizeOrderError>` — Case A · `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback]

**Extracting the Authorization ID** (the ID needed for capture/void/reauthorize):

```csharp
// OrderAuthorizeResponse
//   .PurchaseUnits  IReadOnlyList<PurchaseUnit>?
//   [0].Payments    PaymentCollection?
//   .Authorizations IReadOnlyList<AuthorizationWithAdditionalData>?
//   [0].Id          string?
string? authorizationId = response
    .PurchaseUnits?[0]
    .Payments?.Authorizations?[0]
    .Id;
```

Source: `map/models/records-1-Ac-Pa.md` (`OrderAuthorizeResponse`, `PurchaseUnit`, `PaymentCollection`, `AuthorizationWithAdditionalData`)

**`OrderAuthorizeRequest`** (`PayPalServerSdk.Models`) — only field:
- `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?`
- `OrderAuthorizeRequestPaymentSource`: `Card`, `Token`, `Paypal`, `ApplePay`, `GooglePay`, `Venmo`
- Pass `null` body when payment source was already provided in `CreateOrder`.

**Idempotency**: Pass a caller-generated UUID as `payPalRequestId` on both CreateOrder and AuthorizeOrder.

---

### Operation 2 — Capture

**Controller**: `client.Payments` · Source: `map/operations/Payments.md`

```
CaptureAuthorizedPayment(
    string authorizationId,
    string? payPalMockResponse,   // null
    string? payPalRequestId,      // idempotency key
    string? payPalAuthAssertion,  // null
    CaptureRequest? body,         // null for full capture, or supply Amount for partial
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default
)
```

All 4 nullable params have no default → **must pass explicitly**.
Use `prefer = "return=representation"` to receive `SellerReceivableBreakdown` in the response.

**Returns**: `PayPalServerSdk.Models.CapturedPayment`
**Error**: `SdkException<CaptureAuthorizedPaymentError>` — Case A
  · `TryGetError(out Error)` [400, 401, 403, 404, 409, 422]
  · `TryGetNoContent(out RawError)` [500]
  · `TryGetRawError(out RawError)` [fallback]

**`CaptureRequest`** (`PayPalServerSdk.Models`) — all optional:

| C# property (wire_name) | Type |
|---|---|
| `Amount (amount)` | `Money?` — omit for full capture |
| `InvoiceId (invoice_id)` | `string?` |
| `FinalCapture (final_capture)` | `bool? = false` |
| `PaymentInstruction (payment_instruction)` | `CapturePaymentInstruction?` |
| `NoteToPayer (note_to_payer)` | `string?` |
| `SoftDescriptor (soft_descriptor)` | `string?` |

**Extracting from `CapturedPayment`**:

| Value | Path |
|---|---|
| Capture ID | `response.Id` |
| Captured amount | `response.Amount` (`Money?` with `CurrencyCode`, `Value`) |
| PayPal fee | `response.SellerReceivableBreakdown?.PaypalFee` (`Money?`) |
| Net amount | `response.SellerReceivableBreakdown?.NetAmount` (`Money?`) |

Source: `map/models/records-1-Ac-Pa.md` (`CapturedPayment`, `SellerReceivableBreakdown`)
`SellerReceivableBreakdown`: `GrossAmount (gross_amount): Money !req`, `PaypalFee (paypal_fee): Money?`, `NetAmount (net_amount): Money?`

---

### Operation 3 — Void Authorization

**Controller**: `client.Payments` · Source: `map/operations/Payments.md`

```
VoidPayment(
    string authorizationId,
    string? payPalMockResponse,  // null — must pass explicitly
    string? payPalAuthAssertion, // null — must pass explicitly
    string? payPalRequestId,     // null — must pass explicitly
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default
)
```

All 3 nullable header params have no default → **must pass explicitly**.
**Returns**: `PayPalServerSdk.Models.PaymentAuthorization`
**Error**: `SdkException<VoidPaymentError>` — Case A
  · `TryGetError(out Error)` [401, 403, 404, 409, 422]
  · `TryGetNoContent(out RawError)` [500]
  · `TryGetRawError(out RawError)` [fallback]

No request body. Pass all three nullable headers as `null`.

---

### Operation 4 — Refund Captured Payment

**Controller**: `client.Payments` · Source: `map/operations/Payments.md`

```
RefundCapturedPayment(
    string captureId,
    string? payPalMockResponse,  // null — must pass explicitly
    string? payPalRequestId,     // idempotency key — pass caller-supplied key
    string? payPalAuthAssertion, // null — must pass explicitly
    RefundRequest? body,         // null for full refund; supply Amount for partial
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default
)
```

All 4 nullable params have no default → **must pass explicitly**.
**Idempotency key**: pass the caller-supplied key as `payPalRequestId` (wire: `PayPal-Request-Id` header).
**Returns**: `PayPalServerSdk.Models.Refund`
**Error**: `SdkException<RefundCapturedPaymentError>` — Case A
  · `TryGetError(out Error)` [400, 401, 403, 404, 409, 422]
  · `TryGetNoContent(out RawError)` [500]
  · `TryGetRawError(out RawError)` [fallback]

**`RefundRequest`** (`PayPalServerSdk.Models`) — all optional:

| C# property (wire_name) | Type |
|---|---|
| `Amount (amount)` | `Money?` — omit for full refund; provide for partial |
| `CustomId (custom_id)` | `string?` |
| `InvoiceId (invoice_id)` | `string?` |
| `NoteToPayer (note_to_payer)` | `string?` |
| `PaymentInstruction (payment_instruction)` | `RefundPaymentInstruction?` |

**`Money`**: `CurrencyCode (currency_code): string !req`, `Value (value): string !req`

**Extracting from `Refund`**:

| Value | Path |
|---|---|
| Refund ID | `response.Id` (`string?`) |
| Gross refund amount | `response.Amount` (`Money?`) |
| PayPal fee on refund | `response.SellerPayableBreakdown?.PaypalFee` (`Money?`) |
| Net amount | `response.SellerPayableBreakdown?.NetAmount` (`Money?`) |

`SellerPayableBreakdown`: `GrossAmount?`, `PaypalFee?`, `NetAmount?`, `TotalRefundedAmount?`
Source: `map/models/records-2-Pa-Ve.md` (`Refund`, `SellerPayableBreakdown`)

---

### Operation 5 — Reauthorize Stale Authorization

**Controller**: `client.Payments` · Source: `map/operations/Payments.md`

```
ReauthorizePayment(
    string authorizationId,
    string? payPalRequestId,     // idempotency key — must pass explicitly
    string? payPalAuthAssertion, // null — must pass explicitly
    ReauthorizeRequest? body,    // supply new amount or null
    string? prefer = "return=minimal",
    RequestOptions? requestOptions = null,
    CancellationToken ct = default
)
```

All 3 nullable params have no default → **must pass explicitly**.
**Returns**: `PayPalServerSdk.Models.PaymentAuthorization`
**Error**: `SdkException<ReauthorizePaymentError>` — Case A
  · `TryGetError(out Error)` [400, 401, 403, 404, 422]
  · `TryGetNoContent(out RawError)` [500]
  · `TryGetRawError(out RawError)` [fallback]

**`ReauthorizeRequest`** (`PayPalServerSdk.Models`):
- `Amount (amount): Money?` — only field; optional (PayPal docs: only `amount` is supported)

**Constraint**: reauthorize from day 4 to day 29. If >30 days have elapsed, create a new authorization instead. Up to 115% of original amount (US), not exceeding $75 increase. Source: `map/operations/Payments.md` (ReauthorizePayment notes)

**Extracting new authorization ID**:
- `response.Id` (`string?`) from `PaymentAuthorization`

---

### Operation 6 — Vault a Card (No Charge) — Two-Step Flow

Vaulting without an initial transaction requires two calls:
1. `CreateSetupToken` — stores card details transiently and returns a setup token
2. `CreatePaymentToken` — promotes the setup token into a permanent payment token

#### Step 6a: CreateSetupToken

**Controller**: `client.Vault` · Source: `map/operations/Vault.md`

```
CreateSetupToken(
    string? payPalRequestId,       // idempotency key — must pass explicitly
    SetupTokenRequest body,        // !req (not nullable)
    RequestOptions? requestOptions = null,
    CancellationToken ct = default
)
```

**Returns**: `PayPalServerSdk.Models.SetupTokenResponse`
**Error**: `SdkException<CreateSetupTokenError>` — Case A · `TryGetError1(out Error1)` [400, 403, 422, 500] · `TryGetRawError(out RawError)` [fallback]

**`SetupTokenRequest`** (`PayPalServerSdk.Models`):

| C# property (wire_name) | Type | Required? |
|---|---|---|
| `PaymentSource (payment_source)` | `SetupTokenRequestPaymentSource` | **required** |
| `Customer (customer)` | `Customer?` | optional — supply `Id` to associate with existing customer |

**`SetupTokenRequestPaymentSource`**:
- `Card (card): SetupTokenRequestCard?`
- `Token`, `Paypal`, `Venmo`, `ApplePay`, `Bank` (other alternatives)

**`SetupTokenRequestCard`**:
- `Name?`, `Number?`, `Expiry?`, `SecurityCode?`, `Brand?`, `BillingAddress?`, `VerificationMethod?`, `ExperienceContext?`
- `VerificationMethod (verification_method): VaultCardVerificationMethod?` — use `VaultCardVerificationMethod.ScaWhenRequired` to suppress 3DS for direct vault

**`Customer`** (`PayPalServerSdk.Models`): `Id (id): string?`, `MerchantCustomerId (merchant_customer_id): string?`

**Extracting from `SetupTokenResponse`**:
- `response.Id` = setup token ID (used in next step)
- `response.Customer?.Id` = PayPal customer ID
- `response.Status` = `PaymentTokenStatus?`

Source: `map/models/records-2-Pa-Ve.md` (`SetupTokenRequest`, `SetupTokenResponse`, `SetupTokenRequestCard`)

#### Step 6b: CreatePaymentToken (from Setup Token)

**Controller**: `client.Vault` · Source: `map/operations/Vault.md`

```
CreatePaymentToken(
    string? payPalRequestId,       // idempotency key — must pass explicitly
    PaymentTokenRequest body,      // !req (not nullable)
    RequestOptions? requestOptions = null,
    CancellationToken ct = default
)
```

**Returns**: `PayPalServerSdk.Models.PaymentTokenResponse`
**Error**: `SdkException<CreatePaymentTokenError>` — Case A · `TryGetError1(out Error1)` [400, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback]

**`PaymentTokenRequest`** to promote a setup token:

```csharp
new PaymentTokenRequest
{
    Customer = new Customer { Id = optionalCustomerId },   // optional
    PaymentSource = new PaymentTokenRequestPaymentSource
    {
        Token = new VaultTokenRequest
        {
            Id   = setupTokenId,                    // from Step 6a
            Type = VaultTokenRequestType.SetupToken // "SETUP_TOKEN"
        }
    }
}
```

**`PaymentTokenRequestPaymentSource`**: `Card (card): PaymentTokenRequestCard?`, `Token (token): VaultTokenRequest?`
**`VaultTokenRequest`**: `Id (id): string !req`, `Type (type): VaultTokenRequestType !req`

**Extracting from `PaymentTokenResponse`**:

| Value | Path |
|---|---|
| Vault token ID | `response.Id` (`string?`) |
| Card last 4 digits | `response.PaymentSource?.Card?.LastDigits` (`string?`) |
| Card brand | `response.PaymentSource?.Card?.Brand` (`CardBrand?`) |
| Card type (credit/debit) | `response.PaymentSource?.Card?.Type` (`CardType?`) |

`PaymentTokenResponsePaymentSource`: `Card (card): CardPaymentTokenEntity?`
`CardPaymentTokenEntity`: `LastDigits?`, `Brand?` (`CardBrand`), `Type?` (`CardType`), `Name?`, `Expiry?`, `VerificationStatus?`, ...

Source: `map/models/records-2-Pa-Ve.md` (`PaymentTokenRequest`, `PaymentTokenResponse`, `PaymentTokenResponsePaymentSource`, `CardPaymentTokenEntity`)

---

### Operation 7 — List Vaulted Cards

**Controller**: `client.Vault` · Source: `map/operations/Vault.md`

```
ListCustomerPaymentTokens(
    string customerId,
    int? pageSize = 5,
    int? page = 1,
    bool? totalRequired = false,
    RequestOptions? requestOptions = null,
    CancellationToken ct = default
)
```

Wire query params: `customer_id`, `page_size`, `page`, `total_required`.
**Returns**: `PayPalServerSdk.Models.CustomerVaultPaymentTokensResponse`
**Error**: `SdkException<ListCustomerPaymentTokensError>` — Case A · `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError(out RawError)` [fallback]

**`CustomerVaultPaymentTokensResponse`**:

| C# property | Type | Notes |
|---|---|---|
| `PaymentTokens (payment_tokens)` | `IReadOnlyList<PaymentTokenResponse>?` | Each token has same shape as CreatePaymentToken response |
| `TotalItems (total_items)` | `int?` | |
| `TotalPages (total_pages)` | `int?` | |
| `Customer (customer)` | `VaultResponseCustomer?` | |
| `Links (links)` | `IReadOnlyList<LinkDescription>?` | |

Pass `totalRequired: true` to populate `TotalItems`/`TotalPages`.
The SDK does not auto-paginate — caller must loop: start at `page = 1`, increment until `page > TotalPages`.

Source: `map/models/records-1-Ac-Pa.md` (`CustomerVaultPaymentTokensResponse`)

---

### Operation 8 — Delete Vaulted Card

**Controller**: `client.Vault` · Source: `map/operations/Vault.md`

```
DeletePaymentToken(
    string id,                     // vault payment token ID
    RequestOptions? requestOptions = null,
    CancellationToken ct = default
)
```

**Returns**: `void` (Task)
**Error**: `SdkException<DeletePaymentTokenError>` — Case A · `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError(out RawError)` [fallback]

No request body.

---

### Operation 9 — Pay with Vaulted Card (Authorize)

Reuse the `CreateOrder` + `AuthorizeOrder` two-call flow from Operation 1, but supply the
vault payment token in the `OrderAuthorizeRequestPaymentSource` instead of raw card details.

The `Token` model uses `TokenType`. The SDK's `TokenType` enum declares only
`BillingAgreement ("BILLING_AGREEMENT")` as a static member. Vault payment tokens created by
the V3 Vault API use the type `PAYMENT_METHOD_TOKEN`. Use `TokenType.FromValue(...)` to
construct this value — the `StringEnum<T>` base class accepts any string (see SDK source
`Core/Enum/StringEnum.cs`):

```csharp
// In OrderAuthorizeRequest body:
new OrderAuthorizeRequest
{
    PaymentSource = new OrderAuthorizeRequestPaymentSource
    {
        Token = new Token
        {
            Id   = vaultPaymentTokenId,              // from Op 6/7
            Type = TokenType.FromValue("PAYMENT_METHOD_TOKEN")
        }
    }
}
```

`Token` (`PayPalServerSdk.Models`): `Id (id): string !req`, `Type (type): TokenType !req`

UNVERIFIED: Whether the live PayPal v2 Orders API accepts `PAYMENT_METHOD_TOKEN` as a token
type when sent from the v3 Vault payment token. The `StringEnum` mechanism sends the string
verbatim; confirm with live sandbox traffic before releasing.

Alternative: create the order with `Token` in `OrderRequest.PaymentSource.Token` (use
`PaymentSource.Token` field in CreateOrder's body), which also flows to the Authorize step
without a separate re-supply of payment source.

Source: `map/models/records-1-Ac-Pa.md` (`Token`, `OrderAuthorizeRequestPaymentSource`), SDK source `Core/Enum/StringEnum.cs`

---

### Operation 10 — List Transactions (Paginated)

**Controller**: `client.TransactionSearch` · Source: `map/operations/TransactionSearch.md`

```
SearchTransactions(
    string startDate,                      // ISO-8601, e.g. "2024-01-01T00:00:00-0700"
    string endDate,                        // ISO-8601
    string? transactionId,                 // must pass explicitly (null to skip)
    string? transactionType,               // must pass explicitly (null to skip)
    string? transactionStatus,             // must pass explicitly (null to skip)
    string? transactionAmount,             // must pass explicitly (null to skip)
    string? transactionCurrency,           // must pass explicitly (null to skip)
    string? paymentInstrumentType,         // must pass explicitly (null to skip)
    string? storeId,                       // must pass explicitly (null to skip)
    string? terminalId,                    // must pass explicitly (null to skip)
    string? fields = "transaction_info",
    string? balanceAffectingRecordsOnly = "Y",
    int? pageSize = 100,
    int? page = 1,
    RequestOptions? requestOptions = null,
    CancellationToken ct = default
)
```

8 nullable params (`transactionId` through `terminalId`) have no default → **must pass explicitly**.
**Returns**: `PayPalServerSdk.Models.SearchResponse`
**Error**: `SdkException<RawError>` — **Case B** (the only Case B operation in scope)
  · `ex.Error.StatusCode` (`HttpStatusCode`)
  · `ex.Error.ReadAsString()` → body as string
  · `ex.Error.ReadAsJson<T>()` → body as T

**`SearchResponse`**:

| C# property | Type | Notes |
|---|---|---|
| `TransactionDetails (transaction_details)` | `IReadOnlyList<TransactionDetails>?` | Per-transaction items |
| `TotalPages (total_pages)` | `int?` | Total pages available |
| `TotalItems (total_items)` | `int?` | Total transaction count |
| `Page (page)` | `int?` | Current page |
| `StartDate (start_date)` | `string?` | |
| `EndDate (end_date)` | `string?` | |

**`TransactionDetails`** → `TransactionInfo (transaction_info): TransactionInformation?`
**`TransactionInformation`** key fields: `TransactionId?`, `TransactionAmount?` (`Money`), `FeeAmount?` (`Money`), `TransactionStatus?`, `TransactionInitiationDate?`, `TransactionEventCode?`

**Manual pagination loop** (the SDK provides no auto-pagination):
```csharp
int currentPage = 1;
int totalPages;
do {
    var response = await client.TransactionSearch.SearchTransactions(
        startDate: start, endDate: end,
        transactionId: null, transactionType: null, transactionStatus: null,
        transactionAmount: null, transactionCurrency: null,
        paymentInstrumentType: null, storeId: null, terminalId: null,
        page: currentPage, ct: ct);
    totalPages = response.TotalPages ?? 1;
    // process response.TransactionDetails
    currentPage++;
} while (currentPage <= totalPages);
```

Source: `map/operations/TransactionSearch.md`, `map/models/records-2-Pa-Ve.md` (`SearchResponse`, `TransactionDetails`, `TransactionInformation`)

---

### Error Handling — All Operations

**Universal error model** (source: `sdk-map.md`):

- All operations are throw-based (no `…Result` variants exist in this SDK).
- **Case A (39 of 40 operations in scope)**: `SdkException<{Operation}Error>` where `TError : ApiError`.
  - Error types for this integration:
    - `AuthorizeOrderError`, `CreateOrderError`, `CaptureAuthorizedPaymentError`, `VoidPaymentError`
    - `RefundCapturedPaymentError`, `ReauthorizePaymentError`
    - `CreateSetupTokenError`, `CreatePaymentTokenError`, `ListCustomerPaymentTokensError`, `DeletePaymentTokenError`
    - `SearchBalancesError`
    - All in namespace `PayPalServerSdk.Errors`
  - Accessor: `TryGetError(out Error)` for Orders/Payments errors; `TryGetError1(out Error1)` for Vault errors; check the exact accessor per operation row above.
  - `Error` (`PayPalServerSdk.Models`): `Name !req`, `Message !req`, `DebugId !req`, `Details: IReadOnlyList<ErrorDetails>?`
  - `Error1` (`PayPalServerSdk.Models`): `Name !req`, `Message !req`, `DebugId !req`, `Details: IReadOnlyList<ErrorDetails1>?`
  - Fallback: `TryGetRawError(out RawError)` always available on Case A errors.

- **Case B (SearchTransactions only)**: `SdkException<RawError>`
  - `ex.Error.StatusCode`, `ex.Error.ReadAsString()`, `ex.Error.ReadAsJson<T>()`

- Certain Payments operations also have `TryGetNoContent(out RawError)` [500] — check for it in the catch ladder BEFORE `TryGetRawError` for: `CaptureAuthorizedPayment`, `VoidPayment`, `RefundCapturedPayment`, `ReauthorizePayment`, `GetAuthorizedPayment`.

**`Error` shape** (Case A, used by Orders/Payments): `Name (name): string !req`, `Message (message): string !req`, `DebugId (debug_id): string !req`, `Details (details): IReadOnlyList<ErrorDetails>?`, `Links (links): IReadOnlyList<LinkDescription>?`

Source: `sdk-map.md` (Error-handling model), `map/models/records-1-Ac-Pa.md` (`Error`, `Error1`)

---

## 3. Trap Notes

> ⚠ Step 2 (client registration) — `HttpClient` lifetime and `IHttpClientFactory` registration
> are non-obvious for this SDK. **MUST load `dotnet-client-initialization`** before writing the
> DI registration.

> ⚠ Step 3 (auth credentials) — how OAuth2 credentials are set (before vs during construction,
> secrets from config vs hardcoded) and the token-refresh lifecycle are not shown in the
> signature. **MUST load `dotnet-authentication`** before wiring credentials.

> ⚠ Step 4 (environment/base-URL) — `Timeout` in `RetryOptions` does NOT bound the whole HTTP
> call, and `HttpMethodsToRetry` gates status-based retry only; a transport failure retries on
> ALL verbs including POST, meaning a non-idempotent write (Capture, Refund) can execute more
> than once. **MUST load `dotnet-configuration-resilience`** before configuring retry, timeout,
> or the base URL.

> ⚠ Steps 5–13 (calling endpoints) — many parameters have no C# default and must be passed
> explicitly (positional calls mis-bind silently). **MUST load `dotnet-calling-endpoints`**
> before writing the first call. Named-argument form is strongly preferred for all operations
> with 5+ parameters.

> ⚠ Steps 5–13 (model construction) — enums are `StringEnum<T>`, not C# enums; unions use
> factory methods, not `new`; `required` fields cause a compile error if omitted. **MUST load
> `dotnet-models`** before constructing any request model.

> ⚠ Step 15 (error boundary) — the catch ladder order matters: check `TryGetNoContent` before
> `TryGetRawError` on Payments operations; SearchTransactions is Case B (different exception
> type entirely). **MUST load `dotnet-error-handling`** before writing any try/catch.

> ⚠ Steps 5–14 (testing) — the test seam is the `HttpClient` constructor argument; stub at the
> `HttpMessageHandler` level. **MUST load `dotnet-testing`** before writing any SDK test.

---

## 4. REQUIRED READING

Load ALL of the following **before implementation starts**. This sheet deliberately does not
carry their contents — each skill covers defaults, worked examples, and traps that a one-line
note cannot replace.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 2 — DI registration, `HttpClient` lifetime |
| `dotnet-authentication` | Step 3 — OAuth2 credentials, token refresh |
| `dotnet-configuration-resilience` | Step 4 — retry, timeout, base-URL override |
| `dotnet-calling-endpoints` | Steps 5–14 — named args, parameter order, response envelopes |
| `dotnet-models` | Steps 5–14 — `StringEnum`, `required` init, `Money` as string |
| `dotnet-error-handling` | Step 15 — Case A/B mechanics, catch ladder, `TryGetNoContent` |
| `dotnet-testing` | All — `HttpMessageHandler` test seam |

**`System.Text.Json.JsonException` hazards** — both must be handled at the integration boundary:

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

| # | Item | Type | Detail |
|---|---|---|---|
| A1 | Direct card processing possible | Confirmed (with caveat) | The SDK's `CardRequest` accepts raw card fields and `CardAttributes.Verification.Method = OrdersCardVerificationMethod.AvsCvv` suppresses 3DS. However, passing raw PAN, expiry, and CVV via the API **requires PCI SAQ D compliance**. This is noted in the `CardRequest` XML doc in the SDK source. Confirm PCI scope before implementing. |
| A2 | Card vaulting without initial transaction | Confirmed | Supported via the two-step `CreateSetupToken` → `CreatePaymentToken` flow using `VaultTokenRequestType.SetupToken`. |
| A3 | Transaction reporting API availability | Confirmed | `TransactionSearch.SearchTransactions` maps to `/v1/reporting/transactions`. Pagination is manual — the caller loops on `TotalPages` from `SearchResponse`. |
| A4 | Production environment | **Blocker** | SDK v1.0.1 (`ServerEnvironment`) declares **only `Sandbox`**. There is no `ServerEnvironment.Production`. To target production, set `Environment = ServerEnvironment.Sandbox` and override `options.Server.Default.Sandbox.BaseUrl = "https://api-m.paypal.com"`. The `PayPal:Environment = "Production"` config must be mapped to this URL override in the integration code. |
| A5 | Pay-with-vault-token type | UNVERIFIED | `TokenType` enum only defines `BillingAgreement`. For v3 vault payment tokens, use `TokenType.FromValue("PAYMENT_METHOD_TOKEN")` — the wire string goes through verbatim. Whether the PayPal v2 Orders endpoint accepts this value must be confirmed with live sandbox traffic. |
| A6 | `prefer = "return=representation"` for breakdown fields | UNVERIFIED | Capture's `SellerReceivableBreakdown` and Refund's `SellerPayableBreakdown` may be absent when `prefer = "return=minimal"` (the default). Use `prefer = "return=representation"` on Capture and Refund calls to request the full body. Whether these fields are always present in the representation response cannot be confirmed from the SDK source alone — defensive null checks on all optional fields are required. |
| A7 | Currency code source | Assumption | The `AmountWithBreakdown.CurrencyCode` value is assumed to come from a `PayPal:Currency` application config key (e.g. `"USD"`). No such key exists in eShopOnWeb today — it must be added. |
| A8 | `Order.Total()` precision | Assumption | `Order.Total()` returns `decimal`; `AmountWithBreakdown.Value` is a `string`. The conversion (`total.ToString("F2")`) must use invariant culture to produce the correct wire format (dot decimal separator). |
