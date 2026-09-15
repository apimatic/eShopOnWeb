namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Settings for the Maxio Advanced Billing API.</summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; init; } = string.Empty;
    public string Subdomain { get; init; } = string.Empty;
    public string ProductFamilyHandle { get; init; } = string.Empty;
    public string? BaseUrl { get; init; }
}
