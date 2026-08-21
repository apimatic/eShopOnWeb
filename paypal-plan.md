# PayPal .NET SDK — Integration Plan & Contract Sheet

SDK: `AsadAli.Checkout.Sdk` (install version-less) · root namespace `PayPalServerSdk` · client `PayPalServerSdkClient` · target project `src/PublicApi`. Map provenance: tag `v1.0.1`, source commit `9653d18`. Every row cites the map page it came from.

All operations are **throw-based** (no `…Result` no-throw variants exist). 39 of 40 ops are Case A (typed `{Op}Error`), 1 is Case B (`RawError`) — `TransactionSearch.SearchTransactions`.

---

## 1. Scope & sequence

1. **Client & DI setup** — register `PayPalServerSdkClient` in `src/PublicApi` via `AddPayPalServerSdkClient`, binding `PayPal:*` config. Wire OAuth2 credentials + optional base-URL override (Steps below, `sdk-map.md` *Getting a client* / *Servers & auth*).
2. **Flow 1 — order lifecycle**: CreateOrder(intent=AUTHORIZE, raw card) → read/obtain authorization → CaptureAuthorizedPayment → (or) VoidPayment / ReauthorizePayment → RefundCapturedPayment. Ops: `client.Orders.CreateOrder`, `client.Orders.AuthorizeOrder`, `client.Payments.CaptureAuthorizedPayment`, `client.Payments.ReauthorizePayment`, `client.Payments.VoidPayment`, `client.Payments.RefundCapturedPayment`.
3. **Flow 2 — vault**: CreatePaymentToken (raw card, direct) → pay with vaulted id via `CardRequest.VaultId` → DeletePaymentToken. Ops: `client.Vault.CreatePaymentToken`, `client.Vault.DeletePaymentToken` (+ `CreateSetupToken` as the two-step alternative).
4. **Reconciliation**: `client.TransactionSearch.SearchTransactions` over `[from,to]`, paging to `TotalPages`.
5. **Error boundary + tests** around all of the above.

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### 2a. Namespaces (add a `using` per kind — child namespaces do NOT import transitively)

