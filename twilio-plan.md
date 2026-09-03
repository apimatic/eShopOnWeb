# Twilio .NET SDK plan — eShopOnWeb SMS order notifications

Package: `AsadAli.TwilioSdk` (install version-less). Root namespace in this SDK: `TwilioSdk` (client `TwilioSdkClient`, options `TwilioSdkClientOptions`, DI `AddTwilioSdkClient`). The NuGet package 2.0.0 matches the map stamp, not a `Twilio` root.

---

## 1. Scope & sequence

1. **Client & config** — bind `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid`, optional `Twilio:BaseUrl`. Construct `TwilioSdkClient` / `AddTwilioSdkClient`. Apply `Twilio:BaseUrl` only to the 2010 Messages host (`Server.Default`). Do not apply it to Lookups.
2. **Shopper contact numbers** — `LookupsV2PhoneNumber.FetchPhoneNumber3` to decide usable destination and to store the provider canonical E.164 (`LookupResponse.PhoneNumber`). Reject at registration when not usable. GET/DELETE are application persistence; no further SDK call on delete.
3. **Immediate SMS** (order placed / dispatch / cancel) — `Api20100401Message.CreateMessage` with `from` = `Twilio:FromNumber`. Swallow send failure so the order operation still succeeds. Persist `Sid` + `Status`.
4. **Schedule follow-up with the provider** (dispatch) — same `CreateMessage` with `scheduleType` = `MessageEnumScheduleType.Fixed`, `sendAt` a few days later, `messagingServiceSid` = `Twilio:MessagingServiceSid` (enum Notes: Messaging Services only). Persist the scheduled message `Sid`. This is queued at Twilio, not a local timer.
5. **Cancel scheduled follow-up** (order cancel) — `Api20100401Message.UpdateMessage` with `status` = `MessageEnumUpdateStatus.Canceled`. If the provider rejects because it already sent, treat as Case B and do not fail the cancel.
6. **Refresh delivery outcome** — `Api20100401Message.FetchMessage` by persisted `Sid` for notification status / my-orders / notification detail.
7. **Resend** — another `CreateMessage` to the same destination. Caller idempotency key: see Blocker in §5 (the public signature does not accept one).
8. **Dispose message content at the provider** — `Api20100401Message.UpdateMessage` with `body` set (null is omitted and will not update). Do **not** call `DeleteMessage` (that deletes the resource). Persist Sid/status locally; they survive on the message resource.
9. **Reconciliation** — `Api20100401Message.ListMessage` with `from` = `Twilio:FromNumber`, `dateSentQueryQuery` = range start (`DateSent>`), `dateSentQuery` = range end (`DateSent<`). Walk every page until `NextPageUri` is absent. Do not list the account then filter client-side.

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

Map stamp vs compile surface (source: `TwilioSdkClient.cs`, `TwilioSdkClientOptions.cs`, `ServiceCollectionExtensions.cs`): root `TwilioSdk`, client `TwilioSdk.TwilioSdkClient`, options `TwilioSdk.TwilioSdkClientOptions`, DI `TwilioSdk.ServiceCollectionExtensions.AddTwilioSdkClient`. Controllers live on `TwilioSdkClient` as `Api20100401Message` and `LookupsV2PhoneNumber` (`TwilioSdk.Api`).

### Client construction, auth, servers

