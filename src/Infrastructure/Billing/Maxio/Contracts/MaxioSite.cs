using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

/// <summary>
/// The Maxio site itself. Read for its currency, which products do not carry but prices are quoted in.
/// </summary>
public class MaxioSite
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("subdomain")]
    public string? Subdomain { get; set; }

    /// <summary>ISO 4217 code of the site's primary currency.</summary>
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    /// <summary>True for a sandbox/test site.</summary>
    [JsonPropertyName("test")]
    public bool Test { get; set; }
}
