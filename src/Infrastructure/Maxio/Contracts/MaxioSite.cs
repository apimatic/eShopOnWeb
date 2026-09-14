using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

internal sealed class MaxioSiteEnvelope
{
    [JsonPropertyName("site")]
    public MaxioSite? Site { get; set; }
}

/// <summary>Site level metadata. Read for the primary currency, which products do not carry.</summary>
internal sealed class MaxioSite
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("subdomain")]
    public string? Subdomain { get; set; }

    /// <summary>ISO 4217 code of the primary currency.</summary>
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    /// <summary>True for sandbox/test sites.</summary>
    [JsonPropertyName("test")]
    public bool Test { get; set; }
}
