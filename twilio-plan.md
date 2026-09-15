# Twilio .NET SDK — SMS Order Notifications Integration Plan (eShopOnWeb)

SDK: `AsadAli.TwilioSdk` (install version-less: `dotnet add package AsadAli.TwilioSdk`).
Root namespace `TwilioSdk`. Client `TwilioSdkClient`, options `TwilioSdkClientOptions`.
Source commit the map was generated from: `51fdf48`. Every fact below is grounded in the
bundled SDK map (page cited per row) or, where noted, the pinned SDK source.

---

## 1. Scope & sequence

| # | Step | Operation(s) |
|---|---|---|
| 0 | Register + construct the SDK client (DI), credentials, per-host base URL | `AddTwilioSdkClient` / `TwilioSdkClientOptions` |
| 1 | Validate + canonicalize a phone number at registration | `client.LookupsV2PhoneNumber.FetchPhoneNumber3` |
| 2 | Send SMS (order placed / dispatched / cancelled) — From-number and Messaging-Service paths | `client.Api20100401Message.CreateMessage` |
| 3 | Schedule a follow-up message (provider-queued) | `client.Api20100401Message.CreateMessage` (+ `ScheduleType`, `SendAt`, `MessagingServiceSid`) |
| 4 | Cancel a scheduled-but-unsent message | `client.Api20100401Message.UpdateMessage` (`Status = Canceled`) |
| 5 | Fetch a single message's delivery status by SID | `client.Api20100401Message.FetchMessage` |
| 6 | List messages for reconciliation (From + date range, all pages) | `client.Api20100401Message.ListMessage` |
| 7 | Redact a message's body at the provider (preserve the record) | `client.Api20100401Message.UpdateMessage` (`Body = ""`) |
| 8 | Idempotent resend under a caller key | **App-side only — SDK exposes no idempotency mechanism** (see Assumptions & Blockers) |

All operations are `async` and return `Task<T>`; every method's last two params are
`RequestOptions? requestOptions = null, CancellationToken ct = default`. Pass your ASP.NET
request-aborted token as `ct:` (named — the parameter is literally `ct`, never `cancellationToken`).

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal
> C# identifier. The cancellation-token parameter really is named `ct`: in named
> arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take
> each one from that type's own map row, never from where a neighbouring type sits. A members
> table names the namespace outright; otherwise the row's source path implies it
> (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root
> namespace). Enums, unions, auth, server and client-config types are spread across different
> child namespaces, and two types configured side by side in the same options object routinely
> live in different ones. Dropping a type to the root or to `.Models` makes the implementer
> guess the wrong `using`, and the build breaks.

### 2.0 Client construction, DI, auth, server/base-URL  (map: `sdk-map.md` §Getting a client, §Servers & auth)

**Namespaces (add a `using` per kind — child namespaces are NOT imported transitively):**

| Type | Namespace | `using` |
|---|---|---|
| `TwilioSdkClient`, `TwilioSdkClientOptions`, `ServerOptions` | root | `using TwilioSdk;` |
| `ServerEnvironment`, `DefaultOptions`, `Default4Options` | `TwilioSdk.Servers` | `using TwilioSdk.Servers;` |
| `BasicAuthCredentials` | `TwilioSdk.Core.Authentication.Basic` | `using TwilioSdk.Core.Authentication.Basic;` |
| `RetryOptions` | `TwilioSdk.Core.Configuration` | `using TwilioSdk.Core.Configuration;` |
| `RequestOptions` | `TwilioSdk.Core` | `using TwilioSdk.Core;` |
| Controllers (`Api20100401Message`, `LookupsV2PhoneNumber`) | `TwilioSdk.Api` | `using TwilioSdk.Api;` |
| Records (`ApiV2010AccountMessage`, `LookupResponse`, `ListMessageResponse`) | `TwilioSdk.Models` | `using TwilioSdk.Models;` |
| Message enums (`MessageEnum*`) | `TwilioSdk.Models.Enums` | `using TwilioSdk.Models.Enums;` |
| `SdkException<T>` | `TwilioSdk.Core.Exceptions` | `using TwilioSdk.Core.Exceptions;` |
| `RawError` | `TwilioSdk.Core.ErrorResponse` | `using TwilioSdk.Core.ErrorResponse;` |

