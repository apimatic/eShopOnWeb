# Twilio .NET SDK Integration Plan — eShopOnWeb `src/PublicApi`

SDK: `AsadAli.TwilioSdk` (APIMatic-generated), root namespace `TwilioSdk`, client `TwilioSdkClient`.
Install version-less: `dotnet add package AsadAli.TwilioSdk` into `src/PublicApi`.
All contract facts below are grounded in the bundled SDK map (map page cited per row). Where a fact
is settleable only by live provider traffic it is labelled **UNVERIFIED** with a defensive-coding
directive — implement the directive, do not wait to confirm.

---

## 1. Scope & sequence

1. **Client & DI registration** — register one long-lived `TwilioSdkClient` bound to the `Twilio:`
   config section; set Basic auth (AccountSid/AuthToken) and the messaging base-URL override. Uses
   no operation. (`dotnet-client-initialization`, `dotnet-authentication`, `dotnet-configuration-resilience`)
2. **Flow 1 — register a contact number**: validate + canonicalize a raw phone string.
   Op: `LookupsV2PhoneNumber.FetchPhoneNumber3`.
3. **Send SMS** (order placed / dispatched / cancelled): `Api20100401Message.CreateMessage`
   (explicit `from` variant AND `messagingServiceSid` variant).
4. **Schedule a follow-up** for future send: `Api20100401Message.CreateMessage` with
   `scheduleType` + `sendAt` + `messagingServiceSid`.
5. **Cancel a scheduled follow-up**: `Api20100401Message.UpdateMessage` (status → canceled).
6. **Fetch a message's status by SID**: `Api20100401Message.FetchMessage`.
7. **Flow 3 — content disposal** (redact body, keep send-record): `Api20100401Message.UpdateMessage`
   (body → `""`). Whole-record delete is `DeleteMessage`.
8. **Flow 3 — reconciliation list**: `Api20100401Message.ListMessage` filtered server-side by
   `from` + DateSent range.
9. **Error boundary** around every SDK call so a send failure never throws out of the request path.

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier.
> The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one
> from that type's own map row, never from where a neighbouring type sits. Enums, unions, auth,
> server and client-config types are spread across different child namespaces, and two types
> configured side by side in the same options object routinely live in different ones. Dropping a
> type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### 2.0 Namespaces (`using` directives — one per type kind)

