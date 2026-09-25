using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// The one place the PayPal Server SDK is called. Every method translates SDK failures to
/// <see cref="PayPalIntegrationException"/>, sends a deterministic idempotency key so a repeat is
/// safe, and settles unknown outcomes by re-reading provider state.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>Place a hold (authorize) for the order total, funded by a one-off card or a saved card.</summary>
    Task<AuthorizationResult> AuthorizeAsync(AuthorizeInstruction instruction, CancellationToken cancellationToken);

    /// <summary>Capture (take) the authorized funds at fulfilment, renewing a stale hold first if needed.</summary>
    Task<CaptureResult> CaptureAsync(CaptureInstruction instruction, CancellationToken cancellationToken);

    /// <summary>Void (release) the hold before fulfilment.</summary>
    Task<string> VoidAsync(int orderId, string invoiceId, string authorizationId, CancellationToken cancellationToken);

    /// <summary>Refund a captured payment, in full or in part, under a caller idempotency key.</summary>
    Task<RefundResult> RefundAsync(RefundInstruction instruction, CancellationToken cancellationToken);

    /// <summary>Vault (save) a card and return its vault id plus a safe descriptor.</summary>
    Task<VaultResult> VaultCardAsync(VaultCardInstruction instruction, CancellationToken cancellationToken);

    /// <summary>Delete a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken);

    /// <summary>
    /// Walk PayPal's transaction reporting across a date range, covering the whole range (chunked
    /// into ≤31-day windows and all pages within each), for reconciliation.
    /// </summary>
    Task<TransactionSearchResult> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}
