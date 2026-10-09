# Wikimedia EventStreams integration plan — wiki-edits trends endpoint

## 1. Scope & sequence

| Step | What | Operations |
|---|---|---|
| 1 | Add SDK reference to PublicApi.csproj; update global.json rollForward | — |
| 2 | Register `WikimediaEventStreamsClient` singleton in Program.cs | — |
| 3 | Implement `WikiEditsWatcher` service | `MediawikiRevisionCreateEvents` |
| 4 | Implement `WikiTrendsEndpoint` (GET api/trends/wiki-edits) | — |
| 5 | Unit tests (StubHandler, no network) + integration auth tests | — |

---

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, verbatim — each operation that takes input takes ONE request record as its first parameter, built with an object initializer whose property names are the record's own, never flat arguments.
> ⚠ Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type.

### Operation: `MediawikiRevisionCreateEvents`

| Field | Value | Source |
|---|---|---|
| Controller property | `client` (root) | `WikimediaEventStreamsClient.cs` |
| Method signature | `MediawikiRevisionCreateEvents(MediawikiRevisionCreateEventsRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `map/operations/WikimediaEventStreamsClient.md` |
| Query params wire←C# | `since ← Since` | same page |
| Returns | `IAsyncEnumerable<MediawikiRevisionCreate>` | same page |
| Error | Case B: `ApiException<RawError>` (thrown on non-2xx before stream open) | same page |
| Pagination | None (streaming, not paginated) | defaults table |
| Server group | Default (`https://stream.wikimedia.org`) | `sdk-map.md` Servers section |

### Request record: `MediawikiRevisionCreateEventsRequest`

| Member | Type | Required? | Notes | Source |
|---|---|---|---|---|
| `Since` | `string?` | no | ISO8601 or epoch ms; omit to stream from now | `Requests/MediawikiRevisionCreateEventsRequest.cs` |
| `LastEventId` | `IReadOnlyList<LastEventId>?` | no | Resume from offset; omit for new connection | same |

### Response model: `MediawikiRevisionCreate`

| C# name | Wire name | Type | Required? | Source |
|---|---|---|---|---|
| `Schema` | `$schema` | `string` | yes | `Models/MediawikiRevisionCreate.cs` |
| `Database` | `database` | `string` | yes | same |
| `Dt` | `dt` | `DateTimeOffset` | yes | same |
| `Meta` | `meta` | `Meta` | yes | same |
| `PageId` | `page_id` | `int` | yes | same |
| `PageIsRedirect` | `page_is_redirect` | `bool` | yes | same |
| `PageNamespace` | `page_namespace` | `int` | yes | same |
| `PageTitle` | `page_title` | `string` | yes | same |
| `Performer` | `performer` | `Performer?` | no | same |
| `RevId` | `rev_id` | `int` | yes | same |
| `RevTimestamp` | `rev_timestamp` | `DateTimeOffset` | yes | same |
| `RevSlots` | `rev_slots` | `RevSlots?` | no | same |
| `RevLen` | `rev_len` | `int?` | no | same |
| `RevContentModel` | `rev_content_model` | `string?` | no | same |

**`Meta` fields used:**

| C# name | Wire name | Type | Required? | Source |
|---|---|---|---|---|
| `Domain` | `domain` | `string?` | no | `Models/Meta.cs` |
| `Stream` | `stream` | `string` | yes | same |
| `Dt` | `dt` | `DateTimeOffset?` | no | same |

**`Performer` fields used:**

| C# name | Wire name | Type | Required? | Source |
|---|---|---|---|---|
| `UserText` | `user_text` | `string?` | no | `Models/Performer.cs` |

**`RevSlots` fields:**

| C# name | Wire name | Type | Required? | Source |
|---|---|---|---|---|
| `Main` | `main` | `FragmentMediawikiRevisionSlot` | yes | `Models/RevSlots.cs` |
| `AdditionalProperties` | (extension data) | `AdditionalProperties<FragmentMediawikiRevisionSlot>` | — | same |

**`FragmentMediawikiRevisionSlot` fields:**

| C# name | Wire name | Type | Required? | Source |
|---|---|---|---|---|
| `RevSlotContentModel` | `rev_slot_content_model` | `string` | yes | `Models/FragmentMediawikiRevisionSlot.cs` |
| `RevSlotOriginRevId` | `rev_slot_origin_rev_id` | `int?` | no | same |
| `RevSlotSha1` | `rev_slot_sha1` | `string` | yes | same |
| `RevSlotSize` | `rev_slot_size` | `int` | yes | same |

### Client construction facts

| Fact | Value | Source |
|---|---|---|
| Constructor | `WikimediaEventStreamsClient(HttpClient httpClient, WikimediaEventStreamsClientOptions options)` | `WikimediaEventStreamsClient.cs`, `sdk-map.md` |
| DI extension | `services.AddWikimediaEventStreamsClient(options => { ... })` | `ServiceCollectionExtensions.cs` |
| Default environment | `ServerEnvironment.Production` → `https://stream.wikimedia.org` | `sdk-map.md` Servers |
| Auth | None | `sdk-map.md` Auth section |
| `StreamReadTimeout` | `TimeSpan?`, default 60s; bounds inter-frame idle wait | `WikimediaEventStreamsClientOptions.cs` |
| `Hooks` | `IReadOnlyList<SdkHook>` for request/response observation | `WikimediaEventStreamsClientOptions.cs`, `SdkHook.cs` |

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
|---|---|---|
| none | — | — |

---

## 3. Trap notes

