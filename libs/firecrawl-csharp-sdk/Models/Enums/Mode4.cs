using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// PDF parsing mode. "fast": text-only extraction. "auto": text-first with OCR fallback. "ocr": OCR on every page.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Mode4>))]
public sealed record Mode4 : StringEnum<Mode4>
{
    private Mode4(string value) : base(value)
    {
    }

    public static readonly Mode4 Fast = new("fast");

    public static readonly Mode4 Auto = new("auto");

    public static readonly Mode4 Ocr = new("ocr");

    public static Mode4 FromValue(string value) => FromValueCore(value);
}
