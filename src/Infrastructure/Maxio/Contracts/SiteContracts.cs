using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

/// <summary>Wire model for the specification's <c>Site</c> schema (only the members this integration reads).</summary>
public sealed record MaxioSite
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("subdomain")]
    public string? Subdomain { get; init; }

    /// <summary>ISO-4217 code of the site's primary currency.</summary>
    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    /// <summary>
    /// Distinguishes the Relationship Invoicing architecture from the legacy Statements architecture,
    /// which decides which non-automatic payment collection methods the site accepts.
    /// </summary>
    [JsonPropertyName("relationship_invoicing_enabled")]
    public bool RelationshipInvoicingEnabled { get; init; }

    [JsonPropertyName("default_payment_collection_method")]
    public string? DefaultPaymentCollectionMethod { get; init; }

    [JsonPropertyName("test")]
    public bool? Test { get; init; }
}

/// <summary>Wire model for the specification's <c>Site Response</c> schema.</summary>
public sealed record SiteResponse
{
    [JsonPropertyName("site")]
    public MaxioSite? Site { get; init; }
}
