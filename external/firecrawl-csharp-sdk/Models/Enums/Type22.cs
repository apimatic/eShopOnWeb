using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// Write text into an input field, text area, or contenteditable element. Note: You must first focus the element using a 'click' action before writing. The text will be typed character by character to simulate keyboard input.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Type22>))]
public sealed record Type22 : StringEnum<Type22>
{
    private Type22(string value) : base(value)
    {
    }

    public static readonly Type22 Write = new("write");

    public static Type22 FromValue(string value) => FromValueCore(value);
}
