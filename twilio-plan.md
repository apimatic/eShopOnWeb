# twilio-plan.md — SMS order notifications for eShopOnWeb

Integration plan + SDK contract sheet for adding Twilio SMS order notifications to `src/PublicApi`.
Root namespace of the SDK is **`TwilioSdk`**, client class **`TwilioSdkClient`**, options **`TwilioSdkClientOptions`**
(the getting-started orientation table's `TwilioClient`/`Twilio` names are stale — the source declares `TwilioSdk*`).

---

## 1. Scope & sequence

Layering follows the repo: domain + interfaces in `ApplicationCore`, Twilio adapter + persistence in `Infrastructure`,
HTTP endpoints in `PublicApi` (MinimalApi.Endpoint `IEndpoint<…>` style, `[Authorize]` for roles).

1. **Vendor the SDK** into `src/TwilioSdk` (source copy, CPM opted out), add to `eShopOnWeb.sln`, `ProjectReference` from `Infrastructure`.
2. **Domain (ApplicationCore)**: `ContactNumber`, `OrderNotification` (+ `NotificationKind` enum) aggregates; extend `Order` with a `Status` (Placed/Dispatched/Cancelled). Add `ISmsGateway` + result DTOs, `IContactNumberService`, `IOrderNotificationService`.
3. **Infrastructure**: `TwilioOptions`, `TwilioOptionsValidator` (fail-fast), `TwilioSmsGateway : ISmsGateway` (all SDK calls + error boundary), EF configs + DbSets on `CatalogContext`, DI registration `AddTwilioSms`.
4. **ApplicationCore services**: `ContactNumberService` (validate+canonicalize, list, delete), `OrderNotificationService` (place order from catalog items, dispatch, cancel, resend, dispose content, reconcile, list; every provider call wrapped so a send failure never fails the operation).
5. **PublicApi endpoints** (all under `/api/`, JWT): contact-numbers POST/GET/DELETE; orders POST, dispatch, cancel; my-orders GET; orders/{id}/notifications GET; notifications/{id}/resend POST, notifications/{id}/content DELETE, notifications/reconciliation GET. Operator (admin-role) endpoints: dispatch, cancel, resend, content DELETE, reconciliation.
6. **Secrets**: load env vars into user-secrets for PublicApi; `Twilio:` section bound from config.
7. **Self-verify** end-to-end; write user verification guide.

**Operation → SDK mapping** (all on `client.Api20100401Message` / lookups):
- Register contact number → validate+canonicalize via `LookupsV2PhoneNumber.FetchPhoneNumber3` (`valid`, `phone_number`).
- Order placed / dispatched / cancelled notification (immediate) → `Api20100401Message.CreateMessage` with `from` = `Twilio:FromNumber`.
- Delivery follow-up (queued days later, provider-side) → `CreateMessage` with `messagingServiceSid` + `scheduleType=fixed` + `sendAt` (Messaging-Service-only scheduling; cannot use `from`).
- Cancel not-yet-sent follow-up → `UpdateMessage(status=canceled)`.
- Refresh delivery outcome for reporting → `FetchMessage` (status/error_code/date_sent).
- Resend → `CreateMessage` (new message, from = FromNumber), deduped by caller idempotency key.
- Dispose content → `UpdateMessage(body="")` (redaction: text gone at provider, record/status survives — **not** `DeleteMessage`, which destroys the whole record).
- Reconciliation → `ListMessage(from = FromNumber, DateSent> = from, DateSent< = to)`, paged to completion.

---

## 2. CONTRACT SHEET

> ⚠ Signatures below are **generated code, verbatim** — every parameter name is the literal C# identifier; in named
> arguments use them exactly (the cancellation-token parameter is literally `ct`, so write `ct:`).
> ⚠ Every SDK type is written **fully-qualified** with the namespace its source path implies (`Models/` → `TwilioSdk.Models`,
> `Models/Enums/` → `TwilioSdk.Models.Enums`, root → `TwilioSdk`, `Api/` controllers → `TwilioSdk.Api`,
> `Core/Configuration/` → `TwilioSdk.Core.Configuration`), taken from THAT type's own path.

Client: `new TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)`. DI: `services.AddTwilioSdkClient(Action<TwilioSdkClientOptions>?)` (registers the client as a **singleton**, built once). Auth: `options.AccountSidAuthToken = new TwilioSdk.BasicAuthCredentials { Username = <AccountSid or API key>, Password = <AuthToken or key secret> }`. Server override: `options.Server.Default.Production.BaseUrl` (all Message ops are on server group **`Default`** = `https://api.twilio.com`; the lookup is on `Default4` and is **not** overridden by `Twilio:BaseUrl`).

| operation | signature (verbatim) · request fields · response fields read · error · source |
| --- | --- |
| `client.Api20100401Message.CreateMessage` | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)`. The 24 middle params are nullable-no-default → **must pass explicitly** (`null` to skip). Reads on `TwilioSdk.Models.ApiV2010AccountMessage`: `Sid`, `Status`, `From`, `To`, `Body`, `ErrorCode`, `ErrorMessage`, `DateSent`, `DateCreated`. **Case B** (`SdkException<RawError>`). Source: `map/operations/Api20100401Message.md`, `Models/ApiV2010AccountMessage.cs`. |
| `client.Api20100401Message.FetchMessage` | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `ApiV2010AccountMessage`. **Case B**. Source: same page. |
| `client.Api20100401Message.UpdateMessage` | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `ApiV2010AccountMessage`. `body`/`status` nullable-no-default → **pass explicitly**. Used for **redaction** (`body=""`, status `null`) and **cancel** (`status=MessageEnumUpdateStatus.Canceled`, body `null`). Method `<remarks>`: "used to redact Message body text and to cancel not-yet-sent messages". **Case B**. Source: same page, `Api/Api20100401Message.cs:237`. |
| `client.Api20100401Message.ListMessage` | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `TwilioSdk.Models.ListMessageResponse`. Query wire: `From←from`, `DateSent<←dateSentQuery`, `DateSent>←dateSentQueryQuery`, `PageSize←pageSize`, `Page←page`, `PageToken←pageToken`. Reads: `Messages: IReadOnlyList<ApiV2010AccountMessage>?`, `NextPageUri`, `Page`. **Case B**. Source: same page, `Models/ListMessageResponse.cs`. |
| `client.LookupsV2PhoneNumber.FetchPhoneNumber3` | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, … 13 more nullable-no-default …, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `TwilioSdk.Models.LookupResponse` (pass all optional query params `null`). **Server group `Default4`** (`https://lookups.twilio.com`; not governed by `Twilio:BaseUrl`). Reads: `Valid: bool?`, `PhoneNumber: string?` (E.164 canonical), `ValidationErrors`. **Case B**. Source: `map/operations/LookupsV2PhoneNumber.md`, `Models/LookupResponse.cs`. |

