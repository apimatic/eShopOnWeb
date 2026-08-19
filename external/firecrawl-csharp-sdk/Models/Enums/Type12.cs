using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Type12>))]
public sealed record Type12 : StringEnum<Type12>
{
    private Type12(string value) : base(value)
    {
    }

    public static readonly Type12 Menu = new("menu");

    public static Type12 FromValue(string value) => FromValueCore(value);
}
