# PayPal .NET SDK — integration contract sheet (eShopOnWeb `src/PublicApi`)

SDK: `AsadAli.Checkout.Sdk` (install version-less) · root namespace `PayPalServerSdk` · client `PayPalServerSdkClient`.
Grounded against the bundled SDK map (tag `v1.0.1`, source stamp `9653d18`). Rows cite their map page; the
handful of facts that only the SDK source settles are marked **[source]** with the file that settled them.

---

## 1. Scope & sequence

1. **Client + DI + auth + base-URL** — register `PayPalServerSdkClient` with OAuth2 client-credentials and the
   configurable base URL. (uses `AddPayPalServerSdkClient`, `PayPalServerSdkClientOptions`)
2. **Create order (intent=AUTHORIZE, direct card)** — `Orders.CreateOrder`; detect 3DS/redirect and STOP.
3. **Authorize the created order** — `Orders.AuthorizeOrder`; read authorization id + status.
4. **Capture the authorization (fulfilment)** — `Payments.CaptureAuthorizedPayment`; read amount, fee, net.
5. **Re-authorize a stale authorization** — `Payments.ReauthorizePayment`.
6. **Void an authorization** — `Payments.VoidPayment`.
7. **Refund a capture (full/partial, idempotent)** — `Payments.RefundCapturedPayment`.
8. **Vault a card + reuse + delete** — `Vault.CreateSetupToken` → `Vault.CreatePaymentToken` → reuse via
   `CardRequest.VaultId` → `Vault.DeletePaymentToken`.
9. **Idempotency for order-create/authorize/capture** — `payPalRequestId` param on each.
10. **Reconciliation search** — `TransactionSearch.SearchTransactions` with manual paging.

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

