using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing system was unreachable or answered in a way we cannot act on. Distinct from
/// <see cref="BillingValidationException"/>: the caller did nothing wrong, so this surfaces as an
/// upstream failure rather than a bad request.
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

    /// <summary>HTTP status returned by the provider, when the call got that far.</summary>
    public int? StatusCode { get; }
}
