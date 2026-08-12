# Twilio .NET SDK — SMS Order-Notification Integration Plan (eShopOnWeb)

SDK: `AsadAli.TwilioSdk` (install version-less: `dotnet add package AsadAli.TwilioSdk`), root namespace `TwilioSdk`, client `TwilioSdkClient`. Map source commit `51fdf48`. All facts below are grounded in the bundled SDK map (pages cited per row); the server-override and idempotency facts are grounded in the SDK source files the map named.

Config section `Twilio:` provides: `AccountSid`, `AuthToken`, `FromNumber` (E.164), `MessagingServiceSid` (MG…), optional `BaseUrl` (messaging/api host override only).

---

## 1. Scope & sequence

1. **Client + DI setup** — register `TwilioSdkClient` as a singleton over an `IHttpClientFactory`-owned `HttpClient`; wire basic auth (AccountSid/AuthToken) and apply `Twilio:BaseUrl` to the **messaging (api) server node only**. (client construction/DI)
2. **Number registration** — validate a destination and store its canonical E.164 form via `LookupsV2PhoneNumber.FetchPhoneNumber3` (lookups host). (lookup)
3. **Send order SMS** — `Api20100401Message.CreateMessage` preferring `MessagingServiceSid`. (create)
4. **Schedule follow-up (~3 days out)** — `CreateMessage` with `scheduleType` + `sendAt` (Messaging Service required). (create/schedule)
5. **Cancel scheduled follow-up** — `Api20100401Message.UpdateMessage` with cancel status. (update)
6. **Fetch delivery status** — `Api20100401Message.FetchMessage`. (fetch)
7. **Redact body** — `UpdateMessage` with empty body. (update/redact)
8. **Reconciliation list** — `Api20100401Message.ListMessage` filtered by From + DateSent range, paged. (list)
9. **Error boundary** — one catch layer over all messaging/lookup calls (all Case B, `SdkException<RawError>`). (error handling)

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### 2a. Namespaces (using-directives) — each from its own map/source row

