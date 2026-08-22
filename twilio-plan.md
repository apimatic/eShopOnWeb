# Twilio .NET SDK — eShopOnWeb SMS notifications

NuGet: `AsadAli.TwilioSdk` (install version-less). Root namespace: `TwilioSdk` (not `Twilio`). Client: `TwilioSdkClient`. Source stamp: `51fdf48`.

## Scope & sequence

1. **Client, auth, environments, BaseUrl** — construct `TwilioSdkClient` with Account SID + Auth Token; optional messaging-host override on server node `Default` only.
2. **Register shopper number** — `LookupsV2PhoneNumber.FetchPhoneNumber3` (not Incoming Phone Numbers, not MessagingV1, not Lookups v1). Persist the provider E.164 `PhoneNumber` only when `Valid` is true.
3. **Send SMS immediately** (placed / dispatched / cancelled / operator resend) — `Api20100401Message.CreateMessage` with `from` = `Twilio:FromNumber`. Persist `Sid` + `Status` (+ `ErrorCode` / `ErrorMessage` when present).
4. **Queue follow-up SMS with the provider** (dispatch) — same `CreateMessage` with `scheduleType` + `sendAt` + `messagingServiceSid`. Persist the returned `Sid` (status `scheduled`) so cancel can target it.
5. **Cancel a not-yet-sent follow-up** (order cancel) — `Api20100401Message.UpdateMessage` with `status: MessageEnumUpdateStatus.Canceled`.
6. **Read delivery outcome** (no webhooks) — `Api20100401Message.FetchMessage` by provider `Sid`.
7. **Redact / dispose message body at the provider** — `Api20100401Message.UpdateMessage` with a non-null `body` (do **not** `DeleteMessage`; that removes the resource).
8. **Reconciliation listing** — `Api20100401Message.ListMessage` with provider-side `From` = `Twilio:FromNumber` and `DateSent>` / `DateSent<` bounds; page with `pageToken` until `NextPageUri` is null.
9. **Idempotent resend** — app HTTP idempotency key is **not** an SDK parameter. `CreateMessage` always sends its own `Idempotency-Key` header (fresh `Guid` per invocation). `RequestOptions` cannot override headers.
10. **Error boundary** — every in-scope operation is Case B `SdkException<RawError>` (throw-only; no `…Result` variants).

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

### Client construction / auth / server nodes

| Fact | Value | Cite |
|---|---|---|
| Client ctor | `TwilioSdk.TwilioSdkClient(System.Net.Http.HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)` | `sdk-map.md` · `TwilioSdkClient.cs` |
| DI | `TwilioSdk.ServiceCollectionExtensions.AddTwilioSdkClient(this IServiceCollection, Action<TwilioSdkClientOptions>? configure = null)` — registers `TwilioSdkClient` as singleton via `IHttpClientFactory.CreateClient()` | `ServiceCollectionExtensions.cs` |
| Options members | `Environment: TwilioSdk.Servers.ServerEnvironment` · `Retry: TwilioSdk.Core.Configuration.RetryOptions` · `Logging: TwilioSdk.Core.Configuration.LoggingOptions` · `Server: TwilioSdk.ServerOptions` · `AccountSidAuthToken: TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md` · `TwilioSdkClientOptions.cs` |
| Environment | `TwilioSdk.Servers.ServerEnvironment.Production` (`"production"`). Only member. `Default()` → Production. | `sdk-map.md` · `Servers/ServerEnvironment.cs` |
| Auth scheme | `options.AccountSidAuthToken = new BasicAuthCredentials { Username = <Twilio:AccountSid>, Password = <Twilio:AuthToken> }` — both members `required`. Applied as HTTP Basic. Map XML: Account SID + Auth Token is accepted (also API key as username / secret as password). | `sdk-map.md` · `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Messaging API host (send / fetch / list / update / delete Messages) | Server node **`Default`**. Default `BaseUrl` = `https://api.twilio.com`. Operations call `_server.Default("/2010-04-01/Accounts/{AccountSid}/Messages…")`. | `operations/Api20100401Message.md` · `Servers/DefaultOptions.cs` · `Api/Api20100401Message.cs` |
| Lookup host | Server node **`Default4`**. Default `BaseUrl` = `https://lookups.twilio.com`. `FetchPhoneNumber3` calls `_server.Default4("/v2/PhoneNumbers/{PhoneNumber}")`. | `operations/LookupsV2PhoneNumber.md` · `Servers/Default4Options.cs` |
| `Twilio:BaseUrl` override | When set, assign **verbatim** to `options.Server.Default.Production.BaseUrl` (`TwilioSdk.Servers.DefaultOptions.ProductionOptions.BaseUrl`). Do **not** set `Server.Default4` (Lookup stays on `https://lookups.twilio.com`). Do **not** set `Server.Default1` (`https://messaging.twilio.com` — MessagingV1 host; unused by in-scope ops). | `ServerOptions.cs` · `Servers/DefaultOptions.cs` · `Servers/Default4Options.cs` |
| Other `ServerOptions` nodes | `Default1`…`Default14` exist; leave defaults. | `ServerOptions.cs` |
| `RequestOptions` | `TwilioSdk.Core.RequestOptions` — only member `LogLevel: Microsoft.Extensions.Logging.LogLevel?`. No header bag, no idempotency slot, no per-call base URL. | `Core/RequestOptions.cs` |
| Retry options type | `TwilioSdk.Core.Configuration.RetryOptions` — members `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry`. All `required` unless starting from `RetryOptions.Default()`. | `sdk-map.md` |

