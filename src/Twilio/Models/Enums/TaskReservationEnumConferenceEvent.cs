using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<TaskReservationEnumConferenceEvent>))]
public sealed record TaskReservationEnumConferenceEvent : StringEnum<TaskReservationEnumConferenceEvent>
{
    private TaskReservationEnumConferenceEvent(string value) : base(value)
    {
    }

    public static readonly TaskReservationEnumConferenceEvent Start = new("start");

    public static readonly TaskReservationEnumConferenceEvent End = new("end");

    public static readonly TaskReservationEnumConferenceEvent Join = new("join");

    public static readonly TaskReservationEnumConferenceEvent Leave = new("leave");

    public static readonly TaskReservationEnumConferenceEvent Mute = new("mute");

    public static readonly TaskReservationEnumConferenceEvent Hold = new("hold");

    public static readonly TaskReservationEnumConferenceEvent Speaker = new("speaker");

    public static TaskReservationEnumConferenceEvent FromValue(string value) => FromValueCore(value);
}
