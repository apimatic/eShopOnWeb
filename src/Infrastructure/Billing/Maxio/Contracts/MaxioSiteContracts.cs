using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

internal sealed class MaxioSite
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
    /// Decides which collection methods the site accepts: "remittance"/"automatic"/"prepaid" under
    /// Relationship Invoicing, "invoice"/"automatic" under the legacy Statements architecture.
    /// </summary>
    [JsonPropertyName("relationship_invoicing_enabled")]
    public bool RelationshipInvoicingEnabled { get; set; }

    [JsonPropertyName("default_payment_collection_method")]
    public string? DefaultPaymentCollectionMethod { get; set; }

    [JsonPropertyName("test")]
    public bool Test { get; set; }
}

internal sealed class MaxioSiteEnvelope
{
    [JsonPropertyName("site")]
    public MaxioSite? Site { get; set; }
}
