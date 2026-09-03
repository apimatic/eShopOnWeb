using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The inbound resource status of the Interaction. Will always be <c>delivered</c> for messages and <c>in-progress</c> for calls.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<InteractionEnumResourceStatus>))]
public sealed record InteractionEnumResourceStatus : StringEnum<InteractionEnumResourceStatus>
{
    private InteractionEnumResourceStatus(string value) : base(value)
    {
    }

    public static readonly InteractionEnumResourceStatus Accepted = new("accepted");

    public static readonly InteractionEnumResourceStatus Answered = new("answered");

    public static readonly InteractionEnumResourceStatus Busy = new("busy");

    public static readonly InteractionEnumResourceStatus Canceled = new("canceled");

    public static readonly InteractionEnumResourceStatus Completed = new("completed");

    public static readonly InteractionEnumResourceStatus Deleted = new("deleted");

    public static readonly InteractionEnumResourceStatus Delivered = new("delivered");

    public static readonly InteractionEnumResourceStatus DeliveryUnknown = new("delivery-unknown");

    public static readonly InteractionEnumResourceStatus Failed = new("failed");

    public static readonly InteractionEnumResourceStatus InProgress = new("in-progress");

    public static readonly InteractionEnumResourceStatus Initiated = new("initiated");

    public static readonly InteractionEnumResourceStatus NoAnswer = new("no-answer");

    public static readonly InteractionEnumResourceStatus Queued = new("queued");

    public static readonly InteractionEnumResourceStatus Received = new("received");

    public static readonly InteractionEnumResourceStatus Receiving = new("receiving");

    public static readonly InteractionEnumResourceStatus Ringing = new("ringing");

    public static readonly InteractionEnumResourceStatus Scheduled = new("scheduled");

    public static readonly InteractionEnumResourceStatus Sending = new("sending");

    public static readonly InteractionEnumResourceStatus Sent = new("sent");

    public static readonly InteractionEnumResourceStatus Undelivered = new("undelivered");

    public static readonly InteractionEnumResourceStatus Unknown = new("unknown");

    public static InteractionEnumResourceStatus FromValue(string value) => FromValueCore(value);
}
