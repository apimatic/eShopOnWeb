# PayPal Payment Integration Plan — eShopOnWeb

---

## 1. Scope & Sequence

| # | Step | SDK Operations Used |
|---|------|---|
| 1 | Install NuGet package | `dotnet add package AsadAli.Checkout.Sdk` |
| 2 | Client construction & DI registration | `PayPalServerSdkClient`, `services.AddPayPalServerSdkClient` |
| 3 | Place eShop order (no PayPal call; starts `AwaitingPayment`) | — |
| 4 | Pay — Create PayPal order (AUTHORIZE intent) | `client.Orders.CreateOrder` |
| 5 | Pay — Authorize (hold funds) with card or saved-card id | `client.Orders.AuthorizeOrder` |
| 6 | Fulfil — Check/reauthorize if stale, then capture | `client.Payments.ReauthorizePayment` then `client.Payments.CaptureAuthorizedPayment` |
| 7 | Cancel — Void authorization | `client.Payments.VoidPayment` |
| 8 | Refund — Full or partial, caller-supplied idempotency key | `client.Payments.RefundCapturedPayment` |
| 9 | My orders — shopper order list with payment state | Local DB query (no PayPal call) |
| 10 | Reconciliation — paginated transaction search | `client.TransactionSearch.SearchTransactions` |
| 11 | Save card — vault a card for the signed-in shopper | `client.Vault.CreatePaymentToken` |
| 12 | List saved cards | `client.Vault.ListCustomerPaymentTokens` |
| 13 | Delete saved card | `client.Vault.DeletePaymentToken` |

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal
> C# identifier. The cancellation-token parameter really is named `ct`: in named
> arguments write `ct:`, never `cancellationToken:`.**
>
> **SDK methods have NO `Async` suffix.** Generated methods return `Task<T>` and are
> awaited with `await`, but the method name itself is plain: `CreateOrder`, not
> `CreateOrderAsync`. Using the `Async` suffix causes `CS1061` ("does not contain a
> definition for …").
>
> **SDK record models use `required init` properties — object initializer syntax only.**
> There are no constructor overloads with named parameters. Always write:
> `new AmountWithBreakdown { CurrencyCode = …, Value = … }` — never
> `new AmountWithBreakdown(CurrencyCode: …, Value: …)`. Using constructor syntax
> causes `CS1739`.
> Confirmed from: `checkout-sample-sdk/Models/AmountWithBreakdown.cs`.
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take
> each one from that type's own map row, never from where a neighbouring type sits. A members
> table names the namespace outright; otherwise the row's source path implies it
> (`Core/Configuration/…` => `…Core.Configuration`; a file at the repo root => the root
> namespace). Enums, unions, auth, server and client-config types are spread across different
> child namespaces, and two types configured side by side in the same options object routinely
> live in different ones. Dropping a type to the root or to `.Models` makes the implementer
> guess the wrong `using`, and the build breaks.

---

### 2a. NuGet Install

Package name: `AsadAli.Checkout.Sdk`
Exact version: **`1.0.0`** (declared in `PayPalServerSdk.csproj` `<Version>` element — the authoritative NuGet version; the git tag `v1.0.1` does not match this value)

**With NuGet Central Package Management** (`ManagePackageVersionsCentrally=true`):

`Directory.Packages.props`:
```xml
<PackageVersion Include="AsadAli.Checkout.Sdk" Version="1.0.0" />
```

Consuming project `.csproj` (no `Version` attribute — CPM supplies it):
```xml
<PackageReference Include="AsadAli.Checkout.Sdk" />
```

**Without Central Package Management**:
```
dotnet add package AsadAli.Checkout.Sdk --version 1.0.0
```

Source: `PayPalServerSdk.csproj` line 12 (SDK source — `<Version>1.0.0</Version>`).

---

### 2b. Namespaces (add one `using` per kind of type referenced)

| Contents | Namespace |
|---|---|
| `PayPalServerSdkClient`, `PayPalServerSdkClientOptions`, `ServerOptions` | `PayPalServerSdk` |
| `ServerEnvironment`, `DefaultOptions` | `PayPalServerSdk.Servers` |
| All request/response records | `PayPalServerSdk.Models` |
| All enums (`CheckoutPaymentIntent`, `AuthorizationStatus`, etc.) | `PayPalServerSdk.Models.Enums` |
| Error classes (`CreateOrderError`, `AuthorizeOrderError`, etc.) | `PayPalServerSdk.Errors` |
| `OAuth2ClientCredentials` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` |
| `SdkException<T>` | `PayPalServerSdk.Core.Exceptions` |
| `RawError`, `ApiError` | `PayPalServerSdk.Core.ErrorResponse` |

C# does NOT import child namespaces transitively. Each namespace above requires its own `using`.

Source: `sdk-map.md` — *Namespaces by content type*.

---

### 2c. Client Construction

Constructor:

```
PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)
```

DI registration alternative:

```csharp
services.AddPayPalServerSdkClient(o => { /* set o.Environment, o.Oauth2, o.Server */ });
```

`PayPalServerSdkClientOptions` properties (namespace `PayPalServerSdk`):

| Property | Type | Set to |
|---|---|---|
| `Environment` | `ServerEnvironment` | Always `ServerEnvironment.Sandbox` — see §2d |
| `Oauth2` | `OAuth2ClientCredentials?` | `new OAuth2ClientCredentials { ClientId = ..., ClientSecret = ... }` |
| `Server` | `ServerOptions` | Use `Server.Default.Sandbox.BaseUrl` to override the base URL — see §2d |
| `Retry` | `RetryOptions` | `RetryOptions.Default()` as baseline; tune before registering — see Trap notes |

Source: `sdk-map.md` — *Getting a client*; `PayPalServerSdkClientOptions.cs` (SDK source).

---

### 2d. Environment & Base URL — CRITICAL SDK-LEVEL CONSTRAINT

**`ServerEnvironment` has exactly one member: `ServerEnvironment.Sandbox`.**
There is no `Production` member. Setting any other value causes `ArgumentOutOfRangeException` at the first API call (inside the URL resolver).

To target production without a hard-coded environment:

```csharp
options.Environment = ServerEnvironment.Sandbox; // always — the only valid value

