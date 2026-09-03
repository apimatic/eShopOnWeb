using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

/// <summary>
/// Number of calls made in each type.
/// <c>carrier</c>, <c>sip</c>, <c>trunking</c>, <c>client</c>, <c>whatsapp</c>
/// </summary>
public record CallType
{
    /// <summary>
    /// Number of carrier calls
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("carrier")]
    public int? Carrier { get; init; }

    /// <summary>
    /// Number of SIP calls
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sip")]
    public int? Sip { get; init; }

    /// <summary>
    /// Number of trunking calls
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("trunking")]
    public int? Trunking { get; init; }

    /// <summary>
    /// Number of client calls
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("client")]
    public int? Client { get; init; }

    /// <summary>
    /// Number of WhatsApp Business calls
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("whatsapp")]
    public int? Whatsapp { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
