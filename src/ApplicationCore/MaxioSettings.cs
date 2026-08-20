namespace Microsoft.eShopWeb;

/// <summary>
/// Maxio Advanced Billing connection settings. Bound from the <c>Maxio</c> configuration
/// section. Values must come from environment / user-secrets — never from source.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>API key used as the HTTP Basic username (password is <c>X</c>).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Chargify/Maxio site subdomain used to derive the API host.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Product family API handle whose products are offered as subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional verbatim API base address. When set, used instead of
    /// <c>https://{Subdomain}.chargify.com</c>.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            return string.Empty;
        }

        return $"https://{Subdomain.Trim()}.chargify.com";
    }

    public bool IsConfigured() =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ProductFamilyHandle)
        && !string.IsNullOrWhiteSpace(ResolveBaseUrl());
}