options.Server.Default.Sandbox.BaseUrl =
    config["PayPal:BaseUrl"]                              // explicit override wins
    ?? (config["PayPal:Environment"] == "production"
        ? "https://api-m.paypal.com"                      // PayPal production
        : "https://api-m.sandbox.paypal.com");            // SDK default (sandbox)
```

`options.Server.Default.Sandbox.BaseUrl` controls **ALL** outbound calls:
- Every API request (`/v2/checkout/orders`, `/v2/payments/…`, `/v3/vault/…`, `/v1/reporting/…`)
- The OAuth2 token endpoint (`POST {BaseUrl}/v1/oauth2/token`)

There is no separate token-URL override. A single `PayPal:BaseUrl` config value covers everything.

Type chain (all must compile with correct `using`):

| Expression | Declared type | Namespace |
|---|---|---|
| `options.Server` | `ServerOptions` | `PayPalServerSdk` |
| `options.Server.Default` | `DefaultOptions` | `PayPalServerSdk.Servers` |
| `options.Server.Default.Sandbox` | `DefaultOptions.SandboxOptions` | nested in `DefaultOptions` |
| `options.Server.Default.Sandbox.BaseUrl` | `string` | — |

Source: `ServerEnvironment.cs`, `DefaultOptions.cs`, `AuthSchemes.cs`, `PayPalServerSdkClientOptions.cs` (SDK source).

---

### 2e. Auth (OAuth2 Client Credentials)

```csharp
options.Oauth2 = new OAuth2ClientCredentials
{
    ClientId     = config["PayPal:ClientId"]!,
    ClientSecret = config["PayPal:ClientSecret"]!
    // Scope is optional; leave null for default PayPal scope
};
```

Namespace for `OAuth2ClientCredentials`: `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`

Token request mechanics (from SDK source, for reference only — the SDK handles this automatically):
- `POST {BaseUrl}/v1/oauth2/token`
- `Authorization: Basic base64(clientId:clientSecret)`
- Body: `grant_type=client_credentials`

Source: `sdk-map.md` — *Servers & auth*; `OAuth2ClientCredentialsStrategy.cs` (SDK source).

---

### 2f. Operation Signatures & Contracts

---

#### Step 4 — Create PayPal Order (AUTHORIZE intent)

| | |
|---|---|
| Controller | `client.Orders` (source: `Api/Orders.cs`) |
| Method | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| All 5 nullable header params | Nullable, no default — **must pass explicitly** (pass `null` to skip) |
| Idempotency key | `payPalRequestId: $"create-{eShopOrderId}"` — deterministic per order; prevents double-create on retry |
| `prefer` | `"return=minimal"` (we only read `Order.Id` from this response) |
| Returns | `Order` (namespace `PayPalServerSdk.Models`) |
| Read: PayPal order ID | `Order.Id` — store as `Payment.PayPalOrderId` |
| Error | `SdkException<CreateOrderError>` — Case A |
| Error accessors | `TryGetError(out Error)` [400, 401, 422] · `TryGetRawError(out RawError)` [fallback] |
| Source | `operations/Orders.md` |

**`OrderRequest` fields** (namespace `PayPalServerSdk.Models`, source: `records-1-Ac-Pa.md`):

| Field (wire name) | Type | Required? | Value |
|---|---|---|---|
| `Intent (intent)` | `CheckoutPaymentIntent` | **required** | `CheckoutPaymentIntent.Authorize` |
| `PurchaseUnits (purchase_units)` | `IReadOnlyList<PurchaseUnitRequest>` | **required** | Single-element list |
| `PaymentSource (payment_source)` | `PaymentSource?` | optional | `null` here — card/vault supplied in Step 5 |
| `Payer (payer)` | `Payer?` | optional | `null` |
| `ApplicationContext (application_context)` | `OrderApplicationContext?` | optional | `null` |

**`PurchaseUnitRequest` fields** (namespace `PayPalServerSdk.Models`, source: `records-2-Pa-Ve.md`):

| Field (wire name) | Type | Required? | Value |
|---|---|---|---|
| `Amount (amount)` | `AmountWithBreakdown` | **required** | Set `CurrencyCode` and `Value` |
| `CustomId (custom_id)` | `string?` | optional | `eShopOrderId.ToString()` — for reconciliation secondary match |
| `InvoiceId (invoice_id)` | `string?` | optional | `eShopOrderId.ToString()` — for reconciliation primary match |
| `ReferenceId (reference_id)` | `string?` | optional | `null` |

**`AmountWithBreakdown` fields** (namespace `PayPalServerSdk.Models`, source: `records-1-Ac-Pa.md`):

| Field (wire name) | Type | Required? | Value |
|---|---|---|---|
| `CurrencyCode (currency_code)` | `string` | **required** | `config["PayPal:Currency"]` e.g. `"USD"` |
| `Value (value)` | `string` | **required** | Order total as decimal string e.g. `"49.99"` — format to 2 d.p. |
| `Breakdown (breakdown)` | `AmountBreakdown?` | optional | `null` |

---

#### Step 5 — Authorize (hold funds)

| | |
|---|---|
| Controller | `client.Orders` |
| Method | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| `id` | `Payment.PayPalOrderId` (from Step 4) |
| `payPalMockResponse`, `payPalClientMetadataId`, `payPalAuthAssertion` | Explicit `null` |
| `payPalRequestId` | `$"authorize-{eShopOrderId}"` — deterministic idempotency; prevents double-authorize |
| `prefer` | **`"return=representation"`** — required to get authorization IDs in response |
| Returns | `OrderAuthorizeResponse` (namespace `PayPalServerSdk.Models`) |
| Authorization ID | `response.PurchaseUnits[0].Payments.Authorizations[0].Id` — store as `Payment.AuthorizationId` |
| Auth status | `response.PurchaseUnits[0].Payments.Authorizations[0].Status` (type: `AuthorizationStatus?`) |
| Auth expiry | `response.PurchaseUnits[0].Payments.Authorizations[0].ExpirationTime` (type: `string?`) — store as `Payment.AuthorizationExpiryTime` |
| Error | `SdkException<AuthorizeOrderError>` — Case A |
| Error accessors | `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback] |
| Source | `operations/Orders.md`; `records-2-Pa-Ve.md` — `OrderAuthorizeResponse`, `PaymentCollection`, `AuthorizationWithAdditionalData` |

