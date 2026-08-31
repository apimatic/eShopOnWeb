# Twilio .NET SDK integration — eShopOnWeb `src/PublicApi` — plan & contract sheet

Grounding: every fact below comes from the bundled SDK map (pinned at the SDK source commit the
**v2.0.0** package was published from — v2.0.0 is the *only* published `AsadAli.TwilioSdk` version,
so the installed package matches these names exactly) or from the named SDK source file at that same
commit. Map pages are cited per row.

## 1. Scope & sequence

| # | Step | SDK operation(s) used |
|---|---|---|
| 1 | Add package `AsadAli.TwilioSdk` (version-less) to `src/PublicApi`; bind `Twilio:` config section (AccountSid, AuthToken, FromNumber, MessagingServiceSid, BaseUrl) to an options POCO | — |
| 2 | Register the SDK client in DI with credentials + optional messaging-host override | client construction (§3.1) |
| 3 | Validate shopper phone number at registration; store the provider's canonical form | `LookupsV2PhoneNumber.FetchPhoneNumber3` |
| 4 | Send immediate SMS (order placed / dispatched / cancelled) | `Api20100401Message.CreateMessage` |
| 5 | Schedule delivery-follow-up SMS at dispatch (queued with the provider) | `Api20100401Message.CreateMessage` with `scheduleType` + `sendAt` + `messagingServiceSid` |
| 6 | Cancel the queued follow-up when an order is cancelled | `Api20100401Message.UpdateMessage` (`status`) |
| 7 | Poll current delivery outcome of a message by SID | `Api20100401Message.FetchMessage` |
| 8 | Operator re-send of an undelivered message | fresh `Api20100401Message.CreateMessage` (no dedicated resend op exists — confirmed: the controller exposes exactly Create/Delete/Fetch/List/Update, `operations/Api20100401Message.md`) |
| 9 | Redact message content on shopper request (record + outcome survive) | `Api20100401Message.UpdateMessage` (`body`) — **not** `DeleteMessage`, which removes the whole record |
| 10 | Reconciliation listing by date range, server-side-filtered to our FromNumber | `Api20100401Message.ListMessage` |
| 11 | Error boundary around all SDK calls + tests | §3.4, §4 |

## 2. SDK identity

| | |
|---|---|
| NuGet package | `AsadAli.TwilioSdk` — install version-less: `dotnet add package AsadAli.TwilioSdk` (sdk-map.md) |
| Root namespace | `TwilioSdk` |
| Client class | `TwilioSdkClient` (namespace `TwilioSdk`) — ctor: `TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)` (sdk-map.md) |
| Options class | `TwilioSdkClientOptions` (namespace `TwilioSdk`) |
| Async/cancellation | Every operation is `async` (returns `Task`/`Task<T>`) and takes a trailing `CancellationToken ct = default`. No synchronous or no-throw (`…Result`) variants exist anywhere in the SDK (sdk-map.md). |

Namespaces needed (child namespaces are **not** imported transitively — one `using` each):

| Namespace | Types used from it |
|---|---|
| `TwilioSdk` | `TwilioSdkClient`, `TwilioSdkClientOptions`, `ServerOptions`, `AddTwilioSdkClient` extension |
| `TwilioSdk.Servers` | `ServerEnvironment` (source: `Servers/ServerEnvironment.cs`) |
| `TwilioSdk.Core.Authentication.Basic` | `BasicAuthCredentials` (source: `Core/Authentication/Basic/BasicAuthCredentials.cs`) |
| `TwilioSdk.Core.Configuration` | `RetryOptions`, `LoggingOptions` (source: `Core/Configuration/…`) |
| `TwilioSdk.Core` | `RequestOptions` (source: `Core/RequestOptions.cs`) |
| `TwilioSdk.Core.Exceptions` | `SdkException<TError>` (source: `Core/Exceptions/SdkException.cs`) |
| `TwilioSdk.Core.ErrorResponse` | `RawError` (source: `Core/ErrorResponse/RawError.cs`) |
| `TwilioSdk.Api` | operation controllers (only if you declare their types; `client.X` member access needs no using) |
| `TwilioSdk.Models` | `ApiV2010AccountMessage`, `ListMessageResponse`, `LookupResponse` |
| `TwilioSdk.Models.Enums` | `MessageEnumStatus`, `MessageEnumScheduleType`, `MessageEnumUpdateStatus`, `ValidationError`, … |

## 3. CONTRACT SHEET

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

