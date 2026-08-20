# Twilio .NET SDK — eShopOnWeb SMS notifications

NuGet: `AsadAli.TwilioSdk` (install version-less: `dotnet add package AsadAli.TwilioSdk`). Root namespace: `TwilioSdk` (not `Twilio`). Client: `TwilioSdk.TwilioSdkClient`. Map provenance: `sdk-map.md` source commit `51fdf48`.

---

## Scope & sequence

1. **Client, auth, messaging BaseUrl** — construct `TwilioSdkClient` with `AccountSid`+`AuthToken`; override **only** the messaging (`Default` / api) host from `Twilio:BaseUrl`. Lookups stay on their default host.
2. **Register mobile (`POST /api/contact-numbers`)** — `LookupsV2PhoneNumber.FetchPhoneNumber3`. Store `LookupResponse.PhoneNumber` (E.164). Reject when `Valid` is not true, when `ValidationErrors` is non-empty, or when the call throws.
3. **Send SMS immediately** (place/dispatch/cancel/resend) — `Api20100401Message.CreateMessage` with `from: Twilio:FromNumber` (never omit; reconciliation lists by this number). Persist `Sid` + `Status` (+ `To`/`From`/`DateCreated`/`DateSent`/`ErrorCode`/`ErrorMessage` as needed for later cancel/redact/fetch/resend).
4. **Schedule follow-up SMS at the provider** (dispatch) — same `CreateMessage` with `scheduleType: MessageEnumScheduleType.Fixed`, `sendAt`, and `messagingServiceSid: Twilio:MessagingServiceSid` (enum documents scheduling as Messaging Services only). Still pass `from: Twilio:FromNumber`. Persist `Sid`; expect `Status` `scheduled`.
5. **Cancel a not-yet-sent follow-up** (order cancel) — `Api20100401Message.UpdateMessage` with `status: MessageEnumUpdateStatus.Canceled`.
6. **Poll delivery outcome** — `Api20100401Message.FetchMessage` by persisted `Sid`.
7. **Reconciliation list** — `Api20100401Message.ListMessage` with request `from: Twilio:FromNumber` and the date-range query params (not a client-side filter). Page via `pageSize` / `page` / `pageToken`.
8. **Redact body at the provider** — `Api20100401Message.UpdateMessage` with `body: ""` (empty string). Do **not** use `DeleteMessage` (that deletes the resource).
9. **Resend idempotency** — persist the caller key locally. The SDK does not accept a caller-supplied idempotency key on `CreateMessage`.
10. **Error boundary** — every in-scope operation is Case B `SdkException<RawError>` (throw-only; no `…Result` variant).

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

### Operations

