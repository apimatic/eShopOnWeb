using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed class BillingPlanNotFoundException : Exception
{
    public BillingPlanNotFoundException(string productHandle)
        : base($"Subscription plan '{productHandle}' is not available.") { }
}

public sealed class BillingProviderException : Exception
{
    public BillingProviderException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int? StatusCode { get; }
}
