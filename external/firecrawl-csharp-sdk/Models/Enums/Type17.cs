using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Type17>))]
public sealed record Type17 : StringEnum<Type17>
{
    private Type17(string value) : base(value)
    {
    }

    public static readonly Type17 Pdf = new("pdf");

    public static Type17 FromValue(string value) => FromValueCore(value);
}
