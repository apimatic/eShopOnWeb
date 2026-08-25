using System.Text.Json.Serialization;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// The processor response information for payment requests, such as direct credit card transactions.
/// </summary>
public record ProcessorResponse
{
    /// <summary>
    /// The address verification code for Visa, Discover, Mastercard, or American Express transactions.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("avs_code")]
    public AvsCode? AvsCode { get; init; }

    /// <summary>
    /// The card verification value code for for Visa, Discover, Mastercard, or American Express.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cvv_code")]
    public CvvCode? CvvCode { get; init; }

    /// <summary>
    /// Processor response code for the non-PayPal payment processor errors.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("response_code")]
    public ProcessorResponseCode? ResponseCode { get; init; }

    /// <summary>
    /// The declined payment transactions might have payment advice codes. The card networks, like Visa and Mastercard, return payment advice codes.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payment_advice_code")]
    public PaymentAdviceCode? PaymentAdviceCode { get; init; }
}
