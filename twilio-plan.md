# twilio-plan.md — SMS order notifications for eShopOnWeb (Twilio, .NET)

SDK: APIMatic-generated **Twilio SDK**, root namespace `TwilioSdk`, client `TwilioSdkClient`,
options `TwilioSdkClientOptions`, DI `services.AddTwilioSdkClient(...)`. Basic auth
(`AccountSidAuthToken = new BasicAuthCredentials { Username, Password }`). All facts below are
grounded from the SDK map / named source files this session.

---

## 1. Scope & sequence

New capability lives in **`src/PublicApi`** (endpoints), a Twilio integration layer in
**`src/Infrastructure`** (SDK wrapper + config), and two new aggregates in
**`src/ApplicationCore`** (`ContactNumber`, `OrderNotification`) with EF config + DbSets on
`CatalogContext`. Steps:

1. **Config + client wiring** — bind `Twilio:` section (fail-fast on blanks), register a
   long-lived `TwilioSdkClient` over a named `HttpClient`; `Twilio:BaseUrl` overrides the
   **messaging** server group only.
2. **Domain** — `ContactNumber` (BuyerId, E164), `OrderNotification` (OrderId, BuyerId, Kind,
   MessageSid, Status, ErrorCode/Message, IsScheduled, ScheduledSendAt, To, Body,
   IdempotencyKey, ContentDisposed). EF configs + DbSets.
3. **Twilio gateway service** (`ISmsGateway`) — thin wrapper over the 5 operations, translating
   SDK exceptions to a result type; **never throws through to the order operation**.
4. **Application services** — `ContactNumberService`, `OrderNotificationService` (place/dispatch/
   cancel/resend/dispose/reconcile), each shopper- or operator-scoped.
5. **Endpoints** (all under `/api/`, JWT): contact-numbers CRUD; orders place/dispatch/cancel;
   my-orders; order notifications; resend/dispose/reconciliation.
6. **Self-verify** against the live account (Canadian test number deliverable, US unreachable
   number undelivered).

Operation → flow map:
- **Register number** → `LookupsV2PhoneNumber.FetchPhoneNumber3` (validate + canonical E.164).
- **Order placed / dispatched / cancelled / resend** → `Api20100401Message.CreateMessage`.
- **Follow-up "how was delivery"** → `CreateMessage` with `scheduleType=Fixed`,
  `sendAt=now+N days`, `messagingServiceSid` (Twilio scheduling requires a Messaging Service).
- **Cancel the queued follow-up** → `Api20100401Message.UpdateMessage(status=Canceled)`.
- **Dispose content** → `Api20100401Message.UpdateMessage(body="")` (redaction — keeps the
  record & outcome, removes the text on the provider).
- **Status refresh (no webhooks)** → `Api20100401Message.FetchMessage`.
- **Reconciliation** → `Api20100401Message.ListMessage(from=Twilio:FromNumber, date range)`.

---

## 2. CONTRACT SHEET

> ⚠ Signatures are **generated code, verbatim** — every parameter name is the literal C#
> identifier; named args use those exact names (the cancellation-token parameter is `ct`, so
> `ct:`). ⚠ Every SDK type is written **fully-qualified** with the namespace its source path
> implies (`TwilioSdk.Models.*`, `TwilioSdk.Models.Enums.*`, `TwilioSdk.Core.Authentication.Basic.*`,
> `TwilioSdk.Core.Configuration.*`, `TwilioSdk.Servers.*`).

All 5 operations are **Case B** (`SdkException<RawError>`; `RawError` = `StatusCode`,
`ReadAsString()`, `ReadAsBytes()`, `ReadAsJson<T>()`). No `…Result` no-throw variants exist.
No operation has pagination metadata → each returns a single response (ListMessage is a plain
list — hand-drive paging, see trap notes).

