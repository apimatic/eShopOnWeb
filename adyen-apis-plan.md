# Adyen APIs integration plan — eShopOnWeb card payments

SDK source: `sdk/dotnet/` inside the **adyen** plugin (context-plugins marketplace). All `source` cells below are relative to that SDK root.
Project reference: `src/Infrastructure/Infrastructure.csproj` → `<plugin-root>/sdk/dotnet/AdyenApIs.csproj` (machine-local path; see Assumptions).

## 1. Scope & sequence

| # | Step | SDK operations used |
| --- | --- | --- |
| 1 | `global.json` roll-forward; baseline build/test | — |
| 2 | Order aggregate gets payment state (`OrderPaymentStatus`, paid/refunded minor amounts, currency) + child entities `PaymentAttempt`, `OrderRefund`, `AdyenResponseRecord`; claim entity `PaymentOperationLock` (PK = OrderId) | — |
| 3 | `IPaymentGateway` (ApplicationCore) + `AdyenPaymentGateway` (Infrastructure) wrapping the SDK; settings bound from `Adyen:` with fail-fast validation; client registered over a named `HttpClient` | `Payments.CreatePayment`, `Modifications.RefundPayment` |
| 4 | `OrderPaymentService` (ApplicationCore): place order, pay (claim → SDK → record), refund (claim → SDK → record), settle unknown outcomes | via gateway |
| 5 | PublicApi endpoints: `POST /api/orders`, `POST /api/orders/{orderId}/pay`, `POST /api/orders/{orderId}/refunds` (admin), `GET /api/my-orders`, `GET /api/orders/{orderId}/adyen-record` (admin) | — |
| 6 | Offline tests: gateway against stub `HttpMessageHandler`; service rules; PublicApi integration tests with stubbed Adyen | — |
| 7 | Live self-verification against Adyen test: payment, partial refund, adyen-record | both |

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, verbatim. Each operation that takes input takes ONE request record as its first parameter, built with an object initializer using the record's own property names — never flat arguments.
> ⚠ Every SDK type is written fully-qualified with the namespace implied by ITS OWN source path (from the map), never a neighbouring type's.

