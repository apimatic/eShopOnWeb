# Adyen APIs integration plan — eShopOnWeb card payments

SDK source: `sdk/dotnet/` inside the **adyen** plugin (context-plugins marketplace). All `source` cells below are relative to that SDK root.

## 1. Scope & sequence

| # | Step | Adyen operations |
| --- | --- | --- |
| 1 | `global.json` roll-forward; reference `sdk/dotnet/AdyenApIs.csproj` from `src/Infrastructure`; baseline build | — |
| 2 | Domain: `Order.PaymentStatus` + concurrency token; `OrderPaymentAttempt`, `OrderRefund`, `AdyenRecordEntry` entities kept with the order; EF config + SQL Server migration | — |
| 3 | `AdyenSettings` bound from `Adyen:` (`ApiKey`, `MerchantAccount`, `Environment`, `Currency`), validated on start; named `HttpClient` + singleton `AdyenApIsClient` | — |
| 4 | `AdyenPaymentGateway` (Infrastructure) behind `IPaymentGateway` (ApplicationCore): authorise+capture, refund, raw-body capture via per-call `SdkHook` | `Payments.CreatePayment`, `Modifications.RefundPayment` |
| 5 | `OrderPaymentService` (ApplicationCore): claim → SDK call → record; replay-on-unknown | via gateway |
| 6 | PublicApi endpoints: `POST /api/orders`, `POST /api/orders/{orderId}/pay`, `POST /api/orders/{orderId}/refunds` (admin), `GET /api/my-orders`, `GET /api/orders/{orderId}/adyen-record` (admin) | via service |
| 7 | Tests (no network): gateway over stub `HttpMessageHandler`; endpoints over `WebApplicationFactory` with the Adyen handler stubbed | — |
| 8 | Live verification against Adyen test: payment, partial refund, support record | `CreatePayment`, `RefundPayment` |

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, verbatim. Each operation that takes input takes ONE request record as its first parameter (an operation with no inputs takes none), built with an object initializer whose property names are the record's own — never flat arguments.
> ⚠ Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type, never from where a neighbouring type sits.

