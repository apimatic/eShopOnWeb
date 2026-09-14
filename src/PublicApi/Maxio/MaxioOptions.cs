using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio"
/// configuration section. The build must run against any Maxio site/catalog, so no
/// value is hard-coded: ApiKey/Subdomain/ProductFamilyHandle are required and
/// BaseUrl is an optional override that is used verbatim when set.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Advanced Billing API key (Basic auth username).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Advanced Billing site subdomain, e.g. "cp-exp-7".</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that hosts the subscribable plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Optional full API base address override, used verbatim when set.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Builds the API base address used for every request. When <see cref="BaseUrl"/>
    /// is set it wins; otherwise the address is derived from <see cref="Subdomain"/>.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var configured))
            {
                throw new MaxioConfigurationException(
                    $"Maxio:BaseUrl ('{BaseUrl}') is not a valid absolute URL.");
            }

            var path = configured.AbsolutePath.EndsWith("/", StringComparison.Ordinal)
                ? configured.AbsolutePath
                : configured.AbsolutePath + "/";
            return new Uri(configured.GetLeftPart(UriPartial.Authority) + path, UriKind.Absolute);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured. Set the 'Maxio:Subdomain' (and 'Maxio:ApiKey') " +
                "configuration values (e.g. via user-secrets / MAXIO_SITE_SUBDOMAIN / MAXIO_API_KEY).");
        }

        return new Uri($"https://{Subdomain}.chargify.com/", UriKind.Absolute);
    }
}
