using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;

namespace TwilioSdk.Models;

public record MessagingV1ServiceUsAppToPersonV2
{
    /// <summary>
    /// The unique string that identifies a US A2P Compliance resource <c>QE2c6890da8086d771620e9b13fadeba0b</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^QE[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that the Campaign belongs to.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The unique string to identify the A2P brand.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("brand_registration_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^BN[0-9a-fA-F]{32}$")]
    public string? BrandRegistrationSid { get; init; }

    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/messaging/api/service-resource">Messaging Service</see> that the resource is associated with.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("messaging_service_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^MG[0-9a-fA-F]{32}$")]
    public string? MessagingServiceSid { get; init; }

    /// <summary>
    /// A short description of what this SMS campaign does. Min length: 40 characters. Max length: 4096 characters.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// An array of sample message strings, min two and max five. Min length for each sample: 20 chars. Max length for each sample: 1024 chars.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("message_samples")]
    public IReadOnlyList<string?>? MessageSamples { get; init; }

    /// <summary>
    /// A2P Campaign Use Case. Examples: [ 2FA, EMERGENCY, MARKETING, SOLE_PROPRIETOR...]. SOLE_PROPRIETOR campaign use cases can only be created by SOLE_PROPRIETOR Brands, and there can only be one SOLE_PROPRIETOR campaign created per SOLE_PROPRIETOR Brand.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("us_app_to_person_usecase")]
    public string? UsAppToPersonUsecase { get; init; }

    /// <summary>
    /// Indicate that this SMS campaign will send messages that contain links.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("has_embedded_links")]
    public bool? HasEmbeddedLinks { get; init; }

    /// <summary>
    /// Indicates that this SMS campaign will send messages that contain phone numbers.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("has_embedded_phone")]
    public bool? HasEmbeddedPhone { get; init; }

    /// <summary>
    /// A boolean that specifies whether campaign has Subscriber Optin or not.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("subscriber_opt_in")]
    public bool? SubscriberOptIn { get; init; }

    /// <summary>
    /// A boolean that specifies whether campaign is age gated or not.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("age_gated")]
    public bool? AgeGated { get; init; }

    /// <summary>
    /// A boolean that specifies whether campaign allows direct lending or not.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("direct_lending")]
    public bool? DirectLending { get; init; }

    /// <summary>
    /// Campaign status. Examples: IN_PROGRESS, VERIFIED, FAILED.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("campaign_status")]
    public string? CampaignStatus { get; init; }

    /// <summary>
    /// The Campaign Registry (TCR) Campaign ID.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("campaign_id")]
    public string? CampaignId { get; init; }

    /// <summary>
    /// Indicates whether the campaign was registered externally or not.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("is_externally_registered")]
    public bool? IsExternallyRegistered { get; init; }

    /// <summary>
    /// Rate limit and/or classification set by each carrier, Ex. AT&amp;T or T-Mobile.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("rate_limits")]
    public object? RateLimits { get; init; }

    /// <summary>
    /// Details around how a consumer opts-in to their campaign, therefore giving consent to receive their messages. If multiple opt-in methods can be used for the same campaign, they must all be listed. 40 character minimum. 2048 character maximum.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("message_flow")]
    public string? MessageFlow { get; init; }

    /// <summary>
    /// If end users can text in a keyword to start receiving messages from this campaign, the auto-reply messages sent to the end users must be provided. The opt-in response should include the Brand name, confirmation of opt-in enrollment to a recurring message campaign, how to get help, and clear description of how to opt-out. This field is required if end users can text in a keyword to start receiving messages from this campaign. 20 character minimum. 320 character maximum.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("opt_in_message")]
    public string? OptInMessage { get; init; }

    /// <summary>
    /// Upon receiving the opt-out keywords from the end users, Twilio customers are expected to send back an auto-generated response, which must provide acknowledgment of the opt-out request and confirmation that no further messages will be sent. It is also recommended that these opt-out messages include the brand name. This field is required if managing opt out keywords yourself (i.e. not using Twilio's Default or Advanced Opt Out features). 20 character minimum. 320 character maximum.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("opt_out_message")]
    public string? OptOutMessage { get; init; }

    /// <summary>
    /// When customers receive the help keywords from their end users, Twilio customers are expected to send back an auto-generated response; this may include the brand name and additional support contact information. This field is required if managing help keywords yourself (i.e. not using Twilio's Default or Advanced Opt Out features). 20 character minimum. 320 character maximum.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("help_message")]
    public string? HelpMessage { get; init; }

    /// <summary>
    /// If end users can text in a keyword to start receiving messages from this campaign, those keywords must be provided. This field is required if end users can text in a keyword to start receiving messages from this campaign. Values must be alphanumeric. 255 character maximum.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("opt_in_keywords")]
    public IReadOnlyList<string?>? OptInKeywords { get; init; }

    /// <summary>
    /// End users should be able to text in a keyword to stop receiving messages from this campaign. Those keywords must be provided. This field is required if managing opt out keywords yourself (i.e. not using Twilio's Default or Advanced Opt Out features). Values must be alphanumeric. 255 character maximum.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("opt_out_keywords")]
    public IReadOnlyList<string?>? OptOutKeywords { get; init; }

    /// <summary>
    /// End users should be able to text in a keyword to receive help. Those keywords must be provided as part of the campaign registration request. This field is required if managing help keywords yourself (i.e. not using Twilio's Default or Advanced Opt Out features). Values must be alphanumeric. 255 character maximum.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("help_keywords")]
    public IReadOnlyList<string?>? HelpKeywords { get; init; }

    /// <summary>
    /// The date and time in GMT when the resource was created specified in <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; init; }

    /// <summary>
    /// The date and time in GMT when the resource was last updated specified in <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_updated")]
    public DateTimeOffset? DateUpdated { get; init; }

    /// <summary>
    /// The absolute URL of the US App to Person resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    /// <summary>
    /// A boolean that specifies whether campaign is a mock or not. Mock campaigns will be automatically created if using a mock brand. Mock campaigns should only be used for testing purposes.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("mock")]
    public bool? Mock { get; init; }

    /// <summary>
    /// Details indicating why a campaign registration failed. These errors can indicate one or more fields that were incorrect or did not meet review requirements.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("errors")]
    public IReadOnlyList<object?>? Errors { get; init; }

    /// <summary>
    /// The URL of the privacy policy for the campaign.
    /// </summary>
    [JsonPropertyName("privacy_policy_url")]
    [Format(FormatKind.Uri)]
    public required string? PrivacyPolicyUrl { get; init; }

    /// <summary>
    /// The URL of the terms and conditions for the campaign.
    /// </summary>
    [JsonPropertyName("terms_and_conditions_url")]
    [Format(FormatKind.Uri)]
    public required string? TermsAndConditionsUrl { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
