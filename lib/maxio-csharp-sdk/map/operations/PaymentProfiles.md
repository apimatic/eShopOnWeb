<!-- Generated file — do not edit; regenerated with the SDK. -->

# PaymentProfiles — operations

Accessor: `client.PaymentProfiles` · Source: `Api/PaymentProfiles.cs` · 12 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### ChangeSubscriptionDefaultPaymentProfile

- **Signature**: `ChangeSubscriptionDefaultPaymentProfile(int subscriptionId, int paymentProfileId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `PaymentProfileResponse`
- **Error**: `SdkException<ChangeSubscriptionDefaultPaymentProfileError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `PaymentProfileResponse` | `Models/PaymentProfileResponse.cs` |
| `ChangeSubscriptionDefaultPaymentProfileError` | `Errors/ChangeSubscriptionDefaultPaymentProfileError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### ChangeSubscriptionGroupDefaultPaymentProfile

- **Signature**: `ChangeSubscriptionGroupDefaultPaymentProfile(string uid, int paymentProfileId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `PaymentProfileResponse`
- **Error**: `SdkException<ChangeSubscriptionGroupDefaultPaymentProfileError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `PaymentProfileResponse` | `Models/PaymentProfileResponse.cs` |
| `ChangeSubscriptionGroupDefaultPaymentProfileError` | `Errors/ChangeSubscriptionGroupDefaultPaymentProfileError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### CreatePaymentProfile

- **Signature**: `CreatePaymentProfile(CreatePaymentProfileRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `PaymentProfileResponse`
- **Error**: `SdkException<CreatePaymentProfileError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CreatePaymentProfileRequest` | `Models/CreatePaymentProfileRequest.cs` |
| `PaymentProfileResponse` | `Models/PaymentProfileResponse.cs` |
| `CreatePaymentProfileError` | `Errors/CreatePaymentProfileError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### DeleteSubscriptionGroupPaymentProfile

- **Signature**: `DeleteSubscriptionGroupPaymentProfile(string uid, int paymentProfileId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `void` (Task)
- **Error**: `SdkException<RawError>` — **Case B**

### DeleteSubscriptionsPaymentProfile

- **Signature**: `DeleteSubscriptionsPaymentProfile(int subscriptionId, int paymentProfileId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `void` (Task)
- **Error**: `SdkException<RawError>` — **Case B**

### DeleteUnusedPaymentProfile

- **Signature**: `DeleteUnusedPaymentProfile(int paymentProfileId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `void` (Task)
- **Error**: `SdkException<DeleteUnusedPaymentProfileError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `DeleteUnusedPaymentProfileError` | `Errors/DeleteUnusedPaymentProfileError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### ListPaymentProfiles

- **Signature**: `ListPaymentProfiles(int? customerId, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `customerId` — nullable, no default → **must pass explicitly**
  - defaults: `page` = `1`, `perPage` = `20`
- **Query params (wire ← C#)**: `page` ← `page`, `per_page` ← `perPage`, `customer_id` ← `customerId`
- **Returns**: `IReadOnlyList<PaymentProfileResponse>`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `PaymentProfileResponse` | `Models/PaymentProfileResponse.cs` |

### ReadOneTimeToken

- **Signature**: `ReadOneTimeToken(string chargifyToken, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `GetOneTimeTokenRequest`
- **Error**: `SdkException<ReadOneTimeTokenError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [404] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `GetOneTimeTokenRequest` | `Models/GetOneTimeTokenRequest.cs` |
| `ReadOneTimeTokenError` | `Errors/ReadOneTimeTokenError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### ReadPaymentProfile

- **Signature**: `ReadPaymentProfile(int paymentProfileId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `PaymentProfileResponse`
- **Error**: `SdkException<ReadPaymentProfileError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `PaymentProfileResponse` | `Models/PaymentProfileResponse.cs` |
| `ReadPaymentProfileError` | `Errors/ReadPaymentProfileError.cs` |

### SendRequestUpdatePaymentEmail

- **Signature**: `SendRequestUpdatePaymentEmail(int subscriptionId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `void` (Task)
- **Error**: `SdkException<SendRequestUpdatePaymentEmailError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `SendRequestUpdatePaymentEmailError` | `Errors/SendRequestUpdatePaymentEmailError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### UpdatePaymentProfile

- **Signature**: `UpdatePaymentProfile(int paymentProfileId, UpdatePaymentProfileRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `PaymentProfileResponse`
- **Error**: `SdkException<UpdatePaymentProfileError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetErrorStringMapResponse1(out ErrorStringMapResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `UpdatePaymentProfileRequest` | `Models/UpdatePaymentProfileRequest.cs` |
| `PaymentProfileResponse` | `Models/PaymentProfileResponse.cs` |
| `UpdatePaymentProfileError` | `Errors/UpdatePaymentProfileError.cs` |
| `ErrorStringMapResponse1` | `Models/ErrorStringMapResponse1.cs` |

### VerifyBankAccount

- **Signature**: `VerifyBankAccount(int bankAccountId, BankAccountVerificationRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `BankAccountResponse`
- **Error**: `SdkException<VerifyBankAccountError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `BankAccountVerificationRequest` | `Models/BankAccountVerificationRequest.cs` |
| `BankAccountResponse` | `Models/BankAccountResponse.cs` |
| `VerifyBankAccountError` | `Errors/VerifyBankAccountError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

