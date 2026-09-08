using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Options bound from the <c>Maxio</c> configuration section.
///
/// <list type="bullet">
/// <item><c>Maxio:ApiKey</c> - Advanced Billing API key (basic-auth username; password is "x").</item>
/// <item><c>Maxio:Subdomain</c> - Advanced Billing site subdomain (e.g. <c>cp-exp-4</c>).</item>
/// <item><c>Maxio:ProductFamilyHandle</c> - handle of the product family that holds the plans this app sells.</item>
/// <item><c>Maxio:BaseUrl</c> - optional base URL override. When set it is used verbatim; otherwise the base URL is
/// derived from the subdomain.</item>
/// </list>
/// Values are supplied through configuration (environment variables / .NET user-secrets) and are never hard-coded.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string? ApiKey { get; set; }

    public string? Subdomain { get; set; }

    public string? ProductFamilyHandle { get; set; }

    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the Advanced Billing API base address.
    /// </summary>
    /// <exception cref="MaxioConfigurationException">Thrown when the options required to derive a base URL are missing.</exception>
    public Uri ResolveBaseUri()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl.TrimEnd('/'), UriKind.Absolute);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new MaxioConfigurationException(
                $"'{SectionName}:{nameof(BaseUrl)}' is not configured and '{SectionName}:{nameof(Subdomain)}' is empty; " +
                $"set {SectionName}:{nameof(BaseUrl)} or provide a site subdomain.");
        }

        // Default Advanced Billing (US) environment. Non-default hosting regions must provide an explicit BaseUrl.
        return new Uri($"https://{Subdomain.Trim().ToLowerInvariant()}.chargify.com", UriKind.Absolute);
    }

    public string RequireApiKey()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new MaxioConfigurationException(
                $"'{SectionName}:{nameof(ApiKey)}' is not configured. Provide the Advanced Billing API key through configuration.");
        }

        return ApiKey;
    }

    public string RequireProductFamilyHandle()
    {
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new MaxioConfigurationException(
                $"'{SectionName}:{nameof(ProductFamilyHandle)}' is not configured.");
        }

        return ProductFamilyHandle;
    }
}
