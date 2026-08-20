using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public string GetApiBaseUrl() => string.IsNullOrWhiteSpace(BaseUrl)
        ? $"https://{Subdomain}.chargify.com"
        : BaseUrl;

    public static bool HasValidBaseUrl(MaxioOptions options) =>
        Uri.TryCreate(options.GetApiBaseUrl(), UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