**Construction.** Constructor: `TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)`.
DI: `services.AddTwilioSdkClient(o => { … })` (source `ServiceCollectionExtensions.cs`). The `HttpClient`
must be long-lived / factory-owned (see trap notes). Every API group is a property on the client
(`client.Api20100401Message`, `client.LookupsV2PhoneNumber`).

**`TwilioSdkClientOptions` properties** (source `TwilioSdkClientOptions.cs`):

| Property | Type |
|---|---|
| `Environment` | `ServerEnvironment` (only member: `ServerEnvironment.Production`) |
| `Retry` | `RetryOptions` |
| `Logging` | `LoggingOptions` |
| `Server` | `ServerOptions` |
| `AccountSidAuthToken` | `BasicAuthCredentials?` |

**Auth (Basic).** Set `options.AccountSidAuthToken = new BasicAuthCredentials(username, password)`.
Per the source XML doc: use an API key SID as username + API key secret as password (preferred), or
Account SID + Auth Token for local testing only. The AccountSid string you pass as the `accountSid`
path parameter to every `Api20100401*` operation is your Twilio Account SID (`AC…`), independent of
which credential pair authenticates. Confirm exact constructor shape via `dotnet-authentication`.

**Per-host base URL (`Twilio:BaseUrl`) — CRITICAL for item 1.** The SDK carries a *separate* base URL
per server group under `options.Server` (`ServerOptions`, root namespace). Verified from source
(`Servers/DefaultOptions.cs`, `Servers/Default4Options.cs`):

| Server group | Property path | Default host | Used by |
|---|---|---|---|
| `Default` (api) | `options.Server.Default.Production.BaseUrl` | `https://api.twilio.com` | **Messaging API** (`CreateMessage`, `Fetch/List/Update/DeleteMessage`) |
| `Default4` (lookups) | `options.Server.Default4.Production.BaseUrl` | `https://lookups.twilio.com` | **Lookup V2** (`FetchPhoneNumber3`) |

Apply `Twilio:BaseUrl` to **`options.Server.Default.Production.BaseUrl` only** (the messaging/api host).
It does **NOT** and must not touch `Default4` — the Lookup lives on a **different host/controller**
(`lookups.twilio.com`), so overriding messaging's base URL leaves Lookup pointed at the real Twilio
lookups host, which is the intended behaviour. (`ServerEnvironment` has a single member `Production`;
each group resolves its URL via that environment.)

---

### 2.1 Item 1 — Validate + canonicalize a phone number  (map: `operations/LookupsV2PhoneNumber.md`, `models/records-4-Li-Me.md`)

- **Operation:** `client.LookupsV2PhoneNumber.FetchPhoneNumber3`
- **Signature:** `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `phoneNumber` is the path segment (the number as typed, e.g. what the shopper entered). All 15 params `fields`…`partnerSubId` are nullable-with-no-default → **must be passed explicitly**; pass `null` for every one you don't use (a basic validate+canonicalize needs none of them — pass `null` through, optionally `countryCode` for national-format input).
- **Returns:** `LookupResponse` (record, `TwilioSdk.Models`). Fields you read:
  - `Valid (valid): bool?` — **the validity/usability signal.** Treat `Valid == true` as "provider considers this a valid destination"; reject when `Valid != true` (false or null).
  - `PhoneNumber (phone_number): string?` — **the provider's canonical E.164 form. Store THIS**, not the caller's input.
  - `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?` — reasons a number is invalid (for logging/messaging).
  - Also present: `NationalFormat`, `CountryCode`, `CallingCountryCode`, `Url`.
- **Error:** `SdkException<RawError>` — **Case B** (no typed accessors). Accessors: `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsBytes()`, `ReadAsJson<T>()`. (A 404 from Lookup can indicate an unresolvable number depending on the provider — treat non-2xx defensively; see trap.)
- **Pagination:** none.
- **Host:** Lookups (`Default4`), unaffected by `Twilio:BaseUrl` (see 2.0).

### 2.2 Item 2 — Send an SMS  (map: `operations/Api20100401Message.md`, `models/records-1-Ac-Ca.md`)

- **Operation:** `client.Api20100401Message.CreateMessage`
- **Signature (verbatim):** `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `accountSid` (path) and `to` (destination, E.164 — use the canonical value from item 1) are required.
  - **All 24 params `statusCallback`…`contentSid` are nullable but have NO C# default → must be passed explicitly.** Call with **named arguments** and pass `null` for every field you don't set; positional calls mis-bind. Set only what each send needs (`from` or `messagingServiceSid`, and `body`).
