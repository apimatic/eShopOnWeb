using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing.Exceptions;

/// <summary>
/// The billing capability is not usable because it is mis-configured (missing credentials,
/// unusable base address, unknown product family). This is an operator problem, not a caller
/// problem, so it maps to a 503 rather than a 4xx.
/// </summary>
public class BillingConfigurationException : BillingException
{
    public BillingConfigurationException(string message) : base(message)
    {
    }

    public BillingConfigurationException(string message, Exception? innerException) : base(message, innerException)
    {
    }
}
