using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<TaskReservationEnumSupervisorMode>))]
public sealed record TaskReservationEnumSupervisorMode : StringEnum<TaskReservationEnumSupervisorMode>
{
    private TaskReservationEnumSupervisorMode(string value) : base(value)
    {
    }

    public static readonly TaskReservationEnumSupervisorMode Monitor = new("monitor");

    public static readonly TaskReservationEnumSupervisorMode Whisper = new("whisper");

    public static readonly TaskReservationEnumSupervisorMode Barge = new("barge");

    public static TaskReservationEnumSupervisorMode FromValue(string value) => FromValueCore(value);
}
