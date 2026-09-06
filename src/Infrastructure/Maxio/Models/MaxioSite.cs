using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

/// <summary>
/// Maxio Advanced Billing <c>Site</c>, per <c>maxio-spec/components/schemas/Site.yaml</c>.
/// eShopOnWeb reads it for the site currency, which plan payloads do not carry.
/// </summary>
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
}

/// <summary>
/// Maxio <c>Site Response</c> envelope.
/// </summary>
public class MaxioSiteResponse
{
    [JsonPropertyName("site")]
    public MaxioSite? Site { get; set; }
}
