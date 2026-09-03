using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<CallEnumEvent>))]
public sealed record CallEnumEvent : StringEnum<CallEnumEvent>
{
    private CallEnumEvent(string value) : base(value)
    {
    }

    public static readonly CallEnumEvent Initiated = new("initiated");

    public static readonly CallEnumEvent Ringing = new("ringing");

    public static readonly CallEnumEvent Answered = new("answered");

    public static readonly CallEnumEvent Completed = new("completed");

    public static CallEnumEvent FromValue(string value) => FromValueCore(value);
}
