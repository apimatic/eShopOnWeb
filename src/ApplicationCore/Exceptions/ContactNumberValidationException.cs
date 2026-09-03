using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public sealed class ContactNumberValidationException : Exception
{
    public ContactNumberValidationException(string message) : base(message) { }
}

public sealed class NotificationOperationException : Exception
{
    public NotificationOperationException(string message) : base(message) { }
}
