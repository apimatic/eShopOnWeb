# Adyen APIs plan — eShopOnWeb card payments (PublicApi)

SDK: Adyen APIs .NET SDK (`AdyenApIs`), source + map at plugin path `sdk/dotnet/` (plugin `adyen`, marketplace `context-plugins`).
All "source" cells below are relative to that SDK root.

## 1. Scope & sequence

| # | Step | SDK operations |
|---|------|----------------|
| 1 | Reference SDK project from `src/Infrastructure`; bind + validate `Adyen:` settings (fail fast); register named `HttpClient` + singleton `AdyenApIsClient` | — (client construction) |
| 2 | Domain: `OrderPayment` / `OrderRefund` entities (string PK claims) linked to `Order`; derived order payment state; EF config + SQL migration | — |
| 3 | `IPaymentGateway` (ApplicationCore) + `AdyenPaymentGateway` (Infrastructure): authorise-with-immediate-capture, refund, error boundary, 30 s budget | `Payments.CreatePayment`, `Modifications.RefundPayment` |
| 4 | `OrderPaymentService` (claims, idempotency, over-refund invariant, unknown-outcome settlement) | via gateway |
| 5 | PublicApi endpoints: `POST api/orders`, `POST api/orders/{orderId}/pay`, `POST api/orders/{orderId}/refunds` (Administrators), `GET api/my-orders` | via service |
| 6 | Tests (offline: stub `HttpMessageHandler`), live self-verification against Adyen test | — |

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, verbatim — each operation that takes input takes ONE request record as its first parameter, built with an object initializer whose property names are the record's own, never flat arguments.
> ⚠ Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type, never from where a neighbouring type sits.

| Controller · method | Request record (members) | Body model (fields, wire) | Response + fields read | Error | Paging | Source |
|---|---|---|---|---|---|---|
| `client.Payments` · `CreatePayment(AdyenApIs.Requests.Payments.CreatePaymentRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` → `Task<AdyenApIs.Models.PaymentResponse>` | `IdempotencyKey: string?` (real caller key → `Idempotency-Key` header, max 64 chars) · `Body: AdyenApIs.Models.PaymentRequest?` | `PaymentRequest`: `Amount (amount): AdyenApIs.Models.Amount2, required` {`Currency (currency): string, required, len 3`, `Value (value): long, required, minor units`} · `MerchantAccount (merchantAccount): string, required` · `PaymentMethod (paymentMethod): AdyenApIs.Models.AnyOf.PaymentMethod111, required` — built `PaymentMethod111.Card(AdyenApIs.Models.Card)`; `Card`: `EncryptedCardNumber (encryptedCardNumber)`, `EncryptedExpiryMonth (encryptedExpiryMonth)`, `EncryptedExpiryYear (encryptedExpiryYear)`, `EncryptedSecurityCode (encryptedSecurityCode)`, `HolderName (holderName)`: all `string?`; `Type (type): Type12?` defaults `Type12.Scheme` · `Reference (reference): string, required, ≤80` · `ReturnUrl (returnUrl): string, required` · optional used: `CaptureDelayHours (captureDelayHours): int?`, `ShopperInteraction (shopperInteraction): ShopperInteraction?`, `MerchantOrderReference (merchantOrderReference): string?` | `PaymentResponse`: `ResultCode (resultCode): ResultCode1?`, `PspReference (pspReference): string?`, `RefusalReason (refusalReason): string?`, `RefusalReasonCode (refusalReasonCode): string?`, `Amount (amount): Amount25?` {`Currency`,`Value: long`}, `MerchantReference (merchantReference): string?`, `Action: ActionModel?` (presence only) | **Case A** `ApiException<AdyenApIs.Errors.CreatePaymentError>`: `TryGetServiceError(out AdyenApIs.Models.ServiceError)` [400,401,403,422,500] · `TryGetRawError(out RawError)` [fallback] | none | `map/operations/Payments.md`; `Requests/Payments/CreatePaymentRequest.cs`; `Models/PaymentRequest.cs`; `Models/Amount2.cs`; `Models/AnyOf/PaymentMethod111.cs`; `Models/Card.cs`; `Models/PaymentResponse.cs`; `Errors/CreatePaymentError.cs`; `Api/Payments.cs` (`POST /payments`) |
| `client.Modifications` · `RefundPayment(AdyenApIs.Requests.Modifications.RefundPaymentRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` → `Task<AdyenApIs.Models.PaymentRefundResponse>` | `PaymentPspReference: string, required` (path) · `IdempotencyKey: string?` (real caller key header) · `Body: AdyenApIs.Models.PaymentRefundRequest?` | `PaymentRefundRequest`: `Amount (amount): AdyenApIs.Models.Amount37, required` {`Currency`, `Value: long`} (currency must match authorisation; value ≤ authorised) · `MerchantAccount (merchantAccount): string, required` · optional used: `Reference (reference): string?` ≤80, `MerchantRefundReason (merchantRefundReason): MerchantRefundReason?` | `PaymentRefundResponse`: `PspReference (pspReference): string, required`, `PaymentPspReference: string, required`, `Status (status): string` (const `"received"`), `Amount: Amount38, required` | **Case A** `ApiException<AdyenApIs.Errors.RefundPaymentError>`: `TryGetServiceError(out ServiceError)` [400,401,403,422,500] · `TryGetRawError(out RawError)` [fallback] | none | `map/operations/Modifications.md`; `Requests/Modifications/RefundPaymentRequest.cs`; `Models/PaymentRefundRequest.cs`; `Models/Amount37.cs`; `Models/PaymentRefundResponse.cs`; `Errors/RefundPaymentError.cs`; `Api/Modifications.cs` (`POST /payments/{paymentPspReference}/refunds`) |