| Step | Controller (`client.X`) | Method (params in order) | HTTP | Request (C# `(wire)`: type, required?) | Response envelope | Error | Pagination | Map |
|---|---|---|---|---|---|---|---|---|
| 2 lookup | `TwilioSdk.Api.LookupsV2PhoneNumber` (`client.LookupsV2PhoneNumber`) | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` — **15** params `fields`…`partnerSubId` are nullable with **no C# default → must pass explicitly** (`null` to skip) | `GET /v2/PhoneNumbers/{PhoneNumber}` server **Default4 (lookups)** | Path: `phoneNumber`. Query: `fields (Fields)`, `countryCode (CountryCode)`, `firstName (FirstName)`, `lastName (LastName)`, `addressLine1 (AddressLine1)`, `addressLine2 (AddressLine2)`, `city (City)`, `state (State)`, `postalCode (PostalCode)`, `addressCountryCode (AddressCountryCode)`, `nationalId (NationalId)`, `dateOfBirth (DateOfBirth)`, `lastVerifiedDate (LastVerifiedDate)`, `verificationSid (VerificationSid)`, `partnerSubId (PartnerSubId)` — all `string?` except path `phoneNumber` (`string`, required). XML: `phoneNumber` is E.164 or national (default country +1); `fields` is a **comma-separated** list: `validation`, `caller_name`, `sim_swap`, `call_forwarding`, `line_status`, `line_type_intelligence`, `identity_match`, `reassigned_number`, `sms_pumping_risk`, `phone_number_quality_score`, `pre_fill`. For this app pass `fields: "line_type_intelligence"` (add `line_status` if you also persist line status). Pass `countryCode` when the input is national format. All other extras: `null`. | **Direct** `TwilioSdk.Models.LookupResponse` (no wrapper). Canonical number: `PhoneNumber (phone_number): string?` — E.164 (`+` + country + subscriber). Also read: `Valid (valid): bool?` — “in a valid range that can be freely assigned by a carrier”; `ValidationErrors (validation_errors): IReadOnlyList<TwilioSdk.Models.Enums.ValidationError>?`; `CountryCode (country_code): string?` (ISO 3166-1 alpha-2); `CallingCountryCode (calling_country_code): string?`; `NationalFormat (national_format): string?`; `LineTypeIntelligence (line_type_intelligence): LineTypeIntelligenceInfo?` — members `MobileCountryCode (mobile_country_code): string?`, `MobileNetworkCode (mobile_network_code): string?`, `CarrierName (carrier_name): string?`, `Type (type): string?` (**not** an enum on this model), `ErrorCode (error_code): int?`; `LineStatus (line_status): LineStatusInfo?` — `Status (status): string?`, `ErrorCode (error_code): int?`. | **Case B** `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`. Accessors: `ex.Error.StatusCode: HttpStatusCode` · `ReadAsString()` · `ReadAsJson<T>()` · `ReadAsBytes()`. No typed `{Op}Error`, no `TryGet…` status accessors. Invalid/not-found is **not** a distinct generated type: either a thrown Case B (read `StatusCode` + body) **or** HTTP 2xx with `Valid != true` / non-empty `ValidationErrors`. | none | `map/operations/LookupsV2PhoneNumber.md`, `map/models/records-4-Li-Me.md` (`LookupResponse`), `map/models/records-3-Fl-Li.md` (`LineTypeIntelligenceInfo`, `LineStatusInfo`) |
| 3 send / 4 schedule | `TwilioSdk.Api.Api20100401Message` (`client.Api20100401Message`) | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, TwilioSdk.Models.Enums.MessageEnumContentRetention? contentRetention, TwilioSdk.Models.Enums.MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, TwilioSdk.Models.Enums.MessageEnumTrafficType? trafficType, bool? shortenUrls, TwilioSdk.Models.Enums.MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, TwilioSdk.Models.Enums.MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` — **24** params `statusCallback`…`contentSid` nullable, **no default → must pass explicitly** | `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` server **Default (api)**. Body: `application/x-www-form-urlencoded` (not JSON). | Path: `accountSid` (`string`, required) = `Twilio:AccountSid`. Form: `to (To): string` **required**; `from (From): string?`; `messagingServiceSid (MessagingServiceSid): string?`; `body (Body): string?`; `scheduleType (ScheduleType): MessageEnumScheduleType?`; `sendAt (SendAt): DateTimeOffset?`; plus the other optionals listed in the signature (`statusCallback (StatusCallback)`, `applicationSid (ApplicationSid)`, `maxPrice (MaxPrice)`, `provideFeedback (ProvideFeedback)`, `attempt (Attempt)`, `validityPeriod (ValidityPeriod)`, `forceDelivery (ForceDelivery)`, `contentRetention (ContentRetention)`, `addressRetention (AddressRetention)`, `smartEncoded (SmartEncoded)`, `persistentAction (PersistentAction)`, `trafficType (TrafficType)`, `shortenUrls (ShortenUrls)`, `sendAsMms (SendAsMms)`, `contentVariables (ContentVariables)`, `riskCheck (RiskCheck)`, `fallbackFrom (FallbackFrom)`, `mediaUrl (MediaUrl)`, `contentSid (ContentSid)`). **Null form fields are omitted** (not sent). **From vs Messaging Service:** both `from` and `messagingServiceSid` are optional SDK params; **both may be supplied** (both go on the form when non-null). This app **must** pass `from: Twilio:FromNumber` on every send so list/reconciliation can query `From`. Immediate send: `scheduleType: null`, `sendAt: null`, `messagingServiceSid: null` (FromNumber is the sender). Scheduled send: `scheduleType: MessageEnumScheduleType.Fixed`, `sendAt: <UTC DateTimeOffset>`, `messagingServiceSid: Twilio:MessagingServiceSid` (enum XML: scheduling is **Messaging Services only**, value `fixed`, “in conjunction with the `send_time` parameter” — the actual form field the SDK sends is **`SendAt`**, not `send_time`). SDK encodes no min/max send-at window. | **Direct** `TwilioSdk.Models.ApiV2010AccountMessage` (no wrapper). Persist at least: `Sid (sid): string?` (Twilio message id; pattern `^(SM\|MM)[0-9a-fA-F]{32}$`); `Status (status): MessageEnumStatus?`; `From (from): string?`; `To (to): string?`; `Body (body): string?`; `DateCreated (date_created): string?` (RFC 2822 GMT); `DateSent (date_sent): string?` (RFC 2822 GMT; outgoing = when Twilio sent); `DateUpdated (date_updated): string?`; `ErrorCode (error_code): int?`; `ErrorMessage (error_message): string?`; `MessagingServiceSid (messaging_service_sid): string?`; `Direction (direction): MessageEnumDirection?`; `AccountSid (account_sid): string?`; `NumSegments (num_segments): string?`; `NumMedia (num_media): string?`; `Price (price): string?`; `PriceUnit (price_unit): string?`; `Uri (uri): string?`; `ApiVersion (api_version): string?`; `SubresourceUris (subresource_uris): object?`. Immediate create: persist `Sid` for later fetch/cancel/redact/resend. Scheduled create: same `Sid`; `Status` member `Scheduled` (`scheduled`) is the not-yet-sent value. | **Case B** `SdkException<RawError>` — same accessors as lookup. Send failures (invalid `to`, etc.) are **not** typed: catch `SdkException<RawError>` and read `StatusCode` + `ReadAsString()` / `ReadAsJson<T>()`. No `{CreateMessage}Error`. | none | `map/operations/Api20100401Message.md` (`CreateMessage`), `map/models/records-1-Ac-Ca.md` (`ApiV2010AccountMessage`) |
| 5 cancel scheduled | `client.Api20100401Message` | `UpdateMessage(string accountSid, string sid, string? body, TwilioSdk.Models.Enums.MessageEnumUpdateStatus? status, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` — `body` and `status` nullable, **must pass explicitly** | `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` server **Default (api)**. Form-urlencoded. Notes: “Update a Message resource (used to redact Message `body` text and to cancel not-yet-sent messages)”. | Path: `accountSid`, `sid` (persisted provider SID). Form: `body (Body): string?`; `status (Status): MessageEnumUpdateStatus?`. **Cancel:** `status: MessageEnumUpdateStatus.Canceled` (wire `canceled`), `body: null` (omitted). Already-sent / already-cancelled / not-found are **not** generated error shapes — Case B only (`StatusCode` + raw body). | **Direct** `ApiV2010AccountMessage`. Read `Sid`, `Status` (expect member `Canceled` on success — live value **UNVERIFIED** if the provider disagrees; re-fetch if needed). | **Case B** `SdkException<RawError>` | none | `map/operations/Api20100401Message.md` (`UpdateMessage`) |
| 6 fetch status | `client.Api20100401Message` | `FetchMessage(string accountSid, string sid, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` | `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` server **Default (api)** | Path: `accountSid`, `sid`. No query/body. | **Direct** `ApiV2010AccountMessage`. Fields to read for polling: `Sid`, `Status`, `To`, `From`, `Body`, `DateSent`, `DateCreated`, `DateUpdated`, `ErrorCode`, `ErrorMessage`, `Direction`, `MessagingServiceSid`, `AccountSid`, `NumSegments`, `NumMedia`, `Price`, `PriceUnit`, `Uri`, `ApiVersion`. `DateSent`/`DateCreated`/`DateUpdated` are **`string?` RFC 2822 GMT**, not `DateTimeOffset`. | **Case B** `SdkException<RawError>` (not found included) | none | `map/operations/Api20100401Message.md` (`FetchMessage`) |
| 7 reconcile list | `client.Api20100401Message` | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` — **8** params `to`…`pageToken` nullable, **must pass explicitly** | `GET /2010-04-01/Accounts/{AccountSid}/Messages.json` server **Default (api)** | Path: `accountSid`. Query (**provider-side filters**): `to (To): string?` recipient; `from (From): string?` **sender** — pass `Twilio:FromNumber` so the provider returns only this app’s line (do not list the whole account then filter); `dateSent (DateSent): DateTimeOffset?` exact sent instant; `dateSentQuery (DateSent<): DateTimeOffset?` upper bound; `dateSentQueryQuery (DateSent>): DateTimeOffset?` lower bound; `pageSize (PageSize): long?` (XML: default 50, max 1000); `page (Page): int?` (“client state”); `pageToken (PageToken): string?` (“provided by the API”). SDK sends the three date params as **UTC ISO-8601** `yyyy-MM-ddTHH:mm:ss.fff'Z'` (XML text still describes GMT `YYYY-MM-DD` / `<=` / `>=`). For a range `[from,to]` pass `dateSentQueryQuery: rangeStart`, `dateSentQuery: rangeEnd`, `dateSent: null`, `from: Twilio:FromNumber`. Other available filters: `to`. | Envelope `TwilioSdk.Models.ListMessageResponse`: `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?` plus page metadata `End (end): int?`, `Start (start): int?`, `Page (page): int?`, `PageSize (page_size): int?`, `FirstPageUri (first_page_uri): string?`, `NextPageUri (next_page_uri): string?`, `PreviousPageUri (previous_page_uri): string?`, `Uri (uri): string?`. Item shape = `ApiV2010AccountMessage` (same fields as fetch). | **Case B** `SdkException<RawError>` | Map: **none** (only `page` / `pageToken` / `pageSize` params; no `perPage`; no auto-iterator). Next page: pass `pageToken` from the API (response carries `NextPageUri`, not a `page_token` field). | `map/operations/Api20100401Message.md` (`ListMessage`), `map/models/records-4-Li-Me.md` (`ListMessageResponse`) |
| 8 redact body | `client.Api20100401Message` | same `UpdateMessage` as cancel | same POST update | **Redact:** `body: ""` (**empty string**, not `null` — `null` is omitted from the form and would not redact). `status: null`. Success returns `ApiV2010AccountMessage`; `Body` is `string?`. Whether the provider returns `""` vs `null` after redact is **UNVERIFIED** — treat null or empty as redacted; `FetchMessage` to confirm the text is gone. `DeleteMessage` is a different op (`DELETE` … `/Messages/{Sid}.json`, returns `void`) and **removes the resource**, which does not match “the fact of the send survives”. | **Direct** `ApiV2010AccountMessage` | **Case B** `SdkException<RawError>` | none | `map/operations/Api20100401Message.md` (`UpdateMessage`; `DeleteMessage` out of scope for redact) |

**Lookups v1 (not the primary op):** `client.LookupsV1PhoneNumberApi.FetchPhoneNumber2` → `LookupsV1PhoneNumber` with `PhoneNumber (phone_number): string?` and untyped `Carrier (carrier): object?`. Prefer v2 (`FetchPhoneNumber3`) because `Valid`, `ValidationErrors`, and `LineTypeIntelligence` are modeled. Map: `map/operations/LookupsV1PhoneNumberApi.md`.

### Usable mobile destination (step 2)

| Check | Where | Rule grounded in the map/source |
|---|---|---|
| Canonical form to **store** | `LookupResponse.PhoneNumber` | XML: E.164 (`+` + country + subscriber). `records-4-Li-Me.md` / `Models/LookupResponse.cs` |
| Usable assigned range | `LookupResponse.Valid` | XML: `true` iff the number is in a valid range a carrier can freely assign. Reject when `Valid` is not `true` (including `null`). |
| Invalid reasons | `LookupResponse.ValidationErrors` | `ValidationError` members below. Reject when the list is non-empty. |
| Line classification | `LineTypeIntelligence.Type` | **`string?`, no enum on this model.** Request it via `fields` containing `line_type_intelligence`. A separate enum `TwilioSdk.Models.Enums.LineType` exists (`map/models/enums.md`) with wire values `mobile`, `landline`, `tollFree`, `fixedVoip`, `nonFixedVoip`, `personal`, `premium`, `voicemail`, `sharedCost`, `uan`, `pager`, `unknown` — but that enum’s XML is “the new line type to **override** the original line type”, **not** the lookup `Type` field. That `Type` uses the same vocabulary is **UNVERIFIED**. Defensive: persist `Type`; if you must reject non-mobiles, compare case-sensitively to `"mobile"` as a best-effort heuristic and still require `Valid == true`. |
| HTTP-layer not found / invalid | Case B | No typed 404. Read `ex.Error.StatusCode` and `ReadAsString()`. |

### Idempotency (step 9 / operator resend)

`CreateMessage` has **no** `idempotencyKey` parameter. `TwilioSdk.Core.RequestOptions` has **only** `LogLevel? LogLevel` — it cannot add headers.

The generated `CreateMessage` / `UpdateMessage` / `DeleteMessage` **always** send HTTP header **`Idempotency-Key`** with **`Guid.NewGuid()`** (a new value on every method invocation). The caller cannot supply or reuse a key. Repeating `POST /api/notifications/{id}/resend` under the same application key **will** send a different `Idempotency-Key` and can create a second message.

**Persist the caller-supplied idempotency key locally** and short-circuit a second `CreateMessage`. (`FetchMessage` / `ListMessage` do not send this header.)

### Enums in scope (`TwilioSdk.Models.Enums`, `map/models/enums.md`)

Enums are `StringEnum<T>` (not C# enums): use static members or `T.FromValue("wire")`.

| Type | Members (C# `(wire)`) |
|---|---|
| `MessageEnumStatus` | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` |
| `MessageEnumUpdateStatus` | `Canceled (canceled)` — only legal update-status value |
| `MessageEnumScheduleType` | `Fixed (fixed)` — Messaging Services only |
| `MessageEnumDirection` | `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)` |
| `MessageEnumContentRetention` | `Retain (retain)`, `Discard (discard)` — optional on create; not required for this app |
| `MessageEnumAddressRetention` | `Retain (retain)`, `Obfuscate (obfuscate)` — optional on create |
| `MessageEnumTrafficType` | `Free (free)` — optional on create |
| `MessageEnumRiskCheck` | `Enable (enable)`, `Disable (disable)` — optional on create |
| `ValidationError` | `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)` |
| `Field` | `CallerName (caller_name)`, `SimSwap (sim_swap)`, `CallForwarding (call_forwarding)`, `LineTypeIntelligence (line_type_intelligence)`, `LineStatus (line_status)`, `IdentityMatch (identity_match)`, `ReassignedNumber (reassigned_number)`, `SmsPumpingRisk (sms_pumping_risk)` — used by batch lookup models; v2 fetch `fields` is a **string**, not `IReadOnlyList<Field>` |
| `LineType` | `Mobile (mobile)`, `Landline (landline)`, `TollFree (tollFree)`, `FixedVoip (fixedVoip)`, `NonFixedVoip (nonFixedVoip)`, `Personal (personal)`, `Premium (premium)`, `Voicemail (voicemail)`, `SharedCost (sharedCost)`, `Uan (uan)`, `Pager (pager)`, `Unknown (unknown)` — **not** the declared type of `LineTypeIntelligenceInfo.Type` |
| `ServerEnvironment` (`TwilioSdk.Servers`) | `Production ("production")` only. `ServerEnvironment.Default()` → `Production`. |

### Client construction, auth, servers (`sdk-map.md` *Getting a client* + *Servers & auth*)

| Fact | Exact shape |
|---|---|
| Construct | `new TwilioSdk.TwilioSdkClient(System.Net.Http.HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)` |
| DI | `TwilioSdk.ServiceCollectionExtensions.AddTwilioSdkClient(Action<TwilioSdkClientOptions>? configure = null)` (`ServiceCollectionExtensions.cs`) |
| `TwilioSdkClientOptions` (`TwilioSdkClientOptions.cs`) | `Environment: TwilioSdk.Servers.ServerEnvironment`; `Retry: TwilioSdk.Core.Configuration.RetryOptions`; `Logging: TwilioSdk.Core.Configuration.LoggingOptions`; `Server: TwilioSdk.ServerOptions`; `AccountSidAuthToken: TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?` |
| Auth | `options.AccountSidAuthToken = new TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials { Username = <Twilio:AccountSid>, Password = <Twilio:AuthToken> };` both members `required`. XML: basic auth; API key as username + secret as password, **or** account SID + auth token (limit SID/token to local testing). Applied as HTTP `Authorization: Basic …`. |
| Environment | `options.Environment = TwilioSdk.Servers.ServerEnvironment.Production;` |
| Messaging BaseUrl **only** | Messages create/fetch/list/update use `_server.Default(...)`. `TwilioSdk.Servers.DefaultOptions.ProductionOptions.BaseUrl` default **`https://api.twilio.com`**. When `Twilio:BaseUrl` is set, assign it **verbatim** to `options.Server.Default.Production.BaseUrl`. |
| Lookups host (do **not** override with `Twilio:BaseUrl`) | Lookups use `_server.Default4(...)`. `TwilioSdk.Servers.Default4Options.ProductionOptions.BaseUrl` default **`https://lookups.twilio.com`**. Leave it. |
| `TwilioSdk.ServerOptions` (root namespace, `ServerOptions.cs`) | `Default`, `Default1`…`Default14` — each a `*Options` with nested `Production`. Only `Default` and `Default4` are in this integration. |
| `RequestOptions` (`TwilioSdk.Core.RequestOptions`) | `LogLevel? LogLevel` only. |
| `RetryOptions` (`TwilioSdk.Core.Configuration.RetryOptions`) | `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry` — all `required` on a full instance, or `RetryOptions.Default()`. |
| HttpClient | First constructor argument; DI uses `IHttpClientFactory.CreateClient()`. |
| Per-request `ct` | Named `ct`. |

`Twilio:MessagingServiceSid` is **not** a client-option; it is a `CreateMessage` argument used for **scheduled** sends. `Twilio:FromNumber` is a `CreateMessage`/`ListMessage` argument, not a client option.

### Errors (all in-scope ops)

| | |
|---|---|
| Thrown type | `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` (**Case B**). `SdkException<TError>` public member: `required TError Error { get; init; }` only — **no** status on the exception itself. |
| HTTP status | `ex.Error.StatusCode` (`System.Net.HttpStatusCode`). |
| Body | `ex.Error.ReadAsString()` · `ReadAsJson<T>()` · `ReadAsBytes()`. There is **no** generated Twilio error-code accessor on these operations. |
| Success vs throw | Default policy: **2xx only** is success (`HttpStatusPolicy`: 200–299). **4xx and 5xx both throw** the same Case B type; distinguish by `StatusCode`. |
| Typed Case A | **Absent** for every operation in this sheet (29 Case A ops exist in the SDK; none of them are these). Do not catch `SdkException<{Operation}Error>`. |
| No-throw variant | **Absent** across this SDK. |
| 2xx deserialize | `LookupResponse` / `ApiV2010AccountMessage` / `ListMessageResponse` have **no** `required` members (all nullable). A drifted 2xx can still surface as `JsonException` (see trap). |

---

## Trap notes

⚠ Step 1 (client / DI) — `HttpClient` lifetime and whether the SDK wrapper is registered as a singleton over a factory-created client are not visible from the constructor signature; getting this wrong shares or disposes the handler incorrectly. **MUST load `dotnet-client-initialization`** before writing `new TwilioSdkClient` or `AddTwilioSdkClient`.

⚠ Step 1 (auth) — `AccountSidAuthToken` is a `BasicAuthCredentials?` with `required` `Username`/`Password`; wiring SID vs API-key, when credentials are applied, and loading from config rather than literals are not in the options table. **MUST load `dotnet-authentication`** before setting credentials.

⚠ Step 1 (messaging BaseUrl) — `options.Server.Default.Production.BaseUrl` vs `Default4` (lookups) vs `options.Environment`, and whether a post-construction mutation is observed, are not a single assignment. Overriding the wrong node either misses messaging calls or hijacks lookups. **MUST load `dotnet-configuration-resilience`** before setting `Twilio:BaseUrl`.

⚠ Step 1 (retries / timeout) — `RetryOptions` does **not** bound a whole call and is **not** the timeout on the `HttpClient` you register; `HttpMethodsToRetry` does not describe transport-failure retries. A retried `CreateMessage` (non-GET) can execute more than once relative to the shopper. **MUST load `dotnet-configuration-resilience`** before accepting `RetryOptions.Default()` or leaving timeout unset.

⚠ Steps 2–8 (calls) — `FetchPhoneNumber3`, `CreateMessage`, `ListMessage`, and `UpdateMessage` have long tails of must-pass-explicitly nullable parameters with **no C# defaults**; a positional call binds the wrong argument. **MUST load `dotnet-calling-endpoints`** before the first `client.{Group}.{Op}(...)`.

⚠ Steps 2–8 (models) — status/schedule/validation values are `StringEnum<T>`, not C# enums; `LineTypeIntelligenceInfo.Type` is `string?`; unmodeled JSON is dropped on deserialize; date fields on `ApiV2010AccountMessage` are `string?` RFC 2822, while list filters are `DateTimeOffset?`. **MUST load `dotnet-models`** before mapping to eShop types or comparing statuses.

⚠ Step 7 (pagination) — `ListMessage` has no SDK pager; `NextPageUri` vs `pageToken`/`page`/`pageSize` and what “the API provides” as a token are not a foreach. Stopping after page 0 under-counts reconciliation. **MUST load `dotnet-configuration-resilience`** before writing the list loop.

⚠ Step 10 (error boundary) — every op here is Case B (`SdkException<RawError>`) with no `TryGet…` payload accessors and no `…Result` variant; catching a typed `{Op}Error` compiles against other SDK ops and never runs here. **MUST load `dotnet-error-handling`** before any `try/catch`.

⚠ Step 10 (JsonException / 2xx) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 10 (JsonException / non-2xx) — a **non-2xx** body that does not match its operation’s generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Tests — the constructor `HttpClient` is the seam; substituting SDK internals is not. **MUST load `dotnet-testing`** before stubbing.

---

## REQUIRED READING

Load **before implementation starts**. This sheet does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — constructing `TwilioSdkClient`, `AddTwilioSdkClient`, HttpClient ownership |
| `dotnet-authentication` | Step 1 — `AccountSidAuthToken` / `BasicAuthCredentials` |
| `dotnet-calling-endpoints` | Steps 2–8 — named arguments, must-pass-null optionals, form vs query |
| `dotnet-models` | Steps 2–8 — `StringEnum<T>`, wire names, nullable records, dropped JSON |
| `dotnet-error-handling` | Step 10 — Case B `SdkException<RawError>`, 4xx vs 5xx, **both** `JsonException` directions below |
| `dotnet-configuration-resilience` | Step 1 BaseUrl/retries/timeout; Step 7 pagination |
| `dotnet-testing` | Tests for the integration layer |

`JsonException` reaches the boundary from two directions and they need opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation’s generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## Assumptions & Blockers

**Assumptions**

- Phone-number registration uses **Lookups v2** `FetchPhoneNumber3` (not v1) so `Valid` / `ValidationErrors` / `LineTypeIntelligence` are modeled.
- Immediate SMS sets `from` to `Twilio:FromNumber` and leaves `messagingServiceSid` null; scheduled SMS sets **both** `from` and `messagingServiceSid` because scheduling is documented as Messaging Services only while reconciliation still requires FromNumber on the message.
- Provider message id stored by eShop is `ApiV2010AccountMessage.Sid`.
- Reconciliation `from`/`to` query params on `GET /api/notifications/reconciliation` are a **date range**, mapped to `dateSentQueryQuery` (`DateSent>`) and `dateSentQuery` (`DateSent<`), with the Twilio sender filter always `Twilio:FromNumber`.

**Blockers**

- **Caller-supplied idempotency key is not supported on Messages create.** `CreateMessage` has no idempotency parameter; `RequestOptions` cannot set headers; the SDK always sends `Idempotency-Key: {new Guid}`. Provider-side dedupe of operator resend **cannot** be done through this SDK. Persist the key locally (as the feature request allows). Not a missing send/lookup/schedule/cancel/fetch/list/redact capability.
- **Send-at window** (how far in the future is allowed) is **not present** in the SDK surface or XML. **UNVERIFIED.** Treat a Case B 4xx from `CreateMessage` as the constraint; do not encode a local min/max from memory.
- **`LineTypeIntelligenceInfo.Type` vocabulary** is an untyped `string?`. Whether live lookup returns `LineType` wire values (e.g. `mobile`) is **UNVERIFIED**.
- **Joint `From` + `MessagingServiceSid`** on one create: the SDK will send both; whether the live API accepts both on a scheduled message is **UNVERIFIED**. If create fails Case B, read `StatusCode` + body rather than dropping `from`.
- **Already-sent / already-cancelled / not-found / invalid-to** have **no typed error models** on these operations (Case B only). Do not invent status codes.
