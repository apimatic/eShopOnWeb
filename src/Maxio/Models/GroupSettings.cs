using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record GroupSettings
{
    /// <summary>
    /// Attributes of the target customer who will be the responsible payer of the created subscription. Required.
    /// </summary>
    [JsonPropertyName("target")]
    public required GroupTarget Target { get; init; }

    /// <summary>
    /// (Optional) Attributes related to billing date and accrual. Note: Only applicable for new subscriptions.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("billing")]
    public GroupBilling? Billing { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