| Controller | Signature | Request record + members | Body model + fields (wire) | Response + fields read | Error | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `client.Payments` | `CreatePayment(AdyenApIs.Requests.Payments.CreatePaymentRequest request, AdyenApIs.Core.RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` → `Task<AdyenApIs.Models.PaymentResponse>` | `CreatePaymentRequest`: `IdempotencyKey: string?` (sent as `Idempotency-Key` header, caller-supplied, max 64 chars), `Body: PaymentRequest?` | `AdyenApIs.Models.PaymentRequest`: `Amount (amount): AdyenApIs.Models.Amount2` **required**; `MerchantAccount (merchantAccount): string` **required**; `PaymentMethod (paymentMethod): AdyenApIs.Models.AnyOf.PaymentMethod111` **required**; `Reference (reference): string` **required** (max 80); `ReturnUrl (returnUrl): string` **required**; optional used: `MerchantOrderReference (merchantOrderReference): string?`, `CaptureDelayHours (captureDelayHours): int?` (0 ⇒ capture now), `ShopperInteraction (shopperInteraction): AdyenApIs.Models.Enums.ShopperInteraction?`. `Amount2`: `Currency (currency): string` req (3 chars), `Value (value): long` req (minor units). Card variant: `PaymentMethod111.Card(AdyenApIs.Models.Card)`; `Card`: `EncryptedCardNumber (encryptedCardNumber)`, `EncryptedExpiryMonth (encryptedExpiryMonth)`, `EncryptedExpiryYear (encryptedExpiryYear)`, `EncryptedSecurityCode (encryptedSecurityCode)`, `HolderName (holderName)` all `string?`; `Type (type): Type12?` defaults `Type12.Scheme`. Left out deliberately: `shopperReference` (doc: no PII; no recurring), `shopperName`, `browserInfo`/3DS2 data (no front end), `channel`, `countryCode`. | `PaymentResponse` (no extension-data bag): `PspReference (pspReference): string?`, `ResultCode (resultCode): AdyenApIs.Models.Enums.ResultCode1?`, `RefusalReason (refusalReason): string?`, `RefusalReasonCode (refusalReasonCode): string?`, `Amount (amount): AdyenApIs.Models.Amount25?` (`Currency`, `Value: long`), `MerchantReference`, `Action: ActionModel?` (presence ⇒ shopper action needed) | Case A `AdyenApIs.Core.Exceptions.ApiException<AdyenApIs.Errors.CreatePaymentError>`: `TryGetServiceError(out AdyenApIs.Models.ServiceError)` [400,401,403,422,500] · `TryGetRawError(out AdyenApIs.Core.ErrorResponse.RawError)` [fallback] | none | `map/operations/Payments.md`; `Requests/Payments/CreatePaymentRequest.cs`; `Models/PaymentRequest.cs`; `Models/Amount2.cs`; `Models/AnyOf/PaymentMethod111.cs`; `Models/Card.cs`; `Models/PaymentResponse.cs`; `Errors/CreatePaymentError.cs` |
| `client.Modifications` | `RefundPayment(AdyenApIs.Requests.Modifications.RefundPaymentRequest request, AdyenApIs.Core.RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` → `Task<AdyenApIs.Models.PaymentRefundResponse>` | `RefundPaymentRequest`: `PaymentPspReference: string` **required** (path), `IdempotencyKey: string?` (header), `Body: PaymentRefundRequest?` | `AdyenApIs.Models.PaymentRefundRequest`: `Amount (amount): AdyenApIs.Models.Amount37` **required** (`Currency` req, `Value: long` req); `MerchantAccount (merchantAccount): string` **required**; optional used: `Reference (reference): string?`. Left out: `merchantRefundReason`, `capturePspReference`, `lineItems`, `splits`, `store`. | `PaymentRefundResponse`: `PspReference (pspReference): string` req, `PaymentPspReference: string` req, `Amount: Amount38` req, `Status (status): string` get-only `"received"`, `Reference: string?` | Case A `ApiException<AdyenApIs.Errors.RefundPaymentError>`: `TryGetServiceError(out ServiceError)` [400,401,403,422,500] · `TryGetRawError(out RawError)` [fallback] | none | `map/operations/Modifications.md`; `Requests/Modifications/RefundPaymentRequest.cs`; `Models/PaymentRefundRequest.cs`; `Models/Amount37.cs`; `Models/PaymentRefundResponse.cs`; `Errors/RefundPaymentError.cs` |

`ServiceError` (`Models/ServiceError.cs`): `ErrorCode (errorCode): string?`, `ErrorType (errorType): string?`, `Message (message): string?`, `PspReference (pspReference): string?`, `Status (status): int?`, `AdditionalData`.

Enum `AdyenApIs.Models.Enums.ResultCode1` (`Models/Enums/ResultCode1.cs`), values read: `Authorised` (final, paid), `Refused` (final, reason in `refusalReason`), `Error` (final), `Cancelled` (final), `Pending`/`Received` (not final), `RedirectShopper`/`IdentifyShopper`/`ChallengeShopper`/`PresentToShopper` (shopper action), `PartiallyAuthorised`. Open enum: undeclared values arrive and must hit a default arm.
Enum `AdyenApIs.Models.Enums.ShopperInteraction`: `Ecommerce`. Enum `AdyenApIs.Models.Enums.Type12`: `Scheme` (the `Card` default).

