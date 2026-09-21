using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's view of PayPal. All PayPal-specific types stay behind this boundary in Infrastructure;
/// the domain and orchestration layers speak only in the plain records below. Every method translates SDK
/// and transport failures into <see cref="PayPalGatewayException"/>.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>The transaction currency, from configuration (e.g. USD).</summary>
    string Currency { get; }

    /// <summary>
    /// Authorize (hold) the amount for an order. Uses a raw card or a saved-card vault id. Idempotent on
    /// <see cref="PayPalAuthorizeRequest.IdempotencyKey"/>.
    /// </summary>
    Task<PayPalAuthorizationOutcome> AuthorizeAsync(PayPalAuthorizeRequest request, CancellationToken ct);

    /// <summary>Capture a previously authorized payment (money moves). Idempotent on <paramref name="idempotencyKey"/>.</summary>
    Task<PayPalCaptureOutcome> CaptureAsync(string authorizationId, decimal amount, string idempotencyKey, CancellationToken ct);

    /// <summary>Renew a stale authorization so a capture can still succeed. Idempotent on <paramref name="idempotencyKey"/>.</summary>
    Task<PayPalReauthorizeOutcome> ReauthorizeAsync(string authorizationId, decimal amount, string idempotencyKey, CancellationToken ct);

    /// <summary>Read the current state of an authorization (used to settle ambiguous outcomes).</summary>
    Task<PayPalAuthorizationStatus> GetAuthorizationAsync(string authorizationId, CancellationToken ct);

    /// <summary>Void (release) an authorization before capture. Idempotent on <paramref name="idempotencyKey"/>.</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Refund a captured payment, full (<paramref name="amount"/> null) or partial. Idempotent on <paramref name="idempotencyKey"/>.</summary>
    Task<PayPalRefundOutcome> RefundAsync(string captureId, decimal? amount, string idempotencyKey, CancellationToken ct);

    /// <summary>Vault (save) a card for later reuse. Returns the vault id and a safe descriptor; the PAN is never returned or stored.</summary>
    Task<PayPalVaultOutcome> VaultCardAsync(PayPalVaultCardRequest request, CancellationToken ct);

    /// <summary>Delete a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct);

    /// <summary>
    /// PayPal's own record of transactions across a date range, paged in full and chunked into ≤31-day
    /// windows. May legitimately be empty for very recent activity (reporting lag).
    /// </summary>
    Task<PayPalTransactionSearch> SearchTransactionsAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct);
}

/// <summary>Raw card details for a one-off payment. The PAN is used once and never persisted by this app.</summary>
public sealed record PayPalCardInput(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? CardholderName,
    PayPalBillingAddress? BillingAddress);

public sealed record PayPalBillingAddress(
    string? AddressLine1,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string? CountryCode);

/// <summary>Authorize either a raw card (<see cref="Card"/>) or a saved card (<see cref="VaultId"/>).</summary>
public sealed record PayPalAuthorizeRequest(
    decimal Amount,
    string? InvoiceId,
    string CustomId,
    string IdempotencyKey,
    PayPalCardInput? Card,
    string? VaultId,
    string? Description);

public sealed record PayPalAuthorizationOutcome(
    string PayPalOrderId,
    string? AuthorizationId,
    string OrderStatus,
    string? AuthorizationStatus,
    DateTimeOffset? CreatedAtUtc,
    bool RequiresBuyerApproval);

public sealed record PayPalAuthorizationStatus(string Status, DateTimeOffset? ExpiresAtUtc);

public sealed record PayPalCaptureOutcome(
    string CaptureId,
    string Status,
    decimal Gross,
    decimal? Fee,
    decimal? Net,
    string Currency,
    DateTimeOffset? CreatedAtUtc);

public sealed record PayPalReauthorizeOutcome(string AuthorizationId, string Status, DateTimeOffset? CreatedAtUtc);

public sealed record PayPalRefundOutcome(string RefundId, string Status, decimal Amount);

public sealed record PayPalVaultCardRequest(
    string BuyerReference,
    string? ExistingCustomerId,
    PayPalCardInput Card,
    string IdempotencyKey);

public sealed record PayPalVaultOutcome(
    string VaultId,
    string? CustomerId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName);

public sealed record PayPalTransactionRecord(
    string? TransactionId,
    string? ReferenceId,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? InitiatedAtUtc,
    string? EventCode);

public sealed record PayPalTransactionSearch(
    IReadOnlyList<PayPalTransactionRecord> Records,
    bool Truncated,
    int WindowsCovered);
