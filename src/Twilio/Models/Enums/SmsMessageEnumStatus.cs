using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<SmsMessageEnumStatus>))]
public sealed record SmsMessageEnumStatus : StringEnum<SmsMessageEnumStatus>
{
    private SmsMessageEnumStatus(string value) : base(value)
    {
    }

    public static readonly SmsMessageEnumStatus Queued = new("queued");

    public static readonly SmsMessageEnumStatus Sending = new("sending");

    public static readonly SmsMessageEnumStatus Sent = new("sent");

    public static readonly SmsMessageEnumStatus Failed = new("failed");

    public static readonly SmsMessageEnumStatus Delivered = new("delivered");

    public static readonly SmsMessageEnumStatus Undelivered = new("undelivered");

    public static readonly SmsMessageEnumStatus Receiving = new("receiving");

    public static readonly SmsMessageEnumStatus Received = new("received");

    public static readonly SmsMessageEnumStatus Accepted = new("accepted");

    public static readonly SmsMessageEnumStatus Scheduled = new("scheduled");

    public static readonly SmsMessageEnumStatus Read = new("read");

    public static readonly SmsMessageEnumStatus PartiallyDelivered = new("partially_delivered");

    public static readonly SmsMessageEnumStatus Canceled = new("canceled");

    public static SmsMessageEnumStatus FromValue(string value) => FromValueCore(value);
}
