# Twilio .NET SDK — eShopOnWeb SMS notifications

NuGet: `AsadAli.TwilioSdk` (install version-less: `dotnet add package AsadAli.TwilioSdk`). Root namespace: `TwilioSdk`. Client: `TwilioSdkClient`. Map stamp: source commit `51fdf48`.

## Scope & sequence

| Step | Product flow | SDK operation |
|---|---|---|
| 1 | Client + DI + credentials + messaging BaseUrl | `new TwilioSdkClient(httpClient, options)` / `AddTwilioSdkClient` |
| 2 | `POST /api/contact-numbers` — validate destination, store canonical form | `client.LookupsV2PhoneNumber.FetchPhoneNumber3` |
| 3 | Send SMS (order placed / dispatched / cancelled / resend) | `client.Api20100401Message.CreateMessage` |
| 4 | Queue follow-up “how was delivery” **at Twilio** (not a local timer) | `CreateMessage` with `scheduleType` + `sendAt` + `messagingServiceSid` |
| 5 | Cancel a not-yet-sent follow-up on order cancel | `client.Api20100401Message.UpdateMessage` (`status: Canceled`) |
| 6 | Fetch current delivery outcome (no webhooks) | `client.Api20100401Message.FetchMessage` |
| 7 | Operator resend with caller idempotency key | **GAP** — see Blockers. `CreateMessage` has no caller key parameter |
| 8 | Dispose message text at the provider | `UpdateMessage` (`body: ""`). Do **not** use `DeleteMessage` |
| 9 | Reconciliation list scoped to this app’s From number | `client.Api20100401Message.ListMessage` (`from` = `Twilio:FromNumber`) |
| 10 | Error boundary for lookup/send/fetch/update/list | Case B `SdkException<RawError>` on every in-scope op |

Out of scope (do not call): `Api20100401ValidationRequest.CreateValidationRequest` (outgoing-caller-ID voice verification), `NumbersV1EligibilityApi.CreateEligibility` (hosted-number eligibility), `LookupsV1PhoneNumberApi.FetchPhoneNumber2` (no `Valid` flag; `Carrier` is untyped `object?`), `DeleteMessage` (removes the resource, not just the body).

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

### Client construction, auth, BaseUrl

