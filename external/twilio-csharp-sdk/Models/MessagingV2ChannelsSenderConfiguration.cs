using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Models.Enums;

namespace Twilio.Models;

/// <summary>
/// The configuration settings for creating a sender.
/// </summary>
public record MessagingV2ChannelsSenderConfiguration
{
    /// <summary>
    /// The ID of the WhatsApp Business Account (WABA) to use for this sender.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("waba_id")]
    public string? WabaId { get; init; }

    /// <summary>
    /// The verification method.
    /// </summary>
    [JsonPropertyName("verification_method")]
    public VerificationMethod? VerificationMethod { get; init; } = VerificationMethod.Sms;

    /// <summary>
    /// The verification code.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("verification_code")]
    public string? VerificationCode { get; init; }

    /// <summary>
    /// The SID of the Twilio Voice application.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("voice_application_sid")]
    public string? VoiceApplicationSid { get; init; }

    /// <summary>
    /// The account type for ISV Account Type Migration. Set to 'ISV' or 'ISVSubAccount' to configure, empty string to clear, or omit to preserve the existing value.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_type")]
    public AccountType? AccountType { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