### 3.1 Client construction, auth, base-URL override

`TwilioSdkClientOptions` properties (source: `TwilioSdkClientOptions.cs`, verified at the packaged commit):

| Property | Type | Notes |
|---|---|---|
| `Environment` | `TwilioSdk.Servers.ServerEnvironment` | StringEnum with a single member `ServerEnvironment.Production`; defaults to it — no need to set |
| `Retry` | `TwilioSdk.Core.Configuration.RetryOptions` | All members `required` — build a full instance or start from `RetryOptions.Default()` (sdk-map.md) |
| `Logging` | `TwilioSdk.Core.Configuration.LoggingOptions` | |
| `Server` | `TwilioSdk.ServerOptions` | Per-server base-URL overrides — see below |
| `AccountSidAuthToken` | `TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?` | `new BasicAuthCredentials { Username = <AccountSid>, Password = <AuthToken> }` — both members `required` init-only (source: `Core/Authentication/Basic/BasicAuthCredentials.cs`). Map note: account-SID/auth-token is accepted but the docs recommend an API key + secret for non-local use (sdk-map.md *Servers & auth*) |

Registration (source: `ServiceCollectionExtensions.cs`, verified at the packaged commit):

```csharp
services.AddTwilioSdkClient(o =>
{
    o.AccountSidAuthToken = new BasicAuthCredentials { Username = twilio.AccountSid, Password = twilio.AuthToken };
    if (!string.IsNullOrEmpty(twilio.BaseUrl))
        o.Server.Default.Production.BaseUrl = twilio.BaseUrl;   // messaging API host only
});
```

- `AddTwilioSdkClient` is an `IServiceCollection` extension in namespace `TwilioSdk`; it builds an
  `HttpClient` from `IHttpClientFactory` and registers `TwilioSdkClient` as a **singleton**
  (source: `ServiceCollectionExtensions.cs`). Equivalent manual form: `new TwilioSdkClient(httpClient, options)`.
- **Base-URL override (messaging API only):** `o.Server.Default.Production.BaseUrl = <Twilio:BaseUrl>` —
  verbatim replacement base address for every call on the `api` server (all `Api20100401Message`
  operations). `ServerOptions` (namespace `TwilioSdk`, source: `ServerOptions.cs`) has one property per
  server node, `Default` … `Default14`; each node exposes `.Production.BaseUrl`
  (source: `Servers/DefaultOptions.cs` — default `"https://api.twilio.com"`).
- The phone-number lookup operation runs on a **different server node** (`Default4`, default
  `"https://lookups.twilio.com"` — source: `Servers/Default4Options.cs`; the operation row is labelled
  `Default4 (lookups)`). The `Twilio:BaseUrl` override therefore does **not** redirect lookup calls;
  touching `o.Server.Default4…` is out of scope unless the brief changes.
- There is no edge/region concept on this SDK's options surface — server-node `BaseUrl` replacement is
  the only host override mechanism (source: `ServerOptions.cs`, `Servers/*.cs`).

### 3.2 Operation rows

**Row 1 — Validate phone number (step 3)** · `operations/LookupsV2PhoneNumber.md`

