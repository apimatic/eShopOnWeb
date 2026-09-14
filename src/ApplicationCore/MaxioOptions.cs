namespace Microsoft.eShopWeb;

/// <summary>
/// Settings for talking to Maxio Advanced Billing. Bound from the "Maxio" configuration
/// section; values must come from environment variables / user-secrets, never checked-in config.
/// </summary>
public class MaxioOptions
{
    public const string CONFIG_NAME = "Maxio";

    public string ApiKey { get; set; } = string.Empty;

    public string Subdomain { get; set; } = string.Empty;

    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the Maxio API base address. When set, used verbatim instead of
    /// deriving "https://{Subdomain}.chargify.com/" from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