| Fact | Contract | Source |
|---|---|---|
| Package | `AsadAli.TwilioSdk` | `sdk-map.md` |
| Root namespace | `TwilioSdk` | `sdk-map.md` |
| Client | `TwilioSdk.TwilioSdkClient` | `TwilioSdkClient.cs` |
| Constructor | `TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)` — **only** this overload | `sdk-map.md` |
| Options | `TwilioSdk.TwilioSdkClientOptions`: `Environment: TwilioSdk.Servers.ServerEnvironment`, `Retry: TwilioSdk.Core.Configuration.RetryOptions`, `Logging: TwilioSdk.Core.Configuration.LoggingOptions`, `Server: TwilioSdk.ServerOptions`, `AccountSidAuthToken: TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md`, `TwilioSdkClientOptions.cs` |
| Environment | `TwilioSdk.Servers.ServerEnvironment.Production` (wire `"production"`). `Default()` → `Production` | `Servers/ServerEnvironment.cs` |
| Credentials | `options.AccountSidAuthToken = new BasicAuthCredentials { Username = accountSid, Password = authToken }` — both members `required`. Encoded as HTTP Basic `Username:Password` | `sdk-map.md` Servers & auth; `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Config bind | `Username` ← `Twilio:AccountSid` / `TWILIO_ACCOUNT_SID`; `Password` ← `Twilio:AuthToken` / `TWILIO_AUTH_TOKEN` | product config + auth map row |
| DI | `TwilioSdk.ServiceCollectionExtensions.AddTwilioSdkClient(Action<TwilioSdkClientOptions>? configure = null)` | `ServiceCollectionExtensions.cs` |
| Messaging BaseUrl **only** | `options.Server.Default.Production.BaseUrl = configuredBaseUrl` (verbatim). Default `"https://api.twilio.com"`. `CreateMessage` / `FetchMessage` / `ListMessage` / `UpdateMessage` / `DeleteMessage` all call `_server.Default(...)` | `ServerOptions.cs` (root ns `TwilioSdk`); `Servers/DefaultOptions.cs`; `Api/Api20100401Message.cs` |
| Lookup host (do **not** apply `Twilio:BaseUrl`) | `options.Server.Default4.Production.BaseUrl` default `"https://lookups.twilio.com"`. `FetchPhoneNumber3` calls `_server.Default4(...)` | `Servers/Default4Options.cs`; `Api/LookupsV2PhoneNumber.cs` |
| Not the messaging-messages host | `options.Server.Default1` default `"https://messaging.twilio.com"` is Messaging **Services** (v1 brand/service APIs), **not** 2010 Messages. Do not point `Twilio:BaseUrl` here | `Servers/Default1Options.cs` |
| `RequestOptions` | `TwilioSdk.Core.RequestOptions` — **only** member `LogLevel? LogLevel`. No headers, no idempotency field | `Core/RequestOptions.cs` |
| No-throw variants | Absent across this SDK — every operation is throw-only | `sdk-map.md` |

`accountSid` path argument on every Message op is the same Account SID as `BasicAuthCredentials.Username`.

### 1. Phone number validation / lookup

| | |
|---|---|
| Controller | `client.LookupsV2PhoneNumber` (`TwilioSdk.Api.LookupsV2PhoneNumber`) |
| Method | `FetchPhoneNumber3` |
| HTTP | `GET /v2/PhoneNumbers/{PhoneNumber}` (Default4 / lookups) |
| Signature | `Task<TwilioSdk.Models.LookupResponse> FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | 15 params `fields` … `partnerSubId` are nullable **with no C# default** → pass `null` to skip. Path `phoneNumber` is required |
| Query wire | `Fields` ← `fields`, `CountryCode` ← `countryCode`, … (identity-match fields unused here) |
| Returns | `TwilioSdk.Models.LookupResponse` — **not** wrapped; fields are on the return value itself |
| Error | **Case B** `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` |
| Map | `operations/LookupsV2PhoneNumber.md` |

**`fields` (wire `Fields`)** — comma-separated packages. Source XML: `validation`, `caller_name`, `sim_swap`, `call_forwarding`, `line_status`, `line_type_intelligence`, `identity_match`, `reassigned_number`, `sms_pumping_risk`, `phone_number_quality_score`, `pre_fill`. For this flow pass `fields: "line_type_intelligence"` (add `,line_status` only if you also read line status). Core `Valid` / `PhoneNumber` are on the model regardless.

**`LookupResponse` fields this flow reads** (`map/models/records-4-Li-Me.md`, `Models/LookupResponse.cs`):

| C# (wire) | Type | Role |
|---|---|---|
| `PhoneNumber (phone_number)` | `string?` | **Canonical form** — E.164 (`+` + country code + subscriber). Store this, not the caller’s typed input |
| `NationalFormat (national_format)` | `string?` | Display-only |
| `Valid (valid)` | `bool?` | “in a valid range that can be freely assigned by a carrier to a user” |
| `ValidationErrors (validation_errors)` | `IReadOnlyList<TwilioSdk.Models.Enums.ValidationError>?` | Why invalid |
| `CallingCountryCode (calling_country_code)` | `string?` | E.164 prefix |
| `CountryCode (country_code)` | `string?` | ISO 3166-1 alpha-2 |
| `LineTypeIntelligence (line_type_intelligence)` | `TwilioSdk.Models.LineTypeIntelligenceInfo?` | Populated when `fields` includes `line_type_intelligence` |
| `LineStatus (line_status)` | `TwilioSdk.Models.LineStatusInfo?` | Only if requested |

