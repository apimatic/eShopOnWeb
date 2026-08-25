# PayPal .NET SDK integration plan — eShopOnWeb

Scope: server-to-server (headless) card authorization, card vaulting, capture/void/reauthorize,
refunds, and transaction reporting, on `AsadAli.Checkout.Sdk` (root namespace `PayPalServerSdk`),
against sandbox per the SDK map (`sdk-map.md` + `map/operations/*.md` + `map/models/*.md`,
release `v1.0.1`, source commit `9653d18`).

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 1 | Client construction, DI, and auth (ClientId/ClientSecret/Environment/BaseUrl from config) | `PayPalServerSdkClient`, `AddPayPalServerSdkClient` |
| 2 | Direct (raw) card authorization — no vault, no redirect | `Orders.CreateOrder`, `Orders.AuthorizeOrder` |
| 3 | Save a card (vault a payment token) | `Vault.CreatePaymentToken` |
| 4 | Pay with a previously vaulted card | `Orders.CreateOrder`, `Orders.AuthorizeOrder` (with `CardRequest.VaultId`) |
| 5 | Delete/deactivate a vaulted card | `Vault.DeletePaymentToken` |
| 6 | Capture an authorization (fulfilment step) | `Payments.CaptureAuthorizedPayment` |
| 7 | Reauthorize a stale authorization | `Payments.ReauthorizePayment` |
| 8 | Void an authorization (order cancelled) | `Payments.VoidPayment` |
| 9 | Refund a capture (full/partial, idempotent) | `Payments.RefundCapturedPayment` |
| 10 | Idempotency for all writes above | `payPalRequestId` param → `PayPal-Request-Id` header on every op in steps 2–4, 6, 7, 9, 3 |
| 11 | Transaction reporting / reconciliation | `TransactionSearch.SearchTransactions` (paged) |

Steps 2 and 4 share the same two calls; the only difference is which fields are populated on
`CardRequest` (raw PAN+expiry+cvc vs. `VaultId`).

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
> live in different ones.

### 2.1 Client, auth, server/base-URL

