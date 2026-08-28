using System;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

public sealed class InvalidContactNumberException : Exception
{
    public InvalidContactNumberException() : base("The provider does not consider this a usable destination.") { }
}

public sealed class NotificationConflictException : Exception
{
    public NotificationConflictException(string message) : base(message) { }
}
