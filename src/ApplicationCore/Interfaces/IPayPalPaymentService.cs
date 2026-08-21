using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Thin, domain-facing abstraction over PayPal. The concrete implementation (in Infrastructure) is the only
/// place the PayPal SDK is referenced; everything above this interface works in eShop terms. All amounts are
/// decimals in the configured currency; the implementation formats them to the cent on the wire.
/// </summary>
public interface IPayPalPaymentService
{
    /// <summary>
    /// Authorizes (holds) <paramref name="amount"/> against a card — either raw card details or a previously
    /// vaulted card — without capturing. The <paramref name="idempotencyKey"/> is sent as PayPal's request id
    /// so a retry never places two holds.
    /// </summary>
    Task<PayPalAuthorizationResult> AuthorizeAsync(decimal amount, PayPalCard card, string idempotencyKey, CancellationToken ct);

    /// <summary>Captures a previously-created authorization, taking the money. Idempotent under the same key.</summary>
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Renews a stale authorization, returning the renewed authorization to capture against.</summary>
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string idempotencyKey, CancellationToken ct);

    /// <summary>Voids an authorization, releasing the held funds. No money moves.</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Refunds a capture, in full (<paramref name="amount"/> null) or in part.</summary>
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string idempotencyKey, CancellationToken ct);

    /// <summary>Vaults a card for later reuse, returning the vault id and a safe (non-sensitive) descriptor.</summary>
    Task<PayPalVaultResult> VaultCardAsync(PayPalCard card, string idempotencyKey, CancellationToken ct);

    /// <summary>Removes a vaulted card so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct);

    /// <summary>
    /// Returns PayPal's own record of transactions across the whole date range (paged internally), for
    /// reconciliation against eShop orders.
    /// </summary>
    Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

/// <summary>
/// A card to charge or vault. Either raw details (Number/Expiry/SecurityCode) OR a previously-saved
/// <see cref="VaultId"/> is set — never both. Raw card details are never stored or logged by this app.
/// </summary>
public sealed record PayPalCard
{
    public string? VaultId { get; init; }
    public string? Name { get; init; }
    public string? Number { get; init; }
    public string? Expiry { get; init; }
    public string? SecurityCode { get; init; }
    public PayPalBillingAddress? BillingAddress { get; init; }

    public bool IsVaulted => !string.IsNullOrEmpty(VaultId);
}

public sealed record PayPalBillingAddress
{
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? AdminArea1 { get; init; }
    public string? AdminArea2 { get; init; }
    public string? PostalCode { get; init; }
    public string? CountryCode { get; init; }
}

public sealed record PayPalAuthorizationResult
{
    public string? PayPalOrderId { get; init; }
    public string? AuthorizationId { get; init; }
    public string? AuthorizationStatus { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>True when PayPal answered with a challenge requiring browser approval — payment stopped.</summary>
    public bool RequiresApproval { get; init; }

    /// <summary>Best-effort description of the approval requirement, for reporting (never sensitive).</summary>
    public string? ApprovalDetail { get; init; }

    public bool HasUsableAuthorization => !RequiresApproval && !string.IsNullOrEmpty(AuthorizationId);
}

public sealed record PayPalCaptureResult
{
    public required string CaptureId { get; init; }
    public required string Status { get; init; }
    public required decimal CapturedAmount { get; init; }
    public decimal? PayPalFee { get; init; }
    public decimal? NetAmount { get; init; }
    public string? CurrencyCode { get; init; }
}

public sealed record PayPalRefundResult
{
    public required string RefundId { get; init; }
    public required string Status { get; init; }
    public decimal? Amount { get; init; }
    public decimal? TotalRefunded { get; init; }
    public string? CurrencyCode { get; init; }
}

public sealed record PayPalVaultResult
{
    public required string VaultId { get; init; }
    public string? CardBrand { get; init; }
    public required string LastFourDigits { get; init; }
    public string? Expiry { get; init; }
    public string? CardholderName { get; init; }
}

public sealed record PayPalTransactionRecord
{
    public string? TransactionId { get; init; }
    public decimal? Amount { get; init; }
    public string? CurrencyCode { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? Date { get; init; }
}
