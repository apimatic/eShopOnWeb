namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed configuration for the Maxio Advanced Billing integration, bound from the
/// <c>Maxio</c> configuration section. Only these four keys are read; the environment (US) is
/// fixed in code and is not configuration-driven.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio/Chargify API key. Used as the HTTP Basic username (password is the literal "x").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Site subdomain, substituted into <c>https://{site}.chargify.com</c>.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are offered as subscription plans (e.g. <c>eshop-subscribe</c>).</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Optional base-URL override (e.g. a mock/dev host). When set, replaces the templated production URL verbatim.</summary>
    public string? BaseUrl { get; set; }
}
