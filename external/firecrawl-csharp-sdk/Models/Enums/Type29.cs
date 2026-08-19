using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Type29>))]
public sealed record Type29 : StringEnum<Type29>
{
    private Type29(string value) : base(value)
    {
    }

    public static readonly Type29 Search = new("search");

    public static Type29 FromValue(string value) => FromValueCore(value);
}