Response navigation:
- `OrderAuthorizeResponse.PurchaseUnits` = `IReadOnlyList<PurchaseUnit>?`
- `PurchaseUnit.Payments` = `PaymentCollection?`
- `PaymentCollection.Authorizations` = `IReadOnlyList<AuthorizationWithAdditionalData>?`
- `AuthorizationWithAdditionalData.Id` = `string?` (the authorization ID)
- `AuthorizationWithAdditionalData.ExpirationTime` = `string?`
- `AuthorizationWithAdditionalData.Status` = `AuthorizationStatus?`

**`OrderAuthorizeRequest` fields** (namespace `PayPalServerSdk.Models`, source: `records-1-Ac-Pa.md`):

| Field | Type | Value |
|---|---|---|
| `PaymentSource (payment_source)` | `OrderAuthorizeRequestPaymentSource?` | Carries card or vault reference |

**`OrderAuthorizeRequestPaymentSource` fields** — for one-off card:

```csharp
new OrderAuthorizeRequestPaymentSource
{
    Card = new CardRequest
    {
        Number       = "4111111111111111",
        Expiry       = "2027-10",    // format: YYYY-MM
        SecurityCode = "123",
        BillingAddress = new Address
        {
            AddressLine1 = "...",
            AdminArea2   = "City",
            AdminArea1   = "ST",
            PostalCode   = "00000",
            CountryCode  = "US"      // CountryCode is required on Address
        }
    }
}
```

For saved card (vault token):

```csharp
new OrderAuthorizeRequestPaymentSource
{
    Card = new CardRequest { VaultId = savedCard.PayPalPaymentTokenId }
}
```

`CardRequest` fields (namespace `PayPalServerSdk.Models`, source: `records-1-Ac-Pa.md`):

| Field (wire name) | Type | Notes |
|---|---|---|
| `Number (number)` | `string?` | PAN — never persisted |
| `Expiry (expiry)` | `string?` | `YYYY-MM` |
| `SecurityCode (security_code)` | `string?` | CVV |
| `BillingAddress (billing_address)` | `Address?` | `CountryCode` required if Address is set |
| `VaultId (vault_id)` | `string?` | Use for saved-card path instead of raw number/expiry/SecurityCode |

`Address` fields (source: `records-1-Ac-Pa.md`):

| Field (wire name) | Type | Required? |
|---|---|---|
| `AddressLine1 (address_line_1)` | `string?` | optional |
| `AddressLine2 (address_line_2)` | `string?` | optional |
| `AdminArea2 (admin_area_2)` | `string?` | optional (city) |
| `AdminArea1 (admin_area_1)` | `string?` | optional (state) |
| `PostalCode (postal_code)` | `string?` | optional |
| `CountryCode (country_code)` | `string` | **required** |

**Note on `processing_instruction`**: `OrderRequest` in this SDK does not have a `processing_instruction` field — the SDK does not expose this PayPal API parameter. When `payment_source` (card or vault) is provided in `AuthorizeOrder`, PayPal processes the card immediately. No separate processing instruction is required for headless direct-card authorization.

---

#### Step 6a — Reauthorize stale authorization

