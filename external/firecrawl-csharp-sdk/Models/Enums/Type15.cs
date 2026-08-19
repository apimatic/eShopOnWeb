using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Type15>))]
public sealed record Type15 : StringEnum<Type15>
{
    private Type15(string value) : base(value)
    {
    }

    public static readonly Type15 Question = new("question");

    public static Type15 FromValue(string value) => FromValueCore(value);
}
