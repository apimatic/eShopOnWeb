using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

/// <summary>Maps the <c>Site</c> schema (maxio-spec/components/schemas/Site.yaml).</summary>
public class MaxioSite
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("subdomain")]
    public string? Subdomain { get; set; }

    /// <summary>The site's primary currency, e.g. <c>USD</c>.</summary>
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("default_payment_collection_method")]
    public string? DefaultPaymentCollectionMethod { get; set; }

    /// <summary>True for a test/sandbox site.</summary>
    [JsonPropertyName("test")]
    public bool? Test { get; set; }
}

/// <summary>Maps the <c>Site-Response</c> schema.</summary>
public class MaxioSiteResponse
{
    [JsonPropertyName("site")]
    public MaxioSite? Site { get; set; }
}
