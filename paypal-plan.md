# PayPal .NET SDK integration plan — eShopOnWeb (additive)

SDK: `AsadAli.Checkout.Sdk` · root namespace `PayPalServerSdk` · client `PayPalServerSdkClient`.
Map release stamp: tag `v1.0.1`, source commit `9653d18`. Install version-less:
`dotnet add package AsadAli.Checkout.Sdk` (from the getting-started skill).

This plan grounds every fact in the bundled SDK map (map page cited per row). A small number of
capability-1 facts the map does not carry (OAuth2 credential shape, the base-URL override chain,
absence of a Production environment) were confirmed from the SDK source and are cited by their
**source file name** (not the map).

---

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 1 | Register the SDK client in DI (options + auth + base-URL). | `AddPayPalServerSdkClient` |
| 2 | Authorize an order by direct card (one call, hold not capture). | `Orders.CreateOrder` (primary) / `Orders.AuthorizeOrder` (two-step fallback) |
| 3 | Idempotency on every write. | `payPalRequestId` param on all create/authorize/capture/refund/void/vault ops |
| 4 | Capture an authorization at fulfilment. | `Payments.CaptureAuthorizedPayment` |
| 5 | Re-authorize a stale authorization. | `Payments.ReauthorizePayment` (+ `Payments.GetAuthorizedPayment` to inspect state) |
| 6 | Void an authorization before fulfilment. | `Payments.VoidPayment` |
| 7 | Refund a captured payment (full/partial). | `Payments.RefundCapturedPayment` |
| 8 | Vault cards, list/get/delete, pay by stored card. | `Vault.CreatePaymentToken` / `CreateSetupToken` / `ListCustomerPaymentTokens` / `GetPaymentToken` / `DeletePaymentToken`; pay via `Orders.CreateOrder` with `payment_source.card.vault_id` |
| 9 | Reconciliation / transaction search with full pagination. | `TransactionSearch.SearchTransactions` |

A capability the map lacks is recorded in §5 (Blockers), not worked around.

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier.
> The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one
> from that type's own map row, never from where a neighbouring type sits. Enums, unions, auth,
> server and client-config types are spread across different child namespaces, and two types
> configured side by side in the same options object routinely live in different ones. Dropping a
> type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build
> breaks.

### 2.0 Namespaces (add a `using` per kind — child namespaces do NOT import transitively)

| Type(s) | Namespace | Source |
|---|---|---|
| `PayPalServerSdkClient`, `PayPalServerSdkClientOptions`, `ServerOptions`, `AddPayPalServerSdkClient` | `PayPalServerSdk` | sdk-map.md; ServiceCollectionExtensions.cs |
| `ServerEnvironment`, `DefaultOptions` (+ nested `SandboxOptions`) | `PayPalServerSdk.Servers` | Servers/ServerEnvironment.cs, Servers/DefaultOptions.cs |
| `OAuth2ClientCredentials` | `PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials` | Core/Authentication/OAuth2/ClientCredentials/OAuth2ClientCredentials.cs |
| Controllers `Orders`,`Payments`,`Vault`,`TransactionSearch` | `PayPalServerSdk.Api` | sdk-map.md |
| All request/response records + error payloads `Error`,`Error1`,`DefaultError` | `PayPalServerSdk.Models` | sdk-map.md; Models/Error.cs |
| All enums (`CheckoutPaymentIntent`, `AuthorizationStatus`, …) | `PayPalServerSdk.Models.Enums` | map/models/enums.md |
| `{Operation}Error` classes (`CreateOrderError`, …) | `PayPalServerSdk.Errors` | sdk-map.md |
| `SdkException<T>` | `PayPalServerSdk.Core.Exceptions` | Core/Exceptions/SdkException.cs |
| `RawError`, `ApiError` | `PayPalServerSdk.Core.ErrorResponse` | Core/ErrorResponse/RawError.cs |

### 2.1 Client construction, auth, environment, base-URL (capability 1)

Constructor: `new PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)`.
Every API group is a property on the client (`client.Orders`, `client.Payments`, `client.Vault`,
`client.TransactionSearch`).

