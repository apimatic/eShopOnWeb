using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class Order : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private Order() {}
    #pragma warning restore CS8618

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }

    private readonly List<OrderItem> _orderItems = new List<OrderItem>();

    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

    public string Currency { get; private set; } = string.Empty;
    public string PaymentReference { get; private set; } = string.Empty;
    public PaymentStatus PaymentStatus { get; private set; } = PaymentStatus.AwaitingPayment;
    public string? FinixAuthorizationId { get; private set; }
    public string? FinixAuthorizationState { get; private set; }
    public string? FinixVoidState { get; private set; }
    public string? FinixTransferId { get; private set; }
    public string? FinixTransferState { get; private set; }
    public string? FinixPaymentInstrumentId { get; private set; }
    public int? SavedCardId { get; private set; }
    public long? CapturedAmountCents { get; private set; }
    public decimal? FinixFeeCents { get; private set; }
    public decimal? NetProceedsCents { get; private set; }
    public long RefundedAmountCents { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }
    public DateTimeOffset? CanceledAt { get; private set; }
    public string? LastPaymentError { get; private set; }

    private readonly List<OrderRefund> _refunds = new List<OrderRefund>();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    public void InitializePayment(string currency)
    {
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Currency = currency.ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(PaymentReference))
        {
            PaymentReference = Guid.NewGuid().ToString("N");
        }

        PaymentStatus = PaymentStatus.AwaitingPayment;
    }

    public void MarkAuthorized(string finixAuthorizationId, string finixAuthorizationState, string finixPaymentInstrumentId, int? savedCardId)
    {
        FinixAuthorizationId = finixAuthorizationId;
        FinixAuthorizationState = finixAuthorizationState;
        FinixPaymentInstrumentId = finixPaymentInstrumentId;
        SavedCardId = savedCardId;
        AuthorizedAt = DateTimeOffset.UtcNow;
        LastPaymentError = null;
        PaymentStatus = PaymentStatus.Authorized;
    }

    public void MarkAuthorizationFailed(string finixAuthorizationState, string? message)
    {
        FinixAuthorizationState = finixAuthorizationState;
        LastPaymentError = message;
        PaymentStatus = PaymentStatus.Failed;
    }

    public void MarkCaptured(string finixTransferId, string finixTransferState, long capturedAmountCents, decimal finixFeeCents, decimal netProceedsCents)
    {
        FinixTransferId = finixTransferId;
        FinixTransferState = finixTransferState;
        CapturedAmountCents = capturedAmountCents;
        FinixFeeCents = finixFeeCents;
        NetProceedsCents = netProceedsCents;
        FulfilledAt = DateTimeOffset.UtcNow;
        PaymentStatus = PaymentStatus.Captured;
    }

    public void UpdateTransferState(string finixTransferState)
    {
        if (!string.IsNullOrWhiteSpace(finixTransferState))
        {
            FinixTransferState = finixTransferState;
        }
    }

    public void MarkCanceled(string? finixAuthorizationState, string? finixVoidState, string? message)
    {
        if (!string.IsNullOrWhiteSpace(finixAuthorizationState))
        {
            FinixAuthorizationState = finixAuthorizationState;
        }

        FinixVoidState = finixVoidState;
        LastPaymentError = message;
        CanceledAt = DateTimeOffset.UtcNow;
        PaymentStatus = PaymentStatus.Canceled;
    }

    public long RefundableAmountCents()
    {
        if (CapturedAmountCents is null)
        {
            return 0;
        }

        return Math.Max(0, CapturedAmountCents.Value - RefundedAmountCents);
    }

    public void AddRefund(OrderRefund refu)
    {
        Guard.Against.Null(refu, nameof(refu));

        var captured = CapturedAmountCents ?? 0;
        if (refu.AmountCents > RefundableAmountCents())
        {
            throw new InvalidOperationException("Refund amount exceeds the capturable remaining balance.");
        }

        _refunds.Add(refu);
        RefundedAmountCents += refu.AmountCents;

        if (RefundedAmountCents >= captured)
        {
            PaymentStatus = PaymentStatus.Refunded;
        }
        else
        {
            PaymentStatus = PaymentStatus.PartiallyRefunded;
        }
    }
}
