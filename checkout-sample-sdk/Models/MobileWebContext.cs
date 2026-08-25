using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// Buyer's mobile web browser context to app switch to the PayPal consumer app.
/// </summary>
public record MobileWebContext
{
    /// <summary>
    /// Merchant preference on how the buyer can navigate back to merchant website post approving the transaction on the PayPal App.
    /// </summary>
    [JsonPropertyName("return_flow")]
    public MobileReturnFlow? ReturnFlow { get; init; } = MobileReturnFlow.Auto;

    /// <summary>
    /// User agent from the request originating from the buyer's device. This will be used to identify the buyer's operating system and browser versions. NOTE: Merchants must not alter or modify the buyer's device user agent.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("buyer_user_agent")]
    [StringLength(512, MinimumLength = 1)]
    [RegularExpression("^.*$")]
    public string? BuyerUserAgent { get; init; }
}
