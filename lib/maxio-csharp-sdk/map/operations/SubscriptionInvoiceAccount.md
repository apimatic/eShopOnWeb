<!-- Generated file — do not edit; regenerated with the SDK. -->

# SubscriptionInvoiceAccount — operations

Accessor: `client.SubscriptionInvoiceAccount` · Source: `Api/SubscriptionInvoiceAccount.cs` · 7 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### CreatePrepayment

- **Signature**: `CreatePrepayment(int subscriptionId, CreatePrepaymentRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `CreatePrepaymentResponse`
- **Error**: `SdkException<CreatePrepaymentApiError>` — **Case A (typed)**
- **Error accessors**: `TryGetCreatePrepaymentErrorResponse(out CreatePrepaymentErrorResponse)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CreatePrepaymentRequest` | `Models/CreatePrepaymentRequest.cs` |
| `CreatePrepaymentResponse` | `Models/CreatePrepaymentResponse.cs` |
| `CreatePrepaymentApiError` | `Errors/CreatePrepaymentApiError.cs` |
| `CreatePrepaymentErrorResponse` | `Models/AnyOf/CreatePrepaymentErrorResponse.cs` |

### DeductServiceCredit

- **Signature**: `DeductServiceCredit(int subscriptionId, DeductServiceCreditRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `void` (Task)
- **Error**: `SdkException<DeductServiceCreditApiError>` — **Case A (typed)**
- **Error accessors**: `TryGetDeductServiceCreditErrorResponse(out DeductServiceCreditErrorResponse)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `DeductServiceCreditRequest` | `Models/DeductServiceCreditRequest.cs` |
| `DeductServiceCreditApiError` | `Errors/DeductServiceCreditApiError.cs` |
| `DeductServiceCreditErrorResponse` | `Models/AnyOf/DeductServiceCreditErrorResponse.cs` |

### IssueServiceCredit

- **Signature**: `IssueServiceCredit(int subscriptionId, IssueServiceCreditRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ServiceCredit`
- **Error**: `SdkException<IssueServiceCreditApiError>` — **Case A (typed)**
- **Error accessors**: `TryGetIssueServiceCreditErrorResponse(out IssueServiceCreditErrorResponse)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `IssueServiceCreditRequest` | `Models/IssueServiceCreditRequest.cs` |
| `ServiceCredit` | `Models/ServiceCredit.cs` |
| `IssueServiceCreditApiError` | `Errors/IssueServiceCreditApiError.cs` |
| `IssueServiceCreditErrorResponse` | `Models/AnyOf/IssueServiceCreditErrorResponse.cs` |

### ListPrepayments

- **Signature**: `ListPrepayments(int subscriptionId, ListPrepaymentsFilter? filter, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `filter` — nullable, no default → **must pass explicitly**
  - defaults: `page` = `1`, `perPage` = `20`
- **Query params (wire ← C#)**: `page` ← `page`, `per_page` ← `perPage`, `filter` ← `filter`
- **Returns**: `PrepaymentsResponse`
- **Error**: `SdkException<ListPrepaymentsError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ListPrepaymentsFilter` | `Models/ListPrepaymentsFilter.cs` |
| `PrepaymentsResponse` | `Models/PrepaymentsResponse.cs` |
| `ListPrepaymentsError` | `Errors/ListPrepaymentsError.cs` |

### ListServiceCredits

- **Signature**: `ListServiceCredits(int subscriptionId, SortingDirection? direction, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `direction` — nullable, no default → **must pass explicitly**
  - defaults: `page` = `1`, `perPage` = `20`
- **Query params (wire ← C#)**: `page` ← `page`, `per_page` ← `perPage`, `direction` ← `direction`
- **Returns**: `ListServiceCreditsResponse`
- **Error**: `SdkException<ListServiceCreditsError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `SortingDirection` | `Models/Enums/SortingDirection.cs` |
| `ListServiceCreditsResponse` | `Models/ListServiceCreditsResponse.cs` |
| `ListServiceCreditsError` | `Errors/ListServiceCreditsError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### ReadAccountBalances

- **Signature**: `ReadAccountBalances(int subscriptionId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `AccountBalances`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `AccountBalances` | `Models/AccountBalances.cs` |

### RefundPrepayment

- **Signature**: `RefundPrepayment(int subscriptionId, long prepaymentId, RefundPrepaymentRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `PrepaymentResponse`
- **Error**: `SdkException<RefundPrepaymentApiError>` — **Case A (typed)**
- **Error accessors**: `TryGetRefundPrepaymentBaseErrorsResponse1(out RefundPrepaymentBaseErrorsResponse1)` [400] · `TryGetString(out string)` [404] · `TryGetRefundPrepaymentErrorResponse(out RefundPrepaymentErrorResponse)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `RefundPrepaymentRequest` | `Models/RefundPrepaymentRequest.cs` |
| `PrepaymentResponse` | `Models/PrepaymentResponse.cs` |
| `RefundPrepaymentApiError` | `Errors/RefundPrepaymentApiError.cs` |
| `RefundPrepaymentBaseErrorsResponse1` | `Models/RefundPrepaymentBaseErrorsResponse1.cs` |
| `RefundPrepaymentErrorResponse` | `Models/AnyOf/RefundPrepaymentErrorResponse.cs` |