| Fact | Value | Source |
|---|---|---|
| Client class | `PayPalServerSdk.PayPalServerSdkClient` | `new PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)` — `sdk-map.md` |
| Options class | `PayPalServerSdk.PayPalServerSdkClientOptions` — `Environment: PayPalServerSdk.Servers.ServerEnvironment`, `Retry: PayPalServerSdk.Core.Configuration.RetryOptions`, `Logging: LoggingOptions`, `Server: PayPalServerSdk.ServerOptions`, `Oauth2: PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials?`, `Oauth2TokenStrategy: PayPalServerSdk.Core.Authentication.OAuth2.IOAuth2TokenStrategy<OAuth2ClientCredentials>?` | `sdk-map.md` §Getting a client / §Servers & auth |
| Credentials type | `OAuth2ClientCredentials { required string ClientId; required string ClientSecret; string? Scope; }` — bind `PayPal:ClientId`/`PayPal:ClientSecret` here | Source: `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs` (confirmed — map's auth table names the property, not its member shape) |
| Environment enum | `PayPalServerSdk.Servers.ServerEnvironment` — **only member is `ServerEnvironment.Sandbox`** (`ServerEnvironment.Default()` also returns `Sandbox`) | Source: `Servers/ServerEnvironment.cs` — the map's own §Servers & auth table lists exactly this one member; confirmed against source because "sandbox vs live" is central to this integration |
| Base URL override point | `PayPalServerSdk.ServerOptions` → `.Default` (`PayPalServerSdk.Servers.DefaultOptions`) → `.Sandbox.BaseUrl: string`, default `"https://api-m.sandbox.paypal.com"`. Bind `PayPal:BaseUrl` here when set; otherwise pick `https://api-m.sandbox.paypal.com` or `https://api-m.paypal.com` yourself based on `PayPal:Environment`, since the SDK's own `Environment` enum cannot make that choice (only one member exists). | Source: `ServerOptions.cs`, `Servers/DefaultOptions.cs` |
| Base URL reaches the OAuth token call too | Confirmed: every operation resolves its URL via `_server.Default(path)` (e.g. `Api/Orders.cs`: `_server.Default("/v2/checkout/orders")`), and the OAuth2 client-credentials token strategy is built with `OAuth2ClientCredentialsStrategy.ForBasicAuthRequest(server.Default("/v1/oauth2/token"), rawClient)` — the **same** `Server.Default(path)` → `DefaultOptions.Resolve(...)` → `Sandbox.BaseUrl` resolution. One `BaseUrl` override therefore covers the token endpoint and every API call. | Source: `AuthSchemes.cs`, `Server.cs`, `Servers/DefaultOptions.cs`, `Api/Orders.cs` |
| Token request mechanics (informational — not to be reimplemented) | `POST {BaseUrl}/v1/oauth2/token`, HTTP Basic auth header from `ClientId:ClientSecret`, form body `grant_type=client_credentials` (+ `scope` if set); the SDK issues this automatically the first time a call needs a token. | Source: `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentialsStrategy.cs` |
| DI registration | `IServiceCollection.AddPayPalServerSdkClient(Action<PayPalServerSdkClientOptions>? configure = null)` — registers `PayPalServerSdkClient` as a singleton built from an `IHttpClientFactory`-created `HttpClient` | `sdk-map.md`; source `ServiceCollectionExtensions.cs` |
| Idempotency header | Every `payPalRequestId` parameter below is sent as HTTP header **`PayPal-Request-Id`** (confirmed `new HeaderParam("PayPal-Request-Id", payPalRequestId)` in `Api/Orders.cs`, `Api/Payments.cs`, `Api/Vault.cs`) — pass a caller-generated stable string (e.g. a GUID tied to the business operation, not regenerated on retry) | Source: `Api/Orders.cs:62,105,185`; `Api/Payments.cs:59,186,226,265`; `Api/Vault.cs:50,79` |
| `prefer` header default | Every mutating op below defaults `prefer` to `"return=minimal"` — **pass `prefer: "return=representation"` explicitly** on every call whose response fields this integration reads (all of §2.2–§2.5), or the nested fields (`purchase_units[].payments.authorizations[]`, `seller_receivable_breakdown`, etc.) may come back unpopulated | `map/operations/Orders.md`, `map/operations/Payments.md` (parameter present with this default on every listed signature) |
| **Hidden `Idempotency-Key` header — unconditional, unconfigurable, root cause of a live `422 TRANSACTION_REFUSED`** | Every mutating call across **every** controller (`Orders`, `Payments`, `Vault`, `Subscriptions`) and every OAuth2 token-strategy (client-credentials, authorization-code, password) attaches its own **hardcoded, unrelated** `Idempotency-Key: {a fresh Guid.NewGuid() every call}` header, literally `new HeaderParam("Idempotency-Key", Guid.NewGuid())` inlined at each call site — this is entirely separate from the `payPalRequestId`→`PayPal-Request-Id` header above and is **not** documented in the map, **not** exposed by `PayPalServerSdkClientOptions` (`Environment`/`Retry`/`Logging`/`Server`/`Oauth2`/`Oauth2TokenStrategy` — none control headers) and **not** exposed by per-call `RequestOptions` (its only member is `LogLevel? LogLevel` — confirmed by reading the full record). **There is no supported knob to suppress it.** Live-verified root cause: a byte-identical `CreateOrder`/`AuthorizeOrder` request succeeds via raw curl (no `Idempotency-Key` header) and fails with `422 TRANSACTION_REFUSED` via this SDK (which sends it) — removing the header via an `HttpClient`-pipeline `DelegatingHandler` is therefore required; there is no in-SDK alternative. `RawClient.Execute` builds a plain `HttpRequestMessage`, adds headers via `httpRequest.Headers.AddRange(headers)`, then calls `_httpClient.SendAsync(httpRequest, ...)` (`Core/RawClient.cs`) — so a `DelegatingHandler` registered on the same `HttpClient` (e.g. via `.AddHttpMessageHandler<T>()` on the client `AddPayPalServerSdkClient` resolves from `IHttpClientFactory`, which is the **default-named** client) intercepts the request after the SDK sets this header and can `request.Headers.Remove("Idempotency-Key")` before the network call — this is the confirmed, source-grounded suppression mechanism. Why PayPal's live authorization decisioning rejects this header specifically (vs. tolerating it on `Vault.CreatePaymentToken`, which also sends it, confirmed at `Api/Vault.cs`) is **UNVERIFIED** — nothing in map/source explains PayPal's fraud/risk engine; only the live correlation (curl vs. SDK, header present vs. absent) confirms causation here. | Source: `Api/Orders.cs` (7 call sites), `Api/Payments.cs` (4), `Api/Vault.cs` (3), `Api/Subscriptions.cs` (10), `Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentialsStrategy.cs`, `Core/Authentication/OAuth2/AuthorizationCode/OAuth2AuthorizationCodeStrategy.cs`, `Core/Authentication/OAuth2/Password/OAuth2PasswordCredentialsStrategy.cs`, `Core/RequestOptions.cs`, `Core/RawClient.cs` |

### 2.2 Direct card authorization (headless) — Orders

| | |
|---|---|
| Controller property | `client.Orders` |
| Step A | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, PayPalServerSdk.Models.OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default) → Task<PayPalServerSdk.Models.Order>` |
| Step B | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, PayPalServerSdk.Models.OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default) → Task<PayPalServerSdk.Models.OrderAuthorizeResponse>` |
| `OrderRequest` (step A body) | `Intent (intent): CheckoutPaymentIntent !req` = `CheckoutPaymentIntent.Authorize`; `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req`; `Payer (payer): Payer?`; `PaymentSource (payment_source): PaymentSource?` (leave null — supply the card on step B instead); `ApplicationContext: OrderApplicationContext?` |
| `PurchaseUnitRequest` (one element) | `Amount (amount): AmountWithBreakdown !req`; `ReferenceId`, `Payee`, `CustomId`, `InvoiceId` etc. optional |
| `AmountWithBreakdown` | `CurrencyCode (currency_code): string !req` (bind `PayPal:Currency`); `Value (value): string !req`; `Breakdown: AmountBreakdown?` |
| `OrderAuthorizeRequest` (step B body) | `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` |
| `OrderAuthorizeRequestPaymentSource` | `Card (card): CardRequest?` ← populate this for direct card; also has `Token`, `Paypal`, `ApplePay`, `GooglePay`, `Venmo` (unused here) |
| `CardRequest` (raw card fields) | `Name (name): string?` (1–300 chars); `Number (number): string?` — regex `^[0-9]{13,19}$` (13–19 digits, PAN); `Expiry (expiry): string?` — **must be exactly `YYYY-MM`**, regex `^[0-9]{4}-(0[1-9]|1[0-2])$` (e.g. `"2028-12"`); `SecurityCode (security_code): string?` — regex `^[0-9]{3,4}$`; `BillingAddress (billing_address): Address?`; `Attributes: CardAttributes?`; `VaultId (vault_id): string?` (leave null here — see §2.4); `ExperienceContext: CardExperienceContext?` (`ReturnUrl`/`CancelUrl` — only needed if a 3DS challenge occurs, see Assumptions) |
| `Address` (billing address) | `AddressLine1`, `AddressLine2`, `AdminArea2` (city), `AdminArea1` (state/province), `PostalCode` — all `string?`; `CountryCode (country_code): string !req` (ISO 3166-1 alpha-2) |
| Response envelope | `OrderAuthorizeResponse { Id, Status: OrderStatus?, PaymentSource: OrderAuthorizeResponsePaymentSource?, PurchaseUnits: IReadOnlyList<PurchaseUnit>?, Links, CreateTime, UpdateTime }` |
| **Read the authorization from** | `response.PurchaseUnits[0].Payments.Authorizations[0]` — `PurchaseUnit.Payments: PaymentCollection?` → `PaymentCollection.Authorizations: IReadOnlyList<AuthorizationWithAdditionalData>?` → each has `Id (id): string?` (the authorization id — hold onto this for capture/void/reauthorize), `Status (status): AuthorizationStatus?`, `Amount: Money?`, `ExpirationTime (expiration_time): string?` (ISO-8601 — use for staleness checks, see Assumptions), `CreateTime`, `UpdateTime` |
| `AuthorizationStatus` values | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` — **no `Expired` member** (see Assumptions) |
| `OrderStatus` values (top-level `Order`/`OrderAuthorizeResponse.Status`) | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` — the last value is the buyer-redirect case, see Assumptions |
| Error — CreateOrder | `SdkException<PayPalServerSdk.Errors.CreateOrderError>` (Case A). `TryGetError(out PayPalServerSdk.Models.Error error)` [400, 401, 422]; `TryGetRawError(out RawError)` [fallback]. `Error { Name !req, Message !req, DebugId !req, Details: IReadOnlyList<ErrorDetails>?, Links }`; `ErrorDetails { Field?, Value?, Location? = "body", Issue !req, Links?, Description? }` |
| Error — AuthorizeOrder | `SdkException<PayPalServerSdk.Errors.AuthorizeOrderError>` (Case A). `TryGetError(out Error)` [400, 401, 403, 404, 422, 500]; `TryGetRawError` [fallback] — same `Error`/`ErrorDetails` shape as above |
| Pagination | none |
| Map pages | `map/operations/Orders.md`; `map/models/records-1-Ac-Pa.md` (`OrderRequest`, `PurchaseUnitRequest`, `AmountWithBreakdown`, `Address`, `CardRequest`, `OrderAuthorizeRequest`, `Order`, `OrderAuthorizeResponse`); `map/models/records-2-Pa-Ve.md` (`PurchaseUnit`, `PaymentCollection`); `map/models/records-1-Ac-Pa.md` (`AuthorizationWithAdditionalData`); `map/models/enums.md` |

