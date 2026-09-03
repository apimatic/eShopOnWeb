using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The provider does not consider the supplied number a usable SMS destination.</summary>
public class InvalidContactNumberException : Exception
{
    public InvalidContactNumberException()
        : base("The supplied phone number is not a valid, reachable mobile destination.")
    {
    }
}

/// <summary>No order with the given id exists (in this host's store).</summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId) : base($"No order found with id {orderId}")
    {
    }
}

/// <summary>No notification with the given id exists.</summary>
public class NotificationNotFoundException : Exception
{
    public NotificationNotFoundException(int notificationId) : base($"No notification found with id {notificationId}")
    {
    }
}

/// <summary>An order lifecycle transition was requested that its current status does not allow.</summary>
public class InvalidOrderStateException : Exception
{
    public InvalidOrderStateException(string message) : base(message)
    {
    }
}
