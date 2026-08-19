using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Type6>))]
public sealed record Type6 : StringEnum<Type6>
{
    private Type6(string value) : base(value)
    {
    }

    public static readonly Type6 Images = new("images");

    public static Type6 FromValue(string value) => FromValueCore(value);
}
