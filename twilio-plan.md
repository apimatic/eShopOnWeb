# Twilio .NET SDK integration plan — eShopOnWeb SMS order notifications

**SDK**: NuGet `AsadAli.TwilioSdk` — install **version-less** (`dotnet add package AsadAli.TwilioSdk`, floats to latest; this sheet is grounded at source commit `51fdf48`, the "Publish v2.0.0 SDK" release). SDK targets `netstandard2.0` (C# LangVersion 14, nullable enabled) ⇒ compatible with the app's `net8.0`. Root namespace `TwilioSdk`; client `TwilioSdkClient`; options `TwilioSdkClientOptions`.

## 1. Scope & sequence

| # | Step (where it lands in eShopOnWeb) | SDK operation(s) |
|---|---|---|
| 1 | Client construction, auth, messaging-only base-URL override, DI registration (Infrastructure) | — (client options only) |
| 2 | Validate + canonicalize contact number at shopper registration (ApplicationCore service → Infrastructure gateway) | `FetchPhoneNumber3` (Lookup v2) |
| 3 | Send order-confirmation SMS immediately (Infrastructure SMS sender) | `CreateMessage` |
| 4 | Schedule follow-up SMS days later, provider-held (same sender) | `CreateMessage` + `messagingServiceSid`/`scheduleType`/`sendAt` |
| 5 | Cancel a scheduled follow-up (e.g. order canceled) | `UpdateMessage` (`status: Canceled`) |
| 6 | Poll delivery outcome by SID (no webhooks; background poller) | `FetchMessage` |
| 7 | Nightly reconciliation: list all messages from our number in a date range, all pages | `ListMessage` |
| 8 | Redact message body after retention window (record survives); full delete only if disposal intended | `UpdateMessage` (`body: ""`) / `DeleteMessage` |
| 9 | Error boundary around all of the above + tests | — |

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

**Namespaces used below** (add one `using` per line as needed — C# does not import child namespaces transitively):

| Types | Namespace |
|---|---|
| `TwilioSdkClient`, `TwilioSdkClientOptions`, `ServerOptions` | `TwilioSdk` |
| `ServerEnvironment`, `DefaultOptions`, `Default4Options` | `TwilioSdk.Servers` |
| `RetryOptions` | `TwilioSdk.Core.Configuration` |
| `BasicAuthCredentials` | `TwilioSdk.Core.Authentication.Basic` |
| `RequestOptions` | `TwilioSdk.Core` |
| `SdkException<T>` | `TwilioSdk.Core.Exceptions` |
| `RawError` | `TwilioSdk.Core.ErrorResponse` |
| `ApiV2010AccountMessage`, `ListMessageResponse`, `LookupResponse` | `TwilioSdk.Models` |
| `MessageEnumStatus`, `MessageEnumScheduleType`, `MessageEnumUpdateStatus`, `MessageEnumDirection`, `ValidationError` | `TwilioSdk.Models.Enums` |

### Operation rows

**Row 1 — Validate/canonicalize number** (map: `operations/LookupsV2PhoneNumber.md`, `records-4-Li-Me.md`, `enums.md`)

- **Call**: `client.LookupsV2PhoneNumber.FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` — the 15 params `fields`…`partnerSubId` are nullable with no default ⇒ **must be passed explicitly** (pass `null`); use named arguments.
- **Wire**: path `GET /v2/PhoneNumbers/{PhoneNumber}`; query `Fields`←`fields`, `CountryCode`←`countryCode` (remaining query params are identity-match packages — pass `null`, out of scope).
- **Our usage**: `FetchPhoneNumber3(phoneNumber: rawInput, fields: null, countryCode: defaultRegion /* e.g. "US" when input is national format; null if E.164 */, …all others null…)`. Source param docs: `phoneNumber` accepts E.164 or national format (default country +1); `countryCode` is the ISO 3166-1 alpha-2 code used when the number is national-format.
- **Returns** `LookupResponse` — fields the integration reads: `Valid (valid): bool?`, `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?`, `PhoneNumber (phone_number): string?` ← **the E.164 canonical form to store**, `NationalFormat (national_format): string?`, `CountryCode (country_code): string?`, `CallingCountryCode (calling_country_code): string?`. (Also `CallerName`, `SimSwap`, `LineTypeIntelligence`, etc. — paid add-on packages, not requested ⇒ stay `null`.)
- **Invalid-number behavior**: the 200-response model itself carries `Valid`/`ValidationErrors` ⇒ an unusable number can come back as a **successful** call with `Valid != true` and `ValidationErrors` populated (enum values below). Whether some malformed inputs instead surface as non-2xx (e.g. 404) is `UNVERIFIED` (only live traffic confirms) ⇒ **directive: treat `Valid != true` OR a caught `SdkException<RawError>` with a 4xx `StatusCode` both as "invalid destination"; log `ReadAsString()` for diagnostics.**
- **Error**: `SdkException<RawError>` — **Case B** (no typed accessors). Accessors: `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`, `ReadAsBytes(): ReadOnlyMemory<byte>`.
- Lookup v1 (`client.LookupsV1PhoneNumberApi.FetchPhoneNumber2`, returns `LookupsV1PhoneNumber`) exists but its response has **no `Valid` field** (map: `records-4-Li-Me.md`) ⇒ v2 is the validation capability; do not use v1.

**Row 2 — Send SMS immediately** (map: `operations/Api20100401Message.md`, `records-1-Ac-Ca.md`)

- **Call**: `client.Api20100401Message.CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` — the 24 params `statusCallback`…`contentSid` are nullable with no default ⇒ **must be passed explicitly** (pass `null`); use named arguments.
- **Wire** (form/query params): `To`←`to`, `From`←`from`, `Body`←`body`, `MessagingServiceSid`←`messagingServiceSid`, `ScheduleType`←`scheduleType`, `SendAt`←`sendAt` (full 26-name map on the operation page).
- **Immediate-send profile**: `accountSid` (config), `to` (E.164 from step 2), `from`: the account's sending number, `body`: text, **everything else `null`** (`scheduleType`/`sendAt`/`messagingServiceSid` stay `null`).
- **Returns** `ApiV2010AccountMessage` (flat record, no envelope): `Sid (sid): string?` ← **persist as the message key**, `Status (status): MessageEnumStatus?` ← initial status (do not assert a specific value — see UNVERIFIED note in Row 3), `To (to)`, `From (from)`, `Body (body)`, `ErrorCode (error_code): int?`, `ErrorMessage (error_message): string?`, `DateCreated/DateSent/DateUpdated (date_created/date_sent/date_updated): string?` (strings, not DateTime — parse defensively), `NumSegments (num_segments)`, `Price (price)`, `PriceUnit (price_unit)`, `Direction (direction): MessageEnumDirection?`, `MessagingServiceSid (messaging_service_sid)`, `AccountSid (account_sid)`, `Uri (uri)`, `ApiVersion (api_version)`, `NumMedia (num_media)`, `SubresourceUris (subresource_uris): object?`.
- **Error**: `SdkException<RawError>` — **Case B**. Read `StatusCode` + `ReadAsString()`; Twilio's error JSON is not a generated model here ⇒ extract best-effort via `ReadAsJson<T>()` with an app-side shape, fall back to the raw string.
- **Pagination**: none.

**Row 3 — Send SMS scheduled (provider-held)** (same operation page as Row 2)

- Same `CreateMessage` signature. **Scheduled profile**: `messagingServiceSid`: the Messaging Service SID (config `Twilio:MessagingServiceSid`; `from` stays `null` — the service owns the sender pool), `scheduleType`: `MessageEnumScheduleType.Fixed`, `sendAt`: `DateTimeOffset` of the desired send time, plus `to`/`body`/`accountSid`.
- Map-visible evidence that scheduling is Messaging-Service-only: `MessageEnumScheduleType` has the single value `Fixed (fixed)` whose description reads "For Messaging Services only: Include this parameter with a value of `fixed` in conjuction with the `send_time` parameter in order to schedule a Message" (`enums.md`).
- **SendAt window constraints (min lead time / max horizon)**: NOT carried by the map or the source XML docs (param docs are empty), and the signature accepts any `DateTimeOffset?` — the SDK performs no client-side validation. `UNVERIFIED` (only Twilio docs/live traffic carry the window) ⇒ **directive: take the window from Twilio's scheduling docs at implementation time, validate app-side before calling, and treat a 400 `SdkException<RawError>` on create as a rejected schedule (surface `ReadAsString()`), not a retryable outage.**
- **Status of a scheduled message**: `MessageEnumStatus` includes `Scheduled (scheduled)` and `Accepted (accepted)`; which one a create returns is `UNVERIFIED` ⇒ **directive: persist `Sid` + whatever `Status` comes back; never branch on an assumed initial status.**

**Row 4 — Cancel a scheduled message** (map: `operations/Api20100401Message.md`, `enums.md`)

- **Call**: `client.Api20100401Message.UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `body` and `status` are nullable with no default ⇒ **must be passed explicitly**. Wire: `Body`←`body`, `Status`←`status`. HTTP `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json`.
- **Cancel profile**: `UpdateMessage(accountSid, sid, body: null, status: MessageEnumUpdateStatus.Canceled)`. `MessageEnumUpdateStatus` has exactly one value: `Canceled (canceled)`.
- **Which current statuses allow cancellation**: not enumerated in map/source ⇒ `UNVERIFIED` **directive: attempt cancel only when the last polled status is `Scheduled`/`Accepted`/`Queued`; on a 4xx `SdkException<RawError>` treat the cancel as "too late / not cancellable" (terminal — re-fetch the message and reconcile), never as retryable.**
- **Returns** `ApiV2010AccountMessage` (same fields as Row 2; expect `Status` → `Canceled (canceled)` on success).
- **Error**: `SdkException<RawError>` — **Case B**. No no-throw variant exists (none do in this SDK).

**Row 5 — Fetch message by SID (polling)** (map: `operations/Api20100401Message.md`)

- **Call**: `client.Api20100401Message.FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `ApiV2010AccountMessage`. HTTP `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json`.
- **Read**: `Status` (`MessageEnumStatus?`), `ErrorCode (error_code): int?` (provider error code on failed/undelivered), `ErrorMessage (error_message): string?`, `DateSent (date_sent): string?`.
- **Terminal vs in-flight**: terminal = `Delivered`, `Undelivered`, `Failed`, `Canceled`; in-flight = `Queued`, `Sending`, `Sent`, `Accepted`, `Scheduled` (full enum below). Poll until terminal.
- **Error**: `SdkException<RawError>` — **Case B** (a 404 here means unknown/deleted SID ⇒ terminal for polling).

**Row 6 — List messages for reconciliation** (map: `operations/Api20100401Message.md`, `records-4-Li-Me.md`)

- **Call**: `client.Api20100401Message.ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` — the 8 params `to`…`pageToken` are nullable with no default ⇒ **must be passed explicitly**; use named arguments.
- **Wire**: `To`←`to`, `From`←`from`, `DateSent`←`dateSent`, **`DateSent<`←`dateSentQuery`** (sent BEFORE), **`DateSent>`←`dateSentQueryQuery`** (sent AFTER), `PageSize`←`pageSize`, `Page`←`page`, `PageToken`←`pageToken`. ⚠ The generated names are anti-intuitive: `dateSentQuery` = upper bound, `dateSentQueryQuery` = lower bound — copy this mapping, do not reason from the C# names.
- **Reconciliation profile**: `from`: our sending number (E.164), `dateSentQueryQuery`: range start, `dateSentQuery`: range end, `pageSize`: e.g. 100, `to`/`dateSent`: `null`.
- **Returns** `ListMessageResponse`: `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?` (item fields as Row 2), `NextPageUri (next_page_uri): string?`, `PreviousPageUri (previous_page_uri): string?`, `Page (page): int?`, `PageSize (page_size): int?`, `Start (start)`, `End (end)`, `FirstPageUri (first_page_uri)`, `Uri (uri)`.
- **Pagination**: the SDK has **no built-in paginator** (map row: "Pagination: none"). Enumerate the whole range manually: loop while the returned `NextPageUri` is non-null — primary strategy: parse the `PageToken` query value out of `NextPageUri` and pass it as `pageToken` with all other arguments identical; fallback if the token is absent/unparseable: increment `page` until a page returns fewer items than `pageSize`. The exact token format inside `next_page_uri` is `UNVERIFIED` (visible only on a live response) ⇒ implement the parse defensively and keep the page-increment fallback.
- **Error**: `SdkException<RawError>` — **Case B**.

**Row 7 — Redact body / delete record** (map: `operations/Api20100401Message.md`)

- **Redact (keep the record)** — the requirement's default: `UpdateMessage(accountSid, sid, body: "", status: null)`. The operation's own summary: "Update a Message resource (used to redact Message `body` text and to cancel not-yet-sent messages)". Returns the updated `ApiV2010AccountMessage`; the record (SID, status, outcome) survives with the body emptied.
- **Delete (record gone)**: `client.Api20100401Message.DeleteMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` → returns `void` (`Task`). Summary: "Deletes a Message resource from your account" — the whole resource is removed; afterwards `FetchMessage` for that SID should be expected to 404 (treat as Case-B `SdkException<RawError>`). Use only when full disposal is intended — it destroys the outcome record the brief says must survive.
- **Error** (both): `SdkException<RawError>` — **Case B**.

### Enum value tables (verbatim from `map/models/enums.md`; enums are `StringEnum<T>` records — use the static members, e.g. `MessageEnumScheduleType.Fixed`, or `MessageEnumStatus.FromValue("queued")`; they are NOT C# enums)

| Enum | Members (C# member ← wire value) |
|---|---|
| `MessageEnumStatus` | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` |
| `MessageEnumScheduleType` | `Fixed (fixed)` |
| `MessageEnumUpdateStatus` | `Canceled (canceled)` |
| `MessageEnumDirection` | `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)` |
| `ValidationError` | `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)` |

### Client construction, auth, and the messaging-only base-URL override (map: `sdk-map.md`; source-verified at the pinned commit)

```csharp
// Infrastructure DI registration (shape only — load dotnet-client-initialization before writing this)
services.AddTwilioSdkClient(o =>   // o: TwilioSdkClientOptions (namespace TwilioSdk)
{
    o.AccountSidAuthToken = new BasicAuthCredentials   // TwilioSdk.Core.Authentication.Basic
    {
        Username = cfg["Twilio:AccountSid"],           // required init
        Password = cfg["Twilio:AuthToken"],            // required init
    };
    // o.Environment defaults to ServerEnvironment.Production (only member) — leave it.

    var baseUrl = cfg["Twilio:BaseUrl"];
    if (!string.IsNullOrWhiteSpace(baseUrl))
        o.Server.Default.Production.BaseUrl = baseUrl; // messaging API group ONLY
});
```

- **Auth**: single credentials property `TwilioSdkClientOptions.AccountSidAuthToken: BasicAuthCredentials?` (HTTP Basic; `Username`/`Password` are `required` init-only). Source doc note: an API key + secret is the recommended username/password; account SID + auth token works (this integration uses it per the brief).
- **Base-URL model** (source-verified): `ServerOptions` (root namespace `TwilioSdk`) has one property per server group — `Default`, `Default1`…`Default14`. Each `Api20100401Message` operation is bound to server group **"Default (api)"**; the Lookup operations are bound to **"Default4 (lookups)"**. `DefaultOptions.Production` (`DefaultOptions.ProductionOptions`, `TwilioSdk.Servers`) carries `BaseUrl: string` (default `https://api.twilio.com`), consumed as `new UrlTemplate(Production.BaseUrl, path, [])` — i.e. the value is used **verbatim as the prefix** and the operation path (`/2010-04-01/…`) is appended. `Default4Options.Production.BaseUrl` defaults to `https://lookups.twilio.com`.
- **The override**: setting `o.Server.Default.Production.BaseUrl = <Twilio:BaseUrl>` repoints **every "Default (api)" call — all five messaging operations (create/fetch/update/list/delete)** — at the override, while Lookup validation stays on the real `https://lookups.twilio.com`. The override is per server-group, not per-controller: any other api.twilio.com controller would follow it too (out of scope here). Pass the value without a trailing slash (the built-in default has none).
- **Manual construction** (if DI extension isn't used): `new TwilioSdkClient(httpClient, options)` with `httpClient: System.Net.Http.HttpClient`.

## 3. Trap notes

- ⚠ Step 1 (client registration) — the `HttpClient`/handler pipeline behind `TwilioSdkClient` has a required lifetime/ownership pattern; building it per request or disposing it with a transient client silently breaks sockets/retries. **MUST load `dotnet-client-initialization`** before writing `AddTwilioSdkClient`/`new TwilioSdkClient(...)`.
- ⚠ Step 1 (auth) — when in the construction sequence credentials must be set, and how secrets flow from configuration without leaking into logs, is not visible from the property type. **MUST load `dotnet-authentication`**.
- ⚠ Step 1 (resilience) — the SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; and whether a failed `CreateMessage` POST can be re-sent by the retry layer decides whether a shopper can be double-texted. **MUST load `dotnet-configuration-resilience`** before wiring the client.
- ⚠ Steps 3–7 (calling) — `CreateMessage` has 24 nullable no-default params and `ListMessage` has 8; a positional call mis-binds silently (e.g. your `from` string lands in `statusCallback`). Named arguments are mandatory, and the skill carries the call-shape rules. **MUST load `dotnet-calling-endpoints`** before the first call.
- ⚠ Steps 2–8 (models) — enums are `StringEnum<T>` records (compare/construct per the skill, not as C# enums), records are immutable with `init`-only setters, and unmodeled JSON fields are dropped on deserialize — a field you expected can read `null` without any error. **MUST load `dotnet-models`**.
- ⚠ Step 9 (error boundary) — every operation here is Case B (`SdkException<RawError>`): there are **no** `TryGet…` accessors and no typed payload, so status/body extraction follows the Case-B mechanics; getting the catch ladder wrong loses the HTTP status. **MUST load `dotnet-error-handling`** before writing any `try/catch`.
- ⚠ Step 7 (pagination) — there is no SDK paginator; the manual loop above must also respect what the skill says about list pagination (page-size limits, token semantics) before it goes to production volume. **MUST load `dotnet-configuration-resilience`**.
- ⚠ Step 9 (tests) — the test seam for stubbing the SDK is specific (the `HttpClient` constructor argument); faking the wrong seam produces tests that pass against nothing real. **MUST load `dotnet-testing`** before writing integration tests.

## 4. REQUIRED READING

Load **before implementation starts** — this sheet deliberately does not carry these skills' contents:

- `dotnet-client-initialization` — governs step 1 (client construction, `HttpClient` lifetime, DI registration).
- `dotnet-authentication` — governs step 1 (credentials wiring, secret handling).
- `dotnet-calling-endpoints` — governs steps 2–7 (named-argument discipline, async/cancellation usage).
- `dotnet-models` — governs steps 2–8 (`StringEnum<T>` handling, record construction, nullability).
- `dotnet-error-handling` — governs step 9 and every `try/catch` (Case A/B mechanics, the boundary).
- `dotnet-configuration-resilience` — governs steps 1 and 7 (retry/timeout semantics, pagination, base-URL tuning).
- `dotnet-testing` — governs step 9 (the fake seam, error-path coverage).

Two hazards that shape the error boundary from day one — `System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:
- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

## 5. Assumptions & Blockers

**Assumptions**
- Lookup **v2** (`FetchPhoneNumber3`) is the validation capability — v1's response model carries no `Valid` field (map evidence), so v1 cannot answer "is this a usable destination".
- Immediate sends use the account's sending number via `from`; scheduled sends use `messagingServiceSid` with `from: null` (map evidence: scheduling is documented as Messaging-Service-only). A Messaging Service must exist in the Twilio account; its SID is configuration (`Twilio:MessagingServiceSid`), not something this plan provisions.
- `accountSid` is passed per-call (every signature takes it) and comes from config — the same value used as the Basic-auth username.
- Redaction (body emptied, record kept) is the default disposal; `DeleteMessage` is exposed for completeness but destroys the outcome record.
- No webhooks: delivery outcome is learned only by polling `FetchMessage` (step 6) and reconciling via `ListMessage` (step 7).

**UNVERIFIED items (only live traffic / Twilio docs can confirm — defensive directives given inline above)**
- The `SendAt` scheduling window (min lead / max horizon) — not in map or source; validate app-side per Twilio docs, treat 400 as rejection.
- The exact initial `Status` of a created message and of a scheduled message (`queued` vs `accepted`/`scheduled`) — persist, never assume.
- Which current statuses accept a cancel — attempt only from `Scheduled`/`Accepted`/`Queued`, treat 4xx as terminal.
- Whether a malformed lookup input returns 200-with-`Valid:false` vs a non-2xx — handle both.
- The `PageToken` format inside `next_page_uri` — parse defensively, keep the page-increment fallback.

**Blockers**
- None for planning. One drift caveat: this sheet is grounded at the map-pinned source commit `51fdf48` ("Publish v2.0.0 SDK"); upstream `main` has since drifted (including the root namespace). `dotnet add package` installs the latest release — if any name on this sheet fails to compile against the installed package, trust the compiler and route the failing symbol back to the twilio-sdk agent for a re-grounded row; do not patch from memory.
