using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// The page size of the resulting PDF
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Format>))]
public sealed record Format : StringEnum<Format>
{
    private Format(string value) : base(value)
    {
    }

    public static readonly Format A0 = new("A0");

    public static readonly Format A1 = new("A1");

    public static readonly Format A2 = new("A2");

    public static readonly Format A3 = new("A3");

    public static readonly Format A4 = new("A4");

    public static readonly Format A5 = new("A5");

    public static readonly Format A6 = new("A6");

    public static readonly Format Letter = new("Letter");

    public static readonly Format Legal = new("Legal");

    public static readonly Format Tabloid = new("Tabloid");

    public static readonly Format Ledger = new("Ledger");

    public static Format FromValue(string value) => FromValueCore(value);
}
