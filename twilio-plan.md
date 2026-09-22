# twilio-plan.md — Order notifications by SMS (Twilio) for eShopOnWeb

Scope: additive SMS order notifications on `src/PublicApi`, provider = Twilio, via the
**twilio-platforms-team** SDK (root namespace `TwilioSdk`). This plan is the contract record;
implementation reads from it and does not re-derive facts from memory.

---

## 1. Scope & sequence

Layering follows the app's clean-architecture split (ApplicationCore interfaces/entities →
Infrastructure implementations + EF → PublicApi endpoints). The SDK is referenced only from
Infrastructure (behind `ISmsProvider`); ApplicationCore and PublicApi never see `TwilioSdk.*`.

1. **Vendor the SDK for build.** Copy the SDK source (buildable dirs only, no `map/`,
   `sdk-map.md`, `api-reference.md`) into `src/TwilioSdk/`; opt it out of central package
   management. Add a `ProjectReference` Infrastructure → TwilioSdk. (Not a plan *contract* — a
   build wiring step.)
2. **Config + fail-fast.** Bind `TwilioSettings` from the `Twilio:` section
   (`AccountSid`, `AuthToken`, `FromNumber`, `MessagingServiceSid`, `BaseUrl`). Validate at
   startup: refuse to boot if any of AccountSid/AuthToken/FromNumber/MessagingServiceSid is
   missing or blank (`BaseUrl` optional). Load values into user-secrets (never into repo files).
3. **DI-register the client** via `services.AddTwilioSdkClient(...)` (Infrastructure): Basic
   auth = AccountSid/AuthToken; when `Twilio:BaseUrl` set, override
   `options.Server.Default.Production.BaseUrl` (messaging = `Default` group only; Lookups on
   `Default4` is deliberately left on its default host). Explicit `LoggerFactory`,
   `LogRequestBody` OFF, per-attempt `Timeout`, and a total-budget `CancellationToken`.
4. **Entities + EF config + DbSets** on `CatalogContext`: `ContactNumber`, `SmsNotification`,
   `SmsIdempotencyKey`. In-memory-safe migrations note (data lives one run only).
5. **`ISmsProvider` (ApplicationCore) + `TwilioSmsProvider` (Infrastructure)** wrapping the five
   SDK operations below; maps `ApiV2010AccountMessage` → domain `ProviderMessage`.
6. **`IOrderNotificationService` (ApplicationCore) + impl** orchestrating place/dispatch/cancel
   + resend + content-disposal + reconciliation, using repositories + `ISmsProvider`. A send
   failure never fails the underlying order operation.
7. **PublicApi endpoints** (`/api/…`) per the task, JWT-auth, operator actions gated to
   `Administrators`, shopper actions scoped to `User.Identity.Name`.
8. **Self-verify** live: real send to the Canadian test number, scheduled follow-up
   queued+cancelled, undeliverable US outcome, resend idempotency, reconciliation over a range.

`DeleteMessage` (Api20100401Message) is deliberately **not** used: it removes the whole provider
record, but the task requires "the fact that a message was sent, and what became of it, survives".
Content disposal is therefore `UpdateMessage(body:"")` — Twilio message-body redaction.

---

## 2. CONTRACT SHEET

> ⚠ Signatures below are **generated code, verbatim**. Every parameter name is the literal C#
> identifier; the cancellation-token parameter is named `ct`, so named arguments write `ct:`.
> ⚠ Every SDK type is written **fully-qualified** with the namespace its source path implies,
> taken from the path the map gives for THAT type (records → `TwilioSdk.Models`, enums →
> `TwilioSdk.Models.Enums`, credentials → `TwilioSdk.Core.Authentication.Basic`, client/options
> → `TwilioSdk`, servers → `TwilioSdk.Servers`).

### Client construction / auth / servers (source: `sdk-map.md`, `TwilioSdkClientOptions.cs`, `ServiceCollectionExtensions.cs`, `Servers/DefaultOptions.cs`, `Core/Authentication/Basic/BasicAuthCredentials.cs`)

