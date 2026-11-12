using System;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// The app can't carry out a billing request. Mapped by the exception middleware to the given 4xx/5xx status,
/// with <see cref="Exception.Message"/> as the reason.
/// </summary>
public class BillingException : Exception
{
    public int StatusCode { get; }

    public BillingException(int statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
