# PayPal .NET SDK — Contract Sheet & Integration Plan (direct card payments, AUTHORIZE flow)

SDK: `AsadAli.Checkout.Sdk` (NuGet, install version-less) · root namespace `PayPalServerSdk` · client `PayPalServerSdkClient` · map release `v1.0.1` / commit `9653d18`.
Scope: C#/.NET 8 ASP.NET Core (eShopOnWeb). Every fact below is grounded in the bundled SDK map (pages cited per row) or, where noted, the SDK source at that tag.

---

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 1 | Client construction, auth, base-URL override, DI | `PayPalServerSdkClient` ctor / `AddPayPalServerSdkClient`; `OAuth2ClientCredentials`; `options.Server` |
| 2 | Create AUTHORIZE-intent order, paid with raw card | `client.Orders.CreateOrder` |
| 3 | Authorize the order (place the hold) | inline via CreateOrder (`payments.authorizations`) **or** `client.Orders.AuthorizeOrder` |
| 4 | Capture an authorization at fulfilment | `client.Payments.CaptureAuthorizedPayment` |
| 5 | Reauthorize a stale authorization | `client.Payments.ReauthorizePayment` |
| 6 | Void an authorization | `client.Payments.VoidPayment` |
| 7 | Refund a captured payment (full/partial) | `client.Payments.RefundCapturedPayment` |
| 8 | Re-read state (order / auth / capture / refund) | `client.Orders.GetOrder`, `client.Payments.GetAuthorizedPayment`, `client.Payments.GetCapturedPayment`, `client.Payments.GetRefund` |
| 9 | Vault a card & pay with it | `client.Vault.CreateSetupToken`, `client.Vault.CreatePaymentToken`, `client.Vault.DeletePaymentToken`; pay via `CardRequest.VaultId` |
| 10 | Transaction search / reconciliation | `client.TransactionSearch.SearchTransactions` |
| 11 | Error boundary | `SdkException<TError>` (see §Error handling) |

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

### Namespaces (add a `using` per kind — child namespaces are NOT imported transitively)

