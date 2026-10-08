# wikimedia-event-streams-plan.md

## 1. Scope & Sequence

| Step | What | Operations |
|---|---|---|
| 1 | Fix `global.json`, add SDK ProjectReference | — |
| 2 | Create response/request DTOs | — |
| 3 | Create `IWikiTrendsService` + `WikiTrendsService` | `MediawikiRevisionCreateEvents` |
| 4 | Create `WikiEditsEndpoint` | — |
| 5 | Register client + service in `Program.cs` | — |
| 6 | Create unit tests (`WikiTrendsServiceTests`) | StubHandler; no network |
| 7 | Create integration tests (`WikiEditsEndpointTests`) | Fake IWikiTrendsService; no network |
| 8 | Build, run tests, live self-verify | — |

---

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code. Every operation takes ONE request record as its first parameter (built with an object initializer, named properties—not flat args). Every SDK type is written with the namespace its source path implies.

### Operations

| Controller | Method | Request record + members | Response type | Error case | Pagination | Source |
|---|---|---|---|---|---|---|
| (root client) | `MediawikiRevisionCreateEvents(MediawikiRevisionCreateEventsRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `Since: string?` (optional), `LastEventId: IReadOnlyList<LastEventId>?` (optional) | `Task<IAsyncEnumerable<MediawikiRevisionCreate>>` | Case B — `ApiException<RawError>` | none | `map/operations/WikimediaEventStreamsClient.md`; `Requests/MediawikiRevisionCreateEventsRequest.cs` |

### Response model: `MediawikiRevisionCreate` fields used

| C# Property | Wire name | Type | Required? | Notes |
|---|---|---|---|---|
| `Schema` | `$schema` | string | required | not used in output |
| `Database` | `database` | string | required | not used in output |
| `Dt` | `dt` | DateTimeOffset | required | event time |
| `Meta` | `meta` | `Meta` | required | see below |
| `PageTitle` | `page_title` | string | required | matched against catalog terms |
| `RevId` | `rev_id` | int | required | revision id for output |
| `RevTimestamp` | `rev_timestamp` | DateTimeOffset | required | timestamp for output |
| `Performer` | `performer` | `Performer?` | optional | `.UserText` for editor name |
| `RevSlots` | `rev_slots` | `RevSlots?` | optional | all content slots |

### Response model: `Meta` fields used

| C# Property | Wire name | Type | Required? |
|---|---|---|---|
| `Domain` | `domain` | string? | optional |
| `Stream` | `stream` | string | required |

### Response model: `Performer` field used

| C# Property | Wire name | Type |
|---|---|---|
| `UserText` | `user_text` | string? |

### Response model: `RevSlots`

| C# Property | Wire name | Type | Notes |
|---|---|---|---|
| `Main` | `main` | `FragmentMediawikiRevisionSlot` | required; always the "main" slot |
| `AdditionalProperties` | (extension data) | `AdditionalProperties<FragmentMediawikiRevisionSlot>` | additional slots (e.g. "mediainfo") keyed by slot name; enumerate with `foreach (var (name, slot) in revSlots.AdditionalProperties)` |

### Response model: `FragmentMediawikiRevisionSlot`

| C# Property | Wire name | Type | Required? |
|---|---|---|---|
| `RevSlotContentModel` | `rev_slot_content_model` | string | required |
| `RevSlotSha1` | `rev_slot_sha1` | string | required |
| `RevSlotSize` | `rev_slot_size` | int | required |
| `RevSlotOriginRevId` | `rev_slot_origin_rev_id` | int? | optional |

**Source**: `Models/RevSlots.cs`, `Models/FragmentMediawikiRevisionSlot.cs`

### Client construction facts

| Fact | Value | Source |
|---|---|---|
| Constructor | `WikimediaEventStreamsClient(HttpClient httpClient, WikimediaEventStreamsClientOptions options)` | `WikimediaEventStreamsClient.cs` |
| DI extension | `services.AddWikimediaEventStreamsClient(configure?)` → singleton | `ServiceCollectionExtensions.cs` |
| StreamReadTimeout option | `TimeSpan? StreamReadTimeout` default 60s | `WikimediaEventStreamsClientOptions.cs` |
| Auth | None — no credentials properties | `dotnet-authentication` skill |
| Production base URL | `https://stream.wikimedia.org` | `sdk-map.md` |
| Namespaces required | `WikimediaEventStreams`, `WikimediaEventStreams.Servers`, `WikimediaEventStreams.Models`, `WikimediaEventStreams.Requests`, `WikimediaEventStreams.Core`, `WikimediaEventStreams.Core.Hooks`, `WikimediaEventStreams.Core.Exceptions`, `WikimediaEventStreams.Core.ErrorResponse`, `WikimediaEventStreams.Core.Configuration` | `dotnet-getting-started` skill |

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
|---|---|---|
| none — this integration reads a live stream; it has no write operations and no value one operation returns that another must accept | — | — |

---

## 3. Trap Notes

