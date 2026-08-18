# Twilio .NET SDK integration plan — eShopOnWeb SMS order notifications

SDK: `AsadAli.TwilioSdk` (install version-less: `dotnet add package AsadAli.TwilioSdk`). Root namespace `TwilioSdk`.
Client `TwilioSdkClient`, options `TwilioSdkClientOptions`. Generator: APIMatic. Map source commit `51fdf48`.

All capabilities in scope are reachable through the SDK. There is **one** capability nuance worth flagging up
front: the SMS-send/schedule/cancel/redact/fetch/list operations all live on the classic **`Api20100401Message`**
controller (server node `Default` = `https://api.twilio.com`), and phone-number validation lives on a **separate**
`LookupsV2PhoneNumber` controller (server node `Default4` = `https://lookups.twilio.com`). See Assumptions & Blockers
for what "MESSAGING API base URL" means given this split.

---

## 1. Scope & sequence

1. **Client & DI setup** — register one long-lived `TwilioSdkClient` (see CONTRACT SHEET §Client). Uses ops from every step.
2. **Validate + canonicalize destination number at registration time** — `LookupsV2PhoneNumber.FetchPhoneNumber3`.
3. **Send an order SMS** — `Api20100401Message.CreateMessage` (To + Body + one of From / MessagingServiceSid).
4. **Schedule the follow-up SMS a few days out** — `Api20100401Message.CreateMessage` with `scheduleType` + `sendAt` + `messagingServiceSid`.
5. **Cancel a scheduled follow-up** — `Api20100401Message.UpdateMessage` with `status = Canceled`.
6. **Fetch a single message's delivery status** — `Api20100401Message.FetchMessage`.
7. **Redact a message body at the provider** — `Api20100401Message.UpdateMessage` with `body = ""`.
8. **List messages for reconciliation (FROM + date range)** — `Api20100401Message.ListMessage`.
9. **Error boundary** around every call — all in-scope ops are Case B (`SdkException<RawError>`).

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The
> cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that
> type's own map row, never from where a neighbouring type sits. Enums, unions, auth, server and client-config
> types are spread across different child namespaces, and two types configured side by side in the same options
> object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess
> the wrong `using`, and the build breaks.

### Namespaces (`using` per referenced type)

| Type | Namespace | `using` |
|---|---|---|
| `TwilioSdkClient`, `TwilioSdkClientOptions`, `ServerOptions` | root | `using TwilioSdk;` |
| `ServerEnvironment` | Servers | `using TwilioSdk.Servers;` |
| Controllers `Api20100401Message`, `LookupsV2PhoneNumber` | Api | `using TwilioSdk.Api;` |
| Records `ApiV2010AccountMessage`, `ListMessageResponse`, `LookupResponse` | Models | `using TwilioSdk.Models;` |
| Enums `MessageEnumStatus`, `MessageEnumScheduleType`, `MessageEnumUpdateStatus`, `ValidationError` | Models.Enums | `using TwilioSdk.Models.Enums;` |
| `BasicAuthCredentials` | Core.Authentication.Basic | `using TwilioSdk.Core.Authentication.Basic;` |
| `SdkException<T>` | Core.Exceptions | `using TwilioSdk.Core.Exceptions;` |
| `RawError` | Core.ErrorResponse | `using TwilioSdk.Core.ErrorResponse;` |
| `RetryOptions` | Core.Configuration | `using TwilioSdk.Core.Configuration;` |

Cite: `sdk-map.md` §Namespaces / §Getting a client / §Servers & auth; source-confirmed for `BasicAuthCredentials` (`Core/Authentication/Basic/BasicAuthCredentials.cs`), `ServerOptions` (`ServerOptions.cs`), `SdkException` (`Core/Exceptions/SdkException.cs`), `RawError` (`Core/ErrorResponse/RawError.cs`).

### Operations

All rows: controller accessor `client.Api20100401Message` / `client.LookupsV2PhoneNumber`. Every op is **Case B**
(`SdkException<RawError>`, accessors `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsBytes(): ReadOnlyMemory<byte>` · `ReadAsJson<T>(): T?`). No `…Result` no-throw variant exists on any of them. Async — `await`.

#### 2.1 CreateMessage — send SMS **and** schedule SMS (capabilities 1 & 3) — `operations/Api20100401Message.md`