### Namespaces used below (put a `using` for each kind you touch)
| Type kind | Namespace |
|---|---|
| Client, options, `Server`, `ServerOptions` | `PayPalServerSdk` |
| Controllers (`Orders`, `Payments`, `Vault`, `TransactionSearch`) | `PayPalServerSdk.Api` |
| Records (all request/response models below) | `PayPalServerSdk.Models` |
| Enums (`CheckoutPaymentIntent`, `OrderStatus`, …) | `PayPalServerSdk.Models.Enums` |
| Per-operation typed errors (`AuthorizeOrderError`, `CreatePaymentTokenError`, …) | `PayPalServerSdk.Errors` |
| `OAuth2ClientCredentials` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` |
| `ServerEnvironment`, `DefaultOptions` | `PayPalServerSdk.Servers` |
| `SdkException<T>` | `PayPalServerSdk.Core.Exceptions` (`Core/Exceptions/SdkException.cs`) |
| `RawError` | `PayPalServerSdk.Core.ErrorResponse` (`Core/ErrorResponse/RawError.cs`) |
| `RetryOptions` | `PayPalServerSdk.Core.Configuration` (`Core/Configuration/RetryOptions.cs`) |

---

### Step 1 — Client construction, auth, environment, base-URL  (sdk-map.md · **[source]** where noted)

**Construction.** `new PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)`
or DI: `services.AddPayPalServerSdkClient(o => { ... })`. Controllers are properties: `client.Orders`,
`client.Payments`, `client.Vault`, `client.TransactionSearch`.

**`PayPalServerSdkClientOptions` properties** (source `PayPalServerSdkClientOptions.cs`):
`Environment: ServerEnvironment` · `Retry: RetryOptions` · `Logging: LoggingOptions` · `Server: ServerOptions`
· `Oauth2: OAuth2ClientCredentials?` · `Oauth2TokenStrategy: IOAuth2TokenStrategy<OAuth2ClientCredentials>?`.

**Auth (OAuth2 client-credentials).** Set `options.Oauth2 = new OAuth2ClientCredentials { ClientId = <cfg>,
ClientSecret = <cfg> }`. **[source: `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs`]** —
the type is `sealed`, members: `ClientId (required string)`, `ClientSecret (required string)`, `Scope (string?)`.
Both `ClientId`/`ClientSecret` are C# `required` → must be set in the initializer.

**Token handling (automatic).** You do NOT fetch a token yourself. **[source:
`Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentialsStrategy.cs` + `AuthSchemes.cs`]** — when
`Oauth2` is set and `Oauth2TokenStrategy` is left null, the SDK POSTs `grant_type=client_credentials` to
`/v1/oauth2/token` with an HTTP Basic header (`Base64(ClientId:ClientSecret)`) and reads back the access token,
attaching it to every call. The token endpoint is resolved through the **same** server/base-URL resolver as the
API calls (`server.Default("/v1/oauth2/token")`) — so a base-URL override (below) automatically applies to the
token request too. (Caching/refresh lifecycle of that token is the companion-skill's concern — see trap.)

**Environment enum values.** `ServerEnvironment` (`PayPalServerSdk.Servers`) exposes **exactly one** member:
`ServerEnvironment.Sandbox`. **[source: `Servers/ServerEnvironment.cs`]** — there is **no `Production`/`Live`
member**; `ServerEnvironment.Default()` returns `Sandbox`. Selecting "live" is therefore done **only** by
overriding the base URL (next), not by an enum. See **Gaps**.

**Base-URL override (verbatim, drives every call incl. token).** **[source: `ServerOptions.cs`,
`Servers/DefaultOptions.cs`, `Server.cs`, `AuthSchemes.cs`]** — the effective host is
`options.Server.Default.Sandbox.BaseUrl` (default `https://api-m.sandbox.paypal.com`). Set it from
`PayPal:BaseUrl` when configured:
```
options.Server = new ServerOptions {                 // PayPalServerSdk
    Default = new DefaultOptions {                    // PayPalServerSdk.Servers
        Sandbox = new DefaultOptions.SandboxOptions { BaseUrl = cfgBaseUrl }   // nested class
    }
};
```
Because both the API paths and the `/v1/oauth2/token` request go through `DefaultOptions.Resolve(...)`, this one
override is used for EVERY call including the token request (requirement satisfied). For live without a
`PayPal:BaseUrl`, set this same `BaseUrl` to `https://api-m.paypal.com`.

---

### Step 2 — CreateOrder (intent=AUTHORIZE, raw card)  (operations/Orders.md · records-1/2)

- **Call**: `client.Orders.CreateOrder(string? payPalMockResponse, string? payPalRequestId,
  string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion,
  OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null,
  CancellationToken ct = default)`. The first 5 string params are nullable-no-default → **pass `null`
  explicitly**. Pass `payPalRequestId:` for idempotency (Step 9). Consider `prefer: "return=representation"`
  so the response body is fully populated (default is `return=minimal`).
- **Request `OrderRequest`** (`Models/OrderRequest.cs`): `Intent (intent): CheckoutPaymentIntent !req` →
  `CheckoutPaymentIntent.Authorize`; `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req`;
  `PaymentSource (payment_source): PaymentSource?`; `Payer?`; `ApplicationContext?`.
  - `PurchaseUnitRequest` (`Models/PurchaseUnitRequest.cs`): `Amount (amount): AmountWithBreakdown !req`
    (+ `ReferenceId?`, `CustomId?`, `InvoiceId?`, `Description?`, `Items?`, `Shipping?`).
  - `AmountWithBreakdown` (`Models/AmountWithBreakdown.cs`): `CurrencyCode (currency_code): string !req`
    (currency from config), `Value (value): string !req` (order total as a decimal STRING to the cent,
    e.g. `"49.99"`), `Breakdown (breakdown): AmountBreakdown?`.
  - `PaymentSource` (`Models/PaymentSource.cs`): set `Card (card): CardRequest?` for a direct card.
  - `CardRequest` (`Models/CardRequest.cs`): `Number (number): string?` = `"4111111111111111"`,
    `Expiry (expiry): string?` (`"YYYY-MM"`), `SecurityCode (security_code): string?` (CVC),
    `Name (name): string?`, `BillingAddress (billing_address): Address?`, plus `VaultId (vault_id): string?`
    (used for saved-card reuse, Step 8), `Attributes?`, `ExperienceContext?`, `StoredCredential?`.
  - `Address` (`Models/Address.cs`): `CountryCode (country_code): string !req` (only required field);
    `AddressLine1?`, `AddressLine2?`, `AdminArea1?` (state), `AdminArea2?` (city), `PostalCode?`.
  - Object-initializer construction only (records, `init`-only); no constructors take these fields.
