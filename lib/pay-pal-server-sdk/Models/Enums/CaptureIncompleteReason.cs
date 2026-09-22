using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The reason why the captured payment status is <c>PENDING</c> or <c>DENIED</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CaptureIncompleteReason>))]
public sealed record CaptureIncompleteReason : StringEnum<CaptureIncompleteReason>
{
    private CaptureIncompleteReason(string value) : base(value)
    {
    }

    /// <summary>
    /// The payer initiated a dispute for this captured payment with PayPal.
    /// </summary>
    public static readonly CaptureIncompleteReason BuyerComplaint = new("BUYER_COMPLAINT");

    /// <summary>
    /// The captured funds were reversed in response to the payer disputing this captured payment with the issuer of the financial instrument used to pay for this captured payment.
    /// </summary>
    public static readonly CaptureIncompleteReason Chargeback = new("CHARGEBACK");

    /// <summary>
    /// The payer paid by an eCheck that has not yet cleared.
    /// </summary>
    public static readonly CaptureIncompleteReason Echeck = new("ECHECK");

    /// <summary>
    /// Visit your online account. In your <b>Account Overview</b>, accept and deny this payment.
    /// </summary>
    public static readonly CaptureIncompleteReason InternationalWithdrawal = new("INTERNATIONAL_WITHDRAWAL");

    /// <summary>
    /// No additional specific reason can be provided. For more information about this captured payment, visit your account online or contact PayPal.
    /// </summary>
    public static readonly CaptureIncompleteReason Other = new("OTHER");

    /// <summary>
    /// The captured payment is pending manual review.
    /// </summary>
    public static readonly CaptureIncompleteReason PendingReview = new("PENDING_REVIEW");

    /// <summary>
    /// The payee has not yet set up appropriate receiving preferences for their account. For more information about how to accept or deny this payment, visit your account online. This reason is typically offered in scenarios such as when the currency of the captured payment is different from the primary holding currency of the payee.
    /// </summary>
    public static readonly CaptureIncompleteReason ReceivingPreferenceMandatesManualAction = new("RECEIVING_PREFERENCE_MANDATES_MANUAL_ACTION");

    /// <summary>
    /// The captured funds were refunded.
    /// </summary>
    public static readonly CaptureIncompleteReason Refunded = new("REFUNDED");

    /// <summary>
    /// The payer must send the funds for this captured payment. This code generally appears for manual EFTs.
    /// </summary>
    public static readonly CaptureIncompleteReason TransactionApprovedAwaitingFunding = new("TRANSACTION_APPROVED_AWAITING_FUNDING");

    /// <summary>
    /// The payee does not have a PayPal account.
    /// </summary>
    public static readonly CaptureIncompleteReason Unilateral = new("UNILATERAL");

    /// <summary>
    /// The payee's PayPal account is not verified.
    /// </summary>
    public static readonly CaptureIncompleteReason VerificationRequired = new("VERIFICATION_REQUIRED");

    /// <summary>
    /// Risk Filter set by the payee failed for the transaction.
    /// </summary>
    public static readonly CaptureIncompleteReason DeclinedByRiskFraudFilters = new("DECLINED_BY_RISK_FRAUD_FILTERS");

    public static CaptureIncompleteReason FromValue(string value) => FromValueCore(value);
}
