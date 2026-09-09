namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration. Bound from the
/// "Maxio" configuration section. Values are provided via user-secrets or
/// environment variables and must never be hard-coded.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// The Maxio site API key (used as the Basic auth username, password is "X").
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio site subdomain (e.g. "acme" for https://acme.chargify.com).
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the product family that contains the subscription plans.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override. When set, it is used verbatim as the API base address
    /// instead of deriving one from the subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Returns the API base address. <see cref="BaseUrl"/> wins when set;
    /// otherwise the address is derived from <see cref="Subdomain"/>.
    /// </summary>
    public string GetBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/') + "/";
        }

        return $"https://{Subdomain}.chargify.com/";
    }

    public bool IsConfigured()
        => !string.IsNullOrWhiteSpace(ApiKey)
           && (!string.IsNullOrWhiteSpace(Subdomain) || !string.IsNullOrWhiteSpace(BaseUrl))
           && !string.IsNullOrWhiteSpace(ProductFamilyHandle);
}