| Fact | Contract | Source |
|---|---|---|
| Constructor | `TwilioSdk.TwilioSdkClient(System.Net.Http.HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)` | `TwilioSdkClient.cs` |
| DI | `TwilioSdk.ServiceCollectionExtensions.AddTwilioSdkClient(Action<TwilioSdkClientOptions>? configure = null)` — registers `TwilioSdkClient`; obtains `HttpClient` from `IHttpClientFactory.CreateClient()` | `ServiceCollectionExtensions.cs` |
| Options members | `Environment`: `TwilioSdk.Servers.ServerEnvironment` (only `Production`); `Retry`: `TwilioSdk.Core.Configuration.RetryOptions`; `Logging`: `TwilioSdk.Core.Configuration.LoggingOptions`; `Server`: `TwilioSdk.ServerOptions`; `Hooks`: `IReadOnlyList<TwilioSdk.Core.Hooks.SdkHook>`; `AccountSidAuthToken`: `TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?` | `TwilioSdkClientOptions.cs`; sdk-map *Getting a client* / *Servers & auth* |
| Credentials | `new BasicAuthCredentials { Username = <Twilio:AccountSid>, Password = <Twilio:AuthToken> }` — both members `required`. Applied as HTTP Basic. XML: account SID + auth token, or API key as username and API key secret as password. | `BasicAuthCredentials.cs`; sdk-map *Servers & auth* |
| Messages host (Default) | Create/List/Fetch/Update/Delete Message all call `_server.Default(...)` → `options.Server.Default.Production.BaseUrl` default `"https://api.twilio.com"` | `Api/Api20100401Message.cs`; `Servers/DefaultOptions.cs`; `Server.cs` |
| `Twilio:BaseUrl` | When set, assign **verbatim** to `options.Server.Default.Production.BaseUrl`. That is the only host this integration uses for send/read/reconcile. Do **not** set `Server.Default4` (lookups) or `Server.Default1` (`https://messaging.twilio.com`, unused here). | `Servers/DefaultOptions.cs`; `Servers/Default4Options.cs`; `Servers/Default1Options.cs` |
| Lookups host (Default4) | `FetchPhoneNumber3` calls `_server.Default4(...)` → default `"https://lookups.twilio.com"`. `Twilio:BaseUrl` must not change this. | `Api/LookupsV2PhoneNumber.cs`; `Servers/Default4Options.cs` |
| Per-call options | `TwilioSdk.Core.RequestOptions`: `LogLevel` (`Microsoft.Extensions.Logging.LogLevel?`), `Hooks` (`IReadOnlyList<TwilioSdk.Core.Hooks.SdkHook>?`). **No header bag, no idempotency member.** | `Core/RequestOptions.cs` |
| Environment | `options.Environment` = `TwilioSdk.Servers.ServerEnvironment.Production` | `Servers/ServerEnvironment.cs` |

`accountSid` on every Message operation is the path `{AccountSid}` — bind from **`Twilio:AccountSid`**.

### Operations

