using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// Invoice Event Type
/// </summary>
[JsonConverter(typeof(StringEnumConverter<InvoiceEventType>))]
public sealed record InvoiceEventType : StringEnum<InvoiceEventType>
{
    private InvoiceEventType(string value) : base(value)
    {
    }

    public static readonly InvoiceEventType IssueInvoice = new("issue_invoice");

    public static readonly InvoiceEventType ApplyCreditNote = new("apply_credit_note");

    public static readonly InvoiceEventType CreateCreditNote = new("create_credit_note");

    public static readonly InvoiceEventType ApplyPayment = new("apply_payment");

    public static readonly InvoiceEventType ApplyDebitNote = new("apply_debit_note");

    public static readonly InvoiceEventType CreateDebitNote = new("create_debit_note");

    public static readonly InvoiceEventType RefundInvoice = new("refund_invoice");

    public static readonly InvoiceEventType VoidInvoice = new("void_invoice");

    public static readonly InvoiceEventType VoidRemainder = new("void_remainder");

    public static readonly InvoiceEventType BackportInvoice = new("backport_invoice");

    public static readonly InvoiceEventType ChangeInvoiceStatus = new("change_invoice_status");

    public static readonly InvoiceEventType ChangeInvoiceCollectionMethod = new("change_invoice_collection_method");

    public static readonly InvoiceEventType RemovePayment = new("remove_payment");

    public static readonly InvoiceEventType FailedPayment = new("failed_payment");

    public static readonly InvoiceEventType ChangeChargebackStatus = new("change_chargeback_status");

    public static InvoiceEventType FromValue(string value) => FromValueCore(value);
}
