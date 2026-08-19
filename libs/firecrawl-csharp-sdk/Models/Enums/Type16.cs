using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Type16>))]
public sealed record Type16 : StringEnum<Type16>
{
    private Type16(string value) : base(value)
    {
    }

    public static readonly Type16 Highlights = new("highlights");

    public static Type16 FromValue(string value) => FromValueCore(value);
}
