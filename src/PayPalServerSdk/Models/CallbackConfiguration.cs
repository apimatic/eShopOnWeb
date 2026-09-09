using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Core.Validation;
using PayPalServerSdk.Core.Validation.Attributes;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// CallBack Configuration that the merchant can provide to PayPal/Venmo.
/// </summary>
public record CallbackConfiguration
{
    /// <summary>
    /// An array of callback events merchant can subscribe to for the corresponding callback url.
    /// </summary>
    [JsonPropertyName("callback_events")]
    [MinLength(1)]
    [MaxLength(5)]
    [UniqueItems]
    public required IReadOnlyList<CallbackEvents> CallbackEvents { get; init; }

    /// <summary>
    /// Merchant provided CallBack url.PayPal/Venmo will use this url to call the merchant back when the events occur .PayPal/Venmo expects a secured url usually in the https format.merchant can append the cart id or other params part of the url as query or path params.
    /// </summary>
    [JsonPropertyName("callback_url")]
    [StringLength(2040, MinimumLength = 10)]
    [RegularExpression("^.*$")]
    [Format(FormatKind.Uri)]
    public required string CallbackUrl { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
