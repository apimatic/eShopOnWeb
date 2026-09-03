using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

/// <summary>
/// (Optional) If passed, the proof of the authorized ACH agreement terms will be persisted.
/// </summary>
public record AchAgreement
{
    /// <summary>
    /// (Required when providing ACH agreement params) The ACH authorization agreement terms.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("agreement_terms")]
    public string? AgreementTerms { get; init; }

    /// <summary>
    /// (Required when providing ACH agreement params) The first name of the person authorizing the ACH agreement.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("authorizer_first_name")]
    public string? AuthorizerFirstName { get; init; }

    /// <summary>
    /// (Required when providing ACH agreement params) The last name of the person authorizing the ACH agreement.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("authorizer_last_name")]
    public string? AuthorizerLastName { get; init; }

    /// <summary>
    /// (Required when providing ACH agreement params) The IP address of the person authorizing the ACH agreement.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ip_address")]
    public string? IpAddress { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
