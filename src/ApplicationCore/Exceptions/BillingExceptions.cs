using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class BillingValidationException : Exception
{
    public BillingValidationException(string message) : base(message) { }
}

public class BillingUnavailableException : Exception
{
    public BillingUnavailableException(string message) : base(message) { }
    public BillingUnavailableException(string message, Exception innerException) : base(message, innerException) { }
}

public class SubscriptionInProgressException : Exception
{
    public SubscriptionInProgressException(string message) : base(message) { }
}
