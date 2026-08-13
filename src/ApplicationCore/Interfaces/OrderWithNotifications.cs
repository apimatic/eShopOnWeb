using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>An order together with where each of its notifications got to.</summary>
public record OrderWithNotifications(Order Order, IReadOnlyList<Notification> Notifications);
