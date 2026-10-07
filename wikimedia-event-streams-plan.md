# Wikimedia EventStreams integration plan — `GET /api/trends/wiki-edits`

SDK source (read-only, plugin-relative): `sdk/dotnet/` in the `wikimedia` plugin. Map: `sdk/dotnet/sdk-map.md`,
`sdk/dotnet/map/operations/WikimediaEventStreamsClient.md`.

## 1. Scope & sequence

| # | Step | Operations used |
| --- | --- | --- |
| 1 | `global.json` roll-forward so the .NET 10 SDK builds the 8.0 solution; baseline build/test | — |
| 2 | Reference `sdk/dotnet/WikimediaEventStreams.csproj` from `src/PublicApi` | — |
| 3 | Options class (`WikimediaTrends` config section) + DI: named `HttpClient`, singleton `WikimediaEventStreamsClient`, client-wide User-Agent hook | — |
| 4 | Watcher service: open the revision-create stream, enumerate for `n` s, classify the stop reason, map edits (all slots) | `SubscribeToOneOrMultipleStreams` (raw frames) — revised from `MediawikiRevisionCreateEvents`, see §6 |
| 5 | Catalog term matcher (brands + types from the PublicApi catalog repositories) | — |
| 6 | Admin-only minimal endpoint `GET api/trends/wiki-edits?seconds=` (5..60, default 20) | via step 4 |
| 7 | Offline tests (stub `HttpMessageHandler` seam) + endpoint auth/validation tests | via step 4 |
| 8 | Live verification against `stream.wikimedia.org` | `SubscribeToOneOrMultipleStreams` |

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, verbatim — each operation that takes input takes ONE request record as its first
> parameter (an operation with no inputs takes none), built with an object initializer whose property names are the
> record's own, never flat arguments.
>
> ⚠ Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map
> gives for THAT type, never from where a neighbouring type sits.

| Field | Value | Source |
| --- | --- | --- |
| Controller property | `client` (root — method directly on `WikimediaEventStreams.WikimediaEventStreamsClient`) | `map/operations/WikimediaEventStreamsClient.md` |
| Method signature | `Task<IAsyncEnumerable<WikimediaEventStreams.Models.MediawikiRevisionCreate>> MediawikiRevisionCreateEvents(WikimediaEventStreams.Requests.MediawikiRevisionCreateEventsRequest request, WikimediaEventStreams.Core.RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` — `GET /v2/stream/mediawiki.revision-create`, JSON SSE | `WikimediaEventStreamsClient.cs` (method + `<remarks>`) |
| Request record | `WikimediaEventStreams.Requests.MediawikiRevisionCreateEventsRequest`: `Since: string?, optional` · `LastEventId: IReadOnlyList<WikimediaEventStreams.Models.LastEventId>?, optional` — nothing required; both left unset (live position) | `Requests/MediawikiRevisionCreateEventsRequest.cs` |
| Body model | none (GET) | `WikimediaEventStreamsClient.cs` |
| Response item | `WikimediaEventStreams.Models.MediawikiRevisionCreate` — reads: `Meta (meta): Meta, required` · `PageTitle (page_title): string, required` · `RevId (rev_id): int, required` · `Performer (performer): Performer?` · `RevTimestamp (rev_timestamp): DateTimeOffset, required` · `RevSlots (rev_slots): RevSlots?` · `Database (database): string, required` | `Models/MediawikiRevisionCreate.cs` |
| `Meta` | `Domain (domain): string?` · `Uri (uri): string?` · `Stream (stream): string, required` | `Models/Meta.cs` |
| `Performer` | `UserText (user_text): string?` | `Models/Performer.cs` |
| `RevSlots` | `Main (main): FragmentMediawikiRevisionSlot, required` + `[JsonExtensionData] AdditionalProperties: WikimediaEventStreams.Core.Models.AdditionalProperties<FragmentMediawikiRevisionSlot>` (every non-`main` slot, keyed by wire slot name) | `Models/RevSlots.cs`, `Core/Models/AdditionalProperties.cs` |
| `FragmentMediawikiRevisionSlot` | `RevSlotContentModel (rev_slot_content_model): string, required` · `RevSlotSize (rev_slot_size): int, required` · `RevSlotSha1 (rev_slot_sha1): string, required` · `RevSlotOriginRevId (rev_slot_origin_rev_id): int?` | `Models/FragmentMediawikiRevisionSlot.cs` |
| Error case | **Case B** — `WikimediaEventStreams.Core.Exceptions.ApiException<WikimediaEventStreams.Core.ErrorResponse.RawError>` on the opening `await`; accessors `StatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()`. Mid-stream: `SdkTimeoutException` (idle), `ResponseDeserializationException` (bad frame), `SdkConnectionException` (drop) | `sdk-map.md` § Error-handling model; method `<remarks>` |
| Pagination | none (stream) | `sdk-map.md` defaults table |