Enum tables (only what's used):

| enum (`TwilioSdk.Models.Enums`) | members used · wire · source |
| --- | --- |
| `MessageEnumScheduleType` | `Fixed` (`"fixed"`). Doc: "For Messaging Services only … in conjunction with `send_time` to schedule". Source `Models/Enums/MessageEnumScheduleType.cs`. |
| `MessageEnumUpdateStatus` | `Canceled` (`"canceled"`) — only member. Source `Models/Enums/MessageEnumUpdateStatus.cs`. |
| `MessageEnumStatus` (on `ApiV2010AccountMessage.Status`) | `Queued`,`Sending`,`Sent`,`Accepted`,`Scheduled`,`Receiving` = **not-yet/in-flight**; `Delivered`,`Received`,`Read` = **done**; `Failed`,`Undelivered`,`Canceled` = **failed/terminal-bad**; `PartiallyDelivered` = **failed** (treat as not-fully-delivered). Absent/unreadable → **not-yet** (never success). Source `Models/Enums/MessageEnumStatus.cs`. |

`Twilio:AccountSid` is the value passed as the `accountSid` path parameter on every Message operation, **and** the Basic-auth username (with `Twilio:AuthToken` as password) — this SDK has no separate account-context; the account SID is an explicit method argument.

### CROSS-OPERATION INVARIANTS

| invariant | operations | enforced where |
| --- | --- | --- |
| A destination we send to must be one the provider marked `valid` and stored in its canonical E.164 form. | `CreateMessage` ← `FetchPhoneNumber3` | implementation — `ContactNumberService` rejects on `Valid != true`, stores `LookupResponse.PhoneNumber`; sends only ever read a stored `ContactNumber`. |
| A follow-up cancelled via `UpdateMessage` must be a `Sid` a prior `CreateMessage` (the scheduled follow-up) returned. | `UpdateMessage` ← `CreateMessage` | implementation — cancel reads the order's `DeliveryFollowUp` `OrderNotification.ProviderMessageSid`. |
| A message redacted/refetched must be a `Sid` a prior `CreateMessage` returned and that this app owns. | `UpdateMessage`/`FetchMessage` ← `CreateMessage` | implementation — operator/shopper endpoints resolve `OrderNotification` by id first. |
| Reconciliation counts only messages whose `From` = `Twilio:FromNumber`. | `ListMessage` ← config | implementation — `from:` arg = configured FromNumber; not post-filtered. |

---

## 3. Trap notes (hazard + skill pointer; not resolved here)

- Client is a **singleton built once** by `AddTwilioSdkClient`; how `HttpClient` lifetime / handler reuse must be arranged, and whether the wrapper may be transient — **MUST load twilio-platforms-team:dotnet-client-initialization**.
- Setting Basic-auth credentials on the options object and how a never-set credential silently skips (so a config typo looks like a bad key, not a missing one) — **MUST load twilio-platforms-team:dotnet-authentication**.
- The 24 nullable-no-default params on `CreateMessage` mis-bind in a positional call; use **named arguments**. The injected `Idempotency-Key` header is not a real key — **MUST load twilio-platforms-team:dotnet-calling-endpoints**.
- `MessageEnumStatus`/`MessageEnumUpdateStatus` are `StringEnum<T>` not C# enums; `AdditionalProperties` extension data; reading `Status` — **MUST load twilio-platforms-team:dotnet-models**.
- Every Message op is **Case B** (`SdkException<RawError>`); plus a drifted 2xx body / non-2xx-shape mismatch surfaces as `System.Text.Json.JsonException`, not `SdkException` — the error boundary in `TwilioSmsGateway` must catch both — **MUST load twilio-platforms-team:dotnet-error-handling**.
- `Timeout` is **per-attempt** not total; `HttpMethodsToRetry` default excludes `POST` (so `CreateMessage`/`UpdateMessage` are never auto-resent); `LogRequestBody` logs JSON **unredacted** and `TWILIOCLIENT_LOG` can arm body logging unless `LoggerFactory` is set — and `Twilio:BaseUrl` must map to `options.Server.Default.Production.BaseUrl` — **MUST load twilio-platforms-team:dotnet-configuration-resilience**.
- Faking the SDK seam for tests is via the `HttpClient` ctor arg — **MUST load twilio-platforms-team:dotnet-testing**.

---

## 4. REQUIRED READING (load all before implementation; contents deliberately not carried here)

- `twilio-platforms-team:dotnet-client-initialization` — step 3 (client/DI construction).
- `twilio-platforms-team:dotnet-authentication` — step 3 (Basic-auth credentials).
- `twilio-platforms-team:dotnet-calling-endpoints` — step 4 (all Message/Lookup calls, named args, idempotency header).
- `twilio-platforms-team:dotnet-models` — step 4 (StringEnum, reading Status, models).
- `twilio-platforms-team:dotnet-error-handling` — step 3/4 (error boundary; Case B + JsonException from BOTH a drifted 2xx body and a non-2xx shape mismatch — SDK-only catch ladder lets these escape).
- `twilio-platforms-team:dotnet-configuration-resilience` — step 3 (timeout/retry/base-URL/logging/pagination).
- `twilio-platforms-team:dotnet-testing` — tests (HttpClient seam).

Skill names are shared across every APIMatic .NET plugin — these are the **twilio-platforms-team** copies.

---

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `TwilioOptions` bound from `Twilio:`; four per-part `.Validate(...)` predicates + `.ValidateOnStart()` throw at host start if `AccountSid`, `AuthToken`, `FromNumber`, or `MessagingServiceSid` is null/blank — **each part checked individually** (a blank part ≠ a missing one), message names the key, never echoes the value. `BaseUrl` optional. (where: `Infrastructure/Twilio/TwilioServiceCollectionExtensions.cs` `AddTwilioSms`; `Infrastructure/Twilio/TwilioOptions.cs`) |
| 2 | Secret sourcing & rotation | Secrets come from **.NET user-secrets** (loaded from env vars by me at setup), overlaid by env in prod; never in repo files. `AddTwilioSdkClient` builds the options+credentials **once at registration** into the singleton → a rotated `AuthToken` takes effect only on process restart. Restart-to-rotate is acceptable for this app; documented, no hot-reload. (where: `TwilioServiceCollectionExtensions.AddTwilioSms` → `AddTwilioSdkClient(options => ...)` callback) |
| 3 | Total timeout budget | The SDK `Timeout` is per-attempt; the whole-call bound is a `CancellationToken` deadline. Each gateway call passes a `CancellationTokenSource(TimeSpan)` linked to the request `ct` so a hung retryable call cannot exceed the budget. Budget set in `TwilioSmsGateway`. (where: `Infrastructure/Twilio/TwilioSmsGateway.cs` `Bounded<T>` + `CallBudget = 30s`, linked CTS `CancelAfter`) |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = `GET,HEAD,PUT,OPTIONS`; our writes are `POST` (`CreateMessage`) and `POST` (`UpdateMessage`) → **never auto-resent by the SDK**. Reads (`FetchMessage`,`ListMessage`,`FetchPhoneNumber3`) are `GET` → retried. We keep the default; no write is made SDK-retryable. (where: `TwilioServiceCollectionExtensions` — `RetryOptions.Default() with { Timeout = 15s }`, `HttpMethodsToRetry` left at default) |
| 5 | Idempotency & ambiguous writes | `CreateMessage` takes **no** real caller key (the injected `Idempotency-Key` header is per-call GUID — not one). Resend carries a **caller-supplied idempotency key** persisted on `OrderNotification.IdempotencyKey` with a **unique index**; a repeat key is rejected by the constraint (caught) → the first result is returned, no second send. Placed/dispatch/cancel sends have no key → reconciliation is the recovery path. (where: `OrderNotificationService.ResendAsync`; unique index in `Data/Config/OrderNotificationConfiguration.cs`) |
| 6 | Observability | `IAppLogger<T>` logs at Info (transitions), Warning (send failed but operation succeeded), Error (unexpected). Provider error `RawError.ReadAsString()`/status logged on failure. `LogRequestBody` stays **off**. **Phone numbers / message bodies are never logged** — logs carry `OrderNotification.Id` and `ProviderMessageSid` only. (where: `TwilioSmsGateway` `Bounded` catch logs status only; `OrderNotificationService`/`ContactNumberService` log ids only) |
| 7 | Sensitive data | Request models carry the recipient **phone number** (`to`) and **message body** — both sensitive. Therefore `LogRequestBody` is left off **and** `options.Logging.LoggerFactory` is set explicitly in DI so `TWILIOCLIENT_LOG` cannot arm body logging from outside code. Our own diagnostics never echo `to`/`body`. (where: `LogRequestBody` untouched (default off) in `AddTwilioSdkClient`; that extension sets `Logging.LoggerFactory` from the DI `ILoggerFactory`, disarming `TWILIOCLIENT_LOG`) |
| 8 | Environment selection | One live account. Message ops use server group `Default` (`api.twilio.com`); `Twilio:BaseUrl`, when set, overrides `options.Server.Default.Production.BaseUrl` for **all** Message calls. Lookup uses `Default4` (`lookups.twilio.com`), unaffected. SDK declares no sandbox env; test traffic is kept off the live system by faking the `HttpClient` seam in unit tests and by only ever sending to the two configured verification numbers at runtime. (where: base-URL override in `TwilioServiceCollectionExtensions`; Lookup group in `TwilioSmsGateway.ValidateNumberAsync`; gateway tests `tests/IntegrationTests/Twilio/TwilioSmsGatewayTests.cs` (HttpClient stub seam)) |
| 9 | Duplicate prevention under concurrency | Store `OrderNotifications`, column `IdempotencyKey`, **unique index** (filtered, non-null). The second concurrent resend under the same key fails the insert with `DbUpdateException` → caught → first result returned. (SQL Server enforces it; the dev **in-memory provider does not enforce unique indexes**, so a supplementary same-request existence read makes dedup observable in the in-memory harness — the constraint remains the authority.) (where: `Data/Config/OrderNotificationConfiguration.cs` unique filtered index; `OrderNotificationService.ResendAsync` pre-check `priorByKey` + insert-then-catch) |
| 10 | Partial results | Reconciliation's `ListMessage` is page-based (no SDK `Pageable`). We loop on `NextPageUri` until null so the **whole range** is covered; the report DTO carries a `Truncated` bool + `PagesFetched` so a caller-imposed page cap (if ever hit) is visible in the return type, not only a log. Default: no cap, `Truncated=false`. (where: `TwilioSmsGateway.ListSentAsync` loop + `MaxPages`; `ReconciliationReport.Truncated`/`PagesFetched`) |
| 11 | Startup validation vs test host | Host-booting test projects: **PublicApiIntegrationTests** (`WebApplicationFactory<Program>`) and **FunctionalTests** (`TestApiApplication : WebApplicationFactory<AuthenticateEndpoint>`). Both boot `Program`, which now runs `ValidateOnStart`. Given **placeholder** (obviously-fake, non-secret) `Twilio:` values: PublicApiIntegrationTests via its `appsettings.test.json`; FunctionalTests via added in-memory config in the fixture. Both projects run and green after the change (verified: PublicApiIntegrationTests 15/15, FunctionalTests 12/12). (where: `tests/PublicApiIntegrationTests/appsettings.test.json`; `tests/FunctionalTests/PublicApi/ApiTestFixture.cs` `ConfigureAppConfiguration`) |
| 12 | Ordering & no-op side effects | The local `OrderNotification` row is written (status pending, no SID) **before** the provider call and updated with SID/status **after** it returns. Order transitions (dispatch/cancel) are **idempotent**: the outbound notification + follow-up scheduling/cancellation are **gated on the order status actually changing** (Placed→Dispatched, →Cancelled); a repeat call on an already-dispatched/cancelled order sends nothing. (where: `OrderNotificationService.NotifyAsync` (row before call); `DispatchAsync`/`CancelAsync` gate sends on `Order.MarkDispatched`/`MarkCancelled`) |
| 13 | Unknown outcomes | If `CreateMessage` transport fails after the request may have been received, the catch does **not** assert a definite failure: the pre-written `OrderNotification` row (status pending, its `Id`) survives, and reconciliation (`ListMessage` by `From`=FromNumber over the window) re-reads what the provider actually has, matched by `Sid`. For resend, the same idempotency-key row is the reference. (where: `TwilioSmsGateway.Bounded` catch (no false failure); `OrderNotificationService.ReconcileAsync`/`GetAndRefreshNotificationsAsync` re-read) |
| 14 | Provider status & reconciliation clock | `CreateMessage`/`FetchMessage`/`UpdateMessage` return `Status`. Code sorts it into done / not-yet / failed (enum table §2) — never coalesces absent→success. GET endpoints re-`FetchMessage` to refresh. Reconciliation clock = the provider's **`date_sent`**: both sides filter on it (`ListMessage` `DateSent>`/`DateSent<`; local rows filtered by the persisted `ProviderDateSent`, **not** the local `CreatedAt`). (where: `DeliveryOutcomeMapper`; `GetAndRefreshNotificationsAsync` re-fetch; `NotificationsByProviderDateSentSpecification` + `ReconcileAsync`) |

### DUPLICATE CLAIMS

| write | where the claim is stored | what rejects the second one | where that rejection is caught | where in the code |
| --- | --- | --- | --- | --- |
| resend a notification | `OrderNotifications.IdempotencyKey` column (unique filtered index — `Data/Config/OrderNotificationConfiguration.cs`) | the unique index rejects the duplicate insert | `OrderNotificationService.ResendAsync` `try { AddAsync } catch { re-read by NotificationByIdempotencyKeySpecification; return winner }` (`ApplicationCore/Services/OrderNotificationService.cs`) |

### PAGED READS

| read | what caps it | how the caller learns the answer was cut short | where in the code |
| --- | --- | --- | --- |
| reconciliation `ListMessage` loop | `MaxPages`=50 backstop; otherwise loops `NextPageUri` to null | `ProviderMessageList.Truncated` + `PagesFetched` → surfaced on `ReconciliationReport.Truncated`/`PagesFetched` in the returned DTO | `TwilioSmsGateway.ListSentAsync` (`Infrastructure/Twilio/TwilioSmsGateway.cs`); report built in `OrderNotificationService.ReconcileAsync` |

### REPEATED OPERATIONS

| operation | what tells you the state actually changed | the effects gated on that | where in the code |
| --- | --- | --- | --- |
| dispatch order | `Order.MarkDispatched()` returns true (was `Placed`) | the "on its way" send + follow-up scheduling | `Order.MarkDispatched` (`ApplicationCore/Entities/OrderAggregate/Order.cs`); gated in `OrderNotificationService.DispatchAsync` (returns false → 409, no sends) |
| cancel order | `Order.MarkCancelled()` returns true (was not `Cancelled`) | the "cancelled" send + follow-up cancellation | `Order.MarkCancelled`; gated in `OrderNotificationService.CancelAsync` (returns false → 409, no sends/cancels) |
| resend notification | no prior row for the idempotency key (`priorByKey == null`) | the new `CreateMessage` send | `OrderNotificationService.ResendAsync` (early-return on `priorByKey`) |

### UNKNOWN OUTCOMES

| write | the operation you re-read with | the reference you search by | where in the code |
| --- | --- | --- | --- |
| `CreateMessage` (any notification) | `ListMessage` (reconciliation) / `FetchMessage` | `From`=FromNumber + `date_sent` window, matched by `Sid`; pre-written row `Id` | send failure caught in `OrderNotificationService.NotifyAsync` (records `SendFailed`, not a definite success); provider re-read via `OrderNotificationService.ReconcileAsync` + `GetAndRefreshNotificationsAsync` |
| resend `CreateMessage` | re-read `OrderNotifications` by key | `IdempotencyKey` | `OrderNotificationService.ResendAsync` catch block re-reads by `NotificationByIdempotencyKeySpecification` |

### OPERATION OUTCOMES

| write | the status field | every value it can hold, and what the app does with each | where in the code |
| --- | --- | --- | --- |
| `CreateMessage` / `FetchMessage` / `UpdateMessage` | `ApiV2010AccountMessage.Status` (`MessageEnumStatus`) | done = `delivered`/`received`/`read` (report delivered); not-yet = `queued`/`sending`/`sent`/`accepted`/`scheduled`/`receiving`/absent/unreadable (report pending, refresh later); failed = `failed`/`undelivered`/`canceled`/`partially_delivered` (report failed; eligible for operator resend) | `DeliveryOutcomeMapper.FromProviderStatus` (`ApplicationCore/Notifications/DeliveryOutcome.cs`), applied in `OrderNotificationService.ToView`/`OutcomeOf` and used to gate refresh in `GetAndRefreshNotificationsAsync` |

### WRITE ORDER

| write | what exists locally BEFORE the call | what is written after it returns | where in the code |
| --- | --- | --- | --- |
| any notification send (`CreateMessage`) | `OrderNotification` row: OrderId, OwnerId, Kind, To, Body, no SID | `RecordSent(...)` (SID/status/date) or `RecordSendFailure(...)`, then `UpdateAsync` | `OrderNotificationService.NotifyAsync` — `AddAsync` before the gateway call, `RecordSent`/`RecordSendFailure` + `UpdateAsync` after |
| resend send | `OrderNotification` row with `IdempotencyKey` (unique), no SID | `RecordSent`/`RecordSendFailure` after `CreateMessage` returns | `OrderNotificationService.ResendAsync` — `AddAsync` (claim) before the gateway call, `RecordSent`/`RecordSendFailure` + `UpdateAsync` after |

---

## 6. Assumptions & Blockers

**Blockers:** none — the plugin exposes every capability the integration needs (send, schedule via Messaging Service, cancel scheduled, fetch status, list-by-From for reconciliation, redact body, lookup/validate).

**Assumptions (design decisions, proceeding):**
- The delivery follow-up must be scheduled provider-side; Twilio scheduling is **Messaging-Service-only** (enum doc), so the follow-up is sent via `Twilio:MessagingServiceSid` (not `from`). For it to be counted by a `From`=FromNumber reconciliation, `Twilio:FromNumber` is assumed to be a sender in that Messaging Service (the config supplies both together). Immediate messages use `from`=FromNumber directly. (A follow-up cancelled before sending has no `date_sent`, so it is absent from both sides of a `date_sent`-range reconciliation — consistent, no false discrepancy.)
- Follow-up delay = **3 days** after dispatch (within Twilio's 15-min…7-day window; "a few days later").
- `POST /api/orders` builds a real `Order`/`OrderItem`/`CatalogItemOrdered` (the existing model) from `{catalogItemId, quantity}` pairs; `Order` requires a non-null `ShipToAddress`, and the API carries none, so a placeholder address is used (address is out of this feature's scope).
- `notificationId`/`orderId`/`contactNumberId` are the entities' int `Id` (consistent with `Order`/`CatalogItem`); shopper endpoints scope by owner, operator endpoints are admin-role.
- Verification runs against the **in-memory** provider (single process/run) per the environment note; unique-index enforcement and cross-restart persistence are SQL-Server-only, as designed.
