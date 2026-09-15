# Twilio .NET SDK contract — SMS order-notifications (PublicApi)

SDK: `AsadAli.TwilioSdk` (install version-less: `dotnet add package AsadAli.TwilioSdk`). Root namespace `TwilioSdk`. Client `TwilioSdkClient`. Map source commit `51fdf48`. Every row below cites the map page it came from.

> This sheet is a **contract reference**, not implementation. Do not write project code until the REQUIRED READING skills at the bottom are loaded — the trap notes deliberately name hazards without resolving them; the answers live in those skills.

---

## 1. Scope & sequence

1. **Client + DI + auth + server wiring** — register one `TwilioSdkClient`, bind `Twilio:` config, set Basic-auth credentials, override the messaging base URL from `Twilio:BaseUrl` (leave the lookups host alone). Ops: none yet. Skills: `dotnet-client-initialization`, `dotnet-authentication`, `dotnet-configuration-resilience`.
2. **Validate/normalize a phone number at registration.** Op: `LookupsV2PhoneNumber.FetchPhoneNumber3`.
3. **Send an SMS now.** Op: `Api20100401Message.CreateMessage`.
4. **Schedule an SMS for the future.** Op: `Api20100401Message.CreateMessage` (with `scheduleType` + `sendAt`).
5. **Cancel a scheduled SMS.** Op: `Api20100401Message.UpdateMessage` (status `canceled`).
6. **Fetch a message's current status.** Op: `Api20100401Message.FetchMessage`.
7. **Redact a message body at the provider.** Op: `Api20100401Message.UpdateMessage` (body `""`).
8. **List/reconcile messages by From + date range.** Op: `Api20100401Message.ListMessage`.

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### 2a. Namespaces to `using` (from `sdk-map.md` Namespaces table)

| Contents | Namespace |
|---|---|
| Client & options (`TwilioSdkClient`, `TwilioSdkClientOptions`, `ServerOptions`) | `TwilioSdk` |
| Operation controllers (`Api20100401Message`, `LookupsV2PhoneNumber`) | `TwilioSdk.Api` |
| Records (`ApiV2010AccountMessage`, `ListMessageResponse`, `LookupResponse`) | `TwilioSdk.Models` |
| Enums (`MessageEnumStatus`, `MessageEnumScheduleType`, `MessageEnumUpdateStatus`, `ValidationError`) | `TwilioSdk.Models.Enums` |
| Error classes (`SdkException<T>`, `RawError`) | `TwilioSdk.Errors` (and `SdkException<T>` is `Core/Exceptions`; catch as shown in §2h) |
| `ServerEnvironment` | `TwilioSdk.Servers` |
| `BasicAuthCredentials` | `TwilioSdk.Core.Authentication.Basic` |
| Per-server option types (`DefaultOptions`, `Default4Options`) | `TwilioSdk.Servers` |

### 2b. Client construction, auth, environment (`sdk-map.md` → Getting a client / Servers & auth; source `TwilioSdkClientOptions.cs`, `BasicAuthCredentials.cs`)

- Constructor: `TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)`.
- DI: `services.AddTwilioSdkClient(o => { … })` (`ServiceCollectionExtensions.cs`).
- Auth property: `options.AccountSidAuthToken` of type `BasicAuthCredentials?`. `BasicAuthCredentials` has two `required` init-only members: `Username: string`, `Password: string`. Basic auth → **`Username` = `Twilio:AccountSid`** (or an API key SID), **`Password` = `Twilio:AuthToken`** (or the API key secret).
- Environment: `options.Environment` is `ServerEnvironment` (default `ServerEnvironment.Default()` → `Production`; only member is `ServerEnvironment.Production`).

### 2c. Base-URL / server override (source `ServerOptions.cs`, `Server.cs`, `Servers/DefaultOptions.cs`, `Servers/Default4Options.cs` — this was a real map gap resolved from source)

The SDK routes different product hosts through **named server nodes** on `options.Server` (type `ServerOptions`), each carrying a nested `Production.BaseUrl` string. Overriding is **per-client** — you mutate the `ServerOptions` on the `TwilioSdkClientOptions` handed to that one `TwilioSdkClient`; there is **no global/static base URL**.

| Capability | Server node (map "HTTP" tag) | Override path on `options` | Default value |
|---|---|---|---|
| Messaging / all `2010-04-01` `Api…` ops (send, fetch, list, update, delete) | `Default (api)` | `options.Server.Default.Production.BaseUrl` | `https://api.twilio.com` |
| Phone-number lookup/validation | `Default4 (lookups)` | `options.Server.Default4.Production.BaseUrl` | `https://lookups.twilio.com` |

