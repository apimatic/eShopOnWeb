# Twilio .NET SDK plan — eShopOnWeb SMS order notifications (PublicApi)

Package: `AsadAli.TwilioSdk` (install version-less). Root namespace: `TwilioSdk`. Client: `TwilioSdkClient`. Host: PublicApi. No webhooks — every delivery outcome, cancel confirmation, and reconciliation row is obtained by calling Twilio.

## Scope & sequence

| Step | Feature | Operations |
|---|---|---|
| 1 | Client init, auth, messaging-only base URL | `new TwilioSdkClient(httpClient, options)` / `AddTwilioSdkClient` — credentials + `Server.Default` only |
| 2 | Shopper contact registration — validate + canonicalize | `client.LookupsV2PhoneNumber.FetchPhoneNumber3` |
| 3 | Send SMS immediately (placed / dispatched / cancelled / operator resend) | `client.Api20100401Message.CreateMessage` (`from` + `to` + `body`; `scheduleType`/`sendAt` null) |
| 4 | Schedule follow-up SMS with the provider | `client.Api20100401Message.CreateMessage` (`scheduleType`, `sendAt`, `messagingServiceSid`) |
| 5 | Cancel a not-yet-sent scheduled message | `client.Api20100401Message.UpdateMessage` (`status: Canceled`) |
| 6 | Fetch current delivery outcome by SID | `client.Api20100401Message.FetchMessage` |
| 7 | Operator resend with caller-supplied idempotency key | See CONTRACT SHEET + **GAP** in Assumptions & Blockers — `CreateMessage` does **not** accept a caller key |
| 8 | Dispose message content at the provider (keep the fact of send) | `client.Api20100401Message.UpdateMessage` (`body: ""`) — **not** `DeleteMessage` |
| 9 | Reconciliation: list this app’s From-number traffic over a date range | `client.Api20100401Message.ListMessage` (`from` = `Twilio:FromNumber`, date filters, manual paging) |
| 10 | Error boundary around every SDK call | Case B `SdkException<RawError>` on all in-scope ops; also `JsonException` (see trap notes) |

Do **not** use `DeleteMessage` for feature 8 — it deletes the Message resource. Do **not** apply `Twilio:BaseUrl` to Lookup (`Default4`).

---

## CONTRACT SHEET

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

No-throw `…Result` variants: **absent** on every operation below. All are throw-only.

### Client construction / auth / server-node (`sdk-map.md`, `TwilioSdkClientOptions.cs`, `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Servers/Default4Options.cs`, `Core/Authentication/Basic/BasicAuthCredentials.cs`)

| Item | Fact |
|---|---|
| Client | `TwilioSdk.TwilioSdkClient` — ctor `TwilioSdkClient(System.Net.Http.HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)` |
| DI | `TwilioSdk.ServiceCollectionExtensions.AddTwilioSdkClient(Action<TwilioSdkClientOptions>? configure = null)` |
| Options | `TwilioSdk.TwilioSdkClientOptions`: `Environment` (`TwilioSdk.Servers.ServerEnvironment`, default `ServerEnvironment.Production` / `Default()`), `Retry` (`TwilioSdk.Core.Configuration.RetryOptions`), `Logging` (`TwilioSdk.Core.Configuration.LoggingOptions`), `Server` (`TwilioSdk.ServerOptions` — **root** namespace, not `.Servers`), `AccountSidAuthToken` (`TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?`) |
| Auth | `options.AccountSidAuthToken = new BasicAuthCredentials { Username = accountSid, Password = authToken }` (`required` init). Wire: HTTP Basic. Config keys **names only**: `Twilio:AccountSid`, `Twilio:AuthToken`. Never log these. |
| Messaging base URL | Create/fetch/list/update/delete messages (incl. schedule + cancel) all use server node **Default (api)** → `options.Server.Default.Production.BaseUrl` (`TwilioSdk.Servers.DefaultOptions.ProductionOptions.BaseUrl`, `string`, default `"https://api.twilio.com"`). When `Twilio:BaseUrl` is set, assign that value **verbatim** to this property only. |
| Lookup host (must not follow Twilio:BaseUrl) | `FetchPhoneNumber3` uses **Default4 (lookups)** → `options.Server.Default4.Production.BaseUrl` (default `"https://lookups.twilio.com"`). Leave it at default. |
| Environment | `ServerEnvironment.Production` only (`wire: production`). |
| From vs Messaging Service | Immediate send: pass `from` = `Twilio:FromNumber`; `messagingServiceSid` is optional. Scheduled send: `MessageEnumScheduleType` docs say scheduling is **for Messaging Services only** — pass `messagingServiceSid` = `Twilio:MessagingServiceSid` plus `scheduleType` + `sendAt`. Reconciliation: server-side filter `from` = `Twilio:FromNumber` (not the Messaging Service SID). Config keys **names only**: `Twilio:FromNumber`, `Twilio:MessagingServiceSid`, `Twilio:BaseUrl`. |
| Per-call options | `TwilioSdk.Core.RequestOptions` — **only** `LogLevel? LogLevel { get; init; }`. No headers, no idempotency slot. |
| Retry/timeout types | `TwilioSdk.Core.Configuration.RetryOptions` — all members `required`; start from `RetryOptions.Default()` or `Disabled()`. |

