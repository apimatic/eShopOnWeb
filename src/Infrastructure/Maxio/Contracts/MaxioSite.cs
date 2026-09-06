using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

/// <summary>
/// Maxio OpenAPI schema <c>Site</c> (components/schemas/Site.yaml).
/// Read so plan prices can be reported in the site's own currency instead of an assumed one.
/// </summary>
public class MaxioSite
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("subdomain")]
    public string? Subdomain { get; set; }

    /// <summary>ISO code of the site's primary currency.</summary>
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("default_payment_collection_method")]
    public string? DefaultPaymentCollectionMethod { get; set; }

    /// <summary>True for a sandbox/test site.</summary>
    [JsonPropertyName("test")]
    public bool Test { get; set; }
}

/// <summary>Maxio OpenAPI schema <c>Site-Response</c> (components/schemas/Site-Response.yaml).</summary>
public class MaxioSiteEnvelope
{
    [JsonPropertyName("site")]
    public MaxioSite? Site { get; set; }
}
