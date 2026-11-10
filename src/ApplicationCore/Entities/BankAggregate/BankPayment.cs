using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

/// <summary>The Plaid transfer that paid for an order.</summary>
public class BankPayment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private BankPayment() { }
#pragma warning restore CS8618

    public BankPayment(int orderId, string userName, string transferId, string speed, decimal total, decimal fee, DateTime arrivesOn)
    {
        OrderId = orderId;
        UserName = userName;
        TransferId = transferId;
        Speed = speed;
        Total = total;
        Fee = fee;
        ArrivesOn = arrivesOn;
    }

    public int OrderId { get; private set; }
    public string UserName { get; private set; }
    public string TransferId { get; private set; }
    public string Speed { get; private set; }

    /// <summary>The order total (items and shipping), without the fee.</summary>
    public decimal Total { get; private set; }
    public decimal Fee { get; private set; }
    public decimal Charged => Total + Fee;
    public DateTime ArrivesOn { get; private set; }
    public DateTimeOffset CreatedOn { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RefundedOn { get; private set; }

    public void MarkRefunded() => RefundedOn = DateTimeOffset.UtcNow;
}
