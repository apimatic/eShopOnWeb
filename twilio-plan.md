# Twilio .NET SDK integration plan — eShopOnWeb

Grounded in the bundled SDK map (`twilio-getting-started/sdk-map.md` + `map/`, pinned at source commit `51fdf48`, "Publish v2.0.0 SDK") and, where the map was silent (server-slot override mechanics, `BasicAuthCredentials` members), in the map-named source files at that pinned commit. Map pages cited per row.

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 1 | Add NuGet package `AsadAli.TwilioSdk` (version-less; with central package management, declare the version in `Directory.Packages.props` and add a version-less `PackageReference` in the project) — `sdk-map.md` | — |
| 2 | Register client + options + auth + messaging-only BaseUrl override in DI — `sdk-map.md`, `TwilioSdkClientOptions.cs`, `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Servers/Default4Options.cs` | — |
| 3 | Validate + canonicalize registration mobile number | `LookupsV2PhoneNumber.FetchPhoneNumber3` |
| 4 | Send order lifecycle SMS (placed / dispatched / cancelled) | `Api20100401Message.CreateMessage` |
| 5 | Schedule follow-up SMS (provider-held) | `Api20100401Message.CreateMessage` with `scheduleType` + `sendAt` |
| 6 | Cancel scheduled follow-up on order cancellation | `Api20100401Message.UpdateMessage` (`status`) |
| 7 | Poll delivery status by SID | `Api20100401Message.FetchMessage` |
| 8 | Operator re-send (fresh send, new SID) | `Api20100401Message.CreateMessage` |
| 9 | GDPR body redaction (record survives) / full deletion | `Api20100401Message.UpdateMessage` (`body`) / `Api20100401Message.DeleteMessage` |
| 10 | Reconciliation list: date range + From filter, paged | `Api20100401Message.ListMessage` |

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

### 2.1 Package, client construction, auth, servers

| Fact | Contract | Source |
|---|---|---|
| NuGet package | `AsadAli.TwilioSdk` — install version-less (`dotnet add package AsadAli.TwilioSdk`; under CPM put the resolved version in `Directory.Packages.props`) | `sdk-map.md` |
| Root namespace / client / options | `TwilioSdk` / `TwilioSdkClient` / `TwilioSdkClientOptions` (both root namespace `TwilioSdk`) | `sdk-map.md` |
| Client constructor | `new TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)` | `sdk-map.md` (`TwilioSdkClient.cs`) |
| DI registration | `services.AddTwilioSdkClient(o => { /* set credentials / environment / server on o */ })` — `o` is `TwilioSdkClientOptions` | `sdk-map.md` (`ServiceCollectionExtensions.cs`) |
| Options properties | `Environment: ServerEnvironment` (`TwilioSdk.Servers`, member `ServerEnvironment.Production`) · `Retry: RetryOptions` (`TwilioSdk.Core.Configuration`; all members `required` — start from `RetryOptions.Default()`) · `Logging: LoggingOptions` (`TwilioSdk.Core.Configuration`) · `Server: ServerOptions` (root namespace `TwilioSdk`) · `AccountSidAuthToken: BasicAuthCredentials?` (`TwilioSdk.Core.Authentication.Basic`) | `sdk-map.md`, `TwilioSdkClientOptions.cs` |
| Auth credentials | `o.AccountSidAuthToken = new BasicAuthCredentials { Username = <AccountSid>, Password = <AuthToken> };` — `Username` and `Password` are `required string` init-only properties. (Source XML doc: an API key + secret may instead be used as username/password; account SID + auth token is supported.) | `sdk-map.md` *Servers & auth*, `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Server slots | `ServerOptions` (root namespace `TwilioSdk`) has properties `Default` … `Default14`, each a `DefaultNOptions` (`TwilioSdk.Servers`) with `Production.BaseUrl: string`. Slot → default host: `Default` → `https://api.twilio.com` (messaging API), `Default4` → `https://lookups.twilio.com` (Lookup API). | `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Servers/Default4Options.cs` |
| **BaseUrl override — messaging only** | All 5 `Api20100401Message` operations resolve their URL via server slot **`Default`** (each call site is `_server.Default("/2010-04-01/Accounts/{AccountSid}/Messages…")`); both Lookup operations use slot **`Default4`** (`_server.Default4("/v2/PhoneNumbers/{PhoneNumber}")`). The slot's `Production.BaseUrl` is used verbatim as the base address and the operation path is appended (`new UrlTemplate(Production.BaseUrl, path, [])`). Therefore: `o.Server.Default.Production.BaseUrl = <Twilio:BaseUrl>` redirects **every messaging-API call** (send/fetch/update/delete/list) and **only** those — Lookup keeps going to `https://lookups.twilio.com` unless `o.Server.Default4.Production.BaseUrl` is also set. Apply the override only when config `Twilio:BaseUrl` is non-empty. | `Api/Api20100401Message.cs`, `Api/LookupsV2PhoneNumber.cs`, `Servers/DefaultOptions.cs` |
| Per-call AccountSid | Every messaging operation takes `string accountSid` as its first parameter (path `{AccountSid}`) — pass config `Twilio:AccountSid` per call, in addition to using it as the auth username. | `operations/Api20100401Message.md` |

