using System.Text.Json.Serialization;

namespace Twilio.Models;

/// <summary>
/// whatsapp/flows templates allow you to send multiple messages in a set order with text or select options
/// </summary>
public record WhatsappFlows
{
    [JsonPropertyName("body")]
    public required string Body { get; init; }

    [JsonPropertyName("button_text")]
    public required string ButtonText { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("media_url")]
    public string? MediaUrl { get; init; }

    [JsonPropertyName("flow_id")]
    public required string FlowId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("flow_token")]
    public string? FlowToken { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("flow_first_page_id")]
    public string? FlowFirstPageId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("is_flow_first_page_endpoint")]
    public bool? IsFlowFirstPageEndpoint { get; init; }
}
