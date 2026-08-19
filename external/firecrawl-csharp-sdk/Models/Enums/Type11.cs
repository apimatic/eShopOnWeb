using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Type11>))]
public sealed record Type11 : StringEnum<Type11>
{
    private Type11(string value) : base(value)
    {
    }

    public static readonly Type11 Product = new("product");

    public static Type11 FromValue(string value) => FromValueCore(value);
}
