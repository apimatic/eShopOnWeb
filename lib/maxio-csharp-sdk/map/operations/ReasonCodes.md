<!-- Generated file — do not edit; regenerated with the SDK. -->

# ReasonCodes — operations

Accessor: `client.ReasonCodes` · Source: `Api/ReasonCodes.cs` · 5 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### CreateReasonCode

- **Signature**: `CreateReasonCode(CreateReasonCodeRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ReasonCodeResponse`
- **Error**: `SdkException<CreateReasonCodeError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CreateReasonCodeRequest` | `Models/CreateReasonCodeRequest.cs` |
| `ReasonCodeResponse` | `Models/ReasonCodeResponse.cs` |
| `CreateReasonCodeError` | `Errors/CreateReasonCodeError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### DeleteReasonCode

- **Signature**: `DeleteReasonCode(int reasonCodeId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `OkResponse`
- **Error**: `SdkException<DeleteReasonCodeError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `OkResponse` | `Models/OkResponse.cs` |
| `DeleteReasonCodeError` | `Errors/DeleteReasonCodeError.cs` |

### ListReasonCodes

- **Signature**: `ListReasonCodes(int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - defaults: `page` = `1`, `perPage` = `20`
- **Query params (wire ← C#)**: `page` ← `page`, `per_page` ← `perPage`
- **Returns**: `IReadOnlyList<ReasonCodeResponse>`
- **Error**: `SdkException<ListReasonCodesError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ReasonCodeResponse` | `Models/ReasonCodeResponse.cs` |
| `ListReasonCodesError` | `Errors/ListReasonCodesError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### ReadReasonCode

- **Signature**: `ReadReasonCode(int reasonCodeId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `ReasonCodeResponse`
- **Error**: `SdkException<ReadReasonCodeError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ReasonCodeResponse` | `Models/ReasonCodeResponse.cs` |
| `ReadReasonCodeError` | `Errors/ReadReasonCodeError.cs` |

### UpdateReasonCode

- **Signature**: `UpdateReasonCode(int reasonCodeId, UpdateReasonCodeRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ReasonCodeResponse`
- **Error**: `SdkException<UpdateReasonCodeError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `UpdateReasonCodeRequest` | `Models/UpdateReasonCodeRequest.cs` |
| `ReasonCodeResponse` | `Models/ReasonCodeResponse.cs` |
| `UpdateReasonCodeError` | `Errors/UpdateReasonCodeError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

