# PayPal .NET SDK plan — eShopOnWeb (payments + saved cards)

NuGet: `AsadAli.Checkout.Sdk` (install version-less). Root namespace: `PayPalServerSdk`. Client: `PayPalServerSdkClient`. Sandbox only. Direct card processing (no browser approval round-trip).

---

## Scope & sequence

| Step | What | Operations (`client.{Controller}.{Method}`) |
| ---: | --- | --- |
| 1 | Bind `PayPal:` config (`ClientId`, `ClientSecret`, `Environment`, `Currency`, `BaseUrl`). Map to SDK options. | — |
| 2 | DI-register `PayPalServerSdkClient` in PublicApi. | constructor / `AddPayPalServerSdkClient` |
| 3 | **Pay (one-off card)** — AUTHORIZE (hold) order total. | `Orders.CreateOrder` then `Orders.AuthorizeOrder`. Read-back: `Orders.GetOrder` |
| 4 | **Pay (saved card)** — AUTHORIZE with vault token, not PAN. | same as step 3; `CardRequest.VaultId` instead of `Number`/`SecurityCode` |
| 5 | **Fulfil** — if hold expired/stale, RENEW; then CAPTURE. Show captured amount, PayPal fee, net. | `Payments.GetAuthorizedPayment` → (optional) `Payments.ReauthorizePayment` → `Payments.CaptureAuthorizedPayment` → `Payments.GetCapturedPayment` |
| 6 | **Cancel-before-fulfilment** — VOID the hold. | `Payments.VoidPayment` |
| 7 | **Refund** full or partial; never beyond captured; caller idempotency key. | `Payments.RefundCapturedPayment`; remaining check via `Payments.GetCapturedPayment` / `Payments.GetRefund` |
| 8 | Persist PayPal ids + statuses (order, auth, capture, refunds, vault customer/token). | all of the above |
| 9 | **Reconcile** — list PayPal transactions for ISO-8601 from/to; cover the **whole** range (window + page loops). | `TransactionSearch.SearchTransactions` |
| 10 | **Vault SAVE** a card for a signed-in shopper (safe descriptor only). | `Vault.CreatePaymentToken` |
| 11 | **Vault LIST** caller’s saved cards. | `Vault.ListCustomerPaymentTokens` (pages); optional `Vault.GetPaymentToken` |
| 12 | **Vault DELETE**. | `Vault.DeletePaymentToken` |

**Not used (wrong intent or browser/setup flow):** `Orders.CaptureOrder` (CAPTURE-intent sale, not an auth hold), `Orders.ConfirmOrder`, `Orders.PatchOrder`, tracking ops, `Vault.CreateSetupToken` / `GetSetupToken` (setup-token + `ReturnUrl` is a browser path), `Subscriptions.*`, `TransactionSearch.SearchBalances`.

**3DS / browser challenge:** the SDK does **not** require a shopper-in-browser challenge for this path (`CardExperienceContext.ReturnUrl` / `CancelUrl` are optional; `PaymentTokenRequestCard` has no return URL). If a live `Order.Status` is `PayerActionRequired`, **STOP** — that is a runtime GAP; do not add a redirect/approve flow. Sandbox Visa `4111111111111111` is expected not to challenge.

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

**Unions:** this SDK has **0** `OneOf` / `AnyOf` types (`map/models/unions.md`). Payment sources are ordinary records with optional properties (`Card`, `Paypal`, …) — set the one you use, leave the rest unset. No factory / `TryGet…` on models.

**No-throw `…Result` variants:** absent on every operation. All calls throw.

**`prefer`:** default `"return=minimal"` returns only `id`, `status`, and HATEOAS `links`. Pass `prefer: "return=representation"` on create/authorize/capture/reauthorize/refund/void **or** follow with the matching GET so authorization ids, capture breakdown (fee/net), and refund amounts are present.

### Client construction / auth / server node

