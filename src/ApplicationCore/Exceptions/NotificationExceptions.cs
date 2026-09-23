using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The number the provider does not consider a usable destination — rejected at registration time.</summary>
public class InvalidPhoneNumberException : Exception
{
    public InvalidPhoneNumberException(string message) : base(message) { }
}

/// <summary>No order with the given id is visible to the caller (missing, or owned by another shopper).</summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId) : base($"No order found with id {orderId}") { }
}

/// <summary>No notification with the given id exists.</summary>
public class NotificationNotFoundException : Exception
{
    public NotificationNotFoundException(int notificationId) : base($"No notification found with id {notificationId}") { }
}
