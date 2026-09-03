<!-- Generated file — do not edit; regenerated with the SDK. -->

# Payments — operations

Accessor: `client.Payments` · Source: `Api/Payments.cs` · 7 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### CaptureAuthorizedPayment

- **Signature**: `CaptureAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, CaptureRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 4 params (`payPalMockResponse` … `body`) — nullable, no default → **must pass explicitly** (pass `null` to skip)
  - defaults: `prefer` = `"return=minimal"`
- **Returns**: `CapturedPayment`
- **Error**: `SdkException<CaptureAuthorizedPaymentError>` — **Case A (typed)**
- **Error accessors**: `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CaptureRequest` | `Models/CaptureRequest.cs` |
| `CapturedPayment` | `Models/CapturedPayment.cs` |
| `CaptureAuthorizedPaymentError` | `Errors/CaptureAuthorizedPaymentError.cs` |
| `Error` | `Models/Error.cs` |

### GetAuthorizedPayment

- **Signature**: `GetAuthorizedPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `payPalMockResponse` — nullable, no default → **must pass explicitly**
  - `payPalAuthAssertion` — nullable, no default → **must pass explicitly**
- **Returns**: `PaymentAuthorization`
- **Error**: `SdkException<GetAuthorizedPaymentError>` — **Case A (typed)**
- **Error accessors**: `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `PaymentAuthorization` | `Models/PaymentAuthorization.cs` |
| `GetAuthorizedPaymentError` | `Errors/GetAuthorizedPaymentError.cs` |
| `Error` | `Models/Error.cs` |

### GetCapturedPayment

- **Signature**: `GetCapturedPayment(string captureId, string? payPalMockResponse, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `payPalMockResponse` — nullable, no default → **must pass explicitly**
- **Returns**: `CapturedPayment`
- **Error**: `SdkException<GetCapturedPaymentError>` — **Case A (typed)**
- **Error accessors**: `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CapturedPayment` | `Models/CapturedPayment.cs` |
| `GetCapturedPaymentError` | `Errors/GetCapturedPaymentError.cs` |
| `Error` | `Models/Error.cs` |

### GetRefund

- **Signature**: `GetRefund(string refundId, string? payPalMockResponse, string? payPalAuthAssertion, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `payPalMockResponse` — nullable, no default → **must pass explicitly**
  - `payPalAuthAssertion` — nullable, no default → **must pass explicitly**
- **Returns**: `Refund`
- **Error**: `SdkException<GetRefundError>` — **Case A (typed)**
- **Error accessors**: `TryGetError(out Error)` [401, 403, 404] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `Refund` | `Models/Refund.cs` |
| `GetRefundError` | `Errors/GetRefundError.cs` |
| `Error` | `Models/Error.cs` |

### ReauthorizePayment

- **Signature**: `ReauthorizePayment(string authorizationId, string? payPalRequestId, string? payPalAuthAssertion, ReauthorizeRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `payPalRequestId` — nullable, no default → **must pass explicitly**
  - `payPalAuthAssertion` — nullable, no default → **must pass explicitly**
  - `body` — nullable, no default → **must pass explicitly**
  - defaults: `prefer` = `"return=minimal"`
- **Returns**: `PaymentAuthorization`
- **Error**: `SdkException<ReauthorizePaymentError>` — **Case A (typed)**
- **Error accessors**: `TryGetError(out Error)` [400, 401, 403, 404, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ReauthorizeRequest` | `Models/ReauthorizeRequest.cs` |
| `PaymentAuthorization` | `Models/PaymentAuthorization.cs` |
| `ReauthorizePaymentError` | `Errors/ReauthorizePaymentError.cs` |
| `Error` | `Models/Error.cs` |

### RefundCapturedPayment

- **Signature**: `RefundCapturedPayment(string captureId, string? payPalMockResponse, string? payPalRequestId, string? payPalAuthAssertion, RefundRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 4 params (`payPalMockResponse` … `body`) — nullable, no default → **must pass explicitly** (pass `null` to skip)
  - defaults: `prefer` = `"return=minimal"`
- **Returns**: `Refund`
- **Error**: `SdkException<RefundCapturedPaymentError>` — **Case A (typed)**
- **Error accessors**: `TryGetError(out Error)` [400, 401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `RefundRequest` | `Models/RefundRequest.cs` |
| `Refund` | `Models/Refund.cs` |
| `RefundCapturedPaymentError` | `Errors/RefundCapturedPaymentError.cs` |
| `Error` | `Models/Error.cs` |

### VoidPayment

- **Signature**: `VoidPayment(string authorizationId, string? payPalMockResponse, string? payPalAuthAssertion, string? payPalRequestId, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `payPalMockResponse` — nullable, no default → **must pass explicitly**
  - `payPalAuthAssertion` — nullable, no default → **must pass explicitly**
  - `payPalRequestId` — nullable, no default → **must pass explicitly**
  - defaults: `prefer` = `"return=minimal"`
- **Returns**: `PaymentAuthorization`
- **Error**: `SdkException<VoidPaymentError>` — **Case A (typed)**
- **Error accessors**: `TryGetError(out Error)` [401, 403, 404, 409, 422] · `TryGetNoContent(out RawError)` [500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `PaymentAuthorization` | `Models/PaymentAuthorization.cs` |
| `VoidPaymentError` | `Errors/VoidPaymentError.cs` |
| `Error` | `Models/Error.cs` |

