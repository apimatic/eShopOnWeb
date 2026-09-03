<!-- Generated file — do not edit; regenerated with the SDK. -->

# Orders — operations

Accessor: `client.Orders` · Source: `Api/Orders.cs` · 8 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### AuthorizeOrder

- **Signature**: `AuthorizeOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderAuthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 5 params (`payPalMockResponse` … `body`) — nullable, no default → **must pass explicitly** (pass `null` to skip)
  - defaults: `prefer` = `"return=minimal"`
- **Returns**: `OrderAuthorizeResponse`
- **Error**: `SdkException<AuthorizeOrderError>` — **Case A (typed)**
- **Error accessors**: `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `OrderAuthorizeRequest` | `Models/OrderAuthorizeRequest.cs` |
| `OrderAuthorizeResponse` | `Models/OrderAuthorizeResponse.cs` |
| `AuthorizeOrderError` | `Errors/AuthorizeOrderError.cs` |
| `Error` | `Models/Error.cs` |

### CaptureOrder

- **Signature**: `CaptureOrder(string id, string? payPalMockResponse, string? payPalRequestId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderCaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 5 params (`payPalMockResponse` … `body`) — nullable, no default → **must pass explicitly** (pass `null` to skip)
  - defaults: `prefer` = `"return=minimal"`
- **Returns**: `Order`
- **Error**: `SdkException<CaptureOrderError>` — **Case A (typed)**
- **Error accessors**: `TryGetError(out Error)` [400, 401, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `OrderCaptureRequest` | `Models/OrderCaptureRequest.cs` |
| `Order` | `Models/Order.cs` |
| `CaptureOrderError` | `Errors/CaptureOrderError.cs` |
| `Error` | `Models/Error.cs` |

### ConfirmOrder

- **Signature**: `ConfirmOrder(string id, string? payPalClientMetadataId, string? payPalAuthAssertion, ConfirmOrderRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `payPalClientMetadataId` — nullable, no default → **must pass explicitly**
  - `payPalAuthAssertion` — nullable, no default → **must pass explicitly**
  - `body` — nullable, no default → **must pass explicitly**
  - defaults: `prefer` = `"return=minimal"`
- **Returns**: `Order`
- **Error**: `SdkException<ConfirmOrderError>` — **Case A (typed)**
- **Error accessors**: `TryGetError(out Error)` [400, 403, 422, 500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ConfirmOrderRequest` | `Models/ConfirmOrderRequest.cs` |
| `Order` | `Models/Order.cs` |
| `ConfirmOrderError` | `Errors/ConfirmOrderError.cs` |
| `Error` | `Models/Error.cs` |

### CreateOrder

- **Signature**: `CreateOrder(string? payPalMockResponse, string? payPalRequestId, string? payPalPartnerAttributionId, string? payPalClientMetadataId, string? payPalAuthAssertion, OrderRequest body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 5 params (`payPalMockResponse` … `payPalAuthAssertion`) — nullable, no default → **must pass explicitly** (pass `null` to skip)
  - defaults: `prefer` = `"return=minimal"`
- **Returns**: `Order`
- **Error**: `SdkException<CreateOrderError>` — **Case A (typed)**
- **Error accessors**: `TryGetError(out Error)` [400, 401, 422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `OrderRequest` | `Models/OrderRequest.cs` |
| `Order` | `Models/Order.cs` |
| `CreateOrderError` | `Errors/CreateOrderError.cs` |
| `Error` | `Models/Error.cs` |

### CreateOrderTracking

- **Signature**: `CreateOrderTracking(string id, string? payPalAuthAssertion, OrderTrackerRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `payPalAuthAssertion` — nullable, no default → **must pass explicitly**
- **Returns**: `Order`
- **Error**: `SdkException<CreateOrderTrackingError>` — **Case A (typed)**
- **Error accessors**: `TryGetError(out Error)` [400, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `OrderTrackerRequest` | `Models/OrderTrackerRequest.cs` |
| `Order` | `Models/Order.cs` |
| `CreateOrderTrackingError` | `Errors/CreateOrderTrackingError.cs` |
| `Error` | `Models/Error.cs` |

### GetOrder

- **Signature**: `GetOrder(string id, string? fields, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `fields` — nullable, no default → **must pass explicitly**
  - `payPalMockResponse` — nullable, no default → **must pass explicitly**
  - `payPalAuthAssertion` — nullable, no default → **must pass explicitly**
- **Query params (wire ← C#)**: `fields` ← `fields`
- **Returns**: `Order`
- **Error**: `SdkException<GetOrderError>` — **Case A (typed)**
- **Error accessors**: `TryGetError(out Error)` [401, 404] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `Order` | `Models/Order.cs` |
| `GetOrderError` | `Errors/GetOrderError.cs` |
| `Error` | `Models/Error.cs` |

### PatchOrder

- **Signature**: `PatchOrder(string id, string? payPalMockResponse, string? payPalAuthAssertion, IReadOnlyList<Patch>? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `payPalMockResponse` — nullable, no default → **must pass explicitly**
  - `payPalAuthAssertion` — nullable, no default → **must pass explicitly**
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `void` (Task)
- **Error**: `SdkException<PatchOrderError>` — **Case A (typed)**
- **Error accessors**: `TryGetError(out Error)` [400, 401, 404, 422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `Patch` | `Models/Patch.cs` |
| `PatchOrderError` | `Errors/PatchOrderError.cs` |
| `Error` | `Models/Error.cs` |

### UpdateOrderTracking

- **Signature**: `UpdateOrderTracking(string id, string trackerId, string? payPalAuthAssertion, IReadOnlyList<Patch>? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `payPalAuthAssertion` — nullable, no default → **must pass explicitly**
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `void` (Task)
- **Error**: `SdkException<UpdateOrderTrackingError>` — **Case A (typed)**
- **Error accessors**: `TryGetError(out Error)` [400, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `Patch` | `Models/Patch.cs` |
| `UpdateOrderTrackingError` | `Errors/UpdateOrderTrackingError.cs` |
| `Error` | `Models/Error.cs` |

