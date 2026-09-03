using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// Delivery status of the Communication to this recipient.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<DeliveryStatus>))]
public sealed record DeliveryStatus : StringEnum<DeliveryStatus>
{
    private DeliveryStatus(string value) : base(value)
    {
    }

    public static readonly DeliveryStatus Initiated = new("INITIATED");

    public static readonly DeliveryStatus InProgress = new("IN_PROGRESS");

    public static readonly DeliveryStatus Delivered = new("DELIVERED");

    public static readonly DeliveryStatus Completed = new("COMPLETED");

    public static readonly DeliveryStatus Failed = new("FAILED");

    public static DeliveryStatus FromValue(string value) => FromValueCore(value);
}
