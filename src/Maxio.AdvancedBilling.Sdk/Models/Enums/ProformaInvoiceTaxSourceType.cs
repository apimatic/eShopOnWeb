using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<ProformaInvoiceTaxSourceType>))]
public sealed record ProformaInvoiceTaxSourceType : StringEnum<ProformaInvoiceTaxSourceType>
{
    private ProformaInvoiceTaxSourceType(string value) : base(value)
    {
    }

    public static readonly ProformaInvoiceTaxSourceType Tax = new("Tax");

    public static readonly ProformaInvoiceTaxSourceType Avalara = new("Avalara");

    public static ProformaInvoiceTaxSourceType FromValue(string value) => FromValueCore(value);
}
