# Wikimedia EventStreams Integration Plan
<!-- wikimedia-event-streams-plan.md -->

## 1. Scope & Sequence

1. Fix `global.json` (rollForward latestMajor) so .NET 10 SDK builds the project.
2. Add SDK `<ProjectReference>` to `src/PublicApi/PublicApi.csproj` and `tests/UnitTests/UnitTests.csproj`.
3. Register `WikimediaEventStreamsClient` (singleton, with custom User-Agent hook) and `IWikiTrendsService`/`WikiTrendsService` (scoped) in `src/PublicApi/Program.cs`.
4. Create endpoint files under `src/PublicApi/WikiEditsEndpoints/`:
   - `WikiEditsRequest.cs`, `WikiEditsResponse.cs`, `WikiEditDto.cs`, `RevisionSlotDto.cs`
   - `IWikiTrendsService.cs`, `WikiTrendsService.cs`, `WikiEditsEndpoint.cs`
5. Write unit tests in `tests/UnitTests/WikiEditsEndpoints/WikiTrendsServiceTests.cs`.
6. Run `dotnet build` → `dotnet test` → live 20-second smoke test.

Operations used: `RevisionCreateEvents` only.

---

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code; every input to an operation travels on **one request record** built with an object initializer using the record's own property names — never flat arguments.
> ⚠ Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for that type.

### Operation row

| Field | Value |
|---|---|
| Controller accessor | `client` (root) |
| Method | `RevisionCreateEvents(RevisionCreateEventsRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` |
| Return type | `Task<IAsyncEnumerable<MediawikiRevisionCreate>>` (confirmed from source — `_rawClient.Execute(...)` pattern) |
| Request record | `RevisionCreateEventsRequest` (`Requests/RevisionCreateEventsRequest.cs`, namespace `WikimediaEventStreams.Requests`) |
| Request required members | none |
| Request optional members | `Since: string?`, `LastEventId: IReadOnlyList<LastEventId>?` |
| Error case | **Case B** — `ApiException<RawError>` |
| Error accessors | `ex.Error.StatusCode`, `ex.Error.ReadAsString()` |
| Pagination | none (streaming) |
| Source | `map/operations/WikimediaEventStreamsClient.md` → `WikimediaEventStreamsClient.cs` |

### Response type: `MediawikiRevisionCreate` (`Models/MediawikiRevisionCreate.cs`, ns `WikimediaEventStreams.Models`)

