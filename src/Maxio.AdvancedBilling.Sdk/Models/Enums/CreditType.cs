using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// The type of credit to be created when upgrading/downgrading. Defaults to the component and then site setting if one is not provided.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CreditType>))]
public sealed record CreditType : StringEnum<CreditType>
{
    private CreditType(string value) : base(value)
    {
    }

    public static readonly CreditType Full = new("full");

    public static readonly CreditType Prorated = new("prorated");

    public static readonly CreditType None = new("none");

    public static CreditType FromValue(string value) => FromValueCore(value);
}