`PayPalServerSdkClientOptions` members (source: PayPalServerSdkClientOptions.cs / sdk-map.md):
`Environment: ServerEnvironment`, `Server: ServerOptions`, `Oauth2: OAuth2ClientCredentials?`,
`Oauth2TokenStrategy: IOAuth2TokenStrategy<OAuth2ClientCredentials>?`, `Retry: RetryOptions`,
`Logging: LoggingOptions`.

| Concern | Fact | Source |
|---|---|---|
| OAuth2 client-credentials | Set `options.Oauth2 = new OAuth2ClientCredentials { ClientId = <id>, ClientSecret = <secret> }`. `ClientId` and `ClientSecret` are `required`; `Scope` is optional (`string?`). The SDK fetches/refreshes the bearer token itself at `/v1/oauth2/token` via the default token strategy. | OAuth2ClientCredentials.cs; AuthSchemes.cs |
| Sandbox vs Production | **There is NO Production environment member.** `ServerEnvironment` exposes only `ServerEnvironment.Sandbox` (default). `Environment.Match` throws for any other value. Production is selected ONLY by overriding the base URL (next row) — see Blocker B1. | Servers/ServerEnvironment.cs |
| Base-URL override (verbatim, used for EVERY call incl. token) | Set `options.Server.Default.Sandbox.BaseUrl = <configured BaseUrl>`. Chain: `ServerOptions.Default` (`DefaultOptions`) → `.Sandbox` (`SandboxOptions`) → `.BaseUrl` (`string`, default `https://api-m.sandbox.paypal.com`). The auth scheme resolves the token endpoint through the SAME server (`server.Default("/v1/oauth2/token")`), so overriding `BaseUrl` redirects the OAuth2 token request too. For production use `https://api-m.paypal.com`. | ServerOptions.cs; Servers/DefaultOptions.cs; AuthSchemes.cs |
| DI registration (ASP.NET Core) | `services.AddPayPalServerSdkClient(o => { o.Oauth2 = …; o.Server.Default.Sandbox.BaseUrl = …; });`. It calls `services.AddHttpClient()` and registers `PayPalServerSdkClient` as a **Singleton**, building it from an `IHttpClientFactory`-created `HttpClient`. So the `HttpClient` is factory-owned and long-lived; do not new-up or dispose one per request. | ServiceCollectionExtensions.cs |
| HttpClient ownership/lifetime | Client is a Singleton over a factory `HttpClient` — reuse it; never rebuild per request. (Load `dotnet-client-initialization` — see trap T1.) | ServiceCollectionExtensions.cs |

Config binding: read `PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:BaseUrl`, `PayPal:Currency`
by their binding keys (not raw env-var names). `PayPal:BaseUrl` — if unset, leave the SDK default
(`https://api-m.sandbox.paypal.com`). `PayPal:Currency` feeds every `currency_code` below.

### 2.2 Operation rows

Cross-cutting: every write op below has trailing `string? prefer = "return=minimal"`. Under
`return=minimal` PayPal returns a reduced body (id/status/links, sometimes 204) — to read the
richer fields (authorization objects, `seller_receivable_breakdown`, void/refund details) pass
`prefer: "return=representation"`. See trap T3.

