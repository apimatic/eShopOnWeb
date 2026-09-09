using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// The type of credit to be created when upgrading/downgrading. Defaults to the component and then site setting if one is not provided. Values are:
/// <para>
/// <c>full</c> - A charge is added for the full price of the component.
/// </para>
/// <para>
/// <c>prorated</c> - A charge is added for the prorated price of the component change.
/// </para>
/// <para>
/// <c>none</c> - No charge is added.
/// </para>
/// </summary>
[JsonConverter(typeof(StringEnumConverter<UpgradeChargeCreditType>))]
public sealed record UpgradeChargeCreditType : StringEnum<UpgradeChargeCreditType>
{
    private UpgradeChargeCreditType(string value) : base(value)
    {
    }

    public static readonly UpgradeChargeCreditType Full = new("full");

    public static readonly UpgradeChargeCreditType Prorated = new("prorated");

    public static readonly UpgradeChargeCreditType None = new("none");

    public static UpgradeChargeCreditType FromValue(string value) => FromValueCore(value);
}
