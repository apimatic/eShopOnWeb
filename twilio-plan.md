# eShopOnWeb SMS order-notification — Twilio .NET SDK contract sheet

NuGet: `AsadAli.TwilioSdk` (version-less). Root namespace: `TwilioSdk`. Client: `TwilioSdkClient`. Map stamp: `51fdf48`.

## Scope & sequence

1. **Client, auth, messaging-only base URL** — construct `TwilioSdkClient` from `Twilio:AccountSid` / `Twilio:AuthToken` / optional `Twilio:BaseUrl`.
2. **Register contact number** — `LookupsV2PhoneNumber.FetchPhoneNumber3`; store canonical E.164; reject non-usable destinations.
3. **Send SMS** (placed / dispatched / cancelled / resend / follow-up immediate) — `Api20100401Message.CreateMessage` with `from` = `Twilio:FromNumber`.
4. **Schedule provider-side follow-up** — `CreateMessage` with `scheduleType` + `sendAt` + `messagingServiceSid` = `Twilio:MessagingServiceSid`.
5. **Cancel scheduled follow-up** — `Api20100401Message.UpdateMessage` with `status` = canceled.
6. **Fetch delivery outcome** — `Api20100401Message.FetchMessage` by SID (no webhooks).
7. **Resend failed message** — `CreateMessage` again. **Caller-supplied idempotency is a GAP** (see Blockers).
8. **Dispose message body at provider** — `UpdateMessage` with empty `body` (do **not** `DeleteMessage`).
9. **Reconciliation listing** — `Api20100401Message.ListMessage` filtered by `from` = `Twilio:FromNumber` and date range; page until exhausted.

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

No-throw `…Result` variants: **absent** on every operation below (`sdk-map.md`).

Create/Update POST bodies are `application/x-www-form-urlencoded` (wire names below). Null form/query values are omitted; an empty string **is** sent. (`Api/Api20100401Message.cs`, `Core/ParameterFlattener.cs`)

### Client construction, auth, servers

