# Adyen APIs integration plan — eShopOnWeb card payments & refunds

SDK root (plugin-relative): `sdk/dotnet/` in the `adyen` plugin. All **source** cells below are relative to that root.

## 1. Scope & sequence

| # | Step | Operations |
| --- | --- | --- |
| 1 | Environment: `global.json` rollForward `latestMajor`; reference `sdk/dotnet/AdyenApIs.csproj` from `src/Infrastructure` | — |
| 2 | Domain: extend existing `Order` with payment state (status, currency, paid/refunded amounts, pspReference, concurrency stamp); add `PaymentAttempt` and `OrderRefund` claim aggregates; EF configs; `IPaymentGateway` port in ApplicationCore | — |
| 3 | Settings `Adyen:ApiKey/MerchantAccount/Environment/Currency` bound + validated on start; client registered over a named `HttpClient` | — |
| 4 | `AdyenPaymentGateway` (Infrastructure): charge, reverse, refund; one exception ladder; unknown-outcome settle by idempotent resend | `Payments.CreatePayment`, `Modifications.ReversePayment`, `Modifications.RefundPayment` |
| 5 | `OrderPaymentService` (ApplicationCore): place order, pay (claim → call → record), refund (claim + reservation → call → record), list my orders | via gateway |
| 6 | PublicApi endpoints `POST api/orders`, `POST api/orders/{orderId}/pay`, `POST api/orders/{orderId}/refunds` (admin), `GET api/my-orders` | — |
| 7 | Offline tests (stub `HttpMessageHandler` for SDK; fake gateway for API integration tests); live verification against Adyen test | all three |

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, verbatim — each operation that takes input takes ONE request record as its first parameter (an operation with no inputs takes none), built with an object initializer whose property names are the record's own, never flat arguments.
> ⚠ Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type, never from where a neighbouring type sits.

