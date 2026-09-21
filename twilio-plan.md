# twilio-plan.md — Order SMS notifications for eShopOnWeb (Twilio)

SDK: APIMatic-generated **Twilio SDK (.NET)** — root namespace `TwilioSdk`, client `TwilioSdkClient`,
options `TwilioSdkClientOptions`, DI `services.AddTwilioSdkClient(...)`. Target `netstandard2.0`, C# 14.
(The getting-started orientation table said `Twilio`/`TwilioClient`; the **map wins** — the source declares `TwilioSdk`.)

---

## 1. Scope & sequence

Additive capability on `src/PublicApi` (JWT). Reuses existing `Order`/`OrderItem`/`CatalogItemOrdered`
aggregate for placement. New persisted aggregates live in a Notifications module and persist through the
existing `CatalogContext` + generic `EfRepository<T>` (works for any `IAggregateRoot` with a `DbSet`).

Build order:
1. **Vendor SDK** — copy the temp clone's build source (Api/Core/Errors/Models/Servers + root `*.cs` +
   `TwilioSdk.csproj` + LICENSE) into `src/TwilioSdk/`; set `ManagePackageVersionsCentrally=false` there
   (repo root has CPM=true which would NU1008 on the SDK's pinned versions). ProjectReference from PublicApi.
   The temp clone stays the read-only map reference and is never a build input.
2. **Config + client** — `TwilioSettings` bound from `Twilio:` section; fail-fast validation; register
   `TwilioSdkClient` via `AddTwilioSdkClient`; apply `BaseUrl` override to `options.Server.Default.Production.BaseUrl`.
3. **Provider layer** — `ITwilioMessagingService` (ApplicationCore interface + app-level DTOs) implemented in
   Infrastructure over the SDK. Operations: validate/canonicalize number (Lookups), send immediate, schedule,
   fetch status, cancel scheduled, redact body, list-by-From (paginated).
4. **Domain** — new aggregates `ContactNumber`, `OrderNotification`, `TrackedOrder` + EF configs + DbSets.
5. **App services** — `ContactNumberService`, `OrderNotificationService` (place/dispatch/cancel/list/get/
   resend/dispose/reconcile).
6. **Endpoints** — the 12 routes under `/api/` following the project's `IEndpoint` minimal-API convention.
7. **Secrets** — load env vars into .NET user-secrets (never into repo files).
8. **Self-verify** end to end against the two sandbox numbers.

Operation → SDK call map:
| Flow route | SDK operation |
| --- | --- |
| `POST /api/contact-numbers` (validate + canonical) | `LookupsV2PhoneNumber.FetchPhoneNumber3` |
| order-placed / dispatched / cancelled notify (immediate) | `Api20100401Message.CreateMessage` (from = FromNumber) |
| dispatch follow-up (scheduled, days later) | `Api20100401Message.CreateMessage` (scheduleType=Fixed, sendAt, messagingServiceSid) |
| cancel the follow-up before it sends | `Api20100401Message.UpdateMessage` (status=Canceled) |
| GET notifications / my-orders (refresh outcome) | `Api20100401Message.FetchMessage` |
| `POST /api/notifications/{id}/resend` | `Api20100401Message.CreateMessage` (from = FromNumber) |
| `DELETE /api/notifications/{id}/content` (redact at provider) | `Api20100401Message.UpdateMessage` (body="") |
| `GET /api/notifications/reconciliation` | `Api20100401Message.ListMessage` (from = FromNumber, date range, paged) |

---

## 2. CONTRACT SHEET

> ⚠ Signatures below are **generated code, verbatim**. Every parameter name is the literal C# identifier;
> named arguments must use them exactly (the cancellation-token parameter is literally `ct`, so write `ct:`).
> ⚠ Every SDK type is written **fully-qualified with the namespace its source path implies**, taken from the
> path the map gives for THAT type (`Models/` → `TwilioSdk.Models`, `Models/Enums/` → `TwilioSdk.Models.Enums`,
> `Core/Authentication/Basic/` → `TwilioSdk.Core.Authentication.Basic`, `Servers/` → `TwilioSdk.Servers`).

### Operations

| op | controller · signature | request params (in scope) | response envelope · fields read | error | pagination | source |
| --- | --- | --- | --- | --- | --- | --- |
| Validate number | `client.LookupsV2PhoneNumber.FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `phoneNumber` = raw input (path); all 15 optionals → pass `null` | `TwilioSdk.Models.LookupResponse` — read `Valid` (bool?), `PhoneNumber` (string?, E.164 canonical), `CountryCode` (string?) | `SdkException<RawError>` — **Case B** | none | map/operations/LookupsV2PhoneNumber.md; Models/LookupResponse.cs |
| Send / resend (immediate) | `client.Api20100401Message.CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid` = `Twilio:AccountSid` (path); `to` = destination E.164; `from` = `Twilio:FromNumber`; `body` = text; **all other 22 optionals → `null`** | `TwilioSdk.Models.ApiV2010AccountMessage` — read `Sid`, `Status` (`MessageEnumStatus?`), `To`, `From`, `ErrorCode`, `ErrorMessage`, `DateSent` | Case B | none | map/operations/Api20100401Message.md; Models/ApiV2010AccountMessage.cs |
| Schedule follow-up | same `CreateMessage` | `accountSid`; `to`; `scheduleType` = `MessageEnumScheduleType.Fixed`; `sendAt` = now+N days; `messagingServiceSid` = `Twilio:MessagingServiceSid`; `body`; **`from` = `null`** (scheduling is Messaging-Service-only); other optionals `null` | same | Case B | none | map row; Models/Enums/MessageEnumScheduleType.cs |
| Fetch status | `client.Api20100401Message.FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid`; `sid` = stored SID | `ApiV2010AccountMessage` — read `Status`, `ErrorCode`, `ErrorMessage`, `DateSent` | Case B | none | map row |
| Cancel scheduled | `client.Api20100401Message.UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid`; `sid`; `body` = `null`; `status` = `MessageEnumUpdateStatus.Canceled` | `ApiV2010AccountMessage` — read `Status` | Case B | none | map row; Models/Enums/MessageEnumUpdateStatus.cs |
| Redact body | same `UpdateMessage` | `accountSid`; `sid`; `body` = `""` (empty string ⇒ redaction); `status` = `null` | `ApiV2010AccountMessage` | Case B | none | Api/Api20100401Message.cs `<remarks>`: "used to redact Message body text and to cancel not-yet-sent messages" |
| Reconcile list | `client.Api20100401Message.ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid`; `to`=`null`; `from`=`Twilio:FromNumber`; `dateSent`=`null`; **`dateSentQuery`** = `to` bound (wire `DateSent<`); **`dateSentQueryQuery`** = `from` bound (wire `DateSent>`); `pageSize` = 1000; `page`/`pageToken` per pagination | `TwilioSdk.Models.ListMessageResponse` — read `Messages` (`IReadOnlyList<ApiV2010AccountMessage>?`), `NextPageUri` (string?) | Case B | **link-based**: follow `NextPageUri`; parse its `Page` & `PageToken` query values into next call's `page`/`pageToken`; stop when `NextPageUri` is null | map/operations/Api20100401Message.md; Models/ListMessageResponse.cs |

Notes on optional create-body fields deliberately omitted (each `omit → provider default`): `statusCallback`
(no public callback URL exists — task says so), `maxPrice`, `validityPeriod`, `smartEncoded`, `shortenUrls`,
`contentRetention`/`addressRetention` (omit → provider default retention; content disposal is done post-hoc via
UpdateMessage redaction, which the task requires as an explicit operator action), `riskCheck`, `mediaUrl`,
`contentSid`, `contentVariables`, `sendAsMms`, `forceDelivery`, `fallbackFrom`, `applicationSid`,
`provideFeedback`, `attempt`, `persistentAction`, `trafficType`. None are needed for a plain SMS; setting any
would override a provider-side default.

### Enums needed (source: Models/Enums/*.cs)

| enum | member used | wire |
| --- | --- | --- |
| `TwilioSdk.Models.Enums.MessageEnumScheduleType` | `.Fixed` | `fixed` |
| `TwilioSdk.Models.Enums.MessageEnumUpdateStatus` | `.Canceled` | `canceled` |
| `TwilioSdk.Models.Enums.MessageEnumStatus` (read on response) | read `.Value` (StringEnum) | queued/sent/delivered/failed/undelivered/scheduled/canceled/… |

`MessageEnumStatus` is a `StringEnum<T>` (read `.Value` for the wire string; don't switch on C# enum). Confirm
member/accessor via Models/Enums/MessageEnumStatus.cs at implement time if needed.

### Client construction / auth / server node (source: TwilioSdkClientOptions.cs, ServiceCollectionExtensions.cs, Servers/DefaultOptions.cs, sdk-map.md Servers&auth)

- Auth: `options.AccountSidAuthToken = new TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials { Username = <AccountSid>, Password = <AuthToken> }`.
- `accountSid` path parameter on every `Api20100401Message` op = the account SID string (`Twilio:AccountSid`).
- Server groups: `Api20100401Message` → **`Default`** (`https://api.twilio.com`); `LookupsV2PhoneNumber` →
  **`Default4`** (`https://lookups.twilio.com`). BaseUrl override for the **messaging** API =
  `options.Server.Default.Production.BaseUrl = <Twilio:BaseUrl>` (leaves Lookups' `Default4` untouched — Lookups
  is a different host the task says this setting does not govern).
- DI: `services.AddTwilioSdkClient(o => { … })` registers a singleton client over `IHttpClientFactory`.

### CROSS-OPERATION INVARIANTS

| invariant | operations | enforced where |
| --- | --- | --- |
| A destination number may be registered/messaged only if Lookups says `Valid == true`; the **canonical** `PhoneNumber` from Lookups (not raw input) is what is stored and later sent to. | `CreateMessage.to` ← `FetchPhoneNumber3.PhoneNumber` | implementation (ContactNumberService) |
| `FetchMessage`/`UpdateMessage`/redact/cancel `sid` must be a SID this app previously stored from a `CreateMessage` response. | `FetchMessage`/`UpdateMessage` ← `CreateMessage.Sid` | implementation (OrderNotification store) |
| Reconciliation matches provider rows to app rows by `Sid`; only rows whose `From == Twilio:FromNumber` are in the provider set (enforced by the `from` query param, not post-filtering). | `ListMessage` ↔ stored `OrderNotification.ProviderMessageSid` | implementation |
| Cancel-order must cancel the order's still-pending scheduled follow-up (by its stored `Sid`) so no delivery-survey reaches a cancelled order. | `UpdateMessage(status=Canceled)` ← follow-up `CreateMessage.Sid` | implementation |

---

## 3. Trap notes (name the hazard; the skill resolves it)

- **Client/HttpClient lifetime & DI singleton.** Getting the client's lifetime and HttpClient ownership wrong
  (rebuilding per request, or capturing a rotated secret) is a resource/behaviour hazard. `MUST load dotnet-client-initialization`.
- **Auth credential wiring.** Where/when credentials are set on the options and loading them from config not
  code. `MUST load dotnet-authentication`.
- **Calling ops with many null optionals / named args.** Positional mis-binding across 24/8/15-param signatures,
  and the injected `Idempotency-Key` header is NOT a real key. `MUST load dotnet-calling-endpoints`.
- **Models: StringEnum & unknown fields.** `MessageEnumStatus` etc. are `StringEnum<T>`, not C# enums; response
  models keep unknown fields via `AdditionalProperties`. `MUST load dotnet-models`.
- **Error boundary — two JsonException directions + Case B.** Every message op is Case B (`SdkException<RawError>`);
  a drifted 2xx body throws `JsonException` (not `SdkException`) and a non-2xx body that doesn't match its shape
  throws `JsonException` destroying the status. `MUST load dotnet-error-handling`.
- **Resilience: timeout is per-attempt; POST not retried; BaseUrl node; logging leaks bodies.** The whole-call
  budget needs a `CancellationToken`; `LogRequestBody` prints numbers/bodies in clear. `MUST load dotnet-configuration-resilience`.
- **Testing seam.** The `HttpClient` ctor arg is the fake seam. `MUST load dotnet-testing`.

---

## 4. REQUIRED READING (load ALL before implementing; contents deliberately not inlined here)

| skill (twilio-platforms-team) | governs |
| --- | --- |
| `dotnet-client-initialization` | Step 2 client + DI registration |
| `dotnet-authentication` | Step 2 credential wiring |
| `dotnet-calling-endpoints` | Step 3 every SDK call |
| `dotnet-models` | Step 3 request/response models, StringEnum |
| `dotnet-error-handling` | Step 3 error boundary (always required) |
| `dotnet-configuration-resilience` | Step 2/3 retries, timeout, BaseUrl, logging, pagination |
| `dotnet-testing` | tests |

Mandatory hazard rows (verbatim): (a) a drifted/malformed **2xx** body (missing `required` member) surfaces as
`System.Text.Json.JsonException` from deserialization, **not** `SdkException`, so an SDK-exception-only ladder
lets it escape; (b) a **non-2xx** body that doesn't match its operation's generated error shape throws
`JsonException` **while the error object is constructed**, replacing the `SdkException` and destroying the HTTP
status. The message ops are Case B, so my boundary catches `SdkException<RawError>` **and** `JsonException`
**and** a general fallback — and never lets a provider failure fail the order operation.

---

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `TwilioSettings` bound from `Twilio:`; a startup validator throws if `AccountSid`, `AuthToken`, `FromNumber`, or `MessagingServiceSid` is missing **or blank** (each part checked separately). Host refuses to start rather than surfacing a 401 on first call. `BaseUrl` is optional. |
| 2 | Secret sourcing & rotation | Secrets come from **.NET user-secrets** (loaded from env vars by me; never in repo files). `AddTwilioSdkClient` builds the options object **once at registration** and captures it in the singleton, so a rotated `AuthToken` needs a process restart. Acceptable for this app; documented. No hot-rotation requirement. |
| 3 | Total timeout budget | SDK `Timeout` is **per-attempt**. Each provider call is bounded by a `CancellationToken` with a whole-call deadline (default 30s) created in the provider layer; that is the number the caller actually waits. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS, so `POST` (`CreateMessage`) and `DELETE` are **never** auto-resent — a send is never silently duplicated by the SDK. `GET` (`FetchMessage`/`ListMessage`) may retry, which is safe. I keep SDK retry defaults. |
| 5 | Idempotency & ambiguous writes | `CreateMessage` exposes **no real idempotency key** (Case B, none in signature; the injected `Idempotency-Key` GUID is not one). So: **resend idempotency is enforced by the application** — the caller-supplied key is stored on the resend `OrderNotification`; a repeat under the same key returns the existing `notificationId` and sends nothing; a fresh key sends. For place/dispatch/cancel, ambiguous-send is bounded by no-SDK-retry (row 4) plus the reconciliation report as the audit path. |
| 6 | Observability | Log at Information: operation + orderId/notificationId + provider `Sid` + resulting status; at Warning/Error: provider failure with `RawError.StatusCode` and `ReadAsString()` body for correlation. **Phone numbers and message bodies are never logged.** SDK `LogRequestBody` stays **off**. |
| 7 | Sensitive data | In scope: destination phone numbers and SMS body text (PII / message content). Therefore `LogRequestBody` stays off **and** `options.Logging.LoggerFactory` is set explicitly at registration so the `TWILIOCLIENT_LOG` env var cannot switch body logging on from outside code. My own logs never echo `to`/`body`. Content disposal redacts the provider copy (UpdateMessage body="") **and** nulls the app's stored copy. |
| 8 | Environment selection | One environment: `ServerEnvironment.Production`. Groups touched: `Default` (api.twilio.com — messages) and `Default4` (lookups.twilio.com). `Twilio:BaseUrl`, when set, overrides only `Default` (the messaging host). No separate sandbox environment exists in the SDK; test traffic is kept safe by only ever registering/sending to the two task-provided numbers (`TWILIO_TEST_TO_NUMBER`, `TWILIO_UNREACHABLE_TO_NUMBER`). |

---

## 6. Assumptions & Blockers

- **No blockers.** Every capability the task needs maps to an SDK operation above.
- Assumption: buyer identity = JWT `ClaimTypes.Name` (username/email), matching how eShop derives `Order.BuyerId`.
- Assumption (`YOUR CALL — not in the map`): immediate notifications are sent with `from = Twilio:FromNumber`
  (guarantees they appear under the reconciliation `From` filter); the scheduled follow-up must use
  `messagingServiceSid` because Twilio message scheduling is Messaging-Service-only (cannot schedule with a bare
  `from`). Consequence: a follow-up that actually sent might carry a pool-assigned `From`; but in the required
  flow the follow-up is cancelled before send, so this does not affect verification.
- Assumption (`YOUR CALL`): new state persists via `CatalogContext` + `EfRepository<T>`; with the in-memory
  provider it survives one run (per the environment note). Concurrency for resend-idempotency is check-then-act
  (single-run, low-contention); a DB unique index would harden it under SQL.
- Assumption (`YOUR CALL`): follow-up `sendAt` = now + 3 days (well within Twilio's 15-min…7-day window and far
  enough to cancel before send during verification); configurable via `Twilio:FollowUpDelayDays` (default 3).
