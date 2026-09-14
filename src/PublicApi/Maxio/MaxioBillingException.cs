using Microsoft.AspNetCore.Http;
using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// A failure surfaced by the Maxio subscription integration. Carries the HTTP status the
/// caller should receive and a caller-safe message (never SDK/framework type names).
/// </summary>
public class MaxioBillingException : Exception
{
    public int StatusCode { get; }

    public MaxioBillingException(string message, int statusCode = StatusCodes.Status502BadGateway)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public MaxioBillingException(string message, int statusCode, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
