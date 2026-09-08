using System;

namespace Microsoft.eShopWeb;

/// <summary>
/// Bound from the <c>Maxio:</c> configuration section. No value is ever hard-coded:
/// <c>ApiKey</c>, <c>Subdomain</c> and <c>ProductFamilyHandle</c> are supplied at runtime
/// (e.g. via user-secrets), and <c>BaseUrl</c> is an optional verbatim override.
/// </summary>
public class MaxioSettings
{
    public const string SECTION_NAME = "Maxio";

    /// <summary>Maxio / Chargify API key used as the HTTP Basic username (password is "x").</summary>
    public string? ApiKey { get; set; }

    /// <summary>The Advanced Billing site subdomain, supplied via <c>Maxio:Subdomain</c>.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the Product Family that contains the subscribable plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional. When set, used verbatim as the API base address instead of deriving one
    /// from <see cref="Subdomain"/>. Lets the same build target a different site / environment.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the Maxio Advanced Billing base address. Per the OpenAPI server template, the
    /// US environment is <c>https://&#123;site&#125;.chargify.com</c>. An explicit
    /// <see cref="BaseUrl"/> always wins.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio settings are incomplete: either 'Maxio:BaseUrl' or 'Maxio:Subdomain' must be configured.");
        }

        return $"https://{Subdomain.Trim().TrimEnd('.')}.chargify.com";
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("Maxio settings are incomplete: 'Maxio:ApiKey' must be configured.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio settings are incomplete: 'Maxio:ProductFamilyHandle' must be configured.");
        }

        // Forces the base-address derivation to fail fast if it cannot be resolved.
        _ = ResolveBaseUrl();
    }
}
