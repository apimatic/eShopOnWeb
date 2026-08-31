# Twilio .NET SDK integration plan — eShopOnWeb `src/PublicApi` (SMS order notifications)

Grounded against the bundled SDK map (`twilio-getting-started` skill, source commit `51fdf48`) plus
scoped reads of the map-named source files at that pinned commit. Every sheet row cites its map page.

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 1 | Add NuGet package `AsadAli.TwilioSdk` (version-less: `dotnet add package AsadAli.TwilioSdk`) to `src/PublicApi`; bind config keys `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid`, `Twilio:BaseUrl` (optional) | — |
| 2 | DI-register the client with auth + conditional messaging-only base-URL override | — (client construction) |
| 3 | Validate + canonicalize shopper phone at registration | `LookupsV2PhoneNumber.FetchPhoneNumber3` |
| 4 | Send immediate order SMS | `Api20100401Message.CreateMessage` |
| 5 | On order dispatch: schedule follow-up SMS with the provider (send-at = now + 3 days); store returned `Sid` | `Api20100401Message.CreateMessage` (scheduled) |
| 6 | On order cancel: cancel the not-yet-sent follow-up at the provider | `Api20100401Message.UpdateMessage` (status) |
| 7 | Pull-based delivery-outcome sync (no webhooks): poll by stored provider SID | `Api20100401Message.FetchMessage` |
| 8 | On shopper request: redact message body at the provider (record + outcome survive) | `Api20100401Message.UpdateMessage` (body) |
| 9 | Reconciliation report: server-side filtered list by sender + date-sent range, manual pagination | `Api20100401Message.ListMessage` |
| 10 | Error boundary + map `MessageEnumStatus` onto the app's notification-state model | all |

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

### SDK identity & client construction

| Fact | Value | Source |
|---|---|---|
| NuGet package | `AsadAli.TwilioSdk` — install version-less | `sdk-map.md` |
| Root namespace | `TwilioSdk` | `sdk-map.md` |
| Client class | `TwilioSdk.TwilioSdkClient` — ctor `TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)` | `sdk-map.md` (`TwilioSdkClient.cs`) |
| Options class | `TwilioSdk.TwilioSdkClientOptions` — props: `Environment: ServerEnvironment`, `Retry: RetryOptions`, `Logging: LoggingOptions`, `Server: ServerOptions`, `AccountSidAuthToken: BasicAuthCredentials?` | `sdk-map.md` (`TwilioSdkClientOptions.cs`) |
| DI registration | `TwilioSdk.ServiceCollectionExtensions.AddTwilioSdkClient(this IServiceCollection, Action<TwilioSdkClientOptions>? configure = null)` — registers the client as a singleton over `IHttpClientFactory` | `ServiceCollectionExtensions.cs` (pinned source) |
| Auth | `AccountSidAuthToken = new TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials { Username = <Twilio:AccountSid>, Password = <Twilio:AuthToken> }` — both members `required string`, init-only. (Source XML doc: an API key SID + secret is the preferred production credential; account SID + auth token works.) | `sdk-map.md` *Servers & auth*; `Core/Authentication/Basic/BasicAuthCredentials.cs` (pinned source) |
| Environment | `TwilioSdk.Servers.ServerEnvironment.Production` — the only member; already the default | `sdk-map.md` *Servers & auth* |
| Async / cancellation | Every operation returns `Task` / `Task<T>` and takes `CancellationToken ct = default` — pass the request's `ct` through | all operation rows |
| Per-request options | Last-but-one param `TwilioSdk.Core.RequestOptions? requestOptions = null` on every op — pass nothing (leave default) | `Core/RequestOptions.cs` (pinned source) |

### Base-URL override — messaging only (scope item 8)

`TwilioSdk.ServerOptions` (repo-root file ⇒ root namespace) has one property per named server,
`Default` … `Default14`; each is a `TwilioSdk.Servers.Default{N}Options` with a settable
`Production.BaseUrl: string`. The value is used verbatim as the base address for every operation
on that server.

