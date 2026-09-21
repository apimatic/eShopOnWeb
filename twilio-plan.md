# twilio-plan.md — SMS order notifications for eShopOnWeb (PublicApi)

Twilio .NET SDK (root namespace `Twilio`, client `TwilioClient`, options `TwilioClientOptions`, HTTP Basic
via `AccountSidAuthToken`). All Twilio facts below come from the SDK map / map-named source files.

## 1. Scope & sequence

Layering mirrors the app: domain entities + a provider **port** + services in `ApplicationCore`; the Twilio
**adapter**, config binding and DI in `Infrastructure`; HTTP endpoints in `PublicApi` (MinimalApi.Endpoint
`IEndpoint`, matching the existing catalog endpoints). Twilio SDK types never leave the Infrastructure adapter.

1. **Domain** — new aggregates `ContactNumber`, `Notification`; extend `Order` with an additive `OrderStatus`
   (`Placed`→`Dispatched`/`Cancelled`) + guard methods. New port `ISmsProvider` + domain DTOs (no SDK types).
2. **Infrastructure** — `TwilioSettings` bound from `Twilio:` section (fail-fast); `TwilioSmsProvider`
   adapter over the SDK; DI extension registering a singleton `TwilioClient` via `IHttpClientFactory`;
   `CatalogContext` DbSets + EF configs for the two new aggregates.
   - `ValidateAsync`     → `LookupsV2PhoneNumber.FetchPhoneNumber3` (canonical E.164 + validity)
   - `SendAsync`         → `Api20100401Message.CreateMessage` (immediate, `from` = FromNumber)
   - `ScheduleAsync`     → `Api20100401Message.CreateMessage` (`scheduleType=Fixed`, `sendAt`, `messagingServiceSid`)
   - `CancelScheduledAsync` → `Api20100401Message.UpdateMessage` (`status=Canceled`)
   - `FetchStatusAsync`  → `Api20100401Message.FetchMessage`
   - `DeleteContentAsync`→ `Api20100401Message.DeleteMessage`
   - `ListSentFromNumberAsync` → `Api20100401Message.ListMessage` (`from`=FromNumber, date window, paged)
3. **Services** (`ApplicationCore`) — `ContactNumberService` (register/list/delete), `OrderNotificationService`
   (place/dispatch/cancel messaging, resend, content disposal, reconciliation, status refresh).
4. **Endpoints** (`PublicApi`) — the 12 routes under `/api/…`. Shopper-scoped by `ClaimTypes.Name`; operator
   actions gated by `[Authorize(Roles = ADMINISTRATORS, AuthenticationSchemes = Bearer)]`.
5. **Secrets** — `Twilio:*` loaded into .NET user-secrets (UserSecretsId already on PublicApi.csproj); values
   never written to any repo file.

A message failure must never fail the order/dispatch/cancel; no-number-on-file = not messaged.

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, verbatim — every parameter name is the literal C# identifier; the
> cancellation-token parameter is named `ct`, so named args write `ct:`.
> ⚠ Every SDK type is written fully-qualified with the namespace its source path implies (taken from the path
> the map gives for THAT type). Nullable-no-default params MUST be passed explicitly (`null` to skip).

Client: `Twilio.TwilioClient` / `Twilio.TwilioClientOptions`; auth = `options.AccountSidAuthToken` (Basic,
AccountSid:AuthToken). Environments (`Twilio.Servers.ServerEnvironment.Production`, default): messaging
(`Api20100401Message`) resolves through the **Default** node `https://api.twilio.com`; **Lookups V2** resolves
through **Default4** `https://lookups.twilio.com`. `Twilio:BaseUrl` overrides the **messaging** node only.

