# Twilio .NET SDK — SMS Order-Notifications Integration Plan (eShopOnWeb `src/PublicApi`)

SDK: `AsadAli.TwilioSdk` (root namespace `TwilioSdk`, APIMatic-generated). Client `TwilioSdkClient`,
options `TwilioSdkClientOptions`. Target of this plan: SMS order-notification capabilities (validate
number, send, schedule/cancel, poll status, reconcile, redact). Every fact below is grounded in the
bundled SDK map (map page cited per row); the base-URL override contract was resolved from the SDK
source (files named inline). Install version-less: `dotnet add package AsadAli.TwilioSdk`.

---

## 1. Scope & sequence

1. **Client & DI setup** — register `TwilioSdkClient` in `src/PublicApi` DI via `AddTwilioSdkClient`,
   wiring HTTP basic auth and the messaging base-URL override from config (`Twilio:*`). (Step governs
   capabilities 1.)
2. **Validate + canonicalize number (Lookup)** — `client.LookupsV2PhoneNumber.FetchPhoneNumber3`
   before persisting a shopper's mobile; store the canonical E.164 `PhoneNumber` and gate on `Valid`.
   (Capability 2.)
3. **Send SMS** — `client.Api20100401Message.CreateMessage`. (Capability 3.)
4. **Schedule / cancel** — `CreateMessage` with `scheduleType`/`sendAt` + `messagingServiceSid`;
   cancel via `UpdateMessage` status=`canceled`. (Capability 4.)
5. **Fetch single delivery status** — `client.Api20100401Message.FetchMessage`. (Capability 5.)
6. **List for reconciliation** — `client.Api20100401Message.ListMessage` filtered by `from` +
   date-sent bounds. (Capability 6.)
7. **Redact / delete content** — `UpdateMessage` with empty `body` (record survives) or
   `DeleteMessage` (whole resource removed). (Capability 7.)
8. **Error boundary** — one catch layer around every SDK call (`SdkException<RawError>` — all these
   ops are Case B). (Capability 8.)

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

### 2a. Namespaces (`using` directives) — one per type kind

| Type(s) | Namespace | Source |
|---|---|---|
| `TwilioSdkClient`, `TwilioSdkClientOptions`, `ServerOptions` | `TwilioSdk` | root (`TwilioSdkClient.cs`, `ServerOptions.cs`) |
| Controllers (`Api20100401Message`, `LookupsV2PhoneNumber`) | `TwilioSdk.Api` | `Api/` |
| Records (`ApiV2010AccountMessage`, `ListMessageResponse`, `LookupResponse`) | `TwilioSdk.Models` | `Models/` |
| Enums (`MessageEnumStatus`, `MessageEnumScheduleType`, `MessageEnumUpdateStatus`) | `TwilioSdk.Models.Enums` | `Models/Enums/` |
| `BasicAuthCredentials` | `TwilioSdk.Core.Authentication.Basic` | `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| `ServerEnvironment` | `TwilioSdk.Servers` | `Servers/ServerEnvironment.cs` |
| `SdkException<T>` | `TwilioSdk.Core.Exceptions` | `Core/Exceptions/SdkException.cs` |
| `RawError` | `TwilioSdk.Core.ErrorResponse` | `Core/ErrorResponse/RawError.cs` |

Note: C# does not import child namespaces transitively — `using TwilioSdk.Models;` alone does not
make the enums or error types visible. Add each `using` above separately.

### 2b. Client construction, auth, servers (capability 1)

*(from `sdk-map.md` → "Getting a client" / "Servers & auth", and SDK source `ServerOptions.cs`,
`Servers/DefaultOptions.cs`, `Servers/Default4Options.cs`, `Core/Authentication/Basic/BasicAuthCredentials.cs`)*

- **Constructor**: `new TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)`.
- **DI**: `services.AddTwilioSdkClient(o => { /* set o.AccountSidAuthToken, o.Server, o.Environment */ });`
  (`ServiceCollectionExtensions.cs`).
- **Auth (HTTP basic)** — `TwilioSdkClientOptions.AccountSidAuthToken` is `BasicAuthCredentials?`.
  `BasicAuthCredentials` has two `required` init members: `Username` (string) and `Password` (string).
  For Account SID + Auth Token: `Username` = Account SID, `Password` = Auth Token.
  ```
  o.AccountSidAuthToken = new BasicAuthCredentials { Username = accountSid, Password = authToken };
  ```
- **Environment**: `TwilioSdkClientOptions.Environment` is `ServerEnvironment`; only member is
  `ServerEnvironment.Production`. `ServerEnvironment.Default()` returns `Production`.
- **Base-URL override — messaging ONLY (config key `Twilio:BaseUrl`).**
  `TwilioSdkClientOptions.Server` is `ServerOptions` (root namespace). `ServerOptions` exposes one
  property per named server node. The two relevant nodes:
  - `Server.Default` (type `DefaultOptions`) → the **"api" / messaging** host used by every
    `Api20100401Message` op. Override: `o.Server.Default.Production.BaseUrl = config["Twilio:BaseUrl"];`
    (default value in source: `https://api.twilio.com`).
  - `Server.Default4` (type `Default4Options`) → the **"lookups"** host used by
    `LookupsV2PhoneNumber` (default `https://lookups.twilio.com`). **Do NOT set `Twilio:BaseUrl` on
    this node** — Lookup is a different host; leave `Server.Default4` at its default.
  Each node's shape is `.Production.BaseUrl` (a `ProductionOptions { string BaseUrl }`).

