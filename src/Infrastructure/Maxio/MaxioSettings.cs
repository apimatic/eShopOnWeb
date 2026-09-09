namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio Advanced Billing configuration, bound from the <c>Maxio:</c> section.
/// Values are supplied via .NET user-secrets / environment configuration and never committed to
/// the repository. The credential itself (<see cref="ApiKey"/>) is validated at startup.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio (Chargify) API key. Bound from <c>Maxio:ApiKey</c>. Required.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The Maxio site subdomain. Bound from <c>Maxio:Subdomain</c>. Required.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are the subscription plans. Bound from <c>Maxio:ProductFamilyHandle</c>. Required.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional verbatim base-URL override. Bound from <c>Maxio:BaseUrl</c>. When set it is used
    /// as-is as the API base address instead of deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Whole-call timeout budget (seconds) enforced per operation. Bound from <c>Maxio:RequestTimeoutSeconds</c>; defaults to 30.</summary>
    public int RequestTimeoutSeconds { get; set; } = 30;
}
