# Twilio .NET SDK — Order-Notification SMS Integration (eShopOnWeb)

Contract sheet + plan for adding SMS order notifications via the APIMatic-generated Twilio .NET
SDK (`AsadAli.TwilioSdk`, root namespace `TwilioSdk`). Every fact below is grounded in the bundled
SDK map (page cited per row); the server-node / base-URL override facts were confirmed from the SDK
source (`Servers/ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Servers/Default4Options.cs`).

---

## 1. Scope & sequence

| # | Step | Operation(s) | Controller |
|---|---|---|---|
| 0 | Register SDK client in DI + bind config | `AddTwilioSdkClient` | (client bootstrap) |
| 1 | Validate + canonicalize a phone number at registration | `FetchPhoneNumber3` | `client.LookupsV2PhoneNumber` |
| 2 | Send an SMS immediately | `CreateMessage` | `client.Api20100401Message` |
| 3 | Schedule an SMS for a future time (provider-side) | `CreateMessage` (+ `scheduleType`/`sendAt`) | `client.Api20100401Message` |
| 4 | Cancel a not-yet-sent scheduled message | `UpdateMessage` (`status = canceled`) | `client.Api20100401Message` |
| 5 | Fetch a single message's delivery outcome | `FetchMessage` | `client.Api20100401Message` |
| 6 | Redact message body (keep metadata) OR delete resource | `UpdateMessage` (`body=""`) / `DeleteMessage` | `client.Api20100401Message` |
| 7 | List messages for reconciliation (From + date range) | `ListMessage` | `client.Api20100401Message` |