| Type | Namespace | Source |
|---|---|---|
| `TwilioSdkClient`, `TwilioSdkClientOptions`, `ServerOptions` | `TwilioSdk` | root (`TwilioSdkClient.cs`, `TwilioSdkClientOptions.cs`, `ServerOptions.cs`) |
| `AddTwilioSdkClient` (DI extension) | `TwilioSdk` | `ServiceCollectionExtensions.cs` |
| `ServerEnvironment`, `DefaultOptions`, `Default4Options` | `TwilioSdk.Servers` | `Servers/*.cs` |
| `BasicAuthCredentials` | `TwilioSdk.Core.Authentication.Basic` | source `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| `RequestOptions` | `TwilioSdk.Core` | source `Core/RequestOptions.cs` |
| `RetryOptions` | `TwilioSdk.Core.Configuration` | `Core/Configuration/RetryOptions.cs` |
| Records: `ApiV2010AccountMessage`, `ListMessageResponse`, `LookupResponse` | `TwilioSdk.Models` | `Models/*.cs` |
| Enums: `MessageEnumStatus`, `MessageEnumScheduleType`, `MessageEnumUpdateStatus`, `MessageEnumDirection`, `MessageEnumContentRetention`, `MessageEnumAddressRetention`, `ValidationError` | `TwilioSdk.Models.Enums` | `Models/Enums/*.cs` |
| `SdkException<T>` | `TwilioSdk.Core.Exceptions` | `Core/Exceptions/SdkException.cs` |
| `RawError` | `TwilioSdk.Core.ErrorResponse` | `Core/ErrorResponse/RawError.cs` |

Controllers are accessed as properties on the client (`client.Api20100401Message`, `client.LookupsV2PhoneNumber`) — no `using` needed for the controller types themselves.

### 2b. Client construction & auth (source: `TwilioSdkClientOptions.cs`, `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Servers/Default4Options.cs`, sdk-map *Servers & auth*)

Constructor (only one): `TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)`.

`TwilioSdkClientOptions` writable properties:

| Property | Type | Use |
|---|---|---|
| `AccountSidAuthToken` | `BasicAuthCredentials?` | **Basic auth.** `new BasicAuthCredentials { Username = <AccountSid>, Password = <AuthToken> }` — both are `required`. (Twilio's own XML doc recommends an API key SID/secret here and says account SID + auth token are fine but best limited to local testing — informational; AccountSid/AuthToken works.) |
| `Environment` | `ServerEnvironment` | `ServerEnvironment.Production` is the **only** member (wire `production`). `ServerEnvironment.Default()` also returns Production. |
| `Server` | `ServerOptions` | Per-server-node base-URL overrides — see below. |
| `Retry` | `RetryOptions` | Optional resilience tuning — see trap note. |
| `Logging` | `LoggingOptions` | Optional. |

**Base URL / server selection — the load-bearing part.** `ServerOptions` (on `options.Server`) exposes 15 named server nodes (`Default` … `Default14`), each a per-environment options object with a `Production.BaseUrl` string. Base URL is therefore **per-client and per-environment (Production node), set once at construction — NOT per-request** (`RequestOptions` carries no URL). The two nodes this integration touches:

| Server node | Property path to override | Default host | Used by |
|---|---|---|---|
| `Default` (labelled "api") | `options.Server.Default.Production.BaseUrl` | `https://api.twilio.com` | **all `Api20100401Message` operations** (create/fetch/list/update/delete) |
| `Default4` (labelled "lookups") | `options.Server.Default4.Production.BaseUrl` | `https://lookups.twilio.com` | `LookupsV2PhoneNumber.FetchPhoneNumber3` |

So apply `Twilio:BaseUrl` to **`options.Server.Default.Production.BaseUrl` ONLY**, and leave `options.Server.Default4` at its default `https://lookups.twilio.com`. This gives exactly "messaging API calls use the override; lookup uses its own separate host" as required. Messaging and lookup are genuinely different hosts selected by different server nodes — confirmed in source.

DI (`ServiceCollectionExtensions.cs`): `services.AddTwilioSdkClient(o => { /* set o.AccountSidAuthToken, o.Environment, o.Server.Default.Production.BaseUrl */ });`. HttpClient ownership/lifetime is a trap — see Step 1 note.

### 2c. Operations (page: `operations/Api20100401Message.md`, `operations/LookupsV2PhoneNumber.md`)

All operations below are **Case B** → throw `SdkException<RawError>`; **no** typed error accessors and **no** `…Result` no-throw variant. Error accessors on `ex.Error`: `StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`, `ReadAsBytes(): ReadOnlyMemory<byte>`.

| # | Controller.Op | Signature (params in order; all nullable-no-default params MUST be passed explicitly, `null` to skip) | Returns | Reads |
|---|---|---|---|---|
| 3/4 | `client.Api20100401Message.CreateMessage` | `(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `ApiV2010AccountMessage` | `Sid`, `Status`, `ErrorCode`, `ErrorMessage`, `DateCreated` |
| 5/7 | `client.Api20100401Message.UpdateMessage` | `(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `ApiV2010AccountMessage` | `Sid`, `Status`, `Body` |
| 6 | `client.Api20100401Message.FetchMessage` | `(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `ApiV2010AccountMessage` | `Status`, `ErrorCode`, `ErrorMessage`, `DateSent`, `Price` |
| 8 | `client.Api20100401Message.ListMessage` | `(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `ListMessageResponse` | `Messages`, `NextPageUri`, `Page`, `PageSize` |
| 2 | `client.LookupsV2PhoneNumber.FetchPhoneNumber3` | `(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `LookupResponse` | `PhoneNumber`, `Valid`, `ValidationErrors`, `NationalFormat` |

`accountSid` for all Message ops is the `{AccountSid}` path segment — pass `Twilio:AccountSid`. `sid` is the `SM…` message SID. `phoneNumber` is the `{PhoneNumber}` path segment (pass the raw/E.164 input).

**Per-capability wiring:**

- **(3) Send SMS.** Provide `to` (positional, required), then one sender: **prefer `messagingServiceSid:` (the MG… service)** over `from:` — the Messaging Service adds sender-pool selection, and is *mandatory* for scheduling (capability 4). Set `body:` for the text. Pass `null` for every other param (including `from:` when using the service, and `scheduleType:`/`sendAt:`). Read `Sid` (the `SM…` id), `Status`, and `ErrorCode`/`ErrorMessage` (null on a clean accept).
- **(4) Schedule ~3 days out.** Same `CreateMessage`, additionally: `scheduleType: MessageEnumScheduleType.Fixed` (only member; wire `fixed`), `sendAt: DateTimeOffset.UtcNow.AddDays(3)`. Scheduling **requires `messagingServiceSid:`** and a **`from:` is not allowed** with it (per the `ScheduleType` doc "For Messaging Services only"). Response `Status` will be `Scheduled`.
- **(5) Cancel scheduled.** `UpdateMessage(accountSid, sid, body: null, status: MessageEnumUpdateStatus.Canceled)` (only member; wire `canceled`). If the message already sent, the update is rejected → `SdkException<RawError>` (status/`code` in the JSON body; exact HTTP status is `UNVERIFIED` — treat any non-2xx here as "too late to cancel" and read `ex.Error.StatusCode` + parsed `code`).
- **(6) Fetch status.** `FetchMessage(accountSid, sid)`; read `Status` (enum below), `ErrorCode` (int?), `ErrorMessage`.
- **(7) Redact body.** `UpdateMessage(accountSid, sid, body: "", status: null)` — an **empty-string body** replaces the stored text at the provider. **Survives:** the message record and its metadata — `Sid`, `Status`, `From`/`To`, `DateSent`, `ErrorCode`, etc. remain on the returned `ApiV2010AccountMessage`; only `Body` is emptied. (That empty body vs the pre-existing text is a provider-side effect confirmable only on live traffic — `UNVERIFIED`; code defensively: after the call, treat `Body` as unrecoverable and rely on your own stored copy if you need the original.)
- **(8) List/reconcile.** Filter server-side by our From: pass `from: <Twilio:FromNumber>` (wire `From` — **this is a provider request query param, not client-side filtering**). Date range via the two query-bound params (Twilio semantics): `dateSentQueryQuery` → wire `DateSent>` = **after / lower bound** (range start); `dateSentQuery` → wire `DateSent<` = **before / upper bound** (range end); the plain `dateSent` (wire `DateSent`) is an exact-day match — pass `null` when using a range. Read `Messages` (`IReadOnlyList<ApiV2010AccountMessage>`). **Pagination:** the map marks this op "Pagination: none" — there is **no built-in auto-pager**. Page manually: start with `page: 0` (or `null`) and a `pageSize:` (e.g. 100), then either walk `NextPageUri` from each `ListMessageResponse` or increment `page:`/carry `pageToken:` until `Messages` is empty / `NextPageUri` is null, to cover the whole range. (Range-bound inclusivity is a live-wire detail — `UNVERIFIED`; dedupe by `Sid` across pages and don't assume boundary rows are included.)
- **(2) Validate + canonical E.164.** `FetchPhoneNumber3(phoneNumber, null, null, … null)` — pass the number as the path arg and `null` for all 15 optional query params (canonical validation needs none of them). Host is the **lookups** node (`Default4`), separate from the messaging override — confirmed. Read: **`Valid` (`bool?`) is how validity is signalled — a field, NOT an exception**: an unusable-but-parseable number returns HTTP 200 with `Valid == false` and reasons in `ValidationErrors`. **`PhoneNumber` (`phone_number`) is the canonical E.164 form** to store; `NationalFormat` is the national rendering. A genuinely malformed request (e.g. empty path) or auth failure still throws `SdkException<RawError>`; a merely-invalid number does not.

### 2d. Response record fields (page: `models/records-1-Ac-Ca.md`, `records-4-Li-Me.md`)

**`ApiV2010AccountMessage`** (`TwilioSdk.Models`) — all fields nullable:
`Body (body): string?`, `NumSegments (num_segments): string?`, `Direction (direction): MessageEnumDirection?`, `From (from): string?`, `To (to): string?`, `DateUpdated (date_updated): string?`, `Price (price): string?`, `ErrorMessage (error_message): string?`, `Uri (uri): string?`, `AccountSid (account_sid): string?`, `NumMedia (num_media): string?`, `Status (status): MessageEnumStatus?`, `MessagingServiceSid (messaging_service_sid): string?`, `Sid (sid): string?`, `DateSent (date_sent): string?`, `DateCreated (date_created): string?`, `ErrorCode (error_code): int?`, `PriceUnit (price_unit): string?`, `ApiVersion (api_version): string?`, `SubresourceUris (subresource_uris): object?`.
Note: `DateSent`/`DateCreated` are `string?` (not `DateTimeOffset`); `ErrorCode` is `int?`.

**`ListMessageResponse`** (`TwilioSdk.Models`):
`End (end): int?`, `FirstPageUri (first_page_uri): string?`, `NextPageUri (next_page_uri): string?`, `Page (page): int?`, `PageSize (page_size): int?`, `PreviousPageUri (previous_page_uri): string?`, `Start (start): int?`, `Uri (uri): string?`, `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?`.
**This is the envelope — the actual messages are one level down in `Messages`.**

**`LookupResponse`** (`TwilioSdk.Models`) — relevant fields:
`CallingCountryCode (calling_country_code): string?`, `CountryCode (country_code): string?`, `PhoneNumber (phone_number): string?`, `NationalFormat (national_format): string?`, `Valid (valid): bool?`, `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?`, plus optional data-package fields (`CallerName`, `LineTypeIntelligence`, … — all null unless requested via `fields`), `Url (url): string?`.

### 2e. Enums needed (page: `models/enums.md`) — build with `Type.Member` or `Type.FromValue("wire")`

**`MessageEnumStatus`** (`TwilioSdk.Models.Enums`) — full value list + meaning:

| Member | Wire | Meaning |
|---|---|---|
| `Queued` | `queued` | Accepted, waiting to be sent by Twilio |
| `Sending` | `sending` | In the process of dispatching to the carrier |
| `Sent` | `sent` | Handed to the carrier (not yet confirmed delivered) |
| `Delivered` | `delivered` | Carrier confirmed delivery |
| `Undelivered` | `undelivered` | Carrier could not deliver (see `ErrorCode`/`ErrorMessage`) |
| `Failed` | `failed` | Send failed (see `ErrorCode`/`ErrorMessage`) |
| `Receiving` | `receiving` | Inbound message being received |
| `Received` | `received` | Inbound message received |
| `Accepted` | `accepted` | Accepted for scheduling/processing |
| `Scheduled` | `scheduled` | Scheduled for future send (set after capability 4) |
| `Canceled` | `canceled` | A scheduled message was canceled (capability 5) |
| `PartiallyDelivered` | `partially_delivered` | Some segments delivered |
| `Read` | `read` | Read (WhatsApp only) |

**`MessageEnumScheduleType`** — `Fixed` (wire `fixed`) — only member.
**`MessageEnumUpdateStatus`** — `Canceled` (wire `canceled`) — only member (this is the enum for `UpdateMessage`'s `status` param).
**`ValidationError`** (in `LookupResponse.ValidationErrors`) — `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)`.
**`MessageEnumDirection`** (on the message record) — `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)`.

### 2f. Error handling contract (sdk-map *Error-handling model*)

Every messaging and lookup op is **Case B** — a non-2xx status throws `SdkException<RawError>` (`ex` is `SdkException<RawError>`; `ex.Error` is `RawError`). No typed `{Op}Error`, no `TryGet…` accessors. Read:

- HTTP status: `ex.Error.StatusCode` (`System.Net.HttpStatusCode`).
- Twilio error code + message: **not** a typed accessor — parse the JSON body. `ex.Error.ReadAsString()` gives the raw body; Twilio's standard error body is `{ "code": <int>, "message": "...", "more_info": "...", "status": <int> }`. Deserialize via `ex.Error.ReadAsJson<T>()` into a small DTO (`code`, `message`), falling back to `ReadAsString()`/a generic message if the body is absent or off-shape. (The live body shape is provider-controlled — `UNVERIFIED`; extract best-effort, fall back to the generic message. See the JsonException rows in REQUIRED READING — a non-2xx body that does not match its shape throws `JsonException` while the error object is built, destroying the status.)
- **"Accepted by API but carrier refuses" (e.g. undeliverable US destination) is NOT an exception at send time.** `CreateMessage` returns 2xx with `Status` = `queued`/`accepted` and `ErrorCode == null`. The refusal surfaces **later** as an asynchronous status transition to `Undelivered`/`Failed` with `ErrorCode`/`ErrorMessage` populated — observable only by (6) `FetchMessage` or a status-callback webhook, never by catching the send call.

### 2g. Idempotency (source: `Core/RequestOptions.cs`, `operations/Api20100401Message.md`)

**The SDK's `CreateMessage` does NOT support a caller idempotency key — neither a parameter nor a header hook.** No `idempotencyKey`/`x-idempotency-key` parameter exists in the signature, and `RequestOptions` exposes only `LogLevel? LogLevel` — there is **no** custom-header/arbitrary-header surface to inject one. **Idempotency must be handled at the application layer** (e.g. a dedupe key on your order + a "already sent?" check before calling `CreateMessage`).

---

## 3. Trap notes (load the named skill before writing that step — do not rely on these one-liners as the answer)

- ⚠ **Step 1 (client + DI)** — whether the `HttpClient`/handler pipeline may be rebuilt per request or must be long-lived and factory-owned, and whether the SDK client wrapper is singleton vs transient, is not visible in the constructor signature. **MUST load `dotnet-client-initialization`** before wiring DI.
- ⚠ **Step 1/2 (auth)** — when and where credentials must be set relative to client construction, and how to source them from configuration rather than hardcoding, are conventions the property type does not show. **MUST load `dotnet-authentication`** before setting `AccountSidAuthToken`.
- ⚠ **Step 1 (base URL / resilience)** — what `RetryOptions.Timeout` actually bounds (per-attempt vs whole call), which verbs/statuses actually retry, and whether a failed `POST` (a non-idempotent send) can be re-executed by transport-failure retry, are NOT what the option names suggest. **MUST load `dotnet-configuration-resilience`** before tuning retries/timeouts or the base URL.
- ⚠ **Step 8 (pagination)** — the op has no built-in pager; how to safely walk pages/`NextPageUri` to cover a full range without gaps or double-counting is a usage concern the signature hides. **MUST load `dotnet-configuration-resilience`** (pagination section) before writing the reconciliation loop.
- ⚠ **Steps 3–8 (models)** — enums here are `StringEnum<T>`, not C# enums (build via `Member`/`FromValue`), and unmodeled JSON fields are dropped on deserialize. **MUST load `dotnet-models`** before constructing requests or mapping responses.
- ⚠ **Step 9 (error boundary)** — which exceptions actually reach the catch, and why an SDK-exception-only ladder is silently wrong, are covered by the skill, not by the map row. **MUST load `dotnet-error-handling`** before writing the boundary (see REQUIRED READING for the two mandatory hazards).

---

## 4. REQUIRED READING (load ALL before implementation starts — this sheet deliberately does not carry their contents)

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, HttpClient ownership/lifetime, DI registration |
| `dotnet-authentication` | Steps 1–2 — supplying basic-auth credentials, sourcing secrets from config |
| `dotnet-configuration-resilience` | Step 1 base-URL/server selection & retries/timeouts; Step 8 pagination |
| `dotnet-calling-endpoints` | Steps 2–8 — calling ops with named args, required vs optional params |
| `dotnet-models` | Steps 3–8 — building requests, `StringEnum<T>`, response mapping |
| `dotnet-error-handling` | Step 9 — the exception boundary (mandatory; see below) |
| `dotnet-testing` | Tests — the `HttpClient` test seam |

**Mandatory error-handling hazards — `System.Text.Json.JsonException` reaches the boundary from two directions and they need opposite handling:**

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

- **Assumption (sender preference):** capability 3 uses `messagingServiceSid` as the primary sender (per the brief's "which to prefer"); `from` is the fallback only when no service is configured. Scheduling (capability 4) *requires* the Messaging Service, so it always uses `messagingServiceSid` and passes `from: null`.
- **Assumption (schedule offset):** "~3 days" is implemented as `DateTimeOffset.UtcNow.AddDays(3)`; adjust to the exact business rule if different.
- **Assumption (BaseUrl scope):** `Twilio:BaseUrl` is applied to `options.Server.Default.Production.BaseUrl` only (messaging/api node). The lookups node (`Default4`, `https://lookups.twilio.com`) is left at its default so lookup traffic is unaffected — matching the brief's "messaging only".
- **UNVERIFIED (live-wire, resolved defensively in the sheet, not blockers):** (a) exact HTTP status when canceling an already-sent message; (b) DateSent range boundary inclusivity; (c) the provider error-body JSON shape; (d) the redaction after-effect on `Body`. Each has a defensive directive in §2c/§2f — no open lookup remains.
- **No capability is unexposed.** Every one of the 10 requested capabilities maps to a concrete operation/field/enum grounded above. No blockers.
