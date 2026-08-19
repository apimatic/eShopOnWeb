using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// Generate a PDF of the current page. The PDF will be returned in the <c>actions.pdfs</c> array of the response.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Type27>))]
public sealed record Type27 : StringEnum<Type27>
{
    private Type27(string value) : base(value)
    {
    }

    public static readonly Type27 Pdf = new("pdf");

    public static Type27 FromValue(string value) => FromValueCore(value);
}
