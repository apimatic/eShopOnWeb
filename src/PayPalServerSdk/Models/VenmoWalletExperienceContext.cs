using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// Customizes the buyer experience during the approval process for payment with Venmo. Note: Partners and Marketplaces might configure shipping_preference during partner account setup, which overrides the request values.
/// </summary>
public record VenmoWalletExperienceContext
{
    /// <summary>
    /// The business name of the merchant. The pattern is defined by an external party and supports Unicode.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("brand_name")]
    [StringLength(127, MinimumLength = 1)]
    [RegularExpression("^.*$")]
    public string? BrandName { get; init; }

    /// <summary>
    /// The location from which the shipping address is derived.
    /// </summary>
    [JsonPropertyName("shipping_preference")]
    public VenmoWalletExperienceContextShippingPreference? ShippingPreference { get; init; } = VenmoWalletExperienceContextShippingPreference.GetFromFile;

    /// <summary>
    /// CallBack Configuration that the merchant can provide to PayPal/Venmo.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("order_update_callback_config")]
    public CallbackConfiguration? OrderUpdateCallbackConfig { get; init; }

    /// <summary>
    /// Configures a Continue or Pay Now checkout flow.
    /// </summary>
    [JsonPropertyName("user_action")]
    public VenmoWalletExperienceContextUserAction? UserAction { get; init; } = VenmoWalletExperienceContextUserAction.Continue;

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