| Fact | Contract | Cite |
|---|---|---|
| Client ctor | `TwilioSdk.TwilioSdkClient(System.Net.Http.HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)` | `sdk-map.md`, `TwilioSdkClient.cs` |
| DI | `TwilioSdk.ServiceCollectionExtensions.AddTwilioSdkClient(Action<TwilioSdkClientOptions>? configure = null)` | `sdk-map.md`, `ServiceCollectionExtensions.cs` |
| Options | `Environment`: `TwilioSdk.Servers.ServerEnvironment`; `Retry`: `TwilioSdk.Core.Configuration.RetryOptions`; `Logging`: `TwilioSdk.Core.Configuration.LoggingOptions`; `Server`: `TwilioSdk.ServerOptions`; `AccountSidAuthToken`: `TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md` *Servers & auth*, `TwilioSdkClientOptions.cs` |
| Auth credentials | `BasicAuthCredentials` (`TwilioSdk.Core.Authentication.Basic`): `Username` `string` **required**, `Password` `string` **required**. Map XML: API key as username + API key secret as password, **or** Account SID + Auth Token (limit SID/token to local testing). Config: `Username` = `Twilio:AccountSid`, `Password` = `Twilio:AuthToken` (secret — never log). | `sdk-map.md` *Servers & auth*, `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Environment | `TwilioSdk.Servers.ServerEnvironment.Production` (wire `production`); `ServerEnvironment.Default()` → Production | `sdk-map.md`, `Servers/ServerEnvironment.cs` |
| Messaging base URL (**`Twilio:BaseUrl`**) | Messaging ops use server node **Default**. Set `options.Server.Default.Production.BaseUrl` (`TwilioSdk.Servers.DefaultOptions.ProductionOptions.BaseUrl`: `string`, default `"https://api.twilio.com"`). When `Twilio:BaseUrl` is set, assign it **verbatim** to this property only. | `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Api/Api20100401Message.cs` (`_server.Default(...)`) |
| Lookup host (must **not** take `Twilio:BaseUrl`) | Lookup ops use server node **Default4**. `options.Server.Default4.Production.BaseUrl` (`TwilioSdk.Servers.Default4Options.ProductionOptions.BaseUrl`: `string`, default `"https://lookups.twilio.com"`). Leave unset so Lookup stays on the lookups host. | `Servers/Default4Options.cs`, `Api/LookupsV2PhoneNumber.cs` (`_server.Default4(...)`) |
| `TwilioSdk.ServerOptions` | `Default`, `Default1`…`Default14` each a `TwilioSdk.Servers.DefaultNOptions`. Only `Default` (api/messaging) and `Default4` (lookups) are in scope. | `ServerOptions.cs` (repo root ⇒ namespace `TwilioSdk`) |
| Per-call options | `TwilioSdk.Core.RequestOptions`: `LogLevel` (`Microsoft.Extensions.Logging.LogLevel?`) only. **No extra-headers collection.** | `Core/RequestOptions.cs` |
| Retry options | `TwilioSdk.Core.Configuration.RetryOptions` (all members `required`, or `RetryOptions.Default()`): `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout` (`TimeSpan?`), `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry` | `sdk-map.md` |

### Operation: phone number lookup / validation (step 2)

| | |
|---|---|
| Controller | `client.LookupsV1PhoneNumberApi` exists (`FetchPhoneNumber2` → `TwilioSdk.Models.LookupsV1PhoneNumber`) but **do not use it** — no `Valid` flag; `Carrier` is `object?`. Use v2. |
| Controller | `client.LookupsV2PhoneNumber` · `TwilioSdk.Api.LookupsV2PhoneNumber` |
| Method | `FetchPhoneNumber3` |
| HTTP | `GET /v2/PhoneNumbers/{PhoneNumber}` · server **Default4 (lookups)** |
| Signature | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | 15 params `fields` … `partnerSubId` — nullable, no default → pass `null` to skip |
| Query wire ← C# | `Fields` ← `fields`, `CountryCode` ← `countryCode`, `FirstName` ← `firstName`, `LastName` ← `lastName`, `AddressLine1` ← `addressLine1`, `AddressLine2` ← `addressLine2`, `City` ← `city`, `State` ← `state`, `PostalCode` ← `postalCode`, `AddressCountryCode` ← `addressCountryCode`, `NationalId` ← `nationalId`, `DateOfBirth` ← `dateOfBirth`, `LastVerifiedDate` ← `lastVerifiedDate`, `VerificationSid` ← `verificationSid`, `PartnerSubId` ← `partnerSubId` |
| `fields` values (comma-separated) | `validation`, `caller_name`, `sim_swap`, `call_forwarding`, `line_status`, `line_type_intelligence`, `identity_match`, `reassigned_number`, `sms_pumping_risk`, `phone_number_quality_score`, `pre_fill` (source XML on `FetchPhoneNumber3`) |
| In-scope `fields` | `"validation,line_type_intelligence,line_status"` |
| Returns | `TwilioSdk.Models.LookupResponse` — **no extra envelope** |
| Error | **Case B** `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` · accessors: `StatusCode: HttpStatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()` |
| Pagination | none |
| Cite | `map/operations/LookupsV2PhoneNumber.md`, `map/models/records-4-Li-Me.md`, `Api/LookupsV2PhoneNumber.cs` |

**`LookupResponse` fields used** (`TwilioSdk.Models`, `Models/LookupResponse.cs`):

| C# (wire) | Type | Role |
|---|---|---|
| `PhoneNumber (phone_number)` | `string?` | Provider canonical **E.164** (`+` + country code + subscriber). **This is what gets stored.** |
| `Valid (valid)` | `bool?` | XML: whether the number is in a valid range freely assignable by a carrier to a user. Reject registration when this is not `true`. |
| `ValidationErrors (validation_errors)` | `IReadOnlyList<TwilioSdk.Models.Enums.ValidationError>?` | Reasons when invalid |
| `NationalFormat (national_format)` | `string?` | Display only |
| `CountryCode (country_code)` | `string?` | ISO 3166-1 alpha-2 |
| `CallingCountryCode (calling_country_code)` | `string?` | E.164 prefix |
| `LineTypeIntelligence (line_type_intelligence)` | `TwilioSdk.Models.LineTypeIntelligenceInfo?` | Populated when `fields` includes `line_type_intelligence` |
| `LineStatus (line_status)` | `TwilioSdk.Models.LineStatusInfo?` | Populated when `fields` includes `line_status` |

**`LineTypeIntelligenceInfo`** (`records-3-Fl-Li.md`): `MobileCountryCode (mobile_country_code): string?`, `MobileNetworkCode (mobile_network_code): string?`, `CarrierName (carrier_name): string?`, `Type (type): string?`, `ErrorCode (error_code): int?`. **`Type` is an untyped string — no enum / value list in map or source.**

**`LineStatusInfo`**: `Status (status): string?`, `ErrorCode (error_code): int?`. **`Status` is an untyped string — no enum / value list in map or source.**

There is **no** generated `sms_capable` / `sms` boolean. See Assumptions.

### Operation: send / schedule SMS (steps 3, 4, 7)

| | |
|---|---|
| Controller | `client.Api20100401Message` · `TwilioSdk.Api.Api20100401Message` |
| Method | `CreateMessage` |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` · server **Default (api)** |
| Signature | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, TwilioSdk.Models.Enums.MessageEnumContentRetention? contentRetention, TwilioSdk.Models.Enums.MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, TwilioSdk.Models.Enums.MessageEnumTrafficType? trafficType, bool? shortenUrls, TwilioSdk.Models.Enums.MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, TwilioSdk.Models.Enums.MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Required | `accountSid` (path, = `Twilio:AccountSid`), `to` (destination E.164) |
| Must-pass-explicitly | 24 params `statusCallback` … `contentSid` — nullable, no default → pass `null` to skip |
| Form wire ← C# | `To` ← `to`, `StatusCallback` ← `statusCallback`, `ApplicationSid` ← `applicationSid`, `MaxPrice` ← `maxPrice`, `ProvideFeedback` ← `provideFeedback`, `Attempt` ← `attempt`, `ValidityPeriod` ← `validityPeriod`, `ForceDelivery` ← `forceDelivery`, `ContentRetention` ← `contentRetention`, `AddressRetention` ← `addressRetention`, `SmartEncoded` ← `smartEncoded`, `PersistentAction` ← `persistentAction`, `TrafficType` ← `trafficType`, `ShortenUrls` ← `shortenUrls`, `ScheduleType` ← `scheduleType`, `SendAt` ← `sendAt`, `SendAsMms` ← `sendAsMms`, `ContentVariables` ← `contentVariables`, `RiskCheck` ← `riskCheck`, `From` ← `from`, `FallbackFrom` ← `fallbackFrom`, `MessagingServiceSid` ← `messagingServiceSid`, `Body` ← `body`, `MediaUrl` ← `mediaUrl`, `ContentSid` ← `contentSid` |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` — **no extra envelope** |
| Error | **Case B** `SdkException<RawError>` · same accessors as above |
| Pagination | none |
| Cite | `map/operations/Api20100401Message.md` |

