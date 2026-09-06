using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

/// <summary>Wire shape of the <c>Site</c> schema.</summary>
public sealed class MaxioSite
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
    /// Whether the site runs the Relationship Invoicing architecture. This decides which
    /// Collection-Method values are valid: <c>remittance</c> on Relationship Invoicing sites,
    /// <c>invoice</c> on legacy Statements sites.
    /// </summary>
    [JsonPropertyName("relationship_invoicing_enabled")]
    public bool RelationshipInvoicingEnabled { get; set; }

    [JsonPropertyName("default_payment_collection_method")]
    public string? DefaultPaymentCollectionMethod { get; set; }

    [JsonPropertyName("test")]
    public bool Test { get; set; }
}

/// <summary>Wire shape of the <c>Site-Response</c> schema.</summary>
public sealed class MaxioSiteResponse
{
    [JsonPropertyName("site")]
    public MaxioSite? Site { get; set; }
}
