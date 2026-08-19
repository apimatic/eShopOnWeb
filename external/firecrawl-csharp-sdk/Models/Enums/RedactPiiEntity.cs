using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// Public PII entity buckets supported by Firecrawl redaction.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<RedactPiiEntity>))]
public sealed record RedactPiiEntity : StringEnum<RedactPiiEntity>
{
    private RedactPiiEntity(string value) : base(value)
    {
    }

    public static readonly RedactPiiEntity Person = new("PERSON");

    public static readonly RedactPiiEntity Email = new("EMAIL");

    public static readonly RedactPiiEntity Phone = new("PHONE");

    public static readonly RedactPiiEntity Location = new("LOCATION");

    public static readonly RedactPiiEntity Financial = new("FINANCIAL");

    public static readonly RedactPiiEntity Secret = new("SECRET");

    public static RedactPiiEntity FromValue(string value) => FromValueCore(value);
}
