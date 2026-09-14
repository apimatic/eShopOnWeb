using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when a Maxio API call fails (non-success status or transport error).
/// Derives from <see cref="BillingProviderException"/> so the API layer can map upstream
/// failures without depending on Maxio-specific types.
/// </summary>
public class MaxioApiException : BillingProviderException
{
    public MaxioApiException(string message, int? upstreamStatusCode = null, IReadOnlyList<string>? errors = null)
        : base(message, upstreamStatusCode, errors)
    {
    }

    public MaxioApiException(string message, Exception innerException, int? upstreamStatusCode = null)
        : base(message, innerException, upstreamStatusCode)
    {
    }
}