| Server property | Default `Production.BaseUrl` | Used by |
|---|---|---|
| `options.Server.Default` | `https://api.twilio.com` | **all 5 messaging ops** (`Api20100401Message.*` — each executes `_server.Default(...)`; map rows label them "Default (api)") |
| `options.Server.Default4` | `https://lookups.twilio.com` | **Lookup v1 + v2** (`LookupsV2PhoneNumber.FetchPhoneNumber3` executes `_server.Default4(...)`; map rows label them "Default4 (lookups)") |

**Directive:** when `Twilio:BaseUrl` is set, assign ONLY
`options.Server.Default.Production.BaseUrl = <Twilio:BaseUrl>` inside the `AddTwilioSdkClient`
configure callback. Never touch `Default4` — Lookup keeps hitting `https://lookups.twilio.com`.
(Sources: `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Servers/Default4Options.cs`,
`Api/Api20100401Message.cs`, `Api/LookupsV2PhoneNumber.cs` — all pinned-source reads; the map does
not carry the `ServerOptions` member list, this is the one source-resolved row in this sheet.)

### Operations

All five messaging ops are accessed via `client.Api20100401Message`; Lookup via
`client.LookupsV2PhoneNumber`. **Every in-scope operation is error Case B** — throws
`TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`; no typed
`{Operation}Error`, no no-throw `…Result` variant exists anywhere in this SDK
(`sdk-map.md` error-handling model + per-op rows).

#### CreateMessage — send now (step 4) and schedule (step 5) — `operations/Api20100401Message.md`

`POST /2010-04-01/Accounts/{AccountSid}/Messages.json`, form-url-encoded body, SDK auto-adds an
`Idempotency-Key: Guid.NewGuid()` header (pinned source).

```csharp
Task<TwilioSdk.Models.ApiV2010AccountMessage> CreateMessage(
    string accountSid, string to,
    string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback,
    int? attempt, int? validityPeriod, bool? forceDelivery,
    MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention,
    bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType,
    bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt,
    bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck,
    string? from, string? fallbackFrom, string? messagingServiceSid,
    string? body, IReadOnlyList<string>? mediaUrl, string? contentSid,
    RequestOptions? requestOptions = null, CancellationToken ct = default)
```

The 24 nullable params have **no C# defaults — pass each explicitly** (`null` to skip); use named
arguments. Enum params live in `TwilioSdk.Models.Enums`.

- **Immediate send (step 4):** `accountSid` = `Twilio:AccountSid`, `to` = canonical shopper number,
  `body` = text, and exactly ONE sender param: `from: <Twilio:FromNumber>` **or**
  `messagingServiceSid: <Twilio:MessagingServiceSid>` — never both in one call (whether the
  provider rejects both-present is not stated in map/source — `UNVERIFIED`; defensive rule: pass
  exactly one). All other params `null`.
- **Scheduled send (step 5):** `scheduleType: MessageEnumScheduleType.Fixed` + `sendAt:` a
  `DateTimeOffset` (wire `SendAt`) + `messagingServiceSid:` — the `MessageEnumScheduleType` map
  doc states scheduling is **"For Messaging Services only"** (`enums.md`), so the scheduled path
  uses `Twilio:MessagingServiceSid`, not `FromNumber`. Min/max lead-time constraints on `sendAt`
  are **not carried by the map or the source XML docs** — `UNVERIFIED`; defensive rule: compute
  `sendAt` app-side (dispatch time + 3 days), and treat a provider rejection of the create call as
  a possible outcome surfaced through the error boundary, never as impossible.
- **Returns** `ApiV2010AccountMessage` (fields below) — persist `Sid` from the response.

#### FetchMessage — pull current state (step 7) — `operations/Api20100401Message.md`

`Task<ApiV2010AccountMessage> FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)`
— `GET …/Messages/{Sid}.json`. Read `Status`, `ErrorCode`, `ErrorMessage` off the response.

#### UpdateMessage — cancel (step 6) and redact (step 8) — `operations/Api20100401Message.md`