- **Step 3**: `StreamReadTimeout` bounds only the wait BETWEEN frames, not the whole stream; firing it raises `SdkTimeoutException`, not `OperationCanceledException` — handle it separately. **MUST load wikimedia:dotnet-configuration-resilience**
- **Step 3**: SSE operations return `Task<IAsyncEnumerable<T>>` — `await` opens the connection, then `foreach` reads frames; looping a second time throws `ObjectDisposedException`. **MUST load wikimedia:dotnet-configuration-resilience**
- **Step 3**: `HttpMethodsToRetry` defaults include `GET`; a hung SSE open would multiply `Timeout × retries`; disable retries for the streaming client. **MUST load wikimedia:dotnet-configuration-resilience**
- **Step 3**: The SDK does NOT check `Content-Type`; a 2xx that is not `text/event-stream` gives zero items and no error — silent broken feed. Must detect via `SdkHook.OnResponse`. **MUST load wikimedia:dotnet-configuration-resilience**
- **Step 3**: `OperationCanceledException` from the caller's token must propagate; from an internal watch-window CTS it means `time-limit`. The condition `when (!ct.IsCancellationRequested)` distinguishes them. **MUST load wikimedia:dotnet-error-handling**
- **Step 6**: `AdditionalProperties<T>.GetEnumerator()` yields `KeyValuePair<string, T>` typed values (not `JsonElement`); `foreach (var (name, slot) in revSlots.AdditionalProperties)` works directly. **Source**: `Core/Models/AdditionalProperties.cs`
- **Step 6/7**: `ApiException<TError>` is not constructable in tests; produce it through a stub handler. `SdkTimeoutException` and others are constructable via `new` + object-initializer. **MUST load wikimedia:dotnet-testing**

---

## 4. REQUIRED READING

Load these before implementation starts. The sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `wikimedia:dotnet-client-initialization` | Step 1/5 — construct + DI-register client |
| `wikimedia:dotnet-authentication` | Step 5 — confirm no credentials needed |
| `wikimedia:dotnet-calling-endpoints` | Step 3 — call `MediawikiRevisionCreateEvents` |
| `wikimedia:dotnet-models` | Step 3 — read `RevSlots.AdditionalProperties` |
| `wikimedia:dotnet-error-handling` | Steps 3, 6, 7 — every error boundary |
| `wikimedia:dotnet-configuration-resilience` | Step 3 — SSE streaming, retry, timeout, hooks |
| `wikimedia:dotnet-testing` | Steps 6, 7 — StubHandler, exception construction |

> ⚠ Hazard: a body that does not match its declared type — a drifted or malformed 2xx response or a non-2xx body that does not match the operation's error shape — surfaces as `ResponseDeserializationException`, an `ApiException` that is NOT `ApiException<TError>`; catch ladders covering only `ApiException<TError>` let it escape.

---

## 5. PRODUCTION READINESS

| # | Concern | Decision |
|---|---|---|
| 1 | Credential fail-fast | N/A — Wikimedia EventStreams requires no credentials. No secret to bind. |
| 2 | Secret sourcing & rotation | N/A — no credentials. |
| 3 | Total timeout budget | watchCts = n seconds (watch window); StreamReadTimeout = 15s (no-data idle); HttpClient.Timeout = 10s (opening per-attempt bound). Total ≤ 10s (opening) + max(n, 15s) ≤ n + 15s ≤ n + 20s. CancellationToken from HttpContext.RequestAborted is the outermost bound. |
| 4 | Write-retry ownership | N/A — integration is read-only (SSE stream). No writes. |
| 5 | Idempotency & ambiguous writes | N/A — no writes. |
| 6 | Observability | SDK built-in logger via ILoggerFactory from DI container; logs request URL at Information, response status at Information/Warning, errors at Error. `LogRequestBody` stays off (no body sent). Correlation: no request-id exposed by Wikimedia EventStreams SSE stream. |
| 7 | Sensitive data | No sensitive data in request (no body, no auth header). `LogRequestBody` off. `LoggerFactory` assigned explicitly from DI to disable env-var override. |
| 8 | Environment selection | Single server group `Default`, single environment `Production` → `https://stream.wikimedia.org`. No sandbox. Test isolation via StubHandler (no real traffic in tests). |
| 9 | Duplicate prevention under concurrency | N/A — read-only stream watch; no writes, no state mutations shared across requests. Concurrent requests each get their own watch window. |
| 10 | Partial results | N/A — no pagination. The stream runs for n seconds and returns what arrived. The `stoppedBecause` field communicates why the watch ended. |
| 11 | Unknown outcomes | N/A — no writes; nothing to settle. |

**DUPLICATE CLAIMS**: none

**PAGED READS**: none

**UNKNOWN OUTCOMES**: none

---

## 6. Assumptions & Blockers

- **Assumption**: `meta.domain` on `MediawikiRevisionCreate` events reliably identifies the wiki (e.g. `en.wikipedia.org`, `commons.wikimedia.org`). This is standard Wikimedia EventStreams behaviour. `UNVERIFIED` via live traffic but consistent with SDK model design.
- **Assumption**: Catalog brands/types are loaded fresh per request from the in-memory repository (which is seeded on startup). This is the correct approach given the in-memory store constraint.
- **No blockers** — all required capabilities are present in the SDK.
