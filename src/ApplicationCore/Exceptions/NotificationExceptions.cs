using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The requested order does not exist (→ 404).</summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId) : base($"Order {orderId} was not found.") { }
}

/// <summary>The requested notification does not exist (→ 404).</summary>
public class NotificationNotFoundException : Exception
{
    public NotificationNotFoundException(int notificationId) : base($"Notification {notificationId} was not found.") { }
}

/// <summary>The provider does not consider the supplied number a usable destination (→ 400).</summary>
public class ContactNumberNotUsableException : Exception
{
    public ContactNumberNotUsableException() : base("The supplied number is not a usable SMS destination.") { }
}

/// <summary>A caller-supplied value was invalid for the requested operation (→ 400).</summary>
public class NotificationValidationException : Exception
{
    public NotificationValidationException(string message) : base(message) { }
}