### Operations

| Controller | Method (params in order) | Request fields | Response envelope | Error | Pagination |
|---|---|---|---|---|---|
| `client.LookupsV2PhoneNumber` (`TwilioSdk.Api.LookupsV2PhoneNumber`) · `map/operations/LookupsV2PhoneNumber.md` · `Api/LookupsV2PhoneNumber.cs` | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 15 params `fields`…`partnerSubId` nullable, **no C# default → pass `null` to skip** | Path: `phoneNumber` (E.164 or national; XML: default country +1). Query: `Fields` ← `fields` (comma-separated; XML possible values include `validation`, `line_type_intelligence`, `line_status`, …). `CountryCode` ← `countryCode` (ISO 3166-1 alpha-2 when national format). Other name/address params are identity-match/pre-fill packages — pass `null` for this app. | **No wrapper.** Return is `TwilioSdk.Models.LookupResponse` (`map/models/records-4-Li-Me.md`, `Models/LookupResponse.cs`): `PhoneNumber (phone_number): string?` — **E.164 canonical** (`+` + country + subscriber); `Valid (valid): bool?` — in a valid carrier-assignable range; `ValidationErrors (validation_errors): IReadOnlyList<TwilioSdk.Models.Enums.ValidationError>?`; `NationalFormat (national_format): string?`; `CountryCode (country_code): string?`; `CallingCountryCode (calling_country_code): string?`; `LineTypeIntelligence (line_type_intelligence): TwilioSdk.Models.LineTypeIntelligenceInfo?` — `Type (type): string?` (**not** a generated enum), `ErrorCode (error_code): int?`, `CarrierName (carrier_name): string?`; `LineStatus (line_status): TwilioSdk.Models.LineStatusInfo?` — `Status (status): string?`, `ErrorCode (error_code): int?`. Store `PhoneNumber` (canonical), not the caller-typed string. | **Case B** `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`. Accessors: `StatusCode: HttpStatusCode` · `ReadAsString()` · `ReadAsBytes()` · `ReadAsJson<T>()`. 2xx with `Valid == false` / non-empty `ValidationErrors` is **not** an exception — reject as not a usable destination. Auth 401/403 vs not-usable 4xx: branch on `ex.Error.StatusCode` (401/403 = transport/auth; other 4xx = provider rejection). 5xx / `HttpRequestException` = transport. | none |
| `client.Api20100401Message` (`TwilioSdk.Api.Api20100401Message`) · `map/operations/Api20100401Message.md` · `Api/Api20100401Message.cs` | **CreateMessage** (send now **or** schedule): `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 24 params `statusCallback`…`contentSid` nullable, **no C# default → pass `null` to skip**. HTTP `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` on **Default (api)**. Form body (not JSON). | Path: `AccountSid` ← `accountSid`. Form: `To` ← `to` (required); `From` ← `from`; `MessagingServiceSid` ← `messagingServiceSid`; `Body` ← `body`; `ScheduleType` ← `scheduleType`; `SendAt` ← `sendAt` (`DateTimeOffset?`, **not** a string — SDK form-encodes the value); remaining listed form names as in the map (`StatusCallback`, `FallbackFrom`, `MediaUrl`, …). **Immediate send:** `from` = `Twilio:FromNumber`, `to` = canonical E.164, `body` = text, `scheduleType: null`, `sendAt: null`, `messagingServiceSid: null` (or SID if you also use an MS). **Scheduled send:** `scheduleType: MessageEnumScheduleType.Fixed` (only generated value; wire `fixed`), `sendAt` = provider send instant, `messagingServiceSid` = `Twilio:MessagingServiceSid` (enum XML: Messaging Services only), `to`, `body`. `statusCallback`: **null** (no public URL). | **No wrapper.** Return is `TwilioSdk.Models.ApiV2010AccountMessage` (`map/models/records-1-Ac-Ca.md`): persist `Sid (sid): string?` (pattern `^(SM\|MM)[0-9a-fA-F]{32}$`), `Status (status): MessageEnumStatus?`, `ErrorCode (error_code): int?`, `ErrorMessage (error_message): string?`, `DateSent (date_sent): string?` (RFC 2822 GMT string, not `DateTimeOffset`), `DateCreated (date_created): string?`, `MessagingServiceSid (messaging_service_sid): string?`. Present but **do not log**: `To (to)`, `From (from)`, `Body (body)`. Immediate create is accepted even when later US carrier/registration makes it undeliverable — that shows up as `Status` `failed` / `undelivered` on a later fetch, **not** as a sheet gap. | **Case B** `SdkException<RawError>` — same accessors. Catch this around checkout/dispatch so a send failure **does not** abort the order. | none |
| same | **FetchMessage**: `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` · `GET …/Messages/{Sid}.json` · Default (api) | Path: `AccountSid`, `Sid` | Same `ApiV2010AccountMessage` (no envelope). Read `Status` for queued/sending/sent/delivered/undelivered/failed/canceled/scheduled/…. Still-scheduled = `Status == MessageEnumStatus.Scheduled`. Already sent = any other outbound status (`Queued`/`Sending`/`Sent`/`Delivered`/`Undelivered`/`Failed`/…). Canceled = `Canceled`. | **Case B** `SdkException<RawError>` (404 = unknown SID) | none |
| same | **UpdateMessage** (cancel **or** redact): `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `body` and `status` nullable, **no C# default → pass explicitly**. HTTP `POST …/Messages/{Sid}.json`. XML: “redact Message `body` text and cancel not-yet-sent messages”. Null params are **omitted** from the form (source `ParameterFlattener`: null → no field). | Form: `Body` ← `body`; `Status` ← `status`. **Cancel:** `status: MessageEnumUpdateStatus.Canceled` (only generated member; wire `canceled`), `body: null`. **Redact content (feature 8):** `body: ""` (empty string, **not** null — null would omit Body), `status: null`. Do **not** call `DeleteMessage`. | Same `ApiV2010AccountMessage`. After redact, the resource still exists (SID + status/outcome survive). Exact post-redact `Body` string is **UNVERIFIED** — persist whatever Fetch/Update returns; do not assume a literal. If cancel is rejected because it already went out: Case B (status codes **UNVERIFIED**) — then `FetchMessage` for current `Status`. | **Case B** `SdkException<RawError>` | none |
| same | **ListMessage**: `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 8 params `to`…`pageToken` nullable, **no C# default → pass `null` to skip**. `GET …/Messages.json` · Default (api) | Query (wire ← C#): `To` ← `to`; **`From` ← `from`** — pass `Twilio:FromNumber` (server-side sender filter; do not list the whole account); `DateSent` ← `dateSent` (exact); `DateSent<` ← `dateSentQuery` (**before**); `DateSent>` ← `dateSentQueryQuery` (**after**); `PageSize` ← `pageSize` (`long?`; XML default 50, max 1000); `Page` ← `page`; `PageToken` ← `pageToken`. **Types:** filters are `DateTimeOffset?`, **not** caller-supplied ISO-8601 strings. The SDK serializes them with `ToIso8601()` → UTC `yyyy-MM-ddTHH:mm:ss.fffZ`. Range `from={from}&to={to}`: `dateSentQueryQuery` = range start (DateSent>), `dateSentQuery` = range end (DateSent<), `dateSent: null`, `to: null`. | Envelope `TwilioSdk.Models.ListMessageResponse` (`map/models/records-4-Li-Me.md`): `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?` plus `NextPageUri (next_page_uri): string?`, `PreviousPageUri`, `FirstPageUri`, `Page`, `PageSize`, `Start`, `End`, `Uri`. **No `PageToken` on the response.** | **Case B** `SdkException<RawError>` | **No auto-paginator.** Map: “pagination: none (only `page`, no `perPage`)”. Cover the range by repeating `ListMessage` with the same `from`/date filters while `NextPageUri` is non-null. XML: `pageToken` “is provided by the API”; `page` is “simply for client state”. |
| same | **DeleteMessage** (out of feature-8 scope): `DeleteMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` · `DELETE …/Messages/{Sid}.json` | Path only | `void` (`Task`) — resource gone | **Case B** | none |

**Lookups V1** (`client.LookupsV1PhoneNumberApi.FetchPhoneNumber2` → `LookupsV1PhoneNumber`) has `PhoneNumber` but **no** `Valid` / `ValidationErrors` / `LineTypeIntelligence`. Do not use V1 for shopper registration.

### CreateMessage Idempotency-Key (feature 7) — GAP

`CreateMessage` **does** send HTTP header `Idempotency-Key`, but the SDK hard-codes `new HeaderParam("Idempotency-Key", Guid.NewGuid())` inside the method (`Api/Api20100401Message.cs`). There is **no** method parameter for a caller key. `RequestOptions` cannot set headers (`LogLevel` only). The SDK therefore **does not expose** request-level caller-supplied idempotency for message create. Do not invent a workaround in implementation. See Assumptions & Blockers.

### Enums actually needed (`map/models/enums.md`)

Build with static members or `Type.FromValue("wire")`. These are `StringEnum<T>` (`TwilioSdk.Models.Enums` + `TwilioSdk.Core.Enum`), **not** C# enums. Compare with `==` / `.Value`.

| Type | Members (C# (wire)) |
|---|---|
| `TwilioSdk.Models.Enums.MessageEnumStatus` | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` |
| `TwilioSdk.Models.Enums.MessageEnumUpdateStatus` | `Canceled (canceled)` — only cancel value |
| `TwilioSdk.Models.Enums.MessageEnumScheduleType` | `Fixed (fixed)` — only schedule value |
| `TwilioSdk.Models.Enums.MessageEnumDirection` | `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)` |
| `TwilioSdk.Models.Enums.MessageEnumContentRetention` | `Retain (retain)`, `Discard (discard)` — pass `null` on send |
| `TwilioSdk.Models.Enums.MessageEnumAddressRetention` | `Retain (retain)`, `Obfuscate (obfuscate)` — pass `null` on send |
| `TwilioSdk.Models.Enums.MessageEnumTrafficType` | `Free (free)` — pass `null` |
| `TwilioSdk.Models.Enums.MessageEnumRiskCheck` | `Enable (enable)`, `Disable (disable)` — pass `null` |
| `TwilioSdk.Models.Enums.ValidationError` | `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)` |
| `TwilioSdk.Servers.ServerEnvironment` | `Production (production)` |