- **Two ways to send (set exactly one sender):**
  - **From a number:** set `from: "<FromNumber E.164>"`, `messagingServiceSid: null`.
  - **Via Messaging Service SID:** set `messagingServiceSid: "<MG…>"`, `from: null`.
  - Always set `body: "<text>"`; `to: "<E.164>"`.
- **Returns:** `ApiV2010AccountMessage` (record, `TwilioSdk.Models`). Read:
  - `Sid (sid): string?` — **the provider message identifier.**
  - `Status (status): MessageEnumStatus?` — **current delivery status/outcome** (enum, §2.9).
  - Also: `ErrorCode (error_code): int?`, `ErrorMessage (error_message): string?`, `To`, `From`, `MessagingServiceSid`, `DateSent (date_sent): string?`, `DateCreated`, `Price`, `NumSegments`, `NumMedia`, `Direction (direction): MessageEnumDirection?`.
- **Error:** `SdkException<RawError>` — **Case B**. Read `StatusCode` + `ReadAsString()`; Twilio's provider error code/message live in that JSON body (typical shape `{ "code": <int>, "message": "…", "status": <int>, "more_info": "…" }`). Deserialize with `ReadAsJson<T>()` into a small DTO to surface the provider `code`/`message` — see trap note on Case B and the `JsonException` hazard in REQUIRED READING.
- **Pagination:** none. **Host:** messaging (`Default`).

### 2.3 Item 3 — Schedule a message (provider-queued)  (map: `operations/Api20100401Message.md`, `models/enums.md`)

- **Same operation:** `CreateMessage`, with these fields set (all still passed among the 24 explicit args):
  - `scheduleType: MessageEnumScheduleType.Fixed` (the only enum member; wire `fixed`).
  - `sendAt: DateTimeOffset` — the future send time (serialized to wire `SendAt`).
  - `messagingServiceSid: "<MG…>"` — **required for scheduling.** The `ScheduleType` enum's own source doc states scheduling is "For Messaging Services only" (used in conjunction with the send-time). Set `from: null` for the scheduled send.
  - `body`, `to` as normal.
- **Returns:** `ApiV2010AccountMessage`; a successfully-scheduled message comes back with `Status = MessageEnumStatus.Scheduled` and a `Sid` (`SM…`) — **persist that SID** so item 4 can cancel it.
- **Send-at window constraint:** the SDK signature imposes **no** window (any `DateTimeOffset` compiles). The valid send-at window is a **provider-enforced** rule, not in the SDK surface — do NOT hard-code a window from memory. Treat an out-of-window time as a provider rejection: it returns `SdkException<RawError>` (Case B) whose body carries the reason; surface `code`/`message` rather than pre-validating. `UNVERIFIED` (only live traffic / current Twilio docs confirm the exact min/max offsets).
- **Error / host:** as item 2.

### 2.4 Item 4 — Cancel a scheduled-but-unsent message  (map: `operations/Api20100401Message.md` §UpdateMessage, `models/enums.md`)

