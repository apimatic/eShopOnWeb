using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record MessagingV1BrandRegistrations
{
    /// <summary>
    /// The unique string to identify Brand Registration.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^BN[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Brand Registration resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// A2P Messaging Profile Bundle BundleSid.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("customer_profile_bundle_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^BU[0-9a-fA-F]{32}$")]
    public string? CustomerProfileBundleSid { get; init; }

    /// <summary>
    /// A2P Messaging Profile Bundle BundleSid.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("a2p_profile_bundle_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^BU[0-9a-fA-F]{32}$")]
    public string? A2PProfileBundleSid { get; init; }

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
    /// Type of brand. One of: "STANDARD", "SOLE_PROPRIETOR". SOLE_PROPRIETOR is for the low volume, SOLE_PROPRIETOR campaign use case. There can only be one SOLE_PROPRIETOR campaign created per SOLE_PROPRIETOR brand. STANDARD is for all other campaign use cases. Multiple campaign use cases can be created per STANDARD brand.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("brand_type")]
    public string? BrandType { get; init; }

    /// <summary>
    /// Brand Registration status. One of "PENDING", "APPROVED", "FAILED", "IN_REVIEW", "DELETION_PENDING", "DELETION_FAILED", "SUSPENDED".
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status")]
    public BrandRegistrationsEnumStatus? Status { get; init; }

    /// <summary>
    /// Campaign Registry (TCR) Brand ID. Assigned only after successful brand registration.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tcr_id")]
    public string? TcrId { get; init; }

    /// <summary>
    /// DEPRECATED. A reason why brand registration has failed. Only applicable when status is FAILED.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("failure_reason")]
    public string? FailureReason { get; init; }

    /// <summary>
    /// A list of errors that occurred during the brand registration process.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("errors")]
    public IReadOnlyList<object?>? Errors { get; init; }

    /// <summary>
    /// The absolute URL of the Brand Registration resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    /// <summary>
    /// The secondary vetting score if it was done. Otherwise, it will be the brand score if it's returned from TCR. It may be null if no score is available.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("brand_score")]
    public int? BrandScore { get; init; }

    /// <summary>
    /// DEPRECATED. Feedback on how to improve brand score
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("brand_feedback")]
    public IReadOnlyList<BrandRegistrationsEnumBrandFeedback?>? BrandFeedback { get; init; }

    /// <summary>
    /// When a brand is registered, TCR will attempt to verify the identity of the brand based on the supplied information.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("identity_status")]
    public BrandRegistrationsEnumIdentityStatus? IdentityStatus { get; init; }

    /// <summary>
    /// Publicly traded company identified in the Russell 3000 Index
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("russell_3000")]
    public bool? Russell3000 { get; init; }

    /// <summary>
    /// Identified as a government entity
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("government_entity")]
    public bool? GovernmentEntity { get; init; }

    /// <summary>
    /// Nonprofit organization tax-exempt status per section 501 of the U.S. tax code.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tax_exempt_status")]
    public string? TaxExemptStatus { get; init; }

    /// <summary>
    /// A flag to disable automatic secondary vetting for brands which it would otherwise be done.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("skip_automatic_sec_vet")]
    public bool? SkipAutomaticSecVet { get; init; }

    /// <summary>
    /// A boolean that specifies whether brand should be a mock or not. If true, brand will be registered as a mock brand. Defaults to false if no value is provided.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("mock")]
    public bool? Mock { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("links")]
    public object? Links { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
