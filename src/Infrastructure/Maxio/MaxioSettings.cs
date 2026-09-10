using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio configuration, bound from the <c>Maxio</c> configuration section.
/// Values are supplied via .NET user-secrets / environment configuration and are never
/// committed to the repository.
/// </summary>
public sealed class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key, used as the HTTP Basic auth username.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain, used to derive the API base address when no explicit base url is set.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are exposed as subscription plans.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim; otherwise the base
    /// address is derived from <see cref="Subdomain"/> as <c>https://{subdomain}.chargify.com/</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Payment collection method for new subscriptions. Defaults to <c>remittance</c>
    /// (invoice-based, no card capture) so shoppers can subscribe to payment-method-not-required
    /// plans without providing card details. Valid values depend on the site's architecture:
    /// Relationship Invoicing accepts <c>automatic</c>/<c>remittance</c>/<c>prepaid</c>; legacy
    /// statement-based sites accept <c>automatic</c>/<c>invoice</c>.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>Resolves the effective API base address, honouring an explicit <see cref="BaseUrl"/> override.</summary>
    public Uri ResolveBaseAddress()
    {
        var raw = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain}.chargify.com/"
            : BaseUrl!;

        // HttpClient combines relative request paths correctly only when the base address ends with '/'.
        if (!raw.EndsWith('/'))
        {
            raw += "/";
        }

        return new Uri(raw, UriKind.Absolute);
    }
}
