using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

/// <summary>Envelope of <c>GET /site.json</c>: <c>{ "site": { ... } }</c>.</summary>
internal sealed class MaxioSiteEnvelope
{
    [JsonPropertyName("site")]
    public MaxioSite? Site { get; set; }
}

/// <summary>
/// Site-level settings. Products carry a price but no currency, so the site's default currency is
/// what plan prices are quoted in.
/// </summary>
internal sealed class MaxioSite
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("subdomain")]
    public string? Subdomain { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    /// <summary>True for sandbox sites, where test data can be purged.</summary>
    [JsonPropertyName("test")]
    public bool Test { get; set; }
}