Client / auth / server facts (`sdk-map.md`, `AdyenApIsClientOptions.cs`, `Servers/ServerEnvironment.cs`):
- Only ctor: `new AdyenApIs.AdyenApIsClient(HttpClient httpClient, AdyenApIs.AdyenApIsClientOptions options)`.
- Auth for both ops: `options.BasicAuth` OR `options.ApiKeyAuth` (`X-API-Key`). We set **only** `ApiKeyAuth`.
- `AdyenApIs.Servers.ServerEnvironment` has one member, `Production` (default), whose `Default` group = `https://checkout-test.adyen.com/v71`, `Default1` = `https://management-test.adyen.com/v3`. Both operations use group `Default`. Override point: `options.Server.Default.Production.BaseUrl`.
- `options.Retry: AdyenApIs.Core.Configuration.RetryOptions` (all members required; start from `RetryOptions.Default()` / `Disabled()`), `options.Logging: LoggingOptions` (`LoggerFactory`, `LogRequestBody`), `options.Hooks: IReadOnlyList<AdyenApIs.Core.Hooks.SdkHook>`; per-call `AdyenApIs.Core.RequestOptions { Hooks }` (appended after client-wide hooks). `SdkHook.OnResponse(Func<HttpResponseMessage, HookContext, CancellationToken, ValueTask>)` sees each raw response, inside the pipeline (`Core/Hooks/SdkHook.cs`, `Core/RawClient.cs`).
- Exception family (`AdyenApIs.Core.Exceptions`): `SdkException` → `ApiException` (`StatusCode`) → `ApiException<TError>` / `ResponseDeserializationException`; `SdkConnectionException` → `SdkTimeoutException`; `AuthSchemeException`.

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| `PaymentPspReference` passed to RefundPayment must be the `pspReference` of an **Authorised** payment of the same order that this application stored | `RefundPayment` ← `CreatePayment` (stored `PaymentAttempt.PspReference` with status Authorised) | `OrderPaymentService.RefundAsync` reads it from `order.SuccessfulPayment()` — caller never supplies a psp reference |
| Refund amount currency must be the currency the payment was taken in | `RefundPayment` ← `CreatePayment` (stored `PaymentAttempt.Currency`) | `Order.StartRefund` uses the stored attempt currency |
| Σ refunds (received + unknown) ≤ captured amount (RefundPayment remarks: "as long as their sum doesn't exceed the captured amount") | `RefundPayment` ← `CreatePayment` (stored authorised amount) | `Order.StartRefund` before the SDK call, under the order's payment-operation claim |
| Order id / buyer the shopper acts on must be one this application created for that buyer | `CreatePayment` ← `POST /api/orders` (stored `Order.BuyerId`) | `OrderPaymentService.PayAsync` ownership check before the claim |

## 3. Trap notes

| Step | Hazard → consequence | Skill |
| --- | --- | --- |
| 3 | Client/HttpClient lifetime and DNS staleness for a long-lived client; per-request construction cost | MUST load `adyen:dotnet-client-initialization` |
| 3 | An unset credential sends the request unauthenticated (no exception) → first payment fails with 401 in production | MUST load `adyen:dotnet-authentication` |
| 3 | What `RetryOptions.Timeout` and `HttpClient.Timeout` each actually bound vs. the 30 s caller budget; which verbs the SDK resends | MUST load `adyen:dotnet-configuration-resilience` |
| 3 | Built-in logger + `ADYENAPISCLIENT_LOG` can dump request bodies carrying encrypted card data | MUST load `adyen:dotnet-configuration-resilience` |
| 3 | The `Idempotency-Key` on the wire may be generator-injected rather than the caller's key → duplicate charge on resend | MUST load `adyen:dotnet-configuration-resilience` |
| 3 | Unknown outcome after transport failure on a POST must be settled, not reported as failure | MUST load `adyen:dotnet-configuration-resilience` |
| 3 | `PaymentMethod111` is a union — construction/read shape; `ResultCode1` is an open enum — comparison/`ToString()` pitfalls | MUST load `adyen:dotnet-models` |
| 3 | Request record vs body model, `requestOptions` position before the token | MUST load `adyen:dotnet-calling-endpoints` |
| 3 | Catch-ladder order; `TryGetRawError` is not a catch-all; status on the exception vs body | MUST load `adyen:dotnet-error-handling` |
| 6 | Request body disposed by the SDK before the test inspects it; which seam to stub | MUST load `adyen:dotnet-testing` |

## 4. REQUIRED READING (load before implementation starts; this sheet deliberately does not carry their contents)

