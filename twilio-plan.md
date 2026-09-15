# Twilio .NET SDK integration — CONTRACT SHEET: "Order notifications by SMS" (eShopOnWeb)

SDK: `AsadAli.TwilioSdk` (install version-less: `dotnet add package AsadAli.TwilioSdk`). Root namespace `TwilioSdk`.
Client `TwilioSdkClient`, options `TwilioSdkClientOptions`. Map source commit stamp: `51fdf48`.

Every capability the brief asked for IS covered by the SDK map — there are **no gaps**. One capability
(phone validation, item 1) is served from a **different host** than messaging; that is handled explicitly in
the client-config section below, not left open.

---

## 1. Scope & sequence

| # | Step | Operation(s) used |
|---|---|---|
| 1 | Client + DI registration, auth, per-host base-URL wiring | `AddTwilioSdkClient` / `new TwilioSdkClient` |
| 2 | Validate + canonicalize a phone number before storing | `client.LookupsV2PhoneNumber.FetchPhoneNumber3` |
| 3 | Send an SMS immediately (placed/dispatched/cancelled/resend) | `client.Api20100401Message.CreateMessage` |
| 4 | Schedule a provider-held future message | `client.Api20100401Message.CreateMessage` (with `scheduleType` + `sendAt`) |
| 5 | Cancel a scheduled message before it sends | `client.Api20100401Message.UpdateMessage` (`status: Canceled`) |
| 6 | Fetch one message by SID for its current status | `client.Api20100401Message.FetchMessage` |
| 7 | List messages by From + DateSent window, paginated | `client.Api20100401Message.ListMessage` |
| 8 | Redact a message body at the provider (keep the record) | `client.Api20100401Message.UpdateMessage` (`body: ""`) |

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

### 2a. Namespaces (`using` directives) for every SDK type on this sheet

| Type(s) | Namespace |
|---|---|
| `TwilioSdkClient`, `TwilioSdkClientOptions`, `ServerOptions` | `TwilioSdk` |
| `ServerEnvironment`, `DefaultOptions`, `Default4Options` | `TwilioSdk.Servers` |
| Controllers `Api20100401Message`, `LookupsV2PhoneNumber` (accessed as `client.X`) | `TwilioSdk.Api` |
| `ApiV2010AccountMessage`, `ListMessageResponse`, `LookupResponse` | `TwilioSdk.Models` |
| `MessageEnumStatus`, `MessageEnumScheduleType`, `MessageEnumUpdateStatus`, `MessageEnumDirection`, `ValidationError` | `TwilioSdk.Models.Enums` |
| `SdkException<TError>` | `TwilioSdk.Core.Exceptions` |
| `RawError` | `TwilioSdk.Core.ErrorResponse` |
| `RequestOptions` | `TwilioSdk.Core` |
| `BasicAuthCredentials` | `TwilioSdk.Core.Authentication.Basic` |

### 2b. Operations

All 6 operations below are **Case B** error: they throw `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`.
There is **no typed error accessor and no no-throw (`…Result`) variant** on any of them. Read the failure via
`ex.Error.StatusCode` (`System.Net.HttpStatusCode`), `ex.Error.ReadAsString()`, or
`ex.Error.ReadAsJson<T>()` / `ex.Error.ReadAsBytes()`. (Twilio's error JSON body typically carries
`code` / `message` / `more_info` / `status`, but that shape is on-the-wire, not a generated model — see the
UNVERIFIED note under Traps and read it best-effort via `ReadAsJson`.)

