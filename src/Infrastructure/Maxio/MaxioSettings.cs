using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Bound from the <c>Maxio</c> configuration section. Values are supplied by configuration
/// (user secrets / environment) and must never be committed to the repository.
/// </summary>
public class MaxioSettings
{
    public const string ConfigurationSection = "Maxio";

    /// <summary>Site API key, used as the user name of the HTTP Basic credential.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Site subdomain, e.g. <c>acme</c> for <c>https://acme.chargify.com</c>.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that holds the subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override of the API base address. When set it is used verbatim instead of deriving
    /// the address from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// The API base address. Resolution deliberately fails loudly rather than silently targeting the
    /// wrong site.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        var address = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain.Trim()}.chargify.com/"
            : BaseUrl.Trim();

        // Relative request paths are resolved against the base address, which only works when the
        // address ends in a separator. Everything else about an explicit BaseUrl is preserved as given.
        if (!address.EndsWith('/'))
        {
            address += "/";
        }

        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"'{MaxioSettings.ConfigurationSection}:{nameof(BaseUrl)}' is not a valid absolute URL.");
        }

        return uri;
    }
}