| Fact | Value | Source |
| --- | --- | --- |
| Package | `AsadAli.Checkout.Sdk` | `sdk-map.md` |
| Client | `PayPalServerSdk.PayPalServerSdkClient` | `PayPalServerSdkClient.cs` |
| Constructor | `PayPalServerSdkClient(System.Net.Http.HttpClient httpClient, PayPalServerSdk.PayPalServerSdkClientOptions options)` | `sdk-map.md` |
| DI | `PayPalServerSdk.ServiceCollectionExtensions.AddPayPalServerSdkClient(this Microsoft.Extensions.DependencyInjection.IServiceCollection, System.Action<PayPalServerSdk.PayPalServerSdkClientOptions>? configure = null)` — registers a **singleton** client; obtains `HttpClient` via `IHttpClientFactory.CreateClient()` | `ServiceCollectionExtensions.cs` |
| Options | `PayPalServerSdk.PayPalServerSdkClientOptions`: `Environment` (`PayPalServerSdk.Servers.ServerEnvironment`), `Retry` (`PayPalServerSdk.Core.Configuration.RetryOptions`), `Logging` (`PayPalServerSdk.Core.Configuration.LoggingOptions`), `Server` (`PayPalServerSdk.ServerOptions`), `Oauth2` (`PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials?`), `Oauth2TokenStrategy` (`PayPalServerSdk.Core.Authentication.OAuth2.IOAuth2TokenStrategy<OAuth2ClientCredentials>?`) | `sdk-map.md`, `PayPalServerSdkClientOptions.cs` |
| Auth scheme | OAuth2 **client credentials**. Set `options.Oauth2 = new OAuth2ClientCredentials { ClientId = …, ClientSecret = …, Scope = optional }`. Token POST is `server.Default("/v1/oauth2/token")` with HTTP Basic `clientId:clientSecret` and form `grant_type=client_credentials`. Bearer token applied to API calls. | `sdk-map.md` *Servers & auth*; `OAuth2ClientCredentials.cs`; `AuthSchemes.cs` |
| Environment | **Only** `PayPalServerSdk.Servers.ServerEnvironment.Sandbox` (wire `"Sandbox"`). `Default()` is Sandbox. **No Production member.** Config `PayPal:Environment` must resolve to this member. | `Servers/ServerEnvironment.cs` |
| Default BaseUrl | `https://api-m.sandbox.paypal.com` | `Servers/DefaultOptions.cs` |
| **BaseUrl override (verbatim, every call including token)** | If `PayPal:BaseUrl` is set, assign it **verbatim** to `options.Server.Default.Sandbox.BaseUrl`. Types: `PayPalServerSdk.ServerOptions.Default` → `PayPalServerSdk.Servers.DefaultOptions.Sandbox` → `SandboxOptions.BaseUrl`. `Server.Default(path)` uses that BaseUrl for **Orders, Payments, Vault, TransactionSearch, and `/v1/oauth2/token`**. Paths are joined as `{BaseUrl.TrimEnd('/')}/{path.TrimStart('/')}`. Do **not** put the override on `HttpClient.BaseAddress` — the SDK builds absolute URIs from `ServerOptions`, not from `HttpClient.BaseAddress`. | `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Server.cs`, `AuthSchemes.cs`, `TemplateParamsFactory.cs` |
| Config section | `PayPal:` keys `ClientId`, `ClientSecret`, `Environment`, `Currency`, `BaseUrl` (optional). **Never hard-code.** Currency is **not** an SDK client option — it is `Money.CurrencyCode` / `AmountWithBreakdown.CurrencyCode` on each request. | this brief + models |
| Per-call options | `PayPalServerSdk.Core.RequestOptions` — only `LogLevel`. **Not** how you pass `PayPal-Request-Id` (that is a method parameter). Pass `requestOptions: null` unless overriding log level. | `Core/RequestOptions.cs` |

⚠ Step 2 (client registration) — `HttpClient` / handler pipeline lifetime and whether the SDK wrapper is singleton vs transient are not visible from the constructor. **MUST load `dotnet-client-initialization`** before writing DI.

⚠ Step 2 (auth) — credentials must be supplied before the client is used; load secrets from configuration. **MUST load `dotnet-authentication`**.

⚠ Step 2 (BaseUrl / retries / timeouts) — SDK retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; a transport failure can retry **POST** (authorize/capture/refund/vault) so a write can execute more than once. **MUST load `dotnet-configuration-resilience`** before wiring the client.

⚠ Step 2 (logging) — enabling request-body logging can persist PAN / `security_code` (redaction defaults do not list card fields). Never log `CardRequest` / `PaymentTokenRequestCard`. **MUST load `dotnet-configuration-resilience`**.

### Enums used in code (`PayPalServerSdk.Models.Enums` — `StringEnum<T>`, **not** C# enums)

Construct with the static member (e.g. `CheckoutPaymentIntent.Authorize`) or `Type.FromValue("WIRE")`. Source: `map/models/enums.md`.

