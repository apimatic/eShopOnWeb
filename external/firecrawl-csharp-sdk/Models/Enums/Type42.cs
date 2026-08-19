using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Type42>))]
public sealed record Type42 : StringEnum<Type42>
{
    private Type42(string value) : base(value)
    {
    }

    public static readonly Type42 News = new("news");

    public static Type42 FromValue(string value) => FromValueCore(value);
}