- **Operation:** `client.Api20100401Message.UpdateMessage`
- **Signature:** `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - To cancel: `body: null`, `status: MessageEnumUpdateStatus.Canceled` (the **only** member of that enum; wire `canceled`). `sid` = the scheduled message's `SM…` SID.
- **Returns:** `ApiV2010AccountMessage` (its `Status` should reflect `Canceled`).
- **If it already sent:** the message is no longer cancelable; the provider **rejects** the update → `SdkException<RawError>` (**Case B**). Read `StatusCode` + body (`code`/`message`) to distinguish "already sent / too late" from other failures; the follow-up therefore may still reach the customer only if cancel was attempted after send — cancel promptly on order cancellation. `UNVERIFIED` which exact status/code the provider returns for an already-sent cancel (live-only).
- **Pagination:** none. **Host:** messaging (`Default`).

### 2.5 Item 5 — Fetch a single message's status  (map: `operations/Api20100401Message.md` §FetchMessage)

- **Operation:** `client.Api20100401Message.FetchMessage`
- **Signature:** `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns:** `ApiV2010AccountMessage`. Read `Status (status): MessageEnumStatus?` (full value set §2.9), plus `ErrorCode`, `ErrorMessage`, `To`, `From`, `DateSent`, `Sid`.
- **Error:** `SdkException<RawError>` — **Case B** (a 404 = unknown SID). **Host:** messaging (`Default`).

### 2.6 Item 6 — List messages for reconciliation (From + date range, all pages)  (map: `operations/Api20100401Message.md` §ListMessage, `models/records-1-Ac-Ca.md`, `records-4-Li-Me.md`)

