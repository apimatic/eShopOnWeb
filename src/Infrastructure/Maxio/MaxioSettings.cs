using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Binds the <c>Maxio:</c> configuration section. Values are supplied via environment-sourced
/// user-secrets — never committed to the repository.
/// </summary>
public sealed class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio Advanced Billing API key (used as the HTTP Basic username; password is literally <c>x</c>).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Advanced Billing site subdomain, e.g. <c>acme</c> in <c>https://acme.chargify.com</c>.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that contains the subscribable plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base address. When set, it is used verbatim; otherwise the base
    /// address is derived from <see cref="Subdomain"/> as <c>https://{Subdomain}.chargify.com</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional default plan handle used by the subscribe endpoint when the request does not
    /// specify one. Not required; when unset the request must supply a plan handle.
    /// </summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>Resolves the effective API base address, honoring <see cref="BaseUrl"/> when present.</summary>
    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl, UriKind.Absolute);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio configuration is incomplete: set Maxio:BaseUrl or Maxio:Subdomain.");
        }

        return new Uri($"https://{Subdomain}.chargify.com", UriKind.Absolute);
    }
}
