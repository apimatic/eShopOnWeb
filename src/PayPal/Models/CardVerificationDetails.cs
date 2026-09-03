using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPal.Core.Models;
using PayPal.Models.Enums;

namespace PayPal.Models;

/// <summary>
/// Card Verification details including the authorization details and 3D SECURE details.
/// </summary>
public record CardVerificationDetails
{
    /// <summary>
    /// DEPRECATED. This field is DEPRECATED. Please find the network transaction id data in the 'id' field under the 'network_transaction_reference' object instead of the 'verification' object.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("network_transaction_id")]
    [StringLength(1024, MinimumLength = 1)]
    [RegularExpression("^[a-zA-Z0-9-_@.:&+=*^'~#!$%()]+$")]
    public string? NetworkTransactionId { get; init; }

    /// <summary>
    /// DEPRECATED. This field is DEPRECATED. Please find the date data in the 'date' field under the 'network_transaction_reference' object instead of the 'verification' object.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date")]
    [StringLength(4, MinimumLength = 4)]
    [RegularExpression("^[0-9]+$")]
    public string? Date { get; init; }

    /// <summary>
    /// DEPRECATED. This field is DEPRECATED. Please find the network data in the 'network' field under the 'network_transaction_reference' object instead of the 'verification' object.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("network")]
    public CardBrand? Network { get; init; }

    /// <summary>
    /// DEPRECATED. This field is DEPRECATED. Please find the time data in the 'time' field under the 'network_transaction_reference' object instead of the 'verification' object.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("time")]
    [StringLength(64, MinimumLength = 20)]
    [RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])-(0[1-9]|[1-2][0-9]|3[0-1])[T,t]([0-1][0-9]|2[0-3]):[0-5][0-9]:([0-5][0-9]|60)([.][0-9]+)?([Zz]|[+-][0-9]{2}:[0-9]{2})$")]
    public string? Time { get; init; }

    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("amount")]
    public Money? Amount { get; init; }

    /// <summary>
    /// The processor response information for payment requests, such as direct credit card transactions.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("processor_response")]
    public CardVerificationProcessorResponse? ProcessorResponse { get; init; }

    /// <summary>
    /// DEPRECATED. This field is DEPRECATED. Please find the 3D secure authentication data in the 'three_d_secure' object under the 'authentication_result' object instead of the 'verification' object.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("three_d_secure")]
    public object? ThreeDSecure { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
