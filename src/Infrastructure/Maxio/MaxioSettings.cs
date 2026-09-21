namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio Advanced Billing configuration, bound from the <c>Maxio:</c> configuration section.
/// Values are supplied by the deployment (environment variables loaded into .NET user-secrets); none are
/// hard-coded, so the same build runs against a different Maxio site and catalog.
/// </summary>
public class MaxioSettings
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Maxio";

    /// <summary>Maxio Chargify API key (the Basic-auth username; the password is the literal <c>x</c>). Secret.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain, substituted into the <c>https://{site}.chargify.com</c> base URL.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Default product family handle whose products are the subscribable plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional explicit API base URL. When set it is used verbatim, overriding the subdomain-derived URL.
    /// </summary>
    public string? BaseUrl { get; set; }
}
