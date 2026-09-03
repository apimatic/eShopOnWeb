using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The message delivery status, can be <c>read</c>, <c>failed</c>, <c>delivered</c>, <c>undelivered</c>, <c>sent</c> or null.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ConversationMessageReceiptEnumDeliveryStatus>))]
public sealed record ConversationMessageReceiptEnumDeliveryStatus : StringEnum<ConversationMessageReceiptEnumDeliveryStatus>
{
    private ConversationMessageReceiptEnumDeliveryStatus(string value) : base(value)
    {
    }

    public static readonly ConversationMessageReceiptEnumDeliveryStatus Read = new("read");

    public static readonly ConversationMessageReceiptEnumDeliveryStatus Failed = new("failed");

    public static readonly ConversationMessageReceiptEnumDeliveryStatus Delivered = new("delivered");

    public static readonly ConversationMessageReceiptEnumDeliveryStatus Undelivered = new("undelivered");

    public static readonly ConversationMessageReceiptEnumDeliveryStatus Sent = new("sent");

    public static ConversationMessageReceiptEnumDeliveryStatus FromValue(string value) => FromValueCore(value);
}
