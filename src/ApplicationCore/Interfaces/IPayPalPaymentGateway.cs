using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over PayPal's REST API for the capabilities this integration needs: charging a card
/// (one-off or via a saved/vaulted card), vaulting a card for later reuse, refunding a capture in
/// full, and removing a vaulted card. Implementations talk to PayPal over HTTP; the application core
/// depends only on this interface. Raw card data flows through the <see cref="PayPalCardDetails"/>
/// input and is never persisted or logged by callers.
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>Creates and captures a PayPal order paying with the supplied card in one call.</summary>
    /// <param name="idempotencyKey">Deterministic per-operation key sent as PayPal-Request-Id so a retry
    /// (e.g. a double-click) is deduplicated by PayPal and never charges twice.</param>
    Task<PayPalPaymentResult> ChargeCardAsync(decimal amount, string currency, PayPalCardDetails card,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Creates and captures a PayPal order paying with a previously vaulted card.</summary>
    Task<PayPalPaymentResult> ChargeVaultedCardAsync(decimal amount, string currency, string vaultId,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card for later reuse, returning a token id and PCI-safe display data.</summary>
    /// <param name="payPalCustomerId">Existing PayPal customer id to group the card under, or null to let
    /// PayPal assign a new one (returned on the result).</param>
    Task<PayPalVaultResult> VaultCardAsync(PayPalCardDetails card, string? payPalCustomerId,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture in full.</summary>
    Task<PayPalRefundResult> RefundCaptureAsync(string captureId, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Best-effort removal of a vaulted card at PayPal so no orphaned token is left behind.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);
}

/// <summary>Raw card input for a one-off charge or for vaulting. Never persisted or logged.</summary>
public record PayPalCardDetails(
    string Number,
    string Expiry,           // YYYY-MM
    string? SecurityCode,
    string CardholderName,
    PayPalBillingAddress? BillingAddress);

/// <summary>Billing address in PayPal's address shape (admin_area_1 = state, admin_area_2 = city).</summary>
public record PayPalBillingAddress(
    string? AddressLine1,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string? CountryCode);

/// <summary>Outcome of a charge. <see cref="Succeeded"/> is true only when the capture completed.</summary>
public record PayPalPaymentResult(
    bool Succeeded,
    string Status,
    string? PayPalOrderId,
    string? CaptureId,
    string? Brand,
    string? Last4,
    string? FailureReason);

/// <summary>Result of vaulting a card - the token id plus PCI-safe display fields.</summary>
public record PayPalVaultResult(
    string VaultId,
    string Brand,
    string Last4,
    string Expiry,
    string CardholderName,
    string PayPalCustomerId);

/// <summary>Outcome of a refund. <see cref="Succeeded"/> is true only when the refund completed.</summary>
public record PayPalRefundResult(
    bool Succeeded,
    string Status,
    string? RefundId,
    string? FailureReason);
