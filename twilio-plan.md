# Twilio SMS order-notifications — integration plan (eShopOnWeb / `src/PublicApi`)

## 1. Scope & sequence

Additive SMS notifications for eShopOnWeb, Twilio as provider, exposed on `src/PublicApi`
(JWT). Domain types & interfaces in `ApplicationCore`; SDK-facing gateway + EF config +
services in `Infrastructure`; endpoints + DTOs + DI + config binding in `PublicApi`.
`ApplicationCore` stays SDK-free (gateway translates SDK ⇄ domain).

SDK referencing: SDK is not on NuGet. It is packed (`dotnet pack` in the temp clone) to
`TwilioSdk.1.0.0.nupkg`, vendored under `lib/twilio-sdk/`, exposed via a repo `nuget.config`
local source + a central `PackageVersion`. Consumed by `Infrastructure` (+ `PublicApi` for DI).

Build order:
1. Vendor nupkg + `nuget.config` + `Directory.Packages.props` entry.
2. `ApplicationCore`: entities (`ContactNumber`, `OrderNotification`, `NotificationResendClaim`),
   `OrderStatus` + `Order.Dispatch()/Cancel()`, enums, interfaces
   (`ITwilioMessagingGateway`, `IPhoneNumberValidator`, `IOrderNotificationService`), specs.
3. `Infrastructure`: `TwilioMessagingGateway` (SDK calls — CreateMessage/Fetch/List/Update),
   `TwilioPhoneNumberValidator` (LookupsV2), `OrderNotificationService`, EF `DbSet`s + configs,
   `TwilioClient` DI (`AddTwilioMessaging`) with fail-fast + `TwilioSettings`.
4. `PublicApi`: endpoints under `/api/`, DTOs, register services, bind `Twilio:` section.
5. Tests: gateway unit tests (SDK seam faked) + endpoint smoke; placeholder `Twilio:` config in
   host-booting test projects.

Twilio operations used (all on `client.Api20100401Message` except validation):
- `CreateMessage` — send placed/dispatched/cancelled notices (immediate, `from`=FromNumber) and
  the dispatch follow-up (scheduled: `messagingServiceSid` + `scheduleType=Fixed` + `sendAt`).
- `FetchMessage` — refresh a message's delivery status/outcome.
- `ListMessage` — reconciliation, filtered `From`=FromNumber + DateSent range (paged).
- `UpdateMessage` — cancel a not-yet-sent follow-up (`status=Canceled`); redact body (`body=""`).
- `LookupsV2PhoneNumber.FetchPhoneNumber3` — validate a number + return canonical E.164.

## 2. CONTRACT SHEET

> ⚠ Signatures below are **generated code, verbatim** — every parameter name is the literal C#
> identifier; named args use those exact names (cancellation token is `ct:`).
> ⚠ Every SDK type is written **fully-qualified** with the namespace its source path implies
> (`Models/` → `TwilioSdk.Models`; `Models/Enums/` → `TwilioSdk.Models.Enums`;
> `Api/` → `TwilioSdk.Api`; root → `TwilioSdk`; `Core/…` per file).

Client construction (source: `sdk-map.md` "Getting a client", `TwilioSdkClientOptions.cs`):
- Ctor: `new TwilioSdk.TwilioSdkClient(HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)`.
- Auth: `options.AccountSidAuthToken = new BasicAuthCredentials { Username = <Twilio:AccountSid>, Password = <Twilio:AuthToken> }`. (`BasicAuthCredentials` — `TwilioSdk`, per map auth block.)
- `options.Environment = TwilioSdk.Servers.ServerEnvironment.Production`.
- Messaging base-URL override: when `Twilio:BaseUrl` set →
  `options.Server.Default.Production.BaseUrl = <Twilio:BaseUrl>` (Message ops run on server group
  **`Default`** — verified: `Api/Api20100401Message.cs` uses `_server.Default("…")`). Lookup runs
  on **`Default4`** (`LookupsV2PhoneNumber.md`), a different override point, so BaseUrl does NOT
  govern it. Source: `sdk-map.md` Servers table + the two Api source files.