`Task<ApiV2010AccountMessage> UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)`
— `POST …/Messages/{Sid}.json`, form body `Body`/`Status`, auto idempotency header. `body` and
`status` must both be passed explicitly (`null` = leave unchanged). Source remarks: *"used to
redact Message `body` text and to cancel not-yet-sent messages."*

- **Cancel:** `status: MessageEnumUpdateStatus.Canceled` (the enum's only value), `body: null`.
  Which current statuses make a cancel valid is **not stated in map/source** — `UNVERIFIED`;
  defensive rule: attempt cancel only when the app's last observed status is `Scheduled` (or
  not-yet-sent), and treat a provider rejection as a deterministic 4xx through the error
  boundary — do not retry it.
- **Redact:** `body: ""` (empty string), `status: null`. This blanks the body while the message
  record (Sid, status, dates, error info) survives. `DeleteMessage` exists
  (`Task DeleteMessage(string accountSid, string sid, …)` → `void`, `DELETE …/Messages/{Sid}.json`)
  but deletes the **whole resource** — it would destroy the outcome record the requirement says
  must survive. Use `UpdateMessage` for redaction; do not use `DeleteMessage` for this feature.

#### ListMessage — reconciliation (step 9) — `operations/Api20100401Message.md`

```csharp
Task<TwilioSdk.Models.ListMessageResponse> ListMessage(
    string accountSid, string? to, string? from,
    DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery,
    long? pageSize, int? page, string? pageToken,
    RequestOptions? requestOptions = null, CancellationToken ct = default)
```

Wire mapping (map row + pinned source): `To` ← `to` · `From` ← `from` · `DateSent` ← `dateSent`
(exact day) · `DateSent<` ← `dateSentQuery` (sent **before**) · `DateSent>` ← `dateSentQueryQuery`
(sent **after**) · `PageSize` ← `pageSize` (doc: default 50, max 1000) · `Page` · `PageToken`.
Dates serialize as full ISO-8601 (`ToIso8601()`), GMT per the XML doc.

- **Server-side filter directive:** pass `from: <Twilio:FromNumber>`,
  `dateSentQueryQuery: <rangeStart>`, `dateSentQuery: <rangeEnd>` — the provider applies all three;
  do not fetch wide and filter client-side.
- **Boundary inclusivity** of `DateSent<` / `DateSent>` is **not stated in map/source** (the wire
  operator implies strict; the doc's "on and before/after" phrasing describes an in-value `<=`/`>=`
  syntax the typed `DateTimeOffset?` params cannot express) — `UNVERIFIED`; defensive rule: if the
  exact boundary instant matters, widen the range and de-duplicate by `Sid`.
- **Pagination:** the map lists no built-in pager for this op — loop manually with
  `page`/`pageToken`, following `ListMessageResponse.NextPageUri` until null.

#### FetchPhoneNumber3 — Lookup v2 validation (step 3) — `operations/LookupsV2PhoneNumber.md`

`GET /v2/PhoneNumbers/{PhoneNumber}` on server `Default4` (lookups host — **not** governed by
`Twilio:BaseUrl`).

```csharp
Task<TwilioSdk.Models.LookupResponse> FetchPhoneNumber3(
    string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName,
    string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode,
    string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate,
    string? verificationSid, string? partnerSubId,
    RequestOptions? requestOptions = null, CancellationToken ct = default)
```

Pass `phoneNumber` (the path param, as typed) and `null` for all 15 optional params — base
validity needs no `fields`. (**Do not** use v1 `LookupsV1PhoneNumberApi.FetchPhoneNumber2`: its
response `LookupsV1PhoneNumber` has **no `Valid` field** — `records-4-Li-Me.md`.)

### Response models (fields the integration reads)

`ApiV2010AccountMessage` (`TwilioSdk.Models`; `records-1-Ac-Ca.md`) — returned by Create/Fetch/Update:

