namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Strongly-typed binding of the <c>Maxio</c> configuration section used to talk to
/// Maxio Advanced Billing. Values are supplied through configuration / user-secrets and
/// are never hard-coded so the same build can target a different Maxio site and catalog.
/// </summary>
public class MaxioSettings
{
    /// <summary>Configuration section name (<c>Maxio</c>).</summary>
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key, used as the HTTP Basic Authentication user name.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The Maxio site subdomain, used to derive the API base address.</summary>
    public string? Subdomain { get; set; }

    /// <summary>The handle of the product family that holds the subscribable plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim; otherwise the base
    /// address is derived from <see cref="Subdomain"/> as <c>https://{subdomain}.chargify.com</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional override for the payment collection method used when subscribing (e.g.
    /// <c>remittance</c>, <c>invoice</c>, <c>automatic</c>). When left unset the integration
    /// defaults to invoice-style collection and adapts to the site's billing architecture, so
    /// plans that don't require a stored payment method can be subscribed to without a card.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Resolves the effective API base address, honouring the <see cref="BaseUrl"/> override.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return $"https://{Subdomain}.chargify.com";
    }
}
