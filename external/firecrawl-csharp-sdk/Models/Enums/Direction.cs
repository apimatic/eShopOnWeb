using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// Direction to scroll
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Direction>))]
public sealed record Direction : StringEnum<Direction>
{
    private Direction(string value) : base(value)
    {
    }

    public static readonly Direction Up = new("up");

    public static readonly Direction Down = new("down");

    public static Direction FromValue(string value) => FromValueCore(value);
}
