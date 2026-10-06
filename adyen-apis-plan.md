# Adyen APIs — integration plan (eShopOnWeb PublicApi: card payments, refunds, support record)

SDK: Adyen APIs .NET SDK, root namespace `AdyenApIs`, referenced as source from the adyen plugin at
`sdk/dotnet/` (plugin-relative). All `source` cells below are relative to that SDK root.

## 1. Scope & sequence

| # | Step | Operations |
| --- | --- | --- |
| 1 | `global.json` roll-forward; baseline build/test of the untouched solution | — |
| 2 | Reference `AdyenApIs.csproj` from `src/Infrastructure`; bind `Adyen:` settings (`ApiKey`, `MerchantAccount`, `Environment`, `Currency`) with fail-fast validation; register the client over a named `HttpClient` | client construction |
| 3 | Domain: `Order.PaymentStatus` + `OrderPaymentAttempt` (PK `OrderId`+`AttemptNumber`) + `OrderRefund` (PK `OrderId`+`Sequence`), each keeping the raw Adyen JSON; EF config + migration | — |
| 4 | `POST /api/orders` — place an order from catalog item ids/quantities (prices from catalog), status `AwaitingPayment` | — |
| 5 | `POST /api/orders/{orderId}/pay` — claim attempt slot → `Payments.CreatePayment` → record | `Payments.CreatePayment` |
| 6 | `POST /api/orders/{orderId}/refunds` (Administrators) — claim refund slot (amount reserved) → `Modifications.RefundPayment` → record | `Modifications.RefundPayment` |
| 7 | `GET /api/my-orders`, `GET /api/orders/{orderId}/adyen-record` (Administrators) | — (local reads) |
| 8 | Offline tests (stub `HttpMessageHandler`), migration, live self-verification on the test merchant | — |

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, verbatim — each operation that takes input takes ONE request record as its first parameter, built with an object initializer whose property names are the record's own, never flat arguments.
> ⚠ Every SDK type is written fully-qualified with the namespace its source path implies (`Models/` → `AdyenApIs.Models`, `Models/AnyOf/` → `AdyenApIs.Models.AnyOf`, `Models/Enums/` → `AdyenApIs.Models.Enums`, `Requests/Payments/` → `AdyenApIs.Requests.Payments`, `Requests/Modifications/` → `AdyenApIs.Requests.Modifications`, `Errors/` → `AdyenApIs.Errors`), taken from the path the map gives for THAT type.

