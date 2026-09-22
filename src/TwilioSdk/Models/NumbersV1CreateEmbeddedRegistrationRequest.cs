using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;

namespace TwilioSdk.Models;

public record NumbersV1CreateEmbeddedRegistrationRequest
{
    /// <summary>
    /// The regulation for this registration.
    /// </summary>
    [JsonPropertyName("regulationId")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^RN[0-9a-fA-F]{32}$")]
    public required string RegulationId { get; init; }

    /// <summary>
    /// The regulation version.
    /// </summary>
    [JsonPropertyName("regulationVersion")]
    [Minimum(1)]
    public required int RegulationVersion { get; init; }

    /// <summary>
    /// Human-readable name for the registration.
    /// </summary>
    [JsonPropertyName("friendlyName")]
    [MaxLength(255)]
    public required string FriendlyName { get; init; }

    /// <summary>
    /// Email address for registration status notifications.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("statusNotificationEmail")]
    [MaxLength(255)]
    [Format(FormatKind.Email)]
    public string? StatusNotificationEmail { get; init; }

    /// <summary>
    /// The URL of this resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("statusCallbackUrl")]
    [Format(FormatKind.Uri)]
    public string? StatusCallbackUrl { get; init; }

    /// <summary>
    /// Additional comments about the registration.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("comments")]
    [MaxLength(255)]
    public string? Comments { get; init; }

    /// <summary>
    /// Theme ID for the Compliance Embeddable UI.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("themeSetId")]
    [MaxLength(255)]
    public string? ThemeSetId { get; init; }

    /// <summary>
    /// Registration data organized by section (alphanumericSender, business, useCase, authorizedRepresentative, officer, businessAddress).
    /// </summary>
    [JsonPropertyName("data")]
    public required object Data { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