All seven operations are **Case B** error-wise (`SdkException<RawError>`) — there are **no** typed
error accessors. See CONTRACT SHEET › Error boundary.

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier.
> The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never
> `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each
> one from that type's own map row, never from where a neighbouring type sits. Enums, unions, auth,
> server and client-config types are spread across different child namespaces, and two types
> configured side by side in the same options object routinely live in different ones. Dropping a
> type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build
> breaks.

### 2a. Namespaces (`using` per referenced type)

| Type(s) | Namespace |
|---|---|
| `TwilioSdkClient`, `TwilioSdkClientOptions`, `ServerOptions` | `TwilioSdk` |
| `ServerEnvironment`, `DefaultOptions`, `Default4Options` | `TwilioSdk.Servers` |
| `RetryOptions`, `LoggingOptions` | `TwilioSdk.Core.Configuration` |
| `BasicAuthCredentials` | `TwilioSdk.Core.Authentication.Basic` |
| Records: `ApiV2010AccountMessage`, `LookupResponse`, `ListMessageResponse`, `ValidationError` | `TwilioSdk.Models` |
| Enums: `MessageEnumStatus`, `MessageEnumScheduleType`, `MessageEnumUpdateStatus`, `MessageEnumContentRetention`, `MessageEnumAddressRetention`, `MessageEnumTrafficType`, `MessageEnumRiskCheck` | `TwilioSdk.Models.Enums` |
| `SdkException<T>` (path `Core/Exceptions/`) | `TwilioSdk.Core.Exceptions` |
| `RawError` (path `Core/ErrorResponse/`) | `TwilioSdk.Core.ErrorResponse` |
| Controllers (`Api20100401Message`, `LookupsV2PhoneNumber`) are properties on `client` — no direct `using` needed to call them | (`TwilioSdk.Api`) |

`RequestOptions?` is an optional trailing param (`= null`) on every operation; the integration
does not need to construct it. Leave it defaulted.

### 2b. Operations

| Step | Controller.Method — signature (params in order) | Request fields of note | Response envelope → fields read | Error | Pagination | Map page |
|---|---|---|---|---|---|---|
| 1 Lookup | `client.LookupsV2PhoneNumber.FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `phoneNumber` = raw caller input (path param, required). All 15 remaining are nullable-no-default → **pass `null` explicitly**. Pass `null` for `fields` (basic validation needs no add-on packages). | Returns **`LookupResponse`** (flat, no envelope wrapper). Read: `Valid (valid): bool?` (validity flag), `PhoneNumber (phone_number): string?` (**provider canonical E.164 form — store this, not the raw input**), `NationalFormat (national_format): string?`, `CountryCode (country_code): string?`, `CallingCountryCode (calling_country_code): string?`, `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?` | `SdkException<RawError>` — **Case B** | none | operations/LookupsV2PhoneNumber.md · records-4-Li-Me.md |
| 2 Send | `client.Api20100401Message.CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | Required path: `accountSid`. Required query: `to`. **Sender: supply EITHER `from` (your `Twilio:FromNumber`) OR `messagingServiceSid` — both are optional params; provide exactly one.** Body text: `body`. All 24 middle params are nullable-no-default → **pass `null` for every one you don't use** (call with named args). | Returns **`ApiV2010AccountMessage`** (flat). Read: `Sid (sid): string?` (message SID), `Status (status): MessageEnumStatus?` (delivery outcome), `ErrorCode (error_code): int?`, `ErrorMessage (error_message): string?`, `To (to): string?`, `From (from): string?`, `DateSent (date_sent): string?` | `SdkException<RawError>` — **Case B** | none | operations/Api20100401Message.md · records-1-Ac-Ca.md |
| 3 Schedule | *same as Step 2* — set `scheduleType: MessageEnumScheduleType.Fixed`, `sendAt: <DateTimeOffset future>`, and `messagingServiceSid: <Twilio:MessagingServiceSid>`; pass `from: null`. | Scheduling requires the **Messaging Service** path: set `messagingServiceSid` (NOT `from`), `scheduleType = Fixed`, `sendAt`. The only `MessageEnumScheduleType` value is `Fixed (fixed)`. | Returns `ApiV2010AccountMessage`; a scheduled message comes back with `Status = Scheduled (scheduled)`. | `SdkException<RawError>` — **Case B** | none | operations/Api20100401Message.md · enums.md L199 |
| 4 Cancel | `client.Api20100401Message.UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` — call with `body: null, status: MessageEnumUpdateStatus.Canceled`. | To cancel a not-yet-sent message set `status = Canceled`. `MessageEnumUpdateStatus` has exactly one value: `Canceled (canceled)`. | Returns `ApiV2010AccountMessage`; on success `Status` becomes `Canceled (canceled)`. | `SdkException<RawError>` — **Case B** | none | operations/Api20100401Message.md · enums.md L202 |
| 5 Fetch | `client.Api20100401Message.FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | Path: `accountSid`, `sid`. | Returns `ApiV2010AccountMessage`; read `Status (status): MessageEnumStatus?`, `ErrorCode`, `ErrorMessage`, `Price`, `DateSent`. | `SdkException<RawError>` — **Case B** | none | operations/Api20100401Message.md · records-1-Ac-Ca.md |
| 6a Redact | `client.Api20100401Message.UpdateMessage(accountSid, sid, body: "", status: null)` | Updating `body` to an **empty string** redacts the message text at the provider. | Returns `ApiV2010AccountMessage`; the resource and its metadata (`Sid`, `Status`, `DateSent`, `ErrorCode`, …) **survive**; only `Body` is cleared. **Use this to keep status/metadata but drop the text.** | `SdkException<RawError>` — **Case B** | none | operations/Api20100401Message.md (Notes: "used to redact Message body text") |
| 6b Delete | `client.Api20100401Message.DeleteMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | — | Returns **`void` (Task)**. Removes the **entire** Message resource, metadata included — the record that a message was sent is gone. **Do NOT use this when you must retain status/metadata.** | `SdkException<RawError>` — **Case B** | none | operations/Api20100401Message.md |
| 7 List | `client.Api20100401Message.ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` | **Server-side From filter:** `from` (wire `From`) = your `Twilio:FromNumber`. **Date range (see trap ⚠-7 for direction):** `dateSentQueryQuery` → wire `DateSent>` = sent **on/after** range start; `dateSentQuery` → wire `DateSent<` = sent **on/before** range end; `dateSent` → wire `DateSent` = exact. Pass unused filters as `null`; call with named args. `pageSize` (wire `PageSize`), `page` (wire `Page`), `pageToken` (wire `PageToken`) drive paging. | Returns **`ListMessageResponse`** (envelope). Read: `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?` (the page's items), `NextPageUri (next_page_uri): string?`, `Page (page): int?`, `PageSize (page_size): int?`, `FirstPageUri`, `PreviousPageUri`, `Uri`. | `SdkException<RawError>` — **Case B** | Map: "none (only `page`, no `perPage`)" — **no auto-pager**; iterate manually (see ⚠-7). | operations/Api20100401Message.md · records-4-Li-Me.md |

### 2c. Enum value tables (literal C# member ← wire value)

`MessageEnumStatus` (Step 5 delivery outcome; source `Models/Enums/MessageEnumStatus.cs`, enums.md L200):

| C# member | wire |
|---|---|
| `MessageEnumStatus.Queued` | `queued` |
| `MessageEnumStatus.Sending` | `sending` |
| `MessageEnumStatus.Sent` | `sent` |
| `MessageEnumStatus.Failed` | `failed` |
| `MessageEnumStatus.Delivered` | `delivered` |
| `MessageEnumStatus.Undelivered` | `undelivered` |
| `MessageEnumStatus.Receiving` | `receiving` |
| `MessageEnumStatus.Received` | `received` |
| `MessageEnumStatus.Accepted` | `accepted` |
| `MessageEnumStatus.Scheduled` | `scheduled` |
| `MessageEnumStatus.Read` | `read` |
| `MessageEnumStatus.PartiallyDelivered` | `partially_delivered` |
| `MessageEnumStatus.Canceled` | `canceled` |

`MessageEnumScheduleType` (Step 3): only `MessageEnumScheduleType.Fixed` ← `fixed` (enums.md L199).
`MessageEnumUpdateStatus` (Step 4): only `MessageEnumUpdateStatus.Canceled` ← `canceled` (enums.md L202).

> Enums are `StringEnum<T>`, **not** C# enums — use the static members above, or
> `MessageEnumStatus.FromValue("delivered")`. Comparisons/switches must use the members, not
> string literals. (Load `dotnet-models` — trap ⚠-M.)

### 2d. Client construction / auth / server (base-URL) contract

**Client & options** (map "Getting a client"; `TwilioSdkClient.cs`, `TwilioSdkClientOptions.cs`):
- Constructor: `new TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)`.
- `TwilioSdkClientOptions` members: `Environment: ServerEnvironment`, `Retry: RetryOptions`,
  `Logging: LoggingOptions`, `Server: ServerOptions`, `AccountSidAuthToken: BasicAuthCredentials?`.
- `Environment`: `ServerEnvironment.Production` (only member; `ServerEnvironment.Default()` also returns Production).
- DI: `services.AddTwilioSdkClient(Action<TwilioSdkClientOptions>? configure = null)` (returns
  `IServiceCollection`; `ServiceCollectionExtensions.cs`). Configure credentials/server inside the callback.

**Auth** (map *Servers & auth*; `Core/Authentication/Basic/BasicAuthCredentials.cs`) — HTTP Basic:
- `options.AccountSidAuthToken = new BasicAuthCredentials { Username = <sid>, Password = <secret> }`.
- `BasicAuthCredentials` has two `required` members: `Username` and `Password`.
- Bind `Twilio:AccountSid` → `Username`, `Twilio:AuthToken` → `Password`. (Twilio also accepts an
  API-key SID/secret pair here; the config keys map onto Username/Password identically.)

**Server / base-URL override — CRITICAL (source: `Servers/ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Servers/Default4Options.cs`):**
- `options.Server` is a `ServerOptions` holding **15 independent server nodes** (`Default` …
  `Default14`), each `X.Production.BaseUrl`.
- **Messaging API** (`Api20100401Message.*`, HTTP tagged "Default (api)") resolves through the
  **`Default`** node → `options.Server.Default.Production.BaseUrl` (source default
  `https://api.twilio.com`).