- **`Twilio:BaseUrl` binds to `options.Server.Default.Production.BaseUrl` only** (when the key is present, set it verbatim). It governs every messaging-API call in steps 3–8, because they all resolve through the `Default (api)` node.
- **Lookup lives on a *different host*** (`lookups.twilio.com`, the `Default4` node). It is NOT governed by `Twilio:BaseUrl` and must be left at its default unless you have a separate override. Setting `Twilio:BaseUrl` does not and must not affect it.

### 2d. Step 2 — Validate & canonicalize a phone number (`operations/LookupsV2PhoneNumber.md`; response `records-4-Li-Me.md`; enum `enums.md`)

- **Controller/accessor**: `client.LookupsV2PhoneNumber` (namespace `TwilioSdk.Api`).
- **Signature**: `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `phoneNumber` is the path segment (the number to validate). The **15 params `fields`…`partnerSubId` are nullable with NO default → must be passed explicitly** (pass `null` to skip). Use named args.
- **Returns**: `LookupResponse` (namespace `TwilioSdk.Models`). Fields read by the integration (`CSharpName (wire): type`):
  - `Valid (valid): bool?` — validity indicator.
  - `PhoneNumber (phone_number): string?` — provider's canonical **E.164** form; **store this**, not caller input.
  - `NationalFormat (national_format): string?`, `CountryCode (country_code): string?`, `CallingCountryCode (calling_country_code): string?`.
  - `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?` — reasons a number is invalid.
- **`ValidationError` enum values** (`enums.md`, `StringEnum`, namespace `TwilioSdk.Models.Enums`): `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)`.
- **Host**: `lookups.twilio.com` (`Default4`) — NOT `Twilio:BaseUrl`.
- **Error**: **Case B** — `SdkException<RawError>` (accessors in §2h). No typed error, no no-throw variant.
- **UNVERIFIED (live-wire only):** whether a *parseable-but-invalid* number returns **HTTP 200 with `Valid == false`** versus **throws** `SdkException<RawError>` (e.g. 404 for an unparseable string) is not settled by the SDK source — it is provider wire behavior. **Defensive directive:** treat the number as rejected when `Valid != true` (including `null`) AND when the call throws `SdkException<RawError>`; on success persist `LookupResponse.PhoneNumber` as the canonical number. Do not assume an exception is the only rejection path.

### 2e. Steps 3 & 4 — Send now / schedule (`operations/Api20100401Message.md` → CreateMessage; response `records-1-Ac-Ca.md`; enums `enums.md`)

- **Controller/accessor**: `client.Api20100401Message` (namespace `TwilioSdk.Api`).
- **Signature**: `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `accountSid` = path (`Twilio:AccountSid`). `to` = destination. The **24 params `statusCallback`…`contentSid` are nullable, NO default → must be passed explicitly** (pass `null`). Use named args.
- **Sender selection** (mutually alternative; both are among the must-pass params):
  - Immediate send via number: `from:` = `Twilio:FromNumber`, `messagingServiceSid:` = `null`.
  - Send via messaging service: `messagingServiceSid:` = `Twilio:MessagingServiceSid`, `from:` = `null`.
- **Body**: `body:` carries the SMS text.
- **Scheduling (step 4)**: set `scheduleType:` = `MessageEnumScheduleType.Fixed` (wire `fixed` — the ONLY value of this enum) AND `sendAt:` = a `DateTimeOffset`. Per the SDK's own enum doc, scheduling is **"For Messaging Services only"**, so scheduling requires `messagingServiceSid:` = `Twilio:MessagingServiceSid` (and `from:` = `null`).
- **Returns**: `ApiV2010AccountMessage` (namespace `TwilioSdk.Models`). Fields read by the integration:
  - `Sid (sid): string?` — the message SID (persist it; needed for steps 5–7).
  - `Status (status): MessageEnumStatus?` — see enum values in §2g.
  - `ErrorCode (error_code): int?`, `ErrorMessage (error_message): string?`.
  - Also present: `From (from)`, `To (to)`, `Body (body)`, `MessagingServiceSid (messaging_service_sid)`, `DateSent (date_sent): string?`, `DateCreated (date_created): string?`, `Price (price)`, `NumSegments (num_segments)`, `Direction (direction): MessageEnumDirection?`.
- **Host**: `api.twilio.com` (`Default`) — governed by `Twilio:BaseUrl`.
- **Error**: **Case B** — `SdkException<RawError>`.
- **Provider-enforced, NOT in the SDK contract (UNVERIFIED lead-time bounds):** Twilio's scheduling lead-time window (a minimum and maximum gap between "now" and `sendAt`) is not represented anywhere in this SDK's types — the generated signature accepts any `DateTimeOffset`. **Defensive directive:** do not rely on the SDK to reject an out-of-window `sendAt`; a bad lead time surfaces as `SdkException<RawError>` at send time, so validate the window yourself and handle the rejection path.