| Type(s) | Namespace | Source basis |
|---|---|---|
| `TwilioSdkClient`, `TwilioSdkClientOptions`, `ServerOptions` | `TwilioSdk` | map: *Getting a client*; `ServerOptions.cs` at repo root |
| `AddTwilioSdkClient` (DI extension) | `TwilioSdk` (extension on `IServiceCollection`) | map: `ServiceCollectionExtensions.cs` |
| `BasicAuthCredentials` | `TwilioSdk.Core.Authentication.Basic` | source `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| `ServerEnvironment`, `DefaultOptions`, `Default4Options` | `TwilioSdk.Servers` | source `Servers/*.cs` |
| `RetryOptions`, `LoggingOptions` | `TwilioSdk.Core.Configuration` | map row source `Core/Configuration/RetryOptions.cs` |
| Operation controllers (reached via `client.X` properties) | `TwilioSdk.Api` | map: *Namespaces* table |
| Response/request records (`ApiV2010AccountMessage`, `ListMessageResponse`, `LookupResponse`) | `TwilioSdk.Models` | map: *Namespaces* table |
| Enums (`MessageEnumStatus`, `MessageEnumScheduleType`, `MessageEnumUpdateStatus`, `ValidationError`) | `TwilioSdk.Models.Enums` | map: *Namespaces* table |
| `SdkException<T>` | `TwilioSdk.Core.Exceptions` | map row source `Core/Exceptions/SdkException.cs` |
| `RawError` | `TwilioSdk.Core.ErrorResponse` | map row source `Core/ErrorResponse/RawError.cs` |

### 2.1 Client construction & auth (Capability 1)

- **Constructor**: `new TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)`
  — `sdk-map.md` *Getting a client*. HttpClient is passed in (the SDK does not own it → see trap ⚠A).
- **DI**: `services.AddTwilioSdkClient(o => { … })` — configure `o` (the options) in the callback.
- **`TwilioSdkClientOptions` members** (`sdk-map.md` *client-options*):

  | Property | Type |
  |---|---|
  | `AccountSidAuthToken` | `BasicAuthCredentials?` |
  | `Environment` | `ServerEnvironment` |
  | `Server` | `ServerOptions` |
  | `Retry` | `RetryOptions` |
  | `Logging` | `LoggingOptions` |

- **Basic auth** (`sdk-map.md` *Servers & auth*; source `BasicAuthCredentials.cs`): set
  `options.AccountSidAuthToken = new BasicAuthCredentials { Username = <Twilio:AccountSid>, Password = <Twilio:AuthToken> }`.
  Both members are `required`. (`Username`/`Password` are the literal property names; AccountSid→Username, AuthToken→Password.)
- **Environment**: `options.Environment` is `ServerEnvironment` with a single member
  `ServerEnvironment.Production` (`sdk-map.md` *Servers & auth*). There is no non-prod environment.
- **Base-URL override — MESSAGING host only** (resolved from source `Servers/DefaultOptions.cs`,
  `Servers/Default4Options.cs`, `ServerOptions.cs`; the map named `options.Server`/`Servers/` as the
  override point but did not carry the member shape):
  - Messaging API (`Api20100401Message`, server group **"Default (api)"**) base address =
    `options.Server.Default.Production.BaseUrl` (default `https://api.twilio.com`).
    **When `Twilio:BaseUrl` is set, assign it verbatim here.**
  - Lookup API (`LookupsV2PhoneNumber`, server group **"Default4 (lookups)"**) base address =
    `options.Server.Default4.Production.BaseUrl` (default `https://lookups.twilio.com`) — a
    **separate** property. Leave it at its default; **`Twilio:BaseUrl` must NOT touch it.** This is
    exactly why the two hosts are independent: they are distinct properties on `ServerOptions`, so
    overriding one has no effect on the other. (`ProductionOptions.BaseUrl` is the literal path on
    each; `DefaultOptions`/`Default4Options` live in `TwilioSdk.Servers`, `ServerOptions` at root.)

### 2.2 Phone validation + canonicalization (Capability 2) — `map/operations/LookupsV2PhoneNumber.md`

- **Call**: `client.LookupsV2PhoneNumber.FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - Only `phoneNumber` is required (path param). The 15 params `fields…partnerSubId` are nullable
    with **no default → pass `null` explicitly** (use named args). For plain validity+canonicalization
    pass `fields: null` (basic lookup).
- **HTTP**: `GET /v2/PhoneNumbers/{PhoneNumber}` on the **lookups** host (governed by
  `Server.Default4`, NOT `Twilio:BaseUrl`).
- **Returns**: `LookupResponse` (`map/models/records-4-Li-Me.md`). Fields the integration reads:
  - `PhoneNumber (phone_number): string?` — **the provider's canonical E.164 form.**
  - `Valid (valid): bool?` — **usability/validity flag.**
  - `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?` — reasons when invalid;
    `ValidationError` is a **StringEnum** with values `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`,
    `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`,
    `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)` (`map/models/enums.md`).
  - (also present: `CountryCode`, `CallingCountryCode`, `NationalFormat`, `Url` — not needed.)
- **Error**: `SdkException<RawError>` — **Case B** (no typed accessors).
- **"Not a usable number" — how it surfaces**: a syntactically-recognised-but-invalid number is
  reported on a **200** with `Valid == false` (read `Valid`, do not rely on an exception). A
  **UNVERIFIED** point (live-wire only): a badly malformed input may instead throw
  `SdkException<RawError>` (e.g. 404). **Defensive directive**: treat *either* `Valid == false`
  *or* a caught `SdkException<RawError>` from this call as "not a usable destination"; only accept
  the number when the call returns and `Valid == true`, and canonicalize from `PhoneNumber`.

### 2.3 Send SMS (Capability 3) — `map/operations/Api20100401Message.md`

- **Call**: `client.Api20100401Message.CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - Required (non-nullable): `accountSid` (= `Twilio:AccountSid`), `to`.
  - The **24** params `statusCallback…contentSid` are nullable with **no default → every one must be
    passed explicitly** (`null` to skip). Use named arguments (see trap ⚠C).
  - Body text → `body:`.
  - **Variant A (explicit From)**: `from: <Twilio:FromNumber>`, `messagingServiceSid: null`.
  - **Variant B (Messaging Service)**: `messagingServiceSid: <Twilio:MessagingServiceSid>`, `from: null`.
- **HTTP**: `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` (messaging host — honours `Twilio:BaseUrl`).
- **Returns**: `ApiV2010AccountMessage` (`map/models/records-1-Ac-Ca.md`). Fields read:
  - `Sid (sid): string?` — **message SID.**
  - `Status (status): MessageEnumStatus?` — **delivery status** (values below).
  - also `ErrorCode (error_code): int?`, `ErrorMessage (error_message): string?`, `To`, `From`,
    `Body`, `DateSent (date_sent): string?`, `NumSegments`, `Price`, `MessagingServiceSid`.
- **Error**: `SdkException<RawError>` — **Case B**.
- **`MessageEnumStatus` values** (`map/models/enums.md`) — `StringEnum`, literal C# members:
  `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`,
  `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`,
  `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)`.

### 2.4 Schedule a future message (Capability 4) — `map/operations/Api20100401Message.md`

Same `CreateMessage` op; scheduling is three params on it:
- `scheduleType: MessageEnumScheduleType.Fixed` — `MessageEnumScheduleType` is a `StringEnum` whose
  **only** member is `Fixed (fixed)` (`map/models/enums.md`). Its map note: *"For Messaging Services
  only … in conjunction with the send_time parameter in order to schedule a Message."*
- `sendAt: DateTimeOffset?` — the future send time (wire `SendAt`).
- `messagingServiceSid: <Twilio:MessagingServiceSid>` — **scheduling requires the Messaging Service**
  (the enum note above states "For Messaging Services only"); pass `from: null` in this variant.
- **UNVERIFIED** (live/provider constraint, not in the contract): the allowed `sendAt` window
  (Twilio documents ~15 min to 7 days ahead) is enforced by the provider, not the SDK. **Defensive
  directive**: wrap the scheduled `CreateMessage` in the error boundary (§2.9) and surface a rejected
  schedule as a handled failure rather than letting it throw out of the request path.

### 2.5 Cancel a scheduled message (Capability 5) — `map/operations/Api20100401Message.md`

- **Call**: `client.Api20100401Message.UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - To cancel: `body: null`, `status: MessageEnumUpdateStatus.Canceled`.
  - `MessageEnumUpdateStatus` is a `StringEnum` with the single member `Canceled (canceled)`
    (`map/models/enums.md`).
- **HTTP**: `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json`. Map note on `UpdateMessage`:
  *"used to redact Message body text and to cancel not-yet-sent messages."*
- **Returns**: `ApiV2010AccountMessage`. **Error**: `SdkException<RawError>` — **Case B**.
- **UNVERIFIED** (provider behavior): what makes a message cancelable — it must be a
  `scheduled`/not-yet-sent message; cancelling an already-sent one is rejected by the provider.
  **Defensive directive**: only attempt cancel while the tracked status is `Scheduled`, and treat a
  `SdkException<RawError>` from this call as "already sent / not cancelable" rather than an outage.

### 2.6 Fetch message status by SID (Capability 6) — `map/operations/Api20100401Message.md`

- **Call**: `client.Api20100401Message.FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)`.
- **HTTP**: `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json`.
- **Returns**: `ApiV2010AccountMessage`; read `Status (status): MessageEnumStatus?` (+ `ErrorCode`,
  `ErrorMessage`). **Error**: `SdkException<RawError>` — **Case B**.

### 2.7 Content disposal — redact vs delete (Capability 7) — `map/operations/Api20100401Message.md`

- **Redact body only (record + status survive)** → `UpdateMessage(accountSid, sid, body: "", status: null)`.
  The map's `UpdateMessage` note explicitly lists redacting the body text as its purpose; passing an
  empty `body` erases the text while the Message resource (and its `Status`) remains fetchable. **This
  is the operation that satisfies "text no longer retrievable but send-record + status survive."**
- **Delete the whole resource** → `DeleteMessage(accountSid, sid, …)` → returns `void`
  (`DELETE /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json`). This removes the entire record —
  **do NOT use it for Flow 3** (it destroys the send-record too).
- Both are **Case B** (`SdkException<RawError>`).
- **UNVERIFIED** (provider guarantee, live-wire only): that the provider actually purges the body text
  from its stores after a `body:""` update. **Defensive directive**: treat the successful
  `UpdateMessage` response as the disposal action of record; if the integration must prove
  non-retrievability, re-`FetchMessage` and assert `Body` is empty, handling any exception via §2.9.

### 2.8 List messages for reconciliation (Capability 8) — `map/operations/Api20100401Message.md`

- **Call**: `client.Api20100401Message.ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 8 optional params `to…pageToken` are nullable, **no default → pass explicitly**.
- **Server-side filtering is supported** (query params, wire ← C#):
  `From` ← `from`, `To` ← `to`, `DateSent` ← `dateSent`,
  **`DateSent<` ← `dateSentQuery`** (on-or-before / upper bound = the range **"to"**),
  **`DateSent>` ← `dateSentQueryQuery`** (on-or-after / lower bound = the range **"from"**),
  `PageSize` ← `pageSize`, `Page` ← `page`, `PageToken` ← `pageToken`.
  → Filter by our sending number with `from: <Twilio:FromNumber>`; set the date range with
  `dateSentQueryQuery:` (range start / after) and `dateSentQuery:` (range end / before). **Do the
  From+date filtering server-side via these params — no client-side scan.** (Confirmed: the operation
  accepts `From`, `DateSent>`, and `DateSent<` as server-side query filters.)
  ⚠ Mind the mapping direction: `dateSentQuery` is the **upper** bound (`DateSent<`) and
  `dateSentQueryQuery` is the **lower** bound (`DateSent>`) — the double-Query name is the *after* bound.
- **Returns**: `ListMessageResponse` (`map/models/records-4-Li-Me.md`):
  `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?` plus paging fields
  `Page (page): int?`, `PageSize (page_size): int?`, `Start`, `End`, `FirstPageUri`,
  `NextPageUri (next_page_uri): string?`, `PreviousPageUri`, `Uri`.
  Each item is `ApiV2010AccountMessage` → read `Sid`, `To`, `From`, `Status`, `DateSent`, `Body`.
- **Pagination**: the map marks this op **"Pagination: none (only `page`, no `perPage`)"** — there is
  no auto-paging helper. Page manually with `page`/`pageSize`, or follow `NextPageUri` from the
  response until null. (How to drive manual paging correctly → `dotnet-configuration-resilience`.)
- **Error**: `SdkException<RawError>` — **Case B**.

### 2.9 Error handling (Capability 9) — `sdk-map.md` *Error-handling model*

- **Every in-scope operation is throw-based and Case B**: it throws
  `SdkException<RawError>` (`TwilioSdk.Core.Exceptions.SdkException<T>` / `TwilioSdk.Core.ErrorResponse.RawError`).
  There are **no** typed `{Operation}Error` accessors for these ops, and **no no-throw `…Result`
  variant** exists anywhere in this SDK (`sdk-map.md` *op-stats*).
- **Reading the failure off the exception** — `RawError` members (`sdk-map.md` *error-core*):
  - `StatusCode: HttpStatusCode` — the HTTP status (distinguish 4xx client/number errors from 5xx/transport).
  - `ReadAsString(): string` — raw body.
  - `ReadAsJson<T>(): T?` — deserialize the Twilio error body (Twilio error `code` + `message`) into a
    shape you supply. (Reading the Twilio error code/message safely, and *when* `ReadAsJson` itself can
    throw, are exactly what the error-handling skill covers — see trap ⚠E.)
- Distinguish "invalid/unreachable number" (Lookup `Valid==false`, or a 4xx from `CreateMessage`) from
  transport errors (5xx / no `SdkException` at all) using `StatusCode`. A send failure must be caught
  and turned into a handled result so it never throws out of the request path.

---

## 3. Trap notes (attach to the step where each bites — load the skill before coding that step)

⚠A **Step 1 (client registration)** — the `HttpClient` passed to `TwilioSdkClient` has a lifetime and
handler-pipeline contract the constructor signature does not show, and getting it wrong causes socket
exhaustion or stale DNS. **MUST load `dotnet-client-initialization`** before wiring the client into DI
or writing the factory.

⚠B **Step 1 (auth)** — how and *when* credentials must be set relative to client construction, and how
to source them from configuration rather than hardcoding, is not visible in the property type. **MUST
load `dotnet-authentication`** before setting `AccountSidAuthToken` (and when a call returns 401/403).

⚠C **Steps 2–8 (every call)** — these operations have many optional parameters with **no C# default**
that must be passed explicitly, and a positional call silently mis-binds them. Whether/how to use named
arguments is the thing the signature cannot enforce. **MUST load `dotnet-calling-endpoints`** before the
first `client.*` call.

⚠D **Steps 2–8 (models)** — the SDK's enums are `StringEnum<T>`, not C# enums, and JSON fields the model
doesn't declare are dropped on deserialize; constructing and reading these correctly is not obvious from
the type name. **MUST load `dotnet-models`** before building request payloads or mapping responses.

⚠E **Step 9 (error boundary)** — which exception types actually reach your catch, how to read the
status/error body without a second exception, and why an `SdkException`-only catch ladder is silently
incomplete are all invisible in the signatures. **MUST load `dotnet-error-handling`** before writing the
try/catch or middleware.

⚠F **Step 1 (retries/timeouts) & Step 3 (send)** — the SDK's retry/timeout options do not bound a whole
call the way they read, and their retry behaviour interacts with non-idempotent writes like
`CreateMessage` in a way the option names hide (so a "send once" can become a re-send), and the
base-URL/pagination knobs have semantics the signature won't show. **MUST load
`dotnet-configuration-resilience`** before tuning retries, timeouts, base URL, or paging.

⚠G **Testing** — the fake seam for this SDK is the `HttpClient` constructor argument, not the client
itself, and asserting real behaviour (not just execution) needs the right approach. **MUST load
`dotnet-testing`** before writing tests for the integration layer.

---

## 4. REQUIRED READING (load BEFORE implementation starts — this sheet deliberately omits their contents)

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, HttpClient lifetime, DI registration |
| `dotnet-authentication` | Step 1 — supplying Basic (AccountSid/AuthToken) credentials, 401/403 |
| `dotnet-configuration-resilience` | Step 1/3/8 — retries, timeouts, base-URL override, manual pagination |
| `dotnet-calling-endpoints` | Steps 2–8 — named arguments, required-vs-optional params, async/ct |
| `dotnet-models` | Steps 2–8 — `StringEnum<T>` enums, required members, response mapping |
| `dotnet-error-handling` | Step 9 — the exception boundary around every SDK call |
| `dotnet-testing` | Tests — the HttpClient seam |

These are to be loaded before implementation starts; the sheet intentionally does not carry their
contents. `dotnet-error-handling` is mandatory because the integration always writes an error boundary.

**Two `System.Text.Json.JsonException` hazards that must shape the error boundary from the first cut
(not a later revision) — they reach the boundary from opposite directions and need opposite handling:**

- A drifted or malformed **2xx** body (e.g. a `required` member missing on `ApiV2010AccountMessage`,
  `LookupResponse`, or `ListMessageResponse`) surfaces as a **`JsonException` from deserialization, NOT
  an `SdkException`** — so a catch ladder that only catches `SdkException<…>` lets it escape the
  integration boundary.
- A **non-2xx** body that does not match its operation's generated error shape throws `JsonException`
  **while the error object is being constructed**, so the `JsonException` **replaces** the
  `SdkException` and the HTTP status is destroyed with it — a boundary that blindly maps every
  `JsonException` to 5xx then reports a deterministic rejection as an outage, and a caller that retries
  5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions**
- `Twilio:AccountSid` is used both as the Basic-auth `Username` and as the `accountSid` path argument on
  every `Api20100401*` message operation (the SDK does not infer it from credentials).
- `Twilio:BaseUrl`, when present, is applied only to `options.Server.Default.Production.BaseUrl` (the
  messaging/api host). The Lookup host (`options.Server.Default4.Production.BaseUrl`) is left at its
  default `https://lookups.twilio.com` per the brief.
- "Send via MessagingServiceSid" and "schedule a message" both use `Twilio:MessagingServiceSid`;
  scheduling additionally leaves `from` null (scheduling is Messaging-Services-only per the SDK enum note).
- Delivery-status tracking reads `ApiV2010AccountMessage.Status`; the app persists SIDs returned from
  `CreateMessage` to later `FetchMessage`, cancel, redact, or reconcile.

**Blockers / Gaps**
- **None.** Every requested capability is exposed by the SDK map: Lookup validation/canonicalization
  (`LookupsV2PhoneNumber.FetchPhoneNumber3`), send/schedule (`CreateMessage`), cancel/redact
  (`UpdateMessage`), fetch (`FetchMessage`), delete (`DeleteMessage`), and server-side From+date list
  filtering (`ListMessage`). The one map-silent detail — how to override the messaging base URL without
  affecting the Lookup host — was resolved from SDK source and is documented in §2.1.
- Items that **only live provider traffic can confirm** are labelled **UNVERIFIED** inline (Lookup
  status code for a malformed number §2.2; `sendAt` scheduling window §2.4; cancelable-message
  precondition §2.5; provider body-purge guarantee §2.7); each carries a defensive-coding directive to
  implement rather than a value to trust.
