using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Type43>))]
public sealed record Type43 : StringEnum<Type43>
{
    private Type43(string value) : base(value)
    {
    }

    public static readonly Type43 Github = new("github");

    public static Type43 FromValue(string value) => FromValueCore(value);
}
