using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

/// <summary>
/// An order the caller placed, together with its dispatch/cancel state and where each of its
/// notifications got to.
/// </summary>
public sealed record OrderDeliveryView(
    OrderDelivery Delivery,
    Order? Order,
    IReadOnlyList<OrderNotification> Notifications);
