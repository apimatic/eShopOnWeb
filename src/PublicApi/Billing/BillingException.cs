using System;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Caller-safe failure at the billing boundary. Message is safe to put on the wire;
/// StatusCode carries the provider's HTTP status when one exists, otherwise a 5xx.
/// </summary>
public class BillingException : Exception
{
    public int StatusCode { get; }

    public BillingException(string message, int statusCode = StatusCodes.Status502BadGateway, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
