# Twilio SMS order-notifications — integration plan (eShopOnWeb / PublicApi)

Twilio **.NET SDK** (root namespace `TwilioSdk`, client `TwilioSdkClient`), API spec `1.0.0`, throw-based,
**every operation in scope is Case B (`SdkException<RawError>`)**. All facts below are grounded in the SDK
map + the source files it names (this session's clone); nothing from memory.

## 1. Scope & sequence

| # | Step | Twilio operations used |
| --- | --- | --- |
| 1 | Vendor the SDK source as `src/TwilioSdk` (unpublished to NuGet); `ProjectReference` from `Infrastructure`. | — |
| 2 | `TwilioSettings` bound from config section `Twilio:`; **fail-fast** at startup (§5.1). | — |
| 3 | Register `TwilioSdkClient` (singleton) — Basic auth, optional messaging base-URL override, bounded retry (§5.2–5.4, §5.8). | — |
| 4 | Domain: `ContactNumber` + `OrderNotification` (+ enums) aggregates in ApplicationCore; additive `OrderStatus` on `Order`. | — |
| 5 | EF: add DbSets + configs to `CatalogContext`; **unique index** on `OrderNotification.IdempotencyKey`; migration. | — |
| 6 | `ITwilioMessagingGateway`→`TwilioMessagingGateway` (Infrastructure) — the ONLY SDK call site; translates errors to `TwilioProviderException`. | Lookup, CreateMessage, UpdateMessage, FetchMessage, ListMessage |
| 7 | Orchestration: `IPhoneNumberValidator`, `IOrderNotifier` (ApplicationCore ifaces, Infrastructure impls). | via gateway |
| 8 | PublicApi: 10 endpoints (MinimalApi.Endpoint `IEndpoint<>` pattern) + DI extension. | via orchestration |

Capability the map lacks → Blocker (§6). None found: register/validate, send, schedule, cancel-scheduled,
redact, fetch-status and range-list are all present.

## 2. CONTRACT SHEET

⚠ Signatures are **generated code, verbatim** — every parameter name is the literal C# identifier; named
arguments must use them exactly (the cancellation-token parameter is `ct`, so `ct:`). Every operation in
scope has **24/8/15 leading nullable params with no C# default → each must be passed explicitly** (pass
`null` to skip); use named arguments.
⚠ Every SDK type is written fully-qualified with the namespace its source path implies (records →
`TwilioSdk.Models`, enums → `TwilioSdk.Models.Enums`, client/options → `TwilioSdk`, servers →
`TwilioSdk.Servers`, basic auth → `TwilioSdk.Core.Authentication.Basic`, errors → `TwilioSdk.Core.ErrorResponse`,
exception → `TwilioSdk.Core.Exceptions`).

