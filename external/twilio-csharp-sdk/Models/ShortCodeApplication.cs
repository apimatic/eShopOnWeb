using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record ShortCodeApplication
{
    /// <summary>
    /// The unique identifier of the Short Code Application.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AP[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// The Application Requirements SID.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("application_requirements_sid")]
    public string? ApplicationRequirementsSid { get; init; }

    /// <summary>
    /// The version of the application requirements.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("application_requirements_version")]
    public int? ApplicationRequirementsVersion { get; init; }

    /// <summary>
    /// The Account SID associated with the application.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The Bundle SID for regulatory compliance.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("bundle_sid")]
    public string? BundleSid { get; init; }

    /// <summary>
    /// The reviewer of the application.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("reviewer")]
    [MaxLength(34)]
    public string? Reviewer { get; init; }

    /// <summary>
    /// The Zendesk ticket ID associated with the application.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("zendesk_ticket_id")]
    [MaxLength(34)]
    public string? ZendeskTicketId { get; init; }

    /// <summary>
    /// The friendly name of the application.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("friendly_name")]
    public string? FriendlyName { get; init; }

    /// <summary>
    /// The notification emails for the application.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("notification_emails")]
    [MaxLength(5)]
    public IReadOnlyList<string>? NotificationEmails { get; init; }

    /// <summary>
    /// The ISO country code.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("iso_country")]
    [StringLength(2, MinimumLength = 2)]
    public string? IsoCountry { get; init; }

    /// <summary>
    /// The state of the application.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("state")]
    public State? State { get; init; }

    /// <summary>
    /// Setup configuration for the application.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("setup")]
    public Setup1? Setup { get; init; }

    /// <summary>
    /// Business information associated with the application.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("business_information")]
    public BusinessInformation1? BusinessInformation { get; init; }

    /// <summary>
    /// User sign-up configuration for the application.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("user_sign_up")]
    public UserSignUp? UserSignUp { get; init; }

    /// <summary>
    /// Compliance keywords for the application.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("compliance_keywords")]
    public ComplianceKeywords? ComplianceKeywords { get; init; }

    /// <summary>
    /// Content examples for the application.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("content_examples")]
    public ContentExamples? ContentExamples { get; init; }

    /// <summary>
    /// SMS campaign details for the application.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sms_campaign_details")]
    public SmsCampaignDetails? SmsCampaignDetails { get; init; }

    /// <summary>
    /// The date and time the application was created.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; init; }

    /// <summary>
    /// The date and time the application was last updated.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_updated")]
    public DateTimeOffset? DateUpdated { get; init; }

    /// <summary>
    /// The identity of the user who created the application.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("created_by")]
    public string? CreatedBy { get; init; }

    /// <summary>
    /// The identity of the user who last updated the application.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("updated_by")]
    public string? UpdatedBy { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