| Kind | Namespace |
|---|---|
| Client & `PayPalServerSdkClientOptions`, `ServerOptions`, `Server` | `PayPalServerSdk` |
| Controllers (`client.Orders` etc.) | `PayPalServerSdk.Api` |
| Records (request/response models) | `PayPalServerSdk.Models` |
| Enums | `PayPalServerSdk.Models.Enums` |
| Typed error classes (`CreateOrderError`, `Error`, `Error1`, `DefaultError` are records in `.Models`; the `{Op}Error` wrappers) | `PayPalServerSdk.Errors` |
| `ServerEnvironment`, `DefaultOptions` | `PayPalServerSdk.Servers` |
| `OAuth2ClientCredentials`, `IOAuth2TokenStrategy<>` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` / `PayPalServerSdk.Core.Authentication.OAuth2` |
| `RetryOptions` | `PayPalServerSdk.Core.Configuration` |
| `SdkException<TError>` | `PayPalServerSdk.Core.Exceptions` |
| `RawError` | `PayPalServerSdk.Core.ErrorResponse` |

---

### 1 — Client construction, auth, environments, base-URL override  (source: `sdk-map.md` "Getting a client" / "Servers & auth"; SDK source `PayPalServerSdkClientOptions.cs`, `Server.cs`, `Servers/ServerEnvironment.cs`, `Servers/DefaultOptions.cs`, `AuthSchemes.cs`, `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs`)

- **Constructor**: `new PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)`. DI alternative: `services.AddPayPalServerSdkClient(o => { … })` (`ServiceCollectionExtensions.cs`).
- **`PayPalServerSdkClientOptions` properties** (source `PayPalServerSdkClientOptions.cs`): `Environment: ServerEnvironment` (default `ServerEnvironment.Default()` = `Sandbox`), `Retry: RetryOptions`, `Logging: LoggingOptions`, `Server: ServerOptions`, `Oauth2: OAuth2ClientCredentials?`, `Oauth2TokenStrategy: IOAuth2TokenStrategy<OAuth2ClientCredentials>?`.
- **Auth scheme = OAuth2 client credentials.** Set `options.Oauth2 = new OAuth2ClientCredentials { ClientId = …, ClientSecret = …, Scope = null }`. Type (source-confirmed): `OAuth2ClientCredentials` — `required string ClientId`, `required string ClientSecret`, `string? Scope` (namespace `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`).
- **Token fetch is automatic.** When `Oauth2TokenStrategy` is left null, the client wires `OAuth2ClientCredentialsStrategy.ForBasicAuthRequest(server.Default("/v1/oauth2/token"), rawClient)` for you (source `AuthSchemes.cs`): it exchanges ClientId/ClientSecret (HTTP Basic) at `/v1/oauth2/token` and attaches the bearer token to every call. You do not fetch or attach the token manually. (Caching/refresh semantics: **MUST load `dotnet-authentication`** — do not assume.)
- **Environments — GAP: only `Sandbox` is exposed.** `ServerEnvironment` (source `Servers/ServerEnvironment.cs`) declares exactly one member, `ServerEnvironment.Sandbox`; `Default()` returns it and `Match<T>` throws `ArgumentOutOfRangeException` for anything else. **There is NO `ServerEnvironment.Production`.** You cannot select Production by an environment enum — you reach Production via the base-URL override below.
- **Base-URL override — applies to ALL calls including the token request.** The base URL lives at `options.Server.Default.Sandbox.BaseUrl` (default `"https://api-m.sandbox.paypal.com"`; source `Servers/DefaultOptions.cs`). Every request path, **including the OAuth2 token endpoint** `/v1/oauth2/token`, is resolved through `Server.Default(path)` → `DefaultOptions.Resolve(environment, path)` → `Sandbox.BaseUrl` (source `Server.cs`, `AuthSchemes.cs`). Therefore setting this one string redirects the token request and every operation together. Wire it as:
  ```csharp
  options.Server = new PayPalServerSdk.ServerOptions
  {
      Default = new PayPalServerSdk.Servers.DefaultOptions
      {
          Sandbox = new PayPalServerSdk.Servers.DefaultOptions.SandboxOptions { BaseUrl = payPalBaseUrl }
      }
  };
  ```
  For Production without an override, set `BaseUrl = "https://api-m.paypal.com"`. For the optional `PayPal:BaseUrl` override, set `BaseUrl = <config value>` — it is a free string and governs both the token exchange and all operations. (Note the property is named `Sandbox` even though it is the sole/active environment slot; the `BaseUrl` value is what actually decides the host.)
- **Idempotency header** is a per-operation `payPalRequestId` string parameter (see rows below), not a client-level setting.

⚠ **HttpClient lifetime is yours to get right** — whether the `HttpClient` you pass must be long-lived/`IHttpClientFactory`-managed vs. rebuilt per request, and how the SDK wrapper's lifetime relates. **MUST load `dotnet-client-initialization`.**

---

### 2 & 3 — Create AUTHORIZE order paid with a raw card; obtain the authorization  (source: `operations/Orders.md`; `records-1-Ac-Pa.md`; `records-2-Pa-Ve.md`; `enums.md`)

**Operation `client.Orders.CreateOrder`** — `POST /v2/checkout/orders`:
`CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- First 5 string params are nullable with **no default → must pass explicitly** (pass `null` to skip). `payPalRequestId` = the idempotency header (pass a stable GUID string). `body` is required.
- To read the created authorization inline, pass `prefer: "return=representation"` (default `"return=minimal"` returns only id/status/links).
- **Returns `Order`.** Error: `SdkException<CreateOrderError>` — **Case A**; accessors `TryGetError(out Error)` [400, 401, 422] · `TryGetRawError(out RawError)` [fallback].

**Request `OrderRequest`** (`records-1-Ac-Pa.md`): `Intent (intent): CheckoutPaymentIntent !req`, `Payer (payer): Payer?`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req`, `PaymentSource (payment_source): PaymentSource?`, `ApplicationContext (application_context): OrderApplicationContext?`.
- `Intent` = `CheckoutPaymentIntent.Authorize` (wire `AUTHORIZE`). Enum members: `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` (`enums.md`).

**`PurchaseUnitRequest`** (`records-2-Pa-Ve.md`): `ReferenceId (reference_id): string?`, `Amount (amount): AmountWithBreakdown !req`, `Payee?`, `PaymentInstruction?`, `Description?`, `CustomId (custom_id)?`, `InvoiceId (invoice_id)?`, `SoftDescriptor?`, `Items (items): IReadOnlyList<ItemRequest>?`, `Shipping (shipping): ShippingDetails?`, `SupplementaryData?`.

**`AmountWithBreakdown`** (`records-1-Ac-Pa.md`): `CurrencyCode (currency_code): string !req`, `Value (value): string !req`, `Breakdown (breakdown): AmountBreakdown?`. Value is a decimal **string** to the cent, e.g. `"49.99"`.

**Card path — `PaymentSource.Card`** (`records-2-Pa-Ve.md` `PaymentSource`): set `PaymentSource.Card = new CardRequest { … }`.
**`CardRequest`** (`records-1-Ac-Pa.md`): `Name (name): string?`, `Number (number): string?`, `Expiry (expiry): string?`, `SecurityCode (security_code): string?`, `BillingAddress (billing_address): Address?`, `Attributes (attributes): CardAttributes?`, `VaultId (vault_id): string?`, `SingleUseToken (single_use_token): string?`, `StoredCredential (stored_credential): CardStoredCredential?`, `NetworkToken?`, `ExperienceContext (experience_context): CardExperienceContext?`.
- Raw card: `Number = "4111111111111111"`, `Expiry = "YYYY-MM"` (ISO year-month, e.g. `"2027-11"`), `SecurityCode = "123"` (the CVC/CVV), `Name = "cardholder name"`.
- Card-not-present PCI SAQ D warning is on the record summary — passing raw PAN/CVV is allowed by the type but a compliance obligation.

**`Address`** (billing) (`records-1-Ac-Pa.md`): `AddressLine1 (address_line_1)?`, `AddressLine2 (address_line_2)?`, `AdminArea2 (admin_area_2)?` (city), `AdminArea1 (admin_area_1)?` (state/province), `PostalCode (postal_code)?`, `CountryCode (country_code): string !req`.

**Response `Order`** (`records-1-Ac-Pa.md`): `CreateTime?`, `UpdateTime?`, `Id (id): string?` (the order id), `PaymentSource (payment_source): PaymentSourceResponse?`, `Intent?`, `Payer?`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?`, `Status (status): OrderStatus?`, `Links (links): IReadOnlyList<LinkDescription>?`.
- `OrderStatus` members (`enums.md`): `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`.

