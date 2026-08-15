using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Thin abstraction over the PayPal REST API (sandbox) covering exactly the capabilities this
/// integration needs. Implementations own token acquisition, idempotency headers, currency/amount
/// formatting and translation of PayPal error payloads into domain exceptions.
/// </summary>
public interface IPayPalClient
{
    /// <summary>Create a PayPal order for <paramref name="amount"/> and place a hold using a raw card.</summary>
    Task<PayPalAuthorizationResult> AuthorizeOrderWithCardAsync(decimal amount, string currencyCode,
        PayPalCardDetails card, string idempotencyKey, string invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Create a PayPal order for <paramref name="amount"/> and place a hold using a vaulted card.</summary>
    Task<PayPalAuthorizationResult> AuthorizeOrderWithVaultedCardAsync(decimal amount, string currencyCode,
        string vaultId, string idempotencyKey, string invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Read the current state of an authorization (to detect a stale/expired hold).</summary>
    Task<PayPalAuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Renew a stale authorization, yielding a fresh authorization id.</summary>
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Capture (take) the held funds. Returns PayPal's captured/fee/net breakdown.</summary>
    Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currencyCode,
        string idempotencyKey, string invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Void an authorization, releasing the held funds (no money moves).</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Refund a capture in full (null amount) or in part.</summary>
    Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currencyCode,
        string idempotencyKey, string invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Vault a raw card server-to-server, returning its token id and safe representation.</summary>
    Task<PayPalVaultResult> VaultCardAsync(PayPalCardDetails card, string? existingCustomerId,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Remove a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List PayPal's own record of transactions across the whole <paramref name="from"/>..<paramref name="to"/>
    /// range (chunking the request to respect PayPal's per-call range limit and paging every result).
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
