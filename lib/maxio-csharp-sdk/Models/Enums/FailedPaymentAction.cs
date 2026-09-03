using System.Text.Json.Serialization;
using Maxio.Core.Enum;

namespace Maxio.Models.Enums;

/// <summary>
/// Action taken when payment for an invoice fails:
/// - <c>leave_open_invoice</c> - prepayments and credits applied to invoice; invoice status set to "open"; email sent to the customer for the issued invoice (if setting applies); payment failure recorded in the invoice history. This is the default option.
/// - <c>rollback_to_pending</c> - prepayments and credits not applied; invoice remains in "pending" status; no email sent to the customer; payment failure recorded in the invoice history.
/// - <c>initiate_dunning</c> - prepayments and credits applied to the invoice; invoice status set to "open"; email sent to the customer for the issued invoice (if setting applies); payment failure recorded in the invoice history; subscription will most likely go into "past_due" or "canceled" state (depending upon net terms and dunning settings).
/// </summary>
[JsonConverter(typeof(StringEnumConverter<FailedPaymentAction>))]
public sealed record FailedPaymentAction : StringEnum<FailedPaymentAction>
{
    private FailedPaymentAction(string value) : base(value)
    {
    }

    public static readonly FailedPaymentAction LeaveOpenInvoice = new("leave_open_invoice");

    public static readonly FailedPaymentAction RollbackToPending = new("rollback_to_pending");

    public static readonly FailedPaymentAction InitiateDunning = new("initiate_dunning");

    public static FailedPaymentAction FromValue(string value) => FromValueCore(value);
}
