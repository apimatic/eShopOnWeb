using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Type2>))]
public sealed record Type2 : StringEnum<Type2>
{
    private Type2(string value) : base(value)
    {
    }

    public static readonly Type2 Summary = new("summary");

    public static Type2 FromValue(string value) => FromValueCore(value);
}
