# PayPal .NET SDK — Contract Sheet & Integration Plan

SDK: `AsadAli.Checkout.Sdk` (install version-less). Root namespace `PayPalServerSdk`, client
`PayPalServerSdkClient`. Map provenance: tag `v1.0.1`, source commit `9653d18`. Target host project:
eShopOnWeb `PublicApi` (ASP.NET Core .NET 8). Every row cites the map page (or, for the base-URL/env
facts the map does not fully carry, the SDK source file) it was grounded from.

---

## 1. Scope & sequence

1. **Client & DI setup** — register `PayPalServerSdkClient` (or `AddPayPalServerSdkClient`), wire the
   custom base-URL override. (§Plumbing)
2. **Auth** — set `Oauth2` client credentials from configuration. (§Plumbing)
3. **Direct card order (AUTHORIZE intent)** — `Orders.CreateOrder` (intent=AUTHORIZE, card payment
   source) → `Orders.AuthorizeOrder` → `Payments.CaptureAuthorizedPayment`. (§3, §4, §5)
4. **Direct card order (CAPTURE intent, single step)** — `Orders.CreateOrder` (intent=CAPTURE) →
   `Orders.CaptureOrder`. (§3, §5)
5. **Reauthorize / Void** — `Payments.ReauthorizePayment` / `Payments.VoidPayment`. (§6)
6. **Refund** — `Payments.RefundCapturedPayment`. (§7)
7. **Vaulting** — setup-token → payment-token, or vault-on-order; list / get / delete. (§8)
8. **Reconciliation** — `TransactionSearch.SearchTransactions` with paging. (§9)
9. **Status reads** — `GetOrder` / `GetAuthorizedPayment` / `GetCapturedPayment` / `GetRefund`. (§10)

All operations are `async`, return `Task<T>` (or `Task` for `void` ops); `await` them and pass
cancellation via the **`ct:`** named argument. All are **throw-based** (no `…Result` no-throw
variant exists anywhere in this SDK). Source: `sdk-map.md` error-handling model.

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

### 2.0 Namespaces (add a separate `using` per kind — child namespaces are NOT imported transitively)

