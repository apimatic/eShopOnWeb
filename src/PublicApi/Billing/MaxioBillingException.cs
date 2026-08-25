using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Caller-safe failure of the billing provider integration. Carries the HTTP status the
/// caller should see: provider 4xx statuses are preserved (the caller can act on them);
/// transport failures and unprocessable provider responses surface as 5xx.
/// </summary>
public class MaxioBillingException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public MaxioBillingException(HttpStatusCode statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
