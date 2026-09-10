# PayPal Server SDK integration plan — eShopOnWeb PublicApi

Additive PayPal card-payments + saved-cards capability on `src/PublicApi`. Reuses the existing
`Order`/`OrderItem` aggregate; adds payment/fulfilment state + saved cards as new entities. All PayPal
traffic goes through the vendored PayPal Server SDK (.NET). No storefront UI.

## 1. Scope & sequence

The SDK is **not on NuGet** → it is **vendored** into the repo at `src/PayPalServerSdk/` (a plain copy of the
generated source, `.git`/`map`/`*.md` stripped) with `<ManagePackageVersionsCentrally>false</...>` in its
csproj so its inline package versions survive this repo's Central Package Management. Referenced by
`Infrastructure.csproj` via `ProjectReference`. `global.json` `rollForward` → `latestMajor` (SDK 8.0.x pinned,
only .NET 10 installed).

| # | Step | PayPal operations used |
| --- | --- | --- |
| 1 | Vendor SDK, wire `global.json`, add project refs | — |
| 2 | Domain: `OrderPayment` (aggregate, 1:1 with `Order` by `OrderId`), `PaymentRefund` (child), `SavedPaymentMethod` (aggregate); `PaymentStatus` enum; EF configs; add DbSets to `CatalogContext` | — |
| 3 | `IPaymentGateway` (ApplicationCore, SDK-free DTOs) + `PayPalPaymentGateway` (Infrastructure, wraps SDK) | all below |
| 4 | `PayPalOptions` bind + fail-fast + `AddPayPalIntegration` DI extension (Infrastructure); SDK client registration | client/token |
| 5 | App services: `OrderPaymentService`, `SavedCardService` (ApplicationCore) orchestrating repos + gateway | — |
| 6 | PublicApi endpoints (MinimalApi.Endpoint `IEndpoint` pattern) under `OrderEndpoints/`, `PaymentMethodEndpoints/`, `ReconciliationEndpoints/` | — |
| 7 | Load secrets into user-secrets; run; self-verify live sandbox; tests | — |

Endpoint → operation map:
- `POST /api/orders` (shopper): create `Order`+`OrderPayment(AwaitingPayment)`; **no** PayPal call. → `orderId`.
- `POST /api/orders/{orderId}/pay` (shopper): `CreateOrder`(intent=AUTHORIZE, payment_source.card = one-off card **or** vault_id) → `AuthorizeOrder`. Hold = order total.
- `POST /api/orders/{orderId}/fulfil` (admin): `CaptureAuthorizedPayment`; on stale auth → `ReauthorizePayment` then capture; if not renewable → operator-actionable error.
- `POST /api/orders/{orderId}/cancel` (admin): `VoidPayment` (release hold).
- `POST /api/orders/{orderId}/refunds` (shopper, own order): `RefundCapturedPayment` (full/partial). → `refundId`.
- `GET /api/my-orders` (shopper): own orders + payment state (no PayPal call; reads persisted state).
- `GET /api/reconciliation?from&to` (admin): `SearchTransactions` across the whole range (≤31-day windows × all pages), lined up against `OrderPayment` rows.
- `POST /api/payment-methods` (shopper): `CreateSetupToken` → `CreatePaymentToken`. → `paymentMethodId`.
- `GET /api/payment-methods` (shopper): own saved cards (persisted).
- `DELETE /api/payment-methods/{paymentMethodId}` (shopper, own): `DeletePaymentToken` + remove local row.

Caller identity = JWT `ClaimTypes.Name` (username/email; the token carries only Name + Role — verified in
`IdentityTokenClaimService`). Used as `Order.BuyerId` and the owner key on `OrderPayment`/`SavedPaymentMethod`.
Admin = role `Administrators` (`BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS`).

## 2. CONTRACT SHEET

