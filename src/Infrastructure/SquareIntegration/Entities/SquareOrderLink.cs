using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration.Entities;

/// <summary>
/// The Square order created for an eShop order. Saved together with the eShop order, it is the claim that
/// guarantees one Square order per eShop order, and it carries the idempotency key used to create it.
/// The gift message is deliberately not stored here: it lives only on the Square order.
/// </summary>
public class SquareOrderLink
{
    public int OrderId { get; set; }
    public Order? Order { get; set; }

    public string MerchantId { get; set; } = string.Empty;
    public string LocationId { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string State { get; set; } = SquareOrderLinkState.Pending;
    public string? SquareOrderId { get; set; }
    public string? LastErrorCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public static class SquareOrderLinkState
{
    /// <summary>CreateOrder not yet confirmed (not sent, or its outcome is unknown).</summary>
    public const string Pending = "Pending";
    public const string Created = "Created";
}