| Controller · method | Request record + members | Body model + fields (`Name (wire): type, req?`) | Response + fields read | Error case | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| `client.Payments` · `Task<AdyenApIs.Models.PaymentResponse> CreatePayment(AdyenApIs.Requests.Payments.CreatePaymentRequest request, AdyenApIs.Core.RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` · `POST /payments` | `IdempotencyKey: string?` (REAL caller key → `Idempotency-Key` header, ≤64 chars) · `Body: AdyenApIs.Models.PaymentRequest?` | `AdyenApIs.Models.PaymentRequest`: `Amount (amount): AdyenApIs.Models.Amount2, req` {`Currency (currency): string[3], req`, `Value (value): long minor units, req`} · `MerchantAccount (merchantAccount): string, req` · `PaymentMethod (paymentMethod): AdyenApIs.Models.AnyOf.PaymentMethod111, req` — build `PaymentMethod111.Card(new AdyenApIs.Models.Card{…})`; Card: `EncryptedCardNumber (encryptedCardNumber)`, `EncryptedExpiryMonth (encryptedExpiryMonth)`, `EncryptedExpiryYear (encryptedExpiryYear)`, `EncryptedSecurityCode (encryptedSecurityCode)`, `HolderName (holderName)` all `string?`; `Type (type): AdyenApIs.Models.Enums.Type12?` defaults `Type12.Scheme` · `Reference (reference): string ≤80, req` · `ReturnUrl (returnUrl): string, req` · `CaptureDelayHours (captureDelayHours): int?` (delay authorisation→auto-capture) · `ShopperInteraction (shopperInteraction): AdyenApIs.Models.Enums.ShopperInteraction?` · `ShopperReference (shopperReference): string?` · `Channel (channel): AdyenApIs.Models.Enums.Channel2?`. Left out: billing/delivery address, browserInfo, threeDS2RequestData, shopperIP/email (3DS-related; this API has no front end for challenges) | `PspReference: string?` · `ResultCode: AdyenApIs.Models.Enums.ResultCode1?` · `RefusalReason: string?` · `RefusalReasonCode: string?` · `Amount: AdyenApIs.Models.Amount25?` {`Currency`, `Value: long`} · `Action` (non-null ⇒ shopper action needed) | **A** `AdyenApIs.Core.Exceptions.ApiException<AdyenApIs.Errors.CreatePaymentError>` · `TryGetServiceError(out AdyenApIs.Models.ServiceError)` [400,401,403,422,500] · `TryGetRawError(out AdyenApIs.Core.ErrorResponse.RawError)` [fallback] · ServiceError: `ErrorCode`, `ErrorType`, `Message`, `PspReference` (string?), `Status` (int?) | none | `map/operations/Payments.md`; `Requests/Payments/CreatePaymentRequest.cs`; `Models/PaymentRequest.cs`; `Models/AnyOf/PaymentMethod111.cs`; `Models/Card.cs`; `Models/Amount2.cs`; `Models/PaymentResponse.cs`; `Models/Enums/ResultCode1.cs`; `Errors/CreatePaymentError.cs`; `Models/ServiceError.cs`; remarks `Api/Payments.cs` |
| `client.Modifications` · `Task<AdyenApIs.Models.PaymentRefundResponse> RefundPayment(AdyenApIs.Requests.Modifications.RefundPaymentRequest request, RequestOptions? = null, CancellationToken = default)` · `POST /payments/{paymentPspReference}/refunds` | `PaymentPspReference: string, req` · `IdempotencyKey: string?` (REAL key) · `Body: AdyenApIs.Models.PaymentRefundRequest?` | `Amount (amount): AdyenApIs.Models.Amount37, req` {`Currency`, `Value: long`} · `MerchantAccount (merchantAccount): string, req` · `Reference (reference): string? ≤80` · `MerchantRefundReason (merchantRefundReason): AdyenApIs.Models.Enums.MerchantRefundReason?` (not used). Remarks: refunds a **captured** payment; multiple partial refunds allowed while their sum ≤ captured amount; outcome async (REFUND webhook) | `PspReference: string, req` · `PaymentPspReference: string, req` · `Amount: Amount38, req` · `Status: string` (const `"received"`) · `Reference: string?` | **A** `ApiException<AdyenApIs.Errors.RefundPaymentError>` · `TryGetServiceError(out ServiceError)` [400,401,403,422,500] · `TryGetRawError` [fallback] | none | `map/operations/Modifications.md`; `Requests/Modifications/RefundPaymentRequest.cs`; `Models/PaymentRefundRequest.cs`; `Models/Amount37.cs`; `Models/PaymentRefundResponse.cs`; `Errors/RefundPaymentError.cs`; remarks `Api/Modifications.cs` |
| `client.Modifications` · `Task<AdyenApIs.Models.PaymentReversalResponse> ReversePayment(AdyenApIs.Requests.Modifications.ReversePaymentRequest request, RequestOptions? = null, CancellationToken = default)` · `POST /payments/{paymentPspReference}/reversals` | `PaymentPspReference: string, req` · `IdempotencyKey: string?` · `Body: AdyenApIs.Models.PaymentReversalRequest?` | `MerchantAccount (merchantAccount): string, req` · `Reference (reference): string?`. Remarks: refunds if captured, cancels if not; always the full amount | `PspReference: string, req` · `PaymentPspReference: string, req` · `Status` const `"received"` | **A** `ApiException<AdyenApIs.Errors.ReversePaymentError>` · `TryGetServiceError(out ServiceError)` [400,401,403,422,500] · `TryGetRawError` | none | `map/operations/Modifications.md`; `Requests/Modifications/ReversePaymentRequest.cs`; `Models/PaymentReversalRequest.cs`; `Models/PaymentReversalResponse.cs` |

**Enum values used**

| Enum (`AdyenApIs.Models.Enums`) | Members used | Source |
| --- | --- | --- |
| `ResultCode1` | `Authorised` (final, success) · `Refused`, `Error`, `Cancelled` (final, not paid) · `RedirectShopper`, `ChallengeShopper`, `IdentifyShopper`, `PresentToShopper` (shopper action — unsupported here) · `PartiallyAuthorised` · `Pending`, `Received` (not final) · others (`AuthenticationFinished`, `AuthenticationNotRequired`, `Success`, undeclared) → `otherwise` | `Models/Enums/ResultCode1.cs` |
| `Type12` | `Scheme` (default on `Card`) | `Models/Enums/Type12.cs` |
| `ShopperInteraction` | `Ecommerce` | `Models/Enums/ShopperInteraction.cs` |
| `Channel2` | `Web` | `Models/Enums/Channel2.cs` |