**`LineTypeIntelligenceInfo`** (`records-3-Fl-Li.md`): `MobileCountryCode (mobile_country_code): string?`, `MobileNetworkCode (mobile_network_code): string?`, `CarrierName (carrier_name): string?`, `Type (type): string?`, `ErrorCode (error_code): int?`. **`Type` is a plain `string?` — there is no SDK enum of line-type values.**

**`LineStatusInfo`**: `Status (status): string?`, `ErrorCode (error_code): int?`.

**Reject as unusable destination when:** `Valid` is not `true`, **or** `ValidationErrors` is non-empty, **or** `PhoneNumber` is null/blank (nothing to store as canonical). `LineTypeIntelligence.Type` is extra signal only — see UNVERIFIED below.

**`ValidationError`** (`map/models/enums.md`, ns `TwilioSdk.Models.Enums`): `TooShort ("TOO_SHORT")`, `TooLong ("TOO_LONG")`, `InvalidButPossible ("INVALID_BUT_POSSIBLE")`, `InvalidCountryCode ("INVALID_COUNTRY_CODE")`, `InvalidLength ("INVALID_LENGTH")`, `NotANumber ("NOT_A_NUMBER")`. Build with static members or `ValidationError.FromValue("TOO_SHORT")`.

If the typed input is national (no `+`), pass `countryCode` (ISO 3166-1 alpha-2). Path `phoneNumber` XML: E.164 or national; default country `+1`.

### 2–4. Create / schedule SMS

