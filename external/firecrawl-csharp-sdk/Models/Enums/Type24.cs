using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// Scroll the page or a specific element
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Type24>))]
public sealed record Type24 : StringEnum<Type24>
{
    private Type24(string value) : base(value)
    {
    }

    public static readonly Type24 Scroll = new("scroll");

    public static Type24 FromValue(string value) => FromValueCore(value);
}
