using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio configuration, bound from the <c>Maxio</c> configuration section.
/// Values are supplied at runtime (user-secrets / environment) and are never committed.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key, used as the HTTP Basic username.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Site subdomain, used to derive the API base address when <see cref="BaseUrl"/> is unset.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that contains the subscribable plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim; otherwise the base
    /// address is derived from <see cref="Subdomain"/> as <c>https://{subdomain}.chargify.com/</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// How Maxio collects payment for subscriptions created by this app. Defaults to
    /// <c>remittance</c> (invoice billing) so that plans configured as "payment method not
    /// required" can be subscribed to without capturing a card. Legacy Statements sites should
    /// set this to <c>invoice</c>; sites that capture a card up front can use <c>automatic</c>.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>Resolves the API base address, honoring <see cref="BaseUrl"/> when present.</summary>
    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var value = BaseUrl.EndsWith('/') ? BaseUrl : BaseUrl + "/";
            return new Uri(value, UriKind.Absolute);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio configuration is incomplete: set either Maxio:BaseUrl or Maxio:Subdomain.");
        }

        return new Uri($"https://{Subdomain}.chargify.com/", UriKind.Absolute);
    }

    /// <summary>Validates that the minimum settings required to call Maxio are present.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("Maxio configuration is incomplete: Maxio:ApiKey is required.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio configuration is incomplete: Maxio:ProductFamilyHandle is required.");
        }

        // Also surfaces a missing subdomain/base url early.
        _ = ResolveBaseAddress();
    }
}
