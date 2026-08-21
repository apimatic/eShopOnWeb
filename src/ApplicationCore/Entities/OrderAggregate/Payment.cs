using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The PayPal-owned state of a single order's money movement. It is part of the <see cref="Order"/>
/// aggregate. It never stores card details — only the ids and current status of the hold, the
/// capture and any refunds, plus the amounts PayPal reported at capture (fee and net proceeds).
/// </summary>
public class Payment : BaseEntity
{
    private readonly List<PaymentRefund> _refunds = new();

#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(string payPalOrderId, string authorizationId, string? authorizationStatus,
        decimal authorizedAmount, string currency)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = authorizedAmount;
        Currency = currency;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>PayPal order (checkout) id that produced the hold.</summary>
    public string PayPalOrderId { get; private set; }

    /// <summary>PayPal authorization id — the hold that can be captured, voided or reauthorized.</summary>
    public string AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public decimal AuthorizedAmount { get; private set; }
    public string Currency { get; private set; }

    /// <summary>PayPal capture id — present once the money has actually been taken at fulfilment.</summary>
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }

    /// <summary>PayPal's fee on the capture, as PayPal reported it.</summary>
    public decimal? PayPalFee { get; private set; }

    /// <summary>Net proceeds to the merchant, as PayPal reported it.</summary>
    public decimal? NetAmount { get; private set; }

    public PaymentStatus Status { get; private set; }

    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Replace the hold with a renewed authorization (used when a stale hold is reauthorized).</summary>
    public void RenewAuthorization(string authorizationId, string? status)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Record what PayPal reported when the authorized funds were captured.</summary>
    public void RecordCapture(string captureId, string? status, decimal capturedAmount, decimal? fee, decimal? net)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = fee;
        NetAmount = net;
        Status = PaymentStatus.Captured;
    }

    public void MarkVoided()
    {
        Status = PaymentStatus.Voided;
        AuthorizationStatus = "VOIDED";
    }

    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    public decimal RemainingCapturedAmount() => (CapturedAmount ?? 0m) - TotalRefunded();

    public PaymentRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>Add a refund PayPal made and advance the payment status accordingly.</summary>
    public void AddRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        _refunds.Add(refund);
        Status = TotalRefunded() >= (CapturedAmount ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
    }
}
