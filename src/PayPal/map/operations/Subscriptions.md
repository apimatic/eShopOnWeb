<!-- Generated file — do not edit; regenerated with the SDK. -->

# Subscriptions — operations

Accessor: `client.Subscriptions` · Source: `Api/Subscriptions.cs` · 17 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### ActivateBillingPlan

- **Signature**: `ActivateBillingPlan(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `void` (Task)
- **Error**: `SdkException<ActivateBillingPlanError>` — **Case A (typed)**
- **Error accessors**: `TryGetSubscriptionError(out SubscriptionError)` [401, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ActivateBillingPlanError` | `Errors/ActivateBillingPlanError.cs` |
| `SubscriptionError` | `Models/SubscriptionError.cs` |

### ActivateSubscription

- **Signature**: `ActivateSubscription(string id, ActivateSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `void` (Task)
- **Error**: `SdkException<ActivateSubscriptionError>` — **Case A (typed)**
- **Error accessors**: `TryGetSubscriptionError(out SubscriptionError)` [400, 401, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ActivateSubscriptionRequest` | `Models/ActivateSubscriptionRequest.cs` |
| `ActivateSubscriptionError` | `Errors/ActivateSubscriptionError.cs` |
| `SubscriptionError` | `Models/SubscriptionError.cs` |

### CancelSubscription

- **Signature**: `CancelSubscription(string id, CancelSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `void` (Task)
- **Error**: `SdkException<CancelSubscriptionError>` — **Case A (typed)**
- **Error accessors**: `TryGetSubscriptionError(out SubscriptionError)` [400, 401, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CancelSubscriptionRequest` | `Models/CancelSubscriptionRequest.cs` |
| `CancelSubscriptionError` | `Errors/CancelSubscriptionError.cs` |
| `SubscriptionError` | `Models/SubscriptionError.cs` |

### CaptureSubscription

- **Signature**: `CaptureSubscription(string id, string? payPalRequestId, CaptureSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `payPalRequestId` — nullable, no default → **must pass explicitly**
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `SubscriptionTransactionDetails`
- **Error**: `SdkException<CaptureSubscriptionError>` — **Case A (typed)**
- **Error accessors**: `TryGetSubscriptionError(out SubscriptionError)` [400, 401, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CaptureSubscriptionRequest` | `Models/CaptureSubscriptionRequest.cs` |
| `SubscriptionTransactionDetails` | `Models/SubscriptionTransactionDetails.cs` |
| `CaptureSubscriptionError` | `Errors/CaptureSubscriptionError.cs` |
| `SubscriptionError` | `Models/SubscriptionError.cs` |

### CreateBillingPlan

