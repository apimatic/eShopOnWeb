using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

/// <summary>
/// Required when creating a subscription with Maxio Payments.
/// </summary>
public record AgreementAcceptance
{
    /// <summary>
    /// Required when providing agreement acceptance params.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ip_address")]
    public string? IpAddress { get; init; }

    /// <summary>
    /// Required when creating a subscription with Maxio Payments. Either terms_url or privacy_policy_url is required when providing agreement_acceptance params.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("terms_url")]
    public string? TermsUrl { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("privacy_policy_url")]
    public string? PrivacyPolicyUrl { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("return_refund_policy_url")]
    public string? ReturnRefundPolicyUrl { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("delivery_policy_url")]
    public string? DeliveryPolicyUrl { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("secure_checkout_policy_url")]
    public string? SecureCheckoutPolicyUrl { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