- **Response `Order`** (`Models/Order.cs`): `Id (id): string?`, `Status (status): OrderStatus?`,
  `PaymentSource (payment_source): PaymentSourceResponse?`, `PurchaseUnits?`, `Links (links):
  IReadOnlyList<LinkDescription>?`. Keep `Order.Id` for Step 3.
- **3DS / challenge detection (must STOP if present).** Read `order.Status`. If
  `order.Status == OrderStatus.PayerActionRequired` the buyer must approve in a browser → **STOP, do not
  authorize**. The redirect target, if any, is a `LinkDescription` in `order.Links` (`Rel`/`Href`). A clean
  direct-card order comes back `OrderStatus.Created` (then Step 3) or already `Completed`/`Approved`.
  Also available: `order.PaymentSource.Card.AuthenticationResult.ThreeDSecure` (enrollment/auth status).
  **UNVERIFIED** — the exact `Rel` string of the approval link (e.g. `"payer-action"`) is a live-wire value not
  pinned by the generated model. Defensive directive: treat **any** `PayerActionRequired` status as
  "requires browser approval → STOP", and when scanning `Links`, match the approval link best-effort
  (case-insensitive `rel` containing `payer-action`/`approve`) rather than an exact literal.
- **Error**: `SdkException<CreateOrderError>` — Case A. Accessors: `TryGetError(out Error)` [400,401,422] ·
  `TryGetRawError(out RawError)` [fallback]. (`Error` = `Models/Error.cs`.)

---

### Step 3 — AuthorizeOrder (place the hold)  (operations/Orders.md · records-1)

Clarification: with `intent=AUTHORIZE`, CreateOrder does **not** itself place the hold. You create with
intent=AUTHORIZE, then call AuthorizeOrder on the returned order id.

- **Call**: `client.Orders.AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId,
  string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body,
  string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`.
  For an order already carrying the card from Step 2, pass `body: null` and the 4 middle strings `null`
  (except `payPalRequestId:` for idempotency).
- **Request (optional) `OrderAuthorizeRequest`** (`Models/OrderAuthorizeRequest.cs`): single field
  `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` (only needed if supplying the payment
  source now instead of at create).
