using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The card payment processor. Implementations never throw for provider or transport failures: every outcome,
/// including "the call may or may not have reached the provider", comes back as a result value carrying what
/// the provider returned on the wire.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Name shown in the payment record, e.g. "Adyen".</summary>
    string ProviderName { get; }

    /// <summary>The currency every new payment is charged in.</summary>
    string Currency { get; }

    /// <summary>Authorises the card and takes the money immediately.</summary>
    Task<CardPaymentResult> ChargeCardAsync(CardPaymentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Gives back all or part of a captured payment.</summary>
    Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// The card exactly as the provider's checkout front end hands it over: each field encrypted on the shopper's
/// device. The shop never sees a plain card number.
/// </summary>
public sealed record EncryptedCard(
    string EncryptedCardNumber,
    string EncryptedExpiryMonth,
    string EncryptedExpiryYear,
    string EncryptedSecurityCode,
    string HolderName)
{
    // Never print card data, even encrypted.
    public override string ToString() => "EncryptedCard { *** }";
}

public sealed record CardPaymentRequest(
    string IdempotencyKey,
    string MerchantReference,
    long AmountMinor,
    string Currency,
    EncryptedCard Card);

public enum CardPaymentOutcome
{
    Authorised,
    Refused,
    /// <summary>The provider processed the payment and reported an error or cancellation; no money taken.</summary>
    Failed,
    /// <summary>The card needs a shopper action (redirect / 3-D Secure) that this API cannot perform.</summary>
    ActionRequired,
    /// <summary>The provider accepted the payment without a final outcome.</summary>
    Pending,
    /// <summary>The provider rejected the request itself (validation, configuration).</summary>
    Rejected,
    /// <summary>The call may or may not have reached the provider.</summary>
    Unknown
}

/// <summary>What one exchange with the provider produced, verbatim, for the support record.</summary>
public sealed record ProviderExchange(int? HttpStatus, string? ResponseBody, string? TransportError);

public sealed record CardPaymentResult(
    CardPaymentOutcome Outcome,
    string? PspReference,
    string? ResultCode,
    string? RefusalReason,
    string? RefusalReasonCode,
    string? ErrorCode,
    string? ErrorMessage,
    bool IsConfigurationError,
    ProviderExchange Exchange);

public sealed record RefundRequest(
    string IdempotencyKey,
    string MerchantReference,
    string PaymentPspReference,
    long AmountMinor,
    string Currency);

public enum RefundOutcome
{
    /// <summary>The provider accepted the refund request.</summary>
    Received,
    /// <summary>The provider rejected the refund request.</summary>
    Rejected,
    /// <summary>The call may or may not have reached the provider.</summary>
    Unknown
}

public sealed record RefundResult(
    RefundOutcome Outcome,
    string? PspReference,
    string? ErrorCode,
    string? ErrorMessage,
    bool IsConfigurationError,
    ProviderExchange Exchange);
