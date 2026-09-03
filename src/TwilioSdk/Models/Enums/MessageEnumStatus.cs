using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The status of the Message. Possible values: <c>accepted</c>, <c>scheduled</c>, <c>canceled</c>, <c>queued</c>, <c>sending</c>, <c>sent</c>, <c>failed</c>, <c>delivered</c>, <c>undelivered</c>, <c>receiving</c>, <c>received</c>, or <c>read</c> (WhatsApp only). For more information, See <see href="https://www.twilio.com/docs/sms/api/message-resource#message-status-values">detailed descriptions</see>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<MessageEnumStatus>))]
public sealed record MessageEnumStatus : StringEnum<MessageEnumStatus>
{
    private MessageEnumStatus(string value) : base(value)
    {
    }

    public static readonly MessageEnumStatus Queued = new("queued");

    public static readonly MessageEnumStatus Sending = new("sending");

    public static readonly MessageEnumStatus Sent = new("sent");

    public static readonly MessageEnumStatus Failed = new("failed");

    public static readonly MessageEnumStatus Delivered = new("delivered");

    public static readonly MessageEnumStatus Undelivered = new("undelivered");

    public static readonly MessageEnumStatus Receiving = new("receiving");

    public static readonly MessageEnumStatus Received = new("received");

    public static readonly MessageEnumStatus Accepted = new("accepted");

    public static readonly MessageEnumStatus Scheduled = new("scheduled");

    public static readonly MessageEnumStatus Read = new("read");

    public static readonly MessageEnumStatus PartiallyDelivered = new("partially_delivered");

    public static readonly MessageEnumStatus Canceled = new("canceled");

    public static MessageEnumStatus FromValue(string value) => FromValueCore(value);
}
