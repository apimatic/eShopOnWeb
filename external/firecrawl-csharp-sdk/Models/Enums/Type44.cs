using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Type44>))]
public sealed record Type44 : StringEnum<Type44>
{
    private Type44(string value) : base(value)
    {
    }

    public static readonly Type44 Research = new("research");

    public static Type44 FromValue(string value) => FromValueCore(value);
}