| op | controller · signature | request fields (we pass) | response fields we read | error | pagination | source |
| --- | --- | --- | --- | --- | --- | --- |
| CreateMessage | `client.Api20100401Message.CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions=null, CancellationToken ct=default)` | `accountSid`=Twilio:AccountSid; `to`=canonical dest; `body`=text; **immediate**: `from`=Twilio:FromNumber, `scheduleType`=null,`sendAt`=null,`messagingServiceSid`=null; **scheduled follow-up**: `messagingServiceSid`=Twilio:MessagingServiceSid, `scheduleType`=`MessageEnumScheduleType.Fixed`, `sendAt`=now+3d, `from`=null. ALL other 20 optionals → **`null`** (omit → provider default). | `Sid`, `Status` (`MessageEnumStatus?`), `To`, `From`, `Body`, `ErrorCode` (`int?`), `ErrorMessage`, `DateSent` (string RFC2822), `DateCreated` | `SdkException<RawError>` (B) | none | `map/operations/Api20100401Message.md`; `Models/ApiV2010AccountMessage.cs`; remarks `Api/Api20100401Message.cs` |
| FetchMessage | `client.Api20100401Message.FetchMessage(string accountSid, string sid, RequestOptions?=null, CancellationToken ct=default)` | `accountSid`, `sid` | same as above (`Status`,`ErrorCode`,`ErrorMessage`,`DateSent`,`Body`) | B | none | `Api20100401Message.md`; `ApiV2010AccountMessage.cs` |
| ListMessage | `client.Api20100401Message.ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions?=null, CancellationToken ct=default)` | `accountSid`; `from`=Twilio:FromNumber (wire `From`); `dateSentQueryQuery`=range-from (wire `DateSent>`); `dateSentQuery`=range-to (wire `DateSent<`); `pageSize`=1000; `page` walked 0,1,…; `to`/`dateSent`/`pageToken`=null | `Messages: IReadOnlyList<ApiV2010AccountMessage>?`, `NextPageUri` (string?) → loop control | B | **manual**: no Pageable; loop `page` while `NextPageUri != null` | `Api20100401Message.md`; `Models/ListMessageResponse.cs` |
| UpdateMessage | `client.Api20100401Message.UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions?=null, CancellationToken ct=default)` | **cancel follow-up**: `body`=null, `status`=`MessageEnumUpdateStatus.Canceled`. **redact/dispose content**: `body`=`""` (empty→redacts), `status`=null | `Status`, `Body` (empty after redact) | B | none | `Api20100401Message.md` (remarks: "redact Message body / cancel not-yet-sent"); `Models/Enums/MessageEnumUpdateStatus.cs` |
| DeleteMessage | `client.Api20100401Message.DeleteMessage(string accountSid, string sid, RequestOptions?=null, CancellationToken ct=default)` → `void` | `accountSid`,`sid` | — | B | none | `Api20100401Message.md` |
| Validate# | `client.LookupsV2PhoneNumber.FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions?=null, CancellationToken ct=default)` | `phoneNumber`=caller input; **all 15 optionals → `null`** (omit → default; no add-on data packages needed for basic validity) | `Valid` (`bool?`), `PhoneNumber` (canonical E.164), `ValidationErrors` | B | none | `LookupsV2PhoneNumber.md`; `Models/LookupResponse.cs` |

Enums (source `Models/Enums/…`): `MessageEnumScheduleType.Fixed` ("fixed");
`MessageEnumUpdateStatus.Canceled` ("canceled"); `MessageEnumStatus` members incl.
`Sent/Delivered/Failed/Undelivered/Scheduled/Queued/Accepted/Canceled` — read `.Value`
(`StringEnum<T>`) to persist the wire string; build with static members / `FromValue("…")`.

