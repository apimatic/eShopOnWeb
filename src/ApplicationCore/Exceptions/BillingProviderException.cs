using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing system could not be reached, or answered with something this integration cannot
/// interpret. The request may or may not have been applied upstream; callers should retry the same
/// idempotent operation rather than assume either outcome.
/// </summary>
public class BillingProviderException : BillingException
{
    public BillingProviderException(string message, HttpStatusCode? statusCode = null, string? providerRequestId = null)
        : base(message)
    {
        StatusCode = statusCode;
        ProviderRequestId = providerRequestId;
    }

    public BillingProviderException(string message, Exception innerException, HttpStatusCode? statusCode = null, string? providerRequestId = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ProviderRequestId = providerRequestId;
    }

    /// <summary>Status code the billing system returned, when the call completed at all.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>The billing system request identifier, for correlating with provider-side support.</summary>
    public string? ProviderRequestId { get; }
}
