using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<TaxConfigurationKind>))]
public sealed record TaxConfigurationKind : StringEnum<TaxConfigurationKind>
{
    private TaxConfigurationKind(string value) : base(value)
    {
    }

    public static readonly TaxConfigurationKind Custom = new("custom");

    public static readonly TaxConfigurationKind ManagedAvalara = new("managed avalara");

    public static readonly TaxConfigurationKind LinkedAvalara = new("linked avalara");

    public static readonly TaxConfigurationKind DigitalRiver = new("digital river");

    public static TaxConfigurationKind FromValue(string value) => FromValueCore(value);
}
