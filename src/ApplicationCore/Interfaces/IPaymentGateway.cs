using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The card payment processor. Implementations translate every provider failure into
/// <see cref="Exceptions.PaymentGatewayException"/>; nothing provider-specific leaks past this seam.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>The ISO 4217 currency every order is charged in.</summary>
    string Currency { get; }

    /// <summary>Authorises the card for the amount and captures it immediately.</summary>
    /// <param name="deadline">Cancelled when the caller's time budget for the provider is spent.</param>
    Task<CardPaymentResult> ChargeCardAsync(CardPaymentRequest request, CancellationToken deadline);

    /// <summary>Asks the provider to give back (part of) a captured payment.</summary>
    /// <param name="deadline">Cancelled when the caller's time budget for the provider is spent.</param>
    Task<ProviderRefundResult> RefundAsync(ProviderRefundRequest request, CancellationToken deadline);
}

/// <summary>Card details exactly as the provider's checkout front end hands them over (encrypted fields).</summary>
public record CardDetails(
    string EncryptedCardNumber,
    string EncryptedExpiryMonth,
    string EncryptedExpiryYear,
    string EncryptedSecurityCode,
    string HolderName);

public record CardPaymentRequest(
    int OrderId,
    string IdempotencyKey,
    string MerchantReference,
    string Currency,
    long AmountMinor,
    CardDetails Card);

public enum CardPaymentOutcome
{
    Authorised,
    Refused,
    Error,
    Cancelled,
    Pending,
    /// <summary>The issuer wants a redirect or challenge (e.g. 3-D Secure), which this checkout does not support.</summary>
    ActionRequired,
    /// <summary>A result this integration does not act on (e.g. a partial authorisation).</summary>
    Unsupported
}

public record CardPaymentResult(
    CardPaymentOutcome Outcome,
    string ResultCode,
    string? PspReference,
    string? RefusalReason,
    string? RefusalReasonCode,
    string? ChargedCurrency,
    long? ChargedAmountMinor);

public record ProviderRefundRequest(
    string PaymentPspReference,
    string IdempotencyKey,
    string MerchantReference,
    string Currency,
    long AmountMinor,
    string? Reason);

public record ProviderRefundResult(string PspReference, string Status);