- **Lookup** (`LookupsV2PhoneNumber.FetchPhoneNumber3`, HTTP tagged "Default4 (lookups)") resolves
  through the **`Default4`** node → `options.Server.Default4.Production.BaseUrl` (source default
  `https://lookups.twilio.com`) — **a different host.**
- Therefore bind `Twilio:BaseUrl` (when present) **ONLY** to
  `options.Server.Default.Production.BaseUrl`. **Never** assign it to `Default4` (lookups): the
  lookup is a different Twilio host and must keep its own base URL. Setting the messaging override
  does not touch `Default4`, so the lookup call is unaffected — exactly the required behavior. If
  `Twilio:BaseUrl` is unset, leave both nodes at their source defaults.

---

## 3. Trap notes (name the hazard; load the skill to resolve it)

⚠-0 **Step 0 (client registration).** The `HttpClient`/handler pipeline the client is built on must
be long-lived and shared, not rebuilt per request; getting the lifetime wrong (or newing an
`HttpClient` per send) has consequences the constructor signature does not reveal. **MUST load
`dotnet-client-initialization`** before wiring `AddTwilioSdkClient` / the client factory.

⚠-A **Step 0/all (auth).** Whether credentials must be set before the client is constructed vs. in
the DI callback, and how they flow from configuration, is not visible in the signature. **MUST load
`dotnet-authentication`** before setting `AccountSidAuthToken`.

