using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

/// <summary>
/// The billing information for the phone number.
/// </summary>
public record Billing
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("RecurringBillableItemSid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^BI[0-9a-fA-F]{32}$")]
    public string? RecurringBillableItemSid { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("SetupBillableItemSid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^BI[0-9a-fA-F]{32}$")]
    public string? SetupBillableItemSid { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