**Revised data path (used by the code).**

| Field | Value | Source |
| --- | --- | --- |
| Method signature | `Task<IAsyncEnumerable<string>> SubscribeToOneOrMultipleStreams(WikimediaEventStreams.Requests.SubscribeToOneOrMultipleStreamsRequest request, WikimediaEventStreams.Core.RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` — `GET /v2/stream/{streams}` (list joined with `,`), plain-text SSE: each item is one frame's `data` | `map/operations/WikimediaEventStreamsClient.md`; `WikimediaEventStreamsClient.cs`; `Core/TemplateParamsFactory.cs`; `Core/Response/PlainTextSseResponse.cs` |
| Request record | `Streams: IReadOnlyList<WikimediaEventStreams.Models.Enums.Stream>, required` = `[Stream.MediawikiRevisionCreate]` (`"mediawiki.revision-create"`) · `Since: string?` (unset) · `LastEventId` (unset) | `Requests/SubscribeToOneOrMultipleStreamsRequest.cs`, `Models/Enums/Stream.cs` |
| Error case | Case B `ApiException<RawError>` on open; mid-stream `SdkTimeoutException` / `SdkConnectionException` (a plain-string stream cannot raise `ResponseDeserializationException`) | `sdk-map.md`; `wikimedia:dotnet-configuration-resilience` |
| Per-frame parse | first `System.Text.Json` into `WikimediaEventStreams.Models.MediawikiRevisionCreate` with `JsonSerializerOptions.Web` (what the SDK itself uses); on `JsonException`, a tolerant reader over the same wire names (`meta.domain`, `meta.uri`, `page_title`, `rev_id` as 64-bit, `performer.user_text`, `rev_timestamp`, `rev_slots.<name>.rev_slot_content_model` / `rev_slot_size`) | `Models/MediawikiRevisionCreate.cs`, `Models/Meta.cs`, `Models/Performer.cs`, `Models/RevSlots.cs`, `Models/FragmentMediawikiRevisionSlot.cs` |

Enums needed: none (`ServerEnvironment.Production` only — `Servers/ServerEnvironment.cs`).

Client construction / auth / server facts:

| Fact | Value | Source |
| --- | --- | --- |
| Constructor | `new WikimediaEventStreams.WikimediaEventStreamsClient(HttpClient, WikimediaEventStreams.WikimediaEventStreamsClientOptions)` (only ctor) | `sdk-map.md` § Getting a client |
| Options used | `Environment` (`WikimediaEventStreams.Servers.ServerEnvironment.Production`), `Retry` (`WikimediaEventStreams.Core.Configuration.RetryOptions`, build via `RetryOptions.Default() with {…}`), `Logging` (`LoggingOptions`), `StreamReadTimeout: TimeSpan?` (default 60 s), `Hooks: IReadOnlyList<WikimediaEventStreams.Core.Hooks.SdkHook>`, `Server` | `WikimediaEventStreamsClientOptions.cs` |
| Auth | none — spec declares no security schemes | `sdk-map.md` § Servers & auth |
| Server group | `Default` → `https://stream.wikimedia.org`; override `options.Server.Default.Production.BaseUrl` | `sdk-map.md` § Servers & auth |
| Built-in User-Agent | SDK adds `User-Agent: WikimediaEventStreamsClient/0.20.0 CSharp` as a default header on every request; `SdkHook.BeforeRequest` runs after headers are added | `WikimediaEventStreamsClient.cs` ctor, `Core/RawClient.cs` (`ExecuteResult`) |
| Per-call hooks | `new WikimediaEventStreams.Core.RequestOptions { Hooks = [...] }` (appended after client-wide hooks) | `Core/RequestOptions.cs` |
| Stream release | sequence is lazy & single-shot; response is disposed only when the iterator runs (ends, breaks, throws, or is cancelled) — never enumerated ⇒ never released | method `<remarks>`; `Core/Response/SseFrameReader.cs` |
| Content-Type | SDK does not check it on a 2xx | `Core/Response/JsonSseResponse.cs`, `Core/RawClient.cs` |
| Bad frame | `ResponseDeserializationException` thrown from the iterator ⇒ enumeration ends | `Core/Response/JsonSseResponse.cs` |
| `RevId` / slot sizes typed `int` (`[Maximum(2147483647)]`) | a value above `int.MaxValue` fails that frame, and on the typed operation the thrown `ResponseDeserializationException` ends the whole enumeration | `Models/MediawikiRevisionCreate.cs`, `Core/Response/JsonSseResponse.cs` — **verified live 2026-10-08**: `JsonException … System.Int32. Path: $.rev_id` on the 2nd event of a 20 s watch. Directive: read raw frames (row below) and parse per frame |
| `page_title` normalisation | "normalized title" — whether spaces arrive as `_` | **UNVERIFIED**; directive: matcher treats `_` and space as equivalent |

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| none — the only caller-supplied value is `seconds`, an application parameter; no value the caller supplies is passed to Wikimedia | — | — |

## 3. Trap notes

| Step | Hazard → consequence | Skill |
| --- | --- | --- |
| 3 | `HttpClient`/SDK-client lifetime under DI (singleton + DNS, handler sharing on the default client) → stale connections or leaking settings to other consumers | MUST load `wikimedia:dotnet-client-initialization` |
| 3 | What `Retry.Timeout`, `HttpClient.Timeout` and `StreamReadTimeout` each actually bound, and which ones retry → the `n + 20` s promise breaks | MUST load `wikimedia:dotnet-configuration-resilience` |
| 3 | Logging defaults when `LoggerFactory` is null and the `WIKIMEDIAEVENTSTREAMSCLIENT_LOG` variable → logging switched on from outside the code | MUST load `wikimedia:dotnet-configuration-resilience` |
| 4 | SSE stream semantics: Content-Type not checked, release only on enumeration, single enumeration → a broken feed reported as a quiet one, or a leaked connection | MUST load `wikimedia:dotnet-configuration-resilience` |
| 4 | Which exception leaves the open vs. the enumeration, and what is/isn't wrapped (own cancellation) → mis-classified `stoppedBecause` | MUST load `wikimedia:dotnet-error-handling` |
| 4 | Reading the extension-data slot bag (typed `AdditionalProperties<T>`) → silently dropping non-`main` slots or throwing on one odd slot | MUST load `wikimedia:dotnet-models` |
| 4 | Positional `requestOptions` vs `cancellationToken` → wrong overload binding | MUST load `wikimedia:dotnet-calling-endpoints` |
| 7 | Which seam to fake and how to drive SSE/idle/errors offline → tests that hit the network or assert nothing real | MUST load `wikimedia:dotnet-testing` |

## 4. REQUIRED READING (load **before implementation starts**; this sheet deliberately does not carry their contents)

| Skill (plugin-qualified) | Governs |
| --- | --- |
| `wikimedia:dotnet-client-initialization` | step 3 — client & DI |
| `wikimedia:dotnet-configuration-resilience` | steps 3–4 — timeouts, retries, SSE, logging, hooks |
| `wikimedia:dotnet-error-handling` | step 4 — error boundary |
| `wikimedia:dotnet-models` | step 4 — slot extension bag |
| `wikimedia:dotnet-calling-endpoints` | step 4 — the call |
| `wikimedia:dotnet-testing` | step 7 — tests |
| `wikimedia:dotnet-authentication` | N/A — no auth scheme (`sdk-map.md` § Servers & auth) |