Config keys → SDK (never hard-code): `Twilio:AccountSid` → path `accountSid` + auth `Username`; `Twilio:AuthToken` → auth `Password`; `Twilio:FromNumber` → `CreateMessage.from` / `ListMessage.from`; `Twilio:MessagingServiceSid` → `CreateMessage.messagingServiceSid` (required for schedule); `Twilio:BaseUrl` → `Server.Default.Production.BaseUrl` when present.

### Operations

#### 1. Phone number validation / canonicalization — Lookup v2 (not Incoming Phone Numbers)

**Why this API.** `LookupsV2PhoneNumber.FetchPhoneNumber3` returns `Valid` plus E.164 `PhoneNumber`. Incoming Phone Numbers (`client.Api20100401IncomingPhoneNumber`) is the account's owned numbers (purchase/list/update) — wrong resource. `NumbersV1EligibilityApi.CreateEligibility` is hosted-number eligibility — wrong resource. Lookups v1 (`FetchPhoneNumber2` → `LookupsV1PhoneNumber`) has no `Valid` / `ValidationErrors` members.

| | |
|---|---|
| Controller | `client.LookupsV2PhoneNumber` · `TwilioSdk.Api.LookupsV2PhoneNumber` |
| Method | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | 15 params `fields` … `partnerSubId` are nullable with **no C# default** — pass `null` to skip. |
| HTTP | `GET /v2/PhoneNumbers/{PhoneNumber}` (Default4 / lookups) |
| Query (wire ← C#) | `Fields` ← `fields`, `CountryCode` ← `countryCode`, … (identity_match / reassigned_number / pre_fill extras unused here) |
| `fields` values (XML) | comma-separated: `validation`, `caller_name`, `sim_swap`, `call_forwarding`, `line_status`, `line_type_intelligence`, `identity_match`, `reassigned_number`, `sms_pumping_risk`, `phone_number_quality_score`, `pre_fill` |
| Returns | `TwilioSdk.Models.LookupResponse` (no wrapper envelope) |
| Error | **Case B** `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` |
| Accessors | `StatusCode: HttpStatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()` |
| Pagination | none |
| Cite | `operations/LookupsV2PhoneNumber.md` · `records-4-Li-Me.md` · `Api/LookupsV2PhoneNumber.cs` |

**`LookupResponse` fields this flow reads** (`TwilioSdk.Models`, `Models/LookupResponse.cs`):

| C# (wire) | Type | Role |
|---|---|---|
| `PhoneNumber (phone_number)` | `string?` | Provider canonical **E.164** (`+` + country code + subscriber). **This is what gets stored.** |
| `Valid (valid)` | `bool?` | XML: boolean whether the number is in a valid range freely assignable by a carrier to a user. Reject registration unless this is `true`. |
| `ValidationErrors (validation_errors)` | `IReadOnlyList<TwilioSdk.Models.Enums.ValidationError>?` | Reasons when invalid. |
| `NationalFormat (national_format)` | `string?` | National format (do not store as canonical). |
| `CountryCode (country_code)` | `string?` | ISO 3166-1 alpha-2. |
| `CallingCountryCode (calling_country_code)` | `string?` | E.164 prefix. |
| `LineTypeIntelligence (line_type_intelligence)` | `TwilioSdk.Models.LineTypeIntelligenceInfo?` | Populated when `fields` includes `line_type_intelligence`. `Type (type): string?` — **no SDK enum of SMS-usable types**. |
| `LineStatus (line_status)` | `TwilioSdk.Models.LineStatusInfo?` | Populated when `fields` includes `line_status`. `Status (status): string?` — untyped. |

Registration call shape: `phoneNumber` = caller-supplied (E.164 or national); `countryCode` = ISO-2 if national; `fields` = `"line_type_intelligence,line_status"` (or `null` if only `Valid`+E.164 are used); all other optionals `null`.

**`ValidationError`** (`TwilioSdk.Models.Enums`, `StringEnum`): `TooShort ("TOO_SHORT")`, `TooLong ("TOO_LONG")`, `InvalidButPossible ("INVALID_BUT_POSSIBLE")`, `InvalidCountryCode ("INVALID_COUNTRY_CODE")`, `InvalidLength ("INVALID_LENGTH")`, `NotANumber ("NOT_A_NUMBER")`. Cite: `map/models/enums.md`.

---

#### 2 / 3. Create (send now or schedule) — `CreateMessage`

| | |
|---|---|
| Controller | `client.Api20100401Message` · `TwilioSdk.Api.Api20100401Message` |
| Method | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | 24 params `statusCallback` … `contentSid` — nullable, **no default**, pass `null` to skip. `accountSid` and `to` are required non-nullable. |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` (Default / api) — **form-urlencoded**, not JSON |
| Body/query (wire ← C#) | `To` ← `to`, `StatusCallback` ← `statusCallback`, `ApplicationSid` ← `applicationSid`, `MaxPrice` ← `maxPrice`, `ProvideFeedback` ← `provideFeedback`, `Attempt` ← `attempt`, `ValidityPeriod` ← `validityPeriod`, `ForceDelivery` ← `forceDelivery`, `ContentRetention` ← `contentRetention`, `AddressRetention` ← `addressRetention`, `SmartEncoded` ← `smartEncoded`, `PersistentAction` ← `persistentAction`, `TrafficType` ← `trafficType`, `ShortenUrls` ← `shortenUrls`, `ScheduleType` ← `scheduleType`, `SendAt` ← `sendAt`, `SendAsMms` ← `sendAsMms`, `ContentVariables` ← `contentVariables`, `RiskCheck` ← `riskCheck`, `From` ← `from`, `FallbackFrom` ← `fallbackFrom`, `MessagingServiceSid` ← `messagingServiceSid`, `Body` ← `body`, `MediaUrl` ← `mediaUrl`, `ContentSid` ← `contentSid` |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` (no wrapper) |
| Error | **Case B** `SdkException<RawError>` — same accessors as above |
| Pagination | none |
| Idempotency | **No method parameter.** Implementation always adds header `Idempotency-Key` = `Guid.NewGuid()` at the call site. `RequestOptions` cannot replace it. A second `CreateMessage` invocation always gets a new key. |
| Null form fields | `ParameterFlattener`: `null` values are **omitted** (not sent as empty). |
| Cite | `operations/Api20100401Message.md` · `Api/Api20100401Message.cs` |

**From vs Messaging Service SID**

- Immediate send (flows 2, 9): pass `from: Twilio:FromNumber`, `messagingServiceSid: null` (unless product later requires the service to pick the sender). `to` = stored E.164. `body` = SMS text. `statusCallback: null` (no webhooks). `scheduleType: null`, `sendAt: null`.
- Scheduled follow-up (flow 3): `MessageEnumScheduleType` XML: **“For Messaging Services only”** — pass `messagingServiceSid: Twilio:MessagingServiceSid` (non-null), `scheduleType: MessageEnumScheduleType.Fixed` (wire `"fixed"`), `sendAt: DateTimeOffset` for the send instant. Enum docs mention `send_time`; the operation row/wire name is **`SendAt` / `sendAt`** (authoritative). `from` may be `null` when the Messaging Service supplies the sender; if the product still wants a specific From, the SDK will send both form fields — provider mutual exclusivity is **UNVERIFIED**.
- `SendAt` encoding: `CreateMessage` passes `DateTimeOffset?` into `Param` without `ToIso8601()`. Flattening JSON-serializes the value then form-encodes the string. Pass a UTC `DateTimeOffset`. SDK source does **not** document min/max schedule window.

**Persist from the 2xx body** (`ApiV2010AccountMessage` — `records-1-Ac-Ca.md` · `Models/ApiV2010AccountMessage.cs`):

| C# (wire) | Type | Persist? |
|---|---|---|
| `Sid (sid)` | `string?` (pattern `^(SM\|MM)[0-9a-fA-F]{32}$`) | **Yes** — provider message id for fetch / cancel / redact |
| `Status (status)` | `MessageEnumStatus?` | **Yes** — current outcome |
| `To (to)` / `From (from)` | `string?` | Yes if the app stores them |
| `Body (body)` | `string?` | App choice; provider copy is later redacted via Update |
| `DateCreated (date_created)` / `DateSent (date_sent)` / `DateUpdated (date_updated)` | `string?` (RFC 2822 GMT in XML) | Yes for reporting |
| `ErrorCode (error_code)` | `int?` | Yes when status is `failed` / `undelivered` |
| `ErrorMessage (error_message)` | `string?` | Same; XML: do not treat code/message as a stable programmatic contract |
| `MessagingServiceSid (messaging_service_sid)` | `string?` | Yes for scheduled rows |
| `AccountSid (account_sid)` | `string?` | — |
| `Direction (direction)` | `MessageEnumDirection?` | — |
| `NumSegments (num_segments)` / `NumMedia (num_media)` / `Price (price)` / `PriceUnit (price_unit)` / `Uri (uri)` / `ApiVersion (api_version)` / `SubresourceUris (subresource_uris)` | see model | not required for later cancel/fetch |

**Immediate vs scheduled status on accept:** 2xx with `Status` `queued` / `accepted` / `sending` / `sent` / `scheduled` means **accepted**. Later `undelivered` / `failed` on Fetch is **(a) accepted then undeliverable** (expected for `TWILIO_UNREACHABLE_TO_NUMBER`). Throw on `CreateMessage` is **(b) rejected immediately**.

---

#### 4. Cancel scheduled follow-up — `UpdateMessage` (`status`)

| | |
|---|---|
| Method | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | `body` and `status` nullable, no default — for cancel pass `body: null`, `status: MessageEnumUpdateStatus.Canceled` |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` (Default / api) form-urlencoded |
| Wire | `Body` ← `body`, `Status` ← `status` |
| Returns | `ApiV2010AccountMessage` |
| Error | **Case B** `SdkException<RawError>` |
| Notes (map/XML) | “used to redact Message `body` text and to cancel not-yet-sent messages” |
| Identify the scheduled message | The `Sid` returned by the scheduling `CreateMessage` (persist it). Success: returned `Status` member `Canceled` (`"canceled"`). |
| Already sent | SDK does **not** document the outcome. Expect Case B `RawError` **or** a 2xx whose `Status` is not `Canceled` — treat either as “too late”; then `FetchMessage` is the current truth. **UNVERIFIED** HTTP status for already-sent. |
| Cite | `operations/Api20100401Message.md` |

`MessageEnumUpdateStatus` (`TwilioSdk.Models.Enums`): only member `Canceled ("canceled")`. Cite: `map/models/enums.md`.

Also sends header `Idempotency-Key: Guid.NewGuid()` (same as Create).

---

#### 5. Read delivery outcome — `FetchMessage`

| | |
|---|---|
| Method | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` |
| Returns | `ApiV2010AccountMessage` (same fields as create) |
| Error | **Case B** `SdkException<RawError>` (unknown SID is this path — **UNVERIFIED** status code; read `StatusCode` + `ReadAsString()`) |
| Cite | `operations/Api20100401Message.md` |

No list-after-send is required to refresh a known id; `FetchMessage` is the status read. List is for reconciliation (op 7).

---

#### 6. Redact body at provider — `UpdateMessage` (`body`), not `DeleteMessage`

| | |
|---|---|
| Method | same `UpdateMessage` as cancel |
| Redact call | `body: ""` (empty string), `status: null`. **`body: null` omits the `Body` field** (flattener drops nulls) and will not redact. Empty string is the non-null value that actually transmits `Body`. |
| Returns | `ApiV2010AccountMessage` — read remaining `Body`, `Sid`, `Status`, `ErrorCode`, `ErrorMessage`, dates. Post-redact `Body` empty-vs-null is **UNVERIFIED**; persist whatever the 2xx record contains. |
| Do **not** use | `DeleteMessage(string accountSid, string sid, …)` → `DELETE …/Messages/{Sid}.json` → `void`. Map: “Deletes a Message resource from your account” — that disposes the resource, not only the text. |
| Cite | `operations/Api20100401Message.md` · `Api/Api20100401Message.cs` · `Core/ParameterFlattener.cs` |

---

#### 7. Reconciliation listing — `ListMessage` (provider-side `From` + date bounds)

| | |
|---|---|
| Method | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | 8 params `to` … `pageToken` — pass `null` to skip |
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages.json` |
| Query (wire ← C#) | `To` ← `to`, **`From` ← `from`**, `DateSent` ← `dateSent`, **`DateSent<` ← `dateSentQuery`**, **`DateSent>` ← `dateSentQueryQuery`**, `PageSize` ← `pageSize`, `Page` ← `page`, `PageToken` ← `pageToken` |
| Date encoding | SDK sends `dateSent` / `dateSentQuery` / `dateSentQueryQuery` via `ToIso8601()` = UTC `yyyy-MM-ddTHH:mm:ss.fff'Z'`. XML also describes GMT `YYYY-MM-DD` / `<=` / `>=`; the **wired names are `DateSent`, `DateSent<`, `DateSent>`**. Inclusive vs exclusive of the bound is **UNVERIFIED**. |
| Returns | `TwilioSdk.Models.ListMessageResponse` |
| Error | **Case B** `SdkException<RawError>` |
| Pagination (map) | none (only `page`, no `perPage`) — **not** an auto-pager |
| `pageSize` XML | default 50, maximum 1000 (`long?`) |
| `page` XML | “page index… simply for client state” |
| `pageToken` XML | “provided by the API” |
| Cite | `operations/Api20100401Message.md` · `records-4-Li-Me.md` · `Api/Api20100401Message.cs` |

**This app’s From (do not fetch-all-then-filter):** pass `from: Twilio:FromNumber` (wire `From`). Pass `to: null`. Range: `dateSentQueryQuery` = HTTP `from` (lower bound → `DateSent>`), `dateSentQuery` = HTTP `to` (upper bound → `DateSent<`), `dateSent: null`.

**`ListMessageResponse` envelope** (`Models/ListMessageResponse.cs`):

| C# (wire) | Type |
|---|---|
| `Messages (messages)` | `IReadOnlyList<ApiV2010AccountMessage>?` — page payload |
| `NextPageUri (next_page_uri)` | `string?` — continue while non-null |
| `PageToken` | not a response field; take the `PageToken` query value out of `next_page_uri` and pass as `pageToken` on the next `ListMessage` **with the same `from` / date bounds** |
| `FirstPageUri` / `PreviousPageUri` / `Uri` / `Page` / `PageSize` / `Start` / `End` | paging metadata |

---

#### 8. `DeleteMessage` (out of product scope except as a negative)

`DeleteMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `void`, Case B. Do not use for flow 6.

---

### Enums in scope (`TwilioSdk.Models.Enums` · `map/models/enums.md`)

Build with static members (or `FromValue("wire")`). These are `StringEnum<T>`, not C# enums.

| Type | Members (C# = wire) |
|---|---|
| `MessageEnumStatus` | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` |
| `MessageEnumDirection` | `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)` |
| `MessageEnumScheduleType` | `Fixed (fixed)` only |
| `MessageEnumUpdateStatus` | `Canceled (canceled)` only |
| `MessageEnumContentRetention` | `Retain (retain)`, `Discard (discard)` — unused unless product sets retention at send |
| `MessageEnumAddressRetention` | `Retain (retain)`, `Obfuscate (obfuscate)` |
| `MessageEnumTrafficType` | `Free (free)` |
| `MessageEnumRiskCheck` | `Enable (enable)`, `Disable (disable)` |
| `ValidationError` | see Lookup section |

Namespaces: enums live in `TwilioSdk.Models.Enums`; records in `TwilioSdk.Models`; controllers in `TwilioSdk.Api`; `SdkException<>` in `TwilioSdk.Core.Exceptions`; `RawError` in `TwilioSdk.Core.ErrorResponse`; `BasicAuthCredentials` in `TwilioSdk.Core.Authentication.Basic`; `ServerEnvironment` / `DefaultOptions` / `Default4Options` in `TwilioSdk.Servers`; `ServerOptions` / `TwilioSdkClient` / `TwilioSdkClientOptions` in `TwilioSdk`; `RetryOptions` / `LoggingOptions` in `TwilioSdk.Core.Configuration`; `RequestOptions` in `TwilioSdk.Core`.

### Error matrix (all in-scope ops = Case B)

There is **no** typed `{Operation}Error` and **no** `TryGet…` on these calls. Catch `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`. `ex.Error.StatusCode` is the HTTP status; `ReadAsString()` / `ReadAsJson<T>()` is the body. The SDK has **no** generated RestException model for these operations.

| Situation | What the SDK throws / returns |
|---|---|
| Invalid number at **registration** | Lookup 2xx with `Valid != true` + `ValidationErrors` — **not** an exception. Malformed/unknown that the host rejects → Case B `RawError`. **UNVERIFIED** which invalids are 2xx vs 4xx. |
| Invalid number at **send** | `CreateMessage` Case B (immediate reject). Distinct from 2xx then later `undelivered`. |
| Cancel too late (already sent) | Case B **or** 2xx with non-`Canceled` status. **UNVERIFIED** status code. Follow with `FetchMessage`. |
| Unknown message SID | `FetchMessage` / `UpdateMessage` / `DeleteMessage` Case B. **UNVERIFIED** status code (read `StatusCode`). |
| Wrong credentials | Case B on **every** operation (same exception type). **UNVERIFIED** numeric status; inspect `StatusCode` (do not assume a distinct CLR type). |
| Send accepted, later undeliverable | **Not an exception.** `CreateMessage` 2xx; later `FetchMessage` `Status` = `Undelivered` or `Failed`, optional `ErrorCode` / `ErrorMessage`. Expected for reserved-US unreachable destinations on this account. |
| `JsonException` | Can surface **instead of** `SdkException` — see REQUIRED READING. |

No-throw `…Result` variants: **absent** (`sdk-map.md`).

---

## Trap notes

⚠ Step 1 (client registration) — the constructor taking `HttpClient` does not say who owns the handler pipeline or how `AddTwilioSdkClient` should sit in DI. **MUST load `dotnet-client-initialization`** before constructing or registering the client.

⚠ Step 1 (auth) — `AccountSidAuthToken` / `BasicAuthCredentials` members do not say when credentials must be set or how to bind them from configuration. **MUST load `dotnet-authentication`** before wiring secrets.

⚠ Step 1 (BaseUrl / retries / timeouts) — `RetryOptions` and `Server.Default.Production.BaseUrl` do **not** document what a timeout bounds, which failures retry, or whether a retried `CreateMessage` (POST) can execute more than once. **MUST load `dotnet-configuration-resilience`** before setting `Retry` or the messaging `BaseUrl`.

⚠ Steps 2–8 (calls) — `CreateMessage`, `ListMessage`, and `FetchPhoneNumber3` have long nullable parameter lists with **no C# defaults**; a positional call will bind the wrong arguments. **MUST load `dotnet-calling-endpoints`** before the first SDK call.

⚠ Steps 2–8 (models) — `MessageEnum*` and `ValidationError` are `StringEnum<T>` (static members / `FromValue`), not C# enums; request/response records drop unmodeled JSON. **MUST load `dotnet-models`** before constructing enums or mapping `LookupResponse` / `ApiV2010AccountMessage`.

⚠ Steps 2–10 (errors) — every in-scope operation is Case B `SdkException<RawError>` (no typed `TryGet…`); a Case-A-shaped catch will never match, and `JsonException` can reach the boundary from 2xx deserialize **or** replace `SdkException` on a non-2xx body. **MUST load `dotnet-error-handling`** before writing any try/catch.

⚠ Step 7 (reconciliation paging) — `ListMessage` is not an SDK auto-pager; `NextPageUri` is not a follow-link helper. **MUST load `dotnet-configuration-resilience`** before paging the whole date range.

⚠ Tests — the `HttpClient` constructor argument is the test seam. **MUST load `dotnet-testing`** before stubbing.

---

## REQUIRED READING

Load **before implementation starts**. This sheet does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — `TwilioSdkClient` construction, `HttpClient` ownership, `AddTwilioSdkClient` |
| `dotnet-authentication` | Step 1 — `AccountSidAuthToken` / `BasicAuthCredentials` from config |
| `dotnet-calling-endpoints` | Steps 2–8 — named arguments, must-pass nulls, `ct:` |
| `dotnet-models` | Steps 2–8 — `StringEnum<T>`, records, wire names |
| `dotnet-error-handling` | Steps 2–10 — Case B `RawError`, catch ladder, JsonException |
| `dotnet-configuration-resilience` | Step 1 retries/timeouts/BaseUrl; Step 7 pagination |
| `dotnet-testing` | Tests against the `HttpClient` seam |

**`System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:**

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## Assumptions & Blockers

- **Assumption:** Live destinations are only `TWILIO_TEST_TO_NUMBER` (CA, reachable) and `TWILIO_UNREACHABLE_TO_NUMBER` (reserved US). A 2xx create followed by `undelivered`/`failed` on the US number is an outcome, not an SDK gap.
- **Assumption:** No webhooks — `statusCallback` is always `null`; status is `FetchMessage` only.
- **Assumption:** `Twilio:MessagingServiceSid` is populated whenever a follow-up is scheduled. `MessageEnumScheduleType` is documented as Messaging Services only; the SDK has no other schedule operation.
- **UNVERIFIED (schedule window):** Map and `CreateMessage` XML do not encode min/max `SendAt` offset. Do not invent a client-side window. An out-of-window schedule is a Case B `RawError` on `CreateMessage`.
- **UNVERIFIED (From + MessagingServiceSid together):** SDK sends both form fields if both are non-null. Immediate send should pass `from` and `messagingServiceSid: null` unless scheduling.
- **UNVERIFIED (line type as SMS-usable):** `LineTypeIntelligenceInfo.Type` is `string?` with no enum. The documented registration gate is `Valid == true` plus storing `PhoneNumber`. Treating landline/voip strings as reject reasons is app policy, not an SDK contract.
- **UNVERIFIED (HTTP statuses):** unknown SID, bad credentials, cancel-too-late, lookup-of-garbage — Case B `StatusCode` + body; the SDK does not map them to distinct exception types.
- **UNVERIFIED (redact remainder):** empty-string `Body` is what the SDK will transmit; whether the provider returns `Body` as `""` vs `null` after success is not in the model XML.
- **UNVERIFIED (`DateSent>` / `DateSent<` inclusivity):** inequalities are the wire names; inclusive/exclusive is not in the map.
- **Blocker (none for the listed flows):** Lookup v2, create/schedule, cancel, fetch, body update, and From-filtered list are all on the map. Incoming Phone Numbers / Eligibility / Lookups v1 / `DeleteMessage` are explicitly the wrong tools, not missing ones.
- **Not a blocker — SDK idempotency:** `CreateMessage` has no caller-supplied idempotency parameter. Operator resend idempotency is the app HTTP API’s concern; the SDK will not dedupe two `CreateMessage` calls.
- **Not a blocker — root namespace:** the product brief said `Twilio`; the generated SDK root is `TwilioSdk`.
