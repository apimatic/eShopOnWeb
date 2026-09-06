using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

/// <summary>
/// Maxio <c>Site</c> (<c>maxio-spec/components/schemas/Site.yaml</c>).
/// </summary>
/// <remarks>
/// Read so that plan prices can be shown in the currency the site actually bills in. The product
/// schema carries no currency of its own, so the site record is the specification-backed source
/// for it.
/// </remarks>
public class MaxioSite
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("subdomain")]
    public string? Subdomain { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("default_payment_collection_method")]
    public string? DefaultPaymentCollectionMethod { get; set; }

    [JsonPropertyName("test")]
    public bool Test { get; set; }
}

/// <summary>
/// Maxio <c>Site Response</c> (<c>maxio-spec/components/schemas/Site-Response.yaml</c>).
/// </summary>
public class MaxioSiteResponse
{
    [JsonPropertyName("site")]
    public MaxioSite? Site { get; set; }
}
