using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;

namespace TwilioSdk.Models;

public record NumbersV2RegulatoryComplianceSupportingDocumentType
{
    /// <summary>
    /// The unique string that identifies the Supporting Document Type resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^OY[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// A human-readable description of the Supporting Document Type resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("friendly_name")]
    public string? FriendlyName { get; init; }

    /// <summary>
    /// The machine-readable description of the Supporting Document Type resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("machine_name")]
    public string? MachineName { get; init; }

    /// <summary>
    /// The required information for creating a Supporting Document. The required fields will change as regulatory needs change and will differ for businesses and individuals.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("fields")]
    public IReadOnlyList<object?>? Fields { get; init; }

    /// <summary>
    /// The absolute URL of the Supporting Document Type resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
