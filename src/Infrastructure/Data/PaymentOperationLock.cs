using System;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// A claim on an order's payment operations. The primary key on <see cref="OrderId"/> is what refuses a
/// second concurrent claim.
/// </summary>
public class PaymentOperationLock
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentOperationLock() {}

    public PaymentOperationLock(int orderId, Guid holder, DateTimeOffset acquiredAt)
    {
        OrderId = orderId;
        Holder = holder;
        AcquiredAt = acquiredAt;
    }

    public int OrderId { get; private set; }
    public Guid Holder { get; private set; }
    public DateTimeOffset AcquiredAt { get; private set; }
}