`ServiceError` (`Models/ServiceError.cs`): `ErrorCode (errorCode): string?`, `ErrorType (errorType): string?`, `Message (message): string?`, `PspReference (pspReference): string?` (correlation id), `Status (status): int?`.

Semantics from `<remarks>` (`Api/Payments.cs`, `Api/Modifications.cs`): direct card flow returns `pspReference` + `resultCode` (`Authorised`/`Refused`); a redirect/additional action returns an `action` object. Refund: refunds a **captured** payment; full or partial; multiple partials allowed while their sum ≤ captured amount; outcome is asynchronous (REFUND webhook).

Doc-named `PaymentRequest` fields deliberately left out: `browserInfo`, `authenticationData`, `threeDS2RequestData`, `origin`, `channel` (no native 3DS / redirect front end in scope), `shopperEmail`/`shopperReference`/`shopperIP` (PII not needed), `lineItems`, `additionalData`.

### Enum values used

| Enum (namespace `AdyenApIs.Models.Enums`) | Members used | Source |
|---|---|---|
| `ResultCode1` | `Authorised`, `Refused`, `Error`, `Cancelled`, `Pending`, `Received`, `RedirectShopper`, `ChallengeShopper`, `IdentifyShopper`, `PresentToShopper`, `PartiallyAuthorised` (+ `AuthenticationFinished`, `AuthenticationNotRequired`, `Success` → otherwise) | `Models/Enums/ResultCode1.cs` |
| `ShopperInteraction` | `Ecommerce` | `Models/Enums/ShopperInteraction.cs` |
| `MerchantRefundReason` | `Fraud` "FRAUD", `CustomerRequest` "CUSTOMER REQUEST", `Return` "RETURN", `Duplicate` "DUPLICATE", `Other` "OTHER" | `Models/Enums/MerchantRefundReason.cs` |
| `Type12` | `Scheme` (default on `Card.Type`) | `Models/Enums/Type12.cs` |

### Client construction / auth / servers