| Controller · method | Signature (params in order) | Request fields used / left out | Response envelope (fields this integration reads) | Error | Pagination | Source |
|---|---|---|---|---|---|---|
| `client.Api20100401Message` · `CreateMessage` | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, TwilioSdk.Models.Enums.MessageEnumContentRetention? contentRetention, TwilioSdk.Models.Enums.MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, TwilioSdk.Models.Enums.MessageEnumTrafficType? trafficType, bool? shortenUrls, TwilioSdk.Models.Enums.MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, TwilioSdk.Models.Enums.MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` — 24 nullable params (`statusCallback`…`contentSid`) have **no C# default → must pass explicitly** (`null` to skip). HTTP `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` on Default (api). Form body (not JSON). | **Immediate:** `to` (wire `To`, required), `from` (wire `From`) = `Twilio:FromNumber`, `body` (wire `Body`). **Scheduled:** additionally `scheduleType` (wire `ScheduleType`) = `MessageEnumScheduleType.Fixed`, `sendAt` (wire `SendAt`) = future `DateTimeOffset`, `messagingServiceSid` (wire `MessagingServiceSid`) = `Twilio:MessagingServiceSid`. Notes on `MessageEnumScheduleType`: *For Messaging Services only* + value `fixed` together with the schedule time (SDK param is `sendAt`, not `send_time`). **Left out** (optional; Notes do not tie them to acceptance): `statusCallback`, `applicationSid`, `maxPrice`, `provideFeedback`, `attempt`, `validityPeriod`, `forceDelivery`, `contentRetention`, `addressRetention`, `smartEncoded`, `persistentAction`, `trafficType`, `shortenUrls`, `sendAsMms`, `contentVariables`, `riskCheck`, `fallbackFrom`, `mediaUrl`, `contentSid`. Immediate path also leaves out `scheduleType`, `sendAt`, `messagingServiceSid`. Whether `from` and `messagingServiceSid` may be set together is not in the Notes. | **Direct** `TwilioSdk.Models.ApiV2010AccountMessage` (no wrapper). Read: `Sid (sid): string?` (provider id, `^(SM\|MM)[0-9a-fA-F]{32}$`), `Status (status): MessageEnumStatus?`, `To (to): string?`, `From (from): string?`, `DateSent (date_sent): string?`, `DateCreated (date_created): string?`, `ErrorCode (error_code): int?`, `ErrorMessage (error_message): string?`, `MessagingServiceSid (messaging_service_sid): string?`, `Body (body): string?`. Scheduled create: expect `Status` = `Scheduled`. Immediate create: typically a non-terminal status (`Queued` / `Accepted` / `Sending` / `Sent`) — delivery is later via Fetch. | **Case B** `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`. Accessors: `StatusCode: HttpStatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()`. No-throw variant: absent. | none | `operations/Api20100401Message.md`; `records-1-Ac-Ca.md`; `Api/Api20100401Message.cs`; `Models/ApiV2010AccountMessage.cs` |
| `client.Api20100401Message` · `FetchMessage` | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` HTTP `GET .../Messages/{Sid}.json` | Path: `accountSid`, `sid` = persisted provider SID. | Same `ApiV2010AccountMessage`. Refresh `Status`, `ErrorCode`, `ErrorMessage`, `DateSent`. | Case B `SdkException<RawError>` (same accessors). | none | `operations/Api20100401Message.md`; `records-1-Ac-Ca.md` |
| `client.Api20100401Message` · `UpdateMessage` | `UpdateMessage(string accountSid, string sid, string? body, TwilioSdk.Models.Enums.MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `body` and `status` nullable, no default → **must pass explicitly**. HTTP `POST .../Messages/{Sid}.json`. Notes: *Update a Message resource (used to redact Message `body` text and to cancel not-yet-sent messages)*. | **Cancel scheduled:** `status` (wire `Status`) = `MessageEnumUpdateStatus.Canceled`, `body: null` (omitted). **Redact content:** `body` (wire `Body`) non-null (null is dropped by the form encoder and will not update); `status: null`. Exact redaction token is not named in Notes — empty string is the only empty payload the form encoder will send (`Body=`). Whether the live provider treats `Body=` as redaction is **UNVERIFIED**. **Left out:** nothing else on this signature. | Same `ApiV2010AccountMessage`. After cancel: `Status` = `Canceled`. After redact: metadata (`Sid`, `Status`, `ErrorCode`, …) remains on the resource; `Body` is whatever the provider returns. | Case B `SdkException<RawError>`. Already-sent cancel is a provider error on this path (status via `RawError.StatusCode` / `ReadAsString()`). | none | `operations/Api20100401Message.md`; `Api/Api20100401Message.cs` |
| `client.Api20100401Message` · `ListMessage` | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 8 nullable params must be passed explicitly. HTTP `GET .../Messages.json`. | **Reconciliation:** `from` (wire `From`) = `Twilio:FromNumber` (provider-side filter by sending number). `dateSentQueryQuery` (wire `DateSent>`) = ISO-8601 range start. `dateSentQuery` (wire `DateSent<`) = ISO-8601 range end. `to: null`, `dateSent: null` (exact `DateSent` is not a range). `pageSize` (wire `PageSize`): XML default 50, max 1000. `page` / `pageToken` for subsequent pages. XML on the three date params describes GMT `YYYY-MM-DD` / `<=` / `>=`; the SDK serializes each `DateTimeOffset` as `yyyy-MM-ddTHH:mm:ss.fffZ` (UTC). Whether the provider honors the time component is **UNVERIFIED**. | **Envelope** `TwilioSdk.Models.ListMessageResponse`: `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?`, `NextPageUri (next_page_uri): string?`, `PageToken` is not a response field — XML: *page token is provided by the API*; `Page (page)`, `PageSize (page_size)`, `End`, `Start`, `Uri`, `FirstPageUri`, `PreviousPageUri`. | Case B `SdkException<RawError>`. | Map: **none** (only `page`, no `perPage`). Walk pages yourself via `pageToken` until `NextPageUri` is absent. | `operations/Api20100401Message.md`; `records-4-Li-Me.md`; `Api/Api20100401Message.cs`; `Models/ListMessageResponse.cs` |
| `client.Api20100401Message` · `DeleteMessage` | `DeleteMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` HTTP `DELETE .../Messages/{Sid}.json`. Notes: *Deletes a Message resource from your account*. | **Do not use for Flow 3 content dispose.** That removes the resource; the product requires metadata/status to survive. | `void` (`Task`) | Case B `SdkException<RawError>`. | none | `operations/Api20100401Message.md` |
| `client.LookupsV2PhoneNumber` · `FetchPhoneNumber3` | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 15 nullable params must be passed explicitly. HTTP `GET /v2/PhoneNumbers/{PhoneNumber}` on **Default4 (lookups)** — not the Messages host. | **Required:** `phoneNumber` (path; E.164 or national; default country +1). **Used:** `fields` (wire `Fields`) comma-separated; XML possible values include `validation`, `line_type_intelligence`, `line_status`, … Pass `"line_type_intelligence"` (and `"line_status"` only if you also persist line status). `countryCode` (wire `CountryCode`) when the input is national format. **Left out** (identity_match / reassigned_number / pre_fill / sms_pumping_risk packages; Notes do not require them for a validity lookup): `firstName`, `lastName`, `addressLine1`, `addressLine2`, `city`, `state`, `postalCode`, `addressCountryCode`, `nationalId`, `dateOfBirth`, `lastVerifiedDate`, `verificationSid`, `partnerSubId`. `fields` is `string?`, not `Field`. | **Direct** `TwilioSdk.Models.LookupResponse`. Canonical form: `PhoneNumber (phone_number): string?` (E.164). Usable-range flag: `Valid (valid): bool?` — XML: *Boolean which indicates if the phone number is in a valid range that can be freely assigned by a carrier to a user.* Invalid reasons: `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?`. Also `CallingCountryCode`, `CountryCode`, `NationalFormat`. Line type: `LineTypeIntelligence (line_type_intelligence): LineTypeIntelligenceInfo?` with `Type (type): string?` (not an enum on this model), `ErrorCode (error_code): int?`. Line status: `LineStatus (line_status): LineStatusInfo?` with `Status (status): string?`. **`Valid == false` is a 2xx body, not an exception.** | Case B `SdkException<RawError>` (HTTP failures: malformed request, 401, etc.). | none | `operations/LookupsV2PhoneNumber.md`; `records-4-Li-Me.md`; `records-3-Fl-Li.md`; `Api/LookupsV2PhoneNumber.cs`; `Models/LookupResponse.cs` |
| `client.LookupsV1PhoneNumberApi` · `FetchPhoneNumber2` | Not used. V1 returns `LookupsV1PhoneNumber` with no `Valid` / `ValidationErrors`; `Carrier` is `object?`. V2 is the lookup that exposes usable-range + canonical E.164. | — | — | — | — | `operations/LookupsV1PhoneNumberApi.md`; `records-4-Li-Me.md` |

### Idempotency (Create / Update / Delete Message)

`CreateMessage`, `UpdateMessage`, and `DeleteMessage` each attach a header `Idempotency-Key` whose value is `Guid.NewGuid()` inside the generated method. There is no parameter for a caller key. `RequestOptions` cannot carry one. Repeating an application key therefore cannot be forwarded to the provider through the public signature — each SDK call sends a distinct token. **Blocker: §5.** `FetchMessage` / `ListMessage` / `FetchPhoneNumber3` send no such header.

Source: `Api/Api20100401Message.cs` (header lists on Create/Update/Delete); `Core/RequestOptions.cs`.

### Usable destination (registration)

| Meaning | How the SDK expresses it | Source |
|---|---|---|
| Invalid number | `LookupResponse.Valid == false` and/or `ValidationErrors` non-empty on a **2xx**. Also Case B if the HTTP call itself fails. | `Models/LookupResponse.cs`; `enums.md` `ValidationError` |
| Canonical form to persist | `LookupResponse.PhoneNumber` (E.164: `+` + country code + subscriber) | `Models/LookupResponse.cs` |
| Mobile vs landline | Not a typed field. Optional `fields=line_type_intelligence` fills `LineTypeIntelligenceInfo.Type` as `string?`. `TwilioSdk.Models.Enums.LineType` lists `Mobile (mobile)`, `Landline (landline)`, `TollFree (tollFree)`, `FixedVoip (fixedVoip)`, `NonFixedVoip (nonFixedVoip)`, `Personal (personal)`, `Premium (premium)`, `Voicemail (voicemail)`, `SharedCost (sharedCost)`, `Uan (uan)`, `Pager (pager)`, `Unknown (unknown)` but that enum’s Notes are *The new line type to override the original line type* — it is **not** the type of `LineTypeIntelligenceInfo.Type`. Live `Type` strings are **UNVERIFIED**. | `Models/LineTypeIntelligenceInfo.cs`; `enums.md` `LineType` |
| SMS will actually deliver | **Not** what `Valid` means. CreateMessage can 2xx and later Fetch `Status` = `Undelivered` / `Failed` (sandbox: reserved unassigned US number). Handle as a delivery outcome, not a lookup gap. | `Models/LookupResponse.cs`; `enums.md` `MessageEnumStatus`; product sandbox fact |

Reject registration when `Valid` is not `true`. Persist `PhoneNumber`, never the caller-typed string. Whether to also reject non-mobile `Type` is **YOUR CALL — not in the map** (Valid is assignable-range, not SMS-capability).

### Enums in scope (`TwilioSdk.Models.Enums`, `StringEnum<T>` — use members or `FromValue("wire")`, not C# enums)

| Type | Members (C# · wire) | Source |
|---|---|---|
| `MessageEnumStatus` | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` | `enums.md`; `Models/Enums/MessageEnumStatus.cs` |
| `MessageEnumScheduleType` | `Fixed (fixed)` — Notes: Messaging Services only, with the schedule time | `enums.md`; `Models/Enums/MessageEnumScheduleType.cs` |
| `MessageEnumUpdateStatus` | `Canceled (canceled)` | `enums.md`; `Models/Enums/MessageEnumUpdateStatus.cs` |
| `MessageEnumDirection` | `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)` | `enums.md` |
| `ValidationError` | `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)` | `enums.md` |
| `Field` | `CallerName (caller_name)`, `SimSwap (sim_swap)`, `CallForwarding (call_forwarding)`, `LineTypeIntelligence (line_type_intelligence)`, `LineStatus (line_status)`, `IdentityMatch (identity_match)`, `ReassignedNumber (reassigned_number)`, `SmsPumpingRisk (sms_pumping_risk)` — lookup `fields` is still a `string?` | `enums.md` |
| `MessageEnumContentRetention` | `Retain (retain)`, `Discard (discard)` — create-time only; not the later redact operation | `enums.md` |
| `MessageEnumAddressRetention` | `Retain (retain)`, `Obfuscate (obfuscate)` | `enums.md` |

