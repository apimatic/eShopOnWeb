namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing integration is not configured well enough to serve the request - for example a
/// missing API key or site subdomain. This is an operator error, never a caller error.
/// </summary>
public class BillingConfigurationException : BillingException
{
    public BillingConfigurationException(string message) : base(message)
    {
    }
}
