using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; init; } = string.Empty;
    public string Subdomain { get; init; } = string.Empty;
    public string ProductFamilyHandle { get; init; } = string.Empty;
    public string? BaseUrl { get; init; }

    public static bool IsValid(MaxioOptions options) =>
        !string.IsNullOrWhiteSpace(options.ApiKey) &&
        !string.IsNullOrWhiteSpace(options.Subdomain) &&
        !string.IsNullOrWhiteSpace(options.ProductFamilyHandle) &&
        (string.IsNullOrWhiteSpace(options.BaseUrl) || Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _));
}
