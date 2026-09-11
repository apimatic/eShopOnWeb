using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>
/// The port through which the application talks to PayPal. The concrete adapter builds every
/// request against PayPal's OpenAPI specification (Checkout Orders v2, Payments v2, Vault v3,
/// Transaction Search v1). Domain code depends only on this interface.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>Creates a PayPal order with intent AUTHORIZE and places a hold on the money
    /// equal to the order total. Does not capture. The result reports whether the hold was
    /// placed, whether the card needs a browser challenge, or why it failed.</summary>
    Task<AuthorizeResult> AuthorizeAsync(AuthorizeInstruction instruction, CancellationToken ct = default);

    /// <summary>Fetches the current state of a hold, so a stale one can be detected before
    /// fulfilment.</summary>
    Task<AuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    /// <summary>Renews a hold that has gone stale before fulfilment.</summary>
    Task<AuthorizationInfo> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, string payPalRequestId, CancellationToken ct = default);

    /// <summary>Captures a hold — this is when the money is actually taken. Returns the
    /// captured amount, PayPal's fee and the net proceeds.</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currencyCode, string invoiceId, string payPalRequestId, CancellationToken ct = default);

    /// <summary>Releases a hold before fulfilment so no money moves.</summary>
    Task VoidAsync(string authorizationId, string payPalRequestId, CancellationToken ct = default);

    /// <summary>Refunds a captured payment, in full or in part.</summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode, string invoiceId, string payPalRequestId, CancellationToken ct = default);

    /// <summary>Vaults a card for later reuse and returns the safe descriptor.</summary>
    Task<VaultCardResult> VaultCardAsync(string customerId, CardDetails card, string payPalRequestId, CancellationToken ct = default);

    /// <summary>Removes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default);

    /// <summary>Lists PayPal's own record of transactions across the whole date range,
    /// following pagination so the report covers more than the first page.</summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