### 2f. Steps 5 & 7 — Cancel scheduled / redact body (`operations/Api20100401Message.md` → UpdateMessage; enum `enums.md`)

- **Signature**: `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `body` and `status` are nullable with NO default → must be passed explicitly.
- **Step 5 — cancel a scheduled message**: `UpdateMessage(accountSid, sid, body: null, status: MessageEnumUpdateStatus.Canceled)`. `MessageEnumUpdateStatus` has exactly ONE value: `Canceled (canceled)` (namespace `TwilioSdk.Models.Enums`).
  - **Precondition / allowed transition (provider-enforced, not SDK-checked):** only a message currently in `scheduled` status transitions to `canceled`; a message already sent/queued cannot be canceled. The SDK does not validate this — an illegal transition returns `SdkException<RawError>`. Treat that as "too late to cancel."
- **Step 7 — redact body but KEEP the record**: `UpdateMessage(accountSid, sid, body: "", status: null)`. Updating `body` to an **empty string** blanks the stored text at the provider while the message record and its status survive. This is the "redact, not delete" path.
  - **Do NOT use `DeleteMessage(string accountSid, string sid, …)`** for redaction — that op returns `void` and deletes the entire resource (record gone). It is the wrong tool for "keep the record, drop the text."
- **Returns**: `ApiV2010AccountMessage` (fields as §2e).
- **Error**: **Case B** — `SdkException<RawError>`.

### 2g. Step 6 — Fetch current delivery status (`operations/Api20100401Message.md` → FetchMessage)

- **Signature**: `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)`.
- **Returns**: `ApiV2010AccountMessage`. Read `Status (status): MessageEnumStatus?`, `ErrorCode (error_code): int?`, `ErrorMessage (error_message): string?`.
- **`MessageEnumStatus` values** (`enums.md`, `StringEnum`, namespace `TwilioSdk.Models.Enums`) — literal C# member `(wire value)`: `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)`.
- **Error**: **Case B** — `SdkException<RawError>`.

### 2h. Step 8 — List / reconcile by From + date range (`operations/Api20100401Message.md` → ListMessage; response `records-4-Li-Me.md`)

- **Signature**: `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` — the 8 params `to`…`pageToken` are nullable, NO default → must be passed explicitly.
- **Provider-side filters (wire ← C#), from the map's query-param table** — note the misleading param names, take them from this table exactly:
  - `From ← from` — set to `Twilio:FromNumber`.
  - `DateSent ← dateSent` — exact-day filter (leave `null` for a range).
  - **`DateSent< ← dateSentQuery`** — upper bound (sent on/before). Set to the range **end**.
  - **`DateSent> ← dateSentQueryQuery`** — lower bound (sent on/after). Set to the range **start**.
  - `PageSize ← pageSize` (`long?`), `Page ← page` (`int?`), `PageToken ← pageToken` (`string?`).
  - These are real server-side filters — do NOT fetch broad and filter client-side.
- **Returns**: `ListMessageResponse` (namespace `TwilioSdk.Models`): `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?` plus `Start (start): int?`, `End (end): int?`, `Page (page): int?`, `PageSize (page_size): int?`, `FirstPageUri (first_page_uri): string?`, `NextPageUri (next_page_uri): string?`, `PreviousPageUri (previous_page_uri): string?`, `Uri (uri): string?`.
- **Pagination**: the map records **"Pagination: none (only `page`, no `perPage`)"** — there is NO auto-pager on this op. Paging is manual via `page`/`pageToken` + the `NextPageUri` in the response.
- **Host**: `api.twilio.com` (`Default`) — governed by `Twilio:BaseUrl`.
- **Error**: **Case B** — `SdkException<RawError>`.

### 2i. Error model — Case B accessors (all six ops above; `sdk-map.md` Error-handling model)

Every operation in scope is **Case B**: on an error status the SDK throws `SdkException<RawError>` (throw-based; no no-throw `…Result` variant exists anywhere in this SDK). Read via `ex.Error`:
- `StatusCode: HttpStatusCode`
- `ReadAsString(): string`
- `ReadAsJson<T>(): T?`
- `ReadAsBytes(): ReadOnlyMemory<byte>`

There are no typed `TryGet…` accessors on these ops (that is Case A, which none of the in-scope ops are). Provider error code/message for a *rejected request* come from `RawError` (status + body); provider error code/message for a *delivered-but-failed* message come from `ApiV2010AccountMessage.ErrorCode`/`.ErrorMessage` on the 2xx response, not from an exception.

---

## 3. Trap notes (name the hazard; the answer is in the named skill — load it)

⚠ **Step 1 (client & DI)** — the `HttpClient`/handler pipeline behind `TwilioSdkClient` has lifetime and reuse rules a constructor signature cannot show; getting them wrong causes socket exhaustion or stale DNS. **MUST load `dotnet-client-initialization`** before wiring the client into DI.

⚠ **Step 1 (auth)** — where and when credentials must be set relative to client construction, and how to source them from config rather than hardcode, is not visible in the `BasicAuthCredentials` shape. **MUST load `dotnet-authentication`** before setting credentials.

⚠ **Step 1 (base URL / resilience)** — the SDK's retry/timeout options do not bound a whole call and are not the timeout on the `HttpClient` you register; and the base-URL override interacts with retry/pagination behavior. **MUST load `dotnet-configuration-resilience`** before tuning the client.

⚠⚠ **Steps 3 & 4 (sending SMS) — idempotency of a failed write.** `CreateMessage` is a non-idempotent `POST`; whether a send that fails at the transport layer can be transmitted **more than once** (a duplicate customer text) depends on retry semantics the signature does not reveal, and the status-based retry gate does not tell the whole story. **MUST load `dotnet-configuration-resilience`** before enabling retries on the messaging client.

⚠ **All steps (models)** — every `MessageEnum…` / `ValidationError` field is a `StringEnum<T>`, **not** a C# `enum`, and `sendAt`/`dateSent` filters are `DateTimeOffset`; how you construct enum values, how union/optional fields read back, and the fact that unmodeled JSON fields are dropped on deserialize are not shown by the field list. **MUST load `dotnet-models`** before building request payloads or mapping responses.

⚠ **All steps (calling)** — the long lists of nullable-no-default params mis-bind in a positional call; several optional params have no C# default. **MUST load `dotnet-calling-endpoints`** before writing the first call.

⚠ **All steps (error boundary)** — which exception types actually reach a catch block, and how to read status/body safely, are not inferable from "Case B". **MUST load `dotnet-error-handling`** before writing any try/catch (see the two mandatory JsonException rows in REQUIRED READING).

⚠ **Tests** — the test seam is the `HttpClient` constructor argument; asserting real behavior vs execution has rules the signature hides. **MUST load `dotnet-testing`** before writing integration tests.

---

## 4. REQUIRED READING (load BEFORE implementation starts — this sheet deliberately does not carry their contents)

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — construct/register the client, HttpClient lifetime |
| `dotnet-authentication` | Step 1 — Basic-auth credential wiring from config |
| `dotnet-configuration-resilience` | Step 1 & steps 3–4 — base-URL override, retries/timeouts, non-idempotent send retry, manual pagination for `ListMessage` |
| `dotnet-calling-endpoints` | Steps 2–8 — named args, must-pass-explicitly nullable params |
| `dotnet-models` | Steps 2–8 — `StringEnum` construction, `DateTimeOffset`, dropped-field behavior |
| `dotnet-error-handling` | Steps 2–8 — the Case B exception boundary (mandatory; every integration writes one) |
| `dotnet-testing` | Tests — faking the `HttpClient` seam |

**Two mandatory `System.Text.Json.JsonException` hazards for the error boundary — it reaches the boundary from two directions that need opposite handling:**
- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated error shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions:**
- `Twilio:AccountSid` → `BasicAuthCredentials.Username` and `Twilio:AuthToken` → `BasicAuthCredentials.Password` (the map's auth note permits account SID + auth token for non-production; an API key SID/secret is the recommended production pairing but the provided config keys map to the account SID/auth-token pairing).
- Scheduling (step 4) uses `Twilio:MessagingServiceSid` because the SDK's `MessageEnumScheduleType` doc states scheduling is "For Messaging Services only"; immediate sends (step 3) may use either `Twilio:FromNumber` or `Twilio:MessagingServiceSid` — the plan defaults immediate sends to `Twilio:FromNumber`.
- Reconciliation (step 8) filters on `Twilio:FromNumber`; therefore reconciliation only covers From-number sends, not messages sent via the messaging-service SID (those have no single `From` at request time). Confirm whether messaging-service-sent messages must also be reconciled — if so, a `From`-only filter will miss them (this is a scope question, not an SDK gap).

**Blockers:** none. Every requested capability (lookup/validate, send, schedule, cancel, fetch, redact-body, list-by-From+date) is exposed by the SDK and contracted above. Two items are provider-wire-enforced rather than SDK-typed and are labeled UNVERIFIED with defensive directives in §2d and §2e (invalid-number rejection path; scheduling lead-time window).