| Type kind | Namespace | Examples |
|---|---|---|
| Client, options, `ServerOptions`, `Server` | `PayPalServerSdk` | `PayPalServerSdkClient`, `PayPalServerSdkClientOptions`, `ServerOptions` |
| Controllers | `PayPalServerSdk.Api` | `Orders`, `Payments`, `Vault`, `TransactionSearch` |
| Records (all request/response models) | `PayPalServerSdk.Models` | `OrderRequest`, `CardRequest`, `CapturedPayment`, `Money`, `Error`, `Error1`, `DefaultError` … |
| Enums | `PayPalServerSdk.Models.Enums` | `CheckoutPaymentIntent`, `OrderStatus`, `CaptureStatus`, `AuthorizationStatus`, `RefundStatus` … |
| Generated typed error classes | `PayPalServerSdk.Errors` | `CreateOrderError`, `CaptureAuthorizedPaymentError`, `CreatePaymentTokenError` … |
| `SdkException<TError>` | `PayPalServerSdk.Core.Exceptions` | (source: `Core/Exceptions/SdkException.cs`) |
| `RawError` | `PayPalServerSdk.Core.ErrorResponse` | (source: `Core/ErrorResponse/RawError.cs`) |
| `RequestOptions` | `PayPalServerSdk.Core` | per-call options arg |
| `RetryOptions` | `PayPalServerSdk.Core.Configuration` | client resilience |
| `ServerEnvironment`, `DefaultOptions` | `PayPalServerSdk.Servers` | environment + base-URL |
| `OAuth2ClientCredentials` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` | credentials |
| `IOAuth2TokenStrategy<T>` | `PayPalServerSdk.Core.Authentication.OAuth2` | optional token-strategy override |

Source for namespaces: `sdk-map.md` namespaces table + the source files named above (confirmed in clone).

---

### 2.1 Common models (used throughout)

- **`Money`** (`PayPalServerSdk.Models`): `CurrencyCode (currency_code): string !req`, `Value (value): string !req`. Every amount is this shape. Source: `records-1-Ac-Pa.md`.
- **`Address`** (billing address on cards): `AddressLine1 (address_line_1): string?`, `AddressLine2 (address_line_2): string?`, `AdminArea2 (admin_area_2): string?` (city), `AdminArea1 (admin_area_1): string?` (state), `PostalCode (postal_code): string?`, `CountryCode (country_code): string !req`. Source: `records-1-Ac-Pa.md`.
- **`AmountWithBreakdown`** (purchase-unit amount): `CurrencyCode (currency_code): string !req`, `Value (value): string !req`, `Breakdown (breakdown): AmountBreakdown?`. Source: `records-1-Ac-Pa.md`.

---

### 2.2 §Plumbing — client, auth, environment, base URL

**Client construction** (source: `sdk-map.md` "Getting a client"; `PayPalServerSdkClient.cs`,
`PayPalServerSdkClientOptions.cs`):

- Constructor: `PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)`.
- DI: `services.AddPayPalServerSdkClient(o => { … })` (source: `ServiceCollectionExtensions.cs`; it
  calls `services.AddHttpClient()` and registers the client as a **singleton** built from
  `IHttpClientFactory.CreateClient()`).
- Controller accessors are properties on the client: `client.Orders`, `client.Payments`,
  `client.Vault`, `client.TransactionSearch`.

**`PayPalServerSdkClientOptions` properties** (source: `PayPalServerSdkClientOptions.cs`):

| Property | Type | Notes |
|---|---|---|
| `Environment` | `ServerEnvironment` | default `ServerEnvironment.Default()` = `Sandbox` |
| `Retry` | `RetryOptions` (`PayPalServerSdk.Core.Configuration`) | default `RetryOptions.Default()` |
| `Logging` | `LoggingOptions` | — |
| `Server` | `ServerOptions` (`PayPalServerSdk`) | base-URL override lives here (below) |
| `Oauth2` | `OAuth2ClientCredentials?` | the credentials to set |
| `Oauth2TokenStrategy` | `IOAuth2TokenStrategy<OAuth2ClientCredentials>?` | optional; leave null for default |

**Auth** (OAuth2 client-credentials). Set `options.Oauth2 = new OAuth2ClientCredentials { … }`.
`OAuth2ClientCredentials` (namespace `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`;
source confirmed in clone) members:

| Member | Type | Required |
|---|---|---|
| `ClientId` | `string` | `required` (init) |
| `ClientSecret` | `string` | `required` (init) |
| `Scope` | `string?` | optional |

Load `ClientId`/`ClientSecret` from configuration, never hardcode. The SDK performs the OAuth token
exchange for you against `/v1/oauth2/token` (source: `AuthSchemes.cs`).

**Environment & base-URL override** — RESOLVED FROM SOURCE (the map only lists the `Sandbox` member
and points at `Servers/`; the override shape and the OAuth-URL behaviour below were confirmed in the
clone: `Servers/ServerEnvironment.cs`, `Servers/DefaultOptions.cs`, `Server.cs`, `ServerOptions.cs`,
`AuthSchemes.cs`):

- **There is exactly ONE environment: `ServerEnvironment.Sandbox`.** `ServerEnvironment.cs` declares
  only `Sandbox` (base URL `https://api-m.sandbox.paypal.com`); `Default()` returns `Sandbox`. **There
  is NO `Production`/`Live` environment member.** → **GAP for "select sandbox vs live": the SDK ships
  no first-class Live environment.** To target live PayPal you override the base URL (below) to
  `https://api-m.paypal.com`.
- **Arbitrary base-URL override IS supported** via the settable string
  `options.Server.Default.Sandbox.BaseUrl` (`ServerOptions.Default` is `DefaultOptions`;
  `DefaultOptions.Sandbox` is `DefaultOptions.SandboxOptions` with a public
  `string BaseUrl { get; set; }`). Because `Sandbox` is the only environment, `DefaultOptions.Resolve`
  always resolves the base URL through this one property, so setting it changes the base for **all**
  API calls. Wiring for the `PayPal:BaseUrl` optional override:
  ```csharp
  o.Server.Default.Sandbox.BaseUrl = config["PayPal:BaseUrl"] ?? "https://api-m.sandbox.paypal.com";
  ```
- **The OAuth token request honours the same override.** `AuthSchemes.cs` builds the token endpoint as
  `server.Default("/v1/oauth2/token")` from the *same* `Server`/`DefaultOptions.BaseUrl`. So an
  overridden `BaseUrl` is used verbatim for the OAuth token call too — satisfying the requirement that
  `PayPal:BaseUrl` be the base for ALL calls including OAuth. (Confirmed: no separate/hardcoded token
  host exists.)

**Per-call custom headers** — RESOLVED FROM SOURCE (`Core/RequestOptions.cs`): `RequestOptions` is
`{ LogLevel? LogLevel }` only. → **GAP: there is NO general per-call custom-header bag.** The only
headers you can set are the ones exposed as **dedicated named parameters** on each operation
(`payPalRequestId`, `prefer`, `payPalMockResponse`, `payPalClientMetadataId`, `payPalAuthAssertion`,
`payPalPartnerAttributionId`). Arbitrary extra headers are not supported per-call by the SDK surface.