⚠-R **Step 0/2/3 (retries & timeouts).** `RetryOptions.HttpMethodsToRetry` gates only the *status*
trigger — a *transport failure* is retried on every verb, `CreateMessage` (POST) included, so
whether a failed send can be silently re-sent (a duplicate SMS to a shopper) is a real question the
option names do not answer; likewise what `Timeout` actually bounds. **MUST load
`dotnet-configuration-resilience`** before tuning retries/timeouts or the base URL.

⚠-7 **Step 7 (list pagination + filter direction).** Two hazards: (a) `ListMessage` exposes **no
auto-pager** (map: pagination "none"), so how you walk the whole date range across pages — `page`
vs. `NextPageUri`/`pageToken` — is something you must decide and wire yourself; getting it wrong
silently truncates a reconciliation run. (b) The date-range parameter names are non-obvious
(`dateSentQuery` → wire `DateSent<` = on/before; `dateSentQueryQuery` → wire `DateSent>` =
on/after) — verify the direction against the CONTRACT SHEET row before binding, and confirm paging
semantics. **MUST load `dotnet-configuration-resilience`** (pagination) and
`dotnet-calling-endpoints` (named-argument binding of the optional filters).

⚠-C **Steps 1–7 (calling / named args).** Every operation has many nullable-no-default params that
mis-bind in a positional call; whether an omitted filter must be passed as `null` and how optional
params bind is a call-site trap. **MUST load `dotnet-calling-endpoints`** before the first call.

⚠-M **Steps 2–6 (models & enums).** `MessageEnumStatus` etc. are `StringEnum<T>`, not C# enums, and
records silently drop unmodeled JSON on deserialize — how to construct/compare them safely is not in
the signature. **MUST load `dotnet-models`** before building requests or mapping responses.

⚠-E **Steps 1–7 (error boundary).** All seven ops are Case B (`SdkException<RawError>`) with no
typed accessors — how to read HTTP status and the Twilio error `code`/`message` safely from the raw
body, and which catch shapes silently swallow or mis-map errors, is exactly what the boundary gets
wrong when written from the signature alone. **MUST load `dotnet-error-handling`** before writing
any try/catch.

⚠-T **Testing.** The `HttpClient` constructor argument is the test seam. **MUST load
`dotnet-testing`** before stubbing the SDK.

---

## 4. Error boundary — contract facts (resolve inline; mechanics live in the skill)

All seven in-scope operations throw **`SdkException<RawError>` (Case B)** — confirmed on every
operation's map row. There is **no typed `{Operation}Error`** and **no `TryGet…` accessor** for
these operations. Read the failure via `RawError` (map *Error-handling model*; `Core/ErrorResponse/RawError.cs`):

- HTTP status: `ex.Error.StatusCode` (`System.Net.Http`→ `HttpStatusCode`).
- Twilio error code + message: **not surfaced as typed members** — they live in the raw JSON body.
  Read with `ex.Error.ReadAsJson<T>()` into a small DTO shaped like the Twilio error body
  (`code`, `message`, `more_info`, `status`), or fall back to `ex.Error.ReadAsString()` /
  `ex.Error.ReadAsBytes()`.