> ⚠ **Signatures below are generated code, verbatim.** Every parameter name is the literal C# identifier;
> the cancellation-token parameter really is named `ct`, so named arguments write `ct:`. Nullable-no-default
> params (`payPalMockResponse`, `payPalRequestId`, `payPalAuthAssertion`, `payPalClientMetadataId`, `body`,
> `fields`, …) **must be passed explicitly** — pass `null` to skip.
> ⚠ **Every SDK type is written fully-qualified with the namespace its source path implies** (`Models/` →
> `PayPalServerSdk.Models`; `Models/Enums/` → `PayPalServerSdk.Models.Enums`; `Errors/` →
> `PayPalServerSdk.Errors`; client/options/servers → `PayPalServerSdk` / `PayPalServerSdk.Servers`), taken
> from the path the map gives for THAT type.

Accessors: `client.Orders`, `client.Payments`, `client.Vault`, `client.TransactionSearch`.

| Op | Signature (verbatim) · request fields used · response fields read · error case · source |
| --- | --- |
| **Orders.CreateOrder** | `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`. **Req** `OrderRequest{ Intent(intent)=CheckoutPaymentIntent.Authorize [req], PurchaseUnits(purchase_units)=[PurchaseUnitRequest{ Amount(amount)=AmountWithBreakdown{ CurrencyCode, Value } [req], ReferenceId="default", InvoiceId, CustomId }] [req], PaymentSource(payment_source)=PaymentSource{ Card=CardRequest{ Number, Expiry("YYYY-MM"), SecurityCode, Name, BillingAddress } OR CardRequest{ VaultId } } }`. Pass `prefer:"return=representation"`, `payPalRequestId:` idem key. **Resp** `Order{ Id(id), Status(status)=OrderStatus, PurchaseUnits, Links }`. **Error** Case A `SdkException<CreateOrderError>` — `TryGetError(out Error)`[400,401,422] · `TryGetRawError`. src `map/operations/Orders.md`; `Models/OrderRequest.cs`, `Models/PaymentSource.cs`, `Models/CardRequest.cs`, `Models/AmountWithBreakdown.cs`, `Models/Order.cs`, `Errors/CreateOrderError.cs`, `Models/Error.cs` |
| **Orders.AuthorizeOrder** | `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", …ct)`. Pass `body:null`, `prefer:"return=representation"`, `payPalRequestId:` idem. **Resp** `OrderAuthorizeResponse{ Id, Status, PurchaseUnits[0].Payments.Authorizations[0] = AuthorizationWithAdditionalData{ Id(id), Status(status)=AuthorizationStatus, Amount, ExpirationTime(expiration_time) } }`. **Error** Case A `AuthorizeOrderError` — `TryGetError`[400,401,403,404,422,500]·`TryGetRawError`. src Orders.md; `Models/OrderAuthorizeResponse.cs`, `Models/PurchaseUnit.cs`, `Models/PaymentCollection.cs`, `Models/AuthorizationWithAdditionalData.cs` |
| **Payments.CaptureAuthorizedPayment** | `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", …ct)`. **Req** `CaptureRequest{ FinalCapture(final_capture)=true, InvoiceId }` (omit `Amount` = full). Pass `prefer:"return=representation"`, `payPalRequestId:` idem. **Resp** `CapturedPayment{ Id(id), Status(status)=CaptureStatus, Amount=Money, SellerReceivableBreakdown{ GrossAmount(gross_amount)=Money[req], PaypalFee(paypal_fee)=Money?, NetAmount(net_amount)=Money? } }`. **Error** Case A `CaptureAuthorizedPaymentError` — `TryGetError`[400,401,403,404,409,422]·`TryGetNoContent(out RawError)`[500]·`TryGetRawError`. src Payments.md; `Models/CaptureRequest.cs`, `Models/CapturedPayment.cs`, `Models/SellerReceivableBreakdown.cs`, `Models/Money.cs` |
| **Payments.ReauthorizePayment** | `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", …ct)`. **Req** `ReauthorizeRequest{ Amount=Money }` (or `body:null`). **Resp** `PaymentAuthorization{ Id(id), Status, Amount, ExpirationTime }`. **Error** Case A `ReauthorizePaymentError` — `TryGetError`[400,401,403,404,422]·`TryGetNoContent`[500]·`TryGetRawError`. src Payments.md; `Models/ReauthorizeRequest.cs`, `Models/PaymentAuthorization.cs` |
| **Payments.GetAuthorizedPayment** | `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, …ct)`. **Resp** `PaymentAuthorization{ Status=AuthorizationStatus, ExpirationTime }` — used to detect staleness before capture. **Error** Case A `GetAuthorizedPaymentError`·`TryGetError`[401,403,404]. src Payments.md; `Models/PaymentAuthorization.cs` |
| **Payments.VoidPayment** | `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", …ct)`. Pass all nullables (`null`), `payPalRequestId:` idem. **Resp** `PaymentAuthorization` (204/representation). **Error** Case A `VoidPaymentError`·`TryGetError`[401,403,404,409,422]·`TryGetNoContent`[500]. src Payments.md |
| **Payments.RefundCapturedPayment** | `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", …ct)`. **Req** `RefundRequest{ Amount=Money }` for partial; `body:null` for full. `payPalRequestId:` **caller-supplied idempotency key**. **Resp** `Refund{ Id(id), Status(status)=RefundStatus, Amount=Money }`. **Error** Case A `RefundCapturedPaymentError`·`TryGetError`[400,401,403,404,409,422]·`TryGetNoContent`[500]. src Payments.md; `Models/RefundRequest.cs`, `Models/Refund.cs` |
| **Vault.CreateSetupToken** | `CreateSetupToken(string? payPalRequestId, SetupTokenRequest body, …ct)`. **Req** `SetupTokenRequest{ Customer(customer)=Customer{ MerchantCustomerId }?, PaymentSource(payment_source)=SetupTokenRequestPaymentSource{ Card=SetupTokenRequestCard{ Number, Expiry, SecurityCode, Name, BillingAddress } } [req] }`. **Resp** `SetupTokenResponse{ Id(id) }`. **Error** Case A `CreateSetupTokenError`·`TryGetError`[400,403,422,500]. src Vault.md; `Models/SetupTokenRequest.cs`, `Models/SetupTokenRequestPaymentSource.cs`, `Models/SetupTokenRequestCard.cs`, `Models/Customer.cs` |
| **Vault.CreatePaymentToken** | `CreatePaymentToken(string? payPalRequestId, PaymentTokenRequest body, …ct)`. **Req** `PaymentTokenRequest{ PaymentSource(payment_source)=PaymentTokenRequestPaymentSource{ Token=VaultTokenRequest{ Id=setupTokenId [req], Type=VaultTokenRequestType.SetupToken [req] } } [req] }`. **Resp** `PaymentTokenResponse{ Id(id), PaymentSource.Card=CardPaymentTokenEntity{ LastDigits(last_digits), Brand(brand)=CardBrand, Expiry, Name } }`. **Error** Case A `CreatePaymentTokenError`·`TryGetError`[400,403,404,422,500]. src Vault.md; `Models/PaymentTokenRequest.cs`, `Models/PaymentTokenRequestPaymentSource.cs`, `Models/VaultTokenRequest.cs`, `Models/PaymentTokenResponse.cs`, `Models/PaymentTokenResponsePaymentSource.cs`, `Models/CardPaymentTokenEntity.cs` |
| **Vault.DeletePaymentToken** | `DeletePaymentToken(string id, …ct)` → `void`. **Error** Case A `DeletePaymentTokenError`·`TryGetError`[400,403,500]. src Vault.md; `Errors/DeletePaymentTokenError.cs` |
| **TransactionSearch.SearchTransactions** | `SearchTransactions(string startDate, string endDate, string? transactionId, string? transactionType, string? transactionStatus, string? transactionAmount, string? transactionCurrency, string? paymentInstrumentType, string? storeId, string? terminalId, string? fields = "transaction_info", string? balanceAffectingRecordsOnly = "Y", int? pageSize = 100, int? page = 1, …ct)`. Call with **named args** (8 nullable no-default params). `startDate`/`endDate` = ISO-8601 (RFC3339). **Resp** `SearchResponse{ TransactionDetails(transaction_details)=[TransactionDetails{ TransactionInfo=TransactionInformation{ TransactionId, PaypalReferenceId, InvoiceId, TransactionAmount=Money, FeeAmount=Money, TransactionStatus, TransactionInitiationDate } }], Page, TotalPages(total_pages), TotalItems }`. **Error** **Case B** `SdkException<RawError>`. src TransactionSearch.md; `Models/SearchResponse.cs`, `Models/TransactionDetails.cs`, `Models/TransactionInformation.cs` |