| Capability | Controller.Op · signature (params in order) | Request model + fields | Response envelope → fields to read | Error case + accessors | Source |
|---|---|---|---|---|---|
| 2. Authorize by card (one call) | `client.Orders.CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — pass `null` for the 5 leading header params except `payPalRequestId` (idempotency); pass `prefer: "return=representation"`. | `OrderRequest`: `Intent (intent): CheckoutPaymentIntent !req` = `CheckoutPaymentIntent.Authorize`, `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest> !req`, `PaymentSource (payment_source): PaymentSource?`, `Payer?`, `ApplicationContext?`. `PurchaseUnitRequest`: `Amount (amount): AmountWithBreakdown !req` (else optional). `AmountWithBreakdown`: `CurrencyCode (currency_code): string !req`, `Value (value): string !req`, `Breakdown?`. `PaymentSource`: set `Card (card): CardRequest?`. `CardRequest`: `Number (number): string?`, `Expiry (expiry): string?`, `SecurityCode (security_code): string?`, `Name?`, `BillingAddress?`, `VaultId (vault_id): string?`. | Returns `Order`: `Id (id): string?` = order id; `Status (status): OrderStatus?`; `PurchaseUnits[].Payments (payments): PaymentCollection?` → `Authorizations (authorizations): IReadOnlyList<AuthorizationWithAdditionalData>?` → `[0].Id` = authorization id, `[0].Status (status): AuthorizationStatus?`, `[0].ExpirationTime (expiration_time): string?`. **3DS/challenge signal:** `Order.Status == OrderStatus.PayerActionRequired` → STOP and report (a HATEOAS `payer-action` link accompanies it — UNVERIFIED wire detail, do not depend on the exact rel string). Success: `Order.Status == OrderStatus.Completed` and authorization `Status == AuthorizationStatus.Created`. | Case A `SdkException<CreateOrderError>`: `TryGetError(out Error)` [400,401,422] · `TryGetRawError(out RawError)`. `Error`: `Name`,`Message`,`DebugId`,`Details`,`Links`. | operations/Orders.md; records-1-Ac-Pa.md; records-2-Pa-Ve.md; enums.md |
| 2. (fallback two-step) | `client.Orders.AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — call after `CreateOrder` returns an order id; pass `prefer: "return=representation"`. | `OrderAuthorizeRequest`: `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` → `Card (card): CardRequest?` (or `Token?`). | Returns `OrderAuthorizeResponse`: `Id`, `Status (status): OrderStatus?`, `PurchaseUnits[].Payments.Authorizations[0]` → `.Id`, `.Status`, `.ExpirationTime` (same `AuthorizationWithAdditionalData` shape). | Case A `SdkException<AuthorizeOrderError>`: `TryGetError(out Error)` [400,401,403,404,422,500] · `TryGetRawError`. | operations/Orders.md; records-1-Ac-Pa.md |
| 4. Capture authorization | `client.Payments.CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — pass `prefer: "return=representation"` to read the breakdown. | `CaptureRequest` (all optional): `Amount (amount): Money?` (omit → full capture of the authorized amount), `FinalCapture (final_capture): bool? = false`, `InvoiceId?`, `NoteToPayer?`, `SoftDescriptor?`, `PaymentInstruction?`. For a normal single capture pass `body: null` or `FinalCapture = true`. | Returns `CapturedPayment`: `Id (id): string?`, `Status (status): CaptureStatus?`, `Amount (amount): Money?`, `SellerReceivableBreakdown (seller_receivable_breakdown): SellerReceivableBreakdown?` → `GrossAmount (gross_amount): Money !req` (captured amount), `PaypalFee (paypal_fee): Money?` (fee), `NetAmount (net_amount): Money?` (net proceeds). Read each `Money.Value` (string) + `Money.CurrencyCode`. | Case A `SdkException<CaptureAuthorizedPaymentError>`: `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`. | operations/Payments.md; records-1-Ac-Pa.md; records-2-Pa-Ve.md |
| 5. Re-authorize | `client.Payments.ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `ReauthorizeRequest`: `Amount (amount): Money?` (only supported field per op Notes; omit to reauthorize the original amount). | Returns `PaymentAuthorization`: `Id`, `Status (status): AuthorizationStatus?`, `Amount?`, `ExpirationTime (expiration_time): string?` (new 3-day honor period). | Case A `SdkException<ReauthorizePaymentError>`: `TryGetError(out Error)` [400,401,403,404,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError`. | operations/Payments.md; records-2-Pa-Ve.md; enums.md |
| 5. (inspect state first) | `client.Payments.GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)` | none (GET) | Returns `PaymentAuthorization`: `Status (status): AuthorizationStatus?`, `ExpirationTime`. Use to decide renew vs void vs re-create. | Case A `SdkException<GetAuthorizedPaymentError>`: `TryGetError(out Error)` [401,403,404] · `TryGetNoContent` [500] · `TryGetRawError`. | operations/Payments.md |
| 6. Void authorization | `client.Payments.VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — note param ORDER: `payPalRequestId` is 4th here. | none (no body) | Returns `PaymentAuthorization`. **Success signal = the 2xx (no `SdkException`).** Under default `return=minimal` PayPal returns 204/empty, so fields may be null; pass `prefer: "return=representation"` if you need to confirm `Status == AuthorizationStatus.Voided`. | Case A `SdkException<VoidPaymentError>`: `TryGetError(out Error)` [401,403,404,409,422] · `TryGetNoContent` [500] · `TryGetRawError`. | operations/Payments.md; enums.md |
| 7. Refund capture | `client.Payments.RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` — caller idempotency key → `payPalRequestId`; pass `prefer: "return=representation"` to read the refund back. | **Full refund:** `body: null` (empty payload). **Partial refund:** `RefundRequest { Amount = new Money { CurrencyCode = <cur>, Value = "12.34" } }`. `RefundRequest` other fields optional: `CustomId?`, `InvoiceId?`, `NoteToPayer?`, `PaymentInstruction?`. | Returns `Refund`: `Id (id): string?` = refund id, `Status (status): RefundStatus?`, `Amount?`, `SellerPayableBreakdown?`. | Case A `SdkException<RefundCapturedPaymentError>`: `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent` [500] · `TryGetRawError`. | operations/Payments.md; records-2-Pa-Ve.md; records-1-Ac-Pa.md |
| 8. Vault card (direct, one step) | `client.Vault.CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `PaymentTokenRequest`: `Customer (customer): Customer?`, `PaymentSource (payment_source): PaymentTokenRequestPaymentSource !req`. `Customer`: `Id (id): string?`, `MerchantCustomerId (merchant_customer_id): string?`. `PaymentTokenRequestPaymentSource`: `Card (card): PaymentTokenRequestCard?` (or `Token?`). `PaymentTokenRequestCard`: `Number`, `Expiry`, `SecurityCode`, `Name`, `Brand (brand): CardBrand?`, `BillingAddress?`. | Returns `PaymentTokenResponse`: `Id (id): string?` = **vault/token id**, `Customer (customer): CustomerResponse?` (`.Id`, `.MerchantCustomerId`), `PaymentSource.Card (card): CardPaymentTokenEntity?` → safe description: `Brand (brand): CardBrand?` + `LastDigits (last_digits): string?` + `Expiry (expiry): string?` (never full PAN). | Case A `SdkException<CreatePaymentTokenError>`: `TryGetError1(out Error1)` [400,403,404,422,500] · `TryGetRawError`. `Error1` same shape as `Error`. | operations/Vault.md; records-2-Pa-Ve.md; records-1-Ac-Pa.md |
| 8. Vault card (setup-token → payment-token) | `client.Vault.CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, …)` then `CreatePaymentToken` with `PaymentTokenRequestPaymentSource.Token`. | `SetupTokenRequest`: `Customer?`, `PaymentSource (payment_source): SetupTokenRequestPaymentSource !req` → `Card (card): SetupTokenRequestCard?` (`Number`,`Expiry`,`SecurityCode`,`Brand?`,`BillingAddress?`,`VerificationMethod?`,`ExperienceContext?`). Then `PaymentTokenRequestPaymentSource.Token = new VaultTokenRequest { Id = <setupTokenId>, Type = VaultTokenRequestType.SetupToken }`. | `CreateSetupToken` → `SetupTokenResponse`: `Id (id): string?` = setup token id, `Status (status): PaymentTokenStatus?`. `CreatePaymentToken` → `PaymentTokenResponse.Id` = final vault id. | `SdkException<CreateSetupTokenError>`: `TryGetError1(out Error1)` [400,403,422,500] · `TryGetRawError`. | operations/Vault.md; records-2-Pa-Ve.md; enums.md |
| 8. List vaulted tokens for a customer | `client.Vault.ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)` — pass `totalRequired: true` to get counts for paging. | query: `customer_id ← customerId`, `page_size ← pageSize`, `page ← page`, `total_required ← totalRequired`. | Returns `CustomerVaultPaymentTokensResponse`: `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>?`, `TotalItems (total_items): int?`, `TotalPages (total_pages): int?`, `Customer?`. Page by incrementing `page` until `page >= TotalPages`. | Case A `SdkException<ListCustomerPaymentTokensError>`: `TryGetError1(out Error1)` [400,403,500] · `TryGetRawError`. | operations/Vault.md; records-1-Ac-Pa.md; records-2-Pa-Ve.md |
| 8. Get one vaulted token | `client.Vault.GetPaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | none | Returns `PaymentTokenResponse` (same shape as create). | `SdkException<GetPaymentTokenError>`: `TryGetError1(out Error1)` [403,404,422,500] · `TryGetRawError`. | operations/Vault.md |
| 8. Delete vaulted token | `client.Vault.DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | none | Returns `void` (Task). Success = 2xx (no `SdkException`). | `SdkException<DeletePaymentTokenError>`: `TryGetError1(out Error1)` [400,403,500] · `TryGetRawError`. | operations/Vault.md |
| 8. Pay (authorize) with a stored card | `client.Orders.CreateOrder(…)` with `OrderRequest.PaymentSource.Card = new CardRequest { VaultId = <paymentTokenId> }` (no PAN). | `CardRequest.VaultId (vault_id): string?` carries the stored card token. **Do NOT use `payment_source.token` for a vaulted card** — the `Token` model's `Type (type): TokenType !req` has ONLY `TokenType.BillingAgreement`, i.e. that path is for PayPal billing agreements, not saved cards. | Same `Order` response as capability 2. | as capability 2 | operations/Orders.md; records-1-Ac-Pa.md; records-2-Pa-Ve.md; enums.md |
| 9. Transaction search (paginated) | `client.TransactionSearch.SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `startDate`/`endDate` required; pass the 8 middle filters `null`; **call with named arguments** (many optionals with no C# default mis-bind positionally). | query: `start_date ← startDate`, `end_date ← endDate`, `page_size ← pageSize`, `page ← page`, `fields ← fields` (keep `"transaction_info"` to populate `transaction_info`). Dates are ISO-8601 strings passed verbatim to the query (PayPal expects a full ISO-8601 date-time with offset, e.g. `2026-01-01T00:00:00-0000` — exact tz format is a PayPal wire requirement, UNVERIFIED from map; format best-effort and surface PayPal's error text if rejected). | Returns `SearchResponse`: `TransactionDetails (transaction_details): IReadOnlyList<TransactionDetails>?`, `Page (page): int?`, `TotalItems?`, `TotalPages (total_pages): int?`. **Pagination:** loop `page = 1..TotalPages` (read `TotalPages` off page 1). Per row: `TransactionDetails[].TransactionInfo (transaction_info): TransactionInformation?` → `TransactionId (transaction_id): string?`, `TransactionStatus (transaction_status): string?`, `TransactionAmount (transaction_amount): Money?`. | **Case B** `SdkException<RawError>` (the ONLY Case-B op): `StatusCode: HttpStatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()`. No typed accessors. | operations/TransactionSearch.md; records-2-Pa-Ve.md |

Idempotency (capability 3): the SDK exposes idempotency as the `payPalRequestId` string parameter,
which the SDK sends as the `PayPal-Request-Id` header. Present on `CreateOrder`, `CaptureOrder`,
`AuthorizeOrder`, `CaptureAuthorizedPayment`, `ReauthorizePayment`, `RefundCapturedPayment`,
`VoidPayment`, `CreatePaymentToken`, `CreateSetupToken`. A caller-supplied idempotency key maps
straight to this parameter (capability 7's key → `payPalRequestId`). Source: operations/Orders.md,
operations/Payments.md, operations/Vault.md.

### 2.3 Enum value tables (exact C# member names → wire values)

| Enum | Members (`CSharpName (WIRE)`) | Source |
|---|---|---|
| `CheckoutPaymentIntent` | `Capture (CAPTURE)`, `Authorize (AUTHORIZE)` | enums.md |
| `OrderStatus` | `Created (CREATED)`, `Saved (SAVED)`, `Approved (APPROVED)`, `Voided (VOIDED)`, `Completed (COMPLETED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)` | enums.md |
| `AuthorizationStatus` | `Created (CREATED)`, `Captured (CAPTURED)`, `Denied (DENIED)`, `PartiallyCaptured (PARTIALLY_CAPTURED)`, `Voided (VOIDED)`, `Pending (PENDING)` | enums.md |
| `CaptureStatus` | `Completed (COMPLETED)`, `Declined (DECLINED)`, `PartiallyRefunded (PARTIALLY_REFUNDED)`, `Pending (PENDING)`, `Refunded (REFUNDED)`, `Failed (FAILED)` | enums.md |
| `RefundStatus` | `Cancelled (CANCELLED)`, `Failed (FAILED)`, `Pending (PENDING)`, `Completed (COMPLETED)` | enums.md |
| `PaymentTokenStatus` | `Created (CREATED)`, `PayerActionRequired (PAYER_ACTION_REQUIRED)`, `Approved (APPROVED)`, `Vaulted (VAULTED)`, `Tokenized (TOKENIZED)` | enums.md |
| `TokenType` | `BillingAgreement (BILLING_AGREEMENT)` — only value | enums.md |
| `VaultTokenRequestType` | `SetupToken (SETUP_TOKEN)` — only value | enums.md |
| `CardBrand` | `Visa (VISA)`, `Mastercard (MASTERCARD)`, `Discover (DISCOVER)`, `Amex (AMEX)`, `Jcb (JCB)`, `Maestro (MAESTRO)`, `Diners (DINERS)`, `Elo (ELO)`, `Rupay (RUPAY)`, `ChinaUnionPay (CHINA_UNION_PAY)`, `Unknown (UNKNOWN)`, … (30 members) | enums.md |
| `DisbursementMode` | `Instant (INSTANT)`, `Delayed (DELAYED)` | enums.md |

**Enums are `StringEnum<T>`, NOT C# enums** — write `CheckoutPaymentIntent.Authorize` (static
member) or `CheckoutPaymentIntent.FromValue("AUTHORIZE")`; never `"AUTHORIZE"` as a bare string
into a typed field. (Load `dotnet-models` — trap T4.)

### 2.4 Re-authorize state semantics (capability 5, actionable operator message)

`AuthorizationStatus` has **no `EXPIRED` member** — staleness is not an enum value. Decide from the
combination of `Status` + `ExpirationTime`:

- **Still valid / capturable now:** `Status == Created` and `ExpirationTime` in the future.
- **Renewable via `ReauthorizePayment`:** `Status == Created` after the 3-day honor period, within
  the 29-day window (op Notes). Reauthorize yields a fresh 3-day honor period.
- **No longer renewable (terminal / consumed):** `Status ∈ { Voided, Captured, PartiallyCaptured,
  Denied }` — re-create a new authorization instead.
- **In flight:** `Status == Pending` (with `StatusDetails.Reason`) — wait, do not reauthorize.
- The definitive "too old to renew (>29/30 days)" decision is enforced server-side; on a
  `ReauthorizePayment` rejection read `Error.Message` + `Error.DebugId` and surface them verbatim
  to the operator (defensive — the exact cutoff is not a field the SDK returns). Source:
  operations/Payments.md Notes; enums.md; records-2-Pa-Ve.md.

### 2.5 Money / amount-to-the-cent (capability requirement)

`Money` / `AmountWithBreakdown` carry `CurrencyCode (currency_code): string !req` and
`Value (value): string !req` — **`Value` is a string**, not a decimal. Format the amount to the
currency's minor units with invariant culture (e.g. USD → `"10.00"`). `CurrencyCode` comes from
`PayPal:Currency`. Number of decimal places is currency-dependent (USD=2, JPY=0); the SDK does not
validate it — YOUR CALL to format per currency. Source: records-1-Ac-Pa.md (`Money`,
`AmountWithBreakdown`).

---

## 3. Trap notes (load the companion; do not resolve inline)

- **T1 · Step 1 (client & DI).** How the SDK client and its `HttpClient` must be owned and scoped
  in DI, and what the SDK client wrapper's own lifetime should be, is not shown by the constructor.
  **MUST load `dotnet-client-initialization`** before wiring the client.
- **T2 · Step 1/2 (auth).** When and where credentials must be set relative to client
  construction, and how the token is fetched/cached/refreshed, is not shown by the options shape.
  **MUST load `dotnet-authentication`** before wiring credentials.
- **T3 · Steps 2,4,6,7 (`prefer` default).** `prefer = "return=minimal"` changes what the response
  body contains — whether the field you need to read is even present depends on it. **MUST load
  `dotnet-calling-endpoints`** before relying on any response field from a write op.
- **T4 · Steps 2,4,5,8 (models/enums/unions).** How enums (`StringEnum<T>`) and payment-source
  variants are constructed and read, and that unmodeled JSON is dropped on deserialize, is not
  shown by the field types. **MUST load `dotnet-models`** before building any request payload.
- **T5 · Steps 3 + all writes (retry vs idempotency).** What the SDK's retry/timeout options
  actually bound, and whether a transport failure can re-send a non-idempotent POST (double
  authorize/capture/refund), is not shown by the option names — this is why `payPalRequestId`
  matters on every write. **MUST load `dotnet-configuration-resilience`** before tuning retries,
  timeouts, base-URL, or pagination.
- **T6 · Step 9 (Case B search).** `SearchTransactions` is the only Case-B op — its error carries
  no typed accessors, and its `JsonException` behaviour differs from the typed ops. **MUST load
  `dotnet-error-handling`** before writing its catch.
- **T7 · Step 8 (vault-card pay path).** Paying with a saved card goes through
  `payment_source.card.vault_id`, never `payment_source.token` (billing-agreement-only). Confirm
  the variant construction in **`dotnet-models`**.

---

## 4. REQUIRED READING (load BEFORE implementation starts — this sheet deliberately omits their contents)

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, HttpClient ownership, DI registration |
| `dotnet-authentication` | Step 1/2 — OAuth2 client-credentials wiring, token lifecycle |
| `dotnet-calling-endpoints` | Steps 2–9 — calling ops, named args, `prefer`/response envelopes |
| `dotnet-models` | Steps 2,4,5,8 — building request models, enums, payment-source variants |
| `dotnet-configuration-resilience` | Step 1/3 — retries, timeouts, base-URL, pagination |
| `dotnet-error-handling` | All steps — the try/catch boundary (Case A vs Case B) |
| `dotnet-testing` | Testing the integration layer (HttpClient seam) |

**JsonException at the error boundary — include and handle BOTH directions (they need opposite handling):**

- A drifted or malformed **2xx** body (a missing `required` member — e.g. `SellerReceivableBreakdown.GrossAmount`,
  `Error.Name/Message/DebugId`, `AmountWithBreakdown.CurrencyCode/Value`) surfaces as a
  `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — an
  SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces**
  the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every
  `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that
  retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions**
- A1. Capability 2's "one call" is implemented as `CreateOrder` with `intent=AUTHORIZE` +
  `payment_source.card`. Whether the live wire populates `purchase_units[].payments.authorizations`
  inline in that single response (vs requiring the two-step `CreateOrder` → `AuthorizeOrder`) is
  **UNVERIFIED** from the map/source — it is live-traffic behaviour. Defensive directive: read
  `Authorizations[0]` best-effort; if it is empty or its `Status != Created`, treat the result as
  "needs the two-step authorize or a challenge" and branch on `Order.Status` (see A2).
- A2. 3DS/challenge detection keys off `Order.Status == OrderStatus.PayerActionRequired`. The
  accompanying HATEOAS `payer-action` link's exact `rel` string is **UNVERIFIED** from the map;
  branch on the status, not the link text.
- A3. `customer_id` scoping: to scope a shopper's cards, pass `PaymentTokenRequest.Customer` with an
  `Id`/`MerchantCustomerId` you control, and reuse that `customer_id` in
  `ListCustomerPaymentTokens`. Whether PayPal mints a `customer.id` when omitted (returned in
  `PaymentTokenResponse.Customer.Id`) and how that first id should be persisted against an eShop
  user is an **application decision** (YOUR CALL — not in the map): capture the returned id and
  store it against the user; the map only guarantees the fields, not the persistence policy.
- A4. Config keys assumed: `PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:BaseUrl`,
  `PayPal:Currency` (binding keys, not raw env vars). Names are this plan's proposal; confirm
  against the app's config convention.

**Blockers**
- B1. **No Production `ServerEnvironment`.** The SDK's `ServerEnvironment` exposes only `Sandbox`
  (source: Servers/ServerEnvironment.cs). "Production vs sandbox" therefore CANNOT be selected via
  `options.Environment`; it can only be reached by overriding `options.Server.Default.Sandbox.BaseUrl`
  to `https://api-m.paypal.com`. This is not a coding blocker (the override works and covers the
  token endpoint too) but it IS a design constraint the caller must accept: environment selection =
  base-URL selection. Flagged so no one expects a `ServerEnvironment.Production` member.

**No other in-scope capability is missing from the SDK.** Capabilities 2–9 all map to concrete
operations above.
