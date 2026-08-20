namespace Microsoft.eShopWeb;

/// <summary>
/// Bound from the <c>Maxio</c> configuration section. Values are supplied via
/// environment variables / user-secrets, never hard-coded.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional absolute API base address. When set, used verbatim instead of
    /// deriving <c>https://{Subdomain}.chargify.com/</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && (!string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain))
        && !string.IsNullOrWhiteSpace(ProductFamilyHandle);

    /// <summary>
    /// Resolves the Advanced Billing API origin. Confirmed base URL form is
    /// <c>https://{subdomain}.chargify.com</c> (see Maxio "Core Resources for Building an Integration").
    /// </summary>
    public string ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl;
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException("Maxio:BaseUrl or Maxio:Subdomain must be configured.");
        }

        return $"https://{Subdomain}.chargify.com/";
    }
}
