using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing provider understood the request and refused it — a validation failure rather than an
/// outage. Nothing was applied, so the caller may correct the input and try again.
/// </summary>
public class BillingRequestRejectedException : BillingProviderException
{
    public BillingRequestRejectedException(string message, string operation, int? providerStatusCode = null,
        IReadOnlyList<string>? providerErrors = null, Exception? innerException = null)
        : base(message, operation, providerStatusCode, providerErrors, innerException)
    {
    }
}
