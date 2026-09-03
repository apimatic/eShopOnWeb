using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record ResumeOptions
{
    /// <summary>
    /// Chargify will only attempt to resume the subscription's billing period. If not resumable, the subscription will be left in its current state.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("require_resume")]
    public bool? RequireResume { get; init; }

    /// <summary>
    /// Indicates whether or not Chargify should clear the subscription's existing balance before attempting to resume the subscription. If subscription cannot be resumed, the balance will remain as it was before the attempt to resume was made.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("forgive_balance")]
    public bool? ForgiveBalance { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
