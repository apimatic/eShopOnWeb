using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class BillingConfigurationException : BillingException
{
    public BillingConfigurationException(string message)
        : base(message, HttpStatusCode.ServiceUnavailable)
    {
    }
}