- **`UNVERIFIED` (live-wire): the exact JSON field set of the Twilio error body is not modeled by
  this SDK for these Case-B operations.** Directive: deserialize best-effort into your own DTO,
  and if a field is absent fall back to `StatusCode` + `ReadAsString()` — never assume `code`/
  `message` are present. Do not parse `ex.ToString()` when `ReadAsJson`/`ReadAsString` exist.
- **Never log the auth token or the shopper's phone number.** When surfacing/logging an error, emit
  `StatusCode` + Twilio `code` only; scrub `To`/`From`/`PhoneNumber` and never echo credentials.
- Step-1 validation directive: reject a number when the lookup returns `Valid == false` **or**
  `Valid == null` (treat "unknown" as not-usable), **and** treat a thrown `SdkException<RawError>`
  (e.g. 404 on an unparseable number) as "reject at registration" — do not let it escape as a 500.
- Step-3 lead-time directive: the SDK carries **no** min/max `sendAt` bound in the map or source —
  it just forwards `SendAt`. Any out-of-range schedule window is enforced provider-side and comes
  back as a thrown `SdkException<RawError>`; validate/handle it there. **`UNVERIFIED`: specific
  numeric lead-time limits are a provider rule, not an SDK contract — do not hard-code them from
  memory; surface the provider's rejection.**

---

## 5. REQUIRED READING (load BEFORE implementation starts)

This sheet deliberately does **not** carry these skills' contents — load each one at its step.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 0 — client construction, HttpClient ownership/lifetime, DI registration (⚠-0) |
| `dotnet-authentication` | Step 0/all — supplying `BasicAuthCredentials`, when credentials are set (⚠-A) |
| `dotnet-configuration-resilience` | Step 0/2/3/7 — retries & the POST-resend hazard, timeouts, base-URL/server override, list pagination (⚠-R, ⚠-7) |
| `dotnet-calling-endpoints` | Steps 1–7 — named-arg binding, must-pass-explicitly nulls (⚠-C, ⚠-7) |
| `dotnet-models` | Steps 2–6 — `StringEnum<T>` construction/comparison, dropped-JSON, nullability (⚠-M) |
| `dotnet-error-handling` | Steps 1–7 — the Case-B `RawError` boundary (⚠-E) |
| `dotnet-testing` | Tests — the `HttpClient` seam (⚠-T) |

**Mandatory JSON-boundary hazards — `System.Text.Json.JsonException` reaches the error boundary from
two directions and they need opposite handling:**

- A drifted or malformed **2xx** body (e.g. a missing member the deserializer required) surfaces as
  a `JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated error shape throws
  `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces**
  the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every
  `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that
  retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 6. Assumptions & Blockers

**Assumptions:**
1. `accountSid` passed to every `Api20100401Message.*` call is the account SID (bound from
   `Twilio:AccountSid`, same value used as Basic-auth `Username`). If sending under a subaccount or
   an API-key SID whose owning account differs, the path `accountSid` must be the **account** SID,
   not the API-key SID — confirm which identity the integration operates as.
2. Immediate sends (Step 2) use `from` = `Twilio:FromNumber`; scheduled sends (Step 3) use
   `messagingServiceSid` = `Twilio:MessagingServiceSid` and pass `from: null`. This split is because
   provider-side scheduling is a Messaging-Service feature (map enum note, `MessageEnumScheduleType`).
   If the product wants immediate sends via the Messaging Service instead of `FromNumber`, that is a
   one-line change (swap `from`/`messagingServiceSid`) but should be confirmed.
3. Reconciliation (Step 7) filters on the single configured `Twilio:FromNumber`. If messages may be
   sent from a Messaging Service pool (multiple numbers), a `From`-number filter will miss pool
   traffic — confirm the send path before relying on it.
4. "Redact but keep metadata" (Step 6) = `UpdateMessage(body: "")`; "delete everything" =
   `DeleteMessage`. Assumed the requirement is the former (retain status/outcome, drop text).

**Blockers:** none — every in-scope capability is exposed by the SDK and grounded above. No
capability had to be invented; the only items that cannot be settled from the SDK (exact Twilio
error-body field set; numeric schedule lead-time limits) are labeled `UNVERIFIED` in §4 with
defensive-coding directives rather than left open.
