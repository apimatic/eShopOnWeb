using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Type28>))]
public sealed record Type28 : StringEnum<Type28>
{
    private Type28(string value) : base(value)
    {
    }

    public static readonly Type28 Crawl = new("crawl");

    public static Type28 FromValue(string value) => FromValueCore(value);
}
