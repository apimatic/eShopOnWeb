using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A write-ahead claim that stops two callers from running the same money operation
/// for an order. The composite key is (OrderId, Operation).
/// </summary>
public class PaymentOperationClaim
{
    public int OrderId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
