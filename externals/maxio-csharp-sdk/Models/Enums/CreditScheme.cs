using System.Text.Json.Serialization;
using Maxio.Core.Enum;

namespace Maxio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<CreditScheme>))]
public sealed record CreditScheme : StringEnum<CreditScheme>
{
    private CreditScheme(string value) : base(value)
    {
    }

    public static readonly CreditScheme None = new("none");

    public static readonly CreditScheme Credit = new("credit");

    public static readonly CreditScheme Refund = new("refund");

    public static CreditScheme FromValue(string value) => FromValueCore(value);
}