| | |
|---|---|
| Controller | `client.Payments` |
| Method | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| `authorizationId` | `Payment.AuthorizationId` |
| `payPalRequestId`, `payPalAuthAssertion` | Must pass explicitly; use `null` or a deterministic key |
| `body` | `new ReauthorizeRequest { Amount = new Money { CurrencyCode = currency, Value = amountString } }` |
| `prefer` | `"return=minimal"` |
| Returns | `PaymentAuthorization` (namespace `PayPalServerSdk.Models`) |
| Auth ID after reauth | `PaymentAuthorization.Id` (same authorization ID, validity extended) |
| Error | `SdkException<ReauthorizePaymentError>` — Case A |
| Error accessors | `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback] |
| 422 stale-beyond-29d | `TryGetError(out Error)` — `Error.Name` and `Error.Details[0].Issue` contain the reason; map to "authorization expired, shopper must re-initiate payment" response |
| Source | `operations/Payments.md`; `records-2-Pa-Ve.md` — `ReauthorizeRequest`, `PaymentAuthorization` |

**`ReauthorizeRequest` fields** (namespace `PayPalServerSdk.Models`, source: `records-2-Pa-Ve.md`):

| Field (wire name) | Type | Notes |
|---|---|---|
| `Amount (amount)` | `Money?` | Reauthorization amount; set to order total |

**`Money` fields** (namespace `PayPalServerSdk.Models`, source: `records-1-Ac-Pa.md`):

| Field (wire name) | Type | Required? |
|---|---|---|
| `CurrencyCode (currency_code)` | `string` | **required** |
| `Value (value)` | `string` | **required** — decimal string e.g. `"49.99"` |

**Staleness logic**: Call `GetAuthorizedPayment(authorizationId, null, null)` to get the current `PaymentAuthorization.ExpirationTime`. Parse to `DateTimeOffset`. If `now > expiryTime` and `(now - originalAuthorizationCreateTime).TotalDays <= 29`, call `ReauthorizePayment`. If > 29 days, skip reauth and return actionable error to the operator. After capture (Steps 6b), no reauth is needed.

---

#### Step 6b — Capture (take money at fulfilment)

| | |
|---|---|
| Controller | `client.Payments` |
| Method | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| `authorizationId` | `Payment.AuthorizationId` |
| `payPalMockResponse`, `payPalAuthAssertion` | Explicit `null` |
| `payPalRequestId` | `$"capture-{authorizationId}"` — store in `Payment.CaptureIdempotencyKey`; same key = same capture |
| `body` | `new CaptureRequest()` — empty = capture full authorized amount |
| `prefer` | **`"return=representation"`** — required to read `SellerReceivableBreakdown` |
| Returns | `CapturedPayment` (namespace `PayPalServerSdk.Models`) |
| Capture ID | `CapturedPayment.Id` — store as `Payment.CaptureId` |
| Capture status | `CapturedPayment.Status` (type: `CaptureStatus?`) |
| Captured amount | `CapturedPayment.SellerReceivableBreakdown.GrossAmount` (type: `Money`, **required**) |
| PayPal fee | `CapturedPayment.SellerReceivableBreakdown.PaypalFee` (type: `Money?`) |
| Net proceeds | `CapturedPayment.SellerReceivableBreakdown.NetAmount` (type: `Money?`) |
| Error | `SdkException<CaptureAuthorizedPaymentError>` — Case A |
| Error accessors | `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback] |
| 409 conflict | Authorization already captured — treat as idempotent success; read capture data from stored `Payment` record |
| Source | `operations/Payments.md`; `records-1-Ac-Pa.md` — `CapturedPayment`; `records-2-Pa-Ve.md` — `SellerReceivableBreakdown` |

**`SellerReceivableBreakdown` fields** (namespace `PayPalServerSdk.Models`, source: `records-2-Pa-Ve.md`):

| Field (wire name) | Type | Store as |
|---|---|---|
| `GrossAmount (gross_amount)` | `Money` (**required**) | `Payment.CapturedAmountValue` + `Payment.CapturedAmountCurrency` |
| `PaypalFee (paypal_fee)` | `Money?` | `Payment.PayPalFeeValue` + `Payment.PayPalFeeCurrency` |
| `NetAmount (net_amount)` | `Money?` | `Payment.NetAmountValue` + `Payment.NetAmountCurrency` |

**`CaptureRequest` optional fields** (namespace `PayPalServerSdk.Models`, source: `records-1-Ac-Pa.md`):

| Field (wire name) | Type | Notes |
|---|---|---|
| `Amount (amount)` | `Money?` | `null` = capture full authorized amount |
| `FinalCapture (final_capture)` | `bool? = false` | Set `true` if no further captures |
| `InvoiceId (invoice_id)` | `string?` | optional |
| `NoteToPayer (note_to_payer)` | `string?` | optional |

---

#### Step 7 — Void authorization (cancel flow)

| | |
|---|---|
| Controller | `client.Payments` |
| Method | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| `authorizationId` | `Payment.AuthorizationId` |
| `payPalMockResponse`, `payPalAuthAssertion` | Explicit `null` |
| `payPalRequestId` | `null` or `$"void-{authorizationId}"` for idempotency |
| `prefer` | `"return=minimal"` |
| Request body | None — `EmptyBody` is sent automatically by the SDK |
| Returns | `PaymentAuthorization` (namespace `PayPalServerSdk.Models`) |
| Error | `SdkException<VoidPaymentError>` — Case A |
| Error accessors | `TryGetError(out Error)` [401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback] |
| 409 | Already voided — treat as idempotent success |
| Source | `operations/Payments.md` |

Note on 204 response: the SDK always attempts to deserialize the response as `PaymentAuthorization`. If PayPal returns 204 No Content, the resulting object will be empty/default-valued. Treat a successful call (no exception thrown) as a void confirmation regardless of the response body content. (See U2 in §5.)

---

#### Step 8 — Refund (full or partial)

