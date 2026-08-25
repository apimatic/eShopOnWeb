# PayPal SDK Integration Plan — eShopOnWeb `src/PublicApi`
# UPDATED FOR: PayPalServerSDK 2.4.0

> **VERSION NOTE**: This plan was initially written for `AsadAli.Checkout.Sdk` v1.0.1 (the
> bundled map's SDK). The installed package is **`PayPalServerSDK 2.4.0`** — a completely
> different SDK (official PayPal .NET SDK, source: `paypal/PayPal-Dotnet-Server-SDK`, tag `2.4.0`).
> Every contract detail below is grounded in the v2.4.0 source, read directly from the clone.
> The v1.0.1 plan is entirely superseded. Do not refer to the old plan.
>
> **NuGet install**: `dotnet add package PayPalServerSDK` (package ID: `PayPalServerSDK`)

---

## 1. Scope & Sequence

| Step | Description | Operations used | Controller property |
|------|-------------|-----------------|---------------------|
| 1 | Install SDK NuGet package | — | — |
| 2 | Client construction, DI wiring, auth, environment config | — | `PaypalServerSdkClient` |
| 3 | Create PayPal Order (AUTHORIZE intent) | `CreateOrder` | `client.OrdersController` |
| 4 | Authorize Order — raw card | `AuthorizeOrder` | `client.OrdersController` |
| 5 | Authorize Order — vaulted card | `AuthorizeOrder` | `client.OrdersController` |
| 6 | Capture an Authorization | `CaptureAuthorizedPayment` | `client.PaymentsController` |
| 7 | Re-authorize a stale Authorization | `ReauthorizePayment` | `client.PaymentsController` |
| 8 | Void an Authorization | `VoidPayment` | `client.PaymentsController` |
| 9 | Refund a Capture (full or partial) | `RefundCapturedPayment` | `client.PaymentsController` |
| 10 | Vault a Card | `CreatePaymentToken` | `client.VaultController` |
| 11 | List Vaulted Payment Tokens | `ListCustomerPaymentTokens` | `client.VaultController` |
| 12 | Delete a Vaulted Payment Token | `DeletePaymentToken` | `client.VaultController` |
| 13 | Transaction Search (paged, exhaustive) | `SearchTransactions` | `client.TransactionSearchController` |

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier.
> The cancellation-token parameter really is named `cancellationToken` in the async overloads.**
>
> **Every SDK type is written fully-qualified with the namespace the v2.4.0 source gives it.**
> The root namespace is `PaypalServerSdk.Standard` (note: lowercase 'p' in 'Paypal' — NOT
> `PayPalServerSdk`). Models, enums, and controllers are all under this root.

### 2a. Namespaces (add all relevant `using` directives)

| Contents | Namespace |
|----------|-----------|
| Client | `PaypalServerSdk.Standard` |
| `Environment` enum | `PaypalServerSdk.Standard` |
| Controller classes | `PaypalServerSdk.Standard.Controllers` |
| Models, enums, Input classes | `PaypalServerSdk.Standard.Models` |
| Exception types | `PaypalServerSdk.Standard.Exceptions` |
| `ApiResponse<T>` | `PaypalServerSdk.Standard.Http.Response` |
| `ClientCredentialsAuthModel` | `PaypalServerSdk.Standard.Authentication` |
| `HttpClientConfiguration` | `PaypalServerSdk.Standard.Http.Client` |

Source: confirmed from v2.4.0 source file namespace declarations.

---

### 2b. Client Construction & Auth

**SDK architecture (v2.4.0)**: Uses a **builder pattern**. There is no options class named
`PayPalServerSdkClientOptions`. There is no `AddPayPalServerSdkClient` DI extension method.
Construction is via `PaypalServerSdkClient.Builder`.

**Client class**: `PaypalServerSdkClient` (namespace: `PaypalServerSdk.Standard`)

**Builder**:
```csharp
var client = new PaypalServerSdkClient.Builder()
    .Environment(environment)                // PaypalServerSdk.Standard.Environment enum
    .ClientCredentialsAuth(
        new ClientCredentialsAuthModel.Builder(clientId, clientSecret)
            .Build())
    .Build();
```

**`Environment` enum** (namespace: `PaypalServerSdk.Standard`):
This is a standard C# `enum` (not `StringEnum<T>`). Members:

| Member | Wire value | Base URL |
|--------|-----------|----------|
| `Environment.Production` | `"Production"` | `https://api-m.paypal.com` |
| `Environment.Sandbox` | `"Sandbox"` | `https://api-m.sandbox.paypal.com` |

Both `Production` and `Sandbox` exist in v2.4.0. The base URLs are hardcoded in the client's environment map. Selecting `Environment.Production` routes all calls including token requests to the live PayPal API. **No manual `BaseUrl` override is needed** — choosing the enum member is sufficient.

> The v1.0.1 plan's `ServerEnvironment`, `options.Server.Default.Sandbox.BaseUrl` override, and
> `PayPalServerSdkClientOptions` are ALL GONE in v2.4.0. Do not use any of those.

**`ClientCredentialsAuthModel`** (namespace: `PaypalServerSdk.Standard.Authentication`):

| Builder constructor | `ClientCredentialsAuthModel.Builder(string oAuthClientId, string oAuthClientSecret)` |
|---|---|
| Optional `.OAuthToken(OAuthToken)` | Pre-supply a cached token |
| Optional `.OAuthTokenProvider(Func<...>)` | Custom token refresh delegate |
| `.Build()` | Returns `ClientCredentialsAuthModel` |

**Controller accessor properties on `PaypalServerSdkClient`**:

| Property | Type |
|----------|------|
| `client.OrdersController` | `OrdersController` |
| `client.PaymentsController` | `PaymentsController` |
| `client.VaultController` | `VaultController` |
| `client.TransactionSearchController` | `TransactionSearchController` |

Source: `PaypalServerSdkClient.cs` (v2.4.0 source)

**DI wiring**: No built-in DI extension. Register as singleton via:
```csharp
services.AddSingleton(sp =>
    new PaypalServerSdkClient.Builder()
        .Environment(isProduction ? Environment.Production : Environment.Sandbox)
        .ClientCredentialsAuth(
            new ClientCredentialsAuthModel.Builder(
                config["PayPal:ClientId"]!,
                config["PayPal:ClientSecret"]!)
                .Build())
        .HttpClientConfig(c => c.Timeout(TimeSpan.FromSeconds(30)))
        .Build());
```

Source: `PaypalServerSdkClient.cs`, `Authentication/ClientCredentialsAuthManager.cs` (v2.4.0)

---

### 2c. Operation Signatures & Contracts

**CRITICAL ARCHITECTURE DIFFERENCE FROM v1.0.1:**
- v1.0.1: each operation took many individual parameters
- v2.4.0: each operation takes **a single `{Operation}Input` object** — all headers and body are properties on that object
- v2.4.0: each operation returns **`ApiResponse<T>`** — access the response body via `.Data`
- Sync variant: `client.{Controller}.{Operation}(input)` → `ApiResponse<T>`
- Async variant: `client.{Controller}.{Operation}Async(input, cancellationToken)` → `Task<ApiResponse<T>>`

---

#### STEP 3 — Create PayPal Order

**Operation**: `client.OrdersController.CreateOrderAsync(input, cancellationToken)`
**Source**: `Controllers/OrdersController.cs`, `Models/CreateOrderInput.cs` (v2.4.0)

**Input object**: `CreateOrderInput` (namespace: `PaypalServerSdk.Standard.Models`)

| Property | Type | Default | Note |
|----------|------|---------|------|
| `ContentType` | `string` | — | **Required** — must be `"application/json"` |
| `Body` | `Models.OrderRequest` | — | **Required** — the order payload |
| `PaypalRequestId` | `string?` | null | Idempotency key — `PayPal-Request-Id` header |
| `Prefer` | `string?` | `"return=minimal"` | Pass `"return=representation"` to get full response |
| `PaypalMockResponse` | `string?` | null | Sandbox testing only |
| `PaypalPartnerAttributionId` | `string?` | null | Partner attribution |
| `PaypalClientMetadataId` | `string?` | null | Client metadata |
| `PaypalAuthAssertion` | `string?` | null | Auth assertion JWT |

**`OrderRequest`** (namespace: `PaypalServerSdk.Standard.Models`):

| Field (wire name) | Type | Required |
|-------------------|------|----------|
| `Intent` (intent) | `CheckoutPaymentIntent` | **required** |
| `PurchaseUnits` (purchase_units) | `List<PurchaseUnitRequest>` | **required** |
| `Payer` (payer) | `Payer?` | optional |
| `PaymentSource` (payment_source) | `PaymentSource?` | optional |
| `ApplicationContext` (application_context) | `OrderApplicationContext?` | optional |

**`PurchaseUnitRequest`** (namespace: `PaypalServerSdk.Standard.Models`):

| Field (wire name) | Type | Required |
|-------------------|------|----------|
| `Amount` (amount) | `AmountWithBreakdown` | **required** |
| `ReferenceId` (reference_id) | `string?` | optional |
| others | `?` | optional |

**`AmountWithBreakdown`** (namespace: `PaypalServerSdk.Standard.Models`):

| Field (wire name) | Type | Required | Note |
|-------------------|------|----------|------|
| `CurrencyCode` (currency_code) | `string` | **required** | e.g. `"USD"` |
| `Value` (value) | `string` | **required** | Decimal as string, e.g. `"49.99"` — format with `decimal.ToString("F2", CultureInfo.InvariantCulture)` |
| `Breakdown` (breakdown) | `AmountBreakdown?` | optional | |

**`CheckoutPaymentIntent`** — standard C# `enum` (namespace: `PaypalServerSdk.Standard.Models`):
- `CheckoutPaymentIntent.Capture` → wire: `"CAPTURE"`
- `CheckoutPaymentIntent.Authorize` → wire: `"AUTHORIZE"`

**Example**:
```csharp
var order = await client.OrdersController.CreateOrderAsync(
    new CreateOrderInput
    {
        ContentType    = "application/json",
        PaypalRequestId = idempotencyKey,   // caller GUID; null if no idempotency needed
        Prefer         = "return=representation",
        Body = new OrderRequest
        {
            Intent        = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = currencyCode,
                        Value        = amount.ToString("F2", CultureInfo.InvariantCulture),
                    }
                }
            }
        }
    });
string orderId = order.Data.Id;
```

**Return**: `ApiResponse<Models.Order>` — response body in `.Data`
Key fields: `order.Data.Id`, `order.Data.Status`

**Error**: `catch (ErrorException ex)` — `ex.Name`, `ex.Message`, `ex.DebugId`, `ex.Details`
Status codes: 400, 401, 422 throw `ErrorException`; wildcard `"0"` also throws `ErrorException`

**Idempotency**: Set `PaypalRequestId` to a caller-supplied GUID string.

---

#### STEP 4 — Authorize Order (raw card)

**Operation**: `client.OrdersController.AuthorizeOrderAsync(input, cancellationToken)`
**Source**: `Controllers/OrdersController.cs`, `Models/AuthorizeOrderInput.cs` (v2.4.0)

**Input object**: `AuthorizeOrderInput` (namespace: `PaypalServerSdk.Standard.Models`)

| Property | Type | Default | Note |
|----------|------|---------|------|
| `Id` | `string` | — | **Required** — PayPal order ID from Step 3 |
| `ContentType` | `string` | — | **Required** — `"application/json"` |
| `Body` | `Models.OrderAuthorizeRequest?` | null | Payment source details |
| `PaypalRequestId` | `string?` | null | Idempotency key |
| `Prefer` | `string?` | `"return=minimal"` | Use `"return=representation"` to read auth ID |
| `PaypalMockResponse` | `string?` | null | |
| `PaypalClientMetadataId` | `string?` | null | |
| `PaypalAuthAssertion` | `string?` | null | |

**`OrderAuthorizeRequest`** (namespace: `PaypalServerSdk.Standard.Models`):

| Field | Type |
|-------|------|
| `PaymentSource` | `OrderAuthorizeRequestPaymentSource?` |

**`OrderAuthorizeRequestPaymentSource`** (namespace: `PaypalServerSdk.Standard.Models`):

| Field | Type | Purpose |
|-------|------|---------|
| `Card` | `CardRequest?` | Raw card (Step 4) or vault ID (Step 5) |
| `Token` | `Token?` | Billing agreement token |
| `Paypal` | `PayPalWallet?` | PayPal wallet |

**`CardRequest`** (namespace: `PaypalServerSdk.Standard.Models`) — for raw card:

| Field (wire name) | Type | Note |
|-------------------|------|------|
| `Number` (number) | `string?` | Card number (PAN) |
| `Expiry` (expiry) | `string?` | Format: `YYYY-MM` |
| `SecurityCode` (security_code) | `string?` | CVV |
| `Name` (name) | `string?` | Cardholder name |
| `BillingAddress` (billing_address) | `Address?` | Billing address |
| `VaultId` (vault_id) | `string?` | Vault token ID (Step 5 — leave null for raw) |

**`Address`** (namespace: `PaypalServerSdk.Standard.Models`):

| Field (wire name) | Type | Required |
|-------------------|------|----------|
| `CountryCode` (country_code) | `string` | **required** (ISO 3166-1 alpha-2) |
| `AddressLine1` (address_line_1) | `string?` | Street |
| `AdminArea2` (admin_area_2) | `string?` | City |
| `AdminArea1` (admin_area_1) | `string?` | State/Province |
| `PostalCode` (postal_code) | `string?` | Postal code |

**Example**:
```csharp
var resp = await client.OrdersController.AuthorizeOrderAsync(
    new AuthorizeOrderInput
    {
        Id          = orderId,
        ContentType = "application/json",
        Prefer      = "return=representation",
        PaypalRequestId = idempotencyKey,
        Body = new OrderAuthorizeRequest
        {
            PaymentSource = new OrderAuthorizeRequestPaymentSource
            {
                Card = new CardRequest
                {
                    Number       = cardNumber,
                    Expiry       = $"{expiryYear:D4}-{expiryMonth:D2}",  // YYYY-MM
                    SecurityCode = cvv,
                    Name         = cardholderName,
                    BillingAddress = new Address
                    {
                        CountryCode  = countryCode,
                        AddressLine1 = street,
                        AdminArea2   = city,
                        AdminArea1   = state,
                        PostalCode   = postalCode,
                    }
                }
            }
        }
    });
```

**Return**: `ApiResponse<Models.OrderAuthorizeResponse>` — `.Data`

**Response envelope** — `OrderAuthorizeResponse`:

| Path | Type | Value |
|------|------|-------|
| `resp.Data.PurchaseUnits?[0].Payments?.Authorizations?[0].Id` | `string?` | Authorization ID |
| `resp.Data.PurchaseUnits?[0].Payments?.Authorizations?[0].Status` | `AuthorizationStatus?` | Auth status |
| `resp.Data.PurchaseUnits?[0].Payments?.Authorizations?[0].ExpirationTime` | `string?` | ISO-8601 expiry |

`Payments` is `PaymentCollection?`, `Authorizations` is `List<AuthorizationWithAdditionalData>?`.

**Idempotency**: Set `PaypalRequestId`. Same value on retry returns existing authorization without creating a duplicate.

**Error**: `catch (ErrorException ex)` — 400, 401, 403, 404, 422, and wildcard `"0"` all throw `ErrorException`.
Fields: `ex.Name`, `ex.Message`, `ex.DebugId`, `ex.Details` (list of `ErrorDetails`).
Base: `catch (ApiException ex)` — `ex.HttpContext.Response.StatusCode` for any untyped error.

Source: `Controllers/OrdersController.cs`, `Models/AuthorizeOrderInput.cs` (v2.4.0)

---

#### STEP 5 — Authorize Order (vaulted card)

Same operation and input type as Step 4. Use `CardRequest.VaultId` instead of raw card fields:

```csharp
Body = new OrderAuthorizeRequest
{
    PaymentSource = new OrderAuthorizeRequestPaymentSource
    {
        Card = new CardRequest { VaultId = vaultPaymentTokenId }
    }
}
```

All other fields (`Id`, `ContentType`, `Prefer`, `PaypalRequestId`) and response/error handling are identical to Step 4.

---

#### STEP 6 — Capture an Authorization

**Operation**: `client.PaymentsController.CaptureAuthorizedPaymentAsync(input, cancellationToken)`
**Source**: `Controllers/PaymentsController.cs`, `Models/CaptureAuthorizedPaymentInput.cs` (v2.4.0)

**Input object**: `CaptureAuthorizedPaymentInput` (namespace: `PaypalServerSdk.Standard.Models`)

| Property | Type | Default | Note |
|----------|------|---------|------|
| `AuthorizationId` | `string` | — | **Required** — authorization ID |
| `ContentType` | `string` | — | **Required** — `"application/json"` |
| `Body` | `Models.CaptureRequest?` | null | Capture params; null = full capture |
| `PaypalRequestId` | `string?` | null | Idempotency key |
| `Prefer` | `string?` | `"return=minimal"` | Use `"return=representation"` to read amounts |
| `PaypalMockResponse` | `string?` | null | |
| `PaypalAuthAssertion` | `string?` | null | |

**`CaptureRequest`** (namespace: `PaypalServerSdk.Standard.Models`):

| Field (wire name) | Type | Note |
|-------------------|------|------|
| `Amount` (amount) | `Money?` | Set for partial; omit for full capture |
| `FinalCapture` (final_capture) | `bool?` | Default false; set true if last capture |
| `InvoiceId` (invoice_id) | `string?` | optional |
| `NoteToPayer` (note_to_payer) | `string?` | optional |
| `SoftDescriptor` (soft_descriptor) | `string?` | optional |

**`Money`** (namespace: `PaypalServerSdk.Standard.Models`):

| Field (wire name) | Type | Required |
|-------------------|------|----------|
| `CurrencyCode` (currency_code) | `string` | **required** |
| `Value` (value) | `string` | **required** |

**Return**: `ApiResponse<Models.CapturedPayment>` — `.Data`

**Response fields** — `CapturedPayment`:

| Path | Type | Value |
|------|------|-------|
| `resp.Data.Id` | `string?` | Capture ID |
| `resp.Data.Status` | `CaptureStatus?` | Capture status |
| `resp.Data.Amount` | `Money?` | Captured amount |
| `resp.Data.SellerReceivableBreakdown?.PaypalFee` | `Money?` | PayPal fee |
| `resp.Data.SellerReceivableBreakdown?.NetAmount` | `Money?` | Net amount |

**Error**: `catch (ErrorException ex)` for 400, 401, 403, 404, 409, 422.
500 throws `ApiException` (no typed fields). Wildcard `"0"` throws `ErrorException`.

Source: `Controllers/PaymentsController.cs` (v2.4.0)

---

#### STEP 7 — Re-authorize a Stale Authorization

**Operation**: `client.PaymentsController.ReauthorizePaymentAsync(input, cancellationToken)`
**Source**: `Controllers/PaymentsController.cs`, `Models/ReauthorizePaymentInput.cs` (v2.4.0)

**Input object**: `ReauthorizePaymentInput` (namespace: `PaypalServerSdk.Standard.Models`)

| Property | Type | Default | Note |
|----------|------|---------|------|
| `AuthorizationId` | `string` | — | **Required** |
| `ContentType` | `string` | — | **Required** — `"application/json"` |
| `Body` | `Models.ReauthorizeRequest?` | null | Optional amount override |
| `PaypalRequestId` | `string?` | null | Idempotency |
| `Prefer` | `string?` | `"return=minimal"` | Use `"return=representation"` to read new auth ID |
| `PaypalAuthAssertion` | `string?` | null | |

**`ReauthorizeRequest`** (namespace: `PaypalServerSdk.Standard.Models`):

| Field (wire name) | Type | Note |
|-------------------|------|------|
| `Amount` (amount) | `Money?` | Optional — omit to re-auth at original amount |

**Return**: `ApiResponse<Models.PaymentAuthorization>` — `.Data`

| Path | Type | Value |
|------|------|-------|
| `resp.Data.Id` | `string?` | New authorization ID |
| `resp.Data.Status` | `AuthorizationStatus?` | New status |
| `resp.Data.ExpirationTime` | `string?` | ISO-8601 new expiry |

**Error**: `catch (ErrorException ex)` for 400, 401, 403, 404, 422.
500 throws `ApiException`. Wildcard `"0"` throws `ErrorException`.

Operator-facing error reporting: extract `ex.Name` + `ex.Message` + `ex.Details[i].Issue` to surface actionable information.

Source: `Controllers/PaymentsController.cs`, `Models/ReauthorizePaymentInput.cs` (v2.4.0)

---

#### STEP 8 — Void an Authorization

**Operation**: `client.PaymentsController.VoidPaymentAsync(input, cancellationToken)`
**Source**: `Controllers/PaymentsController.cs`, `Models/VoidPaymentInput.cs` (v2.4.0)

**Input object**: `VoidPaymentInput` (namespace: `PaypalServerSdk.Standard.Models`)

| Property | Type | Default | Note |
|----------|------|---------|------|
| `AuthorizationId` | `string` | — | **Required** |
| `Prefer` | `string?` | `"return=minimal"` | |
| `PaypalMockResponse` | `string?` | null | |
| `PaypalAuthAssertion` | `string?` | null | |
| `PaypalRequestId` | `string?` | null | |

No `ContentType` or `Body` — this is a no-body POST. No `ContentType` property on `VoidPaymentInput` (confirmed: not in the source constructor parameters).

**Return**: `ApiResponse<Models.PaymentAuthorization>` — confirm `resp.Data.Status == AuthorizationStatus.Voided`

**Error**: `catch (ErrorException ex)` for 401, 403, 404, 409, 422.
500 throws `ApiException`. Wildcard `"0"` throws `ErrorException`.
Note: 409 = already captured, cannot void.

Source: `Controllers/PaymentsController.cs`, `Models/VoidPaymentInput.cs` (v2.4.0)

---

#### STEP 9 — Refund a Capture

**Operation**: `client.PaymentsController.RefundCapturedPaymentAsync(input, cancellationToken)`
**Source**: `Controllers/PaymentsController.cs`, `Models/RefundCapturedPaymentInput.cs` (v2.4.0)

**Input object**: `RefundCapturedPaymentInput` (namespace: `PaypalServerSdk.Standard.Models`)

| Property | Type | Default | Note |
|----------|------|---------|------|
| `CaptureId` | `string` | — | **Required** |
| `ContentType` | `string` | — | **Required** — `"application/json"` |
| `Body` | `Models.RefundRequest?` | null | null = full refund; set `Amount` for partial |
| `PaypalRequestId` | `string?` | null | **Caller-supplied idempotency key** |
| `Prefer` | `string?` | `"return=minimal"` | Use `"return=representation"` to read refund ID |
| `PaypalMockResponse` | `string?` | null | |
| `PaypalAuthAssertion` | `string?` | null | |

**`RefundRequest`** (namespace: `PaypalServerSdk.Standard.Models`):

| Field (wire name) | Type | Note |
|-------------------|------|------|
| `Amount` (amount) | `Money?` | Set for partial refund; omit for full refund |
| `CustomId` (custom_id) | `string?` | optional |
| `InvoiceId` (invoice_id) | `string?` | optional |
| `NoteToPayer` (note_to_payer) | `string?` | optional |

**Idempotency**: Set `PaypalRequestId` to a caller-supplied key. Same key = same refund returned, no duplicate.

**Return**: `ApiResponse<Models.Refund>` — `.Data`

| Path | Type | Value |
|------|------|-------|
| `resp.Data.Id` | `string?` | Refund ID |
| `resp.Data.Status` | `RefundStatus?` | Refund status |
| `resp.Data.Amount` | `Money?` | Refunded amount |
| `resp.Data.SellerPayableBreakdown?.GrossAmount` | `Money?` | Gross amount |

**Error**: `catch (ErrorException ex)` for 400, 401, 403, 404, 409, 422.
500 throws `ApiException`. Wildcard `"0"` throws `ErrorException`.
Note: 409 = duplicate refund detected (idempotency key used with different parameters).

Source: `Controllers/PaymentsController.cs`, `Models/RefundCapturedPaymentInput.cs` (v2.4.0)

---

#### STEP 10 — Vault a Card

**Operation**: `client.VaultController.CreatePaymentTokenAsync(input, cancellationToken)`
**Source**: `Controllers/VaultController.cs`, `Models/CreatePaymentTokenInput.cs` (v2.4.0)

**Input object**: `CreatePaymentTokenInput` (namespace: `PaypalServerSdk.Standard.Models`)

| Property | Type | Default | Note |
|----------|------|---------|------|
| `ContentType` | `string` | — | **Required** — `"application/json"` |
| `Body` | `Models.PaymentTokenRequest` | — | **Required** |
| `PaypalRequestId` | `string?` | null | Idempotency |

**`PaymentTokenRequest`** (namespace: `PaypalServerSdk.Standard.Models`):

| Field (wire name) | Type | Required |
|-------------------|------|----------|
| `Customer` (customer) | `Customer?` | optional — supply to scope to a customer |
| `PaymentSource` (payment_source) | `PaymentTokenRequestPaymentSource` | **required** |

**`Customer`** (namespace: `PaypalServerSdk.Standard.Models`):

| Field (wire name) | Type | Note |
|-------------------|------|------|
| `Id` (id) | `string?` | Our user's customer ID |
| `MerchantCustomerId` (merchant_customer_id) | `string?` | Alternative merchant-side ID |

**`PaymentTokenRequestPaymentSource`**:

| Field (wire name) | Type | Note |
|-------------------|------|------|
| `Card` (card) | `PaymentTokenRequestCard?` | Card to vault |
| `Token` (token) | `VaultTokenRequest?` | Vault from setup token |

**`PaymentTokenRequestCard`** (namespace: `PaypalServerSdk.Standard.Models`):

| Field (wire name) | Type | Note |
|-------------------|------|------|
| `Number` (number) | `string?` | Card number |
| `Expiry` (expiry) | `string?` | Format: `YYYY-MM` |
| `SecurityCode` (security_code) | `string?` | CVV |
| `Name` (name) | `string?` | Cardholder name |
| `BillingAddress` (billing_address) | `Address?` | optional |
| `Brand` (brand) | `CardBrand?` | optional |

**SECURITY**: Never persist `Number` or `SecurityCode` in our DB.

**Return**: `ApiResponse<Models.PaymentTokenResponse>` — `.Data`

| Path | Type | Value |
|------|------|-------|
| `resp.Data.Id` | `string?` | Vault payment token ID — persist this |
| `resp.Data.PaymentSource?.Card?.LastDigits` | `string?` | Last 4 digits |
| `resp.Data.PaymentSource?.Card?.Brand` | `CardBrand?` | Card brand enum |
| `resp.Data.PaymentSource?.Card?.Expiry` | `string?` | `YYYY-MM` format |
| `resp.Data.PaymentSource?.Card?.Type` | `CardType?` | Credit/Debit/etc |

**Error**: `catch (ErrorException ex)` for 400, 403, 404, 422, 500. Wildcard `"0"` also `ErrorException`.

> The v1.0.1 plan's distinction between `Error` and `Error1` with `TryGetError` vs `TryGetError1` is
> GONE in v2.4.0. All vault operations throw `ErrorException` — the same type as Orders/Payments.

Source: `Controllers/VaultController.cs`, `Models/CreatePaymentTokenInput.cs` (v2.4.0)

---

#### STEP 11 — List Vaulted Payment Tokens

**Operation**: `client.VaultController.ListCustomerPaymentTokensAsync(input, cancellationToken)`
**Source**: `Controllers/VaultController.cs`, `Models/ListCustomerPaymentTokensInput.cs` (v2.4.0)

**Input object**: `ListCustomerPaymentTokensInput` (namespace: `PaypalServerSdk.Standard.Models`)

Constructor: `ListCustomerPaymentTokensInput(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false)`

| Property | Type | Default |
|----------|------|---------|
| `CustomerId` | `string` | **Required** |
| `PageSize` | `int?` | 5 |
| `Page` | `int?` | 1 |
| `TotalRequired` | `bool?` | false |

Wire names (query params): `customer_id`, `page_size`, `page`, `total_required`.

**Return**: `ApiResponse<Models.CustomerVaultPaymentTokensResponse>` — `.Data`

| Field | Type | Value |
|-------|------|-------|
| `resp.Data.TotalItems` | `int?` | Total count (only when `TotalRequired: true`) |
| `resp.Data.TotalPages` | `int?` | Total pages |
| `resp.Data.PaymentTokens` | `List<PaymentTokenResponse>?` | Tokens on this page |

Per token: `token.Id`, `token.PaymentSource?.Card?.LastDigits`, `.Brand`, `.Expiry`, `.Type`

**Exhaustive pagination loop**:
```csharp
var allTokens = new List<PaymentTokenResponse>();
int page = 1;
CustomerVaultPaymentTokensResponse resp;
do {
    var apiResp = await client.VaultController.ListCustomerPaymentTokensAsync(
        new ListCustomerPaymentTokensInput(customerId, pageSize: 100, page: page, totalRequired: false));
    resp = apiResp.Data;
    if (resp.PaymentTokens is { Count: > 0 } tokens)
        allTokens.AddRange(tokens);
    page++;
} while (page <= (resp.TotalPages ?? 1));
```

**Error**: `catch (ErrorException ex)` for 400, 403, 500. Wildcard `"0"` also `ErrorException`.

Source: `Controllers/VaultController.cs`, `Models/ListCustomerPaymentTokensInput.cs` (v2.4.0)

---

#### STEP 12 — Delete a Vaulted Payment Token

**Operation**: `client.VaultController.DeletePaymentTokenAsync(id, cancellationToken)`
**Source**: `Controllers/VaultController.cs` (v2.4.0)

**Signature**: `DeletePaymentTokenAsync(string id, CancellationToken cancellationToken = default) → Task`

> This operation does NOT take an `Input` object — it takes the token ID directly as a `string` parameter.
> This is the one exception to the "single Input object" pattern among the in-scope operations.

Returns void (Task). No response body. Success = no exception thrown.

**Error**: `catch (ErrorException ex)` for 403, 404, 422, 500. Wildcard `"0"` also `ErrorException`.
Treat 404 as "already deleted / not found" — check `ex.HttpContext.Response.StatusCode` via base `ApiException`.

Source: `Controllers/VaultController.cs` (v2.4.0)

---

#### STEP 13 — Transaction Search (exhaustive paged)

**Operation**: `client.TransactionSearchController.SearchTransactionsAsync(input, cancellationToken)`
**Source**: `Controllers/TransactionSearchController.cs`, `Models/SearchTransactionsInput.cs` (v2.4.0)

**Input object**: `SearchTransactionsInput` (namespace: `PaypalServerSdk.Standard.Models`)

| Property | Type | Default | Note |
|----------|------|---------|------|
| `StartDate` | `string` | — | **Required** — ISO-8601, e.g. `"2024-01-01T00:00:00-0700"` |
| `EndDate` | `string` | — | **Required** — ISO-8601 |
| `TransactionId` | `string?` | null | |
| `TransactionType` | `string?` | null | |
| `TransactionStatus` | `string?` | null | |
| `TransactionAmount` | `string?` | null | |
| `TransactionCurrency` | `string?` | null | |
| `PaymentInstrumentType` | `string?` | null | |
| `StoreId` | `string?` | null | |
| `TerminalId` | `string?` | null | |
| `Fields` | `string?` | `"transaction_info"` | Pass `"all"` for all fields |
| `BalanceAffectingRecordsOnly` | `string?` | — | |
| `PageSize` | `int?` | — | Max 500 per call |
| `Page` | `int?` | — | 1-based |

**Return**: `ApiResponse<Models.SearchResponse>` — `.Data`

| Field | Type | Value |
|-------|------|-------|
| `resp.Data.TransactionDetails` | `List<TransactionDetails>?` | Transactions on this page |
| `resp.Data.Page` | `int?` | Current page |
| `resp.Data.TotalItems` | `int?` | Total count |
| `resp.Data.TotalPages` | `int?` | Total pages |

Per `TransactionDetails`:
```
td.TransactionInfo?.TransactionId          // PayPal transaction ID
td.TransactionInfo?.TransactionStatus      // string status
td.TransactionInfo?.TransactionAmount      // Money? (amount + currency)
td.TransactionInfo?.TransactionInitiationDate // ISO-8601 datetime
td.TransactionInfo?.PaypalReferenceId      // related order/capture ID
td.TransactionInfo?.PaypalReferenceIdType  // PayPalReferenceIdType? (Odr, Txn, Sub, Pap)
```

**Error**: `catch (SearchErrorException ex)` — `ex.Name`, `ex.Message`, `ex.DebugId`, `ex.Details` (wildcard `"0"`).
This is a **typed exception** (`SearchErrorException` inheriting from `ApiException`) — NOT `RawError` as in v1.0.1.

> The v1.0.1 plan stated SearchTransactions was Case B (`SdkException<RawError>`). In v2.4.0 it
> throws `SearchErrorException` which has typed fields. Update any error boundary accordingly.

**Exhaustive pagination**:
```csharp
var allTransactions = new List<TransactionDetails>();
int page = 1;
SearchResponse resp;
do {
    var apiResp = await client.TransactionSearchController.SearchTransactionsAsync(
        new SearchTransactionsInput
        {
            StartDate = startIso, EndDate = endIso,
            Fields = "transaction_info",
            PageSize = 100, Page = page,
        });
    resp = apiResp.Data;
    if (resp.TransactionDetails is { Count: > 0 } details)
        allTransactions.AddRange(details);
    page++;
} while (page <= (resp.TotalPages ?? 1));
```

Source: `Controllers/TransactionSearchController.cs`, `Models/SearchTransactionsInput.cs` (v2.4.0)

---

### 2d. Relevant Enum Values

**Enums in v2.4.0 are standard C# enums** with `[EnumMember(Value = "...")]` attributes. They are NOT `StringEnum<T>`. Use `CheckoutPaymentIntent.Authorize`, `Environment.Production`, etc. directly — no `.FromValue()` builder call needed.

All in namespace `PaypalServerSdk.Standard.Models` except `Environment` which is in `PaypalServerSdk.Standard`.

| Enum | C# members (wire value) | Namespace |
|------|-------------------------|-----------|
| `Environment` | `Production ("Production")`, `Sandbox ("Sandbox")` | `PaypalServerSdk.Standard` |
| `CheckoutPaymentIntent` | `Capture ("CAPTURE")`, `Authorize ("AUTHORIZE")`, `_Unknown` | `PaypalServerSdk.Standard.Models` |
| `AuthorizationStatus` | `Created`, `Captured`, `Denied`, `PartiallyCaptured`, `Voided`, `Pending` | `PaypalServerSdk.Standard.Models` |
| `CaptureStatus` | `Completed`, `Declined`, `PartiallyRefunded`, `Pending`, `Refunded`, `Failed` | `PaypalServerSdk.Standard.Models` |
| `RefundStatus` | `Cancelled`, `Failed`, `Pending`, `Completed` | `PaypalServerSdk.Standard.Models` |
| `OrderStatus` | `Created`, `Saved`, `Approved`, `Voided`, `Completed`, `PayerActionRequired` | `PaypalServerSdk.Standard.Models` |
| `CardBrand` | `Visa`, `Mastercard`, `Discover`, `Amex`, `Jcb`, `Diners`, `Unknown`, and others | `PaypalServerSdk.Standard.Models` |
| `CardType` | `Credit`, `Debit`, `Prepaid`, `Store`, `Unknown` | `PaypalServerSdk.Standard.Models` |

Source: `Models/CheckoutPaymentIntent.cs`, `Models/AuthorizationStatus.cs`, `Environment.cs` (v2.4.0 source)

---

### 2e. SDK-Level Idempotency

Idempotency key is the `PaypalRequestId` property on each `{Operation}Input` object. It is sent as the `PayPal-Request-Id` HTTP header. No separate SDK mechanism.

| Operation | Input type | Idempotency property |
|-----------|-----------|----------------------|
| `CreateOrder` | `CreateOrderInput` | `.PaypalRequestId` |
| `AuthorizeOrder` | `AuthorizeOrderInput` | `.PaypalRequestId` |
| `CaptureAuthorizedPayment` | `CaptureAuthorizedPaymentInput` | `.PaypalRequestId` |
| `ReauthorizePayment` | `ReauthorizePaymentInput` | `.PaypalRequestId` |
| `RefundCapturedPayment` | `RefundCapturedPaymentInput` | `.PaypalRequestId` — **caller must supply per-refund key** |
| `CreatePaymentToken` | `CreatePaymentTokenInput` | `.PaypalRequestId` |

UNVERIFIED: PayPal's idempotency window duration (how long a key is honored after first use).

Source: Input model constructors (v2.4.0 source)

---

### 2f. Error Handling Summary (v2.4.0)

**No `SdkException<T>`, no `TryGetError()` / `TryGetRawError()` — those are v1.0.1 patterns only.**

All exceptions inherit from `ApiException` (namespace: `PaypalServerSdk.Standard.Exceptions`).

| Exception type | Used by | Fields |
|----------------|---------|--------|
| `ErrorException` | CreateOrder, AuthorizeOrder, CaptureAuthorizedPayment, ReauthorizePayment, VoidPayment, RefundCapturedPayment, all Vault operations | `Name`, `Message`, `DebugId`, `Details` (list of `ErrorDetails`), `Links` |
| `SearchErrorException` | SearchTransactions | `Name`, `Message`, `DebugId`, `Details`, `TotalItems`, `MaximumItems` |
| `DefaultErrorException` | SearchBalances | `Name`, `Message`, `DebugId`, `Details` |
| `ApiException` (base) | 500 errors on Payments operations, fallback | `HttpContext.Response.StatusCode`, `Message` |

**Catch pattern**:
```csharp
try { var resp = await client.OrdersController.CreateOrderAsync(input); }
catch (ErrorException ex)
{
    // ex.Name, ex.Message, ex.DebugId, ex.Details[i].Issue
    var statusCode = ex.HttpContext.Response.StatusCode;
}
catch (ApiException ex)
{
    // Base fallback for 500 or any untyped error
    var statusCode = ex.HttpContext.Response.StatusCode;
}
```

Source: `Exceptions/ErrorException.cs`, `Exceptions/SearchErrorException.cs`, `Exceptions/ApiException.cs` (v2.4.0)

---

## 3. Trap Notes

- **Step 2 (namespace)** — the root namespace is `PaypalServerSdk.Standard` (lowercase 'p' in Paypal), NOT `PayPalServerSdk`. Wrong namespace → entire build fails. **MUST load `dotnet-client-initialization`** before writing the DI registration.

- **Step 2 (no options class, no DI extension)** — v2.4.0 has no `PayPalServerSdkClientOptions` and no `AddPayPalServerSdkClient` extension method. Construction is via `PaypalServerSdkClient.Builder`. **MUST load `dotnet-client-initialization`** for the DI lifetime and `IHttpClientFactory` pattern applicable to this builder.

- **Step 2 (auth shape change)** — auth is `ClientCredentialsAuthModel` with its own `.Builder(clientId, clientSecret)` sub-builder pattern; NOT `OAuth2ClientCredentials`. Namespace: `PaypalServerSdk.Standard.Authentication`. **MUST load `dotnet-authentication`** before wiring credentials.

- **Step 2 (environment — Production now exists)** — v2.4.0 has both `Environment.Production` and `Environment.Sandbox`. Choosing the enum member is sufficient; NO manual BaseUrl override needed. The v1.0.1 approach of `options.Server.Default.Sandbox.BaseUrl = "https://api-m.paypal.com"` does not apply.

- **Step 2 (retry — non-idempotent writes)** — retry configuration is on `HttpClientConfiguration.Builder` (`.RetryInterval()`, `.MaximumRetryWaitTime()`, `.RequestMethodsToRetry()`). Transport failures may retry POSTs. **MUST load `dotnet-configuration-resilience`** before configuring retry options.

- **Steps 3–13 (Input objects, not flat params)** — every operation (except `DeletePaymentToken`) takes a single `{Operation}Input` object; there are no flat parameter lists. `ContentType = "application/json"` is a required property on POST Input objects and must be set. **MUST load `dotnet-calling-endpoints`** before writing the first call.

- **Steps 3–13 (ApiResponse wrapper)** — all operations return `ApiResponse<T>`, not `T` directly. Response body is in `.Data`. Forgetting `.Data` results in using the wrapper type, not the model.

- **Steps 3–13 (prefer: return=representation)** — with default `"return=minimal"`, response body content for reads (auth ID, capture ID, amounts, refund ID) is UNVERIFIED. Pass `Prefer = "return=representation"` on all operations where you read from the response.

- **Step 12 (DeletePaymentToken signature exception)** — `DeletePaymentToken(string id)` takes the ID directly, NOT an Input object. This is the only in-scope operation with a flat parameter.

- **Step 13 (SearchTransactions error type)** — throws `SearchErrorException`, not `ErrorException` and not `RawError`. A catch for `ErrorException` alone will NOT catch search errors.

- **All steps (enums are standard C# enums, not StringEnum)** — use `CheckoutPaymentIntent.Authorize` directly; no `.FromValue("AUTHORIZE")` needed. **MUST load `dotnet-models`** for JSON serialization behavior (Newtonsoft.Json, not System.Text.Json).

- **All steps (JSON library)** — v2.4.0 uses **Newtonsoft.Json**, not `System.Text.Json`. The `JsonException` boundary note below still applies but the deserialization stack is different. **MUST load `dotnet-models`** and **MUST load `dotnet-error-handling`** before writing the boundary.

- **All steps (error boundary — `JsonException` in two directions)**:
  - A drifted or malformed **2xx** body surfaces as a `JsonException` from Newtonsoft deserialization, **not** as an `ErrorException` — an exception-only catch ladder for `ErrorException`/`ApiException` lets it escape the integration boundary.
  - A **non-2xx** body that does not match the expected error shape may throw `JsonException` while the error object is being constructed, replacing the `ApiException` — the HTTP status is destroyed with it.
  - **MUST load `dotnet-error-handling`** before writing the error boundary.

---

## 4. REQUIRED READING

Load all of the following **before implementation starts**. The contract sheet deliberately does not carry the contents of these skills.

| Skill | Governs | Step(s) |
|-------|---------|---------|
| `dotnet-client-initialization` | Builder-pattern client construction, HTTP lifetime, DI wiring | Step 2 |
| `dotnet-authentication` | `ClientCredentialsAuthModel` builder, token refresh, 401 handling | Step 2 |
| `dotnet-calling-endpoints` | Input objects, `ApiResponse<T>.Data`, async usage, cancellation | Steps 3–13 |
| `dotnet-models` | Newtonsoft.Json enums, standard C# enum usage, init-only records | Steps 3–13 |
| `dotnet-error-handling` | `ErrorException`/`SearchErrorException`/`ApiException` mechanics, `JsonException` boundary | Steps 3–13 |
| `dotnet-configuration-resilience` | Retry on POSTs, `Timeout` scope, `HttpClientConfiguration.Builder` | Step 2, Steps 3–13 |
| `dotnet-testing` | `HttpClient` seam for unit tests | All test steps |

---

## 5. Assumptions & Blockers

| # | Item | Type |
|---|------|------|
| 1 | Currency code (e.g. `"USD"`) comes from application config, not hardcoded. | Assumption |
| 2 | `PayPal:ClientId` and `PayPal:ClientSecret` are available in `IConfiguration` and never hardcoded. | Assumption |
| 3 | "Environment: sandbox/live" config string is translated to `Environment.Sandbox` or `Environment.Production` — both are valid enum members in v2.4.0. No BaseUrl override needed. | Confirmed |
| 4 | **Custom BaseUrl override (confirmed from v2.4.0 source)**: There is no `.BaseUrl()` method on `PaypalServerSdkClient.Builder` or on `HttpClientConfiguration.Builder`. The only clean hook is `HttpClientConfiguration.Builder.HttpClientInstance(HttpClient httpClientInstance, bool overrideHttpClientConfiguration = true)`. Inject a pre-built `HttpClient` that carries a `DelegatingHandler` which rewrites `request.RequestUri` to replace the host/scheme with the custom base URL. Because the SDK's auth manager drives token requests through the same `GlobalConfiguration` (and thus the same `HttpClient`), the handler intercepts ALL calls — API calls and OAuth2 token calls alike. Pattern: `builder.HttpClientConfig(c => c.HttpClientInstance(new HttpClient(new BaseUrlRewritingHandler(customBaseUrl) { InnerHandler = new HttpClientHandler() }), overrideHttpClientConfiguration: true))`. When `PayPal:BaseUrl` is not configured, do not inject an HttpClientInstance and let the SDK use `Environment.Production` or `Environment.Sandbox` normally. | Confirmed — `DelegatingHandler` is the correct approach |
| 5 | PCI SAQ D compliance must be confirmed before shipping Steps 4 (raw card authorize) and 10 (vault card) — raw card details are transmitted through `src/PublicApi`. | Blocker |
| 6 | `prefer: "return=representation"` is recommended for all operations that read response fields; behavior with `"return=minimal"` for response body content is UNVERIFIED (only live traffic confirms it). | UNVERIFIED |
| 7 | PayPal's idempotency window duration for `PaypalRequestId` is UNVERIFIED. | UNVERIFIED |
| 8 | v2.4.0 uses Newtonsoft.Json, not System.Text.Json. If the rest of `src/PublicApi` uses System.Text.Json, model serialization in SDK responses will use Newtonsoft while the rest of the app uses STJ — they are independent, so no conflict is expected, but implementer should confirm no cross-boundary issues. | Assumption |

---

## Appendix: Complete v1.0.1 → v2.4.0 Change Summary

| Concern | v1.0.1 (`AsadAli.Checkout.Sdk`) | v2.4.0 (`PayPalServerSDK`) |
|---------|--------------------------------|---------------------------|
| Root namespace | `PayPalServerSdk` | `PaypalServerSdk.Standard` |
| Client class | `PayPalServerSdkClient` | `PaypalServerSdkClient` |
| Client construction | `new PayPalServerSdkClient(httpClient, options)` | `new PaypalServerSdkClient.Builder().…Build()` |
| Options class | `PayPalServerSdkClientOptions` | None — builder properties |
| DI extension | `AddPayPalServerSdkClient(o => …)` | Not present — use `services.AddSingleton(…Builder…Build())` |
| Environment | `ServerEnvironment` (only `Sandbox`) | `enum Environment` (`Sandbox` + `Production`) |
| Auth | `OAuth2ClientCredentials { ClientId, ClientSecret }` | `ClientCredentialsAuthModel.Builder(id, secret).Build()` |
| Base URL override | `options.Server.Default.Sandbox.BaseUrl` | Not needed — choose `Environment.Production` |
| Controller access | `client.Orders`, `client.Payments`, etc. | `client.OrdersController`, `client.PaymentsController`, etc. |
| Operation signature | Many flat params | Single `{Operation}Input` object |
| Return type | `Task<T>` | `Task<ApiResponse<T>>` — body in `.Data` |
| Error base | `SdkException<T>` | `ApiException` |
| Main error type | Case A: `SdkException<AuthorizeOrderError>` + `TryGetError()` | `ErrorException` (direct fields) |
| Search error type | Case B: `SdkException<RawError>` | `SearchErrorException` (typed fields) |
| Vault error accessor | `TryGetError1()` (not `TryGetError`) | `ErrorException` — same as Orders/Payments |
| Enums | `StringEnum<T>` — use `.FromValue()` or static members | Standard C# `enum` — use members directly |
| JSON library | System.Text.Json | Newtonsoft.Json |