### Enums (wire values, from `Models/Enums/`)
- `CheckoutPaymentIntent`: `AUTHORIZE` (use `.Authorize`), `CAPTURE`.
- `OrderStatus`: `CREATED, SAVED, APPROVED, VOIDED, COMPLETED, PAYER_ACTION_REQUIRED` — **`PayerActionRequired` ⇒ browser challenge ⇒ STOP/report** (also check `Order.Links` for rel `payer-action`/`approve`).
- `AuthorizationStatus`: `CREATED, CAPTURED, DENIED, PARTIALLY_CAPTURED, VOIDED, PENDING`. (No `EXPIRED` member ⇒ staleness detected via `ExpirationTime` + capture-failure catch, not a status.)
- `CaptureStatus`: `COMPLETED, DECLINED, PARTIALLY_REFUNDED, PENDING, REFUNDED, FAILED`.
- `RefundStatus`: `CANCELLED, FAILED, PENDING, COMPLETED`.
- `VaultTokenRequestType`: `SETUP_TOKEN` (use `.SetupToken`).
- `CardBrand` (read-only, mapped to string for display).

### Client construction / auth / server
- Ctor: `new PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)`; only ctor.
- DI: `services.AddPayPalServerSdkClient(options => { … })` (source `ServiceCollectionExtensions.cs`) — registers the client **singleton**, capturing options once; fills `Logging.LoggerFactory` from the container.
- Auth: `options.Oauth2 = new PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials { ClientId, ClientSecret }`. Client-credentials grant; token cached in-memory per client, re-fetched on expiry / invalidated on 401. `using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;`.
- Environment: only `ServerEnvironment.Sandbox` exists (`Servers/ServerEnvironment.cs`). `PayPal:Environment` maps to it; any non-sandbox value **without** a `BaseUrl` override → fail-fast (no live environment in this SDK version).
- **Base URL override:** `options.Server.Default.Sandbox.BaseUrl = <PayPal:BaseUrl>` when set. **Verified** in `AuthSchemes.cs`: the OAuth token request is `server.Default("/v1/oauth2/token")`, i.e. derived from `Sandbox.BaseUrl` — so the override applies to the **token request too**, exactly as required.