| Skill | Governs |
| --- | --- |
| `adyen:dotnet-client-initialization` | step 3 client + DI registration |
| `adyen:dotnet-authentication` | step 3 credentials + fail-fast |
| `adyen:dotnet-calling-endpoints` | step 3 calls |
| `adyen:dotnet-models` | step 3 union/enum handling |
| `adyen:dotnet-error-handling` | step 3/4 error boundary |
| `adyen:dotnet-configuration-resilience` | step 3/4 timeouts, retries, logging, idempotency, unknown outcomes |
| `adyen:dotnet-testing` | step 6 |

Mandatory hazard row: a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `AdyenSettings` bound from `Adyen:` (`ApiKey`, `MerchantAccount`, `Environment`, `Currency`) with `ValidateOnStart`; blank ApiKey/MerchantAccount, Environment ≠ `test`, or Currency not a 3-letter code ⇒ host refuses to start, message names the key, never the value. Only `ApiKeyAuth` is set, so only it is required. |
| 2 | Secret sourcing & rotation | Dev: .NET user-secrets on PublicApi (loaded from the `ADYEN_*` env vars by hand, never written into the repo). Other deployments: any `IConfiguration` source (`Adyen__ApiKey` env var, Key Vault). Options built once at registration into a singleton client ⇒ rotation takes effect on restart; documented. |
| 3 | Total timeout budget | Caller waits ≤ ~25 s for Adyen work: one `CancellationTokenSource` deadline of 25 s around **all** SDK calls of a request (first send + settle resend), created in the gateway's single `Bounded` path. Per attempt: `RetryOptions.Timeout` 10 s and `HttpClient.Timeout` 12 s backstop, so send + settle-resend (≤ ~20 s) fits the 25 s budget (`OrderPaymentService.ProviderTimeBudget`, `AdyenServiceCollectionExtensions.AttemptTimeout`). Deadline hit ⇒ HTTP 504 "Adyen did not respond". |
| 4 | Write-retry ownership | Both writes are `POST`; client built with `RetryOptions.Disabled()` (keeps per-attempt timeout) so the SDK never resends; the only resend is our explicit same-key settle resend. |
| 5 | Idempotency & ambiguous writes | CreatePayment: `CreatePaymentRequest.IdempotencyKey` = per-attempt GUID persisted on `PaymentAttempt` **before** the call. RefundPayment: `RefundPaymentRequest.IdempotencyKey` = per-refund GUID persisted on `OrderRefund` before the call. Provider-side dedupe was `UNVERIFIED` in source (only "unique identifier for the message"); **verified live 2026-10-07** against Adyen test: two CreatePayment sends with one key returned the same pspReference (one charge). |
| 6 | Observability | SDK `LoggerFactory` = host factory (request line/status at Information, failures at Warning/Error), headers + bodies off. Our logs: order id, attempt/refund id, merchant reference, Adyen `pspReference`, `resultCode`, `ServiceError.errorCode`/`errorType`/`pspReference` (correlation id), HTTP status. Never card fields, never API key. |
| 7 | Sensitive data | Request carries encrypted card fields + holder name ⇒ `LogRequestBody=false`, `LogRequestHeaders=false`, `LoggerFactory` assigned explicitly (disarms `ADYENAPISCLIENT_LOG`). Card data is never persisted; only Adyen **responses** are stored for support. |
| 8 | Environment selection | SDK declares one environment (`Production`) whose groups point at `checkout-test`/`management-test`; we use only group `Default` (checkout). `Adyen:Environment` must be `test`; any other value fails startup (live hosts are not declared by this SDK — see Blockers/Assumptions). Test traffic therefore cannot reach live. |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS. |
| 10 | Partial results | N/A — no paged reads in scope. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES: settle by same-key resend inside the catch; if still unknown, record `Unknown` and the next pay/refund call on that order settles it before anything else. |

