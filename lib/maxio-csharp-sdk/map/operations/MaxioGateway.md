<!-- Generated file — do not edit; regenerated with the SDK. -->

# MaxioGateway — operations

Accessor: `client.MaxioGateway` · Source: `Api/MaxioGateway.cs` · 1 operation

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### RequestAccessToken

- **Server group**: `Oauth`
- **Signature**: `RequestAccessToken(MaxioGatewayOAuthTokenRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `MaxioGatewayOAuthAccessToken`
- **Error**: `SdkException<RequestAccessTokenError>` — **Case A (typed)**
- **Error accessors**: `TryGetMaxioGatewayOAuthError(out MaxioGatewayOAuthError)` [400, 401] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `MaxioGatewayOAuthTokenRequest` | `Models/MaxioGatewayOAuthTokenRequest.cs` |
| `MaxioGatewayOAuthAccessToken` | `Models/MaxioGatewayOAuthAccessToken.cs` |
| `RequestAccessTokenError` | `Errors/RequestAccessTokenError.cs` |
| `MaxioGatewayOAuthError` | `Models/MaxioGatewayOAuthError.cs` |

