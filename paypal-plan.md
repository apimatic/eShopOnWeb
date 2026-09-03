# PayPal integration plan — eShopOnWeb PublicApi

Add card payments (authorize → capture at fulfilment → void/refund), saved cards (vault), and a
reconciliation report to `src/PublicApi`, additive to the existing catalog/basket/order flow. PayPal is
reached **only** through the APIMatic-generated PayPal .NET SDK (root namespace `PayPal`, client
`PayPalClient`), which is vendored into the repo and built from source (it is not on NuGet).

## 1. Scope & sequence

| Step | What | PayPal operations used |
| --- | --- | --- |
| S0 | Vendor SDK source into `src/PayPalSdk/`; disable CPM in that subtree; add to solution; `Infrastructure` ProjectReferences it. Set `global.json` `rollForward: latestMajor` (SDK is `LangVersion 14` → needs .NET 10 SDK). | — |
| S1 | `PayPalSettings` (`PayPal:` section) bound + fail-fast (`ValidateOnStart`). DI: register `PayPalClient` (singleton over a named `HttpClient`) with OAuth2 creds, `Environment.Production`, optional `BaseUrl` override, explicit `LoggerFactory`, `LogRequestBody=false`. | — |
| S2 | Extend `Order` aggregate (ApplicationCore) with payment state + owned refunds; new `SavedCard` aggregate. EF config + `DbSet`s in `CatalogContext`. | — |
| S3 | `IPayPalPaymentGateway` (ApplicationCore, domain DTOs only) + `PayPalPaymentGateway` impl (Infrastructure, references SDK). Single error boundary + bounded `CancellationToken`. | all below |
| S4 | `POST /api/orders` — create eShop Order (BuyerId = caller), status AwaitingPayment. Returns `orderId`. | — |
| S5 | `POST /api/orders/{id}/pay` — authorize the total (card body **or** saved-card id). | `Orders.CreateOrder` (intent AUTHORIZE, payment_source.card / card.vault_id) → `Orders.AuthorizeOrder` |
| S6 | `POST /api/orders/{id}/fulfil` (admin) — capture; reauthorize if stale. | `Payments.CaptureAuthorizedPayment`; on stale → `Payments.ReauthorizePayment` then capture new auth |
| S7 | `POST /api/orders/{id}/cancel` (admin) — void the hold before fulfilment. | `Payments.VoidPayment` |
| S8 | `POST /api/orders/{id}/refunds` — refund full/partial, caller idempotency key, cap ≤ captured. Returns `refundId`. | `Payments.RefundCapturedPayment` |
| S9 | `GET /api/my-orders` — caller's orders + payment state. | — |
| S10 | `GET /api/reconciliation?from&to` (admin) — all pages, line up vs eShop orders. | `TransactionSearch.SearchTransactions` (manual pagination over `total_pages`) |
| S11 | `POST /api/payment-methods` — vault a card. Returns `paymentMethodId`. | `Vault.CreateSetupToken` (card) → `Vault.CreatePaymentToken` (token=SETUP_TOKEN) |
| S12 | `GET /api/payment-methods` (caller) + `DELETE /api/payment-methods/{id}` (caller, owner-checked). | `Vault.DeletePaymentToken` |
| S13 | Tests (domain state machine + gateway seam faked) and end-to-end self-verification against sandbox. | — |

