using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class CatalogItemNotFoundException : Exception
{
    public CatalogItemNotFoundException(IReadOnlyCollection<int> catalogItemIds)
        : base($"Catalog item(s) not found: {string.Join(", ", catalogItemIds)}.")
    {
    }
}

public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId)
        : base($"Order {orderId} was not found.")
    {
    }
}

public class NotificationNotFoundException : Exception
{
    public NotificationNotFoundException(int notificationId)
        : base($"Notification {notificationId} was not found.")
    {
    }
}