| Enum | Members (C# (wire)) | Where |
| --- | --- | --- |
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, **`Authorize (AUTHORIZE)`** | `OrderRequest.Intent` — **must be `Authorize`** for a hold |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` | `Order` / authorize response. `PayerActionRequired` ⇒ browser GAP (stop) |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` | hold |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` | capture; `PartiallyRefunded`/`Refunded` gate further refunds |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` | refund |
| `CardBrand` | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Discover (DISCOVER)`, `Amex (AMEX)`, … `Unknown (UNKNOWN)` | vault display |
| `OrdersCardVerificationMethod` | `ScaAlways (SCA_ALWAYS)`, `ScaWhenRequired (SCA_WHEN_REQUIRED)`, `_3DSecure (3D_SECURE)`, `AvsCvv (AVS_CVV)` | optional; `AvsCvv` requests AVS/CVV without SCA. Default on `CardVerification.Method` is `ScaWhenRequired` |
| `PaymentInitiator` | `Customer (CUSTOMER)`, `Merchant (MERCHANT)` | stored credential on vaulted pay |
| `StoredPaymentSourcePaymentType` | `OneTime (ONE_TIME)`, `Recurring (RECURRING)`, `Unscheduled (UNSCHEDULED)` | stored credential |
| `StoredPaymentSourceUsageType` | `First (FIRST)`, `Subsequent (SUBSEQUENT)`, `Derived (DERIVED)` | stored credential |
| `TokenType` | **`BillingAgreement (BILLING_AGREEMENT)` only** | `PaymentSource.Token` is **not** a vaulted card |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` | vault-from-setup-token only (out of scope) |
| `PaymentTokenStatus` | `Created`, `PayerActionRequired`, `Approved`, `Vaulted`, `Tokenized` | setup-token status (not used if we only `CreatePaymentToken`) |
| `AuthorizationIncompleteReason` | `PendingReview (PENDING_REVIEW)`, `DeclinedByRiskFraudFilters (DECLINED_BY_RISK_FRAUD_FILTERS)` | auth `StatusDetails` |
| `RefundIncompleteReason` | `Echeck (ECHECK)` | refund pending/failed |
| `DisbursementMode` | `Instant (INSTANT)`, `Delayed (DELAYED)` | capture default Instant |

### Shared request/response models (`PayPalServerSdk.Models`)

Fields are `CSharpName (wire_name): Type`. `!req` = C# `required`. Source: `map/models/records-1-Ac-Pa.md`, `records-2-Pa-Ve.md`.

| Record | Fields the integration sets or reads |
| --- | --- |
| `Money` | `CurrencyCode (currency_code): string !req` (3-char ISO-4217 from config), `Value (value): string !req` (decimal string, e.g. `"10.00"`; regex `^((-?[0-9]+)|(-?([0-9]+)?[.][0-9]+))$`). **Order total must match to the cent.** |
| `AmountWithBreakdown` | `CurrencyCode (currency_code): string !req`, `Value (value): string !req`, `Breakdown (breakdown): AmountBreakdown?` |
| `Address` | `AddressLine1 (address_line_1): string?`, `AddressLine2 (address_line_2): string?`, `AdminArea2 (admin_area_2): string?` (city), `AdminArea1 (admin_area_1): string?` (state), `PostalCode (postal_code): string?`, `CountryCode (country_code): string !req` (ISO-3166-1 alpha-2) |
| `Name` | `GivenName (given_name): string?`, `Surname (surname): string?` |
| `CardRequest` | **One-off card:** `Name (name)`, `Number (number)` (13–19 digits), `Expiry (expiry)` **ISO-8601 `YYYY-MM`**, `SecurityCode (security_code)` (3–4 digits), `BillingAddress (billing_address): Address?`. **Saved card:** `VaultId (vault_id)` = payment-token id; omit PAN/CVC. Optional `Attributes (attributes): CardAttributes?`, `StoredCredential (stored_credential): CardStoredCredential?`. Do not persist or log `Number`/`SecurityCode`. |
| `CardAttributes` | `Customer (customer): CardCustomerInformation?`, `Vault (vault): VaultInstructionBase?`, `Verification (verification): CardVerification?` (`Method` default `ScaWhenRequired`) |
| `CardStoredCredential` | `PaymentInitiator (payment_initiator) !req`, `PaymentType (payment_type) !req`, `Usage (usage)?` |
| `PaymentSource` | `Card (card): CardRequest?` — this is the direct-card / vault-id source. `Token (token): Token?` is billing-agreement only (`TokenType.BillingAgreement`) — **do not** use for saved cards. |
| `OrderRequest` | `Intent (intent): CheckoutPaymentIntent !req` = **`Authorize`**, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req`, `PaymentSource (payment_source): PaymentSource?`, `Payer (payer): Payer?`, `ApplicationContext (application_context): OrderApplicationContext?` |
| `PurchaseUnitRequest` | `Amount (amount): AmountWithBreakdown !req` (= order total), `CustomId (custom_id): string?` (**set to eShop order id** for recon), `InvoiceId (invoice_id): string?`, `ReferenceId (reference_id): string?`, `Description (description): string?` |
| `Order` / `OrderAuthorizeResponse` | `Id (id)`, `Status (status): OrderStatus?`, `Intent (intent)`, `PurchaseUnits (purchase_units)`, `PaymentSource (payment_source): PaymentSourceResponse?` (card response has `LastDigits`, `Brand`, `Expiry` — **no PAN**), `Links (links)` |
| `PurchaseUnit` | `Payments (payments): PaymentCollection?`, `Amount`, `CustomId`, `InvoiceId` |
| `PaymentCollection` | `Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?`, `Captures (captures): IReadOnlyList<OrdersCapture>?`, `Refunds (refunds): IReadOnlyList<Refund>?` |
| `Authorization` / `AuthorizationWithAdditionalData` / `PaymentAuthorization` | `Id (id)` **← persist as hold id**, `Status (status)`, `Amount (amount)`, `ExpirationTime (expiration_time)`, `CreateTime`, `UpdateTime`, `StatusDetails` |
| `OrderAuthorizeRequest` | `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` (`Card (card): CardRequest?`) — pass `null` body when card was already on `CreateOrder` |
| `CaptureRequest` | `Amount (amount): Money?` (omit for full remaining), `FinalCapture (final_capture): bool? = false`, `InvoiceId`, `NoteToPayer`, `SoftDescriptor` |
| `CapturedPayment` / `OrdersCapture` | `Id (id)` **← persist**, `Status (status): CaptureStatus?`, `Amount (amount)` **captured amount**, `SellerReceivableBreakdown (seller_receivable_breakdown)` **fee + net**, `CreateTime`, `UpdateTime` |
| `SellerReceivableBreakdown` | `GrossAmount (gross_amount): Money !req`, `PaypalFee (paypal_fee): Money?` **← fee**, `NetAmount (net_amount): Money?` **← merchant proceeds**, `PaypalFeeInReceivableCurrency`, `ReceivableAmount`, `ExchangeRate`, `PlatformFees` |
| `ReauthorizeRequest` | `Amount (amount): Money?` — **only** field this op supports |
| `RefundRequest` | Full refund: `body: null` or empty `RefundRequest`. Partial: `Amount (amount): Money` (must be ≤ captured − already refunded) |
| `Refund` | `Id (id)` **← persist**, `Status`, `Amount`, `SellerPayableBreakdown (seller_payable_breakdown)` (`TotalRefundedAmount (total_refunded_amount)`, `PaypalFee`, `NetAmount`, `GrossAmount`) |
| `PaymentTokenRequest` | `Customer (customer): Customer?` (`Id` = PayPal customer id if known, `MerchantCustomerId` = eShop user id), `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req` |
| `PaymentTokenRequestPaymentSource` | `Card (card): PaymentTokenRequestCard?` (`Name`, `Number`, `Expiry` `YYYY-MM`, `SecurityCode`, `BillingAddress`, `Brand`) |
| `PaymentTokenResponse` | `Id (id)` **← persist token id**, `Customer (customer): CustomerResponse?` (`Id` **← persist PayPal customer id for LIST**), `PaymentSource.Card`: `CardPaymentTokenEntity` — `LastDigits (last_digits)`, `Brand`, `Expiry`, `Name`, `Type` — **never PAN** |
| `CustomerVaultPaymentTokensResponse` | `PaymentTokens (payment_tokens)`, `TotalItems (total_items)`, `TotalPages (total_pages)`, `Customer`, `Links` |
| `SearchResponse` | `TransactionDetails (transaction_details)`, `StartDate`, `EndDate`, `Page (page)`, `TotalItems (total_items)`, `TotalPages (total_pages)`, `Links` |
| `TransactionDetails` | `TransactionInfo (transaction_info): TransactionInformation?` — `TransactionId`, `PaypalReferenceId`, `TransactionAmount`, `FeeAmount`, `TransactionStatus`, `InvoiceId`, `CustomField`, `TransactionInitiationDate`, … |
| `Error` (Orders/Payments Case A payload) | `Name (name) !req`, `Message (message) !req`, `DebugId (debug_id) !req`, `Details (details): IReadOnlyList<ErrorDetails>?` (`Issue !req`, `Description`, `Field`, `Value`), `Links` |
| `Error1` (Vault Case A payload) | same shape; `Details`: `ErrorDetails1`; `Links`: `ErrorLinkDescription` |
| `CardResponse` (safe card on order) | `LastDigits`, `Brand`, `Expiry`, `Type`, `AuthenticationResult` — no PAN |

---

### Operations

Nullable parameters **without a C# default must be passed explicitly** (`null` to skip). **MUST load `dotnet-calling-endpoints`** before the first call (named arguments; mis-bind risk).

#### Idempotency (`PayPal-Request-Id`)

Passed as parameter `payPalRequestId` → header `PayPal-Request-Id`. The SDK also sends a **fresh** `Idempotency-Key: Guid.NewGuid()` on write calls; that header is **not** caller-controlled. App-level idempotency = stable `payPalRequestId` **plus** persisted PayPal ids so a double-click does not start a second authorize/capture.

| Operation | `payPalRequestId`? | Notes |
| --- | --- | --- |
| `Orders.CreateOrder` | **yes** (must pass explicitly) | XML: **mandatory** for single-step create with payment source (card / vault_id). Stored **6 hours**. |
| `Orders.AuthorizeOrder` | **yes** | 6 hours |
| `Orders.GetOrder` | no | |
| `Payments.CaptureAuthorizedPayment` | **yes** | stored **45 days** |
| `Payments.ReauthorizePayment` | **yes** | 45 days |
| `Payments.RefundCapturedPayment` | **yes** — **caller-supplied refund idempotency key goes here** | 45 days |
| `Payments.VoidPayment` | **yes** | 45 days |
| `Payments.GetAuthorizedPayment` / `GetCapturedPayment` / `GetRefund` | no | |
| `Vault.CreatePaymentToken` | **yes** | 3 hours |
| `Vault.ListCustomerPaymentTokens` / `GetPaymentToken` / `DeletePaymentToken` | no | |
| `TransactionSearch.SearchTransactions` | no | |

---

#### `Orders.CreateOrder` — create AUTH order with card or vault id

- **HTTP:** `POST /v2/checkout/orders` · Accessor: `client.Orders` · `PayPalServerSdk.Api.Orders`
- **Signature:** `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly:** `payPalMockResponse`, `payPalRequestId`, `payPalPartnerAttributionId`, `payPalClientMetadataId`, `payPalAuthAssertion` (null to skip). `body` is required (non-nullable).
- **Returns:** `PayPalServerSdk.Models.Order` (not an envelope wrapper — the order **is** the response).
- **Error:** Case A `PayPalServerSdk.Core.Exceptions.SdkException<PayPalServerSdk.Errors.CreateOrderError>` — `TryGetError(out PayPalServerSdk.Models.Error)` [400, 401, 422] · `TryGetRawError(out PayPalServerSdk.Core.ErrorResponse.RawError)` fallback.
- **Pagination:** none
- **Auth intent:** `body.Intent = CheckoutPaymentIntent.Authorize` (wire `AUTHORIZE`). Do **not** use `Capture`.
- **Raw card:** `body.PaymentSource = new PaymentSource { Card = new CardRequest { Number, Expiry: "YYYY-MM", SecurityCode, Name, BillingAddress } }`.
- **Vaulted card:** `Card = new CardRequest { VaultId = persistedPaymentTokenId }` (no PAN). Optional `StoredCredential` with `PaymentInitiator.Customer`, `PaymentType.OneTime` or `Unscheduled`, `Usage.Subsequent`.
- **Amount:** one `PurchaseUnitRequest` whose `Amount.Value`/`CurrencyCode` equal the eShop order total to the cent; `CustomId` = eShop order id.
- **Map:** `operations/Orders.md`, `records-1-Ac-Pa.md`

#### `Orders.AuthorizeOrder` — place the hold

- **HTTP:** `POST /v2/checkout/orders/{id}/authorize`
- **Signature:** `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly:** `payPalMockResponse` … `body`.
- **Returns:** `PayPalServerSdk.Models.OrderAuthorizeResponse`
- **Hold id:** `PurchaseUnits[0].Payments.Authorizations[0].Id` (requires `prefer: "return=representation"` or subsequent `GetOrder`). Persist `Id` + `Status` + `ExpirationTime`.
- **Error:** `SdkException<AuthorizeOrderError>` — `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError`.
- **Notes:** buyer approve **or** a valid `payment_source` on create/authorize. Direct card supplies `payment_source` — no `rel:approve` redirect.
- **Map:** `operations/Orders.md`

#### `Orders.GetOrder` — read-back for persistence

- **HTTP:** `GET /v2/checkout/orders/{id}`
- **Signature:** `GetOrder(string id, string? fields, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly:** `fields`, `payPalMockResponse`, `payPalAuthAssertion`. Query `fields` ← `fields` (comma-separated; documented filter: `payment_source`).
- **Returns:** `Order`
- **Error:** `SdkException<GetOrderError>` — `TryGetError(out Error)` [401, 404] · `TryGetRawError`.
- **Map:** `operations/Orders.md`

#### `Payments.GetAuthorizedPayment` — inspect hold before capture / renew

- **HTTP:** `GET /v2/payments/authorizations/{authorization_id}`
- **Signature:** `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns:** `PayPalServerSdk.Models.PaymentAuthorization` — read `Status`, `ExpirationTime`, `Amount`.
- **Error:** `SdkException<GetAuthorizedPaymentError>` — `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`.
- **Map:** `operations/Payments.md`

#### `Payments.ReauthorizePayment` — RENEW a stale hold

- **HTTP:** `POST /v2/payments/authorizations/{authorization_id}/reauthorize`
- **Signature:** `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly:** `payPalRequestId`, `payPalAuthAssertion`, `body`.
- **Returns:** `PaymentAuthorization` — **new** `Id` / `ExpirationTime` (persist; subsequent capture uses the **new** id).
- **Body:** `ReauthorizeRequest { Amount = original Money }` (operation supports **only** `amount`).
- **Error:** `SdkException<ReauthorizePaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent` [500] · `TryGetRawError`. Surface `Error.Name`, `Message`, `DebugId`, `Details[].Issue` as **operator-actionable** when renew is refused. After ~30 days the notes say you must **create a new authorized payment** (`CreateOrder`+`AuthorizeOrder`), not reauthorize — that failure is operator-actionable, not a missing SDK method.
- **Map disagreement (UNVERIFIED live rule):** operation notes (`Payments.md`) say multiple re-auths are allowed in days 4–29; `ReauthorizeRequest` summary says you can reauthorize **only once**. Implement: try `ReauthorizePayment`; on typed 422, do not invent another API — return the PayPal `Issue`/`Message`/`DebugId` to the operator.
- **Map:** `operations/Payments.md`, `records-2-Pa-Ve.md`

#### `Payments.CaptureAuthorizedPayment` — take the money at fulfilment

- **HTTP:** `POST /v2/payments/authorizations/{authorization_id}/capture`
- **Signature:** `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly:** `payPalMockResponse` … `body`. Full capture: `body: null` or `new CaptureRequest { FinalCapture = true }`.
- **Returns:** `PayPalServerSdk.Models.CapturedPayment`
- **Fee / net / captured amount:** `CapturedPayment.Amount` (captured); `SellerReceivableBreakdown.GrossAmount`, `.PaypalFee`, `.NetAmount`. If `prefer` was minimal, call `GetCapturedPayment`.
- **Error:** `SdkException<CaptureAuthorizedPaymentError>` — `TryGetError(out Error)` [400, 401, 403, 404, **409**, 422] · `TryGetNoContent` [500] · `TryGetRawError`. **409** = conflict (treat as “already captured”: GET current capture, do not capture twice).
- **Map:** `operations/Payments.md`, `records-1-Ac-Pa.md`

#### `Payments.GetCapturedPayment` — display captured amount, fee, net; remaining refundable

- **HTTP:** `GET /v2/payments/captures/{capture_id}`
- **Signature:** `GetCapturedPayment(string captureId, string? payPalMockResponse, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns:** `CapturedPayment` (same breakdown as above). `Status` `Refunded` / `PartiallyRefunded` gates refunds.
- **Error:** `SdkException<GetCapturedPaymentError>` — `TryGetError` [401, 403, 404] · `TryGetNoContent` [500] · `TryGetRawError`.
- **Map:** `operations/Payments.md`

#### `Payments.VoidPayment` — release hold on cancel-before-fulfilment

- **HTTP:** `POST /v2/payments/authorizations/{authorization_id}/void`
- **Signature:** `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly:** `payPalMockResponse`, `payPalAuthAssertion`, `payPalRequestId`. **No body.** Cannot void a fully captured auth.
- **Returns:** `PaymentAuthorization` (`Status` → `Voided`)
- **Error:** `SdkException<VoidPaymentError>` — `TryGetError` [401, 403, 404, 409, 422] · `TryGetNoContent` [500] · `TryGetRawError`.
- **Map:** `operations/Payments.md`

#### `Payments.RefundCapturedPayment` — full or partial refund

- **HTTP:** `POST /v2/payments/captures/{capture_id}/refund`
- **Signature:** `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly:** `payPalMockResponse` … `body`.
- **Idempotency:** caller key → `payPalRequestId` (required by this brief even though the SDK types it nullable).
- **Full:** `body: null`. **Partial:** `body: new RefundRequest { Amount = new Money { CurrencyCode, Value } }`.
- **Cap:** before calling, `GetCapturedPayment`; refuse if `Status` is `Refunded`, or if partial amount > captured `Amount` minus already-refunded (`SellerPayableBreakdown.TotalRefundedAmount` on prior refunds / capture status `PartiallyRefunded`). Persist each refund `Id`, `Amount`, `Status`.
- **Returns:** `PayPalServerSdk.Models.Refund`
- **Error:** `SdkException<RefundCapturedPaymentError>` — `TryGetError` [400, 401, 403, 404, 409, 422] · `TryGetNoContent` [500] · `TryGetRawError`.
- **Map:** `operations/Payments.md`, `records-2-Pa-Ve.md`

#### `Payments.GetRefund`

- **HTTP:** `GET /v2/payments/refunds/{refund_id}`
- **Signature:** `GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns:** `Refund`
- **Error:** `SdkException<GetRefundError>` — `TryGetError` [401, 403, 404] · `TryGetNoContent` [500] · `TryGetRawError`.
- **Map:** `operations/Payments.md`

#### `Vault.CreatePaymentToken` — SAVE card (no PAN stored in app)

- **HTTP:** `POST /v3/vault/payment-tokens` · Accessor: `client.Vault` · `PayPalServerSdk.Api.Vault`
- **Signature:** `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly:** `payPalRequestId`.
- **Returns:** `PaymentTokenResponse` — persist `Id` (token), `Customer.Id` (PayPal customer id for LIST), display `PaymentSource.Card.LastDigits` / `Brand` / `Expiry` only.
- **Error:** `SdkException<CreatePaymentTokenError>` — **`TryGetError1(out Error1)`** [400, 403, 404, 422, 500] · `TryGetRawError`. (Accessor name is `TryGetError1`, not `TryGetError`.)
- **Map:** `operations/Vault.md`, `records-2-Pa-Ve.md`

#### `Vault.ListCustomerPaymentTokens` — LIST saved cards

- **HTTP:** `GET /v3/vault/payment-tokens`
- **Signature:** `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Query:** `customer_id` ← `customerId` (**PayPal** customer id from vault create, not the eShop user id), `page_size`, `page`, `total_required`.
- **Returns:** `CustomerVaultPaymentTokensResponse`
- **Pagination:** SDK has **no auto-pager**. Loop `page = 1 .. TotalPages` (set `totalRequired: true` on first call so `TotalPages` is populated). Default `pageSize` is 5.
- **Error:** `SdkException<ListCustomerPaymentTokensError>` — `TryGetError1(out Error1)` [400, 403, 500] · `TryGetRawError`.
- **Map:** `operations/Vault.md`

#### `Vault.GetPaymentToken`

- **HTTP:** `GET /v3/vault/payment-tokens/{id}`
- **Signature:** `GetPaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns:** `PaymentTokenResponse` (safe card entity)
- **Error:** `SdkException<GetPaymentTokenError>` — `TryGetError1` [403, 404, 422, 500] · `TryGetRawError`.
- **Map:** `operations/Vault.md`

#### `Vault.DeletePaymentToken` — unvault

- **HTTP:** `DELETE /v3/vault/payment-tokens/{id}`
- **Signature:** `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns:** `void` (`Task`)
- **Error:** `SdkException<DeletePaymentTokenError>` — `TryGetError1` [400, 403, 500] · `TryGetRawError`.
- **Map:** `operations/Vault.md`

#### `TransactionSearch.SearchTransactions` — RECONCILIATION

- **HTTP:** `GET /v1/reporting/transactions` · Accessor: `client.TransactionSearch` · `PayPalServerSdk.Api.TransactionSearch`
- **Signature:** `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Must pass explicitly:** the 8 nullable filters `transactionId` … `terminalId` (`null` to skip).
- **Dates:** RFC-3339 / ISO-8601; **seconds required**. **Maximum window per call: 31 days** (XML on `endDate`). For a longer eShop from/to, **chunk into ≤31-day windows**, and for each window page through results. Lag: executed transactions can take **up to three hours** to appear; history up to **three years**.
- **Whole range:** map pagination is **not** auto (`Pagination: none (only page, no perPage)`). Loop `page` from 1 while `page <= TotalPages` (`SearchResponse.TotalPages` / `TotalItems`). Default `pageSize` 100.
- **Match eShop orders:** set `PurchaseUnit.CustomId` / `InvoiceId` at authorize time; align `TransactionInformation.CustomField` / `InvoiceId` / `TransactionId` / amounts. Use `fields: "all"` or `transaction_info,payer_info,cart_info` when you need more than the default `transaction_info`.
- **Returns:** `PayPalServerSdk.Models.SearchResponse`
- **Error:** **Case B** `SdkException<PayPalServerSdk.Core.ErrorResponse.RawError>` — **this is the only Case B operation in the SDK**. Read `ex.Error.StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`. Optional deserialize to `PayPalServerSdk.Models.SearchError` / `DefaultError` (same payload shape) — do not assume typed `TryGet…`.
- **Map:** `operations/TransactionSearch.md`, `records-2-Pa-Ve.md`

---

### Card payment input (sandbox)

| Field | How |
| --- | --- |
| Number | `CardRequest.Number` / `PaymentTokenRequestCard.Number` = `4111111111111111` |
| Expiry | `YYYY-MM` any future month (`CardRequest.Expiry` regex `^[0-9]{4}-(0[1-9]\|1[0-2])$`) |
| CVC | `SecurityCode` any 3–4 digits |
| Name | `CardRequest.Name` |
| Billing address | `Address` with required `CountryCode` |

PCI: passing PAN/CVV via the API is SAQ-D (XML on `CardRequest`). App DB and logs store **only** PayPal ids + last digits/brand/expiry.

---

### Persist (PayPal-owned state)

| Entity | Persist |
| --- | --- |
| Order | `Order.Id`, `Order.Status`, `Intent` |
| Hold | `PaymentAuthorization.Id`, `Status`, `ExpirationTime`, `Amount` |
| Capture | `CapturedPayment.Id`, `Status`, `Amount`, `SellerReceivableBreakdown` (gross/fee/net) |
| Refunds | list of `Refund.Id`, `Amount`, `Status`; running refunded total |
| Idempotency | last `payPalRequestId` per business action (authorize, capture, refund, void, vault) |
| Vault | PayPal `Customer.Id`, each `PaymentTokenResponse.Id`, `LastDigits`, `Brand`, `Expiry` — **never** PAN/CVC |

App-level double-click: if a hold id already exists for the eShop order, skip `CreateOrder`/`AuthorizeOrder`; if a capture id exists, skip capture (optionally GET to refresh). Combine with `payPalRequestId`.

---

### Error / exception types that reach catch blocks

`PayPalServerSdk.Core.Exceptions.SdkException<TError>` — `.Error` is `TError`. **No** HTTP status on the exception itself.

| Case | `TError` | How to read |
| --- | --- | --- |
| A (Orders/Payments) | `{Operation}Error : ApiError` | `TryGetError(out Error)` then `Name`/`Message`/`DebugId`/`Details`. `TryGetNoContent(out RawError)` on many Payments 500s. `TryGetRawError` fallback (`StatusCode`, `ReadAsString()`). |
| A (Vault) | `{Operation}Error` | **`TryGetError1(out Error1)`** (not `TryGetError`). |
| B (SearchTransactions only) | `RawError` | `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` |

`TryGetRawError` is **not** a catch-all on typed errors for the statuses that already mapped to `TryGetError` / `TryGetError1`.

⚠ Error boundary — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`**.

⚠ Error boundary — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`**.

⚠ Models — records are `init`/`required`; enums are `StringEnum<T>`; unmodeled JSON is dropped. **MUST load `dotnet-models`**.

⚠ Tests — the `HttpClient` constructor argument is the test seam. **MUST load `dotnet-testing`**.

---

## Trap notes

⚠ Step 2 (client registration) — the constructor does not tell you how to own `HttpClient` vs the SDK wrapper lifetime. **MUST load `dotnet-client-initialization`**.

⚠ Step 2 (auth) — scheme/property names and when credentials must be present are not a copy-paste from the options type alone. **MUST load `dotnet-authentication`**.

⚠ Step 2 (resilience / BaseUrl) — retry/timeout options do **not** bound a whole call and are **not** the `HttpClient` timeout; POST transport retries affect whether a failed write can be re-sent. **MUST load `dotnet-configuration-resilience`**.

⚠ Steps 3–12 (calls) — optional parameters without defaults must be passed by name; positional calls mis-bind. **MUST load `dotnet-calling-endpoints`**.

⚠ Steps 3–12 (models) — `required`/`init`, `StringEnum<T>`, optional card fields vs unions. **MUST load `dotnet-models`**.

⚠ All steps (errors) — Case A vs Case B, `TryGetError` vs `TryGetError1` vs `RawError`, and both `JsonException` directions above. **MUST load `dotnet-error-handling`**.

⚠ Tests — seam and what to assert. **MUST load `dotnet-testing`**.

---

## REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
| --- | --- |
| `dotnet-client-initialization` | Step 2 — DI, `HttpClient` ownership, builder/options |
| `dotnet-authentication` | Step 2 — `Oauth2` client-credentials wiring |
| `dotnet-calling-endpoints` | Steps 3–12 — named args, must-pass nulls, `ct:` |
| `dotnet-models` | Steps 3–12 — records, `StringEnum<T>`, request payloads |
| `dotnet-error-handling` | All steps — catch ladder, Case A/B, both `JsonException` hazards |
| `dotnet-configuration-resilience` | Step 2 + step 9 — retries/timeouts, BaseUrl, pagination loops, logging/PAN |
| `dotnet-testing` | Tests for the integration layer |

---

## Assumptions & Blockers

**Assumptions**

- PublicApi is the composition root; currency on every `Money`/`AmountWithBreakdown` comes from `PayPal:Currency`.
- One purchase unit per eShop order; `CustomId`/`InvoiceId` carry the eShop order id for reconciliation.
- Direct card path: `CreateOrder` (intent `Authorize` + `payment_source.card`) then `AuthorizeOrder`; capture/void/refund/reauthorize go through **Payments**, not `Orders.CaptureOrder`.
- Saved-card pay uses `CardRequest.VaultId`, not `PaymentSource.Token` (`TokenType` has only `BillingAgreement`).
- Vault LIST uses the **PayPal** customer id returned on `CreatePaymentToken`, not the eShop user id (`MerchantCustomerId` is for PayPal’s records).
- Sandbox Visa `4111111111111111` will not return `OrderStatus.PayerActionRequired`. If it does, that is a GAP (stop).
- `CreatePaymentToken` with raw card is sufficient to vault without `CreateSetupToken` / browser `ReturnUrl`.

**Blockers / GAPs (do not invent APIs)**

- **No Production environment** in this SDK — only `ServerEnvironment.Sandbox`. If `PayPal:Environment` is anything else, fail configuration; do not invent a live host.
- **No union accessors** — not a gap for card/vault (optional properties).
- **No auto-pagination helper** — not a gap: loop `SearchResponse.TotalPages` and `CustomerVaultPaymentTokensResponse.TotalPages`; chunk transaction search to **31-day** windows.
- **Browser/3DS challenge not implemented on purpose.** SDK *can* carry `CardExperienceContext.ReturnUrl`/`CancelUrl` and setup-token vault UX; using them would be a shopper-in-browser round-trip, which this integration **must not** add. If PayPal returns `PAYER_ACTION_REQUIRED`, that is a **GAP**.
- **Vault “Available in the US only”** (XML on `PayPalServerSdkClient`) — if the merchant account is not vault-eligible, SAVE/LIST/DELETE/pay-with-`vault_id` will fail at the provider; there is no alternate SDK API.
- **`Customer.Id` after vault create is nullable on the model.** If live create returns a token but no customer id, LIST cannot be called (`customerId` is a required string). Treat missing `Customer.Id` as operator-visible failure. UNVERIFIED whether sandbox always returns it.
- Reauthorize once-vs-many: map pages disagree; live rule UNVERIFIED. See ReauthorizePayment row.

**Not gaps**

- BaseUrl override **is** supported (`Server.Default.Sandbox.BaseUrl`) and **does** apply to the token request.
- Fee/net **are** on `SellerReceivableBreakdown` (`PaypalFee`, `NetAmount`, `GrossAmount`).
- Idempotency keys **are** `payPalRequestId` → `PayPal-Request-Id` on the write operations listed above.
