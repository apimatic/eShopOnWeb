using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the billing provider could not be reached at all (connection failure, timeout,
/// cancellation of the outbound call). Distinct from <see cref="BillingProviderException"/>, which
/// means the provider answered and refused.
/// </summary>
public class BillingUnavailableException : BillingProviderException
{
    public BillingUnavailableException(string operation, Exception innerException)
        : base(operation, "the billing provider could not be reached", null, innerException)
    {
    }
}