**Auth roles** (from the spec's explicit rule): admin (`Roles.ADMINISTRATORS` = "Administrators") on **fulfil,
cancel, reconciliation** only; every other endpoint is shopper-scoped, acting only on the caller's own data
(BuyerId == `ClaimTypes.Name`). Refunds is therefore shopper/owner-checked, not admin.

## 2. CONTRACT SHEET

> ⚠ Signatures below are generated code, **verbatim** — every parameter name is the literal C# identifier;
> named arguments must use them exactly (the cancellation-token parameter is literally `ct`, so write `ct:`).
> ⚠ Every SDK type is written fully-qualified with the namespace its source path implies (`Models/` →
> `PayPal.Models`, `Models/Enums/` → `PayPal.Models.Enums`, `Errors/` → `PayPal.Errors`, client/options →
> `PayPal`, `ServerEnvironment` → `PayPal.Servers`). Take each type's namespace from its own path, never a
> neighbour's.

### Operations

| Op (`client.X`) | Signature (verbatim) | Request model → fields used | Response → fields read | Error case + accessors | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| `Orders.CreateOrder` | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `OrderRequest`: `Intent (intent): CheckoutPaymentIntent` **req**; `PurchaseUnits (purchase_units): IReadOnlyList<PurchaseUnitRequest>` **req**; `PaymentSource (payment_source): PaymentSource?` | `Order`: `Id (id)`, `Status (status): OrderStatus`, `PurchaseUnits[].Payments (payments): PaymentCollection` → `Authorizations[]` (defensive) | Case A `SdkException<CreateOrderError>`: `TryGetError(out Error)` [400,401,422] · `TryGetRawError(out RawError)` | none | map/operations/Orders.md; Models/OrderRequest.cs |
| `Orders.AuthorizeOrder` | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `OrderAuthorizeRequest`: `PaymentSource (payment_source): OrderAuthorizeRequestPaymentSource?` — pass `body: null` (card already on the created order) | `OrderAuthorizeResponse` → `PurchaseUnits[].Payments.Authorizations[]` (`AuthorizationWithAdditionalData`): `Id`, `Status (AuthorizationStatus)`, `ExpirationTime`, `Amount` | Case A `SdkException<AuthorizeOrderError>`: `TryGetError(out Error)` [400,401,403,404,422,500] · `TryGetRawError` | none | Orders.md; Models/OrderAuthorizeResponse.cs |
| `Payments.CaptureAuthorizedPayment` | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `CaptureRequest`: `Amount (amount): Money?`, `FinalCapture (final_capture): bool? = false`, `InvoiceId (invoice_id): string?` | `CapturedPayment`: `Id`, `Status (CaptureStatus)`, `Amount (Money)`, `SellerReceivableBreakdown` → `GrossAmount` **req**, `PaypalFee?`, `NetAmount?` (each `Money`) | Case A `SdkException<CaptureAuthorizedPaymentError>`: `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | none | map/operations/Payments.md; Models/CapturedPayment.cs, Models/SellerReceivableBreakdown.cs |
| `Payments.ReauthorizePayment` | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `ReauthorizeRequest`: `Amount (amount): Money?` | `PaymentAuthorization`: `Id`, `Status (AuthorizationStatus)`, `ExpirationTime`, `Amount` | Case A `SdkException<ReauthorizePaymentError>`: `TryGetError(out Error)` [400,401,403,404,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | none | Payments.md; Models/PaymentAuthorization.cs |
| `Payments.VoidPayment` | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | *(no body)* | `PaymentAuthorization`: `Status (AuthorizationStatus)` (expect `Voided`) | Case A `SdkException<VoidPaymentError>`: `TryGetError(out Error)` [401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | none | Payments.md |
| `Payments.RefundCapturedPayment` | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)` | `RefundRequest`: `Amount (amount): Money?` (null ⇒ full), `InvoiceId?`, `CustomId?` | `Refund`: `Id`, `Status (RefundStatus)`, `Amount (Money)` | Case A `SdkException<RefundCapturedPaymentError>`: `TryGetError(out Error)` [400,401,403,404,409,422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError` | none | Payments.md; Models/RefundRequest.cs, Models/Refund.cs |
| `Vault.CreateSetupToken` | `CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `SetupTokenRequest`: `PaymentSource (payment_source): SetupTokenRequestPaymentSource` **req** → `Card (card): SetupTokenRequestCard?` (`Number`,`Expiry`,`SecurityCode`,`Name`,`BillingAddress`); `Customer (customer): Customer?` | `SetupTokenResponse`: `Id`, `Status (PaymentTokenStatus)` | Case A `SdkException<CreateSetupTokenError>`: `TryGetError(out Error)` [400,403,422,500] · `TryGetRawError` | none | map/operations/Vault.md; Models/SetupTokenRequest.cs |
| `Vault.CreatePaymentToken` | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `PaymentTokenRequest`: `PaymentSource (payment_source): PaymentTokenRequestPaymentSource` **req** → `Token (token): VaultTokenRequest?` (`Id` **req**=setup-token id, `Type` **req**=`VaultTokenRequestType.SetupToken`); `Customer (customer): Customer?` | `PaymentTokenResponse`: `Id` (= vault id), `PaymentSource.Card (CardPaymentTokenEntity)` → `LastDigits`, `Brand (CardBrand)`, `Expiry`, `Name` | Case A `SdkException<CreatePaymentTokenError>`: `TryGetError(out Error)` [400,403,404,422,500] · `TryGetRawError` | none | Vault.md; Models/PaymentTokenRequest.cs, Models/PaymentTokenResponse.cs |
| `Vault.DeletePaymentToken` | `DeletePaymentToken(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | `void` (Task) | Case A `SdkException<DeletePaymentTokenError>`: `TryGetError(out Error)` [400,403,500] · `TryGetRawError` | none | Vault.md |
| `Vault.ListCustomerPaymentTokens` | `ListCustomerPaymentTokens(string customerId, int? pageSize = 5, int? page = 1, bool? totalRequired = false, RequestOptions? requestOptions = null, CancellationToken ct = default)` | query: `customer_id`,`page_size`,`page`,`total_required` | `CustomerVaultPaymentTokensResponse`: `PaymentTokens (payment_tokens): IReadOnlyList<PaymentTokenResponse>`, `TotalPages` | Case A `SdkException<ListCustomerPaymentTokensError>`: `TryGetError(out Error)` [400,403,500] · `TryGetRawError` | page-number (`page`/`page_size`, drive manually) | Vault.md |
| `TransactionSearch.SearchTransactions` | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)` | query wire: `start_date`,`end_date`,`page`,`page_size`,`fields` | `SearchResponse`: `TransactionDetails[]` → `TransactionInfo (TransactionInformation)`: `TransactionId`, `TransactionStatus` (raw `string` D/P/S/V), `TransactionAmount (Money)`, `TransactionInitiationDate`, `InvoiceId`; `TotalPages`, `Page` | **Case B** `SdkException<RawError>` (no typed accessors — read `StatusCode`/`ReadAsString()`) | page-number: response carries `TotalPages`/`Page`; **drive page 1..TotalPages manually with a page cap** | map/operations/TransactionSearch.md; Models/SearchResponse.cs, Models/TransactionInformation.cs |

### Nested request models (build these)

- `PurchaseUnitRequest`: `Amount (amount): AmountWithBreakdown` **req**; `InvoiceId (invoice_id): string?`; `CustomId (custom_id): string?`; `ReferenceId?`. `AmountWithBreakdown` builds like `Money` (has `CurrencyCode`+`Value`, both required, plus optional breakdown). Source: Models/PurchaseUnitRequest.cs, Models/AmountWithBreakdown.cs.
- `Money`: `CurrencyCode (currency_code): string` **req**, `Value (value): string` **req**. Source: Models/Money.cs.
- `PaymentSource`: `Card (card): CardRequest?`. `CardRequest`: `Number (number)`, `Expiry (expiry)` (`YYYY-MM`), `SecurityCode (security_code)`, `Name (name)`, `BillingAddress (billing_address): Address?`, **`VaultId (vault_id): string?`** (charge a saved card). Source: Models/PaymentSource.cs, Models/CardRequest.cs.
- `Address` (billing): read fields from Models/Address.cs at build time (address_line_1, admin_area_1/2, postal_code, country_code) — pass a minimal valid US address for the sandbox test card.

### Enums (Models/Enums)

- `CheckoutPaymentIntent`: `Authorize = "AUTHORIZE"`, `Capture = "CAPTURE"`.
- `VaultTokenRequestType`: `SetupToken = "SETUP_TOKEN"`.
- `AuthorizationStatus`: `Created`,`Captured`,`Denied`,`PartiallyCaptured`,`Voided`,`Pending` (no EXPIRED member — staleness is via `ExpirationTime` timestamp or a 422 issue on capture).
- `CaptureStatus`: `Completed`,`Declined`,`PartiallyRefunded`,`Pending`,`Refunded`,`Failed`.
- `RefundStatus`: `Cancelled`,`Failed`,`Pending`,`Completed`.
- `CardBrand`: read back `.Value` for display (e.g. `Visa="VISA"`).
- Read `.Value` (never `ToString()`) when persisting a status string.

### Client construction / auth / servers

- Client: `new PayPal.PayPalClient(HttpClient, PayPal.PayPalClientOptions)`. Groups: `client.Orders`, `client.Payments`, `client.Vault`, `client.TransactionSearch`.
- Auth: `options.Oauth2 = new PayPal.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials { ClientId=…, ClientSecret=… }`. Client-credentials grant; token cached in-memory per client, re-fetched on expiry / `401`.
- Env: **only** `PayPal.Servers.ServerEnvironment.Production` exists, and it already targets `https://api-m.sandbox.paypal.com`. Map config `sandbox`→`Production`. BaseUrl override point: `options.Server.Default.Production.BaseUrl`. **Verified in source**: the OAuth token endpoint resolves through the same `server.Default("/v1/oauth2/token")` (AuthSchemes.cs), so a `BaseUrl` override applies to the token request too — satisfies the mandate.
- DI extension `services.AddPayPalClient(...)` exists but binds the shared unnamed `HttpClient`; we register over a **named** `HttpClient` instead for isolated timeout/pool + explicit `LoggerFactory`.

## 3. Trap notes (do not resolve here — load the named skill at that step)

- **S1 client lifetime / DNS staleness on a long-lived singleton, and the shared-vs-named `HttpClient` blast radius.** `MUST load dotnet-client-initialization`.
- **S1 what `options.Retry.Timeout` actually bounds vs. a whole-call budget, and how `BaseUrl`/environment are (not) re-read after construction.** `MUST load dotnet-configuration-resilience`.
- **S1/S3 unredacted body logging: `LogRequestBody` and the `PAYPALCLIENT_LOG` env var can print card PANs; form/JSON redaction differs.** `MUST load dotnet-configuration-resilience`.
- **S3 which exception types actually reach the catch (typed vs `RawError` vs transport vs `JsonException` from a 2xx or an error-shape mismatch), and why `TryGetRawError` goes last.** `MUST load dotnet-error-handling`.
- **S5/S8 idempotency: which parameter is the *real* caller key vs the injected `Idempotency-Key` header; and that a `POST` is never resent by the SDK, leaving an unknown-outcome to reconcile.** `MUST load dotnet-configuration-resilience`.
- **S5/S11 building nested request records, enums are `StringEnum<T>` not C# enums (use static members / `.Value`), unions read via `TryGet…`.** `MUST load dotnet-models`.
- **S10 never leave the page loop bounded only by the provider's "next page" — needs a page cap.** `MUST load dotnet-configuration-resilience`.
- **S13 which seam to fake (the `HttpClient`, or our own `IPayPalPaymentGateway`), asserting behaviour not execution.** `MUST load dotnet-testing`.

## 4. REQUIRED READING (load before implementation; sheet deliberately omits their contents)

- `dotnet-client-initialization` — S1 client/DI construction & lifetime.
- `dotnet-authentication` — S1 OAuth2 client-credentials + startup fail-fast.
- `dotnet-calling-endpoints` — S5–S12 every call (named args, `ct:`, body vs params).
- `dotnet-models` — S5/S8/S11 request records, enums, unions, money strings.
- `dotnet-error-handling` — S3 error boundary (always required).
- `dotnet-configuration-resilience` — S1/S5/S8/S10 retries, timeouts, base URL, pagination, logging/redaction.
- `dotnet-testing` — S13 tests.

⚠ Two hazard rows to hold regardless (both surface `System.Text.Json.JsonException`, opposite handling): a
drifted/malformed **2xx** body throws `JsonException` from deserialization — **not** an `SdkException` — so an
SDK-only catch ladder lets it escape (treat as "outcome unknown"); a **non-2xx** body that does not match the
operation's generated `{Operation}Error` throws `JsonException` *while the error object is being constructed*,
**replacing** the `SdkException` and destroying the HTTP status (treat as the rejection it is). The gateway
boundary catches `JsonException` explicitly and maps to a caller-safe message.

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `PayPalSettings` bound from `PayPal:` with `[Required]` on `ClientId`, `ClientSecret`, `Currency`; `.AddOptions<PayPalSettings>().Bind(section).ValidateDataAnnotations().ValidateOnStart()` in Program.cs so the host refuses to start on any missing/blank part. Each credential half checked independently (blank ≠ missing). Message names the key, never echoes the value. |
| 2 | Secret sourcing & rotation | Values come from env vars `PAYPAL_CLIENT_ID`/`PAYPAL_CLIENT_SECRET`/`PAYPAL_ENVIRONMENT`/`PAYPAL_CURRENCY`, loaded by me into **.NET user-secrets** for the PublicApi project (existing `UserSecretsId`) as `PayPal:ClientId` etc. Never written into any repo file. DI builds the options+client **once at registration** and captures it in the singleton, so a rotated secret needs a process restart — acceptable and documented. |
| 3 | Total timeout budget | A `Bounded(...)` helper in the gateway links `HttpContext.RequestAborted` and applies a per-call budget (config `PayPal:TimeoutSeconds`, default 30s) via a linked `CancellationTokenSource` — the only thing that bounds a whole call. `options.Retry.Timeout` set to 15s (per-attempt, not total) and `HttpClient.Timeout` 20s as backstops. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = `GET,HEAD,PUT,OPTIONS`. All our writes are `POST` (create/authorize/capture/reauthorize/void/refund) or `DELETE` (vault delete) → **never resent by the SDK**. No `PUT` in scope. We keep the SDK default; do not widen the list. |
| 5 | Idempotency & ambiguous writes | Authorize/capture use a **deterministic** `payPalRequestId` (`PayPal-Request-Id`) derived from the eShop order id + operation (e.g. `eshop-auth-{orderId}`, `eshop-cap-{orderId}`) **plus** a local status guard (skip if already Authorized/Fulfilled) so a double-click never authorizes/captures twice. Refunds take the **caller-supplied** idempotency key → mapped to `payPalRequestId`, **and** recorded per order: a repeat under the same key returns the prior `refundId`; two distinct keys → two partial refunds. A transport failure on any `POST` leaves an unknown outcome → reconciled via `GET /api/reconciliation` (S10). Refund cap enforced locally: `sum(refunds)+new ≤ captured`. |
| 6 | Observability | Built-in SDK logger wired to the host `ILoggerFactory` at `Information` (request line/status). Gateway logs at `Information` (op + eShop order id + PayPal ids) and `Error` on failure with the PayPal `Error` body's `debug_id`/issue read from the typed `Error` model. `LogRequestBody` stays **off**. No card data ever logged. |
| 7 | Sensitive data | Card PAN/CVV appear in `CardRequest`/`SetupTokenRequestCard`/`PaymentTokenRequestCard` **request** bodies. Therefore: `options.Logging.LogRequestBody=false` **and** `options.Logging.LoggerFactory` assigned explicitly (from DI) so `PAYPALCLIENT_LOG` cannot switch body logging on from outside. We never store the PAN — only PayPal's vault id + safe display (brand/last4/expiry). Our own diagnostics never echo a request body or card field. |
| 8 | Environment selection | One server group `Default`; one environment `Production` → `https://api-m.sandbox.paypal.com` (PayPal sandbox). All dev/test traffic is sandbox by construction. Config `PayPal:Environment=sandbox`→`Production`. To ever target live PayPal the operator must set `PayPal:BaseUrl` to the live host (used verbatim for every call incl. token). We set `Environment=Production` always and apply `BaseUrl` only when configured. |

## 6. Assumptions & Blockers

- **No blockers.** Every capability the task needs maps to an SDK operation above.
- Assumption (spec authorization rule): "Fulfil, cancel and reconciliation are operator actions… every other
  endpoint is shopper-scoped and acts only on the caller's own data" is authoritative over the intro prose, so
  **refunds is shopper/owner-scoped**, not admin. Minor — proceeding.
- Assumption (direct-card auth): the advanced-card flow is `CreateOrder(intent=AUTHORIZE, payment_source.card)`
  then `AuthorizeOrder(id, body:null)`. If PayPal answers with a browser challenge
  (`PAYER_ACTION_REQUIRED`/an `approve` link), that is the task's STOP-and-report case → surfaced as a clear
  error, not a built approval round-trip. Sandbox test card `4111…` is not expected to challenge.
- Assumption (saved-card ownership): eShop persists a `SavedCard{BuyerId, PayPalVaultId, safe display}` as the
  ownership index; PayPal holds the actual card. List/delete/pay are all scoped by `BuyerId`, so one shopper
  cannot see/use/delete another's, and a deleted card is neither listed nor usable. `Customer.id` on the
  vault is derived deterministically from the buyer to group tokens PayPal-side.
- Assumption (reconciliation matching): capture requests set `invoice_id = ESHOP-{orderId}` so
  `SearchTransactions` rows line up against eShop orders by invoice id (and capture id). Empty results over a
  just-created range are expected sandbox reporting lag, not a gap.
- Assumption (build): `global.json` → `rollForward: latestMajor` so the .NET 10 SDK compiles the SDK's
  `LangVersion 14`; net8.0 app projects still target net8.0.
