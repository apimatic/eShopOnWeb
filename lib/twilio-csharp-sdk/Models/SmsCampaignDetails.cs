using System.Collections.Generic;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Models.Enums;

namespace Twilio.Models;

/// <summary>
/// SMS campaign details for the application.
/// </summary>
public record SmsCampaignDetails
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("campaign_name")]
    public string? CampaignName { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("campaign_brand_website")]
    public string? CampaignBrandWebsite { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("customer_care_channel")]
    public CustomerCareChannel? CustomerCareChannel { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("customer_care_value")]
    public string? CustomerCareValue { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("campaign_frequency")]
    public IReadOnlyList<CampaignFrequency>? CampaignFrequency { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sc_use_case_categories")]
    public IReadOnlyList<TollfreeVerificationEnumUseCaseCategory>? ScUseCaseCategories { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sms_terms_of_service_url")]
    public string? SmsTermsOfServiceUrl { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sms_privacy_policy_url")]
    public string? SmsPrivacyPolicyUrl { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("monthly_outbound_volume_expected")]
    public string? MonthlyOutboundVolumeExpected { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("monthly_inbound_volume_expected")]
    public string? MonthlyInboundVolumeExpected { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("avg_monthly_messages_sent_to_each_subscriber")]
    public string? AvgMonthlyMessagesSentToEachSubscriber { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("avg_monthly_messages_received_from_subscribers")]
    public string? AvgMonthlyMessagesReceivedFromSubscribers { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("estimated_total_subscribers")]
    public string? EstimatedTotalSubscribers { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("duration_of_the_campaign")]
    public string? DurationOfTheCampaign { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("planned_traffic_spikes")]
    public string? PlannedTrafficSpikes { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("spike_details")]
    public string? SpikeDetails { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("expected_traffic_start_date")]
    public string? ExpectedTrafficStartDate { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