### 2.2 Operations

**Error model for every operation below: Case B — throws `SdkException<RawError>`** (`TwilioSdk.Core.Exceptions` / `TwilioSdk.Core.ErrorResponse`). Read failures via `ex.Error.StatusCode: HttpStatusCode`, `ex.Error.ReadAsString(): string`, `ex.Error.ReadAsJson<T>(): T?`, `ex.Error.ReadAsBytes(): ReadOnlyMemory<byte>`. No typed `TryGet…` accessors and no no-throw `…Result` variant exist for any of these operations. (`sdk-map.md` error model + per-operation rows.)

| Step | Controller · signature (verbatim) | Request fields (wire ← C#) | Response envelope → fields read | Pagination |
|---|---|---|---|---|
| 3 — Validate number | `client.LookupsV2PhoneNumber` · `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `fields`…`partnerSubId` nullable, no default → pass `null` explicitly. `GET /v2/PhoneNumbers/{PhoneNumber}` | `phoneNumber` = the number as typed (path param); pass all 15 optional params `null` for a plain validation lookup | `LookupResponse` (no inner envelope — fields sit directly on it): **`PhoneNumber (phone_number): string?` ← canonical E.164 form to store** · **`Valid (valid): bool?` ← usability flag** · **`ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?` ← rejection reasons** · `NationalFormat (national_format): string?` · `CountryCode (country_code): string?` · `CallingCountryCode (calling_country_code): string?` | none |
| 4 / 8 — Send (incl. re-send) | `client.Api20100401Message` · `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `statusCallback`…`contentSid` nullable, no default → pass `null` explicitly. `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` (form-url-encoded body, per source `FormUrlEncodedRequest.Create`) | `To` ← `to` (required) · `From` ← `from` (config `Twilio:FromNumber`) · `MessagingServiceSid` ← `messagingServiceSid` (config `Twilio:MessagingServiceSid`) · `Body` ← `body` · `ScheduleType` ← `scheduleType` · `SendAt` ← `sendAt` · `StatusCallback` ← `statusCallback` · `ValidityPeriod` ← `validityPeriod` · (`MediaUrl`, `ContentSid`, `ContentVariables`, `SmartEncoded`, `ShortenUrls`, `TrafficType`, `RiskCheck`, `ContentRetention`, `AddressRetention`, `PersistentAction`, `MaxPrice`, `ProvideFeedback`, `Attempt`, `ForceDelivery`, `ApplicationSid`, `SendAsMms`, `FallbackFrom` — out of scope, pass `null`) | `ApiV2010AccountMessage` (returned directly, no wrapper): **`Sid (sid): string?` ← provider message identifier** · **`Status (status): MessageEnumStatus?` ← initial status** · `ErrorCode (error_code): int?` · `ErrorMessage (error_message): string?` · `From (from): string?` · `To (to): string?` · `Body (body): string?` · `DateSent (date_sent): string?` · `DateCreated (date_created): string?` · `DateUpdated (date_updated): string?` · `MessagingServiceSid (messaging_service_sid): string?` · `Direction (direction): MessageEnumDirection?` · `NumSegments (num_segments): string?` · `Price (price): string?` · `PriceUnit (price_unit): string?` · `AccountSid (account_sid): string?` · `Uri (uri): string?` · `ApiVersion (api_version): string?` · `SubresourceUris (subresource_uris): object?`. Note: the three date fields are declared `string?` (provider date strings), not `DateTimeOffset` — parse defensively. | none |
| 5 — Schedule | Same `CreateMessage`; scheduling is expressed as **`scheduleType: MessageEnumScheduleType.Fixed`** (wire `ScheduleType=fixed`) **+ `sendAt: <DateTimeOffset>`** (wire `SendAt`, ISO-8601). The enum's map doc: *"For Messaging Services only"* → route scheduled sends via `messagingServiceSid` (pass `from: null`). Read the scheduled message's `Sid` and initial `Status` (= `MessageEnumStatus.Scheduled`) from the same `ApiV2010AccountMessage`. | as above | as above | none |
| 6 — Cancel scheduled | `client.Api20100401Message` · `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `body` and `status` nullable, no default → pass explicitly. `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` | Cancellation = **`body: null`, `status: MessageEnumUpdateStatus.Canceled`** (wire `Status=canceled`; the enum's only value) | `ApiV2010AccountMessage` — confirm `Status` == `MessageEnumStatus.Canceled` | none |
| 7 — Poll status | `client.Api20100401Message` · `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)`. `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` | `sid` = stored message SID (path param) | `ApiV2010AccountMessage` — read `Status`, `ErrorCode`, `ErrorMessage`, `DateSent`, `DateCreated`, `DateUpdated`, `From`, `To`, `Body` | none |
| 9 — Redact body (GDPR) | Same `UpdateMessage`; redaction = **`body: ""` (empty string), `status: null`** (wire `Body=`). Effect: the message text is no longer retrievable from the provider; the Message **record** (SID, status/outcome, dates, from/to) survives — this is the operation that matches the stated requirement. (Map note on the operation: *"used to redact Message `body` text and to cancel not-yet-sent messages"*.) | `Body` ← `body`, `Status` ← `status` | `ApiV2010AccountMessage` | none |
| 9 — Full delete (alternative) | `client.Api20100401Message` · `DeleteMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)`. `DELETE /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` | `sid` (path param) | `void` (Task). Effect: the **entire Message resource is removed** — afterwards `FetchMessage` for that SID fails with an error status (`SdkException<RawError>`). Use only when the record itself may also disappear; not the default for the stated requirement. | none |
| 10 — Reconciliation list | `client.Api20100401Message` · `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `to`…`pageToken` nullable, no default → pass explicitly. `GET /2010-04-01/Accounts/{AccountSid}/Messages.json` | Provider-side filters (wire ← C#): **`From` ← `from`** (config `Twilio:FromNumber`) · **`DateSent>` ← `dateSentQueryQuery`** (range start, ISO-8601 `DateTimeOffset?`) · **`DateSent<` ← `dateSentQuery`** (range end) · `DateSent` ← `dateSent` (exact-date match — not used for a range; pass `null`) · `To` ← `to` (pass `null`) · `PageSize` ← `pageSize` · `Page` ← `page` · `PageToken` ← `pageToken`. Yes — the generated C# names for the two inequality filters really are `dateSentQuery` (`DateSent<`) and `dateSentQueryQuery` (`DateSent>`); use named arguments. | `ListMessageResponse` envelope: **`Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?` ← the page of messages** (each item as in step 4) · **`NextPageUri (next_page_uri): string?` ← non-null while more pages exist** · `PreviousPageUri (previous_page_uri): string?` · `FirstPageUri (first_page_uri): string?` · `Page (page): int?` · `PageSize (page_size): int?` · `Start (start): int?` · `End (end): int?` · `Uri (uri): string?` | **No SDK auto-pagination** (map row: "none"). Loop until `NextPageUri` is null, advancing `page`/`pageToken` per the pagination guidance in `dotnet-configuration-resilience` (see trap notes). |

Map citations: `operations/Api20100401Message.md`, `operations/LookupsV2PhoneNumber.md`, `records-1-Ac-Ca.md` (`ApiV2010AccountMessage`), `records-4-Li-Me.md` (`ListMessageResponse`, `LookupResponse`, `LookupsV1PhoneNumber`), `enums.md`.

### 2.3 Enums (all `StringEnum<T>`, namespace `TwilioSdk.Models.Enums` — `enums.md`)

| Enum | Values (C# member (wire)) | Used for |
|---|---|---|
| `MessageEnumStatus` | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` | `ApiV2010AccountMessage.Status` — reporting/polling outcomes |
| `MessageEnumScheduleType` | `Fixed (fixed)` — map doc: *"For Messaging Services only: Include this parameter with a value of `fixed` in conjuction with the `send_time` parameter in order to schedule a Message."* | step 5 scheduling |
| `MessageEnumUpdateStatus` | `Canceled (canceled)` (only value) | step 6 cancellation |
| `MessageEnumDirection` | `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)` | `ApiV2010AccountMessage.Direction` (reconciliation sanity check) |
| `ValidationError` | `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)` | `LookupResponse.ValidationErrors` |

### 2.4 How an invalid number manifests (step 3)

Two paths, both must reject the registration: (a) HTTP 200 with `Valid != true` (reasons in `ValidationErrors`); (b) an error status surfacing as `SdkException<RawError>` (Case B) — read `StatusCode`/body via the accessors in §2.2. Which path a given bad input takes is a live-wire behavior the map and source cannot settle — **UNVERIFIED**; defensive directive: treat *either* `Valid != true` *or* an `SdkException<RawError>` from `FetchPhoneNumber3` as "not a usable destination", and only persist `PhoneNumber` (E.164) when `Valid == true`.

## 3. Trap notes

> ⚠ Step 2 (client registration) — the `HttpClient`/handler pipeline behind `TwilioSdkClient` has lifetime rules the constructor signature doesn't state; building it per request exhausts sockets. **MUST load `dotnet-client-initialization`** before writing the DI registration.

> ⚠ Step 2 (auth) — when in the construction flow credentials must be set, and how secrets reach the options object, is not visible from the property list. **MUST load `dotnet-authentication`** before wiring `AccountSidAuthToken`.

> ⚠ Steps 4–10 (calling) — `CreateMessage` carries 24 nullable parameters with no C# defaults and `ListMessage` 8; a positional call mis-binds silently, and the two date-range filters have non-obvious generated names. **MUST load `dotnet-calling-endpoints`** before the first call.

> ⚠ Steps 3–10 (models) — `MessageEnumStatus` and friends are `StringEnum<T>`, not C# enums: how values are constructed and compared, and what deserialization does with JSON fields the model doesn't declare, is not shown by the signatures. **MUST load `dotnet-models`** before reading/writing enum-typed fields.

> ⚠ Steps 3–10 (error boundary) — every in-scope operation is Case B (`SdkException<RawError>`), and `System.Text.Json.JsonException` reaches the boundary from two directions needing opposite handling (see REQUIRED READING). What the boundary must look like is not derivable from the signatures. **MUST load `dotnet-error-handling`** before writing any `try/catch`.

> ⚠ Step 2 (resilience) — whether a transport failure on the non-idempotent `CreateMessage` POST can be re-executed by the retry layer (duplicate-SMS risk), what `RetryOptions.Timeout` actually bounds, and how list pagination is meant to be driven, are all things the option names alone do not reveal. **MUST load `dotnet-configuration-resilience`** before tuning retries/timeouts and before writing the step-10 paging loop.

> ⚠ Testing — the SDK's test seam and how to fake it without depending on SDK internals is a named skill concern. **MUST load `dotnet-testing`** before stubbing the SDK.

## 4. REQUIRED READING

Load **before implementation starts**; this sheet deliberately does not carry their contents.

- `dotnet-client-initialization` — step 2 (client construction & DI lifetime)
- `dotnet-authentication` — step 2 (credentials wiring)
- `dotnet-calling-endpoints` — steps 3–10 (every operation call)
- `dotnet-models` — steps 3–10 (records, `StringEnum<T>` enums)
- `dotnet-error-handling` — the integration error boundary (always required)
- `dotnet-configuration-resilience` — step 2 retry/timeout tuning, step 10 pagination, BaseUrl behavior
- `dotnet-testing` — tests for the integration layer

Two `System.Text.Json.JsonException` hazards reach the boundary from opposite directions and need opposite handling:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
  maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
  and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

## 5. Assumptions & Blockers

- **Lookup v2 chosen over v1.** The SDK also exposes `LookupsV1PhoneNumberApi.FetchPhoneNumber2` → `LookupsV1PhoneNumber`, whose model carries **no** `Valid`/`ValidationErrors` fields (`records-4-Li-Me.md`) — only v2's `LookupResponse` supports the reject-unusable-numbers requirement. Assumed v2 is acceptable ("or equivalent capability the SDK exposes").
- **Scheduled sends route through the Messaging Service.** `MessageEnumScheduleType`'s map doc restricts scheduling to Messaging Services, so step 5 sends with `messagingServiceSid` (and `from: null`). Immediate sends (step 4) may use `from` (Twilio:FromNumber) or `messagingServiceSid`; assumed the app picks one sender identity per send from configuration — the map does not state whether `From` and `MessagingServiceSid` may be combined on one request.
- **UNVERIFIED (live-wire only):** whether an invalid/ unusable number comes back as 200 + `Valid=false` versus an error status — defensive handling specified in §2.4 covers both.
- **Version drift risk (reported, not blocking):** this sheet is grounded at the map's pinned source commit `51fdf48` ("Publish v2.0.0 SDK", root namespace `TwilioSdk`). The SDK repo's current `main` HEAD (`3d2efed`, "Regenerate SDK (v4 beta codegen)") uses a different root namespace (`Twilio.*`). Because the package is installed version-less, if NuGet resolves a release newer than the map's pin, the compiler is the backstop: any name that fails to compile means the installed package drifted from this sheet — re-ground with the twilio-sdk agent rather than patching from memory.
- No other blockers.
