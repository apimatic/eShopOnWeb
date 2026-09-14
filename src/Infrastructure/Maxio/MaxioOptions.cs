namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for talking to Maxio (Advanced) Billing. Bound from the
/// "Maxio" configuration section; secrets come from user-secrets / environment.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>The site API key (sent as the Basic-auth username, with "x" as the password).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain, e.g. "acme" for https://acme.chargify.com.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Optional verbatim API base address override; wins over <see cref="Subdomain"/> when set.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Handle of the product family that holds the subscribable plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return $"https://{Subdomain.TrimEnd('/')}.chargify.com";
    }
}