`DeleteMessage` is **not** used for content disposal (it removes the whole record, losing "what
became of it"); disposal uses `UpdateMessage(body="")` which redacts text yet keeps the record.

### CROSS-OPERATION INVARIANTS

| invariant | operations | enforced where |
| --- | --- | --- |
| A `to` passed to CreateMessage must be a canonical number produced by a prior successful validation (`FetchPhoneNumber3.PhoneNumber`) that was stored as a `ContactNumber` owned by the buyer. | `CreateMessage` ← `FetchPhoneNumber3` | implementation (register stores only canonical `Valid==true` numbers; sends read owned `ContactNumber` rows) |
| A `sid`/`Status`/`ErrorCode`/`Body` acted on by Update/Fetch/reconcile must be one persisted from a prior `CreateMessage` response on an `OrderNotification` the caller owns (order-scoped) / operator-gated. | `UpdateMessage`,`FetchMessage`,`ListMessage` ← `CreateMessage` | implementation (`OrderNotification.ProviderMessageSid`) |
| Reconciliation counts only `From`==`Twilio:FromNumber`; asked of the provider via the `from` query param, not filtered after. | `ListMessage` ← config | implementation |

## 3. Trap notes (name hazard + skill; do NOT resolve inline)

- Client/HttpClient lifetime & DI registration shape (long-lived handler vs transient wrapper) — **MUST load `twilio-platforms-team:dotnet-client-initialization`**.
- Basic-auth credential wiring: set-before-construct, secret from config not literal — **MUST load `twilio-platforms-team:dotnet-authentication`**.
- Optional params with no C# default mis-bind positionally; named-arg every call; the injected `Idempotency-Key` header is NOT a real key — **MUST load `twilio-platforms-team:dotnet-calling-endpoints`**.
- `StringEnum<T>` is not a C# enum; reading `.Value`, building via factory; `AdditionalProperties` extension data — **MUST load `twilio-platforms-team:dotnet-models`**.
- Case-B `SdkException<RawError>` mechanics AND the two `JsonException` escape directions (see REQUIRED READING) — **MUST load `twilio-platforms-team:dotnet-error-handling`**.
- `Timeout` is per-attempt not total; `HttpMethodsToRetry` gates POST resend; `LogRequestBody` logs JSON unredacted; base-URL/server override; manual pagination — **MUST load `twilio-platforms-team:dotnet-configuration-resilience`**.
- The `HttpClient` ctor arg is the test seam; match project framework — **MUST load `twilio-platforms-team:dotnet-testing`**.

## 4. REQUIRED READING (load before implementation; contents deliberately not copied here)

- `twilio-platforms-team:dotnet-client-initialization` — client & DI setup.
- `twilio-platforms-team:dotnet-authentication` — Basic-auth credential wiring.
- `twilio-platforms-team:dotnet-calling-endpoints` — every SDK operation call.
- `twilio-platforms-team:dotnet-models` — enums, response models, extension data.
- `twilio-platforms-team:dotnet-error-handling` — every try/catch + error boundary.
- `twilio-platforms-team:dotnet-configuration-resilience` — retries/timeouts/base-URL/logging/pagination.
- `twilio-platforms-team:dotnet-testing` — the SDK test seam.

Mandatory hazard rows (both apply — `System.Text.Json.JsonException` reaches the boundary from
two directions, needing opposite handling):
- A drifted/malformed **2xx** body (missing `required` member) surfaces as `JsonException` from
  deserialization, **not** `SdkException` — an SDK-exception-only catch ladder lets it escape.
- A **non-2xx** body not matching its operation's generated error shape throws `JsonException`
  *while the error object is constructed*, **replacing** the `SdkException` and destroying the
  HTTP status. (All our ops are Case B/`RawError`, but the boundary still catches `JsonException`.)

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `TwilioSettings` bound from `Twilio:` in `AddTwilioMessaging`. A validator throws at startup if `AccountSid`, `AuthToken`, `FromNumber`, or `MessagingServiceSid` is null/whitespace (each part checked separately — blank ≠ missing). `BaseUrl` optional. Host refuses to start (validate-on-start), not a first-call 401. |
| 2 | Secret sourcing & rotation | Secrets from **.NET user-secrets** (dev) / env-mapped `Twilio:` config (prod) — never in repo files. Options built once at registration and captured in the singleton client → rotating `AuthToken` needs a process restart (documented; acceptable for this app). |
| 3 | Total timeout budget | SDK `Timeout` is **per-attempt**. Every SDK call is bounded by a caller `CancellationToken` with a total deadline (`TimeSpan` from settings, default 30s) created in the gateway via `CancellationTokenSource`, linked to the request-aborted token — that bounds the whole call incl. retries. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS. Our writes are **POST** (CreateMessage, UpdateMessage) and **DELETE** — never auto-resent by the SDK. GET reads (Fetch/List) may retry. We keep the default (do not add POST), so a send is issued at most once per gateway call. |
| 5 | Idempotency & ambiguous writes | CreateMessage takes **no** real caller idempotency key (injected `Idempotency-Key` header is per-call GUID — not one). Operator **resend** carries a caller-supplied key enforced by us (see DUPLICATE CLAIMS). For the send itself: no key → reconciliation path (`ListMessage` by From+DateSent) is the dedupe/repair mechanism. |
| 6 | Observability | Structured logs at Info (op placed/dispatched/cancelled, message queued/scheduled/cancelled with **Sid + status only**), Warning (send failed, provider error incl. `RawError.StatusCode` + `ReadAsString()` correlation body), Debug (reconciliation counts). **Phone numbers, message bodies, and the auth token are never logged.** |
| 7 | Sensitive data | Request bodies carry the destination number + message text. `options.Logging.LogRequestBody` stays **off** and `options.Logging.LoggerFactory` is set explicitly (to the app's factory) so `TWILIOCLIENT_LOG` cannot switch body logging on from outside code. Our own logs never echo `To`/`Body`. |
| 8 | Environment selection | One live Production account. Messaging (`Default` → api.twilio.com) optionally overridden by `Twilio:BaseUrl`; Lookup on `Default4` (lookups.twilio.com) unaffected. No sandbox env in SDK → test host projects get **placeholder** `Twilio:` config and never call the API (no endpoint under test hits Twilio); real traffic only from an operator-run instance with real secrets. |
| 9 | Duplicate prevention under concurrency | Resend idempotency: entity `NotificationResendClaim` with **primary key = `IdempotencyKey` (string)**. Insert-first; the PK uniqueness rejects the second row (enforced by SqlServer AND the EF InMemory provider's keyed store); the resend endpoint **catches** that rejection (`DbUpdateException`) and returns the first attempt's result. No existence-check, no in-process lock. |
| 10 | Partial results | Reconciliation walks `ListMessage` pages until `NextPageUri==null` or a page cap (`MaxReconciliationPages`, default 50). If the cap is hit the response carries `Truncated=true` and `PagesRead` — the caller learns via those fields, not a log. |
| 11 | Startup validation vs test host | Both host-booting projects — `tests/PublicApiIntegrationTests` (`WebApplicationFactory<Program>`) and `tests/FunctionalTests` (`WebApplicationFactory<AuthenticateEndpoint>`) — get **placeholder** `Twilio:` values (fake, non-secret) via each project's `appsettings.test.json` (copied to output; `Program` loads it). Both test suites are run and must be green after the fail-fast is added. |
| 12 | Ordering & no-op side effects | For each message: the `OrderNotification` row is inserted (status `Pending`, no Sid) **before** CreateMessage, then completed with Sid+status after. Dispatch/cancel are gated on `Order.Dispatch()/Cancel()` **returning that the status actually changed** — a repeat (already dispatched/cancelled) sends nothing and schedules/cancels nothing. Provider response is never the first local write. |
| 13 | Unknown outcomes | CreateMessage transport failure after the request may have landed: not auto-retried (POST). The notification row is marked `SendFailed` locally, and the **re-read** is `ListMessage` searched by **`To` + `DateSent` range (+`From`)** (no Twilio-side key exists) — surfaced through the reconciliation report, not reported as a definite non-send. |
| 14 | Provider status & reconciliation clocks | CreateMessage/Fetch return `Status` (`MessageEnumStatus`). Code branches: `Failed`/`Undelivered` → notification `Failed` (resend-eligible); `Scheduled` → follow-up pending (cancelable); `Sent`/`Delivered`/`Queued`/`Accepted` → in-flight/ok. No `?? "completed"` defaulting. Reconciliation: both sides filter on **`date_sent`** — Twilio via the `DateSent<`/`DateSent>` query params; eShop notifications by stored `ProviderDateSent` (provider's value), never a local row-creation column. Because `date_sent` is null at send time (queued), `ReconcileAsync` first refreshes candidate rows (SID present, `ProviderDateSent` null, selected by a `CreatedAt` window — used only to *select*, capped at 200) via `FetchMessage`, then compares by SID on the `date_sent` clock. |

### DUPLICATE CLAIMS

| write | where the claim is stored | what rejects the second one | where that rejection is caught |
| --- | --- | --- | --- |
| operator resend of a notification | `NotificationResendClaims` table, key column `IdempotencyKey` | primary-key uniqueness (SqlServer PK + EF InMemory keyed store) | `OrderNotificationService.ResendAsync` catches the insert rejection, re-reads the persisted claim **AsNoTracking** (`ResendClaimByKeySpecification` — the failed insert is still tracked, so `FindAsync` would return that incomplete row), returns its `ResultNotificationId` |

### PAGED READS

| read | what caps it | how the caller learns the answer was cut short |
| --- | --- | --- |
| reconciliation `ListMessage` walk | `MaxReconciliationPages` (default 50) × `pageSize` 1000 | response fields `Truncated` (bool) + `PagesRead` (int) |

### REPEATED OPERATIONS

| operation | what tells you the state actually changed | the effects gated on that |
| --- | --- | --- |
| dispatch | `Order.Dispatch()` returns `true` only on `Placed→Dispatched` (false if already Dispatched; throws on Cancelled) | dispatched SMS + scheduled follow-up message |
| cancel | `Order.Cancel()` returns `true` only on `Placed/Dispatched→Cancelled` (false if already Cancelled) | cancelled SMS + cancellation of any pending follow-up |

### UNKNOWN OUTCOMES

| write | the operation you re-read with | the reference you search by |
| --- | --- | --- |
| CreateMessage (any notice/follow-up) | `ListMessage` | `To` + `DateSent` range (+ `From`=Twilio:FromNumber) |
| UpdateMessage (cancel follow-up) | `FetchMessage` | the follow-up's `Sid` |

## 6. Assumptions & Blockers

- **Assumption (minor):** follow-up delay = **3 days** ("a few days later"), safely inside
  Twilio's fixed-schedule window. The exact min/max window is provider prose not in the map →
  `UNVERIFIED`; send is wrapped so a rejected schedule never fails the dispatch operation and is
  recorded as a failed notification.
- **Assumption (minor):** a shopper may hold several numbers; each order event messages **all**
  the buyer's currently-registered numbers (usually one). Deleted numbers are never messaged.
- **Assumption:** `Order` gains an `OrderStatus` (Placed/Dispatched/Cancelled) — an **extension**
  of the existing aggregate (not a parallel model), needed to gate idempotent transitions.
- No Blockers: every required capability (send, schedule, cancel-schedule, fetch status, list
  for reconciliation, redact body, validate number) maps to an SDK operation above.
- **UNVERIFIED (live-only):** exact provider `status` progression timing and the fixed-schedule
  min/max window — handled by defensive coding (never fail the operation; record outcome).