**Reading the authorization inline** (direct-card path): the authorization is created as part of order processing and surfaces under `Order.PurchaseUnits[].Payments (PaymentCollection).Authorizations` — an `IReadOnlyList<AuthorizationWithAdditionalData>` (`PaymentCollection` in `records-2-Pa-Ve.md`).
**`AuthorizationWithAdditionalData`** (`records-1-Ac-Pa.md`): `Status (status): AuthorizationStatus?`, `Id (id): string?` (**the authorization id**), `Amount (amount): Money?`, `ExpirationTime (expiration_time): string?`, `ProcessorResponse (processor_response): ProcessorResponse?`, `SellerProtection?`, `Links?`, …
- `AuthorizationStatus` members (`enums.md`): `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)`.
- If `prefer` was `"return=minimal"`, `Payments` will be absent — re-read via `GetOrder` (§8) to obtain `authorizations[].id`.

**Alternative operation `client.Orders.AuthorizeOrder`** — `POST /v2/checkout/orders/{id}/authorize` (for the buyer-approval / redirect flow, or to authorize an already-created order):
`AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` (first 5 nullable, no default → pass explicitly).
- **Returns `OrderAuthorizeResponse`.** Error: `SdkException<AuthorizeOrderError>` — Case A; `TryGetError(out Error)` [400,401,403,404,422,500] · `TryGetRawError` [fallback].
- `OrderAuthorizeRequest` (`records-1-Ac-Pa.md`): `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` → `.Card (card): CardRequest?` (same `CardRequest` shape). `OrderAuthorizeResponse.PurchaseUnits[].Payments.Authorizations[]` gives the authorization id/status the same way.

> **Which path applies:** for a card supplied at create time (no browser approval), use **CreateOrder with `PaymentSource.Card` + `prefer: "return=representation"`** and read `Order.PurchaseUnits[].Payments.Authorizations[0].Id`. `AuthorizeOrder` is the separate operation for authorizing after buyer approval. **Whether a given live order requires a distinct `AuthorizeOrder` call after a direct-card create, or authorizes inline, is a live-wire behavior the SDK contract cannot settle — `UNVERIFIED`.** Code defensively: after CreateOrder, if `Status == OrderStatus.Completed` and an authorization is present under `payments.authorizations`, use it; else if `Status == OrderStatus.PayerActionRequired` treat as 3DS/challenge (STOP — see §11); else if no authorization is present, call `AuthorizeOrder(id, …)`.

---

### 4 — Capture an authorization at fulfilment  (source: `operations/Payments.md`; `records-1-Ac-Pa.md`; `records-2-Pa-Ve.md`)

