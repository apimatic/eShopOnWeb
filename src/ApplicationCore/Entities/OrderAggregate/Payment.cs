using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The PayPal-owned payment state for an order: the checkout order id, the authorization (hold),
/// the capture (money taken) and any refunds. This lives inside the Order aggregate and carries
/// enough of PayPal's state (ids + statuses) that a later request can act on the payment.
/// No card number is ever stored here.
/// </summary>
public class Payment : BaseEntity
{
    // --- Hold (authorization) ---
    public string PayPalOrderId { get; private set; }
    public string AuthorizationId { get; private set; }
    public string AuthorizationStatus { get; private set; }
    public decimal AuthorizedAmount { get; private set; }
    public string Currency { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    /// <summary>The saved card used to pay, if any (for audit/traceability; safe descriptor only).</summary>
    public int? PaymentMethodId { get; private set; }

    // --- Capture (money taken at fulfilment) ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    // --- Refunds ---
    private readonly List<Refund> _refunds = new();
    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(string payPalOrderId, string authorizationId, string authorizationStatus,
        decimal authorizedAmount, string currency, DateTimeOffset? authorizationExpiresAt, int? paymentMethodId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = authorizedAmount;
        Currency = currency;
        AuthorizationExpiresAt = authorizationExpiresAt;
        PaymentMethodId = paymentMethodId;
    }

    /// <summary>Update the hold after a reauthorization renews it.</summary>
    public void RenewAuthorization(string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    /// <summary>Record what PayPal reported when the authorization was captured.</summary>
    public void RecordCapture(string captureId, string status, decimal capturedAmount, decimal fee, decimal net)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = fee;
        NetAmount = net;
        CapturedAt = DateTimeOffset.UtcNow;
        AuthorizationStatus = "CAPTURED";
    }

    public void MarkVoided() => AuthorizationStatus = "VOIDED";

    public void AddRefund(Refund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        _refunds.Add(refund);
    }

    public bool IsCaptured => CaptureId is not null;

    /// <summary>Sum of refunds that did not fail.</summary>
    public decimal TotalRefunded =>
        _refunds.Where(r => !string.Equals(r.Status, "FAILED", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(r.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
                .Sum(r => r.Amount);

    /// <summary>How much of the captured amount can still be refunded.</summary>
    public decimal RemainingRefundable => (CapturedAmount ?? 0m) - TotalRefunded;

    /// <summary>Find an already-recorded refund with the given caller idempotency key, if any.</summary>
    public Refund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
}
