<!-- Generated file — do not edit; regenerated with the SDK. -->

# SubscriptionGroupInvoiceAccount — operations

Accessor: `client.SubscriptionGroupInvoiceAccount` · Source: `Api/SubscriptionGroupInvoiceAccount.cs` · 4 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### CreateSubscriptionGroupPrepayment

- **Signature**: `CreateSubscriptionGroupPrepayment(string uid, SubscriptionGroupPrepaymentRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `SubscriptionGroupPrepaymentResponse`
- **Error**: `SdkException<CreateSubscriptionGroupPrepaymentError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `SubscriptionGroupPrepaymentRequest` | `Models/SubscriptionGroupPrepaymentRequest.cs` |
| `SubscriptionGroupPrepaymentResponse` | `Models/SubscriptionGroupPrepaymentResponse.cs` |
| `CreateSubscriptionGroupPrepaymentError` | `Errors/CreateSubscriptionGroupPrepaymentError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### DeductSubscriptionGroupServiceCredit

- **Signature**: `DeductSubscriptionGroupServiceCredit(string uid, DeductServiceCreditRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ServiceCredit`
- **Error**: `SdkException<DeductSubscriptionGroupServiceCreditError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `DeductServiceCreditRequest` | `Models/DeductServiceCreditRequest.cs` |
| `ServiceCredit` | `Models/ServiceCredit.cs` |
| `DeductSubscriptionGroupServiceCreditError` | `Errors/DeductSubscriptionGroupServiceCreditError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### IssueSubscriptionGroupServiceCredit

- **Signature**: `IssueSubscriptionGroupServiceCredit(string uid, IssueServiceCreditRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ServiceCreditResponse`
- **Error**: `SdkException<IssueSubscriptionGroupServiceCreditError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `IssueServiceCreditRequest` | `Models/IssueServiceCreditRequest.cs` |
| `ServiceCreditResponse` | `Models/ServiceCreditResponse.cs` |
| `IssueSubscriptionGroupServiceCreditError` | `Errors/IssueSubscriptionGroupServiceCreditError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### ListPrepaymentsForSubscriptionGroup

- **Signature**: `ListPrepaymentsForSubscriptionGroup(string uid, ListPrepaymentsFilter? filter, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `filter` — nullable, no default → **must pass explicitly**
  - defaults: `page` = `1`, `perPage` = `20`
- **Query params (wire ← C#)**: `page` ← `page`, `per_page` ← `perPage`, `filter` ← `filter`
- **Returns**: `ListSubscriptionGroupPrepaymentResponse`
- **Error**: `SdkException<ListPrepaymentsForSubscriptionGroupError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ListPrepaymentsFilter` | `Models/ListPrepaymentsFilter.cs` |
| `ListSubscriptionGroupPrepaymentResponse` | `Models/ListSubscriptionGroupPrepaymentResponse.cs` |
| `ListPrepaymentsForSubscriptionGroupError` | `Errors/ListPrepaymentsForSubscriptionGroupError.cs` |

