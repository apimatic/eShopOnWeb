using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure at the billing-provider boundary. <see cref="Message"/> is caller-safe;
/// <see cref="StatusCode"/> is the HTTP status the API should surface (provider 4xx are
/// carried through; transport/unknown failures are 502).
/// </summary>
public class BillingException : Exception
{
    public int StatusCode { get; }

    public BillingException(string message, int statusCode = 502)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public BillingException(string message, Exception innerException, int statusCode = 502)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