## 3. Trap notes (hazard + skill pointer — not resolved here)

- Building `OrderRequest`/`PaymentSource`/`CardRequest` etc.: these are `record`s with `required` members, `init`-only, JSON wire names ≠ C# names, enums are `StringEnum<T>` not C# enums, response bodies carry an `AdditionalProperties` extension bag. **MUST load dotnet-models** before constructing any payload / reading any enum. *(step 3,5)*
- Reading capture/auth off the nested `OrderAuthorizeResponse.PurchaseUnits[].Payments.Authorizations[]` and `SearchResponse.TransactionDetails[]`: deep-optional chains, `StringEnum` comparisons. **MUST load dotnet-models**. *(step 3)*
- First `client.{group}.{op}(...)` call: many optional params have no C# default and mis-bind positionally; `SearchTransactions` must use named args; `prefer` default is `return=minimal` (we need representation). **MUST load dotnet-calling-endpoints**. *(step 3)*
- Idempotency: `payPalRequestId` (the real `PayPal-Request-Id`) vs the generator-injected per-call `Idempotency-Key` (fresh GUID, NOT a key). **MUST load dotnet-calling-endpoints** / see §Idempotency of getting-started. *(step 3)*
- Error boundary in the gateway: Case A typed vs Case B (`SearchTransactions`) errors; `TryGetNoContent` on 500 for Payments ops; **`JsonException` reaches the boundary from two directions** (drifted 2xx body, and a non-2xx body that doesn't match `{Operation}Error`). **MUST load dotnet-error-handling**. *(step 3)*
- Client lifetime / timeout / retry-verb eligibility / logging-body redaction / base-URL nesting / pagination of SearchTransactions: **MUST load dotnet-configuration-resilience**. *(step 3,4)*
- Faking the SDK for tests via the `HttpClient` seam. **MUST load dotnet-testing**. *(step 7)*

## 4. REQUIRED READING (load all before implementation; contents deliberately not carried here)

- `paypal-platforms-team:dotnet-models` — building request records / reading enums & nested response models (step 3,5).
- `paypal-platforms-team:dotnet-calling-endpoints` — first calls, named args, `prefer`, real idempotency key (step 3).
- `paypal-platforms-team:dotnet-error-handling` — try/catch ladder, Case A/B, `TryGetNoContent`, JsonException (step 3). **Always required.**
- `paypal-platforms-team:dotnet-configuration-resilience` — client tuning, timeout budget, retry verbs, logging redaction, base URL, SearchTransactions pagination (step 3,4).
- `paypal-platforms-team:dotnet-testing` — SDK test seam (step 7).
- (`dotnet-client-initialization`, `dotnet-authentication` already loaded for §Client construction.)

⚠ **Two hazard rows that always apply** (`System.Text.Json.JsonException` reaches the boundary from two directions, needing opposite handling):
1. A drifted/malformed **2xx** body (a missing `required` member) surfaces as `JsonException` **from deserialization**, NOT as `SdkException` — an SDK-exception-only catch ladder lets it escape.
2. A **non-2xx** body that doesn't match its operation's generated `{Operation}Error` shape throws `JsonException` **while the error object is being constructed**, so it **replaces** the `SdkException` and the HTTP status is destroyed with it.
⇒ the gateway's catch ladder must also catch `JsonException` and translate to a gateway error.

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `PayPalOptions` bound from `PayPal:` with `[Required]` on `ClientId`, `ClientSecret`, `Environment`, `Currency`; `AddOptions<PayPalOptions>().Bind(...).ValidateDataAnnotations().ValidateOnStart()`. Each part checked (blank ≠ missing). Host refuses to boot on any blank; message names the key, never the value. |
| 2 | Secret sourcing & rotation | Values live in **.NET user-secrets** (`PublicApi` `UserSecretsId`), loaded from env vars by me at setup; never in repo. SDK client is a **singleton** capturing options once at registration ⇒ a rotated secret takes effect only on **process restart** (documented; acceptable for this app). |
| 3 | Total timeout budget | Register the SDK over a **named `HttpClient`** with `Timeout = 30s` (per-attempt) and default retries; the whole-call budget is bounded by the caller `CancellationToken` (from the request `ct`) threaded into every gateway call. `Timeout` is per-attempt, not total. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = GET,HEAD,PUT,OPTIONS ⇒ our POSTs (CreateOrder/Authorize/Capture/Void/Refund/Vault) are **never auto-resent** by the SDK. Safe: we still send a deterministic `PayPal-Request-Id` on each so a manual retry can't double-charge. |
| 5 | Idempotency & ambiguous writes | Authorize: `PayPal-Request-Id = pay-{orderId}` (CreateOrder) / `pay-{orderId}-auth` (AuthorizeOrder), deterministic per order + local `OrderPayment.Status` guard (already-authorized ⇒ return existing). Capture: `capture-{orderId}` + status guard. Void: `void-{orderId}`. **Refund: caller-supplied idempotency key** → `PayPal-Request-Id`; local `PaymentRefund` keyed by that key (repeat key ⇒ return stored `refundId`; distinct keys ⇒ distinct partial refunds, subject to over-refund guard `RefundedAmount + amount ≤ CapturedAmount`). |
| 6 | Observability | Structured logs at gateway boundary: operation name + PayPal `debug_id` (from `Error.DebugId` / RawError body) at Warning/Error; Info on success with PayPal ids. `LogRequestBody` stays **off**. |
| 7 | Sensitive data | Requests carry **PAN/CVV/expiry** (`CardRequest`, `SetupTokenRequestCard`). ⇒ `options.Logging.LogRequestBody` left **off** AND `options.Logging.LoggerFactory` set explicitly (via DI extension) so `PAYPALSERVERSDKCLIENT_LOG` can't force body logging on. Card number/CVV **never** persisted (only `last_digits`+brand from PayPal responses) and **never** logged by our code. |
| 8 | Environment selection | SDK declares **only** `ServerEnvironment.Sandbox` (one server group `Default`). All dev/test traffic targets sandbox by construction; there is no live environment to leak to. `PayPal:BaseUrl` override (if set) points every call — incl. token — at that host verbatim; a non-sandbox `Environment` value without a `BaseUrl` override fails fast. |

## 6. Assumptions & Blockers

- **No Blockers.** Every required capability maps to an SDK operation above.
- Assumption: direct (unbranded) card auth needs no buyer approval for the sandbox test card; if PayPal returns `PAYER_ACTION_REQUIRED`/a `payer-action` link, we **STOP and report** (per task) rather than building an approval round-trip.
- Assumption: caller identity = JWT username (token carries no user-id claim); used as owner key and, sanitized, as `merchant_customer_id` for vaulting.
- Assumption: `SearchTransactions` sandbox reporting lags; an empty recent range is expected, not a gap. Range wider than PayPal's 31-day window is chunked into ≤31-day sub-windows, each fully paginated (`total_pages`).
- Reconciliation match key: `OrderPayment.InvoiceReference` (`eshop-{orderId}-{short}`) set as PayPal `invoice_id`+`custom_id`; matched against `TransactionInformation.InvoiceId`.

## 7. Verified sandbox behavior (live self-test)

- **Direct-card authorize:** `CreateOrder(intent=AUTHORIZE, payment_source.card, prefer=return=representation)` processes the card inline and the authorization is **already present** on the created order (`purchase_units[0].payments.authorizations[0]`). Calling `AuthorizeOrder` afterwards returns **422** (already authorized). The gateway therefore reads the authorization off the created order and only falls back to `AuthorizeOrder` if none is present (wallet flows).
- **Void returns 204 No Content** (no body); the gateway treats a `JsonException` on a 204 as an empty success rather than a processing error.
- **Idempotency keys are per-run-unique**, derived from `InvoiceReference` (which carries a GUID), because the in-memory DB resets order ids to 1 each run and PayPal-Request-Ids are unique per merchant across the retention window — a fixed `pay-{orderId}` key collides across runs/agents on the shared sandbox account.
- **Typed errors carry no HTTP status**; the status is captured via an `SdkHook.OnResponse` writing into an `AsyncLocal` holder seeded at the call site (a holder set inside the hook alone would not flow back up).
- End-to-end confirmed on sandbox card 4111…: authorize (hold), capture at fulfil (gross/fee/net reported), full+partial refunds with idempotent repeats and an over-refund rejection, void/cancel, saved-card vault + reuse + delete, reconciliation across a multi-page/multi-window range. Recent captures show as `EShopOnly` in reconciliation due to PayPal reporting lag — expected, not a gap.
