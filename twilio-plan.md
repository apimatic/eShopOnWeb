# Twilio SMS Integration — Plan & Contract Sheet (C#/.NET, `src/PublicApi`)

Grounded against the bundled Twilio SDK map (source commit `51fdf48`). NuGet package: **`AsadAli.TwilioSdk`**
(install version-less: `dotnet add package AsadAli.TwilioSdk` into `src/PublicApi`). Root namespace `TwilioSdk`.

This SDK is a **broad, low-level generated surface**: SMS "send / schedule / cancel / redact" are all the
**same** `Api20100401Message` controller operations differentiated by which fields you set — there is no
purpose-built "schedule" or "cancel" method. Every fact below is a map/source lookup, not a Twilio-helper-library
memory; the classic `Twilio.Rest.Api.V2010...MessageResource` helper API does **not** exist in this package.

---

## 1. Scope & sequence

| # | Step | Operation(s) used |
|---|------|-------------------|
| 1 | Register the SDK client in DI, wire auth + the messaging base-URL override | `AddTwilioSdkClient` / `TwilioSdkClient` ctor |
| 2 | Validate a destination number & get canonical E.164 form | `LookupsV2PhoneNumber.FetchPhoneNumber3` |
| 3 | Send an SMS (via `FromNumber` or `MessagingServiceSid`); capture SID + status | `Api20100401Message.CreateMessage` |
| 4 | Schedule an SMS for future delivery | `Api20100401Message.CreateMessage` (+ `messagingServiceSid`, `sendAt`, `scheduleType`) |
| 5 | Cancel a scheduled-but-unsent message | `Api20100401Message.UpdateMessage` (`status = canceled`) |
| 6 | Fetch a single message's delivery status by SID | `Api20100401Message.FetchMessage` |
| 7 | List messages for reconciliation (filter by From + DateSent range) | `Api20100401Message.ListMessage` |
| 8 | Redact a sent message's body from Twilio (keep the record/outcome) | `Api20100401Message.UpdateMessage` (`body = ""`) |
| 9 | Error boundary translating SDK exceptions so a send failure never fails the order | `SdkException<RawError>` |

Error-handling wraps **every** call in steps 2–8. All message + lookup operations are **Case B** (raw error).

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The
> cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from
> that type's own map row, never from where a neighbouring type sits. Enums, unions, auth, server and
> client-config types are spread across different child namespaces, and two types configured side by side
> in the same options object routinely live in different ones. Dropping a type to the root or to `.Models`
> makes the implementer guess the wrong `using`, and the build breaks.

### 2a. Namespaces (`using` directives) — every SDK type this integration touches

| Type | Namespace |
|---|---|
| `TwilioSdkClient`, `TwilioSdkClientOptions`, `ServerOptions`, `ServiceCollectionExtensions` (`AddTwilioSdkClient`) | `TwilioSdk` |
| Controllers (`Api20100401Message`, `LookupsV2PhoneNumber`) | `TwilioSdk.Api` |
| Records (`ApiV2010AccountMessage`, `ListMessageResponse`, `LookupResponse`) | `TwilioSdk.Models` |
| Enums (`MessageEnumStatus`, `MessageEnumScheduleType`, `MessageEnumUpdateStatus`) | `TwilioSdk.Models.Enums` |
| `BasicAuthCredentials` | `TwilioSdk.Core.Authentication.Basic` |
| `SdkException<TError>` | `TwilioSdk.Core.Exceptions` |
| `RawError` | `TwilioSdk.Core.ErrorResponse` |
| `ServerEnvironment`, `DefaultOptions`, `Default4Options` (server-node options) | `TwilioSdk.Servers` |
| `RetryOptions`, `RetryAttempt` | `TwilioSdk.Core.Configuration` |

Reminder: C# does **not** import child namespaces transitively — add each `using` above separately or you
get `CS0103`/`CS0246`. (Source-verified: `BasicAuthCredentials` really is under `...Core.Authentication.Basic`,
`RawError` under `...Core.ErrorResponse`, and `SdkException` under `...Core.Exceptions` — three different
child namespaces used side by side in the error boundary.)