**Operation `client.Payments.CaptureAuthorizedPayment`** — `POST /v2/payments/authorizations/{authorization_id}/capture`:
`CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` (params 2–5 `payPalMockResponse … body` nullable, no default → pass explicitly; `payPalRequestId` = idempotency header).
- **Returns `CapturedPayment`.** Error: `SdkException<CaptureAuthorizedPaymentError>` — **Case A**; `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback].

**Request `CaptureRequest`** (`records-1-Ac-Pa.md`): `Amount (amount): Money?`, `InvoiceId (invoice_id): string?`, `FinalCapture (final_capture): bool? = false`, `PaymentInstruction (payment_instruction): CapturePaymentInstruction?`, `NoteToPayer (note_to_payer): string?`, `SoftDescriptor (soft_descriptor): string?`.
- `FinalCapture = true` when this is the last/only capture for the authorization. Omit `Amount` for full capture; set `Money { CurrencyCode, Value }` for partial.

**Response `CapturedPayment`** (`records-1-Ac-Pa.md`): `Status (status): CaptureStatus?`, `Id (id): string?` (**capture id**), `Amount (amount): Money?`, `FinalCapture (final_capture): bool? = false`, `SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?`, `DisbursementMode?`, `Links?`, `ProcessorResponse?`, `CreateTime?`, `UpdateTime?`, …
- `CaptureStatus` members (`enums.md`): `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)`.

**`seller_receivable_breakdown` accessor paths** — `SellerReceivableBreakdown` (`records-2-Pa-Ve.md`): `GrossAmount (gross_amount): Money !req`, `PaypalFee (paypal_fee): Money?`, `PaypalFeeInReceivableCurrency (paypal_fee_in_receivable_currency): Money?`, `NetAmount (net_amount): Money?`, `ReceivableAmount?`, `ExchangeRate?`, `PlatformFees?`. `Money` = `CurrencyCode (currency_code): string !req`, `Value (value): string !req`.
- Gross: `capture.SellerReceivableBreakdown?.GrossAmount.Value` (+ `.CurrencyCode`).
- **Fee**: `capture.SellerReceivableBreakdown?.PaypalFee?.Value`.
- **Net**: `capture.SellerReceivableBreakdown?.NetAmount?.Value`.
- Breakdown is absent for pending captures (record summary) — null-guard.

---

### 5 — Reauthorize a stale authorization  (source: `operations/Payments.md`; `records-2-Pa-Ve.md`)

**Operation `client.Payments.ReauthorizePayment`** — `POST /v2/payments/authorizations/{authorization_id}/reauthorize`:
`ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` (`payPalRequestId`, `payPalAuthAssertion`, `body` nullable, no default → pass explicitly).
- **Returns `PaymentAuthorization`.** Error: `SdkException<ReauthorizePaymentError>` — **Case A**; `TryGetError(out Error)` [400,401,403,404,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` [fallback].
- Request `ReauthorizeRequest` (`records-2-Pa-Ve.md`): `Amount (amount): Money?` only (per notes, only `amount` is supported).
- Response `PaymentAuthorization` (`records-2-Pa-Ve.md`): `Status (status): AuthorizationStatus?`, `Id?`, `Amount?`, `ExpirationTime (expiration_time): string?`, `SupplementaryData?`, `Payee?`, `Links?`, … `ExpirationTime` marks the honor-period window.
- **"Can no longer be reauthorized" signal:** an authorization that is `AuthorizationStatus.Voided`/`Captured`/`Denied`, or past its 29-day window, will reject reauthorization. The operator-actionable detail arrives as a 422 in `SdkException<ReauthorizePaymentError>`: read `ex.Error.TryGetError(out Error e)` then `e.Details[].Issue` / `e.Message`. **The exact issue string that means "reauthorization no longer allowed" is not enumerated in the SDK (`ErrorDetails.Issue` is a free `string`) — `UNVERIFIED`.** Code defensively: surface `e.Message` + first `Details[].Issue` verbatim to the operator; do not branch on a hard-coded issue constant.

---

### 6 — Void an authorization  (source: `operations/Payments.md`)

