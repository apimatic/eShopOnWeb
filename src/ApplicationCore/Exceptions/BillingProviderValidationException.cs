using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the billing provider rejects a request as invalid. <see cref="Errors"/> carries
/// the provider's own messages so they can be surfaced to the customer verbatim.
/// </summary>
public class BillingProviderValidationException : BillingProviderException
{
    public BillingProviderValidationException(
        string operation,
        string message,
        IReadOnlyList<string>? errors = null,
        int? statusCode = 422,
        Exception? innerException = null)
        : base(operation, message, statusCode, innerException)
    {
        Errors = errors ?? Array.Empty<string>();
    }

    /// <summary>The provider's validation messages; empty when the provider supplied none.</summary>
    public IReadOnlyList<string> Errors { get; }
}
