using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The type of typing event. "START" indicates the agent began typing, "END" indicates the agent stopped typing. Defaults to "START".
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Event>))]
public sealed record Event : StringEnum<Event>
{
    private Event(string value) : base(value)
    {
    }

    public static readonly Event Start = new("START");

    public static readonly Event End = new("END");

    public static Event FromValue(string value) => FromValueCore(value);
}
