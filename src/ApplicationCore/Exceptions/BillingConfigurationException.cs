using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when subscription billing is switched on but not configured well enough to be used.
/// </summary>
/// <remarks>
/// This is deliberately distinct from <see cref="BillingProviderException"/>: it is an operator
/// problem, not a shopper problem, and the API surfaces it as 503 Service Unavailable so that the
/// rest of eShopOnWeb keeps serving traffic.
/// </remarks>
public class BillingConfigurationException : Exception
{
    public BillingConfigurationException(string message) : base(message)
    {
    }
}
