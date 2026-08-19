using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Enterprise>))]
public sealed record Enterprise : StringEnum<Enterprise>
{
    private Enterprise(string value) : base(value)
    {
    }

    public static readonly Enterprise Anon = new("anon");

    public static readonly Enterprise Zdr = new("zdr");

    public static Enterprise FromValue(string value) => FromValueCore(value);
}
