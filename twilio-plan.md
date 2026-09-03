# twilio-plan.md — Order SMS notifications for eShopOnWeb (Twilio)

Feature: additive SMS order-notification capability on `src/PublicApi`, provider = Twilio, via the
APIMatic-generated Twilio **.NET** SDK (root namespace `Twilio`, client `TwilioClient`).

## 1. Scope & sequence

Build order (each step names the Twilio operations it uses):

1. **Vendor + wire the SDK.** Copy the SDK source into the repo at `src/Twilio/` and reference it
   from `src/Infrastructure` (ProjectReference). Add it to `eShopOnWeb.sln`. The vendored csproj
   opts out of central package management (repo has CPM on). *No Twilio ops.*
2. **Config + client registration (Infrastructure).** Bind `Twilio:` section → `TwilioSettings`;
   fail-fast on missing/blank `AccountSid`/`AuthToken`/`FromNumber`/`MessagingServiceSid`.
   Register `TwilioClient` via `services.AddTwilioClient(...)`: Basic auth
   (Username=AccountSid, Password=AuthToken); when `Twilio:BaseUrl` set, assign it verbatim to
   `options.Server.Default.Production.BaseUrl` (messaging = `Default` group only). *No Twilio ops.*
3. **Provider gateway (Infrastructure).** `ISmsGateway` (ApplicationCore) implemented by
   `TwilioSmsGateway`: `ValidateNumber` → **FetchPhoneNumber3** (Lookups, `Default4`); `SendSms` →
   **CreateMessage** (immediate, `from`=FromNumber); `ScheduleSms` → **CreateMessage**
   (scheduleType=Fixed, sendAt, messagingServiceSid); `CancelScheduled` → **UpdateMessage**
   (status=Canceled); `FetchStatus` → **FetchMessage**; `RedactContent` → **UpdateMessage**
   (body=""); `ListSentFrom` → **ListMessage** (from=FromNumber, DateSent range, paged). Returns
   provider-neutral records; the SDK stays inside Infrastructure.
4. **Domain entities + persistence.** `ContactNumber`, `OrderNotification` aggregates in
   ApplicationCore; DbSets + `IEntityTypeConfiguration` on `CatalogContext` (in-memory ignores
   migrations, so DbSets suffice). Both `IAggregateRoot` → reuse `EfRepository<T>`.
5. **Orchestration services (ApplicationCore).** `ContactNumberService`,
   `OrderNotificationService` — build/dispatch/cancel/resend/redact/reconcile, each persisting
   `OrderNotification` rows and calling `ISmsGateway`. **Every send is wrapped so a provider
   failure never fails the underlying order operation.**
6. **PublicApi endpoints** (`IEndpoint` style, auto-registered) — the 11 routes under `/api/`.
7. **Self-verify** end-to-end against the two approved numbers.

A capability the map lacks would be a Blocker (§6). None found — the whole surface maps to
`Api20100401Message` + `LookupsV2PhoneNumber`.

## 2. CONTRACT SHEET

⚠ **Signatures below are generated code, verbatim.** Every parameter name is the literal C#
identifier; in named arguments use exactly these names (the cancellation-token parameter is `ct`,
so write `ct:`). All 24 CreateMessage nullable params have no default and **must be passed
explicitly** (pass `null` to skip).
⚠ **Every SDK type is fully-qualified from the path the map gives for THAT type.** Namespaces:
client/options → `Twilio`; controllers → `Twilio.Api`; records → `Twilio.Models`; enums →
`Twilio.Models.Enums`; errors → `Twilio.Errors`; `ServerEnvironment` → `Twilio.Servers`;
`RetryOptions` → `Twilio.Core.Configuration`.

Accessors: `client.Api20100401Message`, `client.LookupsV2PhoneNumber`.

