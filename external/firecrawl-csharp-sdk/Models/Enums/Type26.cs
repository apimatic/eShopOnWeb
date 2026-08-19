using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// Execute JavaScript code on the page
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Type26>))]
public sealed record Type26 : StringEnum<Type26>
{
    private Type26(string value) : base(value)
    {
    }

    public static readonly Type26 ExecuteJavascript = new("executeJavascript");

    public static Type26 FromValue(string value) => FromValueCore(value);
}
