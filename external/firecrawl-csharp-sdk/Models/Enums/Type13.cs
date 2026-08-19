using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Type13>))]
public sealed record Type13 : StringEnum<Type13>
{
    private Type13(string value) : base(value)
    {
    }

    public static readonly Type13 Audio = new("audio");

    public static Type13 FromValue(string value) => FromValueCore(value);
}