**Operation `client.Payments.VoidPayment`** — `POST /v2/payments/authorizations/{authorization_id}/void`:
`VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` (`payPalMockResponse`, `payPalAuthAssertion`, `payPalRequestId` nullable, no default → pass explicitly).
- **Returns `PaymentAuthorization`** (status becomes `AuthorizationStatus.Voided`). Error: `SdkException<VoidPaymentError>` — **Case A**; `TryGetError(out Error)` [401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` [fallback]. Cannot void a fully-captured authorization (409/422).

---

### 7 — Refund a captured payment (full or partial)  (source: `operations/Payments.md`; `records-2-Pa-Ve.md`)

**Operation `client.Payments.RefundCapturedPayment`** — `POST /v2/payments/captures/{capture_id}/refund`:
`RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` (params 2–5 `payPalMockResponse … body` nullable, no default → pass explicitly; `payPalRequestId` = idempotency header).
- **Returns `Refund`.** Error: `SdkException<RefundCapturedPaymentError>` — **Case A**; `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` [fallback].
- Request `RefundRequest` (`records-2-Pa-Ve.md`): `Amount (amount): Money?`, `CustomId?`, `InvoiceId?`, `NoteToPayer?`, `PaymentInstruction?`. **Full refund: pass `body: null` (or a RefundRequest with `Amount = null`)**; **partial: set `Amount = new Money { CurrencyCode, Value }`.**
- Response `Refund` (`records-2-Pa-Ve.md`): `Status (status): RefundStatus?`, `Id (id): string?` (**refund id**), `Amount (amount): Money?`, `SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?`, `Links?`, `CreateTime?`, `UpdateTime?`, …
  - `RefundStatus` members (`enums.md`): `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)`.
- **How much has already been refunded:** `SellerPayableBreakdown` (`records-2-Pa-Ve.md`) has `TotalRefundedAmount (total_refunded_amount): Money?` → `refund.SellerPayableBreakdown?.TotalRefundedAmount?.Value`. (Also `GrossAmount`, `PaypalFee`, `NetAmount` for this refund.) A capture's cumulative refund state is likewise re-readable via `GetCapturedPayment` (§8) — its `Status` becomes `PartiallyRefunded`/`Refunded`.

---

### 8 — Re-read current PayPal state  (source: `operations/Orders.md`, `operations/Payments.md`)

| Operation | Signature (non-defaulted params must be passed explicitly) | Returns | Error (Case A) |
|---|---|---|---|
| `client.Orders.GetOrder` | `GetOrder(string id, string? fields, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? = null, CancellationToken ct = default)` | `Order` | `SdkException<GetOrderError>` · `TryGetError(out Error)` [401,404] · `TryGetRawError` |
| `client.Payments.GetAuthorizedPayment` | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? = null, CancellationToken ct = default)` | `PaymentAuthorization` | `SdkException<GetAuthorizedPaymentError>` · `TryGetError(out Error)` [401,403,404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` |
| `client.Payments.GetCapturedPayment` | `GetCapturedPayment(string captureId, string? payPalMockResponse, RequestOptions? = null, CancellationToken ct = default)` | `CapturedPayment` | `SdkException<GetCapturedPaymentError>` · `TryGetError(out Error)` [401,403,404] · `TryGetNoContent` [500] · `TryGetRawError` |
| `client.Payments.GetRefund` | `GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? = null, CancellationToken ct = default)` | `Refund` | `SdkException<GetRefundError>` · `TryGetError(out Error)` [401,403,404] · `TryGetNoContent` [500] · `TryGetRawError` |

- `GetOrder`'s `fields` maps to query `fields` — pass `null` unless you need a field filter. All read ops re-read the same envelope shapes documented above (`Order`, `PaymentAuthorization`, `CapturedPayment`, `Refund`).

---

### 9 — Vault / save a card and pay with it  (source: `operations/Vault.md`; `records-1-Ac-Pa.md`, `records-2-Pa-Ve.md`; `enums.md`)

**Two ways to vault a card:**

**(a) Direct — `client.Vault.CreatePaymentToken`** — `POST /v3/vault/payment-tokens`:
`CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? = null, CancellationToken ct = default)` (`payPalRequestId` nullable no default → pass explicitly; `body` required).
- **Returns `PaymentTokenResponse`.** Error: `SdkException<CreatePaymentTokenError>` — **Case A**; `TryGetError1(out Error1)` [400,403,404,422,500] · `TryGetRawError` [fallback]. (Note the accessor is `TryGetError1`, payload type `Error1`.)
- Request `PaymentTokenRequest` (`records-2-Pa-Ve.md`): `Customer (customer): Customer?`, `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req`.
  - `PaymentTokenRequestPaymentSource` (`records-2-Pa-Ve.md`): `Card (card): PaymentTokenRequestCard?`, `Token (token): VaultTokenRequest?`.
  - `PaymentTokenRequestCard` (`records-2-Pa-Ve.md`): `Name?`, `Number?`, `Expiry?`, `SecurityCode (security_code)?`, `Brand (brand): CardBrand?`, `BillingAddress (billing_address): Address?`.
  - `Customer` (`records-1-Ac-Pa.md`): `Id (id): string?`, `MerchantCustomerId (merchant_customer_id): string?`.

**(b) Two-step — `client.Vault.CreateSetupToken` then `CreatePaymentToken`:**
- `client.Vault.CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions? = null, CancellationToken ct = default)` → **`SetupTokenResponse`**; Error `SdkException<CreateSetupTokenError>` — Case A; `TryGetError1(out Error1)` [400,403,422,500] · `TryGetRawError`.
  - `SetupTokenRequest` (`records-2-Pa-Ve.md`): `Customer?`, `PaymentSource (payment_source): SetupTokenRequestPaymentSource !req` → `.Card (card): SetupTokenRequestCard?`.
  - `SetupTokenRequestCard` (`records-2-Pa-Ve.md`): `Name?`, `Number?`, `Expiry?`, `SecurityCode?`, `Brand?`, `BillingAddress?`, `VerificationMethod (verification_method): VaultCardVerificationMethod?`, `ExperienceContext (experience_context): VaultCardExperienceContext?`.
  - `SetupTokenResponse` (`records-2-Pa-Ve.md`): `Id (id): string?` (setup token id), `Status (status): PaymentTokenStatus? = Created`, `PaymentSource (payment_source): SetupTokenResponsePaymentSource?`, `Links?`.
- Then `CreatePaymentToken` with `PaymentTokenRequest.PaymentSource.Token = new VaultTokenRequest { Id = <setupTokenId>, Type = VaultTokenRequestType.SetupToken }`.
  - `VaultTokenRequest` (`records-2-Pa-Ve.md`): `Id (id): string !req`, `Type (type): VaultTokenRequestType !req`. `VaultTokenRequestType` has exactly one member: `SetupToken (SETUP_TOKEN)` (`enums.md`).

**Vault-save response `PaymentTokenResponse`** (`records-2-Pa-Ve.md`): `Id (id): string?` (**the vault / payment-token id** to reuse), `Customer (customer): CustomerResponse?`, `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?`, `Links?`.
- **Safe card description** — `PaymentTokenResponsePaymentSource.Card` is a `CardPaymentTokenEntity` (`records-1-Ac-Pa.md`): `Name?`, `LastDigits (last_digits): string?` (the "last4"), `Brand (brand): CardBrand?`, `Expiry (expiry): string?`, `BillingAddress (billing_address): CardResponseAddress?`, `Type (type): CardType?`, `VerificationStatus?`, `BinDetails?`. (There is no full `Number`/`last4` alias — the field is `LastDigits`.)
  - `CardBrand` members incl. `Visa (VISA)`, `Mastercard`, `Amex`, `Discover`, … `Unknown` (`enums.md`). `CardType`: `Credit`, `Debit`, `Prepaid`, `Store`, `Unknown`.

**Delete a vaulted token — `client.Vault.DeletePaymentToken`** — `DELETE /v3/vault/payment-tokens/{id}`:
`DeletePaymentToken(string id, RequestOptions? = null, CancellationToken ct = default)` → **`void` (Task)**; Error `SdkException<DeletePaymentTokenError>` — Case A; `TryGetError1(out Error1)` [400,403,500] · `TryGetRawError`.

**Pay an order using a vaulted card:** set `PaymentSource.Card = new CardRequest { VaultId = <PaymentTokenResponse.Id> }` on the `OrderRequest` (or on `OrderAuthorizeRequestPaymentSource.Card` / `OrderCaptureRequestPaymentSource.Card`). `CardRequest.VaultId (vault_id): string?` is the vaulted-card path.
- **GAP / caveat on `payment_source.token`:** the `Token` variant (`Token` record — `Id !req`, `Type: TokenType !req`) exists on `PaymentSource`/`OrderAuthorizeRequestPaymentSource`, **but `TokenType` has exactly one member, `BillingAgreement (BILLING_AGREEMENT)`** (`enums.md`). There is no `type=` value for a vaulted *card* on `payment_source.token`. So to pay with a vaulted **card**, use `CardRequest.VaultId`, not `PaymentSource.Token`. `PaymentSource.Token` is only for PayPal billing-agreement tokens.
- Also: `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, …)` → `CustomerVaultPaymentTokensResponse` lists a customer's saved tokens; `GetPaymentToken(string id, …)` → `PaymentTokenResponse` reads one.

---

### 10 — Transaction search / reconciliation  (source: `operations/TransactionSearch.md`; `records-2-Pa-Ve.md`)

**Operation `client.TransactionSearch.SearchTransactions`** — `GET /v1/reporting/transactions`:
`SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`.
- `startDate`/`endDate` required, ISO-8601 (wire `start_date`/`end_date`). Params `transactionId … terminalId` (8) are nullable, no default → **pass explicitly** (pass `null` to skip). Call with **named arguments** (many optional params, easy to mis-bind positionally).
- **Returns `SearchResponse`.** Error: `SdkException<RawError>` — **Case B** (the only Case-B op in the SDK). Accessors: `ex.Error.StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. There is **no** typed `TryGet…` here.

