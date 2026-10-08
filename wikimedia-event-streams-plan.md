# Wikimedia EventStreams — Integration Plan

## 1. Scope & sequence

| Step | Description | Operation(s) used |
|---|---|---|
| 1 | Add SDK reference; update global.json for .NET 10 | — |
| 2 | Register `WikimediaEventStreamsClient` + `IWikiRevisionStream` in DI | — |
| 3 | Implement `WikiEditsEndpoint` — watch stream for n seconds, filter, return JSON | `MediawikiRevisionCreateEvents` |
| 4 | Write unit/integration tests without network | `MediawikiRevisionCreateEvents` (faked) |

---

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, verbatim — each operation that takes input takes ONE request record as its
> first parameter (never flat arguments); build it with an object initializer using the record's own property
> names. Every SDK type is written fully-qualified with the namespace its source path implies.

### Per-operation rows

| Fact | Detail |
|---|---|
| Controller property | `client` (root — no group) |
| Method | `MediawikiRevisionCreateEvents(MediawikiRevisionCreateEventsRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` |
| Request record + members | `WikimediaEventStreams.Requests.MediawikiRevisionCreateEventsRequest` · `Since: string?` (optional) · `LastEventId: IReadOnlyList<LastEventId>?` (optional) — no required members |
| Return type | `Task<IAsyncEnumerable<MediawikiRevisionCreate>>` — `await` once to open the stream, `await foreach` to enumerate frames |
| Error on open | `ApiException<RawError>` — **Case B** — `ex.Error.StatusCode`, `ex.Error.ReadAsString()` |
| Errors during enumeration | `SdkTimeoutException` (no frame within `StreamReadTimeout`), `ResponseDeserializationException` (bad frame), `SdkConnectionException` (dropped connection) |
| Pagination | None |
| Source | `map/operations/WikimediaEventStreamsClient.md` + `Requests/MediawikiRevisionCreateEventsRequest.cs` |

### Response model fields used

**`MediawikiRevisionCreate`** (`Models/MediawikiRevisionCreate.cs`):
| C# name | Wire name | Type | Required? |
|---|---|---|---|
| `Meta` | `meta` | `Meta` | required |
| `PageTitle` | `page_title` | `string` | required |
| `RevId` | `rev_id` | `int` | required |
| `Performer` | `performer` | `Performer?` | optional |
| `RevTimestamp` | `rev_timestamp` | `DateTimeOffset` | required |
| `RevSlots` | `rev_slots` | `RevSlots?` | optional |

**`Meta`** (`Models/Meta.cs`): `Domain: string?` (wire: `domain`) — identifies the wiki.

**`Performer`** (`Models/Performer.cs`): `UserText: string?` (wire: `user_text`) — editor name.

**`RevSlots`** (`Models/RevSlots.cs`): `Main: FragmentMediawikiRevisionSlot` (required) + `AdditionalProperties<FragmentMediawikiRevisionSlot>` (extra named slots, enumerable as `KeyValuePair<string, FragmentMediawikiRevisionSlot>`).

**`FragmentMediawikiRevisionSlot`** (`Models/FragmentMediawikiRevisionSlot.cs`):
| C# name | Wire name | Type | Required? |
|---|---|---|---|
| `RevSlotContentModel` | `rev_slot_content_model` | `string` | required |
| `RevSlotSize` | `rev_slot_size` | `int` | required |
| `RevSlotOriginRevId` | `rev_slot_origin_rev_id` | `int?` | optional |
| `RevSlotSha1` | `rev_slot_sha1` | `string` | required |

### Client construction facts

| Fact | Detail |
|---|---|
| Client class | `WikimediaEventStreams.WikimediaEventStreamsClient` |
| Options class | `WikimediaEventStreams.WikimediaEventStreamsClientOptions` |
| Constructor | `WikimediaEventStreamsClient(HttpClient httpClient, WikimediaEventStreamsClientOptions options)` |
| Auth | None — no credentials on options |
| Environment | `WikimediaEventStreams.Servers.ServerEnvironment.Production` (default → `https://stream.wikimedia.org`) |
| `StreamReadTimeout` | `TimeSpan?` on options — default 60s; set to 15s for "no-data" detection |
| `Retry` | `RetryOptions.Default()` — disable with `MaxRetries = 0` (streaming: retrying a dropped stream re-opens it, which is not the same operation) |

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
|---|---|---|
| none | — | — |

---

## 3. Trap notes

