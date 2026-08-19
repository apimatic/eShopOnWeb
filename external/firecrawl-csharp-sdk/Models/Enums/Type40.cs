using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Type40>))]
public sealed record Type40 : StringEnum<Type40>
{
    private Type40(string value) : base(value)
    {
    }

    public static readonly Type40 Web = new("web");

    public static Type40 FromValue(string value) => FromValueCore(value);
}