**Response `SearchResponse`** (`records-2-Pa-Ve.md`): `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?`, `AccountNumber?`, `StartDate?`, `EndDate?`, `LastRefreshedDatetime?`, `Page (page): int?`, `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`, `Links (links): IReadOnlyList<LinkDescription>?`.
- **Pagination:** page-number based. Loop `page = 1 .. SearchResponse.TotalPages` (read `TotalPages` from the first response), holding `pageSize` (default 100) constant, to walk the whole range. (Op row: "Pagination: none (only `page`, no `perPage`)" — i.e. no SDK auto-pager; you page manually.)
- **Transaction fields** — `TransactionDetails.TransactionInfo` is a `TransactionInformation` (`records-2-Pa-Ve.md`): `TransactionId (transaction_id): string?`, `TransactionAmount (transaction_amount): Money?`, `FeeAmount (fee_amount): Money?`, `TransactionStatus (transaction_status): string?`, `TransactionInitiationDate (transaction_initiation_date): string?`, `TransactionUpdatedDate (transaction_updated_date): string?`, `PaypalReferenceId?`, `InvoiceId?`, … (`transaction_status` is a free `string`, not an enum). Also `TransactionDetails.PayerInfo`, `.ShippingInfo`, `.CartInfo`, `.StoreInfo` for richer detail — but only if you widen the `fields` query beyond the default `"transaction_info"`.

