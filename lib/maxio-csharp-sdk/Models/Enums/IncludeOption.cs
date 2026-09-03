using System.Text.Json.Serialization;
using Maxio.Core.Enum;

namespace Maxio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<IncludeOption>))]
public sealed record IncludeOption : StringEnum<IncludeOption>
{
    private IncludeOption(string value) : base(value)
    {
    }

    public static readonly IncludeOption _0 = new("0");

    public static readonly IncludeOption _1 = new("1");

    public static IncludeOption FromValue(string value) => FromValueCore(value);
}
