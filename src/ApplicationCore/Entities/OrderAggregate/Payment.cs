using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Carries the payment state that PayPal owns for an order: the ids and current status of
/// the hold (authorization), the capture, and the refunds, plus the money figures PayPal
/// reports at capture time. It holds enough state that a later request (fulfil, cancel,
/// refund, reconcile) can act on the payment, not only the request that created it.
///
/// No card number or other full card detail is ever stored here.
/// </summary>
public class Payment : BaseEntity
{
    /// <summary>The PayPal v2 Checkout order id (the container for the authorization).</summary>
    public string PayPalOrderId { get; private set; }

    public string CurrencyCode { get; private set; }

    /// <summary>The amount authorized (held). Equals the eShop order total to the cent.</summary>
    public decimal AuthorizedAmount { get; private set; }

    /// <summary>PayPal authorization id — the hold on the funds.</summary>
    public string? AuthorizationId { get; private set; }

    /// <summary>Current PayPal authorization status (e.g. CREATED, CAPTURED, VOIDED, EXPIRED).</summary>
    public string? AuthorizationStatus { get; private set; }

    /// <summary>When the current authorization expires and must be re-authorized before capture.</summary>
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    /// <summary>PayPal capture id — created at fulfilment when the money is actually taken.</summary>
    public string? CaptureId { get; private set; }

    /// <summary>Current PayPal capture status (e.g. COMPLETED, PARTIALLY_REFUNDED, REFUNDED).</summary>
    public string? CaptureStatus { get; private set; }

    /// <summary>Gross amount captured, as reported by PayPal.</summary>
    public decimal? CapturedAmount { get; private set; }

    /// <summary>PayPal's fee on the capture, as reported by PayPal.</summary>
    public decimal? PayPalFee { get; private set; }

    /// <summary>Net proceeds to the merchant (captured minus fee), as reported by PayPal.</summary>
    public decimal? NetAmount { get; private set; }

    /// <summary>When the capture happened (used to scope reconciliation by date).</summary>
    public DateTimeOffset? CapturedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>When the card was a saved (vaulted) card, the vault id used. Null for one-off cards.</summary>
    public string? VaultId { get; private set; }

    /// <summary>Safe description of the instrument used (brand + last 4), never full details.</summary>
    public string? CardDescriptor { get; private set; }

    private readonly List<Refund> _refunds = new();
    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(string payPalOrderId, string currencyCode, decimal authorizedAmount)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NegativeOrZero(authorizedAmount, nameof(authorizedAmount));

        PayPalOrderId = payPalOrderId;
        CurrencyCode = currencyCode;
        AuthorizedAmount = authorizedAmount;
    }

    public void SetAuthorization(string authorizationId, string status, DateTimeOffset? expiresAt,
        string? vaultId, string? cardDescriptor)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(status, nameof(status));
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
        VaultId = vaultId;
        CardDescriptor = cardDescriptor;
    }

    /// <summary>Replace the authorization after a re-authorization of a stale hold.</summary>
    public void RenewAuthorization(string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(status, nameof(status));
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    public void UpdateAuthorizationStatus(string status)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        AuthorizationStatus = status;
    }

    public void SetCapture(string captureId, string status, decimal capturedAmount, decimal? fee, decimal? net)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Guard.Against.NullOrEmpty(status, nameof(status));
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = fee;
        NetAmount = net;
        CapturedAt = DateTimeOffset.UtcNow;
        AuthorizationStatus = "CAPTURED";
    }

    public void UpdateCaptureStatus(string status)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        CaptureStatus = status;
    }

    public void AddRefund(Refund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        _refunds.Add(refund);
    }

    public Refund? FindRefundByIdempotencyKey(string idempotencyKey)
        => _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>Total already refunded (sum of refunds that are not failed/cancelled).</summary>
    public decimal TotalRefunded =>
        _refunds.Where(r => !string.Equals(r.Status, "FAILED", StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(r.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
                .Sum(r => r.Amount);

    /// <summary>Amount still available to refund against the capture.</summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - TotalRefunded;
}
