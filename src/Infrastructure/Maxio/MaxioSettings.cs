using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed binding of the <c>Maxio:</c> configuration section. Values are supplied
/// via configuration/user-secrets (never committed): <c>Maxio:ApiKey</c>,
/// <c>Maxio:Subdomain</c>, <c>Maxio:ProductFamilyHandle</c>, and the optional
/// <c>Maxio:BaseUrl</c> override.
/// </summary>
public class MaxioSettings
{
    public const string ConfigSectionName = "Maxio";

    /// <summary>Maxio site API key. Used as the HTTP Basic auth username (password is the literal "x").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain, used to derive the API base address when <see cref="BaseUrl"/> is not set.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base address. When set, it is used verbatim instead of deriving
    /// one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the API base address: the <see cref="BaseUrl"/> override when present,
    /// otherwise <c>https://{Subdomain}.chargify.com/</c>. A trailing slash is guaranteed so
    /// relative request paths resolve correctly.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl!.TrimEnd('/') + "/");
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio configuration is incomplete: set either 'Maxio:BaseUrl' or 'Maxio:Subdomain'.");
        }

        return new Uri($"https://{Subdomain}.chargify.com/");
    }
}
