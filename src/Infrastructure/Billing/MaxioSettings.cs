using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Strongly-typed view of the <c>Maxio:</c> configuration section. Values are supplied at
/// runtime (user-secrets / environment) and must never be committed to the repository.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key, used as the HTTP Basic username (password is a literal "X").</summary>
    public string? ApiKey { get; set; }

    /// <summary>Site subdomain, used to derive the API base address when <see cref="BaseUrl"/> is unset.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim (instead of deriving
    /// one from <see cref="Subdomain"/>), allowing the same build to target a different site.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the API base address: the explicit <see cref="BaseUrl"/> when provided,
    /// otherwise <c>https://{Subdomain}.chargify.com/</c>. Always returns a trailing slash so
    /// relative request paths compose correctly.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var trimmed = BaseUrl.Trim();
            if (!trimmed.EndsWith('/')) trimmed += "/";
            return new Uri(trimmed, UriKind.Absolute);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio configuration is incomplete: set either 'Maxio:BaseUrl' or 'Maxio:Subdomain'.");
        }

        return new Uri($"https://{Subdomain.Trim()}.chargify.com/", UriKind.Absolute);
    }
}
