using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record ActivateSubscriptionRequest
{
    /// <summary>
    /// You may choose how to handle the activation failure. <c>true</c> means do not change the subscription’s state and billing period. <c>false</c> means to continue through with the activation and enter an end-of-life state. If this parameter is omitted or <c>null</c> is passed it will default to the value set in the site settings (default: <c>true</c>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("revert_on_failure")]
    public bool? RevertOnFailure { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
