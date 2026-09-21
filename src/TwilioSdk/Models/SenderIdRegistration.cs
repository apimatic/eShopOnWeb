using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record SenderIdRegistration
{
    /// <summary>
    /// Sender ID Registration Application SID
    /// </summary>
    [JsonPropertyName("application_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WF[0-9a-fA-F]{32}$")]
    public required string ApplicationSid { get; init; }

    /// <summary>
    /// Owning Account SID of the Sender ID
    /// </summary>
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public required string AccountSid { get; init; }

    /// <summary>
    /// Status of the Sender ID Registration Application
    /// </summary>
    [JsonPropertyName("status")]
    public required Status2 Status { get; init; }

    /// <summary>
    /// List of Sender ID Registration information
    /// </summary>
    [JsonPropertyName("registration_info")]
    public required IReadOnlyList<object> RegistrationInfo { get; init; }

    /// <summary>
    /// Purpose for using Sender ID
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("purpose")]
    public SenderIdPurpose? Purpose { get; init; }

    /// <summary>
    /// Whether registering on behalf of subsidiary
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("company_subsidiary")]
    public bool? CompanySubsidiary { get; init; }

    /// <summary>
    /// List of emails to send Sender ID Application updates
    /// </summary>
    [JsonPropertyName("emails_for_notification")]
    [MaxLength(5)]
    public required IReadOnlyList<string> EmailsForNotification { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