`LineTypeIntelligenceInfo.Type` is `string?`. It is **not** `LineType` (that enum is a different model). No generated SMS-capability enum on this field.

### Error types (all in-scope ops)

| | |
|---|---|
| Thrown on non-2xx | `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` (**Case B** on Create/Fetch/List/Update/Delete Message **and** FetchPhoneNumber3) |
| Status | `ex.Error.StatusCode` (`System.Net.HttpStatusCode`) — 401 Unauthorized, 403 Forbidden, 404 Not Found, 400 validation, 429 rate limit, 5xx provider |
| Body | `ex.Error.ReadAsString()`; optional `ReadAsJson<T>()` / `ReadAsBytes()`. There are **no** `TryGet…` typed accessors on Case B. |
| 2xx delivery failure | **Not** an exception. `ApiV2010AccountMessage.Status` + `ErrorCode` + `ErrorMessage`. |
| Not-usable number on Lookup (2xx) | `LookupResponse.Valid == false` and/or `ValidationErrors` non-empty — reject registration now. |
| Transport | `HttpRequestException` / timeout — distinct from provider 4xx. |
| `JsonException` | See trap notes — two directions; not `SdkException`. |

---

## Trap notes

⚠ Step 1 (client registration) — `TwilioSdkClient` takes an `HttpClient` whose lifetime is not the same question as the SDK wrapper’s lifetime; getting this wrong exhausts sockets or drops options. **MUST load `dotnet-client-initialization`** before constructing or DI-registering the client.

