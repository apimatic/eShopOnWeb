namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration, bound from the "Maxio" section.
/// Values are supplied via user-secrets / environment variables and must never be committed.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio Advanced Billing API key (Basic-auth username; the SDK sends password "x").
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Site subdomain; the API base address is derived as https://{subdomain}.chargify.com.
    /// Ignored when <see cref="BaseUrl"/> is set.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Optional verbatim override of the API base address (e.g. a proxy or a differently
    /// hosted site). When set, it is used as-is instead of deriving from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Handle of the product family that holds the subscribable plans.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;
}
