using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618
    private PaymentRefund() { }

    public PaymentRefund(Guid refundId, string idempotencyKey, decimal amount, string currency)
    {
        RefundId = refundId;
        IdempotencyKey = Guard.Against.NullOrEmpty(idempotencyKey);
        Amount = Guard.Against.NegativeOrZero(amount);
        Currency = Guard.Against.NullOrEmpty(currency);
    }

    public Guid RefundId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string? PayPalRefundId { get; private set; }
    public string Status { get; private set; } = "INITIATED";
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public decimal? PayPalFeeRefunded { get; private set; }
    public decimal? NetAmountDebited { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }

    public void RecordPayPalResult(string paypalRefundId, string status, decimal amount,
        decimal? feeRefunded, decimal? netAmountDebited)
    {
        PayPalRefundId = Guard.Against.NullOrEmpty(paypalRefundId);
        Status = Guard.Against.NullOrEmpty(status);
        Amount = amount;
        PayPalFeeRefunded = feeRefunded;
        NetAmountDebited = netAmountDebited;
        FailureCode = null;
        FailureMessage = null;
    }

    public void RecordFailure(string code, string message)
    {
        Status = "FAILED";
        FailureCode = code;
        FailureMessage = message;
    }
}
