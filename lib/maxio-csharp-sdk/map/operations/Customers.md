<!-- Generated file — do not edit; regenerated with the SDK. -->

# Customers — operations

Accessor: `client.Customers` · Source: `Api/Customers.cs` · 7 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### CreateCustomer

- **Signature**: `CreateCustomer(CreateCustomerRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `CustomerResponse`
- **Error**: `SdkException<CreateCustomerError>` — **Case A (typed)**
- **Error accessors**: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CreateCustomerRequest` | `Models/CreateCustomerRequest.cs` |
| `CustomerResponse` | `Models/CustomerResponse.cs` |
| `CreateCustomerError` | `Errors/CreateCustomerError.cs` |
| `CustomerErrorResponse1` | `Models/CustomerErrorResponse1.cs` |

### DeleteCustomer

- **Signature**: `DeleteCustomer(int id, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `void` (Task)
- **Error**: `SdkException<RawError>` — **Case B**

### ListCustomerSubscriptions

- **Signature**: `ListCustomerSubscriptions(int customerId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `IReadOnlyList<SubscriptionResponse>`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `SubscriptionResponse` | `Models/SubscriptionResponse.cs` |

### ListCustomers

- **Signature**: `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 7 params (`direction` … `q`) — nullable, no default → **must pass explicitly** (pass `null` to skip)
  - defaults: `page` = `1`, `perPage` = `50`
- **Query params (wire ← C#)**: `direction` ← `direction`, `page` ← `page`, `per_page` ← `perPage`, `date_field` ← `dateField`, `start_date` ← `startDate`, `end_date` ← `endDate`, `start_datetime` ← `startDatetime`, `end_datetime` ← `endDatetime`, `q` ← `q`
- **Returns**: `IReadOnlyList<CustomerResponse>`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `SortingDirection` | `Models/Enums/SortingDirection.cs` |
| `BasicDateField` | `Models/Enums/BasicDateField.cs` |
| `CustomerResponse` | `Models/CustomerResponse.cs` |

### ReadCustomer

- **Signature**: `ReadCustomer(int id, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `CustomerResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `CustomerResponse` | `Models/CustomerResponse.cs` |

### ReadCustomerByReference

- **Signature**: `ReadCustomerByReference(string reference, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Query params (wire ← C#)**: `reference` ← `reference`
- **Returns**: `CustomerResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `CustomerResponse` | `Models/CustomerResponse.cs` |

### UpdateCustomer

- **Signature**: `UpdateCustomer(int id, UpdateCustomerRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `CustomerResponse`
- **Error**: `SdkException<UpdateCustomerError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `UpdateCustomerRequest` | `Models/UpdateCustomerRequest.cs` |
| `CustomerResponse` | `Models/CustomerResponse.cs` |
| `UpdateCustomerError` | `Errors/UpdateCustomerError.cs` |
| `CustomerErrorResponse1` | `Models/CustomerErrorResponse1.cs` |