- **Response `OrderAuthorizeResponse`** (`Models/OrderAuthorizeResponse.cs`): `Id?`, `Status (status):
  OrderStatus?`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?`.
  **Authorization id + status accessor path**:
  `response.PurchaseUnits[0].Payments.Authorizations[0].Id` and `.Status` (an
  `AuthorizationWithAdditionalData`, `Status` is `AuthorizationStatus`). `PurchaseUnit.Payments` is a
  `PaymentCollection` (`Authorizations: IReadOnlyList<AuthorizationWithAdditionalData>?`). Null-guard the list.
- **Error**: `SdkException<AuthorizeOrderError>` — Case A. `TryGetError(out Error)` [400,401,403,404,422,500] ·
  `TryGetRawError(out RawError)`.

---

### Step 4 — CaptureAuthorizedPayment (at fulfilment)  (operations/Payments.md · records-1/2)

- **Call**: `client.Payments.CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse,
  string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal",
  RequestOptions? requestOptions = null, CancellationToken ct = default)`. Pass `payPalRequestId:` for
  idempotency. `authorizationId` = the id from Step 3.
- **Request (optional) `CaptureRequest`** (`Models/CaptureRequest.cs`): `Amount (amount): Money?` (omit for full
  capture of the authorized amount), `FinalCapture (final_capture): bool? = false`, `InvoiceId?`,
  `NoteToPayer?`, `SoftDescriptor?`, `PaymentInstruction?`.
- **Response `CapturedPayment`** (`Models/CapturedPayment.cs`): `Id (id): string?`,
  `Status (status): CaptureStatus?`, `Amount (amount): Money?`,
  `SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?`.
  **Accessor paths for reconciliation:**
  - captured amount → `captured.Amount.Value` + `captured.Amount.CurrencyCode` (`Money`).
  - gross → `captured.SellerReceivableBreakdown.GrossAmount.Value` (`GrossAmount` is `Money !req` on the
    breakdown, but `SellerReceivableBreakdown` itself is nullable → null-guard the breakdown).
  - PayPal fee → `captured.SellerReceivableBreakdown.PaypalFee?.Value` (`PaypalFee` is `Money?`).
  - net to merchant → `captured.SellerReceivableBreakdown.NetAmount?.Value` (`NetAmount` is `Money?`).
  (`SellerReceivableBreakdown` = `Models/SellerReceivableBreakdown.cs`; not populated while a capture is
  `PENDING`.)
- **Error**: `SdkException<CaptureAuthorizedPaymentError>` — Case A. Accessors: `TryGetError(out Error)`
  [400,401,403,404,409,422] · **`TryGetNoContent(out RawError)` [500]** · `TryGetRawError(out RawError)`.

---

### Step 5 — ReauthorizePayment (stale/expired hold)  (operations/Payments.md · records-2)

- **Call**: `client.Payments.ReauthorizePayment(string authorizationId, string? payPalRequestId,
  string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal",
  RequestOptions? requestOptions = null, CancellationToken ct = default)`. The 3 params
  `payPalRequestId`/`payPalAuthAssertion`/`body` are nullable-no-default → pass explicitly.
- **Request `ReauthorizeRequest`** (`Models/ReauthorizeRequest.cs`): single field `Amount (amount): Money?`
  (only `amount` is supported by this endpoint).
- **Response `PaymentAuthorization`** (`Models/PaymentAuthorization.cs`): `Id?`, `Status (status):
  AuthorizationStatus?`, `Amount?`, `ExpirationTime (expiration_time): string?`.
- **Error**: `SdkException<ReauthorizePaymentError>` — Case A. `TryGetError(out Error)` [400,401,403,404,422] ·
  `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`.
- **"Can no longer be re-authorized" → actionable operator message.** The generated contract surfaces this as a
  `TryGetError(out Error)` (typically 422/400) whose `Error` (`Models/Error.cs`) carries `Name`, `Message`,
  and `Details (details): IReadOnlyList<ErrorDetails>?` where each `ErrorDetails` has `Issue (issue): string`
  and `Description?`. **UNVERIFIED** — the exact `Issue` code for an unreauthorizable hold (e.g. an
  `AUTH_...`/expiry issue string) is a live-wire value, not pinned in the map. Defensive directive: on the
  typed error, surface `Error.Name` + the joined `Details[].Issue`/`Description` (fall back to `Error.Message`)
  verbatim to the operator; do not branch on a hard-coded issue literal.

---

### Step 6 — VoidPayment (cancel before capture)  (operations/Payments.md · records-2)

- **Call**: `client.Payments.VoidPayment(string authorizationId, string? payPalMockResponse,
  string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal",
  RequestOptions? requestOptions = null, CancellationToken ct = default)`. The 3 middle strings are
  nullable-no-default → pass explicitly (`payPalRequestId:` optional idempotency).
- **Response `PaymentAuthorization`** (as Step 5). Cannot void an authorization already fully captured.
- **Error**: `SdkException<VoidPaymentError>` — Case A. `TryGetError(out Error)` [401,403,404,409,422] ·
  `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`.

---

### Step 7 — RefundCapturedPayment (full/partial, idempotent)  (operations/Payments.md · records-2)

- **Call**: `client.Payments.RefundCapturedPayment(string captureId, string? payPalMockResponse,
  string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal",
  RequestOptions? requestOptions = null, CancellationToken ct = default)`. `captureId` = `CapturedPayment.Id`
  from Step 4.
- **Idempotency key**: pass `payPalRequestId:` (the `PayPal-Request-Id` header). Reusing the same value makes a
  repeated identical refund request a no-op instead of a double refund.
- **Request `RefundRequest`** (`Models/RefundRequest.cs`): **full refund** → pass `body: null` (or an empty
  `RefundRequest`); **partial refund** → set `Amount (amount): Money?` = `new Money { CurrencyCode = <cfg>,
  Value = "<partial>" }`. Also `InvoiceId?`, `CustomId?`, `NoteToPayer?`.
- **Response `Refund`** (`Models/Refund.cs`): `Id (id): string?`, `Status (status): RefundStatus?`,
  `Amount (amount): Money?`, `SellerPayableBreakdown?`.
- **Error**: `SdkException<RefundCapturedPaymentError>` — Case A. `TryGetError(out Error)`
  [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)`.

