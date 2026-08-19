using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// Take a screenshot. The links will be in the response's <c>actions.screenshots</c> array.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Type20>))]
public sealed record Type20 : StringEnum<Type20>
{
    private Type20(string value) : base(value)
    {
    }

    public static readonly Type20 Screenshot = new("screenshot");

    public static Type20 FromValue(string value) => FromValueCore(value);
}
