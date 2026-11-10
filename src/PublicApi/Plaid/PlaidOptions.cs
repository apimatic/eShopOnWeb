namespace Microsoft.eShopWeb.PublicApi.Plaid;

/// <summary>Bound from the "Plaid:" configuration section.</summary>
public class PlaidOptions
{
    public const string SectionName = "Plaid";

    public string? ClientId { get; set; }
    public string? Secret { get; set; }
    public string? Environment { get; set; }
    /// <summary>Optional override; when set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }

    public bool HasCredentials => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(Secret);

    /// <summary>Resolves the API base address, or null when it cannot be determined.</summary>
    public string? ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
            return BaseUrl;

        return Environment?.Trim().ToLowerInvariant() switch
        {
            "sandbox" => "https://sandbox.plaid.com",
            "production" => "https://production.plaid.com",
            _ => null
        };
    }
}
