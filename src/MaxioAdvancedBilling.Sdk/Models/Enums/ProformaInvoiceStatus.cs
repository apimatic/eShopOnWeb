using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<ProformaInvoiceStatus>))]
public sealed record ProformaInvoiceStatus : StringEnum<ProformaInvoiceStatus>
{
    private ProformaInvoiceStatus(string value) : base(value)
    {
    }

    public static readonly ProformaInvoiceStatus Draft = new("draft");

    public static readonly ProformaInvoiceStatus Voided = new("voided");

    public static readonly ProformaInvoiceStatus Archived = new("archived");

    public static ProformaInvoiceStatus FromValue(string value) => FromValueCore(value);
}
