using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// Set to <c>only</c> to limit the search to indexed agent-skill files.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Skills1>))]
public sealed record Skills1 : StringEnum<Skills1>
{
    private Skills1(string value) : base(value)
    {
    }

    public static readonly Skills1 Only = new("only");

    public static Skills1 FromValue(string value) => FromValueCore(value);
}