| | |
|---|---|
| Controller | `client.Api20100401Message` (`TwilioSdk.Api.Api20100401Message`) |
| Method | `CreateMessage` |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` (Default / api) |
| Body | `application/x-www-form-urlencoded` (not JSON) |
| Signature | `Task<TwilioSdk.Models.ApiV2010AccountMessage> CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, TwilioSdk.Models.Enums.MessageEnumContentRetention? contentRetention, TwilioSdk.Models.Enums.MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, TwilioSdk.Models.Enums.MessageEnumTrafficType? trafficType, bool? shortenUrls, TwilioSdk.Models.Enums.MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, TwilioSdk.Models.Enums.MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | 24 params `statusCallback` … `contentSid` — nullable, **no default** → pass `null` to skip. `accountSid` and `to` are required |
| Form wire | `To` ← `to`, `From` ← `from`, `MessagingServiceSid` ← `messagingServiceSid`, `Body` ← `body`, `ScheduleType` ← `scheduleType`, `SendAt` ← `sendAt`, plus the unused optionals listed in the signature |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` — **not** wrapped |
| Error | **Case B** `SdkException<RawError>` |
| Pagination | none |
| Map | `operations/Api20100401Message.md` |

Null form/query values are **omitted** (not sent). Empty string **is** sent.

**Immediate send (placed / dispatched / cancelled / resend):**

- `to:` destination (canonical E.164 from step 2)
- `body:` SMS text
- `from:` `Twilio:FromNumber` when sending from a number
- `messagingServiceSid:` `Twilio:MessagingServiceSid` when configured (nullable config → pass `null`)
- `scheduleType: null`, `sendAt: null`
- all other optionals: `null`

From vs Messaging Service: both are independent optional form fields. Supply `from` from `Twilio:FromNumber`. Supply `messagingServiceSid` from `Twilio:MessagingServiceSid` when non-empty. The signature does **not** require either; if the live API rejects a request with neither, that is a provider 400 on this same Case B path.

**Scheduled follow-up:**

- Same `to` / `body` / `from` as above
- `messagingServiceSid:` **required by the enum contract** — `MessageEnumScheduleType` XML: “For Messaging Services only”
- `scheduleType: TwilioSdk.Models.Enums.MessageEnumScheduleType.Fixed` (wire `"fixed"`)
- `sendAt:` future `DateTimeOffset` (serialized UTC ISO-8601 `yyyy-MM-ddTHH:mm:ss.fff'Z'`)
- Enum XML names the companion field `send_time`; the **actual** C#/wire names are `sendAt` / `SendAt`

**Identifier returned:** `ApiV2010AccountMessage.Sid` (wire `sid`). **Delivery/schedule outcome:** `Status` (`MessageEnumStatus`). Immediate create is typically `queued`/`accepted`; scheduled create is typically `scheduled`.

**Schedule window min/max:** not present on the map or in `CreateMessage` XML. See Blockers (UNVERIFIED).

### 5. Cancel a scheduled follow-up

| | |
|---|---|
| Method | `UpdateMessage` |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` |
| Notes | “Update a Message resource (used to redact Message `body` text and to cancel not-yet-sent messages)” |
| Signature | `Task<TwilioSdk.Models.ApiV2010AccountMessage> UpdateMessage(string accountSid, string sid, string? body, TwilioSdk.Models.Enums.MessageEnumUpdateStatus? status, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | `body` and `status` — nullable, no default |
| Form wire | `Body` ← `body`, `Status` ← `status` |
| Identifier | `sid` = the scheduled message’s `Sid` from create |
| Cancel call | `body: null` (omit Body), `status: TwilioSdk.Models.Enums.MessageEnumUpdateStatus.Canceled` (wire `"canceled"`) |
| Result status | `ApiV2010AccountMessage.Status` → `MessageEnumStatus.Canceled` (wire `"canceled"`) |
| Error | **Case B** `SdkException<RawError>` |
| Map | `operations/Api20100401Message.md` |

### 6. Fetch delivery outcome

| | |
|---|---|
| Method | `FetchMessage` |
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` |
| Signature | `Task<TwilioSdk.Models.ApiV2010AccountMessage> FetchMessage(string accountSid, string sid, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Identifier | `sid` = provider message SID |
| Returns | `ApiV2010AccountMessage` (same model as create) |
| Error | **Case B** `SdkException<RawError>` (not-found SID → this path; read `ex.Error.StatusCode`) |
| Map | `operations/Api20100401Message.md` |

### 7. Idempotent send

`CreateMessage` has **no** `idempotencyKey` parameter. `RequestOptions` cannot carry headers. The generated method always attaches `Idempotency-Key: Guid.NewGuid()` (a fresh value per call). See **Blockers**.

### 8. Redact message body at the provider

Same `UpdateMessage` as cancel.

| | |
|---|---|
| Call | `body: ""` (empty string — **not** `null`, or `Body` is omitted and nothing is redacted), `status: null` |
| Returns | `ApiV2010AccountMessage` — SID, status, from, to, dates, error fields remain on the model |
| Do not use | `DeleteMessage` (`DELETE …/Messages/{Sid}.json`, returns `void`) — that deletes the resource |

Post-redact `Body` value (empty vs null vs placeholder) is UNVERIFIED — read `Sid`/`Status`/`ErrorCode` as surviving facts; treat `Body` as unusable after a successful update.

### 9. List messages for reconciliation (provider-side From filter)

| | |
|---|---|
| Method | `ListMessage` |
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages.json` |
| Signature | `Task<TwilioSdk.Models.ListMessageResponse> ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | 8 params `to` … `pageToken` — nullable, no default |
| Query wire | `To` ← `to`, `From` ← `from`, `DateSent` ← `dateSent`, `DateSent<` ← `dateSentQuery`, `DateSent>` ← `dateSentQueryQuery`, `PageSize` ← `pageSize`, `Page` ← `page`, `PageToken` ← `pageToken` |
| Date serialization | `DateTimeOffset.ToIso8601()` → UTC `yyyy-MM-ddTHH:mm:ss.fff'Z'` (`Core/Extensions/DateTimeOffsetExtensions.cs`) |
| Returns | `TwilioSdk.Models.ListMessageResponse` |
| Error | **Case B** `SdkException<RawError>` |
| SDK auto-pagination | **none** (map: “only `page`, no `perPage`”). Loop yourself |
| Map | `operations/Api20100401Message.md` |

**Reconciliation call (ask Twilio for this app’s From number — do not list-all then filter):**

- `from:` `Twilio:FromNumber` (wire `From`) — XML: “Filter by sender”
- `to: null` (unless you also filter by recipient)
- `dateSent: null` (equality; unused for a range)
- `dateSentQueryQuery:` range start → wire `DateSent>` (“after”)
- `dateSentQuery:` range end → wire `DateSent<` (“before”)
- `pageSize:` up to `1000` (XML: default 50, max 1000); `page: null`; `pageToken: null` on the first page

XML on all three date params is copy-pasted (YYYY-MM-DD / `<=` / `>=`, GMT). The **wire names** are the contract: `DateSent` equality, `DateSent<` before, `DateSent>` after. Inclusivity of `<`/`>` vs the docs’ “on and before/after” is UNVERIFIED.

**`ListMessageResponse`** (`records-4-Li-Me.md`): `End (end): int?`, `FirstPageUri (first_page_uri): string?`, `NextPageUri (next_page_uri): string?`, `Page (page): int?`, `PageSize (page_size): int?`, `PreviousPageUri (previous_page_uri): string?`, `Start (start): int?`, `Uri (uri): string?`, `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?`.

**Paging the whole range:** while `NextPageUri` is non-empty, call again with the same filters and `pageToken` set from that URI’s `PageToken` query value (response has **no** `PageToken` property). `page` XML: “simply for client state”. Stop when `NextPageUri` is null/empty. How `PageToken` is spelled on `NextPageUri` is UNVERIFIED — if the query param is absent, stop rather than invent a token.

### `ApiV2010AccountMessage` — fields the integration reads

(`records-1-Ac-Ca.md`, `Models/ApiV2010AccountMessage.cs`; ns `TwilioSdk.Models`)

| C# (wire) | Type | Use |
|---|---|---|
| `Sid (sid)` | `string?` | Provider message id (`SM`/`MM` + 32 hex; length 34) |
| `Status (status)` | `MessageEnumStatus?` | Delivery / schedule outcome |
| `From (from)` | `string?` | Sender |
| `To (to)` | `string?` | Destination |
| `Body (body)` | `string?` | Text (empty after redact — UNVERIFIED exact value) |
| `DateCreated (date_created)` | `string?` | RFC 2822 GMT |
| `DateSent (date_sent)` | `string?` | RFC 2822 GMT (null until sent) |
| `DateUpdated (date_updated)` | `string?` | RFC 2822 GMT |
| `ErrorCode (error_code)` | `int?` | Set when status is `failed` / `undelivered` |
| `ErrorMessage (error_message)` | `string?` | Description of `error_code` |
| `MessagingServiceSid (messaging_service_sid)` | `string?` | `MG` + 32 hex |
| `AccountSid (account_sid)` | `string?` | |
| `Direction (direction)` | `MessageEnumDirection?` | |
| `NumSegments (num_segments)` | `string?` | |
| `NumMedia (num_media)` | `string?` | |
| `Price (price)` | `string?` | |
| `PriceUnit (price_unit)` | `string?` | |
| `Uri (uri)` | `string?` | |
| `ApiVersion (api_version)` | `string?` | |
| `SubresourceUris (subresource_uris)` | `object?` | |

XML on `error_code`/`error_message`: values for a given cause may change; do not branch programmatically on specific codes.

### Enums in scope (`TwilioSdk.Models.Enums`, `map/models/enums.md`)

**`MessageEnumStatus`** (response status):

| Member | Wire |
|---|---|
| `Queued` | `queued` |
| `Sending` | `sending` |
| `Sent` | `sent` |
| `Failed` | `failed` |
| `Delivered` | `delivered` |
| `Undelivered` | `undelivered` |
| `Receiving` | `receiving` |
| `Received` | `received` |
| `Accepted` | `accepted` |
| `Scheduled` | `scheduled` |
| `Read` | `read` |
| `PartiallyDelivered` | `partially_delivered` |
| `Canceled` | `canceled` |

Scheduled not-yet-sent: `Scheduled`. After cancel: `Canceled`. Delivery poll: `Delivered` / `Undelivered` / `Failed` / `Sent` / `Queued` / `Sending` / `Accepted`.

**`MessageEnumUpdateStatus`:** `Canceled ("canceled")` only.

**`MessageEnumScheduleType`:** `Fixed ("fixed")` only.

**`MessageEnumDirection`:** `Inbound ("inbound")`, `OutboundApi ("outbound-api")`, `OutboundCall ("outbound-call")`, `OutboundReply ("outbound-reply")`.

These are `StringEnum<T>` records, **not** C# enums — use the static members (or `FromValue("wire")`). Compare via the member / `.Value`, not a CLR enum cast.

### 10. Errors (every in-scope operation is Case B)

| | |
|---|---|
| Thrown type | `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` |
| Accessors | `ex.Error.StatusCode: System.Net.HttpStatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()` |
| Typed `{Op}Error` / `TryGet…` | **None** on Lookup or Message ops |
| No-throw `…Result` | Absent |

**How to read code / message / more_info:** there is no generated error model on these operations. `RawError` does not parse those fields. Deserialize the body with `ReadAsJson<T>()` into a type with `Code (code): int?`, `Message (message): string?`, `MoreInfo (more_info): string?`, `Status (status): int?` — that shape exists on other 2010-04-01 models (`AccountsCallsRecordingsSidJson201041408Error` in `records-1-Ac-Ca.md`). Whether **live** Message/Lookup error JSON always matches is UNVERIFIED — extract best-effort; if deserialize returns null/throws, fall back to `ReadAsString()` and `StatusCode`. Do not parse `ex.ToString()`.

| Situation | What actually reaches `catch` |
|---|---|
| Invalid / unusable number on **lookup** | `SdkException<RawError>` (e.g. 404) **or** HTTP 2xx with `Valid != true` / `ValidationErrors` — the latter is **not** an exception |
| Invalid destination on **send** | `SdkException<RawError>` — use `StatusCode` + best-effort JSON |
| Unknown message SID (fetch/update) | `SdkException<RawError>` — `StatusCode` (typically 404) |
| Auth failure | `SdkException<RawError>` — `StatusCode` (typically 401/403) |
| Validation / 4xx | `SdkException<RawError>` — `StatusCode` + body |
| Drifted/malformed **2xx** body | `System.Text.Json.JsonException` — **not** `SdkException` |
| Non-2xx body that does not match a typed error object | For these Case B ops the body is stored raw (no typed error ctor). A `JsonException` still replaces `SdkException` if **your** `ReadAsJson<T>` (or a 2xx deserialize) fails — HTTP status is then gone |

`SdkException<TError>` (`Core/Exceptions/SdkException.cs`): `public required TError Error { get; init; }`.

---

## Trap notes

⚠ Step 1 (client registration) — `HttpClient` / handler pipeline lifetime versus the SDK wrapper is not visible from the constructor. **MUST load `dotnet-client-initialization`** before `new TwilioSdkClient` or `AddTwilioSdkClient`.

⚠ Step 1 (auth) — credentials must be on `AccountSidAuthToken` before the client is built; secrets belong in configuration, not source. **MUST load `dotnet-authentication`**.

⚠ Step 1 (BaseUrl / retries / timeouts) — `Retry` / `Timeout` on `TwilioSdkClientOptions` do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; a transport failure can retry verbs the status-code list does not mention, including the `POST` used by `CreateMessage` / `UpdateMessage`. Whether a failed write can be re-sent is decided only after loading the skill. **MUST load `dotnet-configuration-resilience`** before wiring `Server`, `Retry`, or pagination.

⚠ Steps 2–9 (calls) — `CreateMessage` / `ListMessage` / `FetchPhoneNumber3` have long must-pass-null optional lists; a positional call mis-binds. **MUST load `dotnet-calling-endpoints`**.

⚠ Steps 2–9 (models) — statuses and schedule type are `StringEnum<T>` records; unmodeled JSON is dropped; `LookupResponse` / `ApiV2010AccountMessage` are the envelopes (no extra wrapper property). **MUST load `dotnet-models`** before mapping fields or comparing status.

⚠ Step 10 (error boundary) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`**.

