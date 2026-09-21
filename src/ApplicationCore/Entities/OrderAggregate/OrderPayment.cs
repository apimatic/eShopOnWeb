using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The PayPal payment attached to an <see cref="Order"/>. Part of the Order aggregate (owned).
/// Holds enough of the state PayPal owns — the ids and current status for the hold (authorization),
/// the capture, and each refund — that a later request can act on it, not only the one that started it.
/// </summary>
public class OrderPayment
{
    private readonly List<OrderRefund> _refunds = new();

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(
        string payPalOrderId,
        string authorizationId,
        string authorizationStatus,
        decimal authorizedAmount,
        string currency,
        DateTimeOffset? authorizationExpiresAt)
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
    }

    /// <summary>ISO-4217 currency the payment was taken in.</summary>
    public string Currency { get; private set; }

    // --- Hold (authorization) ---
    /// <summary>PayPal order (v2 checkout order) id that produced the authorization.</summary>
    public string PayPalOrderId { get; private set; }
    public string AuthorizationId { get; private set; }
    public string AuthorizationStatus { get; private set; }
    public decimal AuthorizedAmount { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // --- Capture ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    // --- Refunds ---
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Replaces the hold details after a re-authorization (the original expired before capture).</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, decimal authorizedAmount, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = authorizedAmount;
        AuthorizationExpiresAt = expiresAt;
    }

    public void UpdateAuthorizationStatus(string status) => AuthorizationStatus = status;

    /// <summary>Records the money actually taken at fulfilment, with PayPal's reported fee and net proceeds.</summary>
    public void RecordCapture(string captureId, string captureStatus, decimal capturedAmount, decimal? fee, decimal? net)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = fee;
        NetAmount = net;
        AuthorizationStatus = "CAPTURED";
    }

    /// <summary>Marks the hold as voided (cancel before fulfilment). No money ever moved.</summary>
    public void MarkVoided() => AuthorizationStatus = "VOIDED";

    /// <summary>Sum of refunds that are not failed/cancelled (i.e. money returned or in flight).</summary>
    public decimal TotalRefunded() =>
        _refunds.Where(r => !IsTerminatedRefund(r.Status)).Sum(r => r.Amount);

    /// <summary>The amount still available to refund against the capture.</summary>
    public decimal RefundableRemaining() => (CapturedAmount ?? 0m) - TotalRefunded();

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Adds a refund, enforcing that a partly-refunded order can never become refundable beyond what
    /// was captured. Throws <see cref="PaymentOperationException"/> if the cap would be exceeded.
    /// </summary>
    public OrderRefund AddRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        if (CaptureId is null)
        {
            throw new PaymentOperationException("Cannot refund an order that has not been captured.");
        }
        if (amount <= 0m)
        {
            throw new PaymentOperationException("Refund amount must be greater than zero.");
        }
        if (amount > RefundableRemaining())
        {
            throw new PaymentOperationException(
                $"Refund of {amount:0.00} {Currency} exceeds the remaining refundable amount of {RefundableRemaining():0.00} {Currency}.");
        }

        var refund = new OrderRefund(payPalRefundId, amount, status, idempotencyKey);
        _refunds.Add(refund);
        return refund;
    }

    private static bool IsTerminatedRefund(string status) =>
        string.Equals(status, "FAILED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "CANCELLED", StringComparison.OrdinalIgnoreCase);
}
