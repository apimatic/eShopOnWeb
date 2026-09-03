<!-- Generated file — do not edit; regenerated with the SDK. -->

# SubscriptionStatus — operations

Accessor: `client.SubscriptionStatus` · Source: `Api/SubscriptionStatus.cs` · 10 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### CancelDelayedCancellation

- **Signature**: `CancelDelayedCancellation(int subscriptionId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `DelayedCancellationResponse`
- **Error**: `SdkException<CancelDelayedCancellationError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `DelayedCancellationResponse` | `Models/DelayedCancellationResponse.cs` |
| `CancelDelayedCancellationError` | `Errors/CancelDelayedCancellationError.cs` |

### CancelDunning

- **Signature**: `CancelDunning(int subscriptionId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `SubscriptionResponse`
- **Error**: `SdkException<CancelDunningError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `SubscriptionResponse` | `Models/SubscriptionResponse.cs` |
| `CancelDunningError` | `Errors/CancelDunningError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### CancelSubscription

- **Signature**: `CancelSubscription(int subscriptionId, CancellationRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `SubscriptionResponse`
- **Error**: `SdkException<CancelSubscriptionApiError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetCancelSubscriptionErrorResponse(out CancelSubscriptionErrorResponse)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CancellationRequest` | `Models/CancellationRequest.cs` |
| `SubscriptionResponse` | `Models/SubscriptionResponse.cs` |
| `CancelSubscriptionApiError` | `Errors/CancelSubscriptionApiError.cs` |
| `CancelSubscriptionErrorResponse` | `Models/AnyOf/CancelSubscriptionErrorResponse.cs` |

### InitiateDelayedCancellation

- **Signature**: `InitiateDelayedCancellation(int subscriptionId, CancellationRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `DelayedCancellationResponse`
- **Error**: `SdkException<InitiateDelayedCancellationError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CancellationRequest` | `Models/CancellationRequest.cs` |
| `DelayedCancellationResponse` | `Models/DelayedCancellationResponse.cs` |
| `InitiateDelayedCancellationError` | `Errors/InitiateDelayedCancellationError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### PauseSubscription

- **Signature**: `PauseSubscription(int subscriptionId, PauseRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `SubscriptionResponse`
- **Error**: `SdkException<PauseSubscriptionError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `PauseRequest` | `Models/PauseRequest.cs` |
| `SubscriptionResponse` | `Models/SubscriptionResponse.cs` |
| `PauseSubscriptionError` | `Errors/PauseSubscriptionError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### PreviewRenewal

- **Signature**: `PreviewRenewal(int subscriptionId, RenewalPreviewRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `RenewalPreviewResponse`
- **Error**: `SdkException<PreviewRenewalError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `RenewalPreviewRequest` | `Models/RenewalPreviewRequest.cs` |
| `RenewalPreviewResponse` | `Models/RenewalPreviewResponse.cs` |
| `PreviewRenewalError` | `Errors/PreviewRenewalError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### ReactivateSubscription

- **Signature**: `ReactivateSubscription(int subscriptionId, ReactivateSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `SubscriptionResponse`
- **Error**: `SdkException<ReactivateSubscriptionError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ReactivateSubscriptionRequest` | `Models/ReactivateSubscriptionRequest.cs` |
| `SubscriptionResponse` | `Models/SubscriptionResponse.cs` |
| `ReactivateSubscriptionError` | `Errors/ReactivateSubscriptionError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### ResumeSubscription

- **Signature**: `ResumeSubscription(int subscriptionId, ResumptionCharge? calendarBillingResumptionCharge, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `calendarBillingResumptionCharge` — nullable, no default → **must pass explicitly**
- **Query params (wire ← C#)**: `calendar_billing['resumption_charge']` ← `calendarBillingResumptionCharge`
- **Returns**: `SubscriptionResponse`
- **Error**: `SdkException<ResumeSubscriptionError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ResumptionCharge` | `Models/Enums/ResumptionCharge.cs` |
| `SubscriptionResponse` | `Models/SubscriptionResponse.cs` |
| `ResumeSubscriptionError` | `Errors/ResumeSubscriptionError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### RetrySubscription

- **Signature**: `RetrySubscription(int subscriptionId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `SubscriptionResponse`
- **Error**: `SdkException<RetrySubscriptionError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `SubscriptionResponse` | `Models/SubscriptionResponse.cs` |
| `RetrySubscriptionError` | `Errors/RetrySubscriptionError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### UpdateAutomaticSubscriptionResumption

- **Signature**: `UpdateAutomaticSubscriptionResumption(int subscriptionId, PauseRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `SubscriptionResponse`
- **Error**: `SdkException<UpdateAutomaticSubscriptionResumptionError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `PauseRequest` | `Models/PauseRequest.cs` |
| `SubscriptionResponse` | `Models/SubscriptionResponse.cs` |
| `UpdateAutomaticSubscriptionResumptionError` | `Errors/UpdateAutomaticSubscriptionResumptionError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