| | |
|---|---|
| Call | `client.LookupsV2PhoneNumber.FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | `phoneNumber` (the number as typed); the 15 params `fields`…`partnerSubId` are nullable with **no C# default — pass explicitly** (`null` to skip). For pure validation pass `fields: null`; `countryCode` helps parse non-E.164 input. |
| Returns | `TwilioSdk.Models.LookupResponse` — fields read by the integration: `PhoneNumber (phone_number): string?` ← **the canonical E.164 form to store** · `Valid (valid): bool?` · `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?` · `NationalFormat (national_format): string?` · `CountryCode (country_code): string?` · `CallingCountryCode (calling_country_code): string?` (records page `records-4-Li-Me.md`) |
| Error | **Case B** — `SdkException<RawError>`; accessors `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` |
| Pagination | none |

⚠ The response model carries an explicit `Valid` flag + `ValidationErrors`, so an unusable number can
arrive as a **successful (2xx) response with `Valid == false`**, not as an exception. Whether a given
bad number comes back 2xx-with-`Valid:false` versus an error status is live-wire behaviour the map and
source cannot settle — `UNVERIFIED`. Defensive directive: treat the number as usable **only** when the
call succeeds **and** `Valid == true`; on `Valid == false` read `ValidationErrors` for the reason; on
`SdkException<RawError>` treat as lookup failure. (v1 alternative `LookupsV1PhoneNumberApi.FetchPhoneNumber2`
returns `LookupsV1PhoneNumber`, which has **no** `Valid` field — v2 is the right choice;
`operations/LookupsV1PhoneNumberApi.md`, `records-4-Li-Me.md`.)

**Row 2 — Send SMS immediately (step 4)** · `operations/Api20100401Message.md`

| | |
|---|---|
| Call | `client.Api20100401Message.CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | `accountSid` (the configured `Twilio:AccountSid`), `to` (E.164 from step 3). **All 24 params `statusCallback`…`contentSid` are nullable with no default — pass every one explicitly** (`null` to skip); use named arguments. For an immediate send: `from: <Twilio:FromNumber>`, `body: <text>`, `scheduleType: null`, `sendAt: null`, everything else `null`. |
| From vs MessagingServiceSid | Both are independent nullable params (`from`, `messagingServiceSid`, plus `fallbackFrom`). Provider-side rules (exactly-one-sender, interplay) are not visible in the generated code — `UNVERIFIED`; defensive directive: set **exactly one** sender identity per call — `from` for immediate sends, `messagingServiceSid` when scheduling (scheduling is Messaging-Services-only, see Row 3). |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` — fields read: `Sid (sid): string?` · `Status (status): MessageEnumStatus?` · `ErrorCode (error_code): int?` · `ErrorMessage (error_message): string?` · `DateCreated (date_created): string?` · `DateSent (date_sent): string?` · `To (to): string?` · `From (from): string?` · `NumSegments (num_segments): string?` · `Price (price): string?` · `PriceUnit (price_unit): string?` (records page `records-1-Ac-Ca.md`). Note the date fields are **strings**, not `DateTime`. |
| Error | **Case B** — `SdkException<RawError>` (same accessors as Row 1) |
| Pagination | none |

**Row 3 — Schedule a message for later (step 5)** · `operations/Api20100401Message.md`, `enums.md`

Same `CreateMessage` signature as Row 2, with:

| Param | Value |
|---|---|
| `scheduleType` | `MessageEnumScheduleType.Fixed` (wire `fixed`) — the enum's only value. Enum doc: *"For Messaging Services only: Include this parameter with a value of `fixed` in conjuction with the `send_time` parameter in order to schedule a Message."* (the doc's `send_time` refers to the `SendAt` wire param ← `sendAt`; the operation's query-param table is authoritative) |
| `sendAt` | `DateTimeOffset?` — the future send time |
| `messagingServiceSid` | `Twilio:MessagingServiceSid` — **required for scheduling** (Messaging-Services-only per the enum doc); pass `from: null` |
| `to`, `body`, `accountSid` | as Row 2 |

Response: same `ApiV2010AccountMessage`; a scheduled message comes back with `Status` =
`MessageEnumStatus.Scheduled` (wire `scheduled`) and its `Sid` — persist the SID for later
cancel/poll. Provider-side scheduling-window constraints (min/max lead time) are not in the generated
surface — `UNVERIFIED`; surface any `SdkException<RawError>` body to the operator.

**Row 4 — Cancel a scheduled message (step 6)** · `operations/Api20100401Message.md`, `enums.md`

| | |
|---|---|
| Call | `client.Api20100401Message.UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `body` and `status` are nullable, no default → **pass both explicitly**. For cancel: `body: null, status: MessageEnumUpdateStatus.Canceled` (wire `canceled` — the enum's only value) |
| Returns | `ApiV2010AccountMessage` (expect `Status` = `MessageEnumStatus.Canceled`, wire `canceled`) |
| Error | **Case B** — `SdkException<RawError>` |
| Already went out | The map documents this op as cancelling *not-yet-sent* messages; the provider's response when the message already sent (status code/body) is live-wire behaviour — `UNVERIFIED`. Defensive directive: on `SdkException<RawError>`, read `StatusCode` + `ReadAsString()` and treat cancel-failure as "already sent" only after inspecting the payload — never swallow it as success. |

**Row 5 — Fetch current message state (step 7)** · `operations/Api20100401Message.md`, `enums.md`

| | |
|---|---|
| Call | `client.Api20100401Message.FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Returns | `ApiV2010AccountMessage` — read `Status`, `ErrorCode (error_code): int?`, `ErrorMessage (error_message): string?`, `DateSent`, `DateUpdated (date_updated): string?` |
| `MessageEnumStatus` values (StringEnum member (wire)) | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` |
| Error | **Case B** — `SdkException<RawError>` |
| Pagination | none |

**Row 6 — Re-send a message (step 8)** · `operations/Api20100401Message.md`

No dedicated resend operation exists — the `Api20100401Message` controller exposes exactly
`CreateMessage`, `DeleteMessage`, `FetchMessage`, `ListMessage`, `UpdateMessage`. Re-send = a fresh
`CreateMessage` with the same `to`/sender/`body`; it returns a **new** `Sid` (store it alongside/instead
of the old one; the old message's record is untouched).

**Row 7 — Redact message content (step 9)** · `operations/Api20100401Message.md`

| | |
|---|---|
| Call | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, …)` with `body: ""` (empty string), `status: null`. The map documents UpdateMessage as the op *"used to redact Message `body` text and to cancel not-yet-sent messages"*. Passing `body: null` skips the field — redaction requires the explicit empty string. |
| Returns | `ApiV2010AccountMessage` |
| Error | **Case B** — `SdkException<RawError>` |
| What survives | The message **record** (Sid, status/outcome, dates, error fields — all separate fields on the same resource) survives; only the `Body` text is redacted. `DeleteMessage(string accountSid, string sid, …)` exists but removes the whole record and does **not** meet the requirement. The exact post-redaction retrieval surface (e.g. whether `Body` reads back `""` or `null`) is live-wire behaviour — `UNVERIFIED`; re-fetch after redaction and assert `Body` is empty before confirming to the shopper. |

**Row 8 — List messages for reconciliation (step 10)** · `operations/Api20100401Message.md`, `records-4-Li-Me.md`

| | |
|---|---|
| Call | `client.Api20100401Message.ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` — the 8 params `to`…`pageToken` are nullable, no default → **pass all explicitly** |
| Server-side filters (wire ← C#) | `From` ← `from` — pass the configured `Twilio:FromNumber` so the provider restricts the answer to our sender (no client-side filtering) · `To` ← `to` · `DateSent` ← `dateSent` (exact date) · `DateSent<` ← `dateSentQuery` (**sent before**) · `DateSent>` ← `dateSentQueryQuery` (**sent after**) — note the generated names; use named arguments · `PageSize` ← `pageSize` |
| Returns | `TwilioSdk.Models.ListMessageResponse`: `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?` + paging fields `Page (page): int?`, `PageSize (page_size): int?`, `NextPageUri (next_page_uri): string?`, `PreviousPageUri (previous_page_uri): string?`, `FirstPageUri (first_page_uri): string?`, `Start (start): int?`, `End (end): int?`, `Uri (uri): string?` |
| Error | **Case B** — `SdkException<RawError>` |
| Pagination | No SDK pager for this op (map: "none (only `page`, no `perPage`)") — paginate manually via `page`/`pageToken`/`pageSize` against the response's `NextPageUri`. How `pageToken` relates to `NextPageUri` is provider semantics — see the trap note on `dotnet-configuration-resilience`. |

### 3.3 Enum tables needed (all `TwilioSdk.Models.Enums`, StringEnum — use the static members, e.g. `MessageEnumScheduleType.Fixed`, or `Type.FromValue("wire")`; `enums.md`)

| Enum | Members (wire) |
|---|---|
| `MessageEnumScheduleType` | `Fixed (fixed)` |
| `MessageEnumUpdateStatus` | `Canceled (canceled)` |
| `MessageEnumStatus` | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` |
| `ValidationError` | `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)` |
| `MessageEnumDirection` (response field) | `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)` |

### 3.4 Error handling (applies to every operation above)

- **All 7 operations in scope are Case B** (sdk-map.md error model): `catch (SdkException<RawError> ex)`
  with `ex.Error.StatusCode: HttpStatusCode`, `ex.Error.ReadAsString(): string`,
  `ex.Error.ReadAsJson<T>(): T?`, `ex.Error.ReadAsBytes(): ReadOnlyMemory<byte>`.
  `SdkException<T>` lives in `TwilioSdk.Core.Exceptions`; `RawError` in `TwilioSdk.Core.ErrorResponse`.
- No typed `{Operation}Error` classes and no no-throw `…Result` variants exist for these operations.
- Auth/config-shaped failures (401, wrong host from a bad `Twilio:BaseUrl`, timeouts) surface through the
  same Case B channel (or as transport exceptions) — check credentials/server-node configuration before
  touching call sites.

## 4. Trap notes (hazards — load the named skill before writing that step's code)

> ⚠ Step 2 (client registration) — the SDK's `AddTwilioSdkClient` fixes the client's lifetime and the
> `HttpClient` provenance for you; whether that matches eShopOnWeb's `IHttpClientFactory`/named-client
> conventions, and what a hand-rolled registration must reuse to avoid socket exhaustion, is not visible
> from the signature. **MUST load `dotnet-client-initialization`** before wiring DI.

> ⚠ Step 2 (auth) — where the credentials object must be set relative to client construction, and how to
> keep the auth token out of source/config dumps, is a usage-layer concern the options shape doesn't show.
> **MUST load `dotnet-authentication`** before wiring credentials.

> ⚠ Steps 3–10 (every call) — `CreateMessage` has 24 and `FetchPhoneNumber3` has 15 nullable parameters
> with **no C# defaults**; a positional call mis-binds silently. **MUST load `dotnet-calling-endpoints`**
> before the first call.

> ⚠ Steps 3–10 (models) — SDK enums are `StringEnum<T>`, not C# enums (no `switch` arms, no
> `Enum.Parse`); records are immutable with `init`-only setters; response date fields are `string?`; and
> JSON fields the SDK doesn't model are dropped on deserialize. **MUST load `dotnet-models`** before
> mapping SDK responses onto domain types.