---

### 11 — Error handling & the 3DS/challenge STOP condition  (source: `sdk-map.md` "Error-handling model"; `operations/*`; SDK source `Core/Exceptions/SdkException.cs`, `Core/ErrorResponse/RawError.cs`; `records-1-Ac-Pa.md`)

- **Exception type is `SdkException<TError>`, NOT `ApiException`.** (Source `Core/Exceptions/SdkException.cs`.) There is no `ApiException` in this SDK. Every operation is throw-based; **no `…Result` no-throw variants exist** anywhere. 39 of 40 ops are **Case A** (typed `SdkException<{Op}Error>`); the sole **Case B** is `SearchTransactions` (`SdkException<RawError>`).
- `SdkException<TError>` exposes exactly `.Error` (of type `TError`) and the inherited `Exception.Message`. **It has NO `StatusCode` property** (source-confirmed). To read the numeric HTTP status:
  - **Case A:** `ex.Error.TryGetRawError(out RawError raw)` → `raw.StatusCode` (`HttpStatusCode`), and `raw.ReadAsString()` for the raw body. The status-bucketed `TryGetError…`/`TryGetError1`/`TryGetNoContent`/`TryGetDefaultError` accessors give the typed payload for the statuses listed per op.
  - **Case B (`SearchTransactions`):** `ex.Error` is already `RawError` → `ex.Error.StatusCode`, `ex.Error.ReadAsString()`.
- **Error body fields (name / message / details / debug_id):** the typed payloads are records (`PayPalServerSdk.Models`):
  - `Error` (`records-1-Ac-Pa.md`): `Name (name): string !req`, `Message (message): string !req`, `DebugId (debug_id): string !req`, `Details (details): IReadOnlyList<ErrorDetails>?`, `Links?`.
  - `Error1` (Vault ops): same shape with `Details: IReadOnlyList<ErrorDetails1>?`, `Links: IReadOnlyList<ErrorLinkDescription>?`.
  - `DefaultError` (`SearchBalances`): adds `InformationLink?`.
  - `ErrorDetails` (`records-1-Ac-Pa.md`): `Field?`, `Value?`, `Location? = "body"`, `Issue (issue): string !req`, `Description?`, `Links?`. (`Issue` is a free string.)
- **Card challenge / 3DS approval requirement (STOP-and-report per task rules):** for a direct card with no browser approval, a card that demands 3DS/step-up surfaces primarily as **`Order.Status == OrderStatus.PayerActionRequired`** (wire `PAYER_ACTION_REQUIRED`) with a HATEOAS `Links` entry (rel `payer-action`) — this is the deterministic, contract-visible signal to STOP. The card's authentication outcome, when present, is readable at `Order.PaymentSource?.Card?.AuthenticationResult` (`CardResponse.AuthenticationResult: AuthenticationResponse` → `.LiabilityShift: LiabilityShiftIndicator?`, `.ThreeDSecure: ThreeDSecureAuthenticationResponse?` with `AuthenticationStatus`/`EnrollmentStatus`). **Whether a specific decline instead throws a 422 with a challenge-specific `Issue` string, and the exact string, is a live-wire fact the SDK cannot settle — `UNVERIFIED`.** Code defensively: STOP when `Status == PayerActionRequired`; on `SdkException`, extract `Error.Name` + first `Details[].Issue` best-effort and surface them, and do not hard-code a 3DS issue constant.

---

## Trap notes (load the companion skill at the step it bites — do not resolve inline)