`Sid (sid): string?` · `To (to): string?` · `From (from): string?` · `Body (body): string?` ·
`Status (status): MessageEnumStatus?` · `DateSent (date_sent): string?` ·
`DateCreated (date_created): string?` · `DateUpdated (date_updated): string?` ·
`ErrorCode (error_code): int?` · `ErrorMessage (error_message): string?` ·
`MessagingServiceSid (messaging_service_sid): string?` · `NumSegments (num_segments): string?` ·
`Price (price): string?` · `PriceUnit (price_unit): string?` ·
`Direction (direction): MessageEnumDirection?` · `AccountSid (account_sid): string?` ·
`Uri (uri): string?` · `ApiVersion (api_version): string?` · `NumMedia (num_media): string?` ·
`SubresourceUris (subresource_uris): object?`

⚠ All three date fields are `string?`, **not** `DateTimeOffset` — parse app-side.

`ListMessageResponse` (`TwilioSdk.Models`; `records-4-Li-Me.md`):
`Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?` · `NextPageUri (next_page_uri): string?` ·
`PreviousPageUri (previous_page_uri): string?` · `FirstPageUri (first_page_uri): string?` ·
`Page (page): int?` · `PageSize (page_size): int?` · `Start (start): int?` · `End (end): int?` ·
`Uri (uri): string?`

`LookupResponse` (`TwilioSdk.Models`; `records-4-Li-Me.md`):
`Valid (valid): bool?` · `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?` ·
`PhoneNumber (phone_number): string?` ← **the canonical form to store** ·
`NationalFormat (national_format): string?` · `CountryCode (country_code): string?` ·
`CallingCountryCode (calling_country_code): string?` · `Url (url): string?` · (plus optional
paid-field records: `CallerName`, `SimSwap`, `CallForwarding`, `LineTypeIntelligence`,
`LineStatus`, `IdentityMatch`, `ReassignedNumber`, `SmsPumpingRisk` — all null when `fields` is
null). Validation rule: accept only when `Valid == true`; store `PhoneNumber`, never the typed
input. Whether some invalid numbers surface as an error status instead of `Valid == false` is
`UNVERIFIED` — defensive rule: an `SdkException<RawError>` from Lookup means "validity
unconfirmed" ⇒ reject the registration.

### Enums (`TwilioSdk.Models.Enums`; `enums.md`) — `StringEnum<T>`, not C# enums

Use the static members (e.g. `MessageEnumScheduleType.Fixed`) or `…FromValue("wire")`.