> ⚠ Step 11 (error boundary) — which exception types actually reach a `catch` (and what a transport
> failure looks like versus an error status) is not derivable from the operation rows.
> **MUST load `dotnet-error-handling`** before writing any `try/catch`.

> ⚠ Steps 2, 4, 10 (resilience) — the SDK's retry/timeout options do **not** bound a whole call, are
> **not** the timeout on the `HttpClient` you register, and whether a failed `CreateMessage` POST may
> safely be re-sent by the retry layer decides whether a shopper gets duplicate SMSes; list-pagination
> semantics for `ListMessage` live here too. **MUST load `dotnet-configuration-resilience`** before
> tuning the client or writing the reconciliation loop.

> ⚠ Step 11 (tests) — which seam to fake for SDK calls (and which not to) is a usage-layer decision.
> **MUST load `dotnet-testing`** before stubbing the SDK.

## 5. REQUIRED READING — load **before implementation starts**

This sheet deliberately does not carry these skills' contents; load each one at the step named.

- `dotnet-client-initialization` — step 2 (client construction & DI lifetime)
- `dotnet-authentication` — step 2 (credentials wiring)
- `dotnet-calling-endpoints` — steps 3–10 (explicit/nullable params, named arguments)
- `dotnet-models` — steps 3–10 (StringEnum, records, dropped fields)
- `dotnet-error-handling` — step 11 (the exception boundary)
- `dotnet-configuration-resilience` — steps 2, 4, 10 (retries/timeout/pagination/base URL)
- `dotnet-testing` — step 11 (faking the SDK seam)