| Op | Controller.Method | signature (verbatim, key params) | request fields used | response envelope → fields read | error | source |
| --- | --- | --- | --- | --- | --- | --- |
| Validate number | `client.LookupsV2PhoneNumber.FetchPhoneNumber3` | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, … 13 more nullable, RequestOptions? requestOptions=null, CancellationToken ct=default)` — pass `null` for all optional | `phoneNumber` (raw input), rest `null` | `LookupResponse` → `Valid` (bool?), `PhoneNumber` (E.164 canonical, string?), `ValidationErrors` | `SdkException<RawError>` **Case B** | `map/operations/LookupsV2PhoneNumber.md`; `Models/LookupResponse.cs` |
| Send / schedule | `client.Api20100401Message.CreateMessage` | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions=null, CancellationToken ct=default)` | `accountSid`, `to`, `body`; immediate: `from`=FromNumber; scheduled: `scheduleType=Fixed`, `sendAt`, `messagingServiceSid` (and `from`=null). All other nullable params passed `null`. | `ApiV2010AccountMessage` → `Sid`, `Status` (`MessageEnumStatus`), `To`, `From`, `ErrorCode` (int?), `ErrorMessage`, `DateSent` | `SdkException<RawError>` **Case B** | `map/operations/Api20100401Message.md`; `Models/ApiV2010AccountMessage.cs` |
| Cancel scheduled | `client.Api20100401Message.UpdateMessage` | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions=null, CancellationToken ct=default)` | `accountSid`, `sid`, `body`=null, `status`=`MessageEnumUpdateStatus.Canceled` | `ApiV2010AccountMessage` → `Sid`, `Status` | `SdkException<RawError>` **Case B** | same page; `Models/Enums/MessageEnumUpdateStatus.cs` |
| Fetch status | `client.Api20100401Message.FetchMessage` | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions=null, CancellationToken ct=default)` | `accountSid`, `sid` | `ApiV2010AccountMessage` → `Status`, `ErrorCode`, `ErrorMessage`, `To`, `From`, `DateSent` | `SdkException<RawError>` **Case B** | same page |
| Delete content | `client.Api20100401Message.DeleteMessage` | `DeleteMessage(string accountSid, string sid, RequestOptions? requestOptions=null, CancellationToken ct=default)` | `accountSid`, `sid` | `void` (Task) | `SdkException<RawError>` **Case B** | same page |
| Reconcile / list | `client.Api20100401Message.ListMessage` | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions=null, CancellationToken ct=default)` | `accountSid`, `from`=FromNumber, `dateSentQueryQuery`= **from** (wire `DateSent>`), `dateSentQuery`= **to** (wire `DateSent<`), `pageSize`, page loop; others null | `ListMessageResponse` → `Messages` (`IReadOnlyList<ApiV2010AccountMessage>`), `NextPageUri` | `SdkException<RawError>` **Case B** | same page; `Models/ListMessageResponse.cs` |

Enums needed (open-string `StringEnum<T>`; source `Models/Enums/…`):
- `Twilio.Models.Enums.MessageEnumScheduleType.Fixed` (`"fixed"`) — `MessageEnumScheduleType.cs`
- `Twilio.Models.Enums.MessageEnumUpdateStatus.Canceled` (`"canceled"`) — `MessageEnumUpdateStatus.cs`
- `Twilio.Models.Enums.MessageEnumStatus`: `Queued/Sending/Sent/Delivered/Undelivered/Failed/Scheduled/Canceled/Accepted/…` — read `.Value` (string) for storage — `MessageEnumStatus.cs`

(Namespace note — **VERIFIED against source, corrects the map's stale identity table**: the pinned `main`
source declares root namespace **`TwilioSdk`**, not `Twilio`; client `TwilioSdkClient`, options
`TwilioSdkClientOptions`, DI extension `AddTwilioSdkClient`. So: client/options→`TwilioSdk`;
`BasicAuthCredentials`→`TwilioSdk.Core.Authentication.Basic`; `ServerEnvironment`+server option nodes
(`ServerOptions.Default.Production.BaseUrl`)→`TwilioSdk.Servers`; models→`TwilioSdk.Models`; enums→
`TwilioSdk.Models.Enums`; `RetryOptions`/`LoggingOptions`→`TwilioSdk.Core.Configuration`; `RawError`→
`TwilioSdk.Core.ErrorResponse`; `SdkException<>`→`TwilioSdk.Core.Exceptions`; `RequestOptions`→`TwilioSdk.Core`.
The map's identity row saying root `Twilio` is branch drift — the compiler and the `namespace` lines are ground
truth, as getting-started directs. Messaging node override confirmed as `options.Server.Default.Production.BaseUrl`
(default `https://api.twilio.com`); Lookups V2 resolves through `Default4` and is untouched by it.)

## 3. Trap notes (hazard + skill pointer — not resolved here)