| Cap | Accessor.Method | Signature (params in order; all nullable-no-default params MUST be passed — pass `null` to skip) | Request shape | Returns → fields you read | Pagination | Map page |
|---|---|---|---|---|---|---|
| 2 Validate/canonicalize | `client.LookupsV2PhoneNumber.FetchPhoneNumber3` | `(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `phoneNumber` is the path segment (raw input); all 15 query params optional → pass `null`. For basic validity + E.164 no `fields` needed. | `LookupResponse` (below). Read `Valid` (bool? — validity signal), `PhoneNumber` (string? — provider **canonical E.164** to store), `NationalFormat`, `ValidationErrors` (why-invalid reasons). | none | `operations/LookupsV2PhoneNumber.md` |
| 3 Send SMS | `client.Api20100401Message.CreateMessage` | `(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid` path; `to` (wire `To`) required; `body` (wire `Body`) the text; **sender is EITHER** `from` (wire `From`, an E.164/`FromNumber`) **OR** `messagingServiceSid` (wire `MessagingServiceSid`) — the SDK exposes both as params; supply exactly one. 24 middle params are nullable-no-default → pass `null` for every one you don't use. | `ApiV2010AccountMessage` (below). Read `Sid` (provider message id), `Status` (`MessageEnumStatus?` — delivery outcome), `ErrorCode`/`ErrorMessage` for undeliverable. | none | `operations/Api20100401Message.md` |
| 4 Schedule | `client.Api20100401Message.CreateMessage` | same as Send | Set `scheduleType: MessageEnumScheduleType.Fixed` (wire `ScheduleType=fixed`) **and** `sendAt: <DateTimeOffset>` (wire `SendAt`). Per the SDK enum doc this is **Messaging Services only**: supply `messagingServiceSid`, do **not** supply `from`. Provider holds and sends it. | `ApiV2010AccountMessage`; `Status` comes back `Scheduled`. | none | `operations/Api20100401Message.md` + enums row |
| 5 Cancel scheduled | `client.Api20100401Message.UpdateMessage` | `(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid`+`sid` path; pass `body: null`, `status: MessageEnumUpdateStatus.Canceled` (wire `Status=canceled`). | `ApiV2010AccountMessage`; `Status` becomes `Canceled`. If already sent, the provider rejects → `SdkException<RawError>` (read `StatusCode`/body; exact code is provider-side, see Traps). | none | `operations/Api20100401Message.md` |
| 6 Fetch one | `client.Api20100401Message.FetchMessage` | `(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid`+`sid` path. | `ApiV2010AccountMessage`; read `Status`, `ErrorCode`, `ErrorMessage`, `DateSent`, `Price`. | none | `operations/Api20100401Message.md` |
| 7 List/reconcile | `client.Api20100401Message.ListMessage` | `(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` | Server-side filters: `from` (wire `From`) = the sending number; **date-sent window** → `dateSentQueryQuery` (wire `DateSent>`) = lower bound / from, `dateSentQuery` (wire `DateSent<`) = upper bound / to (`dateSent`/wire `DateSent` is exact-day, leave `null`). `pageSize` (wire `PageSize`, `long?`), `page` (wire `Page`, `int?`), `pageToken` (wire `PageToken`, `string?`). | `ListMessageResponse` (below). Read `Messages` (the page), `NextPageUri` (null when no more), `Page`, `PageSize`. | manual only — see Traps | `operations/Api20100401Message.md` |
| 8 Redact body | `client.Api20100401Message.UpdateMessage` | same as cap 5 | `accountSid`+`sid` path; pass `body: ""` (empty, wire `Body`), `status: null`. Map note: UpdateMessage "used to **redact Message `body` text**". This updates in place — the record and its final `Status` survive; only the body text is cleared at the provider. | `ApiV2010AccountMessage` (body now empty). | none | `operations/Api20100401Message.md` |

> **Delete vs redact (item 7 of the brief):** the SDK exposes BOTH. `client.Api20100401Message.DeleteMessage(string accountSid, string sid, ...)`
> → `DELETE …/Messages/{Sid}.json`, returns `void` (Task) and **removes the entire Message resource** (record + status gone).
> To keep the record and its final status while removing only the body text, use **`UpdateMessage` with `body: ""`** (cap 8),
> NOT `DeleteMessage`. Do not use delete for redaction.

> **Idempotency (item 8 of the brief):** the create-message operation exposes **no** idempotency-key parameter or header
> — `CreateMessage` has no such param, and `RequestOptions` (`TwilioSdk.Core.RequestOptions`) carries only `LogLevel? LogLevel`
> (no custom-header hook). **The SDK provides no built-in idempotency mechanism; handle idempotency in the app layer.**
> (See the resilience trap below — transport-failure retries can re-execute a POST, which is why an app-layer key matters.)

### 2c. Response models (field name `(wire_name): Type`)

**`ApiV2010AccountMessage`** (`TwilioSdk.Models`; map: `records-1-Ac-Ca.md`) — flat, no envelope wrapper:
`Body (body): string?`, `NumSegments (num_segments): string?`, `Direction (direction): MessageEnumDirection?`,
`From (from): string?`, `To (to): string?`, `DateUpdated (date_updated): string?`, `Price (price): string?`,
`ErrorMessage (error_message): string?`, `Uri (uri): string?`, `AccountSid (account_sid): string?`,
`NumMedia (num_media): string?`, `Status (status): MessageEnumStatus?`, `MessagingServiceSid (messaging_service_sid): string?`,
`Sid (sid): string?`, `DateSent (date_sent): string?`, `DateCreated (date_created): string?`, `ErrorCode (error_code): int?`,
`PriceUnit (price_unit): string?`, `ApiVersion (api_version): string?`, `SubresourceUris (subresource_uris): object?`.
NB: `DateSent`/`DateCreated`/`DateUpdated` are **`string?`**, not `DateTimeOffset`; `ErrorCode` is `int?`.

**`ListMessageResponse`** (`TwilioSdk.Models`; map: `records-4-Li-Me.md`) — the page envelope:
`End (end): int?`, `FirstPageUri (first_page_uri): string?`, `NextPageUri (next_page_uri): string?`, `Page (page): int?`,
`PageSize (page_size): int?`, `PreviousPageUri (previous_page_uri): string?`, `Start (start): int?`, `Uri (uri): string?`,
`Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?`. The rows live in **`Messages`**; iterate that, then advance
by `NextPageUri`.

**`LookupResponse`** (`TwilioSdk.Models`; map: `records-4-Li-Me.md`):
`CallingCountryCode (calling_country_code): string?`, `CountryCode (country_code): string?`,
**`PhoneNumber (phone_number): string?`** (the canonical E.164 to store), `NationalFormat (national_format): string?`,
**`Valid (valid): bool?`** (validity signal), `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?`,
`CallerName (caller_name): CallerNameInfo?`, `SimSwap (sim_swap): SimSwapInfo?`, `CallForwarding (call_forwarding): CallForwardingInfo?`,
`LineTypeIntelligence (line_type_intelligence): LineTypeIntelligenceInfo?`, `LineStatus (line_status): LineStatusInfo?`,
`IdentityMatch (identity_match): IdentityMatchInfo?`, `ReassignedNumber (reassigned_number): ReassignedNumberInfo?`,
`SmsPumpingRisk (sms_pumping_risk): SmsPumpingRiskInfo?`, `PhoneNumberQualityScore (phone_number_quality_score): object?`,
`PreFill (pre_fill): object?`, `Url (url): string?`.
NB: `Valid`/`PhoneNumber` come back by default. **Real-time reachability / line status is NOT `Valid`** — `Valid` means
"parseable and in an assigned range". Richer reachability requires opting in via the `fields` query param (e.g. `line_status`,
`line_type_intelligence`), which populates the corresponding sub-objects above.

### 2d. Enums needed (literal C# member `(wire value)`; verbatim from `map/models/enums.md`)

**`MessageEnumStatus`** (`TwilioSdk.Models.Enums`) — the full delivery-status set on `ApiV2010AccountMessage.Status`:
`Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`,
`Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`,
`PartiallyDelivered (partially_delivered)`, `Canceled (canceled)`.

**`MessageEnumScheduleType`** (`TwilioSdk.Models.Enums`): `Fixed (fixed)` — the only member.

**`MessageEnumUpdateStatus`** (`TwilioSdk.Models.Enums`): `Canceled (canceled)` — the only member (used to cancel a scheduled message).

**`ValidationError`** (`TwilioSdk.Models.Enums`) — members of `LookupResponse.ValidationErrors`:
`TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`,
`InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)`.

**`MessageEnumDirection`** (`TwilioSdk.Models.Enums`, on `ApiV2010AccountMessage.Direction`):
`Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)`.

> Enums are `StringEnum<T>`, NOT C# enums — build with `MessageEnumScheduleType.Fixed` (static member) or
> `MessageEnumScheduleType.FromValue("fixed")`; never `MessageEnumScheduleType.fixed`. (See Models trap.)

### 2e. Client construction, auth, and per-host base-URL selection

**Construction / DI.** `new TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)`, or DI:
`services.AddTwilioSdkClient(o => { … });`. Every API group is a property on `client` (`client.Api20100401Message`,
`client.LookupsV2PhoneNumber`). `TwilioSdkClientOptions` properties: `Environment: ServerEnvironment`,
`Retry: RetryOptions`, `Logging: LoggingOptions`, `Server: ServerOptions`, `AccountSidAuthToken: BasicAuthCredentials?`.

**Auth (Account SID + Auth Token).** Single scheme — **basic auth** via
`options.AccountSidAuthToken = new TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials(...)` with the Account SID as the
username and the Auth Token as the password (SDK XML doc: API key/secret preferred; Account SID + Auth Token allowed). Set the
credentials before/at client construction.

**Environment.** `options.Environment` is `TwilioSdk.Servers.ServerEnvironment`; the only member is `ServerEnvironment.Production`.

**Base-URL selection is PER-HOST — this is the crux of brief item 1.** `options.Server` is a `TwilioSdk.ServerOptions` holding
one sub-options object per server group, each with a `Production.BaseUrl` string:
- Messaging (`Api20100401Message.*`, HTTP group "Default (api)") resolves against **`options.Server.Default.Production.BaseUrl`**
  (default `https://api.twilio.com`).
- Lookups (`LookupsV2PhoneNumber.*`, HTTP group "Default4 (lookups)") resolves against **`options.Server.Default4.Production.BaseUrl`**
  (default `https://lookups.twilio.com`).

Therefore apply the `Twilio:BaseUrl` setting **only** to the messaging group:
`options.Server.Default.Production.BaseUrl = config["Twilio:BaseUrl"]`. **Do NOT touch `options.Server.Default4`** — leave the
lookups host at its default (or configure it from a separate setting). Pointing `Default4` at the messaging base URL would send
validation calls to the wrong host. There is **no per-call base-URL override** (`RequestOptions` carries only `LogLevel`), so this
is configured once at client construction. (Source-confirmed against `ServerOptions` / `DefaultOptions` / `Default4Options`.)

---

## 3. Trap notes (name the hazard; load the skill before coding that step — do not implement from the one-liner)

⚠ Step 1 (client + DI) — the `HttpClient`/handler pipeline lifetime and how the SDK client wrapper should be scoped in
ASP.NET Core are not visible in the constructor signature; getting this wrong leaks sockets or captures a stale handler.
**MUST load `dotnet-client-initialization`** before wiring `AddTwilioSdkClient` / `new TwilioSdkClient`.

⚠ Step 1 (auth) — exactly when credentials must be set relative to client construction, and how to source the SID/token from
configuration rather than hardcoding, is a usage concern the property type does not convey. **MUST load `dotnet-authentication`**
before setting `AccountSidAuthToken`.

⚠ Step 1 (base URL / resilience) — the SDK's `Retry`/`Timeout` options do **not** bound a whole call and are **not** the timeout on
the `HttpClient` you register; and whether a failed **write** (CreateMessage POST) can be silently re-sent by the retry layer is
exactly what determines whether you need app-layer idempotency (brief item 8). **MUST load `dotnet-configuration-resilience`**
before tuning retries/timeouts or relying on send-once semantics.

⚠ Step 7 (list pagination) — the map marks `ListMessage` pagination **none**: there is **no auto-pager**. You drive it yourself
via the response `NextPageUri`/`Page`/`PageSize` and the request `page`/`pageToken` params — but how `NextPageUri` maps onto the
next `pageToken`/`page` call, and how to loop the whole range without dropping or repeating a page, is the part the signature does
not show. **MUST load `dotnet-configuration-resilience`** (pagination section) before writing the reconciliation loop.

⚠ Steps 2–8 (models) — request params take `StringEnum<T>` values (`MessageEnumScheduleType.Fixed`, `MessageEnumUpdateStatus.Canceled`),
response fields include enums and nested sub-objects, and unmodeled JSON is dropped on deserialize; building or reading these wrong
compiles but misbehaves. **MUST load `dotnet-models`** before constructing request payloads or mapping `LookupResponse`/`ApiV2010AccountMessage`.

⚠ Steps 2–8 (error boundary) — every operation here is Case B (`SdkException<RawError>`) with **no typed accessor**; distinguishing
"accepted-but-undeliverable" from a real failure, and a validation rejection from a transient error, depends on reading status/body
the documented way rather than parsing `.ToString()`. **MUST load `dotnet-error-handling`** before writing any try/catch (see REQUIRED READING).

---

## 4. REQUIRED READING (load BEFORE implementation starts — this sheet deliberately does not carry their contents)

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — construction, options/builder shape, HttpClient lifetime, DI registration |
| `dotnet-authentication` | Step 1 — supplying Account SID + Auth Token as `BasicAuthCredentials`, when to set them |
| `dotnet-configuration-resilience` | Step 1 & Step 7 — base-URL/server selection, retries/timeouts, and list pagination |
| `dotnet-calling-endpoints` | Steps 2–8 — calling ops with named args (many optional params have no default and mis-bind positionally), async/`ct` |
| `dotnet-models` | Steps 2–8 — building request models, `StringEnum<T>` enums, nested response objects, dropped-unknown-fields |
| `dotnet-error-handling` | Steps 2–8 — the Case B error boundary, reading status code and provider error body safely |
| `dotnet-testing` | Test step — faking the `HttpClient` seam for the integration layer |

**Two `System.Text.Json.JsonException` hazards that MUST shape the error boundary from the FIRST version (a boundary written
without these is wrong, and a later revision arrives too late):**
- A drifted or malformed **2xx** body (e.g. a missing `required` member) surfaces as a `JsonException` from deserialization,
  **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated error shape throws `JsonException` *while the error object is
  being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary
  that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx
  retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

- **UNVERIFIED (live traffic only):** The exact provider JSON error-body shape (`code`/`message`/`more_info`/`status`) is
  on-the-wire, not a generated model on Case B. Directive: read it best-effort via `ex.Error.ReadAsJson<T>()` into a small DTO
  and **fall back to `ex.Error.ReadAsString()` / `ex.Error.StatusCode`** if fields are absent; never assume the body parses.
- **UNVERIFIED (live traffic only):** How the provider signals a truly non-existent/unparseable number on Lookup — a 200 with
  `Valid == false` vs a 404 `SdkException<RawError>` — is not settleable from the map/source. Directive: treat **both** as
  "reject and do not store": accept only `Valid == true` with a non-empty `PhoneNumber`, and in the `catch (SdkException<RawError>)`
  treat a 404 as an invalid number (reject) while 5xx/timeouts are transient (retry/surface).
- **UNVERIFIED (live traffic only):** The exact status code the provider returns when cancelling an already-sent message
  (cap 5) is not in the map/source. Directive: on `SdkException<RawError>` from `UpdateMessage`, read `ex.Error.StatusCode`;
  treat a 4xx as "too late / already sent" (do not retry) and 5xx/timeout as transient.
- **UNVERIFIED (live traffic only):** That sending `body: ""` (empty string) is the precise value the provider accepts to redact
  the body (cap 8) — the SDK map only states `UpdateMessage` is the redaction route. Directive: send empty string; verify against
  the returned `ApiV2010AccountMessage.Body` and surface a clear error if the body is not cleared.
- **Assumption:** "immediate send" uses `from` = the configured `FromNumber`; provider-side scheduling (caps 3) uses
  `messagingServiceSid` and omits `from`, per the SDK's `MessageEnumScheduleType` doc (Messaging Services only). Confirm the app
  has a `MessagingServiceSid` configured for the scheduled follow-up path.
- **No SDK-map gaps.** Every requested capability is covered; the only open items are the live-traffic behaviors above, each
  converted to a concrete defensive directive.
