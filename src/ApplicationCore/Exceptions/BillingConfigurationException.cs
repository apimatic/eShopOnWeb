namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the billing integration is misconfigured (e.g. a required Maxio setting
/// such as ApiKey, Subdomain, or ProductFamilyHandle is missing).
/// </summary>
public class BillingConfigurationException : BillingException
{
    public BillingConfigurationException(string message) : base(message)
    {
    }
}
