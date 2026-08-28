using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment : BaseEntity
{
    private readonly List<PaymentAuthorization> _authorizations = new();
    private readonly List<PaymentRefund> _refunds = new();

#pragma warning disable CS8618
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
            throw new ArgumentException("A three-letter currency is required.", nameof(currency));

        Currency = currency.ToUpperInvariant();
        CaptureRequestId = $"eshop-capture-{Guid.NewGuid():N}";
        VoidRequestId = $"eshop-void-{Guid.NewGuid():N}";
    }

    public int OrderId { get; private set; }
    public string Currency { get; private set; }
    public string CaptureRequestId { get; private set; }
    public string VoidRequestId { get; private set; }
    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public decimal RefundedAmount { get; private set; }
    public DateTimeOffset? LastActivityAt { get; private set; }
    public byte[]? RowVersion { get; private set; }
    public IReadOnlyCollection<PaymentAuthorization> Authorizations => _authorizations.AsReadOnly();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();
    public PaymentAuthorization? CurrentAuthorization => _authorizations.SingleOrDefault(x => x.IsCurrent);
    public decimal RefundableAmount => Math.Max(0m, (CapturedAmount ?? 0m) - RefundedAmount);

    public PaymentAuthorization BeginAuthorization(string sourceType, int? paymentMethodId)
    {
        foreach (var authorization in _authorizations) authorization.MakeHistorical();
        var attempt = new PaymentAuthorization(sourceType, paymentMethodId);
        _authorizations.Add(attempt);
        LastActivityAt = DateTimeOffset.UtcNow;
        return attempt;
    }

    public void RecordCapture(string id, string status, decimal amount, decimal? fee,
        decimal? netAmount, DateTimeOffset? capturedAt)
    {
        PayPalCaptureId = id;
        PayPalCaptureStatus = status;
        CapturedAmount = amount;
        PayPalFee = fee;
        NetAmount = netAmount;
        CapturedAt = capturedAt ?? DateTimeOffset.UtcNow;
        LastActivityAt = CapturedAt;
    }

    public void RecordVoid(string status)
    {
        var authorization = CurrentAuthorization ?? throw new InvalidOperationException("No current authorization exists.");
        authorization.RecordStatus(status);
        LastActivityAt = DateTimeOffset.UtcNow;
    }

    public PaymentRefund RecordRefund(string idempotencyKey, string paypalRefundId, string status,
        decimal amount, DateTimeOffset? createdAt)
    {
        if (_refunds.Any(x => x.IdempotencyKey == idempotencyKey))
            throw new InvalidOperationException("This refund idempotency key has already been used.");
        if (amount <= 0 || amount > RefundableAmount)
            throw new InvalidOperationException("Refund amount exceeds the captured amount still available to refund.");

        var refund = new PaymentRefund(idempotencyKey, paypalRefundId, status, amount, createdAt);
        _refunds.Add(refund);
        RefundedAmount += amount;
        LastActivityAt = refund.CreatedAt;
        return refund;
    }
}
