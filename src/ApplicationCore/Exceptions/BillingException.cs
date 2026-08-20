using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Domain error raised by the subscription billing flow and mapped to an HTTP status by PublicApi.
/// </summary>
public class BillingException : Exception
{
    public BillingException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
