using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a subscription, plan or component the caller referenced does not exist at the provider.
/// </summary>
public class BillingEntityNotFoundException : BillingProviderException
{
    public BillingEntityNotFoundException(string message, IEnumerable<string>? providerErrors = null)
        : base(message, 404, providerErrors)
    {
    }
}