**`from` vs `messagingServiceSid`**

- Immediate send (placed / dispatched / cancelled / resend / immediate follow-up): pass `from:` `Twilio:FromNumber`. `messagingServiceSid` may be `Twilio:MessagingServiceSid` or `null` — both parameters are independently optional.
- **Scheduled** send: `MessageEnumScheduleType` XML: *“For Messaging Services only”* — pass `messagingServiceSid:` `Twilio:MessagingServiceSid`, `scheduleType:` `MessageEnumScheduleType.Fixed` (wire `fixed`), `sendAt:` the provider queue time. `from` may still be `Twilio:FromNumber`. The enum XML mentions a `send_time` parameter; **that identifier does not exist** — the C#/wire param is `sendAt` / `SendAt`.
- `sendAt` is passed as `DateTimeOffset?` form `Param` (not `ToIso8601()`). Flatten JSON-serializes `DateTimeOffset` then sends the string (`Core/ParameterFlattener.cs`, `Core/Extensions/ObjectExtensions.cs`).

**Idempotency (step 7) — see Blockers.** `CreateMessage` always attaches header `Idempotency-Key` with `Guid.NewGuid()` (`Api/Api20100401Message.cs`). There is **no** method parameter and **no** `RequestOptions` header slot for a caller-supplied key. Exact header name if it were controllable: `Idempotency-Key`.

Capture from the response: `Sid` (provider id), `Status` (delivery outcome), plus `ErrorCode` / `ErrorMessage` when present.

### Operation: fetch message status (step 6)

