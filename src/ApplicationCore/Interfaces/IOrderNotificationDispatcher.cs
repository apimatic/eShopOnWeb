using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationDispatcher
{
    Task NotifyAsync(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string body,
        DateTimeOffset? sendAt = null,
        CancellationToken cancellationToken = default);

    Task<OrderNotification> SendToContactAsync(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string body,
        ContactNumber destination,
        int? sourceNotificationId,
        CancellationToken cancellationToken = default);

    Task CancelScheduledFollowUpsAsync(int orderId, CancellationToken cancellationToken = default);

    Task RefreshFromProviderAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default);
}
