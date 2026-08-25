namespace Microsoft.eShopWeb;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// Values are supplied via user-secrets / environment variables; never hard-coded.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>Maxio API key (used as the Basic auth username; password is literally "x" per the OpenAPI spec).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Site subdomain, templated into the spec server URL https://{site}.chargify.com.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family that contains the subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Optional override for the API base address. When set, used verbatim instead of deriving from <see cref="Subdomain"/>.</summary>
    public string? BaseUrl { get; set; }

    public string GetBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new System.InvalidOperationException(
                "Maxio is not configured: set either Maxio:BaseUrl or Maxio:Subdomain (from the MAXIO_SITE_SUBDOMAIN environment variable via user-secrets).");
        }

        return $"https://{Subdomain}.chargify.com";
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new System.InvalidOperationException(
                "Maxio is not configured: Maxio:ApiKey is missing (load the MAXIO_API_KEY environment variable into user-secrets).");
        }

        // Triggers the subdomain check when BaseUrl is not set.
        _ = GetBaseUrl();
    }
}
