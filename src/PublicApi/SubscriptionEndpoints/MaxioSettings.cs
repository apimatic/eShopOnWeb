namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Configuration for the Maxio Advanced Billing server described by maxio-spec/openapi.yaml.</summary>
public sealed class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; init; } = string.Empty;
    public string Subdomain { get; init; } = string.Empty;
    public string ProductFamilyHandle { get; init; } = string.Empty;
    public string? BaseUrl { get; init; }
}
