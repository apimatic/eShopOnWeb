using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A billing record the caller named does not exist — for example an unknown plan handle.
/// </summary>
public class BillingNotFoundException : BillingException
{
    public BillingNotFoundException(string message, Exception? innerException = null, int? providerStatusCode = null)
        : base(message, innerException, providerStatusCode)
    {
    }
}