**Idempotency (`PayPal-Request-Id`)** — pass a caller-supplied key via the `payPalRequestId` parameter.
Present on: `CreateOrder`, `AuthorizeOrder`, `CaptureOrder`, `CaptureAuthorizedPayment`,
`ReauthorizePayment`, `RefundCapturedPayment`, `VoidPayment`, `CreatePaymentToken`, `CreateSetupToken`.
NOT present on `GetOrder`, `PatchOrder`, `DeletePaymentToken`, the `Get*` reads, or `SearchTransactions`
(reads/deletes have no request-id param). Source: operation pages `Orders.md`, `Payments.md`, `Vault.md`.

**`Prefer` header** — the `prefer` parameter (default `"return=minimal"`). Pass
`prefer: "return=representation"` to force PayPal to return the full resource body on create/capture/
authorize/refund/void. Source: `Orders.md`, `Payments.md`.

---

### 2.3 §3 — Create order (direct card, AUTHORIZE or CAPTURE intent)

**Operation** (`client.Orders`, source `operations/Orders.md`):

`CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
→ returns **`Order`**.
- First 5 params are nullable-no-default → **must pass explicitly** (`null` to skip). `body` is required.
- **Error**: `SdkException<CreateOrderError>` (Case A). Accessors: `TryGetError(out Error)` [400, 401, 422] · `TryGetRawError(out RawError)` [fallback].

**Request body — `OrderRequest`** (`records-1-Ac-Pa.md`):
- `Intent (intent): CheckoutPaymentIntent !req` — `CheckoutPaymentIntent.Authorize` or `.Capture`.
- `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req`.
- `PaymentSource (payment_source): PaymentSource?` — put the raw card here for no-redirect card pay.
- `Payer (payer): Payer?`, `ApplicationContext (application_context): OrderApplicationContext?` (optional).

**`PurchaseUnitRequest`** (`records-2-Pa-Ve.md`): `Amount (amount): AmountWithBreakdown !req`,
`ReferenceId (reference_id): string?`, `CustomId (custom_id): string?`, `InvoiceId (invoice_id): string?`,
`Description (description): string?`, `Items (items): IReadOnlyList<ItemRequest>?`, plus payee/shipping/etc.

**`PaymentSource`** (`records-2-Pa-Ve.md`): for direct card set `Card (card): CardRequest?`. (Other
wallet/APM variants exist on the same record but are out of scope.)

**`CardRequest`** (`records-1-Ac-Pa.md`) — raw card details (⚠ requires PCI SAQ-D; sandbox test card
`4111111111111111`):
- `Name (name): string?`, `Number (number): string?`, `Expiry (expiry): string?` (PayPal wire format `YYYY-MM`), `SecurityCode (security_code): string?` (cvc), `BillingAddress (billing_address): Address?`.
- `VaultId (vault_id): string?` — set this to charge a previously-vaulted card without re-entering details.
- `Attributes (attributes): CardAttributes?` — vault-on-order + verification (see §8).
- `StoredCredential (stored_credential): CardStoredCredential?`, `ExperienceContext`, `SingleUseToken`, `NetworkToken`.

**Response — `Order`** (`records-1-Ac-Pa.md`): `Id (id): string?`, `Status (status): OrderStatus?`,
`Intent (intent): CheckoutPaymentIntent?`, `PaymentSource (payment_source): PaymentSourceResponse?`,
`PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?`, `Payer`, `Links`, `CreateTime`, `UpdateTime`.
⚠ With `intent=CAPTURE` + direct valid card, PayPal may complete inline; otherwise `Status` will be
`CREATED`/`APPROVED`. Read status from `Order.Status`.

---

### 2.4 §4 — Authorize the order (place the hold)

`AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
→ returns **`OrderAuthorizeResponse`**. (source `operations/Orders.md`)
- Params 2–6 nullable-no-default → **must pass explicitly**. `body` may be `null` (card already on the order) or an `OrderAuthorizeRequest`.
- **Error**: `SdkException<AuthorizeOrderError>` (Case A). `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback].

**`OrderAuthorizeRequest`** (`records-1-Ac-Pa.md`): `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?`
(→ `Card (card): CardRequest?`, `Token`, `Paypal`, wallets). Optional if the payment source was set at create.

**Response — `OrderAuthorizeResponse`** (`records-1-Ac-Pa.md`): `Id (id): string?`,
`Status (status): OrderStatus?`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?`, `Links`.
⚠ **ENVELOPE — the authorization id is nested:** `OrderAuthorizeResponse.PurchaseUnits[].Payments`
(`PaymentCollection`) `.Authorizations[]` (`AuthorizationWithAdditionalData`) `.Id`. The
authorization's status is `AuthorizationWithAdditionalData.Status (status): AuthorizationStatus?` and
its expiry is `ExpirationTime (expiration_time): string?`. Source: `PaymentCollection`,
`AuthorizationWithAdditionalData` in `records-1-Ac-Pa.md`.

