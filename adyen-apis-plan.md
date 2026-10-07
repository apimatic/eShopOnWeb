# Adyen APIs integration plan — eShopOnWeb card payments

SDK source (read-only, referenced by path, never copied): `sdk/dotnet/` inside the **adyen** plugin
(`<plugin-root>/sdk/dotnet/AdyenApIs.csproj`). Map: `sdk/dotnet/sdk-map.md`, `sdk/dotnet/map/operations/*.md`.
All "source" cells below are relative to that SDK root.

## 1. Scope & sequence

| # | Step | Adyen operations |
| --- | --- | --- |
| 1 | `global.json` roll-forward so the .NET 10 SDK builds the solution; reference the SDK project from a new `src/Payments.Adyen` project (keeps the SDK and its Microsoft.Extensions 10.x dependencies out of `Web`/`Infrastructure`) | — |
| 2 | Domain: payment state on the existing `Order` aggregate (`PaymentStatus`, payment attempts, refunds, raw Adyen responses) + EF config in `CatalogContext` | — |
| 3 | `AdyenSettings` bound from `Adyen:` (`ApiKey`, `MerchantAccount`, `Environment`, `Currency`), validated on start; client registered over a named `HttpClient` | — (client construction) |
| 4 | `AdyenPaymentGateway` (`src/Payments.Adyen`) — the single SDK boundary: total-budget token, raw-body capture hook, error ladder → own result/exception types | `Payments.CreatePayment`, `Modifications.RefundPayment` |
| 5 | `OrderPaymentService` (ApplicationCore) — claim → SDK call → record, amount in minor units from catalog prices | via gateway interface |
| 6 | PublicApi endpoints: `POST /api/orders`, `POST /api/orders/{orderId}/pay`, `POST /api/orders/{orderId}/refunds` (admin), `GET /api/my-orders`, `GET /api/orders/{orderId}/adyen-record` (admin) | — |
| 7 | Offline tests (stub `HttpMessageHandler` under the real SDK client + service tests + PublicApi integration tests with a fake gateway) | — |
| 8 | Live self-verification against Adyen test: payment with test Visa, partial refund, support record | `CreatePayment`, `RefundPayment` |

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, verbatim. Each operation that takes input takes **ONE request record** as its
> first parameter, built with an object initializer using the record's own property names — never flat arguments.
>
> ⚠ Every SDK type is written fully-qualified with the namespace its source path implies (map *Namespaces by content
> type*): `Models/` → `AdyenApIs.Models`, `Models/Enums/` → `AdyenApIs.Models.Enums`, `Models/AnyOf/` →
> `AdyenApIs.Models.AnyOf`, `Errors/` → `AdyenApIs.Errors`, `Requests/Payments/` → `AdyenApIs.Requests.Payments`,
> `Requests/Modifications/` → `AdyenApIs.Requests.Modifications`, `Core/Exceptions/` → `AdyenApIs.Core.Exceptions`,
> `Core/ErrorResponse/` → `AdyenApIs.Core.ErrorResponse`, `Core/Hooks/` → `AdyenApIs.Core.Hooks`,
> `Core/RequestOptions.cs` → `AdyenApIs.Core`, `Core/Configuration/` → `AdyenApIs.Core.Configuration`.