| Op | Controller · signature (params I pass) | Returns / fields read | Server group | source |
| --- | --- | --- | --- | --- |
| **Validate number** | `client.LookupsV2PhoneNumber.FetchPhoneNumber3(string phoneNumber, string? fields=null, string? countryCode=null, …13 more null…, ct: ct)` — 15 trailing nullables passed `null` | `LookupResponse`: `Valid: bool?`, `PhoneNumber: string?` (canonical E.164), `ValidationErrors` | **Default4** (`lookups.twilio.com`) — **NOT** overridden by `Twilio:BaseUrl` | `map/operations/LookupsV2PhoneNumber.md`; `Models/LookupResponse.cs` |
| **Send message** | `client.Api20100401Message.CreateMessage(string accountSid, string to, statusCallback:null, applicationSid:null, maxPrice:null, provideFeedback:null, attempt:null, validityPeriod:null, forceDelivery:null, contentRetention:null, addressRetention:null, smartEncoded:null, persistentAction:null, trafficType:null, shortenUrls:null, scheduleType:<null|Fixed>, sendAt:<null|DateTimeOffset>, sendAsMms:null, contentVariables:null, riskCheck:null, from:<FromNumber|null>, fallbackFrom:null, messagingServiceSid:<null|MsgSvcSid>, body:<text>, mediaUrl:null, contentSid:null, ct: ct)` | `ApiV2010AccountMessage`: `Sid`, `Status: MessageEnumStatus?`, `ErrorCode: int?`, `ErrorMessage: string?` | **Default** (`api.twilio.com`) — **overridden by `Twilio:BaseUrl`** | `map/operations/Api20100401Message.md`; `Api/Api20100401Message.cs`; `Models/ApiV2010AccountMessage.cs` |
| **Fetch status** | `client.Api20100401Message.FetchMessage(string accountSid, string sid, ct: ct)` | `ApiV2010AccountMessage` (read `Status`, `ErrorCode`, `ErrorMessage`) | Default (overridable) | same page |
| **List (reconcile)** | `client.Api20100401Message.ListMessage(string accountSid, to:null, from:<FromNumber>, dateSent:null, dateSentQuery:<=to (DateSent<)>, dateSentQueryQuery:<=from (DateSent>)>, pageSize:<n>, page:<i>, pageToken:<tok|null>, ct: ct)` | `ListMessageResponse`: `Messages: IReadOnlyList<ApiV2010AccountMessage>?`, `NextPageUri: string?`, `Page: int?` | Default (overridable) | same page; `Models/ListMessageResponse.cs` |
| **Update (cancel/redact)** | `client.Api20100401Message.UpdateMessage(string accountSid, string sid, body:<null|"">, status:<null|Canceled>, ct: ct)` | `ApiV2010AccountMessage` | Default (overridable) | `Api/Api20100401Message.cs` (POST; remarks: "used to redact Message body text and to cancel not-yet-sent messages") |

**ListMessage query-param wire mapping** (from the map): `From`←`from`, `DateSent<`←`dateSentQuery`,
`DateSent>`←`dateSentQueryQuery`. So for range **[from,to]**: `dateSentQueryQuery = from`,
`dateSentQuery = to`.