⚠ Step 10 (error boundary) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. (In-scope ops are Case B / `RawError`, so this bite is on 2xx payload deserialize and on any `ReadAsJson<T>` you add.) **MUST load `dotnet-error-handling`**.

⚠ Tests — the constructor `HttpClient` argument is the test seam; do not fake SDK internals. **MUST load `dotnet-testing`** before writing integration tests.

---

## REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — construct / DI-register `TwilioSdkClient`, `HttpClient` ownership |
| `dotnet-authentication` | Step 1 — `AccountSidAuthToken` / `BasicAuthCredentials` |
| `dotnet-configuration-resilience` | Step 1 + Step 9 — `Server.Default` BaseUrl, retries/timeouts, manual list pagination |
| `dotnet-calling-endpoints` | Steps 2–9 — named arguments, must-pass nulls, `ct:` |
| `dotnet-models` | Steps 2–9 — `StringEnum<T>`, request/response records, wire names |
| `dotnet-error-handling` | Step 10 — Case B `RawError`, `JsonException` from two directions (rows above) |
| `dotnet-testing` | Tests for the integration layer |

---

## Assumptions & Blockers

**Assumptions**

- Lookup uses **v2** `FetchPhoneNumber3`, not v1 and not `CreateValidationRequest`.
- `Twilio:BaseUrl` maps to `options.Server.Default.Production.BaseUrl` (`https://api.twilio.com` family) and is left unset on `Default4` (Lookup) and `Default1` (`messaging.twilio.com`).
- Path `accountSid` on Message ops is `Twilio:AccountSid`.
- Follow-up scheduling is `CreateMessage` + `MessageEnumScheduleType.Fixed` + `sendAt` + Messaging Service SID (enum: Messaging Services only). If `Twilio:MessagingServiceSid` is empty, scheduling cannot be performed with this SDK contract.
- Redact is `UpdateMessage` with `body: ""`, not `DeleteMessage`.
- Reconciliation passes `from: Twilio:FromNumber` so the provider filters; the app does not download the whole account then filter.

