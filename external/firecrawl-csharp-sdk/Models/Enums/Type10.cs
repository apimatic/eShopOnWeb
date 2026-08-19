using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Type10>))]
public sealed record Type10 : StringEnum<Type10>
{
    private Type10(string value) : base(value)
    {
    }

    public static readonly Type10 Branding = new("branding");

    public static Type10 FromValue(string value) => FromValueCore(value);
}
