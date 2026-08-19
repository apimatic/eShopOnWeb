using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Skills>))]
public sealed record Skills : StringEnum<Skills>
{
    private Skills(string value) : base(value)
    {
    }

    public static readonly Skills Only = new("only");

    public static Skills FromValue(string value) => FromValueCore(value);
}