**Optional create-body fields I deliberately set, with purpose (rest omitted → provider default):**
- `to` (required, positional) — recipient E.164 (the shopper's stored canonical number).
- `from` — set to `Twilio:FromNumber` for **immediate** messages so reconciliation can filter by it.
  Omit (`null`) for the scheduled follow-up. **purpose:** sender identity / reconciliation key.
- `body` — message text. **purpose:** the notification content.
- `scheduleType = MessageEnumScheduleType.Fixed` + `sendAt` + `messagingServiceSid` — the three
  Twilio requires **together** to schedule; set **only** on the follow-up. **purpose:** queue a
  future send at the provider. (`from` must be omitted when `messagingServiceSid` is used.)
- All other 20 create params → **omit → provider default** (`null`): no status callback (there is
  no public URL — trap note), no retention overrides, no media, etc.

**Update-body fields:** `body=""` **only** for content disposal (redaction); `status=Canceled`
**only** for cancelling the queued follow-up. Never both at once.

### Enums (values needed)
| Enum (`TwilioSdk.Models.Enums`) | Members used | source |
| --- | --- | --- |
| `MessageEnumScheduleType` | `Fixed` (`"fixed"`) | `Models/Enums/MessageEnumScheduleType.cs` |
| `MessageEnumUpdateStatus` | `Canceled` (`"canceled"`) | `Models/Enums/MessageEnumUpdateStatus.cs` |
| `MessageEnumStatus` (response) | read-only: `Queued/Sending/Sent/Delivered/Failed/Undelivered/Scheduled/Canceled/Accepted/Received/Read/Receiving/PartiallyDelivered`; use `.Value` (string) for storage | `Models/Enums/MessageEnumStatus.cs` |

`MessageEnumStatus`/`MessageEnumScheduleType`/`MessageEnumUpdateStatus` are `StringEnum<T>`
records (not C# enums) — construct via the static readonly members; read the wire string via
`.Value`. **MUST load `dotnet-models`.**

### Client construction / auth / server
- `new TwilioSdkClient(HttpClient, TwilioSdkClientOptions)`; DI extension `AddTwilioSdkClient` exists
  but I register **manually over a named HttpClient** to own `Timeout` + `PooledConnectionLifetime`
  and to set `LoggerFactory` explicitly. source: `TwilioSdkClient.cs`, `ServiceCollectionExtensions.cs`.
- Auth: `options.AccountSidAuthToken = new TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials
  { Username = <Twilio:AccountSid>, Password = <Twilio:AuthToken> }` (both `required`). source:
  `Core/Authentication/Basic/BasicAuthCredentials.cs`, `sdk-map.md` Servers & auth.
- Env: `options.Environment = TwilioSdk.Servers.ServerEnvironment.Production` (only env).
- Base-URL override (messaging only): `options.Server.Default.Production.BaseUrl = <Twilio:BaseUrl>`
  **only when set**; leave `Default4` (Lookups) untouched. source: `ServerOptions.cs`,
  `Servers/DefaultOptions.cs` (`Production.BaseUrl`, default `https://api.twilio.com`), `sdk-map.md`.
- `accountSid` path param on every Message op = `Twilio:AccountSid` (same as the basic-auth username).

### CROSS-OPERATION INVARIANTS
| invariant | operations | enforced where |
| --- | --- | --- |
| A number that can be **registered/messaged** must be one the provider deems valid; store the provider's **canonical** form, and only ever send to a stored canonical number | `CreateMessage.to` ← `FetchPhoneNumber3.PhoneNumber` (gated by `.Valid==true`) | application (`ContactNumberService` validates at registration; senders read the stored E.164) |
| A **cancel/redact** targets a SID the app obtained from a prior **create** | `UpdateMessage.sid` / `FetchMessage.sid` ← `CreateMessage.Sid` | application (notification row stores `MessageSid`) |
| **Reconciliation** counts only this app's sends | `ListMessage.from` = `Twilio:FromNumber`; compared to stored `OrderNotification.MessageSid` | application |
| **Resend idempotency** — a repeat under the same caller key must not create a second send | operator resend; key held in `OrderNotification.IdempotencyKey`, claimed atomically | application (no provider idempotency-key param exists — see trap) |

---

## 3. Trap notes (hazard + skill; not resolved here)

- **Base-URL override must hit the messaging group only.** Setting the wrong environment's or the
  wrong server group's `BaseUrl` is silently ignored / mis-routes; mutating server options on a
  live client races in-flight calls. Configure before construction. **MUST load
  `dotnet-configuration-resilience`.**
- **Per-attempt `Timeout` is not a call budget; this integration makes multiple SDK calls per
  handler** (validate, send, refresh loops, paged reconcile) — their timeouts add up, and each
  swallowed failure still costs its full bound. **MUST load `dotnet-configuration-resilience`.**
- **`ListMessage` has no auto-pagination** — it is a plain list; a hand-driven page loop that
  trusts "no next page" as its only stop condition is unbounded. **MUST load
  `dotnet-configuration-resilience`.**
- **Write-retry / duplicate sends.** `CreateMessage`/`UpdateMessage` are `POST` → not resent by the
  SDK by default; but a transport failure leaves the send outcome *unknown*, and resend has **no
  provider idempotency-key parameter** (the injected `Idempotency-Key` header is `Guid.NewGuid()`,
  not a key) — idempotency must be enforced app-side and reconciled. **MUST load
  `dotnet-configuration-resilience`.**
- **Error boundary.** All 5 ops are Case B: read `RawError.StatusCode`/`ReadAsString()`. A drifted
  2xx body (missing `required` member) surfaces as `JsonException` from deserialization, **not**
  `SdkException`; a non-2xx body that doesn't match throws `JsonException` while building the error,
  destroying the status. The gateway must catch both so a Twilio hiccup never fails the order op.
  **MUST load `dotnet-error-handling`.**
- **Named-argument binding.** `CreateMessage` has 24 must-pass-explicitly params with no C#
  defaults; a positional call mis-binds. Call with named args. **MUST load `dotnet-calling-endpoints`.**
- **StringEnum, not C# enum** — `MessageEnumStatus` etc. are reference records; compare via the
  static members and store `.Value`. **MUST load `dotnet-models`.**
- **Test seam** — the `HttpClient` handler is the fake seam; match the repo's MSTest style. **MUST
  load `dotnet-testing`.**

---

## 4. REQUIRED READING (load before implementing; contents deliberately not copied here)

Plugin-qualified `twilio-platforms-team:` skills:
- `dotnet-configuration-resilience` — base-URL override, timeouts/call budget, hand-driven paging,
  write-retry/idempotency, logging & sensitive-data posture.
- `dotnet-error-handling` — Case B `RawError`; the two `JsonException` directions above.
- `dotnet-calling-endpoints` — named-argument calls, must-pass-explicit nullables.
- `dotnet-models` — `StringEnum<T>`, `AdditionalProperties`, wire vs C# names.
- `dotnet-client-initialization` — named-`HttpClient` registration, long-lived client, DI. *(loaded)*
- `dotnet-authentication` — `BasicAuthCredentials` set before construction / in DI callback.
- `dotnet-testing` — HttpClient fake seam.

Mandatory hazard rows (always): a malformed **2xx** body → `JsonException` from deserialization
(escapes an SDK-exception-only catch); a non-2xx body not matching its error shape → `JsonException`
**replaces** the `SdkException`, destroying the HTTP status. Both handled in the gateway boundary.

---

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | **Credential fail-fast** | A `TwilioOptions` bound from `Twilio:`; a validator throws at startup if `AccountSid`, `AuthToken`, `FromNumber`, or `MessagingServiceSid` is missing/blank (each part checked individually — a blank part ≠ a missing one). `BaseUrl` optional. Registered before the client so the host refuses to start rather than 401 on first call. |
| 2 | **Secret sourcing & rotation** | Values come from **.NET user-secrets** (loaded from the env vars by me; never written into repo files). The singleton binds options **once at registration**, so a rotated `AuthToken` takes effect on process restart. Restart-to-rotate is acceptable for this reference app; documented. |
| 3 | **Total timeout budget** | `options.Retry.Timeout` set explicitly (per-attempt, e.g. 10s), and each application handler wraps its SDK calls in a **linked `CancellationTokenSource`** off `HttpContext.RequestAborted` with a whole-operation budget (e.g. 30s), passed as `ct:` to every call — the only true call bound. `HttpClient.Timeout` set as backstop. |
| 4 | **Write-retry ownership** | Sends/updates are `POST` — the default `HttpMethodsToRetry` (`GET,HEAD,PUT,OPTIONS`) never resends them. Lookups/fetch/list are `GET` (safe to retry). No verb added to the retry list. |
| 5 | **Idempotency & ambiguous writes** | Resend takes a **caller-supplied** key → claimed atomically app-side (no provider key exists); a repeat returns the first result without a second send. Place/dispatch/cancel guard on a domain state transition so a no-op transition fires no message. Transport-failure outcome of a send is settled by the reconciliation report (provider `From`-filtered list vs stored SIDs). |
| 6 | **Observability** | Gateway logs op name, notification id, message **SID**, status, and `RawError.StatusCode` + provider numeric error code on failure at Info/Warning. **Never** logs the shopper's phone number, message body, or a provider error body (which can echo the number). |
| 7 | **Sensitive data** | Phone numbers + message bodies are sensitive. `LogRequestBody` stays **false** and `LoggingOptions.LoggerFactory` is set explicitly to **`NullLoggerFactory.Instance`** — verified necessary: the number-**lookup** call carries the number in the request URL *path* (paths are not redacted), so the SDK's built-in request-line logger would otherwise write it. NullLoggerFactory silences the SDK entirely and also disables the `TWILIOSDKCLIENT_LOG` env var. Verified against the live account: neither number nor the auth token appears in the app log. |
| 8 | **Environment selection** | Single env `ServerEnvironment.Production`. Messaging (`Default`, `api.twilio.com`) is overridable via `Twilio:BaseUrl` for a mock/sandbox; Lookups (`Default4`, `lookups.twilio.com`) is not. No SDK sandbox env exists — test traffic is kept safe by only ever registering/sending to the two task-provided numbers (Canadian deliverable, US unreachable). |

---

## 6. Assumptions & Blockers

- **No Blockers.** Every capability the task needs maps to a map operation.
- **Assumption (design, YOUR CALL):** when a shopper has multiple registered numbers, order
  messages go to the **most-recently-registered** one. Reasonable; keeps live-message volume low.
- **Assumption:** "how did delivery go" follow-up scheduled **3 days** after dispatch (within
  Twilio's 15-min…7-day scheduling window; "a few days").
- **UNVERIFIED (live-traffic):** a follow-up sent via `messagingServiceSid` acquires `From =
  Twilio:FromNumber` at send time (single-number account), so it appears in the `From`-filtered
  reconciliation list. Handled defensively — if it doesn't, the report surfaces it as a visible
  discrepancy (exactly the report's purpose), not a crash.
- **Assumption:** in-memory provider does not enforce unique indexes, so the resend idempotency
  claim is enforced with an in-process atomic guard (single PublicApi host) + persisted key. Noted
  as environment-scoped.

## 7. Source labels
All contract rows cite a map page or map-named source file (col "source"). Design decisions are
labelled **YOUR CALL** in §6. One **UNVERIFIED** live-traffic item in §6.
