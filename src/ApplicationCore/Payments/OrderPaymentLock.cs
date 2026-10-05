using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// The per-order claim every payment write runs under. Taking it is an insert of the key
/// <c>order:{id}</c>; a concurrent second caller is refused by the store and gets HTTP 409.
/// It expires on its own, so a crashed process cannot block an order forever.
/// </summary>
public sealed class OrderPaymentLock : IAsyncDisposable
{
    public static readonly TimeSpan TimeToLive = TimeSpan.FromMinutes(2);

    private readonly IPaymentClaimStore _store;
    private readonly PaymentClaimLease _lease;

    private OrderPaymentLock(IPaymentClaimStore store, PaymentClaimLease lease)
    {
        _store = store;
        _lease = lease;
    }

    public static string KeyFor(int orderId) => $"order:{orderId}";

    public static async Task<OrderPaymentLock> AcquireAsync(IPaymentClaimStore store, int orderId, CancellationToken cancellationToken)
    {
        var lease = await store.TryAcquireAsync(KeyFor(orderId), TimeToLive, cancellationToken);
        if (lease is null)
            throw new PaymentConflictException(
                $"Another payment operation for order {orderId} is in progress. Retry shortly.", "OPERATION_IN_PROGRESS");
        return new OrderPaymentLock(store, lease);
    }

    public async ValueTask DisposeAsync() => await _store.ReleaseAsync(_lease, CancellationToken.None);
}