Terminal delivery outcomes to persist/report: `Delivered`, `Undelivered`, `Failed`, `Canceled`. In-flight: `Accepted`, `Queued`, `Sending`, `Sent`, `Scheduled`. `Read` is WhatsApp-only per enum Notes.

### Error types that reach catch blocks

All seven in-scope operations are **Case B**. Catch `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`. Read `ex.Error.StatusCode` and `ex.Error.ReadAsString()` (or `ReadAsJson<T>()` / `ReadAsBytes()`). There is no typed `{Operation}Error` and no `TryGet…` besides those RawError members. No `…Result` overload.

| Failure class | How it surfaces | Source |
|---|---|---|
| Number not usable (format/range) | Usually **2xx** `Valid == false` / `ValidationErrors` — not `SdkException` | `LookupResponse` |
| Lookup/send HTTP rejection (401, 400, 404, …) | `SdkException<RawError>` | every op row |
| Message accepted, delivery later fails | Create/Fetch **2xx** with `Status` `Undelivered` or `Failed` and `ErrorCode`/`ErrorMessage` (XML: do not use those two fields programmatically; they may change) | `ApiV2010AccountMessage` |
| Cancel after send | `UpdateMessage` Case B | `Api20100401Message.md` |
| Transport / timeout | Not `SdkException` — `HttpRequestException` / `TaskCanceledException` (timeout path wraps `TimeoutRejectedException`) | `Core/RawClient.cs` |