Signature (params in order; the 24 middle params are nullable but have **no C# default → pass explicitly, `null` to skip**):

```
CreateMessage(
  string accountSid,                              // path {AccountSid}; REQUIRED, positional
  string to,                                       // wire To; REQUIRED, positional (E.164 destination)
  string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback,
  int? attempt, int? validityPeriod, bool? forceDelivery,
  MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention,
  bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType,
  bool? shortenUrls,
  MessageEnumScheduleType? scheduleType,          // wire ScheduleType — set to MessageEnumScheduleType.Fixed to schedule
  DateTimeOffset? sendAt,                          // wire SendAt — when to send (schedule only)
  bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck,
  string? from,                                    // wire From — sender number (Twilio:FromNumber)
  string? fallbackFrom,
  string? messagingServiceSid,                     // wire MessagingServiceSid (Twilio:MessagingServiceSid)
  string? body,                                    // wire Body — message text
  IReadOnlyList<string>? mediaUrl, string? contentSid,
  RequestOptions? requestOptions = null, CancellationToken ct = default)
```

- **HTTP**: `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` (server node `Default` = api.twilio.com).
- **Immediate send**: pass `to`, `body`, and **either** `from:` **or** `messagingServiceSid:` (set the one you use, pass the other `null`). Set all other middle params `null`, `scheduleType: null`, `sendAt: null`.
- **Scheduled send**: `scheduleType: MessageEnumScheduleType.Fixed`, `sendAt: <DateTimeOffset>`, **and** `messagingServiceSid: <sid>` (not `from`). `MessageEnumScheduleType` has exactly one member: `Fixed (fixed)` — scheduling is Messaging-Service-only per the enum doc (`enums.md`). The SendAt allowed window is a **provider-enforced rule not expressed in the SDK contract** → see Assumptions & Blockers; a bad window returns as an `SdkException<RawError>`, read defensively.
- **Returns**: `ApiV2010AccountMessage` (§2.7). Provider identifier = `Sid`; delivery status = `Status` (`MessageEnumStatus?`); a scheduled message comes back with `Status = MessageEnumStatus.Scheduled`.
- Cite: `operations/Api20100401Message.md` (CreateMessage), `records-1-Ac-Ca.md` (ApiV2010AccountMessage), `enums.md` (MessageEnumScheduleType, MessageEnumStatus).

#### 2.2 FetchPhoneNumber3 — validate + canonicalize destination (capability 2) — `operations/LookupsV2PhoneNumber.md`

```
FetchPhoneNumber3(
  string phoneNumber,                             // path {PhoneNumber}; REQUIRED, positional (raw caller input)
  string? fields, string? countryCode, string? firstName, string? lastName,
  string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode,
  string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate,
  string? verificationSid, string? partnerSubId,   // all 15 nullable, no default → pass explicitly (null to skip)
  RequestOptions? requestOptions = null, CancellationToken ct = default)
```

- **HTTP**: `GET /v2/PhoneNumbers/{PhoneNumber}` (server node `Default4` = lookups.twilio.com).
- For basic validation + canonicalization pass just `phoneNumber` and `null` for all 15 optional params. `countryCode:` lets you pass national-format input with a 2-letter country.
- **Returns**: `LookupResponse` (§2.9).
  - **Canonical E.164** to store: `PhoneNumber (phone_number): string?` (Lookup returns the number in E.164; `NationalFormat` is the national, non-canonical form — do **not** store that).
  - **Usable-destination decision**: `Valid (valid): bool?`. Treat `Valid != true` as "reject at registration". Reasons: `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?` where `ValidationError` is a **StringEnum** with members `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)`.
  - **Not** a thrown error for a merely-invalid number: an invalid-but-parseable number returns `200` with `Valid=false`. A structurally un-parseable request path surfaces as `SdkException<RawError>` (e.g. 404). So the rejection logic is: catch `SdkException<RawError>` for hard failures **and** check `Valid == true` on success. Whether the live wire always populates `Valid`/`ValidationErrors` on a 200 is **UNVERIFIED** (live-traffic only) — code defensively: treat null `Valid` as "not usable".
  - Cite: `operations/LookupsV2PhoneNumber.md`, `records-4-Li-Me.md` (LookupResponse), `enums.md` (ValidationError).

#### 2.3 UpdateMessage — cancel scheduled (capability 4) **and** redact body (capability 6) — `operations/Api20100401Message.md`

```
UpdateMessage(
  string accountSid,                              // path {AccountSid}; REQUIRED
  string sid,                                      // path {Sid}; REQUIRED (message SID)
  string? body,                                    // wire Body — pass explicitly
  MessageEnumUpdateStatus? status,                 // wire Status — pass explicitly
  RequestOptions? requestOptions = null, CancellationToken ct = default)
```

- **HTTP**: `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` (server node `Default`).
- **Cancel a not-yet-sent scheduled message**: `body: null`, `status: MessageEnumUpdateStatus.Canceled`. `MessageEnumUpdateStatus` has exactly one member: `Canceled (canceled)` (`enums.md`).
- **Redact body (keep the record)**: `body: ""` (empty string), `status: null`. This blanks the stored text at the provider while the message record + outcome survive. Use this — **not** `DeleteMessage` — when the send/outcome record must be retained.
- **Returns**: `ApiV2010AccountMessage` (§2.7).
- **Already-sent cancel**: the provider rejects cancelling an already-delivered/sent message; it surfaces as `SdkException<RawError>` with a non-2xx `StatusCode` (commonly 400) and a provider `code`/`message` in the body. The exact status/code is **provider-side, not in the SDK contract (UNVERIFIED)** — branch on `ex.Error.StatusCode` and extract the body best-effort (see §Error handling), do not hard-code a specific provider error code.
- Cite: `operations/Api20100401Message.md` (UpdateMessage), `enums.md` (MessageEnumUpdateStatus).

#### 2.4 FetchMessage — single delivery status (capability 5) — `operations/Api20100401Message.md`

```
FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)
```

- **HTTP**: `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` (server node `Default`).
- **Returns**: `ApiV2010AccountMessage` (§2.7). Status field `Status (status): MessageEnumStatus?` — enum values in §2.8.
- Cite: `operations/Api20100401Message.md` (FetchMessage).

#### 2.5 DeleteMessage — (available, NOT for capability 6) — `operations/Api20100401Message.md`

```
DeleteMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)
```

- **HTTP**: `DELETE /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json`. Returns `void` (Task).
- Deletes the **whole** Message resource (record + body). Capability 6 requires the record to survive, so use `UpdateMessage` with `body:""` (§2.3), **not** this. Listed only to disambiguate.
- Cite: `operations/Api20100401Message.md` (DeleteMessage).

#### 2.6 ListMessage — reconciliation by FROM + date range (capability 7) — `operations/Api20100401Message.md`

```
ListMessage(
  string accountSid,                              // path {AccountSid}; REQUIRED
  string? to,                                      // wire To
  string? from,                                    // wire From — filter by sending number (Twilio:FromNumber)
  DateTimeOffset? dateSent,                         // wire DateSent  (exact day)
  DateTimeOffset? dateSentQuery,                    // wire DateSent< (on/before  → the range UPPER bound / "to")
  DateTimeOffset? dateSentQueryQuery,               // wire DateSent> (on/after   → the range LOWER bound / "from")
  long? pageSize,                                   // wire PageSize (max 1000; server default 50)
  int? page,                                        // wire Page
  string? pageToken,                                // wire PageToken
  RequestOptions? requestOptions = null, CancellationToken ct = default)
```

- **HTTP**: `GET /2010-04-01/Accounts/{AccountSid}/Messages.json` (server node `Default`). All 8 middle params nullable, no default → pass explicitly.
- **Range [from, to]**: lower bound (`>=`) → `dateSentQueryQuery` (wire `DateSent>`); upper bound (`<=`) → `dateSentQuery` (wire `DateSent<`). Pass `dateSent: null` when using the range pair. **Note the counter-intuitive mapping**: `dateSentQuery` = the `<` (upper) bound, `dateSentQueryQuery` = the `>` (lower) bound — the C# names do not read in `>=,<=` order.
- Sending-number filter: `from: "<Twilio:FromNumber>"`.
- **Pagination**: map row says **"none (only `page`, no `perPage`)"** — page through with `page`/`pageSize`, or follow `NextPageUri` from the response envelope. There is **no** auto-pager/enumerator; the implementer loops. **MUST load `dotnet-calling-endpoints`** for the manual-pagination pattern (see Trap notes).
- **Returns**: `ListMessageResponse` (envelope, §2.9-list) — the items are in `Messages`.
- Cite: `operations/Api20100401Message.md` (ListMessage), `records-4-Li-Me.md` (ListMessageResponse).

#### 2.7 Response record — `ApiV2010AccountMessage` (`records-1-Ac-Ca.md`)

Fields the integration reads (all `init`-only, all nullable):

| C# (wire): type | Use |
|---|---|
| `Sid (sid): string?` | provider message identifier |
| `Status (status): MessageEnumStatus?` | delivery status — §2.8 |
| `To (to): string?` / `From (from): string?` | recipient / sender |
| `Body (body): string?` | text (empty after redaction) |
| `MessagingServiceSid (messaging_service_sid): string?` | messaging service used |
| `DateSent (date_sent): string?` / `DateCreated (date_created): string?` / `DateUpdated (date_updated): string?` | timestamps (**string**, not DateTimeOffset) |
| `ErrorCode (error_code): int?` / `ErrorMessage (error_message): string?` | provider failure detail on failed/undelivered |
| `Price (price): string?` / `PriceUnit (price_unit): string?` / `NumSegments (num_segments): string?` / `NumMedia (num_media): string?` | cost / segmentation (all **string**) |
| `Direction (direction): MessageEnumDirection?`, `AccountSid`, `ApiVersion`, `Uri`, `SubresourceUris (subresource_uris): object?` | metadata |

There is **no response envelope** on the single-message ops — `CreateMessage`/`FetchMessage`/`UpdateMessage` return the `ApiV2010AccountMessage` record directly (read fields at top level, not one level down). Cite: `records-1-Ac-Ca.md`.

#### 2.8 Enum `MessageEnumStatus` (`enums.md`) — `StringEnum<MessageEnumStatus>`

Members (C# `MessageEnumStatus.X` ← wire): `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)`.

Not a C# `enum` — it is `StringEnum<T>`: build with `MessageEnumStatus.FromValue("delivered")` or the static member `MessageEnumStatus.Delivered`; compare with `==`. Scheduled message → `Scheduled`; cancelled → `Canceled`.

#### 2.9 Response record — `LookupResponse` (`records-4-Li-Me.md`)

Fields the integration reads:

| C# (wire): type | Use |
|---|---|
| `PhoneNumber (phone_number): string?` | **canonical E.164 to store** |
| `Valid (valid): bool?` | usable-destination decision |
| `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?` | reasons (StringEnum, §2.2) |
| `NationalFormat (national_format): string?` | national form — informational, do NOT store as canonical |
| `CountryCode (country_code): string?` / `CallingCountryCode (calling_country_code): string?` | ISO / dialing code |
| `Url (url): string?` | echo of request |

#### 2.9-list Response envelope — `ListMessageResponse` (`records-4-Li-Me.md`)

`End (end): int?`, `FirstPageUri (first_page_uri): string?`, `NextPageUri (next_page_uri): string?`, `Page (page): int?`, `PageSize (page_size): int?`, `PreviousPageUri (previous_page_uri): string?`, `Start (start): int?`, `Uri (uri): string?`, **`Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?`**.

Items live in `Messages` (one level down) — each item is an `ApiV2010AccountMessage` (§2.7) carrying `Sid`, `Status`, `To`, `From`, `DateSent`. Page forward via `NextPageUri` (null when exhausted) or `page`/`pageSize`.

### Client construction, auth & base-URL override (capability 8)

- **Construct**: `new TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)` — the only constructor. Or DI: `services.AddTwilioSdkClient(o => { ... })` (`ServiceCollectionExtensions.cs`). Cite: `sdk-map.md` §Getting a client.
- **Credentials** — set `options.AccountSidAuthToken` to a `BasicAuthCredentials { Username = ..., Password = ... }` (both `required`, `init`). This API uses HTTP Basic auth. Per the SDK's own XML docs: preferred is an **API key SID as `Username` and the API key secret as `Password`**; Account SID as `Username` + Auth Token as `Password` also works but the docs say limit that to local testing. The user's `AccountSid`/`AuthToken` map to `Username`/`Password` respectively. `BasicAuthCredentials` is in `TwilioSdk.Core.Authentication.Basic`. Cite: `sdk-map.md` §Servers & auth; source `Core/Authentication/Basic/BasicAuthCredentials.cs`.
- **Environment**: `options.Environment` is a `ServerEnvironment` with a single member `ServerEnvironment.Production` (`TwilioSdk.Servers`). Cite: `sdk-map.md` §Servers & auth; source `Servers/ServerEnvironment.cs`.
- **Base-URL override for the messaging (SMS) API** — `options.Server` is a `ServerOptions` (namespace `TwilioSdk`) holding one nested options object per server node (`Default` … `Default14`). Every `Api20100401Message` operation routes through the **`Default`** node (source-confirmed: each op calls `_server.Default(...)`), whose base URL defaults to `https://api.twilio.com`. Override it with:

  ```csharp
  options.Server.Default.Production.BaseUrl = configuration["Twilio:BaseUrl"];   // used verbatim as base address
  ```

  Shape: `ServerOptions.Default` → `DefaultOptions` → `.Production` (`DefaultOptions.ProductionOptions`) → `.BaseUrl : string`. This is set **once on the client options** (global to that client instance), and it is **per-server-node** in scope: it changes the base address for every operation on the `Default`/api node (all the SMS message ops here), and does **not** touch the Lookup calls, which route through the separate `Default4` node (`https://lookups.twilio.com`). Only apply the override when `Twilio:BaseUrl` is set. Source-confirmed: `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Api/Api20100401Message.cs`.

  ⚠ The map labels the message server "Default (api)" — the string `"api"` is the server's descriptive name, not a member you index; the C# member is `Server.Default`.

### Error handling (capability 9)

- **Every in-scope operation is Case B**: on any non-2xx it throws `SdkException<RawError>` (`TwilioSdk.Core.Exceptions.SdkException<T>` with `.Error` of type `TwilioSdk.Core.ErrorResponse.RawError`). There are **no** typed `{Operation}Error` accessors for these ops and **no** no-throw `…Result` variant.
- Read from `ex.Error` (a `RawError`): `StatusCode : HttpStatusCode`, `ReadAsString() : string`, `ReadAsJson<T>() : T?`, `ReadAsBytes() : ReadOnlyMemory<byte>`.
- **Provider error code/message**: Twilio's error body carries `code` / `message` / `more_info` / `status`, but for these Case-B ops the SDK does **not** model that shape — there is no typed record to deserialize into. Extraction is therefore **best-effort / UNVERIFIED** (only live traffic confirms the body shape): read `ex.Error.ReadAsJson<System.Collections.Generic.Dictionary<string, object?>>()` (or a small local DTO with `code`/`message`) inside a `try`, pull `code`/`message` when present, and **fall back to `ex.Error.StatusCode` + `ex.Error.ReadAsString()`** when the body is absent or does not match. Never branch program logic on a hard-coded provider `code`; branch on `StatusCode`.
- Cite: `sdk-map.md` §Error-handling model; per-op rows in `operations/Api20100401Message.md`, `operations/LookupsV2PhoneNumber.md`.

---

## 3. Trap notes

> ⚠ Step 1 (client & DI) — the `HttpClient`/handler pipeline behind `TwilioSdkClient` must be long-lived and reused (one instance across requests), not rebuilt per call; whether the SDK wrapper itself should be transient or singleton in DI is not visible in the signature. **MUST load `dotnet-client-initialization`** before wiring the client into the container.

> ⚠ Step 1/8 (configuration & resilience) — the SDK's `RetryOptions` (`StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`) do **not** behave the way the member names suggest: what `Timeout` actually bounds, and whether a failed `POST` (CreateMessage / UpdateMessage — non-idempotent writes) can be re-sent under retry, are not decidable from the names. Getting this wrong means an order SMS or a schedule/cancel can be executed more than once. **MUST load `dotnet-configuration-resilience`** before setting any retry/timeout option or the base URL.

> ⚠ Step 2 (auth) — where credentials must be set relative to client construction, and how to source them from configuration rather than hard-coding, is not shown by the property type. **MUST load `dotnet-authentication`** before wiring `AccountSidAuthToken`.

> ⚠ Steps 3–7 (calling endpoints) — the many optional params on `CreateMessage`/`ListMessage` have no C# default and mis-bind in a positional call; and `ListMessage` has **no** built-in pager, so range reconciliation must loop on `page`/`NextPageUri` yourself. How to call with named args and how to drive manual pagination correctly are not shown by the signature. **MUST load `dotnet-calling-endpoints`** before the first call.

> ⚠ Steps 2–7 (models) — `MessageEnumStatus`, `MessageEnumScheduleType`, `MessageEnumUpdateStatus`, `ValidationError` are `StringEnum<T>`, **not** C# enums (build via static member or `.FromValue("wire")`), and unmodeled JSON fields are dropped on deserialize. Whether a `StringEnum` comparison, construction, or an unrecognized wire value behaves as you expect is not shown by the type. **MUST load `dotnet-models`** before constructing payloads or mapping responses onto domain types.

> ⚠ Step 9 (error boundary) — which exception types actually reach your catch, and the two opposite ways a `JsonException` shows up around a non-2xx, are not shown by the return type. **MUST load `dotnet-error-handling`** before writing the boundary (see REQUIRED READING for the two mandatory `JsonException` hazards).

> ⚠ Step 1/9 (testing) — the `HttpClient` constructor argument is the test seam for faking Twilio responses. How to fake it and cover the error/edge paths is not shown by the signature. **MUST load `dotnet-testing`** before writing integration tests.

---

## 4. REQUIRED READING (load BEFORE implementation starts)

This sheet deliberately does **not** carry these skills' contents — load each one before writing the code for its step:

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, HttpClient lifetime, DI registration |
| `dotnet-authentication` | Step 2 — setting `AccountSidAuthToken`, sourcing secrets |
| `dotnet-calling-endpoints` | Steps 3–7 — named-arg calls, manual pagination on ListMessage |
| `dotnet-models` | Steps 2–7 — StringEnum construction/compare, required/nullable, wire names |
| `dotnet-configuration-resilience` | Steps 1 & 8 — retries/timeouts, base-URL override |
| `dotnet-error-handling` | Step 9 — exception boundary (mandatory; every integration writes one) |
| `dotnet-testing` | Tests — faking the HttpClient seam |

**Mandatory `System.Text.Json.JsonException` hazards (load `dotnet-error-handling` before writing the boundary):**

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated error shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions**
- "MESSAGING API base URL" (capability 8) = the SMS-send/schedule/cancel/redact/fetch/list calls, which the SDK routes through the **`Default` (api.twilio.com)** server node — so the override target is `options.Server.Default.Production.BaseUrl`. Phone-number **Lookup** uses a *different* node (`Default4`, lookups.twilio.com) and is intentionally left on its default; if you also need to redirect Lookup, that is `options.Server.Default4.Production.BaseUrl` (separate setting). Confirm this is the intended scope.
- The integration will send via **either** a `From` number (`Twilio:FromNumber`) **or** a `MessagingServiceSid` (`Twilio:MessagingServiceSid`). Scheduling (capability 3) requires the MessagingServiceSid path — an immediate send may use either. Assumed both settings can be present and the code picks per-call.
- `AccountSid`/`AuthToken` are mapped to `BasicAuthCredentials.Username`/`.Password`. The SDK docs recommend an API key SID + secret instead for non-local use; assumed the app deliberately uses Account SID + Auth Token. Confirm if API keys are preferred in production.
- eShopOnWeb project layout (which project hosts the client, config binding, DI) was **not** surveyed (out of scope for planning) — the main agent picks the host project and wires `IConfiguration` binding for `Twilio:*`.

**Blockers** — none blocking planning.

**UNVERIFIED (live-traffic only — code defensively, do not hard-code):**
- Whether Lookup's 200 response always populates `Valid` / `ValidationErrors` (treat null `Valid` as "not usable").
- The exact HTTP status / provider `code` returned when cancelling an already-sent message, and the SendAt allowed-window enforcement — branch on `StatusCode`, extract the provider body best-effort, never hard-code a provider error code.
- The provider error body shape (`code`/`message`/`more_info`/`status`) is not modeled by the SDK for these Case-B ops — extract best-effort and fall back to `StatusCode` + raw string.
