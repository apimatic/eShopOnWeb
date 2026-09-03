<!-- Generated file — do not edit; regenerated with the SDK. -->

# SubscriptionNotes — operations

Accessor: `client.SubscriptionNotes` · Source: `Api/SubscriptionNotes.cs` · 5 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### CreateSubscriptionNote

- **Signature**: `CreateSubscriptionNote(int subscriptionId, UpdateSubscriptionNoteRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `SubscriptionNoteResponse`
- **Error**: `SdkException<CreateSubscriptionNoteError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `UpdateSubscriptionNoteRequest` | `Models/UpdateSubscriptionNoteRequest.cs` |
| `SubscriptionNoteResponse` | `Models/SubscriptionNoteResponse.cs` |
| `CreateSubscriptionNoteError` | `Errors/CreateSubscriptionNoteError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### DeleteSubscriptionNote

- **Signature**: `DeleteSubscriptionNote(int subscriptionId, int noteId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `void` (Task)
- **Error**: `SdkException<RawError>` — **Case B**

### ListSubscriptionNotes

- **Signature**: `ListSubscriptionNotes(int subscriptionId, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - defaults: `page` = `1`, `perPage` = `20`
- **Query params (wire ← C#)**: `page` ← `page`, `per_page` ← `perPage`
- **Returns**: `IReadOnlyList<SubscriptionNoteResponse>`
- **Error**: `SdkException<ListSubscriptionNotesError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `SubscriptionNoteResponse` | `Models/SubscriptionNoteResponse.cs` |
| `ListSubscriptionNotesError` | `Errors/ListSubscriptionNotesError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### ReadSubscriptionNote

- **Signature**: `ReadSubscriptionNote(int subscriptionId, int noteId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `SubscriptionNoteResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `SubscriptionNoteResponse` | `Models/SubscriptionNoteResponse.cs` |

### UpdateSubscriptionNote

- **Signature**: `UpdateSubscriptionNote(int subscriptionId, int noteId, UpdateSubscriptionNoteRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `SubscriptionNoteResponse`
- **Error**: `SdkException<UpdateSubscriptionNoteError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `UpdateSubscriptionNoteRequest` | `Models/UpdateSubscriptionNoteRequest.cs` |
| `SubscriptionNoteResponse` | `Models/SubscriptionNoteResponse.cs` |
| `UpdateSubscriptionNoteError` | `Errors/UpdateSubscriptionNoteError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