#### 2.2a "Single-step" alternative — `payment_source.card` on `CreateOrder` instead of `AuthorizeOrder`

Confirmed contract for the experiment flagged in the previous round (per PayPal's own doc-comment
terminology, "single-step create order" = supplying `payment_source` directly on `CreateOrder`):

| Fact | Value | Source |
|---|---|---|
| Type of `OrderRequest.PaymentSource` | `PayPalServerSdk.Models.PaymentSource` — **a distinct record from `OrderAuthorizeRequestPaymentSource`** (the type you already use on `AuthorizeOrder`'s body), not the same type | `map/models/records-1-Ac-Pa.md` row `OrderRequest`: `PaymentSource (payment_source): PaymentSource?` |
| `PaymentSource` members | `Card (card): CardRequest?`, `Token (token): Token?`, `Paypal (paypal): PayPalWallet?`, `Bancontact?`, `Blik?`, `Eps?`, `Giropay?`, `Ideal?`, `Mybank?`, `P24?`, `Sofort?`, `Trustly?`, `ApplePay?`, `GooglePay?`, `Venmo?` — namespace `PayPalServerSdk.Models` (all records on this map page share that namespace) | `map/models/records-2-Pa-Ve.md` row `PaymentSource`, source `Models/PaymentSource.cs` |
| Is `PaymentSource.Card` the same `CardRequest` you already build? | **Yes — identical type**, `PayPalServerSdk.Models.CardRequest`, the same record with `Name`/`Number`/`Expiry`/`SecurityCode`/`BillingAddress`/`Attributes`/`VaultId` used in §2.2/§2.4. No second card-request shape exists for this context. | `map/models/records-2-Pa-Ve.md` row `PaymentSource`; `map/models/records-1-Ac-Pa.md` row `CardRequest` |
| Does `AuthorizeOrder(body: null)` compile/type-check after this? | Yes — `body` is `OrderAuthorizeRequest?`, nullable, `null` is a legal argument | `map/operations/Orders.md` `AuthorizeOrder` signature |
| **Is a second `AuthorizeOrder` call even still needed, or does `CreateOrder` alone (intent=AUTHORIZE + `payment_source.card`) already produce the hold synchronously?** | **UNVERIFIED — the map/source do not state this explicitly.** The one grounded hint is PayPal's own doc-comment on the `payPalRequestId` parameter (verbatim on `CreateOrder`/`AuthorizeOrder`/`CaptureOrder`, source `Api/Orders.cs`), which names this exact pattern "single-step create order" — the word "single-step" is suggestive that creation and payment happen in that one call, but no doc-comment anywhere states this outright, and `Order` (CreateOrder's return type) has the identical `PurchaseUnits[].Payments.Authorizations[]` shape as `OrderAuthorizeResponse`, so it is structurally capable of carrying an authorization already. **Defensive directive (do this, don't guess further):** after `CreateOrder`, inspect the returned `Order.Status` and `Order.PurchaseUnits?[0]?.Payments?.Authorizations` *before* deciding whether to call `AuthorizeOrder` at all — if `Status` is already past `Created` (e.g. `Completed`/`Approved`) and an `AuthorizationWithAdditionalData` is already present, the authorization already happened and a further `AuthorizeOrder(body: null)` call may be redundant or may itself error (an order that's already progressed past `CREATED` is not necessarily still authorizable); if `Status` is still `Created` and no authorization is present, proceed to call `AuthorizeOrder(body: null)` as the second step, same response-reading path as §2.2. Log both `Order.Status` values you observe from a live sandbox run so this row can be corrected from UNVERIFIED to confirmed once you've seen the actual behavior. | `Api/Orders.cs` (`payPalRequestId` doc-comment on all three operations); `map/models/records-1-Ac-Pa.md` rows `Order`, `OrderAuthorizeResponse` (identical `PurchaseUnits`/`Payments`/`Authorizations` shape) |

This experiment is **not asserted to fix `TRANSACTION_REFUSED`** (§5 already notes nothing in source ties
that code to this distinction) — it is offered strictly to answer the three questions asked, with the
open synchronous-vs-two-call question called out as UNVERIFIED rather than guessed.

### 2.3 Save a card (vault a payment token) — Vault