| Step | Hazard | Skill |
|---|---|---|
| 2 | SDK client is long-lived; `HttpClient` lifetime and `PooledConnectionLifetime` matter for DNS hygiene — consequence: stale IP after provider failover **MUST load wikimedia:dotnet-client-initialization** |
| 3 | Return type is `Task<IAsyncEnumerable<T>>` not `IAsyncEnumerable<T>` — `await` first, then `foreach`; opening error on the `await`, streaming errors inside `foreach` — consequence: wrong error handling if not awaited **MUST load wikimedia:dotnet-calling-endpoints** |
| 3 | `SdkTimeoutException` is the idle-window leaf; `OperationCanceledException` from caller token passes through unwrapped; the two look similar — consequence: misclassifying our timer as stream-error **MUST load wikimedia:dotnet-error-handling** |
| 3 | `options.Retry.Timeout` is per-attempt; `StreamReadTimeout` is per-frame idle window; `HttpClient.Timeout` is per-attempt from the transport — consequence: wrong total-budget math **MUST load wikimedia:dotnet-configuration-resilience** |
| 4 | SDK test seam is `HttpClient` constructor; `SdkTimeoutException` and `SdkConnectionException` have public constructors for direct construction; `ApiException<RawError>` does not — consequence: test fakery mis-routed **MUST load wikimedia:dotnet-testing** |

---

## 4. REQUIRED READING

Load before implementation starts. These skills govern the steps above; the sheet does not carry their
contents.

| Skill (plugin-qualified) | Step governed |
|---|---|
| `wikimedia:dotnet-client-initialization` | Step 2 — client & DI setup |
| `wikimedia:dotnet-calling-endpoints` | Step 3 — calling `MediawikiRevisionCreateEvents` |
| `wikimedia:dotnet-error-handling` | Steps 3 & 4 — error boundary |
| `wikimedia:dotnet-configuration-resilience` | Step 3 — retries, `StreamReadTimeout`, total budget |
| `wikimedia:dotnet-testing` | Step 4 — fake `HttpClient` seam, exception construction |

⚠ Always hazard: a body that does not match its declared type — a drifted or malformed **2xx** response (a
missing `required` member) or a **non-2xx** body that does not match its operation's generated
`{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps
the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that
handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException`
(or `ApiException`).

---

## 5. PRODUCTION READINESS

| # | Concern | Decision |
|---|---|---|
| 1 | Credential fail-fast | No credentials — the API is public. N/A. |
| 2 | Secret sourcing & rotation | No secrets. N/A. |
| 3 | Total timeout budget | Budget = n + 20s (n from query param, max 60s, so max 80s). Enforced by a `CancellationToken` linked to the HTTP request's `RequestAborted` token, with `CancelAfter(n)`. The overall request timeout is not separately enforced — ASP.NET Core's default request timeout is sufficient for n≤60. `StreamReadTimeout = 15s` bounds each idle gap. |
| 4 | Write-retry ownership | No writes. N/A. |
| 5 | Idempotency & ambiguous writes | No writes. N/A. |
| 6 | Observability | SDK built-in logger at Information level (request line, status). `LoggerFactory` set from container. `LogRequestBody = false` (default). Errors surfaced as `stoppedBecause` in response. |
| 7 | Sensitive data | Request model has no PII fields (only `Since` and `LastEventId`). `LogRequestBody` left false. |
| 8 | Environment selection | One server group `Default`, one environment `Production` → `https://stream.wikimedia.org`. No sandbox environment. Tests use a fake `IWikiRevisionStream` and never reach Wikimedia. |
| 9 | Duplicate prevention under concurrency | No writes. N/A. |
| 10 | Partial results | N/A — this is a time-bounded stream scan, not a paged read. |
| 11 | Unknown outcomes | No writes. N/A. |

**DUPLICATE CLAIMS**: none

**PAGED READS**: none

**UNKNOWN OUTCOMES**: none

---

## 6. Assumptions & Blockers

None. All required capabilities are available in the SDK:
- `MediawikiRevisionCreateEvents` returns all wiki revision-create events globally; filtering by `meta.domain` distinguishes en.wikipedia.org from commons.wikimedia.org.
- `StreamReadTimeout` provides the 15-second no-data idle detection.
- `IWikiRevisionStream` interface abstraction enables tests without network access.

---

## 7. Source references

| Item | Source |
|---|---|
| `MediawikiRevisionCreateEvents` signature | `map/operations/WikimediaEventStreamsClient.md` |
| `MediawikiRevisionCreateEventsRequest` shape | `Requests/MediawikiRevisionCreateEventsRequest.cs` |
| `MediawikiRevisionCreate` shape | `Models/MediawikiRevisionCreate.cs` |
| `Meta` shape | `Models/Meta.cs` |
| `Performer` shape | `Models/Performer.cs` |
| `RevSlots` shape | `Models/RevSlots.cs` |
| `FragmentMediawikiRevisionSlot` shape | `Models/FragmentMediawikiRevisionSlot.cs` |
| `AdditionalProperties<T>` shape | `Core/Models/AdditionalProperties.cs` |
| `WikimediaEventStreamsClient` constructor | `WikimediaEventStreamsClient.cs` |
| `WikimediaEventStreamsClientOptions` properties | `sdk-map.md` |
| Error types | `sdk-map.md` (Error-handling model section) |