- Constructor: `new AdyenApIs.AdyenApIsClient(HttpClient httpClient, AdyenApIs.AdyenApIsClientOptions options)` (`sdk-map.md` → Getting a client).
- Auth: `options.ApiKeyAuth = <Adyen:ApiKey>` → header `X-API-Key`; `BasicAuth` left unset (OR composition; an unset scheme is skipped). Source: `sdk-map.md` → Servers & auth.
- Environment: `AdyenApIs.Servers.ServerEnvironment.Production` is the only member (and `Default()`); its `Default` group = `https://checkout-test.adyen.com/v71` (both operations), `Default1` = management-test (unused). Source: `Servers/ServerEnvironment.cs`, `sdk-map.md`.
- Options used: `Retry: AdyenApIs.Core.Configuration.RetryOptions` (`Default()`, `Disabled()`), `Logging: AdyenApIs.Core.Configuration.LoggingOptions` (`LoggerFactory`, `LogRequestBody`, `LogRequestHeaders`, `LogResponseHeaders`). Source: `Core/Configuration/RetryOptions.cs`, `Core/Configuration/LoggingOptions.cs`.
- Exceptions: `AdyenApIs.Core.Exceptions` (`ApiException<T>`, `ResponseDeserializationException`, `SdkConnectionException`, `SdkTimeoutException`, `AuthSchemeException`); `RawError` in `AdyenApIs.Core.ErrorResponse`.

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
|---|---|---|
| `RefundPayment.PaymentPspReference` must be the `pspReference` of an **Authorised** payment this application stored for that order (never caller-supplied) | `RefundPayment` ← `CreatePayment` (stored `OrderPayment.PspReference`) | `OrderPaymentService.RefundAsync` reads the order's authorised `OrderPayment` before the refund claim |
| Refund `Amount.Currency` must equal the authorised currency; sum of refunds (Received + Processing + Unknown) ≤ captured amount | `RefundPayment` ← `CreatePayment` (stored `OrderPayment.Currency/AmountMinor`) | `OrderPaymentService.RefundAsync` checks `amountMinor <= order.RefundableMinor()` (refund currency taken from the stored `OrderPayment`) before `AddRefundClaimAsync` |
| `CreatePayment.Amount.Value` must equal the order total in minor units of `Adyen:Currency` | `CreatePayment` ← `POST api/orders` (stored `OrderItem.UnitPrice × Units` from catalog) | `OrderPaymentService.PayAsync` → `CurrencyMinorUnits.ToMinor(order.Total(), Currency)` (throws if not exact); also checked at `PlaceOrderAsync` |
| Caller-supplied `catalogItemId`s must be items the catalog offers | `POST api/orders` ← catalog (`CatalogItemsSpecification`) | `OrderPaymentService.PlaceOrderAsync` rejects ids missing from the catalog read |

## 3. Trap notes

| Step | Hazard → consequence | Load |
|---|---|---|
| 1 | Who owns the `HttpClient` and how long the SDK client lives → per-request clients rebuild pipelines; singleton over a factory client caches DNS | MUST load `adyen:dotnet-client-initialization` |
| 1 | Credentials that are unset are silently skipped → first payment returns 401 instead of a boot failure | MUST load `adyen:dotnet-authentication` |
| 1/3 | What the two "Timeout" knobs actually bound, and which verbs the SDK resends → a hung Adyen holds the shopper beyond 30 s, or a POST is replayed | MUST load `adyen:dotnet-configuration-resilience` |
| 1 | What the built-in logger writes when `LoggerFactory` is null / `LogRequestBody` is on, and the `ADYENAPISCLIENT_LOG` switch → encrypted card blobs in logs | MUST load `adyen:dotnet-configuration-resilience` |
| 3 | Request record vs body model, positional `requestOptions` slot → CS1503 / wrong record | MUST load `adyen:dotnet-calling-endpoints` |
| 3 | Union construction and open-enum comparison/`ToString()` → wrong JSON or debug-form strings in logs/DB | MUST load `adyen:dotnet-models` |
| 3 | Which exception types reach the catch, `TryGetRawError` ordering, unknown outcome on transport failure → leaked messages, wrong status, double charge | MUST load `adyen:dotnet-error-handling` |
| 6 | Which seam to fake and when the request body becomes unreadable → tests that pass without exercising the SDK | MUST load `adyen:dotnet-testing` |

## 4. REQUIRED READING (load **before implementation starts**; this sheet deliberately does not carry their contents)

- `adyen:dotnet-client-initialization` — step 1 (client construction, DI, HttpClient)
- `adyen:dotnet-authentication` — step 1 (API key, fail-fast)
- `adyen:dotnet-configuration-resilience` — steps 1, 3 (retries, timeouts, logging, idempotent writes, unknown outcomes)
- `adyen:dotnet-calling-endpoints` — step 3 (calling `CreatePayment` / `RefundPayment`)
- `adyen:dotnet-models` — step 3 (`PaymentMethod111` union, `ResultCode1`/`MerchantRefundReason` enums)
- `adyen:dotnet-error-handling` — step 3 (error boundary)
- `adyen:dotnet-testing` — step 6 (offline tests)