**Blockers / gaps / UNVERIFIED**

1. **Idempotency key on create-message — SDK GAP.** `CreateMessage` has no parameter or header argument for a caller key. `RequestOptions` only has `LogLevel`. Generated code always sends `Idempotency-Key: Guid.NewGuid()`, so the same operator key cannot be reused at the provider through this SDK. Product-layer dedupe (store key → SID locally) is outside the SDK. Behaviour when the same key is reused **at Twilio** is therefore not exposable here.
2. **Schedule min/max window** — not on the map or in `CreateMessage` XML. UNVERIFIED. Treat a provider 4xx on scheduled create as a rejected send time; do not encode a window from memory.
3. **`LineTypeIntelligenceInfo.Type` values** (which strings mean SMS-capable vs landline/voip) — not an SDK enum. UNVERIFIED. Gate on `Valid == true` and a non-blank `PhoneNumber`; do not hard-code type allowlists.
4. **`ListMessage` `DateSent<` / `DateSent>` inclusivity** vs XML “on and before/after”. UNVERIFIED. Pass UTC `DateTimeOffset` bounds; if a boundary row is missing, widen rather than assume inclusive.
5. **Next-page token** — `ListMessageResponse` has `NextPageUri` but no `PageToken` field. UNVERIFIED query-param name. If `PageToken` cannot be parsed from `NextPageUri`, stop paging.
6. **Body after redact** — model still has `Body`; live value UNVERIFIED. Persist SID/status from the update response; do not require a specific placeholder string.
7. **Error JSON** on Message/Lookup — Case B raw body. Mapping onto `code` / `message` / `more_info` is UNVERIFIED vs live traffic. Best-effort `ReadAsJson<T>`; fall back to `ReadAsString()` + `StatusCode`.
