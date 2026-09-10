using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// Values are supplied via .NET user-secrets / environment and are never committed to the repository.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key. Used as the HTTP Basic username (password is the literal "x") per the spec.</summary>
    [Required]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain, used to derive the API base address when <see cref="BaseUrl"/> is not set.</summary>
    [Required]
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    [Required]
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base address. When set, it is used verbatim; otherwise the base address is
    /// derived from <see cref="Subdomain"/> as https://{subdomain}.chargify.com per the spec's server template.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Resolves the API base address, honoring <see cref="BaseUrl"/> when provided.</summary>
    public Uri ResolveBaseAddress()
    {
        var root = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!.Trim()
            : $"https://{Subdomain.Trim()}.chargify.com";

        // HttpClient combines relative request URIs against BaseAddress only when it ends with '/'.
        if (!root.EndsWith('/'))
        {
            root += "/";
        }

        return new Uri(root, UriKind.Absolute);
    }
}
