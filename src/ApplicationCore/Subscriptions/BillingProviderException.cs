using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public class BillingProviderException : Exception
{
    public BillingProviderException(string message) : base(message)
    {
    }

    public BillingProviderException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public sealed class BillingProviderValidationException : BillingProviderException
{
    public BillingProviderValidationException(string message) : base(message)
    {
    }
}
