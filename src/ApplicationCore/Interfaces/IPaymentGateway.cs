using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the PayPal payment processor. The only place SDK types are used is the
/// Infrastructure implementation; everything above this boundary speaks in application DTOs and
/// a single <see cref="PaymentGatewayException"/> failure type.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Merchant currency, from configuration (PayPal:Currency).</summary>
    string Currency { get; }

    /// <summary>Authorize (place a hold for) the order total. Does not capture.</summary>
    Task<AuthorizationResult> AuthorizeAsync(AuthorizeInstruction instruction, CancellationToken ct);

    /// <summary>Read the current state of an authorization (to detect staleness before capture).</summary>
    Task<AuthorizationSnapshot> GetAuthorizationAsync(string authorizationId, CancellationToken ct);

    /// <summary>Renew a stale authorization. Throws <see cref="AuthorizationNotRenewableException"/> when it cannot be renewed.</summary>
    Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string idempotencyKey, CancellationToken ct);

    /// <summary>Capture an authorization (take the money). Returns PayPal's captured amount, fee and net proceeds.</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Void an authorization before capture (release the hold).</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Refund a capture, full or partial. Idempotent under <paramref name="idempotencyKey"/>.</summary>
    Task<RefundResult> RefundAsync(string captureId, decimal amount, string idempotencyKey, CancellationToken ct);

    /// <summary>Vault (save) a card at PayPal, returning its token and a safe descriptor.</summary>
    Task<VaultCardResult> VaultCardAsync(CardInput card, CancellationToken ct);

    /// <summary>Delete a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct);

    /// <summary>List PayPal's own transaction records for a date range, paging the whole range.</summary>
    Task<IReadOnlyList<TransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
