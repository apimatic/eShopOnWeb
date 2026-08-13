# Twilio SMS Order Notifications — Integration Plan & Contract Sheet

SDK: `AsadAli.TwilioSdk` (install version-less: `dotnet add package AsadAli.TwilioSdk`).
Root namespace `TwilioSdk`; client `TwilioSdkClient`; options `TwilioSdkClientOptions`.
Map provenance: source commit `51fdf48`. Every row below cites the map page it came from.
All seven requested capabilities are supported by the SDK — there is **no gap** (see Assumptions & Blockers).

---

## 1. Scope & sequence

1. **Client & DI setup** — register `TwilioSdkClient` via `AddTwilioSdkClient`, bind `Twilio:*` config, set the messaging BaseUrl override, wire `HttpClient` lifetime.
2. **Auth** — set `AccountSidAuthToken` (basic auth) from `Twilio:AccountSid` / `Twilio:AuthToken`.
3. **Validate destination at registration** — `client.LookupsV2PhoneNumber.FetchPhoneNumber3` → read `Valid` and store `PhoneNumber` (provider's E.164).
4. **Send SMS** (order-placed / dispatched / cancelled) — `client.Api20100401Message.CreateMessage`.
5. **Schedule a future message** — `CreateMessage` with `scheduleType` + `sendAt` + a Messaging Service SID.
6. **Cancel a scheduled message** — `client.Api20100401Message.UpdateMessage` with `status = Canceled`.
7. **Fetch a message's current status** — `client.Api20100401Message.FetchMessage`.
8. **Redact body / delete record** — `UpdateMessage` (redact body) and/or `DeleteMessage` (remove record).
9. **List for reconciliation** — `client.Api20100401Message.ListMessage` filtered by `from` + date-sent range.
10. **Error boundary + tests** across all call sites.

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

### Namespaces (`using`) needed by this integration
| Type kind | Namespace | Types used here |
|---|---|---|
| Client, options, `ServerOptions` | `TwilioSdk` | `TwilioSdkClient`, `TwilioSdkClientOptions`, `ServerOptions` |
| Controllers (`Api/`) | `TwilioSdk.Api` | `Api20100401Message`, `LookupsV2PhoneNumber` (accessed via `client.X`) |
| Records (`Models/`) | `TwilioSdk.Models` | `ApiV2010AccountMessage`, `ListMessageResponse`, `LookupResponse`, `ValidationError` |
| Enums (`Models/Enums/`) | `TwilioSdk.Models.Enums` | `MessageEnumStatus`, `MessageEnumUpdateStatus`, `MessageEnumScheduleType`, `MessageEnumDirection`, `MessageEnumContentRetention`, `MessageEnumAddressRetention` |
| Server env | `TwilioSdk.Servers` | `ServerEnvironment` |
| Basic-auth credentials | `TwilioSdk.Core.Authentication.Basic` | `BasicAuthCredentials` |
| Errors (Case B) | `TwilioSdk.Core.ErrorResponse` | `RawError` (exception is `TwilioSdk.Core.Exceptions.SdkException<RawError>`) |

### Operations

**All six operations below are error Case B: `SdkException<RawError>`** (no typed accessors).
Read status/body via `ex.Error.StatusCode` (`System.Net.HttpStatusCode`), `ex.Error.ReadAsString()`, `ex.Error.ReadAsJson<T>()`, `ex.Error.ReadAsBytes()`. No no-throw (`…Result`) variant exists on any of them. `RequestOptions? requestOptions = null` and `CancellationToken ct = default` are the trailing params on every signature (source: map `Error-handling model` + each op page).

| # | Controller.Op | Method signature (params in order; all nullable-no-default params MUST be passed explicitly, pass `null` to skip) | Request fields (wire) | Response envelope → inner fields read | Map page |
|---|---|---|---|---|---|
| 3 | `client.LookupsV2PhoneNumber.FetchPhoneNumber3` | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `phoneNumber` is the path segment (the number to validate, E.164 or local+`countryCode`). `fields` (wire `Fields`) selects optional data packages (e.g. line-type-intelligence); pass `null` for base validation. | Returns `LookupResponse`. Read: `Valid (valid): bool?` (usable/reachable-at-basic-validity), `PhoneNumber (phone_number): string?` = **provider's canonical E.164** to store, `NationalFormat (national_format): string?`, `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?`, `CallingCountryCode (calling_country_code): string?`, `CountryCode (country_code): string?` | `operations/LookupsV2PhoneNumber.md`; `records-4-Li-Me.md` (`LookupResponse`) |
| 4/5(send/schedule) | `client.Api20100401Message.CreateMessage` | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid` (path). `to` (wire `To`, required, E.164 destination). **Sender — supply exactly one:** `from` (wire `From`, your `Twilio:FromNumber`) **or** `messagingServiceSid` (wire `MessagingServiceSid`, your `Twilio:MessagingServiceSid`). `body` (wire `Body`, the SMS text). For **scheduling**: `scheduleType = MessageEnumScheduleType.Fixed` (wire `ScheduleType`) + `sendAt` (wire `SendAt`, `DateTimeOffset`) + `messagingServiceSid` (scheduling is Messaging-Service-only — see enum note). All 24 params `statusCallback`…`contentSid` are nullable-no-default → pass `null` for every one you don't use. | Returns `ApiV2010AccountMessage`. Read: `Sid (sid): string?` (message SID), `Status (status): MessageEnumStatus?` (delivery outcome/state), `ErrorCode (error_code): int?`, `ErrorMessage (error_message): string?`, `To (to): string?`, `From (from): string?`, `DateCreated/DateSent/DateUpdated (…): string?`, `NumSegments (num_segments): string?`, `Price/PriceUnit (…): string?`, `MessagingServiceSid (messaging_service_sid): string?` | `operations/Api20100401Message.md`; `records-1-Ac-Ca.md` (`ApiV2010AccountMessage`) |
| 6 (cancel) | `client.Api20100401Message.UpdateMessage` | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` | To **cancel a scheduled message**: `body = null`, `status = MessageEnumUpdateStatus.Canceled` (wire `Status` = `canceled`). The message must currently be in status `scheduled` (`MessageEnumStatus.Scheduled`) — canceling a message that has already left is not possible (provider-enforced; see UNVERIFIED note in Required Reading). | Returns `ApiV2010AccountMessage` (re-read `Status` to confirm `canceled`). | `operations/Api20100401Message.md` |
| 8 (redact body) | `client.Api20100401Message.UpdateMessage` | same signature as row 6 | To **redact only the body** (record + final status survive): `body = ""` (empty string; wire `Body`), `status = null`. This is the redact-only path. | Returns `ApiV2010AccountMessage` with `Body` now empty. | `operations/Api20100401Message.md` |
| 7 (fetch) | `client.Api20100401Message.FetchMessage` | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid` (path), `sid` (path, message SID). | Returns `ApiV2010AccountMessage`. Read `Status (status): MessageEnumStatus?`, `ErrorCode (error_code): int?`, `ErrorMessage (error_message): string?` for current provider state. | `operations/Api20100401Message.md` |
| 8 (delete record) | `client.Api20100401Message.DeleteMessage` | `DeleteMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid` (path), `sid` (path). | Returns `void` (`Task`). **Removes the entire Message record** at the provider (not a body-only redaction). | `operations/Api20100401Message.md` |
| 9 (list) | `client.Api20100401Message.ListMessage` | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `accountSid` (path). Filter by sender: `from` (wire `From`, your `Twilio:FromNumber`). Date-sent **range bounds** are `DateTimeOffset`: `dateSentQueryQuery` → wire **`DateSent>`** (on/after = lower bound), `dateSentQuery` → wire **`DateSent<`** (on/before = upper bound); `dateSent` → wire `DateSent` (exact-day match). All 8 `to`…`pageToken` are nullable-no-default → pass `null` for each unused one. | Returns `ListMessageResponse`. Read `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?` (each item exposes the same fields as row 4/5 — `Sid`, `Status`, `From`, `To`, `DateSent`, `ErrorCode`, `Body`, `Price`, …). Paging fields: `Page (page): int?`, `PageSize (page_size): int?`, `NextPageUri (next_page_uri): string?`, `PreviousPageUri (previous_page_uri): string?`, `FirstPageUri`, `Uri`, `Start`, `End`. | `operations/Api20100401Message.md`; `records-4-Li-Me.md` (`ListMessageResponse`) |

**Pagination (ListMessage):** the map marks pagination **`none` (only `page`, no `perPage`)** — there is **no auto-pager**. Page manually with `pageSize` (`long?`) + `page` (`int?`) / `pageToken` (`string?`), or follow `NextPageUri` from the response until it is null. (usage/mechanics: `dotnet-configuration-resilience`.)

### Enum value tables (literal C# member ↔ wire value) — source: `models/enums.md`

**`MessageEnumStatus`** (message state / delivery outcome, on responses):
`Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)`.

**`MessageEnumUpdateStatus`** (the only value accepted by `UpdateMessage.status`): `Canceled (canceled)`.

**`MessageEnumScheduleType`** (for `CreateMessage.scheduleType`): `Fixed (fixed)` — enum doc: "For Messaging Services only: include with `send_time`/`sendAt` to schedule a Message." (So scheduling requires a Messaging Service SID, not a plain `from`.)

**`MessageEnumDirection`** (on responses): `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)`.

**`MessageEnumContentRetention`**: `Retain (retain)`, `Discard (discard)`. **`MessageEnumAddressRetention`**: `Retain (retain)`, `Obfuscate (obfuscate)`. (Optional privacy controls settable at `CreateMessage` time; `contentRetention = Discard` discards content up-front, distinct from the post-hoc body redaction in row 8.)

> Enums are **`StringEnum<T>`, not C# enums** — build with the static member (`MessageEnumScheduleType.Fixed`) or `MessageEnumScheduleType.FromValue("fixed")`; never `MessageEnumScheduleType.fixed`. (mechanics: `dotnet-models`.)

### Client construction, auth, and server/BaseUrl override

- **Auth** (map *Servers & auth*): `TwilioSdkClientOptions.AccountSidAuthToken` is `BasicAuthCredentials?` (namespace `TwilioSdk.Core.Authentication.Basic`), with `required string Username` and `required string Password`. Basic auth — for account-SID/auth-token credentials, `Username = Twilio:AccountSid`, `Password = Twilio:AuthToken`. (Map's XML-doc note prefers an API key as username / key secret as password for production; account SID + auth token is acceptable. Exact wiring: `dotnet-authentication`.)
- **Environment**: `options.Environment` is `ServerEnvironment` (namespace `TwilioSdk.Servers`); only member is `ServerEnvironment.Production`.
- **DI**: `services.AddTwilioSdkClient(o => { /* set o.AccountSidAuthToken, o.Server, o.Environment */ });` (source: `ServiceCollectionExtensions.cs`).
- **Client ctor**: `new TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)`.
- **BaseUrl / server override — this is PER-HOST and PER-CLIENT, resolved from SDK source (`ServerOptions.cs`, `Servers/*Options.cs`, `Server.cs`):**
  `options.Server` is a `ServerOptions` (namespace `TwilioSdk`) holding **15 independent named server groups** `Default`…`Default14`, each with a `Production.BaseUrl` string. There is **no single global BaseUrl**; each API family reads its own group:
  - **Messaging API** (`Api20100401Message.*`, tagged "Default (api)") reads **`options.Server.Default.Production.BaseUrl`** — default `https://api.twilio.com`. **Bind `Twilio:BaseUrl` here.**
  - **Lookup API** (`LookupsV2PhoneNumber.FetchPhoneNumber3`, tagged "Default4 (lookups)") reads a **separate** property **`options.Server.Default4.Production.BaseUrl`** — default `https://lookups.twilio.com`.
  - **Consequence for this integration:** setting `Twilio:BaseUrl` on `Server.Default` governs **only the messaging host** and does **not** redirect the Lookup call — Lookup continues to hit `https://lookups.twilio.com` (its own group) unless you separately override `Server.Default4`. This matches the requirement that `Twilio:BaseUrl` govern only the messaging API host. The override is applied once on the options (per-client), not per-call.

  ```
  o.Server.Default.Production.BaseUrl  = config["Twilio:BaseUrl"];   // messaging host only
  // o.Server.Default4.Production.BaseUrl left at its https://lookups.twilio.com default
  ```

---

## 3. Trap notes (name the hazard; load the skill — do not implement from the note)

⚠ Step 1 (client & DI) — the `HttpClient`/handler pipeline the client wraps has ownership and lifetime rules a constructor call won't show, and getting the lifetime wrong causes socket exhaustion or stale DNS; the SDK-client wrapper's own lifetime is a separate decision. **MUST load `dotnet-client-initialization`** before writing `new TwilioSdkClient(...)` or `AddTwilioSdkClient(...)`.

⚠ Step 2 (auth) — where and when credentials must be set relative to client construction, and how to source secrets rather than hardcode them, are not visible in the property signature. **MUST load `dotnet-authentication`** before wiring `AccountSidAuthToken`.

⚠ Steps 3–9 (every call) — these list/create/fetch ops carry long runs of nullable-no-default parameters that mis-bind in a positional call; whether an optional you skipped needs `null` vs omission, and how cancellation flows, are call-shape concerns the signature alone won't settle. **MUST load `dotnet-calling-endpoints`** before the first `client.*` call.

⚠ Steps 3–9 (models/enums) — `StringEnum<T>` construction, required-member initialization, and the fact that unmodeled JSON fields are dropped on deserialize all bite when you build requests or map responses onto domain types. **MUST load `dotnet-models`** before constructing payloads or reading response models.

⚠ Step 1/9 (config, BaseUrl, retries, timeouts, pagination) — the retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; which verbs actually retry (and whether a non-idempotent `CreateMessage` can execute more than once) is not shown by the option names; and manual message-list paging has mechanics the signature hides. **MUST load `dotnet-configuration-resilience`** before tuning the client or writing the list-paging loop. This matters specifically because `CreateMessage`/scheduling are non-idempotent writes.

⚠ All call sites (error boundary) — which exception types actually reach your catch, how to read status/body safely on a Case-B `RawError`, and the traps that make a reasonable catch ladder silently wrong are not inferable from the signatures. **MUST load `dotnet-error-handling`** before writing any try/catch (see the two mandatory `JsonException` rows in Required Reading).

⚠ Tests — the fake seam is the `HttpClient` constructor argument, not the SDK client; asserting real behaviour rather than execution, and staying off SDK internals, needs the skill. **MUST load `dotnet-testing`** before writing integration tests.

---

## 4. REQUIRED READING (load BEFORE implementation starts — this sheet deliberately does not carry these skills' contents)

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — client construction, `AddTwilioSdkClient` DI registration, `HttpClient`/`IHttpClientFactory` ownership & lifetime. |
| `dotnet-authentication` | Step 2 — setting `AccountSidAuthToken` (`BasicAuthCredentials`), secret sourcing, when credentials must be set. |
| `dotnet-calling-endpoints` | Steps 3–9 — named-argument calling of the long nullable-parameter signatures, async, cancellation (`ct`). |
| `dotnet-models` | Steps 3–9 — `StringEnum<T>` build/read, required members, response-model mapping, dropped-field behaviour. |
| `dotnet-configuration-resilience` | Steps 1 & 9 — BaseUrl/server override, retries/timeouts (write-retry safety for `CreateMessage`), manual message-list pagination. |
| `dotnet-error-handling` | All call sites — Case-B `SdkException<RawError>` boundary, reading status/body, catch-ladder traps. |
| `dotnet-testing` | Tests — faking the `HttpClient` seam, covering error/edge paths. |

These are to be loaded **before implementation starts**; the sheet intentionally omits their contents.

**Mandatory `System.Text.Json.JsonException` hazard rows — a `JsonException` reaches the error boundary from two directions that need opposite handling:**
- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

- **No SDK gap.** All seven capabilities are present: phone validation via Lookups V2 (`LookupsV2PhoneNumber.FetchPhoneNumber3`, returns `Valid` + canonical E.164 `PhoneNumber`), send/schedule via `CreateMessage`, cancel/redact via `UpdateMessage`, fetch via `FetchMessage`, delete via `DeleteMessage`, list via `ListMessage`. Nothing was invented.
- **Assumption (sender for plain sends):** the plan assumes you send with `Twilio:FromNumber` (`from`) for immediate notifications and `Twilio:MessagingServiceSid` for scheduled ones, because scheduling requires a Messaging Service. If immediate sends should also go through the Messaging Service, use `messagingServiceSid` instead of `from` (supply exactly one — this is provider-enforced, not visible in the signature).
- **Assumption (validation depth):** "usable/reachable" is read from `LookupResponse.Valid` (basic line validity) plus `ValidationErrors`. Deeper reachability signals (line-type-intelligence, SIM-swap, etc.) require passing `fields` data packages and are billed add-ons — not wired unless you confirm you want them.
- **UNVERIFIED (live-traffic only):** the exact provider precondition for `UpdateMessage`-cancel (message must still be `scheduled`) and for body-redaction (message already sent) can only be confirmed against live provider responses. Directive: treat cancel/redact defensively — inspect the returned `Status`/`ErrorCode` after the call and, on a Case-B `SdkException<RawError>`, extract `StatusCode` + `ReadAsString()` best-effort and fall back to a generic "could not cancel/redact — message may have already been sent" message rather than asserting success.
- **UNVERIFIED (live-traffic only):** whether the live wire payload for `ApiV2010AccountMessage` and `LookupResponse` matches these generated models field-for-field. Directive: map responses best-effort (`Sid`, `Status`, `ErrorCode`, `PhoneNumber`, `Valid`), guard for `null` on every read (all fields are nullable in the model), and fall back to the generic message if a field is absent.
- **No blockers to implementation.** Config keys to bind: `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid`, `Twilio:BaseUrl` (→ `Server.Default.Production.BaseUrl`, messaging host only).
