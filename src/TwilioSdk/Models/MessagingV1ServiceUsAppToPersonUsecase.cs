using System.Collections.Generic;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record MessagingV1ServiceUsAppToPersonUsecase
{
    /// <summary>
    /// Human readable name, code, description and post_approval_required (indicates whether or not post approval is required for this Use Case) of A2P Campaign Use Cases.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("us_app_to_person_usecases")]
    public IReadOnlyList<object?>? UsAppToPersonUsecases { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