| Field (C# name) | Wire name | Type | Required? |
|---|---|---|---|
| `Schema` | `$schema` | `string` | yes |
| `Database` | `database` | `string` | yes |
| `Dt` | `dt` | `DateTimeOffset` | yes |
| `Meta` | `meta` | `Meta` | yes |
| `PageId` | `page_id` | `int` | yes |
| `PageIsRedirect` | `page_is_redirect` | `bool` | yes |
| `PageNamespace` | `page_namespace` | `int` | yes |
| `PageTitle` | `page_title` | `string` | yes |
| `RevId` | `rev_id` | `int` | yes |
| `RevTimestamp` | `rev_timestamp` | `DateTimeOffset` | yes |
| `Performer` | `performer` | `Performer?` | no |
| `RevSlots` | `rev_slots` | `RevSlots?` | no |

### `Meta` (`Models/Meta.cs`, ns `WikimediaEventStreams.Models`)

| Field | Wire | Type | Required? |
|---|---|---|---|
| `Domain` | `domain` | `string?` | no |
| `Dt` | `dt` | `DateTimeOffset?` | no |
| `Stream` | `stream` | `string` | yes |

### `Performer` (`Models/Performer.cs`, ns `WikimediaEventStreams.Models`)

| Field | Wire | Type | Required? |
|---|---|---|---|
| `UserText` | `user_text` | `string?` | no |
| `UserId` | `user_id` | `int?` | no |

### `RevSlots` (`Models/RevSlots.cs`, ns `WikimediaEventStreams.Models`)

| Field | Wire | Type | Required? |
|---|---|---|---|
| `Main` | `main` | `FragmentMediawikiRevisionSlot` | yes |
| `AdditionalProperties` | (extension bag) | `AdditionalProperties<FragmentMediawikiRevisionSlot>` | auto |

Iterating `RevSlots.AdditionalProperties` with `foreach` yields `KeyValuePair<string, FragmentMediawikiRevisionSlot>` for every non-main slot (e.g., Commons' structured-data slot). The key is the slot name.

### `FragmentMediawikiRevisionSlot` (`Models/FragmentMediawikiRevisionSlot.cs`, ns `WikimediaEventStreams.Models`)

| Field | Wire | Type | Required? |
|---|---|---|---|
| `RevSlotContentModel` | `rev_slot_content_model` | `string` | yes |
| `RevSlotSize` | `rev_slot_size` | `int` | yes |
| `RevSlotSha1` | `rev_slot_sha1` | `string` | yes |
| `RevSlotOriginRevId` | `rev_slot_origin_rev_id` | `int?` | no |

### Enum / union values needed

None — all fields used are primitive types.

### Client construction

| Fact | Value |
|---|---|
| Client class | `WikimediaEventStreamsClient` (ns `WikimediaEventStreams`) |
| Options class | `WikimediaEventStreamsClientOptions` (ns `WikimediaEventStreams`) |
| Auth | None |
| Environment | `ServerEnvironment.Production` (default) → `https://stream.wikimedia.org` |
| DI extension | `services.AddWikimediaEventStreamsClient(options => { ... })` |
| `StreamReadTimeout` | Set to `TimeSpan.FromSeconds(15)` for idle-stream detection |
| `Hooks` | `SdkHook.OnRequest` to replace `User-Agent` header |

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
|---|---|---|
| none | — | — |

---

## 3. Trap notes

| Step | Hazard | Skill |
|---|---|---|
| Client setup | `HttpClient` must be long-lived; `Add{Api}Client` registers singleton and captures options once — a rotated credential takes effect only on restart | **MUST load `wikimedia:dotnet-client-initialization`** |
| Client setup | `Logging.LoggerFactory` unset → `WIKIMEDIAEVENTSTREAMSCLIENT_LOG` env var can enable request-body logging from outside the code; `trace` forces it on | **MUST load `wikimedia:dotnet-configuration-resilience`** |
| Calling endpoint | Streaming returns `Task<IAsyncEnumerable<T>>` — `await` the call once to open, then `await foreach`; the sequence is single-shot; a second `await foreach` throws `ObjectDisposedException` | **MUST load `wikimedia:dotnet-calling-endpoints`** |
| Calling endpoint | `StreamReadTimeout` fires `SdkTimeoutException` when idle between frames, not `OperationCanceledException`; mis-catching it reports "no-data" as a connection error | **MUST load `wikimedia:dotnet-configuration-resilience`** |
| Error handling | A body that doesn't match its type throws `ResponseDeserializationException`, not `ApiException<TError>` — a catch ladder missing this case lets it escape | **MUST load `wikimedia:dotnet-error-handling`** |
| Error handling | `SdkTimeoutException` is a subclass of `SdkConnectionException` — catch it first or it falls into the generic connection-error arm and gets mis-labelled | **MUST load `wikimedia:dotnet-error-handling`** |
| Testing | `StubHandler` must buffer the request body during `SendAsync` — it's disposed after the call returns; `StubHandler.Bodies` is the correct capture point | **MUST load `wikimedia:dotnet-testing`** |

---

## 4. REQUIRED READING

All skills below are loaded before implementation starts. This sheet deliberately does not carry their contents.

| Skill (plugin-qualified) | Governs step |
|---|---|
| `wikimedia:dotnet-client-initialization` | Client construction & DI registration |
| `wikimedia:dotnet-calling-endpoints` | Building the request record; reading the streaming response |
| `wikimedia:dotnet-error-handling` | `try/catch` ladder; `ResponseDeserializationException`; `SdkTimeoutException` hierarchy |
| `wikimedia:dotnet-configuration-resilience` | `StreamReadTimeout`; `Timeout`; `Logging`; `SdkHook` for User-Agent |
| `wikimedia:dotnet-testing` | `StubHandler`; testing the streaming seam |

> ⚠ A body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape.

---

## 5. PRODUCTION READINESS

| # | Concern | Decision |
|---|---|---|
| 1 | Credential fail-fast | N/A — the Wikimedia API is fully public; no credentials are used. |
| 2 | Secret sourcing & rotation | N/A — no secrets. |
| 3 | Total timeout budget | Caller's budget: `n + 20` seconds, enforced by a `CancellationTokenSource` linked to the HTTP request token and cancelled after `n + 20` seconds in `WikiTrendsService`. The inner watch token cancels after `n` seconds. `options.Retry.Timeout` = 10 s (per attempt); the stream opening is a single GET and retries are default (GET-eligible). |
| 4 | Write-retry ownership | N/A — this integration makes no writes. The only call is `RevisionCreateEvents` (GET). |
| 5 | Idempotency & ambiguous writes | N/A — read-only. |
| 6 | Observability | `options.Logging.LoggerFactory` is supplied from the DI container via `AddWikimediaEventStreamsClient` (ASP.NET Core extension fills it automatically). Default level `Information` logs request URL + status. `LogRequestBody` defaults to `false` (no body on a GET, irrelevant here). |
| 7 | Sensitive data | `RevisionCreateEventsRequest` carries only `Since` (timestamp string) and `LastEventId` (pagination offsets) — no PII or credentials. `LogRequestBody` can remain `false`. |
| 8 | Environment selection | Single environment: `ServerEnvironment.Production` → `https://stream.wikimedia.org`. No sandbox environment is declared by the SDK; to avoid sending test traffic to the live system, tests use `StubHandler` (offline, no real HTTP). |
| 9 | Duplicate prevention under concurrency | N/A — this is a read-only streaming endpoint; no writes occur. |
| 10 | Partial results | Not applicable — the response is a bounded time-window scan, not a paged collection. The `stoppedBecause` field communicates why the scan ended. |
| 11 | Unknown outcomes | N/A — no writes are made. |

**DUPLICATE CLAIMS**: none

**PAGED READS**: none (streaming, bounded by time and idle-timeout)

**UNKNOWN OUTCOMES**: none (no writes)

---

## 6. Assumptions & Blockers

- **Assumption**: The `Meta.Domain` field on `MediawikiRevisionCreate` reliably identifies the wiki (e.g. `en.wikipedia.org`, `commons.wikimedia.org`). This is optional (`string?`); events missing it are skipped for matching/latestCommons tracking but still counted in `received`.
- **Assumption**: `RevisionCreateEvents` (the stream at `/v2/stream/mediawiki.revision-create`) covers all wikis including Commons. The operation's remarks in source confirm it streams all wiki revision creates.
- **No blockers.**
