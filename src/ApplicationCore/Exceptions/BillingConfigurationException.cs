using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The subscription-billing capability is not usable because its configuration is missing or invalid.
/// The rest of the application is unaffected, so this surfaces as a 503 on billing routes only.
/// </summary>
public class BillingConfigurationException : BillingException
{
    public BillingConfigurationException(string message) : base(message)
    {
    }

    public BillingConfigurationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
