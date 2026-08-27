using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

public class OrderSummary
{
    public Order Order { get; set; } = null!;
    public IReadOnlyList<OrderNotification> Notifications { get; set; } = new List<OrderNotification>();
}