| Op | Signature (params in order) · returns · error | Reads from response |
| --- | --- | --- |
| **CreateMessage** | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `ApiV2010AccountMessage` · **Case B** `SdkException<RawError>` | `Sid`, `Status`, `ErrorCode`, `ErrorMessage` |
| **FetchMessage** | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `ApiV2010AccountMessage` · **Case B** | `Status`, `ErrorCode`, `ErrorMessage`, `DateSent` |
| **UpdateMessage** | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `ApiV2010AccountMessage` · **Case B** | `Sid`, `Status` |
| **ListMessage** | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `ListMessageResponse` · **Case B** | `Messages[]`, `NextPageUri` |
| **FetchPhoneNumber3** | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `LookupResponse` · **Case B** · **Server group `Default4`** | `Valid`, `PhoneNumber`, `ValidationErrors` |

**ListMessage query mapping** (from op page): `From ← from`, `DateSent ← dateSent`,
`DateSent< ← dateSentQuery`, `DateSent> ← dateSentQueryQuery`, `PageSize ← pageSize`,
`Page ← page`, `PageToken ← pageToken`. So a `[from,to]` range = `dateSentQueryQuery`(DateSent>)=from,
`dateSentQuery`(DateSent<)=to. No `Pageable` (map: no pagination bullet) → page manually via
`NextPageUri`'s `PageToken`/`Page` query params until absent.

**Response model — `ApiV2010AccountMessage`** (`Models/ApiV2010AccountMessage.cs`): `Sid: string?`
(`sid`), `Status: MessageEnumStatus?` (`status`), `ErrorCode: int?` (`error_code`),
`ErrorMessage: string?` (`error_message`), `To: string?`, `From: string?`, `Body: string?`,
`DateSent: string?` (`date_sent`), `DateCreated: string?`. **`LookupResponse`**
(`Models/LookupResponse.cs`): `Valid: bool?` (`valid`), `PhoneNumber: string?` (`phone_number`,
canonical E.164), `ValidationErrors: IReadOnlyList<ValidationError>?`. **`ListMessageResponse`**
(`Models/ListMessageResponse.cs`): `Messages: IReadOnlyList<ApiV2010AccountMessage>?` (`messages`),
`NextPageUri: string?` (`next_page_uri`), `Page/PageSize: int?`.

**Enums used** (`Models/Enums/…`, build via static members / `FromValue`):
- `MessageEnumScheduleType.Fixed` (wire `fixed`) — required to schedule (only member).
- `MessageEnumUpdateStatus.Canceled` (wire `canceled`) — cancel not-yet-sent (only member).
- `MessageEnumStatus` (read from responses; wire values): `Queued` `Sending` `Sent` `Failed`
  `Delivered` `Undelivered` `Receiving` `Received` `Accepted` `Scheduled` `Read`
  `PartiallyDelivered`(`partially_delivered`) `Canceled`. Terminal-nondelivery = `Failed`/
  `Undelivered`/`Canceled`; terminal-delivered = `Delivered`/`Read`; else in-flight (refresh).
- CreateMessage optional enums we pass `null` for: ContentRetention/AddressRetention/TrafficType/
  RiskCheck.

**Client construction / auth / servers** (source: `TwilioClient.cs`, `TwilioClientOptions.cs`,
`ServiceCollectionExtensions.cs`, `Servers/DefaultOptions.cs`, sdk-map Servers & auth):
`services.AddTwilioClient(o => { o.AccountSidAuthToken = new BasicAuthCredentials { Username =
AccountSid, Password = AuthToken }; if (BaseUrl set) o.Server.Default.Production.BaseUrl = BaseUrl;
o.Logging = o.Logging with { LogRequestBody = false, LoggerFactory = <explicit> }; })`. Only
`ServerEnvironment.Production` exists. `Default` group default `https://api.twilio.com` (messaging);
`Default4` `https://lookups.twilio.com` (lookup) — **not** governed by `Twilio:BaseUrl`.

Source column: all op signatures/returns/errors/servers → `map/operations/Api20100401Message.md`
& `map/operations/LookupsV2PhoneNumber.md`; model/enum shapes → the named `Models/…` files;
client/auth/servers → sdk-map.md §Getting a client, §Servers & auth + the `.cs` files above.

