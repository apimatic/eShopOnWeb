using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the billing provider rejected a request or could not be reached. Carries the
/// provider's own error messages so they can be surfaced without leaking transport details.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string message, IEnumerable<string>? providerErrors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderErrors = providerErrors?.ToList() ?? new List<string>();
    }

    public IReadOnlyList<string> ProviderErrors { get; }
}
