using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the Maxio Advanced Billing API rejects a request or fails.
/// </summary>
public class MaxioBillingProviderException : Exception
{
    public int StatusCode { get; }

    public IReadOnlyList<string> ProviderErrors { get; }

    /// <summary>
    /// True when Maxio rejected the request because a customer with the same reference already exists.
    /// </summary>
    public bool DuplicateCustomerReference { get; }

    public MaxioBillingProviderException(string message,
        int statusCode = 0,
        IReadOnlyList<string>? providerErrors = null,
        bool duplicateCustomerReference = false)
        : base(message)
    {
        StatusCode = statusCode;
        ProviderErrors = providerErrors ?? Array.Empty<string>();
        DuplicateCustomerReference = duplicateCustomerReference;
    }
}