---

### Step 8 — Vault a card, reuse it, delete it  (operations/Vault.md · records-1/2)

**8a — Preferred two-step (setup token → payment token).**
- `client.Vault.CreateSetupToken(string? payPalRequestId, SetupTokenRequest body,
  RequestOptions? requestOptions = null, CancellationToken ct = default)`.
  - `SetupTokenRequest` (`Models/SetupTokenRequest.cs`): `Customer (customer): Customer?`,
    `PaymentSource (payment_source): SetupTokenRequestPaymentSource !req`.
  - `SetupTokenRequestPaymentSource` (`Models/SetupTokenRequestPaymentSource.cs`):
    `Card (card): SetupTokenRequestCard?` (also `Paypal?`, `Venmo?`, `Token?`, `Bank?`, `ApplePay?`).
  - `SetupTokenRequestCard` (`Models/SetupTokenRequestCard.cs`): `Number?`, `Expiry?`, `SecurityCode?`, `Name?`,
    `Brand (brand): CardBrand?`, `BillingAddress (billing_address): Address?`, `VerificationMethod?`,
    `ExperienceContext?`.
  - Response `SetupTokenResponse` (`Models/SetupTokenResponse.cs`): `Id (id): string?` (the setup token id),
    `Status (status): PaymentTokenStatus?`, `PaymentSource?`.
- Then `client.Vault.CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body,
  RequestOptions? requestOptions = null, CancellationToken ct = default)` referencing that setup token:
  - `PaymentTokenRequest` (`Models/PaymentTokenRequest.cs`): `Customer?`,
    `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req`.
  - `PaymentTokenRequestPaymentSource` (`Models/PaymentTokenRequestPaymentSource.cs`):
    `Token (token): VaultTokenRequest?` — set this to reference the setup token; or `Card
    (card): PaymentTokenRequestCard?` to vault a raw card directly (one-step, 8b).
  - `VaultTokenRequest` (`Models/VaultTokenRequest.cs`): `Id (id): string !req` = the setup token id,
    `Type (type): VaultTokenRequestType !req` = `VaultTokenRequestType.SetupToken` (only member).

**8b — One-step (vault a raw card directly).** Call `CreatePaymentToken` with
`PaymentTokenRequestPaymentSource.Card = new PaymentTokenRequestCard { Number = "...", Expiry = "...",
SecurityCode = "...", Name = "...", Brand = ..., BillingAddress = ... }` (`Models/PaymentTokenRequestCard.cs`).

