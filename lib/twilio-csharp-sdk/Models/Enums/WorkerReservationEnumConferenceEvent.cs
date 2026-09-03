using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<WorkerReservationEnumConferenceEvent>))]
public sealed record WorkerReservationEnumConferenceEvent : StringEnum<WorkerReservationEnumConferenceEvent>
{
    private WorkerReservationEnumConferenceEvent(string value) : base(value)
    {
    }

    public static readonly WorkerReservationEnumConferenceEvent Start = new("start");

    public static readonly WorkerReservationEnumConferenceEvent End = new("end");

    public static readonly WorkerReservationEnumConferenceEvent Join = new("join");

    public static readonly WorkerReservationEnumConferenceEvent Leave = new("leave");

    public static readonly WorkerReservationEnumConferenceEvent Mute = new("mute");

    public static readonly WorkerReservationEnumConferenceEvent Hold = new("hold");

    public static readonly WorkerReservationEnumConferenceEvent Speaker = new("speaker");

    public static WorkerReservationEnumConferenceEvent FromValue(string value) => FromValueCore(value);
}
