using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// The current status of the invoice. See <see href="https://maxio.zendesk.com/hc/en-us/articles/24252287829645-Advanced-Billing-Invoices-Overview#invoice-statuses">Invoice Statuses</see> for more.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<InvoiceStatus>))]
public sealed record InvoiceStatus : StringEnum<InvoiceStatus>
{
    private InvoiceStatus(string value) : base(value)
    {
    }

    public static readonly InvoiceStatus Draft = new("draft");

    public static readonly InvoiceStatus Open = new("open");

    public static readonly InvoiceStatus Paid = new("paid");

    public static readonly InvoiceStatus Pending = new("pending");

    public static readonly InvoiceStatus Voided = new("voided");

    public static readonly InvoiceStatus Canceled = new("canceled");

    public static readonly InvoiceStatus Processing = new("processing");

    public static InvoiceStatus FromValue(string value) => FromValueCore(value);
}
