using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class BillingException : Exception
{
    public BillingException(string message) : base(message)
    {
    }

    public BillingException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public sealed class PlanNotFoundException : BillingException
{
    public PlanNotFoundException(string productHandle)
        : base($"Subscription plan '{productHandle}' was not found.")
    {
        ProductHandle = productHandle;
    }

    public string ProductHandle { get; }
}

public sealed class BillingConfigurationException : BillingException
{
    public BillingConfigurationException(string message) : base(message)
    {
    }
}
