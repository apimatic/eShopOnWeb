using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Application-facing port over PayPal. The implementation lives in Infrastructure and is the only place
/// that talks to the PayPal SDK; it maps SDK models onto the SDK-free DTOs below and translates every
/// SDK/transport failure into a <see cref="Exceptions.PaymentGatewayException"/> (or, for a browser
/// challenge, <see cref="Exceptions.PaymentApprovalRequiredException"/>). All amounts are decimal in the
/// gateway's configured currency; the implementation formats them to the cent on the wire.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>Authorizes (holds) the order total against a card or a vaulted card. Does not capture.</summary>
    Task<PayPalAuthorizationResult> AuthorizeAsync(PayPalAuthorizeCommand command, CancellationToken ct);

    /// <summary>Reads the current state of an authorization (status and expiry).</summary>
    Task<PayPalAuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken ct);

    /// <summary>Renews a stale authorization, producing a fresh authorization id.</summary>
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string idempotencyKey, CancellationToken ct);

    /// <summary>Captures (takes the money for) an authorization.</summary>
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount,
        string idempotencyKey, CancellationToken ct);

    /// <summary>Voids an authorization, releasing the held funds.</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Refunds a captured payment, in full or in part. <paramref name="amount"/> null ⇒ remaining.</summary>
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount,
        string idempotencyKey, CancellationToken ct);

    /// <summary>Vaults a raw card, returning the token and a safe descriptor.</summary>
    Task<PayPalVaultedCard> VaultCardAsync(PayPalVaultCardCommand command, CancellationToken ct);

    /// <summary>Removes a vaulted card so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct);

    /// <summary>Lists PayPal's own transaction records for a date range, across the whole range.</summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct);
}

/// <summary>Card details for a one-off payment (never persisted). Exactly one of card/vault is set.</summary>
public record PayPalCardDetails(string Number, string Expiry, string SecurityCode, string? Name,
    string? CountryCode, string? AddressLine1, string? AddressLine2, string? AdminArea1, string? AdminArea2,
    string? PostalCode);

public record PayPalAuthorizeCommand(
    decimal Amount,
    string InvoiceId,
    string CustomId,
    string Description,
    string IdempotencyKey,
    PayPalCardDetails? Card,
    string? VaultId);

public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    string? InstrumentDescription);

public record PayPalAuthorizationInfo(string Status, DateTimeOffset? ExpiresAt);

public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string Currency);

public record PayPalRefundResult(string RefundId, string Status, decimal Amount, string Currency);

public record PayPalVaultCardCommand(string Number, string Expiry, string SecurityCode, string? Name,
    string MerchantCustomerId, string? CountryCode, string? AddressLine1, string? AddressLine2,
    string? AdminArea1, string? AdminArea2, string? PostalCode);

public record PayPalVaultedCard(string VaultId, string? Brand, string? LastDigits, string? Expiry,
    string? CardholderName, string? PayPalCustomerId);

public record PayPalTransaction(string? TransactionId, string? InvoiceId, decimal? Amount, string? Currency,
    string? Status, DateTimeOffset? Date, decimal? Fee);