**Response of `CreatePaymentToken` = `PaymentTokenResponse`** (`Models/PaymentTokenResponse.cs`):
- `Id (id): string?` → **the vault / payment-token id** to store and reuse.
- `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?` →
  `.Card (card): CardPaymentTokenEntity?` gives the **safe** card descriptor:
  `LastDigits (last_digits): string?`, `Brand (brand): CardBrand?`, `Expiry (expiry): string?`
  (`Models/CardPaymentTokenEntity.cs`). Persist only these, never the PAN/CVC.

**Reuse the saved vault id to pay a later order.** In the later `Orders.CreateOrder`, set
`OrderRequest.PaymentSource.Card = new CardRequest { VaultId = <saved PaymentTokenResponse.Id> }`
(`CardRequest.VaultId (vault_id): string?`). No PAN/CVC needed on the reuse call.

**Delete a vaulted token.** `client.Vault.DeletePaymentToken(string id, RequestOptions? requestOptions = null,
CancellationToken ct = default)` → returns `void` (Task); `id` = the payment-token id.

**Vault error case (note the different accessor names/payload).** All Vault ops are Case A but use
**`TryGetError1(out Error1)`** (not `TryGetError`) + `TryGetRawError(out RawError)`. Payload `Error1`
(`Models/Error1.cs`) — `Name`, `Message`, `DebugId`, `Details: IReadOnlyList<ErrorDetails1>?`.
Status maps: CreatePaymentToken [400,403,404,422,500] · CreateSetupToken [400,403,422,500] ·
DeletePaymentToken [400,403,500].

---

### Step 9 — Idempotency for order-create / authorize / capture  (operations/Orders.md · Payments.md)

Pass the same generated key as **`payPalRequestId:`** (the `PayPal-Request-Id` header) on:
`Orders.CreateOrder(payPalRequestId:)`, `Orders.AuthorizeOrder(payPalRequestId:)`,
`Payments.CaptureAuthorizedPayment(payPalRequestId:)` (and `RefundCapturedPayment`, `ReauthorizePayment`,
`VoidPayment`, `Vault.Create*` all take one). A repeated identical request with the same key does not
create a second charge. Persist the key alongside the eShop order so a retry/double-click reuses it.

---

### Step 10 — TransactionSearch for reconciliation (paged)  (operations/TransactionSearch.md · records-2)

- **Call**: `client.TransactionSearch.SearchTransactions(string startDate, string endDate,
  string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount,
  string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId,
  string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100,
  int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`. The 8 filter strings
  (`transactionId`…`terminalId`) are nullable-no-default → **pass `null` explicitly** (or use named args and
  skip). `startDate`/`endDate` are required ISO-8601 strings (e.g. `"2026-08-01T00:00:00-0000"`).
- **Response `SearchResponse`** (`Models/SearchResponse.cs`): `TransactionDetails (transaction_details):
  IReadOnlyList<TransactionDetails>?`, `Page (page): int?`, `TotalItems (total_items): int?`,
  `TotalPages (total_pages): int?`, `Links?`.
- **Paging (must page the WHOLE range).** There is no auto-pager. Loop: start `page = 1`, keep the returned
  `SearchResponse.TotalPages`, and call again incrementing `page` until `page > TotalPages` (or until
  `TransactionDetails` is empty). Use `pageSize` (max per PayPal is 500; default 100) as `page_size`.
- **Per-transaction fields** (to line up against eShop orders): each `TransactionDetails.TransactionInfo`
  (`TransactionInformation`, `Models/TransactionInformation.cs`): `TransactionId (transaction_id): string?`,
  `TransactionStatus (transaction_status): string?`, `TransactionAmount (transaction_amount): Money?`,
  `TransactionInitiationDate (transaction_initiation_date): string?`,
  `TransactionUpdatedDate (transaction_updated_date): string?`, `InvoiceId (invoice_id): string?`
  (useful to carry your eShop order number). Null-guard `TransactionInfo`.
- **Error — this is the SDK's ONLY Case B op**: `SdkException<RawError>`. No typed accessors; read
  `ex.Error.StatusCode` and `ex.Error.ReadAsString()` / `ex.Error.ReadAsJson<T>()`. Do **not** write a
  `TryGetError(...)` ladder here — it does not exist for this operation.

