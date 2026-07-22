using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the billing provider has no entity matching the identifier that was asked for.
/// </summary>
public class BillingProviderNotFoundException : BillingProviderException
{
    public BillingProviderNotFoundException(string operation, string message, Exception? innerException = null)
        : base(operation, message, 404, innerException)
    {
    }
}