| | |
|---|---|
| Method | `FetchMessage` |
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` · Default (api) |
| Signature | `FetchMessage(string accountSid, string sid, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` — **no extra envelope** |
| Error | **Case B** `SdkException<RawError>` |
| Pagination | none |
| Cite | `map/operations/Api20100401Message.md` |

No inbound webhook/callback exists; poll/read this operation (or list) for current `Status`.

### Operation: cancel scheduled / redact body (steps 5, 8)

| | |
|---|---|
| Method | `UpdateMessage` |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` · Default (api) |
| Notes | “Update a Message resource (used to redact Message `body` text and to cancel not-yet-sent messages)” |
| Signature | `UpdateMessage(string accountSid, string sid, string? body, TwilioSdk.Models.Enums.MessageEnumUpdateStatus? status, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `body`, `status` — nullable, no default |
| Form wire ← C# | `Body` ← `body`, `Status` ← `status` |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` |
| Error | **Case B** `SdkException<RawError>` |
| Cite | `map/operations/Api20100401Message.md` |

- **Cancel follow-up:** `status:` `MessageEnumUpdateStatus.Canceled` (wire `canceled`), `body:` `null` (omitted).
- **Redact content:** `body:` `""` (empty string — **not** `null`, or `Body` is omitted and nothing is redacted), `status:` `null`. After success, `Body` at the provider is the redacted value; `Sid` / `Status` remain. **UNVERIFIED** live redacted-body string (empty vs placeholder) — read `Body` from the returned resource; fall back to treating a successful 2xx as redacted if `Body` is null/empty.
- Also hardcodes `Idempotency-Key: Guid.NewGuid()` (same gap as create; not needed for this flow).

**Do not use `DeleteMessage`** for shopper content disposal: it *deletes the Message resource* (`DELETE …/Messages/{Sid}.json`, returns `void` / `Task`). That removes the provider record that a message was sent — contradicts “the fact that a message was sent, and what became of it, survives.” Signature for completeness: `DeleteMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` · Case B · cite same operations page.

### Operation: reconciliation list (step 9)

| | |
|---|---|
| Method | `ListMessage` |
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages.json` · Default (api) |
| Signature | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | 8 params `to` … `pageToken` |
| Query wire ← C# | `To` ← `to`, `From` ← `from`, `DateSent` ← `dateSent`, `DateSent<` ← `dateSentQuery`, `DateSent>` ← `dateSentQueryQuery`, `PageSize` ← `pageSize`, `Page` ← `page`, `PageToken` ← `pageToken` |
| Date serialization | each non-null date is `date.ToIso8601()` → `yyyy-MM-ddTHH:mm:ss.fff'Z'` UTC (`Api/Api20100401Message.cs`, `Core/Extensions/DateTimeOffsetExtensions.cs`) |
| XML `pageSize` | default 50, maximum 1000 |
| Returns | `TwilioSdk.Models.ListMessageResponse` |
| Error | **Case B** `SdkException<RawError>` |
| Pagination | map: **none** (no auto-pager; only `page` / `pageToken` / `pageSize` — no `perPage`). Walk `NextPageUri` / `pageToken` in a loop. |
| Cite | `map/operations/Api20100401Message.md`, `map/models/records-4-Li-Me.md` |

**Provider-side From filter (required):** pass `from:` `Twilio:FromNumber` (wire `From`) so the provider returns **that sender’s** messages — do not list the whole account and filter locally.

**Date range (`from`/`to` ISO-8601 query params on eShop’s API):** pass `dateSentQueryQuery:` range-start (wire `DateSent>`), `dateSentQuery:` range-end (wire `DateSent<`), `dateSent:` `null`. XML on all three date params talks about `<=YYYY-MM-DD` / `>=YYYY-MM-DD` **inside** a single `DateSent` value; the generated SDK **cannot** send those operator prefixes — inequalities are the separate `DateSent<` / `DateSent>` params. Whether `>` / `<` are exclusive of the exact instants is **UNVERIFIED** (live provider). Defensive: keep the eShop `from`/`to` instants as the two bounds; if a live listing drops messages sitting exactly on an endpoint, widen that bound by one second and re-fetch — do not invent inclusivity.

**`ListMessageResponse`** (`TwilioSdk.Models`): `End (end): int?`, `FirstPageUri (first_page_uri): string?`, `NextPageUri (next_page_uri): string?`, `Page (page): int?`, `PageSize (page_size): int?`, `PreviousPageUri (previous_page_uri): string?`, `Start (start): int?`, `Uri (uri): string?`, `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?`.

### Response model: `ApiV2010AccountMessage`

Namespace `TwilioSdk.Models` · `map/models/records-1-Ac-Ca.md` · `Models/ApiV2010AccountMessage.cs`. All fields optional (`?`). **No wrapper property** — create/fetch/update return this record directly.

| C# (wire) | Type | Integration use |
|---|---|---|
| `Sid (sid)` | `string?` | Provider message id (pattern `^(SM\|MM)[0-9a-fA-F]{32}$`) |
| `Status (status)` | `MessageEnumStatus?` | Current delivery outcome |
| `To (to)` | `string?` | Destination |
| `From (from)` | `string?` | Sender |
| `Body (body)` | `string?` | Text (empty/redacted after step 8) |
| `DateSent (date_sent)` | `string?` | RFC 2822 GMT when sent |
| `DateCreated (date_created)` | `string?` | RFC 2822 GMT created |
| `DateUpdated (date_updated)` | `string?` | RFC 2822 GMT updated |
| `ErrorCode (error_code)` | `int?` | Set when status is `failed` / `undelivered` (XML: do not branch programmatically on a specific code — codes change) |
| `ErrorMessage (error_message)` | `string?` | Description of `error_code` |
| `MessagingServiceSid (messaging_service_sid)` | `string?` | MG… if a Messaging Service was used |
| `AccountSid (account_sid)` | `string?` | AC… |
| `Direction (direction)` | `MessageEnumDirection?` | Outbound API vs inbound, etc. |
| `NumSegments (num_segments)` | `string?` | |
| `NumMedia (num_media)` | `string?` | |
| `Price (price)` | `string?` | |
| `PriceUnit (price_unit)` | `string?` | |
| `Uri (uri)` | `string?` | |
| `ApiVersion (api_version)` | `string?` | |
| `SubresourceUris (subresource_uris)` | `object?` | |

### Enums in scope (`TwilioSdk.Models.Enums`, `map/models/enums.md`)

Build with static members or `Type.FromValue("wire")` — these are `StringEnum<T>`, not C# enums.

| Type | Members (C# = wire) |
|---|---|
| `MessageEnumStatus` | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` |
| `MessageEnumUpdateStatus` | `Canceled (canceled)` |
| `MessageEnumScheduleType` | `Fixed (fixed)` |
| `MessageEnumDirection` | `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)` |
| `ValidationError` | `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)` |

**Outcome mapping (product):** (a) registration reject = Lookup `Valid != true` (and/or Lookup Case B 4xx). (b) accepted-then-undeliverable (e.g. US destination this account cannot reach) = `CreateMessage` 2xx with later `FetchMessage` `Status` `Failed` / `Undelivered` — **expected outcome, not a gap**. (c) API/auth failure = `SdkException<RawError>` with `StatusCode` 401/403/5xx (read body via `ReadAsString` / `ReadAsJson<T>`; no typed `{Op}Error` on these ops). **UNVERIFIED** live error JSON shape — extract `code`/`message` best-effort if present, else generic message from `ReadAsString()`.

### Error types (all in-scope ops)

| | |
|---|---|
| Throw type | `TwilioSdk.Core.Exceptions.SdkException<TError>` with `.Error` (`Core/Exceptions/SdkException.cs`) |
| In-scope TError | `TwilioSdk.Core.ErrorResponse.RawError` (**Case B** on every operation above) |
| `RawError` | `StatusCode: HttpStatusCode` · `ReadAsBytes(): ReadOnlyMemory<byte>` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` |
| Case A `TryGet…` | **not present** on these operations |
| `ApiError.TryGetRawError` | N/A here (no typed `{Op}Error`) |

---

## Trap notes

⚠ Step 1 (client registration) — the constructor does not tell you HttpClient/handler lifetime vs the SDK wrapper lifetime; getting that wrong leaks or starves the pipeline. **MUST load `dotnet-client-initialization`** before `new TwilioSdkClient` or `AddTwilioSdkClient`.

⚠ Step 1 (auth) — which credentials property, username/password mapping, and how secrets are sourced (never hardcoded, never logged) are not visible from the client constructor. **MUST load `dotnet-authentication`** before setting `AccountSidAuthToken`.

⚠ Step 1 (base URL, retries, timeouts, list paging) — SDK retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; whether a failed **write** (`CreateMessage` / `UpdateMessage` POST) can be re-sent is not in the signature; `Twilio:BaseUrl` must hit only the Default (api) node; `ListMessage` has no auto-pager (`NextPageUri` / `pageToken`). **MUST load `dotnet-configuration-resilience`** before wiring the client or looping reconciliation.

⚠ Steps 2–9 (every call) — long nullable-without-default parameter lists (`CreateMessage` 24, `FetchPhoneNumber3` 15, `ListMessage` 8) mis-bind if passed positionally. **MUST load `dotnet-calling-endpoints`** before the first `client.*` call.

⚠ Steps 2–9 (models / enums / status) — `MessageEnum*` / `ValidationError` are `StringEnum<T>` not C# enums; response records drop unmodeled JSON; `LookupResponse` / `ApiV2010AccountMessage` have no extra envelope but `ListMessageResponse` wraps `Messages`. **MUST load `dotnet-models`** before constructing params or reading fields.

⚠ Step 10 (error boundary) — every in-scope operation is Case B (`SdkException<RawError>`); a Case A `TryGet…` ladder will not match. **MUST load `dotnet-error-handling`** before any `try/catch`.

⚠ Step 10 (error boundary) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 10 (error boundary) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Tests — the test seam is not the controller types. **MUST load `dotnet-testing`** before stubbing the SDK.

---

## REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

- `dotnet-client-initialization` — step 1 client / DI / HttpClient lifetime
- `dotnet-authentication` — step 1 `AccountSidAuthToken` / secrets
- `dotnet-calling-endpoints` — steps 2–9 named-argument calls
- `dotnet-models` — enums, records, envelopes, unmodeled JSON
- `dotnet-error-handling` — step 10 catch boundary (Case B + both `JsonException` directions below)
- `dotnet-configuration-resilience` — step 1 retries/timeouts/`Twilio:BaseUrl` on Default only; step 9 pagination
- `dotnet-testing` — tests for the integration layer

`System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## Assumptions & Blockers

### Assumptions

- Lookups **v2** `FetchPhoneNumber3` is the registration validator (v1 has no `Valid` and an untyped `Carrier`).
- Stored number = `LookupResponse.PhoneNumber` (E.164). Reject when `Valid` is not `true` (including `null` — treat as reject). `LineTypeIntelligence.Type` / `LineStatus.Status` may be inspected if present but have **no SDK-enumerated SMS-capability values**.
- Immediate SMS uses `from` = `Twilio:FromNumber`; scheduled follow-up also sends `messagingServiceSid` = `Twilio:MessagingServiceSid` because scheduling is documented as Messaging Services only.
- Content disposal uses `UpdateMessage(body: "")`, not `DeleteMessage`.
- Reconciliation asks the provider with `ListMessage(from: Twilio:FromNumber, dateSentQueryQuery: from, dateSentQuery: to)` and pages on `pageToken` / `NextPageUri`.
- `Twilio:BaseUrl` is applied only to `options.Server.Default.Production.BaseUrl`; Lookup stays on Default4.
- US undeliverable / `TWILIO_UNREACHABLE_TO_NUMBER` is a later `failed`/`undelivered` **outcome**, not a registration or SDK gap.
- Auth token is `BasicAuthCredentials.Password`; never log it.

### Blockers / gaps

1. **Caller-supplied idempotency key (capability 6) — NOT available on the public SDK surface.** `CreateMessage` always sends header `Idempotency-Key` with a fresh `Guid.NewGuid()` (`Api/Api20100401Message.cs`). `RequestOptions` exposes only `LogLevel` (`Core/RequestOptions.cs`) — no headers, no idempotency parameter. Repeating an operator resend under the same application key **will still generate a new header value** and can send a second message. Do not invent a workaround (custom handlers, wrapping `HttpClient`, etc.). Exact header name for the record: `Idempotency-Key`.
2. **No typed “usable SMS destination” flag.** Lookup `Valid` is range/assignability, not SMS capability. `LineTypeIntelligenceInfo.Type` and `LineStatusInfo.Status` are untyped strings with no value lists in the map or source. If product rules require rejecting landlines/voip/inactive lines by those strings, those allow-lists are **not** SDK-grounded (**UNVERIFIED**).
3. **`ListMessage` date-bound inclusivity** (`DateSent>` / `DateSent<` vs a closed ISO-8601 interval) is **UNVERIFIED** live behavior; see defensive note in the ListMessage row.