| Step | Hazard | Consequence if missed | MUST load |
|---|---|---|---|
| 2 | `HttpClient`/handler lifetime — the client owns the pipeline after construction | Stale DNS or token cache exhaustion under a per-request client | **MUST load dotnet-client-initialization** |
| 2 | `LoggerFactory` null under `Add{Api}Client` → logging is already on from the container | Request URLs logged at Information by default | **MUST load dotnet-client-initialization** |
| 3 | `StreamReadTimeout` bounds inter-frame wait only, not total call time | Long hang if CancellationToken isn't also set | **MUST load dotnet-configuration-resilience** |
| 3 | `await foreach` requires `.WithCancellation(ct)` to stop on token fire | Enumeration continues past deadline | **MUST load dotnet-configuration-resilience** |
| 3 | SDK does not check Content-Type; a 2xx non-SSE body yields 0 items, no exception | Silent broken feed reported as quiet watch | **MUST load dotnet-configuration-resilience** (Hooks) |
| 3 | `SdkTimeoutException` is thrown both by per-attempt timeout and by `StreamReadTimeout` | Wrong stoppedBecause if not distinguished by `Timeout` property | **MUST load dotnet-error-handling** |
| 3 | `ResponseDeserializationException` is not `ApiException<TError>` — a catch ladder with only typed catches misses it | Uncaught exception escapes endpoint | **MUST load dotnet-error-handling** |
| 3 | Cancellation from caller's own token is NOT wrapped by SDK — `OperationCanceledException` passes through | Wrong stoppedBecause classification | **MUST load dotnet-error-handling** |
| 3 | `AdditionalProperties<FragmentMediawikiRevisionSlot>` enumerates as `KeyValuePair<string, TValue>` where `Value` is the typed object | Slot name must come from `.Key`, not `.Value` | **MUST load dotnet-models** |
| 5 | `StubHandler` must buffer request body inside `SendAsync`, before the SDK disposes it | `ObjectDisposedException` on captured request content | **MUST load dotnet-testing** |
| 5 | Retries: `SdkTimeoutException` on a non-retryable verb (`POST` not in `HttpMethodsToRetry`), but `GET` is retryable | Test may see multiple requests if stubbing a `GET` error | **MUST load dotnet-testing** |

---

## 4. REQUIRED READING

Load ALL of the following before starting implementation (from this plugin: `wikimedia:dotnet-*`):

| Skill | Governs step |
|---|---|
| `wikimedia:dotnet-client-initialization` | Step 2: DI registration, HttpClient lifetime |
| `wikimedia:dotnet-calling-endpoints` | Step 3: calling MediawikiRevisionCreateEvents |
| `wikimedia:dotnet-error-handling` | Step 3: catch ladder, ResponseDeserializationException |
| `wikimedia:dotnet-configuration-resilience` | Step 3: StreamReadTimeout, CancellationToken, Hooks |
| `wikimedia:dotnet-models` | Step 3: AdditionalProperties enumeration |
| `wikimedia:dotnet-testing` | Step 5: StubHandler, fake SSE |

> ⚠ These skills are to be loaded BEFORE implementation starts. This sheet deliberately does not carry their contents.

> ⚠ A body that does not match its declared type — a drifted or malformed 2xx response (a missing `required` member) or a non-2xx body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is NOT an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

---

## 5. PRODUCTION READINESS

| # | Concern | Decision |
|---|---|---|
| 1 | Credential fail-fast | N/A — Wikimedia EventStreams has no credentials. No auth properties on options. |
| 2 | Secret sourcing & rotation | N/A — no secrets. |
| 3 | Total timeout budget | Caller gets `n + 20` seconds. Enforced via a linked `CancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(n + 20))`. Per-attempt `StreamReadTimeout = 15s` (inter-frame idle). No retry on stream (retries don't apply once stream is open). |
| 4 | Write-retry ownership | N/A — no writes. |
| 5 | Idempotency & ambiguous writes | N/A — no writes. |
| 6 | Observability | SDK logs request line (`Information`) via container `ILoggerFactory`. `LogRequestBody = false` (no body on GET stream). Correlation available via `Meta.RequestId` on events. |
| 7 | Sensitive data | `MediawikiRevisionCreateEventsRequest` has no sensitive fields (only optional `since` / `LastEventId`). `LogRequestBody` stays `false`. `LoggerFactory` populated by DI extension — no env-var exposure. |
| 8 | Environment selection | Single environment: `ServerEnvironment.Production` → `https://stream.wikimedia.org`. No sandbox; test coverage uses `StubHandler` to avoid hitting live. |
| 9 | Duplicate prevention under concurrency | N/A — read-only streaming endpoint, no writes. |
| 10 | Partial results | N/A — streaming endpoint that collects for `n` seconds then returns; result is always the full `n`-second window. |
| 11 | Unknown outcomes | N/A — no writes. |

**DUPLICATE CLAIMS:** none

**PAGED READS:** none (streaming, not paginated)

**UNKNOWN OUTCOMES:** none

---

## 6. Assumptions & Blockers

**Assumptions:**
- `MediawikiRevisionCreateEvents` streams revision-create events across all wikis; filtering to `en.wikipedia.org` and `commons.wikimedia.org` is done client-side via `meta.domain`.
- Catalog brands and types are loaded from the in-memory DB on each request (seeded data: brands = Azure/.NET/Visual Studio/SQL Server/Other; types = Mug/T-Shirt/Sheet/USB Memory Stick).
- The User-Agent header set by the SDK (`WikimediaEventStreamsClient/...`) is replaced via `SdkHook.OnRequest` to satisfy Wikimedia's policy.
- A 2xx response body that is not `text/event-stream` (non-SSE body) is detected via `SdkHook.OnResponse` inspecting `Content-Type`, and reported as `stream-error`.

**Blockers:** None.
