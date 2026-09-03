using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// Always empty for created Message Interactions.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<MessageInteractionEnumResourceStatus>))]
public sealed record MessageInteractionEnumResourceStatus : StringEnum<MessageInteractionEnumResourceStatus>
{
    private MessageInteractionEnumResourceStatus(string value) : base(value)
    {
    }

    public static readonly MessageInteractionEnumResourceStatus Accepted = new("accepted");

    public static readonly MessageInteractionEnumResourceStatus Answered = new("answered");

    public static readonly MessageInteractionEnumResourceStatus Busy = new("busy");

    public static readonly MessageInteractionEnumResourceStatus Canceled = new("canceled");

    public static readonly MessageInteractionEnumResourceStatus Completed = new("completed");

    public static readonly MessageInteractionEnumResourceStatus Deleted = new("deleted");

    public static readonly MessageInteractionEnumResourceStatus Delivered = new("delivered");

    public static readonly MessageInteractionEnumResourceStatus DeliveryUnknown = new("delivery-unknown");

    public static readonly MessageInteractionEnumResourceStatus Failed = new("failed");

    public static readonly MessageInteractionEnumResourceStatus InProgress = new("in-progress");

    public static readonly MessageInteractionEnumResourceStatus Initiated = new("initiated");

    public static readonly MessageInteractionEnumResourceStatus NoAnswer = new("no-answer");

    public static readonly MessageInteractionEnumResourceStatus Queued = new("queued");

    public static readonly MessageInteractionEnumResourceStatus Received = new("received");

    public static readonly MessageInteractionEnumResourceStatus Receiving = new("receiving");

    public static readonly MessageInteractionEnumResourceStatus Ringing = new("ringing");

    public static readonly MessageInteractionEnumResourceStatus Scheduled = new("scheduled");

    public static readonly MessageInteractionEnumResourceStatus Sending = new("sending");

    public static readonly MessageInteractionEnumResourceStatus Sent = new("sent");

    public static readonly MessageInteractionEnumResourceStatus Undelivered = new("undelivered");

    public static readonly MessageInteractionEnumResourceStatus Unknown = new("unknown");

    public static MessageInteractionEnumResourceStatus FromValue(string value) => FromValueCore(value);
}
