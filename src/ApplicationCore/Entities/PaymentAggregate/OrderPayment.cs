using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment attached to an <see cref="OrderAggregate.Order"/>. It mirrors the state PayPal owns
/// (the hold, the capture and any refunds) so that a later request can act on the money that an
/// earlier request set in motion.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    public int OrderId { get; private set; }

    /// <summary>
    /// Random per-payment token carried on every reference sent to the processor, so an invoice id stays
    /// unique to this payment even when ids repeat in a freshly seeded database.
    /// </summary>
    public string Reference { get; private set; }
    public string BuyerId { get; private set; }
    public string Currency { get; private set; }

    /// <summary>The amount put on hold, which always equals the order total at the time of payment.</summary>
    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; } = PaymentStatus.AwaitingPayment;

    /// <summary>PayPal order (the hold) that this payment was created with.</summary>
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiration { get; private set; }

    /// <summary>Set when a stale hold was renewed; the id of the hold it replaced.</summary>
    public string? RenewedFromAuthorizationId { get; private set; }
    public int RenewalCount { get; private set; }

    /// <summary>How many holds this payment has asked the processor for, including declined attempts.</summary>
    public int AuthorizationAttempts { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? FeeAmount { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedDate { get; private set; }

    /// <summary>PayPal request id used for the capture so a retry replays instead of double-charging.</summary>
    public string? CaptureRequestId { get; private set; }

    /// <summary>The saved card this order was paid with, when not a one-off card.</summary>
    public int? PaymentMethodId { get; private set; }

    /// <summary>
    /// PayPal's vault id for the card used. Only stored when the card already lived at PayPal (a saved
    /// card); it is what lets a stale hold be renewed without the shopper being present.
    /// </summary>
    public string? CardVaultId { get; private set; }
    public string? PayPalCustomerId { get; private set; }

    public DateTimeOffset Created { get; private set; } = DateTimeOffset.Now;
    public DateTimeOffset Updated { get; private set; } = DateTimeOffset.Now;

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal RefundedAmount
    {
        get
        {
            var total = 0m;
            foreach (var refund in _refunds)
            {
                if (refund.Status == RefundStatus.Completed)
                {
                    total += refund.Amount;
                }
            }
            return total;
        }
    }

    /// <summary>
    /// Refunds already completed plus the ones still in flight. Money that has been asked back is not
    /// offered back out again, so a stuck refund can never let an order be refunded twice.
    /// </summary>
    public decimal RefundedOrPendingAmount
    {
        get
        {
            var total = 0m;
            foreach (var refund in _refunds)
            {
                if (refund.Status != RefundStatus.Failed)
                {
                    total += refund.Amount;
                }
            }
            return total;
        }
    }

    public decimal RefundableAmount => Math.Max(0m, (CapturedAmount ?? 0m) - RefundedOrPendingAmount);

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618 // Required by Entity Framework

    public OrderPayment(int orderId, string buyerId, string currency, decimal amount)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NotAllowed(amount <= 0m, "An order with a total of zero cannot be paid for.");

        OrderId = orderId;
        Reference = Guid.NewGuid().ToString("N")[..12];
        BuyerId = buyerId;
        Currency = currency;
        Amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Records the hold PayPal has put on the shopper's money.
    /// </summary>
    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? expiration, DateTimeOffset now)
    {
        Guard.Against.NotAllowed(Status != PaymentStatus.AwaitingPayment && Status != PaymentStatus.Declined,
            $"A payment that is {Status} cannot be authorized again.");
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiration = expiration;
        Status = PaymentStatus.Authorized;
        Updated = now;
    }

    /// <summary>
    /// Starts a new hold attempt and returns its number, so each hold this payment asks for carries
    /// its own references while a replay of the same attempt stays recognisable to the processor.
    /// </summary>
    public int BeginAuthorization(DateTimeOffset now)
    {
        AuthorizationAttempts++;
        Updated = now;
        return AuthorizationAttempts;
    }

    /// <summary>True when an invoice or custom id on a statement line was created by this payment.</summary>
    public bool Recognizes(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        return reference.StartsWith(
            string.Concat(PaymentReference.PREFIX, "-", Id.ToString(), "-", Reference), StringComparison.Ordinal);
    }

    /// <summary>True when an identifier is one of the processor ids this payment is tracking.</summary>
    public bool References(string? payPalId) => !string.IsNullOrWhiteSpace(payPalId) && ProcessorIds().Contains(payPalId);

    /// <summary>
    /// Every id the processor knows this payment by: the order the hold was made on, the hold (and the
    /// one it replaced), the capture and each refund. This is what a statement line is matched on.
    /// </summary>
    public IEnumerable<string> ProcessorIds()
    {
        var ids = new List<string>();
        void Add(string? id)
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                ids.Add(id);
            }
        }

        Add(PayPalOrderId);
        Add(AuthorizationId);
        Add(RenewedFromAuthorizationId);
        Add(CaptureId);
        foreach (var refund in _refunds)
        {
            Add(refund.PayPalRefundId);
        }

        return ids;
    }

    public void SetCardSource(int? paymentMethodId, string? cardVaultId, string? payPalCustomerId)
    {
        PaymentMethodId = paymentMethodId;
        CardVaultId = cardVaultId;
        PayPalCustomerId = payPalCustomerId;
    }

    /// <summary>
    /// Records that the processor refused the card, leaving the order payable again.
    /// </summary>
    public void MarkDeclined(DateTimeOffset now)
    {
        Guard.Against.NotAllowed(Status != PaymentStatus.AwaitingPayment,
            $"A payment that is {Status} cannot be declined.");
        Status = PaymentStatus.Declined;
        Updated = now;
    }

    /// <summary>
    /// Prices the payment again: either after the processor refused the card, or after an attempt that
    /// never reached it, so a later payment is asked for at the amount the order now comes to.
    /// </summary>
    public void PriceAgain(decimal amount, string currency, DateTimeOffset now)
    {
        Guard.Against.NotAllowed(Status is not (PaymentStatus.Declined or PaymentStatus.AwaitingPayment),
            $"A payment that is {Status} cannot be priced again.");
        Guard.Against.NotAllowed(amount <= 0m, "An order with a total of zero has nothing to pay for.");

        Amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        Currency = currency;
        Status = PaymentStatus.AwaitingPayment;
        Updated = now;
    }

    /// <summary>
    /// The order was called off while nothing was on hold, so there was no money to release.
    /// </summary>
    public void MarkCancelledWithoutHold(DateTimeOffset now)
    {
        Guard.Against.NotAllowed(Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded
            or PaymentStatus.FullyRefunded,
            "Money has already been taken for this order, so it cannot simply be cancelled.");

        Status = PaymentStatus.Cancelled;
        Updated = now;
    }

    /// <summary>
    /// Records that a hold which had gone stale was renewed, replacing the previous hold. The
    /// replacement may be a hold PayPal made on an order we already had (a reauthorize), in which case
    /// there is no new order id to record.
    /// </summary>
    public void MarkRenewed(string previousAuthorizationId, string? payPalOrderId, string authorizationId,
        string authorizationStatus, DateTimeOffset? expiration, DateTimeOffset now)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        RenewedFromAuthorizationId = previousAuthorizationId;
        RenewalCount++;
        PayPalOrderId = string.IsNullOrWhiteSpace(payPalOrderId) ? PayPalOrderId : payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiration = expiration;
        Status = PaymentStatus.Authorized;
        Updated = now;
    }

    /// <summary>
    /// Records that the money was actually taken, together with what PayPal reported for the capture.
    /// </summary>
    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount, decimal feeAmount,
        decimal netAmount, string captureRequestId, DateTimeOffset now)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        FeeAmount = feeAmount;
        NetAmount = netAmount;
        CaptureRequestId = captureRequestId;
        Status = PaymentStatus.Captured;
        CapturedDate = now;
        Updated = now;
    }

    /// <summary>
    /// Records that the held money was released back to the shopper without ever being taken.
    /// </summary>
    public void MarkVoided(DateTimeOffset now)
    {
        Status = PaymentStatus.Voided;
        AuthorizationStatus = "VOIDED";
        Updated = now;
    }

    /// <summary>
    /// Adds a refund of the captured amount. The money returned can never push the order past what
    /// was actually captured.
    /// </summary>
    public PaymentRefund AddRefund(string idempotencyKey, decimal amount, DateTimeOffset now)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NotAllowed(CaptureId is null, "A payment that has not been captured cannot be refunded.");
        Guard.Against.NotAllowed(amount <= 0m, "A refund must be for more than zero.");
        Guard.Against.NotAllowed(_refunds.Any(refund => refund.IdempotencyKey == idempotencyKey),
            "A refund for this payment has already been recorded under this idempotency key.");

        var refundable = RefundableAmount;
        Guard.Against.NotAllowed(amount > refundable,
            $"Only {refundable:0.00} {Currency} of this payment can still be refunded.");

        var refund = new PaymentRefund(amount, Currency, idempotencyKey, now);
        _refunds.Add(refund);
        Status = refundable - amount <= 0m ? PaymentStatus.FullyRefunded : PaymentStatus.PartiallyRefunded;
        Updated = now;
        return refund;
    }

    /// <summary>
    /// Settles a refund that the processor took back. The payment only counts as refunded once
    /// PayPal has confirmed it.
    /// </summary>
    public void CompleteRefund(PaymentRefund refund, string payPalRefundId, decimal? feeReturned,
        decimal? netAmount, DateTimeOffset now)
    {
        Guard.Against.NotAllowed(!_refunds.Contains(refund), "This refund does not belong to the payment.");
        refund.MarkCompleted(payPalRefundId, feeReturned, netAmount, now);
        Status = RefundableAmount <= 0m ? PaymentStatus.FullyRefunded : PaymentStatus.PartiallyRefunded;
        Updated = now;
    }

    /// <summary>
    /// The processor refused a refund, so the money it was asking for stays refundable.
    /// </summary>
    public void FailRefund(PaymentRefund refund, DateTimeOffset now)
    {
        Guard.Against.NotAllowed(!_refunds.Contains(refund), "This refund does not belong to the payment.");
        refund.MarkFailed();
        Status = RefundedAmount >= (CapturedAmount ?? 0m) && CapturedAmount.HasValue
            ? PaymentStatus.FullyRefunded
            : RefundedAmount > 0m ? PaymentStatus.PartiallyRefunded : PaymentStatus.Captured;
        Updated = now;
    }

    public PaymentRefund? FindRefund(string idempotencyKey)
    {
        foreach (var refund in _refunds)
        {
            if (refund.IdempotencyKey == idempotencyKey)
            {
                return refund;
            }
        }
        return null;
    }
}
