using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio Advanced Billing settings, bound from the <c>Maxio</c> configuration section.
/// Values must never be hard-coded; they are supplied via configuration / user-secrets so the same
/// build can target a different Maxio site and catalog.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>API key used for HTTP Basic auth (as the username, with a fixed "x" password).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain, used to derive the API base URL when <see cref="BaseUrl"/> is not set.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>The product family handle that scopes the plans offered to shoppers.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base URL. When set, it is used verbatim instead of deriving one from
    /// <see cref="Subdomain"/>. Useful for pointing at a non-default host.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// The payment collection method used when creating subscriptions. Defaults to <c>remittance</c>
    /// (invoice-based) so that plans which do not require a stored payment method can be subscribed to
    /// without capturing a card.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>Resolves the API base address: <see cref="BaseUrl"/> when provided, else derived from the subdomain.</summary>
    public Uri ResolveBaseAddress()
    {
        var baseUrl = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain}.chargify.com"
            : BaseUrl!;

        // Ensure a trailing slash so relative request paths resolve correctly.
        if (!baseUrl.EndsWith('/'))
        {
            baseUrl += "/";
        }

        return new Uri(baseUrl, UriKind.Absolute);
    }
}
