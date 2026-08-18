# Twilio .NET SDK — SMS Notification Integration Plan (eShopOnWeb PublicApi)

SDK: `AsadAli.TwilioSdk` (APIMatic-generated), root namespace `TwilioSdk`, client `TwilioSdkClient`.
Map source commit `51fdf48`. Every fact below is grounded in the bundled SDK map (page cited per row);
the base-URL/server shapes and auth/DI type shapes were confirmed against the pinned SDK source.

All eight touch-points are covered by exactly **two** controllers:
`client.LookupsV2PhoneNumber` (feature 1) and `client.Api20100401Message` (features 2–8).
**No required capability is missing — there is no gap.**

---

## 1. Scope & sequence

1. **Client & DI setup** — register `TwilioSdkClient` in `Program.cs`/composition root via
   `AddTwilioSdkClient`; wire auth + the messaging base URL from config. (governs every step)
2. **Phone validation at registration** — `LookupsV2PhoneNumber.FetchPhoneNumber3`; reject unusable
   numbers, store the returned E.164 canonical form. (feature 1)
3. **Send SMS** — `Api20100401Message.CreateMessage` (order placed / on its way / cancelled). (feature 2)
4. **Schedule a follow-up** — `CreateMessage` with `scheduleType` + `sendAt` + `messagingServiceSid`. (feature 3)
5. **Cancel a scheduled message** — `Api20100401Message.UpdateMessage` with `status = Canceled`. (feature 4)
6. **Fetch delivery outcome** — `Api20100401Message.FetchMessage`. (feature 5)
7. **Redact message content** — `UpdateMessage` with `body = ""` (NOT delete). (feature 6)
8. **Reconciliation list** — `Api20100401Message.ListMessage` filtered by `from` + date range, paged. (feature 7)
9. **Resend** — another `CreateMessage` (feature 8) + app-side idempotency (SDK offers none).
10. **Error boundary** around every call so a failed Twilio call never fails the underlying order operation.

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

### 2a. Namespaces (`using` per referenced type)

| Type | Namespace | `using` |
|---|---|---|
| `TwilioSdkClient`, `TwilioSdkClientOptions`, `ServerOptions`, `AddTwilioSdkClient` (extension) | root | `using TwilioSdk;` |
| Controllers `Api20100401Message`, `LookupsV2PhoneNumber` | `TwilioSdk.Api` | `using TwilioSdk.Api;` |
| Records `ApiV2010AccountMessage`, `ListMessageResponse`, `LookupResponse` | `TwilioSdk.Models` | `using TwilioSdk.Models;` |
| Enums `MessageEnumStatus`, `MessageEnumScheduleType`, `MessageEnumUpdateStatus`, `MessageEnumContentRetention`, `MessageEnumAddressRetention` | `TwilioSdk.Models.Enums` | `using TwilioSdk.Models.Enums;` |
| `BasicAuthCredentials` | `TwilioSdk.Core.Authentication.Basic` | `using TwilioSdk.Core.Authentication.Basic;` |
| `ServerEnvironment`, `DefaultOptions`, `Default4Options` | `TwilioSdk.Servers` | `using TwilioSdk.Servers;` |
| `SdkException<T>` | `TwilioSdk.Core.Exceptions` | `using TwilioSdk.Core.Exceptions;` |
| `RawError` | `TwilioSdk.Core.ErrorResponse` | `using TwilioSdk.Core.ErrorResponse;` |

Child namespaces are **not** imported transitively — add each `using` above separately.

### 2b. Operations

Legend: params marked **(must pass)** are nullable with **no C# default** → you must pass them explicitly
(pass `null` to skip). `accountSid` (and `sid` where present) are required non-nullable path params.
Every in-scope operation is **Case B** (`SdkException<RawError>`), throw-only, **no** `…Result` variant,
**no** SDK auto-pagination.

