using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over every PayPal interaction the payment flows need. The concrete implementation lives in
/// Infrastructure and is the only place the PayPal SDK is used; it translates SDK failures into
/// <see cref="PayPalGatewayException"/> and its subtypes.
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>Creates a PayPal order (intent AUTHORIZE) and authorizes it — placing a hold equal to the
    /// order total. Throws <see cref="PayPalChallengeRequiredException"/> if PayPal requires browser approval.</summary>
    Task<AuthorizationResult> AuthorizeAsync(AuthorizeRequest request, CancellationToken ct = default);

    /// <summary>Captures (takes) the authorized funds. Renews a stale authorization first where possible;
    /// throws <see cref="PayPalAuthorizationExpiredException"/> when it cannot be renewed.</summary>
    Task<CaptureResult> CaptureAsync(CaptureCommand command, CancellationToken ct = default);

    /// <summary>Voids an authorization, releasing the held funds.</summary>
    Task VoidAsync(string authorizationId, CancellationToken ct = default);

    /// <summary>Refunds a captured payment, in full (null amount) or in part.</summary>
    Task<RefundResult> RefundAsync(RefundCommand command, CancellationToken ct = default);

    /// <summary>Vaults a card for a customer, returning the token id and a safe description.</summary>
    Task<SavedCardResult> VaultCardAsync(VaultCardCommand command, CancellationToken ct = default);

    /// <summary>Removes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken ct = default);

    /// <summary>Lists PayPal's own record of transactions across the whole date range (all pages).</summary>
    Task<IReadOnlyList<ReconciliationTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
