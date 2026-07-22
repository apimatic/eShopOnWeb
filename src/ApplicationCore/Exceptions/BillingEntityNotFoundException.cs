using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The billing provider has no entity with the requested identifier.</summary>
public class BillingEntityNotFoundException : BillingProviderException
{
    public BillingEntityNotFoundException(string message, string operation, int? providerStatusCode = null,
        IReadOnlyList<string>? providerErrors = null, Exception? innerException = null)
        : base(message, operation, providerStatusCode, providerErrors, innerException)
    {
    }
}
