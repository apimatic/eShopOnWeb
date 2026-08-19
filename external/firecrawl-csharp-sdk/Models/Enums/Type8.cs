using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Type8>))]
public sealed record Type8 : StringEnum<Type8>
{
    private Type8(string value) : base(value)
    {
    }

    public static readonly Type8 Json = new("json");

    public static Type8 FromValue(string value) => FromValueCore(value);
}
