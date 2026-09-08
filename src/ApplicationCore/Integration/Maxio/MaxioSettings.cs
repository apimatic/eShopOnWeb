using System;

namespace Microsoft.eShopWeb.ApplicationCore.Integration.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration.
/// Bound from the "Maxio" configuration section (user-secrets / environment variables).
/// </summary>
public class MaxioSettings
{
    public const string SECTION_NAME = "Maxio";

    /// <summary>Maxio site API key. Sent as the Basic auth username (password is the literal "X").</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain; used to derive the API base address when <see cref="BaseUrl"/> is not set.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose plans are offered for subscription.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override. When set it is used verbatim as the Maxio API base address
    /// instead of deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string GetRequiredApiKey()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException($"'{SECTION_NAME}:{nameof(ApiKey)}' is not configured.");
        }

        return ApiKey;
    }

    /// <summary>
    /// Resolves the Maxio API base address: <see cref="BaseUrl"/> verbatim when provided,
    /// otherwise derived from the site subdomain.
    /// </summary>
    public Uri GetApiBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var uri = new Uri(BaseUrl.Trim() + (BaseUrl.TrimEnd().EndsWith("/") ? string.Empty : "/"), UriKind.Absolute);
            return uri;
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                $"'{SECTION_NAME}:{nameof(Subdomain)}' (or an explicit '{SECTION_NAME}:{nameof(BaseUrl)}') is not configured.");
        }

        return new Uri($"https://{Subdomain.Trim()}.chargify.com/", UriKind.Absolute);
    }
}