### 2c. Operations

| # | Controller.Method (signature, params in order) | Request fields (wire ← C#) | Response envelope + fields read | Error case | Pagination | Map page |
|---|---|---|---|---|---|---|
| 3 Send | `client.Api20100401Message.CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `To`←`to` (required, positional), `From`←`from`, `MessagingServiceSid`←`messagingServiceSid`, `Body`←`body`. The 24 params `statusCallback`…`contentSid` are nullable **with no default → must pass explicitly** (`null` to skip). Supply **either** `from` **or** `messagingServiceSid` (not both); pass the other as `null`. | Returns `ApiV2010AccountMessage`. Read: `Sid (sid): string?` (message SID), `Status (status): MessageEnumStatus?`, `ErrorCode (error_code): int?`, `ErrorMessage (error_message): string?`. | `SdkException<RawError>` — **Case B** | none | operations/Api20100401Message.md · records-1-Ac-Ca.md |
| 4a Schedule | same `CreateMessage` as above | To schedule: set `scheduleType` = `MessageEnumScheduleType.Fixed` (wire `fixed`) **and** `sendAt` (`DateTimeOffset`) **and** `messagingServiceSid` (scheduling is **Messaging-Services-only** — `from` cannot be used to schedule; per enum doc). `body`/`to` as normal. | `ApiV2010AccountMessage`; scheduled message returns `Status` = `MessageEnumStatus.Scheduled` (wire `scheduled`). | Case B | none | operations/Api20100401Message.md · enums.md (MessageEnumScheduleType) |
| 4b Cancel | `client.Api20100401Message.UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` | To cancel a still-scheduled message: `sid` = the scheduled message SID; `status` = `MessageEnumUpdateStatus.Canceled` (wire `canceled`); pass `body: null`. (`body` and `status` are nullable-no-default → must pass explicitly.) | Returns `ApiV2010AccountMessage`; `Status` becomes `MessageEnumStatus.Canceled`. | Case B | none | operations/Api20100401Message.md · enums.md (MessageEnumUpdateStatus) |
| 5 Fetch status | `client.Api20100401Message.FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid`, `sid` (path). | Returns `ApiV2010AccountMessage`. Read `Status (status): MessageEnumStatus?`, `ErrorCode (error_code): int?`, `ErrorMessage (error_message): string?`, `DateSent (date_sent): string?`. | Case B | none | operations/Api20100401Message.md · records-1-Ac-Ca.md |
| 6 List | `client.Api20100401Message.ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` | Filter **by the From number in the request**: `from` → wire `From`. Date-range: `dateSentQuery`→wire `DateSent<` (on/before, upper bound), `dateSentQueryQuery`→wire `DateSent>` (on/after, lower bound); `dateSent`→exact `DateSent`. `pageSize`→`PageSize`, `page`→`Page`, `pageToken`→`PageToken`. All 8 nullable-no-default → pass explicitly. | Returns `ListMessageResponse` envelope: `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?` (the page's items), plus `NextPageUri (next_page_uri): string?`, `PreviousPageUri`, `FirstPageUri`, `Uri`, `Page (page): int?`, `PageSize (page_size): int?`, `Start`, `End`. | Case B | Map: **none** helper — manual paging via `page`/`pageToken`/`NextPageUri`; there is no `perPage`/auto-pager. | operations/Api20100401Message.md · records-4-Li-Me.md |
| 7a Redact body | `UpdateMessage(... string? body, MessageEnumUpdateStatus? status ...)` (same as 4b) | Redact: `sid` = target; `body` = `""` (empty string) → wire `Body`; `status: null`. Map note: UpdateMessage "used to redact Message `body` text". | Returns `ApiV2010AccountMessage`; the resource **survives** — `Sid`, `Status`, `ErrorCode`, dates, etc. remain; only `Body` is emptied at the provider. | Case B | none | operations/Api20100401Message.md |
| 7b Delete resource | `client.Api20100401Message.DeleteMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid`, `sid` (path). | Returns `void` (Task). Removes the **entire** Message resource (record no longer retrievable). Use 7a (redact) when the record + status must survive; use 7b only to remove the whole record. | Case B | none | operations/Api20100401Message.md |
| 2 Lookup | `client.LookupsV2PhoneNumber.FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `phoneNumber` (path, the number to validate). Optional `countryCode` (wire `CountryCode`) for national-format input. 15 params `fields`…`partnerSubId` nullable-no-default → pass explicitly (`null` to skip). | Returns `LookupResponse`. Canonical E.164 lives in `PhoneNumber (phone_number): string?`; validity in `Valid (valid): bool?`; failures in `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?`; also `CallingCountryCode`, `CountryCode`, `NationalFormat`. **Served from the lookups host (`Server.Default4`), NOT the messaging host** — do not apply `Twilio:BaseUrl` to it. | `SdkException<RawError>` — **Case B** | none | operations/LookupsV2PhoneNumber.md · records-4-Li-Me.md |

