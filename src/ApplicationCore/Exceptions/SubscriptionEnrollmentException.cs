using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SubscriptionEnrollmentException : Exception
{
    public SubscriptionEnrollmentException(string message)
        : base(message)
    {
    }
}