| Controller | Signature | Request record (members) | Body model (C# (wire): type, required?) | Response + fields read | Error case | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `client.Payments` | `Task<AdyenApIs.Models.PaymentResponse> CreatePayment(AdyenApIs.Requests.Payments.CreatePaymentRequest request, AdyenApIs.Core.RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `CreatePaymentRequest`: `IdempotencyKey: string?` (sent as `Idempotency-Key` header from the record — a REAL caller key; doc: "unique identifier for the message, max 64 chars, UUID recommended"), `Body: AdyenApIs.Models.PaymentRequest?` | `PaymentRequest`: `Amount (amount): AdyenApIs.Models.Amount2, required` → `Currency (currency): string, required`, `Value (value): long, required` (minor units); `MerchantAccount (merchantAccount): string, required`; `PaymentMethod (paymentMethod): AdyenApIs.Models.AnyOf.PaymentMethod111, required` — built with factory `PaymentMethod111.Card(AdyenApIs.Models.Card)`; `Reference (reference): string, required` (max 80); `ReturnUrl (returnUrl): string, required`; optional used: `CaptureDelayHours (captureDelayHours): int?` (doc: "delay between the authorisation and scheduled auto-capture, in hours" → 0 = take money now), `ShopperInteraction (shopperInteraction): AdyenApIs.Models.Enums.ShopperInteraction?`, `ShopperReference (shopperReference): string?`, `MerchantOrderReference (merchantOrderReference): string?`. Doc-named fields left out: browserInfo/origin/channel (3DS2 native flow — not supported, no front end), shopperEmail/IP/billingAddress (not held by this API). | `Card`: `EncryptedCardNumber (encryptedCardNumber): string?`, `EncryptedExpiryMonth (encryptedExpiryMonth): string?`, `EncryptedExpiryYear (encryptedExpiryYear): string?`, `EncryptedSecurityCode (encryptedSecurityCode): string?`, `HolderName (holderName): string?`, `Type (type): AdyenApIs.Models.Enums.Type12?` = `Type12.Scheme` default | `PaymentResponse` (no envelope): `ResultCode (resultCode): AdyenApIs.Models.Enums.ResultCode1?`, `PspReference (pspReference): string?`, `RefusalReason (refusalReason): string?`, `RefusalReasonCode (refusalReasonCode): string?`, `Amount (amount): AdyenApIs.Models.Amount25?`, `MerchantReference (merchantReference): string?`, `Action (action)` (non-null ⇒ shopper action needed). **No `AdditionalProperties` bag on this model** → unknown fields are dropped by the SDK; raw body captured via hook (see trap T6). | **Case A** `ApiException<AdyenApIs.Errors.CreatePaymentError>`: `TryGetServiceError(out AdyenApIs.Models.ServiceError)` [400,401,403,422,500] · `TryGetRawError(out AdyenApIs.Core.ErrorResponse.RawError)` [fallback]. `ServiceError`: `ErrorCode (errorCode): string?`, `ErrorType (errorType): string?`, `Message (message): string?`, `PspReference (pspReference): string?`, `Status (status): int?` | none | `map/operations/Payments.md` §CreatePayment; `Requests/Payments/CreatePaymentRequest.cs`; `Models/PaymentRequest.cs`; `Models/Amount2.cs`; `Models/AnyOf/PaymentMethod111.cs`; `Models/Card.cs`; `Models/PaymentResponse.cs`; `Errors/CreatePaymentError.cs`; `Models/ServiceError.cs`; route `POST /payments` in `Api/Payments.cs` |
| `client.Modifications` | `Task<AdyenApIs.Models.PaymentRefundResponse> RefundPayment(AdyenApIs.Requests.Modifications.RefundPaymentRequest request, AdyenApIs.Core.RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `RefundPaymentRequest`: `PaymentPspReference: string, required` (the payment's `pspReference`), `IdempotencyKey: string?` (header `Idempotency-Key` from the record — REAL key), `Body: AdyenApIs.Models.PaymentRefundRequest?` | `PaymentRefundRequest`: `Amount (amount): AdyenApIs.Models.Amount37, required` → `Currency: string, required`, `Value: long, required`; `MerchantAccount (merchantAccount): string, required`; optional used: `Reference (reference): string?` (max 80), `MerchantRefundReason (merchantRefundReason): AdyenApIs.Models.Enums.MerchantRefundReason?`. Left out: lineItems (only required for BNPL methods, not cards), splits, store, capturePspReference (PayPal only). | `PaymentRefundResponse`: `PspReference (pspReference): string, required`, `PaymentPspReference: string, required`, `Amount: Amount38, required`, `Reference: string?`, `Status (status): string` fixed `"received"`. Outcome of the refund is asynchronous (REFUND webhook per `<remarks>`) | **Case A** `ApiException<AdyenApIs.Errors.RefundPaymentError>`: `TryGetServiceError(out ServiceError)` [400,401,403,422,500] · `TryGetRawError(out RawError)` [fallback] | none | `map/operations/Modifications.md` §RefundPayment; `Requests/Modifications/RefundPaymentRequest.cs`; `Models/PaymentRefundRequest.cs`; `Models/PaymentRefundResponse.cs`; `Errors/RefundPaymentError.cs`; route `POST /payments/{paymentPspReference}/refunds` in `Api/Modifications.cs` |

**Enums needed**

| Enum (namespace `AdyenApIs.Models.Enums`) | Members used | Source |
| --- | --- | --- |
| `ResultCode1` | `Authorised`, `Refused`, `Error`, `Cancelled`, `Pending`, `Received`, `RedirectShopper`, `IdentifyShopper`, `ChallengeShopper`, `PresentToShopper`, `PartiallyAuthorised`; (also declared: `AuthenticationFinished`, `AuthenticationNotRequired`, `Success`) | `Models/Enums/ResultCode1.cs` |
| `ShopperInteraction` | `Ecommerce` | `Models/Enums/ShopperInteraction.cs` |
| `Type12` | `Scheme` (default on `Card.Type`) | `Models/Enums/Type12.cs` |
| `MerchantRefundReason` | `Fraud` "FRAUD", `CustomerRequest` "CUSTOMER REQUEST", `Return` "RETURN", `Duplicate` "DUPLICATE", `Other` "OTHER" | `Models/Enums/MerchantRefundReason.cs` |

**Client construction / auth / servers**

| Fact | Value | Source |
| --- | --- | --- |
| Constructor | `new AdyenApIs.AdyenApIsClient(HttpClient httpClient, AdyenApIs.AdyenApIsClientOptions options)` (only ctor) | `sdk-map.md` *Getting a client*; `AdyenApIsClient.cs` |
| Options used | `ApiKeyAuth: string?` (header `X-API-Key`), `Environment: AdyenApIs.Servers.ServerEnvironment`, `Retry: AdyenApIs.Core.Configuration.RetryOptions`, `Logging: AdyenApIs.Core.Configuration.LoggingOptions`, `Hooks: IReadOnlyList<AdyenApIs.Core.Hooks.SdkHook>` | `AdyenApIsClientOptions.cs` |
| Auth on both ops | `options.BasicAuth` OR `options.ApiKeyAuth` — this build sets **only** `ApiKeyAuth` | `map/operations/Payments.md`, `Modifications.md` |
| Environments | `ServerEnvironment.Production` is the only member (default); group `Default` → `https://checkout-test.adyen.com/v71` (both ops on `Default`); `Default1` (management) not touched. **No live checkout URL declared.** | `sdk-map.md` *Servers & auth*; `Servers/ServerEnvironment.cs` |
| Per-call hooks | `AdyenApIs.Core.RequestOptions { Hooks = [...] }`; `SdkHook.OnResponse(Func<HttpResponseMessage, HookContext, CancellationToken, ValueTask>)` runs once per attempt inside the pipeline | `Core/RequestOptions.cs`; `Core/Hooks/SdkHook.cs` |
| Exceptions | `SdkException` ⊃ `ApiException` ⊃ `ApiException<TError>` / `ResponseDeserializationException`; `SdkConnectionException` ⊃ `SdkTimeoutException`; `AuthSchemeException` — all `AdyenApIs.Core.Exceptions` | `sdk-map.md` *Error-handling model*; `Core/Exceptions/*.cs` |

**CROSS-OPERATION INVARIANTS**

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| `RefundPayment.PaymentPspReference` must be the `pspReference` of an **Authorised** `CreatePayment` that this application stored for **this order** — never caller-supplied | `RefundPayment` ← `CreatePayment` (stored attempt) | `OrderPaymentService.RefundAsync` reads the order's authorised attempt before calling the gateway; the refund request DTO carries no psp reference |
| Refund `Amount.Currency` must equal the currency of the authorised payment | `RefundPayment` ← `CreatePayment` | `OrderPaymentService.RefundAsync` uses the currency stored on the authorised attempt, not configuration |
| Σ refund amounts (Received + InFlight + Unknown) ≤ amount captured by the authorised payment | `RefundPayment` ← `CreatePayment` | `Order.StartRefund` (domain guard) before the claim is saved |
| Order ids / catalog item ids supplied by callers must be ones this application holds (order owned by caller; catalog item exists) | `CreatePayment` ← `POST /api/orders` | `OrderPaymentService` (buyer-scoped order lookup), `CreateOrder` endpoint (catalog lookup by ids) |

## 3. Trap notes

| Step | Hazard → consequence | Skill |
| --- | --- | --- |
| 3 | Registering via the SDK's DI extension shares the default unnamed `HttpClient` and captures options once; handler lifetime/DNS and timeouts leak to/from other consumers. | MUST load `adyen:dotnet-client-initialization` |
| 3 | A blank/missing credential is not an error inside the SDK — the call goes out unauthenticated and only fails at the provider. | MUST load `adyen:dotnet-authentication` |
| 3/4 | What the knobs named "Timeout" actually bound vs. the 30-second total the task demands; which verbs the SDK resends. | MUST load `adyen:dotnet-configuration-resilience` |
| 3 | The SDK's built-in logger and its env-var switch can write request bodies (encrypted card blobs, holder name) to logs. | MUST load `adyen:dotnet-configuration-resilience` |
| 4 | Building `PaymentMethod111` (an AnyOf union) and reading open-string enums like `ResultCode1` — wrong construction or `ToString()` on an enum corrupts wire values/stored status. | MUST load `adyen:dotnet-models` |
| 4 | Request record vs. body model vs. positional `requestOptions` — wrong shape fails to compile or drops the idempotency key. | MUST load `adyen:dotnet-calling-endpoints` |
| 4 | Catch ladder: typed accessor order, the non-generic failure kinds, and caller-safe messages. | MUST load `adyen:dotnet-error-handling` |
| 4 | **T6** — `PaymentResponse` declares no extension-data bag, so "everything Adyen returned" is not recoverable from the deserialized model; raw-body capture must happen on the transport seam without breaking the SDK's own read. | MUST load `adyen:dotnet-configuration-resilience` (Hooks) |
| 4/5 | A transport failure after sending a payment/refund leaves the outcome unknown; reporting it as failed risks a double charge on retry. | MUST load `adyen:dotnet-configuration-resilience` |
| 7 | Which seam to fake, where request bodies can still be read, and how to produce `ApiException<T>` in tests. | MUST load `adyen:dotnet-testing` |

## 4. REQUIRED READING

Load **before implementation starts** (this sheet deliberately does not carry their contents):

- `adyen:dotnet-client-initialization` — step 3 (named HttpClient, singleton client)
- `adyen:dotnet-authentication` — step 3 (credential fail-fast)
- `adyen:dotnet-configuration-resilience` — steps 3–5 (timeouts, retries, logging, hooks, unknown outcomes)
- `adyen:dotnet-calling-endpoints` — step 4
- `adyen:dotnet-models` — step 4
- `adyen:dotnet-error-handling` — step 4 (error boundary)
- `adyen:dotnet-testing` — step 7

Hazard row (verbatim requirement): a body that does not match its declared type — a drifted or malformed **2xx**
response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated
`{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP
status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only
`ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `AdyenSettings` bound from `Adyen:`; `ValidateOnStart` + custom validation rejects missing/blank `ApiKey`, `MerchantAccount`, `Environment`, `Currency` (3-letter ISO code) — host refuses to start, message names the key, never the value. Only `ApiKeyAuth` is used, so Basic auth parts are not demanded. |
| 2 | Secret sourcing & rotation | Development: .NET user-secrets (PublicApi `UserSecretsId`) loaded from `ADYEN_*` env vars; other environments: any `IConfiguration` source (env vars `Adyen__ApiKey` etc., Key Vault). Options are read once when the singleton client is built → a rotated key takes effect on process restart (accepted; rotation = rolling restart). |
| 3 | Total timeout budget | Caller waits ≤ 30 s: gateway wraps every SDK call in one `CancellationTokenSource` of **25 s** total (one home: `AdyenPaymentGateway.Bounded`); per-attempt `HttpClient.Timeout` = `Retry.Timeout` = **10 s**; SDK retries disabled. Budget expiry / SDK timeout → `502/504` "Adyen did not respond". |
| 4 | Write-retry ownership | Both ops are `POST`; client built with `RetryOptions.Disabled()` so the SDK never resends. The only resend is our own explicit settle-resend with the **same** `IdempotencyKey` (see 11). |
| 5 | Idempotency & ambiguous writes | `CreatePayment`: `IdempotencyKey` = a GUID generated once per payment attempt and stored on the attempt row; reused verbatim on settle-resends. `RefundPayment`: `IdempotencyKey` = GUID stored on the refund row; reused on settle-resends. Generator-injected header is not relied on (these records feed the header themselves). |
| 6 | Observability | SDK logger gets the host `ILoggerFactory` explicitly (request line Information, failures Warning/Error); headers/bodies off. Own logs: orderId, attempt/refund reference, pspReference, resultCode, Adyen `errorCode`/`pspReference` from `ServiceError` on failures. Never the request body or API key. |
| 7 | Sensitive data | Request carries encrypted card fields + holder name ⇒ `LogRequestBody = false`, `LogRequestHeaders = false`, `LoggerFactory` assigned explicitly (disables `ADYENAPISCLIENT_LOG`). Encrypted card data is never persisted; only Adyen's responses (which Adyen returns for support) are stored. |
| 8 | Environment selection | SDK declares one environment (`Production`) whose `Default` group is `checkout-test.adyen.com/v71`. `Adyen:Environment` must be `test` → SDK default URL. Any other value refuses to start (a live checkout URL is not in the map — see §6). Test traffic therefore cannot reach live. |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS. |
| 10 | Partial results | N/A — no paged reads in scope. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| `CreatePayment` (pay order) | `PaymentAttempts` table in `CatalogContext` (owned by `Order`), row inserted with status `InFlight` before the SDK call | Composite primary key `(OrderId, AttemptNumber)`: concurrent callers compute the same next attempt number; the second insert is refused by the store (SQL Server 2627/2601; EF in-memory `ArgumentException` — verified by `OrderPaymentStoreClaimTests`) | `OrderPaymentStore.TrySaveClaimAsync` `catch … when IsDuplicateKey` → returns false → `OrderPaymentService.PayAsync` answers `409 InProgress` (no SDK call) | claim: `Order.StartPaymentAttempt` + `OrderPaymentStore.TrySaveClaimAsync` (in `OrderPaymentService.PayAsync`) → SDK: `AdyenPaymentGateway.AuthoriseAsync` (`client.Payments.CreatePayment`) |
| `RefundPayment` (refund order) | `OrderRefunds` table in `CatalogContext` (owned by `Order`), row inserted `InFlight` with amount reserved before the SDK call | Composite primary key `(OrderId, Sequence)` — concurrent refunds on one order collide; serialises refunds so the over-refund guard is never raced | `OrderPaymentStore.TrySaveClaimAsync` `catch … when IsDuplicateKey` → false → `OrderPaymentService.RefundAsync` answers `409 InProgress` | claim: `Order.StartRefund` + `OrderPaymentStore.TrySaveClaimAsync` (in `OrderPaymentService.RefundAsync`) → SDK: `OrderPaymentService.SettleRefundAsync` → `AdyenPaymentGateway.RefundAsync` (`client.Modifications.RefundPayment`) |

**PAGED READS** — none.

**UNKNOWN OUTCOMES**

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `CreatePayment` | `CreatePayment` re-sent with the attempt's stored `IdempotencyKey` (no payment lookup operation exists in the map's Payments/Modifications pages). In the failing call's own catch while budget remains; otherwise attempt → `Unknown`, settled by the next `pay` call for that order (re-sends with the same key before anything else). `UNVERIFIED`: that Adyen replays the original result for a repeated key. | attempt `IdempotencyKey` + `Reference` | `AdyenPaymentGateway.AuthoriseAsync` `catch … when IsNoResponse` (SdkTimeout/SdkConnection/budget cancellation → `Unknown`, `NoResponse`) → `OrderPaymentService.AuthoriseSettlingUnknownAsync` re-sends with the same key; still unknown → `PaymentAttemptStatus.Unknown`, settled in `OrderPaymentService.PayAsync` (`settles = latest` → `Order.StartPaymentAttempt(…, settles)`) | `OrderPaymentServiceTests.UnknownPaymentOutcomeIsRecordedAndSettledWithTheSameIdempotencyKey`, `…DroppedConnectionIsSettledWithinTheSameRequest`, `AdyenPaymentGatewayTests.ConnectionFailureIsAnUnknownOutcomeAndIsNotResent` |
| `RefundPayment` | `RefundPayment` re-sent with the refund's stored `IdempotencyKey` and identical body. In the catch while budget remains; else refund → `Unknown` (amount stays reserved), settled at the start of the next refund call on that order. | refund `IdempotencyKey` + `Reference` | `AdyenPaymentGateway.RefundAsync` `catch … when IsNoResponse` → `OrderPaymentService.SettleRefundAsync` re-sends with the same key; still unknown → `RefundStatus.Unknown`, settled by the `NeedsSettlement` loop at the start of `OrderPaymentService.RefundAsync` | `OrderPaymentServiceTests.UnknownRefundOutcomeKeepsItsAmountReservedAndIsSettledByTheNextRefund` |

