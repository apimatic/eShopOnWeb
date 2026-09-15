namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Settings for the Maxio Advanced Billing site used by this API.</summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; init; } = string.Empty;
    public string Subdomain { get; init; } = string.Empty;
    public string ProductFamilyHandle { get; init; } = string.Empty;

    /// <summary>Optional full Advanced Billing API base address override.</summary>
    public string? BaseUrl { get; init; }
}