**Client / auth / server facts**

| Fact | Value | Source |
| --- | --- | --- |
| Client | `AdyenApIs.AdyenApIsClient(HttpClient, AdyenApIs.AdyenApIsClientOptions)` — only ctor | `sdk-map.md` Getting a client |
| Auth | Operations above: `BasicAuth` OR `ApiKeyAuth`; we set **only** `options.ApiKeyAuth` (→ `X-API-Key`) | `sdk-map.md` Servers & auth |
| Environments | only `AdyenApIs.Servers.ServerEnvironment.Production` (default) whose `Default` group base URL is `https://checkout-test.adyen.com/v71` (the TEST host) | `Servers/ServerEnvironment.cs`, `Servers/DefaultOptions.cs` |
| Base-URL override | `options.Server.Default.Production.BaseUrl` (re-read per request) | `Servers/DefaultOptions.cs` |
| Retry | `AdyenApIs.Core.Configuration.RetryOptions` (`Default()`/`Disabled()`, all members required) | `Core/Configuration/RetryOptions.cs` |
| Logging | `AdyenApIs.Core.Configuration.LoggingOptions` `{LoggerFactory, LogRequestBody, LogRequestHeaders, …}`; env var `ADYENAPISCLIENT_LOG` | `Core/Configuration/LoggingOptions.cs`, `sdk-map.md` |
| Exceptions | `AdyenApIs.Core.Exceptions.{SdkException, ApiException, ApiException<T>, ResponseDeserializationException, SdkConnectionException, SdkTimeoutException, AuthSchemeException}` | `Core/Exceptions/*.cs` |

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| `RefundPayment.PaymentPspReference` must be the `pspReference` that `CreatePayment` returned with `Authorised` for THIS order — never caller-supplied | `RefundPayment` ← `CreatePayment` | `OrderPaymentService.RefundOrderAsync` reads `Order.PaymentPspReference`; refuses when the order is not paid |
| Refund `Amount.Currency` must equal the currency the payment was charged in | `RefundPayment` ← `CreatePayment` | `OrderPaymentService.RefundOrderAsync` uses `Order.Currency` (set at placement, charged at pay) |
| Σ refunds ≤ amount `CreatePayment` authorised for this order | `RefundPayment` ← `CreatePayment` | `Order.ReserveRefund` guard, committed under the order's concurrency token before `RefundPayment` is called |
| `CreatePayment.Amount` = order total (from catalog prices stored on order items), in the order's currency | `CreatePayment` ← `POST api/orders` (catalog prices) | `OrderPaymentService.PayOrderAsync` computes it server-side; request carries no amount |
| A resend after an unknown outcome reuses the `IdempotencyKey` of the original attempt | `CreatePayment`/`RefundPayment` ← their own earlier call | `PaymentAttempt.IdempotencyKey` / `OrderRefund.IdempotencyKey`, read by the settle paths |
| `ReversePayment.PaymentPspReference` = the pspReference of the partially/mismatched authorisation just returned | `ReversePayment` ← `CreatePayment` | `AdyenPaymentGateway.ChargeAsync` (same call) |

## 3. Trap notes