### 2b. Client construction, auth, servers/base-URL (Step 1)

- **Client class:** `TwilioSdk.TwilioSdkClient`. Only constructor: `TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)`.
  (`sdk-map.md` → *Getting a client*.)
- **Options class:** `TwilioSdk.TwilioSdkClientOptions` with properties `Environment: ServerEnvironment`,
  `Retry: RetryOptions`, `Logging: LoggingOptions`, `Server: ServerOptions`, `AccountSidAuthToken: BasicAuthCredentials?`.
  (`sdk-map.md` → *client-options*.)
- **Auth (Basic).** Set `options.AccountSidAuthToken = new BasicAuthCredentials { Username = <Twilio:AccountSid>, Password = <Twilio:AuthToken> }`.
  `BasicAuthCredentials` has **`required string Username { get; init; }`** and **`required string Password { get; init; }`** — object-initializer only, no constructor.
  Per the source XML docs, Username/Password may be an API key SID + secret; AccountSid + AuthToken is the account-level pair and is what this app is configured with. (`sdk-map.md` → *Servers & auth*; source `Core/Authentication/Basic/BasicAuthCredentials.cs`.)
- **Environment.** `options.Environment = ServerEnvironment.Production` — `Production` is the **only** member. (`sdk-map.md` → *Servers & auth*; source `Servers/ServerEnvironment.cs`.)
- **Base URL for the messaging API (the `Twilio:BaseUrl` override).**
  `options.Server` is a `ServerOptions` holding 15 independent server-node option objects (`Default` … `Default14`),
  **one per host the SDK talks to**. The message operations run on the node the map labels **"Default (api)"** = `ServerOptions.Default`
  (default `https://api.twilio.com`); Lookups run on **"Default4 (lookups)"** = `ServerOptions.Default4` (default `https://lookups.twilio.com`).
  To apply `Twilio:BaseUrl` to messaging **only**, set:
  `options.Server.Default.Production.BaseUrl = <Twilio:BaseUrl>` (type `DefaultOptions.ProductionOptions.BaseUrl`, a `string`).
  (Source `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Servers/Default4Options.cs`.)
  - **Scope of the override — CONFIRMED from source:** setting `Server.Default.Production.BaseUrl` re-points the
    **"Default (api)" node only** — i.e. every `Api20100401*` operation whose HTTP row reads "Default (api)",
    which includes **all** message operations (Create/Fetch/List/Update/Delete). It does **NOT** affect the whole
    client and does **NOT** affect Lookup, which resolves through the separate `Server.Default4` node
    (`lookups.twilio.com`). So within this integration `Twilio:BaseUrl` governs exactly the SMS calls and leaves
    validation untouched — do not apply it to `Default4`.
- **DI registration.** `services.AddTwilioSdkClient(o => { /* set o.AccountSidAuthToken, o.Environment, o.Server.Default.Production.BaseUrl */ })`
  (extension on `IServiceCollection`, namespace `TwilioSdk`). Source-confirmed shape: it calls `services.AddHttpClient()`,
  then registers `TwilioSdkClient` as a **Singleton** built from `IHttpClientFactory.CreateClient()`. HttpClient
  ownership/lifetime nuances are a companion-skill concern — see the Step-1 trap note. (Source `ServiceCollectionExtensions.cs`.)

### 2c. Operations table

