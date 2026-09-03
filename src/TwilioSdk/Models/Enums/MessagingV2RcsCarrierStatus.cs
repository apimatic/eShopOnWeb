using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The carrier-level status.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<MessagingV2RcsCarrierStatus>))]
public sealed record MessagingV2RcsCarrierStatus : StringEnum<MessagingV2RcsCarrierStatus>
{
    private MessagingV2RcsCarrierStatus(string value) : base(value)
    {
    }

    public static readonly MessagingV2RcsCarrierStatus Unknown = new("UNKNOWN");

    public static readonly MessagingV2RcsCarrierStatus Unlaunched = new("UNLAUNCHED");

    public static readonly MessagingV2RcsCarrierStatus CarrierReview = new("CARRIER_REVIEW");

    public static readonly MessagingV2RcsCarrierStatus Approved = new("APPROVED");

    public static readonly MessagingV2RcsCarrierStatus Rejected = new("REJECTED");

    public static readonly MessagingV2RcsCarrierStatus Suspended = new("SUSPENDED");

    public static MessagingV2RcsCarrierStatus FromValue(string value) => FromValueCore(value);
}
