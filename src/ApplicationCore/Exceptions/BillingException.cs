using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure at the billing-provider boundary. <see cref="StatusCode"/> carries
/// the HTTP status the API should surface: provider 4xx rejections are carried
/// through unchanged; transport failures and unreadable provider responses are 5xx.
/// </summary>
public class BillingException : Exception
{
    public int StatusCode { get; }

    public BillingException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public BillingException(int statusCode, string message, Exception innerException) : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