| Kind | Namespace |
|---|---|
| Client, `PayPalServerSdkClientOptions`, `ServerOptions`, DI extension | `PayPalServerSdk` |
| `ServerEnvironment`, `DefaultOptions`/`SandboxOptions` | `PayPalServerSdk.Servers` |
| `OAuth2ClientCredentials` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` |
| Controllers (`Orders`, `Payments`, `Vault`, `TransactionSearch`) | `PayPalServerSdk.Api` |
| All request/response records (`OrderRequest`, `CardRequest`, `Money`, `CapturedPayment`, `Refund`, error payloads `Error`/`Error1`/`DefaultError`, …) | `PayPalServerSdk.Models` |
| Enums (`CheckoutPaymentIntent`, `AuthorizationStatus`, `CaptureStatus`, `RefundStatus`, `CardBrand`, …) | `PayPalServerSdk.Models.Enums` |
| Typed error classes (`CreateOrderError`, `RefundCapturedPaymentError`, …) | `PayPalServerSdk.Errors` |
| `SdkException<T>` | `PayPalServerSdk.Core.Exceptions` |
| `RawError` | `PayPalServerSdk.Core.ErrorResponse` |

### 2b. Operations (map: `operations/Orders.md`, `operations/Payments.md`, `operations/Vault.md`, `operations/TransactionSearch.md`)

Nullable-no-default params **must be passed explicitly** (pass `null` to skip). Idempotency header = the `payPalRequestId` param (wire `PayPal-Request-Id`).

| # | Op (`client.X.Y`) | Signature (params in order) | Request body model | Returns → fields the integration reads | Error case + accessor + payload | Idempotency param |
|---|---|---|---|---|---|---|
| 1 | `Orders.CreateOrder` | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` | `OrderRequest` (required) | `Order` → `Id`, `Status (OrderStatus)`, `PurchaseUnits[].Payments.Authorizations[].Id/Status` | Case A `SdkException<CreateOrderError>` · `TryGetError(out Error)` [400,401,422] · `TryGetRawError(out RawError)` | `payPalRequestId` |
| 2 | `Orders.AuthorizeOrder` | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` | `OrderAuthorizeRequest?` | `OrderAuthorizeResponse` → `Id`, `Status`, `PurchaseUnits[].Payments.Authorizations[].Id/Status` | Case A `SdkException<AuthorizeOrderError>` · `TryGetError(out Error)` [400,401,403,404,422,500] · `TryGetRawError` | `payPalRequestId` |
| 4 | `Payments.CaptureAuthorizedPayment` | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` | `CaptureRequest?` (null = capture full authorized amount) | `CapturedPayment` → `Id`, `Status (CaptureStatus)`, `Amount`, `SellerReceivableBreakdown.{GrossAmount,PaypalFee,NetAmount}` | Case A `SdkException<CaptureAuthorizedPaymentError>` · `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | `payPalRequestId` |
| 5 | `Payments.ReauthorizePayment` | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` | `ReauthorizeRequest?` (`Amount: Money?` only) | `PaymentAuthorization` → `Id`, `Status`, `ExpirationTime` | Case A `SdkException<ReauthorizePaymentError>` · `TryGetError(out Error)` [400,401,403,404,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | `payPalRequestId` |
| 6 | `Payments.VoidPayment` | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` | none | `PaymentAuthorization` → `Status` (expect `Voided`) | Case A `SdkException<VoidPaymentError>` · `TryGetError(out Error)` [401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | `payPalRequestId` |
| 7 | `Payments.RefundCapturedPayment` | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer="return=minimal", RequestOptions? requestOptions=null, CancellationToken ct=default)` | `RefundRequest?` (null/empty = full refund; set `Amount` for partial) | `Refund` → `Id`, `Status (RefundStatus)`, `Amount`, `SellerPayableBreakdown` | Case A `SdkException<RefundCapturedPaymentError>` · `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | `payPalRequestId` ← **caller-supplied key goes here** |
| 9 | `Vault.CreatePaymentToken` | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions=null, CancellationToken ct=default)` | `PaymentTokenRequest` (required) | `PaymentTokenResponse` → `Id` (= vault/payment-method id), `PaymentSource.Card (CardPaymentTokenEntity)` = safe descriptor | Case A `SdkException<CreatePaymentTokenError>` · `TryGetError1(out Error1)` [400,403,404,422,500] · `TryGetRawError` | `payPalRequestId` |
| 9b | `Vault.CreateSetupToken` | `CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions? requestOptions=null, CancellationToken ct=default)` | `SetupTokenRequest` (required) | `SetupTokenResponse` → `Id`, `Status`, `PaymentSource.Card (SetupTokenResponseCard)` | Case A `SdkException<CreateSetupTokenError>` · `TryGetError1(out Error1)` [400,403,422,500] · `TryGetRawError` | `payPalRequestId` |
| 11 | `Vault.DeletePaymentToken` | `DeletePaymentToken(string id, RequestOptions? requestOptions=null, CancellationToken ct=default)` | none | `void`/`Task` (HTTP 204) | Case A `SdkException<DeletePaymentTokenError>` · `TryGetError1(out Error1)` [400,403,500] · `TryGetRawError` | n/a |
| 12 | `TransactionSearch.SearchTransactions` | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields="transaction_info", string? balanceAffectingRecordsOnly="Y", int? pageSize=100, int? page=1, RequestOptions? requestOptions=null, CancellationToken ct=default)` | none (query params) | `SearchResponse` → `TransactionDetails[]`, `Page`, `TotalItems`, `TotalPages` | **Case B** `SdkException<RawError>` · `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` (no typed accessor) | n/a |

Supporting reads (if the integration polls a prior resource): `Payments.GetAuthorizedPayment(authorizationId,…)→PaymentAuthorization`, `Payments.GetCapturedPayment(captureId,…)→CapturedPayment`, `Payments.GetRefund(refundId,…)→Refund`, `Vault.GetPaymentToken(id,…)→PaymentTokenResponse` (all `operations/Payments.md` / `operations/Vault.md`).

### 2c. Request/response model shapes (map: `records-1-Ac-Pa.md`, `records-2-Pa-Ve.md`)

Fields shown as `CSharpName (wire_name): Type`; `!req` = C# `required` (must be set in initializer); trailing `?` = optional/nullable.

**CreateOrder request tree**
- `OrderRequest`: `Intent (intent): CheckoutPaymentIntent !req` (set `CheckoutPaymentIntent.Authorize`), `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req`, `PaymentSource (payment_source): PaymentSource?`, `Payer (payer): Payer?`, `ApplicationContext (application_context): OrderApplicationContext?`. (`records-1-Ac-Pa.md`)
- `PurchaseUnitRequest`: `Amount (amount): AmountWithBreakdown !req`, `ReferenceId (reference_id): string?`, `InvoiceId (invoice_id): string?`, `CustomId (custom_id): string?`, `Description (description): string?`, … (`records-2-Pa-Ve.md`)
- `AmountWithBreakdown`: `CurrencyCode (currency_code): string !req` (from `PayPal:Currency`), `Value (value): string !req` (order total to the cent, e.g. `"49.99"`), `Breakdown (breakdown): AmountBreakdown?`. (`records-1-Ac-Pa.md`)
- `PaymentSource` (union-shaped record of optional wallets): `Card (card): CardRequest?`, `Token (token): Token?`, `Paypal…`, others. **Raw card ⇒ set `.Card`.** (`records-2-Pa-Ve.md`)
- `CardRequest` (raw card): `Name (name): string?`, `Number (number): string?`, `Expiry (expiry): string?` (`YYYY-MM`), `SecurityCode (security_code): string?` (cvc), `BillingAddress (billing_address): Address?`, `VaultId (vault_id): string?` **← set this INSTEAD of Number/Expiry/cvc to pay with a saved card token**, `SingleUseToken`, `StoredCredential`, `ExperienceContext (experience_context): CardExperienceContext?` (carries 3DS return/cancel URLs). (`records-1-Ac-Pa.md`)
- `Address`: `AddressLine1 (address_line_1): string?`, `AddressLine2`, `AdminArea2 (admin_area_2)` (city), `AdminArea1 (admin_area_1)` (state), `PostalCode (postal_code): string?`, `CountryCode (country_code): string !req`. (`records-1-Ac-Pa.md`)
- `Money`: `CurrencyCode (currency_code): string !req`, `Value (value): string !req`. (`records-1-Ac-Pa.md`)

**Create/Authorize response tree (where the authorization id lives)**
- `Order`: `Id (id): string?`, `Status (status): OrderStatus?`, `Intent (intent): CheckoutPaymentIntent?`, `PaymentSource (payment_source): PaymentSourceResponse?`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnit>?`, `Links (links): IReadOnlyList<LinkDescription>?`. (`records-1-Ac-Pa.md`)
- `OrderAuthorizeResponse`: same shape as `Order` but `PaymentSource` is `OrderAuthorizeResponsePaymentSource?`; still has `PurchaseUnits: IReadOnlyList<PurchaseUnit>?`, `Status`, `Links`. (`records-1-Ac-Pa.md`)
- `PurchaseUnit`: `Payments (payments): PaymentCollection?`, `Amount`, `ReferenceId`, … (`records-2-Pa-Ve.md`)
- `PaymentCollection`: `Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?`, `Captures (captures): IReadOnlyList<OrdersCapture>?`, `Refunds (refunds): IReadOnlyList<Refund>?`. (`records-2-Pa-Ve.md`)
- `AuthorizationWithAdditionalData`: **`Id (id): string?`** (the authorization id), **`Status (status): AuthorizationStatus?`**, `StatusDetails (status_details): AuthorizationStatusDetails?`, `Amount (amount): Money?`, `ExpirationTime (expiration_time): string?`. (`records-1-Ac-Pa.md`)
- **Read path for the authorization id (both `Order` and `OrderAuthorizeResponse`):** `.PurchaseUnits[i].Payments.Authorizations[j].Id` / `.Status`.

