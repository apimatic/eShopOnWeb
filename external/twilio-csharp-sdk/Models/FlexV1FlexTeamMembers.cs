using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record FlexV1FlexTeamMembers
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("flex_user_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^FU[0-9a-fA-F]{32}$")]
    public string? FlexUserSid { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("friendly_name")]
    public string? FriendlyName { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("worker_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WK[0-9a-fA-F]{32}$")]
    public string? WorkerSid { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("team_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^QO[0-9a-fA-F]{32}$")]
    public string? TeamSid { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("instance_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^GO[0-9a-fA-F]{32}$")]
    public string? InstanceSid { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
