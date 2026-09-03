using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<EventEnumLevel>))]
public sealed record EventEnumLevel : StringEnum<EventEnumLevel>
{
    private EventEnumLevel(string value) : base(value)
    {
    }

    public static readonly EventEnumLevel Unknown = new("UNKNOWN");

    public static readonly EventEnumLevel Debug = new("DEBUG");

    public static readonly EventEnumLevel Info = new("INFO");

    public static readonly EventEnumLevel Warning = new("WARNING");

    public static readonly EventEnumLevel Error = new("ERROR");

    public static EventEnumLevel FromValue(string value) => FromValueCore(value);
}
