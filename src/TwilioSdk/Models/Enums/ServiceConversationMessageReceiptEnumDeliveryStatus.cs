using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The message delivery status, can be <c>read</c>, <c>failed</c>, <c>delivered</c>, <c>undelivered</c>, <c>sent</c> or null.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ServiceConversationMessageReceiptEnumDeliveryStatus>))]
public sealed record ServiceConversationMessageReceiptEnumDeliveryStatus : StringEnum<ServiceConversationMessageReceiptEnumDeliveryStatus>
{
    private ServiceConversationMessageReceiptEnumDeliveryStatus(string value) : base(value)
    {
    }

    public static readonly ServiceConversationMessageReceiptEnumDeliveryStatus Read = new("read");

    public static readonly ServiceConversationMessageReceiptEnumDeliveryStatus Failed = new("failed");

    public static readonly ServiceConversationMessageReceiptEnumDeliveryStatus Delivered = new("delivered");

    public static readonly ServiceConversationMessageReceiptEnumDeliveryStatus Undelivered = new("undelivered");

    public static readonly ServiceConversationMessageReceiptEnumDeliveryStatus Sent = new("sent");

    public static ServiceConversationMessageReceiptEnumDeliveryStatus FromValue(string value) =>
        FromValueCore(value);
}