| Controller · method | Request record (members) | Body model (fields used: `Name (wire)`: type, required?) | Response + fields read | Error case | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| `client.Payments` · `Task<PaymentResponse> CreatePayment(CreatePaymentRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `AdyenApIs.Requests.Payments.CreatePaymentRequest`: `IdempotencyKey: string?` (header `Idempotency-Key`, "unique identifier for the message, max 64 chars") · `Body: PaymentRequest?` | `AdyenApIs.Models.PaymentRequest`: `Amount (amount): Amount2, required` · `MerchantAccount (merchantAccount): string, required` · `PaymentMethod (paymentMethod): AdyenApIs.Models.AnyOf.PaymentMethod111, required` · `Reference (reference): string, required, max 80` · `ReturnUrl (returnUrl): string, required` · optional used: `ShopperReference (shopperReference): string?` (3–256, no PII) · `ShopperInteraction (shopperInteraction): ShopperInteraction?` · `Channel (channel): Channel2?` · `CaptureDelayHours (captureDelayHours): int?` · `MerchantOrderReference (merchantOrderReference): string?`. `Amount2`: `Currency (currency): string, required, len 3` · `Value (value): long, required` (minor units). `PaymentMethod111.Card(Card)` factory; `AdyenApIs.Models.Card`: `EncryptedCardNumber (encryptedCardNumber)`, `EncryptedExpiryMonth (encryptedExpiryMonth)`, `EncryptedExpiryYear (encryptedExpiryYear)`, `EncryptedSecurityCode (encryptedSecurityCode)`, `HolderName (holderName)`: all `string?`; `Type (type): Type12?` defaults `Type12.Scheme` | `AdyenApIs.Models.PaymentResponse`: `ResultCode (resultCode): ResultCode1?` · `PspReference (pspReference): string?` · `RefusalReason (refusalReason): string?` · `RefusalReasonCode (refusalReasonCode): string?` · `MerchantReference (merchantReference): string?` · `Action (action): ActionModel?`. **No extension-data bag** on this model | A: `ApiException<AdyenApIs.Errors.CreatePaymentError>`; `TryGetServiceError(out AdyenApIs.Models.ServiceError)` [400,401,403,422,500] · `TryGetRawError(out RawError)` [fallback] | none | `map/operations/Payments.md`; `Requests/Payments/CreatePaymentRequest.cs`; `Models/PaymentRequest.cs`; `Models/Amount2.cs`; `Models/AnyOf/PaymentMethod111.cs`; `Models/Card.cs`; `Models/PaymentResponse.cs`; `Errors/CreatePaymentError.cs` |
| `client.Modifications` · `Task<PaymentRefundResponse> RefundPayment(RefundPaymentRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `AdyenApIs.Requests.Modifications.RefundPaymentRequest`: `PaymentPspReference: string, required` · `IdempotencyKey: string?` · `Body: PaymentRefundRequest?` | `AdyenApIs.Models.PaymentRefundRequest`: `Amount (amount): Amount37, required` · `MerchantAccount (merchantAccount): string, required` · optional used: `Reference (reference): string?`. Left out: `MerchantRefundReason`, `LineItems` (only for BNPL methods), `Splits`, `Store`, `CapturePspReference` (PayPal only). `Amount37`: `Currency: string, required` · `Value: long, required` | `AdyenApIs.Models.PaymentRefundResponse`: `PspReference (pspReference): string, required` · `PaymentPspReference: string, required` · `Amount: Amount38, required` · `Status: string` (const `received`). **No extension-data bag**. Remarks: outcome arrives asynchronously in a REFUND webhook; sum of partial refunds must not exceed captured amount | A: `ApiException<AdyenApIs.Errors.RefundPaymentError>`; `TryGetServiceError(out ServiceError)` [400,401,403,422,500] · `TryGetRawError(out RawError)` | none | `map/operations/Modifications.md`; `Requests/Modifications/RefundPaymentRequest.cs`; `Models/PaymentRefundRequest.cs`; `Models/Amount37.cs`; `Models/PaymentRefundResponse.cs`; `Api/Modifications.cs` (remarks) |

`AdyenApIs.Models.ServiceError` (`Models/ServiceError.cs`): `ErrorCode (errorCode): string?` · `ErrorType (errorType): string?` · `Message (message): string?` · `PspReference (pspReference): string?` · `Status (status): int?` · `AdditionalData`.

Enums (`Models/Enums/`, namespace `AdyenApIs.Models.Enums`):

| Enum | Members used (wire) |
| --- | --- |
| `ResultCode1` | `Authorised` · `Refused` · `Error` · `Cancelled` · `Pending` · `Received` · `RedirectShopper` · `IdentifyShopper` · `ChallengeShopper` · `PresentToShopper` · `PartiallyAuthorised` · `AuthenticationFinished` · `AuthenticationNotRequired` · `Success` (open enum — `Match(..., otherwise:)`) |
| `ShopperInteraction` | `Ecommerce` |
| `Channel2` | `Web` |

Client construction / auth / servers (`sdk-map.md`, `AdyenApIsClientOptions.cs`, `Servers/ServerEnvironment.cs`):
- Only ctor `new AdyenApIs.AdyenApIsClient(HttpClient, AdyenApIs.AdyenApIsClientOptions)`.
- Auth on both ops: `options.BasicAuth` OR `options.ApiKeyAuth` (`X-API-Key`). We set `ApiKeyAuth` only.
- `ServerEnvironment.Production` (only member, default) → group `Default` = `https://checkout-test.adyen.com/v71` (both ops are on `Default`). Override point `options.Server.Default.Production.BaseUrl`.
- Raw response access: `AdyenApIs.Core.Hooks.SdkHook.OnResponse(Func<HttpResponseMessage, HookContext, CancellationToken, ValueTask>)` via per-call `AdyenApIs.Core.RequestOptions { Hooks = [...] }` (`Core/Hooks/SdkHook.cs`, `Core/RequestOptions.cs`). The SDK sends with `HttpCompletionOption.ResponseHeadersRead` and deserializes from `ReadAsStreamAsync` after hooks run (`Core/RawClient.cs`, `Core/Response/JsonResponse.cs`) — a hook that buffers (`LoadIntoBufferAsync`) and reads the content leaves it re-readable for the SDK. Verified offline (`PaymentFlowTests.AdyenRecord_ReturnsEverythingAdyenSent_ForPaymentsAndRefunds`) and against the live test endpoint.

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| `RefundPaymentRequest.PaymentPspReference` must be the `pspReference` of an **Authorised** payment this app stored for that same order | `RefundPayment` ← `CreatePayment` (stored `OrderPaymentAttempt` with status `Authorised`) | refund service, before `RefundPayment`: the psp reference is read from the order's own authorised attempt, never from the caller |
| Refund amount + all non-failed refunds of the order ≤ the amount `CreatePayment` authorised for that order (same currency) | `RefundPayment` ← `CreatePayment` + earlier `RefundPayment` rows | refund service after claiming the refund slot, before `RefundPayment` |
| `CreatePayment` amount = order total in minor units of `Adyen:Currency`, exactly (no rounding) | `CreatePayment` ← `Order.Total()` | pay service before `CreatePayment` (reject non-representable totals) |

## 3. Trap notes

| Step | Hazard → consequence | Skill |
| --- | --- | --- |
| 2 | An unset credential sends unauthenticated requests with no exception → 401 at the first payment, not at boot | MUST load `adyen:dotnet-authentication` |
| 2 | Client/HttpClient lifetime and DNS staleness of a singleton; the DI extension resolves the shared default `HttpClient` | MUST load `adyen:dotnet-client-initialization` |
| 2 | What `Retry.Timeout` and `HttpClient.Timeout` actually bound; which verbs are resent; the log env var that can turn on body logging | MUST load `adyen:dotnet-configuration-resilience` |
| 5,6 | Request record vs body model; `requestOptions` sits before `cancellationToken` | MUST load `adyen:dotnet-calling-endpoints` |
| 5 | `PaymentMethod111` is a union (factory, not initializer); enums are open records whose `ToString()` is not the wire value | MUST load `adyen:dotnet-models` |
| 5,6 | Which exception types reach the catch; a typed catch alone misses deserialization / connection failures; on a write a transport failure is an unknown outcome | MUST load `adyen:dotnet-error-handling` |
| 8 | Which seam to stub; request bodies disposed after the call | MUST load `adyen:dotnet-testing` |

## 4. REQUIRED READING (load before implementation starts; this sheet deliberately does not carry their contents)

- `adyen:dotnet-client-initialization` — client + named HttpClient registration (step 2)
- `adyen:dotnet-authentication` — API key + startup validation (step 2)
- `adyen:dotnet-configuration-resilience` — timeouts, retries, hooks, logging, unknown outcomes (steps 2, 5, 6)
- `adyen:dotnet-calling-endpoints` — building request records (steps 5, 6)
- `adyen:dotnet-models` — union/enums (step 5)
- `adyen:dotnet-error-handling` — the error boundary (steps 5, 6)
- `adyen:dotnet-testing` — offline tests (step 8)
- Hazard (verbatim): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `AdyenSettings` bound from `Adyen:` with `ValidateOnStart()`; `ApiKey`, `MerchantAccount`, `Currency` (3 letters), `Environment` (`test`/`live`) must be non-blank; `live` additionally requires `Adyen:CheckoutBaseUrl`. Host refuses to start; message names the key, never the value. |
| 2 | Secret sourcing & rotation | Dev: .NET user-secrets on PublicApi (`Adyen:*`), populated from `ADYEN_*` env vars; deployments: any `IConfiguration` source (`Adyen__ApiKey` etc.), plus a lowest-priority mapping of the `ADYEN_*` variables onto `Adyen:*`. Options are built once into the singleton client → a rotated key takes effect on restart (documented; no hot rotation). |
| 3 | Total timeout budget | Retries disabled (`RetryOptions.Disabled()`); per-attempt `Retry.Timeout` 20s; named `HttpClient.Timeout` 25s backstop; each Adyen call bounded by its own 25s `CancellationTokenSource` (not linked to `RequestAborted` — a shopper closing the tab must not cut a payment mid-flight). Pay/refund handler worst case = 1 call + 1 idempotent replay ≈ 50s. |
| 4 | Write-retry ownership | Both writes are `POST` → never resent by the SDK; retries disabled outright. The only resend is our explicit replay with the **same** idempotency key. |
| 5 | Idempotency & ambiguous writes | Real key on both records: `CreatePaymentRequest.IdempotencyKey` = the attempt row's GUID; `RefundPaymentRequest.IdempotencyKey` = the refund row's GUID. Generator-injected header not relied on (these ops feed the header from the record member). |
| 6 | Observability | SDK logger gets the host `ILoggerFactory` explicitly; `LogRequestBody`/headers off. App logs at Information: order id, attempt/refund id, resultCode, pspReference; Warning on refusals/unknown outcomes with Adyen `errorCode`/`pspReference` from `ServiceError`. Raw responses go to the DB record, not logs. |
| 7 | Sensitive data | `PaymentRequest` carries encrypted card fields + holder name: `LogRequestBody` stays off, `LoggerFactory` assigned explicitly (disables `ADYENAPISCLIENT_LOG`), card fields never persisted or logged; only the response (no card data in the fields read) is stored. |
| 8 | Environment selection | One server group used (`Default`, checkout). SDK's only environment points at the test host. `Adyen:Environment=test` → SDK default (test). `live` → `options.Server.Default.Production.BaseUrl = Adyen:CheckoutBaseUrl` (required, from Customer Area). Any other value refuses to boot, so test traffic cannot silently reach live and vice versa. |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS. |
| 10 | Partial results | N/A — no paged reads in scope. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

### DUPLICATE CLAIMS

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| Pay order (`CreatePayment`) | `OrderPaymentAttempts` table in `CatalogContext`, PK (`OrderId`, `AttemptNumber`), inserted as `InFlight` before the SDK call; earlier non-failed attempt ⇒ the new claim is withdrawn (409 / already-paid result) | duplicate primary key on (`OrderId`, `AttemptNumber`) | `catch (DbUpdateException or InvalidOperationException or ArgumentException)` in `OrderPaymentStore.TryInsertAsync`, confirmed by re-reading the key (a different failure is rethrown) → 409 "payment already in progress" | `OrderPaymentStore.TryClaimPaymentAttemptAsync` → `AdyenPaymentGateway.AuthoriseCardPaymentAsync` (called from `OrderPaymentService.PayAsync`) |
| Refund (`RefundPayment`) | `OrderRefunds` table, PK (`OrderId`, `Sequence`), inserted `Requested` with the amount reserved before the SDK call | duplicate primary key on (`OrderId`, `Sequence`) | same catch in `OrderPaymentStore.TryInsertAsync` → 409 "another refund is being processed" | `OrderPaymentStore.TryClaimRefundAsync` → `AdyenPaymentGateway.RefundAsync` (called from `OrderPaymentService.RefundAsync`) |

Order: claim → SDK call → record; claim released (attempt `Refused`/`Rejected`/`ActionRequired`, refund `Failed`) when Adyen refuses. Store-level proof that the second claim is refused: `IntegrationTests/Repositories/OrderPaymentStoreTests/Claims.cs`.

### PAGED READS

none

### UNKNOWN OUTCOMES

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `CreatePayment` | `CreatePayment` replayed with the same `IdempotencyKey` and body (in the catch); if still unknown, attempt stays `Unknown` and blocks new attempts; the next pay call settles it by replaying with the stored key | the attempt's `IdempotencyKey` (+ `Reference`) | `AdyenPaymentGateway.AuthoriseCardPaymentAsync` (replay in catch) + `OrderPaymentService.PayAsync` (settles a stored `Unknown` attempt) | `PaymentFlowTests.Pay_ConnectionFailure_ReplaysWithSameIdempotencyKey` / `Pay_ConnectionFailureTwice_LeavesOrderPendingAndBlocksSecondCharge` |
| `RefundPayment` | `RefundPayment` replayed with the same `IdempotencyKey` and identical body (rebuilt from the stored row); if still unknown, refund stays `Unknown` with amount reserved; the next refund call on the order replays it first | the refund's `IdempotencyKey` (+ `Reference`) | `AdyenPaymentGateway.RefundAsync` (replay in catch) + `OrderPaymentService.RefundAsync` (settles stored `Unknown` refunds) | `PaymentFlowTests.Refund_ConnectionFailure_ReplaysWithSameIdempotencyKey` / `Refund_UnknownOutcome_KeepsAmountReserved_AndIsSettledBeforeTheNextRefund` |

## 6. Assumptions & Blockers

- Assumption: Adyen honours `Idempotency-Key` by returning the original result for a replayed key (per the record member's doc "unique identifier for the message"); exact retention window `UNVERIFIED`. Defensive directive: a replay that comes back as a 4xx `ServiceError` keeps the row `Unknown` (never marks it failed/released).
- Assumption: card payments via encrypted fields, no 3-D Secure flow. Result codes needing shopper action (`RedirectShopper`, `IdentifyShopper`, `ChallengeShopper`, `PresentToShopper`) are recorded and reported as "additional authentication not supported — use another card"; the action is never returned, so the payment cannot complete later.
- Assumption: `CaptureDelayHours = 0` → immediate capture ("taking the money now"), per the field doc "delay between the authorisation and scheduled auto-capture, in hours".
- Gap (reported, non-blocking): refund **final** outcome arrives only via a REFUND webhook (`Api/Modifications.cs` remarks); the map carries no notification models and `Core/Webhooks` has no generated events for this SDK, so webhook handling is out of scope. Refunds are recorded as `Received` (Adyen accepted the request).
- No blockers.
