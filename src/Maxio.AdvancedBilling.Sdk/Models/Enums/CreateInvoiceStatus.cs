using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<CreateInvoiceStatus>))]
public sealed record CreateInvoiceStatus : StringEnum<CreateInvoiceStatus>
{
    private CreateInvoiceStatus(string value) : base(value)
    {
    }

    public static readonly CreateInvoiceStatus Draft = new("draft");

    public static readonly CreateInvoiceStatus Open = new("open");

    public static CreateInvoiceStatus FromValue(string value) => FromValueCore(value);
}