---

### 2.5 §5 — Capture

**A. Capture an authorization** (authorize-then-capture model) — `client.Payments`:

`CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
→ returns **`CapturedPayment`**. (source `operations/Payments.md`)
- Params 2–5 nullable-no-default → **must pass explicitly**. `body` `null` = capture full amount.
- **Error**: `SdkException<CaptureAuthorizedPaymentError>` (Case A). `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback].

**`CaptureRequest`** (`records-1-Ac-Pa.md`): `Amount (amount): Money?` (partial capture),
`InvoiceId (invoice_id): string?`, `FinalCapture (final_capture): bool? = false`,
`NoteToPayer (note_to_payer): string?`, `SoftDescriptor (soft_descriptor): string?`,
`PaymentInstruction (payment_instruction): CapturePaymentInstruction?`.

**B. Capture an order directly** (single-step CAPTURE-intent flow) — `client.Orders`:

`CaptureOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderCaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
→ returns **`Order`** (NOT `CapturedPayment`). (source `operations/Orders.md`)
- **Error**: `SdkException<CaptureOrderError>` (Case A). `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback].
- ⚠ **ENVELOPE — capture details are nested:** `Order.PurchaseUnits[].Payments` (`PaymentCollection`)
  `.Captures[]` (`OrdersCapture`). `OrdersCapture` carries the same
  `SellerReceivableBreakdown` as `CapturedPayment` (below). Source: `PaymentCollection`, `OrdersCapture`
  in `records-1-Ac-Pa.md`.

**Capture response — `CapturedPayment`** (`records-1-Ac-Pa.md`):
- `Id (id): string?`, `Status (status): CaptureStatus?`, `Amount (amount): Money?`, `FinalCapture (final_capture): bool?`,
- `SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?` ← **the fee/net breakdown**,
- `InvoiceId`, `CustomId`, `SellerProtection`, `ProcessorResponse`, `DisbursementMode`, `Links`, `CreateTime`, `UpdateTime`.

**`SellerReceivableBreakdown`** (`records-2-Pa-Ve.md`) — **captured amount, PayPal fee, net proceeds**:
| Meaning | Property (wire) | Type |
|---|---|---|
| Gross / captured amount | `GrossAmount (gross_amount)` | `Money !req` |
| PayPal fee | `PaypalFee (paypal_fee)` | `Money?` |
| Net proceeds to merchant | `NetAmount (net_amount)` | `Money?` |
| Fee in receivable currency | `PaypalFeeInReceivableCurrency (paypal_fee_in_receivable_currency)` | `Money?` |
| Amount actually receivable | `ReceivableAmount (receivable_amount)` | `Money?` |
| FX rate (cross-currency) | `ExchangeRate (exchange_rate)` | `ExchangeRate?` |
| Platform fees | `PlatformFees (platform_fees)` | `IReadOnlyList<PlatformFee>?` |
Each `Money` → `.CurrencyCode` + `.Value` (string). ⚠ Breakdown is absent for `PENDING` captures.

---

### 2.6 §6 — Reauthorize / Void (`client.Payments`)

**Reauthorize** (refresh a stale/expired hold before capture):
`ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
→ returns **`PaymentAuthorization`**. (source `operations/Payments.md`)
- Params 2–4 nullable-no-default → **must pass explicitly**.
- `ReauthorizeRequest` (`records-2-Pa-Ve.md`): `Amount (amount): Money?` (only `amount` is supported).
- **Error**: `SdkException<ReauthorizePaymentError>` (Case A). `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback].