**Capture response (fee / net proceeds)** — `CapturedPayment` (`records-1-Ac-Pa.md`):
- `Id (id): string?` (capture id), `Status (status): CaptureStatus?`, `Amount (amount): Money?` (captured amount), `FinalCapture (final_capture): bool?`, `SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?`, `Payee (payee): PayeeBase?`.
- `SellerReceivableBreakdown` (`records-2-Pa-Ve.md`): **`GrossAmount (gross_amount): Money !req`**, **`PaypalFee (paypal_fee): Money?`** (PayPal fee), **`NetAmount (net_amount): Money?`** (net proceeds to merchant), `PaypalFeeInReceivableCurrency`, `ReceivableAmount (receivable_amount): Money?`, `ExchangeRate`.
- Exact nested wire paths: `seller_receivable_breakdown.gross_amount.{currency_code,value}`, `…paypal_fee.{…}`, `…net_amount.{…}`.

**Capture request (partial capture)** — `CaptureRequest` (`records-1-Ac-Pa.md`): `Amount (amount): Money?`, `FinalCapture (final_capture): bool? = false`, `InvoiceId`, `NoteToPayer`, `SoftDescriptor`, `PaymentInstruction`.

**Reauthorize request** — `ReauthorizeRequest`: `Amount (amount): Money?` only (supports only amount). (`records-2-Pa-Ve.md`)