### YOUR CALL — not in the map (application decisions)
- **Redaction = UpdateMessage with `body:""`** (source: UpdateMessage `<remarks>` "used to redact
  Message body text and to cancel not-yet-sent messages"). Keeps SID+status at provider, disposes
  text → matches "no longer retrievable from provider … fact/outcome survives". Not DeleteMessage
  (which drops the record entirely). `YOUR CALL` on which of the two; chose redaction.
- **Follow-up = scheduled message ~3 days out** via CreateMessage(scheduleType=Fixed,
  sendAt=now+3d, messagingServiceSid). Twilio scheduling requires a Messaging Service, so the
  follow-up uses `messagingServiceSid` (not `from`). Cancel via UpdateMessage(Canceled) on its Sid.
- **Send immediate messages with `from`=FromNumber** (not the messaging service) so reconciliation's
  FromNumber filter is meaningful.
- **Resend idempotency** is an application key (our store), keyed on the caller-supplied string —
  the SDK exposes no idempotency param on CreateMessage (see §5).

## 3. Trap notes (hazard + consequence + skill; not resolved here)

- **Client/DI lifetime & HttpClient ownership** — getting the singleton/HttpClient-factory wiring
  wrong causes socket exhaustion or a captured stale client. `MUST load dotnet-client-initialization`.
- **Auth credential placement** — where/when Basic creds attach to the client decides whether the
  first call 401s. `MUST load dotnet-authentication`.
- **Positional vs named args on 24-param CreateMessage / 15-param FetchPhoneNumber3** — one
  mis-bound optional silently sends the wrong field. `MUST load dotnet-calling-endpoints`.
- **Enums are `StringEnum<T>`, not C# enums; unions/optionals differ; unknown fields kept** — reading
  `Status`/`Valid` and building enum args has non-obvious mechanics. `MUST load dotnet-models`.
- **Case B error mechanics + the two JsonException directions** — see REQUIRED READING; a naive
  catch ladder lets malformed 2xx or non-matching error bodies escape. `MUST load dotnet-error-handling`.
- **Retry eligibility / `Timeout` is per-attempt / LogRequestBody unredacted / BaseUrl selection /
  manual pagination** — the resend/duplicate and total-timeout behaviour depends on these.
  `MUST load dotnet-configuration-resilience`.
- **Faking the SDK seam (HttpClient) for tests** — `MUST load dotnet-testing`.

## 4. REQUIRED READING (load ALL before implementing; sheet omits their contents on purpose)

- `twilio-platforms-team:dotnet-client-initialization` · step 2 (client + DI registration)
- `twilio-platforms-team:dotnet-authentication` · step 2 (Basic auth wiring)
- `twilio-platforms-team:dotnet-calling-endpoints` · step 3 (every gateway call)
- `twilio-platforms-team:dotnet-models` · step 3 (enums, response reads, building args)
- `twilio-platforms-team:dotnet-error-handling` · steps 3/5 (error boundary — always required)
- `twilio-platforms-team:dotnet-configuration-resilience` · steps 2/3 (retries, timeout, base URL, logging, paging)
- `twilio-platforms-team:dotnet-testing` · tests

**Two hazard rows to carry verbatim (`System.Text.Json.JsonException` reaches the boundary from two
directions, needing opposite handling):** (1) a drifted/malformed **2xx** body (a missing `required`
member) surfaces as a `JsonException` from deserialization, **not** an `SdkException`, so an
SDK-exception-only catch ladder lets it escape; (2) a **non-2xx** body that doesn't match its
operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is
being constructed*, **replacing** the `SdkException` and destroying the HTTP status. Our ops are all
Case B (`RawError`), but both directions still apply — the notification error boundary must catch
`JsonException` and generic `Exception`, not only `SdkException<RawError>`.

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | **Credential fail-fast** | `TwilioSettings` bound from `Twilio:`; a validator run at startup throws if `AccountSid`, `AuthToken`, `FromNumber` or `MessagingServiceSid` is null/whitespace (each part checked — Basic auth needs both AccountSid+AuthToken non-blank). Host refuses to start rather than 401 on first call. `BaseUrl` optional (blank → SDK default). |
| 2 | **Secret sourcing & rotation** | Secrets from **.NET user-secrets** (`Twilio:*` keys), never in repo. `AddTwilioClient` builds the options object once at registration and captures it in the singleton `TwilioClient`, so a rotated token takes effect only on process restart. Acceptable for this app; documented, not hot-reloaded. |
| 3 | **Total timeout budget** | Each provider call is bounded by a per-call `CancellationTokenSource` (≈15 s) passed as `ct:`, because `RetryOptions.Timeout` is per-attempt, not total. Sends are POST → not auto-retried (row 4), so the budget ≈ one attempt. The caller's HTTP request is never blocked longer than that, and a timeout is swallowed by the notification boundary (order op still succeeds). |
| 4 | **Write-retry ownership** | Default `HttpMethodsToRetry` = GET/HEAD/PUT/OPTIONS. Our writes CreateMessage/UpdateMessage are **POST**, DeleteMessage unused → **never auto-resent** by the SDK (good: no duplicate SMS). We add no manual send-retry. Reads FetchMessage/ListMessage/FetchPhoneNumber3 are GET → safely retried. |
| 5 | **Idempotency & ambiguous writes** | CreateMessage/UpdateMessage take **no real idempotency key** (map signatures); the generator's per-call `Idempotency-Key` header is a fresh Guid and is **not** a key. Placed/dispatched/cancelled sends fire once per operation (no retry) — reconciliation (`ListMessage` by FromNumber) is the safety net for a lost/duplicate ack. **Resend** dedups on the **caller-supplied idempotency key** stored on `OrderNotification`: same key → return existing notification, send nothing; fresh key → new send. |
| 6 | **Observability** | Structured logs at Info for lifecycle (order id, notification id, kind, provider status, error code) and Warning on send failure — **never the phone number or message body**. Provider `ErrorCode`/`ErrorMessage` from Case B bodies are logged for correlation. `LogRequestBody` stays **off**. |
| 7 | **Sensitive data** | CreateMessage carries `to`, `from`, `body` (phone numbers + message content) — sensitive. Therefore `LogRequestBody=false` **and** `options.Logging.LoggerFactory` is set explicitly at registration so the `TWILIOCLIENT_LOG` env var cannot switch body logging on from outside code. Our own logs/exceptions never echo the To number or body (task: a shopper's number is never written to logs). Stored `OrderNotification.Body` is redactable (Flow 3 content disposal). |
| 8 | **Environment selection** | Only `ServerEnvironment.Production` exists (no sandbox) → this is a **live** account; test traffic is limited to the two approved numbers and minimal volume. Messaging uses group `Default` (api.twilio.com), overridable via `Twilio:BaseUrl` → `Server.Default.Production.BaseUrl`. Lookup uses group `Default4` (lookups.twilio.com), deliberately **not** overridden by `Twilio:BaseUrl`. |

## 6. Assumptions & Blockers

- **Assumption:** "usable destination" for Flow 1 = Lookups `Valid == true` (and no `ValidationErrors`);
  store `LookupResponse.PhoneNumber` (canonical E.164). Undeliverable-but-valid US numbers (the
  unreachable fixture) still pass Lookup validation and are registerable — undeliverability surfaces
  later as a `failed`/`undelivered` message **status**, which is an expected outcome, not a rejection.
- **Assumption:** a shopper may register several numbers; an order event sends one message per
  registered number (one `OrderNotification` each). For verification only the Canadian number is
  registered, to keep live volume minimal.
- **Assumption:** follow-up delay = 3 days (within Twilio's 15 min–7 day scheduling window).
- **No Blockers.** Every required capability maps to an SDK operation.
