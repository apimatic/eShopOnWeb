<!-- Generated file — do not edit; regenerated with the SDK. -->

# ReferralCodes — operations

Accessor: `client.ReferralCodes` · Source: `Api/ReferralCodes.cs` · 1 operation

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### ValidateReferralCode

- **Signature**: `ValidateReferralCode(string code, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Query params (wire ← C#)**: `code` ← `code`
- **Returns**: `ReferralValidationResponse`
- **Error**: `SdkException<ValidateReferralCodeError>` — **Case A (typed)**
- **Error accessors**: `TryGetSingleStringErrorResponse1(out SingleStringErrorResponse1)` [404] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ReferralValidationResponse` | `Models/ReferralValidationResponse.cs` |
| `ValidateReferralCodeError` | `Errors/ValidateReferralCodeError.cs` |
| `SingleStringErrorResponse1` | `Models/SingleStringErrorResponse1.cs` |

