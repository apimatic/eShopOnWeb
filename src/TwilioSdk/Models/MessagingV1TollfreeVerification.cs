using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record MessagingV1TollfreeVerification
{
    /// <summary>
    /// The unique string to identify Tollfree Verification.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^HH[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Tollfree Verification resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// Customer's Profile Bundle BundleSid.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("customer_profile_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^BU[0-9a-fA-F]{32}$")]
    public string? CustomerProfileSid { get; init; }

    /// <summary>
    /// Tollfree TrustProduct Bundle BundleSid.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("trust_product_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^BU[0-9a-fA-F]{32}$")]
    public string? TrustProductSid { get; init; }

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
    /// The SID of the Regulated Item.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("regulated_item_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^RA[0-9a-fA-F]{32}$")]
    public string? RegulatedItemSid { get; init; }

    /// <summary>
    /// The name of the business or organization using the Tollfree number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("business_name")]
    public string? BusinessName { get; init; }

    /// <summary>
    /// The address of the business or organization using the Tollfree number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("business_street_address")]
    public string? BusinessStreetAddress { get; init; }

    /// <summary>
    /// The address of the business or organization using the Tollfree number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("business_street_address2")]
    public string? BusinessStreetAddress2 { get; init; }

    /// <summary>
    /// The city of the business or organization using the Tollfree number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("business_city")]
    public string? BusinessCity { get; init; }

    /// <summary>
    /// The state/province/region of the business or organization using the Tollfree number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("business_state_province_region")]
    public string? BusinessStateProvinceRegion { get; init; }

    /// <summary>
    /// The postal code of the business or organization using the Tollfree number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("business_postal_code")]
    public string? BusinessPostalCode { get; init; }

    /// <summary>
    /// The country of the business or organization using the Tollfree number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("business_country")]
    public string? BusinessCountry { get; init; }

    /// <summary>
    /// The website of the business or organization using the Tollfree number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("business_website")]
    public string? BusinessWebsite { get; init; }

    /// <summary>
    /// The first name of the contact for the business or organization using the Tollfree number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("business_contact_first_name")]
    public string? BusinessContactFirstName { get; init; }

    /// <summary>
    /// The last name of the contact for the business or organization using the Tollfree number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("business_contact_last_name")]
    public string? BusinessContactLastName { get; init; }

    /// <summary>
    /// The email address of the contact for the business or organization using the Tollfree number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("business_contact_email")]
    public string? BusinessContactEmail { get; init; }

    /// <summary>
    /// The E.164 formatted phone number of the contact for the business or organization using the Tollfree number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("business_contact_phone")]
    public string? BusinessContactPhone { get; init; }

    /// <summary>
    /// The email address to receive the notification about the verification result. .
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("notification_email")]
    public string? NotificationEmail { get; init; }

    /// <summary>
    /// The category of the use case for the Tollfree Number. List as many as are applicable.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("use_case_categories")]
    public IReadOnlyList<TollfreeVerificationEnumUseCaseCategory?>? UseCaseCategories { get; init; }

    /// <summary>
    /// Use this to further explain how messaging is used by the business or organization.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("use_case_summary")]
    public string? UseCaseSummary { get; init; }

    /// <summary>
    /// An example of message content, i.e. a sample message.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("production_message_sample")]
    public string? ProductionMessageSample { get; init; }

    /// <summary>
    /// Link to an image that shows the opt-in workflow. Multiple images allowed and must be a publicly hosted URL.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("opt_in_image_urls")]
    public IReadOnlyList<string?>? OptInImageUrls { get; init; }

    /// <summary>
    /// Describe how a user opts-in to text messages.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("opt_in_type")]
    public TollfreeVerificationEnumOptInType? OptInType { get; init; }

    /// <summary>
    /// Estimate monthly volume of messages from the Tollfree Number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("message_volume")]
    public string? MessageVolume { get; init; }

    /// <summary>
    /// Additional information to be provided for verification.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("additional_information")]
    public string? AdditionalInformation { get; init; }

    /// <summary>
    /// The SID of the Phone Number associated with the Tollfree Verification.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tollfree_phone_number_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^PN[0-9a-fA-F]{32}$")]
    public string? TollfreePhoneNumberSid { get; init; }

    /// <summary>
    /// The E.164 formatted toll-free phone number associated with the verification.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tollfree_phone_number")]
    public string? TollfreePhoneNumber { get; init; }

    /// <summary>
    /// The compliance status of the Tollfree Verification record.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status")]
    public TollfreeVerificationEnumStatus? Status { get; init; }

    /// <summary>
    /// The absolute URL of the Tollfree Verification resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    /// <summary>
    /// The rejection reason given when a Tollfree Verification has been rejected.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("rejection_reason")]
    public string? RejectionReason { get; init; }

    /// <summary>
    /// The error code given when a Tollfree Verification has been rejected.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("error_code")]
    public int? ErrorCode { get; init; }

    /// <summary>
    /// The date and time when the ability to edit a rejected verification expires.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("edit_expiration")]
    public DateTimeOffset? EditExpiration { get; init; }

    /// <summary>
    /// If a rejected verification is allowed to be edited/resubmitted. Some rejection reasons allow editing and some do not.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("edit_allowed")]
    public bool? EditAllowed { get; init; }

    /// <summary>
    /// A legally recognized business registration number
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("business_registration_number")]
    public string? BusinessRegistrationNumber { get; init; }

    /// <summary>
    /// The organizational authority for business registrations. Required for all business types except SOLE_PROPRIETOR.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("business_registration_authority")]
    public TollfreeVerificationEnumBusinessRegistrationAuthority? BusinessRegistrationAuthority { get; init; }

    /// <summary>
    /// Country business is registered in
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("business_registration_country")]
    public string? BusinessRegistrationCountry { get; init; }

    /// <summary>
    /// The type of business, valid values are PRIVATE_PROFIT, PUBLIC_PROFIT, NON_PROFIT, SOLE_PROPRIETOR, GOVERNMENT. Required field.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("business_type")]
    public TollfreeVerificationEnumBusinessType? BusinessType { get; init; }

    /// <summary>
    /// The E.164 formatted number associated with the business.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("business_registration_phone_number")]
    public string? BusinessRegistrationPhoneNumber { get; init; }

    /// <summary>
    /// Trade name, sub entity, or downstream business name of business being submitted for verification
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("doing_business_as")]
    public string? DoingBusinessAs { get; init; }

    /// <summary>
    /// The confirmation message sent to users when they opt in to receive messages.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("opt_in_confirmation_message")]
    public string? OptInConfirmationMessage { get; init; }

    /// <summary>
    /// A sample help message provided to users.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("help_message_sample")]
    public string? HelpMessageSample { get; init; }

    /// <summary>
    /// The URL to the privacy policy for the business or organization.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("privacy_policy_url")]
    [Format(FormatKind.Uri)]
    public string? PrivacyPolicyUrl { get; init; }

    /// <summary>
    /// The URL of the terms and conditions for the business or organization.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("terms_and_conditions_url")]
    [Format(FormatKind.Uri)]
    public string? TermsAndConditionsUrl { get; init; }

    /// <summary>
    /// Indicates if the content is age gated.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("age_gated_content")]
    public bool? AgeGatedContent { get; init; }

    /// <summary>
    /// List of keywords that users can send to opt in or out of messages.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("opt_in_keywords")]
    public IReadOnlyList<string?>? OptInKeywords { get; init; }

    /// <summary>
    /// A list of rejection reasons and codes describing why a Tollfree Verification has been rejected.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("rejection_reasons")]
    public IReadOnlyList<object?>? RejectionReasons { get; init; }

    /// <summary>
    /// The URLs of the documents associated with the Tollfree Verification resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("resource_links")]
    public object? ResourceLinks { get; init; }

    /// <summary>
    /// An optional external reference ID supplied by customer and echoed back on status retrieval.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("external_reference_id")]
    public string? ExternalReferenceId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("vetting_id")]
    [MaxLength(500)]
    public string? VettingId { get; init; }

    /// <summary>
    /// The third-party political vetting provider.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("vetting_provider")]
    public TollfreeVerificationEnumVettingProvider? VettingProvider { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("vetting_id_expiration")]
    public DateTimeOffset? VettingIdExpiration { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
