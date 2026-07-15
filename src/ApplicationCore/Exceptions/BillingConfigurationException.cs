using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A configured billing-provider handle (product/component) does not resolve, or resolves to the
/// wrong shape (e.g. a non-metered component). Per plan.md UC0/UC1/UC2 failure scenarios, this points
/// the operator back at the seed (UC0) rather than enrolling/recording usage against a guess.
/// </summary>
public class BillingConfigurationException : Exception
{
    public BillingConfigurationException(string message) : base(message)
    {
    }

    public BillingConfigurationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