**Refund** — request `RefundRequest` (`records-2-Pa-Ve.md`): `Amount (amount): Money?` (omit for FULL refund; set for PARTIAL), `CustomId`, `InvoiceId`, `NoteToPayer`, `PaymentInstruction`. Response `Refund`: `Id (id): string?`, `Status (status): RefundStatus?`, `Amount (amount): Money?`, `SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?`, `Links`.

**Vault (direct card, no browser step)**
- `PaymentTokenRequest`: `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req`, `Customer (customer): Customer?`. (`records-2-Pa-Ve.md`)
- `PaymentTokenRequestPaymentSource`: `Card (card): PaymentTokenRequestCard?`, `Token (token): VaultTokenRequest?` (use `Token` to promote a prior setup-token; use `Card` to vault a raw card in one step). (`records-2-Pa-Ve.md`)
- `PaymentTokenRequestCard`: `Name`, `Number (number): string?`, `Expiry (expiry): string?`, `SecurityCode (security_code): string?`, `Brand (brand): CardBrand?`, `BillingAddress (billing_address): Address?`. (`records-2-Pa-Ve.md`)
- Response `PaymentTokenResponse`: `Id (id): string?` (vault/payment-method id — use as `CardRequest.VaultId` when paying), `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?`, `Customer`, `Links`. (`records-2-Pa-Ve.md`)
- `PaymentTokenResponsePaymentSource.Card (card): CardPaymentTokenEntity?`. `CardPaymentTokenEntity` (safe descriptor, **no `Number`/full PAN field**): `LastDigits (last_digits): string?`, `Brand (brand): CardBrand?`, `Expiry (expiry): string?`, `Name (name): string?`, `Type (type): CardType?`, `BinDetails`. (`records-1-Ac-Pa.md`) — **Confirmed: no full PAN is ever returned to store.**
- Two-step alternative (`SetupTokenRequest` → `SetupTokenRequestPaymentSource.Card (SetupTokenRequestCard)` → then `CreatePaymentToken` with `PaymentTokenRequestPaymentSource.Token = VaultTokenRequest{ Id=<setup-token-id>, Type=VaultTokenRequestType.SetupToken }`). `VaultTokenRequest`: `Id (id): string !req`, `Type (type): VaultTokenRequestType !req`. (`records-2-Pa-Ve.md`)

