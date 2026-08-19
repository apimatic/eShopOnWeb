using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// Press a key on the page
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Type23>))]
public sealed record Type23 : StringEnum<Type23>
{
    private Type23(string value) : base(value)
    {
    }

    public static readonly Type23 Press = new("press");

    public static Type23 FromValue(string value) => FromValueCore(value);
}
