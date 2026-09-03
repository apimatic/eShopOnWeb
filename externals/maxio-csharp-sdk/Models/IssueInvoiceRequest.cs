using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record IssueInvoiceRequest
{
    /// <summary>
    /// Action taken when payment for an invoice fails:
    /// - <c>leave_open_invoice</c> - prepayments and credits applied to invoice; invoice status set to "open"; email sent to the customer for the issued invoice (if setting applies); payment failure recorded in the invoice history. This is the default option.
    /// - <c>rollback_to_pending</c> - prepayments and credits not applied; invoice remains in "pending" status; no email sent to the customer; payment failure recorded in the invoice history.
    /// - <c>initiate_dunning</c> - prepayments and credits applied to the invoice; invoice status set to "open"; email sent to the customer for the issued invoice (if setting applies); payment failure recorded in the invoice history; subscription will most likely go into "past_due" or "canceled" state (depending upon net terms and dunning settings).
    /// </summary>
    [JsonPropertyName("on_failed_payment")]
    public FailedPaymentAction? OnFailedPayment { get; init; } = FailedPaymentAction.LeaveOpenInvoice;

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
