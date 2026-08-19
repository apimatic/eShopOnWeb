using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Type14>))]
public sealed record Type14 : StringEnum<Type14>
{
    private Type14(string value) : base(value)
    {
    }

    public static readonly Type14 Video = new("video");

    public static Type14 FromValue(string value) => FromValueCore(value);
}
