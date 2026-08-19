using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// <c>tag</c> replaces spans with placeholders like <c>&lt;EMAIL&gt;</c>, <c>mask</c> replaces characters with <c>*</c>, and <c>remove</c> deletes the span text.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ReplaceStyle>))]
public sealed record ReplaceStyle : StringEnum<ReplaceStyle>
{
    private ReplaceStyle(string value) : base(value)
    {
    }

    public static readonly ReplaceStyle Tag = new("tag");

    public static readonly ReplaceStyle Mask = new("mask");

    public static readonly ReplaceStyle Remove = new("remove");

    public static ReplaceStyle FromValue(string value) => FromValueCore(value);
}
