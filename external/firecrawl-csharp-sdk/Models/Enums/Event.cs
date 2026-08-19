using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Event>))]
public sealed record Event : StringEnum<Event>
{
    private Event(string value) : base(value)
    {
    }

    public static readonly Event MonitorPage = new("monitor.page");

    public static readonly Event MonitorCheckCompleted = new("monitor.check.completed");

    public static Event FromValue(string value) => FromValueCore(value);
}
