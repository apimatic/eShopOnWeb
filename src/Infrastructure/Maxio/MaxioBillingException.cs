using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Represents a failed call to the Maxio Advanced Billing API.
/// </summary>
public class MaxioBillingException : Exception
{
    public MaxioBillingException(HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