| # | Op (controller.method) | Signature (params in order) | Request/inputs used | Response envelope → fields read | Error | Map page |
|---|---|---|---|---|---|---|
| 1 | `LookupsV2PhoneNumber.FetchPhoneNumber3` | `(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 15 **(must pass)** params `fields…partnerSubId` | `phoneNumber` = raw input; all 15 optional = `null` (`fields:null` returns the base lookup incl. `valid` + canonical form) | `LookupResponse` (`TwilioSdk.Models`): `PhoneNumber (phone_number): string?` = **provider E.164 canonical form → STORE THIS**; `Valid (valid): bool?` = usable-destination flag → **REJECT unless `Valid == true`**; `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?`; `NationalFormat (national_format): string?`; `CountryCode (country_code): string?` | Case B `SdkException<RawError>` — accessors `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` | operations/LookupsV2PhoneNumber.md; records-4-Li-Me.md |
| 2 | `Api20100401Message.CreateMessage` | `(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 24 **(must pass)** params `statusCallback…contentSid` | `to` = destination E.164 (required, non-null positional); `body` = text; **sender = exactly one of** `from` (explicit From number, e.g. `Twilio:FromNumber`) **or** `messagingServiceSid` (Twilio selects sender from the service's pool). Pass `null` for all other 22. | `ApiV2010AccountMessage` (`TwilioSdk.Models`): `Sid (sid): string?`; `Status (status): MessageEnumStatus?`; `ErrorCode (error_code): int?`; `ErrorMessage (error_message): string?`; `To/From (to/from): string?`; `MessagingServiceSid (messaging_service_sid): string?`; `DateSent (date_sent): string?` | Case B `SdkException<RawError>` | operations/Api20100401Message.md; records-1-Ac-Ca.md |
| 3 | `Api20100401Message.CreateMessage` (scheduling) | same signature as #2 | `to`, `body`; **`messagingServiceSid` REQUIRED** (scheduling is Messaging-Services-only — set `from:null`); `scheduleType: MessageEnumScheduleType.Fixed`; `sendAt: DateTimeOffset?` = future send time (pass a `DateTimeOffset`, prefer UTC; SDK serializes the wire form) | `ApiV2010AccountMessage`: for a scheduled-not-yet-sent message `Status` = `MessageEnumStatus.Scheduled` (wire `scheduled`); `Sid` for later cancel/fetch | Case B `SdkException<RawError>` | operations/Api20100401Message.md; enums.md |
| 4 | `Api20100401Message.UpdateMessage` (cancel) | `(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `body`,`status` **(must pass)** | `sid` = scheduled message Sid; `body: null`; `status: MessageEnumUpdateStatus.Canceled` (only member; wire `canceled`). Provider accepts cancel **only while the message is still `scheduled`** (not once `sending`/`sent`). | `ApiV2010AccountMessage`: `Status` should read `Canceled` on success | Case B `SdkException<RawError>` | operations/Api20100401Message.md; enums.md |
| 5 | `Api20100401Message.FetchMessage` | `(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `sid` | `ApiV2010AccountMessage`: `Status (status): MessageEnumStatus?` (a `StringEnum<MessageEnumStatus>`, **not** a plain string and **not** a C# `enum`); `ErrorCode (error_code): int?`; `ErrorMessage (error_message): string?` | Case B `SdkException<RawError>` | operations/Api20100401Message.md; records-1-Ac-Ca.md |
| 6 | `Api20100401Message.UpdateMessage` (redact) | same signature as #4 | `sid`; `body: ""` (empty string) — redacts the body text at the provider; `status: null`. **Do NOT use `DeleteMessage` here** (that removes the whole record incl. the outcome). | After: `Body` no longer retrievable from provider; the record survives — `Sid`, `Status`, `ErrorCode`, `DateSent`, `DateCreated` remain fetchable via #5 | Case B `SdkException<RawError>` | operations/Api20100401Message.md |
| 7 | `Api20100401Message.ListMessage` | `(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 8 **(must pass)** params `to…pageToken` | `from` = `Twilio:FromNumber` (wire `From`, provider-side filter); **range `from..to`**: `dateSentQueryQuery` = lower bound → wire **`DateSent>`** (on/after); `dateSentQuery` = upper bound → wire **`DateSent<`** (on/before); leave `dateSent` (exact `DateSent`) `null`; `to: null`. Set `pageSize` (e.g. 100). | `ListMessageResponse` (`TwilioSdk.Models`): `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?`; `NextPageUri (next_page_uri): string?`; `Page (page): int?`; `PageSize (page_size): int?`; `FirstPageUri`, `PreviousPageUri`, `Uri`, `Start`, `End` | Case B `SdkException<RawError>` | operations/Api20100401Message.md; records-4-Li-Me.md |
| 8 | `Api20100401Message.CreateMessage` (resend) | same signature as #2 | Identical to #2. **The signature has NO idempotency-key parameter** (`attempt`/`validityPeriod` are not idempotency keys) — enforce idempotency app-side. | `ApiV2010AccountMessage` (new `Sid`) | Case B `SdkException<RawError>` | operations/Api20100401Message.md |

**Pagination (feature 7).** The SDK does **not** auto-paginate (`Pagination: none`, no `…Result`/enumerator).
To cover the whole range, loop: call `ListMessage` with `pageSize` set and advance until the page is empty
or `NextPageUri` is `null`. Advance either by incrementing `page` (0-based `int?`) or by extracting the
`PageToken` query value from `NextPageUri` and passing it as `pageToken`. **Defensive directive:** treat a
`null`/absent `NextPageUri` as end-of-range and stop; do not assume a fixed page count. `UNVERIFIED` — the
exact query key inside `NextPageUri` and whether `DateSent</DateSent>` filter to day- or second-granularity
is provider wire behaviour; parse `NextPageUri` best-effort and pass the whole date range explicitly rather
than relying on inclusive/exclusive edge assumptions.

### 2c. Enum value tables (literal C# member ← wire value)

`MessageEnumStatus` (`TwilioSdk.Models.Enums`, `StringEnum`) — the type of `ApiV2010AccountMessage.Status`:

| C# member | wire |
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

`MessageEnumScheduleType`: `Fixed` ← `fixed` (only member).
`MessageEnumUpdateStatus`: `Canceled` ← `canceled` (only member — the only status `UpdateMessage` accepts).

> `Status` is `StringEnum<MessageEnumStatus>`, not a C# `enum`. Compare with the static members
> (`MessageEnumStatus.Delivered`) or `.FromValue("delivered")`; never `switch` on a bare string literal
> and never assume `.ToString()` equals the wire value. (See MUST-load `dotnet-models`.)

### 2d. Client construction, auth, servers, DI

- **Client**: `new TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)` (root ns `TwilioSdk`).
- **DI**: `services.AddTwilioSdkClient(o => { ... })` (extension in root ns `TwilioSdk`). Confirmed from source:
  it calls `services.AddHttpClient()` and registers `TwilioSdkClient` as a **singleton** built from
  `IHttpClientFactory.CreateClient()` — so the `HttpClient` is factory-owned/long-lived; do not `new` a
  per-request `HttpClient`.
- **Auth** (`TwilioSdkClientOptions.AccountSidAuthToken`, type `BasicAuthCredentials?`): HTTP Basic. Construct
  `new BasicAuthCredentials { Username = <API key SID or AccountSid>, Password = <API key secret or AuthToken> }`
  (both `required`, `init`-only; ns `TwilioSdk.Core.Authentication.Basic`). Set on the options before/while
  building the client. Load `AccountSid`/`AuthToken` from configuration — never hardcode.
- **Environment**: `options.Environment` = `ServerEnvironment.Production` (only member; ns `TwilioSdk.Servers`).
- **Base URL — messaging host only (`Twilio:BaseUrl`)**: set
  `options.Server.Default.Production.BaseUrl = <Twilio:BaseUrl>` when the config value is present; otherwise
  leave the SDK default (`https://api.twilio.com`). `ServerOptions.Default` is a `DefaultOptions`
  (ns `TwilioSdk.Servers`) whose `Production.BaseUrl` governs the **`Default (api)`** host — i.e. **all of the
  `Api20100401Message.*` calls (features 2–8)** and nothing else.
- **Lookups host is SEPARATE and must NOT be redirected by `Twilio:BaseUrl`.** The Lookup call (feature 1)
  runs on the **`Default4 (lookups)`** host, configured by a *different* property
  `options.Server.Default4.Production.BaseUrl` (a `Default4Options`, default `https://lookups.twilio.com`).
  Because it is a distinct property, assigning `Twilio:BaseUrl` to `Server.Default` does **not** touch
  Lookups — leave `Server.Default4` at its default. The SDK client reaches Lookups fine via that built-in
  default. (Confirmed in source: `ServerOptions.Default` vs `ServerOptions.Default4`, distinct
  `ProductionOptions.BaseUrl`.)
- **NuGet**: `dotnet add package AsadAli.TwilioSdk` — install **version-less** (floats to latest); do not pin
  a version from memory. (See Assumptions re: an existing reference already in the repo.)

### 2e. Error boundary (all in-scope ops are Case B)

Every call throws `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` on an error
status. `RawError` exposes **no typed accessors** — read:
`ex.Error.StatusCode` (`HttpStatusCode`), `ex.Error.ReadAsString()`, `ex.Error.ReadAsJson<T>()`,
`ex.Error.ReadAsBytes()`. There is no `TryGet{Operation}…` accessor for these operations.

**Defensive directive (provider error code/message):** the Twilio error body is not modelled by a typed
error here. Read the HTTP status from `StatusCode`; for the provider's own error `code`/`message`, best-effort
`ReadAsJson<T>()` into a small local DTO (Twilio sends `code`, `message`, `more_info`, `status`) and **fall
back to `ReadAsString()` + `StatusCode`** if the shape does not match. `UNVERIFIED` — whether the live error
payload matches that shape can only be confirmed against live traffic; never let a parse failure of the error
body throw out of the boundary. The boundary must ensure a failed Twilio call (send/schedule/etc.) **never**
fails the underlying order operation.

---

## 3. Trap notes (do not implement from these one-liners — load the named skill)

⚠ Step 1 (client & DI) — the `HttpClient`/handler pipeline lifetime, and whether the SDK client wrapper is
singleton vs transient, are not decided by the constructor signature; getting it wrong causes socket
exhaustion or stale DNS. **MUST load `dotnet-client-initialization`** before wiring the client into DI.

⚠ Step 1 (config & base URL / resilience) — which calls retry, what the SDK `Timeout` actually bounds, and
how base-URL/server selection interacts with retries are not visible in the option names. **MUST load
`dotnet-configuration-resilience`** before setting `Server.Default.Production.BaseUrl`, retries, or timeouts.

⚠ Steps 3, 8 (schedule / resend — non-idempotent writes) — whether a `CreateMessage` (POST) that fails at
the transport layer can be re-sent by the SDK's own retry, and therefore whether a "resend" or a scheduled
send can execute more than once, is a resilience-config property, not something the signature reveals. This
is exactly why you must enforce idempotency app-side. **MUST load `dotnet-configuration-resilience`** before
relying on any retry behaviour for these calls.

⚠ Step 2 (auth) — when/where credentials must be set relative to client construction, and how to source them
from configuration rather than hardcoding, are usage rules the property type does not show. **MUST load
`dotnet-authentication`** before wiring `AccountSidAuthToken`.

⚠ Steps 2–8 (calling endpoints) — these operations have many nullable-no-default params that mis-bind in a
positional call; the correct calling convention (named arguments, `ct:`) is a usage rule. **MUST load
`dotnet-calling-endpoints`** before writing the first call.

⚠ Steps 2–8 (models) — `Status` is a `StringEnum<T>` not a C# enum, `sendAt` is a `DateTimeOffset` whose wire
serialization is the SDK's business, and unmodelled JSON fields are dropped on deserialize; how to build and
read these correctly is a usage concern. **MUST load `dotnet-models`** before constructing payloads or mapping
responses to domain types.

⚠ Step 10 (error boundary) — which exception type(s) actually reach the catch, and how a `JsonException` can
bypass or replace an `SdkException`, are not shown by the operation signatures. **MUST load
`dotnet-error-handling`** before writing the boundary (see the mandatory hazard rows below).

⚠ Step 9/testing — the test seam is the `HttpClient` constructor argument; faking the wrong layer produces
tests that assert nothing. **MUST load `dotnet-testing`** before writing integration tests.

---

## 4. REQUIRED READING (load BEFORE implementation — this sheet deliberately omits their contents)

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, HttpClient ownership/lifetime, DI registration |
| `dotnet-authentication` | Step 2 — supplying `AccountSidAuthToken` / `BasicAuthCredentials`, sourcing secrets |
| `dotnet-calling-endpoints` | Steps 2–8 — calling convention, named args, `ct`, request/response shapes |
| `dotnet-models` | Steps 2–8 — `StringEnum` handling, `DateTimeOffset`, dropped-field pitfalls |
| `dotnet-configuration-resilience` | Step 1 + steps 3/8 — base URL, retries/timeouts, non-idempotent-write retry |
| `dotnet-error-handling` | Step 10 — the Case B boundary, `SdkException<RawError>`, `JsonException` traps |
| `dotnet-testing` | Step 9 — faking the `HttpClient` seam |

**Mandatory `dotnet-error-handling` hazard rows — `System.Text.Json.JsonException` reaches the boundary from
two directions and they need opposite handling:**
- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from
  deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the
  integration boundary.
- A **non-2xx** body that does not match its operation's generated error shape throws `JsonException` *while
  the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP
  status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a
  deterministic rejection as an outage, and a caller that retries 5xx retries something that can never
  succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

- **Assumption (plan path):** written to `C:\claude-runs\t4v4ali-task4-plugin-opus48high-002\repo\twilio-plan.md`
  as dictated by the brief.
- **Assumption (feature 1 rejection rule):** "not a usable destination" is taken as `LookupResponse.Valid != true`.
  `Valid` is `bool?`; treat `null` as reject (fail closed). `UNVERIFIED` — that the live Lookup payload always
  populates `Valid`, and whether additional `ValidationErrors` should also gate rejection, can only be
  confirmed against live traffic; code defensively (reject unless `Valid == true`).
- **Assumption (feature 3):** scheduling requires `messagingServiceSid` (Messaging-Services-only, per the
  `MessageEnumScheduleType` doc) and `from` must be omitted for a scheduled create. The app must have a
  configured Messaging Service SID available (not just `Twilio:FromNumber`) to schedule follow-ups.
- **Assumption (auth credentials):** the integration supplies an API key SID + secret (preferred) or
  `AccountSid` + `AuthToken` via configuration; both map onto `BasicAuthCredentials.Username`/`Password`.
- **To verify (not a blocker):** whether an `AsadAli.TwilioSdk` package reference already exists in the
  PublicApi project — the main agent should check the project file and add the version-less package if absent.
- **No capability gap:** all eight touch-points are exposed by the SDK; nothing in scope is missing.
