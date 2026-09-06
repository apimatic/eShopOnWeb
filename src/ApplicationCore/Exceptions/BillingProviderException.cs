using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing provider was unreachable, timed out, refused our credentials, or answered with
/// something we could not interpret. These are upstream failures, not caller mistakes.
/// </summary>
public class BillingProviderException : BillingException
{
    public BillingProviderException(string message, int? statusCode = null) : base(message)
    {
        StatusCode = statusCode;
    }

    public BillingProviderException(string message, Exception innerException, int? statusCode = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>The HTTP status returned by the provider, when the failure came back as a response.</summary>
    public int? StatusCode { get; }
}
