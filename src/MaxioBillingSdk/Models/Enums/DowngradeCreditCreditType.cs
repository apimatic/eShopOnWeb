using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// The type of credit to be created when upgrading/downgrading. Defaults to the component and then site setting if one is not provided. Values are:
/// <para>
/// <c>full</c> -  A full price credit is added for the amount owed.
/// </para>
/// <para>
/// <c>prorated</c> - A prorated credit is added for the amount owed.
/// </para>
/// <para>
/// <c>none</c> - No charge is added.
/// </para>
/// </summary>
[JsonConverter(typeof(StringEnumConverter<DowngradeCreditCreditType>))]
public sealed record DowngradeCreditCreditType : StringEnum<DowngradeCreditCreditType>
{
    private DowngradeCreditCreditType(string value) : base(value)
    {
    }

    public static readonly DowngradeCreditCreditType Full = new("full");

    public static readonly DowngradeCreditCreditType Prorated = new("prorated");

    public static readonly DowngradeCreditCreditType None = new("none");

    public static DowngradeCreditCreditType FromValue(string value) => FromValueCore(value);
}
