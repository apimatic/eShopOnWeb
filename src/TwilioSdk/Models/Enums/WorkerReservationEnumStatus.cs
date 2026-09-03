using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The current status of the reservation. Can be: <c>pending</c>, <c>accepted</c>, <c>rejected</c>, <c>timeout</c>, <c>canceled</c>, or <c>rescinded</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<WorkerReservationEnumStatus>))]
public sealed record WorkerReservationEnumStatus : StringEnum<WorkerReservationEnumStatus>
{
    private WorkerReservationEnumStatus(string value) : base(value)
    {
    }

    public static readonly WorkerReservationEnumStatus Pending = new("pending");

    public static readonly WorkerReservationEnumStatus Accepted = new("accepted");

    public static readonly WorkerReservationEnumStatus Rejected = new("rejected");

    public static readonly WorkerReservationEnumStatus Timeout = new("timeout");

    public static readonly WorkerReservationEnumStatus Canceled = new("canceled");

    public static readonly WorkerReservationEnumStatus Rescinded = new("rescinded");

    public static readonly WorkerReservationEnumStatus Wrapping = new("wrapping");

    public static readonly WorkerReservationEnumStatus Completed = new("completed");

    public static WorkerReservationEnumStatus FromValue(string value) => FromValueCore(value);
}