**Void** (release the hold / cancel-before-fulfilment):
`VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
→ returns **`PaymentAuthorization`**. (source `operations/Payments.md`)
- ⚠ **Param order differs from the other Payments ops:** here it is
  `payPalMockResponse, payPalAuthAssertion, payPalRequestId` (request-id is the **4th** param, after
  auth-assertion). Params 2–4 nullable-no-default → **must pass explicitly**. Use named args.
- **Error**: `SdkException<VoidPaymentError>` (Case A). `TryGetError(out Error)` [401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback].

**`PaymentAuthorization`** (`records-2-Pa-Ve.md`): `Id (id): string?`,
`Status (status): AuthorizationStatus?`, `Amount (amount): Money?`,
`ExpirationTime (expiration_time): string?`, `SellerProtection`, `Links`, `CreateTime`, `UpdateTime`.

---

### 2.7 §7 — Refund a captured payment (`client.Payments`)

`RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
→ returns **`Refund`**. (source `operations/Payments.md`)
- Params 2–5 nullable-no-default → **must pass explicitly**.
- **Idempotency**: pass the caller key via `payPalRequestId`.
- **Full refund**: pass `body: null` (empty payload). **Partial refund**: pass a `RefundRequest` with `Amount`.
- **Error**: `SdkException<RefundCapturedPaymentError>` (Case A). `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback].

**`RefundRequest`** (`records-2-Pa-Ve.md`): `Amount (amount): Money?` (partial amount),
`CustomId (custom_id): string?`, `InvoiceId (invoice_id): string?`,
`NoteToPayer (note_to_payer): string?`, `PaymentInstruction (payment_instruction): RefundPaymentInstruction?`.

**`Refund`** (`records-2-Pa-Ve.md`): `Id (id): string?`, `Status (status): RefundStatus?`,
`Amount (amount): Money?`, `SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?`,
`InvoiceId`, `CustomId`, `NoteToPayer`, `Links`, `CreateTime`, `UpdateTime`.

**`SellerPayableBreakdown`** (`records-2-Pa-Ve.md`) — refund cost breakdown:
`GrossAmount (gross_amount): Money?`, `PaypalFee (paypal_fee): Money?`, `NetAmount (net_amount): Money?`,
`TotalRefundedAmount (total_refunded_amount): Money?`, plus receivable-currency / platform-fee / net-breakdown variants.

---

### 2.8 §8 — Vaulting (save / reuse / list / delete cards) — `client.Vault`

**Two save paths.** (a) **Setup-token → payment-token** (recommended two-step), or
(b) **direct payment-token** from card, or (c) **vault-on-order** during `CreateOrder`.

**(a1) CreateSetupToken** — `CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` → **`SetupTokenResponse`**.
- **Error**: `SdkException<CreateSetupTokenError>` (Case A). ⚠ accessor is `TryGetError1(out Error1)` [400, 403, 422, 500] · `TryGetRawError(out RawError)` [fallback] — Vault ops use `Error1`, not `Error`.
- `SetupTokenRequest` (`records-2-Pa-Ve.md`): `Customer (customer): Customer?`, `PaymentSource (payment_source): SetupTokenRequestPaymentSource !req`.
- `SetupTokenRequestPaymentSource`: `Card (card): SetupTokenRequestCard?`, `Paypal`, `Venmo`, `ApplePay`, `Token`, `Bank`.
- `SetupTokenRequestCard`: `Name`, `Number`, `Expiry`, `SecurityCode (security_code)`, `Brand (brand): CardBrand?`, `BillingAddress (billing_address): Address?`, `VerificationMethod`, `ExperienceContext`.
- `SetupTokenResponse` (`records-2-Pa-Ve.md`): `Id (id): string?` (the setup-token id), `Status (status): PaymentTokenStatus?`, `Customer`, `PaymentSource`, `Links`.

**(a2) CreatePaymentToken** — `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` → **`PaymentTokenResponse`**.
- **Error**: `SdkException<CreatePaymentTokenError>` (Case A). `TryGetError1(out Error1)` [400, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback].
- `PaymentTokenRequest` (`records-2-Pa-Ve.md`): `Customer (customer): Customer?`, `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req`.
- `PaymentTokenRequestPaymentSource`: `Card (card): PaymentTokenRequestCard?` (direct card path), `Token (token): VaultTokenRequest?` (from-setup-token path).
- **From a setup token**: set `Token = new VaultTokenRequest { Id = <setupTokenId>, Type = VaultTokenRequestType.SetupToken }` (`VaultTokenRequest` in `records-2-Pa-Ve.md`; `VaultTokenRequestType.SetupToken` = wire `SETUP_TOKEN`, `enums.md`).
- `PaymentTokenRequestCard`: `Name`, `Number`, `Expiry`, `SecurityCode`, `Brand`, `BillingAddress`.
- `PaymentTokenResponse` (`records-2-Pa-Ve.md`): `Id (id): string?` ← **the vault/payment-token id to reuse**, `Customer (customer): CustomerResponse?`, `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?`, `Links`.

**(c) Vault-on-order during CreateOrder** — set `CardRequest.Attributes` (`CardAttributes`,
`records-1-Ac-Pa.md`): `Customer (customer): CardCustomerInformation?`,
`Vault (vault): VaultInstructionBase?`, `Verification (verification): CardVerification?`.
`VaultInstructionBase` (`records-2-Pa-Ve.md`): `StoreInVault (store_in_vault): StoreInVaultInstruction?`
= `StoreInVaultInstruction.OnSuccess` (wire `ON_SUCCESS`, `enums.md`).
⚠ **UNVERIFIED (live-wire only):** the resulting vault id on the *order/capture response* is documented
to surface at `payment_source.card.attributes.vault.id` — in the SDK that is
`CardResponse.Attributes` (`CardAttributesResponse`) `→ Vault (CardVaultResponse) → Id`
(`CardAttributesResponse`/`CardVaultResponse` in `records-1-Ac-Pa.md`). The map cannot confirm the live
payload always populates this. Directive: read it best-effort (null-check every hop
`payment_source?.card?.attributes?.vault?.id`); if absent, fall back to fetching the token via the
Vault list/get APIs rather than assuming the field is present.

**Reuse a vaulted card to pay a later order**: on the new `CreateOrder`, set
`PaymentSource.Card = new CardRequest { VaultId = <paymentTokenId> }` (no raw card details). Source:
`CardRequest.VaultId` in `records-1-Ac-Pa.md`.

**GetPaymentToken** — `GetPaymentToken(string id, RequestOptions? = null, CancellationToken ct = default)` → **`PaymentTokenResponse`**. Error `SdkException<GetPaymentTokenError>`, `TryGetError1(out Error1)` [403, 404, 422, 500] · `TryGetRawError` [fallback].

**ListCustomerPaymentTokens** — `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? = null, CancellationToken ct = default)` → **`CustomerVaultPaymentTokensResponse`**.
- Query wire: `customer_id`←`customerId`, `page_size`←`pageSize`, `page`←`page`, `total_required`←`totalRequired`.
- Error `SdkException<ListCustomerPaymentTokensError>`, `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError` [fallback].
- `CustomerVaultPaymentTokensResponse` (`records-1-Ac-Pa.md`): `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`, `Customer (customer): VaultResponseCustomer?`, `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?`, `Links`. ⚠ No `page` field is returned; only `total_items`/`total_pages` — page through by incrementing `page` until `page > total_pages`. `total_items`/`total_pages` are populated only when `totalRequired: true`.

**DeletePaymentToken** — `DeletePaymentToken(string id, RequestOptions? = null, CancellationToken ct = default)` → **`void` (Task)**. Error `SdkException<DeletePaymentTokenError>`, `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError` [fallback]. (No `payPalRequestId` param.)

(Setup-token read `GetSetupToken(string id, …)` → `SetupTokenResponse` also exists if needed.)
Source for all Vault ops: `operations/Vault.md`.

---

### 2.9 §9 — Reconciliation / transaction search (`client.TransactionSearch`)

`SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`
→ returns **`SearchResponse`**. (source `operations/TransactionSearch.md`)
- `startDate`/`endDate` are **required** ISO-8601 strings (wire `start_date`/`end_date`).
- The 8 params `transactionId … terminalId` are nullable-no-default → **must pass explicitly** (`null` to skip).
- Paging: `pageSize` (wire `page_size`, default 100), `page` (default 1).
- ⚠ **Error is Case B — `SdkException<RawError>`, NOT a typed error** (the ONLY Case-B operation in the
  SDK). No `TryGet…` typed accessors; read `ex.Error.StatusCode`, `ex.Error.ReadAsString()`,
  `ex.Error.ReadAsJson<T>()`. Your catch ladder must have a distinct `catch (SdkException<RawError>)`
  arm for this op. (The sibling `SearchBalances` is Case A `TryGetDefaultError(out DefaultError)`.)

**`SearchResponse`** (`records-2-Pa-Ve.md`) — paging + rows:
| Meaning | Property (wire) | Type |
|---|---|---|
| Current page | `Page (page)` | `int?` |
| Total items | `TotalItems (total_items)` | `int?` |
| Total pages | `TotalPages (total_pages)` | `int?` |
| Rows | `TransactionDetails (transaction_details)` | `IReadOnlyList<TransactionDetails>?` |
| Range echo | `StartDate`/`EndDate`/`LastRefreshedDatetime`/`AccountNumber` | `string?` |
Page through by incrementing `page` from 1 until `page >= TotalPages`.

**`TransactionDetails`** (`records-2-Pa-Ve.md`): `TransactionInfo (transaction_info): TransactionInformation?`
(+ `PayerInfo`, `ShippingInfo`, `CartInfo`, `StoreInfo`, …). Read the fields off `TransactionInformation`:
| Meaning | Property (wire) | Type |
|---|---|---|
| Transaction id | `TransactionId (transaction_id)` | `string?` |
| Amount | `TransactionAmount (transaction_amount)` | `Money?` |
| Status | `TransactionStatus (transaction_status)` | `string?` (raw string, not an enum here) |
| Fee | `FeeAmount (fee_amount)` | `Money?` |
| Initiated / updated | `TransactionInitiationDate` / `TransactionUpdatedDate` | `string?` |
Source: `TransactionInformation` in `records-2-Pa-Ve.md`. ⚠ Note `transaction_status` here is a plain
`string`, not the `CaptureStatus`/`AuthorizationStatus` enum.

---

### 2.10 §10 — Status reads by PayPal id

| Read | Signature (params in order) | Returns | Status field | Error accessors |
|---|---|---|---|---|
| Order | `GetOrder(string id, string? fields, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? = null, CancellationToken ct = default)` | `Order` | `Order.Status: OrderStatus?` | `SdkException<GetOrderError>`; `TryGetError(out Error)` [401,404] · `TryGetRawError` |
| Authorization | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? = null, CancellationToken ct = default)` | `PaymentAuthorization` | `.Status: AuthorizationStatus?` | `SdkException<GetAuthorizedPaymentError>`; `TryGetError(out Error)` [401,403,404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` |
| Capture | `GetCapturedPayment(string captureId, string? payPalMockResponse, RequestOptions? = null, CancellationToken ct = default)` | `CapturedPayment` | `.Status: CaptureStatus?` | `SdkException<GetCapturedPaymentError>`; `TryGetError(out Error)` [401,403,404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` |
| Refund | `GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? = null, CancellationToken ct = default)` | `Refund` | `.Status: RefundStatus?` | `SdkException<GetRefundError>`; `TryGetError(out Error)` [401,403,404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` |

Every listed nullable-no-default param (`fields`, `payPalMockResponse`, `payPalAuthAssertion`) must be
passed explicitly (`null` to skip). Source: `operations/Orders.md`, `operations/Payments.md`.

---

### 2.11 Enum tables (exact type names + members; namespace `PayPalServerSdk.Models.Enums`)

Enums are `StringEnum<T>` (NOT C# enums). Build via the static member (`CheckoutPaymentIntent.Authorize`)
or `Type.FromValue("AUTHORIZE")`. Members below are `CSharpMember (WIRE_VALUE)`. Source: `enums.md`.

- **`CheckoutPaymentIntent`** (order intent): `Capture (CAPTURE)`, `Authorize (AUTHORIZE)`.
- **`OrderStatus`**: `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`.
- **`AuthorizationStatus`**: `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)`.
- **`CaptureStatus`**: `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)`.
- **`RefundStatus`**: `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)`.
- **`CardBrand`** (response): `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Amex (AMEX)`, `Discover (DISCOVER)`, … (30 members; see `enums.md` if a specific brand is needed).
- **`CardType`**: `Credit (CREDIT)`, `Debit (DEBIT)`, `Prepaid (PREPAID)`, `Store (STORE)`, `Unknown (UNKNOWN)`.
- **`StoreInVaultInstruction`**: `OnSuccess (ON_SUCCESS)`.
- **`VaultTokenRequestType`**: `SetupToken (SETUP_TOKEN)`.
- **`PaymentTokenStatus`**: `Created (CREATED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`, `Approved (APPROVED)`, `Vaulted (VAULTED)`, `Tokenized (TOKENIZED)`.
- **`VaultStatus`**: `Vaulted (VAULTED)`, `Created (CREATED)`, `Approved (APPROVED)`.
- **`DisbursementMode`**: `Instant (INSTANT)`, `Delayed (DELAYED)`.
- **`SellerProtectionStatus`**: `Eligible (ELIGIBLE)`, `PartiallyEligible (PARTIALLY_ELIGIBLE)`, `NotEligible (NOT_ELIGIBLE)`.

### 2.12 Error payload shapes (the `out` types from the accessors above; namespace `PayPalServerSdk.Models`)

All three carry `name`/`message`/`debug_id` (`DebugId`) and a details list — this is where you read
`name` / `message` / `details` / `debug_id`. HTTP status is NOT on these payloads (see trap on reading
status). Source: `records-1-Ac-Pa.md`.
- **`Error`** (Orders/Payments/Get* typed errors): `Name (name): string !req`, `Message (message): string !req`, `DebugId (debug_id): string !req`, `Details (details): IReadOnlyList<ErrorDetails>?`, `Links`.
- **`Error1`** (Vault typed errors): `Name`, `Message`, `DebugId (debug_id): string !req`, `Details (details): IReadOnlyList<ErrorDetails1>?`, `Links (links): IReadOnlyList<ErrorLinkDescription>?`.
- **`DefaultError`** (`SearchBalances`): `Name`, `Message`, `DebugId`, `InformationLink (information_link): string?`, `Details (details): IReadOnlyList<TransactionSearchErrorDetails>?`, `Links`.
- **`ErrorDetails`**: `Field (field): string?`, `Value (value): string?`, `Location (location): string? = "body"`, `Issue (issue): string !req`, `Description (description): string?`.
- **`SearchTransactions`** carries NO typed payload — it is `RawError` (`StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`; namespace `PayPalServerSdk.Core.ErrorResponse`).

---

## 3. Trap notes (load the named skill before the step — do not treat these as resolved)

> ⚠ Step 1 (client & DI) — the `HttpClient`/handler pipeline must be long-lived and shared, and how
> the SDK client wraps it matters for lifetime. **MUST load `dotnet-client-initialization`** before
> writing `new PayPalServerSdkClient(...)` / `AddPayPalServerSdkClient`.

> ⚠ Step 1/2 (base-URL override & resilience) — whether the base URL, retries and `Timeout` behave the
> way the option names suggest is not visible from the signatures; in particular what `Timeout` bounds
> and which verbs actually retry (a **transport failure** can re-send a non-idempotent `POST` even when
> `HttpMethodsToRetry` excludes it — which is exactly why the `payPalRequestId` idempotency keys in this
> sheet matter). **MUST load `dotnet-configuration-resilience`** before tuning the client.

> ⚠ Step 2 (auth) — set `Oauth2` credentials before the client is constructed / in the DI callback, and
> load secrets from configuration; token acquisition/refresh timing is not shown by the property.
> **MUST load `dotnet-authentication`** before wiring credentials.

> ⚠ Step 3+ (calling ops) — many params here are nullable-with-no-default and mis-bind in a positional
> call; call every op with **named arguments** (and `ct:` for cancellation). **MUST load
> `dotnet-calling-endpoints`** before the first call.

> ⚠ Step 3+ (models) — enums are `StringEnum<T>` (not C# enums), amounts/cards are nested records, and
> **unmodeled JSON fields are dropped on deserialize** — which bears directly on the UNVERIFIED
> vault-id-on-response row in §8. **MUST load `dotnet-models`** before building request payloads or
> reading responses.

> ⚠ Step 3+ (error boundary) — which exception actually reaches your catch, and **how to read the HTTP
> status** off it, is not obvious: `SdkException<TError>` exposes only `.Error` (no `.StatusCode` on the
> exception). Numeric status comes from `RawError.StatusCode` — directly for the Case-B
> `SearchTransactions`, and via the `TryGetRawError(out RawError)` fallback for the Case-A ops — but the
> exact conditions under which each `TryGet…` returns true are the skill's domain. Also Vault ops use
> `TryGetError1(out Error1)` while Orders/Payments use `TryGetError(out Error)`. **MUST load
> `dotnet-error-handling`** before writing any try/catch.

> ⚠ Testing — the `HttpClient` constructor argument is the test seam. **MUST load `dotnet-testing`**
> before stubbing the SDK.

---

## 4. REQUIRED READING (load BEFORE implementation; this sheet deliberately omits their contents)

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, HttpClient lifetime, DI registration |
| `dotnet-authentication` | Step 2 — setting `Oauth2` credentials, token lifecycle |
| `dotnet-configuration-resilience` | Step 1/2 — base-URL override, retries, timeouts, pagination |
| `dotnet-calling-endpoints` | Step 3+ — named-argument calls, async/cancellation, envelope shapes |
| `dotnet-models` | Step 3+ — building request records, enums, nullability, wire names |
| `dotnet-error-handling` | error boundary — which exception reaches catch, reading status/body safely |
| `dotnet-testing` | tests — faking the `HttpClient` seam |

An integration always writes an error boundary, so **`dotnet-error-handling` is mandatory reading**.
Two `System.Text.Json.JsonException` hazards reach that boundary from opposite directions and need
opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member — e.g. `Money.CurrencyCode`,
  `SellerReceivableBreakdown.GrossAmount`, `Error.DebugId`) surfaces as a `JsonException` from
  **deserialization**, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it
  escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the
  `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException`
  to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries
  something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

- **Assumption:** this is a plan/contract request (it drives implementation); no plan-file path was
  dictated, so the sheet was written to the default `<project repo root>/paypal-plan.md`.
- **Assumption:** "sandbox vs live" is served by the base-URL override, since the SDK exposes no Live
  environment (see the GAP below); live = override `BaseUrl` to `https://api-m.paypal.com`.
- **GAP (reportable):** no first-class `Production`/`Live` `ServerEnvironment` — only `Sandbox` exists.
- **GAP (reportable):** no general per-call custom-header mechanism — `RequestOptions` carries only
  `LogLevel`; only the dedicated header params (`payPalRequestId`, `prefer`, `payPalMockResponse`,
  `payPalClientMetadataId`, `payPalAuthAssertion`, `payPalPartnerAttributionId`) are settable per-call.
- **UNVERIFIED (live-wire only):** the vault-id location on an order/capture response after
  vault-on-order (`payment_source.card.attributes.vault.id`) — read best-effort with null-checks and
  fall back to the Vault list/get APIs; do not assume the field is populated (see §8).
- **No blockers** to planning. Card-in-the-clear paths (`CardRequest`, `PaymentTokenRequestCard`,
  `SetupTokenRequestCard`) require the host to hold PCI SAQ-D scope — a compliance prerequisite, not an
  SDK blocker.