| Step / op | Method signature (params in order — all mid-list nullables have **no default → pass explicitly**, `null` to skip) | Request fields to set (wire ← C#) | Response type + fields the integration reads | Error case + accessors | Pagination |
|---|---|---|---|---|---|
| **3/4 Send & Schedule** — `client.Api20100401Message.CreateMessage` | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid` = `Twilio:AccountSid` (path); `To`←`to` (E.164 destination); `Body`←`body` (text). **Send-now via number:** `From`←`from` = `Twilio:FromNumber`. **Send-now via service:** `MessagingServiceSid`←`messagingServiceSid` = `Twilio:MessagingServiceSid` (leave `from` null). **Schedule:** set `MessagingServiceSid`←`messagingServiceSid` (**required for scheduling**), `SendAt`←`sendAt` (`DateTimeOffset`, future time), `ScheduleType`←`scheduleType` = `MessageEnumScheduleType.Fixed`, and leave `from` null. | `ApiV2010AccountMessage` — read `Sid (sid): string?` (provider message SID), `Status (status): MessageEnumStatus?` (a scheduled message comes back `Scheduled`), `ErrorCode (error_code): int?`, `ErrorMessage (error_message): string?` | Case **B** — `SdkException<RawError>`; `.Error.StatusCode`, `.Error.ReadAsString()`, `.Error.ReadAsJson<T>()`, `.Error.ReadAsBytes()` | none |
| **5 Cancel scheduled / 8 Redact body** — `client.Api20100401Message.UpdateMessage` | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid`, `sid` (path, the message SID). **Cancel (step 5):** `status`←`status` = `MessageEnumUpdateStatus.Canceled`, `body` = `null`. **Redact (step 8):** `body`←`body` = `""` (empty string — redacts the stored text; map note: "used to redact Message `body` text"), `status` = `null`. | `ApiV2010AccountMessage` — read `Sid`, `Status`, `Body` (empty after redaction) | Case **B** — as above | none |
| **6 Fetch status** — `client.Api20100401Message.FetchMessage` | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid`, `sid` (path) | `ApiV2010AccountMessage` — read `Status (status): MessageEnumStatus?`, `ErrorCode`, `ErrorMessage`, `DateSent (date_sent): string?` | Case **B** — as above | none |
| **7 List / reconcile** — `client.Api20100401Message.ListMessage` | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid` (path); **`From`←`from`** = the sending number (server-side filter — do **not** filter client-side); **date range:** `DateSent<`←`dateSentQuery` = **upper** bound (on/before), `DateSent>`←`dateSentQueryQuery` = **lower** bound (on/after). (`DateSent`←`dateSent` is the exact-day equality filter — leave `null` when using a range.) `pageSize`←`PageSize`, `page`←`Page`, `pageToken`←`PageToken`. | `ListMessageResponse` — read `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?`; page controls `Page (page): int?`, `PageSize (page_size): int?`, `NextPageUri (next_page_uri): string?`, `PreviousPageUri`, `FirstPageUri`, `Uri`, `Start`, `End` | Case **B** — as above | **Manual only.** No auto-pager / `…Result` variant. One call = one page; advance via `page`/`pageToken`, stop when `NextPageUri` is null. |
| **8-alt Delete whole record** — `client.Api20100401Message.DeleteMessage` | `DeleteMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid`, `sid` (path) | `void` (Task). **Removes the ENTIRE Message resource** — the record that it was sent and its outcome are gone from Twilio too. **Do NOT use for step 8** (which must keep the sent-record/outcome); use `UpdateMessage` with `body=""` instead. Listed only to name the contrast. | Case **B** — as above | none |
| **2 Validate + canonicalize** — `client.LookupsV2PhoneNumber.FetchPhoneNumber3` | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `phoneNumber` (path — the raw number to validate). For a plain validity + E.164 canonicalization, pass `fields = null` and all remaining params `null`. Supply `countryCode` (ISO-2, e.g. `"US"`) when the input is in national (non-E.164) format so Twilio can canonicalize it. | `LookupResponse` — read `Valid (valid): bool?` (usable destination → treat `false`/null as reject), `PhoneNumber (phone_number): string?` (**Twilio's canonical E.164 form — store this**), `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?` (rejection reasons) | Case **B** — as above | none |

**Runs on a different host — NOT governed by `Twilio:BaseUrl`:** `FetchPhoneNumber3` resolves through the
`Default4` ("lookups") server node (`lookups.twilio.com`), a separate host from messaging's `Default` ("api") node.
The `Twilio:BaseUrl` override (§2b) must be applied only to `Server.Default`, so validation is unaffected by it.
(`LookupsV2PhoneNumber.md`; source `Servers/Default4Options.cs`.)

### 2d. Enum value tables (literal C# member ↔ wire value) — namespace `TwilioSdk.Models.Enums`

These are `StringEnum<T>`, **not** C# enums: build with `MessageEnumStatus.FromValue("delivered")` or use the
static members below (member name, not the wire string). (`map/models/enums.md`.)

**`MessageEnumStatus`** — delivery outcome read from `ApiV2010AccountMessage.Status` (steps 3/4/6):

| C# member | wire | | C# member | wire |
|---|---|---|---|---|
| `Queued` | `queued` | | `Undelivered` | `undelivered` |
| `Sending` | `sending` | | `Receiving` | `receiving` |
| `Sent` | `sent` | | `Received` | `received` |
| `Failed` | `failed` | | `Accepted` | `accepted` |
| `Delivered` | `delivered` | | `Scheduled` | `scheduled` |
| `Read` | `read` | | `PartiallyDelivered` | `partially_delivered` |
| `Canceled` | `canceled` | | | |

**`MessageEnumScheduleType`** (step 4, `scheduleType`): `Fixed` ← `fixed` — **the only member**.

**`MessageEnumUpdateStatus`** (step 5 cancel, `status`): `Canceled` ← `canceled` — **the only member**.
(Note: distinct type from `MessageEnumStatus`; `UpdateMessage`'s `status` param takes `MessageEnumUpdateStatus?`.)

---

## 3. Trap notes (load the named skill BEFORE writing that step — do not implement from the note alone)

⚠ **Step 1 (client registration & DI)** — the source shows `AddTwilioSdkClient` registering the client as a
singleton over `IHttpClientFactory`, but whether that lifetime, the handler pipeline, and reuse are correct for
your ASP.NET Core host (vs. rebuilding a client per request) is exactly what a signature/registration line does
not settle. **MUST load `dotnet-client-initialization`** before wiring the client into DI.

⚠ **Step 1 (auth wiring)** — where and when credentials must be set relative to client construction, and how to
source them from `Twilio:*` config rather than hardcoding, are not decided by the `BasicAuthCredentials` shape
alone. **MUST load `dotnet-authentication`** before setting `AccountSidAuthToken`.

⚠ **Step 1 (base URL / server node & resilience)** — the `Retry`/`Timeout` options do **not** bound a whole call
and are **not** the timeout on the `HttpClient` you register; and which requests are re-sent on failure (relevant
because `CreateMessage` is a non-idempotent `POST`) is not visible in the option names. Whether a failed send can
silently execute twice bears directly on requirement 9 ("a send failure never fails the order"). **MUST load
`dotnet-configuration-resilience`** before tuning retries/timeouts or finalizing the base-URL override.

⚠ **Steps 2–8 (calling ops / building requests)** — many optional params have no C# default and mis-bind in a
positional call; the correct call style for these long generated signatures is a companion-skill concern. **MUST
load `dotnet-calling-endpoints`** before the first `client.*` call.

⚠ **Steps 2–8 (models & enums)** — `MessageEnumStatus`/`MessageEnumScheduleType`/`MessageEnumUpdateStatus` are
`StringEnum<T>`, not C# enums, and JSON fields the SDK doesn't model are dropped on deserialize — how to
construct/compare these safely is not shown by the field list. **MUST load `dotnet-models`** before building
request payloads or mapping `ApiV2010AccountMessage`/`LookupResponse` onto your domain types.

⚠ **Step 9 (error boundary)** — how to read status + provider error body **safely** off `SdkException<RawError>`,
and which catch shapes are silently wrong, is precisely what the raw-error accessors do not tell you. **MUST load
`dotnet-error-handling`** before writing the try/catch that keeps a send failure from failing the order.

⚠ **Testing** — the seam to fake is the `HttpClient` constructor argument; match the project's existing test
framework/assertion style. **MUST load `dotnet-testing`** before writing integration tests.

---

## 4. Reading the provider error code/message (requirement 9) — defensive directive

All in-scope operations are **Case B**: on any error status they throw
`TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`. There is **no** typed
`{Operation}Error` and **no** `TryGet…` accessor for these operations — the only accessors are on `RawError`:
`StatusCode: HttpStatusCode`, `ReadAsString(): string`, `ReadAsJson<T>(): T?`, `ReadAsBytes(): ReadOnlyMemory<byte>`.

- **HTTP status** to translate to your API: `ex.Error.StatusCode` (reliable, source-declared).
- **Provider error code / message** (Twilio's `code`, `message`, `more_info`, `status` JSON): the SDK does not
  model this body for these operations, so it must be extracted best-effort via `ex.Error.ReadAsJson<T>()` into a
  small local DTO you define. **`UNVERIFIED`** — whether the live wire body matches that shape can only be
  confirmed against real traffic. **Directive:** attempt `ReadAsJson` into your DTO; if it returns null or throws,
  **fall back to `ex.Error.ReadAsString()`** (and then the generic message) rather than assuming the fields are
  present. Never let this extraction throw out of the catch.
- Requirement "a send failure never fails the order": the catch around `CreateMessage`/`UpdateMessage` must
  swallow-and-record (log + persist outcome) rather than propagate, so the order operation completes. The exact
  boundary mechanics (which exceptions actually reach the catch, including the `JsonException` cases in REQUIRED
  READING) come from `dotnet-error-handling`.

---

## 5. REQUIRED READING — load every skill below BEFORE implementation starts

This sheet deliberately does **not** carry these skills' contents (defaults, worked examples, boundary cases);
the trap notes name a hazard and its cost only. Load each before coding the step it governs.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, HttpClient ownership/lifetime, DI registration shape |
| `dotnet-authentication` | Step 1 — supplying `BasicAuthCredentials` from config, when to set credentials |
| `dotnet-configuration-resilience` | Step 1 — base-URL/server selection, retries/timeouts, what `Timeout` bounds, which calls re-send |
| `dotnet-calling-endpoints` | Steps 2–8 — named-argument binding for the long generated signatures, async/cancellation |
| `dotnet-models` | Steps 2–8 — `StringEnum<T>` construction/compare, required members, wire-name mapping, dropped fields |
| `dotnet-error-handling` | Step 9 — the exception boundary, reading status/body safely off `RawError`, catch-ladder traps |
| `dotnet-testing` | Tests — faking the `HttpClient` seam, covering error/edge paths |

**Two hazards that MUST shape the error boundary from the first version (`System.Text.Json.JsonException`
reaches the boundary from two directions and they need opposite handling):**

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from
  deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the
  integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the
  `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx
  then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can
  never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 6. Assumptions & Blockers

- **Assumption:** Basic-auth uses `Twilio:AccountSid` as `Username` and `Twilio:AuthToken` as `Password`. The SDK
  also accepts an API-key SID/secret pair here, but the app is configured with an account SID + auth token, so
  those are used. No blocker.
- **Assumption:** For step 8 "redact body but keep the record/outcome" the intended operation is `UpdateMessage`
  with `body = ""` (the map's own note flags `UpdateMessage` as the body-redaction path); `DeleteMessage` is
  deliberately NOT used because it removes the whole record. If the requirement actually wants the entire record
  gone, switch to `DeleteMessage`.
- **Assumption:** Scheduling (step 4) uses `MessagingServiceSid` (required by Twilio for scheduled sends) rather
  than `FromNumber`; `Twilio:MessagingServiceSid` is present in config for this reason.
- **`UNVERIFIED` (live-traffic only):** the exact JSON shape of the provider error body (Twilio `code`/`message`/
  `more_info`) is not modeled by the SDK for these Case-B operations — handled by the defensive directive in §4.
- **No SDK gaps found.** Every requested capability (1–9) maps to a real operation/field/enum in this SDK; nothing
  had to be invented and nothing is missing. No blockers to implementation.
