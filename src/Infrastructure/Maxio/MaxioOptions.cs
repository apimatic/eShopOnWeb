namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Bound from the "Maxio" configuration section. Values come from user-secrets/environment
/// variables in every environment - none are hard-coded, since the same build must be able to
/// run against a different Maxio site and a different catalog than the one used in development.
/// </summary>
public class MaxioOptions
{
    public const string ConfigSection = "Maxio";

    /// <summary>API key used as the HTTP Basic Auth username (password is the literal "x").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain, e.g. "your-site" for your-site.chargify.com.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that contains the subscribable plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, used verbatim instead of deriving
    /// one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
