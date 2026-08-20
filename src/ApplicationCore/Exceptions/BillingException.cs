using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Billing-provider failure already translated for the HTTP boundary.
/// <see cref="StatusCode"/> is the status the API should return; <see cref="Exception.Message"/> is caller-safe.
/// </summary>
public class BillingException : Exception
{
    public BillingException(int statusCode, string message, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