---

### Enum value tables (only those in scope)  (models/enums.md)

| Enum (`PayPalServerSdk.Models.Enums`) | Members (C# → wire) |
|---|---|
| `CheckoutPaymentIntent` | `Capture` (CAPTURE), `Authorize` (AUTHORIZE) |
| `OrderStatus` | `Created` (CREATED), `Saved` (SAVED), `Approved` (APPROVED), `Voided` (VOIDED), `Completed` (COMPLETED), `PayerActionRequired` (PAYER_ACTION_REQUIRED) |
| `AuthorizationStatus` | `Created` (CREATED), `Captured` (CAPTURED), `Denied` (DENIED), `PartiallyCaptured` (PARTIALLY_CAPTURED), `Voided` (VOIDED), `Pending` (PENDING) |
| `CaptureStatus` | `Completed` (COMPLETED), `Declined` (DECLINED), `PartiallyRefunded` (PARTIALLY_REFUNDED), `Pending` (PENDING), `Refunded` (REFUNDED), `Failed` (FAILED) |
| `RefundStatus` | `Cancelled` (CANCELLED), `Failed` (FAILED), `Pending` (PENDING), `Completed` (COMPLETED) |
| `PaymentTokenStatus` | `Created` (CREATED), `PayerActionRequired` (PAYER_ACTION_REQUIRED), `Approved` (APPROVED), `Vaulted` (VAULTED), `Tokenized` (TOKENIZED) |
| `VaultTokenRequestType` | `SetupToken` (SETUP_TOKEN) — only member |
| `CardBrand` | `Visa` (VISA), `Mastercard` (MASTERCARD), `Amex` (AMEX), `Discover` (DISCOVER), `Jcb` (JCB), `Diners` (DINERS), `Maestro` (MAESTRO), `Rupay` (RUPAY), … (30 members), `Unknown` (UNKNOWN) |
| `CardType` | `Credit` (CREDIT), `Debit` (DEBIT), `Prepaid` (PREPAID), `Store` (STORE), `Unknown` (UNKNOWN) |

Enums are `StringEnum<T>`, **not** C# enums: use `CheckoutPaymentIntent.Authorize` (member) or
`CheckoutPaymentIntent.FromValue("AUTHORIZE")`. See `dotnet-models`.

`Money` (`Models/Money.cs`): `CurrencyCode (currency_code): string !req`, `Value (value): string !req` — value is
a decimal STRING; format the eShop total to the cent yourself.

---

## Trap notes (attached to the step where each bites)

- ⚠ **Step 1 (client + DI)** — the `HttpClient`/handler pipeline must be long-lived and reused
  (`IHttpClientFactory`), not rebuilt per request; the SDK client wrapper's lifetime is a separate decision.
  **MUST load `dotnet-client-initialization`** before wiring the client.
- ⚠ **Step 1 (auth / token)** — the credentials-set timing and the token's caching/refresh/expiry lifecycle
  (and how a 401 is retried) are not shown by the option properties. **MUST load `dotnet-authentication`**
  before wiring credentials.
- ⚠ **Step 1 (base URL / retries / timeouts)** — whether a failed non-idempotent write can be re-sent by the
  retry policy, and what `RetryOptions.Timeout` actually bounds (per-attempt vs whole call), are not visible in
  the option names; there is no built-in logging hook. **MUST load `dotnet-configuration-resilience`** before
  tuning the client. (Especially relevant since writes here carry money — Steps 2–8.)
- ⚠ **Steps 2, 8 (building request bodies / reading enums & unions)** — enums are `StringEnum<T>`, nested
  payment-source objects are records with `init`-only members, and unmodeled JSON is dropped on deserialize.
  **MUST load `dotnet-models`** before constructing payloads or mapping responses to eShop types.
- ⚠ **Step 3 (calling with all-optional params)** — many params are nullable-with-no-default and mis-bind in a
  positional call; whether to call with named arguments is a call-shape concern. **MUST load
  `dotnet-calling-endpoints`** before the first call.
- ⚠ **All steps (error boundary)** — which exception types actually reach a catch, why `TryGetRawError` is not a
  catch-all, and the Case-A vs Case-B split (Steps 2–8 are Case A with per-op accessor names —
  `TryGetError`/`TryGetError1`/`TryGetNoContent`; Step 10 is Case B `RawError`). **MUST load
  `dotnet-error-handling`** before writing the boundary.
- ⚠ **Testing** — the `HttpClient` constructor argument is the test seam; match the eShopOnWeb test framework.
  **MUST load `dotnet-testing`** before stubbing the SDK.

---

## REQUIRED READING (load BEFORE implementation starts — this sheet deliberately omits their contents)

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, HttpClient lifetime, DI registration |
| `dotnet-authentication` | Step 1 — OAuth2 credentials wiring, token lifecycle, 401 handling |
| `dotnet-configuration-resilience` | Step 1 — base-URL/server selection, retries, timeouts, paging (Step 10) |
| `dotnet-calling-endpoints` | Steps 2–10 — named-argument calling, async/cancellation, envelope shapes |
| `dotnet-models` | Steps 2, 7, 8 — request bodies, `StringEnum<T>`, `Money`/nullable reads |
| `dotnet-error-handling` | All steps — the try/catch boundary, Case A/B, status + body reads |
| `dotnet-testing` | Test project — faking the SDK at the `HttpClient` seam |

**Two hazard rows that must shape the error boundary from the first version (both are `JsonException`,
opposite handling):**
- A drifted or malformed **2xx** body (a missing `required` member — e.g. `Money.Value`, `Order.*`) surfaces as
  a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — an SDK-exception-only
  catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the
  `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx
  then reports a deterministic rejection (e.g. a 422 card decline) as an outage, and a caller that retries 5xx
  retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## Assumptions & Blockers

- **Assumption**: "live vs sandbox" is selected by the base-URL config, since the SDK exposes no live
  `ServerEnvironment` member (see Gaps). Assumed `PayPal:BaseUrl`, when set, is the single source of truth for
  the host (sandbox or live) and is applied via `options.Server.Default.Sandbox.BaseUrl`.
- **Assumption**: eShop order total is formatted to a 2-decimal string for `Money.Value`; currency comes from
  config and is passed as `CurrencyCode` on every `Money`/`AmountWithBreakdown`.
- **Assumption**: card vault uses the two-step setup-token→payment-token flow (8a); if PCI scope forbids raw PAN
  on your servers at all, neither 8a nor 8b is appropriate and a hosted-fields/JS approach (out of this SDK's
  scope) is required — confirm PCI posture with the integrator.
- **UNVERIFIED (live-wire, handled defensively in-sheet)**: (a) the exact `rel` of the 3DS/approval link in
  Step 2; (b) the exact `Issue` code signalling an unreauthorizable hold in Step 5. Both are handled by
  best-effort extraction with fallback, per the directives on those steps.
- **No capability gaps blocking the integration.** Every requested operation (1–10) is exposed by the SDK.

### Gaps to report to the integrator
- **No `Production`/`Live` `ServerEnvironment` member.** `ServerEnvironment` exposes only `Sandbox`
  (**[source: `Servers/ServerEnvironment.cs`]**). Going live is done **exclusively** by overriding
  `options.Server.Default.Sandbox.BaseUrl` to `https://api-m.paypal.com`. This is not a missing capability
  (live is reachable) but the environment enum cannot express it — the base-URL override is the only lever, and
  it correctly covers the OAuth2 token request as well.
- Everything else requested (create/authorize/capture/reauthorize/void/refund, card vault + reuse + delete,
  idempotency keys, transaction search with paging) is genuinely exposed — no functional gap.
