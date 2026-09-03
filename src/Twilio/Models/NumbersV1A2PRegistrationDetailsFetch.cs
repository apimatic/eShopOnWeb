using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

/// <summary>
/// Single A2P registration details response including brand and campaign compliance registration SIDs
/// </summary>
public record NumbersV1A2PRegistrationDetailsFetch
{
    /// <summary>
    /// Account Sid that the phone number belongs to in Twilio. This is only returned for phone numbers that already exist in Twilio’s inventory and belong to your account or sub account.
    /// </summary>
    [JsonPropertyName("accountSid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public required string AccountSid { get; init; }

    /// <summary>
    /// Phone Number SID for the requested phone number resource
    /// </summary>
    [JsonPropertyName("phoneNumberSid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^PN[0-9a-fA-F]{32}$")]
    public required string PhoneNumberSid { get; init; }

    [JsonPropertyName("phoneNumber")]
    public required string PhoneNumber { get; init; }

    [JsonPropertyName("externalPhoneNumberStatus")]
    public required string ExternalPhoneNumberStatus { get; init; }

    /// <summary>
    /// Campaign Sid associated with the phone number
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("campaignSid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^CM[0-9a-fA-F]{32}$")]
    public string? CampaignSid { get; init; }

    /// <summary>
    /// Messaging Service Sid that the number is associated with
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("messagingServiceSid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^MG[0-9a-fA-F]{32}$")]
    public string? MessagingServiceSid { get; init; }

    /// <summary>
    /// The identifier for a campaign in the registrar. Typically, this is the TCR Campaign Id.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("externalCampaignId")]
    public string? ExternalCampaignId { get; init; }

    /// <summary>
    /// The date and time when the A2P registration details were last updated
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("lastUpdated")]
    public DateTimeOffset? LastUpdated { get; init; }

    /// <summary>
    /// Sid associated with campaign compliance registration
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("campaignComplianceRegistrationSid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^BU[0-9a-fA-F]{32}$")]
    public string? CampaignComplianceRegistrationSid { get; init; }

    /// <summary>
    /// Sid associated with brand compliance registration
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("brandComplianceRegistrationSid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^BU[0-9a-fA-F]{32}$")]
    public string? BrandComplianceRegistrationSid { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
