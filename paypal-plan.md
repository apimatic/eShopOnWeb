# PayPal .NET SDK — Contract Sheet & Integration Plan (eShopOnWeb `src/PublicApi`, direct card, sandbox)

SDK identity (from `sdk-map.md`): NuGet package **`AsadAli.Checkout.Sdk`** (install version-less: `dotnet add package AsadAli.Checkout.Sdk`) · root namespace **`PayPalServerSdk`** · client class **`PayPalServerSdkClient`** · options **`PayPalServerSdkClientOptions`** · documented release tag `v1.0.1` (source commit `9653d18`). Target framework `netstandard2.0`. Compiler is the backstop — if a name here fails to build, trust the compiler and re-ground.

---

## 1. Scope & sequence

1. **Client & DI setup** — register `PayPalServerSdkClient` with `IHttpClientFactory`, OAuth2 client-credentials, and a `PayPal:BaseUrl` override. (client construction)
2. **Create AUTHORIZE order + raw card** — `client.Orders.CreateOrder` (one-off card). Stamp `invoice_id`/`custom_id` here for reconciliation.
3. **(fallback) Authorize a created order** — `client.Orders.AuthorizeOrder` when the authorization is not returned inline.
4. **Vault a card** — `client.Vault.CreatePaymentToken`; delete via `client.Vault.DeletePaymentToken`; reuse via `payment_source.card.vault_id`.
5. **Capture** — `client.Payments.CaptureAuthorizedPayment`.
6. **Reauthorize** — `client.Payments.ReauthorizePayment`.
7. **Void** — `client.Payments.VoidPayment`.
8. **Refund** — `client.Payments.RefundCapturedPayment`.
9. **Transaction search / reconciliation** — `client.TransactionSearch.SearchTransactions` (paginated).
10. **Read current state** — `client.Orders.GetOrder`, `client.Payments.GetAuthorizedPayment`, `client.Payments.GetCapturedPayment`, `client.Payments.GetRefund`.
11. **Error boundary** — one translation layer over all of the above.

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### 2a. Namespaces (add a separate `using` per type-kind — child namespaces do NOT import transitively)