**Pay with a vaulted token (#3 / #10):** set `OrderRequest.PaymentSource.Card = new CardRequest { VaultId = "<PaymentTokenResponse.Id>" }` (do NOT set Number/Expiry/cvc). Same field on `OrderAuthorizeRequestPaymentSource.Card` if authorizing an already-created order. `OrderAuthorizeRequestPaymentSource`: `Card (card): CardRequest?`, `Token`, `Paypal`, … (`records-1-Ac-Pa.md`). Note `Token`/`TokenType` (`TokenType` has only `BILLING_AGREEMENT`) is for PayPal billing agreements, **not** saved cards — cards go through `CardRequest.VaultId`.

**Transaction search response** — `SearchResponse` (`records-2-Pa-Ve.md`): `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?`, `Page (page): int?`, `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`, `StartDate`, `EndDate`, `Links`.
- `TransactionDetails.TransactionInfo (transaction_info): TransactionInformation?`. (`records-2-Pa-Ve.md`)
- `TransactionInformation` (line up against eShop orders): `TransactionId (transaction_id): string?`, `TransactionStatus (transaction_status): string?`, `TransactionAmount (transaction_amount): Money?`, `FeeAmount (fee_amount): Money?`, `TransactionInitiationDate (transaction_initiation_date): string?`, `TransactionUpdatedDate (transaction_updated_date): string?`, `InvoiceId (invoice_id): string?`, `CustomField (custom_field): string?`. (`records-2-Pa-Ve.md`)

### 2d. Enums needed (map: `enums.md`) — build via `Type.FromValue("WIRE")` or the static member; these are `StringEnum<T>`, NOT C# enums

| Enum | C# member (wire) values used |
|---|---|
| `CheckoutPaymentIntent` | `Authorize (AUTHORIZE)`, `Capture (CAPTURE)` — **use `Authorize`** |
| `OrderStatus` | `Created (CREATED)`, `Saved`, `Approved (APPROVED)`, `Voided`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured`, `Denied`, `PartiallyCaptured`, `Voided (VOIDED)`, `Pending (PENDING)` |
| `AuthorizationIncompleteReason` | `PendingReview (PENDING_REVIEW)`, `DeclinedByRiskFraudFilters (DECLINED_BY_RISK_FRAUD_FILTERS)` |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined`, `PartiallyRefunded`, `Pending`, `Refunded`, `Failed` |
| `RefundStatus` | `Cancelled`, `Failed`, `Pending`, `Completed (COMPLETED)` |
| `CardBrand` | `Visa (VISA)`, `Mastercard`, `Amex`, `Discover`, … (30 members) |
| `CardType` | `Credit (CREDIT)`, `Debit`, `Prepaid`, `Store`, `Unknown` |
| `PaymentTokenStatus` | `Created`, `Approved`, `Vaulted (VAULTED)`, `Tokenized`, `PayerActionRequired` |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` (only value) |
| `TokenType` | `BillingAgreement (BILLING_AGREEMENT)` (only value — not for saved cards) |

### 2e. Client construction, auth, environment, base-URL override (map: `sdk-map.md` *Getting a client* / *Servers & auth*; server-node shape confirmed from SDK source `ServerOptions`/`Servers.DefaultOptions`)

- **Construct / DI**: `services.AddPayPalServerSdkClient(o => { … })` or `new PayPalServerSdkClient(httpClient, options)`. `PayPalServerSdkClientOptions` properties: `Environment: ServerEnvironment`, `Server: ServerOptions`, `Oauth2: OAuth2ClientCredentials?`, `Oauth2TokenStrategy`, `Retry: RetryOptions`, `Logging: LoggingOptions`.
- **Auth (OAuth2 client-credentials)**: set `o.Oauth2 = new OAuth2ClientCredentials { ClientId = cfg["PayPal:ClientId"], ClientSecret = cfg["PayPal:ClientSecret"] }`. The SDK performs the client-credentials grant against `/v1/oauth2/token` (HTTP Basic of `clientId:clientSecret`, `grant_type=client_credentials`), caches the bearer token, and attaches it to every call — you do not call a token endpoint yourself.
- **Environment**: `o.Environment = ServerEnvironment.Sandbox`. Map the config key `PayPal:Environment`: `"sandbox"` ⇒ `ServerEnvironment.Sandbox`. **`ServerEnvironment` exposes ONLY `Sandbox`** — see Blockers for the production gap.
- **Base-URL override (verbatim, applies to token request too)**: when `PayPal:BaseUrl` is set, assign `o.Server.Default.Sandbox.BaseUrl = cfg["PayPal:BaseUrl"]`. The SDK builds the OAuth token URL as `server.Default("/v1/oauth2/token")` from this same `Sandbox.BaseUrl`, so the override reaches **both** the OAuth/token request and every v1/v2/v3 API call. When `PayPal:BaseUrl` is unset, leave it at its default (`https://api-m.sandbox.paypal.com`).

---

## 3. Trap notes (load the named skill at that step — do not resolve inline)

⚠ **Step 1 (client & DI)** — `HttpClient`/handler lifetime and whether the SDK client wrapper may be transient vs must be pooled is not visible in the constructor signature. **MUST load `dotnet-client-initialization`** before writing `AddPayPalServerSdkClient` / `new PayPalServerSdkClient(...)`.

⚠ **Step 1 (auth)** — whether credentials are read once at construction or per-call, and where to set them in the DI callback, is not shown by the property. **MUST load `dotnet-authentication`** before wiring `Oauth2`.

⚠ **Step 1 (retries vs base URL / timeout)** — whether the SDK retry policy re-sends a **POST** on a transport failure (bears directly on whether CreateOrder/Authorize/Capture/Refund can execute more than once), what `Retry.Timeout` actually bounds (per-attempt vs whole call), and how `Server`/base-URL selection interacts with retries, are all invisible in the option names. This is why refunds must carry a caller-supplied `payPalRequestId`. **MUST load `dotnet-configuration-resilience`** before tuning the client.

⚠ **Steps 2–4 (calls)** — every op above has several nullable-no-default header params before `body`; a positional call mis-binds them. Whether an optional param needs an explicit `null` and how `ct:` must be named is call-shape detail. **MUST load `dotnet-calling-endpoints`** before the first call.

⚠ **Steps 2–4 (models)** — enums here are `StringEnum<T>` (not C# enums), `required` members must be set in the initializer, and unmodeled JSON is dropped on deserialize; how to build `Money`/`CardRequest`/`CheckoutPaymentIntent` correctly is model-layer detail. **MUST load `dotnet-models`** before constructing payloads.

⚠ **Step 5 (error boundary)** — Case A vs B differ per op (SearchTransactions is Case B, the rest Case A), `TryGetRawError` is not a catch-all on typed errors, and there is no no-throw variant. **MUST load `dotnet-error-handling`** before writing any try/catch (see REQUIRED READING for the two mandatory `JsonException` hazards).

⚠ **Step 5 (tests)** — the `HttpClient` constructor argument is the test seam. **MUST load `dotnet-testing`** before stubbing the SDK.

---

## 4. REQUIRED READING (load ALL before implementation starts — this sheet deliberately does NOT carry their contents)

| Skill | Governs step |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, DI, HttpClient lifetime |
| `dotnet-authentication` | Step 1 — OAuth2 client-credentials wiring |
| `dotnet-configuration-resilience` | Step 1 — retries/timeout, base-URL/server selection, pagination |
| `dotnet-calling-endpoints` | Steps 2–4 — named args, must-pass-null params, async/`ct` |
| `dotnet-models` | Steps 2–4 — StringEnum, required init, unions, wire names |
| `dotnet-error-handling` | Step 5 — Case A/B, safe status/body reads, catch-ladder traps |
| `dotnet-testing` | Step 5 — the HttpClient seam, error-path coverage |

**Two mandatory `System.Text.Json.JsonException` hazards for the error boundary — `JsonException` reaches the boundary from two directions and they need opposite handling:**
- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions**
- `PayPal:Environment` is expected to be `"sandbox"`; mapped to `ServerEnvironment.Sandbox` (the only member the SDK exposes).
- `PayPal:Currency` is a 3-letter ISO-4217 code fed verbatim into `Money.CurrencyCode` / `AmountWithBreakdown.CurrencyCode`; amount `Value` is a string formatted to the currency's minor units (e.g. `"49.99"`).
- Vaulting a raw card without a browser step uses the **single-step** `Vault.CreatePaymentToken` with `payment_source.card`; the `CreateSetupToken` two-step path is documented as the alternative but not the primary.
- Paying with a saved card uses `CardRequest.VaultId` (not the `Token`/`BILLING_AGREEMENT` union, which is for PayPal wallet agreements).
- Idempotency keys are supplied by the caller on refunds via `payPalRequestId`; for CreateOrder/AuthorizeOrder/Capture the same `payPalRequestId` param exists and should be set for safety against retry double-execution.

**Blockers / gaps to report**
- **Production environment is NOT exposed by this SDK.** `ServerEnvironment` (source `Servers/ServerEnvironment.cs`) declares only `Sandbox`, and its internal `Match` throws `ArgumentOutOfRangeException` for any other value. If `PayPal:Environment` is ever set to `production`/`live`, there is no SDK member to select it — the only route to a non-sandbox host is the `PayPal:BaseUrl` override (`o.Server.Default.Sandbox.BaseUrl`), which changes the URL but still travels under the `Sandbox` environment node. STOP and confirm with the requester before targeting production.
- **`UNVERIFIED` — 3DS / browser challenge on a direct raw-card AUTHORIZE (sandbox).** The map/source cannot confirm whether sandbox returns an approval challenge for card `4111 1111 1111 1111`; only live traffic can. Defensive directive: after CreateOrder (and after AuthorizeOrder), inspect `Order.Status` — if it is `OrderStatus.PayerActionRequired`, or `Order.Links` contains a link with `rel == "payer-action"`, a browser/approval round-trip is required. In that case **STOP and report** rather than proceeding; do not attempt to auto-follow the link. On the happy path expect `Status` `Completed`/`Approved` with `PurchaseUnits[].Payments.Authorizations[]` populated. Label this branch `UNVERIFIED` until observed against sandbox.
- **`UNVERIFIED` — exact issue codes for over-refund and non-reauthorizable authorization.** Over-refunding beyond the captured amount surfaces as `SdkException<RefundCapturedPaymentError>` at HTTP 422 via `TryGetError(out Error)`; a stale authorization that can no longer be reauthorized surfaces as `SdkException<ReauthorizePaymentError>` at 422 via `TryGetError(out Error)`. The specific `Error.Details[].Issue` string (e.g. an amount-exceeded / auth-expired code) is a live-wire value the map does not enumerate. Defensive directive: extract `Error.Message` and `Error.Details[].Issue`/`.Description` best-effort for an actionable operator message, and fall back to the generic `Error.Message` (+ HTTP 422) when `Details` is empty. Do not hard-code an issue string.
- **`UNVERIFIED` — `SearchTransactions` date format.** `startDate`/`endDate` are `string` (map). PayPal's reporting API requires full ISO-8601 date-time with timezone offset; pass the caller's `[from,to]` formatted as RFC-3339 (e.g. `2026-08-01T00:00:00-0000`). Verify against sandbox; a bare `yyyy-MM-dd` may be rejected. **Pagination directive:** `SearchTransactions` exposes only `page`/`pageSize` (no cursor). Call once with `page:1`, read `SearchResponse.TotalPages`, then loop `page:2..TotalPages` (same `pageSize`, `startDate`, `endDate`) and concatenate `TransactionDetails` to cover the whole range — page 1 alone is not the full report.
</content>
</invoke>