| Operation | Controller · signature | Request fields (wire ← C#) | Response envelope → fields read | Error | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| **Validate/canonicalize number** | `client.LookupsV2PhoneNumber.FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions=null, CancellationToken ct=default)` | `phoneNumber` = number as typed; **all 15 optional params → `null`** (purpose: none needed — bare validity+canonical E.164 lookup; omit → provider default lookup with no add-on data packages) | `LookupResponse` → `Valid` (bool?), `PhoneNumber` (canonical E.164 string?) | `SdkException<RawError>` (B) | none | map `LookupsV2PhoneNumber.md`; `Models/LookupResponse.cs` |
| **Send SMS** (placed/dispatched/cancelled + resend) | `client.Api20100401Message.CreateMessage(string accountSid, string to, …24 params…, ct)` | `to` = dest E.164; **`from` = `Twilio:FromNumber`** (purpose: immediate messages go out from the configured sender so reconciliation's `From` filter finds them); `body` = message text; **all other 21 → `null`** (`messagingServiceSid` null here — using `from`; `scheduleType`/`sendAt` null → send now) | `ApiV2010AccountMessage` → `Sid`, `Status` (`MessageEnumStatus?`), `From`, `To`, `ErrorCode` (int?), `DateSent`, `DateCreated` | `SdkException<RawError>` (B) | none | map `Api20100401Message.md` CreateMessage; `Api/Api20100401Message.cs`; `Models/ApiV2010AccountMessage.cs` |
| **Schedule follow-up** ("how did delivery go", a few days out) | same `CreateMessage` | `to` = dest; **`messagingServiceSid` = `Twilio:MessagingServiceSid`** (purpose: scheduled messages MUST go via a Messaging Service, not a raw `from`); **`scheduleType` = `MessageEnumScheduleType.Fixed`**; **`sendAt` = now + 3 days** (`DateTimeOffset`); `body` = feedback text; **`from` null** (mutually exclusive with messagingServiceSid); rest `null` | `ApiV2010AccountMessage` → `Sid`, `Status` (expect `scheduled`) | `SdkException<RawError>` (B) | none | same; `Models/Enums/MessageEnumScheduleType.cs` (`Fixed`="fixed") |
| **Cancel scheduled follow-up** (before it sends) | `client.Api20100401Message.UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions?, CancellationToken ct)` | `sid` = the scheduled msg Sid; **`status` = `MessageEnumUpdateStatus.Canceled`**; **`body` = `null`** | `ApiV2010AccountMessage` → `Status` (expect `canceled`) | `SdkException<RawError>` (B) | none | map UpdateMessage; `Models/Enums/MessageEnumUpdateStatus.cs` (`Canceled`="canceled") |
| **Redact content at provider** (content disposal) | same `UpdateMessage` | `sid` = msg Sid; **`body` = `""`** (empty string redacts the text — method summary: "used to redact Message body text"); **`status` = `null`** | `ApiV2010AccountMessage` → `Body` (now empty), `Status` unchanged | `SdkException<RawError>` (B) | none | same; `Api/Api20100401Message.cs:236` remarks |
| **Fetch current status** (report outcome) | `client.Api20100401Message.FetchMessage(string accountSid, string sid, RequestOptions?, CancellationToken ct)` | `sid` = msg Sid | `ApiV2010AccountMessage` → `Status`, `ErrorCode`, `DateSent`, `To`, `From` | `SdkException<RawError>` (B) | none | map FetchMessage |
| **Reconcile** (provider's record over a range, this app's sender only) | `client.Api20100401Message.ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions?, CancellationToken ct)` | **`from` = `Twilio:FromNumber`** (wire `From` — ask the provider only for THIS sender's messages); **`dateSentQueryQuery` = range start** (wire `DateSent>`); **`dateSentQuery` = range end** (wire `DateSent<`); `pageSize` = 100; `page`/`pageToken` driven by loop; `to`/`dateSent` = `null` | `ListMessageResponse` → `Messages` (`IReadOnlyList<ApiV2010AccountMessage>?`), `NextPageUri` (string?), `Page` (int?) | `SdkException<RawError>` (B) | **page-based, NO auto-Pageable** — hand-drive `page` and stop when `NextPageUri==null`; page cap required (§ PAGED READS) | map ListMessage; `Models/ListMessageResponse.cs` |

Note: **`DeleteMessage` is deliberately NOT used** — deleting removes the whole provider record, but the task
requires the *fact of send + its outcome to survive*; redaction (empty `body` via UpdateMessage) is the
correct mechanism.

### Enum value tables (only what's used)

| Enum | Members used | Wire |
| --- | --- | --- |
| `TwilioSdk.Models.Enums.MessageEnumScheduleType` | `.Fixed` | `fixed` |
| `TwilioSdk.Models.Enums.MessageEnumUpdateStatus` | `.Canceled` | `canceled` |
| `TwilioSdk.Models.Enums.MessageEnumStatus` (response only) | read via `.Value` (NOT `.ToString()`) | `accepted/scheduled/queued/sending/sent/delivered/undelivered/failed/canceled/…` |

Reading a response `StringEnum` value → **`status?.Value`** (interpolation/`ToString()` give the debug form — `dotnet-models`).

### Client construction / auth / server node

- Construct via DI: `services.AddTwilioSdkClient(options => { … })` (`ServiceCollectionExtensions.cs`) — registers a **singleton** over the default `IHttpClientFactory` client, and fills `Logging.LoggerFactory` from the container.
- Auth (Basic): `options.AccountSidAuthToken = new TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials { Username = <Twilio:AccountSid>, Password = <Twilio:AuthToken> }`.
- Messaging base-URL override: **when `Twilio:BaseUrl` is set**, `options.Server.Default.Production.BaseUrl = <Twilio:BaseUrl>` (the `Default` group / `api.twilio.com` is where the Message resource lives — `Api/Api20100401Message.cs` uses `_server.Default(...)`). Lookups is server group `Default4` and is **NOT** overridden by this setting.
- `options.Environment` = `ServerEnvironment.Production` (only environment).

### CROSS-OPERATION INVARIANTS

| invariant | operations | enforced where |
| --- | --- | --- |
| A message may only be sent to a number the caller has registered (validated) | `CreateMessage` ← contact-number registration (`FetchPhoneNumber3` at register time) | `OrderNotifier` resolves the destination only from the caller's `ContactNumber` rows; register-time lookup stored the canonical E.164 that becomes `to` |
| Cancel-scheduled / redact / resend act only on a Sid this app recorded for a message it sent | `UpdateMessage`, `FetchMessage` ← the `OrderNotification.MessageSid` a prior `CreateMessage` produced | operator endpoints load the `OrderNotification` by id and use its stored `MessageSid`; never a caller-supplied Sid |
| Reconciliation compares only this app's sender | `ListMessage(from=FromNumber)` ↔ local `OrderNotification` rows whose `FromAddress == FromNumber` | reconciliation service filters the local side to `FromAddress == FromNumber` so scheduled (service-sent) rows don't invent discrepancies |

## 3. Trap notes (hazard + skill pointer — not resolved here)

- **Reading a response enum's wire value** (message status into our record / report): the debug-form-vs-wire-value trap on `StringEnum`. **MUST load `dotnet-models`.**
- **`Twilio:BaseUrl` override timing** — which env's server options are read, and that mutating after construction is a race. **MUST load `dotnet-configuration-resilience`.**
- **Per-attempt vs whole-call timeout**, and that a handler making several SDK calls (send-then-schedule; message-every-number loop) adds up the per-attempt budgets. **MUST load `dotnet-configuration-resilience`.**
- **Error boundary**: Case B `RawError` carries the status; connection failures are `HttpRequestException`/`TaskCanceledException` (NOT `SdkException`); a drifted 2xx body surfaces as `System.Text.Json.JsonException` from deserialization, NOT an `SdkException`, so an SDK-exception-only ladder lets it escape; a non-2xx body that doesn't match the operation's error shape throws `JsonException` while the error object is constructed, replacing the `SdkException` and destroying the status. **MUST load `dotnet-error-handling`.**
- **Named-argument binding** on 24-/15-param calls — a positional call mis-binds; `requestOptions` sits before `ct`. **MUST load `dotnet-calling-endpoints`.**
- **HttpClient/SDK-client lifetime** — both long-lived; the DI singleton caches DNS (set `PooledConnectionLifetime`). **MUST load `dotnet-client-initialization`.**

## 4. REQUIRED READING (load before implementation; contents deliberately not restated here)

| Skill | Governs |
| --- | --- |
| `twilio-platforms-team:dotnet-client-initialization` | Step 3 — client/DI, HttpClient lifetime |
| `twilio-platforms-team:dotnet-authentication` | Step 3 — Basic credential + startup fail-fast |
| `twilio-platforms-team:dotnet-calling-endpoints` | Step 6 — named-arg calls, form-body ops |
| `twilio-platforms-team:dotnet-models` | Step 6 — StringEnum read-back, DateTimeOffset |
| `twilio-platforms-team:dotnet-error-handling` | Step 6 — Case B boundary + JsonException (two directions) |
| `twilio-platforms-team:dotnet-configuration-resilience` | Step 3/6 — base-URL override, retry/timeout, hand-driven pagination |

Hazard rows (mandatory): `System.Text.Json.JsonException` reaches the boundary from **two** directions —
(1) a drifted/malformed **2xx** body deserializes to a `JsonException`, not an `SdkException`, so an
SDK-exception-only catch ladder lets it escape; (2) a **non-2xx** body not matching the generated error
shape throws `JsonException` *while the error object is built*, replacing the `SdkException` and destroying
the HTTP status. The gateway catches `JsonException` explicitly.

## 5. PRODUCTION READINESS

| # | Concern | Decision | where in the code |
| --- | --- | --- | --- |
| 1 | Credential fail-fast | `TwilioSettings` (`[Required]` on `AccountSid`, `AuthToken`, `FromNumber`, `MessagingServiceSid`) bound + `ValidateDataAnnotations().ValidateOnStart()`; **both** Basic halves checked (blank ≠ missing). `BaseUrl` optional. | `Infrastructure/Twilio/TwilioSettings.cs`; `PublicApi/Configuration/TwilioServiceCollectionExtensions.cs` |
| 2 | Secret sourcing & rotation | Secrets from **.NET user-secrets** (Development) / env at deploy; options built **once at registration** and captured in the singleton client → rotation needs a process restart (documented; acceptable for this reference app). | same extension |
| 3 | Total timeout budget | Each SDK call bounded by a linked `CancellationToken` deadline (`RequestAborted` + `CancelAfter`) in the gateway's `Bounded(...)`; per-attempt `Retry.Timeout` set to 10s; the message-every-number loop and send-then-schedule share one handler-level budget. | `TwilioMessagingGateway.Bounded` |
| 4 | Write-retry ownership | All writes here are `POST` (CreateMessage/UpdateMessage) or `GET` (Fetch/List) → SDK default `HttpMethodsToRetry` (`GET,HEAD,PUT,OPTIONS`) never resends the POSTs; no `PUT` in scope. Left at default. | client registration |
| 5 | Idempotency & ambiguous writes | CreateMessage exposes **no real provider idempotency key** (the wire `Idempotency-Key` header is generator-injected `Guid.NewGuid()` — not one). Resend takes a **caller-supplied key**, enforced by a **unique index** on `OrderNotification.IdempotencyKey` + catch of the DB rejection; ambiguous transport failures surfaced via reconciliation (§ UNKNOWN OUTCOMES). | `OrderNotifier.ResendAsync`; `OrderNotificationConfiguration` |
| 6 | Observability | Structured logs at Info (action), Warning (send failed / provider non-success). **The shopper's phone number and message body are never logged** (only notification id, order id, message Sid, provider status/error-code). SDK `LogRequestBody` stays off; `Retry.OnRetry` logs reason. | gateway + notifier logging |
| 7 | Sensitive data | Request bodies carry the destination number + message text (personal). `LogRequestBody` stays **off** and `LoggerFactory` is set by the DI extension (env var `TWILIOSDKCLIENT_LOG` cannot arm body logging because the factory is non-null). Our own logs never echo number/body. | client registration; gateway |
| 8 | Environment selection | One environment (`Production`). Messaging (`Default`/api.twilio.com) is overridable via `Twilio:BaseUrl` for a mock/sandbox host; Lookups (`Default4`) is not. No separate SDK sandbox exists — a mock base URL is how non-live test traffic would be diverted. Live account here: volume kept minimal, only the two provided destinations. | client registration |
| 9 | Duplicate prevention under concurrency | Store+column: **`OrderNotification.IdempotencyKey`**, a **UNIQUE index**; the second insert is rejected by the constraint and the notifier **catches `DbUpdateException`** then re-reads by key. (A pre-write existence check exists only as a non-authoritative fast path / in-memory-provider aid, not the claim.) | `OrderNotifier.ResendAsync`; `OrderNotificationConfiguration.HasIndex(...).IsUnique()` |
| 10 | Partial results | Reconciliation pages `ListMessage` with a **page cap (`MaxPages=50`)**; on hitting it the result carries `Truncated=true` (a field the caller reads), not just a log line. | `TwilioMessagingGateway.ListSentMessagesAsync`; reconciliation response DTO |
| 11 | Startup validation vs test host | `ValidateOnStart` added; the host-booting test project is `tests/PublicApiIntegrationTests` (`WebApplicationFactory`). It is given placeholder `Twilio:*` values via config so the host boots; run it green. | test host config override |
| 12 | Ordering & no-op side effects | Local `OrderNotification` row (with reference/idempotency key) is written **before** the provider call and completed with the Sid/status after. Dispatch/cancel gate their notification + follow-up on whether the `Order.Status` transition actually changed (idempotent transition returns "unchanged" → no message). | `OrderNotifier`; `Order.MarkDispatched/MarkCancelled` |
| 13 | Unknown outcomes | On transport failure of a send, the row is marked `SendFailed` with no Sid; the **reconciliation** report (`ListMessage` by `From`+range) re-reads provider state and surfaces a message the provider has that eShop recorded as failed (providerOnly). | `OrderNotifier` catch; reconciliation |
| 14 | Provider status & reconciliation clock | `CreateMessage`/`Fetch` return `Status` (`MessageEnumStatus`): each notification stores the provider status; delivered/sent = reached, failed/undelivered/canceled = not reached, queued/sending/scheduled/accepted = pending (distinct path); resend targets only not-reached. Reconciliation filters **both** sides on the **provider send time** (`DateSent`), never a local row-creation column; scheduled rows (no `DateSent` yet) are out-of-window, not discrepancies. | `OrderNotifier.MapStatus`; reconciliation service |

### DUPLICATE CLAIMS

| write | where the claim is stored | what rejects the second one | where that rejection is caught | where in the code |
| --- | --- | --- | --- | --- |
| Resend a notification (caller idempotency key) | `OrderNotification.IdempotencyKey` column (CatalogContext) | UNIQUE index on `IdempotencyKey` | `catch (DbUpdateException)` → re-read by key, return existing notificationId | `OrderNotifier.ResendAsync` |

Placed/dispatched/cancelled sends are operator/shopper actions gated by the `Order.Status` transition
(§ REPEATED OPERATIONS), not by an idempotency key — a repeated dispatch is a no-op, so no duplicate send.

### PAGED READS

| read | what caps it | how the caller learns it was cut short | where in the code |
| --- | --- | --- | --- |
| Reconciliation `ListMessage` loop | `MaxPages=50` (100/page) + `NextPageUri==null` | reconciliation response `Truncated` (bool) field | `TwilioMessagingGateway.ListSentMessagesAsync` → `ReconciliationResponse.Truncated` |

### REPEATED OPERATIONS

| operation | what tells you the state actually changed | the effects gated on that | where in the code |
| --- | --- | --- | --- |
| `POST /orders/{id}/dispatch` | `Order.MarkDispatched()` returns true only if status was `Placed` | the "on its way" SMS + the scheduled follow-up | `OrderNotifier.DispatchAsync` |
| `POST /orders/{id}/cancel` | `Order.MarkCancelled()` returns true only if not already cancelled | the "cancelled" SMS + cancelling the pending follow-up | `OrderNotifier.CancelAsync` |
| `POST /notifications/{id}/resend` | insert of a new row under a fresh idempotency key succeeds | the resent `CreateMessage` | `OrderNotifier.ResendAsync` |

### UNKNOWN OUTCOMES

| write | the operation you re-read with | the reference you search by | where in the code |
| --- | --- | --- | --- |
| `CreateMessage` (any send) transport-fails after bytes may have left | `ListMessage` (reconciliation endpoint) | `From = Twilio:FromNumber` + `DateSent` range | `OrderNotifier` marks `SendFailed`; operator runs `GET /notifications/reconciliation` which re-reads and shows providerOnly |

A notification is fire-and-forget (never fails the order op); the re-read is the reconciliation report rather
than an inline catch, because a duplicate courtesy SMS is low-harm and the report is the designed detector.

### OPERATION OUTCOMES

| write | the status field | every value it can hold, and what the app does | where in the code |
| --- | --- | --- | --- |
| CreateMessage (send) | `ApiV2010AccountMessage.Status` (`MessageEnumStatus`) | `delivered/sent` → Reached; `failed/undelivered` → NotReached (resend-eligible); `queued/sending/accepted/scheduled` → Pending (re-fetchable); `canceled` → Cancelled; absent/unreadable → Pending (never defaulted to success) | `OrderNotifier.MapStatus` |
| CreateMessage (schedule) | same | expect `scheduled` → Pending; anything else handled by the same map | same |
| UpdateMessage (cancel) | same | expect `canceled`; if already sent, provider errors → surfaced, follow-up simply already gone | `OrderNotifier.CancelAsync` |

### WRITE ORDER

| write | what exists locally BEFORE the call | what is written after it returns | where in the code |
| --- | --- | --- | --- |
| Send (placed/dispatched/cancelled/resend) | `OrderNotification` row (OrderId, BuyerId, Kind, ToNumber, FromAddress, Status=Pending, IdempotencyKey for resend) | `MessageSid`, provider `Status`, `ErrorCode`, `DateSent` | `OrderNotifier.*` |
| Schedule follow-up | `OrderNotification` row (Kind=DeliveryFeedback, ScheduledSendAt, Status=Pending) | `MessageSid`, `Status=scheduled` | `OrderNotifier.DispatchAsync` |
| Redact content | (row already exists from the original send) | local `Body` cleared + `ContentRedacted=true` after provider redaction returns | `OrderNotifier.RedactAsync` |

## 6. Assumptions & Blockers

- **Blockers: none.** Every required capability is in the map.
- Assumptions (minor, decided): (a) message **all** of a shopper's registered numbers on an order event
  (demonstrates deliverable + undeliverable cleanly; volume stays at the two provided destinations);
  (b) follow-up `SendAt` = now + **3 days** (within Twilio's fixed-schedule window; "a few days");
  (c) `Order.Status` (Placed/Dispatched/Cancelled) is added additively to gate transitions — existing
  constructors/tests unaffected (defaults to Placed); (d) content-disposal redacts (UpdateMessage body="")
  rather than deletes, so the send + outcome survive; (e) EF **in-memory** provider does not enforce the
  unique index, so the resend path also does a non-authoritative pre-read fast path — the unique constraint
  remains the production claim (row 9).
