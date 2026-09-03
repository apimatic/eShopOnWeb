using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record CallSummaryCrelayInterruptions
{
    [JsonPropertyName("customer_to_agent")]
    public int? CustomerToAgent { get; init; } = 0;

    [JsonPropertyName("agent_to_customer")]
    public int? AgentToCustomer { get; init; } = 0;

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