| Step | Hazard → consequence | Load |
| --- | --- | --- |
| 3 | Client lifetime and `HttpClient` ownership — a per-request client or a singleton over the shared default client leaks handlers / caches DNS / leaks timeouts to other consumers | MUST load `adyen:dotnet-client-initialization` |
| 3 | A blank API key is not an error to the SDK — the call goes out unauthenticated and fails as a 401 at the first payment | MUST load `adyen:dotnet-authentication` |
| 3/4 | What `Retry.Timeout` vs `HttpClient.Timeout` vs a token actually bound — getting it wrong breaks the 30 s caller guarantee | MUST load `adyen:dotnet-configuration-resilience` |
| 4 | Which verbs the SDK resends, and what an `Idempotency-Key` header on the wire does and does not prove | MUST load `adyen:dotnet-configuration-resilience` |
| 4 | Building the `PaymentMethod111` union and reading the open `ResultCode1` enum (`ToString()` vs wire value, undeclared values) | MUST load `adyen:dotnet-models` |
| 4 | Request-record shape, `requestOptions` positional slot before the token | MUST load `adyen:dotnet-calling-endpoints` |
| 4 | Which exception types actually reach the catch; ordering of `TryGet…` accessors; leaking SDK messages to callers | MUST load `adyen:dotnet-error-handling` |
| 4 | A body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`). | MUST load `adyen:dotnet-error-handling` |
| 4 | Transport failure on a write leaves the outcome unknown — reporting it as a failure invites a second charge | MUST load `adyen:dotnet-configuration-resilience` |
| 4 | Card fields in request bodies vs SDK body logging and its env-var switch | MUST load `adyen:dotnet-configuration-resilience` |
| 7 | Which seam to stub; request bodies are disposed before the test reads them | MUST load `adyen:dotnet-testing` |

## 4. REQUIRED READING (load all **before implementation starts**; this sheet deliberately does not carry their contents)

- `adyen:dotnet-client-initialization` — step 3 (client + DI)
- `adyen:dotnet-authentication` — step 3 (credential + fail-fast)
- `adyen:dotnet-calling-endpoints` — step 4 (calls)
- `adyen:dotnet-models` — step 4 (union, enums)
- `adyen:dotnet-error-handling` — step 4 (exception boundary)
- `adyen:dotnet-configuration-resilience` — steps 3–4 (timeouts, retries, logging, unknown outcomes)
- `adyen:dotnet-testing` — step 7

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `AdyenSettings` bound from `Adyen:` section with `[Required]`+custom validation (`ApiKey`, `MerchantAccount` non-blank; `Environment` ∈ {test, live}; `Currency` = known ISO-4217 3-letter code); `ValidateOnStart()` in PublicApi → host refuses to start, message names the key, never the value. Only `ApiKeyAuth` is used, so Basic auth parts are not validated. |
| 2 | Secret sourcing & rotation | Development: .NET user-secrets of `src/PublicApi` (`Adyen:ApiKey` etc., loaded from `ADYEN_*` env vars by the operator); other deployments: any `IConfiguration` source (`Adyen__ApiKey` env var, Key Vault). Options are captured once when the singleton client is built → a rotated key needs a process restart (accepted; documented). |
| 3 | Total timeout budget | Caller waits ≤ **30 s**: one linked `CancellationTokenSource` per API request with a **25 s** Adyen budget, shared by every SDK call in that handler (charge + settle-resend + reversal; refund settle sweep + refund). Per attempt `Retry.Timeout` = 10 s, `HttpClient.Timeout` = 12 s backstop. On expiry the caller gets 504 "Adyen did not respond …". |
| 4 | Write-retry ownership | All three operations are `POST`; client built with `RetryOptions.Disabled() with { Timeout = 10s }` so the SDK never resends regardless of `HttpMethodsToRetry`. The only resend is ours: an explicit same-key resend after an unknown outcome. |
| 5 | Idempotency & ambiguous writes | `CreatePayment`: `IdempotencyKey` = per-attempt GUID stored on `PaymentAttempt` before the call (not derived from order id: the in-memory DB reuses ids across restarts). `RefundPayment`: per-refund GUID stored on `OrderRefund`. `ReversePayment`: key = `rev-` + attempt key. The generator's header slot is fed by these record members (`new HeaderParam("Idempotency-Key", request.IdempotencyKey)` in `Api/Payments.cs`/`Api/Modifications.cs`), so they are real keys. |
| 6 | Observability | SDK built-in logger on, `LoggerFactory` set explicitly to the host's; request/response lines at Information, failures Warning/Error; `LogRequestBody`/headers **off**. Our own logs: order id, attempt id, pspReference, resultCode, refusalReasonCode, Adyen `ServiceError.ErrorCode`/`PspReference` (Adyen's correlation id) — never card fields or holder name. |
| 7 | Sensitive data | `Card` carries encrypted card number/expiry/CVC + holder name → `LogRequestBody=false`, `LoggerFactory` assigned explicitly (disables `ADYENAPISCLIENT_LOG`), card fields never persisted and never logged; API DTO not echoed. |
| 8 | Environment selection | One server group used (`Default`, Checkout). SDK declares only `Production` whose URL is the TEST host. `Adyen:Environment=test` → SDK default URL. `Adyen:Environment=live` → host refuses to start unless `Adyen:CheckoutBaseUrl` (account-specific live URL) is set, so test traffic can never silently go live and live never silently goes to test. `Default1` (Management) is not touched. |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS. |
| 10 | Partial results | N/A — no paged reads; `GET api/my-orders` reads only the local store. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

### DUPLICATE CLAIMS

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| `CreatePayment` (pay order) | `PaymentAttempts` table in `CatalogContext` — row PK `pa_{orderId}_{n}`, n = attempts so far + 1, status `InFlight`, carrying the GUID idempotency key | Primary-key uniqueness: concurrent callers compute the same PK; second insert refused (InMemory `ArgumentException`, relational `DbUpdateException`) | `PaymentStateStore.TryClaimAsync` catch (`DbUpdateException`/`ArgumentException`, key confirmed taken) → `PayOrderAsync` returns `PaymentInProgress` (409) | `OrderPaymentService.PayOrderAsync` (`_stateStore.TryClaimAsync(attempt)` → `PaymentStateStore.TryClaimAsync`) → `OrderPaymentService.ChargeAndRecordAsync` (`_gateway.ChargeAsync` → `AdyenPaymentGateway.ChargeAsync`) |
| `RefundPayment` (refund order) | (a) `OrderRefunds` table — row PK `rf_{orderId}_{hash(Idempotency-Key)}` or `rf_{guid}`, status `InFlight`; (b) amount reservation on `Order.AmountRefunded` under `Order.PaymentConcurrencyStamp` | (a) PK uniqueness (same caller key ⇒ same PK); (b) concurrency token: a second concurrent reservation fails with `DbUpdateConcurrencyException`, is re-read and re-checked against the refundable balance | (a) `PaymentStateStore.TryClaimAsync` catch (`DbUpdateException`/`ArgumentException`) → `RefundOrderAsync` returns the existing refund (`Replay`); (b) `PaymentStateStore.TrySaveOrderAsync` catch `DbUpdateConcurrencyException` → `OrderPaymentService.UpdateOrderAsync` re-reads and re-checks | `OrderPaymentService.RefundOrderAsync` (`_stateStore.TryClaimAsync(refund)`, then `UpdateOrderAsync(o => o.ReserveRefund(amount))`) → `OrderPaymentService.SubmitRefundAsync` (`_gateway.RefundAsync` → `AdyenPaymentGateway.RefundAsync`) |

Release: a refused/failed payment attempt is marked terminal (`Refused`/`Failed`), so the next attempt gets a new PK; an `InFlight` attempt older than 2 min is treated as unknown and settled by same-key resend. A failed refund releases its reservation.

### PAGED READS

none

### UNKNOWN OUTCOMES

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `CreatePayment` | `CreatePayment` re-sent with the same `IdempotencyKey` (no payment lookup operation exists in the map's Payments/Modifications pages) | `PaymentAttempt.IdempotencyKey` | in-call: `AdyenPaymentGateway.ChargeAsync` catch (`SdkConnectionException`/`SdkTimeoutException`/2xx `ResponseDeserializationException`) → one same-key resend within budget; still unknown → `PaymentAttempt.Status=Unknown`, `Order.PaymentStatus=PaymentPending`; settled by `OrderPaymentService.PayOrderAsync` (`latest.NeedsSettlement` branch → same attempt, same key) → `ChargeAndRecordAsync` on the next pay call | `AdyenPaymentGatewayTests.Charge_connection_failure_then_success_resends_same_idempotency_key`, `AdyenPaymentGatewayTests.Charge_connection_failure_twice_reports_unknown`, `OrderPaymentServiceTests.Unknown_attempt_is_settled_with_same_key_on_next_pay` |
| `RefundPayment` | `RefundPayment` re-sent with the same `IdempotencyKey` | `OrderRefund.IdempotencyKey` | in-call: `AdyenPaymentGateway.RefundAsync` catch → one same-key resend; still unknown → `OrderRefund.Status=Unknown` (amount stays reserved); settled by the settle loop at the start of `OrderPaymentService.RefundOrderAsync` (`NeedsSettlement` → `SubmitRefundAsync`, same key) on every refund request for that order | `AdyenPaymentGatewayTests.Refund_timeout_then_success_resends_same_idempotency_key`, `OrderPaymentServiceTests.Unknown_refund_is_settled_before_next_refund` |
| `ReversePayment` | `ReversePayment` re-sent with the same key | `rev-{attemptId}` | `AdyenPaymentGateway.ReverseAsync` (via `SendSettlingAsync` → same-key resend); still unknown → `ChargeStatus.ReversalUnknown` → `PaymentAttempt.MarkReversed(…, reversalConfirmed: false)` = `ReversalUnknown` and logged at Error with pspReference for operator follow-up (rare path: partial authorisation only) | `AdyenPaymentGatewayTests.Partial_authorisation_is_reversed` |

## 6. Assumptions & Blockers

- **Blockers: none.**
- Assumption: "take the money now" = `CaptureDelayHours = 0` (immediate auto-capture) on `CreatePayment`; refunds then apply to the captured payment (`RefundPayment` remarks require a captured payment). `UNVERIFIED`: capture is asynchronous; a refund submitted seconds after authorisation is accepted (`received`) by Adyen and processed once captured.
- Assumption: refund outcome is asynchronous (REFUND webhook). No webhook receiver is in scope (no storefront, no inbound endpoint in the task); a refund is recorded as `Received` (accepted by Adyen) — not "settled".
- `UNVERIFIED`: replaying `CreatePayment`/`RefundPayment` with the same `IdempotencyKey` returns the original result instead of executing again (the record doc only says "unique identifier for the message"). Defensive directive: keys are generated once per attempt/refund, persisted before the call, and never reused for a different amount.
- `UNVERIFIED`: Adyen minor units follow ISO-4217 exponents for the configured currency. Defensive directive: amounts that are not exactly representable in minor units are rejected before any call; unknown currency codes fail startup.
- Shopper-facing refusal text: built from Adyen's `refusalReason` plus an actionable instruction; no hard-coded table of Adyen refusal codes (not in the map).
- 3-D Secure / redirect results (`action` present) are not supported (API-only, no front end): the attempt is closed as not paid and the shopper is told to use a card that does not require extra verification.
- `POST api/orders`: ship-to address optional (API-only orders); when absent an empty address is stored because `Order.ShipToAddress` is required.

## 7. Source labels

Every contract row above cites its map page / declaring file. Application decisions (claims, statuses, budgets, HTTP mapping) are `YOUR CALL — not in the map`, decided per the task.

## 8. Verification record (2026-10-08)

- `dotnet test eShopOnWeb.sln`: 133 passed, 0 failed (offline; Adyen faked at the `HttpMessageHandler` seam or behind `IPaymentGateway`).
- Live, Adyen test: `CreatePayment` Authorised USD 51.00 (pspReference `BM4DCF9RKW8DCG65`); repeat pay → `AlreadyPaid`, no second call; `RefundPayment` partial USD 12.50 → `received` (pspReference `MMRTMZ4WBWW5KG75`); same-key replay not re-sent; over-refund refused locally (422); expired-card attempt → `Refused` / "Expired Card" (402), order left unpaid.
- Confirmed: `PaymentRefundResponse` came back 201 with all `required` members; no card fields or API key in the host log.
