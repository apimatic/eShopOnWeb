using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a configured billing-provider handle (product family, plan, or component) does not
/// resolve, or resolves to an entity of the wrong shape (e.g. a non-metered component). This points
/// back at the UC0 sandbox-seeding step rather than at a transient provider failure.
/// </summary>
public class BillingConfigurationException : Exception
{
    public BillingConfigurationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