⚠ Step 1 (authentication) — `AccountSidAuthToken` is a `BasicAuthCredentials` with `required` `Username`/`Password`; mis-wiring or logging secrets is not visible from the property type. **MUST load `dotnet-authentication`** before setting credentials.

⚠ Step 1 (Twilio:BaseUrl) — which object actually becomes the messaging host (`Server.Default.Production.BaseUrl` vs `HttpClient.BaseAddress` vs other `DefaultN` nodes, including Lookup’s `Default4`) is not the constructor signature. A wrong choice either ignores `Twilio:BaseUrl` or redirects Lookup. **MUST load `dotnet-configuration-resilience`** before assigning any base address.

⚠ Step 1 (retries/timeouts) — what `RetryOptions.Timeout` bounds, and whether a failed **write** (`CreateMessage` / `UpdateMessage`) can run more than once, is not on the operation signature. **MUST load `dotnet-configuration-resilience`** before tuning `options.Retry` or sharing an `HttpClient`.

⚠ Step 1 (logging) — `LoggingOptions` can emit request/response bodies that contain shopper numbers and message text. **MUST load `dotnet-configuration-resilience`** before enabling logging.

⚠ Step 2 / 3 / 4 / 9 (calling) — `FetchPhoneNumber3` (15), `CreateMessage` (24), `UpdateMessage` (2), and `ListMessage` (8) have nullable parameters **without C# defaults**; a positional call binds the wrong argument. **MUST load `dotnet-calling-endpoints`** before the first call.

