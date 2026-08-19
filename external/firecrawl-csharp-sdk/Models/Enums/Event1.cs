using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Event1>))]
public sealed record Event1 : StringEnum<Event1>
{
    private Event1(string value) : base(value)
    {
    }

    public static readonly Event1 Completed = new("completed");

    public static readonly Event1 Page = new("page");

    public static readonly Event1 Failed = new("failed");

    public static readonly Event1 Started = new("started");

    public static Event1 FromValue(string value) => FromValueCore(value);
}