| | |
|---|---|
| Controller property | `client.Vault` |
| Signature | `CreatePaymentToken(string? payPalRequestId, PayPalServerSdk.Models.PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default) → Task<PayPalServerSdk.Models.PaymentTokenResponse>` |
| `PaymentTokenRequest` | `Customer (customer): Customer?` — `Customer { Id?: string, MerchantCustomerId?: string }`; `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req` |
| `PaymentTokenRequestPaymentSource` | `Card (card): PaymentTokenRequestCard?` ← populate for direct-card vaulting; `Token (token): VaultTokenRequest?` (alternate path via a prior `CreateSetupToken` call — not needed for the direct-card flow) |
| `PaymentTokenRequestCard` | `Name`, `Number`, `Expiry`, `SecurityCode` — same wire names/format constraints as `CardRequest` in §2.2 (verified on `CardRequest`; `PaymentTokenRequestCard` is the analogous vault-request record — confirm field-level regexes against source if a save fails validation); `Brand (brand): CardBrand?`; `BillingAddress (billing_address): Address?` |
| Response envelope | `PaymentTokenResponse { Id (id): string?, Customer: CustomerResponse?, PaymentSource: PaymentTokenResponsePaymentSource?, Links }` |
| **The reusable token id** | `response.Id` — this is what you store and pass back as `CardRequest.VaultId` later (§2.4) |
| **Card descriptor fields (no PAN ever returned)** | `response.PaymentSource.Card` : `CardPaymentTokenEntity { Name?, LastDigits (last_digits): string?, Brand (brand): CardBrand?, Expiry (expiry): string?, BillingAddress: CardResponseAddress?, VerificationStatus: CardVerificationStatus?, Type: CardType? }` — use `Brand` + `LastDigits` + `Expiry` to show the shopper "Visa ····1111, exp 12/28" |
| `CardBrand` values | `Visa`, `Mastercard`, `Discover`, `Amex`, `Solo`, `Jcb`, `Star`, `Delta`, `Switch`, `Maestro`, `CbNationale`, `Configoga`, `Confidis`, `Electron`, `Cetelem`, `ChinaUnionPay`, `Diners`, `Elo`, `Hiper`, `Hipercard`, `Rupay`, `Ge`, `Synchrony`, `Eftpos`, `CarteBancaire`, `StarAccess`, `Pulse`, `Nyce`, `Accel`, `Unknown` |
| `CardType` values | `Credit`, `Debit`, `Prepaid`, `Store`, `Unknown` |
| Error | `SdkException<PayPalServerSdk.Errors.CreatePaymentTokenError>` (Case A). `TryGetError1(out PayPalServerSdk.Models.Error1 error)` [400, 403, 404, 422, 500]; `TryGetRawError` [fallback]. `Error1 { Name !req, Message !req, DebugId !req, Details: IReadOnlyList<ErrorDetails1>?, Links: IReadOnlyList<ErrorLinkDescription>? }`; `ErrorDetails1 { Field?, Value?, Location? = "body", Issue !req, Links?, Description? }` — note this is a **different** `Error`/`ErrorDetails` pair than Orders/Payments use (`Error1`/`ErrorDetails1`, not `Error`/`ErrorDetails`) |
| Pagination | none |
| Idempotency | `payPalRequestId` → `PayPal-Request-Id` header (same mechanism as §2.1) — recommended, since retrying a double-click "save card" should not create two tokens |
| Map pages | `map/operations/Vault.md`; `map/models/records-2-Pa-Ve.md` (`PaymentTokenRequest`, `PaymentTokenRequestPaymentSource`, `PaymentTokenRequestCard`, `PaymentTokenResponse`, `PaymentTokenResponsePaymentSource`, `Customer`, `CustomerResponse`); `map/models/records-1-Ac-Pa.md` (`CardPaymentTokenEntity`, `CardResponseAddress`, `Error1`); `map/models/enums.md` |

### 2.4 Pay with a saved/vaulted card

Identical calls to §2.2, with one difference: on `OrderAuthorizeRequestPaymentSource.Card` (`CardRequest`),
set **only** `VaultId (vault_id): string?` (regex `^[0-9a-zA-Z_-]+$`, the id from §2.3's
`PaymentTokenResponse.Id`) and leave `Number`/`Expiry`/`SecurityCode` null — the raw PAN is never
resubmitted. All other fields (response envelope, authorization extraction path, error case,
`AuthorizationStatus` values) are exactly as in §2.2.

### 2.5 Delete/deactivate a vaulted card