| Enum | Members (wire values) |
|---|---|
| `MessageEnumStatus` (message state model, scope item 10) | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` |
| `MessageEnumScheduleType` | `Fixed (fixed)` — sole value |
| `MessageEnumUpdateStatus` | `Canceled (canceled)` — sole value |
| `ValidationError` (Lookup) | `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)` |

### Error handling (scope item 9)

Every in-scope op throws `SdkException<RawError>` on any error status — including invalid number
at send time, message-not-found on fetch/update, and cancel-of-already-sent. Read:
`ex.Error.StatusCode` (`HttpStatusCode`), `ex.Error.ReadAsString()`,
`ex.Error.ReadAsJson<T>()`, `ex.Error.ReadAsBytes()`. **401 and 403 arrive through this same
Case-B path** — there is no distinct auth-failure exception type; branch on `StatusCode`.
Provider error code/message live in the raw body — parse via `ReadAsJson<T>()` into an
app-owned shape. (`sdk-map.md` error-handling model + all six op rows.)

## 3. Trap notes

> ⚠ Step 2 (client registration) — the `HttpClient`/handler pipeline behind the client has
> lifetime rules the ctor signature hides, and the DI extension's singleton-over-factory shape is
> not automatically the right registration for every app. **MUST load
> `dotnet-client-initialization`** before wiring DI.

> ⚠ Step 2 (auth) — when credentials must be set relative to client construction, and how secrets
> flow from configuration without leaking, is not visible from the options type. **MUST load
> `dotnet-authentication`** before setting `AccountSidAuthToken`.

> ⚠ Steps 3–9 (every call) — the 24/15/8-nullable-parameter signatures mis-bind silently in
> positional calls, and the must-pass-explicitly rule is a compile-time silent trap. **MUST load
> `dotnet-calling-endpoints`** before the first call.

> ⚠ Steps 3–10 (models) — enums are `StringEnum<T>` (not C# enums), records are immutable with
> `required` init members, and unmodeled JSON fields are dropped on deserialize — what this costs
> when mapping `ApiV2010AccountMessage`/`LookupResponse` onto domain types is not visible from the
> field list. **MUST load `dotnet-models`**.

> ⚠ Step 10 (error boundary) — every op here is Case B (`RawError`, no typed accessors), so the
> boundary must extract status/code/message without `TryGet…` helpers, and `JsonException` reaches
> the boundary from two directions needing opposite handling (see REQUIRED READING). **MUST load
> `dotnet-error-handling`** before writing any `try/catch`.

> ⚠ Steps 2, 4–6 (resilience) — what `Retry`/`Timeout` actually bound, and whether a failed
> `CreateMessage`/`UpdateMessage` write can be re-sent by the retry layer, is not answerable from
> the options' member names; a non-idempotent write retried behind your back is the concrete
> failure. **MUST load `dotnet-configuration-resilience`** before tuning or accepting defaults.

> ⚠ Step 9 (pagination) — `ListMessage` has no built-in pager; the manual `page`/`pageToken`/
> `NextPageUri` loop has termination pitfalls the response fields alone don't show. **MUST load
> `dotnet-configuration-resilience`** (list pagination) before writing the reconciliation loop.

> ⚠ Tests — the SDK's test seam is a specific ctor argument, not an interface; stubbing the wrong
> seam couples tests to SDK internals. **MUST load `dotnet-testing`** before writing integration
> tests.

## 4. REQUIRED READING — load ALL of these before implementation starts

This sheet deliberately does not carry these skills' contents; loading them is part of the plan.

- `dotnet-client-initialization` — step 2 (client construction & DI).
- `dotnet-authentication` — step 2 (credentials wiring).
- `dotnet-calling-endpoints` — steps 3–9 (every operation call).
- `dotnet-models` — steps 3–10 (request/response models, enums, wire names).
- `dotnet-error-handling` — step 10 (the error boundary). Two `System.Text.Json.JsonException`
  hazards reach the boundary and need opposite handling:
  - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
    `JsonException` from deserialization, **not** as an `SdkException` — so an
    SDK-exception-only catch ladder lets it escape the integration boundary;
  - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
    throws `JsonException` *while the error object is being constructed*, so the `JsonException`
    **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
    maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
    and a caller that retries 5xx retries something that can never succeed.

  **MUST load `dotnet-error-handling`** before writing that boundary.
- `dotnet-configuration-resilience` — steps 2, 4–6, 9 (retries/timeout semantics, list pagination).
- `dotnet-testing` — test seam for the integration layer.

## 5. Assumptions & Blockers

- **Assumption:** Lookup v2 (`FetchPhoneNumber3`) is the validation capability — chosen because its
  `LookupResponse` carries `Valid`/`ValidationErrors`/`PhoneNumber`; v1's response has no validity
  field (map evidence). Assumed the free base lookup (no paid `fields`) satisfies "usable
  destination".
- **Assumption:** redaction means `UpdateMessage(body: "")` per the operation's own remarks;
  `DeleteMessage` is documented here only to rule it out for this requirement.
- **Assumption:** the integration targets the SDK line the map documents (source commit `51fdf48`,
  "Publish v2.0.0 SDK"). **Drift notice:** the SDK repo's `main` has since been regenerated under a
  new codegen (commit `3d2efed`) that renames the root namespace to `Twilio`, the client to
  `TwilioClient`, and options to `TwilioClientOptions`. If the installed NuGet package fails to
  compile against the names in this sheet, trust the compiler and report back for a re-grounded
  sheet — do not patch names from memory.
- **UNVERIFIED (live-traffic only, defensive directives given inline):** min/max scheduling lead
  time for `sendAt`; which current statuses make a cancel valid; `DateSent<`/`DateSent>` boundary
  inclusivity; whether passing both `from` and `messagingServiceSid` is rejected; whether Lookup
  ever answers an invalid number with an error status instead of `Valid == false`.
- **Blockers:** none.