- **Operation:** `client.Api20100401Message.ListMessage`
- **Signature:** `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` — call with **named arguments** (the 8 filters are nullable/no-default; pass `null` to skip).
- **Filters (wire ← C#):**
  - `From ← from` — **filter by the sending number** (set to the specific From E.164).
  - `DateSent ← dateSent` — exact-day match (leave `null` for a range).
  - `DateSent< ← dateSentQuery` — messages sent **before** this instant → pass your range **`to`** here.
  - `DateSent> ← dateSentQueryQuery` — messages sent **after** this instant → pass your range **`from`** here.
  - So a `[from, to]` window = `dateSentQueryQuery: from` (after), `dateSentQuery: to` (before), `dateSent: null`. Values are `DateTimeOffset` (ISO-8601 on the wire).
- **Returns:** `ListMessageResponse` (record, `TwilioSdk.Models`):
  - `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?` — the page of items. Per item read: `Sid`, `Status (MessageEnumStatus?)`, `From`, `To`, `DateSent (date_sent): string?`.
  - Page envelope: `Page (page): int?`, `PageSize (page_size): int?`, `NextPageUri (next_page_uri): string?`, `PreviousPageUri`, `FirstPageUri`, `Uri`, `Start`, `End`.
- **Pagination — MANUAL (map row: "Pagination: none — only `page`, no `perPage`"; no auto-iterator/`…Result` helper exists):** the SDK returns one page and does **not** iterate for you. To cover the whole range, loop: start `page: 0` with a chosen `pageSize` (and `pageToken: null`); after each call, **stop when `NextPageUri` is null** (or `Messages` is null/empty), otherwise request the next page (increment `page`, or extract the `PageToken` query value from `NextPageUri` and pass it as `pageToken`). Keep the other filters identical on every page. Do not assume a total count — drive purely off `NextPageUri`.
- **Error:** `SdkException<RawError>` — **Case B**. **Host:** messaging (`Default`).

### 2.7 Item 7 — Redact a message body at the provider (preserve the record)  (map: `operations/Api20100401Message.md` §UpdateMessage / §DeleteMessage)

- **Redaction = `UpdateMessage` with an empty body.** Call `client.Api20100401Message.UpdateMessage(accountSid, sid, body: "", status: null, ct: …)`. The map's UpdateMessage note states it is "used to redact Message `body` text". Sending `body: ""` (empty string, **not** `null` — `null` means "don't change") clears the stored body at the provider while the message record (SID, status, to/from, timestamps, price) survives.
- **`DeleteMessage` is NOT the redaction path — it removes the whole record.** `DeleteMessage(string accountSid, string sid, …)` returns `void`/`Task` and deletes the Message resource (SID and outcome gone). Use it only when the entire record should disappear; for "content disposed, outcome preserved," use the empty-body `UpdateMessage` above.
- **Returns (UpdateMessage):** `ApiV2010AccountMessage` (with `Body` now empty). **Error:** `SdkException<RawError>` — **Case B**. **Host:** messaging (`Default`).

### 2.8 Item 8 — Idempotent resend  (source-verified: `Core/RequestOptions.cs`; map: `operations/Api20100401Message.md`)

- **The SDK exposes NO idempotency mechanism on message create.** `CreateMessage` has **no** `Idempotency-Key` (or any idempotency) parameter — confirmed from its full signature (§2.2). The only per-call options object, `RequestOptions`, is `sealed record RequestOptions { public LogLevel? LogLevel { get; init; } }` (verified in SDK source) — it carries **only** a log level and **cannot** attach a custom request header. There is therefore no supported way to send an `Idempotency-Key` header through this SDK.
- **Consequence for implementation:** dedupe **in your own app**. Persist the caller-supplied idempotency key with the resulting message `Sid`/outcome; on a repeat under the same key, return the stored result instead of calling `CreateMessage` again. Do not invent a header the SDK will silently drop.

### 2.9 Enum value tables (only those in scope)  (map: `models/enums.md`)

**`MessageEnumStatus`** (namespace `TwilioSdk.Models.Enums`; `StringEnum` — build via member `MessageEnumStatus.Queued` or `MessageEnumStatus.FromValue("queued")`, never a C# enum):

| C# member | wire |
|---|---|
| `Queued` | `queued` |
| `Sending` | `sending` |
| `Sent` | `sent` |
| `Failed` | `failed` |
| `Delivered` | `delivered` |
| `Undelivered` | `undelivered` |
| `Receiving` | `receiving` |
| `Received` | `received` |
| `Accepted` | `accepted` |
| `Scheduled` | `scheduled` |
| `Read` | `read` |
| `PartiallyDelivered` | `partially_delivered` |
| `Canceled` | `canceled` |

**`MessageEnumUpdateStatus`** (the `status` on `UpdateMessage`): single member `Canceled` → wire `canceled`.

**`MessageEnumScheduleType`** (the `scheduleType` on `CreateMessage`): single member `Fixed` → wire `fixed`.

**`MessageEnumDirection`** (on `ApiV2010AccountMessage.Direction`): `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)`.

---

## 3. Trap notes (attach at the step where each bites)

> ⚠ Step 0 (client registration) — the `HttpClient`/handler pipeline must be long-lived and reused, not rebuilt per request, and the SDK client's lifetime relative to it is not obvious from the constructor. **MUST load `dotnet-client-initialization`** before writing `AddTwilioSdkClient` / `new TwilioSdkClient(...)`.

> ⚠ Step 0 (auth) — how and *when* credentials must be set relative to client construction, and loading secrets from configuration rather than hardcoding, are not visible in the property signature. **MUST load `dotnet-authentication`** before wiring `AccountSidAuthToken`.

> ⚠ Step 0 (resilience / base URL) — the SDK's `RetryOptions.Timeout` and retry settings do **not** bound a whole call and are **not** the `HttpClient` timeout; and whether a failed write (`CreateMessage`) can be re-sent by the retry layer is not something the option names reveal (this directly interacts with the idempotency requirement in item 8). **MUST load `dotnet-configuration-resilience`** before tuning retries/timeouts or setting `options.Server.*.BaseUrl`.

> ⚠ Steps 1–7 (every call) — optional params have no C# default and mis-bind in a positional call; enums are `StringEnum<T>` (not C# enums) and unmodeled JSON is dropped on deserialize. **MUST load `dotnet-calling-endpoints`** (named-argument calling) and **`dotnet-models`** (enum/`StringEnum` construction, wire-name mapping, nullability) before the first `CreateMessage`/`ListMessage` call.

> ⚠ Steps 1–8 (error boundary) — every operation here is **Case B** (`SdkException<RawError>`, no typed accessors); reading the provider status and error `code`/`message` safely, and the `JsonException` hazards below, are not something the signature shows. **MUST load `dotnet-error-handling`** before writing any `try/catch` around an SDK call.

> ⚠ Step 6 (testing the integration) — which seam to fake (the `HttpClient`) and how to cover the error/pagination paths is not inferable from the SDK surface. **MUST load `dotnet-testing`** before writing tests.

---

## 4. REQUIRED READING (load BEFORE implementation starts)

This sheet deliberately does **not** carry these skills' contents — load each one at the step it governs:

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 0 — client construction, `HttpClient` lifetime, `AddTwilioSdkClient` DI registration |
| `dotnet-authentication` | Step 0 — supplying `BasicAuthCredentials`, when to set them, secrets from config |
| `dotnet-configuration-resilience` | Step 0 — retries/backoff, what `Timeout` bounds, per-host base URL, whether writes are re-sent, manual pagination tuning |
| `dotnet-calling-endpoints` | Steps 1–7 — named-argument calling, required vs optional params, async/`ct` |
| `dotnet-models` | Steps 1–7 — building request values, `StringEnum` construction, wire-name mapping, nullability |
| `dotnet-error-handling` | Steps 1–8 — the Case B error boundary, reading status + provider `code`/`message` |
| `dotnet-testing` | Tests — faking the `HttpClient` seam, error/pagination coverage |

**Two mandatory `System.Text.Json.JsonException` hazards for the error boundary** — this exception reaches the boundary from two directions and they need opposite handling:

- A drifted or malformed **2xx** body (e.g. a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. The message/lookup response records here are almost entirely nullable (`string?`), which reduces but does not eliminate this; catch `JsonException` at the boundary regardless.
- A **non-2xx** body that does not match its operation's generated error shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection (e.g. an invalid `To`, an already-sent cancel) as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

- **Item 8 (idempotency) — resolved, not a blocker but a design constraint:** the SDK offers no idempotency mechanism on `CreateMessage` (no header hook; `RequestOptions` carries only `LogLevel`). Dedupe must be implemented app-side (persist key → `Sid`/outcome). Stated so the implementer does not search for an SDK feature that does not exist.
- **Assumption:** "validate/usable" for item 1 is read from `LookupResponse.Valid == true`, and the canonical E.164 is `LookupResponse.PhoneNumber`. The Lookup V2 default (no `fields`) returns validity + formatting without add-on data packages; this is sufficient for reject-and-canonicalize. If the business also needs line-type/reachability gating, that requires passing `fields` (e.g. line-type-intelligence) — flag if in scope.
- **Assumption:** `Twilio:BaseUrl` maps to `options.Server.Default.Production.BaseUrl` (messaging/api host) and must never be applied to `Default4` (lookups). Confirmed against SDK source.
- **`UNVERIFIED` (live-only) facts, converted to defensive directives on the sheet, not open lookups:**
  - the exact scheduling send-at window (item 3) — rely on provider rejection via Case B, do not pre-validate from a memorized window;
  - the exact provider status/error code returned when cancelling an already-sent message (item 4) — read `StatusCode` + body `code`/`message` and branch defensively;
  - whether the live wire payload for `ApiV2010AccountMessage`/`LookupResponse` exactly matches these generated (all-nullable) models — extract each field best-effort and fall back to the generic path (catch `JsonException` at the boundary) rather than assuming presence.
- No other blockers: every operation the scope requires exists in the SDK and is grounded above.
