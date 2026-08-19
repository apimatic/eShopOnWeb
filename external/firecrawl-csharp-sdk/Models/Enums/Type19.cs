using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// Wait for a specific element to appear
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Type19>))]
public sealed record Type19 : StringEnum<Type19>
{
    private Type19(string value) : base(value)
    {
    }

    public static readonly Type19 Wait = new("wait");

    public static Type19 FromValue(string value) => FromValueCore(value);
}
