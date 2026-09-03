<!-- Generated file — do not edit; regenerated with the SDK. -->

# Subscriptions — operations

Accessor: `client.Subscriptions` · Source: `Api/Subscriptions.cs` · 12 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### ActivateSubscription

- **Signature**: `ActivateSubscription(int subscriptionId, ActivateSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `SubscriptionResponse`
- **Error**: `SdkException<ActivateSubscriptionError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorArrayMapResponse1(out ErrorArrayMapResponse1)` [400] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ActivateSubscriptionRequest` | `Models/ActivateSubscriptionRequest.cs` |
| `SubscriptionResponse` | `Models/SubscriptionResponse.cs` |
| `ActivateSubscriptionError` | `Errors/ActivateSubscriptionError.cs` |
| `ErrorArrayMapResponse1` | `Models/ErrorArrayMapResponse1.cs` |

### ApplyCouponsToSubscription

- **Signature**: `ApplyCouponsToSubscription(int subscriptionId, string? code, AddCouponsRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `code` — nullable, no default → **must pass explicitly**
  - `body` — nullable, no default → **must pass explicitly**
- **Query params (wire ← C#)**: `code` ← `code`
- **Returns**: `SubscriptionResponse`
- **Error**: `SdkException<ApplyCouponsToSubscriptionError>` — **Case A (typed)**
- **Error accessors**: `TryGetSubscriptionAddCouponError1(out SubscriptionAddCouponError1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `AddCouponsRequest` | `Models/AddCouponsRequest.cs` |
| `SubscriptionResponse` | `Models/SubscriptionResponse.cs` |
| `ApplyCouponsToSubscriptionError` | `Errors/ApplyCouponsToSubscriptionError.cs` |
| `SubscriptionAddCouponError1` | `Models/SubscriptionAddCouponError1.cs` |

### CreateSubscription

- **Signature**: `CreateSubscription(CreateSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `SubscriptionResponse`
- **Error**: `SdkException<CreateSubscriptionError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CreateSubscriptionRequest` | `Models/CreateSubscriptionRequest.cs` |
| `SubscriptionResponse` | `Models/SubscriptionResponse.cs` |
| `CreateSubscriptionError` | `Errors/CreateSubscriptionError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### FindSubscription

- **Signature**: `FindSubscription(string? reference, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `reference` — nullable, no default → **must pass explicitly**
- **Query params (wire ← C#)**: `reference` ← `reference`
- **Returns**: `SubscriptionResponse`
- **Error**: `SdkException<FindSubscriptionError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `SubscriptionResponse` | `Models/SubscriptionResponse.cs` |
| `FindSubscriptionError` | `Errors/FindSubscriptionError.cs` |

### ListSubscriptions

- **Signature**: `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, int? brandingThemeId, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 15 params (`state` … `include`) — nullable, no default → **must pass explicitly** (pass `null` to skip)
  - defaults: `page` = `1`, `perPage` = `20`
- **Query params (wire ← C#)**: `page` ← `page`, `per_page` ← `perPage`, `state` ← `state`, `product` ← `product`, `product_price_point_id` ← `productPricePointId`, `coupon` ← `coupon`, `coupon_code` ← `couponCode`, `branding_theme_id` ← `brandingThemeId`, `date_field` ← `dateField`, `start_date` ← `startDate`, `end_date` ← `endDate`, `start_datetime` ← `startDatetime`, `end_datetime` ← `endDatetime`, `metadata` ← `metadata`, `direction` ← `direction`, `sort` ← `sort`, `include` ← `include`
- **Returns**: `IReadOnlyList<SubscriptionResponse>`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `SubscriptionStateFilter` | `Models/Enums/SubscriptionStateFilter.cs` |
| `SubscriptionDateField` | `Models/Enums/SubscriptionDateField.cs` |
| `SortingDirection` | `Models/Enums/SortingDirection.cs` |
| `SubscriptionSort` | `Models/Enums/SubscriptionSort.cs` |
| `SubscriptionListInclude` | `Models/Enums/SubscriptionListInclude.cs` |
| `SubscriptionResponse` | `Models/SubscriptionResponse.cs` |

### OverrideSubscription

- **Signature**: `OverrideSubscription(int subscriptionId, OverrideSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `void` (Task)
- **Error**: `SdkException<OverrideSubscriptionError>` — **Case A (typed)**
- **Error accessors**: `TryGetSingleErrorResponse1(out SingleErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `OverrideSubscriptionRequest` | `Models/OverrideSubscriptionRequest.cs` |
| `OverrideSubscriptionError` | `Errors/OverrideSubscriptionError.cs` |
| `SingleErrorResponse1` | `Models/SingleErrorResponse1.cs` |

### PreviewSubscription

- **Signature**: `PreviewSubscription(CreateSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `SubscriptionPreviewResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `CreateSubscriptionRequest` | `Models/CreateSubscriptionRequest.cs` |
| `SubscriptionPreviewResponse` | `Models/SubscriptionPreviewResponse.cs` |

### PurgeSubscription

- **Signature**: `PurgeSubscription(int subscriptionId, int ack, IReadOnlyList<SubscriptionPurgeType>? cascade, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `cascade` — nullable, no default → **must pass explicitly**
- **Query params (wire ← C#)**: `ack` ← `ack`, `cascade` ← `cascade`
- **Returns**: `SubscriptionResponse`
- **Error**: `SdkException<PurgeSubscriptionError>` — **Case A (typed)**
- **Error accessors**: `TryGetSubscriptionResponse(out SubscriptionResponse)` [400] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `SubscriptionPurgeType` | `Models/Enums/SubscriptionPurgeType.cs` |
| `SubscriptionResponse` | `Models/SubscriptionResponse.cs` |
| `PurgeSubscriptionError` | `Errors/PurgeSubscriptionError.cs` |

### ReadSubscription

- **Signature**: `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `include` — nullable, no default → **must pass explicitly**
- **Query params (wire ← C#)**: `include` ← `include`
- **Returns**: `SubscriptionResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `SubscriptionInclude` | `Models/Enums/SubscriptionInclude.cs` |
| `SubscriptionResponse` | `Models/SubscriptionResponse.cs` |

### RemoveCouponFromSubscription

- **Signature**: `RemoveCouponFromSubscription(int subscriptionId, string? couponCode, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `couponCode` — nullable, no default → **must pass explicitly**
- **Query params (wire ← C#)**: `coupon_code` ← `couponCode`
- **Returns**: `string`
- **Error**: `SdkException<RemoveCouponFromSubscriptionError>` — **Case A (typed)**
- **Error accessors**: `TryGetSubscriptionRemoveCouponErrors1(out SubscriptionRemoveCouponErrors1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `RemoveCouponFromSubscriptionError` | `Errors/RemoveCouponFromSubscriptionError.cs` |
| `SubscriptionRemoveCouponErrors1` | `Models/SubscriptionRemoveCouponErrors1.cs` |

### UpdatePrepaidSubscriptionConfiguration

- **Signature**: `UpdatePrepaidSubscriptionConfiguration(int subscriptionId, UpsertPrepaidConfigurationRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `PrepaidConfigurationResponse`
- **Error**: `SdkException<UpdatePrepaidSubscriptionConfigurationError>` — **Case A (typed)**
- **Error accessors**: `TryGetPrepaidConfigurationErrorResponse(out PrepaidConfigurationErrorResponse)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `UpsertPrepaidConfigurationRequest` | `Models/UpsertPrepaidConfigurationRequest.cs` |
| `PrepaidConfigurationResponse` | `Models/PrepaidConfigurationResponse.cs` |
| `UpdatePrepaidSubscriptionConfigurationError` | `Errors/UpdatePrepaidSubscriptionConfigurationError.cs` |
| `PrepaidConfigurationErrorResponse` | `Models/AnyOf/PrepaidConfigurationErrorResponse.cs` |

### UpdateSubscription

- **Signature**: `UpdateSubscription(int subscriptionId, UpdateSubscriptionRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `SubscriptionResponse`
- **Error**: `SdkException<UpdateSubscriptionError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `UpdateSubscriptionRequest` | `Models/UpdateSubscriptionRequest.cs` |
| `SubscriptionResponse` | `Models/SubscriptionResponse.cs` |
| `UpdateSubscriptionError` | `Errors/UpdateSubscriptionError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

