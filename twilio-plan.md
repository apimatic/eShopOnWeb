# Twilio .NET SDK — SMS Order Notifications Integration Plan (src/PublicApi)

SDK: `AsadAli.TwilioSdk` (root namespace `TwilioSdk`), APIMatic-generated. Map commit `51fdf48`.
Install version-less: `dotnet add package AsadAli.TwilioSdk` into `src/PublicApi`.

Every fact below is grounded in the bundled SDK map (page cited per row); the base-URL
override mechanism (Step 1) was confirmed from the named SDK source files because the map's
*Servers & auth* section only points at `Servers/` / `options.Server` without the shape.

---

## 1. Scope & sequence

| # | Step | Operation(s) |
|---|---|---|
| 1 | Register + configure the SDK client in DI; supply AccountSid/AuthToken auth; wire the messaging-only base-URL override | `AddTwilioSdkClient` / `TwilioSdkClient` ctor |
| 2 | Validate a destination number + get canonical E.164 (separate `lookups` host) | `client.LookupsV2PhoneNumber.FetchPhoneNumber3` |
| 3 | Send an SMS | `client.Api20100401Message.CreateMessage` |
| 4 | Schedule a message for future delivery | `client.Api20100401Message.CreateMessage` (with `scheduleType`/`sendAt`) |
| 5 | Cancel a scheduled message | `client.Api20100401Message.UpdateMessage` (status=canceled) |
| 6 | Fetch a single message by SID | `client.Api20100401Message.FetchMessage` |
| 7 | List messages for reconciliation (From + DateSent range, paged) | `client.Api20100401Message.ListMessage` |
| 8 | Redact a message body at the provider | `client.Api20100401Message.UpdateMessage` (body="") |
| 9 | Error boundary around every call | `SdkException<RawError>` |

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

### 2a. Namespaces (add a separate `using` for each — child namespaces are NOT transitive)

| Type | `using` namespace | Source |
|---|---|---|
| `TwilioSdkClient`, `TwilioSdkClientOptions`, `ServerOptions`, `Server` | `TwilioSdk` | root |
| `AddTwilioSdkClient` (DI extension) | `TwilioSdk` (extension on `IServiceCollection`) | `ServiceCollectionExtensions.cs` |
| `ServerEnvironment`, `DefaultOptions` (+ nested `DefaultOptions.ProductionOptions`), `Default4Options` | `TwilioSdk.Servers` | `Servers/` |
| `BasicAuthCredentials` | `TwilioSdk.Core.Authentication.Basic` | `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| `ApiV2010AccountMessage`, `ListMessageResponse`, `LookupResponse` | `TwilioSdk.Models` | `Models/` (records-1-Ac-Ca.md, records-4-Li-Me.md) |
| `MessageEnumStatus`, `MessageEnumScheduleType`, `MessageEnumUpdateStatus` | `TwilioSdk.Models.Enums` | `Models/Enums/` (enums.md) |
| `SdkException<TError>` | `TwilioSdk.Core.Exceptions` | `Core/Exceptions/SdkException.cs` |
| `RawError` | `TwilioSdk.Core.ErrorResponse` | `Core/ErrorResponse/RawError.cs` |

Operation controllers live in `TwilioSdk.Api`, but you reach them via client properties
(`client.Api20100401Message`, `client.LookupsV2PhoneNumber`) — no `using` needed unless you name the controller type.

### 2b. Client construction, auth, base-URL override (Step 1)

Source: `sdk-map.md` *Getting a client* / *Servers & auth*; base-URL shape confirmed from `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Servers/Default4Options.cs`.

- **Constructor:** `new TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)`. DI: `services.AddTwilioSdkClient(o => { ... })`.
- **Auth (AccountSid + AuthToken):** set `options.AccountSidAuthToken = new BasicAuthCredentials { Username = <AccountSid>, Password = <AuthToken> }`. `BasicAuthCredentials` has two `required` `init` props — `Username`, `Password` (both `string`). Basic auth: username=AccountSid, password=AuthToken (per the map's auth-notes; API-key/secret also valid).
- **Environment:** `options.Environment = ServerEnvironment.Production` (only member).
- **Base-URL override — MESSAGING ONLY (config key `Twilio:BaseUrl`, optional):** the messaging op (`CreateMessage` etc.) resolves against server node **`Default` (labelled `api`)**; its URL is `options.Server.Default.Production.BaseUrl` (provider default `https://api.twilio.com`). When `Twilio:BaseUrl` is set, assign it verbatim: `options.Server.Default.Production.BaseUrl = config["Twilio:BaseUrl"];` — when unset, leave the default. `options.Server` is a `ServerOptions`; `.Default` is a `DefaultOptions`; `.Production` is `DefaultOptions.ProductionOptions` with a settable `string BaseUrl`.
- **Lookup uses a DIFFERENT host** — server node **`Default4` (labelled `lookups`)**, i.e. `options.Server.Default4.Production.BaseUrl` (provider default `https://lookups.twilio.com`). It is selected automatically by the SDK per-operation; **do NOT apply `Twilio:BaseUrl` to it.** Leave `Default4` at its provider default so the messaging override never bleeds onto Lookup.