| | |
|---|---|
| Controller property | `client.Vault` |
| Signature | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default) → Task` (`void`) |
| Effect | The token can no longer be charged (`VaultId` reuse fails) and drops out of `ListCustomerPaymentTokens` |
| Error | `SdkException<PayPalServerSdk.Errors.DeletePaymentTokenError>` (Case A). `TryGetError1(out Error1)` [400, 403, 500]; `TryGetRawError` [fallback] — same `Error1`/`ErrorDetails1` shape as §2.3 |
| Pagination | none |
| Map page | `map/operations/Vault.md` |

### 2.6 Capture an authorization (fulfilment)

| | |
|---|---|
| Controller property | `client.Payments` |
| Signature | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, PayPalServerSdk.Models.CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default) → Task<PayPalServerSdk.Models.CapturedPayment>` |
| `CaptureRequest` (full-amount capture) | `Amount (amount): Money?` — **leave null to capture the full outstanding authorized amount** (per the record's own doc: "Captures either a portion or the full authorized amount"); `FinalCapture (final_capture): bool? = false` — set `true` to release any remaining hold; `InvoiceId?`, `PaymentInstruction?`, `NoteToPayer?`, `SoftDescriptor?` |
| Response envelope | `CapturedPayment { Status: CaptureStatus?, StatusDetails: CaptureStatusDetails?, Id (id): string?, Amount (amount): Money?, FinalCapture: bool? = false, SellerReceivableBreakdown: SellerReceivableBreakdown?, DisbursementMode: DisbursementMode? = Instant, ProcessorResponse: ProcessorResponse?, Links, CreateTime, UpdateTime }` |
| Captured amount | `response.Amount` (`Money { CurrencyCode !req, Value !req }`) |
| Fee / net breakdown | `response.SellerReceivableBreakdown` : `SellerReceivableBreakdown { GrossAmount (gross_amount): Money !req, PaypalFee (paypal_fee): Money?, PaypalFeeInReceivableCurrency: Money?, NetAmount (net_amount): Money? — merchant receivable, ReceivableAmount: Money?, ExchangeRate?, PlatformFees: IReadOnlyList<PlatformFee>? }` |
| `CaptureStatus` values | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` |
| Error | `SdkException<PayPalServerSdk.Errors.CaptureAuthorizedPaymentError>` (Case A). `TryGetError(out Error)` [400, 401, 403, 404, 409, 422]; `TryGetNoContent(out RawError)` [500]; `TryGetRawError(out RawError)` [fallback] |
| **Expired / no-longer-capturable authorization** | The API surfaces this as a **400/404/409/422** on `CaptureAuthorizedPayment`, not as a distinct return value — read `Error.Details[].Issue`/`.Description` (both plain `string`, not enums; see Assumptions for why the literal codes are UNVERIFIED) and fall back to a generic "capture rejected, needs operator" path for any issue string you don't recognize. Before attempting capture, you can pre-check staleness client-side via the held `AuthorizationWithAdditionalData.ExpirationTime`/`PaymentAuthorization.ExpirationTime` (ISO-8601) from §2.2/§2.7 — but you must still handle the live rejection, since PayPal's own honor-period clock is authoritative, not your local comparison |
| Idempotency | `payPalRequestId` → `PayPal-Request-Id` header |
| Pagination | none |
| Map page | `map/operations/Payments.md`; `map/models/records-1-Ac-Pa.md` (`CaptureRequest`, `CapturedPayment`, `Money`); `map/models/records-2-Pa-Ve.md` (`SellerReceivableBreakdown`) |

### 2.7 Reauthorize a stale authorization vs. "cannot be renewed"

| | |
|---|---|
| Controller property | `client.Payments` |
| Signature | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, PayPalServerSdk.Models.ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default) → Task<PayPalServerSdk.Models.PaymentAuthorization>` |
| Business rule (from the operation's own doc, `map/operations/Payments.md`) | "Reauthorize a payment after its initial three-day honor period expires… you can reauthorize a payment only once from days four to 29… If 30 days have transpired since the date of the original authorization, you must create an authorized payment instead of reauthorizing… A reauthorized payment itself has a new honor period of three days… The allowed amount depends on context and geography, for example in US it is up to 115% of the original authorized amount, not to exceed an increase of $75 USD. Supports only the `amount` request parameter." |
| `ReauthorizeRequest` | `Amount (amount): Money?` — only field the operation accepts |
| **"can be renewed, please retry"** | The call **succeeds** (`200`), returning a `PaymentAuthorization` with a new `Id`? — **no**, re-check: `PaymentAuthorization` has its own `Id`; treat the returned object's `Id`/`Status`/`ExpirationTime` as the authorization to use going forward (the map does not state whether the id changes on reauthorization — verify the returned `Id` against the original at implementation time rather than assuming either way) |
| **"cannot be renewed, operator must be told"** | The call **throws** `SdkException<PayPalServerSdk.Errors.ReauthorizePaymentError>` (Case A) — `TryGetError(out Error)` [400, 401, 403, 404, 422]; `TryGetNoContent(out RawError)` [500]; `TryGetRawError` [fallback]. A 422 here (already voided / already fully captured / beyond the 29-day window / beyond the allowed amount bump) is the "operator must be told" case — same caveat as §2.6 on trusting `Error.Details[].Issue` literal values |
| `PaymentAuthorization` response fields | `Status: AuthorizationStatus?`, `StatusDetails: AuthorizationStatusDetails?` (`Reason: AuthorizationIncompleteReason?` — `PendingReview`, `DeclinedByRiskFraudFilters`), `Id`, `Amount`, `ExpirationTime`, `CreateTime`, `UpdateTime` |
| Idempotency | `payPalRequestId` → `PayPal-Request-Id` header |
| Pagination | none |
| Map page | `map/operations/Payments.md`; `map/models/records-2-Pa-Ve.md` (`PaymentAuthorization`, `ReauthorizeRequest`) |

### 2.8 Void an authorization

| | |
|---|---|
| Controller property | `client.Payments` |
| Signature | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default) → Task<PayPalServerSdk.Models.PaymentAuthorization>` |
| Body | none — void takes no request body, only the id |
| Response | `PaymentAuthorization` with `Status = AuthorizationStatus.Voided` on success |
| Error | `SdkException<PayPalServerSdk.Errors.VoidPaymentError>` (Case A). `TryGetError(out Error)` [401, 403, 404, 409, 422]; `TryGetNoContent(out RawError)` [500]; `TryGetRawError` [fallback]. Per the operation's note, "You cannot void an authorized payment that has been fully captured" — that case surfaces as one of the above statuses (409 is the natural fit, but confirm against `Error.Details` rather than assuming the status code alone disambiguates) |
| Idempotency | `payPalRequestId` → `PayPal-Request-Id` header |
| Pagination | none |
| Map page | `map/operations/Payments.md` |

### 2.9 Refund a capture

| | |
|---|---|
| Controller property | `client.Payments` |
| Signature | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, PayPalServerSdk.Models.RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default) → Task<PayPalServerSdk.Models.Refund>` |
| `RefundRequest` | Full refund: pass `body = null` or an empty `RefundRequest` (`Amount` left null) — per the record's own doc "For a full refund, include an empty request body." Partial refund: set `Amount (amount): Money?` to the partial value. Also: `CustomId?`, `InvoiceId?`, `NoteToPayer?`, `PaymentInstruction?` |
| Response envelope | `Refund { Status: RefundStatus?, StatusDetails: RefundStatusDetails?, Id (id): string?, Amount: Money?, SellerPayableBreakdown: SellerPayableBreakdown?, Links, CreateTime, UpdateTime }` |
| Refund id / status | `response.Id`, `response.Status` |
| `RefundStatus` values | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` |
| `SellerPayableBreakdown` | `GrossAmount?`, `PaypalFee (paypal_fee): Money?`, `NetAmount (net_amount): Money?`, `TotalRefundedAmount (total_refunded_amount): Money?` — **`TotalRefundedAmount` is the running total refunded so far against the original capture; read it after each refund to know the remaining refundable amount (`capture.Amount − TotalRefundedAmount`)** |
| Idempotency | `payPalRequestId` → `PayPal-Request-Id` header — **this is the mechanism for "does a retried identical refund double-refund"**: pass the same caller-generated value on retry and PayPal treats it as the same request |
| Over-refund enforcement | UNVERIFIED whether PayPal's live API itself rejects a refund that would exceed the capturable amount (this is server-side business logic, not encoded in any SDK type — `RefundRequest.Amount` is just an unconstrained `Money`). **Defensive directive:** track `SellerPayableBreakdown.TotalRefundedAmount` from the most recent refund/capture read yourself before issuing a new refund, and still treat any `SdkException<RefundCapturedPaymentError>` (422/409) as the authoritative rejection — do not rely on either mechanism alone |
| Error | `SdkException<PayPalServerSdk.Errors.RefundCapturedPaymentError>` (Case A). `TryGetError(out Error)` [400, 401, 403, 404, 409, 422]; `TryGetNoContent(out RawError)` [500]; `TryGetRawError` [fallback] |
| Pagination | none |
| Map page | `map/operations/Payments.md`; `map/models/records-2-Pa-Ve.md` (`RefundRequest`, `Refund`, `SellerPayableBreakdown`) |

### 2.10 Transaction reporting / reconciliation

| | |
|---|---|
| Controller property | `client.TransactionSearch` |
| Signature | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default) → Task<PayPalServerSdk.Models.SearchResponse>` |
| Wire query params | `start_date` ← `startDate`, `end_date` ← `endDate` (both ISO-8601 strings — required, no default); `transaction_id`, `transaction_type`, `transaction_status`, `transaction_amount`, `transaction_currency`, `payment_instrument_type`, `store_id`, `terminal_id` — all nullable, must pass explicitly (`null` to skip); `fields` (default `"transaction_info"`); `balance_affecting_records_only` (default `"Y"`); `page_size` (default `100`); `page` (default `1`) |
| Response envelope | `SearchResponse { TransactionDetails: IReadOnlyList<TransactionDetails>?, AccountNumber?, StartDate?, EndDate?, LastRefreshedDatetime?, Page (page): int?, TotalItems (total_items): int?, TotalPages (total_pages): int?, Links }` |
| Pagination mechanism | **Manual, page-number based — no cursor, no built-in "next page" helper** (`map/operations/TransactionSearch.md` states "Pagination: none (only `page`, no `perPage`)"). Read `TotalPages` from the first response, then loop `page = 2..TotalPages` re-issuing `SearchTransactions` with the same `startDate`/`endDate` and increasing `page`, until every page is covered |
| Per-transaction fields | `TransactionDetails { TransactionInfo: TransactionInformation?, PayerInfo?, ShippingInfo?, CartInfo?, StoreInfo?, AuctionInfo?, IncentiveInfo? }`; `TransactionInformation { TransactionId (transaction_id): string?, PaypalReferenceId (paypal_reference_id): string?, PaypalReferenceIdType (paypal_reference_id_type): PayPalReferenceIdType?, TransactionAmount (transaction_amount): Money?, FeeAmount: Money?, TransactionStatus (transaction_status): string?, TransactionInitiationDate?, TransactionUpdatedDate?, InvoiceId?, CustomField? }` |
| Linking a transaction back to an order/capture/authorization | `TransactionInformation.PaypalReferenceId` + `PaypalReferenceIdType` (`PayPalReferenceIdType` values: `Odr (ODR)` = order, `Txn (TXN)` = transaction, `Sub (SUB)` = subscription, `Pap (PAP)` = billing agreement) — there is **no** direct `authorization_id`/`capture_id` field on `TransactionInformation`; matching a specific authorization/capture id therefore relies on `PaypalReferenceId` being the order id (type `Odr`) and cross-referencing your own stored order↔authorization↔capture mapping, since this SDK's search response does not echo the authorization/capture id directly |
| `TransactionStatus` | Plain `string?` (not a typed enum on `TransactionInformation`) — compare against known PayPal transaction-status literals defensively (do not hardcode an exhaustive list from memory; log/branch on the raw string) |
| Error | `SdkException<RawError>` — **Case B** (the only Case-B operation in this SDK). `RawError { StatusCode: HttpStatusCode, ReadAsBytes(): ReadOnlyMemory<byte>, ReadAsString(): string, ReadAsJson<T>(): T? }` — there is no typed error payload; read `StatusCode` + `ReadAsString()`/`ReadAsJson<T>()` yourself |
| Date-range limits | UNVERIFIED whether PayPal enforces a maximum queryable date-range span per call (not stated in this operation's map notes or in any SDK type). If a wide `startDate`/`endDate` span is rejected, it will surface as a `SdkException<RawError>` — read `StatusCode`/`ReadAsString()` to see PayPal's stated reason rather than assuming a specific day-count limit |
| Map page | `map/operations/TransactionSearch.md`; `map/models/records-2-Pa-Ve.md` (`SearchResponse`, `TransactionDetails`, `TransactionInformation`) |

---

## 3. Trap notes

> ⚠ Step 1 (client & DI) — the `HttpClient` the SDK client wraps must be long-lived and reused
> via `IHttpClientFactory`, not rebuilt per request; whether `AddPayPalServerSdkClient`'s
> singleton registration already gets this right for your DI lifetime needs checking. **MUST
> load `dotnet-client-initialization`** before registering the client.

> ⚠ Step 1 (auth) — which of `Oauth2` vs `Oauth2TokenStrategy` to set, and whether setting both is
> safe, isn't obvious from the property list alone; also confirm whether the SDK caches/refreshes
> the OAuth token across calls or re-authenticates every time. **MUST load
> `dotnet-authentication`** before wiring `PayPal:ClientId`/`ClientSecret` in.

> ⚠ Steps 2–9 (every call) — several optional parameters (`payPalMockResponse`,
> `payPalClientMetadataId`, `payPalAuthAssertion`, etc.) are nullable-with-no-default and so
> **must** be passed explicitly even when unused; getting the positional order wrong silently
> mis-binds a different parameter. **MUST load `dotnet-calling-endpoints`** before writing the
> first call.

> ⚠ Steps 2–9 (request/response models) — `CardRequest`, `Money`, `Address`, etc. are `record`
> types with `init`-only `required` members and JSON wire names that differ from the C#
> identifiers; enum values are `StringEnum<T>` static members, not C# `enum` values, and must be
> constructed accordingly. **MUST load `dotnet-models`** before building `OrderRequest`/
> `CardRequest`/`CaptureRequest`/`RefundRequest` payloads.

> ⚠ Steps 2, 3, 6, 7, 9 (error boundary) — Case A vs Case B differs per operation (§2), and
> `TryGetRawError` is not a catch-all on a typed error — it is one accessor among several that
> may or may not match. Getting the catch ladder wrong for even one operation here means an error
> body silently fails to parse into anything useful. **MUST load `dotnet-error-handling`** before
> writing any `try/catch` around these calls (see the two mandatory `JsonException` hazard rows
> below, which belong to this same step).

> ⚠ Steps 1, 9, 10 (config & resilience) — the SDK's `RetryOptions.HttpMethodsToRetry` and
> `StatusCodesToRetry` don't tell you whether a transport-level failure (as opposed to a status
> code) is retried on `POST` calls like `CreateOrder`/`CaptureAuthorizedPayment`/
> `RefundCapturedPayment` — if it is, the `PayPal-Request-Id` idempotency header (§2.1) is the
> only thing standing between a retried authorize/capture/refund and a duplicate charge. **MUST
> load `dotnet-configuration-resilience`** before tuning retries/timeouts or relying on
> idempotency headers to be sufficient on their own.

> ⚠ Step 11 (transaction search) — confirm whether `SearchTransactions`'s positional optional
> parameters bind correctly when called positionally versus named; given the long parameter list
> (14 params before `requestOptions`), a positional call is a likely source of silent mis-binding.
> **MUST load `dotnet-calling-endpoints`** (same skill as above, called out again because this
> operation's signature is the largest in the SDK).

> ⚠ Step 5 (all tests) — the seam to fake is the `HttpClient` passed into
> `PayPalServerSdkClient`'s constructor, not the SDK's internal types. **MUST load
> `dotnet-testing`** before writing tests for the authorize/vault/capture/refund/search flows.

**Mandatory error-boundary hazard rows** (per the two directions `System.Text.Json.JsonException`
reaches the boundary from):

- a drifted or malformed **2xx** body (a missing `required` member on e.g. `CapturedPayment`,
  `Refund`, `PaymentTokenResponse`, `SearchResponse`) surfaces as a `JsonException` from
  deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder around
  §2.2–§2.10's calls lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  (`Error`/`Error1`/`RawError` per §2's per-operation rows) throws `JsonException` *while the
  error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and
  the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then
  reports a deterministic rejection (e.g. a genuinely-declined card, §2.6/§2.7's "cannot be
  renewed" case) as an outage, and a caller that retries 5xx retries something that can never
  succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 4. REQUIRED READING

Load these before implementation starts — this sheet deliberately does not carry their contents:

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, `HttpClient` lifetime, DI registration |
| `dotnet-authentication` | Step 1 — `Oauth2`/`Oauth2TokenStrategy` wiring, credential loading, token refresh |
| `dotnet-calling-endpoints` | Steps 2–11 — every SDK call, named-argument discipline on long/optional parameter lists |
| `dotnet-models` | Steps 2–9 — building `OrderRequest`/`CardRequest`/`CaptureRequest`/`RefundRequest`/`PaymentTokenRequest`, enum construction, wire-name mapping |
| `dotnet-error-handling` | Steps 2, 3, 6, 7, 9 — the Case A/B catch ladder per operation, `TryGet…` accessor discipline, and the two mandatory `JsonException` hazard rows above |
| `dotnet-configuration-resilience` | Steps 1, 9, 10 — retry/timeout tuning, whether transport-failure retries threaten idempotency on writes |
| `dotnet-testing` | Testing the integration layer — fake the `HttpClient` seam, not SDK internals |

---

## 5. Assumptions & Blockers

**Blocker to confirm before committing to a fully headless design (per the task's explicit ask):**
Direct card authorization is **not unconditionally pure server-to-server** in this SDK/API. Evidence,
all from the map/source, not memory:
- `CardRequest.ExperienceContext: CardExperienceContext?` (fields `ReturnUrl`, `CancelUrl`) exists
  specifically to "customize the payer experience during the 3DS Approval for payment."
- `Order`/`OrderAuthorizeResponse.Status` (`OrderStatus`) includes a `PayerActionRequired
  (PAYER_ACTION_REQUIRED)` member.
- `CardRequest.Attributes.Verification.Method` (`CardVerification.Method: OrdersCardVerificationMethod?
  = ScaWhenRequired`) — values `ScaAlways`, `ScaWhenRequired` (the default), `_3DSecure`, `AvsCvv` —
  is the switch controlling whether/when a Strong Customer Authentication (3-D Secure) challenge is
  invoked.

Net effect: with the default verification method (`ScaWhenRequired`), if the issuer/card network
decides a given card+amount needs cardholder step-up authentication, `AuthorizeOrder` will return
`Status = PayerActionRequired` plus a `payer-action` HATEOAS link that requires a **browser
redirect** — this is not something the SDK can complete server-to-server. Per each member's own
XML doc-comment on `OrdersCardVerificationMethod` (source `Models/Enums/OrdersCardVerificationMethod.cs`):
`ScaAlways` explicitly "will result in a contingency and HATEOAS link being returned" wherever SCA
is implemented (deterministic redirect risk); `_3DSecure` likewise explicitly "surfaces" a
contingency; `ScaWhenRequired` (the default) triggers it only when local regulation mandates SCA
for that card/region; `AvsCvv`'s doc-comment describes no redirect contingency at all and reads as
the intended headless-only option. **UPDATE — live-verified, not just source-verified:** against
this project's actual PayPal sandbox account, `Method = AvsCvv` was tried and PayPal rejected it
outright with a schema-level `422 INVALID_PARAMETER_VALUE` on
`/payment_source/card/attributes/verification/method` — i.e. even though the SDK's pinned spec
(`v1.0.1`, commit `9653d18`) declares `AVS_CVV` as a legal wire value for exactly this field, this
sandbox account's live validation does not currently accept it. Why (account entitlement gap,
processor configuration, or spec/production drift) is **UNVERIFIED** — nothing in the map or SDK
source can explain a live rejection of a schema-legal value, and this plan does not open
`api-reference.md` or guess further enum values against production. **Revised guidance: do not set
`Attributes.Verification.Method` at all — leave it unset (falls back to the SDK's own default,
`ScaWhenRequired`) — and treat `PAYER_ACTION_REQUIRED` as a real, expected outcome the integration
must handle (surface it to the caller/operator, per `PaymentActionRequiredException` already wired
at the `AuthorizeOrder` call site), not an outcome to engineer away by picking a different enum
member.** No member of this enum is confirmed, by source or by this account's live behavior, to
guarantee headless-only authorization for arbitrary cards — this remains a business-risk decision
(accept that some card/amount/region combinations will require a browser step) rather than a
solvable engineering one, and is now on firmer evidence than at initial planning time.

**Second live blocker found past the first (direct card auth still not confirmed working end-to-end):**
after removing the `AvsCvv` override, `Orders.CreateOrder` → `Orders.AuthorizeOrder` with a direct
(raw, non-vaulted) card now clears the 422 but `AuthorizeOrder` returns an HTTP `402`, `Error.Details[].Issue
= "TRANSACTION_REFUSED"` (`Details[].Field` empty — a business-level refusal, not a field-validation
error). Grounding performed:
- The literal string `TRANSACTION_REFUSED` does not appear anywhere in the SDK source tree (confirmed by
  a full-tree search of the clone) — it is not a typed/modeled SDK error code, it only ever reaches you
  as an untyped `string` inside `Error.Details[].Issue`/`.Description` via the generic `TryGetError`
  branch, exactly as observed. The map/source have **no documented meaning** for this specific code.
- The two-step split itself (`CreateOrder` with no `payment_source`, then `AuthorizeOrder` with
  `payment_source.card` supplied) is **not** a known trap — it is explicitly sanctioned by
  `AuthorizeOrder`'s own XML-doc remarks in source (`Api/Orders.cs`): "the buyer must first approve the
  order **or a valid payment_source must be provided in the request**." This confirms §2.2 of this sheet
  as written.
- One documented-but-unnamed asymmetry worth trying as an alternative, not a confirmed fix: PayPal's own
  boilerplate doc-comment on the `payPalRequestId` parameter (verbatim on `CreateOrder`, `AuthorizeOrder`,
  and `CaptureOrder` alike, source `Api/Orders.cs`) specifically names supplying `payment_source` (Card /
  `vault_id` / billing-agreement-id) **directly on `CreateOrder`** as the "single-step create order" case
  — PayPal's own terminology gives that pattern a name; the "empty `CreateOrder` then `payment_source` on
  `AuthorizeOrder`" pattern this sheet used has no equivalently-named blessing beyond the one remarks
  sentence above. Both are documented as legal; only one is a *named* pattern. Worth trying
  `OrderRequest.PaymentSource` populated directly on `CreateOrder` (leaving `AuthorizeOrder`'s `body`
  null) as a diagnostic experiment — **not asserted as the fix**, since nothing in source ties
  `TRANSACTION_REFUSED` to this distinction either.
- No other `CardRequest`/`CardAttributes`/`CardStoredCredential`/`PurchaseUnitRequest` field is flagged by
  source as required or decline-relevant for a plain customer-initiated, one-time direct-card payment;
  `CardStoredCredential` (`payment_initiator`/`payment_type`/`usage`) is entirely optional and undocumented
  as a requirement here. The one source-documented lever that **can** deliberately force a sandbox
  decline is the `payPalMockResponse` header itself ("configures the sandbox into a negative testing
  state for transactions that include the merchant," `Api/Orders.cs` doc-comment on both operations) —
  worth confirming it is genuinely `null` end-to-end and not defaulting/being set anywhere.
- **Plain assessment, as requested:** the map/source cannot explain `TRANSACTION_REFUSED` — this is
  UNVERIFIED beyond source and is not, on current evidence, an SDK integration bug (the request shape
  matches what source sanctions). The most source-consistent hypothesis, given that vaulting the same
  card via `Vault.CreatePaymentToken` succeeded on this account while direct authorization via
  `Orders.AuthorizeOrder` did not, is an **external account/processor-configuration gap**: vaulting only
  stores card data (no processor authorization is requested), while `AuthorizeOrder` asks a processor to
  place a real hold — these are different capabilities/entitlements on a merchant account, and it is
  plausible this sandbox account has one enabled without the other. Confirming that requires PayPal's own
  sandbox account dashboard/support, not this SDK's map or source — treat as a blocked-pending-external-
  confirmation item, not something to keep guessing at via request-shape changes.

**Vaulting (`CreatePaymentToken`) does not carry an equivalent `ExperienceContext`/redirect field
on `PaymentTokenRequestCard`** in this SDK's request shape, so the direct-card vaulting path in
§2.3 is not flagged as needing a browser step by the map/source — but the same underlying
issuer-SCA risk that affects `AuthorizeOrder` is not something a card-network integration can
categorically rule out either; treat this as lower-risk than §2.2/§2.4 but not proven redirect-free.

**Other assumptions:**
- `PayPal:Environment` config key does not map onto the SDK's own `ServerEnvironment` enum (which
  has only one member, `Sandbox`) — it must instead select which default `BaseUrl` this
  integration passes to `options.Server.Default.Sandbox.BaseUrl`
  (`https://api-m.sandbox.paypal.com` vs `https://api-m.paypal.com`), with `PayPal:BaseUrl`
  applied on top as an explicit override when set. `options.Environment` itself should always be
  left at `ServerEnvironment.Sandbox` (its only legal value in this SDK build).
- Exact wire-level `Issue`/`Description` string values inside `Error.Details`/`Error1.Details`
  (e.g., whatever PayPal's live sandbox actually sends for "authorization already expired" or
  "beyond reauthorization window") are UNVERIFIED — they are untyped `string` fields in this SDK,
  not enums, and their literal content can only be confirmed by a live error response. §2.6/§2.7
  give the defensive-coding fallback (best-effort match, generic "operator must be told" default).
- Whether PayPal's live API itself enforces a maximum refundable amount (rejecting an over-refund)
  is UNVERIFIED from the SDK/source alone; §2.9 gives the defensive tracking directive.
- Whether a wide `startDate`–`endDate` span on `SearchTransactions` is capped by PayPal is
  UNVERIFIED; §2.10 gives the defensive handling (read `RawError.StatusCode`/`ReadAsString()`).
- `PaymentTokenRequestCard`'s field-level validation regexes were not independently re-verified
  against source (only `CardRequest`'s were, since that model was needed for §2.2/§2.4) — if a
  save-card call fails validation unexpectedly, re-check `Models/PaymentTokenRequestCard.cs`
  before assuming the §2.2 constraints apply identically.

No other blockers identified.
