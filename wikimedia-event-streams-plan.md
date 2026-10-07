# Wikimedia EventStreams integration plan — `GET /api/trends/wiki-edits`

SDK source: `sdk/dotnet/` inside the **wikimedia** plugin (context-plugins marketplace). All `source` cells below are
relative to that SDK root. Map index: `sdk-map.md`; operations page: `map/operations/WikimediaEventStreamsClient.md`.

## 1. Scope & sequence

| # | Step | Operations used |
| --- | --- | --- |
| 1 | `global.json` → `rollForward: latestMajor` (only .NET 10 SDK installed). Baseline build/test. | — |
| 2 | Reference the plugin SDK project from `src/PublicApi` only (keeps Web/BlazorAdmin untouched). | — |
| 3 | `WikiTrendsOptions` (UA, connect budget, no-data window, max concurrent watches, match cap) bound from config section `WikiTrends`, validated on start. | — |
| 4 | DI: named `HttpClient` carrying the User-Agent, `WikimediaEventStreamsClient` singleton built from it with explicit options (retry/timeout/stream-read timeout/logger factory). | client construction |
| 5 | `WikiEditWatcher`: opens the stream, verifies it is an SSE answer (per-call `SdkHook.OnResponse`), enumerates frames for the window, deserializes each frame into `MediawikiRevisionCreate`, filters/aggregates, maps every slot, classifies the stop reason. | `SubscribeToOneOrMultipleStreams` with `Streams = [Stream.MediawikiRevisionCreate]` |
| 6 | `WikiEditsEndpoint` (`MinimalApi.Endpoint` `IEndpoint`, admin role + JWT scheme), reads brands/types via `IReadRepository<CatalogBrand/CatalogType>`, validates `seconds` 5–60. | — |
| 7 | Offline tests (fake `HttpMessageHandler` behind the SDK client): happy path, slots incl. extra slot, non-SSE answer, HTTP error, idle → no-data, server-closed stream, malformed frame, deadline; endpoint auth/validation via `WebApplicationFactory`. | — |
| 8 | Live verification with a real 20 s watch. | — |

Why `SubscribeToOneOrMultipleStreams` and not `MediawikiRevisionCreateEvents`: both hit the same stream, but the typed op
deserializes inside the SDK iterator, and a single frame that fails to deserialize throws `ResponseDeserializationException`
and ends the enumeration (source: `Core/Response/JsonSseResponse.cs`). The raw-string op yields each frame's `data` as a
string (`Core/Response/PlainTextSseResponse.cs`), so the watcher deserializes per frame into the same SDK model with the same
`JsonSerializerOptions.Web` the typed op uses (`Core/Extensions/JsonSerializerExtensions.cs`) and survives one bad frame
(counted, not hidden). YOUR CALL — not in the map (the choice); the facts it rests on are cited.

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, verbatim — each operation that takes input takes ONE request record as its first
> parameter, built with an object initializer using the record's own property names, never flat arguments.
> ⚠ Every SDK type is written fully-qualified with the namespace its source path implies, taken from THAT type's path —
> `WikimediaEventStreams.Models.Enums.Stream` collides with `System.IO.Stream`, always qualify it.

| Field | Value | Source |
| --- | --- | --- |
| Controller property | root client (`client.SubscribeToOneOrMultipleStreams`) | `map/operations/WikimediaEventStreamsClient.md` |
| Method signature | `Task<IAsyncEnumerable<string>> SubscribeToOneOrMultipleStreams(SubscribeToOneOrMultipleStreamsRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` — note the **`Task<…>` wrapper** (the map row shows only the inner type) | `WikimediaEventStreamsClient.cs` |
| Route | `GET /v2/stream/{streams}` on server group `Default` | `WikimediaEventStreamsClient.cs` |
| Request record | `WikimediaEventStreams.Requests.SubscribeToOneOrMultipleStreamsRequest`: `Streams: IReadOnlyList<WikimediaEventStreams.Models.Enums.Stream>, required` · `Since: string?, optional` (left out — live tail only) · `LastEventId: IReadOnlyList<LastEventId>?, optional` (left out — no resume) | `Requests/SubscribeToOneOrMultipleStreamsRequest.cs` |
| Enum value used | `Stream.MediawikiRevisionCreate` → wire `mediawiki.revision-create` | `Models/Enums/Stream.cs` |
| Body | none (GET) | `WikimediaEventStreamsClient.cs` |
| Response | `IAsyncEnumerable<string>` — one string per SSE frame `data`; single-shot and lazy; enumerating (or disposing the enumerator) releases the connection; a never-enumerated sequence never releases it | `WikimediaEventStreamsClient.cs` `<remarks>`, `Core/Response/SseFrameReader.cs` |
| Enumeration throws | `SdkTimeoutException` (no frame within `StreamReadTimeout`), `SdkConnectionException` (drop mid-stream). Normal end of body = enumeration completes (no exception) | `<remarks>`, `Core/Response/SseFrameReader.cs` |
| Content-Type | **not checked by the SDK** — a 2xx non-SSE body is parsed as SSE, typically yielding zero frames then a clean end | `Core/Response/SseFrameReader.cs` |
| Error case | **Case B** `WikimediaEventStreams.Core.Exceptions.ApiException<WikimediaEventStreams.Core.ErrorResponse.RawError>` — `StatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()` | `sdk-map.md` (Error-handling model) |
| Pagination | none | `sdk-map.md` defaults table |