| Controller · method | Request record + members | Body model + fields used | Response + fields read | Error case | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| `client.Payments` · `Task<AdyenApIs.Models.PaymentResponse> CreatePayment(AdyenApIs.Requests.Payments.CreatePaymentRequest request, AdyenApIs.Core.RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `CreatePaymentRequest`: `IdempotencyKey: string?` (header `Idempotency-Key`, sent from `request.IdempotencyKey` — a REAL caller key, max 64 chars, UUID recommended) · `Body: PaymentRequest?` | `AdyenApIs.Models.PaymentRequest`: `Amount (amount): Amount2, required` · `MerchantAccount (merchantAccount): string, required` · `PaymentMethod (paymentMethod): AdyenApIs.Models.AnyOf.PaymentMethod111, required` — built with factory `PaymentMethod111.Card(Card)` · `Reference (reference): string, required, ≤80` · `ReturnUrl (returnUrl): string, required, no PII` · `CaptureDelayHours (captureDelayHours): int?` (0 = capture immediately, "taking the money now") · `ShopperInteraction (shopperInteraction): AdyenApIs.Models.Enums.ShopperInteraction?` — left unset (doc: Ecommerce assumed by default). Left out: everything else (3DS2/browserInfo, shopper PII, lineItems, splits, recurring). `AdyenApIs.Models.Amount2`: `Currency (currency): string, required, len 3` · `Value (value): long, required, minor units`. `AdyenApIs.Models.Card`: `EncryptedCardNumber (encryptedCardNumber)`, `EncryptedExpiryMonth (encryptedExpiryMonth)`, `EncryptedExpiryYear (encryptedExpiryYear)`, `EncryptedSecurityCode (encryptedSecurityCode)`, `HolderName (holderName)`: all `string?`; `Type (type): Type12?` defaults to `Type12.Scheme` | `AdyenApIs.Models.PaymentResponse` (direct, no envelope): `PspReference (pspReference): string?` · `ResultCode (resultCode): AdyenApIs.Models.Enums.ResultCode1?` · `RefusalReason (refusalReason): string?` · `RefusalReasonCode (refusalReasonCode): string?` · `Action (action)` presence. **No extension-data bag** on this model → raw body captured via hook | **A** `AdyenApIs.Core.Exceptions.ApiException<AdyenApIs.Errors.CreatePaymentError>` · `TryGetServiceError(out AdyenApIs.Models.ServiceError)` [400,401,403,422,500] · `TryGetRawError(out AdyenApIs.Core.ErrorResponse.RawError)` [fallback]. `ServiceError`: `ErrorCode (errorCode)`, `ErrorType (errorType)`, `Message (message)`, `PspReference (pspReference)`: `string?`; `Status (status): int?` | none | `map/operations/Payments.md`; `Requests/Payments/CreatePaymentRequest.cs`; `Models/PaymentRequest.cs`; `Models/Amount2.cs`; `Models/AnyOf/PaymentMethod111.cs`; `Models/Card.cs`; `Models/PaymentResponse.cs`; `Errors/CreatePaymentError.cs`; `Models/ServiceError.cs`; `Api/Payments.cs` (route `POST /payments`) |
| `client.Modifications` · `Task<AdyenApIs.Models.PaymentRefundResponse> RefundPayment(AdyenApIs.Requests.Modifications.RefundPaymentRequest request, AdyenApIs.Core.RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `RefundPaymentRequest`: `PaymentPspReference: string, required` (path) · `IdempotencyKey: string?` (header, REAL caller key) · `Body: PaymentRefundRequest?` | `AdyenApIs.Models.PaymentRefundRequest`: `Amount (amount): Amount37, required` · `MerchantAccount (merchantAccount): string, required` · `Reference (reference): string?` · `MerchantRefundReason (merchantRefundReason): AdyenApIs.Models.Enums.MerchantRefundReason?` — left unset. `AdyenApIs.Models.Amount37`: `Currency (currency): string, required` · `Value (value): long, required` | `AdyenApIs.Models.PaymentRefundResponse` (direct): `PspReference (pspReference): string, required` · `PaymentPspReference: string, required` · `Status (status)`: const `"received"`. Outcome is asynchronous (REFUND webhook per `<remarks>`) | **A** `ApiException<AdyenApIs.Errors.RefundPaymentError>` · `TryGetServiceError(out ServiceError)` [400,401,403,422,500] · `TryGetRawError(out RawError)` | none | `map/operations/Modifications.md`; `Requests/Modifications/RefundPaymentRequest.cs`; `Models/PaymentRefundRequest.cs`; `Models/Amount37.cs`; `Models/PaymentRefundResponse.cs`; `Errors/RefundPaymentError.cs`; `Api/Modifications.cs` (route `POST /payments/{paymentPspReference}/refunds`; partial refunds allowed while their sum ≤ captured amount) |

**Enum values used** — `ResultCode1` (`Models/Enums/ResultCode1.cs`): `Authorised` (final, success) · `Refused` (final; reason in `refusalReason`) · `Error` (final; reason in `refusalReason`) · `Cancelled` (final) · `Pending` / `Received` (not final) · `RedirectShopper` / `IdentifyShopper` / `ChallengeShopper` / `PresentToShopper` (further shopper action — not supported by this API-only flow) · `PartiallyAuthorised` · `AuthenticationFinished` / `AuthenticationNotRequired` (auth-only, not requested). Undeclared values reach the `otherwise` arm of `Match`.

**Client construction / auth / server** — `AdyenApIs.AdyenApIsClient(HttpClient, AdyenApIs.AdyenApIsClientOptions)` (only ctor; `AdyenApIsClient.cs`). Auth for both operations: `options.BasicAuth` OR `options.ApiKeyAuth` → we set only `ApiKeyAuth` (header `X-API-Key`). `options.Environment`: `AdyenApIs.Servers.ServerEnvironment.Production` is the ONLY member; its `Default` group base URL is `https://checkout-test.adyen.com/v71` (both operations are on group `Default`); override point `options.Server.Default.Production.BaseUrl`. Options: `Retry` (`AdyenApIs.Core.Configuration.RetryOptions`), `Logging` (`AdyenApIs.Core.Configuration.LoggingOptions`), `Hooks` (`AdyenApIs.Core.Hooks.SdkHook`). Source: `sdk-map.md` §Getting a client, §Servers & auth; `AdyenApIsClientOptions.cs`; `Servers/ServerEnvironment.cs`; `Core/Hooks/SdkHook.cs`; `Core/RequestOptions.cs`.

**Raw response capture** — `SdkHook.OnResponse(Func<HttpResponseMessage, HookContext, CancellationToken, ValueTask>)` per call through `RequestOptions.Hooks`; runs after `SendAsync(..., ResponseHeadersRead)` and before the SDK reads the body (`Core/RawClient.cs` `ExecuteResult`). The hook buffers the content (`LoadIntoBufferAsync`) and reads it as a string so the SDK's later read still sees the full body. Verified: gateway tests assert the typed result and the verbatim body from one forward-only stubbed stream; the live test-environment run (2026-10-07) stored Adyen's full payment and refund responses, including fields the SDK models do not declare (`additionalData`, `merchantReference` on the payment response).

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| `RefundPayment.PaymentPspReference` must be the `pspReference` this application stored from its own `Authorised` `CreatePayment` for THAT order (never caller-supplied) | `RefundPayment` ← `CreatePayment` (stored `OrderPaymentAttempt.PspReference`) | `OrderPaymentService.RefundAsync` reads the order's authorised attempt before calling the gateway; the refund request carries no psp reference |
| Refund amount: sum of non-failed refunds ≤ amount authorised+captured by `CreatePayment` | `RefundPayment` ← `CreatePayment` (stored `AmountMinor`) | `OrderPaymentService.RefundAsync` before the claim; the claim bumps `Order.PaymentVersion` so two concurrent refunds cannot both pass the check |
| `RefundPayment` currency = the currency the payment was taken in | `RefundPayment` ← `CreatePayment` (stored `Currency`) | `OrderPaymentService.RefundAsync` uses the stored attempt currency, not current config |
| `CreatePayment` amount = order total in minor units of `Adyen:Currency` | `CreatePayment` ← catalog prices (`Order.Total()`) | `OrderPaymentService.PayAsync` via `Money.ToMinorUnits` (throws if not representable) |

## 3. Trap notes

| Step | Hazard → consequence | Skill |
| --- | --- | --- |
| 3 | Credentials that are blank still produce an unauthenticated request → 401 at first payment instead of a failed boot | MUST load `adyen:dotnet-authentication` |
| 3 | Client/`HttpClient` lifetime and which `HttpClient` the DI extension resolves → stale DNS or a timeout leaking to other consumers | MUST load `adyen:dotnet-client-initialization` |
| 3 | What `Retry.Timeout` and `HttpClient.Timeout` actually bound, and which verbs are resent → a charge that blocks the shopper far longer than intended, or is sent twice | MUST load `adyen:dotnet-configuration-resilience` |
| 3 | Built-in logger switched on from outside the code / body logging → encrypted card blobs and holder names in logs | MUST load `adyen:dotnet-configuration-resilience` |
| 4 | Union construction for `paymentMethod`, open enum read-back of `resultCode` (`ToString()` trap) | MUST load `adyen:dotnet-models` |
| 4 | Request record vs body model; `requestOptions` positional slot before the token | MUST load `adyen:dotnet-calling-endpoints` |
| 4 | Which exception types reach the catch, ordering of `TryGet…`, unknown-outcome on transport failure of a write | MUST load `adyen:dotnet-error-handling` |
| 4 | Hook runs once per attempt and on the raw response stream → wrong or missing support record | MUST load `adyen:dotnet-configuration-resilience` |
| 7 | Which seam to stub; request body disposed after the call → tests asserting nothing | MUST load `adyen:dotnet-testing` |

## 4. REQUIRED READING (load before implementation starts — this sheet deliberately does not carry their contents)

Plugin-qualified names (the `dotnet-*` names are shared across APIMatic plugins; load the **adyen** copy):

- `adyen:dotnet-client-initialization` · step 3 (client + named HttpClient registration)
- `adyen:dotnet-authentication` · step 3 (credentials + fail-fast)
- `adyen:dotnet-configuration-resilience` · steps 3–5 (timeouts, retries, logging, hooks, unknown outcomes, duplicate claims)
- `adyen:dotnet-calling-endpoints` · step 4
- `adyen:dotnet-models` · step 4
- `adyen:dotnet-error-handling` · steps 4–6
- `adyen:dotnet-testing` · step 7

Hazard row (verbatim): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `AdyenSettings` bound from `Adyen:`; `ValidateOnStart` rejects blank `ApiKey`, blank `MerchantAccount`, `Environment` ≠ `test`, `Currency` not a known ISO-4217 code. Messages name the key, never the value. |
| 2 | Secret sourcing & rotation | Dev: .NET user-secrets of `src/PublicApi` (loaded from `ADYEN_*` env vars by the operator); the `ADYEN_*` env vars are also mapped onto the same `Adyen:*` keys at higher precedence so a deployment can point the same build at another account. Options are read once when the singleton client is built → rotation requires a restart (documented). |
| 3 | Total timeout budget | Writes only (POST). Per-attempt `Retry.Timeout` = `Adyen:TimeoutSeconds` (default 20 s); `HttpClient.Timeout` = that + 5 s backstop; gateway wraps each call in its own deadline CTS (timeout + 2 s) NOT linked to the shopper's request abort (a disconnect must not orphan a charge). Pay/refund handler makes at most 2 calls (original + one replay) → worst case ≈ 2 × 22 s ≈ 44 s. |
| 4 | Write-retry ownership | `Retry = RetryOptions.Disabled() with { Timeout = … }` → the SDK never resends either POST regardless of `HttpMethodsToRetry`. The only resend is our deliberate replay with the SAME idempotency key. |
| 5 | Idempotency & ambiguous writes | `CreatePayment.IdempotencyKey` = per-attempt GUID stored on `OrderPaymentAttempt` before the call; `RefundPayment.IdempotencyKey` = per-refund GUID stored on `OrderRefund` (derived from the operator's `idempotencyKey` when supplied). Provider replay semantics for a reused key are UNVERIFIED (the record doc only says "unique identifier for the message"); reconciliation = replay with the same key (no lookup-by-reference operation exists in `Payments`/`Modifications`). |
| 6 | Observability | Host `ILoggerFactory` assigned explicitly to `options.Logging.LoggerFactory`; SDK request/response lines at Information, failures Warning/Error; headers and bodies off. Our logs: order id, attempt no., merchant reference, pspReference, resultCode, refusalReasonCode, Adyen `ServiceError.ErrorCode`/`PspReference` on failures. |
| 7 | Sensitive data | Request carries encrypted card fields + holder name → `LogRequestBody=false`, `LogRequestHeaders=false`, `LoggerFactory` set explicitly (disables `ADYENAPISCLIENT_LOG`). Card fields are never persisted; only Adyen RESPONSES are stored in the support record. |
| 8 | Environment selection | SDK has one server group per operation (`Default` → `https://checkout-test.adyen.com/v71`; `Default1` management, unused) and only `ServerEnvironment.Production`, whose URL is the TEST host. `Adyen:Environment` must be `test`; any other value refuses to start (no live host in the SDK map — see §6). |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS. |
| 10 | Partial results | N/A — no paged reads in scope. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| Pay (`CreatePayment`) | `OrderPaymentAttempts` row keyed `(OrderId, AttemptNumber)` + `Orders.PaymentVersion` rotation, same `SaveChanges`, in `CatalogContext` | `Orders.PaymentVersion` concurrency token + primary key `(OrderId, AttemptNumber)` (relational: one transaction); in-memory provider: version compare-and-save under one gate (`OrderPaymentStore.SaveAsync`) | `OrderPaymentStore.UpdateAsync` `catch (… when IsLostRace)` → re-reads and re-decides → `PaymentClaimKind.InProgress` → HTTP 409; exhausted retries → `PaymentConcurrencyException` → 409 | claim: `Order.ClaimPayment` saved via `OrderPaymentStore.UpdateAsync` in `OrderPaymentService.PayAsync` → SDK call: `AdyenPaymentGateway.ChargeCardAsync` (`client.Payments.CreatePayment`) |
| Refund (`RefundPayment`) | `OrderRefunds` row keyed `Id` (deterministic from the operator's `idempotencyKey` when supplied, `OrderPaymentService.RefundIdFor`) + `Orders.PaymentVersion` rotation, same `SaveChanges` | `Orders.PaymentVersion` concurrency token + primary key `Id`; in-memory: compare-and-save gate | `OrderPaymentStore.UpdateAsync` `catch (… when IsLostRace)` → re-decides on fresh state: `RefundClaimKind.Existing`/`InProgress` (200/409) or `OrderPaymentException(ExceedsRefundable)` (422) | claim: `Order.ClaimRefund` saved via `OrderPaymentStore.UpdateAsync` in `OrderPaymentService.RefundAsync` → SDK call: `AdyenPaymentGateway.RefundAsync` (`client.Modifications.RefundPayment`) from `OrderPaymentService.SendRefundAsync` |

**PAGED READS** — none.

**UNKNOWN OUTCOMES**

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `CreatePayment` | `CreatePayment` re-sent with the stored `IdempotencyKey` (UNVERIFIED replay semantics) | attempt `IdempotencyKey` + merchant `Reference` | `AdyenPaymentGateway.ChargeCardAsync` catches `SdkTimeoutException` / `SdkConnectionException` / 5xx·408 / own deadline → `CardPaymentOutcome.Unknown`; `OrderPaymentService.PayAsync` re-sends in place with the same key; still unknown → `PaymentAttemptStatus.Unknown` (order `PaymentPending`), settled by `Order.ClaimPayment` → `PaymentClaimKind.Resend` on the next `PayAsync` (also after `StaleClaimAfter` for an abandoned `InFlight` claim) | `AdyenPaymentGatewayTests.ConnectionFailureIsUnknownAndThePaymentIsNeverResentByTheSdk`, `AdyenPaymentGatewayTests.TimeoutIsUnknown`, `OrderPaymentServiceTests.UnknownOutcomeIsSettledInPlaceWithTheSameIdempotencyKey`, `OrderPaymentServiceTests.StillUnknownOutcomeIsSettledByTheNextPayRequestWithoutANewCharge` |
| `RefundPayment` | `RefundPayment` re-sent with the stored `IdempotencyKey` | refund `IdempotencyKey` + refund `Reference` | `AdyenPaymentGateway.RefundAsync` catches the same set → `RefundOutcome.Unknown`; `OrderPaymentService.SendRefundAsync` re-sends in place with the same key; still unknown → `RefundStatus.Unknown` (keeps counting against the refundable balance), settled by `OrderPaymentService.SettleUnsettledRefundsAsync` at the start of the next `RefundAsync` for the order, or by `Order.ClaimRefund` → `RefundClaimKind.Resend` when the same operator key is re-posted | `AdyenPaymentGatewayTests.RefundConnectionFailureIsUnknown`, `AdyenPaymentGatewayTests.UnreadableSuccessfulRefundResponseIsUnknownNotFailed`, `OrderPaymentServiceTests.UnknownRefundIsSettledWithItsOwnKeyBeforeTheNextRefund` |

## 6. Assumptions & Blockers

- **No live endpoint in the SDK** — `ServerEnvironment` declares only `Production`, mapped to the test host. Live traffic is out of scope for this task (test account only); `Adyen:Environment=live` refuses to start rather than inventing a host. Not a blocker for this task.
- **Webhooks not covered by the plugin** — refund outcomes (and `Pending` payments) are finalised by Adyen webhooks (`RefundPayment` `<remarks>`); the plugin generates no webhook event models and its signature verifier is internal, so there is no grounded way to receive/verify them. Refunds are therefore recorded as `Received` (requested at Adyen, final outcome asynchronous). Reported as a gap; not a blocker for the requested flows.
- **3-D Secure / redirect actions** — the API takes only the encrypted card fields; an `action` result is recorded and the order stays unpaid with an actionable message.
- **Minor units** — ISO-4217 exponents (0/2/3) used to convert catalog decimals; a total not representable in the currency is rejected before calling Adyen.
- **Shipping address** — optional on `POST /api/orders` (request carries items + quantities); `Order.ShipToAddress` becomes optional.
- `YOUR CALL — not in the map`: persistence layout, status model, endpoint contracts — decided at implementation time.
