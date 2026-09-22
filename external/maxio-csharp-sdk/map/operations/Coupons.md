<!-- Generated file — do not edit; regenerated with the SDK. -->

# Coupons — operations

Accessor: `client.Coupons` · Source: `Api/Coupons.cs` · 14 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### ArchiveCoupon

- **Auth**: `options.BasicAuth` OR `options.BearerAuth`
- **Signature**: `ArchiveCoupon(int productFamilyId, int couponId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `CouponResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `CouponResponse` | `Models/CouponResponse.cs` |

### CreateCoupon

- **Auth**: `options.BasicAuth` OR `options.BearerAuth`
- **Signature**: `CreateCoupon(int productFamilyId, CouponRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `CouponResponse`
- **Error**: `SdkException<CreateCouponError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CouponRequest` | `Models/CouponRequest.cs` |
| `CouponResponse` | `Models/CouponResponse.cs` |
| `CreateCouponError` | `Errors/CreateCouponError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### CreateCouponSubcodes

- **Auth**: `options.BasicAuth` OR `options.BearerAuth`
- **Signature**: `CreateCouponSubcodes(int couponId, CouponSubcodes? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `CouponSubcodesResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `CouponSubcodes` | `Models/CouponSubcodes.cs` |
| `CouponSubcodesResponse` | `Models/CouponSubcodesResponse.cs` |

### CreateOrUpdateCouponCurrencyPrices

- **Auth**: `options.BasicAuth` OR `options.BearerAuth`
- **Signature**: `CreateOrUpdateCouponCurrencyPrices(int couponId, CouponCurrencyRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `CouponCurrencyResponse`
- **Error**: `SdkException<CreateOrUpdateCouponCurrencyPricesError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorStringMapResponse1(out ErrorStringMapResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CouponCurrencyRequest` | `Models/CouponCurrencyRequest.cs` |
| `CouponCurrencyResponse` | `Models/CouponCurrencyResponse.cs` |
| `CreateOrUpdateCouponCurrencyPricesError` | `Errors/CreateOrUpdateCouponCurrencyPricesError.cs` |
| `ErrorStringMapResponse1` | `Models/ErrorStringMapResponse1.cs` |

### DeleteCouponSubcode

- **Auth**: `options.BasicAuth` OR `options.BearerAuth`
- **Signature**: `DeleteCouponSubcode(int couponId, string subcode, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `void` (Task)
- **Error**: `SdkException<DeleteCouponSubcodeError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `DeleteCouponSubcodeError` | `Errors/DeleteCouponSubcodeError.cs` |

### FindCoupon

- **Auth**: `options.BasicAuth` OR `options.BearerAuth`
- **Signature**: `FindCoupon(int? productFamilyId, string? code, bool? currencyPrices, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `productFamilyId` — nullable, no default → **must pass explicitly**
  - `code` — nullable, no default → **must pass explicitly**
  - `currencyPrices` — nullable, no default → **must pass explicitly**
- **Query params (wire ← C#)**: `product_family_id` ← `productFamilyId`, `code` ← `code`, `currency_prices` ← `currencyPrices`
- **Returns**: `CouponResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `CouponResponse` | `Models/CouponResponse.cs` |

### ListCouponSubcodes

- **Auth**: `options.BasicAuth` OR `options.BearerAuth`
- **Signature**: `ListCouponSubcodes(int couponId, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - defaults: `page` = `1`, `perPage` = `20`
- **Query params (wire ← C#)**: `page` ← `page`, `per_page` ← `perPage`
- **Returns**: `CouponSubcodes`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `CouponSubcodes` | `Models/CouponSubcodes.cs` |

### ListCoupons

- **Auth**: `options.BasicAuth` OR `options.BearerAuth`
- **Signature**: `ListCoupons(ListCouponsFilter? filter, bool? currencyPrices, int? page = 1, int? perPage = 30, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `filter` — nullable, no default → **must pass explicitly**
  - `currencyPrices` — nullable, no default → **must pass explicitly**
  - defaults: `page` = `1`, `perPage` = `30`
- **Query params (wire ← C#)**: `page` ← `page`, `per_page` ← `perPage`, `filter` ← `filter`, `currency_prices` ← `currencyPrices`
- **Returns**: `IReadOnlyList<CouponResponse>`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `ListCouponsFilter` | `Models/ListCouponsFilter.cs` |
| `CouponResponse` | `Models/CouponResponse.cs` |

### ListCouponsForProductFamily

- **Auth**: `options.BasicAuth` OR `options.BearerAuth`
- **Signature**: `ListCouponsForProductFamily(int productFamilyId, ListCouponsFilter? filter, bool? currencyPrices, int? page = 1, int? perPage = 30, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `filter` — nullable, no default → **must pass explicitly**
  - `currencyPrices` — nullable, no default → **must pass explicitly**
  - defaults: `page` = `1`, `perPage` = `30`
- **Query params (wire ← C#)**: `page` ← `page`, `per_page` ← `perPage`, `filter` ← `filter`, `currency_prices` ← `currencyPrices`
- **Returns**: `IReadOnlyList<CouponResponse>`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `ListCouponsFilter` | `Models/ListCouponsFilter.cs` |
| `CouponResponse` | `Models/CouponResponse.cs` |

### ReadCoupon

- **Auth**: `options.BasicAuth` OR `options.BearerAuth`
- **Signature**: `ReadCoupon(int productFamilyId, int couponId, bool? currencyPrices, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `currencyPrices` — nullable, no default → **must pass explicitly**
- **Query params (wire ← C#)**: `currency_prices` ← `currencyPrices`
- **Returns**: `CouponResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `CouponResponse` | `Models/CouponResponse.cs` |

### ReadCouponUsage

- **Auth**: `options.BasicAuth` OR `options.BearerAuth`
- **Signature**: `ReadCouponUsage(int productFamilyId, int couponId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `IReadOnlyList<CouponUsage>`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `CouponUsage` | `Models/CouponUsage.cs` |

### UpdateCoupon

- **Auth**: `options.BasicAuth` OR `options.BearerAuth`
- **Signature**: `UpdateCoupon(int productFamilyId, int couponId, CouponRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `CouponResponse`
- **Error**: `SdkException<UpdateCouponError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CouponRequest` | `Models/CouponRequest.cs` |
| `CouponResponse` | `Models/CouponResponse.cs` |
| `UpdateCouponError` | `Errors/UpdateCouponError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### UpdateCouponSubcodes

- **Auth**: `options.BasicAuth` OR `options.BearerAuth`
- **Signature**: `UpdateCouponSubcodes(int couponId, CouponSubcodes? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `CouponSubcodesResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `CouponSubcodes` | `Models/CouponSubcodes.cs` |
| `CouponSubcodesResponse` | `Models/CouponSubcodesResponse.cs` |

### ValidateCoupon

- **Auth**: `options.BasicAuth` OR `options.BearerAuth`
- **Signature**: `ValidateCoupon(string code, int? productFamilyId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `productFamilyId` — nullable, no default → **must pass explicitly**
- **Query params (wire ← C#)**: `code` ← `code`, `product_family_id` ← `productFamilyId`
- **Returns**: `CouponResponse`
- **Error**: `SdkException<ValidateCouponError>` — **Case A (typed)**
- **Error accessors**: `TryGetSingleStringErrorResponse1(out SingleStringErrorResponse1)` [404] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CouponResponse` | `Models/CouponResponse.cs` |
| `ValidateCouponError` | `Errors/ValidateCouponError.cs` |
| `SingleStringErrorResponse1` | `Models/SingleStringErrorResponse1.cs` |

