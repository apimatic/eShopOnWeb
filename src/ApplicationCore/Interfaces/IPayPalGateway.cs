using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Everything this integration needs from the PayPal REST API: direct-card
/// authorize/capture, authorization lifecycle (reauthorize/void), refunds, the card vault, and
/// transaction search for reconciliation. Implemented in Infrastructure against the confirmed
/// PayPal REST contract (Orders v2, Payments v2, Vault v3, Reporting v1).</summary>
public interface IPayPalGateway
{
    /// <summary>Creates a PayPal order and authorizes it for <paramref name="amount"/> using either
    /// raw card details or a previously vaulted card (exactly one of <paramref name="card"/> /
    /// <paramref name="vaultId"/> must be supplied). Direct card entry processes inline, so the
    /// authorization (or a payer-action-required signal) comes back in this one call.</summary>
    Task<PayPalAuthorizationResult> AuthorizeCardPaymentAsync(
        decimal amount, string currencyCode, string invoiceId, string payPalRequestId,
        PayPalCardDetails? card, string? vaultId, CancellationToken ct = default);

    Task<PayPalReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, string payPalRequestId, CancellationToken ct = default);

    Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currencyCode, string payPalRequestId, CancellationToken ct = default);

    Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal amount, string currencyCode, string payPalRequestId, CancellationToken ct = default);

    /// <summary>Saves a new card to PayPal's vault (Vault v3: setup-token then payment-token) and
    /// returns the reusable payment token id plus safe-to-display card details.</summary>
    Task<PayPalVaultCardResult> SaveCardAsync(PayPalCardDetails card, CancellationToken ct = default);

    Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken ct = default);

    /// <summary>Single page of PayPal's transaction report for a range no wider than PayPal's
    /// 31-day maximum. Callers needing a wider range must chunk it themselves.</summary>
    Task<PayPalTransactionSearchResult> SearchTransactionsPageAsync(DateTimeOffset from, DateTimeOffset to, int page, int pageSize, CancellationToken ct = default);
}