⚠ **Step 1 (client & DI)** — the `HttpClient`/handler pipeline lifetime and how the SDK client wrapper's lifetime relates to it is not visible in the constructor signature. **MUST load `dotnet-client-initialization`** before writing `new PayPalServerSdkClient(...)` / `AddPayPalServerSdkClient`.

⚠ **Step 1 (auth)** — set credentials before constructing the client / in the DI callback, load ClientId/ClientSecret from configuration not source, and the token-caching/refresh behavior of the default OAuth2 strategy is not something to assume from the wiring. **MUST load `dotnet-authentication`.**

⚠ **Step 1 (base URL / retries / timeouts)** — the SDK's `RetryOptions.Timeout` is **not** the whole-call timeout and **not** the `HttpClient` timeout, and which verbs/triggers actually retry (a transport failure retries even non-idempotent POSTs) is not on the option names. Because captures/refunds/authorizations are non-idempotent writes, always send the `payPalRequestId` idempotency header on them. **MUST load `dotnet-configuration-resilience`** before tuning retries/timeouts/base-URL.

⚠ **Steps 2–10 (calls)** — call the search/list ops and every op with 5–8 non-defaulted nullable params using **named arguments** (`ct:` for the token) so nothing mis-binds positionally. **MUST load `dotnet-calling-endpoints`.**

⚠ **Steps 2–9 (models)** — enums are `StringEnum<T>` (build via `CheckoutPaymentIntent.Authorize` or `.FromValue("AUTHORIZE")`, never a C# enum literal); unmodeled JSON fields are dropped on deserialize; `required` members must be set in the initializer. **MUST load `dotnet-models`** before constructing request payloads.

⚠ **Step 11 (error boundary)** — Case A vs Case B, reading status safely, and why `TryGetRawError` is not a universal catch-all differ per op. **MUST load `dotnet-error-handling`** before writing any try/catch.

⚠ **Step 3 (prefer header)** — `CreateOrder`/`AuthorizeOrder` default to `prefer: "return=minimal"`, which omits `purchase_units[].payments`; pass `"return=representation"` to read the authorization inline, or re-read via `GetOrder`. (Contract fact, already resolved above — flagged here so it is not missed at the call site.)

---

## REQUIRED READING — load BEFORE implementation starts (this sheet deliberately omits their contents)

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, HttpClient lifetime, DI registration |
| `dotnet-authentication` | Step 1 — OAuth2 client-credentials wiring, secret loading, token strategy |
| `dotnet-configuration-resilience` | Step 1 — retries/timeouts/base-URL, idempotency implications for writes |
| `dotnet-calling-endpoints` | Steps 2–10 — named-argument calls, required vs optional params, async/`ct` |
| `dotnet-models` | Steps 2–9 — building requests, `StringEnum<T>`, required members, wire names |
| `dotnet-error-handling` | Step 11 — the exception boundary (mandatory for every integration) |
| `dotnet-testing` | Tests — the `HttpClient` seam, error/edge paths |

**Mandatory `JsonException` hazard rows for the error boundary (`System.Text.Json.JsonException` reaches the boundary from two directions, needing opposite handling):**

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## Assumptions & Blockers

- **Assumption:** the `PayPal:BaseUrl` override is intended to redirect the whole SDK (token + all operations); the contract supports exactly this via `options.Server.Default.Sandbox.BaseUrl` (single string, source-confirmed). No per-operation base URL is needed or exposed.
- **Assumption:** "select Sandbox vs Production" is satisfied by leaving `Environment = Sandbox` and setting the base URL — because the SDK exposes no `Production` environment member (see GAP below). If you were expecting a `ServerEnvironment.Production`, that expectation cannot be met by this SDK.
- **GAP (reported, not worked around):** `ServerEnvironment` exposes only `Sandbox`; there is no `Production` enum member. Production is reachable only by overriding `options.Server.Default.Sandbox.BaseUrl` to `https://api-m.paypal.com`. This is the documented shape at tag `v1.0.1`.
- **GAP / caveat:** paying an order with a vaulted **card** must go through `CardRequest.VaultId`; `PaymentSource.Token` (`TokenType`) supports only `BILLING_AGREEMENT`, not vaulted cards.
- **UNVERIFIED (live-wire only):** (a) whether a direct-card AUTHORIZE order authorizes inline vs. needs a separate `AuthorizeOrder` call; (b) the exact 422 `Issue` string for "reauthorization no longer allowed"; (c) the exact error name/issue for a 3DS/challenge decline. Each is handled by the defensive-coding directives in §3, §5, and §11 respectively.
- No blockers to implementation. Every operation the feature set needs is exposed by the SDK; the only absence is the Production environment enum member, worked via base-URL override above.
