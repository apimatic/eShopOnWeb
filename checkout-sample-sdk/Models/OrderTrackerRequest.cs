using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// The tracking details of an order.
/// </summary>
public record OrderTrackerRequest
{
    /// <summary>
    /// The tracking number for the shipment. This property supports Unicode.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tracking_number")]
    [StringLength(64, MinimumLength = 1)]
    public string? TrackingNumber { get; init; }

    /// <summary>
    /// The carrier for the shipment. Some carriers have a global version as well as local subsidiaries. The subsidiaries are repeated over many countries and might also have an entry in the global list. Choose the carrier for your country. If the carrier is not available for your country, choose the global version of the carrier. If your carrier name is not in the list, set <c>carrier</c> to <c>OTHER</c> and set carrier name in <c>carrier_name_other</c>. For allowed values, see Carriers.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("carrier")]
    public ShipmentCarrier? Carrier { get; init; }

    /// <summary>
    /// The name of the carrier for the shipment. Provide this value only if the carrier parameter is OTHER. This property supports Unicode.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("carrier_name_other")]
    [StringLength(64, MinimumLength = 1)]
    public string? CarrierNameOther { get; init; }

    /// <summary>
    /// The PayPal capture ID.
    /// </summary>
    [JsonPropertyName("capture_id")]
    [StringLength(50, MinimumLength = 1)]
    [RegularExpression("^[a-zA-Z0-9]*$")]
    public required string CaptureId { get; init; }

    /// <summary>
    /// If true, PayPal will send an email notification to the payer of the PayPal transaction. The email contains the tracking details provided through the Orders tracking API request. Independent of any value passed for <c>notify_payer</c>, the payer may receive tracking notifications within the PayPal app, based on the user's notification preferences.
    /// </summary>
    [JsonPropertyName("notify_payer")]
    public bool? NotifyPayer { get; init; } = false;

    /// <summary>
    /// An array of details of items in the shipment.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("items")]
    public IReadOnlyList<OrderTrackerItem>? Items { get; init; }
}