### 2c. Operations

Controller `client.Api20100401Message` (page `operations/Api20100401Message.md`). `client.LookupsV2PhoneNumber` (page `operations/LookupsV2PhoneNumber.md`). `accountSid` is the first positional arg on every Api20100401Message op (the account SID string). **All ops below are Case B → throw `SdkException<RawError>`; no typed error class, no no-throw variant.** Call with **named arguments** — every optional param is nullable-with-no-default and must be passed explicitly (pass `null` to skip).

| Cap | Operation (signature, params in order) | Request fields you set | Response envelope → fields read | Error | Pagination |
|---|---|---|---|---|---|
| 2 — validate + canonical E.164 | `LookupsV2PhoneNumber.FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `phoneNumber` (path, required — raw input). Pass the other 15 as `null` for basic validation. | `LookupResponse` → `Valid (valid): bool?` (**usable-destination test: `Valid == true`; reject when false/null**), `PhoneNumber (phone_number): string?` (**canonical E.164 to store**), `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?` (why invalid), `CountryCode`, `NationalFormat`. | `SdkException<RawError>` | none |
| 3 — send SMS | `Api20100401Message.CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid` (required), `to` (required, E.164 dest), `body` (text), and **either** `from` (=`Twilio:FromNumber`) **or** `messagingServiceSid` (=`Twilio:MessagingServiceSid`). All other params → `null`. | `ApiV2010AccountMessage` → `Sid (sid): string?` (message SID), `Status (status): MessageEnumStatus?` (current status), `ErrorCode (error_code): int?`, `ErrorMessage (error_message): string?`, `To`, `From`, `DateSent`. | `SdkException<RawError>` | none |
| 4 — schedule future send | Same `CreateMessage` signature | `accountSid`, `to`, `body`, **`messagingServiceSid` (required for scheduling — schedule works for Messaging Services only)**, `scheduleType: MessageEnumScheduleType.Fixed`, `sendAt: <DateTimeOffset>` (wire `SendAt`, ISO-8601). Do **not** set `from` when scheduling via a Messaging Service. | `ApiV2010AccountMessage` → `Sid`, `Status` (expect `MessageEnumStatus.Scheduled` / wire `scheduled`). | `SdkException<RawError>` | none |
| 5 — cancel scheduled | `Api20100401Message.UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid`, `sid`, `body: null`, `status: MessageEnumUpdateStatus.Canceled` (wire `canceled`). | `ApiV2010AccountMessage` → `Status` (expect `Canceled`). | `SdkException<RawError>` — if the message already sent, the provider rejects the cancel (non-2xx → this exception); see trap ⚠-5. | none |
| 6 — fetch one by SID | `Api20100401Message.FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid`, `sid`. | `ApiV2010AccountMessage` → `Sid`, `Status`, `ErrorCode`, `ErrorMessage`, `To`, `From`, `DateSent`, `Body`. | `SdkException<RawError>` | none |
| 7 — list for reconciliation | `Api20100401Message.ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid`, `from` (=configured sending number, wire `From`). **Date range:** `dateSentQueryQuery` = start/after bound (wire `DateSent>`), `dateSentQuery` = end/before bound (wire `DateSent<`). (`dateSent`/wire `DateSent` is exact-match — leave `null` for a range.) `pageSize` optional; `page`/`pageToken` drive paging. Others → `null`. | `ListMessageResponse` → `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?` (page items), `NextPageUri (next_page_uri): string?`, `Page (page): int?`, `PageSize (page_size): int?`. | `SdkException<RawError>` | **No auto-pager** (map: "only `page`, no `perPage`) — see trap ⚠-7. Cover the whole range by looping: stop when `NextPageUri` is null / `Messages` empty. |
| 8 — redact body (keep record+status) | Same `UpdateMessage` signature as cap 5 | `accountSid`, `sid`, **`body: ""` (empty string) to redact the text**, `status: null`. Per the map op note, `UpdateMessage` "is used to redact Message `body` text". | `ApiV2010AccountMessage` → record + `Status` survive; `Body` now empty. | `SdkException<RawError>` | none |

**Delete vs redact (cap 8):** `DeleteMessage(string accountSid, string sid, …)` (`DELETE …/Messages/{Sid}.json`, returns `void`/Task) removes the **entire** Message resource — record AND status gone. That is NOT what the shopper request needs. To dispose of the **text only** while preserving the record + delivery outcome, use `UpdateMessage` with `body: ""` (above). Use `DeleteMessage` only if full removal is intended.

### 2d. Enums needed (page `models/enums.md`; namespace `TwilioSdk.Models.Enums`; build with `Type.Member`, not the wire string)

**`MessageEnumStatus`** (`Status` on `ApiV2010AccountMessage`): `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)`.

**`MessageEnumScheduleType`** (`scheduleType` on `CreateMessage`): `Fixed (fixed)` — only member.

**`MessageEnumUpdateStatus`** (`status` on `UpdateMessage`): `Canceled (canceled)` — only member.

### 2e. GAPS

None. Every required capability maps to a concrete operation above. (Note: nothing in the SDK
surface is a dedicated "redact" endpoint — redaction is `UpdateMessage` with an empty body, per
the operation's own note; that is the documented mechanism, not a workaround.)

---

## 3. Trap notes (attach to the step where each bites; load the named skill before coding that step)

> ⚠ Step 1 (client & DI registration) — how the underlying `HttpClient`/handler pipeline must be
> owned and its lifetime relative to the SDK client wrapper is not visible in the ctor signature,
> and getting it wrong causes socket exhaustion or stale DNS. **MUST load `dotnet-client-initialization`** before wiring the client into DI.

> ⚠ Step 1 (auth) — where and when credentials must be set relative to client construction, and
> how to source them from configuration rather than hardcoding, is not shown by the property type.
> **MUST load `dotnet-authentication`** before setting `AccountSidAuthToken`.

> ⚠ Step 1 (base URL / resilience) — the SDK's retry/timeout options do **not** bound a whole
> call and are **not** the timeout on the `HttpClient` you register; whether a failed write can be
> re-sent on transport failure is not visible in the option names. **MUST load
> `dotnet-configuration-resilience`** before tuning retries/timeouts around the base-URL wiring.

> ⚠ Steps 2–8 (calling ops) — every optional param is nullable-with-no-default and mis-binds in a
> positional call; whether a value round-trips as you expect depends on named-argument use.
> **MUST load `dotnet-calling-endpoints`** before the first `client.*` call.

> ⚠ Steps 2–8 (models/enums) — `MessageEnumStatus`/`MessageEnumScheduleType`/`MessageEnumUpdateStatus`
> are `StringEnum<T>`, not C# enums, and unmodeled JSON fields are dropped on deserialize; how to
> construct/compare them safely is not shown by the field type. **MUST load `dotnet-models`** before
> building request payloads or reading `Status`.

> ⚠ Step 4 (scheduling) — the SDK enforces none of the provider's scheduling window/eligibility
> rules (min/max lead time, Messaging-Service requirement beyond the field); whether a given
> `sendAt` is accepted is decided by the wire, not the type. See §4 UNVERIFIED note; **MUST load
> `dotnet-error-handling`** so the rejection path is handled.

> ⚠ Step 5 (cancel) — whether cancelling an already-sent message returns an error or a no-op is
> provider behavior the signature can't show; do not assume success. See §4 UNVERIFIED note;
> **MUST load `dotnet-error-handling`**.

> ⚠ Step 7 (pagination) — `ListMessage` has **no built-in pager** (map: "only `page`, no `perPage`");
> how to walk `page`/`pageToken`/`NextPageUri` to cover the whole DateSent range without gaps or
> dupes, and how a per-page failure interacts with retries, is not visible in the signature.
> **MUST load `dotnet-configuration-resilience`** (pagination section) before writing the loop.

> ⚠ Step 9 (error boundary) — which exceptions actually reach the catch, and how to read status +
> provider error body safely, is not inferable from the throw-based signature. **MUST load
> `dotnet-error-handling`** before writing any try/catch. See the two mandatory `JsonException`
> hazards in REQUIRED READING.

---

## 4. Assumptions, UNVERIFIED items & Blockers

**Assumptions**
- `src/PublicApi` is the calling project; the package is installed there.
- AccountSid+AuthToken are supplied via configuration (`Twilio:AccountSid`, `Twilio:AuthToken` or similar) and mapped to `BasicAuthCredentials.Username`/`.Password` respectively. Confirm the exact config keys with the app owner if they differ.
- Cap 3 uses `Twilio:FromNumber` and/or `Twilio:MessagingServiceSid`; cap 4 (scheduling) requires `Twilio:MessagingServiceSid` (scheduling is Messaging-Service-only per the `MessageEnumScheduleType` map note).

**UNVERIFIED (only live traffic can confirm — code defensively):**
- **Cap 9 — accepted-but-carrier-undeliverable vs real failure.** Contract-level rule (from the map): a real API-level send failure throws `SdkException<RawError>` (non-2xx); an *accepted* message returns 2xx with an `ApiV2010AccountMessage` whose `Status` and `ErrorCode`/`ErrorMessage` carry the outcome. **UNVERIFIED:** whether a specific carrier-undeliverable case (e.g. US destinations refused for this account) is reflected *synchronously* in the `CreateMessage` response's `Status`/`ErrorCode`, or only later via `FetchMessage`, cannot be settled from the SDK. **Directive:** treat any successful (non-throwing) `CreateMessage` result as "accepted", then classify from the returned model best-effort — read `Status` (undeliverable outcomes: `Undelivered`, `Failed`) and `ErrorCode`/`ErrorMessage`; if `Status` is still pending (`Queued`/`Accepted`/`Scheduled`), poll `FetchMessage` for the terminal status rather than treating the send as failed. Fall back to the generic message if `ErrorCode` is null. Only a thrown `SdkException<RawError>` counts as a real send failure.
- **Cap 4 — scheduling window.** The provider's min/max lead-time and eligibility rules for `sendAt` are not in the SDK. **Directive:** surface the provider's rejection (the `SdkException<RawError>` status + body) to the caller rather than assuming the schedule succeeded.
- **Cap 5 — cancel-after-sent.** Whether cancelling an already-sent message errors or is a no-op is not in the SDK. **Directive:** handle the `SdkException<RawError>` path and re-read `Status` via `FetchMessage` to confirm the final state.

**Blockers:** none.

---

## 5. REQUIRED READING (load BEFORE implementation starts — this sheet deliberately does not carry their contents)

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, `HttpClient` ownership/lifetime, DI registration |
| `dotnet-authentication` | Step 1 — supplying `AccountSidAuthToken` credentials, when/where to set them |
| `dotnet-configuration-resilience` | Step 1 base-URL/retry/timeout wiring; Step 7 list pagination |
| `dotnet-calling-endpoints` | Steps 2–8 — named-argument calling, required vs optional params, request/response shapes |
| `dotnet-models` | Steps 2–8 — building request models, `StringEnum<T>` handling, wire-name mapping |
| `dotnet-error-handling` | Step 9 — the exception boundary around every SDK call (always required) |
| `dotnet-testing` | Testing the integration layer (the `HttpClient` seam) |

**Mandatory `JsonException` hazards for the error boundary — `System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:**
- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.
