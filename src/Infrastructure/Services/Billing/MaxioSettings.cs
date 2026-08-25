namespace Microsoft.eShopWeb.Infrastructure.Services.Billing;

/// <summary>
/// Settings bound from the "Maxio" configuration section. Values are supplied via
/// user-secrets / environment variables — never from files committed to the repo.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";
    public const string HttpClientName = "Maxio";

    /// <summary>API key used as the Basic-auth username (from MAXIO_API_KEY).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Site subdomain, e.g. "cp-exp-1" (from MAXIO_SITE_SUBDOMAIN).</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family holding the subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional base-URL override. When set it is used verbatim as the API base address
    /// instead of the URL derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Optional environment selector: "Us" (default) or "Eu".</summary>
    public string? Environment { get; set; }

    /// <summary>
    /// Payment collection method for new subscriptions ("automatic", "remittance", "prepaid").
    /// Defaults to "remittance" because this app has no card-capture flow, so automatic
    /// collection could never succeed at signup.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }
}
