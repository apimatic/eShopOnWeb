using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record InsightsV1CallEvent
{
    /// <summary>
    /// Event time.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; init; }

    /// <summary>
    /// The unique SID identifier of the Call.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("call_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^CA[0-9a-fA-F]{32}$")]
    public string? CallSid { get; init; }

    /// <summary>
    /// The unique SID identifier of the Account.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("edge")]
    public EventEnumTwilioEdge? Edge { get; init; }

    /// <summary>
    /// Event group.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("group")]
    public string? Group { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("level")]
    public EventEnumLevel? Level { get; init; }

    /// <summary>
    /// Event name.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// <c>object</c> Represents the connection between Twilio and our immediate carrier partners. The events here describe the call lifecycle as reported by Twilio's carrier media gateways. See <see href="https://www.twilio.com/docs/voice/voice-insights/api/call/details-call-summary#edges-and-their-properties">Details: Call Summary</see> for the object properties.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("carrier_edge")]
    public object? CarrierEdge { get; init; }

    /// <summary>
    /// <c>object</c> Represents the Twilio media gateway for SIP interface and SIP trunking calls. The events here describe the call lifecycle as reported by Twilio's public media gateways. See <see href="https://www.twilio.com/docs/voice/voice-insights/api/call/details-call-summary#edges-and-their-properties">Details: Call Summary</see> for the object properties.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sip_edge")]
    public object? SipEdge { get; init; }

    /// <summary>
    /// <c>object</c> Represents the Voice SDK running locally in the browser or in the Android/iOS application. The events here are emitted by the Voice SDK in response to certain call progress events, network changes, or call quality conditions. See <see href="https://www.twilio.com/docs/voice/voice-insights/api/call/details-call-summary#edges-and-their-properties">Details: Call Summary</see> for the object properties.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sdk_edge")]
    public object? SdkEdge { get; init; }

    /// <summary>
    /// <c>object</c> Represents the Twilio media gateway for Client calls. The events here describe the call lifecycle as reported by Twilio's Voice SDK media gateways. See <see href="https://www.twilio.com/docs/voice/voice-insights/api/call/details-call-summary#edges-and-their-properties">Details: Call Summary</see> for the object properties.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("client_edge")]
    public object? ClientEdge { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