⚠ Step 2 / 3 / 6 (models) — statuses and validation errors are `StringEnum<T>`, not C# enums; records are `init`-only; `LookupResponse` is the lookup payload itself (no extra envelope); `ListMessageResponse.Messages` is the list envelope. **MUST load `dotnet-models`** before mapping fields or comparing status.

⚠ Step 3 / 5 / 8 / 10 (error boundary) — every in-scope operation is Case B (`SdkException<RawError>`); there is no `{Op}Error` / `TryGet…` ladder. An SDK-exception-only catch that assumes Case A accessors will not compile or will miss failures. **MUST load `dotnet-error-handling`** before any `try/catch`.

⚠ Step 10 — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 10 — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 9 (pagination) — `ListMessage` has no SDK auto-iterator; `NextPageUri` vs `page` vs `pageToken` (and that the envelope has no `PageToken` property) is not a list-return-type detail you can skip. **MUST load `dotnet-configuration-resilience`** before writing the reconciliation loop.

⚠ Tests — the testable seam is not the controller types. **MUST load `dotnet-testing`** before stubbing Twilio.

---

## REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — `HttpClient` + `TwilioSdkClient` / `AddTwilioSdkClient` |
| `dotnet-authentication` | Step 1 — `AccountSidAuthToken` / `BasicAuthCredentials` |
| `dotnet-configuration-resilience` | Step 1 BaseUrl/retry/timeout/logging; Step 9 pagination |
| `dotnet-calling-endpoints` | Steps 2–9 — named arguments, must-pass-null params, `ct:` |
| `dotnet-models` | Steps 2–9 — `StringEnum<T>`, envelopes, init-only records |
| `dotnet-error-handling` | Step 10 — Case B `RawError`, send-failure catch that must not abort orders, **both** `JsonException` directions above |
| `dotnet-testing` | Tests — `HttpClient` constructor seam |

---

## Assumptions & Blockers

**Assumptions**
- Shopper validation uses Lookups **V2** `FetchPhoneNumber3` (not V1), with `fields` including `line_type_intelligence` (and optionally `validation` / `line_status`); canonical form is `LookupResponse.PhoneNumber`.
- Immediate notifications send with `from` = `Twilio:FromNumber`. Provider-scheduled follow-ups send with `Twilio:MessagingServiceSid` + `MessageEnumScheduleType.Fixed` + `sendAt` because the generated schedule enum is documented as Messaging Services only.
- `statusCallback` stays `null` (no public URL). Status is refreshed via `FetchMessage` / reconciliation `ListMessage`.
- US undeliverable (carrier/registration) is a 2xx message with later `failed`/`undelivered` + `ErrorCode`, not a missing SDK operation.
- `accountSid` on every Messages call is the same Account SID as `BasicAuthCredentials.Username`.
- Feature 8 is `UpdateMessage` with `body: ""`, not `DeleteMessage`.

**Blockers / gaps**
- **Caller-supplied idempotency (feature 7) is not exposed.** `CreateMessage` always sets `Idempotency-Key` to `Guid.NewGuid()` internally. `RequestOptions` has only `LogLevel`. There is no SDK parameter or header bag for the resend key. Do not invent a workaround; POST `/api/notifications/{id}/resend` cannot get same-key provider dedup from this SDK.
- `LineTypeIntelligenceInfo.Type` has no generated enum; SMS-usability beyond `Valid`/`ValidationErrors` is an untyped string. Exact live strings are **UNVERIFIED**.
- Exact HTTP status / error body when `UpdateMessage(status: Canceled)` is applied to an already-sent message: **UNVERIFIED** — read `RawError.StatusCode` + `ReadAsString()`, then `FetchMessage`.
- Exact `Body` after redact: **UNVERIFIED**.
- Provider `sendAt` window (minimum lead time / maximum delay): not on the SDK surface — **UNVERIFIED**.
- `ListMessage` envelope has no `PageToken` field; how the live `next_page_uri` encodes the next token is **UNVERIFIED** beyond the XML that `pageToken` “is provided by the API”.