`TwilioSdk.Core.Exceptions.SdkException<TError>` exposes `required TError Error { get; init; }` (`Core/Exceptions/SdkException.cs`).

---

## 3. Trap notes

⚠ Step 1 (client / DI) — `TwilioSdkClient(HttpClient, TwilioSdkClientOptions)` and `AddTwilioSdkClient` do not tell you who owns `HttpClient` lifetime or which service lifetime is safe. **MUST load `dotnet-client-initialization`** before registering the client.

⚠ Step 1 (auth) — `AccountSidAuthToken` is a `BasicAuthCredentials` username/password pair; the options object will not load `Twilio:AccountSid` / `Twilio:AuthToken` for you. **MUST load `dotnet-authentication`** before setting credentials.

⚠ Step 1 (BaseUrl, retries, logging) — `RetryOptions` / `Timeout` / `HttpMethodsToRetry` do not bound a whole call and are not the timeout on the `HttpClient` you register; whether a failed write can be re-sent, and whether SDK logging will print `Authorization` / `To` / `From` / `Body`, are not settled by the option names. Shopper numbers and the auth token must never appear in logs. **MUST load `dotnet-configuration-resilience`** before wiring the client or enabling SDK logging.

⚠ Step 2–9 (every call) — CreateMessage has 24 must-pass-explicitly nullables; ListMessage 8; FetchPhoneNumber3 15. Positional calls mis-bind. **MUST load `dotnet-calling-endpoints`** before the first `client.{Group}.{Operation}(...)`.