### 2d. Enum value tables (literal C# member ← wire value)

`MessageEnumStatus` (response `Status`; `Models/Enums/MessageEnumStatus.cs`):
`Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`,
`Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`,
`Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`,
`Canceled (canceled)`.

`MessageEnumScheduleType` (`CreateMessage.scheduleType`): only `Fixed (fixed)`.

`MessageEnumUpdateStatus` (`UpdateMessage.status`): only `Canceled (canceled)`.

Enums are `StringEnum<T>`, **not** C# enums. Build via the static member (`MessageEnumScheduleType.Fixed`)
or `MessageEnumScheduleType.FromValue("fixed")` — never `MessageEnumScheduleType.fixed`. (See
`TwilioSdk.Models.Enums`.) *(map: enums.md)*

### 2e. Error surface (capability 8) — all in-scope ops are Case B

Every operation used here throws `SdkException<RawError>` (Case B — no typed `{Op}Error`, no
`TryGet…` accessors). Read status + body via the `RawError` accessors:
`StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` ·
`ReadAsBytes(): ReadOnlyMemory<byte>`. The Twilio error `code`/`message` live inside the JSON body
(read via `ReadAsJson<T>()` / `ReadAsString()`), not as first-class members of `RawError`. No
operation here has a no-throw `…Result` variant. *(map: sdk-map.md "Error-handling model";
per-op rows)*

---

## 3. Trap notes (load the named skill before writing that step's code)

⚠ Step 1 (client & DI) — the `HttpClient`/handler pipeline the SDK client wraps has lifetime and
reuse rules the constructor signature does not reveal (long-lived vs per-request, `IHttpClientFactory`).
Getting this wrong causes socket exhaustion or stale DNS. **MUST load `dotnet-client-initialization`**
before wiring `AddTwilioSdkClient` / building options.

⚠ Step 1 (auth) — how and *when* credentials must be attached to options (before construction vs in
the DI callback) and how secrets should be sourced is not visible in the `AccountSidAuthToken`
property alone. **MUST load `dotnet-authentication`** before setting the credentials.

⚠ Step 1 (resilience / base URL / retries) — the SDK retry/timeout options do **not** bound a whole
call and are **not** the timeout on the registered `HttpClient`; and whether a failed non-idempotent
write (`CreateMessage` POST) can be re-sent on a transport failure is not what the option names
suggest. Base-URL/server selection interacts with retries here too. **MUST load
`dotnet-configuration-resilience`** before tuning retries/timeouts or finalizing the base-URL wiring.

⚠ Steps 2–7 (calling ops) — many optional params on `CreateMessage`/`ListMessage`/`FetchPhoneNumber3`
have **no C# default** and mis-bind in a positional call; whether an argument should be omitted vs
passed as `null` matters. **MUST load `dotnet-calling-endpoints`** before writing the first call.