### DUPLICATE CLAIMS

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| CreatePayment (pay an order) | `PaymentOperationLocks` table in `CatalogContext` (row keyed by OrderId), written before the SDK call; released in `finally`; expires after 2 min | Primary-key violation on `PaymentOperationLock.OrderId` (insert is attempted first; SQL Server raises `DbUpdateException`, the EF in-memory store `ArgumentException`) | `EfPaymentOperationLockStore.TryInsertAsync` `catch (DbUpdateException)` / `catch (ArgumentException)` ⇒ `false` ⇒ `PayOrderOutcome.Busy` ⇒ 409 | `EfPaymentOperationLockStore.TryAcquireAsync` (claim, called first in `OrderPaymentService.PayAsync`) → `AdyenPaymentGateway.AuthoriseAsync` (`client.Payments.CreatePayment`) |
| RefundPayment (refund an order) | same `PaymentOperationLocks` row per OrderId (pay and refund never overlap on one order) | same primary key | same catch ⇒ `RefundOrderOutcome.Busy` ⇒ 409 | `EfPaymentOperationLockStore.TryAcquireAsync` (claim, called first in `OrderPaymentService.RefundAsync`) → `AdyenPaymentGateway.RefundAsync` (`client.Modifications.RefundPayment`) |

### PAGED READS

none

### UNKNOWN OUTCOMES

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| CreatePayment | `CreatePayment` re-sent with the same `IdempotencyKey` (no lookup-by-reference read exists in this SDK's Checkout surface) | stored `PaymentAttempt.IdempotencyKey` (+ `Reference`) | `AdyenPaymentGateway.AuthoriseAsync` `catch (SdkConnectionException) when (send == 1 …)` resends once with the same key; still unknown (or 5xx / unreadable 2xx) ⇒ `PaymentAttemptStatus.Unknown` via `OrderPaymentService.RecordPayment`; `OrderPaymentService.PayAsync` (`order.UnsettledPayment` branch) re-sends it with the same key on the next pay call | `AdyenPaymentGatewayTests.Authorise_ConnectionFailsThenResend_SettlesWithSameIdempotencyKey`, `AdyenPaymentGatewayTests.Authorise_AdyenNeverAnswers_ReturnsUnknownWithinBudget`, `OrderPaymentServiceTests.Pay_UnknownAttempt_IsSettledWithSameKeyOnRetry`, `AdyenPaymentGatewayTests.Authorise_ServerError_IsUnknown_AndIsNotResent`, `AdyenPaymentGatewayTests.Authorise_UnreadableSuccessBody_IsUnknownNotFailure` |
| RefundPayment | `RefundPayment` re-sent with the same `IdempotencyKey` | stored `OrderRefund.IdempotencyKey` (+ `Reference`) | `AdyenPaymentGateway.RefundAsync` `catch (SdkConnectionException) when (send == 1 …)` resends once; still unknown ⇒ `RefundStatus.Unknown` (counted against `Order.RefundableAmountMinor`); the settle loop at the top of `OrderPaymentService.RefundAsync` re-sends it with the same key before any new refund | `AdyenPaymentGatewayTests.Refund_ConnectionFailsThenResend_SettlesWithSameIdempotencyKey`, `OrderPaymentServiceTests.Refund_UnknownRefund_IsSettledBeforeNewRefund`, `OrderPaymentServiceTests.Refund_StillUnknownAfterSettle_SendsNoNewRefund` |

## 6. Assumptions & Blockers

- **Blockers:** none for this task (test environment only).
- Assumption: `Adyen:Environment` = `test` is the only supported value. The SDK declares no live host; going live needs SDK regeneration/override — `YOUR CALL — not in the map`: fail startup rather than invent a URL.
- Assumption: no 3-D Secure front end exists, so a response carrying `action` / `RedirectShopper|IdentifyShopper|ChallengeShopper` is treated as "card requires authentication this checkout cannot perform" (order stays unpaid). Verified live 2026-10-07: the test Visa card returns `Authorised` directly (no action).
- Assumption: `captureDelayHours = 0` makes "taking the money now" an immediate capture; refunds rely on capture having happened (RefundPayment remarks). Refund outcome is asynchronous (REFUND webhook); webhooks are out of scope, so a refund is recorded as `Received` (Adyen's `status`).
- Assumption: minor-unit exponent per ISO 4217 (2 by default; 0/3 for the listed exceptions). An order total not exactly representable is refused rather than rounded.
- Assumption: the SDK project reference path is local to machines with the plugin installed at the same path.
- Refusal messaging: Adyen's `refusalReason` text is surfaced verbatim with generic actionable guidance; no hard-coded refusal-reason catalogue (not in the map).
