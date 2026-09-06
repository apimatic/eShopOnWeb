using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Maxio Advanced Billing settings. Values are supplied through user-secrets or deployment configuration.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; init; } = string.Empty;
    public string Subdomain { get; init; } = string.Empty;
    public string ProductFamilyHandle { get; init; } = string.Empty;
    public string? BaseUrl { get; init; }

    public static Uri GetBaseUri(MaxioOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return EnsureAbsoluteHttpsUri(options.BaseUrl, nameof(BaseUrl));
        }

        if (string.IsNullOrWhiteSpace(options.Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain must be configured when Maxio:BaseUrl is not set.");
        }

        return EnsureAbsoluteHttpsUri($"https://{options.Subdomain}.chargify.com", nameof(Subdomain));
    }

    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException("Maxio:ApiKey must be configured.");
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
            throw new InvalidOperationException("Maxio:ProductFamilyHandle must be configured.");

        _ = GetBaseUri(this);
    }

    private static Uri EnsureAbsoluteHttpsUri(string value, string settingName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException($"Maxio:{settingName} must be an absolute HTTPS URL.");

        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
    }
}