- **Base-URL override for messaging only.** `Twilio:BaseUrl` must replace the messaging node base address on
  *every* messaging call but must NOT touch the Lookups (Default4) call — how per-node/per-call base URL
  selection actually resolves against server groups is the hazard. **MUST load `dotnet-configuration-resilience`.**
- **`Timeout` is per-attempt, retries multiply it.** The caller-visible budget on a message send is not the knob
  value. **MUST load `dotnet-configuration-resilience`.**
- **Write-retry eligibility.** Whether `CreateMessage`/`UpdateMessage`/`DeleteMessage` (POST/DELETE) are ever
  resent by the SDK — and the duplicate-send risk that implies for reconciliation. **MUST load `dotnet-configuration-resilience`.**
- **Pagination has no auto-iterator by default.** Covering the whole reconciliation range means driving the
  page loop myself; the mechanism (page/pageToken/NextPageUri) is the hazard. **MUST load `dotnet-configuration-resilience`.**
- **Client/HttpClient lifetime.** Singleton client over a long-lived `HttpClient`/`IHttpClientFactory`, not
  rebuilt per request. **MUST load `dotnet-client-initialization`.**
- **Credential wiring & when options are captured.** Where/how `AccountSidAuthToken` is set and that a rotated
  secret needs a restart. **MUST load `dotnet-authentication`.**
- **Explicit-null optional args mis-bind if passed positionally wrong / enum & union shapes.** `MessageEnum*`
  are `StringEnum<T>` not C# enums; reading `.Value`. **MUST load `dotnet-models`** and **`dotnet-calling-endpoints`.**
- **Error boundary: two JsonException directions.** (see REQUIRED READING). **MUST load `dotnet-error-handling`.**

## 4. REQUIRED READING (load ALL before implementing; contents deliberately not restated here)

- `twilio-platforms-team:dotnet-client-initialization` — step: singleton client + HttpClient lifetime + DI.
- `twilio-platforms-team:dotnet-authentication` — step: setting `AccountSidAuthToken` from config/user-secrets.
- `twilio-platforms-team:dotnet-calling-endpoints` — step: every `client.*` call; named-arg / explicit-null discipline.
- `twilio-platforms-team:dotnet-models` — step: building requests, `StringEnum<T>` enums, reading response fields.
- `twilio-platforms-team:dotnet-error-handling` — step: the adapter's try/catch around every SDK call.
- `twilio-platforms-team:dotnet-configuration-resilience` — step: base-URL selection, timeout budget, retry
  eligibility, pagination.

Two mandatory hazard rows (verbatim), because `System.Text.Json.JsonException` reaches the boundary from two
directions needing opposite handling:
- A drifted/malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from
  deserialization, **not** as an `SdkException` — an SDK-exception-only catch ladder lets it escape.
- A **non-2xx** body that doesn't match its operation's generated error shape throws `JsonException` *while the
  error object is being constructed*, **replacing** the `SdkException` and destroying the HTTP status with it.