## 6. Assumptions & Blockers

- **Assumption — 3DS / redirect not supported.** No storefront exists; a `resultCode` requiring shopper action (`RedirectShopper`, `IdentifyShopper`, `ChallengeShopper`, `PresentToShopper`) is recorded and reported as "card requires additional authentication, not supported"; the order stays unpaid. The test Visa card is expected to authorise directly. `UNVERIFIED` until the live run.
- **Assumption — `returnUrl` is required by the model** but unused for direct card flow; a fixed URL derived from the request host is sent (`YOUR CALL — not in the map`).
- **Assumption — capture.** `CaptureDelayHours = 0` requests immediate auto-capture ("take the money now"). Refund remarks require a captured payment; capture is asynchronous at Adyen, so an immediate refund might be rejected — `UNVERIFIED`, verified live.
- **Assumption — refund outcome.** `RefundPayment` returns `received`; the final result arrives via webhook, which is out of scope (no inbound webhook infra required). Refund state is `Received` (pending final confirmation) and amounts are reserved against the paid total.
- **Gap (non-blocking for this task) — live environment.** The SDK declares only `checkout-test` base URLs. Running against a *live* Adyen account is not covered by the map; the build refuses to start when `Adyen:Environment` ≠ `test` rather than inventing a URL.
- **Minor units.** Currency exponent is ISO-4217 (not an Adyen API fact): 0-decimal and 3-decimal currencies tabled; others 2. An order total not representable exactly in minor units is rejected, never rounded.

## 7. Verification log (2026-10-07, Adyen test, merchant from `Adyen:MerchantAccount`)

- Live: order 47.50 USD → `CreatePayment` with the test Visa → `Authorised`, `amount.value` 4750 (exact to the cent); repeat pay → `AlreadyPaid`, no second Adyen call.
- Live: partial refund 10.00 → `RefundPayment` HTTP 201 `status: received` immediately after the payment (settles the capture/refund `UNVERIFIED` assumption for this account); over-refund → 422 without calling Adyen; same idempotency key → `Replayed`.
- Live: invalid card number → HTTP 422 `ServiceError` (`errorCode` 101) → order stays unpaid, shopper told why; raw body kept in the support record.
- Live: test Visa authorised directly — no 3DS action was requested (the `ActionRequired` assumption was not exercised).
- Server log checked: no card fields and no API key logged; SDK request lines show `checkout-test.adyen.com/v71` only.
- Still `UNVERIFIED`: Adyen replaying the original result for a re-sent idempotency key (covered offline by tests against the contract, not observable without a real network failure).