Frame model (deserialized by the watcher): `WikimediaEventStreams.Models.MediawikiRevisionCreate` (`Models/MediawikiRevisionCreate.cs`)

| Member (wire) | Type, required? | Used for |
| --- | --- | --- |
| `Meta` (`meta`) → `Domain` (`domain`) | `WikimediaEventStreams.Models.Meta`, required → `string?` (`Models/Meta.cs`) | wiki (`en.wikipedia.org` / `commons.wikimedia.org`) |
| `Database` (`database`) | `string`, required | wiki fallback label when `Meta.Domain` is null |
| `PageTitle` (`page_title`) | `string`, required ("normalized title") | match + output |
| `RevId` (`rev_id`) | `int`, required | output |
| `Performer` (`performer`) → `UserText` (`user_text`) | `Performer?` → `string?` (`Models/Performer.cs`) | editor name (nullable) |
| `RevTimestamp` (`rev_timestamp`) | `DateTimeOffset`, required | timestamp |
| `RevSlots` (`rev_slots`) | `RevSlots?` (`Models/RevSlots.cs`) | slots |
| `RevSlots.Main` (`main`) | `FragmentMediawikiRevisionSlot`, required | slot `main` |
| `RevSlots.AdditionalProperties` | `[JsonExtensionData] WikimediaEventStreams.Core.Models.AdditionalProperties<FragmentMediawikiRevisionSlot>` — every non-`main` slot. Its enumerator / `Values` deserialize **every** entry and throw on one bad entry; `Keys` + `TryGetValue(key, out v)` + `TryGetElement(key, out JsonElement)` are per-entry | `Models/RevSlots.cs`, `Core/Models/AdditionalProperties.cs` |
| `FragmentMediawikiRevisionSlot.RevSlotContentModel` (`rev_slot_content_model`) | `string`, required | slot content model |
| `FragmentMediawikiRevisionSlot.RevSlotSize` (`rev_slot_size`) | `int`, required | slot size in bytes |

Client construction / auth / server facts

