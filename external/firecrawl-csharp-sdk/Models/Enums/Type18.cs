using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// Wait for a specified amount of milliseconds
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Type18>))]
public sealed record Type18 : StringEnum<Type18>
{
    private Type18(string value) : base(value)
    {
    }

    public static readonly Type18 Wait = new("wait");

    public static Type18 FromValue(string value) => FromValueCore(value);
}
