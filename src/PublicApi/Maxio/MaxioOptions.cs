namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings that configure access to the Maxio Advanced Billing API.
/// Bound from the "Maxio" configuration section. No secret values are ever
/// stored in source control; load them via .NET user-secrets or environment
/// configuration (e.g. Maxio:ApiKey).
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>The Maxio/Chargify API key (basic-auth username).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The site subdomain (e.g. "acme"). Used to derive the API base URL.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the Maxio product family that contains the subscribable plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional full API base URL override. When set it is used verbatim instead of a URL
    /// derived from <see cref="Subdomain"/> (this is how you target non-US / custom hosts).
    /// </summary>
    public string? BaseUrl { get; set; }
}