- **Signature**: `CreateBillingPlan(string? payPalRequestId, PlanRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `payPalRequestId` — nullable, no default → **must pass explicitly**
  - `body` — nullable, no default → **must pass explicitly**
  - defaults: `prefer` = `"return=minimal"`
- **Returns**: `BillingPlan`
- **Error**: `SdkException<CreateBillingPlanError>` — **Case A (typed)**
- **Error accessors**: `TryGetSubscriptionError(out SubscriptionError)` [400, 401, 403, 422, 500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `PlanRequest` | `Models/PlanRequest.cs` |
| `BillingPlan` | `Models/BillingPlan.cs` |
| `CreateBillingPlanError` | `Errors/CreateBillingPlanError.cs` |
| `SubscriptionError` | `Models/SubscriptionError.cs` |

### CreateSubscription

- **Signature**: `CreateSubscription(string? payPalRequestId, string? payPalClientMetadataId, CreateSubscriptionRequest? body, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `payPalRequestId` — nullable, no default → **must pass explicitly**
  - `payPalClientMetadataId` — nullable, no default → **must pass explicitly**
  - `body` — nullable, no default → **must pass explicitly**
  - defaults: `prefer` = `"return=minimal"`
- **Returns**: `Subscription`
- **Error**: `SdkException<CreateSubscriptionError>` — **Case A (typed)**
- **Error accessors**: `TryGetSubscriptionError(out SubscriptionError)` [400, 401, 403, 422, 500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CreateSubscriptionRequest` | `Models/CreateSubscriptionRequest.cs` |
| `Subscription` | `Models/Subscription.cs` |
| `CreateSubscriptionError` | `Errors/CreateSubscriptionError.cs` |
| `SubscriptionError` | `Models/SubscriptionError.cs` |

### DeactivateBillingPlan

- **Signature**: `DeactivateBillingPlan(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `void` (Task)
- **Error**: `SdkException<DeactivateBillingPlanError>` — **Case A (typed)**
- **Error accessors**: `TryGetSubscriptionError(out SubscriptionError)` [401, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `DeactivateBillingPlanError` | `Errors/DeactivateBillingPlanError.cs` |
| `SubscriptionError` | `Models/SubscriptionError.cs` |

### GetBillingPlan

- **Signature**: `GetBillingPlan(string id, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `BillingPlan`
- **Error**: `SdkException<GetBillingPlanError>` — **Case A (typed)**
- **Error accessors**: `TryGetSubscriptionError(out SubscriptionError)` [401, 403, 404, 500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `BillingPlan` | `Models/BillingPlan.cs` |
| `GetBillingPlanError` | `Errors/GetBillingPlanError.cs` |
| `SubscriptionError` | `Models/SubscriptionError.cs` |

### GetSubscription

- **Signature**: `GetSubscription(string id, string? fields, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `fields` — nullable, no default → **must pass explicitly**
- **Query params (wire ← C#)**: `fields` ← `fields`
- **Returns**: `Subscription`
- **Error**: `SdkException<GetSubscriptionError>` — **Case A (typed)**
- **Error accessors**: `TryGetSubscriptionError(out SubscriptionError)` [401, 403, 404, 500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `Subscription` | `Models/Subscription.cs` |
| `GetSubscriptionError` | `Errors/GetSubscriptionError.cs` |
| `SubscriptionError` | `Models/SubscriptionError.cs` |

### ListBillingPlans

- **Signature**: `ListBillingPlans(string? productId, int? pageSize = 10, int? page = 1, bool? totalRequired = false, string? prefer = "return=minimal", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `productId` — nullable, no default → **must pass explicitly**
  - defaults: `pageSize` = `10`, `page` = `1`, `totalRequired` = `false`, `prefer` = `"return=minimal"`
- **Query params (wire ← C#)**: `product_id` ← `productId`, `page_size` ← `pageSize`, `page` ← `page`, `total_required` ← `totalRequired`
- **Returns**: `PlanCollection`
- **Error**: `SdkException<ListBillingPlansError>` — **Case A (typed)**
- **Error accessors**: `TryGetSubscriptionError(out SubscriptionError)` [400, 401, 403, 404, 500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `PlanCollection` | `Models/PlanCollection.cs` |
| `ListBillingPlansError` | `Errors/ListBillingPlansError.cs` |
| `SubscriptionError` | `Models/SubscriptionError.cs` |

### ListSubscriptionTransactions

- **Signature**: `ListSubscriptionTransactions(string id, string startTime, string endTime, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Query params (wire ← C#)**: `start_time` ← `startTime`, `end_time` ← `endTime`
- **Returns**: `TransactionsList`
- **Error**: `SdkException<ListSubscriptionTransactionsError>` — **Case A (typed)**
- **Error accessors**: `TryGetSubscriptionError(out SubscriptionError)` [400, 401, 403, 404, 500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `TransactionsList` | `Models/TransactionsList.cs` |
| `ListSubscriptionTransactionsError` | `Errors/ListSubscriptionTransactionsError.cs` |
| `SubscriptionError` | `Models/SubscriptionError.cs` |

### ListSubscriptions

- **Signature**: `ListSubscriptions(string? planIds, string? statuses, string? createdAfter, string? createdBefore, string? statusUpdatedBefore, string? statusUpdatedAfter, string? filter, IReadOnlyList<string>? customerIds, int? pageSize = 10, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 8 params (`planIds` … `customerIds`) — nullable, no default → **must pass explicitly** (pass `null` to skip)
  - defaults: `pageSize` = `10`, `page` = `1`
- **Query params (wire ← C#)**: `plan_ids` ← `planIds`, `statuses` ← `statuses`, `created_after` ← `createdAfter`, `created_before` ← `createdBefore`, `status_updated_before` ← `statusUpdatedBefore`, `status_updated_after` ← `statusUpdatedAfter`, `filter` ← `filter`, `page_size` ← `pageSize`, `page` ← `page`, `customer_ids` ← `customerIds`
- **Returns**: `SubscriptionCollection`
- **Error**: `SdkException<ListSubscriptionsError>` — **Case A (typed)**
- **Error accessors**: `TryGetSubscriptionError(out SubscriptionError)` [400, 401, 403, 500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `SubscriptionCollection` | `Models/SubscriptionCollection.cs` |
| `ListSubscriptionsError` | `Errors/ListSubscriptionsError.cs` |
| `SubscriptionError` | `Models/SubscriptionError.cs` |

### PatchBillingPlan

- **Signature**: `PatchBillingPlan(string id, IReadOnlyList<Patch>? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `void` (Task)
- **Error**: `SdkException<PatchBillingPlanError>` — **Case A (typed)**
- **Error accessors**: `TryGetSubscriptionError(out SubscriptionError)` [400, 401, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `Patch` | `Models/Patch.cs` |
| `PatchBillingPlanError` | `Errors/PatchBillingPlanError.cs` |
| `SubscriptionError` | `Models/SubscriptionError.cs` |

### PatchSubscription

- **Signature**: `PatchSubscription(string id, IReadOnlyList<Patch>? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `void` (Task)
- **Error**: `SdkException<PatchSubscriptionError>` — **Case A (typed)**
- **Error accessors**: `TryGetSubscriptionError(out SubscriptionError)` [400, 401, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `Patch` | `Models/Patch.cs` |
| `PatchSubscriptionError` | `Errors/PatchSubscriptionError.cs` |
| `SubscriptionError` | `Models/SubscriptionError.cs` |

### ReviseSubscription

- **Signature**: `ReviseSubscription(string id, ModifySubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ModifySubscriptionResponse`
- **Error**: `SdkException<ReviseSubscriptionError>` — **Case A (typed)**
- **Error accessors**: `TryGetSubscriptionError(out SubscriptionError)` [400, 401, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ModifySubscriptionRequest` | `Models/ModifySubscriptionRequest.cs` |
| `ModifySubscriptionResponse` | `Models/ModifySubscriptionResponse.cs` |
| `ReviseSubscriptionError` | `Errors/ReviseSubscriptionError.cs` |
| `SubscriptionError` | `Models/SubscriptionError.cs` |

### SuspendSubscription

- **Signature**: `SuspendSubscription(string id, SuspendSubscription? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `void` (Task)
- **Error**: `SdkException<SuspendSubscriptionError>` — **Case A (typed)**
- **Error accessors**: `TryGetSubscriptionError(out SubscriptionError)` [400, 401, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `SuspendSubscription` | `Models/SuspendSubscription.cs` |
| `SuspendSubscriptionError` | `Errors/SuspendSubscriptionError.cs` |
| `SubscriptionError` | `Models/SubscriptionError.cs` |

### UpdateBillingPlanPricingSchemes

- **Signature**: `UpdateBillingPlanPricingSchemes(string id, UpdatePricingSchemesRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `void` (Task)
- **Error**: `SdkException<UpdateBillingPlanPricingSchemesError>` — **Case A (typed)**
- **Error accessors**: `TryGetSubscriptionError(out SubscriptionError)` [400, 401, 403, 404, 422, 500] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `UpdatePricingSchemesRequest` | `Models/UpdatePricingSchemesRequest.cs` |
| `UpdateBillingPlanPricingSchemesError` | `Errors/UpdateBillingPlanPricingSchemesError.cs` |
| `SubscriptionError` | `Models/SubscriptionError.cs` |

