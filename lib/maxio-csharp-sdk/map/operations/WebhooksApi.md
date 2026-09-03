<!-- Generated file — do not edit; regenerated with the SDK. -->

# WebhooksApi — operations

Accessor: `client.WebhooksApi` · Source: `Api/WebhooksApi.cs` · 6 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### CreateEndpoint

- **Signature**: `CreateEndpoint(CreateOrUpdateEndpointRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `EndpointResponse`
- **Error**: `SdkException<CreateEndpointError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CreateOrUpdateEndpointRequest` | `Models/CreateOrUpdateEndpointRequest.cs` |
| `EndpointResponse` | `Models/EndpointResponse.cs` |
| `CreateEndpointError` | `Errors/CreateEndpointError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### EnableWebhooks

- **Signature**: `EnableWebhooks(EnableWebhooksRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `EnableWebhooksResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `EnableWebhooksRequest` | `Models/EnableWebhooksRequest.cs` |
| `EnableWebhooksResponse` | `Models/EnableWebhooksResponse.cs` |

### ListEndpoints

- **Signature**: `ListEndpoints(RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `IReadOnlyList<Endpoint>`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `Endpoint` | `Models/Endpoint.cs` |

### ListWebhooks

- **Signature**: `ListWebhooks(WebhookStatus? status, string? sinceDate, string? untilDate, WebhookOrder? order, int? subscription, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 5 params (`status` … `subscription`) — nullable, no default → **must pass explicitly** (pass `null` to skip)
  - defaults: `page` = `1`, `perPage` = `20`
- **Query params (wire ← C#)**: `status` ← `status`, `since_date` ← `sinceDate`, `until_date` ← `untilDate`, `page` ← `page`, `per_page` ← `perPage`, `order` ← `order`, `subscription` ← `subscription`
- **Returns**: `IReadOnlyList<WebhookResponse>`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `WebhookStatus` | `Models/Enums/WebhookStatus.cs` |
| `WebhookOrder` | `Models/Enums/WebhookOrder.cs` |
| `WebhookResponse` | `Models/WebhookResponse.cs` |

### ReplayWebhooks

- **Signature**: `ReplayWebhooks(ReplayWebhooksRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ReplayWebhooksResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `ReplayWebhooksRequest` | `Models/ReplayWebhooksRequest.cs` |
| `ReplayWebhooksResponse` | `Models/ReplayWebhooksResponse.cs` |

### UpdateEndpoint

- **Signature**: `UpdateEndpoint(int endpointId, CreateOrUpdateEndpointRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `EndpointResponse`
- **Error**: `SdkException<UpdateEndpointError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CreateOrUpdateEndpointRequest` | `Models/CreateOrUpdateEndpointRequest.cs` |
| `EndpointResponse` | `Models/EndpointResponse.cs` |
| `UpdateEndpointError` | `Errors/UpdateEndpointError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