| | |
|---|---|
| Controller | `client.Payments` |
| Method | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| `captureId` | `Payment.CaptureId` |
| `payPalMockResponse`, `payPalAuthAssertion` | Explicit `null` |
| `payPalRequestId` | **Caller's idempotency key** (from API request body) — same key = same refund, idempotent |
| `prefer` | **`"return=representation"`** — required to read `Refund.Id` and status |
| Full refund body | `new RefundRequest()` — all fields optional; omit `Amount` for full refund |
| Partial refund body | `new RefundRequest { Amount = new Money { CurrencyCode = currency, Value = "10.00" } }` |
| Returns | `Refund` (namespace `PayPalServerSdk.Models`) |
| Refund ID | `Refund.Id` — store as `Refund.PayPalRefundId` |
| Refund status | `Refund.Status` (type: `RefundStatus?`) |
| Refund amount | `Refund.Amount` (type: `Money?`) |
| Error | `SdkException<RefundCapturedPaymentError>` — Case A |
| Error accessors | `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback] |
| 409 duplicate | Same idempotency key already used — return existing `Refund` record from DB (do not re-call PayPal) |
| Source | `operations/Payments.md`; `records-2-Pa-Ve.md` — `RefundRequest`, `Refund` |

**Partial refund guard (enforce in application layer before calling SDK)**:

```
requestedAmount <= Payment.CapturedAmountValue - SUM(Refund.AmountValue for Payment)
```

Enforce this in the service layer before calling PayPal; do not rely on PayPal's 422 as the only gate.

**Idempotency contract**: Two partial refunds with **different** caller keys are both legitimate (each creates a distinct `Refund` entity). Two calls with the **same** caller key return the same result — check DB first, short-circuit if found.

**`RefundRequest` fields** (namespace `PayPalServerSdk.Models`, source: `records-2-Pa-Ve.md`):

| Field (wire name) | Type | Notes |
|---|---|---|
| `Amount (amount)` | `Money?` | `null` = full refund of remaining amount |
| `CustomId (custom_id)` | `string?` | optional |
| `InvoiceId (invoice_id)` | `string?` | optional |
| `NoteToPayer (note_to_payer)` | `string?` | optional |

---

#### Step 10 — Reconciliation (paginated transaction search)

| | |
|---|---|
| Controller | `client.TransactionSearch` |
| Method | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| 8 nullable params (`transactionId` … `terminalId`) | **Must pass explicitly** — pass `null` for all |
| `startDate` / `endDate` | ISO-8601 string e.g. `"2024-01-01T00:00:00-0700"` or `DateTimeOffset.UtcNow.ToString("s") + "Z"` |
| `fields` | `"transaction_info"` (default — exposes `TransactionInformation` fields) |
| `pageSize` | `100` (max) |
| `page` | Start `1`; increment each call |
| Returns | `SearchResponse` (namespace `PayPalServerSdk.Models`) |
| Error | `SdkException<RawError>` — **Case B** (not typed) |
| Error accessors | `ex.Error.StatusCode` · `ex.Error.ReadAsString()` · `ex.Error.ReadAsJson<T>()` |
| Source | `operations/TransactionSearch.md`; `records-2-Pa-Ve.md` — `SearchResponse`, `TransactionDetails`, `TransactionInformation` |

**Pagination loop — no SDK auto-pagination; must loop manually**:

```csharp
var allDetails = new List<TransactionDetails>();
int page = 1;
int totalPages;
do
{
    var resp = await client.TransactionSearch.SearchTransactions(
        startDate:               from.ToString("s") + "Z",
        endDate:                 to.ToString("s") + "Z",
        transactionId:           null,
        transactionType:         null,
        transactionStatus:       null,
        transactionAmount:       null,
        transactionCurrency:     null,
        paymentInstrumentType:   null,
        storeId:                 null,
        terminalId:              null,
        pageSize:                100,
        page:                    page,
        ct:                      cancellationToken
    );
    allDetails.AddRange(resp.TransactionDetails ?? []);
    totalPages = resp.TotalPages ?? 1;
    page++;
} while (page <= totalPages);
```

**`SearchResponse` fields** (namespace `PayPalServerSdk.Models`, source: `records-2-Pa-Ve.md`):

| Field (wire name) | Type | Notes |
|---|---|---|
| `TransactionDetails (transaction_details)` | `IReadOnlyList<TransactionDetails>?` | Items on this page |
| `TotalPages (total_pages)` | `int?` | Use for loop termination |
| `Page (page)` | `int?` | Current page |
| `TotalItems (total_items)` | `int?` | Total count |

**`TransactionDetails` key sub-fields** (source: `records-2-Pa-Ve.md` — `TransactionInformation`):

| Property path | Type | Use for |
|---|---|---|
| `.TransactionInfo.TransactionId` | `string?` | PayPal transaction ID |
| `.TransactionInfo.TransactionAmount` | `Money?` | Amount |
| `.TransactionInfo.FeeAmount` | `Money?` | PayPal fee |
| `.TransactionInfo.TransactionStatus` | `string?` | Status (raw string, not enum) |
| `.TransactionInfo.TransactionInitiationDate` | `string?` | Date-time of transaction |
| `.TransactionInfo.PaypalReferenceId` | `string?` | Related entity ID |
| `.TransactionInfo.PaypalReferenceIdType` | `PayPalReferenceIdType?` | `Odr` = PayPal order, `Txn` = transaction |
| `.TransactionInfo.InvoiceId` | `string?` | Invoice ID set on the PayPal order |
| `.TransactionInfo.CustomField` | `string?` | May correspond to `CustomId` set on order — UNVERIFIED (see §5 U1) |

**Matching strategy** — join PayPal transactions to eShop orders:
1. **Primary**: where `PaypalReferenceIdType == PayPalReferenceIdType.Odr`, match `PaypalReferenceId` against `Payment.PayPalOrderId` stored in DB.
2. **Secondary**: match `InvoiceId` against the stored eShop order ID (set as `PurchaseUnitRequest.InvoiceId` in Step 4).
3. Do NOT rely solely on `CustomField` mapping — it is **UNVERIFIED** whether PayPal transaction search returns `custom_id` from the Orders API as `custom_field` here.

`PayPalReferenceIdType` enum values (namespace `PayPalServerSdk.Models.Enums`):

| Member | Wire value | Meaning |
|---|---|---|
| `Odr` | `"ODR"` | PayPal Order |
| `Txn` | `"TXN"` | Transaction |
| `Sub` | `"SUB"` | Subscription |
| `Pap` | `"PAP"` | Pre-Approved Payment |

---

#### Step 11 — Save card (vault)

| | |
|---|---|
| Controller | `client.Vault` |
| Method | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| `payPalRequestId` | Must pass explicitly; use caller's idempotency key or `null` |
| Returns | `PaymentTokenResponse` (namespace `PayPalServerSdk.Models`) |
| Token ID | `PaymentTokenResponse.Id` — store as `SavedCard.PayPalPaymentTokenId` (= `paymentMethodId` in our API) |
| PayPal customer ID | `PaymentTokenResponse.Customer.Id` — store as `SavedCard.PayPalCustomerId` (required for listing) |
| Last four digits | `PaymentTokenResponse.PaymentSource.Card.LastDigits` |
| Card brand | `PaymentTokenResponse.PaymentSource.Card.Brand` (type: `CardBrand?`) |
| Card expiry | `PaymentTokenResponse.PaymentSource.Card.Expiry` |
| Cardholder name | `PaymentTokenResponse.PaymentSource.Card.Name` |
| Error | `SdkException<CreatePaymentTokenError>` — Case A |
| Error accessors | **`TryGetError1(out Error1)`** [400, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback] |
| Source | `operations/Vault.md`; `records-2-Pa-Ve.md` — `PaymentTokenRequest`, `PaymentTokenResponse`; `records-1-Ac-Pa.md` — `CardPaymentTokenEntity` |

Response navigation:
- `PaymentTokenResponse.PaymentSource` = `PaymentTokenResponsePaymentSource?`
- `PaymentTokenResponsePaymentSource.Card` = `CardPaymentTokenEntity?`
- `CardPaymentTokenEntity.LastDigits` = `string?`
- `CardPaymentTokenEntity.Brand` = `CardBrand?`
- `CardPaymentTokenEntity.Expiry` = `string?`

NEVER store the raw card number, security code, or full PAN — only the safe fields above.

**`PaymentTokenRequest` fields** (namespace `PayPalServerSdk.Models`, source: `records-2-Pa-Ve.md`):

| Field (wire name) | Type | Required? | Value |
|---|---|---|---|
| `Customer (customer)` | `Customer?` | optional | `new Customer { MerchantCustomerId = userId.ToString() }` |
| `PaymentSource (payment_source)` | `PaymentTokenRequestPaymentSource` | **required** | See below |

**`PaymentTokenRequestPaymentSource` fields** (source: `records-2-Pa-Ve.md`):

| Field | Type | Notes |
|---|---|---|
| `Card (card)` | `PaymentTokenRequestCard?` | For card vaulting |

**`PaymentTokenRequestCard` fields** (source: `records-2-Pa-Ve.md`):

| Field (wire name) | Type | Notes |
|---|---|---|
| `Name (name)` | `string?` | Cardholder name |
| `Number (number)` | `string?` | PAN — passed to PayPal, never persisted locally |
| `Expiry (expiry)` | `string?` | `YYYY-MM` |
| `SecurityCode (security_code)` | `string?` | CVV — passed to PayPal, never persisted locally |
| `BillingAddress (billing_address)` | `Address?` | `CountryCode` required if Address set |

**`Customer` fields** (namespace `PayPalServerSdk.Models`, source: `records-1-Ac-Pa.md`):

| Field (wire name) | Type | Notes |
|---|---|---|
| `Id (id)` | `string?` | Leave `null` — PayPal creates/finds customer |
| `MerchantCustomerId (merchant_customer_id)` | `string?` | Our user's ID string — enables PayPal to link tokens to our customer |

---

#### Step 12 — List saved cards

| | |
|---|---|
| Controller | `client.Vault` |
| Method | `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| `customerId` | `SavedCard.PayPalCustomerId` (PayPal's customer ID from vault token response — NOT the merchant_customer_id) |
| Returns | `CustomerVaultPaymentTokensResponse` (namespace `PayPalServerSdk.Models`) |
| Token list | `CustomerVaultPaymentTokensResponse.PaymentTokens` (type: `IReadOnlyList<PaymentTokenResponse>?`) |
| Total pages | `CustomerVaultPaymentTokensResponse.TotalPages` |
| Error | `SdkException<ListCustomerPaymentTokensError>` — Case A |
| Error accessors | **`TryGetError1(out Error1)`** [400, 403, 500] · `TryGetRawError(out RawError)` [fallback] |
| Source | `operations/Vault.md`; `records-1-Ac-Pa.md` — `CustomerVaultPaymentTokensResponse` |

Note: `customerId` query param (`customer_id` wire) is PayPal's customer ID, not our `MerchantCustomerId`. Must store `PaymentTokenResponse.Customer.Id` at vault-creation time.

---

#### Step 13 — Delete saved card

| | |
|---|---|
| Controller | `client.Vault` |
| Method | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| `id` | `SavedCard.PayPalPaymentTokenId` |
| Returns | `void` (Task) |
| Error | `SdkException<DeletePaymentTokenError>` — Case A |
| Error accessors | **`TryGetError1(out Error1)`** [400, 403, 500] · `TryGetRawError(out RawError)` [fallback] |
| Source | `operations/Vault.md` |

---

### 2g. Enum Values (in-scope, namespace `PayPalServerSdk.Models.Enums`)

Enums are `StringEnum<T>` — NOT C# enums. Use the static members shown.

| Enum | Members used in this integration | Wire value |
|---|---|---|
| `CheckoutPaymentIntent` | `CheckoutPaymentIntent.Authorize` | `"AUTHORIZE"` |
| `AuthorizationStatus` | `Created`, `Captured`, `Denied`, `PartiallyCaptured`, `Voided`, `Pending` | per name |
| `CaptureStatus` | `Completed`, `Declined`, `PartiallyRefunded`, `Pending`, `Refunded`, `Failed` | per name |
| `RefundStatus` | `Cancelled`, `Failed`, `Pending`, `Completed` | per name |
| `PayPalReferenceIdType` | `Odr` (`"ODR"`), `Txn` (`"TXN"`) | for reconciliation |

Source: `map/models/enums.md`.

---

### 2h. Error Types Summary

| Operation | Exception | Case | Note on accessors |
|---|---|---|---|
| `CreateOrder` | `SdkException<CreateOrderError>` | A | `TryGetError(out Error)` |
| `AuthorizeOrder` | `SdkException<AuthorizeOrderError>` | A | `TryGetError(out Error)` |
| `CaptureAuthorizedPayment` | `SdkException<CaptureAuthorizedPaymentError>` | A | `TryGetError(out Error)` + `TryGetNoContent(out RawError)` [500] |
| `VoidPayment` | `SdkException<VoidPaymentError>` | A | `TryGetError(out Error)` + `TryGetNoContent(out RawError)` [500] |
| `ReauthorizePayment` | `SdkException<ReauthorizePaymentError>` | A | `TryGetError(out Error)` + `TryGetNoContent(out RawError)` [500] |
| `RefundCapturedPayment` | `SdkException<RefundCapturedPaymentError>` | A | `TryGetError(out Error)` + `TryGetNoContent(out RawError)` [500] |
| `CreatePaymentToken` | `SdkException<CreatePaymentTokenError>` | A | **`TryGetError1(out Error1)`** — note `1` suffix |
| `ListCustomerPaymentTokens` | `SdkException<ListCustomerPaymentTokensError>` | A | **`TryGetError1(out Error1)`** |
| `DeletePaymentToken` | `SdkException<DeletePaymentTokenError>` | A | **`TryGetError1(out Error1)`** |
| `SearchTransactions` | `SdkException<RawError>` | **B** | `ex.Error.StatusCode` + `ex.Error.ReadAsString()` — no typed accessor |

**`Error` fields** (namespace `PayPalServerSdk.Models`, source: `records-1-Ac-Pa.md`):
- `Name (name): string !req`
- `Message (message): string !req`
- `DebugId (debug_id): string !req`
- `Details (details): IReadOnlyList<ErrorDetails>?`

**`Error1` fields** (namespace `PayPalServerSdk.Models`, source: `records-1-Ac-Pa.md`) — Vault operations use this, not `Error`:
- `Name (name): string !req`
- `Message (message): string !req`
- `DebugId (debug_id): string !req`
- `Details (details): IReadOnlyList<ErrorDetails1>?`
- `Links (links): IReadOnlyList<ErrorLinkDescription>?`

`Error` and `Error1` are distinct types. Using `TryGetError` on a Vault operation's `CreatePaymentTokenError` will not compile or will silently fail — the correct accessor is `TryGetError1`.

Source: `sdk-map.md` — *Error-handling model*; `operations/Vault.md`, `operations/Payments.md`.

---

### 2i. EF Entity Definitions

#### `Payment` entity

| Column | CLR Type | Notes |
|---|---|---|
| `PaymentId` | `int` / `Guid` | PK |
| `EShopOrderId` | `int` | FK to eShop Order |
| `PayPalOrderId` | `string` | `Order.Id` from `CreateOrder` response |
| `AuthorizationId` | `string?` | `…Authorizations[0].Id` from `AuthorizeOrder` response |
| `AuthorizationStatus` | `string?` | `AuthorizationStatus` wire value |
| `AuthorizationExpiryTime` | `string?` | ISO-8601 from `…Authorizations[0].ExpirationTime` |
| `CaptureId` | `string?` | `CapturedPayment.Id` |
| `CaptureStatus` | `string?` | `CaptureStatus` wire value |
| `CapturedAmountValue` | `string?` | `SellerReceivableBreakdown.GrossAmount.Value` |
| `CapturedAmountCurrency` | `string?` | `SellerReceivableBreakdown.GrossAmount.CurrencyCode` |
| `PayPalFeeValue` | `string?` | `SellerReceivableBreakdown.PaypalFee?.Value` |
| `PayPalFeeCurrency` | `string?` | `SellerReceivableBreakdown.PaypalFee?.CurrencyCode` |
| `NetAmountValue` | `string?` | `SellerReceivableBreakdown.NetAmount?.Value` |
| `NetAmountCurrency` | `string?` | `SellerReceivableBreakdown.NetAmount?.CurrencyCode` |
| `VoidedAt` | `DateTime?` | UTC; set when void completes |
| `CreateIdempotencyKey` | `string` | Stored key for `CreateOrder` (`$"create-{eShopOrderId}"`) |
| `AuthorizeIdempotencyKey` | `string` | Stored key for `AuthorizeOrder` (`$"authorize-{eShopOrderId}"`) |
| `CaptureIdempotencyKey` | `string?` | Stored key for `CaptureAuthorizedPayment` (`$"capture-{authorizationId}"`) |
| `CreatedAt` | `DateTime` | UTC |
| `UpdatedAt` | `DateTime` | UTC |

#### `Refund` entity

| Column | CLR Type | Notes |
|---|---|---|
| `RefundId` | `int` / `Guid` | PK |
| `PaymentId` | `int` | FK to Payment |
| `PayPalRefundId` | `string` | `Refund.Id` from `RefundCapturedPayment` response |
| `CallerIdempotencyKey` | `string` | Caller-supplied key — add unique index to enforce no double-refund |
| `RefundStatus` | `string` | `RefundStatus` wire value |
| `AmountValue` | `string` | `Refund.Amount?.Value` |
| `AmountCurrency` | `string` | `Refund.Amount?.CurrencyCode` |
| `CreatedAt` | `DateTime` | UTC |
| `UpdatedAt` | `DateTime` | UTC |

#### `SavedCard` entity

| Column | CLR Type | Notes |
|---|---|---|
| `SavedCardId` | `int` / `Guid` | PK |
| `ShopperId` | `string` | eShop identity user ID |
| `PayPalPaymentTokenId` | `string` | `PaymentTokenResponse.Id` — the `paymentMethodId` in our API |
| `PayPalCustomerId` | `string` | `PaymentTokenResponse.Customer.Id` — used for `ListCustomerPaymentTokens` |
| `MerchantCustomerId` | `string` | Value set in `Customer.MerchantCustomerId` — our user ID |
| `LastFourDigits` | `string?` | `…PaymentSource.Card.LastDigits` |
| `CardBrand` | `string?` | `…PaymentSource.Card.Brand` (wire value of `CardBrand` enum) |
| `CardExpiry` | `string?` | `…PaymentSource.Card.Expiry` |
| `CardHolderName` | `string?` | `…PaymentSource.Card.Name` |
| `IsDeleted` | `bool` | Soft-delete after `DeletePaymentToken` succeeds |
| `CreatedAt` | `DateTime` | UTC |

---

## 3. Trap Notes

⚠ **Step 2 (environment)** — `ServerEnvironment` has exactly one member (`Sandbox`). Any other value (including `"production"`) causes `ArgumentOutOfRangeException` at the first API call. Production is reached by overriding `options.Server.Default.Sandbox.BaseUrl`, not by changing `options.Environment`. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ **Step 2 (auth namespace)** — `OAuth2ClientCredentials` lives in `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`, not in `PayPalServerSdk.Models` or the root namespace. Missing `using` causes `CS0246`. **MUST load `dotnet-authentication`** before wiring credentials.

⚠ **Steps 4–13 (retry on POSTs)** — the retry and timeout semantics, what `HttpMethodsToRetry` actually gates vs. what transport failures (`HttpRequestException`) bypass on every verb including `POST`, and what `Timeout` bounds (per-attempt, not total) — these are not visible in any signature. A misconfigured retry policy can silently double-authorize, double-capture, or double-refund. **MUST load `dotnet-configuration-resilience`** before configuring retry/timeout.

⚠ **Step 5 (`prefer` parameter)** — the default `prefer = "return=minimal"` does NOT include `PurchaseUnits[].Payments.Authorizations` in the `AuthorizeOrder` response. The authorization ID is unavailable unless `prefer: "return=representation"` is passed explicitly. Same applies to `CaptureAuthorizedPayment` — `SellerReceivableBreakdown` is absent under `"return=minimal"`. **MUST load `dotnet-calling-endpoints`** before writing any operation call.

⚠ **Step 11–13 (Vault error accessor)** — Vault operations use `TryGetError1(out Error1)`, not `TryGetError(out Error)`. `Error1` and `Error` are distinct generated types. Using the wrong accessor silently fails to decode error details. **MUST load `dotnet-error-handling`** before writing any try/catch.

⚠ **All steps (`StringEnum<T>` construction)** — enums are NOT C# enums. Write `CheckoutPaymentIntent.Authorize`, not `CheckoutPaymentIntent.AUTHORIZE` or `(CheckoutPaymentIntent)"AUTHORIZE"`. **MUST load `dotnet-models`** before constructing any enum-typed field.

⚠ **All steps (error boundary — two `JsonException` directions)** — see REQUIRED READING.

---

## 4. REQUIRED READING

Load **all skills below before implementation starts**. The contract sheet deliberately does not carry their contents — each skill has defaults, worked examples, and hazards that a one-line note cannot substitute.

| Skill | Steps governed |
|---|---|
| `dotnet-client-initialization` | Step 2 — client construction, `IHttpClientFactory` lifetime, DI registration pattern |
| `dotnet-authentication` | Step 2 — OAuth2 credential wiring, token refresh, custom `IOAuth2TokenStrategy` |
| `dotnet-calling-endpoints` | Steps 4–13 — named argument binding, positional pitfalls, `prefer` header, request/response envelopes |
| `dotnet-models` | Steps 4–13 — `StringEnum<T>` construction with static members, `required init` record pattern |
| `dotnet-error-handling` | Steps 4–13 — Case A vs Case B, `TryGet…` accessor chain, the two `JsonException` directions |
| `dotnet-configuration-resilience` | Step 2 — retry scope on `POST`, `Timeout` per-attempt vs total, `HttpMethodsToRetry` gate |
| `dotnet-testing` | Any tests covering Steps 4–13 |

**Mandatory `JsonException` hazard rows — these rows must shape the integration boundary:**

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing the integration error boundary.

---

## 5. Assumptions & Blockers

| # | Item |
|---|---|
| A1 | `CheckoutPaymentIntent.Authorize` wire value is `"AUTHORIZE"` (confirmed from `map/models/enums.md`). |
| A2 | Each PayPal order has one purchase unit (eShopOnWeb is single-merchant). |
| A3 | `Money.Value` must be a decimal string formatted to 2 decimal places (e.g. `"49.99"`). The eShop layer is responsible for this formatting. |
| A4 | `PayPal:Currency` config key is required and has no default in this plan. |
| A5 | Sandbox test card: Visa `4111 1111 1111 1111`, any future expiry (format `YYYY-MM`), any 3-digit CVV, billing address required on `Address` with `CountryCode`. |
| A6 | The eShop shopper's user ID (string form) is available at vault-creation time to set as `MerchantCustomerId`. |
| A7 | Authorization expiry logic (stale detection) is based on `AuthorizationWithAdditionalData.ExpirationTime` parsed as `DateTimeOffset`. PayPal's honor period is 3 days; reauth window is day 4–29. |
| U1 | **UNVERIFIED** — Whether `PurchaseUnitRequest.CustomId` (set on the Orders API) maps to `TransactionInformation.CustomField` in the Transaction Search API. Only live traffic can confirm. **Defensive coding**: set both `CustomId` and `InvoiceId` on the order; use `PaypalReferenceId` (type `ODR`) as the primary matching key. Do not build reconciliation logic that fails if `custom_field` is absent. |
| U2 | **UNVERIFIED** — Whether `VoidPayment` returns a populated `PaymentAuthorization` or empty object on HTTP 204. The SDK deserializes the response body unconditionally; a 204 body yields an empty/default record. **Defensive coding**: treat any call that does not throw as a successful void; do not gate on response field values. |
| B1 | No blockers — all SDK contract facts are resolved from the map and SDK source. |
