using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Type7>))]
public sealed record Type7 : StringEnum<Type7>
{
    private Type7(string value) : base(value)
    {
    }

    public static readonly Type7 Screenshot = new("screenshot");

    public static Type7 FromValue(string value) => FromValueCore(value);
}
