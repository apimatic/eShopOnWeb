using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the external payment gateway (PayPal). Keeps the endpoints and domain free of
/// provider-specific HTTP details. Card data flows through the request models only; it is passed
/// straight to the gateway and never persisted or logged.
/// </summary>
public interface IPaymentGatewayService
{
    /// <summary>Charges a raw card for a one-off payment (create + capture in one step).</summary>
    Task<CardPaymentResult> ChargeCardAsync(CardChargeRequest request, CancellationToken cancellationToken = default);

    /// <summary>Charges a previously vaulted card, identified by its payment token.</summary>
    Task<CardPaymentResult> ChargeSavedCardAsync(SavedCardChargeRequest request, CancellationToken cancellationToken = default);

    /// <summary>Refunds a captured payment in full.</summary>
    Task<RefundResult> RefundAsync(string captureId, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card for reuse and returns its payment token plus a safe descriptor.</summary>
    Task<VaultedCard> VaultCardAsync(CardDetails card, CancellationToken cancellationToken = default);

    /// <summary>Removes a vaulted card so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken = default);
}

/// <summary>Raw card details supplied by the shopper. Never stored or logged.</summary>
public record CardDetails(
    string Number,
    string Expiry,
    string? SecurityCode,
    string CardholderName,
    CardBillingAddress BillingAddress);

public record CardBillingAddress(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string? State,
    string PostalCode,
    string CountryCode);

public record CardChargeRequest(decimal Amount, string Currency, CardDetails Card);

public record SavedCardChargeRequest(decimal Amount, string Currency, string VaultId);

public record CardPaymentResult(string ProviderOrderId, string CaptureId, string Status);

public record RefundResult(string RefundId, string Status);

public record VaultedCard(
    string PaymentTokenId,
    string Brand,
    string LastFourDigits,
    string Expiry,
    string? CardholderName,
    string? CustomerId);