⚠ Step 2–9 (models / enums) — `MessageEnum*` / `ValidationError` / `Field` are `StringEnum<T>`, not C# enums; response records are `init`-only with wire names in parentheses; unmodeled JSON is dropped. **MUST load `dotnet-models`** before reading `Status` / `Valid` or constructing schedule/cancel values.

⚠ Step 3 / 5 / 7 / 9 (error boundary) — every in-scope operation throws `SdkException<RawError>` (Case B). A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 9 (reconciliation list) — `ListMessage` is not SDK-paginated (`Pagination: none`); covering the whole `from`/`to` range means walking `page` / `pageToken` / `NextPageUri` yourself. **MUST load `dotnet-configuration-resilience`** before writing that loop.

⚠ Tests — the `HttpClient` constructor argument is the test seam. **MUST load `dotnet-testing`** before stubbing the SDK.

---

## 4. REQUIRED READING

Load these **before implementation starts**. This sheet deliberately does not carry their contents.

- `dotnet-client-initialization` — Step 1 client construction, `HttpClient` ownership, `AddTwilioSdkClient`
- `dotnet-authentication` — Step 1 `AccountSidAuthToken` / `BasicAuthCredentials`
- `dotnet-calling-endpoints` — Steps 2–9 named-argument calls, must-pass-explicitly nullables
- `dotnet-models` — Steps 2–9 records, `StringEnum<T>`, wire names
- `dotnet-error-handling` — error boundary for every operation; both `JsonException` directions in §3
- `dotnet-configuration-resilience` — Step 1 retries/timeouts/BaseUrl/logging; Step 9 pagination
- `dotnet-testing` — tests against the `HttpClient` seam

---

## 5. Assumptions & Blockers

### Assumptions

- Bind configuration from the `Twilio:` section keys named in the product brief (`Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid`, optional `Twilio:BaseUrl`). Do not invent other keys.
- `accountSid` path argument = `Twilio:AccountSid`.
- Immediate SMS uses `from` = `Twilio:FromNumber`. Scheduled SMS uses `messagingServiceSid` = `Twilio:MessagingServiceSid` because `MessageEnumScheduleType` Notes require a Messaging Service.
- “A few days later” for `sendAt` is **YOUR CALL — not in the map**.
- Whether registration also rejects non-mobile `LineTypeIntelligenceInfo.Type` is **YOUR CALL — not in the map**. This sheet treats `Valid != true` as the provider’s unusable-destination signal.
- How many contact numbers a shopper may register, JWT identity, and persistence schema are **YOUR CALL — not in the map**.
- A send/schedule/cancel/redact/resend failure must not fail the order operation — **YOUR CALL — not in the map** (application rule). Persist Sid + Status when the provider accepted the call.
- Register/message only `TWILIO_TEST_TO_NUMBER` and `TWILIO_UNREACHABLE_TO_NUMBER` in live tests — **YOUR CALL — not in the map** (test data).
- Empty-string `body` on `UpdateMessage` is the only empty Body the form encoder will send; live redaction semantics are **UNVERIFIED**.
- `ListMessage` date filters are serialized as UTC ISO-8601; XML documents `YYYY-MM-DD`. Whether time-of-day is honored is **UNVERIFIED**.

### Blockers

- **Caller-supplied idempotency key is not on the public SDK surface.** `CreateMessage` (and `UpdateMessage` / `DeleteMessage`) always send `Idempotency-Key: Guid.NewGuid()`. `RequestOptions` has no idempotency or header member. Flow 3 *same key must not send a second message* cannot be implemented by passing the caller key into the SDK. Application-layer “do not call CreateMessage again for this key” is **YOUR CALL — not in the map** and is not provider-side idempotency.
- No webhook/status-callback URL is in product scope; this plan uses Fetch/List only. `CreateMessage.statusCallback` is left null.

---

## 6. Logging (shopper numbers / auth)

Never log `Twilio:AuthToken` / `BasicAuthCredentials.Password`, `Authorization`, shopper `to` / `PhoneNumber` / `from` of the shopper, or message `Body`. SDK `LoggingOptions` is the lever that can print them; the companion skill governs how. Persist canonical numbers in application storage as required; do not write them to logs.