Mandatory hazard rows for the error boundary (an integration always writes one, so
`dotnet-error-handling` is never optional):

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the
  `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException`
  to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries
  something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

## 6. Assumptions & Blockers

Assumptions (correct me via a revision if any is wrong):

1. **Lookup v2 chosen over v1** for registration-time validation — v2's `LookupResponse` carries the
   explicit `Valid`/`ValidationErrors` fields; v1's response model has no validity field.
2. `Twilio:AccountSid` is used twice: as `BasicAuthCredentials.Username` **and** as the `accountSid`
   path argument on every `Api20100401Message` call.
3. `Twilio:BaseUrl` overrides the **messaging API host only** (`Server.Default.Production.BaseUrl`), per
   the brief; lookup calls keep the default lookups host (`Server.Default4`, untouched).
4. Immediate sends use `from: <Twilio:FromNumber>`; scheduled sends use
   `messagingServiceSid: <Twilio:MessagingServiceSid>` (scheduling is Messaging-Services-only per the
   `MessageEnumScheduleType` enum doc). Exactly-one-sender and other From/MessagingServiceSid interplay
   rules are provider-side semantics not visible in the generated SDK — `UNVERIFIED`; the directive is
   to set exactly one sender identity per call.
5. `UNVERIFIED` live-wire items (defensive directives given inline in §3.2): whether an invalid number
   returns 2xx-with-`Valid:false` vs an error status (Row 1); provider scheduling-window limits (Row 3);
   the error status returned when cancelling an already-sent message (Row 4); the exact post-redaction
   `Body` readback (Row 7); `pageToken`↔`NextPageUri` mechanics (Row 8).

Blockers: none.
