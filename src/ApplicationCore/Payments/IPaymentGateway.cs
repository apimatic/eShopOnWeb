using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// The card processor, as the order flow sees it. Implementations never throw for processor failures:
/// every outcome — including "we could not tell whether it happened" — comes back as a result.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>The ISO-4217 currency this deployment charges in.</summary>
    string Currency { get; }

    /// <summary>Authorises and immediately captures a card payment.</summary>
    /// <param name="cancellationToken">The deadline for the whole charge, including any settle-resend.</param>
    Task<ChargeResult> ChargeAsync(ChargeCommand command, CancellationToken cancellationToken);

    /// <summary>Requests a full or partial refund of a captured payment.</summary>
    Task<RefundResult> RefundAsync(RefundCommand command, CancellationToken cancellationToken);
}

/// <summary>Card fields exactly as the processor's checkout front end hands them over (encrypted).</summary>
public sealed class EncryptedCard
{
    public EncryptedCard(string encryptedCardNumber, string encryptedExpiryMonth, string encryptedExpiryYear, string encryptedSecurityCode, string holderName)
    {
        EncryptedCardNumber = encryptedCardNumber;
        EncryptedExpiryMonth = encryptedExpiryMonth;
        EncryptedExpiryYear = encryptedExpiryYear;
        EncryptedSecurityCode = encryptedSecurityCode;
        HolderName = holderName;
    }

    public string EncryptedCardNumber { get; }
    public string EncryptedExpiryMonth { get; }
    public string EncryptedExpiryYear { get; }
    public string EncryptedSecurityCode { get; }
    public string HolderName { get; }

    // Card data must never reach a log line through string formatting.
    public override string ToString() => "EncryptedCard { *** }";
}

public sealed record ChargeCommand(
    string IdempotencyKey,
    string MerchantReference,
    long AmountMinorUnits,
    string Currency,
    EncryptedCard Card,
    string ReturnUrl);

public enum ChargeStatus
{
    /// <summary>The full amount was authorised (and is being captured).</summary>
    Authorised,

    /// <summary>The issuer or processor declined, or the card needs a shopper step this API cannot perform. No money taken.</summary>
    Declined,

    /// <summary>The processor rejected the request as invalid (e.g. malformed card data). No money taken.</summary>
    Invalid,

    /// <summary>The processor refused to process the call for a reason on the merchant side (credentials, permissions, throttling). No money taken.</summary>
    ProviderError,

    /// <summary>An unusable authorisation (partial or wrong amount) was taken and reversed. No money kept.</summary>
    Reversed,

    /// <summary>As <see cref="Reversed"/>, but the reversal's own outcome is unknown.</summary>
    ReversalUnknown,

    /// <summary>The processor may or may not have charged the card. Resend with the same idempotency key to settle.</summary>
    Unknown
}

public sealed record ChargeResult(
    ChargeStatus Status,
    string ShopperMessage,
    string? PspReference = null,
    string? ResultCode = null,
    string? RefusalReason = null,
    string? RefusalReasonCode = null,
    bool TimedOut = false);

public sealed record RefundCommand(
    string PaymentPspReference,
    string IdempotencyKey,
    string Reference,
    long AmountMinorUnits,
    string Currency);

public enum RefundGatewayStatus
{
    /// <summary>The processor accepted the refund request.</summary>
    Received,

    /// <summary>The processor rejected the refund request. Nothing will be refunded.</summary>
    Rejected,

    /// <summary>The processor refused the call for a merchant-side reason. Nothing will be refunded.</summary>
    ProviderError,

    /// <summary>The processor may or may not have accepted the refund. Resend with the same idempotency key to settle.</summary>
    Unknown
}

public sealed record RefundResult(
    RefundGatewayStatus Status,
    string Message,
    string? PspReference = null,
    bool TimedOut = false);
