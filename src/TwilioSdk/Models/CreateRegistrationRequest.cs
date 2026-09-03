using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record CreateRegistrationRequest
{
    /// <summary>
    /// Global HQ country code (ISO 3166-1 alpha-2)
    /// </summary>
    [JsonPropertyName("global_hq_country")]
    [StringLength(2, MinimumLength = 2)]
    public required string GlobalHqCountry { get; init; }

    /// <summary>
    /// Target country for sender ID registration
    /// </summary>
    [JsonPropertyName("target_country")]
    [StringLength(2, MinimumLength = 2)]
    public required string TargetCountry { get; init; }

    /// <summary>
    /// Purpose of SMS messages
    /// </summary>
    [JsonPropertyName("message_purpose")]
    public required MessagePurpose MessagePurpose { get; init; }

    /// <summary>
    /// Requested alphanumeric sender ID value
    /// </summary>
    [JsonPropertyName("sender_id")]
    public required string SenderId { get; init; }

    /// <summary>
    /// Business customer type
    /// </summary>
    [JsonPropertyName("business_identity")]
    public required BusinessIdentity BusinessIdentity { get; init; }

    /// <summary>
    /// Whether sender ID will be subassigned to other accounts
    /// </summary>
    [JsonPropertyName("is_subassigned")]
    public required bool IsSubassigned { get; init; }

    /// <summary>
    /// Human-readable name for the registration
    /// </summary>
    [JsonPropertyName("friendly_name")]
    public required string FriendlyName { get; init; }

    /// <summary>
    /// Bundle SID of customer's profile
    /// </summary>
    [JsonPropertyName("customer_profile_bundle_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^BU[0-9a-fA-F]{32}$")]
    public required string CustomerProfileBundleSid { get; init; }

    /// <summary>
    /// ISV opt-in consent flag. Defaults to true if not provided. Only rejected when explicitly set to false for ISV customers registering in Australia.
    /// </summary>
    [JsonPropertyName("isv_opt_in_consent")]
    public bool? IsvOptInConsent { get; init; } = true;

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