Hazard row (verbatim): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
|---|---|---|
| 1 | Credential fail-fast | `AdyenSettings` bound from `Adyen:` (`ApiKey`, `MerchantAccount`, `Environment`, `Currency`), custom `IValidateOptions` validation (non-blank each; `Currency` = 3 letters; `Environment` = `test`), `AdyenSettingsValidator` + `.ValidateOnStart()` in `PaymentServiceCollectionExtensions.AddAdyenPayments` → host refuses to start, message names the key, never the value. |
| 2 | Secret sourcing & rotation | Source: `IConfiguration` (`Adyen:*` via user-secrets in Development, env vars `ADYEN_API_KEY`/`ADYEN_MERCHANT_ACCOUNT`/`ADYEN_ENVIRONMENT`/`ADYEN_CURRENCY` mapped onto `Adyen:*`, or any secret store). Options built **once** when the singleton client is first resolved → a rotated key needs a process restart (accepted; documented). |
| 3 | Total timeout budget | Caller waits ≤ **25 s** for Adyen per API request (whole call incl. one same-key resend), enforced by one `CancellationTokenSource` deadline per API request in `OrderPaymentService.PayAsync` / `RefundAsync` (`PaymentOptions.ProviderTimeBudget`), passed to every gateway call in that request (settlement + new write share it); per-attempt `Retry.Timeout` 20 s (`AdyenSettings.AttemptTimeout`); `HttpClient.Timeout` 25 s backstop. Total request (incl. DB) stays < 30 s. On expiry → 504 "Adyen did not respond in time…" (`AdyenPaymentGateway.NoResponse`, mapped in `ExceptionMiddleware`). |
| 4 | Write-retry ownership | Both writes are `POST`. SDK retries disabled (`RetryOptions.Disabled()` with explicit per-attempt `Timeout`) → SDK never resends. The app owns the only resend: one re-send with the **same** `IdempotencyKey` after `SdkConnectionException` when budget remains. |
| 5 | Idempotency & ambiguous writes | `CreatePayment`: real key `CreatePaymentRequest.IdempotencyKey` = per-attempt GUID stored on the `OrderPayment` claim row; `RefundPayment`: `RefundPaymentRequest.IdempotencyKey` = per-refund GUID stored on the `OrderRefund` claim row. Re-sends of an unknown outcome reuse the stored key. |
| 6 | Observability | App logs (Information) attempt start/outcome with orderId, attempt/refund id, merchant reference, amount, currency, resultCode, pspReference; Warning for refused/unknown; Error for provider errors with `ServiceError.ErrorCode`, `ErrorType`, `PspReference` (Adyen's correlation id) and HTTP status. SDK built-in logger: app `ILoggerFactory`, request/response line only. JSON request bodies would be logged unredacted if `LogRequestBody` were on — it stays off. |
| 7 | Sensitive data | `Card` carries encrypted card number/expiry/CVC + holder name. `LogRequestBody=false`, headers off, `LoggerFactory` assigned explicitly (disables `ADYENAPISCLIENT_LOG`). Card fields are never persisted or logged by the app. |
| 8 | Environment selection | One server group per op (`Default`, checkout-test). SDK declares only test hosts (`ServerEnvironment.Production` → `checkout-test.adyen.com`). `Adyen:Environment` must be `test`; any other value fails startup → test traffic can never reach live, and live is unsupported until a live base URL is configured (Blocker-free: task targets test). |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS. |
| 10 | Partial results | N/A — no paged reads (`GET api/my-orders` reads our own DB only). |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES: in-catch same-key resend, else record `Unknown` (counts as in-flight); the next `pay`/`refunds` call on that order re-sends with the stored key before anything new. |

### DUPLICATE CLAIMS

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
|---|---|---|---|---|
| `CreatePayment` (pay order) | `OrderPayments` table (CatalogContext), row PK `"{orderId}-P{n}"` with `n` = attempts on file + 1, status `Processing`, GUID idempotency key beside it | primary-key conflict on insert (second caller computes the same `n`); a caller that sees an active `Processing`/`Authorised` attempt is stopped before claiming | `EfPaymentStore.InsertClaimAsync` `catch (DbUpdateException or ArgumentException or InvalidOperationException)` → confirms the claim row exists → `PaymentConflictException` → 409 (`ExceptionMiddleware`) | claim: `OrderPaymentService.PayAsync` → `_paymentStore.AddPaymentClaimAsync(attempt)`; SDK call: `OrderPaymentService.ChargeAsync` → `IPaymentGateway.ChargeCardAsync` → `AdyenPaymentGateway.ChargeCardAsync` → `_client.Payments.CreatePayment` |
| `RefundPayment` (refund order) | `OrderRefunds` table, row PK `"{orderId}-R{n}"` with `n` = refunds on file + 1, status `Processing`, GUID key | primary-key conflict on insert; over-refund check runs against the same snapshot that produced `n` | same `EfPaymentStore.InsertClaimAsync` catch → `PaymentConflictException` → 409 | claim: `OrderPaymentService.RefundAsync` → `_paymentStore.AddRefundClaimAsync(refund)`; SDK call: `OrderPaymentService.SendRefundAsync` → `IPaymentGateway.RefundAsync` → `AdyenPaymentGateway.RefundAsync` → `_client.Modifications.RefundPayment` |

### PAGED READS

none

### UNKNOWN OUTCOMES

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
|---|---|---|---|---|
| `CreatePayment` | `CreatePayment` re-sent with the same `IdempotencyKey` (no lookup-by-reference op in scope) | stored `OrderPayment.IdempotencyKey` | in-catch resend: `AdyenPaymentGateway.SendAsync` `catch (SdkConnectionException) when (!resent && !deadline.IsCancellationRequested)`; else `OrderPaymentService.ChargeAsync` catch → `OrderPayment.MarkUnknown` (blocks new attempts) → settled by the next `OrderPaymentService.PayAsync` (in-flight branch re-sends the same attempt/key) | `AdyenPaymentGatewayTests.ChargeConnectionFailureResendsOnceWithSameIdempotencyKey`, `ChargeRepeatedConnectionFailureIsAnUnknownOutcomeAfterOneResend`, `OrderPaymentServiceTests.UnknownOutcomeIsSettledByResendingTheSameIdempotencyKey`, `OrderPaymentEndpointsTest.UnresponsiveAdyenFailsWithinTheBudgetAndRetryIsSafe` |
| `RefundPayment` | `RefundPayment` re-sent with the same `IdempotencyKey` | stored `OrderRefund.IdempotencyKey` (+ stored psp/amount) | in-catch resend: `AdyenPaymentGateway.SendAsync` (same catch); else `OrderPaymentService.SendRefundAsync` catch → `OrderRefund.MarkUnknown` (still counts against the refundable amount) → settled by the next `OrderPaymentService.RefundAsync` → `SettleUnresolvedRefundsAsync` | `AdyenPaymentGatewayTests.RefundConnectionFailureResendsOnceWithSameIdempotencyKey`, `OrderPaymentServiceTests.UnknownRefundCountsAgainstThePaymentAndIsSettledBeforeTheNextRefund` |

## 6. Assumptions & Blockers

- No blockers.
- Verified live (Adyen test, this session): `CaptureDelayHours = 0` authorisation followed immediately by a partial `RefundPayment` was accepted (`status: received`).
- Immediate capture via `CaptureDelayHours = 0` — `UNVERIFIED` (doc: "delay between the authorisation and scheduled auto-capture, in hours"); defensive directive: the payment is recorded `Paid` on `Authorised`; a refund refused because the capture has not settled yet surfaces Adyen's message (422) and leaves the refundable amount untouched.
- Same-key resend with different encrypted blobs (shopper retries an unknown attempt with freshly-encrypted data) — `UNVERIFIED` whether Adyen replays the original response or rejects the mismatching body; either outcome is recorded from the response, never a second charge under one key.
- 3-D Secure / redirect results (`action` present: `RedirectShopper`, `ChallengeShopper`, `IdentifyShopper`, `PresentToShopper`) are out of scope (no front end): recorded as failed attempt, order stays unpaid, shopper told the card needs extra verification not supported here. `YOUR CALL — not in the map`.
- Final refund/capture outcomes arrive by webhook (per `<remarks>`); webhook ingestion is out of scope — refunds are recorded `Received` (Adyen accepted). `YOUR CALL — not in the map`.
- Minor-unit exponent from ISO 4217 (default 2; 0/3-decimal currencies listed) — `YOUR CALL — not in the map`.
- `returnUrl` (required) = existing `baseUrls:webBase` + `Order/MyOrders` (the storefront order history) (no redirect flow is used). `YOUR CALL — not in the map`.
- SDK is referenced as a `ProjectReference` into this machine's plugin install (`../marketplace/plugins/adyen/sdk/dotnet`) — builds only where the plugin is installed at that relative path.
