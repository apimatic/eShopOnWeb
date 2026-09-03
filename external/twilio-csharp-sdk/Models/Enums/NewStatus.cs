using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The new status to set for the port in request.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<NewStatus>))]
public sealed record NewStatus : StringEnum<NewStatus>
{
    private NewStatus(string value) : base(value)
    {
    }

    public static readonly NewStatus InReview = new("in_review");

    public static readonly NewStatus WaitingForSignature = new("waiting_for_signature");

    public static readonly NewStatus PortSubmitted = new("port_submitted");

    public static readonly NewStatus PortRejected = new("port_rejected");

    public static readonly NewStatus PortPending = new("port_pending");

    public static readonly NewStatus Canceled = new("canceled");

    public static readonly NewStatus Completed = new("completed");

    public static readonly NewStatus Canceling = new("canceling");

    public static NewStatus FromValue(string value) => FromValueCore(value);
}