- Client: `TwilioSdk.TwilioSdkClient(HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)`. Every API group is a property (`client.Api20100401Message`, `client.LookupsV2PhoneNumber`).
- DI: `services.AddTwilioSdkClient(Action<TwilioSdkClientOptions>? configure)` — registers the client **singleton**, options built **once at registration**, HttpClient from `IHttpClientFactory`.
- Auth: `options.AccountSidAuthToken = new TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials { Username = <AccountSid>, Password = <AuthToken> }`. Both members `required`. A never-set credential is skipped silently → a 401 can mean "nothing sent".
- Environment: `options.Environment = TwilioSdk.Servers.ServerEnvironment.Production` (the only member; also the default).
- Server override (messaging): `options.Server.Default.Production.BaseUrl = <Twilio:BaseUrl>` (default `https://api.twilio.com`). Message ops resolve through group `Default`; only override when configured.
- Options surface: `Environment`, `Retry: RetryOptions`, `Logging: LoggingOptions`, `Server: ServerOptions`, `Hooks`, `AccountSidAuthToken`. `RetryOptions` members incl. `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout: TimeSpan?`, `UseExponentialBackoff`; build from `RetryOptions.Default()`.

### Operations

| # | op | signature (verbatim) · returns · error | fields read | source |
| --- | --- | --- | --- | --- |
| A | `client.LookupsV2PhoneNumber.FetchPhoneNumber3` | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` · returns `TwilioSdk.Models.LookupResponse` · **Case B** (`SdkException<RawError>`). **Server group `Default4`** (lookups host — NOT governed by `Twilio:BaseUrl`). | `LookupResponse.Valid` (bool?) → reject when not `true`; `LookupResponse.PhoneNumber` (string, canonical E.164) → the value stored. | `map/operations/LookupsV2PhoneNumber.md`; `Models/LookupResponse.cs` |
| B | `client.Api20100401Message.CreateMessage` | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 24 middle params nullable-no-default → **pass explicitly** · returns `TwilioSdk.Models.ApiV2010AccountMessage` · **Case B**. Group `Default`. | `.Sid`, `.Status` (`MessageEnumStatus?`), `.ErrorCode` (int?), `.ErrorMessage`, `.To`, `.From`, `.DateSent`, `.Body` | `map/operations/Api20100401Message.md`; `Models/ApiV2010AccountMessage.cs` |
| C | `client.Api20100401Message.UpdateMessage` | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` · returns `ApiV2010AccountMessage` · **Case B**. Group `Default`. | `.Sid`, `.Status`, `.Body` | same page; `Models/ApiV2010AccountMessage.cs`, `Models/Enums/MessageEnumUpdateStatus.cs` |
| D | `client.Api20100401Message.FetchMessage` | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` · returns `ApiV2010AccountMessage` · **Case B**. Group `Default`. | `.Sid`, `.Status`, `.ErrorCode`, `.ErrorMessage`, `.DateSent`, `.To`, `.From`, `.Body` | same page; `Models/ApiV2010AccountMessage.cs` |
| E | `client.Api20100401Message.ListMessage` | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 8 middle params nullable-no-default → **pass explicitly** · returns `TwilioSdk.Models.ListMessageResponse` · **Case B**. Group `Default`. Wire: `From`←`from`, `DateSent<`←`dateSentQuery` (upper bound), `DateSent>`←`dateSentQueryQuery` (lower bound), `PageSize`←`pageSize`, `Page`←`page`, `PageToken`←`pageToken`. | `.Messages` (`IReadOnlyList<ApiV2010AccountMessage>`), `.NextPageUri`, `.Page`, `.PageSize` | `map/operations/Api20100401Message.md`; `Models/ListMessageResponse.cs` |

### Enums needed (source: `Models/Enums/…`)

- `MessageEnumScheduleType.Fixed` (`"fixed"`) — schedule a message (with MessagingServiceSid + sendAt).
- `MessageEnumUpdateStatus.Canceled` (`"canceled"`) — the ONLY update-status value; cancels a scheduled message.
- `MessageEnumStatus`: `Queued, Sending, Sent, Failed, Delivered, Undelivered, Receiving, Received, Accepted, Scheduled, Read, PartiallyDelivered, Canceled`. Terminal-failure set for reporting: `Failed`, `Undelivered`, `Canceled`. Terminal-success: `Delivered`, `Sent`, `Received`, `Read`. `.ToString()`/`.Value` is the wire string. Read via SDK `StringEnum` — compare with the static members, never a re-derived string.

### CreateMessage optional-field decisions (⚠ every optional carried has a purpose; unset → provider default)

| field | used? | purpose |
| --- | --- | --- |
| `to` (required, positional) | yes | recipient E.164 (the shopper's stored canonical number). |
| `from` | **immediate msgs**: set = `Twilio:FromNumber`; **scheduled**: `null` | reconciliation counts by `Twilio:FromNumber`, so placed/dispatched/cancelled/resend send from it. Scheduled messages must use a Messaging Service, so `from` is omitted there → provider default. |
| `messagingServiceSid` | **scheduled**: set = `Twilio:MessagingServiceSid`; **immediate**: `null` | Twilio message scheduling requires a Messaging Service. Immediate messages use `from` instead → omit → provider default. |
| `scheduleType` | scheduled only = `MessageEnumScheduleType.Fixed`; else `null` | marks the create as a scheduled send. omit → provider default (immediate). |
| `sendAt` | scheduled only = now + 3 days; else `null` | when the follow-up goes out (within Twilio's 15min–7day window). omit → provider default. |
| `body` | yes | the message text. |
| all other 20 params | `null` | omit → provider default. Not `UNVERIFIED` — they are explicit "no override" (`statusCallback` unused because there is no public callback URL for this app — status is polled via FetchMessage). |

### CROSS-OPERATION INVARIANTS

| invariant | operations | enforced where |
| --- | --- | --- |
| A message resent / fetched / redacted / cancelled must reference a `Sid` this app itself created and stored on an `SmsNotification`. | `UpdateMessage`/`FetchMessage` ← `CreateMessage` | implementation (`SmsNotification.ProviderSid`, looked up by notification id; operator endpoints act only on stored notifications) |
| The number a message is sent `to` must be a `ContactNumber` the caller registered (canonical form from Lookup), still present (not deleted). | `CreateMessage` ← `FetchPhoneNumber3` + local delete | implementation (send iterates the owner's live `ContactNumber` rows only) |
| Reconciliation's provider side is filtered by the same `from` the app sends immediate messages with. | `ListMessage.from` ← `CreateMessage.from` (both = `Twilio:FromNumber`) | implementation |

---

## 3. Trap notes (name the hazard + the skill; not resolved here)

- **Client/HttpClient lifetime & DI singleton capture.** Getting the client + HttpClient
  lifetime wrong (rebuild per request, or capture a rotated secret) is invisible in the
  signature. `MUST load twilio-platforms-team:dotnet-client-initialization`.
- **Setting credentials & where secrets come from.** How/when to set `AccountSidAuthToken` and
  load it from config not code. `MUST load twilio-platforms-team:dotnet-authentication`.
- **Calling with named args & the injected Idempotency-Key.** 24/8 nullable-no-default params
  mis-bind positionally; the generator's per-call `Idempotency-Key: Guid.NewGuid()` header is
  NOT a real key. `MUST load twilio-platforms-team:dotnet-calling-endpoints`.
- **`StringEnum`/union/extension-data model mechanics.** `MessageEnumStatus` is not a C# enum;
  reading `Status` and comparing it has rules the type won't show.
  `MUST load twilio-platforms-team:dotnet-models`.
- **Error boundary — two JsonException directions + Case B accessors.** A drifted 2xx body and a
  non-matching error body fail in opposite ways; `RawError` has only four accessors.
  `MUST load twilio-platforms-team:dotnet-error-handling`.
- **Retry eligibility, per-attempt timeout vs total budget, `LogRequestBody`, list pagination.**
  What retries, what a timeout bounds, what logging leaks, how the list is walked.
  `MUST load twilio-platforms-team:dotnet-configuration-resilience`.
- **Test seam is the `HttpClient` ctor arg.** `MUST load twilio-platforms-team:dotnet-testing`.

---

## 4. REQUIRED READING (load ALL before implementation; this sheet does NOT carry their contents)

- `twilio-platforms-team:dotnet-client-initialization` · step 3 (client + DI)
- `twilio-platforms-team:dotnet-authentication` · step 3 (credentials)
- `twilio-platforms-team:dotnet-calling-endpoints` · steps 5–6 (every SDK call)
- `twilio-platforms-team:dotnet-models` · steps 5–6 (status enum, response mapping)
- `twilio-platforms-team:dotnet-error-handling` · step 6 (error boundary — always required)
- `twilio-platforms-team:dotnet-configuration-resilience` · step 3 (retries/timeout/logging/pagination)
- `twilio-platforms-team:dotnet-testing` · tests

Mandatory hazard rows (both, verbatim, because `System.Text.Json.JsonException` reaches the
boundary from two directions needing opposite handling):
- A drifted/malformed **2xx** body (missing `required` member) surfaces as `JsonException` from
  deserialization, **not** `SdkException` — an SDK-exception-only catch ladder lets it escape.
- A **non-2xx** body that doesn't match its operation's generated error shape throws
  `JsonException` **while the error object is being constructed**, replacing the `SdkException`
  and destroying the HTTP status. (Here every op in scope is Case B/`RawError`, so the error
  shape is the raw bytes — but the boundary still catches `JsonException` alongside
  `SdkException<RawError>`.)

---

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `TwilioSettingsValidator` (`IValidateOptions`) + `.ValidateOnStart()` refuse to boot if any of `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid` is null/whitespace. Each part checked individually (a blank part ≠ missing). `BaseUrl` optional. Where: `src/Infrastructure/Sms/TwilioSettings.cs` (`TwilioSettingsValidator`), `SmsNotificationServiceExtensions.AddSmsNotifications`. |
| 2 | Secret sourcing & rotation | Secrets from **.NET user-secrets** (loaded from env by the operator; never in repo). `AddSmsNotifications` reads config once at registration and `AddTwilioSdkClient` captures the options in the client singleton → a rotated secret needs a process restart. Restartless rotation not required. Where: `src/Infrastructure/Sms/SmsNotificationServiceExtensions.cs`. |
| 3 | Total timeout budget | `RetryOptions.Timeout` = 15s **per attempt**; each provider call wraps a linked `CancellationTokenSource` (`CallBudget`=30s; `ListBudget`=90s for the page walk) passed as `ct:`, bounding the whole call incl. retries. Where: `src/Infrastructure/Sms/TwilioSmsProvider.cs` (`CallBudget`/`ListBudget`, every method). |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS, so `CreateMessage`/`UpdateMessage` (POST) are **never auto-resent** by the SDK. `FetchMessage`/`ListMessage` (GET) retry freely. Left at default. Where: `SmsNotificationServiceExtensions` (`RetryOptions.Default() with { Timeout }`, list untouched). |
| 5 | Idempotency & ambiguous writes | `CreateMessage`/`UpdateMessage` take **no** real caller key. Resend uses an **app-level caller-supplied key** claimed in `SmsIdempotencyKey` (PK). Order sends are gated on a local state transition (row 12) so they cannot double-send within a run; cross-attempt duplicates are reconciled via `ListMessage`. Where: `src/ApplicationCore/Services/OrderNotificationService.cs` (`ResendAsync`); `src/Infrastructure/Data/ResendIdempotencyStore.cs`. |
| 6 | Observability | Structured logs: Info (order placed / notification refreshed), Warning (send rejected / follow-up not cancelled / transport unknown), Error (provider fault). Twilio correlation = message `Sid`/error code logged. `LogRequestBody` OFF. **Recipient phone numbers are never logged** (log notification id / order id). Where: `TwilioSmsProvider` (`ProviderFault`, warnings), `OrderNotificationService` (`_logger`). |
| 7 | Sensitive data | Request bodies carry the recipient number (`to`) and message text → PII. `LogRequestBody` stays **OFF**; `AddTwilioSdkClient` fills `LoggerFactory` from the container (non-null) so the `TWILIOSDKCLIENT_LOG` env var cannot switch body logging on. Our own logs never echo `to`/`body`. Where: `SmsNotificationServiceExtensions` (`LoggingOptions`), `TwilioSmsProvider` (logging). |
| 8 | Environment selection | Two server groups touched: `Default` (messaging create/update/fetch/list — `api.twilio.com`, overridable by `Twilio:BaseUrl` via `options.Server.Default.Production.BaseUrl`) and `Default4` (Lookups — `lookups.twilio.com`, left default). No sandbox env; test traffic kept safe by only registering/messaging the two supplied numbers. Where: `SmsNotificationServiceExtensions.AddSmsNotifications`. |
| 9 | Duplicate prevention under concurrency | Resend: `SmsIdempotencyKey`, **`Key` is the primary key**; inserted first, duplicate PK rejected by the store (SQL Server PK; EF InMemory rejects duplicate PK at `SaveChanges`). `ResendIdempotencyStore.TryClaimAsync` catches the rejection, detaches, and replays — no existence-check, no lock, no dictionary. **Verified live + unit test.** Where: `src/Infrastructure/Data/ResendIdempotencyStore.cs`, `SmsIdempotencyKeyConfiguration.cs`. |
| 10 | Partial results | Reconciliation walks `ListMessage` pages via `NextPageUri`→`Page`/`PageToken` until null, capped at `MaxReconciliationPages` (50). On the cap the response returns `Truncated=true` + `PagesFetched`. Where: `TwilioSmsProvider.ListSentMessagesAsync`; surfaced by `ReconciliationEndpoint`/`ReconciliationResponse`. |
| 11 | Startup validation vs test host | `PublicApiIntegrationTests` boots the PublicApi host (`WebApplicationFactory<Program>`). `appsettings.test.json` carries `UseOnlyInMemoryDatabase=true` + placeholder `Twilio:*` so fail-fast passes and no live call is made. **Ran green (23 tests).** Where: `tests/PublicApiIntegrationTests/appsettings.test.json`, `OperatorEndpointsAuthTest`/`ShopperOrderFlowTest`. |
| 12 | Ordering & no-op side effects | `SmsNotification` row (State=Pending) written **before** the provider call; provider Sid+status written **after**. Dispatch/cancel flip `Order.NotificationStatus` via `TryMark…()` and **only** send/schedule/cancel when it returns true. Where: `OrderNotificationService` (`SendImmediateToBuyerAsync`, `Dispatch`/`CancelOrderAsync`), `Order.cs` (`TryMarkDispatched`/`TryMarkCancelled`). |
| 13 | Unknown outcomes | On transport failure `TwilioSmsProvider.CreateMessageAsync` returns `SmsSendOutcome.Unknown` (not a failure). `ApplySendResultAsync` marks the notification `Unknown` and re-reads via `FindRecentAsync` (`from`=FromNumber + recipient, recent `DateSent>`) before concluding; refresh via `FetchAsync`. Where: `OrderNotificationService.ApplySendResultAsync`; `TwilioSmsProvider.FindRecentAsync`/`FetchAsync`. |
| 14 | Provider status & reconciliation clock | Status flows `ApiV2010AccountMessage.Status` → `ProviderStatus`. Branches: reached (`delivered/sent/...`), did-not-reach (`FailedStatuses`), non-terminal (refreshed), Unknown (re-read) — never defaulted to success. Reconciliation filters **both** sides on the provider **send** time: provider via `DateSent</DateSent>`, eShop via `SmsNotification.ProviderDateSent` (populated from `date_sent`, refreshed for null rows before comparing). Where: `OrderNotificationService` (`ReconcileAsync`, `RefreshOutcomeIfNeededAsync`, `FailedStatuses`/`NonTerminalStatuses`), `SmsNotificationsSentInRangeSpecification`. |

### DUPLICATE CLAIMS

| write | where the claim is stored | what rejects the second one | where that rejection is caught | where in the code |
| --- | --- | --- | --- | --- |
| resend a notification | `SmsIdempotencyKey` table, `Key` column = **primary key** (`SmsIdempotencyKeyConfiguration.HasKey(k => k.Key)`) | store PK constraint (SQL Server PK; EF InMemory rejects duplicate PK at SaveChanges) | `Infrastructure/Data/ResendIdempotencyStore.TryClaimAsync` catch `when (IsDuplicateKey(ex))` → detaches the failed entity, reads the committed claim, returns its `NotificationId`; consumed by `OrderNotificationService.ResendAsync` which replays without sending | `src/Infrastructure/Data/ResendIdempotencyStore.cs` (`TryClaimAsync`), `src/ApplicationCore/Services/OrderNotificationService.cs` (`ResendAsync`) |

### PAGED READS

| read | what caps it | how the caller learns the answer was cut short | where in the code |
| --- | --- | --- | --- |
| reconciliation `ListMessage` walk | `MaxReconciliationPages` (=50) page-loop guard | response fields `Truncated` (bool) + `PagesFetched`, surfaced through `ReconciliationReport` → `ReconciliationResponse.Truncated`/`ProviderPagesFetched` | `src/Infrastructure/Sms/TwilioSmsProvider.cs` (`ListSentMessagesAsync` while-loop); `src/PublicApi/NotificationEndpoints/ReconciliationEndpoint.cs` |

### REPEATED OPERATIONS

| operation | what tells you the state actually changed | the effects gated on that | where in the code |
| --- | --- | --- | --- |
| dispatch order | `Order.TryMarkDispatched()` returns true (was `Placed`, not Dispatched/Cancelled) | send "on its way" + schedule follow-up | `src/ApplicationCore/Entities/OrderAggregate/Order.cs` (`TryMarkDispatched`); `OrderNotificationService.DispatchOrderAsync` (returns `NoChange` when false, before any send) |
| cancel order | `Order.TryMarkCancelled()` returns true (was not already Cancelled) | send "cancelled" + cancel the scheduled follow-up | `src/ApplicationCore/Entities/OrderAggregate/Order.cs` (`TryMarkCancelled`); `OrderNotificationService.CancelOrderAsync` |
| place order | a new `Order` row was persisted (`_orders.AddAsync` yields a new id) | send "order placed" | `src/ApplicationCore/Services/OrderNotificationService.cs` (`PlaceOrderAsync`) |

### UNKNOWN OUTCOMES

| write | the operation you re-read with | the reference you search by | where in the code |
| --- | --- | --- | --- |
| `CreateMessage` (send) | `ISmsProvider.FindRecentAsync` → `ListMessage` | `from`=`Twilio:FromNumber` + `to`=recipient, `DateSent>` = `notification.CreatedAt - 2 min` | provider returns `SmsSendOutcome.Unknown` (`TwilioSmsProvider.CreateMessageAsync` catch); settled in `OrderNotificationService.ApplySendResultAsync` `default:` branch (marks `Unknown`, then `FindRecentAsync`, then `UpdateProviderOutcome`) |
| `UpdateMessage` (cancel/redact) | `FetchMessage` (via `GetOrderNotificationsAsync` refresh / `ReconcileAsync`) | the `Sid` already stored on the notification | `TwilioSmsProvider.FetchAsync`; `OrderNotificationService.RefreshOutcomeIfNeededAsync` |

### OPERATION OUTCOMES

| write | the status field | every value it can hold, and what the app does with each | where in the code |
| --- | --- | --- | --- |
| `CreateMessage` send | `ApiV2010AccountMessage.Status` (→ `SmsSendResult.Status` / `SmsNotification.ProviderStatus`) | provider `Accepted` → `MarkSent` + store status; `queued/sending/accepted/scheduled/sent/receiving` = non-terminal, refreshed via `FetchMessage` (`NonTerminalStatuses`); `delivered/sent/received/read` = reached; `failed/undelivered/canceled` = did-not-reach / resend-eligible (`FailedStatuses`, `IsFailedStatus`); provider `Rejected` → `MarkFailed`; transport `Unknown` → `MarkUnknown` + re-read (never defaulted to success) | `src/ApplicationCore/Services/OrderNotificationService.cs` (`ApplySendResultAsync`, `RefreshOutcomeIfNeededAsync`, `NonTerminalStatuses`/`FailedStatuses`) |
| `UpdateMessage` cancel | `.Status` | provider `Accepted` → `MarkCancelled(status)` (expect `canceled`); non-Accepted → logged Warning "may still be delivered", state left so reconciliation shows it | `OrderNotificationService.CancelPendingFollowUpsAsync` |
| `UpdateMessage` redact | (no status branch) | success → `MarkContentRedacted`; failure throws `SmsProviderException` → endpoint 502, not marked | `OrderNotificationService.DisposeContentAsync`; `TwilioSmsProvider.RedactContentAsync`; `DisposeNotificationContentEndpoint` |

### WRITE ORDER

| write | what exists locally BEFORE the call | what is written after it returns | where in the code |
| --- | --- | --- | --- |
| send order-placed/dispatched/cancelled | `SmsNotification` row (owner, orderId, kind, recipient, State=Pending) via `_notifications.AddAsync` | `MarkSent`/`MarkFailed`/`MarkUnknown` sets `ProviderSid`, `ProviderStatus`, `ProviderDateSent`, `ErrorCode`, `ErrorMessage` | `OrderNotificationService.SendImmediateToBuyerAsync` (AddAsync before `_sms.SendAsync`), `ApplySendResultAsync` |
| resend | `SmsNotification` (kind=Resend, State=Pending) via `AddAsync`, then key claimed | `ApplySendResultAsync` after `_sms.SendAsync` | `OrderNotificationService.ResendAsync` |
| schedule follow-up | `SmsNotification` row (kind=DeliveryFollowUp, State=Pending) via `AddAsync` | `MarkScheduled` sets `ProviderSid` + `ProviderStatus`=scheduled | `OrderNotificationService.ScheduleFollowUpToBuyerAsync` |

---

## 6. Assumptions & Blockers

**Blockers:** none. Every capability the task needs maps to an in-scope operation:
number validation+canonicalization → `FetchPhoneNumber3`; send → `CreateMessage`; schedule
follow-up → `CreateMessage`(scheduleType=Fixed); cancel follow-up → `UpdateMessage`(Canceled);
content disposal → `UpdateMessage`(body:""); status refresh → `FetchMessage`; reconciliation →
`ListMessage`(from=FromNumber, DateSent range).

**Assumptions (minor — proceeding):**
- US destinations undeliverable for this account is an **outcome** (status `undelivered`/`failed`),
  not a gap — handled as a delivery outcome.
- "A few days later" for the follow-up = **3 days** (inside Twilio's 15min–7day scheduling window).
- Number validity/usability = Lookup `valid == true` (reject otherwise). A reserved-but-valid US
  number (the unreachable fixture) still registers; its undeliverability shows only when messaged.
- A shopper's order-notification is sent to **all** of that shopper's currently-registered numbers
  at event time (deleted numbers excluded). Verification registers one number per shopper.
- Placing an order needs a `ShipToAddress` (Order requires it): request may carry one; otherwise a
  placeholder address is used. Notifications are the feature under test, not shipping.
- Content disposal = provider-side body **redaction** (`UpdateMessage(body:"")`), which keeps the
  record + status while removing retrievable text — matching "the fact/outcome survives".