| Fact | Value | Source |
| --- | --- | --- |
| Constructor | `new WikimediaEventStreams.WikimediaEventStreamsClient(HttpClient httpClient, WikimediaEventStreamsClientOptions options)` (only ctor) | `sdk-map.md` |
| Options used | `Environment`, `Retry` (`WikimediaEventStreams.Core.Configuration.RetryOptions`, start from `RetryOptions.Default()`), `Logging` (`WikimediaEventStreams.Core.Configuration.LoggingOptions` — `LoggerFactory`, `LogRequestBody`…), `StreamReadTimeout: TimeSpan?` (default 60 s), `TimeProvider` | `WikimediaEventStreamsClientOptions.cs`, `Core/Configuration/*.cs` |
| Per-call hooks | `WikimediaEventStreams.Core.RequestOptions { Hooks = [...] }`; `WikimediaEventStreams.Core.Hooks.SdkHook.OnResponse(Action<HttpResponseMessage, HookContext>)`; runs per attempt inside the pipeline; an exception thrown from the hook makes the SDK dispose the response and rethrow | `Core/RequestOptions.cs`, `Core/Hooks/SdkHook.cs`, `Core/RawClient.cs` |
| Auth | none — no credential properties | `sdk-map.md` Servers & auth |
| Server | 1 group `Default`, `ServerEnvironment.Production` (default) → `https://stream.wikimedia.org` | `sdk-map.md` Servers & auth |
| User-Agent | The SDK stamps `User-Agent: WikimediaEventStreamsClient/0.20.0 CSharp` on every request message, so an `HttpClient` default header would be ignored → replaced in a client-wide `SdkHook.OnRequest` (runs after the SDK's headers are added) | `WikimediaEventStreamsClient.cs` (default headers), `Core/RawClient.cs` (hook order) |

UNVERIFIED (only live traffic settles) → defensive directive:
- A frame missing a `required` member (e.g. no `main` slot) or overflowing an `int` → per-frame `JsonException` → counted in `unparsed`, never fatal.
  **Settled by live traffic (2026-10-08):** ~⅓ of live events failed to bind with `$.rev_id` → Int32 overflow: the
  model types `RevId` as `int` (`[Maximum(2147483647)]`) while some wikis' revision ids are larger. Observed Commons ids
  are ~1.28 × 10⁹, so en/Commons events still fit. Directive: classify each frame by `meta.domain` (wire names from
  `Models/MediawikiRevisionCreate.cs` / `Models/Meta.cs`) on a `JsonDocument` first, and bind the SDK model only for
  `en.wikipedia.org` / `commons.wikimedia.org`; other wikis are counted in `received` only. An en/Commons event the model
  cannot bind still shows up in `unparsed`. SDK defect to report upstream: `MediawikiRevisionCreate.RevId` (and the other
  revision-id members) should be 64-bit.
- `page_title` may use `_` for spaces → matching normalizes `_` → space on both sides.
- A non-`main` slot entry that does not match `FragmentMediawikiRevisionSlot` → read leniently from its `JsonElement` (content model / size may be null), never dropped.
- Server may close the stream early → reported as `stream-error`, not as a quiet window.

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| none — the only caller-supplied value is `seconds` (local range 5–60); no value is passed between Wikimedia operations | — | — |

## 3. Trap notes

| Step | Hazard → consequence | Skill |
| --- | --- | --- |
| 4 | HttpClient ownership/lifetime when the client is a singleton and the stream is long-lived; wrong choice → socket exhaustion or stale DNS, or the default `HttpClient.Timeout` silently cutting a 60 s watch | MUST load `wikimedia:dotnet-client-initialization` |
| 4 | What `Retry.Timeout` actually bounds, whether it covers the stream body, and how retries multiply the connect cost → request exceeding `n + 20` s | MUST load `wikimedia:dotnet-configuration-resilience` |
| 4 | SSE idle window vs. our 15 s no-data rule, and what the built-in logger prints / the log env var can switch on | MUST load `wikimedia:dotnet-configuration-resilience` |
| 5 | Building the request record and passing per-call options/cancellation correctly | MUST load `wikimedia:dotnet-calling-endpoints` |
| 5 | Open enum (`Stream`) construction, extension-data slot bag, wire vs C# names | MUST load `wikimedia:dotnet-models` |
| 5 | Which exceptions reach the catch (connect vs. mid-stream vs. HTTP error vs. our hook), and distinguishing our own cancellation from a provider timeout → a broken feed mislabelled as quiet | MUST load `wikimedia:dotnet-error-handling` |
| 7 | Which seam to fake for an SSE stream, keeping tests independent of SDK internals | MUST load `wikimedia:dotnet-testing` |

## 4. REQUIRED READING (load **before implementation starts**; this sheet deliberately does not carry their contents)

All from the **wikimedia** plugin (context-plugins marketplace), not any other APIMatic plugin's same-named copy:

- `wikimedia:dotnet-client-initialization` · step 4 (client + DI)
- `wikimedia:dotnet-configuration-resilience` · steps 4–5 (retry, timeouts, SSE, logging)
- `wikimedia:dotnet-calling-endpoints` · step 5
- `wikimedia:dotnet-models` · step 5
- `wikimedia:dotnet-error-handling` · step 5 (error boundary)
- `wikimedia:dotnet-testing` · step 7
- (`wikimedia:dotnet-authentication` — not needed: the SDK declares no auth.)

Hazard row (verbatim): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing
`required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as
`ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an
`ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch
`ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | N/A — the API declares no auth and needs none. The one required identity setting, the User-Agent (`WikiTrends:UserAgent`, default `eShopOnWeb-trends/1.0 (shop-ops@example.com)`), is validated non-blank with `ValidateOnStart`, so a blank override stops the host at startup. |
| 2 | Secret sourcing & rotation | N/A — no secret exists. Options are bound once at registration; changing `WikiTrends:*` needs a restart (acceptable for an operator tool). Nothing is written to user-secrets because there is nothing secret. |
| 3 | Total timeout budget | Caller gets ≤ `n + 15` s (+ a catalog read), inside the required `n + 20`. One `CancellationToken` (linked to `RequestAborted`) bounds the whole SDK call: a `TimeProvider` timer fires it after `WikiTrends:ConnectTimeout` (15 s, all attempts + backoff) and is re-armed to `n` s once the headers arrive. Per-attempt knobs sit under it: `Retry.Timeout` 10 s, `HttpClient.Timeout` 15 s, `MaxRetries` 2. The SSE idle window (`StreamReadTimeout` = `WikiTrends:NoDataTimeout`, 15 s) ends a silent stream early as `no-data`. Measured live: 20 s watch → 21.4 s. |
| 4 | Write-retry ownership | N/A — the only call is a `GET`; the SDK may resend it on retryable statuses / transport errors during connect, bounded by the connect budget. No writes exist. |
| 5 | Idempotency & ambiguous writes | N/A — no writes. The injected `Idempotency-Key` applies only to non-GET operations, so it is not sent here. |
| 6 | Observability | Our watcher logs at Information: start (seconds) and outcome (received, matches, stoppedBecause, elapsed); Warning for `stream-error` with the reason and HTTP status; Debug for each unparsed frame (no body). SDK logger: `LoggerFactory` assigned explicitly from DI; `LogRequestBody` off (GET, no body anyway). Correlation: the endpoint's `CorrelationId()` (BaseMessage) is put into a logging scope and returned in the response; the provider's error body is captured (truncated to 300 chars) into `reason`. |
| 7 | Sensitive data | Request carries no sensitive fields (GET, one enum path segment). Responses carry public editor names. `LoggerFactory` set explicitly so `WIKIMEDIAEVENTSTREAMSCLIENT_LOG` cannot redirect logging; `LogRequestBody` stays off. |
| 8 | Environment selection | One group `Default`, one environment `Production` → `https://stream.wikimedia.org` (no sandbox declared). Read-only public stream, so live traffic from dev is harmless; automated tests never touch the network — they inject a fake `HttpMessageHandler` into the SDK client. Override point `options.Server.Default.Production.BaseUrl` is not exposed (YOUR CALL). |
| 9 | Duplicate prevention under concurrency | N/A — no writes. Concurrent watches each hold a long-lived connection, so a process-wide cap (`WikiTrends:MaxConcurrentWatches`, default 2) rejects extra calls with 429. |
| 10 | Partial results | No paged reads. The `matches` list is capped (`WikiTrends:MaxMatches`, default 100): response carries `totalMatches` and `matchesTruncated`. A watch that ended early carries `stoppedBecause` ≠ `time-limit` plus `reason`. |
| 11 | Unknown outcomes | N/A — no writes. |

### DUPLICATE CLAIMS
none — the scope makes no writes.

### PAGED READS
none — the scope reads no paged list. (The local match cap is covered in row 10: `WikiEditsResponse.MatchesTruncated` / `TotalMatches`, set in `WikiEditWatcher.EditCollector.CopyTo`.)

### UNKNOWN OUTCOMES
none — the scope makes no writes.

## 6. Assumptions & Blockers

Blockers: none — the map covers the live revision-create stream, its frame model with every slot, and the hook seam needed
to detect a non-SSE answer.

Assumptions (minor, decided):
- "mentions" = case-insensitive, after `_`→space normalization, as a word of its own with an optional plural "s"
  ("Mug" matches "Coffee mugs", not "Mughal Empire"; ".NET" matches "ASP.NET Core"); the matched terms are returned per edit.
- `latestCommons` is ordered newest first.
- On `stream-error` / `no-data` the endpoint still answers 200 with the partial report; the stop reason is in `stoppedBecause` (+ `reason`).
- `received` counts every frame the stream delivered, including frames that could not be parsed (also reported as `unparsed`).
- The `n`-second window starts once Wikimedia's response headers arrive (connect time is budgeted separately).

## 7. Labels
Every `source` cell above cites a map page or a map-named / map-referenced file under `sdk/dotnet/`; application decisions
are labelled `YOUR CALL — not in the map`; wire-only uncertainties are listed under UNVERIFIED with their directive.