- (All message ops here are **Case B** `SdkException<RawError>`; `RawError` carries `StatusCode`.)

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | **Credential fail-fast** | `TwilioSettings` bound from `Twilio:` in the Infrastructure DI extension; host refuses to start (throws) if `AccountSid`, `AuthToken`, `FromNumber`, or `MessagingServiceSid` is null **or blank** (each part checked separately — a blank part ≠ a missing one). `BaseUrl` optional. Validated at registration + `ValidateOnStart`. |
| 2 | **Secret sourcing & rotation** | Values come from .NET user-secrets (loaded from the `TWILIO_*` env vars by me; never written into repo files). Options object built once at registration and captured in the singleton `TwilioClient`, so a rotated `AuthToken` takes effect only on process restart — acceptable here; documented, not hot-reloaded. |
| 3 | **Total timeout budget** | `CancellationToken` from the ASP.NET request is threaded into every SDK call as `ct:`. `Timeout` is per-attempt; I set a bounded per-attempt timeout and cap retries so the worst-case whole-call time is finite; the request `ct` is the real deadline. Confirm knobs against `dotnet-configuration-resilience`. |
| 4 | **Write-retry ownership** | Default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS, so `CreateMessage`/`UpdateMessage` (POST) and `DeleteMessage` (DELETE) are **never** auto-resent by the SDK → no silent duplicate sends. `FetchMessage`/`ListMessage` (GET) may retry — idempotent, safe. |
| 5 | **Idempotency & ambiguous writes** | `CreateMessage` exposes **no** caller idempotency key (the injected `Idempotency-Key: Guid.NewGuid()` header is not one). App-level idempotency for **resend** is enforced in my store: the caller-supplied key is persisted on the produced `Notification`; a repeat under the same key returns the existing notification and sends nothing. For place/dispatch/cancel sends, no key exists → reconciliation endpoint is the recovery path (list provider messages for FromNumber over a window, diff against the store). |
| 6 | **Observability** | Structured logs at Info for lifecycle (order placed/dispatched/cancelled, message queued, follow-up scheduled/cancelled, resend, content disposed) keyed by orderId/notificationId/**message Sid** and the provider `RawError.StatusCode` on failure. `LogRequestBody` stays **off**. The shopper's phone number is **never** logged (masked / omitted). |
| 7 | **Sensitive data** | Request models carry the destination phone number and message body (personal data). Therefore `LogRequestBody` stays off **and** `options.Logging.LoggerFactory` is set explicitly so `TWILIOCLIENT_LOG` cannot switch body logging on from outside code. My own diagnostics never echo the number or body. |
| 8 | **Environment selection** | Production `ServerEnvironment` only (live account — no SDK sandbox environment). Messaging node = `Twilio:BaseUrl` when set, else `https://api.twilio.com`; Lookups always its own node. Test traffic is kept off the live system by policy, not by env: only the two supplied destinations (`TWILIO_TEST_TO_NUMBER`, `TWILIO_UNREACHABLE_TO_NUMBER`) are ever registered/messaged. |

## 6. Assumptions & Blockers

- **A (YOUR CALL):** `POST /api/orders` reuses the existing `Order` aggregate, which requires a `ShipToAddress`.
  The request may carry an address; when omitted I supply a placeholder shipping address (focus is notifications,
  not fulfilment). Not an SDK fact.
- **A (YOUR CALL):** Order status (`Placed/Dispatched/Cancelled`) is added to the `Order` aggregate to gate
  dispatch/cancel transitions and prevent a cancelled order being dispatched.
- **A (UNVERIFIED, live-traffic):** Twilio scheduled sends require a Messaging Service; the SDK doc-comments are
  empty on this. Defensive directive: schedule via `messagingServiceSid` (`from`=null) + `scheduleType=Fixed` +
  `sendAt` 3 days out (within Twilio's 15 min–7 day window). If the provider rejects a schedule, it is caught and
  recorded like any send failure and never fails the dispatch. Reconciliation filters by `from`=FromNumber, which
  assumes the Messaging Service sends from that number (single-number sandbox pool) — noted limitation.
- **A (UNVERIFIED, live-traffic):** US destinations are expected to be *undelivered* for this account
  (`TWILIO_UNREACHABLE_TO_NUMBER`). Handled as a delivery **outcome** (status `undelivered`/`failed` + error
  code), not a gap. `TWILIO_TEST_TO_NUMBER` (Canadian) is expected to deliver.
- **No Blockers.** Every required capability is covered by a mapped operation.

### Post-implementation verification (run against the live account)

- Number validation via Lookups V2: an invalid number is rejected at registration (400); a valid one is stored
  in canonical E.164.
- Deliverable path (Canadian `TWILIO_TEST_TO_NUMBER`): OrderPlaced/OrderDispatched **delivered**.
- Scheduling assumption **CONFIRMED**: the follow-up is accepted with `scheduleType=Fixed`+`sendAt`+
  `messagingServiceSid` (status `scheduled`), and `UpdateMessage status=Canceled` on cancel turns it to
  `canceled` before it ever sends.
- Undeliverable US assumption **CONFIRMED as an outcome, not a gap**: `TWILIO_UNREACHABLE_TO_NUMBER` yields
  status `undelivered`, error code `30034` — the order still succeeds.
- Resend idempotency, content disposal (message gone from provider, fact/outcome retained), and reconciliation
  over a range (filtered to `Twilio:FromNumber`, surfacing provider-only and eShop-only discrepancies) all
  verified through PublicApi alone.