⚠ Steps 2–7 (models & enums) — `Status`, `ScheduleType`, etc. are `StringEnum<T>` (not C# enums),
unions read via `TryGet…`, and unmodeled JSON fields are dropped on deserialize — so how to
build/read these payloads safely is not obvious from the field types. **MUST load `dotnet-models`**
before constructing request payloads or mapping responses onto domain types.

⚠ Step 6 (pagination) — the map marks these list ops as having no auto-pager; how to walk pages
correctly (page vs pageToken vs `NextPageUri`) and where the loop terminates is a usage decision the
signature does not settle. **MUST load `dotnet-calling-endpoints`** (pagination section) before
writing the reconciliation loop.

⚠ Step 8 (error boundary) — which exception types actually reach the catch, and how to read status +
Twilio error code/body without destroying information, is exactly what a signature cannot show.
**MUST load `dotnet-error-handling`** before writing any try/catch (see mandatory rows in REQUIRED
READING below).

⚠ Testing — the `HttpClient` constructor argument is the test seam; matching the project's existing
test framework/assertion style matters. **MUST load `dotnet-testing`** before stubbing the SDK.

---

## 4. REQUIRED READING (load BEFORE implementation starts — this sheet deliberately omits their contents)

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, HttpClient ownership/lifetime, DI registration |
| `dotnet-authentication` | Step 1 — supplying `BasicAuthCredentials` (Account SID / Auth Token), when/where to set them, secret sourcing |
| `dotnet-configuration-resilience` | Step 1 — retries/timeouts/backoff semantics, base-URL/server selection, pagination options |
| `dotnet-calling-endpoints` | Steps 2–7 — required vs optional params, named-argument binding, async/`ct`, pagination loop |
| `dotnet-models` | Steps 2–7 — `StringEnum<T>` enums, required/nullable members, wire-vs-C# names, dropped-field behavior |
| `dotnet-error-handling` | Step 8 — which exceptions reach the catch, reading status/body safely, catch-ladder traps |
| `dotnet-testing` | Tests — the HttpClient test seam, error/edge coverage |

**Two mandatory hazard rows — `System.Text.Json.JsonException` reaches the error boundary from two
directions and each needs opposite handling:**

- A drifted or malformed **2xx** body (e.g. a missing `required` member) surfaces as a
  `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so a
  catch ladder that only catches `SdkException<…>` lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated error shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps
  every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller
  that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions**
- Plan written to the dictated path `C:\claude-runs\t4v4ali-task4-plugin-opus48high-024\repo\twilio-plan.md`.
- Auth is Account SID + Auth Token mapped to `BasicAuthCredentials { Username = AccountSid,
  Password = AuthToken }` (per the SDK's own XML doc, API-key/secret is preferred over SID/token
  for anything beyond local testing — flagged for the implementer, not a blocker).
- The integration sends via a single outbound `From` number (or one `MessagingServiceSid`); the
  reconciliation list filters by that same `From`. Scheduling uses a `MessagingServiceSid`
  (required for `scheduleType=fixed`), which the account must have provisioned.
- `Twilio:BaseUrl` config key overrides **only** the messaging (`Server.Default`) host; Lookup
  (`Server.Default4`) is intentionally left at its own default host.

**Blockers / GAPS**
- **No GAPS.** All eight capabilities are exposed by the bundled SDK map: Lookup
  (`LookupsV2PhoneNumber.FetchPhoneNumber3`, capability 2) is present, and send / schedule / cancel /
  fetch / list / redact / delete are all on `Api20100401Message`. The base-URL override mechanism
  (`ServerOptions` per-node `.Production.BaseUrl`) was the only fact the map named-but-did-not-detail;
  it was resolved from SDK source and is fully specified in §2b.

**UNVERIFIED (live-wire only — code defensively):**
- The exact JSON shape of the Twilio error body inside `RawError` (fields `code`/`message`/`status`)
  can be confirmed only against live traffic. Directive: in the catch layer, read
  `RawError.StatusCode` for the HTTP status and best-effort extract `code`/`message` via
  `ReadAsJson<T>()`; **fall back to `ReadAsString()` / a generic message** if the shape does not
  match. Do not assume typed accessors — these ops are Case B (`RawError`), which has none.
- Whether redaction via `UpdateMessage(body: "")` versus `body: null` is what the provider treats as
  "empty the body" is a live-wire behavior; the map documents UpdateMessage as the redaction path.
  Directive: send an **empty string** (`""`) to redact, treat a null/omitted `body` as "leave
  unchanged", and confirm against a live send that the stored body is cleared while the record and
  `Status` survive.
