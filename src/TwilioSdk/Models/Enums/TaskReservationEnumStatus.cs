using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The current status of the reservation. Can be: <c>pending</c>, <c>accepted</c>, <c>rejected</c>, or <c>timeout</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<TaskReservationEnumStatus>))]
public sealed record TaskReservationEnumStatus : StringEnum<TaskReservationEnumStatus>
{
    private TaskReservationEnumStatus(string value) : base(value)
    {
    }

    public static readonly TaskReservationEnumStatus Pending = new("pending");

    public static readonly TaskReservationEnumStatus Accepted = new("accepted");

    public static readonly TaskReservationEnumStatus Rejected = new("rejected");

    public static readonly TaskReservationEnumStatus Timeout = new("timeout");

    public static readonly TaskReservationEnumStatus Canceled = new("canceled");

    public static readonly TaskReservationEnumStatus Rescinded = new("rescinded");

    public static readonly TaskReservationEnumStatus Wrapping = new("wrapping");

    public static readonly TaskReservationEnumStatus Completed = new("completed");

    public static TaskReservationEnumStatus FromValue(string value) => FromValueCore(value);
}
