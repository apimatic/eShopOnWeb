using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

/// <summary>Envelope for maxio-spec components/schemas/Site-Response.yaml.</summary>
public class SiteResponse
{
    [JsonPropertyName("site")]
    public MaxioSite? Site { get; set; }
}

/// <summary>maxio-spec components/schemas/Site.yaml (attributes consumed by this integration).</summary>
public class MaxioSite
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("subdomain")]
    public string? Subdomain { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    /// <summary>
    /// True when the site runs the Relationship Invoicing architecture, which decides the valid
    /// payment collection methods (see components/schemas/Collection-Method.yaml).
    /// </summary>
    [JsonPropertyName("relationship_invoicing_enabled")]
    public bool RelationshipInvoicingEnabled { get; set; }

    [JsonPropertyName("default_payment_collection_method")]
    public string? DefaultPaymentCollectionMethod { get; set; }

    [JsonPropertyName("test")]
    public bool? Test { get; set; }
}