Hazard row (verbatim): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing
`required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape —
surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type
but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it
must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | N/A — the SDK declares no auth scheme and the stream is public; no credential exists to bind. The only required config value (User-Agent) has a built-in default and is validated non-blank at startup (`ValidateOnStart`). |
| 2 | Secret sourcing & rotation | N/A — no secret. Options are bound once at registration into the singleton client; changing `WikimediaTrends:*` needs a restart (acceptable: nothing secret to rotate). |
| 3 | Total timeout budget | Caller budget = `n` s watch + ≤ ~1 s teardown, hard-capped < `n + 20`. Enforced by one linked `CancellationTokenSource` (request-aborted + `CancelAfter(n)`) passed to the open and the enumeration; per-attempt `Retry.Timeout` 10 s, `HttpClient.Timeout` 10 s (header wait only), `MaxRetries` 2 — all inside the same token; `StreamReadTimeout` 15 s = the `no-data` rule. |
| 4 | Write-retry ownership | N/A — scope has no writes; only a `GET`, which the SDK may resend before the stream opens (bounded by the watch token). |
| 5 | Idempotency & ambiguous writes | N/A — no writes. |
| 6 | Observability | SDK logger wired to the host `ILoggerFactory` explicitly (request line Information, failures Warning/Error); `LogRequestBody` off (no bodies anyway); our watcher logs one Information summary per watch (duration, received, matches, stop reason) and a Warning with the exception for every `stream-error`. No provider correlation id exists in a Case-B raw error; we log HTTP status + the first 500 chars of the raw body. |
| 7 | Sensitive data | None in requests (no body, no credentials). Responses carry public editor names only. `LoggerFactory` assigned explicitly so `WIKIMEDIAEVENTSTREAMSCLIENT_LOG` cannot enable body/header logging. |
| 8 | Environment selection | One group `Default`, one environment `Production` → `https://stream.wikimedia.org`. Deployments set nothing; `WikimediaTrends:BaseUrl` can override (tests use a stub `HttpMessageHandler`, never the live host — no sandbox exists, and the read-only stream is harmless anyway). |
| 9 | Duplicate prevention under concurrency | N/A — read-only; no write a caller can trigger twice. Concurrent watches are capped (`WikimediaTrends:MaxConcurrentWatches`, default 2) → `429` beyond that, to bound outbound connections. |
| 10 | Partial results | `matches` capped at `WikimediaTrends:MaxMatches` (default 200) → `matchesTruncated: true` + `matchCount` (total) in the response. |
| 11 | Unknown outcomes | N/A — no writes. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| none | — | — | — | — |

**PAGED READS**

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| none (stream, not paged; the match list cap is row 10) | — | — | — |

**UNKNOWN OUTCOMES**

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| none | — | — | — | — |

## 6. Assumptions & Blockers

- Assumption: "new page revisions" = the `mediawiki.revision-create` stream (`MediawikiRevisionCreateEvents`); the
  `revision-create` alias (`RevisionCreateEvents`) is the same stream per its `<remarks>`. — YOUR CALL — not in the map
- Assumption: "wiki" in the response = `meta.domain` (fallback: host of `meta.uri`); "editor" = `performer.user_text`;
  "timestamp" = `rev_timestamp`. — YOUR CALL — not in the map
- Assumption: "mentions" = case-insensitive whole-word match (so brand `Other` does not match `Mother`). — YOUR CALL — not in the map
- Assumption: an unanswered connection when the time limit fires (stream never opened) is `stream-error`, not quiet. — YOUR CALL — not in the map
- Revision (2026-10-08, live verification): the typed `MediawikiRevisionCreateEvents` cannot carry live traffic — `rev_id` overflows the SDK's `int` and the stream ends on that frame. Switched to the same SDK's raw `SubscribeToOneOrMultipleStreams`, parsing each frame with the SDK model and falling back to a tolerant reader over the SDK's documented wire names. Frames neither path can read are counted (`unreadable`); a watch in which every received event was unreadable is reported as `stream-error`, never as quiet. SDK defect to report upstream: `MediawikiRevisionCreate.RevId` (and other `int` revision ids) should be 64-bit.
- Blockers: none. User-Agent override uses the SDK's own `SdkHook.OnRequest` seam (`Core/Hooks/SdkHook.cs`).