| Contents | Namespace |
|---|---|
| Client, `PayPalServerSdkClientOptions`, `ServerOptions` (file `ServerOptions.cs` at repo root) | `PayPalServerSdk` |
| `ServerEnvironment`, `DefaultOptions` (+ nested `DefaultOptions.SandboxOptions`) | `PayPalServerSdk.Servers` |
| Operation controllers (property types on client) | `PayPalServerSdk.Api` |
| Request/response **records** (`OrderRequest`, `Order`, `CardRequest`, `Money`, `CapturedPayment`, `Refund`, `PaymentTokenRequest`, `SearchResponse`, error-payload records `Error`/`Error1`/`DefaultError`/`ErrorDetails`, …) | `PayPalServerSdk.Models` |
| **Enums** (`CheckoutPaymentIntent`, `OrderStatus`, `AuthorizationStatus`, `CaptureStatus`, `RefundStatus`, `CardBrand`, `TokenType`, …) | `PayPalServerSdk.Models.Enums` |
| Typed error CLASSES (`CreateOrderError`, `AuthorizeOrderError`, `CaptureAuthorizedPaymentError`, `CreatePaymentTokenError`, …) | `PayPalServerSdk.Errors` |
| `SdkException<TError>` | `PayPalServerSdk.Core.Exceptions` |
| `RawError`, `ApiError` | `PayPalServerSdk.Core.ErrorResponse` |
| `OAuth2ClientCredentials` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` |
| `IOAuth2TokenStrategy<>` | `PayPalServerSdk.Core.Authentication.OAuth2` |
| `RetryOptions` | `PayPalServerSdk.Core.Configuration` |

> Note the split: a Case-A catch is `catch (SdkException<CreateOrderError> ex)` — the CLASS `CreateOrderError` is in `PayPalServerSdk.Errors`; the payload record it hands back via `TryGetError(out Error e)` — `Error` — is in `PayPalServerSdk.Models`.

### 2b. Operations (all signatures verbatim from the map; `RequestOptions? requestOptions = null, CancellationToken ct = default` trail every signature and are omitted below)

| # | Controller.Method (params in order) | Request model + key fields | Response envelope → fields you read | Error case + accessors | Map page |
|---|---|---|---|---|---|
| Create order | `client.Orders.CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal")` — first 5 nullable params **must be passed explicitly** (pass `null`) | `OrderRequest`: `Intent (intent): CheckoutPaymentIntent !req`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req`, `PaymentSource (payment_source): PaymentSource?`, `Payer?`, `ApplicationContext?` | returns **`Order`**: `Id (id): string?`, `Status (status): OrderStatus?`, `PurchaseUnits: IReadOnlyList<PurchaseUnit>?`, `PaymentSource (payment_source): PaymentSourceResponse?`, `Links: IReadOnlyList<LinkDescription>?`. Inline auth: `Order.PurchaseUnits[0].Payments (PaymentCollection).Authorizations[0] (AuthorizationWithAdditionalData).Id / .Status (AuthorizationStatus)` | **Case A** `SdkException<CreateOrderError>`; `TryGetError(out Error)` [400,401,422] · `TryGetRawError(out RawError)` [fallback] | operations/Orders.md; records-1 (`OrderRequest`,`Order`) |
| Authorize order (fallback) | `client.Orders.AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal")` — 5 nullable params must pass explicitly | `OrderAuthorizeRequest`: `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` (variants: `Card: CardRequest?`, `Token: Token?`, `Paypal`, `ApplePay`, `GooglePay`, `Venmo`) | returns **`OrderAuthorizeResponse`**: `Id`, `Status (status): OrderStatus?`, `PurchaseUnits: IReadOnlyList<PurchaseUnit>?` → `[0].Payments.Authorizations[0].Id / .Status` | **Case A** `SdkException<AuthorizeOrderError>`; `TryGetError(out Error)` [400,401,403,404,422,500] · `TryGetRawError` | operations/Orders.md; records-1 |
| Capture authorization | `client.Payments.CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal")` — 4 nullable params must pass explicitly | `CaptureRequest`: `Amount (amount): Money?`, `InvoiceId?`, `FinalCapture (final_capture): bool? = false`, `NoteToPayer?`, `SoftDescriptor?` (pass `body: null` for full capture) | returns **`CapturedPayment`**: `Id (id): string?`, `Status (status): CaptureStatus?`, `Amount (amount): Money?` (`Money.CurrencyCode`, `Money.Value` — both **string**), `SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?` → `.GrossAmount (gross_amount): Money !req`, `.PaypalFee (paypal_fee): Money?`, `.NetAmount (net_amount): Money?` (all `Money`, values are strings; paypal_fee & net_amount nullable) | **Case A** `SdkException<CaptureAuthorizedPaymentError>`; `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | operations/Payments.md; records-1 (`CapturedPayment`), records-2 (`SellerReceivableBreakdown`) |
| Reauthorize | `client.Payments.ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal")` — 3 nullable params must pass explicitly | `ReauthorizeRequest`: **`Amount (amount): Money?` only** (SDK/API support only `amount`) | returns **`PaymentAuthorization`**: `Id`, `Status (status): AuthorizationStatus?`, `Amount: Money?`, `ExpirationTime (expiration_time): string?` | **Case A** `SdkException<ReauthorizePaymentError>`; `TryGetError(out Error)` [400,401,403,404,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | operations/Payments.md; records-2 (`ReauthorizeRequest`,`PaymentAuthorization`) |
| Void | `client.Payments.VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal")` — 3 nullable params must pass explicitly (no request body) | *(none)* | returns **`PaymentAuthorization`**: `Status (status): AuthorizationStatus?` → expect `AuthorizationStatus.Voided`. Success signal = **no exception thrown** (2xx; body may be minimal under `prefer=return=minimal`) | **Case A** `SdkException<VoidPaymentError>`; `TryGetError(out Error)` [401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | operations/Payments.md |
| Refund | `client.Payments.RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal")` — 4 nullable params must pass explicitly | `RefundRequest`: `Amount (amount): Money?` (partial → set; full → pass `body: null` or empty), `CustomId?`, `InvoiceId?`, `NoteToPayer?` | returns **`Refund`**: `Id (id): string?`, `Status (status): RefundStatus?`, `Amount: Money?`, `SellerPayableBreakdown (seller_payable_breakdown): SellerPayableBreakdown?` (`GrossAmount`,`PaypalFee`,`NetAmount`, all `Money?`) | **Case A** `SdkException<RefundCapturedPaymentError>`; `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | operations/Payments.md; records-2 (`RefundRequest`,`Refund`,`SellerPayableBreakdown`) |
| Vault card | `client.Vault.CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body)` — `payPalRequestId` must pass explicitly | `PaymentTokenRequest`: `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req`, `Customer (customer): Customer?`. `PaymentTokenRequestPaymentSource`: `Card (card): PaymentTokenRequestCard?`. `PaymentTokenRequestCard`: `Name?`, `Number?`, `Expiry?`, `SecurityCode (security_code)?`, `Brand: CardBrand?`, `BillingAddress: Address?` | returns **`PaymentTokenResponse`**: `Id (id): string?` (the token id), `PaymentSource (payment_source): PaymentTokenResponsePaymentSource?` → `.Card (card): CardPaymentTokenEntity?` → `.LastDigits (last_digits): string?`, `.Brand (brand): CardBrand?`, `.Expiry (expiry): string?` | **Case A** `SdkException<CreatePaymentTokenError>`; **`TryGetError1(out Error1)`** [400,403,404,422,500] · `TryGetRawError` | operations/Vault.md; records-2 (`PaymentTokenRequest…`,`CardPaymentTokenEntity`→records-1) |
| Delete vaulted token | `client.Vault.DeletePaymentToken(string id)` | *(none)* | returns **`void` (Task)**; success = no exception (204) | **Case A** `SdkException<DeletePaymentTokenError>`; `TryGetError1(out Error1)` [400,403,500] · `TryGetRawError` | operations/Vault.md |
| Search transactions | `client.TransactionSearch.SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1)` — the 8 nullable filters `transactionId…terminalId` must pass explicitly (pass `null`). `startDate`/`endDate` = ISO-8601 (`start_date`/`end_date`) | *(query only)* | returns **`SearchResponse`**: `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?`, `Page (page): int?`, `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`. Per item: `TransactionDetails.TransactionInfo (transaction_info): TransactionInformation?` → `.TransactionId (transaction_id): string?`, `.TransactionStatus (transaction_status): string`, `.TransactionAmount (transaction_amount): Money?`, `.InvoiceId (invoice_id): string?`, `.CustomField (custom_field): string?` | **Case B** `SdkException<RawError>` (the ONLY Case-B op in this SDK): `ex.Error.StatusCode: HttpStatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | operations/TransactionSearch.md; records-2/1 |
| Get order | `client.Orders.GetOrder(string id, string? fields, string? payPalMockResponse, string? payPalAuthAssertion)` — 3 nullable params must pass explicitly | *(query only: `fields`)* | returns **`Order`** (same shape as Create) | **Case A** `SdkException<GetOrderError>`; `TryGetError(out Error)` [401,404] · `TryGetRawError` | operations/Orders.md |
| Get authorization | `client.Payments.GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion)` — 2 nullable params must pass explicitly | *(none)* | returns **`PaymentAuthorization`**: `Status: AuthorizationStatus?`, `ExpirationTime: string?`, `Amount: Money?` | **Case A** `SdkException<GetAuthorizedPaymentError>`; `TryGetError(out Error)` [401,403,404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | operations/Payments.md |
| Get capture | `client.Payments.GetCapturedPayment(string captureId, string? payPalMockResponse)` — 1 nullable param must pass explicitly | *(none)* | returns **`CapturedPayment`** (same shape as capture) | **Case A** `SdkException<GetCapturedPaymentError>`; `TryGetError(out Error)` [401,403,404] · `TryGetNoContent` [500] · `TryGetRawError` | operations/Payments.md |
| Get refund | `client.Payments.GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion)` — 2 nullable params must pass explicitly | *(none)* | returns **`Refund`** | **Case A** `SdkException<GetRefundError>`; `TryGetError(out Error)` [401,403,404] · `TryGetNoContent` [500] · `TryGetRawError` | operations/Payments.md |

### 2c. Card payment_source shapes (request)

- **Raw card (one-off, create order / authorize order)** — `PaymentSource { Card = CardRequest{...} }`.
  `CardRequest` (records-1): `Name (name): string?`, `Number (number): string?`, `Expiry (expiry): string?` (`YYYY-MM`), `SecurityCode (security_code): string?`, `BillingAddress (billing_address): Address?`, `VaultId (vault_id): string?`, `StoredCredential (stored_credential): CardStoredCredential?`, `Attributes (attributes): CardAttributes?`, `ExperienceContext (experience_context): CardExperienceContext?`.
  `Address` (records-1): `AddressLine1 (address_line_1)?`, `AddressLine2 (address_line_2)?`, `AdminArea2 (admin_area_2)?` (city), `AdminArea1 (admin_area_1)?` (state), `PostalCode (postal_code)?`, **`CountryCode (country_code): string !req`**.
- **Saved-card reuse (vaulted)** — `PaymentSource { Card = new CardRequest { VaultId = "<token id>" } }`. The stored-card reuse path is **`payment_source.card.vault_id`**, NOT the `Token` variant (see trap ⚠-V below).
- To **vault-on-purchase** while paying, set `CardRequest.Attributes = CardAttributes{ Vault = VaultInstruction{ StoreInVault = StoreInVaultInstruction.OnSuccess } }` (`CardAttributes.Vault` is `VaultInstructionBase?`; `VaultInstruction.StoreInVault: StoreInVaultInstruction !req`, only value `OnSuccess`/`ON_SUCCESS`).

### 2d. Amount / purchase-unit shape (request)

- `PurchaseUnitRequest` (records-2): **`Amount (amount): AmountWithBreakdown !req`**, `ReferenceId (reference_id)?`, `InvoiceId (invoice_id): string?`, `CustomId (custom_id): string?`, `Description?`, `Items (items): IReadOnlyList<ItemRequest>?`.
- `AmountWithBreakdown` (records-1): **`CurrencyCode (currency_code): string !req`**, **`Value (value): string !req`** (decimal string, e.g. `"12.34"`), `Breakdown (breakdown): AmountBreakdown?`.
- `Money` (records-1, used on responses/capture/refund/reauth): `CurrencyCode (currency_code): string !req`, `Value (value): string !req`. **All monetary values are strings, never decimal.**
- **Reconciliation stamp**: set `PurchaseUnitRequest.InvoiceId` and/or `PurchaseUnitRequest.CustomId` at create-order time; these surface on `TransactionInformation.InvoiceId` / `.CustomField` in transaction search, and on `CapturedPayment.InvoiceId`/`.CustomId`.

### 2e. Enum value tables (literal C# member → wire value) — `PayPalServerSdk.Models.Enums`

| Enum | Members (C# `Member` → `WIRE`) |
|---|---|
| `CheckoutPaymentIntent` | `Capture`→`CAPTURE`, **`Authorize`→`AUTHORIZE`** |
| `OrderStatus` | `Created`→`CREATED`, `Saved`→`SAVED`, `Approved`→`APPROVED`, `Voided`→`VOIDED`, `Completed`→`COMPLETED`, `PayerActionRequired`→`PAYER_ACTION_REQUIRED` |
| `AuthorizationStatus` | `Created`→`CREATED`, `Captured`→`CAPTURED`, `Denied`→`DENIED`, `PartiallyCaptured`→`PARTIALLY_CAPTURED`, `Voided`→`VOIDED`, `Pending`→`PENDING` |
| `CaptureStatus` | `Completed`→`COMPLETED`, `Declined`→`DECLINED`, `PartiallyRefunded`→`PARTIALLY_REFUNDED`, `Pending`→`PENDING`, `Refunded`→`REFUNDED`, `Failed`→`FAILED` |
| `RefundStatus` | `Cancelled`→`CANCELLED`, `Failed`→`FAILED`, `Pending`→`PENDING`, `Completed`→`COMPLETED` |
| `CardBrand` | `Visa`→`VISA`, `Mastercard`→`MASTERCARD`, `Discover`→`DISCOVER`, `Amex`→`AMEX`, `Jcb`→`JCB`, `Maestro`→`MAESTRO`, `Diners`→`DINERS`, `Elo`→`ELO`, `Rupay`→`RUPAY`, `ChinaUnionPay`→`CHINA_UNION_PAY`, `Unknown`→`UNKNOWN`, … (30 members total; see enums.md) |
| `TokenType` | **only** `BillingAgreement`→`BILLING_AGREEMENT` |
| `VaultTokenRequestType` | **only** `SetupToken`→`SETUP_TOKEN` |
| `StoreInVaultInstruction` | **only** `OnSuccess`→`ON_SUCCESS` |
| `PaymentTokenStatus` | `Created`→`CREATED`, `PayerActionRequired`→`PAYER_ACTION_REQUIRED`, `Approved`→`APPROVED`, `Vaulted`→`VAULTED`, `Tokenized`→`TOKENIZED` |
| `AuthorizationIncompleteReason` | `PendingReview`→`PENDING_REVIEW`, `DeclinedByRiskFraudFilters`→`DECLINED_BY_RISK_FRAUD_FILTERS` |
| `CaptureIncompleteReason` | 12 members incl. `PendingReview`→`PENDING_REVIEW`, `DeclinedByRiskFraudFilters`→`DECLINED_BY_RISK_FRAUD_FILTERS`, `Refunded`→`REFUNDED`, `VerificationRequired`→`VERIFICATION_REQUIRED`, … (enums.md) |

> Enums are `StringEnum<T>` **records, not C# enums** — write `CheckoutPaymentIntent.Authorize`, or `CheckoutPaymentIntent.FromValue("AUTHORIZE")`. Never `CheckoutPaymentIntent.AUTHORIZE`. (MUST load `dotnet-models` — see trap ⚠-M.)

> **`EXPIRED` is NOT a modeled status anywhere.** None of `OrderStatus`/`AuthorizationStatus`/`CaptureStatus`/`RefundStatus` has an `EXPIRED` member. An expired authorization is surfaced through an **error** on reauthorize/capture (issue text), not a status value — see §4.

### 2f. Client construction / auth / server (source-grounded)

- **Options type** `PayPalServerSdkClientOptions` (ns `PayPalServerSdk`) properties: `Environment: ServerEnvironment`, `Retry: RetryOptions`, `Logging: LoggingOptions`, `Server: ServerOptions`, `Oauth2: OAuth2ClientCredentials?`, `Oauth2TokenStrategy: IOAuth2TokenStrategy<OAuth2ClientCredentials>?`.
- **Constructor**: `new PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)`. DI helper: `services.AddPayPalServerSdkClient(o => { … })` (in `ServiceCollectionExtensions.cs`).
- **Auth (OAuth2 client-credentials)**: set `options.Oauth2 = new OAuth2ClientCredentials { ClientId = <id>, ClientSecret = <secret> }`. `OAuth2ClientCredentials` (ns `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials`): `required string ClientId`, `required string ClientSecret`, `string? Scope`. The SDK obtains/refreshes the bearer token itself via the default `OAuth2ClientCredentialsStrategy` — no manual token call needed.
- **Environment**: `options.Environment = ServerEnvironment.Sandbox`. **`ServerEnvironment` (ns `PayPalServerSdk.Servers`) ships ONLY the `Sandbox` member** — there is no `Live`/`Production` member in this generated SDK (see BLOCKER B1).
- **Base-URL override (verbatim `PayPal:BaseUrl`)** — set:
  `options.Server = new ServerOptions { Default = new PayPalServerSdk.Servers.DefaultOptions { Sandbox = new PayPalServerSdk.Servers.DefaultOptions.SandboxOptions { BaseUrl = config["PayPal:BaseUrl"] } } };`
  (`ServerOptions` ns `PayPalServerSdk`; `DefaultOptions` + nested `SandboxOptions` ns `PayPalServerSdk.Servers`; default when unset = `https://api-m.sandbox.paypal.com`.) **Source-confirmed:** the OAuth2 token request is built as `server.Default("/v1/oauth2/token")` — i.e. it resolves through the **same** `Server.Default.Sandbox.BaseUrl` as every API call, so this single override covers the token request AND every operation call. (To point at live you would set this to `https://api-m.paypal.com`, since no Live environment member exists.)
- **HttpClient / DI ownership**: the `HttpClient` is the SDK's transport seam and must be long-lived / factory-managed (`IHttpClientFactory`), not rebuilt per request; the SDK client wrapper may be transient. (MUST load `dotnet-client-initialization` — trap ⚠-1.)

### 2g. Idempotency (source-confirmed)

- The idempotency mechanism is the HTTP header **`PayPal-Request-Id`**, set per-call via the **`payPalRequestId`** parameter (source `Api/Orders.cs`, `Api/Payments.cs`, `Api/Vault.cs`: `new HeaderParam("PayPal-Request-Id", payPalRequestId)`). Present on: `CreateOrder`, `AuthorizeOrder`, `CaptureOrder`, `CaptureAuthorizedPayment`, `RefundCapturedPayment`, `ReauthorizePayment`, `VoidPayment`, `CreatePaymentToken`, `CreateSetupToken`. Pass a stable per-logical-operation key (server stores keys ~6h; per SDK doc-comment it is **mandatory for single-step create-order-with-card / vault_id** calls).
- Sibling header `payPalMockResponse` → `PayPal-Mock-Response` (leave `null` in production).

### 2h. Error-body reading & 3DS signal

- `SdkException<TError>` (ns `PayPalServerSdk.Core.Exceptions`) exposes **only** `.Error` (type `TError`) plus the inherited `Exception.Message` — **it has no `StatusCode` property**. So the numeric HTTP status is read from the error payload, not the exception object.
- **Case A** typed payload `Error` (ns `PayPalServerSdk.Models`, via `TryGetError(out Error e)`): `Name (name): string !req`, `Message (message): string !req`, `DebugId (debug_id): string !req`, `Details (details): IReadOnlyList<ErrorDetails>?`, `Links?`. `ErrorDetails`: `Field?`, `Value?`, `Location? = "body"`, **`Issue (issue): string !req`**, `Description?`. Vault ops instead expose `TryGetError1(out Error1)` → `Error1` (same shape, `Details: IReadOnlyList<ErrorDetails1>`). `SearchBalances` exposes `TryGetDefaultError(out DefaultError)`.
- **Numeric HTTP status**: the typed `Error`/`Error1`/`DefaultError` records do **not** carry it; the `RawError` fallback (`TryGetRawError(out RawError raw)` → `raw.StatusCode: HttpStatusCode`) does. Whether `TryGetRawError` also returns a status *after* a typed shape has matched is governed by the error skill — **do not assume**; see trap ⚠-E.
- **Case B** (`SearchTransactions`): `ex.Error.StatusCode`, `ex.Error.ReadAsString()`, `ex.Error.ReadAsJson<T>()`.
- **3DS / "shopper must approve in browser" signal** on a card order response: `Order.Status == OrderStatus.PayerActionRequired` (wire `PAYER_ACTION_REQUIRED`) and/or a HATEOAS link in `Order.Links` with `Rel == "payer-action"` (`LinkDescription { Href, Rel, Method }`). The 3DS authentication outcome is at `Order.PaymentSource.Card (CardResponse).AuthenticationResult (AuthenticationResponse)` → `.LiabilityShift (LiabilityShiftIndicator: No/Possible/Unknown)` and `.ThreeDSecure (ThreeDSecureAuthenticationResponse)` → `.AuthenticationStatus (ParesStatus?)`, `.EnrollmentStatus (EnrollmentStatus?)`. **Directive:** if status is `PAYER_ACTION_REQUIRED`, a `payer-action` link is present, or liability has not shifted, **STOP and surface an operator/shopper-actionable message — do not attempt an automated approval round-trip.** Whether the live sandbox wire populates `AuthenticationResult`/the `payer-action` link for test card `4111...` is `UNVERIFIED` (only live traffic confirms it); code the branch defensively — extract these best-effort and fall back to reporting "approval required / authentication result unknown" if absent.

---

## 3. Answer to "does an AUTHORIZE order with a card return the authorization inline, or need a separate call?"

The contract supports **both**, and the code must handle both:
- `CreateOrder` returns `Order`. When the card is supplied inline as `payment_source.card` in a single-step create (with a `PayPal-Request-Id`) and PayPal processes it without challenge, the authorization can appear inline at `Order.PurchaseUnits[0].Payments.Authorizations[0]` (`.Id`, `.Status: AuthorizationStatus`).
- If it is **not** present inline — e.g. `Order.Status` is `APPROVED` (buyer-approval model) or `PAYER_ACTION_REQUIRED` (3DS) — a **separate** `client.Orders.AuthorizeOrder(id, …)` call is required, whose `OrderAuthorizeResponse.PurchaseUnits[0].Payments.Authorizations[0]` carries the authorization.
- **Directive:** after `CreateOrder`, read `Order.PurchaseUnits?[0]?.Payments?.Authorizations` best-effort; if non-empty use `[0].Id`/`[0].Status`; else, if `Order.Status == OrderStatus.PayerActionRequired` STOP (see 3DS above); else call `AuthorizeOrder` and read from its response. Whether the inline authorization is populated for a raw-card single-step create in sandbox is `UNVERIFIED` (live-traffic dependent) — hence the defensive two-path handling.

---

## 4. Detecting a non-reauthorizable / non-capturable (expired/voided) authorization

- Constraints (from `ReauthorizePayment` map notes): reauthorize only during days **4–29** after the 3-day honor period; if **30+ days** since the original authorization you must create a **new** authorized payment; **only `amount`** is supported in `ReauthorizeRequest`.
- Proactive read: `GetAuthorizedPayment` → `PaymentAuthorization.Status` (`AuthorizationStatus.Voided` means voided; there is **no `EXPIRED` enum value**) and `.ExpirationTime` (compare to now for expiry).
- Reactive detection: the failing reauthorize/capture throws **Case A** `SdkException<ReauthorizePaymentError>` / `SdkException<CaptureAuthorizedPaymentError>`; read `ex.Error.TryGetError(out Error e)` then inspect `e.Details[*].Issue` (free-text string) and `e.Message`. **The exact PayPal issue strings (e.g. an "authorization expired" / "already captured" / "voided" issue name) are NOT enumerated in the generated SDK** — `ErrorDetails.Issue` is a plain `string`, so the specific issue tokens are `UNVERIFIED` from the SDK alone. **Directive:** surface `e.Name` + `e.Message` + each `e.Details[*].Issue` verbatim into the operator message (best-effort extraction, fall back to `ex.Message` if the typed shape is absent); do not hard-match on a memorized issue constant.

---

## 5. Trap notes (load the named skill before the step)

- ⚠-1 **Step 1 (client/DI)** — the `HttpClient`/handler pipeline lifetime and whether the SDK client is singleton vs transient is not shown by the constructor signature; getting it wrong causes socket exhaustion or stale DNS. **MUST load `dotnet-client-initialization`** before wiring the client.
- ⚠-2 **Step 1 (auth)** — how/when credentials are read and how the token is refreshed and cached across the client's lifetime is not visible from the property; secrets must come from configuration, not literals. **MUST load `dotnet-authentication`** before setting `Oauth2`.
- ⚠-3 **Step 1 (base URL / resilience)** — the SDK's `Retry`/`Timeout` options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register, and which verbs/failures actually retry (a transport failure can re-send a non-idempotent `POST`) is not shown by the option names — which is exactly why `PayPal-Request-Id` matters. **MUST load `dotnet-configuration-resilience`** before tuning the client, base URL, retries, or pagination.
- ⚠-C **Every call** — list/search/create ops have optional params with **no C# default** (the `must pass explicitly` params above); a positional call mis-binds them. Whether to call with named arguments and how cancellation/`ct:` flows is the skill's domain. **MUST load `dotnet-calling-endpoints`** before the first call.
- ⚠-M **Any non-scalar field** — enums are `StringEnum<T>` (not C# enums), payment-source is a nested record graph, and **unmodeled JSON fields are dropped on deserialize** (so a field you rely on but the SDK didn't model silently vanishes). **MUST load `dotnet-models`** before building request payloads or mapping responses.
- ⚠-V **Step 4 (vaulted-card reuse)** — do **not** reach for the `Token` payment-source variant to pay with a saved card: `TokenType` has only `BILLING_AGREEMENT`, which is not a vaulted-card id. Whether a given stored id is a card-vault id vs a billing-agreement id changes which field carries it. The card-vault reuse field is `payment_source.card.vault_id`. **MUST load `dotnet-models`** before constructing the reuse payload.
- ⚠-E **Step 11 (error boundary)** — which `SdkException<…>` reaches each catch, whether `TryGetRawError` still yields a status after a typed shape matched, and how to read status/body safely are all skill-governed and easy to get subtly wrong. **MUST load `dotnet-error-handling`** before writing the boundary.
- ⚠-T **Step 11 (tests)** — the `HttpClient` constructor argument is the test seam; match the project's existing test framework/assertion style. **MUST load `dotnet-testing`** before stubbing the SDK.

---

## 6. REQUIRED READING (load BEFORE implementation starts — this sheet deliberately omits their contents)

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, HttpClient lifetime, DI registration |
| `dotnet-authentication` | Step 1 — OAuth2 client-credentials wiring, token refresh, secret loading |
| `dotnet-configuration-resilience` | Step 1 — base-URL override, retries/timeout semantics, pagination walking |
| `dotnet-calling-endpoints` | Steps 2–10 — named-argument calls, required vs optional params, async/`ct` |
| `dotnet-models` | Steps 2–4, 9 — request-model building, enums, nested payment-source graph, vault_id reuse |
| `dotnet-error-handling` | Step 11 — exception types, status/body reading, catch-ladder correctness |
| `dotnet-testing` | tests — the HttpClient seam |

**Two mandatory `JsonException` hazards for the error boundary (`System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling):**
- A drifted or malformed **2xx** body (a missing `required` member — e.g. a required `Money`/`gross_amount` absent) surfaces as a `JsonException` from **deserialization**, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 7. Assumptions & Blockers

- **A1** No plan path was dictated in the brief; wrote to the repo-root default `<repo root>/paypal-plan.md`.
- **A2** "Direct card in sandbox" is taken to mean the raw-card `payment_source.card` single-step flow (SAQ-D; test card `4111 1111 1111 1111`), with `PayPal-Request-Id` on every create/authorize/capture/refund. Confirm the app actually holds SAQ-D compliance before shipping raw PAN handling (SDK doc-comment flags this).
- **A3** Currency comes from config per the brief; `AmountWithBreakdown.Value`/`Money.Value` are decimal **strings** — the integration must format its `decimal` totals to a currency-correct string (e.g. `"12.34"`), never pass a raw number.
- **B1 (report upstream)** **No Live/Production environment is exposed.** `ServerEnvironment` (v1.0.1) ships only `Sandbox`. Going live is only possible by overriding `Server.Default.Sandbox.BaseUrl` to `https://api-m.paypal.com`; there is no first-class `ServerEnvironment.Production`. This is an SDK limitation to surface to the caller.
- **B2 (UNVERIFIED, live-traffic only)** Whether a raw-card single-step `CreateOrder` returns the authorization inline vs requiring a separate `AuthorizeOrder`, and whether the sandbox wire populates the 3DS `AuthenticationResult`/`payer-action` link for `4111...`, cannot be settled from map or source — handled by the defensive two-path logic in §3 and the 3DS directive in §2h.
- **B3 (UNVERIFIED)** The specific PayPal error `issue` strings for expired/voided/already-captured authorizations are not modeled (`ErrorDetails.Issue` is a free `string`) — surface them best-effort per §4 rather than hard-matching constants.
