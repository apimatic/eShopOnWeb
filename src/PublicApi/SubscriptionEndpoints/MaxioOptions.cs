namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Settings for the Maxio Advanced Billing site. Values are supplied through user-secrets
/// or environment-backed configuration, never application configuration files.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string? ApiKey { get; init; }
    public string? Subdomain { get; init; }
    public string? ProductFamilyHandle { get; init; }

    /// <summary>
    /// Optional API base-address override. When present it is used in preference to the
    /// standard site address derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; init; }
}
