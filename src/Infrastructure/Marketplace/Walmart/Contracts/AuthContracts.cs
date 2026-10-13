using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Marketplace.Walmart.Contracts;

// Schemas from api-specs/walmart/authorization.json. Only the fields this integration uses are
// modelled; the client_credentials grant returns no refresh token.

/// <summary>authorization.json #/components/schemas/TokenResponse</summary>
internal sealed class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

/// <summary>authorization.json #/components/schemas/OAuthError</summary>
internal sealed class OAuthError
{
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }
}
