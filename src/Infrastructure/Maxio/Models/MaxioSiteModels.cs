using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

/// <summary>Envelope returned by <c>GET /site.json</c> (operationId <c>readSite</c>).</summary>
public sealed class MaxioSiteEnvelope
{
    [JsonPropertyName("site")]
    public MaxioSite? Site { get; set; }
}

/// <summary>Subset of the spec's <c>Site</c> schema that this integration consumes.</summary>
public sealed class MaxioSite
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("subdomain")]
    public string? Subdomain { get; set; }

    /// <summary>The site's default (primary) currency, e.g. "USD".</summary>
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    /// <summary>True for a test/sandbox site.</summary>
    [JsonPropertyName("test")]
    public bool? Test { get; set; }
}
